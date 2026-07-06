# Azure Scripts for Runbook Steps 16.3 to 26

These scripts are intended for Azure Cloud Shell Bash unless a section says
otherwise. Paste one block at a time.

Production Dockerfiles are now available for all six services. The repo does not
yet contain EF Core migrations/PostgreSQL adapters, so Step 22 remains a
template until those implementation pieces exist.

## Common Variables

```bash
export RG="rg-realtime-pix-poc"
export LOCATION="brazilsouth"
export CAE_NAME="cae-realtime-pix-poc"
export LOG_WORKSPACE="log-realtime-pix-poc"
export APPINSIGHTS_NAME="appi-realtime-pix-poc"
export KEYVAULT_NAME="kv-realtime-pix-<unique>"
export SERVICEBUS_TOPIC="platform-events"
export APPCONFIG_LABEL="poc"

export ACR_NAME="$(az acr list --resource-group "$RG" --query "[0].name" --output tsv)"
export ACR_LOGIN_SERVER="$(az acr show --resource-group "$RG" --name "$ACR_NAME" --query loginServer --output tsv)"
export SERVICEBUS_NAMESPACE="$(az servicebus namespace list --resource-group "$RG" --query "[0].name" --output tsv)"
export SERVICEBUS_FQDN="${SERVICEBUS_NAMESPACE}.servicebus.windows.net"
export APPCONFIG_NAME="$(az appconfig list --resource-group "$RG" --query "[0].name" --output tsv)"
export APPCONFIG_ENDPOINT="https://${APPCONFIG_NAME}.azconfig.io"
export SIGNALR_NAME="$(az signalr list --resource-group "$RG" --query "[0].name" --output tsv)"
export APPINSIGHTS_CONNECTION_STRING="$(az monitor app-insights component show --resource-group "$RG" --app "$APPINSIGHTS_NAME" --query connectionString --output tsv)"

export IMAGE_TAG="${IMAGE_TAG:-manual-$(date +%Y%m%d%H%M%S)}"

echo "ACR=$ACR_LOGIN_SERVER"
echo "ServiceBus=$SERVICEBUS_FQDN"
echo "AppConfig=$APPCONFIG_ENDPOINT"
echo "SignalR=$SIGNALR_NAME"
echo "Image tag=$IMAGE_TAG"
```

## 16.3 Required Service Names

Use these names as `OTEL_SERVICE_NAME` values when creating/updating the
Container Apps:

```bash
export OTEL_API_GATEWAY="realtime-pix-api-gateway"
export OTEL_IDENTITY="realtime-pix-identity-presence"
export OTEL_WALLET="realtime-pix-wallet-ledger"
export OTEL_TRANSACTION="realtime-pix-transaction"
export OTEL_REALTIME="realtime-pix-realtime-events"
export OTEL_BOT="realtime-pix-bot"
```

## 17. Azure Container Apps Environment

```bash
az extension add --name containerapp --upgrade
az provider register --namespace Microsoft.App
az provider register --namespace Microsoft.OperationalInsights
```

```bash
export LOG_WORKSPACE_CUSTOMER_ID="$(az monitor log-analytics workspace show --resource-group "$RG" --workspace-name "$LOG_WORKSPACE" --query customerId --output tsv)"
export LOG_WORKSPACE_SHARED_KEY="$(az monitor log-analytics workspace get-shared-keys --resource-group "$RG" --workspace-name "$LOG_WORKSPACE" --query primarySharedKey --output tsv)"
```

```bash
az containerapp env create \
  --name "$CAE_NAME" \
  --resource-group "$RG" \
  --location "$LOCATION" \
  --logs-workspace-id "$LOG_WORKSPACE_CUSTOMER_ID" \
  --logs-workspace-key "$LOG_WORKSPACE_SHARED_KEY"
```

```bash
az containerapp env show \
  --name "$CAE_NAME" \
  --resource-group "$RG" \
  --query "{Name:name,ProvisioningState:properties.provisioningState,Location:location}" \
  --output table
```

## 18. Build and Push Six Images to ACR

Run from the repository root after Dockerfiles exist.

