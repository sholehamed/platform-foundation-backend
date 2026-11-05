# Platform Foundation — Backend

Reusable ASP.NET Core / .NET 10 backend foundation, developed in small, testable milestones.

> **Current status:** existing code under review, not a production-ready starter template.
> The repository has working infrastructure and tests, but module activation, tenant isolation, and the full authentication integration must be validated before product reuse.

## Repositories

- **Backend:** [platform-foundation-backend](https://github.com/sholehamed/platform-foundation-backend)
- **Frontend:** [platform-foundation-frontend](https://github.com/sholehamed/platform-foundation-frontend) (Angular 22)
- **Rebaseline task:** [F00 issue #11](https://github.com/sholehamed/platform-foundation-backend/issues/11)

## Current code map

- `Domain`, `Domain.Sharedkernel`: domain models and shared primitives
- `Application`, `Application.Sharedkernel`: requests, handlers and pipelines
- `Infrastructure.*`: persistence, durable messaging / outbox and telemetry
- `Modules.Identity`: identity, tenancy and permission model (integration requires validation)
- `Web`: HTTP composition root, OpenAPI/Scalar and opt-in integrations
- `tests/`: focused backend test projects
- `docs/`: concepts, architecture decisions, development status and guides

Some legacy product-specific names remain, including the solution filename. Do **not** infer that all existing projects are wired into the host just because their source files exist.

## Local baseline

Requires .NET SDK 10 and access to the NuGet feeds declared by the project.

```bash
dotnet restore shinera-back.slnx
dotnet build shinera-back.slnx --no-restore -c Release
dotnet test tests/Platform.Foundation.Application.Tests/Platform.Foundation.Application.Tests.csproj -c Release
dotnet test tests/Platform.Foundation.Web.Tests/Platform.Foundation.Web.Tests.csproj -c Release
dotnet test tests/Platform.Foundation.Messaging.Tests/Platform.Foundation.Messaging.Tests.csproj -c Release
dotnet test tests/Platform.Foundation.Observability.Tests/Platform.Foundation.Observability.Tests.csproj -c Release
```

These commands describe the intended baseline; they **have not been rerun as part of this documentation change**. GitHub Actions last reported success on 2026-10-08. Database-dependent runtime behavior needs separate verification.

## Development rules

1. A capability is not considered complete until its runtime behavior and tests are demonstrated.
2. Authorization and tenant isolation are enforced server-side.
3. Keep distributed cache and external message brokers optional.
4. Use a feature branch, focused pull request and test evidence for each milestone.
5. Avoid refactoring working code just to match a diagram.

Start with [architecture overview](docs/architecture/ARCHITECTURE-OVERVIEW.md), [development roadmap](docs/development/ROADMAP.md), and [F00 baseline status](docs/development/FOUNDATION-STATUS.md).
