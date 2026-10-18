using System.Text;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Transport;

internal static class AudioProbeContracts
{
    public static void Run(Action<bool, string> check)
    {
        var facts = AudioProbeParser.Parse("""
            {"streams":[{"index":0,"codec_type":"audio","codec_name":"flac","sample_rate":"48000","channels":2,
            "bits_per_raw_sample":"24","duration":"2.000000","tags":{"TITLE":"Authored"}},
            {"index":1,"codec_type":"video","codec_name":"png"}],"format":{"format_name":"flac","duration":"N/A"}}
            """u8.ToArray());
        check(facts.Streams.Length == 2 && facts.Streams[0].SampleRate == 48000 && facts.Streams[0].SampleBits == 24 &&
            facts.Streams[0].ReportedDurationSeconds == 2 && facts.ReportedDurationSeconds is null && facts.Streams[1].Channels is null,
            "audio probe: typed declarations retain separate streams and unknown properties");
        check(facts.Streams[0].Tags["title"] == "Authored" && facts.Tags.IsEmpty,
            "audio probe: stream tags remain distinct from format tags");
        var video = AudioProbeParser.Parse("""
            {"streams":[{"index":0,"codec_type":"video","codec_name":"h264","time_base":"1/1000"},
            {"index":2,"codec_type":"audio","codec_name":"opus","sample_rate":"48000","channels":2,
            "start_time":"-0.007000","duration_ts":48000,"time_base":"1/48000","tags":{"language":"eng","title":"Commentary"}}],
            "format":{"format_name":"matroska,webm","start_time":"-0.007000","duration":"1.000000"},
            "chapters":[{"id":1,"start_time":"0.000000","end_time":"0.500000","tags":{"title":"Opening"}}]}
            """u8.ToArray());
        check(video.Streams[1].Index == 2 && video.Streams[1].StartSeconds == -0.007m &&
            video.Streams[1].DurationTicks == 48000 && video.Streams[1].TimeBase == "1/48000" && video.StartSeconds == -0.007m,
            "video audio probe retains absolute stream identity, negative priming timestamps and exact time base");
        check(video.Streams[1].Tags["language"] == "eng" && video.Streams[1].Tags["title"] == "Commentary" &&
            video.Chapters.Length == 1 && video.Chapters[0].EndSeconds == 0.5m && video.Chapters[0].Tags["title"] == "Opening",
            "video audio probe keeps track labels and chapters separately for extraction policy");
        new WorkerCommand(1, Guid.NewGuid(), "audio-probe", AudioBytes: [1]).Validate();
        check(true, "audio protocol: dedicated bounded byte payload admitted");
        foreach (var command in new[] { new WorkerCommand(1, Guid.NewGuid(), "audio-probe"),
            new WorkerCommand(1, Guid.NewGuid(), "shutdown", AudioBytes: [1]),
            new WorkerCommand(1, Guid.NewGuid(), "audio-probe", AudioBytes: new byte[WorkerCommand.MaximumAudioProbeBytes + 1]) })
        {
            try { command.Validate(); check(false, "audio protocol: contradictory or oversized request rejected"); }
            catch (InvalidDataException) { check(true, "audio protocol: contradictory or oversized request rejected"); }
        }
        try { (facts with { Container = null! }).Validate(); check(false, "audio protocol: invalid typed reply rejected"); }
        catch (InvalidDataException) { check(true, "audio protocol: invalid typed reply rejected"); }
        foreach (var input in new[] { "{}", "[]", "{", "{\"streams\":[],\"format\":{\"duration\":\"-1\"}}",
            "{\"streams\":[{\"index\":0},{\"index\":0}],\"format\":{}}",
            "{\"streams\":[{\"index\":0,\"channels\":1.5}],\"format\":{}}",
            "{\"streams\":[{\"index\":0,\"bits_per_raw_sample\":100}],\"format\":{}}",
            "{\"streams\":[],\"format\":{\"tags\":{\"title\":\"A\",\"TITLE\":\"B\"}}}",
            "{\"streams\":[],\"format\":{\"tags\":{\"title\":123}}}",
            "{\"streams\":[],\"format\":{\"duration\":\"Infinity\"}}",
            "{\"streams\":[{\"index\":0,\"time_base\":\"1/0\"}],\"format\":{}}",
            "{\"streams\":[{\"index\":0,\"duration_ts\":1.5}],\"format\":{}}",
            "{\"streams\":[],\"format\":{},\"chapters\":{}}",
            "{\"streams\":[],\"format\":{},\"chapters\":[{\"id\":0,\"start_time\":2,\"end_time\":1}]}",
            "{\"streams\":[],\"format\":{},\"chapters\":[{\"id\":0,\"start_time\":0,\"end_time\":1},{\"id\":0,\"start_time\":1,\"end_time\":2}]}",
            "{\"streams\":[],\"format\":{\"format_name\":\"" + new string('x', 129) + "\"}}",
            "{\"streams\":[],\"format\":{\"tags\":{\"title\":\"" + new string('x', 4097) + "\"}}}" })
        {
            try { AudioProbeParser.Parse(Encoding.UTF8.GetBytes(input)); check(false, "audio probe: malformed or excessive reply rejected"); }
            catch (InvalidDataException) { check(true, "audio probe: malformed or excessive reply rejected"); }
        }
        var oversized = new byte[AudioProbeParser.MaximumJsonBytes + 1];
        try { AudioProbeParser.Parse(oversized); check(false, "audio probe: byte budget"); }
        catch (InvalidDataException) { check(true, "audio probe: byte budget"); }
        var streams = "{\"streams\":[" + string.Join(',', Enumerable.Range(0, 33).Select(index => "{\"index\":" + index + "}")) + "],\"format\":{}}";
        try { AudioProbeParser.Parse(Encoding.UTF8.GetBytes(streams)); check(false, "audio probe: stream budget"); }
        catch (InvalidDataException) { check(true, "audio probe: stream budget"); }
        var chapters = "{\"streams\":[],\"format\":{},\"chapters\":[" + string.Join(',', Enumerable.Range(0, 1025)
            .Select(index => "{\"id\":" + index + ",\"start_time\":0,\"end_time\":1}")) + "]}";
        try { AudioProbeParser.Parse(Encoding.UTF8.GetBytes(chapters)); check(false, "audio probe: chapter budget"); }
        catch (InvalidDataException) { check(true, "audio probe: chapter budget"); }
    }
}
