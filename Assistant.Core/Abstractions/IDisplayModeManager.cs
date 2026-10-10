using System;
using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Abstractions
{
    /// <summary>
    /// Presentation-only manager for evaluating active visual display modes, marker colors, and tracers.
    /// Does NOT serve as the source of truth for domain token availability or ranking.
    /// </summary>
    public interface IDisplayModeManager
    {
        /// <summary>
        /// Returns snapshot clones of all configured display modes.
        /// </summary>
        IReadOnlyList<DisplayMode> Modes { get; }

        /// <summary>
        /// Returns a snapshot clone of the active overlay configuration.
        /// </summary>
        OverlayConfig Config { get; }

        /// <summary>
        /// Adds a new display mode. Throws ArgumentException if Id already exists.
        /// </summary>
        void AddMode(DisplayMode mode);

        /// <summary>
        /// Updates an existing display mode by Id. Returns true if updated, false if not found.
        /// </summary>
        bool UpdateMode(DisplayMode mode);

        /// <summary>
        /// Removes a display mode by Id. Returns true if removed, false if not found.
        /// </summary>
        bool RemoveMode(string id);

        /// <summary>
        /// Enables or disables a display mode by Id. Returns true if found and set, false if not found.
        /// </summary>
        bool SetModeEnabled(string id, bool enabled);

        /// <summary>
        /// Toggles the enabled state of all modes matching the given key binding (case-insensitive ordinal).
        /// Unknown key is a safe no-op.
        /// </summary>
        void ToggleModeByKey(string keyBinding);

        /// <summary>
        /// Resolves the dominant matching display mode for a token based on priority, list order, and Id.
        /// Dominant mode governs both marker color and tracer presentation.
        /// </summary>
        DisplayMode? GetDominantMode(TokenLocation token, Vec3 playerPosition);

        /// <summary>
        /// Filters tokens accepted by any enabled display mode and caps to MaxVisibleMarkers.
        /// Ordered by: Rarity desc, EstimatedValue desc, Distance asc, Id asc.
        /// </summary>
        IReadOnlyList<TokenLocation> FilterTokens(IEnumerable<TokenLocation> tokens, Vec3 playerPosition);

        /// <summary>
        /// Returns tokens whose dominant mode has ShowTracer, within TracerMaxDistance, capped to MaxTracerCount.
        /// </summary>
        IReadOnlyList<TokenLocation> GetTracerTokens(IEnumerable<TokenLocation> tokens, Vec3 playerPosition);

        /// <summary>
        /// Replaces the current display mode list with the specified built-in template.
        /// Throws ArgumentException if template name is unknown.
        /// </summary>
        void ApplyTemplate(string templateName);

        /// <summary>
        /// Updates overlay settings and fires ConfigChanged.
        /// </summary>
        void UpdateConfig(OverlayConfig newConfig);

        /// <summary>
        /// Persists modes and config to the specified config store.
        /// </summary>
        void Save(IConfigStore store);

        /// <summary>
        /// Restores modes and config from the specified config store.
        /// On missing or corrupt data, restores safe defaults without throwing.
        /// </summary>
        void Load(IConfigStore store);

        /// <summary>
        /// Raised when modes are added, removed, updated, or toggled.
        /// </summary>
        event Action? ModesChanged;

        /// <summary>
        /// Raised when overlay configuration is updated.
        /// </summary>
        event Action? ConfigChanged;
    }
}
