using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Audio;
using ContextSuite.Private.Audio;

internal static class VideoAudioContracts
{
    public static async Task RunAsync(string engineDirectory, string fixtureDirectory, string root,
        Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        using var adapter = new AudioFileAdapter(engineDirectory, Path.Combine(root, "engine"));
        foreach (var extension in new[] { "mp4", "mov", "webm", "mkv" })
        {
            var path = Path.Combine(fixtureDirectory, "two-tracks." + extension);
            var original = SHA256.HashData(File.ReadAllBytes(path));
            var item = Guid.NewGuid();
            var source = await adapter.ProbeVideoAsync(new(item, path), default);
            check(source.Video is null && source.Facts.Streams.Count(stream => stream.Kind == "audio") == 2 &&
                source.Facts.Streams.Any(stream => stream.Kind == "video") &&
                source.Sha256 == Convert.ToHexString(original), extension + " full video track inventory and source hash");
            var track = source.Facts.Streams.Where(stream => stream.Kind == "audio").Last();
            check(track.Index > 0 && track.SampleRate is > 0 && track.StartSeconds is > 0 &&
                track.Tags.TryGetValue("language", out var language) && language == "fra",
                extension + " second audio track binds absolute index, delayed start and language");
            var descriptions = VideoAudioMetadata.DescriptiveTags(source.Facts, track);
            check(descriptions.GetValueOrDefault("language") == "fra" &&
                descriptions.GetValueOrDefault("title") == "Second" &&
                !descriptions.ContainsKey("handler_name") && !descriptions.ContainsKey("duration"),
                extension + " selected track preserves language and name without container provenance");
            var unnamedTrack = track with { Tags = track.Tags.Remove("title").Remove("name").Remove("handler_name") };
            var movieName = source.Facts with { Tags = source.Facts.Tags.Clear().Add("name", "Movie name") };
            check(VideoAudioMetadata.DescriptiveTags(movieName, unnamedTrack).GetValueOrDefault("title") == "Movie name",
                extension + " movie name becomes the extracted audio title");
            var matchingNames = movieName with { Tags = movieName.Tags.Add("title", "Movie name") };
            check(VideoAudioMetadata.DescriptiveTags(matchingNames, unnamedTrack).GetValueOrDefault("title") == "Movie name",
                extension + " matching movie name and title are retained");
            var conflictingMovieNames = movieName with { Tags = movieName.Tags.Add("title", "Different title") };
            var refusedNames = false;
            try { _ = VideoAudioMetadata.DescriptiveTags(conflictingMovieNames, unnamedTrack); }
            catch (NotSupportedException) { refusedNames = true; }
            check(refusedNames, extension + " conflicting movie name and title are refused");
            refusedNames = false;
            try { _ = VideoAudioMetadata.DescriptiveTags(movieName,
                unnamedTrack with { Tags = unnamedTrack.Tags.Add("title", "Different track title") }); }
            catch (NotSupportedException) { refusedNames = true; }
            check(refusedNames, extension + " conflicting movie and track titles are refused");
            refusedNames = false;
            try { _ = VideoAudioMetadata.DescriptiveTags(source.Facts with { Tags = source.Facts.Tags.Clear() },
                unnamedTrack with { Tags = unnamedTrack.Tags.Add("title", "Track title").Add("name", "Different track name") }); }
            catch (NotSupportedException) { refusedNames = true; }
            check(refusedNames, extension + " conflicting track name and title are refused");
            check(VideoAudioMetadata.DescriptiveTags(source.Facts with { Tags = source.Facts.Tags.Clear() },
                unnamedTrack with { Tags = unnamedTrack.Tags.Add("name", "Track name") })
                    .GetValueOrDefault("title") == "Track name",
                extension + " selected track name remains the extracted audio title");
            check(VideoAudioMetadata.DescriptiveTags(source.Facts with { Tags = source.Facts.Tags.Clear() },
                unnamedTrack with { Tags = unnamedTrack.Tags.Add("handler_name", "Commentary handler") })
                    .GetValueOrDefault("title") == "Commentary handler",
                extension + " selected track handler fallback remains available");
            var selected = source with { Video = new VideoAudioSelection(track.Index) };
            selected.Validate();
            var selectedProbe = await adapter.ProbeConversionAsync(new(item, path), AudioFormat.Wave,
                default, selected.Video!);
            check(selectedProbe.Video == selected.Video && selectedProbe.Sha256 == source.Sha256 &&
                selectedProbe.Facts.Streams.Length == source.Facts.Streams.Length,
                extension + " selected timeline preflight retains full original inventory");
            foreach (var target in new[] { AudioFormat.Wave, AudioFormat.Mp3, AudioFormat.Flac })
            {
                var plan = AudioConversionPlan.Create(selected.Facts, target, video: selected.Video);
                var temporary = Path.Combine(root, $".context-suite-{item:N}.tmp");
                File.WriteAllBytes(temporary, []);
                var result = await adapter.ConvertAsync(new(selected, temporary, target, plan.RequiredConsent), default);
                var bytes = File.ReadAllBytes(temporary);
                // MOV supplies a sample-aligned presentation duration. Matroska
                // AAC instead retains the 45,056 decoded samples independently
                // measured by the fixture's second decoder (including padding).
                var expectedFrames = extension is "mp4" or "mov" && track.DurationTicks is { } ticks ? ticks :
                    extension == "mkv" ? 45056L : 44100L;
                check(result.Validation.MatchesPlan && result.Validation.ItemId == item &&
                    result.Validation.Sha256 == Convert.ToHexString(SHA256.HashData(bytes)) &&
                    result.SourceSha256 == selected.Sha256 && result.DecodedFrames == expectedFrames &&
                    result.OutputBytes == bytes.Length && bytes.Length > 0,
                    extension + " selected second track to " + target + " validated bytes and sample extent");
                check(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(original),
                    extension + " source bytes retained after " + target);
            }
        }

        using var smokeDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtureDirectory, "video-audio-smoke.json")));
        foreach (var fixture in smokeDocument.RootElement.GetProperty("codecVariants").EnumerateArray())
        {
            var name = fixture.GetProperty("fixture").GetString()!;
            var source = await adapter.ProbeVideoAsync(new(Guid.NewGuid(), Path.Combine(fixtureDirectory, name)), default);
            var track = source.Facts.Streams.Single(stream => stream.Kind == "audio");
            var selected = source with { Video = new(track.Index) };
            var plan = AudioConversionPlan.Create(selected.Facts, AudioFormat.Wave, video: selected.Video);
            var temporary = Path.Combine(root, $".context-suite-{selected.ItemId:N}.tmp");
            File.WriteAllBytes(temporary, []);
            var result = await adapter.ConvertAsync(new(selected, temporary, AudioFormat.Wave, plan.RequiredConsent), default);
            var expected = fixture.GetProperty("samples").GetInt64() / track.Channels!.Value;
            check(result.Validation.MatchesPlan && result.DecodedFrames == expected, name + " WAV extraction matches independent sample extent");
        }
        foreach (var extension in new[] { "webm", "mkv" })
        {
            var source = await adapter.ProbeVideoAsync(new(Guid.NewGuid(), Path.Combine(fixtureDirectory, "long-recording." + extension)), default);
            var selected = source with { Video = new(source.Facts.Streams.Single(stream => stream.Kind == "audio").Index) };
            var plan = AudioConversionPlan.Create(selected.Facts, AudioFormat.Wave, video: selected.Video);
            var temporary = Path.Combine(root, $".context-suite-{selected.ItemId:N}.tmp");
            File.WriteAllBytes(temporary, []);
            var result = await adapter.ConvertAsync(new(selected, temporary, AudioFormat.Wave, plan.RequiredConsent), default);
            check(result.Validation.MatchesPlan && result.DecodedFrames == 4800000, extension + " ten-minute recording retains sample count with coarse container timestamps");
        }

        foreach (var name in new[] { "surround.mov", "chapter-artwork.mp4" })
        {
            var source = await adapter.ProbeVideoAsync(new(Guid.NewGuid(), Path.Combine(fixtureDirectory, name)), default);
            var selected = source with { Video = new(source.Facts.Streams.First(stream => stream.Kind == "audio").Index) };
            var surround = name == "surround.mov";
            check(surround ? selected.Facts.Streams.Single(stream => stream.Kind == "audio").Channels == 6 :
                selected.Facts.Chapters.Length == 1 && selected.Facts.Streams.Any(stream => stream.AttachedPicture),
                name + " inventories the actual surround or chapter/artwork policy fixture");
            foreach (var target in new[] { AudioFormat.Wave, AudioFormat.Flac, AudioFormat.Mp3 })
            {
                var plan = AudioConversionPlan.Create(selected.Facts, target, video: selected.Video);
                var temporary = Path.Combine(root, $".context-suite-{selected.ItemId:N}.tmp");
                File.WriteAllBytes(temporary, []);
                if (plan.RequiredConsent != AudioConversionConsent.None)
                {
                    var rejected = false;
                    try { _ = await adapter.ConvertAsync(new(selected, temporary, target, AudioConversionConsent.None), default); }
                    catch (InvalidOperationException) { rejected = true; }
                    check(rejected && new FileInfo(temporary).Length == 0, name + " refuses missing policy consent before writing");
                }
                var result = await adapter.ConvertAsync(new(selected, temporary, target, plan.RequiredConsent), default);
                check(result.Validation.MatchesPlan && File.Exists(source.Path), name + " to " + target + " validates consented output and keeps original");
            }
        }

        var timingTrack = (await adapter.ProbeVideoAsync(new(Guid.NewGuid(),
            Path.Combine(fixtureDirectory, "two-tracks.webm")), default)).Facts.Streams.Last(stream => stream.Kind == "audio");
        var spacedPackets = Enumerable.Range(0, 100).Select(index => new
        {
            stream_index = timingTrack.Index, pts = index * 2, dts = index * 2, duration = 1
        }).ToArray();
        var packetJson = JsonSerializer.SerializeToUtf8Bytes(new { packets = spacedPackets });
        var frameJson = JsonSerializer.SerializeToUtf8Bytes(new { frames = Enumerable.Range(0, 100).Select(index => new
        {
            stream_index = timingTrack.Index, pts = index * 2, duration = 1, nb_samples = 44
        }).ToArray() });
        var refusedTimeline = false;
        try { _ = VideoAudioInventory.Parse(packetJson, frameJson, timingTrack); }
        catch (NotSupportedException) { refusedTimeline = true; }
        check(refusedTimeline, "repeated small packet gaps cannot accumulate into omitted audio");
        var refusedSize = false;
        try { _ = VideoAudioInventory.Parse(new byte[VideoAudioInventory.MaximumJsonBytes + 1], frameJson, timingTrack); }
        catch (InvalidDataException) { refusedSize = true; }
        check(refusedSize, "direct packet inventory has the same bounded JSON budget");

        var largeTrack = timingTrack with { Codec = "pcm_s16le", SampleRate = 192000, Channels = 8,
            ChannelLayout = "7.1", SampleBits = 16, TimeBase = "1/192000", DurationTicks = null };
        var largePackets = JsonSerializer.SerializeToUtf8Bytes(new { packets = Enumerable.Range(0, 2049).Select(index => new
        { stream_index = largeTrack.Index, pts = index * 8192, dts = index * 8192, duration = 8192 }).ToArray() });
        var largeFrames = JsonSerializer.SerializeToUtf8Bytes(new { frames = Enumerable.Range(0, 2049).Select(index => new
        { stream_index = largeTrack.Index, pts = index * 8192, duration = 8192, nb_samples = 8192 }).ToArray() });
        refusedSize = false;
        try { _ = VideoAudioInventory.Parse(largePackets, largeFrames, largeTrack); }
        catch (InvalidDataException error) { refusedSize = error.Message.Contains("decoded extent budget"); }
        check(refusedSize, "video decoded reference above 1 GiB refuses before encoding or WAV publication");
        var excessivePackets = JsonSerializer.SerializeToUtf8Bytes(new { packets = Enumerable.Range(0, 65537).Select(index => new
        { stream_index = largeTrack.Index, pts = index, dts = index, duration = 1 }).ToArray() });
        refusedSize = false;
        try { _ = VideoAudioInventory.Parse(excessivePackets, largeFrames, largeTrack); }
        catch (InvalidDataException error) { refusedSize = error.Message.Contains("packet count"); }
        check(refusedSize, "video packet count above 65536 refuses before timeline decoding");

        var noAudio = Path.Combine(fixtureDirectory, "no-audio.mp4");
        var empty = await adapter.ProbeVideoAsync(new(Guid.NewGuid(), noAudio), default);
        check(empty.Facts.Streams.All(stream => stream.Kind != "audio"), "video-only MP4 inventories no phantom audio track");
        var refused = false;
        try { _ = new VideoAudioSelection(1).Validate(empty.Facts); }
        catch (InvalidDataException) { refused = true; }
        check(refused, "video-only MP4 refuses audio selection");

        var copied = Path.Combine(root, "changed.mp4");
        File.Copy(Path.Combine(fixtureDirectory, "two-tracks.mp4"), copied, true);
        var planned = await adapter.ProbeVideoAsync(new(Guid.NewGuid(), copied), default);
        planned = planned with { Video = new VideoAudioSelection(planned.Facts.Streams.Last(stream => stream.Kind == "audio").Index) };
        var plannedTarget = AudioConversionPlan.Create(planned.Facts, AudioFormat.Wave, video: planned.Video);
        var occupied = Path.Combine(root, $".context-suite-{planned.ItemId:N}.tmp");
        File.WriteAllText(occupied, "occupied");
        refused = false;
        try { _ = await adapter.ConvertAsync(new(planned, occupied, AudioFormat.Wave, plannedTarget.RequiredConsent), default); }
        catch (InvalidDataException) { refused = true; }
        check(refused && File.ReadAllText(occupied) == "occupied", "video extraction refuses occupied reservation");
        File.WriteAllBytes(occupied, []);
        File.AppendAllText(copied, "changed");
        refused = false;
        try { _ = await adapter.ConvertAsync(new(planned, occupied, AudioFormat.Wave, plannedTarget.RequiredConsent), default); }
        catch (InvalidDataException) { refused = true; }
        check(refused && new FileInfo(occupied).Length == 0, "video extraction refuses changed source before writing");

        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        refused = false;
        try { _ = await adapter.ProbeVideoAsync(new(Guid.NewGuid(), noAudio), cancelled.Token); }
        catch (OperationCanceledException) { refused = true; }
        check(refused, "video probe honors cancellation");
    }
}
