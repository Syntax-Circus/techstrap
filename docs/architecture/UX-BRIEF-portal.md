# UX Brief: TechStrap Portal (public support site)

**Handoff audience:** Claude Design, UX designer, and implementation team

> This is a **designer handoff**, not an implementation ticket. It describes what
> customers must be able to do, what each page must contain and how it must
> behave; layout and visual language are the designer's to propose.
> **Prerequisite:** `docs/BRAND.md` is produced in
> [PHASE-02 (brand and UX)](PHASE-02-brand-and-ux.md) following the _template
> `DESIGN.md` process. No significant portal UI is built (PHASE-09) until it
> exists. This brief covers the **web pages and the outbound emails**, because
> both are the customer's experience of support.

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
  - **Brand layering:** one portal domain; the **TechStrap brand is the neutral
    frame** (typography, layout, components, footer); **per-product theming is
    layered on top**: product name, logo and an accent colour supplied by an admin
    through `GetPublicProductRequestHandler` branding. The same theming applies to
    outbound emails. The design must work with **arbitrary admin-chosen accent
    colours** (see Contrast rules); it must also work with no logo (name only) and
    with a very long product name.
  - Blazor **static server-side rendering**: pages are fast, indexable and work
    without JavaScript by default. Interactive behaviour (live deflection) is a
    progressive enhancement and must degrade gracefully (open question 3).
  - The portal never touches the database; it calls the API's public and
    token-authorised endpoints through typed clients over `TechStrap.Contracts`.
  - No customer accounts, passwords or sessions. Identity is possession of the
    emailed link (`/t/{token}`).
  - Mobile-first: most customers arrive from a phone or from inside an app's
    "contact support" link. Layouts are designed at phone width first.
  - English only in v1 (an i18n seam exists).
  - No third-party trackers, fonts from CDNs or CAPTCHAs in v1 (CAPTCHA, e.g.
    Turnstile, is only added if spam appears; see open question 9).
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
    identity beyond a first name or display name (Assumption), or other tickets.
- **Persona: Self-servicer** (searches before asking, often via a search engine)
  - **Goals:** land on a KB article from a search engine, read it, decide if it
    solved the issue; browse by category.
  - **Pain points:** thin pages, no way to search, being forced to a form.
  - **Access/permissions:** anonymous; read-only.
- **Persona: In-app user** (arrives from a product's own "Contact support" button)
  - **Goals:** land on the right product's contact page, ideally pre-filled
    (Assumption: link may carry a non-sensitive subject; no PII or secrets in
    query strings).
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
  link is proved only by email ownership; open question 4). Direct visits with no
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
  - **Primary actions:** choose a product (list of active products with logo and
    name) or search all help.
  - **Data/state:** active public products from the API; if exactly one product
    exists, redirect to it (open question 6).
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
    URL decision in open question 7).
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
    (`GetSitemapEntriesRequestHandler`).
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
| Product theming (layout) | `ProductLayout` pair; `ProductThemeStyle` pair (emits CSS custom properties) | `ProductThemeViewModel` (name, logo URL, accent, derived accessible foreground and text-safe variant) | **Yes: `ProductThemeFactory`**, justified by non-trivial colour/contrast derivation shared across pages and email | Layout loads branding once per request; failure to load branding falls back to the neutral TechStrap theme, never a broken page |
| Product home | `ProductHome` pair; `CategoryList` inline | `ProductHomeViewModel` | None | Static SSR; loading not visible; error page on API failure |
| Contact form | `ContactForm` pair; `SubjectSuggestions` pair (enhanced); `FileInput` pair; `HoneypotField` inline | `ContactFormModel` (direct form model with validation attributes mirroring server rules) | None; attachment-rule display strings come from shared constants | Form model owned by the page; enhanced form post preserves values on error; suggestions are an optional interactive island (open question 3) |
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
  - Footer: TechStrap neutral frame with a modest "Powered by TechStrap" link
    (Assumption), plus "Can't find your ticket link?".
  - Hierarchy: help-first. The contact form is prominent but the search box and
    categories come first on the product home; the contact page leads with the
    form and keeps suggestions adjacent to the subject.
  - Product accent shows in: header rule/mark, primary button, links within
    content, focus-compatible highlights. The TechStrap frame supplies layout,
    typography, spacing, neutrals and semantic colours (error/success/warning),
    which are **never** replaced by the product accent.
  - The **ticket number** is always shown in a distinct, copyable treatment.
