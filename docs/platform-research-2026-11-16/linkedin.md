# LinkedIn capabilities research for GeekSocial (multi-tenant): media, tiers, tokens, webhooks, policy

Researched 2026-11-16 from official pages only: learn.microsoft.com/linkedin, plus linkedin.com/legal and linkedin.com/help
pages that the Microsoft docs link to. Nothing was run against a live app. No repo was written to.

Legend: CONFIRMED = stated in an official page I opened, quote and URL given. UNVERIFIED = docs silent, conflicting or
unopened. BLOCKED = unsupported, or needs an approval we do not have.

Read first: /Users/jeffmartin/development/GeekBackend/docs/PLATFORM-CAPABILITIES.md. Nothing it already confirms is repeated
below except where the current page changes it.

Notation: "doc 202610" means the learn.microsoft.com page rendered at `?view=li-lms-2026-10`. Pages under `/shared/` are
unversioned.

---

## 0. API version and sunset dates (affects every call)

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Current latest version | October 2026, header `Linkedin-Version: 202610`. The existing doc's 202610 is correct | CONFIRMED | "Latest Version ... October 2026 / 202610" https://learn.microsoft.com/en-us/linkedin/marketing/versioning?view=li-lms-2026-10 |
| Header is mandatory, no default | A missing or deprecated header returns an error; the latest version is not applied for you | CONFIRMED | "the latest version is not applied by default" (same page) |
| Support window | Monthly releases, each supported at least one year | CONFIRMED | "supported for a minimum of one (1) year" (same page) |
| 202511 sunsets TODAY | Every page carries a deprecation banner. The migrations table still lists 202511 as "Active" with sunset November 16, 2026 | CONFIRMED | "Marketing Version 202511 (Marketing November 2025) will be sunset on November 16, 2026." (banner on every page below) |
| 202510 and older | Listed "Deprecated" | CONFIRMED | https://learn.microsoft.com/en-us/linkedin/marketing/integrations/migrations?view=li-lms-2026-10 |
| Sunset dates that matter | 202601 = Jan 15 2027; 202602 = Feb 15 2027; 202603 = Mar 16 2027; 202604 = Apr 15 2027; 202605 = May 15 2027; 202606 = Jun 15 2027; 202607 = Jul 15 2027; 202608 = Aug 17 2027; 202609 = Sep 15 2027; 202610 = Oct 15 2027 | CONFIRMED | Same migrations table, e.g. "version 202610 (October 2026) ... October 15, 2027 / Active" |
| Changes 202607 to 202610 touching our topics | None for Posts, Images, Documents, MultiImage, Comments, Reactions, thumbnails, refresh tokens or tiers. Only 202610 added Boost endpoints (Advertising API product, not ours). The Videos `templateName` and `linkbackContext` fields arrived in 202602 | CONFIRMED (the recent-changes page was read only to character 100000 of 143161; older entries were not read) | https://learn.microsoft.com/en-us/linkedin/marketing/integrations/recent-changes?view=li-lms-2026-10 |

---

## A. MEDIA RULES

### A1. Images API (single image and multi-image)

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Step 1, register upload | `POST https://api.linkedin.com/rest/images?action=initializeUpload`, body `{"initializeUploadRequest":{"owner":"urn:li:organization:..."}}`. Owner may be a person or organization URN | CONFIRMED | "Use the `initializeUpload` action to register the upload." https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/images-api |
| Response | `200` with `value.uploadUrl`, `value.uploadUrlExpiresAt` and `value.image` = `urn:li:image:...` (the image URN) | CONFIRMED | "A successful response returns a 200 OK status code with the uploadUrl to upload the image." (images-api) |
| Step 2, binary upload | The Images page only says to send the image to `uploadUrl` and links to the Assets page for the method. That page says PUT with the OAuth bearer token on images. Method and auth on the Images API itself are inherited by reference, not restated | CONFIRMED (by reference) | "Use a PUT method to upload the image." and "The upload call requires a valid OAuth token in the 'Authorization' header." https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/vector-asset-api?view=li-lms-2026-10 |
| Image by public URL? | **BINARY UPLOAD ONLY.** No page in the Images, Posts, MultiImage, Documents or Videos docs accepts a public URL for media. The only mention of a URL is the article `source` link, and the Article Ads page says a thumbnail "must be fetched and uploaded" | CONFIRMED (absence plus the quote) | "to include a thumbnail, the image must be fetched and uploaded if it's to be included." https://learn.microsoft.com/en-us/linkedin/marketing/integrations/ads/advertising-targeting/version/article-ads-integrations?view=li-lms-2026-10 |
| Synchronous upload | Not supported on Images | CONFIRMED | "`SYNCHRONOUS_UPLOAD` is not supported in Images API." (images-api) |
| Poll before use | The image has a `status`: PROCESSING, PROCESSING_FAILED, AVAILABLE, WAITING_UPLOAD. `GET /rest/images/{urn}` reads it. A failed image never shows; the Assets page warns that a post made before the upload succeeds "won't be visible to members" | CONFIRMED | "AVAILABLE - All of the recipe's required artifacts are ready." (images-api); "the post won't be visible to members." (vector-asset-api) |
| Accepted formats | JPG, GIF, PNG. GIF up to 250 frames. (Conflict: the Assets page also says "Currently, only jpeg and png formats are supported" in its recipe paragraph) | CONFIRMED (Images and MultiImage pages); conflict on the legacy Assets page | "JPG, GIF, and PNG formats" and "GIF format supports up to 250 frames" (images-api and multiimage-post-api) |
| Max pixel count | Strictly less than 36,152,320 pixels in total (about 36 megapixels) | CONFIRMED | "Images with less than 36,152,320 pixels." (images-api) |
| Max file size (MB) | No number in any Images/MultiImage/Posts page. A `413 REQUEST_ENTITY_TOO_LARGE` exists on the legacy Assets page with no limit stated | UNVERIFIED | Assets error table: "The uploaded file exceeds the allowed size limit." (vector-asset-api) |
| Max width/height, aspect ratio for organic image posts | Not stated. Only the response fields `aspectRatioWidth/Height` exist | UNVERIFIED | (silent) |
| Image post | `POST /rest/posts` with `content.media = {"altText": "...", "id": "urn:li:image:..."}`, `author` = organization or person URN, `lifecycleState: PUBLISHED`; `201` and the post URN in header `x-restli-id` | CONFIRMED | "A successful response returns a `201 Created` HTTP status code and the ID in the `x-restli-id` response header." (images-api) |
| Alt text | Optional, max 4,086 characters, recommended under 120 | CONFIRMED | "Maximum length is 4,086 characters, recommended length is less than 120 characters." (images-api) |
| Who may own an image | Company owner: caller needs ADMIN or DSC role on the Page. Member owner: caller must be that member | CONFIRMED | "For images with member URN owners, the caller must match the image owner." (images-api) |
| Scopes for `/rest/images` | `rw_ads`, `w_member_social`, `w_organization_social`, `w_power_creators`. A token holding only `w_member_social` is write-only and cannot GET `/rest/images` on the versioned gateway, so it cannot poll `status` | CONFIRMED | "tokens with only `w_member_social` permissions would be unable to perform a GET call for rest/images." (images-api) |
| Multi-image post | `content.multiImage.images[]`, each `{id: urn:li:image:..., altText}`. Min 2, max 20. Organic only (sponsored uses Carousel) | CONFIRMED | "minimum of 2 images and maximum of 20 images" and "API partners can only create non-sponsored multiImage posts." https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/multiimage-post-api |
| Multi-image owner rule | All images must belong to the post author | CONFIRMED | "Please ensure that all images being used are owned by the author or organization." (multiimage-post-api) |
| Organic content types table | Text, Images, Videos, Documents, Article, MultiImage, Poll, Celebration are organic. Carousel is sponsored only | CONFIRMED | "Carousels | No | Yes" https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/posts-api |

