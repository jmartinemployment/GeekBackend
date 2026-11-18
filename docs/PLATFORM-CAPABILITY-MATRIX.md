# GeekSocial verified capability matrix

Roadmap Phase 0, item 10 (`geek-social/plans/geek-social-roadmap-2026-11-18.md:207-214`). For each network, operation by
operation: content types and media limits; scheduling, publish, edit and delete; comments, messages and reviews;
analytics; rate limits. Phase 2's shared publishing design is built on it.

**How it was checked.** Each operation was read in the platform's own API documentation on 2026-11-18. The
research of 2026-11-16 (`docs/platform-research-2026-11-16/`) was used where it still matched. **Nothing was tested
on a live account**: GeekSocial has no connected test account on any network yet. A live call settles each
UNVERIFIED row and each open item in `PLATFORM-CAPABILITIES.md` section 6.

**Status words.**
- **CONFIRMED:** read in the official documentation.
- **UNVERIFIED:** the documentation is silent, ambiguous or self-contradicting; the row says which.
- **NOT OFFERED:** the API does not provide it.

**Citation keys.**
- Research: `meta`, `linkedin`, `gbp`, `x`, `youtube`, `tiktok` and `pinterest` are the files in
  `docs/platform-research-2026-11-16/`; `PC` is `docs/PLATFORM-CAPABILITIES.md`.
- Official pages read 2026-11-18 are keyed per section, and each section lists its keys.

**Relation to the other documents.**
- `PLATFORM-CAPABILITIES.md` stays the record of why each network was chosen, its tokens, webhooks and retention.
  This file is the operation-by-operation record.
- `SocialPlatformCapabilities` (`GeekApplication/Models/Social/SocialPlatforms.cs`) is the code's copy of the rows
  GeekSocial uses. The section "The code's matrix against this one" lists where they differ.

## Facebook Page (Graph API v27.0)

Keys: under `https://developers.facebook.com/documentation/`: PG-POSTS `pages-api/posts.md`, PG-CM
`pages-api/comments-mentions.md`, PG-WH `pages-api/webhooks-for-pages.md`, PG-INS `pages-api/platforminsights/page.md`,
PG-DEP `pages-api/platforminsights/page/deprecated-metrics.md`, VID-PUB `video-api/guides/publishing`, M-SEND, M-CONV and
M-POL `business-messaging/messenger-platform/send-messages.md`, `conversations.md`, `policy.md`. Under
`https://developers.facebook.com/docs/`: FEED `graph-api/reference/page/feed/`, PAGEPOST `graph-api/reference/page-post/`,
PHOTOS `graph-api/reference/page/photos/`, PG-VIDEOS `graph-api/reference/page/videos/`, REELS
`video-api/guides/reels-publishing/`, STORIES `page-stories-api/`, COMMENT `graph-api/reference/comment/`, OBJ-COMMENTS
`graph-api/reference/object/comments/`, LIKES `graph-api/reference/object/likes/`, REACTIONS
`graph-api/reference/object/reactions/`, RATINGS `graph-api/reference/page/ratings/`, RECO
`graph-api/reference/recommendation/`, INSIGHTS `graph-api/reference/insights/`, RL `graph-api/overview/rate-limiting/`,
PERMS `permissions`.

| Operation | Fact | Status | Source |
|---|---|---|---|
| Text-only post | `POST /{page-id}/feed` with `message` | CONFIRMED | FEED |
| Link post | the same with `link`; preview fields only for links the Page owns, otherwise read from the linked page | CONFIRMED | `PC:173`; FEED |
| Single image | `POST /{page-id}/photos`, `url` or multipart bytes | CONFIRMED | `meta:143-144`; PHOTOS |
| Several images | photos with `published=false`, then `/feed` with `attached_media[n]`; unused uploads deleted after about 24 hours | CONFIRMED | `meta:148`; PHOTOS |
| Video | resumable upload of the bytes, then `/{page-id}/videos`, which also takes `file_url` | CONFIRMED | `meta:149`; VID-PUB; PG-VIDEOS |
| Reel | `/{page-id}/video_reels` start, upload (bytes or `file_url`), finish with `video_state=PUBLISHED` | CONFIRMED | REELS |
| Story | `/{page-id}/video_stories`, or `/photo_stories` with a `photo_id` | CONFIRMED | STORIES |
| Photo limits | JPEG, BMP, PNG, GIF, TIFF, up to 10 MB; whether a signed or expiring `url` works is not stated | CONFIRMED; URL UNVERIFIED | `meta:145-147`; PHOTOS |
| Reel limits | MP4, 9:16, at least 540x960, 24 to 60 fps, 3 to 90 seconds; the host must allow `facebookexternalhit` | CONFIRMED | REELS |
| Story limits | photo as above; video up to 60 seconds | CONFIRMED | STORIES |
| Plain video limits | not stated | UNVERIFIED | `meta:150` |
| Scheduling a post | `published=false` with `scheduled_publish_time`. The Pages guide says 10 minutes to 30 days; the feed reference says 10 minutes to 75 days | CONFIRMED; the outer limit UNVERIFIED (conflict) | `PC:174`; PG-POSTS; FEED |
| Scheduling a video | `scheduled_publish_time`, 10 minutes to 6 months | CONFIRMED | PG-VIDEOS |
| Scheduling Reels and Stories | no parameter shown | UNVERIFIED | REELS |
| Edit a post | `POST /{page-post-id}`: `message`, `attached_media`, `is_hidden`, `scheduled_publish_time` and others, not `link`; only posts the app created | CONFIRMED | `meta:153`; PG-POSTS; PAGEPOST |
| Delete a post | the guide documents `DELETE /{page-post-id}`; the reference says "Only select developers can perform this operation" | UNVERIFIED (conflict) | PG-POSTS; PAGEPOST |
| Read comments | `GET /{post-id}/comments`, `filter=stream` for every level; ids need the MODERATE task | CONFIRMED | `PC:175`; OBJ-COMMENTS |
| Reading permission | the Pages guides now list `pages_read_user_engagement`; the permissions reference lists only `pages_read_user_content`, which the connector requires (`MetaAccountConnector.cs:26`) | UNVERIFIED (conflict; open item M2) | PG-POSTS; PG-CM; PERMS |
| Reply to a comment | `POST /{comment-id}/comments` per the edge reference (MODERATE, `pages_manage_engagement`); the comments guide now shows only `POST /{comment_id}` with an @mention, and the comment reference lists creation from videos only | UNVERIFIED (conflict; open item M1) | `PC:177`; OBJ-COMMENTS; COMMENT; PG-CM |
| Hide a comment | `POST /{comment-id}` with `is_hidden=true` | CONFIRMED | COMMENT |
| Delete a comment | `DELETE /{comment-id}` | CONFIRMED | COMMENT |
| Like | `POST /{comment-id}/likes`; `DELETE` removes | CONFIRMED | `PC:178`; LIKES |
| Other reactions | "Creating: This operation is not supported." | NOT OFFERED | `PC:179`; REACTIONS |
| Comment webhook | `feed`, installed per Page with `POST /{page-id}/subscribed_apps` | CONFIRMED | `PC:180`; PG-WH |
| Messages | read `GET /{page-id}/conversations?platform=messenger` (details for the 20 newest messages); send `POST /{page-id}/messages` with `pages_messaging`; webhook `messages`. A reply is allowed within 24 hours of the person's last message, HUMAN_AGENT within 7 days; three message tags return error 100 since 2026-04-27; a private reply to a comment is one message within 7 days | CONFIRMED | M-CONV; M-SEND; M-POL |
| Recommendations | `GET /{page-id}/ratings` is documented, but Recommendation is "deprecated for v22.0 and future versions"; replying is not offered | UNVERIFIED (read); NOT OFFERED (reply) | RATINGS; RECO |
| Page analytics | `GET /{page-id}/insights` (`read_insights`, `pages_read_engagement`): `page_media_view`, `page_follows`, `page_post_engagements`; kept two years | CONFIRMED | PG-INS; INSIGHTS |
| Post analytics | `GET /{post-id}/insights`: `post_media_view`, `post_total_media_view_unique`, `post_reactions_by_type_total`, `post_clicks` | CONFIRMED | PG-INS |
| Retired metrics | `page_fans`, `page_impressions`, `post_impressions` and the `*_impressions_unique` metrics are replaced by the `*_media_view` and `page_follows` metrics (the date is given as 2025-06-15 on one page and 2026-06-15 on another) | CONFIRMED; date UNVERIFIED | PG-DEP; PG-INS |
| Rate limits | Page token: 4800 times engaged users per rolling 24 hours (errors 80001, 32); 30 API-published Reels a day; Messenger 200 times engaged users a day; no separate limit stated for posts, comments or likes | CONFIRMED; per-operation UNVERIFIED | `meta:219-220`; RL; REELS |

