# PHASE-10a Live Updates, Server Side: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give TechStrap's agents near-real-time behaviour from the server side, in one pull request (10a of two).
- **The hub:** an agent-only SignalR `TicketHub` at `/hubs/tickets` that pushes `TicketChanged` to every agent's queue and `PresenceChanged` to the agents who have a ticket open.
- **The post-commit hook:** a ticket change made by an Api request is announced once per ticket, after its database transaction commits, and never for work that rolled back or conflicted.
- **The relay:** a change made by the Worker (auto-close) is sent as a Postgres `NOTIFY` and relayed by a listener in the Api, which reconnects by itself and tells the clients to resync.
- **Metrics and docs:** the first TechStrap meter, and D-046, the spec corrections, the runbook note and the roadmap fixes.

**Architecture:**
- **Identity is a request record, not an accessor.** The hub builds an `UpdateTicketPresenceRequest` from `Context.User` and `Context.ConnectionId`; `UpdateTicketPresenceHandler` decides (agent still active, ticket exists, state really changed) over `IAgentRepository`, `ITicketRepository`, `ITicketPresenceStore` and `ITicketChangeBroadcaster`. The hub stays a thin adapter and no Application type knows SignalR or Npgsql.
- **One fan-in, three implementations.** `ITicketChangeBroadcaster` is called by the post-commit hook. `NullTicketChangeBroadcaster` is the default; the Api replaces it with `SignalRTicketChangeBroadcaster`, the Worker with `PgNotifyTicketChangeBroadcaster`. The hook has two interceptors: one stages the `TicketEvent` rows a SaveChanges inserts, the other publishes after `TransactionCommitted` and drops the staging on rollback or failure.
- **The relay never echoes.** The Api's `TicketChangeListener` only listens; `RelayTicketChangeHandler` validates what arrives (the channel is writable by anything) and forwards it; a lost connection ends in a backoff, a reconnect and one `Resync`.

**Tech Stack:** .NET 10, ASP.NET Core SignalR (shared framework; the client package only in `TechStrap.Api.Tests`), EF Core interceptors, Npgsql `LISTEN`/`NOTIFY`, `System.Diagnostics.Metrics`, xUnit v3, Shouldly, NSubstitute, Testcontainers (Postgres), `FakeTimeProvider` and Pester.

**Spec:** `docs/architecture/PHASE-10-live-updates.md` (T01 to T10, the Success Criteria that concern the server), `docs/architecture/04-DECISION-LOG.md` (D-007, D-018), `docs/architecture/02-ARCHITECTURE.md` (the handler tables) and the owner decisions of 2026-10-07 (the PHASE-10 scope plan, "Spec corrections found" 1 to 10 and "Owner decisions"), recorded as **D-046** in Task 1. This plan covers **10a only**: 10b (the Admin client, the banners, the presence bar, T11 to T17) waits for the `SyntaxCircus.Blazor.Auth` token-provider release.

### Owner decisions (2026-10-07), recorded as D-046
1. **Delivery.** Two pull requests: 10a (T01 to T10) and 10b (T11 to T17, after the package prerequisite).
2. **Detail page on another agent's change (10b).** A "New activity - refresh" banner; the row version moves only when the agent clicks it, so a send before that still gets the 409 and the draft is never touched.
3. **Presence name (10b).** `Agent.Name` (the email when there is none), shown to other agents only; never `PublicDisplayName`.
4. **Hub token source (10b).** A public token-provider API in `SyntaxCircus.Blazor.Auth`, released before 10b.

### Spec corrections this plan applies (D-046 records them, Task 1 writes them into the spec)
`ICurrentUserService` does not exist (identity travels in the request record); `LiveConnectionState` is an Admin type; the hook needs two parts because SaveChanges fires before the commit; only auto-close publishes from the Worker; browsers never reach `/hubs`; the token travels by header only; `TicketChangedDto` gains `EventId`; the Api and the Worker pass the meter name to observability; `TicketChanged` goes to `queue` only and presence to `ticket:{id}`; an unknown ticket is `HubException("Ticket not found")`.

### Decisions made while drafting (D-046 records them)
- **Task split.** The six tasks of the brief, each a RED step, a GREEN step, a recorded mutation run and a commit. Task 1 carries the contracts, the Application handlers and the D-046 documentation; Task 2 the presence store and the hook; Task 3 the hub; Task 4 the Worker's NOTIFY and the Api's listener; Task 5 the metrics; Task 6 the close-out.
- **The spike, proved in a scratch clone (Postgres in Testcontainers, a real `HubConnection`, a real loopback Kestrel port) before the tasks were written.** Each finding is now a durable test.
  - **(a) The post-commit hook.** A `SaveChangesInterceptor` that stages the added `TicketEventRecord` rows per `DbContext` plus a `DbTransactionInterceptor` that publishes on `TransactionCommitted(Async)` works beside the existing `AppendOnlyEventInterceptor`: a commit publishes once per ticket, however many events it holds; a rollback (the scope disposed after SaveChanges ran), a concurrency conflict, a unique violation and `UnitOfWork`'s own `RollBackAsync` publish nothing; a throwing or hanging broadcaster never fails the commit. Details that only the run showed: `AddInterceptors` accumulates, so the new interceptors are added in `AddTechStrapPersistence` (they need the container) while `TechStrapDatabase.Configure(options, cs)` keeps its two-argument shape and its direct callers simply get no hook; EF fires `TransactionCommitted` for the implicit transaction of a multi-statement SaveChanges too (taking the staged changes empties them, so the later "saved" event does not publish twice); a one-statement SaveChanges opens no transaction at all, so the capture interceptor publishes from "saved" when `Database.CurrentTransaction` is null; a failed SaveChanges with no transaction needs `SaveChangesFailed` to drop the staging (the rollback event never comes); events the context only read (state `Unchanged`) must not be announced again; the staging lives in a `ConditionalWeakTable` keyed by the context, so a context disposed with its transaction open cannot leak.
  - **(b) SignalR on the in-memory server.** A `HubConnection` with `HttpMessageHandlerFactory = factory.Server.CreateHandler()`, long polling and `AccessTokenProvider` reaches the hub with the test JWT in the `Authorization` header (the existing `TestJwt`). The in-memory `WebSocketClient` cannot carry the client's handshake header, so one test starts the host on a real loopback Kestrel port (`factory.UseKestrel(0)`, `StartServer()`, the address from `IServerAddressesFeature`; there is no `ServerAddress` property) and connects over WebSockets with `SkipNegotiation`, header token only: it connects, a pushed change arrives, and the same handshake with no token, or with the token only in the query, is refused.
  - **(c) Hub identity.** `Context.User` carries the raw `sub` (the host sets `MapInboundClaims = false`), and the existing static `ClaimsCurrentAgentClaims.FromPrincipal` reads it; `IHttpContextAccessor` is never used in the hub. `Context.UserIdentifier` is null for these tokens, so nothing uses `Clients.User`. SignalR creates one DI scope per hub invocation, so the scoped handlers resolve in the hub constructor.
  - **(d) LISTEN/NOTIFY.** One dedicated, unpooled `NpgsqlConnection` that runs `LISTEN` and loops on `WaitAsync` (the `Notification` event fires inside it; Npgsql hands over one notification per wake-up, so the listener's queue never holds more than one) receives what a second connection notifies. `pg_terminate_backend` of the listener's backend ends `WaitAsync` with an exception; the loop waits its backoff, reconnects and relays one `Resync`. A listener is found without any hook in the product by its `application_name` and by its last statement being the `LISTEN`: finding it by name alone raced (the connection is open before it listens, and a notification sent in that gap is lost). Worker to Api end to end works with two real hosts on one database.
  - **(e) The query token.** `access_token` in the query is not read by the JWT bearer handler as configured, so `/hubs` answers 401 with no code change; a test pins it for negotiate and for a raw WebSocket handshake, and a mutation that adds an `OnMessageReceived` reading the query makes the tests fail. At Verbose, with a request that carries a token in the URL, no log event contains either token (the PII redactor masks the JWT shape, and the hub itself logs paths only).
  - **(f) The meter.** `new Meter("TechStrap")` with an observable gauge and three counters is seen by a `MeterListener`. `AddSyntaxCircusObservability(ServiceName, [TechStrapMetrics.MeterName])` is what makes the exporter read it, and only when OTLP is on; the test proves registration with `Instrument.Enabled` (`TechStrapMetrics.IsObserved`). A pitfall found by a surviving mutant: a meter provider subscribes to every meter of that name in the process, so a second host alive beside the first is observed on the first one's registration; the registration test starts one host at a time and disposes each with `await using`.
- **Deviations from the brief, each with its reason.**
  - **`ITicketChangeBroadcaster` has a second method, `PublishPresenceAsync`.** The brief names one abstraction for the handler's push; presence needs a push to a ticket group, the spec's handler table lists only `ITicketChangeBroadcaster` for the presence handler, and a second interface would add a registration for the same two implementations. The Worker's version does nothing for presence.
  - **`JoinTicket` returns the current presence** (`TicketPresenceDto`); 02-ARCHITECTURE said it returns void. A second tab of an agent already on the ticket changes nothing, so no broadcast reaches it; the return value is how it learns the state.
  - **A composing refresh counts as a change.** The brief says presence is broadcast only when the state really changes; the client's clear-after-10-seconds needs a heartbeat while the agent keeps typing, and the spec has the Admin refresh every 4 seconds. A refresh that extends the lease is therefore sent; a repeat that moves nothing (same instant, same state) is not, and a refresh after a lapse is a change because peers saw "viewing" meanwhile.
  - **A deleted ticket publishes a `Resync`**, and so does an event for a ticket the context does not track. The brief is silent; the facts report called deletion a gap; the closest safe behaviour is "reload everything", which needs no new kind and no query inside SaveChanges.
  - **The hook's two classes are `TicketChangeCaptureInterceptor` and `TicketChangePublishingInterceptor`**, plus `PendingTicketChanges` (the per-context staging) and `TicketChangePublisher` (the broadcaster call with isolation and a 5-second limit). The brief names one class "in two parts".
  - **`TechStrapMetrics` lives in Infrastructure, and the hub takes it.** The gauge needs the hub's connections, and the listener (Infrastructure) records the counters; handlers cannot take a meter. The hub's constructor is the presence handler, the agent options and the metrics, pinned by a test.
  - **New Contracts types beyond the brief's list:** `UpdateTicketPresenceRequest` and `RelayTicketChangeRequest` (D-016: handlers take Contracts request records), `TicketViewerDto`, `TicketHubRoutes`, `TicketPresenceActions`, `TicketLiveLimits` and `TicketHubMessages`.
  - **The listener is off when no connection string is configured** (a Development host without a database), logs how long it will wait, names its connection `techstrap-ticket-change-listener` in `pg_stat_activity`, and never logs a payload, a connection string or an exception message.
  - **`Npgsql` becomes a direct `PackageReference` of Infrastructure** (it was transitive through the EF provider); the version is the one already pinned. `Microsoft.AspNetCore.SignalR.Client` goes into `TechStrap.Api.Tests` only. `03-PACKAGE-MAP.md` records both uses.
  - **`TechStrapMetrics.IsObserved` is public**, so the Api host tests can read it.
  - **Three Pester pins are changed**, not only added: the 08 and 09 pins said "pending merge" for merged phases, and the three D-045 addenda each asserted that there is no D-046. The roadmap rows 07, 08 and 09 now say merged, with their pull request numbers.
  - **"The D-045 as-built notes" is read as the D-046 as-built notes** (its Consequences section); D-045 itself is not edited.
- **Honest limits.** Presence, the in-process publish and the metrics gauge are per Api instance (D-007). Notifications sent while the listener is down are lost by design; the `Resync` covers them. Nothing proves the hub behind a real reverse proxy or against a real Authentik (the Admin client of 10b is where that is checked). The `RelayAsync` catch in the listener (a failure to even create the scope) has no test of its own: the handler turns every relay failure into a Result.
- **A mutation-run hazard that happened.** A second shell running another batch in the same clone left sources mutated and the index holding a mutated file. Run one mutation batch at a time, never `git add` while one runs, and after an interrupted run compare the working tree to the index (`git status` must show no `AM` or ` M` file) before trusting a result.
- **One known flake class.** Every wait in the new tests is a bounded poll (30 seconds, a ceiling and not a measurement) and every waiting test has a `Timeout`. Run the Infrastructure and Api suites again before suspecting a change if one of the listener tests times out on a busy machine.
- **No new setting and no migration.** Every number (the channel, the keepalive, the backoff, the TTL, the caps) is a constant; `dotnet ef migrations has-pending-model-changes` is clean (Task 6, last step).
- **Shared files between tasks** (the tasks run in order, so the overlaps are safe): `LiveNames.cs` (Tasks 1 and 3), `TicketHub.cs`, `LiveHubExtensions.cs`, `Program.cs` of the Api and `LiveServiceCollectionExtensions.cs` (Tasks 3, 4 and 5), `TicketChangeListener.cs` and `TicketChangeListenerTests.cs` (Tasks 4 and 5), `HubPolicyCoverageTests.cs` (Tasks 3 and 5), `04-DECISION-LOG.md`, `PHASE-10-live-updates.md`, `99-IMPLEMENTATION-ROADMAP.md`, `00-DISCOVERY-INDEX.md` and `RepositoryDocs.Tests.ps1` (Tasks 1 and 6).

## Global Constraints

- **Build.** .NET SDK 10.0.401 targeting `net10.0`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild`. Private fields are `_camelCase`, constants are PascalCase, namespaces are file-scoped.
- **Packages.** No new package except `Microsoft.AspNetCore.SignalR.Client` in `TechStrap.Api.Tests` (already pinned in `Directory.Packages.props` and `03-PACKAGE-MAP.md`) and the direct `Npgsql` reference of Infrastructure (also already pinned).
- **Migrations.** None.
- **Config (D-043).** 10a adds no configuration key. A key added later gets four edits: the host's `appsettings.json`, the host's `.env.example`, `deploy/.env.<app>.example` and compose where compose owns it; `scripts/tests/ConfigContract.Tests.ps1` must pass.
- **Architecture rules.**
  - Handlers depend only on Application interfaces, `TimeProvider`, options and loggers: no `IHubContext`, no Npgsql or EF type, no `Meter`.
  - Contracts holds only `*Dto`, `*Request`, `*Response` types and static constant classes; no enum.
  - Every handler is `sealed`, implements `I<Name>` and takes a `CancellationToken` last; every async Application abstraction method ends with a `CancellationToken`; cancellation is passed through to every repository and broadcaster call.
  - Infrastructure types that touch persistence records stay `internal` and map to Application models (`TicketChange`).
- **Secrets and PII.** The change payload holds ids, a wire name and the ticket number only. A token never appears in a URL, a log or a payload. The listener and the publisher log exception type names and error codes, never an exception message, a payload or a connection string. Do not write the names `EnableSensitiveDataLogging` or `IncludeErrorDetail` in comments or code except to set the latter to `false` in the listener's connection string (the logging-safety rule scans for them; the listener's builder is the one allowed use and is covered by a test).
- **Encoding.** Write non-ASCII as `\u` escapes, never raw. The Write tool decodes `\uXXXX`, so a file that needs an escape is written with a script; this plan's files are pure ASCII.
- **Tests.**
  - Failing test first, with RED and GREEN recorded.
  - Prove each pin with a recorded mutation; every mutation must keep the Release build compiling (avoid `if (false)`: CS0162; do not leave a variable unused: the build treats it as an error). A mutation that does not compile is reported by `mut.py` as "NOT A MUTATION" (exit code 4).
  - Every test that waits carries `Timeout = ...` and reads `TestContext.Current.CancellationToken` in its own body (analyzer xUnit1069), waits with a bounded poll or a barrier message, and never depends on tight wall-clock timing. A "nothing else arrived" assertion is made with a barrier: a later message on the same connection arrives after any earlier one, so its arrival proves the earlier ones are not coming.
  - Tests that start more than one host observing a process-wide meter dispose them one at a time with `await using`.
  - No background processes: every mutation batch runs in the foreground, one batch at a time, and the file is restored before the next. Stop the processes you start and check `docker ps` for a leftover Testcontainers container at the end.
- **Commits.** Use Conventional Commits. End each one with exactly these two lines, on separate lines:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- **Forbidden.** `git add -f`, committing `.superpowers/`, `docker compose down -v`, killing processes you did not start, and committing without first running `git diff --cached --stat`.
- **Verification.**
  - `dotnet build TechStrap.slnx -c Release`: 0 warnings.
  - `dotnet test --solution TechStrap.CI.slnf -c Release`: passes.
  - `pwsh -File scripts/Invoke-ScriptTests.ps1`: passes.
  - `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build`: no changes.
- **Branch.** Work on `feat/phase-10a-live-server` (cut from main at 2c9b10d, which includes PHASE-09). Never edit or commit on `main`.

## Review Focus

1. **Hub authentication and authorisation.** Only agents connect, by header token; a query token is refused; a deactivated agent is refused at the handshake; expiry closes the connection; `JoinTicket` cannot probe arbitrary ids and joins no group for an unknown one; no customer text is in any payload and no token in any log.
   - Pinned in Task 3 (`TicketHubTests`: 401, 403, deactivated, query token, raw negotiate, expiry, the unknown-ticket barrier test, the log scan at every level; `HubWebSocketTests`; `HubPolicyCoverageTests`) and Task 1 (`LiveContractsTests`: the payload's property list).
2. **Post-commit correctness.** No broadcast for rolled-back or conflicted work; exactly one broadcast per ticket per commit; a broadcaster failure or hang never fails the request.
   - Pinned in Task 2 (`TicketChangePublishingTests`: commit, several events, two tickets, rollback, rollback then commit in one scope, conflict, unique violation, throwing, partly failing, hanging, no-transaction save, failed save, read-only events, delete) and Task 3 (a REST status change reaches a hub client once, a 409 never does).
3. **Listener robustness.** Reconnect with backoff and one `Resync`; a malformed payload dropped without breaking the loop or reaching a log; a clean shutdown, also during a backoff; no echo loop.
   - Pinned in Task 4 (`TicketChangeListenerTests`, `PgNotifyBroadcasterTests`, `NotifyRelayHostTests`) and Task 5 (the reconnect, relay and failure counters).
4. **Presence correctness.** TTL expiry, `LeaveAll` on disconnect, broadcast only on change (with the heartbeat rule), concurrency safety, names only to agents.
   - Pinned in Task 1 (`UpdateTicketPresenceHandlerTests`) and Task 2 (`InMemoryTicketPresenceStoreTests`, including the contention test and the "store does not grow" test) and Task 3 (two agents see each other come, reply and go; the name is `Agent.Name`).
5. **Architecture rules.** Handlers depend only on approved abstractions; Contracts naming; cancellation passed through.
   - Pinned in Task 1 (the existing architecture tests run against the new types; `UpdateTicketPresenceHandlerTests` and `RelayTicketChangeHandlerTests` check the token reaches each dependency) and Task 3 (`HubPolicyCoverageTests`: the hub's constructor and methods).

---

### Task 1: Contracts, the Application abstractions and handlers, and the D-046 documentation

**Review Focus pin:** 4 (the handler broadcasts only on a real change, re-checks the agent, checks the ticket exists) and 5 (handler dependencies, the cancellation token, Contracts naming), and the payload of Focus 1 (ids only). The relay handler never throws on a bad payload. This task also records the owner's decisions and the spike's findings as D-046, corrects the PHASE-10 spec and 02-ARCHITECTURE, and fixes the stale "pending merge" roadmap rows for 07, 08 and 09.

**Files:**

- Create: `src/TechStrap.Contracts/Live/LiveNames.cs`, `src/TechStrap.Contracts/Live/LiveDtos.cs`, `src/TechStrap.Contracts/Live/LiveRequests.cs`
- Create: `src/TechStrap.Application/Live/ITicketChangeBroadcaster.cs`, `ITicketPresenceStore.cs`, `TicketChange.cs`, `TicketPresenceMapping.cs`, `LiveErrors.cs`, `RelayTicketChangeHandler.cs`, `UpdateTicketPresenceHandler.cs`
- Test (create): `tests/TechStrap.Application.Tests/Live/LiveContractsTests.cs`, `RelayTicketChangeHandlerTests.cs`, `UpdateTicketPresenceHandlerTests.cs`
- Modify: `docs/architecture/04-DECISION-LOG.md`, `docs/architecture/PHASE-10-live-updates.md`, `docs/architecture/02-ARCHITECTURE.md`, `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`, `docs/architecture/00-DISCOVERY-INDEX.md`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`

**Interfaces:**
- Consumes: `IAgentRepository.GetBySubjectAsync(string, CancellationToken)` (`Agent.Id`, `Name`, `Email`, `IsActive`), `ITicketRepository.GetStateAsync(Guid, CancellationToken)`, `AgentErrors.NotProvisioned()` and `AgentErrors.Inactive()` (internal to Application), `Result` and `ResultError(code, message, ResultErrorKind, target)` from `SyntaxCircus.Common`, `TicketEventTypes` (Contracts), the Domain's `TicketEventType`.
- Produces:
  - Contracts (`TechStrap.Contracts.Live`): `TicketChangedDto(Guid EventId, Guid TicketId, string TicketNumber, Guid ProductId, string EventType, Guid? ActorAgentId, DateTimeOffset OccurredAt, string Kind)`, `TicketViewerDto(Guid AgentId, string DisplayName, string State)`, `TicketPresenceDto(Guid TicketId, IReadOnlyList<TicketViewerDto> Viewers)`, `UpdateTicketPresenceRequest(string Action, string AgentSubject, string ConnectionId, Guid? TicketId, bool IsComposing)`, `RelayTicketChangeRequest(string Payload)`; the constants `TicketHubRoutes.Path`, `TicketHubMethods`, `TicketHubGroups` (`Queue`, `Ticket(Guid)`), `TicketChangeKinds`, `TicketPresenceStates`, `TicketPresenceActions`, `TicketLiveLimits` (`ComposingTtlSeconds = 10`, `MaxChangePayloadBytes = 2048`).
  - Application (`TechStrap.Application.Live`): `TicketChange` (with `Resync(Guid, DateTimeOffset)`, `From(dto)`, `ToDto()`), `ITicketChangeBroadcaster` (`PublishAsync(TicketChange, CancellationToken)`, `PublishPresenceAsync(TicketPresence, CancellationToken)`), `ITicketPresenceStore` (`Join`, `SetComposing`, `Leave`, `LeaveAll`, `Get`, all synchronous and thread-safe) with `TicketViewerState`, `TicketViewer`, `TicketPresence`, `PresenceChange(bool Changed, TicketPresence Presence)`, `TicketPresenceMapping.ToDto()`, `IUpdateTicketPresenceHandler.HandleAsync(UpdateTicketPresenceRequest, CancellationToken)` returning `Result<TicketPresence>`, `IRelayTicketChangeHandler.HandleAsync(RelayTicketChangeRequest, CancellationToken)` returning `Result`.
  - Documentation: D-046 (decision log, header bullet and index row), the PHASE-10 "Corrections (D-046, 2026-10-07)" section, the corrected 02-ARCHITECTURE handler table, the corrected roadmap and discovery rows, and the Pester pins that hold them.

- [ ] **Step 1: Keep the helper tools outside the repository**

Every mutation step of this plan uses two small tools: `mut.py` applies one or more replacements to a file, runs a command, reports KILLED, SURVIVED or NOT A MUTATION (the mutated code does not compile) and always puts the file back; `run_muts.py` runs a list of mutations (a spec file) in the foreground, one at a time, and appends one line per mutation to a results file. Keep them OUTSIDE the repository, for example in `C:\tmp\p10a-tools\` (the plan calls that folder `$T`; the specs go in `$T\specs\`). They are the 09d tools unchanged. Two more files live there: `docs_10a.py` (Tasks 1 and 6 apply the documentation edits with it) and the two Pester patch scripts of Tasks 1 and 6.

`mut.py`

```python
"""Mutation helper for the PHASE-09d plan. Keep it OUTSIDE the repository (for example in a temp folder).

usage: python mut.py <file> --replace <old> <new> [--replace <old> <new> ...] -- <command ...>

Applies each replacement to <file> (each <old> must match exactly once; "\\n" in an argument means a newline), runs the command in the current
directory, prints the lines that summarise the run, and ALWAYS puts the file back. The mutation is KILLED when the command fails (exit code 0), a SURVIVOR
(exit code 3) when it passes and NOT A MUTATION (exit code 4) when the mutated code does not compile: pick another mutation. Run it from the repository root, after `git add`-ing the task's files, so a failed run can also be undone with
`git checkout -- <file>`.
"""
import subprocess
import sys

args = sys.argv[1:]
if "--" not in args or "--replace" not in args:
    sys.exit(__doc__)
split = args.index("--")
head, command = args[:split], args[split + 1 :]
path = head[0]
pairs = []
rest = head[1:]
while rest:
    if rest[0] != "--replace" or len(rest) < 3:
        sys.exit(__doc__)
    pairs.append((rest[1].replace("\\n", "\n"), rest[2].replace("\\n", "\n")))
    rest = rest[3:]

with open(path, encoding="utf-8", newline="") as handle:
    original = handle.read()
crlf = "\r\n" in original
mutated = original
for old, new in pairs:
    if crlf:
        old, new = old.replace("\n", "\r\n"), new.replace("\n", "\r\n")
    if mutated.count(old) != 1:
        sys.exit(f"ABORT: {mutated.count(old)} occurrences of {old[:70]!r} in {path}")
    mutated = mutated.replace(old, new)

try:
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(mutated)
    result = subprocess.run(command, capture_output=True, text=True, timeout=900)
    lines = [line.strip() for line in (result.stdout + result.stderr).splitlines() if line.strip()]
    summary = [line for line in lines if line.startswith(("total:", "failed:", "succeeded:", "Tests Passed", "error"))
               or "Tests Passed" in line or "[-]" in line]
    print("\n".join(summary[:6]))
    broken = any("error CS" in line or line.startswith("Build failed") for line in lines)
    if broken:
        print("\n".join(line[-200:] for line in lines if "error CS" in line)[:600])
    print("NOT A MUTATION: the mutated code does not compile" if broken else "KILLED (the command failed)" if result.returncode != 0 else "SURVIVED (the command passed)")
    code = 4 if broken else 0 if result.returncode != 0 else 3
finally:
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(original)
sys.exit(code)
```

`run_muts.py`

```python
"""run_muts.py <spec.py> <out.txt>: runs each mutation of spec.MUTATIONS in the foreground through mut.py, appends 'id: KILLED|SURVIVED|ABORT' to out.txt.

spec.MUTATIONS = [(id, file, [(old, new), ...], [command...]), ...]  (run from the repo root)
"""
import importlib.util
import os
import subprocess
import sys

spec_path, out_path = sys.argv[1], sys.argv[2]
spec = importlib.util.spec_from_file_location("spec", spec_path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
only = set(sys.argv[3:])
mut = os.path.join(os.path.dirname(os.path.abspath(__file__)), "mut.py")
for ident, path, pairs, command in module.MUTATIONS:
    if only and ident.split()[0] not in only:
        continue
    args = [sys.executable, mut, path]
    for old, new in pairs:
        args += ["--replace", old, new]
    args += ["--"] + command
    result = subprocess.run(args, capture_output=True, text=True)
    text = (result.stdout + result.stderr).strip().splitlines()
    verdict = {0: "KILLED", 3: "SURVIVED", 4: "NOT-COMPILING"}.get(result.returncode, "ABORT")
    with open(out_path, "a", encoding="utf-8") as handle:
        handle.write(f"{ident}: {verdict}  | {' / '.join(text[-3:])[:200]}\n")
```

Run every mutation from the repository root, after `git add -A`-ing the task's files, so `git checkout -- <file>` also restores a file if a run is interrupted, and never run two batches at once or stage while one runs (the mutated file would end up in the index). In the spec files, `INFRA`, `API` and `APP` are `dotnet test --project tests/TechStrap.<X>.Tests -c Release --filter-query` and `PESTER` is `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1`. Example, from the repository root:

```bash
python $T/run_muts.py $T/specs/m1.py $T/res_m1.txt 1 2 3
```

- [ ] **Step 2: Write the failing contract and handler tests**

`tests/TechStrap.Application.Tests/Live/LiveContractsTests.cs` (new): the DTOs round trip with the web (camelCase) defaults and carry ids only; the names that cross the process boundary are pinned; a change maps to its DTO and back; a resync names no ticket.

```csharp
using System.Text.Json;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Live;

/// <summary>
/// The live-update wire contract: the DTOs survive a System.Text.Json round trip with the web (camelCase) defaults every host uses, the names that cross
/// the process boundary are pinned, and the string constants stay in step with the enums they mirror.
/// </summary>
public sealed class LiveContractsTests
{
    private static readonly Guid TicketId = Guid.Parse("0197f2a0-0000-7000-8000-000000000001");
    private static readonly Guid EventId = Guid.Parse("0197f2a0-0000-7000-8000-000000000002");
    private static readonly Guid ProductId = Guid.Parse("0197f2a0-0000-7000-8000-000000000003");
    private static readonly Guid AgentId = Guid.Parse("0197f2a0-0000-7000-8000-000000000004");
    private static readonly DateTimeOffset At = new(2026, 10, 7, 9, 30, 0, TimeSpan.Zero);

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonSerializerOptions.Web), JsonSerializerOptions.Web)!;

    [Fact]
    public void A_ticket_change_round_trips_and_is_camel_case()
    {
        var dto = new TicketChangedDto(EventId, TicketId, "ORB-42", ProductId, TicketEventTypes.StatusChanged, AgentId, At, TicketChangeKinds.Updated);

        RoundTrip(dto).ShouldBe(dto);
        var json = JsonSerializer.Serialize(dto, JsonSerializerOptions.Web);
        json.ShouldContain("\"eventId\":");
        json.ShouldContain("\"ticketNumber\":\"ORB-42\"");
        json.ShouldContain("\"actorAgentId\":");
        json.ShouldContain("\"kind\":\"Updated\"");
    }

    [Fact]
    public void A_ticket_change_without_an_actor_round_trips_with_a_null_actor()
    {
        var dto = new TicketChangedDto(EventId, TicketId, "ORB-42", ProductId, TicketEventTypes.StatusChanged, null, At, TicketChangeKinds.Updated);

        RoundTrip(dto).ActorAgentId.ShouldBeNull();
    }

    [Fact]
    public void A_ticket_change_carries_ids_and_names_only()
    {
        // The payload never holds customer text (D-018): every property is an id, a wire name, a ticket number or a time.
        typeof(TicketChangedDto).GetProperties().Select(property => property.Name).ShouldBe(
            ["EventId", "TicketId", "TicketNumber", "ProductId", "EventType", "ActorAgentId", "OccurredAt", "Kind"]);
    }

    [Fact]
    public void Presence_round_trips_with_its_viewers()
    {
        var dto = new TicketPresenceDto(TicketId, [new TicketViewerDto(AgentId, "Sam", TicketPresenceStates.Composing)]);

        var copy = RoundTrip(dto);

        copy.TicketId.ShouldBe(TicketId);
        copy.Viewers.ShouldBe(dto.Viewers);
        JsonSerializer.Serialize(dto, JsonSerializerOptions.Web).ShouldContain("\"displayName\":\"Sam\"");
    }

    [Fact]
    public void The_request_records_round_trip()
    {
        RoundTrip(new RelayTicketChangeRequest("{}")).ShouldBe(new RelayTicketChangeRequest("{}"));
        var presence = new UpdateTicketPresenceRequest(TicketPresenceActions.SetComposing, "sam", "conn-1", TicketId, true);
        RoundTrip(presence).ShouldBe(presence);
    }

    [Fact]
    public void The_names_that_cross_the_process_boundary_are_pinned()
    {
        TicketHubRoutes.Path.ShouldBe("/hubs/tickets");
        TicketHubMethods.JoinTicket.ShouldBe("JoinTicket");
        TicketHubMethods.LeaveTicket.ShouldBe("LeaveTicket");
        TicketHubMethods.SetComposing.ShouldBe("SetComposing");
        TicketHubMethods.TicketChanged.ShouldBe("TicketChanged");
        TicketHubMethods.PresenceChanged.ShouldBe("PresenceChanged");
        TicketHubGroups.Queue.ShouldBe("queue");
        TicketHubGroups.Ticket(TicketId).ShouldBe("ticket:0197f2a0-0000-7000-8000-000000000001");
        TicketChangeKinds.Created.ShouldBe("Created");
        TicketChangeKinds.Updated.ShouldBe("Updated");
        TicketChangeKinds.Resync.ShouldBe("Resync");
        TicketLiveLimits.ComposingTtlSeconds.ShouldBe(10);
        TicketLiveLimits.MaxChangePayloadBytes.ShouldBe(2048);
    }

    [Fact]
    public void Every_constant_in_a_group_is_distinct()
    {
        foreach (var holder in new[] { typeof(TicketHubMethods), typeof(TicketChangeKinds), typeof(TicketPresenceStates), typeof(TicketPresenceActions) })
        {
            var values = holder.GetFields().Where(field => field.IsLiteral && field.FieldType == typeof(string)).Select(field => (string)field.GetRawConstantValue()!).ToList();
            values.Distinct().Count().ShouldBe(values.Count, holder.Name);
        }
    }

    [Fact]
    public void The_viewer_states_match_the_wire_names()
    {
        Enum.GetNames<TicketViewerState>().ShouldBe(
            [TicketPresenceStates.Viewing, TicketPresenceStates.Composing], ignoreOrder: true);
    }

    [Fact]
    public void A_change_maps_to_its_dto_and_back()
    {
        var change = new TicketChange(EventId, TicketId, "ORB-42", ProductId, TicketEventTypes.Assigned, AgentId, At, TicketChangeKinds.Created);

        var dto = change.ToDto();

        dto.ShouldBe(new TicketChangedDto(EventId, TicketId, "ORB-42", ProductId, TicketEventTypes.Assigned, AgentId, At, TicketChangeKinds.Created));
        TicketChange.From(dto).ShouldBe(change);
    }

    [Fact]
    public void Presence_maps_to_its_dto_with_wire_state_names()
    {
        var presence = new TicketPresence(TicketId, [new TicketViewer(AgentId, "Sam", TicketViewerState.Composing), new TicketViewer(EventId, "Kim", TicketViewerState.Viewing)]);

        var dto = presence.ToDto();

        dto.TicketId.ShouldBe(TicketId);
        dto.Viewers.ShouldBe(
            [new TicketViewerDto(AgentId, "Sam", TicketPresenceStates.Composing), new TicketViewerDto(EventId, "Kim", TicketPresenceStates.Viewing)]);
    }

    [Fact]
    public void A_resync_names_no_ticket()
    {
        var resync = TicketChange.Resync(EventId, At);

        resync.Kind.ShouldBe(TicketChangeKinds.Resync);
        resync.TicketId.ShouldBe(Guid.Empty);
        resync.EventId.ShouldBe(EventId);
        resync.OccurredAt.ShouldBe(At);
    }

    [Fact]
    public void The_event_type_names_are_the_domain_ones()
    {
        // A TicketChange.EventType is a TicketEventTypes wire name, which TicketNamesParityTests already ties to the domain enum.
        Enum.TryParse<TicketEventType>(TicketEventTypes.ProductChanged, out _).ShouldBeTrue();
    }
}
```

`tests/TechStrap.Application.Tests/Live/RelayTicketChangeHandlerTests.cs` (new): a valid change is forwarded once; a malformed, oversize (counted in bytes), unknown-kind, unknown-event-type or incomplete payload is a failure Result and is never forwarded; a broadcaster that throws is a failure Result, and a cancellation still cancels.

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyntaxCircus.Common;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Application.Tests.Live;

public sealed class RelayTicketChangeHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly ITicketChangeBroadcaster _broadcaster = Substitute.For<ITicketChangeBroadcaster>();
    private readonly RelayTicketChangeHandler _handler;

    public RelayTicketChangeHandlerTests() =>
        _handler = new RelayTicketChangeHandler(_broadcaster, NullLogger<RelayTicketChangeHandler>.Instance);

    private static TicketChangedDto Valid(string kind = TicketChangeKinds.Updated, string eventType = TicketEventTypes.StatusChanged) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "ORB-42", Guid.NewGuid(), eventType, Guid.NewGuid(), new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero), kind);

    private static RelayTicketChangeRequest Request(TicketChangedDto dto) =>
        new(JsonSerializer.Serialize(dto, JsonSerializerOptions.Web));

    [Fact]
    public async Task A_valid_change_is_forwarded_once_with_every_field()
    {
        var dto = Valid();

        var result = await _handler.HandleAsync(Request(dto), Ct);

        result.IsSuccess.ShouldBeTrue();
        await _broadcaster.Received(1).PublishAsync(Arg.Any<TicketChange>(), Arg.Any<CancellationToken>());
        await _broadcaster.Received(1).PublishAsync(TicketChange.From(dto), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_broadcaster()
    {
        using var source = new CancellationTokenSource();

        await _handler.HandleAsync(Request(Valid()), source.Token);

        await _broadcaster.Received(1).PublishAsync(Arg.Any<TicketChange>(), source.Token);
    }

    [Theory]
    [InlineData(TicketChangeKinds.Created)]
    [InlineData(TicketChangeKinds.Updated)]
    public async Task Both_ordinary_kinds_are_accepted(string kind)
    {
        (await _handler.HandleAsync(Request(Valid(kind)), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_resync_needs_no_ticket()
    {
        var resync = new TicketChangedDto(Guid.NewGuid(), Guid.Empty, string.Empty, Guid.Empty, string.Empty, null, DateTimeOffset.UnixEpoch, TicketChangeKinds.Resync);

        var result = await _handler.HandleAsync(Request(resync), Ct);

        result.IsSuccess.ShouldBeTrue();
        await _broadcaster.Received(1).PublishAsync(Arg.Is<TicketChange>(change => change.Kind == TicketChangeKinds.Resync), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"kind\":\"Updated\"}")]
    [InlineData("{\"eventId\":\"nope\"}")]
    public async Task A_malformed_payload_is_a_failure_result_and_is_never_forwarded(string payload)
    {
        var result = await _handler.HandleAsync(new RelayTicketChangeRequest(payload), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Validation);
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task A_null_payload_is_a_failure_result()
    {
        var result = await _handler.HandleAsync(new RelayTicketChangeRequest(null!), Ct);

        result.IsFailure.ShouldBeTrue();
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task An_unknown_kind_is_refused()
    {
        var result = await _handler.HandleAsync(Request(Valid(kind: "Deleted")), Ct);

        result.IsFailure.ShouldBeTrue();
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task An_unknown_event_type_is_refused()
    {
        var result = await _handler.HandleAsync(Request(Valid(eventType: "Exploded")), Ct);

        result.IsFailure.ShouldBeTrue();
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Theory]
    [InlineData("ticket")]
    [InlineData("event")]
    [InlineData("product")]
    [InlineData("number")]
    [InlineData("time")]
    public async Task An_ordinary_change_with_a_missing_field_is_refused(string missing)
    {
        var valid = Valid();
        var broken = missing switch
        {
            "ticket" => valid with { TicketId = Guid.Empty },
            "event" => valid with { EventId = Guid.Empty },
            "product" => valid with { ProductId = Guid.Empty },
            "number" => valid with { TicketNumber = " " },
            _ => valid with { OccurredAt = default },
        };

        var result = await _handler.HandleAsync(Request(broken), Ct);

        result.IsFailure.ShouldBeTrue();
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task A_ticket_number_that_is_far_too_long_is_refused()
    {
        var result = await _handler.HandleAsync(Request(Valid() with { TicketNumber = new string('X', 65) }), Ct);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task An_oversize_payload_is_refused_before_it_is_parsed()
    {
        var oversize = new string(' ', TicketLiveLimits.MaxChangePayloadBytes + 1);

        var result = await _handler.HandleAsync(new RelayTicketChangeRequest(oversize), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("ticket-change-too-large");
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task The_size_cap_counts_bytes_not_characters()
    {
        // 1,100 two-byte characters are 2,200 bytes: over the cap although only 1,100 characters.
        var payload = new string((char)0xE9, 1100);

        var result = await _handler.HandleAsync(new RelayTicketChangeRequest(payload), Ct);

        result.Errors[0].Code.ShouldBe("ticket-change-too-large");
    }

    [Fact]
    public async Task A_valid_payload_exactly_at_the_cap_is_still_parsed()
    {
        var json = JsonSerializer.Serialize(Valid(), JsonSerializerOptions.Web);
        var padded = json + new string(' ', TicketLiveLimits.MaxChangePayloadBytes - json.Length);

        var result = await _handler.HandleAsync(new RelayTicketChangeRequest(padded), Ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_broadcaster_that_throws_is_a_failure_result_not_an_exception()
    {
        _broadcaster.PublishAsync(Arg.Any<TicketChange>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("hub is down"));

        var result = await _handler.HandleAsync(Request(Valid()), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("ticket-change-relay-failed");
    }

    [Fact]
    public async Task A_cancelled_relay_still_cancels()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        _broadcaster.PublishAsync(Arg.Any<TicketChange>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException(source.Token));

        await Should.ThrowAsync<OperationCanceledException>(() => _handler.HandleAsync(Request(Valid()), source.Token));
    }
}
```

