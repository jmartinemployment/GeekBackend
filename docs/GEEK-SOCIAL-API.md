# GeekSocial API (GeekAPI, `api/social/*`)

The routes the GeekSocial screen calls. Every route below needs a GeekOAuth access token carrying
the scope `geeksocial.manage` (policy `GeekSocialManage`); a token without it gets 403. The webhook
routes under `api/social/webhooks/*` are the platforms' and are not listed here.

JSON is camelCase. Times are UTC ISO 8601. Every failure is answered with

```json
{ "error": "what went wrong, for a person to read", "code": "a SocialFailureCodes value" }
```

and this status: `invalid_request` 400, `forbidden` 403, `not_found` 404, `conflict` 409,
`not_supported` 422, `rate_limited` 429, `token_invalid` 409, `clearance_required` 409, anything else
503. A Reviewer refusal is 422 with code `review_refused` and the rules broken:

```json
{ "error": "The text breaks: em_dash, unapproved_link.", "code": "review_refused", "violations": ["em_dash", "unapproved_link"] }
```

No route ever returns a token. Comment text and commenter names appear only in the comment views,
and are null once their retention has run out.

## Status

`GET api/social/status`

```json
{
  "orchestrator": { "recoveredAt": "...", "eventsRecoveredAtStartup": 0, "interruptedSendsFailed": 0, "eventsScrubbed": 0,
    "eventsTakenIn": 0, "commentsReplyNeeded": 0, "commentsNoReplyNeeded": 0, "commentsBlockedNoClearance": 0,
    "commentsFailed": 0, "draftsPending": 0, "draftsForAPerson": 0, "draftsRefused": 0, "draftsAutoApproved": 0,
    "draftsSent": 0, "draftsSendFailed": 0, "sendsHeld": 0,
    "failures": [ { "step": "create a draft", "code": "store_unavailable", "at": "..." } ] },
  "clearedPlatforms": ["facebook"],
  "metaConfigured": true,
  "sendLimits": { "repliesPerAccountPerHour": 30, "coolDownMinutes": 15 }
}
```

## Accounts

`GET api/social/accounts` returns a list of accounts:

```json
{ "id": "uuid", "platform": "facebook", "platformAccountId": "1234", "displayName": "Geek @ Your Spot",
  "status": "connected | reconnect_required", "autoSendCategories": ["praise"], "sendsDisabled": false,
  "tokenExpiresAt": null, "connectedAt": "..." }
```

`PUT api/social/accounts/{id}/settings` with `{ "autoSendCategories": ["praise", "question"], "sendsDisabled": false }`
returns the account. `complaint` and `sensitive` can never auto-send (400).

`POST api/social/accounts/{id}/resync` reads the comments under the account's stored posts and stores
any the webhooks missed. Returns `{ "postsRead": 3, "commentsRead": 40, "stored": 2 }`.

## Connecting a Facebook Page

1. `POST api/social/connect/meta/start` with `{ "redirectUri": "https://geek-social.geekatyourspot.com/connect/meta/callback" }`
   returns `{ "authorizationUrl": "https://www.facebook.com/..." }`. Send the browser there.
2. Meta sends the browser back to `redirectUri` with `?code=...&state=...` (or `?error=...`).
3. `POST api/social/connect/meta/complete` with `{ "code": "...", "state": "...", "redirectUri": "<the same address>" }`
   returns `{ "accounts": [ <account>, ... ], "subscriptions": [ { "accountId": "uuid", "active": true } ] }`.

The state is valid for 15 minutes and only for the signed-in user who started it. The redirect
address must be listed under "Valid OAuth Redirect URIs" in the Meta app.

## Inbox (reply drafts)

A draft view:

```json
{
  "id": "uuid", "accountId": "uuid", "platform": "facebook", "category": "question",
  "draftText": "Thanks for asking...", "needsHumanReason": null, "reviewRefusal": null,
  "status": "pending | approved | rejected | sending | sent | failed",
  "sentPlatformCommentId": null, "failureCode": null,
  "createdAt": "...", "decidedAt": null, "sendingStartedAt": null,
  "comment": { "eventId": "uuid", "text": "the comment, or null once scrubbed", "authorDisplayName": "or null",
               "platformPostId": "...", "platformCommentId": "...", "occurredAt": "..." }
}
```

- `GET api/social/drafts?status=pending&accountId=<uuid>&limit=50`: newest first. `status` and
  `accountId` are optional; `limit` is 1 to 200, default 50.
- `GET api/social/drafts/{id}`
- `PUT api/social/drafts/{id}` with `{ "draftText": "..." }`: only a pending draft. The Reviewer checks
  the new text first; a refusal is 422 `review_refused` and nothing is saved. Returns the draft view.
- `POST api/social/drafts/{id}/approve`: a pending draft with text that passes the Reviewer is
  approved and sent at once. Returns `{ "draft": <draft view>, "send": <send outcome> }`.
- `POST api/social/drafts/{id}/send`: sends an approved draft that a limit held back. Returns
  `{ "draft": <draft view>, "send": <send outcome> }`.
- `POST api/social/drafts/{id}/reject`: pending or approved to rejected. Returns the draft view.

A send outcome:

```json
{ "sent": true, "held": false, "code": null, "error": null, "retryAfter": null }
```

`held` is true when a limit kept an approved draft back (`code` is `rate_limited`, `retryAfter` says
when). The draft stays approved and is sent at the next wake, or by `send`. A send that reached the
platform and failed leaves the draft `failed` with its code, and it is never sent again.

## Comments

A comment view:

