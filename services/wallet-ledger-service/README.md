# Legacy Wallet Ledger

This service is a trafficless rollback artifact for the first Saga release. The active runtime uses two deployments of `services/bank-ledger-service`, each with an independently owned database and command queue.

The legacy image and database remain deployable for one release only. They are intentionally excluded from the Clean Architecture reference model and must be removed after the Saga rollout is verified and the rollback window closes.
