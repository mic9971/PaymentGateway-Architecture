# Payment State Machine — Draft

Created → Processing → Succeeded / Failed / Unknown.
Unknown → Succeeded / Failed after verified provider inquiry or reconciliation.
Unknown → Processing only if a retry is safe under provider contract.

Refunds have a separate lifecycle. Out-of-order webhook events must not regress confirmed terminal states. Payment correctness invariants are stronger than API and queue delivery guarantees.