`tests/TechStrap.Application.Tests/Live/UpdateTicketPresenceHandlerTests.cs` (new): an unknown ticket, a missing agent and a deactivated agent are refused before the store is touched; a join broadcasts once on a change and never on a repeat but always returns the snapshot; the name is `Agent.Name` (the email when there is none, never the public display name); the token reaches every dependency; a broadcaster failure never fails the call; leaving and leaving everything need no agent lookup; composing needs a join first; a bad action, connection or missing ticket is a validation failure.

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyntaxCircus.Common;
using TechStrap.Application.Live;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Contracts.Live;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Live;

public sealed class UpdateTicketPresenceHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly Guid TicketId = Guid.Parse("0197f2a0-0000-7000-8000-000000000001");
    private static readonly Guid OtherTicketId = Guid.Parse("0197f2a0-0000-7000-8000-000000000009");

    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();
    private readonly ITicketPresenceStore _store = Substitute.For<ITicketPresenceStore>();
    private readonly ITicketChangeBroadcaster _broadcaster = Substitute.For<ITicketChangeBroadcaster>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));
    private readonly Agent _sam;
    private readonly UpdateTicketPresenceHandler _handler;

    public UpdateTicketPresenceHandlerTests()
    {
        _sam = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        _agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns(_sam);
        _tickets.GetStateAsync(TicketId, Arg.Any<CancellationToken>()).Returns(State(TicketId));
        _handler = new UpdateTicketPresenceHandler(_agents, _tickets, _store, _broadcaster, NullLogger<UpdateTicketPresenceHandler>.Instance);
    }

    private static TicketState State(Guid id) =>
        new(id, "ORB-1", TicketStatus.Open, TicketPriority.Normal, Guid.NewGuid(), null, false, [], DateTimeOffset.UnixEpoch, 1);

    private static TicketPresence Presence(Guid ticketId, params TicketViewer[] viewers) => new(ticketId, viewers);

    private TicketViewer Sam(TicketViewerState state = TicketViewerState.Viewing) => new(_sam.Id, "Sam", state);

    private static UpdateTicketPresenceRequest Request(string action, Guid? ticketId = null, bool isComposing = false, string subject = "sam", string connection = "conn-1") =>
        new(action, subject, connection, ticketId, isComposing);

    private Task<Result<TicketPresence>> HandleAsync(UpdateTicketPresenceRequest request) => _handler.HandleAsync(request, Ct);

    [Fact]
    public async Task Joining_an_unknown_ticket_is_not_found_and_touches_nothing()
    {
        _tickets.GetStateAsync(OtherTicketId, Arg.Any<CancellationToken>()).Returns((TicketState?)null);

        var result = await HandleAsync(Request(TicketPresenceActions.Join, OtherTicketId));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.NotFound);
        _store.ReceivedCalls().ShouldBeEmpty();
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishPresenceAsync(default!, Ct);
    }

    [Fact]
    public async Task Joining_as_an_agent_with_no_row_is_refused()
    {
        var result = await HandleAsync(Request(TicketPresenceActions.Join, TicketId, subject: "ghost"));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Forbidden);
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Joining_as_a_deactivated_agent_is_refused()
    {
        _sam.SetActive(false);

        var result = await HandleAsync(Request(TicketPresenceActions.Join, TicketId));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Forbidden);
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_join_that_changes_the_state_broadcasts_once_and_returns_the_snapshot()
    {
        var presence = Presence(TicketId, Sam());
        _store.Join("conn-1", TicketId, _sam.Id, "Sam").Returns(new PresenceChange(true, presence));

        var result = await HandleAsync(Request(TicketPresenceActions.Join, TicketId));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(presence);
        await _broadcaster.Received(1).PublishPresenceAsync(presence, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_join_that_changes_nothing_does_not_broadcast_but_still_returns_the_snapshot()
    {
        var presence = Presence(TicketId, Sam());
        _store.Join("conn-1", TicketId, _sam.Id, "Sam").Returns(new PresenceChange(false, presence));

        var result = await HandleAsync(Request(TicketPresenceActions.Join, TicketId));

        result.Value.ShouldBe(presence);
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishPresenceAsync(default!, Ct);
    }

    [Fact]
    public async Task The_name_shown_is_the_agents_own_name_and_falls_back_to_the_email()
    {
        var unnamed = Agent.Create("kim", null, "kim@example.com", AgentRole.Agent, _clock).Value;
        _agents.GetBySubjectAsync("kim", Arg.Any<CancellationToken>()).Returns(unnamed);
        _store.Join(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>()).Returns(new PresenceChange(false, Presence(TicketId)));

        await HandleAsync(Request(TicketPresenceActions.Join, TicketId, subject: "kim"));
        await HandleAsync(Request(TicketPresenceActions.Join, TicketId));

        _store.Received(1).Join("conn-1", TicketId, unnamed.Id, "kim@example.com");
        _store.Received(1).Join("conn-1", TicketId, _sam.Id, "Sam");
    }

    [Fact]
    public async Task The_public_display_name_is_never_used()
    {
        _sam.SetPublicDisplayName("Sammy from Support");
        _store.Join(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>()).Returns(new PresenceChange(false, Presence(TicketId)));

        await HandleAsync(Request(TicketPresenceActions.Join, TicketId));

        _store.Received(1).Join("conn-1", TicketId, _sam.Id, "Sam");
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_repositories_and_the_broadcaster()
    {
        using var source = new CancellationTokenSource();
        var presence = Presence(TicketId, Sam());
        _store.Join(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>()).Returns(new PresenceChange(true, presence));

        await _handler.HandleAsync(Request(TicketPresenceActions.Join, TicketId), source.Token);

        await _agents.Received(1).GetBySubjectAsync("sam", source.Token);
        await _tickets.Received(1).GetStateAsync(TicketId, source.Token);
        await _broadcaster.Received(1).PublishPresenceAsync(presence, source.Token);
    }

    [Fact]
    public async Task A_broadcaster_failure_does_not_fail_the_join()
    {
        var presence = Presence(TicketId, Sam());
        _store.Join(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>()).Returns(new PresenceChange(true, presence));
        _broadcaster.PublishPresenceAsync(Arg.Any<TicketPresence>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("hub is down"));

        var result = await HandleAsync(Request(TicketPresenceActions.Join, TicketId));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Leaving_broadcasts_only_when_the_state_changed()
    {
        var presence = Presence(TicketId);
        _store.Leave("conn-1", TicketId).Returns(new PresenceChange(true, presence), new PresenceChange(false, presence));

        (await HandleAsync(Request(TicketPresenceActions.Leave, TicketId))).IsSuccess.ShouldBeTrue();
        (await HandleAsync(Request(TicketPresenceActions.Leave, TicketId))).IsSuccess.ShouldBeTrue();

        await _broadcaster.Received(1).PublishPresenceAsync(presence, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Leaving_needs_no_agent_lookup_so_a_deactivated_agent_can_always_clean_up()
    {
        _sam.SetActive(false);
        _store.Leave("conn-1", TicketId).Returns(new PresenceChange(true, Presence(TicketId)));

        var result = await HandleAsync(Request(TicketPresenceActions.Leave, TicketId));

        result.IsSuccess.ShouldBeTrue();
        await _agents.DidNotReceiveWithAnyArgs().GetBySubjectAsync(default!, Ct);
    }

    [Fact]
    public async Task Leaving_everything_broadcasts_one_update_per_ticket_that_changed_and_none_for_the_rest()
    {
        var first = Presence(TicketId);
        var second = Presence(OtherTicketId);
        _store.LeaveAll("conn-1").Returns([new PresenceChange(true, first), new PresenceChange(false, second)]);

        var result = await HandleAsync(Request(TicketPresenceActions.LeaveAll));

        result.IsSuccess.ShouldBeTrue();
        await _broadcaster.Received(1).PublishPresenceAsync(first, Arg.Any<CancellationToken>());
        await _broadcaster.DidNotReceive().PublishPresenceAsync(second, Arg.Any<CancellationToken>());
        await _agents.DidNotReceiveWithAnyArgs().GetBySubjectAsync(default!, Ct);
    }

    [Fact]
    public async Task One_failing_broadcast_does_not_stop_the_other_leaves_from_being_sent()
    {
        var first = Presence(TicketId);
        var second = Presence(OtherTicketId);
        _store.LeaveAll("conn-1").Returns([new PresenceChange(true, first), new PresenceChange(true, second)]);
        _broadcaster.PublishPresenceAsync(first, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("x"));

        var result = await HandleAsync(Request(TicketPresenceActions.LeaveAll));

        result.IsSuccess.ShouldBeTrue();
        await _broadcaster.Received(1).PublishPresenceAsync(second, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Composing_for_a_ticket_the_connection_never_joined_is_a_failure_and_broadcasts_nothing()
    {
        _store.SetComposing("conn-1", TicketId, true).Returns((PresenceChange?)null);

        var result = await HandleAsync(Request(TicketPresenceActions.SetComposing, TicketId, isComposing: true));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("presence-not-joined");
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishPresenceAsync(default!, Ct);
    }

    [Fact]
    public async Task Composing_broadcasts_on_a_change_and_not_on_a_repeat()
    {
        var presence = Presence(TicketId, Sam(TicketViewerState.Composing));
        _store.SetComposing("conn-1", TicketId, true).Returns(new PresenceChange(true, presence), new PresenceChange(false, presence));

        await HandleAsync(Request(TicketPresenceActions.SetComposing, TicketId, isComposing: true));
        await HandleAsync(Request(TicketPresenceActions.SetComposing, TicketId, isComposing: true));

        await _broadcaster.Received(1).PublishPresenceAsync(presence, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Composing_as_a_deactivated_agent_is_refused()
    {
        _sam.SetActive(false);

        var result = await HandleAsync(Request(TicketPresenceActions.SetComposing, TicketId, isComposing: true));

        result.IsFailure.ShouldBeTrue();
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Teleport", "conn-1", true)]
    [InlineData("", "conn-1", true)]
    [InlineData(TicketPresenceActions.Join, "", true)]
    [InlineData(TicketPresenceActions.Join, "   ", true)]
    [InlineData(TicketPresenceActions.Join, "conn-1", false)]
    [InlineData(TicketPresenceActions.Leave, "conn-1", false)]
    [InlineData(TicketPresenceActions.SetComposing, "conn-1", false)]
    public async Task A_request_with_a_bad_action_connection_or_missing_ticket_is_a_validation_failure(string action, string connection, bool hasTicket)
    {
        var result = await HandleAsync(new UpdateTicketPresenceRequest(action, "sam", connection, hasTicket ? TicketId : null, false));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Validation);
        _store.ReceivedCalls().ShouldBeEmpty();
        await _agents.DidNotReceiveWithAnyArgs().GetBySubjectAsync(default!, Ct);
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

```bash
dotnet test --project tests/TechStrap.Application.Tests -c Release
```

Expected: FAIL to compile, because none of the types exists yet:

```text
tests/TechStrap.Application.Tests/Live/LiveContractsTests.cs(2,29): error CS0234: The type or namespace name 'Live' does no...
tests/TechStrap.Application.Tests/Live/LiveContractsTests.cs(3,27): error CS0234: The type or namespace name 'Live' does no...
tests/TechStrap.Application.Tests/Live/RelayTicketChangeHandlerTests.cs(6,29): error CS0234: The type or namespace name 'Li...
tests/TechStrap.Application.Tests/Live/RelayTicketChangeHandlerTests.cs(7,27): error CS0234: The type or namespace name 'Li...
tests/TechStrap.Application.Tests/Live/UpdateTicketPresenceHandlerTests.cs(6,29): error CS0234: The type or namespace name ...
tests/TechStrap.Application.Tests/Live/UpdateTicketPresenceHandlerTests.cs(9,27): error CS0234: The type or namespace name ...
... (25 more)
```

- [ ] **Step 4: Write the Contracts**

`src/TechStrap.Contracts/Live/LiveNames.cs` (new): only static constant classes, because Contracts carries no enum. (`TicketHubMessages` arrives with the hub in Task 3.)

```csharp
namespace TechStrap.Contracts.Live;

/// <summary>Where the agent hub is mapped. The Admin builds its hub address from the API base address and this path.</summary>
public static class TicketHubRoutes
{
    public const string Path = "/hubs/tickets";
}

/// <summary>
/// Hub method names (D-018, D-046). The first three are invoked by the client, the last two are pushed by the server. They cross
/// a process boundary, so they are constants and never typed twice.
/// </summary>
public static class TicketHubMethods
{
    public const string JoinTicket = "JoinTicket";
    public const string LeaveTicket = "LeaveTicket";
    public const string SetComposing = "SetComposing";
    public const string TicketChanged = "TicketChanged";
    public const string PresenceChanged = "PresenceChanged";
}

/// <summary>
/// Hub group names. Every connection is in <see cref="Queue"/> and receives every <c>TicketChanged</c> (clients filter by ticket id, D-046);
/// <see cref="Ticket"/> groups carry presence only.
/// </summary>
public static class TicketHubGroups
{
    public const string Queue = "queue";

    private const string TicketPrefix = "ticket:";

    public static string Ticket(Guid ticketId) => TicketPrefix + ticketId.ToString("D");
}

/// <summary>Wire names for the kind of a ticket change. Contracts carries no enums (naming rule). <c>Resync</c> names no ticket: the client reloads everything.</summary>
public static class TicketChangeKinds
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Resync = "Resync";
}

/// <summary>Wire names for what an agent is doing on a ticket page. Contracts carries no enums (naming rule).</summary>
public static class TicketPresenceStates
{
    public const string Viewing = "Viewing";
    public const string Composing = "Composing";
}

/// <summary>The presence actions a hub call asks <c>UpdateTicketPresenceHandler</c> for.</summary>
public static class TicketPresenceActions
{
    public const string Join = "Join";
    public const string Leave = "Leave";
    public const string LeaveAll = "LeaveAll";
    public const string SetComposing = "SetComposing";
}

/// <summary>Limits shared by the server and the Admin client.</summary>
public static class TicketLiveLimits
{
    /// <summary>A composing hint that is not refreshed within this many seconds counts as viewing again.</summary>
    public const int ComposingTtlSeconds = 10;

    /// <summary>
    /// The largest change payload (UTF-8 bytes) the relay accepts. A real payload is about 300 bytes; Postgres allows 8,000 bytes in a NOTIFY, so
    /// the cap is a safety margin against a stray writer, not a design limit.
    /// </summary>
    public const int MaxChangePayloadBytes = 2048;
}
```

`src/TechStrap.Contracts/Live/LiveDtos.cs` (new)

```csharp
namespace TechStrap.Contracts.Live;

/// <summary>
/// One change to one ticket, pushed to every agent's queue. It carries ids, a wire name and the ticket number only: never customer text or a name
/// (D-018). Clients refresh through the normal REST calls. <paramref name="EventId"/> lets a client de-duplicate (D-046); a <c>Resync</c> has
/// empty ids and means "reload everything".
/// </summary>
public sealed record TicketChangedDto(
    Guid EventId, Guid TicketId, string TicketNumber, Guid ProductId, string EventType, Guid? ActorAgentId, DateTimeOffset OccurredAt, string Kind);

/// <summary>An agent who has a ticket open. <paramref name="DisplayName"/> is the agent's internal name and goes to other agents only (D-046).</summary>
public sealed record TicketViewerDto(Guid AgentId, string DisplayName, string State);

/// <summary>Who has a ticket open right now: one entry per agent, however many tabs they have.</summary>
public sealed record TicketPresenceDto(Guid TicketId, IReadOnlyList<TicketViewerDto> Viewers);
```

`src/TechStrap.Contracts/Live/LiveRequests.cs` (new)

```csharp
namespace TechStrap.Contracts.Live;

/// <summary>
/// A presence call from the hub. The hub fills it from its own caller (<c>Context.User</c> and <c>Context.ConnectionId</c>), never from client input,
/// so a client cannot speak for another agent. <paramref name="TicketId"/> is null for <c>LeaveAll</c>.
/// </summary>
public sealed record UpdateTicketPresenceRequest(string Action, string AgentSubject, string ConnectionId, Guid? TicketId, bool IsComposing);

/// <summary>A payload the Postgres listener received, exactly as text: the handler parses and validates it, because anything could write to the channel.</summary>
public sealed record RelayTicketChangeRequest(string Payload);
```

- [ ] **Step 5: Write the Application abstractions and handlers**

`src/TechStrap.Application/Live/TicketChange.cs` (new)

```csharp
using TechStrap.Contracts.Live;

namespace TechStrap.Application.Live;

/// <summary>
/// A change to one ticket, as Application sees it (D-018): the post-commit hook builds it from the committed event, the broadcasters send it.
/// It holds ids, a wire name and the ticket number only. <paramref name="Kind"/> is a <see cref="TicketChangeKinds"/> name.
/// </summary>
public sealed record TicketChange(
    Guid EventId, Guid TicketId, string TicketNumber, Guid ProductId, string EventType, Guid? ActorAgentId, DateTimeOffset OccurredAt, string Kind)
{
    /// <summary>"Something may have changed, reload everything": sent when the listener had to reconnect, because notifications sent meanwhile are lost.</summary>
    public static TicketChange Resync(Guid eventId, DateTimeOffset occurredAt) =>
        new(eventId, Guid.Empty, string.Empty, Guid.Empty, string.Empty, null, occurredAt, TicketChangeKinds.Resync);

    public static TicketChange From(TicketChangedDto dto) =>
        new(dto.EventId, dto.TicketId, dto.TicketNumber, dto.ProductId, dto.EventType, dto.ActorAgentId, dto.OccurredAt, dto.Kind);

    public TicketChangedDto ToDto() =>
        new(EventId, TicketId, TicketNumber, ProductId, EventType, ActorAgentId, OccurredAt, Kind);
}
```

`src/TechStrap.Application/Live/ITicketChangeBroadcaster.cs` (new)

```csharp
namespace TechStrap.Application.Live;

/// <summary>
/// The one fan-in for live updates (D-018, D-046). The Api's implementation pushes to the SignalR hub, the Worker's sends a Postgres NOTIFY, and
/// <c>NullTicketChangeBroadcaster</c> does nothing for hosts that have neither. Delivery is best effort and at most once: correctness never depends
/// on it, because pages always load full state through REST.
/// </summary>
public interface ITicketChangeBroadcaster
{
    /// <summary>Tells every agent's queue that a ticket changed. It carries the change's ids and names only.</summary>
    Task PublishAsync(TicketChange change, CancellationToken cancellationToken);

    /// <summary>
    /// Tells the agents who have the ticket open who is there now. Presence lives in the Api process only, so the Worker's implementation ignores it.
    /// </summary>
    Task PublishPresenceAsync(TicketPresence presence, CancellationToken cancellationToken);
}
```

`src/TechStrap.Application/Live/ITicketPresenceStore.cs` (new): the model records, the enum (Application may have enums; Contracts may not) and the store interface.

```csharp
namespace TechStrap.Application.Live;

/// <summary>What an agent is doing on a ticket page.</summary>
public enum TicketViewerState
{
    Viewing,
    Composing,
}

/// <summary>An agent who has a ticket open. One entry per agent however many connections they have; Composing wins over Viewing.</summary>
public sealed record TicketViewer(Guid AgentId, string DisplayName, TicketViewerState State);

/// <summary>Who has a ticket open, ordered by name then id so equal states compare equal.</summary>
public sealed record TicketPresence(Guid TicketId, IReadOnlyList<TicketViewer> Viewers);

/// <summary>The presence of a ticket after an update and whether the update changed what other agents would see.</summary>
public sealed record PresenceChange(bool Changed, TicketPresence Presence);

/// <summary>
/// Who has which ticket open (single Api instance, D-007). Entries belong to a hub connection; a composing hint expires after
/// <c>TicketLiveLimits.ComposingTtlSeconds</c> unless refreshed. Every method is safe to call concurrently.
/// </summary>
public interface ITicketPresenceStore
{
    PresenceChange Join(string connectionId, Guid ticketId, Guid agentId, string displayName);

    /// <summary>Null when the connection has not joined the ticket.</summary>
    PresenceChange? SetComposing(string connectionId, Guid ticketId, bool isComposing);

    PresenceChange Leave(string connectionId, Guid ticketId);

    /// <summary>Removes the connection from every ticket (it disconnected) and returns one entry per ticket it was on.</summary>
    IReadOnlyList<PresenceChange> LeaveAll(string connectionId);

    TicketPresence Get(Guid ticketId);
}
```

`src/TechStrap.Application/Live/TicketPresenceMapping.cs` (new)

```csharp
using TechStrap.Contracts.Live;

namespace TechStrap.Application.Live;

/// <summary>Maps presence to its wire shape.</summary>
public static class TicketPresenceMapping
{
    public static TicketPresenceDto ToDto(this TicketPresence presence) =>
        new(presence.TicketId, [.. presence.Viewers.Select(viewer => new TicketViewerDto(viewer.AgentId, viewer.DisplayName, viewer.State.ToWire()))]);

    public static string ToWire(this TicketViewerState state) =>
        state == TicketViewerState.Composing ? TicketPresenceStates.Composing : TicketPresenceStates.Viewing;
}
```

`src/TechStrap.Application/Live/LiveErrors.cs` (new): the fixed texts an agent may see.

```csharp
using SyntaxCircus.Common;

namespace TechStrap.Application.Live;

/// <summary>Live-update outcomes. The hub shows an agent only these fixed messages; the listener logs the code only.</summary>
internal static class LiveErrors
{
    public static class Codes
    {
        public const string PresenceInvalid = "presence-invalid";
        public const string PresenceNotJoined = "presence-not-joined";
        public const string TicketNotFound = "ticket-not-found";
        public const string ChangeInvalid = "ticket-change-invalid";
        public const string ChangeTooLarge = "ticket-change-too-large";
        public const string RelayFailed = "ticket-change-relay-failed";
    }

    public static ResultError PresenceInvalid() =>
        new(Codes.PresenceInvalid, "That presence update is not valid.", ResultErrorKind.Validation);

    public static ResultError PresenceNotJoined() =>
        new(Codes.PresenceNotJoined, "Open the ticket first.", ResultErrorKind.Validation);

    public static ResultError TicketNotFound() =>
        new(Codes.TicketNotFound, "That ticket does not exist.", ResultErrorKind.NotFound);

    public static ResultError ChangeInvalid() =>
        new(Codes.ChangeInvalid, "That ticket change is not valid.", ResultErrorKind.Validation);

    public static ResultError ChangeTooLarge() =>
        new(Codes.ChangeTooLarge, "That ticket change is too large.", ResultErrorKind.Validation);

    public static ResultError RelayFailed() =>
        new(Codes.RelayFailed, "The ticket change could not be passed on.", ResultErrorKind.Failure);
}
```

`src/TechStrap.Application/Live/RelayTicketChangeHandler.cs` (new): the size cap is checked in bytes before anything is parsed, and a forwarding failure is a Result.

```csharp
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Application.Live;

public interface IRelayTicketChangeHandler
{
    Task<Result> HandleAsync(RelayTicketChangeRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The Api's end of the Postgres relay (D-018): validates a payload that arrived on the NOTIFY channel and forwards it to the broadcaster. Anything can
/// write to a channel, so the payload is untrusted: too big, not JSON, or not a valid change is a failure Result, never an exception, and is never forwarded.
/// A forwarding failure is a Result too, so one bad moment never ends the listener's loop.
/// </summary>
public sealed class RelayTicketChangeHandler(ITicketChangeBroadcaster broadcaster, ILogger<RelayTicketChangeHandler> logger) : IRelayTicketChangeHandler
{
    private const int TicketNumberMaxLength = 64;

    private static readonly HashSet<string> Kinds = [TicketChangeKinds.Created, TicketChangeKinds.Updated, TicketChangeKinds.Resync];

    private static readonly HashSet<string> EventTypes = new(
        typeof(TicketEventTypes).GetFields().Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!), StringComparer.Ordinal);

    public async Task<Result> HandleAsync(RelayTicketChangeRequest request, CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (payload is null)
        {
            return Result.Failure(LiveErrors.ChangeInvalid());
        }

        // Count bytes, not characters, and do it before parsing: the cap is what keeps a stray writer from making the API parse a large value.
        if (Encoding.UTF8.GetByteCount(payload) > TicketLiveLimits.MaxChangePayloadBytes)
        {
            return Result.Failure(LiveErrors.ChangeTooLarge());
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return Result.Failure(LiveErrors.ChangeInvalid());
        }

        var change = Parse(payload);
        if (change is null)
        {
            return Result.Failure(LiveErrors.ChangeInvalid());
        }

        try
        {
            await broadcaster.PublishAsync(change, cancellationToken);
            return Result.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Type name only: an exception message may carry data (log redaction rule).
            logger.LogWarning("Relaying a ticket change failed ({ExceptionType}).", exception.GetType().Name);
            return Result.Failure(LiveErrors.RelayFailed());
        }
    }

    private static TicketChange? Parse(string payload)
    {
        TicketChangedDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<TicketChangedDto>(payload, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }

        if (dto is null || dto.Kind is null || !Kinds.Contains(dto.Kind))
        {
            return null;
        }

        // A resync names no ticket, so its ids are not checked; anything else must be a complete change.
        if (dto.Kind == TicketChangeKinds.Resync)
        {
            return dto.EventId == Guid.Empty ? null : TicketChange.Resync(dto.EventId, dto.OccurredAt);
        }

        var complete = dto.EventId != Guid.Empty
            && dto.TicketId != Guid.Empty
            && dto.ProductId != Guid.Empty
            && !string.IsNullOrWhiteSpace(dto.TicketNumber)
            && dto.TicketNumber.Length <= TicketNumberMaxLength
            && dto.EventType is not null && EventTypes.Contains(dto.EventType)
            && dto.OccurredAt != default;
        return complete ? TicketChange.From(dto) : null;
    }
}
```

`src/TechStrap.Application/Live/UpdateTicketPresenceHandler.cs` (new)

```csharp
using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Live;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Live;

public interface IUpdateTicketPresenceHandler
{
    /// <returns>The ticket's presence after the action; for <c>LeaveAll</c>, an empty presence for no ticket.</returns>
    Task<Result<TicketPresence>> HandleAsync(UpdateTicketPresenceRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The hub's presence use case: Join, Leave, LeaveAll and SetComposing (one handler, one action discriminator). The caller's identity arrives in the
/// request, filled from the hub's own context, because <c>ICurrentAgentClaims</c> and <c>IHttpContextAccessor</c> are not reliable inside a hub
/// (D-046). Join and SetComposing re-check that the agent is still active; Leave and LeaveAll never do, so anyone can always clean up. Presence is
/// pushed to the ticket's group only when what other agents would see really changed, and a broadcast failure never fails the call.
/// </summary>
public sealed class UpdateTicketPresenceHandler(
    IAgentRepository agents,
    ITicketRepository tickets,
    ITicketPresenceStore presence,
    ITicketChangeBroadcaster broadcaster,
    ILogger<UpdateTicketPresenceHandler> logger) : IUpdateTicketPresenceHandler
{
    public async Task<Result<TicketPresence>> HandleAsync(UpdateTicketPresenceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionId) || request.Action is null)
        {
            return Result<TicketPresence>.Failure(LiveErrors.PresenceInvalid());
        }

        return request.Action switch
        {
            TicketPresenceActions.LeaveAll => await LeaveAllAsync(request, cancellationToken),
            TicketPresenceActions.Leave when request.TicketId is { } ticketId => await LeaveAsync(request, ticketId, cancellationToken),
            TicketPresenceActions.Join when request.TicketId is { } ticketId => await JoinAsync(request, ticketId, cancellationToken),
            TicketPresenceActions.SetComposing when request.TicketId is { } ticketId => await SetComposingAsync(request, ticketId, cancellationToken),
            _ => Result<TicketPresence>.Failure(LiveErrors.PresenceInvalid()),
        };
    }

    private async Task<Result<TicketPresence>> JoinAsync(UpdateTicketPresenceRequest request, Guid ticketId, CancellationToken cancellationToken)
    {
        var agent = await RequireActiveAgentAsync(request.AgentSubject, cancellationToken);
        if (agent.IsFailure)
        {
            return Result<TicketPresence>.Failure(agent.Errors[0]);
        }

        // The existence check is what stops a client from probing group names with arbitrary ids.
        if (await tickets.GetStateAsync(ticketId, cancellationToken) is null)
        {
            return Result<TicketPresence>.Failure(LiveErrors.TicketNotFound());
        }

        var name = agent.Value.Name ?? agent.Value.Email;
        var change = presence.Join(request.ConnectionId, ticketId, agent.Value.Id, name);
        await BroadcastAsync(change, cancellationToken);
        return Result<TicketPresence>.Success(change.Presence);
    }

    private async Task<Result<TicketPresence>> SetComposingAsync(UpdateTicketPresenceRequest request, Guid ticketId, CancellationToken cancellationToken)
    {
        var agent = await RequireActiveAgentAsync(request.AgentSubject, cancellationToken);
        if (agent.IsFailure)
        {
            return Result<TicketPresence>.Failure(agent.Errors[0]);
        }

        if (presence.SetComposing(request.ConnectionId, ticketId, request.IsComposing) is not { } change)
        {
            return Result<TicketPresence>.Failure(LiveErrors.PresenceNotJoined());
        }

        await BroadcastAsync(change, cancellationToken);
        return Result<TicketPresence>.Success(change.Presence);
    }

    private async Task<Result<TicketPresence>> LeaveAsync(UpdateTicketPresenceRequest request, Guid ticketId, CancellationToken cancellationToken)
    {
        var change = presence.Leave(request.ConnectionId, ticketId);
        await BroadcastAsync(change, cancellationToken);
        return Result<TicketPresence>.Success(change.Presence);
    }

    private async Task<Result<TicketPresence>> LeaveAllAsync(UpdateTicketPresenceRequest request, CancellationToken cancellationToken)
    {
        foreach (var change in presence.LeaveAll(request.ConnectionId))
        {
            await BroadcastAsync(change, cancellationToken);
        }

        return Result<TicketPresence>.Success(new TicketPresence(Guid.Empty, []));
    }

    private async Task<Result<Agent>> RequireActiveAgentAsync(string subject, CancellationToken cancellationToken)
    {
        var agent = string.IsNullOrWhiteSpace(subject) ? null : await agents.GetBySubjectAsync(subject, cancellationToken);
        if (agent is null)
        {
            return Result<Agent>.Failure(AgentErrors.NotProvisioned());
        }

        return agent.IsActive ? Result<Agent>.Success(agent) : Result<Agent>.Failure(AgentErrors.Inactive());
    }

    private async Task BroadcastAsync(PresenceChange change, CancellationToken cancellationToken)
    {
        if (!change.Changed)
        {
            return;
        }

        try
        {
            await broadcaster.PublishPresenceAsync(change.Presence, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Presence is a hint: a failed push is logged by type name only and never fails the agent's call.
            logger.LogWarning("Broadcasting presence failed ({ExceptionType}).", exception.GetType().Name);
        }
    }
}
```

- [ ] **Step 6: Run the tests and the architecture rules to verify they pass**

```bash
dotnet test --project tests/TechStrap.Application.Tests -c Release --filter-query "/*/TechStrap.Application.Tests.Live/*/*"
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: PASS (the second run proves the new handlers, abstractions and Contracts types satisfy `HandlerRules`, `AbstractionRules` and `ContractNamingRules`):

```text
  total: 63
  succeeded: 63
```
```text
  total: 295
  succeeded: 295
```

- [ ] **Step 7: Prove each pin with a recorded mutation**

Save `$T\specs\m1c.py`:

```python
"""Mutations of Task 1 (contracts and the two handlers). Run from the repository root."""
APP = ["dotnet", "test", "--project", "tests/TechStrap.Application.Tests", "-c", "Release", "--filter-query", "/*/TechStrap.Application.Tests.Live/*/*"]
RELAY = "src/TechStrap.Application/Live/RelayTicketChangeHandler.cs"
PRESENCE = "src/TechStrap.Application/Live/UpdateTicketPresenceHandler.cs"

MUTATIONS = [
    ("1 the size cap counts characters, not bytes", RELAY, [("Encoding.UTF8.GetByteCount(payload) > TicketLiveLimits.MaxChangePayloadBytes", "payload.Length > TicketLiveLimits.MaxChangePayloadBytes")], APP),
    ("2 a payload exactly at the cap is refused", RELAY, [("> TicketLiveLimits.MaxChangePayloadBytes", ">= TicketLiveLimits.MaxChangePayloadBytes")], APP),
    ("3 a resync is validated like a change", RELAY, [("if (dto.Kind == TicketChangeKinds.Resync)", 'if (dto.Kind == "Never")')], APP),
    ("4 any kind is accepted", RELAY, [("dto is null || dto.Kind is null || !Kinds.Contains(dto.Kind)", "dto is null || dto.Kind is null")], APP),
    ("5 the event type is not checked", RELAY, [("&& dto.EventType is not null && EventTypes.Contains(dto.EventType)", "&& dto.EventType is not null")], APP),
    ("6 a cancelled relay is swallowed", RELAY, [("catch (Exception exception) when (exception is not OperationCanceledException)", "catch (Exception exception)")], APP),
    ("7 the relay drops the cancellation token", RELAY, [("await broadcaster.PublishAsync(change, cancellationToken);", "await broadcaster.PublishAsync(change, CancellationToken.None);")], APP),
    ("8 a join does not check that the ticket exists", PRESENCE, [("if (await tickets.GetStateAsync(ticketId, cancellationToken) is null)", "if (await tickets.GetStateAsync(ticketId, cancellationToken) is null && ticketId == Guid.Empty)")], APP),
    ("9 presence is broadcast even when nothing changed", PRESENCE, [("        if (!change.Changed)\n        {\n            return;", "        if (change.Presence is null)\n        {\n            return;")], APP),
    ("10 the email is shown before the name", PRESENCE, [("agent.Value.Name ?? agent.Value.Email", "agent.Value.Email")], APP),
    ("11 composing does not re-check the agent", PRESENCE,
     [("        var agent = await RequireActiveAgentAsync(request.AgentSubject, cancellationToken);\n        if (agent.IsFailure)\n        {\n            return Result<TicketPresence>.Failure(agent.Errors[0]);\n        }\n\n        if (presence.SetComposing", "        if (presence.SetComposing")], APP),
    ("12 a failing broadcast escapes the handler", PRESENCE, [("catch (Exception exception) when (exception is not OperationCanceledException)", "catch (InvalidCastException exception)")], APP),
    ("13 leaving is handled as joining", PRESENCE, [("TicketPresenceActions.Leave when request.TicketId is { } ticketId => await LeaveAsync(", "TicketPresenceActions.Leave when request.TicketId is { } ticketId => await JoinAsync(")], APP),
    ("14 the composing lease constant changes", "src/TechStrap.Contracts/Live/LiveNames.cs", [("ComposingTtlSeconds = 10", "ComposingTtlSeconds = 11")], APP),
    ("15 the ticket group name format changes", "src/TechStrap.Contracts/Live/LiveNames.cs", [('TicketPrefix = "ticket:"', 'TicketPrefix = "ticket-"')], APP),
    ("16 a change loses its actor on the wire", "src/TechStrap.Application/Live/TicketChange.cs", [("new(EventId, TicketId, TicketNumber, ProductId, EventType, ActorAgentId, OccurredAt, Kind);", "new(EventId, TicketId, TicketNumber, ProductId, EventType, null, OccurredAt, Kind);")], APP),
    ("17 the viewer state names are swapped", "src/TechStrap.Application/Live/TicketPresenceMapping.cs", [("state == TicketViewerState.Composing ? TicketPresenceStates.Composing : TicketPresenceStates.Viewing", "state == TicketViewerState.Composing ? TicketPresenceStates.Viewing : TicketPresenceStates.Composing")], APP),
]
```

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | the size cap counts characters, not bytes | `RelayTicketChangeHandler.cs` | KILLED (1 failing) |
| 2 | a payload exactly at the cap is refused | `RelayTicketChangeHandler.cs` | KILLED (1 failing) |
| 3 | a resync is validated like a change | `RelayTicketChangeHandler.cs` | KILLED (1 failing) |
| 4 | any kind is accepted | `RelayTicketChangeHandler.cs` | KILLED (1 failing) |
| 5 | the event type is not checked | `RelayTicketChangeHandler.cs` | KILLED (1 failing) |
| 6 | a cancelled relay is swallowed | `RelayTicketChangeHandler.cs` | KILLED (1 failing) |
| 7 | the relay drops the cancellation token | `RelayTicketChangeHandler.cs` | KILLED (1 failing) |
| 8 | a join does not check that the ticket exists | `UpdateTicketPresenceHandler.cs` | KILLED (1 failing) |
| 9 | presence is broadcast even when nothing changed | `UpdateTicketPresenceHandler.cs` | KILLED (4 failing) |
| 10 | the email is shown before the name | `UpdateTicketPresenceHandler.cs` | KILLED (4 failing) |
| 11 | composing does not re-check the agent | `UpdateTicketPresenceHandler.cs` | KILLED (1 failing) |
| 12 | a failing broadcast escapes the handler | `UpdateTicketPresenceHandler.cs` | KILLED (2 failing) |
| 13 | leaving is handled as joining | `UpdateTicketPresenceHandler.cs` | KILLED (2 failing) |
| 14 | the composing lease constant changes | `LiveNames.cs` | KILLED (1 failing) |
| 15 | the ticket group name format changes | `LiveNames.cs` | KILLED (1 failing) |
| 16 | a change loses its actor on the wire | `TicketChange.cs` | KILLED (1 failing) |
| 17 | the viewer state names are swapped | `TicketPresenceMapping.cs` | KILLED (1 failing) |

Run them (two foreground batches, `1`..`9` and `10`..`17`, about 15 seconds each) and record the results. Every mutation is killed; the handler mutations 1 to 7 and 9 to 13 are each caught by exactly the test written for that rule.

- [ ] **Step 8: Write the failing Pester pins**

D-046 does not exist yet, three pins say a merged phase is "pending merge", and three D-045 addenda assert that there is no D-046. Save `$T\pester_t1.py`, which edits `scripts/tests/RepositoryDocs.Tests.ps1`: it changes the 08 and 09 pins to "merged", removes the three "no D-046" lines (each addendum's section match already pins that it sits inside D-045) and appends a `Describe 'D-046 (live updates)'` with four pins.

```python
"""Task 1: the Pester pins for D-046, and the three existing pins that said "pending merge" for phases that are merged. Run from the repository root."""
import sys

PATH = "scripts/tests/RepositoryDocs.Tests.ps1"
text = open(PATH, encoding="utf-8", newline="").read()
crlf = "\r\n" in text
text = text.replace("\r\n", "\n")


def swap(old, new):
    global text
    if text.count(old) != 1:
        sys.exit(f"ABORT: {text.count(old)} occurrences of {old[:70]!r}")
    text = text.replace(old, new)


swap("and the roadmap row says the phase is complete, pending merge'", "and the roadmap row says the phase is merged'")
swap(r"Should -Match '(?m)^\| 08 \|.*D-044.*\| PHASE-08 complete \(pending merge\)'", r"Should -Match '(?m)^\| 08 \|.*D-044.*\| PHASE-08 merged \(PR #13\)'")
swap(r"Should -Match '(?m)^\| 08 \|.*\| PHASE-08 complete \(pending merge\)'", r"Should -Match '(?m)^\| 08 \|.*\| PHASE-08 merged \(PR #13\)'")
swap("and the roadmap and discovery rows say PHASE-09 is complete, pending merge'", "and the roadmap and discovery rows say PHASE-09 is complete and all four pull requests merged'")
swap(r"09c merged \(PR #16\); PHASE-09 complete \(pending merge\)'" + "\n        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md')",
     r"09c merged \(PR #16\); 09d merged \(PR #17\); PHASE-09 complete:'" + "\n        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md')")
swap(r"09c merged \(PR #16\); PHASE-09 complete \(pending merge\)'" + "\n    }", r"09c merged \(PR #16\); 09d merged \(PR #17\); PHASE-09 complete:'" + "\n    }")

# D-046 now exists, so the three addenda can no longer say "there is no D-046"; the section match above each of those lines already pins that the addendum sits inside D-045.
absence = "        $script:Log | Should -Not -Match '(?m)^## D-046'\n"
if text.count(absence) != 3:
    sys.exit(f"ABORT: {text.count(absence)} occurrences of the D-046 absence pin")
text = text.replace(absence, "")

text = text.rstrip("\n") + """

Describe 'D-046 (live updates)' {
    BeforeAll { $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md' }

    It 'is in the decision log with its date, its status, a header bullet and an index row' {
        $script:Log | Should -Match '(?m)^## D-046: PHASE-10: live updates'
        $script:Log | Should -Match '(?s)## D-046:.*?- \*\*Status:\*\* Approved \(owner 2026-10-07.*?- \*\*Date:\*\* 2026-10-07'
        $script:Log | Should -Match '(?m)^\| D-046 \|.*\| 2026-10-07 \|'
        $script:Log | Should -Match '(?m)^- \*\*Owner decision \(2026-10-07, PHASE-10 planning\):\*\* D-046'
    }

    It 'records the owner decisions and the technical rulings the two pull requests rely on' {
        foreach ($phrase in 'Two pull requests', 'New activity - refresh', 'Agent.Name', 'public token-provider API', 'UpdateTicketPresenceRequest', 'TicketChangeCaptureInterceptor', 'TicketChangePublishingInterceptor',
                'HubException("Ticket not found")', 'An `access_token` query value is not read', 'CloseOnAuthenticationExpiration', 'techstrap_ticket_changes', 'Resync', 'techstrap.live.connected_agents', 'PgBouncer', 'No new setting, no migration') {
            $script:Log | Should -Match ([regex]::Escape($phrase)) -Because "D-046 must mention $phrase"
        }
    }

    It 'corrects the PHASE-10 spec and the handler table' {
        $spec = Get-RepoText 'docs/architecture/PHASE-10-live-updates.md'
        $spec | Should -Match '(?m)^### Corrections \(D-046, 2026-10-07\)'
        foreach ($phrase in 'ICurrentUserService', 'LiveConnectionState', 'EventId', 'access_token', 'UpdateTicketPresenceRequest') {
            $spec | Should -Match ([regex]::Escape($phrase))
        }
        $architecture = Get-RepoText 'docs/architecture/02-ARCHITECTURE.md'
        $architecture | Should -Not -Match 'unauthorized joins abort the connection'
        $architecture | Should -Match 'TicketChangeCaptureInterceptor'
    }

    It 'no longer says a merged phase is pending merge in the roadmap and discovery rows' {
        foreach ($file in 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md', 'docs/architecture/00-DISCOVERY-INDEX.md') {
            $text = Get-RepoText $file
            $text | Should -Match '(?m)^\| 07 \|.*\| Complete: 07a merged \(PR #9\), 07b merged \(PR #10\), 07c merged \(PR #11\)'
            $text | Should -Not -Match '(?m)^\| 0[789] \|.*pending merge'
        }
    }
}
"""
open(PATH, "w", encoding="utf-8", newline="").write(text.replace("\n", "\r\n") if crlf else text)
```

Run it from the repository root, then the script test:

```bash
python $T/pester_t1.py
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected: FAIL (the pins that name the merged phases, and the four D-046 pins):

```text
[-] D-044 (the knowledge base).ticks every PHASE-08 task P08-T01 to P08-T20 and the Admin deliverable, and the roadmap row says the phase is merged 51ms
[-] D-045 (the public portal).ticks only the tasks and deliverables 09a to 09d fully deliver, and the roadmap and discovery rows say PHASE-09 is complete and all four pull requests merged 47ms
[-] D-046 (live updates).is in the decision log with its date, its status, a header bullet and an index row 10ms
[-] D-046 (live updates).records the owner decisions and the technical rulings the two pull requests rely on 10ms
[-] D-046 (live updates).corrects the PHASE-10 spec and the handler table 14ms
[-] D-046 (live updates).no longer says a merged phase is pending merge in the roadmap and discovery rows 5ms
Tests Passed: 46, Failed: 6, Skipped: 0, Inconclusive: 0, NotRun: 0
```

- [ ] **Step 9: Write the documentation**

Save `$T\docs_10a.py` (it holds the edits of Task 1 and of Task 6; run it with `1` now and with `6` in Task 6). Each edit replaces text that must occur exactly once, so it stops with a message instead of writing twice.

```python
"""Applies the PHASE-10a documentation edits (Task 1: D-046, the spec corrections, the stale roadmap rows; Task 6: the close-out). Run from the repository root.

Every edit replaces text that must occur exactly once, so a second run, or a file that moved on, stops with a message instead of writing twice.
"""
import re
import sys


def edit(path, pairs):
    with open(path, encoding="utf-8", newline="") as handle:
        text = handle.read()
    crlf = "\r\n" in text
    text = text.replace("\r\n", "\n")
    for old, new in pairs:
        if text.count(old) != 1:
            sys.exit(f"ABORT: {text.count(old)} occurrences of {old[:70]!r} in {path}")
        text = text.replace(old, new)
    if crlf:
        text = text.replace("\n", "\r\n")
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(text)


PHASE = sys.argv[1] if len(sys.argv) > 1 else "all"
LOG = "docs/architecture/04-DECISION-LOG.md"
SPEC = "docs/architecture/PHASE-10-live-updates.md"
ARCH = "docs/architecture/02-ARCHITECTURE.md"
ROADMAP = "docs/architecture/99-IMPLEMENTATION-ROADMAP.md"
INDEX = "docs/architecture/00-DISCOVERY-INDEX.md"

D046 = """
## D-046: PHASE-10: live updates (two pull requests, identity in the request, a two-part post-commit hook, header-only hub token, presence names, the detail banner)

- **Status:** Approved (owner 2026-10-07; technical rulings at PHASE-10a plan review)
- **Date:** 2026-10-07
- **Owner:** Jon Seeley
- **Related artifacts:** D-007, D-016, D-018, D-040, D-043, D-045, `docs/architecture/PHASE-10-live-updates.md`, `docs/superpowers/plans/2026-10-07-phase-10a-live-server.md`

### Context
PHASE-09 is merged, so PHASE-10 can start. Reading the code before planning found these gaps between the spec and what exists:
- **`ICurrentUserService` does not exist.** The Application abstraction is `ICurrentAgentClaims`, backed by `IHttpContextAccessor`, which is not reliable inside a hub.
- **`LiveConnectionState` cannot live in Contracts.** `ContractNamingRules` allows only `*Dto`, `*Request`, `*Response` and static constant classes there, and Contracts carries no enums.
- **A SaveChanges interceptor alone is not post-commit.** `UnitOfWork` commits the transaction after SaveChanges, so SaveChanges' own "saved" event fires before the commit.
- **`DrainEmailOutboxHandler` writes no `TicketEvent`.** Only `AutoCloseSolvedTicketsHandler` publishes from the Worker.
- **Browsers never reach `/hubs`.** The Admin connects server to server (`API__BASEURL: http://api/`), so the reverse proxy needs nothing new for the hub.
- **The hub token needs no query string.** The .NET client sends `Authorization: Bearer` on every transport; the query form is a browser habit and a leak surface.
- **`TicketChangedDto` has no event id** for the client's de-duplication.
- **No custom meter exists**, and `AddSyntaxCircusObservability` exports a custom meter only when its name is passed to it.
- **The spec and 02-ARCHITECTURE disagree on a join for an unknown ticket** (`HubException` or an aborted connection).
- **Presence names and the detail page's behaviour when another agent changes the ticket** were open.

### Decision
**Owner decisions (2026-10-07)**
- **Delivery.** Two pull requests: **10a**, the server (T01 to T10), and **10b**, the Admin (T11 to T17), which starts only after a package prerequisite.
- **Detail page on another agent's change (10b).** A "New activity - refresh" banner. When the agent clicks it the page reloads the timeline and status and takes the new row version. The draft is never touched. Until the click a send still gets the existing 409, so an agent never acts on an unseen version.
- **Presence name (10b).** The agent's own `Agent.Name` (the internal name; the email when there is none), shown to other agents only. The customer-facing `PublicDisplayName` is never used.
- **Hub token source (10b).** `SyntaxCircus.Blazor.Auth` (sibling repository) gains a public token-provider API and is published as a new package version before 10b starts.

**Technical rulings (proposed in the 10a plan; approved when the owner approves it)**
- **Identity travels in the request.** The hub builds an `UpdateTicketPresenceRequest` (action, agent subject, connection id, ticket id, composing flag) from `Context.User` and `Context.ConnectionId`; handlers never read claims or the HTTP context. The Agent policy is on the route and on the hub class.
- **The post-commit hook has two parts.** `TicketChangeCaptureInterceptor` (a `SaveChangesInterceptor`) notes the `TicketEvent` rows being inserted and stages them per context; `TicketChangePublishingInterceptor` (a `DbTransactionInterceptor`) publishes after `TransactionCommitted` and drops the staging on a rollback or failure. A save with no explicit transaction publishes from its own "saved" event. One change per ticket per commit; a deleted ticket, or an event for a ticket the context does not track, becomes one `Resync`. A broadcaster failure or a hang (5 seconds) is logged by exception type and never fails the request. `TechStrapDatabase.Configure` keeps its two-argument shape; the interceptors are added where the container is, in `AddTechStrapPersistence`, and `NullTicketChangeBroadcaster` is the default until the Api or the Worker replaces it.
- **Header token only.** An `access_token` query value is not read on `/hubs` (the JWT bearer default); tests pin the refusal and that no token reaches a log at any level. `CloseOnAuthenticationExpiration` closes a connection when its token expires, and a deactivated agent is refused at the handshake by the Agent policy.
- **Groups.** `TicketChanged` goes to group `queue` only and clients filter by ticket id; presence goes to `ticket:{id}`. `JoinTicket` checks the ticket exists through `ITicketRepository.GetStateAsync` and answers an unknown id with `HubException("Ticket not found")` before any group is joined. `JoinTicket` returns the current presence to its caller, so a second tab of the same agent (for whom nothing changed) still learns the state.
- **Presence.** In memory, one lock, one entry per agent however many tabs; Composing wins. A composing lease lasts 10 seconds. "Changed" means what other agents would see differs or a lease was extended: a refresh is the heartbeat the client's clear-after-TTL depends on, so it is sent; a repeat that moves nothing is not.
- **The relay.** The Worker's `PgNotifyTicketChangeBroadcaster` sends `pg_notify('techstrap_ticket_changes', json)` after the commit; the Api's `TicketChangeListener` holds one dedicated, unpooled, named connection with a keepalive, hands each payload to `RelayTicketChangeHandler` (validate, 2,048-byte cap, forward; bad input is a failure Result, never an exception), reconnects with a 1, 2, 4, up to 30 second backoff and relays one `Resync` after each reconnect. The listener never notifies, so nothing echoes.
- **Metrics.** `TechStrapMetrics` (meter `TechStrap`): `techstrap.live.connected_agents` (distinct agents), `techstrap.live.changes_relayed`, `techstrap.live.relay_failures`, `techstrap.live.listener_reconnects`. The Api and the Worker pass the meter name to `AddSyntaxCircusObservability`. Handlers cannot take a meter, so the hub and the listener record.
- **No new setting, no migration.** Every number is a constant. `Microsoft.AspNetCore.SignalR.Client` is added to `TechStrap.Api.Tests` only (already pinned); `Npgsql` becomes a direct reference of Infrastructure.

### Alternatives Considered
- **Accept `access_token` on `/hubs`.** Rejected: the header works on every transport the Admin uses (proved over real WebSockets), and a query token would reach logs, spans and proxies.
- **Publish from SaveChanges' "saved" event.** Rejected: it fires before the commit, so a rolled-back conflict could announce a change that never happened.
- **A `Removed` change kind for a deleted ticket.** Rejected for now: a `Resync` already makes every queue reload, and delete is an Admin-only rare operation.
- **Server-side expiry of a composing hint.** Rejected: it needs a timer and a handler action; the lease plus the heartbeat gives the same result.
- **Reading the identity through `IHttpContextAccessor` in the hub.** Rejected: it is not reliable there, and the request record keeps handlers transport-free.

### Consequences
- **Spec corrections.** `PHASE-10-live-updates.md` and `02-ARCHITECTURE.md` carry the corrections above; `LiveConnectionState` is an Admin type (10b).
- **A deleted ticket publishes a `Resync`** (queues reload; an open detail page shows "gone" on its next load).
- **Single Api instance.** Presence and the in-process publish do not span instances (D-007). The listener needs a direct Postgres connection: PgBouncer in transaction mode breaks `LISTEN` (DEPLOYMENT.md).
- **As built in 10a: the proof.** The hub is exercised through a real `HubConnection` on the in-memory server (long polling) and, for the header token, over real WebSockets on a loopback Kestrel port; the hook, the broadcaster and the listener run against Postgres (Testcontainers), including `pg_terminate_backend` of the listener's backend, which ends in a reconnect and one `Resync`.
- **As built in 10a: the Admin is not changed.** 10b adds the client, the indicator, the banners and the presence bar; the hub contract (Contracts `Live`) is what it builds on.
- **Known: the first 10a host test of a meter needs an async disposal.** A meter provider subscribes to every meter of that name in the process, so a host that is only disposed synchronously keeps observing the next one's instruments; the registration test disposes each host before it starts the next.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-10 planning)
- **Approved on:** 2026-10-07
"""

CORRECTIONS = """### Corrections (D-046, 2026-10-07)

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

"""


def task1():
    edit(LOG, [
        ("- **Owner decision (2026-10-02, PHASE-02):**",
         "- **Owner decision (2026-10-07, PHASE-10 planning):** D-046, the owner decisions on the two-pull-request split, the detail page's \"New activity\" banner, the presence name and the hub token source. Its technical rulings were proposed in the PHASE-10a plan and approved when the owner approved the plan.\n- **Owner decision (2026-10-02, PHASE-02):**"),
    ])
    text = open(LOG, encoding="utf-8", newline="").read()
    nl = "\r\n" if "\r\n" in text else "\n"
    text = text.replace("\r\n", "\n")
    row = [line for line in text.split("\n") if line.startswith("| D-045 |")][0]
    new_row = "| D-046 | PHASE-10: two pull requests, identity in the request record, a two-part post-commit hook, header-only hub token, presence by `Agent.Name`, the detail page's \"New activity\" banner | Approved (owner 2026-10-07; technical rulings at PHASE-10a plan review) | 2026-10-07 | PHASE-10, 02-ARCHITECTURE |"
    text = text.replace(row, row + "\n" + new_row, 1)
    text = text.rstrip("\n") + "\n" + D046
    open(LOG, "w", encoding="utf-8", newline="").write(text.replace("\n", nl))

    edit(SPEC, [("## Architecture Decisions\n", CORRECTIONS + "## Architecture Decisions\n")])
    edit(ARCH, [
        ("| SignalR `TicketHub.JoinTicket` / `LeaveTicket` / `SetComposing` (Agent JWT) | `UpdateTicketPresenceHandler` | `ITicketPresenceStore`, `ITicketRepository` (existence check), `ITicketChangeBroadcaster`, `ICurrentUserService`, `TimeProvider` |",
         "| SignalR `TicketHub.JoinTicket` / `LeaveTicket` / `SetComposing` (Agent JWT) | `UpdateTicketPresenceHandler` | `IAgentRepository`, `ITicketPresenceStore`, `ITicketRepository` (existence check), `ITicketChangeBroadcaster`; the caller's subject and connection id arrive in the `UpdateTicketPresenceRequest` the hub fills from `Context.User` (D-046) |"),
        ("Hub method returns void; failure results are logged and the hub call returns without a client error; unauthorized joins abort the connection |",
         "`JoinTicket` returns the current presence; a failed call is a `HubException` with a fixed message (`Ticket not found` for an unknown id, D-046); an unauthenticated or non-agent handshake is 401/403 |"),
        ("- API changes: the handler commits; the Infrastructure post-commit interceptor reads the `TicketEvent` rows committed in that unit of work",
         "- API changes: the handler commits; the Infrastructure post-commit hook (`TicketChangeCaptureInterceptor` stages the inserted `TicketEvent` rows, `TicketChangePublishingInterceptor` publishes after the commit, D-046) reads the `TicketEvent` rows committed in that unit of work"),
    ])
    for path in (ROADMAP, INDEX):
        edit(path, [
            ("07 complete (pending merge): 07a merged (PR #9), 07b merged (PR #10), 07c implemented; owner action 7 (Authentik) still open, so P07-T02 stays unticked",
             "Complete: 07a merged (PR #9), 07b merged (PR #10), 07c merged (PR #11); owner action 7 (Authentik) still open, so P07-T02 stays unticked"),
            ("PHASE-08 complete (pending merge): the API (Tasks 1-8) and the Admin (editor, categories, article picker) are implemented; the owner's manual checks are open",
             "PHASE-08 merged (PR #13): the API (Tasks 1-8) and the Admin (editor, categories, article picker); the owner's manual checks are open"),
            ("09c merged (PR #16); PHASE-09 complete (pending merge): 09d implemented; the owner evidence",
             "09c merged (PR #16); 09d merged (PR #17); PHASE-09 complete: the owner evidence"),
        ])


def task6():
    ticks = ["P10-T%02d" % n for n in range(1, 11)]
    pairs = [("- [ ] **%s**" % t, "- [x] **%s**" % t) for t in ticks]
    pairs += [
        ("- [ ] `TicketHub`, `UpdateTicketPresenceHandler`, `ITicketPresenceStore`, `RelayTicketChangeHandler`, `ITicketChangeBroadcaster` (+ SignalR and pg_notify implementations), `TicketChangeListener`.",
         "- [x] `TicketHub`, `UpdateTicketPresenceHandler`, `ITicketPresenceStore`, `RelayTicketChangeHandler`, `ITicketChangeBroadcaster` (+ SignalR and pg_notify implementations), `TicketChangeListener` (10a, D-046)."),
        ("- [ ] Post-commit publishing hook in Infrastructure used by API and Worker.",
         "- [x] Post-commit publishing hook in Infrastructure used by API and Worker (10a, D-046: capture plus publishing interceptors)."),
        ("- [ ] Contracts: `TicketChangedDto`, `TicketPresenceDto`, hub method/group constants, change-kind constants.",
         "- [x] Contracts: `TicketChangedDto`, `TicketPresenceDto`, hub method/group constants, change-kind constants (10a; `LiveConnectionState` is an Admin type, 10b)."),
    ]
    edit(SPEC, pairs)
    for path in (ROADMAP, INDEX):
        text = open(path, encoding="utf-8", newline="").read()
        nl = "\r\n" if "\r\n" in text else "\n"
        lines = text.replace("\r\n", "\n").split("\n")
        for index, line in enumerate(lines):
            if line.startswith("| 10 | [Live updates]"):
                assert line.endswith("| Not started |"), line  # the roadmap row and the discovery row both end this way
                status = "10a complete (pending merge): the server (hub, presence, post-commit hook, NOTIFY relay, metrics); 10b waits on the Blazor.Auth package |"
                lines[index] = line.replace("D-007, D-018 | Not started |", "D-007, D-018, D-046 | " + status).replace("| 12 | Not started |", "| 12 | " + status)
        open(path, "w", encoding="utf-8", newline="").write(nl.join(lines))

    edit("docs/architecture/03-PACKAGE-MAP.md", [
        ("| Admin server-side hub connection (D-007).", "| The hub client: `TechStrap.Api.Tests` connects a real `HubConnection` in 10a (D-046); the Admin's server-side hub connection arrives in 10b (D-007)."),
        ("Direct use for the dedicated `LISTEN` connection (API listener)", "A direct reference of `TechStrap.Infrastructure` (10a, D-046): the dedicated `LISTEN` connection (API listener)"),
    ])
    edit("docs/self-hosting/DEPLOYMENT.md", [("## Health checks and acceptance\n", """## Live updates (PHASE-10)

Agents see ticket changes and who else has a ticket open without reloading. Two things matter to the person running the stack:

- **The hub is internal.** The Admin connects to the Api's `/hubs/tickets` server to server (`API__BASEURL`, `http://api/` in the deploy compose). A browser never connects to the hub, so the reverse proxy needs no WebSocket rule or idle-timeout change for it; the Admin's own `/_blazor` circuit already needs upgrade support. Do not publish `/hubs` on the public host. The hub accepts an agent's token in the `Authorization` header only; a token in the URL is refused.
- **`LISTEN` needs a direct Postgres connection.** The Api holds one long-lived connection that listens for the changes the Worker makes (auto-close) and reconnects by itself with a backoff. Point `ConnectionStrings__TechStrap` at Postgres itself, or at a pooler in session mode; **PgBouncer in transaction mode breaks `LISTEN`**. The connection shows in `pg_stat_activity` with the application name `techstrap-ticket-change-listener`. Presence and the in-process publish live in one Api process, so run one Api instance (D-007).
- **No new settings.** Nothing in the `deploy/.env.*` files changes for live updates.

## Health checks and acceptance
""")])


if PHASE in ("1", "all"):
    task1()
if PHASE in ("6", "all"):
    task6()
```

```bash
python $T/docs_10a.py 1
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected: PASS:

```text
[+] C:\tmp\claude\D--dev-SyntaxCircus-techstrap\42551360-63f8-4931-99a2-3f692676cb7a\scratchpad\p10-plan\clone\scripts\tests\RepositoryDocs.Tests.ps1 1.6s (52 tests)
Tests completed in 1.61s
Tests Passed: 52, Failed: 0, Skipped: 0, Inconclusive: 0, NotRun: 0
```

- [ ] **Step 10: Prove the documentation pins with a recorded mutation**

Save `$T\specs\m1.py`:

```python
"""Mutations of Task 1's documentation pins. Run from the repository root."""
PESTER = ["pwsh", "-NoProfile", "-File", "scripts/Invoke-ScriptTests.ps1", "-Output", "Minimal", "-Path", "scripts/tests/RepositoryDocs.Tests.ps1"]
LOG = "docs/architecture/04-DECISION-LOG.md"
SPEC = "docs/architecture/PHASE-10-live-updates.md"

MUTATIONS = [
    ("1 the D-046 index row has another date", LOG, [("| 2026-10-07 | PHASE-10, 02-ARCHITECTURE |", "| 2026-10-08 | PHASE-10, 02-ARCHITECTURE |")], PESTER),
    ("2 the header bullet for D-046 is gone", LOG, [("- **Owner decision (2026-10-07, PHASE-10 planning):** D-046", "- **Owner decision (2026-10-07):** D-046")], PESTER),
    ("3 D-046 no longer says the token is header only", LOG, [("- **Header token only.** An `access_token` query value is not read", "- **Header token only.** A query value is not read")], PESTER),
    ("4 the spec corrections lose their heading", SPEC, [("### Corrections (D-046, 2026-10-07)", "### Corrections (D-046)")], PESTER),
    ("5 the handler table says unauthorized joins abort again", "docs/architecture/02-ARCHITECTURE.md", [("a failed call is a `HubException` with a fixed message", "unauthorized joins abort the connection; a failed call is a `HubException` with a fixed message")], PESTER),
    ("6 the roadmap says 07c was only implemented", "docs/architecture/99-IMPLEMENTATION-ROADMAP.md", [("07c merged (PR #11)", "07c implemented")], PESTER),
    ("7 the discovery index says PHASE-08 is pending merge again", "docs/architecture/00-DISCOVERY-INDEX.md", [("PHASE-08 merged (PR #13)", "PHASE-08 complete (pending merge)")], PESTER),
    ("8 the roadmap says PHASE-09 is pending merge again", "docs/architecture/99-IMPLEMENTATION-ROADMAP.md", [("09d merged (PR #17); PHASE-09 complete:", "PHASE-09 complete (pending merge):")], PESTER),
]
```

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | the D-046 index row has another date | `04-DECISION-LOG.md` | KILLED |
| 2 | the header bullet for D-046 is gone | `04-DECISION-LOG.md` | KILLED |
| 3 | D-046 no longer says the token is header only | `04-DECISION-LOG.md` | KILLED |
| 4 | the spec corrections lose their heading | `PHASE-10-live-updates.md` | KILLED |
| 5 | the handler table says unauthorized joins abort again | `02-ARCHITECTURE.md` | KILLED |
| 6 | the roadmap says 07c was only implemented | `99-IMPLEMENTATION-ROADMAP.md` | KILLED |
| 7 | the discovery index says PHASE-08 is pending merge again | `00-DISCOVERY-INDEX.md` | KILLED |
| 8 | the roadmap says PHASE-09 is pending merge again | `99-IMPLEMENTATION-ROADMAP.md` | KILLED |

Mutation 3 first survived: the pin looked for the word `access_token`, which D-046 uses in several places, so it was changed to look for the sentence ("An `access_token` query value is not read") and the mutation then died. Every mutation is killed.

- [ ] **Step 11: Run the suites and commit**

```bash
dotnet test --project tests/TechStrap.Application.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected: 822 Application tests (the 63 new ones included), the 295 architecture tests and the 52 Pester tests of the file pass.

```bash
git add -A
git diff --cached --stat
git commit -m "feat: PHASE-10a contracts, abstractions and presence and relay handlers (D-046)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

### Task 2: The presence store and the post-commit ticket change hook

**Review Focus pin:** 2 (no broadcast for rolled-back or conflicted work, exactly one per ticket per commit, a failing or hanging broadcaster never fails the request) and 4 (TTL, `LeaveAll`, broadcast only on change, concurrency safety, the store does not grow).

**Files:**

- Create: `src/TechStrap.Infrastructure/Live/InMemoryTicketPresenceStore.cs`, `NullTicketChangeBroadcaster.cs`, `PendingTicketChanges.cs`, `TicketChangePublisher.cs`, `TicketChangeCaptureInterceptor.cs`, `TicketChangePublishingInterceptor.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs`
- Test (create): `tests/TechStrap.Infrastructure.IntegrationTests/Live/InMemoryTicketPresenceStoreTests.cs`, `RecordingBroadcaster.cs`, `TicketChangePublishingTests.cs`

**Interfaces:**
- Consumes: Task 1's `ITicketPresenceStore`, `PresenceChange`, `TicketPresence`, `TicketViewer`, `ITicketChangeBroadcaster`, `TicketChange`, `TicketChangeKinds`, `TicketLiveLimits.ComposingTtlSeconds`; `TicketEventRecord` and `TicketRecord` (internal persistence records, visible to the test project), `TicketScenario` and `PersistenceTestHost` (existing test support), `PostgresFixture`.
- Produces: `InMemoryTicketPresenceStore(TimeProvider)` (internal; one lock, one entry per agent, a 10-second composing lease; `IsEmpty` for the tests); `NullTicketChangeBroadcaster`; `PendingTicketChanges` (staging per context, `Add`, `Discard`, `Take` which returns one `TicketChange` per ticket); `TicketChangePublisher` (`PublishAsync`, `Publish`, `Discard`, a 5-second limit per broadcast, never throws); the two interceptors; and `AddTechStrapPersistence` registering them with `NullTicketChangeBroadcaster` as the default (the Api and the Worker replace it with `Replace`, after this call).

- [ ] **Step 1: Write the failing store tests**

`tests/TechStrap.Infrastructure.IntegrationTests/Live/InMemoryTicketPresenceStoreTests.cs` (new): what counts as a change, the lease on a `FakeTimeProvider` (including the exact-boundary and the refresh-is-a-change cases), one entry per agent, ordering, `LeaveAll`, and 16 tasks hammering the store.

```csharp
using Microsoft.Extensions.Time.Testing;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Infrastructure.Live;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>Presence rules without a database: what counts as a change, the composing lease on a fake clock, one entry per agent, and safety under contention.</summary>
public sealed class InMemoryTicketPresenceStoreTests
{
    private static readonly Guid Ticket1 = Guid.Parse("0197f2a0-0000-7000-8000-000000000001");
    private static readonly Guid Ticket2 = Guid.Parse("0197f2a0-0000-7000-8000-000000000002");
    private static readonly Guid Sam = Guid.Parse("0197f2a0-0000-7000-8000-0000000000a1");
    private static readonly Guid Kim = Guid.Parse("0197f2a0-0000-7000-8000-0000000000a2");
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(TicketLiveLimits.ComposingTtlSeconds);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryTicketPresenceStore _store;

    public InMemoryTicketPresenceStoreTests() => _store = new InMemoryTicketPresenceStore(_clock);

    private static TicketViewer Viewer(Guid id, string name, TicketViewerState state = TicketViewerState.Viewing) => new(id, name, state);

    [Fact]
    public void An_empty_ticket_has_no_viewers()
    {
        var presence = _store.Get(Ticket1);

        presence.TicketId.ShouldBe(Ticket1);
        presence.Viewers.ShouldBeEmpty();
    }

    [Fact]
    public void Joining_changes_the_presence_and_joining_again_does_not()
    {
        var first = _store.Join("c1", Ticket1, Sam, "Sam");
        var again = _store.Join("c1", Ticket1, Sam, "Sam");

        first.Changed.ShouldBeTrue();
        first.Presence.Viewers.ShouldBe([Viewer(Sam, "Sam")]);
        again.Changed.ShouldBeFalse();
        again.Presence.Viewers.ShouldBe([Viewer(Sam, "Sam")]);
    }

    [Fact]
    public void An_agent_with_two_tabs_is_one_viewer_and_the_second_tab_changes_nothing()
    {
        _store.Join("tab-1", Ticket1, Sam, "Sam");

        var second = _store.Join("tab-2", Ticket1, Sam, "Sam");

        second.Changed.ShouldBeFalse();
        second.Presence.Viewers.Count.ShouldBe(1);
    }

    [Fact]
    public void Closing_one_of_two_tabs_changes_nothing_and_closing_the_last_one_does()
    {
        _store.Join("tab-1", Ticket1, Sam, "Sam");
        _store.Join("tab-2", Ticket1, Sam, "Sam");

        _store.Leave("tab-1", Ticket1).Changed.ShouldBeFalse();
        var last = _store.Leave("tab-2", Ticket1);

        last.Changed.ShouldBeTrue();
        last.Presence.Viewers.ShouldBeEmpty();
    }

    [Fact]
    public void Viewers_are_ordered_by_name_so_equal_states_compare_equal()
    {
        _store.Join("c2", Ticket1, Sam, "Sam");
        var kim = _store.Join("c1", Ticket1, Kim, "Kim");

        kim.Presence.Viewers.Select(viewer => viewer.DisplayName).ShouldBe(["Kim", "Sam"]);
    }

    [Fact]
    public void The_same_name_is_ordered_by_agent_id()
    {
        _store.Join("c1", Ticket1, Kim, "Alex");
        var presence = _store.Join("c2", Ticket1, Sam, "Alex");

        presence.Presence.Viewers.Select(viewer => viewer.AgentId).ShouldBe(new[] { Sam, Kim }.Order().ToList());
    }

    [Fact]
    public void Leaving_the_last_viewer_forgets_the_ticket_so_the_store_does_not_grow()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.IsEmpty.ShouldBeFalse();

        _store.Leave("c1", Ticket1);

        _store.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Leaving_a_ticket_the_connection_never_joined_changes_nothing()
    {
        var change = _store.Leave("ghost", Ticket1);

        change.Changed.ShouldBeFalse();
        change.Presence.Viewers.ShouldBeEmpty();
    }

    [Fact]
    public void Composing_needs_a_join_first()
    {
        _store.SetComposing("c1", Ticket1, true).ShouldBeNull();
    }

    [Fact]
    public void Composing_shows_as_replying_and_stopping_clears_it()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");

        var start = _store.SetComposing("c1", Ticket1, true)!;
        var stop = _store.SetComposing("c1", Ticket1, false)!;

        start.Changed.ShouldBeTrue();
        start.Presence.Viewers.ShouldBe([Viewer(Sam, "Sam", TicketViewerState.Composing)]);
        stop.Changed.ShouldBeTrue();
        stop.Presence.Viewers.ShouldBe([Viewer(Sam, "Sam")]);
    }

    [Fact]
    public void Stopping_when_not_composing_changes_nothing()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");

        _store.SetComposing("c1", Ticket1, false)!.Changed.ShouldBeFalse();
    }

    [Fact]
    public void A_composing_hint_expires_after_the_ttl_unless_refreshed()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.SetComposing("c1", Ticket1, true);

        _clock.Advance(Ttl - TimeSpan.FromSeconds(1));
        _store.Get(Ticket1).Viewers.Single().State.ShouldBe(TicketViewerState.Composing);

        _clock.Advance(TimeSpan.FromSeconds(1));
        _store.Get(Ticket1).Viewers.Single().State.ShouldBe(TicketViewerState.Viewing);
    }

    [Fact]
    public void A_refresh_extends_the_lease_and_is_a_change_because_it_is_the_heartbeat_the_clients_clear_on()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.SetComposing("c1", Ticket1, true);
        _clock.Advance(TimeSpan.FromSeconds(4));

        var refresh = _store.SetComposing("c1", Ticket1, true)!;
        _clock.Advance(TimeSpan.FromSeconds(8));

        refresh.Changed.ShouldBeTrue();
        _store.Get(Ticket1).Viewers.Single().State.ShouldBe(TicketViewerState.Composing);
    }

    [Fact]
    public void A_repeat_at_the_same_instant_changes_nothing()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.SetComposing("c1", Ticket1, true);

        _store.SetComposing("c1", Ticket1, true)!.Changed.ShouldBeFalse();
    }

    [Fact]
    public void Composing_again_after_the_lease_lapsed_is_a_change_because_peers_saw_viewing_meanwhile()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.SetComposing("c1", Ticket1, true);
        _clock.Advance(Ttl + TimeSpan.FromSeconds(1));

        _store.SetComposing("c1", Ticket1, true)!.Changed.ShouldBeTrue();
    }

    [Fact]
    public void Stopping_after_the_lease_lapsed_changes_nothing()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.SetComposing("c1", Ticket1, true);
        _clock.Advance(Ttl + TimeSpan.FromSeconds(1));

        _store.SetComposing("c1", Ticket1, false)!.Changed.ShouldBeFalse();
    }

    [Fact]
    public void One_tab_composing_makes_the_agent_replying_until_that_tab_stops()
    {
        _store.Join("tab-1", Ticket1, Sam, "Sam");
        _store.Join("tab-2", Ticket1, Sam, "Sam");
        _store.SetComposing("tab-1", Ticket1, true);

        _store.Get(Ticket1).Viewers.Single().State.ShouldBe(TicketViewerState.Composing);
        _store.SetComposing("tab-1", Ticket1, false);
        _store.Get(Ticket1).Viewers.Single().State.ShouldBe(TicketViewerState.Viewing);
    }

    [Fact]
    public void Leaving_everything_returns_one_entry_per_ticket_and_removes_only_that_connection()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.Join("c1", Ticket2, Sam, "Sam");
        _store.Join("c2", Ticket1, Kim, "Kim");

        var changes = _store.LeaveAll("c1");

        changes.Select(change => change.Presence.TicketId).ShouldBe([Ticket1, Ticket2], ignoreOrder: true);
        changes.ShouldAllBe(change => change.Changed);
        _store.Get(Ticket1).Viewers.ShouldBe([Viewer(Kim, "Kim")]);
        _store.Get(Ticket2).Viewers.ShouldBeEmpty();
        _store.LeaveAll("c2");
        _store.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Leaving_everything_for_an_unknown_connection_returns_nothing()
    {
        _store.LeaveAll("ghost").ShouldBeEmpty();
    }

    [Fact]
    public void Leaving_everything_reports_no_change_for_a_ticket_the_same_agent_still_has_open_in_another_tab()
    {
        _store.Join("tab-1", Ticket1, Sam, "Sam");
        _store.Join("tab-2", Ticket1, Sam, "Sam");

        var changes = _store.LeaveAll("tab-1");

        changes.Single().Changed.ShouldBeFalse();
        _store.Get(Ticket1).Viewers.Count.ShouldBe(1);
    }

    [Fact]
    public void A_connection_that_left_can_no_longer_compose_on_that_ticket()
    {
        _store.Join("c1", Ticket1, Sam, "Sam");
        _store.LeaveAll("c1");

        _store.SetComposing("c1", Ticket1, true).ShouldBeNull();
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrent_joins_composing_and_leaves_keep_the_store_consistent()
    {
        const int Agents = 16;
        const int Rounds = 200;
        var ids = Enumerable.Range(0, Agents).Select(_ => Guid.NewGuid()).ToArray();

        await Task.WhenAll(Enumerable.Range(0, Agents).Select(index => Task.Run(() =>
        {
            for (var round = 0; round < Rounds; round++)
            {
                var connection = $"c{index}";
                _store.Join(connection, Ticket1, ids[index], $"Agent {index:00}");
                _store.Join(connection, Ticket2, ids[index], $"Agent {index:00}");
                _store.SetComposing(connection, Ticket1, true);
                _store.Get(Ticket1);
                _store.Leave(connection, Ticket2);
                _store.SetComposing(connection, Ticket1, false);
            }
        }, TestContext.Current.CancellationToken)));

        // Every agent is still on ticket 1 (never left it) and nobody is on ticket 2 (every join was followed by a leave).
        _store.Get(Ticket1).Viewers.Count.ShouldBe(Agents);
        _store.Get(Ticket1).Viewers.ShouldAllBe(viewer => viewer.State == TicketViewerState.Viewing);
        _store.Get(Ticket2).Viewers.ShouldBeEmpty();

        await Task.WhenAll(Enumerable.Range(0, Agents).Select(index => Task.Run(() => _store.LeaveAll($"c{index}"), TestContext.Current.CancellationToken)));

        _store.Get(Ticket1).Viewers.ShouldBeEmpty();
        _store.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_viewer_snapshot_is_a_copy_that_later_changes_do_not_touch()
    {
        var before = _store.Join("c1", Ticket1, Sam, "Sam").Presence;

        _store.Join("c2", Ticket1, Kim, "Kim");

        before.Viewers.Count.ShouldBe(1);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter-query "/*/*/InMemoryTicketPresenceStoreTests/*"
```

Expected: FAIL to compile:

```text
tests/TechStrap.Infrastructure.IntegrationTests/Live/InMemoryTicketPresenceStoreTests.cs(4,32): error CS0234: The type or namespace name 'Live' does not exist in the namespace 'TechStrap.Infrastructure' (are you missing an asse...
tests/TechStrap.Infrastructure.IntegrationTests/Live/InMemoryTicketPresenceStoreTests.cs(18,22): error CS0246: The type or namespace name 'InMemoryTicketPresenceStore' could not be found (are you missing a using directive or an...
Build failed with exit code: 1.
```

- [ ] **Step 3: Write the store**

`src/TechStrap.Infrastructure/Live/InMemoryTicketPresenceStore.cs` (new)

```csharp
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// Presence in the Api process's memory (D-007: one instance). One lock guards everything: the data is tiny (a few agents on a few tickets), every operation is
/// a handful of dictionary writes, and a single lock makes "compare before and after" trivially atomic.
/// An entry belongs to a hub connection; an agent with several tabs is one viewer. A composing hint is a lease of <see cref="TicketLiveLimits.ComposingTtlSeconds"/>
/// that only a refresh extends; an expired lease is never reported, so nothing needs to sweep.
/// "Changed" means what other agents would see differs, or a composing lease was extended: that refresh is the heartbeat the clients' own clear-after-TTL
/// depends on, so it is sent (the hub is not asked to send anything else on a repeat that moved nothing).
/// </summary>
internal sealed class InMemoryTicketPresenceStore(TimeProvider clock) : ITicketPresenceStore
{
    private static readonly TimeSpan ComposingLease = TimeSpan.FromSeconds(TicketLiveLimits.ComposingTtlSeconds);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Dictionary<string, Entry>> _byTicket = [];
    private readonly Dictionary<string, HashSet<Guid>> _byConnection = [];

    private sealed record Entry(Guid AgentId, string DisplayName, DateTimeOffset? ComposingUntil);

    /// <summary>True when nothing is held: every connection that joined has left. Lets a test prove the store does not grow forever.</summary>
    internal bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _byTicket.Count == 0 && _byConnection.Count == 0;
            }
        }
    }

    public PresenceChange Join(string connectionId, Guid ticketId, Guid agentId, string displayName)
    {
        lock (_gate)
        {
            var before = Snapshot(ticketId);
            Entries(ticketId)[connectionId] = new Entry(agentId, displayName, null);
            if (!_byConnection.TryGetValue(connectionId, out var tickets))
            {
                _byConnection[connectionId] = tickets = [];
            }

            tickets.Add(ticketId);
            return Change(before, Snapshot(ticketId), leaseMoved: false);
        }
    }

    public PresenceChange? SetComposing(string connectionId, Guid ticketId, bool isComposing)
    {
        lock (_gate)
        {
            if (!_byTicket.TryGetValue(ticketId, out var entries) || !entries.TryGetValue(connectionId, out var entry))
            {
                return null;
            }

            var before = Snapshot(ticketId);
            var until = isComposing ? clock.GetUtcNow() + ComposingLease : (DateTimeOffset?)null;
            entries[connectionId] = entry with { ComposingUntil = until };

            // A lease that was already over and is cleared again moves nothing anyone saw.
            var leaseMoved = isComposing && entry.ComposingUntil != until;
            return Change(before, Snapshot(ticketId), leaseMoved);
        }
    }

    public PresenceChange Leave(string connectionId, Guid ticketId)
    {
        lock (_gate)
        {
            var before = Snapshot(ticketId);
            Remove(connectionId, ticketId);
            return Change(before, Snapshot(ticketId), leaseMoved: false);
        }
    }

    public IReadOnlyList<PresenceChange> LeaveAll(string connectionId)
    {
        lock (_gate)
        {
            if (!_byConnection.TryGetValue(connectionId, out var tickets))
            {
                return [];
            }

            var changes = new List<PresenceChange>(tickets.Count);
            foreach (var ticketId in tickets.ToArray())
            {
                var before = Snapshot(ticketId);
                Remove(connectionId, ticketId);
                changes.Add(Change(before, Snapshot(ticketId), leaseMoved: false));
            }

            return changes;
        }
    }

    public TicketPresence Get(Guid ticketId)
    {
        lock (_gate)
        {
            return Snapshot(ticketId);
        }
    }

    private Dictionary<string, Entry> Entries(Guid ticketId)
    {
        if (!_byTicket.TryGetValue(ticketId, out var entries))
        {
            _byTicket[ticketId] = entries = [];
        }

        return entries;
    }

    private void Remove(string connectionId, Guid ticketId)
    {
        if (_byTicket.TryGetValue(ticketId, out var entries) && entries.Remove(connectionId) && entries.Count == 0)
        {
            _byTicket.Remove(ticketId);
        }

        if (_byConnection.TryGetValue(connectionId, out var tickets) && tickets.Remove(ticketId) && tickets.Count == 0)
        {
            _byConnection.Remove(connectionId);
        }
    }

    private static PresenceChange Change(TicketPresence before, TicketPresence after, bool leaseMoved) =>
        new(leaseMoved || !before.Viewers.SequenceEqual(after.Viewers), after);

    /// <summary>The viewers now: one per agent (composing if any of their connections holds a live lease), by name then id.</summary>
    private TicketPresence Snapshot(Guid ticketId)
    {
        if (!_byTicket.TryGetValue(ticketId, out var entries))
        {
            return new TicketPresence(ticketId, []);
        }

        var now = clock.GetUtcNow();
        var viewers = entries.Values
            .GroupBy(entry => entry.AgentId)
            .Select(group => new TicketViewer(
                group.Key,
                group.OrderBy(entry => entry.DisplayName, StringComparer.Ordinal).First().DisplayName,
                group.Any(entry => entry.ComposingUntil > now) ? TicketViewerState.Composing : TicketViewerState.Viewing))
            .OrderBy(viewer => viewer.DisplayName, StringComparer.Ordinal)
            .ThenBy(viewer => viewer.AgentId)
            .ToList();
        return new TicketPresence(ticketId, viewers);
    }
}
```

- [ ] **Step 4: Write the failing hook tests**

`tests/TechStrap.Infrastructure.IntegrationTests/Live/RecordingBroadcaster.cs` (new): records every call and can be told to throw or to wait.

```csharp
using System.Collections.Concurrent;
using TechStrap.Application.Live;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>An <see cref="ITicketChangeBroadcaster"/> that records every call and can be told to throw or to wait.</summary>
internal sealed class RecordingBroadcaster : ITicketChangeBroadcaster
{
    private readonly ConcurrentQueue<TicketChange> _attempts = new();

    /// <summary>Every change handed to <see cref="PublishAsync"/>, in order, including the ones that threw.</summary>
    public IReadOnlyList<TicketChange> Attempts => [.. _attempts];

    /// <summary>Runs after the call is recorded; throw from it or wait on the token to simulate a failing or a hanging hub.</summary>
    public Func<TicketChange, CancellationToken, Task>? Behaviour { get; set; }

    public Task PublishAsync(TicketChange change, CancellationToken cancellationToken)
    {
        _attempts.Enqueue(change);
        return Behaviour?.Invoke(change, cancellationToken) ?? Task.CompletedTask;
    }

    public Task PublishPresenceAsync(TicketPresence presence, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

`tests/TechStrap.Infrastructure.IntegrationTests/Live/TicketChangePublishingTests.cs` (new): real Postgres, the recorder registered over the default exactly as the Api and the Worker register theirs.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Application.Live;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>
/// The post-commit hook (D-018) against real Postgres: a committed unit of work publishes one change per ticket, and nothing that did not commit ever does.
/// The broadcaster is a recorder registered over the default, exactly as the Api and the Worker register theirs.
/// </summary>
public sealed class TicketChangePublishingTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly RecordingBroadcaster _recorder = new();

    private PersistenceTestHost NewHost() =>
        new(Database, configure: services => services.Replace(ServiceDescriptor.Singleton<ITicketChangeBroadcaster>(_recorder)));

    private static async Task StageNewTicketAsync(IServiceProvider services, TicketScenario scenario, string subject)
    {
        var number = (await services.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(scenario.Acme.Id, Ct)).Value;
        var ticket = Ticket.Create(number, scenario.Acme.Id, scenario.Requester.Id, subject, TicketChannel.Web, null, false, scenario.Host.Clock).Value;
        ticket.AddCustomerReply(scenario.Requester.Id, "<p>Help</p>", scenario.Host.Clock).IsSuccess.ShouldBeTrue();
        services.GetRequiredService<ITicketRepository>().Add(ticket);
    }

    [Fact]
    public async Task Committing_a_new_ticket_publishes_one_created_change_with_ids_and_no_customer_actor()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        _recorder.Attempts.ShouldBeEmpty();

        var ticket = await scenario.CreateTicketAsync();

        var change = _recorder.Attempts.ShouldHaveSingleItem();
        change.Kind.ShouldBe(TicketChangeKinds.Created);
        change.EventType.ShouldBe(TicketEventTypes.Created);
        change.TicketId.ShouldBe(ticket.Id);
        change.TicketNumber.ShouldBe(ticket.Number.ToString());
        change.ProductId.ShouldBe(scenario.Acme.Id);
        change.ActorAgentId.ShouldBeNull();
        change.EventId.ShouldNotBe(Guid.Empty);
        change.OccurredAt.ShouldBe(host.Clock.GetUtcNow() - TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Several_events_in_one_commit_publish_one_change_named_by_the_newest()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        _recorder.Attempts.Count.ShouldBe(1);

        var result = await scenario.UpdateAsync(ticket.Id, loaded =>
        {
            loaded.ChangeStatus(TicketStatus.Open, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            host.Clock.Advance(TimeSpan.FromSeconds(1));
            loaded.ChangePriority(TicketPriority.High, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            host.Clock.Advance(TimeSpan.FromSeconds(1));
            loaded.AddInternalNote(scenario.Agent.Id, "<p>looking</p>", host.Clock).IsSuccess.ShouldBeTrue();
        });

        result.IsSuccess.ShouldBeTrue();
        _recorder.Attempts.Count.ShouldBe(2);
        var change = _recorder.Attempts[1];
        change.Kind.ShouldBe(TicketChangeKinds.Updated);
        change.EventType.ShouldBe(TicketEventTypes.MessageAdded);
        change.ActorAgentId.ShouldBe(scenario.Agent.Id);
        change.TicketId.ShouldBe(ticket.Id);
    }

    [Fact]
    public async Task One_commit_that_touches_two_tickets_publishes_one_change_for_each()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);

        var result = await host.CommitAsync(async services =>
        {
            await StageNewTicketAsync(services, scenario, "First");
            await StageNewTicketAsync(services, scenario, "Second");
        });

        result.IsSuccess.ShouldBeTrue();
        _recorder.Attempts.Count.ShouldBe(2);
        _recorder.Attempts.Select(change => change.TicketId).Distinct().Count().ShouldBe(2);
        _recorder.Attempts.ShouldAllBe(change => change.Kind == TicketChangeKinds.Created);
    }

    [Fact]
    public async Task Moving_a_ticket_to_another_product_publishes_the_new_product()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();

        (await scenario.UpdateAsync(ticket.Id, loaded =>
            loaded.MoveToProduct(scenario.Orbitly.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue())).IsSuccess.ShouldBeTrue();

        var change = _recorder.Attempts.Last();
        change.EventType.ShouldBe(TicketEventTypes.ProductChanged);
        change.ProductId.ShouldBe(scenario.Orbitly.Id);
    }

    [Fact]
    public async Task Work_that_was_saved_but_rolled_back_publishes_nothing_and_leaves_nothing_behind_in_the_next_scope()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);

        await using (var scope = host.CreateScope())
        {
            await using var unitOfWork = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
            await StageNewTicketAsync(scope.ServiceProvider, scenario, "Rolled back");

            // SaveChanges runs (the events are captured) but the transaction is never committed: disposing the scope rolls it back.
            await scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().SaveChangesAsync(Ct);
        }

        _recorder.Attempts.ShouldBeEmpty();

        var committed = await scenario.CreateTicketAsync("Committed");
        _recorder.Attempts.ShouldHaveSingleItem().TicketId.ShouldBe(committed.Id);
    }

    [Fact]
    public async Task A_rollback_followed_by_a_commit_in_the_same_scope_publishes_only_the_commit()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        await using var scope = host.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await using (var first = await unitOfWork.BeginAsync(Ct))
        {
            await StageNewTicketAsync(scope.ServiceProvider, scenario, "Dropped");
            await scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().SaveChangesAsync(Ct);
        }

        scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().ChangeTracker.Clear();
        await using var second = await unitOfWork.BeginAsync(Ct);
        await StageNewTicketAsync(scope.ServiceProvider, scenario, "Kept");
        (await second.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();

        _recorder.Attempts.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_concurrency_conflict_publishes_nothing_for_the_loser()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var before = _recorder.Attempts.Count;

        await using var winnerScope = host.CreateScope();
        await using var loserScope = host.CreateScope();
        await using var winner = await winnerScope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        await using var loser = await loserScope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        var winnersTicket = (await winnerScope.ServiceProvider.GetRequiredService<ITicketRepository>().GetByIdAsync(ticket.Id, Ct))!;
        var losersTicket = (await loserScope.ServiceProvider.GetRequiredService<ITicketRepository>().GetByIdAsync(ticket.Id, Ct))!;
        winnersTicket.ChangeStatus(TicketStatus.Open, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
        losersTicket.ChangePriority(TicketPriority.Urgent, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
        winnerScope.ServiceProvider.GetRequiredService<ITicketRepository>().Update(winnersTicket);
        loserScope.ServiceProvider.GetRequiredService<ITicketRepository>().Update(losersTicket);

        (await winner.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
        var lost = await loser.CommitAsync(Ct);

        lost.IsFailure.ShouldBeTrue();
        lost.Errors[0].Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        var published = _recorder.Attempts.Skip(before).ToList();
        published.ShouldHaveSingleItem().EventType.ShouldBe(TicketEventTypes.StatusChanged);
    }

    [Fact]
    public async Task A_unique_violation_publishes_nothing()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var first = await host.CommitAsync(services =>
        {
            var ticket = Ticket.Create(TicketNumber.Create("ACME", 900).Value, scenario.Acme.Id, scenario.Requester.Id, "One", TicketChannel.Web, null, false, host.Clock).Value;
            services.GetRequiredService<ITicketRepository>().Add(ticket);
            return Task.CompletedTask;
        });
        first.IsSuccess.ShouldBeTrue();
        _recorder.Attempts.Count.ShouldBe(1);

        var second = await host.CommitAsync(services =>
        {
            var ticket = Ticket.Create(TicketNumber.Create("ACME", 900).Value, scenario.Acme.Id, scenario.Requester.Id, "Two", TicketChannel.Web, null, false, host.Clock).Value;
            services.GetRequiredService<ITicketRepository>().Add(ticket);
            return Task.CompletedTask;
        });

        second.IsFailure.ShouldBeTrue();
        second.Errors[0].Code.ShouldBe(PersistenceErrorCodes.Duplicate);
        _recorder.Attempts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_broadcaster_that_throws_never_fails_the_commit_and_does_not_poison_the_next_one()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        _recorder.Behaviour = (_, _) => throw new InvalidOperationException("the hub is down");

        var ticket = await scenario.CreateTicketAsync("Survives");

        _recorder.Attempts.ShouldHaveSingleItem();
        (await scenario.LoadAsync(ticket.Id)).ShouldNotBeNull();

        _recorder.Behaviour = null;
        var next = await scenario.CreateTicketAsync("Next");
        _recorder.Attempts.Count.ShouldBe(2);
        _recorder.Attempts[1].TicketId.ShouldBe(next.Id);
    }

    [Fact]
    public async Task One_ticket_failing_to_publish_does_not_stop_the_other_ticket_in_the_same_commit()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var calls = 0;
        _recorder.Behaviour = (_, _) => Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("first fails") : Task.CompletedTask;

        var result = await host.CommitAsync(async services =>
        {
            await StageNewTicketAsync(services, scenario, "First");
            await StageNewTicketAsync(services, scenario, "Second");
        });

        result.IsSuccess.ShouldBeTrue();
        _recorder.Attempts.Count.ShouldBe(2);
    }

    [Fact(Timeout = 60000)]
    public async Task A_broadcaster_that_hangs_is_cut_off_and_the_commit_still_succeeds()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var cancelled = false;
        _recorder.Behaviour = async (_, token) =>
        {
            // Waits for the publisher's own time limit (never for the test's token, which only guards the test itself).
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, TestContext.Current.CancellationToken);
            try
            {
                await Task.Delay(Timeout.Infinite, linked.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                throw;
            }
        };

        var ticket = await scenario.CreateTicketAsync("Slow hub");

        cancelled.ShouldBeTrue();
        (await scenario.LoadAsync(ticket.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_save_with_no_explicit_transaction_publishes_once_after_it_completes()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var before = _recorder.Attempts.Count;

        await using var scope = host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TechStrapDbContext>();
        (await scope.ServiceProvider.GetRequiredService<ITicketRepository>().GetByIdAsync(ticket.Id, Ct)).ShouldNotBeNull();
        context.Set<TicketEventRecord>().Add(new TicketEventRecord
        {
            Id = Guid.CreateVersion7(),
            TicketId = ticket.Id,
            Type = TicketEventType.Assigned,
            ActorType = ActorType.System,
            Payload = "{}",
            OccurredAt = host.Clock.GetUtcNow(),
        });

        // One INSERT: EF opens no transaction for a single statement, so there is no commit to wait for.
        context.Database.CurrentTransaction.ShouldBeNull();
        await context.SaveChangesAsync(Ct);

        var published = _recorder.Attempts.Skip(before).ToList();
        published.ShouldHaveSingleItem().EventType.ShouldBe(TicketEventTypes.Assigned);
    }

    [Fact]
    public async Task A_save_that_uses_an_implicit_transaction_publishes_once_when_that_transaction_commits()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);

        await using (var scope = host.CreateScope())
        {
            // A ticket, a message and two events are several statements, so EF wraps them in a transaction of its own.
            var ticket = Ticket.Create(TicketNumber.Create("ACME", 800).Value, scenario.Acme.Id, scenario.Requester.Id, "Implicit", TicketChannel.Web, null, false, host.Clock).Value;
            ticket.AddCustomerReply(scenario.Requester.Id, "<p>Help</p>", host.Clock).IsSuccess.ShouldBeTrue();
            scope.ServiceProvider.GetRequiredService<ITicketRepository>().Add(ticket);
            await scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().SaveChangesAsync(Ct);
        }

        _recorder.Attempts.ShouldHaveSingleItem().Kind.ShouldBe(TicketChangeKinds.Created);
    }

    [Fact]
    public async Task A_failed_save_without_a_transaction_drops_what_it_captured()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var first = await scenario.CreateTicketAsync("First");
        var other = await scenario.CreateTicketAsync("Other");
        var before = _recorder.Attempts.Count;
        var eventId = Guid.CreateVersion7();

        TicketEventRecord Event(Guid id, Guid ticketId) => new()
        {
            Id = id,
            TicketId = ticketId,
            Type = TicketEventType.Assigned,
            ActorType = ActorType.System,
            Payload = "{}",
            OccurredAt = host.Clock.GetUtcNow(),
        };

        await using (var scope = host.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ITicketRepository>().GetByIdAsync(first.Id, Ct)).ShouldNotBeNull();
            scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().Set<TicketEventRecord>().Add(Event(eventId, first.Id));
            await scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().SaveChangesAsync(Ct);
        }

        _recorder.Attempts.Count.ShouldBe(before + 1);

        // A second context inserts an event for the other ticket under the same primary key: the INSERT fails, with no transaction around it to roll back.
        await using var second = host.CreateScope();
        var context = second.ServiceProvider.GetRequiredService<TechStrapDbContext>();
        var tickets = second.ServiceProvider.GetRequiredService<ITicketRepository>();
        (await tickets.GetByIdAsync(first.Id, Ct)).ShouldNotBeNull();
        (await tickets.GetByIdAsync(other.Id, Ct)).ShouldNotBeNull();
        context.Set<TicketEventRecord>().Add(Event(eventId, other.Id));
        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
        _recorder.Attempts.Count.ShouldBe(before + 1);

        // What the failed save captured (the other ticket) must not ride along with the next good save (the first ticket).
        context.ChangeTracker.Entries<TicketEventRecord>().Where(entry => entry.State == EntityState.Added).ToList().ForEach(entry => entry.State = EntityState.Detached);
        context.Set<TicketEventRecord>().Add(Event(Guid.CreateVersion7(), first.Id));
        await context.SaveChangesAsync(Ct);

        var published = _recorder.Attempts.Skip(before + 1).ToList();
        published.ShouldHaveSingleItem().TicketId.ShouldBe(first.Id);
    }

    [Fact]
    public async Task Events_that_were_only_read_are_not_announced_again()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var before = _recorder.Attempts.Count;

        var result = await host.CommitAsync(async services =>
        {
            // The ticket's old events are tracked, unchanged, in the same context that then saves a new one.
            await services.GetRequiredService<TechStrapDbContext>().Set<TicketEventRecord>().Where(item => item.TicketId == ticket.Id).ToListAsync(Ct);
            var repository = services.GetRequiredService<ITicketRepository>();
            var loaded = (await repository.GetByIdAsync(ticket.Id, Ct))!;
            loaded.ChangeStatus(TicketStatus.Open, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            repository.Update(loaded);
        });

        result.IsSuccess.ShouldBeTrue();
        var change = _recorder.Attempts.Skip(before).ShouldHaveSingleItem();
        change.Kind.ShouldBe(TicketChangeKinds.Updated);
        change.EventType.ShouldBe(TicketEventTypes.StatusChanged);
    }

    [Fact]
    public async Task Without_a_registration_of_its_own_the_broadcaster_is_the_null_one()
    {
        await using var host = new PersistenceTestHost(Database);
        await using var scope = host.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITicketChangeBroadcaster>().GetType().Name.ShouldBe("NullTicketChangeBroadcaster");
        var scenario = await TicketScenario.CreateAsync(host);
        (await scenario.CreateTicketAsync()).ShouldNotBeNull();
    }

    [Fact]
    public async Task Deleting_a_ticket_publishes_a_resync_because_no_event_describes_it()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var before = _recorder.Attempts.Count;

        var result = await host.CommitAsync(async services =>
        {
            var repository = services.GetRequiredService<ITicketRepository>();
            repository.Remove((await repository.GetByIdAsync(ticket.Id, Ct))!);
        });

        result.IsSuccess.ShouldBeTrue();
        var change = _recorder.Attempts.Skip(before).ShouldHaveSingleItem();
        change.Kind.ShouldBe(TicketChangeKinds.Resync);
        change.TicketId.ShouldBe(Guid.Empty);
    }

    [Fact]
    public async Task The_append_only_guard_still_refuses_a_modified_event_and_nothing_is_published()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var before = _recorder.Attempts.Count;

        await using var scope = host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TechStrapDbContext>();
        var record = await context.Set<TicketEventRecord>().FirstAsync(item => item.TicketId == ticket.Id, Ct);
        record.Payload = "{\"edited\":true}";

        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync(Ct));

        _recorder.Attempts.Count.ShouldBe(before);
    }

    [Fact]
    public async Task Configure_keeps_its_two_argument_shape_and_adds_no_hook_for_callers_that_use_it_directly()
    {
        typeof(TechStrapDatabase).GetMethod(nameof(TechStrapDatabase.Configure))!.GetParameters().Length.ShouldBe(2);
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var before = _recorder.Attempts.Count;

        // A context built by the static Configure (the way the tools and most tests build one) has the guard but no hook, and still works.
        await using var plain = Database.CreateDbContext();
        plain.Set<TicketEventRecord>().Add(new TicketEventRecord
        {
            Id = Guid.CreateVersion7(),
            TicketId = ticket.Id,
            Type = TicketEventType.Assigned,
            ActorType = ActorType.System,
            Payload = "{}",
            OccurredAt = host.Clock.GetUtcNow(),
        });
        await plain.SaveChangesAsync(Ct);

        _recorder.Attempts.Count.ShouldBe(before);
    }
}
```

- [ ] **Step 5: Run them to verify they fail**

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter-query "/*/*/TicketChangePublishingTests/*"
```

Expected: FAIL (the types exist from Step 3 or are not needed by the tests, but nothing publishes yet, so every test that expects a change fails; the two that pass are the append-only guard and the `Configure` shape):

```text
failed TechStrap.Infrastructure.IntegrationTests.Live.TicketChangePublishingTests.A_rollback_followed_by_a_commit_in_the_same_scope_publishes_only_the_commit (486ms)
failed TechStrap.Infrastructure.IntegrationTests.Live.TicketChangePublishingTests.Committing_a_new_ticket_publishes_one_created_change_with_ids_and_no_customer_actor (41ms)
failed TechStrap.Infrastructure.IntegrationTests.Live.TicketChangePublishingTests.Deleting_a_ticket_publishes_a_resync_because_no_event_describes_it (240ms)
failed TechStrap.Infrastructure.IntegrationTests.Live.TicketChangePublishingTests.A_unique_violation_publishes_nothing (37ms)
failed TechStrap.Infrastructure.IntegrationTests.Live.TicketChangePublishingTests.A_save_with_no_explicit_transaction_publishes_once_after_it_completes (46ms)
failed TechStrap.Infrastructure.IntegrationTests.Live.TicketChangePublishingTests.A_concurrency_conflict_publishes_nothing_for_the_loser (67ms)
failed TechStrap.Infrastructure.IntegrationTests.Live.TicketChangePublishingTests.Work_that_was_saved_but_rolled_back_publishes_nothing_and_leaves_nothing_behind_in_the_next_scope (48ms)
failed TechStrap.Infrastructure.IntegrationTests.Live.TicketChangePublishingTests.One_commit_that_touches_two_tickets_publishes_one_change_for_each (41ms)
failed TechStrap.Infrastructure.IntegrationTests.Live.TicketChangePublishingTests.Several_events_in_one_commit_publish_one_change_named_by_the_newest (38ms)
failed TechStrap.Infrastructure.IntegrationTests.Live.TicketChangePublishingTests.A_broadcaster_that_throws_never_fails_the_commit_and_does_not_poison_the_next_one (39ms)
failed TechStrap.Infrastructure.IntegrationTests.Live.TicketChangePublishingTests.A_save_that_uses_an_implicit_transaction_publishes_once_when_that_transaction_commits (31ms)
failed TechStrap.Infrastructure.IntegrationTests.Live.TicketChangePublishingTests.A_broadcaster_that_hangs_is_cut_off_and_the_commit_still_succeeds (40ms)
failed TechStrap.Infrastructure.IntegrationTests.Live.TicketChangePublishingTests.One_ticket_failing_to_publish_does_not_stop_the_other_ticket_in_the_same_commit (42ms)
failed TechStrap.Infrastructure.IntegrationTests.Live.TicketChangePublishingTests.Moving_a_ticket_to_another_product_publishes_the_new_product (57ms)
tests/TechStrap.Infrastructure.IntegrationTests/bin/Release/net10.0/TechStrap.Infrastructure.IntegrationTests.dll (net10.0|x64) failed with 14 error(s) (6s 344ms)
  total: 16
  succeeded: 2
```

- [ ] **Step 6: Write the hook**

`src/TechStrap.Infrastructure/Live/NullTicketChangeBroadcaster.cs` (new)

```csharp
using TechStrap.Application.Live;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// The default <see cref="ITicketChangeBroadcaster"/>: does nothing. <c>AddTechStrapPersistence</c> registers it so the post-commit hook always has something
/// to call (the Admin-less test hosts and tools have no hub and no NOTIFY); the Api and the Worker replace it with their own.
/// </summary>
internal sealed class NullTicketChangeBroadcaster : ITicketChangeBroadcaster
{
    public Task PublishAsync(TicketChange change, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PublishPresenceAsync(TicketPresence presence, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

`src/TechStrap.Infrastructure/Live/PendingTicketChanges.cs` (new): what each open context has staged but not committed, and the consolidation into one change per ticket.

```csharp
using System.Runtime.CompilerServices;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.Live;

/// <summary>One ticket event captured while a unit of work is open. The number and product are null when the ticket was not tracked by the context.</summary>
internal sealed record CapturedTicketEvent(
    Guid EventId, Guid TicketId, string? TicketNumber, Guid? ProductId, TicketEventType Type, Guid? ActorAgentId, DateTimeOffset OccurredAt);

/// <summary>
/// What each open <see cref="Microsoft.EntityFrameworkCore.DbContext"/> has staged but not yet committed (the first half of the post-commit hook, D-018). The interceptors are
/// singletons while a context is per request, so the state is kept per context, in a table that forgets a context when it is collected: a context that is
/// disposed with its transaction open can never leak an entry. All access is under the entry's own lock.
/// </summary>
internal sealed class PendingTicketChanges(TimeProvider clock)
{
    private readonly ConditionalWeakTable<object, Staged> _staged = [];

    private sealed class Staged
    {
        public List<CapturedTicketEvent> Events { get; } = [];

        public bool ResyncNeeded { get; set; }
    }

    /// <param name="resyncNeeded">True when something happened that no event describes, such as a deleted ticket: the clients must reload everything.</param>
    public void Add(object context, IEnumerable<CapturedTicketEvent> events, bool resyncNeeded)
    {
        var staged = _staged.GetOrCreateValue(context);
        lock (staged)
        {
            staged.Events.AddRange(events);
            staged.ResyncNeeded |= resyncNeeded;
        }
    }

    public void Discard(object context) => _staged.Remove(context);

    /// <summary>
    /// Empties the context's staging and returns what to publish: one change per ticket however many events it got (a <c>Created</c> event makes it a <c>Created</c>
    /// change named by that event, otherwise the newest event names it), oldest first. A ticket the context could not describe, or a deletion, becomes a single <c>Resync</c> at the end.
    /// </summary>
    public IReadOnlyList<TicketChange> Take(object context)
    {
        if (!_staged.TryGetValue(context, out var staged))
        {
            return [];
        }

        _staged.Remove(context);
        lock (staged)
        {
            var changes = new List<TicketChange>();
            var resync = staged.ResyncNeeded;
            foreach (var group in staged.Events.GroupBy(captured => captured.TicketId))
            {
                var described = group.LastOrDefault(captured => captured.TicketNumber is not null && captured.ProductId is not null);
                if (described is null)
                {
                    resync = true;
                    continue;
                }

                // A new ticket is named by its Created event; otherwise the newest event names the change. OrderBy is stable, so events with the same time keep the order they were staged in.
                var created = group.FirstOrDefault(captured => captured.Type == TicketEventType.Created);
                var named = created ?? group.OrderBy(captured => captured.OccurredAt).Last();
                var kind = created is null ? TicketChangeKinds.Updated : TicketChangeKinds.Created;
                changes.Add(new TicketChange(
                    named.EventId, group.Key, described.TicketNumber!, described.ProductId!.Value, named.Type.ToString(), named.ActorAgentId, named.OccurredAt, kind));
            }

            var ordered = changes.OrderBy(change => change.OccurredAt).ToList();
            if (resync)
            {
                ordered.Add(TicketChange.Resync(Guid.CreateVersion7(clock.GetUtcNow()), clock.GetUtcNow()));
            }

            return ordered;
        }
    }
}
```

`src/TechStrap.Infrastructure/Live/TicketChangePublisher.cs` (new): the isolation and the time limit.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TechStrap.Application.Live;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// The second half of the post-commit hook (D-018): hands what a context staged to the process's <see cref="ITicketChangeBroadcaster"/>, one call per ticket.
/// It never throws: a broadcaster that fails or hangs is logged by exception type name (never the message) and skipped, so a committed change is never reported as a
/// failed request, and one ticket's failure does not stop the next. The publish has its own time limit, because it runs on the request's own thread of work.
/// </summary>
internal sealed class TicketChangePublisher(PendingTicketChanges pending, ITicketChangeBroadcaster broadcaster, ILogger<TicketChangePublisher> logger)
{
    /// <summary>How long one broadcast may take before it is abandoned.</summary>
    public static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(5);

    public async Task PublishAsync(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var change in pending.Take(context))
        {
            try
            {
                using var timeout = new CancellationTokenSource(PublishTimeout);
                await broadcaster.PublishAsync(change, timeout.Token);
            }
            catch (Exception exception)
            {
                // Type name only: an exception message may carry data (log redaction rule). The change was committed; the clients catch up on their next load.
                logger.LogWarning("Publishing a ticket change failed ({ExceptionType}).", exception.GetType().Name);
            }
        }
    }

    public void Publish(DbContext? context) => PublishAsync(context).GetAwaiter().GetResult();

    public void Discard(DbContext? context)
    {
        if (context is not null)
        {
            pending.Discard(context);
        }
    }
}
```

`src/TechStrap.Infrastructure/Live/TicketChangeCaptureInterceptor.cs` (new): the first half; it reads only what the context already tracks.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// The first half of the post-commit hook (D-018, D-046): while SaveChanges runs it notes the <c>TicketEvent</c> rows being inserted (and whether a ticket is being deleted) and
/// stages them in <see cref="PendingTicketChanges"/>. It publishes nothing, because at this point the unit of work has not committed. The
/// <see cref="TicketChangePublishingInterceptor"/> publishes after the commit and drops the staging on a rollback; this interceptor drops it when SaveChanges itself fails (a concurrency conflict, a
/// constraint violation), and publishes straight away only when there is no explicit transaction (nothing will commit later).
/// It reads only what the context already tracks, never the database, so it adds no query to a save.
/// </summary>
internal sealed class TicketChangeCaptureInterceptor(PendingTicketChanges pending, TicketChangePublisher publisher) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is { Database.CurrentTransaction: null } context)
        {
            publisher.Publish(context);
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { Database.CurrentTransaction: null } context)
        {
            await publisher.PublishAsync(context);
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => publisher.Discard(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        publisher.Discard(eventData.Context);
        return Task.CompletedTask;
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var added = context.ChangeTracker.Entries<TicketEventRecord>().Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity).ToList();
        var tickets = context.ChangeTracker.Entries<TicketRecord>().Where(entry => entry.State != EntityState.Detached).ToDictionary(entry => entry.Entity.Id, entry => entry.Entity);
        var deleted = context.ChangeTracker.Entries<TicketRecord>().Any(entry => entry.State == EntityState.Deleted);
        if (added.Count == 0 && !deleted)
        {
            return;
        }

        pending.Add(
            context,
            added.Select(record => tickets.TryGetValue(record.TicketId, out var ticket)
                ? new CapturedTicketEvent(record.Id, record.TicketId, ticket.Number, ticket.ProductId, record.Type, ActorAgent(record), record.OccurredAt)
                : new CapturedTicketEvent(record.Id, record.TicketId, null, null, record.Type, ActorAgent(record), record.OccurredAt)),
            resyncNeeded: deleted);
    }

    /// <summary>The agent row id for an agent actor; null for a requester or the system (so nothing about a customer is ever sent).</summary>
    private static Guid? ActorAgent(TicketEventRecord record) => record.ActorType == Domain.Tickets.ActorType.Agent ? record.ActorId : null;
}
```

`src/TechStrap.Infrastructure/Live/TicketChangePublishingInterceptor.cs` (new): the post-commit half.

```csharp
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// The post-commit half of the hook (D-018): publishes what <see cref="TicketChangeCaptureInterceptor"/> staged once the database transaction has really committed, and drops it when the
/// transaction rolls back or fails. <c>UnitOfWork</c> commits after SaveChanges, so SaveChanges' own "saved" event fires too early to be the post-commit point.
/// Nothing here can fail the commit that has already happened: <see cref="TicketChangePublisher"/> swallows and logs every error.
/// </summary>
internal sealed class TicketChangePublishingInterceptor(TicketChangePublisher publisher) : DbTransactionInterceptor
{
    public override void TransactionCommitted(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData) =>
        publisher.Publish(eventData.Context);

