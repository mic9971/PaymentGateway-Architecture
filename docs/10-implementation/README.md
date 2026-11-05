# Payment Core — Implementation Foundation v0.1

**Status:** New implementation proposal on `feature/payment-core-foundation`; not merged. Build and runtime tests are **not locally verified** because the authoring environment lacks .NET SDK and Docker.

## Scope actually implemented

- `Payments.Domain`: `PaymentState` and explicit basic state transitions.
- `Payments.Application`: create/get requests, normalization/validation, idempotency request hash.
- `Payments.Infrastructure`: PostgreSQL unique claim, a single transaction for Payment + Attempt + Outbox, tenant-scoped reads, durable worker attempt claims and Unknown recovery.
- `Gateway.Api`: development-only `POST /v1/payments`, `GET /v1/payments/{id}`, and liveness endpoint.
- `Gateway.Worker`: development-only deterministic **Mock PSP** executor; no network call or real charges.
- `deploy/compose`: local PostgreSQL + API + Worker. PostgreSQL schema is a local initialization script, **not** a production migration system.
- Unit/Integration tests and GitHub Actions: compile/test **must be verified from CI run**, do not infer from committing files.

## Explicitly NOT implemented

- Real VietQR, wallet and PSP integrations.
- Authenticated merchants (the local demo header is untrusted and is blocked outside Development).
- RabbitMQ publisher/consumer; Outbox is **stored, not yet delivered**.
- Merchant outbound webhook, PSP inbound signature verification, Reconciliation, RefundBudget, HA/DR, security audit and production hardening.
- Validated PSP-side financial exactly-once: impossible to demonstrate using local DB uniqueness alone.
- Approved Payment/Refund ADR and Customer/Finance/PSP sign-off.

## Architectural decisions for this slice

1. **.NET 10 LTS** is the initial implementation target because .NET 8 support ends in November 2026. This is a technical proposal; record/approve production runtime in ADR.
2. `MOCK` is **the only accepted payment method** in this slice. VietQR/EWallet are still design targets.
3. Financial API processing is **asynchronous** in the local demo; merchant receives `Pending` and polls `GET`.
4. The fake worker's `Succeeded` means **simulated paid outcome**, not actual bank confirmation.
5. A worker crash during a provider attempt eventually yields `Unknown`; it is **never automatically retried**.

## Local setup (Docker required)

```bash
cd deploy/compose
cp .env.example .env
# Choose a new local-only POSTGRES_PASSWORD in .env.
docker compose up --build -d
```

Wait for the API at `http://127.0.0.1:8080/health/live`. The first PostgreSQL startup runs the local schema; existing volumes are not automatically remigrated.

### Create a synthetic payment

```bash
curl -i http://127.0.0.1:8080/v1/payments \
  -H 'Content-Type: application/json' \
  -H 'X-Demo-Merchant-Id: 24ab68f1-136b-4e50-ab7e-03c601c1de05' \
  -H 'Idempotency-Key: demo-order-001' \
  -d '{"merchantReference":"order-001","amountMinor":500000,"currency":"VND","paymentMethod":"MOCK"}'
```

- Expected first response: `202` with `paymentId` and `status: Pending`.
- Repeat the same request/key: `200` with same `paymentId`, `replayed: true`.
- Repeat key but change amount: `409`.
- Query status with same `X-Demo-Merchant-Id`: `GET /v1/payments/{paymentId}`. The worker should eventually simulate a terminal outcome.
- Using another demo merchant ID cannot read this payment.

Stop services: `docker compose down` (add `-v` **only** when intentionally deleting all demo DB data).

### Run tests locally with .NET 10

```bash
dotnet restore PaymentGateway.slnx
dotnet build PaymentGateway.slnx -c Release
dotnet test tests/UnitTests/Payment.Core.UnitTests.csproj -c Release
# For database integration tests, start Postgres and provide:
# export PAYMENT_TEST_CONNECTION_STRING='Host=localhost;Port=5432;Database=payment_demo;Username=payment_demo;Password=...'
dotnet test tests/IntegrationTests/Payment.IntegrationTests.csproj -c Release
```

**CI provides** `PAYMENT_TEST_CONNECTION_STRING`. Without it, local integration tests return without exercising PostgreSQL. Check the CI run and don't report these as passing database tests when no DB is configured.

## Next implementation PR

1. Fix any CI compile/integration errors before merging.
2. Add real Outbox → RabbitMQ publisher and an idempotent consumer, with delivery confirmation/dedup tests.
3. Add provider webhook signature verification and a Mock PSP inquiry flow to resolve Unknown with verifiable simulated evidence.
4. Add authenticated merchant identity and contract/security tests.
5. Only after PSP contracts and Finance review, implement VietQR/wallet adapters and RefundBudget.

### Traceability

- FR-PAY-001 / PC-T01/02: idempotent Payment creation.
- FR-PAY-002 / PC-T12: merchant-scoped reads (currently dev-only identity).
- PC-T03/04/11: attempt claims, no blind repeat after crash.
- PC-T10: Outbox persists in DB but no MQ publish proof yet.
- ADR-0014/0015 and ADR-0013 remain Proposed. Mock behavior is **not financial correctness evidence**.