### A2. Videos API

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Flow | 1. `POST /rest/videos?action=initializeUpload` with `owner`, `fileSizeBytes`, `uploadCaptions`, `uploadThumbnail`. 2. Split into 4 MB parts. 3. PUT each part to its `uploadInstructions[].uploadUrl` with `Content-Type: application/octet-stream`. 4. `POST /rest/videos?action=finalizeUpload` with `video`, `uploadToken`, `uploadedPartIds` (the ETags, in order) | CONFIRMED | "Split the file into 4 MB each (`split -b 4194303`)." https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/videos-api |
| Auth on part upload | The Videos sample PUT carries no Authorization header, and the Assets page says video upload must not send the OAuth token | CONFIRMED | "The upload call shouldn't include an OAuth token in the 'Authorization' header." (vector-asset-api) |
| URL or binary | **BINARY (multipart) upload.** No URL ingestion | CONFIRMED (absence) | (videos-api, no such field) |
| Length | 3 seconds to 30 minutes | CONFIRMED | "Length: Three seconds to 30 minutes." (videos-api) |
| File size | CONFLICT inside one page: 75 KB to 500 MB in the specification list, but `fileSizeBytes` says 5 GB | CONFIRMED (both quotes) with conflict | "File size: Between 75kb and 500MB." and "Maximum allowed Videos size is 5GB." (videos-api) |
| Format | MP4 | CONFIRMED | "File format: MP4." (videos-api) |
| Resolution, aspect, codec | The Videos page defers to the linked Video Ads spec: 16:9 max 1920x1080; 1:1 max 1920x1920; 4:5 max 1080x1350; 9:16 max 1080x1920; H.264 or VP8; AAC audio; under 30 FPS. That page also says "25 videos in 24 hours ... desktop", which is the Campaign Manager UI limit, not shown to apply to the API | CONFIRMED (ad spec, applicability to organic posts is an inference) | "Refer to Video Ads Advertising Specifications" (videos-api); spec page https://www.linkedin.com/help/lms/answer/a424737/video-ads-advertising-specifications?lang=en (read through the fetch tool's summary: "Video codec: H.264 or VP8", "File size: 75 KB to 500 MB") |
| Thumbnail and captions | Optional uploads. Thumbnail JPG or PNG max 2 MB, aspect ratio and resolution must match the video. One caption file only, English only | CONFIRMED | "Each video can include only one caption file, and only English-language captions are supported." (videos-api); thumbnail limits from the linkedin.com/help page above |
| Upload URL lifetime | About 30 days | CONFIRMED | "Typically URLs expire 30 days from the time an upload is initialized." (videos-api) |
| Video post | `content.media = {"title": "...", "id": "urn:li:video:..."}` | CONFIRMED | https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/posts-api |
| Failure signal | `status` PROCESSING_FAILED plus `processingFailureReason` | CONFIRMED | "Reason for video processing failure." (videos-api) |

### A3. Documents API (PDF)

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Limits | 100 MB, 300 pages. Types: PPT, PPTX, DOC, DOCX, PDF | CONFIRMED | "The file size can't exceed 100MB and 300 pages. The following file types are supported: PPT, PPTX, DOC, DOCX, and PDF." https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/documents-api |
| Flow | `POST /rest/documents?action=initializeUpload` returns `uploadUrl` and `urn:li:document:...`; then `curl --upload-file` to `uploadUrl` (sample includes the bearer token); `201` | CONFIRMED | "Using the `uploadUrl` returned from the previous step, upload the document." (documents-api) |
| URL or binary | **BINARY upload only** | CONFIRMED (absence) | (documents-api) |
| Document post | `content.media = {"title": "Example.pdf", "id": "urn:li:document:..."}` | CONFIRMED | (documents-api) |
| Same as images | `SYNCHRONOUS_UPLOAD` is not supported | CONFIRMED | "`SYNCHRONOUS_UPLOAD` is not supported in Documents API." (documents-api) |