    public override Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        publisher.PublishAsync(eventData.Context);

    public override void TransactionRolledBack(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData) =>
        publisher.Discard(eventData.Context);

    public override Task TransactionRolledBackAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        publisher.Discard(eventData.Context);
        return Task.CompletedTask;
    }

    public override void TransactionFailed(System.Data.Common.DbTransaction transaction, TransactionErrorEventData eventData) =>
        publisher.Discard(eventData.Context);

    public override Task TransactionFailedAsync(System.Data.Common.DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        publisher.Discard(eventData.Context);
        return Task.CompletedTask;
    }
}
```

`src/TechStrap.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs` (modify): the null broadcaster, the singletons and the two interceptors added where the container is. `TechStrapDatabase.Configure` is not touched.

```diff
--- a/src/TechStrap.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs
+++ b/src/TechStrap.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs
@@ -4,6 +4,8 @@ using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.DependencyInjection.Extensions;
 using Microsoft.Extensions.Options;
+using TechStrap.Application.Live;
 using TechStrap.Application.Persistence;
 using TechStrap.Application.Requesters;
+using TechStrap.Infrastructure.Live;
 using TechStrap.Infrastructure.Persistence.Repositories;
 
@@ -16,12 +18,25 @@ public static class PersistenceServiceCollectionExtensions
     /// (all scoped, so one request or one worker iteration shares one context and one transaction). The connection string is
     /// resolved when the context is first created, so test overrides apply; outside Development a blank one fails the start (<see cref="DatabaseConnectionOptions"/>).
