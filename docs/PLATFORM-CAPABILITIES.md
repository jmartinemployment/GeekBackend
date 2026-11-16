# GeekSocial platform capabilities (Phase 0 gate)

Written 2026-11-08 from the official Meta and LinkedIn documentation; Instagram (section 2d) was added
2026-11-09 from Meta's Instagram Platform pages. Nothing here was run against a
live app: no Meta app, LinkedIn app or Community Management API access exists yet, so every row is
"documented", not "proven". Proof is Phase 6.

**Gate 7a (2026-11-16)** extends this document to Google Business Profile, X, YouTube, TikTok and
Pinterest (sections 2e to 2i), settles the Instagram open items and adds media rules for every platform
(2j), and compares them side by side (2k). The full findings, with verbatim quotes and every URL opened,
are in `docs/platform-research-2026-11-16/` (one file per platform area); the tables here are condensed
from those files and name the file to read for the quote. GeekSocial is now a multi-tenant product, so the
earlier "own accounts only" assumptions are corrected where they appear (2j, 4b). Two things limit the
evidence: nothing was run against a live app, and some Google, TikTok, Pinterest and X pages are rendered by
JavaScript. Those were first read through a summarizer, then re-read in a real browser (Playwright, 2026-11-16);
the page text is kept in `docs/platform-research-2026-11-16/rendered/` and the rows it settled are marked
"re-read in a browser". Still unread: TikTok's Content Posting audit form (it needs a login) and the
YouTube "supported file formats" page.

Legend: **CONFIRMED** = stated in the official page cited. **UNVERIFIED** = the docs are silent or
conflict; it must be settled in the developer portal or by a live call. **BLOCKED** = unsupported,
closed, or unusable without an approval or a ruling we do not have. Nothing BLOCKED is implemented
around: no scraping, no session cookies, no unofficial APIs.

## 1. What needs Jeff's decision at this gate

1. **LinkedIn member comment text sent to the Claude API (compliance, BLOCKED until ruled).**
   LinkedIn's restricted-uses page says member data "can only be displayed via your application" and
   "can't be exported, distributed, or otherwise transferred from your application", and that it may be
   used only to manage Pages or Profiles. It says nothing about AI. Sending a commenter's text to a
   third-party model API is a transfer on the plain reading. Until LinkedIn answers in writing (Developer
   Support Portal), the LinkedIn path cannot draft replies from comment text. Options: (a) ask LinkedIn;
   (b) LinkedIn comments appear in the inbox and Jeff writes the reply by hand; (c) draft only from
   organization-authored text, never member text. No silent fallback to any of these.
2. **LinkedIn retention is 48 hours for member comments and 24 hours for commenter profile data**
   (section 5). The approval inbox cannot hold a LinkedIn comment's text longer than that, and a
   pending draft older than 48 hours loses its source text. This shapes the Phase 2 schema and needs a
   deadline-based purge that runs well inside 48 hours.
3. **LinkedInMember is publish-only.** `r_member_social` is closed: LinkedIn is not accepting requests.
   Personal-profile comments cannot be read, so there is nothing to reply to or react to.
4. **Facebook reactions are LIKE only.** A Page can like a comment; the reactions edge is read-only.
5. **Native LinkedIn Articles and Newsletters cannot be published by API.** Link posts (article cards)
   can.
6. **Instagram is a fourth platform (Jeff, 2026-11-09).** It publishes one hosted image, video, reel,
   carousel or story: there are no link or text-only posts documented, so a post that points at a blog
   article needs an image, and a link in a caption is not clickable. It can read, reply to and hide
   comments. No reaction to a comment is documented, so the agent does not react on Instagram. Section
   2d.

## 1a. Standing product facts and the question rule (Jeff, 2026-11-16)

These are fixed. Anything written for GeekSocial, including a question to a platform, starts from them.

1. **Tenant ownership.** Each customer (tenant) connects their own social or Google Business Profile accounts
   through OAuth. The tenant is the owner or administrator of those accounts.
2. **A human approves every reply and post.** The product drafts AI-written replies to incoming comments and
   reviews; a person (the business owner or their staff) must review and approve each one before it is posted. It
   is not an autonomous or unattended posting bot.
3. **A 30-day window.** Triage of a comment or review happens within 30 days of receiving it. Incoming reviews and
   comments are stored in our own database only to power an instant approval inbox (the platform's API is never
   queried live on page load). Content older than 30 days is not needed and is purged on a rolling 30-day
   schedule. Where a platform allows less, the shorter limit applies (LinkedIn: 48 hours for member comments, 24
   hours for commenter profile data).
4. **No external data extraction.** Content from a platform API is used only inside the product, for the connected
   tenant's own reply workflow: never exported, resold, aggregated across tenants, or used to train AI models.

**The question rule.** Ask a platform only a question that is genuinely ambiguous and applies to a single-tenant,
human-approved inbox inside a 30-day window. Do not ask about unattended-bot consent, archiving beyond 30 days,
resale of data or multi-account scraping. Keep each question short and tied to the exact policy text.

**Under fact 3 the original comment or review is kept for up to 30 days, and nothing is kept beyond that** (Jeff,
2026-11-16). The original is kept so the person approving a reply can see exactly what the AI is answering. The
text, the reply drafts and the rows that point to them are deleted whole when the item is 30 days old (LinkedIn
member comment text at 48 hours). **A draft never outlives the text it answers:** when a comment's text is
purged its drafts go with it, so nobody is asked to approve a reply without seeing its source. Everything a send needs to be safe (the one-answer-per-comment guard, the uncertain-outcome hold,
the 30-replies-an-hour limit and the 15-minute cooldown) works on hours, not months. A resync must read only
items inside the 30-day window, so a purged item cannot be fetched and drafted again.

## 1b. Gate 7a findings that need Jeff's decision

1. **No autonomous sending. DECIDED (Jeff, 2026-11-16, replacing the earlier opt-in decision):** care must be
   given to prevent AI hallucinations, spam and similar content from being posted, so nothing is sent to any
   platform without a person approving that individual reply or post. There is no auto-send. Consequences: the
   platform consent rules for automation (Google and YouTube "prior specific and express consent", X's express
   consent for automated actions, Pinterest's "each action considered") are not triggered by a person-approved
   send, so no consent question is put to any platform and no per-category consent record is needed. The
   tenant's one-time `ai_consent_accepted_at` stays, because it covers sending comment text to the AI (item 4).
   **Still open, because it is about the app and not about consent:** X's rule that an app which "generates and
   posts replies" needs X's prior approval, which may apply even when a person approves each reply (item 3).
   **What this changes in what is already built (no code is changed at Gate 7a):** `AutoSendCategories` on
   `SocialAccount`, the auto-send step in the Orchestrator, and the auto-send settings on the Accounts page are
   to be removed in a later gate, and the Sender must refuse any send whose draft was not approved by a person.
2. **Google's content-storage rule. DECIDED by design (Jeff, 2026-11-16): no question is put to Google.** Google's
   policy ("Content storage", re-read in a browser) restricts storing Content "for use outside of your Business
   Profile project" and caching "to improve the performance of your project", and says what is stored "must be
   stored temporarily for no more than 30 calendar days", "securely", and "cannot be manipulated or aggregated in
   any way". Standing fact 3 keeps every stored review inside 30 days and standing fact 4 forbids aggregation, so
   the product meets the rule on either reading and nothing applicable to this workflow remains ambiguous.
   YouTube (refresh or delete stored data within 30 days) and Pinterest (delete within 30 days) are met by the
   same window. Still true and separate: a tool provider "cannot develop your own API that allows your clients to
   access the Business Profile APIs through automatic or programmatic computer scripts", so tenants use our screen,
   never an API of ours against Google's.
3. **X. DECIDED (Jeff, 2026-11-16): X is built last, and X is not asked anything, because X offers no way to
   contact it.** What Jeff reports X staff said on X Developer Community threads (the threads were not read or
   cited here, so the URLs are still needed): there is no approval form, portal or ticket queue for AI reply
   apps; and when replies follow X's rules (only when the other user initiates by mention, reply or quote, one
   reply per interaction, through the official API, with human supervision and no unsolicited outreach) no extra
   written approval is needed. Human approval before posting makes it human-assisted publishing, not an
   autonomous bot. This answers the Developer Guidelines line "AI-powered app generates and posts replies:
   Requires prior approval from X" as Jeff reads it. **Conditions the product must meet** (X's Automation Rules,
   read in a browser): the replied-to user opted in by replying to or mentioning the account, an easy opt-out is
   offered and honoured, one automated reply per interaction, and the reply is to the user's original post. A
   commenter's reply to a tenant's post carries an @mention of the tenant, which is X's own definition of
   "summoned" for the API reply rule; this is treated as satisfied and is proven only by a live test (Phase 6).
   **Still not known, and no one can be asked:** how many customers a self-serve app may have before Enterprise
   ("a limited number of end-users"). It will show up in X's billing and access limits when X is built.
