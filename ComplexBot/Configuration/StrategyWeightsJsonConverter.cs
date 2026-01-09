using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingBot.Core.Models;
using ComplexBot.Models;

namespace ComplexBot.Configuration;

public sealed class StrategyWeightsJsonConverter : JsonConverter<Dictionary<StrategyKind, decimal>>
{
    public override Dictionary<StrategyKind, decimal> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object for StrategyWeights.");
        }

        var result = new Dictionary<StrategyKind, decimal>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return result;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name when reading StrategyWeights.");
            }

            var key = reader.GetString() ?? string.Empty;

            if (!StrategyWeightKeyMapper.TryGetStrategyKind(key, out var kind))
            {
                throw new JsonException($"Unknown strategy weight key '{key}'.");
            }

            reader.Read();
            var weight = JsonSerializer.Deserialize<decimal>(ref reader, options);
            result[kind] = weight;
        }

        throw new JsonException("Unexpected end of JSON when reading StrategyWeights.");
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<StrategyKind, decimal> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var (kind, weight) in value)
        {
            writer.WritePropertyName(kind.ToString());
            writer.WriteNumberValue(weight);
        }

        writer.WriteEndObject();
    }
}
