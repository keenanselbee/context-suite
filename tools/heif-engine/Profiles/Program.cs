using ImageMagick;

if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]) || Directory.Exists(args[0]))
    throw new ArgumentException("Specify a new absolute research profile directory.");
Directory.CreateDirectory(args[0]);
File.WriteAllBytes(Path.Combine(args[0], "srgb.icc"), ColorProfiles.SRGB.ToByteArray());
File.WriteAllBytes(Path.Combine(args[0], "adobe.icc"), ColorProfiles.AdobeRGB1998.ToByteArray());
