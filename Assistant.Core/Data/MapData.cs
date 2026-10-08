namespace Assistant.Core.Data
{
    public class MapData
    {
        public string MapId { get; set; } = string.Empty;
        public MapBounds Bounds { get; set; } = new MapBounds();

        public float Width => Bounds.Max.X - Bounds.Min.X;
        public float Height => Bounds.Max.Z - Bounds.Min.Z;

        public MapData() { }

        public MapData(string mapId, MapBounds bounds)
        {
            MapId = mapId;
            Bounds = bounds;
        }

        public override string ToString() => $"Map[{MapId}, Size={Width:F1}x{Height:F1}, {Bounds}]";
    }
}
