# Geek-Receptionist — Engineering Specification

**Status:** Multi-Tenant v1 SaaS Implementation (Non-live v0 missed-call text-back code in `GeekAPI/Services/Receptionist/` is being refactored into v1; all single-tenant v0 assumptions are deprecated).
**v1 Target Customer & Call Flow:** Phase 1 targets home service trades (HVAC, plumbing, electrical, roofing) in Southeastern Florida (Broward, Miami-Dade, and Palm Beach counties). **The AI Receptionist answers every inbound call first**, qualifies the job, answers FAQs from the grounded knowledge base, books appointments via MCP, and transfers calls to the business owner/technician only when configured `emergency_keywords` or escalation rules trigger.

---

## 1. Non-Negotiable Architecture & Network Boundaries

Claude Code and Cursor **MUST** enforce this exact three-tier topology across all generated and modified files:

```text
[Next.js Frontend] ──(REST + SignalR)──► [GeekAPI (C#)] ──(Internal HTTP + X-Internal-Api-Key)──► [GeekRepository (C#)] ──(TLS)──► [Supabase PostgreSQL]
[Twilio / Retell]  ──(Signed POST)─────► [GeekAPI (C#)]
[GeekAPI (C#)]     ──(Internal HTTP)───► [GeekMcpGateway (Node/TS)] ──(MCP Protocol)──► [Google Calendar / Outlook / Cal.com / HubSpot]
```

