# Meta Business Suite, Nextdoor for Business, Planable, Loomly — scope research for GeekSocial

**Checked 2026-11-18.** Every claim carries the URL it came from. Where a point could not be confirmed, it says **UNVERIFIED**.

**How the sources were read**

- Meta's Business Help Center pages render in JavaScript, and plain fetches return an empty shell. They were read in a browser on 2026-11-18.
- The Loomly help center (`loomly.zendesk.com`) and the Planable help center (`help.planable.io`) were read through Zendesk's public Help Center API. The `updated` dates quoted below are each article's own `updated_at`.
- Nextdoor's developer docs were read at `developer.nextdoor.com`.
- Secondary sources (vendor blogs, press) are marked as such and are used only where no official page was found.

---

## PART A — Meta Business Suite and Meta's free business tools

### A1. What it is, and price

- Meta's help center describes the Suite as a tool for small businesses and creators to "centrally manage their business and online presence across Meta technologies, such as Facebook, Instagram, Threads and WhatsApp." It says: "Meta Business Suite is free to use on desktop and in the Meta Business Suite mobile app." — https://www.facebook.com/business/help/205614130852988
- A **business portfolio** is required: "a container that holds your organization's business assets, such as a Facebook Page, Instagram account and ad account, along with the people who have access to them." — same URL
- Areas of the product are Home (to-do list and an overview of recent and scheduled content), notifications, the Planner calendar, posts/stories/reels creation, Insights, Inbox, monetization, ads, the creator marketplace, and "All tools" (Ads Manager, Reach Planner, Events Manager, Business Support Home, Billing, Page settings). — same URL
- **Regional caveat:** "Certain messaging-related metrics, ad campaigns and organic messaging features for businesses and ads delivered to and from Europe and Japan will be unavailable." — https://www.facebook.com/business/help/294426838452244

### A2. Publishing (Planner, scheduling, Stories/Reels, cross-posting)

| Capability | Status | Source |
|---|---|---|
| Create and schedule posts to a Facebook Page, an Instagram profile or a Facebook Group | Yes. The Instagram limit is 10 photos per post. Text can be customised separately for FB and IG. | https://www.facebook.com/business/help/942827662903020 |
| Recommended times | "Active times" are based on when followers were most active over the past 7 days | same |
| Post states | Published, Scheduled, Drafts, Expiring, Expired. You can edit, reschedule, move to drafts, delete, add a collaborator, or enable/disable remixing. | same |
| Reels and Stories | Yes, created and scheduled in Content/Planner | https://www.facebook.com/business/help/205614130852988 ; https://www.facebook.com/business/help/1678733516458988 |
| Planner | A week or month calendar of FB/IG posts, stories, reels and **ads**, both published and scheduled. Filters by content type and by platform (FB Page, FB Groups, IG). Lets you schedule, reschedule or delete from the calendar. Needs "content permissions" for organic content and "ads permissions" for ads. | https://www.facebook.com/business/help/1678733516458988 |
| A/B tests of organic content; preview of FB feed and IG grid; saved ideas; "use AI to help you generate content ideas" | Yes | https://www.facebook.com/business/help/205614130852988 |
| Cross-posting | FB post → FB Stories, IG Feed, FB Groups, Threads. IG post → FB Feed, IG Stories, FB Groups, Threads. FB story ↔ IG story. IG reel → IG Stories, FB Reels, FB Groups. FB reel → FB Stories, IG Reels, FB Groups. | https://www.facebook.com/business/help/914097058035853 |
| Threads | Only through cross-posting, and only on desktop. The Threads profile must be added to the portfolio. From September 2026, Threads access settings can be managed separately from Instagram (rolling out). | same |
| Limits on cross-posting | "Cross-posting a Facebook-only post isn't currently available." FB/IG Page and profile must be connected in the same portfolio. | same |
| Scheduled-post failure | Meta's own guidance: scheduled posts can fail. Check Scheduled for an error, then "Publish now" manually. Posts scheduled to both FB and IG may not publish to both. | https://www.facebook.com/business/help/816464273497113 (search-index excerpt) |
| How far ahead you can schedule on the free Suite | **UNVERIFIED.** Third-party sources disagree (29 days vs 75 days). | https://www.oneupapp.io/blog/schedule-facebook-and-instagram-posts-more-than-29-days-into-future.md ; https://timetopost.co/blog/schedule-facebook-posts-how-far-ahead/ |
| Scheduling with **Meta One Advanced** (paid) | FB reels and posts up to a year ahead. IG stories up to 30 days. IG posts and reels up to a year. Suggested times. Grid preview. Bulk upload on instagram.com. | https://www.meta.com/help/subscriptions/960854640235758/ |
| WhatsApp publishing (Status / Channels) from Business Suite | **UNVERIFIED.** No Business Suite help page found that documents it. WhatsApp "Business Broadcasts" credits are a Meta One benefit (500 / 1,250 / 5,000 credits on Advanced / Expert / Max). | https://www.meta.com/help/subscriptions/960854640235758/ |
| Other networks | None. Every surface documented is Facebook, Instagram, Threads, Messenger or WhatsApp. | https://www.facebook.com/business/help/205614130852988 |

### A3. Unified inbox

From https://www.facebook.com/business/help/294426838452244 unless noted:

- **Messages** from Messenger, Instagram and WhatsApp in one Messages section, viewed together or filtered by platform.
- Instagram message access is "set to allow by default". Any IG admin can change it.
- **Comments** on Facebook and Instagram posts: reply, mark for follow-up, move to Done or Spam.
- Only comments from **connected business assets** appear. Comments on a personal profile, or on unconnected accounts, do not.
- Organising: mark unread, Follow up, Spam, Done. Filter by unread, follow-up, **assignee**, and "Ad response" (messages from ads).
- Availability status (online/offline). Search across people, labels and message content.
- **Customer information panel:** name, photo, location. Add labels, email, phone and private notes, visible to the people who manage the Page.
- A **Leads** tab.
- **Comment actions** (FB): react, reply, send a private message to the commenter, delete, hide, report. Filters: Unread / Follow up / Page comments / Group comments / Done / Spam. — https://www.facebook.com/business/help/1582179663085916
- **Comment actions** (IG): like, reply, message, delete. "You can't send a message in response to comments posted more than 7 days ago." — same URL
- Managing comments needs "community activity permissions". — same URL
- Threads replies in the inbox: **UNVERIFIED**. Not mentioned in any inbox help page read.

### A4. Automations (rule-based, free)

From https://www.facebook.com/business/help/395965998733706, read 2026-11-18:

- Available for Facebook Pages, Instagram business accounts and WhatsApp Business accounts connected to the Suite. "When Inbox automations are turned on, the person who messaged your business or commented on a post will automatically get a response."
- **Template automations:**
  - Instant reply (first message)
  - Away message
  - Frequently asked questions
  - Location
  - Contact information
  - Hours
  - Custom keywords
  - Comment to message ("Reply to a comment that contains specific hashtags", desktop only)
  - Identify unanswered messages
- **Custom keyword limits:**
  - Up to 5 keywords or phrases per automation.
  - "keywords are case-sensitive, and an automated message will not be sent unless the keywords match exactly."
  - A keyword can be used in only one automation.
  - Responses can include photos, videos, buttons and links.
  - "Inbox will automatically send a keyword automation to a message after 15 minutes unless you personally respond before then."
  - Desktop only.
