# FeedTriage

[![CI](https://github.com/onexey/FeedTriage/actions/workflows/ci.yml/badge.svg)](https://github.com/onexey/FeedTriage/actions/workflows/ci.yml)
[![Publish Docker Image](https://github.com/onexey/FeedTriage/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/onexey/FeedTriage/actions/workflows/docker-publish.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

<!-- markdownlint-disable-next-line MD033 -->
<a href="https://www.buymeacoffee.com/onexey" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me a Coffee" height="30"></a>

FeedTriage is a .NET 10 background worker that reads unread entries from Miniflux, uses AI to decide which entries are worth keeping, marks irrelevant items as read, and leaves useful items unread for manual follow-up.

It is a triage service, not a summarizer. The core job is to filter signal from noise with a two-stage AI relevance pipeline.

## Quick start with Docker

Run the published container directly if you already have a Miniflux instance and an Ollama-compatible endpoint available.

```bash
mkdir -p data

docker run -d \
  --name feedtriage \
  -e FEEDTRIAGE__MINIFLUX__BASE_URL=http://host.docker.internal:8080 \
  -e FEEDTRIAGE__FILTERING__FOCUS_TOPICS='software engineering,software architecture,team leadership' \
  -e FEEDTRIAGE__MINIFLUX__API_TOKEN=replace-with-miniflux-token \
  -e FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__API_KEY=replace-with-ollama-api-key \
  -e FEEDTRIAGE__AI__PROVIDERS__REVIEW_OLLAMA_LARGE__API_KEY=replace-with-ollama-api-key \
  -v "$PWD/data:/app/data" \
  ghcr.io/onexey/feedtriage:latest
```

`FEEDTRIAGE__MINIFLUX__BASE_URL` defaults to `http://miniflux:8080`. In a standalone `docker run` setup you usually need to override it, as shown above.

If your Ollama-compatible endpoint does not expose the default models `ministral-3:3b` and `gemma3:27b`, pass these overrides too:

```bash
-e FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__MODEL=your-screening-model \
-e FEEDTRIAGE__AI__PROVIDERS__REVIEW_OLLAMA_LARGE__MODEL=your-review-model
```

## Quick start with Docker Compose

Use Docker Compose to run the published image from GitHub Container Registry in your own environment.

Create a `compose.yml` file like this:

```yaml
services:
  feedtriage:
    image: ghcr.io/onexey/feedtriage:latest
    restart: unless-stopped
    environment:
      FEEDTRIAGE__FILTERING__FOCUS_TOPICS: software engineering,software architecture,team leadership
      FEEDTRIAGE__MINIFLUX__API_TOKEN: replace-with-miniflux-token
      FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__API_KEY: replace-with-ollama-api-key
      FEEDTRIAGE__AI__PROVIDERS__REVIEW_OLLAMA_LARGE__API_KEY: replace-with-ollama-api-key
    volumes:
      - ./data:/app/data
```

Then start it:

```bash
docker compose up -d
```

These values are required with the built-in defaults:

- `FEEDTRIAGE__FILTERING__FOCUS_TOPICS`
- `FEEDTRIAGE__MINIFLUX__API_TOKEN`
- `FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__API_KEY`
- `FEEDTRIAGE__AI__PROVIDERS__REVIEW_OLLAMA_LARGE__API_KEY`

Add `FEEDTRIAGE__MINIFLUX__BASE_URL` only when Miniflux is not reachable as `http://miniflux:8080` from the FeedTriage container.

Add `FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__MODEL` and `FEEDTRIAGE__AI__PROVIDERS__REVIEW_OLLAMA_LARGE__MODEL` when your provider account does not have the default `ministral-3:3b` and `gemma3:27b` models.

By default, FeedTriage runs every 5 minutes and processes up to 5 unread items per run. That default is intentional so a new deployment does not burn too much LLM credit. If your feeds produce more items than that, increase `FEEDTRIAGE__SCHEDULER__RUN_INTERVAL` and/or `FEEDTRIAGE__PROCESSING__MAX_ARTICLES_PER_RUN` to match your volume and budget.

The default state path is `./data/state.json`, so the mounted `./data` volume persists state without further edits.

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

If a key is omitted entirely, FeedTriage uses the default shown below.

| Variable | Required | Default | Description |
| --- | --- | --- | --- |
| `FEEDTRIAGE__SCHEDULER__RUN_ON_START` | | `true` | Run immediately on startup |
| `FEEDTRIAGE__SCHEDULER__RUN_INTERVAL` | | `0.00:05:00` | How often to repeat (`d.hh:mm:ss`) |
| `FEEDTRIAGE__MINIFLUX__BASE_URL` | | `http://miniflux:8080` | Miniflux base URL |
| `FEEDTRIAGE__MINIFLUX__API_TOKEN` | ✓ | *(none)* | Miniflux API token |
| `FEEDTRIAGE__FILTERING__FOCUS_TOPICS` | ✓ | *(none)* | Comma-separated relevant topics |
| `FEEDTRIAGE__FILTERING__ANTI_TOPICS` | | | Comma-separated topics to exclude |
| `FEEDTRIAGE__PROCESSING__MAX_ARTICLES_PER_RUN` | | `5` | Max unread items to fetch and process per run |
| `FEEDTRIAGE__PROCESSING__DRY_RUN` | | `false` | Evaluate only; never mark entries as read |
| `FEEDTRIAGE__PROCESSING__MAX_RETRIES_PER_ENTRY` | | `5` | Retries before giving up on a failed entry |
| `FEEDTRIAGE__STATE__FILE_PATH` | | `./data/state.json` | JSON file used to persist the newest processed publication time |
| `FEEDTRIAGE__AI__SCREENING_CHAIN` | | `screen_ollama_small` | Ordered comma-separated provider names for Stage 1 |
| `FEEDTRIAGE__AI__REVIEW_CHAIN` | | `review_ollama_large` | Ordered comma-separated provider names for Stage 2 |
| `FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__TYPE` | | `ollama` | Provider type for the default Stage 1 provider |
| `FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__BASE_URL` | | `https://ollama.com/api` | Base URL for the default Stage 1 provider |
| `FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__MODEL` | | `ministral-3:3b` | Model for the default Stage 1 provider |
| `FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__API_KEY` | ✓ | *(none)* | API key for the default Stage 1 provider |
| `FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__TIMEOUT_SECONDS` | | `60` | Timeout for the default Stage 1 provider |
| `FEEDTRIAGE__AI__PROVIDERS__REVIEW_OLLAMA_LARGE__TYPE` | | `ollama` | Provider type for the default Stage 2 provider |
| `FEEDTRIAGE__AI__PROVIDERS__REVIEW_OLLAMA_LARGE__BASE_URL` | | `https://ollama.com/api` | Base URL for the default Stage 2 provider |
| `FEEDTRIAGE__AI__PROVIDERS__REVIEW_OLLAMA_LARGE__MODEL` | | `gemma3:27b` | Model for the default Stage 2 provider |
| `FEEDTRIAGE__AI__PROVIDERS__REVIEW_OLLAMA_LARGE__API_KEY` | ✓ | *(none)* | API key for the default Stage 2 provider |
| `FEEDTRIAGE__AI__PROVIDERS__REVIEW_OLLAMA_LARGE__TIMEOUT_SECONDS` | | `180` | Timeout for the default Stage 2 provider |

Provider names are case-insensitive and must match names listed in `SCREENING_CHAIN` or `REVIEW_CHAIN`.

For any additional provider instance name you define, these per-provider defaults still apply unless you override them:

```dotenv
FEEDTRIAGE__AI__PROVIDERS__YOUR_PROVIDER_NAME__TYPE=ollama
FEEDTRIAGE__AI__PROVIDERS__YOUR_PROVIDER_NAME__BASE_URL=https://ollama.com/api
FEEDTRIAGE__AI__PROVIDERS__YOUR_PROVIDER_NAME__MODEL=choose-a-model
FEEDTRIAGE__AI__PROVIDERS__YOUR_PROVIDER_NAME__API_KEY=required
FEEDTRIAGE__AI__PROVIDERS__YOUR_PROVIDER_NAME__TIMEOUT_SECONDS=60
```

If you prefer file-based configuration, [.env.example](/Users/mesutsoylu/Documents/Repos/FeedTriage/.env.example) shows the minimal required `.env` shape plus common overrides.

The 5-minute interval and 5-article cap are conservative defaults meant to limit LLM spend. If your feeds are busier and you are comfortable using more credits, raise the cap, shorten the interval, or both.

## Development

For source-based local container development, use the tracked [docker-compose.yml](/Users/mesutsoylu/Documents/Repos/FeedTriage/docker-compose.yml):

```bash
mkdir -p data

FEEDTRIAGE__MINIFLUX__API_TOKEN=replace-with-miniflux-token \
FEEDTRIAGE__FILTERING__FOCUS_TOPICS='software engineering,software architecture,team leadership' \
FEEDTRIAGE__AI__PROVIDERS__SCREEN_OLLAMA_SMALL__API_KEY=replace-with-ollama-api-key \
FEEDTRIAGE__AI__PROVIDERS__REVIEW_OLLAMA_LARGE__API_KEY=replace-with-ollama-api-key \
docker compose up --build -d
```

Export `FEEDTRIAGE__MINIFLUX__BASE_URL` as well if your local Miniflux is not reachable at the default `http://miniflux:8080`.

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
