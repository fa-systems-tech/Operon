# Model Adapter Layer — Developer Reference

This document explains the model adapter layer architecture for developers implementing workflows.

## Architecture at a Glance

```
IModelProvider (Interface)
    ↑ (implemented by)
    │
    ├─ DockerModelRunnerProvider (DMR)
    ├─ AnthropicProvider (TODO)
    ├─ OpenAiProvider (TODO)
    └─ MockProvider (testing)

IModelProviderFactory (Provider instantiation)
    ↓
ModelRequest / ModelResponse (Unified contracts)
    ↓
IWorkflowExecutionService (Retry logic, budgets, deduplication)
    ↓
Business Logic (Workflows, agents)
```

## Core Concepts

### 1. Provider Abstraction

The `IModelProvider` interface ensures all providers have identical capabilities:

```csharp
Task<ModelResponse> CompleteAsync(ModelRequest, CancellationToken);
Task<ModelResponse> CompleteStructuredAsync(ModelRequest, CancellationToken);
Task<bool> IsHealthyAsync(CancellationToken);
decimal EstimateCost(ModelRequest);
```

**Benefit:** Business logic never knows which provider is running.

### 2. Request/Response Contracts

`ModelRequest` and `ModelResponse` normalize API differences:

**Request normalization:**
- Converts any format (chat, completion, API-specific) into a single `ModelRequest`
- Includes business context automatically
- Handles timeouts, budgets, retries

**Response normalization:**
- Every provider returns the same `ModelResponse` structure
- Includes tokens, cost, validity, model name
- Enables cost tracking and provider comparison

### 3. Workflow Execution Service

`IWorkflowExecutionService` adds:

| Feature | Benefit |
|---------|---------|
| Deduplication | Prevents duplicate artifact generation (same JobKey) |
| Retries | Handles transient failures automatically |
| Budgets | Enforces cost/token limits per workflow |
| Structured Output | Validates JSON schema compliance |
| Audit Trail | Logs every execution for debugging |

### 4. Factory Pattern

`IModelProviderFactory` handles instantiation and selection:

```csharp
var factory = serviceProvider.GetRequiredService<IModelProviderFactory>();
var provider = factory.GetDefaultProvider(); // Uses AI_PROVIDER env var
```

**Benefit:** Provider configuration is centralized; clients don't instantiate providers directly.

## Using the Adapter in Workflows

### Example: Onboarding Workflow

```csharp
public class OnboardingAgent
{
    private readonly IWorkflowExecutionService _workflowService;

    public OnboardingAgent(IWorkflowExecutionService workflowService)
    {
        _workflowService = workflowService;
    }

    public async Task<ClientProfile> ExtractProfileAsync(
        string clientId,
        SourceDocuments documents,
        PlaybookVersion playbook,
        CancellationToken cancellationToken)
    {
        // Prepare context
        var context = new WorkflowContext
        {
            ClientProfileVersion = "approved-v1",
            PlaybookVersion = playbook.Version,
            SourceDocuments = documents.ToContextDocuments(),
            BusinessRules = new Dictionary<string, string>
            {
                ["max_profile_fields"] = "50",
                ["require_budget_confirmation"] = "true"
            },
            OutputSchema = ClientProfileSchema.JsonSchema
        };

        // Execute workflow
        var request = new WorkflowJobRequest
        {
            JobKey = $"onboarding-extract-{clientId}-{playbook.Version}",
            WorkflowType = "onboarding-extract",
            ClientId = clientId,
            Context = context,
            BudgetLimit = 100, // $1.00
            MaxRetries = 3,
            TimeoutSeconds = 300
        };

        var result = await _workflowService.ExecuteAsync(request, cancellationToken);

        if (result.Status != WorkflowExecutionResult.ExecutionStatus.Succeeded)
            throw new WorkflowExecutionException(result.ErrorMessage);

        // Parse response
        var profile = JsonSerializer.Deserialize<ClientProfile>(result.DraftOutput);

        // Log execution
        await LogWorkflowExecutionAsync(new WorkflowLog
        {
            JobKey = request.JobKey,
            WorkflowType = request.WorkflowType,
            ProviderUsed = result.ProviderUsed,
            TokensUsed = result.TokensUsed,
            CostCents = result.TotalCost,
            RequestId = result.RequestId,
            Status = "success"
        });

        return profile;
    }
}
```

**Note:** This code works with DMR, Anthropic, or OpenAI without modification.

## Adding a Custom Provider

### Step 1: Implement IModelProvider

