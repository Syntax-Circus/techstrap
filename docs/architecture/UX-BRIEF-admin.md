# UX Brief: TechStrap Admin (agent app)

**Handoff audience:** Claude Design, UX designer, and implementation team

> This is a **designer handoff**, not an implementation ticket. It states what
> the agent app must do and feel like; layout, visual language and component
> design are the designer's to propose. **Prerequisite:** `docs/BRAND.md` is
> produced in [PHASE-02 (brand and UX)](PHASE-02-brand-and-ux.md) following the
> _template `DESIGN.md` process. No significant admin UI is built (PHASE-07)
> until it exists. This brief does not define colour, type or geometry; it
> constrains information, behaviour and accessibility. Where it says "Bootstrap
> prefers" it is a starting point for the designer, not a style decision.

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
    recognisable product chip so agents can tell products apart at a glance. The
    chip must not rely on colour alone.
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
    API keys correctly (trusted vs public), grant/revoke agent access and roles,
    curate tags, watch operational health (dead-lettered emails), audit who
    changed configuration.
  - **Pain points:** fear of exposing a secret or choosing the wrong key kind;
    unclear blast radius of a revoke or delete; no record of who changed what.
  - **Access/permissions:** admin group claim (all Agent abilities plus the
    screens marked Admin below). The first admin is bootstrapped via
    `TECHSTRAP_BOOTSTRAP_ADMIN`; an unauthorised signed-in user sees a clear
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
  retry, previous list kept visible if stale data exists.

### Reply publicly to a customer

- **Starting state:** ticket detail open, not Closed.
- **Trigger:** agent chooses the **Public reply** composer tab (default tab on a
  ticket awaiting an agent response).
- **Steps:**
  1. [ ] Write the reply (plain text or limited formatting; Assumption: plain
     text with line breaks in v1, sanitised server-side).
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
  1. [ ] Composer visibly changes identity (see Interaction rules: distinct
     treatment, label "Internal note: only agents see this", different
     submit label "Add note").
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
     branding (display name, logo, accent colour, email from-address, reply-to),
     active flag.
  2. [ ] Preview how the branding looks in the portal header and email (live
     preview with the entered accent colour, including a contrast warning).
  3. [ ] Create an API key: choose the **kind** (Trusted or Public) with a plain
     explanation of each; name/label it.
  4. [ ] The secret is shown **once** in a modal with copy button and an
     explicit "I have stored this key" confirmation before it can be dismissed.
- **Success state:** product active; key listed by label, kind, created date,
  last used, prefix only; secret never retrievable again.
- **Failure, loading, and empty states:** duplicate key or invalid logo upload
  surface at the field; a product with no keys shows an empty state explaining
  the two key kinds; revoke is confirmed (see destructive actions).

### Write and publish a KB article

- **Starting state:** admin or agent on KB.
- **Trigger:** "New article", or "Create article from this ticket" (Assumption:
  nice-to-have; open question).
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
  2. [ ] **Retry** (re-queues) or **Discard** (confirmed; reason optional).
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
     anonymises the requester across **all** their tickets and deletes their
     attachments), including counts ("12 tickets, 31 attachments").
  3. [ ] Irreversible actions require typing the ticket number or requester email.
- **Success state:** confirmation toast; the agent lands on the queue; an event or
  admin event records who did it (without retaining the erased PII).
- **Failure, loading, and empty states:** failure leaves everything unchanged with
  an explanation; the dialog is never dismissed by Enter.

## Screen Inventory

Routes are an **Assumption** for the implementation team; designers need the
inventory, not the URLs.

- **Screen/route:** Queue `/queue/{view?}` (`/` opens the queue)
  - **Purpose:** find and prioritise work.
  - **Primary actions:** switch view (Unassigned, Mine, Open, Pending, All),
    filter (product, status, tag, priority), search (full-text over tickets),
    page, open ticket, bulk actions are out of scope for v1 (open question).
  - **Data/state:** `ListTicketsRequestHandler` results (paged); view, filters,
    search term and page held in the query string so views are linkable and
    survive refresh; counts per view; live row updates.
  - **Authorization:** Agent.
