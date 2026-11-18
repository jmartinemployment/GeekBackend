# YouTube (Data API v3) capabilities for GeekSocial

Researched 2026-11-16 from official Google/YouTube pages only. Most developers.google.com pages were fetched raw and text-extracted (their "Last updated" stamps are 2026-11-12 unless noted), so quotes are verbatim. The support.google.com pages are JS-rendered, so I read them through a fetch summarizer; those rows are tagged `(via summarizer)` and should be re-checked by eye before they go into a customer-facing promise.

Legend: CONFIRMED = stated in an official page I opened, with URL and quote. UNVERIFIED = docs silent/conflicting/unreadable. BLOCKED = unsupported or needs an approval we do not have.

**Headline findings that differ from what most people remember (all verified on 2026 pages):**
1. The quota model changed on 2026-06-01. `videos.insert` and `search.list` now have their OWN buckets of 100 calls/day each, 1 unit per call. All other methods share 10,000 units/day.
2. The "videos from unverified API projects are locked private" rule was REVERSED on 2026-11-02. It no longer applies.
3. YouTube push (PubSubHubbub) covers only video upload / title / description. There is no push for comments. New comments need an on-demand `commentThreads.list` (1 unit).
4. There is no API for Community posts.

---

## 1. Publishing

| Action | Endpoint | Scope | Quota cost | Status | Quote + URL |
|---|---|---|---|---|---|
| Upload video (resumable) | `POST https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=...` (`videos.insert`) | any of `youtube.upload`, `youtube`, `youtubepartner`, `youtube.force-ssl` | 1 unit in the separate "Video Uploads" bucket; default 100 calls/day | CONFIRMED | "Quota impact: 100 calls per day. A call to this method has a quota cost of 1 unit in the Video Uploads quota bucket." https://developers.google.com/youtube/v3/docs/videos/insert |
| Resumable protocol | same, step 1 starts a session, then PUT bytes | same | same | CONFIRMED | "To start a resumable video upload, send a POST request to the following URL." https://developers.google.com/youtube/v3/guides/using_resumable_upload_protocol |
| Set privacy / schedule | `status.privacyStatus`, `status.publishAt` in the video resource | as above | none extra | CONFIRMED | "If you don't specify a value for this property, the uploaded video's privacy status defaults to public." https://developers.google.com/youtube/v3/docs/videos/insert |
| Set custom thumbnail | `POST https://www.googleapis.com/upload/youtube/v3/thumbnails/set?videoId=` (`thumbnails.set`) | any of `youtubepartner`, `youtube.upload`, `youtube`, `youtube.force-ssl` | "approximately 50 units" (quota table: 50) | CONFIRMED | "A call to this method has a quota cost of approximately 50 units." https://developers.google.com/youtube/v3/docs/thumbnails/set |
| Thumbnail for a Short | same method | same | same | CONFIRMED | "Uploads a custom video thumbnail to YouTube and sets it for a video, including YouTube Shorts." (call only after `processingStatus` is `succeeded`) https://developers.google.com/youtube/v3/docs/thumbnails/set |
| Create playlist | `POST /youtube/v3/playlists` (`playlists.insert`) | `youtubepartner`, `youtube`, `youtube.force-ssl` | 50 | CONFIRMED | "Creates a playlist. Quota impact: A call to this method has a quota cost of 50 units." https://developers.google.com/youtube/v3/docs/playlists/insert |
| Publish a Short | No separate endpoint. A Short is a `videos.insert` upload; the API docs never describe a Shorts flag. YouTube Help: Shorts are "up to 3 minutes long" `(via summarizer)` | as videos.insert | as videos.insert | CONFIRMED that no Shorts-specific method exists (absence in the resource list); duration rule via support page | Resource list has no Shorts resource: https://developers.google.com/youtube/v3/docs ; Help: https://support.google.com/youtube/answer/10059070 |
| Community post (text / image / link / poll) | none | n/a | n/a | BLOCKED | Absence verified: the official API Reference lists 17 resource types (Activities, Captions, ChannelBanners, ChannelSections, Channels, CommentThreads, Comments, Members, MembershipsLevels, PlaylistItems, Playlists, Subscriptions, Thumbnails, VideoAbuseReportReasons, VideoCategories, Videos, Watermarks), none for posts. The text "communit" occurs 0 times on that page. https://developers.google.com/youtube/v3/docs |
| Reply to a Community post's comments | none | n/a | n/a | BLOCKED | `commentThread` doc: "A commentThread resource represents comments on a video." https://developers.google.com/youtube/v3/docs/commentThreads |
| Set video made-for-kids / AI-disclosure flags | `status.selfDeclaredMadeForKids`, `status.containsSyntheticMedia` | as videos.insert | none extra | CONFIRMED (fields exist in the settable list) | Listed under "You can set values for these properties" https://developers.google.com/youtube/v3/docs/videos/insert |

Made-for-kids duty (ToS 9.1): if the API client lets users upload, it must either let them designate Made for Kids before upload or notify them to declare it on desktop. Quote: "enable users of your non-Child-Directed API Client to designate their content as Made for Kids via your API Client before they can upload their content" https://developers.google.com/youtube/terms/api-services-terms-of-service

