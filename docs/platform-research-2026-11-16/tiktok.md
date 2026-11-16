# TikTok capabilities research (official docs only), read 2026-11-16

Legend: CONFIRMED = stated on an official page I opened (URL cited). UNVERIFIED = docs silent, conflicting, or page not opened. BLOCKED = unsupported/closed/needs approval we lack.

Method note: pages on business-api.tiktok.com are JS-rendered. I read them in a real browser (full text). Pages on developers.tiktok.com were read either in the browser or via a fetch tool; where a quote came only from a fetch-tool summary and I did not re-read it in the browser, I say "(fetch summary)". Quotes under 25 words.

Doc dates seen: developers.tiktok.com pages "Last updated August 4, 2026" (Content Posting, guidelines) and "November 5, 2026" (OAuth). TikTok Developer Terms "Last modified: Dec 26, 2025" (fetch summary).

---------------------------------------------------------------------------
## 0. HEADLINE: TWO SEPARATE PRODUCTS

| Question | Answer |
|---|---|
| Does developers.tiktok.com (Login Kit, Content Posting API, Display API) expose comment read/reply? | No. The scope reference lists 23 scopes; none are comment scopes (closest: video.list, video.manage, video.publish, video.upload). https://developers.tiktok.com/doc/tiktok-api-scopes (fetch summary). Research API has a comment list but it is research-only (research.data.basic), not for own-account moderation. |
| Which product exposes organic comment read/reply? | **TikTok API for Business -> "Organic API" -> "Accounts API"** (business-api.tiktok.com), permission group "TikTok Accounts" (scope ID 18000000), sub-permission "Business Comment" (18030000). |
| Is it open to third-party SaaS? | Yes in principle, but gated: company developer registration, app review, and (since 2026-03-20) an "Accounts API Access Application Form". See section 6. |

---------------------------------------------------------------------------
## 1. PUBLISHING

### 1A. Product 1: Content Posting API (developers.tiktok.com, base https://open.tiktokapis.com)

| Action | Endpoint | Scope/permission | Status | Quote + URL |
|---|---|---|---|---|
| Query creator info (mandatory before Direct Post) | POST /v2/post/publish/creator_info/query/ | video.publish | CONFIRMED | "you must first use the Query Creator Info endpoint to get the target creator's latest information" https://developers.tiktok.com/doc/content-posting-api-get-started |
| Direct Post, video | POST /v2/post/publish/video/init/ | video.publish | CONFIRMED | "Post a video ... source parameter to FILE_UPLOAD ... PULL_FROM_URL" https://developers.tiktok.com/doc/content-posting-api-get-started |
| Direct Post, photo | POST /v2/post/publish/content/init/ with post_mode=DIRECT_POST, media_type=PHOTO | video.publish | CONFIRMED | "Use /v2/post/publish/content/init/ to upload photos" https://developers.tiktok.com/doc/content-posting-api-get-started |
| Upload (draft to inbox), video | POST /v2/post/publish/inbox/video/init/ | video.upload | CONFIRMED | "Requires scope video.upload"; user must "click on inbox notifications to continue the editing flow" (fetch summary) https://developers.tiktok.com/doc/content-posting-api-reference-upload-video |
| Upload (draft to inbox), photo | POST /v2/post/publish/content/init/ with post_mode=MEDIA_UPLOAD | video.upload | CONFIRMED | "MEDIA_UPLOAD sends the photos to the user's inbox" (fetch summary); also error app_version_check_failed, needs TikTok app 31.8+ (fetch summary) https://developers.tiktok.com/doc/content-posting-api-reference-photo-post |
| Chunked binary upload | PUT to returned upload_url | n/a | CONFIRMED | upload_url valid one hour; Content-Type video/mp4, video/quicktime or video/webm; Content-Range required. https://developers.tiktok.com/doc/content-posting-api-reference-direct-post (fetch summary) |
| Poll status | POST /v2/post/publish/status/fetch/ body {publish_id} | token must be same user | CONFIRMED | "Each user access_token is limited to 30 requests per minute." (fetch summary) https://developers.tiktok.com/doc/content-posting-api-reference-get-video-status |
| Webhook for publish status instead of polling | Webhook URL configured on the app in the developer portal | n/a | CONFIRMED | Page lists "Fetch Status endpoint" and "Content Posting webhooks". Events: post.publish.failed, post.publish.complete, post.publish.inbox_delivered, post.publish.publicly_available, post.publish.no_longer_publicaly_available (sic). Read in browser. https://developers.tiktok.com/doc/content-posting-api-reference-get-video-status |
| Status values | n/a | n/a | CONFIRMED | PROCESSING_UPLOAD, PROCESSING_DOWNLOAD, SEND_TO_USER_INBOX, PUBLISH_COMPLETE, FAILED (fetch summary), same URL |
| FILE_UPLOAD vs PULL_FROM_URL | source field | n/a | CONFIRMED | Guidelines: "If video resources are already on API Clients' servers, do not use FILE_UPLOAD; use PULL_FROM_URL instead." https://developers.tiktok.com/doc/content-sharing-guidelines |
| PULL_FROM_URL ownership rule | Manage URL properties in app portal | n/a | CONFIRMED | "The supplied URL must be under the path of a domain or URL prefix API Clients have ownership on." https://developers.tiktok.com/doc/content-sharing-guidelines ; error 403 url_ownership_unverified https://developers.tiktok.com/doc/content-posting-api-reference-direct-post |
| Domain vs URL-prefix verification detail | n/a | n/a | CONFIRMED (fetch summary) | Verifying static.example.com covers video.static.example.com but not example.com; URL-prefix must be https, host a domain not IP, 3xx invalid. https://developers.tiktok.com/doc/content-posting-api-media-transfer-guide |
| Unaudited client = private only | enforced at init | n/a | CONFIRMED | "all content uploaded via this endpoint will be restricted to private viewing mode." Unaudited: "up to 5 users to post in a 24 hour window" and accounts "must be set to private". https://developers.tiktok.com/doc/content-sharing-guidelines |
| Unaudited error | 403 unaudited_client_can_only_post_to_private_accounts | n/a | CONFIRMED | "The publish attempt will be blocked when calling /publish/video/init/." https://developers.tiktok.com/doc/content-posting-api-reference-direct-post |
| Audit process | audit "to verify compliance with our Terms of Service" | n/a | PARTLY CONFIRMED | Doc states audit is required and that an "audit application form" supplies usage estimates (creator cap). I did NOT find the audit form, checklist, or review time. Audit duration = UNVERIFIED. https://developers.tiktok.com/doc/content-sharing-guidelines |
| Audit intent rules (WARNING for a SaaS) | n/a | n/a | CONFIRMED | "Not acceptable: A utility tool to help upload contents to the account(s) you or your team manages." Also clients "must not be limited to test applications". https://developers.tiktok.com/doc/content-sharing-guidelines |
| Caps for audited clients too | n/a | n/a | CONFIRMED | "typically around 15 posts per day/ creator account ... shared across all API Clients"; plus a 24-hour active creator cap from audit form usage estimates. https://developers.tiktok.com/doc/content-sharing-guidelines |
| Required UX (affects an auto-send design) | n/a | n/a | CONFIRMED | Privacy status "no default value", users "must manually select"; comment/duet/stitch "none should be checked by default"; content must be previewed; send content only "after the user has expressly consent". https://developers.tiktok.com/doc/content-sharing-guidelines |
| Commercial disclosure fields | brand_content_toggle, brand_organic_toggle | video.publish | CONFIRMED | Declarations must read e.g. "By posting, you agree to TikTok's Music Usage Confirmation". https://developers.tiktok.com/doc/content-sharing-guidelines |
| Content Posting API daily/pending caps | errors | n/a | CONFIRMED | Upload: "There may be at most 5 pending shares within any 24-hour period" (fetch summary). Errors spam_risk_too_many_posts, reached_active_user_cap. https://developers.tiktok.com/doc/content-posting-api-reference-upload-video |