+    /// It also wires the post-commit hook (D-018): two interceptors that publish committed ticket changes to the host's <see cref="ITicketChangeBroadcaster"/>, which is
+    /// <see cref="NullTicketChangeBroadcaster"/> until the Api or the Worker replaces it (register theirs after this call, with <c>Replace</c>).
     /// Used by the Api and the Worker only; Admin and Portal never touch the database.
     /// </summary>
     public static IServiceCollection AddTechStrapPersistence(this IServiceCollection services)
     {
+        services.TryAddSingleton<ITicketChangeBroadcaster, NullTicketChangeBroadcaster>();
+        services.TryAddSingleton(TimeProvider.System);
+        services.AddSingleton<PendingTicketChanges>();
+        services.AddSingleton<TicketChangePublisher>();
+        services.AddSingleton<TicketChangeCaptureInterceptor>();
+        services.AddSingleton<TicketChangePublishingInterceptor>();
+
+        // Configure stays static and DI-free for the tools and tests that call it directly; the hook's interceptors need the container, so they are added here.
         services.AddDbContext<TechStrapDbContext>((provider, options) =>
             TechStrapDatabase.Configure(
                 options,
-                provider.GetRequiredService<IConfiguration>().GetConnectionString(TechStrapDatabase.ConnectionStringName)));
+                provider.GetRequiredService<IConfiguration>().GetConnectionString(TechStrapDatabase.ConnectionStringName))
+                .AddInterceptors(
+                    provider.GetRequiredService<TicketChangeCaptureInterceptor>(),
+                    provider.GetRequiredService<TicketChangePublishingInterceptor>()));
 
         services.AddOptions<DatabaseConnectionOptions>()
