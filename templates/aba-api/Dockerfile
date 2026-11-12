# Generated domain-neutral API container. No developer secrets or business examples.
# Review .NET 10 Noble base indexes quarterly and after security advisories.
# Immutable multi-arch manifest-list digests verified using Docker buildx imagetools.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /source
COPY . .
RUN dotnet restore Aba.Starter.slnx
RUN dotnet publish src/Aba.Starter.Api/Aba.Starter.Api.csproj -c Release \
    --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e AS runtime
WORKDIR /app
COPY --from=build /publish ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 8080
# Standard .NET image contains an unprivileged app account.
USER $APP_UID
ENTRYPOINT ["dotnet", "Aba.Starter.Api.dll"]