4. **Sending a tenant's content to Claude. DECIDED (Jeff, 2026-11-16, "agreed").** The AI sees a tenant's content only
   for that tenant, and only after that tenant has accepted the AI terms (`tenants.ai_consent_accepted_at`). It is
   never mixed with another tenant's content, it is kept 30 days at most (section 1a), and it is never used to train
   a model. Without that acceptance the AI sees nothing and a person writes the replies by hand. No per-platform
   ruling is asked of any platform. Meta's condition (an AI provider must agree in writing to use the data "solely
   for you and at your direction") is met by Anthropic's agreement and must be checked before Meta content is
   sent; until then `GEEK_SOCIAL_CLAUDE_CLEARED_PLATFORMS` stays empty for every platform and each comment is held
   (`blocked_no_clearance`).
5. **Status checks that Meta, Pinterest and TikTok document as polling.** Instagram container status
   ("once a minute for up to 5 minutes"), Pinterest video media status and TikTok publish status are polls.
   Proposal: use a platform webhook where one exists (TikTok has `post.publish.*` events); otherwise check
   when a person acts or when the screen's hub connection is established, and show "not confirmed yet" until
   then. No timer.
6. **YouTube: not supported (Jeff, 2026-11-16).** The quota question, and the tenant-owned Cloud project decision recorded
   earlier on this item, are moot. The research stays in section 2g and the appendix in case this changes.
7. **Pinterest: not supported (Jeff, 2026-11-16).** It can only publish, which is not what the product is for.
   **TikTok: DECIDED (Jeff, 2026-11-16): obtaining TikTok access is the tenant's responsibility.** Comments exist
   only in the Business API's Accounts API, behind an access application form required since 2026-03-20; the
   tenant, not GeekSocial, registers a TikTok developer app and obtains that access. **Notes, mine and not
   decided:** TikTok developer registration is company-only; a webhook's signature is keyed with the app secret, so
   each tenant's app secret must be stored encrypted and per tenant, and the tenant's app must be pointed at our
   callback; whether a tenant-owned app is allowed for a product run by another developer was not read.
   (YouTube is not supported; see item 6.)
8. **Selling this needs platform approvals that cannot start in code**: Meta Advanced Access (App Review,
   Business Verification, Live mode), LinkedIn Standard tier, Google's Business Profile access form, X's
   enterprise conversation, YouTube's audit and quota extension, TikTok's registration and access form,
   Pinterest Standard access. See section 7.

## 2. Matrix

### 2a. Meta Page (Graph API v27.0)

| Action | Endpoint | Permission / token | Tier | Status |
|---|---|---|---|---|
| Publish a link post | `POST /{page-id}/feed` with `link`, `message`; returns `{id}` | `pages_manage_posts` (depends on `pages_read_engagement`, `pages_show_list`); Page access token from a person with CREATE_CONTENT, MANAGE, MODERATE | Standard Access | CONFIRMED |
| Schedule a post | same, `published=false` + `scheduled_publish_time` (UNIX, ISO 8601 or strtotime), 10 minutes to 30 days ahead | same | Standard | CONFIRMED |
| Read comments | `GET /{post-id}/comments` (`filter=stream` returns all levels; default `toplevel`) | Same permissions as reading the parent; Page token. A user token returns empty data. Comment `id` needs the MODERATE task | Standard | CONFIRMED (the page names no permission; see open item M2) |
| Read commenter text | `message`, `from` on the comment | `pages_read_user_content` ("read user generated content on the Page, such as posts, comments") | Standard | UNVERIFIED whether reading works without it; request it |
| Reply to a comment as the Page | `POST /{comment-id}/comments` with `message`; the Page is the author; returns `{id}` | Page token with MODERATE, plus `pages_read_engagement` and `pages_manage_engagement` | Standard | CONFIRMED in the Pages API "Comments and @mentions" guide; the Graph reference page for `/{comment-id}/comments` says create is not supported on that endpoint. Conflict, so open item M1 |
| React to a comment | `POST /{comment-id}/likes`, no fields | `pages_manage_engagement`; Page token with CREATE_CONTENT; object not already liked | Standard | CONFIRMED, LIKE only |
| React with LOVE, WOW, HAHA, SORRY, ANGRY | none: `/reactions` says creating is not supported | n/a | n/a | BLOCKED |
| Webhook: comments, reactions, posts | `POST /{page-id}/subscribed_apps?subscribed_fields=feed`; field `feed` | `pages_manage_metadata` + `pages_show_list`; Page token from MODERATE/CREATE_CONTENT/MANAGE | Standard | CONFIRMED; delivery in development mode is UNVERIFIED (M3) |
| Personal profile | none | n/a | n/a | BLOCKED (no API; out of scope) |

`feed` payload: `item` includes comment, post, reaction, share, status, photo and others; `verb`
includes add, edit, edited, delete, remove, hide, unhide, update. A comment carries `comment_id`,
`parent_id`, `post_id`, `from` (id, name), `message`, `created_time`; a reaction carries
`reaction_type`. The full item/verb pairing for a Page's own events is not tabulated in the docs.

### 2b. LinkedIn organization (header `Linkedin-Version: 202610`, `X-Restli-Protocol-Version: 2.0.0`)

