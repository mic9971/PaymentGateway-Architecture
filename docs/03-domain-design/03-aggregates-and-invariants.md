# Tactical DDD — Aggregates, Consistency and Financial Invariants

**Proposed aggregates**, not an implemented data schema.

| BC | Aggregate root | Owns | Atomic invariant | Example test |
|---|---|---|---|---|
| Merchant | Merchant | Status, capabilities and merchant policy | Tenant and credential boundaries | Merchant isolation |
| Payment | PaymentIntent | MerchantId, amount, currency, state | UNKNOWN not FAILED; valid state transitions | Lost PSP response |
| Payment | PaymentAttempt | AttemptId, provider selection, reference and outcome | No unsafe duplicated external attempt | Worker crash / redelivery |
| Payment | IdempotencyRecord | Merchant+operation+key+request hash | Same logical request maps to one outcome; conflicting body rejected | Concurrent POST |
| Routing | RoutingDecision | Policy version, eligible PSP, reason | UNKNOWN attempt blocks unsafe switching | PSP A timeout |
| Provider Integration | ProviderEventReceipt | Signature/correlation evidence, event identity | Only authenticated event enters domain workflow | Forged/replayed webhook |
| Refund | Refund | Refund request, attempt and outcome | Refund idempotency; UNKNOWN distinct from FAILED | Refund timeout |
| Refund | RefundBudget | Verified limit, reserved and completed amounts | **Reserved + Completed ≤ Verified refundable limit** | Two concurrent refunds |
| Reconciliation | ReconciliationRun / Discrepancy | Statement evidence, match and exception | Any correction uses verified evidence and audit | Late PSP statement |
| Notifications | MerchantDelivery | Event identity, endpoint, retry attempts | Delivery at least once, durable status | Endpoint outage |
| Operations | InvestigationCase | Responsible operator, evidence and decision | Privileged resolutions auditable | Investigation replay |

## Transaction boundaries

### 1) Payment creation
Use one local PostgreSQL transaction to atomically claim (merchant_id, operation, Idempotency-Key), validate matching payload hash, write PaymentIntent/Attempt and outbox intent. No PSP HTTP calls occur while the DB transaction is open.

### 2) External PSP execution
Attempt is durably claimed. After PSP definitive result, commit a valid Payment transition and outbox. On ambiguous network timeout persist UNKNOWN. DB uniqueness guarantees *local* idempotency, NOT provider-side exactly-once financial effect. Must use provider idempotency/recovery semantics.

### 3) Refund authorization (hardest boundary)
A naive design performs: "Refund queries Payment.Succeeded and checks amount → Refund sends PSP request." Two concurrent refund commands can both see the same balance. **Not safe.**

Two candidates to compare:

- **Option A — Atomic refund budget in one PostgreSQL transactional boundary (MVP preference):** Refund owns a row keyed by PaymentId containing verified refundable limit, completed and reserved totals. First ensure it is initialized/updated only from authorized Payment evidence. In one transaction acquire row lock or perform conditional UPDATE: \`reserved + completed + requested <= verified limit\`. Persist Refund and Outbox in that transaction. Additional payment reversals/corrections need an explicit protocol; stale authorization must not widen the limit.
- **Option B — Payment owns all refund authorization and reserves the budget; Refund handles PSP execution:** Payment exposes \`ReserveRefund\` with DB-side atomic guard. Refund has its own lifecycle but cannot proceed without a durable reservation reference. Clear ownership and contract, but stronger coupling and careful recovery of reservations.

**Decision is NOT approved.** Validate with Finance, PSP semantics, payment adjustments/partial capture and concurrency tests. A dedicated Refund microservice with only eventual-consistent Payment projection is unacceptable if it can over-refund.

### 4) Reconciliation correction
Recon owns evidence comparison. Send \`ApplyVerifiedOutcome(paymentId, attemptId, evidenceRef)\` to Payment; Payment validates merchant, provenance and transition under concurrency. No Recon UPDATE to \`payment.payments\`.

### 5) Cross-context message delivery
Use transactional outbox for domain state + event intent; at-least-once external delivery. Outbox library/schema can be **shared infrastructure** yet the producing BC owns the business transaction and published contract. No unrelated BC is authorized to mutate that business state.

## Minimal invariants (critical)

1. One logical PaymentIntent per merchant + operation + idempotency key.
2. Provider side effect never repeated solely because RabbitMQ redelivered.
3. UNKNOWN remains uncertain until verified, never silently set FAILED.
4. QRGenerated is not PaymentSucceeded.
5. Terminal state is not regressed by late/replayed webhook.
6. Refund completed + in-flight reserved cannot exceed verified financial entitlement.
7. Merchant A cannot access merchant B resources.
8. PSP outcomes require provider authentication and stable correlation.
9. Recon evidence correction is reviewed and auditable.
10. Every financially relevant state change has traceable causal evidence.

## Observability/test cases

Map INV-001..012 to concurrent request, worker crash, PSP response loss, replayed/out-of-order webhook, concurrent refund, tenant crossing, unknown-to-final reconciliation, DB/queue failure and operator override tests.
