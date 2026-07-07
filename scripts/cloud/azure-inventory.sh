#!/usr/bin/env bash
set -euo pipefail

# Read-only inventory for the manually created Azure POC.
# Run from Azure Cloud Shell or any shell with az authenticated.

RG="${RG:-rg-realtime-pix-poc}"
TFSTATE_RG="${TFSTATE_RG:-rg-realtime-pix-tfstate}"
OUT_ROOT="${OUT_ROOT:-work/azure-inventory}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT_DIR="${OUT_ROOT}/${STAMP}"

mkdir -p "${OUT_DIR}"

echo "Writing inventory to ${OUT_DIR}"
az account show --output json > "${OUT_DIR}/account.json"

if az group show --name "${RG}" --output none 2>/dev/null; then
  RG_SCOPE="$(az group show --name "${RG}" --query id -o tsv)"
  az group show --name "${RG}" --output json > "${OUT_DIR}/resource-group.json"
  az resource list --resource-group "${RG}" --output json > "${OUT_DIR}/resources.json"
  az role assignment list --scope "${RG_SCOPE}" --include-inherited --output json > "${OUT_DIR}/rbac-resource-group.json"
else
  echo "Resource group ${RG} was not found." > "${OUT_DIR}/resource-group-missing.txt"
fi

if az group show --name "${TFSTATE_RG}" --output none 2>/dev/null; then
  TFSTATE_RG_SCOPE="$(az group show --name "${TFSTATE_RG}" --query id -o tsv)"
  az group show --name "${TFSTATE_RG}" --output json > "${OUT_DIR}/tfstate-resource-group.json"
  az resource list --resource-group "${TFSTATE_RG}" --output json > "${OUT_DIR}/tfstate-resources.json"
  az role assignment list --scope "${TFSTATE_RG_SCOPE}" --include-inherited --output json > "${OUT_DIR}/rbac-tfstate-resource-group.json"
fi

az postgres flexible-server list --resource-group "${RG}" --output json > "${OUT_DIR}/postgres-servers.json" 2>/dev/null || true
for server in $(az postgres flexible-server list --resource-group "${RG}" --query "[].name" -o tsv 2>/dev/null || true); do
  az postgres flexible-server db list --resource-group "${RG}" --server-name "${server}" --output json > "${OUT_DIR}/postgres-${server}-databases.json" || true
  az postgres flexible-server firewall-rule list --resource-group "${RG}" --name "${server}" --output json > "${OUT_DIR}/postgres-${server}-firewall.json" || true
done

az servicebus namespace list --resource-group "${RG}" --output json > "${OUT_DIR}/servicebus-namespaces.json" 2>/dev/null || true
for ns in $(az servicebus namespace list --resource-group "${RG}" --query "[].name" -o tsv 2>/dev/null || true); do
  az servicebus topic list --resource-group "${RG}" --namespace-name "${ns}" --output json > "${OUT_DIR}/servicebus-${ns}-topics.json" || true
  for topic in $(az servicebus topic list --resource-group "${RG}" --namespace-name "${ns}" --query "[].name" -o tsv 2>/dev/null || true); do
    az servicebus topic subscription list --resource-group "${RG}" --namespace-name "${ns}" --topic-name "${topic}" --output json > "${OUT_DIR}/servicebus-${ns}-${topic}-subscriptions.json" || true
    for sub in $(az servicebus topic subscription list --resource-group "${RG}" --namespace-name "${ns}" --topic-name "${topic}" --query "[].name" -o tsv 2>/dev/null || true); do
      az servicebus topic subscription rule list --resource-group "${RG}" --namespace-name "${ns}" --topic-name "${topic}" --subscription-name "${sub}" --output json > "${OUT_DIR}/servicebus-${ns}-${topic}-${sub}-rules.json" || true
    done
  done
done

az keyvault list --resource-group "${RG}" --output json > "${OUT_DIR}/keyvaults.json" 2>/dev/null || true
for vault in $(az keyvault list --resource-group "${RG}" --query "[].name" -o tsv 2>/dev/null || true); do
  az keyvault secret list --vault-name "${vault}" --query "[].{name:name,enabled:attributes.enabled,created:attributes.created,updated:attributes.updated}" --output json > "${OUT_DIR}/keyvault-${vault}-secret-names.json" || true
done
az keyvault list-deleted --output json > "${OUT_DIR}/keyvaults-deleted-subscription.json" 2>/dev/null || true

az appconfig list --resource-group "${RG}" --output json > "${OUT_DIR}/appconfig-stores.json" 2>/dev/null || true
for store in $(az appconfig list --resource-group "${RG}" --query "[].name" -o tsv 2>/dev/null || true); do
  az appconfig kv list --name "${store}" --auth-mode login --query "[].{key:key,label:label,content_type:content_type}" --output json > "${OUT_DIR}/appconfig-${store}-keys.json" || true
done

az acr list --resource-group "${RG}" --output json > "${OUT_DIR}/acr-registries.json" 2>/dev/null || true
az containerapp env list --resource-group "${RG}" --output json > "${OUT_DIR}/containerapp-environments.json" 2>/dev/null || true
az containerapp list --resource-group "${RG}" --output json > "${OUT_DIR}/containerapps.json" 2>/dev/null || true
az signalr list --resource-group "${RG}" --output json > "${OUT_DIR}/signalr-services.json" 2>/dev/null || true
az monitor log-analytics workspace list --resource-group "${RG}" --output json > "${OUT_DIR}/log-analytics-workspaces.json" 2>/dev/null || true
az monitor app-insights component show --resource-group "${RG}" --app "*" --output json > "${OUT_DIR}/application-insights-components.json" 2>/dev/null || true
az apim list --resource-group "${RG}" --output json > "${OUT_DIR}/apim-services.json" 2>/dev/null || true
az notification-hub namespace list --resource-group "${RG}" --output json > "${OUT_DIR}/notification-hub-namespaces.json" 2>/dev/null || true
az consumption budget list --output json > "${OUT_DIR}/subscription-budgets.json" 2>/dev/null || true

az ad app list --all --query "[?contains(displayName, 'realtime') || contains(displayName, 'pix') || contains(displayName, 'RealtimePix')].{displayName:displayName,appId:appId,id:id}" --output json > "${OUT_DIR}/entra-applications-matching-project.json" 2>/dev/null || true
az identity list --resource-group "${RG}" --output json > "${OUT_DIR}/managed-identities.json" 2>/dev/null || true

echo "${OUT_DIR}" > "${OUT_ROOT}/latest.txt"
echo "Inventory complete: ${OUT_DIR}"
