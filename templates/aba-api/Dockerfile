# Generated domain-neutral API container. No developer secrets or business examples.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore Aba.Starter.slnx
RUN dotnet publish src/Aba.Starter.Api/Aba.Starter.Api.csproj -c Release \
    --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /publish ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 8080
# Standard .NET image contains an unprivileged app account.
USER $APP_UID
ENTRYPOINT ["dotnet", "Aba.Starter.Api.dll"]