```bash
test -f services/api-gateway/Dockerfile
test -f services/identity-presence-service/Dockerfile
test -f services/wallet-ledger-service/Dockerfile
test -f services/transaction-service/Dockerfile
test -f services/realtime-events-service/Dockerfile
test -f services/bot-service/Dockerfile
```

```bash
az acr build --registry "$ACR_NAME" --image "pix-api-gateway:$IMAGE_TAG" --file services/api-gateway/Dockerfile .
az acr build --registry "$ACR_NAME" --image "pix-identity-presence:$IMAGE_TAG" --file services/identity-presence-service/Dockerfile .
az acr build --registry "$ACR_NAME" --image "pix-wallet-ledger:$IMAGE_TAG" --file services/wallet-ledger-service/Dockerfile .
az acr build --registry "$ACR_NAME" --image "pix-transaction:$IMAGE_TAG" --file services/transaction-service/Dockerfile .
az acr build --registry "$ACR_NAME" --image "pix-realtime-events:$IMAGE_TAG" --file services/realtime-events-service/Dockerfile .
az acr build --registry "$ACR_NAME" --image "pix-bot:$IMAGE_TAG" --file services/bot-service/Dockerfile .
```

```bash
az acr repository list --name "$ACR_NAME" --output table
```

## 19. Resource Scopes for Managed Identity RBAC

```bash
export ACR_ID="$(az acr show --resource-group "$RG" --name "$ACR_NAME" --query id --output tsv)"
export KEYVAULT_ID="$(az keyvault show --resource-group "$RG" --name "$KEYVAULT_NAME" --query id --output tsv)"
export APPCONFIG_ID="$(az appconfig show --resource-group "$RG" --name "$APPCONFIG_NAME" --query id --output tsv)"
export SB_TOPIC_ID="$(az servicebus topic show --resource-group "$RG" --namespace-name "$SERVICEBUS_NAMESPACE" --name "$SERVICEBUS_TOPIC" --query id --output tsv)"
export SB_WALLET_SUB_ID="$(az servicebus topic subscription show --resource-group "$RG" --namespace-name "$SERVICEBUS_NAMESPACE" --topic-name "$SERVICEBUS_TOPIC" --name wallet-ledger --query id --output tsv)"
export SB_TRANSACTION_SUB_ID="$(az servicebus topic subscription show --resource-group "$RG" --namespace-name "$SERVICEBUS_NAMESPACE" --topic-name "$SERVICEBUS_TOPIC" --name transaction --query id --output tsv)"
export SB_REALTIME_SUB_ID="$(az servicebus topic subscription show --resource-group "$RG" --namespace-name "$SERVICEBUS_NAMESPACE" --topic-name "$SERVICEBUS_TOPIC" --name realtime-events --query id --output tsv)"
```

## 20. Create Six Container Apps

This creates the apps with a public placeholder image so the system-assigned
managed identities exist. After step 21 grants ACR pull, update them to the real
ACR images.

```bash
az containerapp create --name pix-api-gateway --resource-group "$RG" --environment "$CAE_NAME" --image mcr.microsoft.com/k8se/quickstart:latest --ingress external --target-port 80 --min-replicas 0 --max-replicas 2 --cpu 0.25 --memory 0.5Gi --system-assigned --env-vars ASPNETCORE_URLS=http://+:8080 DOTNET_ENVIRONMENT=Production EventBus__Provider=ServiceBus ServiceBus__FullyQualifiedNamespace="$SERVICEBUS_FQDN" ServiceBus__TopicName="$SERVICEBUS_TOPIC" AppConfig__Endpoint="$APPCONFIG_ENDPOINT" APPLICATIONINSIGHTS_CONNECTION_STRING="$APPINSIGHTS_CONNECTION_STRING" OTEL_SERVICE_NAME="$OTEL_API_GATEWAY" Services__IdentityPresence=http://pix-identity-presence Services__WalletLedger=http://pix-wallet-ledger Services__Transaction=http://pix-transaction Services__RealtimeEvents=http://pix-realtime-events
```

