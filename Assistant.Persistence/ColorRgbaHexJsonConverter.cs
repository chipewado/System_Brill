using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Core.Data;

namespace Assistant.Persistence
{
    /// <summary>
    /// JSON converter that serializes ColorRgba as a 32-bit hex string (#RRGGBBAA).
    /// </summary>
    public class ColorRgbaHexJsonConverter : JsonConverter<ColorRgba>
    {
        public override ColorRgba Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? hex = reader.GetString();
                return ColorRgba.FromHex(hex);
            }

            return ColorRgba.White;
        }

        public override void Write(Utf8JsonWriter writer, ColorRgba value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToHex());
        }
    }
}
