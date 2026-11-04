using Amazon.S3;
using Amazon.S3.Model;
using Gallery.Web.Observability;

namespace Gallery.Web.Storage;

/// <summary>
/// Singleton S3 wrapper. All calls go through <see cref="OpTiming"/> directly because this is a
/// singleton and cannot hold the scoped <see cref="OpTimer"/>.
/// </summary>
public sealed class ImageStore
{
    private readonly AmazonS3Client _client;
    private readonly bool _isHttps;
    private readonly string _imageBaseUrl;
    private readonly ILogger<ImageStore> _logger;

    public ImageStore(S3ClientFactory factory, ILogger<ImageStore> logger)
    {
        _client = factory.CreateClient();
        _isHttps = factory.IsHttps;
        _imageBaseUrl = factory.Options.ImageBaseUrl?.TrimEnd('/') ?? string.Empty;
        _logger = logger;
    }

    public async Task<bool> ExistsAsync(string bucket, string key)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await _client.GetObjectMetadataAsync(bucket, key);
            OpTiming.Log(_logger, "s3.head", Ms(start), "ok");
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            OpTiming.Log(_logger, "s3.head", Ms(start), "ok");
            return false;
        }
        catch
        {
            OpTiming.Log(_logger, "s3.head", Ms(start), "error");
            throw;
        }
    }

    public async Task PutAsync(string bucket, string key, Stream data, string contentType, string cacheControl)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var request = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = data,
                ContentType = contentType,
                DisableDefaultChecksumValidation = true,
                DisablePayloadSigning = _isHttps,
            };
            request.Headers.CacheControl = cacheControl;
            await _client.PutObjectAsync(request);
            OpTiming.Log(_logger, "s3.put", Ms(start), "ok");
        }
        catch
        {
            OpTiming.Log(_logger, "s3.put", Ms(start), "error");
            throw;
        }
    }

    public async Task<byte[]> GetRangeAsync(string bucket, string key, long from, long to)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = bucket,
                Key = key,
                ByteRange = new ByteRange(from, to),
            };
            using var response = await _client.GetObjectAsync(request);
            using var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms);
            OpTiming.Log(_logger, "s3.get-range", Ms(start), "ok");
            return ms.ToArray();
        }
        catch
        {
            OpTiming.Log(_logger, "s3.get-range", Ms(start), "error");
            throw;
        }
    }

    public async Task CopyAsync(string srcBucket, string srcKey, string dstBucket, string dstKey,
        string contentType, string cacheControl)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var request = new CopyObjectRequest
            {
                SourceBucket = srcBucket,
                SourceKey = srcKey,
                DestinationBucket = dstBucket,
                DestinationKey = dstKey,
                ContentType = contentType,
                MetadataDirective = S3MetadataDirective.REPLACE,
            };
            request.Headers.CacheControl = cacheControl;
            await _client.CopyObjectAsync(request);
            OpTiming.Log(_logger, "s3.copy", Ms(start), "ok");
        }
        catch
        {
            OpTiming.Log(_logger, "s3.copy", Ms(start), "error");
            throw;
        }
    }

    public async Task DeleteAsync(string bucket, string key)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await _client.DeleteObjectAsync(bucket, key);
            OpTiming.Log(_logger, "s3.delete", Ms(start), "ok");
        }
        catch
        {
            OpTiming.Log(_logger, "s3.delete", Ms(start), "error");
            throw;
        }
    }

    /// <summary>
    /// Returns a presigned PUT URL valid for up to 15 minutes (III.9).
    /// Throws <see cref="ArgumentOutOfRangeException"/> if <paramref name="expiry"/> exceeds 15 min.
    /// </summary>
    public async Task<string> PresignPutAsync(string bucket, string key, string contentType, TimeSpan expiry)
    {
        if (expiry > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(expiry), "Presign expiry must not exceed 15 minutes (III.9).");

        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            // The SDK writes X-Amz-Expires as the whole seconds from ITS signing time to Expires,
            // rounded up, so "now + 15 min" with a sub-second remainder can come out as 901.
            // Anchoring Expires on a whole-second floor of now keeps it <= expiry (III.9).
            var now = DateTime.UtcNow;
            var request = new GetPreSignedUrlRequest
            {
                BucketName = bucket,
                Key = key,
                ContentType = contentType,
                Verb = HttpVerb.PUT,
                Expires = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond)).Add(expiry),
                Protocol = _isHttps ? Protocol.HTTPS : Protocol.HTTP,
            };
            // GetPreSignedURL is synchronous; wrap in Task to match the async contract.
            var url = await Task.FromResult(_client.GetPreSignedURL(request));
            // The SDK signs on its clock-skew-corrected time (learned per endpoint from response
            // Date headers, second precision), which can sit before our floored "now", so the
            // whole-second ceiling can still come out one second over. Enforce the cap on the
            // value actually signed: re-sign with Expires pulled back by the overshoot (III.9).
            var over = SignedExpirySeconds(url) - (int)expiry.TotalSeconds;
            if (over > 0)
            {
                request.Expires = request.Expires!.Value.AddSeconds(-over);
                url = _client.GetPreSignedURL(request);
            }
            OpTiming.Log(_logger, "s3.presign", Ms(start), "ok");
            return url;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw;
        }
        catch
        {
            OpTiming.Log(_logger, "s3.presign", Ms(start), "error");
            throw;
        }
    }

    // ── 008 upload pipeline additions (T014). Existing methods above are unchanged. ──────────

    /// <summary>
    /// HEAD an object. Returns <c>exists=false</c> (length 0, etag null) on 404 (op <c>s3.head</c>).
    /// </summary>
    public async Task<(bool exists, long length, string? etag)> HeadAsync(string bucket, string key)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var response = await _client.GetObjectMetadataAsync(bucket, key);
            OpTiming.Log(_logger, "s3.head", Ms(start), "ok");
            return (true, response.ContentLength, response.ETag);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            OpTiming.Log(_logger, "s3.head", Ms(start), "ok");
            return (false, 0, null);
        }
        catch
        {
            OpTiming.Log(_logger, "s3.head", Ms(start), "error");
            throw;
        }
    }

    /// <summary>
    /// Reads a whole object (op <c>s3.get</c>). Throws <see cref="ObjectTooLargeException"/> when its
    /// length exceeds <paramref name="maxBytes"/>; the body is never read past the cap.
    /// </summary>
    public async Task<(byte[] bytes, string etag)> GetAsync(string bucket, string key, long maxBytes)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            using var response = await _client.GetObjectAsync(bucket, key);
            if (response.ContentLength > maxBytes)
                throw new ObjectTooLargeException(response.ContentLength, maxBytes);

            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await response.ResponseStream.ReadAsync(buffer)) > 0)
            {
                if (ms.Length + read > maxBytes)
                    throw new ObjectTooLargeException(ms.Length + read, maxBytes);
                ms.Write(buffer, 0, read);
            }
            OpTiming.Log(_logger, "s3.get", Ms(start), "ok");
            return (ms.ToArray(), response.ETag);
        }
        catch (ObjectTooLargeException)
        {
            OpTiming.Log(_logger, "s3.get", Ms(start), "rejected");
            throw;
        }
        catch
        {
            OpTiming.Log(_logger, "s3.get", Ms(start), "error");
            throw;
        }
    }

    /// <summary>Lists every key under <paramref name="prefix"/>, following continuation tokens (op <c>s3.list</c>).</summary>
    public async Task<IReadOnlyList<string>> ListAsync(string bucket, string prefix, int pageSize = 1000)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var keys = new List<string>();
            var request = new ListObjectsV2Request { BucketName = bucket, Prefix = prefix, MaxKeys = pageSize };
            ListObjectsV2Response response;
            do
            {
                response = await _client.ListObjectsV2Async(request);
                if (response.S3Objects is not null)
                    keys.AddRange(response.S3Objects.Select(o => o.Key));
                request.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated == true);
            OpTiming.Log(_logger, "s3.list", Ms(start), "ok");
            return keys;
        }
        catch
        {
            OpTiming.Log(_logger, "s3.list", Ms(start), "error");
            throw;
        }
    }

    /// <summary>
    /// Server-side copy guarded by <c>x-amz-copy-source-if-match</c> (research R-09, probe P3).
    /// A source changed since <paramref name="etag"/> was read gives
    /// <see cref="CopyOutcome.PreconditionFailed"/> (op outcome <c>rejected</c>) and writes nothing.
    /// </summary>
    public async Task<CopyOutcome> CopyIfMatchAsync(string srcBucket, string srcKey, string dstBucket, string dstKey,
        string etag, string contentType, string cacheControl)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var request = new CopyObjectRequest
            {
                SourceBucket = srcBucket,
                SourceKey = srcKey,
                DestinationBucket = dstBucket,
                DestinationKey = dstKey,
                ETagToMatch = etag,
                ContentType = contentType,
                MetadataDirective = S3MetadataDirective.REPLACE,
            };
            request.Headers.CacheControl = cacheControl;
            await _client.CopyObjectAsync(request);
            OpTiming.Log(_logger, "s3.copy", Ms(start), "ok");
            return CopyOutcome.Copied;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            OpTiming.Log(_logger, "s3.copy", Ms(start), "rejected");
            return CopyOutcome.PreconditionFailed;
        }
        catch
        {
            OpTiming.Log(_logger, "s3.copy", Ms(start), "error");
            throw;
        }
    }

    /// <summary>
    /// Best-effort delete (FR-036): runs <paramref name="delete"/>; any exception is logged as
    /// <c>upload.cleanup_failed {key} {error}</c> (error = exception type name only) and swallowed.
    /// This is the ONLY place that template is logged.
    /// </summary>
    public static async Task TryDeleteAsync(Func<Task> delete, string key, ILogger log)
    {
        try
        {
            await delete();
        }
        catch (Exception ex)
        {
            log.LogWarning("upload.cleanup_failed {key} {error}", key, ex.GetType().Name);
        }
    }

    /// <summary>Lists <paramref name="prefix"/> and deletes each key through <see cref="TryDeleteAsync"/>. Never throws.</summary>
    public async Task DeletePrefixAsync(string bucket, string prefix, ILogger log)
    {
        IReadOnlyList<string> keys = [];
        await TryDeleteAsync(async () => keys = await ListAsync(bucket, prefix), prefix, log);
        foreach (var key in keys)
            await TryDeleteAsync(() => DeleteAsync(bucket, key), key, log);
    }

    /// <summary>Returns the public CDN URL for a stored object key.</summary>
    public string PublicUrl(string key) => $"{_imageBaseUrl}/{key}";

    private static double Ms(long start) =>
        Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds, 2);

    /// <summary>The <c>X-Amz-Expires</c> seconds a presigned URL carries (0 when absent).</summary>
    private static int SignedExpirySeconds(string url)
    {
        var q = new Uri(url).Query.TrimStart('?').Split('&');
        foreach (var p in q)
            if (p.StartsWith("X-Amz-Expires=", StringComparison.Ordinal)
                && int.TryParse(p.AsSpan("X-Amz-Expires=".Length), out var s))
                return s;
        return 0;
    }
}

/// <summary>Result of <see cref="ImageStore.CopyIfMatchAsync"/>.</summary>
public enum CopyOutcome { Copied, PreconditionFailed }

/// <summary>Thrown by <see cref="ImageStore.GetAsync"/> when an object exceeds the caller's byte cap.</summary>
public sealed class ObjectTooLargeException(long length, long maxBytes)
    : Exception($"Object is {length} bytes, over the {maxBytes}-byte cap.")
{
    public long Length { get; } = length;
    public long MaxBytes { get; } = maxBytes;
}
