# Geek-CRM: the ACORD structure comes back, in our own names

Written 2026-11-12. Nothing in this plan is built, and no name in it is settled. Every name below is a
proposal for Jeff to accept or change.

## What went wrong

Jeff's decision (2026-11-10): start from the structure of his ACORD P&C CRM, rename the entities in his own
vocabulary, and make them generic enough to sell outside insurance. What was done instead: the structure was
cut down to the sales basics, the insurance-specific entities were dropped instead of renamed, Jeff was not
asked what to call them, and on 2026-11-11 the word ACORD was removed from the schema and the data-model
doc with a test to keep it out. Jeff vetoed a clean room. The structure comes back, in full, with new names.

## The entities and a proposed name for each

Names marked "exists" are already in Geek-CRM. All others are not built.

| ACORD entity | Proposed Geek-CRM name | State |
|---|---|---|
| Party | Participant (person or organization) | exists |
| Address, Phone, Email | Contact point (one record, three kinds) | exists |
| Lead | Lead (today only a lifecycle stage of a participant; ACORD keeps it as its own record) | partly |
| Activity | Activity | exists |
| Policy | Agreement (what a customer holds under terms, with start and end dates and an amount) | not built |
| Policy party (insured, owner, beneficiary) | Agreement participant, with a role | not built |
| Coverage | Agreement line (one component of an agreement, with a limit and an amount) | not built |
| Vehicle | Asset, kind "vehicle" | not built |
| Property | Asset, kind "property" | not built |
| Claim | Case (a request or incident raised against an agreement) | not built |
| Surety bond | Guarantee (an agreement that backs a promise to a third party) | not built |
| Bond party (principal, obligee, surety) | Guarantee participant, with a role | not built |
| Beneficiary | Designated recipient (a role on an agreement participant) | not built |
| Producer | Representative (who sold or services it) | not built |
| Tax identity | Tax record on a participant | not built |
| Audit log entry | Change record | not built |
| Dashboard rollups (renewal pipeline, claims summary, task summary, lead pipeline) | Dashboard summaries (upcoming renewals, open cases, tasks, lead pipeline) | partly |

Already in Geek-CRM with no ACORD counterpart: tenants, pipelines and stages, deals, custom properties, the
product catalog, projects, subscriptions and team members. They stay.

## Not yet read

The fields of each ACORD entity, and which parts of the full ACORD P&C XSD (as opposed to the CRM's own
models) to follow, have not been read in this plan. Reading them is the first step after Jeff settles the names.

## Order of work once the names are settled

1. Update the XSD first, with every new entity marked planned (the rule in the data-model doc), and remove the
   test that bans the word ACORD from the XSD and the doc.
2. Migrations and the contract tests for the new tables.
3. API routes and the screens, in the ACORD CRM's order: Participants, Agreements, Cases, Guarantees, Leads,
   Activities, with the dashboard summaries.
4. The screens follow the ACORD CRM layout Jeff showed on 2026-11-12.

## Open: Jeff to decide

- Accept or change each proposed name.
- Whether Lead becomes its own record as in ACORD, or stays a stage of a participant.
- Whether Vehicle and Property stay as one Asset with a kind, or stay two separate entities as in ACORD.
