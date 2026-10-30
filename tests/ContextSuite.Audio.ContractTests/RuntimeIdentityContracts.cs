using ContextSuite.Private.Audio;

internal static class RuntimeIdentityContracts
{
    public static void Run(string binary, string independent, string evidence, List<string> checks)
    {
        var root = Path.Combine(evidence, "runtime-identity");
        Directory.CreateDirectory(root);
        foreach (var scenario in new[] { "changed-probe", "missing-library", "extra-library", "changed-library", "mixed-encoder" })
        {
            var directory = Path.Combine(root, scenario);
            Directory.CreateDirectory(directory);
            File.Copy(Path.Combine(binary, "ffprobe.exe"), Path.Combine(directory, "ffprobe.exe"));
            if (scenario == "changed-probe")
            {
                using var file = new FileStream(Path.Combine(directory, "ffprobe.exe"), FileMode.Open, FileAccess.ReadWrite);
                file.Position = file.Length - 1;
                var value = file.ReadByte();
                file.Position--;
                file.WriteByte((byte)(value ^ 1));
            }
            else if (scenario == "extra-library")
            {
                foreach (var file in Directory.EnumerateFiles(binary, "*.dll"))
                    File.WriteAllBytes(Path.Combine(directory, Path.GetFileName(file)), [0]);
                File.WriteAllBytes(Path.Combine(directory, "unreviewed.dll"), [0]);
            }
            else if (scenario is "changed-library" or "mixed-encoder")
            {
                foreach (var file in Directory.EnumerateFiles(binary, "*.dll"))
                    File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
                if (scenario == "changed-library")
                {
                    var path = Directory.EnumerateFiles(directory, "*.dll").First();
                    using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
                    file.Position = file.Length - 1;
                    var value = file.ReadByte();
                    file.Position--;
                    file.WriteByte((byte)(value ^ 1));
                }
                else File.Copy(Path.Combine(independent, "ffmpeg.exe"), Path.Combine(directory, "ffmpeg.exe"));
            }
            try
            {
                if (scenario == "mixed-encoder")
                {
                    using var adapter = new AudioEncodingAdapter(directory, Path.Combine(directory, "scratch"));
                }
                else
                {
                    using var adapter = new AudioProbeAdapter(directory);
                }
                throw new Exception("Unsafe runtime admitted: " + scenario);
            }
            catch (InvalidDataException) { checks.Add("runtime identity rejects " + scenario); }
            // Every failed constructor must release its partial identity leases.
            using var writer = new FileStream(Path.Combine(directory, "ffprobe.exe"), FileMode.Open, FileAccess.Write, FileShare.None);
        }
        using var verified = new AudioProbeAdapter(binary);
        foreach (var path in Directory.EnumerateFiles(binary, "*.dll"))
        {
            try
            {
                using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                throw new Exception("Verified runtime library could be modified.");
            }
            catch (IOException) { }
        }
        checks.Add("runtime identity leases exclude library mutation");
    }
}
