using System.Collections.Concurrent;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace CloudManagerWinUIApp.Services;

/// <summary>
/// Загружает изображения с бекенда через обычный HTTP-клиент и декодирует их в
/// <see cref="ImageSource"/>.
/// <para>
/// Так надёжнее, чем отдавать удалённый адрес в <c>BitmapImage.UriSource</c>:
/// встроенный загрузчик WinRT в десктопных приложениях не всегда выполняет такие
/// запросы, а здесь используются те же настройки подключения и тайм-ауты, что и у API.
/// </para>
/// </summary>
internal static class ImageLoader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly ConcurrentDictionary<Guid, Task<ImageSource?>> InFlight = new();
    private static readonly ConcurrentDictionary<Guid, ImageSource> Loaded = new();

    /// <summary>Возвращает изображение файла по его id (результат кэшируется в памяти).</summary>
    public static Task<ImageSource?> GetAsync(Guid fileId)
    {
        if (Loaded.TryGetValue(fileId, out var cached))
            return Task.FromResult<ImageSource?>(cached);

        return InFlight.GetOrAdd(fileId, LoadAsync);
    }

    private static async Task<ImageSource?> LoadAsync(Guid fileId)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(AppServices.ImageUri(fileId)).ConfigureAwait(false);
            var source = await CreateOnUiThreadAsync(bytes).ConfigureAwait(false);

            if (source is not null)
            {
                Loaded[fileId] = source;
                return source;
            }
        }
        catch (Exception)
        {
            // Изображение не критично: карточка покажет заглушку.
        }

        InFlight.TryRemove(fileId, out _);
        return null;
    }

    private static Task<ImageSource?> CreateOnUiThreadAsync(byte[] bytes)
    {
        var queue = App.MainWindow?.DispatcherQueue;
        if (queue is null || queue.HasThreadAccess)
            return CreateAsync(bytes);

        var completion = new TaskCompletionSource<ImageSource?>();
        queue.TryEnqueue(async () =>
        {
            try
            {
                completion.SetResult(await CreateAsync(bytes));
            }
            catch (Exception)
            {
                completion.SetResult(null);
            }
        });

        return completion.Task;
    }

    private static async Task<ImageSource?> CreateAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);

        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
