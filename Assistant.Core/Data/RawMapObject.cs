using System.Collections.Generic;

namespace Assistant.Core.Data
{
    public class RawMapObject
    {
        public string Id { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty;
        public Vec3 Position { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }
}
