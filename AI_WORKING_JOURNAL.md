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

---

### Next Step
Lock in the .NET version, database, and status transition rules, then use Codex to scaffold the initial backend project structure.

## Entry 002 — Backend Scaffolding & Verification
**Focus:** Getting the base .NET API and test project up and running cleanly.

### What I asked ChatGPT / Codex
Prompted Codex to scaffold a minimal .NET 10 ASP.NET Core Web API along with an xUnit integration test project so I have a clean baseline before adding domain logic.

### What I accepted
* **Base project structure & health check:** Accepted the generated solution layout and the `GET /health` endpoint (verified locally returning `200 OK` with `{"status":"ok"}`). Build compiled with zero warnings/errors and the initial integration test passed.
* **Deliberate scope deferral:** Kept this step strictly to project setup—held off on database wiring, claims workflows, auth, Kafka, and Angular so I could verify the foundation first.

### What I challenged / ran into
* **Verification hiccups:** Ran a separate read-only audit pass to sanity-check the generated scaffolding before moving on. The first check hit sandbox permission and timeout issues, so I re-ran it with the right permissions to confirm everything actually built and booted properly.

### Next Step
Add the database layer and define the core claim entity and money handling rules.

---

## Entry 003 — SQLite Persistence & Money Representation
**Focus:** Setting up EF Core persistence and fixing how currency amounts are stored.

### What I asked ChatGPT / Codex
Asked Codex to wire up EF Core 10 with SQLite and generate the initial domain and persistence models for claims.

### What I accepted
* **Single-market MVP scope (Malaysia / MYR):** Even though the brief mentions six APAC markets, I locked the MVP to Malaysia and `MYR` only with explicit database constraints, keeping the schema focused while still leaving room to expand later.
* **Test coverage expansion:** Built out the test suite from 9 to 18 passing tests and verified migrations against a fresh temporary SQLite database.

### What I challenged / overrode
* **Money storage (`TEXT`/`REAL` vs. `Int64` sen):** Codex initially stored `decimal` amounts as `TEXT` in SQLite and wrote database check constraints that cast values to `REAL`. I pushed back on casting money to floating-point `REAL` at the DB layer because of precision risks. Even after double-checking that EF Core handles C# `decimal` math safely in memory, raw SQLite queries and constraints still needed strict integer precision. I overrode the schema to keep `decimal` ringgit properties in the C# domain model while persisting checked `Int64` sen values in SQLite.
* **Deferred items & known trade-offs:** Held off on claim state transitions, role checks, auth, Kafka, and Angular for the next step. Noted that using `Int64` sen works cleanly for individual claims, though large multi-claim aggregations would need overflow protection in a full production build.

### Next Step
Implement the claim status transition rules, role-based actions (claimant vs. staff), and workload/exposure queries.

---

## Entry 004 — Claims Workflow, Role Checks & Migration Safety
**Focus:** Building out the main claims REST endpoints, locking down status transitions, and fixing a risky SQLite migration.

### What I asked ChatGPT / Codex
Prompted Codex to build the core claims workflow endpoints in ASP.NET Core (submit, track, assign officer, request/submit extra info, and approve/reject), along with role checks, an audit history log, concurrency protection on assignments, and integration tests. Ran the generated code through ChatGPT to catch bugs or edge cases before committing.

### What I accepted
* **State machine enforcement:** Implemented the lifecycle `Submitted` -> `UnderReview` -> `InfoRequested` -> `UnderReview` -> `Approved` / `Rejected`. Kept transition validation centralized in one place so endpoints can't bypass workflow rules.
* **Dev-only identity header (`X-Demo-User-Id`):** Used a simple header to simulate claimants and claims officers for local testing, with strict role and ownership checks on every endpoint—and explicitly blocked the header from working outside the `Development` environment.
* **Atomic claim assignment:** Handled officer pickup with a conditional database update so two officers clicking "assign" at the same time won't overwrite each other, writing the assignment and history entry in a single transaction.
* **Audit trail & test coverage:** Logged key events (submission, pickup, info requests/responses, final decision) and expanded the integration test suite from 18 to 28 passing tests with zero build warnings.

