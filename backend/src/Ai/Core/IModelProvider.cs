using System.Threading.Tasks;

namespace Operon.Ai.Core;

/// <summary>
/// Structured request contract for AI model calls.
/// Enables interchangeable provider implementations without changing business logic.
/// </summary>
public class ModelRequest
{
    /// <summary>
    /// The prompt or user message to send to the model.
    /// </summary>
    public required string Prompt { get; set; }

    /// <summary>
    /// Optional conversation history for multi-turn interactions.
    /// </summary>
    public List<ConversationMessage> ConversationHistory { get; set; } = new();

    /// <summary>
    /// Optional context or source information to include with the request.
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    /// Maximum tokens in the response. Provider defaults apply if null.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Temperature for response randomness (0.0 = deterministic, 1.0+ = more creative).
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// Optional schema for structured output validation.
    /// </summary>
    public string? OutputSchema { get; set; }

    /// <summary>
    /// Budget limit for this request (tokens, cost, or provider-specific units).
    /// </summary>
    public int? BudgetLimit { get; set; }

    /// <summary>
    /// Timeout in seconds for the model call.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// Structured response contract for AI model calls.
/// </summary>
public class ModelResponse
{
    /// <summary>
    /// The model's response text.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Indicates whether the response matches the requested output schema.
    /// </summary>
    public bool IsValidStructuredOutput { get; set; }

    /// <summary>
    /// Number of tokens used by the model (estimate if exact count unavailable).
    /// </summary>
    public int TokensUsed { get; set; }

    /// <summary>
    /// Estimated cost of this request in the provider's units.
    /// </summary>
    public decimal EstimatedCost { get; set; }

    /// <summary>
    /// Unique identifier for tracking this request.
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// Provider-specific model identifier used.
    /// </summary>
    public required string ModelUsed { get; set; }

    /// <summary>
    /// Timestamp when the request was completed.
    /// </summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>
    /// Raw provider response for debugging and audit purposes.
    /// </summary>
    public string? RawResponse { get; set; }
}

/// <summary>
/// Represents a single turn in a conversation.
/// </summary>
public class ConversationMessage
{
    public enum Role { User, Assistant }

    public required Role Speaker { get; set; }
    public required string Content { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Abstract interface for AI model providers.
/// Enables switching between Docker Model Runner, Anthropic, OpenAI, etc. without business logic changes.
/// </summary>
public interface IModelProvider
{
    /// <summary>
    /// The name of this provider (e.g., "dmr", "anthropic", "openai").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// True if this provider is available and ready to serve requests.
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a single model call.
    /// </summary>
    /// <param name="request">The model request.</param>
    /// <param name="cancellationToken">Cancellation token for long-running operations.</param>
    /// <returns>The model response.</returns>
    Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a model call with structured JSON output validation.
    /// </summary>
    /// <param name="request">The model request with OutputSchema set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Model response with validated JSON output.</returns>
    Task<ModelResponse> CompleteStructuredAsync(ModelRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimate the cost of a request before execution (optional).
    /// </summary>
    decimal EstimateCost(ModelRequest request);
}

/// <summary>
/// Factory for creating provider instances based on configuration.
/// </summary>
public interface IModelProviderFactory
{
    /// <summary>
    /// Create a provider instance by name.
    /// </summary>
    IModelProvider CreateProvider(string providerName);

    /// <summary>
    /// Get the currently configured default provider.
    /// </summary>
    IModelProvider GetDefaultProvider();

    /// <summary>
    /// List available provider names.
    /// </summary>
    IEnumerable<string> GetAvailableProviders();
}
