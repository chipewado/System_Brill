using System;
using System.Collections.Generic;
using System.Linq;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;

namespace Assistant.Core.Visualization
{
    /// <summary>
    /// Evaluates active display modes, deterministic dominant mode precedence, marker caps, and tracer selection.
    /// Thread-safe: internal state protected by locks; events raised outside locks with subscriber exception isolation.
    /// </summary>
    public class DisplayModeManager : IDisplayModeManager
    {
        public const string DisplayModesConfigKey = "display-modes";
        public const string OverlayConfigKey = "overlay-config";

        private readonly object _syncLock = new object();
        private readonly List<DisplayMode> _modes = new List<DisplayMode>();
        private OverlayConfig _config = new OverlayConfig();

        public event Action? ModesChanged;
        public event Action? ConfigChanged;

        public DisplayModeManager(IEnumerable<DisplayMode>? initialModes = null, OverlayConfig? initialConfig = null)
        {
            if (initialModes != null)
            {
                foreach (var mode in initialModes)
                {
                    if (mode == null) continue;
                    if (_modes.Any(m => string.Equals(m.Id, mode.Id, StringComparison.Ordinal)))
                    {
                        throw new ArgumentException($"Duplicate mode Id '{mode.Id}' in initial modes.", nameof(initialModes));
                    }
                    _modes.Add(mode.Clone());
                }
            }

            if (_modes.Count == 0)
            {
                _modes.Add(BuiltInTemplates.CreateAllTokens());
            }

            if (initialConfig != null)
            {
                _config = initialConfig.Clone();
            }
        }

        public IReadOnlyList<DisplayMode> Modes
        {
            get
            {
                lock (_syncLock)
                {
                    return _modes.Select(m => m.Clone()).ToList();
                }
            }
        }

        public OverlayConfig Config
        {
            get
            {
                lock (_syncLock)
                {
                    return _config.Clone();
                }
            }
        }

