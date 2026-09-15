# What competitor (and partner) data is actually *for* in the Create pipeline

## Context

This documents the strategic purpose of competitor/partner extraction data in GCC V2 Create — what job it
does for content strategy — grounded in what `GccV2CompetitorExtractionService` /
`GccV2PartnerExtractionService` actually implement, verified against the real code (not aspirational). It
also calls out where the schema anticipates capability that was never wired up.

## Correction: what "Partner" and "Competitor" actually mean in this business model

Confirmed directly by the operator, correcting an earlier misreading in this doc's first draft:

- **Partners** = third-party SaaS/software tools the operator sells, resells, or promotes (an
  affiliate/reseller relationship) — confirmed by the Create form's own copy: *"Partners you sell or
  name"* (`new-create-form.tsx:1606`).
- **Competitors** = **rivals to the operator's own business** (e.g. other AI-consulting agencies/service
  businesses competing for the same clients and search visibility) — **not** rivals to the partner tools
  being promoted. Confirmed by the operator directly, and consistent with real production data found
  during this investigation: the 5 domains resolved from `geek_crawler.crawl_runs`
  (`webpalmbeaches.com/ai-consulting-delray-beach.html`, `aipoweredconsulting.ai`, `aligninnovate.com`,
  `hiteshi.com`, `syntecho.com`) read as rival AI-consulting agencies, not rival SaaS products.

This means the operator has **no first-party product** flowing through this pipeline at all — the content
being produced promotes *someone else's* tool (the partner), while differentiating the operator's own
business/service from *other businesses* (the competitors). Partner and competitor data don't compete for
the same "slot" (product vs. rival product) — they answer two different questions: "which tool should the
reader use" (partner) and "why work with us instead of another agency" (competitor).

## Competitor data — intended purpose, mapped to what's actually built

**An AI content creator uses information about rival businesses to identify content/SEO gaps, position the
operator's own service against those rivals, and avoid legally-risky claims about them** — instead of
guessing, it grounds Create's writing in verified, structured facts scraped from each rival's own site. In
this codebase, that intent is implemented unevenly: three mechanisms are real and enforced; several more
exist as data but are inert.

**🔍 Spotting content gaps** — `GapMap` (`GapTopic`, `DepthAssessment`, `OpportunityForUs`) captures where
a competitor covers a subtopic shallowly or not at all, so Create can go deeper. **Implemented, but
prompt-only** — it's printed into the LLM's writing notes as a suggestion; nothing prioritizes it, verifies
the model used it, or turns it into an actionable checklist.

**📐 Reverse-engineering structure/layout** — the schema literally has `DemandSignalAsset.ContentHierarchy`
and `.StructureWorthBeating` fields, meant for exactly this (studying a rival's heading/outline structure
to out-format them). **These are dead code** — extracted, never read anywhere. The one structure-adjacent
field that *is* used, `ComparisonAxes` (`StandardizedFeatureId`/`RivalCapabilityPayload`), only feeds a
prompt line — it doesn't drive an actual comparison-table layout decision.

**📈 Deconstructing messaging/hooks** — `FramingBank` (`FrameExcerpt`, `Sentiment`, `FrameType`) captures
us-vs-them framing language found on rival pages — the closest thing here to "psychological trigger"
extraction. **Prompt-only**, same caveat as above.

**🚦 Continuous monitoring / alerts** — genuinely **not implemented**. `Changelog`
(`ChangeKind`/`ChangeSummary`/`StatedAsOf`) exists in the schema specifically for pricing/feature/CTA
change tracking, but it's fully dead — no scheduler, no diffing, no alerting. Every competitor crawl is a
point-in-time snapshot; there's no "notify when a rival changes their price" capability, despite the data
model anticipating one.