@@ -34,5 +49,4 @@ public static class PersistenceServiceCollectionExtensions
             .AddCheck<DatabaseReadinessHealthCheck>("database", tags: [TechStrapDatabase.ReadyHealthTag]);
 
-        services.TryAddSingleton(TimeProvider.System);
         services.AddScoped<IUnitOfWork, UnitOfWork>();
         services.AddScoped<IProductRepository, ProductRepository>();
```

- [ ] **Step 7: Run the Live tests to verify they pass**

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter-query "/*/TechStrap.Infrastructure.IntegrationTests.Live/*/*"
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: PASS (42 Live tests; the whole Infrastructure suite and the architecture rules, which still hold with the new `Live` namespace):

```text
  total: 42
  succeeded: 42
```

- [ ] **Step 8: Prove each pin with a recorded mutation**

Save `$T\specs\m2.py`:

```python
"""Mutations of Task 2 (presence store and the post-commit hook). Run from the repository root."""
INFRA = ["dotnet", "test", "--project", "tests/TechStrap.Infrastructure.IntegrationTests", "-c", "Release", "--filter-query"]
STORE_TESTS = INFRA + ["/*/*/InMemoryTicketPresenceStoreTests/*"]
HOOK_TESTS = INFRA + ["/*/*/TicketChangePublishingTests/*"]
STORE = "src/TechStrap.Infrastructure/Live/InMemoryTicketPresenceStore.cs"
PUBLISHING = "src/TechStrap.Infrastructure/Live/TicketChangePublishingInterceptor.cs"
CAPTURE = "src/TechStrap.Infrastructure/Live/TicketChangeCaptureInterceptor.cs"
PENDING = "src/TechStrap.Infrastructure/Live/PendingTicketChanges.cs"
PUBLISHER = "src/TechStrap.Infrastructure/Live/TicketChangePublisher.cs"
PERSIST = "src/TechStrap.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs"

MUTATIONS = [
    ("1 the composing lease is twice the TTL", STORE,
     [("TimeSpan.FromSeconds(TicketLiveLimits.ComposingTtlSeconds);", "TimeSpan.FromSeconds(TicketLiveLimits.ComposingTtlSeconds * 2);")], STORE_TESTS),
    ("2 a lease that ends exactly now still counts", STORE,
     [("entry.ComposingUntil > now", "entry.ComposingUntil >= now")], STORE_TESTS),
    ("3 a lease refresh is not a change", STORE,
     [("var leaseMoved = isComposing && entry.ComposingUntil != until;", "var leaseMoved = false;")], STORE_TESTS),
    ("4 every tab must be composing for the agent to be replying", STORE,
     [("group.Any(entry => entry.ComposingUntil > now)", "group.All(entry => entry.ComposingUntil > now)")], STORE_TESTS),
    ("5 viewers are ordered by name descending", STORE,
     [(".OrderBy(viewer => viewer.DisplayName, StringComparer.Ordinal)", ".OrderByDescending(viewer => viewer.DisplayName, StringComparer.Ordinal)")], STORE_TESTS),
    ("6 the same name is ordered by id descending", STORE,
     [(".ThenBy(viewer => viewer.AgentId)", ".ThenByDescending(viewer => viewer.AgentId)")], STORE_TESTS),
    ("7 only a moved lease counts as a change", STORE,
     [("leaseMoved || !before.Viewers.SequenceEqual(after.Viewers)", "leaseMoved")], STORE_TESTS),
    ("8 an empty ticket is never forgotten", STORE,
     [("&& entries.Remove(connectionId) && entries.Count == 0)", "&& entries.Remove(connectionId) && entries.Count < 0)")], STORE_TESTS),
    ("9 a connection with no tickets is never forgotten", STORE,
     [("&& tickets.Remove(ticketId) && tickets.Count == 0)", "&& tickets.Remove(ticketId) && tickets.Count < 0)")], STORE_TESTS),
    ("10 Join is not under the lock", STORE,
     [("        lock (_gate)\n        {\n            var before = Snapshot(ticketId);\n            Entries(ticketId)[connectionId]", "        lock (new object())\n        {\n            var before = Snapshot(ticketId);\n            Entries(ticketId)[connectionId]")], STORE_TESTS),
    ("11 a commit publishes nothing", PUBLISHING,
     [("        publisher.PublishAsync(eventData.Context);", "        Task.CompletedTask;")], HOOK_TESTS),
    ("12 a rollback keeps what was staged", PUBLISHING,
     [("TransactionRolledBackAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)\n    {\n        publisher.Discard(eventData.Context);\n", "TransactionRolledBackAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)\n    {\n")], HOOK_TESTS),
    ("13 SaveChanges publishes inside the transaction, before the commit", CAPTURE,
     [("CurrentTransaction: null } context)\n        {\n            await publisher.PublishAsync(context);", "CurrentTransaction: not null } context)\n        {\n            await publisher.PublishAsync(context);")], HOOK_TESTS),
    ("14 a save with no transaction never publishes", CAPTURE,
     [("            await publisher.PublishAsync(context);", "            await Task.CompletedTask;")], HOOK_TESTS),
    ("15 a failed save keeps what it captured", CAPTURE,
     [("        publisher.Discard(eventData.Context);\n        return Task.CompletedTask;\n    }\n\n    private void Capture", "        return Task.CompletedTask;\n    }\n\n    private void Capture")], HOOK_TESTS),
    ("16 a requester id is sent as the actor", CAPTURE,
     [("record.ActorType == Domain.Tickets.ActorType.Agent ? record.ActorId : null", "record.ActorId")], HOOK_TESTS),
    ("17 every tracked event is captured, not only the new ones", CAPTURE,
     [(".Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity).ToList();", ".Select(entry => entry.Entity).ToList();")], HOOK_TESTS),
    ("18 a deleted ticket asks for no resync", CAPTURE,
     [("resyncNeeded: deleted);", "resyncNeeded: false);")], HOOK_TESTS),
    ("19 a new ticket is announced as updated", PENDING,
     [("var kind = created is null ? TicketChangeKinds.Updated : TicketChangeKinds.Created;", "var kind = TicketChangeKinds.Updated;")], HOOK_TESTS),
    ("20 the oldest event names the change", PENDING,
     [("group.OrderBy(captured => captured.OccurredAt).Last()", "group.OrderBy(captured => captured.OccurredAt).First()")], HOOK_TESTS),
    ("21 taking the staged changes leaves them staged", PENDING,
     [("        _staged.Remove(context);\n        lock (staged)", "        lock (staged)")], HOOK_TESTS),
    ("22 discarding does nothing", PENDING,
     [("public void Discard(object context) => _staged.Remove(context);", "public void Discard(object context) { }")], HOOK_TESTS),
    ("23 a broadcaster that throws escapes", PUBLISHER,
     [("catch (Exception exception)", "catch (InvalidCastException exception)")], HOOK_TESTS),
    ("24 one failing ticket stops the rest", PUBLISHER,
     [('exception.GetType().Name);\n', 'exception.GetType().Name);\n                break;\n')], HOOK_TESTS),
    ("25 a hanging broadcaster is never cut off", PUBLISHER,
     [("PublishTimeout = TimeSpan.FromSeconds(5);", "PublishTimeout = TimeSpan.FromDays(1);")], HOOK_TESTS),
    ("26 the publishing interceptor is not registered", PERSIST,
     [("                    provider.GetRequiredService<TicketChangeCaptureInterceptor>(),\n                    provider.GetRequiredService<TicketChangePublishingInterceptor>()));", "                    provider.GetRequiredService<TicketChangeCaptureInterceptor>()));")], HOOK_TESTS),
]
```

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | the composing lease is twice the TTL | `InMemoryTicketPresenceStore.cs` | KILLED (2 failing) |
| 2 | a lease that ends exactly now still counts | `InMemoryTicketPresenceStore.cs` | KILLED (1 failing) |
| 3 | a lease refresh is not a change | `InMemoryTicketPresenceStore.cs` | KILLED (1 failing) |
| 4 | every tab must be composing for the agent to be replying | `InMemoryTicketPresenceStore.cs` | KILLED (1 failing) |
| 5 | viewers are ordered by name descending | `InMemoryTicketPresenceStore.cs` | KILLED (1 failing) |
| 6 | the same name is ordered by id descending | `InMemoryTicketPresenceStore.cs` | KILLED (1 failing) |
| 7 | only a moved lease counts as a change | `InMemoryTicketPresenceStore.cs` | KILLED (4 failing) |
| 8 | an empty ticket is never forgotten | `InMemoryTicketPresenceStore.cs` | KILLED (3 failing) |
| 9 | a connection with no tickets is never forgotten | `InMemoryTicketPresenceStore.cs` | KILLED (3 failing) |
| 10 | Join is not under the lock | `InMemoryTicketPresenceStore.cs` | KILLED (1 failing) |
| 11 | a commit publishes nothing | `TicketChangePublishingInterceptor.cs` | KILLED (12 failing) |
| 12 | a rollback keeps what was staged | `TicketChangePublishingInterceptor.cs` | KILLED (1 failing) |
| 13 | SaveChanges publishes inside the transaction, before the commit | `TicketChangeCaptureInterceptor.cs` | KILLED (4 failing) |
| 14 | a save with no transaction never publishes | `TicketChangeCaptureInterceptor.cs` | KILLED (2 failing) |
| 15 | a failed save keeps what it captured | `TicketChangeCaptureInterceptor.cs` | KILLED (1 failing) |
| 16 | a requester id is sent as the actor | `TicketChangeCaptureInterceptor.cs` | KILLED (1 failing) |
| 17 | every tracked event is captured, not only the new ones | `TicketChangeCaptureInterceptor.cs` | KILLED (1 failing) |
| 18 | a deleted ticket asks for no resync | `TicketChangeCaptureInterceptor.cs` | KILLED (1 failing) |
| 19 | a new ticket is announced as updated | `PendingTicketChanges.cs` | KILLED (3 failing) |
| 20 | the oldest event names the change | `PendingTicketChanges.cs` | KILLED (1 failing) |
| 21 | taking the staged changes leaves them staged | `PendingTicketChanges.cs` | KILLED (1 failing) |
| 22 | discarding does nothing | `PendingTicketChanges.cs` | KILLED (2 failing) |
| 23 | a broadcaster that throws escapes | `TicketChangePublisher.cs` | KILLED (3 failing) |
| 24 | one failing ticket stops the rest | `TicketChangePublisher.cs` | KILLED (1 failing) |
| 25 | a hanging broadcaster is never cut off | `TicketChangePublisher.cs` | KILLED (1 failing) |
| 26 | the publishing interceptor is not registered | `PersistenceServiceCollectionExtensions.cs` | KILLED (13 failing) |

Run them in three foreground batches (`1`..`10`, `11`..`20`, `21`..`26`, about 25 seconds each; mutation 25 waits out the 60-second test timeout) and record the results. Mutations 15 and 17 first survived (the failed-save test dropped an event for the same ticket as the next good one, so the leaked event merged into it; and no test had read events into the tracker before saving). The tests were strengthened (the failed save now targets another ticket; `Events_that_were_only_read_are_not_announced_again` was added) and both mutations then died. Mutation 21 (taking the staged changes leaves them staged) dies only because EF really does fire `TransactionCommitted` for the implicit transaction of a multi-statement SaveChanges, which is what the implicit-transaction test pins.

- [ ] **Step 9: Run the full suites and commit**

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: the whole Infrastructure suite (the 42 Live tests included) and the 295 architecture tests pass.

```bash
git add -A
git diff --cached --stat
git commit -m "feat: PHASE-10a presence store and post-commit ticket change hook (D-018, D-046)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

### Task 3: The agent hub, the SignalR broadcaster and the hub tests

**Review Focus pin:** 1 (only agents connect, by header token; a query token is refused; expiry closes the connection; `JoinTicket` cannot probe; no token in a log), 2 (a REST write reaches a client exactly once, after the commit) and 4 (two agents see each other's presence; names only to agents).

**Files:**

- Create: `src/TechStrap.Api/Live/TicketHub.cs`, `SignalRTicketChangeBroadcaster.cs`, `LiveHubExtensions.cs`
- Create: `src/TechStrap.Infrastructure/Live/LiveServiceCollectionExtensions.cs`
- Modify: `src/TechStrap.Api/Program.cs`, `src/TechStrap.Contracts/Live/LiveNames.cs`, `tests/TechStrap.Api.Tests/TechStrap.Api.Tests.csproj`
- Test (create): `tests/TechStrap.Api.Tests/Live/HubTestSupport.cs`, `TicketHubTests.cs`, `HubWebSocketTests.cs`, `HubPolicyCoverageTests.cs`

**Interfaces:**
- Consumes: Task 1's handler and DTOs, Task 2's store and the `Replace`-able broadcaster registration, `AuthorizationPolicies.Agent`, `ClaimsCurrentAgentClaims.FromPrincipal(ClaimsPrincipal, AgentAccessOptions)`, `ApiFactory`, `TestJwt`, `TicketTestData`, `ApiTestDatabase`, `TestPostgres`.
- Produces: `TicketHub` (`[Authorize(Policy = Agent)]`; `OnConnectedAsync` joins `queue`; `JoinTicket(Guid)` returns `TicketPresenceDto`; `LeaveTicket(Guid)`; `SetComposing(Guid, bool)`; `OnDisconnectedAsync` runs `LeaveAll`); `SignalRTicketChangeBroadcaster`; `AddTechStrapLiveHub()` (SignalR, the presence store, the broadcaster replaced) and `MapTechStrapLiveHub()` (route plus `RequireAuthorization(Agent)` plus `CloseOnAuthenticationExpiration`); `AddTechStrapPresence()` (Infrastructure); `TicketHubMessages.TicketNotFound`.

- [ ] **Step 1: Add the client package to the Api tests**

`tests/TechStrap.Api.Tests/TechStrap.Api.Tests.csproj` (modify): the version is already central (`Directory.Packages.props`), so only the reference is added.

```diff
--- a/tests/TechStrap.Api.Tests/TechStrap.Api.Tests.csproj
+++ b/tests/TechStrap.Api.Tests/TechStrap.Api.Tests.csproj
@@ -16,4 +16,5 @@
   <ItemGroup>
     <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
+    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />
     <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
     <PackageReference Include="Testcontainers.PostgreSql" />
```

- [ ] **Step 2: Write the failing tests**

`tests/TechStrap.Api.Tests/Live/HubTestSupport.cs` (new): builds a `HubConnection` on the in-memory server (long polling, the token in the `Authorization` header through `AccessTokenProvider`, or in the query to prove it is refused) and an `Inbox<T>` that reads what the hub pushes with a deadline.

```csharp
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Live;

namespace TechStrap.Api.Tests.Live;

/// <summary>Builds <see cref="HubConnection"/>s against an <see cref="ApiFactory"/>'s in-memory server and collects what the hub pushes.</summary>
internal static class HubTestSupport
{
    /// <summary>Generous: a wait is only ever for a message that should arrive, so a pass never waits this long.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    public static string AgentToken(string subject, string? name = null, DateTime? expires = null) =>
        TestJwt.Token(subject, [TestJwt.AgentGroup], email: $"{subject}@example.com", name: name ?? subject, expires: expires);

    /// <summary>
    /// A connection over long polling on the test server (the transport the in-memory server supports for both directions). The token goes in the Authorization
    /// header through <c>AccessTokenProvider</c>, the way the Admin sends it; <paramref name="queryToken"/> puts one in the URL instead, to prove it is refused.
    /// </summary>
    public static HubConnection Connect(ApiFactory factory, string? headerToken, string? queryToken = null)
    {
        var path = TicketHubRoutes.Path + (queryToken is null ? string.Empty : "?access_token=" + Uri.EscapeDataString(queryToken));
        return new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, path), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (headerToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(headerToken);
                }
            })
            .Build();
    }

    /// <summary>Everything the hub pushes under one method name, in order, readable with a deadline.</summary>
    public sealed class Inbox<T> : IDisposable
    {
        private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();
        private readonly IDisposable _subscription;

        public Inbox(HubConnection connection, string method) =>
            _subscription = connection.On<T>(method, message => _channel.Writer.TryWrite(message));

        public async Task<T> NextAsync()
        {
            using var timeout = new CancellationTokenSource(Patience);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
            return await _channel.Reader.ReadAsync(linked.Token);
        }

        /// <summary>What has already arrived, without waiting.</summary>
        public IReadOnlyList<T> Pending()
        {
            var items = new List<T>();
            while (_channel.Reader.TryRead(out var item))
            {
                items.Add(item);
            }

            return items;
        }

        public void Dispose() => _subscription.Dispose();
    }
}
```

`tests/TechStrap.Api.Tests/Live/TicketHubTests.cs` (new): 401, 403, a deactivated agent, a query token (with `HubConnection` and with a raw negotiate), expiry, the queue, presence between two agents, leaving, an unknown ticket (with a barrier proving no group was joined), the name, a REST status change reaching a client once and a rejected one never, and the log scan at Verbose.

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Live;

/// <summary>
/// The agent hub through a real <see cref="HubConnection"/> on the in-memory server and a migrated database: who may connect (Review Focus 1), what a connection
/// receives, presence between two agents, and that a REST write is announced once, after its commit (Review Focus 2).
/// </summary>
public sealed class TicketHubTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, TicketSeed Seed)> StartAsync(Dictionary<string, string?>? extra = null)
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        var factory = new ApiFactory(settings: settings);
        return (factory, database, await TicketTestData.SeedAsync(factory, Ct));
    }

    private static TicketChange Change(Guid? ticketId = null, string number = "ORB-1") =>
        new(Guid.NewGuid(), ticketId ?? Guid.NewGuid(), number, Guid.NewGuid(), TicketEventTypes.StatusChanged, null, DateTimeOffset.UtcNow, TicketChangeKinds.Updated);

    private static async Task<HttpRequestException> StartFailureAsync(HubConnection connection)
    {
        await using (connection)
        {
            return await Should.ThrowAsync<HttpRequestException>(() => connection.StartAsync(Ct));
        }
    }

    // ---- who may connect ---------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_connection_without_a_token_is_401()
    {
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;

        var failure = await StartFailureAsync(HubTestSupport.Connect(factory, headerToken: null));

        failure.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_valid_token_for_someone_who_is_not_an_agent_is_403()
    {
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        var outsider = TestJwt.Token("outsider", ["some-other-group"], email: "outsider@example.com", name: "Outsider");

        var failure = await StartFailureAsync(HubTestSupport.Connect(factory, outsider));

        failure.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_deactivated_agent_is_refused_at_the_handshake()
    {
        var (factory, database, _) = await StartAsync();
        await using var _f = factory;
        await database.ExecuteAsync("UPDATE agents SET is_active = false WHERE oidc_subject = 'kim'");

        var failure = await StartFailureAsync(HubTestSupport.Connect(factory, HubTestSupport.AgentToken("kim", "Kim")));

        failure.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_in_the_query_string_is_refused_even_when_it_is_a_valid_agent_token()
    {
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        var token = HubTestSupport.AgentToken("sam", "Sam");

        var failure = await StartFailureAsync(HubTestSupport.Connect(factory, headerToken: null, queryToken: token));

        failure.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_raw_negotiate_with_the_token_only_in_the_query_is_401_and_with_the_header_is_200()
    {
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        var token = HubTestSupport.AgentToken("sam", "Sam");
        using var client = factory.CreateClient();

        using var viaQuery = await client.PostAsync($"{TicketHubRoutes.Path}/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(token)}", null, Ct);
        using var viaHeader = await client.Bearer(token).PostAsync($"{TicketHubRoutes.Path}/negotiate?negotiateVersion=1", null, Ct);

        viaQuery.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        viaHeader.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(Timeout = 120000)]
    public async Task A_token_that_is_about_to_expire_closes_the_connection_when_it_does()
    {
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam", expires: DateTime.UtcNow.AddSeconds(4)));
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        var testToken = TestContext.Current.CancellationToken;
        await connection.StartAsync(testToken);
        connection.State.ShouldBe(HubConnectionState.Connected);

        // The server closes the connection at the token's expiry (CloseOnAuthenticationExpiration); the wait is only a ceiling, not a measurement.
        await closed.Task.WaitAsync(HubTestSupport.Patience, testToken);

        connection.State.ShouldBe(HubConnectionState.Disconnected);
    }

    // ---- what a connection receives ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_agent_connection_is_in_the_queue_and_receives_ticket_changes_with_ids_only()
    {
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        await using var sam = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        await using var kim = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("kim", "Kim"));
        using var samInbox = new HubTestSupport.Inbox<TicketChangedDto>(sam, TicketHubMethods.TicketChanged);
        using var kimInbox = new HubTestSupport.Inbox<TicketChangedDto>(kim, TicketHubMethods.TicketChanged);
        await sam.StartAsync(Ct);
        await kim.StartAsync(Ct);
        var change = Change(number: "ORB-7");

        await factory.Services.GetRequiredService<ITicketChangeBroadcaster>().PublishAsync(change, Ct);

        (await samInbox.NextAsync()).ShouldBe(change.ToDto());
        (await kimInbox.NextAsync()).ShouldBe(change.ToDto());
    }

    [Fact]
    public async Task The_hubs_broadcaster_is_the_one_the_rest_of_the_host_resolves()
    {
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;

        factory.Services.GetRequiredService<ITicketChangeBroadcaster>().GetType().Name.ShouldBe("SignalRTicketChangeBroadcaster");
    }

    // ---- presence ---------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Two_agents_on_one_ticket_see_each_other_come_replying_and_go()
    {
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        var ticketId = seed.Tickets[0].Id;
        await using var sam = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        await using var kim = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("kim", "Kim"));
        using var samSees = new HubTestSupport.Inbox<TicketPresenceDto>(sam, TicketHubMethods.PresenceChanged);
        await sam.StartAsync(Ct);
        await kim.StartAsync(Ct);

        var first = await sam.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, ticketId, Ct);
        first.TicketId.ShouldBe(ticketId);
        first.Viewers.Select(viewer => (viewer.DisplayName, viewer.State)).ShouldBe([("Sam", TicketPresenceStates.Viewing)]);

        var second = await kim.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, ticketId, Ct);
        second.Viewers.Select(viewer => viewer.DisplayName).ShouldBe(["Kim", "Sam"]);
        (await samSees.NextAsync()).Viewers.Select(viewer => viewer.DisplayName).ShouldBe(["Kim", "Sam"]);

        await kim.InvokeAsync(TicketHubMethods.SetComposing, ticketId, true, Ct);
        var composing = await samSees.NextAsync();
        composing.Viewers.Single(viewer => viewer.DisplayName == "Kim").State.ShouldBe(TicketPresenceStates.Composing);

        await kim.StopAsync(Ct);
        var gone = await samSees.NextAsync();
        gone.Viewers.Select(viewer => viewer.DisplayName).ShouldBe(["Sam"]);
    }

    [Fact]
    public async Task Leaving_a_ticket_is_announced_and_the_leaver_stops_receiving_its_presence()
    {
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        var ticketId = seed.Tickets[0].Id;
        await using var sam = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        await using var kim = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("kim", "Kim"));
        using var samSees = new HubTestSupport.Inbox<TicketPresenceDto>(sam, TicketHubMethods.PresenceChanged);
        using var kimSees = new HubTestSupport.Inbox<TicketPresenceDto>(kim, TicketHubMethods.PresenceChanged);
        await sam.StartAsync(Ct);
        await kim.StartAsync(Ct);
        await sam.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, ticketId, Ct);
        await kim.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, ticketId, Ct);
        (await samSees.NextAsync()).Viewers.Count.ShouldBe(2);

        await kim.InvokeAsync(TicketHubMethods.LeaveTicket, ticketId, Ct);
        (await samSees.NextAsync()).Viewers.Select(viewer => viewer.DisplayName).ShouldBe(["Sam"]);

        // Kim is out of the ticket's group now: a later presence push for the ticket reaches Sam only.
        kimSees.Pending();
        await factory.Services.GetRequiredService<ITicketChangeBroadcaster>().PublishPresenceAsync(new TicketPresence(ticketId, []), Ct);
        (await samSees.NextAsync()).Viewers.ShouldBeEmpty();
        kimSees.Pending().ShouldBeEmpty();
    }

    [Fact]
    public async Task Joining_an_unknown_ticket_is_a_hub_exception_with_the_fixed_message_and_joins_no_group()
    {
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        var unknown = Guid.NewGuid();
        await using var sam = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        using var presence = new HubTestSupport.Inbox<TicketPresenceDto>(sam, TicketHubMethods.PresenceChanged);
        using var queue = new HubTestSupport.Inbox<TicketChangedDto>(sam, TicketHubMethods.TicketChanged);
        await sam.StartAsync(Ct);

        var refusal = await Should.ThrowAsync<HubException>(() => sam.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, unknown, Ct));

        refusal.Message.ShouldEndWith(TicketHubMessages.TicketNotFound);
        refusal.Message.ShouldNotContain(unknown.ToString());

        // Not in the group: a presence push for that id never arrives. The queue message sent after it is the barrier (a connection gets its messages in the order they were sent).
        var broadcaster = factory.Services.GetRequiredService<ITicketChangeBroadcaster>();
        await broadcaster.PublishPresenceAsync(new TicketPresence(unknown, []), Ct);
        var barrier = Change();
        await broadcaster.PublishAsync(barrier, Ct);
        (await queue.NextAsync()).EventId.ShouldBe(barrier.EventId);
        presence.Pending().ShouldBeEmpty();
    }

    [Fact]
    public async Task Composing_on_a_ticket_that_was_never_joined_is_refused()
    {
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        await using var sam = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        await sam.StartAsync(Ct);

        var refusal = await Should.ThrowAsync<HubException>(() => sam.InvokeAsync(TicketHubMethods.SetComposing, seed.Tickets[0].Id, true, Ct));

        refusal.Message.ShouldEndWith("Open the ticket first.");
    }

    [Fact]
    public async Task The_name_other_agents_see_is_the_internal_name_never_the_public_display_name()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        await database.ExecuteAsync("UPDATE agents SET public_display_name = 'Sam from Support' WHERE oidc_subject = 'sam'");
        await using var sam = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        await sam.StartAsync(Ct);

        var presence = await sam.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, seed.Tickets[0].Id, Ct);

        presence.Viewers.Single().DisplayName.ShouldBe("Sam");
    }

    // ---- a REST write is announced once, after its commit -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_rest_status_change_reaches_the_queue_exactly_once_and_a_rejected_one_never_does()
    {
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        var ticket = seed.Tickets[1];
        await using var kim = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("kim", "Kim"));
        using var queue = new HubTestSupport.Inbox<TicketChangedDto>(kim, TicketHubMethods.TicketChanged);
        await kim.StartAsync(Ct);
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var version = await TicketTestData.VersionAsync(sam, ticket.Id);

        using var stale = await sam.PutAsJsonAsync($"/api/tickets/{ticket.Id}/status", new ChangeTicketStatusRequest("Open", version + 100), Ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var accepted = await sam.PutAsJsonAsync($"/api/tickets/{ticket.Id}/status", new ChangeTicketStatusRequest("Open", version), Ct);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);

        var change = await queue.NextAsync();
        change.TicketId.ShouldBe(ticket.Id);
        change.TicketNumber.ShouldBe(ticket.Number.ToString());
        change.ProductId.ShouldBe(ticket.ProductId);
        change.EventType.ShouldBe(TicketEventTypes.StatusChanged);
        change.Kind.ShouldBe(TicketChangeKinds.Updated);
        change.ActorAgentId.ShouldBe(seed.Sam.Id);

        // Anything else the same two requests produced would arrive before this barrier.
        var barrier = Change();
        await factory.Services.GetRequiredService<ITicketChangeBroadcaster>().PublishAsync(barrier, Ct);
        (await queue.NextAsync()).EventId.ShouldBe(barrier.EventId);
        queue.Pending().ShouldBeEmpty();
    }

    // ---- logs -------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task No_token_reaches_a_log_event_at_any_level_whether_it_came_in_a_header_or_a_refused_query()
    {
        var (factory, _, seed) = await StartAsync(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel:Default"] = "Verbose",
            ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
            ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose",
            ["Serilog:MinimumLevel:Override:System"] = "Verbose",
        });
        await using var _f = factory;
        var headerToken = HubTestSupport.AgentToken("sam", "Sam");
        var queryToken = HubTestSupport.AgentToken("kim", "Kim");
        await using (var good = HubTestSupport.Connect(factory, headerToken))
        {
            await good.StartAsync(Ct);
            await good.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, seed.Tickets[0].Id, Ct);
        }

        await StartFailureAsync(HubTestSupport.Connect(factory, headerToken: null, queryToken: queryToken));

        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect, or this test only scanned Information and above");
        factory.LogSink.Events.ShouldContain(e => e.RenderMessage().Contains("/hubs/tickets"), "the hub requests must have been logged, or this test proves nothing");
        foreach (var logEvent in factory.LogSink.Events)
        {
            var text = string.Join('\n', [logEvent.RenderMessage(), logEvent.Exception?.ToString() ?? string.Empty, .. logEvent.Properties.Values.Select(value => value.ToString())]);
            text.ShouldNotContain(headerToken);
            text.ShouldNotContain(queryToken);
            text.ShouldNotContain("eyJ");
        }
    }
}
```

`tests/TechStrap.Api.Tests/Live/HubWebSocketTests.cs` (new): the one test on a real loopback Kestrel port, over real WebSockets with the header token only.

```csharp
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Live;

