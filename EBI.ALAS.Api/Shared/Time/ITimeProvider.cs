namespace EBI.ALAS.Api.Shared.Time;
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