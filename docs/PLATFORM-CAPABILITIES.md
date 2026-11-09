# GeekSocial platform capabilities (Phase 0 gate)

Written 2026-11-08 from the official Meta and LinkedIn documentation; Instagram (section 2d) was added
2026-11-09 from Meta's Instagram Platform pages. Nothing here was run against a
live app: no Meta app, LinkedIn app or Community Management API access exists yet, so every row is
"documented", not "proven". Proof is Phase 6.

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

## 3. Webhook security (all must be implemented as written)

| | Meta | LinkedIn |
|---|---|---|
| Handshake | `GET` with `hub.mode=subscribe`, `hub.verify_token`, `hub.challenge`; compare the token, answer with the challenge value | `GET ?challengeCode=<uuid>`; answer **within 3 seconds**, `200`, `application/json`: `{"challengeCode": ..., "challengeResponse": hex(HMACSHA256(challengeCode, clientSecret))}` |
| Re-validation | none documented | every 2 hours; 3 failures in a row block the endpoint (emails sent, portal shows Blocked) |
| Per-event signature | `X-Hub-Signature-256: sha256=<digest>` over the payload with the App Secret (the page does not say HMAC or raw body; treat as HMAC-SHA256 of the raw body and test it) | `X-LI-Signature` = hex `HMACSHA256("hmacsha256=" + raw body, clientSecret)`; the header holds only the digest; compare in constant time; use the body exactly as received |
| Response | `200` | any `2xx`; HTTPS only; ngrok unsupported |
| Retries | immediately, then over 36 hours; up to 1000 updates per batch | every 5 minutes for 8 hours |
| Dedupe | required (the docs say so) | `notificationId` |

## 4. Tokens and access tiers

| | Meta | LinkedIn |
|---|---|---|
| Token flow | User logs in, exchange for a long-lived user token with `fb_exchange_token` (about 60 days, server-side only), then `GET /{user-id}/accounts` returns each Page's own `access_token` | Authorization code flow, 3-legged |
| Lifetime | Long-lived Page token has no expiry but is invalidated under conditions | Access token 60 days. A programmatic refresh token (365 days, not extended by refreshing) is issued only when the app is authorized for them: UNVERIFIED for our app (L4); without it, re-consent every 60 days |
| Invalidation | Error 190: subcode 460 (logout or password change), 458 (authorization revoked), 463 (session expired), security events. Page role removal is not documented. Meta does not notify; check `GET /debug_token` (`is_valid`, `expires_at`) and handle 190 on any call | 401 means the grant is gone (re-consent); 403 means the member is no longer an administrator |
| Access tier | **Standard Access** covers users with a role on the app, which includes Jeff on his own Page; no App Review. **Advanced Access** (any user) needs App Review and Business Verification, not needed for one owned Page | **Development tier** first: 500 calls per app, 100 per member, no BATCH_GET; **Standard tier** needs a screen recording, test credentials and technical sign-off. A rejected app cannot reapply; a new app is required |
| Approval prerequisites | none for own Page | Registered legal organization, commercial use only; verified business email; legal name, registered address, website, privacy policy; a Page super admin must verify the app; no LinkedIn or Microsoft names or logos in the app; the app must be new and hold no other API product (the request button is otherwise grayed out) |

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
  Facebook comment text is sent to Claude (M4).
- Delete user data on request or when the user no longer has an account; the Data Protection Assessment
  asks about deletion practices.

## 6. Open items (settle in the developer portal or with a live call)

| ID | Question | Where it gets settled |
|---|---|---|
| M1 | Does `POST /{comment-id}/comments` create a reply as the Page? The Pages API guide says yes, the Graph reference says create is unsupported | one live reply on the test Page, before Phase 4 |
| M2 | Which permissions does reading comments actually need (`pages_read_engagement` vs `pages_read_user_content`)? | request both; test with each removed |
| M3 | Does the `feed` webhook deliver comment and reaction events while the app is in development mode with a role-holding admin? | subscribe the test Page and post a comment |
| M4 | Is sending Facebook comment text to the Claude API permitted under Meta's service-provider terms and Anthropic's agreement? | read both agreements; Jeff to confirm |
| M5 | Does removing a person from a Page role invalidate the Page token? | not documented; handle error 190 either way |
| L1 | Scope names: `r_organization_social` / `w_organization_social` vs `*_feed` | shown in the developer portal once the product is approved |
| L2 | Is the webhooks use case approved with Development tier, or only at Standard? | the access request form |
| L3 | Is `w_member_social` granted by Community Management (Profile Management) or only by Share on LinkedIn, which cannot share an app with Community Management? | the portal after the app is created |
| L4 | Does our app get programmatic refresh tokens? | the portal and the token response |
| L5 | May member comment text go to the Claude API? (section 1, item 1) | LinkedIn Developer Support Portal, in writing |
| L6 | Is the 100-calls-per-member limit per day, and does the planned traffic fit? | the portal rate-limit view |
| I1 | Which Instagram setup does the connect flow use, and does a Facebook Page have to be linked? The overview says Instagram Login and Unified Login need no Page; the publishing guide says the account must be connected to one | the Instagram product page in the app dashboard; one live publish |
| I2 | The daily API publishing limit: 100 or 50 posts in 24 hours? | `GET /<IG_ID>/content_publishing_limit` on the connected account |
| I3 | Which comment and mention webhook fields exist for the chosen setup, and do they fire in development mode? | the webhooks fields page; subscribe the account and comment on a post |
| I4 | The exact endpoints for reading, replying to and hiding a comment, and for a reply's author | the Instagram comments reference pages; one live reply |
| I5 | Can one Unified Login connection cover the Facebook Page and the Instagram account together, and how long does the SUAT last? | the login configuration page in the dashboard |
| I6 | Is sending Instagram comment text to the Claude API permitted under Meta's service-provider terms? Same question as M4 | read both agreements; Jeff to confirm |

## 7. Setup path Jeff must start (longest first)

1. LinkedIn: create a **new** app (no other products), associate it with the Geek @ Your Spot LLC
   company Page, have the Page super admin verify it, then file the Community Management Development tier
   request. List the use cases to be demonstrated: Page Management, Profile Management, Employee
   Advocacy (this carries the webhooks).
2. Meta: the app exists (2026-11-09), is connected to the Geek At Your Spot LLC business portfolio, has
   Facebook Login for Business, and Instagram is reported connected. Still to do: the Pages use case and
   the Facebook test Page. Keep the app in development mode; no App Review submission, Data Protection
   Assessment or Marketing API item is needed for the owner's own accounts.
3. Both: have the privacy policy and business email ready; LinkedIn requires a valid privacy policy at
   Standard tier.

## Sources (all read 2026-11-08)

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