```bash
az containerapp create --name pix-identity-presence --resource-group "$RG" --environment "$CAE_NAME" --image mcr.microsoft.com/k8se/quickstart:latest --ingress external --target-port 80 --min-replicas 1 --max-replicas 2 --cpu 0.25 --memory 0.5Gi --system-assigned --env-vars ASPNETCORE_URLS=http://+:8080 DOTNET_ENVIRONMENT=Production EventBus__Provider=ServiceBus ServiceBus__FullyQualifiedNamespace="$SERVICEBUS_FQDN" ServiceBus__TopicName="$SERVICEBUS_TOPIC" AppConfig__Endpoint="$APPCONFIG_ENDPOINT" APPLICATIONINSIGHTS_CONNECTION_STRING="$APPINSIGHTS_CONNECTION_STRING" OTEL_SERVICE_NAME="$OTEL_IDENTITY" Persistence__Provider=Postgres Realtime__Provider=AzureSignalR Azure__SignalR__ApplicationName=realtime-pix-presence
```

```bash
az containerapp create --name pix-wallet-ledger --resource-group "$RG" --environment "$CAE_NAME" --image mcr.microsoft.com/k8se/quickstart:latest --ingress internal --target-port 80 --min-replicas 0 --max-replicas 2 --cpu 0.25 --memory 0.5Gi --system-assigned --env-vars ASPNETCORE_URLS=http://+:8080 DOTNET_ENVIRONMENT=Production EventBus__Provider=ServiceBus ServiceBus__FullyQualifiedNamespace="$SERVICEBUS_FQDN" ServiceBus__TopicName="$SERVICEBUS_TOPIC" ServiceBus__SubscriptionName=wallet-ledger AppConfig__Endpoint="$APPCONFIG_ENDPOINT" APPLICATIONINSIGHTS_CONNECTION_STRING="$APPINSIGHTS_CONNECTION_STRING" OTEL_SERVICE_NAME="$OTEL_WALLET" Persistence__Provider=Postgres
```

```bash
az containerapp create --name pix-transaction --resource-group "$RG" --environment "$CAE_NAME" --image mcr.microsoft.com/k8se/quickstart:latest --ingress internal --target-port 80 --min-replicas 0 --max-replicas 2 --cpu 0.25 --memory 0.5Gi --system-assigned --env-vars ASPNETCORE_URLS=http://+:8080 DOTNET_ENVIRONMENT=Production EventBus__Provider=ServiceBus ServiceBus__FullyQualifiedNamespace="$SERVICEBUS_FQDN" ServiceBus__TopicName="$SERVICEBUS_TOPIC" ServiceBus__SubscriptionName=transaction AppConfig__Endpoint="$APPCONFIG_ENDPOINT" APPLICATIONINSIGHTS_CONNECTION_STRING="$APPINSIGHTS_CONNECTION_STRING" OTEL_SERVICE_NAME="$OTEL_TRANSACTION" Persistence__Provider=Postgres
```

```bash
az containerapp create --name pix-realtime-events --resource-group "$RG" --environment "$CAE_NAME" --image mcr.microsoft.com/k8se/quickstart:latest --ingress external --target-port 80 --min-replicas 1 --max-replicas 2 --cpu 0.25 --memory 0.5Gi --system-assigned --env-vars ASPNETCORE_URLS=http://+:8080 DOTNET_ENVIRONMENT=Production EventBus__Provider=ServiceBus ServiceBus__FullyQualifiedNamespace="$SERVICEBUS_FQDN" ServiceBus__TopicName="$SERVICEBUS_TOPIC" ServiceBus__SubscriptionName=realtime-events AppConfig__Endpoint="$APPCONFIG_ENDPOINT" APPLICATIONINSIGHTS_CONNECTION_STRING="$APPINSIGHTS_CONNECTION_STRING" OTEL_SERVICE_NAME="$OTEL_REALTIME" Persistence__Provider=Postgres Realtime__Provider=AzureSignalR Azure__SignalR__ApplicationName=realtime-pix-events
```

