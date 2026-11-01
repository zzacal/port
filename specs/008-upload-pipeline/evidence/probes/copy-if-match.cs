#:package AWSSDK.S3@4.0.104.8
#:property PublishAot=false
// Probe 008-P3: does a server-side copy with x-amz-copy-source-if-match (CopyObjectRequest.ETagToMatch)
// refuse when the staged object was overwritten after validation? (closes the "re-PUT with a still-valid
// presigned URL between validation and confirm" hole). Also: ETag returned by GET and by HEAD agree.
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

var cfg = new AmazonS3Config
{
    ServiceURL = args.Length > 0 ? args[0] : "http://localhost:18080", ForcePathStyle = true, AuthenticationRegion = "auto",
    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
};
var s3 = new AmazonS3Client(new BasicAWSCredentials("local", "localsecret"), cfg);
try { await s3.PutBucketAsync("priv"); } catch { }
try { await s3.PutBucketAsync("pub"); } catch { }

async Task Put(string key, byte[] body) => await s3.PutObjectAsync(new PutObjectRequest
{ BucketName = "priv", Key = key, InputStream = new MemoryStream(body), ContentType = "image/webp", DisableDefaultChecksumValidation = true });

await Put("staging/x/320.webp", new byte[] { 1, 2, 3, 4 });
using (var g = await s3.GetObjectAsync("priv", "staging/x/320.webp"))
{
    var validatedEtag = g.ETag;
    var head = await s3.GetObjectMetadataAsync("priv", "staging/x/320.webp");
    Console.WriteLine($"GET etag={validatedEtag} HEAD etag={head.ETag} same={validatedEtag == head.ETag} len={g.ContentLength}");

    async Task<string> Copy(string dst)
    {
        try
        {
            await s3.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = "priv", SourceKey = "staging/x/320.webp", DestinationBucket = "pub", DestinationKey = dst,
                ETagToMatch = validatedEtag, ContentType = "image/webp", MetadataDirective = S3MetadataDirective.REPLACE,
            });
            return "copied";
        }
        catch (AmazonS3Exception ex) { return $"{(int)ex.StatusCode} {ex.ErrorCode}"; }
    }
    Console.WriteLine($"copy if-match (unchanged) -> {await Copy("a.webp")}");
    await Put("staging/x/320.webp", new byte[] { 9, 9, 9, 9, 9 });
    Console.WriteLine($"copy if-match (overwritten) -> {await Copy("b.webp")}");
    try { await s3.GetObjectMetadataAsync("pub", "b.webp"); Console.WriteLine("pub/b.webp EXISTS"); }
    catch (AmazonS3Exception ex) { Console.WriteLine($"pub/b.webp absent ({(int)ex.StatusCode})"); }
}
