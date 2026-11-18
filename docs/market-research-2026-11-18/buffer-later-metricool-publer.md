# SMM competitor research: Buffer, Later, Metricool, Publer

Checked 2026-11-18. Research was done on the live web: vendors' own pricing, feature, help-center, changelog and roadmap pages first, then reputable third-party pages, which are marked as third-party wherever they are used. **UNVERIFIED** means no source could confirm the point. Where two of a vendor's own pages disagree, both readings are given.

Method notes:
- **Later:** help-center article pages return HTTP 403 to automated fetches, so they were read through Later's public Zendesk Help Center API. The citations use the normal article URLs.
- **Publer:** per-account prices are drawn in the browser by the plans page's script. They were read from that script and cross-checked against one third-party calculator. The help center confirms only the starting prices.

## Cross-tool summary

Every cell below is a summary of the cited detail in that tool's section.

| Question | Buffer | Later | Metricool | Publer |
|---|---|---|---|---|
| **Nextdoor** | No | No | No | No |
| **Other gaps among the 15 named networks** | No Reddit, Tumblr or WhatsApp. YouTube is Shorts only. | No X (dropped 2025-08-28), Google Business Profile, Bluesky, Mastodon, Reddit, Tumblr or WhatsApp. YouTube is Shorts only. | No Mastodon, Reddit, Tumblr or WhatsApp. X is a paid add-on. | No Reddit, Tumblr or WhatsApp. X is on paid plans only. |
| **Inbox** | "Community": comments on 9 networks, X mentions and Google Business Profile reviews. **No DMs.** | "Social Inbox": beta and desktop only, on Growth and above. Instagram and Facebook DMs; Instagram, Facebook and TikTok comments. | Facebook and Instagram comments and DMs, TikTok comments, X DMs, LinkedIn comments and mentions, YouTube comments, Google Business Profile reviews. Included on Free. | **No unified inbox.** Comments are handled per post on 8 networks. The inbox, Kibo, is a standalone MVP to be folded in later. |
| **AI-suggested replies** | Yes. A person edits and sends each one. 5 a week on Free, unlimited on paid. Not for X. | None found | None found | Yes: AI Assist drafts comment replies. Business plan, or Free and Professional with the customer's own OpenAI key. |
| **Automatic replies** | None | None | Only through "Flows", a paid add-on for Instagram, TikTok and X. **No human approval step.** | None |
| **Review sites (read and reply)** | Google Business Profile only | None | Google Business Profile only | None |
| **Approval workflows** | Team plan, inside Buffer only, with no external client link. Drafts from members who need approval are held even when they come through the API or the MCP server. | Growth and above. Internal approval, plus no-login review links for outsiders that expire after 7 days. **Approval does not block publishing** unless a user's "Publish Without Approval" permission is removed. | Advanced and above. Internal approval, plus clients approving from an email link in a no-login portal. Rules can require no reviewer, any one, or all. **Any rejection blocks publishing.** | Business plan. Two tiers: Admin, then Client (clients are invited by email as members). **An unapproved post is marked Failed and does not publish.** |
| **Email features** | No email marketing. Notification emails and daily or weekly digests go to Buffer users. Substack Notes can be published. | No email marketing. Link-in-bio email capture (CSV export or Mailchimp). Scheduled report and alert emails to any address on Scale. Approval notification emails. | No email marketing. Client approval by email link. Automatic monthly report emails to several recipients with a reply-to address. Under white label, emails go out as the agency. | No email marketing. Notification emails. Weekly or monthly PDF reports by email. Email capture on its link-in-bio page (Linkie). |
| **Pricing model** | Per channel. Free covers 3 channels. Essentials $6 per channel monthly or $5 annual; Team $12 or $10. Volume tiers apply above 10 channels. | Per "Social Set" (one profile per platform), with post caps and AI credits. Annual: $18.75, $37.50, $82.50. Monthly: $25, $50, $110 (third-party figures). A Free plan exists but is not shown on the pricing page. | Per brand. Free covers 1 brand. Starter is $20–$36 annual or $25–$45 monthly; Advanced $53–$159 annual or $67–$210 monthly; Custom by quote. | Per social account plus per extra member. Professional starts at $5, then $4 per account. Business starts at $10, then $7 per account. Annual billing is 20% off. Free covers 3 accounts. |
| **Free trial** | 14 days | 14 days | No standard trial; promotional codes only | Professional 7 days, Business 14 days. Needs a card or PayPal. |
| **PayPal accepted** | **No** | **No** | **Yes** | **Yes** |
| **Customer creates a developer app or API keys?** | **No.** Accounts connect by signing in through each network. Bluesky needs an app password. | **No.** Accounts connect by in-app login. Partner API keys appear only in the enterprise Later Influence product. | **No.** Accounts connect through OAuth in the browser. | **No developer app.** Credentials are needed only for a Bluesky app password, a WordPress application password for self-hosted sites, and an optional own OpenAI key. |
| **Public API / MCP** | GraphQL API and MCP server on all plans, including Free | No public API for Later Social | API on Advanced and above. MCP server and Claude/ChatGPT connectors on all plans. | API and MCP server on Business and Enterprise |


---

## Buffer

Checked 2026-11-18. Sources are Buffer's own pages (buffer.com, support.buffer.com, developers.buffer.com, buffer.com/changelog) unless marked "third-party". Anything I could not confirm is marked **UNVERIFIED**. "Not documented" means Buffer's own pages that should mention it do not.

### 1. Supported networks

Buffer calls each connected account a "channel" and prices by channel. The current supported list is: Facebook Pages, Facebook Groups (notification only), Instagram Business/Creator, Instagram Personal (notification only), X/Twitter, LinkedIn Pages, LinkedIn profiles, Pinterest (business and personal), TikTok, Google Business Profile, Start Page, Mastodon, YouTube Shorts, Threads, Bluesky, and Substack (Notes only).
- Supported-channels table (Publishing / Insights / Basic Analytics / Community per channel): https://support.buffer.com/en-us/articles/supported-channels-LM3P7Y4zsp
- Connection guide. It lists the same channels and names the unsupported ones: "Snapchat, Reddit, WhatsApp, Telegram, Twitch, OnlyFans, lemon8", Discord, and Facebook personal profiles: https://support.buffer.com/articles/connecting-your-channels-to-buffer-HvWLgAJvL9

"Inbox" below means Buffer's **Community** feature. It handles comments, X mentions and Google reviews. **No DMs on any network**: neither the Community page nor its help articles mention direct messages (https://buffer.com/community, https://support.buffer.com/articles/how-comments-work-across-each-social-network-in-buffer-community-2IEzkHcO0Q).

| Network | Supported? | Publishing | Inbox (Community) | Analytics (Insights) |
|---|---|---|---|---|
| Facebook | Yes: Pages. Groups are notification-only. Personal profiles are not supported. | Pages yes. Groups by notification only. | Pages: comments, reply, like. Facebook @mentions not supported. Stories and ads not supported. | Pages yes ("Basic Analytics: Paid"). Groups excluded. |
| Instagram | Yes: Business/Creator. Personal is notification-only. | Yes: posts, Reels, Stories, carousels (max 10 images), automatic or notification publishing | Comments on organic posts. Ads, boosted posts and Stories excluded. No DMs. | Yes (Business/Creator). No analytics for Reels per the channels page. |
| Threads | Yes | Yes | Yes (comments) | Yes |
| X / Twitter | Yes (profile) | Yes | Yes: comments plus a **Mentions tab** (2026-07-13). AI reply suggestions **not** supported for X. | Yes ("Basic Analytics: Paid") |
| LinkedIn Page | Yes | Yes | Yes. Near real-time. All six LinkedIn reactions. | Yes. Includes a follower-growth benchmark (LinkedIn only). |
| LinkedIn profile | Yes | Yes | Yes. Polled about every 6 hours. | Yes |
| TikTok | Yes | "Direct or notification" | Yes: comments (added 2026-03) | Yes ("Basic Analytics: Paid") |
| YouTube | **Shorts only**. Long-form uploads not supported. | Shorts only | Yes: comments and nested replies (added 2026-04) | Yes |
| Pinterest | Yes (business; personal listed with conflicting notes) | Yes | **No** | Yes (business) |
| Google Business Profile | Yes | Yes (GBP posts) | **Reviews: read and reply**. No comment surface on GBP posts. | **No**: "not supported in Insights due to API limitations" |
| Nextdoor | **No**. Not on the supported list. | No | No | No |
| Bluesky | Yes (app-password connection) | Yes | Yes. Only comments on posts published through Buffer. | Yes ("Basic Analytics: No") |
| Mastodon | Yes (any server) | Yes | Yes. Polled about every 4 hours. | Yes |
| Reddit | **No**. Explicitly unsupported. | No | No | No |
| Tumblr | **No**. Not on the supported list. | No | No | No |
| WhatsApp | **No**. Explicitly unsupported. | No | No | No |
| (extra) Substack | Notes only | Yes (Notes; not newsletters or long-form) | No: Substack's API does not expose replies | Yes |
| (extra) Start Page | Buffer's own link-in-bio page, **being retired** (see §2) | Yes | No | Excluded from Insights |

Sources for the row details:
- Per-network Community behaviour: https://support.buffer.com/articles/how-comments-work-across-each-social-network-in-buffer-community-2IEzkHcO0Q
- Insights network list and the GBP exclusion: https://support.buffer.com/articles/using-insights-in-buffer-x4gLauQU5a
- YouTube is Shorts only: https://support.buffer.com/articles/using-youtube-shorts-with-buffer-Jl8iR6jIck
- X Mentions tab: https://buffer.com/changelog
- When TikTok, GBP, YouTube and Mastodon gained comment replies (third-party tracker): https://pricingsaas.com/companies/buffer/diffs/2026Q1
- Bluesky app password: https://support.buffer.com/articles/using-bluesky-with-buffer-7f6OcRnzIO
- Liking is available on LinkedIn, Mastodon, Facebook, TikTok, Bluesky and X, but not Instagram, Threads, GBP or YouTube: https://support.buffer.com/articles/using-community-in-buffer-jvTv4uQacA

### 2. Publishing

Main sources: https://buffer.com/publish and https://buffer.com/pricing

- **Scheduling and queue:** Each channel has recurring posting times, each in its own time zone. A post can go to "Next Available", "Prioritize", a set date and time, or publish now. All plans have the queue. https://buffer.com/publish
- **Content calendar:** Week and month views, filterable by channel, tag or post type. Drag to reschedule. Drafts appear on the calendar (2026-07-13). https://buffer.com/publish, https://buffer.com/changelog
- **Best-time suggestions:** "suggestions based on general engagement patterns". These are generic, not per-account. https://buffer.com/publish
- **Bulk/CSV upload:** On all plans, including Free.
  - Per-channel CSV template with columns Text, Image URL, Tags, Posting Time. Pinterest adds Board Name.
  - Limits per upload: Free up to 10 posts per channel, paid up to 100.
  - Text and single-image posts only. No video, Reels, carousels or first comments. Not available for YouTube.
  - Posts over the limit are skipped without an error.
  - https://support.buffer.com/articles/how-to-upload-posts-in-bulk-to-buffer-cTIhl4mv6H
- **Per-network customization:** Each network gets its own text box with its own caption, hashtags, link and time. https://buffer.com/publish
- **First comment:** Paid plans only ("Not included" on Free). https://buffer.com/pricing
  - Networks per a third-party tracker: Instagram, LinkedIn, and Facebook since 2026-04. The full list is **UNVERIFIED**. https://pricingsaas.com/companies/buffer/diffs/2026Q1
- **Stories/Reels/carousels:**
  - The composer has Post, Reel and Story formats. Instagram carousels are capped at 10 images by the API. https://buffer.com/publish, https://support.buffer.com/articles/scheduling-instagram-posts-reels-stories-and-notifications-3XA98S9Q5p
  - Instagram music, collaborators and tagging were added 2026-10-07. Mixed image and video posts were added 2026-09-28. https://buffer.com/changelog
- **Drafts:** Unlimited on all plans. Approvals are Team only. https://buffer.com/pricing
- **Evergreen/recycling:** No native evergreen re-queue.
  - "Recurring posts" exist **in Beta**: repeat count plus a weekly cadence; X named; plan availability not stated. https://support.buffer.com/articles/is-it-possible-to-schedule-one-post-to-repeat-multiple-times-521zxc7H5D
  - Evergreen recycling relies on third-party integrations such as Evergreen Content Poster (WordPress). https://buffer.com/integrations
- **Content categories:** Tags (250 on all plans) used as themes, and channel groups (paid). https://buffer.com/pricing, https://buffer.com/create
- **RSS/blog auto-posting:**
  - Native "Content Feeds" are **manual pick only**: you click "Create Post" or "Save as idea". Free allows 3 feeds, paid up to 50. RSS/Atom only. https://support.buffer.com/articles/using-content-feeds-in-buffer-aQcD1RyIir
  - Automatic RSS-to-queue goes through Zapier. https://support.buffer.com/articles/automating-rss-feeds-using-zapier-uYIv3KlO6R
  - WordPress auto-share and Nelio Content integrations exist. https://buffer.com/integrations
- **Media library:** Not documented. Media comes from Dropbox, Google Drive, Google Photos, OneDrive, Canva and Unsplash. A native media library is **UNVERIFIED / not found**. https://buffer.com/create, https://buffer.com/integrations
- **Link shortener/UTM:**
  - Link shortening is on all plans, and a Bitly integration exists. https://buffer.com/pricing, https://buffer.com/integrations
  - Campaign tracking (UTM) is turned on per channel. Custom UTM values need a paid plan.
  - UTMs work on Facebook, X, LinkedIn, YouTube Shorts, GBP, Bluesky, TikTok, Start Page and Threads. They do not work on Mastodon, Instagram or Pinterest. https://support.buffer.com/en-us/articles/understanding-utm-parameters-and-google-analytics-m6TkjRVgbd
- **Link-in-bio:** "Start Page" is **being retired**. https://buffer.com/resources/retiring-start-page/
  - New pages stopped 2026-11-04. Editing ends 2026-11-30.
  - Pages come down 2027-02-02 unless migrated. Buffer partnered with Lnk.bio for migration.
- **Other:**
  - Browser extension (all plans).
  - Reminder notifications for notification-publishing.
  - Duplicate posts.
  - Saved custom views (2026-07-07).
  - Custom video covers (paid).
  - https://buffer.com/pricing, https://buffer.com/changelog

### 3. Content creation

- **AI Assistant:** Included on all plans, including Free, "without any limits". Built on OpenAI's API, and text typed into the Assistant is shared with OpenAI. https://buffer.com/ai-assistant
  - Brainstorms post ideas.
  - Repurposes one post into several formats for different platforms.
  - Summarizes long content into short posts.
  - Rewrites, shortens or expands text, and changes tone (more casual or more formal).
  - Tailors posts to LinkedIn, Threads, X, Instagram and Bluesky.
- **Repurposing long content:** The Assistant "summarize[s] long content into short posts". https://buffer.com/ai-assistant
  - No dedicated blog-URL or video-to-posts repurposing tool is documented (**UNVERIFIED / not found**).
- **Other AI:**
  - AI-generated alt text (2026-06-17). https://buffer.com/changelog
  - AI "Takeaways" in Insights. https://support.buffer.com/articles/using-insights-in-buffer-x4gLauQU5a
  - AI reply suggestions in Community (§4).
- **Templates:** Ready-made formats (tip, story, case study, how-to) with placeholders. https://buffer.com/create
  - Custom post templates that can be shared with the team (2026-07-22). https://buffer.com/changelog
- **Ideas board:** Kanban "Doing → Done" board with tags. https://buffer.com/create
  - Limits: Free 100. Essentials and Team are "Unlimited" per https://buffer.com/pricing but "up to 2,000 ideas per organization" per https://buffer.com/create. The two pages conflict.
- **Hashtag tools:** Hashtag Manager saves hashtag groups that are shared across the team (paid plans). https://support.buffer.com/articles/how-hashtags-work-in-buffer-kusRDsVgD8, https://buffer.com/pricing
  - There is also a free web "AI Hashtag Generator", separate from the app. https://buffer.com/ai-assistant
- **Image/video editing:** https://buffer.com/changelog, https://buffer.com/pricing, https://buffer.com/create
  - New image editor (2026-09-15) with crop, rotate, flip, zoom and alt text.
  - Custom video covers (paid).
  - No video editing is documented.
- **Design integrations:**
  - Canva works inside the composer: start from a template, then "Return to Buffer". Desktop only, no Brand Kit, no animated video. https://support.buffer.com/articles/using-canva-with-buffer-dP9Hu8fhao
  - Unsplash for stock photos. https://buffer.com/integrations

### 4. Engagement (Community)

Main sources: https://buffer.com/community, https://support.buffer.com/articles/using-community-in-buffer-jvTv4uQacA, and https://support.buffer.com/articles/how-comments-work-across-each-social-network-in-buffer-community-2IEzkHcO0Q

- **Unified inbox:**
  - Comments: Instagram, Facebook, LinkedIn, Threads, Bluesky, X, TikTok, YouTube and Mastodon.
  - Mentions: an X Mentions tab, plus incoming LinkedIn mentions.
  - Reviews: Google Business Profile.
  - **DMs: not supported** (not documented anywhere by Buffer). A third-party competitor page says DMs are "in progress" with no date (**UNVERIFIED**). https://www.shortsync.app/alternative/buffer/instagram-dms
  - Comments on ads or boosted posts are excluded.
  - Views: "By Post" or "List". Sorts: Unanswered first, Newest, Oldest. Comments can be resolved and bulk-resolved.
- **Assignment to team members:** Not documented. Community has per-channel access levels (No Access / View Only / Full Access, 2026-07-20) but no assignment. Collaboration on posts uses Notes with @mentions (2026-10-14). https://buffer.com/changelog, https://support.buffer.com/articles/using-community-in-buffer-jvTv4uQacA
- **Saved replies:** Shared org-wide, 1,000 characters each.
  - Limits: Free 1. Team unlimited. Essentials is "5" in the help article but "Unlimited" on the pricing page. The two pages conflict. https://support.buffer.com/articles/using-community-in-buffer-jvTv4uQacA, https://buffer.com/pricing
- **AI-suggested replies:**
  - A draft is suggested and a person edits and sends it. Buffer markets them as "Smart suggestions that learn your style and sound like you". https://buffer.com/community
  - Limits: Free and trial get 5 per week (shared across the org). Paid is unlimited. https://support.buffer.com/articles/using-community-in-buffer-jvTv4uQacA
  - Not available for X. Offered for Google reviews that carry a star rating. https://support.buffer.com/articles/how-comments-work-across-each-social-network-in-buffer-community-2IEzkHcO0Q
- **Automatic replies/chatbots:** None documented. Every reply is sent by a person. https://buffer.com/community
- **Moderation rules (auto-hide keywords/spam):** None documented.
  - TikTok comments that TikTok itself hides are collapsed behind a notice. https://support.buffer.com/articles/using-community-in-buffer-jvTv4uQacA
  - Buffer cannot turn comments off on a post. https://support.buffer.com/articles/how-comments-work-across-each-social-network-in-buffer-community-2IEzkHcO0Q
