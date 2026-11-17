namespace CloudManagerWinUIApp.Services;

/// <summary>
/// Ограничивает частоту уведомлений прогресса в UI-поток (~20 раз/с), гарантируя
/// публикацию финального значения 1.0 при завершении передачи.
/// </summary>
internal struct ProgressThrottle(IProgress<double>? progress, long total)
{
    private const long MinIntervalMs = 50;
    private long _lastReportTick;
    private double _lastFraction = -1;
    private bool _hasReported;

    public void Report(long transferred)
    {
        if (progress is null || total <= 0)
            return;

        var fraction = Math.Clamp((double)transferred / total, 0, 1);
        var now = Environment.TickCount64;
        var completed = transferred >= total;

        if (_hasReported && !completed && (now - _lastReportTick < MinIntervalMs || fraction <= _lastFraction))
            return;

        _hasReported = true;
        _lastReportTick = now;
        _lastFraction = fraction;
        progress.Report(fraction);
    }
}

/// <summary>Поток только для чтения, который сообщает о прогрессе передачи вверх с троттлингом.</summary>
internal sealed class ProgressStream(Stream inner, long total, IProgress<double>? progress) : Stream
{
    private long _transferred;
    private ProgressThrottle _throttle = new(progress, total);

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        Advance(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        Advance(read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        Advance(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Advance(read);
        return read;
    }

    private void Advance(int read)
    {
        if (read <= 0)
            return;

        _transferred += read;
        _throttle.Report(_transferred);
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

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
