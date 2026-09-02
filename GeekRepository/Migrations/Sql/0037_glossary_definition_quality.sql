-- Improve definitions and summaries for the most-linked glossary terms.

BEGIN;

-- marketing-automation (121 links)
UPDATE geek_glossary.terms
SET category = 'Marketing',
    short_summary = 'Software that automates repetitive marketing tasks — email, lead nurturing, and campaign workflows — so small teams can personalize at scale.'
WHERE slug = 'marketing-automation';

UPDATE geek_glossary.term_definitions d
SET text = 'Software that runs marketing workflows — such as welcome emails, lead scoring, and follow-up sequences — without manual repetition. Marketing automation helps small businesses stay consistent with prospects while freeing the team for strategy.',
    example = 'A local gym sets up an automated email series that sends class schedules and trial reminders to new sign-ups, converting 22% more trials without adding staff.'
FROM geek_glossary.terms t
WHERE d.term_id = t.id AND t.slug = 'marketing-automation' AND d.sort_order = 0;

-- analytics (75 links)
UPDATE geek_glossary.terms
SET category = 'Analytics',
    short_summary = 'Measuring and interpreting data from websites, ads, and customer interactions to guide smarter business decisions.'
WHERE slug = 'analytics';

UPDATE geek_glossary.term_definitions d
SET text = 'The practice of collecting, measuring, and interpreting data from marketing channels, websites, and operations to understand what is working and what to change next.',
    example = 'A home-services company reviews weekly analytics to see which ad campaigns drive booked appointments, then shifts budget away from underperforming keywords.'
FROM geek_glossary.terms t
WHERE d.term_id = t.id AND t.slug = 'analytics' AND d.sort_order = 0;

-- machine-learning (72 links) — keep dual definitions, enrich first
UPDATE geek_glossary.terms
SET category = 'Technology',
    short_summary = 'AI systems that learn patterns from data to make predictions and recommendations without being manually programmed for every scenario.'
WHERE slug = 'machine-learning';

UPDATE geek_glossary.term_definitions d
SET text = 'A branch of artificial intelligence where software improves by finding patterns in data — powering recommendations, forecasting, and automated decisions in marketing and operations.',
    example = 'An online retailer uses machine learning to suggest add-on products based on what similar customers bought, lifting average order value by 12%.'
FROM geek_glossary.terms t
WHERE d.term_id = t.id AND t.slug = 'machine-learning' AND d.sort_order = 0;

-- engagement-rate (32 links)
UPDATE geek_glossary.terms
SET category = 'Marketing Metrics',
    short_summary = 'The share of your audience that interacts with content — clicks, comments, shares, or other actions — indicating how relevant your message is.'
WHERE slug = 'engagement-rate';

UPDATE geek_glossary.term_definitions d
SET text = 'A metric showing what percentage of people who see your content take an action — such as liking, clicking, commenting, or sharing. Higher engagement usually signals content that resonates with your audience.',
    example = 'A bakery tracks engagement rate on Instagram Reels versus static posts and doubles down on recipe videos that average 8% engagement versus 2% for photos.'
FROM geek_glossary.terms t
WHERE d.term_id = t.id AND t.slug = 'engagement-rate' AND d.sort_order = 0;

-- kpi (28 links)
UPDATE geek_glossary.terms
SET category = 'Business Metrics',
    short_summary = 'A measurable number that tracks progress toward a specific business goal — the scoreboard for whether your strategy is working.'
WHERE slug = 'kpi';

UPDATE geek_glossary.term_definitions d
SET text = 'Key Performance Indicator — a specific, measurable value tied to a business objective. KPIs turn vague goals like "grow sales" into trackable targets such as monthly leads or cost per acquisition.',
    example = 'A SaaS startup sets three KPIs for Q1: demo requests per week, trial-to-paid conversion rate, and customer acquisition cost under $150.'
FROM geek_glossary.terms t
WHERE d.term_id = t.id AND t.slug = 'kpi' AND d.sort_order = 0;

-- personalized-marketing (22 links)
UPDATE geek_glossary.terms
SET category = 'Marketing',
    short_summary = 'Tailoring messages, offers, and experiences to individual customers based on their behavior, preferences, or segment.'
WHERE slug = 'personalized-marketing';

UPDATE geek_glossary.term_definitions d
SET text = 'Marketing that uses customer data — purchase history, browsing behavior, location, or preferences — to deliver relevant messages instead of one-size-fits-all campaigns.',
    example = 'An HVAC company sends maintenance reminders timed to each customer''s last service date and local weather patterns, improving repeat booking rates.'
FROM geek_glossary.terms t
WHERE d.term_id = t.id AND t.slug = 'personalized-marketing' AND d.sort_order = 0;

-- conversion-funnel (21 links)
UPDATE geek_glossary.terms
SET category = 'Marketing',
    short_summary = 'The path prospects follow from first awareness to taking action — and where they drop off along the way.'
WHERE slug = 'conversion-funnel';

UPDATE geek_glossary.term_definitions d
SET text = 'The stages a potential customer moves through — awareness, interest, consideration, and purchase — mapped to show where people leave before converting. Funnel analysis highlights the biggest leaks to fix.',
    example = 'A law firm discovers 60% of visitors abandon the contact form at the phone-number field, so they simplify the form and recover 40 more leads per month.'
FROM geek_glossary.terms t
WHERE d.term_id = t.id AND t.slug = 'conversion-funnel' AND d.sort_order = 0;

-- api-integration (18 links)
UPDATE geek_glossary.terms
SET category = 'Technology',
    short_summary = 'Connecting separate software tools so they share data automatically — eliminating manual exports and duplicate entry.'
WHERE slug = 'api-integration';

UPDATE geek_glossary.term_definitions d
SET text = 'The process of linking two or more business applications through their APIs so data flows between them without manual copying. Integrations keep CRM, marketing, and accounting systems in sync.',
    example = 'A Shopify store integrates with Mailchimp so new customers are automatically added to a welcome email list within minutes of purchase.'
FROM geek_glossary.terms t
WHERE d.term_id = t.id AND t.slug = 'api-integration' AND d.sort_order = 0;

-- keyword-density (16 links)
UPDATE geek_glossary.terms
SET category = 'SEO',
    short_summary = 'How often a target keyword appears in a piece of content relative to total word count — one signal search engines use to understand page relevance.'
WHERE slug = 'keyword-density';

UPDATE geek_glossary.term_definitions d
SET text = 'The frequency of a target keyword or phrase within a page compared to the total word count. Balanced keyword density helps search engines understand topic relevance without keyword stuffing.',
    example = 'A plumber writing a "water heater repair" guide naturally uses the phrase in the title, first paragraph, and one subheading — about 1.5% density — without forcing awkward repetition.'
FROM geek_glossary.terms t
WHERE d.term_id = t.id AND t.slug = 'keyword-density' AND d.sort_order = 0;

-- crm (15 links)
UPDATE geek_glossary.terms
SET category = 'Sales',
    short_summary = 'Software that stores customer and prospect information, tracks interactions, and helps teams manage relationships through the sales cycle.'
WHERE slug = 'crm';

UPDATE geek_glossary.term_definitions d
SET text = 'Customer Relationship Management — a system for storing contact details, logging conversations, tracking deals, and coordinating follow-ups across your sales and support teams.',
    example = 'A B2B consultant uses a CRM to see every email, call, and proposal sent to a prospect before a discovery call, so nothing falls through the cracks.'
FROM geek_glossary.terms t
WHERE d.term_id = t.id AND t.slug = 'crm' AND d.sort_order = 0;

COMMIT;
