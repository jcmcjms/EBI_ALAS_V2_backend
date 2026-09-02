# EBI.ALAS.V2 Backend

A banking-grade **.NET 8 Web API** for end-to-end loan application management, built on ASP.NET Core Minimal APIs with strict role-based authorization, JWT authentication, and a read-only integration into a legacy WebLoan core banking system.

> **Project:** ALAS V2 — Automated Loan Application System
> **Stack:** .NET 8 · ASP.NET Core Minimal APIs · EF Core (SQL Server) · JWT Bearer · FluentValidation · Swashbuckle

---

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Features](#features)
- [Project Structure](#project-structure)
- [Tech Stack](#tech-stack)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Security Model](#security-model)
- [Loan Workflow](#loan-workflow)
- [API Reference](#api-reference)
- [Database](#database)
- [WebLoan Integration](#webloan-integration)
- [Seed Data](#seed-data)
- [Development](#development)

---

## Overview

**ALAS V2** (Automated Loan Application System, version 2) is the internal backend that powers a banking institution's loan origination lifecycle. It enforces a four-eyes workflow across multiple user roles — from initial encoding of borrower information, through recommendation, evaluation, and final approval — with full audit logging of every action taken on every loan application.

The system also integrates with the institution's legacy **WebLoan** core banking database (read-only) to look up existing borrower accounts, outstanding loans, and reloan history when preparing new applications.

### Key Capabilities

- **Loan Origination** — Create, review, recommend, evaluate, approve, reject, revise, disburse, and monitor loan applications through a 10-state workflow.
- **Role-Based Access Control** — Five built-in roles with granular permission policies; admins can override.
- **User Management** — Create, view, edit, and suspend internal users.
- **Branch Registry** — 31 pre-seeded branches across the Philippines.
- **WebLoan Borrower Lookup** — Step-by-step CIS → Account → Active Loans drill-down backed by the WebLoan DB.
- **JWT Auth with Refresh Tokens** — Short-lived access tokens (15 min) + rotating refresh tokens (7 days) delivered via `HttpOnly` cookies.
- **Account Self-Service** — View profile, manage active sessions, change password, view activity, processed loans, and recent clients.
- **Dashboard Summary** — Aggregated metrics scoped to the user's branch and role.
- **Audit Trail** — Every loan action is timestamped and recorded with from/to status and comments.
- **Rate-Limited Login** — Built-in protection against brute-force attempts (5 attempts / 60s by default).

---

## Architecture

The solution uses a **vertical-slice / feature-folder** layout rather than horizontal layers — every concern for a single feature (DTOs, validators, service, repository, endpoints, entity) lives in one folder under `Features/`.

```
                    ┌──────────────────────────┐
                    │      Frontend (SPA)      │
                    │   (consumes this API)    │
                    └────────────┬─────────────┘
                                 │  HTTPS + JWT
                                 ▼
┌────────────────────────────────────────────────────────────┐
│                  ASP.NET Core 8 Pipeline                   │
│                                                            │
│  CORS → GlobalExceptionHandler → RateLimiter →            │
│  Authentication (JWT) → Authorization (Policies) →         │
│  Minimal API Endpoints                                     │
└────────┬─────────────────────────────────────┬─────────────┘
         │                                     │
         ▼                                     ▼
┌────────────────────┐                ┌──────────────────────┐
│  AppDbContext      │                │  WebLoanDbContext    │
│  (ALASv2_DB)       │                │  (webloan DB)        │
│  Read / Write      │                │  Read-only           │
└────────────────────┘                └──────────────────────┘
         │                                     │
         ▼                                     ▼
┌────────────────────┐                ┌──────────────────────┐
│  Audit Interceptor │                │  Read-Only           │
│  (writes Created/  │                │  Interceptor         │
│   Modified fields) │                │  (blocks writes)     │
└────────────────────┘                └──────────────────────┘
```

### Design Choices

- **Minimal APIs** over controllers — concise, fast, and uses C# 12 `Results` + `MapGroup` for tidy endpoint composition.
- **Interface-first repositories & services** for testability and DI seam.
- **No DDD/MediatR** overhead — simple vertical slices keep cognitive load low for a CRUD-shaped domain.
- **EF Core Interceptors** for cross-cutting audit and read-only enforcement.
- **`ITimeProvider` abstraction** with a `PhilippinesTimeProvider` so business dates and the seeded `DateTime.UtcNow` are deterministic in tests.
- **FluentValidation** for request DTOs — run automatically before the handler executes.
- **`GlobalExceptionHandler` middleware** converts exceptions to a consistent `{ success, message, errors }` envelope.

---

## Features

The `Features/` directory is grouped by domain. Each folder is self-contained:

| Feature | Path | Purpose |
|---|---|---|
| **Auth** | `Features/Auth/` | Login, refresh, logout, change-password, JWT issuance & revocation |
| **Account** | `Features/Account/` | "My Account" page — profile, sessions, activity, processed loans, recent clients |
| **Users** | `Features/Users/` | Admin: create, view, edit, suspend internal users |
| **Roles** | `Features/RoleManagement/` | List roles + role × permission matrix |
| **Branches** | `Features/Branches/` | List / lookup the 31 branches |
| **Loans** | `Features/Loans/` | Loan applications — list, get, create, update status (workflow) |
| **WebLoans** | `Features/WebLoans/` | Read-only WebLoan borrower & active-loan lookups |
| **Dashboard** | `Features/Dashboard/` | Branch- and role-scoped summary metrics |
| **Common** | `Common/` | Cross-cutting: middleware, exceptions, models, extensions, auth, constants, time |
| **Infrastructure** | `Infrastructure/` | `AppDbContext`, `WebLoanDbContext`, interceptors, `DbInitializer` |

---

## Project Structure

```
alas_v2_backend/
├── EBI.ALAS.Api/
│   ├── Program.cs                              # Composition root + pipeline
│   ├── appsettings.json                        # Default config (placeholder secrets)
│   ├── EBI.ALAS.Api.csproj                     # Project file (net8.0)
│   │
│   ├── Common/
│   │   ├── Authorization/
│   │   │   ├── PermissionRequirement.cs        # IAuthorizationRequirement
│   │   │   └── PermissionAuthorizationHandler.cs
│   │   ├── Constants/
│   │   │   ├── Permissions.cs                  # 14 permission keys
│   │   │   ├── Roles.cs                        # 5 roles
│   │   │   └── RolePermissions.cs              # Role → Permission[] matrix
│   │   ├── Exceptions/
│   │   │   ├── NotFoundException.cs
│   │   │   ├── ForbiddenAccessException.cs
│   │   │   └── InvalidWorkflowException.cs
│   │   ├── Extensions/
│   │   │   ├── ClaimsPrincipalExtensions.cs    # GetUserId/GetRole/GetBranchId
│   │   │   ├── FluentValidationExtensions.cs
│   │   │   └── ServiceCollectionExtensions.cs  # AddApplicationServices()
│   │   ├── Middleware/
│   │   │   └── GlobalExceptionHandler.cs       # Catches & shapes all errors
│   │   ├── Models/
│   │   │   ├── ApiResponse.cs                  # { success, message, data, errors }
│   │   │   └── PagedResult.cs
│   │   └── Time/
│   │       ├── ITimeProvider.cs                # Abstraction
│   │       ├── PhilippinesTimeProvider.cs      # UTC + Asia/Manila helper
│   │       ├── TimeProviderExtensions.cs
│   │       └── UtcDateTimeConverter.cs         # JSON: DateTime → "...Z"
│   │
│   ├── Features/
│   │   ├── Auth/             (Endpoints, Repo, Service, Validators, Entities)
│   │   ├── Account/          (Endpoints, Service, Repo, Validators, DTOs)
│   │   ├── Users/            (Endpoints, Service, Repo, Validators, DTOs)
│   │   ├── RoleManagement/   (Endpoints)
│   │   ├── Branches/         (Endpoints, Service, Repo, DTOs, Entity)
│   │   ├── Loans/            (Endpoints, Workflow Service, Repo, Audit, Validators)
│   │   ├── WebLoans/         (Endpoints, Service, Entities, DTOs)
│   │   └── Dashboard/        (Endpoints, Service, DTOs)
│   │
│   ├── Infrastructure/
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs                # Main ALAS EF Core context
│   │   │   ├── WebLoanDbContext.cs            # Read-only EF Core context
│   │   │   └── DbInitializer.cs               # Seeds branches + users on first run
│   │   └── Interceptors/
│   │       ├── AuditSaveChangesInterceptor.cs  # Stamps CreatedAt/ModifiedAt
│   │       └── WebLoanReadOnlyInterceptor.cs   # Throws on any non-SELECT SQL
│   │
│   ├── Migrations/                              # EF Core migrations
│   └── Properties/
│       └── launchSettings.json                  # Local dev URLs (https://localhost:7220)
│
├── EBI.ALAS.V2.slnx                            # Solution file
├── .gitignore
├── swagger.txt                                 # Pointer to local Swagger UI
└── README.md                                   # You are here
```

---

## Tech Stack

| Layer | Technology | Version |
|---|---|---|
| Runtime | .NET | **8.0** |
| Web Framework | ASP.NET Core (Minimal APIs) | 8.0 |
| ORM | Entity Framework Core | 8.0.0 |
| Database | Microsoft SQL Server | — |
| Auth | `Microsoft.AspNetCore.Authentication.JwtBearer` | 8.0.0 |
| Token Crypto | `System.IdentityModel.Tokens.Jwt` | 7.1.2 |
| Password Hashing | `BCrypt.Net-Next` | 4.0.3 |
| Validation | `FluentValidation.AspNetCore` | 11.3.0 |
| API Docs | `Swashbuckle.AspNetCore` | 6.5.0 |

### Notable C# Features Used

- `Nullable` reference types — **enabled** project-wide
- `ImplicitUsings` — **enabled**
- `record` types for immutable DTOs
- `init`-only setters on request DTOs
- `JsonStringEnumConverter` and a custom `UtcDateTimeConverter` for stable JSON
- C# 12 collection expressions

---

## Getting Started

### Prerequisites

- **.NET 8 SDK** (download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0))
- **SQL Server** (any edition — LocalDB, Express, Developer, or full)
- A SQL Server database named `ALASv2_DB` (auto-created on first run via `EnsureCreatedAsync`)
- **Optional**: a SQL Server database named `webloan` containing the legacy WebLoan tables (only needed if you want to exercise the `/api/webloans/*` endpoints)

### 1. Clone & Restore

```powershell
git clone <your-repo-url> alas_v2_backend
cd alas_v2_backend
dotnet restore
```

### 2. Configure `appsettings.json`

Edit `EBI.ALAS.Api/appsettings.json` and replace the placeholders:

```jsonc
{
  "ConnectionStrings": {
    "DefaultConnection":  "Server=YOUR_SQL_HOST;Database=ALASv2_DB;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True",
    "WebLoanConnection":  "Server=YOUR_SQL_HOST;Database=webloan;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True"
  },
  "Jwt": {
    "SecretKey": "REPLACE_WITH_A_RANDOM_32+_CHAR_SECRET"
  }
}
```

> The application will **not start** if `Jwt:SecretKey` is shorter than the algorithm requires. Use `openssl rand -base64 48` or similar.

> For production, store secrets in environment variables or a secret manager — **never** commit real credentials. Use `appsettings.Development.json` (gitignored) or `dotnet user-secrets`.

### 3. Run

```powershell
dotnet run --project EBI.ALAS.Api
```

The API listens on:

- **HTTPS:** `https://localhost:7220`
- **HTTP:** `http://localhost:5173`

Swagger UI is available at **`https://localhost:7220/swagger/index.html`** when `ASPNETCORE_ENVIRONMENT=Development`.

On first start, `DbInitializer` will:

1. Create the `ALASv2_DB` schema via `EnsureCreatedAsync`.
2. Seed 31 branches.
3. Seed the default users (see [Seed Data](#seed-data)).

---

## Configuration

All settings live in `EBI.ALAS.Api/appsettings.json`. Override per-environment in `appsettings.{Environment}.json` or via environment variables using the standard ASP.NET Core hierarchy.

### `ConnectionStrings`

| Key | Purpose |
|---|---|
| `DefaultConnection` | EF Core `AppDbContext` — read/write primary database |
| `WebLoanConnection` | EF Core `WebLoanDbContext` — read-only legacy integration |

### `Jwt`

| Key | Default | Description |
|---|---|---|
| `SecretKey` | (placeholder) | HMAC-SHA256 signing key. **Required.** ≥ 32 chars. |
| `Issuer` | `EBI.ALAS.V2` | `iss` claim |
| `Audience` | `EBI.ALAS.V2.Frontend` | `aud` claim |
| `ExpiryMinutes` | `15` | Access token lifetime (short-lived) |
| `RefreshTokenExpiryDays` | `7` | Refresh token sliding lifetime |
| `AbsoluteSessionExpiryDays` | `14` | Hard cap on any single login session, regardless of rotation |

`ClockSkew` is set to `TimeSpan.Zero` — tokens expire exactly when their `exp` says they do.

### `Cors.AllowedOrigins`

A whitelist of origins allowed to call the API with credentials. Defaults include common Vite/Next.js dev ports. Add your production frontend domain here.

### `RateLimiting.Login`

Fixed-window limiter applied to `POST /api/auth/login`:

| Key | Default | Description |
|---|---|---|
| `PermitLimit` | `5` | Attempts allowed per window |
| `WindowSeconds` | `60` | Window length |

Exceeding the limit returns `429 Too Many Requests`.

---

## Security Model

### Authentication

- **Access Token (JWT)** — returned in the JSON body on `POST /api/auth/login`. Short-lived (15 min default). The frontend stores it in-memory (e.g., Zustand) — not in `localStorage` — to limit XSS impact.
- **Refresh Token** — opaque random string, stored only as a **hash** in the DB. Delivered as an `HttpOnly`, `Secure`, `SameSite=Strict` cookie scoped to `/api/auth`. Invisible to JavaScript → XSS-proof.
- **Refresh Rotation** — every successful `POST /api/auth/refresh` issues a new refresh token and revokes the old one. The previous access token's JTI is also added to a blacklist until its natural expiry.
- **Token Revocation (Blacklist)** — `RevokedTokens` table stores JTI + user + expiry. `OnTokenValidated` in the JWT pipeline checks this on every request.
- **Password Hashing** — BCrypt (`BCrypt.Net-Next`).
- **Timing-Attack Mitigation** — login always runs a BCrypt verify, even when the user does not exist (using a dummy hash).

### Password Policy (Change Password)

- Minimum **8 characters**
- Must contain uppercase, lowercase, digit, and one of `!?*.`
- Must differ from the current password
- Changing password **globally revokes all sessions** for that user

### Authorization

Built on ASP.NET Core's policy-based authorization, layered on **14 granular permissions**:

| Permission | Key |
|---|---|
| Create / view / recommend / evaluate / approve / reject loans | `loans.create`, `loans.view`, `loans.recommend`, `loans.evaluate`, `loans.approve`, `loans.reject` |
| Manage / view loan products | `loan_product.manage`, `loan_product.view` |
| Create / view / edit / suspend users | `user.create`, `user.view`, `user.edit`, `user.suspend` |
| Manage / view roles | `role.manage`, `role.view` |

These are bound to **named policies** (`CanCreateLoan`, `CanViewUsers`, etc.) and enforced per-endpoint via `.RequireAuthorization("PolicyName")`.

### Five Roles

| Role | Display | Stage of workflow |
|---|---|---|
| **Encoder** | Encoder (AO/CAA) | Creates loan applications |
| **Recommender** | Branch Head | Reviews & recommends |
| **Evaluator** | Credit Checker | Evaluates credit worthiness |
| **Approver** | Area Head | Final approval / rejection / revision |
| **Admin** | Administrator | Full access; can perform any workflow transition; manages users |

The role × permission matrix is exposed via `GET /api/roles/matrix`.

### Defense-in-Depth Layers

1. **HTTPS** enforced in non-Development environments.
2. **CORS** whitelist with credentials.
3. **Rate limiting** on login.
4. **JWT Bearer** authentication on every endpoint except `/health` and `/api/auth/login`, `/api/auth/refresh`.
5. **Policy-based authorization** with `[PermissionRequirement]` → `PermissionAuthorizationHandler`.
6. **Workflow validation** in `LoanWorkflowService.IsValidTransition` — even if a role had the HTTP permission, they cannot transition a loan outside the defined state machine.
7. **FluentValidation** on every input DTO.
8. **Global exception handler** — never leaks stack traces in non-Development.
9. **Audit interceptor** — every `SaveChanges` stamps `CreatedAt`/`ModifiedAt`.
10. **Read-only interceptor** on `WebLoanDbContext` — throws before any non-`SELECT` SQL hits the legacy DB.

---

## Loan Workflow

A loan application flows through a strict 10-state machine. Each transition is gated by both the **user's role** and the **from/to status pair**. Admins bypass role gates but the transition itself must still be valid.

```
                        ┌─────────┐
                        │  Draft  │
                        └────┬────┘
                             │  Encoder
                             ▼
              ┌──────────────────────────┐
              │   ForRecommendation      │
              └────────────┬─────────────┘
                           │  Recommender
                           ▼
              ┌──────────────────────────┐
              │       ForChecking        │
              └────────────┬─────────────┘
                           │  Evaluator
                           ▼
              ┌──────────────────────────┐
              │       ForApproval        │
              └────┬──────┬─────────┬────┘
       Approver   │      │ Approver│  Approver
        ┌──────────┘      │         └──────────┐
        ▼                 ▼                    ▼
   ┌─────────┐      ┌──────────┐         ┌──────────┐
   │ Approved│      │ Rejected │         │ForRevision│
   └────┬────┘      └──────────┘         └─────┬─────┘
   Admin│                                       │ Encoder
        ▼                                        ▼
   ┌───────────────┐                  ┌────────────────────┐
   │ForDisbursement│                  │ ForRecommendation  │
   └───────┬───────┘                  └────────────────────┘
       Admin│
           ▼
     ┌──────────┐
     │ Disbursed│
     └────┬─────┘
       Admin│
           ▼
     ┌─────────┐
     │ OnGoing │
     └─────────┘
```

Implemented in `Features/Loans/LoanWorkflowService.cs`. Every transition is recorded in `LoanAction` with `FromStatus`, `ToStatus`, `Comments`, `ActionByUserId`, and `ActionDate`.

---

## API Reference

All endpoints return an `ApiResponse<T>` envelope:

```json
{
  "success": true,
  "message": "Loan created successfully",
  "data": { ... },
  "errors": null
}
```

### Health

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/health` | Public | Liveness probe |

### Authentication `/api/auth`

| Method | Path | Auth | Rate-Limited | Description |
|---|---|---|---|---|
| `POST` | `/api/auth/login` | Public | Yes (`LoginLimiter`) | Login → access token (body) + refresh cookie |
| `POST` | `/api/auth/refresh` | Public (cookie) | No | Silent rotation |
| `POST` | `/api/auth/logout` | JWT | No | Revoke access + refresh, clear cookie |
| `POST` | `/api/auth/change-password` | JWT | No | Change password; revokes all sessions |

### Account `/api/account` *(My Account)*

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/account/me` | Get current user profile |
| `PUT` | `/api/account/me` | Update profile |
| `GET` | `/api/account/me/sessions` | List active sessions (paged) |
| `DELETE` | `/api/account/me/sessions/{id}` | Revoke a session |
| `GET` | `/api/account/me/activity` | Recent activity |
| `GET` | `/api/account/me/loans` | Recently processed loans |
| `GET` | `/api/account/me/clients` | Recently handled clients |

All require JWT.

### Users `/api/users`

| Method | Path | Policy | Description |
|---|---|---|---|
| `GET` | `/api/users` | `CanViewUsers` | List users (paged, filterable) |
| `GET` | `/api/users/{id}` | `CanViewUsers` | Get user |
| `POST` | `/api/users` | `CanCreateUsers` | Create user |
| `PUT` | `/api/users/{id}` | `CanEditUsers` | Update user |
| `PATCH` | `/api/users/{id}/status` | `CanSuspendUsers` | Suspend / activate |

### Roles `/api/roles`

| Method | Path | Policy | Description |
|---|---|---|---|
| `GET` | `/api/roles` | `CanViewRoles` | List roles |
| `GET` | `/api/roles/matrix` | `CanViewRoles` | Role × permission matrix |

### Branches `/api/branches`

| Method | Path | Policy | Description |
|---|---|---|---|
| `GET` | `/api/branches` | `CanViewUsers` | Paged branch list |
| `GET` | `/api/branches/all` | `CanViewUsers` | All branches (no paging) |
| `GET` | `/api/branches/{id}` | `CanViewUsers` | Get by numeric id |
| `GET` | `/api/branches/code/{code}` | `CanViewUsers` | Get by branch code (e.g., `007`) |

### Loans `/api/loans`

| Method | Path | Policy | Description |
|---|---|---|---|
| `GET` | `/api/loans` | `CanViewLoan` | Paged, scoped by user's branch & role |
| `GET` | `/api/loans/{id}` | `CanViewLoan` | Full loan detail incl. actions & WebLoan traceability |
| `POST` | `/api/loans` | `CanCreateLoan` | Create draft loan |
| `PUT` | `/api/loans/{id}/status` | (workflow role) | Transition status (validates role + transition) |

### WebLoans `/api/webloans` *(read-only, integration)*

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/webloans/cis/{cisNo}/search` | Step 1 — borrower + account list |
| `GET` | `/api/webloans/cis/{cisNo}/accounts/{accountNo}` | Step 2 — PN records for an account |
| `GET` | `/api/webloans/cis/{cisNo}/accounts/{accountNo}/active-loans` | Up to 10 active loans for the (CIS, account) pair; each row carries a CASE-computed `amortAmount` (C35/C23 → `principal`, otherwise `amort_data.total_amort`) |
| `GET` | `/api/webloans/cis/{cisNo}` | Full borrower profile (backward compatible) |

### Dashboard `/api/dashboard`

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/dashboard/summary` | Aggregated counts, branch- & role-scoped |

---

## Database

### Primary Database — `ALASv2_DB`

Owned by this application. Created via `EnsureCreatedAsync` on first run. Core tables include:

- `Users` — system users
- `Branches` — branch registry
- `RefreshTokens` — hashed refresh tokens with absolute expiry
- `RevokedTokens` — JTI blacklist for access tokens
- `LoanApplications` — main loan entities
- `LoanActions` — audit log of every status change
- *(Plus EF Core migrations under `Migrations/`)*

### Secondary Database — `webloan` *(read-only)*

The legacy WebLoan core banking system. Tables include (per `WebLoanDbContext`):

- `cis_info` / `cis_info_misdata` — borrower master data
- `mis_group` — group/membership info
- `loan_acct_info` — loan account master
- `loan_data` — loan detail records
- `loan_product` / `loan_status` — lookup tables
- `creation_types` — disbursement types

This DB is **accessed read-only**. The `WebLoanReadOnlyInterceptor` blocks any non-`SELECT` command before it reaches SQL Server, providing application-level enforcement independent of SQL Server permissions.

### Audit & Time

- `AuditSaveChangesInterceptor` automatically populates `CreatedAt` / `ModifiedAt` for entities that expose these properties.
- All times flow through `ITimeProvider` — the default implementation is `PhilippinesTimeProvider`, which produces UTC values while exposing helpers for `Asia/Manila` business logic.

---

## WebLoan Integration

The WebLoan feature exposes a **drill-down flow** designed for the loan-origination UI:

1. **Search CIS** — `GET /api/webloans/cis/{cisNo}/search` returns the borrower (`cis_info` + `mis_group`) and a flat list of their accounts (`loan_acct_info`). The frontend renders these as cards.
2. **Pick an account** — `GET /api/webloans/cis/{cisNo}/accounts/{accountNo}` returns all `loan_data` rows (PN records) for that account. The frontend renders the PN table.
3. **Pull active loans** — `GET /api/webloans/cis/{cisNo}/accounts/{accountNo}/active-loans` returns up to 10 active loans (filters `bch='000'`, `is_loan=1`, `loan_status != 10`, ordered by `date_granted desc`). 404 if the account does not belong to the given CIS — prevents cross-tenant enumeration. Each loan row carries a CASE-computed `amortAmount` sourced from `amort_data.total_amort` (first installment, `amort_no = 1`), falling back to `principal` for `C35`/`C23` products.
4. **Full profile** — `GET /api/webloans/cis/{cisNo}` returns everything in one response for backward compatibility.

When a loan is created referencing a WebLoan CIS/account, the resulting `LoanApplication` stores the WebLoan `cis_no`, `bch_code`, `account_no`s, and `pn_no`s for full traceability — visible on `GET /api/loans/{id}` under `WebLoanCisNo`, `WebLoanBranchCode`, `WebLoanAccountNumbers`, `WebLoanPnNumbers`, `WebLoanLastSyncedAt`.

---

## Seed Data

On first run, `DbInitializer` seeds the following if the `Users` table is empty:

### Default Admin

| Username | Password | Branch | Role |
|---|---|---|---|
| `admin` | `admin123` | `011` (Head Office) | Admin |

### Test Users (one per workflow role, branch `007` Tandag)

| Username | Password | Role |
|---|---|---|
| `encoder1` | `encoder123` | Encoder |
| `recommender1` | `recommender1` | Recommender |
| `evaluator1` | `evaluator123` | Evaluator |
| `approver1` | `approver123` | Approver |

> **All default passwords must be changed immediately in any non-development environment.** Also note the `MustChangePassword` flag pattern — first-login flows can use this to force a credential reset.

### Branches

31 branches across the Philippines are pre-seeded, including a Corporate Center (`991`) and Head Office (`011`).

---

## Development

### Useful Commands

```powershell
# Build
dotnet build

# Run
dotnet run --project EBI.ALAS.Api

# Hot-reload
dotnet watch run --project EBI.ALAS.Api

# EF Core migrations (if you change entity models)
dotnet ef migrations add MyChange --project EBI.ALAS.Api
dotnet ef database update --project EBI.ALAS.Api

# Run on a custom port
dotnet run --project EBI.ALAS.Api --urls "https://localhost:8443"

# Swagger UI (after running)
# https://localhost:7220/swagger/index.html
```

### Testing Auth with Swagger

1. Open `https://localhost:7220/swagger`.
2. Call `POST /api/auth/login` with `{ "username": "admin", "password": "admin123" }`.
3. The response body contains `data.accessToken`.
4. Click **Authorize** at the top of Swagger UI, paste the access token (the `Bearer` prefix is added automatically).
5. All subsequent authorized calls will include the token. Refresh happens transparently via the `HttpOnly` cookie.

### Adding a New Loan Workflow Transition

1. Update `Features/Loans/LoanWorkflowService.cs` `ValidTransitions` dictionary.
2. Add any new permission string to `Common/Constants/Permissions.cs`.
3. Add the policy binding in `Program.cs` (`options.AddPolicy(...)`).
4. Map the permission to the responsible role in `Common/Constants/RolePermissions.cs`.
5. The endpoint automatically enforces the new transition on next save.

### Environment Variables (alternative to `appsettings`)

ASP.NET Core picks these up automatically:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=...;Database=ALASv2_DB;..."
$env:Jwt__SecretKey = "your-strong-secret-here"
$env:Jwt__ExpiryMinutes = "15"
dotnet run --project EBI.ALAS.Api
```

### Local Swagger Note

A `swagger.txt` file at the solution root contains a pointer to the local Swagger URL for convenience. Open it after starting the API.

---

## License

Internal project — all rights reserved.

---

**Maintained by:** EBI SD
**Repository:** `alas_v2_backend`
**Solution:** `EBI.ALAS.V2.slnx`