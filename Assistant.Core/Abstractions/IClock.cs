using System;

namespace Assistant.Core.Abstractions
{
    public interface IClock
    {
        DateTimeOffset UtcNow { get; }
    }
}
