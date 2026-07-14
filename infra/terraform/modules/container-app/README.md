# Container App

Creates one immutable-image Container App with a dedicated managed identity,
Key Vault references, health probes, explicit ingress, and bounded scaling.
Workers pass `ingress = null`; public and internal APIs provide an ingress object.
Set `workload_profile_name` for dedicated production capacity; leave it null for
the environment's Consumption profile.