---

## B. ARTICLE / LINK POST: THUMBNAIL AND REQUIRED FIELDS

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| No URL scraping (unchanged) | Confirmed again on the current Posts page and on the Article page | CONFIRMED | "The Posts API does not support URL scraping for article post creation" https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/posts-api |
| Which fields are required | Per the Article schema: `source` required, `title` required, `description` optional, `thumbnail` optional, `thumbnailAltText` optional | CONFIRMED | "thumbnail | ImageUrn | Custom or saved thumbnail for the article. If empty, there's none." https://learn.microsoft.com/en-us/linkedin/marketing/integrations/ads/advertising-targeting/version/article-ads-integrations?view=li-lms-2026-10 |
| Conflict with the Posts page | The Posts page says partners "must set article fields such as thumbnail, title, and description". The Article page says the thumbnail is optional. The existing doc's "must be supplied" is the stricter reading | CONFIRMED (conflict) | "API partners must set article fields such as thumbnail, title, and description within the post" (posts-api) vs "thumbnail is optional when creating an Article Post" (article-ads-integrations) |
| Length limits | `title` under 400 characters; `description` under 4,086 | CONFIRMED | "The length must be less than 400 characters." (article-ads-integrations) |
| How to get a thumbnail | Upload the image with the Images API (binary) and pass the image URN in `thumbnail`. LinkedIn will not fetch it from the page's og:image | CONFIRMED | "use [Images API] to upload thumbnail image and use the ImageUrn on thumbnail field." (posts-api) |
| What the post looks like with no thumbnail | Not documented | UNVERIFIED | (silent) |
| Thumbnail size and aspect limits | Not documented beyond the Images API pixel cap | UNVERIFIED | (silent) |

---

## C. MULTI-TENANT ACCESS (Community Management API tiers)

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Two tiers | Development (initial approval, limited calls) and Standard (full access, requires upgrade) | CONFIRMED | "Development Tier: Initial approval with limited API call volume." https://learn.microsoft.com/en-us/linkedin/marketing/community-management/community-management-overview?view=li-lms-2026-10 |
| Development tier limits | 500 calls per app and 100 per member, each per 24 hours. BATCH_GET: no calls allowed. Social-action webhooks: push notifications disabled | CONFIRMED | "500 API calls for an app for 24 hrs ... 100 API calls per member of an App for 24 hrs ... Push notifications disabled" https://learn.microsoft.com/en-us/linkedin/marketing/increasing-access?view=li-lms-2026-10 |
| Development tier time box | Integration and testing must be done within 12 months | CONFIRMED | "You must complete your integration and testing within twelve months of receiving the API access." (increasing-access) |
| Standard tier | "full-featured", "No restrictions", "designed for live production" | CONFIRMED | "Standard tier is designed for live production with no restrictions." (increasing-access) |
| Can Development tier connect OTHER customers' Pages? | No page says yes and no page says no. Development approval checks the app's own organization, website and Page-association; the docs never state which organizations' tokens the app may call on. The only text bearing on it is the purpose statement ("manage LinkedIn company pages for clients") and the limit design ("100 per member"). Settle with a live test of a second company's admin on a Development-tier app, or ask LinkedIn Developer Support | UNVERIFIED | "manage LinkedIn company pages for clients" (overview); "Development tier is designed to build and test integrations with API call restrictions." (increasing-access) |
| Serving other organizations in production | Standard is the production tier. The review asks for an app-user OAuth flow where "an application user approving access to their LinkedIn page data" and posting "to their LinkedIn page via your app", that is, someone other than the app owner | CONFIRMED (that Standard is the production path and tests the customer-connects-own-Page flow) | "Demonstrate an application user approving access to their LinkedIn page data via the complete OAuth flow." https://learn.microsoft.com/en-us/linkedin/marketing/community-management-app-review?view=li-lms-2026-10 |
| Pre-requisites for Development tier | Registered legal organization, commercial use only; verified business email (personal addresses fail); legal name, registered address, website, privacy policy; Page super admin must verify the app; no LinkedIn or Microsoft names or logos | CONFIRMED | "our Community Management APIs are only available to registered legal organizations for commercial use cases only." (app-review) |
| Development review checks | Use case, business email, organization, website and domain, app verified by the Page | CONFIRMED | "Application verified by LinkedIn Page associated with same organization" (app-review) |
| Development rejection | Cannot reapply on the same app | CONFIRMED | "You won't be able to re-apply for Development tier access with your existing app." (app-review) |
| Standard form | Found in the Developer Portal under My Apps > Products after Development approval | CONFIRMED | "you'll find a Standard tier access request form in the Developer Portal under My Apps > Products." (app-review) |
| Standard prerequisites | Development approved; Community Management APIs "fully integrated"; company and product overview; use case description; test credentials; screen recording; later functional changes "may require re-review" | CONFIRMED | "Prepare to provide a company and product overview, description of your use case, and test credentials for our reviewers" (app-review) |
| Screen recording content (Page Management) | OAuth approval by an app user; user posts to their Page; how a member's comment on that post shows in the app; which commenter profile fields are shown; any other member-data use. Recording must be "high resolution and downloadable", app screens only, narration recommended | CONFIRMED | "Demonstrate how a comment on that post by a member* is displayed to users in your app." (app-review) |
| Other use-case recordings | Brand Engagement, Page Analytics, Executive Management (member profile posting) and Employee Advocacy each have their own script | CONFIRMED | "For Executive Management Use Cases ... Demonstrate a user posting to their LinkedIn profile via your app." (app-review) |
| Screencast length | The older migration guide says under 5 minutes; the current app-review page gives no length limit | CONFIRMED (older page only) | "Limit length of screencast to under 5 min." https://learn.microsoft.com/en-us/linkedin/marketing/community-management/community-management-api-migration-guide?view=li-lms-2026-10 |
| Standard review criteria | Use case; valid privacy policy; compliance with terms and data-storage requirements; recording that shows all core functionality | CONFIRMED | "Valid privacy policy" and "Screen recording that accurately demonstrates all core functionality as outlined above" (app-review) |
| Standard rejection | Cannot reapply on the same app; a new app and a new Development request are needed | CONFIRMED | "You won't be able to re-apply for Standard tier access with your existing app." (app-review) |
| Review times for Community Management | No number stated for Development or Standard. The 30-business-day figure belongs to the Ads API tier upgrade. The survey must be finished within 21 days. A search snippet of a Microsoft Q&A thread (not opened, user report, not official) reports Standard stuck at "stage 1" for about 40 business days | UNVERIFIED | "The review process can take up to 30 business days due to the high volume of requests." (Ads API context only) https://learn.microsoft.com/en-us/linkedin/shared/linkedin-api-partner-support-guide ; "You must complete this survey within 21 days" (same page) |
| One product per app | Community Management must be the only product on the app or the request button is greyed out | CONFIRMED | "The Community Management API requires that it be the only product associated with a developer application due to legal and security constraints." (partner-support-guide) |
| Existing-app workaround (Ads API case) | Create a new app, get Dev then Standard there, then request Standard on the original app using the new app's client ID | CONFIRMED | "Enter your new developer application's client ID in the access form to skip most questions." (overview FAQ 1) |
| Conflicting FAQ | The older migration guide says Ads and Community Management products can share one app if verified with the same Page. The overview and partner guide say Community Management must be alone | CONFIRMED (conflict) | "both products can exist on the same developer application." (migration guide FAQ) vs the partner-guide quote above |
| Technical Sign Off | Described as "rolling out"; to start it you contact "your LinkedIn POC on the Business Development team"; a demo of listed capabilities follows. The app-review page does not list it as a requirement for Standard tier, so whether it gates Standard is not stated. Recent-changes ties it to partner certification | UNVERIFIED that it is required for Standard | "you must initiate a Technical Sign Off request by contacting your LinkedIn POC on the Business Development team." https://learn.microsoft.com/en-us/linkedin/marketing/community-management/integration-requirements-community-management |
| Sign-Off items relevant to us | CM-002 heartbeat `/me`; CM-005/006 use refresh tokens and detect their expiry; CM-009 data-storage compliance; CM-013 create posts in at least two media categories; CM-015 retrieve likes, comments and nested comments; CM-024 register webhooks for specific Company Pages; CM-027 de-register webhooks when a user unsubscribes; CM-028 recover from missed pushes. CM-022/023 cover configurable PULL intervals and time-bound PULL recovery | CONFIRMED | "CM-024 | Demonstrate ability to register webhooks for specific Company Pages." (same page) |
| Customer connects own Page | Each customer org admin runs 3-legged OAuth; org roles gate everything. Page access "cannot be granted or updated through the API" | CONFIRMED | "Access to a Company Page cannot be granted or updated through the API." https://learn.microsoft.com/en-us/linkedin/marketing/quick-start?view=li-lms-2026-10 |
| Headless profiles | Using one profile to manage many customers' accounts is a prohibited use | CONFIRMED | "Do not create fake profiles to manage ad accounts or use individual profiles to manage multiple customers' accounts." https://learn.microsoft.com/en-us/linkedin/marketing/restricted-use-cases?view=li-lms-2026-10 |
| Scope change invalidates tokens | Requesting a different scope than before invalidates all previous access tokens (matters when adding scopes after launch) | CONFIRMED | "If you request a different scope than the previously granted scope, all the previous access tokens are invalidated." https://learn.microsoft.com/en-us/linkedin/shared/authentication/authorization-code-flow |

