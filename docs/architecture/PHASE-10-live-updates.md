# PHASE-10: Live Updates

## Objective

Add near-real-time behavior for agents: a SignalR `TicketHub` on the API
(agent JWT), an in-process broadcast of ticket changes made by the API, a
Postgres `LISTEN/NOTIFY` relay for changes made by the worker (auto-close,
dead-letter effects), and admin queue/detail refresh plus a "viewing/replying"
presence hint.

## Dependencies

- **Depends on:** [PHASE-07](PHASE-07-admin-app.md) (admin queue/detail pages, token forwarding); transitively [PHASE-06](PHASE-06-ticket-operations.md) (events written by handlers and the `AutoCloseSolvedTicketsHandler` worker job).
- **Unblocks:** [PHASE-12](PHASE-12-release-hardening.md).
- **External prerequisites:** Reverse proxy allows WebSocket upgrade to the API (`/hubs/*`) with adequate idle timeouts; Postgres allows a dedicated long-lived connection for `LISTEN` from the API (not via a transaction-pooling proxy such as PgBouncer in transaction mode).

### Corrections (D-046, 2026-10-07)

Where this page and D-046 differ, D-046 wins.
- **Delivery.** Two pull requests: 10a (T01 to T10, the server) and 10b (T11 to T17, the Admin, after a `SyntaxCircus.Blazor.Auth` token-provider release).
- **Identity.** `ICurrentUserService` does not exist. The hub fills an `UpdateTicketPresenceRequest` from `Context.User` and `Context.ConnectionId`; `ICurrentAgentClaims` and `IHttpContextAccessor` are not used in a hub.
- **`LiveConnectionState`** is an Admin type (`Features/Live`), not a Contracts type: Contracts allows only `*Dto`, `*Request`, `*Response` and static constants.
- **Post-commit.** A SaveChanges interceptor fires before the commit. The hook is `TicketChangeCaptureInterceptor` (stages the inserted events) plus `TicketChangePublishingInterceptor` (publishes after the commit, drops on rollback or failure).
- **Worker sources.** `DrainEmailOutboxHandler` writes no `TicketEvent`; only `AutoCloseSolvedTicketsHandler` publishes from the Worker.
- **No browser hops.** Browsers never reach `/hubs`; the Admin connects server to server, so the proxy needs nothing new for the hub.
- **Token by header only.** There is no `access_token` query support and no `OnMessageReceived` change; the tests pin that a query token is refused. P10-T06 shrinks to that pin.
- **Event id.** `TicketChangedDto` gains `EventId`.
- **Groups.** `TicketChanged` goes to `queue` only; presence goes to `ticket:{id}`.
- **Unknown ticket.** `JoinTicket` answers `HubException("Ticket not found")` after an existence check, and returns the current presence to its caller.
- **Metrics.** The Api and the Worker pass the meter name to `AddSyntaxCircusObservability`.
- **Detail banner (10b).** A "New activity - refresh" banner; the row version changes only when the agent clicks it. **Presence name (10b):** `Agent.Name`, to other agents only.

## Architecture Decisions

- **Hub on the API, authenticated by the agent JWT.** `TicketHub` at `/hubs/tickets` requires the same agent authorization policy (group claim) as the REST API. Browsers never connect; the admin's Blazor Server circuit connects server-side with the user's access token from `SyntaxCircus.Blazor.Auth` (token provider per connection). WebSocket token travels as `access_token` query value, accepted only on `/hubs/*` via `JwtBearerEvents.OnMessageReceived`, and redacted from logs (**Assumption** for server-side clients; the header route is used where the transport allows).
- **One fan-in abstraction, two implementations of `ITicketChangeBroadcaster`:**
  - **API process:** `SignalRTicketChangeBroadcaster` (via `IHubContext<TicketHub>`).
  - **Worker process:** `PgNotifyTicketChangeBroadcaster` runs `SELECT pg_notify('techstrap_ticket_changes', @payload)`.
  - Application code only sees the interface (also `ITicketChangeBroadcaster` is what `RelayTicketChangeHandler` calls).
