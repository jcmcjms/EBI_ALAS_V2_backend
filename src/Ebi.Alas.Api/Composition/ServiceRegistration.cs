using System.Text;
using System.Text.Json.Serialization;
using Ebi.Alas.Api.Composition;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Auth.ChangePassword;
using Ebi.Alas.Api.Features.Auth.Login;
using Ebi.Alas.Api.Features.Auth.Refresh;
using Ebi.Alas.Api.Features.Dashboard;
using Ebi.Alas.Api.Features.LoanApplications.GetLoan;
using Ebi.Alas.Api.Features.LoanApplications.ListLoans;
using Ebi.Alas.Api.Features.LoanApplications.Submit;
using Ebi.Alas.Api.Features.LoanApplications.WorkflowActions;
using Ebi.Alas.Api.Features.LoanComputation;
using Ebi.Alas.Api.Features.Users.ChangeUserStatus;
using Ebi.Alas.Api.Features.Users.CreateUser;
using Ebi.Alas.Api.Features.Users.GetUser;
using Ebi.Alas.Api.Features.Users.ListUsers;
using Ebi.Alas.Api.Features.Users.UpdateUser;
using Ebi.Alas.Api.Features.WebLoans;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Background;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ebi.Alas.Api.Composition;

public static class ServiceRegistration
{
    public static IServiceCollection AddApiComposition(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(hostEnvironment);

        services.AddOptions<ApiOptions>()
            .Bind(configuration.GetSection(ApiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AdminSeedOptions>()
            .Bind(configuration.GetSection(AdminSeedOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddProblemDetails();
        services.AddTransient<IExceptionHandler>(serviceProvider =>
            ApiProblemHandler.CreateHandler(serviceProvider.GetRequiredService<IProblemDetailsService>()));
        services.AddOpenApi();
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("auth", httpContext =>
                System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
            options.AddPolicy("write", httpContext =>
                System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        var connectionString = configuration.GetConnectionString("Alas");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<AlasDbContext>(options => options.UseSqlServer(connectionString));
        }
        else if (hostEnvironment.IsDevelopment())
        {
            services.AddDbContext<AlasDbContext>(options =>
                options.UseInMemoryDatabase("alas-dev"));
        }
        else
        {
            throw new InvalidOperationException("ConnectionStrings:Alas is required outside Development.");
        }

        services.AddSingleton(sp =>
        {
            var jwt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiOptions>>().Value.Jwt;
            return new JwtTokenService(jwt);
        });
        services.AddScoped<PasswordHasher>();
        services.AddScoped<TokenStore>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RefreshHandler>();
        services.AddScoped<ChangePasswordHandler>();
        services.AddScoped<CreateUserHandler>();
        services.AddScoped<Features.Users.ImportUsers.ImportUsersHandler>();
        services.AddScoped<ListUsersHandler>();
        services.AddScoped<GetUserHandler>();
        services.AddScoped<UpdateUserHandler>();
        services.AddScoped<ChangeUserStatusHandler>();
        services.AddScoped<Features.LoanProducts.UpdateLoanProductPolicyHandler>();
        services.AddScoped<Features.LoanProducts.SyncLoanProductsHandler>();
        services.AddScoped<Features.LoanProducts.ImportLoanProductsHandler>();
        services.AddScoped<Features.AuditLogs.IAuditLogService, Features.AuditLogs.AuditLogService>();
        services.AddScoped<Features.Users.Seed.AdminUserSeeder>();
        services.AddHostedService<AdminSeedHostedService>();

        var webLoanConnectionString = configuration.GetConnectionString("WebLoan");
        if (!string.IsNullOrWhiteSpace(webLoanConnectionString))
        {
            var webLoanCs = ToReadOnlyIntent(webLoanConnectionString);
            services.AddDbContext<WebLoanDbContext>(options => options.UseSqlServer(webLoanCs));
            services.AddScoped<IWebLoanReader, Infrastructure.WebLoans.WebLoanReader>();
        }
        else if (hostEnvironment.IsDevelopment())
        {
            services.AddDbContext<WebLoanDbContext>(options => options.UseInMemoryDatabase("webloan-dev"));
            services.AddScoped<IWebLoanReader, NullWebLoanReader>();
        }
        else
        {
            services.AddScoped<IWebLoanReader, NullWebLoanReader>();
        }

        services.AddSingleton<LoanComputationService>();
        services.AddScoped<SubmitLoanHandler>();
        services.AddScoped<GetLoanHandler>();
        services.AddScoped<GetLoanByLamIdHandler>();
        services.AddScoped<ListLoansHandler>();
        services.AddScoped<WorkflowQueueService>();
        services.AddScoped<Features.Workflow.DeskQueueService>();
        services.AddScoped<LoanWorkflowService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<Features.Presence.PresenceService>();
        services.AddSignalR();
        services.AddSingleton<Features.Presence.IPresenceNotifier, Features.Presence.SignalRPresenceNotifier>();
        services.AddSingleton<Features.Notifications.IRealtimeNotifier, Features.Notifications.SignalRRealtimeNotifier>();
        services.AddHostedService<Infrastructure.Background.PresenceSweepHostedService>();
        services.AddHostedService<QueueReconciliationHostedService>();
        services.AddHostedService<TokenCleanupHostedService>();
        services.AddHostedService<DisbursementSyncHostedService>();

        services.AddHealthChecks()
            .AddCheck<Infrastructure.Persistence.DbReadyHealthCheck>(
                "db",
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(5));

        var jwtOptions = configuration.GetSection($"{ApiOptions.SectionName}:Jwt").Get<JwtOptions>()
            ?? new JwtOptions();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var path = context.HttpContext.Request.Path.Value ?? "/";
                        context.Token = Auth.HubAccessToken.Resolve(path, context.Request.Query["access_token"]);
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorization();

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("ebi-alas-api"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        return services;
    }

    private static string ToReadOnlyIntent(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ApplicationIntent = ApplicationIntent.ReadOnly
        };
        return builder.ConnectionString;
    }
}
