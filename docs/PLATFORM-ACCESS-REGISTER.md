# GeekSocial platform access register

Roadmap Phase 0, item 8 (`geek-social/plans/geek-social-roadmap-2026-11-18.md`). One row per network: what
GeekSocial needs from the platform before tenants can use it, what can stop it, and how it is kept current. It is
also the maintenance record for each connector: API version, date last verified, known limits and announced
deprecations.

**How this was made, and how far to trust it.** Compiled on 2026-11-18 from the research read from each platform's
own documentation on 2026-11-16 (`docs/platform-research-2026-11-16/`), and from `docs/PLATFORM-CAPABILITIES.md`.
Nothing here was tested against a live app. Every fact cites its line; a field the research does not cover says
"Not in the research", and a fact the research could not confirm keeps its UNVERIFIED mark. Nextdoor had no
research, so its row was read from Nextdoor's own pages on 2026-11-18 and cites their addresses. Re-read the cited
source before acting on any row older than three months.

Citation keys: `meta`, `linkedin`, `gbp`, `x`, `youtube`, `tiktok`, `pinterest` are the files in
`docs/platform-research-2026-11-16/`; `PC` is `docs/PLATFORM-CAPABILITIES.md`.

## What one app per network means

GeekSocial owns one app on each network, and every tenant connects through it (PC section 1b). So:
- **A suspension stops that network for every tenant at once.** None of the research describes what a platform
  does to an app's users when it suspends the app; this follows from the one-app model and is an inference, not a
  platform statement. Several platforms say they may suspend or revoke "at any time" (`pinterest:198`,
  `tiktok:233`, `gbp:215`).
- **One app's quota is shared by every tenant** where the platform counts per app or per project (YouTube:
  `youtube:115`; X: `x:57`; LinkedIn's per-app limits: `linkedin:102`).
