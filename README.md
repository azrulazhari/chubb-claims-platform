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
