using System;

namespace Assistant.Sim.Harness
{
    /// <summary>
    /// Represents a recorded event in the simulation transcript.
    /// Event types: TargetSelected, TargetChanged, TokenCollected, MapChanged, Replan.
    /// </summary>
    public sealed class SimTranscriptEvent
    {
        public double SimTime { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? TokenId { get; set; }
        public float Value { get; set; }

        public SimTranscriptEvent() { }

        public SimTranscriptEvent(double simTime, string type, string? tokenId, float value)
        {
            SimTime = Math.Round(simTime, 3);
            Type = type;
            TokenId = tokenId;
            Value = (float)Math.Round(value, 3);
        }

        public override string ToString() =>
            $"[{SimTime:F3}s] {Type} - Token: {TokenId ?? "<none>"}, Value: {Value:F1}";
    }
}
