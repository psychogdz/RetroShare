# Docker notes

The production image definition lives in the repository root (`Dockerfile`,
`docker-compose.yml`). This folder holds deployment notes.

## Quick start

```bash
cp .env.example .env   # set JWT_SECRET and ADMIN_PASSWORD first
docker compose up --build -d
docker compose logs -f retroshare
```

The app answers on `http://localhost:8080`; health is exposed at `/api/health`
and used by the compose healthcheck.

## Persistence

Everything durable lives in the `retroshare-data` volume mounted at `/data`:

- `/data/retroshare.db` — SQLite database (WAL mode)
- `/data/storage/` — file blobs under `users/{ownerId}/{fileId}`

Back up the whole volume; restore is a copy back.

## Operational checklist

- Set a unique `JWT_SECRET` (≥32 characters) — the app refuses to start in
  production without one.
- Change `ADMIN_PASSWORD` from the default — same refusal applies.
- Put a TLS-terminating reverse proxy (nginx, Caddy, Traefik) in front; gRPC-Web
  works over plain HTTP/1.1, so no special proxy configuration is required.
- Consider lowering `DEFAULT_QUOTA_BYTES` / `MAX_FILE_BYTES` to taste.