- **Screen/route:** Ticket detail `/tickets/{number}`
  - **Purpose:** full conversation and ticket control.
  - **Primary actions:** read timeline; public reply; internal note; link KB
    article; change status, assignee, priority, product, tags; mark spam; delete and
    erase requester (Admin only, D-022); follow link to parent/follow-up ticket.
  - **Data/state:** `GetTicketRequestHandler` (detail plus timeline from
    `TicketEvent`: messages, status/assignment/priority/product/tag changes, in
    one chronological stream with internal notes visually distinct); concurrency
    token; presence; live events; draft text per ticket held in the circuit.
  - **Authorization:** Agent.
- **Screen/route:** Products `/settings/products`, `/settings/products/{id}`
  (branding), `/settings/products/{id}/keys` (API keys)
  - **Purpose:** configure products, branding and integrations.
  - **Primary actions:** create/edit product and branding, upload logo, pick accent
    colour (with contrast check), activate/deactivate, create/revoke API keys.
  - **Data/state:** product and key lists; secret held in memory only for the
    one-time reveal.
  - **Authorization:** Admin for management; the product list is Agent-readable
    for filters (D-022).
- **Screen/route:** Agents `/settings/agents`
  - **Purpose:** see who has access and what role they have.
  - **Primary actions:** change role (Agent/Admin), deactivate/reactivate.
  - **Data/state:** agent list (name, email, role, active, last seen); agents appear
    by signing in (provisioned on first call); no manual invite. Agents read only
    the active-agent list (for assignment); this screen is Admin only (D-022).
  - **Authorization:** Admin; an admin cannot demote or deactivate the last
    admin (UI prevents it, API enforces it).
- **Screen/route:** Tags `/settings/tags`
  - **Purpose:** curate the global tag set.
  - **Primary actions:** create, rename, recolour, delete (with usage count).
  - **Data/state:** tag list with ticket counts.
  - **Authorization:** Admin (Assumption: agents may apply but not manage tags).
- **Screen/route:** KB list `/kb` and editor `/kb/{id}`
  - **Purpose:** manage categories and articles.
  - **Primary actions:** filter by product/category/status; create category;
    create/edit/publish/archive article; Markdown editor with live preview; image
    upload.
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
  - **Primary actions:** filter by actor, entity type, date; read-only.
  - **Data/state:** `ListAdminEventsRequestHandler`, paged, newest first. Ticket
    history lives on the ticket timeline, not here.
  - **Authorization:** Admin.
- **Screen/route:** My settings `/account/notifications`
  - **Purpose:** notification preferences.
  - **Primary actions:** per product, opt in/out of new-ticket email alerts;
    assignment alerts toggle (Assumption).
  - **Data/state:** `GetCurrentAgentRequestHandler` plus
    `UpdateNotificationPreferencesRequestHandler`; saves per toggle with inline
    confirmation.
  - **Authorization:** Agent.
- **Screen/route:** Shell, sign-in and system pages
  - **Purpose:** navigation frame, "no access", Blazor reconnect overlay, 404,
    unexpected error.
  - **Primary actions:** navigate; global search focus; sign out.
  - **Data/state:** current agent, nav badges, connection state.
  - **Authorization:** Authenticated; no-access page for signed-in users without
    the group claim.

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
| My settings | `NotificationPreferences` pair | `NotificationPreferencesViewModel` | None | Per-toggle save state |
| Shell | `MainLayout`, `NavMenu` pair (badge counts), `ConnectionStatus` pair, `ConfirmDialog` pair, `Toasts` pair | Direct | None | Layout owns toast host and connection state; error boundaries from `SyntaxCircus.Blazor.Components` wrap pages |

## Interaction and Content Rules

