FROM mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src
COPY global.json source-revision.txt ./
COPY backend/ backend/
RUN dotnet restore backend/WarehouseRent.Api/WarehouseRent.Api.csproj --locked-mode
RUN revision="$(cat source-revision.txt)" && \
    dotnet publish backend/WarehouseRent.Api/WarehouseRent.Api.csproj -c Release --no-restore \
    -o /publish -p:UseAppHost=false -p:SourceRevisionId="$revision"

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee
USER root
RUN apt-get update && apt-get install -y --no-install-recommends util-linux && \
    rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /publish/ ./
COPY ops/cloud/start-api.sh /app/start-api.sh
RUN chmod 755 /app/start-api.sh
ENV ASPNETCORE_ENVIRONMENT=Production
ENV CloudHosting__Enabled=true
ENV CloudHosting__DataProtectionPath=/data/keys
EXPOSE 8080
ENTRYPOINT ["/app/start-api.sh"]
