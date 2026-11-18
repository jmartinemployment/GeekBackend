# GeekSocial connector contract

Roadmap Phase 0, item 11 (`geek-social/plans/geek-social-roadmap-2026-11-18.md`). What every network's connector
must do across the life of a connection, and the Facebook connector, the one that is built, checked clause by
clause against the code as of GeekBackend `dd6f20aa` (2026-11-18). Each clause says **Met** or **Gap**. A gap is
recorded here and built in the phase that owns it; this document changes no code.

GeekSocial owns one app on each network, and a tenant connects its accounts by signing in through it. So the
connector code is shared by every tenant, and a defect in it reaches all of them.

The pieces:
- `ISocialAccountConnector` (`GeekApplication/Interfaces/Social/SocialInterfaces.cs`): one per platform, registered
  by platform key. It builds the consent address, trades the code for the accounts behind the sign-in, renews and
  checks a token, and subscribes an account to the platform's events.
- `SocialConnectService` (`GeekAPI/Services/Social/Connector/SocialConnectService.cs`): the start and complete
  routes' flow, shared by every platform.
- `SocialTokenProvider` (`GeekAPI/Services/Social/Connector/SocialTokenProvider.cs`): the only component that
  decrypts a stored token; it renews one near its expiry and marks an account `reconnect_required`.
- `SocialCredentialProtector` (`GeekAPI/Services/Social/SocialCredentialProtector.cs`): AES-256-GCM with
  `GEEK_SOCIAL_ENCRYPTION_KEY`, held by GeekAPI only, so GeekRepository stores ciphertext.
- `MetaAccountConnector` (`GeekAPI/Services/Social/Connector/MetaAccountConnector.cs`): the Facebook Page connector.

## 1. Authorize

Every connector must:
- send the person to the platform's own consent screen, never collect a platform password;
- carry a state that only the person who started can complete, for this platform and this redirect address, and
  only for a short time;
- refuse before any platform call when the app's settings are missing, naming the missing settings.

Facebook: **Met.**
- The state binds platform, user, redirect address, issue time and a nonce, sealed with ASP.NET Data Protection,
  and lives 15 minutes (`SocialConnectService.cs:34`, `:65-79`). Complete refuses a state started by another
  person (`:118-123`); that refusal is covered for every tenant by
  `SocialRouteTenantIsolationTests.A_connection_another_tenants_person_started_is_refused_before_the_code_is_exchanged`.
- The consent address is Facebook Login for Business with a configuration id (`MetaAccountConnector.cs:50-70`).
- Missing settings fail `not_configured` with the variable names (`:288-295`). On 2026-11-18 GeekAPI has the app id
  and secret but not `GEEK_SOCIAL_META_VERIFY_TOKEN` or `GEEK_SOCIAL_META_LOGIN_CONFIG_ID`, so no Page can connect
  (Activity says so).

Known limit: the Data Protection keys are not persisted (`SocialServiceRegistration.cs:135`). A state issued before
GeekAPI restarts is refused with "expired, altered or from before a restart" (`SocialConnectService.cs:96-104`), so a
deploy during someone's connect makes them start again. It is shown to them, not silent.

## 2. Discover the accounts behind a sign-in

Every connector must:
- list every account the sign-in granted, page through the whole list, and stop with a stated limit rather than
  keep some silently;
- check the grant carries every permission the agent needs, and refuse naming the missing ones;
- refuse a sign-in that granted no account;
- never let one platform account belong to two tenants.

Facebook: **Met, with one gap.**
- `me/accounts` is paged 100 at a time up to 1,000 Pages, and more fails `invalid_request` naming the limit
  (`MetaAccountConnector.cs:119-167`). No Page granted fails (`:169-171`).
- `me/permissions` must hold all six of `RequiredPermissions` (`:21-29`, `:109-117`).
- One account belongs to one tenant: another tenant's Page answers `conflict` and is never named
  (`GeekRepository/Repositories/Social/SocialRepository.cs:213-215`).
- **Gap:** the Page's `tasks` are requested (`MetaAccountConnector.cs:125`) and not checked. A person whose role on
  a Page does not allow moderating or posting connects it, and the first reply or post fails `forbidden`.

## 3. Store the token encrypted

Every connector must hand back the token in memory only; the shared flow encrypts it before it reaches the store,
and no log line, audit row or answer carries it.

Facebook: **Met.** Each Page token is sealed by `SocialCredentialProtector` before `ConnectAccountAsync`
(`SocialConnectService.cs:134-150`). A missing or malformed key fails `encryption_unavailable` and nothing is
stored (`SocialCredentialProtector.cs:48`, `:84`).

Known limit: there is one key and no rotation. Changing `GEEK_SOCIAL_ENCRYPTION_KEY` makes every stored token
unreadable (`encryption_unavailable` on each use) until each account is connected again.

## 4. Subscribe to the platform's events

Every connector must subscribe each connected account to the events GeekSocial reads, read the subscription back,
and report a failed subscription without undoing the connection. Inbound deliveries are verified before anything
is stored (roadmap, "Inbound events").

