using Assistant.Core.Data;

namespace Assistant.Persistence
{
    /// <summary>
    /// Data transfer object for OverlayConfig persistence.
    /// </summary>
    public class OverlayConfigDto
    {
        public float TracerMaxDistance { get; set; } = 300f;
        public int MaxTracerCount { get; set; } = 10;
        public int MaxVisibleMarkers { get; set; } = 50;
        public bool ShowActiveModes { get; set; } = true;
        public bool ShowPlayerCoordinates { get; set; } = true;
        public string CoordinateFormat { get; set; } = "X: {0:F1}  Z: {1:F1}";
        public float MinimapZoom { get; set; } = 1.0f;

        public static OverlayConfigDto FromDomain(OverlayConfig config)
        {
            return new OverlayConfigDto
            {
                TracerMaxDistance = config.TracerMaxDistance,
                MaxTracerCount = config.MaxTracerCount,
                MaxVisibleMarkers = config.MaxVisibleMarkers,
                ShowActiveModes = config.ShowActiveModes,
                ShowPlayerCoordinates = config.ShowPlayerCoordinates,
                CoordinateFormat = config.CoordinateFormat,
                MinimapZoom = config.MinimapZoom
            };
        }

        public OverlayConfig ToDomain()
        {
            return new OverlayConfig
            {
                TracerMaxDistance = TracerMaxDistance,
                MaxTracerCount = MaxTracerCount,
                MaxVisibleMarkers = MaxVisibleMarkers,
                ShowActiveModes = ShowActiveModes,
                ShowPlayerCoordinates = ShowPlayerCoordinates,
                CoordinateFormat = CoordinateFormat,
                MinimapZoom = MinimapZoom
            };
        }
    }
}
