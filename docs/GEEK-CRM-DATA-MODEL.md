# Geek-CRM — Data Model (v0, for review)

**Status:** v0 implemented (schema, store, receptionist writes, Apollo import). Custom properties and the NAICS lookup have an API (Phase 1); the SIC list is not loaded yet. The canonical contract is `GeekApplication/Schemas/GeekCrm.xsd` (see §2).
**Schema:** `geek_crm` in GeekRepository (EF Core, PostgreSQL), reached only through GeekAPI.
**First writer:** Geek-Receptionist (calls, texts, voicemails, AI qualification answers).
**Later:** the Geek-CRM UI (see `Geek-CRM-Workspace/Plan.md`) grows on top of this, ported from the
plan's Express/Prisma to the platform stack.

---

## 1. Goals

1. Store every prospect and customer that reaches Geek @ Your Spot — starting with the receptionist.
2. Be a **generic, sellable CRM core**: no insurance concepts, no industry lock-in.
3. Stay small: ~12 tables. Everything an individual business needs beyond the core is a
   **custom property**, not a schema change.

## 2. Design basis

Geek-CRM is Jeff's own sellable product, and this model is its own design. It rests on three things:

- **Geek @ Your Spot's sales process and the receptionist's requirements:** missed calls, text-backs,
  voicemails, qualification answers, booked meetings.
- **The party pattern:** one `participant` that is either a person or an organization, with the
  relationships between them held in links rather than baked into either side. A prospect is a
  lifecycle stage, not a type.
- **Concepts every CRM shares:** contacts, companies, deals, pipelines, lifecycle stages, activities
  and custom properties.

Every name and every vocabulary is Geek-CRM's own, enforced by CHECK constraints and by the
canonical schema below. Nothing here is specific to an industry: a business that needs a field the
core does not have adds a custom property, not a table.

What a customer buys or holds is `products` (a per-tenant catalog) and `customer_holdings` (what each
customer holds), with `deal_line_items` on a deal. They arrived in Phase 2 (script 0054, sections 4.12 to
4.14), and `tenant_subscriptions` (script 0055, section 4.15).

### Canonical schema

`GeekApplication/Schemas/GeekCrm.xsd` (namespace `https://geekatyourspot.com/schemas/geek-crm`,
embedded in GeekApplication) is the canonical contract for all of Geek-CRM: v0 and everything planned.
**Where this document and the XSD differ, the XSD wins.**

- Every type carries a phase marker and a `shipped` or `planned` status. Only shipped parts are
  authoritative: the database CHECK lists, the C# vocabularies in `GeekApplication/Models/Crm` and the
  table columns must equal them, and `GeekBackend.Tests/Crm/SchemaContractTests.cs` fails when they do
  not. A planned type becomes authoritative in the same push as its migration, when its marker flips.
- Every schema change starts in the XSD.
- Element names are PascalCase and map one to one to the snake_case columns. A row nested in its
  parent (a stage in its pipeline, a line item in its deal) carries its parent's id by nesting.
- The XSD validates XML documents as a contract. It does not replace database constraints or API
  validation, and it does not describe a tenant's custom properties: the service checks each tenant's
  own keys and values against that tenant's definition records.
- Versioning: the namespace carries no version; `version="1.0"` on the schema element does. Additions
  stay 1.x. A removal or a rename is 2.0 and gets a new namespace ending in `/v2`.

---

## 3. Entity overview

```
Participant ──1:1── Person
     │      ──1:1── Organization ──► NaicsCode (lookup)
     │
     ├──< ContactPoint          (phones, emails, addresses, websites)
     ├──< ParticipantLink >──   (Maria —Owner→ Boca Dental)
     ├──< PropertyValue         (custom properties)
     └──< Activity              (calls, texts, voicemails, meetings, notes)

Product ──► Participant (partner organization, optional)
Product ──< ProductPrice ; Tenant ──1:1── PricingRules ; Tenant ──< PricingUpliftTier
Deal ──< DealLineItem ──► Product ; Deal ──win──► CustomerHolding (one per line item) ──► Participant, Product

Pipeline ──< Stage
Deal ──► Pipeline/Stage ; Deal ──< DealParticipant >── Participant ; Deal ──< Activity
```

