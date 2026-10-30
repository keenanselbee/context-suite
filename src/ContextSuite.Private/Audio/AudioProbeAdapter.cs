using ContextSuite.Private.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using ContextSuite.Core.Audio;
using ContextSuite.Private.Images;

namespace ContextSuite.Private.Audio;

// Optional worker integration candidate; no audio payload ships yet. The engine
// receives bytes after job assignment, never customer paths.
public sealed class AudioProbeAdapter : IDisposable
{
    public const int MaximumInputBytes = 64 * 1024 * 1024;
    private readonly string _executable;
    private readonly List<FileStream> _leases = [];
    private readonly RuntimeDirectoryLeases _directories = new();
    private bool _disposed;
    internal string EncoderSha256 { get; }
    internal string InputFormats { get; } = "wav,flac,mp3,mov,ogg";
    internal bool HasRepairedWma { get; }
    internal bool HasVideoAudio { get; }
    private static readonly Dictionary<string, string> EvaluationFiles = new()
    {
        ["ffprobe.exe"] = "4A9D4A879B5F345005141E73543D98525C8FB0A2A14F00877AFEA3CBC9AB4BAB",
        ["avcodec-63.dll"] = "5830CEDD2F84F55480A0F03530F007D0805C47493424B50B389042870A2E54D1",
        ["avdevice-63.dll"] = "897EE4F0FCF2E290F528F0A8296168EB32818A2E091F31C93E81AD949942E7F1",
        ["avfilter-12.dll"] = "4AE63732C318281504194D397853294F7B4F4FA600B5DE4FE87519CA6C1B3B3B",
        ["avformat-63.dll"] = "C4D01F93D2B280B98C181CA6893CB78725F2040327A81E7F9A0E79C7FD278AEB",
        ["avutil-61.dll"] = "CC46F041E206ECB498311950AD92BCC878061E65C31B2DC7EF49FCBDD8A87A62",
        ["swresample-7.dll"] = "D0B412569AE417932AE9139718C25B4E51D5FDBE5B66171069F7AE3A4E776106",
        ["swscale-10.dll"] = "9CA25A241ED82F990AAAE6494EEA971BA048C40B5BFDA119EAB7FC26FE007D9E"
    };

    // Independently built, restricted x64 candidate; packaging adoption remains separate.
    private static readonly Dictionary<string, string> CuratedFiles = new()
    {
        ["ffprobe.exe"] = "0C3B03CCBB315DD2D1CAE50702441A59ABA35677E6F4D1DEFEC8FC603944ADF0",
        ["avcodec-63.dll"] = "AC10FEAE49D73E67212FF6F52537371C004DA224340139CF8103694EC97CADC5",
        ["avfilter-12.dll"] = "0F6ED76C7A30C6958A3013EA2BE80226949E6C007A9444F2376C7C97920FCB75",
        ["avformat-63.dll"] = "72B3CC8577E9E7CB1A320820A7DD31EEEBD6397BEE1BB464BBF54BF4DA28844C",
        ["avutil-61.dll"] = "0B0611B85A5C029459B76E3F26C3513BC895DE2AD7FFC9053ADF4B44E465DA9F",
        ["swresample-7.dll"] = "1FFBF164911A2F1C56B69005915DC1DEA9330F8F94809577D1151FBC85DD69F6"
    };

    // Explicit evaluation identity; production payload selection is unchanged.
    private static readonly Dictionary<string, string> ExpandedFiles = new()
    {
        ["ffprobe.exe"] = "62DA43BA3563856E02299BBA664B08CF620BFE0A74FB7F1E4A75848C06E260C5",
        ["avcodec-63.dll"] = "99D2CD0B182707E25A0AE81D468729BEBE69A4D387D979355E99A10E37D240F5",
        ["avfilter-12.dll"] = "A944189835A99906F068B093AA5AA1EFBE325DED6CCA22654FD17343B1F7BA38",
        ["avformat-63.dll"] = "02023D0DFD442BFF2932686BAB5C6A9859E3A73C403010D7B26FC043742595CF",
        ["avutil-61.dll"] = "722A2F3A8D5B5DDD62B826A747DD838851E29EC7883E3BBF520C076A683735D6",
        ["swresample-7.dll"] = "92CF8D3FDFA6B072BB49E36AC03F91574BC7E8F8ACF681C1E69D0EF4F90A1535"
    };

