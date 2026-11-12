# GeekSocial handoff, 2026-11-12

Where GeekSocial stands and what is left to turn it on. Written at the end of a session in which Jeff
was setting up the Meta app. Nothing in this file was committed or pushed.

## What it is

GeekSocial watches comments on connected Facebook Pages, has Claude classify each comment and draft
a reply, and holds every draft for a person to approve, edit or reject before it is sent. It also
publishes link posts. It runs inside GeekAPI (`GeekAPI/Services/Social`, routes `api/social/*`, hub
`/hubs/social-realtime`) with storage in GeekRepository. The operator screen is its own repository,
`/Users/jeffmartin/development/geek-social` (Next.js, port 3006, pushed to
github.com/jmartinemployment/geek-social, no Vercel project yet).

Contract and settings: `docs/GEEK-SOCIAL-API.md`. Screen README: `geek-social/README.md`.
Plan: `geekatyourspot/plans/geek-social-engagement-agent-2026-11-08.md`.

## What works today

- Facebook Pages only. Connect, comment intake by webhook and resync, Triage, drafter, Reviewer,
  approval, send, auto-send by category, send limits, audit, post copy and publish.
- None of it has run against a real Page. Phase 6 (proof on the real Page) is the next gate.
- Nothing reaches Claude until `GEEK_SOCIAL_CLAUDE_CLEARED_PLATFORMS` is set. Nothing connects until
  the `GEEK_SOCIAL_META_*` settings are on GeekAPI.

## Other platforms: not built

| Platform | State |
|---|---|
| Instagram | Post-writing code exists (picture plus caption). No connector. Needs Instagram permissions on the Meta app and a Professional account linked to the Facebook Page. Exact permissions not yet checked against Meta's current docs. |
| LinkedIn (company Page, member) | Post-writing code exists. No connector. Needs a LinkedIn developer app associated with the company Page and Community Management API approval. Only Jeff can request that. Member posting is publish-only. |
| Google Business Profile | Not in code or plan. Has an API for posts and review replies, with access approval. It deals in reviews, not comments. Unchecked against Google's current rules. |
| Substack | Not in code or plan. No official public API for posting or comments, as far as is known. Unverified. |
| X, TikTok, others | Not planned. Personal Facebook profiles are out (no API). |

A new platform is a new connector in GeekAPI plus its own entry in
`GEEK_SOCIAL_CLAUDE_CLEARED_PLATFORMS`. Triage, drafts, Reviewer, approvals, limits, audit and the
screen are platform-neutral.

Jeff's position at the end of the session: Facebook alone is not enough, and he is putting together a
complete list of platforms first. Work from his list when he gives it.

## Meta app

- App: `geekatyourspot-app`, App ID `1952063012160616`, in development mode, unpublished, owned by
  the Geek At Your Spot LLC business portfolio (business id `2363247697821885`). A second app,
  `testing`, also exists and is not used.
- App ID and App secret: developers.facebook.com, the app, App settings, Basic. The secret needs
  Jeff's Facebook password to show. Do not paste it into chat.
- Which Facebook Page to connect is not settled. The plan mentions a Facebook test Page. Connect
  lists the Pages Jeff administers; he chooses. Check business.facebook.com, Settings, Accounts,
  Pages.

## Railway variables on GeekAPI

GeekAPI: project `de6ac613-03cf-401d-b01a-cadc6042deb5`, service
`a4a5d12c-6d54-4b5b-92ae-5339d7d94ee3`, environment `b264a128-d2ad-4989-970d-e85294f9b4dc`.

| Variable | State at end of session |
|---|---|
| `GEEK_SOCIAL_META_APP_SECRET` | Jeff added it in the Railway dashboard and a deploy was running. Not verified. |
| `GEEK_SOCIAL_ENCRYPTION_KEY` | Not set. 32 random bytes, base64 (`openssl rand -base64 32`). The code requires base64 of exactly 32 bytes (`SocialCredentialProtector.cs:121`); `-hex` fails. A value was generated this session and is in that session's transcript, so treat it as exposed. If no Page is connected yet, generate a fresh one instead. Never change it once a Page is connected, or stored tokens cannot be read. |
| `GEEK_SOCIAL_META_APP_ID` | Not set. `1952063012160616`. |
| `GEEK_SOCIAL_CLAUDE_CLEARED_PLATFORMS` | Not set. `facebook`. |
| `GEEK_SOCIAL_META_VERIFY_TOKEN` | Not set. Any string; the same string goes in the Meta app's webhook form. |
| `GEEK_SOCIAL_META_LOGIN_CONFIG_ID` | Not set. Created in the Meta app under Facebook Login for Business, Configurations, with `pages_show_list`, `pages_manage_metadata`, `pages_read_engagement`, `pages_read_user_content`, `pages_manage_posts`, `pages_manage_engagement`, user access token type. The Facebook Login for Business product may need adding to the app first. |
| `ANTHROPIC_API_KEY`, `LlmProviders__Anthropic__Model` | Not checked. |

Claude could not set these: the Railway write was refused by the permission system (secret-store
writes). Jeff sets them in the dashboard, or adds a permission rule for
`mcp__railway__set-variables` in Claude Code settings.

## Meta app settings still to do (Jeff only)

1. Webhooks: object Page, field `feed`, callback `https://api.geekatyourspot.com/api/social/webhooks/meta`,
   the verify token above. Set the Railway variables first; Meta checks the callback when it is saved.
2. Facebook Login for Business, Valid OAuth Redirect URIs:
   `http://localhost:3006/connect/meta/callback`, later
   `https://geek-social.geekatyourspot.com/connect/meta/callback` and
   `https://geek-social.vercel.app/connect/meta/callback`.
3. GeekOAuth client `geek-social`: register `http://localhost:3006/auth/callback` and
   `http://localhost:3006/signed-out`.

## Next steps, in order

1. Set the remaining Railway variables (one deploy covers them all).
2. Finish the Meta app: login configuration, webhook, redirect URIs.
3. Register the local addresses on the GeekOAuth `geek-social` client.
4. `cd /Users/jeffmartin/development/geek-social && cp .env.example .env.local && npm install && npm run dev`,
   sign in at http://localhost:3006, connect the Page on `/accounts`, check `/activity`.
5. Phase 6: comment on a post, watch the draft appear, edit and approve it. Leave auto-send off until
   a few drafts have been seen.
6. Jeff: request LinkedIn Community Management API access now; the approval is the slow step.
7. After Facebook is proven: build the Instagram connector, then LinkedIn when approved, then
   whatever else is on Jeff's platform list.

## Before any deploy

A GeekBackend push or a GeekAPI redeploy kills a running Generate and makes the crawler delete a
running crawl. Check GeekRepository's http log first: `@method:POST` over the last few minutes
(a running Generate posts `generate-jobs/{id}/events`), and `@path:/repo/geek-crawler/pages/batch`
(any hit means a crawl is saving). GeekRepository: project `9158e532-6246-4a96-bdf1-2c4b9bee9a9f`,
service `6ee454ac-af10-429a-bf23-dc83ac9f532d`, environment `41de402e-f5ab-4b20-9bd7-66b076a138c4`.

At 2026-11-12 about 10:00 UTC no Generate or crawl was running (last crawl save 2026-10-29). GeekAPI
had started at 09:39, 09:47 and 09:56 UTC for a reason not identified; not caused by this session.

## Notes

- Jeff was frustrated by the length of the Meta setup. Give the exact URL first, one step at a time,
  and name labels as they appear on screen.
- Jeff is on a Claude plan with limited usage and does not want two sessions running at once.
- The model on the session that wrote this was Claude Sonnet 5.5.