```bash
az containerapp create --name pix-bot --resource-group "$RG" --environment "$CAE_NAME" --image mcr.microsoft.com/k8se/quickstart:latest --min-replicas 1 --max-replicas 1 --cpu 0.25 --memory 0.5Gi --system-assigned --env-vars ASPNETCORE_URLS=http://+:8080 DOTNET_ENVIRONMENT=Production EventBus__Provider=ServiceBus ServiceBus__FullyQualifiedNamespace="$SERVICEBUS_FQDN" ServiceBus__TopicName="$SERVICEBUS_TOPIC" AppConfig__Endpoint="$APPCONFIG_ENDPOINT" APPLICATIONINSIGHTS_CONNECTION_STRING="$APPINSIGHTS_CONNECTION_STRING" OTEL_SERVICE_NAME="$OTEL_BOT" WalletServiceUrl=http://pix-wallet-ledger
```

## 21. Service Bus and Supporting RBAC After App Creation

Capture principal IDs:

```bash
export PID_API="$(az containerapp show --resource-group "$RG" --name pix-api-gateway --query identity.principalId --output tsv)"
export PID_IDENTITY="$(az containerapp show --resource-group "$RG" --name pix-identity-presence --query identity.principalId --output tsv)"
export PID_WALLET="$(az containerapp show --resource-group "$RG" --name pix-wallet-ledger --query identity.principalId --output tsv)"
export PID_TRANSACTION="$(az containerapp show --resource-group "$RG" --name pix-transaction --query identity.principalId --output tsv)"
export PID_REALTIME="$(az containerapp show --resource-group "$RG" --name pix-realtime-events --query identity.principalId --output tsv)"
export PID_BOT="$(az containerapp show --resource-group "$RG" --name pix-bot --query identity.principalId --output tsv)"
```

ACR pull and App Configuration read:

```bash
az role assignment create --assignee-object-id "$PID_API" --assignee-principal-type ServicePrincipal --role AcrPull --scope "$ACR_ID"
az role assignment create --assignee-object-id "$PID_IDENTITY" --assignee-principal-type ServicePrincipal --role AcrPull --scope "$ACR_ID"
az role assignment create --assignee-object-id "$PID_WALLET" --assignee-principal-type ServicePrincipal --role AcrPull --scope "$ACR_ID"
az role assignment create --assignee-object-id "$PID_TRANSACTION" --assignee-principal-type ServicePrincipal --role AcrPull --scope "$ACR_ID"
az role assignment create --assignee-object-id "$PID_REALTIME" --assignee-principal-type ServicePrincipal --role AcrPull --scope "$ACR_ID"
az role assignment create --assignee-object-id "$PID_BOT" --assignee-principal-type ServicePrincipal --role AcrPull --scope "$ACR_ID"
```

```bash
az role assignment create --assignee-object-id "$PID_API" --assignee-principal-type ServicePrincipal --role "App Configuration Data Reader" --scope "$APPCONFIG_ID"
az role assignment create --assignee-object-id "$PID_IDENTITY" --assignee-principal-type ServicePrincipal --role "App Configuration Data Reader" --scope "$APPCONFIG_ID"
az role assignment create --assignee-object-id "$PID_WALLET" --assignee-principal-type ServicePrincipal --role "App Configuration Data Reader" --scope "$APPCONFIG_ID"
az role assignment create --assignee-object-id "$PID_TRANSACTION" --assignee-principal-type ServicePrincipal --role "App Configuration Data Reader" --scope "$APPCONFIG_ID"
az role assignment create --assignee-object-id "$PID_REALTIME" --assignee-principal-type ServicePrincipal --role "App Configuration Data Reader" --scope "$APPCONFIG_ID"
az role assignment create --assignee-object-id "$PID_BOT" --assignee-principal-type ServicePrincipal --role "App Configuration Data Reader" --scope "$APPCONFIG_ID"
```

Key Vault secrets access:

