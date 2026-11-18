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

3a. **Favicon.** (2026-11-16) The site had none (`/favicon.ico` answered 404). **Pushed 2026-11-16 21:13 UTC:**
   geekatyourspot.com's orange @ (`favicon.gif`, `favicon.ico`, apple `icon.png`), linked the same way as that site's layout.

## Signup

4. **"What the business does" is an IRS code, not free text.** Jeff: "Change What the business does to the Class C" (2026-11-16).
   **Pushed 2026-11-16 21:13 UTC, removed 2026-11-18 (not yet pushed).** Signup step 1 offered a list of 303 IRS business
   codes and GeekAPI decided healthcare and the addendum on the code. Jeff vetoed the list on 2026-11-18. Signup now asks five
   separate excluded-industry questions (Jeff, 2026-11-18: HIPAA, securities broker-dealer, investment adviser, commodity
   futures or derivatives, Medicare Advantage or Part D TPMO; veterinary and telemarketing are no longer excluded) (the professional services question and its addendum were removed by Jeff the same day), stores every excluded-industry answer forever,
   and takes what the business does as the owner's own words (`industry_vertical`). The code list, its route and the field that
   stored a tenant's code are gone (migration 0069). `docs/Geek-Receptionist.md` section 5, item 1.
   - Open for Jeff: the receptionist's script reads "works in {industry}", which now gives the IRS name, e.g. "works in Plumbing,
     heating, & air-conditioning contractors".

5a. **County is gone.** Jeff: "Why do you ask County?" then "eliminate" (2026-11-16). It was optional and nothing read it. Removed
   from the signup form, GeekAPI's request, the tenant record and the store; migration 0066 drops the column. **Pushed 2026-11-16 21:13 UTC.**

5b. **Phone.** Jeff: "Phone is lame. With the country code, as +1 and ten digits. Country must be U.S.A. or it is not valid"
   (2026-11-16). The box now takes a US number as people write it, placeholder `(561) 555-0101`; GeekAPI stores it as `+1` and
   ten digits and refuses anything that is not a US number (`contact_phone_not_us`). **Pushed 2026-11-16 21:13 UTC.**
5c. **State.** Jeff: "Like you need State or County ?" State's only use is the receptionist's script, "a business in {state}".
   **Waiting for his word** on whether to remove it.

5d. **Country in County's place.** Jeff: "You could replace the stupid County with Country and validate." A Country choice
   (United States or Another country); GeekAPI refuses anything but US. **Pushed 2026-11-16 21:13 UTC.**
5e. **Checkbox.** Jeff: it "can be simplified, and is also missing other exclusions". Proposed text, **waiting for his word**:
   "This business is not a healthcare, veterinary, legal, accounting, tax, payroll, financial, insurance or education business,
   and calls to it may be answered and recorded by an AI receptionist that tells callers so."
5f. **Captcha.** Jeff: "Needs captcha", then Google reCAPTCHA. **Pushed 2026-11-16 21:13 UTC:** the "I'm not a robot"
   checkbox on signup step 1 and on sign-in, checked by Google's siteverify on the site's server before GeekAPI is called. Seen
   working locally with Google's test keys. Key: "Geek-Receptionist checkbox" (Website · Checkbox challenge) in Google Cloud project
   `geek-oauth-04-11-2026`, Security → Fraud Defense (Google's new name for reCAPTCHA); domains `geek-receptionist.geekatyourspot.com`
   and `localhost`. Both values set on Vercel by Jeff, Sensitive, Production, 16:01 and 16:03 on 2026-11-16. Two earlier keys of the
   score type (named RECAPTCHA_SECRET_KEY and RECAPTCHA_SITE_KEY in Google Cloud) are unused and can be deleted. The live site has
   no captcha code until the site push.

## Not working yet (from before the walkthrough)

5. ~~`GEEK_BACKEND_API_KEY` on Vercel~~: done. Jeff re-added it as Sensitive (Production) at 14:47 on 2026-11-16, redeployed at 14:47:57;
   "Needs Attention" is gone.
6. ~~Signup's PayPal steps~~: pushed 2026-11-16 21:13 UTC; the four `PAYPAL_PLAN_*` (sandbox plans) set on GeekAPI the same minute.
7. Connect Google Calendar: GeekAPI's `PORTAL_BASE_URL` and the Google OAuth client's redirect URI still name the earlier
   `receptionist.` address; both must become `geek-receptionist.`.
