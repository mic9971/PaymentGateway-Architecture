# Customer Brief (draft)

## Business problem
The customer wants a new payment gateway capable of routing payments via PSP integrations, managing outcomes, refunds, webhooks and reconciliation.

## Facts / assumptions / unknowns
| Field | Working value | Status | Owner to confirm |
|---|---|---|---|
| Go-live | 6 months | Workshop input; needs contract confirmation | Sponsor |
| Daily volume | 10,000 | Clarify HTTP requests vs payment transactions | Product owner |
| Peak-day volume | 30,000 or 50,000 | Conflicting workshop scenarios | Product owner |
| Concurrent users | 500 | Clarify user behavior and arrival rate | Product owner |
| PSP count | 2 | Assumption | Business / PSP team |
| Budget | TBD | Different workshop scenarios exist | Sponsor |
| Stack | .NET 8/PostgreSQL/RabbitMQ/Redis | Technical preference, not an immutable constraint | Tech sponsor |

## Success criteria
TBD — define measurable financial correctness, security, performance, recovery and acceptance goals.
