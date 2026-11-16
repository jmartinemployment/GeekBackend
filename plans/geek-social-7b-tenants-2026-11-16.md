# GeekSocial Gate 7b: tenants and the onboarding gate (design, no code yet)

Written 2026-11-16 from the code as it stands. Decisions it rests on are in `docs/PLATFORM-CAPABILITIES.md`
sections 1a and 1b (Jeff, 2026-11-16): tenants own their accounts, a person approves every reply and post,
stored comments are purged at 30 days, a tenant's content reaches the AI only for that tenant and only after that
tenant accepted the AI terms, and every tenant registers their own platform apps.

## What exists today

- `geek_social` has five tables (`social_accounts`, `social_posts`, `engagement_events`, `reply_drafts`,
  `action_audit`, migration 0043) with no owner on any row. `ISocialStore` has 25 methods and none takes a tenant.
- The signed-in user is a `Guid` from `ICurrentUserContext.UserId`; `SocialController` already passes `UserId()` to
  the connect and send actions, but only as an actor, never as a filter.
- Other products already have tenants, each in its own schema: `geek_crm.tenants` with `tenant_users` (user id to
  tenant, owner or member, forced row-level security on `tenant_id` read from the `app.tenant_id` setting) and a
  separate `geek_voice.tenants`. A tenant table per product is the established pattern, so `geek_social.tenants` as
  Jeff specified is consistent with it.
- Nothing in GeekAPI reads an email-confirmed or phone-confirmed claim.

## The change

1. **Migration 0064.** `geek_social.tenants` (`id`, `owner_user_id`, `name`, `ai_consent_accepted_at`,
   `created_at`; `owner_user_id` unique). `tenant_id uuid NOT NULL REFERENCES geek_social.tenants` on all five
   tables, with the existing unique indexes widened where a row is per tenant. Same forced row-level security on
   `tenant_id` as `geek_crm`, so a repository bug cannot leak across tenants.
2. **Store and repository.** Every `ISocialStore` method takes the tenant and filters on it; a draft, event, post or
   account of another tenant answers not found, never forbidden. The webhook intake finds the tenant from the
   account the platform names, since it has no signed-in user.
3. **Resolving the tenant.** A signed-in request maps `UserId` to the tenant it owns. No tenant answers a clear
   "finish sign-up" refusal; it is never created implicitly on first read.
4. **Onboarding gate.** Before connecting an account or generating a draft, a tenant must have a confirmed email, a
   confirmed phone and `ai_consent_accepted_at` set. Each missing item is named in the refusal.
5. **Retention (a separate change in the same gate).** `text_expires_at` becomes 30 days for every platform
   (48 hours for LinkedIn member comments), and a draft is deleted with the text it answers. The purge removes
   whole rows at 30 days, as decided in section 1a.
6. **Tests.** One tenant cannot read, list, edit, approve, send, reject or purge another's account, draft, event,
   post or audit row, through the store, the controllers and a direct query under the row-level policy.

## Not part of 7b

Removing auto-send (`AutoSendCategories`, the orchestrator's auto-send step, the screen's settings) and the
connectors for Instagram, LinkedIn, Google Business Profile, X and TikTok. Each is its own later step.

## What this design cannot decide without Jeff

1. **Existing rows.** If production holds an account, draft or event, it needs a tenant. The backfill needs the
   GeekOAuth user id (the JWT `sub`, a Guid) of the person who owns them. If production holds only test data, the
   rows can be deleted instead. Neither is known from the code.
2. **Where "confirmed email and phone" comes from.** GeekAPI sees no such claim. GeekOAuth owns the two-factor and
   phone work in another session, so either it puts email-verified and phone-verified claims in the token, or
   GeekAPI asks it. Which, and what the claims are called, is that session's decision.
3. **Which migration number.** 0063 is the latest; another session may be writing 0064, so the number is confirmed
   at the time the file is written.
