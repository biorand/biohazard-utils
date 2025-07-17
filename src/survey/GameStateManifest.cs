using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace survey
{
    public class GameStateManifest
    {
        public ProcessInfo Process { get; set; } = new();
        public PositionRecorderInfo PositionRecorder { get; set; } = new();
        public ImmutableArray<Variable> Variables { get; set; } = [];
        public ImmutableArray<FlagGroup> FlagGroups { get; set; } = [];
        public ImmutableDictionary<string, string> FlagNames { get; set; } = ImmutableDictionary<string, string>.Empty;

        public static GameStateManifest FromJson(string json)
        {
            return JsonSerializer.Deserialize<GameStateManifest>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                ReadCommentHandling = JsonCommentHandling.Skip,
                Converters = { new HexJsonConverter() }
            }) ?? throw new JsonException("Failed to deserialize GameStateManifest from JSON.");
        }

        public class ProcessInfo
        {
            public string Name { get; set; } = "";
        }

        public class PositionRecorderInfo
        {
            public int Key { get; set; }
            public int Distance { get; set; }
        }

        public class Variable
        {
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            public string? Display { get; set; }
            public int Offset { get; set; }
        }

        public class FlagGroup
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int Offset { get; set; }
            public int Count { get; set; }
        }
    }

    public class HexJsonConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var sz = reader.GetString() ?? "";
                if (sz.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return int.Parse(sz[2..], NumberStyles.HexNumber);
                }
                throw new JsonException("Invalid format for integer");
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetInt32();
            }
            else
            {
                throw new JsonException($"Unexpected token type: {reader.TokenType} for integer conversion.");
            }
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
