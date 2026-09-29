# Operon Architecture

## Project Structure

```
Operon/
├── backend/                          # ASP.NET Core API
│   ├── src/
│   │   ├── Ai/
│   │   │   ├── Core/
│   │   │   │   └── IModelProvider.cs         # Provider interface & contracts
│   │   │   ├── Providers/
│   │   │   │   ├── DockerModelRunnerProvider.cs
│   │   │   │   └── ModelProviderFactory.cs
│   │   │   ├── Services/
│   │   │   │   └── WorkflowExecutionService.cs  # Workflow orchestration
│   │   │   └── Extensions/
│   │   │       └── ServiceCollectionExtensions.cs  # DI setup
│   │   ├── appsettings.json          # Config (development)
│   │   └── appsettings.Production.json
│   ├── Dockerfile                    # Multi-stage build
│   └── requirements.txt              # Dependencies
│
├── frontend/                         # React + TypeScript
│   ├── src/
│   ├── Dockerfile
│   └── package.json
│
├── compose.yaml                      # Development environment
├── compose.prod.yaml                 # Production environment
├── .env.example                      # Configuration template
├── .dockerignore                     # Docker build optimization
│
├── documentation/
│   ├── QUICKSTART.md                 # 5-minute setup guide
│   ├── DMR_INTEGRATION.md            # Docker Model Runner guide
│   ├── MODEL_ADAPTER_DEVELOPER_REFERENCE.md  # Developer guide
│   └── ARCHITECTURE.md               # This file
│
└── README.md                         # Project overview
```

## Service Architecture

### Development Environment (`compose.yaml`)

```
┌─────────────────────────────────────────────────────────────┐
│                  Docker Compose Stack                       │
│                                                             │
│  ┌──────────────┐         ┌──────────────┐                │
│  │  Frontend    │         │  Backend     │                │
│  │  (React)     │◄────────│  (ASP.NET)   │                │
│  │  :3000       │         │  :8080       │                │
│  └──────────────┘         └──────┬───────┘                │
│                                  │                        │
│                    ┌─────────────┼─────────────┐          │
│                    │             │             │          │
│                    ▼             ▼             ▼          │
│              ┌──────────┐  ┌──────────┐  ┌─────────┐     │
│              │PostgreSQL│  │Model Svc │  │Caddy    │     │
│              │Database  │  │(DMR)     │  │(TODO)   │     │
│              │:5432     │  │:8000     │  │:443     │     │
│              └──────────┘  └──────────┘  └─────────┘     │
│                                                             │
│                    operon_network (bridge)                │
└─────────────────────────────────────────────────────────────┘
```

### Service Responsibilities

| Service | Port | Technology | Purpose |
|---------|------|-----------|---------|
| **frontend** | 3000 | React + TypeScript + Vite | Client workspaces, review interface, status views |
| **backend** | 8080 | ASP.NET Core 10 | APIs, business logic, workflow orchestration |
| **postgres** | 5432 | PostgreSQL 17 | Persistent data (clients, profiles, jobs, audit logs) |
| **model** | 8000 | Docker Model Runner | AI model serving (local inference) |

### Database Schema (Planned)

```
Clients
├── id
├── name
├── created_at
└── access_level

ClientProfileVersions
├── id
├── client_id (FK)
├── content (JSON)
├── status (draft/approved)
├── approved_by
├── created_at

SourceDocuments
├── id
├── client_id (FK)
├── title
├── content
├── document_type
├── uploaded_at

WorkflowExecutions
├── id
├── job_key (unique, for deduplication)
├── workflow_type
├── client_id
├── provider_used
├── tokens_used
├── cost_cents
├── status
├── executed_at

AuditEvents
├── id
├── actor_id
├── action
├── resource_type
├── resource_id
├── timestamp
└── details (JSON)
```

## AI Model Adapter Layer

### Layer Diagram

