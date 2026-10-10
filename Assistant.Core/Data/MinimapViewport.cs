using System;

namespace Assistant.Core.Data
{
    /// <summary>
    /// Geometry and configuration model for the 2D minimap / radar widget.
    /// Zoom is controlled exclusively via WorldUnitsPerPixel (derived from OverlayConfig.MinimapZoom).
    /// </summary>
    public class MinimapViewport
    {
        /// <summary>
        /// World coordinates (X = East, Y = North in 2D plane) corresponding to the minimap center (usually player position).
        /// </summary>
        public Vec2 CenterWorld { get; set; }

        /// <summary>
        /// Width of the minimap UI widget in pixels.
        /// </summary>
        public float ViewportWidthPx { get; set; } = 200f;

        /// <summary>
        /// Height of the minimap UI widget in pixels.
        /// </summary>
        public float ViewportHeightPx { get; set; } = 200f;

        /// <summary>
        /// World units represented per UI pixel.
        /// Single source of truth: BaseWorldUnitsPerPixel / OverlayConfig.MinimapZoom.
        /// </summary>
        public float WorldUnitsPerPixel { get; set; } = 0.5f;

        /// <summary>
        /// Heading angle in clockwise degrees from North (0° = North, 90° = East, 180° = South, 270° = West).
        /// </summary>
        public float HeadingDegrees { get; set; } = 0f;

        /// <summary>
        /// If true, the minimap rotates counter-clockwise so the player's heading always points UP (+Y screen).
        /// If false, North (+Z world) always points UP (+Y screen).
        /// </summary>
        public bool RotateWithPlayer { get; set; } = true;

        /// <summary>
        /// Inward padding in pixels from the widget boundary for ray clamping and off-screen markers.
        /// </summary>
        public float EdgePaddingPx { get; set; } = 4f;
    }
}
