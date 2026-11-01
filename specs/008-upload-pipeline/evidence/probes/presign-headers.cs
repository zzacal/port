#:package AWSSDK.S3@4.0.104.8
// Probe 008-P1/P2: which headers does an AWSSDK.S3 4.0.104.8 presigned PUT sign, with the repo's
// S3ClientFactory settings, and how does S3Proxy treat expiry / content-type / extra headers?
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using System.Net;
using System.Web;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:18080";
var cfg = new AmazonS3Config
{
    ServiceURL = endpoint,
    ForcePathStyle = true,
    AuthenticationRegion = "auto",
    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
};
var client = new AmazonS3Client(new BasicAWSCredentials("local", "localsecret"), cfg);
try { await client.PutBucketAsync("priv"); } catch { }

string Presign(string key, string ct, TimeSpan exp, Action<GetPreSignedUrlRequest>? tweak = null)
{
    var r = new GetPreSignedUrlRequest
    {
        BucketName = "priv", Key = key, ContentType = ct, Verb = HttpVerb.PUT,
        Expires = DateTime.UtcNow.Add(exp), Protocol = endpoint.StartsWith("https") ? Protocol.HTTPS : Protocol.HTTP,
    };
    tweak?.Invoke(r);
    return client.GetPreSignedURL(r);
}

void Describe(string label, string url)
{
    var q = HttpUtility.ParseQueryString(new Uri(url).Query);
    var names = string.Join(",", q.AllKeys.OrderBy(k => k));
    Console.WriteLine($"{label}: signedHeaders={q["X-Amz-SignedHeaders"]} expires={q["X-Amz-Expires"]} queryKeys=[{names}]");
}

var http = new HttpClient();
async Task<int> Put(string url, string? ct, Action<HttpRequestMessage>? tweak = null)
{
    var m = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) };
    if (ct != null) m.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ct);
    tweak?.Invoke(m);
    var resp = await http.SendAsync(m);
    return (int)resp.StatusCode;
}

var u1 = Presign("staging/b/i/a/master.jpg", "image/jpeg", TimeSpan.FromMinutes(15));
Describe("default", u1);
var u2 = Presign("staging/b/i/a/320.avif", "image/avif", TimeSpan.FromMinutes(15), r => r.Headers.CacheControl = "public");
Describe("with-cache-control(negative)", u2);
var u3 = Presign("staging/b/i/a/320.webp", "image/webp", TimeSpan.FromMinutes(15), r => r.Metadata.Add("x", "y"));
Describe("with-meta(negative)", u3);

Console.WriteLine($"PUT correct ct            -> {await Put(u1, "image/jpeg")}");
Console.WriteLine($"PUT wrong ct (image/png)  -> {await Put(Presign("staging/b/i/a/m2.jpg", "image/jpeg", TimeSpan.FromMinutes(5)), "image/png")}");
Console.WriteLine($"PUT no ct                 -> {await Put(Presign("staging/b/i/a/m3.jpg", "image/jpeg", TimeSpan.FromMinutes(5)), null)}");
Console.WriteLine($"PUT extra unsigned header -> {await Put(Presign("staging/b/i/a/m4.jpg", "image/jpeg", TimeSpan.FromMinutes(5)), "image/jpeg", m => m.Headers.Add("x-amz-meta-z", "1"))}");
var shortUrl = Presign("staging/b/i/a/exp.jpg", "image/jpeg", TimeSpan.FromSeconds(1));
await Task.Delay(3000);
Console.WriteLine($"PUT after expiry (1s, waited 3s) -> {await Put(shortUrl, "image/jpeg")}");
var getResp = await http.GetAsync(Presign("staging/b/i/a/m5.jpg", "image/jpeg", TimeSpan.FromMinutes(5)));
Console.WriteLine($"GET on PUT url            -> {(int)getResp.StatusCode}");
