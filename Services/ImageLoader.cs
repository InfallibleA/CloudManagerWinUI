using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace CloudManagerWinUIApp.Services;

/// <summary>
/// Загружает изображения с бекенда через общий HTTP-пул и декодирует их в
/// <see cref="ImageSource"/> с ограничением размера кэша и целевой ширины декодирования.
/// </summary>
internal static class ImageLoader
{
    private const int MaxCachedImages = 64;

    private readonly record struct ImageCacheKey(Guid FileId, int DecodePixelWidth);
    private readonly record struct CacheEntry(ImageCacheKey Key, ImageSource Source);

    private static readonly HttpClient Http = new(AppServices.SharedHttpHandler, disposeHandler: false)
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private static readonly ConcurrentDictionary<ImageCacheKey, Task<ImageSource?>> InFlight = new();
    private static readonly Lock CacheLock = new();
    private static readonly Dictionary<ImageCacheKey, LinkedListNode<CacheEntry>> CacheIndex = new(MaxCachedImages);
    private static readonly LinkedList<CacheEntry> CacheLru = new();

    /// <summary>Возвращает изображение файла по его id (результат кэшируется в ограниченном LRU-кэше).</summary>
    public static Task<ImageSource?> GetAsync(
        Guid fileId,
        int decodePixelWidth = 0,
        CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<ImageSource?>(ct);

        var key = new ImageCacheKey(fileId, Math.Max(0, decodePixelWidth));

        if (TryGetCached(key, out var cached))
            return Task.FromResult<ImageSource?>(cached);

        var task = InFlight.GetOrAdd(key, static k => LoadAsync(k));
        return ct.CanBeCanceled ? task.WaitAsync(ct) : task;
    }

    private static bool TryGetCached(ImageCacheKey key, out ImageSource? source)
    {
        lock (CacheLock)
        {
            if (CacheIndex.TryGetValue(key, out var node))
            {
                CacheLru.Remove(node);
                CacheLru.AddFirst(node);
                source = node.Value.Source;
                return true;
            }
        }

        source = null;
        return false;
    }

    private static void StoreCached(ImageCacheKey key, ImageSource source)
    {
        lock (CacheLock)
        {
            if (CacheIndex.TryGetValue(key, out var existing))
            {
                CacheLru.Remove(existing);
                existing.Value = new CacheEntry(key, source);
                CacheLru.AddFirst(existing);
                return;
            }

            if (CacheIndex.Count >= MaxCachedImages && CacheLru.Last is { } oldest)
            {
                CacheLru.RemoveLast();
                CacheIndex.Remove(oldest.Value.Key);
            }

            var node = CacheLru.AddFirst(new CacheEntry(key, source));
            CacheIndex[key] = node;
        }
    }

    private static async Task<ImageSource?> LoadAsync(ImageCacheKey key)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(AppServices.ImageUri(key.FileId)).ConfigureAwait(false);
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer()).AsTask().ConfigureAwait(false);
            stream.Seek(0);

            var source = await CreateBitmapOnUiThreadAsync(stream, key.DecodePixelWidth).ConfigureAwait(false);
            if (source is not null)
            {
                StoreCached(key, source);
                return source;
            }
        }
        catch (Exception)
        {
            // Изображение не критично: карточка покажет заглушку.
        }
        finally
        {
            InFlight.TryRemove(key, out _);
        }

        return null;
    }

    private static Task<ImageSource?> CreateBitmapOnUiThreadAsync(
        InMemoryRandomAccessStream stream,
        int decodePixelWidth)
    {
        var queue = App.MainWindow?.DispatcherQueue;
        if (queue is null || queue.HasThreadAccess)
            return CreateBitmapAsync(stream, decodePixelWidth);

        var completion = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryEnqueue(async () =>
        {
            try
            {
                completion.TrySetResult(await CreateBitmapAsync(stream, decodePixelWidth));
            }
            catch (Exception)
            {
                completion.TrySetResult(null);
            }
        }))
        {
            completion.TrySetResult(null);
        }

        return completion.Task;
    }

    private static async Task<ImageSource?> CreateBitmapAsync(
        InMemoryRandomAccessStream stream,
        int decodePixelWidth)
    {
        var bitmap = new BitmapImage();
        if (decodePixelWidth > 0)
            bitmap.DecodePixelWidth = decodePixelWidth;

        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
