using System.Text.Json.Serialization;

namespace Assistant.Persistence
{
    /// <summary>
    /// Versioned schema envelope wrapping persistent configuration payloads.
    /// Uses camelCase properties ("schemaVersion", "payload") to match the contract specification.
    /// </summary>
    public class ConfigEnvelopeDto<T>
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("payload")]
        public T? Payload { get; set; }
    }
}
