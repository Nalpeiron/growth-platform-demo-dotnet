# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /source

COPY src/NalpeironGrowthPlatformDemo/NalpeironGrowthPlatformDemo.csproj src/NalpeironGrowthPlatformDemo/
RUN --mount=type=secret,id=nuget_config,target=/root/.nuget/NuGet/NuGet.Config,required=true \
    dotnet restore src/NalpeironGrowthPlatformDemo/NalpeironGrowthPlatformDemo.csproj

COPY src/NalpeironGrowthPlatformDemo/ src/NalpeironGrowthPlatformDemo/
RUN dotnet publish src/NalpeironGrowthPlatformDemo/NalpeironGrowthPlatformDemo.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "NalpeironGrowthPlatformDemo.dll"]