- **Sentiment:** Not documented.
- **Comment score:** Tracks response rate, speed and consistency. https://buffer.com/community
- **Comment insights:** Included on all plans. https://buffer.com/pricing
- **Contact/CRM history:** Not documented.
- **Other:**
  - "Create post from reply" (turn a comment into a post). https://buffer.com/community
  - Hourly email alerts for unreplied comments during the first 24 hours after a post publishes. https://support.buffer.com/articles/using-community-in-buffer-jvTv4uQacA

### 5. Reviews

- **Google Business Profile:** Read and reply inside Community. https://support.buffer.com/articles/how-comments-work-across-each-social-network-in-buffer-community-2IEzkHcO0Q
  - Replies publish to the profile and are text-only.
  - Reviews appear in List view only, sometimes up to 2 hours after posting.
  - AI reply suggestions are offered for reviews with a star rating.
  - Reviewer photos and videos show on reviews from 2026-06-10 onward.
- **Facebook recommendations/reviews:** Not documented (**UNVERIFIED**; no Buffer page mentions them).
- **Yelp, TripAdvisor and other review sites:** Not supported. They are not on the channel list. https://support.buffer.com/en-us/articles/supported-channels-LM3P7Y4zsp

### 6. Social listening

- **Keyword/brand monitoring:** Not offered. The plan comparison has no listening feature. The only mention-tracking is the X Mentions tab and incoming LinkedIn mentions in Community. https://buffer.com/pricing, https://buffer.com/changelog
  - A third-party competitor page also says Buffer has no social listening. https://youscan.io/blog/buffer-vs-hootsuite/
- **Competitor tracking:** Not offered.
  - "Benchmarks" compare you with aggregate Buffer accounts, not named competitors. https://buffer.com/analyze
  - The follower-growth benchmark covers LinkedIn only. https://support.buffer.com/articles/using-insights-in-buffer-x4gLauQU5a

### 7. Analytics and reporting

Main sources: https://support.buffer.com/articles/using-insights-in-buffer-x4gLauQU5a, https://buffer.com/analyze, https://buffer.com/pricing

- **Product history:**
  - The "Insights" analytics were rebuilt 2026-04-14 with an All Channels view and per-channel detail. https://buffer.com/changelog
  - The legacy "Buffer Analyze" product retired 2026-10-07. https://support.buffer.com/article/955-using-buffer-analyze
- **Per-network metrics:** Vary by API. Common ones are Posts, Reactions, Comments, Impressions, Shares, Saves, Follows, Reach and Engagement Rate.
  - Networks: Instagram, Facebook Pages, X, LinkedIn Pages and profiles, TikTok, Pinterest, Threads, Bluesky, YouTube, Mastodon and Substack.
  - Not GBP, Facebook Groups or Instagram personal.
- **Cross-network:** All Channels view. Multi-channel Takeaways were added 2026-09-17. https://buffer.com/changelog
- **History:** Free gets 30 days (preset ranges only). Paid gets up to 2 years with custom ranges. https://buffer.com/pricing
- **Competitor benchmarks:** No (see §6).
- **Custom, white-label and PDF reports:**
  - Paid plans export CSV, Markdown and PDF. Snapshot images are available on all plans.
  - The PDF export takes a custom report name, a cover logo, and a choice of sections.
  - "Branded reports" are **Team only** on the pricing page. They add a logo and generate a cover page.
  - There is no true white-label: no custom domain and no removal of Buffer branding is documented.
  - Users with view-only access can't export.
- **Scheduled report delivery:** Built-in email digests only. "Daily Post Recap" and "Weekly Report Card" cover all channels in organizations you own and go to Buffer users. No scheduled PDF delivery to clients is documented. https://support.buffer.com/articles/enabling-email-notifications-in-buffer-kuKhKBU7c3
- **ROI/UTM tracking:** UTM campaign tracking with custom parameters on paid plans (see §2). No revenue or ROI attribution is documented. https://support.buffer.com/en-us/articles/understanding-utm-parameters-and-google-analytics-m6TkjRVgbd
- **Boosted posts:** Boosted-post data covers Facebook and Instagram only (see §10). https://support.buffer.com/article/516-why-isnt-my-ad-data-displayed-on-boosted-posts

### 8. Collaboration and approvals

- **Roles and permissions:** Overhauled 2026-11-17 for Team plan customers. https://buffer.com/changelog/flexible-roles-permissions-for-teams
  - There are four roles:
    - **Admin:** everything, including team and billing.
    - **Creator:** full posting and commenting on selected channels, and can connect channels.
    - **Collaborator:** creates drafts and submits them for approval, and views comments and analytics.
    - **Viewer:** view only.
  - Admins can further restrict permissions org-wide, per channel and per teammate. Examples: block replying to comments, block exporting Insights.
  - Older per-channel settings were Publish (Full Access / Needs Approval / No Access) and Community (Full Access / View Only / No Access). https://support.buffer.com/articles/using-community-in-buffer-jvTv4uQacA
  - "Custom access and permissions" and unlimited users are **Team plan only**. https://buffer.com/pricing
