# AI content automation and repurposing tools: what they create, repurpose, publish and answer

**Checked 2026-11-18.** Every claim below has its source URL. Vendor pages and help centres came first. Third-party pages are labelled "third-party". If something could not be confirmed, it says **UNVERIFIED**.

Tools covered: ContentStudio, Lately (Lately.ai), Missinglettr, FeedHive, Ocoya, Predis.ai. After them comes a market cross-check of social media management (SMM) products that include email.

## How this was checked, and its limits

- I read vendor pages directly with WebFetch, curl or a browser. For prices shown only by a client-side billing toggle (FeedHive), I clicked the toggle in a real browser.
- **Lately's website was unreachable on 2026-11-18.** Every lately.ai URL I tried (`/`, `/pricing`, `/blog`) returned Cloudflare "Error 1014 — CNAME Cross-User Banned", both through curl and in a real Chrome tab. `help.lately.ai` did not resolve, and `app.lately.ai` returned 404. Lately's facts therefore come from the Internet Archive's copies of lately.ai taken 2026-09-22 to 2026-10-12, which were the latest available. Each one is cited by its archive URL.
- The session's WebSearch budget ran out during the email cross-check. Wix, GoDaddy, Vendasta, Klaviyo and Brevo were therefore **not checked** (see the cross-check section).
- PayPal: none of the six vendors' pages that I read says PayPal is accepted. ContentStudio's page names cards through Paddle. For all six, **PayPal is UNVERIFIED**.

---

## At a glance

### Publishing networks (vendor sources, cited in each section)

| Network | ContentStudio | Lately | Missinglettr | FeedHive | Ocoya | Predis.ai |
|---|---|---|---|---|---|---|
| Facebook | Yes | Yes (company Pages) | Yes (Pages + Groups) | Yes (+ Groups doc) | Yes (Pages only) | Yes (+ Groups) |
| Instagram | Yes | Yes (business) | Yes (business only) | Yes (business) | Yes | Yes (+ Stories) |
| Threads | Yes | Not listed | Not listed | Yes | Yes | Not listed |
| X | Yes | Yes | Yes | Yes | Yes | Yes |
| LinkedIn Page | Yes | Yes | Yes | Yes | Yes | Yes |
| LinkedIn profile | Yes | Yes | Yes | UNVERIFIED | Yes | Yes |
| TikTok | Yes | Not listed | Not listed | Yes | Yes | Yes |
| YouTube | Yes | Yes (top tier) | As a content *source* only | Yes | Shorts only | Shorts |
| Pinterest | Yes | Not listed | Not listed (vendor) | Yes | Yes | Vendor pages conflict |
| Google Business Profile | Yes | Not listed | Yes ("Google My Business") | Yes | Yes | Yes |
| **Nextdoor** | **No** | **No** | **No** | **No** | **No** | **No** |
| Bluesky | Yes | Not listed | Yes | Not listed | Yes | Not listed |
| Mastodon | Not listed | Not listed | Yes | Not listed | Yes | Not listed |
| Reddit | Not listed | Not listed | Not listed | Not listed | Not listed | Not listed |

"Not listed" means the vendor's own current network list leaves it out. **No tool in this group, and no suite in the cross-check, lists Nextdoor.**

### Repurposing, email and inbox

| | Main repurposing pattern | Writes email/newsletters? | Sends email? | Engagement inbox | AI replies sent without a person? |
|---|---|---|---|---|---|
| ContentStudio | Per-platform caption rewrite; long video into clips; RSS to posts | No | No (only analytics reports by email) | Yes (Advanced+) | **Yes, optional**: auto-reply rules have a "Send Automatically" mode, or "Save as Draft" |
| Lately | Long text, audio or video into "dozens" of posts plus clips, in a learned voice | No (takes newsletters as *input*) | No | None found | N/A. Enterprise sells "100% automation" of publishing |
| Missinglettr | One blog post becomes a year-long drip of 9 posts by default | No | No | None | N/A. "Nothing is ever published without your approval" |
| FeedHive | One idea becomes channel versions; recycling suggestions | No | No | Yes (AI reply drafts) | No auto-reply found; workflows *can* publish posts without approval |
| Ocoya | Idea or goal becomes a campaign of up to 90 posts; RSS or CMS article becomes posts | **Coming soon** (AI-drafted newsletters, sent from Ocoya) | **Coming soon** | Yes (FB, IG, Google reviews) | AI drafts are never sent by themselves, but **workflows can auto-reply** |
| Predis.ai | Blog URL to carousel or video; product URL to ad or video | No | No | None found | N/A. Auto-post generates and publishes on schedule; review optional |

---

## 1. ContentStudio

