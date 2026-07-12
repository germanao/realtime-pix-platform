#!/usr/bin/env bash
set -euo pipefail

: "${RG:?Set RG to the Azure resource group name.}"
: "${SERVICEBUS_NAMESPACE:?Set SERVICEBUS_NAMESPACE to the Azure Service Bus namespace name.}"

TOPIC_NAME="${TOPIC_NAME:-platform-events}"
SUBSCRIPTION_NAME="${SUBSCRIPTION_NAME:-realtime-events}"
FILTER_SQL="${FILTER_SQL:-eventType IN ('AnonymousUserJoined.v1', 'UserPresenceChanged.v1', 'AccountCreated.v1', 'FundsDeposited.v1', 'PixTransferRequested.v1', 'PixDebitSucceeded.v1', 'PixDebitFailed.v1', 'PixCreditSucceeded.v1', 'PixTransferCompleted.v1', 'PixTransferFailed.v1', 'ArchitectureFlowStepRecorded.v1')}"

echo "Resetting derived realtime projection subscription ${SERVICEBUS_NAMESPACE}/${TOPIC_NAME}/${SUBSCRIPTION_NAME}."

if az servicebus topic subscription show \
  --resource-group "$RG" \
  --namespace-name "$SERVICEBUS_NAMESPACE" \
  --topic-name "$TOPIC_NAME" \
  --name "$SUBSCRIPTION_NAME" \
  --only-show-errors \
  --output none >/dev/null 2>&1; then
  az servicebus topic subscription delete \
    --resource-group "$RG" \
    --namespace-name "$SERVICEBUS_NAMESPACE" \
    --topic-name "$TOPIC_NAME" \
    --name "$SUBSCRIPTION_NAME" \
    --only-show-errors \
    --output none
fi

az servicebus topic subscription create \
  --resource-group "$RG" \
  --namespace-name "$SERVICEBUS_NAMESPACE" \
  --topic-name "$TOPIC_NAME" \
  --name "$SUBSCRIPTION_NAME" \
  --max-delivery-count 10 \
  --default-message-time-to-live P14D \
  --only-show-errors \
  --output none

az servicebus topic subscription rule delete \
  --resource-group "$RG" \
  --namespace-name "$SERVICEBUS_NAMESPACE" \
  --topic-name "$TOPIC_NAME" \
  --subscription-name "$SUBSCRIPTION_NAME" \
  --name '$Default' \
  --only-show-errors \
  --output none >/dev/null 2>&1 || true

az servicebus topic subscription rule create \
  --resource-group "$RG" \
  --namespace-name "$SERVICEBUS_NAMESPACE" \
  --topic-name "$TOPIC_NAME" \
  --subscription-name "$SUBSCRIPTION_NAME" \
  --name event-type-filter \
  --filter-sql-expression "$FILTER_SQL" \
  --only-show-errors \
  --output none

echo "Realtime projection subscription reset complete."
