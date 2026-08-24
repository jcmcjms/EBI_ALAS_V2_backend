using System.Text;
using System.Threading.RateLimiting;
using Alas.Api.Endpoints;
using Alas.Api.Endpoints.Admin;
using Alas.Api.Endpoints.Auth;
using Alas.Api.Endpoints.Audit;
using Alas.Api.Endpoints.Loans;
using Alas.Api.Security;
using Alas.Application.Admin.Roles;
using Alas.Application.Admin.Users;
using Alas.Application.Audit;
using Alas.Application.Common.Auditing;
using Alas.Application.Common.Security;
using Alas.Application.Loans;
using Alas.Infrastructure.Auditing;
using Alas.Infrastructure.Identity;
using Alas.Infrastructure.Persistence;
using Alas.Infrastructure.Services;
using Alas.Infrastructure.Security;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Rate Limiting ────────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"auth:{ipAddress}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

// ── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AlasDbContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Alas"),
        sql =>
        {
            sql.EnableRetryOnFailure(maxRetryCount: 3);
            sql.MigrationsAssembly(typeof(AlasDbContext).Assembly.FullName);
        });
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

// ── Identity ─────────────────────────────────────────────────────────────────
builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<AlasDbContext>()
    .AddDefaultTokenProviders();

// ── JWT Configuration ────────────────────────────────────────────────────────
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

var jwtOptions = builder.Configuration.GetSection("Jwt")
    .Get<JwtOptions>() ?? throw new InvalidOperationException("JWT options are missing.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

// ── Authorization (Permission-based RBAC) ────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in AlasPermissions.All)
    {
        options.AddPolicy(permission, policy =>
        {
            policy.RequirePermission(permission);
        });
    }
});

// ── Application Services ─────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IUserPermissionProvider, UserPermissionProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

// ── Domain Services ──────────────────────────────────────────────────────────
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<RoleService>();
builder.Services.AddScoped<LoanService>();
builder.Services.AddScoped<AuditQueryService>();

// ── Audit Infrastructure ─────────────────────────────────────────────────────
builder.Services.AddSingleton<AuditChannel>();
builder.Services.AddSingleton<IAuditLogger, ChannelAuditLogger>();
builder.Services.AddHostedService<AuditQueueWriter>();

// ── FluentValidation ─────────────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

// ── CORS ─────────────────────────────────────────────────────────────────────
var frontendOrigins = builder.Configuration
    .GetSection("Frontend:Origins")
    .Get<string[]>() ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy
            .WithOrigins(frontendOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ── Swagger ──────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter your JWT token"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ── Seed RBAC in Development ────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    await RbacSeeder.SeedAsync(app.Services);
}

// ── Middleware Pipeline ──────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapRoleEndpoints();
app.MapLoanEndpoints();
app.MapAuditEndpoints();

app.Run();
