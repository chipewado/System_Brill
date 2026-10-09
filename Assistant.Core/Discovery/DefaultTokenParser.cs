using System;
using System.Collections.Generic;
using System.Globalization;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;

namespace Assistant.Core.Discovery
{
    /// <summary>
    /// Safe parser for RawMapObject properties into TokenLocation domain instances.
    /// Does not own time: timestamp assignment is deferred to discovery pipeline orchestration.
    /// </summary>
    public class DefaultTokenParser : ITokenParser
    {
        public bool TryParseToken(RawMapObject? raw, out TokenLocation? token, out string? failureReason)
        {
            token = null;

            if (raw == null)
            {
                failureReason = "RawMapObject is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(raw.Id))
            {
                failureReason = "Token ID is null or whitespace.";
                return false;
            }

            if (!raw.Position.IsFinite())
            {
                failureReason = $"Token position contains non-finite values: {raw.Position}";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(raw.ObjectType))
            {
                string typeLower = raw.ObjectType.Trim().ToLowerInvariant();
                if (typeLower == "obstacle" || typeLower == "decoration" || typeLower == "static")
                {
                    failureReason = $"Object type '{raw.ObjectType}' is not a collectible token.";
                    return false;
                }
            }

            TokenRarity rarity = TokenRarity.Common;
            float estimatedValue = 0f;
            bool isCollected = false;

            if (raw.Properties != null)
            {
                // Parse Rarity
                if (raw.Properties.TryGetValue("Rarity", out var rarityObj) && rarityObj != null)
                {
                    if (rarityObj is TokenRarity r)
                    {
                        rarity = r;
                    }
                    else if (rarityObj is string rStr)
                    {
                        if (Enum.TryParse<TokenRarity>(rStr, ignoreCase: true, out var parsedRarity))
                        {
                            rarity = parsedRarity;
                        }
                        else
                        {
                            failureReason = $"Invalid Rarity string: '{rStr}'.";
                            return false;
                        }
                    }
                    else if (rarityObj is int or long or short or byte)
                    {
                        int rInt = Convert.ToInt32(rarityObj, CultureInfo.InvariantCulture);
                        if (Enum.IsDefined(typeof(TokenRarity), (TokenRarity)rInt))
                        {
                            rarity = (TokenRarity)rInt;
                        }
                        else
                        {
                            failureReason = $"Invalid Rarity integer value: {rInt}.";
                            return false;
                        }
                    }
                    else
                    {
                        failureReason = $"Unsupported Rarity property type: {rarityObj.GetType().Name}.";
                        return false;
                    }
                }

                // Parse EstimatedValue
                object? valObj = null;
                if (raw.Properties.TryGetValue("EstimatedValue", out var ev))
                {
                    valObj = ev;
                }
                else if (raw.Properties.TryGetValue("Value", out var v))
                {
                    valObj = v;
                }

                if (valObj != null)
                {
                    if (valObj is float fVal)
                    {
                        estimatedValue = fVal;
                    }
                    else if (valObj is double dVal)
                    {
                        estimatedValue = (float)dVal;
                    }
                    else if (valObj is int or long or short or byte or decimal)
                    {
                        estimatedValue = Convert.ToSingle(valObj, CultureInfo.InvariantCulture);
                    }
                    else if (valObj is string valStr)
                    {
                        if (float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedVal))
                        {
                            estimatedValue = parsedVal;
                        }
                        else
                        {
                            failureReason = $"Invalid EstimatedValue string: '{valStr}'.";
                            return false;
                        }
                    }
                    else
                    {
                        failureReason = $"Unsupported EstimatedValue property type: {valObj.GetType().Name}.";
                        return false;
                    }

                    if (float.IsNaN(estimatedValue) || float.IsInfinity(estimatedValue))
                    {
                        failureReason = $"EstimatedValue is non-finite: {estimatedValue}.";
                        return false;
                    }

                    if (estimatedValue < 0f)
                    {
                        failureReason = $"EstimatedValue cannot be negative: {estimatedValue}.";
                        return false;
                    }
                }

                // Parse IsCollected
                if (raw.Properties.TryGetValue("IsCollected", out var colObj) && colObj != null)
                {
                    if (colObj is bool bCol)
                    {
                        isCollected = bCol;
                    }
                    else if (colObj is string bStr && bool.TryParse(bStr, out bool parsedBool))
                    {
                        isCollected = parsedBool;
                    }
                }
            }

            token = new TokenLocation
            {
                Id = raw.Id.Trim(),
                Position = raw.Position,
                Rarity = rarity,
                EstimatedValue = estimatedValue,
                IsCollected = isCollected
            };

            failureReason = null;
            return true;
        }

        public IReadOnlyList<TokenLocation> ParseMany(IEnumerable<RawMapObject> rawObjects)
        {
            if (rawObjects == null) return Array.Empty<TokenLocation>();

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var results = new List<TokenLocation>();

            foreach (var raw in rawObjects)
            {
                if (TryParseToken(raw, out var token, out _) && token != null)
                {
                    if (seenIds.Add(token.Id))
                    {
                        results.Add(token);
                    }
                }
            }

            return results;
        }
    }
}