```json
{ "id": "uuid", "accountId": "uuid", "platform": "facebook", "kind": "comment_created",
  "platformPostId": "...", "platformCommentId": "...", "authorDisplayName": "or null", "text": "or null",
  "occurredAt": "...", "triageOutcome": "blocked_no_clearance", "triageFailureCode": null, "triagedAt": "..." }
```

- `GET api/social/comments?state=blocked_no_clearance&accountId=<uuid>&limit=50`: `state` is one of
  `untriaged`, `reply_needed`, `no_reply_needed`, `blocked_no_clearance`, `failed` (required).
- `POST api/social/comments/{id}/rerun`: runs Triage again on a held or failed comment. Returns
  `{ "outcome": "reply_needed" }` (or the outcome it reached).
- `POST api/social/comments/{id}/react` with `{ "reactionType": "LIKE" }`: Facebook takes `LIKE` only.
  Returns `{ "platformReactionId": null }`.

## Posts

- `GET api/social/posts?accountId=<uuid>&limit=50` returns stored posts (newest first). `accountId` is required:
  `{ "id", "accountId", "platform", "platformPostId", "kind", "text", "linkUrl", "scheduledFor", "publishedAt", "createdAt" }`.
- `POST api/social/posts/copy` with
  `{ "articleUrl": "https://...", "articleTitle": "...", "articleSummary": "...", "platforms": ["facebook"], "imageUrl": null }`
  returns `{ "copies": [ { "platform": "facebook", "post": { "kind": "link", "text": "...", "linkUrl": "...",
  "linkTitle": "...", "linkDescription": "...", "imageUrl": null, "scheduledFor": null } } ] }`. A suggestion
  only: nothing is stored or posted.
- `POST api/social/posts` with `{ "accountId": "uuid", "post": { "kind": "link", "text": "...", "linkUrl": "...",
  "linkTitle": null, "linkDescription": null, "imageUrl": null, "scheduledFor": null } }` checks the text
  with the Reviewer (422 `review_refused`), publishes it once and returns the stored post.

## Audit

`GET api/social/audit?accountId=<uuid>&limit=50` returns one row per call that changed something on a platform:

```json
{ "id": "uuid", "accountId": "uuid", "platform": "facebook", "action": "reply", "outcome": "succeeded | failed | outcome_unknown",
  "platformObjectId": "...", "replyDraftId": "uuid", "failureCode": null, "failureDetail": null,
  "actorUserId": "sub of the person, or null when auto-sent", "occurredAt": "..." }
```

## Live updates (SignalR)

Hub: `/hubs/social-realtime` on GeekAPI. Connect with the access token (`accessTokenFactory`); the
hub requires the same scope. Messages carry ids and states only, never text. Read the text through
the routes above.

| Message | Body |
|---|---|
| `draftChanged` | `{ "draftId": "uuid", "accountId": "uuid", "status": "pending" }` |
| `commentChanged` | `{ "eventId": "uuid", "accountId": "uuid", "outcome": "blocked_no_clearance" }` |
| `accountChanged` | `{ "accountId": "uuid", "status": "connected" }` |

## Settings on GeekAPI (Railway)

| Variable | What it is | When it is missing |
|---|---|---|
| `GEEK_SOCIAL_ENCRYPTION_KEY` | 32 random bytes, base64. Encrypts each Page token before GeekRepository stores it. | Connecting and sending fail `encryption_unavailable`. |
| `GEEK_SOCIAL_META_APP_ID` | The Meta app's id. | Connecting fails `not_configured`. |
| `GEEK_SOCIAL_META_APP_SECRET` | The Meta app's secret. Signs every webhook delivery. | Deliveries are refused and connecting fails `not_configured`. |
| `GEEK_SOCIAL_META_VERIFY_TOKEN` | Any string, typed into the Meta app's webhook form as well. | The webhook handshake is refused. |
| `GEEK_SOCIAL_META_LOGIN_CONFIG_ID` | The Facebook Login for Business configuration (user access token type) that grants `pages_show_list`, `pages_manage_metadata`, `pages_read_engagement`, `pages_read_user_content`, `pages_manage_posts`, `pages_manage_engagement`. | Connecting fails `not_configured`; a configuration missing a permission fails `forbidden` and names it. |
| `GEEK_SOCIAL_CLAUDE_CLEARED_PLATFORMS` | Comma list of platforms whose comment text may go to Claude, e.g. `facebook`. | Every comment is held `blocked_no_clearance`. A name that is not a platform stops GeekAPI. |
| `ANTHROPIC_API_KEY`, `LlmProviders__Anthropic__Model` | Claude, for Triage, the drafter and the post writer. | Each call fails `not_configured`, naming the setting. |

## In the Meta app

- Webhooks, object Page: callback `https://api.geekatyourspot.com/api/social/webhooks/meta`, the verify
  token above, field `feed`. Connecting a Page subscribes it; the callback itself is set here once.
- Facebook Login for Business, "Valid OAuth Redirect URIs": `https://geek-social.geekatyourspot.com/connect/meta/callback`
  (and `https://geek-social.vercel.app/connect/meta/callback`, `http://localhost:3006/connect/meta/callback`
  for testing).

## Limits

A send is one attempt and is never retried. An account sends at most 30 replies in any hour, and
sends nothing for 15 minutes after a send of its was throttled, failed on Meta's side or may not
have landed. A draft a limit holds stays approved and goes at the next event taken in, or with Send.
Auto-send applies only to the categories an account chose, never complaint or sensitive, and only to
text the Reviewer passed. A comment the Page itself wrote is never triaged.
