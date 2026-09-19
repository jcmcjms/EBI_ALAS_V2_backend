# EBI.ALAS.V2 Backend

> **ALAS** — Automated Loan Application System, version 2

A banking-grade .NET 8 Web API that powers the full loan origination lifecycle for Enterprise Bank Inc. From the moment an encoder drafts a borrower's application to the final disbursement, ALAS enforces a rigorous four-eyes workflow, captures every decision in an immutable audit trail, and keeps everyone in the loop with real-time notifications.

This isn't just another CRUD API. It's the operational backbone of a multi-branch lending operation — designed to handle the messy realities of loan processing: incomplete documents, approver unavailability, revision cycles, deviation approvals, and the occasional "the internet went down at the branch" scenario.

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
- [Database](#database)
- [Caching Strategy](#caching-strategy)
- [WebLoan Integration](#webloan-integration)
- [Seed Data](#seed-data)
- [Development Guide](#development-guide)
- [Testing](#testing)
- [Deployment Notes](#deployment-notes)

---

## What Does It Do?

At its core, ALAS manages the journey of a loan application through a multi-stage approval pipeline.

**For Loan Officers (Encoders):**
- Draft loan applications with borrower details pulled from the legacy WebLoan system
- Track document completeness in real-time
- Cancel applications when borrowers change their minds
- Handle revision requests from reviewers with full context

**For Branch Heads (Recommenders):**
- Review queued applications from their branch
- Recommend or send back for revision with comments
- See at a glance what's waiting and what's overdue

**For Credit Checkers (Evaluators):**
- Evaluate creditworthiness with access to deviation flags
- Request additional documents when needed
- Forward to the appropriate approval tier based on exposure and risk

**For Area Heads (Approvers):**
- Approve, reject, or request revisions on loans routed to their tier
- Automatic assignment based on approval authority matrix (tier, exposure limits, deviation severity)
- Track workload with real-time presence indicators

**For Administrators:**
- Full visibility across all branches and stages
- Manage users, roles, and loan products
- Monitor system health through dashboards
- Review comprehensive audit logs
- Configure workflow behavior (e.g., skip recommendation step) without redeployment

---

## Architecture at a Glance

The system uses a **vertical-slice architecture** — each feature owns its entire stack from endpoint to database query. No sprawling service layers, no anemic models. If you're working on loans, everything you need is in `Features/Loans/`.

```
                         ┌──────────────────────────────┐
                         │        Frontend (SPA)        │
                         │     React / Next.js / etc.   │
                         └──────────┬───────────────────┘
                                    │  HTTPS + JWT
                                    │  WebSocket (SignalR)
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                        ASP.NET Core 8 Pipeline                             │
│                                                                            │
│  CORS → Response Compression → Correlation ID → Request Logging →          │
│  Security Headers → Global Exception Handler → Rate Limiter →              │
│  Authentication (JWT) → CSRF Validation → Authorization (Policies) →       │
│  Idempotency → Output Cache → Minimal API Endpoints                        │
│                                                                            │
│  SignalR Hub (/hubs/notifications) — real-time WebSocket layer             │
└────────┬──────────────────────────────────┬────────────────────────────────┘
         │                                  │
         ▼                                  ▼
┌─────────────────────────┐      ┌──────────────────────────┐
│   AppDbContext          │      │   WebLoanDbContext        │
│   (ALASv2_DB)           │      │   (webloan — read-only)   │
│   Read / Write          │      │   DbContextFactory pattern │
└────────────┬────────────┘      └────────────┬─────────────┘
             │                                │
             ▼                                ▼
┌─────────────────────────┐      ┌──────────────────────────┐
│  Audit Interceptor      │      │  Read-Only Interceptor    │
│  (CreatedAt/ModifiedAt) │      │  (blocks non-SELECT)      │
└─────────────────────────┘      └──────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────────────────────────┐
│                    Message Bus (MassTransit)                 │
│                                                             │
│  ┌─────────────────┐    ┌──────────────────────────────┐   │
│  │ AuditLogConsumer │    │ NotificationConsumer          │   │
│  │ (async audit)    │    │ (push notifications + SignalR)│   │
│  └─────────────────┘    └──────────────────────────────┘   │
│                                                             │
│  Backed by RabbitMQ (production) or InMemory (dev)         │
└─────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────┐
│                    Distributed Cache (Redis)                 │
│                                                             │
│  • JTI token revocation blacklist                           │
│  • Idempotency replay cache                                 │
│  • Dashboard / branch summary cache                         │
│  • SignalR backplane (cross-pod WebSocket fan-out)          │
│                                                             │
│  Falls back to IMemoryCache if Redis is not configured      │
└─────────────────────────────────────────────────────────────┘
```

### Design Philosophy

- **Minimal APIs** over controllers — concise endpoint definitions using `MapGroup` and C# 12 `Results`. Less ceremony, more signal.
- **Vertical slices** — each feature folder contains its own DTOs, validators, services, repositories, and endpoints. Working on loans? You never need to leave `Features/Loans/`.
- **Interface-first everything** — repositories, services, and time providers are all behind interfaces for testability and DI flexibility.
- **EF Core interceptors** for cross-cutting concerns — audit timestamps happen automatically, and the WebLoan database is protected by an interceptor that kills any non-SELECT command before it reaches SQL Server.
- **`ITimeProvider` abstraction** — all time flows through `PhilippinesTimeProvider`, so business logic uses `Asia/Manila` time while storage remains UTC. Tests can inject deterministic time.
- **FluentValidation** — every request DTO is validated before it touches business logic. No exceptions.
- **CQRS via MediatR** — endpoints dispatch commands/queries through MediatR, enabling pipeline behaviors for validation, logging, and authorization without cluttering handlers.
- **Async message processing** — audit logging and notification delivery are offloaded to MassTransit consumers via RabbitMQ. HTTP responses return immediately; the heavy lifting happens in the background.

---

## Features

The `Features/` directory is organized by business domain. Each folder is self-contained:

| Feature | Path | What It Does |
|---|---|---|
| **Auth** | `Features/Auth/` | Login, refresh tokens, logout, password changes, JWT issuance with revocation |
| **Account** | `Features/Account/` | "My Account" — profile management, active sessions, activity feed, processed loans, recent clients |
| **Users** | `Features/Users/` | Admin user management — create, view, edit, suspend internal users |
| **Roles** | `Features/RoleManagement/` | Role listing and the role × permission matrix |
| **Branches** | `Features/Branches/` | Branch registry — 31+ branches across the Philippines |
| **Loans** | `Features/Loans/` | The heart of the system — loan applications, workflow engine, document checklists, deviation tracking, loan products, workflow queues, SLA policies |
| **Approval Matrix** | `Features/ApprovalMatrix/` | Automatic approver routing based on tier, exposure limits, deviation severity, and branch area coverage |
| **Audit Logs** | `Features/AuditLogs/` | Searchable, paginated audit trail of every state change in the system |
| **Notifications** | `Features/Notifications/` | In-app notification system with real-time delivery via SignalR |
| **Presence** | `Features/Presence/` | Who's online right now — org-wide directory and per-record viewer tracking |
| **Dashboard** | `Features/Dashboard/` | Branch- and role-scoped summary metrics — KPIs, pending queues, trends |
| **WebLoans** | `Features/WebLoans/` | Read-only integration with the legacy WebLoan core banking system |
| **System Settings** | `Features/SystemSettings/` | Runtime-configurable system settings |

### Cross-Cutting Concerns (`Common/`)

| Module | Path | Purpose |
|---|---|---|
| Authorization | `Common/Authorization/` | Permission-based policy handler |
| Constants | `Common/Constants/` | Roles (5), Permissions (16), role-permission matrix, role-queue mappings |
| Exceptions | `Common/Exceptions/` | Domain exceptions (NotFound, Forbidden, InvalidWorkflow) |
| Extensions | `Common/Extensions/` | Claims helpers, FluentValidation extensions, service registration |
| Middleware | `Common/Middleware/` | 6 middleware components — correlation ID, request logging, security headers, IP allowlist, idempotency, global exception handling |
| Models | `Common/Models/` | `ApiResponse<T>` envelope, `PagedResult<T>` |
| Time | `Common/Time/` | `ITimeProvider`, `PhilippinesTimeProvider`, UTC JSON converter |

### Infrastructure (`Infrastructure/`)

| Module | Path | Purpose |
|---|---|---|
| Data | `Infrastructure/Data/` | `AppDbContext`, `WebLoanDbContext`, `DbInitializer` (seeds branches, users, loan products, approval matrix, deviation catalog) |
| Interceptors | `Infrastructure/Interceptors/` | `AuditSaveChangesInterceptor` (auto-timestamps), `WebLoanReadOnlyInterceptor` (blocks writes) |
| Messaging | `Infrastructure/Messaging/` | MassTransit event publisher, `AuditLogConsumer`, `NotificationConsumer`, integration events |
| Security | `Infrastructure/Security/` | `BankingSecurityValidator`, `CachingTokenRevocationRepository` |
| SignalR | `Infrastructure/SignalR/` | JWT user ID provider for SignalR routing |

---

## Project Structure

```
EBI_ALAS_V2_backend/
├── EBI.ALAS.Api/
│   ├── Program.cs                                    # Composition root + full pipeline setup
│   ├── appsettings.json                              # Config (placeholder secrets)
│   ├── appsettings.Development.json                  # Local dev overrides (gitignored secrets)
│   ├── EBI.ALAS.Api.csproj                           # Project file (net8.0)
│   │
│   ├── Common/
│   │   ├── Authorization/                            # PermissionRequirement + handler
│   │   ├── Constants/                                # Roles, Permissions, RolePermissions, RoleQueues
│   │   ├── Exceptions/                               # NotFoundException, ForbiddenAccess, InvalidWorkflow
│   │   ├── Extensions/                               # Claims helpers, validation, DI registration
│   │   ├── Middleware/                                # 6 middleware components
│   │   ├── Models/                                   # ApiResponse, PagedResult
│   │   └── Time/                                     # ITimeProvider, PhilippinesTimeProvider, UTC converter
│   │
│   ├── Features/
│   │   ├── Auth/                                     # Login, refresh, logout, change-password, JWT
│   │   ├── Account/                                  # My Account endpoints
│   │   ├── Users/                                    # Admin user CRUD
│   │   ├── RoleManagement/                           # Role listing + permission matrix
│   │   ├── Branches/                                 # Branch registry
│   │   ├── Loans/
│   │   │   ├── Endpoints/                            # Individual endpoint handlers (Get, Create, Update, Cancel, etc.)
│   │   │   ├── Computation/                          # Loan computation service
│   │   │   ├── LoanWorkflowService.cs                # 10-state workflow engine
│   │   │   ├── WorkflowQueueService.cs               # Queue partitioning + head promotion
│   │   │   ├── LoanProductSyncHostedService.cs       # Background sync from WebLoan
│   │   │   ├── QueueReconciliationHostedService.cs   # Queue consistency repair
│   │   │   ├── DocumentCompletenessSyncHostedService.cs
│   │   │   ├── ChecklistDocumentEndpoints.cs         # Document checklist CRUD
│   │   │   ├── LoanDeviationEndpoints.cs             # Deviation flag management
│   │   │   ├── DocumentRemarkEndpoints.cs            # Document remark/annotation
│   │   │   ├── LoanProductEndpoints.cs               # Product catalog management
│   │   │   └── ... (50 files total)
│   │   ├── ApprovalMatrix/                           # Approver routing, deviation catalog, document completeness
│   │   ├── AuditLogs/                                # Searchable audit trail
│   │   ├── Notifications/                            # In-app notifications + SignalR hub
│   │   ├── Presence/                                 # Online directory + entity viewer tracking
│   │   ├── Dashboard/                                # Aggregated metrics
│   │   ├── WebLoans/                                 # Legacy WebLoan read-only integration
│   │   └── SystemSettings/                           # Runtime config
│   │
│   ├── Infrastructure/
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs                       # Main ALAS context
│   │   │   ├── WebLoanDbContext.cs                   # Read-only legacy context
│   │   │   └── DbInitializer.cs                      # Schema migration + seed data
│   │   ├── Interceptors/
│   │   │   ├── AuditSaveChangesInterceptor.cs       # Auto-stamps CreatedAt/ModifiedAt
│   │   │   └── WebLoanReadOnlyInterceptor.cs        # Blocks non-SELECT SQL
│   │   ├── Messaging/
│   │   │   ├── Consumers/                            # AuditLogConsumer, NotificationConsumer
│   │   │   ├── Events/                               # Integration event definitions
│   │   │   └── EventPublisher.cs                     # IEventPublisher + MassTransit implementation
│   │   ├── Security/
│   │   │   ├── BankingSecurityValidator.cs           # Security hardening
│   │   │   └── CachingTokenRevocationRepository.cs   # Redis-backed JTI blacklist
│   │   └── SignalR/
│   │       └── JwtUserIdProvider.cs                  # Maps JWT claims to SignalR user routing
│   │
│   ├── Migrations/                                   # EF Core migrations
│   ├── Store/                                        # (reserved)
│   └── Properties/
│       └── launchSettings.json                       # Local dev URLs
│
├── EBI.ALAS.Tests/
│   ├── EBI.ALAS.Tests.csproj                         # xUnit test project
│   ├── ApprovalFormConventionsTests.cs               # Approval form validation tests
│   ├── TempPasswordGeneratorTests.cs                 # Password generation tests
│   └── UserRepositoryTests.cs                        # User repository integration tests
│
├── Database/
│   └── Migrations/                                   # Database-level migration scripts
│
├── scripts/
│   ├── backfill_workflow_queue.sql                   # One-time queue backfill for existing loans
│   ├── install-garnet-service.ps1                    # Redis alternative (Garnet) setup
│   └── install-rabbitmq.ps1                          # RabbitMQ installation script
│
├── EBI.ALAS.V2.slnx                                  # Solution file
├── .gitignore
├── swagger.txt                                       # Pointer to local Swagger UI
└── README.md                                         # You are here
```

---

## Tech Stack

| Layer | Technology | Version | Why |
|---|---|---|---|
| Runtime | .NET | 8.0 | LTS, performance, C# 12 features |
| Web Framework | ASP.NET Core Minimal APIs | 8.0 | Low ceremony, high throughput |
| ORM | Entity Framework Core | 8.0 | Code-first, migrations, interceptors |
| Database | Microsoft SQL Server | — | Enterprise-grade, ACID compliance |
| CQRS | MediatR | 12.2 | Command/query separation, pipeline behaviors |
| Message Bus | MassTransit + RabbitMQ | 8.1 | Async audit logging, notification delivery |
| Real-Time | SignalR | 8.0 | WebSocket hub for notifications + presence |
| Distributed Cache | Redis (StackExchange) | 8.0 | Cross-pod cache coherence, SignalR backplane |
| Auth | JWT Bearer | 8.0 | Stateless auth with refresh token rotation |
| Password Hashing | BCrypt.Net-Next | 4.0.3 | Industry-standard, timing-attack resistant |
| Validation | FluentValidation | 11.3 | Declarative, testable validation rules |
| Object Mapping | Mapster | 7.4 | Fast, convention-based mapping |
| Excel | EPPlus | 8.7 | Loan product import/export |
| Resilience | Polly | 8.2 | Retry policies for external calls |
| Logging | Serilog + Seq | 8.0 / 7.0 | Structured logging with correlation IDs |
| Tracing | OpenTelemetry | 1.18 | Distributed tracing (ASP.NET + HTTP) |
| Health Checks | AspNetCore.HealthChecks.* | 8.0 | SQL Server, Redis, RabbitMQ health probes |
| API Docs | Swashbuckle | 6.5 | Swagger UI in Development |
| Testing | xUnit | 2.9 | Unit and integration tests |

### C# Language Features

- **Nullable reference types** — enabled project-wide
- **Implicit usings** — enabled
- **`record` types** — immutable DTOs and query parameters
- **`init`-only setters** — on request DTOs
- **Primary constructors** — on DI-injected services (C# 12)
- **Collection expressions** — `[item1, item2]` syntax (C# 12)
- **`JsonStringEnumConverter`** + custom `UtcDateTimeConverter` — stable JSON serialization
- **Pattern matching** — used extensively in workflow and routing logic

---

## Getting Started

### Prerequisites

- **.NET 8 SDK** — [download here](https://dotnet.microsoft.com/download/dotnet/8.0)
- **SQL Server** — any edition (LocalDB, Express, Developer, or full)
- **Redis** — optional but recommended for production (falls back to in-memory cache)
- **RabbitMQ** — optional but recommended for production (falls back to in-memory transport)

### 1. Clone & Restore

```powershell
git clone <your-repo-url> alas_v2_backend
cd alas_v2_backend
dotnet restore
```

### 2. Configure Secrets

Edit `EBI.ALAS.Api/appsettings.json` or (better) use .NET User Secrets:

```powershell
cd EBI.ALAS.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR_HOST;Database=ALASv2_DB;User Id=YOUR_USER;Password=YOUR_PASS;TrustServerCertificate=True;Encrypt=True"
dotnet user-secrets set "ConnectionStrings:WebLoanConnection" "Server=YOUR_HOST;Database=webloan;User Id=YOUR_USER;Password=YOUR_PASS;TrustServerCertificate=True;Encrypt=True"
dotnet user-secrets set "Jwt:SecretKey" "your-random-32+-character-secret-key-here"
```

**Optional but recommended for production:**

```powershell
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379,abortConnect=false"
dotnet user-secrets set "ConnectionStrings:RabbitMQ" "amqp://guest:guest@localhost:5672"
```

> **Never commit real credentials.** The `appsettings.json` ships with placeholder values. Use `appsettings.Development.json` (gitignored) or `dotnet user-secrets` for local development.

### 3. Run

```powershell
dotnet run --project EBI.ALAS.Api
```

The API starts on:
- **HTTPS:** `https://localhost:7220`
- **HTTP:** `http://localhost:5173`

**On first start**, `DbInitializer` will:
1. Run all pending EF Core migrations
2. Seed 31 branches across the Philippines
3. Seed the default admin user
4. Seed loan products and checklist requirements
5. Seed the approval authority matrix and deviation catalog

Swagger UI is available at **`https://localhost:7220/swagger/index.html`** in Development mode.

---

## Configuration

All settings live in `EBI.ALAS.Api/appsettings.json`. Override per-environment using `appsettings.{Environment}.json` or environment variables.

### Connection Strings

| Key | Purpose | Pool Size |
|---|---|---|
| `DefaultConnection` | Main ALAS database (read/write) | 50–500 connections |
| `WebLoanConnection` | Legacy WebLoan database (read-only) | 10–100 connections |
| `Redis` | Distributed cache + SignalR backplane | — |
| `RabbitMQ` | Message bus for async processing | — |

### JWT Settings

| Key | Default | Description |
|---|---|---|
| `SecretKey` | *(placeholder)* | HMAC-SHA256 signing key. **Required.** Min 32 characters. |
| `Issuer` | `EBI.ALAS.V2` | Token issuer claim |
| `Audience` | `EBI.ALAS.V2.Frontend` | Token audience claim |
| `ExpiryMinutes` | `15` | Access token lifetime (short by design) |
| `RefreshTokenExpiryDays` | `7` | Sliding refresh token lifetime |
| `AbsoluteSessionExpiryDays` | `14` | Hard cap — session dies regardless of rotation |

Clock skew is `TimeSpan.Zero` — tokens expire exactly when `exp` says they do.

### Rate Limiting

| Endpoint | Limit | Window | Notes |
|---|---|---|---|
| `POST /api/auth/login` | 5 attempts | 60 seconds | Brute-force protection |
| All other endpoints | 120 requests | 60 seconds | Per-user (authenticated) or per-IP (anonymous) |

Exceeding limits returns `429 Too Many Requests` with a `Retry-After` header.

### Workflow Configuration

| Key | Default | Description |
|---|---|---|
| `Workflow.RequireRecommendation` | `false` | Skip the recommendation step (encoder → evaluator directly) |
| `WorkflowSlaHours.ForRecommendation` | `4` | SLA hours for recommendation stage |
| `WorkflowSlaHours.ForChecking` | `8` | SLA hours for evaluation stage |
| `WorkflowSlaHours.ForApproval` | `8` | SLA hours for approval stage |
| `WorkflowSlaHours.ForRevision` | `24` | SLA hours for revision stage |
| `WorkflowSlaHours.ForDisbursement` | `24` | SLA hours for disbursement stage |

All workflow settings are hot-reloadable via `IOptionsMonitor` — flip them without redeploying.

### Background Services

| Service | Interval | Purpose |
|---|---|---|
| `LoanProductSyncHostedService` | 6 hours | Syncs loan product catalog from WebLoan |
| `CleanupExpiredTokensHostedService` | 1 hour | Purges expired refresh tokens and JTI blacklist entries |
| `QueueReconciliationHostedService` | — | Repairs queue consistency for in-flight loans |
| `DocumentCompletenessSyncHostedService` | — | Syncs document completeness status |

---

## How Security Works

Security isn't a feature — it's a layer that touches everything. Here's how ALAS protects itself:

### Authentication Flow

```
┌─────────┐     POST /api/auth/login      ┌─────────┐
│  Client  │ ────────────────────────────►  │  API    │
│          │  { username, password }        │         │
│          │                                │         │
│          │  ◄──────────────────────────── │         │
│          │  { accessToken (15 min) }      │         │
│          │  Set-Cookie: refresh_token     │         │
│          │    (HttpOnly, Secure,          │         │
│          │     SameSite=Strict, 7 days)   │         │
└─────────┘                                └─────────┘

┌─────────┐     POST /api/auth/refresh     ┌─────────┐
│  Client  │ ────────────────────────────►  │  API    │
│          │  (cookie sent automatically)   │         │
│          │                                │  1. Validate refresh token hash
│          │                                │  2. Revoke old refresh token
│          │                                │  3. Blacklist old access token JTI
│          │                                │  4. Issue new pair
│          │  ◄──────────────────────────── │         │
│          │  { new accessToken }           │         │
│          │  Set-Cookie: new refresh_token │         │
└─────────┘                                └─────────┘
```

**Key design decisions:**
- **Access tokens live in memory** (e.g., Zustand store) — never in `localStorage`. This limits XSS impact.
- **Refresh tokens are HttpOnly cookies** — invisible to JavaScript, immune to XSS.
- **Refresh rotation** — every refresh invalidates the old token and issues a new one. Token reuse detection can be added.
- **JTI blacklist** — revoked access tokens are checked on every request via `OnTokenValidated`. Stored in Redis (or in-memory for single-pod).
- **Timing-attack mitigation** — login always runs BCrypt verify, even for non-existent users (using a dummy hash).

### Password Policy

- Minimum 8 characters
- Must contain uppercase, lowercase, digit, and one of `!?*.`
- Must differ from current password
- Changing password **revokes all sessions** globally

### Authorization Model

16 granular permissions mapped to 5 roles:

| Permission | Key |
|---|---|
| Loan workflow | `loans.create`, `loans.view`, `loans.recommend`, `loans.evaluate`, `loans.approve`, `loans.reject` |
| Loan products | `loan_product.manage`, `loan_product.view` |
| User management | `user.create`, `user.view`, `user.edit`, `user.suspend` |
| Roles | `role.manage`, `role.view` |
| Audit logs | `auditLogs.view` |
| Workflow config | `workflow.manage` |

### The Five Roles

| Role | Display Name | Typical Title | Workflow Stage |
|---|---|---|---|
| **Encoder** | Encoder (AO/CAA) | Account Officer / Credit Analyst Assistant | Creates and revises applications |
| **Recommender** | Recommender (Branch Head) | Branch Head | Reviews and recommends |
| **Evaluator** | Evaluator (Credit Checker) | Credit Analyst | Evaluates creditworthiness |
| **Approver** | Approver (Area Head) | Area Head / Branch Head | Approves, rejects, or requests revision |
| **Admin** | Administrator | IT / Operations | Full access, manages users and system |

### Defense-in-Depth Layers

1. **HTTPS** enforced in non-Development
2. **CORS** whitelist with credentials
3. **Response compression** (Brotli + Gzip) — 6× bandwidth reduction
4. **Security headers middleware** — CSP, X-Frame-Options, etc.
5. **Rate limiting** — login-specific + global per-user/IP
6. **IP allowlisting** — admin endpoints restricted by IP
7. **JWT Bearer** authentication
8. **CSRF validation** middleware
9. **Policy-based authorization** with permission requirements
10. **Workflow validation** — even authorized users can't make invalid transitions
11. **FluentValidation** on every input
12. **Global exception handler** — never leaks stack traces in production
13. **Audit interceptor** — every `SaveChanges` timestamps mutations
14. **Read-only interceptor** — blocks writes to WebLoan database
15. **Idempotency middleware** — prevents duplicate side effects on retries
16. **Request body size limits** — 10MB max, configurable per endpoint
17. **Kestrel hardening** — request header timeouts, connection limits

---

## The Loan Workflow

A loan application flows through a strict state machine. Each transition is gated by both the **user's role** and the **validity of the from/to status pair**. Admins bypass role checks but the transition itself must still be valid in the state machine.

```
                    ┌──────────┐
                    │  Draft   │
                    └────┬─────┘
                         │  Encoder
                         ▼
          ┌──────────────────────────┐
          │   ForRecommendation *    │  ← Skippable via Workflow.RequireRecommendation
          └────────────┬─────────────┘
                       │  Recommender
                       ▼
          ┌──────────────────────────┐
          │      ForChecking         │
          └────────────┬─────────────┘
                       │  Evaluator
                       ▼
          ┌──────────────────────────┐
          │      ForApproval         │
          └───┬────────┬─────────┬───┘
   Approver  │        │  Approver│  Approver
    ┌────────┘        │          └──────────┐
    ▼                 ▼                     ▼
┌─────────┐    ┌──────────┐         ┌────────────┐
│ Approved │    │ Rejected │         │ ForRevision │
└────┬─────┘    └──────────┘         └──────┬─────┘
     │ Admin                                │ Encoder
     ▼                                      ▼
┌────────────────┐                ┌─────────────────────┐
│ForDisbursement │                │  (back to entry)     │
└───────┬────────┘                └─────────────────────┘
    Admin│
        ▼
  ┌──────────┐
  │ Disbursed│
  └────┬─────┘
   Admin│
        ▼
  ┌──────────┐
  │ OnGoing  │
  └──────────┘

  * Encoder can cancel from: Draft, ForRecommendation, ForChecking,
    ForApproval, ForRevision (ownership enforced in endpoint)
```

### Additional Loan States

- **Cancelled** — Encoder can cancel their own loans at any in-flight stage
- **Revision cycle** — ForRevision → Encoder revises → re-enters at ForRecommendation (or ForChecking if recommendation is skipped)

### Workflow Queue System

Loans in active review stages are automatically placed into partitioned queues:

| Stage | Partition Key | Purpose |
|---|---|---|
| Recommendation | `REC:{branchCode}` | Branch-scoped recommendation queue |
| Evaluation | `EVA:{branchCode}` | Branch-scoped evaluation queue |
| Approval | `APP:{branchCode}:{tier}` | Tier-specific approval queue |

The queue system handles:
- **Head promotion** — the oldest loan in each partition becomes the "head" (first to be served)
- **Assignment/release** — approvers can "lease" a loan for review, preventing duplicate work
- **Reconciliation** — background service repairs queue consistency for edge cases

### Approval Authority Matrix

Loans are automatically routed to the appropriate approval tier based on:

- **Loan type** (New vs. Renewal)
- **Total exposure** (proposed amount + outstanding balance)
- **Deviation severity** (None, Minor, Major)
- **Approval authority tier** (1–5, with priority-based fallback within each tier)
- **Branch area coverage** (branch-level, area-level, or global authority)

### Document Completeness

Each loan product has a checklist of required documents. The system tracks:
- Which documents have been submitted
- Which are missing or incomplete
- Whether the loan is "document-complete" (a prerequisite for certain workflow transitions)

### Deviation Tracking

Loans can have deviation flags (e.g., exceeded exposure limits, missing collateral). Deviations affect:
- Which approval tier is required
- Who can approve the loan
- Whether additional documentation is needed

---

## Real-Time Features

ALAS uses SignalR WebSocket connections for instant updates. No polling required.

### Notifications

When a loan status changes, relevant users get instant notifications:

- **Loan submitted** → Recommender gets notified
- **Loan recommended** → Evaluator gets notified
- **Loan approved** → Encoder + Admin get notified
- **Loan requires revision** → Encoder gets notified
- **Document remark added** → Loan owner gets notified

Notifications are persisted in the database and delivered via SignalR in real-time. The frontend can also poll `GET /api/notifications` as a fallback.

### Presence System

Who's online right now?

- **Online directory** — see which colleagues are active, scoped to your branch (admins see everyone)
- **Entity viewer tracking** — see who else is looking at a specific loan, document, or deviation in real-time
- **Connection limits** — max 10 connections per user to prevent resource exhaustion

### SignalR Hub Events

| Event | Payload | When |
|---|---|---|
| `ReceiveNotification` | Notification object | New notification for the user |
| `PresenceSnapshot` | Full online directory | On connect |
| `PresenceChanged` | User + online/offline | User connects or disconnects |
| `EntityViewersChanged` | Entity type/id + viewer list | Someone opens/closes an entity |

### SignalR Groups

| Group | Scope | Events |
|---|---|---|
| `Branch_{branchCode}` | Branch-scoped | Loan updates, branch events |
| `All_Users` | System-wide | Presence changes, workflow broadcasts |
| `Approvers` | Approver-specific | Assignment notifications |
| `watch:{entityType}:{entityId}` | Per-record | Entity viewer changes |

---

## API Endpoints

All endpoints return a consistent `ApiResponse<T>` envelope:

```json
{
  "success": true,
  "message": "Operation completed",
  "data": { ... },
  "errors": null
}
```

### Health

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/health` | Public | Detailed health check (SQL Server, Redis, RabbitMQ) |

### Authentication `/api/auth`

| Method | Path | Rate-Limited | Description |
|---|---|---|---|
| `POST` | `/api/auth/login` | Yes (5/60s) | Login → access token + refresh cookie |
| `POST` | `/api/auth/refresh` | No | Silent token rotation (uses cookie) |
| `POST` | `/api/auth/logout` | No | Revoke tokens, clear cookie |
| `POST` | `/api/auth/change-password` | No | Change password, revoke all sessions |

### Account `/api/account`

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/account/me` | Current user profile |
| `PUT` | `/api/account/me` | Update profile |
| `GET` | `/api/account/me/sessions` | Active sessions (paged) |
| `DELETE` | `/api/account/me/sessions/{id}` | Revoke a session |
| `GET` | `/api/account/me/activity` | Recent activity feed |
| `GET` | `/api/account/me/loans` | Recently processed loans |
| `GET` | `/api/account/me/clients` | Recently handled clients |

### Users `/api/users`

| Method | Path | Policy | Description |
|---|---|---|---|
| `GET` | `/api/users` | `CanViewUsers` | List users (paged, filterable) |
| `GET` | `/api/users/{id}` | `CanViewUsers` | Get user details |
| `POST` | `/api/users` | `CanCreateUsers` | Create user |
| `PUT` | `/api/users/{id}` | `CanEditUsers` | Update user |
| `PATCH` | `/api/users/{id}/status` | `CanSuspendUsers` | Suspend/activate user |

### Roles `/api/roles`

| Method | Path | Policy | Description |
|---|---|---|---|
| `GET` | `/api/roles` | `CanViewRoles` | List all roles |
| `GET` | `/api/roles/matrix` | `CanViewRoles` | Role × permission matrix |

### Branches `/api/branches`

| Method | Path | Policy | Description |
|---|---|---|---|
| `GET` | `/api/branches` | `CanViewUsers` | Paged branch list |
| `GET` | `/api/branches/all` | `CanViewUsers` | All branches (no paging) |
| `GET` | `/api/branches/{id}` | `CanViewUsers` | Get by numeric ID |
| `GET` | `/api/branches/code/{code}` | `CanViewUsers` | Get by branch code |

### Loans `/api/loans`

| Method | Path | Policy | Description |
|---|---|---|---|
| `GET` | `/api/loans` | `CanViewLoan` | Paged, filterable, sortable loan list. Scoped by branch/role. |
| `GET` | `/api/loans/{id}` | `CanViewLoan` | Full loan detail with actions, WebLoan traceability, routing info |
| `POST` | `/api/loans` | `CanCreateLoan` | Create draft loan application |
| `PUT` | `/api/loans/{id}/status` | Workflow role | Transition loan status |
| `POST` | `/api/loans/{id}/cancel` | `CanCreateLoan` | Cancel a loan (encoder, own loans only) |
| `GET` | `/api/loans/{id}/history` | `CanViewLoan` | Full audit trail for a loan |
| `GET` | `/api/loans/{id}/routing` | `CanViewLoan` | Approval routing: tier, matched rule, completeness, assigned approver |
| `POST` | `/api/loans/{id}/assignment/release` | `CanViewLoan` | Release an active lease |
| `GET` | `/api/loans/sla-policy` | Authenticated | Current SLA hours per workflow stage |
| `GET` | `/api/loans/queue-default` | Authenticated | Default queue configuration |

### Loan Products `/api/loan-products`

| Method | Path | Policy | Description |
|---|---|---|---|
| `GET` | `/api/loan-products` | `CanViewLoanProduct` | List all products (including retired) |
| `GET` | `/api/loan-products/{code}` | `CanViewLoanProduct` | Get product by code |
| `POST` | `/api/loan-products` | `CanManageLoanProduct` | Create product |
| `PUT` | `/api/loan-products/{code}` | `CanManageLoanProduct` | Update product |
| `POST` | `/api/loan-products/import` | `CanManageLoanProduct` | Import from Excel |

### Checklist Documents `/api/loans/{id}/checklist`

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/loans/{id}/checklist` | Get document checklist for a loan |
| `POST` | `/api/loans/{id}/checklist` | Mark document as submitted |
| `DELETE` | `/api/loans/{id}/checklist/{docId}` | Remove document submission |

### Loan Deviations `/api/loans/{id}/deviations`

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/loans/{id}/deviations` | List deviation flags |
| `POST` | `/api/loans/{id}/deviations` | Add deviation flag |
| `DELETE` | `/api/loans/{id}/deviations/{deviationId}` | Remove deviation flag |

### Document Remarks `/api/loans/{id}/remarks`

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/loans/{id}/remarks` | List document remarks |
| `POST` | `/api/loans/{id}/remarks` | Add remark |

### Workflow Configuration `/api/workflow`

| Method | Path | Policy | Description |
|---|---|---|---|
| `GET` | `/api/workflow/config` | `CanManageWorkflow` | Get current workflow configuration |
| `PUT` | `/api/workflow/config` | `CanManageWorkflow` | Update workflow configuration |

### Notifications `/api/notifications`

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/notifications` | Most recent notifications for the calling user |

### Presence `/api/presence`

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/presence/online` | Full online directory (branch-scoped for non-admins) |
| `GET` | `/api/presence?userIds=1,2,3` | Batch liveness flags for specific users |

### Approval Matrix `/api/approval-matrix`

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/approval-matrix` | List approval authorities |
| `GET` | `/api/approval-matrix/approvers` | Available approvers with presence status |

### Audit Logs `/api/audit-logs`

| Method | Path | Policy | Description |
|---|---|---|---|
| `GET` | `/api/audit-logs` | `CanViewAuditLogs` | Paginated, filterable audit trail |
| `GET` | `/api/audit-logs/{id}` | `CanViewAuditLogs` | Single audit log entry |

### Dashboard `/api/dashboard`

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/dashboard/overview` | Cached (30s) aggregate: KPIs, pending queue, trends |

### WebLoans `/api/webloans` *(read-only, legacy integration)*

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/webloans/cis/{cisNo}/search` | Borrower + account list lookup |
| `GET` | `/api/webloans/cis/{cisNo}/accounts/{accountNo}` | PN records for an account |
| `GET` | `/api/webloans/cis/{cisNo}/accounts/{accountNo}/active-loans` | Up to 10 active loans |
| `GET` | `/api/webloans/cis/{cisNo}` | Full borrower profile |

### WebSocket

| Path | Protocol | Description |
|---|---|---|
| `/hubs/notifications` | WebSocket (SignalR) | Real-time notifications, presence, entity viewer tracking |

---

## Database

### Primary Database — `ALASv2_DB`

Owned by this application. Schema managed via EF Core Migrations.

**Core Tables:**
- `Users` — system users with role assignments
- `Branches` — 31+ branch registry
- `RefreshTokens` — hashed refresh tokens with absolute expiry
- `RevokedTokens` — JTI blacklist for access token revocation
- `LoanApplications` — main loan entities with workflow state
- `LoanActions` — immutable audit log of every status change
- `LoanProducts` — product catalog (synced from WebLoan)
- `LoanProductChecklist` — required documents per product
- `LoanDeviations` — deviation flags on loans
- `LoanChecklistDocuments` — submitted documents
- `DocumentRemarks` — annotations on loan documents
- `ApprovalAuthorities` — approval tier configuration
- `DeviationCatalog` — deviation severity definitions
- `WorkflowQueueItems` — partitioned workflow queues
- `WorkflowConfigurations` — runtime workflow settings
- `AuditLogs` — comprehensive audit trail
- `Notifications` — user notifications
- `SystemSettings` — runtime configuration
- `IdempotencyKeys` — idempotency tracking

### Secondary Database — `webloan` *(read-only)*

The legacy WebLoan core banking system. Accessed via `WebLoanDbContext` with `DbContextFactory` pattern for thread-safe parallel queries.

**Key Tables:**
- `cis_info` / `cis_info_misdata` — borrower master data
- `mis_group` — group/membership info
- `loan_acct_info` — loan account master
- `loan_data` — loan detail records (PN records)
- `loan_product` / `loan_status` — lookup tables
- `creation_types` — disbursement types
- `amort_data` — amortization schedules

The `WebLoanReadOnlyInterceptor` provides application-level enforcement — any non-`SELECT` command is blocked before it reaches SQL Server, regardless of database permissions.

### Audit & Time

- `AuditSaveChangesInterceptor` automatically populates `CreatedAt` / `ModifiedAt` for entities that expose these properties
- All times flow through `ITimeProvider` — the `PhilippinesTimeProvider` produces UTC values while exposing helpers for `Asia/Manila` business logic
- `AuditLog` entries capture: user, action, entity type/id, summary, raw changes, IP address, user agent, and timestamp

---

## Caching Strategy

ALAS uses a **two-tier caching architecture**:

### L1: In-Process Cache (IMemoryCache)

Hot-path data that benefits from zero-latency access:

| Consumer | Key Pattern | TTL | Purpose |
|---|---|---|---|
| Token Revocation | `revoked:{jti}` | ≤ access token lifetime | JTI blacklist check on every request |
| Dashboard | Branch-scoped | 30 seconds | Aggregated metrics |
| Branch List | `branches:all` | 5 minutes | Rarely changes |

### L2: Distributed Cache (Redis)

Cross-pod coherent data:

| Consumer | Key Pattern | TTL | Purpose |
|---|---|---|---|
| JTI Blacklist | `ALAS_revoked:{jti}` | ≤ access token lifetime | Multi-pod token revocation |
| Idempotency | `ALAS_idem:{key}` | 90 seconds | Replay protection across pods |
| Dashboard | `ALAS_dashboard:{branch}` | 30 seconds | Shared across pods |
| SignalR Backplane | `ALAS_SignalR:*` | — | WebSocket message fan-out |

**When Redis is not configured**, the system falls back to `DistributedMemoryCache` — functional for single-pod development but not suitable for production multi-replica deployments.

### Trade-offs

**Single-pod (IMemoryCache only):**
- JTI blacklist is local — a revoked token might be accepted on another pod for up to 15 minutes
- Idempotency replay only works within the same pod
- State lost on process restart (mitigated by short token lifetimes)

**Multi-pod (Redis):**
- Full cache coherence across all pods
- SignalR backplane enables WebSocket message fan-out
- Shared idempotency replay protection

---

## WebLoan Integration

The WebLoan feature provides a **drill-down flow** for the loan origination UI:

```
Step 1: Search CIS
  GET /api/webloans/cis/{cisNo}/search
  → Returns borrower info + list of accounts

Step 2: Pick an Account
  GET /api/webloans/cis/{cisNo}/accounts/{accountNo}
  → Returns PN records (loan_data rows)

Step 3: Pull Active Loans
  GET /api/webloans/cis/{cisNo}/accounts/{accountNo}/active-loans
  → Returns up to 10 active loans with computed amortization amounts

Step 4: Full Profile (backward compatible)
  GET /api/webloans/cis/{cisNo}
  → Returns everything in one response
```

When a loan is created referencing a WebLoan CIS/account, the `LoanApplication` stores the WebLoan `cis_no`, `bch_code`, `account_no`s, and `pn_no`s for full traceability. These are visible on `GET /api/loans/{id}` under the WebLoan traceability fields.

The `DbContextFactory` pattern is used for WebLoan queries — each parallel lookup gets its own `DbContext` instance, since `DbContext` is not thread-safe and the search endpoint fires 3–6 concurrent queries.

---

## Seed Data

On first run, `DbInitializer` seeds the following:

### Default Admin

| Username | Password | Branch | Role |
|---|---|---|---|
| `admin` | `admin123` | `011` (Head Office) | Admin |

### Additional Seed Data

- **31 branches** across the Philippines (including Corporate Center and Head Office)
- **Loan products** with checklist requirements
- **Approval authority matrix** — tiered approver configuration
- **Deviation severity catalog** — deviation classification
- **Branch area codes** — area-level grouping for approver routing

> **All default passwords must be changed immediately in any non-development environment.**

---

## Development Guide

### Common Commands

```powershell
# Build
dotnet build

# Run
dotnet run --project EBI.ALAS.Api

# Hot-reload (watches for file changes)
dotnet watch run --project EBI.ALAS.Api

# EF Core migrations
dotnet ef migrations add MyChange --project EBI.ALAS.Api
dotnet ef database update --project EBI.ALAS.Api

# Run tests
dotnet test EBI.ALAS.Tests

# Run on a custom port
dotnet run --project EBI.ALAS.Api --urls "https://localhost:8443"
```

### Testing Auth with Swagger

1. Open `https://localhost:7220/swagger`
2. Call `POST /api/auth/login` with `{ "username": "admin", "password": "admin123" }`
3. Copy `data.accessToken` from the response
4. Click **Authorize** at the top, paste the token (the `Bearer` prefix is added automatically)
5. All subsequent calls will include the token

### Adding a New Workflow Transition

1. Update `Features/Loans/LoanWorkflowService.cs` — add the transition to `BuildTransitions()`
2. Add any new permission to `Common/Constants/Permissions.cs`
3. Register the policy in `Program.cs` (`options.AddPolicy(...)`)
4. Map the permission to the role in `Common/Constants/RolePermissions.cs`
5. The endpoint automatically enforces the new transition

### Adding a New Feature

1. Create a folder under `Features/` (e.g., `Features/MyFeature/`)
2. Add your entity, DTOs, validators, service, repository, and endpoints
3. Register services in `Common/Extensions/ServiceCollectionExtensions.cs`
4. Map endpoints in `Program.cs` (`app.MapMyFeatureEndpoints()`)
5. Add any new permissions and policies

### Environment Variables

ASP.NET Core picks these up automatically (double-underscore separator):

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=...;Database=ALASv2_DB;..."
$env:Jwt__SecretKey = "your-strong-secret-here"
$env:Jwt__ExpiryMinutes = "15"
$env:Workflow__RequireRecommendation = "false"
dotnet run --project EBI.ALAS.Api
```

---

## Testing

The test project (`EBI.ALAS.Tests`) uses xUnit with EF Core InMemory provider for integration tests.

### Running Tests

```powershell
# Run all tests
dotnet test EBI.ALAS.Tests

# Run with verbose output
dotnet test EBI.ALAS.Tests --logger "console;verbosity=detailed"

# Run specific test class
dotnet test EBI.ALAS.Tests --filter "FullyQualifiedName~UserRepositoryTests"
```

### Test Coverage

- `ApprovalFormConventionsTests` — validates approval form conventions
- `TempPasswordGeneratorTests` — validates temporary password generation
- `UserRepositoryTests` — integration tests for user repository operations

---

## Deployment Notes

### Production Checklist

- [ ] Change all default passwords
- [ ] Set strong `Jwt:SecretKey` (32+ characters, random)
- [ ] Configure Redis for distributed caching
- [ ] Configure RabbitMQ for async message processing
- [ ] Set `ASPNETCORE_ENVIRONMENT=Production`
- [ ] Enable HTTPS and configure SSL certificates
- [ ] Set up SQL Server with proper backup strategy
- [ ] Configure CORS origins for production frontend domain
- [ ] Set up health check monitoring (`/health`)
- [ ] Configure structured logging (Seq, ELK, etc.)
- [ ] Review and adjust rate limiting thresholds
- [ ] Set up OpenTelemetry exporters (Jaeger, Zipkin, etc.)

### Multi-Pod Considerations

When deploying multiple replicas:

- **Redis is required** for JTI blacklist coherence, idempotency replay, and SignalR backplane
- **RabbitMQ is required** for async message processing across pods
- **SQL Server connection pooling** is configured with `Max Pool Size=500` and `Min Pool Size=50` — adjust based on pod count
- **SignalR Redis backplane** ensures WebSocket messages fan out to all pods

### Health Checks

The `/health` endpoint returns detailed status for:

- **SQL Server** — connection and query capability
- **Redis** — connectivity and responsiveness (if configured)
- **RabbitMQ** — connectivity (if configured)
- **Application** — self-check

---

## License

Internal project — Enterprise Bank Inc. All rights reserved.

---

**Maintained by:** EBI Software Development  
**Repository:** `EBI_ALAS_V2_backend`  
**Solution:** `EBI.ALAS.V2.slnx`
