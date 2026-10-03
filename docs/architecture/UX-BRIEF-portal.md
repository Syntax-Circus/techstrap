# UX Brief: TechStrap Portal (public support site)

**Handoff audience:** Claude Design, UX designer, and implementation team

> This is a **designer handoff**, not an implementation ticket. It describes what
> customers must be able to do, what each page must contain and how it must
> behave; layout is the designer's to propose within the chosen direction.
> **Visual direction is decided:** Carbon Copy v2 (owner decision, 2026-10-02),
> produced in [PHASE-02 (brand and UX)](PHASE-02-brand-and-ux.md).
> [`docs/BRAND.md`](../BRAND.md) is the system of record; the reference mockup is
> [`docs/design/mockups/direction-carbon-copy-v2.html`](../design/mockups/direction-carbon-copy-v2.html)
> (its Portal view). **For the Portal, the product's own branding leads and
> TechStrap stays in the background** (see [Visual Direction](#visual-direction)).
> No significant portal UI is built (PHASE-09) until BRAND.md is final. This
> brief covers the **web pages and the outbound emails**, because both are the
> customer's experience of support.

## Product Context

- **Application purpose:** `TechStrap.Portal` is the public Blazor SSR site of
  TechStrap, a self-hosted helpdesk for one company supporting many products.
  One portal domain serves every product under `/p/{key}`. Customers use it to
  find help (knowledge base), contact support, and follow a conversation about
  their ticket without creating an account.
- **Primary business/user outcome:** a customer either finds the answer without
  opening a ticket (deflection), or submits a ticket in under a minute, knows it
  was received (ticket number and email), and can return to the conversation
  from an emailed link on any device. For the company: fewer avoidable tickets,
  fewer incomplete tickets, no spam, no leaked customer data.
- **Related architecture artifact:** [02-ARCHITECTURE.md](02-ARCHITECTURE.md),
  [01-REQUIREMENTS.md](01-REQUIREMENTS.md),
  [PHASE-09-public-portal.md](PHASE-09-public-portal.md),
  [PHASE-05-intake-email-worker.md](PHASE-05-intake-email-worker.md) (emails),
  [PHASE-08-knowledge-base.md](PHASE-08-knowledge-base.md). Sibling brief:
  [UX-BRIEF-admin.md](UX-BRIEF-admin.md).
- **Design constraints:**
  - **Brand layering (amended 2026-10-02: product branding leads):** one portal
    domain, many products. The **product's name, logo and accent colour lead**
    on every `/p/{key}` page and in every customer email, supplied by an admin
    through `GetPublicProductRequestHandler` branding. TechStrap is **not** the
    visible brand: it supplies only the neutral structure underneath
    (typography, layout, components, neutrals, semantic colours) and appears
    only as a small "Powered by TechStrap" footer line (exact rule under
    Interaction and Content Rules). The portal has **no mascot, no jokes, no
    carbon tints, no Beige Box windows** and no TechStrap-led headings; those
    are Admin-only. The portal is **light-only in v1**. The design must work
    with **arbitrary admin-chosen accent colours** (see Contrast rules); it must
    also work with no logo (name only) and with a very long product name.
  - Blazor **static server-side rendering**: pages are fast, indexable and work
    without JavaScript by default. Interactive behaviour (live deflection) is a
    progressive enhancement and must degrade gracefully (Handoff Notes, question 3).
  - The portal never touches the database; it calls the API's public and
    token-authorised endpoints through typed clients over `TechStrap.Contracts`.
  - No customer accounts, passwords or sessions. Identity is possession of the
    emailed link (`/t/{token}`).
  - Mobile-first: most customers arrive from a phone or from inside an app's
    "contact support" link. Layouts are designed at phone width first.
  - English only in v1 (an i18n seam exists).
  - No third-party trackers, fonts from CDNs or CAPTCHAs in v1 (CAPTCHA, e.g.
    Turnstile, is only added if spam appears; see Handoff Notes, question 9).
  - Public pages must never reveal whether an email address or ticket exists.

## Users and Personas

- **Persona: Customer with a problem** (a user of one of the company's products)
  - **Goals:** get unstuck quickly; reach a human if self-service fails; know the
    message was received; get a reply and answer it from their phone without
    logging in.
  - **Pain points:** long forms; being asked for information the app already
    knows; not knowing whether a ticket was created; losing the link; email
    replies that do not thread back; jargon.
  - **Access/permissions:** anonymous. Can read published KB articles, submit a
    ticket for a product, and (with a valid token) read the public messages of
    their own ticket and reply to it. Cannot see internal notes, tags, assignee
    identity beyond the resolved agent name (D-024), or other tickets.
- **Persona: Self-servicer** (searches before asking, often via a search engine)
  - **Goals:** land on a KB article from a search engine, read it, decide if it
    solved the issue; browse by category.
  - **Pain points:** thin pages, no way to search, being forced to a form.
  - **Access/permissions:** anonymous; read-only.
- **Persona: In-app user** (arrives from a product's own "Contact support" button)
  - **Goals:** land on the right product's contact page, ideally pre-filled
    (D-024: the link may prefill `subject`, `name` and `email`, all visible and
    editable; app context goes through the SDK/API, not the URL).
  - **Pain points:** a generic page that does not look like the product they
    were just in.
  - **Access/permissions:** anonymous.
- **Persona: Support staff (secondary, indirect)** previews branding in the
  admin and receives the tickets; see [UX-BRIEF-admin.md](UX-BRIEF-admin.md).

## Key User Flows

### Find an answer first (KB deflection while writing a ticket)

- **Starting state:** customer on `/p/{key}/contact`.
- **Trigger:** typing in the **Subject** field.
- **Steps:**
  1. [ ] Customer types the subject. After a short pause and a minimum length
     (Assumption: 300 ms debounce, 3+ characters) the page queries
     `SearchPublicKbArticlesRequestHandler` for that product (plus shared
     articles).
  2. [ ] Up to 3 to 5 matching article titles with a one-line summary appear in a
     "These articles may help" region next to or beneath the subject. Links open in
     a new tab (with a visible new-tab cue) so the form is never lost.
  3. [ ] Customer either opens an article and abandons the form, or ignores the
     suggestions and continues.
- **Success state:** either an answered customer (no ticket) or a normal
  submission; suggestions never block, replace or disable the form.
- **Failure, loading, and empty states:** no results: region stays absent (no
  "nothing found" noise). Search failure or slow response: silently no
  suggestions; the form is unaffected. No JavaScript: no live suggestions; a plain
  link "Browse help articles" sits above the form. Suggestions update politely via
  a live region (count announced, not read out in full).

### Submit a ticket

- **Starting state:** `/p/{key}/contact`, themed for the product.
- **Trigger:** customer decides to contact support.
- **Steps:**
  1. [ ] Enter name, email, subject, message (and optionally attach files).
  2. [ ] Submit. A hidden honeypot field is untouched by humans (see below).
  3. [ ] Server validates, creates the ticket, queues the confirmation email, and
     redirects to the confirmation page (post/redirect/get: refresh does not
     resubmit).
- **Success state:** confirmation page (below).
- **Failure, loading, and empty states:** field-level inline errors plus an error
  summary at the top that receives focus; all entered text (and a note that files
  must be re-attached, if a server round-trip dropped them) is preserved; submit
  button disabled with progress text while sending; too-large or disallowed
  files named specifically; rate-limited requests show a friendly "too many
  attempts, try again in a few minutes" page that does not lose the message
  text where possible; an unavailable API shows an apology with the option to try
  again (and, Assumption, the company's fallback contact address if configured).
  A product that is inactive or unknown gets the uniform not-found page.

### Confirmation

- **Starting state:** just after a successful submission.
- **Trigger:** redirect from the form.
- **Steps:**
  1. [ ] Page states plainly that the request was received and shows the **ticket
     number** prominently (for example `ACME-142`) with a copy affordance, the
     subject, and the email address a confirmation was sent to.
  2. [ ] Explains the next step ("We have emailed you a link. Use it to follow
     the conversation and reply; replying by email is not supported yet.") and
     typical response expectations only if the company sets them (do not invent
     SLAs).
  3. [ ] Offers "Browse help articles" and "Back to {Product}".
- **Success state:** customer has the number and knows to expect an email.
- **Failure, loading, and empty states:** the page works if reloaded (shows the
  same confirmation, ticket number only; Assumption: not the ticket link, so the
  link is proved only by email ownership; Handoff Notes, question 4). Direct visits with no
  valid submission context get the uniform not-found page. If the email later fails
  it is handled by the admin dead-letter flow; the portal does not promise
  delivery times.

### Follow and reply to a ticket (no account)

- **Starting state:** customer clicks `/t/{token}` from an email on any device.
- **Trigger:** link click.
- **Steps:**
  1. [ ] The page shows: product theming, ticket number and subject, status in
     customer-friendly words (mapping of New/Open/Pending/Solved/Closed is a
     design/content decision, e.g. "Received", "In progress", "Waiting for your
     reply", "Solved", "Closed"), and the **public** conversation in chronological
     order (customer and agent messages, with attachments). Internal notes, tags,
     and internal metadata never appear.
  2. [ ] A **reply box** is below (or docked): message, attachments, Send.
  3. [ ] After sending, the reply appears on the page (post/redirect/get) and the
     status wording updates (a Pending or Solved ticket becomes In progress).
- **Success state:** reply visible, confirmation message announced politely.
- **Failure, loading, and empty states:** validation errors preserve the text;
  attachment errors named; an expired, revoked or malformed token gets the
  uniform error page (see below), never a distinct message. **Solved:** a clear
  note that replying reopens the ticket. **Closed:** the reply box is replaced by
  a message such as "This ticket is closed. If you reply, we will start a new
  follow-up ticket linked to it", with a button "Start a follow-up", which posts
  the reply and then lands on the **new** ticket's confirmation (new number,
  linked to the original; the follow-up arrives as `AddCustomerReplyRequestHandler`
  creating a child ticket). The Closed page still shows the full public history.

### Lost link

- **Starting state:** customer cannot find their link, or the link expired.
- **Trigger:** "Can't find your link?" on the contact confirmation, the footer,
  or the uniform error page.
- **Steps:**
  1. [ ] Customer enters their email address.
  2. [ ] The page **always** shows the same response ("If we have tickets for
     that address, we have sent a new link. Check your inbox and spam folder"),
     in the same time and with the same layout, whether or not the address
     matched (`RequestNewAccessLinkRequestHandler`). The email goes only to that
     address.
- **Success state:** the uniform notice; no information about existence.
- **Failure, loading, and empty states:** invalid email syntax is a normal field
  error (it leaks nothing); rate limiting is shown as the generic "try again
  later"; the form never hints at "no account found".

### Find help in the KB

- **Starting state:** product home or search engine landing.
- **Trigger:** browse categories, search, or follow a search-engine result.
- **Steps:**
  1. [ ] Browse categories (single level) for a product (and shared articles), or
     search (full-text; results show title, summary, category).
  2. [ ] Read an article (server-rendered Markdown, sanitised); see related
     category and a path to "Still need help? Contact support" (themed for the
     product).
- **Success state:** reader finds the article; the escalation path is always
  one click away.
- **Failure, loading, and empty states:** no results: suggestions to broaden, plus
  contact link; empty category/KB: friendly message (not an empty shell) and the
  contact route; archived or unpublished articles return the uniform not-found
  page with navigation to search.

## Screen Inventory

Routes are the portal's contract and follow `02-ARCHITECTURE.md` section 8.2; the
team should keep them stable for SEO.

- **Screen/route:** Portal root `/`
  - **Purpose:** orient visitors who arrive without a product.
  - **Primary actions:** choose a product (minimal chooser: active products with
    logo and name). No cross-product search in v1 (Handoff Notes, question 6).
  - **Data/state:** active public products from the API; redirects to
    `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` when that is set (02-ARCHITECTURE.md
    section 8.2); inactive products are never listed. This is the one portal
    page with no product context: it uses the neutral theme and the standard
    "Powered by TechStrap" footer, and no TechStrap-led headline.
  - **Authorization:** Anonymous.
- **Screen/route:** Product home `/p/{key}`
  - **Purpose:** the themed front door for a product.
  - **Primary actions:** search help, browse KB categories, contact support,
    "Check an existing ticket" (explains links arrive by email; offers the
    lost-link form).
  - **Data/state:** `GetPublicProductRequestHandler` (name, logo, accent),
    `ListPublicKbCategoriesRequestHandler`; static-rendered, cache-friendly.
  - **Authorization:** Anonymous. Unknown/inactive key: uniform not-found.
- **Screen/route:** Contact form `/p/{key}/contact`
  - **Purpose:** create a ticket, with deflection.
  - **Primary actions:** type subject (live suggestions), enter name/email/
    message, attach files, submit.
  - **Data/state:** `SubmitTicketRequestHandler` via the public product endpoint
    (multipart); form state; suggestion results; honeypot; validation errors.
  - **Authorization:** Anonymous, rate limited per IP.
- **Screen/route:** Confirmation `/p/{key}/contact/received` (Assumption: carries
  a short-lived opaque reference, not the access token)
  - **Purpose:** confirm receipt and show the ticket number.
  - **Primary actions:** copy ticket number, browse help, return home.
  - **Data/state:** ticket number, subject, masked email.
  - **Authorization:** Anonymous with a valid short-lived reference.
- **Screen/route:** Customer ticket `/t/{token}`
  - **Purpose:** read the public conversation and reply.
  - **Primary actions:** read, download attachments (authorised by the token),
    reply, start follow-up (Closed).
  - **Data/state:** `GetCustomerTicketRequestHandler` (public messages only),
    status, product theme (from the ticket's current product), reply form.
    `AddCustomerReplyRequestHandler` posts replies; `GetAttachmentRequestHandler`
    serves files. The token never appears in analytics, referrers or logs
    (`Referrer-Policy: no-referrer`, `noindex`).
  - **Authorization:** Valid token (hashed server-side; sliding expiry).
- **Screen/route:** Lost link `/p/{key}/lost-link` (product-scoped for theming)
  - **Purpose:** re-send the access link without revealing which emails exist.
  - **Primary actions:** enter email, submit.
  - **Data/state:** `RequestNewAccessLinkRequestHandler`; always the same
    response.
  - **Authorization:** Anonymous, rate limited.
- **Screen/route:** KB home `/p/{key}/kb`
  - **Purpose:** browse all published categories for a product (plus shared
    articles) and search.
  - **Primary actions:** open a category, search (GET form to KB search),
    contact support.
  - **Data/state:** `ListPublicKbCategoriesRequestHandler`; empty KB shows the
    friendly empty message plus the contact route.
  - **Authorization:** Anonymous. Unknown/inactive key: uniform not-found.
- **Screen/route:** KB category `/p/{key}/kb/{categorySlug}`
  - **Purpose:** list articles in a category.
  - **Primary actions:** open an article, search, back to home.
  - **Data/state:** published articles for the category.
  - **Authorization:** Anonymous.
- **Screen/route:** KB article `/p/{key}/kb/{categorySlug}/{articleSlug}`
  - **Purpose:** read an article.
  - **Primary actions:** read, search, contact support, share/copy link.
  - **Data/state:** `GetPublishedKbArticleRequestHandler` (rendered, sanitised
    HTML; shared articles reachable under any product they apply to, canonical
    URL decision in Handoff Notes, question 7).
  - **Authorization:** Anonymous.
- **Screen/route:** KB search `/p/{key}/kb/search?q=`
  - **Purpose:** search published articles.
  - **Primary actions:** search, open result.
  - **Data/state:** `SearchPublicKbArticlesRequestHandler` (same use case as
    deflection); paged; query kept in the URL.
  - **Authorization:** Anonymous.
- **Screen/route:** System pages: uniform not-found/invalid link, rate-limited,
  server error, `sitemap.xml`, `robots.txt`
  - **Purpose:** safe, calm failure states; crawler support
    (`GetSitemapEntriesRequestHandler`). Product-branded where a product is
    known, otherwise neutral. Plain copy: no mascot, no jokes, even on 404.
  - **Primary actions:** go home, search help, request a new link.
  - **Data/state:** none.
  - **Authorization:** Anonymous.

## Razor Presentation Architecture

Follows _template `RAZOR_COMPONENT_ARCHITECTURE.md`. API contracts are
`…Dto`/`…Request`/`…Response` in `TechStrap.Contracts`; ViewModels are
portal-only and feature-local. Pages are static SSR; components with injection,
lifecycle work or state are paired. The final list is set in PHASE-09 and
recorded in [02-ARCHITECTURE.md](02-ARCHITECTURE.md).

| Feature/route | Component pair (`.razor` / `.razor.cs`) | ViewModel or direct model | Factory decision | State behavior |
| :------------ | :-------------------------------------- | :----------------------- | :--------------- | :------------- |
| Product theming (layout) | `ProductLayout` pair (PHASE-09 names it `PortalLayout`); `ProductHeader` and `ProductFooter` inline; custom properties emitted on the page wrapper (no injected `<style>`) | `ProductThemeViewModel` (name, logo URL, accent, derived on-accent and accent-ink) | **Yes: `BrandingThemeFactory`** (PHASE-09's name), justified by non-trivial colour/contrast derivation shared across pages and email | Layout loads branding once per request; failure to load branding falls back to the neutral TechStrap theme, never a broken page |
| Product home | `ProductHome` pair; `CategoryList` inline | `ProductHomeViewModel` | None | Static SSR; loading not visible; error page on API failure |
| Contact form | `ContactForm` pair; `SubjectSuggestions` pair (enhanced); `FileInput` pair; `HoneypotField` inline | `ContactFormModel` (direct form model with validation attributes mirroring server rules) | None; attachment-rule display strings come from shared constants | Form model owned by the page; enhanced form post preserves values on error; suggestions are an optional interactive island (Handoff Notes, question 3) |
| Confirmation | `ContactReceived` pair | `ContactReceivedViewModel` | None | Stateless; handles missing/expired reference with the uniform not-found page |
| Customer ticket | `CustomerTicket` pair; `PublicMessageList` pair; `ReplyForm` pair; `ClosedNotice` inline | `CustomerTicketViewModel` (friendly status text, public messages) | **Maybe: `CustomerTicketViewModelFactory`** if status wording and attachment link shaping stay non-trivial; otherwise code-behind | Page owns token, reply form state and Closed behaviour; uniform error on any token failure |
| Lost link | `LostLinkForm` pair | `LostLinkModel` | None | Always renders the same result state after post |
| KB category and article | `KbCategoryPage` pair; `KbArticlePage` pair; `KbArticleBody` inline (renders trusted-sanitised HTML) | `KbArticleViewModel` | None | Static SSR; SEO meta set per page through `SyntaxCircus.Blazor.Seo` |
| KB search | `KbSearchPage` pair; `SearchBox` pair | `KbSearchResultViewModel` | None | Query in URL; empty/no-results states owned by the page |
| System pages | `NotFound`, `RateLimited`, `ErrorPage` inline | None | None | Uniform copy; no technical detail |

## Interaction and Content Rules

- **Navigation and information hierarchy:**
  - Header: product logo and name (links to product home), "Help articles",
    "Contact us". No login or account controls. Search box is available from the
    header on every product page.
  - Footer: the **"Powered by TechStrap" rule** below, plus "Can't find your
    ticket link?".
  - **"Powered by TechStrap" footer rule (exact):**
    1. Every portal page carries the footer line, including KB, contact,
       confirmation, ticket, lost-link, portal root and system pages. It is the
       **only** place the TechStrap name appears on the portal. TechStrap does
       not appear in the header, headings, button labels, page titles, Open
       Graph or SEO tags, or 404 copy; those use the product's name.
    2. Text is exactly "Powered by TechStrap": one line, a text link, small
       (caption size), in the neutral secondary ink at 4.5:1 or better on the
       page background; never in the product accent, never bold, never animated.
    3. An optional TechStrap mark at **16 px** may sit immediately before the
       text (the mockup does this). It is the static logo mark only: no mascot
       pose, no window frame, and it is not used anywhere else (not in the
       header, hero or ticket page body). It carries `alt=""` when the text is
       present.
    4. Placement: bottom of the page, in the footer region, inside the page
       container, with the same placement on every page. It never sticks to the
       viewport and never competes with the "Can't find your ticket link?" link.
    5. **Decided (owner 2026-10-02, D-024):** the text is a link to
       https://github.com/Syntax-Circus/techstrap (neutral secondary ink,
       underlined, same colour on hover; never the accent). It is shown by
       default. An installation-level setting hides it everywhere on the
       portal and in emails: `TECHSTRAP_PORTAL_SHOW_POWERED_BY=false`
       (default `true`; not per product). When hidden, the whole line and the
       optional mark are omitted and the footer keeps its other content.
  - **Agent identity (decided, D-024):** wherever a customer sees an agent (ticket
    view messages, and the agent-reply email), the name is the agent's **first
    name plus the product's support name**: "Sam from Orbitly Support". If the
    agent set a **public display name** it replaces the first name and is used
    as-is: "Samantha from Orbitly Support". **Assumption:** the " from {Product}
    Support" suffix is kept with an override. The product part is the product's
    branding display name (falls back to its name). Surnames are never added,
    and agent emails, ids and avatars never appear. The customer's own messages
    read "You". The portal and emails render the string the API resolved.
  - Hierarchy: help-first. The contact form is prominent but the search box and
    categories come first on the product home; the contact page leads with the
    form and keeps suggestions adjacent to the subject.
  - Product accent shows in: header rule and name/logo tile, primary button
    fill (label in the derived on-accent colour), links and accent text (in the
    derived accent-ink colour), and decorative highlights. The neutral
    structure supplies layout, typography, spacing, neutrals and semantic
    colours (error/success/warning), which are **never** replaced by the
    product accent. The focus ring is a high-contrast neutral, not the accent.
  - The **ticket number** is always shown in a distinct, copyable treatment.
- **Forms and validation:**
  - Contact fields: name (required), email (required, validated), subject
    (required, length-limited), message (required, length-limited; Assumption:
    a visible character counter near the limit), attachments (optional). Labels
    always visible (no placeholder-as-label); `autocomplete` attributes set
    (`name`, `email`); correct mobile keyboards (`type=email`).
  - Keep the form short; do not ask for product (it is the page's context) or
    information the in-app link already supplies.
  - **Prefill (decided, D-024):** `/p/{key}/contact?subject=...&name=...&email=...`
    may prefill exactly these three fields. They stay visible, labelled and
    editable; there are no hidden fields and nothing is auto-submitted. App
    context such as version and device goes through the SDK/API, not the URL.
    Prefilled values are validated and length-limited exactly like typed input
    (same rules, same error summary). Unknown parameters are ignored and never
    echoed; values are HTML-encoded; request logs redact `name` and `email`.
  - **Honeypot (invisible):** an extra field that real users never see or reach.
    It must not be visible, focusable (`tabindex=-1`), announced to assistive
    technology (`aria-hidden`, not just offscreen text), autofilled by browsers
    (`autocomplete=off`, a name that password managers ignore) or leave layout
    gaps. A filled honeypot must produce the **same** confirmation experience a
    real user would see (Assumption; the ticket is silently dropped) so bots get
    no signal. Never explain the honeypot to users; never rely on it as the only
    abuse control (per-IP limits and attachment/body limits also apply).
  - **Attachment rules (Assumption: defaults from the spec, configurable):**
    10 MB per file, 25 MB per message, an allowlist of common document and image
    types. The rules are stated **before** the user picks files ("Images and
    documents up to 10 MB each, 25 MB total"). Selected files are listed with
    name, size and remove button; violations are reported per file at selection
    (where script allows) and again on submit (server authority). Filenames shown
    are sanitised; attachments open as downloads, not inline execution. Maximum
    file count: Handoff Notes, question 11.
  - Reply form on the ticket page follows the same attachment rules and error
    behaviour.
  - Do not autofocus the first field on mobile in ways that pop the keyboard and
    hide context.
- **Notifications and errors:**
  - Plain, human, blame-free copy; state what happened, what is kept, what to do.
  - Validation: summary on submit plus per-field messages tied with
    `aria-describedby`; focus moves to the summary.
  - Uniform failure rule: an **invalid, expired, revoked or malformed token**, an
    unknown product key, an unpublished article and an unknown route all render
    the same not-found page (same status code semantics, same copy, no hint of
    which part failed). It offers: search help, contact support (when a product is
    known), and "Request a new link". Error pages are `noindex`.
  - Rate-limit page is distinct but equally generic ("Too many attempts").
  - Server errors show an apology and a correlation id the customer can quote
    (Assumption: shown on the error page only).
- **Destructive actions and confirmation:**
  - The customer has no destructive actions. The one consequential action is
    "start a follow-up" on a Closed ticket, and its button text says what it
    does ("Send and start a new follow-up ticket"). No confirmation modal; the
    explanatory text is the confirmation.
  - Replying to a Solved ticket reopens it; the text beside the button says so.
- **Loading, empty, offline, and degraded states:**
  - Static SSR means most pages have no loading state; progressive enhancement
    (suggestions, in-flight submit) shows small inline indicators only.
  - Empty KB/category/search have helpful copy plus the contact route.
  - Offline/slow: form submission failure keeps the text; no data loss; no
    modal "you are offline" blocking. If the API is down the portal shows an
    apology page (and the optional fallback contact address).
  - No stale sensitive content: ticket pages are `Cache-Control: no-store`.

### Outbound email templates (part of the customer experience)

Rendered by `IEmailTemplateRenderer` as **text and HTML** parts, branded per
product (product name, logo if hosted by the portal, accent colour, from
display name, reply-to), English only (an i18n seam is kept). Email is the
customer's door back into the portal, so the link is the most important element.
Common rules:

- **Brand rule (customer emails):** the **product leads**: product header (logo
  or name) and the product accent only. No TechStrap header, no mascot, no
  jokes, no carbon tints, no window frames, no stamp motifs. TechStrap appears
  only as a single "Powered by TechStrap" line at the very bottom, in both the
  HTML and the plain-text part, with the same wording and plain-text,
  secondary-ink styling as the web footer (an optional 16 px mark in HTML only;
  the link and the hide setting follow the web footer rule: GitHub link in HTML, bare URL in the plain-text part, omitted entirely when `TECHSTRAP_PORTAL_SHOW_POWERED_BY` is false; D-024). Emails are light
  designs that must survive client dark-mode inversion.
- **Common frame:** neutral structure with product header (logo or name),
  one primary call-to-action button plus the same URL as plain text, the ticket
  number and subject in a consistent position, and a short footer (who sent it
  and why; "Replying by email does not reach us yet; use the link above" until
  inbound email ships; no marketing) followed by the "Powered by TechStrap" line (unless hidden by the installation setting).
- **Subject lines:** consistent and threadable by humans, with the ticket number
  first, e.g. `[ACME-142] We received your request`, `[ACME-142] New reply from
  support`, `[ACME-142] Your ticket was closed` (copy is a design/content
  decision).
- **Email-client reality:** table-based, inline-styled HTML that survives
  Outlook, Gmail and dark-mode inversion; images have alt text; the message is
  fully legible with images blocked and in plain text; width about 600 px, a
  single column; touch-sized CTA button; minimum font size ~16 px for body.
- **Accent colour in email:** the product accent only, using the same
  derivation as the web, computed by `BrandingThemeFactory` and written as
  **inline literal values** (email cannot use CSS custom properties): accent as
  the CTA button background and a thin header rule, with the **on-accent**
  colour as the label (at least 4.5:1); any accent-coloured text link uses the
  **accent-ink** colour (at least 4.5:1 on the white email body); the raw accent
  is never used for small text. Set button background both as a `bgcolor`
  attribute and inline style, and the label colour explicitly, so inversion in
  dark-mode clients cannot produce unreadable text. Logos: no white-only logos
  (place the logo on a white or neutral backing cell); no logo means the product
  name as text (Handoff Notes, question 8). A very light accent (Pixelforge) and
  a very dark accent (Acme) must both pass without redesign.
- **Security:** the link is `/t/{token}`; emails never include internal notes,
  tags or other tickets; never include attachments in the email (customers get
  them through the link; Assumption); avoid showing the full token as visible
  text other than the URL; do not echo untrusted metadata.
- **Templates:**
  1. **Confirmation** (sent on ticket creation, including follow-ups): "We got it".
     Contains ticket number, subject, a short copy of what the customer wrote
     (truncated), the link to follow the conversation, the lost-link tip, and
     for a follow-up a sentence linking it to the original ticket number.
  2. **Agent reply**: the agent's public reply text (sanitised), linked KB
     articles as titled links (when the agent linked any), the agent's resolved name
     (D-024: "Sam from Orbitly Support", or the agent's public display name
     plus the same suffix; never email or surname), the CTA "View and reply", and the ticket number. Long
     replies are shown in full (not truncated) because the email may be the only
     thing read.
  3. **Closed notice**: sent when a Solved ticket is auto-closed (or manually
     closed): states it is now closed, shows the number, explains that replying via
     the link will start a **follow-up** ticket, and offers the link to the
     history. Tone: calm, no surprise.
  4. **New access link** (lost-link response): a minimal email with the link(s) to
     their tickets (Assumption: one email listing recent tickets; multiple
     tickets per requester: Handoff Notes, question 5). Sent only to the address
     entered; subject and body do not confirm anything on the web page, which
     stays uniform.
  5. **Solved notice** and 6. **Follow-up created** (both listed in PHASE-06's
     notification templates): same frame and rules; the Solved notice says that
     replying through the link reopens the ticket; the Follow-up created notice
     links the new ticket number to the original. Copy is a content decision
     for PHASE-06.
  Agent-facing alert emails (new ticket, assignment, customer reply) are an
  admin-side notification concern and are **not** in this brief (Handoff Notes,
  question 10).

## Accessibility and Responsive Behavior

Target: **WCAG 2.2 AA** (the audience is the general public).

- **Keyboard and screen-reader expectations:**
  - Fully keyboard operable; visible focus that is not obscured by sticky
    headers (2.4.11); a "Skip to main content" link; landmarks (`header`, `nav`,
    `main`, `footer`); one `h1` per page; heading levels follow structure.
  - Forms: programmatic labels, `aria-describedby` for hints and errors, error
    summary with links to fields and focus on arrival, `required` communicated
    in text, not just an asterisk, no timeouts that discard input (2.2.1).
  - Suggestions region is `aria-live="polite"`, announces a count, is operable
    by keyboard, and does not move focus; links that open a new tab say so.
  - The ticket page is an ordered list/articles with author, role (you /
    support), time (`<time datetime>`) and attachments as text; the reply form
    is reachable via a "Reply" skip anchor on long conversations.
  - Honeypot field invisible to assistive technology (see Forms).
  - Attachment picker has a native, keyboard-operable file input; no
    drag-only upload (2.5.7); targets at least 24x24 CSS px (2.5.8); do not
    require re-entering already supplied data (3.3.7); no cognitive test for
    access (3.3.8; the token link is the credential).
  - KB articles: semantic headings, lists, tables with headers, code blocks that
    scroll within their region and are keyboard-reachable, descriptive link
    text, meaningful image alt text (required in the editor).
- **Color contrast and non-color cues:**
  - **Accent-derivation rule (hard requirement).** Input: one admin-chosen
    `#RRGGBB` accent (re-validated before use). The portal is **light-only in
    v1**, so contrast is always measured against a white (`#FFFFFF`) page and
    surface. `BrandingThemeFactory` derives, automatically and verifiably, from
    that single input:
    1. **`--ts-accent`**: the accent as entered. Used only for fills that
       carry their own label or are decorative: primary button background,
       header rule, logo tile, highlights.
    2. **`--ts-on-accent`**: white or black (`#FFFFFF` / `#000000`, whichever contrasts more; see BRAND.md; exact value
       in BRAND.md), **whichever contrasts more with the accent**. It is the
       label colour on accent fills and must reach **at least 4.5:1**. (If
       neither reaches 4.5:1, which can happen for mid-tones, use pure black; if
       that still fails, apply rule 4.)
    3. **`--ts-accent-ink`**: the accent itself if it already gives **at least
       4.5:1 on white**; otherwise the accent **darkened** (same hue, lightness
       reduced step by step) until it does. Used for every accent-coloured
       text, link and meaningful non-text UI (also needs 3:1, which 4.5:1
       satisfies). BRAND.md may require a stricter target; 4.5:1 is the floor.
    4. **Fallback:** if the input is invalid, or no safe pair can be derived, use
       the neutral theme accent and surface the failure in the admin contrast
       report. The portal never renders an unchecked accent.
    The property names above are the contract (`--ts-accent`, `--ts-on-accent`,
    `--ts-accent-ink`); PHASE-09 now uses the same names (the earlier
    `--ts-accent-contrast` is retired). BRAND.md and the mockup use unprefixed `--accent`, `--on-accent`,
    `--accent-ink`; the same values drive the email inline styles.
    The accent is never the only carrier of meaning: links are underlined,
    focus rings use a high-contrast neutral not the accent, status uses text and
    icon. Hover or pressed states must not rely on further darkening (a very
    dark accent cannot darken); use an underline, outline or inset change. The
    admin branding screen shows a contrast report; the portal still protects
    itself if bad values slip through.
  - **Worked examples (ratios against white unless stated; derived values are
    illustrative of the rule, the factory computes the exact values):**

    | Product | Accent | On white | On-accent label | Accent-ink | Notes |
    | :------ | :----- | :------- | :-------------- | :--------- | :---- |
    | Pixelforge (very light) | `#F59E0B` | 2.15:1 (fails) | black `#000000` (about 9.8:1 on the accent; white would be 2.15:1) | darkened to about `#A26807` (about 4.65:1) | Raw accent must never be text or a meaningful border on white; button is amber with dark label. |
    | Acme Cloud Backup (very dark) | `#0F3D2E` | 12.16:1 | white (about 12.2:1) | unchanged (the accent already passes) | Accent is close to body-text ink, so links need underlines and the accent cannot signal state on its own; button is dark green with white label. |
    | Orbitly (mid, reference) | `#7C3AED` | 5.7:1 | white | unchanged | Passes as entered. |

  - Test the design with at least: very light yellow (Pixelforge), a very dark
    green (Acme), saturated red, mid-gray, near-black, and a brand-like blue;
    all must pass without redesign.
  - Product logos on unknown backgrounds: logo sits on a defined white/neutral
    surface with safe padding. The portal is light-only, so no dark logo
    variants are needed on the web; emails use a neutral backing cell
    (Handoff Notes, question 8).
  - Error and success states combine text, icon and colour.
- **Responsive layout behavior:**
  - Design at ~360 px first. Single column; comfortable reading measure on
    larger screens (a narrow, article-style column for KB and conversation), with
    wider space used for search/categories on product home. The contact form
    keeps suggestions beneath the subject on phones and beside/below on wide
    screens without layout jumps (reserve space to avoid CLS).
  - Primary actions are thumb-reachable; sticky submit/reply bars must not cover
    inputs or the on-screen keyboard.
  - Zoom to 400% and text-spacing overrides must not break layout; no
    horizontal page scroll (tables and code blocks scroll within themselves).
  - Print: KB articles print cleanly (nice-to-have).
- **Reduced-motion or other preferences:** honour `prefers-reduced-motion`
  (no animated suggestion entry or scroll effects), forced-colors mode, and
  user font-size settings. The portal is light-only in v1 and does not switch on
  `prefers-color-scheme`. No autoplay media, no carousels.

### SEO (public KB and product pages)

- Server-rendered HTML for every KB and product page; each has a unique
  `<title>`, meta description (from the article summary), canonical URL,
  Open Graph/Twitter tags using the product name and logo, and one `h1`
  (`SyntaxCircus.Blazor.Seo`).
- `sitemap.xml` lists product homes, categories and published articles
  (`GetSitemapEntriesRequestHandler`); `robots.txt` references it.
- **`noindex`:** customer ticket pages (`/t/*`), the contact confirmation,
  lost-link, search result pages with query strings (Assumption), error pages.
  Customer ticket pages also send `Referrer-Policy: no-referrer` and `no-store`.
- Structured data: `Article` or `FAQPage` JSON-LD on articles is a nice-to-have;
  breadcrumbs (`BreadcrumbList`) recommended (portal root > product > category >
  article).
- Clean, stable, lowercase slug URLs; renamed slugs 301 to the new URL (design
  to confirm with the KB model); archived articles return the uniform not-found
  (and drop from the sitemap).
- Performance budget (Assumption): LCP under 2.5 s on mid-range mobile; no
  render-blocking third-party resources; logos sized and lazily loaded below the
  fold; font loading strategy that avoids layout shift.

## Visual Direction

**System of record:** [`docs/BRAND.md`](../BRAND.md). **Reference mockup:**
[`docs/design/mockups/direction-carbon-copy-v2.html`](../design/mockups/direction-carbon-copy-v2.html)
(its **Portal** view, with the Orbitly, Pixelforge and Acme accent switcher).
Overall direction is Carbon Copy v2, chosen by the owner on 2026-10-02, but the
Portal uses **very little** of it. The Admin app carries the personality; the
Portal is the product's own front door.

How it applies to the Portal:

- **Product branding leads.** Product name, logo and accent colour are the
  visible brand on every page and email. TechStrap appears only as the small
  "Powered by TechStrap" footer line (and an optional 16 px mark), per the
  footer rule. Branding is data-driven (admin supplies name, logo, accent); the
  accent flows through the derivation rule (accent, on-accent, accent-ink).
- **Plain and neutral everywhere else:** friendly, plain, concrete copy. **No
  mascot, no jokes, no carbon tints (canary/pink), no dashed notched notes, no
  status stamps, no numbered paper-form header, no Beige Box windows, no
  keyboard layer or palette** on portal pages or in customer emails. The
  ticket page's "you" and "support" messages are distinguished by label,
  alignment and neutral surfaces, not by the Admin carbon tint code. 404 and
  error pages are plain too.
- **Light-only in v1:** one light theme; no dark theme, no
  `prefers-color-scheme` switching, no dark logo variants for the web.
- **Fonts:** the mockup loads fonts from a CDN for convenience only; the
  portal must self-host its fonts (no third-party font hosts, a stated
  constraint).
- **Mockup note:** the mockup's accent-ink uses a stricter 7:1 target; this
  brief sets 4.5:1 as the floor and BRAND.md may choose a stricter target.

### Bootstrap and SCSS guidance

- **Bootstrap 5 components/utilities to prefer:** container/grid with a narrow
  reading column, form controls with validation states, buttons, list groups for
  the conversation, cards sparingly (avoid card-everything per `DESIGN.md`),
  breadcrumbs, pagination, alerts for notices (with text and icons), badges
  for the ticket status, input groups for the search box, `visually-hidden`
  helpers, responsive utilities.
- **SCSS variable overrides:** neutral-structure tokens (fonts, scale, radius,
  spacing, neutrals, semantic colours, focus ring) are compile-time SCSS, from
  BRAND.md. Product theming is **runtime CSS custom properties** set from
  branding (`--ts-accent`, `--ts-on-accent`, `--ts-accent-ink`), consumed by a
  small set of themed utilities; do not generate per-product stylesheets. The
  same derived values drive the email templates' inline styles.
- **Custom SCSS justified only for:** the product theming layer, the ticket
  conversation treatment (you vs support), the ticket-number treatment, the
  suggestions region, the honeypot hiding rule, the footer line, and KB
  article typography (prose, code, tables, callouts).
- **Typography, imagery, and content tone:** highly readable typography tuned for
  long-form reading and for non-native English speakers; consistent monospaced
  or distinct ticket number style shared with the admin; imagery limited to the
  product logo and article images (alt text required); no decorative stock
  photography. Tone: friendly, plain, concrete and brief; avoid helpdesk
  jargon ("ticket" is acceptable; "case", "SLA", "escalate" are not
  customer-facing); never blame the user; apologise once, plainly, on errors.
  Product personality may show through the product's own name, logo and accent;
  the portal copy itself stays neutral so it fits every product.

## Handoff Notes

- **Open design questions** (all dispositioned; none is left unmarked):
  1. How far product theming goes. **Answered:** name, logo and accent only in
     v1 (the three fields the admin supplies). Hero image, tagline and
     per-product favicon are **Deferred post-v1** (they need new product
     fields and admin UI not in PHASE-07 or PHASE-09).
  2. Customer-facing status wording and agent names. **Answered in part:**
     provisional mapping New "Received", Open "In progress", Pending "Waiting
     for your reply", Solved "Solved", Closed "Closed", in plain copy; final
     wording is set in PHASE-09 (`CustomerTicketPresenter`) within the plain-copy
     rule. **Answered (owner 2026-10-02, D-024):** customers see the
     agent's first name plus the product support name ("Sam from Orbitly
     Support"), or the agent's optional public display name with the same
     suffix (Assumption); the same string is used in the agent-reply email.
  3. Live deflection on an SSR site. **Answered:** a small interactive island
     (`KbDeflectionSuggestions`, PHASE-09) with a no-JS fallback "Browse help
     articles" link; suggestions never block the form.
  4. Confirmation page and the access link. **Answered:** the confirmation shows
     the ticket number and "check your email" only; the link is delivered by
     email alone (proves email ownership; matches PHASE-09).
  5. Requester with several tickets in the lost-link email. **Deferred to
     PHASE-06** (`RequestNewAccessLinkRequestHandler` and the new-access-link
     template): it depends on how tokens are issued per ticket. UX constraint
     that stands: one email, sent only to the entered address, and the web
     response stays identical.
  6. Portal root with several products. **Answered:** a minimal product chooser;
     redirect to `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` when set (02-ARCHITECTURE.md
     section 8.2). No cross-product search in v1 (**Deferred post-v1**).
  7. Shared KB articles: canonical URL and multi-product presentation.
     **Deferred to PHASE-08/PHASE-09 (P09-T14):** the canonical choice depends
     on the KB model's slug rules and affects sitemap output; until settled, the
     per-product path under the visited product is canonical, and the article
     takes that product's branding.
  8. Logo handling for email and dark mode. **Answered:** the portal is
     light-only, so no dark logo variants for the web; emails place the logo on
     a neutral/white backing cell and never rely on a white-only logo; no logo
     means the product name as text (an initial tile in the accent is
     acceptable on web). **Deferred to PHASE-07 (P07-T14):** minimum
     dimensions, file types and size limits for the logo upload.
  9. Honeypot plus rate limiting vs CAPTCHA. **Answered:** enough for launch; no
     CAPTCHA space is reserved. A challenge (for example Turnstile) is added
     only if spam appears.
  10. Agent-alert emails and the customer email frame. **Answered:** they do not
      share it. Alerts go to agents, are plain working text and use no
      mascot or humour; they are not product-led customer mail. **Deferred to
      PHASE-06** (notification templates) for their layout.
  11. Attachment UI limits (file count, thumbnails, virus-scan messaging).
      **Answered in part:** a plain file list with name, size and remove; no
      image thumbnails; no virus-scan messaging in v1. **Deferred to PHASE-06:**
      the maximum file count, which is an attachment-rule constant (the 10 MB
      and 25 MB limits stand).
  12. Dark theme for the portal. **Answered:** light-only in v1 (owner decision
      2026-10-02).
  13. In-app launch prefill. **Answered (owner 2026-10-02, D-024):** the URL may
      prefill `subject`, `name` and `email`, all visible and editable, no hidden
      fields, validated and length-limited like typed input. App context (version,
      device) goes through the SDK/API; PHASE-11 documents the URL contract.
- **Prototype/wireframe references:**
  - Chosen direction and Portal sample: `docs/design/mockups/direction-carbon-copy-v2.html`
    (Portal view: contact form with suggestions, confirmation, three sample
    accents including the very light Pixelforge `#F59E0B` and the very dark
    Acme `#0F3D2E`). The other mockups are superseded explorations.
  - Still expected from PHASE-02 (tracked there): the final `docs/BRAND.md`;
    mockups at 360, 768 and 1280 px of product home, ticket view (open, Solved,
    Closed), lost link, a KB article and the uniform error page, each with at
    least four sample products (including the hostile accents above, with and
    without a logo); email mockups in light and dark clients for the
    confirmation, agent reply and closed notice; and the CSS custom-property
    contract for the product theme.
- **Acceptance criteria for design review:**
  - [ ] `docs/BRAND.md` exists and the design follows it; product branding
    leads on every page and email.
  - [ ] "Powered by TechStrap" appears once, as a small footer line on every
    page and email, with no other TechStrap presence; no mascot, joke, carbon
    tint, stamp or window appears anywhere on the portal or in customer email.
  - [ ] Each hostile accent colour (very light Pixelforge `#F59E0B`, very dark
    Acme `#0F3D2E`, saturated red, mid-gray, near-black, blue) produces a
    passing, legible result for buttons (on-accent at least 4.5:1), links and
    accent text (accent-ink at least 4.5:1 on white), headers, status and
    emails, without manual tweaking.
  - [ ] The contact form works end to end at 360 px with suggestions, errors,
    attachment rules and success; the honeypot is invisible and does not affect
    assistive technology or layout.
  - [ ] The confirmation page shows the ticket number prominently.
  - [ ] The customer ticket page is clearly public-only, readable on a phone, and
    handles Open, Pending, Solved and Closed (including the follow-up message).
  - [ ] The invalid-token, unknown-product, and unpublished-article pages are
    visually and verbally identical, and the lost-link response is identical for
    matched and unmatched addresses.
  - [ ] The customer emails (confirmation, agent reply, closed notice, new
    access link) render acceptably in plain text, with images blocked, and in a
    dark-mode client, carry only the product accent and a "Powered by
    TechStrap" line.
  - [ ] SEO checklist applied: unique titles and descriptions, canonical,
    sitemap, noindex on ticket/confirmation/error pages.
  - [ ] Keyboard-only and screen-reader walkthrough of submit, reply and KB
    search passes WCAG 2.2 AA checks.
  - [ ] Rendered desktop and mobile screens reviewed (per _template
    `DESIGN.md` §10), not just CSS reasoning.
  - [ ] No generic template patterns introduced without a reason recorded in
    BRAND.md.
