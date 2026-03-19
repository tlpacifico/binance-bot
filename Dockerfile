# ---- Stage 1: Build ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY BinanceBot.sln .
COPY src/BinanceBot.Core/BinanceBot.Core.csproj                             src/BinanceBot.Core/
COPY src/BinanceBot.Infrastructure/BinanceBot.Infrastructure.csproj         src/BinanceBot.Infrastructure/
COPY src/BinanceBot.Strategies/BinanceBot.Strategies.csproj                 src/BinanceBot.Strategies/
COPY src/BinanceBot.Worker/BinanceBot.Worker.csproj                         src/BinanceBot.Worker/
COPY tests/BinanceBot.Core.Tests/BinanceBot.Core.Tests.csproj               tests/BinanceBot.Core.Tests/
COPY tests/BinanceBot.Infrastructure.Tests/BinanceBot.Infrastructure.Tests.csproj tests/BinanceBot.Infrastructure.Tests/
COPY tests/BinanceBot.Strategies.Tests/BinanceBot.Strategies.Tests.csproj   tests/BinanceBot.Strategies.Tests/
COPY tests/BinanceBot.Worker.Tests/BinanceBot.Worker.Tests.csproj           tests/BinanceBot.Worker.Tests/

RUN dotnet restore

COPY . .
RUN dotnet publish src/BinanceBot.Worker/BinanceBot.Worker.csproj \
    -c Release -o /app/publish --no-restore

# ---- Stage 2: Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /app/data /app/logs

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:3000
EXPOSE 3000

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:3000/api/health || exit 1

ENTRYPOINT ["dotnet", "BinanceBot.Worker.dll"]
