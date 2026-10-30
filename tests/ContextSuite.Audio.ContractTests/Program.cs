using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ContextSuite.Private.Audio;

if (args is ["--local-libraries", var localBinary, var localEvidence, var localScenario])
{
    await LocalAudioLibraryContracts.RunAsync(localBinary, localEvidence, localScenario);
    return 0;
}

if (args is ["--process-fixture", var mode, var signal]) return ProcessContracts.Child(mode, signal);
if (args is ["--expanded-audio-refusals", var refusalRuntime, var refusalFixtures, var refusalEvidence])
{
    await ExpandedAudioRefusalContracts.RunAsync(refusalRuntime, refusalFixtures, refusalEvidence);
    return 0;
}
if (args is ["--wma-runtime", var previousRuntime, var repairedRuntime, var runtimeCorpus, var runtimeEvidence])
{
    await WmaRuntimeContracts.RunAsync(previousRuntime, repairedRuntime, runtimeCorpus, runtimeEvidence);
    return 0;
}
if (args is ["--wma-voice-duration", var durationBinary, var durationHelper, var durationCorpus, var durationEvidence])
{
    await WmaVoiceDurationContracts.RunAsync(durationBinary, durationHelper, durationCorpus, durationEvidence);
    return 0;
}
if (args is ["--wma-voice-availability", var availabilityHelper, var availabilityEvidence])
{
    await WmaVoiceAvailabilityContracts.RunAsync(availabilityHelper, availabilityEvidence);
    return 0;
}
if (args is ["--wma-voice-convert", var voiceBinary, var voiceDirectory, var conversionCorpus, var conversionEvidence, var voiceSupplier])
{
    await WmaVoiceAdapterContracts.RunAsync(voiceBinary, voiceDirectory, conversionCorpus, conversionEvidence, voiceSupplier);
    return 0;
}
if (args is ["--wma-voice-owner", var ownerHelper, var ownerInput, var ownerDirectory])
{
    await WmaVoiceProcessContracts.OwnerAsync(ownerHelper, ownerInput, ownerDirectory);
    return 0;
}
if (args is ["--wma-voice-process", var voiceHelper, var voiceCorpus, var voiceRefusals, var voiceEvidence])
{
    await WmaVoiceProcessContracts.RunAsync(voiceHelper, voiceCorpus, voiceRefusals, voiceEvidence);
    return 0;
}
if (args is ["--wma-inputs", var wmaBinary, var wmaFixtures, var wmaEvidence, var wmaDecoder])
{
    await WmaInputContracts.RunAsync(wmaBinary, wmaFixtures, wmaEvidence, wmaDecoder);
    return 0;
}
if (args is ["--aiff-inputs", var aiffBinary, var aiffFixtures, var aiffEvidence, var aiffDecoder])
{
    await AiffInputContracts.RunAsync(aiffBinary, aiffFixtures, aiffEvidence, aiffDecoder);
    return 0;
}
if (args is ["--finite-resampling", var finiteBinary, var finiteEvidence])
{
    await FiniteResamplingContracts.RunAsync(finiteBinary, finiteEvidence);
    return 0;
}
if (args is ["--resampling-sources", var sourceBinary, var sourceEvidence, var sourceDecoder])
{
    await ShortResamplingMatrix.RunSourcesAsync(sourceBinary, sourceEvidence, sourceDecoder);
    return 0;
}
if (args is ["--short-resampling", var resamplingBinary, var resamplingEvidence, var resamplingDecoder])
{
    await ShortResamplingMatrix.RunAsync(resamplingBinary, resamplingEvidence, resamplingDecoder);
    return 0;
}
if (args is ["--mp3-padding", var paddingBinary, var paddingEvidence, var paddingDecoder])
{
    await Mp3PaddingContracts.RunAsync(paddingBinary, paddingEvidence, paddingDecoder);
    return 0;
}
if (args is ["--short-audio-matrix", var shortBinary, var shortEvidence, var shortDecoder])
{
    await ShortAudioMatrix.RunAsync(shortBinary, shortEvidence, shortDecoder);
    return 0;
}
if (args is ["--rate-layout-matrix", var matrixBinary, var matrixEvidence])
{
    await RateLayoutMatrix.RunAsync(matrixBinary, matrixEvidence);
    return 0;
}
if (args is ["--flac-independent-fixtures", var flacBinary, var flacEvidence])
{
    await FlacIndependentFixtures.RunAsync(flacBinary, flacEvidence);
    return 0;
}
if (args is [var artworkMode, var artworkBinary, var artworkFixtures, var artworkEvidence, var artworkDecoder] && artworkMode is "--flac-ogg-artwork" or "--mp3-artwork" or "--ogg-artwork" or "--m4a-artwork" or "--mp3-output-artwork" or "--m4a-output-artwork" or "--wave-artwork" or "--wave-output-artwork" or "--wave-text")
{
    if (Directory.Exists(artworkEvidence)) throw new IOException("Evidence directory already exists.");
    Directory.CreateDirectory(artworkEvidence);
    using var decoderLease = new AudioEncodingAdapter(artworkDecoder, Path.Combine(artworkEvidence, "independent-decoder-lease"));
    var artworkChecks = new List<string>(); var artworkReports = new List<object>();
    if (artworkMode == "--wave-text") await WaveTextContracts.RunAsync(artworkBinary, artworkFixtures, artworkEvidence, artworkChecks, artworkReports, artworkDecoder);
    else if (artworkMode == "--wave-output-artwork") await WaveOutputArtworkContracts.RunAsync(artworkBinary, artworkFixtures, artworkEvidence, artworkChecks, artworkReports, artworkDecoder);
    else if (artworkMode == "--wave-artwork") await WavePreservationContracts.PicturesAsync(artworkBinary, artworkFixtures, artworkEvidence, artworkChecks, artworkReports, artworkDecoder);
    else if (artworkMode == "--m4a-output-artwork") await M4aOutputArtworkContracts.RunAsync(artworkBinary, artworkFixtures, artworkEvidence, artworkChecks, artworkReports, artworkDecoder);
    else if (artworkMode == "--mp3-output-artwork") await Mp3OutputArtworkContracts.RunAsync(artworkBinary, artworkFixtures, artworkEvidence, artworkChecks, artworkReports, artworkDecoder);
    else if (artworkMode == "--mp3-artwork") await Mp3PreservationContracts.PicturesAsync(artworkBinary, artworkFixtures, artworkEvidence, artworkChecks, artworkReports, artworkDecoder);
    else if (artworkMode == "--ogg-artwork") await OggPreservationContracts.PicturesAsync(artworkBinary, artworkFixtures, artworkEvidence, artworkChecks, artworkReports, artworkDecoder);
    else if (artworkMode == "--m4a-artwork") await M4aPreservationContracts.PicturesAsync(artworkBinary, artworkFixtures, artworkEvidence, artworkChecks, artworkReports, artworkDecoder);
    else await FlacToOggArtworkContracts.RunAsync(artworkBinary, artworkFixtures, artworkEvidence, artworkChecks, artworkReports, artworkDecoder);
    await File.WriteAllTextAsync(Path.Combine(artworkEvidence, artworkMode[2..] + ".json"), JsonSerializer.Serialize(new { checks = artworkChecks, reports = artworkReports }));
    Console.WriteLine($"Passed {artworkChecks.Count} {artworkMode[2..]} checks. Evidence: {artworkEvidence}");
    return 0;
}
if (args is [var focusedMode, var oggBinary, var oggFixtures, var oggEvidence] && focusedMode is "--ogg-preservation" or "--mp3-preservation" or "--m4a-preservation" or "--wave-preservation")
{
    if (Directory.Exists(oggEvidence)) throw new IOException("Evidence directory already exists.");
    Directory.CreateDirectory(oggEvidence);
    var oggChecks = new List<string>(); var oggReports = new List<object>();
    if (focusedMode == "--wave-preservation") await WavePreservationContracts.RunAsync(oggBinary, oggFixtures, oggEvidence, oggChecks, oggReports);
    else if (focusedMode == "--ogg-preservation") await OggPreservationContracts.RunAsync(oggBinary, oggFixtures, oggEvidence, oggChecks, oggReports);
    else if (focusedMode == "--mp3-preservation") await Mp3PreservationContracts.RunAsync(oggBinary, oggFixtures, oggEvidence, oggChecks, oggReports);
    else await M4aPreservationContracts.RunAsync(oggBinary, oggFixtures, oggEvidence, oggChecks, oggReports);
    await File.WriteAllTextAsync(Path.Combine(oggEvidence, focusedMode[2..] + ".json"), JsonSerializer.Serialize(new { checks = oggChecks, reports = oggReports }));
    Console.WriteLine($"Passed {oggChecks.Count} focused {focusedMode[2..]} checks. Evidence: {oggEvidence}");
    return 0;
}
string? independentDecoder = null;
if (args is [var candidateBinary, var candidateFixtures, var candidateEvidence, "--independent-decoder", var decoderBinary])
{
    independentDecoder = decoderBinary;
    args = [candidateBinary, candidateFixtures, candidateEvidence];
}
if (args.Length != 3) throw new ArgumentException("Expected pinned engine bin directory, generated matrix fixtures and new evidence directory; optional --independent-decoder selects a pinned artwork decoder.");
if (Directory.Exists(args[2])) throw new IOException("Evidence directory already exists.");
Directory.CreateDirectory(args[2]);
// Hold the independently pinned runtime for test-only artwork pixel decoding.
// The candidate still performs every production probe, optimization and encode.
using var independentLease = independentDecoder is null ? null :
    new AudioEncodingAdapter(independentDecoder, Path.Combine(args[2], "independent-decoder-lease"));
