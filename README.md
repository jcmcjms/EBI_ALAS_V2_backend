# ALAS v2 Backend

> A .NET 8 Clean Architecture backend for loan management — JWT auth, permission-based RBAC, rotating refresh tokens, and Minimal APIs.

[![.NET 8](https://img.shields.io/badge/.NET-8-purple)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![SQL Server](https://img.shields.io/badge/SQL-Server-CC2927?logo=microsoft-sql-server&logoColor=white)](https://www.microsoft.com/en-us/sql-server)

## Why This Exists

Loan management systems need secure, auditable workflows — from officer creation through manager approval to disbursement. ALAS v2 provides a production-ready backend that handles authentication, role-based access control, and the full loan lifecycle so frontend teams can build the UI without worrying about the security layer.

## Quick Start

```bash
# Clone the repo
git clone https://github.com/your-org/alas_v2_backend.git
cd alas_v2_backend

# Update connection string in appsettings.Development.json
# Run migrations
dotnet ef database update --project Alas.Infrastructure --startup-project Alas.Api

# Run the API
dotnet run --project Alas.Api
```

Open `https://localhost:{port}/swagger` — you're up.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 8 |
| API | ASP.NET Core Minimal APIs (no controllers) |
| Auth | ASP.NET Core Identity + JWT Bearer (HMAC-SHA256) |
| Database | SQL Server + Entity Framework Core |
| Validation | FluentValidation |
| Docs | Swagger/OpenAPI |
| Audit | Channel-based async logging (BackgroundService) |

## Architecture

Clean Architecture with strict dependency flow:

```
Api → Application → Domain
         ↑
    Infrastructure
```

```
alas_v2_backend/
├── Alas.Domain/          # Entities, enums — zero dependencies
├── Alas.Application/     # DTOs, validators, security contracts, interfaces
├── Alas.Infrastructure/  # EF Core, Identity, services implementations
└── Alas.Api/             # Minimal API endpoints, Swagger, Program.cs
```

**Dependency rules:**
- `Domain` depends on nothing
- `Application` depends only on `Domain`
- `Infrastructure` depends on `Application` (and `Domain`)
- `Api` depends on `Application` (and `Domain`)

## Project Structure

```
Alas.Api/
├── Endpoints/
│   ├── AuthEndpoints.cs              # login, refresh, logout, me
│   ├── AuthResponse.cs               # response DTO
│   ├── Auth/
│   │   └── AuthValidators.cs         # FluentValidation for login
│   ├── Loans/
│   │   └── LoanEndpoints.cs          # CRUD + workflow endpoints
│   ├── Admin/
│   │   ├── UserEndpoints.cs          # user management
│   │   └── RoleEndpoints.cs          # role management
│   └── Audit/
│       └── AuditEndpoints.cs         # audit log queries
├── Security/
│   ├── PermissionAuthorizationHandler.cs
│   ├── AuthorizationPolicyExtentions.cs
│   └── PermissionRequirement.cs
├── Validation/
│   └── ValidationFilter.cs
├── Program.cs
└── appsettings.json / appsettings.Development.json

Alas.Application/
├── Common/
│   ├── Security/
│   │   ├── AlasClaimTypes.cs
│   │   ├── AlasPermissions.cs        # 8 permissions + SuperAdmin wildcard
│   │   ├── AlasRoles.cs              # 5 roles + permission matrix
│   │   ├── AlasPolicies.cs
│   │   ├── IUserPermissionProvider.cs
│   │   ├── UserPermissionSet.cs
│   │   └── AuthUserDto.cs
│   └── Auditing/
│       ├── IAuditLogger.cs
│       └── AuditEntry.cs
├── Admin/
│   ├── Users/    # UserService DTOs, Validators
│   └── Roles/    # RoleService DTOs, Validators
├── Loans/        # LoanDtos, LoanValidators
└── Audit/        # AuditDtos

Alas.Domain/
└── Entities/
    ├── Loan.cs
    └── LoanStatus.cs

Alas.Infrastructure/
├── Identity/
│   ├── AppUser.cs                    # IdentityUser<Guid>
│   ├── AppRole.cs                    # IdentityRole<Guid>
│   └── RefreshToken.cs
├── Persistence/
│   ├── AlasDbContext.cs              # IdentityDbContext<AppUser, AppRole, Guid>
│   └── Migrations/
├── Security/
│   ├── JwtOptions.cs
│   ├── TokenService.cs               # JWT access token creation
│   ├── RefreshTokenService.cs        # create, rotate, revoke refresh tokens
│   ├── AuthService.cs                # login, refresh, logout orchestration
│   ├── AuthDto.cs / AuthResult.cs
│   ├── RbacSeeder.cs                 # seeds roles + permission claims
│   └── UserPermissionProvider.cs     # cached permission lookup
├── Services/
│   ├── UserService.cs
│   ├── RoleService.cs
│   ├── LoanService.cs
│   └── AuditQueryService.cs
├── Auditing/
│   ├── AuditLog.cs
│   ├── AuditChannel.cs
│   ├── AuditQueueWriter.cs           # BackgroundService
│   └── ChannelAuditLogger.cs
└── Loans/
    └── LoanEntityConfiguration.cs

Root:
├── alasbackend.slnx                  # solution file
├── .gitignore
└── FirstCheck.md                     # original RBAC spec
```

## Domain Entities

### Loan

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `Guid` | Primary key |
| `LoanNumber` | `string` | Unique loan identifier |
| `BorrowerName` | `string` | Borrower's full name |
| `BorrowerContact` | `string` | Contact information |
| `PrincipalAmount` | `decimal` | Loan principal |
| `InterestRate` | `decimal` | Annual interest rate |
| `TermMonths` | `int` | Loan term in months |
| `Purpose` | `string` | Reason for the loan |
| `BranchId` | `Guid` | Branch reference |
| `Status` | `LoanStatus` | Current workflow status |
| `CreatedByUserId` | `Guid` | Officer who created the loan |
| `ApprovedByUserId` | `Guid?` | Manager who approved (nullable) |
| `CreatedUtc` | `DateTime` | Creation timestamp |
| `ApprovedUtc` | `DateTime?` | Approval timestamp |
| `DisbursedUtc` | `DateTime?` | Disbursement timestamp |
| `Remarks` | `string` | General notes |
| `RejectionReason` | `string` | Reason for rejection |

### LoanStatus

| Value | Name | Description |
|-------|------|-------------|
| 0 | `Draft` | Initial state |
| 1 | `PendingReview` | Submitted by officer, awaiting manager review |
| 2 | `PendingApproval` | Reviewed, awaiting final approval |
| 3 | `Approved` | Approved by manager |
| 4 | `Disbursed` | Funds disbursed |
| 5 | `Rejected` | Rejected by manager |
| 6 | `Cancelled` | Cancelled by officer |

## Roles & Permissions

### Roles

| Role | Description | Permissions |
|------|-------------|-------------|
| **SuperAdmin** | Full system access | `*` (all permissions) |
| **Admin** | System administration | `users.manage`, `roles.manage`, `audit.read`, `dashboard.admin` |
| **LoanManager** | Loan approval workflow | `loans.read`, `loans.create`, `loans.approve`, `loans.monitor` |
| **LoanOfficer** | Day-to-day loan operations | `loans.read`, `loans.create`, `loans.monitor` |
| **Auditor** | Read-only audit access | `loans.read`, `audit.read` |

### Permissions

| Permission | Description |
|------------|-------------|
| `loans.read` | View loan details and lists |
| `loans.create` | Create new loans |
| `loans.approve` | Approve or reject loans |
| `loans.monitor` | View loan dashboard/statistics |
| `users.manage` | CRUD operations on users |
| `roles.manage` | CRUD operations on roles and permissions |
| `audit.read` | Query audit logs |
| `dashboard.admin` | Access admin dashboard |

> **Note:** `AlasPermissions.Normalize()` maps legacy frontend permission strings to canonical ones automatically.

## API Endpoints

### Authentication (`/api/auth`)

| Method | Endpoint | Auth | Rate Limit | Description |
|--------|----------|------|------------|-------------|
| `POST` | `/api/auth/login` | Anonymous | 20/min per IP | Login and receive tokens |
| `POST` | `/api/auth/refresh` | Anonymous | 20/min per IP | Rotate refresh token (cookie or body) |
| `POST` | `/api/auth/logout` | Required | — | Revoke refresh token |
| `GET` | `/api/auth/me` | Required | — | Get current user info |

### Loans (`/api/loans`)

| Method | Endpoint | Permission | Description |
|--------|----------|------------|-------------|
| `GET` | `/api/loans` | `loans.read` | List loans (paginated, searchable, filterable) |
| `GET` | `/api/loans/monitor` | `loans.monitor` | Dashboard statistics |
| `GET` | `/api/loans/{id}` | `loans.read` | Loan detail |
| `POST` | `/api/loans` | `loans.create` | Create a loan |
| `POST` | `/api/loans/{id}/submit` | `loans.create` | Submit for review |
| `POST` | `/api/loans/{id}/submit-approval` | `loans.approve` | Submit for approval |
| `POST` | `/api/loans/{id}/approve` | `loans.approve` | Approve loan |
| `POST` | `/api/loans/{id}/reject` | `loans.approve` | Reject loan |

### User Management (`/api/admin/users`)

All endpoints require `users.manage`.

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/admin/users` | List users (paginated, searchable) |
| `GET` | `/api/admin/users/{id}` | User detail |
| `POST` | `/api/admin/users` | Create user |
| `PUT` | `/api/admin/users/{id}/status` | Toggle active status |
| `PUT` | `/api/admin/users/{id}/roles` | Assign roles |

### Role Management (`/api/admin/roles`)

All endpoints require `roles.manage`.

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/admin/roles` | List roles |
| `GET` | `/api/admin/roles/{id}` | Role detail |
| `POST` | `/api/admin/roles` | Create role |
| `PUT` | `/api/admin/roles/{id}/permissions` | Assign permissions |

### Audit (`/api/audit`)

All endpoints require `audit.read`.

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/audit` | Query audit logs (with filtering) |
| `GET` | `/api/audit/login-history` | Login history |
| `GET` | `/api/audit/permission-changes` | Permission change history |
| `GET` | `/api/audit/role-changes` | Role change history |
| `GET` | `/api/audit/loan-events` | Loan event history |

## Security

### Authentication Flow

1. **Login** — validates credentials, returns JWT access token (15 min) + sets refresh token in httpOnly cookie (7 days)
2. **Refresh** — rotates refresh token, revokes old one, issues new access token
3. **Logout** — revokes refresh token, clears cookie

### JWT Configuration

| Parameter | Value |
|-----------|-------|
| Algorithm | HMAC-SHA256 |
| Access token expiry | 15 minutes |
| Refresh token expiry | 7 days |
| Signing key | Configurable via `JwtOptions.SigningKey` in appsettings |

### Refresh Token Security

- **httpOnly cookie** — not accessible via JavaScript
- **Secure flag** — HTTPS only
- **SameSite=Strict** — no cross-site transmission
- **Scoped** — path limited to `/api/auth`
- **Hashed storage** — SHA-256 hash stored in DB, not the raw token
- **Rotation** — new token on every refresh, old token revoked
- **Reuse detection** — if a revoked token is reused, all sessions for that user are revoked

### Permission-Based RBAC

```
JWT Claims → PermissionAuthorizationHandler → IUserPermissionProvider → IMemoryCache (60s TTL)
```

- Permissions stored as Identity `RoleClaims`
- Cached per-user in `IMemoryCache` with 60-second TTL
- Cache invalidation via `IUserPermissionProvider.InvalidateAsync()` when permissions change
- For multi-instance deployments, replace `IMemoryCache` with Redis

### Rate Limiting

Fixed window limiter on authentication endpoints:
- **20 requests per minute** per IP address
- Applies to `/api/auth/login` and `/api/auth/refresh`

### Password Policy

| Requirement | Value |
|-------------|-------|
| Minimum length | 12 characters |
| Requires digit | Yes |
| Requires lowercase | Yes |
| Requires uppercase | Yes |
| Requires non-alphanumeric | Yes |
| Lockout threshold | 5 failed attempts |
| Lockout window | 15 minutes |

### CORS

Configurable frontend origins (default):
- `http://localhost:5173`
- `http://localhost:3000`

## Database

### Connection

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=ALAS_DB;Integrated Security=True;TrustServerCertificate=True"
  }
}
```

### Schema

| Schema | Tables |
|--------|--------|
| `identity` | `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `RefreshToken` |
| `audit` | `AuditLogs` |
| default | `Loans` |

