# AGENT INSTRUCTIONS

You are an expert AI pair programmer and senior .NET backend engineer working on **ALAS V2 (Automated Loan Application System)**. You are operating in a high-stakes **banking environment** where security, data integrity, and scalability are non-negotiable.

Whenever you generate, update, or review code for this repository, you MUST adhere strictly to the following guidelines.

---

## 1. PROJECT CONTEXT

- **Domain:** Loan origination lifecycle featuring a strict **Four-Eyes Workflow** (Encoding → Recommendation → Evaluation → Approval).
- **Integrations:** Read-only integration with a legacy core banking system (`WebLoan`).
- **Tech Stack:** .NET 8, ASP.NET Core Minimal APIs, EF Core 8 (SQL Server), JWT Bearer, FluentValidation, MediatR, MassTransit/RabbitMQ, SignalR, Redis.
- **Scale Target:** Architect for millions of users, low latency, and high throughput. Every millisecond and database round-trip matters.

---

## 2. ARCHITECTURE & STRUCTURE

We use **Vertical Slice Architecture** rather than traditional layered (Clean/N-Tier) architecture. Code is organized by *feature*, not by technical concern.

### Standard Feature Folder Structure:

```text
EBI.ALAS.Api/
├── Features/
│   ├── Account/
│   ├── ApprovalMatrix/
│   ├── AuditLogs/
│   ├── Auth/
│   ├── Branches/
│   ├── Dashboard/
│   ├── Loans/
│   │   ├── Endpoints/          # Minimal API mappings (MapGet, MapPost, etc.)
│   │   ├── Services/           # Business logic and orchestration
│   │   ├── Repositories/       # Complex data access (optional; use Services if simple)
│   │   ├── DTOs/               # Request/Response records
│   │   ├── Entities/           # EF Core models
│   │   └── Validators/         # FluentValidation rules
│   ├── Notifications/
│   ├── Presence/
│   ├── RoleManagement/
│   ├── SystemSettings/
│   ├── Users/
│   └── WebLoans/
├── Common/
│   ├── Authorization/          # PermissionAuthorizationHandler, PermissionRequirement, BranchScopeService
│   ├── Constants/              # Permissions, Roles, RolePermissions, RoleQueues
│   ├── Exceptions/             # NotFoundException, ForbiddenAccessException, InvalidWorkflowException, CapacityGateException
│   ├── Extensions/             # ClaimsPrincipalExtensions, FluentValidationExtensions, ServiceCollectionExtensions
│   ├── Middleware/              # GlobalExceptionHandler, SecurityHeadersMiddleware, CorrelationIdMiddleware, RequestLoggingMiddleware, CsrfValidationMiddleware, IpAllowlistMiddleware, IdempotencyMiddleware
│   ├── Models/                 # ApiResponse<T>, PagedResult<T>, PaginationRequest
│   └── Time/                   # ITimeProvider, PhilippinesTimeProvider, UtcDateTimeConverter
├── Infrastructure/
│   ├── Data/                   # AppDbContext, WebLoanDbContext, DbInitializer
│   ├── Interceptors/           # AuditSaveChangesInterceptor, WebLoanReadOnlyInterceptor
│   ├── Messaging/              # IEventPublisher, MassTransitEventPublisher, Consumers (AuditLogConsumer, NotificationConsumer)
│   ├── Security/               # Banking security hardening extensions
│   └── SignalR/                # JwtUserIdProvider, NotificationHub
├── Migrations/
└── Program.cs
```

### Key Architectural Patterns:

- **Two DbContexts:** `AppDbContext` (read-write, primary database) and `WebLoanDbContext` (read-only, legacy core banking integration).
- **CQRS via MediatR:** Endpoints dispatch commands/queries through MediatR handlers for decoupling.
- **Event-Driven via MassTransit:** Audit logging and notification delivery are asynchronous via RabbitMQ (in-memory fallback for dev).
- **Real-Time via SignalR:** WebSocket-based notifications with Redis backplane for horizontal scaling.

---

## 3. CLEAN CODE & C# 12 PRINCIPLES

