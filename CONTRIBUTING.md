# Contributing to RetroShare

Thanks for your interest in improving RetroShare! This document covers the basics
for getting a development environment running and the expectations for changes.

## Getting started

```bash
git clone <your-fork>
cd RetroShare
dotnet restore
dotnet run --project src/RetroShare.API
```

The app starts on `http://localhost:5000` (see `launchSettings.json`), applies EF Core
migrations, seeds permissions/roles/admin and serves the frontend from
`src/RetroShare.Web/wwwroot`.

Default development credentials (development only — production refuses to start
with them): `admin` / `ChangeMe!123`.

## Project layout

| Path | Purpose |
| --- | --- |
| `src/RetroShare.Domain` | Entities, enums, constants — no dependencies |
| `src/RetroShare.Application` | Business services, DTOs, interfaces, validation |
| `src/RetroShare.Infrastructure` | EF Core + SQLite, repositories, JWT, storage, gRPC service |
| `src/RetroShare.API` | REST controllers, middleware, authorization, hosting |
| `src/RetroShare.Web` | Static retro frontend (HTML + Bootstrap + vanilla JS) |
| `tests/RetroShare.UnitTests` | Password hashing, validation, JWT, storage path safety |
| `tests/RetroShare.IntegrationTests` | Full-stack tests incl. gRPC streaming over the test server |

## Ground rules

1. **Tests must pass.** `dotnet test RetroShare.sln` before every push; CI enforces it.
2. **Authorization is permission-based.** New endpoints declare
   `[Authorize(Policy = Permissions.X)]`; never check role names in code. New
   permissions are added to `Domain/Constants/Permissions.cs` (they are seeded
   automatically).
3. **No secrets in code.** Configuration flows through `appsettings.json` +
   environment variables (see `.env.example`).
4. **Files stream.** Never load whole files into memory — uploads/downloads go
   through the gRPC data plane chunk by chunk.
5. **EF entities stay internal.** API responses use DTOs from the Application layer.
6. **Keep the frontend framework-free.** HTML + Bootstrap + vanilla ES modules only.

## Adding a migration

```bash
dotnet tool restore   # installs dotnet-ef from the local manifest
dotnet dotnet-ef migrations add <Name> \
  --project src/RetroShare.Infrastructure \
  --startup-project src/RetroShare.API \
  --output-dir Data/Migrations
```

## Commit style

Conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`, `ci:`), one
logical change per commit.

## Reporting issues

Include reproduction steps, expected vs actual behavior, and log output from
`logs/retroshare-*.log` (redact anything sensitive).
