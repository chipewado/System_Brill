using System.Collections.Generic;
using Assistant.Core.Data;

namespace Assistant.Core.Abstractions
{
    public interface IMapProvider
    {
        MapData GetMap();
        IEnumerable<RawMapObject> GetObjects(Region region);
    }
}
