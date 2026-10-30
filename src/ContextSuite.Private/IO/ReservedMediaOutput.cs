using ContextSuite.Core.Images;
using ContextSuite.Private.Images;

namespace ContextSuite.Private.IO;

// Native encoder callbacks write only to an existing, empty, single-link reservation.
// Check the opened handle as well as the path so a substituted link cannot redirect the write.
internal sealed class ReservedMediaOutput : FileStream
{
    private readonly long _maximumBytes;
    public ReservedMediaOutput(string path, long maximumBytes = ImageContainer.MaximumFileBytes) : base(MediaFiles.ValidatePath(path), FileMode.Open, FileAccess.ReadWrite, FileShare.None)
    {
        try
        {
            MediaFiles.CheckHandle(this, path);
            if (Length != 0 || maximumBytes <= 0) throw new InvalidDataException("Reserved output must be empty and bounded.");
            _maximumBytes = maximumBytes;
        }
        catch { Dispose(); throw; }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        CheckWrite(count);
        base.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        CheckWrite(buffer.Length);
        base.Write(buffer);
    }

    public override void WriteByte(byte value)
    {
        CheckWrite(1);
        base.WriteByte(value);
    }

    public override void SetLength(long value)
    {
        if (value > _maximumBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        base.SetLength(value);
    }

    private void CheckWrite(int count)
    {
        if (count < 0 || Position > _maximumBytes - count)
            throw new ImageFailureException(ImageFailure.ResourceLimit);
    }
}