### C-open. Items L3 and L4 from the existing doc

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| L3: which product grants `w_member_social` | The Community Management API product lists `w_member_social` (and `w_member_social_feed`) among its permissions, and the overview lists "Profile Management" as an approved use case. A separate Share on LinkedIn product also grants it | CONFIRMED | "Community Management API ... `w_member_social` ... `w_member_social_feed`" (increasing-access table); "Share on LinkedIn product which will grant you `w_member_social`." https://learn.microsoft.com/en-us/linkedin/consumer/integrations/self-serve/share-on-linkedin |
| L3: can Share on LinkedIn share an app with Community Management | The partner guide names "Share" as an example of another provisioned product that greys out the Community Management request. So do not add Share on LinkedIn to the app. Use the Community Management product's `w_member_social` | CONFIRMED (that Share is named as a blocker) | "already has other provisioned products (for example, Ads API, Share) ... the option to request the Community Management API is disabled" (partner-support-guide) |
| L3: does Development tier grant `w_member_social` | The scope appears in the product's permission list; tier tables change limits only, never scopes | UNVERIFIED for Development specifically (inference from the tables) | (increasing-access) |
| Member-profile reading | `r_member_social` is closed | CONFIRMED (unchanged) | "`r_member_social` is a **closed** permission. We're not accepting access requests at this time due to resource constraints." (overview FAQ 6) |
| Profile Management meaning | Posts, comments, reactions "while monitoring engagement" for individual profiles, yet reading engagement needs `r_member_social`, which is closed. The text promises monitoring but the scope for it is closed | CONFIRMED (conflict) | "Create and manage posts, comments, and reactions while monitoring engagement on behalf of individual profiles." (overview) |
| Share on LinkedIn limits (if ever used alone) | Member 150 requests/day, application 100,000/day, UTC. Page dated 2023 and uses legacy `/v2/ugcPosts` and `/v2/assets` | CONFIRMED (old page) | "Member | 150 Requests / Application | 100,000 Requests" (share-on-linkedin) |

---