### What I challenged / overrode
* **`Approved` vs. `Settled` domain logic:** Codex initially mapped claim approval straight to the `Settled` status. I pushed back on this because in insurance, approving a claim isn't the same thing as paying it out. Split `Approved` into its own status while keeping `Settled` intact for existing records.
* **Risky SQLite table-rebuild migration:** Because SQLite has limited `ALTER TABLE` support, EF Core migrations often rebuild tables under the hood. The first migration Codex generated split the rebuild across multiple transactions, which risked losing data if anything failed midway. I rejected it and had it rewritten as an additive migration inside a single transaction that preserves all existing claims, history, info requests, foreign keys, and indexes. Added tests for both the upgrade path and forced-failure rollbacks, and noted that running it requires stopping writers and backing up the DB first.
* **Scope discipline:** Kept full auth, payout/settlement flows, Kafka, and Angular out of this commit so I wouldn't blow past the 5-hour cap before finishing the officer reporting endpoints.

### Next Step
Commit this batch of work, then build the claims officer workload and MYR liability exposure summary endpoints before starting on a minimal Angular UI.

---

## Entry 005 — Claims Reporting & Officer Workload Dashboard
**Focus:** Building the reporting endpoint for staff workload and outstanding MYR liability exposure.

### What I asked ChatGPT / Codex
Prompted Codex to build a dashboard reporting endpoint on the existing ASP.NET Core + SQLite backend. Asked for claim status breakdowns, per-officer active workload counts, unassigned queue counts, and total outstanding liability in MYR—along with strict role checks, exact integer math, and integration tests.

### What I accepted
* **Dashboard endpoint (`GET /api/reports/dashboard`):** Returns total claims, active/outstanding counts, approved/rejected counts, unassigned queue size, and total estimated liability.
* **Clear definition of "outstanding":** Counted only `Submitted`, `UnderReview`, and `InfoRequested` claims toward active workload and exposure—excluding terminal states (`Approved`, `Rejected`, and legacy `Settled`).
* **Full officer workload visibility:** Returned active claim counts for every claims officer, explicitly keeping officers with `0` assigned claims in the list so managers can see idle capacity.
* **Access control & tests:** Locked the endpoint down to claims officers (`403 Forbidden` for claimants, `401 Unauthorized` for missing/invalid identities) and expanded the integration test suite from 28 to 37 passing tests with zero build warnings.

### What I challenged / overrode
* **Integer overflow & precision on liability totals:** Since Entry 003 flagged `Int64` sen aggregation overflow as a risk, I made sure Codex summed the raw `Int64` sen columns directly rather than converting to floating-point values, and wrapped the sum in `checked` arithmetic. If total liability ever exceeds the supported `Int64` range, the API explicitly fails with `422 Unprocessable Entity` rather than silently overflowing into a wrong dollar figure.
* **Consistent snapshot reads:** Wrapped the dashboard queries in a database read transaction so all counts and liability totals reflect the exact same point-in-time snapshot rather than shifting mid-request.
* **No premature caching or background workers:** Kept metrics computed directly on read for the MYR-only MVP instead of adding Redis, materialized views, or background aggregators within the 5-hour cap.

### Next Step
Commit the reporting endpoint, then scaffold a lean Angular frontend covering the core claimant and claims officer flows and run end-to-end tests against the API.

---

## Entry 006 — Angular Foundation & API Integration
**Focus:** Scaffolding the Angular frontend, wiring up the dev proxy to the .NET API, and separating claimant vs. officer views.

### What I asked ChatGPT / Codex
Prompted Codex to generate a minimal Angular frontend inside the existing repo (matching my local Node runtime) with separate routes for claimants and claims officers, a dev-only user switcher, typed API client services, and a live health-check banner. Asked it to verify that the frontend builds, passes tests, and talks cleanly to the ASP.NET Core backend.

### What I accepted
* **Angular 22.3 standalone setup:** Built a single Angular app using standalone components with no heavy third-party UI libraries so it stays fast and easy to maintain within the assessment window.
* **Role-based routing & dev switcher:** Set up distinct claimant and claims officer routes with placeholder views, wired to a local-only identity selector matching the backend's seeded users (clearly isolated from real auth).
* **Dev proxy instead of CORS changes:** Configured Angular's dev proxy to forward `/api` and `/health` calls to `localhost:5080`, letting frontend and backend communicate locally without relaxing CORS rules on the API.
* **Health-check status & verification:** Built explicit loading, success, error, and retry states on the health indicator, verified a `200 OK` round-trip through the proxy against a temporary database, and confirmed the production build succeeded.

