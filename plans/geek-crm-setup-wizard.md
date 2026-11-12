# Geek-CRM: sign-up and the guided setup

Written 2026-11-12. Nothing in this plan is built yet.

## Why

Geek-CRM is sold to small businesses. Today a customer cannot start: Jeff's tenant was made by hand and a
user is added to a tenant by an API call with the admin key. The model for the fix is HubSpot's AI setup,
which Jeff has been showing screen by screen. A customer signs up, answers a few questions in a conversation,
confirms what the system proposes, and lands in a CRM that already fits their business. A "Set up manually"
button is on every screen and skips all of it.

## What the customer sees

One page with a progress line ("Step 3 of 5"). Each step has a short heading and sentence, then either a
card with a proposal or a list of options. Under the card is one question with the same four answers every
time: "Yes, looks good", "Edit it", "Skip this", and a "Something else" box for free text. Every answer stays
on the page as a "Q: ... A: ..." bubble, and one plain line says what changed ("Switched to Lead Generation,
now showing 5 reports"). Nothing is saved until the customer confirms that step.

## Feature 1: Sign-up creates the customer's own CRM

- A new person signs up through GeekOAuth, then Geek-CRM creates a tenant for them, seeds the default
  pipeline (New, Qualified, Meeting booked, Proposal sent, Won, Lost), and makes them its owner.
- Today this is done by hand: `/api/platform/tenant-users` with the admin key. The new route is for a signed-in
  user with no tenant, creates the tenant and their owner row in one transaction, and refuses a user who already
  has one (a user belongs to one tenant).
- Not verified: whether GeekOAuth lets a stranger register. Check this first. If it does not, sign-up needs
  that change in GeekOAuth before anything here can be used by a customer.
- A signed-in user with no tenant is sent to the setup page, not shown an error.

## Feature 2: The setup conversation

A `setup` record per tenant holds which step the customer is on and what they confirmed, so closing the tab
and coming back resumes. Steps:

1. **Your business.** The customer gives a website address. GeekAPI fetches that page and the model fills in
   what the business does, who it serves and what it sells. The customer corrects it. Without a website the
   customer types the same three things.
2. **Your main goal.** Options: catch missed calls, follow up new leads, book appointments, keep customers
   coming back, or something else.
3. **Your tools.** Gmail, Google Calendar, Outlook, Zapier. Ticking a tool records the wish. Connecting is
   Feature 5, and until it is built the screen says "We will connect these for you" in those words, so a
   tick-box never pretends to be a connection.
4. **Your workspace.** Three cards (Contacts, Companies, Deals) of proposed custom fields, plus the pipeline
   stages. Edit it lets the customer rename, add or remove. Confirming creates the field definitions and stages.
5. **Your first dashboard.** One of a small set of ready-made dashboards (see Feature 4), proposed from the goal.
   Edit it can swap to another. Confirming stores the choice.

## Feature 3: Custom fields and pipeline stages, editable any time

The setup creates them and the customer must be able to change them later, so they also get their own
Settings screens. Without these the wizard would produce fields nobody could edit afterwards.

- Fields: list, add, rename, change the allowed options, hide (retire) a field. The data model already
  enforces the rules (a field's type and key never change, a retired field keeps its stored values).
- Pipeline stages: add, rename, reorder, and mark one as the won stage or the lost stage. There are routes to
  read stages today and none to change them, so these are new, with a check that every pipeline keeps at
  least one open, one won and one lost stage, and that a stage holding deals cannot be removed.
- The person, company and deal screens show and edit the custom fields. They do not today.

## Feature 4: Ready-made dashboards

The dashboard is fixed code today. It becomes a choice among a few: Pipeline (today's), Lead generation,
and Receptionist activity (calls answered, texts sent, tasks from calls). The tenant stores which one it uses
and can change it in Settings. The model only chooses among these by name; it never invents a layout. A
customer-built dashboard is out of scope.

## Feature 5: Connect Gmail and Google Calendar (later)

Separate piece of work, started only after Features 1 to 4 are used by a real customer. Gmail connects so
emails to and from a contact appear on that contact; Google Calendar connects so booked meetings appear and
the receptionist can book. Outlook and Zapier follow. This needs Google's approval for the permissions it
asks for, which takes time and is Jeff's to start, so it is named here and not scheduled.

## The model's part

- The model proposes and code writes. It never touches the CRM.
- Calls go through GeekAPI to OpenAI (the live provider), using the existing OpenAI client in
  `Services/Workflow/Providers`, with the model name from configuration and no default in code.
- Each call is forced into a schema: the business summary, the goal-based field list, the dashboard name. The
  answer is checked before it is shown. A bad answer is a visible message with the "Set up manually" button.
  It is not retried and not replaced with a guess.
- A field proposal is checked against the same rules as a field the customer adds by hand.
- The customer's website text is data to read, not instructions to follow.
- A call that fails is logged at error level with the tenant id, the step and the failure code, and never the
  customer's text.

## Build order, one commit each, on main

1. Check GeekOAuth sign-up, then sign-up creates a tenant, seeds the pipeline and makes the signer owner.
2. The `setup` record, the proposal route for steps 1 and 2, and the model call that reads a website.
3. Stage editing routes and the Settings screens for fields and stages (Feature 3), with the custom fields
   shown on the person, company and deal screens.
4. The stored dashboard choice and the three dashboards (Feature 4).
5. Proposal and confirm routes for steps 4 and 5.
6. The setup screens in Geek-CRM, with progress, the four answers and the conversation bubbles.
7. A run with a real small-business website, from sign-up to a finished dashboard, before the wizard is
   called done.

## Tests

- Unit: sign-up refuses a second tenant for the same user; every setup step refuses a confirm out of order; a
  model answer outside its schema is refused for each of the three calls; a proposed field that breaks a
  field rule is refused; a stage edit that would leave no won or no lost stage is refused; a stage holding
  deals cannot be removed; "Set up manually" makes no model call.
- Not unit-testable here, so reported as not run: anything against the real database (Jeff has declined a
  local database), and the real website read.

## Out of scope

Billing and plans, teammate invites, customer-built dashboards, reports beyond the three dashboards,
Outlook and Zapier connections, and the visual redesign.
