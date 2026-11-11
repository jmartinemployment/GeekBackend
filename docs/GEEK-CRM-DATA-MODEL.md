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

What a customer buys or holds is `Product` (a catalog) and `customer_holdings` (what each customer
holds). They are planned (see the XSD's phase markers) and are not in v0.

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

Pipeline ──< Stage
Deal ──► Pipeline/Stage ; Deal ──< DealParticipant >── Participant ; Deal ──< Activity
```

---

## 4. Tables

Common columns on every table: `id uuid`, `tenant_id uuid` (multi-tenant from day one; Geek @ Your
Spot is tenant #1), `created_at`, `updated_at`. Soft delete via `archived_at` where noted.

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

### GeekAPI routes (X-API-Key, like every /api route)

`GET /api/crm/lookups/naics?q=&level=&limit=` searches the NAICS list: digits search by code prefix,
words search titles, `level` is 2 to 6. Custom properties:

| Method | Path | Purpose |
|--------|------|---------|
| GET, POST | `/api/crm/properties/definitions?objectType=` | List, create a custom property. `includeRetired=true` and `includeSensitive=true` widen the list |
| PUT, DELETE | `/api/crm/properties/definitions/{id}` | Edit (label, options, sensitivity), retire |
| GET, PUT | `/api/crm/{objectType}/{recordId}/properties` | Read, set values on a `person`, `organization` or `deal`. PUT body: `{"values": {"key": value, "other": null}}`; a null clears |

A sensitive property is hidden unless the call passes `includeSensitive=true`. Every caller today holds
the X-API-Key (a trusted server); when signed-in users arrive their role decides it.

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/crm/imports/apollo?source=Apollo%20Batch%201&dryRun=false` | Body = the Apollo "Export contacts" CSV (`Content-Type: text/csv`). Re-importing matches instead of duplicating. `dryRun=true` returns the would-create and would-match counts and writes nothing |
| GET | `/api/crm/participants?stage=lead&kind=organization&limit=50` | List |
| GET | `/api/crm/participants/{id}` | One participant with contact points, links and the last 25 activities |
| GET | `/api/crm/deals?openOnly=true` | Deals with their stage |

All take an optional `tenantId`; the default is Geek @ Your Spot.

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
- `GeekBackend.Tests/Receptionist/CrmReceptionistRecorderTests.cs` — what a call, text, STOP and voicemail write; a failing or slow store never reaches the call.
- `GeekBackend.IntegrationTests/CrmIntegrationTests.cs` (TEST_DATABASE_URL) — the script against Postgres: one person per caller, one activity per Twilio retry, STOP opt-out, import matching, the deal rule.

## 7. Out of scope for v0

Quotes, invoices, products, tickets, email sequences, forms, lists/segments UI, permissions beyond
tenant + owner, file attachments. All are in `Geek-CRM-Workspace/Plan.md` for later phases.

## 8. Decisions (Jeff, 2026-11-10)

1. **Proposal sent** stage: included (deals may skip it).
2. **`annual_revenue_band`**: kept, optional, filled from imports/enrichment; never asked by the AI.
3. **Multi-tenant from day one**: yes. Every table carries `tenant_id`; every query is scoped by it.
4. **Import Apollo Batch 1** as `lead` participants with `source = import`: yes (the reason for a CRM
   instead of a spreadsheet).