Facebook: **Met.** Each Page's `feed` field is subscribed and read back, and only this app's entry counts
(`MetaAccountConnector.cs:245-286`). A failed subscription leaves the Page connected, is audited and is returned
with its reason (`SocialConnectService.cs:163-178`). Deliveries are checked against `X-Hub-Signature-256` first
(`MetaEngagementReader.cs:25`, `SocialWebhooksController.cs:56`, `:76`).

## 5. Refresh

Every connector must renew a token that expires before it does, store the new one before using it, and say
`not_supported` when the platform has no renewal.

Facebook: **Met.** A Page token made from a long-lived user token has no expiry, so `RefreshTokenAsync` answers
`not_supported` (`MetaAccountConnector.cs:197-199`) and the token provider returns the stored token as is
(`SocialTokenProvider.cs:71-72`, `:95-96`). Renewal starts 7 days before an expiry (`:29`) and runs only when a token
is asked for, never on a timer (`:7-13`). This path is exercised by tests only: no platform with expiring tokens is
built yet.

## 6. Detect revocation and permission changes

Every connector must, when the platform refuses the grant (revoked, expired, a permission or role removed):
- mark the account `reconnect_required`, so nothing else is sent from it and the Accounts screen asks for a
  reconnect;
- audit it;
- tell the operator which account and why.

Facebook: **Gap.**
- `MarkReconnectRequiredAsync` is called only by `SocialTokenProvider` (`SocialTokenProvider.cs:131`), on an
  expired token or a refused renewal. A Facebook Page token never expires and is never renewed, so it never takes
  that path.
- When Meta refuses the token during a reply (error 190 or 102, mapped to `token_invalid` in
  `MetaGraphClient.cs:168-170`) or a permission (code 10 or 200 to 299, mapped to `forbidden` at `:178-180`), the
  draft fails with that code (`SocialSendStep.cs:185`) and the account stays `connected`. Each failed draft is
  shown, so nothing is silent, but the account's state is wrong: the reconnect notice never appears, and every
  later reply, post and reaction fails the same way.
- Permissions are checked only at connect (`MetaAccountConnector.cs:109-117`). `GetTokenStatusAsync`
  (`debug_token`, `:206-243`) exists and nothing calls it.
- Meta's deauthorize callback, sent when a person removes the app, is not received: no route exists. What Meta
  requires for it and for data deletion is recorded in the platform access register.

The fix this contract asks for: a platform answer of `token_invalid`, or `forbidden` for a missing permission, on
any write (reply, publish, react) or read (resync) marks the account `reconnect_required` through the store, audits
it, and then fails as it does now. It belongs in the shared sender, not in each connector, so every platform gets
it.

## 7. Reauthorize

Every connector must let the same tenant connect an account again through the same flow, replacing the token and
returning the account to `connected`, without creating a second account.

Facebook: **Met.** The repository finds the account by platform and platform id, keeps its id, and replaces the
name, status and token (`SocialRepository.cs:200-241`). The Accounts screen offers the same button for a reconnect.

## 8. Disconnect

Every connector must let a tenant disconnect an account: GeekSocial stops reading and sending for it, revokes or
forgets the token, unsubscribes its events where the platform allows, and deletes what the retention rules say
(roadmap, "Retention"). The same must happen when the platform tells GeekSocial the person removed the app.

Facebook: **Gap.** There is no disconnect route, no unsubscribe, and no receiver for Meta's deauthorize or
data-deletion callbacks. A tenant can pause sends on an account (`PUT accounts/{id}/settings`) but cannot remove it,
and its token and rows stay until the 30-day purge, which itself is not built for Facebook comments.

## 9. Failures, at every stage

Every connector and the shared flow must fail loud (CLAUDE.md section 2): a step that cannot complete returns its
code and message to the caller or reaches the operator, never only a log line.

Facebook: **Met, with one gap.**
- Every connector call returns a `Result` with a failure code, and the connect flow returns it to the screen.
- **Gap:** an audit row that cannot be stored during connect is logged and dropped
  (`SocialConnectService.cs:206-207`). The connection itself is reported correctly, but the record of it is lost
  without the operator seeing it. Keeping it as a step failure, which Activity already shows, would make it loud.

## Gaps, most harmful first

| # | Gap | Effect | Where it is built |
|---|---|---|---|
| 1 | A refused token or permission does not mark the account `reconnect_required` (section 6) | The account shows connected while every reply, post and reaction fails | Before the first tenant connects a Page |
| 2 | No disconnect, no deauthorize or data-deletion callback (section 8) | A tenant cannot remove an account; a person who removes the app on Meta leaves the token and rows behind | Before Meta App Review (item 9), since the register records what Meta requires |
| 3 | Page tasks not checked at connect (section 2) | A Page connects and its first write fails `forbidden` | With gap 1 |
| 4 | Connect audit failures logged only (section 9) | The record of a connection can be lost unseen | With gap 1 |
| 5 | Data Protection keys not persisted (section 1) | A deploy during a connect makes the person start again | Known limit, shown to the person |
| 6 | One encryption key, no rotation (section 3) | Changing the key strands every stored token | Before any key change |

Each new connector (Instagram, LinkedIn, Google Business Profile and the rest) is checked against sections 1 to 9
before it ships, and its entry in the platform access register names its API version and the date it was last
verified.
