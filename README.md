# Chubb Claims Platform

The backend foundation uses ASP.NET Core and xUnit on .NET 10 LTS. Install SDK 10.0.404 or a later patch in the same SDK feature band, as configured in `global.json`.

From the repository root, restore, build, and test:

```sh
dotnet restore
dotnet build
dotnet test
```

Run the API on a local HTTP port:

```sh
dotnet run --project src/Chubb.Claims.Api --no-launch-profile -- --urls http://localhost:5080
```

In another terminal, run `curl -i http://localhost:5080/health`. The endpoint returns HTTP 200 with `{"status":"ok"}`.

- `src/Chubb.Claims.Api` hosts the minimal API.
- `tests/Chubb.Claims.Tests` verifies the health endpoint through the ASP.NET Core test host.

## SQLite persistence

The EF Core model uses SQLite. In the `Development` environment the API applies
pending migrations at startup and creates `claims.db` in its working directory.
The database and its sidecars are ignored by Git. Set the `ConnectionStrings__Claims`
environment variable to override `Data Source=claims.db;Foreign Keys=True`; retain
foreign-key enforcement in custom connection strings. For other environments, apply
migrations as a separate deployment step rather than relying on startup migration.

To initialize a local development database and run the API:

```sh
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Chubb.Claims.Api --no-launch-profile
```

The development initializer seeds two deterministic **demo identities** (IDs 1 and 2):
a claimant and a claims officer. These are sample records, not authentication or
authorization. It runs after migrations only in Development; do not treat them as real users.

`IncidentDate` is a calendar date. All timestamps use `DateTimeOffset` and must be
provided in UTC (offset `+00:00`); newly constructed records default to UTC. Callers
are responsible for preserving this convention and updating `UpdatedAt` on changes.
The MVP supports market `MY` and currency `MYR` only; other codes are rejected.
Claim amounts are decimal ringgit in the application and signed 64-bit integer sen
in SQLite (`ClaimedAmountSen` and `EstimatedLiabilitySen`). Conversion is checked:
values must be representable in whole sen with no rounding, within the Int64 range;
claimed amounts must be positive and estimated liability may be zero. For example,
RM 0.10 and RM 0.20 are stored as 10 and 20 sen. Integer sums are exact but can
overflow Int64; any future reporting must handle that limit. Broader markets and
currencies, including differing minor-unit scales, are deferred.
Claim status values are defined centrally in `Domain/Entities.cs`, but allowed
transitions and role-based actions are deferred to the claims workflow phase.