| Action | Endpoint | Permission / role | Tier | Status |
|---|---|---|---|---|
| Publish an article (link) post | `POST /rest/posts`, `content.article` = `source`, `title`, `description`, `thumbnail` (image URN from the Images API); `lifecycleState: PUBLISHED`; 201 with the post URN in header `x-restli-id` | `w_organization_social`; role ADMINISTRATOR, DIRECT_SPONSORED_CONTENT_POSTER or CONTENT_ADMIN | Development tier | CONFIRMED. The API does not scrape URLs, so the title, description and thumbnail must be supplied |
| Native long-form Article or Newsletter | none in the Posts API content-type table | n/a | n/a | BLOCKED |
| Read comments | `GET /rest/socialActions/{postUrn}/comments`; replies via `.../{commentUrn}/comments` | `r_organization_social` per the Posts API page; `r_organization_social_feed` per the Comments and Reactions pages | Development | CONFIRMED endpoint; scope name UNVERIFIED (L1) |
| Reply to a comment | `POST /rest/socialActions/{postUrn or commentUrn}/comments` body `actor`, `object`, `message`, `parentComment`; id in `x-restli-id` | `w_organization_social` (or `_feed`); org role | Development | CONFIRMED. A creation throttle returns 429 ("1 minute" window) |
| React to a post or a comment | `POST /rest/reactions?actor={orgUrn}` body `root` (post or `urn:li:comment:(...)`), `reactionType`; 201 | `w_organization_social` (or `_feed`) | Development | CONFIRMED. Types: LIKE, PRAISE, EMPATHY, INTEREST, APPRECIATION, ENTERTAINMENT. `MAYBE` returns 400 |
| Webhook: comments, likes, shares, mentions | `PUT /rest/eventSubscriptions/(developerApplication:...,user:...,entity:urn:li:organization:...,eventType:ORGANIZATION_SOCIAL_ACTION_NOTIFICATIONS)` with `webhook` URL | `rw_organization_admin`, authorized by an org ADMINISTRATOR; the Webhooks tab exists only for apps with an approved webhooks use case (the Employee Advocacy use case lists these notifications) | Development | CONFIRMED; approval needed (L2) |
| Gap recovery | `GET /rest/organizationalEntityNotifications?q=criteria&actions=...&organizationalEntity=...` | same | Development | CONFIRMED; 60-day retention; LinkedIn recommends it only for bootstrap and error handling |

Webhook notification: batches of 10, URNs only, `action` is one of LIKE, COMMENT, SHARE,
SHARE_MENTION, ADMIN_COMMENT, COMMENT_EDIT, COMMENT_DELETE, `notificationId` is the dedupe key.
Events fire only for posts with `visibility: PUBLIC`. Redelivery is every 5 minutes for 8 hours.
A subscription ends when the subscribing member's grant expires or they stop being an administrator.

### 2c. LinkedIn member (personal profile)

| Action | Endpoint | Permission | Status |
|---|---|---|---|
| Publish a post | `POST /rest/posts`, `author: urn:li:person:{id}` | `w_member_social` | CONFIRMED endpoint. Which product grants the scope is UNVERIFIED (L3): the Community Management overview lists "Profile Management" (posts, comments, reactions for individual profiles) as a use case |
| Read comments or reactions on own posts | `socialActions/.../comments` | `r_member_social` | BLOCKED: "a closed permission. We're not accepting access requests" |
| Reply, react | same endpoints as the organization, `actor` = person URN | `w_member_social` | Technically callable, but unusable: nothing can read the comments to answer |
| Webhook or notification | none exists for members | n/a | BLOCKED |
| Polling fallback | none possible without read access | n/a | BLOCKED, so none is built |

### 2d. Instagram (professional account; Meta Instagram Platform pages read 2026-11-09)

An Instagram **professional** account (Business or Creator) is required; a personal account cannot use
the API. Meta documents three setups for the same account:

| Setup | Needs | Host and token | Notes |
|---|---|---|---|
| Unified Login (Meta: "recommended for new integrations") | A business portfolio; Facebook Login for Business; no Facebook Page | `graph.facebook.com`; a Business Integration System User access token (SUAT) owned by the portfolio | Expiry is set when the login configuration is created; the Business Integration System User Access Token Management API manages it. The token stops working if the app leaves the portfolio or the account moves to another portfolio |
| Instagram Login | No Page, no portfolio | `graph.instagram.com`; an Instagram User access token | `instagram_business_*` permissions; no hashtag search, product tagging or partnership ads |
| Facebook Login | A Facebook Page linked to the account | `graph.facebook.com`; a Page access token | Hashtag search, product tagging, partnership ads |

| Action | Endpoint | Permission | Tier | Status |
|---|---|---|---|---|
| Publish an image, video, reel, carousel or story | `POST /<IG_ID>/media` (container; `image_url` or `video_url`, `caption`, `media_type`), then `POST /<IG_ID>/media_publish` with `creation_id`; carousels hold up to 20 items; images must be JPEG; the media must be on a publicly reachable server | Instagram Login: `instagram_business_basic` + `instagram_business_content_publish`. Unified Login: `instagram_basic` + `instagram_content_publish`. Facebook Login adds `pages_read_engagement` | Standard (own account) | CONFIRMED |
| Publish a link post or a text-only post | none documented | n/a | n/a | BLOCKED |
| Schedule a post | none documented | n/a | n/a | BLOCKED |
| Publishing limit | `GET /<IG_ID>/content_publishing_limit` returns the live figure | n/a | n/a | UNVERIFIED: one page says 100 API-published posts per 24 hours, another says 50 (I2) |
| Read, reply to and hide comments | the comments edges of the media object | `instagram_business_manage_comments` ("comment reading, moderation, and replies") | Standard | CONFIRMED permission; the endpoints were not read (I4) |
| React to a comment | none documented | n/a | n/a | BLOCKED |
| Webhook: comments, mentions | subscription on the app; payloads must be signature-validated | field availability depends on the setup; `/documentation/instagram-platform/webhooks/fields` lists them | Standard | UNVERIFIED: fields not read, delivery in development mode not known (I3) |

Instagram Login tokens: the authorization code lasts 1 hour and is single use; it is exchanged for a
short-lived token (1 hour), then for a long-lived token (60 days). A long-lived token refreshes at
`graph.instagram.com/refresh_access_token` once it is 24 hours old and not expired; each refresh gives
another 60 days, and a token not refreshed within 60 days cannot be refreshed. Exchanging and refreshing
happen on the server only.

**Access tier.** Standard Access, the default, serves accounts the app owner owns or manages and has
added to the app in the dashboard. Advanced Access (App Review and Business Verification) is for
accounts the owner does not control. The agent uses Standard Access only; no App Review submission is
made. (Meta's review checklist also lists a Marketing API Access Tier item. It is for ad accounts, needs
500 test calls and is unrelated to this agent.)

**Setup as Jeff reported it, 2026-11-09:** the Meta app `geekatyourspot-app` is connected to the
Geek At Your Spot LLC business portfolio, has Facebook Login for Business, and Instagram is reported
connected. Which of the three setups the dashboard used is not known and is settled when the connect flow
is built (I1).

### 2e. Google Business Profile (read 2026-11-16; findings in `platform-research-2026-11-16/gbp.md`)

APIs: Account Management, Business Information and Notifications are v1. **Reviews, Local Posts and Media
exist only on the v4 `mybusiness.googleapis.com`**; they are not on Google's deprecation schedule and gained
fields as late as 2026-07. The Q&A API and its notification types were discontinued 2025-11-03.