- **Recovery is not documented by any platform in the research.** The only reapply paths are for a rejected
  application (`gbp:203`, `linkedin:116`) or a failed audit (YouTube's Appeals Form, `youtube:113`). Each row's
  recovery field says so.
- **Asking end users to register their own apps is not the model**, and X says doing so "could result in
  enforcement actions" (`x:154`).

## What the applications require of the public pages (input to Phase 0, item 9)

Every network in this register asks for a privacy policy before review. What each adds:

| Network | Requirement | Source |
|---|---|---|
| Facebook, Instagram | Privacy policy URL in the app settings, and the policy must describe deletion. Either a data deletion instructions URL or a Data Deletion Request callback (`{ url, confirmation_code }`); a failing callback can get the app disabled. | `meta:171`, `meta:205-207` |
| Google Business Profile | A published privacy policy that fully documents how the app handles user data; the research advises it say review text goes to an AI provider. | `gbp:187`, `gbp:231` |
| YouTube | The policy describes what is accessed, collected, stored, used and shared; links to `https://security.google.com/settings/security/permissions`; the delete mechanism says deleting GeekSocial's data does not affect YouTube's. | `youtube:165-166` |
| TikTok | The privacy policy and terms "visible on the website URL", and a demo video of the full flow. | `tiktok:206` |
| Pinterest | A policy "consistent with all applicable laws"; Trial is denied if it is "inaccurate or not accessible". | `pinterest:199`, `pinterest:146` |
| LinkedIn | A website and a privacy policy for the Development tier; a "Valid privacy policy" for Standard. | `linkedin:107`, `linkedin:115` |
| X | The app's website shows as the source label on every post. | `x:59` |

**robots.txt and the noindex header.** None of the research states a rule about crawling or indexing these pages;
the only reachability rule is Pinterest's "not accessible". So the pages must open without signing in, which the
draft on geek-social's `legal-pages` branch does; nothing in the research requires them to be indexed, so the
site-wide `noindex` can stay. A reviewer reads the page in a browser, which `noindex` does not stop.

**For the draft privacy policy:** its deletion section, `https://geek-social.geekatyourspot.com/privacy#delete` once
published (section 6 of the draft on geek-social's `legal-pages` branch), can serve as Meta's data deletion
instructions URL (`meta:207` accepts an instructions URL in place of a callback). Its promise that a comment's
drafts are deleted with it is not yet true in code (geek-social `HANDOFF.md`, "Legal pages"). The YouTube and Google wording above is added when
those connectors are built.

## At a glance

| Network | Review before customers can use it | Stated approval time | API price | Inbound events |
|---|---|---|---|---|
| Facebook Page | Business Verification, then Advanced Access per permission through App Review | Review "within a week"; verification not stated | Not in the research | Webhook, `feed` per Page, Live mode only |
| Instagram | Same app; Business Verification and Advanced Access per permission | "within a week" | Not in the research | Webhook per account, Live mode only |
| Google Business Profile | Basic API Access (quota 0 until approved); OAuth verification if `business.manage` is sensitive (UNVERIFIED) | None stated | No charge (`gbp:36`) | Pub/Sub, per account |
| X | Use case reviewed and binding; AI replies need prior approval | Not in the research | Pay per use, from $0.005 a read and $0.015 a post | Webhook (X Activity API) |
| YouTube | Compliance audit for quota above 10,000 units a day; OAuth verification for sensitive scopes | Verification up to 10 days; audit none stated | Not in the research | WebSub for uploads only; no comment push |
| TikTok | Company-only registration, app review, Accounts API form; Content Posting audit for public posts | Registration 3 business days; review 2 to 3 | Not in the research | Webhook, one per app |
| Pinterest | Trial (sandbox only), then Standard with a demo video | None published; "a few days" before following up | Not in the research | None for organic content |
| LinkedIn organization | Community Management: Development tier, then Standard | None stated (one unofficial report of about 40 business days) | Not in the research | Webhook, Standard tier only |
| LinkedIn member | Same app and review as the organization row | As above | Not in the research | None |
| Nextdoor | Publish API by application, and the form excludes promoting a business; the Share Plugin needs no approval | Not found | Not found | None documented |

## Facebook Page (Graph API v27.0)

The one connector that is built. Its state on 2026-11-18: the app `geekatyourspot-app` exists on the Geek At Your
Spot LLC portfolio with Facebook Login for Business; the Pages use case and a test Page are still to do
(`PC:248-250`, `PC:579-581`); GeekAPI lacks `GEEK_SOCIAL_META_VERIFY_TOKEN` and `GEEK_SOCIAL_META_LOGIN_CONFIG_ID`,
so no Page can be connected yet.

| Field | Fact | Source |
|---|---|---|
| Developer account | A Meta app owned by a business portfolio separate from the customers'; Pages use the Facebook Login for Business token path with `pages_*` permissions. | `meta:19`, `meta:37-40` |
| Business verification | Required for Advanced Access and before the app is published; without it other businesses' users cannot grant permissions and "all features will be inactive". | `meta:162`, `meta:166` |
| App review | Standard Access serves only people with a role on the app; every other business needs Advanced Access per permission through App Review, and Facebook Login for Business needs Advanced Access to `public_profile` before going live. Review covers app settings, data handling, a testable app, a screencast, and one successful call per permission within 30 days. | `meta:162`, `meta:167`, `meta:171-173`, `PC:503` |
| Permissions | `pages_show_list`, `pages_read_engagement`, `pages_manage_engagement`, `pages_manage_posts`, `pages_manage_metadata`, `pages_read_user_content` (the six `MetaAccountConnector.RequiredPermissions` checks), and `publish_video` for video. | `meta:165`, `PC:173-180` |
| Account eligibility | A Page only; a personal profile is blocked. The person needs the CREATE_CONTENT, MANAGE or MODERATE task, and replies need MODERATE. The Page must not have the App platform turned off. | `PC:173-181`, `meta:154` |
| Quotas | Page-token calls fall under Business Use Case limits, 4800 times engaged users per rolling 24 hours; error 80001 when limited. | `meta:219-222` |
| API price | Not in the research. | |
| Testing limits | In Development mode only people with a role on the app can grant permissions. Whether the `feed` webhook delivers in Development mode is UNVERIFIED (open item M3). Going Live before review can make the app unusable for role users and makes test posts public. | `meta:175`, `meta:177`, `PC:557` |
| Renewal | The long-lived user token lasts about 60 days; the Page token has no expiry but is invalidated (error 190: logout, password change, revoked authorization, security events) and Meta does not notify, so check `debug_token`. Annual Data Use Checkup; annual Data Protection Assessment with 60 days to complete once enrolled. | `PC:500-502`, `meta:178-179` |
| Approval time | App Review decision "within a week"; resubmission times not documented. Business Verification time: not in the research. | `meta:172` |
| Suspension | Not in the research. A failing data-deletion callback can mean "the app disabled", and a missed Data Protection Assessment risks "losing platform access". | `meta:206`, `meta:179` |
| Recovery | Not in the research (reapplication rules were not read). | `meta:182` |
| Public pages | App settings need a privacy policy URL, a 1024x1024 icon, a category and a business email. The app needs either a data deletion instructions URL or an HTTPS Data Deletion Request callback returning `{ url, confirmation_code }`, and the privacy policy must describe deletion. Terms URL and crawlability: not in the research. | `meta:171`, `meta:205-207`, `PC:544` |
| Inbound events | Verification `GET` with `hub.verify_token`; each event signed in `X-Hub-Signature-256` with the app secret; retries over 36 hours, so deduplicate. Installed per Page with `POST /{page-id}/subscribed_apps?subscribed_fields=feed`. Delivery to customers needs Live mode. | `PC:480-485`, `PC:461`, `meta:154` |
| Maintenance | Graph API v27.0, read 2026-11-16 (section 2a sources 2026-11-08 and 2026-11-09); Platform Terms and Developer Policies updated 2026-02-03. LIKE is the only reaction; an app updates only posts it created. Tech Provider Access Verification retired 2026-10-09. v27.0's end date: not in the research. | `meta:3`, `PC:598`, `meta:188`, `PC:178-179`, `meta:153`, `meta:168` |

Open items: M1 to M5 (`PC:555-559`): replying as the Page, which permission reading comments needs, the `feed`
webhook in Development mode, sending comment text to the AI provider under Meta's terms (M4, which decides whether
any provider can be cleared for Facebook), and whether removing a Page role invalidates the token.

## Instagram (professional account)

| Field | Fact | Source |
|---|---|---|
| Developer account | The same portfolio-owned Meta app; Unified Login is the recommended setup, from web surfaces only. Without a verified business, 15 apps at most. Which setup the dashboard used is not known. | `meta:19`, `meta:37-38`, `meta:181`, `PC:248-251`, `PC:419` |
| Business verification | "Required for production access" in all three setups, and before the app is published. | `meta:64`, `meta:166` |
| App review | A Tech Provider serving several businesses needs Advanced Access, with App Review "Required", for every permission in the configuration, and the app must be Live. The `comments` webhooks need Advanced Access. Requesting unused permissions fails the submission. `PC` section 2d's "Standard Access only" does not hold for a SaaS. | `meta:163-164`, `meta:63`, `meta:174`, `PC:436-438` |
| Permissions | `instagram_basic`, `instagram_manage_comments`, `instagram_content_publish`, and `business_management`; `instagram_manage_messages` only for private replies. No reaction permission is documented for Unified Login. | `meta:165`, `meta:29`, `meta:54`, `meta:93`, `PC:233` |
| Account eligibility | Professional accounts only (Business or Creator); no Page needed under Unified Login; each business portfolio is a separate connection; private accounts get no comment notifications. | `PC:217-218`, `meta:18-25`, `meta:65`, `meta:134` |
| Quotas | Business Use Case limit 4800 times impressions per rolling 24 hours per app and account; 50 or 100 posts a day (read `content_publishing_limit`); 400 containers a day. | `meta:217`, `meta:104-108` |
| API price | Not in the research. | |
| Testing limits | Development mode: people with a role on the app only; test users not supported for Unified Login review; webhook delivery in Development mode UNVERIFIED. | `meta:175`, `meta:171`, `meta:61-62` |
| Renewal | System-user token: never expires by default, optionally 60 days; it stops when the customer removes the app or moves the account, signalled by a `business_integration_uninstall` webhook. Annual Data Use Checkup and Data Protection Assessment. | `meta:26`, `meta:31-32`, `meta:178-179` |
| Approval time | "within a week". | `meta:172` |
| Suspension | Not in the research. | |
| Recovery | Not in the research. | |
| Public pages | As for Facebook; whether the deletion callback fires for customers who connected through Instagram login is UNVERIFIED. | `meta:171`, `meta:205-209` |
| Inbound events | Callback URL, verify token and fields set in the dashboard for the whole app; each account subscribed with `POST /{IG_ID}/subscribed_apps`; Live mode required; no store of missed events. | `meta:57-58`, `meta:61`, `meta:67-68` |
| Maintenance | Graph API v27.0, read 2026-11-16. JPEG only, 8 MB; no link, text-only or scheduled posts; replies to top-level comments only. | `meta:3`, `meta:117-118`, `meta:86`, `PC:229-230` |

Open items: I2 to I5 (`PC:567-570`), the system-user token path for Pages (`meta:33`), and the deletion callback for
Instagram-login customers (`meta:209`).

## Google Business Profile

| Field | Fact | Source |
|---|---|---|
| Developer account | A Google Cloud project; access is requested for the project by its number. The Business Profile APIs are enabled in it, and the GBP API appears in the console only after approval. | `gbp:196-197`, `gbp:37`, `gbp:204` |
| Business verification | The applicant must manage a GBP verified and active 60 days or more that lists a website (a client's profile counts; the application email must be an owner or manager on it). Whether `business.manage` is sensitive or restricted, and so needs Google OAuth verification, is UNVERIFIED; what that verification asks for is not in Google's docs. | `gbp:198-200`, `PC:281-282`, `gbp:186`, `gbp:247` |
| App review | "Application for Basic API Access" on the GBP API contact form. Approved when the quota shows 300 QPM; 0 QPM means not approved. A rejected applicant meets the requirements before reapplying. A demo account, if asked for, is due within seven days. | `gbp:196-203`, `gbp:212` |
| Permissions | `https://www.googleapis.com/auth/business.manage` for every action, read and write; no read-only scope. `access_type=offline` for a refresh token. | `gbp:176-178`, `PC:259-266` |
| Account eligibility | Accounts the user owns or manages. Reviews and replies need a verified location; on an unverified profile posts stay "Pending". A Workspace admin must enable Business Profile, or calls fail 403. Reviews exist only in some countries and categories. | `gbp:46`, `gbp:50`, `gbp:64-65`, `gbp:68`, `gbp:77`, `gbp:184` |
| Quotas | 300 QPM per API by default; edits 10 a minute per profile, not raisable; 429 above it. 0 before approval. The v4 Reviews, Posts and Media QPM is UNVERIFIED. | `gbp:201`, `gbp:205-207` |
| API price | "available to registered users at no charge". Pub/Sub cost: not in the research. | `gbp:36` |
| Testing limits | No sandbox. Quota 0 and the API hidden until approval. Refresh tokens expire in 7 days while the consent screen is in "Testing". | `gbp:38`, `gbp:182`, `gbp:201`, `gbp:204` |
| Renewal | Short-lived access tokens (read `expires_in`). A refresh token stops after six months unused, by admin policy, or when the account has more than 100 for one client (the oldest is dropped silently). | `gbp:179-183`, `PC:510` |
| Approval time | None stated for Basic API Access. | `gbp:202`, `PC:283` |
| Suspension | Google "may deactivate your API project ID if your project is not adhering to Google's API policies", and may review use "at any time and for any reason". One project serves every customer. | `gbp:215`, `gbp:233`, `gbp:208` |
| Recovery | Not in the research. | |
| Public pages | A published privacy policy that fully documents how the app handles user data; the research advises it say review text goes to an AI provider. Terms, homepage, domain verification and crawlability: not in the research. | `gbp:187`, `gbp:231` |
| Inbound events | Pub/Sub topic in our project with Publish granted to `mybusiness-api-pubsub@system.gserviceaccount.com`; one `notificationSetting` per customer account; the payload names the review and location only, so a `reviews.get` follows; push endpoints check an OIDC JWT; at-least-once, unordered. | `gbp:136-163`, `PC:268-271`, `PC:491` |
| Maintenance | v1 for accounts, business information, notifications, verifications and performance; v4 `mybusiness.googleapis.com` for reviews, posts and media. Read 2026-11-16. Reply at most 4096 bytes; stored content kept at most 30 days. Q&A discontinued 2025-11-03; `plus.business.manage` deprecated. | `gbp:25-34`, `gbp:3`, `gbp:67`, `gbp:177`, `gbp:228` |

Open items: `gbp:242-256` (the Pub/Sub message body, whether `business.manage` is sensitive, the v4 QPM, the review
time, whether review text may go to an AI provider).

## X (API v2)

| Field | Fact | Source |
|---|---|---|
| Developer account | X reviews every proposed use, and "Your use case description is binding on you". One app serves all customers; making end users register their own apps "could result in enforcement actions". Several apps for one use case are not allowed, apart from dev, staging and prod. White-labelling needs separate approval. | `x:157`, `x:154`, `x:168`, `x:158` |
| Business verification | Not in the research. | |
| Access tier | Pay-per-use credits. The pricing page lists free credits, pay-per-use and Enterprise; whether Basic and Pro remain is in conflict (`x:35` against `PC:289-291`). Self-serve is for "a limited number of end-users", not defined. AI-generated replies "Requires prior approval from X". | `x:20`, `x:34-37`, `x:155`, `x:162`, `PC:297` |
| Permissions | `tweet.read tweet.write users.read media.write offline.access`, and `tweet.moderate.write` only to hide replies; derived by the research as the minimum. | `x:143`, `PC:306` |
| Account eligibility | Not in the research. Posts from protected accounts are not delivered by the X Activity API. | `x:76` |
| Quotas | 3M Post reads a billing cycle. `POST /2/tweets` 10,000 per 24 hours per app and 100 per 15 minutes per user (an older page differs). Media upload 500 per 15 minutes. 1,500 X Activity API subscriptions on self-serve. | `x:22-24`, `x:57`, `x:89`, `x:127` |
| API price | Reads: Post $0.005, User $0.010. Writes: $0.015, $0.200 with a URL, $0.010 when summoned. Only successful responses are billed; resources deduplicated per UTC day. | `x:21-31` |
| Testing limits | Not in the research; separate dev, staging and prod apps are allowed. | `x:168` |
| Renewal | Access token 2 hours; `offline.access` gives a refresh token; refresh tokens are single-use and rotate, and two simultaneous refreshes make one fail; "about 6 months" (stated only on the OAuth 1.0a migration page). The user revoking access sends `oauth.revoke`. | `x:140`, `x:144-147`, `x:90-91` |
| Approval time | Not in the research. | |
| Suspension | Not described. If "Your API access is terminated", all X data is deleted within 10 business days. Reaching the spending limit blocks requests until the next cycle. | `x:175`, `x:32` |
| Recovery | Not in the research. | |
| Public pages | The app name and website show as the source label on posts. Privacy policy and terms: not in the research. | `x:59` |
| Inbound events | The X Activity API delivers mentions and replies to a webhook: HTTPS, public, no port, 200 within 10 seconds; CRC and per-event signature are HMAC-SHA256 with the OAuth 2.0 client secret in `X-Twitter-Webhooks-Signature-OAuth2`. The Account Activity API is being deprecated. | `x:80-104`, `PC:492` |
| Maintenance | Read 2026-11-16; agreements last updated 2026-04-27. OAuth 1.0a apps must move by 2027-01-31. Replies only when summoned; AI replies need approval. | `x:3-5`, `x:10`, `x:102`, `x:148` |

Open items: `x:192-205` (AI-reply approval, the end-user threshold, the summoned rule, X Activity API on pay-per-use).
`PC:110-123` records the decision that X is not asked anything, while `PC:588` lists a conversation with X sales;
the two need reconciling.

## YouTube (Data API v3)

| Field | Fact | Source |
|---|---|---|
| Developer account | A Google Cloud project; quota belongs to the project, and one project serves every customer. Creating several projects for more quota is prohibited. | `youtube:107`, `youtube:115-117`, `PC:335` |
| Business verification | Sensitive or restricted scopes need Google OAuth verification with a video demonstration; whether these scopes are sensitive or restricted is UNVERIFIED. | `youtube:131-133`, `PC:340-341` |
| App review | Quota above the default needs the YouTube API Services compliance audit ("Audit and Quota Extension Form"), a launch dependency; a re-audit within 12 months for a further extension. YouTube may audit at any time. | `youtube:112-114`, `youtube:120`, `youtube:173` |
| Permissions | `https://www.googleapis.com/auth/youtube.force-ssl` alone (read, reply, moderate, upload, thumbnails). The scope for held comments is UNVERIFIED. | `youtube:43`, `youtube:54`, `youtube:129-130`, `PC:325-330` |
| Account eligibility | Posting a comment needs the YouTube account merged with the Google account; only the owner moderates; custom thumbnails and uploads over 15 minutes need a verified account. | `youtube:47`, `youtube:63`, `youtube:89`, `youtube:95` |
| Quotas | 10,000 units a day per project, plus 100 `videos.insert` and 100 `search.list` a day; reset at midnight Pacific. A reply costs 50 units, so all tenants together get about 200 replies and 100 uploads a day. | `youtube:107-109`, `youtube:115`, `PC:471-472` |
| API price | Not in the research. | |
| Testing limits | Unverified apps have a user cap (number UNVERIFIED); refresh tokens expire in 7 days in "Testing". The private-only lock on unverified uploads was reversed on 2026-11-02. | `youtube:134`, `youtube:138`, `youtube:182-188` |
| Renewal | Access token from `expires_in`. A refresh token ends after 6 months unused, on revocation, by admin policy, or when the 101st is issued. Stored data refreshed or deleted within 30 days. | `youtube:135-139`, `youtube:157`, `youtube:164` |
| Approval time | Sensitive-scope verification up to 10 days; the audit has no stated time. | `youtube:113`, `youtube:118`, `youtube:132` |
| Suspension | Not in the research. | |
| Recovery | Not in the research (the Appeals Form covers failed audits only). | `youtube:113` |
| Public pages | A published privacy policy describing what is accessed, collected, stored, used and shared, linking to `https://security.google.com/settings/security/permissions`; the delete mechanism must say deleting our data does not affect YouTube's. Other pages: not in the research. | `youtube:165-166`, `youtube:174` |
| Inbound events | WebSub for uploads and title or description changes only; no push for comments, which need a `commentThreads.list` read. | `youtube:74-79`, `PC:332` |
| Maintenance | Data API v3; read 2026-11-16 (pages stamped 2026-11-12). No Community-post API; replies to top-level comments only. Upload cost fell to about 100 units on 2025-12-04. | `youtube:1-11`, `youtube:26`, `youtube:45`, `youtube:110-111` |

Open items: `youtube:194-205` and `youtube:174` (held-comment scope, sensitive-scope status, the audit's criteria,
whether AI drafting is allowed under the derived-data rules, the Required Minimum Functionality pages).

## TikTok

TikTok has two products: the Business API's Accounts API (comments and publishing) and developers.tiktok.com (Login
Kit and the Content Posting API), whose scopes do not cover comments (`tiktok:10-16`, `PC:351-352`).

| Field | Fact | Source |
|---|---|---|
| Developer account | Business API registration is company-only ("unable to onboard personal accounts or individual developers") with a verified company-domain email; at most five apps per developer. The app needs the "TikTok Accounts" permission. | `tiktok:200-201`, `tiktok:165` |
| Business verification | No separate step in the research; registration itself is company-only. | `tiktok:200` |
| App review | Business app review; since 2026-03-20 the "Accounts API Access Application Form" before a new app or a scope increase with "TikTok Accounts" (criteria not read). The Content Posting API needs an audit before public posts, and lists "A utility tool to help upload contents to the account(s) you or your team manages" as not acceptable. | `tiktok:201-202`, `tiktok:39-40`, `PC:368-375` |
| Permissions | Comments: 18030100 "Get Business Comment" and 18030200 "Manage Business Comment"; "Business Media" 18020000; publishing "Video Publish" on the Business API. No final list recorded. | `tiktok:96-106`, `tiktok:50`, `tiktok:25-28` |
| Account eligibility | Which account types can authorize the comment scopes is UNVERIFIED; comment webhooks cover public posts only; regions UNVERIFIED. | `tiktok:107`, `tiktok:125`, `tiktok:209` |
| Quotas | Business publishing 6 a minute and 15 a day per account. Accounts API 40 queries a minute per account per endpoint, 600 or 1,000 a minute per app. | `tiktok:57`, `tiktok:253-254` |
| API price | Not in the research. | |
| Testing limits | Before the Content Posting audit: private viewing only, accounts set to private, at most 5 users posting in 24 hours. Business Sandbox accounts are "To-be-deprecated". | `tiktok:37-38`, `tiktok:206`, `tiktok:210`, `PC:372-373` |
| Renewal | Business API: access token 1 day, refresh token 1 year. developers.tiktok.com: 2 hours and 180 days (an FAQ says one year). Users can revoke at any time. | `tiktok:167-172`, `tiktok:176`, `tiktok:187-190` |
| Approval time | Registration 3 business days; Business app review 2 to 3 business days; developers.tiktok.com review "several days to two weeks". The Accounts API form and the audit: UNVERIFIED. | `tiktok:200-205` |
| Suspension | TikTok "may suspend or terminate some or all TikTok Developer Services at any time with or without notice", and may revoke access "without prior notice". Webhooks are per app. | `tiktok:233`, `tiktok:204`, `tiktok:130` |
| Recovery | Not in the research; violations "will likely lead to immediate revocation ... and a permanent ban". | `tiktok:211` |
| Public pages | developers.tiktok.com review: the privacy policy and terms "visible on the website URL" and a demo video of the full flow. Media URLs on a verified domain (DNS TXT or signature file). Business API page requirements: not in the research. | `tiktok:206`, `tiktok:35-36`, `tiktok:53` |
| Inbound events | Business API: one `callback_url` per app, `TikTok-Signature` HMAC-SHA256 with the app secret; `comment.update` "within five minutes"; retries undocumented. | `tiktok:120-130`, `PC:493` |
| Maintenance | Business API v1.3; Content Posting `/v2/`; read 2026-11-16. Unverified video URLs can no longer publish. | `tiktok:1`, `tiktok:21`, `tiktok:46`, `tiktok:53` |

Open items: `tiktok:263-277` (account types for the comment scopes, the Accounts API form's criteria, whether
Business API publishing escapes the private-only rule, whether commenter text may go to an AI provider).

## Pinterest (API v5)

| Field | Fact | Source |
|---|---|---|
| Developer account | developers.pinterest.com, administered from a Pinterest business account; the app name may not contain "Pinterest". | `pinterest:140`, `pinterest:155` |
| Business verification | Not in the research. | |
| App review | Trial (Pins and boards visible only to their creator, as Sandbox entities), then Standard: an approved Trial app, a video of the app doing an API action including the OAuth flow, and a check of the use case and privacy-policy link. Material changes need re-review. | `pinterest:140-148`, `PC:404-406` |
| Permissions | `boards:read`, `boards:write`, `pins:read`, `pins:write` to publish; no final list recorded. | `pinterest:27`, `pinterest:121`, `PC:393` |
| Account eligibility | Apps registered on or after 2026-09-14 get `403 PINNER_DATA_ACCESS_DENIED` on five reads for non-business users; a business account that authorized the app is the exception. Whether `POST /pins` works for personal accounts is UNVERIFIED. | `pinterest:17`, `pinterest:149-151`, `PC:406-408` |
| Quotas | Trial per day per app, Standard per minute per user per app: `org_write` 300 and 100, `org_read` 1,000 and 1,000. Trial 1,000 requests a day in all. "subject to change without notice". | `pinterest:164-171` |
| API price | Not in the research. | |
| Testing limits | Trial content is sandbox-only; sandbox `GET /boards` can return empty items while boards exist; analytics disabled. | `pinterest:141`, `pinterest:215`, `pinterest:95` |
| Renewal | Access token 30 days; continuous refresh token 60 days, "refreshable indefinitely". A password change invalidates tokens and Pinterest "does not always notify". Revoke covers system users only. | `pinterest:123-130`, `PC:403-404` |
| Approval time | No SLA; "allow a few days" before following up. | `pinterest:21`, `pinterest:147` |
| Suspension | "Pinterest may terminate or suspend your right to access or use the Materials at any time for any reason"; three strikes bans the app; on termination Pinterest data is deleted within 30 days. | `pinterest:198`, `pinterest:173`, `pinterest:172`, `pinterest:191` |
| Recovery | Not in the research. | |
| Public pages | A privacy policy "consistent with all applicable laws"; Trial is denied if the "Privacy policy is inaccurate or not accessible"; the Standard demo video shows live integration (wireframes and shared credentials refused). | `pinterest:199`, `pinterest:146`, `pinterest:144-145` |
| Inbound events | None for organic Pins, boards or comments; new items come only from reads a person starts. | `pinterest:15`, `pinterest:104-108`, `PC:397` |
| Maintenance | v5 (OpenAPI 5.28.0); read 2026-11-16; Terms updated August 2026. Publish only, no comments. The 2026-09-14 `PINNER_DATA_ACCESS_DENIED` policy; legacy 365-day refresh tokens retired. | `pinterest:3-20`, `pinterest:126`, `pinterest:150`, `PC:382-384` |

Open items: `pinterest:204-215` (personal accounts after 2026-09-14, `image_url` fetching, refresh-token
invalidation, how a disconnect invalidates a user's token).

## LinkedIn organization (company page)

| Field | Fact | Source |
|---|---|---|
| Developer account | A new LinkedIn app with no other products, associated with the Geek @ Your Spot LLC company Page and verified by its super admin; Community Management must be the only product (Share on LinkedIn blocks the request). | `PC:575-576`, `linkedin:118`, `linkedin:132` |
| Business verification | Development tier: a registered legal organization for commercial use, a verified business email, legal name, registered address, website and privacy policy, the Page super admin's verification, no LinkedIn or Microsoft names or logos. | `linkedin:107-108`, `PC:504` |
| App review | Community Management API: Development tier, then Standard (full integration, overview, use case, test credentials, screen recording). A rejected app cannot reapply at either tier. Use cases: Page Management, Profile Management, Employee Advocacy. | `linkedin:101-116`, `PC:577-578` |
| Permissions | `r_organization_social`, `w_organization_social`, `r_organization_social_feed`, `w_organization_social_feed`; webhooks need `rw_organization_admin`. | `linkedin:227`, `linkedin:168`, `PC:443-445` |
| Account eligibility | The tenant's organization admin signs in; Page access "cannot be granted or updated through the API". Posting needs ADMINISTRATOR, DIRECT_SPONSORED_CONTENT_POSTER or CONTENT_ADMIN. One person's profile may not manage many customers' accounts. | `linkedin:123-124`, `PC:192` |
| Quotas | Development: 500 calls per app and 100 per member a day, no BATCH_GET. Standard: not published, shown in the portal. Reset at midnight UTC; 429 above it. | `linkedin:102`, `linkedin:210-219` |
| API price | Not in the research. | |
| Testing limits | Development-tier limits and no webhook push; integration finished within 12 months. Whether a Development app can connect another company's Page is UNVERIFIED. | `linkedin:102-105` |
| Renewal | Access token 60 days; refresh token 365 days, not extended by refreshing. Whether this app gets refresh tokens is UNVERIFIED (without them, re-consent every 60 days). Different scopes invalidate earlier tokens. | `linkedin:144-148`, `linkedin:125`, `PC:501` |
| Approval time | None stated; an unopened unofficial snippet reports about 40 business days at Standard "stage 1". | `linkedin:117`, `PC:450-451` |
| Suspension | Not in the research; LinkedIn "reserves the right to revoke" tokens at any time. | `linkedin:152` |
| Recovery | Not in the research; after a Standard rejection a new app and a new Development request are needed. | `linkedin:116` |
| Public pages | A website and a privacy policy for Development; a "Valid privacy policy" is a Standard criterion. Terms and reachability: not in the research. | `linkedin:107`, `linkedin:115`, `PC:589-590` |
| Inbound events | Standard tier only; one subscription per app, member, organization and event type; HTTPS, an HMAC challenge within 3 seconds, re-validated every 2 hours; `X-LI-Signature`; retries every 5 minutes for 8 hours. | `linkedin:164-174`, `PC:480-485` |
| Maintenance | `Linkedin-Version: 202610` (sunsets 2027-10-15), `X-Restli-Protocol-Version: 2.0.0`; each monthly version supported at least a year; read 2026-11-16. Media uploaded as files only. | `PC:188`, `linkedin:21-27`, `PC:433` |

## LinkedIn member (personal profile)

The same app, review and token rules as the organization row (`linkedin:131-132`, `PC:445-447`). It differs in:
- **Permissions:** `w_member_social`; `r_member_social` is closed to new requests (`PC:209-210`, `linkedin:134`).
- **Eligibility:** the member's own profile only (`linkedin:49`, `linkedin:124`).
- **Inbound events:** none, and no read access to fall back on, so the member path is publish-only
  (`PC:211-213`).
- **Review:** the Profile Management recording must show "a user posting to their LinkedIn profile via your app"
  (`linkedin:113`); whether the Development tier grants `w_member_social` is UNVERIFIED (`linkedin:133`).

LinkedIn open items: `linkedin:105`, `PC:560-565` (a second company's Page on the Development tier, refresh
tokens, whether member comment text may go to the AI provider).

**Where the research and `PLATFORM-CAPABILITIES.md` disagreed.** Corrected in `PLATFORM-CAPABILITIES.md` on
2026-11-18, to match its own section 2j and the research:
- LinkedIn webhooks: the section 2b row said Development tier; they need Standard (`linkedin:164`, `PC:440`).
- LinkedIn webhook payload: section 2b said URNs only; it can carry comment text (`linkedin:170`, `PC:442`).
- LinkedIn technical sign-off: section 4 said Standard needs it; whether it does is UNVERIFIED (`linkedin:121`).

Not disagreements, on reading both:
- X Automation Rules and the Basic and Pro plans: the research's fetches failed or found only the changelog, and
  `PC:289-291` and `PC:311` record a later re-read in a browser. `PC` is the newer evidence; the changelog's "remain
  available" (`x:35`) and a pricing page that lists no Basic or Pro price can both be true.
- YouTube `thumbnails.set`: `PC:327` names the one scope GeekSocial uses; `youtube:22` lists every scope the method
  accepts.

## Nextdoor

No research existed for Nextdoor. This row was read from Nextdoor's own pages on 2026-11-18;
`help.nextdoor.com` did not load (certificate error), so help-centre facts come from `business.nextdoor.com` and
`blog.nextdoor.com`. Sources are listed below the table by letter.

**Decision needed.** The roadmap says "posts are planned and approved in GeekSocial; there is no Share Plugin.
Publishing goes through Nextdoor's API once access exists." (`geek-social/plans/geek-social-roadmap-2026-11-18.md:43`). Nextdoor's own Publish API
application form says: "The Publish API is approved only for the following use cases: public agency
communications, emergency alerts, local news, local events, and marketplace listings." and "It is not available for
promoting your own business or your clients' businesses." [F]. And Nextdoor's access page says the Share Plugin
"is an open offering and you are free to integrate it without Nextdoor approval" [AV], which the roadmap's "there is
no Share Plugin" does not reflect. What the Share Plugin does, and whether it can post as a business page, is not
found in Nextdoor's pages. Both checked again at the source on 2026-11-18.

| Field | Fact | Source |
|---|---|---|
| Developer account | "The Nextdoor API is currently in closed BETA"; "You must apply for access to integrate the Publish API" through the Publishing API request form; if approved Nextdoor sends "a client id and client secret". | [AV], [SI], [A] |
| Business verification | The form asks for company name, site URL, headquarters address, company email, organization type ("Software platform or SaaS" is offered), product stage, approved category, posts per week and the use case; "API requests are meant to be for commercial use cases only". Further checks: not found. | [F] |
| App review | Not found beyond submitting the form; "Every application is reviewed individually against our current program criteria", which are not described. The form's approved categories exclude promoting a business (above). | [A], [F] |
| Permissions | `openid`, `publish_api`, `post:write`, `post:read`, `comment:write`, `profile:read`, `agency.boundary:read`, `profile`. Reading comments: `GET /post/` returns comments, but "Business and Entity page users are not supported at the moment". | [AC], [CP], [CC], [GP] |
| Account eligibility | Posts come from a Nextdoor user (neighbor, public agency or business), "not from the partner platform directly"; one login can hold several business profiles, named per call by `secure_profile_id`; the poster must be "an owner, employee, or other authorized representative of the business"; an unverified business cannot post to the newsfeed (2021 FAQ). | [SO], [B], [CP], [BP], [FAQ] |
| Quotas | Not found; no 429 in the error codes. A post's body is at most 8,192 bytes with at most 10 media attachments. | [EC], [CP] |
| API price | Not found. | |
| Testing limits | Not found. | |
| Renewal | Authorization at `https://www.nextdoor.com/v3/authorize/`, token at `https://auth.nextdoor.com/v2/token`; the only lifetime shown is an example `expires_in: 31536000`; no `refresh_token` field is documented, though "You must also periodically refresh the access tokens", and the refresh page contradicts itself. | [AC], [AT], [RT] |
| Approval time | Not found. | |
| Suspension | Not found. | |
| Recovery | Not found. | |
| Public pages | Not found; the form requires a company site URL, and redirect URIs and a logo URL "only required for Publish API". | [F] |
| Inbound events | None documented. | [L] |
| Maintenance | Publish endpoints under `/external/api/partner/v1/`; the changelog announces no deprecations. Read 2026-11-18. | [CP], [CL] |

Sources (read 2026-11-18): [A] https://developer.nextdoor.com/reference/applying-for-access ·
[F] https://forms.gle/i2hHc4A9noKJRBGW9 (the "Nextdoor Share API Developer Application", "created inside of
Nextdoor") · [AV] https://developer.nextdoor.com/reference/sharing-availability ·
[SI] https://developer.nextdoor.com/reference/sharing-introduction ·
[SO] https://developer.nextdoor.com/docs/sharing-overview · [B] https://developer.nextdoor.com/docs/business-share ·
[AC] https://developer.nextdoor.com/reference/sharing-get-authorization-code ·
[AT] https://developer.nextdoor.com/reference/sharing-get-access-token ·
[RT] https://developer.nextdoor.com/reference/sharing-refresh-access-token ·
[EC] https://developer.nextdoor.com/reference/sharing-error-codes.md ·
[CP] https://developer.nextdoor.com/reference/create-post.md ·
[CC] https://developer.nextdoor.com/reference/create-comment-to-a-post-create-reply-to-a-comment.md ·
[GP] https://developer.nextdoor.com/reference/get-your-posts.md · [L] https://developer.nextdoor.com/llms.txt ·
[CL] https://developer.nextdoor.com/changelog/welcome-to-nextdoor-test.md ·
[BP] https://business.nextdoor.com/en-us/small-business/product/business-posts ·
[FAQ] https://business.nextdoor.com/local/resources/managing-your-nextdoor-business-page-answers-to-your-most-asked-questions

Third-party claims that Nextdoor limits access to approved partner categories, or that other tools integrate with
it, were not used as facts.