## D. PROGRAMMATIC REFRESH TOKENS (existing item L4)

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Availability, page 1 | "all approved Marketing Developer Platform (MDP) partners" | CONFIRMED | "LinkedIn supports programmatic refresh tokens for all approved Marketing Developer Platform (MDP) partners." https://learn.microsoft.com/en-us/linkedin/shared/authentication/programmatic-refresh-tokens |
| Availability, page 2 (conflict) | "a limited set of partners ... if this feature has been enabled for your application" | CONFIRMED (conflict) | "Programmatic refresh tokens are available for a limited set of partners." https://learn.microsoft.com/en-us/linkedin/shared/authentication/authorization-code-flow |
| Is our app in scope | Community Management is a Marketing Developer Platform program, and the Technical Sign Off list expects refresh-token use (CM-005, CM-006). The docs never say a Development-tier app gets `refresh_token` in the token response. Only the first token response of a real app settles it | UNVERIFIED for our app and for Development tier | "CM-005 | Demonstrate ability to use refresh tokens to mint new access tokens." (integration-requirements) |
| Access token life | 60 days, fixed | CONFIRMED | "Currently, all access tokens are issued with a 60-day lifespan." (authorization-code-flow) |
| Refresh token life | 365 days fixed. Refreshing does not extend it. Each refresh gives a new 60-day access token. On day 360 both expire in 5 days; the member must re-authorize | CONFIRMED | "the lifespan or Time To Live (TTL) of the refresh token remains the same as specified in the initial OAuth flow (365 days)" (programmatic-refresh-tokens) |
| Fields returned | `refresh_token`, `refresh_token_expires_in` (seconds) only "if your application is authorized for programmatic refresh tokens" | CONFIRMED | "If your application is authorized for programmatic refresh tokens, the following fields are returned" (programmatic-refresh-tokens) |
| Sample values do not match prose | The sample shows `expires_in: 86400` (one day) and `refresh_token_expires_in: 525600`. 525600 is the number of minutes in 365 days, but the field is documented as seconds. Do not hard-code units; read the live response | CONFIRMED (inconsistent samples) | "The number of seconds remaining until the refresh token expires." vs sample `"refresh_token_expires_in": 525600` (programmatic-refresh-tokens) |
| Refresh call | `POST https://www.linkedin.com/oauth/v2/accessToken`, form body `grant_type=refresh_token`, `refresh_token`, `client_id`, `client_secret` | CONFIRMED | "The value of this field should always be refresh_token." (programmatic-refresh-tokens) |
| Revocation | LinkedIn may revoke at any time; the app must fall back to the login screen | CONFIRMED | "LinkedIn reserves the right to revoke Refresh Tokens or Access Tokens at any time" (programmatic-refresh-tokens) |
| Refresh quota | A per-app daily quota for minting tokens by refresh exists, with a Throttled state, reset at 00:00 UTC | CONFIRMED | "the total allowed quota per app per day, and whether the app is currently Throttled or Not Throttled." https://learn.microsoft.com/en-us/linkedin/shared/authentication/developer-portal-tools |
| Non-programmatic refresh | Without programmatic refresh tokens the only way is to run the authorization again, which skips the consent screen only if the member is still logged into linkedin.com and the old token has not expired | CONFIRMED | "the member is still logged into https://www.linkedin.com ... The member's current access token has not expired" (authorization-code-flow) |

---

## E. WEBHOOKS: ORGANIZATION SOCIAL ACTION NOTIFICATIONS

Existing doc facts (batch of 10, 3-second challenge, 2-hour revalidation, 3 failures block, `X-LI-Signature`, 5-minute redelivery for 8 hours, 60-day pull, PUBLIC-only, subscription lifetime) were re-checked on pages updated 2026-06-10 (notifications) and 2026-11-04 (webhook validation). All unchanged. Only the items below are new or different.

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Development tier: no push (settles L2) | Social-action webhooks are disabled in Development tier. Push delivery needs Standard | CONFIRMED | "Webhook for Social Actions: Push notifications disabled" https://learn.microsoft.com/en-us/linkedin/marketing/increasing-access?view=li-lms-2026-10 ; "Push notifications for Social Actions disabled" (migration guide) |
| Consequence for the no-timer-polling rule | In Development tier there is no push, so the only read path is a pull call made when a user or an operator action triggers it (for example opening the inbox). Nothing may be scheduled. Development tier also forbids BATCH_GET. This is a design inference from the two quotes | CONFIRMED (the restrictions) | (above) |
| Webhooks tab | Exists only for apps with an approved webhook use case | CONFIRMED | "This functionality is only available for applications with an approved use case for webhooks." https://learn.microsoft.com/en-us/linkedin/shared/api-guide/webhook-validation |
| Subscription | `PUT /rest/eventSubscriptions/(developerApplication:urn:li:developerApplication:{id},user:urn:li:person:{id},entity:urn:li:organization:{id},eventType:ORGANIZATION_SOCIAL_ACTION_NOTIFICATIONS)` body `{"webhook":"https://..."}`. One subscription per (app, member, organization, eventType). GET by key, GET by member, DELETE exist; `expiresAt` appears in the responses | CONFIRMED | "Each subscription record is uniquely identified by your app, the member, the organizational entity and the eventType." https://learn.microsoft.com/en-us/linkedin/marketing/community-management/organizations/organization-social-action-notifications?view=li-lms-2026-10 |
| Who can subscribe | The subscribing member must be an administrator of that organization and must have granted `rw_organization_admin` to our app. In a multi-tenant app each tenant admin must authorize and subscribe separately | CONFIRMED | "The subscription request will only succeed if the subscribing member is an administrator of the organization being subscribed to." (same page) |
| Event types | `action` is one of LIKE, COMMENT, SHARE, SHARE_MENTION, ADMIN_COMMENT, COMMENT_EDIT, COMMENT_DELETE | CONFIRMED (unchanged) | (same page, OrganizationSocialActionNotification schema) |
| Payload content: URNs only? (conflict with the existing doc) | The page says "only URNs", but the schema and sample include `decoratedSourcePost` and `decoratedGeneratedActivity`, which carry the comment `text` and the commenter URN as `owner`. Both fields "may not be populated if there was an error when decorating". So a payload can hold commenter text. The existing doc's "URNs only" is too strong | CONFIRMED (conflict) | "Notifications contain only URNs of entities related to the notification." vs "The decorated generated activity created as a result of acting on the sourcePost." (same page) |
| Subscription loss | Removed if the grant expires, the member revokes, or the member stops being an administrator. Detect via 401 (re-consent) and 403 (no longer admin); resubscribe, then pull missed items using the last-received timestamp and dedupe on `notificationId` | CONFIRMED | "A subscription will also be removed in the case that the member is no longer an administrator of the company they're subscribed to." (same page) |
| Notification skipped at send time | LinkedIn checks admin access and a valid grant at event time | CONFIRMED | "The notification will be skipped if these access requirements are not met at the time of the event." (same page) |
| Delivery | HTTPS only, 2xx, 3-second challenge, 2-hour revalidation, 3 failures block, redeliver every 5 minutes for 8 hours | CONFIRMED (unchanged) | "If the re-validation check fails 3 times in a row, the endpoint will move to a blocked state" (webhook-validation) |
| Pull for recovery | `GET /rest/organizationalEntityNotifications?q=criteria`, retained 60 days. LinkedIn: use it to bootstrap and for error handling; webhooks recommended otherwise | CONFIRMED (unchanged) | "Notifications are retained and available to pull for 60 days." (notifications page) |

