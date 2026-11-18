# Hootsuite and Sprout Social — current-state research for GeekSocial scope

Checked 2026-11-18. Every claim below was read on the vendor's own pricing page, product page, help center or changelog on 2026-11-18 unless the line says otherwise. Third-party sources are labelled as such. Anything that could not be confirmed is marked **UNVERIFIED**.

Method notes:
- Hootsuite's pricing comparison table is rendered client-side; it was read from the page's embedded plan-comparison data on https://www.hootsuite.com/plans. Hootsuite's help center (help.hootsuite.com, a JavaScript-rendered Salesforce site) was read in a browser.
- Sprout's pricing comparison table and FAQ were read from the page's embedded data on https://sproutsocial.com/pricing/. Sprout help-center articles were read from support.sproutsocial.com (article "updated" dates are given where they matter).

Source key used below (full URLs):

Hootsuite
- [H-plans] https://www.hootsuite.com/plans
- [H-std] https://www.hootsuite.com/plans/standard
- [H-faq] https://www.hootsuite.com/faq
- [H-int] https://www.hootsuite.com/platform/integrations
- [H-bsky] https://www.hootsuite.com/bluesky
- [H-agency] https://www.hootsuite.com/industries/agencies
- [H-connect] https://help.hootsuite.com/s/article/connect-social?language=en_US (updated 2026-10-26)
- [H-inbox] https://help.hootsuite.com/s/article/overview-inbox?language=en_US (updated 2026-11-17)
- [H-gbp] https://help.hootsuite.com/s/article/connect-gb?language=en_US
- [H-bsky-connect] https://help.hootsuite.com/s/article/connect-bsky?language=en_US
- [H-reddit] https://help.hootsuite.com/s/article/connect-reddit?language=en_US
- [H-ig] https://help.hootsuite.com/s/article/create-ig?language=en_US
- [H-approvals] https://help.hootsuite.com/s/article/set-up-approvals?language=en_US (updated 2026-11-18)
- [H-ext-approvals] https://help.hootsuite.com/s/article/manage-external-approvals?language=en_US
- [H-smart] https://help.hootsuite.com/s/article/smart-replies?language=en_US
- [H-auto-actions] https://help.hootsuite.com/s/article/automatic-actions?language=en_US
- [H-auto-resp] https://help.hootsuite.com/s/article/automatic-responses?language=en_US
- [H-chatbot-faq] https://help.hootsuite.com/s/article/chatbot-FAQ?language=en_US
- [H-chatbot] https://help.hootsuite.com/s/article/get-started-chatbot?language=en_US
- [H-dm-auto] https://help.hootsuite.com/s/article/automate-dms?language=en_US
- [H-wisdom] https://help.hootsuite.com/s/article/get-started-owlygpt?language=en_US
- [H-mcp] https://help.hootsuite.com/s/article/about-mcps?language=en_US
- [H-export] https://help.hootsuite.com/s/article/export-analytics-report?language=en_US
- [H-email-notif] https://help.hootsuite.com/s/article/email-notifications?language=en_US
- [H-send-email] https://help.hootsuite.com/s/article/send-content?language=en_US
- [H-hide] https://help.hootsuite.com/s/article/hide-mute?language=en_US
- [H-getstarted] https://help.hootsuite.com/s/article/get-started-hs?language=en_US
- [H-account] https://help.hootsuite.com/s/article/plan-account?language=en_US
- [H-zapier] https://help.hootsuite.com/s/article/zapier?language=en_US
- [H-dev] https://developer.hootsuite.com/
- [H-wn-socialos] https://www.hootsuite.com/whats-new/social-os (2026-06-24)
- [H-wn-q3] https://www.hootsuite.com/whats-new/whats-new-in-q3 (2026-10-19)
- [H-wn-gbp] https://www.hootsuite.com/whats-new/google-business-profile-reviews-in-inbox (2026-04-20)
- [H-wn-reddit] https://www.hootsuite.com/whats-new/reddit-public-comments-in-nest (2026-10-28)
- [H-wn-yt] https://www.hootsuite.com/whats-new/plan-and-publish-youtube-posts-from-hootsuites-perch (2026-11-16)
- [H-wn-fbfc] https://www.hootsuite.com/whats-new/facebook-first-comment-in-perchs-composer (2026-11-10)
- [H-wn-agents] https://www.hootsuite.com/whats-new/wisdom-agents-nest-agent-and-upgraded-publishing-agent (2026-10-29)
- [H-wn-csp] https://www.hootsuite.com/whats-new/custom-social-profile (2026-10-30)
- [H-wn-report] https://www.hootsuite.com/whats-new/analytics-report-creation-by-wisdom (2026-10-29)
- [H-wn-instr] https://www.hootsuite.com/whats-new/wisdom-saved-instructions-and-chat-sharing (2026-10-29)
- [H-wn-lumen] https://www.hootsuite.com/whats-new/lumen-agent-for-hootsuite (2026-10-07)
- [H-wn-hootbio] https://www.hootsuite.com/whats-new/hootbio-now-available-in-global-org-settings-as-part-of-teams-and-organization-experience (page is dated 2026-11-21, i.e. later than today)
- [H-pr] https://www.hootsuite.com/newsroom/press-releases/hootsuite-rebuilds-for-ai-era-introducing-wisdom
- [3P-pricingsaas-H] https://pricingsaas.com/companies/hootsuite/diffs/2026W27 (third party)
- [3P-toolradar-H] https://toolradar.com/tools/hootsuite/pricing (third party)