- **SOLID & SRP:** A service class should do one thing. If an endpoint handles complex orchestration, extract it into a dedicated Service.
- **Modern C# (.NET 8):**
  - Use **Primary Constructors** for Services, Validators, and Endpoints.
  - Use `record` types for all DTOs. Use `init`-only setters for Request DTOs to ensure immutability after creation.
  - Use C# 12 collection expressions (`[]`) for initializing lists/arrays.
  - **Nullable Reference Types** are enabled globally. Never ignore compiler warnings regarding nullability. Use `ArgumentNullException.ThrowIfNull()` where appropriate.
- **Naming:** Use descriptive, intention-revealing names. Method names should read like verbs (`ProcessLoanApplicationAsync`), properties like nouns (`ApprovalStatus`).
- **No Over-Abstraction:** Do not create an interface for a service unless there are multiple implementations or it is strictly required for unit testing isolation. Prefer concrete classes to reduce complexity.
- **Consistency:** Ensure API DTO property names use `camelCase` (default in .NET JSON serialization) to map seamlessly to the React/TypeScript frontend interfaces.
- **Response Wrapper:** All API responses MUST use `ApiResponse<T>` or `ApiResponse` from `Common/Models/ApiResponse.cs`. Never return raw data or raw error objects.

### Example Response Patterns:

```csharp
// Success with data
return Results.Ok(ApiResponse<List<LoanResponse>>.SuccessResponse(loans));

// Success with message
return Results.Ok(ApiResponse.SuccessResponse("Assignment released."));

// Error responses
return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));
return Results.BadRequest(ApiResponse.ErrorResponse("Insufficient funds", errors));
```

---

## 4. SECURITY (BANKING-GRADE / NON-NEGOTIABLE)

- **The Four-Eyes Principle:** A user can NEVER approve a workflow step they initiated or previously handled. Enforce this using the `RolePermissions` matrix and `PermissionAuthorizationHandler`.
- **IDOR Prevention:** Never trust IDs passed in the URL/Body without verifying the authenticated user has access to that specific record. Use `ClaimsPrincipalExtensions.GetUserId()`, `GetBranchId()`, and `GetBranchCode()` to scope queries.
- **Legacy Database Safety:** `WebLoanDbContext` is strictly read-only. The `WebLoanReadOnlyInterceptor` will block writes, but your code must NEVER attempt to add/update/delete entities in this context. Always use `.AsNoTracking()` on legacy queries. The context overrides `SaveChanges()` and `SaveChangesAsync()` to throw `InvalidOperationException`.
- **Audit Logging:** Every state mutation in the `Loans` feature MUST be auditable. Rely on the `AuditSaveChangesInterceptor` to stamp `CreatedAt`, `ModifiedAt`, and user tracking automatically. Do not manually manage audit timestamps in business logic.
- **Validation:** Apply `FluentValidation` at the Minimal API endpoint level (using Endpoint Filters or explicit validation before service invocation). Fail fast on invalid input. Never pass unvalidated data to the service layer.
- **Secrets & Passwords:** Never hardcode connection strings or secrets. Use `BCrypt.Net-Next` for password hashing. Never use reversible encryption or weak hashes (MD5/SHA).
- **Rate Limiting:** Login endpoints use `LoginLimiter` (5 requests/60s). Data endpoints use per-user `DataLimiter` (120 requests/60s). Global rate limiter excludes `/api/auth` and `/health`.
- **Token Security:** JWT tokens include JTI claims for revocation. The `ITokenRevocationRepository` checks revoked tokens on every request. Refresh tokens use SHA-256 hashing.
- **CSRF Protection:** `CsrfValidationMiddleware` runs after authentication but before authorization.
- **Security Headers:** `SecurityHeadersMiddleware` applies banking-grade HTTP security headers.
- **IP Allowlisting:** `IpAllowlistMiddleware` restricts admin endpoints to approved IP ranges.

### Authorization Policies:

