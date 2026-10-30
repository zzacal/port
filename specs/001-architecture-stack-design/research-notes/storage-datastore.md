# Storage & Datastore Research Notes

_Covers Cloudflare R2 and MongoDB Atlas Free (formerly M0) tier facts for a single-owner photo gallery (~5,000 images, low-thousands page-views/month)._

---

## Q1: R2 Free Tier — Storage, Class A/B ops, egress

**Answer:** R2 free tier includes 10 GB-month storage, 1 million Class A ops/month, 10 million Class B ops/month. Egress to the internet is free on all R2 tiers (no charge).

**Source:** https://developers.cloudflare.com/r2/pricing/

**Quote:** "Storage: 10 GB-month / month | Class A Operations: 1 million requests / month | Class B Operations: 10 million requests / month | Egress (data transfer to Internet): Free"

**Fetch: OK**

---

## Q2: Public buckets — custom domain, caching, r2.dev warning

**Answer:** A custom domain for an R2 public bucket requires the domain to be added as a zone in the same Cloudflare account (full or partial/CNAME setup). Custom-domain access enables Cloudflare Cache, WAF, Cache Rules, etc. The `r2.dev` subdomain is a development-only URL and does not support WAF, caching, or access controls.

**Source:** https://developers.cloudflare.com/r2/buckets/public-buckets/

**Quote:** "The domain being used must have been added as a zone in the same account as the R2 bucket." and "To use features like WAF custom rules, caching, access controls, or Bot Management, you must configure your bucket behind a custom domain. These capabilities are not available when using the r2.dev development url."

**Fetch: OK**

---

## Q3: Presigned URLs (PUT), max expiry, CORS, multipart min part size

**Answer:** R2 supports presigned PUT URLs (and GET, HEAD, DELETE). The expiry range is 1 second to 7 days (604,800 seconds). CORS must be configured separately on the bucket for browser-originated presigned-URL requests to work. Multipart upload minimum part size is 5 MiB (last part may be smaller).

**Source:** https://developers.cloudflare.com/r2/api/s3/presigned-urls/ and https://developers.cloudflare.com/r2/objects/upload-objects/ (multipart details section)

**Quote (presigned expiry):** "Expiry: Timeout from 1 second to 7 days (604,800 seconds)"

**Quote (CORS):** "While presigned URLs handle authentication, you still need to configure CORS when making requests from a browser. Without a CORS policy, browser-based uploads and downloads using presigned URLs will fail, even though the presigned URL itself is valid."

**Quote (multipart part size):** "Minimum part size: 5 MiB (except for the last part)"

**Fetch: OK**

---

## Q4: Object lifecycle rules

**Answer:** R2 supports prefix-scoped lifecycle rules to delete objects after N days and to transition objects to Infrequent Access storage after N days. By default, incomplete multipart uploads are automatically aborted after 7 days; this can be changed with a custom lifecycle policy. Up to 1,000 lifecycle rules per bucket.

**Source:** https://developers.cloudflare.com/r2/buckets/object-lifecycles/

**Quote:** "Object lifecycles determine the retention period of objects uploaded to your bucket and allow you to specify when objects should transition from Standard storage to Infrequent Access storage." and "Buckets have a default lifecycle rule to expire multipart uploads seven days after initiation." and "Note that object lifecycles currently has a 1000 rule maximum."

**Fetch: OK**

---

## Q5: Class A vs Class B ops — PutObject, CopyObject, DeleteObject, ListObjects

**Answer:** `PutObject`, `CopyObject`, `ListObjects`, `CreateMultipartUpload`, `UploadPart`, `CompleteMultipartUpload`, `ListMultipartUploads`, `UploadPartCopy`, `ListParts`, `PutBucketCors`, `PutBucketLifecycleConfiguration` are all **Class A** (more expensive, mutate state). `GetObject`, `HeadObject`, `HeadBucket`, `GetBucketCors`, `GetBucketLifecycleConfiguration` are **Class B**. `DeleteObject`, `DeleteBucket`, and `AbortMultipartUpload` are **free** (no charge).

