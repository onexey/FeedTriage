# ── Build stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies first (layer-cached)
COPY src/RssSummarizer.Worker/RssSummarizer.Worker.csproj src/RssSummarizer.Worker/
RUN dotnet restore src/RssSummarizer.Worker/RssSummarizer.Worker.csproj

# Copy source and publish
COPY src/ src/
RUN dotnet publish src/RssSummarizer.Worker/RssSummarizer.Worker.csproj \
    -c Release \
    -o /app/publish

# ── Runtime stage ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# Run as non-root
RUN groupadd --system --gid 1001 appgroup && \
    useradd --system --uid 1001 --gid appgroup --create-home appuser
USER appuser

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "RssSummarizer.Worker.dll"]
