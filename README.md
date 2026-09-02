# Boarding House API

Backend API for boarding house / room rental management (property, room, lease, invoice, payment, maintenance, vehicle, notification, expense...).

## Tech stack

| Category  | Choice                                                                                                        |
| --------- | ------------------------------------------------------------------------------------------------------------- |
| Framework | ASP.NET Core (.NET 10)                                                                                        |
| Database  | PostgreSQL 17                                                                                                 |
| ORM       | EF Core + Npgsql (planned)                                                                                    |
| API docs  | OpenAPI + [Scalar](https://github.com/scalar/scalar) UI (`Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`) |
| Container | Docker + Docker Compose                                                                                       |

## Project structure

```text
BoardingHouse.slnx
docker-compose.yml           # Postgres + api (base, used for CI/production)
docker-compose.override.yml  # auto-merged on `docker compose up` — hot reload for api + pgAdmin (local dev only)
docker/
  pgadmin/                   # supporting config for the pgadmin service (servers.json, pgpass)
src/
  BoardingHouse.Api/
    Controllers/
    Program.cs
    Dockerfile                # multi-stage: build → dev (hot reload) → final (runtime image)
```

## Requirements

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) + Docker Compose

## Running locally

### 1. Copy the sample env files

```bash
cp .env.example .env
cp docker/pgadmin/pgpass.example docker/pgadmin/pgpass
chmod 600 docker/pgadmin/pgpass
```

`.env` contains the following variables (adjust as needed, especially passwords, before using anywhere beyond local dev):

| Variable                   | Meaning                               |
| -------------------------- | ------------------------------------- |
| `POSTGRES_USER`            | Postgres username                     |
| `POSTGRES_PASSWORD`        | Postgres password                     |
| `POSTGRES_DB`              | Database name                         |
| `PGADMIN_DEFAULT_EMAIL`    | pgAdmin login email (desktop mode)    |
| `PGADMIN_DEFAULT_PASSWORD` | pgAdmin login password (desktop mode) |

⚠️ Keep the credentials in `docker/pgadmin/pgpass` in sync with `.env` (and with `docker/pgadmin/servers.json` if you change `POSTGRES_DB`/`POSTGRES_USER`) — pgAdmin uses this file to auto-connect to Postgres without prompting for a password.

### 2. Start Postgres + pgAdmin

```bash
docker compose up -d postgres pgadmin
```

- **Postgres**: `localhost:5432` (user/password/db as set in `.env`).
- **pgAdmin**: [http://localhost:5050](http://localhost:5050) — log in with `PGADMIN_DEFAULT_EMAIL`/`PGADMIN_DEFAULT_PASSWORD` (from `.env`); the `postgres` server is already registered and auto-connected (via `docker/pgadmin/servers.json` + `pgpass`), no manual connection setup needed. Only defined in `docker-compose.override.yml` (local dev, see below) — the base `docker-compose.yml` used for CI/production has no pgAdmin service at all.

### 3. Run the API

Pick one of the two options:

- **Directly via the SDK** (no Docker needed for the api, Postgres from step 2 must be running):

  ```bash
  dotnet run --project src/BoardingHouse.Api
  ```

  Port comes from `src/BoardingHouse.Api/Properties/launchSettings.json` (defaults: `http://localhost:5066`, `https://localhost:7108`).

- **Via Docker** (hot reload included, see details below):

  ```bash
  docker compose up -d --build api
  ```

  API is exposed at `http://localhost:8080`.

### 4. Open the API docs

Open the Scalar UI at `/scalar/v1`:

- Via Docker: [http://localhost:8080/scalar/v1](http://localhost:8080/scalar/v1)
- Via `dotnet run`: use the port from step 3 (e.g. `http://localhost:5066/scalar/v1`)

The Scalar UI is only enabled in the `Development` environment (see `Program.cs`).

## Hot reload when running via Docker

`docker-compose.override.yml` is automatically merged into `docker-compose.yml` by Docker Compose when running `docker compose up` (no extra flag needed). This file:

- Builds the `api` service at the `dev` stage of the [Dockerfile](src/BoardingHouse.Api/Dockerfile) — this stage runs `dotnet watch run`.
- Bind-mounts the source code (`src/BoardingHouse.Api`) into the container, so code changes on the host are applied to the running container immediately (`dotnet watch` logs `🔥 changes applied`), no image rebuild needed.
- Uses named volumes (`api_bin`, `api_obj`) to isolate the `bin/`/`obj/` produced inside the container (Linux) from the host's `bin/`/`obj/` — preventing the container from overwriting the host's build artifacts with Linux ones, which would otherwise break IntelliSense/OmniSharp on the dev machine (macOS/Windows).
- Sets `DOTNET_USE_POLLING_FILE_WATCHER=true` — required for `dotnet watch` to pick up file changes through the bind mount, especially on macOS/Windows (filesystem events don't reliably cross the Docker Desktop VM boundary).
- Sets `ASPNETCORE_ENVIRONMENT=Development` for the `api` service (the base `docker-compose.yml` runs it as `Production`), which is what enables the Scalar UI (see step 4 above).
- Adds the `pgadmin` service itself, and binds its port (`5050`) and Postgres' (`5432`) to `127.0.0.1` only, keeping them off the LAN. pgAdmin isn't part of the base `docker-compose.yml` at all — it never runs in CI/production.

**Notes:**

- Only changes _within a method body_ are applied live ("non-rude edit"). Adding/removing methods, changing signatures, etc. require a restart:

  ```bash
  docker compose restart api
  ```

- CI/production builds the real image (stage `final`, no hot reload, non-root user) by explicitly specifying `-f docker-compose.yml` (skipping this override file), or by setting `COMPOSE_FILE` accordingly.

## Dockerfile — multi-stage build

[src/BoardingHouse.Api/Dockerfile](src/BoardingHouse.Api/Dockerfile) has 3 stages:

1. **`build`**: base `mcr.microsoft.com/dotnet/sdk:10.0`, restore + `dotnet publish` into `/app/publish`.
2. **`dev`**: base `mcr.microsoft.com/dotnet/sdk:10.0`, restore then run `dotnet watch run` — used for local dev (see Hot reload above), only activated when `docker-compose.override.yml` is merged (`target: dev`).
3. **`final`**: base `mcr.microsoft.com/dotnet/aspnet:10.0` (runtime-only, lighter than the SDK image), copies the output from the `build` stage, runs as a non-root user (UID 1500 — UID 1000 is already taken in the base image). This is the default stage when no `target` is specified (CI/production).

Both stages expose port `8080` (Kestrel default inside the container, mapped to `localhost:8080` via `docker-compose.yml`).

## Stop & clean up

```bash
docker compose down        # stop containers, keep data
docker compose down -v     # stop containers + delete Postgres/pgAdmin data volumes
```

## Contributing

Branch naming: `<type>/<short-description>` (`feature`, `fix`, `chore`, `refactor`, `docs`, `test`), branched off `main`. Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/).