Sprout Social
- [S-pricing] https://sproutsocial.com/pricing/
- [S-int] https://sproutsocial.com/integrations/
- [S-faq] https://sproutsocial.com/faq/
- [S-trial] https://sproutsocial.com/trial/
- [S-agency] https://sproutsocial.com/agencies/
- [S-pub] https://sproutsocial.com/features/social-media-publishing/
- [S-ai] https://sproutsocial.com/ai/
- [S-networks] https://support.sproutsocial.com/hc/en-us/articles/115005707863 (What social profiles can I connect to Sprout?, updated 2026-10-06)
- [S-connect] https://support.sproutsocial.com/hc/en-us/articles/4435502912653 (How do I connect a social profile, updated 2026-11-03)
- [S-engage] https://support.sproutsocial.com/hc/en-us/articles/360000576446 (Introduction to Engagement, updated 2026-08-12)
- [S-posttypes] https://support.sproutsocial.com/hc/en-us/articles/15632585403021 (What types of posts can I publish, updated 2026-10-09)
- [S-reviews] https://support.sproutsocial.com/hc/en-us/articles/360031168531 (Reviews)
- [S-tripadvisor] https://support.sproutsocial.com/hc/en-us/articles/49423047157517 (Tripadvisor Integration Sunset, updated 2026-11-18)
- [S-gmb] https://support.sproutsocial.com/hc/en-us/articles/360041901331 (Google My Business Publishing)
- [S-reddit] https://support.sproutsocial.com/hc/en-us/articles/44713446274573 (Support for Reddit)
- [S-bsky-inbox] https://support.sproutsocial.com/hc/en-us/articles/49670829022861 (Bluesky Comments in Smart Inbox)
- [S-bsky-connect] https://support.sproutsocial.com/hc/en-us/articles/38584899708813
- [S-wa-faq] https://support.sproutsocial.com/hc/en-us/articles/5829594503949 (WhatsApp FAQs)
- [S-wa-connect] https://support.sproutsocial.com/hc/en-us/articles/5809703483661
- [S-tiktok-connect] https://support.sproutsocial.com/hc/en-us/articles/6161267525133
- [S-approvals] https://support.sproutsocial.com/hc/en-us/articles/205974715 (Message Approval Workflows, updated 2026-11-11)
- [S-ext-approvals] https://support.sproutsocial.com/hc/en-us/articles/9385327882125
- [S-reply-approvals] https://support.sproutsocial.com/hc/en-us/articles/360046943071
- [S-ai-use] https://support.sproutsocial.com/hc/en-us/articles/20104813531533 (Using AI and automation in Sprout)
- [S-ai-list] https://support.sproutsocial.com/hc/en-us/articles/20105027830669 (AI and automation features, updated 2026-11-12)
- [S-trellis] https://support.sproutsocial.com/hc/en-us/articles/42052737313549 (Trellis overview, updated 2026-11-18)
- [S-trellis-cap] https://support.sproutsocial.com/hc/en-us/articles/44428542886925 (Trellis Fair Use Cap)
- [S-trellis-plus] https://support.sproutsocial.com/hc/en-us/articles/46301987792781
- [S-trellis-shared] https://support.sproutsocial.com/hc/en-us/articles/49682193505165 (Trellis Plus: Shared Credits, 2026-11-17)
- [S-studio] https://support.sproutsocial.com/hc/en-us/articles/46017877635213 (Trellis Studio)
- [S-gen-posts] https://support.sproutsocial.com/hc/en-us/articles/33318590268301 (Generate Posts by AI Assist)
- [S-nov26] https://support.sproutsocial.com/hc/en-us/articles/50117332809869 (November 2026 release notes)
- [S-oct26] https://support.sproutsocial.com/hc/en-us/articles/49334278872845 (October 2026 release notes)
- [S-bots] https://support.sproutsocial.com/hc/en-us/articles/115003059703 (Bot Builder)
- [S-rules] https://support.sproutsocial.com/hc/en-us/articles/360016185371 (Automated Rules)
- [S-blocked] https://support.sproutsocial.com/hc/en-us/articles/37186171292941 (Blocked Words)
- [S-sproutlink] https://support.sproutsocial.com/hc/en-us/articles/4402850990605 (SproutLink in bio)
- [S-rss] https://support.sproutsocial.com/hc/en-us/articles/20299161205645 (Automated Feed Publishing)
- [S-bulk] https://support.sproutsocial.com/hc/en-us/articles/115005864203 (Bulk scheduling)
- [S-firstcomment] https://support.sproutsocial.com/hc/en-us/articles/40034843877901
- [S-reportpdf] https://support.sproutsocial.com/hc/en-us/articles/115002389306 (Scheduling and Sending Report PDFs)
- [S-branding] https://support.sproutsocial.com/hc/en-us/articles/9950046867341 (Customize branding in Reports)
- [S-audit] https://support.sproutsocial.com/hc/en-us/articles/13113997567885 (Audit trail logs)
- [S-groups] https://support.sproutsocial.com/hc/en-us/articles/202443876 (Groups and Users)
- [S-saved] https://support.sproutsocial.com/hc/en-us/articles/360046201412 (Saved Replies)
- [S-msgactions] https://support.sproutsocial.com/hc/en-us/articles/360000576406 (Message Actions)
- [S-chatgpt] https://support.sproutsocial.com/hc/en-us/articles/41236305631373 (ChatGPT Integration FAQs)
- [S-reauth] https://support.sproutsocial.com/hc/en-us/articles/48428076602765 (Network Reauthorization Cadences)
- [S-lite] https://support.sproutsocial.com/hc/en-us/articles/50381379648269 (Listening Lite, 2026-11-17)
- [S-api] https://support.sproutsocial.com/hc/en-us/articles/360045006152 (Sprout Public API)
- [S-api-token] https://support.sproutsocial.com/hc/en-us/articles/38272064706317
- [S-ads] https://support.sproutsocial.com/hc/en-us/articles/202124248 (Facebook Ad Tools)
- [S-plan-ess] https://support.sproutsocial.com/hc/en-us/articles/44592630251533 (Essentials Plan)
- [S-plan-std] https://support.sproutsocial.com/hc/en-us/articles/360051650231 (Standard Plan)
- [S-plan-pro] https://support.sproutsocial.com/hc/en-us/articles/360051650451 (Professional Plan)
- [S-plan-adv] https://support.sproutsocial.com/hc/en-us/articles/360051186472 (Advanced Plan)
- [S-trial-end] https://support.sproutsocial.com/hc/en-us/articles/31716210491149
- [S-community-nextdoor] https://community.sproutsocial.com/questions/489
- [3P-zapier-S] https://zapier.com/apps/sprout-social/integrations (third party)
- [3P-pricingsaas-S] https://pricingsaas.com/companies/sproutsocial/diffs/2026W35 (third party)

---

## HOOTSUITE

Context: on 2026-06-24 Hootsuite relaunched as "Social OS": Perch (publishing/planning), Nest (inbox/customer care, formerly Inbox), Lumen by Talkwalker (listening/insights), Parliament (employee advocacy, formerly Amplify), Wisdom (AI agent, formerly OwlyGPT), plus Vigil (compliance/governance, Enterprise add-on). [H-wn-socialos] [H-pr] [H-plans] [H-wisdom]

### H1. Supported networks

Official account types (help center, updated 2026-10-26) [H-connect]: Facebook Pages (Facebook personal profiles listed but publishing to them not supported); Instagram Business and Creator; LinkedIn Pages (Company/Showcase/University) and LinkedIn profiles; Threads; X; TikTok; Bluesky; YouTube channels; Pinterest ("for publishing only, so streams aren't available"); WhatsApp ("only available for use in Inbox", needs a verified Meta business account); Google Business Profile ("only available for use in Inbox"). Reddit has its own connect article [H-reddit]. Quote: "Hootsuite does not currently support any integrations for Snapchat, MeWe, Nextdoor, or XING." The same article says Mastodon scheduling is possible only via the third-party Fedica app.

Inbox (Nest) supported channels [H-inbox]: Facebook, Instagram, LinkedIn Pages, Threads, X, WhatsApp, YouTube, Inbox Messenger, Custom Social Profile (webchat), TikTok, TikTok Business Messaging, Reddit, Google Business Profile. (Bluesky and Pinterest are not in the Inbox list.)

Analytics: Standard plan "Track paid and organic social media metrics for Facebook, Instagram, X, TikTok, LinkedIn, Pinterest, and YouTube." [H-std]. Reddit: "report on Reddit performance in Analytics" [H-reddit]. Bluesky: product page claims insights into "audience growth, engagement rates" [H-bsky].