---

## 2. Reading, replying, moderating comments

| Action | Endpoint | Scope | Quota cost | Status | Quote + URL |
|---|---|---|---|---|---|
| List threads for a video | `GET /youtube/v3/commentThreads?part=snippet,replies&videoId=` | The reference page lists NO scope for this method. Public comments work with an API key or any OAuth token (see note) | 1 | CONFIRMED (cost, params); scope UNVERIFIED | "A call to this method has a quota cost of 1 unit." https://developers.google.com/youtube/v3/docs/commentThreads/list |
| List ALL threads for a channel (every video, incl. channel comments) | `commentThreads.list?allThreadsRelatedToChannelId=` | same | 1 per call, up to 100 threads/page | CONFIRMED | "The allThreadsRelatedToChannelId parameter instructs the API to return all comment threads associated with the specified channel." https://developers.google.com/youtube/v3/docs/commentThreads/list |
| Page size and order | `maxResults` 1-100 (default 20); `order=time` (default) or `relevance`; `pageToken`; `searchTerms`; `textFormat` html/plainText | n/a | each extra page costs 1 | CONFIRMED | "Acceptable values are 1 to 100, inclusive. The default value is 20." https://developers.google.com/youtube/v3/docs/commentThreads/list |
| List held-for-review / likely-spam comments | `moderationStatus=heldForReview\|likelySpam\|published` | OAuth by the channel owner required | 1 | CONFIRMED (needs authorized request); exact scope UNVERIFIED on this page | "This parameter can only be used in a properly authorized request." https://developers.google.com/youtube/v3/docs/commentThreads/list |
| List replies of one top-level comment | `GET /youtube/v3/comments?part=snippet&parentId=` (`comments.list`) | page lists none | 1 | CONFIRMED | "A call to this method has a quota cost of 1 unit." https://developers.google.com/youtube/v3/docs/comments/list |
| Reply to a comment | `POST /youtube/v3/comments?part=snippet` (`comments.insert`), body `snippet.parentId` + `snippet.textOriginal` | `https://www.googleapis.com/auth/youtube.force-ssl` | 50 | CONFIRMED | "A call to this method has a quota cost of 50 units." and "To create a top-level comment, use the commentThreads.insert method." https://developers.google.com/youtube/v3/docs/comments/insert |
| Reply limits | | | | CONFIRMED | "The API does not support replies to private comments." and `snippet.canReply` "indicates whether the current viewer can reply to the thread." https://developers.google.com/youtube/v3/docs/comments/insert and https://developers.google.com/youtube/v3/docs/commentThreads |
| Replies to replies | not supported | n/a | n/a | BLOCKED (replies only attach to a top-level comment) | "YouTube currently supports replies only for top-level comments. However, replies to replies may be supported in the future." https://developers.google.com/youtube/v3/docs/comments/list |
| New top-level comment | `commentThreads.insert` | `youtube.force-ssl` | 50 | CONFIRMED | "Creates a new top-level comment." https://developers.google.com/youtube/v3/docs/commentThreads |
| Hold / publish / reject; ban author | `POST /youtube/v3/comments/setModerationStatus?id=...&moderationStatus=heldForReview\|published\|rejected[&banAuthor=true]` | `youtube.force-ssl` | 50 (up to 50 IDs per call) | CONFIRMED | "The API request must be authorized by the owner of the channel or video associated with the comments." https://developers.google.com/youtube/v3/docs/comments/setModerationStatus |
| Moderation limits | | | | CONFIRMED | "You can't change the moderation status of a rejected comment to published". `banAuthor` "is only valid if the moderationStatus parameter is also set to rejected." https://developers.google.com/youtube/v3/docs/comments/setModerationStatus |
| Delete a comment | `comments.delete` | `youtube.force-ssl` | 50 | CONFIRMED (cost); only the author can delete | Revision-history and quota table list 50; the moderation page states `comments.delete` is only for the comment's original author, so reject is the owner's remove. https://developers.google.com/youtube/v3/determine_quota_cost |
| Edit a comment | `comments.update` | `youtube.force-ssl` | 50 | CONFIRMED | quota table: "comments ... update 50" https://developers.google.com/youtube/v3/determine_quota_cost |
| Mark as spam | `comments.markAsSpam` | n/a | n/a | BLOCKED (removed) | "The comments.markAsSpam method has been deprecated for several years. This method is already unsupported on YouTube and is no longer supported through the API." https://developers.google.com/youtube/v3/revision_history (2023-09-12) |
| Like / heart a comment | none. The `comment` resource has read-only `likeCount`, `canRate`, `viewerRating`; the methods list is list / insert / update / delete / setModerationStatus. `videos.rate` rates VIDEOS only | n/a | n/a | BLOCKED (absence verified in method list) | "The API supports the following methods for comments resources: list ... insert ... update ... delete ... setModerationStatus" https://developers.google.com/youtube/v3/docs/comments |