    // Combined Standard/Pro tail, surround-matrix and raw-transport repair candidate. Voice still requires
    // the separate verified Windows provider; the FFmpeg Voice path is not used.
    private static readonly Dictionary<string, string> RepairedFiles = new()
    {
        ["ffprobe.exe"] = "5EE15EEF2ED11A4F5C0110F8324FC13A1D4E39B94B0D456261753F03ED8D63C6",
        ["avcodec-63.dll"] = "74D3276AFEAE95B354159B8B1266FCA0185A115D0913DDD92C9CAB7A8F7C7BD4",
        ["avfilter-12.dll"] = "2732CA46D82DCDE2870CD31E3079BFBADB8249A933C8260D59FEAFEA13F87F20",
        ["avformat-63.dll"] = "5B96093AB4D5C74354D12E96AF6AE355538634413F5610B49E0EA0A9E00E8E2D",
        ["avutil-61.dll"] = "7FE2FD137248F65ECDBEFCAC5A4DF34029DB76457426A5FE8B99A6EF2CAC4383",
        ["swresample-7.dll"] = "62BC091A40810E5358F39761D259DF9C5D639C1DFA40BAC82FF52A692BD06A68"
    };

    // Combined expanded-audio repair and bounded video-audio input candidate.
    private static readonly Dictionary<string, string> VideoAudioFiles = new()
    {
        ["ffprobe.exe"] = "88F635611485E856AB090F63B64B0F85F8C48B7681D2D23B6FD273E3EBF2C548",
        ["avcodec-63.dll"] = "D9AA40BB39FB902DE8BAECA6FED5761F4E3DF25EF75BBDB8FE091E108C0EC8A9",
        ["avfilter-12.dll"] = "AAA2BEAC77BF74983A7B2A4A95C0137E9FF579C576FE3339D0F9B54B70517F73",
        ["avformat-63.dll"] = "0AB578E586547426C1F654CBF502B3CF2E78807C90192229E6C1DB95A0152B07",
        ["avutil-61.dll"] = "1AD16B98EABEF9EE08C40ED756EEDB8F0EE497658BDBDCCAFD5AB4DFC27F2468",
        ["swresample-7.dll"] = "FA51C13BD7F955B125C318C02B9BFAF2995105DF155CE455392BFBC5BB1003EA"
    };