---

## F. POLICIES

Quoted exactly as written. No interpretation is added. Whether text sent to an AI provider counts as a "transfer" is not decided by any page and stays open.

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Restricted uses, scope of data | Member data includes profile portions, identifiers, and activity (posts, comments, reactions); the restrictions apply to "any derivatives thereof" | CONFIRMED | "This member data (including any derivatives thereof) is subject to a number of restrictions under our terms" https://learn.microsoft.com/en-us/linkedin/marketing/restricted-use-cases?view=li-lms-2026-10 |
| Restricted uses, purpose | Only to manage Pages or Profiles via the application | CONFIRMED | "Accessing, using, or storing member data for any use case other than to manage LinkedIn Pages or Profiles via your application is a violation of our Marketing API Terms." (restricted-use-cases) |
| Restricted uses, display and transfer (L5) | Display only via the application; no export, distribution or transfer from it, including to customers | CONFIRMED | "Member data can only be displayed via your application. Member data can't be exported, distributed, or otherwise transferred from your application (including to your customers)." (restricted-use-cases) |
| Restricted uses, combining | No combining with your data, other LinkedIn data or third-party data to create or supplement profiles, leads or reference tables | CONFIRMED | "Member data can't be combined with your data, any other LinkedIn data, or any third party data" (restricted-use-cases) |
| Restricted uses, audience | Only people tied to that Page or Profile | CONFIRMED | "it may only be displayed to individuals associated with that Page or Profile." (restricted-use-cases) |
| Restricted uses, no advertising/sales/recruiting | Also bars "sending mass messages" and "social feed" uses of Community Management data | CONFIRMED | "none of the data provided via our Community Management APIs can be used in a social feed use case" (restricted-use-cases) |
| Any mention of AI, ML, LLMs, model providers on the restricted-use page | None. The page contains no such word | CONFIRMED (absence on this page) | (restricted-use-cases, full text read) |
| Marketing API Terms: AI clause | Fetch-tool extraction of https://www.linkedin.com/legal/l/marketing-api-terms found no clause on AI, machine learning or model training. Extraction was by a summarizing model, so re-read sections 3.1(e), 3.2(a), 4.1 and 4.2 on the page before relying on the exact words | UNVERIFIED (summarizer-extracted) | (none found) |
| Marketing API Terms: display of Member Data | Section 3.1(e) text as extracted: "you may only display the Member Data via your Marketing Application to support the Marketing Services for which the Member Data was made available to you." | CONFIRMED (summarizer-extracted excerpt) | https://www.linkedin.com/legal/l/marketing-api-terms |
| Marketing API Terms: third parties | Section 3.2(a) as extracted bars making the APIs, integrations or data available to any third party "other than your Clients". "service provider" appears only in the Lead Form and Page Messaging Data carve-out of 3.1(e) | CONFIRMED (summarizer-extracted excerpt) | "Make the Marketing APIs, Marketing API Integrations, or Marketing Data available to any third party other than your Clients" (same page) |
| LinkedIn API Terms of Use | Fetch-tool extraction found no AI or ML clause; sections 3.1(8) and 4.1 bar making Content available "to any third party" and storing it where a third party could access it. Which of the two terms governs a Community Management app is not stated on the pages I read | UNVERIFIED (summarizer-extracted; which terms apply not settled) | https://www.linkedin.com/legal/l/api-terms-of-use |
| AI-drafted replies as such | No page forbids or allows an AI drafting a reply that the Page then posts. The posted reply is the organization's own social activity (see storage row) | UNVERIFIED | (silent) |
| Storage: other members' comments and mentions | 48 hours. "the shortest duration applies" if a field fits more than one row | CONFIRMED | "Members' Social Activity Data ... 48 Hours" and "the shortest storage/caching duration shall apply." https://learn.microsoft.com/en-us/linkedin/marketing/data-storage-requirements?view=li-lms-2026-10 |
| Storage: other members' profile data | 24-hour caching, not stored | CONFIRMED | "nothing in these requirements or the LI MDP Terms shall permit you to cache this data in excess of 24 hours or store this data." (data-storage-requirements) |
| Storage: our own replies and posts | Six weeks; six months where the organization has authenticated into our app. If an org item "includes member content or results in a social action by a member", the member content stays at 48 hours and member profile data at 24 hours | CONFIRMED | "Six Weeks. If content is being pulled that relates to an organization that has authenticated into your application, you may store it for six months." (data-storage-requirements) |
| Storage: IDs and URNs | No limit on organization, post and social-action URNs and member person URNs | CONFIRMED | "No Restriction on Duration" (data-storage-requirements) |
| Storage: data given by clients | The storage rules do not cover data "independently provided to you by your clients and not retrieved via the LinkedIn Marketing APIs" | CONFIRMED | (data-storage-requirements, intro) |
| Deletion on request | The Marketing API Program Terms require deletion on a member's request (data-storage page). The Marketing API Terms (4.2, extracted) require deletion of data stored on behalf of a Client in ten days or less and on the Client's request. The API Terms of Use (4.4, extracted) require immediate deletion of User-collected Content on that User's request | CONFIRMED (first quote exact; the other two summarizer-extracted) | "delete data upon the member's request" (data-storage-requirements); "in ten (10) days or less" (marketing-api-terms 4.2); "You must immediately delete all Content collected through the APIs on behalf of a User" (api-terms-of-use 4.4) |
| Compliance is checked at Standard review | Standard review includes the data-storage requirements; approval does not shift responsibility | CONFIRMED | "you're solely responsible for ensuring that your integration (including all use, disclosure, and storage of LinkedIn data) is, at all times, in compliance" (app-review) |