- **Precedence:** when a template automation and a keyword automation both fire, only the template response is sent. Example: the away message wins.
- "If you turn on Meta Business Agent, any existing automations (such as away messages or instant replies) will be paused."

**Inbox suggestions** (assistive, not automatic) — https://www.facebook.com/business/help/991413128263758

- Context-based suggestions: confirm an order, confirm a payment, save a frequent reply, suggested FAQs.
- "Inbox suggestions don't send automatically, and you can accept or decline."
- Also available through the Messenger API.

### A5. Meta Business Agent (AI that answers customers) — what it does and its guardrails

**Official announcement, 3 June 2026** — https://about.fb.com/news/2026/06/meta-business-agent/

- Answers business-specific questions, recommends catalog products, books appointments, qualifies leads, closes sales, and replies in local languages.
- More than one million businesses already use it on WhatsApp and Messenger. Expanding to Instagram.
- A "morning briefing and insights" feature is limited to select businesses on the WhatsApp Business app, Instagram Pro, Messenger and Meta Business Suite, with a waitlist.
- Larger businesses get a **Business Agent Platform** with "enterprise-grade controls, guardrails, and measurement" and connectors to Shopify, Zendesk and Shopee.
- Pricing: Instagram activation is free for now. Paid subscriptions are coming "in the coming months".

**Help center — "About Meta Business Agent"** — https://www.facebook.com/business/help/1505847033372169

- "This feature may not be available to you yet."
- "Access to Meta Business Agent is limited. If you reach your free limit on Messenger or Instagram, you can subscribe to a Meta One plan to get more access … or wait for your free limit to refresh."
- Setup: you share your catalog, business description, address and hours. You "set boundaries for what it should or shouldn't answer", tailor its voice, and keep teaching and testing it.
- Answers basic info, product/service questions, recommendations, pricing, discounts, payment and shipping.
- **Guardrails stated by Meta:**
  - Takeover: "You can manually take over a chat with AI at any time … the AI will stop generating responses on your behalf."
  - Disclosure to customers: the chat shows "This business uses AI from Meta to generate messages. By chatting, you agree to the AI Terms."
  - Opt-out: "People can always request to stop chatting with AI directly in the chat, and the AI will transfer them to someone from your team."
  - Escalation: you get a notification "if Meta Business Agent determines that a customer needs your attention."
  - Feedback: long-press any AI message to give feedback.
  - Accuracy warning: "Some AI messages may be inaccurate or inappropriate." It is subject to the Meta Business Agents Terms of Service.
  - Interaction with automations: turning it on pauses automations such as away messages "while AI is active"; they resume when the AI exits a chat.

**Help center — setup** — https://www.facebook.com/business/help/834508185328904

- It learns from your existing presence across Meta, plus your website, catalog, documents, images and links.
- "Your Meta Business Agent will only respond if it can answer the customer's question. If it needs help, you will be notified to take over the chat."
- Messages generated by AI are marked "AI" in WhatsApp chats.
- Requirements:
  - Messenger: messaging on.
  - Instagram: messaging enabled on a professional account.
  - WhatsApp: number registered to the WhatsApp Business app, and possibly a catalog.

**Help center — teach and test** — https://www.facebook.com/business/help/1320359319435841

- Inbox labels show "Meta Business Agent Responding" and "Meta Business Agent Transferred".
- "Respond yourself" makes the agent leave the chat.
- "Responding manually … will pause Meta Business Agent for that chat. Meta Business Agent will start responding again after 7 days of inactivity."
- "Messages sent or received in chats where Meta Business Agent is connected will be collected by Meta."

**Secondary sources (not confirmed on a Meta page)**

- The agent "only responds to people who message your business first". Replies to Reel comments, Story replies and campaign triggers need trigger-based automation. — https://metricool.com/meta-business-agent/ (dated 9 Oct 2026)
- It does not send comment-triggered Private Replies or run broadcasts. — https://instantdm.com/blog/meta-business-agent-facebook-page
- **Whether the agent can draft replies for human approval instead of sending them: UNVERIFIED.** No Meta page read documents a draft/approve mode. The documented model is **auto-send with human takeover**. A Botcake guide claims a "Draft" mode for the older "Business AI": https://botcake.io/blog/how-to-set-up-meta-business-ai-on-messenger — not confirmed.

### A6. Comment moderation

- **In the Suite:** hide (the commenter and their friends still see the comment), delete, report, mark as spam, Instagram "Restrict", and limit who can comment on FB posts. — https://www.facebook.com/business/help/882357210033009
- **Page-level keyword blocking:** up to 1,000 keywords, with variants (misspellings, plurals) hidden automatically. CSV upload is supported. There is also a separate profanity filter. Both also apply to ads from the Page. — https://www.facebook.com/help/131671940241729 ; https://www.facebook.com/help/1017549069082358 (search-index excerpts)

### A7. Insights / analytics

From https://www.facebook.com/business/help/700570830721044:

- Organic and paid results for the FB Page and IG profile.
- Tabs:
  - **Overview** (28 days; Views, Follows)
  - **Plan** (recommended tasks)
  - **Results** (Views, Content interactions, Link clicks)
  - **Audience** (age, gender, interests, location, active times, potential ad audience)
  - **Messaging** (profiles that messaged, new vs returning contacts, orders and leads from conversations, **response rate and response time**)
  - **Benchmarking** (against similar businesses in the same category with similar 28-day reach)
  - Content overview
- **Export of Instagram insights**, competitive insights, a longer look-back and custom audience insights are **Meta One Advanced** benefits, not free. — https://www.meta.com/help/subscriptions/960854640235758/

### A8. Ads / boosting

- "Create quick and simple individual ads or boost content." Ads are tracked in the Suite's Ads tab. For detailed campaigns, Meta says to use Ads Manager. — https://www.facebook.com/business/help/205614130852988
- Ads appear in the Planner. — https://www.facebook.com/business/help/1678733516458988

### A9. Team roles

- Pages in a business portfolio use **task-based access**: Content; Messages and community activity; Ads; Insights. Full control (admin) covers all of them. — https://www.facebook.com/help/289207354498410 ; https://en-gb.facebook.com/business/help/1053717031486355 (search-index excerpts)
- **Partners** (for example agencies) can be given partial access and can re-delegate only the tasks they were given. — https://www.facebook.com/business/help/1717412048538897 (search-index excerpt)
- Shared access to an **Instagram** account without sharing the password is a paid Meta One benefit: up to 4 people on Advanced, 8 on Expert, 30 on Max. — https://www.meta.com/help/subscriptions/960854640235758/

### A10. Price: free Suite and paid Meta One (and Meta Verified)

- The Suite itself: **free** — https://www.facebook.com/business/help/205614130852988
- **Meta One business and creator plans** (announced 15 Sept 2026): Essential from $14.99/mo, Advanced from $49.99/mo, Expert from $149/mo, Max from $499/mo. "Plans, benefits, pricing, and availability may vary by region, by app, and by account." — https://about.fb.com/news/2026/09/introducing-meta-one-subscription-service-more-features-ai/ ; https://www.engadget.com/2258403/meta-adds-new-subscription-tiers-for-businesses-creators-and-ai-power-users/
- **Meta One help page** (read 2026-11-18; the page says "Updated 5 days ago") — https://www.meta.com/help/subscriptions/960854640235758/
  - "Meta One is currently in limited testing and isn't available everywhere yet."
  - Mobile onboarding only for now.
  - "not intended for use by government or political entities."
