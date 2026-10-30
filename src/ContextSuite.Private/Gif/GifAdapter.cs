using System.Collections.Immutable;
using System.Security.Cryptography;
using ContextSuite.Core.Gif;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Private.Images;
using ContextSuite.Private.IO;
using ImageMagick;

namespace ContextSuite.Private.Gif;

// The sequential worker owns one evolving GIF compositor until the final frame.
public sealed class GifAdapter : IDisposable
{
    private FileStream? _source;
    private GifSourceFacts? _facts;
    private IEnumerator<GifDisplayedFrame>? _frames;
    private int _nextFrame;

    public GifAdapter(string scratchDirectory)
    {
        ImageEngineRuntime.Initialize(scratchDirectory);
    }

    public GifSourceFacts Probe(GifProbe request, CancellationToken token)
    {
        Release();
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        token.ThrowIfCancellationRequested();
        using var source = MediaFiles.OpenRead(request.Path);
        var (input, hash) = ReadSource(source, request.Path, token);
        var gif = Scan(input, token);
        return Facts(request.ItemId, request.Path, source.Length, hash, gif);
    }

    public GifWorkResult Convert(GifWork request, CancellationToken token)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Validate();
            token.ThrowIfCancellationRequested();
            if (_facts is null || !SameSource(_facts, request.Source) || request.FrameIndex < _nextFrame)
            {
                Release();
                Open(request.Source, token);
            }

            GifDisplayedFrame? frame = null;
            while (_nextFrame <= request.FrameIndex)
            {
                token.ThrowIfCancellationRequested();
                bool hasFrame;
                try { hasFrame = _frames!.MoveNext(); }
                catch (InvalidDataException error) { throw new ImageFailureException(ImageFailure.InvalidInput, error); }
                if (!hasFrame) throw new ImageFailureException(ImageFailure.InvalidInput);
                frame = _frames.Current;
                if (frame.Index != _nextFrame || frame.Width != request.Source.Width || frame.Height != request.Source.Height ||
                    frame.DelayCentiseconds != request.Source.DelayCentiseconds[_nextFrame])
                    throw new ImageFailureException(ImageFailure.SourceChanged);
                _nextFrame++;
            }