---

## 4. Tables

Common columns on every table: `id uuid`, `tenant_id uuid` (multi-tenant from day one; Geek @ Your
Spot is tenant #1), `created_at`, `updated_at`. Soft delete via `archived_at` where noted.

**The tenant boundary is enforced by the database, not only by the service** (`0053_geek_crm_tenant_keys.sql`).
Each tenant table has `UNIQUE (tenant_id, id)` and every foreign key between tenant tables is composite
on `(tenant_id, <column>)`, so a row cannot point at another tenant's row. Row-level security is on and
forced for those tables, with one policy: `tenant_id = current_setting('app.tenant_id')::uuid`. Every
store operation runs in one transaction that sets `app.tenant_id` first (`ICrmTenantScope`,
`Repositories/Crm/CrmTenantScope.cs`), so a query that forgets its tenant filter still sees one tenant.
A read with no tenant set returns no rows. Row-level security does not apply to a superuser or a role
with BYPASSRLS; GeekRepository's role must be neither, and the script needs Postgres 15 or later (it
uses `ON DELETE SET NULL (column)`). Numbering: 0051 is the additive columns, 0052 the NAICS load, 0053
the tenant keys and row-level security.

### 4.1 `participant` — anyone we deal with

| Column | Type | Notes |
|--------|------|-------|
| `kind` | enum `person` \| `organization` | Never changes |
| `display_name` | text | "Maria Lopez" / "Boca Dental" |
| `lifecycle_stage` | enum | `lead`, `prospect`, `customer`, `former_customer`, `other`. **Prospect is a stage, not a type** |
| `source` | enum | `phone_call`, `text`, `website`, `referral`, `import`, `event`, `outreach`, `other` |
| `source_detail` | text? | e.g. "Apollo Batch 1", "Missed call 2026-11-10" |
| `owner_user_id` | uuid? | GeekOAuth user who owns the relationship |
| `preferred_language` | text? | BCP-47, e.g. `en`, `es` |
| `notes` | text? | |
| `archived_at` | timestamptz? | Soft delete |
| `import_batch_id` | uuid? | The import that created the row, so a batch can be reviewed and archived as a unit |

### 4.2 `person` (1:1 with participant, `kind = person`)

| Column | Type | Notes |
|--------|------|-------|
| `participant_id` | uuid PK/FK | |
| `first_name`, `last_name` | text? | Callers often give only one |
| `preferred_name` | text? | "Call me Mike" |
| `job_title` | text? | |
| **Demographics** (all optional) | | Recorded only when the person provides them. The AI receptionist never asks; lead scoring never uses gender or marital status |
| `birth_date` | date? | |
| `gender` | text? | Free text or short list |
| `marital_status` | enum? | `single`, `married`, `domestic_partner`, `divorced`, `widowed` |
| `dependents_count` | int? | |
| `education_level` | enum? | `high_school`, `some_college`, `associate`, `bachelor`, `master`, `doctorate`, `other` |
| `occupation` | text? | |

### 4.3 `organization` (1:1 with participant, `kind = organization`)

| Column | Type | Notes |
|--------|------|-------|
| `participant_id` | uuid PK/FK | |
| `legal_name` | text | |
| `dba` | text? | Doing-business-as |
| `organization_type` | enum? | `sole_proprietor`, `llc`, `partnership`, `c_corporation`, `s_corporation`, `nonprofit`, `government`, `other` |
| `naics_code` | char(6)? FK → `naics_code` | Primary industry classification |
| `sic_code` | char(4)? | Kept when an import supplies it |
| `industry_label` | text? | Free-text fallback ("HVAC") when no code yet |
| `employee_count` | int? | |
| `year_started` | int? | |
| `annual_revenue_band` | enum? | `under_250k`, `250k_1m`, `1m_5m`, `5m_25m`, `over_25m` |
| `website` | text? | |
| `service_area` | text? | e.g. "Palm Beach & Broward" |
| `description` | text? | What they do |

An organization **may exist without a person** (imports, partial calls). The app flags
organizations with no primary contact; a Deal requires one (see 4.8).