- **Essential:**
  - Verified badge on IG, FB and WhatsApp Business (once verified).
  - Impersonation protection.
  - Link page.
  - "more access to 24/7 AI responses from Meta Business Agent on WhatsApp Business."
- **Advanced:**
  - Featured in feed, optimised search, follow invites.
  - 500 WhatsApp broadcast credits.
  - Shared IG access for 4 people.
  - Scheduling up to a year.
  - Links in up to 4 IG posts/month, and in 4 IG reels and 8 FB reels/month.
  - Enhanced profile.
  - Competitive, custom-audience and extended insights, plus export.
  - More Business Agent access on WhatsApp.
  - Human support chat once a month.
  - Original-content credit.
  - 3 WhatsApp channels and 6 linked devices.
- **Expert:**
  - 1,250 broadcast credits.
  - Shared access for 8.
  - Custom chat themes.
  - 8 IG post links/month.
  - "expanded access to 24/7 responses from Meta Business Agent on Messenger".
  - Human support 5 times a month, with callback.
- **Max:**
  - 5,000 broadcast credits.
  - Shared access for 30.
  - A content-strategy adviser call every 6 months.
- **Meta Verified for business as a separate product:** its current price and whether it is still sold separately are **UNVERIFIED**. `facebook.com/business/tools/meta-verified` returned "page that doesn't exist" on 2026-11-18. Third-party blogs disagree. — e.g. https://www.reeldrop.io/learn/meta-one-subscription-explained

### A11. What Meta Business Suite does NOT do

- **No non-Meta networks.** No X, LinkedIn, TikTok, YouTube, Pinterest, Google Business Profile, Nextdoor, Bluesky and so on. — scope at https://www.facebook.com/business/help/205614130852988
- **No approval workflow for content or replies.** Roles control *who* can post or reply. No help page read describes draft → approve → publish, or reply approval. Absence is inferred from the content, inbox and roles pages read above. **UNVERIFIED as an absolute.**
- **No multi-client agency workspace** beyond switching between portfolios and assets, and partner access.
- **No email channel.** The agent reportedly does not handle email, phone, website live chat or SMS. — secondary: https://enterprisedna.co/resources/news/meta-business-agent-global-launch-whatsapp-2026/ (summarised in search results; not opened)
- **No review management**, beyond whatever appears as FB comments or recommendations. **UNVERIFIED.** No review inbox is documented in the pages read.
- **Rule automations are rigid:** exact, case-sensitive keyword matching; desktop-only for keywords and comment-to-message.
- **The AI agent auto-sends.** Draft-for-approval is undocumented.

### A12. What a paid third-party product must offer to beat "free" Meta Business Suite (analysis)

Derived from the gaps above. These are not Meta's statements.

1. **Non-Meta networks** in the same calendar and inbox: Google Business Profile, LinkedIn, TikTok, YouTube, X, Pinterest, Bluesky, and Nextdoor where possible.
2. **Human-approved AI replies.** Meta's agent auto-sends and you take over afterwards. A product where AI drafts and a person approves *before* anything is sent covers a gap Meta does not document. It is also the only model available for comments, which Meta's agent does not answer.
3. **Approval workflows** (draft → review → approve → auto-schedule), client or owner sign-off, and an audit trail. None is documented in the Suite.
4. **Multi-location / multi-tenant views** for businesses with several Pages, IG accounts or locations, and for the agencies that serve them.
5. **Reviews** (Google, and Facebook recommendations) alongside comments and DMs.
6. **Cross-network reporting with export and scheduled email reports.** Meta puts IG export behind Meta One Advanced at $49.99/mo per profile.
7. **More robust automation** than exact-match keywords, with fail-visible behaviour. Meta's own scheduled posts can fail silently until someone checks the Scheduled tab.

---

## PART B — Nextdoor for Business

### B1. What a business can do on Nextdoor natively

| Feature | Detail | Source |
|---|---|---|
| Free Business Page | Create or claim. "Get discovered locally …" | https://business.nextdoor.com/en-us/small-business |
| Business Posts | **Conflicting official pages.** The Business Page page says "unlimited free posts". The FAQ blog says posts "up to two times per calendar month", targeted to chosen neighborhoods or members within 2 miles, with "no charge … at this time". The current limit is **UNVERIFIED**. | https://business.nextdoor.com/en-us/getting-started/business-page ; https://business.nextdoor.com/en-us/small-business/resources/blog/the-most-commonly-asked-questions-from-local-businesses-about-nextdoor |
| Posting location | Log in to the claimed Business Page and use the Content tab | secondary, summarised in https://thestacc.com/blog/nextdoor-for-business/ |
| Local Deals (paid) | "Prices for Local Deals start as low as $1 with neighborhood targeting." For local businesses in listed categories. | FAQ blog URL above |
| Neighborhood Sponsorships (paid) | Eligible: real-estate agents and brokers, insurance agents, mortgage brokers, home builders, contractors, rental agents, property managers, vacation-rental managers, roofers. Price not stated (**UNVERIFIED**; one secondary source quotes about $150/mo in a prime ZIP). | FAQ blog URL above; https://powerdigitalmarketing.com/blog/nextdoor-advertising-cost/ |
| Ads (Nextdoor Ads Manager) | Self-serve. Image (FSF, Newsfeed, right rail), video, carousel and lead-gen ads. Daily or lifetime budgets. Pixel and Conversions API. "Programmatic access via our API and DSP integrations." Minimum spend not stated on the page. | https://business.nextdoor.com/en-us/advertise-with-nam-2026 |
| Recommendations / Faves | "Collect recommendations to drive word-of-mouth", plus "Faves" | https://business.nextdoor.com/en-us/getting-started/business-page |
| Replying to recommendations | Owners can reply (thank, or address concerns) from the recommendations area of the Business Page. Seen only in secondary sources; no official help article was retrievable (help.nextdoor.com failed TLS for the fetcher). **Partially UNVERIFIED.** | https://www.signpost.com/blog/nextdoor-business-reviews/ |
| Messages | The free page includes "responding to their messages from your page" | https://business.nextdoor.com/en-us/getting-started/business-page |
| Multiple locations | One Business Page per location; up to ten locations under one account | FAQ blog URL above |
| September 2026 changes | Redesigned free Business Pages (US, CA, UK) with AI summaries, a "what neighbors say" tab and a call/message bar. New onboarding and dashboard (US, coming soon). **"Feed for Local Businesses"**: businesses can comment and engage in the neighborhood feed (US, coming soon). **Opportunity Alerts** (US, paid) route verified neighbors' service requests to providers. | Business Wire release dated 2 Sept 2026 via https://finviz.com/news/387850/nextdoor-deepens-its-commitment-to-local-business-discovery-and-recommendations |
| Native scheduling of organic Business Posts | **UNVERIFIED.** Not found. | — |

### B2. Nextdoor developer platform — what exists and how to get access

Portal: https://developer.nextdoor.com/ — launched 8 Dec 2023 as "a self-service portal". — https://about.nextdoor.com/press-releases/partnerships/nextdoor-launches-developer-site-to-connect-partners-with-the-power-of-local

**Status:** "The Nextdoor API is currently in closed BETA" and requires requesting access. — https://developer.nextdoor.com/reference/sharing-availability

**Products**, as listed in the portal navigation: https://developer.nextdoor.com/

