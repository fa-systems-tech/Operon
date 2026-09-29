using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Operon.Ai.Providers;

/// <summary>
/// Factory for creating and managing model provider instances.
/// Supports switching between providers (DMR, Anthropic, OpenAI) via configuration.
/// </summary>
public class ModelProviderFactory : IModelProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ModelProviderOptions _options;
    private readonly ILogger<ModelProviderFactory> _logger;
    private readonly Dictionary<string, Lazy<IModelProvider>> _providers;

    public ModelProviderFactory(
        IServiceProvider serviceProvider,
        ModelProviderOptions options,
        ILogger<ModelProviderFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _providers = new Dictionary<string, Lazy<IModelProvider>>(StringComparer.OrdinalIgnoreCase);
        RegisterProviders();
    }

    public IModelProvider CreateProvider(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Provider name cannot be null or empty", nameof(providerName));

        if (_providers.TryGetValue(providerName, out var lazyProvider))
        {
            _logger.LogInformation("Created provider: {ProviderName}", providerName);
            return lazyProvider.Value;
        }

        throw new InvalidOperationException($"Unknown provider: {providerName}");
    }

    public IModelProvider GetDefaultProvider()
    {
        var provider = CreateProvider(_options.DefaultProvider);
        _logger.LogInformation("Using default provider: {Provider}", _options.DefaultProvider);
        return provider;
    }

    public IEnumerable<string> GetAvailableProviders()
    {
        return _providers.Keys.OrderBy(x => x);
    }

    private void RegisterProviders()
    {
        // Docker Model Runner (local, cost-free)
        _providers["dmr"] = new Lazy<IModelProvider>(() =>
        {
            var httpClientFactory = (IHttpClientFactory)_serviceProvider.GetService(typeof(IHttpClientFactory));
            var logger = (ILogger<DockerModelRunnerProvider>)_serviceProvider
                .GetService(typeof(ILogger<DockerModelRunnerProvider>));

            var httpClient = httpClientFactory.CreateClient("dmr");
            return new DockerModelRunnerProvider(
                httpClient,
                _options.DmrBaseUrl,
                _options.DmrModelName,
                _options.DmrContextSize,
                logger);
        });

        // TODO: Anthropic provider
        // _providers["anthropic"] = new Lazy<IModelProvider>(() => CreateAnthropicProvider());

        // TODO: OpenAI provider
        // _providers["openai"] = new Lazy<IModelProvider>(() => CreateOpenAiProvider());
    }
}

/// <summary>
/// Configuration options for model providers.
/// </summary>
public class ModelProviderOptions
{
    public const string ConfigSection = "Ai:ModelProvider";

    /// <summary>
    /// Default provider to use (dmr, anthropic, openai).
    /// </summary>
    public string DefaultProvider { get; set; } = "dmr";

    /// <summary>
    /// Base URL for Docker Model Runner.
    /// </summary>
    public string DmrBaseUrl { get; set; } = "http://model:8000";

    /// <summary>
    /// Model name/ID for Docker Model Runner (e.g., ai/smollm2, ai/llama2).
    /// </summary>
    public string DmrModelName { get; set; } = "ai/smollm2";

    /// <summary>
    /// Context window size for DMR model.
    /// </summary>
    public int DmrContextSize { get; set; } = 4096;

    /// <summary>
    /// Anthropic API key (if using anthropic provider).
    /// </summary>
    public string AnthropicApiKey { get; set; }

    /// <summary>
    /// OpenAI API key (if using openai provider).
    /// </summary>
    public string OpenAiApiKey { get; set; }

    /// <summary>
    /// Maximum budget (in cents or provider units) per workflow execution.
    /// </summary>
    public int MaxBudgetCents { get; set; } = 100; // $1.00 default

    /// <summary>
    /// Enable request/response logging for debugging.
    /// </summary>
    public bool EnableDetailedLogging { get; set; }
}
