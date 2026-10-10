using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;
using SysPath = System.IO.Path;

namespace Assistant.Persistence
{
    /// <summary>
    /// File-based JSON configuration store with atomic replace, schemaVersion envelopes, and key traversal guards.
    /// Core domain models contain no serialization attributes; mapping to/from DTOs is handled here.
    /// </summary>
    public class JsonConfigStore : IConfigStore
    {
        public const int CurrentSchemaVersion = 1;

        private readonly string _directory;
        private readonly JsonSerializerOptions _jsonOptions;

        public JsonConfigStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Directory must not be null or whitespace.", nameof(directory));
            }

            _directory = SysPath.GetFullPath(directory);
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                NumberHandling = JsonNumberHandling.Strict, // Strictly rejects NaN and Infinity
                Converters =
                {
                    new JsonStringEnumConverter(),
                    new ColorRgbaHexJsonConverter()
                }
            };
        }

        public void Save<T>(string key, T data)
        {
            ValidateKey(key);
            if (data == null) throw new ArgumentNullException(nameof(data));

            Directory.CreateDirectory(_directory);

            string targetFile = GetFilePath(key);
            string tempFile = SysPath.Combine(_directory, $"{key}.{Guid.NewGuid():N}.tmp");

            try
            {
                object payloadDto = PreparePayloadForSerialization(data);
                var envelope = new ConfigEnvelopeDto<object>
                {
                    SchemaVersion = CurrentSchemaVersion,
                    Payload = payloadDto
                };

                using (var stream = File.Create(tempFile))
                {
                    JsonSerializer.Serialize(stream, envelope, _jsonOptions);
                }

                // Atomic replacement via File.Move with overwrite: true
                File.Move(tempFile, targetFile, overwrite: true);
            }
            catch
            {
                // Clean up temporary file on failure so no stray temp files remain
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch
                {
                    // Suppress temp deletion errors during failure reporting
                }

                throw;
            }
        }

        public T? Load<T>(string key)
        {
            ValidateKey(key);
            string filePath = GetFilePath(key);

            if (!File.Exists(filePath))
            {
                return default;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return default;
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                bool hasVersion = root.TryGetProperty("schemaVersion", out var versionElement) ||
                                  root.TryGetProperty("SchemaVersion", out versionElement);

                if (!hasVersion ||
                    versionElement.ValueKind != JsonValueKind.Number ||
                    versionElement.GetInt32() != CurrentSchemaVersion)
                {
                    // Newer or unsupported schema version -> return null without throwing or overwriting
                    return default;
                }

                bool hasPayload = root.TryGetProperty("payload", out var payloadElement) ||
                                  root.TryGetProperty("Payload", out payloadElement);

                if (!hasPayload ||
                    payloadElement.ValueKind == JsonValueKind.Null ||
                    payloadElement.ValueKind == JsonValueKind.Undefined)
                {
                    return default;
                }

                return DeserializePayload<T>(payloadElement);
            }
            catch
            {
                // Malformed or unparseable JSON -> return null without throwing
                return default;
            }
        }

        public bool Exists(string key)
        {
            ValidateKey(key);
            return File.Exists(GetFilePath(key));
        }

        public bool Delete(string key)
        {
            ValidateKey(key);
            string filePath = GetFilePath(key);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
            return false;
        }

        public static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Config key must not be null or whitespace.", nameof(key));
            }

            if (key.Contains('/') || key.Contains('\\') || key.Contains(".."))
            {
                throw new ArgumentException($"Config key '{key}' contains illegal path characters or traversal sequences.", nameof(key));
            }

            char[] invalidChars = SysPath.GetInvalidFileNameChars();
            if (key.IndexOfAny(invalidChars) >= 0)
            {
                throw new ArgumentException($"Config key '{key}' contains invalid filename characters.", nameof(key));
            }
        }

        private string GetFilePath(string key) => SysPath.Combine(_directory, $"{key}.json");

        private static object PreparePayloadForSerialization<T>(T data)
        {
            if (data is IEnumerable<DisplayMode> modeList)
            {
                return modeList.Select(DisplayModeDto.FromDomain).ToList();
            }

            if (data is DisplayMode singleMode)
            {
                return DisplayModeDto.FromDomain(singleMode);
            }

            if (data is OverlayConfig overlayConfig)
            {
                return OverlayConfigDto.FromDomain(overlayConfig);
            }

            return data!;
        }

        private T? DeserializePayload<T>(JsonElement payloadElement)
        {
            Type targetType = typeof(T);

            if (targetType == typeof(List<DisplayMode>) || targetType == typeof(IReadOnlyList<DisplayMode>) || targetType == typeof(IEnumerable<DisplayMode>))
            {
                var dtos = JsonSerializer.Deserialize<List<DisplayModeDto>>(payloadElement.GetRawText(), _jsonOptions);
                if (dtos == null) return default;
                var domainList = dtos.Select(d => d.ToDomain()).ToList();
                return (T)(object)domainList;
            }

            if (targetType == typeof(DisplayMode))
            {
                var dto = JsonSerializer.Deserialize<DisplayModeDto>(payloadElement.GetRawText(), _jsonOptions);
                if (dto == null) return default;
                return (T)(object)dto.ToDomain();
            }

            if (targetType == typeof(OverlayConfig))
            {
                var dto = JsonSerializer.Deserialize<OverlayConfigDto>(payloadElement.GetRawText(), _jsonOptions);
                if (dto == null) return default;
                return (T)(object)dto.ToDomain();
            }

            return JsonSerializer.Deserialize<T>(payloadElement.GetRawText(), _jsonOptions);
        }
    }
}