**Source:** https://developers.cloudflare.com/r2/pricing/

**Quote (Class A):** "Class A Operations include `ListBuckets`, `PutBucket`, `ListObjects`, `PutObject`, `CopyObject`, `CompleteMultipartUpload`, `CreateMultipartUpload`, `LifecycleStorageTierTransition`, `ListMultipartUploads`, `UploadPart`, `UploadPartCopy`, `ListParts`, `PutBucketEncryption`, `PutBucketCors` and `PutBucketLifecycleConfiguration`."

**Quote (Free):** "Free operations include `DeleteObject`, `DeleteBucket` and `AbortMultipartUpload`."

**Fetch: OK**

---

## Q6: Server-side CopyObject — class and use for staging→final prefix moves

**Answer:** `CopyObject` is a server-side operation (no egress cost since data stays within R2). It is a **Class A** operation, so it counts against the 1 million free Class A ops/month and costs $4.50/million thereafter. It is suitable for moving files from a staging prefix to a final prefix without downloading and re-uploading.

**Source:** https://developers.cloudflare.com/r2/pricing/

**Quote:** "Class A Operations include … `CopyObject` … $4.50 / million requests" and "Egress (data transfer to Internet): Free" (no separate charge for server-side copy).

**Fetch: OK**

---

## Q7: Atlas Free (M0) — storage, connections, ops/sec, data transfer, Azure regions

**Answer:** Free clusters (formerly M0) have 0.5 GB storage (hard limit), max 500 connections, 100 operations/second throughput limit, and 10 GB in / 10 GB out data transfer per rolling 7-day period. Azure regions are available for Free clusters, but only a subset; the page lists them but the specific region checkmarks were not captured (see Gaps). The cluster runs MongoDB 8.0.

**Source:** https://www.mongodb.com/docs/atlas/reference/free-shared-limitations/ and https://www.mongodb.com/docs/atlas/reference/microsoft-azure/

**Quote (storage):** "Free clusters limit the maximum total data storage space as follows: Free clusters: 0.5 GB Data storage space includes the number of bytes of all uncompressed BSON documents stored in all collections, plus the bytes stored in their associated indexes."

**Quote (connections):** "Free clusters can only have a maximum of 500 connections."

**Quote (throughput):** "Free clusters: 100 operations per second"

**Quote (data transfer):** "Free clusters: 10 GB in and 10 GB out per period"

**Fetch: OK**

---

## Q8: Backups on Free cluster — Atlas snapshots, mongodump allowed?

**Answer:** Atlas offers an optional paid backup subscription for Free clusters ($0.10/day, max $3.00/month) that takes daily snapshots retaining the last 4. Without this subscription, no automated backups are included. `mongodump` is permitted on Free clusters with limited options: `--dumpDbUsersAndRoles` and `--oplog` flags are not supported, and `mongodump` cannot be run against the `admin` database.

**Source:** https://www.mongodb.com/docs/atlas/backup/cloud-backup/free-cluster-backup/ and https://www.mongodb.com/docs/atlas/reference/free-shared-limitations/

**Quote (backup cost):** "Atlas bills $0.10 per day for each Free cluster with backups enabled, up to a maximum of $3.00 per month per cluster."

**Quote (mongodump restriction):** "Free clusters and Flex clusters don't support the following command line tool options: [`mongodump`] — `--dumpDbUsersAndRoles`; `--oplog`. For Free clusters, you can't run `mongorestore` or `mongodump` on the `admin` database."

**Fetch: OK**

---

## Q9: Multi-document transactions, unique indexes, change streams on Free cluster

**Answer:** Multi-document transactions are supported on Free clusters (no restriction listed in the official limits page). Unique indexes are a standard MongoDB feature with no Free-cluster restriction mentioned. Change streams are supported but with a filtering limitation: only strings and regular expressions (not operators like `$in`) can be used in database-namespace (`ns`) filters; collection-name filtering is unrestricted.

**Source:** https://www.mongodb.com/docs/atlas/reference/free-shared-limitations/