    public AudioProbeAdapter(string binaryDirectory)
    {
        if (!Path.IsPathFullyQualified(binaryDirectory)) throw new ArgumentException("An absolute engine directory is required.");
        _executable = Path.Combine(binaryDirectory, "ffprobe.exe");
        try
        {
            var probeHash = LeaseFile("ffprobe.exe");
            IReadOnlyDictionary<string, string> pinnedFiles;
            if (probeHash == EvaluationFiles["ffprobe.exe"])
            {
                pinnedFiles = EvaluationFiles;
                EncoderSha256 = "E94AFE21C9ECBA51A43B0EF7874386253E5EFD47E8902E783373B5CC74920A19";
            }
            else if (probeHash == CuratedFiles["ffprobe.exe"])
            {
                pinnedFiles = CuratedFiles;
                EncoderSha256 = "1DFC346B347503923CE686630D6DC479D3FE62EA40B4D7C98DE4BD899B46F9BC";
            }
            else if (probeHash == ExpandedFiles["ffprobe.exe"])
            {
                pinnedFiles = ExpandedFiles;
                EncoderSha256 = "E067F07328AE2B9E25E3B30B097D57A20DB865F7FAB7B711809B840CA2178051";
                InputFormats += ",aiff,asf";
            }
            else if (probeHash == RepairedFiles["ffprobe.exe"])
            {
                pinnedFiles = RepairedFiles;
                EncoderSha256 = "A5C61F77EE6D541A1B75F55A02A9C0A33F66C7E972D9932D4D40342EC2D3715D";
                InputFormats += ",aiff,asf";
                HasRepairedWma = true;
            }
            else if (probeHash == VideoAudioFiles["ffprobe.exe"])
            {
                pinnedFiles = VideoAudioFiles;
                EncoderSha256 = "C4F2D89D1A5EEECFC5FFA321AA08E813894DCA14FBEB1D3A909DE25853FFBC0F";
                InputFormats += ",aiff,asf,matroska,webm";
                HasRepairedWma = true;
                HasVideoAudio = true;
            }
            else throw new InvalidDataException("Audio engine identity mismatch.");
            pinnedFiles = LocalLibraryManifest.Read(binaryDirectory, "ffprobe.exe", pinnedFiles, _directories, _leases);
            var expectedLibraries = pinnedFiles.Keys.Where(name => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            var libraries = Directory.EnumerateFiles(binaryDirectory, "*.dll").Select(Path.GetFileName);
            if (!new HashSet<string?>(libraries, StringComparer.OrdinalIgnoreCase).SetEquals(expectedLibraries))
                throw new InvalidDataException("Audio engine library membership mismatch.");
            foreach (var file in pinnedFiles.Where(file => file.Key != "ffprobe.exe"))
                if (LeaseFile(file.Key) != file.Value) throw new InvalidDataException("Audio engine identity mismatch.");

            string LeaseFile(string name)
            {
                var path = Path.Combine(binaryDirectory, name);
                var lease = _directories.OpenFileRead(path);
                _leases.Add(lease);
                return Convert.ToHexString(SHA256.HashData(lease));
            }
        }
        catch { Dispose(); throw; }
    }

    public async Task<AudioProbeFacts> ProbeAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.IsEmpty || source.Length > MaximumInputBytes) throw new InvalidDataException("Audio probe input exceeds its bounds.");
        var start = new ProcessStartInfo(_executable) { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_executable)!, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        // User environment must not enable engine-side report files or leak media into logs.
        start.Environment.Remove("FFREPORT");
        foreach (var argument in new[] { "-v", "error", "-protocol_whitelist", "pipe", "-format_whitelist", InputFormats,
            "-probesize", "65536", "-analyzeduration", "1000000", "-threads", "1", "-show_entries",
            "stream=index,codec_type,codec_name,sample_rate,channels,bits_per_raw_sample,bit_rate,duration,channel_layout:stream_disposition=attached_pic:stream_tags:format=format_name,duration:format_tags",
            "-of", "json", "pipe:0" }) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        using var job = new EncoderJob();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        process.Start();
        void Stop()
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
        using var registration = deadline.Token.Register(Stop);
        try
        {
            job.Assign(process);
            var output = ReadBoundedAsync(process.StandardOutput.BaseStream, AudioProbeParser.MaximumJsonBytes, deadline.Token);
            var errors = ReadBoundedAsync(process.StandardError.BaseStream, 65536, deadline.Token);
            var input = SendAsync();
            foreach (var task in new Task[] { output, errors, input })
                _ = task.ContinueWith(_ => Stop(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            await Task.WhenAll(output, errors, input, process.WaitForExitAsync(deadline.Token));
            deadline.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidDataException("The audio engine could not identify this bounded input.");
            return AudioProbeParser.Parse(await output);

            async Task SendAsync()
            {
                try { await process.StandardInput.BaseStream.WriteAsync(source, deadline.Token); }
                catch (IOException) when (!deadline.IsCancellationRequested)
                { /* A successful bounded probe can close stdin before the unused tail. Exit and JSON still require validation. */ }
                finally { process.StandardInput.Close(); }
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException("Audio probing cancelled.", error, cancellationToken);
            if (deadline.IsCancellationRequested) throw new TimeoutException("Audio probing exceeded its deadline.", error);
            throw;
        }
        finally { Stop(); await process.WaitForExitAsync(CancellationToken.None); }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("Audio probe output exceeds its budget.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    internal async Task<AudioProbeFacts> ProbeSeekableAsync(FileStream snapshot, string workingDirectory, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        snapshot.Position = 0;
        var bytes = await SeekableMediaProcess.RunAsync(_executable, workingDirectory, snapshot,
            ["-v", "error", "-protocol_whitelist", "fd", "-format_whitelist", InputFormats,
                "-probesize", "65536", "-analyzeduration", "1000000", "-threads", "1", "-show_entries",
                "stream=index,codec_type,codec_name,sample_rate,channels,bits_per_raw_sample,bit_rate,duration,channel_layout:stream_disposition=attached_pic:stream_tags:format=format_name,duration:format_tags",
                "-of", "json", "fd:"], AudioProbeParser.MaximumJsonBytes, deadline.Token);
        return AudioProbeParser.Parse(bytes);
    }

    internal async Task<AudioProbeFacts> ProbeVideoSeekableAsync(FileStream snapshot, string workingDirectory, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!HasVideoAudio) throw new NotSupportedException("The pinned audio engine does not support video audio extraction.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        snapshot.Position = 0;
        var bytes = await SeekableMediaProcess.RunAsync(_executable, workingDirectory, snapshot,
            ["-v", "error", "-protocol_whitelist", "fd", "-format_whitelist", InputFormats,
                "-probesize", "65536", "-analyzeduration", "1000000", "-threads", "1", "-show_chapters", "-show_entries",
                "stream=index,codec_type,codec_name,sample_rate,channels,bits_per_raw_sample,bit_rate,duration,channel_layout,start_time,duration_ts,time_base:stream_disposition=attached_pic:stream_tags:format=format_name,duration,start_time:format_tags:chapter=id,start_time,end_time:chapter_tags",
                "-of", "json", "fd:"], AudioProbeParser.MaximumJsonBytes, deadline.Token);
        return AudioProbeParser.Parse(bytes);
    }

    internal async Task<VideoAudioInventory> InspectVideoAudioAsync(FileStream snapshot, string workingDirectory,
        AudioStreamFacts stream, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!HasVideoAudio) throw new NotSupportedException("The pinned audio engine does not support video audio extraction.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var fields = "packet=stream_index,pts,dts,duration,side_data_list:packet_side_data=side_data_type,skip_samples,discard_padding";
        snapshot.Position = 0;
        var packets = await SeekableMediaProcess.RunAsync(_executable, workingDirectory, snapshot,
            ["-v", "error", "-protocol_whitelist", "fd", "-format_whitelist", InputFormats,
                "-threads", "1", "-select_streams", stream.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-show_packets", "-show_entries", fields, "-of", "json", "fd:"], VideoAudioInventory.MaximumJsonBytes, deadline.Token);
        snapshot.Position = 0;
        var frames = await SeekableMediaProcess.RunAsync(_executable, workingDirectory, snapshot,
            ["-v", "error", "-protocol_whitelist", "fd", "-format_whitelist", InputFormats,
                "-threads", "1", "-select_streams", stream.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-show_frames", "-show_entries", "frame=stream_index,pts,nb_samples,duration", "-of", "json", "fd:"],
            VideoAudioInventory.MaximumJsonBytes, deadline.Token);
        return VideoAudioInventory.Parse(packets, frames, stream);
    }

    internal async Task<System.Collections.Immutable.ImmutableArray<FlacFrame>> ReadFlacFramesAsync(FileStream source,
        FlacMetadataHeader header, string directory, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        source.Position = 0;
        var json = await SeekableMediaProcess.RunAsync(_executable, directory, source,
            ["-v", "error", "-protocol_whitelist", "fd", "-format_whitelist", "flac", "-threads", "1", "-select_streams", "a:0",
                "-show_packets", "-show_entries", "packet=pts,duration,pos,size:stream=codec_name,time_base", "-of", "json", "fd:"],
            FlacFrameProbe.MaximumJsonBytes, token);
        var frames = FlacFrameProbe.Parse(json, header, source.Length);
        await FlacFrameProbe.ValidateBytesAsync(source, header, frames, token);
        return frames;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var lease in _leases) lease.Dispose();
        _leases.Clear();
        _directories.Dispose();
    }
}