### 1B. Product 2: Business API "Accounts Video Publishing" (base https://business-api.tiktok.com/open_api/v1.3, header Access-Token)

| Action | Endpoint | Scope/permission | Status | Quote + URL |
|---|---|---|---|---|
| Publish public video | POST /business/video/publish/ (body business_id, video_url, post_info) | Permission "Video Publish" id 7050363942433996801 under "Business Content" 7044139020472942593 | CONFIRMED | "Use this endpoint to publish a public video post to an owned TikTok Account." https://business-api.tiktok.com/portal/docs/publish-a-public-video-post-to-an-owned-account/v1.3 ; ids https://business-api.tiktok.com/portal/docs/permission-scope/v1.3 |
| Publish photo post | POST /business/photo/publish/ | same group | CONFIRMED | "A list of up to 35 publicly accessible HTTP(s) URLs for the photo content" https://business-api.tiktok.com/portal/docs/publish-a-photo-post-to-an-owned-account/v1.3 |
| Source is URL only (no binary upload endpoint documented) | video_url / photo_images | n/a | CONFIRMED | "A publicly accessible HTTP(s) URL for the video content to be published - with a minimum recommended TTL of 30 minutes." (no FILE_UPLOAD documented on the page) https://business-api.tiktok.com/portal/docs/publish-a-public-video-post-to-an-owned-account/v1.3 |
| Verified URL property required | POST /business/property/add/ then /business/property/verify/ | n/a | CONFIRMED | "you can no longer publish videos using unverified video URLs." Domain: DNS TXT record; URL prefix: upload signature file; 3xx not followed. https://business-api.tiktok.com/portal/docs/manage-url-properties/v1.3 |
| Draft to inbox | same endpoint with post_info.upload_to_draft=true (video) / post_info.is_draft=true (photo) | needs video.upload on the user token | CONFIRMED | "you can find the draft through the inbox notification within the TikTok app." https://business-api.tiktok.com/portal/docs/publish-a-public-video-post-to-an-owned-account/v1.3 |
| Poll status | GET /business/publish/status/?business_id=&publish_id= (publish_id = share_id from publish response) | same | CONFIRMED | Statuses PROCESSING_DOWNLOAD, PUBLISH_COMPLETE, FAILED, SEND_TO_USER_INBOX; "Post IDs require up to three minutes of processing time." https://business-api.tiktok.com/portal/docs/get-the-publishing-status-of-a-tiktok-post/v1.3 |
| Webhook instead of polling | /business/webhook/update/ with event_type=VIDEO | "Business Content" permission | CONFIRMED | Events post.publish.failed / .complete / .publicly_available / .no_longer_publicly_available; "triggered only for posts that are published through /business/video/publish/ or /business/photo/publish/." https://business-api.tiktok.com/portal/docs/post-publishing-events/v1.3 |
| Per-account publish cap | n/a | n/a | CONFIRMED | "six video posts per minute with an upper limit of 15 per day." Same for photos. https://business-api.tiktok.com/portal/docs/publish-a-public-video-post-to-an-owned-account/v1.3 |
| Audit / private-only restriction on this route | n/a | n/a | UNVERIFIED | The Business API publish pages mention NO audit and NO unaudited private-only rule. I cannot say none exists; the docs are silent. Needs a live call. |
| AI label | post_info.is_ai_generated (video, photo) | n/a | CONFIRMED | "labeled as Creator labeled as AI-generated once posted and can't be changed." https://business-api.tiktok.com/portal/docs/publish-a-public-video-post-to-an-owned-account/v1.3 |
| Brand labels (required fields) | is_brand_organic, is_branded_content (both marked Required) | n/a | CONFIRMED | "A 'Promotional content' label will be attached" / "'Paid partnership' label". Same URL. |
| Account status of video is "public" only | n/a | n/a | CONFIRMED | Endpoint titled "Publish a public video post"; photo post requires post_info.privacy_level (enum incl. SELF_ONLY). https://business-api.tiktok.com/portal/docs/publish-a-photo-post-to-an-owned-account/v1.3 |
| Publish via MCP server | account_user_post_publish | n/a | BLOCKED (for MCP route) | "not supported through standard MCP authorization yet." https://business-api.tiktok.com/portal/docs/tiktok-social-mcp-server/v1.3 |

Doc inconsistency to remember: post.publish.failed reason duration_check_failed says "min 3 seconds long, max 60 seconds long" while the publish page says max 600 seconds. Use /business/video/settings/ max_video_post_duration_sec (CONFIRMED to exist on the publish page) per account. https://business-api.tiktok.com/portal/docs/post-publishing-events/v1.3

---------------------------------------------------------------------------
## 2. MEDIA RULES

