using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using EBI.ALAS.Api.Common.Authorization;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Middleware;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Account;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Dashboard;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.RoleManagement;
using EBI.ALAS.Api.Features.Users;
using EBI.ALAS.Api.Features.WebLoans;
using EBI.ALAS.Api.Infrastructure.Data;
using EBI.ALAS.Api.Infrastructure.Interceptors;
using EBI.ALAS.Api.Infrastructure.Security;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ─── Configuration ───────────────────────────────────────────────────────────
var configuration = builder.Configuration;
var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
var corsOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()!;

// ─── Database ────────────────────────────────────────────────────────────────
builder.Services.AddScoped<AuditSaveChangesInterceptor>();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));
    options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
});

// WebLoan system database (same server, read-only access)
builder.Services.AddDbContext<WebLoanDbContext>(options =>
{
    options.UseSqlServer(configuration.GetConnectionString("WebLoanConnection"));
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking); // read-only: skip change tracking
    options.AddInterceptors(new WebLoanReadOnlyInterceptor());          // blocks any write/DDL SQL before it reaches the DB
});

// ─── Authentication ──────────────────────────────────────────────────────────
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
        ClockSkew = TimeSpan.Zero // Strict token expiration
    };

    // Check token blacklist on every authenticated request
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var jti = context.Principal?.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti);
            if (string.IsNullOrEmpty(jti))
            {
                context.Fail("Token missing JTI claim");
                return;
            }

            // Resolve scoped service from the request scope
            var tokenRevocationRepo = context.HttpContext.RequestServices
                .GetRequiredService<ITokenRevocationRepository>();

            if (await tokenRevocationRepo.IsTokenRevokedAsync(jti))
            {
                context.Fail("Token has been revoked (user logged out)");
            }
        }
    };
});

// ─── Authorization ───────────────────────────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    // Loan policies
    options.AddPolicy("CanCreateLoan", policy =>
        policy.Requirements.Add(new PermissionRequirement(Permissions.LoansCreate)));
    options.AddPolicy("CanViewLoan", policy =>
        policy.Requirements.Add(new PermissionRequirement(Permissions.LoansView)));
    options.AddPolicy("CanRecommendLoan", policy =>
        policy.Requirements.Add(new PermissionRequirement(Permissions.LoansRecommend)));
    options.AddPolicy("CanEvaluateLoan", policy =>
        policy.Requirements.Add(new PermissionRequirement(Permissions.LoansEvaluate)));
    options.AddPolicy("CanApproveLoan", policy =>
        policy.Requirements.Add(new PermissionRequirement(Permissions.LoansApprove)));
    options.AddPolicy("CanRejectLoan", policy =>
        policy.Requirements.Add(new PermissionRequirement(Permissions.LoansReject)));

    // User management policies
    options.AddPolicy("CanViewUsers", policy =>
        policy.Requirements.Add(new PermissionRequirement(Permissions.UserView)));
    options.AddPolicy("CanCreateUsers", policy =>
        policy.Requirements.Add(new PermissionRequirement(Permissions.UserCreate)));
    options.AddPolicy("CanEditUsers", policy =>
        policy.Requirements.Add(new PermissionRequirement(Permissions.UserEdit)));
    options.AddPolicy("CanSuspendUsers", policy =>
        policy.Requirements.Add(new PermissionRequirement(Permissions.UserSuspend)));

    // Role policies
    options.AddPolicy("CanViewRoles", policy =>
        policy.Requirements.Add(new PermissionRequirement(Permissions.RoleView)));
});