```csharp
public class MyCustomProvider : IModelProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MyCustomProvider> _logger;
    private readonly MyCustomOptions _options;

    public string ProviderName => "mycustom";

    public MyCustomProvider(
        HttpClient httpClient,
        MyCustomOptions options,
        ILogger<MyCustomProvider> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_options.BaseUrl}/health",
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ModelResponse> CompleteAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        // Convert ModelRequest to your provider's format
        var myRequest = new MyCustomRequest
        {
            Text = request.Prompt,
            Context = request.Context,
            MaxTokens = request.MaxTokens ?? 2048
        };

        // Call your provider
        var httpRequest = new StringContent(
            JsonSerializer.Serialize(myRequest),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(
            $"{_options.BaseUrl}/api/complete",
            httpRequest,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var myResponse = JsonSerializer.Deserialize<MyCustomResponse>(json);

        // Convert to ModelResponse (standardized format)
        return new ModelResponse
        {
            Content = myResponse.Output,
            IsValidStructuredOutput = request.OutputSchema == null,
            TokensUsed = myResponse.TokenCount,
            EstimatedCost = CalculateCost(myResponse.TokenCount),
            RequestId = myResponse.Id,
            ModelUsed = _options.ModelName,
            CompletedAt = DateTime.UtcNow,
            RawResponse = json
        };
    }

    public async Task<ModelResponse> CompleteStructuredAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        // Your provider may support structured output natively
        // Or validate schema here
        var response = await CompleteAsync(request, cancellationToken);

        if (request.OutputSchema != null)
        {
            response.IsValidStructuredOutput = ValidateJsonSchema(response.Content);
        }

        return response;
    }

    public decimal EstimateCost(ModelRequest request)
    {
        // Estimate cost based on prompt size
        var inputTokens = EstimateTokens(request.Prompt + request.Context);
        var outputTokens = (request.MaxTokens ?? 2048) / 2; // rough estimate
        return (inputTokens * _options.InputTokenPrice +
                outputTokens * _options.OutputTokenPrice) / 100m;
    }

    private decimal CalculateCost(int tokensUsed)
    {
        return (tokensUsed * _options.AverageTokenPrice) / 100m;
    }

    private bool ValidateJsonSchema(string content)
    {
        try
        {
            JsonSerializer.Deserialize<object>(content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private int EstimateTokens(string text)
    {
        // Rough estimation: ~4 chars per token
        return text.Length / 4;
    }
}

public class MyCustomOptions
{
    public string BaseUrl { get; set; }
    public string ModelName { get; set; }
    public decimal InputTokenPrice { get; set; } // price per 1M tokens in cents
    public decimal OutputTokenPrice { get; set; }
    public decimal AverageTokenPrice => (InputTokenPrice + OutputTokenPrice) / 2;
}
```

### Step 2: Register in Factory

Edit `ModelProviderFactory.RegisterProviders()`:

```csharp
_providers["mycustom"] = new Lazy<IModelProvider>(() =>
{
    var httpClientFactory = (IHttpClientFactory)_serviceProvider
        .GetService(typeof(IHttpClientFactory));
    var logger = (ILogger<MyCustomProvider>)_serviceProvider
        .GetService(typeof(ILogger<MyCustomProvider>));

    var httpClient = httpClientFactory.CreateClient("mycustom");
    var options = new MyCustomOptions
    {
        BaseUrl = _options.MyCustomBaseUrl,
        ModelName = _options.MyCustomModel,
        InputTokenPrice = 0.001m, // $0.001 per 1M input tokens
        OutputTokenPrice = 0.002m // $0.002 per 1M output tokens
    };

    return new MyCustomProvider(httpClient, options, logger);
});
```

### Step 3: Configure HTTP Client

In `ServiceCollectionExtensions.AddAiProviders()`:

```csharp
services.AddHttpClient("mycustom", client =>
{
    client.BaseAddress = new Uri(options.MyCustomBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(300);
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.MyCustomApiKey}");
});
```

### Step 4: Update Configuration

In `appsettings.json`:

```json
{
  "Ai": {
    "ModelProvider": {
      "DefaultProvider": "mycustom",
      "MyCustomBaseUrl": "https://api.mycustom.com",
      "MyCustomModel": "custom-model-v1",
      "MyCustomApiKey": "${MY_CUSTOM_API_KEY}"
    }
  }
}
```

### Step 5: Use It

```bash
export MY_CUSTOM_API_KEY=sk-...
export AI_PROVIDER=mycustom
docker compose restart backend
```

**Business logic unchanged.**

## Testing