1. **Publish API** (Sharing). Create, edit and delete posts and comments. Agency posts, event posts, For Sale & Free posts. Toggle commenting. "Get your posts".
2. **Share Plugin (ShareKit).** "open offering … free to integrate it without Nextdoor approval". The user is sent to a pre-filled post form and "manually creates the content … after logging into Nextdoor". It cannot publish automatically. — https://developer.nextdoor.com/reference/sharing-introduction
3. **Content display APIs:** Search (posts, FSF, events, businesses), Trending Posts, Public Agency Feed.
4. **Verify by Nextdoor.**
5. **Advertising API v3:** advertisers, campaigns, ad groups, ads, creatives, audiences, targeting, **reporting**.
6. **Conversions API (CAPI)** and **Universal Pixel**.
7. **Nextdoor for News Partners:** publisher onboarding, page claim, create/update articles, ingestion report, post metrics.

**Access requirements**, from https://developer.nextdoor.com/reference/applying-for-access:

- **Publishing:**
  - Application form https://forms.gle/i2hHc4A9noKJRBGW9.
  - If approved, Nextdoor sends "a client id and client secret".
  - Eligibility criteria are **not published**.
- **Displaying content:** separate form https://forms.gle/RLebsZfBjk95LVnj7.
- **Ads and Conversion APIs:**
  - "intended for advertisers and the agencies and platforms" working for them.
  - "Every application is reviewed individually … Approval isn't guaranteed".
  - "Approved access requires an API agreement". CAPI agreements are signed per advertiser.
  - No exceptions for research or academic use.
  - Form: https://forms.gle/bbCSGUEuvrfxj3U58.
- **Share Plugin:** no application.

**Business-account flow for the Publish API** — https://developer.nextdoor.com/docs/business-share

1. Apply for a developer token, specifying "Publish API", with a company logo and a redirect URI.
2. OAuth with scope `profile`.
3. Exchange the code for an access token.
4. Call `/me/profiles`. "each account could have multiple business profiles". Save them and ask the user which profile to post as.
5. Pass the profile `id` (`secure_profile_id`) on every call.

Also: "The posting must come from a Nextdoor user (either a neighbor or a public agency or a business), not from the partner platform directly". It suits cases "where a perpetual connection makes sense". — https://developer.nextdoor.com/docs/sharing-overview

**Endpoint-level facts:**

| Endpoint | Business profile? | Notes | Source |
|---|---|---|---|
| Create post | **Yes** ("This endpoint support neighbor profile and business profile") | Scope `post:write`. Body up to 8,192 bytes/characters. One hashtag. Up to 10 public media URLs. Optional lat/lon geotag and smartlink URL. Returns `share_link`. No scheduling or audience parameter. | https://developer.nextdoor.com/reference/create-post |
| Create comment / reply to comment | **Not stated** | Scope `comment:write`. `id` (post share id), `body_text`, `parent_comment_id` for replies, up to 10 media. The page has no endpoint for *reading* comments. | https://developer.nextdoor.com/reference/create-comment-to-a-post-create-reply-to-a-comment |
| Get your posts (posts, comments, reactions, `view_count` days 0–14, `smartlink_click_count` 14 days) | **No:** "Business and Entity page users are not supported at the moment." | Scopes `post:read` / `cms_api:read` | https://developer.nextdoor.com/reference/get-your-posts |
| `/me/profiles` | Business yes; "Does not support agency account" | | https://developer.nextdoor.com/ |
| Recommendations / reviews | **No endpoint exists** in the API reference index | | https://developer.nextdoor.com/ |
| Direct messages | **No endpoint exists** in the API reference index | | https://developer.nextdoor.com/ |
| Ads reporting | Yes, for ads (Advertising API) | Ads only, not organic | https://developer.nextdoor.com/ |

### B3. Direct answers: can a third-party tool…

| Need | Answer | Access requirement |
|---|---|---|
| (a) Publish posts to a business page | **Yes, technically.** Create post supports business profiles. | Approved **Publish API** application (closed beta, criteria unpublished), client id and secret, the user's OAuth consent. The only no-approval path is the Share Plugin, which is **manual** (the user finishes the post on Nextdoor). |
| (b) Read and reply to comments and recommendations | **Read comments: no** for business pages ("Get your posts" excludes them, and no other read endpoint exists). **Reply to a comment:** a write endpoint exists, but business-profile support is not stated (**UNVERIFIED**). **Recommendations: no API at all.** **DMs: no API.** | Publish API approval |
| (c) Read analytics | **Organic business analytics: no** (the only metrics live in "Get your posts", which excludes business pages). **Ads analytics: yes**, through the Ads API reporting endpoints. | Ads/CAPI application, individual review and an API agreement |

The **strongest third-party corroboration** comes from Sprinklr's help center. It lists for Nextdoor:

- Supported: "Add Ads Account", "Publish Ad Campaigns", "Reporting".
- **Not supported:** "Publishing Content", "Private Messages", "View Posts, Comments, and Replies", "Engage on user Posts", "Engage on user Comments and Replies", "Edit a Published Post", "Recommendations".
- The reason given: "API Limitation – The functionality is not available for Sprinklr to build."

Source: https://www.sprinklr.com/help/articles/nextdoor/nextdoor-channel-capabilities-and-limitations/64106b657517d84a3aaf4c16

### B4. Third-party tools that officially support Nextdoor, and what they support

