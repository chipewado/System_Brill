using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Assistant.Core.Abstractions;

namespace Assistant.Tests
{
    [TestFixture]
    public class ClockAndAmbientTimeTests
    {
        [Test]
        public void FakeClock_InitialTime_IsSetCorrectly()
        {
            var initial = new DateTimeOffset(2026, 5, 10, 8, 30, 0, TimeSpan.Zero);
            var clock = new FakeClock(initial);

            Assert.That(clock.UtcNow, Is.EqualTo(initial));
        }

        [Test]
        public void FakeClock_Advance_IncrementsTimeDeterministically()
        {
            var initial = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var clock = new FakeClock(initial);

            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.That(clock.UtcNow, Is.EqualTo(new DateTimeOffset(2026, 1, 1, 0, 0, 10, TimeSpan.Zero)));

            clock.AdvanceSeconds(25.5);
            Assert.That(clock.UtcNow, Is.EqualTo(new DateTimeOffset(2026, 1, 1, 0, 0, 35, TimeSpan.Zero).AddMilliseconds(500)));
        }

        [Test]
        public void FakeClock_SetTime_OverridesTime()
        {
            var clock = new FakeClock();
            var newTime = new DateTimeOffset(2027, 12, 31, 23, 59, 59, TimeSpan.Zero);

            clock.SetTime(newTime);
            Assert.That(clock.UtcNow, Is.EqualTo(newTime));
        }

        [Test]
        public void SystemClock_ReturnsValidUtcTime()
        {
            var clock = SystemClock.Instance;
            var now = clock.UtcNow;

            Assert.That(now, Is.GreaterThan(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            Assert.That(now.Offset, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void AmbientTimeAudit_OnlySystemClock_AccessesWallClock()
        {
            // Find solution root directory relative to test assembly execution
            var baseDir = TestContext.CurrentContext.TestDirectory;
            var dir = new DirectoryInfo(baseDir);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BrilliantCryptoAssistant.slnx")) &&
                   !File.Exists(Path.Combine(dir.FullName, "BrilliantCryptoAssistant.sln")))
            {
                dir = dir.Parent;
            }

            Assert.That(dir, Is.Not.Null, "Could not locate solution root folder.");
            var slnRoot = dir!.FullName;

            var targetProjects = new[] { "Assistant.Core", "Assistant.Sim" };
            var forbiddenPatterns = new[]
            {
                new Regex(@"\bDateTime\.UtcNow\b", RegexOptions.Compiled),
                new Regex(@"\bDateTime\.Now\b", RegexOptions.Compiled),
                new Regex(@"\bDateTimeOffset\.UtcNow\b", RegexOptions.Compiled),
                new Regex(@"\bDateTimeOffset\.Now\b", RegexOptions.Compiled)
            };

            var violations = new System.Collections.Generic.List<string>();

            foreach (var proj in targetProjects)
            {
                var projDir = Path.Combine(slnRoot, proj);
                if (!Directory.Exists(projDir)) continue;

                var csFiles = Directory.GetFiles(projDir, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                                !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

                foreach (var file in csFiles)
                {
                    var fileName = Path.GetFileName(file);
                    // SystemClock.cs is the ONLY designated wall-clock provider
                    if (fileName.Equals("SystemClock.cs", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        // Skip comments
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                        {
                            continue;
                        }

                        foreach (var pat in forbiddenPatterns)
                        {
                            if (pat.IsMatch(line))
                            {
                                violations.Add($"{Path.GetRelativePath(slnRoot, file)}:Line {i + 1}: {trimmed}");
                            }
                        }
                    }
                }
            }

            Assert.That(violations, Is.Empty,
                "Found ambient wall-clock access in business/simulation logic:\n" + string.Join("\n", violations));
        }
    }
}
