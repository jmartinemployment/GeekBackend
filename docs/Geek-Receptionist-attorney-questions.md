# Geek-Receptionist: questions to ask your attorney

One list, merged 2026-11-18 at Jeff's instruction. It joins four sources:

- **(ours)** the five questions written 2026-11-17 in `docs/Geek-Receptionist-no-retention.md` section 5, which now points here.
- **(AI 1)** the nine questions from an AI's answer to Jeff's prompt on whether Geek @ Your Spot LLC should retain any part of its
  inbound calls.
- **(AI 3)** the seven questions from the follow-up answer on industry exceptions and consent before the call continues.
- **(AI 4)** the eight questions from a second opinion on the same retention prompt.

The AI questions are kept in that AI's wording. The full answers are saved in `docs/Geek-Receptionist-retention-ai-responses.md`
under the same numbers. Nothing in them was checked by us, so this document holds their questions and none of their conclusions.

**Retell AI was dropped on 2026-11-18** (Jeff: "Retell is dropped") and its code removed. The planned voice AI is Twilio
ConversationRelay with our own model and voice, not built; today no AI hears a tenant's call. The AI questions that name Retell
(1, 2, 13, 14, 24) are kept as they were asked; read Retell there as the voice vendor. Our own questions now name the voice vendor.

No question was dropped. Where two overlap they sit next to each other. The policy is
`docs/Geek-Receptionist-zero-data-retention.md`.

## How to ask

Ask Florida counsel with multistate communications and privacy experience (AI 4).

Give the attorney the actual architecture, not a generic privacy-policy request, and ask for written answers (AI 1). Take the actual
vendor settings, contracts, call-flow timing, and sample logs (AI 4).

Ask the attorney to distinguish the law that applies where the caller is from the law that applies to a Florida business. Interstate
recording disputes can involve choice-of-law questions, so a state-by-state list is not a substitute for that analysis (AI 1).

## A. Recording and the notice in the greeting

1. (AI 1) Does temporary recording and processing by Retell constitute interception under Florida Statute § 934.03, even if all
   content is deleted within approximately ten minutes?
2. (AI 3) Does Retell's temporary audio processing constitute interception even when no recording is retained by Geek @ Your Spot?
3. (ours) Whether keeping no recording changes the call-recording notice duties (Fla. Stat. 934.03 and other all-party-consent
   states), given that the voice vendor will process the audio live.
4. (AI 1) Does the greeting's announcement followed by continued participation establish prior consent under Florida law?
5. (AI 4) Is continued participation sufficient for your exact notice and use, or should you require affirmative agreement?
6. (AI 1) Does the same announcement and continued participation satisfy applicable consent requirements in California, Connecticut,
   Illinois, Maryland, Massachusetts, Montana, Nevada, New Hampshire, Pennsylvania, Washington, and the more ambiguous jurisdictions?
7. (AI 4) Does the exact connection-to-disclosure-to-consent sequence satisfy Florida law and the laws likely to govern interstate
   calls, including Washington's recorded-announcement provision?
8. (AI 3) What additional state-specific requirements apply to callers outside Florida?
9. (AI 1) Is a non-content event log sufficient for the AI disclosure and recording-notice evidence I intend to retain?
10. (AI 4) Which current state AI disclosure laws reach this inbound receptionist, and what changes take effect soon?

## B. Asking for consent before the AI is connected

11. (AI 4) Does capturing caller audio before the greeting ends create exposure, and can a pre-recording keypad or other consent
    gate resolve it?
12. (AI 1) Would a keypad-based affirmative consent mechanism before recording provide a sufficiently defensible nationwide approach?
13. (AI 3) Is affirmative DTMF consent before connecting Retell sufficient under Florida's interception statute?
14. (AI 3) Would an affirmative SMS response before connecting Retell provide sufficient electronic consent?

## C. Text messages

15. (AI 1) Does the verbal agreement followed by an inbound YES adequately establish consent for the two transactional SMS messages
    under TCPA and applicable state laws?
16. (AI 4) Does the initial oral permission adequately authorize the first SMS, and what evidence will satisfy both applicable law
    and your approved Twilio campaign?
17. (AI 3) Does the proposed SMS consent architecture satisfy Twilio's messaging policies and applicable TCPA requirements?
18. (AI 1) What retention period should apply to SMS consent records, given Twilio's messaging policy and applicable limitation
    periods?
19. (AI 4) What retention periods and legal-hold triggers apply to the minimal call events, SMS evidence, and opt-out records?

## D. What may be published, and what retaining audio would bring

20. (ours) Whether the policy sentence ("We do not record, archive, retain, or provide access to call histories.") can be published
    as written while Twilio keeps call records, and while the voice vendors behind ConversationRelay may hold call content for a
    time (not checked; OpenAI keeps API data up to 30 days without approved zero retention, which is not in hand).
21. (AI 4) How should your privacy promise distinguish temporary vendor processing, retained compliance metadata, SMS records,
    appointment records, and carrier-held call records?
22. (AI 1) Does retaining ordinary voice audio, without generating voiceprints, trigger any biometric privacy requirements in the
    states where callers reside?

## E. Vendors and the customer agreement

23. (AI 4) Do any vendors create voiceprints, retain training data, or retain content outside the stated deletion process?
24. (AI 1) What vendor disclosures, data-processing agreements, and privacy-policy language are necessary for Retell and Twilio's
    temporary processing and independent retention?
25. (ours) What the customer agreement and the data processing addendum must say about each vendor in
    `docs/Geek-Receptionist-no-retention.md` section 3.

## F. Which customers are excluded

Jeff's decision, 2026-11-18: securities broker-dealers, investment advisers, commodity futures and derivatives businesses, Medicare
sales organizations and telemarketing are excluded, with healthcare and veterinary businesses, which already were
(`docs/Geek-Receptionist.md` section 5, item 1).

26. (AI 3) Can your standard service legally exclude customers with mandatory recording obligations through its terms of service and
    customer qualification process?
27. (AI 3) Are there particular industries or customer activities that should be expressly excluded from your service?
28. (ours, 2026-11-18) Every answer to the seven excluded-industry questions is kept forever with the visitor's address and country,
    including for a visitor who answered yes, was refused and never became a customer. Whether that may be kept without end, and
    what the signup site must tell the visitor (it has no privacy page today).
29. (ours) Whether brief processing of a call makes Geek @ Your Spot LLC a business associate for a medical customer, and what
    agreements that needs with the customer, the voice vendors and Twilio. Healthcare and veterinary businesses are refused today.
30. (ours) Whether any law that applies to a customer's profession requires the customer to keep call records, and that the customer
    agreement puts that duty on the customer. The signup form asks legal, accounting, tax, payroll, finance, insurance and education
    businesses to agree to an addendum.
