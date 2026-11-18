# SMM competitor research: Agorapulse, Vista Social, Statusbrew

**Checked 2026-11-18.** Prepared for scoping GeekSocial (multi-tenant SMM SaaS for small businesses: publishing, engagement inbox, AI-drafted replies a person approves).

## How this was researched

- Vendor pages first: pricing pages were rendered in a real browser on 2026-11-18 (all three vendors load prices with JavaScript, so a plain fetch shows "Custom" or blank). Help-center articles were read directly. Vista Social's help center was read through its public Zendesk article API (`support.vistasocial.com/api/v2/help_center/...`), which returns the same articles as the web pages and their `updated_at` dates.
- Each claim carries a source key such as **[A3]**. The key list at the start of each tool's section maps keys to URLs. A date after a source is the page's own date or "updated" stamp where it showed one.
- **UNVERIFIED** means I could not confirm the point on a vendor page. "Not found" means I looked in the places named and the item was absent, which is evidence but not proof of absence.
- Limitation: the session's shared web-search budget ran out late in the work. After that, only direct fetches of known vendor URLs were possible, so a few items (noted inline) could not be chased further.

---

## Quick comparison (the points that matter most for GeekSocial)

| Topic | Agorapulse | Vista Social | Statusbrew |
|---|---|---|---|
| Nextdoor | No | No | No |
| Mastodon / Tumblr / WhatsApp | No / No / No | No / Yes (publish) / No | No / No / Yes (inbox only) |
| Review sites you can reply to | Google Business, Facebook recommendations | Google Business, Facebook, Apple App Store, Google Play (Yelp, TripAdvisor, Trustpilot, OpenTable are view-only) | Google Business, Facebook reviews, App Store, Play Store, Trustpilot (Yelp listed on marketing page only: UNVERIFIED) |
| Review-request campaigns | Not found | Not found | Not found |
| Automatic replies | None (moderation rules can hide/delete/assign/label but never reply) | Yes: keyword or AI-intent triggered DMs/comment replies/review replies, including AI-written replies | Yes: rule-engine DMs (FB/IG/TikTok) and public Google review replies, including AI-written replies (Premium) |
| Human approval of AI/automated replies | AI reply *suggestions* only, person sends; inbox approval workflows (Sep 2026) can require approval of replies by members or AI agents | Automations send without per-message approval once live (guardrails: test mode, schedules, once-per-user, escalation rules); Ask Vista replies/posts follow approval workflows | Rule-engine AI replies send without per-message approval (fallback text guardrail); MCP replies need confirmation |
| Post approval workflows | Single-step (Professional+), multi-step (Custom); external approval via shared calendar (Advanced+) | Single-step all plans, multi-step Advanced+; no-login client approval via shared calendar (Advanced+) | Premium+; external approvers by email with magic link, no seat |
| Email features | Notifications, scheduled report emails; no email marketing | Automation "Send emails" action, scheduled report emails, white-label notification emails, "custom profile" for newsletters (manual); no email marketing | Rule "Send email" incl. external addresses, external-approver emails, scheduled PDF reports; no email marketing |
| Pricing model | Per user: $79/$119/$149 per user/mo annual ($99/$149/$199 monthly); 10 profiles | Per plan: $99/$199/$449 per mo monthly ($950/$1,910/$4,310 per yr) | Per plan: $69/$129/$229 per mo annual ($89/$179/$299 monthly); agencies from $49/client/mo |
| Free plan / trial | Old free plan ended 2026-07-06; free "Archie" AI tier; 30-day trial | None; 14-day trial | None; 14-day trial |
| PayPal | **Yes** | No (cards only listed) | Not listed (card, invoice, some US debit cards) |
| Customer brings own developer app / API keys? | No for social networks; Bluesky needs a user-generated app password | No for social networks; Bluesky app password; customer-generated keys for Apple App Store (.p8) and Google Play (service-account JSON); API key for OpenAI Ads | No for social networks; Bluesky app password; customer-generated keys for App Store, Play Store and Trustpilot (all Enterprise-only) |
| X (Twitter) | Paid add-on (X Lite / X Plus; X Plus $69 per profile/mo) | Paid add-on $29 per X profile/mo since 2026-03-01 | No add-on found; X listening off by default "because of high API costs" |

---

# 1. Agorapulse

### Sources (Agorapulse)

| Key | URL | Date shown |
|---|---|---|
| A1 | https://www.agorapulse.com/pricing/ (browser-rendered, incl. feature table and FAQ) | checked 2026-11-18 |
| A2 | https://support.agorapulse.com/en/articles/8762832-what-is-a-social-profile | 2026-01-08 |
| A3 | https://support.agorapulse.com/en/articles/8712660-api-limitations-per-network | "updated over a week ago" |
| A4 | https://support.agorapulse.com/en/articles/10520824-publishing-by-social-network-explained | "updated today" |
| A5 | https://support.agorapulse.com/en/articles/10393075-agorapulse-inbox-explained | "updated over a week ago" |
| A6 | https://support.agorapulse.com/en/articles/11066949-reports-frequently-asked-questions-faqs | 2026-10-15 |
| A7 | https://support.agorapulse.com/en/articles/11066993-agorapulse-reports-explained | "updated over 2 weeks ago" |
| A8 | https://www.agorapulse.com/integrations/ | checked 2026-11-18 |
| A9 | https://support.agorapulse.com/en/articles/8762473-how-to-add-a-social-profile | 2026-09-29 |
| A10 | https://support.agorapulse.com/en/articles/10419838-how-to-set-up-automated-moderation-in-the-inbox | 2026-09-29 |
| A11 | https://support.agorapulse.com/en/articles/10393331-how-to-reply-to-an-inbox-item | 2026-10-06 |
| A12 | https://support.agorapulse.com/en/articles/13315559-agorapulse-release-notes-2026 | 2026 entries through 2026-10-27 |
| A13 | https://www.agorapulse.com/blog/agorapulse-news/agorapulse-mcp-announcement/ | MCP launched 2026-10-29 |
| A14 | https://www.agorapulse.com/blog/agorapulse-news/archie-announcement/ | Archie launched 2026-10-20 |
| A15 | https://support.agorapulse.com/en/articles/8762514-agorapulse-free-plan-explained | 2026-09-29 |
| A16 | https://support.agorapulse.com/en/articles/12097076-billing-frequently-asked-questions-faq | "updated over 2 weeks ago" |
| A17 | https://intercom.help/agorapulse/en/articles/9979006-shared-calendar-explained | 2026-07-09 |
| A18 | https://support.agorapulse.com/en/articles/10346598-how-to-manage-post-approval-workflows | "updated over 3 weeks ago" |
| A19 | https://agorapulse.com/features/ai-social-media-sidekick | checked 2026-11-18 |
| A20 | https://www.agorapulse.com/features/social-media-inbox/ | checked 2026-11-18 |
| A21 | https://www.agorapulse.com/features/social-media-publishing/ | checked 2026-11-18 |
| A22 | https://www.agorapulse.com/features/social-listening/ | checked 2026-11-18 |
| A23 | https://support.agorapulse.com/en/articles/17301243-activity-logs-explained | 2026-10-08 |
| A24 | https://support.agorapulse.com/en/articles/8762721-notifications-overview | 2026-04-27 |
| A25 | https://support.agorapulse.com/en/articles/9542481-team-settings-explained | 2026-10-15 |
| A26 | https://www.agorapulse.com/features/agencies/ | checked 2026-11-18 |

## A1. Supported networks

Connectable profile types: Facebook Page, X profile, Instagram Professional (Business or Creator), LinkedIn Business Page, LinkedIn personal profile, YouTube channel, Google Business Profile, TikTok Business account, Threads, Pinterest, Bluesky **[A2]**. Facebook and Instagram personal profiles cannot be added **[A3]**. Reddit appears only as a listening source ("Tracks keyword mentions…") **[A8]**.

