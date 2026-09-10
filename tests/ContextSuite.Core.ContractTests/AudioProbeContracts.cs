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
    }
}
