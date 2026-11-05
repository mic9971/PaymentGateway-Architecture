# Security and Deployment — Discovery Draft

Threat boundaries: merchant ↔ edge; API ↔ PSP; webhook ingress; services ↔ DB/RabbitMQ/Redis; operators ↔ admin tooling.

TBD: PCI scope, secrets manager, PSP signature/replay protections, tenant authorization, encryption at rest/in transit, audit retention, backup, region, RTO/RPO.

Do not place PSP keys, credentials or raw card data in repository.
