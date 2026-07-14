FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]
COPY ["contracts/dotnet/RealtimePix.Contracts/RealtimePix.Contracts.csproj", "contracts/dotnet/RealtimePix.Contracts/"]
COPY ["building-blocks/dotnet/RealtimePix.Eventing/RealtimePix.Eventing.csproj", "building-blocks/dotnet/RealtimePix.Eventing/"]
COPY ["building-blocks/dotnet/RealtimePix.Persistence/RealtimePix.Persistence.csproj", "building-blocks/dotnet/RealtimePix.Persistence/"]
COPY ["building-blocks/dotnet/RealtimePix.Web/RealtimePix.Web.csproj", "building-blocks/dotnet/RealtimePix.Web/"]
COPY ["services/identity-presence-service/IdentityPresence.Domain/IdentityPresence.Domain.csproj", "services/identity-presence-service/IdentityPresence.Domain/"]
COPY ["services/identity-presence-service/IdentityPresence.Application/IdentityPresence.Application.csproj", "services/identity-presence-service/IdentityPresence.Application/"]
COPY ["services/identity-presence-service/IdentityPresence.Infrastructure/IdentityPresence.Infrastructure.csproj", "services/identity-presence-service/IdentityPresence.Infrastructure/"]
COPY ["services/identity-presence-service/IdentityPresence.Api/IdentityPresence.Api.csproj", "services/identity-presence-service/IdentityPresence.Api/"]
RUN dotnet restore "services/identity-presence-service/IdentityPresence.Api/IdentityPresence.Api.csproj"

COPY ["contracts/dotnet/RealtimePix.Contracts/", "contracts/dotnet/RealtimePix.Contracts/"]
COPY ["building-blocks/dotnet/RealtimePix.Eventing/", "building-blocks/dotnet/RealtimePix.Eventing/"]
COPY ["building-blocks/dotnet/RealtimePix.Persistence/", "building-blocks/dotnet/RealtimePix.Persistence/"]
COPY ["building-blocks/dotnet/RealtimePix.Web/", "building-blocks/dotnet/RealtimePix.Web/"]
COPY ["services/identity-presence-service/", "services/identity-presence-service/"]
RUN dotnet publish "services/identity-presence-service/IdentityPresence.Api/IdentityPresence.Api.csproj" \
    --configuration "$BUILD_CONFIGURATION" \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_ENVIRONMENT=Production \
    EventBus__Directory=/tmp/realtime-pix-bus
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "IdentityPresence.Api.dll"]
