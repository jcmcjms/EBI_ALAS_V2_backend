using EBI.ALAS.Api.Infrastructure.Caching;
using EBI.ALAS.Api.Infrastructure.Messaging;
using EBI.ALAS.Api.Infrastructure.Messaging.Consumers;
using EBI.ALAS.Api.Infrastructure.SignalR;
using MassTransit;
using StackExchange.Redis;
namespace EBI.ALAS.Api.Common.Extensions;
public static class MessagingExtensions
{
    public static IServiceCollection AddBankingMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMassTransitMessaging(configuration);
        services.AddSignalRMessaging(configuration);
        return services;
    }
    private static IServiceCollection AddMassTransitMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var rabbitMqConnection = configuration.GetConnectionString("RabbitMQ");
        if (!string.IsNullOrWhiteSpace(rabbitMqConnection))
        {
            services.AddMassTransit(x =>
            {
                x.AddConsumer<AuditLogConsumer>();
                x.AddConsumer<NotificationConsumer>();
                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(rabbitMqConnection);
                    cfg.ConfigureEndpoints(context);
                });
            });
        }
        else
        {
            services.AddMassTransit(x =>
            {
                x.AddConsumer<AuditLogConsumer>();
                x.AddConsumer<NotificationConsumer>();
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.ConfigureEndpoints(context);
                });
            });
        }
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
        return services;
    }
    private static IServiceCollection AddSignalRMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");
        services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider,
            JwtUserIdProvider>();
        var signalRBuilder = services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = IsDevelopment();
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        });
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            signalRBuilder.AddStackExchangeRedis(redisConnection, options =>
            {
                options.Configuration.ChannelPrefix =
                    RedisChannel.Literal("ALAS_SignalR");
                options.Configuration.ReconnectRetryPolicy = new ExponentialRetry(5000);
                options.Configuration.KeepAlive = 30;
                options.Configuration.AbortOnConnectFail = false;
            });
        }
        return services;
    }
    private static bool IsDevelopment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }
}
