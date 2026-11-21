# AI Working Journal — Chubb Claims Platform

## Entry 001 — Initial Setup & Architecture Scoping
**Focus:** Breaking down the brief, choosing the stack, and setting boundaries before writing code.

### What I asked ChatGPT / Codex
Fed the assessment brief into ChatGPT to help map out the domain boundaries and decide on an architecture that fits a 3-to-5-hour sprint. Asked for a comparison between spinning up microservices right away vs. starting with a single service, plus advice on what to prioritize first.

### What I accepted
* **Backend-first approach:** The brief explicitly says a solid backend with a partial frontend scores better than rushing both, so I’m spending most of my time on the API, domain logic, and state transitions first.
* **Core stack (C# / ASP.NET Core + Angular):** Went with the brief's preferred stack. C#/.NET is a strong fit for enforcing strict claim validation and state rules, and keeping Angular on the list avoids unnecessary friction later if I have time for the UI.
* **Modular monolith over microservices:** ChatGPT suggested splitting into separate services (e.g., Claims vs. Notifications/Reporting), or keeping it in one app. Given the 5-hour hard cap, managing multi-service deployments and distributed transactions will just eat up time. Starting with a modular monolith keeps the domain logic cleanly separated by folder/module without the infra overhead.
* **Milestone-based Git commits:** Committing at clear checkpoints rather than one giant commit at the end so the progression is easy to trace.

### What I challenged / still figuring out
* **Kafka integration:** The brief lists Kafka as preferred, and ChatGPT mapped out an event-driven flow for claim status updates. I’m pushing back on adding a full Kafka broker setup right now—if I spend an hour debugging Docker/Kafka networking, I’ll lose time on core business logic. Still deciding whether to wire up a lightweight Kafka setup via Docker Compose or use an in-memory event bus/outbox pattern that mimics the async flow cleanly within the time limit.
* **Database choice:** Weighing SQLite or Postgres in Docker. SQLite is faster to get running locally with zero setup, while Postgres is closer to production—need to lock this down before generating the initial solution.
* **Claims state machine:** Need to explicitly define the exact status transitions (e.g., `Submitted` -> `UnderReview` -> `InfoRequested` -> `Settled`/`Rejected`) myself before letting Codex scaffold the entities, so it doesn't overcomplicate the schema.

### Next Step
Lock in the .NET version, database, and status transition rules, then use Codex to scaffold the initial backend project structure.

## Entry 002 — Backend Scaffolding and Audit
**Focus:** Establishing and verifying the initial backend foundation.

Codex generated the .NET 10 ASP.NET Core API and xUnit test project. A separate read-only audit examined the implementation and verified the foundation before acceptance.

The build succeeded with zero warnings and zero errors, and one HTTP endpoint integration test passed. The API also started locally, and `GET /health` returned HTTP 200 with JSON `{"status":"ok"}`. The audit initially encountered sandbox permission and timeout issues; the checks passed after retrying with the required permissions.

We accepted the scaffolding because it meets the initial requirements. Database integration, claims business logic, authentication, Kafka, and Angular were deliberately deferred.

---