| Network | Supported? | Publishing | Inbox / engagement | Analytics | Source |
|---|---|---|---|---|---|
| Facebook | Yes (Pages) | Yes (posts, stories, reels; first comment added 2026-11-10) | Yes (DMs, comments incl. dark-post comments, wall posts, mentions) | Yes | [H-connect] [H-inbox] [H-wn-fbfc] [H-std] |
| Instagram | Yes (Business/Creator) | Yes (posts, carousels up to 10 images, stories, reels) | Yes (DMs, comments, mentions) | Yes | [H-ig] [H-inbox] [H-std] |
| Threads | Yes | Yes | Yes (public comments only; no mentions) | UNVERIFIED | [H-connect] [H-inbox] [H-faq] |
| X | Yes | Yes | Yes (DMs, comments, mentions) | Yes | [H-connect] [H-inbox] [H-std] |
| LinkedIn Page | Yes | Yes | Yes (comments, mentions, DMs where enabled) | Yes | [H-connect] [H-inbox] |
| LinkedIn profile | Yes | Yes | No (Inbox lists LinkedIn Pages only) | UNVERIFIED | [H-connect] [H-inbox] |
| TikTok | Yes | Yes (Business account needed for scheduling per FAQ) | Yes (comments on owned videos; TikTok Business Messaging in select regions) | Yes | [H-connect] [H-inbox] [H-faq] [H-std] |
| YouTube | Yes | Yes (videos; YouTube Posts added 2026-11-16) | Yes (video comments) | Yes | [H-connect] [H-inbox] [H-wn-yt] [H-std] |
| Pinterest | Yes | Yes (web only) | No ("publishing only") | Yes | [H-connect] [H-std] |
| Google Business Profile | Yes | No (native integration is "only available for use in Inbox"); a separate GMB App Directory app is described as letting you "create posts" — conflict, see note | Yes (reviews: reply, tag, topics, sentiment) | UNVERIFIED | [H-connect] [H-gbp] [H-inbox] [H-int] |
| Nextdoor | **No** ("does not currently support any integrations for ... Nextdoor") | No | No | No | [H-connect] |
| Bluesky | Yes | Yes | No (not in Inbox channel list); listening yes | Claimed on product page | [H-connect] [H-bsky] [H-inbox] |
| Mastodon | No native support; only via third-party Fedica app | Via Fedica app only | No | No | [H-connect] |
| Reddit | Yes (one u/ account + moderated subreddits) | UNVERIFIED (no publishing described) | Yes (comments/posts in moderated subreddits; launched 2026-10-28) | Yes (no export) | [H-reddit] [H-wn-reddit] [H-export] |
| Tumblr | No (not in supported list) | No | No | No | [H-connect] |
| WhatsApp | Yes (Inbox only) | No | Yes (private messages) | Inbox metrics only (UNVERIFIED beyond that) | [H-connect] [H-inbox] |

Note on GBP conflict: [H-connect] (help center, 2026-10-26) says GBP is "only available for use in Inbox"; [H-int] FAQ says "Hootsuite has a Google My Business app ... you can create posts and respond to reviews from a single tab." Treat native GBP publishing as **UNVERIFIED**.

Other: Custom Social Profile (2026-10-30) lets an organization pipe its own website webchat into Nest via webhooks; "your developers handle the setup, with support from Hootsuite implementation consultants." [H-wn-csp]

### H2. Publishing

From the plan comparison data on [H-plans] unless noted:
- Scheduling: "Draft, schedule, and publish posts" — Unlimited on every paid plan. Trial accounts are capped at "10-20 posts per organization" per day. [H-plans]
- Content calendar: yes, calendar or list view, all plans; calendar export to CSV/PDF, all plans. [H-plans]
- Queue/time slots: **UNVERIFIED** (no fixed time-slot queue found in pricing or help center search).
- Best-time suggestions: "Recommended times to publish" (Facebook, LinkedIn, Instagram, X, TikTok), all plans; "Best times to publish charts" also include Pinterest and YouTube. [H-plans]
- Bulk/CSV: "Bulk Composer to schedule and publish up to 350 posts at once by uploading a .csv file", all plans; "Bulk scheduling is not available during trial." [H-plans]
- Per-network customization: "Schedule a post to multiple networks or customize your post for each network", all plans. [H-plans]
- First comment: Instagram (direct publishing) [H-ig]; Facebook feed posts and reels (not Stories), added 2026-11-10, and the first comment travels through Approvals [H-wn-fbfc]. LinkedIn first comment: **UNVERIFIED**.
- Stories/Reels/carousels: Instagram posts (up to 10 images), stories, reels; multi-image stories and Creator profiles require the "Publish via mobile notification" workflow; Instagram limits third-party direct publishing to 25 posts/reels per rolling 24 hours. [H-ig] Facebook post/story/reel article exists (help center search, 2026-11-18).
- Drafts: yes. [H-plans] [H-getstarted]
- Evergreen/recycling or content categories: no recycling feature found (**UNVERIFIED** that none exists). Categorization is via "Outbound message tagging" (Professional+) and Campaigns (Advanced+). [H-plans] [H-ig]
- RSS/blog auto-posting: via the "Hootsuite Syndicator" app ("View RSS feeds in your Hootsuite dashboard and share them to your social channels"); fully automatic RSS posting **UNVERIFIED**. [H-int]
- Media library: "Asset and content library" (approved post templates and images), all plans; free stock photos and GIPHY. [H-plans]
- Link shortener/UTM: Ow.ly shortening all plans; automated link tracking presets, custom vanity URLs ("sold separately") and Bitly integration on Professional+. [H-plans] [H-int]
- Link-in-bio: "Link in bio tool (Hootbio)", all plans. [H-plans] [H-wn-hootbio]
- Suspend scheduled posts: Advanced+. [H-plans]
- YouTube Posts (image/text posts) added to Perch 2026-11-16. [H-wn-yt]

### H3. Content creation

- AI (Wisdom, formerly OwlyGPT/OwlyWriter): "Let AI generate engaging captions for every social media network, repurpose top-performing posts, and automatically create content for holidays"; caption enhancement (spelling/grammar, length, tone, "more actionable"); AI hashtag suggestions; post and image generation — all plans. [H-plans] [H-ig]
- Wisdom chat: brainstorm, research trends (Lumen agent, last 7 days with citations), draft on-brand posts, analyze performance; accepts a website URL in the prompt and file uploads (PNG, JPEG, PDF, DOCX, TXT, .md). [H-wisdom] [H-wn-lumen]
- Repurposing long content: Wisdom can draft from a website URL or an uploaded document [H-wisdom]; repurposing video into posts **UNVERIFIED**.
- Wisdom "Instructions" (saved role/brand/tone, up to 2,000 chars, checked against content rules) added 2026-10-29. [H-wn-instr]
- Templates: post templates in the asset/content library. [H-plans]
- Hashtags: AI hashtag suggestions (all plans). [H-plans]
- Image/video editing: "Digital media editing" built in, all plans; video editing adds filters, stickers, music. [H-plans] [H-ig]
- Design integrations: Canva and Adobe Express templates in the composer (all plans); Google Drive and Dropbox media import. [H-plans] [H-ig] [H-int]
- AI credits: "100 AI credits/mo" on Standard, Professional and Advanced; Enterprise custom. [H-plans]

### H4. Engagement

