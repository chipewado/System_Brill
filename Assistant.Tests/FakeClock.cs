using System;
using Assistant.Core.Abstractions;

namespace Assistant.Tests
{
    public class FakeClock : IClock
    {
        private DateTimeOffset _utcNow;

        public DateTimeOffset UtcNow
        {
            get => _utcNow;
            set => _utcNow = value;
        }

        public FakeClock() : this(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
        {
        }

        public FakeClock(DateTimeOffset initialTime)
        {
            _utcNow = initialTime;
        }

        public void SetTime(DateTimeOffset time)
        {
            _utcNow = time;
        }

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }

        public void AdvanceSeconds(double seconds)
        {
            Advance(TimeSpan.FromSeconds(seconds));
        }
    }
}
