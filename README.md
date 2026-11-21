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
**Migrations are offline maintenance:** stop all API instances and every other
writer before starting the application or applying migrations, and back up the
database first. The Approved-status migration rebuilds tables in one transaction
with foreign keys enabled; it refuses extra indexes, triggers, and foreign-key
references that it cannot preserve. Its downgrade is intentionally unsupported.
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
## Demo claims API

Claims endpoints accept `X-Demo-User-Id` only when `ASPNETCORE_ENVIRONMENT` is
`Development`. Seeded IDs are `1` (claimant) and `2` (officer). This header is
impersonation for local demonstration, not authentication: never use this mechanism
for real data. Outside Development all claims endpoints return 401 until real
authentication is implemented. `/health` remains public.

Request and response bodies use JSON; monetary amounts are decimal MYR. Claims are
always MY/MYR. Date values use `YYYY-MM-DD` and timestamps are UTC. A claimant sees
only their own claims; an officer can list and view all claims, but only the assigned
officer can act on a claim. Claim history and information requests are included in
claim responses. API operations are:

- `POST /api/claims`: `{ "claimType": "Motor", "incidentDescription": "Damage", "incidentDate": "2026-01-02", "claimedAmount": 12.34 }`. A claimant creates a Submitted claim; Property is also supported.
- `GET /api/claims` and `GET /api/claims/{id}`: list visible claims or retrieve one.
- `POST /api/claims/{id}/assign`: an officer takes an unassigned Submitted claim, moving it to UnderReview.
- `POST /api/claims/{id}/information-requests`: assigned officer sends `{ "message": "..." }`, moving UnderReview to InformationRequired.
- `POST /api/claims/{id}/respond`: the owning claimant sends `{ "message": "..." }`, returning InformationRequired to UnderReview.
- `POST /api/claims/{id}/decision`: assigned officer sends `{ "decision": "Approve", "reason": "..." }` or uses `Reject`. The resulting Approved or Rejected claim is terminal. Approval records a decision only; it does not pay or settle a claim.

The database stores InformationRequired as `InfoRequested`. New approvals are stored
as `Approved`; existing `Settled` records retain their legacy status and are not
reinterpreted as approvals. This MVP has no settlement operation. Changes are recorded
in claim history. Conditional database updates and a transaction make
assignment and its history atomic. Creation returns 201; invalid requests return
400, missing identity 401, wrong roles 403, inaccessible claims 404, and invalid
transitions 409. Demo identity does not provide production authorization.

## Officer dashboard

`GET /api/reports/dashboard` requires a Development demo claims officer, for example
`curl -H 'X-Demo-User-Id: 2' http://localhost:5080/api/reports/dashboard`.
Claimants receive 403; missing or invalid identities and requests outside Development
receive 401. The demo header is not production authentication.

Example response:

```json
{
  "totalClaimCount": 6,
  "outstandingCount": 3,
  "approvedCount": 1,
  "rejectedCount": 1,
  "unassignedOutstandingCount": 1,
  "outstandingEstimatedLiability": 0.33,
  "currencyCode": "MYR",
  "officerWorkload": [
    { "officerId": 2, "displayName": "Demo Claims Officer", "outstandingCount": 2 },
    { "officerId": 3, "displayName": "Second officer", "outstandingCount": 0 }
  ]
}
```

Outstanding means `Submitted`, `UnderReview`, or `InfoRequested`. `Approved`,
`Rejected`, and legacy `Settled` are excluded; Approved and Settled are distinct.
The liability is an estimate on outstanding claims, not a settled or paid amount.
Workload includes each officer, including those with zero assigned outstanding claims.
All values are MYR only. The server reads integer sen and adds them with checked
Int64 arithmetic; if the sum exceeds Int64, it returns HTTP 422 rather than an
inaccurate total. This implementation scans claims for each dashboard request.

## Angular frontend foundation

The standalone Angular application is in `frontend/`. It uses the seeded demo
claimant (ID 1) and claims officer (ID 2). The selector is local Development-only
impersonation, not authentication. The backend still rejects demo identities outside
Development; the UI does not implement production authentication.

The Angular 22 toolchain supports Node.js `^22.22.3`, `^24.15.0`, or `^26.0.0`.
From the repository root, use two terminals:

```sh
# Terminal 1 — stop other database writers before Development migrations
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Chubb.Claims.Api --no-launch-profile -- --urls http://localhost:5080
```

```sh
# Terminal 2
cd frontend
npm ci
npm start
```

Open `http://localhost:4200`. The Angular development server proxies `/api` and
`/health` to `http://localhost:5080`; no backend CORS change is required. The
selector adds `X-Demo-User-Id` only to same-origin API calls in development builds.
Select **Demo Claimant · ID 1** to submit a Motor or Property claim, view its
list and details, or respond when information is required. The server enforces
ownership and permissions; choosing a view is not authorization. Amount input is
validated as a decimal string and sent as an exact JSON numeric token without
floating-point conversion. The existing API returns JSON numbers, which browsers
parse as JavaScript numbers: very large returned MYR amounts can lose precision
when displayed. Select **Demo Claims Officer · ID 2** to view the workload
dashboard and claims queue, inspect claim history and information requests, take
an unassigned Submitted claim, request information, or approve/reject an
UnderReview claim assigned to that officer. A decision requires a reason;
approval does not settle or pay a claim. The dashboard and claim details display
API JSON numbers directly, so very large amounts may lose precision in the browser.
The backend remains responsible for authorization and transition checks.

For frontend verification, run `npm run build` and `npm test -- --watch=false`
from `frontend/`.
