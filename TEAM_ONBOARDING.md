# Team Onboarding Checklist

Use this checklist to ensure every team member gets set up correctly and understands the foundation.

## For Each Team Member

### Phase 1: Environment Setup (15 minutes)

- [ ] Clone the repository
  ```bash
  git clone https://github.com/fa-systems-tech/Operon.git
  cd Operon
  ```

- [ ] Verify Docker is installed and running
  ```bash
  docker --version
  docker compose --version
  ```

- [ ] Enable Docker Model Runner
  - macOS/Windows: Docker Desktop → Settings → **AI** → Enable Docker Model Runner
  - Linux: Verify Docker 27.0+ is installed (`docker --version`)

- [ ] Create local environment file
  ```bash
  cp .env.example .env
  ```

- [ ] Pull the default AI model (lightweight, ~1.5GB)
  ```bash
  docker model pull ai/smollm2
  ```

- [ ] Start the full stack
  ```bash
  docker compose up --build --pull always
  ```

- [ ] Verify services are running
  ```bash
  # In another terminal
  curl http://localhost:3000      # Frontend
  curl http://localhost:8080      # Backend
  curl http://localhost:8080/health  # Health check
  ```

### Phase 2: Understanding the Foundation (30 minutes)

- [ ] Read FOUNDATION_SUMMARY.md
  - Understand what was built and why
  - Know where to find key files

- [ ] Read documentation/QUICKSTART.md
  - Understand how to start/stop services
  - Learn how to switch models

- [ ] Read documentation/ARCHITECTURE.md
  - Understand service architecture
  - Know the three main components (Brain, Workforce, Command Center from plan)
  - Understand the AI model adapter layer

- [ ] Read documentation/DMR_INTEGRATION.md (if working on AI/models)
  - Understand Docker Model Runner
  - Know how to configure model selection

- [ ] Skim backend/src/Ai/Core/IModelProvider.cs
  - Understand the provider abstraction
  - See how requests/responses are structured

### Phase 3: Component-Specific Setup

#### If Working on Frontend

- [ ] Install Node.js 20+ (verify: `node --version`)
- [ ] Navigate to frontend and install dependencies
  ```bash
  cd frontend
  npm install
  ```
- [ ] Start Vite dev server
  ```bash
  npm run dev
  ```
