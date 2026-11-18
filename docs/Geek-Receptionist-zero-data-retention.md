# Geek-Receptionist: Zero Data Retention (ZDR)

Written 2026-11-17. Section 1 is the policy as Jeff wrote it that day. Sections 2 to 6 are the documentation built around it, using
Vapi's documentation as the reference, as he asked. Section 7 says what exists in the code today, which is almost none of it, and
section 8 lists what the policy text settles and what it leaves open.

Companions: `docs/Geek-Receptionist.md` section 5, item 6, and `docs/Geek-Receptionist-no-retention.md`.

**Read this first: this is a requirement, not a description.** No ZDR switch, no storage connection and no upload to a client's
bucket exists. Every section below that describes behavior has a "Built today" line.

## 1. The policy (Jeff, 2026-11-17; spelling corrected, nothing else changed)

> **Zero Data Retention Policy (ZDR).** Zero Data Retention stops Geek-Receptionist from retaining call content after a call ends.
> Disable it in the dashboard. ZDR applies to every call in a given organization.
>
> **What ZDR retains.** Geek-Receptionist processes calls in real time. After a call ends, it retains operation metadata so you can
> still see call history and usage.
>
> | Data | Retained in Geek-Repository with ZDR |
> |---|---|
> | Recordings, transcripts, messages, summaries, and detailed call logs | NO |
> | Structured Outputs | NO by default |
> | Call history, cost, latency, billing, and usage metadata | YES |
>
> **Find recordings, transcripts, logs, and metadata.** With ZDR, retrieve call content from your own storage. AWS S3 or Google
> Cloud Storage are the current supported storage types.
>
> Disable ZDR to store recordings in cloud storage with encrypted access via API credentials or your own custom bucket routing.

Clarified by Jeff later the same day, verbatim:

> this feature is disabled by default. Client must configure Google Cloud or AWS S3 storage.

And, later again, verbatim:

> The direction forward is Zero Data Retention no persistence of transcripts, audio, etc. Only billing metadata is retained.

> I envision disabling ZDR & enable persistence requires additional settings.

Nothing on the site, in the greeting or in these documents is to say that call data is saved automatically (Jeff, same day).

**What recording means (2026-11-18).** Every call is recorded, by Retell. Jeff: "Retell records, it is only save for 10 minutes,
but it is recorded." Zero Data Retention decides where a recording is kept, not whether there is one: never with us, and in the
client's own storage when the client has connected one. Jeff confirmed this reading on 2026-11-18 and said to add "this call may
be recorded" back in to the greeting. It had been taken out the day before on the wrong reading that ZDR means no call is ever
recorded. His preference, stated the same day, is not to save any client call data.

