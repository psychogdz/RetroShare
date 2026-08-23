# RetroShare v1.0.0 — First stable release

**A modern file-sharing platform with a retro soul.** Streaming uploads over gRPC,
expiring password-protected share links, storage quotas, a full role & permission
engine, and a CRT-inspired interface — built with ASP.NET Core, EF Core + SQLite,
Bootstrap and vanilla JavaScript.

## What shipped

- Complete file-sharing platform: accounts, files, folders, trash, share links,
  quotas, dashboard, activity log and a full admin panel.
- Streaming data plane over gRPC with a no-build-step retro web frontend.
- 93 automated tests (unit + full-stack integration), Docker packaging and a
  CI pipeline that fails on any red test.

## Architecture

Clean layered architecture — no shortcuts, no shortcuts around it either:

| Project | Responsibility |
| --- | --- |
| `RetroShare.Domain` | Entities, enums, permission catalog |
| `RetroShare.Application` | Business services, DTOs, validation, repository interfaces (no EF Core) |
| `RetroShare.Infrastructure` | EF Core 9 + SQLite with migrations, JWT, PBKDF2 hashing, local blob storage, gRPC service, seeding |
| `RetroShare.API` | Controllers, permission policies, middleware, Swagger, health checks |
| `RetroShare.Web` | Static retro frontend served by the API |

The **control plane** is REST over `ApiController`s returning DTOs only; the
**data plane** is a dedicated gRPC `FileTransfer` service. The database is applied
via migrations and seeded idempotently at startup (permissions, three system
roles, bootstrap admin).

## Authentication & permissions

- PBKDF2-SHA256 password hashing (per-hash salt, versioned format).
- 15-minute HS256 JWT access tokens; 64-byte refresh tokens stored only as
  SHA-256 hashes, rotated on every refresh, revocable per token and per user.
- Authorization is **entirely database-driven**: `User → UserRole → Role →
  RolePermission → Permission`. Every permission becomes an ASP.NET Core policy
  (`[Authorize(Policy = "files.upload")]`); controllers never hard-code role
  names. Role changes apply immediately via a permission handler with a
  micro-cache and explicit invalidation. Custom roles can be created at runtime
  from the admin UI.

## gRPC data plane

- `Upload` (client-streaming): init metadata + raw chunks; server pre-validates
  name/extension/MIME/quota, streams to disk, and commits only when the received
  byte count matches the declared size.
- `Download` (server-streaming): metadata + 64 KB chunks; authorized by JWT
  (owner/admin) or by share token with optional password (anonymous); share
  download counters increment atomically before streaming.
- gRPC-Web enabled so the vanilla-JS frontend streams directly from the browser
  with a ~200-line hand-rolled protobuf codec — no npm toolchain.

## Frontend

Bootstrap 5 + vanilla ES modules, no build step. CRT/monospace aesthetic that
stays professional and usable, responsive from 360 px to 1920 px. Uploads and
downloads show live progress, speed and ETA, with cancel and retry. Views:
login/register, dashboard, file manager, trash, shares, activity, profile and
the admin panel.

## Testing

**93 tests, all passing:**

| Suite | Covers |
| --- | --- |
| Unit (61) | password hashing, token generation, share expiration/limits, name sanitization & reserved names, blocked extensions/MIMEs, path-traversal protection, storage round-trip |
| Integration (32) | full app on `WebApplicationFactory` + in-memory SQLite: auth flows, refresh rotation, permission enforcement incl. live role changes, ownership isolation, quota enforcement, gRPC upload/download round-trips, share lifecycle (password/expiry/limit/revoke), folder trees, user deletion cascades, dashboards, health |

## Docker & CI

- Multi-stage Dockerfile that **runs the full test suite during the image
  build** — a broken build never reaches a runtime image.
- `docker compose up --build` with mandatory `JWT_SECRET` / `ADMIN_PASSWORD`
  (compose refuses to start without them); database + blobs persist under the
  `retroshare-data` volume; curl-based container healthcheck.
- GitHub Actions: restore → build → test → publish, plus a Docker build job.

## Production hardening

- No signing secret in base configuration; a development-only secret lives in
  `appsettings.Development.json`.
- Production startup **refuses** to boot with a missing/short JWT secret, with
  any known repository placeholder secret, or with the development seed admin
  password.
- Opt-in forwarded-headers support for reverse-proxy deployments, trusting only
  explicitly configured proxies.
- Filename sanitization, blocked extension/MIME lists, path-traversal-safe
  storage, backend-enforced quotas, rate limiting (stricter on auth), safe
  error envelopes, auditable activity log.

## Verified flows

Verified end-to-end in a real browser (black-box GUI) and via integration tests:

- Landing, registration and login; token storage and refresh.
- Dashboard statistics, quota meters and the activity feed.
- gRPC-Web **upload** through the app's own dropzone pipeline (progress UI,
  declared-size commit).
- **Owner download** over gRPC-Web — byte-exact round-trip verified.
- Share link creation (expiry, download cap) and the anonymous `/s/{token}`
  page; **anonymous share download** — byte-exact, no credentials required.
- Admin panel: system health, users, roles (25 permissions), permission
  catalog, shares with revoke.
- Production boot paths: refused without a secret, refused with a known dev
  secret, healthy with a unique secret.

## Known limitations

- The compose `healthy` state was verified by construction (curl installed,
  health endpoint proven) but not observed end-to-end — no Docker on the
  development machine used for release verification.
- Share tokens are stored as plaintext values in the database (128-bit random,
  not derived from user input); hashing them is a candidate follow-up.
- PBKDF2 uses 100,000 iterations (OWASP now suggests 600k for SHA-256); the
  versioned hash format allows raising this without invalidating existing
  hashes.
- The container runs as root; a non-root user is a recommended follow-up.
- No blob checksums, virus scanning, or resumable downloads yet — see
  "Future improvements" in the README.

## Development seed credentials

Development only — never use in production:

- Username: `admin`
- Password: `ChangeMe!123`

Production deployments require explicit secrets (`JWT_SECRET`,
`ADMIN_PASSWORD`) and refuse to boot with these seed values.
