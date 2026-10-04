# Admin app (agents)

`TechStrap.Admin` is the Blazor Server app agents work in. It never touches the database: every screen reads and writes through the TechStrap API as the
signed-in agent. This page is for people who run it and people who extend it. What agents can do to a ticket through the API is in
[TICKET-OPERATIONS.md](TICKET-OPERATIONS.md); how the API checks tokens is in [AGENT-AUTHENTICATION.md](../self-hosting/AGENT-AUTHENTICATION.md).

PHASE-07 is delivered in three pull requests. **07a**: sign-in, the shell, the queue, ticket detail, the reply composer, the sidebar, spam, delete and
erase. **07b**: settings (products and their branding, API keys, agents, tags, My settings with the public display name), failed emails and the audit log.
**07c**: polish, the command palette, compose and architecture rules, CSP. This page describes the app as it is once 07b is merged; the 07b parts say so.

## Run it locally

You need the API running (see [TICKET-OPERATIONS.md](TICKET-OPERATIONS.md) and the root README) and an OpenID Connect provider. Without a provider the Admin still
starts if you give it placeholder values, but signing in fails: that is expected until an identity provider exists (see [Authentik setup](#authentik-setup-note)).

```bash
cp src/TechStrap.Admin/.env.example src/TechStrap.Admin/.env.local     # then edit the values below
dotnet run --project src/TechStrap.Admin --urls http://localhost:8081
```

`.env.local` is read in Development only and is git-ignored. With Docker Compose the Admin listens on `http://127.0.0.1:8081`. Compose reads the three provider
values from the root `.env` (not from `src/TechStrap.Admin/.env.local`, whose `AUTH__*` keys would clash with the compose ones):

```bash
OIDC_AUTHORITY=https://auth.example.com/application/o/techstrap-admin/
OIDC_ADMIN_CLIENT_ID=techstrap-admin
OIDC_ADMIN_CLIENT_SECRET=...
```

Without them the container starts on placeholder values and the sign-in page works, but the redirect to the provider fails.

### Configuration

The Admin refuses to start with a message that names the missing variable. Values are validated at start and read lazily.

| Key | Required | Default | Meaning |
| --- | --- | --- | --- |
| `API__BASEURL` | yes | | Absolute URL of the TechStrap API, for example `http://localhost:8080/`. |
| `API__TIMEOUTSECONDS` | no | 30 | Timeout of one API call (1 to 300). |
| `AUTH__AUTHORITY` | yes | | The provider's issuer URL (https outside Development), with its trailing slash. |
| `AUTH__CLIENTID` | yes | | The OIDC client id of the Admin. |
| `AUTH__CLIENTSECRET` | yes | | The client secret. The Admin client is confidential. |
| `AUTH__SCOPES__0`, `AUTH__SCOPES__1`, ... | no | `openid profile email offline_access` | Must keep `openid` and `offline_access` (the API token is refreshed with the refresh token). |
| `TECHSTRAP_AGENT_GROUP` | no | `techstrap-agents` | Same key and default as the API. The API decides who has access; the Admin only shows what the API allows. |
| `TECHSTRAP_ADMIN_GROUP` | no | `techstrap-admins` | Same. |
| `TECHSTRAP_GROUP_CLAIM_TYPE` | no | `groups` | Same. |
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
   at most twice, with a short backoff, on a transport error, a timeout, 408, 502, 503 or 504 (never on 500, and there is no circuit breaker); **writes are never retried** (a retried reply would send twice).
   Tokens: sign-in uses `SaveTokens = true` and there is no session store, so the access, refresh and id tokens are kept in the **encrypted auth cookie** (the browser holds
   that cookie, not the tokens in readable form), and they are also copied to the server-side token cache that the circuit's HTTP clients read. They are never rendered, put in a URL or logged.

### Roles

| What | Agent | Admin |
| --- | --- | --- |
| Queue, ticket detail, reply, note, status, assignee, priority, product, tags | yes | yes |
| Mark as spam, Not spam | yes | yes |
| Delete a ticket, erase a requester | no (not rendered) | yes, with a typed confirmation |
| My settings (email alerts, keyboard shortcuts, theme, public display name) | yes | yes |
| Products, branding, API keys | no (page-level no-access) | yes |
| Agents: see the list, activate, deactivate | no | yes |
| Tags: create, rename, recolour, delete | no | yes |
| Audit log, failed emails (retry, discard) | no | yes |

**Roles are read-only here (D-041).** The agents page shows each role as a badge, with the note "Roles come from your identity provider's groups." An admin can only activate or deactivate an
agent. To make someone an admin, or an agent, change their group in the identity provider; the API reads it at their next sign-in.

## Authentik setup note

Authentik is not set up yet, so none of this has been exercised against a live provider. The tests use a fake `Test` authentication scheme and a stub API. When you set
Authentik up, configure the following; any OIDC provider that supports the same features works.

1. **Create an OAuth2/OpenID provider and an application** (for example slug `techstrap-admin`).
   - Client type: **confidential**, authorization **code** flow with **PKCE** (the Admin starts it with PKCE). Note the client id and the secret: they are `AUTH__CLIENTID` and `AUTH__CLIENTSECRET`.
   - Redirect URIs (strict): `https://<admin host>/signin-oidc`. For local development also `http://localhost:8081/signin-oidc`.
   - Post-logout redirect URIs: `https://<admin host>/signout-callback-oidc` (and `http://localhost:8081/signout-callback-oidc`).
   - **Front-channel and back-channel (remote) sign-out are not used**: the Admin disables `RemoteSignOutPath`, so do not configure a logout URI that points at the Admin. Sign-out is started by the Admin itself (a POST to `/signout`), and the provider sends the browser back to `/signout-callback-oidc`.
   - The authority is the provider's issuer URL, which ends in a slash: `https://auth.example.com/application/o/techstrap-admin/`.
2. **Scopes.** Select `openid`, `profile`, `email` and **`offline_access`** (Authentik's "offline_access" scope mapping). Without `offline_access` there is no refresh token and an agent's API
   calls start failing when the access token expires (Authentik's default is minutes). Set the refresh token validity to at least a working day.
3. **Groups claim.** Create the groups named by `TECHSTRAP_AGENT_GROUP` and `TECHSTRAP_ADMIN_GROUP` (defaults `techstrap-agents`, `techstrap-admins`) and add people to them. The default
   `profile` scope mapping in Authentik emits a `groups` claim with the group names, which matches `TECHSTRAP_GROUP_CLAIM_TYPE=groups`. An Admin does not also need the agent group.
4. **The token the API sees.** The Admin sends the **access token** to the API, and the API validates it (`Authentication__JwtBearer__Authority`, `__Audiences__0`; see
   [AGENT-AUTHENTICATION.md](../self-hosting/AGENT-AUTHENTICATION.md)). So the access token must carry `sub`, `email`, the groups claim, and an audience equal to the API's configured
   audience. In Authentik the audience of an access token is the client id of the provider that issued it, so the simplest setup is to set the API audience to the Admin's client id.
   `sub` and `email` are **required**: the API matches the agent by them, and without `email` the no-access page says so. Decode a real access token at the first sign-in and check those four claims (this is not verified yet).
5. **Local compose uses placeholder values.** Without `OIDC_AUTHORITY`, `OIDC_ADMIN_CLIENT_ID` and `OIDC_ADMIN_CLIENT_SECRET` in the root `.env`, the Admin container starts on `https://authentik.invalid/...`, the sign-in page renders, and sign-in fails. That is expected until Authentik exists.
6. **Behind a reverse proxy**, set the trusted proxy keys so the Admin builds `https` redirect URIs, and make sure the provider can be reached from the Admin container at the authority URL.

If sign-in loops or the no-access page shows "not in the agent group" for someone who is, check in this order: the groups claim name, whether the claim is in the **access** token (the API
reads that one), and `TECHSTRAP_AGENT_GROUP` on **both** the API and the Admin.

## Where things live

```text
src/TechStrap.Admin/
  Auth/           cookie + OIDC wiring, /signin and /signout endpoints, AgentSession (the API's answer to /me)
  Clients/        ApiConnection and the typed clients (IAgentsClient, IProductsClient, ITagsClient, ITicketsClient, IRequestersClient, IAdminEventsClient, IDeadLettersClient), the /attachments/{id} pass-through
  Components/
    Ui/           reusable primitives: LoadingState, ErrorState, EmptyState, ConfirmDialog, PagerControl, TagChip, RelativeTime, StatusStamp, PriorityMark, AdminOnly (the admin page guard), AccentPreview, ...
    Layout/       MainLayout, NavMenu, StatusBar, AgentGate, ShortcutHelpDialog
    Pages/        sign-in landing, no-access, not found, error, style guide (Development only)
  Features/
    Shell/        StatusMessageService, ShortcutService, ShortcutCatalog
    Queue/        TicketQueuePage and its filter bar, tabs and row
    Tickets/      TicketDetailPage, presenter, timeline factory, ReplyComposer, TicketSidebar, TagPicker, TicketActions
    Settings/     Admin only. Products/ (ProductsPage, ProductEditorPage, ProductKeysPage, ApiKeysPanel, NewApiKeyDialog; the logo rule is `BrandingRules.IsAcceptableLogoUrl` in Contracts.Branding), Agents/ (AgentsPage), Tags/ (TagsPage),
                  Audit/ (AdminEventsPage, AdminEventSummaryFactory), EmailKinds
    Ops/          Admin only. DeadLetters/ (DeadLettersPage)
    Account/      My settings for every agent: NotificationPreferencesPage, PublicDisplayNameField, PublicNamePreview
  wwwroot/js/     ES modules only: dialog.js, shortcuts.js, queue.js, preferences.js (browser preferences and the theme), clipboard.js (copy the new API key) (no inline script anywhere)
  Styles/         SCSS partials over the brand tokens (docs/BRAND.md)
```

Rules the code follows (and the reviewers check):

- The Admin references **Contracts and Hosting only**: never Application, Infrastructure or Domain. DTOs are never renamed; view models are feature-local.
- **Components never inject `HttpClient`.** They inject the `I*Client` interfaces, which return `Result<T>`; the clients map the API's ProblemDetails (the error `type` is the code).
- Every read takes the component's `CancellationToken`. Writes (reply, note, sidebar changes, spam, delete, erase, and every settings write) deliberately pass `CancellationToken.None`: the server may commit a write after the agent has left the screen, so cancelling the call would only hide the outcome.
- Razor components are always public classes, so a type used as a component parameter is public (a view model cannot be `internal`).
- A component beyond a few plain parameters and one forwarder has a `.razor.cs`; a factory or presenter exists only for non-trivial assembly (`TicketDetailPresenter`, `TimelineEntryFactory`).
- The single `MarkupString` is the message body in `MessageBubble` (the API sanitises it). Everything else is encoded.
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
The command palette (Ctrl+K) arrives in 07c.

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
| `?` | Anywhere | Show the list |

## What an agent can do here (07a)

- **Queue**: six views (Unassigned is the default, then Mine, Open, Pending, All and the separate Spam view), counts per view (Spam muted), filters (product, status, priority, tag), search, 25 per page. Every filter is in
  the URL, so views can be bookmarked. The queue refreshes when you press Refresh; live updates arrive with PHASE-10.
- **Ticket**: one timeline of messages and changes (customer white, public reply canary, internal note pink with a dashed edge), the requester, metadata labelled **Untrusted** unless it came from a trusted key,
  attachments as downloads (served through the Admin, never inline), and a link to the parent of a follow-up.
- **Reply or note**: separate drafts per mode, files (up to 5, 10 MB each), Pending by default or "Send and solve". A failed send, or a conflict, never loses the text or the files.
- **Change a ticket**: status, assignee, priority, product, tags. Each change shows "Saving...", then the screen shows what the API accepted. If someone else changed the ticket first you see
  "This ticket changed since you opened it": press Reload, your draft is kept.
- **Spam, delete, erase**: Mark as spam asks first. Delete and erase (Admins) need the ticket number or the requester's email typed, and say so plainly; erase covers every ticket from that requester.

## What an admin can do here (07b)

Everything in this section needs the Admin role. The API refuses the same calls to anyone else.

- **Products** (`/settings/products`, `/settings/products/new`, `/settings/products/{id}`): a list of every product, active or not, and an editor for the name and the branding (display name, logo, accent colour,
  email from address and reply-to). The key and the ticket number prefix are permanent: they are asked for when the product is created and shown read-only afterwards. The logo is an **address, not an upload**:
  only a full `https://` address is accepted (`http://localhost` too, in Development), a blank value means no logo, and the API refuses the same addresses, so `javascript:`, `data:`, relative paths and other
  schemes never reach an email or the portal. The accent colour is checked with the same pattern as the API (`#RRGGBB`); a low-contrast colour only shows a note, because TechStrap darkens it wherever it is used
  for text. A live preview shows the name, the logo and the accent. Saving always sends the product's current Active setting and the version the editor was opened on: if someone else saved first you see "This
  product changed since you opened it", your edits stay on screen, and Reload shows the saved version. A failed or lost save never clears the form.
- **API keys** (`/settings/products/{id}/keys`): the keys of a product by label, kind (a Trusted or a Public badge, always the word), prefix, created and last-used time, and status. Creating one asks for the kind
  and an optional label, then shows the key **once**, in a dialog with a Copy button. The dialog cannot be closed (Cancel and Esc do nothing) until you tick "I have stored this key"; closing it clears the key from
  the page, and nothing can show it again. If the answer to a create is lost, the page says the key may exist but its secret cannot be shown: revoke it and create another. Revoking asks first ("Apps using this key
  will stop working.") and a revoked key stays in the list, marked Revoked.
- **Agents** (`/settings/agents`): everyone who has signed in, with role badge, active or not, and last seen, 25 to a page. Activate needs no confirmation; Deactivate asks first. Deactivating the only active admin
  is refused by the API, and the page shows its message inside the dialog. Deactivating yourself warns you, and then shows the no-access page.
- **Tags** (`/settings/tags`): every tag with a **ticket count**, create (the slug follows the name until you edit it), rename and recolour in the row, and delete. Deleting an unused tag asks first. Deleting a tag
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

## Known limits

- **Pages render twice.** Blazor Server prerenders each page on the server, then renders it again in the circuit, so every page loads its data twice (two calls to the API for the same screen). The first render is what a plain HTTP request sees, which is why the host tests can read the data in the HTML. Making the second load reuse the first (persistent component state) is not done in 07a.
- **Attachments must be re-attached after leaving a ticket.** Chosen files are held by the browser for the composer on screen. The browser only lets the page read a file while the file input that produced it is on screen, so leaving the ticket drops them and the composer tells you to attach them again. The typed text is kept for the life of the circuit; the files are not. A failed send or a conflict on the same screen keeps both.

- **The read client retries but never breaks the circuit.** A read is tried up to three times in all (250 ms base backoff with jitter) on transport errors, timeouts, 408, 502, 503 and 504. There is no circuit breaker: one named read client is shared by every agent, and a breaker opened by one failing endpoint would lock everyone out. Under a real outage every call waits out its retries (roughly a second) before it fails.
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
| A product's logo is refused | Only a full https:// address is accepted (and http://localhost in Development). There is no upload. |
| The theme does not change | The choice is kept in this browser: private windows and cleared site data forget it, and Auto follows the device. |

## Tests

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release                              # bUnit components and the host tests (fake sign-in, stub API)
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-class "*Admin*" --filter-class "*ShellHostTests"
```

bUnit notes that cost time once: JS interop runs in strict mode, so set up `./js/dialog.js`, `./js/shortcuts.js` and `./js/preferences.js` (the `AdminComponentTest` base class does; a test that copies an API key sets up
`./js/clipboard.js` itself); the settings pages derive from `AdminPageTest`, which gives them a session (an Admin unless the test calls `AsAgent()` before it renders), what `NoAccessPage` needs and a host environment;
the element reference of an element is blanked after the next
render, so read it before the action; services cannot be added after the first render; `InputFile` has no `MaxAllowedSize` and bUnit does not enforce stream limits, so the composer checks files itself.

## Known gaps in 07a

Recorded in D-040 and tracked for later phases: no counts in the erase and delete dialogs and no list of a ticket's follow-ups (the API offers neither); the requester card has no ticket count or first-seen date;
times are shown in UTC (the agent's zone needs the browser); "Apply my change again" after a conflict is not built (Reload only); a lost circuit loses an unsent draft (the leave-warning covers a reload);
no knowledge-base article picker (PHASE-08); no presence or live updates (PHASE-10); no command palette (07c); the manual sign-in check against a real Authentik is outstanding.

## Decisions that changed during 07b

- **Logo addresses are stored normalised.** The API saves the logo as `uri.AbsoluteUri` (so `HTTPS://Example.com` comes back as `https://example.com/`), and it rejects an address that holds a Unicode format character (such as a right-to-left override) as well as whitespace and control characters. The editor shows the saved form after a save.
- **`ConfirmDialog` has `Dismissable`** (default true). The show-once API key dialog sets it to false until "I have stored this key" is ticked: Esc and a stray native close cannot close the browser's dialog, Esc still reaches the owner so it can explain, and when the browser closed the dialog anyway the component resyncs and shows it again.
- **`AgentSession.ReloadAsync` keeps the session Ready** when the API answers with a server error, a timeout or cannot be reached, so a failed refresh (a transient failure while the page is open) never turns a working page into an error screen. A refusal (403, for example after the admin deactivates their own account) or an inactive account still changes the state to no access.
- **The API clients have no HttpClient logging.** The default `IHttpClientFactory` logging writes each request header, `Authorization` included, at Trace (event 102). Both named API clients carry the agent's bearer token, so `ApiClientRegistration` calls `RemoveAllLoggers()` on them (the auth, forwarded-IP and retry handlers are untouched). `AdminLeakTests` runs with every Serilog level at Verbose and fails if a token or a new API key reaches any log line.

## Known gaps in 07b

Recorded in D-041 and tracked for later phases:

- Roles cannot be changed here (use the identity provider's groups), and there is no invite: an agent appears by signing in.
- The logo is an address, not an upload. The preview loads it from the address you typed, so a slow host shows a slow preview.
- The editors do not warn about unsaved changes when you leave the page; a conflict banner keeps your edits and offers Reload, but "Apply my change again" is not built (as in 07a).
- Ticket counts on the tags page, and the count in the delete dialog, are read when the list loads. A tag that gains tickets meanwhile is caught by the API (409 `tag-in-use`) and shown again with its new count.
- The audit log filters by what changed and by who only: the API has no event-type or date filter. Events show ids, slugs, prefixes and counts and never names, because a payload carries none.
- My settings has the new-ticket alerts only: the UX brief's assignment-alert switch has no API field yet.
- Discard on a failed email takes no reason (the API has none). The failed-emails count in the navigation is read when the shell loads and after you act on the page; it is not live (PHASE-10).
- Times are shown in UTC, as in 07a.
- The OpenAPI bearer scheme, the CSP and the responsive and accessibility pass remain 07c.
