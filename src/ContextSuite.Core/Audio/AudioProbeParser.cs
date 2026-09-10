using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace ContextSuite.Core.Audio;

public static class AudioProbeParser
{
    public const int MaximumJsonBytes = 1024 * 1024;

    public static AudioProbeFacts Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaximumJsonBytes) throw new InvalidDataException("Audio probe output exceeds its byte budget.");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            var format = root.GetProperty("format");
            var streams = root.GetProperty("streams");
            if (streams.ValueKind != JsonValueKind.Array || streams.GetArrayLength() > 32)
                throw new InvalidDataException("Audio probe stream count exceeds its budget.");
            var result = ImmutableArray.CreateBuilder<AudioStreamFacts>();
            var indices = new HashSet<int>();
            foreach (var stream in streams.EnumerateArray())
            {
                var index = Integer(stream, "index") ?? throw new InvalidDataException("Missing stream index.");
                if (index > int.MaxValue || !indices.Add((int)index)) throw new InvalidDataException("Invalid or duplicate stream index.");
                var rate = Integer(stream, "sample_rate");
                var channels = Integer(stream, "channels");
                var bits = Integer(stream, "bits_per_raw_sample");
                if (rate > int.MaxValue || channels > int.MaxValue || bits > 64)
                    throw new InvalidDataException("Audio probe sample declarations are invalid.");
                result.Add(new((int)index, Text(stream, "codec_type", 32) ?? "unknown", Text(stream, "codec_name", 64) ?? "unknown",
                    rate is > 0 ? (int)rate : null, channels is > 0 ? (int)channels : null, bits is > 0 ? (int)bits : null,
                    Integer(stream, "bit_rate"), Number(stream, "duration"), Text(stream, "channel_layout", 128), Tags(stream)));
            }
            var facts = new AudioProbeFacts(Text(format, "format_name", 128) ?? "unknown", Number(format, "duration"), result.ToImmutable(), Tags(format));
            facts.Validate();
            return facts;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new InvalidDataException("Malformed audio probe output.", error); }
    }

    private static ImmutableDictionary<string, string> Tags(JsonElement parent)
    {
        var result = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!parent.TryGetProperty("tags", out var tags)) return result.ToImmutable();
        foreach (var tag in tags.EnumerateObject())
        {
            if (result.Count == 128 || tag.Name.Length > 128 || tag.Value.ValueKind != JsonValueKind.String ||
                tag.Value.GetString() is not { Length: <= 4096 } value || !result.TryAdd(tag.Name, value))
                throw new InvalidDataException("Audio probe tags exceed their bounds or are ambiguous.");
        }
        return result.ToImmutable();
    }

    private static string? Text(JsonElement parent, string name, int maximum)
    {
        if (!parent.TryGetProperty(name, out var value)) return null;
        var text = value.GetString();
        if (text?.Length > maximum) throw new InvalidDataException("Audio probe text exceeds its budget.");
        return text;
    }

    private static decimal? Number(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)) return null;
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        if (text == "N/A") return null;
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) || number < 0)
            throw new InvalidDataException("Invalid audio probe numeric declaration.");
        return number;
    }

    private static long? Integer(JsonElement parent, string name)
    {
        var value = Number(parent, name);
        if (value is null) return null;
        if (value > long.MaxValue || value != decimal.Truncate(value.Value)) throw new InvalidDataException("Invalid audio probe integer.");
        return (long)value;
    }
}