```bash
az role assignment create --assignee-object-id "$PID_IDENTITY" --assignee-principal-type ServicePrincipal --role "Key Vault Secrets User" --scope "$KEYVAULT_ID"
az role assignment create --assignee-object-id "$PID_WALLET" --assignee-principal-type ServicePrincipal --role "Key Vault Secrets User" --scope "$KEYVAULT_ID"
az role assignment create --assignee-object-id "$PID_TRANSACTION" --assignee-principal-type ServicePrincipal --role "Key Vault Secrets User" --scope "$KEYVAULT_ID"
az role assignment create --assignee-object-id "$PID_REALTIME" --assignee-principal-type ServicePrincipal --role "Key Vault Secrets User" --scope "$KEYVAULT_ID"
```

Service Bus sender and receiver:

```bash
az role assignment create --assignee-object-id "$PID_IDENTITY" --assignee-principal-type ServicePrincipal --role "Azure Service Bus Data Sender" --scope "$SB_TOPIC_ID"
az role assignment create --assignee-object-id "$PID_WALLET" --assignee-principal-type ServicePrincipal --role "Azure Service Bus Data Sender" --scope "$SB_TOPIC_ID"
az role assignment create --assignee-object-id "$PID_TRANSACTION" --assignee-principal-type ServicePrincipal --role "Azure Service Bus Data Sender" --scope "$SB_TOPIC_ID"
az role assignment create --assignee-object-id "$PID_REALTIME" --assignee-principal-type ServicePrincipal --role "Azure Service Bus Data Sender" --scope "$SB_TOPIC_ID"
az role assignment create --assignee-object-id "$PID_BOT" --assignee-principal-type ServicePrincipal --role "Azure Service Bus Data Sender" --scope "$SB_TOPIC_ID"
```

```bash
az role assignment create --assignee-object-id "$PID_WALLET" --assignee-principal-type ServicePrincipal --role "Azure Service Bus Data Receiver" --scope "$SB_WALLET_SUB_ID"
az role assignment create --assignee-object-id "$PID_TRANSACTION" --assignee-principal-type ServicePrincipal --role "Azure Service Bus Data Receiver" --scope "$SB_TRANSACTION_SUB_ID"
az role assignment create --assignee-object-id "$PID_REALTIME" --assignee-principal-type ServicePrincipal --role "Azure Service Bus Data Receiver" --scope "$SB_REALTIME_SUB_ID"
```

Wait a few minutes for RBAC propagation before setting Key Vault references and
ACR images.

## 20 Post-RBAC: Key Vault References and Final Images

Store the SignalR connection string once:

```bash
export SIGNALR_CONNECTION_STRING="$(az signalr key list --resource-group "$RG" --name "$SIGNALR_NAME" --query primaryConnectionString --output tsv)"
az keyvault secret set --vault-name "$KEYVAULT_NAME" --name azure-signalr --value "$SIGNALR_CONNECTION_STRING" --output none
unset SIGNALR_CONNECTION_STRING
```

Capture secret URIs:

```bash
export SECRET_IDENTITY_DB="$(az keyvault secret show --vault-name "$KEYVAULT_NAME" --name identity-db --query id --output tsv)"
export SECRET_WALLET_DB="$(az keyvault secret show --vault-name "$KEYVAULT_NAME" --name wallet-db --query id --output tsv)"
export SECRET_TRANSACTION_DB="$(az keyvault secret show --vault-name "$KEYVAULT_NAME" --name transaction-db --query id --output tsv)"
export SECRET_REALTIME_DB="$(az keyvault secret show --vault-name "$KEYVAULT_NAME" --name realtime-db --query id --output tsv)"
export SECRET_SIGNALR="$(az keyvault secret show --vault-name "$KEYVAULT_NAME" --name azure-signalr --query id --output tsv)"
```

Set container app secrets and environment references:

```bash
az containerapp secret set --resource-group "$RG" --name pix-identity-presence --secrets "identity-db=keyvaultref:$SECRET_IDENTITY_DB,identityref:system"
az containerapp secret set --resource-group "$RG" --name pix-identity-presence --secrets "azure-signalr=keyvaultref:$SECRET_SIGNALR,identityref:system"
az containerapp update --resource-group "$RG" --name pix-identity-presence --set-env-vars ConnectionStrings__IdentityPresence=secretref:identity-db Azure__SignalR__ConnectionString=secretref:azure-signalr
```

