# Stock Replenishment Request System

Production line workers request material replenishment for their station; requests go through an
approval workflow, with a slow external stock availability check that runs in the background.

```
Draft → Submitted → Approved → Fulfilled
            ↘ Rejected
```

## Running it

Requires the **.NET 10 SDK**. Nothing else: the database is SQLite in-memory and is seeded on startup.

```bash
dotnet build
dotnet run --project src/StockReplenishment.Api
```

or `cd src/StockReplenishment.Api` and plain `dotnet run`. `dotnet run` from the repository root does not
work: it can only start a project, not a solution.

Open **http://localhost:5080**. The API host also serves the Blazor WebAssembly client, so this one command
starts everything. (In Visual Studio / Rider, set `StockReplenishment.Api` as the startup project.)

Run the tests:

```bash
dotnet test
```

### Things to try

1. The list opens with 30 seeded requests in every state. Filter by status, priority and location,
   search (`RR-000012`, `bolt`, `Maria`), sort, and page.
2. Two seeded requests are *Submitted* with a pending stock check. The background worker picks them up
   on startup and their spinners turn into results within a few seconds, without a page reload.
3. **New request**: pick a location and some articles, then **Save and submit**. The page returns
   immediately and shows "Checking stock availability…". The result is pushed to the page when the
   simulated service answers (3–8 s).
4. Try **Approve** as the requester and the API refuses (409): a request must be reviewed by someone else.
   Switch user in the top-right corner (e.g. *Erik Johansson – Reviewer*), then approve and **Mark as fulfilled**.
5. The simulated service fails 15% of calls. The worker retries with backoff. If every attempt fails, the
   request shows *Stock check failed* with a **Retry check** button (seeded examples: filter by
   *Submitted* and look for the red icon).

**Swagger UI** at **http://localhost:5080/swagger** lets you try every endpoint from the browser ("Try it out").
Fill in the `X-User-Name` field to choose who you act as, e.g. `Erik Johansson` to approve. The raw OpenAPI
document is at `/openapi/v1.json`. Both are available in Development, which is what `dotnet run` uses.
`src/StockReplenishment.Api/StockReplenishment.Api.http` contains the same kind of calls for VS / Rider / VS Code.

## Solution layout

| Project | Purpose |
|---|---|
| `StockReplenishment.Contracts` | DTOs, input models (with DataAnnotations), enums and constants shared by API and client |
| `StockReplenishment.Api` | ASP.NET Core controllers, domain model, EF Core, background stock check worker, SignalR hub; hosts the client |
| `StockReplenishment.Client` | Blazor WebAssembly + MudBlazor UI, talking to the API over HTTP only |
| `StockReplenishment.Api.Tests` | NUnit + NSubstitute: domain, service, background processor, persistence and HTTP integration tests |

Inside the API project:

```
Domain/          Aggregate root (ReplenishmentRequest) with all workflow rules, plus domain exceptions
Data/            DbContext, entity configurations, in-memory SQLite lifetime, demo data seeder
Application/     ReplenishmentRequestService and CatalogService, plus abstractions (ICurrentUser, IStockCheckQueue, IRequestNotifier)
StockChecks/     Simulated external service, queue, processor (retry/timeout), BackgroundService worker
Infrastructure/  Exception → ProblemDetails mapping, SignalR hub and notifier, current user from header
Controllers/     Thin controllers: HTTP semantics only
```

## Data model

```
StockLocations 1──* ReplenishmentRequests 1──* ReplenishmentRequestLines *──1 Materials
                              │
                              └──* RequestStatusChanges   (audit trail)
```

- **ReplenishmentRequests**: location, priority, status, requester, timestamps, reviewer/rejection reason,
  fulfilment info, stock check state/message/result, and a `Version` concurrency token.
- **ReplenishmentRequestLines**: material, requested quantity, quantity reported available by the
  stock check, fulfilled quantity. `(RequestId, MaterialId)` is unique, so an article can appear only once per request.
- **Materials**: article catalog (unique article number, description, unit). Lines reference the catalog
  instead of copying free text, so the API rejects unknown article numbers.
- **RequestStatusChanges**: one row per transition (who, when, comment), shown as the history timeline.
- Indexes cover the list filters (status + created, priority, location, stock check state).
- Quantities are `decimal(18,3)` because some materials are measured in kg or litres.

## Design decisions

**Slow external stock check: 202 Accepted + background worker + push.**
`POST /submit` validates the transition, persists `Status=Submitted, StockCheckStatus=Pending`, puts the
request id on an in-process `Channel`, and returns **202 Accepted** with a `Location` header in milliseconds.
A `BackgroundService` consumes the channel and runs up to `MaxConcurrentChecks` checks in parallel. Each
check has a per-attempt timeout and exponential-backoff retries, and ends as `Completed` or `Failed`.
The client learns about the result in two ways:
- **Push**: a SignalR hub broadcasts `RequestUpdated`, and the Blazor pages refresh the affected request.
  The UI reloads after a reconnect, in case it missed messages.
- **Poll**: any HTTP client can GET the `Location` URL until `stockCheckStatus` is no longer `Pending`.
  The integration tests do this.

The persisted `Pending` state is the source of truth and the channel only signals the worker. On
startup the worker re-queues every pending check, so nothing is lost on a restart. The request is not
tracked by EF while the slow call is in flight; it is reloaded afterwards. If it was rejected in the
meantime, the late result is discarded.

**Workflow rules live in the aggregate.** `ReplenishmentRequest` exposes intention-revealing methods
(`Submit`, `Approve`, `Reject`, `Fulfill`, …) with private setters, so an invalid transition cannot be
written from anywhere else. Rules enforced:
- Only drafts can be edited, submitted or deleted. A request needs at least one line, with no duplicate
  articles and quantities greater than zero.
