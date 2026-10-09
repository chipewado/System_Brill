using System;
using Assistant.Core.Data;

namespace Assistant.Sim
{
    /// <summary>
    /// Simulation helper raising region-specific MapChanged events on SimGameApi.
    /// </summary>
    public class SimMapChangeNotifier
    {
        private readonly SimGameApi _api;

        public SimMapChangeNotifier(SimGameApi api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public void NotifyEntireMapChanged(string reason = "Entire map invalidated")
        {
            _api.TriggerMapChanged(null, reason);
        }

        public void NotifyRegionChanged(Region region, string reason = "Sub-region invalidated")
        {
            if (region == null) throw new ArgumentNullException(nameof(region));
            _api.TriggerMapChanged(region, reason);
        }
    }
}
