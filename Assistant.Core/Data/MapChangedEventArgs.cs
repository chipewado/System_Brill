using System;

namespace Assistant.Core.Data
{
    public class MapChangedEventArgs
    {
        public Region? AffectedRegion { get; }
        public string Reason { get; }
        public DateTimeOffset Timestamp { get; }

        public MapChangedEventArgs(Region? affectedRegion, string reason, DateTimeOffset timestamp)
        {
            AffectedRegion = affectedRegion;
            Reason = reason ?? string.Empty;
            Timestamp = timestamp;
        }

        public static MapChangedEventArgs EntireMap(string reason, DateTimeOffset timestamp) =>
            new MapChangedEventArgs(null, reason, timestamp);

        public static MapChangedEventArgs SubRegion(Region region, string reason, DateTimeOffset timestamp) =>
            new MapChangedEventArgs(region, reason, timestamp);
    }
}
