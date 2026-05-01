# ── Build stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies first (layer-cached)
COPY src/FeedTriage.Worker/FeedTriage.Worker.csproj src/FeedTriage.Worker/
RUN dotnet restore src/FeedTriage.Worker/FeedTriage.Worker.csproj

# Copy source and publish
COPY src/ src/
RUN dotnet publish src/FeedTriage.Worker/FeedTriage.Worker.csproj \
    -c Release \
    -o /app/publish

# ── Runtime stage ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# Prepare runtime user
RUN groupadd --system --gid 1001 appgroup && \
    useradd --system --uid 1001 --gid appgroup --create-home appuser

COPY --from=build /app/publish .
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh

RUN chmod +x /usr/local/bin/docker-entrypoint.sh && \
    chown -R appuser:appgroup /app /home/appuser

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
CMD ["dotnet", "FeedTriage.Worker.dll"]