### Migrations

```bash
# Apply all migrations
dotnet ef database update --project Alas.Infrastructure --startup-project Alas.Api

# Create a new migration
dotnet ef migrations add <MigrationName> --project Alas.Infrastructure --startup-project Alas.Api
```

Migrations are located in `Alas.Infrastructure/Persistence/Migrations/`.

## Configuration

### appsettings.Development.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=ALAS_DB;Integrated Security=True;TrustServerCertificate=True"
  },
  "Jwt": {
    "SigningKey": "your-secret-key-at-least-32-characters-long",
    "Issuer": "ALAS",
    "Audience": "ALAS-Frontend",
    "AccessTokenExpiryMinutes": 15,
    "RefreshTokenExpiryDays": 7
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5173",
      "http://localhost:3000"
    ]
  }
}
```

> **Important:** For production, use a proper secret management solution (Azure Key Vault, AWS Secrets Manager, etc.). Never commit signing keys to source control.

## Authorization Flow

```
Request → JWT Middleware (validates token)
       → Endpoint requires policy (e.g., "loans.read")
       → PermissionAuthorizationHandler runs
       → Extracts user ID from JWT claims
       → IUserPermissionProvider loads cached permissions (IMemoryCache, 60s TTL)
       → If user has required permission → 200 OK
       → If not → 403 Forbidden
