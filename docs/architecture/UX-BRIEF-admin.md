# UX Brief: TechStrap Admin (agent app)

**Handoff audience:** Claude Design, UX designer, and implementation team

> This is a **designer handoff**, not an implementation ticket. It states what
> the agent app must do and feel like; layout and component design are the
> designer's to propose within the chosen direction. **Visual direction is
> decided:** Carbon Copy v2 (owner decision, 2026-10-02), produced in
> [PHASE-02 (brand and UX)](PHASE-02-brand-and-ux.md).
> [`docs/BRAND.md`](../BRAND.md) is the system of record for color, type,
> geometry, tokens and mascot usage; the reference mockup is
> [`docs/design/mockups/direction-carbon-copy-v2.html`](../design/mockups/direction-carbon-copy-v2.html).
> See [Visual Direction](#visual-direction). No significant admin UI is built
> (PHASE-07) until BRAND.md is final. Where this brief and BRAND.md disagree on
> a visual value, BRAND.md wins; on behavior, content or accessibility, this
> brief wins and BRAND.md is corrected. Where it says "Bootstrap prefers" it is
> a starting point, not a style decision.

## Product Context

- **Application purpose:** `TechStrap.Admin` is the Blazor Server agent app of
  TechStrap, a lightweight self-hosted helpdesk for one company supporting many
  products. Agents triage, reply to, tag, assign and solve tickets across all
  products; admins also configure products, API keys, agents, tags, the
  knowledge base (KB) and operational health (dead letters, audit log).
- **Primary business/user outcome:** an agent clears a queue quickly and
  accurately: find the ticket that needs them, read the full conversation,
  respond to the customer (or leave an internal note) without ever confusing the
  two, and move on. Secondary: an admin can safely set up a new product and
  integration in minutes.
- **Related architecture artifact:** [02-ARCHITECTURE.md](02-ARCHITECTURE.md)
  (application boundary and presentation tables), [01-REQUIREMENTS.md](01-REQUIREMENTS.md),
  [PHASE-07-admin-app.md](PHASE-07-admin-app.md),
  [PHASE-08-knowledge-base.md](PHASE-08-knowledge-base.md),
  [PHASE-10-live-updates.md](PHASE-10-live-updates.md). Sibling brief:
  [UX-BRIEF-portal.md](UX-BRIEF-portal.md).
- **Design constraints:**
  - Blazor Server (interactive server rendering), Bootstrap 5 SCSS via libman and
    AspNetCore.SassCompiler; no compiled CSS committed.
  - The admin never touches the database; every screen reads and writes through
    typed API clients over `TechStrap.Contracts` DTOs. Errors arrive as
    ProblemDetails; the UI maps them to the states below.
  - Authentication is OIDC; access requires the configured agent group claim.
    Admin-only screens require the admin group claim.
  - One company, one installation: no tenant switcher, no per-customer branding
    in the admin. Product identity (name, logo, accent) appears only as a
    recognizable product chip so agents can tell products apart at a glance. The
    chip must not rely on color alone.
  - Desktop-first (agents work at a desk), but must remain usable on a tablet and
    degrade to a readable single-column phone layout for quick checks.
  - English only in v1 (an i18n seam exists; no translations to design).
  - Live updates (SignalR) are an enhancement layer: every screen must be fully
    correct without them.
  - Statuses are fixed: `New`, `Open`, `Pending`, `Solved`, `Closed`. A `Spam`
    flag is orthogonal. Priority levels are the domain's (Assumption: Low,
    Normal, High, Urgent). Closed tickets are read-only.

## Users and Personas

- **Persona: Agent** (support person; the daily user)
  - **Goals:** see what needs attention now; reply fast and accurately; reuse KB
    knowledge in replies; collaborate through internal notes; never reply to a
    customer by accident from an internal note; avoid doubling up on a ticket a
    colleague is already handling.
  - **Pain points:** queues that bury new work; losing a long reply to a conflict
    or a dropped connection; unclear whether a message is customer-visible; not
    knowing a colleague is mid-reply; slow navigation that needs the mouse for
    every step; context spread across products.
  - **Access/permissions:** signed in via OIDC with the agent group claim. Can
    view and work all tickets, KB articles/categories, tags on tickets, and
    their own notification preferences. Can mark spam. Cannot delete a ticket or
    erase a requester: those destructive privacy actions are Admin only (D-022).
- **Persona: Admin** (usually the owner or a lead; occasional, high-stakes)
  - **Goals:** onboard a product (name, branding, from-address), mint and revoke
    API keys correctly (trusted vs public), activate or deactivate agents (roles come from the IdP groups, D-029 and D-041),
    curate tags, watch operational health (dead-lettered emails), audit who
    changed configuration.
  - **Pain points:** fear of exposing a secret or choosing the wrong key kind;
    unclear blast radius of a revoke or delete; no record of who changed what.
  - **Access/permissions:** admin group claim (all Agent abilities plus the
    screens marked Admin below). The first admin is whoever is in the IdP
    admin group (D-029); an unauthorized signed-in user sees a clear
    "no access" screen, never an empty app.

## Key User Flows

### Triage the queue and take a ticket

- **Starting state:** agent signed in, on the queue (default view: Unassigned, or
  Mine if the agent has tickets awaiting action; Assumption: remembers last view).
- **Trigger:** start of shift, or a live "new ticket" cue.
- **Steps:**
  1. [ ] Scan the Unassigned view; rows show ticket number, product chip,
     subject, requester, status, priority, assignee, last activity.
  2. [ ] Narrow with filters (product, status, tag, priority) or search.
  3. [ ] Open a ticket (click or keyboard), read the timeline.
  4. [ ] Assign to self (single action, shortcut available); status moves
     `New` to `Open` on first agent action.
- **Success state:** the ticket appears in Mine; the agent is on its detail view.
- **Failure, loading, and empty states:** skeleton rows while loading; empty
  Unassigned = positive "Nothing waiting" message; empty filtered result = "No
  tickets match" with one-click clear-filters; API failure = inline error with
  retry, previous list kept visible if stale data exists. A truly empty
  Unassigned, Mine or Open view (no filters, no search) shows the **All caught
  up** brand moment (see Brand moments); a filtered-empty result never does.