```
┌─────────────────────────────────┐
│  Business Logic                 │
│  (Workflows, Agents)            │
└─────────────────┬───────────────┘
                  │
                  │ Uses
                  ▼
┌─────────────────────────────────┐
│ WorkflowExecutionService        │
│ - Deduplication                 │
│ - Retries & Budgets             │
│ - Structured Output             │
│ - Audit Trail                   │
└─────────────────┬───────────────┘
                  │
                  │ Calls
                  ▼
┌─────────────────────────────────┐
│ ModelProviderFactory            │
│ (Provider instantiation)         │
└─────────────────┬───────────────┘
                  │
        ┌─────────┼─────────┬──────────┐
        │         │         │          │
        ▼         ▼         ▼          ▼
    ┌─────┐  ┌────────┐ ┌──────────┐ ┌───────┐
    │ DMR │  │Anthropic│ │OpenAI    │ │Custom │
    │     │  │(TODO)   │ │(TODO)    │ └───────┘
    └─────┘  └────────┘ └──────────┘
       │
       │ HTTP (OpenAI-compatible API)
       ▼
    ┌──────────────────────────────┐
    │ Docker Model Runner          │
    │ (Local AI Model Service)     │
    └──────────────────────────────┘
```

### Data Flow

```
User Initiates Workflow
        │
        ▼
┌──────────────────────────┐
│ WorkflowExecutionService │
│ - Build context          │
│ - Check dedup (JobKey)   │
│ - Estimate cost          │
└──────┬───────────────────┘
       │
       ▼
┌──────────────────────────┐
│ GetDefaultProvider()     │
│ (from factory)           │
└──────┬───────────────────┘
       │
       ▼
┌──────────────────────────┐
│ IModelProvider           │
│ - IsHealthyAsync()       │
│ - CompleteAsync()        │
└──────┬───────────────────┘
       │
       ▼ (HTTP request)
┌──────────────────────────┐
│ Docker Model Runner      │
│ Model in memory          │
└──────┬───────────────────┘
       │
       ▼ (Response)
┌──────────────────────────┐
│ WorkflowExecutionService │
│ - Validate output        │
│ - Log execution          │
│ - Save draft artifact    │
└──────┬───────────────────┘
       │
       ▼
Human Review & Approval
```

## Technology Stack

### Frontend
- **React 18** — UI framework
- **TypeScript** — Type safety
- **Vite** — Build tooling
- **TailwindCSS** (recommended) — Styling

### Backend
- **C# 13** / **ASP.NET Core 10** — Runtime
- **Entity Framework Core** — ORM
- **PostgreSQL 17** — Database
- **ASP.NET Core Identity** — Authentication
- **Serilog** (recommended) — Logging

### Infrastructure
- **Docker** — Containerization
- **Docker Compose** — Orchestration
- **Docker Model Runner** — Local AI inference
- **GitHub Actions** — CI/CD
- **Caddy** (planned) — Reverse proxy / HTTPS

## Configuration Management

### Environment Variables

All configuration flows through environment variables:

```
Development (docker-compose.yaml):
  AI_PROVIDER=dmr
  DMR_BASE_URL=http://model:8000
  DMR_MODEL_NAME=ai/smollm2
  ...

Production (compose.prod.yaml):
  AI_PROVIDER=dmr  (or anthropic/openai)
  DMR_BASE_URL=http://model:8000
  DB_PASSWORD=<secret>
  ...
```

### Configuration Files

```
appsettings.json (development defaults)
  └─ Read by ASP.NET Core on startup
  └─ Overridden by environment variables
  
appsettings.Production.json (production defaults)
  └─ More restrictive logging, optimized settings
```

### Switching Providers

No code changes needed:

```bash
# Development: switch to Anthropic (when implemented)
export AI_PROVIDER=anthropic
export ANTHROPIC_API_KEY=sk-ant-...
docker compose restart backend

# Production: use Anthropic
docker compose -f compose.prod.yaml down
export AI_PROVIDER=anthropic
docker compose -f compose.prod.yaml up -d
```

## Deployment Models

### Local Development

```bash
docker compose up --build
```

- Uses docker-compose.yaml
- DMR on developer's machine
- All services on localhost
- Hot reload for frontend

### Staging/Pilot

```bash
docker compose -f compose.prod.yaml up -d
```

- Uses compose.prod.yaml
- Separate database, volumes
- Health checks enabled
- Restart policies
- No secrets in code (env vars only)