## Instagram (professional account)

Keys: under `https://developers.facebook.com/documentation/instagram-platform/`: IG-CP `content-publishing.md`,
IG-MEDIA `instagram-graph-api/reference/ig-user/media.md`, IG-CPL `instagram-graph-api/reference/ig-user/content_publishing_limit.md`,
IG-MEDIAREF `reference/instagram-media.md`, IG-C `instagram-graph-api/reference/ig-comment.md`, IG-R
`instagram-graph-api/reference/ig-comment/replies.md`, IG-MC `instagram-graph-api/reference/ig-media/comments.md`,
IG-LIKES `instagram-graph-api/reference/ig-user/user-likes.md`, IG-WHF `webhooks/fields.md`, IG-MSG
`instagram-api-with-instagram-login/messaging-api.md`, IG-ULPERM `instagram-api-with-unified-login/permissions.md`,
IG-UINS `api-reference/instagram-user/insights.md`, IG-MINS `reference/instagram-media/insights.md`, IG-CHG
`changelog.md`, IG-IDX `llms.txt`.

| Operation | Fact | Status | Source |
|---|---|---|---|
| Text-only or link post | containers need `image_url` or `video_url` | NOT OFFERED | `PC:230`; IG-MEDIA |
| Single image | `POST /{ig-id}/media` with `image_url`, then `POST /{ig-id}/media_publish` with `creation_id` | CONFIRMED | `PC:229`; IG-CP |
| Carousel | `media_type=CAROUSEL`, up to 20 children | CONFIRMED | `meta:127`; IG-CP |
| Video or Reel | `media_type=REELS`, `video_url` or resumable upload | CONFIRMED | `meta:123-124`; IG-CP |
| Story | `media_type=STORIES`, business accounts | CONFIRMED | `meta:125-126` |
| Image limits | JPEG, up to 8 MB, 4:5 to 1.91:1, 320 to 1440 wide, a public URL; whether a signed or expiring URL works is not stated | CONFIRMED; URL UNVERIFIED | `meta:117-121`; IG-MEDIA |
| Reel limits | MOV or MP4, 3 seconds to 15 minutes, up to 300 MB, 23 to 60 fps | CONFIRMED | `meta:124`; IG-MEDIA |
| Story limits | video 3 to 60 seconds and 100 MB; JPEG up to 8 MB | CONFIRMED | `meta:125`; IG-MEDIA |
| Caption | up to 2,200 characters, 30 hashtags, 20 tags | CONFIRMED | IG-MEDIA |
| Containers | expire after 24 hours; 400 per rolling 24 hours | CONFIRMED | `meta:108`; IG-MEDIA |
| Scheduling | no parameter; GeekSocial holds the post and creates the container near the time | NOT OFFERED | `PC:231`; IG-CP |
| Publish status | `GET /{container-id}?fields=status_code`, once a minute for up to five minutes | CONFIRMED | `meta:128`; IG-CP |
| Edit | only `comment_enabled` can change | NOT OFFERED (caption, media) | IG-MEDIAREF |
| Delete a post | `DELETE /{ig-media-id}` with `instagram_manage_contents`, Instagram API with Facebook Login only; not under Unified Login | CONFIRMED (Facebook Login); NOT OFFERED (Unified Login) | IG-MEDIAREF; IG-CHG |
| Read comments | `GET /{media-id}/comments` (top level, up to 50); `GET /{comment-id}/replies` | CONFIRMED | `meta:82`, `meta:85`; IG-MC |
| Reply | `POST /{comment-id}/replies`; one level; not on hidden or live comments | CONFIRMED | `meta:86`; IG-R |
| Hide, delete a comment | `POST /{comment-id}?hide=true`; `DELETE /{comment-id}` | CONFIRMED | `meta:89-90`; IG-C |
| Like a comment | `POST /{ig-user-id}/likes` with `comment_id`, `instagram_manage_engagement`, Facebook Login only; more than 50 calls in 5 seconds locks it for an hour | CONFIRMED (Facebook Login); NOT OFFERED (Unified Login) | IG-LIKES; IG-CHG |
| Comment webhook | `comments`, `live_comments`, `mentions`; per account `subscribed_apps` | CONFIRMED | `PC:423`; IG-WHF |
| Messages | read and send through the Instagram Login or Facebook Login paths; text under 1,000 characters; only after the person writes, within 24 hours (`human_agent` 7 days). Under Unified Login "Supported" with `instagram_manage_messages`, but no page shows the path for a system-user token | CONFIRMED; Unified Login UNVERIFIED | IG-MSG; IG-ULPERM; M-POL |
| Reviews | none in the reference index | NOT OFFERED | IG-IDX |
| Account analytics | `GET /{ig-id}/insights` (`instagram_manage_insights`): `reach`, `views`, `accounts_engaged`, `total_interactions`, `follows_and_unfollows` | CONFIRMED | IG-UINS |
| Media analytics | `GET /{media-id}/insights`: `views`, `reach`, `likes`, `comments`, `saved`, `shares`, `total_interactions`; story data for 24 hours | CONFIRMED | IG-MINS |
| Retired metrics | `impressions`, `plays` and two Reels play counts, from 2025-04-21; use `views` | CONFIRMED | IG-CHG |
| Rate limits | 4800 times impressions per account per rolling 24 hours; publishing 100 or 50 a day (read `content_publishing_limit`); private replies 750 an hour | CONFIRMED; publishing figure UNVERIFIED | `meta:217`, `meta:224`; IG-CP; IG-CPL |

