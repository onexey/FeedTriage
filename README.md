# FeedTriage

FeedTriage is a .NET 10 background worker that reads unread entries from Miniflux, uses AI to decide which entries are worth keeping, marks irrelevant items as read, and leaves useful items unread for manual follow-up.

It is a triage service, not a summarizer. The core job is to filter signal from noise with a two-stage AI relevance pipeline.

## Quick start with Docker

Run the published container directly if you already have a Miniflux instance and an Ollama-compatible endpoint available.

```bash
cp .env.example .env
$EDITOR .env

docker run -d \
  --name feedtriage \
  --env-file .env \
  -v "$PWD/data:/app/data" \
  ghcr.io/onexey/feedtriage:latest
```

## Quick start with Docker Compose

Use Docker Compose to run the published image from GitHub Container Registry in your own environment.

Create a `compose.yml` file like this:

```yaml
services:
  feedtriage:
    image: ghcr.io/onexey/feedtriage:latest
    restart: unless-stopped
    env_file:
      - .env
    volumes:
      - ./data:/app/data
```

Then start it:

```bash
cp .env.example .env
$EDITOR .env
docker compose up -d
```

Only the blank secret values in `.env` need to be filled in before first start:

- `FEEDTRIAGE__MINIFLUX__API_TOKEN`
- `FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__API_KEY`
- `FEEDTRIAGE__AI__PROVIDERS__REVIEW_OLLAMA_LARGE__API_KEY`

The sample state path already points at `./data/state.json`, so the checked-in Compose file persists state without further edits.

The repository also includes [docker-compose.yml](/Users/mesutsoylu/Documents/Repos/FeedTriage/docker-compose.yml) for developers working from source. That file builds the image locally from the checked-out code, while the example above is for end users who only want to pull `ghcr.io/onexey/feedtriage:latest` and run it.

## Supported systems

- Feed system: Miniflux
- AI provider type: Ollama-compatible endpoints
- Tested provider setup: Ollama Cloud

The current implementation includes a Miniflux client and an Ollama provider adapter. If you point the AI configuration at another Ollama-compatible endpoint, FeedTriage can use it without changing the application code.

## What it does

1. Fetch unread entries from Miniflux.
2. Run Stage 1 screening on the title and excerpt using a faster model.
3. Fetch full article content for entries that pass screening.
4. Run Stage 2 review using a stronger model.
5. Mark irrelevant entries as read.
6. Leave relevant entries unread so they stand out in Miniflux.

If AI evaluation fails or full-content retrieval fails, the entry stays unread so it can be retried later.

Hacker News entries are handled specially: FeedTriage evaluates the linked article and the discussion thread independently, and keeps the Miniflux entry unread if either one looks relevant.

## How it works

The worker is split into a few focused parts:

- Miniflux client for unread entry retrieval, article extraction, and mark-as-read calls
- AI decision pipeline for stage-specific provider fallback chains
- Ollama provider adapter behind a shared AI provider interface
- Article processor that orchestrates triage and state updates
- Run state repository that stores the newest processed publication timestamp

Key behavior:

- Screening and review use independent ordered provider chains.
- The first provider that returns a valid decision wins.
- If every provider in a stage fails, the entry stays unread.
- Dry run evaluates entries but does not mutate Miniflux read state or local run state.

## Requirements

- .NET 10 SDK for local development
- Docker and Docker Compose for containerized runs
- A running Miniflux instance
- An Ollama Cloud account with an API key, or another reachable Ollama-compatible endpoint

## Configuration reference

All configuration is supplied with environment variables. Double underscores (`__`) separate sections.

| Variable | Required | Default | Description |
| --- | --- | --- | --- |
| `FEEDTRIAGE__SCHEDULER__RUN_ON_START` | | `true` | Run immediately on startup |
| `FEEDTRIAGE__SCHEDULER__RUN_INTERVAL` | | `1.00:00:00` | How often to repeat (`d.hh:mm:ss`) |
| `FEEDTRIAGE__MINIFLUX__BASE_URL` | ✓ | | Miniflux base URL |
| `FEEDTRIAGE__MINIFLUX__API_TOKEN` | ✓ | | Miniflux API token |
| `FEEDTRIAGE__FILTERING__FOCUS_TOPICS` | ✓ | | Comma-separated relevant topics |
| `FEEDTRIAGE__FILTERING__ANTI_TOPICS` | | | Comma-separated topics to exclude |
| `FEEDTRIAGE__PROCESSING__MAX_ARTICLES_PER_RUN` | | *(unlimited)* | Max unread items to fetch and process per run |
| `FEEDTRIAGE__PROCESSING__DRY_RUN` | | `false` | Evaluate only; never mark entries as read |
| `FEEDTRIAGE__STATE__FILE_PATH` | | `./data/state.json` | JSON file used to persist the newest processed publication time |
| `FEEDTRIAGE__AI__SCREENING_CHAIN` | ✓ | | Ordered comma-separated provider names for Stage 1 |
| `FEEDTRIAGE__AI__REVIEW_CHAIN` | ✓ | | Ordered comma-separated provider names for Stage 2 |

The default sample uses these provider instance keys:

```dotenv
FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__TYPE=ollama
FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__BASE_URL=https://ollama.com/api
FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__MODEL=qwen3:4b
FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__API_KEY=
FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__TIMEOUT_SECONDS=60
```

Provider names are case-insensitive and must match names listed in `SCREENING_CHAIN` or `REVIEW_CHAIN`. See [.env.example](/Users/mesutsoylu/Documents/Repos/FeedTriage/.env.example) for the full working sample.

## Development

For source-based local container development, use the tracked [docker-compose.yml](/Users/mesutsoylu/Documents/Repos/FeedTriage/docker-compose.yml):

```bash
cp .env.example .env
$EDITOR .env
docker compose up --build -d
```

```bash
dotnet restore FeedTriage.sln
dotnet build FeedTriage.sln
dotnet test FeedTriage.sln

FEEDTRIAGE__PROCESSING__DRY_RUN=true dotnet run --project src/FeedTriage.Worker
```

## Docker image

This repository publishes a container image to GitHub Container Registry.

- Registry: `ghcr.io`
- Image name: `ghcr.io/onexey/feedtriage`
- Tags on `main`: `latest`, `main`, and the commit SHA

Example:

```bash
docker pull ghcr.io/onexey/feedtriage:latest
```

## Failure semantics

| Failure | Behavior |
| --- | --- |
| Stage 1: all providers fail | Entry left unread; retried next run |
| Full-content fetch fails | Entry left unread |
| Stage 2: all providers fail | Entry left unread |
| Mark-as-read fails after an irrelevant decision | Warning logged; entry may be processed again next run |
