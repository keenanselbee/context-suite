using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

internal static partial class AudioConversionDirectContracts
{
    internal sealed record MetadataFixture(string Name, string Path, string Sha256,
        Dictionary<string, string> Tags, bool Picture, bool Refuse = false);

    public static async Task ExpandedMetadataAsync(string scratch, string executable, string manifest, Action<bool, string> check)
    {
        if (Directory.Exists(scratch)) throw new IOException("Use new metadata command evidence.");
        var fixtures = JsonSerializer.Deserialize<MetadataFixture[]>(await File.ReadAllTextAsync(manifest))!;
        if (fixtures.Length != 9 || fixtures.Count(item => item.Refuse) != 2) throw new InvalidDataException("Expected seven inputs and two preservation refusals.");
        Directory.CreateDirectory(scratch);
        var access = new Access { Allowed = true };
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new RefusingRecycler());
        var worker = new WorkerClient(executable, Path.Combine(scratch, "workers"));
        await using var vm = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access);
        var prompts = 0; var reports = new List<object>();
        var quiet = new QuietWorkflow();
        vm.QuickBatchStarted += (request, _) => quiet.Begin(request.RequestId, false, DateTimeOffset.UtcNow);
        vm.QuickBatchCompleted += (request, rows) => quiet.Complete(request.RequestId, rows.Select(row => row.Result).ToArray());
        vm.ConversionRequested += (_, _) => throw new Exception("Audio metadata opened an image planner.");
        vm.AudioConversionRequested += async (decision, _) =>
        {
            prompts++;
            check(decision.Plan.RequiredConsent != AudioConversionConsent.None, "Expanded metadata: no routine metadata acknowledgement");
            await decision.RefreshAccessAsync();
            ConfirmedAudioConversion? result = null;
            decision.Confirmed += value => result = value;
            decision.ConfirmCommand.Execute(null);
            return result;
        };
        foreach (var fixture in fixtures)
        {
            check(Path.GetFileName(fixture.Name) == fixture.Name && await HashMetadataAsync(fixture.Path) == fixture.Sha256,
                "Expanded metadata: fixture identity " + fixture.Name);
            var source = Path.Combine(scratch, fixture.Name + Path.GetExtension(fixture.Path)); File.Copy(fixture.Path, source);
            var modified = File.GetLastWriteTimeUtc(source);
            if (fixture.Refuse)
            {
                var admissions = access.Admissions; var priorPrompts = prompts;
                vm.Admit(new(Guid.NewGuid(), "convert", "wav", [source])); await vm.WaitForIdleAsync();
                check(vm.Rows[^1].Result.State == OperationState.Unsupported && access.Admissions == admissions && prompts == priorPrompts &&
                    !Directory.EnumerateFiles(scratch, fixture.Name + " - Converted*").Any(),
                    "Expanded metadata: unsafe information refused before admission or output " + fixture.Name + ": " + vm.Rows[^1].Status);
            }
            else
            {
                var original = await ReadExpandedMetadataAsync(source);
                var descriptions = original.Tags.Where(tag => !AudioCommentConversion.IsTechnicalTag(tag.Key)).ToImmutableDictionary();
                check(fixture.Tags.All(tag => original.Tags.TryGetValue(tag.Key, out var value) && value == tag.Value) &&
                    original.Pictures.Length == (fixture.Picture ? 1 : 0), "Expanded metadata: authored text and cover present " + fixture.Name);
                var expectedPictures = FlacDescriptiveMetadata.ReadBlocks(original.Pictures).Pictures;
                foreach (var (action, extension) in new[] { ("wav", ".wav"), ("flac", ".flac"), ("mp3", ".mp3"),
                    ("m4a", ".m4a"), ("vorbis", ".ogg"), ("opus", ".opus") })
                {
                    var admissions = access.Admissions;
                    vm.Admit(new(Guid.NewGuid(), "convert", action, [source])); await vm.WaitForIdleAsync();
                    var row = vm.Rows[^1];
                    check(row.Result.State == OperationState.Succeeded && row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated &&
                        Path.GetExtension(row.OutputPath) == extension && access.Admissions == admissions + 1 && !quiet.NeedsAttention,
                        "Expanded metadata: quiet validated copy " + fixture.Name + "/" + action + ": " + row.Status);
                    var actual = await ReadExpandedMetadataAsync(row.OutputPath);
                    var actualDescriptions = actual.Tags.Where(tag => !AudioCommentConversion.IsTechnicalTag(tag.Key)).ToImmutableDictionary();
                    // M4A's mandatory track language may declare "undetermined"
                    // when the input has no language; it does not invent one.
                    if (action == "m4a" && !descriptions.ContainsKey("language") && actualDescriptions.GetValueOrDefault("language") == "und")
                        actualDescriptions = actualDescriptions.Remove("language");
                    check(descriptions.Count == actualDescriptions.Count && descriptions.All(tag => actualDescriptions.TryGetValue(tag.Key, out var value) && value == tag.Value),
                        "Expanded metadata: all descriptive values preserved " + fixture.Name + "/" + action + ": " + JsonSerializer.Serialize(actualDescriptions));
                    var pictures = FlacDescriptiveMetadata.ReadBlocks(actual.Pictures).Pictures;
                    check(pictures.Length == expectedPictures.Length && pictures.Select((picture, i) => picture.Type == expectedPictures[i].Type &&
                        picture.Description == expectedPictures[i].Description && picture.MediaType == expectedPictures[i].MediaType &&
                        picture.Sha256 == expectedPictures[i].Sha256).All(equal => equal),
                        "Expanded metadata: exact picture bytes and labels preserved " + fixture.Name + "/" + action);
                    reports.Add(new { fixture.Name, Source = source, Output = row.OutputPath, Target = action, Tags = actualDescriptions,
                        Picture = fixture.Picture, SourceSha256 = fixture.Sha256, OutputSha256 = await HashMetadataAsync(row.OutputPath) });
                    await File.WriteAllTextAsync(Path.Combine(scratch, "expanded-metadata.json"), JsonSerializer.Serialize(reports,
                        new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            check(await HashMetadataAsync(source) == fixture.Sha256 && File.GetLastWriteTimeUtc(source) == modified,
                "Expanded metadata: original bytes and timestamp retained " + fixture.Name);
        }
        check(!Directory.EnumerateFiles(scratch, ".context-suite-*.tmp", SearchOption.AllDirectories).Any() && publisher.FindRecoveryRecords().Count == 0,
            "Expanded metadata: no abandoned output or recovery records");
    }

    private static async Task<string> HashMetadataAsync(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file));
    }

    private static async Task<(ImmutableDictionary<string, string> Tags, ImmutableArray<FlacMetadataBlock> Pictures)> ReadExpandedMetadataAsync(string path)
    {
        using var file = File.OpenRead(path);
        switch (Path.GetExtension(path))
        {
            case ".aif":
                var aiff = await AiffMetadata.ReadAsync(file, default); aiff.RequireConversionSupport();
                return (aiff.Tags, aiff.Pictures);
            case ".wma":
                var asf = await AsfMetadata.ReadAsync(file, default); asf.RequireConversionSupport();
                return (asf.Tags, asf.Pictures);
            case ".wav":
                var wave = await WaveMetadata.ReadAsync(file, default, true);
                return (wave.Tags.ToImmutableDictionary(tag => tag.Name, tag => tag.Value), wave.Pictures);
            case ".flac":
                var flac = FlacMetadata.Parse(await File.ReadAllBytesAsync(path));
                return (AudioCommentConversion.Read(FlacDescriptiveMetadata.Read(flac).Comments), flac.Blocks.Where(block => block.Type == 6).ToImmutableArray());
            case ".mp3":
                var mp3 = await Mp3Metadata.ReadAsync(file, default, true); return (mp3.Tags, mp3.Pictures);
            case ".m4a":
                var m4a = await M4aMetadata.ReadAsync(file, default, true); return (m4a.Tags, m4a.Pictures);
            default:
                var ogg = await OggMetadata.ReadAsync(file, default, true);
                var pictures = OggPictureComments.Read(ogg.Descriptions); return (ogg.ConversionTags(pictures), pictures);
        }
    }
}