### What I challenged / overrode
* **Broken starter tests:** The generated tests initially failed because they were still asserting against Angular's default boilerplate welcome page. Rewrote them to test the actual app layout and routes, bringing the suite to 2/2 passing tests.
* **JavaScript floating-point money risk:** Flagged early that standard JS `number` handling can introduce rounding errors on currency values. Held off on writing monetary inputs and formatting in this scaffolding commit so I can handle MYR/sen string/decimal formatting cleanly when building the claimant submission form.
* **npm install-script warnings:** `npm install` flagged pending lifecycle script approvals, but since the build and tests ran cleanly without them, I left the lockfile/config untouched instead of adding unnecessary npm overrides—just noted it here for transparency.

### Next Step
Build out the claimant workflow screens (submit claim, view list/details, respond to info requests), followed by the claims officer queue and dashboard views, then run full end-to-end checks.

---

## Entry 007 — Claimant Portal & Financial Input Validation
**Focus:** Building the claimant-facing Angular UI and handling MYR inputs without JavaScript floating-point rounding bugs.

### What I asked ChatGPT / Codex
Prompted Codex to replace the claimant placeholder page with a working Angular view wired to the existing ASP.NET Core endpoints. Asked for Motor and Property claim submission, claim listing, status/history tracking, information request responses, form validation, and frontend tests—specifically ensuring MYR amounts don't suffer from JS float precision issues and that switching demo identities never leaks stale data from another user.

### What I accepted
* **Full claimant flow:** Implemented Motor and Property claim submission, claim lists, detail views with audit history, and inline responses to open information requests—reusing the existing backend API contracts with zero backend or schema changes needed.
* **Exact JSON serialization for MYR amounts:** Validated currency input strings directly into integer sen values before sending them over the wire, serializing them as raw JSON numeric tokens instead of passing them through JS `number` floats.
* **Safe demo identity switching:** Made the UI immediately wipe active claimant state and abort in-flight HTTP requests whenever the demo identity dropdown changes, preventing a previous user's response from rendering under the new identity.
* **Test coverage & smoke check:** Grew the Angular test suite from 2 to 17 passing tests, verified a clean production build, and ran a proxy smoke test covering submission, officer info requests, and claimant replies.

### What I challenged / overrode
* **No standard JS float parsing on form inputs:** Rejected parsing monetary form inputs via `parseFloat` or `Number(...)` since binary floats can silently distort ringgit/sen amounts before submission.
* **Documented response-side precision limit:** Noted that while outgoing claim amounts are serialized cleanly from integer sen, the backend still returns standard JSON numbers—meaning extremely large amounts could still lose precision on the JS read side unless the API serializes money as strings or integer sen. Flagged this explicitly rather than pretending the frontend solved end-to-end precision alone.
* **Scope discipline:** Kept this commit strictly focused on the claimant portal and left the claims officer queue, dashboard metrics, and decision actions for the final UI pass.

### Next Step
Commit the claimant portal, then build the claims officer workspace (dashboard metrics, queue pickup, info requests, and approve/reject actions) and run a full end-to-end walkthrough across both roles.

---

## Entry 008 — Claims Officer Portal & End-to-End Workflow Integration
**Focus:** Building the claims officer Angular workspace, wiring up dashboard metrics and claim decisions, and verifying the full end-to-end lifecycle.

### What I asked ChatGPT / Codex
Prompted Codex to replace the claims officer placeholder with a working Angular view connected to the existing ASP.NET Core endpoints. Asked for dashboard summary cards, per-officer workload counts, the incoming claims queue and detail drawer/view, self-assignment, information requests, and approve/reject actions—along with error handling, safe identity-switching behavior, and integration tests.

