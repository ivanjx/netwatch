FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build

RUN apt-get update \
    && apt-get install --yes --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir /data

WORKDIR /source
COPY . .
RUN dotnet publish src/NetWatch.Server/NetWatch.Server.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    --warnaserror \
    --output /app/publish


FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled-extra AS final

WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish/ ./
COPY --from=build --chown=$APP_UID:$APP_UID /data /data

USER $APP_UID

EXPOSE 8080/tcp
EXPOSE 2055/udp
VOLUME ["/data"]

ENTRYPOINT ["./NetWatch.Server"]