### Future: Multi-Node

The architecture supports evolution to:
- Docker Swarm (via compose)
- Kubernetes (migrate services to manifests)
- Load balancing (Caddy reverse proxy)
- Database replication (PostgreSQL HA)

## API Contracts

### Health Endpoint

```
GET /health
200 OK
{
  "status": "healthy",
  "timestamp": "2025-01-15T10:30:00Z",
  "services": {
    "database": "ok",
    "model_provider": "ok"
  }
}
```

### Workflow Execution Endpoint (Planned)

```
POST /api/workflows/execute
{
  "jobKey": "onboarding-client123-v1",
  "workflowType": "onboarding",
  "clientId": "client123",
  "context": { ... },
  "budgetLimit": 100
}

200 OK
{
  "status": "succeeded",
  "draftOutput": "...",
  "tokensUsed": 1250,
  "cost": 0.12,
  "providerId": "dmr"
}
```

## Security Considerations

### Data Isolation
- Client data scoped by `client_id`
- Backend access checks on every API
- Frontend enforces user roles

### Secret Management
- No secrets in code or env vars (use Docker secrets for production)
- API keys loaded from environment only
- Audit logs for approval/access

### Privacy
- Source documents minimized before sending to AI
- Anonymized/redacted for development
- Cost tracking per workflow

## Monitoring & Observability

### Logging

- **Application:** ASP.NET Core logging to console/file
- **Container:** Docker compose logs
- **Database:** PostgreSQL logs (mount volume)

### Metrics (Planned)

- Workflow execution rate
- Average response time per provider
- Cost per workflow / month
- Error rates by provider

### Health Checks

```bash
# Backend
curl http://localhost:8080/health

# Database
docker compose exec postgres pg_isready

# Models
docker model list
```

## Testing Strategy

### Unit Tests
- Mock providers
- Service layer logic
- Data transformations

### Integration Tests
- Real provider (DMR) with test models
- Database operations
- API endpoints

### E2E Tests
- Full workflow execution
- UI interactions
- Database state

## Development Workflows

### Adding a Feature

1. Create feature branch
2. Update database schema (if needed)
3. Implement business logic
4. Add/update tests
5. Create PR with linked issue
6. Merge to main after review

### Adding a Provider

1. Implement `IModelProvider`
2. Register in `ModelProviderFactory`
3. Add configuration
4. Test with mock
5. Test with real provider (if available)

### Deploying

```bash
# Build images
docker compose -f compose.prod.yaml build

# Push to registry
docker tag operon_backend:latest myregistry/operon-backend:v1.0
docker push myregistry/operon-backend:v1.0

# Deploy
docker compose -f compose.prod.yaml pull
docker compose -f compose.prod.yaml up -d

# Verify
curl https://operon.mycompany.com/health
```

## Performance Characteristics

### Typical Request Timeline

```
User Uploads Document
        ↓ (50ms)
Backend Validates & Extracts Features
        ↓ (100ms)
Load Model into Memory (first request: 30-60s, subsequent: 1-5s)
        ↓
Generate Response (5-15s depending on model)
        ↓ (100ms)
Validate Output Schema
        ↓ (50ms)
Save Draft to Database
        ↓
Return to Frontend

Total: 35-80s (first) or 5-15s (cached model)
```

### Resource Usage

| Environment | CPU | RAM | Storage |
|-------------|-----|-----|---------|
| Development (weak computer) | 2 vCPU | 4GB | 10GB (OS) + 2GB (models) |
| Development (normal) | 4 vCPU | 8GB | 10GB + 4GB (models) |
| Staging/Pilot | 4 vCPU | 8GB | 50GB (OS + DB) + 4GB (models) |

## Next Steps

- [ ] Implement frontend scaffolding (React + TypeScript)
- [ ] Create health check endpoint
- [ ] Add database migrations
- [ ] Implement onboarding workflow
- [ ] Add API documentation (Swagger)
- [ ] Set up GitHub Actions CI/CD
- [ ] Implement Anthropic provider
- [ ] Add cost dashboard
- [ ] Set up monitoring (Prometheus/Grafana)
