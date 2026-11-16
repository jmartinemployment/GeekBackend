# Geek-Receptionist walkthrough

Jeff's notes from going through the site at https://geek-receptionist.geekatyourspot.com, in the order he raised them. Each item says
what he said, what is decided, and whether it is done. Nothing here is built until it says so.

## Look

1. **The site does not use his style guide.** (2026-11-16, "did you find and use my style guide?") The guide is
   `~/development/geek-style-guide.html`. The site uses a blue (`#1a5fb4`) and system fonts I chose; neither is his.
   - Agreed reading ("sounds right"): his blues are section grounds, not accents; the accent is orange `#C83803` on navy `#0B162A`.
   - Proposed exact change, **waiting for his yes**: navy ground, Sora headings and Figtree body, orange `btn-primary` with teak hover,
     navy `#132340` panels, links that follow the ground, no light/dark switching. Navbar, footer and section grounds not included
     unless he asks.
2. **The home page is "pretty lame".** It is a placeholder: a title, one sentence, two buttons. He is "very, very picky about the
   front-end": the look and the words are his. **Waiting for his design or copy.**
3. **"Set up a receptionist" is temporary.** Placed by me; it can go whenever he says. A signed-in owner never sees it (the home page
   sends them to the dashboard); signed out, it shows again.
   - Gap found: a signed-in owner who opens `/signup` directly gets step 1. Offered to send them to the dashboard. **Waiting for his yes.**

3a. **Favicon.** (2026-11-16) The site had none (`/favicon.ico` answered 404). **Built, committed locally, not pushed:**
   geekatyourspot.com's orange @ (`favicon.gif`, `favicon.ico`, apple `icon.png`), linked the same way as that site's layout.

## Signup

4. **"What the business does" is an IRS code, not free text.** Jeff: "Change What the business does to the Class C" (2026-11-16).
   **Built, committed locally, not pushed.** Signup step 1 offers the 303 codes of the 2025 Schedule C chart (IRS Principal
   Business Activity codes), grouped by sector; GeekAPI serves the list, refuses a code not on it, and decides healthcare
   (`621`, `622`, `623`, `541940` veterinary) and addendum (`5411`, `5412`, `52`, `61`) by prefix. The tenant stores the code
   (`business_activity_code`, migration 0065) and its name in `industry_vertical`.
   - The Schedule C chart has no `541511` Custom computer programming; for Geek @ Your Spot it is `541510` Computer systems design
     & related services.
   - Open for Jeff: the receptionist's script reads "works in {industry}", which now gives the IRS name, e.g. "works in Plumbing,
     heating, & air-conditioning contractors".

5a. **County is gone.** Jeff: "Why do you ask County?" then "eliminate" (2026-11-16). It was optional and nothing read it. Removed
   from the signup form, GeekAPI's request, the tenant record and the store; migration 0066 drops the column. **Committed locally,
   not pushed.**

## Not working yet (from before the walkthrough)

5. ~~`GEEK_BACKEND_API_KEY` on Vercel~~: done. Jeff re-added it as Sensitive (Production) at 14:47 on 2026-11-16, redeployed at 14:47:57;
   "Needs Attention" is gone.
6. Signup's PayPal steps are not on GeekAPI: needs his "push" (743b3ed and the plan-file commits) and the four `PAYPAL_PLAN_*`
   variables.
7. Connect Google Calendar: GeekAPI's `PORTAL_BASE_URL` and the Google OAuth client's redirect URI still name the earlier
   `receptionist.` address; both must become `geek-receptionist.`.