```csharp
// Available policies (defined in Program.cs):
"CanCreateLoan"      // Permissions.LoansCreate
"CanViewLoan"        // Permissions.LoansView
"CanRecommendLoan"   // Permissions.LoansRecommend
"CanEvaluateLoan"    // Permissions.LoansEvaluate
"CanApproveLoan"     // Permissions.LoansApprove
"CanRejectLoan"      // Permissions.LoansReject
"CanViewUsers"       // Permissions.UserView
"CanCreateUsers"     // Permissions.UserCreate
"CanEditUsers"       // Permissions.UserEdit
"CanSuspendUsers"    // Permissions.UserSuspend
"CanViewRoles"       // Permissions.RoleView
"CanViewAuditLogs"   // Permissions.AuditLogsView
"CanViewLoanProduct" // Permissions.LoanProductView
"CanManageLoanProduct" // Permissions.LoanProductManage
"CanManageWorkflow"  // Permissions.WorkflowManage
```

---

## 5. PERFORMANCE & SCALABILITY (MILLIONS OF USERS)

- **Database Round-Trips:**
  - **Eliminate N+1 Queries:** Use `.Include()` or, preferably, **Projection** (`.Select(x => new Dto { ... })`) directly to DTOs to minimize data payload.
  - **Pagination:** List endpoints MUST use `PagedResult<T>`. Never return an unbounded `IEnumerable` or `List<T>`.
  - **Read-Only Queries:** Always append `.AsNoTracking()` to read-only queries to bypass EF Core's Change Tracker overhead.
  - **Split Queries:** Global `QuerySplittingBehavior.SplitQuery` is configured. Complex `Include()` chains automatically use split queries. Override with `.AsSingleQuery()` only when necessary.
- **Asynchronous I/O:**
  - Everything must be `async`/`await`. Use `.ToListAsync()`, `.FirstOrDefaultAsync()`, etc.
  - **Cancellation:** ALWAYS accept and pass `CancellationToken` from the Minimal API endpoint down to the EF Core queries and HTTP calls.
- **Caching Strategy:**
  - **L1 (IMemoryCache):** Hot-path caching for JTI revocation blacklist, idempotency replays, and dashboard summaries.
  - **L2 (Redis Distributed Cache):** Cross-process coherence for multi-pod deployments. Configured via `AddStackExchangeRedisCache`.
  - **Output Caching:** `BranchCache` (5min), `DashboardCache` (30s), `LoanProductCache` (10min).
- **Time Handling:** Store all dates in the database as **UTC**. Use the custom `PhilippinesTimeProvider` for localized display conversions if required. Rely on `UtcDateTimeConverter` for JSON serialization.
- **Response Compression:** Brotli + Gzip compression enabled. Reduces average JSON payload from ~200KB to ~30KB.
- **Connection Pooling:** SQL Server connection strings should explicitly configure `MaxPool Size` and `Min Pool Size` for connection pooling control.

### EF Core 8 Specifics (SQL Server 2019):

```csharp
// Program.cs DbContext configuration pattern:
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlServer(
        configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.CommandTimeout(30);
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: new[] { 4060, 40197, 40501, 40613, 49918, 49919, 49920 });
            sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        });
    options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
});
```

---

## 6. ERROR HANDLING

- Rely on `GlobalExceptionHandler` middleware to catch exceptions and shape them into the standard `ApiResponse<T>` wrapper.
- Throw domain-specific exceptions located in `Common/Exceptions/`:
  - `NotFoundException` — Entity not found (404)
  - `ForbiddenAccessException` — Authorization failure (403)
  - `InvalidWorkflowException` — Workflow state transition violation (400)
  - `CapacityGateException` — Approval capacity exceeded (429)
- Never swallow exceptions. Never return `null` from a service method when an entity is expected; throw `NotFoundException`.
- Use `ApiResponse.ErrorResponse()` for all error responses.

### Exception Usage Pattern:

```csharp
// In service layer:
if (loan is null)
    throw new NotFoundException(nameof(LoanApplication), id);

if (!HasPermission(user, Permissions.LoansApprove))
    throw new ForbiddenAccessException("You do not have permission to approve loans.");

// In endpoints (handled automatically by GlobalExceptionHandler):
// The middleware catches exceptions and returns:
// {
//   "success": false,
//   "message": "LoanApplication with id 42 was not found.",
//   "errors": [],
//   "timestamp": "2026-09-18T..."
// }
```

---