```bash
az containerapp secret set --resource-group "$RG" --name pix-wallet-ledger --secrets "wallet-db=keyvaultref:$SECRET_WALLET_DB,identityref:system"
az containerapp update --resource-group "$RG" --name pix-wallet-ledger --set-env-vars ConnectionStrings__WalletLedger=secretref:wallet-db
```

```bash
az containerapp secret set --resource-group "$RG" --name pix-transaction --secrets "transaction-db=keyvaultref:$SECRET_TRANSACTION_DB,identityref:system"
az containerapp update --resource-group "$RG" --name pix-transaction --set-env-vars ConnectionStrings__Transaction=secretref:transaction-db
```

```bash
az containerapp secret set --resource-group "$RG" --name pix-realtime-events --secrets "realtime-db=keyvaultref:$SECRET_REALTIME_DB,identityref:system"
az containerapp secret set --resource-group "$RG" --name pix-realtime-events --secrets "azure-signalr=keyvaultref:$SECRET_SIGNALR,identityref:system"
az containerapp update --resource-group "$RG" --name pix-realtime-events --set-env-vars ConnectionStrings__RealtimeProjection=secretref:realtime-db Azure__SignalR__ConnectionString=secretref:azure-signalr
```

Set ACR registry identity and update to final images:

```bash
az containerapp registry set --resource-group "$RG" --name pix-api-gateway --server "$ACR_LOGIN_SERVER" --identity system
az containerapp registry set --resource-group "$RG" --name pix-identity-presence --server "$ACR_LOGIN_SERVER" --identity system
az containerapp registry set --resource-group "$RG" --name pix-wallet-ledger --server "$ACR_LOGIN_SERVER" --identity system
az containerapp registry set --resource-group "$RG" --name pix-transaction --server "$ACR_LOGIN_SERVER" --identity system
az containerapp registry set --resource-group "$RG" --name pix-realtime-events --server "$ACR_LOGIN_SERVER" --identity system
az containerapp registry set --resource-group "$RG" --name pix-bot --server "$ACR_LOGIN_SERVER" --identity system
```

```bash
az containerapp update --resource-group "$RG" --name pix-api-gateway --image "$ACR_LOGIN_SERVER/pix-api-gateway:$IMAGE_TAG"
az containerapp update --resource-group "$RG" --name pix-identity-presence --image "$ACR_LOGIN_SERVER/pix-identity-presence:$IMAGE_TAG"
az containerapp update --resource-group "$RG" --name pix-wallet-ledger --image "$ACR_LOGIN_SERVER/pix-wallet-ledger:$IMAGE_TAG"
az containerapp update --resource-group "$RG" --name pix-transaction --image "$ACR_LOGIN_SERVER/pix-transaction:$IMAGE_TAG"
az containerapp update --resource-group "$RG" --name pix-realtime-events --image "$ACR_LOGIN_SERVER/pix-realtime-events:$IMAGE_TAG"
az containerapp update --resource-group "$RG" --name pix-bot --image "$ACR_LOGIN_SERVER/pix-bot:$IMAGE_TAG"
```

Record public FQDNs:

```bash
export API_GATEWAY_FQDN="$(az containerapp show --resource-group "$RG" --name pix-api-gateway --query properties.configuration.ingress.fqdn --output tsv)"
export IDENTITY_FQDN="$(az containerapp show --resource-group "$RG" --name pix-identity-presence --query properties.configuration.ingress.fqdn --output tsv)"
export REALTIME_FQDN="$(az containerapp show --resource-group "$RG" --name pix-realtime-events --query properties.configuration.ingress.fqdn --output tsv)"

echo "API=https://$API_GATEWAY_FQDN"
echo "PresenceHub=https://$IDENTITY_FQDN/presence/hub"
echo "EventsHub=https://$REALTIME_FQDN/events/hub"
```

## 22. Run PostgreSQL Migrations

Run from a machine/agent with the repo, .NET SDK, `dotnet-ef`, EF Core packages,
and migrations already implemented.

```bash
dotnet tool restore
```