- Unified inbox: "All-in-one centralized inbox", all plans; channels as in H1. [H-plans] [H-inbox]
- Reviews in inbox: Google Business Profile reviews (2026-04-20). [H-wn-gbp]
- Assignment: "Assign messages to teammates" and skill-based routing — Advanced+. [H-plans]
- Saved replies: all plans. [H-plans]
- AI-suggested replies: "AI-powered smart replies" — Professional+ per the pricing table [H-plans]; the help article says "Audience: Enterprise Inbox agents with Advanced Inbox" and "Smart replies are not available for Facebook or Instagram private messages or for WhatsApp"; the agent can accept, rewrite or cancel before sending [H-smart]. Plan-tier conflict noted.
- Automatic replies: "Automated message responses" (keywords/business hours) Professional+ [H-plans]; scripted "auto responders" (virtual agents) for DMs only, help article audience "Advanced and Enterprise Inbox admins" [H-auto-resp]; Instagram comment-keyword DM automation, Professional+ [H-plans] [H-dm-auto].
- Chatbots: generative AI chatbot answering from a "pre-approved knowledge bank file", on website, Messenger, Instagram, LinkedIn, X, WhatsApp; "Audience: Enterprise Inbox admins with Advanced Inbox"; built on OpenAI GPT / Azure OpenAI. [H-chatbot] [H-chatbot-faq]
- Moderation: manual Hide for Facebook, Instagram, TikTok and X comments; Mute a post; Mask PII ("Message masking" all plans). [H-hide] [H-plans] Automatic keyword-based hiding: **not found** — the Inbox automation actions are tag, apply topic, resolve, forward to CRM [H-auto-actions]. Auto-hide by keyword is **UNVERIFIED**.
- Automations: tag/topic/resolve/forward-to-Salesforce rules; "Has no text" trigger to auto-resolve rating-only reviews; AI classifier automations (Enterprise). [H-auto-actions]
- Sentiment: listening sentiment on all plans [H-plans]; GBP reviews "analyze sentiment" in Inbox [H-inbox].
- Contact/CRM history: cross-channel conversation history, contact notes, all plans; "Build complete contact profiles ... connecting to your CRM" Professional+; Salesforce Service Cloud integration Enterprise. [H-plans] HubSpot app [H-int]. Zendesk OAuth (2026 changelog listing).
- Other: business hours/SLAs, collision avoidance (all plans); CSAT surveys, agent chat, team productivity reporting (Advanced+). [H-plans] Inbox is disabled if unused/unanswered for more than 60 days. [H-inbox]

### H5. Reviews

- Google Business Profile: read and reply in Inbox (native). [H-gbp] [H-wn-gbp] [H-inbox]
- Facebook reviews, Yelp, TripAdvisor and others: not native. Monitoring is via the third-party ReviewTrackers app ("Monitor brand reviews across 85 sites, including ... Yelp, Facebook Reviews, BBB, TripAdvisor, and OpenTable") [H-int]; replying to those through Hootsuite **UNVERIFIED**.
- Enterprise add-on "Monitor brand reviews alongside social mentions." [H-plans]
- Glassdoor: "Hootsuite does not currently integrate with Glassdoor." [H-int]

### H6. Social listening

- Lumen Quick Search: 7 days (Standard), 30 days (Professional+); compare 2 (Standard) or 5 (Pro/Advanced) results; sentiment, image recognition, peak detection, AI summaries on all plans; 90-day trend forecasting Pro+; basic sources (news, newsletters, blogs, forums, Facebook, X, Bluesky, YouTube, Quora, podcasts); advanced sources (Instagram, Threads, LinkedIn, Reddit, Pinterest, Twitch...), 13-month history, alerts and scheduled listening reports — Enterprise only. [H-plans]
- Competitor tracking: competitive benchmarking of 5 competitors (Standard) or 20 (Pro/Advanced). [H-plans]

### H7. Analytics and reporting

- Per-network: Facebook, Instagram, X, TikTok, LinkedIn, Pinterest, YouTube (paid and organic). [H-std]
- Cross-network: "Multiple networks reporting", post performance reports, industry benchmarking, social performance score — all plans. [H-plans]
- Custom reports: Professional+; report notes Professional+; logo on reports Advanced+. [H-plans] Agency page: "fully customizable, white-labeled reports." [H-agency]
- Exports: the help article says "All plans- Export overview reports to PDF and PPTX" and CSV/XLSX on Advanced+ [H-export]; the pricing table instead shows "Basic report exporting" (.pdf/.ppt) as Professional+ and not on Standard [H-plans] — conflict. Reddit data cannot be exported. [H-export]
- Scheduled delivery: "Basic report scheduling" (emailed) Professional+; "Export to an external email ... on a set schedule" Advanced+; weekly (Tuesday) or monthly, 10am GMT, optional password-protected ZIP. [H-plans] [H-export]
- Wisdom can turn an analytics question into a saved report (2026-10-29). [H-wn-report]
- ROI/UTM: ROI reporting Enterprise; advanced social attribution Enterprise add-on; link tracking Pro+. [H-plans]

### H8. Collaboration and approvals

- Roles/permissions: user access permissions and custom permissions on all plans; SSO Enterprise. [H-plans]
- Internal approvals: Advanced and Enterprise ("Plans: Advanced and Enterprise"). Per-member permission "Publish Message with Approval"; inbox replies can also require approval ("Comment and reply with Approval"); approval teams; multi-tier approvers, Sequential or Parallel. [H-approvals] Parallel approvals shipped in Q3 2026. [H-wn-q3]
- External (client) approval: no external-reviewer link documented. "Manage external approvals" is Enterprise-only and lets a user "mark posts as approved" with a note when the approval happened outside Hootsuite. [H-ext-approvals] The agency page says "With external approvals, you can easily get the go-ahead from clients before you hit publish", and its FAQ clarifies "Hootsuite allows you to document external approvals directly within each post." [H-agency]
- Notes/tasks: internal comments on posts (Advanced+); conversation/contact notes (all); agent chat (Advanced+). [H-plans] Tasks: **UNVERIFIED**.
- Audit trail: approval history and CSV exports record approvals [H-ext-approvals]; Inbox audit trails exist for "Enterprise Inbox admins with Advanced Inbox" (help center search result, 2026-11-18); Vigil "keep a record of review decisions" (Enterprise add-on) [H-plans].

### H9. Agency / multi-client

- Organizations and teams; a social account belongs to one organization and can be shared with multiple teams. [H-connect]
- Agency page: "organize accounts by client", white-labeled scheduled reports, approval workflows, documented external approvals. [H-agency]
- Client portal: none described. **UNVERIFIED**.
- Per-client billing: not described. **UNVERIFIED**.

### H10. Ads

- All plans: publish and schedule ads, ads analytics/reporting and campaign management on Facebook, Instagram, LinkedIn and X; boost Facebook, Instagram and LinkedIn posts. Professional+: "Automatically boost posts" (Facebook/Instagram) by criteria. [H-plans]

### H11. Email