### Reply publicly to a customer

- **Starting state:** ticket detail open, not Closed.
- **Trigger:** agent chooses the **Public reply** composer tab (default tab on a
  ticket awaiting an agent response).
- **Steps:**
  1. [ ] Write the reply (plain text or limited formatting; Assumption: plain
     text with line breaks in v1, sanitized server-side).
  2. [ ] Optionally attach files (same type/size rules as the portal) and link
     KB articles through the article linker (search published articles, pick
     one or more; they render as links in the email and customer view).
  3. [ ] Optionally set the status to apply on send (default Pending; Solved
     available as "Send and solve").
  4. [ ] Send. The message appears in the timeline as a Public agent message.
- **Success state:** reply on the timeline, status updated, a quiet confirmation
  ("Sent to requester@example.com"). The composer clears; the draft is discarded.
- **Failure, loading, and empty states:** send button shows progress and is
  disabled to prevent double-send; on failure the text is **preserved** with an
  inline error and retry; concurrency conflict follows the conflict flow below;
  an email that later fails delivery is surfaced via dead letters, not as a send
  error (email never fails the request).

### Leave an internal note

- **Starting state:** ticket detail open.
- **Trigger:** agent switches to the **Internal note** composer tab.
- **Steps:**
  1. [ ] Composer visibly changes identity (see Interaction rules, "Composer
     distinction": pink dashed notched container, the persistent warning
     "INTERNAL: the customer will NOT see this note.", submit label "Add
     internal note").
  2. [ ] Write and submit.
- **Success state:** note on the timeline in the internal visual treatment; no
  email is sent; status does not change automatically.
- **Failure, loading, and empty states:** as public reply. The composer must never
  default to Internal when the last customer-visible message awaits a reply, nor
  silently switch tab after submit.

### Resolve a concurrency conflict

- **Starting state:** agent edits a ticket (changes a field, or composes) while
  another agent or the system changed it; the API rejects with a concurrency
  conflict.
- **Trigger:** the save or send returns the conflict ProblemDetails.
- **Steps:**
  1. [ ] The UI shows a non-blocking conflict banner on the ticket: what changed
     and by whom (from the event log), without losing the agent's unsent text.
  2. [ ] The ticket refreshes from the server; the agent's pending composer text
     and unsaved field choice are kept alongside.
  3. [ ] The agent re-applies (one click "Apply my change again") or discards.
- **Success state:** the change is saved against the fresh version; the timeline
  shows both agents' events in order.
- **Failure, loading, and empty states:** repeated conflicts show the same
  banner; text is never dropped. A ticket deleted meanwhile shows "This ticket no
  longer exists" with a link back to the queue.

### Know who else is on a ticket (presence) and see live changes

- **Starting state:** ticket detail or queue open, SignalR connected.
- **Trigger:** another agent opens the ticket, starts composing, or any ticket
  changes (new customer reply, status or assignment change, auto-close).
- **Steps:**
  1. [ ] A subtle hint near the title/composer reads "Sam is viewing" or "Sam is
     replying" (never a blocking lock).
  2. [ ] New timeline events append without scroll jump; if the agent is reading
     older events, a "1 new message" chip appears instead of auto-scroll.
  3. [ ] The queue updates rows in place and highlights changed rows; if live
     sorting would move rows under the cursor, hold the order and offer "N updates
     (refresh)".
- **Success state:** agents avoid duplicate replies and see current state.
- **Failure, loading, and empty states:** if the connection drops, a discreet
  "Live updates paused, reconnecting" indicator shows and the app keeps working;
  on reconnect the view reloads silently. Presence is advisory: it never prevents
  an action.

### Onboard a product and integrate an app (Admin)

- **Starting state:** admin on Products.
- **Trigger:** a new product needs a portal and/or in-app support.
- **Steps:**
  1. [ ] Create the product: key (URL-safe, immutable once tickets exist), name,
     branding (display name, logo URL, accent color, email from-address, reply-to),
     active flag.
  2. [ ] Preview how the branding looks in the portal header and email (live
     preview with the entered accent color, including a contrast warning).
  3. [ ] Create an API key: choose the **kind** (Trusted or Public) with a plain
     explanation of each; name/label it.
  4. [ ] The secret is shown **once** in a modal with copy button and an
     explicit "I have stored this key" confirmation before it can be dismissed.
- **Success state:** product active; key listed by label, kind, created date,
  last used, prefix only; secret never retrievable again.
- **Failure, loading, and empty states:** duplicate key or invalid logo URL
  surface at the field; a product with no keys shows an empty state explaining
  the two key kinds; revoke is confirmed (see destructive actions).

### Write and publish a KB article

- **Starting state:** admin or agent on KB.
- **Trigger:** "New article". ("Create article from this ticket" is not in v1;
  see Handoff Notes, question 8.)
- **Steps:**
  1. [ ] Set product (or Shared), category, title, slug (auto, editable),
     summary.
  2. [ ] Write Markdown with side-by-side (or tabbed on narrow screens) preview.
  3. [ ] Upload images (drag-drop or paste); insert the Markdown link at the cursor
     with alt text prompted.
  4. [ ] Save draft; Publish; later Archive.
- **Success state:** article status chip updates; "View on portal" link appears
  when published.
- **Failure, loading, and empty states:** unsaved-changes guard on navigation;
  image over size/type limit rejected at selection with a clear reason; preview
  failures show inline without losing text; empty KB shows a first-article
  prompt.

### Handle a dead letter (Admin)

- **Starting state:** the dead letters screen shows emails that exhausted retries.
- **Trigger:** a nav badge count, or routine check.
- **Steps:**
  1. [ ] Open a row: recipient, template type, ticket link, last error, attempts,
     timestamps.
  2. [ ] **Retry** (re-queues) or **Discard** (confirmed; the API takes no reason).
- **Success state:** row leaves the list; an admin event is recorded.
- **Failure, loading, and empty states:** empty = "No failed emails" (positive).
  Retry that fails again reappears with an updated error.

### Erase a requester, mark spam, or delete a ticket

- **Starting state:** ticket detail (or requester context) open.
- **Trigger:** overflow/danger menu on the ticket (not on the primary toolbar).
- **Steps:**
  1. [ ] Choose **Mark as spam** (any Agent), **Delete ticket** or **Erase requester** (Admins only; the entries are absent for Agents, D-022).
  2. [ ] A confirmation dialog states exactly what will happen (spam: hides and
     flags; delete: permanent, removes messages and attachments; erase:
     anonymizes the requester across **all** their tickets and deletes their
     attachments), including counts ("12 tickets, 31 attachments").
  3. [ ] Irreversible actions require typing the ticket number or requester email.
  4. [ ] **Not spam** (D-024) is reversible and needs no dialog: choose it from the overflow menu or the Spam view row, or press `u`. The ticket leaves the Spam view, returns to its normal views with its status unchanged, and the status bar says "Restored ACME-142 from spam".
- **Success state:** confirmation toast; the agent lands on the queue; an event or
  admin event records who did it (without retaining the erased PII).
- **Failure, loading, and empty states:** failure leaves everything unchanged with
  an explanation; the dialog is never dismissed by Enter.

## Screen Inventory

Routes are an **Assumption** for the implementation team; designers need the
inventory, not the URLs.

- **Screen/route:** Queue `/queue/{view?}` (`/` opens the queue)
  - **Purpose:** find and prioritize work.
  - **Primary actions:** switch view (Unassigned, Mine, Open, Pending, All, and a separate **Spam** view in the rail, D-024; the first five exclude spam),
    filter (product, status, tag, priority), search (full-text over tickets),
    page, open ticket. Bulk actions are out of scope for v1 (Handoff Notes,
    question 9). The ledger anatomy follows the v2 mockup: selection marker,
    ticket number, subject with tag chips, product, requester, status stamp
    (straight, no animation), priority, assignee avatar, last activity; the
    keyboard layer below applies.
  - **Data/state:** `ListTicketsRequestHandler` results (paged); view, filters,
    search term and page held in the query string so views are linkable and
    survive refresh; counts per view (the Spam count is muted, never an alert); live row updates. The Spam view lists tickets with `is_spam = true`, rows keep the double-bordered "Spam?" stamp, **Not spam** (`u`) restores the selected row, and an empty Spam view reads "No spam" in plain text (no brand moment).
  - **Authorization:** Agent.
- **Screen/route:** Ticket detail `/tickets/{number}`
  - **Purpose:** full conversation and ticket control.
  - **Primary actions:** read timeline; public reply; internal note; link KB
    article; change status, assignee, priority, product, tags; mark spam, or Not spam (`u`) on a flagged ticket; delete and
    erase requester (Admin only, D-022); follow link to parent/follow-up ticket.
  - **Data/state:** `GetTicketRequestHandler` (detail plus timeline from
    `TicketEvent`: messages, status/assignment/priority/product/tag changes, in
    one chronological stream with internal notes visually distinct); concurrency
    token; presence; live events; draft text per ticket held in the circuit.
  - **Authorization:** Agent.
- **Screen/route:** Products `/settings/products`, `/settings/products/{id}`
  (branding), `/settings/products/{id}/keys` (API keys)
  - **Purpose:** configure products, branding and integrations.
  - **Primary actions:** create/edit product and branding, set the logo URL (https, with a preview), pick accent
    color (with contrast check), activate/deactivate, create/revoke API keys.
  - **Data/state:** product and key lists; secret held in memory only for the
    one-time reveal.
  - **Authorization:** Admin for management; the product list is Agent-readable
    for filters (D-022).
- **Screen/route:** Agents `/settings/agents`
  - **Purpose:** see who has access and what role they have.
  - **Primary actions:** deactivate/reactivate; the role is a read-only badge with the note "Roles come from your identity provider's groups." (D-029, D-041).
  - **Data/state:** agent list (name, email, role, active, last seen); agents appear
    by signing in (provisioned on first call); no manual invite. Agents read only
    the active-agent list (for assignment); this screen is Admin only (D-022).
  - **Authorization:** Admin; the API refuses to deactivate the last active
    admin (409 last-active-admin) and the screen shows its message inline (D-041).
- **Screen/route:** Tags `/settings/tags`
  - **Purpose:** curate the global tag set.
  - **Primary actions:** create, rename, recolor, delete (with usage count).
  - **Data/state:** tag list with ticket counts.
  - **Authorization:** Admin (Assumption: agents may apply but not manage tags).
- **Screen/route:** KB list `/kb`, editor `/kb/new` and `/kb/{id}`, and
  categories `/kb/categories` (route for categories is an Assumption; PHASE-08
  names `KbCategoriesPage` without a route)
  - **Purpose:** manage categories and articles.
  - **Primary actions:** filter by product/category/status/text; create
    category (inline CRUD on the categories page; delete blocked with an
    explanation when articles exist); create/edit/publish/archive article;
    Markdown editor with live preview; image upload; the article picker used by
    the reply composer lives on the ticket page (PHASE-08).
  - **Data/state:** article list (title, product or Shared, category, status,
    updated); editor state with dirty tracking; preview rendered server-side
    through `POST /api/kb/preview` (the same Markdown renderer as the portal, D-021).
  - **Authorization:** Agent for articles and for creating and editing categories; Admin to delete categories.
- **Screen/route:** Dead letters `/ops/dead-letters`
  - **Purpose:** resolve undelivered emails.
  - **Primary actions:** inspect, retry, discard.
  - **Data/state:** `ListDeadLettersRequestHandler`; nav badge count.
  - **Authorization:** Admin.
- **Screen/route:** Audit log `/settings/audit`
  - **Purpose:** who changed configuration (products, keys, agents, tags) and who ran privacy and operations actions (erase requester, delete ticket, dead-letter retry or discard).
  - **Primary actions:** filter by actor and subject type; read-only (the API has no date or event-type filter, D-041).
  - **Data/state:** `ListAdminEventsRequestHandler`, paged, newest first. Ticket
    history lives on the ticket timeline, not here.
  - **Authorization:** Admin.
- **Screen/route:** My settings `/account/notifications`
  - **Purpose:** notification, keyboard and theme preferences, and the name customers see.
  - **Primary actions:** per product, opt in/out of new-ticket email alerts;
    assignment alerts toggle (Assumption); a single "Keyboard shortcuts" toggle
    (single-key shortcuts on/off) and the theme choice (auto, light, dark); an optional **Public display name** text field (D-024) with a live preview line beneath it, "Customers see: Sam from Orbitly Support" (the first name of the agent's profile name by default; typing "Samantha" changes it to "Customers see: Samantha from Orbitly Support"; clearing the field restores the default). The preview uses an example product (the first active product; **Assumption**: a product picker when several exist). The field is optional, plain text, 60 characters at most, rejects `@`, saves on blur or Enter with inline confirmation. Helper text: "Customers never see your email address."
  - **Data/state:** `GetCurrentAgentRequestHandler` plus
    `UpdateNotificationPreferencesRequestHandler`; saves per toggle with inline
    confirmation. The display name saves through the new
    `UpdateMyProfileRequestHandler` (PHASE-04): the preferences handler is a
    per-product alert opt-in and does not fit.
  - **Authorization:** Agent.
- **Screen/route:** Shell (`MainLayout`, `NavMenu`, status bar, command palette)
  - **Purpose:** navigation frame, keyboard layer and feedback surface.
  - **Primary actions:** navigate; global search focus (`/`); open the command
    palette (`Ctrl+K`); read shortcut hints and transient confirmations in the
    status bar; theme choice (auto, light, dark); sign out.
  - **Data/state:** current agent, nav badges, connection state, theme and
    shortcut preferences (per user, in the browser).
  - **Authorization:** Authenticated.
- **Screen/route:** Agent sign-in (signed-out landing; OIDC challenge starts here)
  - **Purpose:** brand moment; the only screen shown before authentication.
  - **Primary actions:** "Sign in" (redirects to the OIDC provider; the mockup
    labels it "Sign in with Authentik", provider name comes from configuration).
  - **Data/state:** none; a signed-out or expired-session return shows the same
    window with a plain one-line reason ("Your session ended. Sign in again.").
    No password is ever entered in the app.
  - **Authorization:** Anonymous. Frame: Beige Box window with mascot (see Brand
    moments).
- **Screen/route:** No access (`NoAccessPage`)
  - **Purpose:** tell a signed-in user without the agent group claim why they
    see nothing, and what to do.
  - **Primary actions:** sign out / sign in as another user; copy the contact
    for the administrator if configured.
  - **Data/state:** current principal name and email.
  - **Authorization:** Signed in without the agent group claim. Plain screen:
    no mascot, no window frame, no humor (it blocks work).
- **Screen/route:** All caught up (queue empty state, not a route)
  - **Purpose:** positive empty state for a truly empty Unassigned, Mine or Open
    view.
  - **Primary actions:** "View open tickets" (or the next sensible view).
  - **Data/state:** the view's count is 0 with no filters and no search.
  - **Authorization:** Agent. Beige Box window with mascot (see Brand moments).
    Other empty views (Pending, All, filtered results, dead letters, tags,
    audit) use plain empty states without the window or mascot.
- **Screen/route:** Not found (404, any unknown route or missing ticket number)
  - **Purpose:** recover from a bad link.
  - **Primary actions:** back to the queue; open the command palette.
  - **Data/state:** none.
  - **Authorization:** Any. Beige Box window with mascot; body copy stays
    plain (see Brand moments).
- **Screen/route:** Unexpected error, session expired and Blazor reconnect
  overlay
  - **Purpose:** recoverable failure states for the whole app.
  - **Primary actions:** "Try again"; reload; sign in again.
  - **Data/state:** correlation id on the error page.
  - **Authorization:** Any. Plain: no mascot, no window frame, no humor.

## Razor Presentation Architecture

Follows _template `RAZOR_COMPONENT_ARCHITECTURE.md`. API contracts are `…Dto`,
`…Request`, `…Response` in `TechStrap.Contracts`, never ViewModels; ViewModels
are admin-only, feature-local and `internal`. All components that inject, have
lifecycle work, state, callbacks or JS interop are paired `.razor` /
`.razor.cs`. The final component list is set in PHASE-07 and recorded in
[02-ARCHITECTURE.md](02-ARCHITECTURE.md); this table is the UX-facing proposal.

| Feature/route | Component pair (`.razor` / `.razor.cs`) | ViewModel or direct model | Factory decision | State behavior |
| :------------ | :-------------------------------------- | :----------------------- | :--------------- | :------------- |
| Queue `/` | `TicketQueue` pair; `QueueFilters` pair; `TicketRow` inline (parameters plus one `EventCallback`) | `TicketRowViewModel` (status/priority labels, relative time, product chip); filter state as a feature-local record | None; simple mapping in code-behind | Page owns query-string state, loading, error and empty; live row updates merge into the held list; paging resets on filter change |
| Ticket detail `/tickets/{number}` | `TicketDetail` pair; `TicketTimeline` pair; `TimelineEntry` inline; `Composer` pair (Public/Internal tabs); `KbArticleLinker` pair; `TicketSidePanel` pair; `PresenceHint` pair; `ConflictBanner` inline | `TicketDetailViewModel`, `TimelineEntryViewModel` (merges messages and events into one stream, flags internal) | **Yes: `TicketDetailViewModelFactory`**, justified by multiple sources (ticket, timeline, presence, linked articles) and non-trivial event-to-timeline mapping | Detail page owns concurrency token, draft per composer tab, optimistic apply and conflict recovery; presence and live events arrive through an injected live-update abstraction (PHASE-10) and are disposed with the circuit |
| Products and branding | `ProductList` pair; `ProductEditor` pair; `BrandingPreview` pair; `ApiKeyList` pair; `CreateKeyDialog` pair; `SecretRevealDialog` pair | `ProductEditorViewModel` (form model with validation), `ApiKeyRowViewModel` | None | Page owns form and dirty state; the secret lives only in `SecretRevealDialog` memory and is nulled on close |
| Agents | `AgentList` pair | Direct DTO plus small row view model | None | Optimistic toggle with rollback on error; last-admin guard messaging |
| Tags | `TagManager` pair | Direct `TagDto` | None | Inline edit rows; delete confirmation shows usage |
| KB list and editor | `KbArticleList` pair; `KbArticleEditor` pair; `MarkdownPreview` pair; `ImageUploadButton` pair | `KbArticleEditorViewModel` | None initially (revisit if preview/upload assembly grows) | Editor owns dirty tracking, preview debounce, upload progress and leave-guard |
| Dead letters | `DeadLetterList` pair | Direct `DeadLetterDto` | None | Row-level action state; list refresh after retry/discard |
| Audit log | `AuditLog` pair | Direct `AdminEventDto` plus formatted summary | None | Paged read-only; filters in query string |
| My settings | `NotificationPreferences` pair | `NotificationPreferencesViewModel` and `MyProfileViewModel` (display-name draft and preview) | None | Per-toggle save state |
| Shell | `MainLayout`, `NavMenu` pair (badge counts), `ConnectionStatus` pair, `ConfirmDialog` pair, `Toasts` pair | Direct | None | Layout owns toast host and connection state; error boundaries from `SyntaxCircus.Blazor.Components` wrap pages |

## Interaction and Content Rules

- **Navigation and information hierarchy:**
  - Persistent left navigation (collapsible) with: Queue (with view counts),
    KB, and for Admins a Settings group (Products, Agents, Tags) and Operations
    group (Failed emails with a count badge, Audit; in 07b the five admin links sit in one group, D-041). Current agent menu holds My
    settings and Sign out.
  - The queue and ticket detail are the product; everything else is secondary
    chrome. Ticket detail is a two-region layout: conversation (timeline plus
    composer) as the dominant region; a side panel for status, assignee,
    priority, product, tags, requester and ticket metadata. On narrow screens
    the side panel collapses above or beneath the conversation.
  - Ticket header always shows: ticket number (monospaced, copyable), subject,
    status, product chip, and any presence hint. Moved tickets keep the original
    number; the product chip shows the current product.
  - The requester's `metadata` (app version, device) shows in the side panel;
    metadata from public API keys is labeled **untrusted** and visually
    distinct from trusted metadata.
  - Follow-up tickets show a link to the parent ("Follow-up to ACME-142") and the
    parent shows its follow-ups.
- **Forms and validation:**
  - Server-validated ProblemDetails errors map to field-level messages; a form
    summary appears for non-field errors. Validate on blur and on submit, never
    on every keystroke.
  - Side-panel changes (status, assignee, priority, product, tags) apply
    immediately with optimistic UI and rollback on error; each emits a
    timeline entry. Closed tickets disable all controls and explain why
    ("Closed tickets are read-only; a customer reply starts a follow-up").
  - Composer: preserve drafts per ticket and tab while navigating within the
    session; warn before leaving with unsent text.
  - **Composer distinction (hard requirement):** the carbon tint code applies
    to the timeline and to the composer. Customer message = white sheet;
    public agent reply = canary; internal note = pink with a dashed border and
    a notched corner. Color is never the only cue. In the **composer**:
    - A two-option segmented control, "Public reply `r`" and "Internal note
      `n`" (pressed state exposed with `aria-pressed`). The control, not the
      body text, decides the mode; switching never moves or converts typed text
      (each mode keeps its own draft per ticket).
    - **Public reply mode:** canary container with a solid edge; submit label
      "Send reply" (`Ctrl+Enter`); audience line "To: {requester email}" and
      the plain warning line "PUBLIC: this will be emailed to the customer."
      Optional "Insert KB link" and status-on-send choice (default Pending;
      "Send and solve").
    - **Internal note mode:** pink container with dashed border and notched
      corner; a warning bar directly above the text area reading exactly
      **"INTERNAL: the customer will NOT see this note."** (`role="status"`, so
      it is announced on switch, and it remains visible while the mode is
      active); audience line "Visible to agents only"; submit label "Add
      internal note"; placeholder "Note for the team only"; no status-on-send
      and no KB link controls (they do nothing for a note).
    - **Defaults and guards:** default mode is Public reply whenever the last
      customer-visible message awaits an agent response; the composer never
      silently changes mode after submit or after a conflict reload; the mode
      is shown in the status bar message after every send ("Reply sent on
      ACME-142" vs "Internal note added to ACME-142").
    - **Timeline entries** repeat the system: a text label (agents see "agent
      reply", "customer", and "INTERNAL NOTE" in the entry header), the tint,
      the dashed notched edge for notes, and an indent for agent entries. A
      small legend (Customer white, Public reply canary, Internal note pink)
      sits under the timeline. The system must still read in grayscale and
      forced-colors mode through label, border style and notch alone.
- **Notifications and errors:**
  - Success toasts are brief and non-blocking (`role="status"`, polite); errors
    that need action are inline, persistent and `role="alert"`.
  - Never lose user input on any error. Network/circuit loss shows the Blazor
    reconnect UI plus a clear explanation; Blazor Server circuit state may be
    lost, so unsent composer drafts need a plan (Handoff Notes, question 6).
  - Conflict, connectivity and permission failures each have distinct,
    plain-language copy (what happened, what was kept, what to do).
  - Email alerts are separate from in-app: v1 has no in-app notification center.
- **Destructive actions and confirmation:**
  - Tiered: reversible actions (status, assignment, tag removal) need no
    confirmation but are undoable via the timeline/toast "Undo" where cheap.
    Medium (mark spam, revoke API key, deactivate agent, discard dead letter,
    archive article): confirmation dialog naming the object and consequence.
    Irreversible (delete ticket, erase requester, delete tag/category with
    usage): dialog with counts and a typed confirmation.
  - Danger actions live in an overflow menu, away from primary controls, and use
    a distinct danger style plus the word, not color alone.
  - **API key secret:** shown exactly once at creation in a modal; copy-to-
    clipboard with feedback; the dialog cannot be dismissed by backdrop click or
    Esc until the admin ticks "I have stored this key"; afterward only the key
    prefix and label are visible. Key kind (Trusted/Public) is always shown as a
    labeled badge with a one-line explanation (Trusted: server-side only, may
    set external user ref and trusted metadata; Public: safe to embed in a
    client app, create-only, rate limited, metadata treated as untrusted).
    Revoke explains that apps using the key stop working immediately.
- **Loading, empty, offline, and degraded states:**
  - Skeletons for lists/timeline on first load; subtle inline spinners on actions;
    no full-page spinners after first render.
  - Every list has a designed empty state with the next sensible action; a
    filtered-empty state is distinct from a truly-empty one. Only the queue's
    truly-empty Unassigned, Mine and Open views use the brand-moment window
    (All caught up); every other empty state is plain text.
  - Error state per region (not whole-page) with retry; error boundaries catch
    component failures and show a recoverable fallback.
  - Offline or SignalR down: the app is read/write as long as the circuit lives;
    live indicators degrade to "paused"; no feature depends on live events.
  - Relative times ("5 min ago") with absolute UTC-converted local time in a
    tooltip/`<time datetime>`; store UTC, display in the agent's local zone.

### Density and keyboard shortcuts

- **Density:** agents live in this app all day. Provide a compact default table
  density for the queue (one line per ticket where possible) with a comfortable
  alternative (Assumption: a single user-level density toggle). Reading areas
  (timeline messages) keep comfortable line length and spacing regardless.
- **Keyboard map (decided; the mockup is the reference).** Single-key shortcuts
  are inactive while focus is in a text field, select or editable area.

  | Key | Context | Action |
  | :-- | :------ | :----- |
  | `j` / `k` (also arrow down/up) | Queue | Move the row selection down/up (selection scrolls into view; never reorders rows) |
  | `Enter` | Queue, focus on the page body | Open the selected ticket |
  | `/` | Anywhere | Focus search (jumps to the queue search from other screens) |
  | `r` | Ticket | Open the Public reply tab and focus the composer |
  | `n` | Ticket | Open the Internal note tab and focus the composer |
  | `e` | Ticket | Focus the Assignee control (the palette command "Assign ... to me" assigns immediately) |
  | `u` | Spam view (row selected) or a flagged Ticket | **Not spam**: clear the spam flag now, no dialog (D-024). Ignored on a ticket that is not flagged |
  | `Ctrl+Enter` | Composer focused | Send in the current mode (reply or note) |
  | `Esc` | Text field focused | Blur the field (typed text is kept) |
  | `Esc` | Ticket, not typing | Back to the queue |
  | `Esc` | Palette or dialog open | Close it (the API-key secret dialog is the exception, see destructive actions) |
  | `Ctrl+K` (`Cmd+K`) | Anywhere in Admin | Open or close the command palette |
  | `?` | Anywhere | Open the shortcut help dialog |

  - **Command palette (decided: yes):** a modal combobox (`role="dialog"`,
    listbox of results, `aria-activedescendant`) filtered by typed words; arrow
    keys select, `Enter` runs, `Esc` closes and returns focus to the previous
    element. Initial commands: go to queue, open a ticket, reply, add internal
    note, assign to me, not spam, switch view (Unassigned, Mine, ... Spam), cycle theme.
    Commands that have a shortcut display it. (The mockup's "Show the brand
    moments" command is a review aid and is not shipped.)
  - **Status bar:** a persistent footer line listing the current hints
    (`j k` move, `Enter` open, `r` reply, `n` note, `e` assign, `/` search,
    `Ctrl K` palette) plus a polite message slot (`role="status"`) for short
    confirmations ("Assigned ACME-142 to Sam"). It supplements, and never
    replaces, toasts for errors that need action.
  - **Accessibility of the layer (WCAG 2.1.4):** single-key shortcuts can be
    turned off in My settings; all shortcuts are listed in the help dialog;
    every shortcut has a visible, mouse-reachable equivalent; the layer is
    inactive on the portal and on the sign-in window.

## Accessibility and Responsive Behavior

Target: **WCAG 2.2 AA**.

- **Keyboard and screen-reader expectations:**
  - Every function is operable by keyboard with a visible, high-contrast focus
    indicator that is never obscured by sticky headers or the composer
    (2.4.11); a "Skip to main content" link; logical landmarks (`nav`, `main`,
    `aside` for the side panel); one `h1` per screen.
  - Queue is a real table (or `role="grid"` only if truly interactive) with
    column headers, sortable headers announced, row focus management, and a
    live region announcing result counts after filtering.
  - The timeline is a list in chronological order; each entry exposes author,
    audience (public/internal), type and time as text. Live additions are
    announced politely ("New message from Alex") without stealing focus or
    moving the scroll position.
  - Presence hints are text with `aria-live="polite"` (throttled) and do not
    announce every keystroke.
  - Dialogs trap and restore focus, label themselves, and have a clear first
    focus target (the safe option on destructive dialogs).
  - Markdown editor: toolbar buttons labeled; preview is a labeled region;
    drag-and-drop upload has a keyboard-operable "Add image" button; alt text is
    required or explicitly marked decorative.
  - Targets at least 24x24 CSS px (2.5.8); no drag-only interactions
    (2.5.7); consistent help location (3.2.6); do not require re-entering
    data already provided (3.3.7); authentication does not rely on a cognitive
    test (3.3.8; handled by the OIDC provider).
- **Color contrast and non-color cues:**
  - Text 4.5:1, large text and UI components 3:1 in **both** the light and the
    dark theme (both ship in v1; carbon tints, stamps and ledger rules are
    re-derived for dark and checked separately). Status, priority, product and
    tag chips combine text label plus shape/icon; color never carries meaning
    alone. Product accent colors and tag colors are arbitrary admin input: the
    UI must compute a legible foreground and never put text directly on an
    unchecked color.
  - Internal vs public, trusted vs untrusted metadata, and danger actions each use
    at least two non-color cues.
- **Responsive layout behavior:**
  - Breakpoints from Bootstrap defaults. Desktop (>=1200): navigation + list/detail
    with side panel. Tablet: collapsible navigation, side panel below the
    header. Phone: single column; queue rows become stacked cards or a reduced
    table; the composer is reachable without hiding the timeline; settings screens
    usable but not optimized. No horizontal page scroll; wide tables scroll
    inside their own region with a visible cue.
  - Respect browser zoom to 400% and text spacing overrides (1.4.4, 1.4.10,
    1.4.12).
- **Reduced-motion or other preferences:** honor `prefers-reduced-motion`
  (no animated row insertion, scroll animations or pulsing indicators, and no
  stamp "thud" animation on the ticket view: the stamp changes state instantly;
  use static highlights), `prefers-color-scheme` (default theme is "auto";
  the agent can override to light or dark), forced-colors/high-contrast mode,
  and user text-size settings. No auto-playing or flashing content. Live-update
  highlights fade by instant state change, not animation, when reduced motion
  is on.

## Visual Direction

**System of record:** [`docs/BRAND.md`](../BRAND.md) (color, type, geometry,
tokens, mascot rules). **Reference mockup:**
[`docs/design/mockups/direction-carbon-copy-v2.html`](../design/mockups/direction-carbon-copy-v2.html)
(queue, ticket, caught-up, brand moments and portal views; light and dark).
Direction: **Carbon Copy v2**, chosen by the owner on 2026-10-02. Personality:
**cheeky frame, serious tools** (humor only in brand moments; see below).

How it applies to Admin:

- **Ruled ledger queue:** square-cornered ledger rows on ruled lines with a
  margin rule, mono ticket numbers and tabular figures; dense, one line per
  ticket (see Density).
- **Status stamps:** the five statuses (and a `Spam?` stamp with a double
  border) are stamp-shaped, text-labeled marks. **Straight and static in
  lists. Tilted, with a one-off "thud" animation when the status changes, only
  on the ticket view** (animation off under reduced motion).
- **Carbon tint code:** white = customer, canary = public reply, pink + dashed
  + notched = internal note (see Composer distinction). Tints are never the
  only cue.
- **Numbered paper-form ticket header:** the ticket header is a call-log form
  with numbered field boxes (1 Ticket no., 2 Received, 3 Caller, 4 Channel,
  5 Problem), mirroring the mockup. The numbering is decoration on the ticket
  header only; it does not appear elsewhere. Header contents still obey the
  Interaction rules (number copyable, subject, status, product chip, presence).
- **Keyboard layer:** see the Keyboard map; status bar and `Ctrl+K` palette are
  part of the shell.
- **Themes:** light and dark both ship in v1 (paper-and-ink light; ruled-blue
  night theme). Default follows `prefers-color-scheme`; the agent can override.
- **Brand moments:** Beige Box retro window frames with the mascot, **only** on
  the three Admin brand moments below. Everything else is working UI.
- **Fonts:** the mockup loads fonts from a CDN for convenience only;
  production self-hosts whatever BRAND.md selects (no third-party font hosts).

### Brand moments and mascot rules (Admin)

Personality rule: **cheeky frame, serious tools.** A brand moment is a screen
where the agent is not mid-task. Working screens, customer-facing text, errors
that block work, and legal or security copy stay plain.

| Screen | Frame | Copy (mockup wording, provisional) | Notes |
| :----- | :---- | :--------------------------------- | :---- |
| **All caught up** (empty Unassigned, Mine or Open view, no filters) | Beige Box window, title bar `queue.exe - 0 items` | Heading "All caught up"; line "Zero tickets, fully supported."; action "View open tickets" | Never shown for filtered-empty or load failure (those are plain). Mascot is decorative: `alt=""`. |
| **Agent sign-in** | Beige Box window, title bar `techstrap - sign in` | Heading "Agent sign-in"; plain security copy "Single sign-on through your company's identity provider. No passwords are entered here."; action "Sign in"; fine print "Agents only. Customers: use your product's support page." | The security and legal-style lines are plain by rule; humor, if any, is limited to the title bar and mascot. |
| **404** | Beige Box window, title bar `ERROR 404 - not found` | Heading may be cheeky ("This page fell out of its strap."); body must be plain and useful: "The address you followed doesn't match any page here. It may have moved, or the link may have a typo."; action "Back to the queue" | A missing ticket number inside a known ticket route still says plainly that the ticket was not found. |

**Where the mascot MAY appear:** inside the Beige Box window on the three
screens above; the small TechStrap mark as the app logo in the navigation rail
(a static logo, not a pose or illustration; BRAND.md confirms whether mark and
mascot are the same artwork and sets the logo-size rule).

**Where the mascot MUST NOT appear:** the queue with rows; the ticket view, its
timeline, composer and side panel; settings (products, agents, tags,
notifications); KB list, editor and categories; dead letters and audit log; the
no-access page, unexpected-error page, session-expired and reconnect overlay
(errors that block work); confirmation and destructive dialogs, the API-key
secret dialog and all security copy; toasts and the status bar; any email; any
Portal page. Window frames (title bar, LED, retro chrome) are also limited to
the three brand moments and never wrap working content.

### Bootstrap and SCSS guidance

- **Bootstrap 5 components/utilities to prefer:** tables (with responsive
  wrapper), list groups for the side panel, nav/offcanvas for the responsive
  menu, modal for dialogs and the palette, toast for notifications, badges
  where a stamp is not wanted (kind, status in the side panel), form controls
  and validation states, dropdown for overflow actions, pagination,
  placeholders for skeletons, `visually-hidden` helpers. Prefer composition of
  utilities over custom classes.
- **SCSS variable overrides:** token overrides only, produced by PHASE-02 and
  recorded in BRAND.md: colors for both themes (via Bootstrap color modes),
  fonts and scale, zero border radius, spacing and table-density variables,
  focus ring, and the semantic status/priority palette (checked for contrast).
- **Custom SCSS justified only for:** the ledger rows and rules, status stamps
  (including the ticket-view tilt and animation), the carbon tint timeline and
  composer treatments, the numbered form header, the Beige Box window, the
  status bar and command palette, the product chip with computed contrast,
  presence and live-change indicators, the Markdown split-pane editor, and the
  density toggle. Everything else stays stock Bootstrap.
- **Typography, imagery, and content tone:** tabular-number-friendly type; a
  monospaced treatment for ticket numbers, API key prefixes, stamps and
  keyboard hints. No decorative imagery in working screens; the product logo
  appears only in the product chip and settings. Tone: calm, plain, brief;
  microcopy states consequences ("This will email the customer") rather than
  jargon. Light personality is allowed only on the three brand moments.

## Handoff Notes

- **Open design questions** (all dispositioned; none is left unmarked):
  1. Layout of ticket detail at 1280 px and 1920 px; docked or inline
     composer. **Answered in part:** two regions as in the mockup (conversation
     dominant; side panel for properties, requester and linked KB article) and
     the composer **inline** beneath the timeline, not docked. **Deferred to
     PHASE-07 (P07-T08):** exact column widths and the narrow-screen collapse
     need rendered review at both widths.
  2. Queue row anatomy and density, narrow-width columns, saved filter presets.
     **Answered:** anatomy per the mockup (see Screen Inventory). Saved filter
     presets are not in v1. **Deferred to PHASE-07 (P07-T07):** which columns
     drop first on narrow widths (proposed order: Product, Requester, Priority);
     requires rendered review.
  3. Dark theme in v1. **Answered:** yes; light and dark both ship (owner
     decision 2026-10-02).
  4. (Resolved, D-022: delete ticket and erase requester are Admin only.)
  5. Keyboard set and command palette. **Answered:** the Keyboard map above,
     with a `Ctrl+K` palette and status bar.
  6. Draft protection against Blazor Server circuit loss. **Deferred to
     PHASE-07 (`ReplyComposer` task):** it is a technical trade-off (session
     storage backup vs a warning) that needs a spike. Default if no spike
     happens: drafts live in the circuit and the leave-warning is shown; the UX
     requirement (never silently lose typed text) is unchanged.
  7. Product chip for arbitrary accents, and the product-filter breakpoint.
     **Answered:** a chip with a text label (product name or key prefix) on a
     background that uses the derived on-accent color (same derivation as the
     Portal brief: white or near-black, whichever contrasts more, at least
     4.5:1), never color alone. **Deferred to PHASE-07 (P07-T07):** the product
     count at which the product filter becomes a searchable combobox (needs a
     realistic product list to judge).
  8. KB editor: split-pane vs tab; toolbar scope; "create article from
     ticket". **Answered:** side-by-side preview on wide screens, tabbed on
     narrow; a minimal Markdown toolbar (bold, italic, link, list, code,
     image), no WYSIWYG. "Create article from this ticket" is **not in v1**
     (not in PHASE-08's page list); **Deferred post-v1.** **Deferred to
     PHASE-08 (P08-T17):** the exact toolbar button set.
  9. Bulk actions. **Deferred post-v1:** not in PHASE-07's scope or success
     criteria. The ledger's selection marker column is reserved but inert.
  10. Requester history in the side panel. **Answered in part:** the requester
      card shows the ticket count and first-seen date (as in the mockup).
      **Deferred to PHASE-07 (P07-T08):** a compact list of other tickets needs
      a requester-scoped ticket list on the API; include only if
      `ListTicketsRequest` already supports it, otherwise post-v1.
  11. How the `Spam` flag is shown and recovered. **Answered (owner
      2026-10-02, D-024):** v1 ships a dedicated **Spam** view in the rail,
      listing tickets with `is_spam = true`; the five normal views exclude
      them. A flagged row shows a double-bordered "Spam?" stamp in place of the
      status stamp. The one-key **Not spam** action is `u` (chosen to avoid
      `j`, `k`, `r`, `n`, `e` and `/`); the overflow menu and palette offer it
      too. PHASE-07's views become six (P07-T22).
  12. Editing a message's audience (public/internal) after the fact.
      **Answered:** not editable in v1; the timeline is append-only.
- **Prototype/wireframe references:**
  - Chosen direction: `docs/design/mockups/direction-carbon-copy-v2.html`
    (queue, ticket with both composer states, caught-up, brand moments,
    portal sample; light and dark). The other mockups in
    `docs/design/mockups/` are superseded explorations.
  - Delivered by PHASE-02: the final `docs/BRAND.md` and the token sheet for
    the SCSS overrides. Still open (tracked in PHASE-07, D-040): mockups for
    states the v2 file does not cover (conflict banner, API-key secret reveal,
    KB editor, dead letters, tablet width). PHASE-07a builds the conflict
    banner from the text of this brief; the 07c review covers the rest.
  - Comparable products to study, not copy: FreeScout (the stated "light
    helpdesk" reference).
- **Acceptance criteria for design review:**
  - [ ] `docs/BRAND.md` exists and the design follows it; the logo-removal test
    was considered.
  - [ ] Public reply and internal note are unmistakable in the composer and the
    timeline, in light, dark, grayscale and forced-colors modes, including the
    exact "INTERNAL: the customer will NOT see this note." warning.
  - [ ] Stamps are straight and static in lists and tilted and animated only on
    the ticket view; the animation is off under reduced motion.
  - [ ] The Beige Box frame and mascot appear on all-caught-up, agent sign-in
    and 404 only; a screen audit finds none elsewhere.
  - [ ] Every screen in the inventory has designed default, loading, empty,
    error and (where relevant) conflict/offline states, in both themes.
  - [ ] The concurrency-conflict flow shows that no typed text is lost.
  - [ ] API-key creation clearly distinguishes Trusted vs Public and enforces
    show-once.
  - [ ] Destructive actions follow the three confirmation tiers and name their
    consequences with counts.
  - [ ] Presence and live-update patterns never move content under the
    pointer or steal focus.
  - [ ] Contrast checked (4.5:1 text, 3:1 UI) for all status, priority and tag
    colors and for arbitrary product accents, in light and dark.
  - [ ] Keyboard-only walkthrough of triage, reply, note, assign, palette and
    key creation passes; shortcut help and opt-out exist; single-key shortcuts
    are inactive while typing.
  - [ ] Desktop, tablet and phone widths reviewed on rendered screens
    (per _template `DESIGN.md` §10), not just from CSS.
  - [ ] No generic SaaS-dashboard patterns introduced without a reason recorded
    in BRAND.md.