## LinkedIn organization (company Page)

Version `Linkedin-Version: 202610`, which LinkedIn supports until 2027-10-15 (`linkedin:26`). LinkedIn's latest
version is now 202611, which changes only ads, Boost and Conversions (L-ver, L-recent).

Keys: `L-<page>` is `https://learn.microsoft.com/en-us/linkedin/marketing/community-management/<path>?view=li-lms-2026-10`,
with posts `shares/posts-api`, img `shares/images-api`, vid `shares/videos-api`, doc `shares/documents-api`, multi
`shares/multiimage-post-api`, poll `shares/poll-post-api`, com `shares/comments-api`, react `shares/reactions-api`,
meta `shares/social-metadata-api`, notif `organizations/organization-social-action-notifications`, share
`organizations/share-statistics`, foll `organizations/follower-statistics`, page `organizations/page-statistics`,
mpost `members/post-statistics`, mfoll `members/follower-statistics`, over `community-management-overview`.
L-access is `.../linkedin/marketing/increasing-access`, L-ver `.../linkedin/marketing/versioning`, L-recent
`.../linkedin/marketing/integrations/recent-changes?view=li-lms-2026-11`, L-rate
`.../linkedin/shared/api-guide/concepts/rate-limits`.

| Operation | Fact | Status | Source |
|---|---|---|---|
| Text-only post | `POST /rest/posts`, author the organization URN, `lifecycleState: PUBLISHED`; scope `w_organization_social`; role ADMINISTRATOR, DIRECT_SPONSORED_CONTENT_POSTER or CONTENT_ADMIN | CONFIRMED | L-posts; `PC:192` |
| Link (article) | `content.article` with `source`, `title`, `description` and `thumbnail` (an image URN uploaded through the Images API). LinkedIn does not scrape the URL | CONFIRMED; whether the thumbnail is required: the pages conflict | L-posts; `linkedin:87-90` |
| Single image | Images API `initializeUpload`, PUT the bytes, then `content.media.id` | CONFIRMED | L-img |
| Several images | `content.multiImage`, 2 to 20 images, organic posts only | CONFIRMED | L-multi |
| Video | Videos API, uploaded in 4 MB parts, then `finalizeUpload` | CONFIRMED | L-vid |
| Document | Documents API: PPT, PPTX, DOC, DOCX, PDF | CONFIRMED | L-doc |
| Poll | `content.poll`: question up to 140 characters, 2 to 4 options up to 30 characters, 1, 3, 7 or 14 days; organic only | CONFIRMED | L-poll |
| Carousel | sponsored posts only ("Organic carousel is currently not supported") | NOT OFFERED | L-posts |
| Media input | bytes only; no field takes a media URL | CONFIRMED | `linkedin:40`; L-img |
| Image limits | JPG, GIF (up to 250 frames), PNG; fewer than 36,152,320 pixels; no maximum size in MB stated | CONFIRMED; MB UNVERIFIED | L-img; `linkedin:45` |
| Video limits | MP4, 3 seconds to 30 minutes; the page gives the size both as 75 KB to 500 MB and as 5 GB maximum | CONFIRMED; size UNVERIFIED (conflict) | L-vid |
| Document limits | 100 MB, 300 pages | CONFIRMED | L-doc |
| Scheduling | no schedule field; "PUBLISHED is the only accepted field during creation". GeekSocial holds the post and publishes at the time | NOT OFFERED | L-posts |
| Edit a post | `POST /rest/posts/{urn}` with `X-RestLi-Method: PARTIAL_UPDATE`; only `commentary`, `contentCallToActionLabel`, `contentLandingPage`, `lifecycleState` and `adContext`; 204. Media cannot change | CONFIRMED | L-posts |
| Delete a post | `DELETE /rest/posts/{urn}`, idempotent, 204; no batch delete | CONFIRMED | L-posts |
| Read comments | `GET /rest/socialActions/{postUrn or commentUrn}/comments`; scope `r_organization_social_feed` | CONFIRMED | L-com |
| Reply | `POST /rest/socialActions/{postUrn}/comments`, `parentComment` for a nested reply; scope `w_organization_social_feed`. The comments page lists ADMINISTRATOR, DIRECT_SPONSORED_CONTENT_POSTER and RECRUITING_POSTER, while the posts page lets CONTENT_ADMIN comment through `w_organization_social`. Maximum nesting depth not stated | CONFIRMED; CONTENT_ADMIN replying and depth UNVERIFIED | L-com; L-posts |
| Edit or delete a comment | edit by PARTIAL_UPDATE (message text and attributes only); `DELETE .../comments/{id}?actor=` | CONFIRMED; deleting a member's comment as the Page UNVERIFIED | L-com |
| Hide one comment | no endpoint; comments can only be closed or reopened for a whole post (`POST /rest/socialMetadata/{postUrn}`, `commentsState`) | NOT OFFERED (one comment); CONFIRMED (whole post) | L-com; L-meta |
| React | `POST /rest/reactions?actor=` on a post or comment: LIKE, PRAISE, EMPATHY, INTEREST, APPRECIATION, ENTERTAINMENT; `DELETE` removes | CONFIRMED | L-react |
| Comment events | webhook `ORGANIZATION_SOCIAL_ACTION_NOTIFICATIONS` (LIKE, COMMENT, SHARE, SHARE_MENTION, ADMIN_COMMENT, COMMENT_EDIT, COMMENT_DELETE), PUBLIC posts only, Standard tier only; pull from `organizationalEntityNotifications`, kept 60 days | CONFIRMED | L-notif; L-access; `linkedin:164`, `linkedin:169` |
| Messages | no messaging endpoint or scope in Community Management | NOT OFFERED | L-access; L-over |
| Reviews | none | NOT OFFERED | |
| Post analytics | `GET /rest/organizationalEntityShareStatistics` with `shares=` or `ugcPosts=`: clicks, comments, engagement, impressions, likes, shares, unique impressions (lifetime); scope `rw_organization_admin`, ADMINISTRATOR | CONFIRMED | L-share |
| Account analytics | the same endpoint by DAY or MONTH over a rolling 12 months; `organizationalEntityFollowerStatistics`; `organizationPageStatistics` | CONFIRMED | L-share; L-foll; L-page |
| Rate limits | Development tier 500 calls per app and 100 per member a day, no BATCH_GET, no push; Standard limits shown only in the Developer Portal; reset midnight UTC; creating comments has a one-minute throttle per member (429) | CONFIRMED; Standard per-operation figures UNVERIFIED | L-access; L-rate; L-com |

