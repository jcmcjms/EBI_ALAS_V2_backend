using System;

namespace EBI.ALAS.Api.Common.Time;

/// <summary>
/// Extension methods for easy Philippines Time conversions.
/// </summary>
public static class TimeProviderExtensions
{
    private static readonly TimeZoneInfo PhilippinesTimeZone = 
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

    /// <summary>
    /// Converts a UTC DateTime to Philippines Time (PHT, UTC+8).
    /// </summary>
    /// <param name="utcDateTime">The UTC DateTime to convert.</param>
    /// <returns>DateTime in Philippines Time.</returns>
    public static DateTime ToPhilippinesTime(this DateTime utcDateTime)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, PhilippinesTimeZone);
    }

    /// <summary>
    /// Converts a UTC DateTime to Philippines Time as DateTimeOffset.
    /// </summary>
    /// <param name="utcDateTime">The UTC DateTime to convert.</param>
    /// <returns>DateTimeOffset in Philippines Time with correct offset.</returns>
    public static DateTimeOffset ToPhilippinesTimeOffset(this DateTime utcDateTime)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        var philippinesTime = TimeZoneInfo.ConvertTimeFromUtc(utc, PhilippinesTimeZone);
        return new DateTimeOffset(philippinesTime, PhilippinesTimeZone.GetUtcOffset(utc));
    }

    /// <summary>
    /// Converts a Philippines Time DateTime to UTC.
    /// </summary>
    /// <param name="philippinesDateTime">The Philippines Time DateTime to convert.</param>
    /// <returns>DateTime in UTC.</returns>
    public static DateTime ToUtc(this DateTime philippinesDateTime)
    {
        var unspecified = DateTime.SpecifyKind(philippinesDateTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, PhilippinesTimeZone);
    }

    /// <summary>
    /// Gets the current Philippines Time.
    /// </summary>
    /// <returns>Current DateTime in Philippines Time.</returns>
    public static DateTime PhilippinesNow()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhilippinesTimeZone);
    }

    /// <summary>
    /// Gets the current Philippines Time as DateTimeOffset.
    /// </summary>
    /// <returns>Current DateTimeOffset in Philippines Time with correct offset.</returns>
    public static DateTimeOffset PhilippinesNowOffset()
    {
        var philippinesTime = PhilippinesNow();
        return new DateTimeOffset(philippinesTime, PhilippinesTimeZone.GetUtcOffset(DateTime.UtcNow));
    }

    /// <summary>
    /// Formats a UTC DateTime as ISO 8601 string with 'Z' suffix (UTC indicator).
    /// Use this for API responses to clearly indicate UTC timestamps.
    /// </summary>
    /// <param name="utcDateTime">The UTC DateTime to format.</param>
    /// <returns>ISO 8601 string with 'Z' suffix.</returns>
    public static string ToUtcIsoString(this DateTime utcDateTime)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }

    /// <summary>
    /// Formats a UTC DateTime as ISO 8601 string in Philippines Time with +08:00 offset.
    /// Use this for API responses when you want to return Philippines Time explicitly.
    /// </summary>
    /// <param name="utcDateTime">The UTC DateTime to format.</param>
    /// <returns>ISO 8601 string with +08:00 offset.</returns>
    public static string ToPhilippinesIsoString(this DateTime utcDateTime)
    {
        var philippinesTime = utcDateTime.ToPhilippinesTime();
        return $"{philippinesTime:yyyy-MM-ddTHH:mm:ss.fff}+08:00";
    }
}