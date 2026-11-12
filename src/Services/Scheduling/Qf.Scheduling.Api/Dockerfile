# syntax=docker/dockerfile:1.7
# Build context: repository root. docker build -f src/Services/Scheduling/Qf.Scheduling.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN dotnet restore src/Services/Scheduling/Qf.Scheduling.Api/Qf.Scheduling.Api.csproj
RUN dotnet publish src/Services/Scheduling/Qf.Scheduling.Api/Qf.Scheduling.Api.csproj \
      -c Release -o /app --no-restore /p:UseAppHost=false

# Chiseled Ubuntu: no shell, no package manager, runs as non-root (uid 1654) by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Qf.Scheduling.Api.dll"]