## LinkedIn member (personal profile)

| Operation | Fact | Status | Source |
|---|---|---|---|
| Content types, media, publish, edit, delete | the same Posts API and limits with `author: urn:li:person:{id}`, scope `w_member_social` | CONFIRMED | L-posts; L-img |
| Scheduling | as for an organization | NOT OFFERED | L-posts |
| Read comments | `r_member_social` is "restricted" to approved users; `r_member_social_feed` is "granted to select developers only" | NOT OFFERED to GeekSocial | L-posts; L-com; L-over; `PC:211` |
| Reply, react | the endpoints exist, but GeekSocial cannot read the comments it would answer | CONFIRMED (endpoint only) | L-com; `PC:212` |
| Comment events | the notifications API takes only an organization | NOT OFFERED | L-notif; `PC:213` |
| Messages, reviews | none | NOT OFFERED | L-access |
| Post analytics | `GET /rest/memberCreatorPostAnalytics` (`q=entity` or `q=me`): IMPRESSION, MEMBERS_REACHED, RESHARE, REACTION, COMMENT; scope `r_member_postAnalytics` | CONFIRMED; whether the Development tier grants it UNVERIFIED | L-mpost; L-access |
| Account analytics | `memberFollowersCount`; scope `r_member_profileAnalytics` | CONFIRMED | L-mfoll; L-access |
| Rate limits | as for an organization | CONFIRMED | L-access |

## X (API v2, pay-per-use)

Keys: `X-<page>` is `https://docs.x.com/x-api/<path>.md`, with create `posts/create-or-edit-post`, edit
`fundamentals/edit-posts`, del `posts/delete-post`, media `media/introduction`, bp `media/quickstart/best-practices`,
init `media/initialize-media-upload`, manage `posts/manage-tweets/introduction`, hide `posts/hide-replies/introduction`,
like `users/like-post`, dm `direct-messages/manage/introduction` and `direct-messages/lookup/introduction`, xaa
`activity/introduction`, metrics `fundamentals/metrics`, analytics `posts/get-post-analytics`, price
`getting-started/pricing`, rate `fundamentals/rate-limits`, dd `fundamentals/data-dictionary/reference`. X-log is
`https://docs.x.com/changelog.md`.

| Operation | Fact | Status | Source |
|---|---|---|---|
| Text-only post | `POST /2/tweets` with `text`, 280 characters unless the account has X Premium; scopes `tweet.read tweet.write users.read`; $0.015 | CONFIRMED | X-create; X-price |
| Link post | a URL in `text`; $0.200 | CONFIRMED | X-price |
| Images | `media.media_ids`, "up to 4 photos" (the request schema allows 20) | CONFIRMED (4); schema conflict | X-media; X-create |
| GIF or video | one animated GIF or one video per post | CONFIRMED | X-media |
| Document | no document media category | NOT OFFERED | X-init |
| Poll | 2 to 4 options of 1 to 25 characters, 5 to 10,080 minutes | CONFIRMED | X-create |
| Media input | bytes or base64; no URL parameter | CONFIRMED | X-bp; `x:118` |
| Image limits | JPG, PNG, GIF, WEBP up to 5 MB; animated GIF up to 15 MB, 1280x1080, 350 frames | CONFIRMED | X-media; X-bp |
| Video limits | chunked upload; 0.5 seconds to 20 minutes and 8 GB (Premium 125 minutes, 16 GB); H264 High, AAC LC, 60 FPS or less, 32x32 to 1280x1024, 1:3 to 3:1 | CONFIRMED | X-media; X-bp |
| Alt text | `POST /2/media/metadata`, up to 1,000 characters, $0.005 | CONFIRMED | `x:125`; X-price |
| Scheduling | no schedule field (`additionalProperties: false`) | NOT OFFERED | X-create |
| Edit a post | `POST /2/tweets` with `edit_options.previous_post_id`; each edit gets a new id; polls and replies to others cannot be edited. X-edit says "within 30 minutes of posting, up to 5 times"; the 2025-10-03 changelog says X Premium and "within the last hour" | CONFIRMED (endpoint); window and Premium UNVERIFIED (conflict) | X-create; X-edit; X-log |
| Delete a post | `DELETE /2/tweets/{id}`, own posts only; deletes the whole edit chain | CONFIRMED | X-del; X-edit |
| Read replies | `GET /2/users/{id}/mentions` (800 most recent); `search/recent` with `conversation_id:` (7 days); $0.005 per post returned | CONFIRMED | `x:67-71`; X-price |
| Reply | `reply.in_reply_to_tweet_id`; a self-serve app may reply only when summoned (an @mention or quote); AI-written replies need X's prior approval | CONFIRMED | X-manage; `x:162` |
| Hide a reply | `PUT /2/tweets/{id}/hidden`; scope `tweet.moderate.write`; replies to the user's own posts | CONFIRMED | X-hide |
| Delete another's reply | own posts only | NOT OFFERED | X-del |
| Like | `POST /2/users/{id}/likes`, scope `like.write`; its price line is not named | CONFIRMED; price UNVERIFIED | X-like |
| Reply events | X Activity API: `post.reply.create` (direct replies), `post.mention.create`, `like.create`; 1,500 subscriptions on self-serve | CONFIRMED | X-xaa |
| Messages | read `GET /2/dm_events` (`dm.read`, last 30 days, $0.010 per event); send `POST /2/dm_conversations/with/{id}/messages` (`dm.write`; "DM Interaction: Create" is $0.015, not named as this endpoint) | CONFIRMED; send price inferred | X-dm; X-price |
| Reviews | none | NOT OFFERED | |
| Post analytics | `public_metrics` on any post; `non_public_metrics` and `organic_metrics` on own posts from the last 30 days; `GET /2/tweets/analytics` up to 100 ids, hourly to total | CONFIRMED; the analytics endpoint's price UNVERIFIED | X-metrics; X-analytics |
| Account analytics | the user's `public_metrics` at $0.010 per user read; no account analytics endpoint | CONFIRMED / NOT OFFERED | X-dd; X-price |
| Rate limits (per app / per user, 15 minutes unless stated) | `POST /2/tweets` 10,000 a day / 100; delete 50 per user; mentions 450 / 300; `search/recent` 450 / 300; hide 50 per user; likes 50 and 1,000 a day per user; `dm_events` 15 per user; DM send 1,440 a day; analytics 300 / 300; media upload 50,000 a day / 500 | CONFIRMED | X-rate |

## Google Business Profile

v1 APIs for business information, notifications and performance; v4 `mybusiness.googleapis.com` for posts,
reviews and media (`gbp:25-34`).

