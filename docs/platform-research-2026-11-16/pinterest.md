# Pinterest API v5 - capabilities research for GeekSocial

Researched 2026-11-16. Official sources only (developers.pinterest.com, policy.pinterest.com, Pinterest's own OpenAPI repo, one Pinterest staff post on community.pinterest.biz).
Legend: CONFIRMED = read in an official page I opened and cite. UNVERIFIED = docs silent, conflicting, or not openable. BLOCKED = unsupported, closed, or needs an approval we do not have.

## How the evidence was gathered (read this first)

- The docs pages under developers.pinterest.com are client-rendered; the WebFetch tool returned only navigation for them. I fetched the same pages with curl and stripped the HTML, which returns the full server-rendered text. The Terms page (developers.pinterest.com/terms/) loads its text client-side, so I read it in a real browser.
- The endpoint list comes from Pinterest's official OpenAPI description: https://github.com/pinterest/api-description (file `v5/openapi.json`, `info.version` = 5.28.0, last commit 2026-06-04, MIT license, README says "this schema is used internally by Pinterest's API infrastructure"). The live API reference page https://developers.pinterest.com/docs/api/v5/pins-create embeds the same spec, and its navigation tree (305 operations, includes the October 2026 `/media_library/...` additions) matches. So the endpoint list is current, not stale.
- The spec has `security` (scopes) and `x-ratelimit-category` per operation; the "Scope" and rate-limit columns below come from there.

## HEADLINE FINDINGS (affect the GeekSocial plan)

1. **There is no official v5 API to read or reply to Pin comments. BLOCKED.** None of 305 operations is a comments endpoint (see section 3). The Developer Guidelines additionally list "making comments" among actions you may not take for users without their specific knowledge and consent.
2. **There are no webhooks for organic Pins/boards/comments. BLOCKED.** The only webhook-like feature is Lead Ads subscriptions (ads lead-form data), which is irrelevant to GeekSocial (section 4). `POST /notifications` is the reverse direction (partner -> Pinterest) and gated.
3. **Pinterest is therefore a publish-only channel for GeekSocial.** The agent "read comments -> draft reply" loop cannot exist for Pinterest. At most: create Pins, list/read the customer's own Pins and boards on demand.
4. **Apps registered on or after 2026-09-14 cannot read boards/Pins of personal (non-business) accounts.** A Pinterest staff post says personal accounts using such apps "won't have this access"; fix is converting the account to business. Our app, if registered now, falls in this group (section 6). Whether POST /pins (write) is also blocked for personal accounts is UNVERIFIED.
5. **Developer Guidelines forbid auto-initiated actions.** "Offering features that enable end users to automatically initiate actions without specifically considering each action" is listed as unacceptable, with the Pin scheduler example "the end user must choose each Pin". This conflicts with a "category auto-sends" mode for Pinterest. Human-approves-each-Pin is the compliant shape.
6. **Storage ban:** "you may not store any information accessed through any Pinterest Materials including the API. Instead, call the API each time you need to access information." (Exception: campaign analytics.) This constrains what GeekSocial may persist from Pinterest reads.
7. **AI/ML ban:** using Pinterest Materials "to train, fine-tune, or otherwise improve or develop any artificial intelligence or machine learning models" is prohibited without written consent. The text does not mention inference/prompting; whether sending Pinterest data into a third-party LLM prompt is allowed is UNVERIFIED (see section 8).
8. **Trial access:** Pins and boards created are visible only to the creator (Sandbox). Standard access needs an OAuth-flow demo video and review; no review-time SLA is published.

---

## 1. PUBLISHING

Base URL `https://api.pinterest.com/v5`. All scopes below are from the OpenAPI `security` blocks (the Pins/boards guide https://developers.pinterest.com/docs/api-features/creating-boards-and-pins/ also says to generate a token with `boards:read`, `boards:write`, `pins:read`, `pins:write`).

| Action | Endpoint | Scope | Access level | Status | Quote+URL |
|---|---|---|---|---|---|
| Create Pin | `POST /pins` | boards:read, boards:write, pins:read, pins:write | Trial (visible only to creator) / Standard. Rate category `org_write`. Sandbox-enabled | CONFIRMED | "Create a Pin on a board or board section owned by the \"operation user_account\"." - https://developers.pinterest.com/docs/api/v5/pins-create (spec: https://raw.githubusercontent.com/pinterest/api-description/main/v5/openapi.json) |
| Create Pin: fields | body: `title` (max 100), `description` (max 800), `alt_text` (max 500), `link` (max 2048), `board_id`, `board_section_id`, `media_source`, `parent_pin_id`, `dominant_color`, `sponsor_id`, `ai_disclosures` | same | same | CONFIRMED (limits from the spec's JSON schema, no prose quote) | Spec schema `PinCreate`: `title.maxLength=100`, `description.maxLength=800`, `alt_text.maxLength=500`, `link.maxLength=2048` - openapi.json above. `board_id` is not in the `required` list in the schema (the spec shows `required: []`), but the docs say a board is needed unless the Pin is ad-only: "Pins only require a board ID to be created" - https://developers.pinterest.com/docs/key-concepts/best-practices/ |
| media_source `image_url` | `media_source: {source_type:"image_url", url}` | pins:write etc. | Trial/Standard | CONFIRMED | Spec: required `source_type`, `url`; optional `is_standard` (default true). Docs example: "The URL of the Pin image from the third-party website." - https://developers.pinterest.com/docs/api-features/creating-boards-and-pins/ |
| media_source `image_base64` | `{source_type:"image_base64", content_type:"image/jpeg"\|"image/png", data}` | same | same | CONFIRMED | Spec enum `content_type`: `image/jpeg`, `image/png`; `data` pattern `^[a-zA-Z0-9+/=]+$` - openapi.json |
| Carousel / multi-image | `source_type:"multiple_image_urls"` (items[].url required; optional title/description/link per item; optional `index`) or `multiple_image_base64` (items[].content_type, data) | same | same | CONFIRMED | Spec: `items` `minItems: 2`, `maxItems: 5` - openapi.json. Help page: "2 to 5 images per carousel" - https://help.pinterest.com/business/article/pinterest-product-specs/ |
| Video Pin | `source_type:"video_id"` with `media_id` (required), plus a cover: `cover_image_url` OR `cover_image_data`+`cover_image_content_type` OR `cover_image_key_frame_time` (seconds) | same | same | CONFIRMED | "To create video Pins, provide a video and a cover image." and "A valid image url must be provided to avoid a 400 Bad Request." - https://developers.pinterest.com/docs/api-features/creating-boards-and-pins/ . NOTE: the guide's prose example uses `cover_image_url`; the spec also allows base64 cover and key-frame time (spec schema `PinMediaSourceVideoID`). |
| `pin_url` source | `{source_type:"pin_url", is_affiliate_link}` | - | Beta users only | BLOCKED (for us) | Spec: "Currently the field is only available to a list of beta users." - openapi.json |
| `is_standard:false` (simplified Pin) | in image/video media_source | - | beta | BLOCKED (for us) | Spec: "Currently the field is only available to a list of beta users." - openapi.json |
| AI disclosure on a Pin | `ai_disclosures: {values:["AI_MODIFIED","SYNTHETIC_PERFORMER"]}` | pins:write | Trial/Standard | CONFIRMED (field exists) | Spec: "AI disclosure declarations the creator has made about this Pin." - https://developers.pinterest.com/docs/api/v5/pins-create . Whether Pinterest REQUIRES disclosure for AI-made organic Pins is UNVERIFIED (changelog entry "Disclosing AI content in ads" exists, June 2026, body not readable - see section 9). |
| Update Pin | `PATCH /pins/{pin_id}` | boards:read/write, pins:read/write | same | CONFIRMED (endpoint+scopes) | openapi.json |
| Delete Pin | `DELETE /pins/{pin_id}` | same | same | CONFIRMED | openapi.json; listed in "Pins" group at https://developers.pinterest.com/docs/api-features/creating-boards-and-pins/ |
| Save an existing Pin to a board | `POST /pins/{pin_id}/save` | same | same | CONFIRMED | Guide: "We recommend using the POST Save Pin endpoint whenever you or your users are curating Pins." - creating-boards-and-pins page |
| List boards | `GET /boards` (params incl. privacy, bookmark, page_size) | boards:read (secret boards need boards:read_secret) | Trial/Standard; `org_read` | CONFIRMED | "Get a list of the boards owned by the \"operation user_account\" + group boards where this account is a collaborator" - spec `boards/list` |
| Create board | `POST /boards` body `name` (required), `description`, `privacy` PUBLIC/PROTECTED/SECRET (default PUBLIC), `is_ads_only` | boards:read, boards:write | `org_write` | CONFIRMED | "Create a board owned by the \"operation user_account\"." - spec `boards/create` |
| Get / update / delete board | `GET/PATCH/DELETE /boards/{board_id}` | boards:read (+boards:write for PATCH/DELETE) | | CONFIRMED | openapi.json |
| Board sections: list/create/update/delete | `GET/POST /boards/{board_id}/sections`, `PATCH/DELETE /boards/{board_id}/sections/{section_id}`, `GET .../sections/{section_id}/pins` | boards:read (+boards:write for writes) | | CONFIRMED | "Create a board section on a board owned by the \"operation user_account\" - or on a group board that has been shared with this account." - spec `board_sections/create` |
| Must a board exist before creating a Pin? | - | - | - | CONFIRMED | Pin create needs `board_id` (optionally `board_section_id`); ad-only Pins auto-go to an "Ad-only Pins" board: "you do not have to specify a board ID in this case." (creating-boards-and-pins page). So for organic Pins the board must exist first; create it via `POST /boards`. |
| Volume limits | - | - | - | CONFIRMED | "Boards: up to 2,000 boards of any type" and "Pins: up to 200,000 Pins including secret Pins" - creating-boards-and-pins page |
| Scheduled publishing (native `publish_at`) | none in `PinCreate` | - | - | BLOCKED (no such field) | The spec's PinCreate properties are: ai_disclosures, alt_text, board_id, board_section_id, description, dominant_color, link, media_source, parent_pin_id, sponsor_id, title. Scheduling would have to be done in GeekSocial (a human action triggering the publish; no timers allowed by our rule). |

### Video upload flow (CONFIRMED)

Source: https://developers.pinterest.com/docs/api-features/creating-boards-and-pins/ (section "Creating video Pins"), spec for `/media`.

| Step | Endpoint | Scope | Status | Quote |
|---|---|---|---|---|
| 1. Register | `POST /media` body `{"media_type":"video"}` -> `media_id`, `upload_url`, `upload_parameters` | pins:read, pins:write (`org_write`) | CONFIRMED | "This declares your intention to upload a video to Pinterest." |
| 2. Upload | `POST {upload_url}` (an S3 accelerate bucket) as multipart/form-data with all `upload_parameters` as form fields plus the `file` | none | CONFIRMED | "No Bearer Authorization is needed for this step. A successful call will return a 204 No Content." |
| 3. Poll status | `GET /media/{media_id}` -> status `registered`, `processing`, `succeeded`, `failed` | pins:read (`org_read`) | CONFIRMED | "Once you receive a succeeded status you can proceed to step 4." |
| 4. Create Pin | `POST /pins` with `media_source.source_type="video_id"`, `media_id`, cover | as above | CONFIRMED | "both the video and cover image will be copied and stored on Pinterest's servers." |
| After create | Video Pins processing | - | CONFIRMED | "you may see an incorrect value for the creative_type field until the processing finishes." |

NOTE for GeekSocial: step 3 is a status poll. Our rule forbids timer polling; the poll must be triggered by a person's action (or the publish step must be held "processing" until the user next opens the screen). Pinterest offers no push for upload completion (section 4). Whether step 4 can be called before `succeeded` is UNVERIFIED (docs say to wait).

## 2. MEDIA RULES

| Rule | Value | Status | Quote+URL |
|---|---|---|---|
| Base64 vs URL | API accepts both `image_url` and `image_base64` (also multi variants). Videos only via register/upload (`video_id`); no URL option for the video itself. | CONFIRMED | Spec `media_source` discriminator mapping: image_base64, image_url, video_id, multiple_image_base64, multiple_image_urls, pin_url - openapi.json |
| Is the `image_url` fetched by Pinterest? Must it be public? | Docs do not state the fetch mechanism, user-agent, auth, redirect or timeout behaviour. Only: "The URL of the Pin image from the third-party website." | UNVERIFIED (docs silent) | https://developers.pinterest.com/docs/api-features/creating-boards-and-pins/ . Settle with a live call: use a signed/private URL vs a public one. Safe default is base64 or a public URL. |
| Accepted image types (API base64) | `image/jpeg`, `image/png` only | CONFIRMED | Spec enum - openapi.json |
| Accepted image types (help page) | ".PNG or .JPEG" | CONFIRMED (ad spec page, applies to formats that can also be organic) | https://help.pinterest.com/business/article/pinterest-product-specs/ |
| Image conversion | "All uploaded images are converted to standard 8-bit RGB JPEGs before being turned into Pins and ads." | CONFIRMED | same page |
| Max image size | "Desktop 20 MB, In-app 32 MB" for standard image ads. Carousel: "20 MB per image". These are the Ads spec page ("Review ad specs"), which says standard image/video/carousel formats "Can be used in paid ad campaigns or to generate organic impressions". An API-specific max for `image_url`/`image_base64` is not documented. | UNVERIFIED for the API (CONFIRMED only for the ad-spec page) | https://help.pinterest.com/business/article/pinterest-product-specs/ |
| Base64 request body max | not documented | UNVERIFIED | - |
| Aspect ratio / dimensions (image) | Recommendation only: "We recommend using a 2:3 aspect ratio, or 1000 x 1500 pixels. Pins with an aspect ratio greater than 2:3 might get cut off in people's feeds." No hard minimum/maximum pixel dimension found in the docs I read. | CONFIRMED (recommendation) / hard limits UNVERIFIED | same help page |
| Carousel | 2-5 images; ratio "1:1 or 2:3" | CONFIRMED | spec `minItems 2 maxItems 5`; help page "Aspect ratio 1:1 or 2:3" |
| Video file types | upload step: "the video file (.mp4/.mov/.m4v)" | CONFIRMED | creating-boards-and-pins page |
| Video encoding / size / length / ratio | ".MP4, .MOV or .M4V"; "H.264 or H.265"; "Up to 2GB"; "Minimum 4 seconds, maximum 15 minutes"; ratio "Shorter than 1:2 (width:height), taller than 1.91:1" (these are the standard-width video ad specs, same page says usable organically) | CONFIRMED for the ad-spec page; applicability to the organic API upload is UNVERIFIED | https://help.pinterest.com/business/article/pinterest-product-specs/ |
| Title shown | "Enter up to 100 characters. ... the first 40 characters may show in people's feeds." | CONFIRMED | same page |
| Description | "Enter up to 800 characters." | CONFIRMED | same page; matches spec maxLength 800 |
| Video cover | required; invalid cover URL -> 400 | CONFIRMED | "A valid image url must be provided to avoid a 400 Bad Request." |
| Copying | Pins copy the media: "both the video and cover image will be copied and stored on Pinterest's servers." | CONFIRMED | creating-boards-and-pins page |

## 3. COMMENTS AND ENGAGEMENT

Method: (a) listed all 305 operations in the live docs navigation tree embedded in https://developers.pinterest.com/docs/api/v5/pins-create and all paths in the official spec; (b) searched the spec text for "comment", "reaction", "webhook"; (c) read the changelog index https://developers.pinterest.com/docs/changelog/changelog/ (2026 entries, titles only; entry bodies load client-side and I could not read them) and the Sandbox endpoint list https://developers.pinterest.com/docs/developer-tools/sandbox/.

| Action | Endpoint | Scope | Access level | Status | Evidence |
|---|---|---|---|---|---|
| Read comments on a Pin | none | - | - | BLOCKED | Verified absence: no path containing `comment` exists among the 305 operations. Pin-related paths are only: `/pins`, `/pins/{pin_id}`, `/pins/{pin_id}/analytics`, `/pins/{pin_id}/save`, `/pins/{pin_id}/product_tags` (+`bulk-delete`), `/pins/analytics`. Board paths: boards, sections, `/boards/{id}/pins`. |
| Reply to / create / delete a comment | none | - | - | BLOCKED | Same absence. The Developer Guidelines name "making comments" as an action not to take without user consent, but no endpoint exists to do so. |
| Reactions | none | - | - | BLOCKED | No reactions endpoint. The only occurrences of "reaction" in the spec are the analytics metric names. |
| Hidden / unreleased comment API | x-tagGroups lists tags `aggregated_comments`, `aggregated_pin_data`, `entity_history` under "Pin and Boards", but no operation in the spec uses `aggregated_comments` (the tag is not even defined in `tags`). | - | - | UNVERIFIED (what it is) | In openapi.json `x-tagGroups[0].tags` = pins, boards, media, aggregated_comments, aggregated_pin_data, product_tags, user_account, entity_history. No paths. Do not infer a comments API from this. If it ever appears it will be a documented path. |
| Pin analytics (comment COUNT only) | `GET /pins/{pin_id}/analytics`, `GET /pins/analytics`, `GET /user_account/analytics*` | pins:read, boards:read / user_accounts:read | Sandbox-disabled; `org_analytics` | CONFIRMED but OUT OF SCOPE | Spec: "Total comments and total reactions are only available with lifetime Pin metrics." - openapi.json. These return counts, never comment text or IDs. |
| Ads audience rule "comment" engagement | `engagement_type` includes `comment` | ads:* | - | CONFIRMED but OUT OF SCOPE | "Supported values are `click`, `save`, `closeup`, `comment` and `like`." (audience targeting rule, not comment access) |

Conclusion: there is no official v5 way to read or answer Pin comments. Any Pinterest "inbox" in GeekSocial would have to be built from something else (not available). I do not recommend scraping: the Terms prohibit it ("use the Materials to scrape, collect, search, copy or otherwise access Pinterest Data in unauthorized ways, such as by using automated means").

## 4. NEW-ITEM DELIVERY (webhooks)

| Action | Endpoint | Scope | Access level | Status | Quote+URL |
|---|---|---|---|---|---|
| Webhooks for Pin comments / saves / reactions / new Pins / board events | none | - | - | BLOCKED | Verified absence: no webhook or event-subscription endpoint or doc page among the 305 operations; the changelog titles (Jan-Oct 2026) list none; the spec search for "webhook" finds only the lead-ads subscription text below. |
| Lead Ads webhook subscription | `POST/GET/DELETE /ad_accounts/{ad_account_id}/leads/subscriptions[/{subscription_id}]` | (ads scope) | Ads accounts; OWNER/ADMIN only | CONFIRMED but IRRELEVANT | "Create a lead ads webhook subscription. Subscriptions allow Pinterest to deliver lead data from Ads Manager directly to the subscriber." Also "egress lead data is encrypted with AES-256-GCM." - spec `ad_accounts_subscriptions/post` |
| Signature scheme for webhooks | not documented for organic events | - | - | UNVERIFIED / N/A | No event types or signature header exist for organic content. |
| `POST /notifications` | "Receive notifications from external partners." | user_accounts:read | Gated | CONFIRMED but wrong direction | "Used by third-party partners to send notifications to Pinterest... This API is gated and you need to request access to this feature." - spec `notification/post`. It sends TO Pinterest; it is not a webhook to us. |
| On-demand sync (allowed by our rule) | `GET /pins`, `GET /boards/{id}/pins`, `GET /pins/{id}`, `GET /boards` | pins:read, boards:read | `org_read` | CONFIRMED | Can list the customer's own Pins/boards when a person opens the screen. For apps registered on/after 2026-09-14, reads on personal accounts return 403 (section 6). No comment data is returned. |

## 5. OAuth 2.0

Source: https://developers.pinterest.com/docs/getting-started/set-up-authentication-and-authorization/ and the spec `securitySchemes`/`/oauth/token`.

| Action | Endpoint | Scope | Access level | Status | Quote+URL |
|---|---|---|---|---|---|
| Authorization page | `https://www.pinterest.com/oauth/?client_id&redirect_uri&response_type=code&scope&state` | - | - | CONFIRMED | "Prompt the user to click a link that redirects them to https://www.pinterest.com/oauth/" |
| Redirect URI matching | exact match, no secondary redirect | - | - | CONFIRMED | "Must exactly match the value as registered with Pinterest." and "must not cause the responding server to send a secondary redirect" (connect-app page) |
| PKCE | not mentioned in the docs I read | - | - | UNVERIFIED | The auth guide documents only `code`, `redirect_uri`, `grant_type`, `continuous_refresh`; `OauthAccessTokenCreate` schema has no `code_verifier`. Our BFF (confidential client with secret) is the documented path regardless. |
| Token exchange | `POST /v5/oauth/token` with HTTP Basic (client_id:client_secret), form body `grant_type=authorization_code`, `code`, `redirect_uri` | - | - | CONFIRMED | "Use HTTP Basic Authentication: Username: client_id Password: client_secret" |
| Scopes | boards:read, boards:read_secret, boards:write, boards:write_secret, pins:read, pins:read_secret, pins:write, pins:write_secret, user_accounts:read, user_accounts:write (+ ads:*, billing:*, biz_access:*, catalogs:*, msot:write) | - | - | CONFIRMED | Spec scope list e.g. `"pins:write": "Create, update, or delete your public Pins"`; `"boards:read_secret": "See your secret boards"` |
| Scopes needed to publish | boards:read, boards:write, pins:read, pins:write | | | CONFIRMED | "Generate an access token with the following scopes: boards:read boards:write pins:read pins:write" - creating-boards-and-pins page |
| Scope subset on refresh | optional `scope` param | | | CONFIRMED | "Include this parameter to get an access token with a subset of the scopes originally granted" |
| Access-token lifetime | 30 days (`expires_in` 2592000) | | | CONFIRMED | "Refresh your access token before it expires-–within 30 days (2592000 seconds) after it is issued" |
| Refresh-token lifetime | 60 days, continuous | | | CONFIRMED | "Pinterest only supports the continuous refresh token (60-day expiration, refreshable indefinitely)" |
| Rotation | Each refresh returns a new access token AND a new refresh token with a new `refresh_token_expires_at`. Whether the old refresh token is invalidated at once: not stated. | | | CONFIRMED (new token returned) / UNVERIFIED (old token invalidation) | "A successful request returns a new access token and a continuous refresh token." and "Repeat these steps before 60 days in order to keep the continuous refresh token valid." |
| Continuous refresh, apps created before 2025-09-25 | must send `continuous_refresh=true` | | | CONFIRMED | "Only applicable to apps created before September 25, 2025." Apps created on/after that date get it automatically (our new app does). Legacy 365-day tokens: "no longer supports the legacy refresh token (365-day expiration, hard limit)". |
| Refresh-token expiry consequence | user must re-consent | | | CONFIRMED | "Once the refresh token has expired, you will need to explicitly request access again by repeating the Authorization Code flow." |
| Token invalidation | changed username/password; GitHub secret scanning | | | CONFIRMED | "The most common reasons for token invalidation are: The account user has changed their username or password." Pinterest "does not always notify you". HTTP 401 / API code 2 on expiry/invalid. |
| Token prefixes | access `pina`, refresh `pinr`, client-credentials `pinc` | | | CONFIRMED | auth guide examples |
| Revocation | `POST /oauth/token/revoke` | | | CONFIRMED (limited) | "Only tokens issued for system users are currently supported." - spec. So a customer-disconnect cannot revoke their Pinterest token via API (UNVERIFIED alternative). |
| Client credentials grant | needs 2FA, acts as the app owner | | | CONFIRMED (not for customers) | "designed for situations when the client (your app) is acting on its own behalf, not a user." Use Authorization Code for customers: "Use this grant type if you are building an app to be used by multiple, independent end users, or customers." |
| Token debugger | developers.pinterest.com/docs/developer-tools/token-debugger/ | | | CONFIRMED (link only; page not opened) | |

## 6. ACCESS FOR THIRD-PARTY CUSTOMERS

Sources: https://developers.pinterest.com/docs/key-concepts/access-tiers/ , https://developers.pinterest.com/docs/getting-started/connect-app/ , https://developers.pinterest.com/terms/ , https://community.pinterest.biz/t/understanding-the-pinner-data-access-denied-access-error/51738 (staff post).

| Item | Finding | Status | Quote+URL |
|---|---|---|---|
| Entry | Business account owns the developer app; verify email; accept Developer Terms; submit app details; Trial access reviewed by Pinterest | CONFIRMED | "Login or create a business account. This is the account you will use to administer your app." and "Application requests are reviewed by Pinterest." - connect-app page |
| Trial access | Can read/create boards, Pins; but entities created are private | CONFIRMED | "Creating Pins Yes (visible only to creator)" (access-tiers table) and "all Pins and Boards created with Trial access are only visible to their creator as Sandbox entities." |
| Trial rate limit shape | per day per app | CONFIRMED | "Apps with Trial access are rate limited based on calls per day/per app." |
| Standard access | Required to "offer Pinterest capabilities to your users" | CONFIRMED | "If you want to offer Pinterest capabilities to your users, we recommend upgrading to Standard access" (word is "recommend"; the visibility restriction of Trial makes it effectively required for real publishing) |
| Standard upgrade requirements | App already approved for Trial and compliant; video recording of the app completing an action via the API incl. OAuth flow; verify use case and privacy policy link; upload demo | CONFIRMED | "Prepare a video recording of your app completing an action using the Pinterest API. We will review to verify that you are sending users through the OAuth flow appropriately and not storing any sensitive information." |
| Demo-video denial reasons | must show authentication flow; must show live Pinterest integration, not wireframes; credentials/cookies not acceptable | CONFIRMED | "Demo video does not show live Pinterest integration. ... Wireframes or general platform demos ... may be denied." |
| Trial denial reasons | privacy policy inaccurate/inaccessible; description vague | CONFIRMED | "Privacy policy is inaccurate or not accessible." |
| Review duration | no SLA stated | UNVERIFIED | Docs say only "Standard upgrade requests are reviewed regularly" and "Please allow a few days for teams to review your access requests before following up". |
| Contractual review gate | Application must be approved by Pinterest before distribution to end users; material changes need re-review | CONFIRMED | "You must submit your application, integration, or interface to Pinterest for review and approval prior to distribution to End Users or commencement of use." - Terms s.2 |
| Can a SaaS serve many customers' accounts? | Yes, via Authorization Code; Customers must agree to the terms | CONFIRMED | "You will not provide access to the Materials to any company... (each such entity, your \"Customer\") unless you have ensured that each such Customer has agreed to all applicable Terms and Policies." - Terms s.2. Also "You represent and warrant that you will require each of your Customers ... to abide by the provisions of this Section 4." |
| Customer-facing business-account requirement (NEW) | For apps registered on or after 2026-09-14: HTTP 403 `PINNER_DATA_ACCESS_DENIED` on Get Pin, Get board, List Pins on board, List Pins on board section, List board sections unless approved. Exception: Business account that authorized the app. Staff clarification: personal accounts do not get it; convert to business. | CONFIRMED (docs banner + staff post) | Docs: "If you registered your app on or after September 14, 2026, you may receive HTTP 403 PINNER_DATA_ACCESS_DENIED error when you request a board or Pin without Pinterest's authorization for non-business users." - access-tiers page. Staff: "Personal accounts using apps registered on or after September 14, 2026 won't have this access under the updated policy." - community post (Oct 29, 2026) |
| Does the 403 also hit `GET /boards`, `GET /pins`, and `POST /pins` for personal accounts? | Two developers in the thread report 403 code 6410 on `GET /v5/boards` and `GET /v5/pins` for personal accounts; the staff post's endpoint list names only five reads. `POST /pins` is not mentioned. | UNVERIFIED (settle by live call; `GET /v5/user_account` reportedly still works) | community thread posts #2, #7, #8 |
| Escalation path | "contact your Pinterest account manager"; "Do not retry the same request in a loop" | CONFIRMED | community post. Treat as non-retryable (matches our "never turn unknown into failed / retry only transient" rule). |
| Beta / restricted features | Closed beta needs Pinterest approval; request via Help Center | CONFIRMED | "If you try to call a restricted or closed beta endpoint without having been granted access by a Pinterest representative, you are likely to receive an error response" - https://developers.pinterest.com/docs/getting-started/using-beta-and-restricted-features/ |
| Business Access param | `ad_account_id` lets a token holder act as the owner of an ad account | CONFIRMED | spec text on create Pin. Not needed for our case. |
| App name rules | at least 3 Latin letters/numbers; cannot contain "Pinterest" | CONFIRMED | connect-app page |
| Apps for under-13 | not permitted | CONFIRMED | Developer Guidelines |

## 7. RATE LIMITS

Source: https://developers.pinterest.com/docs/reference/rate-limits/ (plus per-endpoint `x-ratelimit-category` in the spec).

| Category (endpoints) | Trial (requests per day per app) | Standard (requests per minute per user per app) | Status | Quote |
|---|---|---|---|---|
| `org_write` - create/edit/delete boards, sections, Pins (POST/PATCH/DELETE /pins, /boards, /boards/{id}/sections, POST /media, POST /pins/{id}/save, POST /user_account/following) | 300 | 100 | CONFIRMED | "Creating, editing or deleting boards, board sections or Pins." table row `org_write | 300 | 100` |
| `org_read` - GET /pins, /pins/{id}, /boards, /boards/{id}/pins, sections, /media/{id}, /user_account | 1,000 | 1,000 | CONFIRMED | "Fetching user accounts, boards, board sections, or Pins." `org_read | 1,000 | 1,000` |
| `org_analytics` - pin/user analytics | 1,000 | 60 | CONFIRMED (out of scope) | table |
| `ads_read` (also used by GET /user_account/followers and POST /notifications) | 1,000 | 1,000 | CONFIRMED | table |
| Universal caps | Trial: 1000/day all requests. Standard: 100 req/second per user per app | CONFIRMED | "Trial access: 1000 requests per day for all API requests. Standard access: 100 requests per second per user per app for all API requests" |
| Rate-limit headers | `x-ratelimit-limit`, `x-ratelimit-remaining`, `x-ratelimit-reset` | CONFIRMED | Example in rate-limits page: "x-ratelimit-limit: 100, 100;w=1, 1000;w=60" |
| Change policy | "All rate limits are subject to change without notice." Increases: contact Support (API Technical Support). Standard: "Increased rate limits based on activity." | CONFIRMED | same page and access-tiers table |
| 429 | documented response for all endpoints | CONFIRMED | Spec: "The user has sent too many requests in a given amount of time and is being rate limited." |
| Abuse caps | Terms allow throttling/suspension at will | CONFIRMED | "Pinterest may, in its sole discretion, set, modify, and enforce limits on your use of the Materials" - Terms s.2 "API Limitations" |
| Anti-spam | three strikes then app ban | CONFIRMED | "If your app receives three strikes, it will be banned from Pinterest" - https://developers.pinterest.com/docs/key-concepts/best-practices/ |

Spam-prevention guidance from Pinterest (best-practices page): "Implement rate limits for Pin creation", "Limit the creation of duplicate content", "Check for bots". An AI agent bulk-creating similar Pins is the profile this targets.

## 8. DEVELOPER GUIDELINES AND TERMS (exact quotes)

Sources: Developer Guidelines https://policy.pinterest.com/developer-guidelines/ (identical text is Exhibit A in the Terms) and Developer and API Terms of Service (Last Updated: August 2026) https://developers.pinterest.com/terms/ .

| Topic | Rule | Status | Quote |
|---|---|---|---|
| Automation / auto-send | Prohibited to automatically initiate actions without each being specifically considered by the user | CONFIRMED | "Offering features that enable end users to automatically initiate actions without specifically considering each action." and "if your app allows end users to schedule Pin publishing, the end user must choose each Pin to be published." (Guidelines, What not to do) |
| Acting for users | Specific knowledge and consent per action, covers creating Pins and making comments | CONFIRMED | "Taking actions on behalf of end users without their specific knowledge and consent. This includes ... creating, saving and editing Pins; ... sending messages; and making comments." |
| Spirit | | CONFIRMED | "don't build automations that could lessen the authenticity of engagement on Pinterest" |
| Dynamic creative / schedulers allowed | Pin schedulers and tools that "automate the creation of content" are listed as acceptable integrations | CONFIRMED | "Content marketing tools, like Pin schedulers" and "Dynamic creative tools, like apps that automate the creation of content" (What to do) |
| AI-generated content (publishing) | No rule found banning AI-generated Pins; Pin API has an `ai_disclosures` field. Pinterest's GenAI Acceptable Use Guidelines apply to "Pinterest GenAI products", not to your own models. | UNVERIFIED (whether disclosure is mandatory for organic Pins) | https://policy.pinterest.com/en/genai-acceptable-use-guidelines : "These guidelines apply to your use (inputs and outputs) of any Pinterest GenAI products, features, or capabilities" |
| AI/ML training | Prohibited | CONFIRMED | "use the Materials to collect, scan, or otherwise use Pinterest Data to train, fine-tune, or otherwise improve or develop any artificial intelligence or machine learning models, except with Pinterest's express prior written consent (email is sufficient)" (Terms s.2) |
| Sending Pinterest data to a third-party LLM for inference/drafting | Not addressed explicitly. Constraints that bear on it: sharing ban and subcontractor/Intermediary rules (below). Since Pinterest offers no comment data anyway, the practical exposure is the customer's own Pin/board text. | UNVERIFIED (docs silent on inference) | "Don't share or sell information from our API with a third party, including another advertising service. (It's okay to share information with the person from whose account that information came.)" (Guidelines) ; "you are solely responsible for the acts and omissions of any Intermediary Tools ... including any data processed, stored, or transmitted by such Intermediary Tools" and Intermediary Tools explicitly include "Model Context Protocol (MCP) servers, wrappers, plugins, middleware, agents, connectors" (Terms definitions, s.2). Get written confirmation from Pinterest before sending Pinterest Data to an external model API. |
| Data retention / storage | No storing API data (except campaign analytics) without written consent | CONFIRMED | "you may not store any information accessed through any Pinterest Materials including the API. Instead, call the API each time you need to access information." (Guidelines); "you may not store any information accessed through the Materials without Pinterest's express prior written consent" (Terms s.4(a)) |
| Data deletion | All Pinterest Data deleted within 30 days on: termination, Pinterest request, user revokes consent/deletes data/account, no longer necessary, customer stops using service or revokes access, law | CONFIRMED | "You will delete all Pinterest Data in your possession or control ... without undue delay and in no event later than thirty (30) days" (Terms s.4 Data Deletion). Certification of deletion within "ten (10) business days" on request. |
| Combining data | | CONFIRMED | "Don't combine someone's account information with information from other people's accounts" (Guidelines) |
| Use of data | | CONFIRMED | "Only use information from someone's account to provide the services as specified to that person" |
| Credentials | Never collect Pinterest passwords | CONFIRMED | "Do not solicit or collect login credentials or use login credentials to access other people's accounts or take actions on their behalf." |
| Scraping | Prohibited | CONFIRMED | Guidelines and Terms: no "automated means or form of scraping or data extraction" |
| Customers must accept Pinterest terms | | CONFIRMED | Terms s.2 "Responsibility for Your Customers" (quoted in section 6) |
| App review before launch | | CONFIRMED | Terms s.2 "Review" |
| Termination at will | | CONFIRMED | "Pinterest may terminate or suspend your right to access or use the Materials at any time for any reason" (Terms s.5) |
| Privacy policy required | | CONFIRMED | "You must have a privacy policy that's consistent with all applicable laws." |
| Spam | | CONFIRMED | "not offering features that facilitate spamming Pinterest or other users" (Guidelines, app abuse) |

## 9. OPEN ITEMS (only a live call or Pinterest confirmation settles them)

1. Does `POST /pins` (and `GET /boards`, `GET /pins`) succeed for a PERSONAL account authorized by an app registered after 2026-09-14? Staff list only five read endpoints; two developers report 403/6410 on `GET /boards` and `GET /pins`. Test with a fresh personal account and a business account. Settle before promising Pinterest to non-business customers. (Section 6.)
2. Is `image_url` fetched by Pinterest, and does it work with private/signed/short-lived URLs, redirects, or non-public hosts? What is the timeout? Docs silent. (Section 2.)
3. Max base64 request size, and max image pixel dimensions/size accepted by `POST /pins` (the 20/32 MB figures are from the Ads spec page, not the API reference). (Section 2.)
4. Can `POST /pins` with `video_id` be called before `GET /media/{id}` reports `succeeded`? Docs say wait; failure mode untested. (Section 1.)
5. Is the old refresh token invalidated when a new one is issued (rotation semantics)? Concurrency impact for a multi-instance BFF: take a lock around refresh until verified. (Section 5.)
6. Does the OAuth page accept PKCE parameters? Not documented. (Section 5.)
7. Is an `ai_disclosures` declaration mandatory for AI-generated organic Pins? The changelog entry "Disclosing AI content in ads" (June 2026) exists but its body loads client-side and I could not read it. Also the changelog entry "Developer terms and guidelines updated" (August 2026) body unread (the Terms page shows Last Updated: August 2026 and I read the current text). (Sections 1, 8.)
8. Written consent route for: (a) storing Pin/board metadata beyond live calls, (b) sending Pinterest data to a third-party model. Docs say consent is by email to Pinterest; no form found.
9. Standard-access review turnaround (no SLA published) and whether a multi-tenant SaaS demo video must show a second tenant. Unknown.
10. `aggregated_comments` tag in `x-tagGroups` with no operations: confirm with Pinterest whether any comment read API is planned or closed-beta. (Section 3.)
11. Behaviour of a customer who disconnects: token revocation endpoint supports only system users. How to invalidate a user token from our side (versus deleting our copy and the user revoking in Pinterest settings) is undocumented.
12. Sandbox: `GET /boards` in Sandbox "can return an empty items array with a bookmark even when Sandbox boards exist" (Sandbox page) - relevant to Trial testing only.

## URLS OPENED AND WHAT I READ

Opened with curl/browser (full text read unless noted):
- https://github.com/pinterest/api-description (raw `v5/openapi.json`, 2.6 MB, version 5.28.0; `README.md`; GitHub commits API for the last commit date 2026-06-04) - full path list, security scopes, `x-ratelimit-category`, Pin create schema, media schema, oauth/token descriptions, tags, x-tagGroups, comment/webhook searches.
- https://developers.pinterest.com/docs/api/v5/pins-create (HTML with embedded spec + `apiRefPaths` nav tree of 305 operations) - confirmed the live reference matches the repo and has no comments/webhook operations.
- https://developers.pinterest.com/docs/api/v5/ (HTML) - searched for comment/webhook strings.
- https://developers.pinterest.com/docs/getting-started/set-up-authentication-and-authorization/ - OAuth flow, scopes, tokens, refresh, grant types.
- https://developers.pinterest.com/docs/api-features/creating-boards-and-pins/ - board/Pin endpoint list, volume limits, video upload steps, Pin parameters.
- https://developers.pinterest.com/docs/getting-started/connect-app/ - registration, Trial request, redirect URIs, test tokens (24h).
- https://developers.pinterest.com/docs/key-concepts/access-tiers/ - Trial vs Standard, demo video, denial reasons, PINNER_DATA_ACCESS_DENIED banner.
- https://developers.pinterest.com/docs/reference/rate-limits/ - category table and headers.
- https://developers.pinterest.com/docs/getting-started/using-beta-and-restricted-features/ - restricted/closed beta access.
- https://developers.pinterest.com/docs/developer-tools/sandbox/ - sandbox endpoint list (updated Sep 8, 2026), group-board warning, `/notifications` entry.
- https://developers.pinterest.com/docs/key-concepts/best-practices/ - spam/strikes, scheduled publishing, ID guidance.
- https://developers.pinterest.com/docs/changelog/changelog/ - entry titles for 2026 only (bodies not retrievable); grep for comment/webhook/reaction: none.
- https://developers.pinterest.com/terms/ (read in a browser) - full Developer and API Terms of Service (Last Updated: August 2026) with Developer Guidelines as Exhibit A.
- https://policy.pinterest.com/developer-guidelines/ - Developer Guidelines (same text as Exhibit A).
- https://policy.pinterest.com/en/genai-acceptable-use-guidelines - scope is Pinterest's own GenAI tools.
- https://help.pinterest.com/business/article/pinterest-product-specs/ - Pinterest Business Help "Review ad specs": formats, sizes, ratios, character limits (ad spec page; says formats can also serve organic).
- https://community.pinterest.biz/t/understanding-the-pinner-data-access-denied-access-error/51738.md - Pinterest staff post (Oct 1, 2026) and thread through Oct 30, 2026. This is a community forum, Pinterest-operated, not the docs; I label it as such.

Tried and failed / not usable:
- WebFetch on developers.pinterest.com pages returned only navigation (client-rendered); replaced by curl.
- https://developers.pinterest.com/docs/changelog/ entries' bodies (e.g. "Pinner data access denial error", "Disclosing AI content in ads", "Developer terms and guidelines updated") are not in the server-rendered HTML.
- https://policy.pinterest.com/en/developer-terms (and variants) returned 404; the Terms live at developers.pinterest.com/terms/.
- https://developers.pinterest.com/docs/developer-tools/token-debugger/ not opened.
