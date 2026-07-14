# ADR-002: Enforce Clean Architecture Per Service

**Status:** Accepted

## Context

The original minimal APIs mixed endpoints, state, and adapters. That made service boundaries difficult to evaluate and encouraged shared implementation details.

## Decision

Each service uses Domain, Application, Infrastructure, and host projects. Domain has no project dependencies; Application depends on Domain; Infrastructure implements Application ports; Api/Worker is the composition root. Integration schemas alone are shared.

## Consequences

- Domain rules run without Azure, EF, HTTP, or a broker.
- Infrastructure can switch between local and Azure adapters.
- More projects and explicit mappings add ceremony.
- xUnit architecture checks make the dependency rule executable rather than aspirational.
