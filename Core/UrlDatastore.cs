using System.Collections.Concurrent;

namespace Core;

public sealed class UrlDatastore : IUrlDatastore
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _longToShortUrlMap = new();
    private readonly ConcurrentDictionary<string, string> _shortToLongUrlMap = new();
    private readonly ConcurrentDictionary<string, int> _shortUrlClickCountMap = new();

    public UrlDatastore()
    {
        UrlClicked += (_, args) => IncrementClickCount(args.ShortUrl);
    }

    public string? GetOriginalUrl(string shortUrl)
    {
        if (_shortToLongUrlMap.TryGetValue(shortUrl, out var longUrl))
        {
            UrlClicked?.Invoke(this, new UrlClickEventArgs(shortUrl));
            return longUrl;
        }
        return null;
    }

    public string CreateShortUrlCode(string longUrl, string? customCode = null)
    {
        if (customCode is null)
        {
            do
            {
                customCode = ShortUrlHelper.GetRandomCode();
            }
            while (_shortToLongUrlMap.TryGetValue(customCode, out _));
        }
        else if (_shortToLongUrlMap.TryGetValue(customCode, out _))
        {
            throw new ArgumentException("Short URL code is already in use.", nameof(customCode));
        }

        // atomic operations to ensure thread safety
        _longToShortUrlMap.AddOrUpdate(longUrl,
            [customCode],
            (_, bag) =>
            {
                bag.Add(customCode);
                return bag;
            });
        _shortToLongUrlMap.AddOrUpdate(customCode, longUrl, (_, _) => longUrl);
        _shortUrlClickCountMap.AddOrUpdate(customCode, 0, static (_, _) => 0);

        return customCode;
    }

    public bool DeleteShortUrlCode(string shortUrlCode)
    {
        if (_shortToLongUrlMap.TryRemove(shortUrlCode, out var longUrl))
        {
            if (_longToShortUrlMap.TryGetValue(longUrl, out var shortUrls))
            {
                if (shortUrls.Count is 1)
                {
                    _longToShortUrlMap.TryRemove(longUrl, out _);
                }
                else
                {
                    shortUrls.TryTake(out _);
                }
            }
            _shortUrlClickCountMap.TryRemove(shortUrlCode, out _);
            return true;
        }
        return false;
    }

    public bool DeleteAllShortUrlsByLongUrl(string longUrl)
    {
        if (_longToShortUrlMap.TryRemove(longUrl, out var shortUrls))
        {
            foreach (var shortUrl in shortUrls)
            {
                _shortToLongUrlMap.TryRemove(shortUrl, out _);
                _shortUrlClickCountMap.TryRemove(shortUrl, out _);
            }

            return true;
        }

        return false;
    }

    public int GetClickCount(string shortUrl)
    {
        return _shortUrlClickCountMap.GetValueOrDefault(shortUrl, -1);
    }

    public bool IsShortUrlCodeInUse(string shortUrlCode)
    {
        return _shortToLongUrlMap.TryGetValue(shortUrlCode, out _);
    }

    private event EventHandler<UrlClickEventArgs>? UrlClicked;

    /// <summary>
    ///     Increments the click count for a shortened URL.
    /// </summary>
    private void IncrementClickCount(string shortUrl)
    {
        _shortUrlClickCountMap.AddOrUpdate(shortUrl, 1, static (_, count) => count + 1);
    }

    /// <inheritdoc />
    /// <summary>
    ///     Event arguments for when a shortened URL is clicked.
    /// </summary>
    private sealed class UrlClickEventArgs : EventArgs
    {
        internal UrlClickEventArgs(string shortUrl)
        {
            ShortUrl = shortUrl;
        }

        internal string ShortUrl { get; }
    }
}
