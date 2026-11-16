# Meta platform research for GeekSocial multi-tenant (official docs only)

Researched 2026-11-16. Graph API v27.0 pages. Every URL below was opened (raw text pulled with curl from the
`.md` mirror that developers.facebook.com serves for its Instagram, Pages, Login and Development docs, or from
the rendered HTML for `/docs/...` pages, `/terms/` and `/devpolicy/`). Nothing was run against a live app.

Legend: CONFIRMED = stated in an official page I opened (URL cited, short verbatim quote). UNVERIFIED = docs
silent, conflicting or unopened. BLOCKED = unsupported or needs an approval we do not have.
Short URL forms used in the tables are expanded in the "URLs opened" list at the end (key in brackets).

---------------------------------------------------------------------------------------------------

## A. Connect-flow choice (I1, I5) and the Unified Login token (SUAT)

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Which setup for a SaaS | Use **Unified Login**. Meta recommends it for new integrations and says new features ship only there. | CONFIRMED | "recommended for new integrations" [OVR]. "New Instagram API features are released only for this setup." [UL] |
| Does a Facebook Page have to be linked (I1) | **No** for Unified Login and Instagram Login. **Yes** only for the Facebook Login setup. This settles I1: the old claim that publishing needs a Page is gone from the current publishing guide, which now mentions a Page only for Page Publishing Authorization (PPA) when one happens to be linked. | CONFIRMED | "can serve app users who don't have a Facebook Page" [OVR]. "Facebook Login serves app users whose Instagram professional account is linked to a Facebook Page." [OVR]. Table row: "Facebook Page ... Not required" for Unified Login [UL] |
| What Unified Login needs from us and the customer | A Meta app owned by a business portfolio; the customer must pick a business portfolio at login. Our portfolio must be separate from the customer's. Web surfaces only. | CONFIRMED | "Business portfolio \| Required" for Unified Login [UL]. "This needs to be separate from the business portfolio owned by your business client." [FLB]. "Your app can only request logins from web surfaces" [FLB] |
| Customer onboarding burden | Customer logs in with Instagram or Facebook, selects a portfolio, selects the IG account(s), approves permissions. Meta warns many customers do not know they have a portfolio. | CONFIRMED | "Many clients think of their business as just their Instagram account." [BO] |
| Which setup is worst for our no-timer rule | Instagram Login issues 60-day tokens that must be refreshed (long-lived token "valid for 60 days" and refreshable), which implies a refresh job. Unified Login's SUAT can be non-expiring. | CONFIRMED | "This access token is valid for 60 days and can be refreshed before they expire." [OVR] |
| Can one login cover both the Page and the IG account (I5) | **Yes at the Facebook Login for Business level**: a configuration lists the assets (Pages, Instagram accounts, ad accounts) and the permissions (the `pages_*` set is supported for system-user tokens). One SUAT then holds both. | CONFIRMED (FLB) | "Select all the assets your app needs access to." and "business assets, such as Facebook Pages, ad accounts or Instagram accounts" [FLB]. Event example: "Client business allows your System User to make posts on their Facebook Page" [FLBW] |
| Same, through the Instagram "API setup with Unified login" configuration screen specifically | The Unified Login guide says the default configuration requests an IG account and an ad account, and that a configuration may request "additional assets". It never names a Page asset. Whether the Page can be added on that screen is not stated. | UNVERIFIED | "any additional assets your configuration requests, such as an ad account" [BO]. Default config text in [UL-GS] |
| One token for several IG accounts | One SUAT covers every IG account the customer shared for that portfolio. | CONFIRMED | "A client can share more than one Instagram account, and one access token covers all of them." [UL-GS] |
| One connection per portfolio | A customer with accounts in several portfolios makes one connection per portfolio and we get one token each. | CONFIRMED | "they set up a separate connection for each, and your app gets a separate access token for each" [BO] |
| SUAT lifetime | **Chosen per configuration.** Meta's Login for Business page says the system-user token "Defaults to never expire". A token generated through the management API can be asked to expire in 60 days. The Instagram pages say only that expiry depends on the configuration (the App Dashboard preview shows it). | CONFIRMED | "You choose the expiration when you create the configuration" [UL-AUTH]. "Defaults to never expire for the common offline server-to-server communication." [FLB]. "set to `true` so that the token expires in 60 days" [FLB] |
| How a SUAT is created per customer | Customer completes the login URL (`https://www.facebook.com/dialog/oauth?client_id&config_id&redirect_uri&response_type=code`); our server then calls `GET https://graph.facebook.com/v27.0/oauth/access_token?client_id&client_secret&code`. App ID and secret are the Meta app's, not the Instagram app's. Authorization-code grant only. | CONFIRMED | "Your app doesn't receive an access token directly from the login flow." [UL-AUTH]. "Authorization Code grant only." [FLB] |
| Identify the customer after the exchange | With a SUAT, `/me` is the system user, not the IG account. Call `GET /me?fields=client_business_id` and `GET /me/assigned_instagram_accounts`; store both with the token and the `config_id`. | CONFIRMED | "`/me` represents the business integration system user, not the client's Instagram professional account." [UL-GS] |
| Token management API | `POST /{CLIENT_BUSINESS_ID}/system_user_access_tokens` can fetch (`fetch_only=true`) or generate granular tokens (subset of assets/scope). Needs `business_management` and `appsecret_proof`. | CONFIRMED | "This access token requires the `business_management` permission" [FLB] |
| Who owns the token | The customer's portfolio, not the person who logged in; survives that person leaving. | CONFIRMED | "belongs to your client's business portfolio, not to the person who logged in" [UL] |
| How a SUAT dies | Customer removes our app from their portfolio, or moves the IG account to another portfolio. Meta says stop retrying and ask for a reconnect. | CONFIRMED | "stops working if your client removes your app from their business portfolio or moves the Instagram account" [UL-AUTH]. "stop retrying and ask your client to connect again" [UL-AUTH] |
| Push signal for disconnects (no polling) | App-level webhook object `application`, fields `business_integration_install`, `business_integration_uninstall`, `business_integration_update` (asset added/removed). | CONFIRMED | "When a business integration is uninstalled. (Business Integration System User is deleted)" [FLBW] |
| Page access token derived from a SUAT; Page webhook subscription with a SUAT | The Pages webhook guide specifies "A Page access token requested from a person who can perform" CREATE_CONTENT / MANAGE / MODERATE. How a Page token is obtained from a SUAT is not shown on any page I opened. | UNVERIFIED | [PG-WH] |
| Instagram Login as the alternative | No portfolio, no Page; `graph.instagram.com`; Instagram User token (code 1 h, short token 1 h, long token 60 d); no hashtag search, product tagging or partnership ads. | CONFIRMED | "This API setup cannot access ads or tagging." [IGL] |
| Facebook Login setup | Needs the Page; Page access token; adds `pages_read_engagement`; the user needs admin-equivalent tasks on the Page. | CONFIRMED | "your app user must also be able to perform admin-equivalent tasks on the linked Facebook Page" [OVR] |

