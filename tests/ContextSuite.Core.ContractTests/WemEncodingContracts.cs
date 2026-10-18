using System.Collections.Immutable;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Core.Transport;

internal static class WemEncodingContracts
{
    public static void Run(string scratch, Action<bool, string> check)
    {
        var installation = new WwiseInstallation(Path.Combine(scratch, "Wwise", "Authoring", "x64", "Release", "bin", "WwiseConsole.exe"),
            new Version(2023, 1, 10, 8659), new string('A', 64));
        var tags = ImmutableDictionary<string, string>.Empty;
        var facts = new AudioProbeFacts("wav", 1, [new(0, "audio", "pcm_s16le", 48000, 1, 16, 768000, 1, "mono", tags)], tags);
        var source = new AudioFileSource(Guid.NewGuid(), Path.Combine(scratch, "original.wav"), new string('B', 64), 96044, facts);
        var reference = new WemReference(Path.Combine(scratch, "original.wem"), new string('C', 64), 1024, WemCodec.Vorbis, 48000, 1);
        foreach (var codec in new[] { WemCodec.Pcm16, WemCodec.Vorbis })
        {
            var options = new WemEncodingOptions(installation, codec);
            var plan = AudioConversionPlan.Create(facts, AudioFormat.Wem, options);
            check(plan.Extension == ".wem" && plan.SampleRate == 48000 && plan.Channels == 1 && !plan.AlreadyTarget &&
                plan.RequiresExactSamples == (codec == WemCodec.Pcm16) && plan.RequiredConsent == AudioConversionConsent.None,
                "WEM plan: explicit codec preserves source rate/channels " + codec);
            var batch = AudioConversionBatch.Create(Guid.NewGuid(), [source], AudioFormat.Wem, new("convert", new()), wem: options);
            var confirmed = batch.Confirm(0, false, false);
            check(confirmed.Plan.Wem == options && confirmed.Plan.HasExecutableItems, "WEM plan: immutable options survive batch confirmation");
            var work = new AudioConversionWork(source, Path.Combine(scratch, $".context-suite-{source.ItemId:N}.tmp"), AudioFormat.Wem, 0, Wem: options);
            var command = new WorkerCommand(1, Guid.NewGuid(), "audio-convert", AudioWork: work);
            var copy = JsonSerializer.Deserialize<WorkerCommand>(JsonSerializer.Serialize(command))!;
            copy.Validate();
            check(copy.AudioWork!.Wem == options, "WEM transport: installation identity and codec survive JSON");
            Reject(() => (batch with { Wem = options with { Codec = codec == WemCodec.Pcm16 ? WemCodec.Vorbis : WemCodec.Pcm16 } }).Confirm(0, false, false),
                "changing the codec invalidates the planned batch");
        }
        var matched = new WemEncodingOptions(installation, WemCodec.Vorbis, reference);
        check(AudioConversionPlan.Create(facts, AudioFormat.Wem, matched).Notices.Single().Contains("game compatibility"),
            "WEM plan: reference match does not claim encoder or game compatibility");
        var mismatch = AudioConversionBatch.Create(Guid.NewGuid(), [source], AudioFormat.Wem, new("convert", new()),
            wem: matched with { Reference = reference with { SampleRate = 44100 } });
        check(!mismatch.HasExecutableItems && mismatch.Items[0].BlockReason!.Contains("differs"),
            "WEM plan: differing reference rate refuses implicit resampling");
        foreach (var invalid in new[] {
            matched with { Reference = reference with { Channels = 2 } },
            matched with { Reference = reference with { Codec = WemCodec.Pcm16 } },
            matched with { Codec = WemCodec.Unsupported },
            matched with { Installation = installation with { ExecutableSha256 = "bad" } } })
            Reject(() => AudioConversionPlan.Create(facts, AudioFormat.Wem, invalid), "invalid or contradictory encoding snapshot");
        Reject(() => AudioConversionPlan.Create(facts, AudioFormat.Wem), "WEM requires a deliberate setup choice");
        Reject(() => AudioConversionPlan.Create(facts, AudioFormat.Flac, matched), "WEM options cannot be hidden in another target");
        Reject(() => AudioConversionPlan.Create(facts with { Container = "flac" }, AudioFormat.Wem, matched), "initial WEM input is WAV only");
        Reject(() => AudioConversionPlan.Create(facts with { Streams = [facts.Streams[0] with { SampleBits = 24, Codec = "pcm_s24le" }] }, AudioFormat.Wem, matched),
            "higher PCM precision is not silently reduced");
        Reject(() => AudioConversionPlan.Create(facts with { Tags = tags.Add("title", "Must survive") }, AudioFormat.Wem, matched),
            "WAV tags need an explicit preservation route");

        foreach (var rate in new[] { 44100, 48000 })
        foreach (var channels in new[] { 1, 2 })
        {
            var floating = facts with { Streams = [facts.Streams[0] with
                { Codec = "pcm_f32le", SampleBits = 32, SampleRate = rate, Channels = channels,
                  ChannelLayout = channels == 1 ? "mono" : "stereo" }] };
            var mp3 = floating with { Container = "mp3", Streams = [floating.Streams[0] with
                { Codec = "mp3", SampleBits = null }] };
            foreach (var input in new[] { floating, mp3 })
            foreach (var codec in new[] { WemCodec.Pcm16, WemCodec.Vorbis })
            {
                var expected = AudioConversionConsent.PrecisionReduction |
                    (input.Container == "mp3" && codec == WemCodec.Vorbis ? AudioConversionConsent.LossyTranscoding : 0);
                var plan = AudioConversionPlan.Create(input, AudioFormat.Wem, new(installation, codec));
                check(plan.SampleRate == rate && plan.Channels == channels && plan.RequiredConsent == expected &&
                    plan.Notices.Any(notice => notice.Contains("16-bit", StringComparison.OrdinalIgnoreCase)) &&
                    !plan.Notices.Any(notice => notice.Contains("FLAC", StringComparison.OrdinalIgnoreCase)),
                    $"WEM plan: {input.Container}/{codec} {rate} Hz {channels}ch requires exact 16-bit normalization consent");
                var item = source with { ItemId = Guid.NewGuid(), Path = Path.Combine(scratch, $"input-{Guid.NewGuid():N}.{input.Container}"), Facts = input };
                var batch = AudioConversionBatch.Create(Guid.NewGuid(), [item], AudioFormat.Wem, new("convert", new()), wem: new(installation, codec));
                Reject(() => batch.Confirm(AudioConversionConsent.None, false, false),
                    $"{input.Container}/{codec} refuses absent normalization consent");
                if (expected.HasFlag(AudioConversionConsent.LossyTranscoding))
                    Reject(() => batch.Confirm(AudioConversionConsent.PrecisionReduction, false, false),
                        "MP3 to Vorbis refuses missing lossy-transcoding consent");
                check(batch.Confirm(expected, false, false).AcceptedConsent == expected,
                    $"WEM plan: {input.Container}/{codec} accepts explicit required consent");
            }
        }
        var floatFacts = facts with { Streams = [facts.Streams[0] with { Codec = "pcm_f32le", SampleBits = 32 }] };
        var mp3Facts = facts with { Container = "mp3", Streams = [facts.Streams[0] with { Codec = "mp3", SampleBits = null }] };
        var mixed = AudioConversionBatch.Create(Guid.NewGuid(),
            [source with { ItemId = Guid.NewGuid(), Facts = floatFacts },
             source with { ItemId = Guid.NewGuid(), Path = Path.Combine(scratch, "song.mp3"), Facts = mp3Facts }],
            AudioFormat.Wem, new("convert", new()), wem: new(installation, WemCodec.Vorbis));
        check(mixed.RequiredConsent == (AudioConversionConsent.PrecisionReduction | AudioConversionConsent.LossyTranscoding) &&
            mixed.Confirm(mixed.RequiredConsent, false, false).Plan.Items.Length == 2,
            "WEM plan: mixed float WAV/MP3 batch has one combined quality decision");
        using (var decision = new AudioConversionViewModel(mixed, new NoAdmissionAccess(), false))
            check(decision.Heading == "Convert to WEM" && decision.QualityChanges.Contains("16-bit") &&
                decision.QualityChanges.Contains("quality loss") && !decision.QualityChanges.Contains("FLAC"),
                "WEM decision: quality explanation names 16-bit WEM conversion rather than FLAC");
        foreach (var invalid in new[] {
            floatFacts with { Container = "aiff" },
            floatFacts with { Streams = [floatFacts.Streams[0] with { Codec = "pcm_f64le", SampleBits = 64 }] },
            floatFacts with { Streams = [floatFacts.Streams[0] with { Codec = "pcm_s24le", SampleBits = 24 }] },
            floatFacts with { Streams = [floatFacts.Streams[0] with { SampleRate = 32000 }] },
            floatFacts with { Streams = [floatFacts.Streams[0] with { Channels = 3, ChannelLayout = "2.1" }] },
            floatFacts with { Tags = tags.Add("artist", "Must survive") },
            floatFacts with { Streams = [floatFacts.Streams[0] with { Tags = tags.Add("artist", "Must survive") }] },
            mp3Facts with { Container = "wav" },
            mp3Facts with { Streams = [mp3Facts.Streams[0] with { Codec = "aac" }] },
            mp3Facts with { Streams = [mp3Facts.Streams[0], new(1, "video", "mjpeg", null, null, null, null, null, null, tags, true)] }
        })
            Reject(() => AudioConversionPlan.Create(invalid, AudioFormat.Wem, new(installation, WemCodec.Pcm16)),
                "unsupported WEM source, format, stream or metadata is refused");

        void Reject(Action action, string message)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or ArgumentException or InvalidOperationException)
            { check(true, "WEM plan: " + message); return; }
            check(false, "WEM plan accepted: " + message);
        }
    }

    private sealed class NoAdmissionAccess : IOperationAccess
    {
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) =>
            Task.FromResult(new OperationAccessStatus(true, "Synthetic access"));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) =>
            throw new InvalidOperationException("No admission during quality-copy inspection.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) =>
            throw new InvalidOperationException("No admission during quality-copy inspection.");
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedAudioConversion confirmed, CancellationToken token) =>
            throw new InvalidOperationException("No admission during quality-copy inspection.");
    }
}