| Action | Endpoint | Scope | Status |
|---|---|---|---|
| List the accounts and locations a user manages | Account Management v1 (accounts), Business Information v1 (locations) | `business.manage` | CONFIRMED |
| Read reviews | `GET v4 accounts/{a}/locations/{l}/reviews`, 50 per page; reviewer name, `starRating` (an enum), `comment`, `createTime`, `updateTime` | `business.manage` | CONFIRMED; re-read in a browser: "This operation is only valid if the specified location is verified." and "The maximum pageSize is 50." |
| Reply to a review | `PUT .../reviews/{id}/reply` with `{"comment": ...}`, at most 4096 bytes | `business.manage` | CONFIRMED. Google moderates a reply for up to 30 days, so a 200 does not mean it is published |
| Create a local post | `POST .../localPosts`: STANDARD, EVENT and OFFER work; ALERT is effectively closed; call to action BOOK, ORDER, SHOP, LEARN_MORE, SIGN_UP or CALL, and the only link slot is `callToAction.url` | `business.manage` | CONFIRMED; a link-preview card is not documented; summary length UNVERIFIED |
| React to a review | none documented | n/a | BLOCKED |
| New reviews without polling | Cloud Pub/Sub, see below | n/a | CONFIRMED |

**Push.** One Pub/Sub topic; the service account `mybusiness-api-pubsub@system.gserviceaccount.com` needs
Publish on it; then `PATCH mybusinessnotifications.googleapis.com/v1/accounts/{id}/notificationSetting` once per
customer account. Types: NEW_REVIEW, UPDATED_REVIEW, GOOGLE_UPDATE, NEW_CUSTOMER_MEDIA. The documented payload
names the review and location (`review_name`, `location_name`) and carries no text, so a `reviews.get` follows.
A push subscription to an HTTPS endpoint is supported and authenticated by a Google-signed OIDC JWT. Delivery
is at least once, so dedupe. No sample message or latency is documented. On demand: a person-triggered
`reviews.list`.

**Media.** A post's image is attached by a public URL only. A location photo takes a public URL or a binary
upload (`media:startUpload`, then POST to `/upload/v1/media/{resourceName}`, then `media.create`). JPG or PNG,
10 KB to 5 MB, 250 px minimum on the short edge, 720 px recommended. Maximum pixels, aspect ratio and video by
API are UNVERIFIED.

**Access.** Approval is per Cloud project through the "Application for Basic API Access" form, which asks for a
verified profile active for 60 or more days with a website. Default quota is 300 QPM (0 means not approved). No
review time is stated. One project may serve every customer; customers cannot be required to apply themselves.

### 2f. X, API v2 (read 2026-11-16; `platform-research-2026-11-16/x.md`)

Pricing is pay-per-use credits (launched 2026-02-06 per the changelog). Reading a post is $0.005 (up to 3M
post reads per billing cycle); writing is $0.015, $0.20 if the post carries a URL, $0.01 for a "summoned" reply.
The current pricing page, re-read in a browser, says "The X API uses pay-per-usage pricing with no subscriptions"
and lists only free credits, pay-per-usage and Enterprise, so no Basic or Pro price exists on it (developer.x.com
returned HTTP 402 to a plain request and 404 in a browser). Enterprise is custom.

| Action | Endpoint | Scope | Status |
|---|---|---|---|
| Post, reply, thread | `POST /2/tweets`; a reply sets `reply.in_reply_to_tweet_id` | `tweet.read tweet.write users.read` | CONFIRMED; threads inferred |
| **Reply by a self-serve app** | same | same | **BLOCKED unless the original author summoned the replier** (an @mention or a quote). Whether a commenter's direct reply counts is UNVERIFIED and needs a live test |
| **AI-written replies** | same | same | **BLOCKED until X approves**: "AI-powered app generates and posts replies: Requires prior approval from X" |
| Read mentions | `GET /2/users/:id/mentions` with `since_id`, at most 800 recent, 300 per 15 minutes per user | `tweet.read users.read` | CONFIRMED. A customer's mentions cost $0.005 each; $0.001 "owned read" needs the user to own the developer app |
| Mentions and replies without polling | X Activity API: `POST /2/activity/subscriptions` with `post.mention.create` or `post.reply.create` to a webhook; 1,500 subscriptions on self-serve | user OAuth 2.0 token | CONFIRMED to exist on self-serve; inclusion in pay-per-use is inferred (no sentence says it). Filtered-stream webhooks are Enterprise-only; the Account Activity API is deprecating |
| React to a post | not read | n/a | UNVERIFIED |

**Media.** Binary or base64 only: no URL or signed-URL source. JPG, PNG, GIF or WEBP; images up to 5 MB, GIFs up
to 15 MB; video by chunked upload (default 20 minutes and 8 GB, Premium 125 minutes and 16 GB). A post takes 4
photos, or 1 GIF, or 1 video. Alt text is `POST /2/media/metadata`, up to 1000 characters. Scope `media.write`.

**OAuth 2.0 with PKCE.** Scopes `tweet.read tweet.write users.read media.write offline.access`. Access token 2
hours; refresh tokens are single use and last about 6 months (stated on the OAuth 1.0a migration page).

**Terms.** The Developer Agreement, Restricted Uses and Guidelines forbid using X content to train or fine-tune
a model. None mentions inference, so sending a reply's text to Claude to draft a response is UNVERIFIED. X's
Automation Rules (help.x.com, re-read in a browser) allow an automated reply to a user only if the user has
asked or clearly indicated they want to be contacted ("for example by replying to a post from your account"),
the account offers an easy opt-out and honours it, sends one automated reply per interaction, and replies to the
user's original post; keyword-search replies are not permitted. They also say OAuth authorization alone is not
consent to automated actions through a user's account (section 1b, item 1). Self-serve plans suit "a
limited number of end-users"; the number is not stated.

### 2g. YouTube (Data API v3; read 2026-11-16; `platform-research-2026-11-16/youtube.md`)

**NOT SUPPORTED (Jeff, 2026-11-16: "We do not support YouTube").** This section is kept as research only and no YouTube
connector will be built. It overrides the earlier Gate 7a list that included YouTube.

| Action | Endpoint | Scope | Quota | Status |
|---|---|---|---|---|
| Publish a video or a Short | `videos.insert`, resumable binary upload | `youtube.upload` or `youtube.force-ssl` | own bucket of 100 calls a day | CONFIRMED. No URL upload (BLOCKED) |
| Publish a Community post | none | n/a | n/a | BLOCKED (checked against the full resource list) |
| Set a thumbnail | `thumbnails.set` | `youtube.force-ssl` | 50 | CONFIRMED; JPEG or PNG up to 50 MB |
| Read comments | `commentThreads.list` (up to 100 threads; `allThreadsRelatedToChannelId` covers the whole channel), `comments.list` | `youtube.force-ssl` | 1 | CONFIRMED; the two reference pages name no scope (UNVERIFIED) |
| Reply to a comment | `comments.insert`, to a top-level comment only | `youtube.force-ssl` | 50 | CONFIRMED |
| Hold, publish or reject a comment | `comments.setModerationStatus`, `banAuthor` | `youtube.force-ssl`, channel owner | 50 | CONFIRMED |
| Like or heart a comment | none | n/a | n/a | BLOCKED |
| New comments without polling | none: the only push (PubSubHubbub) is for uploads and title or description edits | n/a | n/a | BLOCKED; sync is on demand, 1 unit for one channel-wide call |