- [ ] Verify hot reload works (http://localhost:5173)

#### If Working on Backend

- [ ] Install .NET 10 SDK (verify: `dotnet --version`)
- [ ] Navigate to backend
  ```bash
  cd backend
  dotnet restore
  ```
- [ ] Read backend/src/appsettings.json to understand configuration
- [ ] Know where `backend/src/Ai/` directory is (model adapter layer)

#### If Working on AI/Agents/Workflows

- [ ] Read documentation/MODEL_ADAPTER_DEVELOPER_REFERENCE.md
- [ ] Study backend/src/Workflows/Onboarding/OnboardingWorkflow.example.cs
- [ ] Understand the pattern:
  - Inject `IWorkflowExecutionService`
  - Create `WorkflowJobRequest`
  - Call `_executionService.ExecuteAsync()`
  - No provider-specific code needed

#### If Working on DevOps/Infrastructure

- [ ] Understand compose.yaml (development) vs. compose.prod.yaml (production)
- [ ] Know Docker basics (images, containers, networks)
- [ ] Understand .env and appsettings.json configuration
- [ ] Set up GitHub Actions CI/CD (beyond this scope, but noted)

### Phase 4: Verification

- [ ] Run `docker compose ps` — all services should show "Up"
- [ ] Check backend logs: `docker compose logs -f backend` — no errors
- [ ] Verify database: `docker compose exec postgres psql -U operon_user -d operon_dev -c '\dt'`
- [ ] Test model: `docker model list` — ai/smollm2 (or chosen model) listed
- [ ] Test API health: `curl http://localhost:8080/health` — 200 OK (once implemented)

## Component Owner Setup

### Frontend Owner

After Phase 3 (Frontend):
- [ ] Explore `frontend/src/` structure
- [ ] Set up preferred code editor (VS Code recommended with Prettier/ESLint)
- [ ] Understand TypeScript (required for this project)
- [ ] Know API will be at `http://localhost:8080`

### Backend Owner

After Phase 3 (Backend):
- [ ] Explore `backend/src/` structure
- [ ] Set up VS Code or Rider (C# IDE)
- [ ] Understand the Ai folder structure (provider abstraction)
- [ ] Create first API endpoint (health check)

### AI/Agents Owner

After Phase 3 (AI/Agents):
- [ ] Study the example workflow: `backend/src/Workflows/Onboarding/OnboardingWorkflow.example.cs`
- [ ] Understand `IWorkflowExecutionService` — this is your primary tool
- [ ] Know you'll never directly instantiate providers
- [ ] Test DMR locally: pull a model, verify it works

### Database Owner

After Phase 2:
- [ ] Connect to PostgreSQL
  ```bash
  docker compose exec postgres psql -U operon_user -d operon_dev
  ```
- [ ] Understand Entity Framework Core migrations will come next
- [ ] Plan schema (Clients, ClientProfileVersions, SourceDocuments, WorkflowExecutions, AuditEvents)

### DevOps/Infrastructure Owner

After Phase 3 (DevOps):
- [ ] Understand production deployment strategy
- [ ] Know that secrets (API keys, DB passwords) use environment variables
- [ ] Plan for CI/CD (GitHub Actions) in coming weeks
- [ ] Document backup and recovery procedures

## Team-Wide Activities

### Before Each Iteration

- [ ] **Iteration Planning** — Confirm scope, owners, risks
- [ ] **Architecture Review** — Ensure new work aligns with adapter layer pattern
- [ ] **Configuration Review** — Verify .env and appsettings are correct

### During Iteration

- [ ] **Daily Standup** — Share progress, blockers
- [ ] **Mid-Iteration Integration** — Merge PRs, verify compose stack still works
- [ ] **Code Review** — Every PR reviewed before merge

### End of Iteration

- [ ] **Tag Release** — `git tag -a v1.0 -m "Release 1"` (example)
- [ ] **Verify Compose** — `docker compose down -v && docker compose up` works
- [ ] **Documentation Update** — README, ARCHITECTURE, etc.
- [ ] **Retrospective** — What worked? What needs improvement?

## Troubleshooting During Setup

### Docker not running
```bash
# macOS: Start Docker Desktop from Applications
# Windows: Start Docker Desktop from Start Menu
# Linux: sudo systemctl start docker
docker ps  # Should work
```

### Model pull fails
```bash
# Try manual pull with retry
docker model pull ai/smollm2
# Or check internet connectivity
curl https://api.github.com  # Should work
```

### Compose fails to start
```bash
# Check logs
docker compose logs

# Common issues:
# - Port 3000, 8080, 5432 in use
# - Insufficient disk space
# - Docker image pull failed (network issue)
```

### Frontend won't load at localhost:3000
```bash
# Check frontend container
docker compose logs frontend

# Or run directly
cd frontend && npm install && npm run dev
```

### Backend won't connect to model
```bash
# Verify model service is accessible
docker compose exec backend curl http://model:8000/health

# Check compose networking
docker network inspect operon_network

# Verify DMR is enabled (macOS/Windows)
docker model list
```

## Success Criteria

Everyone should be able to:

- [ ] Clone repo and run `docker compose up` successfully
- [ ] Access frontend (localhost:3000) and see a page (blank is OK at this stage)
- [ ] Access backend API health check (localhost:8080/health)
- [ ] View backend logs with `docker compose logs -f backend`
- [ ] Explain the three-layer architecture (Brain, Workforce, Command Center)
- [ ] Understand the model adapter pattern (IModelProvider, factory, contracts)
- [ ] Know which provider is currently active (`AI_PROVIDER` env var)
- [ ] Switch to a different model by updating `.env` and restarting
- [ ] Write a simple test for their component
- [ ] Contribute to their assigned component without breaking other services

## Communication & Support

### Getting Help

- **Architecture/Design:** Check ARCHITECTURE.md, FOUNDATION_SUMMARY.md
- **How to use Docker Compose:** QUICKSTART.md
- **How to implement a workflow:** MODEL_ADAPTER_DEVELOPER_REFERENCE.md, OnboardingWorkflow.example.cs
- **Docker Model Runner specifics:** DMR_INTEGRATION.md
- **Stuck on setup:** Team Slack/Discord, escalate to DevOps owner

### Updating Documentation

If you find:
- Outdated instructions → Update the .md file
- Missing explanation → Add it to ARCHITECTURE.md
- New issue encountered → Add to Troubleshooting section

Keep docs in sync with code!

## Next Steps After Setup

1. **Wait for iteration kickoff** — Team will assign you to specific components
2. **Read component-specific docs** — Backend owner reads backend setup guide, etc.
3. **Create first PR** — Contribute to your component
4. **Collaborate** — Talk to teammates about dependencies

## Final Checklist

Before claiming "I'm ready to work":

- [ ] `docker compose ps` shows all services Up
- [ ] `curl http://localhost:8080/health` works
- [ ] I can explain the three layers (Brain/Workforce/Command Center)
- [ ] I understand the model adapter pattern
- [ ] I know where the code I'll touch lives
- [ ] I've read documentation for my component
- [ ] I can create and push a Git branch
- [ ] I know how to restart services after making changes

---

**Welcome to Operon!** 🚀

Your foundation is solid. The heavy lifting on architecture is done. Now we build the business logic.

Questions? Check the docs first. Still stuck? Ask the team.
