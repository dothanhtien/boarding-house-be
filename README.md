# Boarding House API

[![CI](https://github.com/dothanhtien/boarding-house-be/actions/workflows/ci.yml/badge.svg)](https://github.com/dothanhtien/boarding-house-be/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-4169E1?logo=postgresql&logoColor=white)
![Redis](https://img.shields.io/badge/Redis-7-DC382D?logo=redis&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-ready-2496ED?logo=docker&logoColor=white)

Backend API for boarding house / room rental management (property, room, lease, invoice, payment, maintenance, vehicle, notification, expense...).

## Tech stack

| Category   | Choice                                                                                                        |
| ---------- | ------------------------------------------------------------------------------------------------------------- |
| Framework  | ASP.NET Core (.NET 10)                                                                                        |
| Database   | PostgreSQL 17                                                                                                 |
| ORM        | EF Core + Npgsql (snake_case naming convention)                                                               |
| Cache      | Redis 7 (distributed cache, e.g. user lookups)                                                                |
| Auth       | JWT bearer access tokens + rotating refresh tokens, RBAC (roles/permissions)                                  |
| Validation | FluentValidation                                                                                              |
| Logging    | Serilog (console + rolling file), request logging with correlation id                                         |
| API docs   | OpenAPI + [Scalar](https://github.com/scalar/scalar) UI (`Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`) |
| Container  | Docker + Docker Compose                                                                                       |

## Project structure

```text
BoardingHouse.slnx
docker-compose.yml           # Postgres + Redis + api (base, used for CI/production)
docker-compose.override.yml  # auto-merged on `docker compose up` — hot reload for api + pgAdmin (local dev only)
docker/
  pgadmin/                   # supporting config for the pgadmin service (servers.json, pgpass)
src/
  BoardingHouse.Api/
    Controllers/             # AuthController, UsersController
    DTOs/                    # request/response models + FluentValidation validators
    Entities/                # User, Role, Permission, RolePermission, UserRole, RefreshToken
    Exceptions/              # AppException + GlobalExceptionHandler (RFC 7807 problem details)
    Extensions/              # JwtAuthenticationExtensions
    Middleware/               # CorrelationIdMiddleware
    Persistence/              # AppDbContext, EF Core configurations, migrations, audit interceptor
      Seed/                  # RbacSeeder (default roles/permissions)
    Repositories/            # EF Core repositories
    Services/                # AuthService, UserService, TokenService, UserCache (Redis)
    Program.cs
    Dockerfile                # multi-stage: build → dev (hot reload) → final (runtime image)
tests/
  BoardingHouse.UnitTests/
  BoardingHouse.IntegrationTests/
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

### 2. Start Postgres + Redis + pgAdmin

```bash
docker compose up -d postgres redis pgadmin
```

- **Postgres**: `localhost:5432` (user/password/db as set in `.env`).
- **Redis**: `localhost:6379` (password as set in `.env`) — used as the distributed cache (e.g. user lookups, see `IUserCache`).
- **pgAdmin**: [http://localhost:5050](http://localhost:5050) — log in with `PGADMIN_DEFAULT_EMAIL`/`PGADMIN_DEFAULT_PASSWORD` (from `.env`); the `postgres` server is already registered and auto-connected (via `docker/pgadmin/servers.json` + `pgpass`), no manual connection setup needed. Only defined in `docker-compose.override.yml` (local dev, see below) — the base `docker-compose.yml` used for CI/production has no pgAdmin service at all.

### 3. Run the API

Pick one of the two options:

- **Directly via the SDK** (no Docker needed for the api, Postgres + Redis from step 2 must be running):

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

## Authentication & RBAC

- **JWT access tokens** are short-lived (`Jwt:AccessTokenExpirationMinutes` in `appsettings.json`, default 15 min), signed with `JWT_SECRET`.
- **Refresh tokens** are longer-lived (`Jwt:RefreshTokenExpirationDays`, default 7 days), stored in the `refresh_tokens` table, and rotated on each use (`POST /api/auth/refresh-token`); `POST /api/auth/logout` revokes one.
- **RBAC** (`Role`, `Permission`, `RolePermission`, `UserRole` entities) models user access; default roles/permissions are populated by `RbacSeeder`.
  - In `Development`, the seed runs automatically on every app startup.
  - In `Production`, it does **not** run automatically (avoids a race condition if multiple instances start at once). Seed manually, after migrating, with:

    ```bash
    dotnet BoardingHouse.Api.dll --seed-rbac
    ```

    This runs the seed then exits without starting the web host.

## Database migrations

Postgres from step 2 must be running. Generate a new migration with `dotnet ef migrations add`:

```bash
dotnet ef migrations add InitialCreate \
  --project src/BoardingHouse.Api \
  --output-dir Persistence/Migrations
```

Apply pending migrations to the database:

```bash
dotnet ef database update --project src/BoardingHouse.Api
```

## Stop & clean up

```bash
docker compose down        # stop containers, keep data
docker compose down -v     # stop containers + delete Postgres/Redis/pgAdmin data volumes
```

## Contributing

Branch naming: `<type>/<short-description>` (`feature`, `fix`, `chore`, `refactor`, `docs`, `test`), branched off `main`. Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/).