| Tool | Who it's for | What it supports on Nextdoor | Source |
|---|---|---|---|
| **Sprinklr** | Enterprise | **Ads only:** add an ads account, publish ad campaigns, reporting. Ad accounts are added by sending Sprinklr Support your access token. Organic publishing, DMs, comments and recommendations are **not supported** (API limitation). Integration announced in 2018. | https://www.sprinklr.com/help/articles/nextdoor/nextdoor-channel-capabilities-and-limitations/64106b657517d84a3aaf4c16 ; https://www.sprinklr.com/help/articles/dimensions/nextdoor-ads-reporting-dimensions/641caf8855c4c33ae8b816a9 ; https://blog.nextdoor.com/2018/08/21/nextdoor-teams-up-with-sprinklr |
| **Orlo** (UK) | **Public sector** ("public sector teams") | Create, schedule and post. **Geo-targeting** by your Nextdoor custom boundaries (districts, boroughs, wards). Drafts and approvals inside Orlo. Alt text. Post analytics including CTR ("a metric not currently available natively through Nextdoor"). Listening to neighbourhood conversations. **Inbox/comments not stated for Nextdoor.** Emergency alerts "Coming soon". | https://orlo.tech/features/nextdoor/ ; https://orlo.tech/integrations/nextdoor/ |
| **Social News Desk (SND)** | Government agencies and newsrooms | Publish and schedule "directly to Nextdoor for your agency's page". "Track how your posts are performing". Comment management not described. **Business pages not mentioned.** | https://www.socialnewsdesk.com/blog/nextdoor-publishing-available-for-government-agencies-in-snd/ |
| **Zencity** | Local government | Publishing and scheduling to Nextdoor plus access to Nextdoor conversation data. Claims to be "the only North American partner with both direct access to Nextdoor's conversation data and publishing capabilities" (vendor claim). | https://info.zencity.io/zencity-and-nextdoor-partnership ; Nextdoor press release (Orlo and ZenCity use the Publish API so "public agency clients can schedule, share, and geo-target posts"): https://about.nextdoor.com/press-releases/partnerships/nextdoor-launches-developer-site-to-connect-partners-with-the-power-of-local |
| **CivicPlus** | Local government | Emergency-notification posting to Nextdoor, and **social media archiving** of Nextdoor content (for public-records requests) | https://www.civicplus.com/news/nn/civicplus-announces-social-media-archiving-integration-with-nextdoor/ |
| **Marfeel (Amplify)** | News publishers | Publish and schedule articles, news and events to Nextdoor pages via OAuth. **Every post must include an article link.** Reactions, comments, shares and clicks collected as metrics. Comment reply not stated. | https://www.marfeel.com/whatsnew/nextdoor-is-now-a-publishing-channel-in-amplify ; https://www.marfeel.com/docs/social/social-configuration/set-up-the-nextdoor-integration |
| **True Anthem** | News publishers (Nextdoor News Accounts) | Schedule and automate link posts. Manual link and video posts. Analytics from Nextdoor's API. Each post needs an article URL. Processing takes 15–30 min. Nextdoor's Publisher Policy bars national news, politics, obituaries and opinion. Article dated 26 Aug 2025. | https://www.trueanthem.com/how-local-publishers-can-build-and-engage-audiences-on-nextdoor/ |
| **Jetpack Social** (WordPress) | Site owners | Auto-share new WordPress posts to Nextdoor. **"Jetpack Social currently supports sharing only to personal Nextdoor profiles." "Nextdoor Business Pages are not supported at this time."** | https://jetpack.com/support/jetpack-social/jetpack-social-nextdoor/ |
| Hootsuite, Sprout Social, Buffer, Later, **Planable**, **Loomly** | — | **No Nextdoor support.** Per Orlo's comparison (a competitor, last updated 23 Apr 2026). For Planable and Loomly this is also confirmed by their own help centers: neither lists Nextdoor, and a Nextdoor search returns zero articles in both. | https://orlo.tech/blog/best-social-media-scheduling-tools-in-2026/ ; https://help.planable.io/hc/en-us/articles/21715422567708-Social-media-platforms-supported ; https://loomly.zendesk.com/hc/en-us/articles/38554893818267-Which-social-media-platforms-does-Loomly-support |
| SOCi, Vista Social, Sendible, Agorapulse | — | **UNVERIFIED.** No source found showing Nextdoor support. | — |

**Pattern:** every tool that does organic publishing to Nextdoor serves **public agencies or news publishers**. No small-business SMM tool was found that publishes organic posts to a Nextdoor **Business Page**, and none was found that reads or replies to recommendations through an API.

### B5. Implications for GeekSocial (analysis)

- Organic publishing to a tenant's Nextdoor Business Page is **possible only after Nextdoor approves a Publish API application**. The API is in closed beta and its criteria are unpublished.
- Even with approval, the API does **not** let you read back business-page posts, comments, metrics, recommendations or messages. A Nextdoor "inbox" or AI-reply feature is **not buildable on the documented API**.
- The no-approval fallback is the **Share Plugin**: a prefilled post the person completes on Nextdoor.
- Ads (API agreement required) are the only programmatic analytics surface.

---

## PART C1 — Planable (approval-workflow focused)

The help center (Zendesk) was read through the API. The pricing page is https://planable.io/pricing/.

### Supported networks

Source: https://help.planable.io/hc/en-us/articles/21715422567708-Social-media-platforms-supported (updated 2026-11-10)

| Network | Yes/No | Notes |
|---|---|---|
| Facebook | Yes | Pages only. Personal profiles cannot be published to (Meta restriction since 2018): https://help.planable.io/hc/en-us/articles/21715498385052-Connect-a-page |
| Instagram | Yes | Business and Creator |
| Threads | Yes | |
| X | Yes | Basic plan: 1 post per 24h per page/workspace. Pro: unlimited. Not on the free trial. https://help.planable.io/hc/en-us/articles/21715263801756-X-ex-Twitter-publishing-per-plan |
| LinkedIn page / profile | Yes / Yes | |
| TikTok | Yes | Personal and Business |
| YouTube | Yes | Channels |
| Pinterest | Yes | Business accounts only |
| Google Business Profile | Yes | Verified locations. Image and video posts (video ≤25 MB), offers and events. CTA on posts and events. https://help.planable.io/hc/en-us/articles/21715425755804-Google-Business-Profile |
| Nextdoor | **No** | Zero help-center results for "Nextdoor" |
| Bluesky | **No** | Not listed; zero search results |
| Mastodon | **No** | Not listed; zero search results |
| Reddit | **No (publishing)** | Reddit is covered by **Social Listening** only |
| Other | "Universal content" pages for blogs, newsletters and other channels: planning and approval only. "Planable does not publish content on universal content pages." | https://help.planable.io/hc/en-us/articles/21715307378204-Schedule-posts-automatically-on-Approval |

### Publishing

- Views: Feed, Calendar, Grid (Pro+), List (Enterprise). Recurring posts. Auto-schedule on approval. Campaigns (3 / 10 / unlimited). Labels. Version history (1 week / 30 days / unlimited or 365 days). — https://help.planable.io/hc/en-us/articles/21715370520092-Questions-on-pricing ; https://planable.io/features/
- First comment, @mentions, alt text and timeslots / best time to post (the MCP page lists these as post-creation capabilities). LinkedIn polls and PDFs work in the web app only. — https://help.planable.io/hc/en-us/articles/27538577098780-How-to-connect-Planable-MCP-to-your-AI-tools
- "Sync previous content" (Pro+). "Save as an ad" (Pro). — Questions on pricing URL above
- Published posts are archived after 1 year. — help-center article index: https://help.planable.io/hc/en-us/articles/21715307893916

### AI content creation and repurposing

- Generate a post from a prompt. "Generate from posts" in the style of past posts (needs ≥5 posts). Rewrite presets (shorter, expand, surprise me, punchier). Hashtags. Caption from images or video (including carousels). Alt text. — https://help.planable.io/hc/en-us/articles/21715420314524-How-to-use-AI-to-create-posts-in-Planable ; https://help.planable.io/hc/en-us/articles/21715308272156-AI-Caption-generator
- **Brand context:** a per-workspace brand document (company description, products, tone, values, approved/banned terminology, audience). The AI reads up to 4,000 characters of it. Editors+ can edit it, and client visibility can be toggled. — https://help.planable.io/hc/en-us/articles/27960528842396-Brand-context-for-AI-generated-content
- **Guardrail:** "Nothing the AI writes goes anywhere on its own … AI never publishes a post for you." — AI Caption generator URL above
- The pricing page shows AI rewrite and generate on all plans with no stated credit quota. — https://planable.io/pricing/

### Engagement inbox

Social Inbox is a **paid add-on**: $9/workspace/month or $90/workspace/year, with a 30-day free trial. — https://help.planable.io/hc/en-us/articles/29213177431836-Managing-your-Social-Inbox

- **Comments:** FB, IG, LinkedIn (company pages; personal profiles only for posts published through Planable), TikTok (Business only).
- Ad and boosted-post comments on FB and IG are available once a Meta ad account is connected.
- **DMs:** FB Page and IG only. IG limits:
  - No voice, reactions or group chats.
  - First 15 conversations only.
  - 20 messages per load.
