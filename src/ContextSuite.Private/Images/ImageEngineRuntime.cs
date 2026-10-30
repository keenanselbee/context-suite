using ImageMagick;
using ImageMagick.Configuration;
using ContextSuite.Core.Transport;

namespace ContextSuite.Private.Images;

// Initialize once, inside the sequential media worker, before reading any image.
public static class ImageEngineRuntime
{
    private static bool _initialized;

    public static ImageEngineIdentity GetIdentity()
    {
        return new("Magick.NET-Q16-x64", MagickNET.Version, MagickNET.ImageMagickVersion);
    }

    public static void Initialize(string scratchDirectory)
    {
        if (_initialized) return;
        if (!Path.IsPathFullyQualified(scratchDirectory)) throw new ArgumentException("An absolute engine scratch path is required.");
        Directory.CreateDirectory(scratchDirectory);
        var configuration = ConfigurationFiles.Default;
        configuration.Policy.Data = """
            <policymap>
              <policy domain="delegate" rights="none" pattern="*" />
              <policy domain="filter" rights="none" pattern="*" />
              <policy domain="path" rights="none" pattern="@*" />
              <policy domain="coder" rights="none" pattern="*" />
              <policy domain="coder" rights="read|write" pattern="{PNG,JPEG,WEBP,BMP,BMP3,TGA}" />
              <policy domain="resource" name="width" value="16384" />
              <policy domain="resource" name="height" value="16384" />
              <policy domain="resource" name="list-length" value="2" />
              <policy domain="resource" name="memory" value="512MiB" />
              <policy domain="resource" name="map" value="0" />
              <policy domain="resource" name="disk" value="1GiB" />
              <policy domain="resource" name="thread" value="2" />
            </policymap>
            """;
        var configurationDirectory = Path.Combine(scratchDirectory, "configuration");
        Directory.CreateDirectory(configurationDirectory);
        MagickNET.Initialize(configuration, configurationDirectory);
        MagickNET.SetTempDirectory(scratchDirectory);
        ResourceLimits.MaxProfileSize = 4 * 1024 * 1024;
        _initialized = true;
    }
}