### 1.1 What it is for, and for whom
An all-in-one SMM suite: publishing and calendar, AI Studio (text, image and video generation, clip maker), social inbox, analytics, social listening, automations (RSS, evergreen, bulk CSV), approvals and client workspaces, white label, plus a developer API, MCP server and CLI. ([features](https://contentstudio.io/features), [pricing](https://contentstudio.io/pricing))

Target customers are solo businesses (Standard), teams (Advanced) and agencies (Agency Unlimited, white label, reseller). There is also an "API-centric" plan for developers. ([pricing](https://contentstudio.io/pricing))

### 1.2 Networks
The vendor lists Facebook, Instagram, X, LinkedIn, YouTube, TikTok, Pinterest, Threads, Bluesky, Telegram and Google Business Profile. ([pricing](https://contentstudio.io/pricing), [integrations](https://contentstudio.io/integrations), [features](https://contentstudio.io/features))

- **LinkedIn:** both personal profile and company page ("Connect LinkedIn Profile and Pages"). ([docs](https://docs.contentstudio.io/article/423-connect-linkdin))
- **Tumblr:** still listed under "Blogs" on the integrations page. ([integrations](https://contentstudio.io/integrations))
- **Mastodon, Reddit, Nextdoor:** not listed on any vendor page I read ([integrations](https://contentstudio.io/integrations), [llms.txt](https://contentstudio.io/llms.txt)). A competitor page also says Mastodon is not listed, as of June 2026 (third-party, [SocialKit](https://socialk.it/en/vs/contentstudio)).

### 1.3 Content creation and repurposing
- **Inputs:**
  - Ideas and prompts in AI Studio.
  - Long videos: Clip Maker / Video Clips "Cut long videos into short, shareable clips automatically" ([AI Studio](https://contentstudio.io/ai-studio)); llms.txt describes it as turning "long-form videos into short, social-ready clips for Reels, Shorts, and TikTok" ([llms.txt](https://contentstudio.io/llms.txt)).
  - RSS feeds or OPML imports for automation ([features](https://contentstudio.io/features)).
  - None of the pages read describes a **paste-a-blog-URL, get-posts** feature ([AI Studio](https://contentstudio.io/ai-studio), [automation](https://contentstudio.io/social-media-automation)).
- **Outputs:**
  - Captions with hashtags; "Customize with AI" generates "a tailored caption for each selected platform" (5 Aug 2026 release) ([announcements](https://contentstudio.frill.co/announcements)).
  - AI images (25/50/125 per month by plan), AI video (100/200/500) and video clipping (100/200/500) ([pricing](https://contentstudio.io/pricing)).
  - AI Studio also has text-to-video, image-to-video, lip sync and a talking avatar ([AI Studio](https://contentstudio.io/ai-studio)).
  - Carousels: not mentioned in AI Studio ([AI Studio](https://contentstudio.io/ai-studio)). LinkedIn carousel posting was added to the API on 5 Aug 2026 ([announcements](https://contentstudio.frill.co/announcements)).
- **Per-network versions:** yes, through "Customize with AI" ([announcements](https://contentstudio.frill.co/announcements)).
- **Evergreen:** an AI step generates headline variations for each repost ([automation](https://contentstudio.io/social-media-automation)).
- **Brand voice:** "Brand Voice" in AI Studio ([AI Studio](https://contentstudio.io/ai-studio)). On 28 Jul 2026 brand style, voice and profile were merged into one "Unified Brand Profile" per workspace, which can sync websites, documents and accounts as source material ([announcements](https://contentstudio.frill.co/announcements)).
- **Curation:** "Discover" lets you manage and read feeds ([features](https://contentstudio.io/features)). Discovery sources include Feedly, Flickr, Imgur, Giphy, Pixabay and Dailymotion ([integrations](https://contentstudio.io/integrations)).
- **Drip campaigns over weeks:** not offered as such. Evergreen re-queues top posts at intervals ([automation](https://contentstudio.io/social-media-automation)).
- **Removed in 2026 (third-party):** a pricing tracker logged "Publishing to blogs" (WordPress, Medium, Shopify, Webflow, Tumblr) and "curated content automation campaigns" as removed from the pricing page between Mar 30 and Jun 29, 2026 ([PricingSaaS](https://pricingsaas.com/companies/contentstudio/diffs/2026Q2)).

### 1.4 Email
- No newsletter or email-sending product. Email is used only to deliver analytics reports, by email or as scheduled recurring sends (25 Aug 2026 release) ([announcements](https://contentstudio.frill.co/announcements), [pricing](https://contentstudio.io/pricing)).
- A free "AI Email Writer" tool is listed as a marketing tool, not a product feature ([llms.txt](https://contentstudio.io/llms.txt)).
- No Mailchimp or email integration on the integrations page ([integrations](https://contentstudio.io/integrations)).

### 1.5 Publishing and scheduling
- Calendar, Grid and Feed planner views; a shareable calendar; smart scheduling; content categories with time slots ([features](https://contentstudio.io/features)).
- A "Queued" publishing option, alongside Draft and Schedule ([announcements](https://contentstudio.frill.co/announcements)).
- Evergreen recycling, RSS auto-posting (you choose how captions are written and the schedule) and bulk CSV ([automation](https://contentstudio.io/social-media-automation)).
- An Automations API covers RSS, evergreen and bulk CSV (23 Sep 2026) ([announcements](https://contentstudio.frill.co/announcements)).
- Unlimited posts on Standard, Advanced and Agency ([pricing](https://contentstudio.io/pricing)).

### 1.6 Engagement inbox and replies
- **Plan:** Advanced and Agency only. The FAQ says it is "not available on the Standard plan" ([pricing](https://contentstudio.io/pricing)).
- **Coverage:**
  - Facebook, Instagram, X, LinkedIn and Google Business Profile: DMs, comments on posts and ads, mentions and reviews ([social inbox](https://contentstudio.io/social-inbox)).
  - YouTube comments were added to the inbox (announcement) ([announcement](https://contentstudio.frill.co/announcements/youtube-comments-brand-knowledge-enhancements-api-updates-fixes-and-improvements)).
- **Team features:** assignment, collision detection, internal notes, saved replies, role-based access ([social inbox](https://contentstudio.io/social-inbox)).
- **Manual "Reply with AI":** the AI writes a draft; you can Rephrase, Lengthen, Shorten or Fix Grammar; you click Send ([docs](https://docs.contentstudio.io/article/1147-ai-integeration-in-inbox)).
- **Automated "Auto Replies" rules** ([docs](https://docs.contentstudio.io/article/1209-auto-replies-in-inbox)):
  - Triggers are keywords plus a "What should AI look for?" description of the intent.
  - Channel: Private Chat (FB/IG DMs), Post Comments (FB, IG, YouTube, LinkedIn) or Reviews (Google Business Profile), scoped to chosen accounts.
  - **AI Reply** ("Premium") can use a Brand Voice and uploaded help docs. **Manual Reply** sends a fixed text with name variables.
  - **"AI Send Mode": "Send Automatically"** (sent immediately, cannot be undone) **or "Save as Draft"** for team review.
  - No sentiment exclusion, schedule or delay guardrail is documented.
- **Auto-reply credits:** 100/month on Advanced and 250 on Agency ([pricing](https://contentstudio.io/pricing)). More can be bought ([docs](https://docs.contentstudio.io/article/1209-auto-replies-in-inbox)).

### 1.7 Analytics
- Dashboard with PDF/CSV export and competitor analytics ([features](https://contentstudio.io/features)).
- Google Ads and Meta Ads analytics; Google Data Studio connector ([features](https://contentstudio.io/features)).
- Bluesky analytics (Aug 2026) and YouTube competitor analytics (Sep 2026) ([announcements](https://contentstudio.frill.co/announcements)).
- White-label reports on Advanced and Agency ([pricing](https://contentstudio.io/pricing)).

### 1.8 Collaboration and agencies
- Approval workflow on Advanced and Agency; approver/client dashboard and client invitations ([pricing](https://contentstudio.io/pricing)).
- Reviewers can comment and approve from a shareable link with no login ([search excerpt of the frill.co announcements](https://contentstudio.frill.co/announcements)).
- Approval Workflows API and an "approved" webhook (Aug/Sep 2026) ([announcements](https://contentstudio.frill.co/announcements)).
- White label $50/mo, reseller $50/mo, SSO $150/mo ([pricing](https://contentstudio.io/pricing)).

### 1.9 Integrations and API
- Canva, VistaCreate, PostNitro, Google Drive, Dropbox, Bitly, Replug, Feedly, Zapier, Make, n8n ([integrations](https://contentstudio.io/integrations)).
- AI agents: Claude, ChatGPT, Gemini, Cursor, Codex and others; MCP server; CLI ([integrations](https://contentstudio.io/integrations)).
- No WordPress, Medium or Mailchimp integration listed ([integrations](https://contentstudio.io/integrations)).
- The OpenAPI spec has "177 endpoints covering posts, scheduling, workspaces, social accounts, inbox, and per-network analytics" ([llms.txt](https://contentstudio.io/llms.txt)).
- Inbox API (5 Aug 2026) and public webhooks (21 Aug 2026) ([announcements](https://contentstudio.frill.co/announcements)).

### 1.10 Pricing (from [pricing](https://contentstudio.io/pricing))

| Plan | Billed annually | Month-to-month | Workspaces | Social accounts | Users | AI text | AI images | AI video | Clipping | AI auto replies |
|---|---|---|---|---|---|---|---|---|---|---|
| Standard | $19/mo ($228/yr) | $29 | 1 | 5 | 1 | 25,000 words | 25 | 100 | 100 | none listed |
| Advanced | $49/mo ($588/yr) | $69 | 2 | 10 | 2 | 50,000 | 50 | 200 | 200 | 100 |
| Agency Unlimited | $99/mo ($1,188/yr) | $139 | Unlimited | 25 | Unlimited | 125,000 | 125 | 500 | 500 | 250 |
| Enterprise | Custom | – | Unlimited | Custom | Unlimited | Custom | | | | |
| API-centric | $15/mo ($180/yr) | not listed | 1 | 10 (up to 50) | 1 | | | | | |

- **Add-ons:**
  - Social listening $99/mo (the page also shows $150/mo in another place, so it is inconsistent).
  - On Advanced: extra account $5, extra user $10, extra workspace $10.
- **Trial:** 7-day free trial, no card required. There is no free plan.
- **Payment:** cards through Paddle; **PayPal UNVERIFIED** (not mentioned).
- The llms.txt mentions a Black Friday offer of "up to 50% off every plan" ([llms.txt](https://contentstudio.io/llms.txt)).

### 1.11 Onboarding
- Accounts are connected inside the app; the LinkedIn guide starts at Settings and uses a selection pop-up for profiles and pages ([docs](https://docs.contentstudio.io/article/423-connect-linkdin)).
- I found no step where a customer must create their own developer app.
- API keys exist only for customers who use the API, MCP or CLI ([integrations](https://contentstudio.io/integrations)).
- Whether white label requires the agency's own platform apps is **UNVERIFIED**.

### 1.12 Distinctive points for a newcomer
- Auto-replies with a **per-rule choice between sending automatically and saving drafts**, intent detection, help-doc grounding and per-plan AI auto-reply quotas.
- A $15/mo API-only plan; an agent-facing MCP, CLI and webhooks; blog publishing dropped in 2026.

---

## 2. Lately (Lately.ai)

**Status:** not acquired. I found no evidence of an acquisition or shutdown. The site was **down on 2026-11-18** (Cloudflare 1014, see "How this was checked").

- The archived homepage of 2026-09-22 promotes "Kately™", a chat-based "superintelligent social media agent", as **waitlist only**: "We're getting ready to launch an all new version of Lately" ([archived homepage](https://web.archive.org/web/20260922154112/https://lately.ai/)).
- A third-party tracker says the pricing was "last confirmed on Nov 10, 2026" ([Toolradar](https://toolradar.com/tools/lately/pricing)).

### 2.1 What it is for, and for whom
An AI "content creation assistant" that turns long-form content into many social posts, written in a "voice model" learned from your past performance. It can also schedule and publish them.

- **Markets:** marketing teams, social selling, employee advocacy, franchises and agencies. Its industry pages run from small business to Fortune 500 ([archived pricing, 2026-10-09](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing), [archived homepage](https://web.archive.org/web/20260922154112/https://lately.ai/)).
- **Claimed reach:** "Trusted to drive more revenue for 20,000+ businesses" ([archived homepage](https://web.archive.org/web/20260922154112/https://lately.ai/)).

### 2.2 Networks
"Lately publishes to LinkedIn personal and company pages, X personal and company pages, Facebook company pages, Instagram company pages and YouTube. Additional platforms coming soon!" ([archived pricing](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing))

- **YouTube:** limited to the top plan: "Additional scheduling and publishing to YouTube is available only on our Enterprisely plan" (same page; the FAQ still uses older plan names).
- **Not listed:** Threads, TikTok, Pinterest, Google Business Profile, Nextdoor, Bluesky, Mastodon, Reddit. They are absent from that statement.

### 2.3 Content creation and repurposing
- **Inputs:**
  - "Works with any longform text, audio and video... earned, owned or found media... podcasts, webinars, blogs and more!" ([archived pricing FAQ](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing)).
  - The homepage names "blog, newsletter, press release, white paper or e-book... podcasts or workshops, webinars and interviews" ([archived homepage](https://web.archive.org/web/20260922154112/https://lately.ai/)).
  - RSS feed integration on Growth ([archived pricing](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing)).
- **Outputs** ([archived pricing FAQ](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing)):
  - "From text, we'll generate dozens of social posts."
  - "From audio, we'll generate a transcript and dozens of social posts with matching audiograms."
  - "From video, we'll generate a transcript, and dozens of posts with matching video clips."
  - Also "Lately AI Original" posts that it writes from your source.
  - Video and audio clip captioning; AI-recommended hashtags and emojis.
- **Per-network versions:** "Results optimized for individual social platforms" ([archived pricing](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing)).
- **Brand voice:** a "Voice model" learned from your past posts: "Our AI constantly learns from your past social media posts and builds a writing model based on what is most engaging for your audience".
  - Messaging guides, keyword enforcement and "Audience Pre-Testing" ([archived product page](https://web.archive.org/web/20260925190103/https://www.lately.ai/product/social-media-content-writing-ai-app)).
  - Voice model fine-tuning, anti-keyword controls and back-catalog ingestion, by tier ([archived pricing](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing)).
- **Languages:** source content in German, Portuguese, Japanese, Italian, Spanish, French and Dutch ([archived pricing](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing)).
- **Drip over weeks:** "Lately will even schedule them at the optimal cadence, day and time" ([archived homepage](https://web.archive.org/web/20260922154112/https://lately.ai/)). This is AI-optimized scheduling, not a fixed drip template.
- **Curation:** an UpContent integration ([archived pricing FAQ](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing)). There is no native discovery feed.

### 2.4 Email
- Newsletters are an **input** only ([archived homepage](https://web.archive.org/web/20260922154112/https://lately.ai/)).
- No email output, no sending, and no email-service integration on the archived pricing and product pages.

### 2.5 Publishing and scheduling
- "Publish with Lately", a social media calendar, and "AI-optimized scheduling for ideal cadence, time and day of week" ([archived pricing](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing)).
- It can also send content to Hootsuite, Sprinklr or HubSpot for publishing, or export it as CSV (same page).
- Evergreen recycling: **UNVERIFIED**.

### 2.6 Engagement inbox
None found. No inbox or reply feature appears on the archived pricing, product, analytics or feature-guide pages ([feature guide](https://web.archive.org/web/20261011022623/https://www.lately.ai/resources/lately-ai-feature-guide)).

### 2.7 Analytics
- Engagement overview; top posts, channels and authors; keywords, hashtags and mentions; best times; competitor keyword analysis; campaign tracking ([archived pricing](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing)).
- Parent-child dashboards across regions, brands and employees; word-cloud recommendations; Excel export ([archived analytics page](https://web.archive.org/web/20261010210447/https://www.lately.ai/product/social-media-content-analytics-software)).

### 2.8 Collaboration and agencies
- **Approvals:** "Limited" on Growth, full on Enterprise.
- **Enterprise only:** "Multiple client account governance", "Employee advocacy functionality" and "100% automation" ([archived pricing](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing)).
- **Analytics page:** "Approval Workflows — Review and approve every post before you schedule it" ([archived analytics page](https://web.archive.org/web/20261010210447/https://www.lately.ai/product/social-media-content-analytics-software)).

### 2.9 Integrations and API
- Hootsuite, Hootsuite Amplify, HubSpot Marketing Hub, Sprinklr, Salesforce (partners nav), UpContent, Grammarly, Bitly ([archived pricing](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing), [archived homepage](https://web.archive.org/web/20260922154112/https://lately.ai/)).
- Public API: **UNVERIFIED**.
- Canva, Zapier, WordPress, Mailchimp: not mentioned.

### 2.10 Pricing
Last vendor snapshot found: 2026-10-09 ([archive](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing)), identical to the 2026-09-25 snapshot ([archive](https://web.archive.org/web/20260925190053/https://www.lately.ai/pricing)).

| Plan | Annual billing | Monthly billing | Limits |
|---|---|---|---|
| Starter | $14/mo | $19/mo | 1 voice model, 5 social channels, 1 user; "TRY NOW FOR FREE" |
| Growth | $199/mo | $239/mo | 1 voice model; channels and seats shown inconsistently (one block says 25 channels / 5 seats, another 5 channels / 3 seats; the comparison table says 25 channels) |
| Enterprise | Custom | Custom | Unlimited voice models, channels and seats |

- "Save 20% with annual billing" (same page).
- Nonprofit pricing exists, for annual contracts only (same page).
- **Third-party note:** Toolradar says "Starter removed as of 2026-11-03" and lists Growth at $239/mo ([Toolradar](https://toolradar.com/tools/lately/pricing)). I could not confirm this on the live site because it was down.
- A free trial is offered on Starter. Its length is **UNVERIFIED**.
- **PayPal UNVERIFIED.**

### 2.11 Onboarding
**UNVERIFIED** because the help centre was unreachable.

- The archived nav links a "Connect Instagram to Lately" guide.
- Nothing on the archived pages says a customer must create a developer app.
- Bitly: "Already have your own Bit.ly account? We'll be glad to connect it" ([archived pricing](https://web.archive.org/web/20261009003637/https://www.lately.ai/pricing)).

### 2.12 Distinctive points
- The original "atomize long-form into dozens of posts in a learned voice" product, with audio and video clip generation.
- It positions as an add-on *to* Hootsuite, HubSpot and Sprinklr as well as a publisher.
- Its price jumps from $19 to $239/mo between tiers; it has no inbox.
- It is pivoting to a chat agent ("Kately") that is still on a waitlist.
- Its live site was broken on the day of checking.

---

## 3. Missinglettr

### 3.1 What it is for, and for whom
It turns each blog post (or YouTube video) into a pre-written **drip campaign** of social posts spread over a year. "Curate" is a content-sharing network that also promotes your content to other users. The target is bloggers, content marketers and agencies ("Join 30,000+ passionate creators and content marketers") ([home](https://missinglettr.com/), [pricing](https://missinglettr.com/pricing/)).

### 3.2 Networks
**The vendor's help article** says: "At this time, we currently support scheduling on the following social media platforms" ([help](https://help.missinglettr.com/social-media-general/which-social-media-accounts-can-i-connect)):

- Facebook (Pages and groups)
- Twitter
- LinkedIn (profiles and pages)
- Instagram (business accounts only)
- Google My Business

**Bluesky and Mastodon** are also supported, per dedicated help articles and the in-app connection dialog ([help index](https://help.missinglettr.com/), [Mastodon article](https://help.missinglettr.com/mastodon/i-can-t-connect-my-mastodon-account), [drip page](https://missinglettr.com/features/drip-campaigns/)).

**Not listed:** Threads, TikTok, Pinterest, Nextdoor, Reddit.

- **YouTube** is a content *source*, not a destination ("Drip Campaigns for YouTube and Wistia") ([pricing](https://missinglettr.com/pricing/)).
- **Medium** is no longer connectable ("I used to be able to connect my Medium account. What changed?") ([help index](https://help.missinglettr.com/)).
- **Third-party disagreement:** a review lists Pinterest ([inspiretothrive](https://inspiretothrive.com/missinglettr-review/)). The vendor does not.

### 3.3 Content creation and repurposing
- **Inputs:**
  - A blog connected by URL; RSS is the easiest route ([help](https://help.missinglettr.com/getting-started/what-is-a-missinglettr-campaign)).
  - Any platform with an RSS feed, e.g. "WordPress, Blogger, Squarespace" ([help](https://help.missinglettr.com/faq/which-blogging-platforms-does-missinglettr-work-with)).
  - "native company blog, Medium, or even Youtube channel" ([drip page](https://missinglettr.com/features/drip-campaigns/)).
  - YouTube and Wistia drip campaigns ([pricing](https://missinglettr.com/pricing/)).
  - With AI on, "paste a URL or type a topic" ([AI help](https://help.missinglettr.com/getting-started/ai-features-in-missing-lettr)).
- **Default output:** "a sequence of 9 unique social media posts (text+image+link)" whose "default schedule spreads these over a year". You can add or remove posts and change frequency or timeframe ([help](https://help.missinglettr.com/getting-started/what-is-a-missinglettr-campaign)).
- **Content generation:**
  - Text: "engaging snippets from your article."
  - Images: extracted from the post, or branded "quote bubbles" ([help](https://help.missinglettr.com/getting-started/what-is-a-missinglettr-campaign)).
  - Trending-hashtag matching ([drip page](https://missinglettr.com/features/drip-campaigns/)).
- **Three AI levels** ([AI help](https://help.missinglettr.com/getting-started/ai-features-in-missing-lettr)):
  - **Full AI:** AI "writes as many posts as needed for the campaign, spread across a phase strategy that matches your goal" (e.g. "awareness → education → authority"). Each post gets an AI caption, picked media and 2–3 hashtags. Goals: "We pick", "Stay consistently visible", "Build my reputation", "Get more customers" ([goals](https://help.missinglettr.com/ai-settings/we-pick-how-do-we-choose-the-right-goal)).
  - **Balanced:** "We pull real sentences from your blog post: no AI rewriting, no invented copy."
  - **No AI:** rules-based sentence extraction; a URL is required.
- **AI quotas by plan:** 100 or 500 "AI Posts", "AI Articles" and "AI Image" generations ([pricing](https://missinglettr.com/pricing/)). What "AI Articles" produces is **UNVERIFIED**; no help article describes it.
- **Per-network versions:** a "Character limits for different platforms" article exists ([help index](https://help.missinglettr.com/)). Separately AI-written versions per network are **UNVERIFIED**.
- **Brand voice:** "Brand focused... mimics your brand's voice, tone, personality, and messaging" ([more features](https://missinglettr.com/features/more/)). Also custom fonts and post templates ([pricing](https://missinglettr.com/pricing/)).
- **Curation:** "Curate" offers "a list of high-quality content that your industry audience is looking for" ([Curate](https://missinglettr.com/features/curate/)).
  - "Curate Auto-Schedule" "will automatically schedule content from other Curate users in your Calendar". Delay, maximum count, profiles and topics are configurable ([help](https://help.missinglettr.com/curate/curate-auto-schedule)).

### 3.4 Email
None. No email or newsletter output, sending or integration. The only email item is Missinglettr's own marketing "Insane Traffic Email Course" ([home](https://missinglettr.com/), [pricing](https://missinglettr.com/pricing/)).

### 3.5 Publishing and scheduling
- Calendar; "Scheduling Rules"; schedule templates; dates and times per campaign ([home](https://missinglettr.com/), [help index](https://help.missinglettr.com/)).
- Direct post scheduling and video posts ([pricing](https://missinglettr.com/pricing/)).
- "Automatic Repost" finds best performers and best times and duplicates them ([more features](https://missinglettr.com/features/more/)).
- Agency: 10,000 scheduled posts ([pricing](https://missinglettr.com/pricing/)).
- RSS-driven campaign creation ([help](https://help.missinglettr.com/faq/which-blogging-platforms-does-missinglettr-work-with)).

### 3.6 Engagement inbox
None on vendor pages. A competitor states Missinglettr has no engagement inbox (third-party, [RecurPost](https://recurpost.com/missinglettr-alternative)).

### 3.7 Analytics
- Campaign analytics and "Advanced Analytics" ([home](https://missinglettr.com/), [pricing](https://missinglettr.com/pricing/)).
- Click tracking through shortened URLs; "clicks vs. impressions" ([help index](https://help.missinglettr.com/)).

### 3.8 Collaboration and agencies
- **Approval is the core guardrail:** "Nothing is ever published without your approval" ([help](https://help.missinglettr.com/getting-started/what-is-a-missinglettr-campaign)). A pending Curate post shows "a yellow dot" in the calendar ([help](https://help.missinglettr.com/curate/curate-auto-schedule)).
- Team members with permissions ([help index](https://help.missinglettr.com/)).
- Agency add-on: unlimited workspaces and users; clients "collaborate and review their social performance, within a whitelabeled interface"; custom domain, dashboard and reports ([pricing](https://missinglettr.com/pricing/)).

### 3.9 Integrations and API
- Bitly, Replug, PixelMe, Epictions ([help index](https://help.missinglettr.com/)).
- Zapier, Canva, WordPress plugin, Mailchimp, public API: **UNVERIFIED**. None appears on vendor pages read.

### 3.10 Pricing ([pricing](https://missinglettr.com/pricing/), read in a browser)

| Plan | Monthly | Yearly (first 2 months free) | Limits |
|---|---|---|---|
| Solo | $15 | $150 | 1 workspace, 1 extra user, 3 social profiles, 100 AI posts / 100 AI articles / 100 AI images |
| Pro | $39 | $390 | 3 workspaces, 10 profiles, 500 / 500 / 500 |
| Agency | $147 ("starting from") | $1,415 | Unlimited workspaces and users, 25 profiles + discounted add-ons, 10,000 scheduled posts, unlimited Curate posts; the plan card shows "10 AI Posts Generations" |
| Enterprise | Demo | – | Custom |

- **Trial:** every plan has a free trial, "No credit card required", cancel anytime.
- **PayPal UNVERIFIED.**

### 3.11 Onboarding
- OAuth-style connections for the major networks (help articles on re-authentication) ([help index](https://help.missinglettr.com/)).
- **Bluesky:** the customer supplies a handle and an **App Password** ([help index](https://help.missinglettr.com/), [drip page](https://missinglettr.com/features/drip-campaigns/)).
- **Self-hosted Mastodon:** the customer may have to "create a new app" on their server and send Missinglettr the Client key and Client secret by email ([help](https://help.missinglettr.com/mastodon/i-can-t-connect-my-mastodon-account)).
- **AI provider:** the AI switch "is forced off" if "your workspace has no AI provider connected" ([AI help](https://help.missinglettr.com/getting-started/ai-features-in-missing-lettr)). Whether customers must bring their own AI key is **UNVERIFIED**; the pricing page includes AI generations.

### 3.12 Distinctive points
- The one-post-to-year-long-drip model with mandatory approval.
- A **"Balanced" mode that uses only real sentences from the source**, plus a no-AI mode.
- A cross-customer Curate sharing network.
- No inbox, no TikTok, Threads or Pinterest, no email.

---

## 4. FeedHive

### 4.1 What it is for, and for whom
"Social media management for AI agents" ([home](https://www.feedhive.com/)). It is "for creators, founders, agencies, and teams" to "create, schedule, publish, recycle, and analyze social content" ([llms.txt](https://www.feedhive.com/llms.txt)). It is built around an AI writing agent ("Vibe Marketing"), automation workflows, and an API, MCP and CLI for external agents ([home](https://www.feedhive.com/)).

### 4.2 Networks
"X/Twitter, LinkedIn, Facebook, Instagram, Threads, TikTok, Pinterest, YouTube, Google Business Profile, and Discord", with "availability [that] may vary by feature and provider API rules" ([llms.txt](https://www.feedhive.com/llms.txt), [AI page](https://www.feedhive.com/features/ai)).

- Facebook Groups have their own doc ([docs](https://docs.feedhive.com/)).
- **Not listed by the vendor:** Bluesky, Mastodon, Reddit, Nextdoor. Third-party pages disagree on Bluesky and Mastodon ([SocialKit, says not listed](https://socialk.it/en/vs/feedhive)).
- **LinkedIn profile vs page: UNVERIFIED.**

### 4.3 Content creation and repurposing
- **Inputs:** a rough idea or prompt for the built-in agent ([home](https://www.feedhive.com/)). Workflow Trigger URLs can feed content from Zapier, Make, n8n, forms or a backend ([triggers](https://docs.feedhive.com/triggers/introduction)).
  - No URL-, video- or podcast-to-posts feature is described on vendor pages ([AI page](https://www.feedhive.com/features/ai)).
  - A third-party review says it does not clip long-form video or produce carousels, blogs or newsletters ([Kompozy](https://kompozy.io/ai-tools/feedhive)).
- **Outputs:** it turns one idea into "captions, images and hashtags for every platform you publish to" ([AI page](https://www.feedhive.com/features/ai)).
  - "IG Carousels" and "Thread Builder" are publishing features without AI detail (same page).
  - Images come from Flux Pro and Nano Banana 2 (same page).
- **Per-network versions:** yes; one post becomes "channel-ready versions" ([home](https://www.feedhive.com/), [AI page](https://www.feedhive.com/features/ai)).
- **Brand voice:** "Tell FeedHive about your voice, audience and goals once" ([AI page](https://www.feedhive.com/features/ai)).
- **Scoring:** performance prediction and a Content Score per draft ([AI page](https://www.feedhive.com/features/ai)).
- **Model choice:** you can pick the model, "from GPT-5.1 to GPT-4.1 Mini" (same page).
- **Recycling:** suggestions only. "We don't believe in reposting the same content on repeat"; there are no evergreen posts ([docs](https://docs.feedhive.com/planning-and-scheduling/recycling-content)).
- Drip campaigns and curation: none found.

### 4.4 Email
None on any vendor page read. Beehiiv appears only as a customer logo ([home](https://www.feedhive.com/), [llms.txt](https://www.feedhive.com/llms.txt)).

### 4.5 Publishing and scheduling
- Posting plans (time slots), calendar, CSV upload, best time slots ([docs](https://docs.feedhive.com/), [home](https://www.feedhive.com/)).
- **Post Conditions:** a follow-up comment or thread posts automatically once the main post hits a threshold. Thresholds include "Above average", delay, likes, retweets, comments, impressions and engagement rate. Platforms: Twitter, Facebook and Instagram ([docs](https://docs.feedhive.com/planning-and-scheduling/how-to-use-post-conditions)).
- **Workflows** can run Trigger URL → Create Draft → Add Socials → **Publish**, so content "goes out in the same run" with no review step ([triggers](https://docs.feedhive.com/triggers/introduction)).
- RSS: no native RSS trigger is documented. It runs through Zapier, Make or n8n into a Trigger URL ([triggers](https://docs.feedhive.com/triggers/introduction)).

### 4.6 Engagement inbox and replies
- "Social Inbox for monitoring and replying to comments and conversations" ([llms.txt](https://www.feedhive.com/llms.txt)).
- AI-assisted replies: "Draft on-brand replies faster"; saved replies; ownership hand-off ([social inbox](https://feedhive.com/features/social-inbox)).
- The networks that feed the inbox are not named: **UNVERIFIED**.
- No auto-reply or keyword-trigger feature is documented (same page; [docs index](https://docs.feedhive.com/)).
- **Agent guardrail:** "Agent-created posts are saved as drafts for your approval" ([AI page](https://www.feedhive.com/features/ai)).

### 4.7 Analytics
- Performance stats, engagement prediction, Content Score, best time slots ([home](https://www.feedhive.com/), [docs](https://docs.feedhive.com/)).
- An Analytics section exists in the REST API ([docs](https://docs.feedhive.com/)).

### 4.8 Collaboration and agencies
- Workspaces and team members: 5 per workspace on Brand, 20 on Business and Agency ([pricing](https://www.feedhive.com/pricing)).
- **Approval workflow:**
  - Scheduling by a non-approver puts the post into "needing approval".
  - Only the owner grants "Can approve".
  - A non-approver's edit sends an approved post back to draft.
  - No client-approval role is documented ([docs](https://docs.feedhive.com/workspace-management/approval-workflow-for-feedhive-teams)).
- White label: not mentioned.

### 4.9 Integrations and API
- Zapier, Make, n8n (as trigger senders), webhooks ("notifications"), REST API, MCP server, CLI ([llms.txt](https://www.feedhive.com/llms.txt), [docs](https://docs.feedhive.com/)).
- Agent guides: ChatGPT, Claude, Claude Code, OpenClaw, Hermes, Grok, Manus, Muse, Dots ([docs](https://docs.feedhive.com/)).
- LinkDrip, FeedHive's own link shortener ([home](https://www.feedhive.com/)).
- Canva, WordPress, Mailchimp: not mentioned.

### 4.10 Pricing ([pricing](https://www.feedhive.com/pricing), billing toggle read in a browser)

| Plan | Monthly | Yearly | Accounts | Workspaces | Team / workspace | Scheduled posts | AI credits/mo | Automation runs/mo |
|---|---|---|---|---|---|---|---|---|
| Creator | $19 | $15/mo ($180/yr) | 4 | not listed | not listed | 30 (14-day horizon) | 2,500 | 500 |
| Brand | $29 | $22/mo ($264/yr) | 10 | 5 | 5 | 500 (60-day horizon) | 10,000 | 2,000 |
| Business | $99 | $69/mo ($828/yr) | 100 | 50 | 20 | Unlimited | 50,000 | 10,000 |
| Agency | $299 | $239/mo ($2,868/yr) | 500 | 100 | 20 | Unlimited | 100,000 | 100,000 |

- **Trial and promo:** 7-day free trial, plus a "Limited launch bonus: Double your AI credits for 3 months".
- **Credit costs:** an AI post uses 8–15 credits and an AI image 20–30 ([AI page](https://www.feedhive.com/features/ai)).
- No free plan. **PayPal UNVERIFIED.**

### 4.11 Onboarding
- **Instagram:** Facebook login, with a Business account linked to a Page; no developer app ([docs](https://docs.feedhive.com/connection-setup/setting-up-instagram-on-feedhive)).
- **Discord:** the customer creates a **webhook URL in Discord** and pastes it in ([docs](https://docs.feedhive.com/connection-setup/setting-up-discord-on-feedhive)).
- **Optional own AI keys:** customers can add their **own OpenAI API key** "to bypass FeedHive's AI credit limit entirely" ([docs](https://docs.feedhive.com/content-creation/adding-your-openai-api-key)), and a Replicate key ([docs](https://docs.feedhive.com/)).

### 4.12 Distinctive points
- Agent-first positioning: MCP, CLI, workflows, and agent drafts held for approval.
- Engagement-threshold "post conditions".
- An explicit anti-evergreen stance; bring-your-own OpenAI key.
- No email, no Bluesky or Mastodon listed.

---

## 5. Ocoya

**Status:** still operating. It was re-packaged during 2026:

- Former tiers were Bronze, Silver, Gold and Diamond ([PricingSaaS](https://pricingsaas.com/companies/ocoya)).
- The current tiers are Starter, Team and Agency, credit-based. A tracker logged 25 → 28 integrations on Oct 26, 2026 ([PricingSaaS W44](https://pricingsaas.com/companies/ocoya/diffs/2026W44)).

### 5.1 What it is for, and for whom
A marketing platform "for social media, SEO, and (soon) email": AI social posts, SEO articles published to CMSs, a design studio, campaigns, an inbox and workflows. It is "Made for creators & teams". Tiers target creators and small brands, teams with a few brands, and agencies ([home](https://www.ocoya.com/), [pricing](https://www.ocoya.com/pricing)).

### 5.2 Networks
**Social:** Facebook (Pages: "Posts to your Pages"), Instagram, X, LinkedIn ("Profiles and Pages"), Pinterest, Bluesky, Threads, YouTube Shorts, Google Business, TikTok, Mastodon, Dribbble ([integrations](https://www.ocoya.com/integrations)).

**Other destinations:** Telegram, Slack, Discord, Whop, Tumblr, WordPress, DEV.to, Notion, Airtable, Webflow, Framer, Wix, Twitch, Kick, and a custom endpoint. **Email is "Coming soon"** (same page).

- Not listed: Nextdoor, Reddit, Facebook Groups, regular (non-Shorts) YouTube.

### 5.3 Content creation and repurposing
- **Campaigns** ([campaigns](https://www.ocoya.com/features/campaigns)):
  - **Social campaign:** input is "A goal, an audience and a stretch of calendar". Output is "Up to 90 short posts, each sized for its network", over up to 90 days, at every 2 days up to 4 posts a day.
  - **Blog campaign:** "1 to 20 full articles, 600 to 3,000 words each."
  - Email campaign: coming soon.
- **Repurposing an article into posts:** Workflows trigger on a new RSS item or a new Notion, Airtable, Webflow, Framer or Wix Blog item, then generate text and images and publish to several networks ([workflows](https://www.ocoya.com/features/workflows)).
  - Video generation is "Coming soon".
  - There is no paste-a-URL repurposing feature (same page).
- **Products into posts:** products imported under Brand → Products feed a "Pick a product" workflow step ([workflows](https://www.ocoya.com/features/workflows)). Shopify and WooCommerce are not named ([brand](https://www.ocoya.com/features/brand)).
- **Per-network versions:** "a version per network, without touching the rest", fitted to each limit ([social](https://www.ocoya.com/features/social)).
- **Images and design:** "Generate images, carousels and short video that match your brand" ([home](https://www.ocoya.com/)). The Design Studio makes on-brand images ([social](https://www.ocoya.com/features/social)).
- **Brand voice:** "Fetch brand reads your website and lifts out your name, description, logos and colors"; "Pick up to three tones and add rules of your own" (up to 2,000 characters) ([brand](https://www.ocoya.com/features/brand)).
- Curation and discovery: none found.

### 5.4 Email
**Not live. Planned inside the same product** ([email](https://www.ocoya.com/features/email)):

- "One campaign becomes a run of on-brand emails, each set for its day and written in your voice."
- Emails go on the same calendar as posts and articles.
- "Every email arrives as a draft first."
- The sending mechanism, lists and subscribers are not described. No Mailchimp or ESP integration is named.
- Email replies in the inbox are "soon", and a workflow "Send email" step is "Coming soon" ([engage](https://www.ocoya.com/features/engage), [workflows](https://www.ocoya.com/features/workflows)).

### 5.5 Publishing and scheduling
- One calendar for posts, articles and (soon) emails; List, Board and Calendar views; drag to schedule ([publish](https://www.ocoya.com/features/publish)).
- Weekly slots per channel; best-time suggestions are "A suggestion, never an automatic reschedule" (same page).
- Bulk queueing, "Up to a thousand posts a minute"; "Auto-post your blog from RSS" (same page).
- Evergreen recycling: not mentioned.

### 5.6 Engagement inbox and replies ([engage](https://www.ocoya.com/features/engage))
- **Coverage:** Facebook and Instagram DMs, comments and mentions; Google Business reviews (the reply "posts publicly under the review"); comments on your SEO articles. Other networks do not feed the inbox.
- **AI reply drafts:** produced on request, reading the last four DM messages. "Nothing is sent on its own." Each costs 1 credit.
- **Automatic replies are possible through Workflows:** a new DM, comment, mention, review or follower can trigger a reply, a DM, hiding or deleting spam, or a follow-back on Bluesky or Mastodon ([workflows](https://www.ocoya.com/features/workflows)).
  - **Guardrails:** Filter and "AI filter" steps ("Continues only when AI rates the text as negative, spam or what you describe"), scoping to one account or feed, test runs, errors that block activation, and immediate pause.
  - A new workflow "is a draft until you set it live" ([workflows](https://www.ocoya.com/features/workflows)).
  - An automated reply leaves the thread Open until a person resolves it ([engage](https://www.ocoya.com/features/engage)).
- Assignment ("a marker, not a lock"), internal notes and per-brand snippets ([engage](https://www.ocoya.com/features/engage)).

### 5.7 Analytics
- Best-times chart (last 90 days), bio-link clicks, UTM tagging ([social](https://www.ocoya.com/features/social)).
- "Track post performance and article traffic" ([home](https://www.ocoya.com/)).
- A full post-analytics dashboard is not described: **UNVERIFIED** depth.

### 5.8 Collaboration and agencies
- "Approvals are on every plan." Held posts never publish, even after their time passes ([publish](https://www.ocoya.com/features/publish)).
- A Client role can approve, decline and comment but not create or edit. There is a public read-only calendar link (same page).
- Campaigns arrive as a plan: "Only approved posts get scheduled" ([campaigns](https://www.ocoya.com/features/campaigns)).
- Unlimited brands on every plan; "nothing leaks between them" ([brand](https://www.ocoya.com/features/brand)). Channels and credits are pooled across brands ([pricing](https://www.ocoya.com/pricing)).

### 5.9 Integrations and API
- REST API, MCP and n8n ([home](https://www.ocoya.com/)).
- Not listed: Zapier, Make, Canva, Shopify, WooCommerce, Mailchimp ([integrations](https://www.ocoya.com/integrations)).

### 5.10 Pricing ([pricing](https://www.ocoya.com/pricing))

| Plan | Monthly | Yearly (2 months free) | Channels | Credits/mo |
|---|---|---|---|---|
| Starter | $29 | $290 | 5 | 300 |
| Team | $79 | $790 | 20 | 1,500 |
| Agency | $199 | $1,990 | 100 | 5,000 |
| Enterprise | Contact ([PricingSaaS](https://pricingsaas.com/companies/ocoya/diffs/2026W44)) | | | |

- **Credit costs:** a post without images costs 1 credit; each image costs 5; a blog campaign costs 1 plus 2 per article ([campaigns](https://www.ocoya.com/features/campaigns)).
- **Trial:** "7 days free, card required". No free plan. Prices exclude VAT.
- **PayPal UNVERIFIED.**

### 5.11 Onboarding
**UNVERIFIED.** No vendor page read requires a customer developer app. The API, MCP and "Custom endpoint" are optional ([integrations](https://www.ocoya.com/integrations)).

### 5.12 Distinctive points
- One calendar for social, SEO articles and (planned) email.
- **AI inbox drafts never auto-send, but separate workflows can auto-reply** behind an AI filter.
- Approvals with a Client role on every plan; unlimited brands with pooled credits.
- The broadest network list in this group, including Bluesky and Mastodon.

---

## 6. Predis.ai

**Status:** operating. Its homepage now positions it primarily as an **AI ad generator** ("Built for anyone who has to keep the ads coming"), with organic social as a secondary use ([home](https://predis.ai/)).

### 6.1 What it is for, and for whom
- **Main use:** it generates video ads, UGC-avatar ads and image ads from a product link, prompt or image. The same brand kit "also makes social posts, carousels, and reels for your organic accounts" ([features](https://predis.ai/features/)).
- **Other features:** scheduling, auto-posting, competitor intelligence and client workspaces ([home](https://predis.ai/)).
- **Target:** creators and coaches, e-commerce stores, agencies ([home](https://predis.ai/)).

### 6.2 Networks
- **Pricing page:** Facebook, Instagram, Facebook Groups, Instagram Stories, TikTok, Google Business Profile, LinkedIn (personal and business page), YouTube Shorts, Twitter. **The pricing channel list does not include Pinterest** ([pricing](https://predis.ai/pricing/)).
- **Scheduling page:** Instagram, Facebook, TikTok, YouTube Shorts, LinkedIn, **Pinterest**, Google Business, X ([scheduler](https://predis.ai/social-media-content-scheduling-tool/)). So Pinterest is listed inconsistently across vendor pages.
- **Not listed:** Threads, Bluesky, Mastodon, Reddit, Nextdoor.

### 6.3 Content creation and repurposing
- **Product URL → ad:** "Paste a product URL and Predis.ai writes the script, picks the shots, adds a voiceover, and cuts the video" ([features](https://predis.ai/features/)).
- **Blog URL → posts:** paste a blog URL; the AI proposes variants "based on the hierarchical structure of headings in the blog"; you choose "videos or carousel posts" ([blog-to-posts](https://predis.ai/blog-to-posts/)).
- **Blog → video:** the blog becomes a script with scenes, text overlays and visuals ([blog-to-video](https://predis.ai/blog-to-video/)).
- **Shorts maker:** input is a text idea, "a product or blog link", or your clips. Output is a 9:16 1080p Short with script, voiceover, captions and music, plus titles and hashtags in bulk. Dubbing into 19 languages ([Shorts maker](https://predis.ai/use-cases/youtube-shorts-maker/)).
- A long-YouTube-video-to-clips feature is not described there.
- **Per-network versions:** "Sized for every placement" ([features](https://predis.ai/features/)). "Each platform gets its own format" ([scheduler](https://predis.ai/social-media-content-scheduling-tool/)).
- **Brand kit:** "your logo, colors, fonts, and tone" ([features](https://predis.ai/features/)).
- **Competitor analysis:** 60, 130 or 600 runs a month by plan ([pricing](https://predis.ai/pricing/)); "Ad Competitor Intelligence" ([home](https://predis.ai/)).
- Drip campaigns: none. Content curation: none found.

### 6.4 Email
None on any vendor page read ([features](https://predis.ai/features/), [pricing](https://predis.ai/pricing/)).

### 6.5 Publishing and scheduling
- Calendar; "Bulk & Batch Scheduling" ([scheduler](https://predis.ai/social-media-content-scheduling-tool/)).
- **Auto-post:** Predis creates "a week of posts, captions, images, and reels" from brand inputs, and content "publishes on schedule, every day, without you logging in". Review is optional ([auto-post](https://predis.ai/auto-post/)).
- Auto-posting is not on Core; Rise "Auto-publishes up to 60 posts / month" ([pricing](https://predis.ai/pricing/), [auto-post](https://predis.ai/auto-post/)).
- RSS and evergreen: not mentioned ([scheduler](https://predis.ai/social-media-content-scheduling-tool/)).

### 6.6 Engagement inbox
None found on any vendor page read ([features](https://predis.ai/features/), [AI social media manager](https://predis.ai/ai-social-media-manager/), [scheduler](https://predis.ai/social-media-content-scheduling-tool/)).

### 6.7 Analytics
- "Insights show which of your posts are doing best" ([AI social media manager](https://predis.ai/ai-social-media-manager/)).
- Ad insights on "which formats and hooks are carrying them" ([features](https://predis.ai/features/)).
- Competitor themes, hashtags and schedules ([AI social media manager](https://predis.ai/ai-social-media-manager/)).

### 6.8 Collaboration and agencies
- **Approval flow:** "Send for Review" by email to a reviewer; the reviewer approves or rejects with a comment; on approval the post "is scheduled automatically"; history is kept ([approval flow](https://predis.ai/use-cases/approval-flow/)).
- Workspaces per brand or client ([features](https://predis.ai/features/)).
- Unlimited team members on all plans ([pricing](https://predis.ai/pricing/)).
- White label: not mentioned.

### 6.9 Integrations and API
- Shopify ([home](https://predis.ai/)).
- The API ("video ads, UGC videos, image ads, and social posts") is on all plans ([features](https://predis.ai/features/), [pricing](https://predis.ai/pricing/)). There are developer docs and separate API pricing (linked from the homepage: [developer docs](https://predis.ai/developers/docs/intro/), [API pricing](https://predis.ai/developers/docs/predis-api/api-pricing)).
- MCP for Claude and ChatGPT ([home](https://predis.ai/), [MCP](https://predis.ai/mcp/)).
- Not mentioned: Canva, Zapier, WooCommerce, WordPress, Mailchimp.

### 6.10 Pricing ([pricing](https://predis.ai/pricing/), Black Friday promotion active)

| Plan | Annual price shown (≈ per month) | Struck-through price shown | Credits/mo | Brands | Channels | Competitor runs | Auto posting |
|---|---|---|---|---|---|---|---|
| Core | $230/yr (≈ $19) | $32 | 1,300 (≈ 325 images or 33 videos) | 1 | 10 | 60 | No |
| Rise | $474/yr (≈ $40) | $79 | 3,200 | up to 4 | 20 | 130 | Yes |
| Enterprise+ | $2,540/yr (≈ $212) | $249 | 10,000 | Unlimited | 60 | 600 | Yes |

- **Discounts:** the page ties 40%, 50% and 15% discounts to a Black Friday annual promotion. **The regular month-to-month prices are UNVERIFIED**; the struck-through figures ($32, $79, $249) may be them.
- **Add-ons:** extra brand $5; channels from $5; extra credits $29.
- **Trial:** "Every plan starts with 3 days free". A card is charged at the trial's end, and payments after the trial are non-refundable.
- **PayPal UNVERIFIED.**

### 6.11 Onboarding
**UNVERIFIED.** No vendor page read requires a customer developer app. The API is optional.

### 6.12 Distinctive points
- An ad-first creative generator that also publishes.
- **Fully automatic generate-and-publish ("without you logging in") with optional review.**
- Blog-to-carousel and blog-to-video repurposing.
- No inbox, no Threads or Bluesky.

---

## 7. Cross-check: SMM products that include email or newsletters

| Product | Email in the same product? | Social side | AI that crosses email and social | Source |
|---|---|---|---|---|
| **HubSpot** (Marketing Hub + Content Hub) | Yes. Marketing emails sit in the same platform as the social tool | Publish to Facebook, Instagram, LinkedIn, X, TikTok, YouTube (Reddit has separate guides); needs **Marketing Hub Professional or Enterprise**. Social inbox with AI quick replies and a beta sentiment feature | **Content Remix (Beta, updated Jul 20, 2026)**: up to 6 inputs (text, HubSpot content, documents, Google Drive, a URL, audio, video, images) into up to 6 assets: blog posts, **social posts**, images, SMS, ads, landing pages, **marketing emails**, podcast episodes, video clips. Content Hub Pro/Enterprise; social output also needs Marketing Hub Pro/Enterprise | [social publishing KB](https://knowledge.hubspot.com/social/create-and-publish-social-posts), [social inbox product page](https://www.hubspot.com/products/marketing/social-inbox), [inbox KB (search-result excerpt: "use AI quick replies")](https://knowledge.hubspot.com/social/use-inbox-streams), [Content Remix KB](https://knowledge.hubspot.com/blog/repurpose-content-using-ai-with-content-remix) |
| **Constant Contact** | Yes. An email-first suite; "Social posting and reporting are included in all plans" | Facebook, Instagram, LinkedIn, TikTok; scheduling (not on every plan); per-platform editing; social inbox for comments, mentions and messages; analytics; Canva | "Turn your emails into social content in a few clicks"; AI can "turn emails you've already written into ready-to-go social posts"; mobile web drafts 3 variations | [social tools page (read in a browser)](https://www.constantcontact.com/features/social-media-tools), [KB 33131, updated Oct 28, 2026](https://knowledgebase.constantcontact.com/email-digital-marketing/tutorials/KnowledgeBase/33131-Create-a-Social-Post-and-schedule-it-to-publish-on-social-media?lang=en_US) |
| **Mailchimp** | Yes. An email-first suite | Facebook Pages, Instagram business, X (the features page); the help article covers FB and IG only. Immediate publishing on all plans including Free; **scheduling Standard or higher**. No inbox mentioned | "AI tools can generate social media posts based on the content of your previous marketing campaigns"; can "automatically post to Facebook and Instagram when you send an email" | [organic social posting](https://mailchimp.com/features/organic-social-posting/), [help: create social post](https://mailchimp.com/help/create-social-post/) |
| **Zoho** (Social + Campaigns + Marketing Automation, bundled as Marketing Plus) | Bundle, not one app. **Zoho Campaigns removed its own social campaigns on May 10, 2023** (Facebook, LinkedIn, Tumblr, Twitter) and pointed users to Marketing Automation and a future Zoho Social integration | **Zoho Social:** free plan; Facebook, X, Instagram, LinkedIn profile and page, GBP; paid tiers add YouTube, Pinterest, TikTok, Mastodon, Threads, Bluesky, Snapchat; top tiers add WhatsApp and Telegram. Marketing Plus "Social" has an inbox for DMs and replies and approvals. Per a search-result summary of Zoho's help, Marketing Automation runs social campaigns (FB Pages, LinkedIn profile and page, Instagram, X) and can send them to Zoho Social brands | No official email-to-social AI found | [Campaigns change notice](https://help.zoho.com/portal/en/community/topic/changes-to-social-campaigns-in-zoho-campaigns?page=1), [Marketing Plus](https://www.zoho.com/marketingplus/), [Marketing Plus social help](https://help.zoho.com/portal/en/kb/marketing-plus/modules/articles/manage-social-media), [Zoho Social pricing](https://www.zoho.com/social/pricing.html), [Marketing Automation social campaigns (not fetched)](https://help.zoho.com/portal/en/kb/marketing-automation-2-0/user-guide/marketing-campaigns/social/articles/getting-started-with-social-campaigns) |
| **HighLevel** (agency and white-label CRM) | Yes. "Email + SMS Marketing" and "Social Media Management" are core features of one platform | Social Planner: Facebook, Instagram, LinkedIn, YouTube, Pinterest, TikTok, GBP, Threads, Bluesky (KB modified Nov 3, 2026) | Content AI listed; specifics **UNVERIFIED** | [Social Planner KB](https://help.gohighlevel.com/support/solutions/articles/155000004101-social-planner-statistics), [pricing](https://www.gohighlevel.com/pricing) |
| **Ocoya** | **Coming soon** in the same calendar: AI-drafted newsletters and campaigns, each email a draft first | See §5 | Campaign → email series (planned) | [email](https://www.ocoya.com/features/email) |

**Constant Contact pricing** (read in a browser), per month billed annually, and dependent on contact count: Lite $10.20, Standard $29.75, Premium $68 ("Save 15%"). The month-to-month equivalents are **UNVERIFIED** ([page](https://www.constantcontact.com/features/social-media-tools)).

**HighLevel pricing:** the page reads Starter $97/mo ($970/yr). The other tiers' monthly and annual figures came back scrambled from the page (Founder/Unlimited at $297 or $497/mo; $2,970 and $4,970/yr), so the mapping is **UNVERIFIED** ([pricing](https://www.gohighlevel.com/pricing)).

**Constant Contact Social:** the vendor page also promotes a separate "AI social tool that tells you what to post" ([page](https://www.constantcontact.com/features/social-media-tools)). Its details are **UNVERIFIED**.

**Not checked (search budget exhausted):** GoDaddy (Airo / Digital Marketing), Wix (Email Marketing + social posts), Vendasta, Klaviyo (social auto-replies), Brevo. Any claim about them would be a guess.

**Among the six tools:** none sends email or writes newsletters today.

- Ocoya has email "coming soon".
- ContentStudio emails only analytics reports.
- Lately accepts newsletters as *input*.
- Missinglettr, FeedHive and Predis have no email at all.

---

## 8. Observed patterns (facts drawn from the sections above)

1. **There are four repurposing patterns:**
   - (a) **Atomize long-form into many posts plus clips:** Lately; Missinglettr's 9-post year-long drip.
   - (b) **One idea or caption into per-network versions:** ContentStudio "Customize with AI", FeedHive, Ocoya, Constant Contact.
   - (c) **Long video into short clips:** ContentStudio Clip Maker, Lately video clips, HubSpot.
   - (d) **URL into visual formats:** Predis blog-to-carousel or video, and product-to-ad.

   RSS-triggered auto-posting is common (ContentStudio, Ocoya, Lately Growth, Missinglettr).
2. **Email is the gap among the AI repurposers.** Only the email-first suites (HubSpot, Constant Contact, Mailchimp) convert between email and social today, and they run in both directions:
   - Email → social: Constant Contact and Mailchimp.
   - Any source → email and social: HubSpot Content Remix.

   Ocoya is the only social-first tool here announcing native email.
3. **Nextdoor:** supported by none of the 6 tools, and absent from the HubSpot, Constant Contact, Mailchimp, Zoho Social and HighLevel network lists checked.
4. **Automation guardrails vary widely:**
   - **Mandatory human approval:** Missinglettr ("Nothing is ever published without your approval"); Ocoya campaigns ("Only approved posts get scheduled"); FeedHive agent posts saved as drafts.
   - **Inbox AI drafts that a person sends:** ContentStudio "Reply with AI", Ocoya ("Nothing is sent on its own"), FeedHive, HubSpot.
   - **Unattended sending is available:**
     - ContentStudio auto-reply "Send Automatically" (cannot be undone).
     - Ocoya workflows auto-reply to DMs, comments and reviews behind an AI filter and a pause.
     - FeedHive workflows "Create & Publish".
     - Predis auto-post ("without you logging in").
     - Lately Enterprise "100% automation".
5. **Onboarding:** none of the vendors documents customer-built developer apps for the major networks. The exceptions are:
   - a Bluesky app password (Missinglettr);
   - self-hosted Mastodon apps whose keys are emailed to support (Missinglettr);
   - a Discord webhook (FeedHive);
   - optional bring-your-own OpenAI or Replicate keys (FeedHive).
6. **Agent and API surfaces are now standard:** MCP, CLI or API at ContentStudio, FeedHive, Ocoya and Predis. ContentStudio sells a $15/mo API-only plan.
