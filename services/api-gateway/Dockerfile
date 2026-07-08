FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["global.json", "."]
COPY ["building-blocks/dotnet/RealtimePix.Eventing/RealtimePix.Eventing.csproj", "building-blocks/dotnet/RealtimePix.Eventing/"]
COPY ["services/api-gateway/ApiGateway.csproj", "services/api-gateway/"]
RUN dotnet restore "services/api-gateway/ApiGateway.csproj"

COPY ["building-blocks/dotnet/RealtimePix.Eventing/", "building-blocks/dotnet/RealtimePix.Eventing/"]
COPY ["services/api-gateway/", "services/api-gateway/"]
RUN dotnet publish "services/api-gateway/ApiGateway.csproj" \
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
ENTRYPOINT ["dotnet", "ApiGateway.dll"]