| Item | Content Posting API (developers) | Business API publish | Status | Quote + URL |
|---|---|---|---|---|
| Video formats | MP4 (recommended), WebM, MOV | .mp4, .mov, .webm | CONFIRMED | Business: "The audio-video content must be contained in .mp4, .mov or .webm format." https://business-api.tiktok.com/portal/docs/publish-a-public-video-post-to-an-owned-account/v1.3 ; developers (fetch summary) https://developers.tiktok.com/doc/content-posting-api-media-transfer-guide |
| Codecs | H.264 (rec.), H.265, VP8, VP9 | not stated | CONFIRMED / UNVERIFIED (Business) | developers: same media-transfer-guide (fetch summary) |
| Max size | 4 GB | 1 GB | CONFIRMED | Business: "The maximum video size is 1 GB." |
| Duration | up to 10 min via API; per-creator max in creator_info (max_video_post_duration_sec) | min 3 s, max 600 s; per-account via /business/video/settings/ | CONFIRMED | Business: "minimum video duration must be 3 seconds, and the maximum video duration is 600 seconds." |
| Resolution | 360 to 4,096 px each side | min 360 px each side; no max stated | CONFIRMED | Business: "minimum height and minimum width of the video must be 360 pixels." |
| Frame rate | 23 to 60 FPS | 23 to 60 FPS | CONFIRMED | Business: "minimum frame rate ... 23 FPS, and maximum frame rate is 60 FPS." |
| Photo formats | WebP, JPEG | JPG, JPEG, WebP | CONFIRMED | Business: "File type: JPG, JPEG, or WebP." https://business-api.tiktok.com/portal/docs/publish-a-photo-post-to-an-owned-account/v1.3 . PNG is NOT allowed for post photos (PNG only for covers/comment-reply images). |
| Photo max count | 35 | 35 | CONFIRMED | "up to 35 URLs" (fetch summary) https://developers.tiktok.com/doc/content-posting-api-reference-photo-post |
| Photo size/dimension | 20 MB each; max 1080p | 20 MB each; max 1080 x 1920 or 1920 x 1080 | CONFIRMED | Business: "File size: within 20 MB for each image." |
| Titles/captions | video title 2,200 UTF-16 runes; photo title 90, description 4,000 | video caption 2,200 (30 mentions max); photo title 90, caption 4,000 | CONFIRMED | developers (fetch summary); Business pages as above |
| Chunking | chunk 5-64 MB (final chunk up to 128 MB), 1-1,000 chunks, in order; under 5 MB = single chunk | n/a (URL pull only documented) | CONFIRMED (fetch summary) | "Each chunk must be at least 5 MB but no greater than 64 MB" https://developers.tiktok.com/doc/content-posting-api-media-transfer-guide |
| PULL_FROM_URL | HTTPS, no redirect, must stay up for the download, one-hour timeout | HTTP(s), TTL >= 30 min recommended; download timeout one hour (video_pull_failed) | CONFIRMED | Business: failure reason "terminated since it cannot be completed within the one hour timeout." https://business-api.tiktok.com/portal/docs/post-publishing-events/v1.3 |
| Domain verification | required (Content Posting) | required (Business) | CONFIRMED | see section 1 |
| Watermarks | do not add promotional marks | n/a | CONFIRMED | "should not superimpose or otherwise include any brand name, logo, watermark" https://developers.tiktok.com/doc/content-sharing-guidelines |
| Transcoding | n/a | quality may drop | CONFIRMED | "you might notice a decrease in resolution and bit rate" (publish page) |

SaaS implication: our own domain (verified once) must host media for PULL_FROM_URL, because TikTok pulls only from verified domains. Whether ONE verified domain is accepted for posts to MANY different customer accounts is UNVERIFIED (docs say property is added "to an ad account" in the Business API).

---------------------------------------------------------------------------
## 3. COMMENTS

Product: Business API Organic "Accounts API" (permission group "TikTok Accounts" > "Business Comment"). Base https://business-api.tiktok.com/open_api/v1.3 ; header Access-Token (a TikTok-account access token from /tt_user/oauth2/token/, NOT an advertiser token).

| Action | Endpoint | Scope/permission | Status | Quote + URL |
|---|---|---|---|---|
| List comments on an owned video (top-level, optional replies) | GET /business/comment/list/ params business_id, video_id, comment_ids, include_replies, status, sort_field, sort_order, cursor, max_count (<=30) | permission 18030100 "Get Business Comment"; user scope comment.list | CONFIRMED | "all the comments ... both public and hidden - that have been created against a specific organic video posted by an owned TikTok Account." https://business-api.tiktok.com/portal/docs/get-comments-on-an-owned-video/v1.3 ; ids https://business-api.tiktok.com/portal/docs/permission-scope/v1.3 |
| List all replies to a comment | GET /business/comment/reply/list/ (business_id, video_id, comment_id, cursor, max_count <=30) | 18030100 | CONFIRMED | "/business/comment/reply/list/ currently does not support getting replies to a hidden comment." https://business-api.tiktok.com/portal/docs/get-all-replies-to-a-comment/v1.3 |
| Reply to a comment | POST /business/comment/reply/create/ (business_id, video_id, comment_id, text or image_uri) | 18030200 "Manage Business Comment" | CONFIRMED | text "Length limit: 1,200 characters (UTF-8 encoding)." https://business-api.tiktok.com/portal/docs/reply-to-an-existing-comment-on-an-owned-video/v1.3 |
| Create top-level comment | POST /business/comment/create/ | 18030200 | CONFIRMED | listed https://business-api.tiktok.com/portal/docs/manage-comments-on-owned-tiktok-videos/v1.3 |
| Like/unlike | POST /business/comment/like/ (action LIKE/UNLIKE) | 18030200 | CONFIRMED | same |
| Hide/unhide | POST /business/comment/hide/ (action HIDE/UNHIDE) | 18030200 | CONFIRMED | same |
| Delete | POST /business/comment/delete/ | 18030200 | CONFIRMED | same |
| Upload comment image | POST /business/comment/image/upload/ | 18030200 | CONFIRMED (endpoint named, not opened) | referenced on reply page |
| Find video IDs | GET /business/video/list/ (item_id) | "Business Media" 18020000 | CONFIRMED | "Use /business/video/list/ to obtain the ID of an owned TikTok video through the returned item_id." https://business-api.tiktok.com/portal/docs/manage-comments-on-owned-tiktok-videos/v1.3 |
| Spam risk when replying | n/a | n/a | CONFIRMED | "avoid posting a high volume of comments with largely similar content within a short timeframe." Spam-flagged comments get hidden. https://business-api.tiktok.com/portal/docs/reply-to-an-existing-comment-on-an-owned-video/v1.3 |
| Exact OAuth scope strings | comment.list ; comment.list.manage | n/a | PARTLY CONFIRMED | `comment.list` is defined: "Read the comments and replies of your in-app content" https://business-api.tiktok.com/portal/docs/comment-update-event/v1.3 . `comment.list.manage` appears only in the example scope string of the token response https://business-api.tiktok.com/portal/docs/accounts-api-authentication/v1.3 ; its mapping to write-comment is UNVERIFIED (no page I read defines it). |
| Account type needed (personal vs Business vs verified) for the comment endpoints | n/a | n/a | **UNVERIFIED** | Accounts Overview defines 3 account types (General/Personal, Advanced Access, Verified Business) but never says which the comment endpoints require. https://business-api.tiktok.com/portal/docs/accounts-api-overview/v1.3 . The Business Messaging API does require Advanced Access or Verified (not opened; snippet only), so do not assume the same for comments. Needs a live call with a personal account. |
| Distinct: Ad comments | /comment/list/, /comment/post/, /comment/status/update/, /comment/delete/ (scope group 15000000 "Ad Comments") | Ad Comments | CONFIRMED | These are for ads, separate from organic. https://business-api.tiktok.com/portal/docs/permission-scope/v1.3 |
| Consumer developer platform comments | none | n/a | BLOCKED (not offered) | No comment scope in the 23-scope list. https://developers.tiktok.com/doc/tiktok-api-scopes (fetch summary) |
| Third-party SaaS allowed? | n/a | n/a | CONFIRMED (permitted use) | Authorized use: "Manage the organic presence of brands or creators' owned accounts on TikTok, including ... Moderating comments." Also "enables advertisers' third party partners ... publishing capabilities within their own platforms." https://business-api.tiktok.com/portal/docs/accounts-api-overview/v1.3 |
| AI agent comment replies | TikTok Social MCP Server (30+ tools incl. reply to comment) | n/a | CONFIRMED | "Drafts authentic, context-aware replies for approval." and "AI agents execute complete social workflows without human intervention at every step." https://business-api.tiktok.com/portal/docs/tiktok-social-mcp-server/v1.3 |

