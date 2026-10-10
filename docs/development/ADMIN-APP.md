# Admin app (agents)

`TechStrap.Admin` is the Blazor Server app agents work in. It never touches the database: every screen reads and writes through the TechStrap API as the
signed-in agent. This page is for people who run it and people who extend it. What agents can do to a ticket through the API is in
[TICKET-OPERATIONS.md](TICKET-OPERATIONS.md); how the API checks tokens is in [AGENT-AUTHENTICATION.md](../self-hosting/AGENT-AUTHENTICATION.md).

PHASE-07 is delivered in three pull requests. **07a**: sign-in, the shell, the queue, ticket detail, the reply composer, the sidebar, spam, delete and
erase. **07b**: settings (products and their branding, API keys, agents, tags, My settings with the public display name), failed emails and the audit log.
**07c**: the command palette, local time, the CSP and security headers, the responsive and accessibility pass, session resilience, compose verification and the Admin architecture rules. This page describes
the app as it is once 07c is merged; the 07b and 07c parts say so. **PHASE-08** adds the knowledge base screens (the article list, the editor with a live preview and picture upload, the categories
page) and an article picker in the reply composer; the "Knowledge base (08)" section below describes them.

## Run it locally

You need the API running (see [TICKET-OPERATIONS.md](TICKET-OPERATIONS.md) and the root README) and an OpenID Connect provider. Without a provider the Admin still
starts if you give it placeholder values, but signing in fails: that is expected until an identity provider exists (see [Authentik setup](#authentik-setup-note)).

```bash
cp src/TechStrap.Admin/.env.example src/TechStrap.Admin/.env.local     # then edit the values below
dotnet run --project src/TechStrap.Admin --urls http://localhost:8081
```

`.env.local` is read in Development only and is git-ignored. With Docker Compose the Admin listens on `http://127.0.0.1:8081` and reads the same
`src/TechStrap.Admin/.env.local`: compose no longer sets the sign-in values itself, so there is one place to put them. Uncomment and fill the three keys in that file:

```bash
AUTH__AUTHORITY=https://auth.example.com/application/o/techstrap/
AUTH__CLIENTID=techstrap-admin
AUTH__CLIENTSECRET=...
```

Without them the Admin starts on the placeholders in `src/TechStrap.Admin/appsettings.Development.json` (`https://authentik.invalid/...`, `techstrap-admin`, `not-configured`)
and the sign-in page works, but the redirect to the provider fails. Leave the three keys commented out rather than blank: a blank value replaces the placeholder and the Admin stops at start.
Every setting the Admin reads is listed, with its default, in `src/TechStrap.Admin/appsettings.json`.

### Configuration

The Admin refuses to start with a message that names the missing variable. Values are validated at start and read lazily.

| Key | Required | Default | Meaning |
| --- | --- | --- | --- |
| `API__BASEURL` | yes | | Absolute URL of the TechStrap API, for example `http://localhost:8080/`. |
| `API__TIMEOUTSECONDS` | no | 30 | Timeout of one API call (1 to 300). |
| `AUTH__AUTHORITY` | yes | | The provider's issuer URL (https outside Development), with its trailing slash. |
| `AUTH__CLIENTID` | yes | | The OIDC client id of the Admin. |
| `AUTH__CLIENTSECRET` | yes | | The client secret. The Admin client is confidential. |
| `AUTH__SCOPES__0`, `AUTH__SCOPES__1`, ... | no | `openid profile email offline_access` | Added to the defaults, not a replacement (array binding appends), so leave them out unless you need another scope. `openid` and `offline_access` must stay (the API token is refreshed with the refresh token). |
| `TECHSTRAP_AGENT_GROUP` | no | `techstrap-agents` | Same key and default as the API. The API decides who has access; the Admin only shows what the API allows. |
| `TECHSTRAP_ADMIN_GROUP` | no | `techstrap-admins` | Same. |
| `TECHSTRAP_GROUP_CLAIM_TYPE` | no | `groups` | Same. |
| `TECHSTRAP_PORTAL_PUBLIC_URL` | no | | The customer portal's public base URL, the same key the API reads. The Admin uses it only for the "View on portal" link of a published article; blank hides the link. Absolute http or https, no query or fragment (the Admin refuses to start otherwise). |
| `LIVEUPDATES__ENABLED` | no | `true` | The live-updates kill switch (10b): `false` opens no hub connection and draws no indicator, banner or presence bar (see Live updates (10b)). The hub address is `API__BASEURL` plus `/hubs/tickets`. |
| `DATAPROTECTION__KEYRINGPATH` | in containers | | Persistent folder for the cookie and antiforgery keys. |
| `TRUSTEDPROXY__*`, `ALLOWEDHOSTS` | production | | Trust only your reverse proxy, so the sign-in redirect URI is built with the public scheme and host. |
| `SENTRY__*`, `OPENTELEMETRY__*`, `SERILOG__MINIMUMLEVEL__DEFAULT` | no | | Observability; see the `.env.example`. |

## How sign-in and access work

1. An anonymous visitor to any page is redirected to `/signin` (a plain landing page with one button). The button goes to `/signin/start`, which starts the OIDC code
   flow with PKCE. After the provider, the Admin receives the code on `/signin-oidc`, stores the tokens in the session cookie (`SaveTokens`) and returns the agent to the
   page they asked for (a local path only).
2. **The API decides who is an agent.** Once per circuit the Admin calls `GET /api/agents/me` with the agent's access token. That call also creates the agent's row, which
   every ticket call needs. Nothing inside the gate renders, and no ticket call is made, until it has succeeded. The answer is one of:
   - accepted: the shell, the queue and everything else appear;
   - refused (403): the **no-access page** with the reason (not in the agent group, deactivated, no email claim, identity not matched);
   - the token was rejected (401): "Your session has expired" with a sign-in button;
   - the API could not be reached: an alert with Retry, and still no page content.
3. Delete and erase, and the 07b pages (products, agents, tags, the audit log, failed emails), are **for an Admin only** (`AgentDto.Role`). A plain agent who types the address of one gets the
   page-level no-access page ("You don't have access to this page.") and the page makes no API call; the navigation shows them no admin links. While the session is still loading the page shows
   "Checking your access...", never the page and never a refusal. The API enforces the same rule (403 `admin-access-required`), so hiding is a courtesy. My settings is for every agent.
4. Sign out is a POST to `/signout` with an antiforgery token; it ends the cookie session and the provider session.
5. The Admin forwards the agent's **access token** to the API on every call, through named HTTP clients created per circuit (`IBlazorCircuitHttpClientFactory`). Reads are retried
   at most twice, with a short backoff, on a transport error, a timeout, 408, 502, 503 or 504 (never on 500, and there is no circuit breaker); a `Retry-After` header is honored but never waits longer than 2 seconds (07c); **writes are never retried** (a retried reply would send twice).
   Tokens: sign-in uses `SaveTokens = true` and there is no session store, so the access, refresh and id tokens are kept in the **encrypted auth cookie** (the browser holds
   that cookie, not the tokens in readable form), and they are also copied to the server-side token cache that the circuit's HTTP clients read. They are never rendered, put in a URL or logged.

**A 401 in the middle of a session (07c).** Every 401 is reported by `ApiConnection` to the scoped `SessionExpiry`, and `AgentSession` moves to "session expired". A 401 on the first load still shows the
full "Your session has expired" page. In the middle of work the session keeps what it knows (`ExpiredWhileWorking`; `IsAdmitted` and so `IsAdmin` stay as they were) and a `SessionExpiredBanner` (`role="alert"`) appears above the page with a "Sign in again" link to
`/signin/start?returnUrl=` for the page you are on; the page stays mounted, so an unsent reply or note is not lost.

**Once the session has lapsed, `ApiConnection` sends no further API request.** Every later read and write returns a local "session expired" failure, and the session stays expired for the rest of the circuit (a late `/me` answer cannot bring it back to Ready). The reason is the auth
package: it logs the request's `PathAndQuery` on an unauthenticated call, which includes the queue's search text. The consequence is that signing in again in another tab does not revive the old tab; the agent follows the banner's link, which starts a new circuit and lands on the same page. A draft that was only in the circuit is gone with it, so copy it before you follow the link if it matters.

### Roles

| What | Agent | Admin |
| --- | --- | --- |
| Queue, ticket detail, reply, note, status, assignee, priority, product, tags | yes | yes |
| Mark as spam, Not spam | yes | yes |
| Delete a ticket, erase a requester | no (not rendered) | yes, with a typed confirmation |
| My settings (email alerts, keyboard shortcuts, theme, public display name) | yes | yes |
| Products, branding, API keys | no (page-level no-access) | yes |
| Agents: see the list, activate, deactivate | no | yes |
| Tags: create, rename, recolor, delete | no | yes |
| Audit log, failed emails (retry, discard) | no | yes |
| Knowledge base: list, write, save, publish and archive articles; preview; upload pictures; create and edit categories; link articles from a reply | yes | yes |
| Delete a category | no (the button is not drawn) | yes |

**Roles are read-only here (D-041).** The agents page shows each role as a badge, with the note "Roles come from your identity provider's groups." An admin can only activate or deactivate an
agent. To make someone an admin, or an agent, change their group in the identity provider; the API reads it at their next sign-in.

## Authentik setup note

The step-by-step provider setup (groups, provider, application, the groups claim, which value goes in which env file, and how to verify) is in the [Authentik worked example](../self-hosting/AUTHENTIK.md); the claims the Api needs are in [AGENT-AUTHENTICATION.md](../self-hosting/AGENT-AUTHENTICATION.md). Any OIDC provider that supports the same features works. The provider flow is documented in the worked example and is verified against a live Authentik in 12c (T14). The tests here use a fake `Test` authentication scheme and a stub API.

Two things concern local development only:

1. **Local runs use placeholder values.** Without `AUTH__AUTHORITY`, `AUTH__CLIENTID` and `AUTH__CLIENTSECRET` in `src/TechStrap.Admin/.env.local`, the Admin (in compose or under `dotnet run`) starts on the placeholders of `appsettings.Development.json`, the sign-in page renders, and sign-in fails. That is expected until you point it at a provider. For a local provider also register `http://localhost:8081/signin-oidc` and `http://localhost:8081/signout-callback-oidc`.
2. **Behind a reverse proxy**, set the trusted proxy keys so the Admin builds `https` redirect URIs, and make sure the provider can be reached from the Admin container at the authority URL.

If sign-in loops or the no-access page shows "not in the agent group" for someone who is, check in this order: the groups claim name, whether the claim is in the **access** token (the API
reads that one), and `TECHSTRAP_AGENT_GROUP` on **both** the API and the Admin.

## Where things live

```text
src/TechStrap.Admin/
  Auth/           cookie + OIDC wiring, /signin and /signout endpoints, AgentSession (the API's answer to /me)
  Clients/        ApiConnection and the typed clients (IAgentsClient, IProductsClient, ITagsClient, ITicketsClient, IRequestersClient, IAdminEventsClient, IDeadLettersClient, IKbClient), the /attachments/{id} pass-through
  Components/
    Ui/           reusable primitives: LoadingState, ErrorState, EmptyState, ConfirmDialog, PagerControl, TagChip, RelativeTime (local time), ScrollRegion (a table's scroll box), StatusStamp, PriorityMark, AdminOnly (the admin page guard), AccentPreview, ...
    Layout/       MainLayout, NavMenu (the collapsible rail), RailLink, StatusBar, AgentGate, ShortcutHelpDialog, CommandPalette
    Pages/        sign-in landing, no-access, not found, error, style guide (Development only)
  Features/
    Shell/        StatusMessageService, ShortcutService, ShortcutCatalog, CommandRegistry (the palette's commands), LocalTimeService (the browser's time zone), UncertainMarks (writes held until a later read)
    Queue/        TicketQueuePage and its filter bar, tabs and row
    Tickets/      TicketDetailPage, presenter, timeline factory, ReplyComposer, TicketSidebar, TagPicker, TicketActions
    Live/         Live updates (10b): ITicketLiveClient and SignalRTicketLiveClient (one per circuit; NullTicketLiveClient when the switch is off), LiveConnectionIndicator (the one place the connection starts), QueueLiveBanner, ChangedTicketBanner, TicketPresenceBar, PresenceViewModelFactory, LiveChangeRules (own change, this ticket), LiveRetryPolicy
    Settings/     Admin only. Products/ (ProductsPage, ProductEditorPage, ProductKeysPage, ApiKeysPanel, NewApiKeyDialog; the logo rule is `BrandingRules.IsAcceptableLogoUrl` in Contracts.Branding), Agents/ (AgentsPage), Tags/ (TagsPage),
                  Audit/ (AdminEventsPage, AdminEventSummaryFactory), EmailKinds
    Ops/          Admin only. DeadLetters/ (DeadLettersPage)
    Kb/           Every agent. KbArticleListPage, KbArticleEditorPage (with KbArticleEditorPresenter and the form model), MarkdownEditor, KbPreviewPane, KbImageUploadButton, MarkdownSnippets, KbCategoriesPage, ArticlePicker (mounted by ReplyComposer)
    Account/      My settings for every agent: NotificationPreferencesPage, PublicDisplayNameField, PublicNamePreview
  wwwroot/js/     dialog.js, shortcuts.js, queue.js, preferences.js (browser preferences and the theme), clipboard.js (copy the new API key), tz.js (the browser's time zone), palette.js and menu.js (keys of the command palette and the actions menu)
                  are ES modules; theme-init.js is the one classic script, loaded in the page head. No inline script anywhere
  Styles/         SCSS partials over the brand tokens (docs/BRAND.md)
```

Rules the code follows (and the reviewers check):

- The Admin references **Contracts and Hosting only**: never Application, Infrastructure or Domain. DTOs are never renamed; view models are feature-local.
- **Components never inject `HttpClient`.** They inject the `I*Client` interfaces, which return `Result<T>`; the clients map the API's ProblemDetails (the error `type` is the code).
- Every read takes the component's `CancellationToken`. Writes (reply, note, sidebar changes, spam, delete, erase, and every settings write) deliberately pass `CancellationToken.None`: the server may commit a write after the agent has left the screen, so canceling the call would only hide the outcome.
- Razor components are always public classes, so a type used as a component parameter is public (a view model cannot be `internal`).
- A component beyond a few plain parameters and one forwarder has a `.razor.cs`; a factory or presenter exists only for non-trivial assembly (`TicketDetailPresenter`, `TimelineEntryFactory`).
- `MarkupString` is used in exactly two files, and both draw HTML the API sanitized: the message body in `MessageBubble`, and the knowledge base preview in `KbPreviewPane` (the answer of `POST /api/kb/preview`, which runs the same renderer and
  sanitizer as the portal). `MarkupStringSiteTests` pins the two. Everything else is encoded.
- Repeated or meaningful literals are named constants (`QueueDefaults.PageSize`, `QueueDefaults.SearchDebounce`, the Contracts constants for views and event types).

### Add a screen that calls the API

1. Add the method to the client interface and implementation in `Clients/`, returning `Result<T>` and taking a `CancellationToken` last. A GET goes through the read client, anything that changes
   data through the write client (never retried).
2. Put the page under `Features/<Area>/` with `@page`, `@attribute [Authorize]`, a paired code-behind, and load in `OnParametersSetAsync` with a cancellable token. An admin page is a thin shell, `<AdminOnly><XxxContent /></AdminOnly>`, and the content component (which makes the API calls) is only created for an Admin, so a plain agent's page load calls nothing the API would refuse.
3. Render the four states with `LoadingState`, `ErrorState` (with Retry), `EmptyState` and the content. Keep the previous content when a refresh fails.
4. Send the ticket's `RowVersion` on every write and replace the local state from the returned `TicketStateDto`. Raise the page's conflict callback on `concurrency-conflict`.
5. Test it with bUnit and a substitute client; add a host test only for what bUnit cannot see.

## Keyboard shortcuts

Single-key shortcuts are off while you type in a text field, select or editable area and while a dialog is open. `?` lists them in the app. My settings has a Keyboard shortcuts switch that turns them off; it is remembered in this browser.
The command palette (Ctrl+K or Cmd+K) is a chord, not a single key, so it works while you type and when the single-key switch is off (WCAG 2.1.4).

| Key | Where | What |
| --- | --- | --- |
| `j` / `k` (or the arrow keys) | Queue | Move the selection; it never reorders rows |
| `Enter` | Queue, nothing focused | Open the selected ticket |
| `/` | Anywhere | Focus search (opens the queue from other screens) |
| `r` / `n` | Ticket | Public reply / internal note: switch tab and focus the box |
| `e` | Ticket | Focus the assignee control |
| `u` | Spam view (row selected) or a flagged ticket | Not spam, no dialog |
| `Ctrl+Enter` | Reply box | Send in the current mode |
| `Esc` | Anywhere | Leave a field (text kept), close a dialog, or go back to the queue |
| `Ctrl+K` / `Cmd+K` | Anywhere | Open or close the command palette |
| `?` | Anywhere | Show the list |

### Command palette (07c)

Ctrl+K opens a dialog with a search box and a list of commands. Type to filter (every word must appear in the name or the group), ArrowDown and ArrowUp move the selection (they wrap; Home and End jump), Enter or a click
runs the selected command, Esc closes it and focus returns to where it was. The palette closes first and the command runs after it has closed, so a command that moves focus (Reply) lands on the page and not on the dialog.
A command runs once. Ctrl+K never opens over a confirmation dialog, and opens only when `State == Ready` (the API has said who you are and the session has not expired). Admin commands are listed only when `IsAdmin`, and they are checked again when chosen. Choosing a command, and running it after the dialog has closed, also require `Ready`, so nothing runs once the session has lapsed. Nothing here ends the circuit: whatever a command throws (a cancellation included) is logged by exception type only and reported in the status bar. Destructive actions (close, delete, spam, key revocation) are deliberately not palette commands.

| Group | Commands | Who sees them |
| --- | --- | --- |
| Go to | Queue: Unassigned, Mine, Open, Pending, All, Spam; Knowledge base; My settings | Every agent |
| Admin | Products, Agents, Tags, Audit, Failed emails | Admins only; `AdminOnly` is checked again when the command runs, not only when it is listed |
| Ticket | Reply to requester, Add internal note, Assign to me (when it is not already yours), Not spam (on a flagged ticket) | While that ticket is on screen and open |

A screen adds its own commands with `CommandRegistry.Register(...)` while it is mounted and disposes the registration when it goes; `CommandRegistry.Available(isAdmin)` is the one place that decides who sees what.
The ticket commands raise the same shortcut action as their key, so the composer, the sidebar and the actions menu run their own code.

## What an agent can do here (07a)

- **Queue**: six views (Unassigned is the default, then Mine, Open, Pending, All and the separate Spam view), counts per view (Spam muted), filters (product, status, priority, tag), search, 25 per page. Every filter is in
  the URL, so views can be bookmarked. The queue refreshes when you press Refresh, and a banner offers the same refresh when something changes (Live updates (10b), below).
- **Ticket**: one timeline of messages and changes (customer white, public reply canary, internal note pink with a dashed edge), the requester, metadata labeled **Untrusted** unless it came from a trusted key,
  attachments as downloads (served through the Admin, never inline), and a link to the parent of a follow-up.
- **Reply or note**: separate drafts per mode, files (up to 5, 10 MB each), Pending by default or "Send and solve". A failed send, or a conflict, never loses the text or the files.
- **Change a ticket**: status, assignee, priority, product, tags. Each change shows "Saving...", then the screen shows what the API accepted. If someone else changed the ticket first you see
  "This ticket changed since you opened it": press Reload, your draft is kept.
- **Spam, delete, erase**: Mark as spam asks first. Delete and erase (Admins) need the ticket number or the requester's email typed, and say so plainly; erase covers every ticket from that requester.

## What an admin can do here (07b)

Everything in this section needs the Admin role. The API refuses the same calls to anyone else.

- **Products** (`/settings/products`, `/settings/products/new`, `/settings/products/{id}`): a list of every product, active or not, and an editor for the name and the branding (display name, logo, accent color,
  email from address and reply-to). The key and the ticket number prefix are permanent: they are asked for when the product is created and shown read-only afterwards. The **logo address** is a link, not an upload:
  only a full `https://` address is accepted (`http://localhost` or `http://127.0.0.1` too, in every environment), a blank value means no logo, and the API refuses the same addresses, so `javascript:`, `data:`, relative paths and other
  schemes never reach an email or the portal. The accent color is checked with the same pattern as the API (`#RRGGBB`); a low-contrast color only shows a note, because TechStrap darkens it wherever it is used
  for text. A **tagline** (up to 160 characters, no control characters) is the short line under the name on a landing card; blank means none. **Listed on the landing page** (on by default) decides whether the product gets a card when the Portal runs with `TECHSTRAP_PORTAL_LANDING=Products`; an unlisted product is still reachable and in the sitemap. An **uploaded logo** (PNG, JPEG or WebP, at most 1 MiB; SVG and GIF are refused) is chosen with the upload button beside the preview and removed with Remove; it wins over the logo address everywhere, uploads happen at once (not with Save), a save is held while one is running, and a new product must be saved before it can have a logo. A live preview shows the name, the logo and the accent. Saving always sends the product's current Active setting and the version the editor was opened on: if someone else saved first you see "This
  product changed since you opened it", your edits stay on screen, and Reload shows the saved version. A failed or lost save never clears the form.
- **API keys** (`/settings/products/{id}/keys`): the keys of a product by label, kind (a Trusted or a Public badge, always the word), prefix, created and last-used time, and status. Creating one asks for the kind
  and an optional label, then shows the key **once**, in a dialog with a Copy button. The dialog cannot be closed (Cancel and Esc do nothing) until you tick "I have stored this key"; closing it clears the key from
  the page, and nothing can show it again. If the answer to a create is lost, the page says the key may exist but its secret cannot be shown: revoke it and create another. Revoking asks first ("Apps using this key
  will stop working.") and a revoked key stays in the list, marked Revoked.
- **Agents** (`/settings/agents`): everyone who has signed in, with role badge, active or not, and last seen, 25 to a page. Activate needs no confirmation; Deactivate asks first. Deactivating the only active admin
  is refused by the API, and the page shows its message inside the dialog. Deactivating yourself warns you, and then shows the no-access page.
- **Tags** (`/settings/tags`): every tag with a **ticket count**, create (the slug follows the name until you edit it), rename and recolor in the row, and delete. Deleting an unused tag asks first. Deleting a tag
  that is in use shows its count ("12 tickets"), asks you to type the tag's name, and only then removes it from every ticket and deletes it.
- **Audit log** (`/settings/audit`): who changed what, newest first, 25 to a page. Filter by what changed (product, API key, agent, tag, requester, ticket, email) and by who; both stay in the address so a view can
  be linked. Every event is one sentence built from the ids, slugs, prefixes and counts in its payload. The raw payload is never shown. The first page fixes a point in time, so events recorded while you read do
  not push rows onto the next page.
- **Failed emails** (`/ops/dead-letters`): emails that used up their retries, with the recipient masked, the kind, a link to the ticket, the tries and the last error as a plain category. Retry puts one back in the
  queue with no confirmation; Discard asks first. After either, the list and the count beside "Failed emails" in the navigation are read again. An empty list says "No failed emails".

Every agent, not only an admin, has **My settings** (`/account/notifications`):

- **Email alerts**: a switch per active product for "a new ticket arrived". Each change sends every product's setting, so nothing is left for the API to guess.
- **Keyboard shortcuts** and **theme** (Auto, Light or Dark): kept in this browser only, so they follow the browser, not the account.
- **Public display name**: optional, plain text, up to 60 characters, no `@`. A live line shows what customers will see ("Customers see: Sam from Orbitly Support"); clearing the field returns to the first name from
  your profile. It saves when you leave the field or press Enter, and customers never see your email address.

## Knowledge base (08)

Every agent can write and publish articles; deleting a category is the one thing that needs the Admin role. The rail has a "Knowledge base" link for everyone, and the palette has "Knowledge base" (`go-kb`). The API behind all of it is in
[PHASE-08-knowledge-base.md](../architecture/PHASE-08-knowledge-base.md); the decisions are D-044.

- **Articles** (`/kb`): the list, 25 to a page, newest change first. Search the title, summary and text, and filter by product (including "Shared", the articles every product shows), category and status (Draft, Published or Archived). Every filter is in the address, so a
  view can be linked. Each row shows the title, the product or "Shared", the category, the status as a word, and when it changed. An empty knowledge base invites the first article.
- **Editor** (`/kb/new`, `/kb/{id}`): product (chosen once, or "Shared by every product"), category, title, slug, summary and the article text. The slug follows the title until you edit it, and like the product it is permanent once the article exists. The category list offers the
  shared categories and, for a product article, that product's own; a shared article can only use a shared category. The text is Markdown in a plain text box with a toolbar (bold, italic, link, list, code) beside a live preview.
- **Preview**: 300 ms after you stop typing, the Admin asks the API to render the text (`POST /api/kb/preview`) and draws the answer, which is the same HTML the portal will show (tables and pictures included, anything unsafe removed). A call that a newer keystroke replaced is
  canceled and its answer is never drawn; a failed preview keeps your text and the last good preview and says so; an empty text is not previewed. Below 992 px Write and Preview are two buttons and one pane at a time.
- **Pictures**: "Add image" takes a PNG, JPEG, GIF or WebP file of up to 5 MB (the file is read in the browser and anything else is refused before it is sent), uploads it and adds `![file name](address)` at the **end** of the text. The toolbar buttons also add their Markdown at the end. Blazor cannot read the
  caret, and paste and drag-and-drop are not built (D-044). If the answer to an upload is lost nothing is added to the text and you pick the picture again; the earlier copy, if the API kept one, is an unreferenced file.
- **Save, publish, archive**: Save sends the version the article was loaded with, so a stale save is a 409: you see "This article changed since you opened it", your edits stay in the form, saving stays off, and Reload brings in the latest version (and replaces your edits, so copy what you need
  first). Editing an **archived** article makes it a draft again, and the status line says so. Publish needs the article saved first and a title, slug, text and category (a line under the buttons says which is missing); it sends the loaded version too. Archive asks first ("It is removed from the portal,
  from search and from the sitemap"). A write whose answer is lost (a timeout, an unreachable API, a 5xx) is held: the page says it may have gone through and nothing more is sent until you reload.
- **Leaving with unsaved changes**: moving to another page in the Admin asks "Leave without saving?" (Stay or Leave), and closing the tab or following an outside link gets the browser's own prompt. A form that is back to what was saved does not ask.
- **View on portal**: a published article shows this link, built from `TECHSTRAP_PORTAL_PUBLIC_URL` as `{portal}/p/{product key}/kb/{category slug}/{slug}` (from the saved category, not one you have picked but not saved). A shared article is reachable under every product, so the link uses the
  first **active** product by name (an inactive product is skipped, and with none active there is no link). With no portal address configured, or for a draft or an archived article, there is no link.
- **Article text limits**: the knowledge base renders Markdown with its own content profile, separate from the one for messages. A text that renders to more than 5000 elements (paragraphs, list items, table cells) is refused with `kb-body-too-complex` on preview, save, create and
  publish; the editor says to split the article or shorten the table or list, and the preview keeps the last version that fitted. **Reload** asks first when the form has unsaved changes, because it replaces them.
- **Categories** (`/kb/categories`): every category with its product (or "Shared"), slug, sort order and description; create one (the slug follows the name until you edit it; the slug `search` is kept for the portal's search page; a slug already used anywhere is refused), rename it,
  describe it and change its sort order in the row (saved through the update, with the version the list was read with). **Delete** is drawn only for an Admin. A category that still has articles cannot be deleted: the dialog says so and cannot be confirmed; move the articles first.
- **Linking an article from a reply**: a public reply has "Link a knowledge base article". It opens a search of the **published** articles of the ticket's product and the shared ones (nothing is searched until you type, and a draft or an archived article is never offered); each result has an
  Add button, and the choices show as chips with Remove. At most 10 articles. The chips belong to the ticket's draft: they survive switching to a note and back, a failed send and leaving the ticket; a note never sends them; an accepted send takes out exactly the ones it sent. If the API
  refuses a link (an article unpublished or moved to another product meanwhile) the text and the chips stay and you are told to remove the ones you no longer want. The customer's email gets a link to each article. The links are validated when the reply is
  sent and checked again when the email is sent: an article unpublished in between is dropped from the email. The email's list keeps each title to 120 characters, and the list is left out if the email would still be over its size cap (the reply is always sent).

## Local time, theme, security headers and layout (07c)

**Local time.** Times show in the zone of the browser (`Intl.DateTimeFormat().resolvedOptions().timeZone`, read by `wwwroot/js/tz.js` once per circuit). The prerender, and any circuit whose browser gives no usable zone, shows UTC;
when the zone arrives every time draws again. The zone load has its own guard (a failure there never touches the shortcuts) and runs after the shortcuts have started. Relative times ("5 min ago") do not depend on the zone; from a week on the visible text is a bare date (`yyyy-MM-dd`: in UTC until the zone loads, then in your zone, with no label on the text), and the tooltip shows your local time with the zone name and the UTC time
("2026-10-04 12:55 Europe/London, 2026-10-04 11:55 UTC"). A zone with a base offset of 0 and no daylight saving (`Etc/UTC`, `Atlantic/Reykjavik`) shows the single UTC tooltip, because the two lines would be the same instant. The `datetime` attribute is always UTC. The zone is not stored: there is no API field and no preference for it (D-042). The Admin image installs `tzdata`; an unknown or oddly shaped zone name falls back to UTC.
**Image risk:** the legacy zone names (`Asia/Calcutta`, `US/Eastern`) come from the `tzdata-legacy` package on Debian trixie, which the image would not have after a move of the base image to trixie; check that a browser reporting such a name still resolves (otherwise it shows UTC) when the base image changes.

**Theme before first paint.** `wwwroot/js/theme-init.js` is a small blocking classic script in the page head, loaded by `App.razor` and by the sign-in landing page. It reads the same storage key as `preferences.js` (`techstrap.admin.theme`) and sets `data-bs-theme` for Light or Dark before the
first paint, so a stored choice no longer flashes the other theme while the circuit connects. Auto removes the attribute. The script never throws; without storage the page is Auto.

**Content Security Policy and headers.** Admin and Portal send one policy from `TechStrap.Hosting` (`TechStrapCsp.ForBlazorApp`, applied by `UseTechStrapWebHost`), plus `X-Content-Type-Options`, `Referrer-Policy` and `Permissions-Policy`. The policy is `default-src 'self'; script-src 'self'; style-src 'self'; style-src-attr 'unsafe-inline'; img-src 'self' https: data:; connect-src 'self'; font-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self' <the identity provider's origin>`
(the origin comes from the configured authority, written with `IdnHost` and accepted only as printable ASCII), so the sign-in and sign-out redirects are allowed. `data:` is there because Bootstrap's compiled CSS draws its icons with data: SVG. In Development `img-src` also allows loopback http (a logo on localhost). There is no `upgrade-insecure-requests`, and `connect-src` has no explicit `ws:` or `wss:`: whether `'self'` covers the circuit's websocket is checked in the owner's browser (the checklist below). Scripts stay strict: there is no inline script, and `<ImportMap />` was removed from both apps (an architecture rule flags it coming back). The Api sends `default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`, and a successful (2xx) attachment download additionally gets `sandbox`, in the Api and in the Admin's `/attachments` pass-through alike. The one relaxation is `style-src-attr`, because the product accent and tag colors are admin data set as `style` attributes (`AccentPreview`, `TagChip`).
If a page loses its styles, a font or sign-in after a change, open the browser console: a blocked resource is named there with the directive. Header tests assert the headers on the Admin pages (including `/signin` and `/error`), the Portal and the Api. `UseTechStrapWebHost` does not send HSTS in Development, and the policy override is applied with `PostConfigure`, so it wins over the package default. The attachment download's `sandbox` matches the exact directive and replaces a weaker `sandbox` value. Only a 2xx answer is sandboxed: an upstream 404 re-executes the ordinary not-found page, which needs its script and keeps the full policy.
The Admin architecture rules also flag inline `on*=` event attributes and `<ImportMap>` in every spelling.

**Static pages.** Only `Error`, `NotFound` and `StyleGuide` may carry `[ExcludeFromInteractiveRouting]` (they render without a circuit and so without the access check). The authoritative gate is `StaticPageReflectionTests` in `TechStrap.Admin.Tests`, which reads the compiled assembly; the text rule in `TechStrap.Architecture.Tests` fails closed (a file it cannot parse is a violation), and both pin the pages to exact paths.

**Security in the shared hosting code (07c).** Sentry masks the value of `search` and `q` in the URL, the query string, every request header (including `Referer`), breadcrumbs, the message and its parameters, the request body and cookies (when they are text), extras, tags, exceptions, and span data and tags; a value that is not itself sensitive is scrubbed in its turn, so a cookie holding `last=/queue?search=x` is masked; names are matched after URL-decoding. The Worker registers the same scrubbers as the other hosts. The Api's OpenAPI document has the `Bearer`, `ApiKey` (`X-Api-Key`) and `TicketToken` (`X-Ticket-Token`) schemes, as a requirement per operation. A product update looks the product up before it validates, so an unknown product with an invalid body now answers 404 and no longer 400.

**Responsive behavior.** From 992 px the rail is a column at the left. Below 992 px it becomes a bar with a Menu button (`aria-expanded`) that opens the links (the closed links are `display: none`, so they are out of the tab order), and choosing a link closes it again. Below 768 px each ticket in the queue is a card (number, status The very bottom of the rail shows the running build's version as muted text (for example `v0.4.1`: the SemVer from the image's `InformationalVersion` with the build metadata cut off, no commit and no branch); a build whose version is not SemVer, and a session that is not admitted yet, show nothing.
and time, then the subject, then Product, Requester, Priority and Assignee with their names); between 768 and 992 px the queue drops Product, Requester and Priority. On a phone the reply box follows the conversation and the ticket controls come after it. A wide table
scrolls inside its own region (`ScrollRegion`, a named, focusable box, wrapping the 7 ledger tables), never the page. On a phone the ledger tables turn into stacked cards; the tables and their rows, cells and headers carry explicit `table`, `rowgroup`, `row`, `cell` and `columnheader` roles so browsers that drop table semantics when the CSS changes `display` keep them; whether a screen reader then announces table, row and column names on those cards is still an owner check. When the rail folds away after you choose a link, focus that was inside the panel moves to the Menu button (`menu.js` `focusIfWithin`); focus elsewhere is left alone. `prefers-reduced-motion` switches animation off, and forced colors keep the current link, tab and row by an outline.

**Manual checklist (owner, before merging 07c).** The tests read the compiled CSS and the markup; they cannot see a layout or a real browser. With the app signed in (this needs the identity provider, owner action 7), Chrome with the DevTools Console open and the Network tab on the WS filter:

1. No CSP errors on the main pages. On `/signin`, `/error`, `/not-found` and the signed-in `/`: no `Refused to ...` lines in the console; fonts, favicon and CSS load.
2. Style guide (`/_styleguide`). Swatch and tag-chip colors show (they depend on `style-src-attr`). Bootstrap's `data:` icons show: the select arrow, the checkbox tick and the close button.
3. Sign-in. Sign in from `/signin`, and again from the gate's "Sign in again" link. The redirect to Authentik and back works with no `form-action` refusal. If one appears, note the blocked origin: it means discovery's origin differs from `Auth:Authority`.
4. The live connection connects. In Network, `/_blazor?id=...` shows `101 Switching Protocols` with no `connect-src` refusal (this is the check that `connect-src 'self'` covers the websocket; if it does not, say so in the PR so the policy can name `wss:`). Note whether you are behind the reverse proxy (`wss://`) or on plain localhost (`ws://`).
5. Reconnect dialog. Stop the Admin process while a page is open: the app's own reconnect dialog appears, not Blazor's default overlay, with no `style-src` refusal. Restart the Admin; Retry or Resume works.
6. JS modules load: no 404 for `/js/*.js` across the shortcut help (`?`), a confirm dialog, the queue, a preference change, API-key copy-to-clipboard and Ctrl+K.
7. Attachment download headers. Reply to a ticket and open an attachment: the download response has exactly one `Content-Security-Policy`, ending in `; sandbox`. Open `/attachments/` with an id that does not exist: the not-found page loads normally and its policy has no `sandbox`.
8. Sign-out. `POST /signout` goes through the provider's end-session and back to `/signin`, with no `form-action` refusal.
9. Optional, Firefox: repeat 4 and 5, because `'self'` matching ws and wss is browser dependent.
10. 07b key dialog: pressing Esc repeatedly on the new-key dialog must not close it until "stored" is ticked.
11. No theme flash on load; local times are correct; the palette works by keyboard (Ctrl+K, type "mine", Enter; focus returns to the opener when it closes).
12. Layout at 1280 px: the rail is a 208 px column with no Menu button; the queue shows every column; settings tables keep all headers and show no stray scrollbar.
13. Layout at 768 px (also 800 and 991): the rail is a bar with brand and Menu (at least 44 px); the panel opens full width; the current link is marked; choosing a link closes it (note where focus lands; also click the current page's link); the queue drops Product, Requester and Priority; settings tables scroll sideways inside a focus-ringed region with an edge shadow (light and dark); the ticket page's side panel is above the conversation.
14. Layout at 390 px: each ticket is a card (number, status and time, subject, labeled Product, Requester, Priority, Assignee); "Not spam" works in the spam view; the selected-row bar shows; long subjects wrap; no horizontal page scroll anywhere; the Menu button is square at the top right; the reply box is after the conversation; Tab reaches a scroll region and the arrow keys scroll it.
15. Screen reader (VoiceOver or NVDA) on the 390 px card layout: are table, rows and column names announced?
16. Forced-colors emulation: a 2 px Highlight outline on the current link, tab, selected row, open Menu and focused region. Reduced-motion emulation: no animation. The console shows no CSP violations in either.
17. Keyboard only, from the queue: Ctrl+K, type "mine", Enter; j/k to a ticket, Enter; r, type a reply, Ctrl+Enter; set the status to Solved from the sidebar. Focus is visible everywhere and never lost; Esc closes the palette and the actions menu and returns focus.
18. Run the axe browser extension on the queue, a ticket, Products and the style guide (`/_styleguide`, Development): no critical findings; record the result in the PR.
19. The mid-session 401. Type a draft (a reply or a note). Make the API refuse the token, for example by revoking the session in Authentik. Trigger a load. Then: the session-expired banner shows; the draft stays; nothing else saves; "Sign in again" returns to the same page.
20. Portal. The Portal `/` shows no `Refused to` lines in the console.

**Compose smoke.** `pwsh scripts/Test-ComposeSmoke.ps1` builds the four images one after another, starts the local compose stack under its own project name (`techstrap-smoke`) on free ports, waits for every healthcheck and checks that the Api, the Admin and the Portal answer `/health/ready` with 200 and that the Admin container is healthy. It then proves that the Api's rate limit sees the real client address through the Portal, not the Portal's own (PHASE-09 T18; see [PORTAL-APP.md](PORTAL-APP.md)), and stops the project (never `down -v`). It needs Docker and runs by hand or from the "Compose smoke" workflow; it is not part of every pull request.

## Known limits

- **Pages render twice.** Blazor Server prerenders each page on the server, then renders it again in the circuit, so every page loads its data twice (two calls to the API for the same screen). The first render is what a plain HTTP request sees, which is why the host tests can read the data in the HTML. Making the second load reuse the first (persistent component state) is not done in 07a.
- **Attachments must be re-attached after leaving a ticket.** Chosen files are held by the browser for the composer on screen. The browser only lets the page read a file while the file input that produced it is on screen, so leaving the ticket drops them and the composer tells you to attach them again. The typed text is kept for the life of the circuit; the files are not. A failed send or a conflict on the same screen keeps both.

- **The read client retries but never breaks the circuit.** A read is tried up to three times in all (250 ms base backoff with jitter) on transport errors, timeouts, 408, 502, 503 and 504, and a `Retry-After` header is honored but capped at 2 seconds a try. There is no circuit breaker: one named read client is shared by every agent, and a breaker opened by one failing endpoint would lock everyone out. Under a real outage every call waits out its retries (a few seconds at most) before it fails.
- **Any 5xx counts as an uncertain write.** The pages decide by the answer's status, not by the API's problem `type`, so a write that ends in a 500 says "this may have happened" and offers a reload rather than a bare retry. The write client's timeout is at least 300 seconds (uploads), whatever `Api:TimeoutSeconds` says.

## Troubleshooting

| You see | Cause |
| --- | --- |
| The Admin does not start; the log names `AUTH__AUTHORITY` (or another key) | A required key is missing or malformed. |
| Sign-in bounces back to the sign-in page | The provider rejected the redirect URI, or `AUTH__CLIENTSECRET` is wrong. |
| "You don't have access" | The API refused `GET /api/agents/me`; the page says why. The group claim is checked by the API. |
| "Your session has expired" shortly after signing in | No `offline_access` scope, or a very short refresh token validity. |
| The queue shows "Couldn't load tickets" with the API's message | The API answered an error; the message is the API's own. |
| Attachments open as a page instead of downloading | Report it: the pass-through must force a download. |
| Keyboard shortcuts do nothing | Focus is in a field, a dialog is open, My settings has Keyboard shortcuts switched off, or `shortcuts.js` was blocked. Check the browser console. |
| A settings page says "You don't have access to this page." | The signed-in agent is not an Admin. Roles come from the identity provider's groups, and the API decides. |
| The new API key dialog will not close | Tick "I have stored this key" first. The key is shown once and cannot be shown again; if you lost it, revoke the key and create another. |
| Copy does nothing in the new API key dialog | The browser refused the clipboard (a page that is not https, or blocked). The key is selected: press Ctrl+C. |
| A product's logo is refused | Only a full https:// address is accepted (and http://localhost or http://127.0.0.1, in every environment). There is no upload. |
| The theme does not change | The choice is kept in this browser: private windows and cleared site data forget it, and Auto follows the device. |
| Times show in UTC | The browser gave no usable time zone, or the page has not finished connecting. Reload; the tooltip always shows the UTC time too. |
| Ctrl+K does nothing | A dialog is open (close it first), the page has not finished connecting, the API has not accepted you yet, or `shortcuts.js` was blocked. The browser's own Ctrl+K (address bar) is stopped only while the page has focus. |
| A banner says "Sign in again" above the page | The API rejected your token in the middle of your work. Copy any unsent text, follow the link, and you return to this page. |
| The page loads unstyled, or sign-in does nothing | Check the browser console for a Content-Security-Policy violation: the directive and the blocked address are named. A proxy that injects scripts or styles will be blocked by design. |

## Tests

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release                              # bUnit components and the host tests (fake sign-in, stub API)
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-class "*Admin*" --filter-class "*ShellHostTests"
```

bUnit notes that cost time once: JS interop runs in strict mode, so set up `./js/dialog.js`, `./js/shortcuts.js`, `./js/preferences.js`, `./js/tz.js`, `./js/palette.js` and `./js/menu.js` (the `AdminComponentTest` base class does; a test that copies an API key sets up
`./js/clipboard.js` itself); the settings pages derive from `AdminPageTest`, which gives them a session (an Admin unless the test calls `AsAgent()` before it renders), what `NoAccessPage` needs and a host environment;
the element reference of an element is blanked after the next
render, so read it before the action; services cannot be added after the first render; `InputFile` has no `MaxAllowedSize` and bUnit does not enforce stream limits, so the composer and the picture button check files themselves; `UploadFiles` returns only when the change handler has finished, so a test that holds an upload open
(a pending task) starts the pick on its own thread (`Task.Run`) and awaits it after the answer is released.

## Known gaps in 07a

Recorded in D-040 and tracked for later phases: no counts in the erase and delete dialogs and no list of a ticket's follow-ups (the API offers neither); the requester card has no ticket count or first-seen date;
times are shown in UTC (07c shows them in the browser's zone); "Apply my change again" after a conflict is not built (Reload only); a lost circuit loses an unsent draft (the leave-warning covers a reload);
no knowledge-base article picker (added in 08); no presence or live updates (added in 10b); no command palette (added in 07c); the manual sign-in check against a real Authentik is outstanding.

## Decisions that changed during 07b

- **Logo addresses are stored normalized.** The API saves the logo as `uri.AbsoluteUri` (so `HTTPS://Example.com` comes back as `https://example.com/`), and it rejects an address that holds a Unicode format character (such as a right-to-left override) as well as whitespace and control characters. The editor shows the saved form after a save.
- **`ConfirmDialog` has `Dismissable`** (default true). The show-once API key dialog sets it to false until "I have stored this key" is ticked: Esc and a stray native close cannot close the browser's dialog, Esc still reaches the owner so it can explain, and when the browser closed the dialog anyway the component resyncs and shows it again.
- **`AgentSession.ReloadAsync` keeps the session Ready** when the API answers with a server error, a timeout or cannot be reached, so a failed refresh (a transient failure while the page is open) never turns a working page into an error screen. A refusal (403, for example after the admin deactivates their own account) or an inactive account still changes the state to no access.
- **No factory client has HttpClient logging.** The default `IHttpClientFactory` logging writes each request header, `Authorization` included, at Trace (event 102), and the factory's own default leaks header values in its structured state. (07b also said an OTLP exporter's `x-api-key` reached the log through the factory; 07c could not reproduce that, and the OTLP leak tests remain as an end-to-end guard.) Every host (Api, Worker, Admin and Portal) removes the logging handlers through one shared registration in `TechStrap.Hosting` (`AddTechStrapHttpClientDefaults`, 07c, D-042), so this is a default for every client the factory creates (the two API clients and any future one); only the logging handlers are removed, the auth, forwarded-IP and retry handlers are untouched. `ApiClientRegistration` still calls `RemoveAllLoggers()` on the two API clients. `AdminLeakTests` runs with every Serilog level at Verbose and fails if a token, a new API key or an OTLP header secret reaches any log line.

## Known gaps in 07b

Recorded in D-041 and tracked for later phases:

- Roles cannot be changed here (use the identity provider's groups), and there is no invite: an agent appears by signing in.
- The logo is an address, not an upload. The preview loads it from the address you typed, so a slow host shows a slow preview.
- The editors do not warn about unsaved changes when you leave the page; a conflict banner keeps your edits and offers Reload, but "Apply my change again" is not built (as in 07a).
- Ticket counts on the tags page, and the count in the delete dialog, are read when the list loads. A tag that gains tickets meanwhile is caught by the API (409 `tag-in-use`) and shown again with its new count.
- The audit log filters by what changed and by who only: the API has no event-type or date filter. Events show ids, slugs, prefixes and counts and never names, because a payload carries none.
- My settings has the new-ticket alerts only: the UX brief's assignment-alert switch has no API field yet.
- Discard on a failed email takes no reason (the API has none). The failed-emails count in the navigation is read when the shell loads and after you act on the page; it is not live: the hub does not drive it (see Known gaps in 10b).
- Times were shown in UTC, as in 07a, until 07c.
- The OpenAPI security schemes, the CSP and the responsive and accessibility pass were done in 07c.

## Known gaps in 07c

Recorded in D-042 and tracked for later phases:

- Authentik is not set up (owner action 7), so P07-T02 stays open, and the checks that need a signed-in session have not been run against a real provider: the keyboard walk, the axe run, the CSP check of the sign-in and sign-out redirects. The checklist above is the owner's.
- **A stored logo must be treated as untrusted everywhere.** A product logo stored before 07b was never validated, and because an unchanged stored logo is accepted when a product is saved (`ProductBranding.CreateForUpdate`), any stored `LogoPath` may still be a relative path or another scheme. Every renderer must check it again: the Portal (PHASE-09) and the email renderer, which accepts only `https://`.
- The queue's search text stays in the address (so a view can be bookmarked). It is masked in Sentry (`search` and `q`), but it is in the browser history and in the reverse proxy's access log.
- The time zone comes from the browser only. A second device or browser shows its own zone, and there is no way to choose another one.
- After a mid-session 401 the circuit stays expired for good; a sign-in in another tab does not revive it (see above).
- The palette has navigation and the four ticket commands. "Open a ticket by number", "Cycle theme" and the other commands of the UX brief are not built.
- The Compose smoke is by hand (or the manual workflow), not on every pull request, because it builds four images.

## Known gaps in 08

Recorded in D-044 and tracked for later phases:

- Pictures and toolbar snippets are added at the end of the text, not at the caret, and there is no paste or drag-and-drop of a picture (Blazor cannot read the caret without script). The alt text is the file name; the Markdown can be edited after.
- There is no revision history, no hard delete of an article (archiving is the removal), no "create an article from this ticket" and no clean-up of pictures that no article uses.
- The ticket timeline lists the titles of the linked articles as plain text, not as links: the linked-article DTO has no portal address.
- "View on portal" for a shared article uses the first active product by name. The canonical address of a shared article under several products is the portal's decision (PHASE-09).
- The CSP already lets the Admin draw an API picture in Development (`img-src` has `http://localhost:*` and `http://127.0.0.1:*` on any port) and in Production over https; a plain-http `TECHSTRAP_API_PUBLIC_URL` in Production would show no pictures in the preview. `KbImageCspTests` pins both.
- Pictures (API side, for the operator): an upload is checked by its real type (PNG, JPEG, GIF, WebP) and only the first 1024 bytes are scanned for markup; the defenses are the sniffed type, `X-Content-Type-Options: nosniff`, a CSP `sandbox` on the picture and serving it from the API's own origin. `/kb-images/{key}` (GET and HEAD) has no rate limit: rely on the proxy or CDN. Picture addresses are built from `TECHSTRAP_API_PUBLIC_URL` (the local compose sets `http://localhost:8080`); a path prefix in that address needs the proxy to strip it before the API.
- Public API (for the portal): every text field of the public knowledge base DTOs, snippets included, is **plain text**; only `Html` is HTML, so the portal must HTML-encode the rest. A malformed slug or key answers an empty result or 404, never a 500. The agent `/api/kb` responses carry `Cache-Control: no-store`, and the JSON body limit is 2 MiB. Every mutating knowledge base call checks that the agent is still active.
- The manual checks (write and publish an article with a picture; the picture loads through the API's public address behind the proxy; a linked article appears in a reply email) are the owner's.

## Live updates (10b)

What an agent sees (PHASE-10, D-046). Nothing here moves a page by itself: a change only raises a banner, and the agent decides when to take it.

- **Indicator.** A small status line above the status bar says "Live", "Connecting", "Reconnecting" or "Offline". It is a polite live region, so a screen reader hears a change without being interrupted, and the state is always written, never color alone. It is drawn once the agent is admitted, and it is the only place the connection starts.
- **Queue.** When another agent, the customer or the Worker (auto-close) changes any ticket, or the connection comes back after a gap, one banner "Queue updated – refresh" appears after a one-second window, however many changes there were. Nothing reloads or reorders until the banner is clicked; the click is the ordinary Refresh with the current filters. Your own changes raise nothing.
- **Ticket.** A change to the open ticket by someone else raises "New activity – refresh". Until the click the page keeps what it shows, including the row version, so a send in between still gets "This ticket changed since you opened it" and the draft is kept. The click reloads the timeline, the status and the row version. Your own changes (this tab or another) raise nothing; a send from another tab of yours still meets the 409.
- **Presence.** "Ada Admin is viewing" or "is replying" above the timeline: the other agents on the ticket, by their agent name (the email when there is none), never the customer-facing name and never yourself. "Replying" is sent while you type (at most every 4 seconds); it ends when you leave the box, send, empty the box, open another ticket or close the tab, and it lapses on its own after 10 seconds.
- **Reconnect.** While the session lives the Admin keeps trying (at once, then after 2, 5 and 10 seconds, then every 30), asks for a fresh token each time, joins the open ticket again and raises both banners (notifications of the gap are gone). It stops, showing "Offline", when the session has lapsed, no token is available (the agent is not signed out; only the live connection stays off), or the hub refuses the connection for good.
- **A live failure never reaches a page.** A hub, token or network failure is logged (the exception type only, never a token or a message) and shows as "Reconnecting" or "Offline"; the page, the composer and the draft work as before.

### The kill switch

`LIVEUPDATES__ENABLED` (`LiveUpdates:Enabled`, default `true`) in `.env.admin` or `.env.local`. `false`: no connection is ever opened and no indicator, banner or presence bar is drawn; the Admin behaves exactly as it did before PHASE-10. Restart the Admin to change it. Compose does not set it, so the env file is the one place; the numbers (backoff, the one-second window, the 4-second hint) are constants.

### Manual check against a real identity provider (owner)

Two browsers, two agents (A and B), the stack running with the Worker.

1. A and B open the queue: both show "Live". B changes a ticket's status: A sees "Queue updated – refresh" within about two seconds and B does not. Clicking it reloads A's queue with A's filters.
2. A and B open the same ticket: each sees the other as viewing. B types: A sees "B is replying" within about two seconds, and it goes when B leaves the box, sends or closes the tab.
3. A types a draft; B replies. A sees "New activity – refresh" and the draft is untouched. A sends before clicking: "This ticket changed since you opened it", the draft is kept; the banner then shows B's reply.
4. Worker auto-close: with the seed data (`TECHSTRAP_SEED_DEV_DATA=true`, `docs/development/DEV-DATA.md`), make a Solved ticket whose `solved_at` is older than the auto-close days (the smallest is `TECHSTRAP_AUTOCLOSE_DAYS=1`, so backdate it in the database: `UPDATE tickets SET solved_at = now() - interval '2 days' WHERE number = '<the ticket number>' AND status = 'Solved';`), start the Worker with `AUTOCLOSE__INTERVALMINUTES=1`: within a minute A's open queue shows the banner once, and the ticket is Closed.
5. Stop the Api for a minute: the indicator says "Reconnecting"; start it again: "Live", and both banners appear.
6. Set `LIVEUPDATES__ENABLED=false` and restart the Admin: no indicator, no banner, no presence bar, and no request to `/hubs/tickets` in the Api log.

The automated end-to-end test (`AdminLiveClientHostTests`) runs the Worker's auto-close handler, NOTIFY, the Api's listener and hub and the Admin's real client in one process; the compose smoke script is unchanged.

## Known gaps in 10b

- The connection is per circuit (per open tab): two tabs are two connections. The browser tests that would prove "one connection per circuit" in a real browser do not exist (Playwright is deferred); it is proved by the client's start guard, the single starter and the prerender test.
- After a reconnect both banners are raised even when nothing changed, because notifications are at most once and the gap cannot be told from a quiet minute.
- A "replying" hint that is not refreshed within 10 seconds is shown as "viewing" by a timer in the page; the hub does not push the lapse.
- The reconnect schedule has no jitter: after an Api restart every open tab retries on the same schedule.
- A hub that ends because the session ended is not reported to the session: the indicator says "Offline" and the session-expired banner appears with the next call to the API.
- The failed-emails badge in the navigation is still read when the shell loads and after an action; it is not driven by the hub.
- Erasing a requester publishes nothing, so an open ticket keeps its old text until the next load (D-046, known limit).
- A deactivated agent's already-open connection keeps receiving changes until its token expires (D-046, known limit).
- Presence and the in-process publish are for one Api instance (D-007).
- A live change that arrives while the page is reloading is not lost: the banner stays, but a reload that fails keeps the banner too.
- A change made before the live connection is first established (on either page), or while a ticket's first load runs, raises no banner (on the ticket a send still gets the 409; the queue shows it at the next refresh).
- When the hub refuses the connection for good (it closes it with no reconnect allowed), the indicator stays "Offline" until the page is reloaded; the client does not restart on its own yet.