Use `textDisplay` for other people's comments: `textOriginal` is returned only to the author. **Quota changed on
2026-06-01:** 10,000 units a day per Google Cloud project, shared by all customers, sharding across projects
prohibited. The "uploads from unverified projects are locked private" rule was reversed on 2026-11-02; an audit
is still needed for more quota. Policy: automating comments needs "prior specific and express consent" (section
1b); stored authorized data must be deleted or refreshed within 30 days, a deletion request has a 7-day
deadline, derived data is prohibited, and the policies say nothing about AI (UNVERIFIED). OAuth: a consent
screen in "Testing" expires refresh tokens in 7 days; sensitive-scope verification can take up to 10 days;
whether the scopes are "restricted" (a security assessment) is UNVERIFIED. Re-read in a browser (CONFIRMED): "By
default, you can upload videos that are up to 15 minutes long. Verified accounts can upload videos longer than 15
minutes"; "The maximum file size you can upload is 256 GB or 12 hours, whichever is less"; Shorts are "up to 3
minutes long" (the "1 to 3 minutes" wording on another page is how vertical videos are classified as Shorts since
2024-10-15); and a custom thumbnail can be uploaded only "if your account is verified", so `thumbnails.set` needs
a verified channel. YouTube's recommended container is MP4 (H.264, AAC); the "supported file formats" page was
not read.

### 2h. TikTok (read 2026-11-16; `platform-research-2026-11-16/tiktok.md`)

Two products. **The Business API (Organic, "Accounts API")** has comments and publishing for business accounts.
**developers.tiktok.com** (Login Kit, Content Posting API) has no comment scope among its 23 scopes.

| Action | Endpoint | Permission | Status |
|---|---|---|---|
| Read comments and replies | `GET /business/comment/list/`, `/business/comment/reply/list/` | 18030100 (`comment.list`) | CONFIRMED |
| Reply, like, hide, delete | `POST /business/comment/reply/create/` and the matching create, like, hide and delete endpoints; reply text up to 1,200 characters | 18030200 | CONFIRMED; the write scope name `comment.list.manage` appears only in an example (UNVERIFIED) |
| New comments without polling | webhook `comment.update`, subscribed with `POST /business/webhook/update/` (`event_type` COMMENT); "within five minutes"; covers manually posted videos; carries `comment_id`, `video_id`, `comment_action`, `text`, `unique_identifier` | needs `comment.list` | CONFIRMED. Stops if the authorization expires or is revoked |
| Publish a video or photos (Business API) | `/business/video/publish/`, `/business/photo/publish/`: a verified-domain URL only, drafts with `upload_to_draft` | publish permission | CONFIRMED. 6 a minute and 15 a day per account; video 1 GB and 3 to 600 seconds; photos JPG, JPEG or WEBP, up to 35 at 20 MB each |
| Publish (Content Posting API) | Direct Post: `FILE_UPLOAD` or `PULL_FROM_URL` (verified domain) | `video.publish`, `video.upload` | CONFIRMED. **Private-only until the client is audited**; unaudited clients are capped at 5 users per 24 hours. The audit form and review time were not found |
| Publish status without polling | `post.publish.*` webhook events on both products | n/a | CONFIRMED; the events page and the Content Posting status page disagree on the event list |

**Webhook signature.** `TikTok-Signature: t=,s=`, HMAC-SHA256 over `timestamp + "." + raw JSON body`, keyed with
the app secret. **Tokens.** Business API access token 1 day, refresh token 1 year, auth code 10 minutes, no PKCE
described; whether the refresh token rotates is UNVERIFIED. developers.tiktok.com web flow has no PKCE (desktop
and mobile require it): access 2 hours, refresh 180 days with rotation, though the App Review FAQ says "maximum
of one year". **Access.** Registration is company-only with a 3-business-day review; app review is 2 to 3 business
days on the Business side and "several days to two weeks" on developers.tiktok.com. **Since 2026-03-20 an
"Accounts API Access Application Form" is required** before any new app or scope increase that includes "TikTok
Accounts"; its criteria were not opened. **Re-read in a browser (CONFIRMED):** the Accounts API gate ("starting March 20, 2026 ... developers must complete
the Accounts API Access Application Form before submitting a new developer app or requesting a scope increase
that includes the 'TikTok Accounts' permission scope"), and the unaudited-client limits ("Unaudited API Clients
can allow up to 5 users to post in a 24 hour window", private-only and `SELF_ONLY` viewing). The Content Posting
guidelines also require API clients to let users disclose commercial content (off by default), and say clients
"must not be limited to test applications". The audit application form itself needs a login and was not read.
**Limits:** 40 queries a minute per account per endpoint and 600 a
minute per app. **Terms:** the Developer Terms say nothing about AI (UNVERIFIED); they limit data use to
"developing, maintaining, and supporting your Applications" and bar spam. Not settled by the docs: which account
types can authorize the comment scopes, whether US customers are supported, whether one verified domain can
serve many customers, and whether Business API posting is exempt from the audit rule.

### 2i. Pinterest, API v5 (read 2026-11-16; `platform-research-2026-11-16/pinterest.md`)

**NOT SUPPORTED (Jeff, 2026-11-16).** Pinterest is publish-only (confirmed twice: the OpenAPI spec lists no comments or
reactions operation, and the only webhook is for Lead Ads). This section is kept as research only and no Pinterest
connector will be built.

**Publish-only.** None of the 305 operations in Pinterest's OpenAPI spec (v5.28.0) is a comments or reactions
endpoint, and the only webhook is for Lead Ads.

| Action | Endpoint | Scope | Status |
|---|---|---|---|
| Create a Pin | `POST /v5/pins`; media source `image_url`, `image_base64`, `video_id` (cover required), `multiple_image_urls` or base64 (2 to 5 images); title up to 100, description up to 800, alt text up to 500, link up to 2048, `board_id`, `board_section_id` | `boards:read/write`, `pins:read/write` | CONFIRMED |
| Create a board | `POST /boards` (a board must exist first) | `boards:write` | CONFIRMED |
| Video | `POST /media`, upload to the returned S3 `upload_url`, check `GET /media/{id}` until it succeeds, then create the Pin | same | CONFIRMED; the status check is a poll (section 1b, item 5) |
| Read or reply to Pin comments, reactions | none | n/a | BLOCKED |
| New items without polling | none | n/a | BLOCKED; only person-triggered `GET /pins`, `/boards`, `/boards/{id}/pins` |

A Pin has an `ai_disclosures` object ("AI disclosure declarations the creator has made about the Pin"); whether
it is mandatory for AI-made organic Pins is UNVERIFIED. The reference page for Create Pin (re-read in a browser)
states no image size or format limits, so whether Pinterest fetches `image_url` as a public address, the API size
limit and the base64 size limit stay UNVERIFIED. The 20 MB and 32 MB images, 2 GB video and 2:3 ratio figures come from Pinterest's ads specification
help page, not the API reference. **Tokens:** access 30 days, refresh 60 days and renewable indefinitely; whether
the old refresh token is invalidated and whether PKCE is supported are UNVERIFIED. **Access:** Trial pins and
boards are visible only to their creator; Standard needs a video demo of the OAuth flow, and the Terms require
Pinterest approval before distribution to end users; no review time is published. **Personal accounts:** apps
registered on or after 2026-09-14 get `403 PINNER_DATA_ACCESS_DENIED` reading personal boards and Pins (the
clarification is a Pinterest staff post, not the docs); whether publishing is blocked too needs a live test.
**Limits:** `org_write` 300 a day per app on Trial, 100 a minute per user per app on Standard. **Terms:** do not
store API-accessed information without written consent, delete within 30 days, do not train or fine-tune AI/ML
on Pinterest Data; sending it to an LLM for drafting is UNVERIFIED.

### 2j. Corrections and additions to 2a to 2d for a multi-tenant product (`meta.md`, `linkedin.md`)