**The three mechanisms that are actually real and enforced (this is where competitor data earns its
keep):**
1. **Positioning against rival businesses, expressed through partner-tool recommendations** —
   `DeficitRouter` (`TriggerDeficit`/`RecommendedSwap`/`PivotCopy`) is the one competitor signal that
   reliably reaches final copy: it's merged into the *partner* document's `Alternatives` list
   (`GccV2GeekCrawlerResearchResolver.EnrichPartnerExtractionAfterCompetitorAsync`,
   `GccV2GeekCrawlerResearchResolver.cs:219-236`) and surfaces as "switch from Competitor X because of
   weakness Y" messaging attached to the partner tool being written about. Since the operator has no
   product of their own, this is really: *"here's a weakness in rival agency/business X's approach —
   here's why our recommended tool/stack solves it better."* It's real reverse-engineering of a rival
   business's shortcomings into the operator's own service positioning — not aspirational, it ships.
   Structurally a bit of an odd fit (competitor deficits get filed under the *partner tool's* alternatives
   list rather than under the operator's own service positioning directly), but functionally it works
   because the partner tool recommendation *is* the vehicle for the operator's positioning.
2. **Steering what gets written** — `TypeLabels` (`CompetitorType`/`TypeRationale`) drives PLAN's actual
   research/topic framing (`GccV2CompetitorTypePlanRouting` → `GccV2PlanService.cs:142-148`) — e.g.
   whether this is framed as a direct-rival comparison vs. a content-rival differentiation piece. This is
   the single biggest real lever competitor data has on the finished draft.
3. **Legal/compliance guardrail** — `ClaimRiskFlags` (superlative/absolute/dated claim detection) is
   checked by a fail-closed VALIDATE gate (`GccV2CompetitorClaimRiskGate`,
   `GccV2ValidateService.cs:288`) that **rejects the job outright** if the draft echoes an unverified risky
   claim about a rival. This isn't a content-strategy win so much as a liability shield — but it's the
   most heavily enforced use of competitor data in the whole pipeline.

**Pricing/offer comparison** (`PricingCatalog`, `OfferCtas`, `Disqualifiers`) exists to let Create cite a
rival's real, verified pricing/limits rather than guess — feeds both prompt text and (nominally)
competitor JSON-LD. In practice the JSON-LD half never ships (see below), so this mostly reduces to
prompt-text grounding today.

## Where this differs from typical "AI competitor analysis" practice

- **No AI-visibility tracking** (are we cited by Perplexity/Gemini/etc. vs. this rival) — not part of this
  pipeline at all; that's the `ai-seo`/GEO domain, a separate concern from Create's competitor extraction.
- **No continuous/alerted monitoring** — every competitor signal is captured once, at crawl time, with no
  freshness re-check or change-notification loop (`Changelog` is vestigial).
- **No visual/ad-creative remixing** — nothing here scans a rival's ad creative or imagery; extraction is
  text-only (Markdown/HTML paragraphs).
- **Structure/layout reverse-engineering was designed for but never wired up** — the schema has the
  fields; nothing reads them.
- **Schema markup (JSON-LD) is asymmetric**: partner JSON-LD actually gets embedded on live tool pages
  (`GccV2ToolPageSchemaBuilder.cs:28`); the identically-shaped competitor JSON-LD
  (`GccV2CompetitorSoftwareApplicationJsonLd`) is computed once
  (`GccV2GeekCrawlerResearchResolver.cs:535`) and never emitted anywhere — a real gap if the intent was
  ever to publish competitor comparison schema.

## What partner data is for

**Correction/clarification: "Partner tools" are not the operator's own product.** The Create form's own
copy says it plainly (`new-create-form.tsx:1606`): *"Partners you sell or name."* Partner tools are
third-party SaaS/software the operator promotes, resells, or earns affiliate/referral revenue from — this
is an affiliate/reseller review-and-comparison content model (think: a SaaS review or "best tools for X"
site), not first-party product marketing. That single fact explains an otherwise-odd field:
`AffiliateDisclosureAsset` only makes sense once you know the operator is monetizing recommendations of
tools they don't own.

Given that, partner data exists to let Create write **grounded, cited claims about tools the operator
sells/promotes** — real pricing, real integrations, real proof points, scraped and verified from the
tool's own site — rather than let the model invent or oversell them. Two things follow directly from the
affiliate-business-model framing:

- **The citation gate matters more than it would for first-party content.** `Citables` feeds the VALIDATE
  fail-closed gate (`GccV2PartnerCitableBridge` → `GccV2CitationEvidenceGuard`) precisely because the
  operator has a financial incentive (referral revenue) to overstate a tool's merits — verified-quote-only
  citation is the guardrail against that.
- **`Advertisements`, `OfferCtas`, `Alternatives`, and the Ads-specific write seed
  (`GccV2WriteService.BuildPartnerExtractionAdsSeed`) exist to drive affiliate/referral conversions** —
  ad copy and CTAs pointing at the partner tool's own offer, not brand marketing for the operator.
- **`AffiliateDisclosureAsset.JurisdictionOrPolicy`** — the one field that would actually matter for
  compliance in an affiliate content business (FTC-style disclosure requirements vary by jurisdiction) —
  is dead: always `null`, never set, never read. Given the business model, this is arguably the most
  consequential dead field in the whole schema, not a minor omission.

Its one legally-enforced mechanism (`Citables` → citation gate) is narrower in trigger condition than
competitor's `ClaimRiskFlags` (which specifically targets superlative/absolute/dated language), but broader
in scope (it gates *any* uncited factual claim, not just risky-sounding ones). Most other partner fields
(`Icp`, use-case playbooks, battlecards, demo beats, compliance snippets) remain unenforced prompt
suggestions, same pattern as competitor's prompt-only fields.

## Reference: JSON-LD shape (schema.org)

Both partner and competitor extraction build the identical structure:

| schema.org type | Built from |
|---|---|
| `SoftwareApplication` (root) | `Categories` → `applicationCategory`; `Integrations` → OS/`ApiOrSdk`; `TypeLabels` → name/url/description (competitor only) |
| `Offer` (nested) | `OfferCtas`, `PricingCatalog` list price |
| `UnitPriceSpecification` (nested in Offer) | `PricingCatalog` billing period |
| `Review` (nested) | Partner: `Citables`/`Disqualifiers`/`Alternatives`. Competitor: `Disqualifiers`/`DeficitRouter`/`DemandSignals.AdOrCopyTheme` |
| `Organization` (nested in Review, as reviewer) | Constant: `"Master RAG Pipeline Audit"` |

Only the partner side is ever emitted (`GccV2ToolPageSchemaBuilder.cs:28`). The competitor side is built
and discarded (see above).
