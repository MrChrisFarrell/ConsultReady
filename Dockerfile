FROM node:24-bookworm-slim AS web
WORKDIR /build/web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
COPY src/ConsultReady.Api/ConsultReady.Api.csproj src/ConsultReady.Api/
RUN dotnet restore src/ConsultReady.Api/ConsultReady.Api.csproj
COPY src/ConsultReady.Api/ src/ConsultReady.Api/
COPY --from=web /build/src/ConsultReady.Api/wwwroot/ src/ConsultReady.Api/wwwroot/
RUN dotnet publish src/ConsultReady.Api/ConsultReady.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/ ./
# Render mounts the persistent disk here. Runtime initializes ownership before dropping privileges.
USER root
RUN apt-get update && apt-get install -y --no-install-recommends gosu && rm -rf /var/lib/apt/lists/*
COPY docker-entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 ConsultReady__Mode=Hosted ConsultReady__DataDirectory=/var/data
EXPOSE 8080
ENTRYPOINT ["/entrypoint.sh"]
