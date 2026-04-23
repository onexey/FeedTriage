# RssSummarizer

A minimal .NET 10 background worker that reads unread entries from **Miniflux**, runs a two-stage AI relevance filter using **Ollama**, marks irrelevant entries as read, and leaves relevant entries unread for manual review.

## How it works

1. On each scheduled run the worker fetches unread entries from your Miniflux instance.
2. **Stage 1 — Screening**: the title and a short excerpt are sent to a fast AI model. Entries that fail screening are marked as read.
3. **Stage 2 — Full review**: for entries that pass screening, the full article content is fetched via Miniflux's scraper and sent to a larger review model.
4. Entries that fail full review are marked as read.
5. Entries that pass full review are left **unread** in Miniflux so they stand out for manual follow-up.
6. If any AI stage or content fetch fails, the entry is left **unread** so it can be retried on the next run.
7. Hacker News entries are handled specially: the linked article and the discussion thread are evaluated independently, and if either candidate is relevant the Miniflux entry stays unread.

Each AI stage returns a single `{ passed, reason }` response. Reasons are surfaced in logs and dry-run output for debugging and are not stored elsewhere.

## Requirements

- .NET 10 SDK
- Docker and Docker Compose for containerized deployment
- A running [Miniflux](https://miniflux.app) instance
- An [Ollama Cloud](https://ollama.com) account with an API key, or another reachable Ollama-compatible endpoint

## Quick start

```bash
# 1. Copy and fill in the environment file
cp .env.example .env
$EDITOR .env   # set Miniflux and Ollama values

# 2. Start
docker compose up -d
```

## Configuration reference

All configuration is via environment variables. Double underscores (`__`) separate sections.

| Variable | Required | Default | Description |
|---|---|---|---|
| `RSSSUMMARIZER__SCHEDULER__RUN_ON_START` | | `true` | Run immediately on startup |
| `RSSSUMMARIZER__SCHEDULER__RUN_INTERVAL` | | `1.00:00:00` | How often to repeat (`d.hh:mm:ss`) |
| `RSSSUMMARIZER__MINIFLUX__BASE_URL` | ✓ | | Miniflux base URL |
| `RSSSUMMARIZER__MINIFLUX__API_TOKEN` | ✓ | | Miniflux API token |
| `RSSSUMMARIZER__FILTERING__FOCUS_TOPICS` | ✓ | | Comma-separated relevant topics |
| `RSSSUMMARIZER__FILTERING__ANTI_TOPICS` | | | Comma-separated topics to exclude |
| `RSSSUMMARIZER__PROCESSING__MAX_ARTICLES_PER_RUN` | | *(unlimited)* | Max unread items to fetch and process per run |
| `RSSSUMMARIZER__PROCESSING__DRY_RUN` | | `false` | Evaluate only; never mark entries as read |
| `RSSSUMMARIZER__STATE__FILE_PATH` | | `state.json` | JSON file used to persist the newest processed publication time |
| `RSSSUMMARIZER__AI__SCREENING_CHAIN` | ✓ | | Ordered comma-separated provider names for Stage 1 |
| `RSSSUMMARIZER__AI__REVIEW_CHAIN` | ✓ | | Ordered comma-separated provider names for Stage 2 |

Provider instances follow this pattern:

```dotenv
RSSSUMMARIZER__AI__PROVIDERS__{NAME}__TYPE=ollama
RSSSUMMARIZER__AI__PROVIDERS__{NAME}__BASE_URL=https://ollama.com/api
RSSSUMMARIZER__AI__PROVIDERS__{NAME}__MODEL=qwen3:4b
RSSSUMMARIZER__AI__PROVIDERS__{NAME}__API_KEY=your-ollama-cloud-api-key
RSSSUMMARIZER__AI__PROVIDERS__{NAME}__TIMEOUT_SECONDS=60
```

`{NAME}` is case-insensitive and must match a name in `SCREENING_CHAIN` or `REVIEW_CHAIN`. See `.env.example` for full examples.

## Development

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run with dry-run enabled (no read-state mutations)
RSSSUMMARIZER__PROCESSING__DRY_RUN=true dotnet run --project src/RssSummarizer.Worker
```

## Failure semantics

| Failure | Behaviour |
|---|---|
| Stage 1: all providers fail | Entry left unread; retried next run |
| Full-content fetch fails | Entry left unread |
| Stage 2: all providers fail | Entry left unread |
| Mark-as-read fails after an irrelevant decision | Warning logged; entry may be processed again next run |