            if (frame is null) throw new ImageFailureException(ImageFailure.ValidationFailed);
            MediaFiles.CheckHandle(_source!, request.Source.Path);
            if (_source!.Length != request.Source.FileBytes)
                throw new ImageFailureException(ImageFailure.SourceChanged);
            var result = WriteAndValidate(request, frame, token);
            if (_nextFrame == request.Source.FrameCount) Release();
            return result;
        }
        catch (MagickException error)
        {
            Release();
            token.ThrowIfCancellationRequested();
            throw new ImageFailureException(ImageFailure.EngineFailure, error);
        }
        catch
        {
            Release();
            throw;
        }
    }

    public void Release()
    {
        _frames?.Dispose();
        _source?.Dispose();
        _frames = null;
        _source = null;
        _facts = null;
        _nextFrame = 0;
    }

    public void Dispose() => Release();

    private void Open(GifSourceFacts expected, CancellationToken token)
    {
        FileStream? source = null;
        try
        {
            source = MediaFiles.OpenRead(expected.Path);
            var (input, hash) = ReadSource(source, expected.Path, token);
            if (source.Length != expected.FileBytes || hash != expected.Sha256)
                throw new ImageFailureException(ImageFailure.SourceChanged);
            var gif = Scan(input, token);
            var actual = Facts(expected.ItemId, expected.Path, source.Length, hash, gif);
            if (!SameSource(actual, expected)) throw new ImageFailureException(ImageFailure.SourceChanged);
            _frames = gif.ReadFrames(token).GetEnumerator();
            _source = source;
            _facts = actual;
            source = null;
        }
        finally { source?.Dispose(); }
    }

    private static (byte[] Bytes, string Hash) ReadSource(FileStream source, string path, CancellationToken token)
    {
        if (source.Length < 14) throw new ImageFailureException(ImageFailure.InvalidInput);
        if (source.Length > GifSourceFacts.MaximumFileBytes)
            throw new ImageFailureException(ImageFailure.ResourceLimit);
        source.Position = 0;
        var bytes = new byte[checked((int)source.Length)];
        var read = 0;
        while (read < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            var count = source.Read(bytes, read, Math.Min(65536, bytes.Length - read));
            if (count == 0) throw new ImageFailureException(ImageFailure.InvalidInput);
            read += count;
        }
        token.ThrowIfCancellationRequested();
        MediaFiles.CheckHandle(source, path);
        return (bytes, System.Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static GifInput Scan(byte[] bytes, CancellationToken token)
    {
        try { return GifInput.Scan(bytes, token); }
        catch (InvalidDataException error) { throw new ImageFailureException(ImageFailure.InvalidInput, error); }
    }

    private static GifSourceFacts Facts(Guid id, string path, long length, string hash, GifInput gif)
    {
        var facts = new GifSourceFacts(id, Path.GetFullPath(path), hash, length, (uint)gif.Width, (uint)gif.Height,
            gif.FrameCount, gif.DelayCentiseconds.ToImmutableArray(), gif.LoopCount);
        facts.Validate();
        return facts;
    }

    private static bool SameSource(GifSourceFacts left, GifSourceFacts right) =>
        left.ItemId == right.ItemId &&
        string.Equals(Path.GetFullPath(left.Path), Path.GetFullPath(right.Path), StringComparison.OrdinalIgnoreCase) &&
        left.Sha256 == right.Sha256 && left.FileBytes == right.FileBytes &&
        left.Width == right.Width && left.Height == right.Height && left.FrameCount == right.FrameCount &&
        left.LoopCount == right.LoopCount && left.DelayCentiseconds.SequenceEqual(right.DelayCentiseconds);

    private static GifWorkResult WriteAndValidate(GifWork request, GifDisplayedFrame frame, CancellationToken token)
    {
        using (var image = new MagickImage())
        {
            image.Progress += (_, progress) => { if (token.IsCancellationRequested) progress.Cancel = true; };
            image.ReadPixels(frame.RgbaPixels, new PixelReadSettings((uint)frame.Width, (uint)frame.Height, StorageType.Char, PixelMapping.RGBA));
            image.ColorSpace = ColorSpace.sRGB;
            image.Depth = 8;
            image.SetProfile(ColorProfiles.SRGB);
            image.Settings.SetDefine(MagickFormat.Png, "color-type", "6");
            image.Settings.SetDefine(MagickFormat.Png, "bit-depth", "8");
            image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
            image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "date,time");
            token.ThrowIfCancellationRequested();
            try
            {
                using var output = new ReservedMediaOutput(request.TemporaryPath, GifWork.MaximumOutputBytes);
                image.Write(output, MagickFormat.Png);
                output.Flush(true);
            }
            catch (MagickException) when (token.IsCancellationRequested)
            { token.ThrowIfCancellationRequested(); throw; }
            catch (MagickException error) when (!token.IsCancellationRequested)
            { throw new ImageFailureException(ImageFailure.EngineFailure, error); }
        }

        token.ThrowIfCancellationRequested();
        using var validated = MediaFiles.OpenRead(request.TemporaryPath);
        if (validated.Length is <= 0 or > GifWork.MaximumOutputBytes)
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        var (bytes, hash) = ReadSource(validated, request.TemporaryPath, token);
        try
        {
            var container = ImageContainer.Read(bytes);
            using var decoded = new MagickImage(bytes, MagickFormat.Png);
            if (container.Format != ImageFormat.Png || decoded.Format != MagickFormat.Png ||
                decoded.Width != request.Source.Width || decoded.Height != request.Source.Height || decoded.Depth != 8 ||
                decoded.GetColorProfile()?.ToByteArray() is not { } profile ||
                !profile.SequenceEqual(ColorProfiles.SRGB.ToByteArray()) ||
                decoded.GetPixels().ToByteArray(PixelMapping.RGBA) is not { } pixels ||
                !pixels.SequenceEqual(frame.RgbaPixels))
                throw new ImageFailureException(ImageFailure.ValidationFailed);
        }
        catch (Exception error) when (error is InvalidDataException or MagickException)
        { throw new ImageFailureException(ImageFailure.ValidationFailed, error); }
        token.ThrowIfCancellationRequested();
        return new GifWorkResult(new OutputValidation(request.OutputId, hash, true), request.Source.ItemId,
            request.Source.Sha256, request.FrameIndex, request.Source.Width, request.Source.Height, bytes.LongLength,
            "Context Suite GIF compositor 1; curated PNG sRGB8 encoder");
    }
}
