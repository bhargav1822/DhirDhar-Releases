using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace DhirDhar.Desktop.Services;

public interface IImageCacheService
{
    Task<BitmapImage?> GetOrCreateFromPathAsync(string? path, int decodePixelWidth = 0, int decodePixelHeight = 0);
    Task<BitmapImage?> GetOrCreateFromBytesAsync(string key, byte[]? bytes, int decodePixelWidth = 0, int decodePixelHeight = 0);
    void Invalidate(string keyOrPath);
    void Clear();
}

public sealed class ImageCacheService : IImageCacheService
{
    private const int MaxCacheEntries = 128;
    private readonly ConcurrentDictionary<string, BitmapImage> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _lruQueue = new();

    public async Task<BitmapImage?> GetOrCreateFromPathAsync(string? path, int decodePixelWidth = 0, int decodePixelHeight = 0)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var key = $"path_{path}_{decodePixelWidth}_{decodePixelHeight}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            Uri? uri = null;
            if (Uri.TryCreate(path, UriKind.Absolute, out var u) && (u.Scheme == "ms-appx" || u.Scheme == "ms-appdata" || u.Scheme == "file" || u.Scheme == "http" || u.Scheme == "https"))
            {
                uri = u;
            }
            else if (File.Exists(path))
            {
                uri = new Uri(path);
            }

            if (uri == null) return null;

            var bitmap = new BitmapImage(uri);
            if (decodePixelWidth > 0) bitmap.DecodePixelWidth = decodePixelWidth;
            if (decodePixelHeight > 0) bitmap.DecodePixelHeight = decodePixelHeight;

            AddCacheEntry(key, bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public async Task<BitmapImage?> GetOrCreateFromBytesAsync(string key, byte[]? bytes, int decodePixelWidth = 0, int decodePixelHeight = 0)
    {
        if (string.IsNullOrWhiteSpace(key) || bytes == null || bytes.Length == 0) return null;

        var cacheKey = $"bytes_{key}_{decodePixelWidth}_{decodePixelHeight}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var bitmap = new BitmapImage();
            if (decodePixelWidth > 0) bitmap.DecodePixelWidth = decodePixelWidth;
            if (decodePixelHeight > 0) bitmap.DecodePixelHeight = decodePixelHeight;

            using var stream = new InMemoryRandomAccessStream();
            using var writer = new DataWriter(stream);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);

            AddCacheEntry(cacheKey, bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public void Invalidate(string keyOrPath)
    {
        if (string.IsNullOrWhiteSpace(keyOrPath)) return;

        foreach (var key in _cache.Keys)
        {
            if (key.Contains(keyOrPath, StringComparison.OrdinalIgnoreCase))
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

    public void Clear()
    {
        _cache.Clear();
        while (_lruQueue.TryDequeue(out _)) { }
    }

    private void AddCacheEntry(string key, BitmapImage bitmap)
    {
        if (_cache.Count >= MaxCacheEntries)
        {
            if (_lruQueue.TryDequeue(out var oldestKey))
            {
                _cache.TryRemove(oldestKey, out _);
            }
        }

        _cache[key] = bitmap;
        _lruQueue.Enqueue(key);
    }
}
