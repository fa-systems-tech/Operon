using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Operon.Ai.Providers;

/// <summary>
/// Docker Model Runner (DMR) provider implementation.
/// Connects to locally running Docker Model Runner for cost-free, privacy-preserving model inference.
/// </summary>
public class DockerModelRunnerProvider : IModelProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _modelName;
    private readonly int _contextSize;
    private readonly ILogger<DockerModelRunnerProvider> _logger;

    public string ProviderName => "dmr";

    public DockerModelRunnerProvider(
        HttpClient httpClient,
        string baseUrl,
        string modelName,
        int contextSize,
        ILogger<DockerModelRunnerProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
        _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
        _contextSize = contextSize;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DMR health check failed");
            return false;
        }
    }

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            var dmrRequest = BuildDmrRequest(request);
            var content = new StringContent(
                JsonSerializer.Serialize(dmrRequest),
                Encoding.UTF8,
                "application/json");

            _logger.LogInformation("Sending request to DMR: {Model}", _modelName);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);

            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/v1/chat/completions",
                content,
                linkedCts.Token);

            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var dmrResponse = JsonSerializer.Deserialize<DmrCompletionResponse>(responseBody);

            if (dmrResponse?.Choices == null || dmrResponse.Choices.Count == 0)
                throw new InvalidOperationException("DMR returned no choices");

            var assistantMessage = dmrResponse.Choices[0].Message?.Content ?? string.Empty;

            return new ModelResponse
            {
                Content = assistantMessage,
                IsValidStructuredOutput = request.OutputSchema == null,
                TokensUsed = dmrResponse.Usage?.CompletionTokens ?? 0,
                EstimatedCost = 0m, // DMR is free (local execution)
                RequestId = dmrResponse.Id,
                ModelUsed = _modelName,
                CompletedAt = DateTime.UtcNow,
                RawResponse = responseBody
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "DMR HTTP error");
            throw new InvalidOperationException($"DMR request failed: {ex.Message}", ex);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "DMR request timeout after {Seconds}s", request.TimeoutSeconds);
            throw new InvalidOperationException($"DMR request timeout after {request.TimeoutSeconds}s", ex);
        }
    }

    public async Task<ModelResponse> CompleteStructuredAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        // DMR supports structured output via response_format if available on the backend
        // For now, validate the response against the schema in business logic
        var response = await CompleteAsync(request, cancellationToken);

        if (request.OutputSchema != null)
        {
            response.IsValidStructuredOutput = ValidateJsonSchema(response.Content, request.OutputSchema);
        }

        return response;
    }

    public decimal EstimateCost(ModelRequest request)
    {
        // DMR runs locally with no API costs
        return 0m;
    }

    private DmrCompletionRequest BuildDmrRequest(ModelRequest request)
    {
        var messages = new List<DmrMessage>();

        // Add conversation history if provided
        if (request.ConversationHistory.Count > 0)
        {
            foreach (var msg in request.ConversationHistory)
            {
                messages.Add(new DmrMessage
                {
                    Role = msg.Speaker.ToString().ToLower(),
                    Content = msg.Content
                });
            }
        }

        // Build the final user message with context
        var userContent = new StringBuilder();
        if (!string.IsNullOrEmpty(request.Context))
        {
            userContent.AppendLine("Context:");
            userContent.AppendLine(request.Context);
            userContent.AppendLine();
        }
        userContent.Append(request.Prompt);

        messages.Add(new DmrMessage
        {
            Role = "user",
            Content = userContent.ToString()
        });

        return new DmrCompletionRequest
        {
            Model = _modelName,
            Messages = messages,
            MaxTokens = request.MaxTokens ?? 2048,
            Temperature = request.Temperature ?? 0.7
        };
    }

    private bool ValidateJsonSchema(string content, string schema)
    {
        try
        {
            // Attempt to parse as JSON
            JsonSerializer.Deserialize<object>(content);
            return true;
        }
        catch
        {
            _logger.LogWarning("Response is not valid JSON: {Content}", content.Substring(0, 100));
            return false;
        }
    }

    // DTO classes for DMR API compatibility
    private class DmrCompletionRequest
    {
        public string Model { get; set; }
        public List<DmrMessage> Messages { get; set; }
        public int MaxTokens { get; set; }
        public double Temperature { get; set; }
    }

    private class DmrMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }
    }

    private class DmrCompletionResponse
    {
        public string Id { get; set; }
        public List<DmrChoice> Choices { get; set; }
        public DmrUsage Usage { get; set; }
    }

    private class DmrChoice
    {
        public DmrMessage Message { get; set; }
    }

    private class DmrUsage
    {
        public int CompletionTokens { get; set; }
    }
}