```

## How to Run

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [SQL Server](https://www.microsoft.com/en-us/sql-server) (local or remote)
- [EF Core tools](https://learn.microsoft.com/en-us/ef/core/cli/dotnet): `dotnet tool install --global dotnet-ef`

### Setup

```bash
# 1. Clone
git clone https://github.com/your-org/alas_v2_backend.git
cd alas_v2_backend

# 2. Configure
#    Edit appsettings.Development.json with your connection string and JWT key

# 3. Run migrations
dotnet ef database update --project Alas.Infrastructure --startup-project Alas.Api

# 4. Run the API
dotnet run --project Alas.Api

# 5. RBAC seeding (roles + permissions) runs automatically in Development mode
```

### Access Swagger

Navigate to `https://localhost:{port}/swagger` in your browser.

## Deployment Notes

- **Multi-instance:** Replace `IMemoryCache` with Redis for shared permission cache
- **Secrets:** Use Azure Key Vault, AWS Secrets Manager, or similar — never hardcode secrets
- **HTTPS:** Required for secure cookies in production
- **Audit logging:** Channel-based async logging via `AuditQueueWriter` hosted service — ensure the background service is running

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Code Standards

- Follow Clean Architecture dependency rules
- Use FluentValidation for all request DTOs
- Add audit logging for state-changing operations
- Write unit tests for services and validators
- Update this README if adding new endpoints or features

## License

MIT