```bash
export Persistence__Provider="Postgres"
export ConnectionStrings__IdentityPresence="$(az keyvault secret show --vault-name "$KEYVAULT_NAME" --name identity-db --query value --output tsv)"
dotnet ef database update --project services/identity-presence-service --startup-project services/identity-presence-service --configuration Release
unset ConnectionStrings__IdentityPresence
```

```bash
export ConnectionStrings__WalletLedger="$(az keyvault secret show --vault-name "$KEYVAULT_NAME" --name wallet-db --query value --output tsv)"
dotnet ef database update --project services/wallet-ledger-service --startup-project services/wallet-ledger-service --configuration Release
unset ConnectionStrings__WalletLedger
```

```bash
export ConnectionStrings__Transaction="$(az keyvault secret show --vault-name "$KEYVAULT_NAME" --name transaction-db --query value --output tsv)"
dotnet ef database update --project services/transaction-service --startup-project services/transaction-service --configuration Release
unset ConnectionStrings__Transaction
```

```bash
export ConnectionStrings__RealtimeProjection="$(az keyvault secret show --vault-name "$KEYVAULT_NAME" --name realtime-db --query value --output tsv)"
dotnet ef database update --project services/realtime-events-service --startup-project services/realtime-events-service --configuration Release
unset ConnectionStrings__RealtimeProjection
unset Persistence__Provider
```

## 23.5 Azure DevOps Variable Group

Azure DevOps CLI can create a protected variable group. Key Vault-linked
variable groups are easiest to create in the Portal UI.

```bash
az extension add --name azure-devops --upgrade
export AZDO_ORG="https://dev.azure.com/<your-org>"
export AZDO_PROJECT="RealtimePixPlatform"
az devops configure --defaults organization="$AZDO_ORG" project="$AZDO_PROJECT"
```

```bash
export VERCEL_TOKEN="$(az keyvault secret show --vault-name "$KEYVAULT_NAME" --name vercel-deployment-token --query value --output tsv)"
export VERCEL_GROUP_ID="$(az pipelines variable-group create --name vercel-realtime-pix --variables vercel-deployment-token=placeholder --authorize false --query id --output tsv)"
az pipelines variable-group variable update --group-id "$VERCEL_GROUP_ID" --name vercel-deployment-token --secret true --value "$VERCEL_TOKEN"
unset VERCEL_TOKEN
```

## 23.6 Vercel Public Frontend Environment Variables

Run locally or in an agent with Node.js.

```bash
export NEXT_PUBLIC_API_BASE_URL="https://<apim-name>.azure-api.net"
export NEXT_PUBLIC_PRESENCE_HUB_URL="https://${IDENTITY_FQDN}/presence/hub"
export NEXT_PUBLIC_EVENTS_HUB_URL="https://${REALTIME_FQDN}/events/hub"
export VERCEL_TOKEN="$(az keyvault secret show --vault-name "$KEYVAULT_NAME" --name vercel-deployment-token --query value --output tsv)"
```

```bash
cd apps/web
npx vercel link --yes --project realtime-pix-web --token "$VERCEL_TOKEN"
printf "%s" "$NEXT_PUBLIC_API_BASE_URL" | npx vercel env add NEXT_PUBLIC_API_BASE_URL production --token "$VERCEL_TOKEN"
printf "%s" "$NEXT_PUBLIC_PRESENCE_HUB_URL" | npx vercel env add NEXT_PUBLIC_PRESENCE_HUB_URL production --token "$VERCEL_TOKEN"
printf "%s" "$NEXT_PUBLIC_EVENTS_HUB_URL" | npx vercel env add NEXT_PUBLIC_EVENTS_HUB_URL production --token "$VERCEL_TOKEN"
cd ../..
unset VERCEL_TOKEN
```

## 23.7 Vercel Deployment Commands

```bash
export VERCEL_TOKEN="$(az keyvault secret show --vault-name "$KEYVAULT_NAME" --name vercel-deployment-token --query value --output tsv)"
cd apps/web
npm ci
npm test
npm run build
npx vercel pull --yes --environment=production --token "$VERCEL_TOKEN"
npx vercel build --prod --token "$VERCEL_TOKEN"
npx vercel deploy --prebuilt --prod --token "$VERCEL_TOKEN"
cd ../..
unset VERCEL_TOKEN
```