/// <summary>
/// The transport the Admin really uses. The in-memory test server cannot carry a WebSocket handshake header, so this one test starts the host on a real loopback
/// Kestrel port and connects with the real client over WebSockets and only the Authorization header: it proves the header token is enough on that transport, which is
/// why the hub needs no query-token support (D-046). The listener stops with the factory.
/// </summary>
public sealed class HubWebSocketTests(TestPostgres postgres)
{
    [Fact(Timeout = 120000)]
    public async Task A_header_token_is_enough_over_real_web_sockets_and_a_change_arrives()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" };
        await using var factory = new ApiFactory(settings: settings);
        factory.UseKestrel(0);
        factory.StartServer();
        await TicketTestData.SeedAsync(factory, ct);
        var address = new Uri(factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());

        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(address, TicketHubRoutes.Path), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.AccessTokenProvider = () => Task.FromResult<string?>(HubTestSupport.AgentToken("sam", "Sam"));
            })
            .Build();
        using var inbox = new HubTestSupport.Inbox<TicketChangedDto>(connection, TicketHubMethods.TicketChanged);
        await connection.StartAsync(ct);
        var change = new TicketChange(Guid.NewGuid(), Guid.NewGuid(), "ORB-1", Guid.NewGuid(), TicketEventTypes.StatusChanged, null, DateTimeOffset.UtcNow, TicketChangeKinds.Updated);

        await factory.Services.GetRequiredService<ITicketChangeBroadcaster>().PublishAsync(change, ct);

        (await inbox.NextAsync()).EventId.ShouldBe(change.EventId);

        // The same handshake with no token, and with the token only in the query (the browser habit), is refused.
        foreach (var url in new[] { TicketHubRoutes.Path, TicketHubRoutes.Path + "?access_token=" + Uri.EscapeDataString(HubTestSupport.AgentToken("sam", "Sam")) })
        {
            await using var refused = new HubConnectionBuilder()
                .WithUrl(new Uri(address, url), options =>
                {
                    options.Transports = HttpTransportType.WebSockets;
                    options.SkipNegotiation = true;
                })
                .Build();
            await Should.ThrowAsync<Exception>(() => refused.StartAsync(ct));
        }
    }
}
```

`tests/TechStrap.Api.Tests/Live/HubPolicyCoverageTests.cs` (new): `RoutePolicyCoverageTests` only scans routes that start with `api/`, so the hub gets its own.

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Api.Live;
using TechStrap.Api.Security;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;

namespace TechStrap.Api.Tests.Live;

/// <summary>
/// <c>RoutePolicyCoverageTests</c> only looks at routes that start with <c>api/</c>, so the hub gets its own: every hub endpoint names exactly the Agent policy and is never
/// anonymous, every hub class carries the policy too, and a hub is only a thin adapter over the presence handler.
/// </summary>
public sealed class HubPolicyCoverageTests
{
    [Fact]
    public void Every_hub_endpoint_declares_exactly_the_agent_policy_and_is_not_anonymous()
    {
        using var factory = new ApiFactory();
        var hubs = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith("hubs/", StringComparison.Ordinal) == true)
            .ToList();

        hubs.ShouldNotBeEmpty();
        hubs.ShouldContain(endpoint => endpoint.RoutePattern.RawText!.TrimStart('/').StartsWith("hubs/tickets", StringComparison.Ordinal));
        foreach (var endpoint in hubs)
        {
            var name = endpoint.RoutePattern.RawText;
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).Distinct().ShouldBe([AuthorizationPolicies.Agent], name);
            endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull(name);
        }
    }

    [Fact]
    public void The_hub_path_constant_is_the_mapped_path()
    {
        using var factory = new ApiFactory();

        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .ShouldContain(endpoint => endpoint.RoutePattern.RawText!.TrimStart('/') == TicketHubRoutes.Path.TrimStart('/'));
    }

    [Fact]
    public void Every_hub_class_in_the_api_requires_the_agent_policy_itself()
    {
        var hubs = typeof(TicketHub).Assembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false } && typeof(Hub).IsAssignableFrom(type)).ToList();

        hubs.ShouldContain(typeof(TicketHub));
        foreach (var hub in hubs)
        {
            hub.GetCustomAttributes<AuthorizeAttribute>().Select(attribute => attribute.Policy).ShouldContain(AuthorizationPolicies.Agent, hub.Name);
        }
    }

    [Fact]
    public void The_hub_methods_are_the_three_contract_names_and_nothing_else()
    {
        var methods = typeof(TicketHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(method => method.Name).ToList();

        methods.ShouldBe(
            [TicketHubMethods.JoinTicket, TicketHubMethods.LeaveTicket, TicketHubMethods.SetComposing, nameof(Hub.OnConnectedAsync), nameof(Hub.OnDisconnectedAsync)],
            ignoreOrder: true);
    }

    [Fact]
    public void The_hub_depends_only_on_the_presence_handler_and_the_agent_options()
    {
        var parameters = typeof(TicketHub).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToList();

        parameters.ShouldBe([typeof(IUpdateTicketPresenceHandler), typeof(IOptions<TechStrap.Api.Options.AgentAccessOptions>)], ignoreOrder: true);
    }

    [Fact]
    public void The_handlers_the_hub_and_the_listener_need_are_registered_by_the_api()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IUpdateTicketPresenceHandler>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IRelayTicketChangeHandler>().ShouldNotBeNull();
    }

    [Fact]
    public void Presence_is_one_store_for_the_whole_process()
    {
        using var factory = new ApiFactory();

        var first = factory.Services.GetRequiredService<ITicketPresenceStore>();
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITicketPresenceStore>().ShouldBeSameAs(first);
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

```bash
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/TechStrap.Api.Tests.Live/*/*"
```

Expected: FAIL to compile, because the hub does not exist:

```text
tests/TechStrap.Api.Tests/Live/HubPolicyCoverageTests.cs(7,21): error CS0234: The type or namespace name 'Live' does not exist in the namespace 'TechStrap.Api' (are you missing an assembly reference?)
Build failed with exit code: 1.
```

- [ ] **Step 4: Write the hub**

`src/TechStrap.Contracts/Live/LiveNames.cs` (modify): the one message a hub call sends for an unknown ticket.

```diff
--- a/src/TechStrap.Contracts/Live/LiveNames.cs
+++ b/src/TechStrap.Contracts/Live/LiveNames.cs
@@ -33,4 +33,10 @@ public static class TicketHubGroups
 }
 
+/// <summary>The one message a hub call sends to a client when it names a ticket that does not exist (D-046). Every other refusal sends its own fixed handler text.</summary>
+public static class TicketHubMessages
+{
+    public const string TicketNotFound = "Ticket not found";
+}
+
 /// <summary>Wire names for the kind of a ticket change. Contracts carries no enums (naming rule). <c>Resync</c> names no ticket: the client reloads everything.</summary>
 public static class TicketChangeKinds
```

`src/TechStrap.Api/Live/TicketHub.cs` (new)

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;

namespace TechStrap.Api.Live;

/// <summary>
/// The agent hub at <see cref="TicketHubRoutes.Path"/> (D-007, D-018, D-046). Agents only: the Agent policy is on the route and repeated on the class, the token travels in the
/// <c>Authorization</c> header (a query token is not read), and the connection closes when the token expires. Every connection joins the <c>queue</c> group, which carries
/// <c>TicketChanged</c>; a ticket group carries presence only and is joined only for a ticket that exists. The methods are thin: they build a request from the hub's own
/// caller (<c>Context.User</c>, <c>Context.ConnectionId</c>) and let <see cref="IUpdateTicketPresenceHandler"/> decide, so a client can never speak for another agent
/// and <c>IHttpContextAccessor</c> is never needed inside a hub.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class TicketHub(IUpdateTicketPresenceHandler presence, IOptions<AgentAccessOptions> access) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, TicketHubGroups.Queue, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    /// <summary>Opens a ticket: returns who has it open now (the caller included); everyone else on the ticket is told when that changed.</summary>
    public async Task<TicketPresenceDto> JoinTicket(Guid ticketId)
    {
        var result = await presence.HandleAsync(Request(TicketPresenceActions.Join, ticketId), Context.ConnectionAborted);
        if (result.IsFailure)
        {
            throw Refusal(result.Errors[0]);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TicketHubGroups.Ticket(ticketId), Context.ConnectionAborted);
        return result.Value.ToDto();
    }

    public async Task LeaveTicket(Guid ticketId)
    {
        var result = await presence.HandleAsync(Request(TicketPresenceActions.Leave, ticketId), Context.ConnectionAborted);
        if (result.IsFailure)
        {
            throw Refusal(result.Errors[0]);
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, TicketHubGroups.Ticket(ticketId), Context.ConnectionAborted);
    }

    public async Task SetComposing(Guid ticketId, bool isComposing)
    {
        var result = await presence.HandleAsync(Request(TicketPresenceActions.SetComposing, ticketId, isComposing), Context.ConnectionAborted);
        if (result.IsFailure)
        {
            throw Refusal(result.Errors[0]);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // The connection is already gone, so its abort token is spent: cleaning up must not depend on it. SignalR removes the connection from its groups itself.
        await presence.HandleAsync(Request(TicketPresenceActions.LeaveAll, null), CancellationToken.None);
        await base.OnDisconnectedAsync(exception);
    }

    private UpdateTicketPresenceRequest Request(string action, Guid? ticketId, bool isComposing = false) =>
        new(action, ClaimsCurrentAgentClaims.FromPrincipal(Context.User!, access.Value)?.Subject ?? string.Empty, Context.ConnectionId, ticketId, isComposing);

    /// <summary>Only the fixed texts of the handler's outcomes reach the client; an unknown ticket is the one the spec names.</summary>
    private static HubException Refusal(ResultError error) =>
        new(error.Kind == ResultErrorKind.NotFound ? TicketHubMessages.TicketNotFound : error.Message);
}
```

`src/TechStrap.Api/Live/SignalRTicketChangeBroadcaster.cs` (new)

```csharp
using Microsoft.AspNetCore.SignalR;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;

namespace TechStrap.Api.Live;

/// <summary>
/// The Api's <see cref="ITicketChangeBroadcaster"/>: pushes to the hub's groups (D-018). A change goes to the <c>queue</c> group, which every connection is in, and the client
/// filters by ticket id (D-046); presence goes to the ticket's own group. The payloads are the Contracts DTOs, which hold ids and names only.
/// </summary>
public sealed class SignalRTicketChangeBroadcaster(IHubContext<TicketHub> hub) : ITicketChangeBroadcaster
{
    public Task PublishAsync(TicketChange change, CancellationToken cancellationToken) =>
        hub.Clients.Group(TicketHubGroups.Queue).SendAsync(TicketHubMethods.TicketChanged, change.ToDto(), cancellationToken);

    public Task PublishPresenceAsync(TicketPresence presence, CancellationToken cancellationToken) =>
        hub.Clients.Group(TicketHubGroups.Ticket(presence.TicketId)).SendAsync(TicketHubMethods.PresenceChanged, presence.ToDto(), cancellationToken);
}
```

`src/TechStrap.Api/Live/LiveHubExtensions.cs` (new)

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Api.Security;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Infrastructure.Live;

namespace TechStrap.Api.Live;

public static class LiveHubExtensions
{
    /// <summary>
    /// SignalR, the in-memory presence store (D-007: one Api instance) and the hub's <see cref="ITicketChangeBroadcaster"/>. Call it after <c>AddTechStrapPersistence</c>:
    /// that registered the null broadcaster, and <c>Replace</c> is what makes the hub's win.
    /// </summary>
    public static IServiceCollection AddTechStrapLiveHub(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddTechStrapPresence();
        services.Replace(ServiceDescriptor.Singleton<ITicketChangeBroadcaster, SignalRTicketChangeBroadcaster>());
        return services;
    }

    /// <summary>
    /// Maps the hub at <see cref="TicketHubRoutes.Path"/> for agents only, closing a connection when its token expires (the JWT is validated at the handshake only).
    /// The explicit policy is deliberate although the fallback policy would also require a sign-in.
    /// </summary>
    public static IEndpointConventionBuilder MapTechStrapLiveHub(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHub<TicketHub>(TicketHubRoutes.Path, options => options.CloseOnAuthenticationExpiration = true)
            .RequireAuthorization(AuthorizationPolicies.Agent);
}
```

`src/TechStrap.Infrastructure/Live/LiveServiceCollectionExtensions.cs` (new): the presence store, one for the process.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Application.Live;

namespace TechStrap.Infrastructure.Live;

public static class LiveServiceCollectionExtensions
{
    /// <summary>
    /// The in-memory presence store, one for the process (D-007: a single Api instance). The Api registers it; the Worker never does, because presence is not
    /// the Worker's business. Needs a <see cref="TimeProvider"/>, which <c>AddTechStrapPersistence</c> registers.
    /// </summary>
    public static IServiceCollection AddTechStrapPresence(this IServiceCollection services)
    {
        services.TryAddSingleton<ITicketPresenceStore, InMemoryTicketPresenceStore>();
        return services;
    }
}
```

`src/TechStrap.Api/Program.cs` (modify): after `AddApplicationHandlers`, so the hub's broadcaster replaces the null one, and the map after `MapControllers`.

```diff
--- a/src/TechStrap.Api/Program.cs
+++ b/src/TechStrap.Api/Program.cs
@@ -6,4 +6,5 @@ using SyntaxCircus.AspNetCore.Serilog;
 using SyntaxCircus.DotEnv;
 using SyntaxCircus.Observability;
+using TechStrap.Api.Live;
 using TechStrap.Api.Options;
 using TechStrap.Api.Security;
@@ -104,4 +105,6 @@ builder.Services.AddTechStrapTicketOperations(builder.Configuration);
 builder.Services.AddResultProblemDetails();
 builder.Services.AddApplicationHandlers();
+// The agent hub and its broadcaster (D-018). After AddTechStrapPersistence, whose null broadcaster the hub's replaces.
+builder.Services.AddTechStrapLiveHub();
 
 // KB images (D-044): the Api's own public address builds each image URL. Required outside Development; blank there means the request origin.
@@ -166,4 +169,5 @@ app.MapOpenApi().AllowAnonymous().RequireRateLimiting(PublicRateLimitOptions.Pol
 app.MapControllers();
 app.MapKbImages();
+app.MapTechStrapLiveHub();
 
 app.Run();
```

- [ ] **Step 5: Run the Live tests and the whole Api suite to verify they pass**

```bash
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/TechStrap.Api.Tests.Live/*/*"
dotnet test --project tests/TechStrap.Api.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: PASS (23 Live tests; 955 Api tests in all; the architecture rules):

```text
  total: 23
  succeeded: 23
```

- [ ] **Step 6: Prove each pin with a recorded mutation**

Save `$T\specs\m3.py`:

```python
"""Mutations of Task 3 (the hub). Run from the repository root."""
API = ["dotnet", "test", "--project", "tests/TechStrap.Api.Tests", "-c", "Release", "--filter-query"]
LIVE = API + ["/*/TechStrap.Api.Tests.Live/*/*"]
HUB = "src/TechStrap.Api/Live/TicketHub.cs"
EXT = "src/TechStrap.Api/Live/LiveHubExtensions.cs"
BROAD = "src/TechStrap.Api/Live/SignalRTicketChangeBroadcaster.cs"

MUTATIONS = [
    ("1 the hub route is anonymous", EXT, [(".RequireAuthorization(AuthorizationPolicies.Agent);", ".AllowAnonymous();")], LIVE),
    ("2 the hub class drops its policy", HUB, [("[Authorize(Policy = AuthorizationPolicies.Agent)]\n", "")], LIVE),
    ("3 a connection outlives its token", EXT, [("options.CloseOnAuthenticationExpiration = true", "options.CloseOnAuthenticationExpiration = false")], LIVE),
    ("4 a connection does not join the queue", HUB, [("        await Groups.AddToGroupAsync(Context.ConnectionId, TicketHubGroups.Queue, Context.ConnectionAborted);\n", "")], LIVE),
    ("5 a refused join is ignored", HUB, [("        var result = await presence.HandleAsync(Request(TicketPresenceActions.Join, ticketId), Context.ConnectionAborted);\n        if (result.IsFailure)", "        var result = await presence.HandleAsync(Request(TicketPresenceActions.Join, ticketId), Context.ConnectionAborted);\n        if (result.IsFailure && ticketId == Guid.Empty)")], LIVE),
    ("6 joining a ticket joins the queue group instead", HUB, [("TicketHubGroups.Ticket(ticketId), Context.ConnectionAborted);\n        return result.Value.ToDto();", "TicketHubGroups.Queue, Context.ConnectionAborted);\n        return result.Value.ToDto();")], LIVE),
    ("7 an unknown ticket says the handler's text", HUB, [("error.Kind == ResultErrorKind.NotFound ? TicketHubMessages.TicketNotFound : error.Message", "error.Message")], LIVE),
    ("8 a disconnect leaves nothing", HUB, [("Request(TicketPresenceActions.LeaveAll, null)", "Request(TicketPresenceActions.Leave, null)")], LIVE),
    ("9 every caller is Sam", HUB, [("?.Subject ?? string.Empty, Context.ConnectionId", "?.Subject.Replace(\"kim\", \"sam\") ?? string.Empty, Context.ConnectionId")], LIVE),
    ("10 presence goes to the whole queue", BROAD, [("Group(TicketHubGroups.Ticket(presence.TicketId))", "Group(TicketHubGroups.Queue)")], LIVE),
    ("11 changes go to the ticket's group only", BROAD, [("hub.Clients.Group(TicketHubGroups.Queue).SendAsync(TicketHubMethods.TicketChanged", "hub.Clients.Group(TicketHubGroups.Ticket(change.TicketId)).SendAsync(TicketHubMethods.TicketChanged")], LIVE),
    ("12 the null broadcaster is not replaced", EXT, [("services.Replace(ServiceDescriptor.Singleton<ITicketChangeBroadcaster, SignalRTicketChangeBroadcaster>());", "services.TryAddSingleton<ITicketChangeBroadcaster, SignalRTicketChangeBroadcaster>();")], LIVE),
    ("13 the hub is not mapped", "src/TechStrap.Api/Program.cs", [("app.MapTechStrapLiveHub();\n", "")], LIVE),
    ("14 the query token is read", "src/TechStrap.Api/Security/AgentAuthenticationSetup.cs",
     [("            options.MapInboundClaims = false;\n", "            options.MapInboundClaims = false;\n            options.Events ??= new JwtBearerEvents();\n            options.Events.OnMessageReceived = context =>\n            {\n                context.Token = context.Request.Query[\"access_token\"];\n                return Task.CompletedTask;\n            };\n")], LIVE),
]
```

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | the hub route is anonymous | `LiveHubExtensions.cs` | KILLED (8 failing) |
| 2 | the hub class drops its policy | `TicketHub.cs` | KILLED (1 failing) |
| 3 | a connection outlives its token | `LiveHubExtensions.cs` | KILLED (1 failing) |
| 4 | a connection does not join the queue | `TicketHub.cs` | KILLED (4 failing) |
| 5 | a refused join is ignored | `TicketHub.cs` | KILLED (1 failing) |
| 6 | joining a ticket joins the queue group instead | `TicketHub.cs` | KILLED (2 failing) |
| 7 | an unknown ticket says the handler's text | `TicketHub.cs` | KILLED (1 failing) |
| 8 | a disconnect leaves nothing | `TicketHub.cs` | KILLED (1 failing) |
| 9 | every caller is Sam | `TicketHub.cs` | KILLED (2 failing) |
| 10 | presence goes to the whole queue | `SignalRTicketChangeBroadcaster.cs` | KILLED (3 failing) |
| 11 | changes go to the ticket's group only | `SignalRTicketChangeBroadcaster.cs` | KILLED (4 failing) |
| 12 | the null broadcaster is not replaced | `LiveHubExtensions.cs` | KILLED (7 failing) |
| 13 | the hub is not mapped | `Program.cs` | KILLED (15 failing) |
| 14 | the query token is read | `AgentAuthenticationSetup.cs` | KILLED (4 failing) |

Run them (two foreground batches, `1`..`7` and `8`..`14`, about 40 seconds each; mutation 3 waits out the 30-second ceiling) and record the results. Every mutation is killed. Mutations 1 and 2 are killed together by the coverage test because the endpoint's metadata is the union of the route's and the class's policies, so neither alone would show; each is killed by the part of the test that looks for the other (`AllowAnonymous` on the route; the class attribute directly).

- [ ] **Step 7: Run the full suites and commit**

```bash
dotnet test --project tests/TechStrap.Api.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: 955 and 295 tests pass.

```bash
git add -A
git diff --cached --stat
git commit -m "feat: PHASE-10a agent hub, SignalR broadcaster and hub tests (D-046)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

### Task 4: The Worker's NOTIFY and the Api's listener

**Review Focus pin:** 3 (reconnect with backoff and one `Resync`; a malformed payload dropped without breaking the loop; a clean shutdown; no echo) and 2 (a worker-style commit notifies once per ticket and a rollback never does).

**Files:**

- Create: `src/TechStrap.Infrastructure/Live/TicketChangeNotify.cs`, `PgNotifyTicketChangeBroadcaster.cs`, `TicketChangeListener.cs`
- Modify: `src/TechStrap.Infrastructure/Live/LiveServiceCollectionExtensions.cs`, `src/TechStrap.Api/Live/LiveHubExtensions.cs`, `src/TechStrap.Worker/Program.cs`, `src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj`
- Test (create): `tests/TechStrap.Infrastructure.IntegrationTests/Live/NotifyTestSupport.cs`, `TicketChangeListenerTests.cs`, `PgNotifyBroadcasterTests.cs`, `tests/TechStrap.Api.Tests/Live/NotifyRelayHostTests.cs`

**Interfaces:**
- Consumes: Task 1's `IRelayTicketChangeHandler`, `TicketChange.Resync`, `TicketLiveLimits.MaxChangePayloadBytes`; Task 2's `RecordingBroadcaster`, `PersistenceTestHost`, `TicketScenario`; Task 3's hub support; `DatabaseConnectionOptions`; `AddTechStrapAutoClose` and the auto-close handler.
- Produces: `TicketChangeNotify` (`Channel = "techstrap_ticket_changes"`, `ListenerApplicationName`, `KeepAliveSeconds = 30`, `ReconnectInitialDelay` 1 s, `ReconnectMaxDelay` 30 s); `PgNotifyTicketChangeBroadcaster` (internal; `SELECT pg_notify(@channel, @payload)` on a data source of its own, refuses a payload over the cap, ignores presence); `TicketChangeListener` (internal `BackgroundService`; `BackoffFor(int)` and `ListenerConnectionString(string)` internal and static); `AddTechStrapTicketChangeListener()` and `AddTechStrapNotifyBroadcaster()`; the Api registers the listener, the Worker the broadcaster.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Infrastructure.IntegrationTests/Live/NotifyTestSupport.cs` (new): SQL helpers (send, find the listener's backend, terminate it), a bounded poll, and a `Probe`, the test's own listener on the channel.

```csharp
using System.Threading.Channels;
using Npgsql;
using TechStrap.Infrastructure.Live;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>Plain SQL helpers for the NOTIFY tests: send a notification, find and kill the listener's backend, wait for a condition without sleeping a fixed time.</summary>
internal static class NotifyTestSupport
{
    /// <summary>A ceiling for waits, never a measurement: a pass returns as soon as the condition holds.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    public static async Task NotifyAsync(string connectionString, string payload)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_notify(@channel, @payload)", connection);
        command.Parameters.AddWithValue("channel", TicketChangeNotify.Channel);
        command.Parameters.AddWithValue("payload", payload);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Several notifications in one transaction: Postgres delivers them together, so the listener sees them in a single wake-up.</summary>
    public static async Task NotifyTogetherAsync(string connectionString, IReadOnlyList<string> payloads)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        foreach (var payload in payloads)
        {
            await using var command = new NpgsqlCommand("SELECT pg_notify(@channel, @payload)", connection, transaction);
            command.Parameters.AddWithValue("channel", TicketChangeNotify.Channel);
            command.Parameters.AddWithValue("payload", payload);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The listener's backends that are listening: found by the application name the listener gives its connection and by their last statement being the LISTEN (a connection that is open but has not listened yet would lose a notification).</summary>
    public static async Task<IReadOnlyList<int>> ListenerPidsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT pid FROM pg_stat_activity WHERE datname = current_database() AND application_name = '" + TicketChangeNotify.ListenerApplicationName + "' AND query LIKE 'LISTEN %'",
            connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var pids = new List<int>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            pids.Add(reader.GetInt32(0));
        }