---

## G. RATE LIMITS (Standard tier, as documented)

| Item | Finding | Status | Quote + URL |
|---|---|---|---|
| Standard tier numeric limits | Not published. The tier table says Standard has "No restrictions", which describes the tier, not a lifted rate limit; per-endpoint limits still apply and can only be read in the portal | CONFIRMED (that numbers are not published) | "Standard rate limits are not published in documentation." https://learn.microsoft.com/en-us/linkedin/shared/api-guide/concepts/rate-limits |
| Where to see them | Developer Portal > your app > Analytics tab. Shows only endpoints called at least once today (UTC); one test call makes an endpoint appear | CONFIRMED | "make 1 test call to that endpoint and refresh the Analytics page." (rate-limits) |
| Window and reset | 24 hours, resetting at midnight UTC | CONFIRMED | "These limits reset at midnight UTC every day." (rate-limits) |
| Two limit kinds | Application, and Member (one member token per application) | CONFIRMED | "The total number of calls that a single member per application can make in a day." (rate-limits) |
| Member limit and a partner managing many members | The page explains the member limit as the calls a partner can initiate "on behalf of a member token" | CONFIRMED | "the permissible number of API calls the partner can initiate from their application on behalf of a member token." (rate-limits) |
| 429 | Over-limit calls get 429; LinkedIn may also return 429 as infrastructure protection | CONFIRMED | "Rate limited requests will receive a 429 response." (rate-limits) |
| Alerts | Email to Developer Admins at 75% of the application quota, delayed 1 to 2 hours; application-level only | CONFIRMED | "Developer Admins will receive email alerts when their application exceeds 75% of the assigned rate limit quota." (rate-limits) |
| Comment-create throttle | A 1-minute creation limit per member | CONFIRMED | "Comment create throttled: creation rate limit exceeded for member" https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/comments-api?view=li-lms-2026-10 |
| Development tier | 500 per app, 100 per member, per 24 hours. "Per Member" went from 10 to 100 and "Per App" from 100 to 500 | CONFIRMED | "Per Member: Increased from 10 to 100 requests." (overview FAQ 3) |
| L6: per day? | Yes: both Development limits are per 24 hours | CONFIRMED | "100 API calls per member of an App for 24 hrs" (increasing-access) |

---

## H. Existing open items, settled or not

| ID | Result | Status |
|---|---|---|
| L1 scope names | Both pairs are listed on the Community Management product. `r_organization_social` and `w_organization_social` cover Posts; `r_organization_social_feed`, `w_organization_social_feed` and `w_member_social_feed` cover `/reactions`, `/socialActions/comments`, `/socialActions/likes`, `/socialMetadata`. The Comments page lists only the `_feed` scopes for comments. So: posts need `*_organization_social`; comments and reactions need `*_organization_social_feed`. Request both pairs | CONFIRMED. Sources: increasing-access, comments-api, migration guide |
| L2 webhooks in Development tier | Push notifications for social actions are disabled in Development tier. Webhooks need Standard | CONFIRMED |
| L3 `w_member_social` product | The Community Management product lists it. Share on LinkedIn grants it too but is named as a blocker for the Community Management request. Whether Development tier issues it is not stated | CONFIRMED (the first two); UNVERIFIED (Development) |
| L4 refresh tokens | Mechanics and lifetimes confirmed (60-day access, 365-day refresh, no extension). Availability for our app is documented two conflicting ways | UNVERIFIED for our app |
| L5 comment text to Claude API | No page mentions AI. The restricted-use page forbids "export, distribute or otherwise transfer from your application". No page rules whether a model call is that. Needs LinkedIn's written answer | UNVERIFIED. Quotes in section F |
| L6 100 per member | Per 24 hours | CONFIRMED |

Changes the existing doc should take from this pass:
1. Webhooks are Standard-only (L2). The existing text implies they could arrive at Development tier.
2. A Development-tier app has no push and no BATCH_GET. Nothing can arrive unless a user-triggered pull happens.
3. The webhook payload can carry decorated text, so it is "URNs plus optional decorated activity", not "URNs only".
4. 202511 sunsets today. Confirm every call sends 202610 (or any 2026 version).
5. Binary upload is the only media path. No media can be attached by URL.

---

## I. Conflicts found inside the official docs (do not silently pick one)

