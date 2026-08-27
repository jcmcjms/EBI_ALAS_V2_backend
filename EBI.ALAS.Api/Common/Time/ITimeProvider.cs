namespace EBI.ALAS.Api.Common.Time;

/// <summary>
/// Provides consistent time operations across the application.
/// Uses Philippines Time (PHT, UTC+8) as the reference timezone for business operations.
/// All database timestamps are stored in UTC; this provider handles conversion for display/logging.
/// </summary>
public interface ITimeProvider
{
    /// <summary>
    /// Gets the current time in UTC (for database storage).
    /// </summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Gets the current time in Philippines Time (PHT, UTC+8) for business logic and display.
    /// </summary>
    DateTime PhilippinesNow { get; }

    /// <summary>
    /// Gets the current time as DateTimeOffset in UTC.
    /// </summary>
    DateTimeOffset UtcNowOffset { get; }

    /// <summary>
    /// Gets the current time as DateTimeOffset in Philippines Time.
    /// </summary>
    DateTimeOffset PhilippinesNowOffset { get; }

    /// <summary>
    /// Converts a UTC DateTime to Philippines Time.
    /// </summary>
    DateTime ToPhilippinesTime(DateTime utcDateTime);

    /// <summary>
    /// Converts a UTC DateTime to Philippines Time as DateTimeOffset.
    /// </summary>
    DateTimeOffset ToPhilippinesTimeOffset(DateTime utcDateTime);

    /// <summary>
    /// Converts a Philippines Time DateTime to UTC.
    /// </summary>
    DateTime ToUtc(DateTime philippinesDateTime);

    /// <summary>
    /// Gets the Philippines Time zone info.
    /// </summary>
    TimeZoneInfo PhilippinesTimeZone { get; }
}