using System;

namespace Assistant.Core.Abstractions
{
    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
