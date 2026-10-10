using System;

namespace Assistant.Core.Data
{
    /// <summary>
    /// Global configuration model for HUD indicators, minimap viewport, and tracers.
    /// MinimapZoom serves as the single source of truth for minimap scaling.
    /// </summary>
    public class OverlayConfig
    {
        public float TracerMaxDistance { get; set; } = 300f;
        public int MaxTracerCount { get; set; } = 10;
        public int MaxVisibleMarkers { get; set; } = 50;
        public bool ShowActiveModes { get; set; } = true;
        public bool ShowPlayerCoordinates { get; set; } = true;
        public string CoordinateFormat { get; set; } = "X: {0:F1}  Z: {1:F1}";

        /// <summary>
        /// Single source of truth for user-facing minimap zoom factor (default 1.0f).
        /// WorldUnitsPerPixel = BaseWorldUnitsPerPixel / MinimapZoom.
        /// </summary>
        public float MinimapZoom { get; set; } = 1.0f;

        /// <summary>
        /// Creates a MinimapViewport using this config's MinimapZoom.
        /// </summary>
        public MinimapViewport CreateViewport(
            Vec2 centerWorld,
            float headingDegrees = 0f,
            float baseWorldUnitsPerPixel = 0.5f,
            float widthPx = 200f,
            float heightPx = 200f,
            float edgePaddingPx = 4f,
            bool rotateWithPlayer = true)
        {
            return MinimapViewport.CreateFromConfig(this, centerWorld, headingDegrees, baseWorldUnitsPerPixel, widthPx, heightPx, edgePaddingPx, rotateWithPlayer);
        }
    }
}