- **Forms and validation:**
  - Contact fields: name (required), email (required, validated), subject
    (required, length-limited), message (required, length-limited; Assumption:
    a visible character counter near the limit), attachments (optional). Labels
    always visible (no placeholder-as-label); `autocomplete` attributes set
    (`name`, `email`); correct mobile keyboards (`type=email`).
  - Keep the form short; do not ask for product (it is the page's context) or
    information the in-app link already supplies.
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
    file count is a design question.
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

- **Common frame:** neutral TechStrap structure with product header (logo or name),
  one primary call-to-action button plus the same URL as plain text, the ticket
  number and subject in a consistent position, and a short footer (who sent it
  and why; "Replying by email does not reach us yet; use the link above" until
  inbound email ships; no marketing).
- **Subject lines:** consistent and threadable by humans, with the ticket number
  first, e.g. `[ACME-142] We received your request`, `[ACME-142] New reply from
  support`, `[ACME-142] Your ticket was closed` (copy is a design/content
  decision).
- **Email-client reality:** table-based, inline-styled HTML that survives
  Outlook, Gmail and dark-mode inversion; images have alt text; the message is
  fully legible with images blocked and in plain text; width about 600 px, a
  single column; touch-sized CTA button; minimum font size ~16 px for body.
- **Accent colour in email:** apply the same contrast derivation as the web
  (accent only as button background/rule with a computed readable label colour;
  never as small text on white). Dark-mode safe: no pure-white-only logos;
  specify a logo fallback (open question 8).
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
     articles as titled links (when the agent linked any), the agent's display name
     (Assumption: first name or a product-level "Support" identity, a design and
     privacy decision), the CTA "View and reply", and the ticket number. Long
     replies are shown in full (not truncated) because the email may be the only
     thing read.
  3. **Closed notice**: sent when a Solved ticket is auto-closed (or manually
     closed): states it is now closed, shows the number, explains that replying via
     the link will start a **follow-up** ticket, and offers the link to the
     history. Tone: calm, no surprise.
  4. **New access link** (lost-link response): a minimal email with the link(s) to
     their tickets (Assumption: one email listing recent tickets; multiple
     tickets per requester is a design question). Sent only to the address
     entered; subject and body do not confirm anything on the web page, which
     stays uniform.
  Agent-facing alert emails (new ticket, assignment, customer reply) are an
  admin-side notification concern and are **not** in this brief; they may reuse
  the same frame (open question 10).

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
  - **Arbitrary accent rule (hard requirement):** the theming system takes one
    admin-chosen accent colour and must derive, automatically and verifiably:
    (a) a foreground (light or dark) that gives at least 4.5:1 on the accent when
    it is used as a button/background; (b) a text-safe variant (the accent
    darkened or lightened, per light/dark theme) that meets 4.5:1 against the page
    background for link text and 3:1 for non-text UI; (c) a fallback to the
    neutral TechStrap accent when no safe derivation is possible. The accent is
    never the only carrier of meaning (links are underlined or otherwise marked,
    focus rings use a high-contrast neutral not the accent, status uses text).
    The admin branding screen shows a contrast report; the portal still protects
    itself if bad values slip through.
  - Test the design with at least: very light yellow, saturated red, mid-gray,
    near-black, and a brand-like blue; all must pass without redesign.
  - Product logos on unknown backgrounds: logo sits on a defined neutral
    surface with safe padding; support dark-mode variants or a neutral
    backing (open question 8).
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
  (no animated suggestion entry or scroll effects), `prefers-color-scheme`
  (dark theme is a design question), forced-colors mode, and user font-size
  settings. No autoplay media, no carousels.

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

Direction comes from `docs/BRAND.md` (PHASE-02). Two layers are needed:
(1) the **TechStrap neutral frame** and (2) a **product theming layer**. The
designer defines both and demonstrates them with several sample products.

- **Bootstrap 5 components/utilities to prefer:** container/grid with a narrow
  reading column, form controls with validation states, buttons, list groups for
  the conversation, cards sparingly (avoid card-everything per `DESIGN.md`),
  breadcrumbs, pagination, alerts for notices (with text and icons), badges
  for the ticket status, input groups for the search box, `visually-hidden`
  helpers, responsive utilities.
- **SCSS variable overrides:** neutral-frame tokens (fonts, scale, radius,
  spacing, neutrals, semantic colours, focus ring) are compile-time SCSS.
  Product theming is **runtime CSS custom properties** set from branding
  (accent, accent foreground, text-safe accent), consumed by a small set of
  themed utilities; do not generate per-product stylesheets. The same token
  names should drive the email templates' inline styles.
- **Custom SCSS justified only for:** the product theming layer, the ticket
  conversation treatment (you vs support), the ticket-number treatment, the
  suggestions region, the honeypot hiding rule, and KB article typography
  (prose, code, tables, callouts).
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

- **Open design questions:**
  1. How far can product theming go: accent and logo only (current plan), or also
     a hero image, tagline or favicon per product?
  2. Customer-facing status wording and the mapping from New/Open/Pending/
     Solved/Closed; whether to show agent names.
  3. Live deflection requires interactivity on an SSR site: acceptable to add a
     small interactive island (or enhanced navigation plus a tiny script), with
     a no-JS fallback of a "Browse help articles" link?
  4. Should the confirmation page expose the access link directly (convenient) or
     only the number plus "check your email" (stronger proof of email
     ownership)? Current assumption: number only.
  5. How should a requester with several tickets be handled by the lost-link
     email (one link per ticket, or a list)?
  6. Portal root with several products: a product picker, a cross-product search,
     or redirect when there is only one product?
  7. Shared KB articles: canonical URL (a neutral `/kb/...` vs per-product path)
     and how an article is presented when it belongs to more than one product.
  8. Logo handling for dark mode and email: required variants, minimum
     dimensions, fallback when no logo is supplied.
  9. Is the honeypot plus rate limiting enough for launch, or should the design
     reserve space for an optional CAPTCHA challenge (Turnstile)?
  10. Do agent-alert emails share the customer email frame?
  11. Attachment UI limits: number of files, preview thumbnails for images,
      and virus-scan messaging (none planned in v1).
  12. Dark theme for the portal in v1, or light only?
  13. In-app launch: which parameters may an app pass to prefill the form
      (subject, product area) without leaking PII?
- **Prototype/wireframe references:** none yet. Expected from PHASE-02:
  (a) `docs/BRAND.md`; (b) coded mockups at 360, 768 and 1280 px of product
  home, contact form with suggestions, confirmation, ticket view (open, Solved,
  Closed), lost link, a KB article and the uniform error page, each shown with
  at least four sample products and accents (including the hostile accents
  listed above, with and without a logo); (c) email mockups in light and dark
  clients for the confirmation, agent reply and closed notice; (d) the token
  sheet for the SCSS neutral frame and the CSS custom-property contract for the
  product theme.
- **Acceptance criteria for design review:**
  - [ ] `docs/BRAND.md` exists and the neutral-frame plus theme-layer design
    follows it; the logo-removal test was considered for the frame.
  - [ ] Each hostile accent colour produces a passing, legible result for buttons,
    links, headers, status and emails without manual tweaking.
  - [ ] The contact form works end to end at 360 px with suggestions, errors,
    attachment rules and success; the honeypot is invisible and does not affect
    assistive technology or layout.
  - [ ] The confirmation page shows the ticket number prominently.
  - [ ] The customer ticket page is clearly public-only, readable on a phone, and
    handles Open, Pending, Solved and Closed (including the follow-up message).
  - [ ] The invalid-token, unknown-product, and unpublished-article pages are
    visually and verbally identical, and the lost-link response is identical for
    matched and unmatched addresses.
  - [ ] The three emails (confirmation, agent reply, closed notice) plus the new
    access link render acceptably in plain text, with images blocked, and in a
    dark-mode client.
  - [ ] SEO checklist applied: unique titles and descriptions, canonical,
    sitemap, noindex on ticket/confirmation/error pages.
  - [ ] Keyboard-only and screen-reader walkthrough of submit, reply and KB
    search passes WCAG 2.2 AA checks.
  - [ ] Rendered desktop and mobile screens reviewed (per _template
    `DESIGN.md` §10), not just CSS reasoning.
  - [ ] No generic template patterns introduced without a reason recorded in
    BRAND.md.