Keys, under `https://developers.google.com/my-business/`: LP `reference/rest/v4/accounts.locations.localPosts`
(LPPATCH `/patch`, LPDEL `/delete`); POSTS `content/posts-data`; MEDIA `reference/rest/v4/accounts.locations.media`;
REV `reference/rest/v4/accounts.locations.reviews` (REVLIST `/list`, UPD `/updateReply`, DEL `/deleteReply`); BGR
`reference/rest/v4/accounts.locations/batchGetReviews`; REVDATA `content/review-data`; NOTIF
`reference/notifications/rest/v1/NotificationSetting`; PERF `reference/performance/rest` (DM `/v1/DailyMetric`, KW
`/v1/locations.searchkeywords.impressions.monthly/list`); SUNSET `content/sunset-dates`; LIMITS `content/limits`; REF
`reference/rest`. Help: PHOTOHELP https://support.google.com/business/answer/6103862, CHAT
https://support.google.com/business/answer/14919056.

| Operation | Fact | Status | Source |
|---|---|---|---|
| Standard post | `POST v4 .../locations/{l}/localPosts`, `topicType: STANDARD`: summary, photos, a call to action (BOOK, ORDER, SHOP, LEARN_MORE, SIGN_UP, CALL) | CONFIRMED | `gbp:87`, `gbp:94`; LP |
| Event post | `EVENT` with `event{title, schedule}`, every schedule field set | CONFIRMED | `gbp:89`; LP |
| Offer post | `OFFER` with `event` and `offer{couponCode, redeemOnlineUrl, termsConditions}`; the call to action is ignored | CONFIRMED | `gbp:90`; LP |
| Alert post | only `COVID_19`, "not always available for authoring" | NOT OFFERED (in effect) | `gbp:91`; LP |
| Product post | "Product Posts cannot be created using the Google My Business API at this time." | NOT OFFERED | POSTS |
| Post summary length | not stated | UNVERIFIED | `gbp:93` |
| Post media | `media[].sourceUrl`, a public URL; no bytes | CONFIRMED | `gbp:114`; MEDIA |
| Location photo | `POST .../media` by URL, or as bytes through `media:startUpload`, the upload, then `media.create` with `dataRef`; get, list, patch (metadata), delete | CONFIRMED | `gbp:116-119`; MEDIA |
| Photo limits | JPG or PNG, 10 KB to 5 MB, at least 250 px, 720x720 recommended; no stated maximum dimensions or aspect ratio | CONFIRMED; maximum and aspect ratio UNVERIFIED | `gbp:120-124`; PHOTOHELP; MEDIA |
| Video | help allows 30 seconds, 75 MB, 720p or more; the API says "In this version, only photos are supported" | UNVERIFIED by the API | `gbp:127`; PHOTOHELP; MEDIA |
| Scheduling | native: `scheduledTime`; `event.recurrenceInfo` repeats daily, weekly or monthly; states SCHEDULED and RECURRING | CONFIRMED | `gbp:96`; LP |
| Edit a post | `PATCH .../localPosts/{id}?updateMask=`; Google's example edits `summary`; no page lists the fields that cannot change | CONFIRMED (summary); other fields UNVERIFIED | LPPATCH; POSTS |
| Delete a post | `DELETE .../localPosts/{id}` | CONFIRMED | LPDEL |
| Post outcome | LIVE, PROCESSING or REJECTED; posts older than six months are archived unless dated | CONFIRMED | `gbp:97`, `gbp:102` |
| Comments | local posts have no comments; Q&A was discontinued 2025-11-03 | NOT OFFERED | `gbp:33`; SUNSET |
| Messages | chat ended 2024-07-31 and the Business Messages API redirects to RCS for Business; no messaging endpoint in the reference | NOT OFFERED | CHAT; REF |
| List reviews | `GET v4 .../reviews`, 50 per page, sort by rating or update time; verified locations only | CONFIRMED | `gbp:61`; REVLIST |
| Get review | `GET .../reviews/{r}` (no `reviewReplyState` or `policyViolation`) | CONFIRMED | `gbp:62`; REVDATA |
| Reviews across locations | `POST v4 accounts/{a}/locations:batchGetReviews`, up to 50 locations | CONFIRMED | `gbp:63`; BGR |
| Reply or update a reply | `PUT .../reviews/{r}/reply`, at most 4096 bytes, verified locations only; Google moderates the reply (PENDING, REJECTED, APPROVED) for up to 30 days | CONFIRMED | `gbp:65-67`, `gbp:75`; UPD |
| Delete a reply | `DELETE .../reviews/{r}/reply`, verified locations only | CONFIRMED | `gbp:66`; DEL |
| Delete or react to a review | the only review methods are list, get, updateReply and deleteReply | NOT OFFERED | `PC:266`; REV |
| New-review push | Pub/Sub NEW_REVIEW and UPDATED_REVIEW, resource names only | CONFIRMED | `gbp:149-150`; NOTIF |
| Profile analytics | `businessprofileperformance.googleapis.com/v1/locations/{id}:fetchMultiDailyMetricsTimeSeries`: Maps and Search impressions by device, direction requests, call clicks, website clicks, messaging clicks, bookings, food menu clicks (BUSINESS_CONVERSATIONS and BUSINESS_FOOD_ORDERS deprecated); monthly search keywords | CONFIRMED | `gbp:34`; PERF; DM; KW |
| Post analytics | `localPosts.reportInsights` discontinued 2023-02-20, no replacement | NOT OFFERED | SUNSET |
| Photo views | `insights.viewCount` still documented; MediaInsights listed as discontinued 2023-02-20 | UNVERIFIED (conflict) | MEDIA; SUNSET |
| Quotas | 300 QPM for each v1 API, 429 above; 10 edits a minute per profile; daily Create Location 300 and Update Location 10,000; no row for v4 reviews, posts or media | CONFIRMED; v4 UNVERIFIED | `gbp:205-206`; LIMITS |

## YouTube (Data API v3)

Keys, under `https://developers.google.com/youtube/`: INS `v3/docs/videos/insert`, VID `v3/docs/videos`, UPD
`v3/docs/videos/update`, VDEL `v3/docs/videos/delete`, THUMB `v3/docs/thumbnails/set`, POSTS `v3/docs/posts` (PINS
`/insert`, PIMG `/imageUpload`), CTL `v3/docs/commentThreads/list`, COM `v3/docs/comments` (CINS `/insert`, CLIST
`/list`, MOD `/setModerationStatus`), PUSH `v3/guides/push_notifications`, QUOTA `v3/determine_quota_cost`, REVH
`v3/revision_history`, DOCS `v3/docs`, YA `analytics/reference/reports/query`, YAM `analytics/metrics`, YADM
`analytics/data_model`, YAAUTH `reporting/guides/authorization`, YREP `reporting/v1/reports`. Help: SHORTS
https://support.google.com/youtube/answer/15424877, UPHELP https://support.google.com/youtube/answer/71673, THELP
https://support.google.com/youtube/answer/72431.