### 4.4 `contact_point` — phones, emails, addresses, websites in one table

| Column | Type | Notes |
|--------|------|-------|
| `participant_id` | uuid FK | |
| `kind` | enum | `phone`, `email`, `address`, `website`, `social` |
| `label` | enum | `mobile`, `work`, `home`, `main`, `billing`, `other` |
| `value` | text | Phone in E.164; email lower-cased; URL |
| `address_line1`, `address_line2`, `city`, `region`, `postal_code`, `country` | text? | Only when `kind = address` |
| `is_primary` | bool | One primary per participant per kind |
| `do_not_contact` | bool | Never call/email/text |
| `sms_opt_out` | bool | Set when the person replies STOP (from Twilio) |
| `verified_at` | timestamptz? | e.g. they replied from this number |
| `import_batch_id` | uuid? | The import that created the row |

Unique: `(tenant_id, kind, value)` for phone and email, so a caller's number finds the existing record.

### 4.5 `participant_link` — relationships between participants

| Column | Type | Notes |
|--------|------|-------|
| `from_participant_id` | uuid FK | Usually the person |
| `to_participant_id` | uuid FK | Usually the organization |
| `role` | enum | `owner`, `employee`, `decision_maker`, `billing_contact`, `referred_by`, `partner`, `other` |
| `is_primary` | bool | Primary contact for the organization |
| `started_on`, `ended_on` | date? | People leave; history stays |

### 4.6 `pipeline` and `stage`

| `pipeline` | | `stage` | |
|---|---|---|---|
| `name` | "Sales" | `pipeline_id` | FK |
| `is_default` | bool | `name` | text |
| | | `position` | int (order) |
| | | `outcome` | enum `open` \| `won` \| `lost` |
| | | `probability_pct` | int? |

Default pipeline seeded per tenant: **New → Qualified → Meeting booked → Proposal sent → Won / Lost**.

### 4.7 `deal` — an opportunity

| Column | Type | Notes |
|--------|------|-------|
| `name` | text | "Boca Dental – AI Receptionist" |
| `pipeline_id`, `stage_id` | uuid FK | |
| `organization_participant_id` | uuid? FK | The business |
| `product_category` | text | Key into the qualification question bank (`missed_call_text_back`, `ai_receptionist`, `website`, ...) |
| `qualification` | jsonb | Answers captured by the AI or by hand, keyed by question id |
| `estimated_value` | numeric(12,2)? | |
| `recurring_value_monthly` | numeric(12,2)? | e.g. 49.00 |
| `expected_close_on` | date? | |
| `owner_user_id` | uuid? | |
| `closed_at` | timestamptz? | Set when the stage outcome is won/lost |
| `lost_reason` | text? | |

`deal_stage_history` (`deal_id`, `from_stage_id`, `to_stage_id`, `changed_at`, `changed_by`) keeps the
pipeline timeline.

### 4.8 `deal_participant`

| Column | Type | Notes |
|--------|------|-------|
| `deal_id`, `participant_id` | uuid FK | |
| `role` | enum | `primary_contact`, `decision_maker`, `influencer`, `billing`, `other` |

Rule (app level): a deal cannot move past **Qualified** without a `primary_contact` person.

### 4.9 `activity` — everything that happened

| Column | Type | Notes |
|--------|------|-------|
| `participant_id` | uuid? FK | Who it was with |
| `deal_id` | uuid? FK | |
| `channel` | enum | `call`, `sms`, `voicemail`, `email`, `meeting`, `note`, `task` |
| `direction` | enum? | `inbound`, `outbound` |
| `occurred_at` | timestamptz | |
| `subject` | text? | |
| `body` | text? | Text message body, note text |
| `summary` | text? | AI summary of a call |
| `transcript` | text? | AI call transcript (when recorded with disclosure) |
| `recording_url` | text? | Twilio recording |
| `duration_seconds` | int? | |
| `outcome` | enum? | `answered`, `missed`, `texted_back`, `voicemail_left`, `booked`, `no_show`, `completed` |
| `external_ref` | text? | Twilio CallSid / MessageSid (unique per tenant) for idempotency |
| `due_at`, `completed_at` | timestamptz? | For tasks |
| `created_by` | text | `receptionist`, `ai_receptionist`, `user:<id>`, `import` |