## 7. AGENT WORKFLOW FOR CREATING/UPDATING FEATURES

When instructed to create a new feature or update an existing one, you MUST execute the following sequence:

1. **Define the Slice:** Create or locate the feature folder. Keep all files strictly within this slice.
2. **Define Contracts:** Create immutable `record` Request and Response DTOs. Use `ApiResponse<T>` wrapper for responses.
3. **Validate:** Write a `FluentValidator` for the Request DTO.
4. **Implement Logic:** Write the Service class using a Primary Constructor to inject dependencies (`AppDbContext`, `ITimeProvider`, `IEventPublisher`, etc.).
5. **Map Endpoint:** Write the Minimal API.
   - Add `.RequireAuthorization("SpecificPermission")`.
   - Validate the request using the validator.
   - Pass `CancellationToken` to the service.
   - Return `ApiResponse<T>` wrapped responses.
6. **Security Check:** Verify ownership/branch scoping. Ensure the Four-Eyes principle isn't violated.
7. **Performance Check:** Check for `.AsNoTracking()`, pagination, and N+1 risks.

### Minimal API Endpoint Template:

```csharp
public static class MyFeatureEndpoints
{
    public static void MapMyFeatureEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/my-feature")
            .WithTags("My Feature")
            .RequireAuthorization();

        group.MapGet("/", async (
            AppDbContext db,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            var branchCode = principal.GetBranchCode();

            var items = await db.MyEntities
                .AsNoTracking()
                .Where(e => e.BranchCode == branchCode)
                .Select(e => new MyResponse
                {
                    Id = e.Id,
                    Name = e.Name,
                })
                .ToListAsync(ct);

            return Results.Ok(ApiResponse<List<MyResponse>>.SuccessResponse(items));
        })
        .WithName("GetMyEntities")
        .Produces<ApiResponse<List<MyResponse>>>(200)
        .RequireAuthorization("CanViewMyFeature");

        group.MapPost("/", async (
            CreateMyEntityRequest request,
            IValidator<CreateMyEntityRequest> validator,
            AppDbContext db,
            ClaimsPrincipal principal,
            IEventPublisher events,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var userId = principal.GetUserId();

            var entity = new MyEntity
            {
                Name = request.Name,
                CreatedById = userId,
            };

            db.MyEntities.Add(entity);
            await db.SaveChangesAsync(ct);

            await events.PublishAsync(new MyEntityCreatedEvent(entity.Id), ct);

            return Results.Ok(ApiResponse<MyResponse>.SuccessResponse(
                new MyResponse { Id = entity.Id, Name = entity.Name },
                "Entity created successfully."));
        })
        .WithName("CreateMyEntity")
        .Produces<ApiResponse<MyResponse>>(200)
        .ProducesValidationProblem()
        .RequireAuthorization("CanCreateMyFeature");
    }
}
```

---

## 8. TESTING STRATEGY

- **Unit Tests:** Write xUnit tests for all Service logic, especially around the Four-Eyes workflow and state transitions.
- **Integration Tests:** Use `WebApplicationFactory` to test Minimal API endpoints against an in-memory or Testcontainers SQL Server database.
- **Mocking:** Use `NSubstitute` or `Moq` for external dependencies, but prefer testing against real DbContexts (via Testcontainers) for data access logic to catch EF Core translation errors.

### Test Project Structure:

```text
EBI.ALAS.Tests/
├── ApprovalFormConventionsTests.cs
├── TempPasswordGeneratorTests.cs
├── UserRepositoryTests.cs
└── [Feature]Tests.cs
```

---

## 9. DATABASE CONVENTIONS

### AppDbContext (Primary Database):

- **Audit Columns:** All entities should have `CreatedAt` (DateTime, required) and optionally `UpdatedAt` (DateTime, nullable). The `AuditSaveChangesInterceptor` handles these automatically.
- **Foreign Keys:** Use `DeleteBehavior.Restrict` for audit-critical relationships (User references). Use `DeleteBehavior.Cascade` only for owned child entities (OutstandingLoans, BuyOuts, etc.).
- **Indexes:** Create composite indexes for common query patterns. Use SQL Server filtered indexes where appropriate (e.g., `WorkflowQueueItems` live rows).
- **Decimal Precision:** Use `decimal(18,2)` for monetary amounts. Use `decimal(9,6)` for interest rates.

