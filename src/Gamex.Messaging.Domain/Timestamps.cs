namespace Gamex.Messaging.Domain;

public static class Timestamps
{
    /// <summary>
    /// Truncates to whole microseconds (MySQL <c>datetime(6)</c>) and marks the value UTC.
    /// Version timestamps are compared by event consumers, so the published value must equal the stored one.
    /// </summary>
    public static DateTime ToStoragePrecision(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return new DateTime(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc);
    }
}