---------------------------------------------------------------------------
## 4. NEW-ITEM DELIVERY (no polling)

### 4A. Business API webhooks (the useful ones)

| Action | Endpoint | Scope/permission | Status | Quote + URL |
|---|---|---|---|---|
| Subscribe to comment events | POST /business/webhook/update/ body {app_id, secret, event_type:"COMMENT", callback_url, optional item_list[]} | app needs "TikTok Accounts > Business Comment" | CONFIRMED | "Optionally, you can specify the specific TikTok posts ... via item_list." https://business-api.tiktok.com/portal/docs/subscribe-to-tiktok-account-webhook-events-via-webhooks-api/v1.3 |
| Event comment.update | pushed to callback_url | user must authorize comment.list | CONFIRMED | "Fired within five minutes of a comment or reply being created, deleted, or the comment visibility settings being modified" https://business-api.tiktok.com/portal/docs/comment-update-event/v1.3 |
| comment.update payload | content JSON-string with comment_id, video_id, parent_comment_id (replies), comment_type (comment/reply), comment_action (insert, delete, set_to_hidden, set_to_friends_only, set_to_public), timestamp, unique_identifier, text | n/a | CONFIRMED | same page. Envelope: client_key, event, create_time, user_openid, content. Note: payload carries commenter text + opaque id, not username. |
| Covers manually posted videos | n/a | n/a | CONFIRMED | "applies to both posts published through /business/video/publish/ or /business/photo/publish/ and posts manually published through the TikTok App." (same page) |
| Authorization must stay valid | n/a | n/a | CONFIRMED | "If the authorization expires or is revoked, you will no longer receive comment.update events" (same page) |
| Public posts only | n/a | n/a | CONFIRMED | "any public video under an owned TikTok account" https://business-api.tiktok.com/portal/docs/subscribe-to-tiktok-account-webhook-events-via-webhooks-api/v1.3 |
| Subscribe to publish-status events | same endpoint, event_type "VIDEO" | "Business Content" | CONFIRMED | same subscribe page |
| Event categories | exactly two: post publishing, comment updates | n/a | CONFIRMED | https://business-api.tiktok.com/portal/docs/webhook-event-types/v1.3 |
| Signature verification | header TikTok-Signature: t=<unix seconds>,s=<hex> | app secret | CONFIRMED | "compute an HMAC using the SHA256 hash function, using your application secret as the key"; signed string = timestamp + "." + stringified JSON body; hex-encode; compare. https://business-api.tiktok.com/portal/docs/webhook-verification/v1.3 . Sample code uses acceptableDelay = 5 seconds (sample only; no official tolerance stated). |
| Delivery guarantees / retries for the Business webhook | n/a | n/a | UNVERIFIED | Retry policy appears on the developers.tiktok.com webhook page (below), not on the Business API pages I read. Do not assume it applies. |
| Webhook set per app, not per customer | one callback_url per app (app_id, secret in body) | n/a | CONFIRMED (inferred from request shape) | request body has app_id/secret/callback_url only; events carry user_openid to route to tenant. |
| New comment on a NEW video of a customer | comment.update on all public posts | n/a | CONFIRMED | subscribing without item_list = "all posts" (example "Subscribe to the comment update event for all posts"). |

### 4B. developers.tiktok.com webhooks (Content Posting / Login Kit)

| Action | Endpoint | Scope | Status | Quote + URL |
|---|---|---|---|---|
| Register callback | portal config on the app | n/a | CONFIRMED (fetch summary) | "callback URL should be registered in the client application on TikTok Developer Portal"; HTTPS required; respond 200 immediately. https://developers.tiktok.com/doc/webhooks-overview |
| Retry | n/a | n/a | CONFIRMED (fetch summary) | "TikTok retries the delivery of event notification for up to 72 hours using exponential backoff." Same URL. At-least-once; handlers must be idempotent. |
| Content Posting events | post.publish.failed / .complete / .inbox_delivered / .publicly_available / .no_longer_publicaly_available | n/a | CONFIRMED | status page, read in browser: https://developers.tiktok.com/doc/content-posting-api-reference-get-video-status |
| CONFLICT | webhooks-events page lists only authorization.removed, video.upload.failed, video.publish.completed, portability.download.ready | n/a | UNVERIFIED which list is current | The Events page (fetch summary, asked twice) does not list post.publish.*; the Content Posting status page does. Treat the status page as authoritative for Content Posting but confirm by live test. https://developers.tiktok.com/doc/webhooks-events |
| Signature | header TikTok-Signature, t=,s= ; HMAC-SHA256, key = client_secret, payload = timestamp + "." + raw JSON | client_secret | CONFIRMED (fetch summary) | "An HMAC with the SHA256 hash function is computed with your client_secret as the key." https://developers.tiktok.com/doc/webhooks-verification . No numeric tolerance. |
| authorization.removed | pushed when user deauthorizes | n/a | CONFIRMED (fetch summary) | "Fired when the user's account deauthorized from your application." https://developers.tiktok.com/doc/webhooks-events |
| Comment webhooks on developers.tiktok.com | none | n/a | BLOCKED (not offered) | No comment events listed on webhooks-overview or webhooks-events. |

