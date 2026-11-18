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
what was said in a call is saved automatically, by us or by a vendor. Today no AI answers a tenant's call: every call hears one
closing message and is hung up, and nothing is recorded (section 3.1). A client that wants its call content turns Zero Data
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

### 3.1 The voice AI (Twilio ConversationRelay, not built)

- **Today:** no AI hears a tenant's call. Twilio posts each call to the tenant's number to
  `POST /api/receptionist/tenant/{tenantId}/voice`, which checks `X-Twilio-Signature` with the tenant's subaccount auth token and that
  `AccountSid` is that subaccount, and reads the tenant's billing standing on every call. Every call, whatever the standing, hears
  "Thank you for calling {business}. We can't take your call right now. Please call again later. Goodbye." and is hung up: no
  `<Record>`, no voicemail, no texts. Nothing of the call is recorded by us or by Twilio.
- **History:** Retell AI was the voice agent until Jeff dropped it on 2026-11-18 ("Retell is dropped"), because it held each call's
  recording, transcript and logs for about 10 minutes with no setting to close that window. Its code, variables and columns were
  removed (migration `0070`).
- **Planned:** Twilio ConversationRelay with our own model and voice. **Not built.** It does not by itself give Zero Data Retention:
  Twilio says transcripts are not stored unless its intelligence service is turned on; OpenAI keeps API data up to 30 days unless it
  has approved zero retention for the account, which is not in hand; the speech-to-text and voice vendors behind Twilio were not checked.
- **Attorney:** the voice vendors' data processing terms, and a business associate agreement if healthcare is ever served.

### 3.2 GeekAPI and GeekRepository (our own code and database)

- **Today (in the code):** `TwilioCallStatusRecorder` writes `geek_voice.call_logs` from Twilio's status callback
  (`POST /api/receptionist/tenant/{tenantId}/call-status`, same signature and account check), keyed by Twilio's `CallSid`: the call's
  status, its length from `CallDuration` in seconds, and billable minutes rounded up per started minute. The caller's number (`From`)
  is never read. No transcript, summary or recording column is written (since migration `0067`). What earlier calls stored in
  `caller_number`, `transcript_json`, `call_summary` and `recording_url` is still there. Nothing deletes a row. Overage billing reads
  the minutes from the same table. `geek_voice.appointments` has columns for the customer's name, phone, email, address and
  issue, and a store route that writes them. No route gives an owner call content.
- **Requirement:** the status callback records the call's metadata and nothing of what was said. The transcript, the summary, the
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

### 3.4 Twilio (the phone number)

- **Today:** the tenant's number is bought in its own Twilio subaccount, with no SIP trunk, and its voice webhook and status callback
  point at GeekAPI (section 3.1). `TwilioProvisioner` turns on no recording and no transcription, and the tenant route's TwiML has no
  `<Record>` (not checked on a real tenant number). Cancelling a tenant closes its subaccount, which releases the number. Twilio keeps its own call records
  (numbers, times, lengths). The older path on the company's own line, (561) 865-6990 (`GeekAPI/Services/Receptionist/`,
  `docs/Geek-Receptionist.md` section 4a), stopped taking voicemail and sending missed-call texts on 2026-11-18: a call nobody
  answers hears a closing message and ends. Voicemails it recorded before then are still held by Twilio.
- **Requirement:** no recording or transcription is ever enabled on a tenant's subaccount or number. Text messages sent for a
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
- **Requirement:** no log line carries a transcript, a summary, a recording link, a caller's number or a request body from
  Twilio.
- **Enforced by:** a review of every log call in `Services/Voice`, `Controllers/Voice` and `Services/Receptionist`, fixing the
  missed-call lines, and no request-body logging on the webhook routes.
- **Not verified:** how long Railway keeps logs on this plan.

### 3.6 Integrations: Google Calendar, Outlook, Cal.com, HubSpot (through GeekMcpGateway)

- **Today:** the gateway is stateless by rule and passes calendar calls to the business's own account. No voice AI is
  built, so no call books anything yet.
- **Requirement:** an event or record written to the business's own calendar is the delivery. The gateway and GeekAPI keep no copy
  of what was written. The gateway logs no request or response body.
- **Not verified:** what the gateway logs today.
- **Attorney:** the business, not us, is the keeper of what lands in its calendar. The customer agreement should say so.

### 3.7 CRM

**CRM is in development and is not part of Geek-Receptionist's plans (Jeff, 2026-11-18).** There is no CRM in production. No receptionist code writes to one: the company line's code stopped writing missed calls and texts to Geek-CRM on 2026-11-18.

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
  Jeff's instruction, before anything was deployed. Today no tenant call is recorded (section 3.1). Zero Data Retention decides where a recording is kept (never with us; in the client's own
  storage when it has connected one), not whether there is one.
- The signup site's compliance checkbox says calls are recorded. The professional services addendum was removed by Jeff on 2026-11-18.
- Geek @ Your Spot LLC's own callers are asked on the call whether they want a text confirming their appointment, and consent is
  their YES reply to one text, not a recording (Jeff, 2026-11-18; `docs/Geek-Receptionist.md` section 4a). Not built.

## 5. For the attorney

The questions are in `docs/Geek-Receptionist-attorney-questions.md`. The five that were here moved there on 2026-11-18 and were
merged with nine more, so there is one list.

## 6. Order of work

1. Build the voice AI on Twilio ConversationRelay (not built), and get each layer's retention in writing first (section 3.1).
2. Stop storing content in GeekAPI (changed in the code 2026-11-17); drop the columns; delete what is stored.
3. The greeting and the checkbox stay as they are (2026-11-18, section 4). The addendum was removed by Jeff the same day.
4. Clean the logs, then get the vendors' answers in section 3 in writing.
5. Publish the policy sentence only after 1 to 4 are done and checked. The public wording is the attorney's
   (`docs/Geek-Receptionist-attorney-questions.md`, section D).
6. Separately: the settings that let a client turn Zero Data Retention off and send call content to its own storage
   (`docs/Geek-Receptionist-zero-data-retention.md`).