**Settled Meta items.**

| Item | Result | Status |
|---|---|---|
| I1 connect setup | **Unified Login**, which Meta recommends for new integrations and where new features ship first. No Facebook Page is needed for Unified Login or Instagram Login, but the customer must choose a business portfolio and our app must belong to a different one | CONFIRMED |
| I5 Page and Instagram together | one Login for Business token can hold Pages and Instagram accounts; the Instagram dashboard's configuration screen documents only Instagram and ad-account assets, so adding a Page there is UNVERIFIED | CONFIRMED / UNVERIFIED |
| System-user token (SUAT) | expiry is set per login configuration (default never, optional 60 days); created by exchanging the login code on the server, one per customer portfolio; it dies when the customer removes our app, signalled by a `business_integration_uninstall` webhook | CONFIRMED |
| I3 Instagram webhooks | fields `comments`, `live_comments`, `mentions`, all under `instagram_manage_comments`; subscribe each customer account with `POST /{IG_ID}/subscribed_apps`; `comments` payloads carry the text, `from`, `parent_id` and `media`, `mentions` payloads do not; delivery to customers needs Live mode and Advanced Access for `comments` and `live_comments`. Delivery in Development mode to a role-holding tester is UNVERIFIED | CONFIRMED / UNVERIFIED |
| I4 Instagram comments | read `GET /{media}/comments` (top level only, 50 a page); reply `POST /{comment}/replies?message=`; hide `POST /{comment}?hide=true`; delete `DELETE /{comment}`. The author shown on a reply is not stated | CONFIRMED / UNVERIFIED |
| I2 publishing limit | not settled: the guide says 100 in one section and 50 in its carousel section; the endpoint reference says `quota_total` is "currently 50". Plan with 50 and read `content_publishing_limit` per account | UNVERIFIED |

**Media rules, Meta and LinkedIn.**

| Platform | Binary or URL | Formats and limits | Status |
|---|---|---|---|
| Facebook Page photo | `POST /{page}/photos` with `url` or a multipart upload | jpeg, bmp, png, gif, tiff, up to 10 MB; several photos by unpublished uploads plus `attached_media`; video through the Video API and the Resumable Upload API | CONFIRMED |
| Instagram image | a public `image_url`; a signed or expiring URL is UNVERIFIED | JPEG only, up to 8 MB, aspect ratio 4:5 to 1.91:1; carousel up to 20 items | CONFIRMED |
| Instagram video | `video_url`, or resumable upload at `rupload.facebook.com` for Login for Business apps only | status check documented as once a minute for up to 5 minutes; no webhook alternative documented (section 1b, item 5) | CONFIRMED |
| LinkedIn image, video, document | binary only: `initializeUpload`, then a PUT of the bytes to `uploadUrl`; no page accepts a media URL | images JPG, GIF or PNG under 36,152,320 pixels (max size and aspect ratio UNVERIFIED); video MP4, 3 seconds to 30 minutes, 4 MB parts (docs conflict: 500 MB or 5 GB); documents 100 MB and 300 pages; multi-image post 2 to 20 images, organic only | CONFIRMED / conflicting |
| LinkedIn article thumbnail | uploaded through the Images API; the API does not scrape a URL | the Posts page says thumbnail, title and description are required; the article schema says only `source` and `title` are. How a post looks without a thumbnail is not documented | conflicting |

**Corrections.** (1) Section 2d's "Standard Access only" does not hold for a SaaS: customers without a role on
our app need Advanced Access through App Review, Business Verification and Live mode, plus Data Use Checkup
before going Live; Meta says App Review decisions come "within a week". The Data Protection Assessment is annual
with 60 days to complete once enrolled; whether we are enrolled is UNVERIFIED. Tech Provider Access
Verification was retired 2026-10-09. (2) LinkedIn webhooks need the Standard tier (L2). A Development-tier app has
no push and no BATCH_GET, so nothing arrives unless a person's action triggers a read. (3) The LinkedIn webhook
payload is not "URNs only": the sample carries decorated comment text. (4) LinkedIn version 202511 sunsets
2026-11-16; 202601 to 202610 sunset 2027-01-15 to 2027-10-15. Every call must send 202610 or later. (5) LinkedIn
comments and reactions need `*_organization_social_feed` scopes and posts need `*_organization_social`: request
both pairs (L1). (6) `w_member_social` is listed under Community Management, so a personal profile does not need
the Share on LinkedIn product, which the partner guide names as a blocker for the Community Management request
(L3); whether Development tier issues the scope is UNVERIFIED. (7) Refresh tokens last 365 days and refreshing
does not extend them; whether our app is given them is UNVERIFIED because the docs conflict (L4). (8) The
Standard tier needs a screen recording of the OAuth flow, posting and comment display, test credentials, a
privacy policy and data-storage compliance; a rejected app cannot reapply; no review time is stated for
Community Management; whether Development tier may connect other customers' Pages is UNVERIFIED; Standard
rate limits are published only in the Developer Portal. (9) LinkedIn also bars using individual profiles "to
manage multiple customers' accounts" (restricted-use-cases page), which a multi-tenant design must respect.
Meta requires each client's data to be kept separate, and an AI provider is a Service Provider that must first
agree in writing to use the data "solely for you and at your direction".

### 2k. Side by side

| Platform | Read incoming comments or reviews | New items without polling | Media in | Image formats | Reply | React | Auto-send policy |
|---|---|---|---|---|---|---|---|
| Facebook | yes | `feed` webhook (Live mode for customers) | URL or multipart | jpeg, bmp, png, gif, tiff, 10 MB | yes | LIKE only | no rule found |
| Instagram | yes | `comments`, `live_comments`, `mentions` webhooks | public URL; rupload for some apps | JPEG only, 8 MB | yes | none | no rule found |
| LinkedIn | organization only (member is closed) | Standard tier webhooks only | binary only | JPG, GIF, PNG | yes | yes | no rule found |
| Google Business Profile | reviews | Pub/Sub (IDs only, then a read) | public URL (posts); URL or binary (location photos) | JPG, PNG, 10 KB to 5 MB | yes (moderated up to 30 days) | none | n/a: no auto-send (1b, item 1) |
| X | mentions and replies | X Activity API webhook | binary or base64 | JPG, PNG, GIF, WEBP, 5 MB | **only if summoned; AI replies need approval** | UNVERIFIED | n/a: no auto-send; X approval of AI replies still open (1b, item 3) |
| YouTube | yes | **none** | binary only (video) | thumbnail JPEG or PNG | yes | none | n/a: no auto-send (1b, item 1) |
| TikTok | Business API only | `comment.update` webhook | verified-domain URL (Business), or file or URL (Content Posting) | JPG, JPEG, WEBP | yes | like (Business API) | no rule found |
| Pinterest | **none** | **none** | URL or base64 | not in the API reference | none | none | n/a: no auto-send |

No platform's auto-send rule applies: nothing is sent without a person's approval (section 1b, item 1).
Platforms with no push (YouTube, Pinterest, and LinkedIn below Standard) get one on-demand read when the
customer's hub connection is established, as approved. That read costs 1 quota unit on YouTube (shared by all
customers) and 1 call against a 100-a-day member limit on LinkedIn's Development tier. X and TikTok have a
push, so they need no on-demand read for new items; an X read would cost $0.005 per post returned.

## 3. Webhook security (all must be implemented as written)