**What the 10 minutes is (Retell's Data Storage Settings page, read 2026-11-18).** With `basic_attributes_only`, "Retell deletes
the raw recording, transcript, and log files about 10 minutes after post-call processing ends, so download them from the webhook
right away". The window is the time to collect the files. Retell does not say it meets a consent or record-keeping requirement,
so a call recording is never our proof of anything. Twilio records nothing on a tenant's trunk: the provisioner sets no recording
mode (not checked on a real trunk).

**What this means for the claim (found 2026-11-18).** Retell has no setting that shortens or removes the 10 minutes, and no
setting that turns recording off. So while Retell answers the calls this policy is met on our side only: we retain nothing, and the
vendor holds the content for about 10 minutes. "Zero Data Retention", unqualified, cannot be claimed at launch (Jeff: "at launch
I can't claim ZDR"). Retell is to be replaced for this reason. The detail, and what can be said meanwhile, is in
`docs/Geek-Receptionist.md` section 5, item 6.

Read together: by default a call's recording, transcript and logs are gone from Retell about 10 minutes after the call is
processed, are kept by no one after that, and are sent nowhere. A client that wants them
turns the feature on by connecting its own Google Cloud Storage or Amazon S3 bucket, and from then on they are written there.
We run no storage for call content in either case. "Organization" is this product's tenant, so the setting covers every call a
tenant receives.

## 2. What ZDR retains

| Data | Kept by us with ZDR on | Built today |
|---|---|---|
| Recording | No | No recording link is written to the call row, and new Retell agents are set to keep none past about 10 minutes, which Retell cannot shorten (both changed 2026-11-17, not deployed). Agents that already exist still keep theirs (section 7). |
| Transcript and messages | No | Same two changes, same gap for agents that already exist. |
| Summary | No | Not written to the call row (same change). |
| Detailed call logs | No | We write none. Retell: same change and same gap. |
| Structured outputs | No by default | The agent has one tool, `end_call`, so a call produces none. `geek_voice.appointments` has columns for a caller's name, phone, email, address and issue and would be one. |
| Call history | Yes | One row per call in `geek_voice.call_logs`: the call's ids, status, start, end, length, disposition. |
| Billing and usage | Yes | Billable minutes per call in the same row; overage billing reads it. |
| Cost | Yes | Not recorded. |
| Latency | Yes | Not recorded. |

"No by default" for structured outputs follows Vapi, where one flag on a structured output keeps it in spite of ZDR. This product
has no such flag, and whether it ever gets one is not decided.

## 3. Where call content goes: the client's own storage

This is off by default. A client turns it on by connecting storage it owns. From then on a call's content is written there and is
never kept by us, and the client reads it there. Two kinds of storage are supported: Amazon S3 and Google Cloud Storage. A client
that connects neither gets no recording, transcript or log of any call, from anyone. Retell still holds them for about 10 minutes
(section 1).

**Built today:** none of this section. No connection, no upload, no screen.

### 3.1 What the client connects (from Vapi's storage pages, read 2026-11-17; not yet decided for this product)

| Storage | What Vapi asks for |
|---|---|
| Amazon S3, by access key | Access key id, secret access key, bucket name, bucket region, optional path prefix |
| Amazon S3, by role | The ARN of a role in the client's account that the vendor assumes, bucket name, bucket region, optional path prefix, optional external id |
| Google Cloud Storage | Service account key (JSON), bucket name, bucket region, optional path prefix, HMAC access key and secret |

For the S3 role, Vapi's page lists the permissions on the bucket's objects: put, read back, list multipart parts, and delete (left
out if the vendor should never delete). Its Google page does not list the role the service account needs.

These are a client's cloud credentials. Wherever they are entered, they are stored encrypted, never shown again after saving, and
never logged.

### 3.2 What is written, and where (Vapi's layout, as the reference)

| Content | Where Vapi puts it in the bucket |
|---|---|
| Audio | The recording path, as WAV or MP3 |
| Transcript, messages and analysis | One JSON file per call, named by the call's id, under the connection's path prefix |
| Detailed logs | The logging path, as compressed JSON lines |

Files are found by call id. The call id is the one in the call history we keep, so the history is the index and the bucket holds
the content.

### 3.3 Before the first call

1. Connect the bucket (3.1).
2. Choose what is written: recording, transcript, logs. In Vapi each is its own switch (section 6).
3. Place a test call. Its files appear in the bucket, and the call appears in the call history.

## 4. Turning call content on

Off is the default. The client turns it on in the dashboard by connecting its bucket, and it applies to the whole tenant. The
policy text's "cloud storage with encrypted access via API credentials or your own custom bucket routing" is, with Jeff's
clarification, the client's own Google Cloud or Amazon S3 storage, reached with the credentials the client gives. There is no
storage of ours to choose instead.

**Built today:** nothing. No route returns call content, which is right for the default and is all there is.

A change applies to calls after it. Turning it on does not bring back content from earlier calls, because none was kept.

## 5. If the client cannot find its data

Adapted from Vapi's page. Each line assumes sections 3 and 4 exist.

- The storage connection and the content switches must have been on before the call. A transcript file does not prove audio was
  recorded.
- Check the bucket, the region and the path prefix. If the prefix uses the date, it is the upload date.
- Wait for processing after the call to finish, then check that the bucket allows the write.
- Content that never reached the client's storage cannot be recovered, because we did not keep it.
- The call still appears in the call history either way.

## 6. Vapi's configuration, for reference (pasted by Jeff, 2026-11-17)

This is Vapi's request body, not ours. Geek-Receptionist has no `artifactPlan` and no `credentials` object. It is here because it
shows the switches the policy implies: recording, logging and transcript are separate, and each can be sent to custom storage.

```json
{
  "artifactPlan": {
    "recordingEnabled": true,
    "recordingFormat": "wav;l16",
    "loggingEnabled": true,
    "pcapEnabled": true,
    "recordingUseCustomStorageEnabled": true,
    "loggingUseCustomStorageEnabled": true,
    "pcapUseCustomStorageEnabled": true,
    "recordingPath": "/recordings",
    "loggingPath": "/logs",
    "transcriptPlan": {
      "enabled": true,
      "assistantName": "Assistant",
      "userName": "Customer"
    }
  },
  "credentials": [
    {
      "provider": "gcp",
      "serviceAccountKey": "your-service-account-json"
    }
  ]
}
```

Other points on Vapi's pages that bear on ours:

- A transcript there is a list of turns, each with who spoke, what was said and when, and the two speakers' names are settings.
- Vapi's own call-recording page tells its customers to inform callers about recording and to follow consent laws. That duty
  does not go away when the recording lands in the client's bucket instead of ours.
- On Vapi, ZDR and its HIPAA mode cannot both be on. This product refuses healthcare and veterinary businesses, and ZDR does not
  change that.
- Vapi says its ZDR depends on the customer's plan. Whether ours does is not stated.

## 6a. HIPAA as a paid add-on (Jeff, 2026-11-17: "I like how Vapi handles HIPPA $2000/mo")

A stated liking, not yet a decision. Healthcare and veterinary businesses are still refused at signup, and nothing here changes
that.

How Vapi does it (its HIPAA page, read 2026-11-17):

- A paid add-on bought in the dashboard's billing page. Vapi's pricing page (vapi.ai/pricing, read 2026-11-17) lists it under
  Add-ons at $2,000 a month: "HIPAA eligible data handling. Standard Data Processing Agreement and Business Associate Agreement."
- The same page does not price ZDR on its own. It is included with a paid plan: "ZDR included" on Core ($29 a month), and listed
  on Pro and Premier.
- The customer names who signs, Vapi emails the business associate agreement, and HIPAA mode is activated after it is signed.
- It covers the whole organization, cannot be left off for one assistant, and cannot be turned on through the API.
- With it on, only providers Vapi lists as HIPAA-compliant can be chosen; a change that picks another is rejected.
- Recordings, transcripts and logs are kept in private storage, read only with the customer's private key, unless the customer
  connects its own storage.
- **HIPAA mode and ZDR cannot both be on.** In Vapi, HIPAA mode keeps call content under protection; ZDR keeps none.
- The customer carries duties of its own: no patient information in prompts or labels, and none in an organization without the
  add-on, even for a test.

What it would take here, none of it built or checked: a business associate agreement from us to the client, and from Retell,
Twilio and every other vendor in `docs/Geek-Receptionist-no-retention.md` section 3 to us; a billing add-on; the healthcare
question at signup leading to the add-on instead of a refusal; and the attorney's answer on whether keeping nothing by default
changes any of it.

## 7. What this means in our code (facts, not decisions)

- **The voice vendor is Retell, not Vapi.** One search of Retell's documentation (2026-11-17) found no setting that sends
  recordings or transcripts to a customer's bucket. If that holds, the write to the client's bucket is ours to do: GeekAPI takes
  the content from Retell's call-ended webhook, writes it to the client's bucket, and keeps nothing.
- **Retell keeps everything today.** The deployed `RetellProvisioner` sets no `data_storage_setting`, so Retell's default
  `everything` applies. **Changed in the code 2026-11-17, not yet deployed:** new agents are created with
  `basic_attributes_only`, which Retell documents as storing no transcripts, recordings or logs, and a test holds the request to
  it. Agents that already exist are not changed by this; each needs one update call to Retell, which has not been made.
- **The webhook carries the content under every storage setting** (Retell's webhook page, read 2026-11-17): "Webhooks always carry
  the full, unscrubbed call data, whatever the agent's data storage setting is." Under `basic_attributes_only` Retell keeps only
  ids, timestamps, status and cost, and deletes the raw recording, transcript and log files about 10 minutes after its processing
  of the call ends. Its advice: download the recording and the other file links as soon as the webhook arrives. So "recordings in
  the client's storage" and "nothing kept at Retell" hold together, and the copy to the client's bucket has about 10 minutes.
- **What each event carries** (same page, and Retell's Get Call reference for the call object, read 2026-11-17):

  | Event | Carries |
  |---|---|
  | `call_ended` | "All fields from the call object except call_analysis." |
  | `call_analyzed` | "Full call data including call_analysis object" |

  The call object's content fields: `transcript`, `transcript_object` (turns with timestamps), `transcript_with_tool_calls`,
  `recording_url`, `recording_multi_channel_url` (each party on its own channel), `public_log_url`,
  `knowledge_base_retrieved_contents_url`, and `call_analysis` (sentiment, summary, extracted data). It also has `latency` and
  `call_cost`, which is where the ZDR table's cost and latency come from.
- **So `call_ended` alone has everything ZDR hands to the client**, and no summary or analysis. `call_analyzed` adds only the
  analysis, which "Transcribe forward delete" does not want.
- **The recording and the log arrive as links, not as files.** GeekAPI has to download each one and write it to the client's
  bucket. Retell deletes the files "about 10 minutes after post-call processing ends, shortly after call_analyzed is sent".
- **The links are public unless the agent opts in to signed URLs** (`opt_in_signed_url`), which then "automatically expire after
  24 hours" by default. Under `basic_attributes_only` the files are gone in about 10 minutes either way.
- **Delivery rules that shape the copy:** Retell waits 10 seconds for a `2xx`, then retries, up to 3 times, so the same event can
  arrive more than once. A recording download and a bucket upload may not fit in 10 seconds. Retell's advice is to answer as soon
  as the payload is safely accepted and do the work after. Under ZDR "safely accepted" cannot mean written to our database, so
  the copy either finishes inside the request or runs after the answer with the content held in memory only. Not decided.
- **The spoken notice:** Retell's page says the recording notice is the agent's first sentence, and that under
  `basic_attributes_only` "no recording is retained, but the agent still speaks the announcement live."
- **Not verified:** all of the above is Retell's documentation. No real delivery under `basic_attributes_only` has been looked at.
- **The call row already matches the "yes" line of the table**, apart from cost and latency (section 2). That change is in the
  working tree, not deployed.
- **Not built:** the ZDR setting on a tenant, the storage connection and its encrypted credentials, the S3 and Google Cloud Storage
  writers, the dashboard switch, and the onboarding steps that ask for all of it.

## 8. What the policy text settles, and what it leaves open

**Settled by the text:**

- Where a call's content goes: the client's own storage, Amazon S3 or Google Cloud Storage.
- A per-call history row with billing and usage is kept. This closes the open billing question in `docs/Geek-Receptionist.md`
  section 5, item 6.
- Keeping nothing is the default, set in the dashboard, and covers the whole tenant.
- Call content is off by default, so we retain nothing for a client that has not connected storage. A client may finish
  onboarding without storage, and then a call's content is written nowhere. Retell still records each call and holds the files
  about 10 minutes (section 1), so whether the words "We do not record" can be published is the attorney's question
  (`docs/Geek-Receptionist-attorney-questions.md`, section D).
- We run no storage for call content. The only places it can go are the client's Google Cloud Storage or Amazon S3 bucket.

**Left open:**

1. "Could be CRM, could be SMS" was said earlier the same day. The policy and its clarification name Google Cloud and Amazon S3
   only. Read here as: those two are the supported kinds, and CRM and SMS are not offered.
2. What happens to a call's content when the write to the client's bucket fails. Vapi's answer is that the content is gone.
3. Which of Vapi's connection methods are offered (S3 by key, S3 by role, Google by service account).
4. Whether the client chooses what is written (recording, transcript, logs, each its own switch as in Vapi) or gets all three.
5. Whether connecting storage is offered as an onboarding step, or only in the dashboard afterward.
6. Cost and latency per call: Retell's call object carries `call_cost` and `latency` (section 7). Storing them is not built.
7. Settled 2026-11-18: the wording callers and clients are given stays. The greeting says "this call may be recorded", and the
   signup checkbox and the first paragraph of the addendum say calls are recorded. Retell records every call and, with
   `basic_attributes_only`, deletes the files about 10 minutes later; we keep no recording either way (section 1).

## 9. Questions the multi-step form puts to the client (kept 2026-11-17, not built)

Jeff, 2026-11-17: "This multi-step form must answer your questions", and of the list below, "keep those question I don't think we
are there yet". These are questions for the client to answer in the form. None is ours to answer for them, and none is built.

1. **Do you want anything from your calls kept?** No, keep nothing (Zero Data Retention, the default) / Yes, in my own storage.
2. **Where is your storage?** Amazon S3 / Google Cloud. Asked only after a yes.
3. **Your storage connection.** The bucket name, region and access details for the one chosen.
4. **What should be written there?** Recording / Transcript / Logs, each its own tick box.
5. **If your storage can't be reached when a call ends, that call's content is discarded.** A box to tick, not a choice, because
   we keep no copy.
6. **How do you want to hear about a caller?** The answer choices are not known.
7. **How should your receptionist answer?** Greeting, business hours, questions to ask callers, the number to transfer to,
   emergency keywords.

Questions 1 to 5 come from the policy in section 1. Questions 6 and 7 have no answers from Jeff yet. Which of the seven belong in
the form, and in what order, is not decided.