### 4C. On-demand sync cost (if a person opens the screen)

| Item | Status | Detail + URL |
|---|---|---|
| Pull flow | CONFIRMED | /business/video/list/ -> /business/comment/list/ per video -> /business/comment/reply/list/ per comment. Page size max 30. https://business-api.tiktok.com/portal/docs/get-comments-on-an-owned-video/v1.3 |
| Per-account endpoint limit | CONFIRMED | "The rate limit per authorized TikTok account and Accounts API endpoint is 40 queries per minute (QPM)." https://business-api.tiktok.com/portal/docs/accounts-api-rate-limits/v1.3 |
| Per-app combined limit | CONFIRMED | Basic 600 QPM; Advanced/Premium/Ultimate 1,000 QPM (same page) |
| Depth cap | CONFIRMED | "If the number of comments on the owned video exceeds 500, the first 500 comments will be returned according to the specified sorting", rest reversed by likes, may duplicate. (comment list page) |
| No "since" filter | CONFIRMED (absence) | List params have cursor, no created-after filter; dedupe on comment_id. |
| Hidden-comment replies | CONFIRMED | reply list "does not support getting replies to a hidden comment". |

Conclusion: new comments CAN be received without polling via comment.update (within five minutes, not instant). A screen-open sync is only needed to backfill history and for reconciliation.

---------------------------------------------------------------------------
## 5. OAuth

### 5A. Business API Accounts API (the one needed for comments)

| Action | Endpoint | Scope/permission | Status | Quote + URL |
|---|---|---|---|---|
| Authorize | https://www.tiktok.com/v2/auth/authorize?{params} (the "TikTok account holder authorization URL" from the app's Basic Information page) | app must have "TikTok Accounts" permission | CONFIRMED | "The URL can be found by navigating to My Apps > App Detail > Basic Information > TikTok account holder authorization URL." https://business-api.tiktok.com/portal/docs/accounts-api-authorization/v1.3 |
| Redirect URL rules | up to 10 per app; absolute, must end with "/", https, no port, no query, no #, 10-512 chars | n/a | CONFIRMED | same page. Pass per-tenant data via `state`. |
| Auth code | n/a | n/a | CONFIRMED | "auth_code is only valid for 10 minutes and can be used only once." same page |
| Exchange | POST /tt_user/oauth2/token/ {client_id, client_secret, grant_type=authorization_code, auth_code, redirect_uri} | n/a | CONFIRMED | https://business-api.tiktok.com/portal/docs/accounts-api-authentication/v1.3 |
| Access token life | n/a | n/a | CONFIRMED | "The TikTok account access token is valid for one day." (expires_in 86400) same page |
| Refresh token life | n/a | n/a | CONFIRMED | "The TikTok account refresh token is valid for one year." same page |
| Refresh | POST /tt_user/oauth2/refresh_token/ {client_id, client_secret, grant_type=refresh_token, refresh_token} | n/a | CONFIRMED | same page |
| Refresh-token rotation | n/a | n/a | UNVERIFIED | Page does not say whether a new refresh_token is issued. Example refresh response shows refresh_token_expires_in 28484364 (a shrinking remainder), which hints the SAME refresh token persists, but this is inference. Store whatever is returned each time. |
| Revoke | POST /tt_user/oauth2/revoke/ | n/a | CONFIRMED | same page |
| PKCE | n/a | n/a | UNVERIFIED / not described | Business API authorization/authentication pages describe a confidential-client flow with client_secret and redirect_uri; PKCE is not mentioned. |
| Inspect granted scopes | /tt_user/token_info/get/ | n/a | CONFIRMED | "pass the generated access token to /tt_user/token_info/get/." same page |
| User can revoke any time | n/a | n/a | CONFIRMED | "can revoke the authorization at any time from within the TikTok app." authorization page |
| Scope string seen in example | message.list.manage, video.list, video.insights, user.insights, user.info.basic, comment.list, comment.list.manage, video.upload, video.publish, user.account.type, ... | n/a | CONFIRMED (example only) | https://business-api.tiktok.com/portal/docs/accounts-api-authentication/v1.3 |
| MCP authorization lifespan | n/a | n/a | CONFIRMED | "remains valid for a one-year window." https://business-api.tiktok.com/portal/docs/tiktok-social-mcp-server/v1.3 |

### 5B. Login Kit v2 / Content Posting (developers.tiktok.com)

| Action | Endpoint | Scope | Status | Quote + URL |
|---|---|---|---|---|
| Authorize (web) | https://www.tiktok.com/v2/auth/authorize/ params client_key, response_type=code, scope (comma-separated), redirect_uri, state | video.publish / video.upload / user.info.basic | CONFIRMED (fetch summary) | "Always `code`" for response_type. https://developers.tiktok.com/doc/login-kit-web |
| PKCE | code_challenge, code_challenge_method=S256 (hex-encoded SHA256 of verifier) | n/a | CONFIRMED: required for desktop and mobile, NOT required for web | Desktop page: "TikTok requires the Proof Key for Code Exchange (PKCE) protocol for desktop apps". Token page: code_verifier "Required for mobile and desktop apps." Web: no PKCE described. https://developers.tiktok.com/doc/login-kit-desktop ; https://developers.tiktok.com/doc/oauth-get-a-user-access-token |
| Token exchange | POST https://open.tiktokapis.com/v2/oauth/token/ (form-encoded: client_key, client_secret, code, grant_type=authorization_code, redirect_uri) | none | CONFIRMED | https://developers.tiktok.com/doc/oauth-get-a-user-access-token |
| Access token life | n/a | n/a | CONFIRMED | "The access token is valid for 2 hours after initial issuance." same page |
| Refresh token life | n/a | n/a | CONFIRMED | "The refresh token is valid for 180 days after the initial issuance." same page |
| Rotation | n/a | n/a | CONFIRMED | "Refreshed tokens are rotated: a successful refresh request returns a new refresh_token ... immediately invalidating the previous token." (Get-token page, paraphrase of "Refresh tokens are rotated: ..." ) https://developers.tiktok.com/doc/oauth-user-access-token-management : "Replace your stored refresh token with the returned value every time you refresh." |
| CONFLICT on lifetime | n/a | n/a | UNVERIFIED which is current | App Review FAQ: "User Access Tokens can be extended to a maximum of one year." https://developers.tiktok.com/doc/getting-started-faq . A search snippet of the Minis OAuth page (not opened) said 24 hours / 365 days. Use expires_in / refresh_expires_in from the response; do not hard-code. |
| Auth code life | n/a | n/a | UNVERIFIED (consumer) | Only a search snippet (5 minutes, Minis page, not opened). |

