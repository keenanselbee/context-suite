using System.Globalization;
using ContextSuite.Core.Audio;

namespace ContextSuite.Core.Analysis;

public static class AudioAnalysis
{
    public static bool CanProbe(FileAnalysis header, ReadOnlySpan<byte> bytes)
    {
        return bytes.Length > 0 && (header.Identity.FormatId is "wave" or "flac" or "ogg" or "mp3" or "m4a" ||
            bytes.StartsWith("ID3"u8) || (bytes.Length >= 2 && bytes[0] == 255 && (bytes[1] & 224) == 224) ||
            (bytes.Length >= 12 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8)));
    }

    public static FileAnalysis AddProbe(FileAnalysis header, AudioProbeFacts probe, int inspectedBytes)
    {
        probe.Validate();
        if (inspectedBytes < header.InspectedBytes || inspectedBytes > header.FileBytes)
            throw new InvalidDataException("Invalid audio analysis byte count.");
        var facts = header.Facts.ToBuilder();
        facts.Add(new("audio.probe.bytes", "Audio probe", "Bytes supplied to audio probe", Integer: inspectedBytes));
        facts.Add(new("audio.probe.container", "Audio probe", "Reported container", Text: probe.Container, Availability: FactAvailability.Derived));
        facts.Add(new("audio.probe.duration", "Audio probe", "Reported duration (seconds)",
            Text: probe.ReportedDurationSeconds?.ToString(CultureInfo.InvariantCulture),
            Availability: probe.ReportedDurationSeconds is null ? FactAvailability.Unavailable : FactAvailability.Derived));
        facts.Add(new("audio.probe.tags", "Audio probe", "Reported container tag fields", Integer: probe.Tags.Count, Availability: FactAvailability.Derived));
        foreach (var stream in probe.Streams)
        {
            var group = $"Stream {stream.Index}: {(stream.AttachedPicture ? "embedded artwork" : stream.Kind)}";
            var id = "audio.probe.stream." + stream.Index.ToString(CultureInfo.InvariantCulture);
            facts.Add(new(id + ".codec", group, "Reported codec", Text: stream.Codec, Availability: FactAvailability.Derived));
            if (stream.AttachedPicture)
            {
                facts.Add(new(id + ".artwork", group, "Reported role", Text: "Embedded artwork", Availability: FactAvailability.Derived));
                AddNumber("tags", "Reported artwork tag fields", stream.Tags.Count);
                continue;
            }
            AddNumber("rate", "Reported sample rate (Hz)", stream.SampleRate);
            AddNumber("channels", "Reported channels", stream.Channels);
            AddNumber("precision", "Reported sample precision (bits)", stream.SampleBits);
            AddNumber("bitrate", "Reported bitrate (bits/s)", stream.BitRate);
            facts.Add(new(id + ".layout", group, "Reported channel layout", Text: stream.ChannelLayout,
                Availability: stream.ChannelLayout is null ? FactAvailability.Unavailable : FactAvailability.Derived));
            facts.Add(new(id + ".duration", group, "Reported duration (seconds)",
                Text: stream.ReportedDurationSeconds?.ToString(CultureInfo.InvariantCulture),
                Availability: stream.ReportedDurationSeconds is null ? FactAvailability.Unavailable : FactAvailability.Derived));
            AddNumber("tags", "Reported stream tag fields", stream.Tags.Count);
            void AddNumber(string key, string label, long? value)
            {
                facts.Add(new(id + "." + key, group, label, Integer: value,
                    Availability: value is null ? FactAvailability.Unavailable : FactAvailability.Derived));
            }
        }
        var evidence = header.Identity.Evidence.Add($"Additional audio probing used {inspectedBytes:N0} bytes with fixed resource limits. Reported properties are not full-file or decoded-sample validation.");
        var identity = header.Identity with { Evidence = evidence };
        var warnings = header.Warnings;
        var detectedId = probe.Container switch { "wav" => "wave", "flac" => "flac", "mp3" => "mp3", "ogg" => "ogg", _ => null };
        if (detectedId is not null)
        {
            var type = FileTypeCatalog.Default.Get(detectedId);
            identity = new(type.Id, type.Name, type.Family, type.CommonUses, IdentificationConfidence.Likely, evidence, IdentificationBasis.Content);
            warnings = warnings.Remove(HeaderAnalyzer.FilenameOnlyWarning);
            if (!header.FilenameHints.IsDefaultOrEmpty && header.FilenameHints.All(hint => hint.Id != detectedId))
                warnings = warnings.Add("The audio probe reports a different type from the filename hint.");
        }
        return header with { Identity = identity, Facts = facts.ToImmutable(), Warnings = warnings, InspectedBytes = inspectedBytes };
    }
}