| | Meta | LinkedIn |
|---|---|---|
| Handshake | `GET` with `hub.mode=subscribe`, `hub.verify_token`, `hub.challenge`; compare the token, answer with the challenge value | `GET ?challengeCode=<uuid>`; answer **within 3 seconds**, `200`, `application/json`: `{"challengeCode": ..., "challengeResponse": hex(HMACSHA256(challengeCode, clientSecret))}` |
| Re-validation | none documented | every 2 hours; 3 failures in a row block the endpoint (emails sent, portal shows Blocked) |
| Per-event signature | `X-Hub-Signature-256: sha256=<digest>` over the payload with the App Secret (the page does not say HMAC or raw body; treat as HMAC-SHA256 of the raw body and test it) | `X-LI-Signature` = hex `HMACSHA256("hmacsha256=" + raw body, clientSecret)`; the header holds only the digest; compare in constant time; use the body exactly as received |
| Response | `200` | any `2xx`; HTTPS only; ngrok unsupported |
| Retries | immediately, then over 36 hours; up to 1000 updates per batch | every 5 minutes for 8 hours |
| Dedupe | required (the docs say so) | `notificationId` |

### 3b. Webhook security for the platforms added at Gate 7a

| Platform | Verification |
|---|---|
| Google Business Profile | Pub/Sub push to an HTTPS endpoint, authenticated by a Google-signed OIDC JWT; at-least-once delivery, so dedupe |
| X | CRC reply and per-event signature are HMAC-SHA256, base64 with a `sha256=` prefix, keyed with the app's OAuth 2.0 client secret; header `X-Twitter-Webhooks-Signature-OAuth2`. The CRC cadence conflicts (30 minutes or hourly) and retry behaviour is undocumented beyond a 24-hour replay endpoint |
| TikTok | `TikTok-Signature: t=,s=`, HMAC-SHA256 over `timestamp + "." + raw JSON body`, keyed with the app secret. Business webhook retry rules are not documented |
| YouTube, Pinterest | no comment webhook exists |

## 4. Tokens and access tiers

| | Meta | LinkedIn |
|---|---|---|
| Token flow | User logs in, exchange for a long-lived user token with `fb_exchange_token` (about 60 days, server-side only), then `GET /{user-id}/accounts` returns each Page's own `access_token` | Authorization code flow, 3-legged |
| Lifetime | Long-lived Page token has no expiry but is invalidated under conditions | Access token 60 days. A programmatic refresh token (365 days, not extended by refreshing) is issued only when the app is authorized for them: UNVERIFIED for our app (L4); without it, re-consent every 60 days |
| Invalidation | Error 190: subcode 460 (logout or password change), 458 (authorization revoked), 463 (session expired), security events. Page role removal is not documented. Meta does not notify; check `GET /debug_token` (`is_valid`, `expires_at`) and handle 190 on any call | 401 means the grant is gone (re-consent); 403 means the member is no longer an administrator |
| Access tier | **Standard Access** covers users with a role on the app, which includes Jeff on his own Page; no App Review. **Advanced Access** (any user) needs App Review and Business Verification, not needed for one owned Page | **Development tier** first: 500 calls per app, 100 per member, no BATCH_GET; **Standard tier** needs a screen recording, test credentials and technical sign-off. A rejected app cannot reapply; a new app is required |
| Approval prerequisites | none for own Page | Registered legal organization, commercial use only; verified business email; legal name, registered address, website, privacy policy; a Page super admin must verify the app; no LinkedIn or Microsoft names or logos in the app; the app must be new and hold no other API product (the request button is otherwise grayed out) |

### 4b. Tokens and approvals for the platforms added at Gate 7a

| Platform | Access token | Refresh | Approval before other people's accounts |
|---|---|---|---|
| Google Business Profile | short-lived | until revoked, unused 6 months, or past 100 per client; 7 days if the consent screen is in Testing | "Application for Basic API Access" per Cloud project (verified profile active 60 or more days); sensitive-scope verification status of `business.manage` UNVERIFIED |
| X | 2 hours | single use, about 6 months | pay-per-use self-serve suits "a limited number of end-users"; AI replies need X's approval; a sales conversation |
| YouTube | per `expires_in` | 7 days in Testing; 100 per account per client; unused 6 months | sensitive-scope verification up to 10 days; compliance audit for more quota |
| TikTok | Business 1 day; web 2 hours | Business 1 year; web 180 days with rotation | company-only registration, 3 business days; app review 2 to 3 business days (Business) or up to 2 weeks (developers); Accounts API Access Application Form; Content Posting audit |
| Pinterest | 30 days | 60 days, renewable | Trial to Standard needs a video demo of the OAuth flow and Pinterest's approval to distribute |

## 5. Data retention and use obligations

LinkedIn (data storage requirements, doc version 2026-10; "the shortest duration applies"):
- Other members' social activity (their comments, mentions, reactions): **48 hours**.
- Other members' profile data (a commenter's name and headline): **24 hours** of caching, and not stored.
- IDs and URNs of organizations, posts and social actions: no limit.
- Organization's own social activity (our posts and replies): six weeks; six months for an authenticated organization.
- Member data may be shown only inside the application to people tied to that Page, may not be exported
  or transferred out of it, may not be combined with other data, and may not be used for advertising,
  sales or recruiting. Delete on a member's request.

Meta (Platform Terms, developer policies; check the current versions):
- Platform Data may go to a service provider only after that provider agrees in writing to process it
  solely for us and at our direction. Anthropic's commercial terms must be checked against that before
  Facebook comment text is sent to Claude (M4). Until a platform is named in `GEEK_SOCIAL_CLAUDE_CLEARED_PLATFORMS`
  (a comma list of platform names on GeekAPI), no comment text from it is sent and each comment is held
  as `blocked_no_clearance`; a name that is not a platform stops GeekAPI from starting.
- Delete user data on request or when the user no longer has an account; the Data Protection Assessment
  asks about deletion practices.

### 5b. Retention rules found at Gate 7a

| Platform | Rule | Effect |
|---|---|---|
| Google Business Profile | Content stored under the policy's exception "must be stored temporarily for no more than 30 calendar days" and "cannot be manipulated or aggregated" | met by the 30-day window and by no aggregation (section 1a and 1b, item 2) |
| YouTube | refresh or delete stored authorized data within 30 days; delete within 7 days of a request or revoked consent; no derived data; no cross-owner aggregation | per-tenant stores and a deletion path |
| Pinterest | no storing API-accessed information without written consent; delete within 30 days | applies to published-post records |
| Meta | each client's data kept separate; a deletion-callback URL receives a `signed_request` and returns `{url, confirmation_code}` | per-tenant isolation; build the callback |
| LinkedIn | 48 hours for member comments, 24 hours for commenter profile data (section 5) | unchanged |

## 6. Open items (settle in the developer portal or with a live call)

Gate 7a (2026-11-16) settled or narrowed several rows below: I1, I3, I4, I5 and L1 to L3, L6 (section 2j);
L2 is settled (webhooks need the Standard tier); I2, L4 and L5 stay open. The open items for the five new
platforms are listed at the end of each file in `platform-research-2026-11-16/`.

