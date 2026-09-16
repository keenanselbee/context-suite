using System.Buffers.Binary;

internal static class AudioListeningFixtures
{
    internal static void Create(string path, bool transients)
    {
        var rate = transients ? 48000 : 44100;
        var seconds = transients ? 12 : 18;
        var frames = rate * seconds;
        var bytes = new byte[44 + frames * 6];
        "RIFF"u8.CopyTo(bytes); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), rate);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), rate * 6);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 6);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 24);
        "data"u8.CopyTo(bytes.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), frames * 6);
        var notes = new[] { 220.0, 261.625565, 329.627557, 293.664768, 349.228231, 329.627557, 261.625565, 196.0 };
        uint noise = 0x145b731d;
        for (var frame = 0; frame < frames; frame++)
        {
            var time = (double)frame / rate;
            var fade = Math.Min(1, time / .02) * Math.Min(1, (seconds - time) / .15);
            for (var channel = 0; channel < 2; channel++)
            {
                noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
                var random = (int)noise / (double)int.MaxValue;
                double value;
                if (transients)
                {
                    var local = time % .5;
                    var side = (int)(time / 3) % 2;
                    var envelope = Math.Min(1, local / .001) * Math.Exp(-local * 65);
                    value = .20 * random * envelope * (channel == side ? 1 : .15);
                    if (time is >= 3 and < 6)
                        value += .04 * Math.Sin(2 * Math.PI * (channel == 0 ? 6400 : 8200) * time);
                    if (time is >= 9) value += .025 * random * (12 - time) / 3;
                }
                else
                {
                    var beat = time % .375;
                    var frequency = notes[(int)(time / .375) % notes.Length] * (channel == 0 ? 1 : 1.5);
                    var envelope = Math.Min(1, beat / .005) * Math.Exp(-beat * 7);
                    value = .14 * envelope * (Math.Sin(2 * Math.PI * frequency * time) +
                        .35 * Math.Sin(2 * Math.PI * frequency * 2 * time) + .12 * Math.Sin(2 * Math.PI * frequency * 3 * time));
                    value += .065 * Math.Sin(2 * Math.PI * 55 * time) * Math.Exp(-(time % .75) * 9);
                    value += .025 * random * Math.Exp(-beat * 95);
                }
                var sample = (int)Math.Round(Math.Clamp(value * fade, -.8, .8) * 8388607);
                var offset = 44 + (frame * 2 + channel) * 3;
                bytes[offset] = (byte)sample; bytes[offset + 1] = (byte)(sample >> 8); bytes[offset + 2] = (byte)(sample >> 16);
            }
        }
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(bytes);
    }
}