## 23.8 Configure CORS

The current .NET code allows broad CORS and does not read CORS settings from
configuration yet. After implementing config-driven CORS, use:

```bash
export VERCEL_ORIGIN="https://realtime-pix-web.vercel.app"
az containerapp update --resource-group "$RG" --name pix-api-gateway --set-env-vars Cors__AllowedOrigins__0="$VERCEL_ORIGIN"
az containerapp update --resource-group "$RG" --name pix-identity-presence --set-env-vars Cors__AllowedOrigins__0="$VERCEL_ORIGIN"
az containerapp update --resource-group "$RG" --name pix-realtime-events --set-env-vars Cors__AllowedOrigins__0="$VERCEL_ORIGIN"
```

## 23.9 Validate

```bash
curl -I "https://$API_GATEWAY_FQDN/health"
curl -I "https://$IDENTITY_FQDN/health"
curl -I "https://$REALTIME_FQDN/health"
```

After Vercel deployment:

```bash
curl -I "https://realtime-pix-web.vercel.app"
```

## 24. Azure API Management Consumption

```bash
export APIM_NAME="apim-realtime-pix-5312"
export PUBLISHER_EMAIL="<your-email>"
export PUBLISHER_NAME="Realtime PIX"
```

```bash
az apim create \
  --resource-group "$RG" \
  --name "$APIM_NAME" \
  --location "$LOCATION" \
  --publisher-email "$PUBLISHER_EMAIL" \
  --publisher-name "$PUBLISHER_NAME" \
  --sku-name Consumption
```

```bash
export API_GATEWAY_URL="https://${API_GATEWAY_FQDN}"

az apim api create \
  --resource-group "$RG" \
  --service-name "$APIM_NAME" \
  --api-id realtime-pix-api \
  --display-name "Realtime PIX API" \
  --path "" \
  --protocols https \
  --service-url "$API_GATEWAY_URL"
```

Add the first health operation:

```bash
az apim api operation create \
  --resource-group "$RG" \
  --service-name "$APIM_NAME" \
  --api-id realtime-pix-api \
  --operation-id health \
  --display-name "Health" \
  --method GET \
  --url-template "/health"
```

Validate:

```bash
curl -i "https://${APIM_NAME}.azure-api.net/health"
```

## 25. Azure Notification Hubs

Install the extension if needed:

```bash
az extension add --name notification-hub --upgrade
```

```bash
export NH_NAMESPACE="nhns-realtime-pix-5312"
export NH_HUB="notification-realtime-pix"
```

```bash
az notification-hub namespace create \
  --resource-group "$RG" \
  --name "$NH_NAMESPACE" \
  --location "$LOCATION" \
  --sku Free
```

```bash
az notification-hub create \
  --resource-group "$RG" \
  --namespace-name "$NH_NAMESPACE" \
  --name "$NH_HUB" \
  --location "$LOCATION"
```

## 26. Azure Pipelines CI/CD

Create or update `azure-pipelines.yml` after Dockerfiles, EF migrations, cloud
adapters, and Vercel settings exist. Minimal creation command:

```bash
az extension add --name azure-devops --upgrade
export AZDO_ORG="https://dev.azure.com/<your-org>"
export AZDO_PROJECT="RealtimePixPlatform"
az devops configure --defaults organization="$AZDO_ORG" project="$AZDO_PROJECT"
```

```bash
az pipelines create \
  --name realtime-pix-ci-cd \
  --repository RealtimePixPlatform \
  --branch main \
  --yml-path azure-pipelines.yml \
  --skip-first-run false
```

Pipeline stages to encode in YAML:

```text
1. dotnet restore/build/test
2. npm ci/test/build for apps/web
3. az acr build six images with $(Build.SourceVersion)
4. deploy/update six Container Apps
5. run EF migrations as controlled jobs
6. deploy Vercel frontend
7. run smoke tests
```
