## Summary and scope
- Business requirement(s):
- Assumption(s) changed:
- Risk(s) added/updated:
- Architecture decision(s) (ADR):
- Impacted contexts/contracts:

## Financial correctness and security
- [ ] No unintended duplicate PSP charge on retry / redelivery
- [ ] Unknown outcome does not cause blind cross-PSP failover
- [ ] Merchant/tenant isolation validated
- [ ] No secrets, credentials or sensitive data included
- [ ] Schema/API/Event compatibility reviewed

## Evidence
- Unit/integration/contract test links:
- PSP sandbox/fault injection (if relevant):
- Query plan/load test baseline and after (if relevant):
- Restore / failover test (if relevant):
- Traceability: BR/FR/NFR → Risk → ADR → Test → Gate

## Release/merge permission
- [ ] Product / Finance / Security owners consulted if financial boundary changed
- [ ] Architect review completed
- [ ] **Repository owner explicitly approves merge**
- [ ] Draft PR is not automatically merged

**Workflow:** feature/* → develop via reviewed PR; develop → main via separately approved release PR.
