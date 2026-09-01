using System;

namespace EBI.ALAS.Api.Common.Time;
public static class TimeProviderExtensions
{
    private static readonly TimeZoneInfo PhilippinesTimeZone = 
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
    public static DateTime ToPhilippinesTime(this DateTime utcDateTime)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, PhilippinesTimeZone);
    }
    public static DateTimeOffset ToPhilippinesTimeOffset(this DateTime utcDateTime)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        var philippinesTime = TimeZoneInfo.ConvertTimeFromUtc(utc, PhilippinesTimeZone);
        return new DateTimeOffset(philippinesTime, PhilippinesTimeZone.GetUtcOffset(utc));
    }
    public static DateTime ToUtc(this DateTime philippinesDateTime)
    {
        var unspecified = DateTime.SpecifyKind(philippinesDateTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, PhilippinesTimeZone);
    }
    public static DateTime PhilippinesNow()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhilippinesTimeZone);
    }
    public static DateTimeOffset PhilippinesNowOffset()
    {
        var philippinesTime = PhilippinesNow();
        return new DateTimeOffset(philippinesTime, PhilippinesTimeZone.GetUtcOffset(DateTime.UtcNow));
    }
    public static string ToUtcIsoString(this DateTime utcDateTime)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }
    public static string ToPhilippinesIsoString(this DateTime utcDateTime)
    {
        var philippinesTime = utcDateTime.ToPhilippinesTime();
        return $"{philippinesTime:yyyy-MM-ddTHH:mm:ss.fff}+08:00";
    }
}
