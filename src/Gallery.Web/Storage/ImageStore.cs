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
            var request = new GetPreSignedUrlRequest
            {
                BucketName = bucket,
                Key = key,
                ContentType = contentType,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.Add(expiry),
                Protocol = _isHttps ? Protocol.HTTPS : Protocol.HTTP,
            };
            // GetPreSignedURL is synchronous; wrap in Task to match the async contract.
            var url = await Task.FromResult(_client.GetPreSignedURL(request));
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

    /// <summary>Returns the public CDN URL for a stored object key.</summary>
    public string PublicUrl(string key) => $"{_imageBaseUrl}/{key}";

    private static double Ms(long start) =>
        Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds, 2);
}
