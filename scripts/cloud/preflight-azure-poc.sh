#!/usr/bin/env bash
set -euo pipefail

LOCATION="${LOCATION:-brazilsouth}"

az account show --output table

for provider in Microsoft.App Microsoft.ContainerRegistry Microsoft.DBforPostgreSQL Microsoft.ServiceBus Microsoft.SignalRService Microsoft.KeyVault Microsoft.AppConfiguration Microsoft.ApiManagement Microsoft.NotificationHubs Microsoft.Insights; do
  state="$(az provider show --namespace "${provider}" --query registrationState -o tsv 2>/dev/null || true)"
  echo "${provider}: ${state:-not registered}"
done

echo "Checking representative SKUs in ${LOCATION}..."
az vm list-skus --location "${LOCATION}" --resource-type "flexibleServers" --query "[?contains(name, 'B_Standard_B1ms')].{name:name,locations:locations}" -o table || true
az acr check-name --name "acrpreflight$(date +%s)" --output table || true
az containerapp env list --output none || true

echo "Preflight complete. If a provider is NotRegistered, register it before Terraform:"
echo "  az provider register --namespace Microsoft.App"