- Email marketing/newsletters: none found (**UNVERIFIED** that none exists; nothing in pricing, integrations or help search). Zapier can bridge to Mailchimp. [H-zapier]
- Sending social content as email: "Send to email" from Streams for X, Facebook, Instagram, LinkedIn, YouTube (you log in to your own email account). [H-send-email]
- Email notifications: failed scheduled post, Inbox activity, "A post requires your approval", rejected in pre-review; disconnection emails go to the member who connected the account. [H-email-notif]
- Report delivery by email: scheduled exports, Pro+ internal, Advanced+ external addresses. [H-plans] [H-export]
- Email inbox integration (email as an Inbox channel): not in the Inbox channel list [H-inbox]; "Hootsuite does not currently integrate with Outlook." [H-int]

### H12. Automation and AI agents (and guardrails)

- Acts without a person:
  - Scheduled/bulk publishing (all plans). [H-plans]
  - Auto-responders (scripted virtual agents in DMs, hand off on timeout). [H-auto-resp]
  - Instagram DM automation: on a keyword comment, Hootsuite "automatically reply to their comment and send them a direct message (DM) on your behalf", rotating through preset public replies with optional delay. [H-dm-auto]
  - Generative AI chatbot (Enterprise, Advanced Inbox): replies to customers in real time from the knowledge bank; escalation to a human agent; oversight is after the fact ("all chatbot conversations ... recorded ... in the Resolved queue"). [H-chatbot] [H-chatbot-faq]
  - Auto-boost of top posts (Pro+). [H-plans]
  - Automation rules (tag/topic/resolve/forward). [H-auto-actions]
- Guardrails:
  - AI smart replies are suggestions; the agent accepts, rewrites or cancels. [H-smart]
  - Wisdom agents (Nest agent, Publishing agent, 2026-10-29): "Review an Allow or Deny prompt before either agent writes or edits anything." The Publishing agent saves drafts to Planner. [H-wn-agents]
  - MCP connectors (Claude, ChatGPT): Perch can save drafts and read analytics; Nest "Sending public replies to customers isn't supported through MCP"; data from X, LinkedIn, Reddit, Snapchat and YouTube is excluded when a connector is active. [H-mcp]
  - Approval workflows for posts and inbox replies (Advanced+). [H-approvals]
  - Vigil (Enterprise add-on) screens posts, replies and advocacy shares "against your organization's publishing policies before they go live." [H-plans]
  - Chatbot: optional AI-consent prompt to customers; knowledge-bank-only answers. [H-chatbot] [H-chatbot-faq]

### H13. Integrations and API

- Canva (built into composer), Adobe Express, Google Drive, Dropbox, Bitly (Pro+), Slack app, HubSpot app, Salesforce (Enterprise), monday.com, Wrike, Asana, Proofpoint, Bynder, Shopify (Shopview), Google Analytics (Advanced Analytics), ReviewTrackers, Syndicator (RSS). [H-int] [H-plans] [H-ig]
- Zapier: yes ("schedule or publish posts including images"). [H-zapier]
- Public API: REST API with OAuth2 for publishing (messages, approve/reject), media upload, members, organizations, social profiles, SCIM, Analytics, Inbox; App Directory SDK. [H-dev]
- MCP connectors: Perch, Nest, Lumen, Parliament for Claude and ChatGPT. [H-mcp] Lumen MCP connector listed as an Enterprise add-on. [H-plans]
- Mobile apps: iOS and Android. [H-getstarted]

### H14. Pricing (per user/seat)

From [H-plans] (plan cards, FAQ, JSON-LD offer data):

| Plan | Annual billing (per user/month) | Monthly billing | Social accounts | Users |
|---|---|---|---|---|
| Standard | $99 | Higher than annual; amount **UNVERIFIED** (not published; shown only in signup flow per third-party [3P-toolradar-H]) | 10 | 1+ (buy seats) |
| Professional | $199 | **UNVERIFIED** | Unlimited | 1+ |
| Advanced | "up to $399" (JSON-LD price 399.00) | **UNVERIFIED** | Unlimited | 1+ |
| Enterprise | Custom ("Request a demo") | Custom | Unlimited | Custom |

