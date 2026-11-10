# Geek-CRM — Data Model (v0, for review)

**Status:** draft for Jeff's review. No code yet.
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

## 2. Provenance (how this model was designed)

Geek-CRM is Jeff's own product. This model is written from:

- Geek @ Your Spot's sales process and the receptionist's requirements;
- publicly documented CRM concepts (contacts, companies, deals, pipelines, lifecycle stages,
  activities, custom properties) common to HubSpot, Salesforce, Pipedrive and others;
- the general **party pattern** (one record for "anyone we deal with", which is either a person or
  an organization) found in standard data-modeling literature;
- U.S. government classification standards: **NAICS** (Census Bureau) and **SIC**.

ACORD insurance schemas were reviewed only to confirm which generic concepts are worth having
(e.g. firmographic fields, a do-not-contact flag). **No ACORD element names, code lists, nesting
or documentation are used.** Insurance concepts (policies, coverages, claims, beneficiaries,
producers, tax identity) are out of scope.

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
| `key`, `label` | text | `record_id` | uuid |
| `data_type` | `text`, `number`, `date`, `bool`, `enum`, `multi_enum` | `value` | jsonb |
| `options` | jsonb (for enums) | | |
| `is_sensitive` | bool — excluded from AI summaries and exports unless allowed | | |

### 4.11 Lookups

- `naics_code` (`code`, `title`, `level`) — loaded from the Census Bureau list.
- `sic_code` (`code`, `title`) — optional, loaded from the federal list.

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

## 6. Out of scope for v0

Quotes, invoices, products, tickets, email sequences, forms, lists/segments UI, permissions beyond
tenant + owner, file attachments. All are in `Geek-CRM-Workspace/Plan.md` for later phases.

## 7. Decisions (Jeff, 2026-11-10)

1. **Proposal sent** stage: included (deals may skip it).
2. **`annual_revenue_band`**: kept, optional, filled from imports/enrichment; never asked by the AI.
3. **Multi-tenant from day one**: yes. Every table carries `tenant_id`; every query is scoped by it.
4. **Import Apollo Batch 1** as `lead` participants with `source = import`: yes (the reason for a CRM
   instead of a spreadsheet).