- **Where changes originate (D-018):** a post-commit hook in Infrastructure (`SaveChanges` interceptor on the `IUnitOfWork`'s DbContext) inspects newly inserted `TicketEvent` rows and, **after the transaction commits**, publishes one `TicketChangedDto` per affected ticket to the process-local `ITicketChangeBroadcaster`. This leaves the P06 handlers unchanged and guarantees no broadcast for rolled-back work. Broadcast failures are logged and never fail the request.
- **Payload** (`TicketChangedDto`, in Contracts): ticket id, ticket number, product id, event type, actor agent id (nullable), `occurred_at`, a `Kind` (`Created|Updated|Resync`). It carries **no** message content or PII; clients re-fetch via the normal REST calls. Size stays far below Postgres's 8 kB NOTIFY limit.
- **Groups:** every connection joins group `queue` automatically (all agents see all products in the core); `JoinTicket(ticketId)`/`LeaveTicket(ticketId)` manage group `ticket:{id}`. Clients get `TicketChanged` on `queue` for list refresh and on `ticket:{id}` for detail refresh (the same message is sent once per connection; client de-duplicates by event id).
- **Presence:** `UpdateTicketPresenceHandler` handles `JoinTicket`, `LeaveTicket`, `SetComposing`, and connection disconnect (leave-all). State is held in an in-memory `ITicketPresenceStore` (single API instance; **Assumption**, Risks). `Composing` auto-expires after a constant TTL (default 10 s) unless refreshed; admin refreshes at most every 4 s while typing. Presence is broadcast as `PresenceChanged(ticketId, [agentId, displayName, state])` to `ticket:{id}`. Presence is a hint only: it never blocks a reply (optimistic concurrency from P06 stays authoritative).
- **Worker -> API relay:** a hosted `TicketChangeListener` in the API holds a dedicated Npgsql connection, `LISTEN techstrap_ticket_changes`, deserializes payloads and calls `RelayTicketChangeHandler`, which validates and forwards to `ITicketChangeBroadcaster`. On connection loss it reconnects with exponential backoff (constants) and, on reconnect, emits a `Resync` change so clients refresh (notifications sent while disconnected are lost by design). The API does not publish its own changes to NOTIFY, so there are no echo loops (**Assumption**; multi-instance API would need all publishers on NOTIFY, noted in Risks).
- **Delivery semantics:** at-most-once, best effort. Correctness never depends on a notification: pages always load full state from REST; live updates only trigger a refresh. A manual refresh control and the `Resync` message cover gaps.
- **Admin client side:** `ITicketLiveClient` (scoped, wraps `HubConnection` with automatic reconnect, `IAsyncDisposable`) exposes events; pages subscribe in code-behind and refresh via the existing typed clients. Queue refresh is debounced (constant 1 s) and shows an "N tickets updated – refresh" banner instead of reordering rows under the agent's cursor. Detail refresh updates timeline/status; the reply draft and selected KB articles are never touched.
- **Hub method names and group name formats are constants** in `TechStrap.Contracts` (`TicketHubMethods`, `TicketHubGroups`), as they cross the process boundary.
- **No polling fallback** in the core (**Assumption**); the connection indicator and manual refresh are the fallback.
- **Security:** hub methods authorize via the policy plus per-call `ICurrentUserService`; `JoinTicket` verifies the ticket exists (cheap read) so group names cannot be probed for arbitrary ids; payloads contain no customer text. Connection token expiry: the admin recreates the connection when the access token it was started with expires (token provider supplies a fresh one on reconnect). **Assumption**; verify Authentik token lifetime in UAT.

## Application Boundaries

Follow _template APPLICATION_ARCHITECTURE.md. The hub and the listener are
transport adapters: they bind input, pass cancellation, delegate to one named
handler and map the `Result`. Hub method failures map to `HubException` with a
sanitized message; the listener logs failures.

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| SignalR `TicketHub.JoinTicket(ticketId)` | `UpdateTicketPresenceHandler` (action `Join`) | `ITicketPresenceStore`, `ITicketRepository` (existence check), `ICurrentUserService`, `ITicketChangeBroadcaster`, `TimeProvider` | `InMemoryTicketPresenceStore`, EF `TicketRepository`, `SignalRTicketChangeBroadcaster` | `Result` -> success or `HubException("Ticket not found")`; presence pushed to `ticket:{id}` | Named handler (one handler for join/leave/composing: same use case, one action discriminator) |
| SignalR `TicketHub.LeaveTicket(ticketId)` and `OnDisconnectedAsync` (leave-all) | `UpdateTicketPresenceHandler` (actions `Leave`, `LeaveAll`) | As above | As above | Always success; presence updated | Same handler |
| SignalR `TicketHub.SetComposing(ticketId, isComposing)` | `UpdateTicketPresenceHandler` (action `SetComposing`) | As above | As above | Success; TTL-refreshed composing flag broadcast | Same handler |
| Worker change -> Postgres `NOTIFY techstrap_ticket_changes` -> API `TicketChangeListener` (hosted service) | `RelayTicketChangeHandler` | `ITicketChangeBroadcaster`, `TimeProvider` | `SignalRTicketChangeBroadcaster`; listener in Infrastructure/Api host using Npgsql | Validate payload (malformed -> logged and dropped, never throws out of the loop); forward to hub groups | Named handler |
| Worker-side publish after commit (`PgNotifyTicketChangeBroadcaster`) | Not an entry point: infrastructure implementation of `ITicketChangeBroadcaster` invoked by the post-commit hook from `AutoCloseSolvedTicketsHandler` (P06) and `DrainEmailOutboxHandler` (P05) | `ITicketChangeBroadcaster` | `PgNotifyTicketChangeBroadcaster` | Best effort; failure logged | No new handler; documented so the worker publish path is not mistaken for an unlisted entry point |
| API-originated changes (post-commit hook) | Not an entry point: `TicketChangePublishingInterceptor` (Infrastructure) calls `ITicketChangeBroadcaster` after commit for work done by P06 handlers | `ITicketChangeBroadcaster` | EF interceptor + `SignalRTicketChangeBroadcaster` | Best effort | No new handler; infrastructure cross-cutting hook recorded as D-018 ([04-DECISION-LOG.md](04-DECISION-LOG.md), Approved) |
| `/hubs/tickets` negotiate/WebSocket handshake | Exempt | JWT bearer authentication, authorization policy | SignalR framework | 401/403 at handshake | Exempt: framework-owned connection setup; no application workflow (methods above are the use cases) |
| Admin UI (queue/detail/presence components) | Consumes hub; REST via P06 handlers | Admin `ITicketLiveClient`, existing typed clients | `SignalRTicketLiveClient` | UI state | UI only; no new server entry point |

## Razor Component Boundaries

Follow _template RAZOR_COMPONENT_ARCHITECTURE.md. Components live in
`TechStrap.Admin/Features/Live` plus edits to Queue/TicketDetail pages.

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| `LiveConnectionIndicator` | Paired (subscribes to connection-state events, disposal) | None (binds enum `LiveConnectionState` from the client) | Connected / Reconnecting / Disconnected; announced via `aria-live` | None |
| `QueueLiveBanner` | Paired (subscription, debounce counter, disposal) | `QueueLiveBannerViewModel` (count, label) built in code-behind | Counts changed tickets since last load; click triggers parent refresh callback | `TicketChangedDto` |
| `TicketQueuePage` (changed) | Already paired; adds subscription + refresh | None new | Debounced refresh; keeps scroll/filters; no row reordering until accepted | `TicketChangedDto` |
| `TicketDetailPage` (changed) | Already paired; joins/leaves ticket group, refresh on change | Presenter reused | Reloads timeline/status; never resets composer; resets concurrency token on refresh | `TicketChangedDto`, `TicketPresenceDto` |
| `TicketPresenceBar` | Paired (subscription, formatting of names, disposal) | `PresenceViewModel` list (excludes self) built by feature-local `PresenceViewModelFactory` (merges viewing + composing states, ordering) | Viewing / Replying states with TTL-driven clears | `TicketPresenceDto` |
| `ReplyComposer` (changed) | Already paired; raises throttled `SetComposing` via `ITicketLiveClient` | Unchanged | Throttle timer (constant 4 s); sends `false` on blur/submit/dispose | None |
| `ChangedTicketBanner` (detail "New activity – refresh") | Inline (message parameter + one `EventCallback` forwarder) | None | Stateless | None |
| `ITicketLiveClient` / `SignalRTicketLiveClient` (service, not a component) | N/A (plain class) | N/A | Scoped per circuit; owns `HubConnection` | `TicketChangedDto`, `TicketPresenceDto`, hub constants |

## Syntax Circus Packages

Versions in [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md). `Microsoft.AspNetCore.SignalR.Client`, `Microsoft.AspNetCore.Authentication.JwtBearer` and Npgsql are third-party and pinned in the package map.

| Package | Concern | Why it belongs in this phase | Verification |
| :------ | :------ | :--------------------------- | :----------- |
| `SyntaxCircus.AspNetCore.Authentication` | JWT bearer + authorization policy | Hub reuses the agent policy; `OnMessageReceived` token for `/hubs` | Api.Tests: hub connect without token -> 401; non-agent -> 403 |
| `SyntaxCircus.Blazor.Auth` | Per-user token for admin hub connection | Admin circuit connects as the signed-in agent | Test: `AccessTokenProvider` yields the user's token and a refreshed one after expiry |
| `SyntaxCircus.EntityFrameworkCore.Postgres` | EF/Npgsql data source, migrations | Dedicated NOTIFY/LISTEN connections reuse the configured connection string; no schema change | Integration test (Testcontainers) round-trip NOTIFY -> listener |
| `SyntaxCircus.Common` | `Result` | Presence/relay handlers return `Result` | Unit tests |
| `SyntaxCircus.Observability`, `.AspNetCore.Serilog` | Metrics/logging | Connected-agents gauge, relay counters, token redaction on `/hubs` | Metric present in test meter; log redaction test |
| `SyntaxCircus.Blazor.Components` | Reconnect/error UI | Admin already wired; `LiveConnectionIndicator` complements `ReconnectModal` | bUnit |

## Deliverables

- [x] `TicketHub`, `UpdateTicketPresenceHandler`, `ITicketPresenceStore`, `RelayTicketChangeHandler`, `ITicketChangeBroadcaster` (+ SignalR and pg_notify implementations), `TicketChangeListener` (10a, D-046).
- [x] Post-commit publishing hook in Infrastructure used by API and Worker (10a, D-046: capture plus publishing interceptors).
- [x] Contracts: `TicketChangedDto`, `TicketPresenceDto`, hub method/group constants, change-kind constants (10a; `LiveConnectionState` is an Admin type, 10b).
- [ ] Admin `ITicketLiveClient`, live indicator, queue banner, detail refresh and presence bar.
- [ ] Reverse-proxy/WebSocket notes added to the compose docs.
- [ ] Unit, API (in-memory hub), integration (NOTIFY) and bUnit tests.

## Actionable Tasks

- [x] **P10-T01** Define `TicketChangedDto`, `TicketPresenceDto`, `LiveConnectionState`, `TicketHubMethods`, `TicketHubGroups` and change-kind constants in `TechStrap.Contracts`
  - **Depends on:** P06
  - **Validation:** Build; Architecture.Tests: Contracts has no inward references; serialization round-trip unit test for each DTO (System.Text.Json, camelCase).
- [x] **P10-T02** Define `ITicketChangeBroadcaster` and `ITicketPresenceStore` in Application (with `TicketChange` application model) and implement `InMemoryTicketPresenceStore` (TTL expiry via `TimeProvider`)
  - **Depends on:** P10-T01
  - **Validation:** Unit tests with `FakeTimeProvider`: composing expires after TTL; leave-all removes every ticket for a connection; concurrent updates safe.
- [x] **P10-T03** Implement `UpdateTicketPresenceHandler` (Join/Leave/LeaveAll/SetComposing) returning `Result`, broadcasting presence changes only on actual state change
  - **Depends on:** P10-T02
  - **Validation:** Application.Tests with NSubstitute: unknown ticket -> not-found result; repeated identical state does not broadcast; cancellation passed to the repository call.
- [x] **P10-T04** Implement `RelayTicketChangeHandler` (validate payload, drop malformed, forward to broadcaster)
  - **Depends on:** P10-T02
  - **Validation:** Unit tests: valid payload forwarded once; malformed JSON/unknown kind returns failure result without throwing; oversize payload rejected.
- [x] **P10-T05** Implement `TicketHub` (auth policy, auto-join `queue`, thin methods delegating to the handler, `OnDisconnectedAsync` -> LeaveAll) and `SignalRTicketChangeBroadcaster`
  - **Depends on:** P10-T03
  - **Validation:** Api.Tests with in-process test server and `HubConnection`: unauthenticated rejected; two agents on one ticket see each other's presence; `TicketChanged` reaches `queue` members; Architecture.Tests: hub methods only call handlers.
- [x] **P10-T06** Configure JWT `OnMessageReceived` for `/hubs`, WebSocket/forwarded-header settings, and log redaction of the `access_token` query value (as built, D-046: reshaped to the pin that a query token is refused and no token reaches a log; no `OnMessageReceived` change)
  - **Depends on:** P10-T05
  - **Validation:** Api.Tests: token via query accepted only on `/hubs/*` (rejected on `/api/*`); log capture never contains the token.
- [x] **P10-T07** Implement the post-commit publishing hook (`TicketChangePublishingInterceptor`) in Infrastructure and register it in API (SignalR broadcaster) with failure isolation
  - **Depends on:** P10-T05
  - **Validation:** Integration test: committing a handler transaction that adds a `TicketEvent` triggers exactly one broadcast per ticket; rollback triggers none; broadcaster exception does not fail the request.
- [x] **P10-T08** Implement `PgNotifyTicketChangeBroadcaster` and register it in the Worker host; wire the same hook for `AutoCloseSolvedTicketsHandler` and `DrainEmailOutboxHandler` commits (as built, D-046: `AutoCloseSolvedTicketsHandler` is the one Worker writer; `DrainEmailOutboxHandler` writes no `TicketEvent`)
  - **Depends on:** P10-T07
  - **Validation:** Integration test (Testcontainers): worker-style commit emits a `NOTIFY` with the expected JSON (<8 kB) on channel `techstrap_ticket_changes`.
- [x] **P10-T09** Implement `TicketChangeListener` hosted service (dedicated Npgsql connection, LISTEN, backoff reconnect, `Resync` emission) calling `RelayTicketChangeHandler`
  - **Depends on:** P10-T04, P10-T05
  - **Validation:** Integration test: NOTIFY from a second connection reaches a connected hub client; killing the listener connection (`pg_terminate_backend`) results in reconnect and a `Resync` message; listener stops cleanly on shutdown.
- [x] **P10-T10** Add observability: connected-agents gauge, changes-relayed counter, relay failures counter, listener-reconnect counter
  - **Depends on:** P10-T09
  - **Validation:** Meter listener test asserts instruments update; names in constants.
- [ ] **P10-T11** Implement admin `ITicketLiveClient` / `SignalRTicketLiveClient` (token provider from Blazor.Auth, automatic reconnect with backoff, event de-duplication, `IAsyncDisposable`)
  - **Depends on:** P10-T05, P07-T02
  - **Validation:** Unit tests with a fake connection abstraction: reconnect state transitions raised; duplicate event ids ignored; disposal stops the connection and unsubscribes.
- [ ] **P10-T12** Build `LiveConnectionIndicator` and place it in `MainLayout`
  - **Depends on:** P10-T11
  - **Validation:** bUnit: renders each state with appropriate text/`aria-live`; unsubscribes on dispose.
- [ ] **P10-T13** Add queue live refresh: subscription in `TicketQueuePage`, `QueueLiveBanner`, debounced counter and explicit refresh
  - **Depends on:** P10-T11, P07-T07
  - **Validation:** bUnit with `FakeTimeProvider`: a burst of changes yields one banner update; accepting refreshes with current filters; rows not reordered before acceptance.
- [ ] **P10-T14** Add detail live refresh: join/leave ticket group in `TicketDetailPage`, `ChangedTicketBanner`, reload timeline preserving composer draft and updating the concurrency token
  - **Depends on:** P10-T11, P07-T08, P07-T11
  - **Validation:** bUnit: change for the open ticket reloads timeline; draft text untouched; change for another ticket ignored; leaves the group on navigation/dispose.
- [ ] **P10-T15** Build `TicketPresenceBar` + `PresenceViewModelFactory` and throttled `SetComposing` in `ReplyComposer`
  - **Depends on:** P10-T14
  - **Validation:** Factory theory (viewing only, replying only, both, self excluded, ordering); bUnit with fake timers: typing sends at most one `SetComposing(true)` per throttle window and `false` on submit/blur/dispose.
- [ ] **P10-T16** Document and verify reverse-proxy WebSocket requirements (upgrade headers, idle timeout, buffering) in compose docs; add the connection settings as `.env.example` keys
  - **Depends on:** P10-T06
  - **Validation:** Manual UAT check: two browsers behind the proxy see presence and refresh within 2 s; connection survives a 5-minute idle period (keepalive configured).
- [ ] **P10-T17** End-to-end verification script/test: worker auto-close of a Solved ticket updates an open admin queue without manual reload
  - **Depends on:** P10-T08, P10-T09, P10-T13
  - **Validation:** Compose-based test (or documented manual run with seed data): shorten auto-close to 1 minute, observe banner in admin; no duplicate notifications.

## Success Criteria

- [ ] An agent opening a ticket sees other agents viewing it and, within about 2 seconds, sees "replying…" when another agent types; presence clears after leaving or the TTL.
- [ ] A change made by one agent (reply, status, assignment) shows on another agent's open queue (banner) and open ticket (timeline refresh) without reloading; typed drafts are never lost.
- [ ] A worker-originated change (auto-close) reaches admin clients through NOTIFY -> listener -> hub.
- [ ] Losing the DB listener or the hub connection results in automatic recovery and a resync; the UI shows connection state.
- [ ] Unauthenticated or non-agent users cannot connect or join; payloads contain no message content.
- [ ] Rolled-back transactions never produce notifications; broadcast failure never fails a user request.
- [ ] `dotnet build`/`dotnet test` green including Testcontainers integration tests; no new database migration required.

## Boundary Validation

- [ ] Application use-case entry points delegate to the named handlers listed above (`TicketHub` methods -> `UpdateTicketPresenceHandler`; listener -> `RelayTicketChangeHandler`).
- [ ] Framework-owned operational or static exemptions execute no application workflow (`/hubs/tickets` handshake only authenticates).
- [ ] Handler constructor dependencies contain only approved abstractions (`ITicketPresenceStore`, `ITicketRepository`, `ITicketChangeBroadcaster`, `ICurrentUserService`, `TimeProvider`; no `IHubContext`, no Npgsql types).
- [ ] Persistence and integration entities do not cross infrastructure boundaries (the interceptor maps EF entities to the application `TicketChange` model).
- [ ] Cancellation reaches asynchronous handler dependencies (hub `Context.ConnectionAborted`, listener stopping token).
- [ ] Expected outcomes and transport mapping have focused tests (hub exceptions, malformed payload handling).
- [ ] Infrastructure implementations have integration coverage (NOTIFY/LISTEN, interceptor, presence store).
- [ ] Inline Razor components contain only simple parameters and, at most, one trivial synchronous `EventCallback`-forwarding callback (`ChangedTicketBanner`).
- [ ] Every component beyond the inline ceiling uses paired `.razor` and `.razor.cs` files, with all C# in code-behind.
- [ ] Each Razor ViewModel is feature-local and presentation-only; no component exposes a Contracts DTO as a ViewModel.
- [ ] A factory or presentation service is used only for non-trivial mapping, asynchronous assembly, or multiple dependencies (`PresenceViewModelFactory` only).
- [ ] API request and response contracts use DTO names and contracts, never Razor ViewModels.
- [ ] Repeated or business-meaningful literals are named constants (hub path, method/group names, channel name, TTLs, throttle and debounce intervals, backoff).
- [ ] Duplicated-looking logic across flows was evaluated for genuine divergence (API in-process publish vs worker NOTIFY publish are two `ITicketChangeBroadcaster` implementations by design, not a shared branching class).

## Risks and Open Questions

- [ ] **Single API instance assumption:** the in-memory presence store and in-process publish do not span multiple API instances. If the API scales out, move all publishers to NOTIFY and presence to a shared store; deferred (UAT runs one instance).
- [ ] **PgBouncer/transaction pooling** breaks `LISTEN`; the listener must use a direct connection (document in self-host guide, P12).
- [ ] Post-commit interceptor is an infrastructure cross-cutting mechanism not in the handler catalog (D-018); if the owner rejects it, have the P05/P06 handlers call the broadcaster explicitly and update the handler tables in `02-ARCHITECTURE.md`.
- [ ] Long-lived circuits vs token expiry (see Architecture Decisions); validate against Authentik token lifetime.
- [ ] Notification loss during listener downtime is accepted; `Resync` covers it.
- [ ] Presence leaks agent display names to other agents only; confirm acceptable (agents are one company; **Assumption**).
- [ ] `Microsoft.AspNetCore.SignalR.Client`, Npgsql and bUnit/`FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) are pinned in [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md).

## Handoff

Before [PHASE-12](PHASE-12-release-hardening.md) starts: hub, listener and
admin live features work against compose (including the worker-originated
path); the WebSocket/proxy and `LISTEN` connection requirements are written
into the compose/self-host notes; hub authentication and the `access_token`
query-string handling are listed for the security review. D-018 (the post-commit
publishing hook) was approved on 2026-10-02 in
[04-DECISION-LOG.md](04-DECISION-LOG.md).
