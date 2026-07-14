#!/usr/bin/env bash
set -euo pipefail

: "${RG:?Set RG to the production resource group.}"
: "${SIGNALR_NAME:?Set SIGNALR_NAME to the production SignalR service.}"

mapfile -t private_connections < <(
  az signalr network-rule list \
    --resource-group "${RG}" \
    --name "${SIGNALR_NAME}" \
    --query 'privateEndpoints[].name' \
    --output tsv
)

if [[ ${#private_connections[@]} -eq 0 ]]; then
  echo "No approved SignalR private endpoint connection exists." >&2
  exit 1
fi

az signalr update \
  --resource-group "${RG}" \
  --name "${SIGNALR_NAME}" \
  --default-action Deny \
  --only-show-errors \
  --output none

az signalr network-rule update \
  --resource-group "${RG}" \
  --name "${SIGNALR_NAME}" \
  --public-network true \
  --allow ClientConnection \
  --deny ServerConnection RESTAPI Trace \
  --only-show-errors \
  --output none

for connection in "${private_connections[@]}"; do
  az signalr network-rule update \
    --resource-group "${RG}" \
    --name "${SIGNALR_NAME}" \
    --connection-name "${connection}" \
    --allow ServerConnection RESTAPI Trace \
    --deny ClientConnection \
    --only-show-errors \
    --output none
done

rules="$(az signalr network-rule list --resource-group "${RG}" --name "${SIGNALR_NAME}" --output json)"
jq -e '
  .defaultAction == "Deny" and
  (.publicNetwork.allow | sort) == (["ClientConnection"] | sort) and
  (.publicNetwork.deny | sort) == (["RESTAPI", "ServerConnection", "Trace"] | sort) and
  all(.privateEndpoints[];
    (.allow | sort) == (["RESTAPI", "ServerConnection", "Trace"] | sort) and
    (.deny | sort) == (["ClientConnection"] | sort)
  )
' <<< "${rules}" >/dev/null

echo "SignalR request-type ACLs are configured and verified."