- **Navigation and information hierarchy:**
  - Persistent left navigation (collapsible) with: Queue (with view counts),
    KB, and for Admins a Settings group (Products, Agents, Tags) and Operations
    group (Dead letters with badge, Audit log). Current agent menu holds My
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
    metadata from public API keys is labelled **untrusted** and visually
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
  - **Composer distinction (hard requirement):** Public reply and Internal note
    differ by more than colour: a text label, a different container treatment
    (e.g. border style/pattern), a different icon, a different submit button
    label, and a persistent line stating the audience ("Sent to the customer by
    email" vs "Visible to agents only"). Timeline entries repeat the same
    distinction. The same two-cue system must be reviewed in grayscale and
    forced-colours mode.
- **Notifications and errors:**
  - Success toasts are brief and non-blocking (`role="status"`, polite); errors
    that need action are inline, persistent and `role="alert"`.
  - Never lose user input on any error. Network/circuit loss shows the Blazor
    reconnect UI plus a clear explanation; Blazor Server circuit state may be
    lost, so unsent composer drafts need a plan (open question 6).
  - Conflict, connectivity and permission failures each have distinct,
    plain-language copy (what happened, what was kept, what to do).
  - Email alerts are separate from in-app: v1 has no in-app notification centre.
- **Destructive actions and confirmation:**
  - Tiered: reversible actions (status, assignment, tag removal) need no
    confirmation but are undoable via the timeline/toast "Undo" where cheap.
    Medium (mark spam, revoke API key, deactivate agent, discard dead letter,
    archive article): confirmation dialog naming the object and consequence.
    Irreversible (delete ticket, erase requester, delete tag/category with
    usage): dialog with counts and a typed confirmation.
  - Danger actions live in an overflow menu, away from primary controls, and use
    a distinct danger style plus the word, not colour alone.
  - **API key secret:** shown exactly once at creation in a modal; copy-to-
    clipboard with feedback; the dialog cannot be dismissed by backdrop click or
    Esc until the admin ticks "I have stored this key"; afterward only the key
    prefix and label are visible. Key kind (Trusted/Public) is always shown as a
    labelled badge with a one-line explanation (Trusted: server-side only, may
    set external user ref and trusted metadata; Public: safe to embed in a
    client app, create-only, rate limited, metadata treated as untrusted).
    Revoke explains that apps using the key stop working immediately.
- **Loading, empty, offline, and degraded states:**
  - Skeletons for lists/timeline on first load; subtle inline spinners on actions;
    no full-page spinners after first render.
  - Every list has a designed empty state with the next sensible action; a
    filtered-empty state is distinct from a truly-empty one.
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
- **Shortcuts (Assumption, final set is a design decision; open question 5):**
  `g` then `q` go to queue; `j`/`k` next/previous row; `Enter` open; `/` focus
  search; `?` shortcut help; on a ticket: `r` focus public reply, `n` focus
  internal note, `a` assign to me, `s` status menu, `Ctrl+Enter` send, `Esc`
  blur composer/close menu, `[` back to the queue. Single-key shortcuts are
  disabled while typing in a field, are listed in an accessible help dialog, and
  can be turned off (WCAG 2.1.4). Every shortcut has a visible, mouse-reachable
  equivalent.

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
  - Markdown editor: toolbar buttons labelled; preview is a labelled region;
    drag-and-drop upload has a keyboard-operable "Add image" button; alt text is
    required or explicitly marked decorative.
  - Targets at least 24x24 CSS px (2.5.8); no drag-only interactions
    (2.5.7); consistent help location (3.2.6); do not require re-entering
    data already provided (3.3.7); authentication does not rely on a cognitive
    test (3.3.8; handled by the OIDC provider).
- **Color contrast and non-color cues:**
  - Text 4.5:1, large text and UI components 3:1 in light and dark themes (a dark
    theme is a design question; open question 7). Status, priority, product and
    tag chips combine text label plus shape/icon; colour never carries meaning
    alone. Product accent colours and tag colours are arbitrary admin input: the
    UI must compute a legible foreground and never put text directly on an
    unchecked colour.
  - Internal vs public, trusted vs untrusted metadata, and danger actions each use
    at least two non-colour cues.
- **Responsive layout behavior:**
  - Breakpoints from Bootstrap defaults. Desktop (>=1200): navigation + list/detail
    with side panel. Tablet: collapsible navigation, side panel below the
    header. Phone: single column; queue rows become stacked cards or a reduced
    table; the composer is reachable without hiding the timeline; settings screens
    usable but not optimised. No horizontal page scroll; wide tables scroll
    inside their own region with a visible cue.
  - Respect browser zoom to 400% and text spacing overrides (1.4.4, 1.4.10,
    1.4.12).
- **Reduced-motion or other preferences:** honour `prefers-reduced-motion`
  (no animated row insertion, scroll animations or pulsing indicators; use
  static highlights), `prefers-color-scheme`, forced-colors/high-contrast mode,
  and user text-size settings. No auto-playing or flashing content. Live-update
  highlights fade by instant state change, not animation, when reduced motion
  is on.

## Visual Direction

Direction comes from `docs/BRAND.md` (PHASE-02). The TechStrap brand is a
**neutral, tool-like frame**; product accents appear only on product chips and
previews. The designer should treat the admin as a dense working instrument, not
a marketing surface.

- **Bootstrap 5 components/utilities to prefer:** tables (with responsive
  wrapper), list groups for the timeline and side panel, nav/offcanvas for the
  responsive menu, modal for dialogs, toast for notifications, badges for
  status/priority/kind, form controls and validation states, dropdown for
  overflow actions, pagination, placeholders for skeletons, `visually-hidden`
  helpers. Prefer composition of utilities over custom classes.
- **SCSS variable overrides:** token overrides only, produced by PHASE-02:
  colours, body/heading fonts and scale, border radius, spacing and table
  density variables, focus ring, and the semantic status/priority palette
  (checked for contrast). Dark mode via Bootstrap colour modes if adopted.
- **Custom SCSS justified only for:** the distinct public/internal timeline
  treatments, the composer distinction, the product chip with computed
  contrast, presence and live-change indicators, the Markdown split-pane
  editor, and the density toggle. Everything else stays stock Bootstrap.
- **Typography, imagery, and content tone:** a legible, tabular-number-friendly
  type scheme (ticket numbers, counts and times align); a monospaced or
  otherwise distinct treatment for ticket numbers and API key prefixes. No
  decorative imagery in the working screens; the product logo appears only in
  the product chip and settings. Tone: calm, plain, brief; microcopy states
  consequences ("This will email the customer") rather than jargon. Empty states
  may carry light personality only if BRAND.md allows; clarity first.

## Handoff Notes

- **Open design questions:**
  1. Layout of ticket detail: how the timeline, composer and side panel share
     space at 1280 px and 1920 px, and whether the composer is docked or inline.
  2. Queue row anatomy and density: which columns survive on narrow widths; are
     saved filter presets in v1 (Assumption: no).
  3. Dark theme in v1, or light only first?
  4. (Resolved, D-022: delete ticket and erase requester are Admin only.)
  5. Final keyboard-shortcut set and whether a command palette is warranted.
  6. Draft protection against Blazor Server circuit loss (session-storage draft
     backup vs accepting loss with a warning).
  7. Product chip design that works for any admin-chosen accent colour, and how
     many products before the product filter needs a different control.
  8. KB editor: split-pane vs tab preview; WYSIWYG toolbar scope; "create
     article from ticket" in v1?
  9. Bulk actions (assign/close many) in v1 or later?
  10. Do agents see requester history (other tickets from the same requester) in
      the side panel? (Assumption: a compact list; not in the original spec.)
  11. How is a ticket's `Spam` flag shown and recovered (a Spam view, undo)?
  12. Marking a message vs ticket as public/internal after the fact: out of scope
      (Assumption: not editable).
- **Prototype/wireframe references:** none yet. Expected from PHASE-02:
  (a) `docs/BRAND.md`; (b) three divergent directions per _template
  `DESIGN.md` §5, ideally as coded mockups at desktop and tablet widths, covering
  queue, ticket detail with both composer states, a conflict state, the API-key
  secret reveal, and the KB editor; (c) a token sheet for the SCSS overrides.
  Comparable products to study, not copy: FreeScout (the stated "light
  helpdesk" reference).
- **Acceptance criteria for design review:**
  - [ ] `docs/BRAND.md` exists and the design follows it; the logo-removal test
    was considered.
  - [ ] Public reply and internal note are unmistakable in the composer and the
    timeline, in colour, grayscale and forced-colours modes.
  - [ ] Every screen in the inventory has designed default, loading, empty,
    error and (where relevant) conflict/offline states.
  - [ ] The concurrency-conflict flow shows that no typed text is lost.
  - [ ] API-key creation clearly distinguishes Trusted vs Public and enforces
    show-once.
  - [ ] Destructive actions follow the three confirmation tiers and name their
    consequences with counts.
  - [ ] Presence and live-update patterns never move content under the
    pointer or steal focus.
  - [ ] Contrast checked (4.5:1 text, 3:1 UI) for all status, priority and tag
    colours and for arbitrary product accents.
  - [ ] Keyboard-only walkthrough of triage, reply, note, assign and key
    creation passes; shortcut help and opt-out exist.
  - [ ] Desktop, tablet and phone widths reviewed on rendered screens
    (per _template `DESIGN.md` §10), not just from CSS.
  - [ ] No generic SaaS-dashboard patterns introduced without a reason recorded
    in BRAND.md.