Recommendation for the connect flow: **Unified Login** (Facebook Login for Business configuration, system-user
token, "Instagram Login Compatible" configuration label so the customer can log in with Instagram or Facebook).
Facebook Pages are a separate asset on the same business-portfolio token path (Facebook Login for Business with
`pages_*` permissions), not a prerequisite of the Instagram side. The one open question is whether a single
configuration can carry both the Page asset and the IG asset when built from the Instagram dashboard tab; if not,
use a Facebook Login for Business configuration created under the Facebook Login for Business product.

Tension with the hard no-timer rule: a non-expiring SUAT removes the need for a refresh timer; that is the only
setup of the three where the token lifetime does not force one. Invalidation then arrives by the
`business_integration_uninstall` webhook or by an error on the next call, not by a poll.

---------------------------------------------------------------------------------------------------

## B. Instagram webhooks (I3)

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Fields for comments and mentions (Unified Login) | `comments`, `live_comments`, `mentions`; all need `instagram_manage_comments`. Message fields (`messages`, `message_reactions`, `messaging_postbacks`, `messaging_referral`, `messaging_seen`) need `instagram_manage_messages`. `story_insights` needs `instagram_manage_insights`. | CONFIRMED | "`mentions` \| `instagram_manage_comments`" [WH-F] |
| Fields under Instagram Login | `comments`, `live_comments` need `instagram_business_basic` + `instagram_business_manage_comments`; mentions are folded into `comments`. | CONFIRMED | "Included in the `comments` webhook notification" [WH-F] |
| Fields under Facebook Login | `comments`, `live_comments`, `mentions` need `instagram_basic`, `instagram_manage_comments`, `pages_manage_metadata`, `pages_read_engagement`, `pages_show_list`. | CONFIRMED | [WH-F] |
| Subscription mechanics, Unified Login | (1) App Dashboard, Configure webhooks: callback URL, verify token, select fields. (2) After each customer connects, `POST https://graph.facebook.com/v27.0/<IG_ACCOUNT_ID>/subscribed_apps` with `subscribed_fields=comments,...` and the SUAT. Response `{"success": true}`. Instagram User tokens and `graph.instagram.com` do not work for Unified Login. | CONFIRMED | "subscribe each Instagram professional account your client shared to your app" [WH-SETUP]. "Instagram User access tokens and `graph.instagram.com` don't work with the Instagram API with Unified Login." [WH-SETUP] |
| Fields are app-wide, not per account | You cannot choose fields per customer account. | CONFIRMED | "You cannot select fields for each app user account. An account receives every field selected for the app." [WH-SETUP] |
| Does the payload carry the comment text | **Yes** for `comments` and `live_comments`: `from` (IGSID + username), `comment_id`, `parent_id` (replies), `text`, `media` (`id`, `media_product_type`; ad fields for ads). **No** for `mentions`: only `media_id` (and `comment_id` for a mention in a comment); fetch the text with the mentioned-comment edge. | CONFIRMED | "Comment text, included if comment included text" [WH-EX]. Mentions example carries only `comment_id` and `media_id` [WH-EX] |
| Payload shape caveat | The docs show two shapes: `entry[].changes[].value` with `comment_id` (Unified Login and Facebook Login for Business objects) and a flatter shape with `id` (Business Login for Instagram objects). Parse the Unified shape; log and fail loudly on any other. | CONFIRMED | Comparison of [CM] "Facebook Login for Business" and "Business Login for Instagram" payloads |
| Delivery requires Live mode | Setup page requirement line. | CONFIRMED | "Your app must be published or **Live** to receive notifications for app users." [WH-SETUP] |
| Fires in Development mode for a role-holding tester account | The Unified Login guide has a "Step 3: Configure webhooks" and tests with an Instagram Tester account "while your app is in Development mode", and says "Your server receives a test webhook notification". The setup page says Live is required "for app users". Whether real comments on a tester's account fire in Development mode is not stated. The dashboard **Test** button sends a sample notification. | UNVERIFIED | "Open **Generate access tokens** to test with your own Instagram account while your app is in Development mode." [UL-GS] |
| Access level for `comments` / `live_comments` | The Limitations list says Advanced Access is required. The requirements table says Unified Login access level "Depends on the requested API and webhook field" (and Instagram Login "Advanced Access"). So for customers' accounts plan on Advanced Access (App Review) for these fields. | CONFIRMED | "Advanced Access is required for `comments` and `live_comments` notifications." [WH-SETUP] |
| Business Verification | Required for production access in all three setups. | CONFIRMED | "Business verification \| Required for production access" [WH-SETUP] |
| Account must be public | Private accounts get no comment or mention notifications. | CONFIRMED | "Private accounts do not receive comment or mention notifications." [WH-SETUP] |
| Live-comment lifetime | Live-media comment notifications stop when the broadcast ends. | CONFIRMED | "Comment notifications for live media stop when the broadcast ends." [WH-SETUP] |
| Security | Verification GET (`hub.mode`, `hub.challenge`, `hub.verify_token`); POST signed with `X-Hub-Signature-256: sha256=<...>` using the App Secret; respond `200`; batches up to 1000; retries immediately then decreasing frequency for 36 hours; dedupe required. Meta says validating is optional but should be done; the existing doc rules still apply. | CONFIRMED | "You don't have to validate the payload, but you should." [WH-SETUP]. "Unacknowledged responses will be dropped after 36 hours." [WH-SETUP] |
| No history | Missed events cannot be re-queried by webhook. | CONFIRMED | "Webhooks do not provide a historical notification store" [WH] |
| Missed-event recovery without a timer | `GET /<IG_MEDIA_ID>/comments` exists but Meta gives no gap-recovery contract for Instagram webhooks (unlike LinkedIn). A recovery read has to be user- or event-triggered by us. | UNVERIFIED | [CM] says "We strongly recommend using webhooks to prevent rate limiting." |
| mTLS | Optional step on the setup page; the text refers to WhatsApp Business Account. Not needed. | CONFIRMED | "Configure mutual TLS" [WH-SETUP] |

---------------------------------------------------------------------------------------------------

## C. Instagram comment endpoints (I4)

