# Quick Start Guide

## Prerequisites

- Docker Desktop (or Docker Engine on Linux)
- Git
- Node.js 20+ (for frontend development)
- .NET 10 SDK (for backend development)

## Setup (5 minutes)

### 1. Enable Docker Model Runner

**Docker Desktop (macOS/Windows):**
- Open Docker Desktop
- Settings → AI → Enable Docker Model Runner

**Linux:**
```bash
# DMR is available in Docker Engine 27.0+
docker version
```

### 2. Clone & Configure

```bash
git clone https://github.com/fa-systems-tech/Operon.git
cd Operon
cp .env.example .env
```

### 3. Pull a Model

```bash
# Use the default Smollm2 (lightweight, ~1.5GB)
docker model pull ai/smollm2

# Or choose another:
# docker model pull ai/phi3      # ~2GB, faster
# docker model pull ai/mistral   # ~5GB, better quality
```

### 4. Start the Application

```bash
docker compose up --build --pull always
```

Wait for all services to start (1-2 minutes on first run).

### 5. Access

- **Frontend:** http://localhost:3000
- **Backend API:** http://localhost:8080
- **API Docs:** http://localhost:8080/swagger (once implemented)

## Development Workflow

### Frontend Development

```bash
cd frontend
npm install
npm run dev
```

This starts the Vite dev server at http://localhost:5173 with hot reload.

### Backend Development

```bash
cd backend
dotnet restore
dotnet run
```

Or use Docker Compose for the full stack:
```bash
docker compose up
```

### Running Tests

```bash
cd backend
dotnet test
```

## Switching Models

Edit `.env`:
```env
DMR_MODEL_NAME=ai/phi3
```

Restart the backend:
```bash
docker compose restart backend
```

**No code changes needed.**

## Checking Status

```bash
# List downloaded models
docker model list

# View running containers
docker compose ps

# View logs
docker compose logs -f backend
```

## Stopping

```bash
docker compose down
```

## Next Steps

- Read [DMR_INTEGRATION.md](./DMR_INTEGRATION.md) for architecture details
- Check [ARCHITECTURE.md](./ARCHITECTURE.md) for project structure
- Review the model adapter code in `backend/src/Ai/`