- Approval requires a *completed* stock check. Approval is allowed when some items are short, because the
  reviewer sees the shortage and decides. Rejection is allowed at any point while Submitted. It cancels a
  pending check and requires a non-blank reason.
- The requester cannot approve or reject their own request (segregation of duties).
- Fulfilment must give a quantity, between 0 and the requested quantity, for every line.
- A failed check can be retried (`POST /stock-check`).

**Errors are RFC 9457 problem details.** `ApiExceptionHandler` maps exceptions to responses:

| Situation | Status |
|---|---|
| Malformed input / DataAnnotations failures (automatic via `[ApiController]`) | 400 `ValidationProblemDetails` |
| Business rule violations (unknown location/article, missing reason, bad fulfilment quantities) | 400 with field errors |
| Unknown request | 404 |
| Operation not valid in the current state (approve a draft, delete a submitted request, …) | 409 |
| Concurrent modification (optimistic concurrency token) | 409 |
| Unknown `/api/*` route / known route with the wrong verb | 404 / 405 (never the SPA's `index.html`) |

**Concurrency.** The background worker and reviewers can write to the same request. `Version` is an
EF concurrency token, incremented in `SaveChanges`, so the second writer gets a conflict instead of
silently overwriting the first. The worker reloads and retries; an API caller gets 409.

**Pagination.** `GET /api/replenishment-requests?status=&priority=&locationId=&search=&sort=&page=&pageSize=`
returns `{ items, page, pageSize, totalCount, totalPages }`. `pageSize` is capped at 100. Results always
have a deterministic order: the sort key, then id.

**Database.** SQLite in-memory rather than the EF InMemory provider, because it is relational: foreign
keys, unique indexes, transactions and concurrency tokens actually behave like they would in SQL Server.
A shared-cache in-memory database is kept alive by one open connection, while each `DbContext` gets its
own connection, since connections are not thread-safe. Connections use `PRAGMA read_uncommitted`, because
shared-cache table locks otherwise let concurrent readers and writers deadlock ("database table is locked"). Priority is stored as its ordinal so "sort by
priority" is meaningful. Timestamps are UTC, and a value converter restores `DateTimeKind.Utc`, which
SQLite drops.

**Users.** Authentication is out of scope. The acting user comes from an `X-User-Name` header behind
`ICurrentUser`. The UI's user switcher sets this header, so the review rules can be demonstrated. In
production `ICurrentUser` would read the authenticated principal, and role-based authorization
(worker / reviewer / warehouse) would be added to the endpoints.

## API

| Method | Route | Result |
|---|---|---|
| GET | `/api/replenishment-requests` | 200 paged list (filters above) |
| GET | `/api/replenishment-requests/{id}` | 200 request with lines and history / 404 |
| POST | `/api/replenishment-requests` | 201 + `Location` (creates a Draft) |
| PUT | `/api/replenishment-requests/{id}` | 200 (replace draft content) / 409 if not a draft |
| DELETE | `/api/replenishment-requests/{id}` | 204 / 409 if not a draft |
| POST | `/api/replenishment-requests/{id}/submit` | **202** + `Location`; stock check runs in background |
| POST | `/api/replenishment-requests/{id}/stock-check` | 202, retries a failed check |
| POST | `/api/replenishment-requests/{id}/approve` | 200, optional `{ comment }` |
| POST | `/api/replenishment-requests/{id}/reject` | 200, `{ reason }` required |
| POST | `/api/replenishment-requests/{id}/fulfill` | 200, `{ lines: [{ lineId, fulfilledQuantity }] }` |
| GET | `/api/locations` | 200 |
| GET | `/api/materials?search=&limit=` | 200 |
| WS | `/hubs/requests` | SignalR, server → client `RequestUpdated` |

## Configuration (`appsettings.json`)

| Setting | Default | |
|---|---|---|
| `StockCheck:MaxConcurrentChecks` | 4 | parallel calls to the external service |
| `StockCheck:MaxAttempts` | 3 | attempts before the check is marked Failed |
| `StockCheck:AttemptTimeout` | 20 s | per attempt |
| `StockCheck:RetryBaseDelay` | 2 s | doubled per retry |
| `SimulatedStockService:MinLatency` / `MaxLatency` | 3 s / 8 s | simulated response time |
| `SimulatedStockService:FailureRate` | 0.15 | probability a call fails |
| `Database:SeedDemoData` | true | |

## Tests

73 tests, a few seconds:
- **Domain**: every transition, including invalid ones; validation rules; stock check outcomes; the
  rule that requesters cannot review their own requests; cancelling a check on rejection.
- **Service** (real SQLite, NSubstitute for queue/notifier/user): create/update/delete; submit persists
  and enqueues exactly once; fulfilment rules; filtering, paging, sorting and search.
- **Stock check processor** (NSubstitute for the external service): success, transient failure followed by a
  retry, permanent failure, timeout, rejection while in flight (result discarded), skipping requests that
  no longer need a check.
- **Persistence**: the concurrency token detects conflicting writers; timestamps come back as UTC.
- **HTTP integration** (`WebApplicationFactory`): status codes, problem details, `Location` headers,
  the asynchronous submit → poll → completed flow, and the complete approve → fulfil workflow.

## What I'd do next for production

- A durable queue (an outbox table plus Azure Service Bus or RabbitMQ) instead of the in-process channel,
  so that checks survive across instances.
- Real authentication and role-based authorization, and SignalR groups per request or user instead of
  broadcasting to everyone.
- SQL Server with EF migrations, using `rowversion` as the concurrency token.
- `ETag` / `If-Match` on mutating endpoints, so a client cannot act on a stale view.
- Polly / `Microsoft.Extensions.Resilience` (circuit breaker) around the real stock service client.
