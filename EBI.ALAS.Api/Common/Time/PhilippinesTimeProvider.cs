using System;

namespace EBI.ALAS.Api.Common.Time;
public sealed class PhilippinesTimeProvider : ITimeProvider
{
    private static readonly TimeZoneInfo s_philippinesTimeZone = 
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime PhilippinesNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, s_philippinesTimeZone);
    public DateTimeOffset UtcNowOffset => DateTimeOffset.UtcNow;
    public DateTimeOffset PhilippinesNowOffset => new DateTimeOffset(PhilippinesNow, s_philippinesTimeZone.GetUtcOffset(DateTime.UtcNow));
    public DateTime ToPhilippinesTime(DateTime utcDateTime)
    {
        // Ensure the input is treated as UTC
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, s_philippinesTimeZone);
    }
    public DateTimeOffset ToPhilippinesTimeOffset(DateTime utcDateTime)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        var philippinesTime = TimeZoneInfo.ConvertTimeFromUtc(utc, s_philippinesTimeZone);
        return new DateTimeOffset(philippinesTime, s_philippinesTimeZone.GetUtcOffset(utc));
    }
    public DateTime ToUtc(DateTime philippinesDateTime)
    {
        // Treat input as Philippines Time (unspecified kind), convert to UTC
        var unspecified = DateTime.SpecifyKind(philippinesDateTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, s_philippinesTimeZone);
    }
    public TimeZoneInfo PhilippinesTimeZone => s_philippinesTimeZone;
}