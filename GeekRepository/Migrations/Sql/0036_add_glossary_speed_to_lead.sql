-- Additional glossary terms identified during manual linking pass.

BEGIN;

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES (
    'speed-to-lead',
    'Speed to Lead',
    'Marketing Metrics',
    'How quickly a business responds to a new lead after first contact — a key driver of conversion for small businesses.',
    'published'
)
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun',
    'The elapsed time between when a prospect expresses interest and when your business makes first contact. Faster speed to lead keeps prospects engaged before they move to a competitor.',
    'A roofing company uses a website chatbot to book estimates within 60 seconds of a form submission, cutting their average speed to lead from four hours to under two minutes.'
FROM geek_glossary.terms t WHERE t.slug = 'speed-to-lead';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES (
    'search-engine-optimization',
    'Search Engine Optimization',
    'Marketing',
    'The practice of improving website content and structure to rank higher in organic search results and attract qualified traffic.',
    'published'
)
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun',
    'A set of techniques — keyword targeting, content quality, site structure, and technical performance — used to increase a page''s visibility in unpaid search engine results.',
    'A local HVAC company publishes service-area blog posts optimized for "AC repair West Palm Beach," climbing from page three to the top five Google results within six months.'
FROM geek_glossary.terms t WHERE t.slug = 'search-engine-optimization';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES (
    'content-repurposing',
    'Content Repurposing',
    'Marketing',
    'Transforming existing content into new formats and channels to extend reach without creating from scratch.',
    'published'
)
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun',
    'The practice of adapting one piece of content — such as a blog post, webinar, or report — into multiple formats like social posts, email newsletters, or video scripts.',
    'A marketing team turns a 2,000-word case study into a LinkedIn carousel, three email snippets, and a short-form video script using AI repurposing tools.'
FROM geek_glossary.terms t WHERE t.slug = 'content-repurposing';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES (
    'dynamic-creative-optimization',
    'Dynamic Creative Optimization',
    'Marketing',
    'AI-driven ad technology that automatically tests and serves the best-performing creative combinations to each audience segment.',
    'published'
)
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun',
    'An automated approach to digital advertising where AI assembles and tests combinations of headlines, images, and calls-to-action in real time, shifting budget toward top performers.',
    'An e-commerce brand uses DCO to show different product images to returning visitors versus first-time browsers, lifting click-through rates by 18%.'
FROM geek_glossary.terms t WHERE t.slug = 'dynamic-creative-optimization';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES (
    'accounts-payable',
    'Accounts Payable',
    'Accounting',
    'Money a business owes to vendors and suppliers for goods and services received — managed through invoice processing and payment workflows.',
    'published'
)
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun',
    'The accounting function responsible for receiving vendor invoices, verifying charges against purchase orders, obtaining approvals, and scheduling payments on time.',
    'A growing restaurant group automates accounts payable so invoices are captured, matched, and approved within 48 hours instead of two weeks.'
FROM geek_glossary.terms t WHERE t.slug = 'accounts-payable';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES (
    'cash-flow-forecasting',
    'Cash Flow Forecasting',
    'Accounting',
    'Projecting future cash inflows and outflows so a business can plan for surpluses, shortfalls, and financing needs.',
    'published'
)
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun',
    'A financial planning process that estimates how much cash will enter and leave a business over a future period, helping leaders manage liquidity and make informed spending decisions.',
    'A contractor uses a 13-week cash flow forecast to spot a payroll shortfall in advance and secure a line of credit before busy season.'
FROM geek_glossary.terms t WHERE t.slug = 'cash-flow-forecasting';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES (
    'tax-compliance',
    'Tax Compliance',
    'Accounting',
    'Processes and systems that ensure a business calculates, collects, and remits taxes correctly across jurisdictions.',
    'published'
)
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun',
    'The ongoing practice of meeting federal, state, and local tax obligations — including sales tax, VAT, and payroll tax — through accurate calculation, filing, and payment.',
    'A multi-state retailer uses Avalara to automate sales tax calculation at checkout and file returns in every jurisdiction where they have nexus.'
FROM geek_glossary.terms t WHERE t.slug = 'tax-compliance';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES (
    'data-quality',
    'Data Quality',
    'Analytics',
    'The accuracy, completeness, and reliability of data used for analytics, reporting, and automated decision-making.',
    'published'
)
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun',
    'A measure of how fit data is for its intended use — covering accuracy, consistency, timeliness, and completeness across systems and pipelines.',
    'A marketing team runs data quality checks on ad platform exports before feeding them into a budget optimization model, catching duplicate records and missing spend fields.'
FROM geek_glossary.terms t WHERE t.slug = 'data-quality';

COMMIT;