- Posts: unlimited scheduled posts on paid plans; trial accounts capped at 10–20 posts per day per organization; no bulk scheduling during trial. [H-plans]
- Free plan: none listed. Free trial: 14 days on Standard, Professional and Advanced. FAQ: "At the end of your trial, regular billing begins and you'll be charged. You can avoid charges by canceling any time before your trial ends." [H-plans] Whether a card is required at trial start is **UNVERIFIED** (third-party [3P-pricingsaas-H] says a no-credit-card trial was introduced in June 2026; Hootsuite's own FAQ implies automatic billing).
- Add-ons (Enterprise only): Parliament, Vigil, Advanced Analytics, Advanced Inbox, Lumen by Talkwalker, Lumen MCP connector, brand review monitoring, Salesforce. Vanity URLs "sold separately". Extra seats per user. [H-plans]
- Nonprofit discount on some plans. [H-plans]
- PayPal: **yes** — "We accept Visa, Mastercard, Discover credit cards, JCB, and PayPal, and for the annual Enterprise plan, we can invoice you." [H-plans]
- History (third party): Standard/Advanced at $99/$249 in April 2026; Professional tier added late June 2026. [3P-pricingsaas-H] Legacy plans (Professional, Team, Standard, Advanced) still exist; moving to a new "Suite" plan requires creating a new account. [H-account]

### H15. Onboarding

1. Sign up with email or with Google, Facebook, LinkedIn or X. [H-getstarted]
2. "The first time you sign in, Hootsuite prompts you to connect your social accounts and set permissions." [H-getstarted]
3. Connect: Account > Settings > Social accounts > Add social account, then the network's own sign-in/authorization. Organization Super Admin/Admin or a "Connect social accounts" permission is required. [H-connect]
   - Bluesky: enter username, then Bluesky password, then Authorize. [H-bsky-connect]
   - Google Business Profile: Google sign-in; must be Owner or Manager of the profile. [H-gbp]
   - Reddit: OAuth; account must moderate each subreddit; one subreddit per Hootsuite organization. [H-reddit]
   - WhatsApp: requires a verified Meta business account. [H-connect]
4. Developer app / API keys: none of the network connect articles ask the customer to create a developer app or API keys; every connection is a sign-in/authorize flow. The one exception is optional: Custom Social Profile (own webchat) requires the customer's developers to wire webhooks. [H-connect] [H-bsky-connect] [H-gbp] [H-reddit] [H-wn-csp]

### H16. Distinctive points for a newcomer

- Per-user pricing starting at $99/user/month (annual), with only 10 social accounts on the entry plan. [H-plans]
- Nextdoor is explicitly unsupported. [H-connect]
- Approvals, assignment and external-email report delivery are all gated to Advanced ($399/user/month). [H-plans]
- AI is pervasive (Wisdom, 100 credits/month) but write actions by agents are gated by an Allow/Deny prompt; the generative chatbot is the one place AI talks to customers unattended (Enterprise). [H-wn-agents] [H-chatbot-faq]
- MCP connectors to Claude/ChatGPT exclude X, LinkedIn, Reddit, Snapchat and YouTube data "due to agreements with certain social networks". [H-mcp]
- Inbox is auto-disabled after 60 days without use. [H-inbox]
- Instagram direct publishing limit: 25 posts/reels per rolling 24 hours (Instagram API). [H-ig]

---

## SPROUT SOCIAL

### S1. Supported networks

Help center list (updated 2026-10-06) [S-networks]: X, Facebook, TikTok, LinkedIn, Instagram (Business only), Pinterest, YouTube (brand account), Bluesky, Threads, WhatsApp, Trustpilot; review platforms Google My Business, Glassdoor, Yelp, Facebook, Google Play Store, Apple App Store. Connect guides also cover Snapchat and Reddit. [S-connect]

Smart Inbox supported networks [S-engage]: X (mentions, DMs, replies, brand keywords); Facebook (comments, posts, DMs, reviews, ad comments); Instagram (comments, mentions, DMs, Story replies, ad comments); LinkedIn (comments, mentions, DMs); TikTok (comments, mentions, ad comments); WhatsApp Business (private messages); YouTube (comments); Reddit (moderated subreddits); Threads (comments and mentions); Bluesky (comments on owned posts).

| Network | Supported? | Publishing | Inbox / engagement | Analytics | Source |
|---|---|---|---|---|---|
| Facebook | Yes (Pages) | Yes (image, video, reels, link, text, Page polls; no Stories, no events) | Yes | Yes | [S-posttypes] [S-engage] |
| Instagram | Yes (Business) | Yes (image, video, reels, carousels up to 10, Stories; Story stickers need mobile app) | Yes | Yes | [S-posttypes] [S-engage] |
| Threads | Yes | Yes (text, image, video, carousel up to 20) | Yes (comments, mentions) | Yes ("reporting capabilities") | [S-posttypes] [S-engage] [S-int] |
| X | Yes | Yes (no polls, no threads) | Yes | Yes | [S-posttypes] [S-engage] |
| LinkedIn Page | Yes | Yes (incl. PDF documents) | Yes | Yes | [S-posttypes] [S-engage] |
| LinkedIn profile | Yes (personal profiles count toward limit) | Yes (first comment supported on personal profiles) | Yes — member-profile @mentions added 2026-10-06 | UNVERIFIED | [S-networks] [S-firstcomment] [S-oct26] |
| TikTok | Yes (Business) | Yes (video direct for Business accounts; photo posts) | Yes (comments, mentions, ad comments) | Yes | [S-posttypes] [S-engage] [S-tiktok-connect] |
| YouTube | Yes | Yes (videos, Shorts; YouTube Posts added 2026-11-16) | Yes (comments) | Yes | [S-posttypes] [S-nov26] |
| Pinterest | Yes | Yes (static/video/board pins) | No (not in Smart Inbox list) | Yes | [S-posttypes] [S-engage] [S-int] |
| Google Business Profile | Yes | Yes ("What's New" and Event posts, one image; multi-location posting) | Reviews tab (read/reply) | UNVERIFIED (Reviews reporting exists) | [S-gmb] [S-reviews] [S-tripadvisor] |
| Nextdoor | **No** (not in any network list; help center search returns zero articles; June 2024 community reply: "I don't see a request for a Nextdoor integration in there yet") | No | No | No | [S-networks] [S-community-nextdoor] |
| Bluesky | Yes | Yes (text, 1–4 images, one video, link) | Yes (comments on owned posts, 2026-10-16, US datacenter) | Yes (reporting overview article) | [S-posttypes] [S-bsky-inbox] [S-oct26] |
| Mastodon | No (not listed; zero help results) | No | No | No | [S-networks] |
| Reddit | Yes (official Reddit Data Partner) | Yes (as a user to own profile or subscribed subreddit) | Yes (moderated subreddits; no CSV/PDF/email export, no CRM) | Inbox reports | [S-reddit] [S-posttypes] |
| Tumblr | Listening only (add-on) | No | No | No | [S-int] [S-lite] |
| WhatsApp | Yes (Standard+) | No | Yes (text/image; templates; no chatbots per FAQ) | Inbox reports | [S-wa-faq] [S-pricing] |
| Snapchat (extra) | Yes (Public Profile) | Yes (Stories, Spotlight) | No | UNVERIFIED | [S-posttypes] |

Note: the WhatsApp FAQ says you cannot "Setup chatbots" for WhatsApp [S-wa-faq], while the Bot Builder article lists WhatsApp among chatbot channels [S-bots] — conflict.

### S2. Publishing

From the [S-pricing] comparison table unless noted:
- Scheduling and calendar: collaborative content calendar, advanced scheduler (multi-day/time), multi-profile and multi-network publishing — all plans. Posts can be scheduled up to 180 days ahead. [S-pricing] [S-pub]
- Queue/time slots: "Sprout Queue (message per day)" — 10 (Essentials, Standard), 20 (Professional), 30 (Advanced); ViralPost picks send times in the queue. [S-pricing]
- Best-time suggestions: Optimal Send Times in Compose, all plans (examines 16 weeks of audience data). [S-pricing] [S-pub]
- Bulk/CSV: Professional+; up to 350 messages per import; no video; trials 50 per import. [S-pricing] [S-bulk]
- Per-network customization: yes ("Customize by Network"). [S-firstcomment]
- First comment: Facebook, LinkedIn (Company and Personal) and Instagram; text, links, hashtags only. [S-firstcomment]
- Stories/Reels/carousels: see S1 table. [S-posttypes]
- Drafts: all plans. [S-pricing]
- Evergreen/recycling: none found (**UNVERIFIED** that none exists). Content categories: campaign tagging (Pro+), Campaign Planner. [S-pricing] [S-pub]
- RSS: "Automated feeds publishing" — Professional 10 feeds, Advanced 30; can "Publish posts immediately", create scheduled posts or drafts; to Threads, X, Bluesky, Facebook, LinkedIn. [S-pricing] [S-rss]
- Media library: "Digital asset & content library" Professional+. [S-pricing]
- Link shortener/UTM: Bitly (all plans); custom link tracking Pro+; automated link tracking rules Advanced. [S-pricing] [S-int]
- Link-in-bio: SproutLink (Instagram), clickable links per post, click tracking. [S-sproutlink] Plan availability **UNVERIFIED**.
- Instagram grid planning: Professional+. [S-pricing]
- Pause all scheduled/queued messages (crisis): all plans. [S-pricing]
- Paid promotion (boost) for Facebook and Instagram: all plans. [S-pricing]

### S3. Content creation

- AI Assist (OpenAI-powered features listed with "Can this be disabled? Yes"): Enhance Post (Professional+), Generate Posts (from top 20 posts, a custom topic, or an article URL), alt text (all plans), subtitles, translations, recommended tags, suggested hashtags. [S-pricing] [S-ai-list] [S-gen-posts]
- Repurposing long content: "To generate a post from an article: ... Add your article's URL." [S-gen-posts] Video-to-posts repurposing **UNVERIFIED**.
- Trellis (AI agent, all plans, 100 credits/seat/month): drafts replies and post variations, inbox briefings, report summaries. [S-pricing] [S-trellis] Trellis Studio: scheduled "skills" (e.g. "Generate post ideas", "Optimize upcoming content"). [S-studio]
- Templates: text assets/saved replies in the Asset Library. [S-saved]
- Hashtags: type-ahead hashtag suggestions, all plans. [S-pricing]
- Image/video editing: built-in image editor (all plans); Video Editor in Compose (help article exists). [S-pricing]
- Design integrations: Canva Design Importer and Canva Draft Publisher (all plans), Adobe Express, Adobe Experience Manager, Bynder, Dropbox, Google Drive. [S-pricing] [S-int]

### S4. Engagement

- Unified inbox (Smart Inbox): Standard+ (Essentials has no inbox, cases or reviews). [S-pricing] [S-plan-ess]
- Assignment: Cases (assign messages as cases, Standard+); case routing for teams and automatic case assignment, Advanced. [S-plan-std] [S-pricing] [S-oct26]
- Saved replies: Professional+ (since 2024-03-01). [S-saved] [S-pricing]
- AI-suggested replies: "Enhance Reply by AI Assist" Advanced (tone options in Smart Inbox); Trellis "Drafted replies". [S-pricing] [S-ai-list]
- Automatic replies/chatbots: Bot Builder (Advanced) — scripted welcome messages, quick replies and auto-responses for X DMs, Facebook, Instagram DMs (and WhatsApp per this article); one active bot per profile. [S-bots] [S-pricing] Automated Rules (Advanced) can tag, complete, alert, hide [S-rules]; the Bluesky article also lists "auto-reply" as a rule action [S-bsky-inbox].
- Moderation: comment moderation (Standard+); Automated Rules "Hide messages" action for Facebook, Instagram, YouTube, TikTok and X based on words/phrases (Advanced); spam classification (Advanced). [S-pricing] [S-rules] [S-engage]
- Sentiment: "Sentiment in the Smart Inbox and Reviews", Advanced. [S-pricing]
- Contact/CRM history: contact views with notes, comments, history (Standard+); conversation history 90 days (Standard) or full (Pro+); helpdesk/CRM integrations (HubSpot Service Hub, Salesforce, Dynamics 365, Zendesk, Marketo) Advanced. [S-pricing] [S-int] (The Standard plan article says "send messages to your CRM tools" [S-plan-std], which conflicts with the Advanced-only table row.)
- Other: reply collision detection, iOS/Android push, CSAT/NPS surveys (Advanced), message spike alerts (Advanced), macros (Advanced), AI message-intent classification (Advanced). [S-pricing]

### S5. Reviews

- Read and reply: Facebook, Google My Business, Yelp, Trustpilot (needs a paid Trustpilot account), Glassdoor, Apple App Store, Google Play Store. Functionality listed as "Read, Reply, Tag" for each. [S-reviews] [S-int]
- TripAdvisor: **sunset**. "Starting October 30, 2026, you'll no longer be able to connect new Tripadvisor locations, and Sprout will stop pulling in new Tripadvisor reviews"; data available through 2026-11-30; "Replying to Tripadvisor reviews has never been available inside Sprout." [S-tripadvisor]
- Plan: "Review management" from Standard; each review location counts against the profile limit. [S-pricing] [S-networks]
- Reviews can be tagged, turned into cases, emailed, auto-ruled; one reply per review; reply approvals apply to reviews. [S-reviews] [S-reply-approvals] [S-rules]

### S6. Social listening

- Standard+: real-time keyword/brand monitoring and location monitoring; brand keywords 10 (Standard/Pro) or 20 (Advanced). [S-pricing] [S-plan-std]
- Listening: paid add-on (Standard+) — Smart Categories, sentiment, spike alerts, Bluesky, Facebook, Instagram, Reddit, TikTok, Tumblr, Web, X, YouTube. [S-pricing] [S-int]
- Listening Lite (help article 2026-11-17): 3 topics, rolling 30-day window, Bluesky/Reddit/Web/X only, no replying. Plan/price availability **UNVERIFIED**. [S-lite]
- Competitor tracking: Facebook, X and Instagram competitor reports, Professional+. [S-pricing]

### S7. Analytics and reporting

- All plans: group, profile and post-level reports; cross-network group report; unlimited PDF/CSV downloads; Google Analytics report. [S-pricing]
- Professional+: competitor reports, paid social performance reports, tag reports, custom report branding (PDF logo per group). [S-pricing] [S-branding]
- Advanced: team/productivity, care and customer-feedback reports; "Report scheduled delivery" weekly or monthly PDF by email; BI integration via API. [S-pricing] [S-reportpdf]
- Premium Analytics add-on: custom reports, advanced filtering, ROI charts, shareable links. [S-pricing]
- ROI/UTM: link tracking (Pro+), Google Analytics integration. [S-pricing] [S-int]

### S8. Collaboration and approvals

- Roles/permissions: profile-level access restrictions (all plans); advanced permissions (Pro+); feature visibility controls (Advanced); Groups. [S-pricing] [S-groups]
- Post approvals: "Message approval workflow" Professional+; multi-step, multi-user, "Any or All" per step; optional bypass for Full Publishing users; email and in-app notifications; posts not approved in time do not publish; auto-reject after 90 days in Needs Approval; mobile approvals. [S-pricing] [S-approvals]
- External (client) approval: Advanced only; "up to 3 external approvers (people without a Sprout account) at no additional cost"; approvers get an email linking to a consolidated stream to Approve, Reject or Comment, no login. [S-pricing] [S-approvals] [S-ext-approvals]
- Reply approvals (inbox replies and review replies): Advanced. [S-pricing] [S-reply-approvals]
- Notes/tasks: team conversations, tasks, internal comments in Approval Activity. [S-bsky-inbox] [S-approvals]
- Audit trail: customer audit trail CSV (e.g. event PUB_APPROVED_POST); Guardian events need the paid Guardian add-on. Plan availability of the base audit trail **UNVERIFIED**. [S-audit] [S-approvals]

### S9. Agency / multi-client

- Groups per client with separate permissions, reporting and profiles; per-group report branding; shared/external calendar links; content approval workflows; Agency Partner Program; "a variety of packages" for agencies (contact sales). [S-agency] [S-groups] [S-branding]
- Client portal: no dedicated portal; external approvers use an emailed review stream. [S-ext-approvals]
- White-labeling: logo on PDF reports only (Pro+). Full white-label **UNVERIFIED**/not offered. [S-branding]
- Per-client billing: **UNVERIFIED**.

### S10. Ads

- Boost published and unpublished ("dark") Facebook/Instagram posts from Compose, calendar or Post Performance report (all plans per pricing table). [S-ads] [S-pricing]
- Ad comments in Smart Inbox (Facebook, Instagram, TikTok ads). [S-engage] [S-pricing]
- Paid performance reports for Facebook, Instagram, X, LinkedIn (Professional+). [S-plan-pro]
- Full ad campaign creation/management in Sprout: **UNVERIFIED** (help article sends targeted ads to Facebook Ads Manager). [S-ads]

### S11. Email

- Email marketing/newsletters for customers: none found (**UNVERIFIED** that none exists). Employee Advocacy (add-on) sends employee newsletters. [S-pricing]
- Email a message/review to anyone (non-Sprout users); export a thread as CSV by email. [S-msgactions] [S-reviews]
- Email-based approvals: approval notifications by email; external approvers approve via an emailed link. [S-approvals] [S-ext-approvals]
- Report delivery: "Send PDF" to up to 25 emails (Pro/Advanced); scheduled weekly/monthly PDF delivery (Advanced). [S-reportpdf]
- Alerts by email: Automated Rules "Send an email" action; spike alerts; review email notifications. [S-rules] [S-pricing] [S-reviews]
- Email inbox integration (email as a channel): not in the Smart Inbox network list. [S-engage]

### S12. Automation and AI agents (and guardrails)

- Acts without a person:
  - Queue/RSS publishing ("Publish posts immediately" option for RSS). [S-rss]
  - Chatbots (scripted, Advanced). [S-bots]
  - Automated Rules (tag/complete/hide/alert; "auto-reply" per Bluesky article), Advanced. [S-rules] [S-bsky-inbox]
  - "Workflows, such as generating AI replies automatically" — requires the Advanced plan and Trellis Plus: Shared Credits. [S-trellis-shared] Whether these AI replies are sent without human approval is **UNVERIFIED**.
  - Trellis Studio scheduled skills (analysis, not posting). [S-studio]
- Guardrails:
  - Account owner must enable OpenAI integrations; most generative features can be disabled. [S-ai-use] [S-ai-list]
  - Reply approvals and post approval workflows. [S-reply-approvals] [S-approvals]
  - Blocked Words (Guardian add-on): block or require approval for listed words in Compose, Smart Inbox replies, Advocacy, mobile; preset profanity lists; attempts logged in the audit trail. [S-blocked]
  - Trellis: 100 credits/seat/month; fair-use cap disables Trellis until month end. [S-trellis-cap]
  - ChatGPT MCP integration is read-only and TikTok data only; "Write actions ... are also not supported." [S-chatgpt]
  - No-training agreements with AI partners. [S-ai] [S-trellis]

### S13. Integrations and API

- Canva, Google Drive, Dropbox, Slack, Bitly, Adobe Express/AEM, Bynder: all plans. HubSpot, Salesforce, Dynamics 365, Zendesk, Marketo: Advanced. Shopify, WooCommerce, Facebook Shops: Professional+. Tableau, Salesforce Marketing Intelligence. Microsoft Teams (Advocacy). [S-pricing] [S-int]
- Zapier: no official Sprout Social app ("Sprout Social has not yet built an integration on Zapier", third party). [3P-zapier-S]
- Public API: Advanced plan, API token generated in Settings; 60 requests/minute enforced from 2026-11-03; "Sprout doesn't offer implementation assistance." [S-api] [S-api-token] [S-nov26] (Sprout's own FAQ page still says "Sprout Social does not have a public API at this time" [S-faq] — stale, contradicted by the help center and pricing page.)
- ChatGPT MCP connector (read-only, TikTok data, Professional+ users). [S-chatgpt]
- Mobile apps: iOS and Android on all plans. [S-pricing]

### S14. Pricing (per seat)

From [S-pricing] (cards and the FAQ "Monthly billing pricing"):

| Plan | Annual billing (per seat/month) | Monthly billing (per seat/month) | Social profiles | Key limits |
|---|---|---|---|---|
| Essentials (new) | $79 | $99 | 5 | No inbox, reviews, cases, approvals, listening; queue 10/day |
| Standard | $199 | $249 | 5 | Inbox, reviews, 10 brand keywords, 90-day conversation history; queue 10/day |
| Professional | $299 | $399 | Unlimited | Approvals, bulk, competitor reports, saved replies; queue 20/day |
| Advanced | $399 | $499 | Unlimited | External approvers, reply approvals, chatbots, rules, API, scheduled PDFs; queue 30/day |
| Enterprise | Custom | Custom | Unlimited | SSO, white-glove onboarding |

- Profile counting: X, Instagram, Pinterest, YouTube, Facebook, TikTok, WhatsApp, LinkedIn Pages and personal profiles, Google My Business, TripAdvisor, Glassdoor, Yelp count; ad accounts and Google Analytics do not. Each review location counts. [S-pricing] [S-networks]
- AI: 100 Trellis credits per seat/month on all plans; Trellis Plus $28/seat/month annual or $35 monthly (1,000 credits); Trellis Plus Shared Credits sold by sales in blocks of 1,000/month. [S-pricing] [S-trellis-shared]
- Add-ons: Premium Analytics, Listening, Employee Advocacy, Professional Services (Standard+); Influencer Marketing and NewsWhip (no Sprout plan required); Guardian (paid add-on per [S-audit]); additional brand keywords [S-plan-std]. Add-on prices not published.
- Free plan: none. Free trial: 30 days, "No credit card required", trial is of Professional. [S-pricing] [S-trial] Scheduled posts stop and profiles disconnect when the trial expires; trial data deleted 30 days later. [S-trial-end]
- Nonprofit pricing: Professional and Advanced, US only. [S-pricing]
- PayPal: **yes** — "Online payment methods include major credit cards and Paypal." [S-pricing]
- History (third party): Essentials and Trellis added in 2026; Trellis Plus $28 from 2026-11-01. [3P-pricingsaas-S]

### S15. Onboarding

1. Trial page: enter email address, "Try 30 days free", no credit card. [S-trial]
2. "Connect at least one social media profile"; data can take up to 24 hours to appear. [S-trial]
3. Connect: Account and settings > Connect a Profile > choose Group > Connect > "Go To" the network > authorize. Requires the "Manage Profiles" permission and native credentials. [S-connect]
   - Bluesky: enter handle, "Go to Bluesky", authorize. [S-bsky-connect]
   - TikTok: Business profile authorization; ad accounts separately. [S-tiktok-connect]
   - Reddit: choose User Profile or Moderated Subreddit, authorize, pick subreddits. [S-reddit]
   - Google Business Profile: Google account with Owner or Manager role on a verified location. [S-reviews]
   - WhatsApp: Meta Business Manager admin; agree to WhatsApp terms; 6-digit PIN; a number not already on the WhatsApp app (or delete the app account first); display-name approval by WhatsApp may be needed. [S-wa-connect] [S-wa-faq]
4. Developer app / API keys: no connect guide asks the customer to create a developer app or obtain network API keys. Sprout's own API tokens exist only for customers using the Sprout Public API (Advanced). [S-connect] [S-api-token]
5. Re-authorization: Instagram every 90 days (standard auth), LinkedIn every 60–365 days, TikTok/Pinterest refresh up to 365 days/1 year; Facebook, X, YouTube, Threads, Bluesky, GBP, WhatsApp have no fixed expiry. [S-reauth]

### S16. Distinctive points for a newcomer

- Per-seat pricing with a hard 5-profile limit on the two cheaper plans; the inbox is not in Essentials. [S-pricing] [S-plan-ess]
- Broadest review coverage of the two (Facebook, GBP, Yelp, Trustpilot, Glassdoor, app stores), but TripAdvisor was just dropped because its API is being retired. [S-reviews] [S-tripadvisor]
- External client approval (no login, email link) is a real feature but only on Advanced and capped at 3 external approvers. [S-ext-approvals]
- Reddit data comes with partner restrictions: no export, not in the API Messages endpoint, not sent to CRMs. [S-reddit]
- AI is metered by credits per seat with a fair-use cap. [S-trellis-cap]
- Nextdoor and Mastodon are not supported. [S-networks] [S-community-nextdoor]
- Published re-authorization cadences per network are a useful model for GeekSocial's own reconnect UX. [S-reauth]
