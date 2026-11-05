# ADR-0016 — Choose a Supported Production Runtime

- **Status:** Proposed (not approved).
- **Context:** Early workshop examples used ASP.NET Core 8. New Payment Gateway delivery is estimated at six months beginning November 2026.
- **Observed vendor fact:** Microsoft support lifecycle shows .NET 8 LTS ends November 2026, while .NET 10 LTS is supported into November 2028. Source: https://learn.microsoft.com/en-us/dotnet/core/releases-and-support
- **Option A:** Continue building on .NET 8. Low change cost for old examples, unacceptable unattended support lifecycle risk at a six-month go-live.
- **Option B (candidate):** Implement new demo foundation on .NET 10 LTS and approve a supportable runtime before production. Modern version, but tooling/library compatibility and team readiness must be verified.
- **Candidate implementation selection:** .NET 10 for the isolated, non-production foundation.
- **Evidence required:** successful build/test CI, dependency compatibility, security patch policy, production platform readiness and Architecture Board sign-off.
- **Decision:** Still **Proposed**; no production release approval implied.
