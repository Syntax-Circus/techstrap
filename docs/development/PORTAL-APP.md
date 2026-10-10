# Portal app (customers)

`TechStrap.Portal` is the public site customers use: a product's own help and support pages, with the product's name, logo and accent. It never touches the database: every page asks the TechStrap API,
anonymously. This page is for people who run it and people who extend it. How agents work the tickets customers send is in [ADMIN-APP.md](ADMIN-APP.md); the decisions behind the Portal are D-045 in the
[decision log](../architecture/04-DECISION-LOG.md) (with its 2026-10-06 addenda for 09b, 09c and 09d) and the spec is [PHASE-09](../architecture/PHASE-09-public-portal.md).

PHASE-09 is delivered in four pull requests. **09a** is the foundation: the settings, the API client, the per-product theme and shell, the product home, the root page, the ticket-page headers, robots.txt, log
redaction and the architecture rules. **09b** adds the customer flows: the contact form with its suggestions and received page, the ticket page with replies and the Closed follow-up, the attachment pass-through
and the lost-link page. **09c** (this page describes all three) adds the help center: the knowledge base home, category, search and article pages, the SEO head and structured data, output caching and the sitemap.
**09d** is the polish pass: the double-send guard, the form helpers (the sending state, the copy button and the counter), the styling, the accessibility and the no-JS pass, and the hardening of the start-failure and
uniform-404 tests. PHASE-09 is complete pending merge: what is still the owner's (the axe run, Lighthouse, the JavaScript-off walk, the screenshots and the compose smoke) is the checklist under
[Manual checks](#manual-checks-owner-before-merging-09d). The routes of all of them are in `PortalRoutes`.

## Run it locally

You need the API running (the root README starts it with Docker Compose, or run `src/TechStrap.Api`). With the Development seed (`TECHSTRAP_SEED_DEV_DATA=true`, see
[DEV-DATA.md](DEV-DATA.md)) the products `orbitly` and `paperplane` exist.

```bash
cp src/TechStrap.Portal/.env.example src/TechStrap.Portal/.env.local     # then edit the values below
dotnet run --project src/TechStrap.Portal --urls http://localhost:8082
```

`.env.local` is read in Development only and is git-ignored. Open `http://localhost:8082/p/paperplane` for the themed product home, `http://localhost:8082/p/paperplane/contact` for the contact form (try
`?subject=Printer%20jam&name=Jane%20Doe&email=jane%40example.com` for the prefill) and `http://localhost:8082/p/nope` for the not-found page. A ticket link from an email is `/t/{token}`. With Docker Compose the
Portal listens on `http://127.0.0.1:8082` and compose sets `Api__BaseUrl` and `TECHSTRAP_PORTAL_PUBLIC_URL` for it.

### Configuration

The Portal refuses to start with a message that names the missing or malformed key. Every key is in `src/TechStrap.Portal/appsettings.json` with its default (D-043). 09b adds no key.

| Key | Required | Default | Meaning |
| --- | --- | --- | --- |
| `API__BASEURL` (`Api:BaseUrl`) | Yes | blank (the compose files set `http://api/`) | The address of the TechStrap API, absolute http or https |
| `TECHSTRAP_PORTAL_PUBLIC_URL` | Outside Development | blank | The Portal's public address as customers see it, absolute http or https, no query or fragment. The base of canonical URLs and robots.txt's sitemap line (`Seo:BaseUrl` is derived from it); the same value as the Api's key |
| `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` | No | blank | A product key. When set, `/` redirects (302) to `/p/<key>`; blank shows the neutral page (or the landing page when `TECHSTRAP_PORTAL_LANDING=Products`) |
| `TECHSTRAP_PORTAL_LANDING` | No | `Neutral` | `Neutral` or `Products` (D-052). `Products` lists the active products whose landing flag is on, as cards (name, uploaded or linked logo, tagline) on the default host's `/`; a hosted product's card links to its own host. An unreachable Api shows the neutral copy. Not allowed with `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` (the Portal stops at start). A product host's `/` is always that product's home |
| `TECHSTRAP_PORTAL_SHOW_POWERED_BY` | No | `true` | `false` hides the "Powered by TechStrap" line on every page. Any other value than `true` or `false` stops the start (D-024) |
| `CANONICALHOST__CANONICALHOST` | No | blank | The host to redirect legacy hosts to; blank turns the redirect off |
| `CANONICALHOST__LEGACYHOSTS__0` ... | No | none | The hosts that are redirected. Only these are; any other host is left alone |
| `CANONICALHOST__FORCEHTTPS`, `CANONICALHOST__PERMANENT` | No | `false`, `true` | https target; 301 (true) or 302 |
| `DATAPROTECTION__KEYRINGPATH`, `TRUSTEDPROXY__*`, `SECURITYHEADERS__*`, `SENTRY__*`, `OPENTELEMETRY__*`, `SERILOG__*` | No | see `appsettings.json` | Shared with the other hosts. In production keep `DATAPROTECTION__KEYRINGPATH` on a volume (both compose files do): the "received" page's `?ref=` is protected with it, and without a persisted ring a restart makes every pending reference show the generic confirmation (and invalidates every antiforgery token in flight) |

## How a page is served

Every page is static server-side rendering: there is no render mode, no circuit and no SignalR (`PortalRules.InteractivityViolations` fails the build of the architecture tests if one appears).

- **The product scope.** A page under `/p/{key}` derives from `ProductPageBase`. It loads the product once through `IPublicProductClient` and puts the result in the request's `ProductScope`; `PortalLayout`
  then wraps the page in the product's accent (`AccentScope`), a header (logo and name) and a footer. The scope is per request, so the not-found and error pages, which the host renders in a fresh scope, are
  never branded. The ticket page has no key in its address: it loads the ticket first and asks for the product named by the ticket's `ProductKey`.
- **One answer for "no such product".** An unknown key, an inactive product and a key that is not a slug (`^[a-z0-9]+(-[a-z0-9]+)*$`, at most 40 characters) all call `NavigationManager.NotFound()`, so the visitor
  gets the same neutral 404 page as for an unknown route; a malformed key never reaches the API. `NeutralPagesGuardTests` pins it.
- **When the API fails** the page shows "This page could not be loaded." with a Try again link and a 503 (429 when the API is rate limiting), never a stack trace.
- **Branding is untrusted.** `ProductThemeViewModel` keeps the accent only if `ProductAccent.TryDerive` accepts it and the logo only if it is https (or http to `localhost` or `127.0.0.1` in Development, the same
  rule as the CSP's `img-src`). The product name is always encoded.
- **Copy** lives in `ShellCopy`, `ProblemCopy`, `FormCopy`, `ContactCopy`, `LostLinkCopy`, `TicketCopy` and `KbCopy`; routes in `PortalRoutes` (a page declares `@attribute [Route(PortalRoutes.XTemplate)]` and builds every link with a builder).

### Themes and skins (D-053)

The look of every page is one resolved skin, written onto the single `ts-accent-scope` wrapper as custom properties and preset attributes. Nothing is raw CSS and nothing is a `<style>` element.

- **One resolver.** `SkinResolver` (Contracts) merges Classic, then the deployment default pack, then the product's pack, then the product's token overrides, and derives the on-colors and inks. The Portal, the Api validation and the email renderer all call it; `PortalSkinFactory` (Portal `Products/`) is the Portal's only caller and `PortalLayout` and the landing cards use its result.
- **Classic baseline: emit only what differs.** `SkinCss.Properties` writes a variable only when its resolved value differs from Classic's, so a product with no skin renders byte-identical HTML (a golden test, `An_unskinned_product_page_is_byte_identical`, guards it). Every SCSS rule reads the variable with today's value as the fallback. The accent trio (`--ts-accent`, `--ts-on-accent`, `--ts-accent-ink`) comes first and is emitted when the brand is explicit (a product accent or a skin brand) or differs from Classic's brand, so a dark pack keeps readable links.
- **Variables.** `--p-bg`, `--ts-surface`, `--p-ink`, `--p-ink2` (muted), `--p-line` (border), `--ts-chrome` and `--ts-on-chrome`, `--ts-focus`, `--ts-radius`, `--ts-border-w`, `--ts-font-heading`, `--ts-font-body`, plus the accent trio. Values are validated hex, `rem` or `px` numbers, or a font stack built from the closed `SkinFonts` list; a value that fails the grammar never reaches `style=` (`Hostile_skin_values_never_reach_the_page`).
- **Presets.** `data-ts-shadow` (`soft`, `hard`), `data-ts-button` (`bevel`, `outline`), `data-ts-header` (`solid`, `band`) and `data-ts-scheme="dark"` are closed constants emitted only when not Classic's. The rules are attribute selectors in `Styles/_presets.scss` (imported after the layout and before `_a11y`), so no composed class name has to exist. A solid header uses the on-chrome color for the focus ring and the accent so the ring is visible on the fill; forced-colors and print drop the header's `border-image`.
- **Dark scheme.** `data-ts-scheme="dark"` follows the resolved page background (its contrast against white is greater than against black), not the pack: a light page on Midnight is a light page. The dark block redefines the soft and semantic surfaces (error, warning, success) and the select arrow. `color-scheme: dark` is deliberately not used (`StyleBuildTests` forbids it), so native scrollbars and widgets stay light on Midnight. A light page on a dark pack must also set `muted` and `focus`, because the resolver reverts both members of a failing contrast pair.
- **The default pack.** `DefaultPackProvider` (singleton, over `ISiteSettingsClient`, `GET api/public/site`) keeps the key for 60 seconds and refreshes in the background. The first read waits at most about 2 seconds, then answers Classic (a stale placeholder) while the read finishes; a real read always wins. A failed read keeps the last good value and is retried at most every 10 seconds. Neutral pages (root, 404, error) use the default pack only, never a product, so their body is identical for every address (D-045).
- **Landing cards** each get their own scope, resolved from the summary's `Skin` and accent.
- **Fonts.** The five families come from libman (fontsource) through `_fonts.scss`; no binary is tracked and nothing comes from a CDN.

### Forms and uploads

The contact form, the reply form and the lost-link form are plain static-SSR forms: `<form method="post" @formname=...>` with `<AntiforgeryToken />`, a `[SupplyParameterFromForm]` model and a redirect after the post, so
nothing needs script and a refresh never sends twice.

- **Antiforgery** is enforced by the framework: a post without a valid token is a 400 and no handler runs.
- **The model.** A class with settable properties and a nullable `IReadOnlyList<IBrowserFile>? Files` binds `<input type="file" multiple name="Form.Files">`; a form with no file chosen binds no files at all. The property
  may not have an initializer (analyzer BL0008): the page sets it in `OnInitializedAsync` when the framework left it null. Always read a file with `OpenReadStream(IntakeLimits.MaxFileBytes)`: the framework's
  default stops at 512,000 bytes.
- **The size limit.** `[RequestSizeLimit(IntakeLimits.FormBodyBytes)]` on the page (27,262,976 bytes, the API's own limit) is applied by endpoint routing before the antiforgery check reads the form, so an
  oversized body is never buffered. `RequestTooLargeMiddleware` answers a declared length over the limit with a plain 413 before the framework turns the failed read into a 400 about a token; a chunked body over
  the limit is still that 400. `TestServer` has no limit feature, so these tests use `UseKestrel(0)`.
  Deployment: set a request body limit and a rate limit for the Portal's form POSTs at the reverse proxy, because the Portal buffers a form of up to about 27 MB before the antiforgery check runs (see [DEPLOYMENT.md](../self-hosting/DEPLOYMENT.md)).
- **Checks before the API.** `ContactFormValidator`, `ReplyFormValidator`, `EmailRules` and `AttachmentRules` use the `IntakeLimits` constants (name 100, email 320, subject 200, body 100,000, 5 files, 10 MB each,
  25 MB in all, ten extensions), the same constants the inputs' `maxlength` attributes use; `IntakeLimitsParityTests` keeps them equal to the Domain's. The API checks again and is the authority. An error is a
  `FormError` (field, code, sentence): the codes are the API's (`email-invalid`, `attachments-too-many`, ...) and the sentences are `FormCopy`'s, so the API's own text is never shown. `FormFailure` decides
  what the page shows for a refused call: field errors for a 400, the attachment error for a 413 or 415, a calm notice and a status for a 429 (429), `reply-conflict` (409) or an outage (503), and the uniform 404
  when the product or ticket is gone. What the visitor typed is kept; files are not (a browser never keeps them), and the form says so.
- **File names** come from the browser, so every file name is cleaned (`AttachmentFileName`: last path segment, no quotes, control or format characters) before it is shown or sent.
- **The honeypot** (`Form.Website`) is hidden by a style sheet class, out of the tab order, `aria-hidden` and `autocomplete="off"`. A filled one is sent to the API like any other value; the API validates the
  product and answers a believable 201 without creating a ticket, so a bot gets the real response.
- **The prefill** `?subject=&name=&email=` fills the same three visible, editable inputs on the contact page (nothing hidden, nothing auto-submitted, other parameters ignored and never echoed) and is judged on post
  exactly like typed text. The form posts to the page's own address without the query. The `name`, `email`, `subject`, `ref` and `q` query values are masked in logs and Sentry.
- **The received page** (`/p/{key}/contact/received?ref=`) shows the ticket number from a data-protected, 10-minute reference (`ReceivedReference`, purpose `TechStrap.Portal.ContactReceived.v1`). A missing, expired, tampered or
  foreign reference shows the generic confirmation, never an error. It carries the number only.
- **A required field is said in words.** One sentence above each form says what is required (`ContactCopy.RequiredNote`, `LostLinkCopy.RequiredNote`, `TicketCopy.ReplyNote`); the labels stay as they are.

### The double-send guard

A double click on a form must not send twice (P09-T09). Each of the three forms renders `<FormGuard Name="Form.SubmitId" />` (`Reply.SubmitId` on the ticket page) right after the antiforgery field: a hidden input with a
fresh 22-character random `SubmitId` (`SubmitIds.New`: 128 bits from `RandomNumberGenerator`), made anew on every render, so a form shown again after an error carries a new id, not the posted one. The handler
claims the id with the singleton `SubmitGuard` after validation passes and before the API call.

- **What a repeat does.** The first post with an id claims it under a lock and does the write; a repeat of the id never writes. It waits for the first's answer when that is still running or reads the stored one, and redirects
  to the same place (a reply: the same ticket page, or the follow-up's page; contact: the received page with its reference; lost link: the sent page).
- **The claimed write runs on its own token** with the guard's own deadline of 295 seconds (`SubmitGuard.WriteTimeout`: `ApiClientRegistration.WriteTimeoutSeconds`, the write client's 300 seconds sized for 25 MB uploads, less 5 seconds), never on `RequestAborted`: a real double click makes the browser abort the first POST while the API may already have the ticket. The guard's deadline is deliberately the shorter of the two, so it always fires first and a timeout is always "unknown" (never the client's own timeout racing it and releasing the claim for an upload that then commits); `SubmitGuardTests` pins it below the client's. The first
  request waits for its own write without its abort token, so the files it is reading stay valid (a precaution, not reproduced).
- **Unknown is not retried.** A write that times out or throws leaves the claim as unknown: a repeat goes to the fallback (a reply: the ticket page; contact: the received page with no reference, which shows its
  generic confirmation; lost link: the sent page) and never sends. The first request shows its own calm notice with a 503 (`FormCopy.Unknown`, "We could not confirm it was sent. Check your email before sending again.": not "try again", because the message may have arrived).
- **A failure releases the claim**, so a retry sends (429, 409, 503 and anything else, a 5xx or a transport failure too: accepted, D-045); a repeat that was waiting gets the same failure and does not write.
- **A missing or malformed id means no guard**: the post goes through as it did before (old pages and the test kits keep working), and a full cache leaves a new post unguarded rather than refused.
- **A resend after the back button is a new message.** After Back the visitor always has a fresh id: either the page is fetched again (`no-store`, a server-rendered id, and the `FormGuard` input is `autocomplete="off"` so the browser does not restore the old one), or it comes from the back/forward cache and `portal-forms.js` writes a new id into every form on that `pageshow`. So any resend after Back, edited or identical, is sent (confirmed in Edge: two ticket POSTs). The guard covers a double click and a refresh that re-posts the same id. The key also hashes a digest of what was posted (`SubmitContent.Digest`: the trimmed text fields, and each attachment's name and size in order), so an edited re-post that reuses an id is a new message too. Only a hash is kept; the text is never stored or logged.
- **Scope and secrecy.** The key is a hash of the form name, the product key (or the ticket's access token), the id and the content digest, so an id cannot be used on another form, product or ticket, and the cache never holds a token or a word the visitor wrote.
  `SubmitGuard` has its own `MemoryCache` with a cap of 10,000 claims (the shared `IMemoryCache` holds the sitemap and has no size), a claim lives 6 minutes (`SubmitGuard.Lifetime`: longer than the 295-second write deadline, shorter than `ReceivedReference.Lifetime`), the stored
  target is memory-only and the class takes no logger. For a follow-up reply the target holds the new ticket's access token; that is accepted for the 6 minutes.
- **Limits.** The guard is per instance and is lost on a restart (like the sitemap cache), and the API still has no idempotency key for `/api/customer` or the public ticket route. The tests are
  `SubmitGuardTests` (the class on its own, with a gate that holds the write) and `DoubleSendHostTests` (the three forms through the host).

### Suggestions beside the subject

The contact page puts `<ts-kb-suggestions field="subject" src="/p/{key}/suggest">` after the subject field, with a plain link to the help search inside it (shown only without script). The document shell (`App.razor`)
loads `wwwroot/js/kb-suggestions.js` as `<script type="module" src=...>` (no CSP change), not the page: Blazor's enhanced navigation does not run a script that arrives with swapped content, so a script in the contact page's own
body never ran for a visitor who got there by a click (09d). The module defines the element: a 300 ms debounce, at least 3 characters, one request at a time (a newer one aborts the older
and its answer is dropped), results written with `textContent` and links built only from root-relative paths, hide-on-error, and clean-up in `disconnectedCallback`. It never parses text as markup, and a test fails
if the file ever contains `innerHTML`. `GET /p/{key}/suggest?q=` (`SuggestEndpoint`) is the Portal-hosted adapter: it asks `IPublicKbClient.SearchAsync` (a read, forwarding the visitor's address), cuts the text at 200
characters, returns at most 5 `{title, snippet, href}` items with the links built by `PortalRoutes.KbArticle`, passes the API's 429 through and answers an empty list for a blank text or any other failure.
The module is tested with `node --test` (`tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs`, run by `scripts/tests/PortalScripts.Tests.ps1`, skipped without node).

### Form helpers (`portal-forms.js`)

`wwwroot/js/portal-forms.js` is the second module the document shell loads (once). Every page works without it. It defines two custom elements and two document-level listeners, so it works on whatever
Blazor's enhanced navigation swaps in (a listener on the document survives; the browser calls `connectedCallback` for an inserted element; a module in a page body would not run). It keeps its state in a `WeakMap`,
never in an attribute of the markup, because the swap rewrites the attributes of an element it keeps. A same-page enhanced navigation (a form post that shows errors, a link to the same route) also keeps `ts-copy-text`, `ts-char-count` and
`ts-kb-suggestions` but replaces their content with the server's (empty, or the no-script fallback link), and `connectedCallback` does not run again; so each element watches its own children with a `MutationObserver` and builds them again when they are gone (`data-permanent`
was rejected: it would also keep the old page's field, limit and suggest path through a navigation to another product's page). Proven in Edge headless: after `Blazor.navigateTo` to the same address the counter, the suggestions and the copy button all still work in the same document.

- **The sending state.** A form with `data-sending-label` (`FormCopy.Sending`, "Sending" and an ellipsis written as `\u2026`) gets its submit button disabled and relabeled when it is submitted, so a double click cannot post twice
  (the server's one-time id is the real guard); a second submit of a form that is sending is canceled; the button comes back on `pageshow` after the back button and after a minute (a post the visitor stopped); a submit that something else already canceled leaves the button alone. On a `pageshow` from the cache every `input[name$=".SubmitId"]` also gets a fresh id (see "A resend after the back button").
- **`<ts-copy-text target="ticket-number" data-label data-copied data-failed>`** on the received page renders a button that copies the number to the clipboard; when the browser refuses it selects the number, so Ctrl+C
  works, and says so. Without script the number is still there to select (`.ts-ticket-number` is `user-select: all`).
- **`<ts-char-count for="body" data-limit data-template data-over>`** under the long text fields shows "{0} of {1} characters used" from 80 percent of the limit, and how far over it is, bold and underlined, above it. A line
  break counts as two characters, because the browser posts CR LF and the server counts what it receives, while the textarea's own `maxlength` counts one. The screen reader hears the count once typing pauses.
- **Words and safety.** Every word comes from a `*Copy` constant through a data attribute; text goes on the page with `textContent` only (a node test fails if the file contains `innerHTML`, `eval`, a request or
  a non-ASCII character); there is no inline script and no `on*` attribute, so the CSP is unchanged.
- **Tests.** `tests/TechStrap.Portal.Tests/js/portal-forms.test.mjs` (run by `PortalScripts.Tests.ps1`) and `PortalFormsHostTests` (the shell loads both modules once and no page body has a script, the markup and the words).

### Accessibility, layout and the base address

- **Widths.** One token for the reading column (`--ts-reading-width`, 40rem: a form, the conversation, an article, the confirmation; the page puts its content in `.ts-reading`) and one for the wide container
  (`--ts-wide-width`, 64rem: the header, the footer, search, the categories, the product home). One column below 768px; the category cards are two across from 768px and three from 1200px.
- **Colors.** `--p-error`, `--p-success` and `--p-warn` and their grounds (BRAND.md, "Portal tokens") carry errors, confirmations and notices; a state is never color alone (the summary has a heading and a list, a field in
  error has text and a 2px border, the counter over its limit is bold and underlined). A control's edge is `--p-ink2`, not the decorative `--p-line`. Focus is a 3px ink ring with an accent halo; in forced colors the ring
  stays and the halo is dropped; reduced motion switches every animation and transition off, and smooth scrolling is off in the build (`$enable-smooth-scroll: false`).
- **Landmarks and headings.** A product page starts with a skip link (`.ts-skip-link`, off the screen until it has focus, in ink on the page so no accent can hide it) and `main` is `id="main" tabindex="-1"`. The header holds a
  named `nav` (the product, the help center and contact) and the footer a named `nav`; the "Powered by" footer is the one contentinfo. A neutral page (the root, not-found, error) has none of the product frame, so every
  neutral 404 is still the same bytes. Every page has exactly one `h1` (`HeadingHostTests`); the search page's is "Search the help center"; an `h1` inside an article or a message body, which the API's sanitizer allows, is
  shown as an `h2` (`BodyHeadings`, the only change the Portal makes to those bodies).
- **The `<base href="/">` rule.** The document's `base` is `/` (the stylesheet, the favicon and `blazor.web.js` are relative to it, and Blazor reads it), so a bare `href="#email"` is the home page. A link to a place on the
  current page is written by `PageLinks.ToFragment` as the root-relative path plus the fragment (the error summary's field links, the skip link and the "jump to your reply" link on the ticket page), which the browser
  treats as a jump, and carries `data-enhance-nav="false"`: Blazor takes a click on an in-page link, scrolls and leaves the focus on the link, while the browser moves the focus to the field. The query string is kept only for
  the parameters the page itself reads (the contact prefill, `ref`, `sent`, `q` and `page`), in the address bar's own raw spelling (a form's `paper+jam` stays `paper+jam`), so a jump is not a reload that loses what the page shows, and an extra parameter is neither echoed nor kept in a copy the output
  cache stores. `ErrorSummaryLinkHostTests` and `LayoutLandmarkHostTests` resolve each link against the base the way a browser does.
- **Styles** are three partials after `_components`: `_layout.scss` (the widths and the breakpoints) and `_a11y.scss` (the semantic colors, the targets of at least 44px, forced colors, reduced motion). Every `ts-*` class the
  markup uses has a rule (`ResponsiveStyleTests` scans the markup); `TokenContrastTests` checks every text pair of the Portal's tokens against WCAG AA in the compiled CSS.

### The help center

Four pages, each on `ProductPageBase`, each static SSR with plain links and a GET form, so everything works without script:

- `/p/{key}/kb` (`KbHome`) lists the categories the product can see with their article counts and descriptions (`IPublicKbClient.ListCategoriesAsync`); a product with no article shows the empty state.
- `/p/{key}/kb/{category}` (`KbCategory`) is a page of the category's published articles, newest update first, 10 a page (`ListCategoryArticlesAsync`). The pager (`Pager`) is `?page=n` links: page one has no query.
- `/p/{key}/kb/search?q=&page=` (`KbSearch`) is a GET form (`KbSearchBox`). An empty text shows a prompt and makes no call; no result shows a link to contact support; a snippet is plain text; a text is cut at 200
  characters; a page with a text is `noindex`; paging links keep the escaped text.
- `/p/{key}/kb/{category}/{slug}` (`KbArticle`) shows the article: breadcrumbs, the title, the day it changed, the body and a "Still need help?" link. **`KbArticleBody` is the second and last place the Portal turns
  text into markup** (`PortalRules.MarkupStringSites` lists exactly it and `CustomerMessageBody`): the API renders the Markdown and sanitizes the HTML (D-044), and the Portal passes it on byte for byte
  (`KbArticleHostTests` compares it with the API's string), except that an `h1` in the body is shown as an `h2` (`BodyHeadings`), because the page's own title is its one `h1`. A plain-http image in an article is blocked by the CSP's `img-src 'self' https: data:` in Production; that is the intended posture.

Everything an agent wrote or a visitor typed (a name, a title, a summary, a snippet, a search text) is plain text shown by Razor, which encodes it. `/p/{key}/kb/search/{x}` matches the article route with the category
`search`, which the API reserves, so it is a 404.

**One answer for "not there".** An unknown or inactive product, a category that is unknown, another product's or empty, a page past the end, and an article that is a draft, archived, another product's, in the wrong
category or unknown are all the neutral 404, byte for byte the page an unknown route gets, with none of the product's theme (`ProductPageBase.Fail` forgets the product first). A key or slug that is not a slug is
refused by `IPublicKbClient` without a call (`KbSlugShape`, like `ProductKeyShape`). When the API fails the page shows the calm message with a 503 (429 when rate limiting), in the product's theme.
**The `page` and `q` values are read as text** (`KbPaging.Parse`, `KbSearchText.Clean`): the framework's binding to a number answers 500 for `?page=abc`.

### SEO and structured data

Each page sets its head with `SeoHead` (`SyntaxCircus.Blazor.Seo`): a unique title ("{page} - {product} Help Center", the product being the site because `Seo:SiteName` is global), a description, the canonical
address (on the default host built from `TECHSTRAP_PORTAL_PUBLIC_URL`, on a product host from the product's stored `PortalHost`, through `PortalLinks.Absolute`; the raw Host header is only a lookup key into the host map and never appears in a URL; a page of a category names its own page, and a shared article is canonical under the product the visitor is on), Open Graph (the product's logo
when it has an acceptable one, else `/icon-512.png`, never the bare site address) and `NoIndex` for a search with a text. The description of an article is its summary, else the first sentence of the body as plain
text (`KbPlainText`), else the title and product. The article page also writes a `BreadcrumbList` and an `Article` as JSON-LD.

**JSON-LD must go through `JsonLdText`.** `SyntaxCircus.Blazor.Seo` 0.1.4's `JsonLd` writes its JSON through a markup string with an encoder that leaves `<`, `>` and `&` alone, so a `</script>` in an article title
ends the block and injects markup. Escaping first would be escaped twice, so every string of the Portal's own structured-data records (`BreadcrumbListLd`, `ArticleSchema`) is a `JsonLdText`, whose converter writes
`<`, `>`, `&`, the apostrophe, `+` and every non-ASCII character as `\uXXXX`; the JSON reads back as the original. A Razor file cannot hold the text `</script` in a string (the Razor parser reads a tag), so the
hostile texts live in C# tests. The block is data, not script, so the CSP does not change. Do not use the package's schema records for text a visitor or an agent wrote.

### Caching and the sitemap

The help-center home, a category page and an article page are kept for 60 seconds by the framework's output cache (`AddPortalOutputCache`, one base policy with the path predicate `PortalCachePaths.IsCacheable`; no
attribute on a page). The key varies by the `page` query value and by host (`SetVaryByHost(true)`, D-050: the same path on two hosts is two entries; the key holds the raw Host value, so unknown hosts each get entries, see Known gaps), but not by the rest of the query string (the framework's default key holds the whole query string, so `?utm=1`, `?utm=2` ... would fill the store); a `page`
value is kept only on a category page and only from two up (the home and an article ignore `page`; `?page=1` is the page with no value); any other value is answered but never kept. A category page is kept only when its raw `Request.QueryString` is empty or literally `?page=` and 2 to 9999: `?PAGE=2`, `?pa%67e=2`, `?page=%32` and any request with another parameter are answered but never stored, because the page's links repeat the address bar's own spelling and a stored copy would hand one visitor's spelling to the next. Only all-lowercase paths are kept (a path with an upper-case letter is never stored or looked up), so a capitalized path can never be answered from the lower-case entry: `/p/ACME/kb` stays the neutral 404 and `/p/acme/KB` is a 200 that is never stored. The search page, the form pages, `/p/{key}` itself, `/t/*`, the suggest adapter, `/not-found`, the sitemap and every answer that is not a 200 are never kept,
and the output cache never stores a response that sets a cookie (no help-center page does). A delivered KB page tells browsers `Cache-Control: public, max-age=60` (`PathHeaderRule.SetOnSuccess` in Hosting: a 404,
429 or 503 never gets it); the search page is `no-store`. `UsePortalOutputCache` goes after the error pages and before the endpoints (`ProgramOrderTests` pins the order), so the shared security headers and the per-path
rules are applied to a cached answer too, and it sets the request's own `X-Correlation-Id` again when the response starts, because a stored copy replays the first request's.

`/sitemap.xml` is `MapSeoSitemap` with a provider (`PortalSitemap`). A build (`PortalSitemapBuilder`) asks for the active products (`IPublicProductClient.ListAsync`), then for each product's published articles, and
lists the root page (only when no default product is configured: `/` is then a redirect), each product's home, its help center home, its categories and its articles; a shared article is listed under each product. Every
address is absolute (from the public URL, or from the product's host) and at most 50,000 are listed, the root page included. The list is per host (D-050): a product host lists only its own product, with clean paths on `https://{host}`; the default host lists only the products without a host plus the root entry. `PortalSitemapCache` is keyed by the host (`Context.Host ?? "default"`) and keeps each result for 15 minutes in an `IMemoryCache` with single-flight (twenty concurrent
requests make one build), builds on its own task with its own cancellation token (a crawler that goes away stops waiting but cannot cancel the build), remembers a failed build for one minute while the last good sitemap is
served, and fails the request (the 500 page) only when there has never been a good one. While a rebuild runs, a request other than the one that started it gets the last good sitemap at once. The build's calls carry the address of the visitor whose request started it (a stand-in `HttpContext`; known gap below).

### The ticket page and attachments

`/t/{token}` (`Ticket.razor`) parses the token first (`TicketToken.TryParse`; a malformed one is the uniform 404 and the API is never asked), loads the ticket through `ICustomerTicketClient`, then the ticket's
product theme (an inactive or unknown product is the neutral theme, never a 404). `CustomerTicketPresenter` builds the view model: the status in the customer's words (New "Received", Open "In progress",
Pending "Waiting for your reply", Solved "Solved" with a note that a reply reopens it, Closed "Closed" with the note that a reply starts a follow-up), "You" for the customer's messages, the API's resolved name
for an agent exactly as it came, a neutral label for the system. **`CustomerMessageBody` is one of the two places the Portal turns text into markup** (`PortalRules.MarkupStringSites`; the other is `KbArticleBody`): the API sanitizes the message HTML.
Every other string, the subject, the names and the file names included, is encoded by Razor.

A reply is a multipart post through the write client (never retried) with the token as the `X-Ticket-Token` header. On success the page redirects to itself; when the API started a follow-up (a reply to a Closed
ticket) the new token is read from the last segment of `FollowUpViewUrl` by `FollowUpLink` and the redirect is `PortalRoutes.Ticket(newToken)`, so a visitor is never sent to another host; a link that cannot
be read gives a generic "we started a follow-up" message and no redirect.

`GET /t/{token}/attachments/{id}` (`AttachmentPassThrough`, exempt like D-017) opens `GET api/customer/attachments/{id}` with `ApiConnection.OpenStreamAsync` (response headers only, the body is streamed, never
buffered) and always sends a download: `Content-Disposition: attachment` with a cleaned name, `X-Content-Type-Options: nosniff`, and, from the `/t` header rules, `no-store`, `no-referrer`, `noindex` and the
sandbox CSP. A bad token, an id that is not a GUID (default `D` format) and an upstream 404 are the same empty 404, which the host turns into the one neutral page; the API's 429 is a 429; anything else is a 502.

### The lost-link page

`/p/{key}/lost-link` asks `ICustomerTicketClient.RequestAccessLinkAsync` (a write, never retried, no token) and redirects to `?sent=1`, which shows one sentence. The Portal looks at nothing the API answered beyond
success, so every well-formed address gets a byte-identical response (`LostLinkHostTests` compares them; the timing difference D-038 accepts is out of scope). A malformed address is a field error and the API is not
asked; a 429 is a calm notice.

### Talking to the API

`ApiConnection` (internal, `Clients/`) is the only place the Portal uses HTTP. It sends reads through a client that retries transport errors, 408 and 502 to 504 (twice, honoring `Retry-After` up to
2 seconds, no circuit breaker) and writes through a client that never retries. Both forward the visitor's address in `X-Forwarded-For` (`AddForwardedClientIp`; the API trusts it only from the compose subnet,
D-019) and have no logging handlers. `ProblemMapping` turns every answer into a `Result`: 400 keeps the API's field codes, 404 is one not-found whatever the API called it, 409 is `reply-conflict`, 413 and 415 are the
attachment errors, 429 is rate limited, any 5xx or transport error is `api-unavailable`, with fixed sentences from `ProblemCopy`. A call made as a ticket's customer takes a `TicketToken` (43 base64url characters;
it prints as `[token]`), which becomes the `X-Ticket-Token` header of that request only. The typed clients are `IPublicProductClient`, `IPublicTicketClient` (multipart intake), `ICustomerTicketClient` (view, reply,
lost link, attachment stream) and `IPublicKbClient` (paged search, categories, a category's articles, the article and the sitemap entries; `IPublicProductClient` also lists the active products for the sitemap). `MultipartForm` builds the bodies (text fields first, one `Attachments` part per file, the file streams owned by the request).

### Product hosts

A product with a `PortalHost` (set in the Admin, D-050) is also served at `https://{host}/` with clean paths (`/`, `/contact`, `/kb/...`); the default host keeps `/p/{key}/...`. The Portal needs no new setting.

- **`ProductHostMiddleware`** (`Hosting/`, registered by `UseProductHosts()` directly after `UsePortalSeo()` and before `UseTechStrapErrorPages()`; it calls `UseRouting()` so endpoint selection follows the rewrite) lowercases the request host and looks it up in the host map. The Host header is only a lookup key: every URL, redirect target and canonical is built from a stored `PortalHost` or `TECHSTRAP_PORTAL_PUBLIC_URL`. An unknown host is the default host. A host that can never be a product host (a single label such as `localhost`, or an IP literal) is neither looked up nor redirected.
- **The map** (`ProductHostMap`, singleton) is an immutable snapshot built from `IPublicProductClient.ListAsync`, driven by `TimeProvider`: a 60 s TTL, stale-while-revalidate (an expired snapshot is served while at most one background reload runs; only a cold start waits) and a miss refresh at most once per 10 s, which bounds Api calls from unknown hosts. A read is cut off after 30 s and a failed read keeps the previous map; until the first successful read (an Api that is down at cold start) every product host behaves as the default host, and a failed read is retried at most once per 10 s. An entry whose host equals the default host is ignored and logged by key. `ProductHostContext` (`Key`, `Host`) is scoped per request and survives the re-execution of the 404 and error pages.
- **The rewrite table** (product host only): `/` becomes `/p/{key}`; `/contact`, `/contact/received`, `/lost-link`, `/kb...` and `/suggest` become `/p/{key}/...`. Passed through untouched, by segment: `/t`, `/_framework`, `/_blazor`, `/_content`, `/css`, `/js`, `/img`, `/favicon*`, `/sitemap.xml`, `/robots.txt`, `/health`, `/not-found`, `/error`, `/_styleguide`. Any other clean path on a product host passes through unchanged: a static asset (`/icon-512.png`, `/css/...`) is served, anything else is a 404. Ticket pages are served on every host, but an emailed link names the product's host (`https://{oldHost}/t/{token}`), so it works after a host change only while the old host's DNS and proxy site remain. Keeping them preserves `/t/` links only: the old host is then an unknown host, so old help-center links on it are not rewritten and answer 404, and `/` shows the default root.
- **Canonical 301s** (GET and HEAD only, the query string is kept, always an absolute `https://{storedHost}/...`): `/p/{sameKey}/x` on its own host goes to `/x`; `/p/{otherKey}/x` goes to that product's canonical URL; on the default host `/p/{key}/x` of a hosted product goes to `https://{host}/x`. A POST on a long-form path is rewritten and served, never redirected.
- **`PortalLinks`** (scoped, `Routing/PortalLinks.cs`) builds every link a component renders: a clean path for the current host's product, `https://{host}` plus a clean path for another hosted product, `/p/{key}/...` otherwise. `PortalLinks.Absolute` feeds the canonical URL and the JSON-LD. An architecture rule forbids the `PortalRoutes` builders in components outside `PortalLinks` and `PageLinks`.
- **SEO and cache.** `/sitemap.xml` is per host (see Caching and the sitemap); the output cache varies by host (`SetVaryByHost(true)`); the sitemap takes each product's host from the fresh product list. The per-path header rules run before the rewrite, so they match both the `/p/{key}/...` and the clean shape of the form pages and the help center. `/robots.txt` on a product host names that host's own sitemap (`ProductHostSeoUrlBuilder`, driven by the resolved product host; an unknown host names the default host's sitemap).

### Headers, robots.txt and the canonical host

`UseTechStrapWebHost(PortalHeaderRules.Rules)` applies per-path rules after the shared security headers: everything under `/t` gets `Referrer-Policy: no-referrer`, `Cache-Control: no-store` and
`X-Robots-Tag: noindex`, only `/t/{token}/attachments/{id}` is sandboxed (so the ticket page keeps the normal CSP), and the four form pages of a product (`/p/{key}/contact`, `/contact/received`, `/lost-link` and
`/suggest`) get `no-store` and `noindex`; a delivered help-center page gets `public, max-age=60` and the search page `no-store` (see Caching). `/robots.txt` disallows `/t/` and names `{public url}/sitemap.xml` (on a product host, that host's own sitemap). The
canonical-host redirect is an allow-list of legacy hosts and does nothing until configured.

### Logs and Sentry

The framework's request lines (path and query) are suppressed by default (`Microsoft.AspNetCore` is set to Warning); an operator who enables them gets them redacted. The PII enricher masks the access token in a
`/t/{token}` address and the value of a `name`, `email`, `subject`, `ref` or `q` query parameter; the Sentry processors mask the same (except `q`, which they already masked) in URLs, headers, breadcrumbs and spans.
`RequestLogRedactionHostTests`, `TicketReplyHostTests`, `ContactPostHostTests` and `TicketTokenLeakTests` scan every level at Verbose.

### The compose smoke

`pwsh scripts/Test-ComposeSmoke.ps1` (by hand or the manual "Compose smoke" workflow) also starts the Portal, checks its `/health/ready`, and proves the Api's rate limit sees the visitor, not the Portal's
container (D-019): its override lowers the Api's public limit to 3 and makes the Portal trust the compose subnet, then calls `GET /p/smoke/suggest` from inside the network as `203.0.113.10` (three 200s, then a 429)
and as `203.0.113.11` (a 200). It never runs `down -v`.

## Where things live

```text
src/TechStrap.Portal/
  Caching/        PortalCachePaths (what is kept), PortalOutputCache (the policy and the pipeline step)
  Clients/        ApiConnection, ProblemMapping and ProblemCopy, TicketToken, ApiClientRegistration (the two named clients), the typed clients (IPublicProductClient, IPublicTicketClient,
                  ICustomerTicketClient, IPublicKbClient, ISiteSettingsClient), MultipartForm, AttachmentFileName, ApiQuery, ApiDownload
  Components/
    Kb/           KbArticleBody (the other markup site), KbArticleCard, KbBreadcrumbs and KbCrumb, KbSearchBox, KbPlainText
    Layout/       PortalLayout, ProductHeader, ProductFooter
    Pages/        Home (the root), ProductHome, KbHome, KbCategory, KbSearch, KbArticle, Contact, ContactReceived, Ticket, LostLink, NotFound, Error, StyleGuide (Development only)
    Tickets/      CustomerMessageBody (a markup site), MessageThread, TicketStatusBanner
    Ui/           AccentScope, PoweredByFooter, ProductUnavailable, DevelopmentOnly, ErrorSummary, FieldError, FormField, FormGuard, AttachmentInput, HoneypotField, Pager, StateMessage
    BodyHeadings.cs  an h1 in an author's body is shown as an h2
    KbCopy.cs     the words of the help center (beside ShellCopy)
  Forms/          FormError and FormFields, FormCopy, FormFailure, AttachmentRules, EmailRules, ContactFormViewModel and its validator, ReplyForm, LostLinkForm, ContactCopy, ReceivedReference,
                  SubmitIds, SubmitKey and SubmitGuard (the double-send guard)
  Kb/             KbPaging, KbSearchText (plain helpers the pages and the suggest adapter share)
  Headers/        PortalHeaderRules (the /t rules, the attachment sandbox, the form pages and the help center)
  Products/       ProductThemeViewModel, ProductScope, ProductPageBase, PortalSkinFactory (the one caller of SkinResolver), DefaultPackProvider (the 60 s default-pack snapshot)
  Routing/        PortalRoutes, PageLinks (a link to a place on the current page), ProductKeyShape, KbSlugShape
  Seo/            PortalSeoRegistration (Blazor.Seo: base URL, robots.txt, the sitemap, canonical host), PortalSitemap, PortalSitemapBuilder, PortalSitemapCache, JsonLdText,
                  KbStructuredData (the breadcrumb and article records), KbSeo
  Settings/       PortalOptions and its validator
  Suggestions/    SuggestEndpoint (GET /p/{key}/suggest)
  Tickets/        CustomerTicketPresenter and its view models, TicketCopy, FollowUpLink, AttachmentPassThrough
  Uploads/        RequestTooLargeMiddleware
  Styles/         SCSS partials over the brand tokens (docs/BRAND.md)
  wwwroot/js/     kb-suggestions.js, portal-forms.js (both loaded once by App.razor)
```

Rules the code follows (the architecture tests check them): the Portal references **Contracts and Hosting only**; **components never inject `HttpClient`** (only `Clients/` mentions it); no inline script
or style; no interactive render mode; `MarkupString` is used in exactly two files, `Components/Tickets/CustomerMessageBody.razor` and `Components/Kb/KbArticleBody.razor` (a third is a design decision, argued in
its own commit); every plain-text DTO field is encoded and every JSON-LD string is a `JsonLdText`.

## Tests

`tests/TechStrap.Portal.Tests` runs the host in memory with a stub API behind its two named clients (`PortalFactory.Api`, `StubApiHandler`), so a test sees what the API would see, including the headers the
Portal added. `ProxyHopStartupFilter` puts the host behind a trusted reverse proxy and every API-calling host test asserts the visitor's address with `AssertEveryCallBore`; `OkProbeStartupFilter` answers 200 on a
path no Portal route matches. A test that needs the real server (a request size limit) calls `UseKestrel(0)` and `StartServer()` and reads the address from `IServerAddressesFeature`. bUnit covers the layout and
the shared help-center components; the help-center host tests (`Kb/`) parse the page with AngleSharp (which bUnit brings) and assert on elements, the JSON-LD is parsed as JSON, and `OutputCachePipelineTests` builds a
small host with the real wiring to prove what the cache keeps. The cache and sitemap-cache tests that wait use short real lifetimes, because a `MemoryCache` has no `TimeProvider`.
`tests/TechStrap.Portal.Tests/js` holds the node tests of the two browser modules. The start-failure tests of all three test projects use the one `tests/Shared/StartupFailure.cs`, which reads a host's refusal to start
from the log sink of its factory when `CreateClient()` loses a race with the host's disposal, and the "neutral 404" tests compare `Seen`, which holds every response header except the per-request ones. The accessibility and
style tests are `ResponsiveStyleTests`, `TokenContrastTests`, `CspStyleTests`, `HeadingHostTests`, `LayoutLandmarkHostTests` and `ErrorSummaryLinkHostTests`; the double-send tests are `SubmitGuardTests` and `DoubleSendHostTests`. The shared rules are in `TechStrap.Architecture.Tests` (`PortalRules`), the Hosting header-rule mechanism in `TechStrap.Api.Tests`
(`PathHeaderRuleHostTests`), and the log and Sentry redaction in `TechStrap.Api.Tests`. The skin tests are `SkinRenderingHostTests` (the byte-identical golden `Fixtures/unskinned-product-home.html`, the hostile-values test, neutral pages identical across hosts), `PresetStyleTests` (the compiled preset rules and the dark block's contrast pairs) and `DefaultPackProviderTests` (a fake clock moves the 2 s cold wait and the refresh).

## Manual checks (owner, before merging 09d)

The tests read the compiled CSS and the markup and run the host in memory; they cannot see a layout, a screen reader or a real browser. These checks close **P09-T16** and, with the compose run, **P09-T18**, and each result is
recorded in the pull request (the checklist there is ticked by the owner). Run the Portal against an API with the Development seed (`orbitly` and `paperplane`), in Chrome with the DevTools Console open.

1. No CSP errors on the Portal's pages: `/`, `/p/paperplane`, the contact form, the received page, a ticket page, the help center, an article. No `Refused to ...` lines; the fonts and the CSS load; both modules load (`js/kb-suggestions.*.js` and `js/portal-forms.*.js`, status 200, `text/javascript`).
2. Run the axe browser extension on the product home, the contact form (with the error summary showing), the received page, a ticket page, the help-center home, a category page, search results and an article: no critical findings. Record the result.
3. Run Lighthouse (accessibility) on the contact form, a ticket page and an article: at least 90 each. Record the scores.
4. JavaScript off (DevTools, Disable JavaScript): contact form -> submit -> received page, and a ticket -> reply -> the same page, work end to end; the received page still shows the number (it can be selected); the skip link jumps to the main content; an error summary link moves the focus to its field.
5. Screenshots at 360, 768 and 1280 px of the product home, the contact form, the contact form with errors, the received page, the ticket page and an article: one column on a phone, no horizontal scroll, the category cards two across from 768 and three from 1200, nothing clipped. Attach them to the pull request.
6. Keyboard only: Tab goes first to the skip link (visible), then the header navigation; Enter on the skip link moves the focus to the main content; on a form with errors the summary has the focus when the page opens and each link moves the focus to its field; focus is visible everywhere.
7. Click the contact button on the product home (an enhanced navigation) and type in the subject: the article suggestions appear. Then do the same from a page that was loaded directly.
8. Double click "Send message" on the contact form, "Send" on a ticket reply and "Send me a new link": one ticket, one reply, one email (check the Mailpit inbox and the Admin queue); the button shows "Sending" until the page changes. Press the back button after a send: the button is usable again.
9. On the received page click "Copy ticket number": the number is on the clipboard and "Copied" shows; in a page opened over plain http from another address (no clipboard) the number is selected and the sentence says so.
10. Type or paste about 85,000 characters into the message: the counter appears and counts; a long text with many line breaks shows it over the limit before the server would say so.
11. Forced-colors emulation (DevTools > Rendering > Emulate CSS media feature forced-colors): a 3px outline on the focused control, the skip link bordered, the error summary and the status banner outlined. Reduced-motion emulation: no animation. The console shows no CSP violations in either.
12. 400 percent zoom and increased text spacing (a bookmarklet or the Text Spacing extension): nothing is lost or overlaps.
13. The hostile accents: set a product's accent to `#F59E0B`, `#0F3D2E`, a red, a gray, a near-black and a blue in the Admin and look at the contact form and the product home: text, buttons, the skip link and focus stay readable.
14. A screen reader (NVDA or VoiceOver) on the contact form with errors, and on a ticket page: the landmarks are announced with their names (Main, the footer navigation, "Powered by"); the error summary is announced once; the counter speaks after a pause, not on every key.
15. An invalid token, an unknown product and an unpublished article look the same (the neutral 404 page).
16. The compose smoke (`pwsh scripts/Test-ComposeSmoke.ps1`) and the contact flow under `docker compose up` (P09-T18): the Portal is healthy, and hitting the contact form repeatedly from one client address trips the API's 429 for that address only.
17. The back button: send a contact message, press Back, edit the text, send again within 6 minutes: the second message arrives (a second ticket, with the edited text). Send, Back, send again without editing: it also arrives (Back gives a fresh form). A double click, or a refresh that re-posts, sends once.

## Known gaps

- Skins (D-053): a change of the default pack shows within about a minute (the Portal's 60 s snapshot), and output-cached help-center pages keep the old look until their own 60 s lifetime ends. Native scrollbars and widgets stay light on Midnight (`color-scheme` is not used). Tokens cannot express pixel-art imagery, hero sections, copy and voice, stepped text shadows, background images or a product's own display face outside the built-in list (see `docs/skins/README.md`). The landing cards, ticket and help-center pages and the mobile width of the five packs have had host and compiled-CSS tests but no hand visual check yet (the root, product home and contact pages of each pack were looked at in a headless browser).
- Product hosts (D-050): `robots.txt`, the canonical fallback and the Open Graph image on a product host follow that host (`ProductHostSeoUrlBuilder`; amended 2026-10-08); the output cache keys per raw Host value, so unknown hosts each get entries (a bound is PHASE-12 hardening); the canonical 301s carry `Cache-Control: public, max-age=3600` (a 301 from a form page or the KB search keeps that page's `no-store`), so a changed or removed host reaches visitors in about an hour, though emailed links still point at the old host (no redirect table).
- The product home has the shared search box (`KbSearchBox`, merged in 09d) and the header's link to the help center, but still no list of categories.
- The sitemap build's API calls carry the address of the visitor whose request started it, so each sitemap build makes 1 + N API calls under one forwarded IP, and the API's public limit is 120 per minute per IP:
  with about 120 or more active products the sitemap build is rate-limited and the sitemap goes stale or becomes unavailable. Possible later fixes are a bulk sitemap endpoint, or exempting the Portal's own build traffic from the limit.
- The help center has no category description on its category page and the product home has no category list (the article list does not carry a description, and a second call per page was not worth it).
- The form helpers need script (the sending state, the copy button, the counter) and are only extras: every flow works without them, which is the owner's JavaScript-off walk below.
- The double-send guard is per instance and is lost on a restart, and two Portal replicas do not share it (like the sitemap cache); the API has no idempotency key for the two public writes.
- The Blazor DOM diff of an enhanced navigation rewrites the attributes of an element it keeps, and the children a script added to such an element. The helpers are built for it (a custom element rebuilds in
  `connectedCallback` and again from a `MutationObserver` when its children were stripped; the sending state is in a `WeakMap`); a browser check of a link to the same page type is in the manual checklist.
- An address with parameters the page does not read gets a skip link that reloads to the cleaned address (the link keeps only the parameters the page reads, so it is no longer a same-document jump there). Focus still lands on the main content.
- The package's `JsonLd` component is unsafe for any text an author wrote (see SEO); the Portal does not use it for strings, and the problem is to be reported to `SyntaxCircus.Blazor.Seo`.
- A chunked post over the size limit is the framework's 400 about an antiforgery token, not a 413 (a browser form post always declares its length).
- A post to an unknown product is the framework's plain-text 400 ("Cannot submit the form 'contact' because no form on the page currently has that name."), not the 404 page a GET gets: the product page ends in `NotFound()` before the form is rendered, so there is no form to post to. Nothing is created. A post to a malformed or unknown ticket token is the same for the form `reply`, with an identical body, so nothing tells the two apart.
- A legacy-host redirect decodes percent-escapes in the query string (the package builds the target with `Uri.ToString()`), so a value that holds an encoded `&` or `#` changes meaning. It affects only hosts
  in `CANONICALHOST__LEGACYHOSTS`; report it upstream before using the redirect with the contact prefill.
- `NavigationManager.NotFound()` adds the framework's `blazor-enhanced-nav: allow` response header to an unknown product's 404, which the router's own unknown-route 404 does not carry. The bodies are identical,
  and both are 404, so it reveals nothing about which products exist.
- The Portal does not use `GlobalErrorBoundary`: its Try again button needs interactivity, and catching a render error in the page would answer 200 with a branded page instead of the plain 500 error page.
- Sentry has no general email rule: it masks the `name`, `email`, `subject` and `ref` query values and the `/t/{token}` path only (Serilog's email pattern does catch addresses in logs).
- If OpenTelemetry tracing were enabled, server spans would carry `url.path=/t/<token>`. It is off by default; masking it is a follow-up.
- The Portal's own `Seen` comparison ignores the antiforgery `Set-Cookie` (other cookies are compared by name) and ignores `Pragma` only when the antiforgery cookie is in play, set by the response or sent with the request (the framework's antiforgery step adds both to a response that rendered a form, which a post to a product that vanished after its form was served still carries): a visitor holding that form already knew the product.
