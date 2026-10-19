# EBI.ALAS.V2 Backend

> **ALAS** — Automated Loan Application System, version 2

A banking-grade **.NET 10** Web API that powers the full loan origination lifecycle for Enterprise Bank Inc. From the moment an encoder drafts a borrower's application to final disbursement, ALAS enforces a staged review workflow, captures every decision in an immutable per-loan action trail, signs each stage with snapshotted e-signature ink, and keeps everyone in the loop with real-time SignalR notifications.

The codebase is deliberately lean: Minimal APIs, vertical-slice features, plain EF Core, and a SQL Server-backed token store. No message broker, no external cache — everything the system needs is the API and its databases.

---

## Table of Contents

- [What Does It Do?](#what-does-it-do)
- [Architecture at a Glance](#architecture-at-a-glance)
- [Features](#features)
- [Project Structure](#project-structure)
- [Tech Stack](#tech-stack)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [How Security Works](#how-security-works)
- [The Loan Workflow](#the-loan-workflow)
- [Real-Time Features](#real-time-features)
- [API Endpoints](#api-endpoints)
- [Databases](#databases)
- [Background Services](#background-services)
- [Email Delivery](#email-delivery)
- [Seed Data](#seed-data)
- [Development Guide](#development-guide)
- [Testing](#testing)
- [Deployment Notes](#deployment-notes)

---

## What Does It Do?

At its core, ALAS manages the journey of a loan application through a multi-stage approval pipeline.

**For Loan Officers (Encoders):**
- Draft loan applications — one or several loans per borrower, grouped under a single application number
- Pull borrower and account data from the legacy WebLoan core banking system
- Get instant amortization, NTHP, and max-loanable computations
- Track document checklists, flag responses, and revision requests
- Revise and resubmit applications returned by reviewers, with full context

**For Branch Heads (Recommenders):**
- Work a branch-scoped desk queue of applications waiting for recommendation
- Recommend or push back for revision with section-specific comments
- Sign their slot in the loan's signature chain (e-signature ink snapshotted)

**For Credit Checkers (Evaluators):**
- Evaluate creditworthiness and forward to approval as Recommended / Not Recommended
- Flag loans with missing documents and notify the encoder
- Annotate checklist documents and deviation items with remarks

**For Area Heads (Approvers):**
- Approve or reject loans routed to their desk
- Push back for revision when something is missing
- Release or reclaim queue leases; expired leases are reaped automatically

**For Administrators:**
- Manage users (create, edit, suspend/activate, bulk Excel import), branches, and loan products
- Sync the loan product catalog from WebLoan; maintain per-product document requirements
- Flip workflow behavior (e.g., require/skip the recommendation stage) at runtime — no redeployment
- See every branch's queues, dashboards, and notifications

---

## Architecture at a Glance

The system uses a **vertical-slice architecture** — each feature owns its contracts, handlers, domain, persistence configuration, and endpoints in one folder. If you're working on loans, everything you need is under `Features/LoanApplications/`.

```
                        ┌──────────────────────────────┐
                        │        Frontend (SPA)        │
                        └──────────┬───────────────────┘
                                   │  HTTPS + JWT (Authorization header)
                                   │  WebSocket (SignalR, /hubs/notifications)
                                   ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                        ASP.NET Core 10 Pipeline                            │
│                                                                            │
│  ForwardedHeaders → Exception Handler (ProblemDetails) → Status Codes →    │
│  Rate Limiter → Authentication (JWT Bearer) → Authorization (Policies) →   │
│  AuthSecurity (must-change-password gate + CSRF double-submit) →            │
│  Minimal API Endpoints + SignalR Hub                                       │
│                                                                            │
│  Development only: OpenAPI document (/openapi/v1.json) + Scalar (/scalar)  │
└───────────────┬──────────────────────────────────┬────────────────────────┘
                │                                  │
                ▼                                  ▼
┌────────────────────────────────┐   ┌─────────────────────────────────────┐
│  AlasDbContext                 │   │  WebLoanDbContext (read-only)       │
│  ALASv2_DB — read/write        │   │  webloan catalog on the same SQL    │
│  SQL Server (or EF InMemory    │   │  Server host, ApplicationIntent=    │
│  in Development without a      │   │  ReadOnly, NoTracking               │
│  connection string)            │   │                                     │
│                                │   │  Checklist binaries: bpb_binary.dbo │
│  EF Core Migrations            │   │  .docs/.doc_ref via parameterized   │
│  (src/Ebi.Alas.Api/Migrations) │   │  SQL on the ALAS connection         │
└────────────────────────────────┘   └─────────────────────────────────────┘
                │
                ▼
┌────────────────────────────────────────────────────────────────────────┐
│  Hosted services: admin seeding · presence sweep (1 min) · queue       │
│  reconciliation (5 min) · token cleanup (60 min) · disbursement sync   │
│  from WebLoan (30 min)                                                 │
└────────────────────────────────────────────────────────────────────────┘
```

### Design Philosophy

- **Minimal APIs** over controllers — endpoint groups per feature, mapped explicitly in `Composition/PipelineConfiguration.cs`.
- **Vertical slices** — each feature folder contains its own contracts, handlers, domain entities, EF configurations, and endpoint mappers. No shared service layer.
- **Plain handlers, no mediator** — endpoints inject the handler they need directly. DI registration lives in one place (`Composition/ServiceRegistration.cs`).
- **Domain exceptions → RFC 9457 ProblemDetails** — `NotFound`, `Forbidden`, `Conflict`, and `Validation` exceptions are mapped by a single `IExceptionHandler`; unexpected errors never leak internals, and database failures surface as `503`.
- **`TimeProvider` everywhere** — all time flows through the injected `TimeProvider` (UTC). Philippines time (`Asia/Manila`, UTC+8) is applied explicitly where the business needs it: application numbering and dashboard "business day" boundaries.
- **Database-backed security state** — refresh tokens, the revoked-JTI blacklist, sessions' client info, and presence all live in SQL Server, so security state survives restarts and is shared across instances.
- **Optimistic concurrency** — `LoanApplications` and `WorkflowQueueItems` carry `RowVersion` columns; queue claims fail fast instead of double-assigning.
- **Validation where it belongs** — request contracts use `required`/`init` members, handlers guard their own invariants, and `IValidatableObject` + `ValidateOnStart` fail the boot on bad configuration.

---

## Features

The `Features/` directory is organized by business domain:

| Feature | Path | What It Does |
|---|---|---|
| **Auth** | `Features/Auth/` | Login, refresh rotation, logout, change-password, forgot-password (emailed temporary password), JWT issuance, DB token store |
| **Account** | `Features/Account/` | "My Account" — profile + e-signature, activity feed (date/kind filters), processed loans, recent clients, session management |
| **Users** | `Features/Users/` | Admin user management — create, list (search/role/branch filters), edit, suspend/activate, bulk Excel import |
| **Roles** | `Features/Roles/` | Role catalog and the role × permission matrix embedded in JWTs |
| **Branches** | `Features/Branches/` | Branch registry (code, name, area code, status) + branch visibility rules |
| **LoanApplications** | `Features/LoanApplications/` | The heart of the system — submission, listing, detail, workflow actions, revise, review (history/timeline/deviations/routing), checklist documents, document flags and remarks, loan groups, signature chain |
| **LoanComputation** | `Features/LoanComputation/` | Amortization (diminishing / MIC / ADO lump), interest, net proceeds, NTHP and max-loanable checks |
| **LoanProducts** | `Features/LoanProducts/` | Product catalog, policy fields, WebLoan sync, Excel import/export, per-product document requirements |
| **Approvals** | `Features/Approvals/` | Approval authority routing (tier/exposure/severity/scope), signature chain entries with ink snapshots, SLA policy |
| **Workflow** | `Features/Workflow/` | Stage queues (recommendation/evaluation/approval) with leasing, desk queues, runtime workflow configuration |
| **Deviations** | `Features/Deviations/` | Deviation catalog (21 items), loan deviations, deviation remarks |
| **Documents** | `Features/Documents/` | Checklist item tracking and document remarks |
| **Revisions** | `Features/Revisions/` | Pushback revision requests and resolution |
| **Notifications** | `Features/Notifications/` | Persisted in-app notifications + SignalR hub and real-time delivery |
| **Presence** | `Features/Presence/` | Heartbeat-based online directory with sweep |
| **Dashboard** | `Features/Dashboard/` | Role-scoped overview: stage counters, queues, now-serving, pushbacks, approvals, flagged documents, weekly trend (Manila business days) |
| **WebLoans** | `Features/WebLoans/` | Read-only integration with the legacy WebLoan core banking system |
| **SystemSettings** | `Features/SystemSettings/` | DB-backed runtime settings (currently the recommendation-stage toggle) |

### Composition (`Composition/`)

| Module | Path | Purpose |
|---|---|---|
| Service registration | `Composition/ServiceRegistration.cs` | Single DI composition root: options, DbContexts, handlers, JWT, rate limits, health checks, OpenTelemetry, email |
| Pipeline | `Composition/PipelineConfiguration.cs` | Middleware order + endpoint mapping |
| Auth | `Composition/Auth/` | `CallerContext`, permission constants + policy handler, CSRF guard, must-change-password gate, hub access-token resolver, claims helpers |
| Errors | `Composition/Errors/` | `ApiProblemHandler` (`IExceptionHandler`) + domain exceptions |
| Pagination | `Composition/Pagination/` | `PageRequest` / `PageResult<T>` |
| Options | `Composition/ApiOptions.cs`, `AdminSeedOptions.cs` | Strongly-typed, start-validated configuration |

### Infrastructure (`Infrastructure/`)

| Module | Path | Purpose |
|---|---|---|
| Persistence | `Infrastructure/Persistence/` | `AlasDbContext`, `WebLoanDbContext`, `DbReadyHealthCheck`, design-time factory |
| Background | `Infrastructure/Background/` | 5 hosted services (seeding, presence sweep, queue reconciliation, token cleanup, disbursement sync) |
| Email | `Infrastructure/Email/` | `IEmailSender` with 4 providers: Enterprise Email API (default), Resend, SMTP (MailKit), DevOutbox |
| WebLoans | `Infrastructure/WebLoans/` | `WebLoanReader` (EF Core LINQ against the webloan catalog) |

---

## Project Structure

```
EBI_ALAS_V2_backend/
├── Directory.Build.props                 # net10.0, C# 14, nullable, warnings-as-errors
├── Ebi.Alas.slnx                         # Solution file
├── .github/
│   └── copilot-instructions.md           # AI coding rules for this repo
├── docs/
│   └── compose/                          # Historical design docs (specs / plans / reports)
│       └── …webloan-name-prefix…         # (approach since superseded — see WebLoan section)
├── src/
│   └── Ebi.Alas.Api/
│       ├── Program.cs                    # Composition root entry (ForwardedHeaders + pipeline)
│       ├── appsettings.json              # Non-secret defaults (JWT lifetimes, email provider, paging)
│       ├── appsettings.Development.json  # User-secrets key list + dev logging (no secrets committed)
│       ├── Composition/                  # DI, pipeline, auth plumbing, errors, pagination, options
│       ├── Features/
│       │   ├── Account/                  # My Account endpoints + service
│       │   ├── Approvals/                # Routing, signature chain entries, SLA policy
│       │   ├── Auth/                     # Login / Refresh / Logout / ChangePassword / ForgotPassword slices
│       │   ├── Branches/                 # Branch registry + visibility
│       │   ├── Dashboard/                # Overview aggregation
│       │   ├── Deviations/               # Catalog, loan deviations, remarks
│       │   ├── Documents/                # Checklist items, document remarks
│       │   ├── LoanApplications/         # Submit, list, get, workflow actions, revise, review,
│       │   │                             #   documents, flags, groups, signature chain
│       │   ├── LoanComputation/          # Amortization / NTHP / max-loanable
│       │   ├── LoanProducts/             # Catalog, sync, Excel import/export, document requirements
│       │   ├── Notifications/            # Entities, endpoints, SignalR hub + notifier
│       │   ├── Presence/                 # Heartbeat, online directory, sweep
│       │   ├── Revisions/                # Revision requests
│       │   ├── Roles/                    # RoleCatalog
│       │   ├── SystemSettings/           # Runtime settings store
│       │   ├── Users/                    # Admin user management + Excel import + admin seeder
│       │   ├── WebLoans/                 # Legacy integration endpoints, options, access rules
│       │   └── Workflow/                 # Stage queues, desk queues, workflow configuration
│       ├── Infrastructure/
│       │   ├── Background/               # Hosted services
│       │   ├── Email/                    # Email senders
│       │   ├── Persistence/              # DbContexts + health check
│       │   └── WebLoans/                 # WebLoanReader
│       └── Migrations/                   # EF Core migrations (InitialAlasSchema → …)
└── tests/
    └── Ebi.Alas.Api.Tests/               # xUnit: unit + WebApplicationFactory integration tests
```

---

## Tech Stack

| Layer | Technology | Version | Why |
|---|---|---|---|
| Runtime | .NET | 10.0 | Current, performance, C# 14 features |
| Web Framework | ASP.NET Core Minimal APIs | 10.0 | Low ceremony, high throughput |
| ORM | Entity Framework Core | 10.0.12 | Code-first, migrations, SQL Server + InMemory/Sqlite for tests |
| Database | Microsoft SQL Server | — | ALAS data (read/write) + legacy `webloan` / `bpb_binary` catalogs (read-only) |
| Real-Time | SignalR | 10.0 | Notification + presence hub over WebSocket |
| Auth | JWT Bearer | 10.0.12 | Stateless access tokens with DB-backed revocation |
| Password Hashing | ASP.NET Core Identity `PasswordHasher<T>` | built-in | PBKDF2, iterated + salted, timing-safe verification |
| Excel | ClosedXML | 0.104.2 | User & loan-product import/export (.xlsx) |
| Email | MailKit + HTTP senders | 4.18.1 | SMTP, Enterprise Email API (Graph), Resend, dev outbox |
| API Docs | Microsoft.AspNetCore.OpenApi + Scalar | 10.0.12 / 2.17.14 | OpenAPI document + Scalar UI at `/scalar` (Development) |
| Observability | OpenTelemetry | 1.14–1.19 | ASP.NET Core + HttpClient metrics and tracing (`ebi-alas-api`) |
| Health Checks | ASP.NET Core Health Checks | built-in | `/health/live`, `/health/ready` (DB probe) |
| Crypto | System.Security.Cryptography.Pkcs | 10.0.13 | PKCS support |
| Testing | xUnit + `WebApplicationFactory` | 2.9.3 | ~430 test methods across ~120 files |

No Redis, no RabbitMQ, no MediatR, no FluentValidation, no object mapper — the earlier architecture's message bus, distributed cache, and mediator pipeline were removed in favor of SQL Server-backed state and direct handlers.

### C# Language Features

- **C# 14 / .NET 10** — `LangVersion` pinned in `Directory.Build.props`, `TreatWarningsAsErrors` on
- **Nullable reference types + implicit usings** — enabled solution-wide
- **Primary constructors** — on every handler and service
- **`record` types** — immutable contracts, DTOs, and outcomes
- **Collection expressions** — `[..]` syntax throughout
- **`required` + `init` members** — on request contracts
- **`GeneratedRegex`** — source-generated validation patterns (e.g., WebLoan database names)
- **`JsonStringEnumConverter`** — enums serialize as strings

---

## Getting Started

### Prerequisites

- **.NET 10 SDK** — [download here](https://dotnet.microsoft.com/download/dotnet/10.0)
- **SQL Server** — any edition (LocalDB, Express, Developer, or full). In Development you can also run with no SQL Server at all — the API falls back to an EF InMemory database (data is lost on restart, WebLoan reads are disabled).

### 1. Clone & Restore

```powershell
git clone https://github.com/jcmcjms/EBI_ALAS_V2_backend.git
cd EBI_ALAS_V2_backend
dotnet restore
```

### 2. Configure Secrets

Secrets are never committed. Use .NET User Secrets (ID `ebi-alas-v2-backend-local`):

```powershell
dotnet user-secrets set "ConnectionStrings:Alas" "Server=YOUR_HOST;Database=ALASv2_DB;User Id=YOUR_USER;Password=YOUR_PASS;TrustServerCertificate=True;Encrypt=True" --project src/Ebi.Alas.Api
dotnet user-secrets set "Api:Jwt:SigningKey" "a-random-string-of-at-least-32-characters" --project src/Ebi.Alas.Api
dotnet user-secrets set "Api:Jwt:Issuer" "ebi-alas-dev" --project src/Ebi.Alas.Api
dotnet user-secrets set "Api:Jwt:Audience" "alas-clients-dev" --project src/Ebi.Alas.Api

# Seed admin (created on first startup if the username does not exist)
dotnet user-secrets set "Seed:AdminUserName" "admin" --project src/Ebi.Alas.Api
dotnet user-secrets set "Seed:AdminPassword" "change-me-12+chars" --project src/Ebi.Alas.Api
dotnet user-secrets set "Seed:AdminFirstName" "System" --project src/Ebi.Alas.Api
dotnet user-secrets set "Seed:AdminLastName" "Administrator" --project src/Ebi.Alas.Api
dotnet user-secrets set "Seed:AdminEmail" "admin@example.com" --project src/Ebi.Alas.Api
dotnet user-secrets set "Seed:AdminBranchId" "011" --project src/Ebi.Alas.Api

# Optional: legacy WebLoan catalog + checklist binaries (same SQL Server host)
dotnet user-secrets set "WebLoan:Database" "webloan" --project src/Ebi.Alas.Api
dotnet user-secrets set "WebLoan:BinaryDatabase" "bpb_binary" --project src/Ebi.Alas.Api

# Optional: email (forgot-password). Provider: EnterpriseEmailApi | Resend | Smtp | DevOutbox
dotnet user-secrets set "Api:Email:Smtp:Provider" "DevOutbox" --project src/Ebi.Alas.Api
```

`appsettings.Development.json` lists every supported user-secrets key. Without `ConnectionStrings:Alas` in Development, the API uses the in-memory `alas-dev` database; outside Development the connection string is **required** (startup throws).

### 3. Create / Update the Database

Migrations are applied with EF Core tooling (the API does not auto-migrate):

```powershell
dotnet tool install --global dotnet-ef   # once
dotnet ef database update --project src/Ebi.Alas.Api
```

### 4. Run

```powershell
dotnet run --project src/Ebi.Alas.Api
```

The API starts on **`https://localhost:7220`** (see `Properties/launchSettings.json`).

On first start, `AdminSeedHostedService`:
1. Creates the seed admin from the `Seed:*` options **only if that username does not exist** (existing credentials are never overwritten)
2. Seeds per-product document requirements from the built-in catalog (A16, A17, C02, C21, C23, C35) if none exist

In Development, the interactive API docs (Scalar) are available at **`https://localhost:7220/scalar`**, backed by the OpenAPI document at `/openapi/v1.json`.

---

## Configuration

Defaults live in `src/Ebi.Alas.Api/appsettings.json`; secrets come from user-secrets / environment. All option classes are validated at startup (`ValidateOnStart`) — bad configuration fails the boot, not the first request.

### Connection Strings & WebLoan

| Setting | Test | Live |
|---|---|---|
| `ConnectionStrings:Alas` | `Server=192.168.6.159,1433;Database=ALASv2_DB;...` | `Server=192.168.25.30,1433;Database=ALASv2_DB;...` |
| `WebLoan:Database` | `webloan` | `webloan` |
| `WebLoan:BinaryDatabase` | `bpb_binary` | `bpb_binary` |

WebLoan is read via `WebLoanDbContext` (EF Core LINQ) on the **same SQL Server host as ALAS**, with `Initial Catalog = WebLoan:Database` (default `webloan`) and `ApplicationIntent=ReadOnly`.

Checklist document binaries (`doc_ref` / `docs`) are read from `WebLoan:BinaryDatabase` on the same ALAS host with parameterized SQL (no linked server / `OPENQUERY`). Both database names are regex-validated (`[A-Za-z0-9_]+`) at startup.

Leave `WebLoan:Database` empty to disable live WebLoan reads (`NullWebLoanReader` — empty results).

### JWT Settings (`Api:Jwt`)

| Key | Default | Constraints | Description |
|---|---|---|---|
| `SigningKey` | *(secret)* | ≥ 32 chars, required | HMAC-SHA256 signing key |
| `Issuer` | `ebi-alas-dev` | required | Token issuer claim |
| `Audience` | `alas-clients-dev` | required | Token audience claim |
| `AccessTokenMinutes` | `15` | 1–120 | Access token lifetime (short by design) |
| `RefreshTokenDays` | `7` | 1–30 | Sliding refresh token lifetime |
| `RefreshTokenAbsoluteDays` | `14` | ≥ `RefreshTokenDays` | Hard cap — session dies regardless of rotation |

Clock skew is 30 seconds.

### Email (`Api:Email:Smtp`)

| Key | Description |
|---|---|
| `Provider` | `EnterpriseEmailApi` (default), `Resend`, `Smtp`, or `DevOutbox` (local development — writes `.eml`-style files to disk; blocked in Production) |
| `BaseUrl` / `ApiKey` / `SenderConfigId` | Enterprise Email API (`POST {BaseUrl}/api/Email/SendEmail` with `X-Api-Key`) or Resend (`re_…` key) |
| `Host` / `Port` / `From` / `UserName` / `Password` / `EnableSsl` | SMTP via MailKit |
| `DevOutboxPath` | Outbox directory (defaults to `{BaseDirectory}/email-outbox`) |

### Workflow (`Workflow`)

| Key | Default | Description |
|---|---|---|
| `RequireRecommendation` | `true` | Whether new applications start at `ForRecommendation` (vs. going straight to `ForChecking`) |
| `MinimumNthp` | `5800` | Minimum net take-home pay for affordability checks |

`RequireRecommendation` can be overridden **at runtime** via `PUT /api/workflow/configuration` (stored in the `SystemSettings` table, hot-swappable, records who changed it).

### Paging (`Api`)

| Key | Default | Description |
|---|---|---|
| `DefaultPageSize` | `20` | Page size when the client omits it |
| `MaxPageSize` | `100` | Server-side cap (absolute max 1000) |

### Rate Limiting

Two fixed-window policies, partitioned **per client IP** (reverse-proxy `X-Forwarded-For` is honored):

| Policy | Limit | Applied to |
|---|---|---|
| `auth` | 10 requests / minute | `login`, `refresh`, `change-password`, `forgot-password` |
| `write` | 30 requests / minute | State-changing endpoints (create/update/import/claim/read-marking, etc.) |

Exceeding a limit returns `429 Too Many Requests`.

---

## How Security Works

### Authentication Flow

```
┌─────────┐     POST /api/auth/login      ┌─────────┐
│  Client  │ ────────────────────────────►  │   API   │
│          │  { userName, password }        │         │
│          │                                │  1. Verify PBKDF2 hash
│          │                                │  2. Reject suspended accounts (403)
│          │                                │  3. Issue JWT (15 min) + opaque refresh token
│          │                                │  4. Record session IP + parsed device info
│          │  ◄──────────────────────────── │
│          │  { accessToken,                │
│          │    accessTokenExpiresAt,       │
│          │    mustChangePassword }        │
│          │  Set-Cookie: alas_refresh      │
│          │   (HttpOnly, Secure,           │
│          │    SameSite=Lax,               │
│          │    Path=/api/auth, 7 days)     │
└─────────┘                                └─────────┘

┌─────────┐     POST /api/auth/refresh     ┌─────────┐
│  Client  │ ────────────────────────────►  │   API   │
│          │  (cookie sent automatically)   │  1. Look up SHA-256 hash of token
│          │                                │  2. Rotate: old token revoked,
│          │                                │     linked to its replacement
│          │                                │  3. Issue new JWT + new cookie
│          │  ◄──────────────────────────── │
└─────────┘                                └─────────┘
```

**Key design decisions:**
- **Access tokens live in memory on the client** — never in `localStorage`.
- **Refresh tokens are opaque 64-byte random strings**, stored only as SHA-256 hashes (`RefreshTokens` table), delivered via an HttpOnly cookie scoped to `/api/auth`.
- **Rotation with linkage** — every refresh revokes the presented token and records its replacement (`ReplacedById`).
- **JTI blacklist in the database** — `logout` revokes the current access token's `jti` (`RevokedTokens`); every authenticated request re-checks revocation in `OnTokenValidated`. Shared across instances, survives restarts.
- **Absolute expiry** — refresh chains die after `RefreshTokenAbsoluteDays` regardless of activity.
- **Session forensics** — each refresh token stores the client IP and parsed device/browser info at login and rotation (`SessionClientInfo`), surfaced in `GET /api/account/me/sessions`.
- **SignalR tokens** — the hub accepts `?access_token=` **only** on `/hubs/*` paths (`HubAccessToken`), never on regular API routes.

### Password Policy

- Minimum **12** characters, maximum 128 (change-password, user creation, and Excel import all enforce the same floor)
- Hashed with ASP.NET Core Identity's `PasswordHasher` (salted, iterated PBKDF2; timing-safe verify)
- New users start with `MustChangePassword = true` — the **must-change gate** returns `403 ProblemDetails` for every `/api/*` route except `login`, `refresh`, `logout`, `change-password`, and `/health/*` until the password is changed
- Changing a password **revokes all of the user's refresh tokens** (every session must re-authenticate)

### Forgot Password

`POST /api/auth/forgot-password` (rate-limited, no auth):
1. Looks up the user; requires a stored email address
2. Enforces a **15-minute cooldown** per user (`PasswordResetSentAt`)
3. Generates a 16-character temporary password (upper/lower/digit/symbol, ambiguity-free alphabet, CSPRNG)
4. Sends it via the configured `IEmailSender` — **default: the bank's Enterprise Email API (Microsoft Graph)**
5. Only after a successful send: re-hashes the password, sets `MustChangePassword`, revokes all refresh tokens

A failed email cannot lock the account — persistence happens after delivery succeeds.

### CSRF Protection

Authenticated state-changing requests must echo the per-token `XsrfToken` claim (embedded in the JWT at issuance, cryptographically random) in the **`X-XSRF-TOKEN`** header. Safe methods (`GET`/`HEAD`/`OPTIONS`/`TRACE`) are exempt. Mismatch → `403 ProblemDetails`. This is the double-submit pattern adapted for bearer-token clients.

### Authorization Model

Permission strings are embedded in the JWT from the `RoleCatalog` and enforced by named policies (`PermissionAuthorizationHandler`):

| Policy | Permission | Used by |
|---|---|---|
| `CanReviseLoan` | `loans.revise` | `PUT /api/loans/{id}/revise` |
| `CanManageWorkflow` | `workflow.manage` | `/api/workflow/configuration` |
| `CanViewWebLoans` | `webloans.view` | all `/api/webloans/*` |
| `CanManageBranches` | `branches.manage` | branch create/update/status |
| `CanCreateLoan`, `CanViewLoan`, `CanRecommendLoan`, `CanEvaluateLoan`, `CanApproveLoan`, `CanRejectLoan`, `CanViewUsers`, `CanCreateUsers`, `CanEditUsers`, `CanSuspendUsers`, `CanViewRoles`, `CanViewAuditLogs`, `CanViewLoanProduct`, `CanManageLoanProduct` | various | Registered and available; several workflow/user endpoints instead enforce the role directly (see below) |

Admin-only surfaces (`/api/users/*`, loan-product sync/import/update, document-requirement updates) use `RequireAuthorization(Roles = "Admin")`. Workflow actions (`recommend`, `evaluate`, `approve`, `reject`, `pushback`) enforce the acting role inside `LoanWorkflowService` (`EnsureRole`), on top of branch scoping.

### The Roles

| Role | Typical Title | What It Does |
|---|---|---|
| **Encoder** | Account Officer | Creates and revises applications; views WebLoan data |
| **Recommender** | Branch Head | Recommends or pushes back; views WebLoan data |
| **Evaluator** | Credit Checker | Evaluates, flags documents, manages checklist; views WebLoan data |
| **Approver** | Area Head | Approves, rejects, pushes back; views WebLoan data |
| **Admin** | IT / Operations | Users, branches, products, workflow config, cross-branch visibility |
| **System** | (internal) | Non-interactive role with no interactive permissions |

Branch visibility: Admin/System see all branches; users whose job title is *Area Head* see their area's branches; everyone else sees their home branch only (`BranchVisibility`).

### Defense-in-Depth Layers

1. **TLS in front** — the refresh cookie is always marked `Secure`; terminate TLS at the proxy and enable `ForwardedHeaders` (already wired) so rate limits and logs see real client IPs
2. **Rate limiting** — `auth` (10/min) and `write` (30/min) per-IP fixed windows
3. **JWT Bearer** — HS256, validated issuer/audience/lifetime, 30s skew
4. **DB-backed revocation** — revoked JTIs rejected on every request; `logout` blacklists the current access token, while password change and forgot-password revoke every refresh session
5. **Must-change-password gate** — locks provisional accounts out of the API surface
6. **CSRF double-submit** — `X-XSRF-TOKEN` vs. per-token claim
7. **Policy + role authorization** — named permission policies, role checks in workflow services, Admin-gated management endpoints
8. **Branch isolation** — loans, queues, remarks, revision resolution, and WebLoan LAI accounts are all scoped to the caller's branch/area
9. **Workflow state machine** — even authorized users cannot make invalid status transitions
10. **Optimistic concurrency** — `RowVersion` on loans and queue items prevents lost updates / double claims
11. **Read-only legacy access** — `ApplicationIntent=ReadOnly` + `NoTracking` on `WebLoanDbContext`; database names regex-validated; document SQL fully parameterized
12. **ProblemDetails everywhere** — no stack traces or internals leak; SQL failures surface as `503`, cancellations as `499`
13. **E-signature sanitization** — stored ink must be a PNG/JPEG/WEBP base64 data URL under ~2 MB (`ESignaturePolicy`); SVG/HTML rejected
14. **CSPRNG** — refresh tokens, JTIs, XSRF tokens, and temporary passwords all use `RandomNumberGenerator`
15. **Startup validation** — every options section is validated on boot (`ValidateOnStart`)

---

## The Loan Workflow

A loan application flows through a strict state machine (`LoanStatus`). Each transition is validated by the domain entity (`TransitionTo`) and gated by the caller's role and branch.

### Loan States

| Status | Meaning |
|---|---|
| `Draft` | Created but not submitted |
| `ForRecommendation` | Waiting for the Branch Head |
| `ForChecking` | Waiting for the Credit Checker |
| `ForApproval` | Waiting for the Approver |
| `ForRevision` | Pushed back to the encoder |
| `ForIncompleteDocuments` | Flagged for missing documents (evaluator) |
| `Approved` | Approved, awaiting disbursement processing |
| `Rejected` | Declined |
| `Cancelled` | Cancelled by owner/approver/admin |
| `ForDisbursement` | Released for disbursement |
| `Disbursed` | Money released (also set by the disbursement sync) |
| `OnGoing` | Active loan being serviced |

```
                 (submit)
                     │
        ┌────────────▼─────────────┐
        │  ForRecommendation *     │  ← skipped when Workflow.RequireRecommendation = false
        └──────┬──────────┬────────┘
      recommend│          │pushback
               ▼          ▼
        ┌────────────┐  ┌─────────────┐   revise (encoder, notes ≥ 10 chars)
        │ ForChecking│  │ ForRevision │ ──────────► re-enters at ForRecommendation
        └──┬─────┬───┘  └─────────────┘             (or ForChecking when skipped)
  evaluate │     │ pushback        ▲
           │     └────────────────►│
           ▼                       │
        ┌────────────┐   pushback  │
        │ ForApproval│ ────────────┘
        └─┬─────┬──┬─┘
  approve │     │  │ reject
          ▼     │  ▼
     ┌──────────┐│ ┌──────────┐
     │ Approved ││ │ Rejected │
     └────┬─────┘│ └──────────┘
          ▼      │
   ┌────────────────┐
   │ForDisbursement │
   └──────┬─────────┘
    sync  ▼
     ┌──────────┐
     │ Disbursed│
     └────┬─────┘
      sync│
          ▼
     ┌──────────┐
     │ OnGoing  │
     └──────────┘

  ForChecking ↔ ForIncompleteDocuments — document-parking transitions defined
  by the state machine (document flags currently record missing items on the
  loan without changing its status).

  Cancel is allowed from any pre-terminal state (not from Approved, Rejected,
  Cancelled, ForDisbursement, Disbursed, OnGoing) by the owning encoder,
  an Approver, or Admin/System.
```

### Creating an Application

`POST /api/loans` accepts **one or more loans for a single borrower** in one call:

- Application numbers are generated in Philippines time: group `APP-yyyyMMdd-######`, each loan `LAM-yyyyMMdd-######` (`ApplicationNumberGenerator`)
- Each line is computed server-side (`LoanComputationService`): amortization by mode (Diminishing / MIC / ADO Lump), total interest, deductions, net proceeds, NTHP (gross monthly income less living expenses), disposable income, max-loanable, and affordability checks (amortization ≤ 40% of NTHP)
- The initial status follows the runtime workflow configuration: `ForRecommendation` (default) or `ForChecking`
- The loan is enqueued in the matching stage queue (branch partition) and a **signature chain** is created
- Recommenders (or evaluators, when recommendation is skipped) get an arrival notification + SignalR push

### Workflow Actions

| Action | Endpoint | Who | Effect |
|---|---|---|---|
| Recommend | `POST /api/loans/{id}/recommend` | Recommender | → `ForChecking`, signs slot, enqueues evaluation, notifies evaluators. *(An Evaluator hitting this on a `ForChecking` loan fast-paths into evaluate.)* |
| Evaluate | `POST /api/loans/{id}/evaluate` | Evaluator | Body `{ "recommend": true/false }` → `ForApproval`, signs slot, enqueues approval |
| Approve | `POST /api/loans/{id}/approve` | Approver | → `Approved`, signs slot, notifies encoder |
| Reject | `POST /api/loans/{id}/reject` | Approver | → `Rejected` |
| Push back | `POST /api/loans/{id}/pushback` | Recommender / Evaluator / Approver | Body `{ section, comment }` → `ForRevision`, creates a `RevisionRequest`, reassigns to the encoder, notifies |
| Cancel | `POST /api/loans/{id}/cancel` | Owner / Approver / Admin | → `Cancelled`, closes open queue items |
| Revise | `PUT /api/loans/{id}/revise` | Encoder (`CanReviseLoan`) | From `ForRevision` only; recomputes figures, requires notes ≥ 10 chars, re-enters the pipeline at the configured initial stage |

Every action records an immutable `LoanApplicationAction` (actor, from/to status, verb, comments, timestamp) — the source for the loan **history** and **timeline** endpoints — and broadcasts a `DashboardUpdated` SignalR event.

Revision requests can be listed (`GET /api/loans/{id}/revision-requests`) and resolved (`PUT …/resolve`) with branch isolation enforced on both.

### Document Flagging & Checklists

- The desk reviewer for the loan's current stage (Recommender at `ForRecommendation`, Evaluator at `ForChecking`, Approver at `ForApproval`) can flag it for missing requirements: `POST /api/loans/{id}/document-flag` with the missing requirement codes and a reason (5–2000 chars). The flag records what's missing, who flagged it, and when — the loan's status is unchanged. The encoder is notified ("Documents Flagged — Upload Required") and the dashboard surfaces it. `DELETE` clears the flag.
- The state machine also defines `ForChecking ↔ ForIncompleteDocuments` parking transitions for loans held on documents.
- `GET /api/loans/{id}/checklist-documents` merges the WebLoan checklist (`doc_ref` + catalog descriptions) with submission state; `GET /api/loans/checklist-documents/{docId}/view` streams the binary from `bpb_binary` on the ALAS host (inline disposition allowed for PDF/PNG/JPEG/GIF only).
- Checklist documents and deviation items support threaded **remarks** (`document-remarks`, `deviations/{deviationId}/remarks`), branch-isolated, with informal-comment flagging.

### Workflow Queues

Active review stages are backed by `WorkflowQueueItems`:

| Stage | Partition | Purpose |
|---|---|---|
| `Recommendation` | branch code | Branch desk queue for Branch Heads |
| `Evaluation` | branch code | Branch desk queue for Credit Checkers |
| `Approval` | branch code | Branch desk queue for Area Heads |

- **Sequenced FIFO** within a partition; claiming takes the head (or a specific item)
- **Leasing** — a claim marks the item `Active` with a 30-minute lease; `RowVersion` makes concurrent claims fail fast (`409`)
- **Reaping** — expired leases are reaped on read and by a background service every 5 minutes; claims on cancelled loans are released automatically
- **Desk endpoints** — `GET /api/loans/queue/my` returns the caller's role desk (label, waiting list, current claim); `POST /api/loans/queue/claim` claims the head; `POST /api/loans/queue/release` and `POST /api/loans/queue/{loanId}/claim` manage specific claims. Admins/System can target any branch via `/api/workflow/queue*`.
- A `LoanAssigned` SignalR event notifies the claiming user.

### Signature Chain & E-Signatures

Each loan gets an ordered chain of signature slots following the configured workflow:

| Order | Slot | Role | Job title on the form |
|---|---|---|---|
| 1 | Encode | Encoder | Account Officer |
| 2 | Recommend *(only when recommendation is required)* | Recommender | Branch Head |
| 3/2 | Evaluate | Evaluator | Credit Checker |
| 4/3 | Approve | Approver | Area Head |

When a user signs (submit/recommend/evaluate/approve), their stored **e-signature ink is snapshotted** onto the slot (immutable after signing; attachable later if the profile signature was captured after the action). Ink must satisfy `ESignaturePolicy`: base64 data URL of PNG/JPEG/WEBP, ≤ ~2 MB — set via `PUT /api/account/me`. `GET /api/loans/{id}/signature-chain` returns display-ready slots (names, titles, timestamps, ink).

### Approval Routing

`ApprovalRoutingService` computes the required approval tier from:

- **Total exposure** (principal + interest): ≥ ₱1,000,000 → tier 3; ≥ ₱250,000 → tier 2; else tier 1
- **Max deviation severity**: ≥ 3 → tier 3; ≥ 2 → tier 2

and matches an `ApprovalAuthority` by tier, new/reloan allowance, severity ceiling, exposure ceiling, and scope (**Branch / Area / Global**), escalating to a higher tier when needed. `GET /api/loans/{id}/routing` exposes exposure, severity, document completeness (missing documents), and the assigned approver for a loan.

### SLA Policy

Default stage SLAs (`GET /api/loans/sla-policy`): **Recommendation 24 h · Evaluation 48 h · Approval 72 h**.

### Deviations

A fixed catalog of 21 deviation items (`GET /api/deviation-catalog`) with severities 1–2 (e.g., *"Age not within the prescribed parameters"*, *"With past due account — non performing loan"*, *"Total consumer loan exposure exceeding 1.5 million"*). Loans carry `LoanDeviations` (code, description, severity) with remark threads; the max severity feeds approval routing.

---

## Real-Time Features

A single SignalR hub at **`/hubs/notifications`** (JWT-authorized; `?access_token=` honored only under `/hubs`) carries everything.

### Hub Events

| Event | Payload | When |
|---|---|---|
| `ReceiveNotification` | `{ id, title, description, link, timestamp }` | A persisted notification is created for the user (group `user:{id}`) |
| `DashboardUpdated` | `{ timestamp }` | Any workflow action changes dashboard numbers (broadcast to all) |
| `LoanAssigned` | `{ loanId, userId, escalated }` | A queue item is claimed by / assigned to the user |
| `PresenceSnapshot` | online user list | Sent to the caller on hub connect (best-effort, 5s budget) |
| `PresenceChanged` | user + online/offline | Broadcast to the `presence` group when someone crosses the online threshold |

### Presence

- Clients call `POST /api/presence/heartbeat`; a user counts as **online for 5 minutes** after their last heartbeat (`UserPresences.LastSeenAt`, indexed)
- `GET /api/presence/online` returns the current directory
- A background sweep runs every minute (batch 200) to mark stale users offline and emit `PresenceChanged`
- Hub connect/disconnect also updates presence

### Notifications

Notifications are persisted (`Notifications` table, types `Application` / `Action` / `Message` / `System`) and pushed over the hub. `GET /api/notifications` returns the caller's most recent 50; `POST /api/notifications/{id}/read` and `POST /api/notifications/read-all` mark them read (ownership enforced). Role-targeted arrival notifications (e.g., "new application ready for evaluation") fan out to every active user of that role in the branch — plus Area Heads whose area covers it (`ApplicationArrivalNotifier`).

---

## API Endpoints

Errors use **RFC 9457 `ProblemDetails`** (`{ type, title, status, detail }`). Paged lists use `PageResult<T>`: `{ items, totalCount, page, pageSize, totalPages }`. Enums serialize as strings.

### Health

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/health/live` | Public | Liveness (static OK) |
| `GET` | `/health/ready` | Public | Readiness — SQL Server connectivity (`DbReadyHealthCheck`, 5s timeout) |

### Authentication `/api/auth`

| Method | Path | Rate-Limited | Description |
|---|---|---|---|
| `POST` | `/api/auth/login` | `auth` | Login → access token + refresh cookie |
| `POST` | `/api/auth/refresh` | `auth` | Silent rotation (cookie or body token) |
| `POST` | `/api/auth/logout` | `write` | Revoke refresh token, blacklist access JTI, clear cookie |
| `POST` | `/api/auth/change-password` | `auth` | Change password (12–128 chars), revokes all sessions |
| `POST` | `/api/auth/forgot-password` | `auth` | Email a temporary password (15-min cooldown) |

### Account `/api/account`

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/account/me` | Current profile |
| `PUT` | `/api/account/me` | Update email, phone, emergency contact, e-signature *(write RL)* |
| `GET` | `/api/account/me/activity` | Activity feed — `limit` (1–50), `from`/`to` (dates), `action` filter: `login`, `security`, `draft`, `approval`, `application` |
| `GET` | `/api/account/me/loans` | Recently processed loans |
| `GET` | `/api/account/me/clients` | Recently handled clients |
| `GET` | `/api/account/me/sessions` | Active sessions with IP/device info (current session marked) |
| `DELETE` | `/api/account/me/sessions/{id}` | Revoke a session (not the current one) *(write RL)* |
| `DELETE` | `/api/account/me/sessions/others` | Revoke every other session *(write RL)* |

### Users `/api/users` *(Admin role)*

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/users` | Paged list — `search`, `role`, `branchId` filters |
| `GET` | `/api/users/{id}` | User detail |
| `POST` | `/api/users` | Create user (password ≥ 12 chars, `MustChangePassword`) *(write RL)* |
| `PUT` | `/api/users/{id}` | Update user *(write RL)* |
| `POST` | `/api/users/{id}/suspend` | Suspend *(write RL)* |
| `POST` | `/api/users/{id}/activate` | Reactivate *(write RL)* |
| `GET` | `/api/users/import-template` | Download the .xlsx import template |
| `POST` | `/api/users/import` | Bulk import (.xlsx ≤ 10 MB; missing passwords are generated and returned once) *(write RL)* |

### Roles `/api/roles`

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/roles` | Role catalog |
| `GET` | `/api/roles/matrix` | Role × permission matrix |

### Branches `/api/branches`

| Method | Path | Policy | Description |
|---|---|---|---|
| `GET` | `/api/branches` | Authenticated | Branch list (`status` filter) |
| `GET` | `/api/branches/{code}` | Authenticated | Get by code |
| `POST` | `/api/branches` | `CanManageBranches` | Create branch *(write RL)* |
| `PUT` | `/api/branches/{code}` | `CanManageBranches` | Update branch *(write RL)* |
| `PATCH` | `/api/branches/{code}/status` | `CanManageBranches` | Activate/deactivate *(write RL)* |

### Loans `/api/loans`

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/loans` | Paged list — `status` (multi), `branchId`/`branchCode`, `search`, `dateFrom`/`dateTo`, `myTurn` (assigned to me). Branch/area-scoped for non-admins. |
| `POST` | `/api/loans` | Create application group (1..n loans) *(write RL)* |
| `GET` | `/api/loans/{id}` | Full loan detail |
| `GET` | `/api/loans/lam/{lamId}` | Lookup by LAM number |
| `POST` | `/api/loans/compute` | Amortization / NTHP / max-loanable computation |
| `GET` | `/api/loans/queue-default` | Default status set for queue views |
| `GET` | `/api/loans/sla-policy` | SLA hours per stage |

### Loan Workflow `/api/loans/{id}`

| Method | Path | Who |
|---|---|---|
| `POST` | `/recommend` | Recommender (Evaluator fast-path when already `ForChecking`) |
| `POST` | `/evaluate` | Evaluator |
| `POST` | `/approve` · `/reject` | Approver |
| `POST` | `/pushback` | Recommender / Evaluator / Approver |
| `POST` | `/cancel` | Owner / Approver / Admin |
| `PUT` | `/revise` | Encoder — `CanReviseLoan` *(write RL)* |
| `GET` | `/signature-chain` | Any caller with loan access |

### Loan Review `/api/loans/{id}`

| Method | Path | Description |
|---|---|---|
| `GET` | `/history` | Full action trail (actor, from → to, verb, comments) |
| `GET` | `/timeline` | Paged, newest-first timeline (workflow actions, signatures, deviations, remarks) |
| `GET` | `/deviations` | Deviation flags with remarks |
| `POST` | `/deviations/{deviationId}/remarks` | Add deviation remark *(write RL)* |
| `GET` | `/routing` | Exposure, severity, completeness, assigned approver |
| `GET` | `/revision-requests` | Open/resolved pushback requests |
| `PUT` | `/revision-requests/{revisionId}/resolve` | Resolve a request *(write RL)* |

### Loan Documents `/api/loans`

| Method | Path | Description |
|---|---|---|
| `GET` | `/{id}/checklist-documents` | Checklist with WebLoan document references |
| `GET` | `/checklist-documents/{docId}/view` | Stream binary from `bpb_binary` (`?disposition=inline` for PDF/images) |
| `GET` | `/{id}/document-remarks` | Remarks per checklist code |
| `POST` | `/{id}/document-remarks` | Add remark *(write RL)* |
| `POST` | `/{id}/document-flag` | Flag missing documents (reason required) |
| `DELETE` | `/{id}/document-flag` | Clear the flag |

### Loan Groups & Queues

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/loan-groups/{groupNo}` | All loans in an application group |
| `GET` | `/api/loans/queue/my` | Caller's role desk (branch partition) |
| `POST` | `/api/loans/queue/claim` | Claim the head of my desk *(write RL)* |
| `POST` | `/api/loans/queue/release` | Release my current claim *(write RL)* |
| `POST` | `/api/loans/queue/{loanId}/claim` | Claim a specific loan *(write RL)* |
| `GET` | `/api/workflow/queue` | Queue view (`stage`, optional `branchId` for Admin/System) |
| `POST` | `/api/workflow/queue/claim` | Claim *(write RL)* |
| `POST` | `/api/workflow/queue/{id}/release` | Release *(write RL)* |
| `GET` | `/api/workflow/signature-chain` | Slot template for the current configuration |
| `GET`/`PUT` | `/api/workflow/configuration` | `CanManageWorkflow` — runtime `RequireRecommendation` toggle |

### Dashboard · Notifications · Presence · Deviations

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/dashboard/overview` | Role-scoped overview: pending counts, approved today, pushbacks, flagged documents, pending queue, now serving, approved loans, weekly trend (Manila business days; encoder lists scoped to own loans) |
| `GET` | `/api/notifications` | Latest 50 notifications |
| `POST` | `/api/notifications/{id}/read` | Mark read *(write RL)* |
| `POST` | `/api/notifications/read-all` | Mark all read *(write RL)* |
| `POST` | `/api/presence/heartbeat` | I'm alive |
| `GET` | `/api/presence/online` | Online directory |
| `GET` | `/api/deviation-catalog` | The 21 deviation items |

### Loan Products `/api/loan-products`

| Method | Path | Who | Description |
|---|---|---|---|
| `GET` | `/api/loan-products` | Authenticated | Paged catalog |
| `GET` | `/api/loan-products/{code}` | Authenticated | Product detail |
| `PUT` | `/api/loan-products/{code}` | Admin | Update policy fields (rates, fees, term/amount bounds, amortization mode) *(write RL)* |
| `POST` | `/api/loan-products/sync` | Admin | Sync from WebLoan (≤ 500 products; applies retired flags from WebLoan expirations, preserves local policy fields such as interest rate) *(write RL)* |
| `GET` | `/api/loan-products/export` | Authenticated | Export .xlsx (`includeRetired` filter) |
| `GET` | `/api/loan-products/import/template` | Admin | Import template .xlsx |
| `POST` | `/api/loan-products/import` | Admin | Bulk import .xlsx ≤ 10 MB *(write RL)* |
| `GET` | `/api/loan-products/{code}/document-requirements` | Authenticated | Required document codes per product |
| `PUT` | `/api/loan-products/{code}/document-requirements` | Admin | Replace requirements *(write RL)* |

### WebLoans `/api/webloans` *(read-only legacy integration, `CanViewWebLoans`)*

| Method | Path | Description |
|---|---|---|
| `GET` | `/cis?q=&max=` | Borrower search (max 50) |
| `GET` | `/cis/{cisNo}/search` | Borrower profile + accounts (**branch-isolated** — encoders never see other branches' LAI accounts) |
| `GET` | `/loan-products?max=` | Active WebLoan product catalog |
| `GET` | `/cis/{cisNo}/accounts/{accountId}/outstanding-loans` | Paged outstanding loans for an account |
| `GET` | `/cis/{cisNo}/accounts/{accountId}/pending-loan` | Pending loan for an account |
| `GET` | `/outstanding/{cifNo}?max=` | Outstanding loans by CIF |
| `GET` | `/loan-class?bch=&loanNo=&loanProduct=` | Loan classification |

### WebSocket & Docs

| Path | Description |
|---|---|
| `/hubs/notifications` | SignalR hub (JWT; `?access_token=` allowed under `/hubs` only) |
| `/scalar` | Scalar API reference — **Development only** |
| `/openapi/v1.json` | OpenAPI document — **Development only** |

---

## Databases

### Primary — `ALASv2_DB` (owned, read/write)

Schema managed by EF Core Migrations (`src/Ebi.Alas.Api/Migrations`, 23 migrations from `InitialAlasSchema` through `RefreshTokenClientInfo`). Apply with `dotnet ef database update --project src/Ebi.Alas.Api`.

| Table | Purpose |
|---|---|
| `Users` | Internal users — credentials (PBKDF2 hash), role, branch, job title, contact fields, e-signature, `MustChangePassword`, `PasswordResetSentAt` |
| `RefreshTokens` | Hashed refresh tokens — rotation chain (`ReplacedById`), absolute expiry, client IP + device info |
| `RevokedTokens` | JTI blacklist for access-token revocation |
| `Branches` | Branch registry — code, name, area code, status |
| `LoanApplications` | Loan entities — LAM/group numbers, borrower, product, figures, status, document-flag fields, `AssignedToUserId`, form snapshot JSON, `RowVersion` |
| `LoanApplicationActions` | Immutable per-loan action trail (actor, from/to, verb, comments) |
| `WorkflowQueueItems` | Stage queues — partition, sequence, state, lease, `RowVersion` |
| `SignatureChainEntries` | Ordered signature slots with signer + ink snapshot |
| `DocumentChecklistItems` | Per-loan document checklist state |
| `DocumentRemarks` | Remarks on checklist documents (threaded) |
| `LoanDeviations` · `DeviationRemarks` | Deviation flags and remark threads |
| `RevisionRequests` | Pushback requests (section, comment, resolution) |
| `LoanProducts` · `LoanProductDocumentRequirements` | Product catalog (synced from WebLoan) + required documents per product |
| `Notifications` | Persisted user notifications |
| `UserPresences` | Presence heartbeats (`LastSeenAt`, indexed) |
| `SystemSettings` | Runtime settings (e.g., `Workflow.RequireRecommendation`) with updated-by audit fields |

In Development without `ConnectionStrings:Alas`, EF InMemory (`alas-dev`) is used instead — fine for UI work, not for migration testing.

### Secondary — `webloan` (legacy catalog, read-only)

Mapped through `WebLoanDbContext` on the same SQL Server host (`ApplicationIntent=ReadOnly`, `NoTracking`). Tables mapped: `cis_info`, `cis_info_misc_data`, `loan_acct_info`, `loan_product`, `check_list_data`, `check_list_all`, `mis_group`, `loan_purpose`.

### Tertiary — `bpb_binary` (checklist binaries, read-only)

`docs` / `doc_ref` tables holding uploaded checklist document binaries. Read with **parameterized SQL on the ALAS connection** (three-part names on the same host — no linked server, no `OPENQUERY`).

> Historical note: an intermediate design queried WebLoan through the ALAS connection with a configurable name prefix (`WebLoan:NamePrefix`, incl. linked-server four-part names). That approach was superseded by the current `WebLoanDbContext` restore; the design docs remain under `docs/compose/` for reference.

---

## Background Services

| Service | Schedule | Purpose |
|---|---|---|
| `AdminSeedHostedService` | Startup | Seeds the admin user from `Seed:*` options (only if absent) and product document requirements from the catalog |
| `PresenceSweepHostedService` | Every 1 min | Marks stale presence offline (batch 200), emits `PresenceChanged` |
| `QueueReconciliationHostedService` | Every 5 min | Reaps expired queue leases (batch 100) |
| `TokenCleanupHostedService` | Every 60 min | Purges expired refresh tokens and stale revoked JTIs (batches of 500) |
| `DisbursementSyncHostedService` | Every 30 min | For loans in `ForDisbursement`/`Disbursed` (batch 50), checks WebLoan outstanding balances and advances `ForDisbursement → Disbursed → OnGoing` when the account shows active |

---

## Email Delivery

`IEmailSender` abstracts four providers (selected by `Api:Email:Smtp:Provider`):

| Provider | Transport | Notes |
|---|---|---|
| `EnterpriseEmailApi` *(default)* | HTTP → `EnterpriseBank_EmailAPI` (Microsoft Graph) | `BaseUrl` + `X-Api-Key`, optional `SenderConfigId` |
| `Resend` | HTTP → Resend API | `ApiKey` (`re_…`) |
| `Smtp` | MailKit | Host/port/credentials, optional SSL |
| `DevOutbox` | Filesystem | For local development — writes `.eml` files to `email-outbox/` for inspection; hard-blocked in Production |

Currently used by **forgot-password**. Send failures raise `EmailSendException` and never mutate user state (see the flow above).

---

## Seed Data

There is **no built-in default password in the repository**. On first startup:

- **Admin user** — created from `Seed:AdminUserName` / `Seed:AdminPassword` (≥ 12 chars) / name / email / `Seed:AdminBranchId`, role `Admin`, only if the username doesn't already exist. Existing credentials are never overwritten.
- **Product document requirements** — seeded from `ProductDocumentRequirementCatalog` for products **A16, A17, C02, C21, C23, C35** (codes like `A1004`, `A2008`, `CCR38`, `PIC02`…) when the table is empty.
- **Branches** are *not* seeded — create them via `POST /api/branches` (or import through your deployment tooling).

> Keep seed credentials in user-secrets / a secret store only. Rotate the seed admin password immediately after first login in any shared environment.

---

## Development Guide

### Common Commands

```powershell
# Build (warnings are errors)
dotnet build

# Run
dotnet run --project src/Ebi.Alas.Api

# Hot-reload
dotnet watch run --project src/Ebi.Alas.Api

# EF Core migrations
dotnet ef migrations add MyChange --project src/Ebi.Alas.Api
dotnet ef database update --project src/Ebi.Alas.Api

# Tests
dotnet test

# Formatting check (CI-style)
dotnet format --verify-no-changes
```

### Trying the API with Scalar

1. Run in Development and open `https://localhost:7220/scalar`
2. Call `POST /api/auth/login` with your seeded admin credentials
3. Copy `accessToken` from the response and use it as a Bearer token for subsequent calls
4. For state-changing calls also send the `X-XSRF-TOKEN` header with the `XsrfToken` claim value from the JWT (decode it at jwt.io, or read it from the token in your client)
5. If the response says `mustChangePassword: true`, call `POST /api/auth/change-password` first — everything else returns 403 until you do

### Conventions

**Adding a feature slice:**
1. Create `Features/<Name>/` with contracts, handler(s), domain, and an endpoint mapper (`MapXxx` extension)
2. Register handlers in `Composition/ServiceRegistration.cs` (one place, explicit)
3. Map the endpoints in `Composition/PipelineConfiguration.cs`
4. Add EF configuration under the feature's `Persistence/` folder and a migration
5. Add permission/policy only when a rule actually needs it (`Composition/Auth/`)

**Adding a workflow transition:**
1. Extend `IsValidTransition` in `Features/LoanApplications/Domain/LoanApplication.cs`
2. Add the action in `LoanWorkflowService` (role check, action record, queue move, signature, notifications)
3. Map the endpoint in `LoanWorkflowEndpoints.cs`
4. Cover it with tests in `tests/Ebi.Alas.Api.Tests/Features/LoanApplications/`

**House rules:** see `.github/copilot-instructions.md` — smallest complete solution, no speculative abstractions, verify builds/tests before claiming done.

### Environment Variables

ASP.NET Core picks these up automatically (double-underscore separator):

```powershell
$env:ConnectionStrings__Alas = "Server=...;Database=ALASv2_DB;..."
$env:Api__Jwt__SigningKey = "your-strong-signing-key"
$env:Api__Jwt__AccessTokenMinutes = "15"
$env:Workflow__RequireRecommendation = "true"
$env:Api__Email__Smtp__Provider = "DevOutbox"
dotnet run --project src/Ebi.Alas.Api
```

---

## Testing

The test project `tests/Ebi.Alas.Api.Tests` (xUnit) holds **~430 test methods across ~120 files**:

- **API integration tests** (`Api/`) — `WebApplicationFactory`-hosted end-to-end tests for auth, CSRF, rate limits, health, presence, branch and user-import flows. `TestHostSecrets` isolates tests from developer user-secrets and runs against the EF InMemory database with WebLoan disabled.
- **Feature unit tests** (`Features/`) — handlers, domain rules, workflow notifications, queue concurrency/isolation, dashboard scoping and date filters, signature-chain display, approval routing, WebLoan access rules, and more.
- **Composition/persistence tests** — options validation, problem-handler mapping, caller context, pagination, DbContext relational mappings, WebLoan read-only behavior.

```powershell
dotnet test                                    # everything
dotnet test --filter "FullyQualifiedName~LoginHandlerTests"
dotnet test --logger "console;verbosity=detailed"
```

Test collections run sequentially within the assembly (`xunit.runner.json`) because WebApplicationFactory hosts share the in-memory database.

---

## Deployment Notes

### Production Checklist

- [ ] Set `ConnectionStrings:Alas` (required outside Development — startup throws without it)
- [ ] Set a strong `Api:Jwt:SigningKey` (≥ 32 random chars) plus production `Issuer`/`Audience`
- [ ] Configure `Seed:*` admin credentials via secret store (password ≥ 12 chars); rotate after first login
- [ ] Configure the email provider (`Api:Email:Smtp:*`) so forgot-password works
- [ ] Apply migrations: `dotnet ef database update --project src/Ebi.Alas.Api`
- [ ] Set `ASPNETCORE_ENVIRONMENT=Production` (Scalar/OpenAPI endpoints are Development-only)
- [ ] Terminate TLS at the proxy and forward `X-Forwarded-For` / `X-Forwarded-Proto` (the API honors them for rate-limit partitions and session IPs)
- [ ] Point probes at `/health/live` (liveness) and `/health/ready` (readiness — DB)
- [ ] Create branches (`POST /api/branches`) and sync loan products (`POST /api/loan-products/sync`)
- [ ] Optionally wire OpenTelemetry exporters (instrumentation for ASP.NET Core + HttpClient is already registered; no exporter is configured by default)

### Scaling Notes

The API is stateless except for SQL Server, which makes horizontal scale straightforward, with two caveats:

- **SignalR** uses the default in-memory backplane — with multiple replicas, either enable sticky sessions at the proxy or add a backplane; REST endpoints and security state (tokens, revocation, queues) are fully shared through the database.
- **Background services** run per instance. They are batch-bounded and idempotent-by-lease/RowVersion (queue reaping, token cleanup), but avoid running several instances if you want a single disbursement-sync writer.

---

## Repository Extras

| Path | What |
|---|---|
| `.github/copilot-instructions.md` | Mandatory rules for AI-assisted changes in this repo |
| `docs/compose/` | Per-feature design specs, plans, and delivery reports (historical record) |
| `Directory.Build.props` | Solution-wide build settings (net10.0, C# 14, nullable, warnings-as-errors) |

---

## License

Internal project — Enterprise Bank Inc. All rights reserved.

---

**Maintained by:** EBI Software Development  
**Repository:** [`jcmcjms/EBI_ALAS_V2_backend`](https://github.com/jcmcjms/EBI_ALAS_V2_backend)  
**Solution:** `Ebi.Alas.slnx` (`src/Ebi.Alas.Api` + `tests/Ebi.Alas.Api.Tests`)