### 4.10 Custom properties

| `property_definition` | | `property_value` | |
|---|---|---|---|
| `object_type` | `person` \| `organization` \| `deal` | `definition_id` | FK |
| `key`, `label` | text; a key is lower case letters, digits and underscores | `record_id` | uuid |
| `data_type` | `text`, `number`, `date`, `bool`, `enum`, `multi_enum` | `value` | jsonb |
| `options` | jsonb list (required for `enum` and `multi_enum`) | | |
| `is_sensitive` | bool; hidden from reads unless the caller may see it, never logged, kept out of AI summaries and exports | | |
| `retired_at` | timestamptz?; a retired property is hidden and refuses new values, its stored values are kept for history | | |

The service checks every value against its definition's `data_type` and `options` before writing, and
writes all of a call's values or none. A date is `yyyy-MM-dd`; a `multi_enum` is a list of distinct
options; text is at most 4,000 characters. A property's object type, key and data type never change.
A key stays reserved after the property is retired. Removing an option that a stored value uses is
refused. A property that does not exist, is retired, or is sensitive for a caller who may not see it
is answered the same way (not found), so the answer never says which.

### 4.12 Catalog: `products`, `product_prices`, `pricing_rules`, `pricing_uplift_tiers`

`products` is the tenant's catalog: `name`, `code` (lower-case kebab, unique per tenant), `kind`
(`geek_saas`, `implementation`, `partner_subscription`, `support_plan`, `service`), `category`
(`accounting`, `business_operations`, `marketing`, `sales`, `bespoke`), `use_case_path` (the
`/use-cases/...` page), `partner_participant_id` (an organization of the same tenant; a related use case is
one with the same partner), `billing` (`one_time`, `monthly`, `annual`) and `is_active`. A product is
retired (`is_active = false`), never deleted, so a holding can point at a retired product; a retired product
cannot be added to a new deal or held by a new customer. Code and kind never change after create.

Prices are not on the product. `product_prices` carries `size` (small, medium, large, or empty for a flat
price), `hours`, `unit_price`, `recurring_price_monthly` (each may be blank until set), `currency` (USD)
and `effective_from` / `effective_to`, so a won deal keeps the price it was sold at. `pricing_rules` is one
row per tenant (`hourly_rate`, `related_use_case_pct`, default 60) and `pricing_uplift_tiers` holds the
`booked_pct_from` and `uplift_pct` pairs. Phase 3's estimates read these three tables, and the "Use-Case Pricing Draft" sheet is imported into
them once. How they behave:

- **History.** Setting a price (`POST /api/crm/products/{id}/prices`) starts it on `effectiveFrom` and ends the
  price in effect for the same size the day before. A date on or before the start of an existing price for that
  size is refused: a price is never edited in place, so a won deal's price is always findable. Each size has its
  own history; no size means a flat price. The product row is locked while a price is set, so two changes cannot overlap.
- **Lookup.** `GET /api/crm/products/{id}/price?size=&on=` answers the price in effect on a date (today when omitted),
  or 404 when none is set. A row with blank prices means "not priced yet".
- **Line items use it.** A new line item with no `unitPrice` takes the monthly price when it is recurring and the one-time
  price otherwise (for its `size`, when given). A missing or blank price refuses the line; nothing is guessed.
- **Rules and tiers.** `GET`/`PUT /api/crm/pricing-rules` (one row per tenant; 404 until set, never defaulted) and
  `GET`/`PUT /api/crm/pricing-uplift-tiers` (a PUT replaces the whole list in one transaction; a booked level appears
  once, enforced by a unique key).

### 4.13 `deal_line_items`

`deal_id`, `product_id`, `quantity`, `unit_price`, `is_recurring`, `discount_pct`, `currency`. A line total
is `quantity x unit_price x (1 - discount_pct / 100)`, rounded half away from zero to cents. When a deal
has line items its `estimated_value` is the sum of its one-time line totals and its
`recurring_value_monthly` the sum of its recurring ones (a line is recurring by default when its product
bills monthly). Once a deal is won its line items are frozen: add, change and delete are refused (conflict).
Later changes are made on the holdings, or on a new deal.