| Network | Supported? | Publishing | Inbox / engagement | Analytics |
|---|---|---|---|---|
| Facebook (Pages) | Yes [A2] | Yes: text, image, video, link, Stories, Reels; no carousels [A4] | Post comments, ad comments, mentions, DMs, reviews (recommendations) [A5] | Yes [A7] |
| Instagram (Professional) | Yes [A2] | Image, video, carousels, Stories, Reels; no Stories from Creator accounts [A4][A3] | Post, ad and Reel comments, mentions, DMs [A5] | Yes [A7] |
| Threads | Yes [A2] | Text, image, video; multi-part threads in Composer since 2026-05-05 [A4][A12] | Comments, mentions, quoted reposts [A5] | Conflicting: A7 lists Threads reports (Audience 60-day, Content 30-day sync) and A12 added Threads report export on 2026-02-03, but A4 says "Analytics and reporting aren't available" |
| X | Yes, **paid add-on** [A1][A4] | Requires X Lite or X Plus add-on; threads yes, polls no [A4] | Mentions, replies, DMs; requires X Plus [A5] | Requires X Plus, "$69/€69 per profile per month" [A6] |
| LinkedIn Page | Yes [A2] | Text, image, video, link, document carousels, polls [A4] | Post comments, ad comments, mentions [A5] | Yes [A7] |
| LinkedIn personal profile | Yes [A2] | Yes (limitations listed under "LinkedIn (Page and Profile)") [A3] | Private messages from personal profiles are not synced [A3]; other item types UNVERIFIED | UNVERIFIED |
| TikTok (Business) | Yes [A2] | Image and video posts [A4] | Post comments, ad comments; DMs not synced [A5] | Yes [A7] |
| YouTube | Yes [A2] | Videos and Shorts; no Community posts [A4] | Video comments [A5] | Yes, not on Legacy plans [A7] |
| Pinterest | Yes [A2] | Image and video pins [A4] | **Not supported** [A5] | **Not supported** [A6] |
| Google Business Profile | Yes [A2] | Image posts only (What's New, Event, Offer) [A4] | Reviews [A5] | **Not supported** [A6] |
| Nextdoor | **No** (absent from A2 and A8) | No | No | No |
| Bluesky | Yes [A2] | Text, image, video, link; no threads [A4] | **Not supported** [A5] | **Not supported** [A6] |
| Mastodon | **No** (absent from A2, A8) | No | No | No |
| Reddit | Listening only [A8] | No | No | Listening only |
| Tumblr | **No** (absent from A2, A8) | No | No | No |
| WhatsApp | **No** (absent from A2, A8) | No | No | No |

## A2. Publishing

From the pricing page's feature table **[A1]** unless noted (S = Standard, P = Professional, A = Advanced, C = Custom):
- Unlimited scheduling, multi-network and multi-profile publishing, unified calendar, drafts, audience targeting: all plans.
- **Queues**: "Publishing queues" A and C. Queues are pitched for rescheduling content: "Reschedule content and put it in front of new audiences" **[A21]**. No separately named evergreen/recycling feature found.
- **Best time**: "Recommended publishing times in composer" A and C **[A1]**; "Best Time to Publish" listed among AI assistants **[A19]**.
- **Bulk/CSV**: A and C, "up to 200 posts with a single .csv file upload" **[A1][A21]**. Bulk approve: A and C.
- **Per-network customization**: yes **[A21]**.
- **First comment and X threads**: P and up **[A1]**; first comments on Facebook Reels added 2026-04-08 for Professional and above **[A12]**.
- **Stories / Reels / carousels**: see network table **[A4]**.
- **Content categories**: post labels A and C **[A1]**; bulk labeling 2026-08-18; label groups in reports 2026-10-27 **[A12]**.
- **RSS / blog auto-posting**: not found on the publishing feature page **[A21]** or pricing table **[A1]**. UNVERIFIED (likely absent).
- **Media library**: "Content library" limited to 10 images / 2 videos on S and P, full on A and C **[A1]**.
- **Link shortener / UTM**: pulse.ly or Bitly, UTM tracking, automated link tracking on all plans **[A1][A21]**.
- **Link in bio**: PulseLink, P and up **[A1]**.
- Other: Instagram grid preview, product tagging, video subtitle upload, internal comments, calendar notes (P and up); custom fields, post labels (A and up); shared calendars (A: 5, C: unlimited) **[A1]**. Edit text of published Facebook posts (2026-09-01); locked draft templates (2026-06-30); calendar CSV export (2026-07-22); trending/royalty-free audio on Reels (2026-07-29) **[A12]**.

## A3. Content creation

- **AI Writing Assistant** ("creates on-brand content, or improves your draft"), **AI alt text**, **Organization Context AI** (stores brand tone once) on all plans **[A1][A19]**. AI brand context is pre-filled from the email domain and connected profiles (2026-03-11) **[A12]**.
- **Hashtag suggestions** listed as an AI feature **[A19]**.
- **Archie** (launched 2026-10-20): free AI content-repurposing tool at archie.app. Builds a "playbook" per brand, drafts platform-specific posts from existing material (webinars, white papers, reports, meeting notes), "creates visuals and extracts video clips" **[A14]**. Since 2026-10-27 it turns a pasted YouTube video into text posts and drafts TikTok, Threads and X posts **[A12]**.
- **Templates**: locked draft templates (2026-06-30) **[A12]**. No post-template library beyond that found.
- **Image/video editing**: image cropping (3:4 added 2026-02-17), video thumbnails, saved hashtags, post preview **[A21][A12]**. Generated images reuse Playbook brand elements (2026-09-21) **[A12]**.
- **Design integrations**: Canva (P and up), Google Drive, Tenor GIFs **[A1][A8]**.

## A4. Engagement (inbox)

- **Unified inbox** for organic and paid comments, mentions, DMs and reviews across Facebook, Instagram, Threads, LinkedIn, X, YouTube, Google and TikTok **[A20]**. Item types per network are in the network table **[A5]**.
- **Sync speed** **[A5]**: Facebook comments and DMs in real time (usually 1–5 minutes, "when someone is logged in"), reviews hourly. Instagram real time, last 100 posts. Threads, YouTube, TikTok, LinkedIn comments every 15 minutes. Google Business reviews every 2 hours on Standard/Professional, every 5 minutes on Advanced/Trial/Custom. Historical sync covers 32 days after (re)connection.
- **Workflow model**: items are *reviewed* (Inbox Zero style); assign, label, bookmark, hide, delete, bulk actions **[A5][A20]**.
- **Assignment to team members**: P and up; **reply collision detection**: P and up; custom inbox views: P and up **[A1]**.
- **Saved replies**, filter presets, advanced spam management, bulk inbox management, inbox labels, comment export: A and C **[A1]**.
- **AI reply suggestions**: **Custom plan only** **[A1][A11]**. Per the help article they are generated for **private messages (DMs)**, use 3 months of conversation history, appear in 1–5 seconds, and "reply quality has only been tested and validated in English"; toggled per social profile **[A11]**. (Marketing copy says "comments and DMs" **[A19]**; the help article is narrower.) The person still sends the reply.
- **Automatic replies / chatbots**: **none**. Automated moderation actions are Review, Assign, Label, Hide, Delete, Ban sender, Apply sentiment; there is no reply action **[A10]**.
- **Moderation rules ("Inbox Assistant" / Automated Moderation)** **[A10]**: conditions are keywords, only-mentions, only-emojis, contains link, contains email, word count, item language (18 languages, added 2026-07-07 [A12]), inbox item type, post is labeled. Hide works on Facebook, Instagram, Threads, X, YouTube, TikTok; Delete on Facebook, Instagram, LinkedIn; Ban sender on Facebook, YouTube. Rule allowance: 1 rule per profile with 1 condition on Standard/Professional/Advanced; unlimited rules and AND/OR conditions on Custom **[A1][A10]**. Up to 6 actions per rule; all matching rules run in priority order; hidden items are not auto-reviewed; only profile Admin/Editor/Moderator can create rules **[A10]**. Organization-level rules and auto-moderation of comments on labeled posts were added in January 2026 **[A12]**.
- **Sentiment**: "Display only" on Advanced, "Display & Filter" on Custom **[A1]**; a moderation rule can apply sentiment **[A10]**.
- **Contact / CRM history**: "Contact history & notes" and conversation notes on all plans **[A1]**; HubSpot and Salesforce can create leads/contacts from the inbox (Custom) **[A1][A8]**.
- **Inline translation**: all plans; **business hours settings**: Custom **[A1]**.
- **Inbox approval workflows** (2026-09-10): "Inbox actions such as reply, hide, delete, and like can require approval for specific members or AI agents" **[A12]**. Plan tier UNVERIFIED (no help article found).
- **Reply constraints**: private replies to public comments on Facebook, Instagram, X; on Facebook/Instagram only within 7 days; Facebook/Instagram DMs older than 7 days cannot be answered **[A11]**.
- **Ad comments**: synced for Facebook, Instagram (incl. dynamic ads), LinkedIn and TikTok ads; can hide/delete **[A20]**; plan P and up **[A1]**.

## A5. Reviews

- Sources: **Google Business Profile reviews** and **Facebook recommendations** only **[A5]**. "Google reviews moderation" is on all plans **[A1]**.
- Yelp, TripAdvisor, Trustpilot, app stores: not supported (absent from A2, A5, A8).
- Google Business reviews cannot be flagged, reviews with pictures don't sync, no like/dislike **[A3]**.
- **Review-request campaigns**: not found. UNVERIFIED (likely absent).

## A6. Social listening

- Listening is sold as an **add-on**; price is not published ("depends on the volume of conversations you monitor and how far back you look") **[A22][A1]**.
- Sources: Facebook, Instagram, YouTube, X, LinkedIn, Reddit, direct TikTok mentions, plus news, blogs and forums; AI "Listening Screening" removes off-topic results; Share of Voice versus competitors; sentiment (reclassifiable); spike and sentiment alerts; up to 24 months history **[A22]**. 2026 additions: Boolean AND/OR (03-05), forums and web (04-22), 24-month history (05-20), competitor listening and search screening (07-02) **[A12]**.
- **Competitor tracking**: "Competitive Benchmarking" is listed as a demo-only add-on **[A1]**, while the Advanced plan card advertises "competitor benchmarking" in reporting **[A1]**; Competitors moved to its own Analytics menu item on 2026-03-17 **[A12]**. Which tier includes what is unclear (conflicting on the same page).

## A7. Analytics and reporting

- All plans: profile reports, post performance, community-management (team response) report, basic ROI report, **white-label reporting**, basic export, AI summary **[A1]**.
- Professional+: team performance report. Advanced+: cross-network group reports, custom date comparisons, label reports, **scheduled report emails**, advanced export, advanced ROI report, ads reporting. Custom: historical data import (up to 24 months), Reports open API, Looker Studio **[A1]**.
- Data retention: 6 / 12 / 24 / 24 months by plan **[A1]**.
- **Report Studio** (drag-and-drop branded reports) on Advanced/Tailored from 2026-07-29; legacy Custom Reports removed end of September 2026; redesigned PDF export with brand colour and white-label logo (2026-10-20); scheduled CSV Builder exports (2026-10-20) **[A12]**. PPTX exports and automated report exports on Advanced/Custom **[A7]**.
- Networks without reports: Pinterest, Google Business, Bluesky; X needs X Plus **[A6]**.
- UTM and Google Analytics for ROI **[A21][A8]**.

## A8. Collaboration and approvals

- **Roles** **[A25]**: organization roles Owner, Co-owner, Manager, Advanced Member, Member (paid seats); free seats Profile Connector and Archie user. Profile roles Admin, Editor, Moderator ("can schedule posts, but cannot publish them"), Guest (view only). Custom roles and feature-visibility controls are Custom-plan only **[A1]**.
- **Approvals**: "Simple approval workflows" Professional+, "Multi-step approval workflows" Custom **[A1]**. The help article is internally inconsistent about plans **[A18]**. Multi-step workflows: unlimited steps, optional "Require approval from all", posts become read-only while under a workflow, only Owners/Co-owners/Managers create workflows **[A18]**.
- **External (client) approval**: an approval step can include external users from a **Shared Calendar**; they need no Agorapulse account, get a personal emailed link valid 60 days, and can approve, reject, comment or send back. Shared Calendars need Advanced (up to 5) or Custom; branding needs Custom **[A17][A18]**.
- **Notes/tasks**: internal comments and calendar notes (P+) **[A1]**; conversation notes in the inbox **[A1]**. No separate task manager found.
- **Audit trail**: **Activity Log** (2026-10-06) records publishing, approvals, inbox hide/delete/assign/reply, team and settings changes; viewable by Owner/Co-owner/Manager; CSV export up to 6 months; "only available on the Tailored plan"; syncs once a day **[A23][A12]**. Org owners can enforce 2FA (2026-01-06) **[A12]**.

## A9. Agency / multi-client

- One organization per owner; a user can belong to several organizations and switch between them **[A25]**. **Workspaces** (restricting members to some workspaces) are Custom-only **[A25]**.
- **Profile Connector** free seat lets an external person connect only the profiles they add (Advanced: 5 invites, Custom: 10) **[A25]**.
- **White-label**: white-label reporting on all plans **[A1]**; PDF white-label logo option **[A12]**. No white-label of the app or domain found **[A26]**.
- **Client portal**: the Shared Calendar is the client-facing surface **[A17]**. No other portal found **[A26]**.
- **Per-client billing**: not described **[A26]**. UNVERIFIED.

## A10. Ads

- **Boost posts (Facebook)** and **ad calendar sync**: Custom only **[A1]**.
- **Ads reporting**: Advanced+ **[A1]**; Meta Ads report (organic + paid) **[A7]**; per-member access control for Meta and LinkedIn Ads reports (2026-04-14) **[A12]**.
- **Ad-comment moderation**: Professional+; Facebook, Instagram (incl. dynamic ads), LinkedIn, TikTok ads **[A1][A20]**.
- Creating or managing ad campaigns: not found. UNVERIFIED (likely absent).

## A11. Email

- **Email marketing / newsletters / sending posts as email**: not found anywhere in A1, A8, A21. Treat as absent.
- **Email notifications** **[A24]**: token-health digests (daily/weekly), token-expiry alerts, inbox assignment (by a user or an automated rule), post review requests, comments, edits, approvals, rejections, publish failures. Slack and Teams notifications are Custom-only. Reply-by-email: not documented.
- **Scheduled report emails**: Advanced+ **[A1]**; scheduling by weekday/occurrence/hour (2026-06-30) **[A12]**.
- **Email-based approvals**: Shared Calendar users receive an emailed login link and approve on the web page, not inside the email **[A17]**.
- **Email inbox integration**: not found.

## A12. Automation and AI agents (and guardrails)

- **Auto-publishing** of scheduled and queued posts **[A21]**.
- **Automated moderation** acts without a person (hide, delete, ban, assign, label) but **never replies** **[A10]**.
- **AI reply suggestions** are drafts only; a person sends **[A11]**.
- **Archie**: "Every Archie post is reviewed and approved by a human before it's published"; "Nothing is published without human sign-off" **[A14]**.
- **MCP** (Claude, ChatGPT, Microsoft Copilot Studio; all subscribers; needs a paid AI plan): reads listening, inbox, reports; prepares replies and drafts posts. Guardrails: "Every post and reply the AI drafts is reviewed and approved by a person before it goes out"; "The AI assistant confirms every inbox reply with the user before sending it"; "When an approval workflow covers the profile, the reply waits for a manager"; Agorapulse "chose not to give their AI assistants a 'publish now' action"; the AI sees only what that user can access; the connector stores no data **[A13]**.
- **Controls**: "All AI features are opt-in" **[A19]**; org owners/co-owners can switch off AI features individually or org-wide (2026-09-08) **[A12]**; inbox approval workflows can require approval of replies by specific members **or AI agents** (2026-09-10) **[A12]**.

## A13. Integrations and API

- All plans: ChatGPT & Claude (MCP), Bitly, Google Analytics, Google Drive, Tenor. Professional+: Canva. Advanced+: Zapier. Custom: Looker Studio, HubSpot, Salesforce, Microsoft Teams, Slack, Reports open API **[A1]**.
- **Dropbox**: not listed **[A8][A1]**. Absent.
- **Public API**: only a **Reports open API** (reporting data to BI tools), Custom plan **[A1][A8]**. No publishing/inbox API found.
- **Mobile apps**: iOS and Android on all plans **[A1]**.

## A14. Pricing

Per **user** per month **[A1]** (browser-rendered 2026-11-18):

| Plan | Annual billing | Monthly billing | Social profiles | Notes |
|---|---|---|---|---|
| Standard | $79 | $99 | 10 | 6-month data retention, <6 h first response |
| Professional | $119 | $149 | 10 | 12-month retention, live chat |
| Advanced ("most popular") | $149 | $199 | 10 | 24-month retention, CSV bulk, 5 shared calendars |
| Custom | Tailored | — | Unlimited | SSO, multi-step approvals, AI reply suggestions, API |

- Extra profiles: "you can add extra profiles for $10 [annual] / $15 [monthly] /month each" (pricing page's profile-limit tooltip, which also says ad accounts, Google Analytics and third-party integrations don't count toward the limit) **[A1]**. Extra users and profiles are also listed as add-ons in **[A16]**.
- Unlimited post scheduling on all plans **[A1]**.
- **Add-ons**: Advanced Listening, Advocacy, Competitive Benchmarking (book a demo); X Lite and X Plus (contact us) **[A1]**; X Plus $69/€69 per profile per month **[A6]**.
- **Free trial**: 30 days, no credit card **[A1]**.
- **Free plan**: the pricing FAQ still says you can "keep using the free plan" **[A1]**, but the help center says "Starting July 6, 2026, our Free plan will no longer be available to new or current Free plan users". The replacement is the free **Archie plan**: 1 owner plus up to 100 Archie-only users, up to 50 profiles (Facebook, Instagram, LinkedIn, YouTube only), 10 credits per month, and new posts can only be created with Archie **[A15]**.
- Nonprofit pricing on request **[A1]**.
- **PayPal: yes.** Accepted: Visa, MasterCard, American Express, PayPal, and bank transfer (eligible annual plans) **[A16]**.

## A15. Onboarding

1. Start the 30-day trial without a card **[A1]**.
2. Click **+** in the left navigation, choose to add a social profile, pick the network, log in and authorize Agorapulse, click **+ Connect** next to each page/profile, then **Done connecting profiles** **[A9]**.
3. Network-side permissions are required (Facebook Page access or Full Control in a Meta Business portfolio; Instagram must be Professional and linked to Facebook; LinkedIn page Super Admin; YouTube Manager/Owner; Google Business Primary Owner/Owner/Manager/Site Manager with a verified business) **[A9]**.
4. Team access can be granted at connection time (2026-02-18) **[A12]**.
- **Developer app / API keys: none.** The help article describes only OAuth-style authorization. The one exception is **Bluesky**, where the customer enters their handle and an **app password generated in Bluesky's settings** ("This is not your login password") **[A9]**. That is a user credential, not a developer app.

## A16. Distinctive points for a newcomer

- **Per-user pricing** with a fixed 10 profiles makes it costly for teams and cheap for a solo manager **[A1]**.
- **No auto-replies at all**; AI help in the inbox is suggestion-only, DMs-only, Custom-only and English-validated **[A10][A11]**. This is the most conservative stance of the three.
- **Approval gates for AI agents** in the inbox (2026-09-10) and an MCP that never publishes directly and confirms every reply **[A12][A13]**. This is the closest match to GeekSocial's "a person approves every reply".
- X is a paid add-on; Pinterest, Bluesky and Google Business have no reports **[A6]**.
- The free tier became an AI-content product (Archie) in July 2026 **[A15]**.

---

# 2. Vista Social

### Sources (Vista Social)

Help-center articles are at `https://support.vistasocial.com/hc/en-us/articles/<id>`; the "updated" date is the article's own `updated_at`.

| Key | URL | Updated |
|---|---|---|
| V1 | https://vistasocial.com/pricing/ (browser-rendered; monthly and yearly toggles; full comparison table) | checked 2026-11-18 |
| V2 | https://vistasocial.com/integrations/ | checked 2026-11-18 |
| V3 | https://support.vistasocial.com/hc/en-us/sections/9761238301595-Connecting-Profiles | checked 2026-11-18 |
| V4 | https://support.vistasocial.com/hc/en-us/articles/25311040215195-Which-engagements-does-Vista-Social-support-for-each-network | 2026-11-16 |
| V5 | https://support.vistasocial.com/hc/en-us/articles/45316376080667-Inbox-Automation-Triggers-Actions-by-Network | 2026-10-27 |
| V6 | https://support.vistasocial.com/hc/en-us/articles/37925115779483-Getting-Started-with-Inbox-Review-Automations | 2026-11-11 |
| V7 | https://support.vistasocial.com/hc/en-us/articles/49559808727451-How-to-set-up-email-notifications-for-your-inbox-with-automations | 2026-11-12 |
| V8 | https://support.vistasocial.com/hc/en-us/articles/48454694714651-How-to-automatically-hide-or-delete-comments-with-automations | 2026-11-13 |
| V9 | https://support.vistasocial.com/hc/en-us/articles/57208545821211-How-to-build-automations-with-Ask-Vista | 2026-11-16 |
| V10 | https://support.vistasocial.com/hc/en-us/articles/51276073157787-Ask-Vista-agents | 2026-11-13 |
| V11 | https://support.vistasocial.com/hc/en-us/articles/57203065735067-Introducing-Ask-Vista | 2026-11-14 |
| V12 | https://support.vistasocial.com/hc/en-us/articles/46961598441371-Brain-formerly-AI-Training-Knowledge-Getting-Started | 2026-11-16 |
| V13 | https://support.vistasocial.com/hc/en-us/articles/11761321250331-How-to-reply-to-messages-and-comments-with-AI-Assistant | 2026-11-13 |
| V14 | https://support.vistasocial.com/hc/en-us/articles/4411200233627-Responding-to-reviews | 2026-11-12 |
| V15 | https://support.vistasocial.com/hc/en-us/articles/46353782814363-Approval-workflows-in-Vista-Social | 2026-11-13 |
| V16 | https://support.vistasocial.com/hc/en-us/articles/18158890067355-Using-shared-calendars-for-external-client-approvals-no-login | 2026-11-13 |
| V17 | https://support.vistasocial.com/hc/en-us/articles/16852036868507-White-labeling-with-Vista-Social | 2026-11-13 |
| V18 | https://support.vistasocial.com/hc/en-us/articles/5974883710875-Getting-started-with-Vista-Social | 2026-11-16 |
| V19 | https://support.vistasocial.com/hc/en-us/articles/46160002160923-X-Twitter-Integration-Changes-March-1-2026 | 2026-11-10 |
| V20 | https://support.vistasocial.com/hc/en-us/articles/32993061787035-Vista-Social-API | 2026-10-07 |
| V21 | https://support.vistasocial.com/hc/en-us/articles/4415219717019-Social-Listening-with-Vista-Social | 2026-11-12 |
| V22 | https://support.vistasocial.com/hc/en-us/articles/30687223430555-How-to-let-external-users-connect-social-profiles-without-logging-in-profile-connect-link | 2026-11-16 |
| V23 | https://support.vistasocial.com/hc/en-us/articles/4411200236315-Smart-Publishing | 2026-11-11 |
| V24 | https://support.vistasocial.com/hc/en-us/articles/40740183689115-Evergreen-Auto-Repurposing | 2026-11-02 |
| V25 | https://support.vistasocial.com/hc/en-us/articles/4411057807643-How-to-set-up-and-use-publishing-queues | 2026-11-08 |
| V26 | https://support.vistasocial.com/hc/en-us/articles/36121115657371-How-to-use-the-Social-Media-CRM-in-Vista-Social-s-Inbox | 2026-10-17 |
| V27 | https://support.vistasocial.com/hc/en-us/articles/4409614773531-Getting-started-with-the-Social-Inbox | 2026-11-17 |
| V28 | https://support.vistasocial.com/hc/en-us/articles/45316588357275-Automating-Your-Inbox-Internal-Routing-Organizing | 2026-10-27 |
| V29 | https://support.vistasocial.com/hc/en-us/articles/58243924859675-Ads-management-connect-ad-accounts-set-spend-limits-and-manage-campaigns | 2026-11-16 |
| V30 | https://support.vistasocial.com/hc/en-us/articles/23687755431835-Boosting-posts-with-Vista-Social | 2026-11-13 |
| V31 | https://support.vistasocial.com/hc/en-us/articles/30145017495579-Are-ad-comments-supported-in-the-Vista-Social-Inbox-dark-post-comments | 2026-11-06 |
| V32 | https://support.vistasocial.com/hc/en-us/articles/29117458372379-Sentiment-Analysis | 2026-10-17 |
| V33 | https://support.vistasocial.com/hc/en-us/articles/57069318348827-Audit-Log-Track-Every-Action-for-Security-and-Compliance | 2026-10-13 |
| V34 | https://support.vistasocial.com/hc/en-us/articles/4409604728219-Changing-user-permissions-in-your-organization | 2026-10-16 |
| V35 | https://support.vistasocial.com/hc/en-us/articles/42075096746267-How-to-pause-publishing-and-add-a-publish-delay-Publishing-Safety | 2026-11-05 |
| V36 | https://support.vistasocial.com/hc/en-us/articles/30733771731483-Brand-Safety-and-Compliance-Policy | 2026-11-08 |
| V37 | https://support.vistasocial.com/hc/en-us/articles/6977311182619-How-to-schedule-reports | 2026-11-13 |
| V38 | https://support.vistasocial.com/hc/en-us/articles/43881261320987-Team-conversations-in-the-inbox | 2026-11-12 |
| V39 | https://support.vistasocial.com/hc/en-us/articles/46525029430299-Vista-Social-s-Available-MCP-Tools | 2026-11-15 |
| V40 | https://support.vistasocial.com/hc/en-us/articles/4407224874523-Accepted-payment-methods | checked 2026-11-18 |
| V41 | https://support.vistasocial.com/hc/en-us/articles/4407250446235-Information-about-free-trials | 2026-11-06 |
| V42 | https://support.vistasocial.com/hc/en-us/articles/39013177705115-Data-Collection-with-DM-Automations | 2026-10-27 |
| V43 | https://support.vistasocial.com/hc/en-us/articles/42850944558491-How-do-I-set-up-an-Apple-App-Store-integration | 2026-11-13 |
| V44 | https://support.vistasocial.com/hc/en-us/articles/42851659265307-How-do-I-set-up-a-Google-Play-Store-integration | 2026-11-13 |
| V45 | https://support.vistasocial.com/hc/en-us/articles/4409604890523-Connecting-your-Yelp-profile-to-Vista-Social | 2026-11-13 |
| V46 | https://support.vistasocial.com/hc/en-us/articles/31091418183579-Connecting-your-Bluesky-profile-to-Vista-Social | 2026-11-13 |
| V47 | https://support.vistasocial.com/hc/en-us/articles/32928788122267-How-to-connect-and-post-to-custom-profiles-in-Vista-Social | 2026-11-13 |
| V48 | https://support.vistasocial.com/hc/en-us/articles/4409614593051-Connecting-your-Reddit-profile-to-Vista-Social | 2026-11-13 |
| V49 | Help-center search, https://support.vistasocial.com/api/v2/help_center/articles/search.json?query=... (used to confirm which per-network "Publishing with Vista Social" and "report definitions" articles exist, and that "Nextdoor" and "Mastodon" return zero articles) | checked 2026-11-18 |

## V1. Supported networks

Connectable (from the Connecting Profiles section **[V3]** and integrations page **[V2]**): Facebook Pages and Facebook Groups, Instagram Business/Creator, X, LinkedIn Company Page and Personal Profile, TikTok, YouTube, Pinterest, Snapchat Public Profile, Threads, Bluesky, Reddit, Tumblr, Google Business; review sites Yelp, TripAdvisor, Trustpilot, OpenTable, Apple App Store, Google Play Store; plus "custom profiles" for channels with no integration **[V47]**.

| Network | Supported? | Publishing | Inbox / engagement [V4] | Analytics |
|---|---|---|---|---|
| Facebook (Pages; Groups connectable) | Yes [V3] | Yes, plus boosting [V2][V30] | Post comments, live comments, mentions, messages, reviews/recommendations; Meta ad (dark post) comments [V4][V31] | Yes ("Facebook Page metrics & report definitions" article exists) [V49] |
| Instagram (Business/Creator) | Yes [V3] | Yes, incl. Stories, Reels, trending audio, user/product/collab tags [V1][V49] | Comments (posts, carousels, Reels, Stories, Live, ads), mentions, messages [V4] | Yes [V49] |
| Threads | Yes [V2] | Yes [V49] | Comments, mentions [V4] | Yes [V49] |
| X | Yes, **$29/month per X profile add-on** since 2026-03-01 [V19] | Yes (add-on covers publishing, boosting, automations, analytics) [V19] | Comments/replies, mentions, DMs [V4][V5] | Yes, via add-on [V19] |
| LinkedIn Company Page | Yes [V3] | Yes [V49] | Comments, mentions (reshares as automation trigger) [V4][V5] | Yes [V49] |
| LinkedIn Personal Profile | Yes [V3] | Yes [V49] | Comments [V4] | Yes ("LinkedIn Personal Profile report definitions") [V49] |
| TikTok | Yes [V3] | Yes [V49] | Comments; DMs for business accounts outside EEA/Switzerland/UK [V4] | Yes [V49] |
| YouTube | Yes [V3] | Yes [V49] | Comments (100 most recent posts) [V4] | UNVERIFIED (no YouTube report-definitions article found) |
| Pinterest | Yes [V3] | Yes [V49] | None [V4] | Yes [V49] |
| Google Business Profile | Yes [V3] | Yes (posts with CTA buttons) [V49] | Reviews [V4] | Yes [V49] |
| Nextdoor | **No** (absent from V2, V3; zero help articles for "Nextdoor") [V49] | No | No | No |
| Bluesky | Yes [V2] | Yes [V2][V49] | Comments [V4]; DMs if the app password allows DM access [V46] | Yes (per integrations page) [V2] |
| Mastodon | **No** (absent from V2, V3; zero help articles) [V49] | No | No | No |
| Reddit | Yes [V48] | Yes ("Reddit Publishing with Vista Social") [V48] | Comments, messages [V4][V5] | Yes ("Reddit report definitions") [V48]; keyword listening needs the external-listening add-on [V21] |
| Tumblr | Yes [V2] | Yes [V49] | None listed [V4] (integrations page claims "response management") [V2] | Claimed on integrations page [V2]; no report-definitions article found |
| WhatsApp | **No** (absent from V2, V3) | No | No | No |
| Snapchat (extra) | Yes [V2] | Stories, Spotlight, saved stories [V2] | None [V4] | Yes [V49] |

The pricing FAQ also mentions "Vimeo" **[V1]**, but Vimeo is not on the integrations page or the connection list **[V2][V3]**. UNVERIFIED (probably stale).

**Custom profiles** (all plans): plan and schedule content for channels Vista doesn't integrate with, "like email newsletters, blog posts, SMS campaigns, or ads". There is no auto-publishing; the post is marked published at its time or an email reminder is sent so someone publishes manually **[V47]**. This is the only way Nextdoor content could be planned in Vista.

## V2. Publishing

From the pricing comparison table (all plans unless noted) **[V1]**: unlimited post scheduling, calendar view, bulk scheduling, "Smart Scheduling", optimal time recommendation, posting schedule (queue) per profile, drafts, AI image and video generation (AI credits), customize posts per profile, tag users/products/collabs, country targeting (Advanced+), first like, "Additional Comments" (first comment and follow-ups), link shortener, external calendars, media library, trending audio, hashtag suggestions, saved captions/hashtags/mentions, post labels, custom fields.

- **Queues / time slots**: weekly slots per profile, tagged sub-queues, copy queue to other profiles, "Queue next / available / last" **[V25]**.
- **Best time**: optimal time recommendations **[V1][V25]**.
- **Bulk / CSV**: bulk scheduling on all plans **[V1]**; "CSV format guidelines for Bulk Scheduling" article exists **[V49]**.
- **Evergreen / recycling**: mark posts evergreen; up to 25 reuses, 3–100 day interval, never more than once per 72 hours, filled only into evergreen-marked queue slots, with ROI tracking **[V24]**.
- **RSS / blog auto-posting**: "Smart Publishing" from Bing News trending news, an RSS feed, or AI-generated ideas, up to 24 posts per day; the review option can be "No Review", "All approvers" or a specific approver **[V23]**.
- **Link shortener / UTM**: Vist.ly, Bitly, or a custom domain (Advanced+); UTM tracking **[V17][V49]**.
- **Link in bio**: Vista Page (all plans), custom domain and Google Tag Manager on Advanced+ **[V1]**; Vista Page can host an AI chatbot block using Brain knowledge **[V12]**.
- **Publishing safety**: pause publishing per profile group or account; "Publish now" delay of 0–59 minutes; admins only **[V35]**.
- Other entry points: Instagram and TikTok planners, X thread maker, multi-time scheduling, Chrome/Firefox extensions **[V18]**.

## V3. Content creation

- **AI Assistant** ("powered by ChatGPT") for captions and replies, with tone, format, goal, CTA type, brand voice and Knowledge selection **[V13]**.
- **Brain** (Advanced+; Premium sources on Scale/Enterprise): knowledge bases from prompts, documents, URLs, sitemaps, Zendesk; used when writing posts, inbox replies, automation AI replies and the Vista Page chatbot **[V12]**.
- **Ask Vista** (all plans): repurposing such as "Turn this blog post into 5 posts for LinkedIn", turning uploaded spreadsheets/documents into posts, and planning two weeks of content **[V11]**.
- **AI image and video generation** using AI credits (1,000 / 2,000 / 3,500 / unlimited per month by plan; $10 per extra 1,000) **[V1]**.
- **Templates**: "Ready-to-Go" automation templates **[V6]**; report templates **[V37]**. No post-template library found.
- **Hashtags**: suggestions and saved hashtags **[V1]**.
- **Editing / design**: built-in photo editor (help article exists) **[V49]**; Canva, Figma, Bynder, Giphy, Pexels, Unsplash, Google Drive, OneDrive, Dropbox **[V2][V1]**.

## V4. Engagement (inbox)

- **Unified inbox** of comments, mentions, messages, reviews and shares, organized by profile **[V27][V4]**. Facebook, Instagram and TikTok load in real time; other networks every 5–6 hours; up to 500 messages and comments per profile per day; DMs cannot be deleted; comments on posts from before the connection (for example older than 60 days) can't be fetched **[V4]**.
- **Assignment**: "Assign as task" creates a task in Vista Work with assignee, status, due date and priority **[V27][V28]**.
- **Saved replies**: all plans **[V1]**; save AI replies for reuse **[V13]**. **Macros** apply several actions in one click **[V27]**.
- **AI-suggested replies**: AI Assistant drafts replies to comments, messages, reviews and mentions in guided or advanced mode; the person picks one and sends **[V13]**.
- **Automatic replies** (all plans; AI intent detection Advanced+) **[V6]**:
  - Triggers: DM, comments on posts/Reels, story replies, mentions, reviews; keyword include/exclude, AI "Detect intent", star ratings, followers-only (Instagram) **[V6]**.
  - Actions: AI reply (prompted, optionally grounded in a Brain Knowledge), text replies with rotated variations, image, video, PDF, card, DM, comment reply, hide/delete, delay, data collection; plus Vista actions: tags, assign task, Zendesk ticket, mark complete, send emails **[V6]**.
  - Per network **[V5]**: Instagram and Facebook (DM and comment replies, hide, delete, data collection; Facebook also reviews); TikTok (DM, hide, delete); LinkedIn (comment reply, delete); X (comment reply, hide); Reddit (DM, comment, delete); YouTube (comment, delete); Threads and Bluesky (comment, hide); Google Business (reply to review); Yelp, TripAdvisor, Trustpilot, OpenTable (trigger only, no network action).
  - **Data collection** (Advanced+): the bot asks for name, email, phone etc. with validation, stores answers in the inbox CRM, and can deliver to email, a Slack webhook or a REST endpoint **[V42]**.
- **Moderation rules**: automations can hide (Bluesky, Threads, X, Facebook, Instagram, TikTok) or delete (Reddit, YouTube, LinkedIn, Facebook, Instagram, TikTok) comments by keyword or AI intent **[V8]**. Spam marking and archiving in the inbox **[V27]**.
- **Sentiment**: **Enterprise only**, positive/negative/neutral/mixed with an explanation, editable **[V32][V1]**.
- **Contact / CRM history**: "Social Media CRM" panel with occupation, company, phone, email, description and custom fields, visible to the team; Zendesk connection **[V26]**.
- **Internal collaboration**: internal notes with @mentions, team conversations (private thread per message, used "for getting approval before replying"), @Vista AI help in the thread **[V38][V27]**.
- **Escalation to humans**: Brain escalation rules (Scale/Enterprise) send email, Slack or REST alerts when topics such as legal complaints or billing disputes come up **[V12]**; automations can "Assign a task" as a hand-off **[V6]**.
- **Ad comments**: Meta (Facebook and Instagram) only, shown with a "Dark" badge **[V31]**.

## V5. Reviews

- **Reply from Vista**: Google Business, Facebook, Apple App Store, Google Play Store **[V14]**.
- **Monitor only**: Trustpilot, Yelp, TripAdvisor, OpenTable "due to third-party platform limitations" **[V14][V4]**.
- AI drafting of review replies; share a review by link for a manager to comment without logging in; only the last 30 days of reviews load at first connection **[V14]**.
- **Automated review replies**: review trigger by star rating or keyword; AI replies on Google Business and Facebook **[V6]**.
- Review sites (Yelp, App Store, Play Store) need Advanced or above **[V45][V43][V44]**. Review reports exist on all plans **[V1]**.
- **Review-request campaigns**: not found (help-center search for review requests returned nothing relevant) **[V49]**. UNVERIFIED (likely absent).

## V6. Social listening

- **Internal listening** (own profiles: mentions, reviews, comments) is included in every plan; **external listening** (public web, social, blogs, news, X Trends/News, YouTube Trending, Google Trends) is an add-on "from $75/mo" **[V21][V1]**.
- Boolean keyword groups, exclusions, sentiment filter, language filter, alerts, shareable listeners (keywords hidden by default), 4–6 hour refresh, daily result quota **[V21]**.
- **Competitor reports** on all plans **[V1]**; competitor analysis via listening (help article exists) **[V49]**.

## V7. Analytics and reporting

- Report types: profile performance, post performance, review performance, task/work performance, competitor analysis, advocacy, sentiment, industry benchmark, listener, inbox performance, paid performance **[V18][V37]**.
- By plan **[V1]**: social, post, web analytics (GA), review, competitor reports and scheduled reports on all plans; custom reports Advanced+; sentiment reports, industry benchmarks and custom dashboards Enterprise only. Looker Studio, Power BI, Tableau on Advanced+.
- **Scheduled delivery**: weekly, monthly or one-off; PDF and/or report link; to team users and external email addresses; customizable email subject and body **[V37]**.
- **White-label**: branded PDF reports Advanced+; custom domain for report links Scale+ **[V17]**.
- **ROI**: UTM tracking, Google Analytics, evergreen ROI **[V24][V2]**.
- Ask Vista builds narrative reports exportable as PDF, Word, Excel, PowerPoint, Markdown or CSV, or a shared web page **[V11]**.

## V8. Collaboration and approvals

- **Roles**: Account admin, Profile Group admin, Read-only, Restricted user with per-feature No access / View / Manage, and a **Contributor** publishing permission whose posts "require review before being published" **[V34]**.
- **Approval workflows**: single-step on Professional; multi-step on Advanced+ **[V15][V1]**. Content goes "created → submitted for review → approval → scheduled/published"; contributors' posts go to an admin-set default workflow and they see "Send for approval" instead of "Schedule" **[V15]**.
- **External (client) approval** (Advanced+): a workflow step marked "Shared calendar"; the client opens a link (optional password and expiry), approves or rejects with a note, and is asked only for name and email; viewers are **not** emailed when a post needs them ("To have reviewers notified by email, invite them as team members") **[V16]**.
- **Notes/tasks**: calendar notes, post comment collaboration, Vista Work projects and tasks **[V1][V28]**.
- **Brand safety & compliance policy** (Enterprise; Scale add-on): AI checks text and attached images/videos of posts **and replies** against the policy and warns the drafter; flags show to reviewers **[V36][V15]**.
- **Audit log**: Enterprise, **off by default**; records actions from web, mobile, API, MCP, Ask Vista, Slack, Teams and automations, marking automated actions under the person who set them up; CSV export and API; default retention 12 months **[V33]**.

## V9. Agency / multi-client

- **Profile groups** separate brands/clients, each with its own brand voice, Brain knowledge and policy **[V18][V12][V36]**.
- **Profile connect link** (Scale+): clients connect their own profiles without logging in and without using a seat **[V22]**.
- **White-label** matrix **[V17]**: branded PDF reports (Advanced+); custom link-shortener domain, Vista Page domain, unbranded shared calendars, remove Vista logo from pages (Advanced+); custom domain for shared calendars (Scale+); custom domain for dashboard/login, custom dashboard logo, **custom sender email and logo for email notifications**, custom domain for connect link (Scale+).
- **Client portal**: shared calendars and "share anything with a link" pages (ideas, media, tasks, listeners, automation performance) **[V16][V21][V6]**.
- Slack/Teams: one channel per client tied to that client's profile group and knowledge base (Advanced+) **[V11]**.
- **Per-client billing**: not found. UNVERIFIED.

## V10. Ads

- **Boosting** (all plans): Facebook, Instagram, LinkedIn, TikTok, X, Pinterest, using saved boost configurations (budget, days, CPM bid, audiences) **[V30]**.
- **Ads management** (Advanced+): connect Meta, LinkedIn, TikTok, Snapchat, X, Pinterest, Reddit, Google Ads, Microsoft Ads and OpenAI Ads (ChatGPT ads). Create/edit campaigns, pause, change budgets, mostly via Ask Vista **[V29]**.
- Guardrails **[V29]**: per-person Ads permission (No access / View; Admin or Manager to change); per-ad-account access level (Everything / Pause and lower only / Read only); optional monthly spend limit (treated as reached if spend can't be read); required daily budget cap per campaign (default 100 in account currency); every Ask Vista change shows a confirmation card and nothing is sent until "Confirm and proceed"; new campaigns start paused; duplicate confirmations within minutes are sent once; changes are logged. MCP-connected assistants "don't show Vista Social's confirmation card".
- **Ad-comment moderation**: Meta ads only, in the inbox **[V31]**; automations don't run on ads or dark posts **[V42]**.
- Paid performance reports **[V37]**.

## V11. Email

- **Email marketing / newsletters**: **none found**. The nearest feature is a **custom profile** to plan newsletters on the calendar, published manually with an emailed reminder **[V47]**.
- **Email as an automation action**: "Send emails" alerts to selected users or **any external address** when inbox activity matches keywords or AI intent (e.g. sales leads, urgent messages) **[V7][V28]**.
- **Escalation emails** from Brain rules **[V12]**; data-collection results delivered by email **[V42]**.
- **Scheduled report emails** with custom subject and body; agents can email client recaps ("rebuild the client report and email it to the client") **[V37][V11][V10]**.
- **Notifications**: email, in-app, browser or Slack **[V18]**; agents can alert by email **[V10]**.
- **White-label notification emails** (custom sender email and logo) on Scale+ **[V17]**.
- **Email approvals**: internal approvers get email notifications; shared-calendar (external) reviewers do not **[V16]**.
- **Email inbox integration** (reading a mailbox into the inbox): not found.

## V12. Automation and AI agents (and guardrails)

- **Inbox and review automations** send replies without a person once set live **[V6]**. Guardrails: "Test automation" replays recent messages or typed messages and "Nothing is sent to anyone while you test"; schedules (days/hours/time zone); sequence order; "Exclusive"; "Once per user"; only team members with Manage access can create them; AI replies can be grounded in a Brain Knowledge **[V6]**. Automations built through Ask Vista are saved "as a paused draft, so nothing goes live until you say so" **[V9]**.
- No per-message human approval step for automation replies is documented **[V6]**. Brand-safety policy checks replies drafted by team members (warnings) **[V36]**; whether it blocks automation AI replies is UNVERIFIED.
- **Ask Vista** (all plans) answers questions and does work across calendar, inbox, reports, then "asks for your approval"; "Every post Ask Vista creates goes through your normal approval workflow"; "Posts and replies follow your approval workflows"; it acts with the user's permissions; admins can turn AI on or off per profile group **[V11]**. It can work the inbox, e.g. "Answer simple questions from our FAQ, flag leads and complaints" **[V11]**.
- **Ask Vista agents** (Advanced+): scheduled AI "teammates" (morning briefer, inbox SLA monitor, lead scout, review watcher, sentiment spike watcher, approval-flow watcher, evergreen recycle planner, etc.). Some only report; others act: "Posts and replies follow your approval workflows." Agents pause automatically when AI credits run out; "Agents don't replace your review" **[V10]**. Agents can call connected tools such as HubSpot and ClickUp **[V10]**.
- **Smart Publishing** can auto-post RSS/news/AI content with "No Review" **[V23]**; **evergreen** recycles posts automatically **[V24]**.
- **MCP**: 134 tools on all plans, including approve/reject posts **[V39][V1]**.
- **Audit log** marks automated and AI actions (Enterprise, opt-in) **[V33]**.

## V13. Integrations and API

- From the pricing table **[V1]**: MCP, Ask Vista connectors (Notion, HubSpot, ClickUp "and 20 more, or your own MCP server"), Canva, Google Drive/OneDrive/Dropbox, Google Analytics on all plans; Zapier, Make.com, Slack, Microsoft Teams, Looker Studio, Power BI, Tableau, Bynder on Advanced+; Zendesk on Scale+; **Vista Social API on Enterprise only**.
- Integrations page also lists Salesforce (turn inbox messages into cases, sync contacts), Bitly, Figma, Giphy, Pexels, Unsplash **[V2]**. Note: the data-collection article says direct HubSpot/Salesforce CRM integrations for collected contacts "are not available" **[V42]**, which conflicts with the Salesforce listing on V2.
- **Public API**: REST API 2.0, 76 POST endpoints (publishing, tasks, profiles/teams, inbox, reports, listening, automations, shared calendars, Vista Pages); workspace-scoped `x-api-key`; 60 requests/minute; included with Enterprise "and with the API add-on" (add-on price not published); no sandbox; API 1.0 retired 2026-11-01 **[V20]**.
- **Apps**: iOS, Android, Mac, Windows, Chrome, Firefox **[V1]** (footer) and **[V18]**.

## V14. Pricing

Browser-rendered 2026-11-18 **[V1]**:

| Plan | Monthly billing | Yearly billing | Profiles / users | DM contacts | AI credits/mo |
|---|---|---|---|---|---|
| Professional | $99/mo | $950/yr (shown as $80/mo) | 15 / 2 | 10,000 | 1,000 |
| Advanced ("most popular") | $199/mo | $1,910/yr ($160/mo) | 30 / 3 | 25,000 | 2,000 |
| Scale | $449/mo | $4,310/yr ($360/mo) | 70 / 4 | 100,000 | 3,500 |
| Enterprise | Custom | Custom | Unlimited / 8+ | Custom | Unlimited |

- Unlimited post scheduling on all plans. Extra profiles and users: "Add as you grow" on Scale, custom on Enterprise; not offered on Professional/Advanced in the table **[V1]**.
- **Add-ons** **[V1]**: Employee Advocacy (free to try, then $199/mo for 25 employees); Listening from $75/mo; X $29/mo per X profile; AI credits $10/mo per 1,000. Enterprise add-ons: SSO, audit log, data migration (no price). Brand safety is a Scale add-on **[V36]**; API add-on price not published **[V20]**.
- Annual billing "requires a one-time upfront payment for the entire subscription term" **[V1]**.
- **Free plan**: none. **Trial**: 14 days, no credit card; Enterprise not self-serve; data kept 30 days after trial ends **[V41][V1]**.
- Nonprofit discount (US) on Professional and Advanced **[V1]**.
- **PayPal: no.** Accepted: credit, debit and prepaid cards from Visa, Mastercard, American Express, JCB, Discover, Diners Club; Enterprise can pay by wire, ACH or net terms; all charges in USD **[V40]**.
- Third-party price history (not vendor): pricingsaas.com logged an increase from $79/$149/$349 to $99/$199/$449 in 2026 week 40 (https://pricingsaas.com/companies/vistasocial/diffs/2026W40). Not independently verified.

## V15. Onboarding

1. Start a trial: new users enter email and password; verify the email **[V41][V18]**.
2. A "Welcome to Vista" setup checklist: connect profiles (30%), invite team (20%), schedule first post (20%), then optional steps (Ask Vista, brand voice, Brain, DM automation, Vista Page, agent, apps). A free 30-minute onboarding call can be booked from the checklist **[V18]**.
3. Connect profiles: Quick add → Connect a profile → choose profile group → pick network → authorize on the network ("allow all permissions"; "Do not modify the permissions") **[V18][V48]**.
4. Optional: clients connect their own profiles through a profile connect link (Scale+) **[V22]**.
- **Developer app / API keys for social networks: none** (OAuth authorization) **[V48][V18]**. Exceptions where the customer creates credentials:
  - **Bluesky**: handle plus an app password the user creates in Bluesky settings **[V46]**.
  - **Apple App Store**: the customer's App Store Connect admin generates an API key (.p8 file) and supplies Issuer ID and Key ID **[V43]**.
  - **Google Play**: the customer creates a Google Cloud project and service account, enables APIs, downloads a JSON key, and invites the service account in Play Console ("This is the only way to connect") **[V44]**.
  - **OpenAI Ads**: "connected directly with an API key" **[V29]**.
  - **Yelp, etc.**: paste the public listing URL **[V45]**.

## V16. Distinctive points for a newcomer

- The most **automation-forward** of the three: keyword/intent-triggered auto-DMs, comment replies and AI review replies on the base plan, data-collection bots, escalation rules, scheduled AI agents, and AI ads management with spend caps **[V6][V42][V12][V10][V29]**.
- Broadest network list of the three (Snapchat, Tumblr, Reddit publishing, Facebook Groups, app-store and restaurant/travel review sites), but still **no Nextdoor, Mastodon or WhatsApp** **[V2][V3][V49]**.
- Guardrails are configuration-level (test mode, paused drafts, schedules, once-per-user, spend caps, confirmation cards) rather than per-reply human approval **[V6][V9][V29]**.
- Sentiment, audit log and API are Enterprise-only **[V1][V32][V33][V20]**.
- X costs $29/month per profile on top of the plan **[V19]**.

---

# 3. Statusbrew

### Sources (Statusbrew)

Statusbrew help articles mostly show no publish/updated date; where a date was visible it is given.

| Key | URL | Date shown |
|---|---|---|
| S1 | https://statusbrew.com/pricing (browser-rendered; yearly and monthly; FAQ expanded) | checked 2026-11-18 |
| S2 | https://statusbrew.com/help/articles/what-social-profiles-can-i-connect-to-statusbrew | none |
| S3 | https://statusbrew.com/help/articles/conversation-type-and-sync-rates | none |
| S4 | https://statusbrew.com/integrations (browser-rendered) | checked 2026-11-18 |
| S5 | https://statusbrew.com/social-media-engagement (browser-rendered, FAQ expanded) | checked 2026-11-18 |
| S6 | https://statusbrew.com/social-media-publishing (browser-rendered) | checked 2026-11-18 |
| S7 | https://statusbrew.com/help/articles/what-is-statusbrew | none |
| S8 | https://statusbrew.com/help/articles/rule-engine | none (screenshot dated 2025-10-15) |
| S9 | https://statusbrew.com/help/articles/actions-rule-engine | none |
| S10 | https://statusbrew.com/help/articles/ai-assistant | none |
| S11 | https://statusbrew.com/mcp | checked 2026-11-18 |
| S12 | https://statusbrew.com/help/articles/approval-workflow | none |
| S13 | https://statusbrew.com/help/articles/external-approvers | none |
| S14 | https://statusbrew.com/help/articles/statusbrew-for-agencies | none |
| S15 | https://statusbrew.com/help/articles/understanding-reports | none |
| S16 | https://statusbrew.com/help/articles/statusbrew-for-enterprises | none |
| S17 | https://statusbrew.com/help/articles/statusbrew-insights-apis | none |
| S18 | https://statusbrew.com/help/articles/understanding-listening | none |
| S19 | https://statusbrew.com/help/articles/free-trial-statusbrew | none |
| S20 | https://statusbrew.com/help/articles/acceptable-payment-methods | none |
| S21 | https://statusbrew.com/help/articles/connect-social-profiles | none |
| S22 | https://statusbrew.com/help/articles/connect-app-store-app | none |
| S23 | https://statusbrew.com/help/articles/connect-play-store-app | none |
| S24 | https://statusbrew.com/help/articles/connect-trustpilot | none |
| S25 | https://statusbrew.com/help/articles/connect-whatsapp-account | none |
| S26 | https://statusbrew.com/help/articles/facebook-boosting | none |
| S27 | https://statusbrew.com/help/articles/rss-feed | none |
| S28 | https://statusbrew.com/help/articles/publish-schedule-categories | none |
| S29 | https://statusbrew.com/help/articles/understanding-statusbrew-publish | none |
| S30 | https://statusbrew.com/help/articles/youtube-integration | none |
| S31 | https://statusbrew.com/help/articles/bluesky-integrations | none |
| S32 | https://statusbrew.com/lp/ad-comment-management | checked 2026-11-18 |
| S33 | https://statusbrew.com/apps | checked 2026-11-18 |
| S34 | https://headwayapp.co/statusbrew-updates (product changelog linked from the site footer) | entries undated |
| S35 | https://statusbrew.com/help/articles/connect-twitter-account | none |
| S36 | https://statusbrew.com/help/articles/how-does-statusbrew-connect-with-social-networks | none |
| S37 | https://statusbrew.com/sitemap.xml (used to confirm which help articles exist, e.g. `schedule-gmb-posts`, `schedule-bluesky-posts`, `schedule-threads-posts`, `schedule-pinterest-pin`, `schedule-twitter-polls`, `manage-tiktok-ad-comments`, `brand-voice`; latest monthly "Product Changes" article is October 2025) | checked 2026-11-18 |

## S1. Supported networks

Connectable account types: Facebook Pages, Instagram Business/Creator, X, Threads, LinkedIn Pages (company/showcase/university) **and** LinkedIn personal profiles, Pinterest, YouTube, TikTok, **WhatsApp** (verified Meta business account), Reddit, Bluesky, Google Business Profile, **Line** Business, Play Store, App Store, Trustpilot Business **[S2]**. Not supported: Facebook personal profiles, LinkedIn groups **[S2]**.

| Network | Supported? | Publishing | Inbox / engagement [S3] | Analytics |
|---|---|---|---|---|
| Facebook (Pages) | Yes [S2] | Yes, incl. geo-targeting and boosting [S4][S26] | Message, comment, ad comment, recommendation, mention, visitor post (immediate) | Yes [S15][S7] |
| Instagram (Business/Creator) | Yes [S2] | Yes: Reels, Stories, carousels, first comments, collaborators, product tags [S7] | Message, mention, story mention, comment, media tagged, ad comment, hashtag post | Yes [S7] |
| Threads | Yes [S2] | Yes [S4][S37] | Replies (immediate) | Yes ("generate meaningful analytics") [S4] |
| X | Yes [S2] | Yes; threads and polls (help articles exist) [S37] | DM, mention, keyword (every 15 min Enterprise, 40 min other plans) | UNVERIFIED in detail; X listening "disabled by default because of high API costs" [S18]. No X add-on fee found [S1][S35] |
| LinkedIn Page | Yes [S2] | Yes (polls article exists) [S37] | Comment, mention (<5 min) | Yes (`linkedin-metrics`) [S37] |
| LinkedIn personal profile | Yes [S2] | UNVERIFIED (connection supported; publishing not separately confirmed) | UNVERIFIED | UNVERIFIED |
| TikTok | Yes [S2] | Yes [S4] | Comment, DM (immediate), mention (up to 48 h); business account needed [S3] | Yes [S4] |
| YouTube | Yes [S2] | "Schedule YouTube Videos", first comment; Shorts not confirmed [S30] | Comments [S3] | Yes [S30] |
| Pinterest | Yes [S2] | Yes [S4] | Not listed [S3] | Yes [S4] |
| Google Business Profile | Yes [S2] (Standard+ [S1]) | Yes (`schedule-gmb-posts` article exists) [S37] | Reviews (every 15 min) [S3]; Q&A and Google messages (beta) [S5] | Yes [S37] |
| Nextdoor | **No** (absent from S2, S4, S7; no help article in sitemap) [S37] | No | No | No |
| Bluesky | Yes [S2] | Yes [S31] | Not listed [S3] | Yes [S31] |
| Mastodon | **No** (absent from S2, S4, S7) | No | No | No |
| Reddit | Yes [S2] | **No** ("You cannot publish and schedule a post to your Reddit account") [S2] | Posts (hourly) [S3] | Listening [S4][S18] |
| Tumblr | **No** (absent from S2, S4, S7) | No | No | No |
| WhatsApp | **Yes** [S2] | No | Messages (immediate) [S3] | UNVERIFIED |
| Line (extra) | Yes [S2] | No | Messages [S3] | UNVERIFIED |

## S2. Publishing

- **Scheduling, calendar**: multi-network scheduling with live previews including Stories; custom calendars in List, Weekly, Monthly, Grid layouts; "Projects" boards **[S6][S29]**.
- **Queue / time slots**: "Publish categories" are weekly cycles of posting times (up to 200 categories per space, 100 slots each) **[S28]**; "Best Time Posting Queue" on Lite **[S1]**.
- **Best-time suggestions**: "Optimal Times" from up to 180 days of past performance **[S6][S28]**.
- **Bulk / CSV**: bulk scheduling on Lite and up **[S1]**; CSV of captions, times, images, GIFs, video links **[S29]**.
- **Per-network customization**: yes, per post including Stories **[S6]**; Post Groups to edit posts for several profiles together **[S6]**.
- **First comment, Stories/Reels/carousels, Threads, polls**: yes, network-dependent **[S7]**.
- **Drafts**: help article "How To Save And Schedule Drafts?" exists **[S37]**.
- **Evergreen / recycling**: no named recycling feature; categories repeat *time slots*, not posts **[S28]**. UNVERIFIED (likely absent in current product).
- **RSS**: RSS feeds pull articles into a list; **no auto-publishing** ("Copy to compose"); Lite 5 feeds, Premium 100, Enterprise up to 500 **[S27]**.
- **Media library**: Asset Manager with unlimited assets, tags and sharing **[S29]**.
- **Link shortener / UTM**: built-in sbrew.link shortener and Bitly **[S7][S4]**; UTM links **[S6]**.
- **Link in bio**: BrewLink (Standard+) **[S1][S6]**.
- **Publish Rules** auto-moderate posts against guidelines (approvals, tagging, alerts, compliance checks) — Premium **[S6][S7]**.
- Trending audio for TikTok and Instagram Reels **[S6]**.

## S3. Content creation

- **Brew AI** for captions ("settle tone, mood, and style") and translation **[S6][S7]**.
- **AI Assistant** in the reply box: translate (17 languages), rephrase, concise, friendly, formal, expand, fix grammar; Premium and Enterprise; uses AI credits; "It never sends replies" **[S10]**.
- AI report builder and AI Keyword Assistant **[S7]**; brand-voice help article exists **[S37]**.
- **Repurposing long content (blogs, video)**: not found. UNVERIFIED (likely absent beyond RSS-to-compose).
- **Templates**: Canva network-ready templates **[S4]**; rule templates referenced **[S8]**. No post-template library found.
- **Hashtag tools**: Instagram hashtag tracking via listening **[S4]**; hashtag suggestions UNVERIFIED.
- **Image/video editing**: UNVERIFIED (not described on S6/S29).
- **Design integrations**: Canva, Google Drive, Dropbox **[S4]**.

## S4. Engagement (inbox)

- **Unified inbox** for organic and paid conversations from social, messaging (incl. TikTok DMs, WhatsApp, Line) and review platforms; "Auto-backfill all historic comments/ad comments"; "25+ conversation types"; no charge per conversation **[S5][S3]**.
- **Team inboxes**, assignment, bulk actions, saved replies (macros), built-in social CRM **[S7]**; assignment workflows are listed under Premium **[S1]**.
- **Filters**: 25+ combinable filters including AI sentiment, campaign, keywords, type (ad/organic comment, mention, review, message), first-contact and repeat customers **[S5]**.
- **Saved, suggested and auto-replies**; hide/delete one or 100 comments with one click; collision detection; parent-child tagging **[S5]**.
- **AI**: conversation summaries, sentiment with intensity scores, tone adjustment, translation **[S5]**; reply-box AI Assistant (Premium+) **[S10]**.
- **Automatic replies (Rule Engine, Engage rules)** **[S9]**:
  - Private-message (DM) replies to Facebook and Instagram organic and ad comments, DMs, and TikTok messages; public replies to Google Business reviews (Facebook/Instagram public replies only "on request" via support).
  - "Write your reply" (one of several messages sent at random) or **"Let AI reply (Premium plan only)"** from instructions plus the incoming message; AI reads the last 15 messages (max 50); optional fallback text up to 1,000 characters "so the rule never silently skips a reply".
- **Moderation**: Lite includes hiding organic Facebook/Instagram comments **[S1]**. Rules can hide (Facebook, Instagram incl. ads, TikTok, YouTube, X replies, Threads replies), delete (Facebook, Instagram, YouTube, LinkedIn), assign, close, add watchers, move to team inbox, mark priority, set sentiment, tag conversations and contacts, update custom fields, and alert via Slack, Teams, in-app or **email (internal and external addresses)** **[S9]**. Conditions include conversation type, keywords (with AI "Describe intent"), attachments, link/mention counts, story mentions, sentiment, language, star rating, post text/tags, contact tags/follower count/first seen/conversation count, UTM fields **[S8]**. Marketing also cites "intensity-based moderation for toxic, offensive, or inappropriate comments" and filtering images, URLs, emails, phone numbers, emojis **[S5]**.
- **Sentiment**: AI sentiment on Premium **[S1]**; sentiment and language conditions need Premium **[S8]**.
- **Contact / CRM history**: built-in social CRM; HubSpot and Salesforce sync (Salesforce on Enterprise) **[S7]**; Enterprise plan lists HubSpot & Salesforce **[S1]**; "We can also expose WebHooks and APIs" for other CRMs **[S5]**.
- **SLA**: team & SLA reporting on Premium **[S1]**.
- **Mobile**: Companion app (iOS, Android) with inbox and notifications **[S33]**.

## S5. Reviews

- **Read and reply**: Google Business Profile reviews (plus Q&A and messages beta) **[S5][S3]**; Facebook Page reviews/recommendations **[S4][S3]**; Apple App Store and Google Play Store reviews (**Enterprise only**) **[S22][S23]**; Trustpilot (**Enterprise only**, and the customer needs a paid Trustpilot plan; replies can be posted, not edited) **[S24]**.
- **Yelp**: listed on the integrations page ("proactive moderation and responses to Yelp reviews") **[S4]** but absent from the connectable-profiles list **[S2]** and the conversation-types list **[S3]**, with no Yelp help article in the sitemap **[S37]**. UNVERIFIED.
- **TripAdvisor**: not listed **[S2][S4]**.
- **Automated review replies**: public auto-replies to Google Business reviews, AI-written on Premium **[S9]**.
- Review management "from hundreds of locations in one single dashboard" **[S5]**.
- **Review-request campaigns**: not found **[S37]**. UNVERIFIED (likely absent).

## S6. Social listening

- Included on **Premium** ("Social & Web Listening") **[S1]**: 4 topics / 1,000 conversations per month; extra capacity $50/month per 2 searches and 1,000 conversations; Enterprise higher (unstated) **[S18]**.
- Sources: Instagram (hashtags), X (off by default due to API cost), YouTube, Reddit, Threads, web (blogs and news) **[S18][S4]**.
- Brand and competitor keywords, Listening Performance report with sentiment trends; reply to listened conversations from Engage **[S18]**. Alerts: not mentioned **[S18]**.
- **Competitor benchmarking**: Premium **[S1]**; competitor metrics in reports **[S15]**.

## S7. Analytics and reporting

- 250+/275+ metrics, 20+ visualization types, dashboards and widgets, drill-down, notes, folders **[S15]**; templates, Widget Builder, AI-assisted widgets, organic vs. paid, competitor comparisons, team & SLA reporting **[S7]**.
- Standard: reporting with **18 months of data backfill**; Premium: team & SLA reporting, competitor benchmarking **[S1]**.
- **Exports and delivery**: PDF (grid or stack), widget CSV/PNG; **scheduled reports emailed automatically**; external view-only live links; embed on web pages **[S15]**; scheduled PDF reports daily/weekly/monthly for agencies **[S14]**.
- **White-label / custom branding**: Enterprise lists "Platform Customization" **[S16]**; no help article describing white-labelled reports was found. UNVERIFIED.
- **ROI / UTM**: Google Analytics 4 integration with UTM filters for traffic, conversions, revenue **[S4]**.
- **API**: read-only **Insights API** (Bearer token provisioned by account manager): included in Enterprise, **$399/month add-on on Premium** **[S17]**.
- Changelog: four new Meta ad metrics; Facebook reach breakdowns blank from June 2026 because Meta stopped providing them **[S34]**.

## S8. Collaboration and approvals

- **Roles**: Primary Owner, Owner, Admin and others; only these three connect profiles and create rules **[S21][S8]**. User groups, granular permissions **[S14]**.
- **Approval workflows** (**Premium+**; Premium up to 100 workflows, Enterprise 200): single or multi-step; "Require approval from all" per step; rejection requires a reason; hourly and expiry reminders; emails held 5 minutes and skipped if read in-app; "Suppress approval notifications" option **[S12]**.
- **External approvers**: added by email in a workflow step; "without taking a user seat"; email with "a magic link that signs them straight in to their approvals page" ("No password needed"); can "review, approve, reject, comment and suggest edits… nothing more"; sign-in at external.statusbrew.com **[S13]**.
- **Guest users** (unlimited): view, comment, suggest edits **[S7]**; shareable calendars **[S6]**.
- **Inbox reply approvals**: not mentioned in the approval article **[S12]**. UNVERIFIED (likely absent).
- **Audit trail**: "Governance Rules & Audit Logs" **[S1]**; Enterprise grid lists Complete Logging, Team Activity Log, Audit User, Live Team Activity **[S16]**. Notification Center for mentions, approval requests, assignments **[S34]**.

## S9. Agency / multi-client

- **Spaces**: one isolated workspace per client with its own profiles, inboxes, tags, rules, reports **[S14]**; profile groups for bulk actions **[S7]**.
- **Agency pricing**: "From $49 /mo/client" with "Unlimited spaces, shareable calendars, reports" **[S1][S14]**.
- **Client profile connection**: a Shared Link lets the client authorize their own profiles without joining the workspace or sharing passwords **[S7][S21]**.
- **Client portal**: external approver page and shared report/calendar links **[S13][S15]**.
- **White-labeling**: not described on the agency page **[S14]**. UNVERIFIED.
- **Per-client billing**: price is per client, invoicing not described **[S14]**.

## S10. Ads

- **Boosting**: boost Facebook (and Instagram) posts from Compose or Planner, including drafts and scheduled posts; dark posts; needs the ad account connected **[S26]**.
- **Ad-comment management**: auto-hide all or campaign-specific comments on Facebook/Instagram regular and dynamic ads, delete, unhide, link filtering, ML toxic filtering **[S32]**; TikTok ad comments (help article exists) **[S37]**; ad comments sync after the Meta ad account is connected **[S3]**.
- **Ads reporting**: Facebook Paid Performance Report on Premium/Enterprise **[S26]**; Meta ad purchase/result metrics **[S34]**.
- **Campaign creation / ads management**: not found **[S32]**. UNVERIFIED (likely absent).

## S11. Email

- **Email marketing / newsletters / posts as email**: none found **[S1][S7][S4]**.
- **Rule-engine email actions**: "Send email" to Statusbrew users or groups and **"Send email to external users"** **[S9]**.
- **Approvals by email**: external approvers get an email with a passwordless magic link **[S13]**; internal approvers get email per their settings **[S12]**.
- **Scheduled report emails** (PDF) **[S15][S14]**.
- Vendor policy: "No Upselling, No Sales Emails" **[S1]**.
- **Email inbox integration**: not found. UNVERIFIED.

## S12. Automation and AI agents (and guardrails)

- **Engage rules** act without a person (hide, delete, assign, tag, route, alert, auto-reply, AI auto-reply) **[S9]**. Guardrails **[S8][S9]**: only Primary Owners/Owners/Admins create or pause rules; hide/delete require a conversation-type filter, and the docs warn that without keywords a rule "hides or deletes all comments"; AI fallback text; rules apply only to new incoming conversations; "Stop execution of following rules"; working-hours and date-range scheduling; per-org rule limits (Lite 25, Standard 100, Premium 500, Enterprise 2,000). **No human approval step and no test mode are documented**; AI replies are generated and sent per matching message **[S9]**.
- Plan note: the pricing page puts "Rule Engine for automated comment management" on **Standard** **[S1]**, while the Rule Engine help article says "available on Lite and above" **[S8]**. Conflicting.
- **Publish rules** (Premium) for compliance checks before posting **[S7]**.
- **MCP** (listed on the Enterprise plan card **[S1]**): read/reply inbox, create/update/reschedule posts, approve/reject, calendar, reporting; "The AI can only see and do what your own Statusbrew account already can"; OAuth 2.0 scoped token; "Your AI writes the replies. Confirm each one and it goes out as you"; "Your AI saves everything as a draft. You schedule it in Statusbrew" **[S11][S5][S6]**. A newer integrations-page line says you can ask it "to publish a post" **[S4]**, which is looser than the S6 wording.

## S13. Integrations and API

- Slack, Microsoft Teams, HubSpot, Salesforce, Dropbox, Google Drive, Canva, Bitly, Google Analytics **[S4]**; Zendesk and Zapier "added on request" (Enterprise grid) **[S16]**; HubSpot/Salesforce on Enterprise **[S1]**.
- MCP for Claude, ChatGPT, Gemini, Copilot (Grok and Copilot Studio named on S4) **[S1][S4][S11]**.
- **Public API**: read-only Insights API only (Enterprise, or $399/mo on Premium) **[S17]**; developer portal at https://developer.statusbrew.com/ (JavaScript-rendered; contents not readable here, UNVERIFIED).
- **Mobile**: Companion app for iOS and Android **[S33]**.

## S14. Pricing

Browser-rendered 2026-11-18; "all our prices are in USD" **[S1]**:

| Plan | Annual billing | Monthly billing | Users / profiles | Highlights |
|---|---|---|---|---|
| Lite | $69/mo | $89/mo | 1 / 5 | Unlimited posts, best-time queue, bulk scheduling, inbox, hide organic FB/IG comments, email support |
| Standard | $129/mo | $179/mo | 3 / 10 | BrewLink, reports with 18-month backfill, Google Business Profile, Rule Engine, chat support |
| Premium ("recommended") | $229/mo | $299/mo | 6 / 15 (both expandable) | AI sentiment, assignment and approval workflows, team & SLA reporting, competitor benchmarking, listening |
| Enterprise | Custom | Custom | Unlimited / unlimited | Account manager, HubSpot & Salesforce, Insights API, SAML SSO, MCP, compliance support |
| Agency offer | From $49 per client per month | | Unlimited spaces | Shareable calendars, reports |

- **Add-ons**: extra users/profiles on Premium (price not shown) **[S1]**; listening $50/mo per 2 searches + 1,000 conversations **[S18]**; Insights API $399/mo on Premium **[S17]**.
- **Free plan**: none mentioned **[S1][S19]**. **Trial**: 14 days, no card; access simply stops if you don't upgrade **[S19]**. "Avail free migration & onboarding services" **[S1]**.
- Nonprofits: flat 50% off Standard and Premium **[S1]**. "No Price Hikes, Ever. The price you sign up for is the price you keep." **[S1]**.
- **PayPal: not listed.** Accepted: "credit card, invoice, and some debit cards in the U.S. only"; invoice only for annual billing; cards via Stripe **[S20]**. Whether PayPal is ever accepted: UNVERIFIED.

## S15. Onboarding

1. Pricing page → choose plan → "Try free" → email, full name, phone, password → organization details (business name, category, team size, time zone) → role and goals → connect at least one social profile **[S19]**.
2. Connect profiles: Settings → Profiles → Add profile → network → checklist → sign in on the network and grant access → finish profile details **[S21]**. Network-side admin access needed (e.g. full control of a Facebook Page) **[S21]**.
3. Optional: client Shared Link to connect profiles **[S21]**.
- **Developer app / API keys for social networks: none documented** (redirect-to-network authorization) **[S21][S36]**. WhatsApp connects through Facebook login with a WhatsApp Business account; the phone number must not still be running in the WhatsApp consumer or Business app **[S25]**. Exceptions where the customer creates credentials:
  - **Bluesky**: handle plus a Bluesky App Password **[S21]**.
  - **App Store** (Enterprise): App Store Connect API key (.p8), Issuer ID, Key ID **[S22]**.
  - **Play Store** (Enterprise): customer's own Google Cloud service account and JSON key, invited into Play Console **[S23]**.
  - **Trustpilot** (Enterprise): customer creates a Trustpilot API application and supplies API key, secret and author user ID; requires a paid Trustpilot plan **[S24]**.

## S16. Distinctive points for a newcomer

- Engagement-first: 25+ conversation types, WhatsApp and Line messaging, ad-comment moderation as a headline use case, SLA reporting **[S5][S3][S32][S1]**.
- Rule Engine is a general automation layer (routing, tagging, alerts, external emails, auto-replies, AI auto-replies with fallback) but has no documented test mode or human-in-the-loop for auto-replies **[S8][S9]**.
- Pricing transparency claims: no price hikes for existing customers, no sales emails, no long contracts **[S1]**.
- App-store and Trustpilot review management, but Enterprise-only and requiring customer-created keys **[S22][S23][S24]**.
- Public changelog entries are undated, and the newest monthly "Product Changes" help article is October 2025 **[S34][S37]**, so recency is harder to judge than for Agorapulse or Vista.

---

## Implications for GeekSocial scope (observations, not decisions)

- **Nextdoor is not supported by any of the three.** Only Vista offers a workaround (a manual "custom profile" with reminder emails) **[V47]**.
- **"A person approves every reply"** is closest to Agorapulse (suggestions only; MCP confirms each reply; inbox approval workflows that can cover AI agents) **[A11][A13][A12]**. Vista and Statusbrew both ship fully automatic AI replies; their guardrails are configuration-time (testing, schedules, fallbacks, escalation), not per-message approval **[V6][S9]**.
- **Email** is a side channel for all three (alerts, approvals, scheduled reports). None does email marketing or reads an email inbox.
- **Customers never create developer apps for the main social networks** in any of the three. Customer-created credentials appear only for Bluesky app passwords and for app-store / Trustpilot / OpenAI-Ads connections **[A9][V43][V44][V46][V29][S21][S22][S23][S24]**.
- **X is now a paid extra** at Agorapulse and Vista because X's API access is paid **[A6][V19]**.