### WebLoanDbContext (Legacy Read-Only):

- **NEVER write to this context.** The `WebLoanReadOnlyInterceptor` blocks all write operations.
- **Always use `.AsNoTracking()`.** The context is configured with `QueryTrackingBehavior.NoTracking` by default.
- **Use `IDbContextFactory<WebLoanDbContext>`** for parallel queries. DbContext is not thread-safe; the factory produces short-lived contexts on demand.
- **Keyless Entities:** Many WebLoan entities use `HasNoKey()` because the legacy database has composite keys that EF Core doesn't map directly.

---

## 10. MESSAGING & EVENTS

### MassTransit/RabbitMQ:

- **Audit Logging:** `AuditLogConsumer` processes audit events asynchronously.
- **Notifications:** `NotificationConsumer` delivers in-app notifications via SignalR.
- **Event Publisher:** Use `IEventPublisher` (wraps `IPublishEndpoint`) to publish domain events.

### SignalR:

- **Real-Time Notifications:** `NotificationHub` at `/hubs/notifications`.
- **User Routing:** `JwtUserIdProvider` maps JWT `userId` claim to SignalR's user-based routing.
- **Redis Backplane:** Required for multi-pod deployments. Messages fan out across all pods.

---

## 11. KEY FILES REFERENCE

| File | Purpose |
|------|---------|
| `Program.cs` | Application startup, DI registration, middleware pipeline |
| `Infrastructure/Data/AppDbContext.cs` | Primary database context with all entity configurations |
| `Infrastructure/Data/WebLoanDbContext.cs` | Read-only legacy database context |
| `Infrastructure/Interceptors/AuditSaveChangesInterceptor.cs` | Auto-stamps CreatedAt/UpdatedAt on SaveChanges |
| `Infrastructure/Interceptors/WebLoanReadOnlyInterceptor.cs` | Blocks write commands against WebLoan database |
| `Common/Models/ApiResponse.cs` | Standard API response wrapper |
| `Common/Models/PagedResult.cs` | Pagination wrapper |
| `Common/Extensions/ClaimsPrincipalExtensions.cs` | JWT claim extraction helpers |
| `Common/Constants/Permissions.cs` | Permission constants |
| `Common/Constants/Roles.cs` | Role constants |
| `Common/Exceptions/NotFoundException.cs` | 404 exception |
| `Common/Exceptions/ForbiddenAccessException.cs` | 403 exception |
| `Common/Exceptions/InvalidWorkflowException.cs` | Workflow violation exception |
| `Common/Time/ITimeProvider.cs` | Time abstraction for testability |
| `Common/Time/PhilippinesTimeProvider.cs` | UTC+8 time provider |
| `Infrastructure/Messaging/IEventPublisher.cs` | Event publishing abstraction |

---

## 12. COMMON PITFALLS TO AVOID

1. **Writing to WebLoanDbContext:** This will throw `InvalidOperationException`. Always use `AppDbContext` for writes.
2. **Forgetting CancellationToken:** Every async method must accept and propagate `CancellationToken`.
3. **Missing `.AsNoTracking()`:** Read-only queries without this waste memory and CPU on change tracking.
4. **Returning raw objects:** Always wrap responses in `ApiResponse<T>`.
5. **Hardcoding user IDs:** Always extract from `ClaimsPrincipal` using `GetUserId()`.
6. **Ignoring Four-Eyes:** Verify the user hasn't already acted on the workflow step.
7. **Unbounded queries:** Always paginate list endpoints using `PagedResult<T>`.
8. **Synchronous I/O:** Never use `.Result` or `.Wait()`. Always `await` async operations.
9. **Missing validation:** Always validate input with FluentValidation before processing.
10. **Manual audit timestamps:** Let `AuditSaveChangesInterceptor` handle `CreatedAt`/`UpdatedAt`.

---

*This document is the single source of truth for AI agents working on the ALAS V2 codebase. Follow these guidelines strictly to maintain security, performance, and architectural consistency.*
