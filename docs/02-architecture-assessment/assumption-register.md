# Assumption Register

| ID | Assumption | If false | Validation | Owner | Status |
|---|---|---|---|---|---|
| ASM-001 | PSP supports provider-side idempotency | Risk of duplicate charge on unknown outcome | Read PSP API/SLA; sandbox test | PSP owner | Open |
| ASM-002 | Payment flow can be async | Different UX, API and transaction design | Customer checkout workshop | Product owner | Open |
| ASM-003 | Single-region deployment is acceptable for MVP | Requires different DR/availability topology | RTO/RPO and residency review | Sponsor | Open |
| ASM-004 | Gateway doesn't hold raw card data | Compliance scope expands | PCI/security review | Security | Open |
