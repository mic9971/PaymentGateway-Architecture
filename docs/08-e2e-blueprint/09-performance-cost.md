# 09 — Workload Modeling, PostgreSQL Performance, .NET Memory and Total Cost

The [E2E workbook](https://docs.google.com/spreadsheets/d/1d4ab3DIkGC_BYZUiJ34SSCEWjXENAC8U/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true) contains the editable formula model in tabs E2E Workload, E2E Perf Budget and E2E Cost Model. These are **illustrative assumptions** until Sponsor/Product/SRE sign-off.

## Workload inputs

| Input | Example only | Status |
|---|---:|---|
| Payments / day | 10,000 | Workshop input, ambiguous payments vs HTTP requests |
| Peak traffic share | 30% of day in 2h | Assumed |
| HTTP calls / payment | 4 | Assumed |
| Burst factor | 3x | Assumed |
| Retry amplification | 1.5x | Assumed |
| MQ events / payment | 3 | Assumed |
| Average API latency | 250 ms | Assumed |
| Sustained capacity per API instance | 40 RPS | Unbenchmarked placeholder |
| Reserve capacity | 30% | Proposed |
| Survive one instance failure | N-1 | Proposed |

Calculated illustrative outputs:

- Daily average Payment TPS = 10,000 / 86,400 = **0.1157**.
- Mean Payment TPS over 2-hour peak window = (10,000 × 0.30) / (2 × 3,600) = **0.4167**.
- Mean peak-window HTTP RPS = 0.4167 × 4 = **1.6667**.
- Design HTTP RPS under synthetic burst+retry assumptions = 1.6667 × 3 × 1.5 = **7.5**.
- MQ msg/s under same multipliers = 0.4167 × 3 events × 3 × 1.5 = **5.625**.
- Average in-flight requests (Little's Law) = 7.5 × 0.25s = **1.875**.
- Usable instance RPS with 30% reserve = 40 × (1 - 0.30) = **28**.
- Simplified N-1 instance count = CEILING(7.5 / 28) + 1 = **2**, **NOT a production sizing recommendation**.

Real workload needs arrival histogram, provider calls/latency, asynchronous response decisions, merchant concurrency, duplicate retries, queue events, batch schedules, incident amplification and max connection counts.

## Evidence-driven diagnosis sequence

~~~mermaid
flowchart TD
    W["Confirm workload + SLO"] --> B["Establish benchmark"]
    B --> O["Observe p95/p99/errors/traces/DB/MQ"]
    O --> H{"Bottleneck?"}
    H --> Q["Query plan, index, lock, cache, batching, Npgsql pool"]
    H --> C["CPU, ThreadPool, .NET GC/allocations"]
    H --> P["Provider latency, queue backlog"]
    Q --> X["Change one hypothesis"]
    C --> X
    P --> X
    X --> V["A/B same workload: performance + correctness + cost"]
    V -->|Regression| O
~~~

### Database query evidence

- For each hot endpoint map endpoint → SQL query → selectivity → actual plan → IO/CPU/lock waits.
- Review candidate composite/partial/unique indexes; avoid indexing purely by intuition.
- PostgreSQL EXPLAIN (ANALYZE, BUFFERS), ANALYZE stats and pg_stat_statements support evidence collection.
- Investigate Npgsql connection pooling and long-running transactions before increasing pool limit.
- Batching is useful for reconciliation/notifications and can reduce roundtrips; never cross financial transaction boundaries blindly.
- Partitioning improves some large-table retention/scan workloads; not a default scale fix.

### .NET memory diagnostics

Collect allocation rate, GC heap/LOH, working set and GC pauses. Use dotnet-counters, trace and dump/GC roots after appropriate privacy and production-safety review. High allocation, retained managed objects and native memory growth are different classes of problem; don't treat GC.Collect as an automatic fix.

## Cost / TCO model

The workbook intentionally leaves vendor unit prices blank. Build a costed proposal only after comparing hosting/location and real quotes:

- API and worker compute across prod/stage.
- PostgreSQL primary, replicas, PITR/backup, storage, egress.
- RabbitMQ operations/HA and Redis capacity.
- WAF/ingress, network, secrets, security controls.
- Logs/traces/retention, CI/CD, load test and recovery.
- On-call labor, software support, PSP financial fees and taxes as applicable.

Review monthly cost and cost / 1,000 successfully reconciled payments; do not confuse infrastructure price with the entire project budget.

Official references:
- https://www.postgresql.org/docs/current/indexes.html
- https://www.postgresql.org/docs/current/using-explain.html
- https://www.postgresql.org/docs/current/ddl-partitioning.html
- https://www.npgsql.org/doc/basic-usage.html