### 4.14 `customer_holdings`

What a customer holds and pays: `participant_id` (usually the organization), `product_id`, optional
`deal_id` and `deal_line_item_id`, `status` (`proposed`, `active`, `paused`, `cancelled`, `ended`),
`start_on` / `end_on`, `quantity`, `unit_price`, `discount_pct` (as sold), `recurring_amount_monthly`,
`one_time_amount`, `external_ref` and `tenant_subscription_id` (a plain column until the platform table
exists). A customer may hold the same product more than once (two locations), so there is no unique key on
participant plus product.

A `partner_subscription` holding records how the customer gets it in `arrangement`: `customer_direct` (the
customer pays the partner; only tracked), `referral` (`referral_fee`, `referral_fee_is_recurring`,
`referral_paid_on`) or `resold` (the business bills the customer; `partner_cost_monthly` against
`recurring_amount_monthly` gives the margin). Other kinds leave `arrangement` empty, and the referral and
cost fields are refused unless they match the arrangement.

**Win.** `POST /api/crm/deals/{id}/win` locks the deal row (`SELECT ... FOR UPDATE`), moves it to its Won
stage and creates one `active` holding per line item, never consolidated, each copying the product,
quantity, price and discount as sold and recording its `deal_line_item_id`. `UNIQUE (tenant_id,
deal_line_item_id)` and the lock mean a repeat or a concurrent call creates nothing extra. It is one
transaction: if anything fails, the stage change and every holding roll back. A deal with no line items, or
with neither an organization nor a primary contact to hold them, is refused.

### 4.15 `tenant_subscriptions` (platform-scoped)

Which tenant may use which multi-tenant Geek product: `subscriber_tenant_id`, `product_code`, `plan`, `status`
(the holding statuses), `start_on`, `end_on`. It belongs to the platform, so it has no `tenant_id` and its gate
is different: reachable only through `/api/platform/subscriptions` with the usual `X-API-Key` **and** an
`X-Platform-Admin-Key` equal to `GEEK_PLATFORM_ADMIN_KEY` (unset means the routes answer 503, never open), and
in the database by forced row-level security whose only policy asks for `app.platform = 'on'`, which only a
platform operation sets. A tenant-scoped transaction cannot read a subscription.

`product_code` must be an active `geek_saas` product of the selling tenant (Geek @ Your Spot, tenant #1); the
subscriber tenant must exist; a tenant has one live (proposed, active, paused) subscription per product.
Creating one may link the seller's own holding of that product (`holdingId`), which sets
`customer_holdings.tenant_subscription_id` in the same transaction; a holding links once.

### 4.16 Projects and capacity (Phase 3, script 0056)

`projects`: `participant_id`, optional `deal_id` and `customer_holding_id`, `status` (`scheduled`, `active`, `on_hold`,
`done`, `cancelled`), `start_on`, `target_end_on`, `estimated_hours`, `hours_per_week`. Projects stay in Geek-CRM: they
set delivery capacity and feed the demand uplift. A project holds hours only while it is `scheduled` or `active`, from
the Monday of its start to the Monday of its last day (its target end, or its start plus enough weeks for its hours at its
pace); it then needs a start date.

Capacity is **40 base + 20 surge hours a week** (`GET /api/crm/capacity?weeks=8`, up to 52). The read lists, per
Monday-start week, the booked hours, the booked percentage of the 40 base hours, the uplift that percentage carries
(the uplift tier with the highest `booked_pct_from` at or below it), and the first week with base capacity free. It takes no
lock, so its number is always an estimate, never a reservation.

Hours are claimed only by writing a project. Creating one, or changing any project, takes
`pg_advisory_xact_lock(hashtextextended('geek_crm.capacity:' || tenant_id, 0))` for the transaction, so two schedules
cannot both claim the last surge hours. A decrease (the same or a shorter span at the same or fewer hours a week) never
fails; anything else fails with a conflict naming the first week it would take past 60 hours.

### 4.17 `tenant_users` (Phase 4, script 0057)

Which tenant a signed-in GeekOAuth user works in, and as what: `tenant_id`, `user_id` (the `sub` of the user's GeekOAuth
token), `role` (`owner` or `member`). A user belongs to **one** tenant (unique on `user_id`), and a tenant keeps at least
one owner: the last owner cannot be demoted or removed. It has the usual `tenant_isolation` policy and, because the lookup
by user id spans tenants and the caller's tenant is not yet known, a `platform_access` policy like `tenant_subscriptions`:
the membership is read and changed only by a platform operation. It is managed through `/api/platform/tenant-users` (same
two keys as subscriptions). The first owner of Geek @ Your Spot is added there, by hand, with the owner's GeekOAuth `sub`.

### 4.11 Lookups

- `naics_code` (`code`, `title`, `level`) — the Census Bureau's 2022 NAICS list, 2,125 codes (20 sectors
  including the ranges 31-33, 44-45 and 48-49; 96 subsectors; 308 industry groups; 689 NAICS industries;
  1,012 national industries), loaded by `0052_geek_crm_naics_load.sql`. `level` is the number of digits,
  and 2 for a sector range. The script is idempotent.