| Operation | Fact | Status | Source |
|---|---|---|---|
| Video | `videos.insert`, resumable upload of the bytes | CONFIRMED | `youtube:19`; INS |
| Short | a `videos.insert` upload; square or vertical, up to three minutes, is filed as a Short | CONFIRMED | `youtube:25`; SHORTS |
| Community post | a `posts` resource (list, insert, update text, delete, imageUpload; text or 1 to 10 images) is "in a pilot phase with access limited to select launch partners", general availability planned for 2027 | NOT OFFERED to GeekSocial | POSTS; REVH |
| Video limits | up to 256 GB or 12 hours; 15 minutes unless the account is verified; MP4, MOV, WebM and other formats | CONFIRMED | `youtube:87-90`; INS; UPHELP |
| Media by URL | none; bytes only | NOT OFFERED | `youtube:97`; INS |
| Thumbnail | `thumbnails.set`, JPEG or PNG up to 50 MB, about 50 units; needs a verified account. Help says custom Shorts thumbnails are set only in YouTube Studio; the API says it supports Shorts | CONFIRMED; Shorts UNVERIFIED (conflict) | `youtube:22-23`; THUMB; THELP |
| Scheduling | native: `status.publishAt`, with `privacyStatus: private`, only for a never-published video | CONFIRMED | `youtube:21`; VID |
| Edit a video | `videos.update`, 50 units: title, description, tags, category, privacy, `publishAt` and other metadata; a property left out is deleted | CONFIRMED | UPD |
| Delete a video | `videos.delete`, 50 units | CONFIRMED | VDEL; QUOTA |
| Read comments | `commentThreads.list` (1 unit, up to 100), `comments.list?parentId=`; `allThreadsRelatedToChannelId` leaves out threads on posts | CONFIRMED; reading scope UNVERIFIED | `youtube:38-42`; CTL; CLIST |
| Reply | `comments.insert` with `snippet.parentId`, `youtube.force-ssl`; top-level comments only. The page gives 50 units and also "snippet part has a quota cost of 2 units" | CONFIRMED; cost UNVERIFIED (conflict) | `youtube:43-45`; CINS |
| Hold, publish, reject | `comments.setModerationStatus`, up to 50 ids, owner only, 50 units; `banAuthor` only with rejected | CONFIRMED | `youtube:47-48`; MOD |
| Delete a comment | the author only; the owner rejects instead | CONFIRMED | `youtube:49`; MOD |
| Like, heart, mark as spam | no method | NOT OFFERED | `youtube:51-52`; COM |
| Comment push | WebSub fires on uploads and title or description changes only | NOT OFFERED | `youtube:75`; PUSH |
| Messages, reviews | none | NOT OFFERED | DOCS |
| Analytics | `GET youtubeanalytics.googleapis.com/v2/reports`, `ids=channel==MINE`: views, engagedViews, estimatedMinutesWatched, averageViewDuration, likes, comments, shares, subscribersGained; one unit a request; bulk CSVs from the Reporting API, kept 60 days. The scope is `yt-analytics.readonly` per the authorization guide and `youtube.readonly` per the query page | CONFIRMED; scope UNVERIFIED (conflict) | YA; YAM; YADM; YAAUTH; YREP |
| Quota | per Cloud project 10,000 units a day plus 100 `videos.insert` and 100 `search.list`; resets midnight Pacific; more needs an audit | CONFIRMED | `youtube:107-114`; QUOTA |

## TikTok

Two products: **B**, the Business API v1.3 Accounts API, which carries comments; **D**, developers.tiktok.com's
Content Posting and Display APIs, which have no comment scope.

Keys: TB pages are `https://business-api.tiktok.com/portal/docs/<page>/v1.3`: TB-vid
`publish-a-public-video-post-to-an-owned-account`, TB-photo `publish-a-photo-post-to-an-owned-account`, TB-status
`get-the-publishing-status-of-a-tiktok-post`, TB-scope `permission-scope`, TB-post `get-post-data-of-a-tiktok-account`,
TB-profile `get-profile-data-of-a-tiktok-account`, TB-rl `accounts-api-rate-limits`, TB-send
`send-a-message-to-a-conversation`, TB-msgs `get-a-list-of-messages`, TB-dmaccess `access-to-business-messaging-api`.
TD pages are `https://developers.tiktok.com/doc/<page>`: TD-direct `content-posting-api-reference-direct-post`,
TD-photo `content-posting-api-reference-photo-post`, TD-media `content-posting-api-media-transfer-guide`, TD-scopes
`tiktok-api-scopes`, TD-vlist `tiktok-api-v2-video-list`.

| Operation | Fact | Status | Source |
|---|---|---|---|
| Video (B) | `POST /business/video/publish/`, `video_url` only | CONFIRMED | TB-vid |
| Photo carousel (B) | `POST /business/photo/publish/`, up to 35 URLs | CONFIRMED | TB-photo |
| Video (D) | `POST /v2/post/publish/video/init/` (`video.publish`); to the creator's inbox through `/v2/post/publish/inbox/video/init/` (`video.upload`) | CONFIRMED | TD-direct; `tiktok:28` |
| Photo (D) | `POST /v2/post/publish/content/init/`, up to 35, URL only | CONFIRMED | TD-photo |
| Text-only or link post | no endpoint in either product | NOT OFFERED | TB-vid; TD-direct |
| Video limits (B) | MP4, MOV, WebM up to 1 GB, 3 to 600 seconds (per-account maximum from `/business/video/settings/`), at least 360 px, 23 to 60 fps; caption up to 2,200 characters | CONFIRMED | TB-vid |
| Photo limits (B and D) | JPEG or WebP, up to 20 MB, at most 1080x1920 | CONFIRMED | TB-photo; TD-media |
| Video limits (D) | MP4, WebM, MOV up to 4 GB, 360 to 4,096 px, 23 to 60 fps, up to 10 minutes | CONFIRMED | TD-media |
| Media transport | B: URL only, on a verified domain or URL prefix; unverified URLs refused since 2023-11-16. D: chunked bytes or a URL on a verified domain, https, no redirect | CONFIRMED | TB-vid; TD-media; `tiktok:53` |
| Scheduling | no publish-time field in either product | NOT OFFERED | TB-vid; TB-photo; TD-direct; TD-photo |
| Publish status | B `GET /business/publish/status/`; D `POST /v2/post/publish/status/fetch/`; both also by webhook | CONFIRMED | `tiktok:31-32`, `tiktok:55-56` |
| Edit or delete (B) | the Posts reference lists only publish, status, hashtags and location tags | NOT OFFERED | TB-status |
| Edit or delete (D) | scope `video.manage` is defined as editing and deleting published posts; no endpoint is documented | UNVERIFIED | TD-scopes |
| Read comments (B) | `GET /business/comment/list/` (30 a page, first 500 only) and `/comment/reply/list/` (no hidden comments); permission 18030100 | CONFIRMED | TB-scope; `tiktok:96-97` |
| Reply, create (B) | `POST /business/comment/reply/create/` (up to 1,200 characters), `/comment/create/` | CONFIRMED | TB-scope; `tiktok:98` |
| Hide, like, delete a comment (B) | `/business/comment/hide/`, `/like/`, `/delete/`; permission 18030200 | CONFIRMED | TB-scope; `tiktok:100-102` |
| Comment depth | comments and replies; whether a reply can be answered is not stated | UNVERIFIED | `tiktok:122` |
| Comment webhook (B) | `comment.update` through `POST /business/webhook/update/`, "within five minutes", public posts only | CONFIRMED | `tiktok:120-125` |
| Messages (B) | send `POST /business/message/send/`, up to 10 messages in the 48 hours after a received message; read `GET /business/message/content/list/` (20 newest). Needs Advanced Access or a Verified Business Account, a data security review (US accounts also the USDS addendum); not in the EEA, Switzerland or the UK | CONFIRMED | TB-send; TB-msgs; TB-dmaccess |
| Messages (D), reviews | none (D has only data-export scopes) | NOT OFFERED | TD-scopes |
| Post analytics (B) | `GET /business/video/list/`: likes, comments, shares, favorites, reach, views, watch time, completion, follows, profile views, clicks, audience; updated 24 to 48 hours later | CONFIRMED | TB-post |
| Account analytics (B) | `GET /business/get/`: followers, views, profile views, likes, comments, shares, follower changes; up to 60 days back | CONFIRMED | TB-profile |
| Analytics (D) | `POST /v2/video/list/`: view, like, comment and share counts | CONFIRMED | TD-vlist |
| Rate limits | B publishing 6 a minute and 15 a day per account; Accounts API 40 a minute per account per endpoint, 600 or 1,000 a minute per app. D: 6 inits a minute per token, about 15 posts a day per creator; before the audit 5 users a day, private only | CONFIRMED; comment caps UNVERIFIED | TB-vid; TB-rl; TD-direct; `tiktok:249-257` |

