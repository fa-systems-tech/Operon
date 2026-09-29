# Docker Model Runner Integration Guide

This guide explains how Operon uses Docker Model Runner (DMR) to provide cost-free, local AI model inference while maintaining support for cloud providers.

## Architecture Overview

```
┌─────────────┐
│  Frontend   │
│  (React)    │
└──────┬──────┘
       │ HTTP API
       ▼
┌─────────────────────────────────────┐
│  Backend (ASP.NET Core)             │
│  ┌──────────────────────────────┐   │
│  │  Workflow Execution Service  │   │
│  │  (Business Logic)            │   │
│  └──────────┬───────────────────┘   │
│             │ Uses              │
│  ┌──────────▼──────────────┐        │
│  │  Model Provider Factory │        │
│  │  (Provider Abstraction) │        │
│  └──────────┬──────────────┘        │
│             │                       │
│  ┌──────────▼───────────────────┐   │
│  │  IModelProvider              │   │
│  │  (Interface)                 │   │
│  └──────────┬───────────────────┘   │
└─────────────┼────────────────────────┘
              │
    ┌─────────┼──────────┬──────────┐
    │         │          │          │
    ▼         ▼          ▼          ▼
  ┌───┐   ┌────────┐ ┌──────────┐ ┌──────┐
  │DMR│   │Claude  │ │ OpenAI   │ │ TODO │
  └───┘   │(TODO)  │ │ (TODO)   │ └──────┘
          └────────┘ └──────────┘
```

## Docker Model Runner (DMR)

### What It Does

Docker Model Runner is a Docker Engine feature that:
- Pulls and caches AI models locally (Docker Hub, Hugging Face)
- Serves models via OpenAI-compatible and Ollama-compatible APIs
- Loads models into memory only when needed
- Automatically unloads unused models to save resources
- Costs $0 — inference happens on the developer's/server's hardware

### Supported Models

Popular models available for Operon:

| Model | Size | Use Case | Docker Hub Reference |
|-------|------|----------|---------------------|
| Smollm2 | ~1.5GB | General-purpose lightweight | `ai/smollm2` |
| Phi3 | ~2GB | Code-aware, medium-quality | `ai/phi3` |
| Llama2 | ~7GB | High-quality, more resources | `ai/llama2` |
| Mistral | ~5GB | Fast, efficient reasoning | `ai/mistral` |