- `sic_code` (`code`, `title`) — optional, loaded later from the federal list.

---

## 5. How the receptionist writes to it

| Event | Writes |
|-------|--------|
| Missed call | Find participant by `contact_point(phone)`; create a `lead` participant if new; `activity(call, inbound, outcome=missed)`; `activity(sms, outbound, outcome=texted_back)` if texted |
| Caller replies by text | `activity(sms, inbound, body)` |
| Caller replies STOP | `contact_point.sms_opt_out = true` |
| Voicemail | `activity(voicemail, inbound, recording_url, duration)` |
| AI receptionist call (later) | Person/organization fields it learned; `deal` in **New** or **Qualified** with `product_category` + `qualification`; `activity(call, summary, transcript)`; **Meeting booked** if they booked |

All writes are idempotent on `external_ref` (Twilio SID), so Twilio retries never duplicate.

**Intake (Phase 3).** `POST /api/crm/receptionist/intake` writes, in one transaction: the caller (found by phone, or created
as a lead person), their phone and optional email, a deal in the first stage of the default pipeline (`product_category`,
`qualification` answers as a JSON object keyed by question id), estimate line items, the call activity (`outcome = answered`,
`summary`, `transcript`, `priority`) and, when a meeting was booked, a `meeting` activity (`outcome = booked`, `starts_at`,
`ends_at`, `calendar_event_id`). A second call with the same CallSid writes nothing and answers the first result
(`created = false`).