- Statuses Open / Later / Done. **Sentiment** (Positive / Negative / Neutral / Question, needs ≥5 comments). **"Answer with AI"** gives suggested replies.
- Only posts and comments from the **last 14 days** are shown.
- Permissions to reply are set per member. — https://help.planable.io/hc/en-us/articles/21715283422236-Reply-to-comments-Social-Inbox

### Reviews

- **Not supported.** Google Business Profile is a publishing and analytics channel only; the Social Inbox channel list has no GBP or review source. — Supported-platforms URL above

### Analytics and reports

Analytics is an **add-on**: $14/workspace/month or $140/year. — https://help.planable.io/hc/en-us/articles/21715231495196-Planable-Analytics

- Platforms: FB, IG, LinkedIn (pages and profiles; paid data for pages only), TikTok Business, GBP, YouTube, X. X and GBP appear in single-channel view only.
- Organic vs paid split once an ad account is connected (FB, IG, LinkedIn, TikTok).
- Spend, CPM, CPC and CPE.
- Competitor analytics.
- **Customizable reports:** logo and titles, a **public share link** (no login; it exposes ad spend if the report is built on paid data), PDF and CSV export. — https://help.planable.io/hc/en-us/articles/24935960220444-Creating-Customizable-Reports-in-Planable-Analytics
- **Social Listening add-on:** $99/workspace/month or $990/year, 30-day trial.
  - Sources: X, YouTube, Reddit, TikTok, FB, IG, LinkedIn and the web.
  - 1 brand (+3 aliases), 3 topics and 3 competitors per workspace.
  - Brand and competitors sync every 24h; topics every 7 days.
  - Public posts only. Replies happen on the original platform.
  - Source: https://help.planable.io/hc/en-us/articles/29421245139356-Social-listening

### Collaboration and approval workflows

Source: https://help.planable.io/hc/en-us/articles/21715462785180-Approvals-and-Approval-Workflows

- **Four workflows, set per workspace:**
  - **None** (all plans)
  - **Optional** (all plans)
  - **Required**: "no post can be scheduled or published until at least one approver has approved it" (Pro, Enterprise)
  - **Multi-level** (Enterprise)
- Only owners and admins change the workflow.
- Switching to Required prompts "Auto-approve scheduled posts?"
- **Multi-level:** — https://help.planable.io/hc/en-us/articles/21715417710876-Multi-level-approvals
  - Up to **10 levels** per workspace. Default levels are "Team" and "Clients".
  - Levels approve **in order**. **One approver per level is enough.**
  - Only an approver of the most recent approving level can revoke. Published posts cannot be revoked.
- **Settings:**
  - **Lock content after approval.**
  - **Schedule automatically on approval.** Revoking an approval unschedules the post. — https://help.planable.io/hc/en-us/articles/21715307378204-Schedule-posts-automatically-on-Approval
  - **Automated approval reminders:** an email 48h after the request and 1 day before the publish date, only for posts scheduled at least 2 days out. Off by default. — https://help.planable.io/hc/en-us/articles/27486527769500-Automated-reminders-for-approvals
- **External client approval** (Pro+): — https://help.planable.io/hc/en-us/articles/22581435847324-How-to-get-client-approval-for-content-in-Planable
  - Invite the client with the **Client** membership type and the **Approver** permission.
  - "Create new posts as internal", so drafts stay hidden until "Make visible to clients".
  - A client **"My approvals"** queue (Awaiting / Feedback left / Approved), with approve-all.
  - Bulk approval requests (Pro+).
  - **Public links** allow view and comment but **not approve**. Guests do not count against members. — https://help.planable.io/hc/en-us/articles/21715458842652-Share-content-with-external-collaborators
- Comments, @mentions, attachments, **annotations** on text, **suggestions** (tracked edits), resolve, emoji reactions. — https://planable.io/features/ ; help-center article index

### Agency / multi-client

- **One workspace per client.** Each has its own pages, members, approval workflow and brand context.
- Roles:
  - Guest: view and comment
  - Writer
  - Contributor: create, schedule, publish, analytics, engage
  - Approver
  - Administrator
  - Owner
- Granular permissions: Approve / Edit / Publish / Analyze / Administrate.
- A client invited as Admin can connect their own pages. Client membership is not on Basic.
- Sources: https://help.planable.io/hc/en-us/articles/21715390472348-Setting-up-accounts-for-multiple-clients ; https://help.planable.io/hc/en-us/articles/22072063868444-User-permissions

### Email features

- Email notifications (when idle or always; immediately or hourly), approval-reminder emails, listening alerts, and invite emails. — https://help.planable.io/hc/en-us/articles/21715379143068-Notifications-settings
- Invoices are emailed each billing cycle. — Questions on pricing URL
- Newsletters can be planned and approved as "universal content", but **Planable does not send email**. No email-marketing feature was found.

### Automation, AI agents and guardrails