## Pinterest (API v5, spec 5.30.0)

Keys: P-spec `https://raw.githubusercontent.com/pinterest/api-description/main/v5/openapi.json` (5.30.0, released
2026-11-18); P-ref `https://developers.pinterest.com/docs/api/v5/pins-create`; P-guide
`https://developers.pinterest.com/docs/api-features/creating-boards-and-pins/`; P-rl
`https://developers.pinterest.com/docs/reference/rate-limits/`.

| Operation | Fact | Status | Source |
|---|---|---|---|
| Image Pin | `POST /v5/pins` with `image_url` or `image_base64`, on an existing board; title up to 100, description 800, alt text 500, link 2,048 | CONFIRMED | P-spec; `pinterest:32-34` |
| Video Pin | `POST /media`, upload the bytes to the returned `upload_url`, wait for `GET /media/{id}` to report success, then `POST /pins` with `video_id` and a cover | CONFIRMED | P-guide |
| Carousel | the spec still has 2 to 5 `multiple_image_urls`; the guide says organic formats are now image or video Pins | UNVERIFIED (conflict) | P-spec; P-guide |
| Text-only post | a Pin needs media | NOT OFFERED | P-spec |
| Media limits | none stated in the API reference (the figures in the research are from the ads spec) | UNVERIFIED | `pinterest:74-79` |
| Media transport | images by URL or base64; video as bytes; no domain verification; whether `image_url` must be public is not stated | CONFIRMED; public URL UNVERIFIED | P-spec; `pinterest:69-70` |
| Scheduling | no publish-time field | NOT OFFERED | P-spec; P-ref |
| Edit | `PATCH /pins/{pin_id}`: title, description, alt text, link, board; not media. "This endpoint is currently in beta and not available to all apps." | CONFIRMED (beta) | P-spec |
| Delete | `DELETE /pins/{pin_id}` | CONFIRMED | P-spec |
| Comments, messages, reviews | no operation in the spec; the only webhook is for Lead Ads | NOT OFFERED | P-spec; `pinterest:91-93`, `pinterest:104-105` |
| Pin analytics | `GET /pins/{pin_id}/analytics`, 90 days back: impressions, outbound clicks, Pin clicks, saves, comments, reactions, follows, profile visits, video views | CONFIRMED | P-spec |
| Account analytics | `GET /user_account/analytics`, `/top_pins`, `/top_video_pins` (`user_accounts:read`) | CONFIRMED | P-spec |
| Rate limits | `org_write`: Trial 300 a day per app, Standard 100 a minute per user per app; `org_read` 1,000 / 1,000; `org_analytics` 1,000 / 60 | CONFIRMED | P-rl |
| Personal accounts | apps registered on or after 2026-09-14 get `PINNER_DATA_ACCESS_DENIED` on reads; whether `POST /pins` is blocked too is not stated | UNVERIFIED | P-guide; `pinterest:150-151` |

## Nextdoor (Publish API, "currently in closed BETA")

Nextdoor's application form excludes promoting a business (`PLATFORM-ACCESS-REGISTER.md`, Nextdoor). The
operations are recorded for that decision. Paths are relative to `https://nextdoor.com/external/api/partner/v1`.

Keys: under `https://developer.nextdoor.com/reference/`: N-create `create-post.md`, N-event `create-event-post.md`,
N-edit `edit-a-post.md`, N-del `delete-a-post.md`, N-comment `create-comment-to-a-post-create-reply-to-a-comment.md`,
N-cedit `edit-a-comment.md`, N-cdel `delete-a-comment.md`, N-toggle `toggle-commenting.md`, N-posts `get-your-posts.md`,
N-errors `sharing-error-codes.md`; N-index `https://developer.nextdoor.com/llms.txt`.

| Operation | Fact | Status | Source |
|---|---|---|---|
| Text post | `POST /post/create/`, body up to 8,192, `secure_profile_id` from `/me/profiles` ("Support neighbor profile and business profile"); `post:write` | CONFIRMED | N-create |
| Link | `smartlink_url`, previewed by Nextdoor | CONFIRMED | N-create |
| Photo | `media_attachments`, up to 10 public URLs; no formats or sizes stated | CONFIRMED; limits UNVERIFIED | N-create |
| Video | not stated | UNVERIFIED | N-create |
| Event post | `POST /post/event/`; "Entity page user can not create event post" | CONFIRMED | N-event |
| Scheduling | no publish-time field | NOT OFFERED | N-create |
| Edit a post | `PUT /post/`: text, hashtag, location, link; not media | CONFIRMED | N-edit |
| Delete a post | `DELETE /post/?id=` | CONFIRMED | N-del |
| Read comments | `GET /post/` returns comments and replies, but "Business and Entity page users are not supported at the moment" | NOT OFFERED for business profiles | N-posts |
| Reply | `POST /comment/` with `parent_comment_id`; `comment:write` | CONFIRMED | N-comment |
| Edit, delete own comment | `PUT /comment/`, `DELETE /comment/?id=` | CONFIRMED | N-cedit; N-cdel |
| Comments off on a post | `POST /post/toggle_commenting/` | CONFIRMED | N-toggle |
| Hide, react, webhook, messages, reviews | no endpoint in the index | NOT OFFERED | N-index |
| Analytics | `view_count` and `smartlink_click_count` on `GET /post/`, which excludes business profiles | NOT OFFERED for business profiles | N-posts |
| Rate limits | none stated; no 429 among the error codes | UNVERIFIED | N-errors |