- **Internal approval workflow:** **Team plan only.** https://support.buffer.com/articles/requesting-approval-and-approving-draft-posts-57li7M8tDA
  - A Needs Approval member clicks "Save and Request Approval", which moves the draft to the "Approvals" tab.
  - The owner or a Full Access member can edit, approve (the post goes into the queue or its set time), reject (back to the creator's drafts) or delete it.
  - Drafts from Needs Approval members stay drafts whether they come from the dashboard, mobile apps, the **Buffer API, the MCP server, or third-party apps**.
  - Approval-request emails are opt-in under Settings > Notifications > "Collaboration". Creators get an email when a draft is rejected.
- **External client approval:** Clients have to be Team-plan members. There is no guest approval link or no-login client portal. https://support.buffer.com/article/667-using-buffer-as-an-agency
  - Buffer's documented agency setup gives agency staff "Requires Approval" access and the client "Full Posting" access, so the client approves inside Buffer.
- **Notes/tasks:**
  - Notes on posts with @mentions and real-time discussion (2026-10-14). https://buffer.com/changelog
  - Notes are included on all plans. https://buffer.com/pricing
  - No task feature is documented.
- **Audit trail:** Not documented (**UNVERIFIED**).

### 9. Agency / multi-client

- **Workspaces:** "Organizations" are documented. A user can belong to several and switch between them, and each organization's tools follow its own subscription. https://support.buffer.com/article/873-switching-organizations-on-the-web, https://support.buffer.com/article/674-getting-started-as-a-user-in-an-organization
  - Whether each organization is billed separately is not stated explicitly (**UNVERIFIED**).
- **Agency positioning:** Billing is per channel with unlimited team members (Team plan), and per-channel cost can be built into client packages. https://buffer.com/agencies
  - Buffer recommends managing no more than 5 Instagram accounts in total per organization. https://support.buffer.com/article/667-using-buffer-as-an-agency
- **Client portals:** Not offered (clients log in as team members).
- **White-labeling:** Not offered beyond logo and cover-page branded PDF reports (Team plan). https://buffer.com/pricing, https://buffer.com/agencies
- **Per-client billing:** Not documented. Billing is tied to the organization owner. https://support.buffer.com/article/667-using-buffer-as-an-agency
- **Agency plan:** None among current plans (Free, Essentials, Team). A third-party source says the Agency plan was retired in a November 2025 pricing update (**UNVERIFIED**). https://buffer.com/pricing, https://www.socialpilot.co/blog/buffer-pricing

### 10. Ads

- **Boosting posts and running ads:** Not supported. Buffer "cannot boost posts or run social media ads". https://support.buffer.com/hc/en-us/articles/360056412074 (search-result text). The fetch of that URL returned 404, so the quote rests on the search snippet; related article: https://support.buffer.com/article/516-why-isnt-my-ad-data-displayed-on-boosted-posts
- **Ads reporting:** Boosted-post data is shown for Facebook and Instagram only. https://buffer.com/resources/boosted-post-insights/, https://support.buffer.com/article/516-why-isnt-my-ad-data-displayed-on-boosted-posts
  - Ads created in Ads Manager and boosted X posts are not included.
  - It is **UNVERIFIED** whether this survived the move to the new Insights.

### 11. Email

- **Email marketing/newsletters:** None. Buffer is not an email tool.
  - The closest feature is publishing **Substack Notes** (Notes only, not newsletters), added 2026-09-23. https://buffer.com/changelog, https://support.buffer.com/en-us/articles/supported-channels-LM3P7Y4zsp
- **Sending posts as email:** Not native. It is possible through Zapier ("send emails for new Buffer queue items"). https://zapier.com/apps/buffer/integrations/email/1289946/send-emails-for-new-buffer-queue-items-using-email-by-zapier
- **Creating posts from email:** Not documented natively. It works through Zapier or IFTTT email triggers. https://zapier.com/apps/buffer/integrations/email/1601528/add-new-inbound-emails-from-email-by-zapier-to-queues-in-buffer
  - An old Buffer address, add@to.bufferapp.com, appears only on a stale staging page (**UNVERIFIED**, likely retired).
- **Email-based approvals:** Approval-request and rejection emails are notifications only. Approving still happens in the app. https://support.buffer.com/articles/requesting-approval-and-approving-draft-posts-57li7M8tDA
- **Email report delivery:** "Daily Post Recap" and "Weekly Report Card" go to account users. No scheduled report emails to outside recipients are documented. https://support.buffer.com/articles/enabling-email-notifications-in-buffer-kuKhKBU7c3
- **Other notification emails:** https://support.buffer.com/articles/enabling-email-notifications-in-buffer-kuKhKBU7c3
  - Post Failures
  - Channel Connection Updates
  - Hourly Comment Alerts
  - Published Post Confirmations
  - Empty Queue Alerts
  - Mentions of you
  - Updates in threads you follow
- **Email inbox integration:** None.

### 12. Automation and AI agents

- **Auto-posting:**
  - The queue publishes automatically. Instagram, TikTok and others fall back to "notification publishing" where the API can't auto-post. https://support.buffer.com/en-us/articles/supported-channels-LM3P7Y4zsp
  - Recurring posts are in Beta (§2).
  - RSS auto-posting only through Zapier or WordPress plugins (§2).
- **Auto-replies:** None documented. AI reply suggestions are drafts that a person edits and sends. https://support.buffer.com/articles/using-community-in-buffer-jvTv4uQacA
- **AI agents via MCP** (2026-06-10): https://buffer.com/mcp, https://buffer.com/changelog
  - Works with Claude, ChatGPT, Perplexity, Cursor, Grok, Muse and Raycast.
  - Can create and schedule posts, manage the queue, save ideas, and read Insights.
  - Comments are not mentioned.
  - Available on every plan, including Free.
- **MCP guardrails:**
  - Posts created by an assistant "land in your queue as drafts or scheduled posts, not as live posts", so they can be reviewed before publishing.
  - The user chooses which tools get access, and access can be revoked under Settings → API. https://buffer.com/mcp
  - With the Team plan, members set to Needs Approval have API/MCP-created posts held as drafts awaiting approval. https://support.buffer.com/articles/requesting-approval-and-approving-draft-posts-57li7M8tDA
  - Note: "scheduled" means an agent can put a post on the schedule without a review step unless the approval role applies (inference from the MCP page wording).

### 13. Integrations and API

- **Integration list** (from https://buffer.com/integrations):
  - Bitly
  - Canva
  - Dropbox
  - Google Drive
  - Google Photos
  - OneDrive
  - Unsplash
  - Zapier
  - Make
  - n8n
  - IFTTT
  - Microsoft Power Automate
  - WordPress
  - Nelio Content
  - Evergreen Content Poster
  - Feedly
  - Quuu
  - WriteStack
  - Raycast
  - ChatGPT
  - Claude
  - Cursor
  - Grok
  - Muse
  - Perplexity
  - OpenAI (AI Assistant)
- **Not listed:** Slack, HubSpot, Salesforce or any CRM, and Shopify.
- **Public API:**
  - A new **GraphQL Public API** was announced 2026-05-27, with a CLI and an MCP server. https://buffer.com/resources/buffer-api-is-here/
  - Available on all plans, including Free.
  - Coverage: Account, Organizations, Channels, Tags, Posts, Ideas, and post metrics. Comments/Community is not covered. https://developers.buffer.com/
  - Authentication: a personal API key (Bearer) or **OAuth 2.0 PKCE app clients** that "access Buffer accounts on behalf of your users". https://developers.buffer.com/guides/authentication.md
    - Only the organization owner can create keys. https://support.buffer.com/articles/how-to-create-your-buffer-api-key-ShIgYVwM6j
    - A key covers every organization in the account, with no per-organization scoping.
    - One Buffer migration-guide page still called OAuth for third-party apps "coming soon". The docs conflict, and the current authentication guide presents OAuth as available. https://developers.buffer.com/guides/rest-migration.html
  - Limits per plan (Free / Essentials / Team):
    - API keys: 1 / 3 / 5
    - App clients: 1 / 3 / 5
    - Requests per 15 minutes: 150 / 450 / 1,500
    - Requests per 24 hours: 600 / 1,800 / 6,000
    - Requests per 30 days: 1k / 3k / 10k per channel, up to 3k / 30k / 100k
    - https://buffer.com/pricing
  - **Legacy REST API is retired on 2027-02-01.** Brownouts on 2026-11-11 and 2026-12-09. New legacy client creation is already closed. https://buffer.com/resources/legacy-rest-api-retired/
- **Mobile apps:** iOS and Android. Push-notification settings can be managed from the web (2026-09-22). https://buffer.com/changelog, https://buffer.com/publish

### 14. Pricing

Pricing is **per channel**: each connected social account is one channel. Main sources: https://buffer.com/pricing and Buffer's machine-readable https://buffer.com/pricing.md

| Plan | Monthly billing | Annual billing | Users | Channels | Posts |
|---|---|---|---|---|---|
| Free | $0 | $0 | 1 | Up to 3. Lifetime cap of 8 unique channels per network on Free. | 10 scheduled per channel; 100 ideas |
| Essentials | **$6 per channel/mo** (channels 1–10) | **$5 per channel/mo** ($60 per channel per year) | 1 | Per channel, unlimited | "Unlimited", fair-use cap of 5,000 per channel |
| Team | **$12 per channel/mo** (channels 1–10) | **$10 per channel/mo** ($120 per channel per year) | **Unlimited** | Per channel, unlimited | "Unlimited", fair-use cap of 5,000 per channel |

- **Volume tiers** (monthly, per channel, Essentials / Team):
  - Channels 11–25: $4 / $4
  - Channels 26–50: $3 / $3
  - Channels 51+: $1 / $2
  - Annual rates above 10 channels are not published. https://buffer.com/pricing.md
- **Discount wording:** The plan cards say "save 2 months" (about 17%) while the FAQ says "20% discount". Buffer's own pages are inconsistent. https://buffer.com/pricing
- **Trial:** 14 days of the full toolkit (30 days via referral). https://support.buffer.com/articles/information-about-free-trials-UXaCr0Y1Ht
  - A card is not required to start ("No credit card needed" for the Free signup). https://buffer.com/join
  - Without a card on file, the account drops to Free at the end of the trial.
- **Add-ons:** None. Discounts: 50% for nonprofits. https://buffer.com/pricing.md
- **What separates the plans:**
  - **Essentials** adds: first comment, channel groups, hashtag manager, custom video covers, 2-year Insights, reports and exports, custom UTMs, unlimited AI replies.
  - **Team** adds: unlimited users, approval workflows, custom access and permissions, branded reports.
  - https://buffer.com/pricing
- **Payment methods:** Cards, Apple Pay, Amazon Pay, Cash App, Link, and US bank accounts for transactions over $250. https://support.buffer.com/articles/buffers-accepted-payment-methods-PQpO40rDXV
  - **PayPal is NOT accepted:** "Buffer doesn't currently accept PayPal, Google Pay, Venmo, Zelle, check, or other types of payments not listed".
- **Price history:** A third-party tracker headline from 2026-03-03 reads "Buffer slashes prices 75% but cuts default channels to 1", with no detail (**UNVERIFIED**). https://pricingsaas.com/companies/buffer/diffs/2026Q1

### 15. Onboarding

- **Sign up:** Use "Get started for free" at https://buffer.com/join ("No credit card needed. Free forever"), or start a 14-day trial from the pricing page. https://support.buffer.com/articles/information-about-free-trials-UXaCr0Y1Ht
  - The exact sign-up methods (email, Google and so on) are **UNVERIFIED**; the join page doesn't name them.
- **Connect a channel:** https://support.buffer.com/articles/connecting-your-channels-to-buffer-HvWLgAJvL9
  1. Log in to the exact social account in one browser tab.
  2. In Buffer, click "+ New" → "Connect a New Channel".
  3. Pick the network and grant permissions ("approve everything it offers").
  4. Finish.
- **Per-network requirements:**
  - Facebook: a profile with "Full control" of the Page.
  - LinkedIn Pages: must be a Super Admin.
  - YouTube: must be the channel Owner.
  - Instagram: must be a Business or Creator account for auto-publishing.
  - Bluesky: the user generates a Bluesky **app password** and pastes it into Buffer. This is the one manual-credential step. https://support.buffer.com/articles/using-bluesky-with-buffer-7f6OcRnzIO
  - Mastodon: pick your server and sign in.
- **Developer apps or API keys:** **Customers never create a developer app or API key to use Buffer.** The connection guide only describes OAuth sign-in through each network, plus the Bluesky app password.
  - API keys and OAuth app clients exist only to connect outside tools, scripts or AI assistants: "Your key works like a password that lets another tool act on your Buffer account." https://support.buffer.com/articles/how-to-create-your-buffer-api-key-ShIgYVwM6j

### 16. Distinctive points

- **Per-channel pricing with a public volume ladder** (down to $1–$2 per channel at 51+), unlimited users on Team, and a genuinely usable Free plan. The Free plan includes 3 channels, Community replies, 5 AI reply suggestions a week, and API access. https://buffer.com/pricing.md, https://pricingsaas.com/companies/buffer/diffs/2026Q1
- **Engagement has no DMs.** The inbox is comments, X mentions and GBP reviews. AI replies are always suggestions that a person sends. There are no auto-replies or moderation rules. Comment score gamifies responsiveness. https://buffer.com/community
- **Approvals are Team-only and in-app only.** There is no external approval link, but the approval role also holds API- and MCP-created posts as drafts. This is a useful pattern for products that add AI agents. https://support.buffer.com/articles/requesting-approval-and-approving-draft-posts-57li7M8tDA
- **AI-agent-first moves:**
  - An MCP server on all plans.
  - A GraphQL API with OAuth app clients on all plans.
  - A machine-readable pricing page for AI agents (https://buffer.com/pricing.md). Third-party tracker: https://pricingsaas.com/companies/buffer/diffs/2026Q1
- **Pruning to the core:**
  - Start Page link-in-bio is being retired (2027-02-02). https://buffer.com/resources/retiring-start-page/
  - Legacy Analyze retired 2026-10-07. https://support.buffer.com/article/955-using-buffer-analyze
  - Legacy REST API retired 2027-02-01. https://buffer.com/resources/legacy-rest-api-retired/
- **Gaps a newcomer can target:**
  - No Nextdoor, Reddit, Tumblr or WhatsApp.
  - YouTube is Shorts only.
  - Reviews only on Google, not Yelp or Facebook.
  - No social listening or competitor tracking.
  - No email marketing.
  - No white-label or client portal.
  - No PayPal.
  - No ad boosting.
- **Fast shipping cadence:** 20 changelog entries between 2026-04-14 and 2026-11-17. https://buffer.com/changelog


---

## Later

Checked 2026-11-18.

Scope: **Later Social** (later.com), the social media management product, including Link in Bio (formerly "Linkin.bio") and Social Inbox (which replaced the older "Conversations" tool). **Later Influence** is a separate, sales-led influencer-marketing product for enterprise brands. It appears below only where it changes a point. Later now presents itself as an influencer-marketing company first, with social media management as one of four offerings (https://later.com/).

Help-center articles were read through Later's public Zendesk Help Center API (`help.later.com/api/v2/help_center/...`), because the article pages return HTTP 403 to automated fetches. The URLs cited are the human-readable article URLs, and the article-updated dates come from the API.

### 1. Supported networks

Later Social supports eight platforms: Instagram, Facebook, Pinterest, TikTok, LinkedIn, Threads, YouTube (Shorts only) and Snapchat. The same article lists these as **not supported**: Google My Business, Mastodon, WordPress, Tumblr, RSS Feeds, Twitch, Vimeo, Zapier, ClickUp, HubSpot, Reddit and Bluesky. Source: "Supported Social Platforms & Post Types", updated 2026-10-21, https://help.later.com/hc/en-us/articles/360060842914-Supported-Social-Platforms-Post-Types

**X (Twitter) was dropped.** New X accounts could not be connected after July 30, 2025. Support ended on August 28, 2025. Posts scheduled for that date or later did not publish, and X analytics and X Social Listening data stopped. Later's article gives no reason (https://help.later.com/hc/en-us/articles/33709975610135-Ending-Support-for-X-formerly-Twitter). Later's partners page still lists X as a "partial integration, Later Influence only" (https://later.com/partners/).

| Network | Supported? | Publishing | Inbox / engagement | Analytics |
|---|---|---|---|---|
| Facebook | Yes, Pages only. No personal profiles or Groups. | Yes: image, video, multi-photo, text-only, Reels. **No Stories.** [1] | Yes on Growth+: comments and DMs [2] | Yes, Starter+ [3] |
| Instagram | Yes, Business and Creator only. Personal profiles are no longer supported. [1][4] | Yes: single image, Reels, Stories (auto-publish for Business only), carousel, Collab posts (up to 3) [1] | Yes on Growth+: comments and DMs. DMs older than 7 days cannot be answered. [2] | Yes, Starter+. History: 3 months (Starter), 1 year (Growth), 2 years (Scale). [3] |
| Threads | Yes | Yes: text, image, video, carousel. Desktop only. [1] | **No.** Social Inbox covers only Instagram, Facebook and TikTok. [2] | Yes, Starter+ [3][5] |
| X (Twitter) | **No.** Removed 2025-08-28. [6] | No | No | No |
| LinkedIn Page | Yes. Connecting needs LinkedIn Page super-admin rights. [4] | Yes: single image or video, text, carousels. No PDF documents. [1] | No [2] | Yes, Starter+. CSV export on Growth+. [7] |
| LinkedIn personal profile | Yes [1] | Yes: single image, video, text. Multi-photo is not supported. The article is internally inconsistent about carousels and video for personal profiles. [1] | No [2] | Yes, post performance: impressions, members reached, reactions, comments, reshares. [7] |
| TikTok | Yes, Personal and Business [1] | Yes: video, photo, multi-photo. Photo posts are desktop only. [1] | **Comments only** (no DMs), Growth+ [2] | Yes, Starter+ [3] |
| YouTube | Shorts only, on Personal and Brand channels. [1] | Shorts only. **No long-form video.** Desktop only. Free plan: 1 Short a month. [1][3] | No [2] | **No.** "Later doesn't currently offer built-in analytics for YouTube Shorts or Snapchat." [5] |
| Pinterest | Yes, Personal and Business [1] | Yes: image, video. No Stories. [1] | No [2] | Yes, Starter+. Pinterest click tracking on Growth+. [3] |
| Google Business Profile | **No.** Listed as unsupported under the name "Google My Business". [1] | No | No | No |
| Nextdoor | **No.** It is not named on the supported list, the unsupported list or the partners page. [1][8] | No | No | No |
| Bluesky | **No.** Listed as unsupported. [1] | No | No | No |
| Mastodon | **No.** Listed as unsupported. [1] | No | No | No |
| Reddit | **No.** Listed as unsupported. [1] | No | No | No |
| Tumblr | **No.** Listed as unsupported. [1] | No | No | No |
| WhatsApp | **No.** Not mentioned anywhere in Later's supported or partner lists. [1][8] | No | No | No |
| Snapchat (extra) | Yes, public profiles with at least one public post, Story or Spotlight. [1] | Yes: Video Spotlight and My Story Public. Desktop only. [1] | No [2] | No [5] |

Sources:
[1] https://help.later.com/hc/en-us/articles/360060842914-Supported-Social-Platforms-Post-Types
[2] https://help.later.com/hc/en-us/articles/31010972850199-Manage-DMs-Comments-in-Later-with-Social-Inbox and https://later.com/social-media-inbox/
[3] https://help.later.com/hc/en-us/articles/40920284785687-Reference-Guide-Plan-Availability-for-Later-Social (updated 2026-11-03)
[4] https://help.later.com/hc/en-us/articles/360043244733-Add-Remove-Transfer-Social-Profiles-in-Later and https://help.later.com/hc/en-us/articles/1500003115942-Social-Profile-Connection-Statuses
[5] https://help.later.com/hc/en-us/articles/360043246993-Getting-Started-With-Later-Social and https://help.later.com/hc/en-us/articles/33109662792471-Later-s-Custom-Analytics
[6] https://help.later.com/hc/en-us/articles/33709975610135-Ending-Support-for-X-formerly-Twitter
[7] https://help.later.com/hc/en-us/articles/33639033081367-Later-s-LinkedIn-Analytics
[8] https://later.com/partners/

### 2. Publishing

All points are from https://help.later.com/hc/en-us/articles/40920284785687-Reference-Guide-Plan-Availability-for-Later-Social unless another source is given.

- **Scheduling and content calendar:** Yes. Planning and publishing run from one visual calendar, with drag-and-drop feed preview (the Visual Planner) on all plans (https://later.com/social-media-scheduler/). Calendar view on Free covers 2 weeks back and forward; paid plans cover 1 year (https://help.later.com/hc/en-us/articles/360059362253-Choose-a-Later-Social-Plan).
- **Auto Publish vs notification publishing:** Pinterest, LinkedIn and Facebook publish automatically. Instagram and TikTok publish automatically only when set to Auto Publish; otherwise Later sends a push notification and the person posts manually (https://help.later.com/hc/en-us/articles/360043243873-Schedule-One-Post-to-Multiple-Social-Profiles).
- **Queue and time slots ("Quick Schedule"):** Yes, on all plans. These are recurring weekly time slots; you drag media onto the calendar and it fills them. Only one post can go in a slot. This works for Instagram, Facebook, TikTok and LinkedIn, but not for Pinterest or YouTube Shorts, and not for multi-profile scheduling. Quick-scheduled Instagram and TikTok posts default to notification publishing (https://help.later.com/hc/en-us/articles/360043243793-How-to-Quick-Schedule-Your-Week-of-Posts, updated 2026-10-26).
- **Best-time suggestions:** "Best Time to Post" on Starter+, built from past post data, follower activity and industry trends. Multi-profile best times and "smart scheduling with future trends" are on Growth+ (https://later.com/pricing/, https://later.com/social-media-ai-tools/).
- **Bulk / CSV upload:** Bulk *media* upload is supported: you drag in many photos or videos at once (https://help.later.com/hc/en-us/articles/360043360953-Later-Glossary). **No CSV or spreadsheet post import was found** in Later's help center. Posts cannot be bulk-edited (https://help.later.com/hc/en-us/articles/360043243873-Schedule-One-Post-to-Multiple-Social-Profiles). A competitor (RecurPost) also states that Later has no bulk CSV upload (https://recurpost.com/later-alternatives). Treat the absence of CSV import as likely but UNVERIFIED by Later itself.
- **Per-network text customisation:** Yes. One post can be scheduled to up to 10 profiles at once, and "Customize X Posts" lets you edit content and settings per profile. The profiles must be in the same Access Group, and you cannot add profiles to a post after it is scheduled (https://help.later.com/hc/en-us/articles/360043243873-Schedule-One-Post-to-Multiple-Social-Profiles).
- **First comment:** **Instagram only**, Business or Creator profiles, Auto Publish posts only, Starter+. "Scheduling a First Comment isn't supported for TikTok, Facebook, LinkedIn, Pinterest, YouTube, Threads, or Snapchat" (https://help.later.com/hc/en-us/articles/360043099194-Schedule-a-First-Comment-on-Instagram).
- **Stories, Reels and carousels:** Instagram Reels on all plans, including Reels audio from Meta's library. Instagram Stories on Starter+, auto-publishing only for Business profiles. Facebook Reels yes; Facebook Stories no. Carousels for Instagram and Facebook on Starter+; carousels also for Threads, TikTok (multi-photo) and LinkedIn (https://help.later.com/hc/en-us/articles/360060842914-Supported-Social-Platforms-Post-Types, plan reference guide).
- **Drafts:** Yes, on all plans. Drafts do not publish, and **each draft counts toward the monthly post limit** (https://help.later.com/hc/en-us/articles/8843980454295-About-Draft-Posts).
- **Evergreen, recycling, content categories:** No recycling or evergreen queue was found. RSS Feeds are explicitly unsupported (https://help.later.com/hc/en-us/articles/360060842914-Supported-Social-Platforms-Post-Types). For organisation there are post and campaign tags: 20 each on Free and Starter, 50 each on Growth, unlimited on Scale (plan reference guide; https://help.later.com/hc/en-us/articles/34506757229591-Using-Post-Campaign-Tagging-to-Organize-and-Report-on-Content).
- **RSS / blog auto-posting:** No. RSS Feeds and WordPress are listed as unsupported (https://help.later.com/hc/en-us/articles/360060842914-Supported-Social-Platforms-Post-Types).
- **Media library:** Yes, with labels, media notes, starred media and a side library (https://help.later.com/hc/en-us/articles/360042771474-Access-Organize-Your-Media-Library). You can import from Canva and Dropbox. **Google Drive "can't be connected to your Later Media Library"** (https://help.later.com/hc/en-us/articles/360043361213-Uploading-Media-Format-Requirements). Upload limits are photo 5 MB and video 25 MB on Free, 20 MB and 512 MB on Starter, and 100 MB and 1.5 GB on Growth and Scale (plan reference guide).
- **Link shortener / UTM:** There is no general link shortener (UNVERIFIED: none found). UTMs exist only in Link in Bio: default UTMs on Free, custom `utm_source`/`utm_medium` on Starter+ (https://help.later.com/hc/en-us/articles/24114233277847-Link-in-Bio-UTM-Parameters).
- **Link in bio:** Yes, the Link in Bio page (formerly Linkin.bio), on all plans, for Instagram and TikTok. It has unlimited buttons and button groups, post-linked tiles, and email collection. Free shows a Later banner. Starter+ removes the banner and adds custom SEO title and description and custom UTMs. Growth+ adds page views by location. Analytics history is 3 months on Free, 1 year on Starter and Growth, and 2 years on Scale (https://help.later.com/hc/en-us/articles/360043255013-Link-in-Bio-Features-By-Plan, plan reference guide).
- **Other:** Instagram product (Shop) tagging on Growth+; alt text, saved captions and Instagram collaborator tagging (plan reference guide).

### 3. Content creation

- **AI writer ("Caption Writer"):** It generates captions from a short description and can rephrase or change tone (casual, professional, custom) on desktop and iOS; Android can only generate. It is a beta feature, English only, and on paid plans only. It uses a third-party model, and Later's article says "Later is not responsible for any content generated." Each generation costs 1 AI credit and returns 3 versions (https://help.later.com/hc/en-us/articles/12252569781015-Later-s-Caption-Writer). Later's marketing says it "learns your tone from past posts" (https://later.com/social-media-ai-tools/).
- **AI "Ideas":** Content pillars and post ideas, 3 ideas per credit. Beta, desktop only, paid plans (https://help.later.com/hc/en-us/articles/15701256720407-Managing-Later-Plan-Add-ons-Credits, plan reference guide).
- **AI credits:** Monthly credits are 0 on Free, 5 on Starter, 50 on Growth and 100 on Scale. They come from one shared pool, reset on the 1st of each month at UTC midnight, and add-on credits are sold only as recurring 100-credit packs (https://help.later.com/hc/en-us/articles/15701256720407-Managing-Later-Plan-Add-ons-Credits, https://later.com/pricing/).
- **Repurposing long content (blog or video into posts):** Not found in any Later page or help article. UNVERIFIED; assume absent.
- **Templates:** "Saved Captions" are reusable text, such as hashtag groups, scoped to an Access Group (https://help.later.com/hc/en-us/articles/360043215033-Saved-Captions-on-Mobile). No post-design templates were found.
- **Hashtag tools:** Search-based hashtag suggestions on Starter+. Automatic hashtag suggestions on Growth+, Instagram only and desktop only. Instagram Hashtag Analytics on Growth+ (plan reference guide).
- **Image and video editing:** Crop and filters on all plans (https://help.later.com/hc/en-us/articles/360043245553-Crop-and-Add-Filters-to-Photos). "Add text to images" on Starter+ (plan reference guide). No video editor was found (UNVERIFIED). Trending sounds, stickers and polls cannot be added to Instagram or TikTok posts because of API limits (https://help.later.com/hc/en-us/articles/17722041742487-Add-Music-Captions-Stickers-Effects-to-Instagram-TikTok-Posts).
- **Canva and design integrations:** Canva's "Export To Later" option and Dropbox import feed the media library. Canva and Dropbox also work in Link in Bio on all plans (https://help.later.com/hc/en-us/articles/360043361213-Uploading-Media-Format-Requirements, plan reference guide). Adobe is listed as a technology partner, with no detail (https://later.com/partners/).
- **UGC collection ("Collect"):** Collect media you are tagged in or mentioned in, or found by hashtag or profile search, and repost it with credit. Tags collection and search are on Starter+; Mentions collection and Contributors (third-party submissions) are on Growth+ (https://help.later.com/hc/en-us/articles/360043246393-Collect-Repost-Media-from-Instagram-Posts-You-re-Tagged-In, plan reference guide).

### 4. Engagement

- **Unified inbox ("Social Inbox"):** Growth and Scale only; Free and Starter can preview it. It is **marked beta and is desktop only**, with no mobile support. It covers **Instagram and Facebook DMs**, and **Instagram, Facebook and TikTok comments**. Mentions and reviews are not in the inbox (https://help.later.com/hc/en-us/articles/31010972850199-Manage-DMs-Comments-in-Later-with-Social-Inbox, https://later.com/social-media-inbox/).
- **Inbox limits:** Only DMs received in the last 7 days can be answered, which Later attributes to Instagram API limits. Conversations in the Requests tab appear only with activity in the last 30 days. Emoji reactions, voice notes, images, polls and starting new DM threads are not supported. You refresh the browser tab to check for new DMs (same help article).
- **Assignment to team members:** The product page says you can "assign and track messages" so ownership stays clear, with response-status indicators (https://later.com/social-media-inbox/). **The help article does not describe assignment**, so how it works is UNVERIFIED. Account Owners **cannot restrict a Member's access to the inbox** (help article above).
- **Saved replies:** Not found. UNVERIFIED; assume absent.
- **AI-suggested replies:** Not found on any Later page or help article. Later's AI features are for captions and ideas only (https://later.com/social-media-ai-tools/). Assume absent.
- **Automatic replies and chatbots:** Not found. Assume absent.
- **Moderation:** You can manually reply to, **hide** or **delete** comments. No auto-hide by keyword or spam rules was found (https://help.later.com/hc/en-us/articles/31010972850199-Manage-DMs-Comments-in-Later-with-Social-Inbox).
- **Sentiment:** Not in the inbox. It is in Social Listening (Scale only): a sentiment score on a -1 to +1 scale over 90 days, with "impactful" comments from the last 7 days and a reply button that opens Social Inbox (https://help.later.com/hc/en-us/articles/33212383435415-Social-Listening-Sentiment).
- **Contact or CRM history:** Not found. Assume absent. HubSpot is listed as unsupported (https://help.later.com/hc/en-us/articles/360060842914-Supported-Social-Platforms-Post-Types).

### 5. Reviews

- **None for local-business reviews.** Google Business Profile is unsupported (listed as "Google My Business"). Facebook reviews or recommendations, Yelp and TripAdvisor are not mentioned anywhere (https://help.later.com/hc/en-us/articles/360060842914-Supported-Social-Platforms-Post-Types). Later Social cannot read or reply to reviews on any review site.
- **Not the same thing:** Later *Influence* (enterprise) integrates with Bazaarvoice, PowerReviews and Yotpo. These integrations send **creator-written product reviews** collected on Later-hosted forms to those platforms for moderation and syndication. They do not manage a business's incoming reviews (https://later.com/partners/, https://help-influence.later.com/hc/en-us/sections/32932443117847-Integrations, https://www.yotpo.com/integrations/later/).

### 6. Social listening

All listening below is **desktop only**.

- **Collect** (mentions, tags and hashtag or profile search; UGC-oriented): Growth+ (plan reference guide).
- **Brand Mentions:** Scale only. It tracks posts and comments that mention your profile, lists top posts, common hashtags and keywords, and the profiles mentioning you. It covers **Instagram and TikTok only** (https://help.later.com/hc/en-us/articles/33212416631063-Social-Listening-Mentions).
- **Sentiment / Brand Health:** Scale only. Covers Instagram, TikTok and Facebook Pages. Data updates hourly (https://help.later.com/hc/en-us/articles/33212383435415-Social-Listening-Sentiment).
- **Keyword "Topics" monitoring:** **Legacy Enterprise plan only**; not available on current plans. Instagram only, with up to 30 keywords per rolling 7 days (https://help.later.com/hc/en-us/articles/24326209263767-Social-Listening-Topics). Open-web or keyword listening is therefore **not** on current self-serve plans.
- **Competitor tracking ("Competitor Benchmarking"):** Scale only, **Instagram only**, desktop only. It compares engagement and growth side by side with competitor accounts. The number of competitors you can track is not stated, so it is UNVERIFIED (plan reference guide, https://later.com/social-media-competitor-analysis/).
- **Future Insights / Industry Insights** (AI trend prediction): Scale only, desktop only (plan reference guide, https://later.com/pricing/). Later launched a sentiment-pills update on 2026-06-04 (https://releasebot.io/updates/later/later-social-listening).

### 7. Analytics and reporting

- **Per-network:** Instagram, Facebook, TikTok, Pinterest, Threads and LinkedIn, on Starter+. Data history is 3 months on Starter, 1 year on Growth and 2 years on Scale. **There are no analytics for YouTube Shorts or Snapchat** (https://help.later.com/hc/en-us/articles/360043246993-Getting-Started-With-Later-Social, https://later.com/pricing/). The Free plan has only "introductory analytics" (https://help.later.com/hc/en-us/articles/360059362253-Choose-a-Later-Social-Plan).
- **Cross-network reports ("Custom Analytics"):** Scale only. One dashboard filtered by platform, profile, post type, time range and post or campaign tags. It covers Instagram, Facebook, TikTok, Pinterest and Threads; **"LinkedIn, YouTube, and Snapchat aren't included."** Reports download "in multiple formats", but the formats are not named, so PDF is UNVERIFIED (https://help.later.com/hc/en-us/articles/33109662792471-Later-s-Custom-Analytics).
- **Competitor benchmarks:** Scale only, Instagram only (see item 6).
- **Custom, white-label and PDF reports:** No white-label reporting was found. Later's own blog points readers to Sendible and SocialPilot for white-label work (https://later.com/blog/social-media-scheduling-tools/). CSV export: Instagram on Starter+, LinkedIn on Growth+ (https://help.later.com/hc/en-us/articles/360043246313-Export-Later-s-Instagram-Analytics-as-a-CSV, https://help.later.com/hc/en-us/articles/33639033081367-Later-s-LinkedIn-Analytics).
- **Scheduled report delivery:** Scale only. Custom Analytics can email a chart on a set cadence (the example is every 24 hours) to any address, and recipients do not need a Later account. It can also email threshold-based **alerts**. Separately, a **weekly Instagram analytics summary email** goes to account admins on Starter+ (https://help.later.com/hc/en-us/articles/33109662792471-Later-s-Custom-Analytics, https://help.later.com/hc/en-us/articles/360042770354-Instagram-Analytics-Weekly-Summary-Email, plan reference guide).
- **ROI / UTM tracking:** UTMs and click analytics only through Link in Bio (page views, button and post clicks, CTR, CSV). Pinterest click tracking is on Growth+. No conversion or revenue tracking was found (https://help.later.com/hc/en-us/articles/360042743234-Link-in-Bio-Analytics, https://later.com/social-media-analytics/).
- **Paid plus organic:** Scale only, with a connected Meta ad account (see item 10).

### 8. Collaboration and approvals

Source for this item: https://help.later.com/hc/en-us/articles/40954654500375-Team-Collaboration-and-Content-Approvals (updated 2026-07-08), unless another source is given.

- **Plans:** Collaboration and approvals are Growth+ and desktop only. They do not cover Instagram Stories. The Notification Center is Scale only.
- **Roles:** Account Owner, Reviewer, Member and Member (Restricted). Owners can approve, change account settings, manage roles and invite users. Reviewers can approve. Members can request approval and can also schedule without approval. Member (Restricted) can comment, submit for approval and save drafts, but **cannot schedule without approval**. "Custom roles and permissions" appears on the Growth plan in pricing (https://later.com/pricing/), but the help article describes only these four fixed roles plus per-Access-Group toggles for Reviewer and "Publish Without Approval".
- **Critical behaviour:** **"Approval status doesn't block publishing."** A scheduled post goes live at its time whether or not it was approved, unless it is moved back to draft. To enforce approval, save posts as drafts, or remove "Publish Without Approval" from a user so that they can only save drafts. A Reviewer's own "Publish Without Approval" permission cannot be switched off.
- **Internal approval:** A request-approval flow. Comments and an activity log with submission and approval timestamps live in the Post Builder, and you can "Request changes".
- **External client approval:** "Send posts" creates a review link for people without a Later account. You share it by copying the link or entering an email address. **Links expire after 7 days and have no password.** Reviewers enter a name and can leave comments of up to 500 characters. Growth+. A separate shared-calendar link (Growth+) expires after 48 hours, shows scheduled posts only, and cannot take approvals or edits in Later (https://help.later.com/hc/en-us/articles/360042743434-Share-Your-Instagram-Content-Calendar).
- **Notes and tasks:** Calendar Notes and Share Calendar on Growth+ (plan reference guide). No task management was found.
- **Audit trail:** Only the per-post activity log of comments and submission and approval timestamps. No account-wide audit log was found (UNVERIFIED).
- **Notifications:** Approval requests are emailed to all Reviewers; approvals are emailed to the requester. Comments do not trigger emails. In-app notifications are Scale only.

### 9. Agency / multi-client

- **Workspaces = Access Groups + Social Sets.** A Social Set holds at most one profile per platform, so two Instagram accounts need two sets (https://help.later.com/hc/en-us/articles/360059362253-Choose-a-Later-Social-Plan). An Access Group separates profiles, media library, saved captions and users, for example per client. There is 1 Access Group on Free and Starter and unlimited on Growth+, and only the owner can create them (https://help.later.com/hc/en-us/articles/360042609854-Create-Manage-Access-Groups).
- **Client access:** You can invite a client as a user restricted to their Access Group (https://help.later.com/hc/en-us/articles/360042609854-Create-Manage-Access-Groups), or send them external review links (item 8). Each email address can belong to only one Later account (https://help.later.com/hc/en-us/articles/360042662754-Add-Remove-Users-from-Later).
- **Client portal:** None found. UNVERIFIED; assume absent.
- **White-labelling:** None found. Later's blog points to Sendible and SocialPilot for white-label (https://later.com/blog/social-media-scheduling-tools/).
- **Per-client billing:** None found. There is one subscription per account, extended with Social Set and user add-ons (https://help.later.com/hc/en-us/articles/360059362253-Choose-a-Later-Social-Plan).
- Later markets Scale as "For Enterprise-level analytics — without the enterprise contract" and describes it as designed for agencies. Package variants exist, for example "Scale 15" and "Scale 30" (https://later.com/pricing/, https://help.later.com/hc/en-us/articles/360059362253-Choose-a-Later-Social-Plan). The agencies page (https://later.com/agencies-freelancers/) is positioned around influencer marketing.

### 10. Ads

- **No ad creation, management or post boosting** in Later Social. Its only paid feature is **reporting**: on Scale, you connect a Meta ad account to compare paid and organic performance for boosted Facebook and Instagram posts in Custom Analytics. Data starts from the date of connection and is not backfilled (https://help.later.com/hc/en-us/articles/42677693975319-Connect-a-Meta-Ad-Account-to-Later, https://help.later.com/hc/en-us/articles/33109662792471-Later-s-Custom-Analytics). Pricing lists "Meta Ads analytics" under Scale (https://later.com/pricing/).
- **Partnership Ads (creator side):** A brand can ask to boost a creator's Instagram post. "You approve or deny every request. No post can be boosted without your permission" (https://help.later.com/hc/en-us/articles/35471556976407-Partnership-Ads-A-New-Way-to-Grow-Your-Reach).

### 11. Email

- **Email marketing or newsletters:** **None.** Later does not send newsletters, and no "post as email" feature was found.
- **Email capture:** The Link in Bio page can show a sign-up form that collects visitors' emails "for newsletters, marketing, and promotions". You get them by CSV export (the download is available for 24 hours, and unsubscribes are not removed) or through a **Mailchimp** integration that adds them to your Mailchimp audience. This is on all plans, including Free (https://help.later.com/hc/en-us/articles/7899653024407-Collect-Emails-With-Link-in-Bio, plan reference guide). Mailchimp is listed as a technology partner (https://later.com/partners/).
- **Report delivery by email:** A weekly Instagram analytics summary email to admins (Starter+). On Scale, scheduled Custom Analytics report emails and threshold alerts go to any address (https://help.later.com/hc/en-us/articles/360042770354-Instagram-Analytics-Weekly-Summary-Email, https://help.later.com/hc/en-us/articles/33109662792471-Later-s-Custom-Analytics).
- **Approval by email:** Approval requests are emailed to Reviewers, and approvals are emailed to the requester. External review links can be "invited" by email address, but the approval itself happens on the web link, not by replying to the email (https://help.later.com/hc/en-us/articles/40954654500375-Team-Collaboration-and-Content-Approvals).
- **Email inbox integration:** None found. Social Inbox covers only social DMs and comments.

### 12. Automation and AI agents

- **What acts without a person:**
  - **Auto Publish** of scheduled posts (item 2). Instagram first comments post automatically with Auto Publish posts.
  - **Best Time to Post** is "auto-enabled if your profile is eligible" (https://later.com/social-media-scheduler/).
  - **Scheduled report emails and alerts** (Scale).
  - **Collect** gathers UGC automatically.
- **Not offered:** No auto-replies, chatbots, keyword auto-hide or AI agent were found in Later Social (https://later.com/social-media-ai-tools/, https://help.later.com/hc/en-us/articles/31010972850199-Manage-DMs-Comments-in-Later-with-Social-Inbox). Later's "EdgeAI" is an influencer-marketing engine that analyses creator behaviour, trends and purchase data. It is not a social-reply agent (https://later.com/).
- **Guardrails:**
  - AI captions are always inserted by a person. A generated caption is appended to the post's caption, so a person must edit and schedule it. Later disclaims the AI output: "Later is not responsible for any content generated" (https://help.later.com/hc/en-us/articles/12252569781015-Later-s-Caption-Writer).
  - The approval workflow does **not** block publishing. The only hard gate is denying "Publish Without Approval", which leaves those users able to save drafts only (https://help.later.com/hc/en-us/articles/40954654500375-Team-Collaboration-and-Content-Approvals).
  - On the creator side, Partnership Ads cannot boost a post without the creator's explicit approval (https://help.later.com/hc/en-us/articles/35471556976407-Partnership-Ads-A-New-Way-to-Grow-Your-Reach).

### 13. Integrations and API

- **Canva:** Yes, export to the Later media library, and Canva in Link in Bio (https://help.later.com/hc/en-us/articles/360043361213-Uploading-Media-Format-Requirements).
- **Dropbox:** Yes (same article).
- **Google Drive:** **No.** "Google Drive can't be connected to your Later Media Library" (same article).
- **Zapier:** **No.** Listed as unsupported (https://help.later.com/hc/en-us/articles/360060842914-Supported-Social-Platforms-Post-Types). Zapier's own listing page for Later.com returned 404 when checked.
- **Slack, Bitly and CRM:** None found. HubSpot and ClickUp are listed as unsupported (same article). Slack and Bitly: UNVERIFIED, no mention found.
- **Mailchimp:** Yes, for Link in Bio email capture (item 11).
- **Shopify:** Instagram Shop product tagging on Growth+ (plan reference guide). A direct Shopify integration is UNVERIFIED.
- **Partners page** (https://later.com/partners/): Google, Dropbox, Adobe, Mailchimp, Canva, Bazaarvoice, Yotpo, PowerReviews and Everflow. The review and affiliate partners serve Later Influence.
- **Public API:** **No public API for Later Social was found.** Every mention of "API" in Later's help center refers to the social platforms' APIs (Zendesk search, https://help.later.com/api/v2/help_center/articles/search.json?query=API). Later Influence has a **Reporting API** for campaign and creator data (https://docs.reporting.api.later.com/). Its clientId and clientSecret are issued by the Later account team; it is not self-serve.
- **Mobile apps:** iOS and Android. You can schedule, upload, view the calendar, set up Link in Bio and manage Auto Publish. **Analytics, the inbox (Social Inbox / Conversations), Collect, and account and billing are desktop only.** The iOS app sells plans through Apple at local-currency prices ending in .99, without add-ons or coupons (https://help.later.com/hc/en-us/articles/360043244813-A-Guide-to-the-Later-Mobile-App, https://help.later.com/hc/en-us/articles/360059362253-Choose-a-Later-Social-Plan).

### 14. Pricing

Prices are in USD, and "applicable taxes are extra" (https://later.com/pricing/, checked 2026-11-18).

| Plan | Annual billing (per month) | Monthly billing (per month) | Social Sets (profiles) | Users | Posts per profile per month | AI credits per month | Inbox |
|---|---|---|---|---|---|---|---|
| Free | $0 | $0 | 1 | 1 | 12 (drafts count) | 0 | Preview only |
| Starter | **$18.75** | $25 (third-party) | 1 (8 profiles) | 1 | 30 | 5 | No (preview only) |
| Growth | **$37.50** | $50 (third-party) | 2 (16 profiles) | 2 | 180 | 50 | Yes |
| Scale | **$82.50** | $110 (third-party) | 6 (48 profiles) | 4 | Unlimited | 100 | Yes |

- **Sources for the table:**
  - Annual prices, Social Sets, users, posts and AI credits: https://later.com/pricing/
  - Free plan limits: https://help.later.com/hc/en-us/articles/40920284785687-Reference-Guide-Plan-Availability-for-Later-Social
  - Monthly-billing prices: **not shown in the text Later's pricing page returned** (the page has a monthly/yearly toggle). The $25, $50 and $110 figures come from SocialPilot, "prices last checked on Later's pricing page and help center on September 25, 2026" (https://www.socialpilot.co/blog/later-pricing). They match the 25% annual discount Later states.
- **Annual discount:** The pricing page says "3 months free | 25% off" (https://later.com/pricing/). Later's own help article says "Annual plans include 4 months free" (https://help.later.com/hc/en-us/articles/360059362253-Choose-a-Later-Social-Plan). The two Later pages disagree.
- **Add-ons (Growth and Scale only; none on Starter; desktop sign-ups only):**
  - Extra Social Set: $11.25 a month (annual billing) or $15 (monthly)
  - Extra user: $3.75 or $5
  - 100 AI credits: $3.75 or $5

  Annual prices are from https://later.com/pricing/; monthly prices are from https://www.socialpilot.co/blog/later-pricing.
- **Free plan:** **Exists, but the pricing page does not show it.** It has limited scheduling (12 posts per profile per month), a 2-week calendar window, Link in Bio, introductory analytics and 1 Access Group. Cancelling a paid plan or trial drops you to Free (https://help.later.com/hc/en-us/articles/360059362253-Choose-a-Later-Social-Plan, https://help.later.com/hc/en-us/articles/360042992233-Cancel-Your-Later-Plan).
- **Free trial:** **14 days** on any paid plan. It is not available to accounts that have had a trial or paid plan before (https://help.later.com/hc/en-us/articles/360060127733-FAQ-About-Trials, https://help.later.com/hc/en-us/articles/360042493234-Upgrading-to-a-Paid-Plan). Sources conflict on whether a card is needed. Feature pages say "No credit card required" (https://later.com/social-media-inbox/, https://later.com/social-media-competitor-analysis/). The trial FAQ (updated 2025-12-03) says checkout takes card details, "You won't be charged for your free trial", and the plan converts to paid automatically unless cancelled. Treat this as UNVERIFIED.
- **Other pricing points:**
  - Non-profit discount on Growth annual plans only (plan reference guide).
  - Enterprise influencer programmes are sales-led ("Let's talk strategy") (https://later.com/pricing/).
  - Legacy and package plans (Scale 15, Scale 30, Scale202506, legacy Enterprise with SSO, Topics and Brand DNA) differ from the table (https://help.later.com/hc/en-us/articles/360059362253-Choose-a-Later-Social-Plan).
- **PayPal:** **Not accepted.** "We don't accept PayPal payments, Interac payments, or bank transfers." Later accepts major credit and debit cards (Visa, Visa Debit, Mastercard, Amex). Google Pay and Apple Pay are available only when starting a new trial. iOS sign-ups are billed through Apple (https://help.later.com/hc/en-us/articles/360042493054-Accepted-Payment-Methods).

### 15. Onboarding

1. **Sign up** on desktop at app.later.com (email and password; passwords need at least 10 characters with a number, letter and symbol; 2FA is optional), or in the iOS or Android app. Then start the 14-day trial with "Start 14-Day Free Trial", or stay on Free (https://help.later.com/hc/en-us/articles/360059362253-Choose-a-Later-Social-Plan, https://help.later.com/hc/en-us/articles/360046487214-Later-Account-Security-Password-Management). Whether Google, Apple or Facebook sign-in is offered is UNVERIFIED.
2. **Connect profiles.** From the Calendar, click "+" on an empty profile slot (or go to Settings > Social Sets & Access > Add Social Profile), choose the platform, and "follow the prompts to log in and authorize Later". On mobile, go to Account > Social Sets > Connect (https://help.later.com/hc/en-us/articles/360043244733-Add-Remove-Transfer-Social-Profiles-in-Later, updated 2026-10-28).
3. **Account requirements per platform:**
   - **Instagram:** must be a Business or Creator account. Personal profiles are no longer supported because Meta deprecated the Instagram Basic Display API (https://help.later.com/hc/en-us/articles/26417547154711-Why-You-Should-Switch-to-an-Instagram-Business-or-Creator-Profile).
   - **Instagram Login:** Instagram can connect through Instagram Login without a Facebook Page. Linking a Facebook Page is optional, but some features need it, such as location tagging, Collect and Link in Bio (https://help.later.com/hc/en-us/articles/360043244733-Add-Remove-Transfer-Social-Profiles-in-Later, https://help.later.com/hc/en-us/articles/360061664373-Using-Later-as-an-Account-User).
   - **Facebook Pages:** need page-admin rights.
   - **LinkedIn Pages:** need super-admin rights.
   - **TikTok and Pinterest:** need only basic login (https://help.later.com/hc/en-us/articles/360043244733-Add-Remove-Transfer-Social-Profiles-in-Later).
4. Connections expire periodically and are refreshed by logging in again (https://help.later.com/hc/en-us/articles/360043244093-Refresh-Your-Social-Profile-Connection).

**Developer apps and API keys: none for Later Social.** Every connection is an in-app login and authorisation through Later's own platform integrations. No Later Social help article asks customers to create a developer app, API key or token (articles above, and https://help.later.com/hc/en-us/articles/360043246993-Getting-Started-With-Later-Social: "Later connects to your social profiles through their respective APIs").

The only "bring your own credentials" cases are in **Later Influence** (enterprise):
- The PowerReviews integration needs Write API keys, which the brand requests from its PowerReviews representative.
- The Yotpo integration needs the brand's Yotpo credentials, sent to Later's implementation manager.

Source for both: https://help-influence.later.com/hc/en-us/sections/32932443117847-Integrations

### 16. Distinctive points

- **Later is moving towards influencer marketing.** Its homepage leads with influencer programmes and "Later EdgeAI" (https://later.com/). It bought the creator-affiliate platform Mavely for $250M in January 2025 (https://www.summitpartners.com/news/later-acquires-mavely-for-250-million-unlocking-new-opportunities-for-marketers-and-creators-to-maximize-their-return-on-social). Much of its 2026 release activity is in Later Influence (https://releasebot.io/updates/later).
- **Visual, Instagram-first heritage:** a drag-and-drop feed planner, Link in Bio with email capture, and UGC "Collect" (https://later.com/social-media-scheduler/).
- **It dropped X entirely** (August 2025) and never added Bluesky, Mastodon, Google Business Profile, Reddit or Tumblr. Its network list is narrow: 8 platforms, YouTube Shorts only (https://help.later.com/hc/en-us/articles/360060842914-Supported-Social-Platforms-Post-Types).
- **Pricing is per "Social Set"** (one profile per platform), with **post caps per profile** in which drafts count, plus metered **AI credits**. An inbox, approvals and listening all need Growth ($37.50+ a month on annual billing) or above (https://later.com/pricing/).
- **The engagement inbox is thin:** beta, desktop only, Instagram, Facebook and TikTok only, with no AI or saved replies, no auto-moderation, no reviews and a 7-day window for Instagram DMs (https://help.later.com/hc/en-us/articles/31010972850199-Manage-DMs-Comments-in-Later-with-Social-Inbox). This is a gap that a product built around AI-drafted, human-approved replies would fill directly.
- **Approvals do not gate publishing** unless users are stripped of "Publish Without Approval" (https://help.later.com/hc/en-us/articles/40954654500375-Team-Collaboration-and-Content-Approvals). A newcomer that makes "nothing posts without a human approving it" a hard guarantee would be different.
- **No public API or Zapier for Later Social. No PayPal. Google Drive is not supported.** Sources: https://help.later.com/hc/en-us/articles/360060842914-Supported-Social-Platforms-Post-Types and https://help.later.com/hc/en-us/articles/360042493054-Accepted-Payment-Methods.
- **Help-center inconsistencies** show up in several places:
  - Annual discount: 3 months free vs 4.
  - Trial: whether a card is required.
  - Credit add-ons on Starter.
  - LinkedIn carousels.
  - Hashtag limits: 30 vs 5.

  These are worth noting as signs of fast change, and as a reason to read Later's live pages rather than older third-party reviews. Sources: https://later.com/pricing/, https://help.later.com/hc/en-us/articles/360059362253-Choose-a-Later-Social-Plan, https://help.later.com/hc/en-us/articles/360043099194-Schedule-a-First-Comment-on-Instagram.


---

## Metricool

Checked 2026-11-18. Sources are Metricool's own pages (metricool.com, help.metricool.com, product-update posts) unless marked otherwise. Where two Metricool pages disagree, both are given. "UNVERIFIED" means no source could confirm the point.

### 1. Supported networks

Metricool lists 11 networks: Instagram, TikTok, YouTube, Facebook, X/Twitter, LinkedIn (profiles and company pages), Threads, Bluesky, Pinterest, Twitch and Google Business Profile (https://metricool.com/llms.txt; the same list is in the homepage menu, https://metricool.com/). The help center also has connection articles for a website or blog, Meta Ads, Google Ads and TikTok Ads, and none for Mastodon, Reddit, Tumblr, Nextdoor or WhatsApp (https://help.metricool.com/connecting-by-social-network-jidia).

Publishing guides: https://help.metricool.com/publishing-by-social-network-c8f90. Inbox coverage: https://help.metricool.com/inbox-manager-how-to-manage-messages-and-comments-from-metricool-s9zze. Analytics articles: https://help.metricool.com/metrics-by-social-network-dnxxf.

| Network | Supported? | Publishing | Inbox / engagement | Analytics |
|---|---|---|---|---|
| Facebook | Yes, Pages only ("third-party API only supports Facebook Pages", https://help.metricool.com/getting-started-with-metricool-im4va) | Yes: posts and Reels. No polls, no direct posting to Groups (only sharing a link), no mixed photo+video posts (https://help.metricool.com/en/article/api-limitations-per-social-network-508ay5/) | Yes: comments and private messages | Yes, organic and paid |
| Instagram | Yes, professional (business or creator) accounts only; no personal accounts (https://help.metricool.com/how-to-connect-instagram-to-metricool-sermi) | Yes: feed posts, carousels (up to 10 items), Reels, Trial Reels and Stories auto-publish. Stories with links, mentions or stickers, and Reels with outside audio, go out as a phone notification instead (https://help.metricool.com/schedule-and-post-on-instagram-6b6q5) | Yes: comments and DMs. DM replies are allowed for 24 hours when connected through Instagram login, 7 days when connected through Facebook | Yes, including Collab metrics |
| Threads | Yes | Yes (https://help.metricool.com/schedule-and-post-on-threads-pj8pq) | No; not listed among the Inbox networks | Yes (https://help.metricool.com/threads-metrics-90qhd) |
| X (Twitter) | Yes, paid plans only, through a per-account paid add-on; not on Free (https://metricool.com/pricing/) | Yes, including polls and threads | Partly: private messages only, per the Inbox article | Yes. "Complete Twitter/X analytics" is listed under Advanced (https://metricool.com/pricing/) |
| LinkedIn company page | Yes, paid plans only. Needs super-admin rights (https://help.metricool.com/how-to-connect-linkedin-to-metricool-jq80g) | Yes, including polls and PDF carousels. No articles, events or co-authored posts | Yes: comments and mentions. No private messages | Yes (https://help.metricool.com/linkedin-company-page-metrics-dfpdp) |
| LinkedIn personal profile | Yes, paid plans only. A brand can hold the profile or a page, not both (https://help.metricool.com/how-to-connect-linkedin-to-metricool-jq80g) | Yes | Yes: comments and mentions on the profile's posts | Yes (https://help.metricool.com/linkedin-personal-profile-metrics-h3usk) |
| TikTok | Yes | Yes: videos and photo posts. No Stories, overlay text or Shop tags (API-limitations page) | Partly: comments on Business accounts only. No DMs | Yes. No audience demographics; other TikTok accounts cannot be tracked as competitors |
| YouTube | Yes | Yes: videos and Shorts. No Community posts | Yes: comments on long videos and Shorts | Yes |
| Pinterest | Yes | Yes. No Story Pins and no private boards | No; not listed | Yes |
| Google Business Profile | Yes. One location per Metricool brand (https://help.metricool.com/whats-a-brand-in-metricool-and-how-does-it-work-5l78b) | Yes: posts, and video posts since October 2026 (https://metricool.com/it/novita-metricool-ottobre-2026/). CTAs cannot be edited and only one image or video per post | Yes: reviews (read and reply) | Yes (https://help.metricool.com/google-business-profile-metrics-1bg2y) |
| Nextdoor | **No.** Not in any Metricool network list or connection article | No | No | No |
| Bluesky | Yes. Only `.bsky.social` handles; custom-domain handles cannot connect (https://help.metricool.com/how-to-connect-bluesky-to-metricool-y9d7z) | Yes. No polls, quote posts or editing (https://help.metricool.com/schedule-and-publish-on-bluesky-0zzn8) | Not in the Inbox article's network list. The API-limitations page says only that Bluesky DMs cannot be sent. Whether Bluesky comments reach the Inbox is UNVERIFIED | Yes (https://help.metricool.com/bluesky-metrics-gmvk3) |
| Mastodon | **No** (absent from the lists above) | No | No | No |
| Reddit | **No** | No | No | No |
| Tumblr | **No** | No | No | No |
| WhatsApp | **No** | No | No | No |
| Twitch (extra) | Yes | No publishing guide exists; analytics only | No | Yes, and up to 100 Twitch competitors (https://metricool.com/twitch-with-metricool/) |
| Website or blog (extra) | Yes: JavaScript snippet, WordPress plugin or tracking pixel (https://help.metricool.com/how-to-connect-your-website-or-blog-to-metricool-ejuy1) | n/a | n/a | Yes: visits, country, page, and blog posts read from RSS |
| Meta Ads, Google Ads, TikTok Ads (extra) | Yes | See item 10 | n/a | Yes |

Plan gating for networks: Free covers 1 brand and "all networks except LinkedIn and Twitter/X". LinkedIn needs Starter or higher, and X needs a paid add-on (https://metricool.com/pricing/; https://help.metricool.com/main-differences-between-free-and-paid-plans-bl0v9). A brand holds one profile per network (https://help.metricool.com/whats-a-brand-in-metricool-and-how-does-it-work-5l78b).

### 2. Publishing

- **Scheduling and content calendar:** The calendar has Day, Week and Month views. It filters by network, publication status (Published, Pending, Draft, Error, Autolist), approval status (Advanced and Custom only) and notes (https://help.metricool.com/how-to-manage-your-calendar-in-metricool-xhlbw). It can also show "Social Calendars", meaning highlighted and imported dates (same source).
- **Queue / time slots:** Autolists are weekly grids of time slots, filled from a list of posts (https://help.metricool.com/schedule-content-from-an-autolist-fxehk). Recurring posts are also supported (https://help.metricool.com/optimize-your-scheduling-ste72).
- **Best-time suggestions:** Yes, as a heatmap. It uses the account's own API data for Instagram (linked to a Facebook Page, 100+ followers), Facebook, X (300+ active followers) and TikTok Business. LinkedIn, YouTube and some other accounts get "behavioral studies" instead. Best time for X is Advanced and Custom only (https://help.metricool.com/best-time-to-post-on-social-media-in-metricool-w7ll9).
- **Bulk / CSV upload:** Yes. CSV import works from the calendar (one or several brands) or into an Autolist, and scheduled posts can be exported as CSV (https://help.metricool.com/how-to-schedule-posts-in-batch-with-a-csv-file-in-metricool-3zwxj; https://metricool.com/planner/).
- **Per-network text customization:** Yes, through "Multiposting: edit per social network" (https://help.metricool.com/creating-scheduling-content-e962r). There are also per-network alt text and presets (https://metricool.com/planner/).
- **First comment:** Yes, but only together with a post. Supported on Instagram (posts and Reels), Facebook (posts and Reels), LinkedIn (regular posts), TikTok (professional pages) and YouTube (videos and Shorts) (https://help.metricool.com/how-to-schedule-a-first-comment-on-your-posts-pwm25).
- **Stories, Reels and carousels:** Yes. Instagram covers feed, carousel, Reels, Trial Reels and Stories (some go by notification, see item 1). LinkedIn has polls and carousels. X has polls and threads (https://help.metricool.com/publishing-by-social-network-c8f90).
- **Drafts:** Yes. "Save draft" keeps a post unscheduled, and drafts show grey in the calendar (https://help.metricool.com/how-to-manage-your-calendar-in-metricool-xhlbw).
- **Evergreen / recycling:** Yes. With "repeat" on, an Autolist is circular: "Once content is published, it moves to the end of the list". Limits are 200 posts per Autolist and 10 attachments per post (https://help.metricool.com/schedule-content-from-an-autolist-fxehk).
- **Content categories:** A Tags system organizes content. Tags can be applied by AI and imported or exported by CSV (https://help.metricool.com/ category "Tags system", e.g. https://help.metricool.com/how-to-auto-tag-content-with-ai-chz0c).
- **RSS / blog auto-posting:** Yes. An RSS feed is linked to an Autolist and fills it automatically. Options are "include previous posts", "disable posts until manually activated" and "add new posts to the beginning". Images are pulled only for Instagram and Pinterest (https://help.metricool.com/how-to-link-an-rss-feed-to-an-autolist-ank6m; https://help.metricool.com/schedule-content-from-an-autolist-fxehk).
- **Media library:** There is a "Posts Library" (https://help.metricool.com/posts-library-u05cj), Google Drive and Canva pickers, and "our free image and video bank" (https://metricool.com/planner/). A stand-alone asset library is UNVERIFIED. The Posts Library is not on Free (https://help.metricool.com/main-differences-between-free-and-paid-plans-bl0v9).
- **Link shortener / UTM:**
  - The UTM builder works while planning: "Generate UTMs and trackable links" (https://metricool.com/planner/; https://help.metricool.com/utm-builder-while-planning-your-content-qm0de).
  - Links are shortened automatically at publish time on a Metricool domain (e.g. t.mtrbio.com), and the shortener "doesn't track anything" (https://help.metricool.com/scheduling-content-with-links-6kqha).
- **Link-in-bio:** Yes, as SmartLinks. They have themes, a custom URL and visit/click/CTR analytics (https://metricool.com/socialmedia-smartlinks/). "Multiple link-in-bio" is a Starter feature (https://metricool.com/pricing/).
- **Other:** Notes on planned posts (https://help.metricool.com/how-to-add-notes-in-the-planning-4aueg); duplicating posts; recovering deleted posts; LinkedIn PDF posts; a Chrome extension (https://metricool.com/integrations/).
- **Timezone handling:** UNVERIFIED.

### 3. Content creation

- **AI writer ("AI social media assistant" / AI Text Generator):**
  - It writes a draft from a topic. You choose the tone (formal, informal, creative), the language and optionally a target network. It can then add hashtags, shorten or lengthen the text, or add a CTA, and it can write a 5-post thread for X or Threads. It runs in the calendar and in Autolists (https://help.metricool.com/how-to-create-posts-with-metricools-ai-text-generator-sv0j8).
  - Per-brand, per-network "AI Configuration" instructions customize the prompts (https://help.metricool.com/how-to-set-up-ai-prompt-customization-in-metricool-bl6y7). Since October 2026, "Generate with AI" writes those instructions from the brand's own published posts (https://metricool.com/it/novita-metricool-ottobre-2026/).
  - **Credits:** Free 5 per month; Starter 20 per brand per month; Advanced 35 per brand per month; Custom 20 per brand, adjustable. Extra credits cannot be bought and unused credits do not roll over (https://help.metricool.com/how-to-create-posts-with-metricools-ai-text-generator-sv0j8).
  - AI image or video generation: UNVERIFIED, with no Metricool source found. Competitors state the AI is text-only (https://contentstudio.io/metricool-alternative), which is not independent evidence.
- **Other AI:** AI alt text for images; AI auto-tagging; labelling AI-generated content for the EU AI Act (https://help.metricool.com/creating-scheduling-content-e962r). Metricool Studio is an AI report builder (item 7).
- **Repurposing long content (blogs, video) into posts:** No AI repurposing feature was found (UNVERIFIED). The nearest feature is RSS to Autolist, which turns new blog posts into social posts with a prefix or suffix (item 2).
- **Templates:** Saved post templates and text snippets: "Save ready-to-use post templates and texts" (https://metricool.com/planner/; https://help.metricool.com/how-to-save-texts-zedge).
- **Hashtag tools:**
  - The AI suggests hashtags (https://help.metricool.com/how-to-create-posts-with-metricools-ai-text-generator-sv0j8).
  - "Hashtag Analysis" appears in the plan comparison (https://metricool.com/pricing/).
  - The paid Hashtag Tracker is covered in item 6.
  - Instagram auto-publishing allows only 5 hashtags per post (https://help.metricool.com/schedule-and-post-on-instagram-6b6q5).
- **Image / video editing:** Built in. Images can be cropped, adjusted, filtered, given stickers, drawn on, framed, blurred or pixelated, and resized. Videos can be muted, split, merged, trimmed and filtered (https://help.metricool.com/edit-images-and-videos-while-scheduling-your-content-wputz).
- **Design integrations:**
  - **Canva:** "Search, export, and schedule your designs without leaving the planner" (https://metricool.com/integrations/).
  - **Adobe Express:** Templates and Adobe Stock (same page).
  - **Google Drive** (same page).
  - Canva and Google Drive are listed as Starter features (https://metricool.com/pricing/), and the help center says they are "unavailable on Free" (https://help.metricool.com/main-differences-between-free-and-paid-plans-bl0v9). The pricing page's add-on block, however, lists Canva as "For free" on all plans (https://metricool.com/pricing/). The two sources conflict.

### 4. Engagement

Source for this item, unless noted: https://help.metricool.com/inbox-manager-how-to-manage-messages-and-comments-from-metricool-s9zze.

- **Unified inbox — what it covers:**
  - Facebook: comments and private messages.
  - Instagram: comments and private messages.
  - TikTok: comments, Business accounts only.
  - X: private messages only.
  - LinkedIn: comments and mentions, for company pages and personal profiles.
  - Google Business Profile: reviews.
  - YouTube: comments.
  - Mentions appear only for LinkedIn, and reviews only for Google Business Profile.
- **Free plan:** The Free plan includes it: "manage messages from your networks" (https://metricool.com/).
- **Assignment to team members: No.** "the Inbox does not include message assignment or user tagging". Collaborators share one Inbox per brand and coordinate through private notes on conversations (https://help.metricool.com/adding-notes-in-the-inbox-mnka8). Posts can be assigned in the Planner, but inbox conversations cannot (https://metricool.com/product-updates-september-2026/).
- **Saved replies:** Yes, as "Saved Answers": templates with text and tags (https://help.metricool.com/inbox-saved-answers-nught).
- **AI-suggested replies:** None found in the Inbox article or the 2026 product updates (UNVERIFIED that any exist). The only AI route is exporting comments through the API to an outside AI tool.
- **Automatic replies:** **Not in the Inbox**: "Metricool does not support automatic replies or auto-reply templates." They exist only as Flows, a separate paid add-on (item 12), for Instagram, TikTok and X.
- **Moderation rules:**
  - The Inbox cannot hide or delete anything: "Deleting or hiding comments and messages is not possible from Metricool".
  - A TikTok Flow can "automatically hide TikTok comments that contain banned words" (https://metricool.com/message-moderation; https://help.metricool.com/what-are-flows-nfody).
  - No keyword auto-hide was found for Facebook or Instagram (UNVERIFIED).
- **Workflow states:** Read/unread (not synced with the networks) and resolved/unresolved. Filters by network, message type and status, plus keyword search (https://metricool.com/social-media-inbox/).
- **Sentiment:** None found (UNVERIFIED).
- **Contact / CRM history: No.** "The Inbox does not store history". It reloads from each network every time it opens.
- **Other limits:**
  - The Inbox works per brand only; there is no unified multi-brand view.
  - There are no push or email notifications for new messages.
  - Ad comments cannot be replied to.
  - API comment export covers only the 50 most recent posts per network.

### 5. Reviews

- **Google Business Profile reviews:** Yes, read and reply in the Inbox. "The Inbox manages reviews, not comments". Because of Google API changes the Manager role may not be enough, and an owner role may be needed (https://help.metricool.com/comments-or-messages-not-appearing-in-inbox-tmofe). Limits: reviews cannot be flagged, reviews with images do not sync, and reviews cannot be liked (https://help.metricool.com/en/article/api-limitations-per-social-network-508ay5/).
- **Facebook reviews / recommendations:** Not mentioned in any Inbox article; only Facebook comments and private messages are listed (UNVERIFIED, likely no).
- **Yelp, TripAdvisor, Trustpilot or any other review site:** No. None appears in any Metricool network list or connection article (https://help.metricool.com/connecting-by-social-network-jidia).

### 6. Social listening

- **Keyword / brand monitoring:** No general listening product. The API-limitations page says Facebook cannot "listen for brand mentions" (https://help.metricool.com/en/article/api-limitations-per-social-network-508ay5/).
- **Hashtag Tracker:** Pay-per-use and not included in any plan, at **€25 per day per network** (buy 4 days, get 1 free).
  - On X it tracks a hashtag, a keyword or an @account, with up to 7 days of look-back.
  - On Instagram it tracks one hashtag per session, from creation onward, in feed posts and Reels only.
  - Data refreshes every 8 hours.
  - X tracking needs the X add-on (https://help.metricool.com/hashtag-tracker-4hux8).
  - The USD pricing table shows "$25/day" (https://metricool.com/pricing/).
- **Competitor tracking:**
  - Plan limits: up to 100 competitors on Starter and above; 5 on Free, which cannot be changed (https://metricool.com/pricing/; https://help.metricool.com/main-differences-between-free-and-paid-plans-bl0v9).
  - Networks: the marketing page lists Instagram, Facebook, X, YouTube and Twitch (https://metricool.com/competitors/). The Free-plan help article lists Facebook, Instagram, Bluesky and Twitch.
  - LinkedIn company pages and TikTok accounts cannot be tracked as competitors (https://help.metricool.com/en/article/api-limitations-per-social-network-508ay5/).

### 7. Analytics and reporting

- **Per-network metrics:** Facebook, Instagram (including Collabs), Threads, X, Bluesky, LinkedIn profile and page, Pinterest, TikTok, Google Business Profile, YouTube, Twitch, the website, Meta Ads, Google Ads and TikTok Ads (https://help.metricool.com/metrics-by-social-network-dnxxf). History is 30 days on Free and unlimited on paid plans (https://metricool.com/pricing/).
- **Cross-network reports:** Reporting builds one report across the selected platforms ("No social media platform selected: a summary will be created for all platforms"), downloadable as PDF or PPT, in a chosen language, with an optional logo. Reports are Starter and above (https://help.metricool.com/how-to-generate-reports-szc5k).
- **Custom templates:** Advanced and Custom only (same source; https://metricool.com/pricing/).
- **White-label reports:** Adding a logo is possible on any report plan. Full white label is the Custom plan (item 9).
- **Scheduled report delivery:** An "Automatic monthly report" goes by email to one or more addresses. It has a "Send replies to" address, a chosen sending day, a test send, and a PDF link that "does not expire". Starter and above (https://help.metricool.com/how-to-generate-reports-szc5k). A separate fixed **monthly summary** email goes to the owner on all plans, with organic data only (https://help.metricool.com/how-to-activate-your-monthly-summary-in-metricool-0zjmf).
- **Competitor benchmarks:** In the Competitors section (item 6). Metricool Studio can analyze competitors' Reels (https://metricool.com/metricool-studio/).
- **Metricool Studio (launched May 2026):** An AI report builder that works from plain-language requests. Reports include an executive summary and charts, and are shared as a live URL that viewers can open without an account (https://metricool.com/metricool-studio/; https://metricool.com/press-release-metricool-studio/). Full Studio access is part of the Advanced Analytics add-on (https://help.metricool.com/plans-add-ons-and-api-access-explained-xux1u).
- **Campaign Dashboards:** Track campaigns, with goals added in October 2026 (https://metricool.com/it/novita-metricool-ottobre-2026/). Access is limited on Starter and unlimited with Advanced Analytics (https://metricool.com/pricing/).
- **Looker Studio (Data Studio) connector:** Advanced and Custom (https://metricool.com/pricing/). The API can also feed Power BI, Tableau and n8n (https://help.metricool.com/integrations-via-the-metricool-api-an8ch).
- **ROI / UTM tracking:** A UTM builder (item 2) plus Metricool's own website analytics (item 1), and paid ads results (item 10). Revenue or ROI attribution was not found (UNVERIFIED).

### 8. Collaboration and approvals

- **Roles and permissions:**
  - Default roles: Super Admin, Account Manager, Analyst, Editor, Content Creator and Client. They are locked but can be cloned.
  - Custom roles are allowed.
  - Permissions are per brand and per section: view-only or edit for Analytics, Inbox, Planner, Ads, SmartLinks, Campaign Dashboards, Flows, Reports, Studio and Hashtag Tracker. Organization permissions are Manage brands, Manage roles and Manage team members.
  - Planner permissions include "Schedule and publish posts (no approval needed)", "Schedule pending review (posts must be reviewed)" and "Review posts".
  - **Team members and roles are Advanced and Custom only; Free and Starter have a single login** (https://help.metricool.com/roles-and-permissions-in-metricool-rhljq). The pricing table shows "Unlimited" team members on Advanced and Custom (https://metricool.com/pricing/).
- **Approval workflows (Advanced and Custom):**
  - **Internal:** Posts are reviewed on a board with your own workflow stages before going to the client.
  - **External:** "Clients approve or reject from a link in their email, with no Metricool account". They can act on several posts at once in a per-brand reviewer portal.
  - **Rules:** "no reviewer required, at least one, or all reviewers", and "Any rejection blocks publishing". Rejections carry a note and come back as "Review requests" (https://metricool.com/approval-system-social-media/).
  - **Emails:** External reviewers get one email per post, all pointing to the same portal. Metricool users get one bundled email. Approve and reject notices go by email, in-app or Slack (https://help.metricool.com/how-notifications-work-in-post-approval-7lfad). Search snippets from the help center say the portal link token expires after 72 hours and a fresh link is sent. That point was not on the fetched article, so it is UNVERIFIED.
  - **Review list:** "Review requests" (September 2026) lists requests "For you" and "Assigned by you" (https://metricool.com/product-updates-september-2026/). Since October 2026 "For you" combines all brands, and rejection notes can tag teammates (https://metricool.com/it/novita-metricool-ottobre-2026/).
- **Notes and tasks:** Notes on planned posts and on Inbox conversations; post assignment in the Planner and Library (https://metricool.com/product-updates-september-2026/; https://help.metricool.com/adding-notes-in-the-inbox-mnka8).
- **Audit trail:** None found. The roles article does not mention one (UNVERIFIED). The nearest feature, from October 2026, is an Integrations list of every third-party app with account access, marked Recognized or Unverified, with one-click "Remove access" (https://metricool.com/it/novita-metricool-ottobre-2026/).

### 9. Agency / multi-client

- **Workspaces:** The "Brand" is the unit: "Each brand in Metricool represents a business, brand, or customer" (https://metricool.com/pricing/). Plans are sold by brand count: Starter 5 or 10, Advanced 15, 25 or 50, Custom 50+ (https://help.metricool.com/whats-a-brand-in-metricool-and-how-does-it-work-5l78b).
- **Client portal:** Clients can be Collaborators with the Client role (Advanced and above). External reviewers use the no-login approval portal (item 8). Metricool Studio reports are shared as live URLs.
- **White label:** "White Label for Agencies" is on the Custom plan only, priced on request. It covers:
  - a custom URL such as clients.youragency.com;
  - logo, colors, favicon and login page;
  - "Your clients receive communication signed by you, not us" (branded emails);
  - branded automatic reports (https://metricool.com/white-label-metricool/; https://help.metricool.com/white-label-for-agencies-product-overview-wihqh).
  - There is also "White Label for Integrators (WLI)" for embedding Metricool in your own business (https://help.metricool.com/welcome-to-metricool-white-label-1jh8p; Custom plan: "Metricool integration in your business", https://metricool.com/pricing/).
- **Per-client billing:** None found. No Metricool page describes agencies billing their clients through Metricool (UNVERIFIED).

### 10. Ads

Source for this item, unless noted: https://help.metricool.com/ads-in-metricool-create-manage-and-analyze-your-campaigns-s2zha.

- **Platforms:** Meta Ads (Facebook and Instagram), Google Ads and TikTok Ads. LinkedIn Ads are not mentioned (UNVERIFIED, likely no).
- **Create and manage:** Meta and Google campaigns can be created, paused, reactivated and withdrawn, and their budgets and ad groups edited. Ads are images only, and campaign creation is confirmed with an 8-digit code sent by email. TikTok Ads are analysis only: "You can't create, edit or pause TikTok Ads campaigns from Metricool".
- **Boosting posts:** Facebook (single image) and Instagram (single image and Reels). A post can be boosted from the post list or while scheduling. The minimum is 2 units of currency, run for about 2 days (https://help.metricool.com/how-to-boost-your-posts-fr90h).
- **Ads reporting:** Campaigns, ad groups and ads for all three platforms, with platforms compared in one dashboard or report. There are metrics articles for each (https://help.metricool.com/metrics-by-social-network-dnxxf).
- **Plan gating for ads:** Not stated in the ads articles (UNVERIFIED).
- **Ad comments:** Cannot be replied to from the Inbox (https://help.metricool.com/inbox-manager-how-to-manage-messages-and-comments-from-metricool-s9zze).

### 11. Email

- **Email marketing or newsletters:** None. No Metricool page offers sending newsletters or emails to customers' audiences (UNVERIFIED that any exists; none found).
- **Sending posts as email:** None found.
- **Email-based approvals:** Yes, the main email feature. External client reviewers get an email with a link to a no-login approval portal, and creators get approve or reject emails (https://metricool.com/approval-system-social-media/; https://help.metricool.com/how-notifications-work-in-post-approval-7lfad).
- **Email report delivery:**
  - The automatic monthly report goes to multiple recipients, with a configurable reply-to so client replies reach the agency (Starter and above) (https://help.metricool.com/how-to-generate-reports-szc5k).
  - The fixed monthly summary goes from info@metricool.com on all plans (https://help.metricool.com/how-to-activate-your-monthly-summary-in-metricool-0zjmf).
  - Under white label, emails are sent with the agency's sender name and logo (https://metricool.com/white-label-metricool/).
- **Operational notification emails:** Failed posts, posts needing manual publishing, approval requests and decisions, network disconnections, website disconnection, and the monthly recap. Channels are email, in-app and push (push only for manual publishing). Brand alerts can also go to Slack (https://help.metricool.com/types-of-notifications-in-metricool-ynqf8).
- **Email inbox integration:** None. The social Inbox itself sends "no push or email notifications for new messages" (https://help.metricool.com/inbox-manager-how-to-manage-messages-and-comments-from-metricool-s9zze).
- **Account emails:** An activation email on email sign-up (https://help.metricool.com/how-to-log-in-to-metricool-from-the-website-wx7ux), and an 8-digit email code to confirm ad campaigns (https://help.metricool.com/ads-in-metricool-create-manage-and-analyze-your-campaigns-s2zha).

### 12. Automation and AI agents

- **Auto-posting:**
  - Scheduled posts publish automatically.
  - Autolists can repeat posts forever.
  - RSS feeds fill Autolists automatically. Guardrail: the "disable posts until manually activated" option holds feed posts until a person turns them on (https://help.metricool.com/schedule-content-from-an-autolist-fxehk; https://help.metricool.com/how-to-link-an-rss-feed-to-an-autolist-ank6m).
- **Flows (auto-replies and auto-DMs):**
  - Flows are "trigger-based automations that reply to social media interactions" (https://help.metricool.com/what-are-flows-nfody):
    - Instagram: comment to DM, keyword DM reply, story reply to DM.
    - TikTok: instant comment replies and comment moderation (hiding banned words); keyword DM replies are "coming soon, US only".
    - X: comment to DM, instant DM reply, and repost-then-delete.
  - Up to three reply variants are allowed, and Metricool picks one at random (https://metricool.com/message-moderation).
  - **Guardrails:** Each Flow is bound to one post. Users need the "Edit Flows" permission. You can set Flows to stop at the execution limit. **No human approval or review step is described** (https://help.metricool.com/what-are-flows-nfody).
  - **Pricing:** Flows was free until 9 November 2026. It is now a paid add-on at $12 (€10) per 2,500 executions a month, with $0.01 per extra execution (https://help.metricool.com/your-guide-to-the-flows-add-on-fy0m9; https://pricingsaas.com/companies/metricool/diffs/2026W46, third party).
- **AI agents via MCP and connectors:**
  - The official Metricool MCP server works with Claude Code and Claude Desktop. It has over 20 tools, including `post_schedule_post`, `update_schedule_post`, `get_analytics` and `get_best_time_to_post`, and works on any plan including Free. Authentication is OAuth or a user token and ID (https://metricool.com/metricool-mcp-claude/).
  - Official Claude and ChatGPT connectors launched 10 September 2026 for all users, including Free. They read data, review drafts and scheduled posts, and create and schedule content (https://metricool.com/metricool-launches-official-connectors-for-claude-and-chatgpt/).
  - **No agent-specific guardrails are described.** The guardrails that exist are account-level: role permission "Schedule pending review", and the approval system on Advanced and above (https://help.metricool.com/roles-and-permissions-in-metricool-rhljq). Since October 2026, AI-assistant apps appear as "Unverified" in the Integrations list and can be revoked (https://metricool.com/it/novita-metricool-ottobre-2026/).
- **Inbox:** No auto-replies and no AI replies (item 4).
- **Other AI that acts on its own:** AI auto-tagging of content; AI-generated brand instructions, which run in the background.

### 13. Integrations and API

- **Canva:** Yes (item 3 has the plan conflict) (https://metricool.com/integrations/).
- **Google Drive:** Yes, including sharing files from Drive on mobile (https://help.metricool.com/how-to-share-files-from-google-drive-to-metricool-from-your-mobile-1oxn3).
- **Adobe Express:** Yes.
- **Dropbox:** Not listed (https://metricool.com/integrations/); none found.
- **Zapier and Make:** Yes, Advanced and Custom (https://metricool.com/pricing/; https://help.metricool.com/main-differences-between-free-and-paid-plans-bl0v9).
- **n8n, Power BI, Tableau:** Through the API (https://help.metricool.com/integrations-via-the-metricool-api-an8ch).
- **Looker Studio connector:** Advanced and Custom.
- **Slack:** Brand alerts to a Slack channel (https://help.metricool.com/types-of-notifications-in-metricool-ynqf8).
- **CRM:** No native CRM integration found. The API can export metrics and Inbox comments "to databases, CRMs" (https://help.metricool.com/api-access-export-your-metricool-data-to-other-tools-and-automate-tasks-x8ln5).
- **Bitly:** None found. Metricool uses its own non-tracking shortener (https://help.metricool.com/scheduling-content-with-links-6kqha).
- **Website connectors:** WordPress plugin, Shopify, Squarespace, Wix and Google Tag Manager (https://help.metricool.com/how-to-connect-your-website-or-blog-to-metricool-ejuy1).
- **Chrome extension** (https://metricool.com/integrations/).
- **Public API:**
  - **Advanced and Custom only** (https://help.metricool.com/plans-add-ons-and-api-access-explained-xux1u).
  - The token is at Account Settings > API.
  - Docs: https://app.metricool.com/resources/apidocs/index.html.
  - It can export metrics and comments, schedule posts and add to Autolists (https://help.metricool.com/api-access-export-your-metricool-data-to-other-tools-and-automate-tasks-x8ln5; https://help.metricool.com/optimize-your-scheduling-ste72).
  - Rate limits: UNVERIFIED.
  - The MCP works on all plans (item 12).
- **Mobile apps:** iOS and Android. They cover the planner (plan, edit, duplicate; images only in the editor), Inbox, analytics, approvals and review requests, push reminders for manual publishing, and report generation. Report templates are web-only and Flows are not in the app (https://help.metricool.com/features-available-in-the-mobile-app-213ni; https://help.metricool.com/what-are-flows-nfody).
  - An in-app "Starter Mobile 5" plan is monthly only and billed through the App Store or Google Play, with X included (https://help.metricool.com/starter-mobile-5-subscribing-from-the-app-wd7w8). Its price is UNVERIFIED.

### 14. Pricing

**Model:** Priced by **number of brands**, not by user or by profile. A brand holds one profile per network (https://metricool.com/pricing/; https://help.metricool.com/whats-a-brand-in-metricool-and-how-does-it-work-5l78b). Prices exclude VAT. Annual billing saves "up to 24%" (https://metricool.com/pricing/).

| Plan | Brands | Monthly billing (USD/mo) | Annual billing (USD/mo) | Users | Key limits / inclusions |
|---|---|---|---|---|---|
| Free | 1 | $0 | $0 | 1 login | 20 scheduled posts per month; all networks except LinkedIn and X; 5 fixed competitors; 30 days of analytics; 5 AI credits per month; MCP; Inbox; no PDF/PPT reports, Canva, Drive or Posts Library |
| Starter 5 | 5 | $25 | $20 | 1 login | Unlimited posts (fair use); LinkedIn; 100 competitors; unlimited history; PDF/PPT and automatic monthly report emails; multiple link-in-bio; Canva and Drive; 20 AI credits per brand per month; add-ons available |
| Starter 10 | 10 | $45 | $36 | 1 login | Same as Starter 5 |
| Advanced 15 | 15 | $67 | $53 | Unlimited team members | Adds team and client management, roles, approval system, full X analytics, custom report templates, Looker Studio connector, API, Zapier and Make; 35 AI credits per brand per month |
| Advanced 25 | 25 | $107 | $85 | Unlimited | Same as Advanced 15 |
| Advanced 50 | 50 | $210 | $159 | Unlimited | Same as Advanced 15 |
| Custom | 50+ | Quote | Quote | Unlimited | White label, dedicated account manager, custom AI credits, integration in your own business |

Sources for the table: https://metricool.com/pricing/; https://metricool.com/premium-vs-free-metricool-plans (dated 13 July 2026, same figures; EUR are Starter €20/€16, €37/€29; Advanced €54/€43, €87/€69, €172/€130); https://help.metricool.com/main-differences-between-free-and-paid-plans-bl0v9; https://help.metricool.com/roles-and-permissions-in-metricool-rhljq; https://help.metricool.com/how-to-create-posts-with-metricools-ai-text-generator-sv0j8.

**Add-ons** (https://metricool.com/pricing/; https://help.metricool.com/metricool-add-ons-whats-available-and-how-theyre-billed-jscss):
- **X / Twitter, per connected account:**
  - The USD pricing page shows **$12 per month**. The help center shows **€10 / $10 per month** (€120 / $120 per year). The two Metricool sources conflict on the USD price.
  - Accounts that had the add-on before July 2026 keep a legacy €5 / $5 per month.
  - Starter and above only.
- **Advanced Analytics:** From $12 per month (€10) on Starter, and €30 / $36 on Advanced. It is one charge for the whole account and gives full Studio, unlimited Campaign Dashboards and detailed analytics.
- **Flows:** $12 (€10) per 2,500 executions a month; overage $0.01 per execution, and under 100 extra executions is not charged. Paid since 9 November 2026.
- **Hashtag Tracker:** €25 per day per network, pay-per-use (https://help.metricool.com/hashtag-tracker-4hux8).
- **Canva:** Free (pricing page).
- **White label:** Custom plan, contact sales.
- Add-ons are not available on Free.

**Free plan:** Yes, "Free, forever", no credit card (https://metricool.com/pricing/; https://help.metricool.com/main-differences-between-free-and-paid-plans-bl0v9). Some third-party reviews cite 50 posts per month, but Metricool's own pages say 20.

**Free trial:** The pricing page advertises **no standard trial** of paid plans; the closest wording is "Start for free". Trials exist through promotional or affiliate trial codes. These need a payment method, are not charged during the trial, convert to paid unless cancelled, and work once per account (https://help.metricool.com/getting-started-with-metricool-im4va; https://help.metricool.com/how-to-redeem-a-promotional-code-in-metricool-ecq8h). A creator referral page offers 30 days (https://metricool.com/my-secret-weapon). There is no standard trial length.

**Payment methods:**
- **PayPal: Yes.** Cards are also accepted: "major credit and debit cards such as Visa and Mastercard". Prepaid and virtual cards are not accepted.
- Payment is in EUR or USD only, and the VAT or tax ID must match the company name (https://help.metricool.com/available-payment-methods-in-metricool-0siw2).
- Mobile in-app plans are paid through the App Store or Google Play (https://help.metricool.com/starter-mobile-5-subscribing-from-the-app-wd7w8).

**Billing rules** (https://help.metricool.com/plans-add-ons-and-api-access-explained-xux1u):
- Upgrades are immediate and prorated; downgrades take effect at the end of the period.
- Cancelling reverts the account to Free at the end of the period.
- Deleting the account does not cancel the subscription.

### 15. Onboarding

1. **Sign up** with email and password (an activation email must be clicked before first login), Google, or Facebook. Facebook login is "only available on FREE plans" (https://help.metricool.com/how-to-log-in-to-metricool-from-the-website-wx7ux).
2. **Brand:** The Free plan comes with one brand. Paid plans create brands up to their tier, one per client, business or location (https://help.metricool.com/whats-a-brand-in-metricool-and-how-does-it-work-5l78b).
3. **Connect networks:** Profile picture > Connections > select the brand > choose a platform > "follow its authorization flow". "The OAuth authorization flow runs through your browser", and you need admin or owner rights on each account (https://help.metricool.com/getting-started-with-metricool-im4va). Per-network specifics:
   - **Facebook:** Page admin.
   - **Instagram:** Professional account, connected through Instagram login (basic features) or through the linked Facebook Page (all features) (https://help.metricool.com/how-to-connect-instagram-to-metricool-sermi).
   - **LinkedIn:** Personal profile by LinkedIn login; company page needs super admin (https://help.metricool.com/how-to-connect-linkedin-to-metricool-jq80g).
   - **Bluesky:** Sign in through a pop-up; `.bsky.social` handles only (https://help.metricool.com/how-to-connect-bluesky-to-metricool-y9d7z).
   - **Google Business Profile:** One location per brand; owner role may be needed for reviews (https://help.metricool.com/comments-or-messages-not-appearing-in-inbox-tmofe).
   - **Website:** Paste a JS snippet, install the WordPress plugin, or use a pixel, then click "Verify connection" (https://help.metricool.com/how-to-connect-your-website-or-blog-to-metricool-ejuy1).
4. **Developer apps or API keys:**
   - **Customers do not create any developer app, client ID or API key to connect networks.** The connection articles fetched (getting started, Instagram, LinkedIn, Bluesky, website) describe only in-browser OAuth or login with the customer's own account, and none mentions a developer app, app review or keys. Metricool's own Meta app appears only as something to remove under Facebook business integrations when re-granting permissions (https://help.metricool.com/how-to-connect-instagram-to-metricool-sermi).
   - The only keys a customer ever handles are optional and point outward: the Metricool API token (Advanced and above, Account Settings > API) for their own integrations, and the MCP user token and ID if they don't use OAuth (https://help.metricool.com/api-access-export-your-metricool-data-to-other-tools-and-automate-tasks-x8ln5; https://metricool.com/metricool-mcp-claude/).

### 16. Distinctive points

- **Brand-based pricing with no per-seat charge.** Advanced gives unlimited team members across 15–50 brands for $53–$159 a month on annual billing. Small businesses get a real free plan forever (1 brand, 20 posts a month, Inbox, AI, MCP). Gaps that cost extra: X costs extra per account, and team logins and approvals start at Advanced (https://metricool.com/pricing/).
- **Analytics-first heritage.** Metricool has the widest analytics surface of the four: competitors (up to 100), Twitch, its own website analytics, Meta, Google and TikTok Ads reporting, Looker Studio, and the AI report builder Studio.
- **The Inbox is thin:**
  - no assignment;
  - no AI replies;
  - no auto-replies outside paid Flows;
  - no hide or delete;
  - no stored history or contact record;
  - no email or push alerts;
  - one brand at a time.

  (https://help.metricool.com/inbox-manager-how-to-manage-messages-and-comments-from-metricool-s9zze). A product with an AI-drafted, human-approved reply inbox would be clearly differentiated here.
- **Automation without approval.** Flows send DMs and replies automatically, with no human step. The MCP and the Claude and ChatGPT connectors can create and edit scheduled posts on every plan, including Free, with no agent-specific guardrails described (items 4 and 12). The only human-in-the-loop control is the approval system and "Schedule pending review" role, which needs the Advanced plan.
- **Client approval without accounts.** Clients approve by email link into a no-login portal, with configurable rules: none, any one reviewer, or all reviewers. This is the bar for agency approvals (https://metricool.com/approval-system-social-media/).
- **Network constraints a newcomer will face too:**
  - Google Business Profile reviews may need an owner role.
  - Instagram DM replies have a 24-hour or 7-day window.
  - TikTok has no DMs and comments only on Business accounts.
  - Bluesky works only on `.bsky.social` handles.

  (help articles cited above).
- **Shipping cadence.** Metricool publishes monthly product-update posts: Studio (May 2026), Flows (September 2026, paid since 9 November 2026), Claude and ChatGPT connectors (September 2026), and cross-brand review requests (October 2026) (https://metricool.com/product-updates-september-2026/; https://metricool.com/it/novita-metricool-ottobre-2026/; https://metricool.com/metricool-launches-official-connectors-for-claude-and-chatgpt/).


---

## Publer

Checked 2026-11-18. Every claim below was read on the live web on that date. Vendor pages (publer.com plans, feature pages, help center, roadmap, blog, API docs) come first; third-party pages are marked as such. Help-center articles show an "Updated on" date, given here where the page had one. Publer is made by Kalemi (the footer reads "© 2026 Kalemi") and was founded in Tirana, Albania (https://publer.com/about).

**Note on the pricing source.** The prices on https://publer.com/plans are drawn in the browser by the page script `https://publer.com/_next/static/chunks/pages/plans-332f215a99c630a3.js`. I read the pricing formula in that script (default rates, the 1- and 2-account special prices, the "10th free" rule and the yearly discount). A third-party calculator readout matches it (Blotato, https://www.blotato.com/blog/publer-pricing, updated 2026-10-30).

### 1. Supported networks

Publer lists 13 account types: Facebook (Pages and Locations, Groups, Personal Profiles), Instagram (Business and Creator), X/Twitter, LinkedIn (Personal Profiles and Business Pages), Pinterest, Google Business Profile (locations), YouTube (channels), WordPress (WordPress.com-hosted and self-hosted), TikTok, Telegram (groups and channels), Mastodon, Threads and Bluesky (https://publer.com/help/en/article/what-social-networks-are-supported-npoun1/, updated 23/09/2026; https://publer.com/plans). X is "only on a paid subscription basis" because of the "Twitter / X Enterprise API cost" (https://publer.com/plans).

"Inbox / engagement" below means Publer's comment management on published posts. It covers only these networks: "Facebook Pages, Instagram, Twitter, Mastodon, LinkedIn, Threads, BlueSky, and YouTube". You can view, add, delete and reply to comments, and only the Admin role can do it (https://publer.com/help/en/article/how-to-manage-comments-of-published-posts-from-within-publer-ukrslk/). Publer has **no DM inbox and no unified inbox** (see section 4).

Analytics coverage comes from https://publer.com/help/en/article/what-metricsanalytics-are-gathered-for-each-social-network-1ibwz3q (updated 10/07/2026).

| Network | Supported? | Publishing | Inbox / engagement | Analytics | Notes / source |
|---|---|---|---|---|---|
| Facebook | Yes | Yes. Pages publish automatically. Personal Profiles and Groups get **push-notification reminders only**, because Meta removed the Groups API and Profiles cannot be posted to by API | Pages only: reply, add and delete comments. No Messenger DMs | Pages yes. Groups and Profiles "No insights" | https://publer.com/help/en/article/how-to-connect-facebook-groups-1lrfn2h/ (06/11/2026); https://publer.com/help/en/article/how-to-connect-facebook-personal-profiles-f8641f/ |
| Instagram | Yes (Business and Creator, not personal) | Yes: feed, carousel, Reels, Stories. Stories publish automatically only from Business accounts; otherwise by reminder | Comments yes; DMs no | Yes | https://publer.com/help/en/article/how-to-connect-instagram-accounts-1ny3f7m/ (16/04/2026); https://publer.com/help/en/article/what-post-types-are-supported-and-what-are-their-limitations-1687rte/ |
| Threads | Yes | Yes: text, link, photo, video, threads of up to 25 posts | Comments yes | Yes | Help articles above; https://publer.com/features/threads |
| X (Twitter) | Yes, **paid plans only** | Yes: posts, threads, polls, Communities, reposts | Comments yes | Yes, plus competitor insights | https://publer.com/plans; https://publer.com/help/en/article/how-to-connect-xtwitter-accounts-vuy4nh/ |
| LinkedIn page | Yes | Yes: text, photo, video, PDF document, poll | "LinkedIn" comments yes (the article does not say page or profile) | Yes | Help articles above |
| LinkedIn profile | Yes | Yes | Same as page | Yes (followers, connections, reach and more) | Help articles above |
| TikTok | Yes | Yes: video and photo carousels | **No** (not in the comment-management list) | Yes | Help articles above |
| YouTube | Yes | Yes: videos and Shorts | Comments yes | Yes | Help articles above |
| Pinterest | Yes | Yes: pins, carousels up to 5 photos, video. Free plan only connects accounts with at least 100 followers or a claimed website | No | Yes | https://publer.com/plans; help articles above |
| Google Business Profile | Yes | Yes: updates, events and offers with a **single photo**. "Videos are not supported" and multi-photo is not supported | **No** (no review or Q&A management) | Profile-level counts only (views, website clicks, calls, directions, messages, bookings). "Post insights stopped February 20, 2023" | https://publer.com/help/en/article/what-post-types-are-supported-and-what-are-their-limitations-1687rte/; metrics article above |
| Nextdoor | **No** | No | No | No | Not in any supported-network list (help npoun1, /plans, /integrations) |
| Bluesky | Yes (only accounts hosted on Bluesky; custom PDS not supported) | Yes: text, link, up to 4 photos, video, threads | Comments yes | Yes | https://publer.com/help/en/article/how-to-connect-bluesky-accounts-87vsua/ (23/02/2026) |
| Mastodon | Yes | Yes: text, photos, video, GIF, poll, threads | Comments yes; you can delete only your own comments | Yes | Help articles above |
| Reddit | **No** | No | No | No | Not in any supported-network list |
| Tumblr | **No** | No | No | No | Not in any list. Only reachable through Zapier (https://zapier.com/apps/publer-k20/integrations/tumblr) |
| WhatsApp | **No** | No | No | No | Not in any list. Publer replied on G2 (about 4–5 years ago) "not plans at the moment due to API limitations" (https://www.g2.com/discussions/50653-whatsapp-and-wordpress-integration) |
| Telegram (extra) | Yes | Yes: messages, photos up to 10, video, GIF | No | "No insights" | https://publer.com/help/en/article/how-to-connect-telegram-xp1zpl/ |
| WordPress (extra) | Yes | Yes: articles | No | Yes, for WordPress.com-hosted sites only | Help articles above |

Daily posting limits per account apply on every plan except Enterprise. Examples per profile per day, Free / Professional / Business: Facebook posts 12/24/36, Instagram 15/20/25, Threads 150/200/250, X 25/50/100, Google Business 5/10/15. Enterprise is "Unlimited" (https://publer.com/plans).

### 2. Publishing

All from https://publer.com/plans unless another source is given.

- **Scheduling:** yes, on all plans. The Free plan allows 10 scheduled posts per account at a time; paid plans are unlimited, subject to the daily limits above.
- **Content calendar:** yes. Drag and drop, filter and search, and holiday hashtags for each day. Instagram and TikTok feed preview.
- **Queue and time slots:** yes. "Add to queue (auto-schedule)" fills a posting schedule you define. It is included on Free (https://publer.com/help/en/article/what-is-included-in-publer-free-dliovh/, 12/08/2026). Failed posts can be auto-rescheduled to the next slot (https://publer.com/roadmap).
- **Best-time suggestions:** yes, on **Business**, which can also build posting schedules from them (https://publer.com/help/en/article/what-is-included-in-publer-business-1c57hsq/, 15/09/2026; https://publer.com/features/analytics).
- **Bulk / CSV upload:** yes. Up to 500 posts by CSV, a multi-post editor, and 500 media at once. Professional and up (https://publer.com/features/bulk-scheduling; https://publer.com/help/en/article/what-is-included-in-publer-professional-1srlp9n/, 27/09/2026).
- **Different text per network:** yes ("Post curation"), on all plans, with a live preview.
- **First comment:** yes. Called "follow-up comments", from Professional. Comments can be delayed. Comments triggered by performance (post age, engagement, reach) are **Business** only (https://publer.com/blog/auto-comment-share-delete-posts-by-performance/).
- **Stories, Reels, carousels:** yes. Facebook Stories (Pages) and Reels, Instagram Stories, Reels and carousels, TikTok photo carousels, YouTube Shorts, LinkedIn PDF carousels, Instagram Trial Reels (https://publer.com/help/en/article/what-post-types-are-supported-and-what-are-their-limitations-1687rte/; https://publer.com/roadmap). "App reminders" let you schedule any post type and finish it in the network's own app, for music, stickers and so on (https://publer.com/blog/publer-mobile-app-features/, 2026-11-04).
- **Drafts:** 25 on Free, unlimited on paid plans. "Ideas" is a Kanban board for content ideas, with labels, assignees, notes and approval (https://publer.com/features/ideas).
- **Evergreen recycling and content categories:**
  - Post recycling, recurring posts and Spintax variations are **Business** (https://publer.com/features/recycling; help 1c57hsq).
  - Buffer-style content categories: UNVERIFIED. I found labels on media and on Ideas, but no category-based queue.
- **RSS / blog auto-posting:** yes. "Unlimited RSS automations" from Professional. New items can publish automatically or go to the queue, with the title or description as the caption (https://publer.com/features/rss-feed).
- **Media library:** yes, from Professional, with folders, nested folders and labels (https://publer.com/features/media-integrations).
- **Link shortener and UTM:** yes, on all plans including Free. Bitly, Rebrandly, PixelMe, Switchy, JotURL, RocketLink, RetargetKit and Dub. UTM tags use Google Analytics keys (https://publer.com/integrations; help dliovh).
- **Link in bio:** yes, through "Linkie", now a separate Publer product at linkie.bio. It is free on all plans, updates automatically with posts published through Publer, and can collect emails. Click tracking is on Business: "if you have the Business plan, you can also track the clicks" (https://publer.com/features/link-in-bio; https://publer.com/plans).
- **Other publishing features:**
  - auto-share to other accounts, and auto-delete (Professional)
  - signatures and photo watermarks (Professional); video watermarks (Business)
  - shortcodes and hashtag groups
  - delays between accounts
  - a threads composer that works across X, Threads, Mastodon and Bluesky, up to 25 posts, and splits long text automatically (https://publer.com/features/threads)
  - polls on LinkedIn, X and Mastodon
  - a browser extension
  - sync of posts published outside Publer, with "eternal" history on paid plans

### 3. Content creation

- **AI writer ("AI Assist"):**
  - What it does: writes and completes captions, rewrites and fixes grammar, tailors text to each platform, and supports 80+ languages. It also does "Brand Voices" (tone guidelines per brand or client), an "AI as Analyst" chat over your post analytics, hashtag suggestions, alt text, replies to comments, AI images (the page says "GPT Image 2"), and WordPress blog and SEO articles. It can label posts as AI-generated (https://publer.com/features/ai-assist).
  - Who gets it: Business has unlimited prompts. Free and Professional can use it **only by connecting their own OpenAI API key**, and OpenAI bills them directly (https://publer.com/help/en/article/what-is-the-publer-ai-assist-1n883h3/, 21/09/2026; https://publer.com/help/en/article/how-to-connect-my-own-openai-account-for-the-ai-assist-1dmawuf/, 21/09/2026). New Free users also get trial "credits"; the number is UNVERIFIED.
- **Repurposing:**
  - Turn X threads into image carousels and text quotes into images (https://publer.com/roadmap).
  - Split long text into threads automatically.
  - Turn RSS articles into posts.
  - Share YouTube or WordPress posts automatically to other networks (https://publer.com/blog/auto-comment-share-delete-posts-by-performance/).
  - Use AI to turn an idea into a post (https://publer.com/features/ideas).
  - AI repurposing of long videos into clips: UNVERIFIED, none found.
- **Templates:** saved drafts and "templates" (https://publer.com/plans). Design templates come through the Canva, VistaCreate (free on Business), PostNitro (AI carousels) and Contentdrips integrations (https://publer.com/features/media-integrations).
- **Hashtag tools:**
  - hashtag suggestions and hashtag analytics (Business)
  - hashtag groups (free) (https://publer.com/blog/hashtag-groups/)
  - a holiday hashtag calendar
  - a free AI hashtag generator tool (https://publer.com/plans; https://publer.com/tools/ai-hashtag-generator)
- **Image and video editing:** a built-in photo editor (crop, resize, filters, text, drawing) and a video editor (https://publer.com/blog/all-in-one-editing-upgraded-photo-editor-new-video-editor-in-publer/; https://publer.com/plans).
- **Canva and other design tools:**
  - Canva works inside Publer on all plans; sending Canva designs to Publer is Professional (help dliovh, help 1srlp9n).
  - VistaCreate, PostNitro, Contentdrips, Giphy, Unsplash and Senja (video testimonials) are also integrated (https://publer.com/integrations).

### 4. Engagement

- **Unified inbox:** **No.** Publer's own comparison page (modified 2026-01-12) marks Publer "Unified Inbox ✕ (soon Kibo)" and "Social listening ✕" (https://publer.com/blog/agorapulse-vs-publer/).
  - What Publer has instead: managing comments per post under Posts > Posted. You can view, reply to, add and delete comments on Facebook Pages, Instagram, X, Mastodon, LinkedIn, Threads, Bluesky and YouTube. Only the Admin role can do it (https://publer.com/help/en/article/how-to-manage-comments-of-published-posts-from-within-publer-ukrslk/).
  - DMs, mentions and reviews: not supported. None are documented anywhere in Publer's help or plans.
- **Kibo, a separate unified inbox:**
  - Publer's Q4 2026 roadmap lists "First MVP of Kibo, the unified inbox" as "Live & gathering feedback". It says "Like Linkie… Kibo will launch as a standalone product and later be integrated into Publer" (https://publer.com/roadmap).
  - kibo.ai currently serves only a login app ("Kibo - Unified Inbox").
  - Publer's 2021 announcement described it as one dashboard for "messages, comments, mentions, and reviews", plus a website chat widget, email-lead collection, status filters and private notes (https://publer.com/blog/en/welcome-kibo/, 2021-05-24).
  - Current Kibo features, networks and pricing: UNVERIFIED.
- **Assigning conversations to team members:** No. Ideas can be assigned, but conversations cannot.
- **Saved replies:** No. Shortcodes are text snippets for the composer; using them in comment replies is UNVERIFIED.
- **AI-suggested replies:** Yes. AI Assist "generate[s] quick responses to comments" (https://publer.com/features/ai-assist). Business, or Free/Professional with your own OpenAI key.
- **Automatic replies / chatbots:** No. The only automatic comments are follow-up comments on **your own** posts, triggered by time or performance (https://publer.com/blog/auto-comment-share-delete-posts-by-performance/).
- **Moderation rules (auto-hide keywords or spam):** No. The only automatic hiding is of your **own** Facebook Page or YouTube posts that perform poorly (same blog post).
- **Sentiment:** No.
- **Contact / CRM history:** No.

### 5. Reviews

- **None in Publer.** You cannot read or reply to reviews on Google, Facebook, Yelp, TripAdvisor or any other site. Google Business Profile support is publishing plus profile counts only (https://publer.com/help/en/article/what-post-types-are-supported-and-what-are-their-limitations-1687rte/; metrics article 1ibwz3q).
- Publer's blog explains replying to Google reviews **inside Google's own interface**, not in Publer (https://publer.com/blog/how-to-reply-to-a-google-review/).
- Kibo's 2021 description mentions "reviews" without naming any site. UNVERIFIED for today.

### 6. Social listening

- **Keyword or brand monitoring:** No. Publer's own comparison says "Social listening ✕" (https://publer.com/blog/agorapulse-vs-publer/).
- **Competitor tracking:** Yes, on Business. It tracks competitors' follower growth, engagement, top posts and posting times on **Facebook, Instagram and X only** (https://publer.com/features/analytics).
- **Content discovery, not listening:** the "Explore" tab has trending posts from Instagram and X in 70+ categories, a news feed filterable by country, category and keyword, and RSS feeds (https://publer.com/features/explore).

### 7. Analytics and reporting

- **Plan:** "Analytics insights & reports" are **Business** (https://publer.com/plans).
- **Per-network metrics:** per account and per post, as listed in section 1. Includes reach rate, engagement rate, click-through rate, demographics and follower growth. Insights sync every 24 hours (https://publer.com/features/analytics; metrics article 1ibwz3q; https://publer.com/blog/auto-comment-share-delete-posts-by-performance/).
- **Cross-network reports:** one dashboard across accounts, Workspace analytics, and team-member activity and performance (https://publer.com/features/analytics; https://publer.com/features/workspaces).
- **Competitor benchmarks:** yes, Facebook, Instagram and X (section 6). Also hashtag analysis.
- **Custom / white-label / PDF reports:**
  - PDF and CSV export (https://publer.com/features/analytics).
  - **White-labeled analytics reports are Enterprise-only** (https://publer.com/help/en/article/what-is-included-in-publer-enterprise-13ap85n/, 17/07/2026).
  - A custom report builder: UNVERIFIED, none found.
- **Scheduled report delivery:** yes. Weekly or monthly PDF reports **by email**, per account or combined. PDF only, not CSV (https://publer.com/help/en/article/how-to-automate-analytics-pdf-reports-12s8wj7/; https://publer.com/help/en/article/how-to-manage-email-notifications-19wmtzl/, 22/06/2026). Business lists "automated report delivery" (help 1c57hsq).
- **ROI / UTM tracking:**
  - UTM tags and click tracking through link shorteners; Linkie click analytics.
  - The API exposes analytics for custom dashboards such as Power BI or Notion (https://publer.com/blog/publer-api-for-marketers-and-developers/).
  - Revenue or conversion ROI attribution: UNVERIFIED, none found.

### 8. Collaboration and approvals

- **Roles** (https://publer.com/help/en/article/what-are-the-different-member-roles-and-permission-levels-ior42l/, 27/09/2026):
  - **Owner:** one per Workspace. Handles billing, adds social accounts, and can enforce 2FA.
  - **Admin:** manages Editors, approves posts, and edits or deletes others' posts on assigned accounts.
  - **Editor:** "Full posting access" or "must be approved by Admin".
  - **Client**, with three levels: Read-only, Posting access, or Full access (may add its own social accounts). Any level can be given approval rights.
  - Accounts are assigned per member.
- **Approval workflows:**
  - Publer calls it a "two-tiered approval system": internal (Admin/Owner) and external client approval (https://publer.com/features/workspaces).
  - Pending posts sit in Posts > Scheduled > "Pending approval".
  - Approvers can approve all, or act per account. Declining requires a note, and the author can resubmit. Approval also works in the mobile app (https://publer.com/help/en/article/how-to-approve-or-decline-posts-1r9siip, 28/09/2026).
  - **It fails closed:** a post that is not approved before its scheduled time "will be marked as Failed" and is not published (Publer help on posting access and failed posts, via https://publer.com/help/en/article/how-to-choose-between-full-posting-access-and-approval-needed-for-your-members-1qbn3xv/ and https://publer.com/help/en/article/how-to-get-notified-about-my-failed-posts-acjw20).
  - Email notifications cover posts pending approval, approved and declined (at most one email per 30 minutes) and post notes (https://publer.com/help/en/article/how-to-manage-email-notifications-19wmtzl/).
  - Plan: "roles, permissions, and approval workflows" are listed as **Business** features (help 1c57hsq). Professional allows extra members and Workspaces but not those.
- **Notes and tasks:** internal notes on posts with @mentions of members (https://publer.com/features/workspaces). The Ideas board has assignees, notes and sign-off (https://publer.com/features/ideas).
- **Audit trail:** UNVERIFIED. No audit log is documented. The closest is "Track each team member's activity" in analytics (https://publer.com/features/analytics).
- **Security:** 2FA, which can be required for members (https://publer.com/features/workspaces).

### 9. Agency / multi-client

- **Workspaces:** unlimited on Professional and Business, one per brand or client (https://publer.com/features/workspaces).
- **Client portal:** no separate portal. Clients are invited by email into their Workspace as "Client" members, where they can preview, approve or suggest edits (https://publer.com/help/en/article/y76c4k/, 12/08/2026; https://publer.com/features/workspaces). Whether Client members count as paid seats: UNVERIFIED.
- **White-labeling:**
  - Only white-labeled analytics reports, on Enterprise (help 13ap85n).
  - A white-label app or domain: none found.
  - Publer suggests its API for "building a white-label scheduler" (https://publer.com/blog/publer-api-for-marketers-and-developers/).
- **Per-client billing:** UNVERIFIED, none found. Billing is one subscription per Owner, scaled by accounts and members (https://publer.com/plans).

### 10. Ads

- **No.** Publer's feature list, plans and integrations pages show no ad management, boosting or ad reporting (https://publer.com/plans; https://publer.com/integrations). This is based on absence, not an explicit statement from Publer.

### 11. Email

Publer has **no email marketing or newsletter feature** and **no email-inbox integration**. Sending posts as email: none found. The only email-related functions are:

- **Notification emails:** failed posts (per failure, hourly, daily digest, or off), posts pending, approved or declined, post notes, empty queues, accounts needing reauthorization, and marketing emails (https://publer.com/help/en/article/how-to-manage-email-notifications-19wmtzl/; https://publer.com/help/en/article/how-to-get-notified-about-my-failed-posts-acjw20).
- **Report delivery:** weekly or monthly analytics PDFs by email (section 7).
- **Invites:** clients and members are invited by email (help y76c4k).
- **Email capture:** Linkie, the link-in-bio page, can "Collect emails directly from your Linkie profile" (https://publer.com/features/link-in-bio). Kibo's 2021 description included lead and email-list collection (https://publer.com/blog/en/welcome-kibo/).
- **Through Zapier:** for example "Post fails → send an email via Gmail" (https://publer.com/blog/publer-zapier-automation-guide/, 2026-03-30).
- **Support:** by email at support@publer.com, alongside human live chat (https://publer.com/plans).

### 12. Automation and AI agents

**What runs without a person:**

- scheduled auto-publishing and the queue
- RSS auto-publish, either immediately or to the queue (https://publer.com/features/rss-feed)
- recycling and recurring posts with Spintax (https://publer.com/features/recycling)
- auto-share and auto-delete, and auto-hide on Facebook and YouTube
- follow-up comments, reposts and deletions triggered by performance (Business) (https://publer.com/blog/auto-comment-share-delete-posts-by-performance/)
- Threads posts auto-shared to Instagram Stories (https://publer.com/roadmap)
- Zapier triggers and actions, on all plans (https://publer.com/help/en/article/how-to-connect-to-zapier-and-automate-your-workflows-18mun8n/)
- the REST API (Business and Enterprise)
- **Publer MCP server** (Business): it lets ChatGPT, Claude or another MCP client read the calendar and analytics, draft content in the brand voice and **schedule posts**. Publer says the assistant "can work more like an agent: give it a goal… and use Publer's tools to carry out the work" (https://publer.com/blog/publer-mcp/, 2026-09-28).
- Publer offers **no automatic replies to customers** at all.

**Guardrails:**

- Approval workflows apply to members, and an unapproved post fails at its scheduled time instead of publishing (section 8).
- Per-network daily posting limits apply "to prevent spamming and abuse" (https://publer.com/plans).
- Performance-based actions are released at the next best posting time (blog above).
- MCP connects through OAuth. It is limited to what Publer's MCP tools can do, social credentials are not shared with the AI, and you can disconnect at any time (https://publer.com/blog/publer-mcp/).
- API keys carry selectable scopes: workspaces, accounts, posts, media, analytics (https://publer.com/blog/publer-api-for-marketers-and-developers/).
- App reminders put a human at the final publish step when native features are needed (https://publer.com/blog/publer-mobile-app-features/).
- Whether posts created through the API or MCP go through member approval: UNVERIFIED.

### 13. Integrations and API

- **Integration directory** (https://publer.com/integrations): Canva, VistaCreate, PostNitro, Contentdrips, OpenAI, Google Drive, Dropbox, OneDrive, Giphy, Unsplash, Senja, Dub, Bitly, Rebrandly, PixelMe, Switchy, JotURL, RocketLink, RetargetKit, Google Analytics (UTM), Zapier, Linkie, MCP (ChatGPT, Claude), browser extension, iOS and Android apps.
- **Zapier:** "available on all plans" (help 18mun8n).
- **Slack:** no native integration. Only through Zapier templates (https://zapier.com/apps/publer-k20/integrations/slack/255714436/send-slack-messages-for-new-published-posts-in-publer).
- **CRM:** none native.
- **Public API:**
  - Business and Enterprise only. "Publer does not offer a public API that is available to everyone" (help 1c57hsq).
  - Auth header: `Authorization: Bearer-API <token>`, plus a `Publer-Workspace-Id` header. Tokens are created under Settings → Access & Login → API Keys. Rate limit: 100 requests per 2 minutes per user.
  - Endpoints cover posts, media, workspaces and accounts, and analytics (https://publer.com/docs). No comment, message or inbox endpoints are mentioned.
- **Mobile apps:** iOS and Android, covering scheduling, calendar, approvals, AI Assist, analytics and app reminders (https://publer.com/blog/publer-mobile-app-features/).

### 14. Pricing

Model: **price per social account plus per extra member.** Prices are in USD, VAT is extra, and there is a 14-day money-back guarantee (https://publer.com/plans).

| Plan | Monthly billing | Annual billing (20% off; the toggle is labelled "2 months free") | Limits / what you get |
|---|---|---|---|
| **Free** | $0 forever | — | 1 user, 1 Workspace, 3 social accounts (not X), 10 scheduled posts per account, 25 drafts, 24-hour post history, Linkie, trials of some paid features with no card required (https://publer.com/plans; help dliovh) |
| **Professional** | $5/mo for 1 account; $9 for 2; from 3 accounts, $4 per account ($12 for 3, $20 for 5, $36 for 10 with the 10th free). +$2/mo per extra member | 20% off: $4/mo for 1 account; $3.20 per account from 3 | Unlimited scheduling, drafts and Workspaces; X; bulk/CSV; media library; RSS; first comments and threads; auto-share and auto-delete; watermark photos (https://publer.com/plans; help 1srlp9n) |
| **Business** | $10/mo for 1 account; $17 for 2; from 3 accounts, $7 per account ($21 for 3, $35 for 5, $63 for 10). +$3/mo per extra member | 20% off: $8/mo for 1 account; $5.60 per account from 3 | Adds unlimited AI, analytics and reports, best times, competitor and hashtag analysis, recycling, recurring posts and Spintax, roles, permissions and approvals, VistaCreate, video watermarks, API and MCP (help 1c57hsq) |
| **Enterprise** | Quote. The plans page says "Billed yearly"; the help article says annual or monthly | Quote | Volume discounts, unlimited daily posting, white-label reports, 1:1 onboarding, priority support, bank or wire transfer (https://publer.com/plans; help 13ap85n) |

How the per-account rates were derived:

- The official help center confirms only the starting prices: Professional "starts at $5/month", Business "starts at $10/month", yearly "20% discount" (https://publer.com/help/en/article/what-are-publers-plans-and-pricing-15h4yqh/, 15/09/2026).
- The per-account rates ($5 or $4.50 each for the first 1–2 accounts, then $4 each; Business $10 or $8.50, then $7) and member rates ($2 Pro, $3 Business) come from the plans-page pricing script named at the top.
- Every 9 accounts or members, the 10th is free (https://publer.com/plans).
- Blotato's calculator readout matches these figures ($12/$21 for 3 accounts, $36/$63 for 10) (https://www.blotato.com/blog/publer-pricing).
- An older help-center wording, "$4 per extra account, $2 per extra member", described a $5 + $4 + $4 progression. That gives $13 for 3 accounts, but the live calculator charges $12.

Other pricing points:

- **Free trial:** Professional 7 days, Business 14 days. "A credit card or PayPal account is required" for the trial (help 1srlp9n; help 1c57hsq). Enterprise gets 14 days, extendable (help 13ap85n).
- **Add-ons:** extra social accounts and extra members are the only add-ons. A Linkie lifetime deal is sold separately (https://publer.com/roadmap). AI is unlimited on Business; Free and Professional pay OpenAI through their own key (section 3).
- **PayPal:** **Yes.** Accepted methods are "Credit or Debit Card", "PayPal", "Apple Pay", "Google Pay" (Apple and Google Pay in the mobile app only), "Purchase Order" and "Wire Transfer" (https://publer.com/help/en/article/what-payment-methods-do-you-accept-9ns850/, 12/03/2025). PayPro Global and Paddle are the merchants of record (https://publer.com/plans; https://publer.com/terms).

### 15. Onboarding

**Sign-up steps:**

1. Sign up at publer.com, either with email (confirmation email required) or with Facebook, X, LinkedIn, Google or Apple. LinkedIn, Google and Apple need no email confirmation (https://publer.com/help/en/article/how-to-create-an-account-with-publer-dffv4n/; https://publer.com/help/en/article/how-to-sign-up-to-publer-with-my-social-networks-1hx0vjn/).
2. Answer onboarding questions (role, intended use).
3. Create the first Workspace and pick an industry.
4. Connect social accounts from the Social Accounts tab. Each network redirects you to authorize Publer, then you return and click Connect.
5. Invite members (https://publer.com/help/en/article/how-to-create-a-workspace-13potyl).

**Does the customer ever create their own developer app or API keys? No developer app for any network.** What the docs show, network by network:

- **Facebook, Instagram, X, LinkedIn, Google Business, YouTube, TikTok, Pinterest, Threads, Mastodon:** log in and authorize Publer's app (OAuth). Publer pays for the X Enterprise API, which is why X is paid-only. The X and Mastodon help articles mention no developer account, application or token (https://publer.com/help/en/article/how-to-connect-xtwitter-accounts-vuy4nh/, 11/07/2025; https://publer.com/help/en/article/how-to-connect-mastodon-in-publer-1t22ebw/; https://publer.com/help/en/article/how-to-connect-google-business-profiles-1w6ldhs/).
- **Bluesky:** the user creates a **Bluesky app password** in Bluesky's settings, guided by Publer, and pastes it in. This is an account credential, not a developer app (https://publer.com/help/en/article/how-to-connect-bluesky-accounts-87vsua/).
- **Telegram:** the user makes **Publer's own bot (@publr_bot)** an admin of the group or channel and pastes its link. No bot token of their own is needed. Quote: "you need to make Publer Bot an admin" (https://publer.com/help/en/article/how-to-connect-telegram-xp1zpl/, 29/08/2025).
  - Conflict: Publer's API networks page says Telegram "Requires a bot API token and a chat ID" and X "Requires a developer account" (https://publer.com/docs/posting/create-posts/networks). That page describes the developer API and contradicts the help center. Treat it as unreliable for end-user onboarding.
- **Self-hosted WordPress:** the user creates a **WordPress application password** and enters site URL, email and password. On WordPress 5.6 or lower this needs the Application Passwords plugin (https://publer.com/help/en/article/how-to-connect-to-my-self-hosted-wordpress-site-that-is-version-56-or-lower-4cxhbm/).
- **Optional bring-your-own-key:** Free and Professional users who want AI paste their **own OpenAI API key** (help 1dmawuf). Business users create Publer API keys only if they want the API.

### 16. Distinctive points

- **Per-account pricing**, the cheapest in this class. About $4 per account per month (Professional) or $7 (Business), the 10th account free, X kept off the Free plan because of X API cost. Approvals, analytics and AI sit behind Business (https://publer.com/plans).
- **Publishing-first, with no inbox.** Publer's own comparison admits no unified inbox and no listening. The inbox (Kibo) is being built as a separate product, to be folded in later (https://publer.com/blog/agorapulse-vs-publer/; https://publer.com/roadmap). This leaves an engagement and review gap for small businesses.
- **Unusual networks:** Telegram, WordPress, Mastodon, Bluesky, Facebook Groups and Profiles (by reminder), and X Communities. No Nextdoor, Reddit, Tumblr or WhatsApp.
- **Approval fails closed:** an unapproved post fails at its scheduled time rather than publishing (section 8).
- **AI and agent surface:**
  - an MCP server, so ChatGPT or Claude can schedule posts and act "like an agent" (Business)
  - a bring-your-own-OpenAI-key option on lower plans
  - Brand Voices per client
  - AI-content labels (https://publer.com/blog/publer-mcp/; https://publer.com/features/ai-assist)
- **Performance-triggered automations** (comment, repost or delete when thresholds are hit) and **daily posting limits per network per plan** as an anti-spam guardrail (https://publer.com/blog/auto-comment-share-delete-posts-by-performance/; https://publer.com/plans).
- **App reminders as the fallback** for anything the APIs can't do: music, stickers, Groups, Profiles (https://publer.com/blog/publer-mobile-app-features/).
- **Spin-off products:** Linkie (link in bio, with email capture) and Kibo (inbox).
- **Company:** a small company (Kalemi; Albania, Kosovo, Philippines). It runs a public roadmap and feedback board and claims "500,000+" users and human live chat (https://publer.com/about; https://publer.com/roadmap; https://publer.com/blog/topics/product-updates/).