**Quote (change streams):** "For Free clusters, you can use only strings and regular expressions in filters on database names (namespace `ns` fields) in change streams. You can't use commands, such as `$in`, in database namespace filters. This limit doesn't apply to filtering on collection names in change streams."

**Note:** The limits page does not mention restrictions on multi-document transactions or unique indexes for Free clusters. `$where` and server-side JavaScript (map-reduce) are explicitly unsupported.

**Fetch: OK**

---

## Q10: Tier rename, idle-pause behaviour

**Answer:** MongoDB Atlas renamed `M0` to "Free cluster" in the Atlas UI; the documentation now uses "Free cluster" throughout but notes the old `M0` name. Free clusters are paused automatically after **30 days of inactivity** (zero connections). They can be resumed at any time unless the paused cluster was on an old version that can't be restored. Free clusters never expire unless manually deleted.

**Source:** https://www.mongodb.com/docs/atlas/reference/free-shared-limitations/

**Quote (rename):** "Free clusters were formerly known as `M0` clusters. The Atlas documentation uses the term 'Free cluster' to match the cluster tier's name in the Atlas user interface."

**Quote (auto-pause):** "Atlas automatically pauses Free clusters after 30 days of inactivity where there are zero connections to the cluster. You can resume the cluster at any time unless the paused cluster was running on an old version that Atlas can't restore to the current version."

**Fetch: OK**

---

## Gaps

1. **Q7 — Specific Azure regions supporting Free clusters**: The `microsoft-azure` page table lists all Azure regions with a `Free Support` column, but the markdown extraction stripped the checkmark/dash symbols. The page text confirms that only _a subset_ of Azure regions support Free clusters, but the exact region list was not captured. Recommend visiting https://www.mongodb.com/docs/atlas/reference/microsoft-azure/ directly or using a browser to inspect the table.

2. **Q9 — Multi-document transaction size/time limits on Free clusters**: The limits page does not call out transaction-specific caps for Free clusters, but Free clusters do have a 100 ops/second throttle and 32 MB sort memory limit which may indirectly constrain large transactions. No explicit documentation found confirming or denying a max transaction lifetime setting on Free clusters (M10+ allows configuring this).

3. **Q3 — CORS configuration syntax**: The CORS page was identified but not fully extracted. The structure of the CORS JSON (`AllowedOrigins`, `AllowedMethods`, `AllowedHeaders`, `MaxAgeSeconds`) follows AWS S3 convention; confirm via https://developers.cloudflare.com/r2/buckets/cors/ for exact R2-specific fields.

---

## Q11: R2 API token scope (added for constitution 4.0.0)

**Answer:** An R2 API token with Object Read & Write or Object Read permission can be scoped to a set of buckets. It yields an S3 Access Key ID and Secret Access Key. Admin permissions cover bucket configuration and are not bucket-scoped.

**Source:** https://developers.cloudflare.com/r2/api/tokens/

**Quote:** "(Optional) If you select the **Object Read and Write** or **Object Read** permissions, you can scope your token to a set of buckets." and "Object Read & Write | Allows the ability to read, write, and list objects in specific buckets."

**Fetch: OK** (2026-10-30)

---

## Q12: R2 custom domain, r2.dev, and the cache (added for constitution 4.0.0)

**Answer:** R2 buckets are private by default. A custom domain on a zone in the same account makes the bucket public through Cloudflare's cache; Smart Tiered Cache puts the upper tier next to the bucket. The r2.dev URL is independent of the custom domain and can be disabled. Custom-domain access does not require r2.dev.

**Source:** https://developers.cloudflare.com/r2/buckets/public-buckets/ and https://developers.cloudflare.com/cache/interaction-cloudflare-products/r2/

**Quote:** "Enabling custom domains does not require enabling `r2.dev` access." and "Disabling public development URL access removes your bucket's exposure through the `r2.dev` subdomain." and "By default, R2 buckets are private. To serve R2 objects through Cloudflare's cache, you need to attach a domain name you control — called a Custom Domain — to the bucket."

**Fetch: OK** (2026-10-30)
