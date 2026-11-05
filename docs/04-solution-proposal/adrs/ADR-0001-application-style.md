# ADR-0001: Application Style

- Status: **Proposed — not accepted**
- Context: 6-month MVP, Payment Gateway, unclear team size, correctness and recovery requirements.
- Option A: Modular Monolith + background workers.
- Option B: Separately deployed Microservices.
- Proposed decision: Prefer A until deployment and scaling evidence warrants B.
- Consequences: Easier local transactions and testing; limited independent release per business module.
- Revisit trigger: independently scalable bounded context with measurable need; team ownership and operational budget available.
- Owner / approval date: TBD.