### What I accepted
* **Live officer dashboard:** Hooked up `GET /api/reports/dashboard` to show total claims, outstanding MYR liability exposure, unassigned queue count, and active workload per officer.
* **Claims queue & decision actions:** Built the officer queue and detail views (showing incident details, audit history, current assignee, and info request threads) along with self-assignment, request-info, and approve/reject actions requiring a decision reason. Left all actual state transition and permission enforcement on the backend.
* **State refresh & identity isolation:** Triggered automatic refreshes across dashboard metrics, queue lists, and the selected claim after any mutation, and cleared role state + aborted in-flight requests whenever the demo identity switches.
* **Test coverage & proxy E2E check:** Expanded the Angular test suite from 17 to 27 passing tests with a clean production build, and ran an end-to-end smoke test through the Angular dev proxy against a fresh database (submit -> assign -> request info -> claimant reply -> approve), confirming the dashboard flipped from 1 outstanding claim to 1 approved claim.

### What I challenged / overrode
* **UI hiding vs. real backend authorization:** Kept role-based UI views for usability, but explicitly relied on the backend's role and ownership checks as the source of truth rather than treating frontend route/button hiding as security. Kept the demo identity switcher clearly scoped to local `Development` testing.
* **Display-side currency precision:** Kept the explicit caveat from Entry 007 that while outgoing MYR inputs are serialized cleanly from integer sen, large JSON numbers returned by the API could still lose precision in JS if rendered naively—avoided overclaiming full precision across arbitrary ranges.
* **Proxy smoke test vs. manual browser walkthrough:** Clearly separated the automated API/proxy round-trip verification from a real interactive browser click-through, flagging the browser check as a mandatory step before final submission.
* **Scope discipline:** Reused existing API contracts with zero new infrastructure, auth providers, or Kafka brokers—prioritizing a complete, reliable end-to-end claims lifecycle within the 5-hour assessment limit.

### Next Step
Commit the officer portal, run a full interactive browser walkthrough across both roles, rerun the complete backend and frontend test suites, clean up documentation and Git history, and package everything for submission.

---

## Entry 009 — Final UI Corrections, Business Validation & Verification
**Focus:** Fixing issues found during manual Chrome testing and final code review, and verifying the complete claims workflow.

### What I asked ChatGPT / Codex
During manual Chrome testing, I spotted a desktop layout bug where the officer workspace dropped below the sidebar instead of sitting beside it, and noticed the claimant form allowed future incident dates. After fixing those, I had Codex do a final pass over the codebase for any remaining correctness issues—which surfaced an async response race condition in the claimant view that I asked Codex to patch and cover with tests.

### What I accepted
* **Desktop shell grid fix:** Updated the Angular layout grid so the sidebar and routed workspace sit side-by-side on desktop screens while keeping mobile stacking and table horizontal scrolling intact.
* **Dual-layer incident date validation:** Locked incident dates to today or earlier using the Malaysia calendar date (`Asia/Kuala_Lumpur`), enforced independently on both the Angular form and `POST /api/claims` (returning `400 Bad Request` on future dates). Added tests covering yesterday, today, tomorrow, and the UTC/Malaysia midnight boundary.
* **Claimant async race-condition guard:** Added selected-claim ID and selection-version tracking so delayed HTTP responses or errors from an earlier selection can't overwrite the details, messages, or loading state of a newly selected claim.
* **Regression tests & manual Chrome walkthrough:** Backend tests reached 43 passing tests, and frontend tests reached 32 passing tests (covering delayed responses, stale errors after navigation, and identity-switch cancellations) with clean builds. Completed a full manual Chrome walkthrough (submit -> assign -> info request -> claimant reply -> approve -> dashboard refresh) and confirmed the fixed desktop layout.

### What I challenged / overrode
* **Passing tests != working UI:** Didn't trust headless test runs alone—manual browser testing caught the desktop grid alignment issue that automated tests missed.
* **Server-side date enforcement:** Refused to rely solely on HTML date-picker constraints; required independent backend validation so direct API requests can't submit future incidents.
* **Non-blocking stale-response handling:** Required stale callbacks to quietly discard outdated responses based on claim ID/version rather than locking or blocking user navigation while requests are in flight.
* **Honest testing scope:** Explicitly noted that the manual Chrome walkthrough happened before the final async race-condition patch, while the race fix itself was verified via focused frontend unit/integration tests.
* **Clean commit boundaries:** Kept an unrelated local Angular CLI analytics config change separate from these fixes so the Git history stays clean.

### Next Step
Commit the final fixes, do a quick pass over the README and Git commit history, and package the repo for submission.

---