### Mock Provider for Unit Tests

```csharp
public class MockModelProvider : IModelProvider
{
    private readonly Dictionary<string, string> _responses;

    public string ProviderName => "mock";

    public MockModelProvider(Dictionary<string, string> responses = null)
    {
        _responses = responses ?? new Dictionary<string, string>();
    }

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<ModelResponse> CompleteAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        var content = _responses.TryGetValue(request.Prompt, out var response)
            ? response
            : "{}"; // Default response

        return Task.FromResult(new ModelResponse
        {
            Content = content,
            IsValidStructuredOutput = true,
            TokensUsed = 100,
            EstimatedCost = 0m,
            RequestId = Guid.NewGuid().ToString(),
            ModelUsed = "mock",
            CompletedAt = DateTime.UtcNow
        });
    }

    public Task<ModelResponse> CompleteStructuredAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
        => CompleteAsync(request, cancellationToken);

    public decimal EstimateCost(ModelRequest request) => 0m;
}
```

### Using Mock in Tests

```csharp
[Fact]
public async Task OnboardingAgent_ExtractProfile_Succeeds()
{
    // Arrange
    var mockResponses = new Dictionary<string, string>
    {
        ["Extract client profile..."] = @"{ ""name"": ""ACME Inc"", ""budget"": 50000 }"
    };
    var mockProvider = new MockModelProvider(mockResponses);

    var agent = new OnboardingAgent(
        new WorkflowExecutionService(
            MockFactory(mockProvider),
            logger));

    // Act
    var profile = await agent.ExtractProfileAsync(
        "client-1",
        documents,
        playbook,
        CancellationToken.None);

    // Assert
    Assert.Equal("ACME Inc", profile.Name);
    Assert.Equal(50000, profile.Budget);
}

private IModelProviderFactory MockFactory(IModelProvider provider)
{
    var mock = new Mock<IModelProviderFactory>();
    mock.Setup(f => f.GetDefaultProvider()).Returns(provider);
    return mock.Object;
}
```

## Cost Tracking

Every workflow logs costs automatically:

```csharp
// Logged by WorkflowExecutionService
var execution = new WorkflowExecution
{
    JobKey = result.RequestId,
    WorkflowType = "onboarding-extract",
    ProviderUsed = result.ProviderUsed,
    TokensUsed = result.TokensUsed,
    CostCents = result.TotalCost,
    Status = "succeeded",
    ExecutedAt = DateTime.UtcNow
};

// Query by workflow and provider
var costByWorkflow = db.WorkflowExecutions
    .GroupBy(e => new { e.WorkflowType, e.ProviderUsed })
    .Select(g => new
    {
        g.Key.WorkflowType,
        g.Key.ProviderUsed,
        TotalTokens = g.Sum(e => e.TokensUsed),
        TotalCost = g.Sum(e => e.CostCents)
    });
```

## Debugging

### Enable Detailed Logging

In `appsettings.Development.json`:

```json
{
  "Ai": {
    "ModelProvider": {
      "EnableDetailedLogging": true
    }
  },
  "Logging": {
    "LogLevel": {
      "Operon.Ai": "Debug"
    }
  }
}
```

### Common Issues

| Issue | Cause | Fix |
|-------|-------|-----|
| "Provider is not healthy" | DMR not running or network issue | `docker compose up` or check `DMR_BASE_URL` |
| "Request timeout" | Model too large for RAM | Switch to smaller model or increase `JOB_TIMEOUT_SECONDS` |
| "Invalid output schema" | LLM output doesn't match schema | Validate schema, improve prompt, increase MaxTokens |
| "Budget exceeded" | Cost estimated before execution | Reduce context size or lower `MaxBudgetCents` |

## Performance Considerations

### Optimize for Weak Computers

1. **Use smaller models:**
   ```bash
   docker model pull ai/smollm2  # 1.5GB, ~3-5s responses
   ```

2. **Reduce context size:**
   ```env
   DMR_CONTEXT_SIZE=2048  # Default is 4096
   ```

3. **Limit concurrent jobs:**
   ```env
   MAX_CONCURRENT_JOBS=1  # Lower on weak systems
   ```

4. **Profile resource usage:**
   ```bash
   docker stats operon_backend
   ```

## Roadmap

- [ ] Add Anthropic provider
- [ ] Add OpenAI provider
- [ ] Implement cost dashboard
- [ ] Add provider comparison benchmarks
- [ ] Support multi-provider fallback (try DMR first, fallback to cloud)
- [ ] Add model auto-download on startup