        return pids;
    }

    public static async Task TerminateAsync(string connectionString, int pid)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", connection);
        command.Parameters.AddWithValue("pid", pid);
        await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Polls until <paramref name="condition"/> holds. <paramref name="between"/> runs after each miss (for example, to move a fake clock on).</summary>
    public static async Task<T> UntilAsync<T>(Func<Task<T>> read, Func<T, bool> condition, Action? between = null)
    {
        using var timeout = new CancellationTokenSource(Patience);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
        while (true)
        {
            var value = await read();
            if (condition(value))
            {
                return value;
            }

            between?.Invoke();
            await Task.Delay(50, linked.Token);
        }
    }

    public static Task<bool> UntilAsync(Func<bool> condition, Action? between = null) =>
        UntilAsync(() => Task.FromResult(condition()), held => held, between);

    /// <summary>The test's own listener: reads every payload sent on the channel, so a test can see exactly what was notified (and that nothing else was).</summary>
    public sealed class Probe : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly Channel<string> _received = Channel.CreateUnbounded<string>();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        private Probe(NpgsqlConnection connection)
        {
            _connection = connection;
            _connection.Notification += (_, notification) => _received.Writer.TryWrite(notification.Payload);
            _loop = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await _connection.WaitAsync(_stop.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        public static async Task<Probe> StartAsync(string connectionString)
        {
            var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using (var listen = new NpgsqlCommand($"LISTEN {TicketChangeNotify.Channel}", connection))
            {
                await listen.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            return new Probe(connection);
        }

        public async Task<string> NextAsync()
        {
            using var timeout = new CancellationTokenSource(Patience);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
            return await _received.Reader.ReadAsync(linked.Token);
        }

        public IReadOnlyList<string> Pending()
        {
            var items = new List<string>();
            while (_received.Reader.TryRead(out var item))
            {
                items.Add(item);
            }

            return items;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _loop;
            await _connection.DisposeAsync();
            _stop.Dispose();
        }
    }
}
```

`tests/TechStrap.Infrastructure.IntegrationTests/Live/TicketChangeListenerTests.cs` (new): the real relay handler with a recorder as the hub, and a fake clock for the backoff.

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;
using TechStrap.Infrastructure.Live;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>
/// The Api's NOTIFY listener against real Postgres (D-007, D-018): a notification reaches the relay, a bad payload is dropped without ending the loop, a killed connection leads to a
/// reconnect and one Resync, the listener never notifies (so nothing echoes), and stopping is clean. The real relay handler runs; the hub is a recorder.
/// </summary>
public sealed class TicketChangeListenerTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class CapturingLogger : ILogger<TicketChangeListener>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception) + (exception is null ? string.Empty : " " + exception)));
    }

    private sealed class ListenerHost : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public ListenerHost(string? connectionString)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ITicketChangeBroadcaster>(Recorder);
            services.AddScoped<IRelayTicketChangeHandler, RelayTicketChangeHandler>();
            _provider = services.BuildServiceProvider();
            Listener = new TicketChangeListener(
                Options.Create(new DatabaseConnectionOptions { ConnectionString = connectionString }),
                _provider.GetRequiredService<IServiceScopeFactory>(),
                Clock,
                Log);
        }

        public RecordingBroadcaster Recorder { get; } = new();

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));

        public CapturingLogger Log { get; } = new();

        public TicketChangeListener Listener { get; }

        public Task StartAsync(CancellationToken cancellationToken) => Listener.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => Listener.StopAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await Listener.StopAsync(CancellationToken.None);
            Listener.Dispose();
            await _provider.DisposeAsync();
        }
    }

    private static string Payload(Guid? ticketId = null, string kind = TicketChangeKinds.Updated) =>
        JsonSerializer.Serialize(
            new TicketChangedDto(Guid.NewGuid(), ticketId ?? Guid.NewGuid(), "ORB-5", Guid.NewGuid(), TicketEventTypes.StatusChanged, null, new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero), kind),
            JsonSerializerOptions.Web);

    private Task<IReadOnlyList<int>> WaitForListenerAsync(Func<IReadOnlyList<int>, bool>? condition = null) =>
        NotifyTestSupport.UntilAsync(() => NotifyTestSupport.ListenerPidsAsync(Database.ConnectionString), pids => condition?.Invoke(pids) ?? pids.Count == 1);

    // ---- the relay ----------------------------------------------------------------------------------------------------------------------------------

    [Fact(Timeout = 120000)]
    public async Task A_notification_reaches_the_relay_and_the_broadcaster_once()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitForListenerAsync();
        var payload = Payload();

        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, payload);
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 1);

        var change = host.Recorder.Attempts.ShouldHaveSingleItem();
        change.ToDto().ShouldBe(JsonSerializer.Deserialize<TicketChangedDto>(payload, JsonSerializerOptions.Web)!);
    }

    [Fact(Timeout = 120000)]
    public async Task A_bad_payload_is_dropped_without_its_text_in_the_log_and_the_loop_goes_on()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitForListenerAsync();
        var oversize = new string('x', TicketLiveLimits.MaxChangePayloadBytes + 10);

        foreach (var bad in new[] { "secret-not-json", "{\"kind\":\"Updated\"}", Payload(kind: "Exploded"), oversize, "null" })
        {
            await NotifyTestSupport.NotifyAsync(Database.ConnectionString, bad);
        }

        var good = Payload();
        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, good);
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 1);

        host.Recorder.Attempts.ShouldHaveSingleItem().EventId.ShouldBe(JsonSerializer.Deserialize<TicketChangedDto>(good, JsonSerializerOptions.Web)!.EventId);
        var warnings = host.Log.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToList();
        warnings.Count.ShouldBe(5);
        warnings.ShouldAllBe(message => message.Contains("dropped"));
        host.Log.Entries.ShouldAllBe(entry => !entry.Message.Contains("secret-not-json") && !entry.Message.Contains("xxxxx"));
    }

    [Fact(Timeout = 120000)]
    public async Task Notifications_that_arrive_together_are_all_relayed_in_order()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitForListenerAsync();
        var payloads = Enumerable.Range(0, 3).Select(_ => Payload()).ToList();

        await NotifyTestSupport.NotifyTogetherAsync(Database.ConnectionString, payloads);
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 3);

        host.Recorder.Attempts.Select(change => change.ToDto().EventId).ShouldBe(
            payloads.Select(payload => JsonSerializer.Deserialize<TicketChangedDto>(payload, JsonSerializerOptions.Web)!.EventId));
    }

    [Fact(Timeout = 120000)]
    public async Task A_hub_that_fails_does_not_end_the_loop()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitForListenerAsync();
        host.Recorder.Behaviour = (_, _) => throw new InvalidOperationException("hub down");

        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, Payload());
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 1);
        host.Recorder.Behaviour = null;
        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, Payload());
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 2);

        host.Recorder.Attempts.Count.ShouldBe(2);
    }

    [Fact(Timeout = 120000)]
    public async Task The_listener_only_listens_so_nothing_echoes()
    {
        await using var probe = await NotifyTestSupport.Probe.StartAsync(Database.ConnectionString);
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitForListenerAsync();
        var first = Payload();
        var sentinel = Payload();

        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, first);
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 1);
        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, sentinel);
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 2);

        // The probe is on the same channel: it saw the two notifications the test sent and nothing the listener might have sent back.
        (await probe.NextAsync()).ShouldBe(first);
        (await probe.NextAsync()).ShouldBe(sentinel);
        probe.Pending().ShouldBeEmpty();
    }

    // ---- reconnecting -------------------------------------------------------------------------------------------------------------------------------

    [Fact(Timeout = 180000)]
    public async Task Killing_the_connection_leads_to_a_reconnect_and_exactly_one_resync_and_the_relay_still_works()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var first = (await WaitForListenerAsync()).Single();
        host.Recorder.Attempts.ShouldBeEmpty();

        await NotifyTestSupport.TerminateAsync(Database.ConnectionString, first);

        // The backoff waits on the fake clock: move it on until the listener is back on a new backend.
        var second = (await NotifyTestSupport.UntilAsync(
            () => NotifyTestSupport.ListenerPidsAsync(Database.ConnectionString),
            pids => pids.Count == 1 && pids[0] != first,
            between: () => host.Clock.Advance(TimeSpan.FromMinutes(1)))).Single();
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 1);

        second.ShouldNotBe(first);
        host.Recorder.Attempts.ShouldHaveSingleItem().Kind.ShouldBe(TicketChangeKinds.Resync);
        host.Log.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("lost its database connection"));

        // A good connection resets the backoff: losing it again waits the first delay again, not the next one.
        await NotifyTestSupport.TerminateAsync(Database.ConnectionString, second);
        await NotifyTestSupport.UntilAsync(
            () => NotifyTestSupport.ListenerPidsAsync(Database.ConnectionString),
            pids => pids.Count == 1 && pids[0] != second,
            between: () => host.Clock.Advance(TimeSpan.FromMinutes(1)));
        var lost = host.Log.Entries.Where(entry => entry.Message.Contains("lost its database connection")).Select(entry => entry.Message).ToList();
        lost.ShouldAllBe(message => message.Contains("reconnect in 1s"));

        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, Payload());
        await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 3);
        host.Recorder.Attempts.Select(change => change.Kind).ShouldBe([TicketChangeKinds.Resync, TicketChangeKinds.Resync, TicketChangeKinds.Updated]);
    }

    [Fact(Timeout = 180000)]
    public async Task A_database_that_cannot_be_reached_is_retried_on_the_backoff_and_never_throws_out_of_the_service()
    {
        var unreachable = new NpgsqlConnectionStringBuilder(Database.ConnectionString) { Host = "127.0.0.1", Port = 1, Timeout = 2 }.ConnectionString;
        await using var host = new ListenerHost(unreachable);

        await host.StartAsync(TestContext.Current.CancellationToken);
        await NotifyTestSupport.UntilAsync(
            () => host.Log.Entries.Count(entry => entry.Message.Contains("lost its database connection")) >= 3,
            between: () => host.Clock.Advance(TimeSpan.FromMinutes(1)));

        host.Recorder.Attempts.ShouldBeEmpty();
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void The_backoff_doubles_from_the_initial_delay_and_stops_at_the_maximum()
    {
        Enumerable.Range(0, 7).Select(attempt => TicketChangeListener.BackoffFor(attempt).TotalSeconds).ShouldBe([1d, 2d, 4d, 8d, 16d, 30d, 30d]);
        TicketChangeListener.BackoffFor(-3).ShouldBe(TicketChangeNotify.ReconnectInitialDelay);
        TicketChangeListener.BackoffFor(10_000).ShouldBe(TicketChangeNotify.ReconnectMaxDelay);
    }

    [Fact]
    public void The_listener_gets_its_own_unpooled_named_keepalive_connection()
    {
        var builder = new NpgsqlConnectionStringBuilder(TicketChangeListener.ListenerConnectionString("Host=db;Database=techstrap;Username=u;Password=p;Maximum Pool Size=5"));

        builder.Pooling.ShouldBeFalse();
        builder.KeepAlive.ShouldBe(TicketChangeNotify.KeepAliveSeconds);
        builder.KeepAlive.ShouldBeGreaterThan(0);
        builder.ApplicationName.ShouldBe(TicketChangeNotify.ListenerApplicationName);
        builder.Database.ShouldBe("techstrap");
        builder.Host.ShouldBe("db");
    }

    // ---- starting and stopping ----------------------------------------------------------------------------------------------------------------------

    [Fact(Timeout = 120000)]
    public async Task Stopping_closes_the_connection_and_returns_promptly()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitForListenerAsync();

        await host.StopAsync(TestContext.Current.CancellationToken);

        await NotifyTestSupport.UntilAsync(() => NotifyTestSupport.ListenerPidsAsync(Database.ConnectionString), pids => pids.Count == 0);
    }

    [Fact(Timeout = 120000)]
    public async Task Stopping_while_waiting_to_reconnect_is_clean_too()
    {
        await using var host = new ListenerHost(Database.ConnectionString);
        await host.StartAsync(TestContext.Current.CancellationToken);
        var pid = (await WaitForListenerAsync()).Single();
        await NotifyTestSupport.TerminateAsync(Database.ConnectionString, pid);
        await NotifyTestSupport.UntilAsync(() => host.Log.Entries.Any(entry => entry.Message.Contains("lost its database connection")));

        // The clock never moved, so the listener is inside its backoff delay.
        await host.StopAsync(TestContext.Current.CancellationToken);

        (await NotifyTestSupport.ListenerPidsAsync(Database.ConnectionString)).ShouldBeEmpty();
    }

    [Theory(Timeout = 60000)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_a_connection_string_the_listener_is_off_and_starts_and_stops_cleanly(string? connectionString)
    {
        await using var host = new ListenerHost(connectionString);

        await host.StartAsync(TestContext.Current.CancellationToken);

        // With nothing to listen to, the service's work ends by itself; the wait is on that, not on a sleep.
        await host.Listener.ExecuteTask!.WaitAsync(NotifyTestSupport.Patience, TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        host.Log.Entries.ShouldContain(entry => entry.Level == LogLevel.Information && entry.Message.Contains("is off"));
        host.Recorder.Attempts.ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Infrastructure.IntegrationTests/Live/PgNotifyBroadcasterTests.cs` (new): the payload, the cap, a worker-style commit, a rollback and the real auto-close handler.

```csharp
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TechStrap.Application.Intake;
using TechStrap.Application.Live;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets.AutoClose;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.AutoClose;
using TechStrap.Infrastructure.Intake;
using TechStrap.Infrastructure.Live;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>
/// The Worker's side of the relay against real Postgres: the NOTIFY it sends has the right channel and a small, ids-only JSON payload, a worker-style commit notifies once per ticket
/// and a rollback never does, and the real auto-close handler produces the notification the Api will relay.
/// </summary>
public sealed class PgNotifyBroadcasterTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private PgNotifyTicketChangeBroadcaster NewBroadcaster() =>
        new(Options.Create(new DatabaseConnectionOptions { ConnectionString = Database.ConnectionString }));

    private PersistenceTestHost NewWorkerHost(Action<IServiceCollection>? extra = null) =>
        new(Database, configure: services =>
        {
            // The Worker's database options are validated when something reads them, which needs the host environment a real host has.
            services.AddSingleton<IHostEnvironment>(new DevelopmentEnvironment());
            services.AddTechStrapNotifyBroadcaster();
            extra?.Invoke(services);
        });

    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "TechStrap.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static TicketChange Change() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "ORB-42", Guid.NewGuid(), TicketEventTypes.StatusChanged, Guid.NewGuid(), new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero), TicketChangeKinds.Updated);

    [Fact(Timeout = 120000)]
    public async Task A_published_change_arrives_on_the_channel_as_small_ids_only_json()
    {
        await using var probe = await NotifyTestSupport.Probe.StartAsync(Database.ConnectionString);
        await using var broadcaster = NewBroadcaster();
        var change = Change();

        await broadcaster.PublishAsync(change, TestContext.Current.CancellationToken);

        var payload = await probe.NextAsync();
        JsonSerializer.Deserialize<TicketChangedDto>(payload, JsonSerializerOptions.Web).ShouldBe(change.ToDto());
        Encoding.UTF8.GetByteCount(payload).ShouldBeLessThan(8000);
        Encoding.UTF8.GetByteCount(payload).ShouldBeLessThanOrEqualTo(TicketLiveLimits.MaxChangePayloadBytes);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(
            ["eventId", "ticketId", "ticketNumber", "productId", "eventType", "actorAgentId", "occurredAt", "kind"]);
    }

    [Fact]
    public async Task A_change_that_is_over_the_cap_is_refused_before_it_is_sent()
    {
        await using var broadcaster = NewBroadcaster();

        await Should.ThrowAsync<InvalidOperationException>(() => broadcaster.PublishAsync(Change() with { TicketNumber = new string('X', 3000) }, Ct));
    }

    [Fact]
    public async Task Presence_is_not_sent_because_it_lives_in_the_api_process()
    {
        await using var broadcaster = NewBroadcaster();

        await broadcaster.PublishPresenceAsync(new TicketPresence(Guid.NewGuid(), []), Ct);
    }

    [Fact]
    public async Task Without_a_connection_string_publishing_throws_and_does_not_create_a_data_source()
    {
        await using var broadcaster = new PgNotifyTicketChangeBroadcaster(Options.Create(new DatabaseConnectionOptions()));

        await Should.ThrowAsync<InvalidOperationException>(() => broadcaster.PublishAsync(Change(), Ct));
    }

    [Fact(Timeout = 120000)]
    public async Task A_worker_style_commit_notifies_once_per_ticket_and_a_rollback_notifies_nothing()
    {
        await using var probe = await NotifyTestSupport.Probe.StartAsync(Database.ConnectionString);
        await using var host = NewWorkerHost();
        var scenario = await TicketScenario.CreateAsync(host);

        await using (var scope = host.CreateScope())
        {
            await using var unitOfWork = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(TestContext.Current.CancellationToken);
            var ticket = Ticket.Create(TicketNumber.Create("ACME", 700).Value, scenario.Acme.Id, scenario.Requester.Id, "Rolled back", TicketChannel.Web, null, false, host.Clock).Value;
            scope.ServiceProvider.GetRequiredService<ITicketRepository>().Add(ticket);
            await scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var committed = await scenario.CreateTicketAsync("Committed");

        var payload = JsonSerializer.Deserialize<TicketChangedDto>(await probe.NextAsync(), JsonSerializerOptions.Web)!;
        payload.TicketId.ShouldBe(committed.Id);
        payload.Kind.ShouldBe(TicketChangeKinds.Created);
        payload.TicketNumber.ShouldBe(committed.Number.ToString());

        // Anything else would have been notified before this barrier.
        await NotifyTestSupport.NotifyAsync(Database.ConnectionString, "barrier");
        (await probe.NextAsync()).ShouldBe("barrier");
        probe.Pending().ShouldBeEmpty();
    }

    [Fact(Timeout = 120000)]
    public async Task The_real_auto_close_handler_notifies_a_status_change_with_no_agent_actor()
    {
        await using var probe = await NotifyTestSupport.Probe.StartAsync(Database.ConnectionString);
        await using var host = NewWorkerHost(services =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [PortalLinkOptions.PublicUrlKey] = "https://help.test", [AutoCloseOptions.DaysKey] = "7" })
                .Build();
            services.AddLogging();
            services.AddTechStrapIntake(configuration);
            services.AddTechStrapTicketOperations(configuration);
            services.AddTechStrapAutoClose(configuration);
        });
        Guid productId = default, requesterId = default, ticketId = default;
        (await host.CommitAsync(services =>
        {
            var product = Product.Create("orbitly", "Orbitly", "ORB", null, host.Clock).Value;
            services.GetRequiredService<IProductRepository>().Add(product);
            var requester = Requester.Create("pat@example.com", "Pat", null, host.Clock).Value;
            services.GetRequiredService<IRequesterRepository>().Add(requester);
            (productId, requesterId) = (product.Id, requester.Id);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        (await host.CommitAsync(async services =>
        {
            var number = (await services.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(productId, TestContext.Current.CancellationToken)).Value;
            var ticket = Ticket.Create(number, productId, requesterId, "Cannot log in", TicketChannel.Web, null, false, host.Clock).Value;
            ticket.AddCustomerReply(requesterId, "<p>Help</p>", host.Clock).IsSuccess.ShouldBeTrue();
            ticket.ChangeStatus(TicketStatus.Solved, Actor.ForAgent(Guid.NewGuid()), host.Clock).IsSuccess.ShouldBeTrue();
            services.GetRequiredService<ITicketRepository>().Add(ticket);
            ticketId = ticket.Id;
        })).IsSuccess.ShouldBeTrue();
        _ = await probe.NextAsync();
        host.Clock.Advance(TimeSpan.FromDays(8));

        await using var scope = host.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IAutoCloseSolvedTicketsHandler>().HandleAsync(TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        var closed = JsonSerializer.Deserialize<TicketChangedDto>(await probe.NextAsync(), JsonSerializerOptions.Web)!;
        closed.TicketId.ShouldBe(ticketId);
        closed.EventType.ShouldBe(TicketEventTypes.StatusChanged);
        closed.Kind.ShouldBe(TicketChangeKinds.Updated);
        closed.ActorAgentId.ShouldBeNull();
        probe.Pending().ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Api.Tests/Live/NotifyRelayHostTests.cs` (new): the two hosts wired as in production, and the whole path from a Worker publish to a hub client, including a killed listener connection ending in a `Resync` (the real backoff is one second).

```csharp
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;
using TechStrap.Infrastructure.Live;

namespace TechStrap.Api.Tests.Live;

/// <summary>
/// The two hosts wired as in production: the Worker publishes through NOTIFY, the Api listens and pushes to a hub client (D-018). The wiring tests need no database; the relay test shares
/// one database between a real Api host and a real Worker host.
/// </summary>
public sealed class NotifyRelayHostTests(TestPostgres postgres)
{
    private const string ListenerQuery = "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND application_name = '" + TicketChangeNotify.ListenerApplicationName + "' AND query LIKE 'LISTEN %'";

    [Fact]
    public void The_api_listens_and_pushes_to_the_hub_and_the_worker_only_notifies()
    {
        using var api = new ApiFactory();
        using var worker = new WorkerFactory();

        api.Services.GetServices<IHostedService>().Select(service => service.GetType().Name).ShouldContain("TicketChangeListener");
        api.Services.GetRequiredService<ITicketChangeBroadcaster>().GetType().Name.ShouldBe("SignalRTicketChangeBroadcaster");
        worker.Services.GetServices<IHostedService>().Select(service => service.GetType().Name).ShouldNotContain("TicketChangeListener");
        worker.Services.GetRequiredService<ITicketChangeBroadcaster>().GetType().Name.ShouldBe("PgNotifyTicketChangeBroadcaster");
        worker.Services.GetService<ITicketPresenceStore>().ShouldBeNull();
    }

    [Fact(Timeout = 180000)]
    public async Task A_change_the_worker_publishes_reaches_a_hub_client_and_a_killed_listener_connection_ends_in_a_resync()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var apiSettings = new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" };
        await using var api = new ApiFactory(settings: apiSettings);
        await using var worker = new WorkerFactory(settings: new Dictionary<string, string?> { ["ConnectionStrings:TechStrap"] = database.ConnectionString });
        await TicketTestData.SeedAsync(api, ct);
        await using var connection = HubTestSupport.Connect(api, HubTestSupport.AgentToken("sam", "Sam"));
        using var inbox = new HubTestSupport.Inbox<TicketChangedDto>(connection, TicketHubMethods.TicketChanged);
        await connection.StartAsync(ct);
        await WaitForListenerAsync(database, expected: 1);
        var change = new TicketChange(Guid.NewGuid(), Guid.NewGuid(), "ORB-9", Guid.NewGuid(), TicketEventTypes.StatusChanged, null, DateTimeOffset.UtcNow, TicketChangeKinds.Updated);

        await worker.Services.GetRequiredService<ITicketChangeBroadcaster>().PublishAsync(change, ct);

        (await inbox.NextAsync()).ShouldBe(change.ToDto());

        // Kill the listener's backend: it reconnects after its backoff (a real second) and tells the hub's clients to reload everything.
        await database.ExecuteAsync($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = current_database() AND application_name = '{TicketChangeNotify.ListenerApplicationName}'");
        var resync = await inbox.NextAsync();

        resync.Kind.ShouldBe(TicketChangeKinds.Resync);
        resync.TicketId.ShouldBe(Guid.Empty);
        await WaitForListenerAsync(database, expected: 1);
        var after = new TicketChange(Guid.NewGuid(), Guid.NewGuid(), "ORB-10", Guid.NewGuid(), TicketEventTypes.Assigned, null, DateTimeOffset.UtcNow, TicketChangeKinds.Updated);
        await worker.Services.GetRequiredService<ITicketChangeBroadcaster>().PublishAsync(after, ct);
        (await inbox.NextAsync()).EventId.ShouldBe(after.EventId);
    }

    private static async Task WaitForListenerAsync(ApiTestDatabase database, int expected)
    {
        using var timeout = new CancellationTokenSource(HubTestSupport.Patience);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
        while (await database.ScalarAsync<long>(ListenerQuery) != expected)
        {
            await Task.Delay(50, linked.Token);
        }
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter-query "/*/TechStrap.Infrastructure.IntegrationTests.Live/*/*"
```

Expected: FAIL to compile, because the broadcaster and the listener do not exist:

```text
tests/TechStrap.Infrastructure.IntegrationTests/Live/PgNotifyBroadcasterTests.cs(33,13): error CS0246: The type or namespace name 'PgNotifyTicketChangeBroadcaster' could not be found (are you missing a using directive or an ass...
tests/TechStrap.Infrastructure.IntegrationTests/Live/TicketChangeListenerTests.cs(24,52): error CS0246: The type or namespace name 'TicketChangeListener' could not be found (are you missing a using directive or an assembly refe...
tests/TechStrap.Infrastructure.IntegrationTests/Live/TicketChangeListenerTests.cs(60,16): error CS0246: The type or namespace name 'TicketChangeListener' could not be found (are you missing a using directive or an assembly refe...
Build failed with exit code: 1.
```

- [ ] **Step 3: Write the relay**

`src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj` (modify): `Npgsql` becomes a direct reference (its version is already pinned centrally).

```diff
--- a/src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj
+++ b/src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj
@@ -16,4 +16,6 @@
     <PackageReference Include="Markdig" />
     <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
+    <!-- LISTEN/NOTIFY use Npgsql directly; it also arrives through the EF provider, but code that calls it should not depend on that. -->
+    <PackageReference Include="Npgsql" />
     <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
     <PackageReference Include="SyntaxCircus.Email" />
```

`src/TechStrap.Infrastructure/Live/TicketChangeNotify.cs` (new)

```csharp
namespace TechStrap.Infrastructure.Live;

/// <summary>The Postgres NOTIFY relay between the Worker and the Api (D-007, D-018): the channel and the listener's timings. Constants, not settings: nothing here is for an operator to tune.</summary>
public static class TicketChangeNotify
{
    /// <summary>The channel the Worker notifies and the Api listens on.</summary>
    public const string Channel = "techstrap_ticket_changes";

    /// <summary>The listener connection's name in <c>pg_stat_activity</c>, so an operator can see it (and a test can find it).</summary>
    public const string ListenerApplicationName = "techstrap-ticket-change-listener";

    /// <summary>Seconds of silence before Npgsql sends a keepalive on the listener's connection, so an idle NAT or firewall does not drop it.</summary>
    public const int KeepAliveSeconds = 30;

    /// <summary>The wait before the first reconnect; each further failure in a row doubles it.</summary>
    public static readonly TimeSpan ReconnectInitialDelay = TimeSpan.FromSeconds(1);

    /// <summary>The longest wait between reconnect attempts.</summary>
    public static readonly TimeSpan ReconnectMaxDelay = TimeSpan.FromSeconds(30);
}
```

`src/TechStrap.Infrastructure/Live/PgNotifyTicketChangeBroadcaster.cs` (new)

```csharp
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// The Worker's <see cref="ITicketChangeBroadcaster"/> (D-018): sends a Postgres <c>NOTIFY</c> on <see cref="TicketChangeNotify.Channel"/> for the Api's listener to relay. It runs after the commit, from
/// the post-commit hook, on a pooled connection of its own, so it is separate from the transaction that wrote the change. The payload is the change's JSON (ids and names only, far under
/// the 8,000 bytes Postgres allows). Presence belongs to the Api process, so <see cref="PublishPresenceAsync"/> does nothing.
/// </summary>
internal sealed class PgNotifyTicketChangeBroadcaster(IOptions<DatabaseConnectionOptions> database) : ITicketChangeBroadcaster, IAsyncDisposable
{
    private readonly Lazy<NpgsqlDataSource> _dataSource = new(() =>
        NpgsqlDataSource.Create(database.Value.ConnectionString is { Length: > 0 } connectionString
            ? connectionString
            : throw new InvalidOperationException("No database connection string is configured, so a ticket change cannot be sent.")));

    public async Task PublishAsync(TicketChange change, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(change.ToDto(), JsonSerializerOptions.Web);
        if (Encoding.UTF8.GetByteCount(payload) > TicketLiveLimits.MaxChangePayloadBytes)
        {
            throw new InvalidOperationException("The ticket change is larger than the relay accepts.");
        }

        await using var command = _dataSource.Value.CreateCommand("SELECT pg_notify(@channel, @payload)");
        command.Parameters.AddWithValue("channel", TicketChangeNotify.Channel);
        command.Parameters.AddWithValue("payload", payload);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task PublishPresenceAsync(TicketPresence presence, CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => _dataSource.IsValueCreated ? _dataSource.Value.DisposeAsync() : ValueTask.CompletedTask;
}
```

`src/TechStrap.Infrastructure/Live/TicketChangeListener.cs` (new): the delay is worked out after each attempt, so a connection that was good for a while and is then lost starts again from the first delay (the first draft computed it before the attempt and waited the stale value; the "reconnect in 1s" assertion of the reconnect test caught it).

```csharp
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// The Api's end of the NOTIFY relay (D-007, D-018): one dedicated, unpooled connection that does <c>LISTEN</c> on <see cref="TicketChangeNotify.Channel"/> and hands each payload to
/// <see cref="IRelayTicketChangeHandler"/>, which validates it. A payload that is bad is dropped by the handler and the loop goes on. When the connection is lost it reconnects with an
/// exponential backoff (<see cref="BackoffFor"/>) and, once it is listening again, relays a <c>Resync</c>, because notifications sent in the gap are gone for good. It only ever listens: it
/// never notifies, so a change cannot echo back to itself. Stopping the host cancels the wait and closes the connection. Needs a direct Postgres connection (not a transaction-pooling proxy).
/// Nothing the loop logs carries a payload, a connection string or an exception message.
/// </summary>
internal sealed class TicketChangeListener(
    IOptions<DatabaseConnectionOptions> database,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<TicketChangeListener> logger) : BackgroundService
{
    /// <summary>The delay before reconnect number <paramref name="failuresInARow"/> (0 is the first): the initial delay doubled each time, never over the maximum.</summary>
    internal static TimeSpan BackoffFor(int failuresInARow)
    {
        var doublings = Math.Min(Math.Max(failuresInARow, 0), 16);
        var delay = TimeSpan.FromTicks(TicketChangeNotify.ReconnectInitialDelay.Ticks << doublings);
        return delay > TicketChangeNotify.ReconnectMaxDelay ? TicketChangeNotify.ReconnectMaxDelay : delay;
    }

    /// <summary>The configured connection with its own settings for a long-lived listener: no pool, a name and a keepalive.</summary>
    internal static string ListenerConnectionString(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            ApplicationName = TicketChangeNotify.ListenerApplicationName,
            KeepAlive = TicketChangeNotify.KeepAliveSeconds,
            TcpKeepAlive = true,
        }.ConnectionString;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(database.Value.ConnectionString))
        {
            logger.LogInformation("The ticket change listener is off: no database connection string is configured.");
            return;
        }

        var connectionString = ListenerConnectionString(database.Value.ConnectionString);
        var failuresInARow = 0;
        var listenedBefore = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            string? lost = null;
            try
            {
                await ListenAsync(
                    connectionString,
                    resync: listenedBefore,
                    onListening: () =>
                    {
                        failuresInARow = 0;
                        listenedBefore = true;
                    },
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                lost = exception.GetType().Name;
            }

            // Worked out after the attempt, so a connection that was good for a while and is then lost starts again from the first delay.
            var delay = BackoffFor(failuresInARow++);
            if (lost is not null)
            {
                logger.LogWarning("The ticket change listener lost its database connection ({ExceptionType}); it will reconnect in {DelaySeconds}s.", lost, delay.TotalSeconds);
            }

            try
            {
                await Task.Delay(delay, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ListenAsync(string connectionString, bool resync, Action onListening, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var received = new Queue<string>();
        connection.Notification += (_, notification) => received.Enqueue(notification.Payload);
        await using (var listen = new NpgsqlCommand($"LISTEN {TicketChangeNotify.Channel}", connection))
        {
            await listen.ExecuteNonQueryAsync(cancellationToken);
        }

        onListening();
        if (resync)
        {
            // Whatever was sent while the connection was down is lost, so tell the clients to reload everything.
            await RelayAsync(JsonSerializer.Serialize(TicketChange.Resync(Guid.CreateVersion7(clock.GetUtcNow()), clock.GetUtcNow()).ToDto(), JsonSerializerOptions.Web), cancellationToken);
        }

        while (true)
        {
            await connection.WaitAsync(cancellationToken);
            while (received.TryDequeue(out var payload))
            {
                await RelayAsync(payload, cancellationToken);
            }
        }
    }

    private async Task RelayAsync(string payload, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IRelayTicketChangeHandler>().HandleAsync(new RelayTicketChangeRequest(payload), cancellationToken);
            if (result.IsFailure)
            {
                // The code only: the payload came off a channel anything can write to and is never logged.
                logger.LogWarning("A ticket change from the database was dropped ({ErrorCode}).", result.Errors[0].Code);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Relaying a ticket change from the database failed ({ExceptionType}).", exception.GetType().Name);
        }
    }
}
```

`src/TechStrap.Infrastructure/Live/LiveServiceCollectionExtensions.cs` (modify)

```diff
--- a/src/TechStrap.Infrastructure/Live/LiveServiceCollectionExtensions.cs
+++ b/src/TechStrap.Infrastructure/Live/LiveServiceCollectionExtensions.cs
@@ -16,3 +16,20 @@ public static class LiveServiceCollectionExtensions
         return services;
     }
+
+    /// <summary>
+    /// The Api's listener on the NOTIFY channel (a hosted service). It needs <c>IRelayTicketChangeHandler</c> (registered with the other handlers) and a broadcaster that reaches the
+    /// hub. The Worker never calls this, and nothing here ever notifies, so a change cannot echo.
+    /// </summary>
+    public static IServiceCollection AddTechStrapTicketChangeListener(this IServiceCollection services)
+    {
+        services.AddHostedService<TicketChangeListener>();
+        return services;
+    }
+
+    /// <summary>The Worker's broadcaster: a Postgres NOTIFY after each commit, for the Api's listener. Call it after <c>AddTechStrapPersistence</c>, whose null broadcaster it replaces.</summary>
+    public static IServiceCollection AddTechStrapNotifyBroadcaster(this IServiceCollection services)
+    {
+        services.Replace(ServiceDescriptor.Singleton<ITicketChangeBroadcaster, PgNotifyTicketChangeBroadcaster>());
+        return services;
+    }
 }
```

`src/TechStrap.Api/Live/LiveHubExtensions.cs` (modify): the Api starts the listener.

```diff
--- a/src/TechStrap.Api/Live/LiveHubExtensions.cs
+++ b/src/TechStrap.Api/Live/LiveHubExtensions.cs
@@ -10,5 +10,5 @@ public static class LiveHubExtensions
 {
     /// <summary>
-    /// SignalR, the in-memory presence store (D-007: one Api instance) and the hub's <see cref="ITicketChangeBroadcaster"/>. Call it after <c>AddTechStrapPersistence</c>:
+    /// SignalR, the in-memory presence store (D-007: one Api instance), the hub's <see cref="ITicketChangeBroadcaster"/> and the listener that relays the Worker's NOTIFY to it. Call it after <c>AddTechStrapPersistence</c>:
     /// that registered the null broadcaster, and <c>Replace</c> is what makes the hub's win.
     /// </summary>
@@ -18,4 +18,5 @@ public static class LiveHubExtensions
         services.AddTechStrapPresence();
         services.Replace(ServiceDescriptor.Singleton<ITicketChangeBroadcaster, SignalRTicketChangeBroadcaster>());
+        services.AddTechStrapTicketChangeListener();
         return services;
     }
```

`src/TechStrap.Worker/Program.cs` (modify): after `AddTechStrapPersistence`, so the NOTIFY broadcaster replaces the null one.

```diff
--- a/src/TechStrap.Worker/Program.cs
+++ b/src/TechStrap.Worker/Program.cs
@@ -9,4 +9,5 @@ using TechStrap.Hosting.Wiring;
 using TechStrap.Infrastructure.AutoClose;
 using TechStrap.Infrastructure.Email;
+using TechStrap.Infrastructure.Live;
 using TechStrap.Infrastructure.Persistence;
 using TechStrap.Worker.AutoClose;
@@ -46,4 +47,6 @@ builder.Services.AddCorrelationId();
 builder.Services.AddTechStrapHttpClientDefaults();
 builder.Services.AddTechStrapPersistence();
+// A committed change is sent as a Postgres NOTIFY for the Api to relay to its hub (D-018). After AddTechStrapPersistence, whose null broadcaster this replaces.
+builder.Services.AddTechStrapNotifyBroadcaster();
 builder.Services.AddTechStrapEmail(builder.Configuration);
 builder.Services.AddHostedService<EmailOutboxWorker>();
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter-query "/*/TechStrap.Infrastructure.IntegrationTests.Live/*/*"
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/TechStrap.Api.Tests.Live/*/*"
```

Expected: PASS (62 Infrastructure Live tests, 25 Api Live tests):

```text
  total: 62
  succeeded: 62
  total: 25
  succeeded: 25
```

- [ ] **Step 5: Prove each pin with a recorded mutation**

Save `$T\specs\m4.py`:

```python
"""Mutations of Task 4 (worker NOTIFY and the Api listener). Run from the repository root."""
INFRA = ["dotnet", "test", "--project", "tests/TechStrap.Infrastructure.IntegrationTests", "-c", "Release", "--filter-query"]
API = ["dotnet", "test", "--project", "tests/TechStrap.Api.Tests", "-c", "Release", "--filter-query"]
LISTENER_TESTS = INFRA + ["/*/*/TicketChangeListenerTests/*"]
BROADCAST_TESTS = INFRA + ["/*/*/PgNotifyBroadcasterTests/*"]
WIRING = API + ["/*/*/NotifyRelayHostTests/The_api_listens*"]
L = "src/TechStrap.Infrastructure/Live/TicketChangeListener.cs"
B = "src/TechStrap.Infrastructure/Live/PgNotifyTicketChangeBroadcaster.cs"
X = "src/TechStrap.Infrastructure/Live/LiveServiceCollectionExtensions.cs"

MUTATIONS = [
    ("1 the backoff does not double", L, [("TicketChangeNotify.ReconnectInitialDelay.Ticks << doublings", "TicketChangeNotify.ReconnectInitialDelay.Ticks << 0")], LISTENER_TESTS),
    ("2 the backoff has no ceiling", L, [("return delay > TicketChangeNotify.ReconnectMaxDelay ? TicketChangeNotify.ReconnectMaxDelay : delay;", "return delay;")], LISTENER_TESTS),
    ("3 a reconnect sends no resync", L, [("resync: listenedBefore,", "resync: listenedBefore && false,")], LISTENER_TESTS),
    ("4 the first connect sends a resync", L, [("var listenedBefore = false;", "var listenedBefore = true;")], LISTENER_TESTS),
    ("5 a good connection does not reset the backoff", L, [("                        failuresInARow = 0;\n", "")], LISTENER_TESTS),
    ("6 the listener listens on another channel", L, [('new NpgsqlCommand($"LISTEN {TicketChangeNotify.Channel}", connection)', 'new NpgsqlCommand("LISTEN other_channel", connection)')], LISTENER_TESTS),
    ("7 only the first queued payload is relayed per wake-up", L, [("            while (received.TryDequeue(out var payload))", "            if (received.TryDequeue(out var payload))")], LISTENER_TESTS),
    ("8 the listener connection is pooled", L, [("            Pooling = false,\n", "            Pooling = true,\n")], LISTENER_TESTS),
    ("9 the listener has no keepalive", L, [("            KeepAlive = TicketChangeNotify.KeepAliveSeconds,\n", "            KeepAlive = 0,\n")], LISTENER_TESTS),
    ("11 a blank connection string is not noticed", L, [("if (string.IsNullOrWhiteSpace(database.Value.ConnectionString))", "if (database.Value.ConnectionString is null)")], LISTENER_TESTS),
    ("12 a dropped payload is logged with its text", L, [('logger.LogWarning("A ticket change from the database was dropped ({ErrorCode}).", result.Errors[0].Code);', 'logger.LogWarning("A ticket change from the database was dropped ({ErrorCode}): {Payload}.", result.Errors[0].Code, payload);')], LISTENER_TESTS),
    ("13 the NOTIFY goes to another channel", B, [('command.Parameters.AddWithValue("channel", TicketChangeNotify.Channel);', 'command.Parameters.AddWithValue("channel", "other_channel");')], BROADCAST_TESTS),
    ("14 the NOTIFY payload has no size cap", B, [("> TicketLiveLimits.MaxChangePayloadBytes)", "> TicketLiveLimits.MaxChangePayloadBytes * 1000)")], BROADCAST_TESTS),
    ("15 the worker keeps the null broadcaster", X, [("services.Replace(ServiceDescriptor.Singleton<ITicketChangeBroadcaster, PgNotifyTicketChangeBroadcaster>());", "services.Replace(ServiceDescriptor.Singleton<ITicketChangeBroadcaster, NullTicketChangeBroadcaster>());")], BROADCAST_TESTS),
    ("16 the api does not start the listener", "src/TechStrap.Api/Live/LiveHubExtensions.cs", [("        services.AddTechStrapTicketChangeListener();\n", "")], WIRING),
]
```

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | the backoff does not double | `TicketChangeListener.cs` | KILLED (1 failing) |
| 2 | the backoff has no ceiling | `TicketChangeListener.cs` | KILLED (1 failing) |
| 3 | a reconnect sends no resync | `TicketChangeListener.cs` | KILLED (1 failing) |
| 4 | the first connect sends a resync | `TicketChangeListener.cs` | KILLED (3 failing) |
| 5 | a good connection does not reset the backoff | `TicketChangeListener.cs` | KILLED (1 failing) |
| 6 | the listener listens on another channel | `TicketChangeListener.cs` | KILLED (5 failing) |
| 7 | only the first queued payload is relayed per wake-up | `TicketChangeListener.cs` | SURVIVED |
| 8 | the listener connection is pooled | `TicketChangeListener.cs` | KILLED (1 failing) |
| 9 | the listener has no keepalive | `TicketChangeListener.cs` | KILLED (1 failing) |
| 11 | a blank connection string is not noticed | `TicketChangeListener.cs` | KILLED (1 failing) |
| 12 | a dropped payload is logged with its text | `TicketChangeListener.cs` | KILLED (3 failing) |
| 13 | the NOTIFY goes to another channel | `PgNotifyTicketChangeBroadcaster.cs` | KILLED (3 failing) |
| 14 | the NOTIFY payload has no size cap | `PgNotifyTicketChangeBroadcaster.cs` | KILLED (1 failing) |
| 15 | the worker keeps the null broadcaster | `LiveServiceCollectionExtensions.cs` | KILLED (2 failing) |
| 16 | the api does not start the listener | `LiveHubExtensions.cs` | KILLED (1 failing) |

Run them (three foreground batches, about 20 seconds each) and record the results. Mutation 3 first did not compile (the variable it left unused is an error) and was rewritten to `listenedBefore && false`. Mutation 7 is an honest survivor and equivalent: Npgsql hands over one notification per `WaitAsync`, so the listener's queue never holds two (a test that sends three notifications in one transaction, `Notifications_that_arrive_together_are_all_relayed_in_order`, passes with the loop and with the `if`). The other mutations are killed; the first mutation run found that the harness had a second batch running in the clone (see "A mutation-run hazard that happened"), so every result above was re-run alone.

- [ ] **Step 6: Run the full suites and commit**

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: all pass (the Worker and Api start with the new registrations in every host test).

```bash
git add -A
git diff --cached --stat
git commit -m "feat: PHASE-10a worker NOTIFY broadcaster and Api listener (D-018, D-046)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

### Task 5: Observability: the TechStrap meter

**Review Focus pin:** 3 (the reconnect, relay and failure counters of the listener) and 4 (the gauge counts agents, not connections).

**Files:**

- Create: `src/TechStrap.Infrastructure/Live/TechStrapMetrics.cs`
- Modify: `src/TechStrap.Infrastructure/Live/LiveServiceCollectionExtensions.cs`, `src/TechStrap.Infrastructure/Live/TicketChangeListener.cs`, `src/TechStrap.Api/Live/TicketHub.cs`, `src/TechStrap.Api/Live/LiveHubExtensions.cs`, `src/TechStrap.Api/Program.cs`, `src/TechStrap.Worker/Program.cs`
- Test (create): `tests/TechStrap.Infrastructure.IntegrationTests/Live/MetricsProbe.cs`, `TechStrapMetricsTests.cs`, `tests/TechStrap.Api.Tests/Live/LiveMetricsHostTests.cs`
- Test (modify): `tests/TechStrap.Infrastructure.IntegrationTests/Live/TicketChangeListenerTests.cs`, `tests/TechStrap.Api.Tests/Live/HubPolicyCoverageTests.cs`

**Interfaces:**
- Consumes: Task 3's hub and Task 4's listener, `AddSyntaxCircusObservability(string, IEnumerable<string>)` of the observability package.
- Produces: `TechStrapMetrics` (public, `IDisposable`, singleton; the meter `TechStrap`; the constants `MeterName`, `ConnectedAgentsName`, `ChangesRelayedName`, `RelayFailuresName`, `ListenerReconnectsName`; `AgentConnected(connectionId, subject)`, `AgentDisconnected(connectionId)`, `ConnectedAgents`, `ChangeRelayed()`, `RelayFailed()`, `ListenerReconnected()`, `IsObserved`); `AddTechStrapMetrics()`; the hub and the listener record; the Api and the Worker pass `[TechStrapMetrics.MeterName]` to observability.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Infrastructure.IntegrationTests/Live/MetricsProbe.cs` (new): a `MeterListener` on one `TechStrapMetrics` instance only (other hosts in the same process create meters with the same name).

```csharp
using System.Diagnostics.Metrics;
using TechStrap.Infrastructure.Live;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>
/// A <see cref="MeterListener"/> on one <see cref="TechStrapMetrics"/> instance only: other hosts in the same test process create meters with the same name, so the listener
/// subscribes to an instrument only when it belongs to the meter it was given.
/// </summary>
internal sealed class MetricsProbe : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _sums = [];
    private readonly Dictionary<string, long> _lastObserved = [];
    private readonly List<string> _published = [];

    public MetricsProbe(TechStrapMetrics metrics)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter == metrics.Meter)
            {
                lock (_gate)
                {
                    _published.Add($"{instrument.Name}:{instrument.GetType().Name}");
                }

                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => Record(instrument, value));
        _listener.SetMeasurementEventCallback<int>((instrument, value, _, _) => Record(instrument, value));
        _listener.Start();
    }

    public IReadOnlyList<string> Published
    {
        get
        {
            lock (_gate)
            {
                return [.. _published];
            }
        }
    }

    /// <summary>The total of a counter's increments so far.</summary>
    public long Sum(string instrument)
    {
        lock (_gate)
        {
            return _sums.GetValueOrDefault(instrument);
        }
    }

    /// <summary>Asks every observable instrument for its current value and returns the gauge's.</summary>
    public long Observe(string instrument)
    {
        _listener.RecordObservableInstruments();
        lock (_gate)
        {
            return _lastObserved.GetValueOrDefault(instrument, -1);
        }
    }

    private void Record(Instrument instrument, long value)
    {
        lock (_gate)
        {
            if (instrument is ObservableGauge<int>)
            {
                _lastObserved[instrument.Name] = value;
            }
            else
            {
                _sums[instrument.Name] = _sums.GetValueOrDefault(instrument.Name) + value;
            }
        }
    }

    public void Dispose() => _listener.Dispose();
}
```

`tests/TechStrap.Infrastructure.IntegrationTests/Live/TechStrapMetricsTests.cs` (new)

```csharp
using TechStrap.Infrastructure.Live;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>The instruments are real: a <see cref="System.Diagnostics.Metrics.MeterListener"/> sees them by their constant names and they move when the adapters record.</summary>
public sealed class TechStrapMetricsTests
{
    [Fact]
    public void The_names_are_pinned_constants()
    {
        TechStrapMetrics.MeterName.ShouldBe("TechStrap");
        TechStrapMetrics.ConnectedAgentsName.ShouldBe("techstrap.live.connected_agents");
        TechStrapMetrics.ChangesRelayedName.ShouldBe("techstrap.live.changes_relayed");
        TechStrapMetrics.RelayFailuresName.ShouldBe("techstrap.live.relay_failures");
        TechStrapMetrics.ListenerReconnectsName.ShouldBe("techstrap.live.listener_reconnects");
    }

    [Fact]
    public void A_listener_sees_the_four_instruments_with_their_kinds()
    {
        using var metrics = new TechStrapMetrics();
        using var probe = new MetricsProbe(metrics);

        probe.Published.ShouldBe(
            [
                $"{TechStrapMetrics.ConnectedAgentsName}:ObservableGauge`1",
                $"{TechStrapMetrics.ChangesRelayedName}:Counter`1",
                $"{TechStrapMetrics.RelayFailuresName}:Counter`1",
                $"{TechStrapMetrics.ListenerReconnectsName}:Counter`1",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void The_gauge_counts_distinct_agents_not_connections()
    {
        using var metrics = new TechStrapMetrics();
        using var probe = new MetricsProbe(metrics);
        probe.Observe(TechStrapMetrics.ConnectedAgentsName).ShouldBe(0);

        metrics.AgentConnected("c1", "sam");
        metrics.AgentConnected("c2", "sam");
        probe.Observe(TechStrapMetrics.ConnectedAgentsName).ShouldBe(1);

        metrics.AgentConnected("c3", "kim");
        probe.Observe(TechStrapMetrics.ConnectedAgentsName).ShouldBe(2);

        metrics.AgentDisconnected("c1");
        probe.Observe(TechStrapMetrics.ConnectedAgentsName).ShouldBe(2);

        metrics.AgentDisconnected("c2");
        probe.Observe(TechStrapMetrics.ConnectedAgentsName).ShouldBe(1);

        metrics.AgentDisconnected("c2");
        metrics.AgentDisconnected("unknown");
        metrics.AgentDisconnected("c3");
        probe.Observe(TechStrapMetrics.ConnectedAgentsName).ShouldBe(0);
    }

    [Fact]
    public void Connecting_the_same_connection_twice_does_not_count_twice()
    {
        using var metrics = new TechStrapMetrics();

        metrics.AgentConnected("c1", "sam");
        metrics.AgentConnected("c1", "sam");

        metrics.ConnectedAgents.ShouldBe(1);
    }

    [Fact]
    public void Each_counter_moves_by_one_per_call_and_independently()
    {
        using var metrics = new TechStrapMetrics();
        using var probe = new MetricsProbe(metrics);

        metrics.ChangeRelayed();
        metrics.ChangeRelayed();
        metrics.RelayFailed();
        metrics.ListenerReconnected();
        metrics.ListenerReconnected();
        metrics.ListenerReconnected();

        probe.Sum(TechStrapMetrics.ChangesRelayedName).ShouldBe(2);
        probe.Sum(TechStrapMetrics.RelayFailuresName).ShouldBe(1);
        probe.Sum(TechStrapMetrics.ListenerReconnectsName).ShouldBe(3);
    }

    [Fact]
    public void A_probe_on_one_instance_does_not_see_another_instance()
    {
        using var first = new TechStrapMetrics();
        using var second = new TechStrapMetrics();
        using var probe = new MetricsProbe(first);

        second.ChangeRelayed();
        first.ChangeRelayed();

        probe.Sum(TechStrapMetrics.ChangesRelayedName).ShouldBe(1);
    }

    [Fact]
    public void The_meter_is_observed_only_while_something_listens()
    {
        using var metrics = new TechStrapMetrics();
        metrics.IsObserved.ShouldBeFalse();

        using var probe = new MetricsProbe(metrics);

        metrics.IsObserved.ShouldBeTrue();
    }
}
```

`tests/TechStrap.Api.Tests/Live/LiveMetricsHostTests.cs` (new): the gauge follows real hub connections, and the meter name is registered by the Api and by the Worker (read through `IsObserved`, one host at a time, disposed asynchronously).

```csharp
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Hosting;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Infrastructure.Live;
using TechStrap.Tests.Shared;

