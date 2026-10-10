using System;
using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Visualization
{
    /// <summary>
    /// Standard built-in display mode templates with fixed deterministic IDs.
    /// </summary>
    public static class BuiltInTemplates
    {
        public const string AllId = "tpl.all";
        public const string RarePlusId = "tpl.rare_plus";
        public const string LegendaryId = "tpl.legendary";
        public const string NearbyId = "tpl.nearby";
        public const string BestValueId = "tpl.best_value";

        public static DisplayMode CreateAllTokens()
        {
            return new DisplayMode(AllId)
            {
                Name = "All",
                Color = ColorRgba.White,
                Filter = FilterMode.Exclusive,
                Rarities = new HashSet<TokenRarity>(), // Exclusive + empty set matches everything
                MinValue = 0f,
                MaxDistance = null,
                ShowTracer = false,
                Enabled = true,
                Priority = 0
            };
        }

        public static DisplayMode CreateRarePlus()
        {
            return new DisplayMode(RarePlusId)
            {
                Name = "Rare+",
                Color = ColorRgba.Cyan,
                Filter = FilterMode.Inclusive,
                Rarities = new HashSet<TokenRarity>
                {
                    TokenRarity.Rare,
                    TokenRarity.Epic,
                    TokenRarity.Legendary
                },
                MinValue = 0f,
                MaxDistance = null,
                ShowTracer = true,
                Enabled = true,
                Priority = 10
            };
        }

        public static DisplayMode CreateLegendaryOnly()
        {
            return new DisplayMode(LegendaryId)
            {
                Name = "Legendary",
                Color = ColorRgba.Orange,
                Filter = FilterMode.Inclusive,
                Rarities = new HashSet<TokenRarity>
                {
                    TokenRarity.Legendary
                },
                MinValue = 0f,
                MaxDistance = null,
                ShowTracer = true,
                Enabled = true,
                Priority = 20
            };
        }

        public static DisplayMode CreateNearby(float maxDistance = 100f)
        {
            return new DisplayMode(NearbyId)
            {
                Name = "Nearby",
                Color = ColorRgba.Yellow,
                Filter = FilterMode.Exclusive,
                Rarities = new HashSet<TokenRarity>(),
                MinValue = 0f,
                MaxDistance = maxDistance > 0f ? maxDistance : 100f,
                ShowTracer = false,
                Enabled = true,
                Priority = 5
            };
        }

        public static DisplayMode CreateBestValue(float minValue = 50f)
        {
            return new DisplayMode(BestValueId)
            {
                Name = "Best Value",
                Color = ColorRgba.Purple,
                Filter = FilterMode.Exclusive,
                Rarities = new HashSet<TokenRarity>(),
                MinValue = minValue >= 0f ? minValue : 50f,
                MaxDistance = null,
                ShowTracer = true,
                Enabled = true,
                Priority = 15
            };
        }

        public static IReadOnlyList<DisplayMode> GetTemplateModes(string templateName)
        {
            if (string.IsNullOrWhiteSpace(templateName))
            {
                throw new ArgumentException("Template name must be non-empty.", nameof(templateName));
            }

            string normalized = templateName.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "all":
                case "all tokens":
                case "tpl.all":
                    return new[] { CreateAllTokens() };

                case "rare+":
                case "rare plus":
                case "rare_plus":
                case "tpl.rare_plus":
                    return new[] { CreateRarePlus() };

                case "legendary":
                case "legendary only":
                case "tpl.legendary":
                    return new[] { CreateLegendaryOnly() };

                case "nearby":
                case "tpl.nearby":
                    return new[] { CreateNearby() };

                case "best value":
                case "bestvalue":
                case "best_value":
                case "tpl.best_value":
                    return new[] { CreateBestValue() };

                default:
                    throw new ArgumentException($"Unknown template '{templateName}'.", nameof(templateName));
            }
        }
    }
}
