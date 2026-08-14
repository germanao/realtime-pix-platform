# Container App

Creates one immutable-image Container App with a dedicated managed identity,
optional private-registry and Key Vault references, health probes, explicit ingress, and bounded scaling. Public GHCR images omit the registry block entirely.
Workers pass `ingress = null`; public and internal APIs provide an ingress object.
Set `workload_profile_name` for dedicated production capacity; leave it null for
the environment's Consumption profile.
