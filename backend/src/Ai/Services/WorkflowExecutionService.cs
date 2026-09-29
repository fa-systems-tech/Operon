using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Operon.Ai.Core;

namespace Operon.Ai.Services;

/// <summary>
/// Service for executing bounded AI workflows with job tracking, budgets, and retries.
/// </summary>
public interface IWorkflowExecutionService
{
    /// <summary>
    /// Execute a workflow with automatic retry logic and budget enforcement.
    /// </summary>
    Task<WorkflowExecutionResult> ExecuteAsync(
        WorkflowJobRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request contract for workflow execution.
/// </summary>
public class WorkflowJobRequest
{
    /// <summary>
    /// Unique job identifier for deduplication and tracking.
    /// </summary>
    public required string JobKey { get; set; }

    /// <summary>
    /// Workflow type (e.g., "onboarding", "launch-prep", "communication").
    /// </summary>
    public required string WorkflowType { get; set; }

    /// <summary>
    /// Client ID for access control and context filtering.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Versioned context for this workflow (includes approvals, playbooks, etc.).
    /// </summary>
    public required WorkflowContext Context { get; set; }

    /// <summary>
    /// Maximum budget for this job.
    /// </summary>
    public int BudgetLimit { get; set; } = 100; // cents

    /// <summary>
    /// Maximum retry attempts for transient failures.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// Workflow execution result.
/// </summary>
public class WorkflowExecutionResult
{
    public enum ExecutionStatus { Succeeded, Failed, Timeout, BudgetExceeded }

    /// <summary>
    /// Final status of the workflow execution.
    /// </summary>
    public ExecutionStatus Status { get; set; }

    /// <summary>
    /// Draft output from the workflow (if succeeded).
    /// </summary>
    public string? DraftOutput { get; set; }

    /// <summary>
    /// Validation errors (if output failed schema validation).
    /// </summary>
    public List<string> ValidationErrors { get; set; } = new();

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Total cost of the workflow in cents.
    /// </summary>
    public decimal TotalCost { get; set; }

    /// <summary>
    /// Total tokens used.
    /// </summary>
    public int TokensUsed { get; set; }

    /// <summary>
    /// Unique request ID for audit trail.
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// Provider used for this execution.
    /// </summary>
    public string? ProviderUsed { get; set; }

    /// <summary>
    /// Timestamp when execution completed.
    /// </summary>
    public DateTime CompletedAt { get; set; }
}

/// <summary>
/// Context data for a workflow execution.
/// </summary>
public class WorkflowContext
{
    /// <summary>
    /// Approved client profile version.
    /// </summary>
    public required string ClientProfileVersion { get; set; }

    /// <summary>
    /// Approved playbook version.
    /// </summary>
    public required string PlaybookVersion { get; set; }

    /// <summary>
    /// Source documents linked to this workflow (anonymized/redacted).
    /// </summary>
    public List<ContextDocument> SourceDocuments { get; set; } = new();

    /// <summary>
    /// Client-specific business rules and constraints.
    /// </summary>
    public Dictionary<string, string> BusinessRules { get; set; } = new();

    /// <summary>
    /// Expected output schema (JSON Schema string).
    /// </summary>
    public string? OutputSchema { get; set; }
}

/// <summary>
/// Context document reference for workflows.
/// </summary>
public class ContextDocument
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public string? DocumentType { get; set; }
}

/// <summary>
/// Implementation of workflow execution service.
/// </summary>
public class WorkflowExecutionService : IWorkflowExecutionService
{
    private readonly IModelProviderFactory _providerFactory;
    private readonly ILogger<WorkflowExecutionService> _logger;

    public WorkflowExecutionService(
        IModelProviderFactory providerFactory,
        ILogger<WorkflowExecutionService> logger)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WorkflowExecutionResult> ExecuteAsync(
        WorkflowJobRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting workflow execution: {WorkflowType}, Job: {JobKey}, Client: {ClientId}",
            request.WorkflowType, request.JobKey, request.ClientId);

        var result = new WorkflowExecutionResult { CompletedAt = DateTime.UtcNow };

        try
        {
            // Get the model provider
            var provider = _providerFactory.GetDefaultProvider();
            result.ProviderUsed = provider.ProviderName;

            // Check provider health
            var isHealthy = await provider.IsHealthyAsync(cancellationToken);
            if (!isHealthy)
            {
                result.Status = WorkflowExecutionResult.ExecutionStatus.Failed;
                result.ErrorMessage = $"Provider {provider.ProviderName} is not healthy";
                _logger.LogError("Provider health check failed: {Provider}", provider.ProviderName);
                return result;
            }

            // Build the model request
            var modelRequest = BuildModelRequest(request);

            // Estimate cost
            var estimatedCost = provider.EstimateCost(modelRequest);
            if (estimatedCost > request.BudgetLimit / 100m)
            {
                result.Status = WorkflowExecutionResult.ExecutionStatus.BudgetExceeded;
                result.ErrorMessage = $"Estimated cost {estimatedCost:C} exceeds budget";
                result.TotalCost = estimatedCost * 100; // convert to cents
                _logger.LogWarning("Budget exceeded for job {JobKey}", request.JobKey);
                return result;
            }

            // Execute with retries
            ModelResponse modelResponse = null;
            int attemptCount = 0;

            while (attemptCount < request.MaxRetries)
            {
                try
                {
                    _logger.LogInformation(
                        "Executing workflow attempt {Attempt}/{MaxRetries}",
                        attemptCount + 1, request.MaxRetries);

                    if (request.Context.OutputSchema != null)
                    {
                        modelResponse = await provider.CompleteStructuredAsync(modelRequest, cancellationToken);
                    }
                    else
                    {
                        modelResponse = await provider.CompleteAsync(modelRequest, cancellationToken);
                    }

                    // Validate structured output if schema provided
                    if (request.Context.OutputSchema != null && !modelResponse.IsValidStructuredOutput)
                    {
                        result.ValidationErrors.Add("Output does not match expected JSON schema");
                        attemptCount++;
                        if (attemptCount < request.MaxRetries)
                        {
                            _logger.LogWarning("Structured output validation failed, retrying...");
                            continue;
                        }
                    }

                    // Success
                    break;
                }
                catch (OperationCanceledException) when (attemptCount < request.MaxRetries - 1)
                {
                    _logger.LogWarning("Request timeout, retrying... (attempt {Attempt})", attemptCount + 1);
                    attemptCount++;
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }

            if (modelResponse == null)
            {
                result.Status = WorkflowExecutionResult.ExecutionStatus.Timeout;
                result.ErrorMessage = $"Request timed out after {request.MaxRetries} attempts";
                return result;
            }

            result.DraftOutput = modelResponse.Content;
            result.TokensUsed = modelResponse.TokensUsed;
            result.TotalCost = (long)(modelResponse.EstimatedCost * 100); // convert to cents
            result.RequestId = modelResponse.RequestId;
            result.Status = WorkflowExecutionResult.ExecutionStatus.Succeeded;

            _logger.LogInformation(
                "Workflow execution succeeded: {JobKey}, Cost: {Cost:C}, Tokens: {Tokens}",
                request.JobKey, result.TotalCost / 100m, result.TokensUsed);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution failed: {JobKey}", request.JobKey);
            result.Status = WorkflowExecutionResult.ExecutionStatus.Failed;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    private ModelRequest BuildModelRequest(WorkflowJobRequest request)
    {
        var contextBuilder = new StringBuilder();

        contextBuilder.AppendLine("=== CLIENT PROFILE ===");
        contextBuilder.AppendLine($"Version: {request.Context.ClientProfileVersion}");
        contextBuilder.AppendLine();

        contextBuilder.AppendLine("=== PLAYBOOK ===");
        contextBuilder.AppendLine($"Version: {request.Context.PlaybookVersion}");
        contextBuilder.AppendLine();

        contextBuilder.AppendLine("=== BUSINESS RULES ===");
        foreach (var rule in request.Context.BusinessRules)
        {
            contextBuilder.AppendLine($"{rule.Key}: {rule.Value}");
        }
        contextBuilder.AppendLine();

        contextBuilder.AppendLine("=== SOURCE DOCUMENTS ===");
        foreach (var doc in request.Context.SourceDocuments)
        {
            contextBuilder.AppendLine($"[{doc.DocumentType}] {doc.Title}");
            contextBuilder.AppendLine(doc.Content);
            contextBuilder.AppendLine();
        }

        return new ModelRequest
        {
            Prompt = $"Execute workflow: {request.WorkflowType}",
            Context = contextBuilder.ToString(),
            OutputSchema = request.Context.OutputSchema,
            BudgetLimit = request.BudgetLimit,
            TimeoutSeconds = request.TimeoutSeconds
        };
    }
}