- No autonomous reply agent. AI output must be applied or sent by a person (see AI and inbox above).
- **Planable MCP** (https://mcp.planable.io/mcp) lets Claude, ChatGPT, Gemini and others browse, create, schedule and approve posts, read and reply to inbox items, and pull analytics. — https://help.planable.io/hc/en-us/articles/27538577098780-How-to-connect-Planable-MCP-to-your-AI-tools
- MCP guardrails:
  - "The AI can only do what you can do", including plan limits and permissions.
  - Per-action toggles live in the AI tool's connector settings.
  - The connection expires after 8h idle; consent lasts 180 days.
  - Planable advises starting read-only.

### Integrations / API

- **Public API v1** (Pro+). Owners or admins generate tokens with Read or Write scope, chosen workspaces and an expiry; up to 10 active tokens.
- Endpoints: workspaces, pages and analytics, posts (create/update unpublished, comments), media, labels, IG stories. Base URL `https://api.planable.io/api/v1`.
- Source: https://help.planable.io/hc/en-us/articles/27638359236508-How-to-connect-and-use-the-Planable-Public-API
- Also Zapier, MCP, Canva, and Slack/email/push notifications. — same URL ; https://planable.io/features/

### Pricing

Sources: https://help.planable.io/hc/en-us/articles/21715370520092-Questions-on-pricing (updated 2026-10-30) ; https://help.planable.io/hc/en-us/articles/21715426370588-Do-you-have-a-free-plan ; https://planable.io/pricing/

| Plan | Monthly | Annual | Key limits |
|---|---|---|---|
| Free trial | $0 | — | **No time limit, 50 posts total (never resets)**, unlimited users and workspaces, up to 50 pages per workspace, includes Multi-level approvals. No Public API and no paid add-ons. No credit card. X publishing not available. |
| Basic | **$39 / workspace / month** | **$390 / workspace / year** | 4 pages, 60 posts/month, 5 labels, 3 campaigns, 10 GB, Feed and Calendar, approvals None/Optional, no client membership, X limited to 1 post per 24h |
| Pro | **$59 / workspace / month** | **$590 / workspace / year** | 10 pages, 150 posts/month (500 with the Extra posts add-on at $49/mo or $490/yr), Grid view, **Required** approval, bulk approval, internal/external collaboration, Public API |
| Enterprise | Custom, **from $2,500/year** | — | Unlimited pages and posts, List view, **Multi-level** approvals, SSO, dedicated support, from 100 GB storage |
| Add-ons (per workspace) | Analytics $14 · Social Inbox $9 · Social Listening $99 · Extra storage $20 per 50 GB | $140 · $90 · $990 · $200 | Each has a 30-day free trial |

- **Note on the pricing page:** it shows annual-billed monthly equivalents — Basic **$33** and Pro **$49** per workspace per month, add-ons $12 / $7.5 / $82.5 — together with "2 months free" for yearly billing. These match $390/12 and $590/12.
- **Payment:** "any major credit card … processed securely by Stripe", debit cards too. Bank or ACH transfer for Enterprise only. **PayPal: not listed** (not accepted per the stated methods).
- Refunds: 14 days, first-time subscriptions only. Nonprofits: 30% off. — https://help.planable.io/hc/en-us/articles/21715384970012-Refund-Policy ; https://help.planable.io/hc/en-us/articles/21715322073244-Frequently-asked-Questions-and-answers

### Onboarding — does the customer create their own developer app or keys?

- **No.** Pages are connected through Planable's own OAuth app: Channels → + → Add Pages, then authenticate. — https://help.planable.io/hc/en-us/articles/21715498385052-Connect-a-page
- A Meta ad account and a LinkedIn ad account are separate OAuth connections. — same URL
- API tokens are optional and only for the customer's own scripts.

---

## PART C2 — Loomly (calendar + approvals)

Loomly is now operated by **Bending Spoons** (site footer: "© Bending Spoons Operations S.p.A."). — https://www.loomly.com/pricing

### Supported networks

Current list: https://loomly.zendesk.com/hc/en-us/articles/38554893818267-Which-social-media-platforms-does-Loomly-support (updated 2026-05-06) and https://loomly.zendesk.com/hc/en-us/articles/39089576017435-0-Introduction-to-Loomly. **One account per platform per calendar.**

| Network | Yes/No | Notes |
|---|---|---|
| Facebook | Yes | Pages |
| Instagram | Yes | Reels, Stories, posts and carousels: https://www.loomly.com/integrations |
| Threads | Yes | |
| X | **No** | Not on the current platform list. An older Intercom-hosted help page still lists X/Twitter (https://intercom.help/loomly/en/articles/1897339-which-social-media-platforms-does-loomly-support), so treat X as **dropped**. |
| LinkedIn page / profile | Yes / Yes | Profiles have no analytics. https://loomly.zendesk.com/hc/en-us/articles/38858615843483-LinkedIn-and-Loomly-overview |
| TikTok | Yes | |
| YouTube | Yes | |
| Pinterest | Yes | |
| Google Business Profile | Yes | Verified locations. What's New, Event and Offer posts. CTAs. Single image (no multi-image, no video). https://loomly.zendesk.com/hc/en-us/articles/38858836319387-Google-Business-Profile-and-Loomly-overview |
| Snapchat | Yes | |
| Nextdoor | **No** | Not listed; zero help-center results |
| Bluesky | Yes | |
| Mastodon | **No** | Not listed; zero results |
| Reddit | **No** (native) | Possible only through a "Custom Channel" (manual publish with email reminder) or a Zapier "Reddit (link post)" zap. https://loomly.zendesk.com/hc/en-us/articles/38970534476827-How-to-Set-Up-Custom-Channels-in-Loomly ; https://loomly.zendesk.com/hc/en-us/articles/38970621662619-How-to-Use-Zapier-to-Connect-Loomly-with-Other-Apps |

### Publishing

- Direct publishing; unlimited scheduling (paid plans); recurring posts; bulk posting (CSV, up to 60 posts at once); best time to post (FB, IG, LinkedIn); hashtag manager; Idea board (kanban); link shorteners; media editor (Loomly Studio); slideshow creator; Canva, Google Drive, Unsplash and Giphy. — https://www.loomly.com/pricing ; https://loomly.zendesk.com/hc/en-us/articles/38554846554779-Plans-and-pricing-update-2025
- LinkedIn: PDF carousels, polls, first comment, organic audience targeting (segment must have ≥300 followers), post sponsoring. — LinkedIn overview URL above
- **Post sponsoring / boosting** for Facebook, Instagram and LinkedIn from inside Loomly. — https://loomly.zendesk.com/hc/en-us/articles/38970198857755-Boost-your-posts-with-Facebook-ads
- Clone or duplicate posts across calendars. — https://loomly.zendesk.com/hc/en-us/articles/38818583806875-What-do-Users-and-Social-Accounts-mean-in-the-pricing-table

### AI content creation and repurposing

- **AI Assistant chat:** answers questions, generates post ideas "drawing on what has performed best", creates ready-to-publish posts including **captions, hashtags and generated images**, analyses performance, and reworks content (tone, translate, shorten, expand). — https://loomly.zendesk.com/hc/en-us/articles/50360331671323-AI-Assistant
- **AI Captions:** generate, improve, change tone, translate, resize, add hashtags. Uses calendar **AI credits**. — https://loomly.zendesk.com/hc/en-us/articles/38554867579163-AI-Captions
- **AI Knowledge** (per calendar): brand instructions ("things you want the AI to do or not to do", content rules), brand website, and a help-centre link plus tone of voice for DMs and comments. Shows AI usage and reset date. — https://loomly.zendesk.com/hc/en-us/articles/50360228749467-AI-Knowledge-in-Calendar-Settings
- AI usage: "Limited monthly usage" (Starter) vs "Extended" (Beyond). — https://www.loomly.com/pricing

### Engagement inbox ("Interactions")

Source: https://loomly.zendesk.com/hc/en-us/articles/39173171844507-How-to-respond-to-messages-comments-replies-and-mentions-with-Loomly (updated 2026-09-03)

- **Facebook:** comments, mentions, Messenger (Pages).
- **Instagram:** comments and tags; DMs for Business accounts and for Creator accounts under 500k followers.
- **LinkedIn:** comments on profiles and pages; mentions on pages.
- **TikTok:** comments.
- **Bluesky:** comments and mentions.
- Not supported: Instagram ad or boosted-post interactions.
- Limits: 5,000 comments and 2,500 messages per rolling 14 days; 6 months of retention.
- Statuses Unread / Read / Archived; assign to a collaborator; see who replied; **saved replies**.
- **Reply with AI:** the user clicks the button, the AI generates a reply from AI Knowledge, and the user sends it. "Try AI Reply" sandbox. — https://loomly.zendesk.com/hc/en-us/articles/50360315177243-Reply-with-AI

### Reviews

- **Not supported.** No review management found in the help center. GBP is a publishing channel only. — GBP overview URL above

### Analytics and reports

- Basic analytics on every plan: account overview, per-network account stats, post metrics, Loom.ly link click counts, sources and locations, IG story metrics. Includes posts published outside Loomly. — https://loomly.zendesk.com/hc/en-us/articles/38554911694747-What-kind-of-analytics-does-Loomly-offer
- **Advanced Analytics** (higher tiers; needs ≥5 posts): date-range comparison, label filters, PDF and CSV export. — same URL
- **Scheduled Reports:** weekly or monthly PDF emails, up to 3 per calendar, up to 25 recipients. Beyond only per the pricing page. — https://loomly.zendesk.com/hc/en-us/articles/38823508846107-What-are-Scheduled-Reports
- **AI analytics** and **social listening** (AI competitor and sentiment analysis needing a brand website): 2 runs a month on Starter, 5 on Beyond. — https://loomly.zendesk.com/hc/en-us/articles/50360340121371-Social-Listening ; https://www.loomly.com/pricing

### Collaboration and approval workflows

- **Three approval workflows per calendar.** — https://loomly.zendesk.com/hc/en-us/articles/39019477164187-What-are-collaboration-workflows (updated 2026-09-04)
  - **Original:** 2+ approvers. Draft → Requires Edits → Pending Approval → Approved → Scheduled → Published / Canceled.
  - **Lite:** 1 approver. Approval goes straight to Scheduled.
  - **Zero:** no approval.
- **Custom Workflows:**
  - **Assignment Triggers** auto-assign users when a post enters a state; one trigger per state.
  - **State Guards** block entry into a state until **all** or **any** listed users act. Each guard has **Allow/Block** skipping and **"Overrideable By"** users, for vacations.
  - If a post moves backwards, the guard has to be satisfied again.
  - Plan: the pricing page lists "Custom roles & workflows" for **Beyond**. The help page says "Advanced Plan and up" (a legacy plan name).
- **Roles:** Owner, Editor, Contributor (no approve or schedule), **Client** (no create or edit, **cannot see Drafts**, can approve and schedule), Viewer. **Custom Roles** can also limit which post dates a person can see. — https://loomly.zendesk.com/hc/en-us/articles/39019855278107-Which-permissions-can-I-grant-to-my-Collaborators
  - Plan conflict: that article says Custom Roles are on "Starter plan and up"; the pricing page says Beyond.
- **Private comments** are hidden from Client-role users and can be the default. Post mockups. End-to-end post history. — https://loomly.zendesk.com/hc/en-us/articles/38818705572763-Is-Loomly-suitable-for-agencies
- Notifications only go to **assigned** collaborators, unless Assignment Triggers are set up. — https://loomly.zendesk.com/hc/en-us/articles/39256542800027-5-Collaborating-with-your-team-and-clients
- **Offline approval:** export calendar, list or post views as PDF. — same URL
- External clients must be invited users. They are **free** (no subscription needed) but **count as users** against the plan's seat limit. — same URL ; Users-and-Social-Accounts URL above

### Agency / multi-client

- **One calendar per client, brand or location.** Clients see only the calendars they're invited to. Unlimited calendars. You pay per connected social account and per user, not per calendar. — Is-Loomly-suitable-for-agencies URL ; Users-and-Social-Accounts URL
- **Custom branding** (Beyond): branded subdomain `yourbrand.myloomly.com`, logo on the app and PDF exports, favicon. Emails still come from support@myloomly.com. The mobile app is not branded. — https://loomly.zendesk.com/hc/en-us/articles/38818856191003-Do-you-offer-white-label

### Email features

- Email notifications (per activity, with frequency settings), scheduled report emails, **email reminders to publish manually** on Custom Channels (for example a newsletter), and Mailchimp through Zapier. — Collaborating URL ; Custom Channels URL ; Zapier URL
- **No email-marketing or sending feature.**

### Automation, AI agents and guardrails

- No autonomous reply agent was found. **Reply with AI requires a person to click and send.** AI Knowledge holds do/don't rules. Approval workflows and State Guards gate publishing. — Reply-with-AI URL ; AI-Knowledge URL ; workflows URL
- Zapier triggers: new post, post published, post failed, publish required, assignment updated, state updated, edited, library item, internal comment, invite. Zapier actions: create Quick Post, create Custom Post Idea. — Zapier URL

### Integrations / API

- "Loomly does not currently offer a public API." Zapier, Slack, Microsoft Teams, Canva, Google Drive, Unsplash, Giphy. — Zapier URL ; https://www.loomly.com/integrations

### Pricing

Source: https://www.loomly.com/pricing, read in a browser 2026-11-18 with the Monthly and Yearly toggles.

| Plan | Monthly billing | Yearly billing | Limits |
|---|---|---|---|
| **Free** | $0 | — | 1 user, 3 social accounts, **5 posts per rolling month** (counted by creation date), direct publishing, analytics dashboard, export, Zapier, 2FA. "might not be available for all users." https://loomly.zendesk.com/hc/en-us/articles/39051127130779-New-Free-plan |
| **Starter** | **$65/mo** | **$49/mo ($588/yr)** | 12 social accounts, 3 users, unlimited calendars, approval workflows and roles, advanced analytics, limited AI, 2 listening runs/month |
| **Beyond** | **$332/mo** | **$249/mo ($2,988/yr)** | 60 social accounts, unlimited users, custom branding, custom roles and workflows, scheduled reports, calendar-level 2FA enforcement, extended AI, 5 listening runs/month |
| **Enterprise** | Custom | Custom | 61+ accounts, priority support |

- Yearly saves 25% and cannot be terminated early.
- Nonprofits: 50% lifetime discount.
- **Free trial:** "Start your free trial today. No credit card required." Trial **length UNVERIFIED**. The help center says trial "availability can vary" and the team "is unable to customize this experience". — https://loomly.zendesk.com/hc/en-us/articles/38827847926299-How-to-manage-my-subscription
- **Payment:** "Visa, MasterCard, American Express, JCB, Discover, and Diners Club". No checks or cash. "cannot facilitate sending manual invoices or bank transfers." **PayPal: not listed.** — https://loomly.zendesk.com/hc/en-us/articles/39249341535131-How-can-I-update-my-billing-information-and-payment-method
- Refunds: annual within 60 days, monthly within 20 days, self-serve. — https://loomly.zendesk.com/hc/en-us/articles/38828704091803-What-is-your-refund-policy

### Onboarding — does the customer create their own developer app or keys?

- **No.** Accounts are connected through Loomly's OAuth from Calendar Settings → Social Accounts. — https://loomly.zendesk.com/hc/en-us/articles/38971313355547-How-do-I-connect-my-social-media-accounts
- LinkedIn Pages connect through the admin's profile. — LinkedIn overview URL
- Boosting requires re-connecting the FB Page and choosing an ad account. — Boost URL

---

## Cross-cutting: approval-workflow patterns seen (Planable, Loomly) vs Meta (analysis, with sources above)

1. **Approval mode is a per-workspace or per-calendar setting with graded strictness.**
   - Planable: None / Optional / Required / Multi-level.
   - Loomly: Zero / Lite / Original / Custom.
   - Strictness is a pricing lever: Required is Pro, Multi-level is Enterprise; custom workflows are Beyond.
2. **Hidden-until-ready.** Drafts and internal comments are invisible to the client role.
   - Planable: "Create new posts as internal" plus "Make visible to clients".
   - Loomly: Client role cannot see Drafts; private comments.
3. **An approval queue for the approver**, with bulk approve.
   - Planable: "My approvals" with approve-all.
   - Loomly: approval statuses plus assignments.
4. **Approve → auto-schedule, revoke → unschedule** (Planable). **Lock content after approval** (Planable).
5. **Sequential levels.**
   - Planable: up to 10 levels, any one approver per level.
   - Loomly: State Guards require **all** or **any** listed users, with **override-by** delegates and **block-skip**.
6. **Nudges:** Planable sends reminder emails 48h after a request and 1 day before publish. Loomly notifies only assigned users.
7. **External reviewers:**
   - Planable: public links allow view and comment but **not approve**. Approvers need accounts.
   - Loomly: clients are free accounts that consume seats.
8. **AI replies are drafts; a person sends.**
   - Planable: "Answer with AI".
   - Loomly: "Reply with AI".
   - Meta Inbox suggestions "don't send automatically".
   - **The exception is Meta Business Agent, which auto-sends with human takeover and does not document a draft-for-approval mode.**
