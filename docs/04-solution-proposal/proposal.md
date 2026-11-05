# Solution Architecture Proposal — Draft v0.1

## Business objective
TBD: validated customer goal and scope.

## Recommended candidate
Modular Monolith with separate worker process and explicit bounded contexts, PostgreSQL source of truth, transactional outbox into RabbitMQ, Redis for non-authoritative cache and rate limiting.

## Decision rationale
Lower coordination/operational overhead for an MVP; retain business boundaries and explicit contracts. Alternatives must be revisited when team topology and independent scaling evidence are known.

## Risks and open decisions
See `../02-architecture-assessment/` and `solution-options.md`. Provider semantics, budget, PCI scope, RTO/RPO, SLO and capacity are TBD.

## Approval
- Sponsor: pending
- Product owner: pending
- Architecture review: pending
- Security review: pending
