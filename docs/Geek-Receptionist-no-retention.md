# Geek-Receptionist: no-conversation-retention requirement

Written 2026-11-17 for the developer and the attorney. It is a requirement, not a description: most of it is **not yet true**, and each
section says what is true today. Companion to `docs/Geek-Receptionist.md` section 5, item 6.

## 1. The standard

The policy, as Jeff stated it on 2026-11-17:

> We do not record, archive, retain, or provide access to call histories. We process the call to perform the contracted service and
> discard its content after successful delivery.

Two questions sit under it, and they have different answers:

| Question | Answer | Source |
|---|---|---|
| Must we retain conversations to provide the service? | No. | HHS, Guidance on HIPAA and Cloud Computing: the HIPAA Rules generally do not require a business associate to keep the information past the time it provides the service (read on hhs.gov, 2026-11-17). |
| Does processing a conversation, even briefly, create obligations? | Possibly yes. | Same guidance: a provider that creates, receives or maintains the information is a business associate even if it cannot read it, and is generally not a "conduit". |

The direction, in Jeff's words later the same day: "The direction forward is Zero Data Retention no persistence of transcripts,
audio, etc. Only billing metadata is retained." The written policy is `docs/Geek-Receptionist-zero-data-retention.md`. Nothing of
what was said in a call is saved automatically, by us or by a vendor. Retell cannot meet the vendor half of that: it holds each
call's content about 10 minutes and has no setting to close that window (section 3.1). A client that wants its call content turns Zero Data
Retention off and connects its own Amazon S3 or Google Cloud storage, which needs settings of its own and is not built.

So this document removes retention. It does not remove the contracts that processing may require. Those are for the attorney (section 5).

## 2. Definitions (proposed; need Jeff's decision where marked)

- **Conversation content:** the call's audio, any recording, the transcript, any summary or analysis of what was said, and anything the
  caller said about themselves (name, call-back number, address, the reason for the call).
- **Call metadata:** the facts billing needs about a call: which tenant, when it started, how long it lasted, its billable minutes.
  The Zero Data Retention policy keeps it: "Call history, cost, latency, billing, and usage metadata: YES".
- **The caller's phone number** is content. It is not kept.

## 3. The requirement, vendor by vendor

Each row: what the vendor touches today, what the requirement is, how it is enforced, and whether anyone has checked.

### 3.1 Retell AI (the voice agent)

- **Today:** the deployed `RetellProvisioner` creates each agent with no `data_storage_setting`, so Retell's default `everything` applies:
  Retell keeps transcripts, recordings and logs, indefinitely unless `data_storage_retention_days` is set.
- **Changed 2026-11-17, not yet deployed:** new agents are created with `basic_attributes_only`, and a test holds the request to it.
  Agents that already exist are not changed.
- **Requirement:** every agent is created with `data_storage_setting: "basic_attributes_only"`, which Retell documents as storing "no
  transcripts/recordings/logs". Agents that already exist are updated to it.
- **Enforced by:** the provisioner sending the field, and a test that the create-agent request carries it. A provisioning call that does
  not carry it is a defect.
- **Read on Retell's Data Storage Settings page, 2026-11-18:** Retell still records the call under this setting. "Retell deletes
  the raw recording, transcript, and log files about 10 minutes after post-call processing ends, so download them from the webhook
  right away", and the webhook still carries them in that time. The page does not say the window meets any consent or
  record-keeping requirement. We download nothing.
- **Found 2026-11-18: the window cannot be closed by a setting.** Retell's page gives no option to shorten or remove it, and its
  create-agent API has no field that turns recording off. So the policy sentence in section 1 ("We do not record") is not true of a
  call answered through Retell, and "Zero Data Retention" cannot be claimed unqualified at launch. What can be said, and Jeff's
  decision to replace Retell, are in `docs/Geek-Receptionist.md` section 5, item 6. Whether Retell will close the window by
  contract was not found out.
- **Not verified:** what Retell's "basic attributes" are; a real delivery under this setting; whether call analysis (the
  summary) still runs; what Retell's own model provider keeps (the model is the one named in `RETELL_LLM_MODEL`); whether Retell's
  zero-retention terms need a contract or a plan. Ask Retell in writing.
- **Attorney:** Retell's data processing terms, and a business associate agreement if healthcare is ever served.

### 3.2 GeekAPI and GeekRepository (our own code and database)

- **Changed 2026-11-17, not yet deployed:** the call row no longer takes the caller's number, transcript, summary or recording
  link. The "Today" line below describes the deployed code.

- **Today:** Retell's webhook delivers the call to GeekAPI, and `RetellCallRecorder` stores it in `geek_voice.call_logs`: `caller_number`,
  `transcript_json`, `call_summary`, `recording_url`, with the start time, length and billable minutes. Nothing deletes a row. Overage
  billing reads the minutes from the same table. `geek_voice.appointments` has columns for the customer's name, phone, email, address and
  issue, and a store route that writes them. No route gives an owner call content.
- **Requirement:** the webhook handler records the call's metadata and nothing of what was said. The transcript, the summary, the
  recording link and the caller's number are never kept, and their columns are dropped, as are the appointment columns that copy
  what the caller said. No route, hub or dashboard screen returns conversation content.
- **Enforced by:** the columns not existing (a write of content then fails loudly), a test that a recorded call's command has no
  content fields, and no read route for content.
- **Existing rows:** every stored transcript, summary, recording link and caller number is deleted when the columns are dropped.

### 3.3 Supabase (PostgreSQL hosting and backups)

- **Today:** holds `geek_voice.call_logs` and `geek_voice.appointments` as above. Backups and point-in-time recovery hold copies of
  whatever the tables held.
