# Architecture Fitness Functions — Proposed

- Only Payments can write payment-owned aggregates.
- Domain layer has no dependency on Infrastructure.
- Public contracts versioned compatibly.
- No secrets in source control.
- Critical payment failure scenarios run in CI.
- Architecture decisions have owners and review dates.

Distinguish code-level checks (dependency/static policy) from runtime correctness, p95 latency and recovery checks.
