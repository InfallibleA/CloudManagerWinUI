namespace CloudManagerWinUIApp.Services;

/// <summary>Поток только для чтения, который сообщает о прогрессе передачи вверх.</summary>
internal sealed class ProgressStream(Stream inner, long total, IProgress<double>? progress) : Stream
{
    private long _transferred;

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        _transferred += read;
        if (total > 0)
            progress?.Report(Math.Clamp((double)_transferred / total, 0, 1));
        return read;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position
    {
        get => _transferred;
        set => throw new NotSupportedException();
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>Закрывает обёрнутый поток — иначе <see cref="FileStream"/> остаётся открытым.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();

        base.Dispose(disposing);
    }
}