1. Video size: 75 KB to 500 MB vs "Maximum allowed Videos size is 5GB" (videos-api).
2. Image formats: "JPG, GIF, and PNG" vs "only jpeg and png" (vector-asset-api).
3. Article thumbnail: "must set" (posts-api) vs optional (article-ads-integrations).
4. Refresh tokens: "all approved MDP partners" vs "limited set of partners".
5. Refresh sample: `expires_in` 86400 and `refresh_token_expires_in` 525600 do not match "60 days" and "seconds".
6. One-product-per-app: overview and partner guide say Community Management must be alone; the older migration guide says Ads and Community Management can share an app.
7. Webhook payload: "only URNs" vs sample with `decoratedGeneratedActivity` text.
8. Profile Management: promises monitoring engagement; `r_member_social` is closed.

---

## J. URLs opened and what I read

All fetched 2026-11-16 through the fetch tool, which returns page text. Pages marked "(summary)" came back as a model's extraction, not raw text.

1. https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/images-api : full Images API (schema, initializeUpload, pixel and format limits, image post, permissions).
2. https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/videos-api : full Videos API (4 MB parts, finalize, size and length lines, 30-day URL expiry).
3. https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/documents-api : full Documents API (100 MB, 300 pages, types).
4. https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/multiimage-post-api : full MultiImage page (2 to 20 images).
5. https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/posts-api : content-type table, permissions, article section, no URL scraping, error table.
6. https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/vector-asset-api?view=li-lms-2026-10 : legacy Assets API; upload method and auth rules, image and video upload comparison.
7. https://learn.microsoft.com/en-us/linkedin/marketing/integrations/ads/advertising-targeting/version/article-ads-integrations?view=li-lms-2026-10 : article schema and thumbnail fetch-and-upload note.
8. https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/comments-api?view=li-lms-2026-10 : scopes, create, edit, delete, 429 throttle.
9. https://learn.microsoft.com/en-us/linkedin/marketing/community-management/community-management-overview?view=li-lms-2026-10 : use cases, program tiers, FAQ (rate limits, grayed-out button, `r_member_social` closed).
10. https://learn.microsoft.com/en-us/linkedin/marketing/community-management-app-review?view=li-lms-2026-10 : Development and Standard review, screencast scripts, rejection rules.
11. https://learn.microsoft.com/en-us/linkedin/marketing/increasing-access?view=li-lms-2026-10 : tier table (500/100, BATCH_GET, push disabled), 12 months, full scope list.
12. https://learn.microsoft.com/en-us/linkedin/marketing/community-management/integration-requirements-community-management : Technical Sign Off rule list CM-001 to CM-028.
13. https://learn.microsoft.com/en-us/linkedin/marketing/quick-start?view=li-lms-2026-10 : step 1 and Community Management steps, Standard upgrade items.
14. https://learn.microsoft.com/en-us/linkedin/marketing/community-management/community-management-api-migration-guide?view=li-lms-2026-10 : tier provisioning, scope renames, older FAQ.
15. https://learn.microsoft.com/en-us/linkedin/shared/linkedin-api-partner-support-guide : one-product rule, survey 21 days, 30-business-day figure for Ads.
16. https://learn.microsoft.com/en-us/linkedin/shared/authentication/programmatic-refresh-tokens : lifetimes, request, fields, revocation note.
17. https://learn.microsoft.com/en-us/linkedin/shared/authentication/authorization-code-flow : 60-day access token, "limited set of partners", scope-change invalidation.
18. https://learn.microsoft.com/en-us/linkedin/shared/authentication/developer-portal-tools : refresh-token usage quota table.
19. https://learn.microsoft.com/en-us/linkedin/consumer/integrations/self-serve/share-on-linkedin : Share on LinkedIn product, `w_member_social`, 150 and 100,000 limits.
20. https://learn.microsoft.com/en-us/linkedin/marketing/restricted-use-cases?view=li-lms-2026-10 : full restricted-uses text.
21. https://learn.microsoft.com/en-us/linkedin/marketing/data-storage-requirements?view=li-lms-2026-10 : full storage table.
22. https://learn.microsoft.com/en-us/linkedin/marketing/community-management/organizations/organization-social-action-notifications?view=li-lms-2026-10 : subscription API, payload, retries, pull.
23. https://learn.microsoft.com/en-us/linkedin/shared/api-guide/webhook-validation : registration, challenge, revalidation, signature (page dated 2026-11-04).
24. https://learn.microsoft.com/en-us/linkedin/shared/api-guide/concepts/rate-limits : rate-limit rules and alerts.
25. https://learn.microsoft.com/en-us/linkedin/marketing/versioning?view=li-lms-2026-10 : latest version 202610, header rules.
26. https://learn.microsoft.com/en-us/linkedin/marketing/integrations/migrations?view=li-lms-2026-10 : sunset table.
27. https://learn.microsoft.com/en-us/linkedin/marketing/integrations/recent-changes?view=li-lms-2026-10 : (summary) changes 2026-07 to 2026-10; only the first 100,000 of 143,161 characters.
28. https://www.linkedin.com/legal/l/marketing-api-terms : (summary) sections 1.7, 3.1(e), 3.2(a), 4.1, 4.2, definitions; two fetches. Quotes are the summarizer's excerpts.
29. https://www.linkedin.com/legal/l/api-terms-of-use : (summary) AI search (none), 3.1, 4.1, 4.4.
30. https://www.linkedin.com/help/lms/answer/a424737/video-ads-advertising-specifications?lang=en : (summary) video and thumbnail specs.

Not opened or failed:
- https://learn.microsoft.com/en-us/linkedin/marketing/community-management/community-management-app-review returned 404 (a guessed path; the real page is under `/marketing/` as item 10).
- The existing doc's app-review URL (https://learn.microsoft.com/en-us/linkedin/marketing/community-management-app-review) works when `?view=li-lms-2026-10` is added; I did not try it without.
- A search-snippet only (not opened): Microsoft Q&A "Community Management API Standard Tier review at stage 1 for 40 business days" https://learn.microsoft.com/en-us/answers/questions/5999694/community-management-api-standard-tier-review-at-s . User report, not official; kept out of the CONFIRMED rows.
- No page on image max file size or per-aspect limits for organic image posts was found.
