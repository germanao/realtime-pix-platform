FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]
COPY ["contracts/dotnet/RealtimePix.Contracts/RealtimePix.Contracts.csproj", "contracts/dotnet/RealtimePix.Contracts/"]
COPY ["building-blocks/dotnet/RealtimePix.Eventing/RealtimePix.Eventing.csproj", "building-blocks/dotnet/RealtimePix.Eventing/"]
COPY ["services/bot-service/Bot.Domain/Bot.Domain.csproj", "services/bot-service/Bot.Domain/"]
COPY ["services/bot-service/Bot.Application/Bot.Application.csproj", "services/bot-service/Bot.Application/"]
COPY ["services/bot-service/Bot.Infrastructure/Bot.Infrastructure.csproj", "services/bot-service/Bot.Infrastructure/"]
COPY ["services/bot-service/Bot.Worker/Bot.Worker.csproj", "services/bot-service/Bot.Worker/"]
RUN dotnet restore "services/bot-service/Bot.Worker/Bot.Worker.csproj"

COPY ["contracts/dotnet/RealtimePix.Contracts/", "contracts/dotnet/RealtimePix.Contracts/"]
COPY ["building-blocks/dotnet/RealtimePix.Eventing/", "building-blocks/dotnet/RealtimePix.Eventing/"]
COPY ["services/bot-service/", "services/bot-service/"]
RUN dotnet publish "services/bot-service/Bot.Worker/Bot.Worker.csproj" \
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
ENTRYPOINT ["dotnet", "Bot.Worker.dll"]
