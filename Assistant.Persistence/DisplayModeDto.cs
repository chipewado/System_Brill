using System;
using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Persistence
{
    /// <summary>
    /// Data transfer object for DisplayMode persistence, separating domain logic from serialization concerns.
    /// </summary>
    public class DisplayModeDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public ColorRgba Color { get; set; } = ColorRgba.White;
        public string KeyBinding { get; set; } = string.Empty;
        public FilterMode Filter { get; set; } = FilterMode.Inclusive;
        public List<TokenRarity> Rarities { get; set; } = new List<TokenRarity>();
        public float MinValue { get; set; } = 0f;
        public float? MaxDistance { get; set; } = null;
        public bool ShowTracer { get; set; } = false;
        public bool Enabled { get; set; } = true;
        public int Priority { get; set; } = 0;

        public static DisplayModeDto FromDomain(DisplayMode mode)
        {
            return new DisplayModeDto
            {
                Id = mode.Id,
                Name = mode.Name,
                Color = mode.Color,
                KeyBinding = mode.KeyBinding,
                Filter = mode.Filter,
                Rarities = mode.Rarities != null ? new List<TokenRarity>(mode.Rarities) : new List<TokenRarity>(),
                MinValue = mode.MinValue,
                MaxDistance = mode.MaxDistance,
                ShowTracer = mode.ShowTracer,
                Enabled = mode.Enabled,
                Priority = mode.Priority
            };
        }

        public DisplayMode ToDomain()
        {
            var mode = new DisplayMode(Id)
            {
                Name = Name,
                Color = Color,
                KeyBinding = KeyBinding,
                Filter = Filter,
                Rarities = Rarities != null ? new HashSet<TokenRarity>(Rarities) : new HashSet<TokenRarity>(),
                MinValue = MinValue,
                MaxDistance = MaxDistance,
                ShowTracer = ShowTracer,
                Enabled = Enabled,
                Priority = Priority
            };
            return mode;
        }
    }
}