### Mandatory Service Rules
1. **`GeekAPI` (C# REST API + SignalR Hubs):**
   - **MUST** handle all public REST routes, PayPal billing verification, Twilio/Retell webhooks, Twilio Subaccount provisioning, Retell Agent provisioning, SignalR live dashboard hubs, `geek-OAuth` user provisioning, and internal HTTP calls to `GeekRepository` and `GeekMcpGateway`.
   - **MUST NOT** reference `Npgsql`, `Dapper`, or `Microsoft.EntityFrameworkCore`.
   - **MUST NOT** contain `DATABASE_URL` or connect directly to any database.
   - **MUST** communicate with `GeekRepository` exclusively over HTTP using `REPO_URL`. All `/internal/v1/*` (`geek_voice`) calls send header `X-Internal-Api-Key: {GEEK_REPOSITORY_API_KEY}`. Existing non-voice routes continue to use `REPO_URL` and `X-Repo-Key`.
   - **MUST** use SignalR Hubs exclusively for real-time server-to-client dashboard updates (`CallStarted`, `TranscriptTurnAppended`, `AppointmentBooked`, `CallCompleted`). **MUST NOT** use raw C# `System.Net.WebSockets` for client dashboard communication.
2. **`GeekRepository` (C# Data Persistence Service):**
   - **MUST** be the **sole service** connecting to Supabase PostgreSQL (`DATABASE_URL` stored in Railway environment variables).
   - **MUST** reject any HTTP request to `/internal/v1/*` that lacks a valid `X-Internal-Api-Key` header (`401 Unauthorized`).
   - **MUST** use Dapper with explicit SQL statements against the `geek_voice` schema.
3. **`GeekMcpGateway` (Node.js/TypeScript Sidecar):**
   - **MUST** execute tenant-scoped MCP tool calls (`google_calendar`, `outlook`, `cal_com`, `hubspot`) using decrypted OAuth tokens passed per request from `GeekAPI`.
   - **MUST NOT** store persistent state or connect directly to Supabase.

---

## 2. Strict Coding & Generation Constraints

Every file created or modified by Claude Code / Cursor **MUST** obey these rules without exception:

1. **Zero Stubs or Placeholders:** Every controller action, service method, and data store query must contain complete, compilable, production logic. **MUST NOT** emit `throw new NotImplementedException()`, `// TODO`, mock return payloads, or dummy values.
2. **Zero Silent Fallbacks:** If a required environment variable, database row, PayPal verification, Twilio API call, Retell API call, or MCP tool call fails, the service **MUST** fail explicitly and return a structured HTTP error (`400`, `401`, `403`, `404`, `409`, `502`, `503`). **MUST NOT** catch exceptions to return default/fallback data.
3. **Zero Retry Loops:** **MUST NOT** implement `while`/`for` retry loops, exponential backoff wrappers, or Polly retry policies around HTTP calls, Twilio calls, PayPal calls, or database queries. Execute once and propagate failure cleanly.
4. **Readability Over Forced DRY:** Write clear, linear, self-contained methods per vertical slice. **MUST NOT** create generic `IRepository<T>` base classes or deep inheritance hierarchies.
5. **No Markdown in Code:** **MUST NOT** include Markdown syntax inside `.cs`, `.sql`, `.ts`, or `.tsx` files.

---

## 3. Multi-Tenant Database Isolation (`GeekRepository` — `geek_voice` Schema)

Every tenant-scoped database operation in `GeekRepository` **MUST** enforce defense-in-depth isolation:

1. **Connection-Level RLS Context:** Immediately upon opening `NpgsqlConnection`, execute:
   ```sql
   SELECT set_config('app.current_tenant_id', @tenantId, false);
   ```
2. **Query-Level Tenant Predicate:** Every `SELECT`, `UPDATE`, and `DELETE` against `geek_voice` tenant tables **MUST** include `WHERE tenant_id = @TenantId` (or `WHERE id = @TenantId` on `geek_voice.tenants`).

### Canonical `geek_voice` Tables
* `geek_voice.tenants` — Tenant profile, `country_code` (`'US'`), `state_code`, `county`, `industry_vertical`, `requires_addendum`, `addendum_signed_at`, `addendum_ip_address`, `compliance_attested_at`, `status` (`pending_payment`, `active`, `suspended`, `cancelled`).
* `geek_voice.tenant_subscriptions` — `tier_code` (`STARTER`, `STANDARD`, `PRO`, `PREMIUM`), `monthly_price_usd`, `included_minutes`, `overage_rate_usd` (`0.50`), `paypal_subscription_id`, `paypal_vault_token_id`, `current_period_start`, `current_period_end`, `billing_status`.
* `geek_voice.tenant_telephony` — `twilio_subaccount_sid`, `twilio_auth_token_encrypted`, `assigned_phone_number` (E.164), `twilio_phone_sid`, `retell_agent_id`, `a2p_brand_sid`, `a2p_campaign_sid`, `a2p_status`, `sms_notifications_enabled`.
* `geek_voice.receptionist_configs` — `greeting_message`, `voice_id`, `system_prompt_template`, `business_hours_json` (`JSONB`), `qualifying_questions_json` (`JSONB`), `transfer_mode` (`warm_transfer`, `cold_transfer`, `voicemail_only`), `transfer_target_number`, `transfer_caller_id_mode` (`original_caller`, `assigned_ai_number`), `emergency_keywords` (`TEXT[]`), `recording_disclosure_enabled` (`BOOLEAN`, default `TRUE`).
* `geek_voice.tenant_mcp_connections` — `provider` (`google_calendar`, `outlook`, `cal_com`, `hubspot`), `oauth_access_token_encrypted`, `oauth_refresh_token_encrypted`, `token_expires_at`, `external_calendar_id`, `is_active`.
* `geek_voice.knowledge_documents` — `source_type` (`guided_trade_form`, `pdf_upload`, `geek_crawler_url`), `title`, `source_url`, `raw_content`, `status`.
* `geek_voice.knowledge_chunks` — `document_id`, `chunk_index`, `chunk_text`, `embedding` (`vector(384)` for `BAAI/bge-small-en-v1.5`).
* `geek_voice.call_logs` — `twilio_call_sid`, `caller_number`, `call_status`, `started_at`, `ended_at`, `duration_seconds`, `billable_minutes`, `transcript_json` (`JSONB`), `call_summary`, `disposition`, `recording_url`.
* `geek_voice.appointments` — `call_log_id`, `customer_name`, `customer_phone`, `customer_email`, `service_address`, `issue_description`, `scheduled_start`, `scheduled_end`, `external_event_id`, `mcp_provider`.

---

## 4. Handling Existing Non-Live v0 Code (`GeekAPI/Services/Receptionist/`)

The existing code in `GeekAPI/Services/Receptionist/` was merged from `Geek-Receptionist` as a non-live single-tenant v0 missed-call text-back implementation. When modifying or refactoring `GeekAPI/Services/Receptionist/`:

### MUST Keep & Generalize to Multi-Tenant
* **`TwilioSignatureFilter`:** Keep signature verification on all `/api/receptionist/twilio/*` routes (which bypass `ApiKeyMiddleware`). Refactor token lookup so `TwilioSignatureFilter` resolves the tenant's decrypted `twilio_auth_token` from `GeekRepository` by `tenantId` (or `To` phone number) instead of reading a single static `TWILIO_AUTH_TOKEN` environment variable.
* **US/Canada (`+1`) SMS Guard:** Keep strict validation refusing outbound SMS to non-`+1` numbers to prevent SMS-pumping fraud.
* **MMS Media Deletion:** Keep logic that deletes inbound Twilio MMS attachments immediately and never forwards media files.
* **A2P 10DLC Verbal Consent Disclosure:** When missed-call SMS fallback is sent on a tenant's subaccount (only if `sms_notifications_enabled == true` and `a2p_status == 'approved'`), preserve the explicit verbal opt-in disclosure wording in the voice prompt.

### MUST Delete & Replace
* **DELETE** all single-tenant environment variables (`TWILIO_NUMBER`, `OWNER_CELL`, `COMPANY_NAME`, `CALL_MODE`, `RECEPTIONIST_CRM_TENANT_ID`).
* **DELETE** all in-memory `ConcurrentDictionary` state for repeat-caller SMS windows and call screening. All state lookups and writes **MUST** go through `GeekRepository` (`geek_voice.call_logs`).
* **DELETE** any direct CRM or database client calls inside `GeekAPI/Services/Receptionist/`.

---

## 5. Business Logic, Compliance, Voice AI & Billing Specifications

1. **Self-Service Onboarding Gate (`/api/v1/onboarding/validate-eligibility`):**
   - **Country Gate:** Reject if `countryCode != "US"` (`400 Bad Request`). Governing venue in Terms of Service is Palm Beach County, Florida.
   - **Healthcare Exclusion (Permanent):** Reject immediately (`400 Bad Request`) if `industryVertical` matches `Healthcare`, `Medical`, `Dental`, `Clinic`, or `Hospital` (HIPAA-covered entities excluded outright).
   - **Phase 1 Addendum-Pending Block (Temporary until Phase 2 Addendums are built):** Reject (`400 Bad Request` with code `industry_addendum_not_available`) if `industryVertical` matches `Law Firm`, `Legal`, `Financial Services`, `Accounting`, `School`, or `Education` while `FeatureFlags:EnableIndustryAddendums` is `false`. Once Phase 2 addendums are published and `FeatureFlags:EnableIndustryAddendums` is set to `true`, require `addendumSigned == true` and record `addendum_signed_at` (UTC) + `addendum_ip_address`.
   - **Attestation Gate:** Reject (`400 Bad Request`) if `complianceAttested != true`.

2. **Upfront PayPal Subscription + Vaulted Overage Billing (`/api/v1/onboarding/activate-subscription`):**
   - **No Free Trials:** Verify `ACTIVE` PayPal Subscription and a valid PayPal Vaulted Payment Token (`paypal_vault_token_id`) before provisioning any telephony or AI resources.
   - **Exact 4-Tier Subscription Schedule:**
     - `STARTER`: **$30.00/mo** — **90** included minutes — **$0.50/min** overage.
     - `STANDARD`: **$48.00/mo** — **250** included minutes — **$0.50/min** overage.
     - `PRO`: **$148.00/mo** — **1,000** included minutes — **$0.50/min** overage.
     - `PREMIUM`: **$298.00/mo** — **2,000** included minutes — **$0.50/min** overage.
   - **End-of-Cycle Overages:** Billed at **$0.50** per rounded-up billable minute above `included_minutes` via PayPal `/v2/checkout/orders` using `paypal_vault_token_id`.

3. **Twilio Provisioning & Call Transfer Rules:**
   - Provision an isolated Twilio Subaccount, purchase a local US number in `preferredAreaCode`, configure SIP trunking/webhooks to `PUBLIC_BASE_URL/api/receptionist/twilio/voice/inbound/{tenantId}` and `.../status/{tenantId}`, and submit TrustHub A2P 10DLC Brand registration if `enableSmsNotifications == true`.
   - Default `recording_disclosure_enabled` to `TRUE` (Florida all-party consent compliance, Fla. Stat. § 934.03).
   - Execute call transfers according to `receptionist_configs.transfer_mode` (`warm_transfer`, `cold_transfer`, `voicemail_only`) and `transfer_caller_id_mode` (`original_caller` passes inbound caller E.164 on `<Dial callerId="...">`).

4. **Phase 1 Voice Orchestration & Live Call AI Model:**
   - **Phase 1 Voice Orchestrator (Expediency):** Use **Retell AI** connected to each tenant's provisioned Twilio Subaccount number via SIP trunking. During onboarding (`activate-subscription`), `GeekAPI` programmatically creates a tenant-isolated Retell Agent via REST API and binds its webhook URLs to `PUBLIC_BASE_URL/api/receptionist/retell/webhook/{tenantId}`.
   - **Live Call LLM:** Configure the Retell Agent to use **`gpt-4o-mini`** (`temperature = 0.2`, `max_tokens = 150`) for real-time conversation and custom tool-calling (`search_knowledge`, `check_calendar_availability`, `book_appointment`, `transfer_call`).
   - **Prohibited on Live Loop:** **MUST NOT** build custom raw audio WebSocket loops in Phase 1 or use expensive speech-to-speech models (`gpt-4o-realtime`). Migration to direct **Twilio ConversationRelay** is deferred to Phase 2 margin optimization.
   - **Post-Call Summarization:** Run asynchronously on call completion (`voice/status/{tenantId}`) to populate `call_logs.call_summary` and `disposition`.

5. **Knowledge Grounding (`Geek-RAG` / `Geek-Crawler` Contract):**
   - Phase 1 normalizes Guided Trade Forms (`guided_trade_form`) and uploaded PDFs/docs (`pdf_upload`) to Markdown in `geek_voice.knowledge_documents`, chunked and embedded (`vector(384)`, `BAAI/bge-small-en-v1.5`) in `geek_voice.knowledge_chunks`.
   - Phase 2 `Geek-Crawler` ingestion **MUST** write to the exact same `geek_voice.knowledge_documents` (`source_type = 'geek_crawler_url'`) and `geek_voice.knowledge_chunks` tables via `GeekRepository`.

---

## 6. Exact API Route Inventory

### `GeekAPI` Public & Webhook Routes
* `POST /api/v1/onboarding/validate-eligibility`
* `POST /api/v1/onboarding/activate-subscription`
* `PUT  /api/v1/tenants/{tenantId}/config`
* `GET  /api/v1/oauth/mcp/{provider}/callback`
* `POST /api/v1/tenants/{tenantId}/knowledge/guided-form`
* `POST /api/v1/tenants/{tenantId}/knowledge/upload-pdf`
* `POST /api/receptionist/twilio/voice/inbound/{tenantId}` (`TwilioSignatureFilter`)
* `POST /api/receptionist/twilio/voice/status/{tenantId}` (`TwilioSignatureFilter`)
* `POST /api/receptionist/twilio/sms/incoming/{tenantId}` (`TwilioSignatureFilter`)
* `POST /api/receptionist/retell/webhook/{tenantId}` (Retell signature verified)
* `HUB  /hubs/receptionist-live` (SignalR Hub, JWT `tenant_id` group-scoped)

### `GeekRepository` Internal Routes (`X-Internal-Api-Key` Required)
* `POST /internal/v1/onboarding/tenants`
* `GET  /internal/v1/onboarding/tenants/{tenantId}`
* `POST /internal/v1/onboarding/tenants/{tenantId}/activate`
* `GET  /internal/v1/tenants/{tenantId}/telephony`
* `GET  /internal/v1/tenants/{tenantId}/config`
* `PUT  /internal/v1/tenants/{tenantId}/config`
* `GET  /internal/v1/tenants/{tenantId}/mcp/{provider}`
* `PUT  /internal/v1/tenants/{tenantId}/mcp/{provider}`
* `POST /internal/v1/tenants/{tenantId}/knowledge/documents`
* `POST /internal/v1/tenants/{tenantId}/knowledge/search`
* `POST /internal/v1/tenants/{tenantId}/calls`
* `POST /internal/v1/tenants/{tenantId}/appointments`

---

## 7. Frontend Location, Routing & Authentication (`Next.js` + `geek-OAuth`)

1. **Frontend Hosting & Location:**
   - The client-facing onboarding wizard and dashboard live in a dedicated **Next.js (App Router)** application named **Geek-Receptionist-Portal**, hosted on **Vercel** at subdomain `receptionist` of `geekatyourspot.com` (`PORTAL_BASE_URL`, configurable until DNS is provisioned on Vercel), completely decoupled from the statically generated marketing site (`geekatyourspot.com`) and from `GeekRepository`.
   - **Frontend Route Structure:**
     - `/signup` — Unauthenticated 4-step self-service onboarding wizard (Eligibility & Compliance Gate → PayPal Subscription + Vault Checkout → Area Code Selection & Provisioning → Owner Account Creation & Guided Trade Setup).
     - `/login` — Client sign-in page initiating OAuth 2.0 Authorization Code + PKCE against `geek-OAuth`.
     - `/dashboard` — Authenticated master-detail workspace (Live Calls & SignalR Transcripts, Appointments, Knowledge Grounding, MCP Calendar Connections, Transfer & Recording Settings, Billing/Usage).

2. **Authentication & Tenant Identity (`geek-OAuth` / OpenIddict):**
   - Clients **MUST NOT** authenticate directly with Supabase Auth. Supabase is accessible **only** to `GeekRepository`.
   - User authentication is handled exclusively by **`geek-OAuth`** (OpenIddict).
   - During Step 4 of `/signup` (immediately after PayPal activation succeeds), `GeekAPI` provisions the owner's login credentials in `geek-OAuth` with an immutable custom JWT claim: `tenant_id = {tenantId}`.
   - Next.js middleware protects `/dashboard/*`, stores tokens in `HttpOnly`, `Secure`, `SameSite=Lax` cookies, and forwards `Authorization: Bearer <jwt>` to `GeekAPI`.
   - **Zero Trust Tenant Extraction:** `GeekAPI` **MUST** extract `tenantId` exclusively from the validated JWT `tenant_id` claim on all `/api/v1/tenants/*` endpoints and inside `ReceptionistLiveHub.OnConnectedAsync()`. `GeekAPI` **MUST NEVER** trust a `tenantId` passed in a URL or body without verifying it matches the authenticated JWT `tenant_id` claim.

3. **Public Onboarding Calls (`/api/v1/onboarding/*`):**
   - The visitor has no account yet, so the call is made by the Geek-Receptionist-Portal's server, never by the browser, with `X-API-Key: {GEEK_BACKEND_API_KEY}`. `GeekAPI` rejects an onboarding call without a valid key (`401`).
   - Because the portal stands between the visitor and `GeekAPI`, the portal **MUST** forward the visitor's own IP address in `X-Visitor-Ip` and the country Vercel resolved for it (`x-vercel-ip-country`) in `X-Visitor-Country`. `GeekAPI` believes these two headers only on a call whose `X-API-Key` was verified.
   - **US-Only IP Check:** Reject (`400 Bad Request`, code `visitor_country_not_supported`) if `X-Visitor-Country` is not `US`, even when the form's `countryCode` is `US`. Reject (`400 Bad Request`, code `visitor_unverified`) if either header is missing. This runs immediately after the `countryCode` check in Section 5, item 1.
   - The IP address recorded in `addendum_ip_address` is the visitor's `X-Visitor-Ip`, never the portal's connection address.
   - **Rate Limit:** `10` calls per hour per visitor IP; the eleventh is refused (`429 Too Many Requests`, code `rate_limited`).

---

## 8. Railway Environment Variables Inventory (v1 Active & v0 Deprecated)

This section satisfies the cross-references in `Architecture.md` and `GeekAPI/README.md`.

### A. Active v1 Railway Variables (`GeekAPI`)

| Variable | Required | Value / Format | Description |
|----------|:--------:|----------------|-------------|
| `TWILIO_MASTER_ACCOUNT_SID` | ✓ | `AC...` | Parent Twilio Account SID used to provision isolated tenant Subaccounts |
| `TWILIO_MASTER_AUTH_TOKEN` | ✓ | Secret | Parent Twilio Auth Token used to create Subaccounts and manage TrustHub ISV profiles |
| `PUBLIC_BASE_URL` | ✓ | `https://api.geekatyourspot.com` | Public HTTPS base URL of `GeekAPI` (no trailing slash); used for Twilio/Retell webhook URLs and `TwilioSignatureFilter` validation |
| `VALIDATE_TWILIO_SIGNATURE` | | `true` | Defaults to `true`; `false` permitted only in local integration tests |
| `RETELL_API_KEY` | ✓ | Secret | Retell AI API key used to programmatically create/update per-tenant voice agents |
| `RETELL_WEBHOOK_SECRET` | ✓ | Secret | Secret used to verify inbound Retell tool-call and post-call webhooks |
| `PAYPAL_BASE_URL` | ✓ | Sandbox: `https://api-m.sandbox.paypal.com`<br>Live: `https://api-m.paypal.com` | PayPal REST API host for Subscription verification, Vault tokens, and `$0.50/min` overage capture |
| `PAYPAL_CLIENT_ID` | ✓ | Client ID | PayPal REST app client ID |
| `PAYPAL_CLIENT_SECRET` | ✓ | Secret | PayPal REST app secret |
| `REPO_URL` | ✓ | `http://geek-repository.railway.internal:5050` | Railway private network URL for `GeekRepository` (service `geek-repository`, port `5050`); used by every route, voice and non-voice |
| `GEEK_REPOSITORY_API_KEY` | ✓ | Secret | Shared internal API key sent via `X-Internal-Api-Key` to `GeekRepository` |
| `MCP_GATEWAY_URL` | ✓ | `http://geek-mcp-gateway.railway.internal:3000` | Railway private network URL for `GeekMcpGateway` (service `geek-mcp-gateway`, port `3000`, no public domain) |
| `MCP_GATEWAY_API_KEY` | ✓ | Secret | Shared key `GeekAPI` sends to `GeekMcpGateway` in header `X-Internal-Api-Key` |
| `PORTAL_BASE_URL` | ✓ | `https://receptionist.geekatyourspot.com` | Origin of Geek-Receptionist-Portal; OAuth callbacks redirect here (configurable until DNS is provisioned on Vercel) |
| `FeatureFlags__EnableIndustryAddendums` | | `false` | Defaults to `false` (rejects Law, Financial, and School verticals until Phase 2 addendums ship) |

### B. Active v1 Railway Variables (`GeekRepository`)

| Variable | Required | Value / Format | Description |
|----------|:--------:|----------------|-------------|
| `DATABASE_URL` | ✓ | Postgres URI | Supabase PostgreSQL connection string (**MUST live ONLY in `GeekRepository`**) |
| `GEEK_REPOSITORY_API_KEY` | ✓ | Secret | Validates `X-Internal-Api-Key` on all `/internal/v1/*` requests from `GeekAPI` |

### B2. Active v1 Railway Variables (`GeekMcpGateway`)

| Variable | Required | Value / Format | Description |
|----------|:--------:|----------------|-------------|
| `PORT` | ✓ | `3000` | Listening port |
| `MCP_GATEWAY_API_KEY` | ✓ | Secret | Validates `X-Internal-Api-Key` on every request from `GeekAPI` |

### C. Deprecated v0 Variables (Removed — Do Not Use)
The following single-tenant v0 variables are **deleted** and replaced by per-tenant rows in `geek_voice.tenant_telephony` and `geek_voice.receptionist_configs`:
* `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN`, `TWILIO_API_KEY_SID`, `TWILIO_API_KEY_SECRET`, `TWILIO_NUMBER`, `TWILIO_WEBHOOK_BASE_URL` -> Replaced by `PUBLIC_BASE_URL` + `geek_voice.tenant_telephony`
* `OWNER_CELL`, `COMPANY_NAME`, `CALL_MODE`, `RING_SECONDS`, `SCREEN_CALLS`, `CALLER_TEXT`, `REPEAT_TEXT_MINUTES` -> Replaced by `geek_voice.receptionist_configs` and `geek_voice.call_logs`
* `RECEPTIONIST_CRM_TENANT_ID` -> Removed (`GeekAPI` writes exclusively through `GeekRepository`)
