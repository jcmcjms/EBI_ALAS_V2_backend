using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using EBI.ALAS.Api.Common.Authorization;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Middleware;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Dashboard;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.RoleManagement;
using EBI.ALAS.Api.Features.Users;
using EBI.ALAS.Api.Features.WebLoans;
using EBI.ALAS.Api.Infrastructure.Data;
using EBI.ALAS.Api.Infrastructure.Interceptors;
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
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));
    options.AddInterceptors(new AuditSaveChangesInterceptor());
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

    options.AddFixedWindowLimiter("LoginLimiter", limiterOptions =>
    {
        limiterOptions.PermitLimit = configuration.GetValue<int>("RateLimiting:Login:PermitLimit", 5);
        limiterOptions.Window = TimeSpan.FromSeconds(configuration.GetValue<int>("RateLimiting:Login:WindowSeconds", 60));
        limiterOptions.QueueLimit = 0;
    });
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

// ─── Minimal API Endpoints ───────────────────────────────────────────────────

// Health check
app.MapHealthChecks("/health");

// Auth endpoints
app.MapAuthEndpoints();

// User management endpoints
app.MapUserEndpoints();

// Role management endpoints
app.MapRoleEndpoints();

// Loan endpoints
app.MapLoanEndpoints();

// WebLoan integration endpoints (read-only fetch from webloan DB)
app.MapWebLoanEndpoints();

// Dashboard endpoints
app.MapDashboardEndpoints();

// ─── Database Initialization ─────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.InitializeAsync(dbContext, scope.ServiceProvider);
}

app.Run();
