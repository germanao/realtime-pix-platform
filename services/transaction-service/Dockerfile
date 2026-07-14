FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]
COPY ["contracts/dotnet/RealtimePix.Contracts/RealtimePix.Contracts.csproj", "contracts/dotnet/RealtimePix.Contracts/"]
COPY ["building-blocks/dotnet/RealtimePix.Eventing/RealtimePix.Eventing.csproj", "building-blocks/dotnet/RealtimePix.Eventing/"]
COPY ["building-blocks/dotnet/RealtimePix.Persistence/RealtimePix.Persistence.csproj", "building-blocks/dotnet/RealtimePix.Persistence/"]
COPY ["services/transaction-service/Transaction.Domain/Transaction.Domain.csproj", "services/transaction-service/Transaction.Domain/"]
COPY ["services/transaction-service/Transaction.Application/Transaction.Application.csproj", "services/transaction-service/Transaction.Application/"]
COPY ["services/transaction-service/Transaction.Infrastructure/Transaction.Infrastructure.csproj", "services/transaction-service/Transaction.Infrastructure/"]
COPY ["services/transaction-service/Transaction.Api/Transaction.Api.csproj", "services/transaction-service/Transaction.Api/"]
RUN dotnet restore "services/transaction-service/Transaction.Api/Transaction.Api.csproj"

COPY ["contracts/dotnet/RealtimePix.Contracts/", "contracts/dotnet/RealtimePix.Contracts/"]
COPY ["building-blocks/dotnet/RealtimePix.Eventing/", "building-blocks/dotnet/RealtimePix.Eventing/"]
COPY ["building-blocks/dotnet/RealtimePix.Persistence/", "building-blocks/dotnet/RealtimePix.Persistence/"]
COPY ["services/transaction-service/", "services/transaction-service/"]
RUN dotnet publish "services/transaction-service/Transaction.Api/Transaction.Api.csproj" \
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
ENTRYPOINT ["dotnet", "Transaction.Api.dll"]
