-- Auto-generated seed from src/data/glossary.ts
BEGIN;

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('a-b-testing', 'A/B Testing', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A method of comparing two versions of a webpage, email, or other digital content to determine which performs better.', 'The marketing team ran an A/B test with two different call-to-action button colors to optimize conversion rates.'
FROM geek_glossary.terms t WHERE t.slug = 'a-b-testing';

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 1, 'noun', 'A statistical technique that splits traffic between variant A and variant B to measure differences in performance metrics.', 'Through A/B testing, we discovered that users preferred the simplified checkout flow, increasing our conversion rate by 15%.'
FROM geek_glossary.terms t WHERE t.slug = 'a-b-testing';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('agent-interface', 'Agent Interface', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The user-facing interface through which users interact with AI agents, including chat windows, voice inputs, or integrated messaging platforms. A well-designed agent interface ensures smooth communication between users and the AI system.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'agent-interface';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('agentic-ai', 'Agentic AI', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'AI systems designed to autonomously perform tasks and make decisions with minimal human intervention. Agentic AI can plan, execute, and adapt its behavior based on real-time feedback and changing conditions.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'agentic-ai';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('ai-agent', 'AI Agent', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An intelligent software entity that perceives its environment, makes decisions, and takes actions to achieve specific goals. AI agents are used in customer service, automation, and support to handle complex workflows.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'ai-agent';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('ai-chatbots', 'AI Chatbots', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Conversational AI systems powered by machine learning and natural language processing that simulate human-like interactions. AI chatbots provide instant customer support, answer FAQs, and guide users through processes 24/7.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'ai-chatbots';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('ai-flow-builder', 'AI Flow Builder', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A visual tool that allows users to design and automate complex AI workflows and decision trees without coding. Flow builders enable businesses to create intelligent automation sequences for customer service and lead management.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'ai-flow-builder';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('ai-in-customer-service', 'AI in Customer Service', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The application of artificial intelligence technologies to improve customer support and service delivery. AI in customer service includes chatbots, voice agents, ticket routing, and personalized recommendations.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'ai-in-customer-service';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('ai-knowledge-management', 'AI Knowledge Management', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Systems that organize, store, and retrieve business information to help AI agents provide accurate and relevant answers. Knowledge management ensures AI systems have access to up-to-date FAQs, policies, and product information.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'ai-knowledge-management';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('ai-voice-agent', 'AI Voice Agent', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An AI system that conducts conversations using natural language processing and speech recognition to handle calls and inquiries. Voice agents can schedule appointments, collect information, and resolve issues through voice interaction.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'ai-voice-agent';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('aiaas', 'AIaaS', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Artificial Intelligence as a Service - cloud-based AI platforms and tools offered by vendors to businesses. AIaaS allows companies to access AI capabilities without building infrastructure or hiring specialized AI teams.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'aiaas';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('aiml', 'AIML', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Artificial Intelligence Markup Language - an XML-based language used to create chatbot responses and conversation patterns. AIML enables developers to define how chatbots understand and respond to user inputs.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'aiml';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('analytics', 'Analytics', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The collection, measurement, and analysis of data to understand customer behavior, business performance, and system efficiency. Analytics provide insights that drive decision-making and optimization strategies.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'analytics';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('api', 'API', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Application Programming Interface - a set of protocols, tools, and definitions for building software applications.', 'The company published a public API so developers could integrate their payment system with third-party applications.'
FROM geek_glossary.terms t WHERE t.slug = 'api';

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 1, 'noun', 'A specification that enables different software systems and services to communicate and share data with each other.', 'By using the Stripe API, our team was able to add payment processing to our mobile app without building the entire payment system from scratch.'
FROM geek_glossary.terms t WHERE t.slug = 'api';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('api-integration', 'API Integration', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The process of connecting different software systems and applications through their APIs to share data and functionality. API integration enables seamless communication between business tools and extends platform capabilities.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'api-integration';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('api-keys', 'API Keys', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Unique security tokens used to authenticate and authorize API requests from applications or services. API keys verify that a request is legitimate and track which application is making calls to protect sensitive data.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'api-keys';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('appointment-bookings', 'Appointment Bookings', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A system that automates the scheduling of appointments and meetings between customers and service providers. Appointment booking systems reduce no-shows, optimize staff schedules, and improve customer convenience.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'appointment-bookings';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('asynchronous-messaging', 'Asynchronous Messaging', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A communication method where messages are sent and received at different times rather than in real-time. Asynchronous messaging allows users to respond when convenient without requiring immediate interaction.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'asynchronous-messaging';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('attributes', 'Attributes', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Data properties or characteristics that describe a customer, product, or entity in a system. Attributes are used for segmentation, personalization, and targeting in marketing and customer service automation.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'attributes';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('automobile-chatbot', 'Automobile Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An AI chatbot specialized for the automotive industry to handle inquiries about vehicles, service appointments, financing, and inventory. Automobile chatbots improve the car-buying experience and support dealership operations.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'automobile-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('autoresponder', 'Autoresponder', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An automated system that sends pre-written messages in response to customer inquiries or actions. Autoresponders provide immediate acknowledgment, set expectations, and deliver information without manual intervention.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'autoresponder';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('average-handling-time', 'Average Handling Time', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A metric measuring the average time spent by an agent handling a customer interaction including talk time and after-call work. Reducing average handling time improves efficiency while maintaining service quality.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'average-handling-time';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('average-order-value', 'Average Order Value', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A business metric calculated by dividing total revenue by the number of orders to determine the average transaction value. Increasing average order value is a key strategy for improving business profitability.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'average-order-value';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('average-response-time', 'Average Response Time', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A metric measuring how quickly a customer service team responds to inquiries across all channels. Lower average response times improve customer satisfaction and reduce the likelihood of customer churn.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'average-response-time';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('behavioral-segmentation', 'Behavioral Segmentation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The practice of dividing customers into groups based on their actions, purchase history, and engagement patterns. Behavioral segmentation enables targeted marketing and personalized customer experiences.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'behavioral-segmentation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('bot-cards', 'Bot Cards', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Rich message templates used by chatbots to display formatted content including images, buttons, and interactive elements. Bot cards enhance user experience by presenting information in visually organized and engaging formats.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'bot-cards';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('bounce-rate', 'Bounce Rate', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A metric measuring the percentage of website visitors who leave without interacting or taking any action. High bounce rates indicate that landing pages may need optimization to better engage visitors.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'bounce-rate';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('broadcast', 'Broadcast', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A one-way communication method to send messages to multiple recipients simultaneously without requiring individual responses. Broadcasting is commonly used for announcements, promotions, and bulk messaging campaigns.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'broadcast';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('broadcast-text-message', 'Broadcast Text Message', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An automated SMS message sent to a large group of customers or contacts at the same time for announcements or promotions. Broadcast text messages reach customers directly on their phones with high open rates.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'broadcast-text-message';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('business-chatbot', 'Business Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An AI-powered conversational system designed to handle business operations including customer support, lead generation, and internal processes. Business chatbots streamline workflows and improve operational efficiency.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'business-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('business-process-outsourcing', 'Business Process Outsourcing', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The practice of delegating specific business functions or operations to external third-party providers. Business process outsourcing reduces costs and allows companies to focus on core competencies.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'business-process-outsourcing';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('call-masking', 'Call Masking', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A technology that masks phone numbers in calls between customers and service providers for privacy and security. Call masking allows tracking and recording of calls while protecting the actual phone numbers of participants.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'call-masking';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('call-monitoring-software', 'Call Monitoring Software', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A tool that records, tracks, and analyzes phone calls for quality assurance, training, and compliance purposes. Call monitoring software helps managers evaluate agent performance and identify improvement areas.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'call-monitoring-software';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('call-routing', 'Call Routing', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A system that automatically directs incoming calls to the most appropriate agent or department based on predetermined rules. Call routing reduces wait times and ensures customers reach the right resource quickly.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'call-routing';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('call-to-action', 'Call To Action', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A directive message that prompts users to take a specific action such as clicking a button, making a purchase, or signing up. Effective calls to action drive conversions and guide customers through the desired customer journey.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'call-to-action';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('canned-messages', 'Canned Messages', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Pre-written responses or message templates used by customer service agents to quickly respond to common inquiries. Canned messages improve response time while maintaining consistency in customer communication.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'canned-messages';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('chat-automation', 'Chat Automation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The use of automated systems to handle conversations and respond to messages without human intervention. Chat automation improves response speed, reduces support costs, and ensures 24/7 availability.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'chat-automation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('chat-logs', 'Chat Logs', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Records of all conversations between customers and support agents or chatbots for documentation and analysis. Chat logs provide valuable data for training, compliance, and improving customer service quality.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'chat-logs';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('chatbot', 'Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An AI-powered software application designed to simulate human conversation through text or voice interactions.', 'The customer service chatbot on our website handles common questions 24/7, reducing support ticket volume by 40%.'
FROM geek_glossary.terms t WHERE t.slug = 'chatbot';

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 1, 'noun', 'A conversational agent that uses natural language processing and machine learning to understand user queries and provide relevant responses.', 'Our e-commerce chatbot can recommend products, answer FAQs, and guide customers through the checkout process.'
FROM geek_glossary.terms t WHERE t.slug = 'chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('chatbot-analytics', 'Chatbot Analytics', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Metrics and reporting that track chatbot performance including conversation volume, resolution rates, user satisfaction, and handoff frequency.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'chatbot-analytics';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('chatbot-architecture', 'Chatbot Architecture', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The structural design of a chatbot system including its NLP engine, dialog manager, integrations, and data storage components.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'chatbot-architecture';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('chatbot-archive', 'Chatbot Archive', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A stored collection of past chatbot conversations retained for compliance, training, and quality review purposes.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'chatbot-archive';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('chatbot-automation', 'Chatbot Automation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The use of chatbots to automate repetitive conversational tasks such as FAQs, lead capture, and appointment scheduling without human agents.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'chatbot-automation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('chatbot-avatar', 'Chatbot Avatar', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A visual representation or character image displayed alongside chatbot messages to humanize the interaction and reinforce brand identity.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'chatbot-avatar';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('chatbot-dashboard', 'Chatbot Dashboard', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An administrative interface that displays chatbot analytics, conversation logs, and configuration settings in one place.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'chatbot-dashboard';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('chatbot-decision-tree', 'Chatbot Decision Tree', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A branching flowchart that maps user inputs to predefined chatbot responses and actions based on conditional logic.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'chatbot-decision-tree';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('chatbot-persona', 'Chatbot Persona', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The personality, tone, and characteristics assigned to a chatbot to create a distinctive brand voice. A well-defined chatbot persona helps build customer trust and improves the conversational experience.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'chatbot-persona';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('chatbot-ux', 'Chatbot UX', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The user experience design of chatbot interactions including ease of navigation, response clarity, and conversation flow. Good chatbot UX ensures users can easily complete tasks and feel satisfied with the interaction.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'chatbot-ux';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('chatforms', 'Chatforms', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Interactive forms embedded in chat interfaces that collect user information through conversational prompts. Chatforms improve form completion rates compared to traditional forms by creating a more engaging experience.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'chatforms';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('chatgpt', 'ChatGPT', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A large language model developed by OpenAI that generates human-like responses to user queries. ChatGPT powers many modern chatbot applications and has become a standard for natural language understanding.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'chatgpt';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('claude-ai', 'Claude AI', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An advanced AI assistant created by Anthropic designed for complex reasoning, analysis, and content generation. Claude AI is known for its ability to handle nuanced conversations and provide thoughtful responses.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'claude-ai';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('click-to-open-rate', 'Click To Open Rate', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A metric measuring the percentage of email recipients who click on a link within an email they have opened. Click-to-open rate is a more accurate indicator of email engagement than overall click-through rate.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'click-to-open-rate';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('cloud-contact-center', 'Cloud Contact Center', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A cloud-based customer service platform that manages inbound and outbound communications from a remote infrastructure. Cloud contact centers offer scalability, flexibility, and lower operational costs compared to on-premise solutions.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'cloud-contact-center';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('cognitive-search', 'Cognitive Search', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A search technology that uses AI and natural language processing to understand user intent and deliver more relevant results. Cognitive search goes beyond keyword matching to interpret context and meaning.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'cognitive-search';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('cohort-analysis', 'Cohort Analysis', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A behavioral analysis technique that segments users into groups based on shared characteristics or experiences during a time period. Cohort analysis helps identify trends and patterns in user behavior.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'cohort-analysis';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('communication-channel', 'Communication Channel', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Any medium through which businesses and customers exchange messages including email, chat, SMS, social media, and phone. Multi-channel communication strategies ensure customers can reach support through their preferred channel.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'communication-channel';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('contact-center', 'Contact Center', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A centralized hub where customer service representatives handle inbound and outbound communications. Contact centers manage high volumes of customer interactions across multiple channels.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'contact-center';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('contact-center-automation', 'Contact Center Automation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The use of technology to automate routine tasks in contact centers such as call routing, ticket creation, and response generation. Contact center automation improves efficiency and reduces operational costs.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'contact-center-automation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('contextual-conversation', 'Contextual Conversation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A conversation where AI maintains awareness of previous messages, customer history, and relevant context to provide personalized responses. Contextual conversation creates more natural and relevant interactions.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'contextual-conversation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('conversational-channel', 'Conversational Channel', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Communication platforms designed for two-way dialogue between customers and businesses such as chat, messaging apps, and voice. Conversational channels are more engaging than traditional broadcast communication.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'conversational-channel';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('conversational-commerce', 'Conversational Commerce', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The use of conversational interfaces like chatbots to facilitate buying and selling of products or services. Conversational commerce reduces friction in the purchasing process and increases conversion rates.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'conversational-commerce';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('conversational-flow', 'Conversational Flow', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The logical sequence and structure of a chatbot conversation from initial greeting to resolution. Conversational flow should be natural, intuitive, and guide users toward their goals.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'conversational-flow';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('conversational-landing-page', 'Conversational Landing Page', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A web page that uses chatbot interactions instead of traditional form fields to engage visitors and collect information. Conversational landing pages improve user engagement and form completion rates.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'conversational-landing-page';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('conversational-marketing', 'Conversational Marketing', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Marketing strategies that use real-time, personalized conversations to engage prospects and customers. Conversational marketing builds relationships and accelerates the sales cycle through direct dialogue.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'conversational-marketing';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('conversational-support', 'Conversational Support', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Customer support delivered through chat, messaging, and voice interfaces that simulate natural conversation. Conversational support is more engaging and efficient than traditional ticket-based support.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'conversational-support';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('conversational-ui', 'Conversational UI', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'User interface design that uses natural language dialogue instead of traditional buttons and menus for interaction. Conversational UI is more intuitive and accessible for users unfamiliar with technology.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'conversational-ui';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('conversational-ux', 'Conversational UX', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The user experience design principles applied to conversational interfaces to ensure smooth, natural, and effective dialogue. Good conversational UX requires understanding user intent and designing flows that feel human-like.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'conversational-ux';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('conversion-funnel', 'Conversion Funnel', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The series of stages a customer moves through from initial awareness to final purchase or action. Conversion funnel analysis identifies where customers drop off and opportunities for optimization.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'conversion-funnel';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('crm', 'CRM', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Customer Relationship Management - software systems that track and manage customer interactions, data, and relationships. CRM platforms help sales and support teams collaborate and improve customer lifetime value.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'crm';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('csat', 'CSAT', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Customer Satisfaction Score - a metric measuring how satisfied customers are with a product, service, or interaction on a numerical scale. CSAT surveys help identify satisfaction levels and areas for improvement.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'csat';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('custom-attributes', 'Custom Attributes', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'User-defined data fields added to customer profiles to track information specific to a business. Custom attributes enable more granular segmentation and personalization in marketing and support.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'custom-attributes';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('custom-display', 'Custom Display', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Configuration options that allow customization of how content, forms, or widgets appear to users. Custom display settings enable brands to maintain visual consistency and design control.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'custom-display';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('custom-form', 'Custom Form', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A form created with custom fields and logic tailored to specific business needs and use cases. Custom forms collect specific information required for business processes or data analysis.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'custom-form';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('custom-regex', 'Custom Regex', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Regular expression patterns created to match specific text patterns for validation or data extraction. Custom regex enables precise pattern matching for specialized business needs.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'custom-regex';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('custom-script', 'Custom Script', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Code written specifically to automate custom business logic or integrate with proprietary systems. Custom scripts extend platform functionality beyond standard features.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'custom-script';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-advocacy', 'Customer Advocacy', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The practice of encouraging satisfied customers to promote a brand through reviews, referrals, and testimonials. Customer advocacy programs turn loyal customers into brand ambassadors.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-advocacy';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-analytics', 'Customer Analytics', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The analysis of customer data to understand behaviors, preferences, and patterns. Customer analytics inform strategy decisions and help optimize customer experience.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-analytics';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-delight', 'Customer Delight', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Going beyond meeting customer expectations to create memorable and positive experiences. Customer delight builds loyalty and encourages word-of-mouth promotion.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-delight';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-effort-score', 'Customer Effort Score', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A metric measuring how easy it is for customers to interact with a company or resolve their issues. Lower customer effort scores correlate with higher satisfaction and loyalty.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-effort-score';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-engagement', 'Customer Engagement', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The level of interaction and involvement a customer has with a brand across touchpoints. Higher customer engagement leads to increased loyalty and lifetime value.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-engagement';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-engagement-platform', 'Customer Engagement Platform', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Software that enables businesses to manage, automate, and measure customer interactions across multiple channels. Engagement platforms provide unified views of customer relationships.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-engagement-platform';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-experience', 'Customer Experience', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The overall perception and satisfaction a customer has with a brand based on all interactions and touchpoints throughout their journey.', 'Improving customer experience requires attention to every stage from initial awareness through post-purchase support.'
FROM geek_glossary.terms t WHERE t.slug = 'customer-experience';

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 1, 'noun', 'The sum of all experiences a customer has when interacting with a company, including sales, support, and product quality.', 'Our investment in chatbot technology and personalized support significantly enhanced the customer experience, leading to higher retention rates.'
FROM geek_glossary.terms t WHERE t.slug = 'customer-experience';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-experience-as-a-service', 'Customer Experience as a Service', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A service delivery model where third-party providers manage customer experience functions for businesses. CXaaS allows companies to outsource customer management while focusing on core business operations.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-experience-as-a-service';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-experience-automation', 'Customer Experience Automation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The use of technology to automate and optimize customer experience processes across all touchpoints. Customer experience automation ensures consistent, personalized service at scale.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-experience-automation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-experience-management', 'Customer Experience Management', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The strategy and processes for measuring, analyzing, and improving all customer interactions. Effective CX management requires coordination across sales, marketing, and support teams.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-experience-management';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-experience-program', 'Customer Experience Program', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Organized initiatives designed to systematically improve customer satisfaction and loyalty. CX programs typically include measurement systems, feedback loops, and continuous improvement processes.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-experience-program';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-experience-strategy', 'Customer Experience Strategy', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A comprehensive plan for delivering superior customer experiences aligned with business objectives. A strong CX strategy considers all touchpoints and employee involvement.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-experience-strategy';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-experience-transformation', 'Customer Experience Transformation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A major organizational change initiative focused on fundamentally improving how companies interact with customers. CX transformation often involves technology adoption and cultural shifts.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-experience-transformation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-feedback', 'Customer Feedback', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Information and opinions provided by customers about their experiences with products, services, or interactions. Customer feedback is essential for identifying improvements and understanding satisfaction levels.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-feedback';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-insights', 'Customer Insights', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Deep understanding of customer behaviors, motivations, preferences, and needs derived from data and feedback. Customer insights guide product development, marketing, and service strategy.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-insights';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-journey', 'Customer Journey', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The complete experience of a customer from initial awareness through post-purchase engagement. Mapping the customer journey identifies key touchpoints and optimization opportunities.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-journey';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-lifetime-value', 'Customer Lifetime Value', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The total revenue a customer generates for a business over their entire relationship. Increasing CLV is often more cost-effective than acquiring new customers.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-lifetime-value';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-onboarding', 'Customer Onboarding', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The process of introducing new customers to products or services and helping them achieve initial success. Effective onboarding reduces churn and accelerates time-to-value.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-onboarding';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-persona', 'Customer Persona', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A detailed fictional representation of an ideal customer based on research and data. Customer personas guide marketing messaging, product development, and support strategies.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-persona';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-satisfaction', 'Customer Satisfaction', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A measure of how well products or services meet or exceed customer expectations. Customer satisfaction is a leading indicator of loyalty and retention.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-satisfaction';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-service', 'Customer Service', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The support provided to customers before, during, and after purchase to ensure satisfaction. Quality customer service builds loyalty and differentiates brands in competitive markets.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-service';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-support', 'Customer Support', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The assistance provided to customers when they encounter problems or have questions about products or services. Effective support reduces customer frustration and increases retention.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-support';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-support-automation', 'Customer Support Automation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Technology that automatically handles common customer support tasks like ticketing, routing, and response generation. Support automation improves response times while reducing costs.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-support-automation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-touchpoint', 'Customer Touchpoint', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Any interaction point between a customer and a business including websites, phone calls, emails, and social media. Multiple touchpoints create the overall customer experience.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-touchpoint';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('customer-training', 'Customer Training', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Educational programs designed to help customers understand and effectively use products or services. Customer training reduces support burden and increases product adoption.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'customer-training';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('cx-tech-ecosystem', 'CX Tech Ecosystem', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The collection of software, platforms, and tools integrated to manage customer experience across the organization. A well-designed tech ecosystem enables seamless customer interactions.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'cx-tech-ecosystem';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('deep-learning', 'Deep Learning', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A subset of machine learning using neural networks with multiple layers to learn complex patterns from data. Deep learning powers many AI applications including image recognition and natural language processing.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'deep-learning';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('drip-marketing', 'Drip Marketing', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A marketing strategy that sends automated, pre-written messages to prospects or customers over time. Drip marketing nurtures leads and keeps brands top-of-mind without manual effort.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'drip-marketing';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('e-commerce-chatbot', 'E-Commerce Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A specialized chatbot designed for online retail to help customers browse products, answer questions, and complete purchases. E-commerce chatbots reduce cart abandonment and improve conversion rates.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'e-commerce-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('ecommerce-automation', 'Ecommerce Automation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The use of technology to automate online retail processes including inventory, order processing, and customer communication. Ecommerce automation improves efficiency and customer experience.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'ecommerce-automation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('edtech', 'EdTech', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Educational Technology - software and platforms designed to enhance learning and teaching. EdTech includes learning management systems, tutoring apps, and interactive educational content.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'edtech';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('education-chatbot', 'Education Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A chatbot designed to support learning by answering questions, providing tutoring, and delivering course content. Education chatbots provide personalized learning experiences and reduce instructor workload.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'education-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('embed-chatbot', 'Embed Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A chatbot integrated directly into a website, app, or messaging platform to provide seamless customer interactions. Embedded chatbots eliminate the need for customers to navigate to a separate system.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'embed-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('engaged-users', 'Engaged Users', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Users who actively interact with a product, service, or content showing consistent engagement behaviors. Engaged users have higher lifetime value and are more likely to become advocates.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'engaged-users';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('engagement-rate', 'Engagement Rate', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A metric measuring the percentage of users who interact with content or take specific actions. Engagement rate indicates content relevance and audience interest.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'engagement-rate';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('erp', 'ERP', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Enterprise Resource Planning - integrated software systems that manage business operations across departments. ERP systems centralize data and improve organizational efficiency.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'erp';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('escalation-management', 'Escalation Management', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Processes and systems for routing complex or urgent issues to appropriate personnel or higher authority levels. Effective escalation management ensures critical issues receive prompt attention.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'escalation-management';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('explainable-ai', 'Explainable AI', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'AI systems designed to provide transparency and clarity into how decisions are made. Explainable AI builds trust and helps identify potential biases in algorithmic decision-making.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'explainable-ai';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('facebook-automation', 'Facebook Automation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The use of bots and automated tools to manage Facebook pages, respond to messages, and post content. Facebook automation helps businesses maintain consistent engagement on social media.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'facebook-automation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('faq-chatbot-automation', 'FAQ Chatbot Automation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Automated systems that answer frequently asked questions using AI without human intervention. FAQ automation provides instant answers to common inquiries and reduces support ticket volume.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'faq-chatbot-automation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('feedback-chatbot', 'Feedback Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A chatbot designed to collect customer feedback and survey responses through conversational interfaces. Feedback chatbots gather insights more efficiently than traditional surveys.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'feedback-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('feedback-loop', 'Feedback Loop', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A system that captures customer feedback and uses it to drive continuous improvement. Feedback loops create accountability and ensure customer voices influence product and service decisions.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'feedback-loop';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('feedback-widget', 'Feedback Widget', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An embedded tool on websites or apps that allows users to quickly submit feedback or rate their experience. Feedback widgets make it easy to capture real-time customer sentiment.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'feedback-widget';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('finance-chatbot', 'Finance Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A specialized chatbot for financial services to help customers with account inquiries, transactions, and financial advice. Finance chatbots provide secure, convenient banking experiences.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'finance-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('fine-tuning', 'Fine Tuning', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The process of training a pre-trained AI model on specific data to improve performance for particular tasks. Fine-tuning enables models to adapt to domain-specific language and patterns.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'fine-tuning';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('first-call-resolution', 'First Call Resolution', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A metric measuring the percentage of customer issues resolved during the first interaction. High FCR rates indicate efficient support and improve customer satisfaction.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'first-call-resolution';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('free-web-chat', 'Free Web Chat', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Live chat functionality available on websites at no cost to provide customer support. Free web chat improves customer experience and can be offered by businesses of any size.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'free-web-chat';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('gemini', 'Gemini', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Google''s advanced AI model designed for complex reasoning and multi-modal understanding. Gemini competes with other large language models in generating human-like responses.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'gemini';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('google-calendar', 'Google Calendar', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Google''s cloud-based calendar and scheduling service for managing appointments and events. Google Calendar integrates with chatbots to enable automated appointment booking.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'google-calendar';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('gpt', 'GPT', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Generative Pre-trained Transformer - a type of large language model that generates human-like text. GPT models power many modern AI applications and chatbots.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'gpt';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('gpt-3', 'GPT-3', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'OpenAI''s third-generation language model known for its ability to perform tasks with minimal examples. GPT-3 demonstrated breakthrough capabilities in few-shot learning and natural language understanding.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'gpt-3';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('gpt-4', 'GPT-4', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'OpenAI''s latest generation language model with improved reasoning, reliability, and safety compared to GPT-3. GPT-4 sets new standards for AI capabilities in language understanding and generation.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'gpt-4';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('hard-bounce', 'Hard Bounce', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An email that fails to deliver because the recipient address is invalid or the domain doesn''t exist. Hard bounces should be removed from mailing lists to maintain sender reputation.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'hard-bounce';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('headless-commerce', 'Headless Commerce', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An e-commerce architecture where the frontend presentation is separated from backend business logic. Headless commerce enables flexibility to deliver shopping experiences across multiple channels.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'headless-commerce';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('help-desk', 'Help Desk', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A system or team responsible for providing technical support and resolving user issues. Help desks serve as the first point of contact for technical problems and service requests.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'help-desk';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('human-handover', 'Human Handover', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The transfer of a conversation from a chatbot to a human agent for more complex support. Human handover ensures customers get help when AI cannot resolve their issues.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'human-handover';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('hybrid-chatbot', 'Hybrid Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A chatbot that combines rule-based logic with machine learning to handle both simple and complex conversations. Hybrid chatbots provide flexibility and improve accuracy.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'hybrid-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('in-app-support', 'In App Support', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Customer support integrated directly within a mobile or web application for seamless assistance. In-app support reduces friction and improves user satisfaction without app abandonment.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'in-app-support';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('inbound-call-center', 'Inbound Call Center', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A facility focused on receiving and handling incoming customer calls for support, sales, or inquiries. Inbound centers require efficient call routing and knowledgeable representatives.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'inbound-call-center';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('information-extraction', 'Information Extraction', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An AI process that identifies and extracts relevant data from unstructured text or documents. Information extraction automates data entry and knowledge capture.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'information-extraction';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('information-retrieval', 'Information Retrieval', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The process of finding relevant information from large databases or knowledge bases based on queries. Information retrieval is fundamental to search engines and knowledge management.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'information-retrieval';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('instagram-automation', 'Instagram Automation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The use of tools and bots to automate Instagram tasks like posting, messaging, and engagement. Instagram automation helps brands maintain consistent presence on social media.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'instagram-automation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('instagram-chatbot', 'Instagram Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A chatbot integrated with Instagram Direct Messages to handle customer inquiries and interactions. Instagram chatbots provide shopping assistance and customer support on the platform.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'instagram-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('intelligent-routing', 'Intelligent Routing', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Advanced call or message routing that uses AI to direct interactions to the best available resource. Intelligent routing improves efficiency and customer satisfaction.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'intelligent-routing';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('interoperability', 'Interoperability', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The ability of different systems and platforms to work together and share data seamlessly. Interoperability enables integrated solutions and reduces data silos.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'interoperability';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('ivr', 'IVR', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Interactive Voice Response - a system that uses voice recognition and phone keypads to automate call routing. IVR systems handle simple inquiries without agent intervention.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'ivr';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('journey-mapping', 'Journey Mapping', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The process of visualizing all touchpoints and interactions in a customer''s relationship with a brand. Journey mapping identifies pain points and optimization opportunities.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'journey-mapping';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('keyword-density', 'Keyword Density', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A metric measuring how frequently a keyword appears in content relative to total word count. Keyword density affects search engine rankings and content relevance.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'keyword-density';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('knowledge-base', 'Knowledge Base', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A centralized repository of information including FAQs, guides, and documentation for customers and staff. Knowledge bases reduce support tickets by enabling self-service.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'knowledge-base';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('knowledge-extraction', 'Knowledge Extraction', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The automated process of pulling valuable information from documents and databases. Knowledge extraction feeds AI systems with the information needed to answer questions.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'knowledge-extraction';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('kpi', 'KPI', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Key Performance Indicator - a measurable value showing how effectively goals are being achieved. KPIs guide decision-making and performance management.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'kpi';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('language-detection', 'Language Detection', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'AI technology that automatically identifies the language used in text or speech. Language detection enables multi-language support in chatbots and applications.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'language-detection';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('large-language-models', 'Large Language Models', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Advanced AI models trained on vast amounts of text data to understand and generate language. Large language models power modern chatbots and AI assistants.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'large-language-models';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('lead-generation', 'Lead Generation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The process of attracting and collecting information from prospective customers interested in products or services. Lead generation is crucial for sales pipeline development.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'lead-generation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('lead-generation-process', 'Lead Generation Process', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The systematic workflow from identifying prospects through collecting contact information and qualification. Effective lead generation processes use multiple channels and automation.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'lead-generation-process';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('legacy-systems', 'Legacy Systems', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Older software or hardware systems still in use despite newer alternatives being available. Legacy systems often require special integration efforts with modern solutions.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'legacy-systems';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('live-agent', 'Live Agent', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A human customer service representative providing real-time support through chat, phone, or messaging. Live agents handle complex issues and high-touch customer interactions.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'live-agent';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('live-chat', 'Live Chat', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A real-time messaging service that enables customers to communicate with agents or chatbots instantly. Live chat improves customer satisfaction compared to asynchronous channels.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'live-chat';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('localization', 'Localization', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The process of adapting content and products for specific languages, cultures, and regions. Localization goes beyond translation to ensure cultural relevance.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'localization';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('low-code-platforms', 'Low Code Platforms', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Development environments that allow building applications with minimal hand-coded programming. Low-code platforms accelerate development and reduce technical barriers.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'low-code-platforms';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('machine-learning', 'Machine Learning', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A subset of artificial intelligence that enables systems to learn and improve from data without being explicitly programmed.', 'Our recommendation engine uses machine learning to analyze customer behavior and suggest products they''re likely to purchase.'
FROM geek_glossary.terms t WHERE t.slug = 'machine-learning';

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 1, 'noun', 'A technology that allows computers to identify patterns in data and make predictions or decisions on new data automatically.', 'Machine learning powers our sentiment analysis tool, which automatically categorizes customer feedback as positive, negative, or neutral.'
FROM geek_glossary.terms t WHERE t.slug = 'machine-learning';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('marketing-automation', 'Marketing Automation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The use of software to automate marketing tasks including email, social media, and lead nurturing. Marketing automation improves efficiency and enables personalization at scale.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'marketing-automation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('mcommerce', 'Mcommerce', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Mobile commerce - buying and selling products through mobile devices like smartphones and tablets. Mcommerce has become essential as mobile traffic dominates online shopping.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'mcommerce';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('merchant-services-provider', 'Merchant Services Provider', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A company that processes payments and provides banking services for merchants. Merchant services providers enable businesses to accept credit cards and digital payments.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'merchant-services-provider';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('messenger-api', 'Messenger API', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Facebook''s application programming interface that enables chatbots to integrate with Facebook Messenger. The Messenger API allows businesses to automate conversations at scale.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'messenger-api';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('messenger-chatbot-integration', 'Messenger Chatbot Integration', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The process of connecting a chatbot to Facebook Messenger to automate customer interactions. Messenger chatbot integration reaches customers where they already communicate.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'messenger-chatbot-integration';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('model-context-protocol', 'Model Context Protocol', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A protocol enabling AI models to access tools and context needed for more informed decision-making. The Model Context Protocol extends AI capabilities beyond language understanding.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'model-context-protocol';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('multi-channel-ecommerce', 'Multi Channel Ecommerce', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Selling products across multiple sales channels including websites, marketplaces, and physical stores. Multi-channel ecommerce expands reach and provides customers with shopping flexibility.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'multi-channel-ecommerce';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('multi-channel-support', 'Multi Channel Support', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Providing customer support across multiple channels including email, chat, phone, and social media. Multi-channel support meets customers where they prefer to interact.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'multi-channel-support';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('named-entity-recognition', 'Named Entity Recognition', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An AI technique that identifies and extracts named entities like names, locations, and organizations from text. Named entity recognition helps extract structured information from unstructured data.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'named-entity-recognition';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('natural-language-processing', 'Natural Language Processing', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A branch of artificial intelligence focused on enabling computers to understand, interpret, and generate human language.', 'Natural language processing allows our chatbot to understand customer intent even when they use different phrasing or colloquial language.'
FROM geek_glossary.terms t WHERE t.slug = 'natural-language-processing';

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 1, 'noun', 'A technology that analyzes text and speech to extract meaning, sentiment, and intent from human communication.', 'We use natural language processing to automatically extract key information from customer support emails and categorize them by topic.'
FROM geek_glossary.terms t WHERE t.slug = 'natural-language-processing';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('natural-language-understanding', 'Natural Language Understanding', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A subset of NLP focused on deriving meaning and intent from human language input. NLU enables chatbots to understand customer queries even when phrased differently.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'natural-language-understanding';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('neural-network', 'Neural Network', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A machine learning architecture inspired by biological neural networks that learns patterns from data. Neural networks power deep learning and modern AI applications.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'neural-network';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('oauth', 'OAuth', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An open standard protocol for secure authorization and authentication across applications. OAuth enables single sign-on and secure third-party access without sharing passwords.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'oauth';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('omnichannel', 'Omnichannel', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A strategy providing seamless customer experience across all touchpoints and channels. Omnichannel integration ensures consistent messaging and data across platforms.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'omnichannel';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('omnichannel-chatbot', 'Omnichannel Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A chatbot deployed across multiple channels to provide consistent interactions everywhere customers engage. Omnichannel chatbots maintain conversation context across channels.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'omnichannel-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('omnichannel-messaging', 'Omnichannel Messaging', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A unified system for managing customer messages across all communication channels from one platform. Omnichannel messaging improves efficiency and customer satisfaction.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'omnichannel-messaging';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('on-time-resolution', 'On Time Resolution', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A metric measuring the percentage of customer issues resolved within a targeted timeframe. High on-time resolution rates indicate efficient support processes.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'on-time-resolution';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('opt-out', 'Opt Out', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A customer''s choice to stop receiving marketing communications or messages from a business. Opt-out options are legally required in most jurisdictions for marketing communications.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'opt-out';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('personalization-vs-customization', 'Personalization vs Customization', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Personalization is automated tailoring of experiences based on individual data, while customization lets users manually adjust preferences. Both improve customer satisfaction through tailored experiences.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'personalization-vs-customization';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('personalized-interaction', 'Personalized Interaction', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Customer interactions customized based on individual preferences, history, and behavior. Personalized interactions increase relevance and improve customer satisfaction.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'personalized-interaction';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('personalized-marketing', 'Personalized Marketing', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Marketing strategies that use customer data to deliver tailored messages and offers to individuals. Personalized marketing improves conversion rates and customer loyalty.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'personalized-marketing';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('personalized-support', 'Personalized Support', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Customer support that accounts for individual customer history, preferences, and needs. Personalized support improves satisfaction by demonstrating attentiveness to individual circumstances.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'personalized-support';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('prompt-engineering', 'Prompt Engineering', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The practice of crafting precise instructions for AI models to generate desired outputs. Effective prompt engineering significantly improves AI response quality and relevance.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'prompt-engineering';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('prospects', 'Prospects', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Potential customers who have shown interest in products or services but have not yet made a purchase. Prospects are qualified based on likelihood to convert.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'prospects';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('query-language', 'Query Language', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A language designed to retrieve and manipulate data from databases and knowledge bases. Query languages like SQL enable precise data searching and analysis.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'query-language';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('quick-reply', 'Quick Reply', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Pre-formatted response buttons that allow users to quickly respond to chatbots or messages. Quick replies improve user experience and reduce typing burden.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'quick-reply';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('real-time-chat', 'Real Time Chat', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Live messaging between customers and agents or chatbots with immediate message delivery. Real-time chat enables quick problem resolution and higher satisfaction.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'real-time-chat';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('real-time-engagement', 'Real Time Engagement', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Marketing and support activities that respond immediately to customer actions and behaviors. Real-time engagement capitalizes on immediate customer intent.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'real-time-engagement';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('recommendation-systems', 'Recommendation Systems', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'AI systems that suggest products, content, or services based on user behavior and preferences. Recommendation systems increase conversion and customer satisfaction.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'recommendation-systems';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('request-user-data-node', 'Request User Data Node', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A chatbot function that requests specific user information during a conversation for data collection. Request user data nodes enable personalization and information gathering.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'request-user-data-node';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('return-rate', 'Return Rate', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A metric measuring the percentage of customers who make repeat purchases after an initial transaction. Higher return rates indicate customer satisfaction and loyalty.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'return-rate';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('rule-based-chatbot', 'Rule Based Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A chatbot that operates using predefined rules and decision trees without machine learning. Rule-based chatbots are good for simple, predictable interactions.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'rule-based-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('rule-based-system', 'Rule Based System', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A system that uses explicit rules to make decisions and perform actions without learning from data. Rule-based systems are transparent and predictable but less flexible than AI systems.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'rule-based-system';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('saas', 'SaaS', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Software as a Service - cloud-based software applications accessed via subscription instead of installation. SaaS reduces infrastructure costs and enables automatic updates.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'saas';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('sales-automation', 'Sales Automation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The use of technology to automate sales tasks including lead scoring, follow-ups, and pipeline management. Sales automation improves efficiency and consistency.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'sales-automation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('sales-qualified-lead', 'Sales Qualified Lead', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A prospect who has been vetted by sales and marketing as ready for direct sales engagement. SQLs have higher conversion probability than other leads.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'sales-qualified-lead';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('scenario-automation', 'Scenario Automation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Automating specific business processes or customer scenarios end-to-end with predefined workflows. Scenario automation ensures consistency and reduces manual errors.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'scenario-automation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('sentiment-analysis', 'Sentiment Analysis', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'AI technology that determines the emotional tone or sentiment expressed in text or speech. Sentiment analysis helps identify customer satisfaction and emerging issues.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'sentiment-analysis';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('shared-whatsapp-business-team-inbox', 'Shared WhatsApp Business Team Inbox', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A WhatsApp Business feature allowing multiple team members to manage customer messages in a shared inbox. Team inboxes enable collaboration and faster response times.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'shared-whatsapp-business-team-inbox';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('shopify-analytics', 'Shopify Analytics', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Shopify''s built-in analytics tools that track sales, traffic, and customer behavior. Shopify analytics help businesses understand performance and optimize strategy.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'shopify-analytics';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('smart-responses', 'Smart Responses', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'AI-generated suggested replies based on conversation context and message content. Smart responses save time for support agents by providing relevant response options.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'smart-responses';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('sms-chatbot', 'SMS Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A chatbot accessed through SMS text messages to provide information and support. SMS chatbots reach customers on the device they always carry.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'sms-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('sms-marketing', 'SMS Marketing', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Direct marketing to customers via text messages for promotions, updates, and alerts. SMS marketing has high open rates and immediate delivery.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'sms-marketing';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('software-development-kit', 'Software Development Kit', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A collection of tools and libraries that developers use to build applications. SDKs provide pre-built functionality and reduce development time.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'software-development-kit';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('speech-processing', 'Speech Processing', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Technology that analyzes and understands human speech for applications like voice recognition. Speech processing converts audio into actionable data and insights.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'speech-processing';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('speech-synthesis', 'Speech Synthesis', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Technology that converts text into spoken words using AI-generated voices. Speech synthesis enables voice-based interfaces and accessibility features.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'speech-synthesis';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('speech-to-text-translation', 'Speech To Text Translation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Converting spoken words into written text and optionally translating to another language. Speech-to-text translation breaks language barriers in real-time conversations.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'speech-to-text-translation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('spin-selling', 'Spin Selling', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A sales methodology using investigative questioning to uncover customer needs and problems. Spin selling improves sales effectiveness through consultative approach.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'spin-selling';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('squarespace-chatbot', 'Squarespace Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A chatbot integrated with Squarespace websites to automate customer interactions. Squarespace chatbots help e-commerce and service businesses engage visitors.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'squarespace-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('stock-keeping-unit', 'Stock Keeping Unit', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A unique identifier for inventory management tracking individual product variants. SKUs enable accurate inventory control and sales analysis.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'stock-keeping-unit';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('support-agent', 'Support Agent', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A human representative providing customer service and support to resolve issues. Support agents require product knowledge and communication skills.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'support-agent';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('support-benchmarks', 'Support Benchmarks', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Industry standards and metrics for measuring customer support performance. Support benchmarks help organizations understand performance relative to peers.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'support-benchmarks';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('support-bot', 'Support Bot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An automated chatbot designed specifically for customer support and issue resolution. Support bots handle routine inquiries reducing burden on human agents.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'support-bot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('support-operations', 'Support Operations', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The processes and systems for managing customer support at scale. Support operations include ticketing, scheduling, knowledge management, and quality assurance.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'support-operations';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('support-ticket', 'Support Ticket', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A record of a customer issue or inquiry tracked through the support resolution process. Tickets ensure nothing falls through the cracks in support workflow.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'support-ticket';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('telegram-chatbot', 'Telegram Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A chatbot integrated with Telegram messaging app to automate interactions. Telegram chatbots provide customer service within a popular messaging platform.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'telegram-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('text-to-speech', 'Text To Speech', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Technology that reads text aloud using synthesized or recorded human voices. Text-to-speech improves accessibility and enables audio content delivery.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'text-to-speech';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('ticket-generation', 'Ticket Generation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The automatic creation of support tickets when customers report issues or request help. Ticket generation ensures systematic tracking of all customer requests.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'ticket-generation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('ticket-management', 'Ticket Management', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Systems and processes for tracking, organizing, and resolving customer support tickets. Effective ticket management ensures timely resolution and customer satisfaction.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'ticket-management';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('ticket-routing', 'Ticket Routing', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Automatically directing support tickets to the appropriate agent or team based on criteria. Smart ticket routing improves efficiency and first contact resolution.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'ticket-routing';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('tiered-support', 'Tiered Support', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A support model with different levels of expertise from basic to advanced handling. Tiered support optimizes resources by matching complexity to appropriate expertise.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'tiered-support';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('training-data', 'Training Data', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The dataset used to teach machine learning and AI models to recognize patterns and make predictions. Quality training data directly impacts AI model accuracy and performance.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'training-data';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('transformer-architecture', 'Transformer Architecture', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A deep learning model architecture that processes sequences of data in parallel. Transformer architecture powers modern language models and ChatGPT.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'transformer-architecture';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('trigger', 'Trigger', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An event or condition that automatically initiates an action or workflow in a system. Triggers enable automation by responding to specific events.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'trigger';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('troubleshooting', 'Troubleshooting', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The systematic process of identifying and resolving technical problems and issues. Effective troubleshooting requires methodical approach and logical thinking.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'troubleshooting';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('two-factor-authentication', 'Two Factor Authentication', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A security method requiring two forms of verification to access an account. Two-factor authentication significantly improves account security.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'two-factor-authentication';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('unified-conversations', 'Unified Conversations', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A single conversation thread combining messages across multiple communication channels. Unified conversations provide complete context regardless of channel.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'unified-conversations';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('unified-inbox', 'Unified Inbox', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A centralized inbox combining messages from all customer communication channels. Unified inbox improves efficiency by eliminating channel switching.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'unified-inbox';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('upselling', 'Upselling', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The sales technique of encouraging customers to buy a more expensive or premium version of a product. Upselling increases customer lifetime value and revenue.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'upselling';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('user-engagement', 'User Engagement', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The level of interaction and participation users have with a product or platform. Higher engagement correlates with retention and loyalty.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'user-engagement';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('user-feedback', 'User Feedback', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Information and opinions collected from users about their experiences and satisfaction. User feedback drives product improvements and innovation.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'user-feedback';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('user-interface', 'User Interface', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The visual and interactive elements users interact with in software and applications. Well-designed UI improves usability and user satisfaction.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'user-interface';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('user-preferences', 'User Preferences', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Settings and configurations that users customize to personalize their experience. User preferences enable customization and improve satisfaction.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'user-preferences';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('user-ratings', 'User Ratings', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Numerical or star-based ratings provided by users to evaluate products or services. User ratings influence purchasing decisions and product reputation.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'user-ratings';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('user-segmentation', 'User Segmentation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Dividing users into groups based on shared characteristics or behaviors for targeted strategies. User segmentation enables personalized and relevant experiences.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'user-segmentation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('utm-parameters', 'UTM Parameters', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'URL parameters used to track and identify the source and campaign of website traffic. UTM parameters are essential for measuring marketing campaign effectiveness.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'utm-parameters';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('virtual-agent', 'Virtual Agent', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An AI-powered software agent that autonomously performs tasks or interacts with customers. Virtual agents handle routine work freeing humans for complex tasks.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'virtual-agent';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('virtual-call-center', 'Virtual Call Center', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A cloud-based call center that handles customer calls from distributed locations. Virtual call centers offer flexibility and scalability.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'virtual-call-center';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('virtual-employee', 'Virtual Employee', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An AI system or chatbot that performs employee-like functions in business processes. Virtual employees automate routine work and improve efficiency.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'virtual-employee';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('virtual-receptionist', 'Virtual Receptionist', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An AI-powered system that answers calls, routes them, and provides information. Virtual receptionists provide 24/7 professional greeting for businesses.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'virtual-receptionist';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('visual-chatgpt', 'Visual ChatGPT', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An extension of ChatGPT that can understand and generate images in addition to text. Visual ChatGPT enables multimodal conversation and content creation.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'visual-chatgpt';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('voice-bot', 'Voice Bot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An AI chatbot designed to interact through voice rather than text. Voice bots provide hands-free assistance and accessibility.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'voice-bot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('voice-commerce', 'Voice Commerce', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Shopping through voice commands using smart speakers and voice assistants. Voice commerce enables shopping without screens or keyboards.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'voice-commerce';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('voice-recognition', 'Voice Recognition', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Technology that identifies and authenticates users based on their unique voice patterns. Voice recognition enables secure voice-based authentication.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'voice-recognition';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('voice-user-interface', 'Voice User Interface', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An interface that allows users to interact with systems through voice commands. Voice UI improves accessibility and hands-free operation.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'voice-user-interface';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('web-chat', 'Web Chat', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Live chat functionality on websites enabling real-time communication with visitors. Web chat improves customer engagement and conversion rates.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'web-chat';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('webforms', 'Webforms', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'HTML forms on websites that collect user input and data for various purposes. Webforms are fundamental to lead generation and customer communication.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'webforms';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('webhooks', 'Webhooks', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Automated messages sent from one application to another when specific events occur. Webhooks enable real-time integration and automation between systems.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'webhooks';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('whatsapp-advertising-campaign', 'WhatsApp Advertising Campaign', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Marketing campaigns run through WhatsApp to promote products or services to customers. WhatsApp campaigns reach engaged audiences on their preferred platform.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'whatsapp-advertising-campaign';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('whatsapp-api', 'WhatsApp API', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Meta''s application programming interface enabling businesses to integrate WhatsApp messaging. The WhatsApp API enables automated customer communication at scale.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'whatsapp-api';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('whatsapp-auto-reply', 'WhatsApp Auto Reply', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Automatic messages sent in response to customer messages on WhatsApp. Auto replies provide immediate acknowledgment and set expectations.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'whatsapp-auto-reply';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('whatsapp-automation', 'WhatsApp Automation', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'The use of bots and automation to manage WhatsApp messages and interactions. WhatsApp automation improves efficiency and response times.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'whatsapp-automation';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('whatsapp-broadcast', 'WhatsApp Broadcast', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Sending messages to multiple WhatsApp contacts simultaneously for announcements. WhatsApp broadcast reaches many contacts with one message.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'whatsapp-broadcast';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('whatsapp-business-account', 'WhatsApp Business Account', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A special WhatsApp account for businesses with additional features and tools. Business accounts enable professional communication and automation.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'whatsapp-business-account';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('whatsapp-business-app', 'WhatsApp Business App', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A dedicated app version of WhatsApp designed for business use with professional features. The Business App includes message templates and quick replies.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'whatsapp-business-app';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('whatsapp-commerce', 'WhatsApp Commerce', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Using WhatsApp to facilitate buying and selling of products and services. WhatsApp commerce provides convenient shopping within messaging.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'whatsapp-commerce';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('whatsapp-crm-integration', 'WhatsApp CRM Integration', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Connecting WhatsApp messaging to customer relationship management systems. CRM integration provides customer context during conversations.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'whatsapp-crm-integration';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('whatsapp-link-generator', 'WhatsApp Link Generator', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A tool that generates clickable links to start WhatsApp conversations with businesses. Link generators simplify customer connection initiation.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'whatsapp-link-generator';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('whatsapp-marketing', 'WhatsApp Marketing', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Marketing campaigns and customer engagement conducted through WhatsApp messaging. WhatsApp marketing reaches customers on a platform they actively use.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'whatsapp-marketing';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('whatsapp-qr-code-generator', 'WhatsApp QR Code Generator', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A tool that generates QR codes linking to WhatsApp business accounts. QR codes enable easy connection to business chats.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'whatsapp-qr-code-generator';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('whatsapp-widget', 'WhatsApp Widget', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'An embedded chat widget on websites that opens WhatsApp conversations. WhatsApp widgets simplify customer connection to business accounts.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'whatsapp-widget';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('word-of-mouth', 'Word Of Mouth', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'Marketing through customer recommendations and referrals rather than paid advertising. Word-of-mouth is highly credible and cost-effective.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'word-of-mouth';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('wordpress-chatbot', 'WordPress Chatbot', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'A chatbot plugin integrated with WordPress websites for visitor engagement. WordPress chatbots enhance user experience on content sites.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'wordpress-chatbot';

INSERT INTO geek_glossary.terms (slug, title, category, short_summary, status)
VALUES ('zero-shot-learning', 'Zero Shot Learning', NULL, NULL, 'published')
ON CONFLICT (slug) DO NOTHING;

INSERT INTO geek_glossary.term_definitions (term_id, sort_order, part_of_speech, text, example)
SELECT t.id, 0, 'noun', 'AI model capability to perform tasks without prior training examples in that specific domain. Zero-shot learning enables AI to handle novel tasks quickly.', NULL
FROM geek_glossary.terms t WHERE t.slug = 'zero-shot-learning';

COMMIT;