Host `graph.facebook.com` for Unified Login and Facebook Login, `graph.instagram.com` for Instagram Login. IDs are
the Instagram professional account ID, media ID, comment ID. Permissions for Unified Login: `instagram_basic` +
`instagram_manage_comments` (add `instagram_manage_messages` only for private replies).

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Read comments on a media object | `GET /<IG_MEDIA_ID>/comments`. Top-level only, newest first, max 50 per query, no timestamp filter; `replies` only via field expansion. Cursor paging. Also `GET /<IG_MEDIA_ID>/ordered_comments` (Facebook Login only). | CONFIRMED | "Returns only top-level comments." "Returns a maximum of 50 comments per query." "Comments cannot be filtered by timestamp." [IG-MC] |
| Read one comment | `GET /<IG_COMMENT_ID>?fields=...`. Fields: `from{id,username}`, `hidden`, `id`, `like_count`, `legacy_instagram_comment_id`, `media{id,media_product_type}`, `parent_id`, `replies`, `text`, `timestamp`, `user`, `username`. | CONFIRMED | Fields table in [IG-C] |
| `username` needs the permission | `username` of a commenter requires `instagram_manage_comments` (since 2024-08-27). | CONFIRMED | "will be required to access the `username` field" [IG-C] |
| Read replies | `GET /<IG_COMMENT_ID>/replies`. Not for deleted comments. | CONFIRMED | "You cannot get replies to a comment that has been deleted." [IG-R] |
| Reply to a comment | `POST /<IG_COMMENT_ID>/replies?message=<text>` (message required). Returns `{"id": "<new comment id>"}`. Limits: top-level comments only (a reply to a reply lands on the top-level comment), not on hidden comments, not on live-video comments (use a private reply instead). | CONFIRMED | "You can only reply to top-level comments; replies to a reply will be added to the top-level comment." "You cannot reply to hidden comments." [IG-R] |
| Who the reply appears as | The reference says only that the reply is "created" with the app user's token. It does not state the author shown on Instagram. Expect the connected account; prove it with one live reply. | UNVERIFIED | [IG-R] |
| Create a top-level comment | `POST /<IG_MEDIA_ID>/comments?message=`. Not for live media. | CONFIRMED | "Comments on live video IG Media are not supported." [IG-MC] |
| Hide / unhide | `POST /<IG_COMMENT_ID>?hide=true` (or `false`), returns `{"success": true}`. The media owner's own comments cannot be hidden. Not supported on live media. | CONFIRMED | "`hide` (required) — Set to `true` to hide the comment, or `false` to show the comment." [IG-C]. "will always be displayed, even if the comments have been set to `hide=true`" [IG-C] |
| Delete | `DELETE /<IG_COMMENT_ID>`, returns `{"success": true}`. Only the owner of the media the comment is on can delete. Not on live media. | CONFIRMED | "A comment can only be deleted by the owner of the object upon which the comment was made" [IG-C] |
| Disable / enable comments on media | `POST /<IG_MEDIA_ID>` (listed; details on the media reference page, not opened). | CONFIRMED (listed only) | "Disable/enable comments on an IG Media" [CM] |
| Visibility limits | Comments on age-gated media are not returned; comments by users the owner has restricted are not returned until approved. | CONFIRMED | "Comments on age-gated media are not returned." [IG-C] |
| Reaction (like) on a comment | Not on the comment pages. A separate permission `instagram_manage_engagement` ("publish or delete a Like on ... IG Comment objects") exists for the Facebook Login setup; it is not in the Unified Login common list. | UNVERIFIED for Unified Login | "publish or delete a "Like" on IG Media objects ... and IG Comment objects" [PERM] |
| Permission page vs product pages | The legacy permission reference lists `pages_read_engagement` and `pages_show_list` as dependencies of `instagram_manage_comments`; the Unified Login endpoint pages list only `instagram_basic` + `instagram_manage_comments`. | UNVERIFIED (conflict) | [PERM] vs [IG-MC] |
| Allowed usage for review | "Read, update and delete comments of Instagram Business accounts." | CONFIRMED | [PERM] |
| Private reply to a commenter (optional) | Exists; needs `instagram_manage_messages`; limit 750 calls per hour per account for post/reel comments. | CONFIRMED | "750 calls per hour per Instagram professional account for private replies to comments on Instagram posts and reels" [RL] |

---------------------------------------------------------------------------------------------------

