#!/usr/bin/env bash
set -euo pipefail

: "${RG:?Set RG.}"
: "${SERVICEBUS_NAMESPACE:?Set SERVICEBUS_NAMESPACE.}"
: "${TOPIC:=platform-events}"

for subscription in wallet-ledger transaction realtime-events; do
  echo "Removing default TrueFilter rule from ${subscription}, if present..."
  az servicebus topic subscription rule delete \
    --resource-group "${RG}" \
    --namespace-name "${SERVICEBUS_NAMESPACE}" \
    --topic-name "${TOPIC}" \
    --subscription-name "${subscription}" \
    --name "\$Default" \
    --only-show-errors || true
done

echo "Default Service Bus rules removed where they existed."