Note on scopes for read: the reference pages for `commentThreads.list` and `comments.list` list no scope at all, and the revision history says `youtube.force-ssl` "is required for calls to the comments.insert, comments.update, comments.markAsSpam, comments.setModerationStatus, comments.delete, commentThreads.insert, and commentThreads.update methods" (https://developers.google.com/youtube/v3/revision_history). That sentence leaves out `list`. Reading private/held comments with an OAuth token, and which scope covers it, is UNVERIFIED by docs: settle with a live call (see section 9).

### Fields available (comment resource)
CONFIRMED at https://developers.google.com/youtube/v3/docs/comments :
`id`, `snippet.authorDisplayName`, `snippet.authorProfileImageUrl`, `snippet.authorChannelUrl`, `snippet.authorChannelId.value`, `snippet.channelId`, `snippet.textDisplay`, `snippet.textOriginal`, `snippet.parentId`, `snippet.canRate`, `snippet.viewerRating`, `snippet.likeCount`, `snippet.moderationStatus`, `snippet.publishedAt`, `snippet.updatedAt`, `snippet.imageUrl`.

Gotchas, verbatim:
- `textOriginal`: "The original text is only returned to the authenticated user if they are the comment's author." So for OTHER people's comments use `textDisplay` (request `textFormat=plainText`).
- `textDisplay`: "Even the plain text may differ from the original comment text. For example, it may replace video links with video titles."
- `moderationStatus`: "only returned if the API request was authorized by the owner of the channel or the video".
- `imageUrl`: "The URL is served as a signed link with a six-hour time to live (TTL)".
- Thread (https://developers.google.com/youtube/v3/docs/commentThreads): `snippet.videoId`, `snippet.channelId`, `snippet.canReply`, `snippet.totalReplyCount`, `snippet.isPublic`, `replies.comments[]`. "The list contains a limited number of replies" so fetch the rest with `comments.list&parentId=`.
- `comments.list` `id` filter, error `operationNotSupported`: "The id filter is only compatible with comments based on Google+." Do not rely on fetching by `id`; use `parentId` or `commentThreads.list`. (https://developers.google.com/youtube/v3/docs/comments/list)

---

## 3. New-item delivery

| Action | Endpoint | Scope | Quota cost | Status | Quote + URL |
|---|---|---|---|---|---|
| Push for new VIDEOS / title / description edits | WebSub (PubSubHubbub) hub `https://pubsubhubbub.appspot.com`, topic `https://www.youtube.com/xml/feeds/videos.xml?channel_id=CHANNEL_ID`, Atom payload to our callback | none (public feed topic) | none stated | CONFIRMED | "Your PubSubHubbub callback server receives Atom feed notifications when a channel does any of the following activities: uploads a video updates a video's title updates a video's description" https://developers.google.com/youtube/v3/guides/push_notifications |
| Push for new COMMENTS | none | n/a | n/a | BLOCKED | The push guide lists exactly the three triggers above and no comment trigger. A full search of the revision history for "push", "webhook", "WebSub" finds only the 2015 PubSubHubbub entries; no later push feature. https://developers.google.com/youtube/v3/guides/push_notifications , https://developers.google.com/youtube/v3/revision_history |
| Any other push (Pub/Sub, webhooks, streaming) for comments | none found in the Data API docs | n/a | n/a | BLOCKED (absence in: guide, resource list, revision history) | https://developers.google.com/youtube/v3/docs |
| ON-DEMAND sync (customer opens the screen) | ONE `GET commentThreads?part=snippet,replies&allThreadsRelatedToChannelId=CHANNEL&order=time&maxResults=100` | OAuth by the channel owner (to include `moderationStatus`/held); public data needs only a key | 1 unit per call (100 threads); +1 per extra page; page through only until you hit an already-seen `publishedAt`/`id` | CONFIRMED (cost); not-polling compliance is a design question for us, not a docs one | "A call to this method has a quota cost of 1 unit." https://developers.google.com/youtube/v3/docs/commentThreads/list |

Answer: new comments CANNOT be received without polling. Push exists only for video events, so each new comment needs a user-triggered `commentThreads.list` pull. One on-demand sync costs 1 unit (plus 1 per extra 100-thread page). Replies beyond the nested subset cost +1 per `comments.list&parentId` call. With a 10,000-unit shared daily bucket, reads are cheap; writes (reply 50 units, moderation 50) are what consume quota: about 200 replies/day across ALL customers at default quota.

---

## 4. Media rules

| Action | Endpoint | Scope | Quota cost | Status | Quote + URL |
|---|---|---|---|---|---|
| Video max file size (API) | `videos.insert` | n/a | n/a | CONFIRMED | "Maximum file size: 256GB" and "Accepted Media MIME types: video/*, application/octet-stream" https://developers.google.com/youtube/v3/docs/videos/insert |
| Video max size/duration (Help) | n/a | n/a | n/a | CONFIRMED (via summarizer) | "The maximum file size you can upload is 256 GB or 12 hours, whichever is less." https://support.google.com/youtube/answer/71673 |
| Default max duration, unverified channel | n/a | n/a | n/a | CONFIRMED (via summarizer) | "By default, you can upload videos that are up to 15 minutes long." and "Verified accounts can upload videos longer than 15 minutes." https://support.google.com/youtube/answer/71673 |
| Accepted containers | n/a | n/a | n/a | CONFIRMED (via summarizer) | List: .MOV, .MPEG-1, .MPEG-2, .MPEG4, .MP4, .MPG, .AVI, .WMV, .MPEGPS, .FLV, 3GPP, WebM, DNxHR, ProRes, CineForm, HEVC (H.265). "Audio files, like MP3, WAV, or PCM files, can't be uploaded to create a YouTube video." https://support.google.com/youtube/troubleshooter/2888402 |
| Recommended container | n/a | n/a | n/a | CONFIRMED (via summarizer) | "Container: MP4" with "moov atom at the front of the file (Fast Start)" https://support.google.com/youtube/answer/1722171 |
| Shorts length | n/a | n/a | n/a | CONFIRMED (via summarizer) | Shorts can be "up to 3 minutes long." Help gives no explicit aspect-ratio rule beyond "vertical videos". https://support.google.com/youtube/answer/10059070 |
| Thumbnail formats / size (API) | `thumbnails.set` | as above | 50 | CONFIRMED | "Maximum file size: 50MB Accepted Media MIME types: image/jpeg, image/png, application/octet-stream" https://developers.google.com/youtube/v3/docs/thumbnails/set |
| Thumbnail dimensions (Help, recommendations) | n/a | n/a | n/a | CONFIRMED (via summarizer) | "Have a resolution of 3840 x 2160 pixels for videos" with "a minimum width of 640 pixels for videos"; aspect "16:9". Shorts: 2160 x 3840, min height 640, 9:16. https://support.google.com/youtube/answer/72431 |
| Thumbnail eligibility | n/a | n/a | n/a | CONFIRMED (via summarizer) | "You can choose from auto generated suggested options, or upload your own if your account is verified." (API error `forbidden`: "The authenticated user doesn't have permissions to upload and set custom video thumbnails.") https://support.google.com/youtube/answer/72431 , https://developers.google.com/youtube/v3/docs/thumbnails/set |
| Thumbnail rate limit | `thumbnails.set` | | | CONFIRMED | error `uploadRateLimitExceeded`: "The channel has uploaded too many thumbnails recently." https://developers.google.com/youtube/v3/docs/thumbnails/set |
| URL-based video upload (pull from a URL) | none | n/a | n/a | BLOCKED (absence verified) | `videos.insert` documents only a media-body upload ("This method supports media upload") and the resumable guide takes bytes via `X-Upload-Content-Length`; no URL/remote-source parameter appears in the parameter list. https://developers.google.com/youtube/v3/docs/videos/insert |

Our server must hold or stream the video bytes itself (resumable upload, chunked in multiples of 256 KB per the sample guide). `chunksize=-1` uploads in one request. Source: https://developers.google.com/youtube/v3/guides/uploading_a_video

---

## 5. Quota

| Action | Endpoint | Scope | Quota cost | Status | Quote + URL |
|---|---|---|---|---|---|
| Default daily quota | per Cloud project | n/a | 10,000 units/day for all endpoints except two buckets: 100 `search.list` calls and 100 `videos.insert` calls per day | CONFIRMED | "Projects that enable the YouTube Data API have a default quota allocation of 100 search.list calls, 100 videos.insert calls, and 10,000 units per day combined for all other endpoints." https://developers.google.com/youtube/v3/determine_quota_cost |
| Reset | n/a | n/a | n/a | CONFIRMED | "Daily quotas reset at midnight Pacific Time (PT)." same URL |
| Cost per method | | | | CONFIRMED | Table: `videos.insert` "100 quota per day. Each call costs 1 quota."; `thumbnails.set` 50; `playlists.insert` 50; `commentThreads.list` 1; `commentThreads.insert` 50; `comments.list` 1; `comments.insert` 50; `comments.update` 50; `comments.setModerationStatus` 50; `comments.delete` 50; `videos.list` 1; `videos.update` 50; `videos.delete` 50; `search.list` bucket 100/day, 1 per call. "All API requests, including invalid requests, incur a quota cost of at least one point." https://developers.google.com/youtube/v3/determine_quota_cost |
| Upload cost history | | | | CONFIRMED | "reflect a change in the quota cost of a video upload from approximately 1600 units to approximately 100 units" (2025-12-04), then split into its own bucket (2026-06-01). https://developers.google.com/youtube/v3/revision_history |
| Granular-bucket change | | | | CONFIRMED | "API calls to the videos.insert and search.list methods will be charged to their own respective quota buckets." (2026-06-01) same URL |
| Extension | Audit and Quota Extension Form | n/a | n/a | CONFIRMED | "If you would like to request additional quota beyond the default allocation, you must first complete an audit to show that your project is in compliance with the YouTube API Services Terms of Service." https://developers.google.com/youtube/v3/guides/quota_and_compliance_audits |
| Audit steps | | | | CONFIRMED | "fill out and submit the YouTube API Services - Audit and Quota Extension Form. A member of YouTube's API Services team will contact you as soon as possible." Also: re-audit within 12 months for another extension; Appeals Form for failed audits; Periodic Audit Form; Change of Control Form. same URL |
| Audit before more than default | | | | CONFIRMED | "All developers using YouTube's API Services must complete an API Compliance Audit in order to be granted more than the default quota allocation of 10,000 units." (2021-07-01 entry) https://developers.google.com/youtube/v3/revision_history |
| SaaS / many customers: quota is shared | | | | CONFIRMED (per project); consequence is my inference, flagged | Quota is allocated per "Projects that enable the YouTube Data API". Docs do not say customers get separate quota, so all customers share one project bucket. |
| SaaS: no sharding | | | | CONFIRMED | "You can't create multiple apps/sites or create multiple Google Cloud projects for use across multiple apps/sites to artificially acquire more API quota (aka "sharding") for a single API service or use case." https://developers.google.com/youtube/terms/developer-policies-guide ; ToS 15: "You and your API Client(s) will not, and will not attempt to, exceed or circumvent use or quota restrictions." https://developers.google.com/youtube/terms/api-services-terms-of-service |
| Allowed separation | | | | CONFIRMED | "An application's developer team is allowed to have separate API keys for test, dev, and prod environments." https://developers.google.com/youtube/terms/developer-policies-guide |
| Quota-increase review time / approval odds | | | | UNVERIFIED | No SLA or criteria stated on the pages read. |

Planning consequence: at default quota the whole product can publish at most 100 videos/day across every customer, and about 200 comment replies/day (10,000 / 50), minus thumbnails and moderation. An audit and quota extension is a launch dependency, not an option.

---

## 6. OAuth

| Action | Endpoint | Scope | Quota cost | Status | Quote + URL |
|---|---|---|---|---|---|
| Scope list | auth endpoint | `youtube` "Manage your YouTube account."; `youtube.force-ssl` "See, edit, and permanently delete your YouTube videos, ratings, comments and captions"; `youtube.upload` "Manage your YouTube videos"; `youtube.readonly` "View your YouTube account"; `youtubepartner`; `youtube.channel-memberships.creator`; `youtubepartner-channel-audit` | n/a | CONFIRMED | https://developers.google.com/identity/protocols/oauth2/scopes and https://developers.google.com/youtube/v3/guides/authentication |
| Minimum set for our features | read comments + reply + moderate: `youtube.force-ssl`; upload video + thumbnail: `youtube.upload` (also accepted by `thumbnails.set`) or `youtube.force-ssl` alone | | | CONFIRMED by the per-method scope lists above | `videos.insert` and `thumbnails.set` accept `youtube.force-ssl`, so `youtube.force-ssl` alone covers every method in this document except the unlisted read scope question. |
| Narrowest-scope rule | | | | CONFIRMED | "Request the most narrow scope that supports your application's functionality." https://developers.google.com/youtube/v3/guides/authentication |
| Sensitive-scope verification required | Google OAuth app verification | | | CONFIRMED (general rule; YouTube-scope classification partly) | "If your app requests scopes categorized as sensitive or restricted, you probably need to complete the verification process unless your app's use qualifies for an exception." and "Examples of sensitive scopes include ... deleting a YouTube video." https://developers.google.com/identity/protocols/oauth2/production-readiness/sensitive-scope-verification |
| Verification duration | | | | CONFIRMED | "The sensitive scope verification process can take up to 10 days to complete." same URL. It also requires "a video demonstration for sensitive scopes". |
| Is `youtube.force-ssl` / `youtube.upload` "sensitive" vs "restricted"; security assessment needed? | | | | UNVERIFIED | The scopes reference page says sensitive scopes carry "a sensitive indicator on the Google Cloud Console's OAuth consent screen configuration page" and its text does not mark each YouTube scope. The restricted-scope page (which carries the "possible security assessment") mentions YouTube only as the host for the demo video. No page I opened classifies each YouTube scope. Read the badge in our Cloud Console before assuming. https://developers.google.com/identity/protocols/oauth2/scopes , https://developers.google.com/identity/protocols/oauth2/production-readiness/restricted-scope-verification |
| Unverified-app limits | | | | CONFIRMED (existence); numeric cap UNVERIFIED | "A user cap restricts the number of Google Accounts able to grant access to your unverified app." https://developers.google.com/identity/protocols/oauth2/production-readiness/sensitive-scope-verification |
| Access token lifetime | token endpoint returns `expires_in` | | | UNVERIFIED as a fixed number | Docs say "Access tokens have a limited lifetime and expire periodically" (https://developers.google.com/youtube/v3/guides/authentication); the response field `expires_in` is "The remaining lifetime of the access token in seconds." The example shows 3920. Always read `expires_in`. https://developers.google.com/youtube/v3/guides/auth/server-side-web-apps |
| Refresh token issued | `access_type=offline` | | | CONFIRMED | "Server-side web applications obtain refresh tokens by requesting offline access (access_type=offline) during the authorization request." https://developers.google.com/youtube/v3/guides/authentication |
| Refresh token expiry causes | | | | CONFIRMED | "The refresh token has not been used for six months." "The user has revoked your app's access." Plus password change only if Gmail scopes, admin policy, and exceeded token limits. https://developers.google.com/identity/protocols/oauth2 |
| 7-day expiry in Testing | | | | CONFIRMED | "A Google Cloud Platform project with an OAuth consent screen configured for an external user type and a publishing status of "Testing" is issued a refresh token expiring in 7 days, unless the only OAuth scopes requested are a subset of name, email address, and user profile" https://developers.google.com/identity/protocols/oauth2 |
| Refresh-token cap | | | | CONFIRMED | "There is currently a limit of 100 refresh tokens per Google Account per OAuth 2.0 client ID. If the limit is reached, creating a new refresh token automatically invalidates the oldest refresh token without warning." same URL |
| Revoke | `POST https://oauth2.googleapis.com/revoke?token=` | | | CONFIRMED | https://developers.google.com/youtube/v3/guides/authentication |
| Channel selection | OAuth ties to a Google account; brand-account channel choice and "the YouTube account ... must be merged with the user's Google account" for comments | | | CONFIRMED (merge requirement); brand-account flow UNVERIFIED | "The YouTube account used to authorize the API request must be merged with the user's Google account to insert a comment or comment thread." https://developers.google.com/youtube/v3/docs/comments/insert |

---

## 7. YouTube API Services Terms and Developer Policies (verbatim, last updated 2026-11-06)

Pages: https://developers.google.com/youtube/terms/api-services-terms-of-service and https://developers.google.com/youtube/terms/developer-policies

| Topic | Status | Quote + URL |
|---|---|---|
| Automated comments / actions need prior express consent | CONFIRMED | "you must not automate or trigger views, uploads, comments, likes, dislikes, or other actions without the user's prior specific and express consent" (Developer Policies, abusive behaviors). **This governs auto-send categories**: a standing, per-category consent captured before sending is the minimum; whether a category-level toggle counts as "prior specific and express" is UNVERIFIED (settle in the audit). |
| Express consent before write actions | CONFIRMED | "API Clients must clearly identify any actions that they take to insert, share, update, or delete data or content on the authorizing user's behalf. In addition, the user must expressly consent to those actions prior to their actual execution." |
| Channel identification | CONFIRMED | "API Clients must clearly identify the YouTube channel or content owner that is associated with any request that requires user authorization." |
| Privacy-status changes | CONFIRMED | "API Clients must not modify existing content visibility settings unless the authorizing user has expressly instructed the API Clients to do so." |
| Spam / deception | CONFIRMED | "You and your API Clients must not ... confuse, deceive, defraud, mislead, misrepresent, defame, abuse, stalk, threaten, spam, surprise, or harass anyone" |
| Showing Authorized Data to others | CONFIRMED | "API Clients must not display or allow access to Authorized Data to anyone other than the authorizing user or agents expressly approved by that user." (relevant: staff/agent seats must be approved by the account owner) |
| Storage of Authorized Data (30 days) | CONFIRMED | "API Clients may store all other types of Authorized Data not identified in section (III.E.4.b) for as long as is necessary for the purposes of the specific consent granted by an active user and for no longer than 30 calendar days. After 30 calendar days, the API Client must either delete or refresh the stored data." |
| Non-authorized data | CONFIRMED | "API Clients may temporarily store limited amounts of Non-Authorized Data for as long as is necessary for the purposes of the API Client but not longer than 30 calendar days." |
| Statistics exception | CONFIRMED | Stats (e.g. view counts) may be stored longer but "the Client must still ensure every 30 days that it is still authorized by the user to access that data." |
| Tokens | CONFIRMED | "API Clients may store authorization tokens for as long as is necessary provided that those tokens are used only for purposes consistent with the specific consent granted by an active user" |
| Freshness | CONFIRMED | "API Clients must use reasonable efforts to ensure that their stored API Data is consistent with the current data available through YouTube API Services." |
| User-requested deletion (7 days) | CONFIRMED | "If the user indicates that you should delete that data, you must then delete it as soon as possible and within 7 calendar days." Also: "if a user deletes his account from your API Client, you must also delete any user data you have stored related to that user as soon as possible and within 7 calendar days." |
| Revocation (7 days) | CONFIRMED | "following revocation of consent through this mechanism, you and your API Clients must delete all Authorized Data that was accessed or stored pursuant to that consent. That deletion should happen as soon as possible and must take place within 7 calendar days of the revocation." Must also "programmatically revoke that token right away". |
| Revocation via Google settings (30 days) | CONFIRMED | "all such deletions should happen as soon as possible and must take place within 30 calendar days of that revocation." Needs periodic token revalidation: "delete API Data associated with users whose authorization tokens cannot be refreshed." |
| Delete mechanism wording | CONFIRMED | Must "make clear that deleting the data stored by the API Client does not, in any way, affect data stored by YouTube" |
| Privacy policy | CONFIRMED | ToS 7: "Each API Client will provide and adhere to a published privacy policy that clearly and accurately describes to its users what user information you and your API Client access, collect and store, and how and why you and your API Client use, process, and share such information" Policy also requires a link to https://security.google.com/settings/security/permissions |
| Derived data | CONFIRMED | "Your API Clients must not (i) replace API Data with similar, independently calculated data, or (ii) access or use API Data to create new or derived data or metrics." A clear reading of whether AI-classified comment categories count as "derived data" is UNVERIFIED (ask in the audit). |
| Sending API data to third parties | PARTIALLY CONFIRMED | Policy: "API Clients must not display or allow access to Authorized Data to anyone other than the authorizing user or agents expressly approved by that user." and "API Clients must not use user data for secondary purposes that are not clearly disclosed to users." Privacy-policy disclosure of sharing is required (ToS 7). No dedicated rule names an LLM vendor. |
| AI / machine learning | UNVERIFIED (docs silent) | The strings "machine learning", "artificial intelligence", "generative", "train" (outside the nav item "YouTube Video Third-Party Trainability API") do not appear in the Developer Policies, ToS, or Policies Guide text I extracted. Silence is not permission: general rules above (consent, disclosed purposes, derived data, aggregation) still apply. Ask in the audit. |
| Aggregation | CONFIRMED | "Do not aggregate API Data except that you may only aggregate API Data relating to YouTube channels that are under the same content owner as recognized by YouTube" (multi-tenant cross-customer analytics is out). |
| Reselling API access / SaaS | CONFIRMED, needs audit reading | Prohibited: "offer an API service that allows users to access any data or functionality directly or indirectly provided by the YouTube API Services" and "sell YouTube API Services or access to any components of YouTube API Services unless you obtain YouTube's prior written approval". An app that adds value (replies, moderation) is the normal case but the line is for the audit to draw. |
| Scraping | CONFIRMED | "You and your API Clients must not ... scrape YouTube Applications or Google Applications, or obtain scraped YouTube data or content." |
| YouTube may audit | CONFIRMED | ToS 6: "YouTube may monitor, review and inspect your API Client(s), and monitor and audit your access to and use of the YouTube API Services, at any time and without further notice to you" |
| Required Minimum Functionality, Branding Guidelines | NOT OPENED | UNVERIFIED. |

---

## 8. "Unverified API project => uploads locked private"

| Item | Status | Quote + URL |
|---|---|---|
| Current rule | CONFIRMED: NO LONGER APPLIES (reversed 2026-11-02) | "Videos uploaded through the videos.insert method from unverified API projects are no longer restricted to private viewing mode. This change reverses the policy announced on July 28, 2020" https://developers.google.com/youtube/v3/revision_history |
| Same statement on the method page | CONFIRMED | "Videos uploaded from unverified API projects are not restricted to private viewing mode." https://developers.google.com/youtube/v3/docs/videos/insert |
| Old rule (for history) | CONFIRMED | "All videos uploaded using the videos.insert endpoint from unverified API projects created after 28 July 2020 will be restricted to private viewing mode." (2020-07-28 entry; same history page) |
| Defaults still apply | CONFIRMED | "If you don't specify a value for the status.privacyStatus property in a videos.insert request, the uploaded video's privacy status defaults to public. Certain uploads might default to private based on creator account settings" |
| Still need an audit for quota | CONFIRMED | see section 5 |

The reversal is 14 days old. If any internal doc says uploads are locked private for unaudited projects, it is out of date. A live test is still prudent (section 9).

---

## 9. Open items settled only by a live call or an audit

1. Which scope a read of held-for-review / likely-spam comments needs (`commentThreads.list&moderationStatus=heldForReview` with `youtube.force-ssl` vs `youtube`); and whether `youtube.readonly` suffices for a pure read. Test with a throwaway channel.
2. Whether `commentThreads.list` with `allThreadsRelatedToChannelId` returns every comment (all videos, Shorts, channel-page comments) and its real page-depth/latency. Docs give 100/page only.
3. Whether comments on Community posts are reachable at all through the API (docs say threads are video comments).
4. Whether `youtube.force-ssl` / `youtube.upload` are tagged sensitive or restricted in our Cloud Console, and whether a third-party security assessment is demanded (restricted scopes only).
5. Exact numeric cap of unverified-app users and test users while in Testing.
6. Approval criteria and time for a quota extension (audit), and whether a SaaS serving many channels is judged as "selling access" under the Developer Policies.
7. (The auto-send half of this question is moot since 2026-11-18: GeekSocial has no auto-send, and a person approves every reply.) Whether a category-level "auto-send" setting counts as "prior specific and express consent" for comments, and whether AI drafting or AI categorization of comments is acceptable under the derived-data rule and the third-party-disclosure rules. YouTube docs do not mention AI.
8. Whether the post-2026-11-02 removal of the private lock is enforced for a brand-new project (do one real upload test with `privacyStatus=public`).
9. Access-token lifetime (read `expires_in`; docs give no fixed value).
10. Support-page figures (15-minute default, 256 GB or 12 hours, format list, 50 MB thumbnail minimums for Help) read through a summarizer: re-read by eye.
11. Whether brand-account / multi-channel owners are handled by a channel chooser in the consent flow, and how `mine=true` resolves.
12. How many comment replies per minute YouTube tolerates before an anti-abuse block (no rate figure in docs); the only stated limit is quota. Error `uploadRateLimitExceeded` exists for thumbnails.

---

## URLs opened and what I read

Raw fetch (full text extracted):
- https://developers.google.com/youtube/v3/determine_quota_cost : default quota, per-method costs, reset time, buckets.
- https://developers.google.com/youtube/v3/guides/quota_and_compliance_audits : default allocation, audit and extension forms.
- https://developers.google.com/youtube/v3/docs/videos/insert : 256GB, MIME types, scopes, unverified-project sentence, settable properties.
- https://developers.google.com/youtube/v3/guides/uploading_a_video : resumable Python sample, `youtube.upload` scope, chunking.
- https://developers.google.com/youtube/v3/guides/using_resumable_upload_protocol : session start, headers, `video/*` MIME.
- https://developers.google.com/youtube/v3/docs/thumbnails/set : 50MB, jpeg/png, scopes, Shorts note, errors.
- https://developers.google.com/youtube/v3/guides/push_notifications : PubSubHubbub triggers (upload, title, description).
- https://developers.google.com/youtube/v3/docs/comments : resource fields and methods list.
- https://developers.google.com/youtube/v3/docs/commentThreads : resource fields, canReply, replies subset.
- https://developers.google.com/youtube/v3/docs/commentThreads/list : cost 1, parameters, moderationStatus.
- https://developers.google.com/youtube/v3/docs/commentThreads/insert : cost 50, force-ssl, restrictions.
- https://developers.google.com/youtube/v3/docs/comments/list : cost 1, parameters.
- https://developers.google.com/youtube/v3/docs/comments/insert : cost 50, force-ssl, restrictions.
- https://developers.google.com/youtube/v3/docs/comments/setModerationStatus : cost 50, force-ssl, values, banAuthor, restrictions (read via summarizer then confirmed in revision history for scope).
- https://developers.google.com/youtube/v3/docs/comments/delete : fetched, cost 50 from the quota table.
- https://developers.google.com/youtube/v3/docs/playlists/insert : cost 50, scopes.
- https://developers.google.com/youtube/v3/docs : full resource list (no posts resource).
- https://developers.google.com/youtube/v3/revision_history : 2026-11-02 private-lock reversal, 2026-11-03 Shorts thumbnails, 2026-06-01 granular quota, 2025-12-04 upload cost, 2023-09-12 markAsSpam, 2021-07-01 audit, 2015 push.
- https://developers.google.com/youtube/v3/guides/authentication : scopes, refresh tokens, revoke.
- https://developers.google.com/youtube/v3/guides/auth/server-side-web-apps : `expires_in`, `access_type=offline`.
- https://developers.google.com/identity/protocols/oauth2 : refresh token expiry causes, 7-day Testing rule, 100-token limit.
- https://developers.google.com/identity/protocols/oauth2/scopes : YouTube scope list; sensitive-scope indicator note.
- https://developers.google.com/identity/protocols/oauth2/production-readiness/sensitive-scope-verification : verification, 10 days, user cap, YouTube-delete example.
- https://developers.google.com/identity/protocols/oauth2/production-readiness/restricted-scope-verification : restricted scopes, possible security assessment; YouTube appears only as demo-video host.
- https://developers.google.com/youtube/terms/api-services-terms-of-service : ToS sections 3-15 (quota circumvention 15, privacy 7, audit 6, made-for-kids 9.1).
- https://developers.google.com/youtube/terms/developer-policies : consent, spam, storage 30 days, deletion 7 days, aggregation, derived data, resale.
- https://developers.google.com/youtube/terms/developer-policies-guide : no sharding, separate dev/test/prod keys.

Read through the fetch summarizer (JS-rendered; tagged above):
- https://support.google.com/youtube/answer/71673 : 15-minute default, 256 GB or 12 hours.
- https://support.google.com/youtube/answer/1722171 : recommended MP4 container.
- https://support.google.com/youtube/troubleshooter/2888402 : supported file formats list.
- https://support.google.com/youtube/answer/72431 : thumbnail specs, verified-account requirement.
- https://support.google.com/youtube/answer/10059070 : Shorts up to 3 minutes.
- https://support.google.com/youtube/answer/6013276 and https://support.google.com/youtube/answer/4603579 : opened; the first gave a different Shorts-length statement (1-3 minutes), the second is the Content ID/partner formatting spec and was NOT used for format claims. The Shorts length is therefore reported from 10059070 only. If the two disagree, treat the Shorts duration as UNVERIFIED until re-read by eye.

Web search (one query) only to look for a Community-posts endpoint; results were third-party vendors, not used as evidence. Absence was established from the official resource list.
