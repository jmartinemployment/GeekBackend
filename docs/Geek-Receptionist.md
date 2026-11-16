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
   - **MUST** handle all public REST routes, PayPal billing verification, Twilio/Retell webhooks, Twilio Subaccount provisioning, Retell Agent provisioning, SignalR live dashboard hubs, owner logins and their tokens (Section 7), and internal HTTP calls to `GeekRepository` and `GeekMcpGateway`.
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
* `geek_voice.call_logs` — `retell_call_id` (unique; the key a repeated Retell delivery is matched on), `twilio_call_sid` (optional: Retell gives it only when available), `caller_number`, `call_status`, `started_at`, `ended_at`, `duration_seconds`, `billable_minutes`, `transcript_json` (`JSONB`), `call_summary`, `disposition`, `recording_url`.
* `geek_voice.overage_charges` — one row per closed billing period (unique per subscription and `period_start`): `period_start`, `period_end`, `included_minutes`, `used_minutes`, `overage_minutes`, `overage_rate_usd`, `amount_usd`, `status` (`no_overage`, `charging`, `charged`, `failed`), `paypal_order_id`, `paypal_capture_id`, `failure`, `settled_at`.
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
     - **Built (2026-11-16):** each call's billable minutes are its length rounded up to the whole minute, per call, recorded from Retell's webhook. Once an hour `OverageBillingHostedService` finds the subscriptions whose period ended at least two hours ago (room for the last call's delivery) and whose `billing_status` is `ACTIVE`, and for each: closes the period once (the minutes of calls that *started* in it), charges the overage to the saved payment method with no buyer present (`stored_credential` merchant-initiated, the charge's id as `PayPal-Request-Id`, invoice `GR-OVERAGE-{id}`), records `charged` with PayPal's order and capture ids or `failed` with the reason, and then, once PayPal shows a payment after the period began and a later next billing time, starts the next period exactly where the old one ended. Nothing is retried: a refused charge, a PayPal 5xx, no answer, and a charge left with no outcome for 15 minutes are all `failed`, logged at Error, and the reason names the invoice to look up before charging again. A PayPal status other than `ACTIVE` is copied to `billing_status` and stops billing. The sweep closes nothing when the PayPal variables are unset.
     - **Verified:** a $0.50 sandbox charge to a saved token with no buyer present completed in one call (2026-11-16). **Not yet run against a real activated tenant.**
     - **Standing (decided 2026-11-16, built the same day):** a `failed` overage charge, or a subscription PayPal holds other than `ACTIVE` (a failed monthly payment), suspends the tenant (`suspended_at`). Its owner can still sign in, and its number keeps answering for **seven days**; then the sweep has Retell take the agent off the number (`inbound_agents: []`) and records `answering_stopped_at`. Once no overage is unpaid and PayPal holds the subscription `ACTIVE`, the number is put back on its agent first and the tenant is restored to `active`; the store refuses a restore while either is not so. A subscription PayPal `CANCELLED` or `EXPIRED` is billed to the end of its paid period, then the tenant is `cancelled` and its Retell number, agent and model are deleted and its Twilio subaccount closed (which releases the number), one attempt each; `tenant_telephony.released_at` records that all of it went, and anything left is logged at Error by id. The sweep looks at every suspended tenant each hour and follows PayPal's status for a held subscription.
     - **Paying an overage (owner):** `GET /api/v1/tenants/{tenantId}/billing` answers the owner's status, the day the number stops answering, and each unpaid overage (never a PayPal id or a failure's wording). `POST .../billing/overage-charges/{chargeId}/pay` makes a PayPal order for that charge's exact amount (invoice `GR-OVERAGE-{id}-OWNER`) that the owner approves in PayPal, returning to an https address (http only on localhost); `POST .../complete` with PayPal's order id captures it once, accepts it only when PayPal says it COMPLETED for this charge and amount in USD, records it on the charge (`collected_order_id`, `collected_capture_id`, `collected_at`; the charge stays `failed`), and restores a tenant that is then clear at once. A payment PayPal took that cannot be recorded is logged at Critical with the order. The portal's dashboard shows a Subscription card with Pay now, and `/billing/paid` completes the payment.
     - **Not verified:** what a caller hears when Retell has no agent on the number (Retell's documentation does not say); a full cycle on a real activated tenant; the portal screens, which have not been opened with a suspended tenant.

3. **Twilio Provisioning & Call Transfer Rules:**
   - Provision an isolated Twilio Subaccount, purchase a local US number in `preferredAreaCode`, create an Elastic SIP trunk on that Subaccount (origination `sip:sip.retellai.com`, termination by credentials), assign the number to the trunk, import the number into Retell (`/import-phone-number`) bound to the tenant's agent, so a call goes from Twilio straight to Retell and GeekAPI never receives the voice call, and, if `enableSmsNotifications == true`, point the number's SMS webhook at `PUBLIC_BASE_URL/api/receptionist/twilio/sms/incoming/{tenantId}` and submit TrustHub A2P 10DLC Brand registration.
   - Default `recording_disclosure_enabled` to `TRUE` (Florida all-party consent compliance, Fla. Stat. § 934.03).
   - Execute call transfers according to `receptionist_configs.transfer_mode` (`warm_transfer`, `cold_transfer`, `voicemail_only`) and `transfer_caller_id_mode` (`original_caller` passes inbound caller E.164 on `<Dial callerId="...">`).

4. **Phase 1 Voice Orchestration & Live Call AI Model:**
   - **Phase 1 Voice Orchestrator (Expediency):** Use **Retell AI** connected to each tenant's provisioned Twilio Subaccount number via SIP trunking. During onboarding (`activate-subscription`), `GeekAPI` programmatically creates a tenant-isolated Retell Agent via REST API and binds its webhook URLs to `PUBLIC_BASE_URL/api/receptionist/retell/webhook/{tenantId}`.
   - **Live Call LLM:** Configure the Retell Agent to use the model named in the Railway variable **`RETELL_LLM_MODEL`** (set on `GeekAPI` only, no default in code; it must be a model on Retell's published list; `temperature = 0.2`) for real-time conversation and custom tool-calling (`search_knowledge`, `check_calendar_availability`, `book_appointment`, `transfer_call`).
   - **Prohibited on Live Loop:** **MUST NOT** build custom raw audio WebSocket loops in Phase 1 or use expensive speech-to-speech models (`gpt-4o-realtime`). Migration to direct **Twilio ConversationRelay** is deferred to Phase 2 margin optimization.
   - **Post-Call Summarization:** Run asynchronously when Retell's call-ended webhook arrives (`retell/webhook/{tenantId}`) to populate `call_logs.call_summary` and `disposition`. **Built:** the route records `call_ended` and `call_analyzed` (other events are accepted and not recorded); `call_summary` is Retell's own `call_analysis.call_summary`. `disposition` is not set by anything yet.

5. **Knowledge Grounding (`Geek-RAG` / `Geek-Crawler` Contract):**
   - Phase 1 normalizes Guided Trade Forms (`guided_trade_form`) and uploaded PDFs/docs (`pdf_upload`) to Markdown in `geek_voice.knowledge_documents`, chunked and embedded (`vector(384)`, `BAAI/bge-small-en-v1.5`) in `geek_voice.knowledge_chunks`.
   - Phase 2 `Geek-Crawler` ingestion **MUST** write to the exact same `geek_voice.knowledge_documents` (`source_type = 'geek_crawler_url'`) and `geek_voice.knowledge_chunks` tables via `GeekRepository`.

---

## 6. Exact API Route Inventory

### `GeekAPI` Public & Webhook Routes
* `POST /api/v1/onboarding/validate-eligibility`
* `POST /api/v1/onboarding/paypal/subscription` (portal's server, `X-API-Key`; body `{ tenantId, tierCode, returnUrl, cancelUrl }`; only a `pending_payment` tenant; a PayPal subscription to the tier's plan from `PAYPAL_PLAN_{TIER}` with `custom_id` = the tenant; answers `{ subscriptionId, approvalUrl }`)
* `POST /api/v1/onboarding/paypal/vault` (same key and tenant rule; body `{ tenantId, returnUrl, cancelUrl }`; a merchant setup token that saves the visitor's PayPal account; answers `{ setupTokenId, approvalUrl }`)
* `POST /api/v1/onboarding/paypal/vault/complete` (same key and tenant rule; body `{ tenantId, setupTokenId }`; turns the approved setup token into the payment token, once, with the setup token as `PayPal-Request-Id`; answers `{ paypalVaultTokenId }`)
* `POST /api/v1/onboarding/activate-subscription` (activation now also refuses a subscription whose `custom_id` is not this tenant, `400 subscription_not_found`)
* `POST /api/v1/onboarding/create-owner-login` (portal's server, `X-API-Key`; body `{ tenantId, email, password, displayName }`; only an `active` tenant; answers `201 { tenantId, ownerLoginId }`)
* `POST /api/v1/owner-auth/sign-in` (portal's server, `X-API-Key`; body `{ email, password }`; section 7)
* `POST /api/v1/owner-auth/refresh` (portal's server, `X-API-Key`; body `{ refreshToken }`; section 7)
* `POST /api/v1/owner-auth/sign-out` (portal's server, `X-API-Key`; body `{ refreshToken }`; section 7)
* `PUT  /api/v1/tenants/{tenantId}/config`
* `GET  /api/v1/tenants/{tenantId}/calendar/check` (owner's JWT; runs `list_calendars` for the owner's tenant through the same path a phone call uses; `200 { connected: true, calendars }`, `404 calendar_not_connected`, `409 reauthorization_required`)
* `GET  /api/v1/tenants/{tenantId}/oauth/mcp/{provider}/authorize` (owner's JWT; returns `{ authorizationUrl }`)
* `POST /api/v1/tenants/{tenantId}/oauth/mcp/{provider}/complete` (owner's JWT; body `{ code, state, error }`; the portal calls it after Google redirects the owner's browser to the portal's `/oauth/mcp/{provider}/callback`)
* `POST /api/v1/tenants/{tenantId}/knowledge/guided-form`
* `POST /api/v1/tenants/{tenantId}/knowledge/upload-pdf`
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

## 7. Frontend Location, Routing & Authentication (`Next.js` + Geek-Receptionist's own login)

1. **Frontend Hosting & Location:**
   - The client-facing onboarding wizard and dashboard live in a dedicated **Next.js (App Router)** application named **Geek-Receptionist** (GitHub `jmartinemployment/Geek-Receptionist`, folder `~/development/Geek-Receptionist`; the v0 .NET service that held the name before 2026-11-16 is `Geek-Receptionist-v0`), hosted on **Vercel** at subdomain `geek-receptionist` of `geekatyourspot.com` (`PORTAL_BASE_URL`, configurable until DNS is provisioned on Vercel), completely decoupled from the statically generated marketing site (`geekatyourspot.com`) and from `GeekRepository`.
   - **Frontend Route Structure:**
     - `/signup` — Unauthenticated 4-step self-service onboarding wizard (Eligibility & Compliance Gate → PayPal Subscription + Vault Checkout → Area Code Selection & Provisioning → Owner Account Creation & Guided Trade Setup).
     - `/login` — Email and password sign-in page; the portal's server sends them to `GeekAPI`'s `POST /api/v1/owner-auth/sign-in`.
     - `/dashboard` — Authenticated master-detail workspace (Live Calls & SignalR Transcripts, Appointments, Knowledge Grounding, MCP Calendar Connections, Transfer & Recording Settings, Billing/Usage).

2. **Authentication & Tenant Identity (Geek-Receptionist's own login):**
   - Clients **MUST NOT** authenticate directly with Supabase Auth. Supabase is accessible **only** to `GeekRepository`.
   - Owners sign in with Geek-Receptionist's own accounts. **GeekOAuth is not used**: Geek-Receptionist is not a production app and does not touch the shared identity service that production apps sign in through. Its tokens are good for Geek-Receptionist alone, and no other app's token is accepted on its routes.
   - **Accounts:** `geek_voice.owner_logins` (one per tenant, email unique, stored lower-case) and `geek_voice.owner_refresh_tokens` (migration `0061`), in `GeekRepository` behind `/internal/v1/owner-logins` and `/internal/v1/owner-refresh-tokens`. `GeekAPI` hashes the password (ASP.NET Core Identity's `PasswordHasher`, PBKDF2) and the refresh tokens (SHA-256) before they reach `GeekRepository`, which never sees a password or a token. A password is accepted as typed: no length, character or complexity rule.
   - **Creating the login:** during Step 4 of `/signup` (after PayPal activation succeeds) the portal's server calls `POST /api/v1/onboarding/create-owner-login` with `X-API-Key`. The tenant must be `active` (`409 tenant_not_active`), and a tenant or email that already has a login is `409 owner_exists`. Answer `201 { tenantId, ownerLoginId }`.
   - **Signing in:** `POST /api/v1/owner-auth/sign-in { email, password }` answers `200` with an access token (HS256 JWT, issuer `geek-receptionist`, audience `geek-receptionist-portal`, 15 minutes, claims `sub` = owner login, `tenant_id`, `email`) and a refresh token (32 random bytes, 30 days). A wrong password and an unknown email are the same `401 invalid_credentials` and take as long (the stand-in hash for an unknown email is made at startup, and a stored hash made at a lower cost than the hasher now uses is replaced at the owner's next sign-in, so both cost the same). Sign-in has its own cap of the same size as item 3's, counted apart, so signup calls do not use it up. Beside it, an email that has failed 10 sign-ins in an hour is refused without its password being checked until the hour ends (`429 too_many_failed_sign_ins`), whether or not a login has that email, so a password cannot be guessed from many addresses at once. Each sign-in and refresh also answers a device token (HS256 JWT, audience `geek-receptionist-device`, `sub` only, 90 days) that the portal keeps in an httpOnly cookie through sign-out; a sign-in carrying a device token for that same login is not held back by the email cap, so someone who knows the owner's email cannot keep the owner out of a browser they signed in from. The failure count lives in `GeekAPI`'s memory under the email's SHA-256 and starts again when `GeekAPI` restarts. An owner whose subscription is `cancelled` is refused at sign-in, only after the password checks out so a guesser learns nothing, and at the next renewal, which also ends the sign-in (`403 account_cancelled`; decided 2026-11-16). A `suspended` owner is still let in, to see why and to fix the payment. When the account store does not answer, the owner is told so in plain words and never shown an internal service's name; a login whose save went unanswered tells the owner to try signing in first, because it may have been created. An unset or unusable `RECEPTIONIST_JWT_SIGNING_KEY` is `503 owner_auth_not_configured`.
   - **Refresh and sign-out:** `POST /api/v1/owner-auth/refresh { refreshToken }` answers a new pair in the same sign-in. `GeekRepository` spends the presented token and stores its replacement in one transaction (`/internal/v1/owner-refresh-tokens/rotate`), so a renewal is never half done and a failed one leaves the token usable. A token presented again within 60 seconds of being spent is renewed again, because two requests from one browser can renew together and an answer can be lost after the renewal was saved; after that it ends the whole sign-in (`401 refresh_token_reused`). An unknown or expired one is `401 refresh_token_invalid`. `POST /api/v1/owner-auth/sign-out { refreshToken }` ends the sign-in (`204`). A renewal and a sign-out of one owner take the same lock on the owner's row, so a sign-out never misses a replacement stored at the same moment. Nothing is retried, no answer holding a token is cached, and no password, token, hash or email is logged.
   - **The portal's key is required:** every onboarding and owner-auth route refuses a call whose `X-API-Key` was not verified (`401 api_key_required`), a GeekOAuth token included, because `ApiKeyMiddleware` lets a valid GeekOAuth token through without the key.
   - **Which routes read the token:** only routes that name the `ReceptionistBearer` scheme (the calendar routes so far). It is never `GeekAPI`'s default scheme, so a Geek-Receptionist token is accepted nowhere else and a GeekOAuth token is refused on Geek-Receptionist's routes.
   - Next.js middleware protects `/dashboard/*`, stores tokens in `HttpOnly`, `Secure`, `SameSite=Lax` cookies, and forwards `Authorization: Bearer <jwt>` to `GeekAPI`.
   - **Zero Trust Tenant Extraction:** `GeekAPI` **MUST** extract `tenantId` exclusively from the validated JWT `tenant_id` claim on all `/api/v1/tenants/*` endpoints and inside `ReceptionistLiveHub.OnConnectedAsync()`. `GeekAPI` **MUST NEVER** trust a `tenantId` passed in a URL or body without verifying it matches the authenticated JWT `tenant_id` claim.

3. **Public Onboarding Calls (`/api/v1/onboarding/*`):**
   - The visitor has no account yet, so the call is made by the Geek-Receptionist site's server, never by the browser, with `X-API-Key: {GEEK_BACKEND_API_KEY}`. `GeekAPI` rejects an onboarding call without a valid key (`401`), and one carrying a GeekOAuth token instead of the key (`401 api_key_required`).
   - Because the portal stands between the visitor and `GeekAPI`, the portal **MUST** forward the visitor's own IP address in `X-Visitor-Ip` and the country Vercel resolved for it (`x-vercel-ip-country`) in `X-Visitor-Country`. `GeekAPI` believes these two headers only on a call whose `X-API-Key` was verified.
   - **US-Only IP Check:** Reject (`400 Bad Request`, code `visitor_country_not_supported`) if `X-Visitor-Country` is not `US`, even when the form's `countryCode` is `US`. Reject (`400 Bad Request`, code `visitor_unverified`) if either header is missing. This runs immediately after the `countryCode` check in Section 5, item 1.
   - The IP address recorded in `addendum_ip_address` is the visitor's `X-Visitor-Ip`, never the portal's connection address.
   - **Rate Limit:** `10` calls per hour per visitor IP; the eleventh is refused (`429 Too Many Requests`, code `rate_limited`). An IPv6 address counts as its `/64`, so stepping through the addresses of one block is still one visitor. An email or display name holding a control character is refused before the database (`400`, or `401 invalid_credentials` at sign-in).

4. **Calendar Connection (Google Calendar built; Outlook, Cal.com and HubSpot not built):**
   - The OAuth client is a Web client in Google Cloud project `geek-oauth-04-11-2026` ("geek-google-oauth"). Its redirect URI must be `https://geek-receptionist.geekatyourspot.com/oauth/mcp/google_calendar/callback` (on 2026-11-16 it still holds only the earlier `https://receptionist.geekatyourspot.com/...` address, and `PORTAL_BASE_URL` on GeekAPI is still that earlier address; both must change before an owner can connect a calendar) (`PORTAL_BASE_URL` + `/oauth/mcp/google_calendar/callback`), a page of the portal, not a `GeekAPI` route. The Calendar API and the Calendar MCP API (`calendarmcp.googleapis.com`) are enabled on that project.
   - The consent screen lists exactly three scopes: `calendar.events` (create, change and delete events; Google marks it sensitive), `calendar.events.freebusy` and `calendar.calendarlist.readonly`. A person who unticks `calendar.events` is refused (`scope_not_granted`), because booking would fail later. Until Google verifies the app, the consent screen shows an unverified warning and is limited to 100 test users.
   - **The flow, and why Google never calls GeekAPI:** the portal calls `authorize` with the owner's JWT and sends the owner's browser to the returned Google address. Google sends the browser back to the portal's callback page, which holds the owner's session. That page reads `code`, `state` and `error` from the query and calls `complete` with the owner's JWT. No `GeekAPI` route in this flow is open without a token.
   - **Tenant checks:** the tenant comes from the token's `tenant_id` claim on both routes; a different tenant in the URL is `403 tenant_forbidden`, a token with no claim is `403 tenant_unverified`, and a tenant that has not paid is `409 tenant_not_active` (on `authorize`). `complete` also refuses, `403 tenant_forbidden`, an approval whose sealed state names a different tenant than the token. That check is what stops someone from starting a connection for their own tenant and getting a stranger to approve it with the stranger's Google account. `tenant_id` is put in a token only by Geek-Receptionist's own sign-in (item 2), from the owner login of that tenant, and a tenant has one owner login (a unique index), so a token that carries it is the tenant's owner by construction; a future member or staff login must be given a different claim, because the tenant routes check only that `tenant_id` matches and make no owner-or-member distinction. Every sealed value (OAuth state, access token, refresh token, Twilio auth token) is bound to its purpose as AES-GCM associated data, so one cannot be opened as another. A connection is refused (`400 scope_not_granted`) unless the owner granted all three scopes on the consent screen, because each tool needs its own. Until the portal calls this and an owner signs in, both routes can only refuse.
   - **What `complete` does:** it checks the sealed state (tenant, provider, 10-minute expiry; AES-256-GCM under `VOICE_ENCRYPTION_KEY`) and the tenant match **before** it spends Google's one-time code, exchanges the code once with no retry, encrypts both tokens, saves them through GeekRepository and answers `200 { connected: true }`. Every refusal is a `400`, `403`, `404`, `409` or `503` with a `code` the portal shows the owner.
   - **Portal, first piece:** the site `Geek-Receptionist` (GitHub, Vercel project `geek-receptionist` at `https://geek-receptionist.geekatyourspot.com`, live 2026-11-16) has `/dashboard` with the calendar status and a connect button, and `/oauth/mcp/google_calendar/callback`. Its sign-in still goes through GeekOAuth and must be replaced by the email and password sign-in of item 2; the `geek-receptionist` client was removed from GeekOAuth (e49a8de). Nothing has run with a real owner's login. **Not built:** `/signup`, the rest of the dashboard, hosting at `geek-receptionist.geekatyourspot.com`, and anything that creates a tenant without PayPal. `TWILIO_MASTER_*` was set on GeekAPI on 2026-11-15 to the owner's one Twilio account, which Twilio confirmed is an active full main account, so it can be the parent of every tenant's subaccount; The three PayPal values were set on GeekAPI on 2026-11-16 for the sandbox; the client ID and secret were confirmed by a sandbox token, but `PAYPAL_BASE_URL` was set to the sandbox website (`https://sandbox.paypal.com`) and must be the API address `https://api-m.sandbox.paypal.com`. The sandbox's Geek-Receptionist product (`PROD-2VY07554LB818101E`) has one plan per tier, each one monthly USD cycle with no trial: Starter `P-93Y59179GB9223215NL3BFNI` ($30, made earlier), Standard `P-8LX7403571151502NNL5QMQA` ($48), Pro `P-8CS30918MX536813NNL5QMQI` ($148), Premium `P-7S735698AD441763PNL5QMQI` ($298), the last three made 2026-11-16. A plan named Unlimited ($99) in the same product matches no tier and is refused by the verifier. The sandbox app has Save payment methods (Vault) enabled, confirmed on 2026-11-16 by a setup token PayPal accepted (`PAYER_ACTION_REQUIRED`); without it PayPal answers `403 NOT_AUTHORIZED` and no vault token, and so no signup, is possible. A change to the app's features reaches only an access token issued after it, and PayPal hands back a cached token for up to nine hours. The live app needs the same box ticked before going live. Overage charging itself (counting minutes at cycle end and charging the vault token through `/v2/checkout/orders`) is not built. **Next:** correct `PAYPAL_BASE_URL`, build `/signup`, then run a real sandbox signup to create the first owner login.
   - **Gateway transport:** `GeekMcpGateway` is an MCP client of Google's hosted Calendar MCP server (`https://calendarmcp.googleapis.com/mcp/v1`, Developer Preview, Streamable HTTP, the tenant's access token as a bearer header), using the official MCP SDK. Verified without a token on 2026-11-15: the server lists its tools, and a call with a bad token is `401`. **Not yet verified with a real tenant token:** that `create_event` is permitted by the scopes the consent screen holds (Google's documentation lists only read-only scopes), and the exact shape of each tool's result.

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
| `RETELL_WEBHOOK_SECRET` | ✓ | Secret | The Retell API key that carries the **Webhook** badge in Retell's dashboard (Settings, API Keys); `X-Retell-Signature` is checked with it, because Retell signs deliveries with that key only. The account has two keys (checked 2026-11-16): `RETELL_API_KEY` is "Geek @ Your Spot LLC" and this one is "Secret Key", the badged one. Unset: the webhook answers 503 and no signup is activated |
| `RETELL_LLM_MODEL` | ✓ | Model name from Retell's list, e.g. `gpt-4.1-mini` | The live-call model every tenant's Retell agent uses at `temperature = 0.2`; a variable of its own, never shared with Content Creator's model variables; unset: activation fails with 503 `retell_not_configured` |
| `PAYPAL_BASE_URL` | ✓ | Sandbox: `https://api-m.sandbox.paypal.com`<br>Live: `https://api-m.paypal.com` | PayPal REST API host for Subscription verification, Vault tokens, and `$0.50/min` overage capture |
| `PAYPAL_CLIENT_ID` | ✓ | Client ID | PayPal REST app client ID |
| `PAYPAL_CLIENT_SECRET` | ✓ | Secret | PayPal REST app secret |
| `PAYPAL_PLAN_STARTER`, `PAYPAL_PLAN_STANDARD`, `PAYPAL_PLAN_PRO`, `PAYPAL_PLAN_PREMIUM` | ✓ | PayPal plan id (`P-...`) | The plan /signup subscribes a visitor to for each tier. Sandbox: `P-93Y59179GB9223215NL3BFNI`, `P-8LX7403571151502NNL5QMQA`, `P-8CS30918MX536813NNL5QMQI`, `P-7S735698AD441763PNL5QMQI`; the live account needs its own. Unset: that tier's signup is refused `503 billing_not_configured`; activation checks the plan's price, so a wrong plan is refused, never charged |
| `REPO_URL` | ✓ | `http://geek-repository.railway.internal:5050` | Railway private network URL for `GeekRepository` (service `geek-repository`, port `5050`); used by every route, voice and non-voice |
| `GEEK_REPOSITORY_API_KEY` | ✓ | Secret | Shared internal API key sent via `X-Internal-Api-Key` to `GeekRepository` |
| `MCP_GATEWAY_URL` | ✓ | `http://geek-mcp-gateway.railway.internal:3000` | Railway private network URL for `GeekMcpGateway` (service `geek-mcp-gateway`, port `3000`, no public domain) |
| `MCP_GATEWAY_API_KEY` | ✓ | Secret | Shared key `GeekAPI` sends to `GeekMcpGateway` in header `X-Internal-Api-Key` |
| `PORTAL_BASE_URL` | ✓ | `https://geek-receptionist.geekatyourspot.com` | Origin of the Geek-Receptionist site; Google redirects the owner's browser to `PORTAL_BASE_URL/oauth/mcp/{provider}/callback`, so it is also the origin of the registered Google redirect URI (configurable until DNS is provisioned on Vercel); unset: a calendar connection fails with 503 `oauth_not_configured` |
| `GOOGLE_OAUTH_CLIENT_ID` | ✓ | `...apps.googleusercontent.com` | Web OAuth client in Google Cloud project `geek-oauth-04-11-2026` used to connect a tenant's Google Calendar; unset: a connection fails with 503 `oauth_not_configured` |
| `GOOGLE_OAUTH_CLIENT_SECRET` | ✓ | Secret | That client's secret; held by `GeekAPI` alone and never logged |
| `RECEPTIONIST_JWT_SIGNING_KEY` | ✓ | Secret, 32 or more bytes, base64 | Signs and checks Geek-Receptionist's owner tokens; held by `GeekAPI` alone, used by no other app. Unset, not base64 or shorter than 32 bytes: sign-in and refresh are `503 owner_auth_not_configured` and the `ReceptionistBearer` scheme accepts no token. Changing it makes every access token invalid, but it does not sign anyone out: a refresh token does not depend on the key, so an owner's next request renews with the new key |
| `VOICE_ENCRYPTION_KEY` | ✓ | Secret, 32 bytes, base64 | AES-256-GCM key held by `GeekAPI` alone; encrypts each tenant's Twilio auth token (`twilio_auth_token_encrypted`) and calendar/CRM OAuth tokens (`oauth_*_token_encrypted`) before they reach `GeekRepository`, which stores ciphertext only. Unset or not 32 bytes: the operation that needs it fails with 503 `encryption_unavailable` |
| `FeatureFlags__EnableIndustryAddendums` | | `false` | Defaults to `false` (rejects Law, Financial, and School verticals until Phase 2 addendums ship) |

### B. Active v1 Railway Variables (`GeekRepository`)

| Variable | Required | Value / Format | Description |
|----------|:--------:|----------------|-------------|
| `DATABASE_URL` | ✓ | Postgres URI | Supabase PostgreSQL connection string (**MUST live ONLY in `GeekRepository`**) |
| `GEEK_REPOSITORY_API_KEY` | ✓ | Secret | Validates `X-Internal-Api-Key` on all `/internal/v1/*` requests from `GeekAPI` |

### B1. `GeekMcpGateway` Routes (folder `GeekMcpGateway/`, `Dockerfile.gateway`)

An MCP client, stateless, private network only. Every route except `GET /health` needs `X-Internal-Api-Key` (`401 unauthorized` otherwise, checked before the body is read). `GeekAPI` decrypts the tenant's access token and sends it in the body of each call; the gateway opens one MCP session to Google for that call, uses the token once, closes the session, and never stores, logs or returns the token. No retry, the SDK's reconnection included. Only `provider: "google_calendar"` is accepted (`400 provider_not_supported`).

* `POST /v1/mcp/tools` body `{ provider }` answers `200 { tools: [{ name, description, inputSchema }] }`, the offered tools exactly as Google describes them. No token is needed.
* `POST /v1/mcp/call` body `{ provider, accessToken, tool, arguments }` answers `200 { tool, content, structuredContent }`, whatever the tool returned. The offered tools are `list_calendars`, `list_events`, `get_event`, `suggest_time` and `create_event`; any other tool is `400 tool_not_allowed`, so the receptionist cannot delete or change a tenant's events. Each tool accepts only the arguments it needs (`list_events`: `calendarId`, `startTime`, `endTime`, `timeZone`, `orderBy`, `pageSize`, `pageToken`; `create_event`: `calendarId`, `summary`, `startTime`, `endTime`, `timeZone`, `description`, `location`; and so on in `GeekMcpGateway/src/mcp.ts`), and any other argument is `400 argument_not_allowed`. In particular `create_event` takes no attendees, attachments, meeting links or recurrence, and the gateway adds `notificationLevel: NONE` after the caller's arguments, so a booking never sends an invitation from the owner's calendar however the arguments were built. `suggest_time` takes at most five attendee emails. Google validates the rest, and the argument names come from `/v1/mcp/tools`. Google's tool descriptions say to send `startTime` and `endTime` as local time with no offset and a `timeZone` only when one is known.
* One deadline of 20 seconds covers the whole call, the MCP initialize step included; the body must arrive within 10 seconds (`408 body_timeout`, connection closed); an `accessToken` must be printable ASCII with no spaces (`400`). A real round trip to Google measured 110 to 270 ms on 2026-11-15, but that was a refused token, which stops at the first step; a full session (initialize, notification, call) was not timed with a valid token and is not cached, because the gateway keeps no state.
* **`create_event` has no idempotency key** (Google's tool takes no event id). A `create_event` that fails or times out may still have booked, so the caller must treat that outcome as unknown and look for the event with `list_events` over that time before booking again; it must never simply repeat the call. The booking step of the receptionist (not built) owns this.
* A `422 tool_error` carries Google's own error text in `content`. `GeekAPI` keeps it: `McpGatewayClient` turns it into the code `tool_error` and drops the text, so it never reaches the portal or an owner. If the receptionist's model needs the text to recover, that is a decision for the booking step. The gateway logs only the length of the text, so whether Google's wording for an expired credential matches the pattern that makes it `401 token_rejected` can be checked in the first real runs.
* Failures: `400 invalid_request | provider_rejected | tool_not_allowed`, `401 token_rejected` (the access token was refused or has expired, so `GeekAPI` must refresh it; not built), `403 provider_forbidden`, `413 body_too_large`, `422 tool_error` (the tool ran and reported an error; the body carries its `content`), `502 provider_unavailable | provider_unreachable`.
* `GeekAPI` calls it through `CalendarTools`: it decrypts the tenant's stored access token, renews it with Google first when it has lapsed or lapses within two minutes (saving the new access token and expiry and keeping the refresh token), then makes one gateway call. Google refusing the refresh token (`invalid_grant`), or the gateway still answering `401 token_rejected`, is `409 reauthorization_required`: the owner must connect again, and nothing is renewed twice or repeated. `MCP_GATEWAY_URL` or `MCP_GATEWAY_API_KEY` unset is `503 gateway_not_configured`. Nothing in a phone call uses `CalendarTools` yet.

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
