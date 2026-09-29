using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Operon.Ai.Services;

namespace Operon.Workflows.Onboarding;

/// <summary>
/// Example onboarding workflow showing how to use the model adapter layer.
/// This demonstrates the pattern for all workflows in Operon.
/// </summary>
public interface IOnboardingWorkflow
{
    Task<OnboardingResult> ExtractClientProfileAsync(
        string clientId,
        SourceMaterials materials,
        PlaybookVersion playbook,
        CancellationToken cancellationToken = default);
}

public class OnboardingWorkflow : IOnboardingWorkflow
{
    private readonly IWorkflowExecutionService _executionService;
    private readonly ILogger<OnboardingWorkflow> _logger;

    public OnboardingWorkflow(
        IWorkflowExecutionService executionService,
        ILogger<OnboardingWorkflow> logger)
    {
        _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OnboardingResult> ExtractClientProfileAsync(
        string clientId,
        SourceMaterials materials,
        PlaybookVersion playbook,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting onboarding extraction for client {ClientId} using playbook {PlaybookVersion}",
            clientId, playbook.Version);

        // Build the workflow context
        var context = BuildWorkflowContext(materials, playbook);

        // Create the workflow job request
        var jobRequest = new WorkflowJobRequest
        {
            JobKey = $"onboarding-extract-{clientId}-{playbook.Version}",
            WorkflowType = "onboarding-extract",
            ClientId = clientId,
            Context = context,
            BudgetLimit = 150, // $1.50 max
            MaxRetries = 3,
            TimeoutSeconds = 300
        };

        // Execute the workflow through the execution service
        // This provides:
        // - Automatic deduplication (same JobKey won't run twice)
        // - Retry logic (transient failures retried up to 3 times)
        // - Budget enforcement (stops if cost exceeds $1.50)
        // - Structured output validation (JSON schema validation)
        // - Audit trail (logged in database)
        var result = await _executionService.ExecuteAsync(jobRequest, cancellationToken);

        if (result.Status != WorkflowExecutionResult.ExecutionStatus.Succeeded)
        {
            _logger.LogError(
                "Workflow execution failed: {Status}, Error: {Error}",
                result.Status, result.ErrorMessage);

            return new OnboardingResult
            {
                Success = false,
                ErrorMessage = result.ErrorMessage,
                FailureReason = result.Status.ToString()
            };
        }

        // Parse the model response into a typed profile
        try
        {
            var profile = JsonSerializer.Deserialize<ClientProfile>(
                result.DraftOutput,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogInformation(
                "Onboarding extraction succeeded for {ClientId}. " +
                "Tokens: {Tokens}, Cost: ${Cost}, Provider: {Provider}",
                clientId, result.TokensUsed, result.TotalCost / 100m, result.ProviderUsed);

            return new OnboardingResult
            {
                Success = true,
                Profile = profile,
                ExecutionMetrics = new ExecutionMetrics
                {
                    TokensUsed = result.TokensUsed,
                    CostCents = result.TotalCost,
                    ProviderUsed = result.ProviderUsed,
                    RequestId = result.RequestId
                }
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Failed to parse model response for client {ClientId}",
                clientId);

            return new OnboardingResult
            {
                Success = false,
                ErrorMessage = "Invalid response format from model",
                FailureReason = "JsonParseError"
            };
        }
    }

    private WorkflowContext BuildWorkflowContext(
        SourceMaterials materials,
        PlaybookVersion playbook)
    {
        // Convert source materials to context documents
        var contextDocs = new List<ContextDocument>();

        if (!string.IsNullOrEmpty(materials.SalesTranscript))
        {
            contextDocs.Add(new ContextDocument
            {
                Id = $"sales-transcript-{Guid.NewGuid()}",
                Title = "Sales Transcript",
                Content = materials.SalesTranscript,
                DocumentType = "transcript"
            });
        }

        if (!string.IsNullOrEmpty(materials.IntakeForm))
        {
            contextDocs.Add(new ContextDocument
            {
                Id = $"intake-form-{Guid.NewGuid()}",
                Title = "Client Intake Form",
                Content = materials.IntakeForm,
                DocumentType = "form"
            });
        }

        // Build business rules from playbook
        var businessRules = new Dictionary<string, string>
        {
            ["max_profile_fields"] = "50",
            ["require_budget_confirmation"] = "true",
            ["service_categories"] = playbook.ServiceCategories,
            ["qualification_rules"] = playbook.QualificationRules
        };

        // Define expected output schema (JSON Schema)
        var outputSchema = @"
        {
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"" },
                ""budget"": { ""type"": ""number"" },
                ""services_requested"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } },
                ""missing_fields"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } },
                ""conflicts"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
            },
            ""required"": [""name"", ""services_requested"", ""missing_fields""]
        }";

        return new WorkflowContext
        {
            ClientProfileVersion = "initial-draft",
            PlaybookVersion = "fa-systems-v2",
            SourceDocuments = contextDocs,
            BusinessRules = businessRules,
            OutputSchema = outputSchema
        };
    }
}

// Domain models

public class OnboardingResult
{
    public bool Success { get; set; }
    public ClientProfile? Profile { get; set; }
    public ExecutionMetrics? ExecutionMetrics { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FailureReason { get; set; }
}

public class ClientProfile
{
    public string Name { get; set; }
    public decimal? Budget { get; set; }
    public List<string> ServicesRequested { get; set; } = new();
    public List<string> MissingFields { get; set; } = new();
    public List<string> Conflicts { get; set; } = new();
}

public class ExecutionMetrics
{
    public int TokensUsed { get; set; }
    public long CostCents { get; set; }
    public string ProviderUsed { get; set; }
    public string RequestId { get; set; }
}

public class SourceMaterials
{
    public string? SalesTranscript { get; set; }
    public string? IntakeForm { get; set; }
}

public class PlaybookVersion
{
    public string Version { get; set; }
    public string ServiceCategories { get; set; }
    public string QualificationRules { get; set; }
}