**The estimate.** Each requested product (by catalog code and size) is priced at the catalog price in effect today: the monthly
price for a monthly product, the one-time price otherwise. One-time work priced by hours is raised by the uplift of the
earliest week with free base capacity. A second use case with the same partner is priced at `related_use_case_pct` of its
price (recorded as the line's discount). Each line stores `size`, `hours`, `hourly_rate`, `demand_uplift_pct`,
`is_estimate = true` and `estimate_inputs` (the price used, the pricing rules, the capacity week and a timestamp), and the deal
gets `estimate_given_at` and the sums. A product that is unknown or has no price is left out and named in `problems`; nothing
is guessed.

---

## 6. Implementation (v0)

| Piece | Where |
|-------|-------|
| Schema + seed (tenant #1, Sales pipeline) | `GeekRepository/Migrations/Sql/0050_geek_crm_schema.sql` (numbered 0050 to leave room for GeekSocial scripts) |
| EF mapping | `GeekRepository/Data/CrmDbContext.cs`, `Data/Entities/Crm/CrmRows.cs` |
| Store (tenant-scoped, Result-returning) | `GeekRepository/Repositories/Crm/CrmRepository.cs` behind `repo/crm/*` (`Controllers/Crm/CrmController.cs`) |
| Contract | `GeekApplication/Schemas/GeekCrm.xsd` (canonical), `GeekApplication/Interfaces/Crm/ICrmStore.cs`, `Models/Crm/*` (vocabularies equal the XSD enumerations and the SQL CHECKs) |
| GeekAPI client | `GeekAPI/HttpClients/HttpCrmRepository.cs` |
| Receptionist writes | `GeekAPI/Services/Receptionist/ReceptionistCrm.cs` — 3-second limit, never fails a call |
| Apollo import + reads | `GeekAPI/Controllers/Crm/CrmController.cs`, `Services/Crm/ApolloCsv.cs` |

### Who may call `/api/crm/*` (Phase 4)

Two kinds of caller, decided by `CrmAccess` in GeekAPI (`GeekAPI/Auth/CrmAccess.cs`):

- **A server** holding the `X-API-Key` (the receptionist, imports, scripts). `ApiKeyMiddleware` marks the request only after
  the key was checked. A server may name any tenant with `tenantId` (default Geek @ Your Spot) and counts as an owner.
- **A signed-in user**: a GeekOAuth token carrying the `geekcrm.manage` scope whose `sub` has a `tenant_users` row. A valid
  token without that scope is refused (403): GeekOAuth is shared across Geek apps and GeekAPI does not check audience, so
  the scope says who may call and `tenant_users` says where and as what. The tenant is **always** the membership's; a
  `tenantId` in the address is ignored, never trusted. No row is 403, a membership lookup that fails is 503. The
  membership is cached for at most 60 seconds, so a removed member loses access within a minute; only a found row is
  cached.

| | owner | member |
|---|---|---|
| Leads, participants, deals, line items, win, holdings, projects, capacity, activities, intake | yes | yes |
| Read the catalog, prices, pricing rules, uplift tiers | yes | yes |
| Create or edit products, set prices, pricing rules, uplift tiers | yes | 403 |
| Create, edit, retire property definitions; Apollo imports | yes | 403 |
| See sensitive properties (`includeSensitive=true`) | yes | never, the flag is ignored |

Which routes are owner-only is the `[CrmOwnerOnly]` attribute, pinned by a test that lists them. Another tenant's record
id answers 404 because every store call is tenant-scoped (composite keys and row-level security).

### GeekAPI routes (X-API-Key or a signed-in user, see above)

`GET /api/crm/lookups/naics?q=&level=&limit=` searches the NAICS list: digits search by code prefix,
words search titles, `level` is 2 to 6. Custom properties:

| Method | Path | Purpose |
|--------|------|---------|
| GET, POST | `/api/crm/properties/definitions?objectType=` | List, create a custom property. `includeRetired=true` and `includeSensitive=true` widen the list |
| PUT, DELETE | `/api/crm/properties/definitions/{id}` | Edit (label, options, sensitivity), retire |
| GET, PUT | `/api/crm/{objectType}/{recordId}/properties` | Read, set values on a `person`, `organization` or `deal`. PUT body: `{"values": {"key": value, "other": null}}`; a null clears |

A sensitive property is hidden unless an owner (or a server) passes `includeSensitive=true`; for a member the flag is ignored.

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/crm/imports/apollo?source=Apollo%20Batch%201&dryRun=false` | Body = the Apollo "Export contacts" CSV (`Content-Type: text/csv`). Re-importing matches instead of duplicating. `dryRun=true` returns the would-create and would-match counts and writes nothing |
| GET | `/api/crm/participants?stage=lead&kind=organization&limit=50` | List |
| GET | `/api/crm/participants/{id}` | One participant with contact points, links and the last 25 activities |
| GET | `/api/crm/deals?openOnly=true` | Deals with their stage |
| GET, POST, PUT | `/api/crm/products?kind=&includeRetired=` , `/api/crm/products/{id}` | The catalog (PUT retires with `isActive: false`) |
| GET, POST, PUT | `/api/crm/participants/{id}/holdings`, `.../holdings/{holdingId}` | A customer's holdings |
| GET, POST, PUT, DELETE | `/api/crm/deals/{id}/line-items`, `.../line-items/{lineItemId}` | A deal's line items; the answer carries the deal's new totals |
| GET, POST | `/api/crm/products/{id}/prices`, `/api/crm/products/{id}/price` | Price history, set a price, the price in effect |
| GET, PUT | `/api/crm/pricing-rules`, `/api/crm/pricing-uplift-tiers` | The tenant's pricing rules and uplift tiers |
| GET, POST, PUT | `/api/platform/subscriptions`, `.../{id}` | Tenant subscriptions; needs `X-Platform-Admin-Key` as well |
| GET, POST, PUT, DELETE | `/api/platform/tenant-users`, `.../{id}` | Who works in which tenant and as what; `X-API-Key` and `X-Platform-Admin-Key`. GET takes `tenantId` |
| POST | `/api/crm/receptionist/intake` | One call writes the caller, their contact points, a deal with estimate line items, the call and the meeting; idempotent on the Twilio CallSid |
| GET, POST, PUT | `/api/crm/projects`, `/api/crm/projects/{id}` | Projects; a change that would pass 60 hours in a week is a conflict |
| GET | `/api/crm/capacity?weeks=8` | Booked hours per week against 40 + 20 surge, the uplift, the earliest start |
| GET | `/api/crm/activities?priority=urgent&channel=&limit=` | Activities, newest first; urgent ones filterable |
| POST | `/api/crm/deals/{id}/win` | Moves the deal to Won and creates its holdings in one transaction; a repeat creates nothing |

All take an optional `tenantId` from a server (default Geek @ Your Spot); a signed-in user's tenant comes from `tenant_users`.

```bash
curl -X POST "https://api.geekatyourspot.com/api/crm/imports/apollo?source=Apollo%20Batch%201" \
  -H "X-API-Key: $GEEK_BACKEND_API_KEY" -H "Content-Type: text/csv" --data-binary @apollo-export.csv
```

An import is one transaction: a failure partway leaves nothing behind, and a dry run does exactly the work
of a real run and rolls it back. Every row a real run creates carries the `batchId` the answer returns
(`import_batch_id`), so the batch can be reviewed and archived as a unit. An organization matches an
existing one by case-insensitive name or by exact website domain (`acme.com` never matches `notacme.com`);
a person matches by email, then by Apollo's person id.

Apollo's revenue figures are its own estimates; the revenue band is only as good as they are.

### Tests

- `GeekBackend.Tests/Crm/SchemaContractTests.cs` — the XSD compiles clean; shipped enumerations, columns, types and nullability equal the C# vocabularies, the script and the EF mapping; a sample tenant validates and a dangling reference, a duplicate id, another tenant's row and a bad enum value are rejected.
- `GeekBackend.Tests/Crm` — SQL CHECK lists vs vocabularies, table columns vs EF mapping, seed, phone/URL/code normalizing, Apollo parsing, the primary-contact rule.
- `GeekBackend.Tests/Crm/CrmAccessTests.cs`, `CrmTenantUserTests.cs` — the access filter (key, no token, token without the scope, scope without a membership, tenant from the membership, owner-only routes, sensitive flag, 60-second cache with a fake clock), the owner-only route list, the key flag set only by a right key (Production, so the development bearer shortcut is off), and the membership store (one tenant per user, last owner kept).
- `GeekBackend.Tests/Receptionist/CrmReceptionistRecorderTests.cs` — what a call, text, STOP and voicemail write; a failing or slow store never reaches the call.
- `GeekBackend.IntegrationTests/CrmIntegrationTests.cs` (TEST_DATABASE_URL) — the script against Postgres: one person per caller, one activity per Twilio retry, STOP opt-out, import matching, the deal rule.

## 7. Out of scope for v0

Quotes, invoices, tickets, email sequences, forms, lists/segments UI, permissions beyond
tenant + owner, file attachments. All are in `Geek-CRM-Workspace/Plan.md` for later phases.

## 8. Decisions (Jeff, 2026-11-10)

1. **Proposal sent** stage: included (deals may skip it).
2. **`annual_revenue_band`**: kept, optional, filled from imports/enrichment; never asked by the AI.
3. **Multi-tenant from day one**: yes. Every table carries `tenant_id`; every query is scoped by it.
4. **Import Apollo Batch 1** as `lead` participants with `source = import`: yes (the reason for a CRM
   instead of a spreadsheet).
