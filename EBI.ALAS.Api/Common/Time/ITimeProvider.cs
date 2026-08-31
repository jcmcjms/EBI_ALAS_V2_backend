namespace EBI.ALAS.Api.Common.Time;

/// <summary>
/// Provides consistent time operations across the application.
/// Uses Philippines Time (PHT, UTC+8) as the reference timezone for business operations.
/// All database timestamps are stored in UTC; this provider handles conversion for display/logging.
/// </summary>
public interface ITimeProvider
{
    DateTime UtcNow { get; }

    DateTime PhilippinesNow { get; }

    DateTimeOffset UtcNowOffset { get; }

    DateTimeOffset PhilippinesNowOffset { get; }

    DateTime ToPhilippinesTime(DateTime utcDateTime);

    DateTimeOffset ToPhilippinesTimeOffset(DateTime utcDateTime);

    DateTime ToUtc(DateTime philippinesDateTime);

    TimeZoneInfo PhilippinesTimeZone { get; }
}