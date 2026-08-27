using System;

namespace EBI.ALAS.Api.Common.Time;

/// <summary>
/// Implementation of ITimeProvider using Philippines Time (PHT, UTC+8).
/// Philippines does not observe Daylight Saving Time, so the offset is always +8 hours.
/// </summary>
public sealed class PhilippinesTimeProvider : ITimeProvider
{
    private static readonly TimeZoneInfo s_philippinesTimeZone = 
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;

    /// <inheritdoc />
    public DateTime PhilippinesNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, s_philippinesTimeZone);

    /// <inheritdoc />
    public DateTimeOffset UtcNowOffset => DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public DateTimeOffset PhilippinesNowOffset => new DateTimeOffset(PhilippinesNow, s_philippinesTimeZone.GetUtcOffset(DateTime.UtcNow));

    /// <inheritdoc />
    public DateTime ToPhilippinesTime(DateTime utcDateTime)
    {
        // Ensure the input is treated as UTC
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, s_philippinesTimeZone);
    }

    /// <inheritdoc />
    public DateTimeOffset ToPhilippinesTimeOffset(DateTime utcDateTime)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        var philippinesTime = TimeZoneInfo.ConvertTimeFromUtc(utc, s_philippinesTimeZone);
        return new DateTimeOffset(philippinesTime, s_philippinesTimeZone.GetUtcOffset(utc));
    }

    /// <inheritdoc />
    public DateTime ToUtc(DateTime philippinesDateTime)
    {
        // Treat input as Philippines Time (unspecified kind), convert to UTC
        var unspecified = DateTime.SpecifyKind(philippinesDateTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, s_philippinesTimeZone);
    }

    /// <inheritdoc />
    public TimeZoneInfo PhilippinesTimeZone => s_philippinesTimeZone;
}