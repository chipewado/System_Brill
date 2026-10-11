using System;
using Assistant.Core.Abstractions;

namespace Assistant.Sim
{
    /// <summary>
    /// Deterministic simulation clock advanced strictly by the simulation harness.
    /// Provides consistent, repeatable timestamps for simulation runs.
    /// </summary>
    public sealed class SimClock : IClock
    {
        private DateTimeOffset _currentTime;

        public SimClock(DateTimeOffset? startTime = null)
        {
            _currentTime = startTime ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }

        public DateTimeOffset UtcNow => _currentTime;

        public void Advance(TimeSpan dt)
        {
            if (dt < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(dt), "Time delta cannot be negative.");

            _currentTime = _currentTime.Add(dt);
        }

        public void Advance(double seconds)
        {
            Advance(TimeSpan.FromSeconds(seconds));
        }
    }
}
