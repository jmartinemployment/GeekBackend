# SMM competitor research: SocialBee, SocialPilot, Sendible, MeetEdgar

Prepared for GeekSocial scope definition. **All pages checked 2026-11-18** unless a claim says otherwise.

## How to read this file

- Every claim carries a source tag such as `[SB1]`. Each tool section opens with a **source key** that maps every tag to its URL.
- **Vendor pages were read first.** Third-party sources are used only where the vendor is silent, and they are labelled as third-party.
- **`(HC excerpt)`** marks a claim taken from a search-engine excerpt of a vendor help-center article. `help.socialbee.com`, `help.socialpilot.co` and `support.sendible.com` return HTTP 403 to automated fetching, so those articles could not be read in full. MeetEdgar's help center (`help.meetedgar.com`) could be read directly.
- **UNVERIFIED** means the point could not be confirmed from any source. "Not found" means the vendor's own pages were checked and the feature does not appear on them. That is weaker than a confirmed "no".
- **Live Black Friday promotions changed the displayed prices on the check date** for SocialBee, SocialPilot and MeetEdgar. Regular prices are given separately from the promotional prices.
- **Limit on this research:** the session's web-search budget ran out near the end. The last MeetEdgar checks were therefore done by direct fetches of help-center articles, without searching.

---

## Cross-tool summary (details and sources are in each tool's section)