        public void AddMode(DisplayMode mode)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));

            lock (_syncLock)
            {
                if (_modes.Any(m => string.Equals(m.Id, mode.Id, StringComparison.Ordinal)))
                {
                    throw new ArgumentException($"DisplayMode with Id '{mode.Id}' already exists.", nameof(mode));
                }

                _modes.Add(mode.Clone());
            }

            RaiseModesChanged();
        }

        public bool UpdateMode(DisplayMode mode)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            bool updated = false;

            lock (_syncLock)
            {
                int index = _modes.FindIndex(m => string.Equals(m.Id, mode.Id, StringComparison.Ordinal));
                if (index >= 0)
                {
                    _modes[index] = mode.Clone();
                    updated = true;
                }
            }

            if (updated)
            {
                RaiseModesChanged();
            }
            return updated;
        }

        public bool RemoveMode(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            bool removed = false;

            lock (_syncLock)
            {
                int index = _modes.FindIndex(m => string.Equals(m.Id, id, StringComparison.Ordinal));
                if (index >= 0)
                {
                    _modes.RemoveAt(index);
                    removed = true;
                }
            }

            if (removed)
            {
                RaiseModesChanged();
            }
            return removed;
        }

        public bool SetModeEnabled(string id, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            bool found = false;

            lock (_syncLock)
            {
                int index = _modes.FindIndex(m => string.Equals(m.Id, id, StringComparison.Ordinal));
                if (index >= 0)
                {
                    if (_modes[index].Enabled != enabled)
                    {
                        _modes[index].Enabled = enabled;
                        found = true;
                    }
                    else
                    {
                        return true;
                    }
                }
            }

            if (found)
            {
                RaiseModesChanged();
            }
            return found;
        }

        public void ToggleModeByKey(string keyBinding)
        {
            if (string.IsNullOrWhiteSpace(keyBinding)) return;
            bool changed = false;

            lock (_syncLock)
            {
                foreach (var mode in _modes)
                {
                    if (string.Equals(mode.KeyBinding, keyBinding, StringComparison.OrdinalIgnoreCase))
                    {
                        mode.Enabled = !mode.Enabled;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                RaiseModesChanged();
            }
        }

        public DisplayMode? GetDominantMode(TokenLocation token, Vec3 playerPosition)
        {
            if (token == null) return null;

            lock (_syncLock)
            {
                return SelectDominantModeInternal(token, playerPosition, _modes);
            }
        }

        public IReadOnlyList<TokenLocation> FilterTokens(IEnumerable<TokenLocation> tokens, Vec3 playerPosition)
        {
            if (tokens == null) return Array.Empty<TokenLocation>();

            int maxVisible;
            List<DisplayMode> modesSnapshot;

            lock (_syncLock)
            {
                maxVisible = _config.MaxVisibleMarkers;
                if (maxVisible <= 0) return Array.Empty<TokenLocation>();
                modesSnapshot = _modes.Select(m => m.Clone()).ToList();
            }

            var matchingTokens = new List<TokenLocation>();
            foreach (var token in tokens)
            {
                if (token == null) continue;
                var dominant = SelectDominantModeInternal(token, playerPosition, modesSnapshot);
                if (dominant != null)
                {
                    matchingTokens.Add(token);
                }
            }

            if (matchingTokens.Count == 0) return Array.Empty<TokenLocation>();

            // Ordering: Rarity desc, EstimatedValue desc, Distance asc, Id asc (Ordinal)
            var ordered = matchingTokens
                .OrderByDescending(t => t.Rarity)
                .ThenByDescending(t => t.EstimatedValue)
                .ThenBy(t => Vec3.Distance2D(playerPosition, t.Position))
                .ThenBy(t => t.Id, StringComparer.Ordinal)
                .Take(maxVisible)
                .ToList();

            return ordered;
        }

        public IReadOnlyList<TokenLocation> GetTracerTokens(IEnumerable<TokenLocation> tokens, Vec3 playerPosition)
        {
            if (tokens == null) return Array.Empty<TokenLocation>();

            int maxTracers;
            float tracerMaxDistance;
            List<DisplayMode> modesSnapshot;

            lock (_syncLock)
            {
                maxTracers = _config.MaxTracerCount;
                tracerMaxDistance = _config.TracerMaxDistance;
                if (maxTracers <= 0) return Array.Empty<TokenLocation>();
                modesSnapshot = _modes.Select(m => m.Clone()).ToList();
            }

            var tracerCandidates = new List<TokenLocation>();
            foreach (var token in tokens)
            {
                if (token == null) continue;
                var dominant = SelectDominantModeInternal(token, playerPosition, modesSnapshot);
                if (dominant != null && dominant.ShowTracer)
                {
                    float dist = Vec3.Distance2D(playerPosition, token.Position);
                    if (dist <= tracerMaxDistance)
                    {
                        tracerCandidates.Add(token);
                    }
                }
            }

            if (tracerCandidates.Count == 0) return Array.Empty<TokenLocation>();

            // Identical ordering: Rarity desc, EstimatedValue desc, Distance asc, Id asc (Ordinal)
            var ordered = tracerCandidates
                .OrderByDescending(t => t.Rarity)
                .ThenByDescending(t => t.EstimatedValue)
                .ThenBy(t => Vec3.Distance2D(playerPosition, t.Position))
                .ThenBy(t => t.Id, StringComparer.Ordinal)
                .Take(maxTracers)
                .ToList();

            return ordered;
        }

        public void ApplyTemplate(string templateName)
        {
            var templateModes = BuiltInTemplates.GetTemplateModes(templateName);

            lock (_syncLock)
            {
                _modes.Clear();
                foreach (var mode in templateModes)
                {
                    _modes.Add(mode.Clone());
                }
            }

            RaiseModesChanged();
        }

        public void UpdateConfig(OverlayConfig newConfig)
        {
            if (newConfig == null) throw new ArgumentNullException(nameof(newConfig));

            lock (_syncLock)
            {
                _config = newConfig.Clone();
            }

            RaiseConfigChanged();
        }

        public void Save(IConfigStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));

            List<DisplayMode> modesCopy;
            OverlayConfig configCopy;

            lock (_syncLock)
            {
                modesCopy = _modes.Select(m => m.Clone()).ToList();
                configCopy = _config.Clone();
            }

            store.Save(DisplayModesConfigKey, modesCopy);
            store.Save(OverlayConfigKey, configCopy);
        }

        public void Load(IConfigStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));

            List<DisplayMode>? loadedModes = null;
            OverlayConfig? loadedConfig = null;

            try
            {
                loadedModes = store.Load<List<DisplayMode>>(DisplayModesConfigKey);
            }
            catch
            {
                // Never throw on corrupt or failing store
            }

            try
            {
                loadedConfig = store.Load<OverlayConfig>(OverlayConfigKey);
            }
            catch
            {
                // Never throw on corrupt or failing store
            }

            bool modesChanged = false;
            bool configChanged = false;

            lock (_syncLock)
            {
                if (loadedModes != null && loadedModes.Count > 0)
                {
                    var validModes = new List<DisplayMode>();
                    var seenIds = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var m in loadedModes)
                    {
                        if (m == null || string.IsNullOrWhiteSpace(m.Id)) continue;
                        if (seenIds.Add(m.Id))
                        {
                            // Validate and clamp MaxDistance if invalid
                            if (m.MaxDistance.HasValue && (m.MaxDistance.Value < 0f || float.IsNaN(m.MaxDistance.Value) || float.IsInfinity(m.MaxDistance.Value)))
                            {
                                m.MaxDistance = null;
                            }
                            if (float.IsNaN(m.MinValue) || float.IsInfinity(m.MinValue) || m.MinValue < 0f)
                            {
                                m.MinValue = 0f;
                            }
                            validModes.Add(m.Clone());
                        }
                    }

                    if (validModes.Count > 0)
                    {
                        _modes.Clear();
                        _modes.AddRange(validModes);
                        modesChanged = true;
                    }
                    else
                    {
                        _modes.Clear();
                        _modes.Add(BuiltInTemplates.CreateAllTokens());
                        modesChanged = true;
                    }
                }
                else
                {
                    _modes.Clear();
                    _modes.Add(BuiltInTemplates.CreateAllTokens());
                    modesChanged = true;
                }

                if (loadedConfig != null)
                {
                    // Validate and clamp config fields
                    if (loadedConfig.MaxVisibleMarkers < 0) loadedConfig.MaxVisibleMarkers = 0;
                    if (loadedConfig.MaxTracerCount < 0) loadedConfig.MaxTracerCount = 0;
                    if (loadedConfig.TracerMaxDistance <= 0f || float.IsNaN(loadedConfig.TracerMaxDistance) || float.IsInfinity(loadedConfig.TracerMaxDistance))
                    {
                        loadedConfig.TracerMaxDistance = 300f;
                    }
                    if (loadedConfig.MinimapZoom <= 0f || float.IsNaN(loadedConfig.MinimapZoom) || float.IsInfinity(loadedConfig.MinimapZoom))
                    {
                        loadedConfig.MinimapZoom = 1.0f;
                    }
                    _config = loadedConfig.Clone();
                    configChanged = true;
                }
                else
                {
                    _config = new OverlayConfig();
                    configChanged = true;
                }
            }

            if (modesChanged) RaiseModesChanged();
            if (configChanged) RaiseConfigChanged();
        }

        private static DisplayMode? SelectDominantModeInternal(TokenLocation token, Vec3 playerPosition, List<DisplayMode> modes)
        {
            DisplayMode? dominant = null;
            int dominantIndex = -1;

            for (int i = 0; i < modes.Count; i++)
            {
                var candidate = modes[i];
                if (!candidate.Matches(token, playerPosition)) continue;

                if (dominant == null)
                {
                    dominant = candidate;
                    dominantIndex = i;
                    continue;
                }

                // 1. Highest Priority
                if (candidate.Priority > dominant.Priority)
                {
                    dominant = candidate;
                    dominantIndex = i;
                }
                else if (candidate.Priority == dominant.Priority)
                {
                    // 2. Tie-break: List order in Modes (lower index wins)
                    if (i < dominantIndex)
                    {
                        dominant = candidate;
                        dominantIndex = i;
                    }
                    else if (i == dominantIndex)
                    {
                        // 3. Tie-break: Lexicographical order by DisplayMode.Id
                        if (string.Compare(candidate.Id, dominant.Id, StringComparison.Ordinal) < 0)
                        {
                            dominant = candidate;
                            dominantIndex = i;
                        }
                    }
                }
            }

            return dominant?.Clone();
        }

        private void RaiseModesChanged()
        {
            var handler = ModesChanged;
            if (handler == null) return;

            foreach (Action sub in handler.GetInvocationList())
            {
                try
                {
                    sub();
                }
                catch
                {
                    // Per-subscriber exception isolation
                }
            }
        }

        private void RaiseConfigChanged()
        {
            var handler = ConfigChanged;
            if (handler == null) return;

            foreach (Action sub in handler.GetInvocationList())
            {
                try
                {
                    sub();
                }
                catch
                {
                    // Per-subscriber exception isolation
                }
            }
        }
    }
}