See [hub.docker.com/u/ai](https://hub.docker.com/u/ai) for the full list.

## Setting Up DMR

### 1. Enable DMR in Docker Desktop

**macOS / Windows:**
- Open Docker Desktop
- Settings → **AI** → Enable Docker Model Runner

**Linux:**
```bash
docker run --rm -it --privileged docker:latest \
  docker run -d --name dmr docker:latest
```

### 2. Pull a Model

```bash
# Pull the default Smollm2 model
docker model pull ai/smollm2

# Or pull a different model
docker model pull ai/phi3
docker model pull ai/llama2
```

Models are cached in `~/.docker/models/` (or the equivalent on your OS).

### 3. Start the Model Service

Models are served automatically when accessed via the API. You don't need to manually start a container.

## Running Operon with DMR

### Development

1. **Clone the repository:**
   ```bash
   git clone https://github.com/fa-systems-tech/Operon.git
   cd Operon
   ```

2. **Create `.env` from template:**
   ```bash
   cp .env.example .env
   ```

3. **Start the stack:**
   ```bash
   docker compose up --build --pull always
   ```

   This will:
   - Build the backend and frontend
   - Start PostgreSQL
   - Start the model service (DMR runs as a Docker Engine feature, not a traditional container)
   - Connect everything via `operon_network`

4. **Access the application:**
   - Frontend: http://localhost:3000
   - Backend API: http://localhost:8080
   - Health check: http://localhost:8080/health

### Production

For staging/pilot deployment:
```bash
docker compose -f compose.prod.yaml up -d
```

This uses a stricter configuration with health checks and restart policies.

## Configuration

### Environment Variables

All configuration is read from environment variables and `appsettings.json`.

**Key variables:**

| Variable | Description | Default |
|----------|-------------|---------|
| `AI_PROVIDER` | Which provider to use | `dmr` |
| `DMR_BASE_URL` | Docker Model Runner endpoint | `http://model:8000` |
| `DMR_MODEL_NAME` | Model name/ID | `ai/smollm2` |
| `DMR_CONTEXT_SIZE` | Context window size | `4096` |
| `MAX_CONCURRENT_JOBS` | Parallel workflow jobs | `3` |
| `JOB_TIMEOUT_SECONDS` | Per-job timeout | `300` |

### Switching Models at Runtime

Update `DMR_MODEL_NAME` in your `.env` or environment:

```bash
export DMR_MODEL_NAME=ai/phi3
docker compose restart backend
```

The backend will connect to the new model on next startup.

### Adding Cloud Provider Support

When ready to support Anthropic or OpenAI:

1. **Add configuration:**
   ```json
   {
     "Ai": {
       "ModelProvider": {
         "DefaultProvider": "anthropic",
         "AnthropicApiKey": "${ANTHROPIC_API_KEY}"
       }
     }
   }
   ```

2. **Implement provider:**
   ```csharp
   // backend/src/Ai/Providers/AnthropicProvider.cs
   public class AnthropicProvider : IModelProvider { }
   ```

3. **Register in factory:**
   ```csharp
   _providers["anthropic"] = new Lazy<IModelProvider>(() => 
       CreateAnthropicProvider());
   ```

4. **No business logic changes needed** — the abstraction handles provider switching.

## Model Adapter Layer

### Interfaces

The model adapter layer consists of three key abstractions:

#### 1. `IModelProvider` — The Core Interface

```csharp
public interface IModelProvider
{
    string ProviderName { get; }
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
    Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
    Task<ModelResponse> CompleteStructuredAsync(ModelRequest request, CancellationToken cancellationToken);
    decimal EstimateCost(ModelRequest request);
}
```

**Every provider must implement these methods.** This enables:
- Provider-agnostic business logic
- Testing with mock providers
- Easy provider switching
- Cost tracking per provider

#### 2. `ModelRequest` & `ModelResponse` — Unified Contracts

```csharp
public class ModelRequest
{
    public string Prompt { get; set; }
    public List<ConversationMessage> ConversationHistory { get; set; }
    public string Context { get; set; } // Injected business context
    public int? MaxTokens { get; set; }
    public double? Temperature { get; set; }
    public string? OutputSchema { get; set; } // For structured output validation
    public int? BudgetLimit { get; set; }
    public int TimeoutSeconds { get; set; }
}

public class ModelResponse
{
    public string Content { get; set; }
    public bool IsValidStructuredOutput { get; set; }
    public int TokensUsed { get; set; }
    public decimal EstimatedCost { get; set; }
    public string ModelUsed { get; set; }
}
```

These are **provider-agnostic** — any provider returns the same response structure.

#### 3. `IModelProviderFactory` — Instantiation

```csharp
public interface IModelProviderFactory
{
    IModelProvider CreateProvider(string providerName);
    IModelProvider GetDefaultProvider();
    IEnumerable<string> GetAvailableProviders();
}
```

The factory handles:
- Provider instantiation
- HTTP client management
- Configuration injection
- Provider registration

### Using the Model Adapter

#### In Business Logic (Workflow Service)

```csharp
public class OnboardingWorkflow
{
    private readonly IModelProviderFactory _providerFactory;

    public async Task<ClientProfile> GenerateProfileAsync(
        SourceDocuments docs,
        CancellationToken cancellationToken)
    {
        var provider = _providerFactory.GetDefaultProvider();

        var request = new ModelRequest
        {
            Prompt = "Extract client information...",
            Context = FormatContext(docs),
            OutputSchema = ClientProfileSchema,
            BudgetLimit = 50, // cents
            TimeoutSeconds = 60
        };

        var response = await provider.CompleteStructuredAsync(
            request, 
            cancellationToken);

        if (!response.IsValidStructuredOutput)
            throw new InvalidOperationException("Invalid output schema");

        return ParseProfile(response.Content);
    }
}
```

**No provider-specific code is needed.**

#### Testing with Mock Provider

```csharp
public class OnboardingWorkflowTests
{
    [Fact]
    public async Task GenerateProfile_ReturnsValidProfile()
    {
        // Use a mock provider for deterministic testing
        var mockProvider = new Mock<IModelProvider>();
        mockProvider
            .Setup(p => p.CompleteStructuredAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModelResponse
            {
                Content = @"{ ""name"": ""Test Client"", ""budget"": 50000 }",
                IsValidStructuredOutput = true,
                ModelUsed = "test"
            });

        var workflow = new OnboardingWorkflow(mockProvider.Object);
        var profile = await workflow.GenerateProfileAsync(docs, CancellationToken.None);

        Assert.NotNull(profile);
    }
}
```

## Workflow Execution Service

The `IWorkflowExecutionService` wraps model calls with:

- **Deduplication:** JobKey prevents duplicate execution
- **Retries:** Automatic retry on transient failures (up to 3 times)
- **Budgets:** Enforces maximum token/cost limits per job
- **Timeouts:** Respects per-job and per-request timeouts
- **Structured Output:** Validates JSON schema compliance
- **Audit Trail:** Logs provider, cost, tokens for every execution

### Example Usage

```csharp
var request = new WorkflowJobRequest
{
    JobKey = $"onboarding-{clientId}-v1",
    WorkflowType = "onboarding",
    ClientId = clientId,
    Context = new WorkflowContext
    {
        ClientProfileVersion = "approved-v1",
        PlaybookVersion = "fa-systems-v2",
        SourceDocuments = new List<ContextDocument> { /* ... */ },
        OutputSchema = ClientProfileSchema
    },
    BudgetLimit = 100, // $1.00
    MaxRetries = 3,
    TimeoutSeconds = 300
};

var result = await executionService.ExecuteAsync(request);

if (result.Status == WorkflowExecutionResult.ExecutionStatus.Succeeded)
{
    var draft = new Artifact
    {
        DraftContent = result.DraftOutput,
        Cost = result.TotalCost,
        TokensUsed = result.TokensUsed,
        ProviderUsed = result.ProviderUsed,
        RequestId = result.RequestId
    };
    // Save draft to database for human review
}
```

## Monitoring & Debugging

### Health Checks

```bash
# Backend health
curl http://localhost:8080/health

# DMR endpoint (if running standalone)
curl http://localhost:8000/health
```

### Logs

Check Docker logs:
```bash
# Backend logs
docker logs operon_backend

# All services
docker logs operon_backend operon_frontend operon_postgres
```

### Cost Tracking

Every workflow execution logs:
- Provider used
- Tokens consumed
- Estimated cost
- Request ID

Track across workflows to identify expensive operations:
```sql
SELECT workflow_type, provider, SUM(tokens_used), SUM(cost)
FROM workflow_executions
GROUP BY workflow_type, provider;
```

## Troubleshooting

### "DMR is not healthy"

1. Ensure Docker Model Runner is enabled in Docker Desktop (Settings → AI).
2. Check if the model has been pulled:
   ```bash
   docker model list
   ```
3. Verify network connectivity:
   ```bash
   docker compose exec backend curl http://model:8000/health
   ```

### "Request timed out"

- Increase `JOB_TIMEOUT_SECONDS` in `.env`
- Model may be too large for available RAM; try a smaller model
- Check system resources: `docker stats`

### Model takes too long to load initially

The first request loads the model into memory (can take 30-60 seconds). Subsequent requests are faster. This is expected behavior.

### Switching providers mid-project

1. Update `AI_PROVIDER` in `.env`
2. Rebuild and restart backend: `docker compose restart backend`
3. Business logic requires no changes (thanks to the abstraction!)

## Expanding the Model Adapter

When adding new providers:

1. **Implement `IModelProvider`:**
   ```csharp
   public class MyProvider : IModelProvider { }
   ```

2. **Register in factory:**
   ```csharp
   _providers["myprovider"] = new Lazy<IModelProvider>(() => 
       new MyProvider(...));
   ```

3. **Configure in appsettings:**
   ```json
   "Ai": {
       "DefaultProvider": "myprovider",
       "MyProviderApiKey": "${API_KEY}"
   }
   ```

4. **No business logic changes needed.**

## Next Steps

- [ ] Implement health check endpoint (`GET /health`)
- [ ] Add Anthropic provider
- [ ] Add OpenAI provider
- [ ] Implement structured output validation (JSON Schema)
- [ ] Add cost tracking database schema
- [ ] Create admin dashboard for provider/model selection
- [ ] Set up monitoring/alerting for failed jobs