| Question | SocialBee | SocialPilot | Sendible | MeetEdgar |
|---|---|---|---|---|
| Nextdoor | No | No | No | No |
| Pinterest | Yes | Yes | **No** | Yes |
| Bluesky / Threads | Yes / Yes | Yes / Yes | Yes / Yes | Yes / Yes |
| Mastodon, Reddit, WhatsApp | Reminder only ("Universal Posting", posted by hand) | No (WhatsApp only for review *requests*) | No | No |
| Tumblr | No | No | No | No |
| Unified inbox | Yes, every plan (FB, IG, Threads, LinkedIn page, X, YouTube) | Standard plan and up (FB, IG, LinkedIn page, Google Business Profile reviews and Q&A) | Yes (FB incl. ad comments, IG incl. DMs since 2026-10-29, LinkedIn page, YouTube) | EDGAR plan only (FB and IG) |
| AI-suggested or automatic replies | Not found | **Yes, AI auto-replies to DMs and comments (Jan 2026)**, with a send-after-approval option | Not found | No |
| Review sites | None | **Separate "Reviews" product**: replies to Google and Facebook reviews; monitors Tripadvisor, Yelp, Trustpilot; free tier | None found | None |
| Approvals | Yes (Accelerate and up); clients can join a workspace to approve | Manager approval (Standard+); **client approval by no-login email link** (Premium+); email reminders for pending approvals | Plus and up; client dashboards; approval emails | **None.** Up to 20 collaborators, all with full access |
| Email features | Email notifications | Emailed and scheduled PDF reports, approval links and reminders by email, email/SMS/WhatsApp review requests | Scheduled email reports, approval emails, **add content to the library by email**, reconnection-link emails | Weekly performance-report email |
| Email marketing / newsletters | No | No | No | No |
| Content categories and recycling | **Core model**: categories with time slots, evergreen re-queue, expiry | Repeat posts, queues, tags (no category recycling) | "Smart Queues" plus repeating posts | **Core model**: categories with time slots, library kept forever, LIFO order, variations |
| Pricing model | Tiered by profiles; extra users and workspaces sold as add-ons | Tiered by accounts **and users** ($4 per extra account, $5 per extra user) | **Unlimited users**; tiered by workspaces (6 profiles each) | Two tiers by accounts; 20 users included |
| Entry price (monthly billing, USD) | $29 | $30 | $35 | $29.99 |
| Free trial | 14 days, no card | 14 days, no card | 14 days, no card | 30 days |
| PayPal | **Yes** | **Yes** | Not listed (cards; invoice via sales) | Not listed (cards only) |
| Customer creates a developer app or API keys | **No** | **No** (API key only for optional MCP use in Cursor, Windsurf or custom setups) | **No** (a publishing API with admin-generated keys is "on its way") | **No** (Edgar's own API key is used only for Zapier) |

---

# 1. SocialBee

### Source key

| Tag | URL |
|---|---|
| SB1 | https://socialbee.com/pricing/ |
| SB2 | https://socialbee.com/agency |
| SB3 | https://socialbee.com/concierge/concierge-migration/ |
| SB4 | https://socialbee.com/concierge/ |
| SB5 | https://conciergebee.com/social-media-specialist |
| SB6 | https://conciergebee.com/white-label (redirect target of socialbee.com/concierge/white-label/) |
| SB7 | https://socialbee.com/features/ |
| SB8 | https://socialbee.com/social-media-publishing |
| SB9 | https://socialbee.com/social-media-management/ |
| SB10 | https://socialbee.com/universal-posting/ |
| SB11 | https://socialbee.com/ai-social-media-post-generator/ (Copilot) |
| SB12 | https://socialbee.com/social-media-engagement |
| SB13 | https://socialbee.com/social-media-analytics/ |
| SB14 | https://socialbee.com/social-media-collaboration/ |
| SB15 | https://socialbee.com/integrations/ |
| SB16 | https://changelog.socialbee.com/ (socialbee.com/roadmap/ redirects here) |
| SB17 | https://releasebot.io/updates/socialbee (third-party mirror of the changelog, with dates) |
| SB18 | https://help.socialbee.com/article/110-how-can-i-add-an-account (HC excerpt) |
| SB19 | https://help.socialbee.com/hc/en-us/articles/29979155669143-All-SocialBee-s-Integrations (HC excerpt) |
| SB20 | https://help.socialbee.com/article/147-content-approval (HC excerpt) |
| SB21 | https://help.socialbee.com/article/148-users-and-roles (HC excerpt) |
| SB22 | https://help.socialbee.com/article/368-universal-posting (HC excerpt) |
| SB23 | https://help.socialbee.com/article/73-evergreen-vs-share-once (HC excerpt) |
| SB24 | https://help.socialbee.com/article/359-social-media-copilot (HC excerpt) |
| SB25 | https://docs.xara.com/en/articles/5313071-social-bee-connecting-your-social-media-accounts (third party) |
| SB26 | https://replient.ai/en/comparison/socialbee-alternative (competitor page, October 2026; biased) |

## 1.1 Supported networks

**Direct posting:** Facebook, Instagram, Threads, LinkedIn, X, Pinterest, TikTok, YouTube, Google Business Profile, Bluesky [SB1][SB9][SB15].

**"Universal Posting"** covers networks SocialBee cannot post to through an API. Posts are scheduled in SocialBee, a phone notification fires at the scheduled time, and **the user publishes by hand**. Networks named: Facebook Groups, Reddit, WhatsApp, Telegram, Mastodon, Quora, "and more" [SB10][SB22]. Section 1.2 describes the workflow.

**Account types** (HC excerpt) [SB18]:
- Facebook profiles, Pages and Groups.
- Instagram Business, Creator and personal profiles. Personal Facebook and Instagram profiles get reminders, not direct posts.
- LinkedIn personal profiles and Company Pages. Both are posted to directly.

Note: Meta shut down the Facebook Groups API; MeetEdgar's help center dates the cutoff to 22 April 2024 (see section 4.1). SocialBee's own Universal Posting page lists Facebook Groups as a reminder-only network [SB10], so the help-center wording about Groups may be out of date.

| Network | Publish | Inbox / engagement | Analytics |
|---|---|---|---|
| Facebook | Yes, Pages [SB1]; Groups and personal profiles by reminder only [SB10][SB18] | Yes, Pages: comments, mentions, DMs [SB1][SB12] | Yes, plus "Competitor analysis" [SB13] |
| Instagram | Yes; personal profiles by reminder [SB18] | Yes, Business: comments, mentions, DMs [SB12] | Yes, with audience demographics [SB13] |
| Threads | Yes [SB1] | Yes: comments, mentions [SB12] | Yes [SB13] |
| X | Yes [SB1] | Yes: comments, mentions (DMs not listed) [SB12] | Yes [SB13] |
| LinkedIn Page | Yes [SB1] | Yes, Company: comments, mentions [SB12] | Yes, Company [SB13] |
| LinkedIn profile | Yes (HC excerpt) [SB18] | No; only Company is listed [SB12] | No; only Company is listed [SB13] |
| TikTok | Yes [SB1] | No; not listed [SB12] | Yes [SB13] |
| YouTube | Yes [SB1] | Yes, comments only [SB12] | Yes [SB13] |
| Pinterest | Yes [SB1] | No [SB12] | Yes [SB13] |
| Google Business Profile | Yes [SB1] | No; reviews are not mentioned [SB12] | Yes: page, post and account-activity analytics [SB13] |
| Nextdoor | No; not on any list [SB1][SB10][SB15] | No | No |
| Bluesky | Yes [SB1] | No [SB12] | Not listed on the analytics page [SB13] (UNVERIFIED) |
| Mastodon | Reminder only [SB10] | No | No |
| Reddit | Reminder only [SB10] | No | No |
| Tumblr | No; not listed [SB1][SB10] | No | No |
| WhatsApp | Reminder only [SB10] | No | No |

## 1.2 Publishing

- **Scheduling and calendar:**
  - Posts can be set for a specific date and time [SB8].
  - Calendar views: Overview, Feed, Calendar, Grid and List [SB8].
  - Time-zone support [SB9].
- **Best-time suggestions:** "personalized best posting time suggestions based on your top-performing posts" [SB8].
- **Bulk and CSV:**
  - The Bulk post editor and CSV uploads are on **Accelerate and up**, not Bootstrap [SB1].
  - The bulk editor pauses, edits, approves or deletes every post in a category at once [SB9].
- **Per-network customization:** Posts can be customized per network, and AI "can customize content automatically" [SB7][SB9].
- **First comment:** Yes [SB7][SB9].
- **Alt text and Instagram location:** Yes [SB9].
- **Stories, Reels, carousels:** Not described on SocialBee's feature pages [SB7][SB8]. UNVERIFIED.
- **Drafts:** "Use the 'draft' feature to save your social media posts and let your team have a look before publishing" [SB8]. Posts that are not approved behave as drafts and will not publish until set to "Approved" (HC excerpt) [SB20].
- **Content categories and evergreen recycling (SocialBee's signature feature):**
  - Posts are grouped into categories and scheduled across several accounts together [SB8].
  - A whole category can be paused: "Just flip a switch with one click" [SB8].
  - RSS imports and bulk imports sort posts into topic folders [SB7].
  - **Evergreen vs Share Once:** SocialBee recommends a mix of evergreen posts (re-queued after they publish) and share-once posts. Recycling can be applied to a single post or to several posts in a category (HC excerpt) [SB23].
  - "By re-queueing your posts, you can cultivate an evergreen content cycle" [SB8].
  - **Expiry:** Posts can "expire them at a specific date or after several shares" [SB8].
  - **Plan limits:** categories 10 / 50 / unlimited; posts per category 1,000 / 5,000 / 5,000 (Bootstrap / Accelerate / Pro) [SB1].
  - **Recent change:** On 2026-10-20 the changelog announced that "Category Sequence" and "Random" categories are being replaced by "active categories". The "Next Posts" view is being retired in favour of the Calendar and List views. Biweekly, monthly and yearly scheduling is planned for "later this quarter" [SB16][SB17].
  - **Per-category UTM:** "custom UTM and Ref parameters for each content category" [SB13].
- **RSS / blog auto-posting:**
  - "Content sources" are capped at 10 / 30 / unlimited by plan [SB1].
  - RSS feeds import into categories [SB7].
  - The 2026-11-14 changelog notes "improved RSS feed imports" [SB17].
- **Media library:** Not described on the pages read. UNVERIFIED.
- **Image sources and editing:** Canva, Unsplash and GIPHY [SB1]. Crop, flip, rotate, and video thumbnail frame or custom cover [SB9].
- **Link shortener and UTM:**
  - Shorteners: Bitly, Rebrandly, Replug, Switchy, RocketLink, BL.INK, JotURL [SB7][SB15].
  - UTM and Ref parameters can be added [SB7].
- **Link-in-bio:** Not found [SB7][SB8][SB9].
- **Universal Posting workflow** [SB10][SB22]:
  1. Create a Universal profile in the web dashboard.
  2. Schedule content to it.
  3. At posting time the SocialBee mobile app (iOS/Android) sends a push notification.
  4. On Android, tapping it copies the caption, downloads the media and opens the target app. The user finishes the post by hand.
  5. A missed reminder stays in the profile's "To Share" list.

## 1.3 Content creation

- **AI Copilot:** "SocialBee's AI social media manager built to plan, create, optimize and manage every post" [SB11].
  - Input: a few answers about the brand, or the brand's website URL (HC excerpt) [SB24].
  - Output: a strategy with "pillars, schedule and campaigns", platform recommendations, generated content categories, a recommended posting plan, and posts with captions, hashtags, platform variations and "AI-generated visuals" [SB11].
  - The FAQ says "nothing is posted without your approval" [SB11].
  - SocialBee's help center calls the Copilot "[BETA]" (HC excerpt) [SB24].
- **AI generation is "unlimited" on all three standard plans** ("AI Copilot and unlimited AI content generation") [SB1].
- **Caption controls:** tone, length, emoji and hashtag toggles, and "1K+ ready prompts" [SB11].
- **Repurposing:**
  - Copilot output includes "repurposed content" [SB11].
  - "Post variations" re-present one piece of content as image, video or text-only [SB7].
  - No feature turns a blog post or video into posts. Not found [SB9].
- **Hashtags:** A hashtag generator saves hashtags into collections [SB7]. The "Hashtag organizer" is on Accelerate and up [SB1].
- **Design tools:** Canva, Unsplash and GIPHY on every plan [SB1].
- **Templates:** Not found as a product feature.
- **Free AI post generator** (no account needed) [SB9].

## 1.4 Engagement

- **Unified inbox on every plan:** "every plan includes the engagement inbox" [SB12][SB1].
  - Organised into "Boards" (by campaign, platform or client) and "Streams" (mentions, comments, DMs) [SB12].
  - Notifications arrive as new items come in [SB12].
  - Replies and likes can be sent from the inbox [SB7].
- **Internal notes:** "Internal notes help the team agree on responses" [SB7][SB9]. The engagement page itself does not describe inbox notes [SB12].
- **Not found on vendor pages** [SB12]: assignment, saved replies, AI-suggested replies, auto-replies or chatbots, moderation rules (auto-hide), sentiment, contact or CRM history. A competitor's October 2026 comparison calls SocialBee's inbox manual with no AI responses [SB26] (biased source).

## 1.5 Reviews

None. The inbox networks do not include reviews, and Google Business Profile inbox support is not listed [SB12]. No review-site support was found.

## 1.6 Social listening

No keyword or brand listening was found [SB9][SB13]. The only competitor feature is "Competitor analysis" in Facebook analytics, with no further detail [SB13].

## 1.7 Analytics and reporting

- **Per-network analytics** (networks listed in section 1.1) [SB13]:
  - Page analytics, post analytics, category metrics, post-type metrics, post history and a daily engagement map.
  - Audience demographics for Instagram; audience country and industry for LinkedIn; country distribution for YouTube.
- **Cross-network:** "Profile analytics" across all accounts over chosen timeframes [SB13].
- **Competitor benchmarks:** Facebook only ("Competitor analysis") [SB13].
- **Reports:**
  - PDF export to share with team and clients [SB13].
  - "Export branded reports" is on **Pro only** [SB1].
  - "Advanced analytics" is on Accelerate and up [SB1].
- **Analytics history:** up to 3 months on Bootstrap; up to 2 years on Accelerate and Pro [SB1].
- **Scheduled email delivery of reports:** Not found [SB13].
- **ROI / UTM:** UTM and Ref per category; no ROI metrics [SB13].
- **Metric change:** Facebook "Impressions" was replaced by "Views" to follow Meta (changelog, June/Aug 2026) [SB17].

## 1.8 Collaboration and approvals

- **Roles** [SB14]:
  - Owner: whole account, can create and delete workspaces.
  - Manager: Owner's access except deleting workspaces.
  - Publisher: views analytics; creates, edits and **approves** content.
  - Contributor: creates content, **cannot approve**.
  - The help center also lists "Team members", "Clients" and "Personal assistants" as user types (HC excerpt) [SB21].
- **Approval workflow:** draft → review → approval. Clients "can join a workspace with limited access to review drafts and approve or request changes" [SB14]. There is no separate no-login client-approval link (not found).
- **Plan availability conflicts:**
  - The pricing table puts "Post approval workflow" on Accelerate and up and "Internal notes/feedback" on Pro only [SB1].
  - The collaboration page says "Every plan includes basic collaboration tools like roles, notes, and approvals" [SB14].
- **Notes:** Feedback is left on the post itself, with @mentions [SB9][SB14].
- **Notifications:** by email: "Get pinged the moment someone comments or makes a change" [SB14].
- **Audit trail:** Not found [SB14].

## 1.9 Agency and multi-client

- **Workspaces:**
  - 1 workspace on Bootstrap and Accelerate; 5 on Pro [SB1].
  - Agency plans Pro50, Pro100 and Pro150 have 10, 20 and 30 workspaces, with 5 users per workspace [SB2].
  - Agency plans "include the same features as Pro"; the difference is scale [SB1][SB2].
- **Client portal, white-label software, per-client billing:** None on SocialBee's pages [SB2].
- **White label is offered only for ConciergeBee services, not for the software** [SB4][SB6]. See section 1.15.

## 1.10 Ads

- No ads management or boosting in the app was found.
- Post analytics show paid and organic reach [SB13].
- **Ads are sold as done-for-you services:** ConciergeBee "Ads campaign setup" and "Ads management" [SB4], and a "Boosted Posts" add-on to the Social Media Specialist service [SB5].

## 1.11 Email

- **In the product:** email notifications for comments and changes [SB14].
- **No email marketing or newsletters**, no sending posts as email, and no email-based approval or report delivery were found [SB9][SB13][SB14].
- **Email in services and billing:**
  - ConciergeBee sends its intake form by email after purchase [SB4][SB5].
  - The SocialBee University discount code arrives by email after a yearly purchase [SB1].

## 1.12 Automation and AI agents

- **What runs without a person:**
  - Evergreen recycling and category-driven scheduling [SB8].
  - RSS import into categories [SB7].
  - Concierge Migration sets migrated posts to "not approved", so nothing publishes until reviewed [SB3].
- **Guardrails:**
  - Copilot output must be approved: "nothing is posted without your approval" [SB11].
  - Posts that are not approved act as drafts and do not publish (HC excerpt) [SB20].
- **No auto-replies or chatbots** were found [SB12].

## 1.13 Integrations and API

- **Integrations page** [SB15]:
  - Canva, Unsplash, GIPHY.
  - Quu (content curation).
  - Bitly, Rebrandly, Replug, Switchy, BL.INK, JotURL, RocketLink.
  - Zapier, Pabbly, Make.
- **Not listed:** Google Drive, Dropbox, Slack, CRM [SB15].
- **Public API: none.** The help center says SocialBee "does not have a public API available yet"; a public API is on its "long-term roadmap" with no timeline (HC excerpt) [SB19].
- **Mobile app:** iOS and Android. It is the delivery mechanism for Universal Posting reminders [SB10][SB22].

## 1.14 Pricing

- **Standard plans, monthly billing** [SB1]:

  | Plan | Price | Profiles | Users | Workspaces |
  |---|---|---|---|---|
  | Bootstrap | $29/mo | 5 | 1 per workspace | 1 |
  | Accelerate | $49/mo | 10 | 1 per workspace | 1 |
  | Pro | $99/mo | 25 | 3 per workspace | 5 |

- **Agency plans, monthly billing** [SB2]:

  | Plan | Price | Profiles | Workspaces | Users |
  |---|---|---|---|---|
  | Pro50 | $179/mo | 50 | 10 | 5 per workspace |
  | Pro100 | $329/mo | 100 | 20 | 5 per workspace |
  | Pro150 | $449/mo | 150 | 30 | 5 per workspace |

- **Annual billing (regular):**
  - $290/yr Bootstrap, $490 Accelerate, $990 Pro, $1,790 Pro50, $3,290 Pro100, $4,490 Pro150 [SB3].
  - Per month that is about $24.17, $40.83, $82.50, $149.17, $274.17 and $374.17.
  - This matches the pricing-page FAQ ("starts at $24/month on the annual Bootstrap plan"; "annual plans come with a discount of 16%") [SB1].
- **Pricing-page conflict:** On 2026-11-18 the pricing page displayed annual list prices of exactly 12 × monthly ($348, $588, $1,188, $2,148, $3,948, $5,388), struck through to Black Friday prices of $174, $294, $594, $1,074, $1,974 and $2,694 [SB1][SB2].
- **Black Friday 2026:** code SOCIALBEEBF26, valid until 1 December: 70% off monthly plans for 3 months, or 50% off yearly plans [SB1].
- **Plan limits** [SB1]:
  - Content categories: 10 / 50 / unlimited.
  - Content sources: 10 / 30 / unlimited.
  - Posts per category: 1,000 / 5,000 / 5,000.
  - Analytics history: 3 months / 2 years / 2 years.
  - No monthly post cap is stated.
- **Add-ons (bought in the app)** [SB1]:
  - User: $10/mo or $100/yr.
  - Workspace: $10/mo or $100/yr.
  - 5 profiles: $15/mo or $150/yr.
- **Free plan:** none [SB1].
- **Free trial:** 14 days, no credit card. "Posting stops automatically once your trial expires" [SB1].
- **Payment:** credit/debit cards, **PayPal**, Amazon Payments. Prices exclude VAT, GST and other taxes. No contracts [SB1].

## 1.15 Onboarding and Concierge

- **Sign-up:** 14-day trial, no card [SB1].
- **Connecting accounts** (HC excerpt) [SB18]:
  - An **Onboarding Wizard** prompts the user to connect a first account; more are added from the dashboard.
  - Each network uses its own log-in or authorization.
  - Common failures are permission problems: Facebook Page Admin role, an Instagram Business or Creator account linked to the Page, and the LinkedIn Page Super Admin role.
  - Historically, a Facebook Group required installing the SocialBee app in the group (third party) [SB25]. Universal Posting has since replaced this [SB10].
- **Copilot onboarding** can start from a website URL [SB11][SB24].
- **Developer app or API keys:** **No evidence that customers ever create one.** Connections are per-network authorizations [SB18]. SocialBee has no public API to issue keys from [SB19].
- **Concierge services ("ConciergeBee", SocialBee's in-house agency)** [SB4]:
  - Social content creation: Social Media Specialist; Video First Specialist.
  - Engaged growth: IG community management; LinkedIn lead generation.
  - Content marketing: copywriting; guide creation.
  - Article writing; "Content machine".
  - Ads: campaign setup; ads management.
  - Website creation on Sitejet.
  - **How it works:** buy a tier → intake form by email (10–15 minutes) → a designated specialist is introduced, usually within one day. 30-day money-back guarantee; cancel any time.
- **Social Media Specialist tiers** [SB5]:

  | Tier | Price | Posts per week | Edit requests |
  |---|---|---|---|
  | Light | $150/mo | 3 | Within 3 working days, max 3 per week |
  | Standard | $240/mo | 4 + 1 video | Within 5 working days, max 4 per week |
  | Plus | $360/mo | 4 + 1 video | Within 7 working days, max 5 per week |

  - Content is delivered into SocialBee, another scheduler, or cloud storage.
  - Add-ons: Content Curation and Videos & Animated Posts at $120/mo each; Boosted Posts.
  - One-time pack of 10 posts: $120.
  - Yearly billing, available on request, saves "the equivalent of 2 months".
  - Onboarding: intake form → specialist call → website and social analysis → Client Brief within about 7 working days → feedback → weekly delivery.
- **Concierge Migration:** $199, or free with a yearly plan (time-limited offer) [SB3].
  - Moves content, posting schedule, RSS feeds and shortener settings, and sends a migration report.
  - Steps: 15-minute call, profile connection, secure credential sharing, migration.
- **White-label ConciergeBee for agencies** [SB4][SB6]:
  - Services: Social Media Specialist, Video First Specialist, Article Writing, Content Machine, Copywriting, Guide Creation.
  - Tiers: Bronze (base price), Silver (+10%, $299 setup), Gold (+20%, $499 setup, agency-branded intake form and agency-domain communication).

## 1.16 Distinctive points for a newcomer

- **Category-first scheduling with evergreen recycling is the identity.** The tool sells "set up categories once, the calendar fills itself" [SB8][SB23]. SocialBee is now simplifying it, retiring "Category Sequence" and "Random" [SB16].
- **"Universal Posting" lets SocialBee list networks it cannot post to** (Reddit, WhatsApp, Mastodon, Telegram, Quora, Facebook Groups) by turning them into phone reminders [SB10]. A newcomer comparing network counts should separate "direct" from "reminder" support.
- **Software plus done-for-you services.** ConciergeBee upsells human social media managers from $150/mo, offers them white-labelled to agencies, and SocialBee offers migration as a paid service [SB3][SB5][SB6].
- **Reliability is visible in the public changelog.** Logged incidents include:
  - 2026-07-27: an internal fault stopped scheduled posts for about 12 hours, and **missed posts were not republished**.
  - Repeated Bluesky, TikTok and X outages through 2026 [SB16][SB17].
- **Gaps a newcomer can exploit:** no AI reply drafting, no reviews, no listening, and no public API [SB12][SB19][SB26].

---

# 2. SocialPilot

### Source key

| Tag | URL |
|---|---|
| SP1 | https://www.socialpilot.co/plans (USD page as served 2026-11-18, Black Friday live) |
| SP2 | https://www.socialpilot.co/socialpilot-pricing.md (vendor's pricing document, dated July 2026; regular prices) |
| SP3 | https://www.socialpilot.co/pricing (served in INR to the fetcher; India plan set) |
| SP4 | https://www.socialpilot.co/features |
| SP5 | https://www.socialpilot.co/integrations?format=md |
| SP6 | https://www.socialpilot.co/product-update |
| SP7 | https://www.socialpilot.co/product-update/social-inbox-automation?format=md |
| SP8 | https://help.socialpilot.co/article/870-how-to-automate-replies-for-messages-and-comments (HC excerpt) |
| SP9 | https://www.socialpilot.co/social-media-engagement?format=md |
| SP10 | https://www.socialpilot.co/product-update/gbp-inbox?format=md |
| SP11 | https://www.socialpilot.co/reviews |
| SP12 | https://www.socialpilot.co/reviews-pricing.md (July 2026) |
| SP13 | https://help.socialpilot.co/article/796-how-to-automate-review-response-with-socialpilot-reviews (HC excerpt) |
| SP14 | https://www.socialpilot.co/mcp-server?format=md |
| SP15 | https://www.socialpilot.co/white-label-solution?format=md |
| SP16 | https://help.socialpilot.co/article/630-how-to-setup-white-label-with-socialpilot (HC excerpt) |
| SP17 | https://www.socialpilot.co/social-media-collaboration?format=md |
| SP18 | https://www.socialpilot.co/features/approvals-on-the-go?format=md |
| SP19 | https://help.socialpilot.co/article/339-how-to-approve-posts-sent-for-approval (HC excerpt) |
| SP20 | https://help.socialpilot.co/article/573-how-do-i-add-clients-to-my-team (HC excerpt) |
| SP21 | https://www.socialpilot.co/product-update/pending-approvals-reminder?format=md |
| SP22 | https://www.socialpilot.co/social-media-analytics?format=md |
| SP23 | https://www.socialpilot.co/features/client-management?format=md |
| SP24 | https://www.socialpilot.co/mobile-app?format=md |
| SP25 | https://www.socialpilot.co/linkedin-scheduler?format=md |
| SP26 | https://www.socialpilot.co/instagram-scheduler?format=md |
| SP27 | https://www.socialpilot.co/features/ai-pilot?format=md |
| SP28 | https://www.socialpilot.co/saas-white-label-integration |
| SP29 | https://help.socialpilot.co/article/614-can-i-target-audience-when-i-create-a-post (HC excerpt) |
| SP30 | https://help.socialpilot.co/article/348-what-is-social-inbox and /article/808-how-to-create-a-new-social-inbox (HC excerpts) |
| SP31 | https://help.socialpilot.co/article/430-how-do-i-connect-an-account-in-socialpilot and /article/434-how-do-i-connect-my-client-s-account-in-socialpilot (HC excerpts) |
| SP32 | https://www.socialpilot.co/listings/plans (search excerpt) |
| SP33 | https://www.socialpilot.co/blog/how-to-boost-facebook-posts (vendor blog, search excerpt) |
| SP34 | https://www.socialpilot.co/social-media-approval-software?format=md (vendor article; internally inconsistent on plans) |

## 2.1 Supported networks

**Ten networks:** Facebook, Instagram, TikTok, X, LinkedIn, Threads, YouTube, Pinterest, Google Business Profile, Bluesky [SP5][SP4]. **No** Nextdoor, Mastodon, Reddit, Tumblr or WhatsApp on any vendor list [SP5][SP4]. WhatsApp is used only by the separate Reviews product, to send review **requests** (added 2026-07-27) [SP6].

| Network | Publish | Inbox / engagement | Analytics |
|---|---|---|---|
| Facebook | Yes, Pages [SP5]; Groups UNVERIFIED (Meta shut the Groups API; see 4.1) | Yes, Pages: comments, messages, mentions [SP9][SP30] | Yes [SP22] |
| Instagram | Yes, Business/Creator: posts, Stories, Reels, mixed-media carousels, first comment [SP26][SP6] | Yes: comments, DMs, Story replies [SP9][SP1] | Yes [SP22] |
| Threads | Yes [SP5] | Not listed [SP9][SP30] | Not listed [SP22] (UNVERIFIED) |
| X | Yes [SP5] | Not listed [SP9][SP30] | Yes [SP22] |
| LinkedIn Page | Yes, including documents and carousels of up to 30 images [SP25] | Yes: page comments and mentions [SP25][SP30] | Yes, company pages [SP25] |
| LinkedIn profile | Yes ("Choose a LinkedIn profile or page") [SP25] | No [SP30] | No; company pages only [SP25] |
| TikTok | Yes [SP5] | Not listed [SP30] | Yes [SP22] |
| YouTube | Yes [SP5] | Not listed [SP30] | Yes [SP22] |
| Pinterest | Yes [SP5] | No [SP30] | Not listed [SP22] (UNVERIFIED) |
| Google Business Profile | Yes, including Event and Offer posts since 2026-09-17 [SP6] | **Yes: reviews and Q&A in the Social Inbox** [SP10] | Yes [SP22] |
| Nextdoor | No [SP5] | No | No |
| Bluesky | Yes [SP5] | No [SP30] | Not listed [SP22] (UNVERIFIED) |
| Mastodon | No | No | No |
| Reddit | No | No | No |
| Tumblr | No (only an outdated G2 review mentions it) | No | No |
| WhatsApp | No (review requests only) [SP6] | No | No |

## 2.2 Publishing

- **Queue and time slots:** "Queue Schedules" reuse best posting times across accounts [SP4]. "Smart Queues" are on every plan [SP1].
- **Calendar:** drag-to-reschedule calendar [SP4]. A bulk edit of scheduled posts arrived 2026-03-20 [SP6].
- **Best time:** "Automated Post Scheduler" publishes "at peak times" [SP4]. A free "optimal post time checker" tool exists for Facebook and Instagram (vendor sitemap). "AI Scheduler" is listed as a top feature [SP1].
- **Bulk and CSV:**
  - "upload up to 500 posts via CSV" [SP4].
  - Bulk Scheduling & CSV Uploads are **Premium and up** (dashes for Essentials and Standard in the comparison table, consistent with the plan cards) [SP1].
- **Per-network customization, post preview, first comment, LinkedIn documents, mentions, location tagging, Instagram image tagging, custom calls-to-action, drafts:** all listed on the plan comparison [SP1].
- **Saved Mention Groups:** added 2026-02-17 [SP6].
- **Content library, tags, repeat posts:**
  - "Repeat Posts: daily, weekly, or monthly recurrence" [SP4].
  - Content Library and Tags are on every plan [SP1].
  - **No category-driven evergreen recycling** like SocialBee or Edgar was found.
- **RSS auto-scheduling:** 5 / 10 / 15 / 20 feeds by plan [SP1].
- **Link shortening:**
  - A URL shortener is built in [SP1].
  - Bitly, Rebrandly and Sniply integrations [SP5].
  - UTM parameters in the composer [SP4].
- **Custom fields and image watermark:** Premium and up [SP1].
- **Link-in-bio:** Not found [SP4][SP26].
- **Media sources:** Canva, Unsplash, GIPHY, Google Drive, Dropbox, Box [SP5][SP1].
- **Facebook organic audience targeting:** preferred and restricted audiences, up to 16 interests; Pages only (HC excerpt) [SP29].

## 2.3 Content creation

- **AI Pilot** [SP4][SP27]:
  - Brand voice set once per client.
  - Captions and hashtag suggestions.
  - "Content Variations".
  - **"Post Repurposing: turn long-form content into a week of posts"** [SP4].
  - Content in 10 languages.
  - Seasonal and holiday ideas.
  - Proofreading.
- **AI credits:** 500 / 1,000 / 5,000 / unlimited by plan [SP1].
- **Image generation:** Not found [SP27].
- **Image editor and Holiday Calendar:** on every plan [SP1].
- **Design and media integrations:** Canva, Unsplash, GIPHY [SP5].
- **"AI Scheduler GPT"** lets users "Create and schedule posts in ChatGPT" (vendor navigation) [SP1].

## 2.4 Engagement

- **Social Inbox:**
  - Covers messages, comments and mentions [SP9]. Supported: Facebook Pages, Instagram Business/Creator and LinkedIn Pages (HC excerpt) [SP30]. Google Business Profile reviews and Q&A also arrive in the inbox [SP10].
  - **Standard plan and up.** Essentials has dashes for the inbox reply rows [SP1].
- **Organisation and team handling:**
  - "Add team members to manage multiple conversations simultaneously" [SP9].
  - Tags and Notes on conversations; sorting by priority; Account Groups to separate clients [SP9].
  - Bulk actions (mark, complete, tag) [SP4].
- **Per-conversation assignment:** not described [SP9]. UNVERIFIED.
- **Saved replies:** Not found [SP9].
- **AI:** "Use AI to refine responses for clarity and professionalism" [SP9].
- **Automatic replies ("Social Inbox Automation", released 2026-01-21)** [SP6][SP7]:
  - Auto-reply to DMs "with AI-generated replies or your own custom text".
  - Auto-replies on individual posts' comments.
  - **Auto-DM when someone comments (Instagram only)**.
  - Help-center excerpt [SP8]:
    - AI reply tones: Concise, Friendly, Professional, Empathetic.
    - Replies can be **sent right away, scheduled, or approved before sending**.
    - Comment-triggered DMs are not supported for Facebook and LinkedIn because of the networks' API rules [SP30].
  - Essentials does not include Social Inbox Automation [SP1].
- **Moderation (auto-hide), sentiment, CRM/contact history:** Not found [SP9].
- **Mobile:** inbox comments, messages and mentions in the iOS/Android app [SP24].

## 2.5 Reviews (separate "SocialPilot Reviews" product with its own plans)

- **Deep integrations (read and reply):** Google Business Profile and Facebook [SP11][SP12].
- **Monitoring only:** Tripadvisor, Yelp, Trustpilot [SP12].
- **"Review links" for requests:** Zillow, Influenster, Amazon, Angi, Bazaarvoice, Capterra, Gartner, Glassdoor, G2, X, Typeform, Wirecutter, Foursquare [SP12].
- **AI replies:**
  - "Automate review replies with AI in over 40 languages" [SP11].
  - Auto-reply rules are built from a trigger, an action and content, per connected location. Replies can be AI-written (tone and language chosen) or custom text (HC excerpt) [SP13].
- **Review requests:**
  - Email and SMS (Twilio) [SP11].
  - WhatsApp, added 2026-07-27 [SP6].
  - Bulk requests to up to 5,000 customers (2026-05-01) and duplicate-request prevention (2026-03-20) [SP6].
  - Ratings below average are sent to an intermediate feedback page first [SP11].
- **Other features:**
  - Customizable feedback forms (2026-03-05) [SP6].
  - Review reports [SP6].
  - Reviews in the mobile app (2026-04-15) [SP6].
  - Featuring reviews on social and on the website [SP11].
- **Pricing** [SP12]:

  | Plan | Price | Brands | Locations | Email credits per month |
  |---|---|---|---|---|
  | Starter | Free, no card | 1 | 1 | 100 |
  | Pro | $30/mo, or $25.50/mo billed annually | Unlimited | 3, plus add-ons | 5,000 |

  The location add-on price is shown only for India (₹300 per location per month) [SP12].
- **Listings** is a third product, with a free Starter tier for one location covering Google Business Profile listings and review monitoring (search excerpt) [SP32].

## 2.6 Social listening

- **No keyword or brand listening was found.** The vendor's listening articles review third-party tools [SP4][SP22].
- **Competitor tracking:**
  - "Competitor Reports" on Premium and up [SP1][SP22].
  - Free Instagram and Facebook "competitor analysis" tools, including a "Head-to-Head Instagram Analysis Tool" [SP1].

## 2.7 Analytics and reporting

- **Platform analytics:** Facebook, X, LinkedIn, Google Business Profile, Instagram, YouTube, TikTok [SP22].
- **Data depth:** 2 years of history; full figures up to nine digits (2026-04-02) [SP22][SP6].
- **Reports:**
  - Custom cross-account reports in PDF or CSV.
  - Competitor reports.
  - White-label PDF.
  - **Scheduled reports** and live-link sharing [SP22].
  - Report duplication (2026-09-25) [SP6].
- **Plan gating** [SP1]:
  - Detailed analytics, post performance, PDF reports, "Send PDF Report Via Email" and Schedule Reports: Standard and up.
  - Custom, Competitor and White Label Reports: Premium and up.
- **Boosted post performance:** organic and paid results tracked separately [SP4].
- **Campaign tags in analytics:** 2026-01-29 [SP6].
- **UTM:** in the composer [SP4]. ROI metrics are not described [SP22].

## 2.8 Collaboration and approvals

- **Roles:** Admin, Manager and Content Scheduler are named on the collaboration page [SP17]. A vendor article names "Manager, Admin, and Content Creator" [SP34]. Role definitions: UNVERIFIED.
- **Internal approval** (HC excerpt) [SP19]:
  - A scheduler's post goes to "Pending Review".
  - A Manager approves it, or sends it back with a comment for the scheduler to edit and resubmit.
- **Client approval** [SP18][SP19][SP20]:
  - Turned on per account with "Need Client Approval".
  - The client gets an **"Approve On-the-Go" link by email**, opens the content calendar **without logging in**, and approves or sends back with comments.
  - Clients who already have accounts get emailed approval links.
  - The invite link lets the agency limit which platforms a joining client may connect.
- **Pending Approval Reminders:** automated **email** reminders until posts are reviewed (2026-01-13) [SP6][SP21].
- **Mobile:** comments on scheduled posts from the app (2026-01-28) [SP6]; approvals from the app [SP24].
- **Plan gating** [SP1]:
  - Content and manager approval workflows: Standard and up.
  - Client management and client approval workflows: Premium and up.
- **Audit trail:** Not found [SP17].

## 2.9 Agency and multi-client

- **Client seats:** 0 on Essentials and Standard; unlimited on Premium and Ultimate [SP1].
- **Clients are a permission-limited role inside one account; there are no separate workspaces.** "Account Groups" separate clients' accounts [SP9][SP23]. A competitor says the same: "'Clients' are a permission-limited role inside a single workspace" (Sendible's comparison page, see SE14).
- **Client onboarding:** personalised email invitation links [SP23]; a reusable client invite link with platform restrictions (HC excerpt) [SP20]. Clients connect their own accounts after accepting (HC excerpt) [SP31].
- **White label:**
  - **White-label reports from Premium.** "Advanced White Label" (the full platform) is on **Ultimate** [SP1].
  - Covers: custom domain, branded login page, dashboard and browser-tab branding, **system emails sent from your own domain**, and branded PDF reports [SP15].
  - Setup is self-serve: colours, logo, favicon, custom domain with DNS/SSL, custom email [SP15][SP16].
  - Slack integration is not available to white-label users [SP1].
- **SaaS embedding:** a "SaaS white-label integration" offer for software companies (SSO; emails from the customer's white-label domain) [SP28].
- **Per-client billing:** Not found [SP23].

## 2.10 Ads

- **"Boosted Post Performance"** reporting (organic vs paid) [SP4].
- **Boosting:** The vendor blog says SocialPilot can "schedule and automatically boost your best-performing posts" (blog excerpt) [SP33]. The in-app boost workflow could not be confirmed. UNVERIFIED.
- **Ads management (campaigns, ad accounts):** Not found [SP4].

## 2.11 Email

- **Reports:** "Send PDF Report Via Email" and "Schedule Reports" (Standard and up) [SP1]. "Automated Report Scheduling: scheduled branded reports sent to clients" [SP4]. Consolidated PDF reports to client inboxes [SP23].
- **Approvals:** client approval links by email [SP18]; email reminders for pending approvals [SP21]; email and in-app notifications for approvals [SP34].
- **White-label system emails** from the agency's domain [SP15].
- **Reviews product:** email review requests with credits (100 on Starter, 5,000 on Pro) [SP12]; SMS via Twilio [SP11]; WhatsApp [SP6].
- **No email marketing or newsletters, and no email-inbox integration** [SP4][SP5].

## 2.12 Automation and AI agents

- **Social Inbox Automation:** AI or custom auto-replies to DMs and comments, and Instagram auto-DM on comment [SP7]. **Guardrail:** replies can be sent immediately, scheduled, **or approved before sending** (HC excerpt) [SP8].
- **Review reply automation:** rule-based (trigger, action, content), AI or custom text, per location [SP13].
- **MCP Server (2026-04-09, "included on every plan")** [SP6][SP14]:
  - Lets Claude (app and Claude Code), ChatGPT (paid, Developer Mode), Cursor, Windsurf and Zapier create and schedule posts, save drafts, and check scheduled, published or failed posts.
  - **Guardrails:**
    - The assistant posts only when asked; drafts allow review.
    - "Your AI assistant can only access the accounts and perform the actions allowed by your SocialPilot role."
    - Connecting adds no permissions.
    - Google Business Profile Event and Offer posts cannot be made through MCP.
    - Disconnecting does not cancel scheduled posts.
- **RSS auto-scheduling** [SP1] and repeat posts [SP4].

## 2.13 Integrations and API

- **Integrations** [SP5]: Slack, Unsplash, Google Drive, Giphy, Dropbox, Zapier, Sniply, Rebrandly, Bitly, Feedly, Canva, Box, WordPress, Zillow, ChatGPT, Shopify.
- **CRM:** Not found [SP5].
- **Browser extension and mobile app (iOS/Android):** on every plan [SP1][SP24].
- **Public API:**
  - Not on the USD plan matrix [SP1].
  - The India plan set lists "API access, SSO, enforced 2FA" under **Enterprise** [SP3].
  - Third-party pages say API access is Enterprise-only or not public. UNVERIFIED for USD customers.
- **MCP:** every plan [SP14].

## 2.14 Pricing

- **Regular USD prices** [SP2]:

  | Plan | Monthly billing | Annual billing, per month (~15% off) | Accounts | Users | Client seats | AI credits | RSS feeds |
  |---|---|---|---|---|---|---|---|
  | Essentials | $30 | $25.50 | 7 | 1 | 0 | 500 | 5 |
  | Standard | $50 | $42.50 | 15 | 3 | 0 | 1,000 | 10 |
  | Premium | $100 | $85 | 25 | 6 | Unlimited | 5,000 | 15 |
  | Ultimate | $200 | $170 | 50 | Unlimited | Unlimited | Unlimited | 20 |
  | Enterprise | Contact sales | Contact sales | Custom | Unlimited | Unlimited | Unlimited | — |

  Account, user and RSS limits are from [SP1]; AI credits from [SP1][SP2].
- **Black Friday 2026 (displayed on 2026-11-18)** [SP1]:
  - Monthly billing, 20% off: $24 / $40 / $80 / $160.
  - Annual billing, 30% off: $21 / $35 / $70 / $140 per month.
  - Trials started now keep the offer pricing until 18 December.
- **Add-ons:** extra social account $4/mo (all plans); extra user $5/mo (Standard and Premium) [SP1][SP2].
- **Free plan:** none for the social product [SP1]. Reviews and Listings have free Starter tiers [SP12][SP32].
- **Free trial:** 14 days, "No credit card required" [SP1][SP3].
- **Payment:** "We accept payments made with credit cards and **PayPal**" [SP1].
  - The same FAQ says payment is in Indian rupees. That line appears to be the geo-default text, because the page shows USD prices and the vendor's pricing document lists USD, EUR, GBP, AUD and INR [SP2].
  - Plans can be paused [SP1].
- **Regional plan set:** India uses Standard, Premium and Enterprise (₹2,000 / ₹6,000 / custom) with different limits [SP2][SP3].
- **Taxes:** prices exclude tax; no contracts [SP1].

## 2.15 Onboarding

- **Sign-up:** 14-day trial, no card [SP1].
- **Connecting accounts** (HC excerpts) [SP31]:
  - Go to Accounts > Connect Account, pick the platform, and authorize.
  - Facebook Pages and Instagram Business profiles need Admin or Editor access, and the Instagram account must be a Business account linked to a Page.
- **Agencies:** invite clients with a link; clients connect their own accounts [SP20][SP31].
- **Ultimate:** migration and onboarding service plus a dedicated account manager [SP1].
- **Developer app or API keys: No evidence that customers create one** for social connections. The only customer-side API keys are for the optional MCP connection from Cursor, Windsurf or custom clients. Claude, ChatGPT and Zapier sign in "without an API key" [SP14].

## 2.16 Distinctive points for a newcomer

- **A four-product suite under one brand: Social, Reviews, Listings, AI Pilot** [SP1]. The Reviews product is the most aggressive part (free tier, AI auto-replies in 40+ languages, SMS/WhatsApp/email requests) and gets most 2026 product updates [SP6][SP11].
- **Already ships AI auto-replies in the inbox** with an approve-before-send option [SP7][SP8]. That is the closest of these four tools to GeekSocial's "AI drafts, a person approves" model.
- **MCP server on every plan, scoped to the user's role** [SP14]. This is the most agent-ready of the four.
- **Per-account ($4) and per-user ($5) add-ons are cheap;** client approval and bulk scheduling are gated at Premium ($100) [SP1].
- **Regional pricing** (an India plan set in INR) and PayPal acceptance [SP1][SP2].

---

# 3. Sendible

### Source key

| Tag | URL |
|---|---|
| SE1 | https://www.sendible.com/pricing (full HTML read 2026-11-18: plan cards, price data attributes, feature breakdown, FAQ) |
| SE2 | https://www.sendible.com/changelog |
| SE3 | https://www.sendible.com/features/priority-inbox |
| SE4 | https://www.sendible.com/features/client-connect |
| SE5 | https://www.sendible.com/features/smart-queues |
| SE6 | https://www.sendible.com/features/automated-reports |
| SE7 | https://www.sendible.com/social-media-team-collaboration-tool |
| SE8 | https://www.sendible.com/white-label-social-media-management-software |
| SE9 | https://www.sendible.com/workspaces |
| SE10 | https://www.sendible.com/solutions/software-integrators |
| SE11 | https://www.sendible.com/features/ai-assist |
| SE12 | https://www.sendible.com/features/social-media-analytics |
| SE13 | https://www.sendible.com/features/social-listening (now serves the generic homepage; no listening content) |
| SE14 | https://www.sendible.com/socialpilot-alternative |
| SE15 | https://support.sendible.com/hc/en-us/articles/19440567324317 (Overview of AI Assist; HC excerpt) |
| SE16 | https://support.sendible.com/hc/en-us/articles/208052886 (Add to a Content Library using email; HC excerpt) |
| SE17 | https://support.sendible.com/hc/en-us/articles/208052076-Send-messages-for-approval (HC excerpt) |
| SE18 | https://app.livestorm.co/p/cbec0488-f4eb-41f0-9cd5-1644aa3de5bf (Sendible "Keyword Monitor" webinar listing; undated excerpt) |
| SE19 | https://toolradar.com/tools/sendible/pricing.md (third-party price tracker) |
| SE20 | https://sendible.com/features (search excerpt about mobile apps) |

## 3.1 Supported networks

The pricing FAQ names: Facebook, Instagram, LinkedIn, TikTok, X, Google Business Profile, YouTube, Threads, Bluesky and **WordPress** (blog posts) [SE1]. It adds: "You can monitor and reply to comments on Facebook, Instagram and LinkedIn" [SE1].

**Pinterest does not appear on any Sendible page read** [SE1][SE13]. There is no Nextdoor, Mastodon, Reddit, Tumblr or WhatsApp either [SE1].

| Network | Publish | Inbox / engagement | Analytics |
|---|---|---|---|
| Facebook | Yes, Pages: photos, videos, Stories, Reels [SE1][SE13] | Yes: comments on posts **and ads**; Messenger [SE1][SE2] | Yes [SE1] |
| Instagram | Yes, Professional: posts, Reels, Stories, carousels, first comment, geotags. Some carousels and Stories with stickers go by push reminder [SE1] | Yes: comments; **DMs added 2026-10-29** within Instagram's 24-hour window [SE1][SE2] | Yes [SE1] |
| Threads | Yes [SE1] | Not listed [SE1] | UNVERIFIED |
| X | Yes [SE1] | Not listed in the FAQ's reply list [SE1] (UNVERIFIED) | UNVERIFIED ("pre-built reports for most social networks") [SE1] |
| LinkedIn Page | Yes, including PDF carousels [SE1] | Yes: comments on Pages [SE1] | Yes [SE1] |
| LinkedIn profile | Yes ("profiles and Company Pages") [SE1] | No [SE1] | No; insights are for Company Pages [SE13] |
| TikTok | Yes, Business **and Personal** accounts [SE1] | Not listed [SE1] | Yes ("track video analytics") [SE1] |
| YouTube | Yes: videos and Shorts [SE1][SE13] | Yes: reply to comments [SE1] | Yes [SE1] |
| Pinterest | **No** [SE1] | No | No |
| Google Business Profile | Yes, many locations [SE1] | No reviews found | **No**: "Google Business Profile performance reports aren't available in Sendible yet" [SE1] |
| Nextdoor | No [SE1] | No | No |
| Bluesky | Yes [SE1] | No [SE1] | UNVERIFIED |
| Mastodon, Reddit, Tumblr, WhatsApp | No [SE1] | No | No |

## 3.2 Publishing

- **Calendar:**
  - Drag-and-drop with a filter by user; PDF export and print [SE1].
  - A new Unified Calendar (beta, 2026-03-18) adds monthly, weekly and daily views with status, network, campaign and user filters [SE2].
- **Smart Queues (evergreen):**
  - Dedicated queues (quotes, testimonials, evergreen) post on preset days and times and **repost at regular intervals**, up to two years ahead.
  - Queues can be paused and edited [SE5].
  - 10 / 30 / 70 / unlimited / unlimited queues by plan [SE1].
- **"Repeated content":** recycle evergreen posts with start and end dates [SE1].
- **Best time:** "Sendible continuously learns and adapts optimal times based on evolving audience behaviour" [SE12].
- **Bulk and CSV:** bulk scheduler and CSV with video support [SE1][SE2] (bulk video since 2026-10-29). Sendible says bulk scheduling is on every plan from Core [SE14].
- **"Bulk posting dynamic fields":** email-style placeholders (names, cities, services) personalise one post across many profiles. **Elite and up** [SE1].
- **Per-network customization and previews:** yes; previews for Facebook, LinkedIn, X and Instagram [SE1].
- **First comment:** Facebook, Instagram, LinkedIn and TikTok, up to 30 hashtags (2026-06-22) [SE2].
- **Alt text:** Instagram, LinkedIn and X, up to 200 characters [SE2].
- **Custom video thumbnails** [SE2].
- **Stories, Reels, carousels:** see 3.1. Mixed-media posts of up to 40 items [SE2].
- **Drafts:** yes [SE13].
- **RSS:** RSS/blog feed following, an "RSS Auto-poster", Google Alerts as a content source, and a holiday calendar of 240+ events [SE1][SE13].
- **Media:**
  - Content libraries (Premium and up) [SE1].
  - Google Drive and Dropbox [SE1].
  - Pexels, GIPHY, Flickr [SE1].
  - Image and video editor with per-network presets [SE1][SE2].
- **Links:** Bitly branded links (requires a paid Bitly account) and UTM parameters [SE1].
- **Custom Tags:** prefilled brand fields such as URL and phone [SE11].
- **Link-in-bio:** Not found [SE1].
- **Daily publishing cap per workspace:** 100 / 200 / 300 / 500 / 500 by plan [SE1].

## 3.3 Content creation

- **AI Assist** [SE11][SE15][SE1]:
  - Generates captions, rephrases, fixes spelling and grammar, and makes copy shorter or longer, inside the Compose Box (HC excerpt for the length and grammar options).
  - **"Unlimited AI credits" on every plan** [SE1].
- **Not found:** repurposing blogs or video into posts, and AI image generation [SE11].
- **Hashtags:** saved "topic-cluster hashtags" in the content library [SE11].
- **Design:** Canva integration with templates; built-in image and video editor [SE1][SE2].
- **Chrome extension and share button:** curate from any page; embed the button on websites [SE1].

## 3.4 Engagement

- **Priority Inbox:**
  - Aggregates "every comment, mention and message likely to need a response".
  - The user chooses which profiles to include.
  - Filters: date, profile, archived status.
  - **Assign messages to team members** [SE3].
  - Periodically updated. "Social Feeds" streams give real time for Facebook Pages and ads and for LinkedIn Pages [SE1].
- **Notifications:** "via email or Slack" (claim on Sendible's own comparison page) [SE14].
- **Not found:** saved replies, AI-suggested replies, auto-replies or chatbots, moderation rules, sentiment, contact or CRM history [SE3][SE11].

## 3.5 Reviews

**None found.** The comment-reply networks are Facebook, Instagram and LinkedIn (plus YouTube), and Google Business Profile reviews are not mentioned anywhere [SE1].

## 3.6 Social listening

- **The listening feature page is gone:** sendible.com/features/social-listening now serves the generic homepage [SE13].
- The white-label page still links "Listening: Monitor brand mentions and improve engagement" [SE8].
- A Sendible webinar listing covers setting up a "keyword monitor" and engaging with keyword mentions (undated) [SE18].
- **Current keyword or brand monitoring: UNVERIFIED.**
- Competitor benchmarking: Not found.

## 3.7 Analytics and reporting

- **Reports:**
  - Standard reports on every plan: per-network reports and a combined Engagement report [SE1].
  - "8 pre-built reports including a Google Analytics (GA) report" [SE1].
  - **Custom report builder:** 2 per workspace on Premium, 3 on Elite and Enterprise. Drag-and-drop modules and branded covers [SE1][SE12].
  - Custom branded reports with no Sendible branding: Premium and up [SE1].
- **Delivery:**
  - PDF save and print [SE1].
  - **Automatic email delivery daily, weekly or monthly**, including to people without a Sendible account [SE6][SE12].
  - Live report links [SE1].
- **ROI:** Google Analytics integration for traffic, UTMs and ROI; campaigns and labels [SE6][SE12].
- **Not available:** Google Business Profile reports [SE1].
- **2026 changes:** Instagram Insights and LinkedIn report updates [SE2].

## 3.8 Collaboration and approvals

- **Users:** **unlimited on every plan** (team, freelancers, clients, stakeholders) [SE1].
- **Approvals:**
  - "Assignment & approvals" on **Plus and up** [SE1].
  - "Assign posts for approval" to another user; "Custom approval workflows" that require approval for chosen users (for example juniors) [SE1].
  - Approval requests can be sent individually or in bulk [SE7].
  - Video playback in the approval tasks view (2026-06-22) [SE2].
- **Client approval:** "client users will only ever have access to their own dashboards to be able to preview and approve content" [SE1]. The client "will get a notification on their dashboard and an email requesting approval" (HC excerpt) [SE17].
- **Permissions:** permission groups; "Advanced user permissions" on **Elite and up** [SE1].
- **Workspaces:** per-user access control and "clearer attribution" (launched 2026-08-18) [SE2].
- **Audit trail:** Not found as a named feature.

## 3.9 Agency and multi-client

- **Workspaces** are the pricing unit: 1 / 3 / 7 / 15 / 50, with **6 social profiles each**, which can be split freely between workspaces [SE1][SE9].
- **Extra workspace:** $35 / $30 / $25 / $20 / $15 per month (Core → Enterprise), each with 6 more profiles [SE1].
- **Client Connect:**
  - Clients link their own profiles via a widget on the agency's website; no passwords are shared.
  - **Reconnection links are emailed** to the client automatically.
  - Can be sold "as a premium service" [SE4].
  - Available on Core [SE14].
- **White label (paid add-on, Elite and Enterprise only)** [SE1]:
  - Covers logo, colours, domain, email notifications, custom support pages, **SSO**, embedded apps, and a **branded free-trial page** for prospects [SE1][SE8].
  - Agencies can "create service packages for clients and limit areas of the dashboard until they upgrade with you" [SE1]. This is the nearest thing to per-client packaging. Sendible does not bill the agency's clients directly (Not found).
  - **Price:** "$349/mo on Elite and $800/mo on Enterprise" [SE1]. The white-label page's own FAQ still says "starts at $268 ($315 with a 15% annual discount)" and may add an onboarding fee [SE8]. The two pages conflict.
- **Software integrators:** embed white-label scheduling in a SaaS product, with SSO, Share Button, Client Connect, and "API and automation options" [SE10].

## 3.10 Ads

- Facebook **ad comments** can be monitored and answered [SE1].
- **No ads creation, boosting or ad reporting** was found [SE1].

## 3.11 Email

- **Reports:** automatically emailed custom reports with per-recipient subject lines and messages [SE6].
- **Approvals:** approval-request emails [SE17]. White label sends approval emails from the agency's custom domain [SE8].
- **Client Connect:** reconnection-link emails [SE4].
- **"Add to a Content Library using email":**
  - Each library gets a forwarding address.
  - Text and images only, no video.
  - A receipt email confirms the item (HC excerpt) [SE16].
  - The collaboration page also mentions curation "via emails" [SE7].
- **Notifications:** by email or Slack [SE14].
- **No email marketing or newsletters** were found [SE1].

## 3.12 Automation and AI agents

- **Runs without a person:** Smart Queues and repeated content [SE5][SE1]; the RSS auto-poster [SE1]; automated reports [SE6].
- **Zapier (beta, 2026-01-07):** project tools (Airtable, Notion, Monday.com, ClickUp) can create, queue, draft or delete posts [SE2][SE1].
- **Coming:**
  - "Publishing API access is on its way", letting admins generate API keys and schedule without Zapier [SE2].
  - "A Sendible API and MCP connector are in the works" [SE1].
- **No AI auto-replies** were found.
- **Guardrails:** approval workflows can be forced for chosen users [SE1].

## 3.13 Integrations and API

- **Integrations:** Zapier, Canva, Google Drive, Dropbox, Pexels, Flickr, GIPHY, Bitly, Google Analytics, WordPress, Chrome extension [SE1].
- **Slack:** notifications only, per the comparison page [SE14].
- **CRM:** Not found.
- **Public API:** **not yet**; publishing API and MCP announced as coming [SE1][SE2].
- **Mobile apps:** iOS and Android (search excerpt of sendible.com/features) [SE20]. Feature depth: UNVERIFIED.

## 3.14 Pricing (plans changed in 2026)

**Sendible replaced Creator, Traction, Scale, White Label and White Label+ with Core, Plus, Premium, Elite and Enterprise.** A tracker dates the change to about 31 October 2026 [SE19]. That tracker lists Elite at $349 and Enterprise at $800, but those figures match Sendible's **white-label add-on** prices [SE1], not the plan prices.

**Plan prices (USD), from the pricing page's price data attributes** [SE1]:

| Plan | Monthly billing | Annual billing, per month | Workspaces | Profiles | Users | Smart queues | Daily sends per workspace | Custom reports |
|---|---|---|---|---|---|---|---|---|
| Core | $35 | $31 | 1 | 6 | Unlimited | 10 | 100 | — |
| Plus | $99 | $86 | 3 | 18 | Unlimited | 30 | 200 | — |
| Premium | $199 | $170 | 7 | 42 | Unlimited | 70 | 300 | 2 per workspace |
| Elite | $299 | $255 | 15 | 90 | Unlimited | Unlimited | 500 | 3 per workspace |
| Enterprise | $750 | $638 | 50 | 300 | Unlimited | Unlimited | 500 | 3 per workspace |

Small conflicts in Sendible's own copy: the meta description says Core is "$30/mo billed annually"; the FAQ says Premium is "$169/mo billed annually"; the SocialPilot comparison page says Plus is "$84/mo billed yearly" [SE1][SE14]. Annual billing is "around 15%" cheaper [SE1].

- **What each plan adds** [SE1]:
  - Plus: assignment and approvals.
  - Premium: content libraries, custom branded reports, campaigns, dedicated onboarding, dedicated account manager.
  - Elite: bulk dynamic fields, advanced permissions, white-label option.
  - Enterprise: every feature, plus SSO.
- **Every plan:** unlimited users, unlimited scheduling, unlimited AI credits, bulk scheduling, monitoring and replying, standard reports [SE1].
- **Add-ons:**
  - Extra workspace: $35 / $30 / $25 / $20 / $15 per month.
  - White label: $349/mo on Elite, $800/mo on Enterprise [SE1].
- **Free plan:** none. **Free trial:** 14 days, no card needed. A card added during the trial is billed automatically at the end [SE1].
- **Payment:** "all major credit cards"; invoice or purchase order via sales@sendible.com. **PayPal is not mentioned** [SE1].
- **Currencies:** USD, GBP, EUR [SE1].
- **Nonprofit discount:** 15% off monthly or 25% off annual billing [SE1].

## 3.15 Onboarding

- **Sign-up:** 14-day trial with no card; connect profiles and invite the team immediately [SE1][SE13].
- **Client Connect** lets clients add their own profiles without sharing credentials or admin access [SE4][SE7].
- **Reconnections:** LinkedIn reconnection reminders now come yearly instead of every 60 days (2026-08-18) [SE2]. Reconnection links are emailed [SE4].
- **Support:** Premium and up get dedicated onboarding and an account manager [SE1].
- **Developer app or API keys: No evidence that customers create one.** Profiles are connected by authorization or Client Connect [SE4]. The only customer keys mentioned are for Sendible's upcoming publishing API [SE2]. Bitly branded links require the customer's own paid Bitly account [SE1].

## 3.16 Distinctive points for a newcomer

- **Unlimited users on every plan; pricing is by workspace.** This is Sendible's main argument against per-seat competitors [SE1][SE14].
- **Built for agencies and multi-location brands:**
  - Client Connect, white-label reselling with service packages and branded trials, and a software-integrator embedding offer [SE4][SE8][SE10].
  - Vertical pages for real estate, franchises, dental, hotels and car dealerships [SE1].
- **Weak spots:**
  - No Pinterest [SE1].
  - No Google Business Profile reporting or reviews [SE1].
  - No AI in the inbox [SE3][SE11].
  - No API yet, though one is announced [SE2].
  - Daily publishing caps per workspace [SE1].
- **Pricing was overhauled in late 2026,** so many third-party comparisons are already wrong [SE19].

---

# 4. MeetEdgar

### Source key

| Tag | URL |
|---|---|
| ME1 | https://meetedgar.com/pricing |
| ME2 | https://meetedgar.com/features |
| ME3 | https://meetedgar.com/ |
| ME4 | https://help.meetedgar.com/en/collections/47695-edgar-s-features-and-best-practices (article index) |
| ME5 | https://help.meetedgar.com/en/collections/47566-edgar-integrations (network article index) |
| ME6 | https://help.meetedgar.com/en/articles/281096-edgar-s-categories |
| ME7 | https://help.meetedgar.com/en/articles/281185-queue-overview |
| ME8 | https://help.meetedgar.com/en/articles/281190-scheduling-with-edgar |
| ME9 | https://help.meetedgar.com/en/articles/281255-use-once |
| ME10 | https://help.meetedgar.com/en/articles/281280-expiration-dates-limited-run-events-and-seasonal-content |
| ME11 | https://help.meetedgar.com/en/articles/2065091-variations |
| ME12 | https://help.meetedgar.com/en/articles/1618286-how-to-import-content-using-rss-feeds |
| ME13 | https://help.meetedgar.com/en/articles/281245-how-to-bulk-import-content |
| ME14 | https://help.meetedgar.com/en/articles/8886975-meet-inky-edgar-s-ai-sidekick |
| ME15 | https://help.meetedgar.com/en/articles/6213008-using-inbox-feature-in-meetedgar |
| ME16 | https://help.meetedgar.com/en/articles/5795117-edgar-teams |
| ME17 | https://help.meetedgar.com/en/articles/281275-meetedgar-billing-and-invoice-faqs |
| ME18 | https://help.meetedgar.com/en/articles/281278-edgar-s-plans-pricing |
| ME19 | https://help.meetedgar.com/en/articles/281088-connecting-social-accounts-to-edgar |
| ME20 | https://help.meetedgar.com/en/articles/1410896-account-permissions |
| ME21 | https://help.meetedgar.com/en/articles/1307015-connecting-linkedin-to-edgar |
| ME22 | https://help.meetedgar.com/en/articles/2146216-connecting-facebook-groups-to-edgar |
| ME23 | https://help.meetedgar.com/en/articles/281164-weekly-performance-reports |
| ME24 | https://help.meetedgar.com/en/articles/281090-review-performance-with-edgar-s-dashboard |
| ME25 | https://help.meetedgar.com/en/articles/281214-how-link-shortening-click-tracking-work-in-meetedgar |
| ME26 | https://help.meetedgar.com/en/articles/7203911-edgar-shopify |
| ME27 | https://help.meetedgar.com/en/articles/281162-notifications |
| ME28 | https://help.meetedgar.com/en/articles/6602498-how-to-publish-posts-to-instagram-with-edgar-s-mobile-app |
| ME29 | https://help.meetedgar.com/en/articles/18182710-schedule-your-first-comment-in-meetedgar |
| ME30 | https://help.meetedgar.com/en/articles/281178-edgar-zapier |
| ME31 | https://help.meetedgar.com/en/articles/281218-meetedgar-s-cancellation-and-refund-policy |
| ME32 | https://help.meetedgar.com/en/collections/17822483-threads-and-edgar |
| ME33 | https://pricingsaas.com/companies/meetedgar/diffs/2026W37 (third-party tracker) |

## 4.1 Supported networks

**Ten networks:** TikTok, Facebook, Instagram, LinkedIn, Pinterest, Google Business Profile, X, Threads, **YouTube Shorts**, Bluesky [ME3][ME2]. Each has its own help article [ME5][ME32].

**Facebook Groups are no longer supported** because of Meta's Groups API shutdown. The article gives 22 April 2024 as the cutoff [ME22].

There is no Nextdoor, Mastodon, Reddit, Tumblr or WhatsApp [ME3][ME5].

| Network | Publish | Inbox / engagement | Analytics |
|---|---|---|---|
| Facebook | Yes, Pages and Stories; **Groups no** [ME5][ME22] | Yes on the EDGAR plan: comments on Edgar-published posts, and DMs [ME15] | Account-level dashboard (content sent, link clicks, impressions) [ME24] |
| Instagram | Yes: Business direct (images, carousels, Reels); Stories and personal accounts by mobile-app reminder [ME5][ME28] | Yes on the EDGAR plan: comments, DMs, Story replies [ME15] | Dashboard only; no click tracking on Instagram [ME24][ME25] |
| Threads | Yes [ME32] | No [ME15] | Network-specific metrics UNVERIFIED |
| X | Yes, but **each variation publishes only once** [ME10][ME11] | No [ME15] | Network-specific metrics UNVERIFIED |
| LinkedIn Page | Yes, including carousels [ME5][ME21] | No [ME15] | Network-specific metrics UNVERIFIED |
| LinkedIn profile | Yes [ME21] | No | Network-specific metrics UNVERIFIED |
| TikTok | Yes [ME5] | No | Network-specific metrics UNVERIFIED |
| YouTube | **Shorts only** [ME3][ME5] | No | Network-specific metrics UNVERIFIED |
| Pinterest | Yes, personal and business; video on business only [ME20] | No | Network-specific metrics UNVERIFIED |
| Google Business Profile | Yes (updates) [ME5] | No | Network-specific metrics UNVERIFIED |
| Nextdoor | No | No | No |
| Bluesky | Yes [ME5] | No | Network-specific metrics UNVERIFIED |
| Mastodon, Reddit, Tumblr, WhatsApp | No | No | No |

The inbox article says "Support for additional platforms is already in the works" [ME15].

## 4.2 Publishing (Edgar's category and library model)

- **The library keeps every post.** "Other schedulers delete your posts after publishing, but not Edgar" [ME3]. The library is unlimited [ME2].
- **Categories** [ME6]:
  - Default categories: General (cannot be deleted; always in Random), My Blog Posts, Promotions & Specials, Recommended Reads.
  - Categories can be excluded from **Random** slots, which suits seasonal content.
  - Deleting a category deletes its posts and its time slots.
- **Schedule (time slots)** [ME8]:
  - Recurring weekly slots in a calendar grid. Each slot picks profiles and **a category** (or a single post).
  - Slots with several profiles count as one slot toward the plan's **weekly automation limit** (10 on Eddie, 1,000 on EDGAR) [ME1].
  - **Random slots** pick any library post, whether or not it was already published.
  - Edgar suggests starter slots "based on internal research" when the schedule is empty [ME8].
- **Selection and recycling** [ME7]:
  - Default order is **last-in, first-out** (newest first).
  - "When a category runs out of unposted content, Edgar starts recycling posts that were already published."
  - The queue shows about two weeks ahead and is rebuilt daily.
  - Posts can be skipped (the next post in the category moves up) or shuffled, and the whole queue can be paused.
- **Use Once:** publishes once to each selected account, then retires the post [ME9].
- **Expiration dates:** expired posts grey out and can be reactivated [ME10].
- **Seasonal content:** create a category, exclude it from Random, and add or remove its time slots each year [ME10].
- **Variations** [ME11]:
  - Unlimited caption versions per post, each with its own media.
  - "Suggest variations" pulls up to 4 pull quotes from the linked page (scraped text, **not AI**).
  - Variations rotate so the same post does not publish back to back.
  - On X each variation publishes once, after which the post is marked "Expired for X" and **Edgar emails you when X content runs low**.
- **Bulk import (CSV)** [ME13]:
  - Columns: text, category, image URLs (up to 10 per post).
  - Recommended maximum of 100 rows.
  - No video, no emoji, and no Instagram or Pinterest.
- **Bulk edit** [ME4].
- **RSS:** feeds are checked daily into a chosen category and accounts. The user chooses **straight to the library, or to a "pending content" queue for review**, and can set imported posts to Use Once [ME12].
- **First comment:** Facebook, LinkedIn and Instagram, in the new composer [ME29].
- **Stories and Reels:** Facebook and Instagram Stories, Instagram Reels, LinkedIn carousels, TikTok videos, YouTube Shorts [ME5].
- **Drafts:** Not found as a named feature [ME4].
- **Best-time suggestions:** only the starter time slots [ME8].
- **Link shortening:** Rebrandly (custom domain, click tracking shown in Edgar) or Bitly (click data only on bitly.com) [ME25]. UTM parameters [ME4].
- **Media library:** the content library itself [ME2].
- **Link-in-bio:** Not found [ME2][ME4].
- **Bookmarklet, Chrome extension and share button** [ME4][ME5].
- **A/B testing** is listed on the features page [ME2]. The help center describes it only as comparing variation click data via Rebrandly [ME11].

## 4.3 Content creation

- **Inky (AI):**
  - Writes captions from a description, with a tone choice (friendly, professional, witty) and optional hashtags [ME14].
  - The output is **copied and pasted** into the post by the user.
  - Credits: **15 per month on Eddie, 50 per month on EDGAR**; 100 extra credits for $2.50, expiring after 30 days [ME14][ME1].
- **Not found:** repurposing blogs or videos, and image generation [ME14].
- **Canva:** design import [ME4].
- **Hashtags:** only through Inky [ME14].
- **Image editing:** Not found.

## 4.4 Engagement

- **Social Inbox (EDGAR plan only)** [ME15][ME1]:
  - Facebook and Instagram DMs, including Story replies.
  - Comments **only on posts Edgar published**.
  - Views: DMs, Unread, Comments.
  - Any team member can reply.
  - No filters or search yet; messages cannot be deleted from Edgar.
  - A tracker records the Social Inbox appearing in the plan comparison in early September 2026 [ME33].
- **Not found:** mentions, assignment, saved replies, AI replies, automatic replies, moderation, sentiment, CRM [ME15].

## 4.5 Reviews

None [ME3][ME15].

## 4.6 Social listening

None [ME2][ME3].

## 4.7 Analytics and reporting

- **Dashboard:** total content sent, shortened-link clicks and impressions, filtered by period (default: last week), broken down by **category** and by **account** [ME24].
- **Weekly Performance Report by email** every Sunday evening (US time): posts published, categories used, follower changes, clicks. Turned off under Settings [ME23].
- **Post history page** [ME4].
- **Not found:** competitor benchmarks, custom or white-label or PDF reports, export [ME24].
- **ROI:** click tracking via Rebrandly; UTM parameters [ME25][ME4].

## 4.8 Collaboration and approvals

- **Up to 20 team members on both plans** at no extra cost [ME1][ME16].
- **Collaborators get full access to everything except billing.** No other roles exist [ME16].
- **No approval workflow** [ME16][ME2].
- **Notes and tasks, audit trail:** Not found.
- Removing a member transfers their content to the owner [ME16].

## 4.9 Agency and multi-client

None. There are no workspaces, client portals, white label or per-client billing [ME1][ME2]. The product is aimed at solo businesses and small teams.

## 4.10 Ads

None found [ME2].

## 4.11 Email

- **Weekly performance-report email** [ME23].
- **Notification emails:**
  - LinkedIn token-refresh reminders (60-day tokens) [ME27].
  - Emails when X variations run low [ME11].
- **Team invitation emails** [ME16].
- **Billing receipts** [ME17].
- **No email marketing or newsletters** were found. "EdgarNews" is MeetEdgar's own marketing list [ME3].

## 4.12 Automation and AI agents

- **The whole product is automation:** the self-filling queue, recycling when a category runs out, Random slots, RSS import, and Shopify product import into the "automated evergreen content recycler" [ME7][ME12][ME26].
- **Guardrails:**
  - RSS posts can be routed to a **pending queue for review** [ME12].
  - Use Once and expiration dates stop recycling [ME9][ME10].
  - The queue can be paused [ME7].
- **AI:** Inky only drafts captions, which a person pastes in [ME14]. There are no AI agents or auto-replies.

## 4.13 Integrations and API

- **Integrations:** Canva, Zapier, Shopify [ME2]; Bitly and Rebrandly [ME25].
- **Zapier:** text-only templates (Pocket, YouTube, Pinterest, Gumroad, Google Forms, Feedly) that add content to the library. It uses **an Edgar API key copied from account settings** [ME30].
- **Public API documentation:** Not found. UNVERIFIED.
- **Mobile app:** iOS and Android, for Instagram reminders and publishing, and for viewing and skipping the queue [ME28][ME7].
- **Google Drive, Dropbox, Slack, CRM:** Not found [ME2].

## 4.14 Pricing

**Plans** [ME1]:

| | Eddie | EDGAR |
|---|---|---|
| Monthly billing | $29.99/mo | $49.99/mo |
| Annual billing | $299/yr ($24.91/mo; save 17%) | $499/yr ($41.58/mo) |
| Social accounts | 5 | 25 |
| Scheduled posts | Unlimited | Unlimited |
| Weekly automations | 10 | 1,000 |
| Content categories | 4 | Unlimited |
| Team members | 20 | 20 |
| Social Inbox | No | Yes |
| Inky credits per month | 15 | 50 |
| Extra social account | $4.99/mo ($3.99 annual) | $2.99/mo ($1.99 annual) |

- **Help-center naming:** an older help article calls the plans "Edgar Lite" and "Standard Edgar", with the same prices and limits [ME18].
- **Free trial:** **30 days**, full access; "No payment required" at sign-up [ME1][ME3].
- **Free plan:** none [ME1]. **Exception:** a free **Shopify-only plan** with one social account, available through the Shopify app [ME26].
- **Discounts:**
  - Black Friday 2026: 30% off for 12 months with code BF2026SAVE [ME1].
  - Permanent 10% off for nonprofits, military and veterans [ME1]. The help article prices this at $449/yr or $45/mo on the EDGAR plan [ME18].
- **Payment:** Visa, MasterCard, American Express, JCB, Discover, Diners Club, via Stripe, billed in USD. **PayPal is not listed** [ME17].
- **Refunds:** 30-day prorated money-back guarantee [ME31][ME18].

## 4.15 Onboarding

- **Sign-up:** 30-day trial [ME1].
- **Connecting accounts:** Settings → Social media accounts → choose the network → log in and authorize [ME19].
  - To add a second account on the same network, log out of the first on that network and repeat [ME19].
- **Permission requirements** [ME20][ME21]:
  - Facebook: the user must be a Page admin, granting "Permission to post for you" and "Permission to manage your pages".
  - X: "Authorize App".
  - LinkedIn Company: Super Admin.
  - Instagram Business: via the linked Facebook Page.
  - Instagram personal: log in when prompted.
- **Default categories** fill a new library quickly [ME6]. Starter time slots are suggested [ME8].
- **Developer app or API keys: No.** Every network is connected through a log-in or authorization flow inside Edgar. The permissions article mentions no developer apps or keys [ME20]. The only key is Edgar's own, for Zapier [ME30].

## 4.16 Distinctive points for a newcomer

- **"Create content and keep it":** a permanent library, category time slots, LIFO selection, automatic recycling, and rotating variations [ME3][ME7][ME11]. This is the purest evergreen-automation tool of the four.
- **Simple and cheap,** with a long 30-day trial and 20 users included [ME1]. The trade-offs: no approvals, no roles, no agency features, and an inbox limited to Facebook and Instagram on the top plan [ME15][ME16].
- **Network rules leak into the product.** X allows each variation to publish only once [ME11]. Facebook Groups were lost to Meta's API shutdown [ME22]. Instagram personal accounts and Stories need phone reminders [ME28].
- **Inky's AI is small:** 15 or 50 credits a month, and a copy-and-paste workflow [ME14]. AI is not central to the product.

---

## Answers to the brief's priority questions (sources in each section above)

1. **Nextdoor:** **none** of the four supports it [SB1][SP5][SE1][ME3].
2. **Email features:**
   - None offers email marketing or newsletters.
   - Email is used for notifications (all four), scheduled report delivery (SocialPilot, Sendible; Edgar's fixed weekly report) and approvals (SocialPilot's no-login approval links and reminders; Sendible's approval emails).
   - Sendible also accepts **inbound email into a content library** [SE16].
   - SocialPilot Reviews sends **email, SMS and WhatsApp review requests** [SP11][SP6].
3. **Review sites:**
   - Only SocialPilot handles reviews, through a separate product: read and reply on Google and Facebook (also in the social inbox for Google Business Profile), plus monitoring of Tripadvisor, Yelp and Trustpilot, with AI auto-replies [SP10][SP12][SP13].
   - SocialBee, Sendible and MeetEdgar: none.
4. **Approval workflows:**
   - SocialPilot is the most complete: manager and client approval, no-login links, email reminders [SP18][SP19][SP21].
   - Sendible: approvals, custom workflows, client dashboards (Plus and up) [SE1].
   - SocialBee: role-based approval; clients can join a workspace [SB14].
   - MeetEdgar: none [ME16].
5. **Pricing models:**
   - SocialBee: by profiles, with extra users and workspaces as add-ons [SB1].
   - SocialPilot: by accounts and users, with $4 and $5 add-ons [SP1].
   - Sendible: by workspace, with unlimited users [SE1].
   - MeetEdgar: two flat tiers by accounts, 20 users included [ME1].
   - All four offer monthly billing with annual discounts of roughly 15–17%. Three ran Black Friday promotions on the check date.
6. **Content categories and recycling:**
   - SocialBee and MeetEdgar are built around them [SB8][SB23][ME6][ME7].
   - Sendible approximates them with Smart Queues [SE5].
   - SocialPilot has only repeat posts and queues [SP4].
7. **Customers bringing their own developer app or keys:** **never required** by any of the four. Every social connection is a log-in or authorization flow run by the vendor's own registered apps [SB18][SP31][SE4][ME20]. The only customer-side keys are optional automation keys issued by the vendor itself: SocialPilot MCP for some clients [SP14], Edgar's Zapier key [ME30], and Sendible's upcoming API [SE2].