Our BFF is a confidential client, so the web flow (client_secret, no PKCE) matches TikTok's documented web flow. Adding PKCE is not documented for web; do not send code_challenge unless a live test shows it is accepted.

---------------------------------------------------------------------------
## 6. ACCESS FOR THIRD-PARTY CUSTOMERS

| Question | Status | Evidence + URL |
|---|---|---|
| Business API developer registration | CONFIRMED | Company only: "Currently, we are unable to onboard personal accounts or individual developers." Verified company-domain email; "You will be notified of the review result in three business days." https://business-api.tiktok.com/portal/docs/register-as-a-developer/v1.3 |
| Business API app review | CONFIRMED | "The review may take 2 to 3 business days." Max five developer apps per developer. https://business-api.tiktok.com/portal/docs/create-a-developer-app/v1.3 |
| Accounts API gate (new) | CONFIRMED | "starting March 20, 2026 ... developers must complete the Accounts API Access Application Form before submitting a new developer app or requesting a scope increase that includes the 'TikTok Accounts' permission scope." https://business-api.tiktok.com/portal/docs/get-started/v1.3 (same note also on create-a-developer-app and accounts-api-overview). Form contents, criteria, turnaround: UNVERIFIED (form not opened). |
| Many customers per app | CONFIRMED | App has "Developer App Access Controls: ... shared with other external accounts or organizations" question; Accounts Overview: "enables advertisers' third party partners ... publishing capabilities within their own platforms." https://business-api.tiktok.com/portal/docs/create-a-developer-app/v1.3 ; https://business-api.tiktok.com/portal/docs/accounts-api-overview/v1.3 |
| Prohibited uses relevant to us | CONFIRMED | "Download TikTok videos and images ... or migrate content to another TikTok account or other social media platforms." https://business-api.tiktok.com/portal/docs/accounts-api-overview/v1.3 . TikTok may revoke "at any time without prior notice". |
| developers.tiktok.com app review | CONFIRMED | "App review may take several days to two weeks after submission." Not an official timeline: "We do not provide an official review timeline or any guarantees for approval." https://developers.tiktok.com/doc/getting-started-faq ; https://developers.tiktok.com/doc/our-guidelines-developer-guidelines |
| developers.tiktok.com review requirements | CONFIRMED | "Apps must not be for private or personal use."; need Privacy Policy and Terms "visible on the website URL"; demo video "complete end-to-end flow"; max 5 videos 50 MB each; Sandbox required if not previously approved. https://developers.tiktok.com/doc/app-review-guidelines |
| Content Posting audit gate for public posts | CONFIRMED | see section 1A. Audit time UNVERIFIED. |
| Active user cap | CONFIRMED | "Can I increase my active user cap? ... contact us via our Support form ... not all requests will be approved." https://developers.tiktok.com/doc/getting-started-faq |
| Region: US customers only | UNVERIFIED | I found NO regional restriction on the Accounts API or Content Posting pages I read, and no statement that US accounts are supported. A search snippet says Business Messaging API requires a US data security review for US accounts, but I did not open that page and it concerns DMs, not comments. Needs a live call / TikTok contact. Accounts FAQ: "Post reach is determined solely by your TikTok account's region." https://business-api.tiktok.com/portal/docs/accounts-api-faqs/v1.3 |
| Sandbox | CONFIRMED (named) | "(To-be-deprecated) Sandbox accounts" appears in nav on Business Get Started. Details not read. |
| Quality bar | CONFIRMED | "failure to maintain a quality experience can lead to revocation of your integration." and violations "will likely lead to immediate revocation ... and a permanent ban". https://developers.tiktok.com/doc/our-guidelines-developer-guidelines |

---------------------------------------------------------------------------
## 7. DEVELOPER TERMS / POLICIES (quote exactly)

Source: https://www.tiktok.com/legal/page/global/tik-tok-developer-terms-of-service/en (read in browser, full text).

