# RetroShare — Architecture Notes

Companion to the [README](../README.md) with deeper detail on the decisions behind
the system.

## Layering & dependency flow

```
API  →  Application  →  Domain
 │
 └──→ Infrastructure → Application → Domain
```

- **Domain** — zero dependencies. Entities plus the two catalogs that drive the
  system: `Permissions.All` (the permission catalog, also the source for policy
  registration) and `FileRules` (blocked extensions/MIMEs, chunk sizes, defaults).
- **Application** — services own all business rules and throw typed
  `AppException`s; DTOs only. No EF Core reference: persistence is accessed
  through repository interfaces with intention-revealing async methods.
- **Infrastructure** — EF Core mapping (delete behaviors chosen to avoid cascade
  cycles: self-references and cross-aggregate FKs are Restrict; multi-path
  cascades that SQLite tolerates are handled at the database level), SQLite,
  PBKDF2/JWT/token implementations, local blob storage, the gRPC service, seeding.
- **API** — thin controllers delegating to services; middleware for exceptions
  and consistent error envelopes; policy-based authorization; Swagger; health
  checks; static hosting of the frontend.

## Why gRPC + gRPC-Web for transfers

The control plane stays simple JSON REST; the data plane needs streaming,
back-pressure and a defined wire contract for the browser. gRPC gives all three;
`Grpc.AspNetCore.Web` lets the same service serve browsers. The frontend codec is
a ~200-line hand-written protobuf encoder/decoder (`src/RetroShare.Web/wwwroot/assets/js/grpc.js`)
for the four messages involved — intentionally no npm/codegen toolchain.

## Upload protocol

1. Client opens `POST /grpc/filetransfer.FileTransfer/Upload`
   (`application/grpc-web+proto`), first frame = `UploadInit`
   (name, declared size, MIME, optional folder id).
2. Server validates the name (sanitization, reserved names), extension/MIME block
   lists, the single-file cap and the caller's quota **before any byte is written**,
   then opens the blob at `users/{ownerId}/{fileId}` (generated, never user input).
3. Chunks stream to disk; the server rejects the call if more bytes arrive than
   announced or than the configured cap.
4. On stream end the byte count must equal the declaration; only then is the
   `Files` row committed (with a concurrent-quota re-check). Any failure discards
   the partial blob.

## Download protocol

`DownloadRequest` selects either an authenticated owner/admin download
(`file_id` + JWT, requires `files.download`) or an anonymous share download
(`share_token` + optional `share_password`). Authorization happens **before the
first response frame** so failures map to precise gRPC statuses
(`NotFound`, `Unauthenticated`, `FailedPrecondition`). Share counters increment
atomically before streaming to make download limits race-free under SQLite's
single-writer serialization.

## Authorization internals

- Policies are generated per permission name at startup; `PermissionRequirement`
  is satisfied by `PermissionAuthorizationHandler`, which resolves the user's
  live permission set through `IPermissionChecker`.
- `PermissionChecker` joins `UserRoles → RolePermissions → Permissions` with a
  30-second `IMemoryCache` entry per user. Mutations invalidate explicitly:
  user role changes drop that user's entry; role permission changes bump a
  shared key version (cheap "flush all").
- The seeded Admin role must keep `system.manage` and system roles can't be
  renamed or deleted — the only deliberate system-level checks, existing so a
  misconfiguration cannot permanently lock out every administrator.

## Storage layout & safety

```
{Storage:Root}/users/{ownerId:N}/{fileId:N}      ← blob (StoredName kept for audits)
```

- `LocalFileStorage.SafeCombine` is the single choke point: every path is
  resolved and verified to stay inside the root (path-traversal tests cover it).
- Display names are sanitized (control chars, separators, leading dots, reserved
  device names) and never used for filesystem paths.
- Trash is a soft delete; blobs are destroyed only on permanent delete or user
  deletion (which first commits metadata, then removes blobs).

## Concurrency notes

SQLite serializes writers, which the design leans on for quota checks and share
counters. Uploads write directly to their own per-file path, so concurrent
uploads never touch the same blob.

## Testing strategy

- Unit tests cover pure logic (hashing, validation, share rules, path safety).
- Integration tests boot the whole app with `WebApplicationFactory`:
  each class gets a **uniquely named** in-memory SQLite database (shared-cache,
  connection held open) and its own temp storage root, so parallel classes are
  isolated; gRPC calls run over the in-memory test server channel, exercising
  the true streaming path.
