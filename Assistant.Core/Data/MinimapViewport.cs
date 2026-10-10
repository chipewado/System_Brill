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

        /// <summary>
        /// Determines whether all geometric parameters and dimensions are strictly valid and finite.
        /// Useful for UI guard assertions prior to transformation calls.
        /// </summary>
        public bool IsValid =>
            ViewportWidthPx > 0f && !float.IsNaN(ViewportWidthPx) && !float.IsInfinity(ViewportWidthPx) &&
            ViewportHeightPx > 0f && !float.IsNaN(ViewportHeightPx) && !float.IsInfinity(ViewportHeightPx) &&
            WorldUnitsPerPixel > 0f && !float.IsNaN(WorldUnitsPerPixel) && !float.IsInfinity(WorldUnitsPerPixel) &&
            EdgePaddingPx >= 0f && !float.IsNaN(EdgePaddingPx) && !float.IsInfinity(EdgePaddingPx) &&
            EdgePaddingPx < (ViewportWidthPx * 0.5f) && EdgePaddingPx < (ViewportHeightPx * 0.5f) &&
            !float.IsNaN(HeadingDegrees) && !float.IsInfinity(HeadingDegrees) &&
            !float.IsNaN(CenterWorld.X) && !float.IsInfinity(CenterWorld.X) &&
            !float.IsNaN(CenterWorld.Y) && !float.IsInfinity(CenterWorld.Y);

        /// <summary>
        /// Factory creating a MinimapViewport from an OverlayConfig, deriving WorldUnitsPerPixel = Base / MinimapZoom.
        /// </summary>
        public static MinimapViewport CreateFromConfig(
            OverlayConfig config,
            Vec2 centerWorld,
            float headingDegrees = 0f,
            float baseWorldUnitsPerPixel = 0.5f,
            float widthPx = 200f,
            float heightPx = 200f,
            float edgePaddingPx = 4f,
            bool rotateWithPlayer = true)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.MinimapZoom <= 0f || float.IsNaN(config.MinimapZoom) || float.IsInfinity(config.MinimapZoom))
            {
                throw new ArgumentException("MinimapZoom must be positive and finite.", nameof(config));
            }

            return new MinimapViewport
            {
                CenterWorld = centerWorld,
                ViewportWidthPx = widthPx,
                ViewportHeightPx = heightPx,
                WorldUnitsPerPixel = baseWorldUnitsPerPixel / config.MinimapZoom,
                HeadingDegrees = headingDegrees,
                RotateWithPlayer = rotateWithPlayer,
                EdgePaddingPx = edgePaddingPx
            };
        }
    }
}