## Across networks: what Phase 2 designs around

**Native scheduling** exists only on Facebook (posts and photos, 10 minutes to 30 days by the Pages guide; video
6 months), Google Business Profile (`scheduledTime`, recurring posts) and YouTube (`publishAt`, private never-published
videos). Instagram, LinkedIn, X, TikTok, Pinterest and Nextdoor have none. So GeekSocial's own scheduler, which
holds a post and publishes it at its time, is the general path. Native scheduling is at most an optimization.

**How media reaches each network** decides the shape of the roadmap's media rule (short-lived signed URLs, and
GeekRepository never streams file bytes):

| Network | By URL | As bytes |
|---|---|---|
| Facebook | yes (photos, video `file_url`, Reels); whether a signed or expiring URL works is UNVERIFIED | yes |
| Instagram | only (public URL; signed or expiring UNVERIFIED) | Reels by resumable upload |
| Google Business Profile | post media only by URL | location photos |
| TikTok | B only by URL, on a verified domain; D by URL on a verified domain | D, in chunks |
| Nextdoor | only (public URLs) | no |
| Pinterest | images | images (base64) and video |
| LinkedIn, X, YouTube | no | only |

Where a network takes only bytes, something must fetch the file from storage and upload it. Where it takes only
a URL, the URL must stay valid until the platform has fetched it, and for TikTok its domain must be verified.

**Comments** can be read and replied to on Facebook, Instagram, LinkedIn organizations, X (replies only when
summoned), YouTube and TikTok (Business API). Google Business Profile has reviews instead. A LinkedIn member
profile and Pinterest have none. A Nextdoor business profile can reply but cannot read its comments.

**Editing after publishing** is broad on Facebook and YouTube. It is limited to text on LinkedIn, Nextdoor and
Google Business Profile (summary). X allows 30 minutes and five edits. Pinterest's edit is in beta. Instagram can
change only whether comments are on, and TikTok documents none.

## The code's matrix against this one

`SocialPlatformCapabilities` (`GeekApplication/Models/Social/SocialPlatforms.cs:181-238`) has rows for Facebook,
Instagram and both LinkedIn kinds. Where it and this matrix differ:

| Row | Code | This matrix | Effect |
|---|---|---|---|
| Facebook scheduling | 10 minutes to 30 days (`:191-192`) | the Pages guide says 30 days, the feed reference 75 | none; the code takes the shorter limit |
| Facebook reading permission | the connector requires `pages_read_user_content` (`MetaAccountConnector.cs:26`) | the Pages guides name `pages_read_user_engagement`, which the permissions reference does not list | if Meta's dashboard offers only the new name, every connect fails naming the missing permission (loud, not silent). Check when the login configuration is created (open item M2) |
| Instagram reactions | none (`:208`) | liking a comment exists for the Facebook Login setup only | none under Unified Login; which setup the app uses is open (`PC:248-251`) |
| LinkedIn organization webhooks | `DeliversWebhooks = true` (`:223`) | Standard tier only; push is disabled in Development | until Standard access, nothing arrives by push; a read must be started by a person |
| LinkedIn link picture | `LinkPostTakesPreview = true` (`:219`) | the thumbnail must be an image uploaded through the Images API | the LinkedIn publisher, when built, uploads the picture's bytes before posting |

The other networks have no row in the code, because no connector for them is built.

## Changed or contradicted since 2026-11-16

To be carried into `PLATFORM-CAPABILITIES.md` and the research when each connector is built.

Meta:
1. Instagram can like a comment and delete a post, both for the Facebook Login setup only (IG-LIKES, IG-MEDIAREF,
   IG-CHG). `PC:234` and `PC:463` say no reaction exists.
2. Facebook's scheduling window is 30 days in the Pages guide and 75 days in the feed reference (`PC:174` says 30).
3. Replying to a Facebook comment: the guide and the references now disagree (open item M1, `PC:177`).
4. Deleting a Facebook post: the guide documents it; the reference says "select developers" only.
5. The reading permission's name (above; `PC:176`).
6. Page Insights' retirement date of the `*_impressions_unique` metrics is given as 2025 and 2026.
7. Recommendations are documented on `/ratings` and deprecated on Recommendation.

LinkedIn and X:
8. LinkedIn's latest version is 202611 (`linkedin:21` says 202610); it changes nothing GeekSocial uses.
9. Replying to and reacting to comments: the comments page lists ADMINISTRATOR, DIRECT_SPONSORED_CONTENT_POSTER and
   RECRUITING_POSTER, not CONTENT_ADMIN (`PC:192` lists CONTENT_ADMIN, correct for posts).
10. LinkedIn member analytics exist (`r_member_postAnalytics`, `r_member_profileAnalytics`); `PC` section 2c has
    none.
11. X likes are documented (`PC:301` said not read).
12. X editing: 30 minutes and five edits on the editing page, X Premium and one hour in the changelog.
13. X photos per post: 4 in the prose, 20 in the request schema.
14. X Activity API billing: the pricing table says `post.delete` is not billed; the Activity API page says it is.

Google:
15. A YouTube posts API exists, for pilot partners only, general availability planned for 2027 (`youtube:26-27`
    and `PC:327` say none).
16. `allThreadsRelatedToChannelId` leaves out comments on posts (`youtube:39`, `PC:329` say all).
17. YouTube view counts include co-views from 2026-11-16, and paid product placement must now be set to false
    explicitly on upload.
18. A YouTube comment reply's cost is given as 50 units and as 2 (`youtube:43` says 50).
19. Google Business Profile: product posts cannot be created by API; chat and Business Messages ended 2024-07-31;
    `BUSINESS_CONVERSATIONS` is deprecated; deleting a review reply needs a verified location; daily quotas exist
    for creating and updating locations (none of these is in `gbp.md`).

TikTok and Pinterest:
20. TikTok lists 25 scopes, not 23 (`tiktok:14`, `PC:353`), including `video.manage` for editing and deleting.
21. TikTok direct messages are now confirmed from the docs (`tiktok:107`, `tiktok:209` had only search snippets).
22. Pinterest's Pin edit is in beta (`pinterest:40` has no beta note), and the guide drops organic carousels
    (`pinterest:35`, `PC:394`).
23. Pinterest's spec is 5.30.0 (`pinterest:9` cites 5.28.0); the new versions add only a merchant status call.