namespace TechStrap.Api.Tests.Live;

/// <summary>
/// The metrics at the host: the connected-agents gauge follows real hub connections (distinct agents), and TechStrap's meter is registered with the observability package in the
/// Api and the Worker, so an exporter would actually read it. A meter nobody registers has instruments that nothing ever collects.
/// </summary>
/// <remarks>The registration tests set process environment variables (the exporter options bind before host settings exist), so the class runs in the non-parallel <see cref="ProcessEnvironmentCollection"/>.</remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class LiveMetricsHostTests(TestPostgres postgres)
{
    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(HubTestSupport.Patience);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
        while (!condition())
        {
            await Task.Delay(50, linked.Token);
        }
    }

    [Fact(Timeout = 120000)]
    public async Task The_connected_agents_gauge_counts_distinct_agents_as_hub_connections_come_and_go()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" });
        await TicketTestData.SeedAsync(factory, ct);
        var metrics = factory.Services.GetRequiredService<TechStrapMetrics>();
        await using var samOne = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        await using var samTwo = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        await using var kim = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("kim", "Kim"));
        metrics.ConnectedAgents.ShouldBe(0);

        await samOne.StartAsync(ct);
        await samTwo.StartAsync(ct);
        await UntilAsync(() => metrics.ConnectedAgents == 1);
        await kim.StartAsync(ct);
        await UntilAsync(() => metrics.ConnectedAgents == 2);

        await samOne.StopAsync(ct);
        await Task.Delay(200, ct);
        metrics.ConnectedAgents.ShouldBe(2);
        await samTwo.StopAsync(ct);
        await UntilAsync(() => metrics.ConnectedAgents == 1);
        await kim.StopAsync(ct);
        await UntilAsync(() => metrics.ConnectedAgents == 0);
    }

    [Fact]
    public async Task The_api_and_the_worker_register_the_meter_so_an_enabled_exporter_reads_it_and_it_is_unread_otherwise()
    {
        await using var probe = new OtlpProbe();
        bool apiPlain, workerPlain;
        await using (var plainApi = new ApiFactory())
        {
            apiPlain = plainApi.Services.GetRequiredService<TechStrapMetrics>().IsObserved;
        }

        await using (var plainWorker = new WorkerFactory())
        {
            workerPlain = plainWorker.Services.GetRequiredService<TechStrapMetrics>().IsObserved;
        }

        bool apiExported = false, workerExported = false;
        await OtlpLeakTests.WithOtlpAsync(probe, async () =>
        {
            // One host at a time: a meter provider subscribes to every meter of that name in the process, so a second host alive beside the first would be observed on the first one's registration.
            await using (var api = new ApiFactory())
            {
                apiExported = api.Services.GetRequiredService<TechStrapMetrics>().IsObserved;
            }

            await using (var worker = new WorkerFactory())
            {
                workerExported = worker.Services.GetRequiredService<TechStrapMetrics>().IsObserved;
            }
        });

        apiPlain.ShouldBeFalse();
        workerPlain.ShouldBeFalse();
        apiExported.ShouldBeTrue("the Api must pass the meter name to AddSyntaxCircusObservability");
        workerExported.ShouldBeTrue("the Worker must pass the meter name to AddSyntaxCircusObservability");
    }
}
```

`tests/TechStrap.Infrastructure.IntegrationTests/Live/TicketChangeListenerTests.cs` (modify): the listener host gets a `TechStrapMetrics`, and four tests assert the counters.

```diff
--- a/tests/TechStrap.Infrastructure.IntegrationTests/Live/TicketChangeListenerTests.cs
+++ b/tests/TechStrap.Infrastructure.IntegrationTests/Live/TicketChangeListenerTests.cs
@@ -49,4 +49,5 @@ public sealed class TicketChangeListenerTests(PostgresFixture postgres) : Postgr
                 _provider.GetRequiredService<IServiceScopeFactory>(),
                 Clock,
+                Metrics,
                 Log);
         }
@@ -58,4 +59,6 @@ public sealed class TicketChangeListenerTests(PostgresFixture postgres) : Postgr
         public CapturingLogger Log { get; } = new();
 
+        public TechStrapMetrics Metrics { get; } = new();
+
         public TicketChangeListener Listener { get; }
 
@@ -86,4 +89,5 @@ public sealed class TicketChangeListenerTests(PostgresFixture postgres) : Postgr
     {
         await using var host = new ListenerHost(Database.ConnectionString);
+        using var metrics = new MetricsProbe(host.Metrics);
         await host.StartAsync(TestContext.Current.CancellationToken);
         await WaitForListenerAsync();
@@ -92,4 +96,7 @@ public sealed class TicketChangeListenerTests(PostgresFixture postgres) : Postgr
         await NotifyTestSupport.NotifyAsync(Database.ConnectionString, payload);
         await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 1);
+        await NotifyTestSupport.UntilAsync(() => metrics.Sum(TechStrapMetrics.ChangesRelayedName) >= 1);
+        metrics.Sum(TechStrapMetrics.RelayFailuresName).ShouldBe(0);
+        metrics.Sum(TechStrapMetrics.ListenerReconnectsName).ShouldBe(0);
 
         var change = host.Recorder.Attempts.ShouldHaveSingleItem();
@@ -101,4 +108,5 @@ public sealed class TicketChangeListenerTests(PostgresFixture postgres) : Postgr
     {
         await using var host = new ListenerHost(Database.ConnectionString);
+        using var metrics = new MetricsProbe(host.Metrics);
         await host.StartAsync(TestContext.Current.CancellationToken);
         await WaitForListenerAsync();
@@ -117,4 +125,7 @@ public sealed class TicketChangeListenerTests(PostgresFixture postgres) : Postgr
         var warnings = host.Log.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToList();
         warnings.Count.ShouldBe(5);
+        await NotifyTestSupport.UntilAsync(() => metrics.Sum(TechStrapMetrics.ChangesRelayedName) >= 1);
+        metrics.Sum(TechStrapMetrics.RelayFailuresName).ShouldBe(5);
+        metrics.Sum(TechStrapMetrics.ChangesRelayedName).ShouldBe(1);
         warnings.ShouldAllBe(message => message.Contains("dropped"));
         host.Log.Entries.ShouldAllBe(entry => !entry.Message.Contains("secret-not-json") && !entry.Message.Contains("xxxxx"));
@@ -180,4 +191,5 @@ public sealed class TicketChangeListenerTests(PostgresFixture postgres) : Postgr
     {
         await using var host = new ListenerHost(Database.ConnectionString);
+        using var metrics = new MetricsProbe(host.Metrics);
         await host.StartAsync(TestContext.Current.CancellationToken);
         var first = (await WaitForListenerAsync()).Single();
@@ -194,4 +206,5 @@ public sealed class TicketChangeListenerTests(PostgresFixture postgres) : Postgr
 
         second.ShouldNotBe(first);
+        metrics.Sum(TechStrapMetrics.ListenerReconnectsName).ShouldBe(1);
         host.Recorder.Attempts.ShouldHaveSingleItem().Kind.ShouldBe(TicketChangeKinds.Resync);
         host.Log.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("lost its database connection"));
@@ -209,4 +222,5 @@ public sealed class TicketChangeListenerTests(PostgresFixture postgres) : Postgr
         await NotifyTestSupport.UntilAsync(() => host.Recorder.Attempts.Count >= 3);
         host.Recorder.Attempts.Select(change => change.Kind).ShouldBe([TicketChangeKinds.Resync, TicketChangeKinds.Resync, TicketChangeKinds.Updated]);
+        metrics.Sum(TechStrapMetrics.ListenerReconnectsName).ShouldBe(2);
     }
 
```

`tests/TechStrap.Api.Tests/Live/HubPolicyCoverageTests.cs` (modify): the hub's constructor now also takes the metrics.

```diff
--- a/tests/TechStrap.Api.Tests/Live/HubPolicyCoverageTests.cs
+++ b/tests/TechStrap.Api.Tests/Live/HubPolicyCoverageTests.cs
@@ -68,9 +68,9 @@ public sealed class HubPolicyCoverageTests
 
     [Fact]
-    public void The_hub_depends_only_on_the_presence_handler_and_the_agent_options()
+    public void The_hub_depends_only_on_the_presence_handler_the_agent_options_and_the_metrics()
     {
         var parameters = typeof(TicketHub).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToList();
 
-        parameters.ShouldBe([typeof(IUpdateTicketPresenceHandler), typeof(IOptions<TechStrap.Api.Options.AgentAccessOptions>)], ignoreOrder: true);
+        parameters.ShouldBe([typeof(IUpdateTicketPresenceHandler), typeof(IOptions<TechStrap.Api.Options.AgentAccessOptions>), typeof(TechStrap.Infrastructure.Live.TechStrapMetrics)], ignoreOrder: true);
     }
 
```

- [ ] **Step 2: Run them to verify they fail**

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter-query "/*/TechStrap.Infrastructure.IntegrationTests.Live/*/*"
```

Expected: FAIL to compile:

```text
tests/TechStrap.Infrastructure.IntegrationTests/Live/MetricsProbe.cs(18,25): error CS0246: The type or namespace name 'TechStrapMetrics' could not be found (are you missing a using directive or an assembly reference?)
tests/TechStrap.Infrastructure.IntegrationTests/Live/TicketChangeListenerTests.cs(61,16): error CS0246: The type or namespace name 'TechStrapMetrics' could not be found (are you missing a using directive or an assembly reference?)
Build failed with exit code: 1.
```

- [ ] **Step 3: Write the metrics**

`src/TechStrap.Infrastructure/Live/TechStrapMetrics.cs` (new)

```csharp
using System.Diagnostics.Metrics;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// TechStrap's own instruments (the first custom meter, D-046): how many distinct agents are connected to the hub, how many ticket changes the listener relayed, how many relays
/// failed (a bad payload counts) and how many times the listener got its database connection back. The names are constants because dashboards and alerts depend on them.
/// The meter only reaches an exporter when its name is passed to <c>AddSyntaxCircusObservability</c> (the Api and the Worker do), and then only when OpenTelemetry metrics are on.
/// Handlers never see this class (they may depend on Application abstractions only): the hub and the listener, which are adapters, record.
/// </summary>
public sealed class TechStrapMetrics : IDisposable
{
    public const string MeterName = "TechStrap";
    public const string ConnectedAgentsName = "techstrap.live.connected_agents";
    public const string ChangesRelayedName = "techstrap.live.changes_relayed";
    public const string RelayFailuresName = "techstrap.live.relay_failures";
    public const string ListenerReconnectsName = "techstrap.live.listener_reconnects";

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _subjectByConnection = [];
    private readonly Counter<long> _changesRelayed;
    private readonly Counter<long> _relayFailures;
    private readonly Counter<long> _listenerReconnects;

    public TechStrapMetrics()
    {
        Meter = new Meter(MeterName);
        Meter.CreateObservableGauge(ConnectedAgentsName, () => ConnectedAgents, unit: "{agent}", description: "Distinct agents with at least one open hub connection.");
        _changesRelayed = Meter.CreateCounter<long>(ChangesRelayedName, unit: "{change}", description: "Ticket changes the listener relayed from Postgres to the hub.");
        _relayFailures = Meter.CreateCounter<long>(RelayFailuresName, unit: "{change}", description: "Notifications the listener could not relay (invalid, too large or the hub failed).");
        _listenerReconnects = Meter.CreateCounter<long>(ListenerReconnectsName, unit: "{reconnect}", description: "Times the listener got its database connection back after losing it.");
    }

    internal Meter Meter { get; }

    /// <summary>True while an exporter or listener is subscribed to the counters: how a test proves the meter name was registered.</summary>
    public bool IsObserved => _changesRelayed.Enabled;

    /// <summary>Agents, not connections: an agent with three tabs counts once.</summary>
    public int ConnectedAgents
    {
        get
        {
            lock (_gate)
            {
                return _subjectByConnection.Values.Distinct(StringComparer.Ordinal).Count();
            }
        }
    }

    public void AgentConnected(string connectionId, string subject)
    {
        lock (_gate)
        {
            _subjectByConnection[connectionId] = subject;
        }
    }

    public void AgentDisconnected(string connectionId)
    {
        lock (_gate)
        {
            _subjectByConnection.Remove(connectionId);
        }
    }

    public void ChangeRelayed() => _changesRelayed.Add(1);

    public void RelayFailed() => _relayFailures.Add(1);

    public void ListenerReconnected() => _listenerReconnects.Add(1);

    public void Dispose() => Meter.Dispose();
}
```

`src/TechStrap.Infrastructure/Live/LiveServiceCollectionExtensions.cs` (modify)

```diff
--- a/src/TechStrap.Infrastructure/Live/LiveServiceCollectionExtensions.cs
+++ b/src/TechStrap.Infrastructure/Live/LiveServiceCollectionExtensions.cs
@@ -23,8 +23,19 @@ public static class LiveServiceCollectionExtensions
     public static IServiceCollection AddTechStrapTicketChangeListener(this IServiceCollection services)
     {
+        services.AddTechStrapMetrics();
         services.AddHostedService<TicketChangeListener>();
         return services;
     }
 
+    /// <summary>
+    /// <see cref="TechStrapMetrics"/> as a singleton. The Api and the Worker also pass <see cref="TechStrapMetrics.MeterName"/> to <c>AddSyntaxCircusObservability</c>; without that the instruments
+    /// exist but no exporter ever reads them.
+    /// </summary>
+    public static IServiceCollection AddTechStrapMetrics(this IServiceCollection services)
+    {
+        services.TryAddSingleton<TechStrapMetrics>();
+        return services;
+    }
+
     /// <summary>The Worker's broadcaster: a Postgres NOTIFY after each commit, for the Api's listener. Call it after <c>AddTechStrapPersistence</c>, whose null broadcaster it replaces.</summary>
     public static IServiceCollection AddTechStrapNotifyBroadcaster(this IServiceCollection services)
```

`src/TechStrap.Infrastructure/Live/TicketChangeListener.cs` (modify): the listener records.

```diff
--- a/src/TechStrap.Infrastructure/Live/TicketChangeListener.cs
+++ b/src/TechStrap.Infrastructure/Live/TicketChangeListener.cs
@@ -22,4 +22,5 @@ internal sealed class TicketChangeListener(
     IServiceScopeFactory scopes,
     TimeProvider clock,
+    TechStrapMetrics metrics,
     ILogger<TicketChangeListener> logger) : BackgroundService
 {
@@ -109,4 +110,6 @@ internal sealed class TicketChangeListener(
         if (resync)
         {
+            metrics.ListenerReconnected();
+
             // Whatever was sent while the connection was down is lost, so tell the clients to reload everything.
             await RelayAsync(JsonSerializer.Serialize(TicketChange.Resync(Guid.CreateVersion7(clock.GetUtcNow()), clock.GetUtcNow()).ToDto(), JsonSerializerOptions.Web), cancellationToken);
@@ -129,6 +132,12 @@ internal sealed class TicketChangeListener(
             await using var scope = scopes.CreateAsyncScope();
             var result = await scope.ServiceProvider.GetRequiredService<IRelayTicketChangeHandler>().HandleAsync(new RelayTicketChangeRequest(payload), cancellationToken);
-            if (result.IsFailure)
+            if (result.IsSuccess)
+            {
+                metrics.ChangeRelayed();
+            }
+            else
             {
+                metrics.RelayFailed();
+
                 // The code only: the payload came off a channel anything can write to and is never logged.
                 logger.LogWarning("A ticket change from the database was dropped ({ErrorCode}).", result.Errors[0].Code);
@@ -137,4 +146,5 @@ internal sealed class TicketChangeListener(
         catch (Exception exception) when (exception is not OperationCanceledException)
         {
+            metrics.RelayFailed();
             logger.LogWarning("Relaying a ticket change from the database failed ({ExceptionType}).", exception.GetType().Name);
         }
```

`src/TechStrap.Api/Live/TicketHub.cs` (modify): the hub records connects and disconnects.

```diff
--- a/src/TechStrap.Api/Live/TicketHub.cs
+++ b/src/TechStrap.Api/Live/TicketHub.cs
@@ -7,4 +7,5 @@ using TechStrap.Api.Security;
 using TechStrap.Application.Live;
 using TechStrap.Contracts.Live;
+using TechStrap.Infrastructure.Live;
 
 namespace TechStrap.Api.Live;
@@ -18,8 +19,9 @@ namespace TechStrap.Api.Live;
 /// </summary>
 [Authorize(Policy = AuthorizationPolicies.Agent)]
-public sealed class TicketHub(IUpdateTicketPresenceHandler presence, IOptions<AgentAccessOptions> access) : Hub
+public sealed class TicketHub(IUpdateTicketPresenceHandler presence, IOptions<AgentAccessOptions> access, TechStrapMetrics metrics) : Hub
 {
     public override async Task OnConnectedAsync()
     {
+        metrics.AgentConnected(Context.ConnectionId, Subject());
         await Groups.AddToGroupAsync(Context.ConnectionId, TicketHubGroups.Queue, Context.ConnectionAborted);
         await base.OnConnectedAsync();
@@ -61,4 +63,6 @@ public sealed class TicketHub(IUpdateTicketPresenceHandler presence, IOptions<Ag
     public override async Task OnDisconnectedAsync(Exception? exception)
     {
+        metrics.AgentDisconnected(Context.ConnectionId);
+
         // The connection is already gone, so its abort token is spent: cleaning up must not depend on it. SignalR removes the connection from its groups itself.
         await presence.HandleAsync(Request(TicketPresenceActions.LeaveAll, null), CancellationToken.None);
@@ -67,5 +71,7 @@ public sealed class TicketHub(IUpdateTicketPresenceHandler presence, IOptions<Ag
 
     private UpdateTicketPresenceRequest Request(string action, Guid? ticketId, bool isComposing = false) =>
-        new(action, ClaimsCurrentAgentClaims.FromPrincipal(Context.User!, access.Value)?.Subject ?? string.Empty, Context.ConnectionId, ticketId, isComposing);
+        new(action, Subject(), Context.ConnectionId, ticketId, isComposing);
+
+    private string Subject() => ClaimsCurrentAgentClaims.FromPrincipal(Context.User!, access.Value)?.Subject ?? string.Empty;
 
     /// <summary>Only the fixed texts of the handler's outcomes reach the client; an unknown ticket is the one the spec names.</summary>
```

`src/TechStrap.Api/Live/LiveHubExtensions.cs` (modify)

```diff
--- a/src/TechStrap.Api/Live/LiveHubExtensions.cs
+++ b/src/TechStrap.Api/Live/LiveHubExtensions.cs
@@ -17,4 +17,5 @@ public static class LiveHubExtensions
         services.AddSignalR();
         services.AddTechStrapPresence();
+        services.AddTechStrapMetrics();
         services.Replace(ServiceDescriptor.Singleton<ITicketChangeBroadcaster, SignalRTicketChangeBroadcaster>());
         services.AddTechStrapTicketChangeListener();
```

`src/TechStrap.Api/Program.cs` (modify): the meter name is what makes the exporter read it.

```diff
--- a/src/TechStrap.Api/Program.cs
+++ b/src/TechStrap.Api/Program.cs
@@ -15,4 +15,5 @@ using TechStrap.Hosting.Sentry;
 using TechStrap.Hosting.Wiring;
 using TechStrap.Infrastructure.Intake;
+using TechStrap.Infrastructure.Live;
 using TechStrap.Infrastructure.Persistence;
 using TechStrap.Infrastructure.Security;
@@ -28,5 +29,6 @@ if (builder.Configuration.ShouldLoadDotEnv(builder.Environment))
 }
 
-var telemetry = builder.AddSyntaxCircusObservability(ServiceName);
+// TechStrap's own meter is exported only when its name is passed here.
+var telemetry = builder.AddSyntaxCircusObservability(ServiceName, [TechStrapMetrics.MeterName]);
 builder.AddStandardSerilog(configureEnrichment: logger =>
 {
```

`src/TechStrap.Worker/Program.cs` (modify)

```diff
--- a/src/TechStrap.Worker/Program.cs
+++ b/src/TechStrap.Worker/Program.cs
@@ -25,5 +25,5 @@ if (builder.Configuration.ShouldLoadDotEnv(builder.Environment))
 }
 
-var telemetry = builder.AddSyntaxCircusObservability(ServiceName);
+var telemetry = builder.AddSyntaxCircusObservability(ServiceName, [TechStrapMetrics.MeterName]);
 builder.AddStandardSerilog(configureEnrichment: logger =>
 {
@@ -49,4 +49,5 @@ builder.Services.AddTechStrapPersistence();
 // A committed change is sent as a Postgres NOTIFY for the Api to relay to its hub (D-018). After AddTechStrapPersistence, whose null broadcaster this replaces.
 builder.Services.AddTechStrapNotifyBroadcaster();
+builder.Services.AddTechStrapMetrics();
 builder.Services.AddTechStrapEmail(builder.Configuration);
 builder.Services.AddHostedService<EmailOutboxWorker>();
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter-query "/*/TechStrap.Infrastructure.IntegrationTests.Live/*/*"
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/TechStrap.Api.Tests.Live/*/*"
```

Expected: PASS (69 and 27):

```text
  total: 69
  succeeded: 69
  total: 27
  succeeded: 27
```

- [ ] **Step 5: Prove each pin with a recorded mutation**

Save `$T\specs\m5.py`:

```python
"""Mutations of Task 5 (metrics). Run from the repository root."""
INFRA = ["dotnet", "test", "--project", "tests/TechStrap.Infrastructure.IntegrationTests", "-c", "Release", "--filter-query"]
API = ["dotnet", "test", "--project", "tests/TechStrap.Api.Tests", "-c", "Release", "--filter-query"]
METRICS_TESTS = INFRA + ["/*/*/TechStrapMetricsTests/*"]
LISTENER_TESTS = INFRA + ["/*/*/TicketChangeListenerTests/*"]
HOST_TESTS = API + ["/*/*/LiveMetricsHostTests/*"]
M = "src/TechStrap.Infrastructure/Live/TechStrapMetrics.cs"
L = "src/TechStrap.Infrastructure/Live/TicketChangeListener.cs"
HUB = "src/TechStrap.Api/Live/TicketHub.cs"

MUTATIONS = [
    ("1 the gauge counts connections, not agents", M, [("_subjectByConnection.Values.Distinct(StringComparer.Ordinal).Count()", "_subjectByConnection.Count")], METRICS_TESTS),
    ("2 a disconnect is forgotten", M, [("            _subjectByConnection.Remove(connectionId);\n", "            _ = connectionId;\n")], METRICS_TESTS),
    ("3 a relayed change counts twice", M, [("public void ChangeRelayed() => _changesRelayed.Add(1);", "public void ChangeRelayed() => _changesRelayed.Add(2);")], METRICS_TESTS),
    ("4 a failure counts as a relay", M, [("public void RelayFailed() => _relayFailures.Add(1);", "public void RelayFailed() => _changesRelayed.Add(1);")], METRICS_TESTS),
    ("5 a metric name changes", M, [('ChangesRelayedName = "techstrap.live.changes_relayed"', 'ChangesRelayedName = "techstrap.live.relayed"')], METRICS_TESTS),
    ("6 the listener never counts a relay", L, [("                metrics.ChangeRelayed();\n", "")], LISTENER_TESTS),
    ("7 the listener never counts a dropped payload", L, [("                metrics.RelayFailed();\n\n                // The code only", "                // The code only")], LISTENER_TESTS),
    ("8 the listener never counts a reconnect", L, [("            metrics.ListenerReconnected();\n\n", "")], LISTENER_TESTS),
    ("9 the api does not register the meter name", "src/TechStrap.Api/Program.cs", [("AddSyntaxCircusObservability(ServiceName, [TechStrapMetrics.MeterName])", "AddSyntaxCircusObservability(ServiceName, [TechStrapMetrics.MeterName + \"-other\"])")], HOST_TESTS),
    ("10 the worker does not register the meter name", "src/TechStrap.Worker/Program.cs", [("AddSyntaxCircusObservability(ServiceName, [TechStrapMetrics.MeterName])", "AddSyntaxCircusObservability(ServiceName, [TechStrapMetrics.MeterName + \"-other\"])")], HOST_TESTS),
    ("11 the hub never counts a connection", HUB, [("        metrics.AgentConnected(Context.ConnectionId, Subject());\n", "")], HOST_TESTS),
    ("12 the hub never forgets a connection", HUB, [("        metrics.AgentDisconnected(Context.ConnectionId);\n\n", "")], HOST_TESTS),
]
```

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | the gauge counts connections, not agents | `TechStrapMetrics.cs` | KILLED (1 failing) |
| 2 | a disconnect is forgotten | `TechStrapMetrics.cs` | KILLED (1 failing) |
| 3 | a relayed change counts twice | `TechStrapMetrics.cs` | KILLED (2 failing) |
| 4 | a failure counts as a relay | `TechStrapMetrics.cs` | KILLED (1 failing) |
| 5 | a metric name changes | `TechStrapMetrics.cs` | KILLED (1 failing) |
| 6 | the listener never counts a relay | `TicketChangeListener.cs` | KILLED (2 failing) |
| 7 | the listener never counts a dropped payload | `TicketChangeListener.cs` | KILLED (1 failing) |
| 8 | the listener never counts a reconnect | `TicketChangeListener.cs` | KILLED (1 failing) |
| 9 | the api does not register the meter name | `Program.cs` | KILLED (1 failing) |
| 10 | the worker does not register the meter name | `Program.cs` | KILLED (1 failing) |
| 11 | the hub never counts a connection | `TicketHub.cs` | KILLED (1 failing) |
| 12 | the hub never forgets a connection | `TicketHub.cs` | KILLED (1 failing) |

Run them (two foreground batches, `1`..`8` and `9`..`12`). Mutation 10 first survived: the registration test started the Api and the Worker host side by side, and a meter provider subscribes to every meter of that name in the process, so the Worker's instruments were observed on the Api's registration. The test now starts one host at a time and disposes each with `await using` (a plain `using` was not enough), and the mutation then died.

- [ ] **Step 6: Run the full suites and commit**

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: all pass (793 and 959 tests, and 295).

```bash
git add -A
git diff --cached --stat
git commit -m "feat: PHASE-10a live metrics: connected agents, relayed changes, relay failures, listener reconnects (D-046)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

### Task 6: Close-out: the runbook, the spec ticks, the roadmap row and the package map

**Review Focus pin:** none new; this task records what 10a delivered and what it did not (T11 to T17 and the Admin deliverables stay open), and tells the operator the two things that matter: the hub is internal and `LISTEN` needs a direct connection.

**Files:**

- Modify: `docs/self-hosting/DEPLOYMENT.md`, `docs/architecture/PHASE-10-live-updates.md`, `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`, `docs/architecture/00-DISCOVERY-INDEX.md`, `docs/architecture/03-PACKAGE-MAP.md`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`

**Interfaces:**
- Consumes: `$T\docs_10a.py` from Task 1 (its `6` mode), the Pester file as Task 1 left it.
- Produces: P10-T01 to T10 and the three server deliverables ticked (T11 to T17 and the Admin and reverse-proxy deliverables stay unticked), the row 10 status "10a complete (pending merge): the server (...); 10b waits on the Blazor.Auth package" in the roadmap and the discovery index, the DEPLOYMENT.md section "Live updates (PHASE-10)", the package-map notes, and the Pester pins for all of them.

- [ ] **Step 1: Write the failing Pester pins**

Save `$T\pester_t6.py`, which appends four pins to the `D-046` Describe:

```python
"""Task 6: the Pester pins for the PHASE-10a close-out (ticks, the roadmap row, the runbook, the package map). Run from the repository root."""
PATH = "scripts/tests/RepositoryDocs.Tests.ps1"
text = open(PATH, encoding="utf-8", newline="").read()
crlf = "\r\n" in text
text = text.replace("\r\n", "\n").rstrip("\n")
assert text.endswith("}")
text = text[:-1].rstrip("\n") + """

    It 'ticks only what 10a delivers: P10-T01 to T10 and the three server deliverables, with the Admin work still open' {
        $spec = Get-RepoText 'docs/architecture/PHASE-10-live-updates.md'
        foreach ($number in 1..10) {
            $id = 'P10-T{0:00}' -f $number
            $spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is delivered by 10a"
        }
        foreach ($number in 11..17) {
            $id = 'P10-T{0:00}' -f $number
            $spec | Should -Match ('(?m)^- \[ \] \*\*' + $id + '\*\*') -Because "$id is 10b"
        }
        $spec | Should -Match '(?m)^- \[x\] `TicketHub`, `UpdateTicketPresenceHandler`'
        $spec | Should -Match '(?m)^- \[x\] Post-commit publishing hook in Infrastructure'
        $spec | Should -Match '(?m)^- \[x\] Contracts: `TicketChangedDto`'
        $spec | Should -Match '(?m)^- \[ \] Admin `ITicketLiveClient`'
        $spec | Should -Match '(?m)^- \[ \] Reverse-proxy/WebSocket notes'
    }

    It 'says in the roadmap and discovery rows that 10a is complete pending merge and 10b waits on the Blazor.Auth package' {
        foreach ($file in 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md', 'docs/architecture/00-DISCOVERY-INDEX.md') {
            (Get-RepoText $file) | Should -Match '(?m)^\| 10 \|.*\| 10a complete \(pending merge\):.*10b waits on the Blazor\.Auth package'
        }
    }

    It 'tells the operator that the hub is internal and that LISTEN needs a direct connection' {
        $runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md'
        $runbook | Should -Match '(?m)^## Live updates \(PHASE-10\)'
        foreach ($phrase in 'The hub is internal', 'PgBouncer in transaction mode breaks `LISTEN`', 'techstrap-ticket-change-listener', 'Do not publish `/hubs`', 'No new settings') {
            $runbook | Should -Match ([regex]::Escape($phrase)) -Because "the live-updates note must say $phrase"
        }
    }

    It 'records the two packages the live updates use' {
        $map = Get-RepoText 'docs/architecture/03-PACKAGE-MAP.md'
        $map | Should -Match 'TechStrap\.Api\.Tests` connects a real `HubConnection` in 10a'
        $map | Should -Match 'A direct reference of `TechStrap\.Infrastructure` \(10a, D-046\)'
    }
}
"""
open(PATH, "w", encoding="utf-8", newline="").write(text.replace("\n", "\r\n") if crlf else text + "\n")
```

```bash
python $T/pester_t6.py
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected: FAIL (the four new pins):

```text
[-] D-046 (live updates).ticks only what 10a delivers: P10-T01 to T10 and the three server deliverables, with the Admin work still open 33ms
[-] D-046 (live updates).says in the roadmap and discovery rows that 10a is complete pending merge and 10b waits on the Blazor.Auth package 17ms
[-] D-046 (live updates).tells the operator that the hub is internal and that LISTEN needs a direct connection 12ms
[-] D-046 (live updates).records the two packages the live updates use 11ms
Tests Passed: 52, Failed: 4, Skipped: 0, Inconclusive: 0, NotRun: 0
```

- [ ] **Step 2: Write the documentation**

```bash
python $T/docs_10a.py 6
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1
```

The script ticks T01 to T10 (T06 as shrunk by the correction to "the query token is refused", T08 for the one Worker writer there is) and the three server deliverables, writes the row 10 status, adds the runbook section and the two package-map notes. The runbook section it adds reads:

> **Live updates (PHASE-10).** The hub is internal: the Admin connects to the Api's `/hubs/tickets` server to server, a browser never does, so the reverse proxy needs no WebSocket rule for it; do not publish `/hubs` on the public host; the hub takes the token in the `Authorization` header only. `LISTEN` needs a direct Postgres connection: PgBouncer in transaction mode breaks it; the connection is named `techstrap-ticket-change-listener` in `pg_stat_activity`; run one Api instance (D-007). No new settings.

Expected: PASS:

```text
Tests Passed: 56, Failed: 0, Skipped: 0, Inconclusive: 0, NotRun: 0
```

- [ ] **Step 3: Prove each pin with a recorded mutation**

Save `$T\specs\m6.py`:

```python
"""Mutations of Task 6's documentation pins. Run from the repository root."""
PESTER = ["pwsh", "-NoProfile", "-File", "scripts/Invoke-ScriptTests.ps1", "-Output", "Minimal", "-Path", "scripts/tests/RepositoryDocs.Tests.ps1"]
SPEC = "docs/architecture/PHASE-10-live-updates.md"
RUNBOOK = "docs/self-hosting/DEPLOYMENT.md"

MUTATIONS = [
    ("1 a delivered task is left unticked", SPEC, [("- [x] **P10-T05**", "- [ ] **P10-T05**")], PESTER),
    ("2 a 10b task is ticked", SPEC, [("- [ ] **P10-T11**", "- [x] **P10-T11**")], PESTER),
    ("3 the Admin deliverable is ticked", SPEC, [("- [ ] Admin `ITicketLiveClient`", "- [x] Admin `ITicketLiveClient`")], PESTER),
    ("4 the roadmap row loses the 10b dependency", "docs/architecture/99-IMPLEMENTATION-ROADMAP.md", [("10b waits on the Blazor.Auth package", "10b is next")], PESTER),
    ("5 the discovery row loses the 10b dependency", "docs/architecture/00-DISCOVERY-INDEX.md", [("10b waits on the Blazor.Auth package", "10b is next")], PESTER),
    ("6 the runbook stops naming PgBouncer", RUNBOOK, [("**PgBouncer in transaction mode breaks `LISTEN`**", "**pooling can break `LISTEN`**")], PESTER),
    ("7 the runbook says the hub is public", RUNBOOK, [("**The hub is internal.**", "**The hub is public.**")], PESTER),
    ("8 the runbook drops the do-not-publish warning", RUNBOOK, [(" Do not publish `/hubs` on the public host.", "")], PESTER),
    ("9 the package map forgets the hub client test", "docs/architecture/03-PACKAGE-MAP.md", [("`TechStrap.Api.Tests` connects a real `HubConnection` in 10a (D-046)", "`TechStrap.Api.Tests` connects (D-046)")], PESTER),
]
```

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | a delivered task is left unticked | `PHASE-10-live-updates.md` | KILLED |
| 2 | a 10b task is ticked | `PHASE-10-live-updates.md` | KILLED |
| 3 | the Admin deliverable is ticked | `PHASE-10-live-updates.md` | KILLED |
| 4 | the roadmap row loses the 10b dependency | `99-IMPLEMENTATION-ROADMAP.md` | KILLED |
| 5 | the discovery row loses the 10b dependency | `00-DISCOVERY-INDEX.md` | KILLED |
| 6 | the runbook stops naming PgBouncer | `DEPLOYMENT.md` | KILLED |
| 7 | the runbook says the hub is public | `DEPLOYMENT.md` | KILLED |
| 8 | the runbook drops the do-not-publish warning | `DEPLOYMENT.md` | KILLED |
| 9 | the package map forgets the hub client test | `03-PACKAGE-MAP.md` | KILLED |

Every mutation is killed.

- [ ] **Step 4: Run the whole verification and commit**

```bash
dotnet build TechStrap.slnx -c Release --no-incremental
dotnet test --solution TechStrap.CI.slnf -c Release
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build
docker ps
```

Expected: 0 warnings and 0 errors; the CI suite passes; the script tests pass (the package-version check and the config contract included, since no package, setting or compose file changed in a way they do not already accept); no model changes; no Testcontainers container left:

```text
    0 Warning(s)
    0 Error(s)
```
```text
Test run summary: Passed!
  total: 7064
  succeeded: 7064
```
```text
  [+] tracks no font binaries or restored font licences 32ms
  [+] loads no font, script or style from a CDN at runtime 548ms
Tests completed in 46.79s
Tests Passed: 328, Failed: 0, Skipped: 0, Inconclusive: 0, NotRun: 0
```
```text
No changes have been made to the model since the last migration.
```

```bash
git add -A
git diff --cached --stat
git commit -m "docs: PHASE-10a close-out: spec ticks, roadmap row, runbook note, package map (D-046)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

## Self-review (done while drafting)

- **Spec coverage.** T01 (contracts, round-trip tests): Task 1. T02 (abstractions, store, TTL, contention): Tasks 1 and 2. T03 (presence handler): Task 1. T04 (relay handler): Task 1. T05 (hub, broadcaster): Task 3. T06 (query token): Task 3, as the pin that it is refused. T07 (post-commit hook): Task 2. T08 (NOTIFY broadcaster): Task 4. T09 (listener): Task 4. T10 (metrics): Task 5. The spec's Success Criteria that concern the server (worker change reaches the hub, recovery with a resync, unauthenticated or non-agent refused, no message content, no notification for a rolled-back transaction, a failing broadcast never fails a request, no migration) each have a test named in the tasks above. The Admin criteria and T11 to T17 are 10b.
- **Placeholder scan.** No step says "TBD" or "similar to Task N"; every code step shows the file as committed and every command shows its recorded output.
- **Type consistency.** The names used in later tasks (`PresenceChange`, `TicketChange.Resync`, `TicketHubMessages`, `TicketChangeNotify.ListenerApplicationName`, `TechStrapMetrics.IsObserved`, `RecordingBroadcaster`) are the ones defined in the tasks that produce them.
- **Review Focus.** Each of the five has its pin named in its line above.