// ─── CORS ────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ─── Rate Limiting ───────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // On rejection, populate Retry-After header (clients can back off intelligently).
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";

        var payload = EBI.ALAS.Api.Common.Models.ApiResponse.ErrorResponse(
            "Too many requests. Please slow down and retry after the period indicated by the Retry-After header.");

        await context.HttpContext.Response.WriteAsJsonAsync(payload, cancellationToken);
    };

    options.AddFixedWindowLimiter("LoginLimiter", limiterOptions =>
    {
        limiterOptions.PermitLimit = configuration.GetValue<int>("RateLimiting:Login:PermitLimit", 5);
        limiterOptions.Window = TimeSpan.FromSeconds(configuration.GetValue<int>("RateLimiting:Login:WindowSeconds", 60));
        limiterOptions.QueueLimit = 0;
    });

    // Global per-user limiter for all authenticated data endpoints. Without this,
    // a leaked token could be used to scrape the entire API.
    options.AddPolicy("DataLimiter", context =>
    {
        // Partition by user when authenticated, by IP otherwise.
        // Identity.Name is populated from the Name claim; we fall back to the
        // standard `sub` claim when Name is empty (common for JWT bearer tokens).
        var partitionKey = context.User?.Identity?.IsAuthenticated == true
            ? (context.User.Identity.Name
               ?? context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
               ?? "anonymous")
            : (context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip");

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = configuration.GetValue<int>("RateLimiting:Data:PermitLimit", 120),
            Window = TimeSpan.FromSeconds(configuration.GetValue<int>("RateLimiting:Data:WindowSeconds", 60)),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    });

    // Apply DataLimiter as the global fallback so every endpoint gets protection
    // without each endpoint needing to opt-in. Endpoints that have their own
    // named policy (login, refresh) still win because RequireRateLimiting
    // takes precedence over the fallback policy.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // Don't double-limit the auth endpoints — they have LoginLimiter/refresh
        // exemptions built in via their own policies.
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetNoLimiter("no-limit");
        }

        var partitionKey = context.User?.Identity?.IsAuthenticated == true
            ? (context.User.Identity.Name
               ?? context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
               ?? "anonymous")
            : (context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip");

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = configuration.GetValue<int>("RateLimiting:Data:PermitLimit", 120),
            Window = TimeSpan.FromSeconds(configuration.GetValue<int>("RateLimiting:Data:WindowSeconds", 60)),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    });
});

// ─── JSON Serialization ────────────────────────────────────────────────────────
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    // Use UTC format with 'Z' suffix for DateTime to avoid timezone ambiguity
    options.SerializerOptions.Converters.Add(new UtcDateTimeConverter());
});

builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
});

// ─── Application Services ────────────────────────────────────────────────────
builder.Services.AddApplicationServices();

// ─── FluentValidation ────────────────────────────────────────────────────────
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddFluentValidationClientsideAdapters();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ─── API Explorer & Swagger ──────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "EBI.ALAS.V2 API",
        Version = "v1",
        Description = "Banking-grade .NET 8 Web API for loan application management"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ─── Health Checks ───────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

// ─── Banking Security Hardening ─────────────────────────────────────────────
// Fail-fast on weak/missing JWT secret in Production. Runs before
// builder.Build() so a misconfigured deploy never silently boots.
builder.Services.AddBankingSecurityHardening(builder.Configuration, builder.Environment);

var app = builder.Build();

// ─── Middleware Pipeline ─────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Only redirect to HTTPS in Production
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// CORS must be before Authentication/Authorization
app.UseCors("AllowFrontend");

// Global Exception Handler — catches all unhandled exceptions downstream
// and returns consistent { success, message, errors } JSON responses.
// Positioned after CORS so exception responses include the CORS headers.
app.UseMiddleware<GlobalExceptionHandler>();

// Rate limiting
app.UseRateLimiter();

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// CSRF validation — MUST come after UseAuthentication so the bearer token
// (and therefore the XsrfToken claim) is already on HttpContext.User.
app.UseCsrfValidation();

// ─── Minimal API Endpoints ───────────────────────────────────────────────────

// Health check
app.MapHealthChecks("/health");

// Auth endpoints
app.MapAuthEndpoints();

// User management endpoints
app.MapUserEndpoints();

// Role management endpoints
app.MapRoleEndpoints();

// Branch endpoints
app.MapBranchEndpoints();

// Loan endpoints
app.MapLoanEndpoints();

// WebLoan integration endpoints (read-only fetch from webloan DB)
app.MapWebLoanEndpoints();

// Dashboard endpoints
app.MapDashboardEndpoints();

// Account endpoints (My Account page)
app.MapAccountEndpoints();

// ─── Database Initialization ─────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.InitializeAsync(dbContext, scope.ServiceProvider);
}

app.Run();