## D. Instagram publishing limit (I2)

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Content Publishing guide, "Rate Limit" section | **100** API-published posts in a 24-hour moving period; carousels count as one; enforced on `media_publish`. | CONFIRMED (that this text exists) | "Instagram accounts are limited to 100 API-published posts within a 24-hour moving period." [CP] |
| Same guide, carousel "Limitations" | **50** published posts per 24 hours. | CONFIRMED (that this text exists) | "Accounts are limited to 50 published posts within a 24-hour period." [CP] |
| `content_publishing_limit` reference | `config.quota_total` is "currently 50", `quota_duration` 86400; the sample response shows `quota_total: 50`. | CONFIRMED (that this text exists) | "the maximum number of IG Containers the app user can publish within the `quota_duration` time period (currently `50`)" [CPL] |
| Settled value | **Not settled by the pages: they still contradict each other** (100 in one section of the guide, 50 in another section of the same guide and on the endpoint's own reference page). The endpoint reference is the field-level, machine-read source and says 50. Plan with 50 as the safe ceiling and read `GET /<IG_ID>/content_publishing_limit?fields=config,quota_usage` per account to get the authoritative number. | UNVERIFIED | see rows above |
| Container creation limit | 400 containers per rolling 24 hours per account; containers expire after 24 hours. | CONFIRMED | "An Instagram account can only create 400 containers within a rolling 24 hour period" [IG-MEDIA] |
| Usage query | `GET /<IG_USER_ID>/content_publishing_limit` with `fields=config,quota_usage` and optional `since` (not older than 24 h). | CONFIRMED | "A Unix timestamp no older than 24 hours." [CPL] |

---------------------------------------------------------------------------------------------------

## E. Instagram media rules

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Image format | JPEG only. MPO and JPS are not supported. | CONFIRMED | "JPEG is the only image format supported." [CP] |
| Image size and dimensions | Max 8 MB; width 320 to 1440 px (scaled up/down); sRGB (others converted). | CONFIRMED | "File size: 8 MB maximum." "Maximum width: 1440" [IG-MEDIA] |
| Image aspect ratio | Within 4:5 to 1.91:1. | CONFIRMED | "Aspect ratio: Must be within a 4:5 to 1.91:1 range" [IG-MEDIA] |
| `image_url` must be publicly reachable | Yes; Meta fetches it. | CONFIRMED | "We cURL media used in publishing attempts, so the media must be hosted on a publicly accessible server at the time of the attempt." [CP] |
| Signed / expiring URL | Not mentioned anywhere I opened. The wording "at the time of the attempt" says reachability is checked then, but says nothing about query-string signatures, redirects, auth headers or when the fetch happens (container creation or publish). Meta "strongly recommended" ASCII-only URLs, "or the request will fail". | UNVERIFIED | "URLs that contain only US ASCII characters" [IG-MEDIA] |
| Video by URL | `video_url` for videos and reels, public server. | CONFIRMED | "We cURL the video using the passed-in URL, so it must be on a public server." [IG-MEDIA] |
| Resumable upload | `POST /<IG_ID>/media?upload_type=resumable` returns a container id and `uri`; then `POST https://rupload.facebook.com/ig-api-upload/<API_VERSION>/<IG_MEDIA_CONTAINER_ID>` with header `Authorization: OAuth <token>`, `offset`, `file_size` and `--data-binary`, or header `file_url` for a hosted file. Success `{"success":true,"message":"Upload successful."}`. Interrupted uploads resume from `bytes_transferred` (read `video_status.uploading_phase`). Only for apps using Facebook Login for Business (Unified Login is built on it; Instagram Login is not). | CONFIRMED | "Only for apps that have implemented Facebook Login for Business." [CP-RU]. "calls to upload videos for Reels use `rupload.facebook.com`" [CP-RU] |
| Reel video | MOV/MP4, no edit lists, moov atom first; H264 or HEVC, AAC; 23-60 FPS; max 1920 px wide; aspect 0.01:1 to 10:1 (9:16 recommended); 3 s to 15 min; max 300 MB; max 25 Mbps VBR. Reels cannot be in carousels. Cover image JPEG max 8 MB. | CONFIRMED | "Duration: 15 mins maximum, 3 seconds minimum" "File size: 300MB maximum" [IG-MEDIA]. "Reels cannot appear in carousels" [IG-MEDIA] |
| Story video / image | Video: 3 to 60 s, max 100 MB, same codecs, aspect 0.1:1 to 10:1. Image: JPEG, max 8 MB, 9:16 recommended. Stories expire after 24 hours. Stickers (link, poll, location) not supported. | CONFIRMED | "Duration: 60 seconds maximum, 3 seconds minimum" "File size: 100MB maximum" [IG-MEDIA] |
| Stories need a business account | The Facebook Login page says Stories are only for business accounts (not Creator). Not repeated on the Unified Login pages. | CONFIRMED (Facebook Login page) | "except Stories, which are only available to business accounts" [IGFB] |
| Carousel | Up to 20 images/videos/mixed; children created with `is_carousel_item=true`; container `media_type=CAROUSEL` with `children`; all cropped to the first item, default 1:1. | CONFIRMED | "Carousels are limited to 20 images, videos, or a mix of the two." "Carousel images are all cropped based on the first image in the carousel" [CP] |
| Container status polling | `GET /<IG_CONTAINER_ID>?fields=status_code` returns EXPIRED, ERROR, FINISHED, IN_PROGRESS or PUBLISHED; the `status` field carries the error subcode on ERROR. Meta's recommended cadence is a timer-style poll. | CONFIRMED | "We recommend querying a container's status once per minute, for no more than 5 minutes." [CP] |
| Webhook alternative to status polling | **None documented.** The Instagram webhook fields matrix (comments, live_comments, mentions, message fields, story_insights) has no container-status field. I found no page that offers one. | UNVERIFIED | Fields list in [WH-F] |
| Is a status check required for an image | The guide frames the status query as troubleshooting for videos ("If you are able to create a container for a video but ... `media_publish` does not return the published media ID"). For images it does not say whether `media_publish` can be called immediately. | UNVERIFIED | [CP] "Container publishing status" |
| Tension with the no-timer rule | Meta's cadence is a bounded timer loop. If the rule forbids it, the only documented alternative is a check triggered by a user or event action (not a schedule); for video publishing this needs an explicit decision. | decision needed | n/a |
| AI self-disclosure on publish | `is_ai_generated=true` on the container (carousel container only; error if set on children). Available for Facebook Login and Instagram Login setups; Unified Login not named. | CONFIRMED | "To provide a self-disclosure of AI usage in the media, you can set `is_ai_generated` parameter to `true`" [CP] |
| Alt text | `alt_text` for image posts only. | CONFIRMED | "Reels and stories are not supported." [CP] |
| PPA | An account linked to a Page that requires Page Publishing Authorization cannot be published to until completed; Meta cannot tell us in advance, so tell customers. | CONFIRMED | "cannot be published to until PPA has been completed" [CP] |
| Not supported | Shopping tags, filters. Link posts and text-only posts: not documented (BLOCKED, per the existing doc). | CONFIRMED | "Shopping tags are not supported." "Filters are not supported." [CP] |

---------------------------------------------------------------------------------------------------

## F. Facebook Page media rules

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Publish a photo | `POST /{page-id}/photos` with `url` (a hosted image) **or** multipart/form-data upload (`source` historically). Returns `{id, post_id}`. Permissions: `pages_manage_posts`, `pages_read_engagement`, `pages_show_list`; token from a person with CREATE_CONTENT. | CONFIRMED | "Use a photo that is already on the internet by publishing using the `url` parameter" [FB-PH]. Pages guide: "send a `POST` request to the `/page_id/photos` endpoint" [PG-POSTS] |
| Binary upload alternative | Attach the photo as multipart/form-data; the field name does not matter. | CONFIRMED | "Attach the photo as multipart/form-data" [FB-PH] |
| Accepted formats | .jpeg, .bmp, .png, .gif, .tiff | CONFIRMED | "`.jpeg, .bmp, .png, .gif, .tiff`" [FB-PH] |
| Size | Max 10 MB (PNG: keep under 1 MB to avoid pixelation). Location metadata stripped. | CONFIRMED | "Files can not exceed 10MB." [FB-PH] |
| Must `url` be public | The page says only that it is a photo "already on the internet". It does not say "public" and does not mention signed URLs. | UNVERIFIED | [FB-PH] |
| Multi-photo post | Upload each photo with `published=false` (add `temporary=true` if the post is scheduled), keep the returned ids, then `POST /{page-id}/feed` with `message` and `attached_media[n]={"media_fbid":"<id>"}`. Scheduled: also `published=false`, `scheduled_publish_time`, `unpublished_content_type=SCHEDULED`. Unpublished photos are deleted after about 24 hours. | CONFIRMED | "it will remain on Facebook servers for about 24 hours. If you do not publish these photos within 24 hours, we delete them." [FB-PH] |
| Video | Video API: `POST https://graph-video.facebook.com/v27.0/<PAGE_ID>/videos` with `title`, `description` and `fbuploader_video_file_chunk=<handle>`; the handle comes from the Resumable Upload API (`POST /<APP_ID>/uploads` with `file_name`, `file_length`, `file_type`; then `POST /upload:<SESSION_ID>` with `file_offset` header and binary body). Resumable accepts `application/pdf`, `image/jpeg`, `image/jpg`, `image/png`, `video/mp4`. Needs `pages_show_list`, `pages_read_engagement`, `pages_manage_posts`; the Pages posts guide additionally names `publish_video`. | CONFIRMED | "A video handle ... received when you upload your video file to Meta servers using the Resumable Upload API" [VID] |
| Video size / duration limits | Not stated on the pages I opened (reference pages for the video node were not opened). | UNVERIFIED | [VID] |
| Permission conflict on video | Video guide lists `pages_manage_posts`; Pages posts guide lists `publish_video` "if you are publishing a video to the Page". Request both. | UNVERIFIED (conflict) | [VID] vs [PG-POSTS] |
| Link post, schedule | Already CONFIRMED in the existing doc (`/feed`, `link`, 10 min to 30 days). Reconfirmed in [PG-POSTS]. | CONFIRMED | "The publish date must be between 10 minutes and 30 days from the time of the API request." [PG-POSTS] |
| Update/delete limits | An app can update only a post it created. | CONFIRMED | "An app can only update a Page post if the post was made using that app." [PG-POSTS] |
| Page-level webhook (feed) install | `POST /{page-id}/subscribed_apps?subscribed_fields=feed` with a Page token; the Page must not have the App platform disabled. | CONFIRMED | "Webhook notifications will only be sent if your Page has installed your Webhooks configured-app" [PG-WH] |

---------------------------------------------------------------------------------------------------

## G. Multi-tenant access (customers' own accounts)

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Standard vs Advanced Access | Standard Access (automatic) works only for people with a role on the app. Serving any other business needs Advanced Access per permission via App Review, and Advanced Access requires Business Verification. | CONFIRMED | "Advanced Access, however, must be approved on an individual permission and feature basis through the App Review process." [AL]. "This access level requires App Review and Business Verification." [OVR] |
| Instagram App Review table | A Tech Provider whose app serves multiple businesses: Advanced Access, App Review "Required". Own business only: Standard, not required. | CONFIRMED | "I am a Tech Provider and my app serves multiple businesses." [IG-AR] |
| What to request for Unified Login | Advanced Access to **each permission in the configuration**, and the app must be in Live mode. If the customer has no role on the app, this is mandatory. | CONFIRMED | "To connect other clients, your app needs Advanced Access to each permission in your configuration, and it must be in Live mode." [UL-GS] |
| Our minimum permission set | Unified Login: `instagram_basic`, `instagram_manage_comments`, `instagram_content_publish` (+ `business_management` for the token API; `instagram_manage_messages` only if DMs/private replies). Facebook Pages: `pages_show_list`, `pages_read_engagement`, `pages_manage_engagement`, `pages_manage_posts`, `pages_manage_metadata`, `pages_read_user_content` (existing doc), plus `publish_video` for video. | CONFIRMED (names) | Common-permissions table [UL-PERM]; Pages posts guide [PG-POSTS]; [PERM] |
| Business Verification | Required before other businesses can grant permissions. Without it, other businesses' users cannot grant and features are inactive. Also required before the app can be published. | CONFIRMED | "app users from other Businesses will be unable to grant these apps permissions and all features will be inactive." [BV]. "You're required to connect your app to a business that has completed Business Verification before you can publish your app." [IG-CREATE] |
| Facebook Login for Business extra gate | Advanced Access to `public_profile` is required before a Facebook Login for Business app goes live (for external users without a role). Unified Login is built on Facebook Login for Business, so treat it as applying. | CONFIRMED (FLB); applicability to Unified Login inferred | "advanced access to the public_profile permission is required for Facebook Login for Business apps before they go live" [FLB] |
| Tech Provider / Solution Provider | The Instagram App Review page uses "Tech Provider". Meta's separate **Access Verification** for Tech Providers (about 5 days) was **retired on 2026-10-09**: no form, no API gate. Business Verification and App Review are unchanged; "Direct Integrator" is now the default classification and Tech Provider follows from upgrading the app or from the advanced permissions requested. The legacy page lists `instagram_basic`, `instagram_content_publish`, `pages_manage_posts`, `pages_read_user_content` etc. as the permissions that triggered it. | CONFIRMED | "Access Verification will no longer be required in the Tech Provider onboarding flow." [BLOG]. "Business Verification is still required before your app can access data owned by other businesses." [BLOG]. Legacy header: "Access Verification is no longer required." [AV] |
| "Solution Provider" (Meta's WhatsApp programme) | Not an Instagram/Facebook Pages requirement on any page I opened. | UNVERIFIED (not found) | n/a |
| Tech Provider contract duties (Platform Terms) | Use data only for the Client's purpose; keep each Client's data separate; keep a Client list; no sharing except with the Client, a Service Provider needed for the Client's purpose, or when required by law. | CONFIRMED | "You will ensure that Platform Data you maintain on behalf of one Client is maintained separately from that of other Clients" [TERMS 5.b] |
| App Review stages (as documented) | (1) App settings: 1024x1024 icon, privacy policy URL, category, business email. (2) Business Verification. (3) Data handling questions. (4) App verification: working app, reviewer instructions and credentials (no test users for Unified Login: "Test users aren't supported"). (5) Permissions review: description + screencast per permission. (6) At least one successful API call per requested permission within 30 days. (7) Submit; decision. | CONFIRMED | "App Review consists of two main components - app verification and permissions review." [AR-C]. "Make at least 1 successful API call using each permission for which you are requesting advanced access." [AR-SG] |
| Review duration | Queued; decision "within a week". Resubmission/re-review times not documented. | CONFIRMED | "your submission will be queued and you should receive a decision within a week." [AR-SG] |
| Reviewer access | If Meta cannot test the app, the whole submission is rejected; a permission whose function cannot be tested is not approved. | CONFIRMED | "If we are unable to access your app to test it, your entire submission will be rejected." [AR] |
| Allowed-usage trap | Requesting permissions the app does not use, or whose allowed usage the app does not match, fails the submission. | CONFIRMED | "your submission will not be approved." [IG-AR] |
| Live mode and who can grant | Live mode allows permissions from anyone, but only approved ones. Development mode: only role users. | CONFIRMED | "Apps in Live mode can request permissions from anyone, but only permissions approved through App Review." [MODES] |
| Live mode required for webhooks to reach customers | Yes for Instagram webhooks. | CONFIRMED | "Your app must be published or **Live** to receive notifications for app users." [WH-SETUP] |
| Switching to Live before review | Meta warns Live mode before App Review can make the app unusable even for role users, and Development-mode test posts become public on the switch. | CONFIRMED | "switching to Live mode prematurely may make your app unusable for users who have a role on your app" [AR-SG] |
| Data Use Checkup (DUC) | Annual, for apps with Advanced Access or live with a use case; not needed in Development mode but must be done before the Live switch. Includes data-handling questions. Apps using Facebook Login for Business have reduced ongoing-review scope. | CONFIRMED | "will need to complete DUC before the app can be switched to Live mode" [DUC] |
| Data Protection Assessment (DPA) | Annual questionnaire on use, sharing and protection of Platform Data; an app admin has 60 days after enrolment or risks losing access. Triggering criteria are only "apps accessing certain types of data"; whether GeekSocial will be enrolled is not predictable from the docs. | CONFIRMED (process); our enrolment UNVERIFIED | "An admin of the app will be given 60 days to complete the assessment or risk losing platform access." [DPA] |
| Private-app shortcut | Only for apps with no UI/no login: approval limited to `instagram_basic` and `instagram_manage_comments`. Not applicable (we have a UI). | CONFIRMED | [OVR] "Private apps" |
| App count limit | Max 15 apps with developer/admin role if not connected to a verified business. | CONFIRMED | "a maximum of 15 apps that are not already connected to a Meta Verified Business Account" [IG-CREATE] |
| Reapplication, rejection reasons, per-permission timelines for Pages permissions | Not read (AR FAQ and DPA content pages returned no text). | UNVERIFIED | n/a |

---------------------------------------------------------------------------------------------------

## H. Platform Terms and policies on AI / service providers, AI-generated replies, data deletion

Platform Terms last updated February 3, 2026 (page header). Developer Policies last updated February 3, 2026.

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Is an AI/LLM provider a "Service Provider" | The Terms define Service Provider generally; they contain no sentence about AI, machine learning or model training (I searched the full text for "AI", "artificial", "machine learning", "model", "train": no hits). So an LLM API is governed by the general Service Provider clause, nothing AI-specific. | CONFIRMED (definition); AI-specific rule absent | "an entity you use to provide you services in connection with Platform or any Platform Data" [TERMS 12.r] |
| Condition for using a Service Provider | The provider must first agree in writing. | CONFIRMED | "unless such Service Provider first agrees in writing to do the following" [TERMS 5.a.i] |
| What the written agreement must say | Use Platform Data solely for you, at your direction, for the services you requested, not for the provider's own purposes; flow the same down to sub-service providers. | CONFIRMED | "Use Platform and Process Platform Data solely for you and at your direction" [TERMS 5.a.i.1] |
| Responsibility and audit | You answer for the provider; Meta may ask for the provider list, data types and volumes, and the written agreements; Meta can force you to stop using a provider. | CONFIRMED | "you are responsible for their acts and omissions" [TERMS 5.a.ii]. "proof of written agreements with your Service Providers" [TERMS 5.a.iv] |
| Sharing allowed | Non-Tech-Provider data may go "with your Service Provider in order for them to provide the services you requested". | CONFIRMED | [TERMS 3.c.ii.2] |
| Sharing allowed as a Tech Provider (our case: customers' accounts) | Only "with your Service Provider solely to the extent necessary for your applicable Client's Purpose", with the Client, or when law requires. | CONFIRMED | "solely to the extent necessary for your applicable Client's Purpose" [TERMS 5.b.ii.4.c] |
| Deletion on provider exit | When you stop using a Service Provider it must stop and "promptly delete all Platform Data in their possession or control". | CONFIRMED | [TERMS 5.a.iii] |
| Derived data counts | Platform Data includes data "anonymized, aggregated, or derived from such data", so model inputs and outputs built from comment text are Platform Data. | CONFIRMED | "including data anonymized, aggregated, or derived from such data" [TERMS 12.l] |
| Prohibited related practices | No selling/licensing Platform Data; no processing for purposes outside the permitted purposes in the Developer Docs; no profile-building without consent. Model training on comment text by us or the provider is therefore at least a risk; the Terms do not mention it explicitly. | CONFIRMED (text); training status UNVERIFIED | "Selling, licensing, or purchasing Platform Data." "Processing Platform Data for purposes other than the applicable permitted purposes set forth in Meta's Developer Docs." [TERMS 3.a.iv, viii] |
| Is Anthropic's agreement sufficient | Out of scope for Meta docs; needs Anthropic's terms read against 5.a.i/5.b (existing items M4, I6). | UNVERIFIED | n/a |
| Rule about AI-generated replies on IG/FB comments | **None found.** No page I opened requires disclosing that a comment reply is AI-written. What does exist: (a) for **messaging** automations, "When required by applicable law, automated chat experiences must disclose that a person is interacting with an automated service"; (b) for **published media**, an optional `is_ai_generated` flag; (c) general policy lines below. | UNVERIFIED (docs silent for comments) | "When required by applicable law, automated chat experiences must disclose" [OVR] |
| General policies that bear on AI replies | "Don't confuse, deceive, defraud, mislead, spam or surprise anyone." "Obtain consent from people before publishing content or taking any other action on their behalf." Spam definition includes "creating bots either manually or automatically, at very high frequencies". | CONFIRMED | [DEVPOL 1, 5] |
| Policy that may affect an inbox product | "Don't use the Instagram Platform to simply display User Content, import or backup content, or manage Instagram relationships, without our prior permission." Our product replies and moderates, not only displays, but Meta's reading of "manage Instagram relationships" is not defined. | CONFIRMED (text); impact UNVERIFIED | [DEVPOL 6] |
| Data deletion: requirement | Apps that access user data must offer a deletion path and say so in the privacy policy. The Terms require an "easily accessible and clearly marked way" and, for Tech Providers, deletion when the User or the Client asks or the Client's account ends. | CONFIRMED | "Update or delete Platform Data promptly after receiving a request from us or the User to do so" [TERMS 3.d.i.1]. "for Tech Providers, when a User or the Client requests their Platform Data be deleted" [TERMS 3.d.i.2.d] |
| Data Deletion Request Callback (App Dashboard setting) | HTTPS URL in **Data Deletion Request URL**; Meta POSTs a `signed_request` (HMAC-SHA256 with the app secret) containing the app-scoped `user_id`; you start deletion and return JSON `{ url, confirmation_code }` where `url` shows human-readable status. Failure can mean the callback is removed or the app disabled. | CONFIRMED | "Your callback must use the secure HTTPS protocol and must be listed in the **Data Deletion Request URL** field" [DEL]. "Return a JSON response that contains a URL where the user can check the status of their deletion request and an alphanumeric confirmation code." [DEL] |
| Callback is optional if an instructions URL exists | Either an instructions URL or a callback URL must be set in Basic Settings. | CONFIRMED | "Developers need to specify either a data deletion callback instruction URL or a callback URL found in Basic Settings for your app." [DEL] |
| Other deletion signals | Every 21 days Meta emails a list of user IDs to delete (downloadable from Advanced settings, 60-day expiry); `business_integration_uninstall` (Unified/FLB) signals a client removing the integration. | CONFIRMED | "Every 21 days, you will receive an alert including the user identifiers you are required to delete" [DEL] |
| Does the callback fire for Instagram-login (Instagram-only) users | The page describes Facebook profile Apps and Websites settings only. Whether a customer who connected through Instagram sees "Send Request" is not stated. | UNVERIFIED | [DEL] |

---------------------------------------------------------------------------------------------------

## I. Rate limits

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Instagram Platform | Business Use Case (BUC) limits, except Business Discovery and Hashtag Search (Platform limits). Per app and app-user (customer account) pair over a rolling 24 hours: calls = 4800 x impressions, where impressions = times any content from that account entered a person's screen in the last 24 hours. Messaging has separate limits. | CONFIRMED | "Calls within 24 hours = 4800 * Number of Impressions" [RL], [OVR] |
| Meaning for small customer accounts | The quota scales with the account's reach, so a low-reach account gets a low call budget. Meta advises webhooks to reduce calls. | CONFIRMED | "Using webhooks will reduce the number of needed API calls made by your app and hence, reducing the risk of being rate limited." [OVR] |
| Pages | BUC limits apply to Pages API calls made with a **Page or system-user token**: calls per rolling 24 h = 4800 x engaged users (users who engaged with the Page per 24 h). Calls with a user or app token fall under Platform limits instead (200 x users per hour for app tokens; user limits hidden). | CONFIRMED | "Calls within 24 hours = 4800 * Number of Engaged Users" [RL]. "requests made with system user or page access tokens are subject to Business Use Case Rate Limits" [RL] |
| Error codes | Platform: 4 (app), 17 (user), 32 (Pages API, user or app), 613 (custom). Page-specific: 80001 "There have been too many calls to this Page account." | CONFIRMED | "(#32) Page request limit reached" [RL]. "80001 There have been too many calls to this Page account" [FB-PH] |
| Usage headers | `X-App-Usage` (platform), `X-Business-Use-Case` (BUC, including `estimated_time_to_regain_access`), once enough calls are made. | CONFIRMED | "Check the X-App-Usage HTTP header to see how close your app is to its limit" [RL] |
| Behavior when limited | Stop calling; continued calls extend the lockout. | CONFIRMED | "When the limit has been reached, stop making API calls." [RL] |
| Instagram publishing | Separate cap, see section D. | see D | |
| Private replies | 750 calls/hour per account (posts and reels), 100/second for Live. | CONFIRMED | [RL] |

---------------------------------------------------------------------------------------------------

## Corrections to docs/PLATFORM-CAPABILITIES.md implied by this research

1. I1 resolved: Unified Login or Instagram Login need no Page; the current publishing guide no longer says the account must be connected to one.
2. I2 not resolved by the docs: 100 (guide "Rate Limit") vs 50 (guide carousel limit and the endpoint reference "currently 50"). Read the live value.
3. I3 resolved except development-mode delivery: fields, per-account `subscribed_apps`, payload text, Live-mode requirement all documented now.
4. I4 resolved: endpoints in section C; the author shown on a reply is not stated.
5. I5 partly resolved: SUAT expiry is configurable (default never); one Facebook Login for Business token can hold Pages and IG accounts; the Instagram-tab configuration screen only documents the IG and ad-account assets.
6. Section 2d "Standard Access only" does not hold for a SaaS: customers without a role on our app need Advanced Access, Business Verification, App Review and Live mode.
7. Access Verification for Tech Providers was retired 2026-10-09; do not plan around it.

---------------------------------------------------------------------------------------------------

## URLs opened and what I read from each

Key in brackets is used above. `.md` mirror pages were fetched with curl; `/docs/...` pages as rendered HTML.

- [OVR] https://developers.facebook.com/documentation/instagram-platform/overview.md  Three setups, comparison table (token, host, Page, portfolio), access levels, tasks, token lifetimes, rate limits, webhook advice, automated-chat disclosure.
- [UL] https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-unified-login.md  Unified Login summary, comparison, SUAT ownership.
- [UL-GS] https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-unified-login/get-started.md  Configuration, login URL, code exchange, client_business_id, assigned_instagram_accounts, webhook step, App Review step.
- [BO] https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-unified-login/business-onboarding.md  Customer screens, portfolio selection, one connection per portfolio.
- [UL-AUTH] https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-unified-login/authentication.md  Expiry set by configuration, invalidation conditions, token storage.
- [UL-PERM] https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-unified-login/permissions.md  Permission list, Advanced Access and Live mode.
- Eligibility page: https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-unified-login/eligibility-and-troubleshooting.md  App needs, troubleshooting table.
- [IGL] https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-instagram-login.md  No Page required; cannot access ads or tagging.
- [IGFB] https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-facebook-login.md  Limitations incl. Stories business-only.
- Instagram Login business login: https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-instagram-login/business-login.md  60-day tokens, refresh endpoint, Advanced Access line.
- Instagram Login get started: https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-instagram-login/get-started.md  Token types and lifetimes.
- Facebook Login get started: https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-facebook-login/get-started.md  Fetched, skimmed only.
- [WH] https://developers.facebook.com/documentation/instagram-platform/webhooks.md  Overview, no historical store, setups table.
- [WH-SETUP] https://developers.facebook.com/documentation/instagram-platform/webhooks/setup.md  Requirements table (Live mode, Advanced Access, Business Verification), limitations, verification, signature, retries, per-account `subscribed_apps` for each setup.
- [WH-F] https://developers.facebook.com/documentation/instagram-platform/webhooks/fields.md  Field-to-permission matrix per setup.
- [WH-EX] https://developers.facebook.com/documentation/instagram-platform/webhooks/examples.md  Payloads for comments, live_comments, mentions.
- [CP] https://developers.facebook.com/documentation/instagram-platform/content-publishing.md  Limits (100 and 50), JPEG, public server, containers, carousel, reels, stories, AI flag, status_code.
- [CP-RU] https://developers.facebook.com/documentation/instagram-platform/content-publishing/resumable-uploads.md  rupload flow, FLB-only, status fields.
- [IG-MEDIA] https://developers.facebook.com/documentation/instagram-platform/instagram-graph-api/reference/ig-user/media.md  Image, reel, story specs; 400 containers/24h; parameters.
- [CPL] https://developers.facebook.com/documentation/instagram-platform/instagram-graph-api/reference/ig-user/content_publishing_limit.md  quota_total "currently 50".
- Container reference: https://developers.facebook.com/documentation/instagram-platform/instagram-graph-api/reference/ig-container.md  status_code values.
- [CM] https://developers.facebook.com/documentation/instagram-platform/comment-moderation.md  Comment endpoint list, webhooks vs API.
- [IG-C] https://developers.facebook.com/documentation/instagram-platform/instagram-graph-api/reference/ig-comment.md  Fields, hide, delete, limits.
- [IG-R] https://developers.facebook.com/documentation/instagram-platform/instagram-graph-api/reference/ig-comment/replies.md  Reply and read replies, limits.
- [IG-MC] https://developers.facebook.com/documentation/instagram-platform/instagram-graph-api/reference/ig-media/comments.md  Read comments limits (top-level, 50), ordered_comments.
- [IG-AR] https://developers.facebook.com/documentation/instagram-platform/app-review.md  Tech Provider scenario table, review flow.
- [IG-CREATE] https://developers.facebook.com/documentation/instagram-platform/create-an-instagram-app.md  Business Verification before publishing; 15-app limit.
- Private replies: https://developers.facebook.com/documentation/instagram-platform/private-replies.md  Fetched, skimmed only.
- Changelog: https://developers.facebook.com/documentation/instagram-platform/changelog.md  Searched for publishing-limit changes: none found.
- [FLB] https://developers.facebook.com/documentation/facebook-login/facebook-login-for-business.md  SUAT vs user token, never-expire default, 60-day option, token API, supported `pages_*`/`instagram_*` permissions, Advanced Access, public_profile requirement.
- [FLBW] https://developers.facebook.com/documentation/facebook-login/facebook-login-for-business/integration-webhooks.md  install/uninstall/update webhook fields.
- [MODES] https://developers.facebook.com/documentation/development/build-and-test/app-modes.md  Development vs Live.
- [AL] https://developers.facebook.com/documentation/development/graph-api/overview/access-levels.md  Standard vs Advanced, Business Verification, DUC.
- [BV] https://developers.facebook.com/documentation/development/release/business-verification.md  Verification requirements.
- [AV] https://developers.facebook.com/documentation/development/release/access-verification.md  Legacy Tech Provider process, retired notice.
- [BLOG] https://developers.facebook.com/blog/post/2026/10/09/retiring-access-verification-for-tech-provider-apps  Retirement of Access Verification, what is unchanged.
- [AR] https://developers.facebook.com/documentation/resp-plat-initiatives/individual-processes/app-review.md  When App Review is needed, reviewer access.
- [AR-C] https://developers.facebook.com/documentation/resp-plat-initiatives/individual-processes/app-review/content.md  App verification, permissions review, Business Verification, data handling questions, app settings.
- [AR-SG] https://developers.facebook.com/documentation/resp-plat-initiatives/individual-processes/app-review/submission-guide.md  Prerequisites, 30-day API call, decision within a week, Live-mode warning.
- [DPA] https://developers.facebook.com/documentation/resp-plat-initiatives/individual-processes/data-protection-assessment.md  Annual questionnaire, 60 days.
- DPA FAQ: https://developers.facebook.com/docs/resp-plat-initiatives/data-protection-assessment/faq  "required for developers who have apps that access certain types of data".
- [DUC] https://developers.facebook.com/documentation/resp-plat-initiatives/individual-processes/data-use-checkup.md  Annual, before Live mode.
- Data handling questions: https://developers.facebook.com/documentation/resp-plat-initiatives/individual-processes/data-handling-questions.md  Applies to Advanced Access apps.
- Ongoing reviews: https://developers.facebook.com/documentation/resp-plat-initiatives/individual-processes/ongoing-reviews.md  FLB reduced scope.
- Not readable (returned page shell, no content): .../individual-processes/app-review/AR-FAQs.md, .../data-protection-assessment/content.md, .../individual-processes.md.
- [DEL] https://developers.facebook.com/documentation/development/create-an-app/app-dashboard/data-deletion-callback.md  Data Deletion Request Callback, FAQ.
- [TERMS] https://developers.facebook.com/terms/  Platform Terms (last updated 2026-02-03): sections 3, 4, 5 (Service Providers and Tech Providers), 12 glossary.
- [DEVPOL] https://developers.facebook.com/devpolicy/  Developer Policies (last updated 2026-02-03): sections 1, 5, 6.
- [PERM] https://developers.facebook.com/docs/permissions  Allowed usage for `instagram_manage_comments`, `instagram_content_publish`, `pages_*`.
- [RL] https://developers.facebook.com/docs/graph-api/overview/rate-limiting/  Platform and BUC limits, Instagram and Pages formulas, error codes.
- [PG-POSTS] https://developers.facebook.com/documentation/pages-api/posts.md  Page posts, photos, scheduling, permissions.
- Pages overview: https://developers.facebook.com/documentation/pages-api/overview.md  App Review/Advanced Access statement for Business apps.
- Pages comments: https://developers.facebook.com/documentation/pages-api/comments-mentions.md  Skimmed for access rules.
- [PG-WH] https://developers.facebook.com/documentation/pages-api/webhooks-for-pages.md  Page `feed` subscription requirements.
- [FB-PH] https://developers.facebook.com/docs/graph-api/reference/page/photos/  Photo specs, url vs multipart, multi-photo, 10 MB, temporary photos, error 80001.
- [VID] https://developers.facebook.com/documentation/video-api/guides/publishing  Page video publish and Resumable Upload API.
- Webhooks getting started: https://developers.facebook.com/docs/graph-api/webhooks/getting-started  Checked for development-mode statements: none found.
- Index pages used to discover the above: https://developers.facebook.com/documentation/instagram-platform/llms.txt, https://developers.facebook.com/documentation/pages-api/llms.txt, https://developers.facebook.com/documentation/development/llms.txt, https://developers.facebook.com/documentation/facebook-login/llms.txt
- Also fetched through WebSearch (summary only, not relied on): a search for Tech Provider requirements.

Raw copies of the pages were not kept; each row cites its URL and quotes the text it relies on.
