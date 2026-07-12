#!/usr/bin/env bash
set -euo pipefail

: "${AZURE_SUBSCRIPTION_ID:?Set AZURE_SUBSCRIPTION_ID.}"

RESOURCE_GROUP="$(terraform output -raw resource_group_name 2>/dev/null || true)"
SERVICEBUS_NAMESPACE="$(terraform output -raw servicebus_namespace_name 2>/dev/null || true)"

if [[ -z "$RESOURCE_GROUP" || -z "$SERVICEBUS_NAMESPACE" ]]; then
  echo "Skipping realtime Service Bus rule import because foundation outputs are not available yet."
  exit 0
fi

RULE_ID="/subscriptions/${AZURE_SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}/providers/Microsoft.ServiceBus/namespaces/${SERVICEBUS_NAMESPACE}/topics/platform-events/subscriptions/realtime-events/rules/event-type-filter"
ADDRESS='azurerm_servicebus_subscription_rule.consumer_filters["realtime-events"]'

set +e
IMPORT_OUTPUT="$(terraform import "$ADDRESS" "$RULE_ID" 2>&1)"
IMPORT_STATUS=$?
set -e

if [[ $IMPORT_STATUS -eq 0 ]]; then
  echo "Imported realtime Service Bus rule into Terraform state."
  exit 0
fi

if grep -qiE "already managed|Cannot import non-existent remote object|Cannot import non-existent" <<<"$IMPORT_OUTPUT"; then
  echo "Realtime Service Bus rule import skipped: ${IMPORT_OUTPUT}"
  exit 0
fi

echo "$IMPORT_OUTPUT"
exit "$IMPORT_STATUS"