- **Requirement:** once 3.2 is done the live tables hold no content. Backups taken before that still do, until they age out.
- **Not verified:** the project's backup schedule and how long backups are kept. Read it in the Supabase dashboard and record the date
  after which no backup contains conversation content.

### 3.4 Twilio (the phone number and the trunk)

- **Today:** the call goes from Twilio's trunk straight to Retell; GeekAPI never receives the voice call. `TwilioProvisioner` turns on no
  recording and no transcription: it sets no recording mode on the trunk, and Twilio's docs say a trunk records only in the modes
  `record-from-ringing` and `record-from-answer` (read 2026-11-18; not checked on a real trunk). Twilio keeps its own call records
  (numbers, times, lengths). The older path on the company's own line, (561) 865-6990 (`GeekAPI/Services/Receptionist/`,
  `docs/Geek-Receptionist.md` section 4a), stopped taking voicemail and sending missed-call texts on 2026-11-18: a call nobody
  answers hears a closing message and ends. Voicemails it recorded before then are still held by Twilio.
- **Requirement:** no recording or transcription is ever enabled on a tenant's subaccount, trunk or number. Text messages sent for a
  tenant carry no conversation content.
- **Enforced by:** a test that no provisioning request sets a recording or transcription option.
- **Not verified:** how long Twilio keeps call records and message bodies, and whether either can be shortened or redacted on the
  account. Ask Twilio.
- **Attorney:** Twilio's call records are a call history held by a vendor, outside our control. Whether the policy's wording has to
  say so.

### 3.5 Railway (hosting and application logs)

- **Today:** GeekAPI, GeekRepository and the gateway write logs that Railway keeps. The voice code logs tenant ids and failure codes, not
  conversation content. The older missed-call path logs callers' phone numbers (`MissedCallService`). A failed webhook logs its error
  text.
- **Requirement:** no log line carries a transcript, a summary, a recording link, a caller's number or a request body from Retell or
  Twilio.
- **Enforced by:** a review of every log call in `Services/Voice`, `Controllers/Voice` and `Services/Receptionist`, fixing the
  missed-call lines, and no request-body logging on the webhook routes.
- **Not verified:** how long Railway keeps logs on this plan.

### 3.6 Integrations: Google Calendar, Outlook, Cal.com, HubSpot (through GeekMcpGateway)

- **Today:** the gateway is stateless by rule and passes calendar calls to the business's own account. The Retell agent is created with
  one tool, `end_call`, so no call books anything yet.
- **Requirement:** an event or record written to the business's own calendar or CRM is the delivery. The gateway and GeekAPI keep no copy
  of what was written. The gateway logs no request or response body.
- **Not verified:** what the gateway logs today.
- **Attorney:** the business, not us, is the keeper of what lands in its calendar. The customer agreement should say so.

### 3.7 CRM (the older v0 path)

**There is no CRM in production (Jeff, 2026-11-18).** What follows is code that exists, not a CRM in use.

- **Today:** `CrmController` accepts a call's summary, transcript and recording link (`GeekAPI/Controllers/Crm/CrmController.cs`), used by
  the older single-tenant receptionist.
- **Requirement:** no receptionist call for a tenant writes conversation content to the CRM.
- **Not verified:** whether anything still calls that route for receptionist calls.

### 3.8 Email delivery

- **Today:** GeekAPI has no email sender. Nothing is emailed.
- **Requirement:** if messages are ever delivered by email, the provider is one that can be set not to keep message bodies, and the
  email is the delivery: we keep no copy.

### 3.9 Vercel (the signup and owner site) and PayPal

- **Today:** neither receives conversation content. The site handles signup, sign-in and billing; PayPal handles payments.
- **Requirement:** it stays that way. A dashboard that showed transcripts or recordings would break the policy; the plan in
  `docs/Geek-Receptionist.md` no longer lists one (changed 2026-11-17).

## 4. What customers and callers are told

- The default greeting says the receptionist is an AI and that the call may be recorded, and a new receptionist's
  `recording_disclosure_enabled` starts `TRUE`. Both were taken out of the working tree on 2026-11-17 and put back on 2026-11-18 at
  Jeff's instruction, before anything was deployed. Every call is recorded, by Retell (Jeff: "Retell records, it is only save for
  10 minutes, but it is recorded."). Zero Data Retention decides where a recording is kept (never with us; in the client's own
  storage when it has connected one), not whether there is one.
- The signup site's compliance checkbox and the first paragraph of its draft professional services addendum say calls are recorded.
- Geek @ Your Spot LLC's own callers are asked on the call whether they want a text confirming their appointment, and consent is
  their YES reply to one text, not a recording (Jeff, 2026-11-18; `docs/Geek-Receptionist.md` section 4a). Not built.

## 5. For the attorney

The questions are in `docs/Geek-Receptionist-attorney-questions.md`. The five that were here moved there on 2026-11-18 and were
merged with nine more, so there is one list.

## 6. Order of work

1. Set `basic_attributes_only` on new Retell agents (changed in the code 2026-11-17) and on the agents that already exist.
2. Stop storing content in GeekAPI (changed in the code 2026-11-17); drop the columns; delete what is stored.
3. The greeting, the checkbox and the addendum stay as they are (2026-11-18, section 4).
4. Clean the logs, then get the vendors' answers in section 3 in writing.
5. Publish the policy sentence only after 1 to 4 are done and checked, and not while Retell answers the calls: with Retell the
   sentence that can be published is the narrower one in `docs/Geek-Receptionist.md` section 5, item 6.
6. Separately: the settings that let a client turn Zero Data Retention off and send call content to its own storage
   (`docs/Geek-Receptionist-zero-data-retention.md`).
