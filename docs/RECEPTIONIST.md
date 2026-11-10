# Geek-Receptionist (in GeekAPI)

Missed-call text-back for small businesses, built on Twilio. A caller whose call goes unanswered
gets one text in the business's name, the owner is notified, the caller can leave a voicemail, and
the caller's text replies are forwarded to the owner's cell.

**Where it lives:** inside **GeekAPI** (`GeekAPI/Services/Receptionist/`), served from
`api.geekatyourspot.com`. It started as the standalone repo
[`Geek-Receptionist`](https://github.com/jmartinemployment/Geek-Receptionist) and was merged into
GeekAPI on 2026-11-10 (Jeff's decision): one Railway service and one domain instead of a separate
deployment. That repo is kept only as history; change the code here.

> **Status:** v0, missed-call text-back. AI voice answering is next (see [Roadmap](#roadmap)).

---

## What happens on a call

```
Caller ──► Twilio number ──► POST /api/receptionist/twilio/voice/incoming
                                │
           CALL_MODE=Dial       │        CALL_MODE=Forwarded
   ring OWNER_CELL for          │        (the business's own line rang first and
   RING_SECONDS                 │         forwarded the unanswered call here)
   owner presses a key? ─ yes ─► connected, nothing sent
           │ no / voicemail / busy                 │
           ▼                                       ▼
   text the caller (once per REPEAT_TEXT_MINUTES) + notify owner + greeting + voicemail

Caller replies by text ──► POST /api/receptionist/twilio/sms/incoming ──► forwarded to owner
                                                    (attachments deleted, never forwarded)
```

| Mode | Use it when | What happens |
|------|-------------|--------------|
| `Dial` (default) | Demos, or the Twilio number is the business's main number | Rings the owner's cell first; only unanswered calls trigger the text |
| `Forwarded` | The business keeps its existing number | Its carrier forwards unanswered calls here; every call that arrives is a missed call |

**Call screening** (`SCREEN_CALLS=true`, Dial mode): the owner hears *"Call for {Company}. Press any
key to accept."* Without it, the owner's own cell voicemail "answers", Twilio reports the call as
completed, and the missed call never gets a text.

**Greeting = registered verbal consent.** When the caller was texted, the greeting is:

> Sorry we missed you. {Company} is texting you at this number about your call. Message and data
> rates may apply. Reply STOP to opt out. You can also leave a message after the tone.

This wording is the verbal opt-in registered with the A2P 10DLC campaign (Message flow field).
**Change the code (`ReceptionistEndpoints.GreetingText`) and the campaign together.** Callers who
were not texted (repeat window, failed send, blocked or non-US number) hear only *"Sorry we missed
you. Please leave a message after the tone."*, so the greeting never claims a text that wasn't sent.

---

## Endpoints

All are under `ReceptionistEndpoints.RoutePrefix` = `/api/receptionist/twilio`, called only by Twilio.

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/voice/incoming` | Call arrives: ring owner (Dial) or treat as missed (Forwarded) |
| POST | `/voice/screen` | Whisper to owner: press a key to accept |
| POST | `/voice/screen-accepted` | Owner accepted; call connects |
| POST | `/voice/dial-complete` | Owner leg ended: missed or answered |
| POST | `/voice/voicemail-complete` | Text the owner the recording link |
| POST | `/sms/incoming` | Forward a reply text to the owner; delete attachments |

### Authentication

Twilio cannot send `X-API-Key`, so **`ApiKeyMiddleware` lets the `/api/receptionist/twilio/`
prefix through** (and only that prefix; see `ApiKeyMiddlewareAuthBoundaryTests`). Every route under
it is authenticated by **`TwilioSignatureFilter`**, which validates `X-Twilio-Signature` with
`TWILIO_AUTH_TOKEN` against `PUBLIC_BASE_URL + path + query`. Unsigned or forged requests get `403`.

### When it is off

The receptionist is optional inside GeekAPI and never stops GeekAPI from starting:

| Configuration | Result |
|---------------|--------|
| `TWILIO_ACCOUNT_SID` not set | Off. Info log `Geek-Receptionist is off: TWILIO_ACCOUNT_SID is not set` |
| Set but incomplete or invalid | Off. **Error** log listing every problem (`Geek-Receptionist is off because its configuration is invalid ...`) |
| Valid | On. Info log `Geek-Receptionist on for {Company}: mode ..., ring ...s, ...` |

While off, every path under the prefix answers **503** `receptionist_not_configured`.

---

## Configuration (GeekAPI Railway variables)

| Variable | Required | Default | Description |
|----------|:--------:|---------|-------------|
| `TWILIO_ACCOUNT_SID` | ✓ | | Account SID (`AC…`). Its presence is what turns the receptionist on |
| `TWILIO_AUTH_TOKEN` | ✓ | | Used **only** to verify webhook signatures; never used to call Twilio |
| `TWILIO_API_KEY_SID` | ✓ | | Restricted/Standard API key SID (`SK…`) used for every outbound Twilio call |
| `TWILIO_API_KEY_SECRET` | ✓ | | Secret for that key (shown once at creation) |
| `TWILIO_NUMBER` | ✓ | | The Twilio number (E.164), e.g. `+15618656990` |
| `OWNER_CELL` | ✓ | | Owner's cell (E.164): rung in Dial mode, receives all notifications |
| `COMPANY_NAME` | ✓ | | Used in the caller text, greeting, screening prompt and notifications |
| `PUBLIC_BASE_URL` | ✓ | | GeekAPI's public URL as Twilio calls it, no trailing slash: `https://api.geekatyourspot.com`. Must match the webhook URLs exactly or every signature fails (403) |
| `CALL_MODE` | | `Dial` | `Dial` or `Forwarded` |
| `RING_SECONDS` | | `18` | How long to ring the owner (5–60); keep it under the owner's carrier voicemail pickup |
| `SCREEN_CALLS` | | `true` | Owner must press a key to accept |
| `CALLER_TEXT` | | built-in | Text sent to missed callers; `{company}` is replaced. Must stay consistent with the A2P campaign samples |
| `REPEAT_TEXT_MINUTES` | | `10` | Don't text the same caller again within this window (0–1440) |
| `VALIDATE_TWILIO_SIGNATURE` | | `true` | Only `false` for local testing without Twilio |
| `RECEPTIONIST_CRM_TENANT_ID` | | Geek @ Your Spot | Geek-CRM tenant the receptionist writes calls and texts to |

Never commit these values; they live only in Railway variables.

---

## Twilio setup

1. **Number:** `+1 561-865-6990` (Geek @ Your Spot). Voice and SMS capable.
2. **A2P 10DLC** (required to text US phones from a 10-digit number; unregistered texts fail with `30034`):
   - Trust Hub: ISV primary customer profile → **Low Volume Standard Brand** (Geek @ Your Spot LLC, EIN) → approved 2026-11-10.
   - **Campaign:** Low Volume Mixed, opt-in method **Verbal consent** (the greeting above), disclosures at
     `https://geekatyourspot.com/terms-and-conditions#text-messages` and
     `https://geekatyourspot.com/privacy-policy#text-messages`. Submitted 2026-11-10.
   - Calls and voicemail work before approval; texts fail and the owner is told the Twilio error.
3. **Messaging Service** (the one holding the number):
   - **Integration → Incoming Messages: "Defer to sender's webhook".** Otherwise the Service decides
     where texts go and caller replies are silently dropped.
   - **Smart Encoding on** (curly quotes and long dashes otherwise double the cost of a text).
   - ⚠️ **Never delete the Messaging Service.** That deletes the A2P campaign and stops all texting.
4. **Geo permissions:** Messaging → Settings → Geo permissions: US/Canada only (the code also refuses
   non-`+1` numbers, which blocks SMS-pumping fraud).
5. **API key:** Console → Account → API keys & tokens → Restricted (or Standard) key. The service calls
   Twilio with the key; the auth token is only for signatures, so a leaked key is revoked without
   touching the auth token.
6. **Number webhooks** (Phone Numbers → the number):

   | Section | Setting | Value |
   |---------|---------|-------|
   | Voice | A call comes in → Webhook, HTTP POST | `https://api.geekatyourspot.com/api/receptionist/twilio/voice/incoming` |
   | Messaging | A message comes in → Webhook, HTTP POST | `https://api.geekatyourspot.com/api/receptionist/twilio/sms/incoming` |

## Phone forwarding (Forwarded mode)

The business's published line, **(561) 526-3512**, forwards unanswered calls to the Twilio number:

| Carrier | Turn on (no answer) | Check | Turn off |
|---------|--------------------|-------|----------|
| AT&T / T-Mobile | `**004*15618656990#` | `*#004#` | `##004#` |
| Verizon | `*71` + `5618656990` | | `*73` |

## Test it

1. From a second phone, call (561) 526-3512 and let it go unanswered.
2. The calling phone hears the greeting and gets the text; the owner gets "missed call from … Auto-text sent."
3. Reply to the text; the reply arrives on the owner's cell.
4. Call again and leave a voicemail; the owner gets the recording link.

---

## Safety by design

| Rule | Why |
|------|-----|
| One fixed message, only to someone who just called | Informational reply to an inbound call, not marketing; no free-form sending, no lists |
| Only US/Canada (`+1`) numbers are texted | Blocks SMS-pumping fraud |
| Same caller texted at most once per window | No spamming repeat callers |
| Inbound attachments deleted, never forwarded | The service only moves words, never images or files |
| Every webhook needs a valid Twilio signature | Forged requests get 403 |
| Failures reported to the owner | e.g. A2P not yet approved: owner gets the exact Twilio error |

Twilio handles `STOP` / `HELP` / `START` keywords. Florida requires all-party consent to record
calls; the voicemail prompt makes recording obvious, and there is no silent call recording.

## Code and tests

```
GeekAPI/Services/Receptionist/
  ReceptionistEndpoints.cs   AddReceptionist / MapReceptionist, webhooks, TwiML, greeting
  ReceptionistOptions.cs     Environment configuration and validation
  MissedCallService.cs       Missed-call handling, repeat window, screening state
  InboundSms.cs              Reply forwarding, attachment deletion
  Sms.cs                     Twilio SMS sender
  TwilioSignatureFilter.cs   X-Twilio-Signature validation
GeekBackend.Tests/Receptionist/   Endpoints on a TestServer with fake SMS and signed requests
```

```bash
dotnet test GeekBackend.Tests --filter "FullyQualifiedName~GeekBackend.Tests.Receptionist"
```

State (repeat-caller window, screening) is **in memory** and resets when GeekAPI restarts.

## Geek-CRM

Every missed call, text-back, inbound text, STOP/START and voicemail is also written to Geek-CRM
(`geek_crm`, see [`GEEK-CRM-DATA-MODEL.md`](GEEK-CRM-DATA-MODEL.md)): the caller's number finds or
creates a **lead**, and each event is an **activity** keyed by its Twilio SID, so a webhook retry
records nothing twice. STOP sets `sms_opt_out` on the number. Writes go to tenant
`RECEPTIONIST_CRM_TENANT_ID` (optional; Geek @ Your Spot when unset), give up after 3 seconds, and
never change what the caller hears.

## Roadmap

- ~~Call and text history~~: done in Geek-CRM (`geek_crm`); an owner dashboard is next
- Multi-tenant: one deployment, client identified by the number called (today: one business per configuration)
- Business-hours rules (different text after hours)
- AI voice receptionist: answer, capture the job details, book or escalate; text-back stays the fallback