| Topic | Status | Quote |
|---|---|---|
| Automation allowed | CONFIRMED | II.1: "use automated means in your Application to collect information from or otherwise interact with the TikTok Developer Services" |
| Use limited to your app | CONFIRMED | II.1: "You may use TikTok Information obtained through the TikTok Developer Services solely for the purpose of developing, maintaining, and supporting your Applications." |
| Availability to end users | CONFIRMED | II.1: TikTok Information "may only be made available to End Users on a personal, non-exclusive, non-sublicensable, non-transferrable basis." |
| Bulk / excessive use | CONFIRMED | III.3(g): "exceeds reasonable request volume, constitutes excessive or abusive usage" |
| Spam / commercial solicitation | CONFIRMED | III.3(c): "for any commercial or unauthorized purpose, including without limitation communicating or facilitating any commercial advertisement or solicitation or spamming" (without TikTok's written consent). Relevant to auto-replies. |
| Profiles / databases about individuals | CONFIRMED | III.3(h): "build, help build, or supplement any profiles, databases, or similar records on any individual" (for unauthorized/unlawful purpose). Relevant to storing commenter records. |
| Sell/transfer TikTok data | CONFIRMED | III.3(a): "distribute, license, transfer, directly or indirectly sell, rent, syndicate, lend, copy" the Developer Services or TikTok Services |
| Sending TikTok data to third-party AI | **UNVERIFIED** | The terms contain NO occurrence of "artificial", "machine learning" or "train" (checked by text search of the full page). They are silent on AI. The only governing language is the use-limitation (II.1) and III.3(a). No explicit permission or prohibition to pass comment text to an LLM provider. Needs TikTok confirmation. |
| Data retention / deletion | CONFIRMED (termination only) | VI: on termination "delete TikTok Confidential Information or TikTok Information obtained through TikTok Developer Services in all forms in your possession and control". No fixed retention period stated in these terms. |
| Retention rule in the Business Messaging review (DMs, NOT comments) | CONFIRMED but scope-limited | "When a TikTok account user no longer authorizes sharing data with a developer, the developer must delete the personal data in a timely manner." https://business-api.tiktok.com/portal/docs/data-security-and-privacy-review/v1.3 . Applies to the Business Messaging scope; whether it is extended to comments is UNVERIFIED. Treat as the prudent standard. |
| Data minimization | CONFIRMED (DM page) | "Developers must only request or access the minimum amount of data required" same page |
| Personal data to controller terms | CONFIRMED | III.5: TikTok Controller to Controller Data Terms apply to TikTok Personal Data. |
| Audit/monitoring right | CONFIRMED | II.4 and III.4: TikTok "may audit your Applications" |
| Suspend/terminate any time | CONFIRMED | VI: "TikTok may suspend or terminate some or all TikTok Developer Services at any time with or without notice" |
| Terms cover webhooks | CONFIRMED | I.7 defines TikTok API as including "webhook, call-back" interfaces. |
| User data ownership | CONFIRMED | "Understand that the user's data belongs to them. You must safeguard their data. Don't share it with anyone without their proper consent." https://developers.tiktok.com/doc/our-guidelines-developer-guidelines |
| AI-generated content label (publishing) | CONFIRMED | Business: is_ai_generated, Content Posting: is_aigc "Set to true if the video is AI generated content." (read in browser) https://developers.tiktok.com/doc/content-posting-api-reference-direct-post . Whether labelling is MANDATORY for AI-written captions or AI-generated media: UNVERIFIED (fields are optional, default false; policy text not found in the pages I read). |
| Required UX: user control of what is posted | CONFIRMED | "The users of API Clients must have full awareness and control of what is being posted to their TikTok accounts." https://developers.tiktok.com/doc/content-sharing-guidelines . Applies to Content Posting; a category auto-send for POSTS conflicts with "no default value" privacy selection. Auto-send of COMMENT REPLIES is not addressed there. |
| TikTok's own stance on agents (Business) | CONFIRMED | Social MCP page promotes "execute complete social workflows without human intervention" and "draft replies for approval". https://business-api.tiktok.com/portal/docs/tiktok-social-mcp-server/v1.3 |
| Business API specific developer terms | UNVERIFIED | Data security page refers to "TikTok for Business Developer Terms"; I did not find/open that document. The Developer Terms above incorporate the Commercial Terms (not opened). |

---------------------------------------------------------------------------
## 8. RATE LIMITS / QUOTAS

| Action | Endpoint | Limit | Status | Quote + URL |
|---|---|---|---|---|
| Content Posting video init | /v2/post/publish/video/init/ | 6 requests/min per user access token | CONFIRMED (fetch summary) | "Each user access token is limited to 6 requests per minute." https://developers.tiktok.com/doc/content-posting-api-reference-direct-post |
| Content Posting photo init | /v2/post/publish/content/init/ | 6/min per token | CONFIRMED (fetch summary) | https://developers.tiktok.com/doc/content-posting-api-reference-photo-post |
| Content Posting upload inbox | /v2/post/publish/inbox/video/init/ | 6/min per token; max 5 pending shares per 24h | CONFIRMED (fetch summary) | https://developers.tiktok.com/doc/content-posting-api-reference-upload-video |
| Content Posting status | /v2/post/publish/status/fetch/ | 30/min per token | CONFIRMED (fetch summary) | https://developers.tiktok.com/doc/content-posting-api-reference-get-video-status |
| Posts per creator per day (Direct Post) | n/a | typically ~15, shared across all API clients | CONFIRMED | https://developers.tiktok.com/doc/content-sharing-guidelines |
| Unaudited client | n/a | 5 users per 24 h, private only | CONFIRMED | same |
| Business publish | /business/video/publish/, /business/photo/publish/ | 6/min per account, 15/day per account | CONFIRMED | https://business-api.tiktok.com/portal/docs/publish-a-public-video-post-to-an-owned-account/v1.3 |
| Business Accounts API, any endpoint (incl. comment list/reply/create) | all Accounts API endpoints | 40 QPM per authorized account per endpoint | CONFIRMED | https://business-api.tiktok.com/portal/docs/accounts-api-rate-limits/v1.3 |
| Business Accounts API, whole app | combined | Basic 600 QPM, Advanced/Premium/Ultimate 1,000 QPM | CONFIRMED | same |
| Global app level (Marketing endpoints) | n/a | Basic 10 QPS/600 QPM/864,000 QPD up to Ultimate 50/3,000/4,320,000; throttle code 40100, QPM breach = wait 5 minutes | CONFIRMED | https://business-api.tiktok.com/portal/docs/rate-limits/v1.3 . Whether the global QPS/QPD figures also apply to Accounts API: UNVERIFIED (Accounts page lists only QPM). |
| Social MCP | per tool per user | 6 QPS | CONFIRMED | https://business-api.tiktok.com/portal/docs/tiktok-social-mcp-server/v1.3 |
| Comment-specific per-day caps / reply caps | n/a | none stated | UNVERIFIED | only the spam-similarity warning. |
| Comment webhook | n/a | no volume limit stated | UNVERIFIED | |

---------------------------------------------------------------------------
## 9. OPEN ITEMS SETTLED ONLY BY A LIVE CALL OR AUDIT

1. Which TikTok account types can authorize the comment scopes (personal vs Advanced Access vs Verified Business). Docs silent. Test with a personal account.
2. Accounts API Access Application Form: criteria, whether a multi-tenant AI reply agent is approved, turnaround. Form not opened.
3. Whether `comment.list.manage` is the write scope name and whether write permission 18030200 needs a separate approval.
4. Whether an Accounts API app can serve US accounts and any region restriction (nothing found either way).
5. Whether Business-API publishing is exempt from the Content Posting "unaudited = private only" rule (Business pages are silent).
6. Content Posting API audit: form, review time, whether a multi-tenant AI-assisted tool passes given the "utility tool for accounts you manage" exclusion (this wording targets self-managed accounts; ours serves customers' own accounts, but it is the clause to quote in the audit).
7. Whether one verified domain/URL prefix can be used for posts on behalf of many different customer accounts (Business property is added "to an ad account").
8. Which webhook event list is current on developers.tiktok.com (status page lists post.publish.*, events page does not).
9. Business webhook delivery guarantees: retry window, duplicates, ordering, response-code rules, timestamp tolerance (only the developers.tiktok.com page states 72h retry).
10. Refresh-token rotation for the Business API (tt_user) flow; true access/refresh lifetimes on developers.tiktok.com (2h/180d on the OAuth page vs "maximum of one year" in the FAQ).
11. Whether commenter-supplied text may be sent to a third-party LLM provider (terms silent on AI). Ask TikTok in writing.
12. Whether AI-drafted comment replies need any label (no comment-level AI field exists in the reply endpoint; is_ai_generated exists only for posts).
13. Auto-send of comment replies: Accounts API text allows it technically; Content Posting UX rules ("full awareness and control") apply to posts and were not stated for comments.
14. Whether comment.update arrives for comments on videos older than the webhook subscription, and for private/friends-only posts (docs say "public").
15. Behaviour of comment.update when a reply is spam-flagged by TikTok after we post it (doc: no set_to_public event if flagged).

---------------------------------------------------------------------------
## URLS OPENED (and what was read)

Opened in a real browser (full text):
- https://business-api.tiktok.com/portal/docs/permission-scope/v1.3 : full permission tables; Business Comment 18030000, Get 18030100 endpoints, Manage 18030200 endpoints, Video Publish 7050363942433996801, Ad Comments group 15000000.
- https://business-api.tiktok.com/portal/docs/get-comments-on-an-owned-video/v1.3 : /business/comment/list/ params, 500-comment note, webhook note.
- https://business-api.tiktok.com/portal/docs/get-all-replies-to-a-comment/v1.3 : /business/comment/reply/list/.
- https://business-api.tiktok.com/portal/docs/reply-to-an-existing-comment-on-an-owned-video/v1.3 : reply create, 1,200 chars, spam note.
- https://business-api.tiktok.com/portal/docs/manage-comments-on-owned-tiktok-videos/v1.3 : comment use-case table; webhook subscription.
- https://business-api.tiktok.com/portal/docs/comment-update-event/v1.3 : comment.update schema, five minutes, comment.list prerequisite.
- https://business-api.tiktok.com/portal/docs/subscribe-to-tiktok-account-webhook-events-via-webhooks-api/v1.3 : /business/webhook/update/, event types VIDEO and COMMENT, permission prerequisites.
- https://business-api.tiktok.com/portal/docs/webhook-event-types/v1.3 : two event categories.
- https://business-api.tiktok.com/portal/docs/post-publishing-events/v1.3 : post.publish.* events, failure reasons.
- https://business-api.tiktok.com/portal/docs/webhook-verification/v1.3 : TikTok-Signature algorithm, sample.
- https://business-api.tiktok.com/portal/docs/publish-a-public-video-post-to-an-owned-account/v1.3 : video publish params, limits, URL verification, draft, AI flag.
- https://business-api.tiktok.com/portal/docs/publish-a-photo-post-to-an-owned-account/v1.3 : photo publish params.
- https://business-api.tiktok.com/portal/docs/get-the-publishing-status-of-a-tiktok-post/v1.3 : status endpoint.
- https://business-api.tiktok.com/portal/docs/manage-url-properties/v1.3 : domain/URL-prefix verification.
- https://business-api.tiktok.com/portal/docs/get-started/v1.3 : Accounts API Access Application Form note (2026-03-20).
- https://business-api.tiktok.com/portal/docs/accounts-api-overview/v1.3 : use cases, prohibited uses, account-type definitions.
- https://business-api.tiktok.com/portal/docs/accounts-api-authorization/v1.3 : authorization URL, auth_code 10 min.
- https://business-api.tiktok.com/portal/docs/accounts-api-authentication/v1.3 : token endpoints, 1 day / 1 year, scope example.
- https://business-api.tiktok.com/portal/docs/accounts-api-authorization-faqs/v1.3 : authorization URL differences, token expiry table.
- https://business-api.tiktok.com/portal/docs/accounts-api-faqs/v1.3 : revoke methods, region/reach note.
- https://business-api.tiktok.com/portal/docs/accounts-api-rate-limits/v1.3 : 40 QPM, 600/1,000 app QPM.
- https://business-api.tiktok.com/portal/docs/rate-limits/v1.3 : global app-level levels, 40100 throttle code.
- https://business-api.tiktok.com/portal/docs/register-as-a-developer/v1.3 : company-only, 3 business days.
- https://business-api.tiktok.com/portal/docs/create-a-developer-app/v1.3 : 2-3 business day app review, 5 apps.
- https://business-api.tiktok.com/portal/docs/get-started-guide/v1.3 : overview only.
- https://business-api.tiktok.com/portal/docs/tiktok-social-mcp-server/v1.3 : MCP limits, 6 QPS, one-year authorization, publishing not supported over MCP.
- https://business-api.tiktok.com/portal/docs/data-security-and-privacy-review/v1.3 : Business Messaging DSPR (DM-specific), retention wording.
- https://developers.tiktok.com/doc/oauth-user-access-token-management : 2h / 180d / rotation.
- https://developers.tiktok.com/doc/oauth-get-a-user-access-token : /v2/oauth/token/, code_verifier mobile+desktop only.
- https://developers.tiktok.com/doc/login-kit-desktop : PKCE required for desktop.
- https://developers.tiktok.com/doc/content-posting-api-get-started : Direct Post flow, prerequisites, unaudited note.
- https://developers.tiktok.com/doc/content-sharing-guidelines : unaudited caps, intent rules, UX rules.
- https://developers.tiktok.com/doc/getting-started-faq : app review time, token extension, private posts FAQ.
- https://developers.tiktok.com/doc/app-review-guidelines : review criteria.
- https://developers.tiktok.com/doc/our-guidelines-developer-guidelines : user data, no official timeline.
- https://developers.tiktok.com/doc/content-posting-api-reference-get-video-status : webhooks + events (JS search of page).
- https://developers.tiktok.com/doc/content-posting-api-reference-direct-post : is_aigc and error codes (JS search of page).
- https://www.tiktok.com/legal/page/global/tik-tok-developer-terms-of-service/en : full Developer Terms.

Read via fetch tool (summaries, flagged "fetch summary" above):
- https://developers.tiktok.com/doc/content-posting-api-reference-direct-post (fields, limits), -photo-post, -upload-video, -media-transfer-guide (chunking, formats, domain rules), /doc/webhooks-overview (72h retry), /doc/webhooks-events (4 events), /doc/webhooks-verification (signature), /doc/tiktok-api-scopes (23 scopes, no comment scope), /doc/login-kit-web (authorize URL).

Opened but unusable / not found: https://developers.tiktok.com/doc/login-kit-manage-user-authorization (404), https://developers.tiktok.com/doc/content-posting-api-faq (not found), https://developers.tiktok.com/policy/en (401), https://developers.tiktok.com/doc/scopes-overview (names one scope only).

Seen only as search snippets, NOT opened (treated UNVERIFIED): Business Messaging access/US data-security pages, Minis OAuth page (24h/365d), Research API comment list page.