| ID | Question | Where it gets settled |
|---|---|---|
| M1 | Does `POST /{comment-id}/comments` create a reply as the Page? The Pages API guide says yes, the Graph reference says create is unsupported | one live reply on the test Page, before Phase 4 |
| M2 | Which permissions does reading comments actually need (`pages_read_engagement` vs `pages_read_user_content`)? | request both; test with each removed |
| M3 | Does the `feed` webhook deliver comment and reaction events while the app is in development mode with a role-holding admin? | subscribe the test Page and post a comment |
| M4 | Is sending Facebook comment text to the Claude API permitted under Meta's service-provider terms and Anthropic's agreement? | read both agreements; Jeff to confirm, then add `facebook` to `GEEK_SOCIAL_CLAUDE_CLEARED_PLATFORMS` on GeekAPI |
| M5 | Does removing a person from a Page role invalidate the Page token? | not documented; handle error 190 either way |
| L1 | Scope names: `r_organization_social` / `w_organization_social` vs `*_feed` | shown in the developer portal once the product is approved |
| L2 | Is the webhooks use case approved with Development tier, or only at Standard? | the access request form |
| L3 | Is `w_member_social` granted by Community Management (Profile Management) or only by Share on LinkedIn, which cannot share an app with Community Management? | the portal after the app is created |
| L4 | Does our app get programmatic refresh tokens? | the portal and the token response |
| L5 | May member comment text go to the Claude API? (section 1, item 1) | LinkedIn Developer Support Portal, in writing, then add `linkedin_organization` to `GEEK_SOCIAL_CLAUDE_CLEARED_PLATFORMS` on GeekAPI |
| L6 | Is the 100-calls-per-member limit per day, and does the planned traffic fit? | the portal rate-limit view |
| I1 | Which Instagram setup does the connect flow use, and does a Facebook Page have to be linked? The overview says Instagram Login and Unified Login need no Page; the publishing guide says the account must be connected to one | the Instagram product page in the app dashboard; one live publish |
| I2 | The daily API publishing limit: 100 or 50 posts in 24 hours? | `GET /<IG_ID>/content_publishing_limit` on the connected account |
| I3 | Which comment and mention webhook fields exist for the chosen setup, and do they fire in development mode? | the webhooks fields page; subscribe the account and comment on a post |
| I4 | The exact endpoints for reading, replying to and hiding a comment, and for a reply's author | the Instagram comments reference pages; one live reply |
| I5 | Can one Unified Login connection cover the Facebook Page and the Instagram account together, and how long does the SUAT last? | the login configuration page in the dashboard |
| I6 | Is sending Instagram comment text to the Claude API permitted under Meta's service-provider terms? Same question as M4 | read both agreements; Jeff to confirm, then add `instagram` to `GEEK_SOCIAL_CLAUDE_CLEARED_PLATFORMS` on GeekAPI |

## 7. Setup path Jeff must start (longest first)

1. LinkedIn: create a **new** app (no other products), associate it with the Geek @ Your Spot LLC
   company Page, have the Page super admin verify it, then file the Community Management Development tier
   request. List the use cases to be demonstrated: Page Management, Profile Management, Employee
   Advocacy (this carries the webhooks).
2. Meta: the app exists (2026-11-09), is connected to the Geek At Your Spot LLC business portfolio, has
   Facebook Login for Business, and Instagram is reported connected. Still to do: the Pages use case and
   the Facebook test Page. Keep the app in development mode; no App Review submission, Data Protection
   Assessment or Marketing API item is needed for the owner's own accounts.
3. Approvals that selling GeekSocial needs, none of which code can start (Gate 7a, longest first): LinkedIn
   Community Management Standard tier; Meta Advanced Access (App Review, Business Verification, Live mode,
   Data Use Checkup); Google's "Application for Basic API Access" for the Business Profile APIs, plus OAuth
   verification; TikTok company registration and the Accounts API Access Application Form; YouTube sensitive-scope
   verification and the compliance audit with a quota extension; Pinterest Standard access with a video demo;
   and a conversation with X sales about the AI-reply approval, the "summoned" reply rule and the end-user limit.
4. Both: have the privacy policy and business email ready; LinkedIn requires a valid privacy policy at
   Standard tier.

## Sources

Gate 7a (read 2026-11-16): every URL opened for Google Business Profile, X, YouTube, TikTok, Pinterest, Meta
and LinkedIn is listed at the end of its file in `docs/platform-research-2026-11-16/`, with what was read from it.
Pages marked "(fetch summary)" or "re-read" there were not read as raw text.

Read 2026-11-08 and 2026-11-09:

- LinkedIn Posts API: https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/posts-api
- LinkedIn Comments API: https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/comments-api
- LinkedIn Reactions API: https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/reactions-api
- LinkedIn Organization Social Action Notifications: https://learn.microsoft.com/en-us/linkedin/marketing/community-management/organizations/organization-social-action-notifications
- LinkedIn Webhooks: https://learn.microsoft.com/en-us/linkedin/shared/api-guide/webhook-validation
- LinkedIn Community Management overview: https://learn.microsoft.com/en-us/linkedin/marketing/community-management/community-management-overview
- LinkedIn Community Management App Review: https://learn.microsoft.com/en-us/linkedin/marketing/community-management-app-review
- LinkedIn Data Storage Requirements: https://learn.microsoft.com/en-us/linkedin/marketing/data-storage-requirements
- LinkedIn Restricted Uses: https://learn.microsoft.com/en-us/linkedin/marketing/restricted-use-cases
- LinkedIn refresh tokens: https://learn.microsoft.com/en-us/linkedin/shared/authentication/programmatic-refresh-tokens (seen through search results, not opened)
- Meta Pages API, posts: https://developers.facebook.com/docs/pages-api/posts
- Meta Pages API, comments and @mentions: https://developers.facebook.com/documentation/pages-api/comments-mentions
- Meta Graph reference, comments: https://developers.facebook.com/docs/graph-api/reference/object/comments/
- Meta Graph reference, comment replies: https://developers.facebook.com/docs/graph-api/reference/comment/comments/
- Meta Graph reference, likes: https://developers.facebook.com/docs/graph-api/reference/object/likes/
- Meta Graph reference, reactions: https://developers.facebook.com/docs/graph-api/reference/object/reactions/
- Meta webhooks, getting started: https://developers.facebook.com/docs/graph-api/webhooks/getting-started
- Meta webhooks for Pages: https://developers.facebook.com/docs/graph-api/webhooks/getting-started/webhooks-for-pages
- Meta webhooks reference, Page `feed`: https://developers.facebook.com/docs/graph-api/webhooks/reference/page/
- Meta long-lived tokens: https://developers.facebook.com/docs/facebook-login/guides/access-tokens/get-long-lived/
- Meta token debugging and errors: https://developers.facebook.com/docs/facebook-login/access-tokens/debugging-and-error-handling
- Meta permissions: https://developers.facebook.com/docs/permissions
- Meta access levels: https://developers.facebook.com/docs/graph-api/overview/access-levels
- Meta Platform Terms: https://developers.facebook.com/terms
- Instagram Platform overview (read 2026-11-09): https://developers.facebook.com/docs/instagram-platform/overview
- Instagram API with Instagram Login (read 2026-11-09): https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login
- Business Login for Instagram, tokens (read 2026-11-09): https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login/business-login
- Instagram content publishing (read 2026-11-09): https://developers.facebook.com/docs/instagram-platform/content-publishing
- Instagram webhooks overview (read 2026-11-09): https://developers.facebook.com/docs/instagram-platform/webhooks
- Instagram Unified Login authentication (read 2026-11-09): https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-unified-login/authentication
