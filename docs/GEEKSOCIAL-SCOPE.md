# GeekSocial scope

GeekSocial is a social media management product for small businesses. It is paid, multi-tenant and multi-platform: never a Facebook-only tool. The scope comes from research into 22 tools, in `docs/market-research-2026-11-18/`. The plan of work, with its phases and open decisions, is `geek-social/plans/geek-social-roadmap-2026-11-18.md` (revision 3, frozen 2026-11-18).

## Requirements

- **Connecting accounts:** GeekSocial owns the app on each network, and a tenant connects by signing in, as the researched tools do. Onboarding is a multi-step setup form.
- **Networks:**
  - Facebook, Instagram, LinkedIn (page and profile), Google Business Profile, X, TikTok, YouTube and Pinterest.
  - Nextdoor: posts are planned and approved in GeekSocial. There is no Share Plugin; publishing goes through Nextdoor's API once access exists.
- **Email:** what Constant Contact and Mailchimp do: subscriber lists, campaigns, sending, and email ↔ social repurposing.
- **Billing:** a paid SaaS through PayPal.
- **AI:**
  - **Replaceable provider:** GeekSocial reaches its AI through one interface, and the provider is chosen by configuration: Anthropic, OpenAI, Gemini, Perplexity, Groq or another.
  - **No default in code:** no provider is a default in code. Which one GeekSocial launches with is decided before launch, on cost per reply.
  - **What the AI does:** it drafts replies and post copy.
  - **Call limit:** a comment goes to the model at most twice per reply.
- **Consent:**
  - Today, changing AI provider needs no new consent from tenants.
  - Once GeekSocial is in production, tenants are notified of a provider change and given a choice.
- **No auto-send:** a person approves every reply and every post before it is sent.
- **Data:** comments are purged after 30 days. Content is never aggregated across tenants or used for training.

## Target scope and where it stands (2026-11-18)

| Area | Target | Built |
|---|---|---|
| Networks | The list above | Facebook Pages |
| Onboarding | Business, plan and payment, AI terms, connect networks, invite team | Business name and AI terms |
| Publishing | Composer with per-network versions, media, scheduling, calendar, queue and time slots, drafts, first comment, RSS | Facebook link post, publish now |
| Content categories and recycling | Categories with time slots, evergreen re-queue, expiry | None |
| Approvals | Every post and reply approved by a person; multi-step; client approval link | Replies only, one person |
| AI provider | Chosen by configuration; clearance per provider and platform | Anthropic, OpenAI, Gemini, Perplexity and Groq clients behind one interface, chosen by `LlmProviders__DefaultProvider`; clearance as `provider:platform` pairs. Only the provider whose key is set is proven live |
| AI content | Per-network versions from one source; brand voice; email versions | Suggested copy for one link post |
| Inbox | Comments, DMs, mentions, reviews across networks; assignment, saved replies, labels, hide and delete; AI drafts a person approves | Facebook comments; Like |
| Reviews | Google Business Profile and Facebook | None |
| Email | Lists with consent, sign-up forms, campaigns, drip campaigns, sending, unsubscribes, bounces, reports, email ↔ social | None |
| Analytics | Per-network and cross-network, PDF, scheduled report emails | Orchestrator counters (in memory, global across tenants) |
| Team and agency | Members, roles, invitations; brands later | One owner per tenant |
| Billing | PayPal subscriptions by tier | None |
| Listening, ads, link-in-bio | Not planned yet | None |

The "Built" column changes as the roadmap's phases ship. Update it in the same commit as the code.
