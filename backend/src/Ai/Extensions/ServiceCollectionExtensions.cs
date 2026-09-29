using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Operon.Ai.Providers;
using Operon.Ai.Services;

namespace Operon.Ai.Extensions;

/// <summary>
/// Extension methods for registering AI services in the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register AI model providers and workflow services.
    /// Configuration is read from appsettings.json under "Ai:ModelProvider".
    /// </summary>
    public static IServiceCollection AddAiProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Read configuration
        var options = configuration
            .GetSection(ModelProviderOptions.ConfigSection)
            .Get<ModelProviderOptions>() ?? new ModelProviderOptions();

        // Validate configuration
        if (string.IsNullOrWhiteSpace(options.DmrBaseUrl))
            throw new InvalidOperationException("Ai:ModelProvider:DmrBaseUrl is required");

        if (string.IsNullOrWhiteSpace(options.DmrModelName))
            throw new InvalidOperationException("Ai:ModelProvider:DmrModelName is required");

        // Register configuration options
        services.Configure<ModelProviderOptions>(
            configuration.GetSection(ModelProviderOptions.ConfigSection));

        // Register HTTP clients for each provider
        services.AddHttpClient("dmr", client =>
        {
            client.BaseAddress = new Uri(options.DmrBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(300);
        });

        // Register the factory
        services.AddSingleton<IModelProviderFactory>(sp =>
            new ModelProviderFactory(
                sp,
                options,
                sp.GetRequiredService<ILogger<ModelProviderFactory>>()));

        // Register workflow service
        services.AddScoped<IWorkflowExecutionService, WorkflowExecutionService>();

        return services;
    }
}