var checks = new List<string>();
var reports = new List<object>();
if (independentDecoder is not null) RuntimeIdentityContracts.Run(args[0], independentDecoder, args[2], checks);
using var adapter = new AudioProbeAdapter(args[0]);
foreach (var format in new[] { "wav", "flac", "mp3", "m4a", "ogg", "opus" })
{
    var source = await File.ReadAllBytesAsync(Path.Combine(args[1], "source." + format));
    var facts = await adapter.ProbeAsync(source, CancellationToken.None);
    var stream = facts.Streams.Single(item => item.Kind == "audio");
    if (stream.SampleRate != 48000 || stream.Channels != 2 || stream.Codec != (format switch
        { "wav" => "pcm_s16le", "m4a" => "aac", "ogg" => "vorbis", _ => format }))
        throw new Exception("Incorrect real audio facts for " + format);
    if (!facts.Tags.ContainsKey("title") && !stream.Tags.ContainsKey("title")) throw new Exception("Missing title for " + format);
    checks.Add("real pipe probe and tags: " + format);
    reports.Add(new { format, facts });
}
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { await adapter.ProbeAsync(new byte[128], cancelled.Token); throw new Exception("Cancellation ignored."); }
    catch (OperationCanceledException) { checks.Add("pre-cancellation"); }
}
var previousReport = Environment.GetEnvironmentVariable("FFREPORT");
var reportPath = Path.Combine(args[2], "must-not-create-engine-report.log");
try
{
    Environment.SetEnvironmentVariable("FFREPORT", "file=" + reportPath.Replace('\\', '/').Replace(":", "\\:") + ":level=48");
    await adapter.ProbeAsync(await File.ReadAllBytesAsync(Path.Combine(args[1], "source.wav")), CancellationToken.None);
    if (File.Exists(reportPath)) throw new Exception("Inherited environment enabled an engine-side log file.");
    checks.Add("engine reporting environment suppressed");
}
finally { Environment.SetEnvironmentVariable("FFREPORT", previousReport); }
try { await adapter.ProbeAsync(new byte[AudioProbeAdapter.MaximumInputBytes + 1], CancellationToken.None); throw new Exception("Input budget ignored."); }
catch (InvalidDataException) { checks.Add("input byte budget"); }
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
try
{
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var playlist = Encoding.UTF8.GetBytes($"#EXTM3U\n#EXT-X-TARGETDURATION:10\n#EXTINF:2,\nhttp://127.0.0.1:{port}/must-not-fetch.ts\n#EXT-X-ENDLIST\n");
    try { await adapter.ProbeAsync(playlist, CancellationToken.None); throw new Exception("Playlist unexpectedly admitted."); }
    catch (InvalidDataException) { checks.Add("playlist format rejected"); }
    if (listener.Pending()) throw new Exception("Rejected playlist accessed the network.");
    checks.Add("no loopback connection from rejected playlist");
}
finally { listener.Stop(); }
try
{
    using var writer = new FileStream(Path.Combine(args[0], "ffprobe.exe"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
    throw new Exception("Engine identity lease did not exclude writers.");
}
catch (IOException) { checks.Add("verified engine lease excludes mutation"); }
adapter.Dispose();
try { await adapter.ProbeAsync(new byte[128], CancellationToken.None); throw new Exception("Disposed adapter ran."); }
catch (ObjectDisposedException) { checks.Add("disposed adapter rejected"); }
await File.WriteAllTextAsync(Path.Combine(args[2], "probe-adapter.json"), JsonSerializer.Serialize(new { utc = DateTime.UtcNow, checks, reports },
    new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Passed {checks.Count} private audio adapter checks. Evidence: {args[2]}");
await EncodingContracts.RunAsync(args[0], args[1], args[2], checks, reports);
await ProcessContracts.RunAsync(args[2], checks);
await FileEncodingContracts.RunAsync(args[0], args[2], checks, reports);
await FileEncodingContracts.DelayedCleanupAsync(args[2], checks);
await FlacArtworkContracts.RunAsync(args[0], args[1], args[2], checks, reports, independentDecoder);
await FlacToOggArtworkContracts.RunAsync(args[0], args[1], args[2], checks, reports, independentDecoder);
await FlacSeekContracts.RunAsync(args[0], args[1], args[2], checks, reports);
await WavePreservationContracts.RunAsync(args[0], args[1], args[2], checks, reports);
await FlacConversionContracts.RunAsync(args[0], args[1], args[2], checks, reports);
await OggPreservationContracts.RunAsync(args[0], args[1], args[2], checks, reports);
await Mp3PreservationContracts.RunAsync(args[0], args[1], args[2], checks, reports);
await M4aPreservationContracts.RunAsync(args[0], args[1], args[2], checks, reports);
await File.WriteAllTextAsync(Path.Combine(args[2], "encoding-adapter.json"), JsonSerializer.Serialize(new { utc = DateTime.UtcNow, checks, reports },
    new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Passed {checks.Count} combined audio probe/encoding checks. Evidence: {args[2]}");
return 0;
