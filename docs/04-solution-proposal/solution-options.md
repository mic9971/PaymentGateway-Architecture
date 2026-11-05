# Solution Options — Draft

| Criterion | Modular Monolith + workers | Microservices |
|---|---|---|
| 6-month MVP complexity | Lower | Higher |
| Independent deployments | Limited | Strong |
| Local transactional boundaries | Easier | More distributed workflows |
| Operational overhead | Lower | Higher |
| Independent scale | Coarse-grained initially | Finer-grained |
| Main risk | Module coupling without tests | Distributed failure and consistency |

Decision pending requirements, team topology, SLO, ownership and cost review.
