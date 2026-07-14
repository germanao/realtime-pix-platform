# Diagnostic Settings

Discovers the log and metric categories supported by one Azure resource and
sends them to a workspace using resource-specific tables. Callers may exclude
known noisy categories explicitly; the default captures every supported log
and metric so newly supported categories are visible in reviewed plans.

The POC and production roots are executable examples. Diagnostic settings are
kept outside service modules because the shared workspace belongs to the
environment-level observability boundary.
