# syntax=docker/dockerfile:1.7

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.302
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-alpine
ARG INSTALL_RUNTIME_DEPENDENCIES=true

FROM ${SDK_IMAGE} AS build
WORKDIR /source

COPY --chown=app:app src/NalpeironGrowthPlatformDemo/NalpeironGrowthPlatformDemo.csproj src/NalpeironGrowthPlatformDemo/
USER app
RUN --mount=type=secret,id=nuget_config,target=/tmp/NuGet.Config,mode=0444,required=true \
    dotnet restore src/NalpeironGrowthPlatformDemo/NalpeironGrowthPlatformDemo.csproj \
    --configfile /tmp/NuGet.Config

COPY --chown=app:app src/NalpeironGrowthPlatformDemo/ src/NalpeironGrowthPlatformDemo/
RUN --mount=type=secret,id=nuget_config,target=/tmp/NuGet.Config,mode=0444,required=true \
    dotnet publish src/NalpeironGrowthPlatformDemo/NalpeironGrowthPlatformDemo.csproj \
    --configuration Release \
    --output /tmp/publish \
    /p:UseAppHost=false \
    /p:RestoreConfigFile=/tmp/NuGet.Config

FROM ${RUNTIME_IMAGE} AS runtime
ARG INSTALL_RUNTIME_DEPENDENCIES
WORKDIR /app

RUN if [ "$INSTALL_RUNTIME_DEPENDENCIES" = "true" ]; then \
        apk add --no-cache \
            curl \
            icu-data-full \
            icu-libs \
            tzdata; \
    fi

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    LC_ALL=en_US.UTF-8 \
    LANG=en_US.UTF-8
EXPOSE 8080

COPY --from=build /tmp/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "NalpeironGrowthPlatformDemo.dll"]
