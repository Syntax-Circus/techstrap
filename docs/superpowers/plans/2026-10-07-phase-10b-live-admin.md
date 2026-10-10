# PHASE-10b Live Updates in the Admin: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Blazor Server Admin consume the PHASE-10a hub, in one pull request (10b of two): a live connection per circuit, a connection indicator, a queue banner, the detail page's "New activity - refresh" banner, a presence bar and the composing hint, behind one kill switch, with an end-to-end test from the Worker's auto-close to the Admin's client.
- **The client:** `SignalRTicketLiveClient`, scoped per circuit, started once by `LiveConnectionIndicator` after its first render (never during prerendering), with the agent's token from `IUserAccessTokenProvider` (SyntaxCircus.Blazor.Auth 0.2.0) in the header, a reconnect policy that never gives up, a re-join and a resync after every reconnect, and de-duplication by event id.
- **The screens:** a polite status indicator in `MainLayout`; one queue banner per one-second window; the detail banner that never touches the row version or the draft; a presence bar and a throttled "replying" hint.
- **The switch and the proof:** `LiveUpdates:Enabled` (`LIVEUPDATES__ENABLED`) with the four edits of D-043; `AdminLiveClientHostTests` runs Postgres, the Worker's real auto-close, NOTIFY, the Api's real listener and hub and the Admin's real client in one process; ADMIN-APP.md and the D-046 addendum close the phase.

**Architecture:**
- **Components depend on `ITicketLiveClient` only.** The real client sits on a small `ILiveConnection` abstraction (`HubLiveConnection` wraps `HubConnection`), so its logic (state, de-duplication, re-joining, disposal, the token, the retry) is tested with a scripted connection and also against the real hub. Components marshal events with `InvokeAsync`; the client never does.
- **One owner of the start.** `LiveConnectionIndicator` is the only component that calls `StartAsync`. `MainLayout` is outside `AgentGate`, so the indicator waits for `AgentSession` to be Ready (`Session.Changed`) and starts from `OnAfterRender`, which prerendering never runs.
- **A live change is a prompt, not an update.** The pages never reload, reorder or take a new row version by themselves. The agent's own changes are ignored by one shared rule (`LiveChangeRules.IsOwn`); a `Resync` is never own.

**Tech Stack:** .NET 10, Blazor Server, SignalR client (`Microsoft.AspNetCore.SignalR.Client` 10.0.12, already pinned), `SyntaxCircus.Blazor.Auth` 0.2.0, xUnit v3, Shouldly, NSubstitute, bUnit, `FakeTimeProvider`, Testcontainers (Postgres) and Pester.

**Spec:** `docs/architecture/PHASE-10-live-updates.md` (T11 to T17, the Corrections block and the Success Criteria that concern the Admin), `docs/architecture/04-DECISION-LOG.md` (D-046 and its Known limits, D-043 for the setting), `docs/development/ADMIN-APP.md`, and the owner decisions of 2026-10-07 (the PHASE-10b scope plan, "Owner decisions"), recorded as an **addendum of D-046** in Task 5.

### Owner decisions (2026-10-07), recorded as an addendum of D-046
1. **One kill switch.** `LiveUpdates:Enabled` (`LIVEUPDATES__ENABLED`, default `true`), with the four edits of D-043. Off, a no-op client is used: no connection, no indicator or banners, and the pages behave exactly as today. The hub address is `API__BASEURL` plus `TicketHubRoutes.Path`; backoff and debounce are constants.
2. **T17.** An automated in-process integration test, plus a short manual real-IdP checklist in ADMIN-APP.md. The compose smoke script is unchanged.
3. **Own changes are ignored.** A `TicketChanged` whose `ActorAgentId` is the signed-in agent raises no banner. Another tab still hits the existing 409 on send.
4. **The queue banner.** One "Queue updated - refresh" banner for any non-own change or `Resync`, debounced to one second. Nothing is reordered or reloaded until the agent clicks it.
5. **Carried from D-046.** The detail banner does not take the new row version until clicked (a send before that gets the 409); the draft is never touched; the presence name is `Agent.Name`, to other agents only, and the agent's own entry is excluded by `AgentSession.Agent.Id`.

### Decisions made while drafting (the D-046 addendum records them)
- **The decisions log: an addendum of D-046, not D-047.** The log's own pattern for a later phase of an approved decision is an addendum under the same number ("Addendum (2026-10-06, PHASE-09c ...)", "(2026-10-06, PHASE-09d ...)" under D-045; each says "There is no new decision number"). The Pester pins hold D-046's header bullet and index row, which stay unchanged. The addendum states that "No new setting" in D-046 now holds for the Api and the Worker only.
- **Task split.** The brief's five tasks, kept: (1) package bump, kill switch and the client core with its unit tests; (2) the indicator, the start and the queue banner; (3) the detail page, presence and the composing hint; (4) the T17 test; (5) the close-out. Each is RED, GREEN, a recorded mutation run and a commit.
- **The spikes, proved in a scratch clone before the tasks were written** (every finding is now a durable test):
  - **(a) The start point.** The pages prerender (`App.razor:33`, `InteractiveServer`): the first render runs on the HTTP request with no circuit, and `OnAfterRenderAsync` does not run there. Starting from `OnAfterRender` of the layout-level indicator never starts during a prerender (a host test with the real client and a counting connection factory GETs `/` and `/queue/mine` and finds no connection created), starts exactly once however many renders and state changes follow (the client's `_starting ??=` guard, plus the indicator's own flag), and only when `AgentSession` is Ready (`Session.Changed` re-renders it). The owner is the indicator, not a new `LiveConnectionHost`: it is already the one component that must exist in the layout, outside `AgentGate`. A real browser circuit is not exercised (Playwright is deferred, D-040): "one connection per circuit" rests on the guard, the single starter and the layout being mounted once per circuit; the manual checklist covers it.
  - **(b) The token.** `IUserAccessTokenProvider` is `ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)`, scoped, null for an anonymous or lapsed user, throwing when a refresh fails and the token has expired (the 0.2.0 package ships without an XML file; the signature is confirmed by the compile and by the package README in the NuGet cache). `HubConnectionBuilder.WithUrl(uri, o => o.AccessTokenProvider = ...)` calls it on every start and every reconnect, and puts the token in the `Authorization` header only. A null token sets a flag that stops the start loop and the retry policy (a hub request without a token is refused, so every retry would be); a throw is logged by type and retried. The existing named Api `HttpClient`s add the auth handler and a retry and are not used for the hub: `HubLiveConnectionFactory` is a singleton that holds the validated `Api` options only, builds `Api:BaseUrl` plus the hub path (a base path survives) and has one test seam, an `Action<HttpConnectionOptions>` for `HttpMessageHandlerFactory` and the transport.
  - **(c) The testable seam.** `ITicketLiveClient` for components, `ILiveConnection` under the client. `FakeTicketLiveClient` is registered once in `AdminComponentTest`'s constructor, with a default signed-in `AgentSession`; 64 test classes derive from `AdminComponentTest` or `BunitContext` and exactly one of the latter (`LayoutResilienceTests`) renders the layout without the base, so it registers the fake itself: the "one edit" was two.
  - **(d) The T17 topology.** `TechStrap.Api.Tests` references the Api, the Worker and the Admin, and the Architecture rules constrain the source projects only (`AdminRules.AllowedPackages`, `ProjectReferenceDirectionTests`), so one test there wires Testcontainers Postgres, `WorkerFactory`'s real `IAutoCloseSolvedTicketsHandler` and NOTIFY publisher, `ApiFactory`'s real listener and hub (the 10a `NotifyRelayHostTests` pattern), and the Admin's real `SignalRTicketLiveClient` through `HubLiveConnectionFactory` with `HttpMessageHandlerFactory = api.Server.CreateHandler()`, long polling and a fixed test JWT. The Solved ticket is solved through the real endpoint and backdated with one SQL update (`solved_at`), because the Worker's `Days` setting cannot go below one day. A barrier message (a later NOTIFY) proves no second copy arrives. Real-hub tests also pin an unknown ticket, a missing token and a token that expires (the server closes the connection, the reconnect asks for a fresh token and a `Resync` follows).
  - **(e) The kill switch.** `LiveUpdatesOptions` is bound from the `LiveUpdates` section with `ValidateOnStart` (a value that is not a boolean stops the start, like the other Admin options); a scoped factory registration returns the real client or `NullTicketLiveClient`. The D-043 edits: `appsettings.json`, `src/TechStrap.Admin/.env.example`, `deploy/.env.admin.example` and the ConfigContract pin. **Compose is not edited**: an `environment:` entry would override the operator's env file (the brief listed "the compose admin env", but the existing pin keeps the local compose's Admin keys to exactly five, and a setting the operator owns belongs in `.env.admin`); the new pin asserts the switch is `true` in both templates and set by neither compose file.
- **Deviations from the brief, each with its reason.**
  - **The queue "debounce" is a window, not a restarted timer.** The brief says the `QueueFilterBar` timer pattern, which restarts on every keystroke. A queue that keeps changing would never show a banner, so the first change opens a one-second window and everything in it joins that banner; `CreateTimer`, `CountingTimeProvider` and `FakeTimeProvider` are used as asked.
  - **An emptied text box also clears the composing hint**, and so does a ticket change in the composer. The brief lists blur, submit and dispose; "replying" on an empty box would be wrong.
  - **A "replying" hint lapses on the page's own timer.** The server's lease is 10 seconds and the hub pushes nothing when it lapses, so the page shows "viewing" 10 seconds after the last presence message that carried a replier.
  - **`ILiveConnection` and `ILiveConnectionFactory`** exist so the client can be unit-tested with a fake connection (the spec's T11 validation asks for exactly that); `SignalRTicketLiveClient` and `HubLiveConnectionFactory` are public so `TechStrap.Api.Tests` can build the real client.
  - **A null token does not call `SessionExpiry.Report()`.** A hub that cannot authenticate shows "Offline" and stops; the session-expired banner comes with the next REST call. Reporting would turn an auxiliary connection into a sign-out. This is listed in "Known gaps in 10b".
  - **`ChangedTicketBanner` and `QueueLiveBanner` are inline** (one `EventCallback` parameter each); `LiveConnectionIndicator` and `TicketPresenceBar` are paired or tiny; `PresenceViewModelFactory` is the only factory. No inline script, style or `on*` attribute (`@onblur` is the Razor directive).
  - **No new Contracts type and no new package** beyond the SignalR client in the Admin and the Blazor.Auth bump.
- **Mutants that survived the first draft of the tests, and what killed them.** Task 2: a second change after the banner was shown started another timer (the burst test now waits for the renderer's queue to drain with `await cut.InvokeAsync(() => { })` and asserts no timer). Task 3: presence of another ticket was applied and then replaced (the test now checks the text between the two updates); a failing join was not logged (the failing-client test now asserts the "Joining" and "Leaving" log lines). Task 4: one run reported a survivor because an interrupted batch had left a stale build; see the hazard below.
- **A mutation-run hazard that happened (again).** A batch started with a `timeout` shorter than the batch was killed in the middle of a mutation: the source stayed mutated and the next run reported a false "SURVIVED" from a stale build. After any interrupted run, `git status` must show nothing but staged files (`git checkout -- <file>` restores one), and the mutation is run again alone. Keep a batch to about a dozen mutations, run it in the foreground and give the shell call the full ten minutes.
- **Honest limits.** No real identity provider and no real browser circuit are exercised; the manual checklist in ADMIN-APP.md is the owner's. The reconnect has no jitter. `LiveConnectionIndicator` starts the client without awaiting it, so a start that never ends (it does not: the loop ends on success, a lapse, no token or a disposal) would simply stay "Connecting". The client's de-duplication memory is 256 ids; an id older than that is delivered again, which a page treats as one more reason to offer a refresh.
- **No migration.** `dotnet ef migrations has-pending-model-changes` is clean (Task 5, last step).
- **Shared files between tasks** (the tasks run in order, so the overlaps are safe): `AdminComponentTest.cs` (Tasks 1 and 2), `LiveCopy.cs` (Tasks 2 and 3, the throttle constant is declared with the window), `_live.scss` (Tasks 2 and 3), `LiveTestData.cs` (Tasks 1 and 3), `04-DECISION-LOG.md`, `PHASE-10-live-updates.md`, `99-IMPLEMENTATION-ROADMAP.md`, `00-DISCOVERY-INDEX.md` and `RepositoryDocs.Tests.ps1` (Task 5 only; Task 1 touches `03-PACKAGE-MAP.md` and `ConfigContract.Tests.ps1`).

## Global Constraints

- **Build.** .NET SDK 10.0.401 targeting `net10.0`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild`. Private fields are `_camelCase`, constants are PascalCase, namespaces are file-scoped.
- **Packages.** `SyntaxCircus.Blazor.Auth` 0.1.7 to 0.2.0 (`Directory.Packages.props` and `03-PACKAGE-MAP.md` in the same commit; `scripts/Check-PackageVersions.ps1` fails when they disagree). `TechStrap.Admin` references the already-pinned `Microsoft.AspNetCore.SignalR.Client` (a `PackageReference` with no version, plus `AdminRules.AllowedPackages`, in the same commit). Nothing else is new. A restore fetches 0.2.0 from nuget.org.
- **Migrations.** None.
- **Config (D-043).** Exactly one new setting, `LiveUpdates:Enabled`, with its edits: `src/TechStrap.Admin/appsettings.json`, `src/TechStrap.Admin/.env.example`, `deploy/.env.admin.example` and `scripts/tests/ConfigContract.Tests.ps1`. Compose does not set it.
- **Architecture rules.**
  - The Admin references Contracts and Hosting only, and only the packages in `AdminRules.AllowedPackages`.
  - No `HttpClient` or `IHttpClientFactory` in a `.razor` or `.razor.cs`; the live client is a plain `.cs`. No inline `<script>`, `<style>` or raw `on*` attribute in markup (use `@onblur`, `@onclick`).
  - Razor components are public classes; a view model is feature-local; copy lives in `*Copy` constant classes; a repeated number is a named constant (the one-second window and the four-second throttle are `LiveDefaults`).
  - Reads pass the component's `CancellationToken`; the live calls take none that matters.
  - Hub path, method and group names come from the Contracts constants (`TicketHubRoutes.Path`, `TicketHubMethods`, `TicketHubMessages`); no string literal repeats them.
- **Secrets and PII.** The token travels in the `Authorization` header only: never in a URL, a log or a message. Every live failure is logged by exception type only, never a message (it could carry a token, a name or an address); the Api's `AdminLeakTests` scan every log level. A presence name is `Agent.Name`, shown to other agents only.
- **Isolation.** Any live-client failure, whether start, token, invoke or disposal, is logged and degrades to the "Reconnecting" or "Offline" indicator. It never throws into a page, a composer or a circuit.
- **Encoding.** Write non-ASCII in C# copy as `\u` escapes (the en dash of the banners is `\u2013`); `SourceEncodingTests` reads every source file as strict UTF-8. New files are written with LF; the working tree of this repository has CRLF in existing files, so an edit to an existing file is made with a tool that matches `\n` and keeps the file's endings (`edit.py` below, or the editor's own replace); git normalizes either way.
- **Tests.**
  - Failing test first, with RED and GREEN recorded. A task whose tests cover behavior that already exists (Task 4) records RED as the failure of a mutation that breaks the chain.
  - Prove each pin with a recorded mutation; every mutation must keep the Release build compiling (avoid `if (false)`: CS0162; do not leave a variable unused: CS0219 or IDE0059 is an error). A mutation that does not compile is reported by `mut.py` as "NOT A MUTATION" (exit code 4).
  - Every test that waits carries `Timeout = ...` (xUnit1069 then wants `Xunit.TestContext.Current.CancellationToken` in the body, and xUnit1051 wants it passed to every call that takes a token; inside a bUnit class write `Xunit.TestContext`, because `Bunit.TestContext` is ambiguous). No tight wall-clock assertion: time is a `FakeTimeProvider`, a timer is counted with `CountingTimeProvider`, and a real wait is a bounded poll (a ceiling, not a measurement).
  - **A negative on a SignalR connection is proved with a barrier message**, never a sleep: a later message on the same connection arrives after every earlier one. In 10a we learned that the .NET client dispatches handlers apart from invocation completions, so an answered invoke does not prove an earlier push has been handled; the T17 test uses a second NOTIFY as the barrier.
  - A bUnit test that must know a component has handled an event raised from the test thread awaits `cut.InvokeAsync(() => { })` (the renderer runs its queue in order).
  - No background processes: every mutation batch runs in the foreground, one batch at a time, and the file is restored before the next. Testcontainers' normal disposal is enough; `docker ps` shows no leftover container of yours at the end.
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
- **Branch.** Work on `feat/phase-10b-live-admin` (cut from main at ecf07f1, which includes PHASE-10a, PR #18). Never edit or commit on `main`.

### The helper tools (keep them outside the repository)

Every mutation step uses two small tools, the 09d and 10a tools unchanged: `mut.py` applies one or more replacements to a file, runs a command, reports KILLED, SURVIVED or NOT A MUTATION and always puts the file back; `run_muts.py` runs the mutations of a spec file in the foreground, one at a time, and appends one line per mutation to a results file. Keep them in, for example, `C:\tmp\p10b-tools\` (the plan calls it `$T`; the specs go in `$T\specs\`). A third file, `edit.py`, replaces exactly one occurrence in a file and keeps its line endings, for the edits to existing files.

`mut.py`

```python
"""Mutation helper for the PHASE-09d plan. Keep it OUTSIDE the repository (for example in a temp folder).

usage: python mut.py <file> --replace <old> <new> [--replace <old> <new> ...] -- <command ...>

Applies each replacement to <file> (each <old> must match exactly once; "\\n" in an argument means a newline), runs the command in the current
directory, prints the lines that summarize the run, and ALWAYS puts the file back. The mutation is KILLED when the command fails (exit code 0), a SURVIVOR
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

`edit.py`

```python
"""edit.py <file> <old> <new>: replace exactly one occurrence, keeping the file's line endings. "\\n" in an argument is a newline."""
import sys

path, old, new = sys.argv[1:4]
old, new = old.replace("\\n", "\n"), new.replace("\\n", "\n")
with open(path, encoding="utf-8", newline="") as handle:
    text = handle.read()
if "\r\n" in text:
    old, new = old.replace("\n", "\r\n"), new.replace("\n", "\r\n")
if text.count(old) != 1:
    sys.exit(f"ABORT: {text.count(old)} occurrences in {path}")
with open(path, "w", encoding="utf-8", newline="") as handle:
    handle.write(text.replace(old, new))
```

Run every mutation from the repository root, after `git add -A`-ing the task's files, so `git checkout -- <file>` also restores a file if a run is interrupted, and never run two batches at once or stage while one runs. In the spec files, `LIVE` is `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter-namespace TechStrap.Admin.Tests.Live`, `T17` is the same for `tests/TechStrap.Api.Tests` with `--filter-class "*AdminLiveClientHostTests"`, `ARCH` and `PESTER` are the Architecture and Pester equivalents. Example, from the repository root:

```bash
python $T/run_muts.py $T/specs/m1.py $T/res_m1.txt 1 2 3
```

## Review Focus

1. **Circuit lifetime and start.** Nothing starts during a prerender; one connection per circuit however many components ask; disposal stops the connection and unhooks every event (no handler leaks, no `StateHasChanged` on a disposed component); no scoped service is captured in a singleton.
   - Pinned in Task 1 (`SignalRTicketLiveClientTests`: one start for concurrent callers, nothing created before the first start, disposal once and idempotent, a disposal that ends a waiting retry; `LiveRegistrationTests`: the client is scoped, each scope gets its own and the root provider refuses to resolve it) and Task 2 (`LiveConnectionIndicatorTests`, `LivePrerenderHostTests`: no connection from a prerender, one start however often it renders, the pages never start it; the disposal tests of the indicator and the queue) and Task 3 (the detail page's disposal).
2. **Token.** Header only; a null token and a lapsed session stop the reconnect loop; no token in any log; a reconnect gets a fresh token.
   - Pinned in Task 1 (the token is asked for on every attempt and never cached; a null token stops for good; a throwing provider is retried and its text never logged; the retry policy ends for a lapsed session or a missing token) and Task 4 (against the real hub: a missing token ends in "Offline" after one request; a token that expires closes the connection and the reconnect uses the second token and raises a `Resync`).
3. **Conflict semantics.** The detail banner never silently updates the row version or the draft; a send before the click still gets the 409; own changes are ignored.
   - Pinned in Task 3 (`TicketDetailLiveTests`: a change loads nothing, a send before the click carries the old row version and gets the conflict banner with the draft kept, the click takes the new version, a change that arrives during the reload keeps the banner, own changes, another ticket's changes and a `Resync`) and Task 1 (`LiveChangeRulesTests`).
4. **UI behavior.** A burst gives one queue banner; nothing reorders without the click; presence excludes self; composing is throttled and resets on blur, submit and dispose; announcements are accessible.
   - Pinned in Task 2 (`QueueLiveBannerTests`: one banner and one timer for a burst, nothing reloaded or reordered, the click is the ordinary load with the same filters, `aria-live="polite"` on the indicator and a polite announcement of the banner) and Task 3 (`PresenceViewModelFactoryTests`, `ReplyComposerLiveTests`: at most one hint per four seconds, false on blur, send, empty text, another ticket and disposal; `TicketDetailLiveTests`: the presence bar is a polite status region and lapses after the lease).
5. **Kill switch and isolation.** When off, the Admin behaves exactly as before (no connection attempt, nothing drawn); a live failure never breaks a page; `AdminRules` and the architecture tests stay green.
   - Pinned in Task 1 (`LiveRegistrationTests`: the null client when off, the default on, a non-boolean stops the start; the Pester pin that both env templates say `true` and no compose sets it; `AdminRuleTests`) and Tasks 2 and 3 (every live component has a "switched off" test and a "failing client" test that checks only the exception type is logged).

---

### Task 1: The package bump, the kill switch and the live client core

**Review Focus pin:** 1 (the client is scoped and created per circuit, one start, disposal), 2 (the token: header only, null stops, a throw is retried, a fresh token each attempt, no message in a log) and 5 (the kill switch, the null client, the allowlist, the env contract). This task also adds the fake client every component test will use.

**Files:**

- Create: `src/TechStrap.Admin/Features/Live/ITicketLiveClient.cs`, `ILiveConnection.cs`, `LiveRetryPolicy.cs`, `LiveChangeRules.cs`, `SignalRTicketLiveClient.cs`, `NullTicketLiveClient.cs`, `HubLiveConnection.cs`, `LiveServiceCollectionExtensions.cs`, `src/TechStrap.Admin/Options/LiveUpdatesOptions.cs`
- Modify: `Directory.Packages.props`, `src/TechStrap.Admin/TechStrap.Admin.csproj`, `src/TechStrap.Admin/Program.cs`, `src/TechStrap.Admin/appsettings.json`, `src/TechStrap.Admin/.env.example`, `deploy/.env.admin.example`, `docs/architecture/03-PACKAGE-MAP.md`
- Test (create): `tests/TechStrap.Admin.Tests/Live/FakeLiveConnection.cs`, `ListLogger.cs`, `LiveTestData.cs`, `SignalRTicketLiveClientTests.cs`, `LiveChangeRulesTests.cs`, `LiveRegistrationTests.cs`, `tests/TechStrap.Admin.Tests/Support/FakeTicketLiveClient.cs`
- Test (modify): `tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs`, `tests/TechStrap.Architecture.Tests/AdminRules.cs`, `tests/TechStrap.Architecture.Tests/AdminRuleTests.cs`, `scripts/tests/ConfigContract.Tests.ps1`

**Interfaces:**
- Consumes: `IUserAccessTokenProvider` (`SyntaxCircus.Blazor.Auth` 0.2.0: `ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)`, scoped, registered by `AddBlazorTokenForwarding`), `ApiOptions` (`BaseUrl`), `SessionExpiry` (`IsLapsed`, `Report()`), `AgentSession`, the Contracts constants `TicketHubRoutes.Path`, `TicketHubMethods`, `TicketHubMessages.TicketNotFound`, `TicketChangeKinds`, and the DTOs `TicketChangedDto` and `TicketPresenceDto`.
- Produces:
  - Package and rule: `SyntaxCircus.Blazor.Auth` 0.2.0; the Admin references `Microsoft.AspNetCore.SignalR.Client`; `AdminRules.AllowedPackages` lists it.
  - Setting: `LiveUpdatesOptions` (`SectionName = "LiveUpdates"`, `bool Enabled = true`); `AddLiveFeatures(this IServiceCollection, IConfiguration)`, called in `Program.cs` after `AddKbFeatures()`, registers the scoped `ITicketLiveClient` (the real client, or `NullTicketLiveClient` when `Enabled` is false), the singleton `ILiveConnectionFactory`, and `TimeProvider.System` when none exists.
  - `enum LiveConnectionState { Disconnected, Connecting, Connected, Reconnecting }`.
  - `interface ITicketLiveClient : IAsyncDisposable`: `bool IsEnabled`, `LiveConnectionState State`, `event Action<LiveConnectionState>? StateChanged`, `event Action<TicketChangedDto>? TicketChanged`, `event Action<TicketPresenceDto>? PresenceChanged`, `Task StartAsync(CancellationToken = default)`, `Task<TicketPresenceDto?> JoinTicketAsync(Guid, CancellationToken = default)`, `Task LeaveTicketAsync(Guid, CancellationToken = default)`, `Task SetComposingAsync(Guid, bool, CancellationToken = default)`. None throws.
  - `SignalRTicketLiveClient(ILiveConnectionFactory, IUserAccessTokenProvider, SessionExpiry, TimeProvider, ILogger<SignalRTicketLiveClient>)` (public, scoped, `const int DeduplicationCapacity = 256`); `NullTicketLiveClient` (`IsEnabled` false).
  - `ILiveConnection` (events `TicketChanged`, `PresenceChanged`, `Func<Task>` `Reconnecting`, `Reconnected`, `Closed`; `StartAsync`, `JoinTicketAsync`, `LeaveTicketAsync`, `SetComposingAsync`), `ILiveConnectionFactory.Create(LiveConnectionOptions)`, `record LiveConnectionOptions(Func<Task<string?>> AccessToken, IRetryPolicy RetryPolicy)`, `HubLiveConnectionFactory(IOptions<ApiOptions>, Action<HttpConnectionOptions>? = null)` with `static Uri HubUri(string apiBaseUrl)`.
  - `LiveRetryPolicy(Func<bool> shouldStop) : IRetryPolicy` with `static TimeSpan DelayFor(long attempt)` and `MaxDelay` (30 s); the delays are 0, 2, 5, 10 and then 30 seconds for ever.
  - `LiveChangeRules`: `IsResync(change)`, `IsOwn(change, Guid? ownAgentId)` (a resync is never own), `Concerns(change, ticketId)` (a resync concerns every ticket), `NewResync(DateTimeOffset)`.
  - Test support: `FakeTicketLiveClient` (public; `AdminComponentTest` registers it and exposes it as `LiveClient`), `FakeLiveConnection` and `FakeLiveConnectionFactory`, `LiveTestData`, `ListLogger<T>`.

- [ ] **Step 1: Check the branch, keep the helper tools outside the repository**

```bash
git switch feat/phase-10b-live-admin     # cut from main at ecf07f1; `git status` must be clean
```

Put `mut.py`, `run_muts.py` and `edit.py` (Global Constraints) in `$T` and make the folder `$T\specs\`.

- [ ] **Step 2: Write the failing tests, the fake client and the pins**

New files first. `FakeLiveConnection` scripts what `HubConnection` would do (it asks for the token first, like the real one, and fails with a 401 when there is none); `FakeTicketLiveClient` is what every component test will get.

`tests/TechStrap.Admin.Tests/Live/FakeLiveConnection.cs` (new)

```csharp
using System.Net;
using TechStrap.Admin.Features.Live;
using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Tests.Live;

/// <summary>
/// A scripted <see cref="ILiveConnection"/>. Like the real one, <c>StartAsync</c> asks for the access token first and fails when there is none (the hub answers 401), so a test sees what
/// the client does with a null token or a throwing provider. The test raises the events the hub connection would raise.
/// </summary>
internal sealed class FakeLiveConnection(LiveConnectionOptions options) : ILiveConnection
{
    public LiveConnectionOptions Options { get; } = options;

    public int StartCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    /// <summary>How many of the next starts fail with a transport error, after the token has been asked for.</summary>
    public int FailStarts { get; set; }

    public List<Guid> Joined { get; } = [];

    public List<Guid> Left { get; } = [];

    public List<(Guid TicketId, bool IsComposing)> Composing { get; } = [];

    /// <summary>What a join answers; the default is an empty presence for the ticket. A test sets it to throw.</summary>
    public Func<Guid, Task<TicketPresenceDto>> JoinHandler { get; set; } = ticketId => Task.FromResult(new TicketPresenceDto(ticketId, []));

    /// <summary>When set, <c>LeaveTicket</c> and <c>SetComposing</c> throw it.</summary>
    public Exception? InvokeFailure { get; set; }

    public bool HasSubscribers => TicketChanged is not null || PresenceChanged is not null || Reconnecting is not null || Reconnected is not null || Closed is not null;

    public event Action<TicketChangedDto>? TicketChanged;

    public event Action<TicketPresenceDto>? PresenceChanged;

    public event Func<Task>? Reconnecting;

    public event Func<Task>? Reconnected;

    public event Func<Task>? Closed;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        StartCalls++;
        var token = await Options.AccessToken();
        if (token is null)
        {
            throw new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
        }

        if (FailStarts > 0)
        {
            FailStarts--;
            throw new HttpRequestException("Connection refused");
        }
    }

    public Task<TicketPresenceDto> JoinTicketAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        Joined.Add(ticketId);
        return JoinHandler(ticketId);
    }

    public Task LeaveTicketAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        Left.Add(ticketId);
        return InvokeFailure is null ? Task.CompletedTask : Task.FromException(InvokeFailure);
    }

    public Task SetComposingAsync(Guid ticketId, bool isComposing, CancellationToken cancellationToken)
    {
        Composing.Add((ticketId, isComposing));
        return InvokeFailure is null ? Task.CompletedTask : Task.FromException(InvokeFailure);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
    }

    public void RaiseChanged(TicketChangedDto change) => TicketChanged?.Invoke(change);

    public void RaisePresence(TicketPresenceDto presence) => PresenceChanged?.Invoke(presence);

    public Task RaiseReconnectingAsync() => Reconnecting?.Invoke() ?? Task.CompletedTask;

    public Task RaiseReconnectedAsync() => Reconnected?.Invoke() ?? Task.CompletedTask;

    public Task RaiseClosedAsync() => Closed?.Invoke() ?? Task.CompletedTask;
}

internal sealed class FakeLiveConnectionFactory : ILiveConnectionFactory
{
    public List<FakeLiveConnection> Created { get; } = [];

    public FakeLiveConnection Only => Created.Single();

    public ILiveConnection Create(LiveConnectionOptions options)
    {
        var connection = new FakeLiveConnection(options);
        Created.Add(connection);
        return connection;
    }
}
```

`tests/TechStrap.Admin.Tests/Live/LiveTestData.cs` (new)

```csharp
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Live;

internal static class LiveTestData
{
    public static readonly Guid TicketId = Guid.Parse("0197f2a0-0000-7000-8000-000000000001");
    public static readonly Guid OtherTicketId = Guid.Parse("0197f2a0-0000-7000-8000-000000000009");
    public static readonly Guid MeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid ColleagueId = Guid.Parse("0197f2a0-0000-7000-8000-0000000000aa");
    public static readonly DateTimeOffset At = new(2026, 10, 7, 9, 30, 0, TimeSpan.Zero);

    public static TicketChangedDto Change(Guid? ticketId = null, Guid? actor = null, Guid? eventId = null, string kind = TicketChangeKinds.Updated) =>
        new(eventId ?? Guid.NewGuid(), ticketId ?? TicketId, "ORB-42", Guid.NewGuid(), TicketEventTypes.StatusChanged, actor ?? ColleagueId, At, kind);

    public static TicketChangedDto Resync() =>
        new(Guid.NewGuid(), Guid.Empty, string.Empty, Guid.Empty, string.Empty, null, At, TicketChangeKinds.Resync);

    public static TicketPresenceDto Presence(Guid ticketId, params TicketViewerDto[] viewers) => new(ticketId, viewers);

    public static TicketViewerDto Viewer(Guid agentId, string name, string state = TicketPresenceStates.Viewing) => new(agentId, name, state);
}
```

`tests/TechStrap.Admin.Tests/Live/ListLogger.cs` (new)

```csharp
using Microsoft.Extensions.Logging;

namespace TechStrap.Admin.Tests.Live;

/// <summary>Keeps every formatted message (and the exception of each entry), so a test can prove what was logged and what never was.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_messages)
        {
            _messages.Add(formatter(state, exception) + (exception is null ? string.Empty : " | " + exception));
        }
    }
}
```

`tests/TechStrap.Admin.Tests/Support/FakeTicketLiveClient.cs` (new)

```csharp
using TechStrap.Admin.Features.Live;
using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Tests.Support;

/// <summary>
/// The <see cref="ITicketLiveClient"/> every component test gets (<see cref="AdminComponentTest"/> registers it): events are raised synchronously by the test, calls are recorded, and nothing ever
/// reaches a hub. <see cref="HasSubscribers"/> proves a component unsubscribed when it was disposed.
/// </summary>
public sealed class FakeTicketLiveClient : ITicketLiveClient
{
    private LiveConnectionState _state = LiveConnectionState.Connected;

    public bool IsEnabled { get; set; } = true;

    public LiveConnectionState State => _state;

    public int StartCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    public List<Guid> Joined { get; } = [];

    public List<Guid> Left { get; } = [];

    public List<(Guid TicketId, bool IsComposing)> Composing { get; } = [];

    /// <summary>What a join answers; null (the default) is "not connected yet".</summary>
    public Func<Guid, TicketPresenceDto?> JoinResult { get; set; } = _ => null;

    /// <summary>When set, every call throws it, to prove a failing client never breaks a page.</summary>
    public Exception? Failure { get; set; }

    public bool HasSubscribers => StateChanged is not null || TicketChanged is not null || PresenceChanged is not null;

    public event Action<LiveConnectionState>? StateChanged;

    public event Action<TicketChangedDto>? TicketChanged;

    public event Action<TicketPresenceDto>? PresenceChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        StartCalls++;
        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }

    public Task<TicketPresenceDto?> JoinTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        Joined.Add(ticketId);
        return Failure is null ? Task.FromResult(JoinResult(ticketId)) : Task.FromException<TicketPresenceDto?>(Failure);
    }

    public Task LeaveTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        Left.Add(ticketId);
        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }

    public Task SetComposingAsync(Guid ticketId, bool isComposing, CancellationToken cancellationToken = default)
    {
        Composing.Add((ticketId, isComposing));
        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
    }

    /// <summary>Raises <see cref="StateChanged"/> on the calling thread, as the real client does on a pool thread; the component marshals.</summary>
    public void SetState(LiveConnectionState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }

    public void RaiseChange(TicketChangedDto change) => TicketChanged?.Invoke(change);

    public void RaisePresence(TicketPresenceDto presence) => PresenceChanged?.Invoke(presence);
}
```



The client tests: one start, the state it reports, the token, the retry, what it joins again and announces after a reconnect, the duplicates it drops, the disposal. A test that waits advances the fake clock a second at a time in a bounded poll, because a retry delay is a timer on it.

`tests/TechStrap.Admin.Tests/Live/SignalRTicketLiveClientTests.cs` (new)

```csharp
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Features.Live;
using TechStrap.Contracts.Live;
using static TechStrap.Admin.Tests.Live.LiveTestData;

namespace TechStrap.Admin.Tests.Live;

/// <summary>
/// The client's own behavior over a scripted connection: one start, the state it reports, the token it hands the hub, the reconnect it asks for, what it re-joins and re-announces afterwards, the
/// duplicates it drops, and a disposal that stops everything. A failure of the hub never reaches a caller.
/// </summary>
public sealed class SignalRTicketLiveClientTests
{
    private readonly FakeTimeProvider _time = new(At);
    private readonly FakeLiveConnectionFactory _factory = new();
    private readonly IUserAccessTokenProvider _tokens = Substitute.For<IUserAccessTokenProvider>();
    private readonly SessionExpiry _expiry = new();
    private readonly ListLogger<SignalRTicketLiveClient> _log = new();

    public SignalRTicketLiveClientTests() => _tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>("token-1"));

    private SignalRTicketLiveClient NewClient(ILiveConnectionFactory? factory = null) => new(factory ?? _factory, _tokens, _expiry, _time, _log);

    private static FakeLiveConnection FailingConnection(int failStarts) =>
        new(new LiveConnectionOptions(() => Task.FromResult<string?>("t"), new LiveRetryPolicy(() => false))) { FailStarts = failStarts };

    /// <summary>Waits for a condition on another thread, advancing the fake clock a second at a time (a retry delay is a timer on it). A ceiling, not a measurement.</summary>
    private async Task UntilAsync(Func<bool> condition)
    {
        var ct = TestContext.Current.CancellationToken;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            (DateTime.UtcNow < deadline).ShouldBeTrue("the condition was not reached");
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5, ct);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Start_connects_once_however_many_callers_ask_and_reports_connecting_then_connected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        var states = new List<LiveConnectionState>();
        client.StateChanged += states.Add;
        client.State.ShouldBe(LiveConnectionState.Disconnected);

        await Task.WhenAll(client.StartAsync(ct), client.StartAsync(ct), client.StartAsync(ct));
        await client.StartAsync(ct);

        _factory.Created.Count.ShouldBe(1);
        _factory.Only.StartCalls.ShouldBe(1);
        states.ShouldBe([LiveConnectionState.Connecting, LiveConnectionState.Connected]);
        client.State.ShouldBe(LiveConnectionState.Connected);
        client.IsEnabled.ShouldBeTrue();
    }

    [Fact(Timeout = 30000)]
    public async Task Nothing_is_created_before_the_first_start()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();

        await client.JoinTicketAsync(TicketId, ct);
        await client.SetComposingAsync(TicketId, true, ct);

        _factory.Created.ShouldBeEmpty();
        client.State.ShouldBe(LiveConnectionState.Disconnected);
    }

    [Fact(Timeout = 30000)]
    public async Task The_token_is_asked_for_on_every_connection_attempt_and_never_cached()
    {
        var ct = TestContext.Current.CancellationToken;
        _tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>("token-1"), new ValueTask<string?>("token-2"));
        await using var client = NewClient();
        await client.StartAsync(ct);

        (await _factory.Only.Options.AccessToken()).ShouldBe("token-2");
        await _tokens.Received(2).GetAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact(Timeout = 30000)]
    public async Task A_null_token_stops_the_connection_for_good_with_no_retry()
    {
        var ct = TestContext.Current.CancellationToken;
        _tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>((string?)null));
        await using var client = NewClient();

        await client.StartAsync(ct);

        client.State.ShouldBe(LiveConnectionState.Disconnected);
        _factory.Only.StartCalls.ShouldBe(1);
        _time.Advance(TimeSpan.FromMinutes(5));
        _factory.Only.StartCalls.ShouldBe(1);
        _factory.Only.Options.RetryPolicy.NextRetryDelay(new RetryContext { PreviousRetryCount = 0 }).ShouldBeNull();
    }

    [Fact(Timeout = 30000)]
    public async Task A_token_provider_that_throws_is_retried_and_its_message_is_never_logged()
    {
        var ct = TestContext.Current.CancellationToken;
        _tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new InvalidOperationException("secret-token-material"),
            _ => new ValueTask<string?>("token-2"));
        await using var client = NewClient();

        await client.StartAsync(ct);

        client.State.ShouldBe(LiveConnectionState.Connected);
        _factory.Only.StartCalls.ShouldBe(2);
        _log.Messages.ShouldNotBeEmpty();
        _log.Messages.ShouldAllBe(message => !message.Contains("secret-token-material", StringComparison.Ordinal) && !message.Contains("token-", StringComparison.Ordinal));
    }

    [Fact(Timeout = 30000)]
    public async Task A_failed_first_start_retries_with_the_backoff_until_it_connects()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = FailingConnection(failStarts: 3);
        await using var client = NewClient(new SingleConnectionFactory(connection));
        var states = new List<LiveConnectionState>();
        client.StateChanged += states.Add;

        var start = client.StartAsync(ct);
        await UntilAsync(() => start.IsCompleted);
        await start;

        connection.StartCalls.ShouldBe(4);
        states.ShouldBe([LiveConnectionState.Connecting, LiveConnectionState.Reconnecting, LiveConnectionState.Connected]);
    }

    [Fact(Timeout = 30000)]
    public async Task A_lapsed_session_ends_the_start_loop()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = FailingConnection(failStarts: 100);
        await using var client = NewClient(new SingleConnectionFactory(connection));

        var start = client.StartAsync(ct);
        await UntilAsync(() => connection.StartCalls >= 2);
        _expiry.Report();
        await UntilAsync(() => start.IsCompleted);
        await start;

        client.State.ShouldBe(LiveConnectionState.Disconnected);
    }

    [Fact]
    public void The_retry_policy_never_gives_up_caps_its_delay_and_stops_for_a_lapsed_session()
    {
        var stop = false;
        var policy = new LiveRetryPolicy(() => stop);

        var delays = Enumerable.Range(0, 8).Select(attempt => policy.NextRetryDelay(new RetryContext { PreviousRetryCount = attempt })).ToList();
        delays.ShouldBe([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)]);
        policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 10_000 }).ShouldBe(LiveRetryPolicy.MaxDelay);

        stop = true;
        policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 0 }).ShouldBeNull();
    }

    [Fact(Timeout = 30000)]
    public async Task Reconnect_events_move_the_state_and_a_reconnect_asks_pages_to_resync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.StartAsync(ct);
        var states = new List<LiveConnectionState>();
        var changes = new List<TicketChangedDto>();
        client.StateChanged += states.Add;
        client.TicketChanged += changes.Add;

        await _factory.Only.RaiseReconnectingAsync();
        client.State.ShouldBe(LiveConnectionState.Reconnecting);
        changes.ShouldBeEmpty();
        await _factory.Only.RaiseReconnectedAsync();

        client.State.ShouldBe(LiveConnectionState.Connected);
        states.ShouldBe([LiveConnectionState.Reconnecting, LiveConnectionState.Connected]);
        var resync = changes.ShouldHaveSingleItem();
        resync.Kind.ShouldBe(TicketChangeKinds.Resync);
        resync.TicketId.ShouldBe(Guid.Empty);
        resync.EventId.ShouldNotBe(Guid.Empty);
        resync.ActorAgentId.ShouldBeNull();

        await _factory.Only.RaiseClosedAsync();
        client.State.ShouldBe(LiveConnectionState.Disconnected);
    }

    [Fact(Timeout = 30000)]
    public async Task Joined_tickets_are_joined_again_after_every_connect_and_the_presence_is_announced()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.JoinTicketAsync(TicketId, ct);
        await client.JoinTicketAsync(OtherTicketId, ct);
        await client.LeaveTicketAsync(OtherTicketId, ct);
        var presence = new List<TicketPresenceDto>();
        client.PresenceChanged += presence.Add;
        _factory.Created.ShouldBeEmpty();

        await client.StartAsync(ct);

        _factory.Only.Joined.ShouldBe([TicketId]);
        presence.Select(p => p.TicketId).ShouldBe([TicketId]);

        await _factory.Only.RaiseReconnectedAsync();

        _factory.Only.Joined.ShouldBe([TicketId, TicketId]);
        presence.Count.ShouldBe(2);
    }

    [Fact(Timeout = 30000)]
    public async Task A_join_while_connected_returns_the_presence_and_a_leave_stops_the_rejoin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.StartAsync(ct);
        var viewer = Viewer(ColleagueId, "Colleague");
        _factory.Only.JoinHandler = id => Task.FromResult(Presence(id, viewer));

        var presence = await client.JoinTicketAsync(TicketId, ct);
        await client.LeaveTicketAsync(TicketId, ct);
        await _factory.Only.RaiseReconnectedAsync();

        presence.ShouldNotBeNull().Viewers.ShouldBe([viewer]);
        _factory.Only.Left.ShouldBe([TicketId]);
        _factory.Only.Joined.ShouldBe([TicketId]);
    }

    [Fact(Timeout = 30000)]
    public async Task A_ticket_the_hub_does_not_know_is_not_joined_again_and_nothing_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.StartAsync(ct);
        _factory.Only.JoinHandler = _ => throw new HubException(TicketHubMessages.TicketNotFound);

        var presence = await client.JoinTicketAsync(TicketId, ct);
        await _factory.Only.RaiseReconnectedAsync();

        presence.ShouldBeNull();
        _factory.Only.Joined.ShouldBe([TicketId]);
    }

    [Fact(Timeout = 30000)]
    public async Task A_failing_invoke_never_throws_into_the_caller_and_its_message_is_never_logged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.StartAsync(ct);
        _factory.Only.JoinHandler = _ => throw new InvalidOperationException("boom");
        _factory.Only.InvokeFailure = new InvalidOperationException("boom");

        (await client.JoinTicketAsync(TicketId, ct)).ShouldBeNull();
        await client.LeaveTicketAsync(TicketId, ct);
        await client.SetComposingAsync(TicketId, true, ct);

        _factory.Only.Composing.ShouldBe([(TicketId, true)]);
        _log.Messages.ShouldAllBe(message => !message.Contains("boom", StringComparison.Ordinal));
    }

    [Fact(Timeout = 30000)]
    public async Task An_event_id_is_delivered_once_and_the_memory_of_ids_is_bounded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.StartAsync(ct);
        var seen = new List<Guid>();
        client.TicketChanged += change => seen.Add(change.EventId);
        var first = Guid.NewGuid();

        _factory.Only.RaiseChanged(Change(eventId: first));
        _factory.Only.RaiseChanged(Change(eventId: first));
        seen.ShouldBe([first]);

        for (var i = 0; i < SignalRTicketLiveClient.DeduplicationCapacity; i++)
        {
            _factory.Only.RaiseChanged(Change());
        }

        // The first id is older than the last 256 distinct ones: forgotten, so it is delivered again; the newest is still remembered.
        _factory.Only.RaiseChanged(Change(eventId: first));
        seen.Count(id => id == first).ShouldBe(2);
        var newest = seen[^2];
        _factory.Only.RaiseChanged(Change(eventId: newest));
        seen.Count(id => id == newest).ShouldBe(1);
    }

    [Fact(Timeout = 30000)]
    public async Task A_throwing_subscriber_does_not_stop_the_others()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = NewClient();
        await client.StartAsync(ct);
        var received = new List<TicketPresenceDto>();
        var changes = new List<TicketChangedDto>();
        client.PresenceChanged += _ => throw new InvalidOperationException("subscriber failed");
        client.PresenceChanged += received.Add;
        client.TicketChanged += _ => throw new InvalidOperationException("subscriber failed");
        client.TicketChanged += changes.Add;

        _factory.Only.RaisePresence(Presence(TicketId));
        _factory.Only.RaiseChanged(Change());

        received.Count.ShouldBe(1);
        changes.Count.ShouldBe(1);
    }

    [Fact(Timeout = 30000)]
    public async Task Disposal_stops_the_connection_once_unhooks_its_events_and_ends_every_later_call()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = NewClient();
        await client.StartAsync(ct);
        var connection = _factory.Only;
        connection.HasSubscribers.ShouldBeTrue();
        var states = new List<LiveConnectionState>();
        client.StateChanged += states.Add;

        await client.DisposeAsync();
        await client.DisposeAsync();

        connection.DisposeCalls.ShouldBe(1);
        connection.HasSubscribers.ShouldBeFalse();
        client.State.ShouldBe(LiveConnectionState.Disconnected);
        (await client.JoinTicketAsync(TicketId, ct)).ShouldBeNull();
        await client.SetComposingAsync(TicketId, true, ct);
        await client.StartAsync(ct);
        connection.Joined.ShouldBeEmpty();
        connection.Composing.ShouldBeEmpty();
        _factory.Created.Count.ShouldBe(1);
        states.ShouldBeEmpty();
    }

    [Fact(Timeout = 30000)]
    public async Task Disposal_ends_a_start_that_is_waiting_to_retry()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = FailingConnection(failStarts: 100);
        var client = NewClient(new SingleConnectionFactory(connection));
        var start = client.StartAsync(ct);
        await UntilAsync(() => connection.StartCalls >= 2);

        await client.DisposeAsync();
        await start;

        var calls = connection.StartCalls;
        _time.Advance(TimeSpan.FromMinutes(5));
        connection.StartCalls.ShouldBe(calls);
        connection.DisposeCalls.ShouldBe(1);
    }

    private sealed class SingleConnectionFactory(FakeLiveConnection connection) : ILiveConnectionFactory
    {
        public ILiveConnection Create(LiveConnectionOptions options) => connection;
    }
}
```



The shared rules and the kill switch:

`tests/TechStrap.Admin.Tests/Live/LiveChangeRulesTests.cs` (new)

```csharp
using TechStrap.Admin.Features.Live;
using TechStrap.Contracts.Live;
using static TechStrap.Admin.Tests.Live.LiveTestData;

namespace TechStrap.Admin.Tests.Live;

public sealed class LiveChangeRulesTests
{
    [Fact]
    public void A_change_by_the_signed_in_agent_is_own_and_every_other_change_is_not()
    {
        LiveChangeRules.IsOwn(Change(actor: MeId), MeId).ShouldBeTrue();
        LiveChangeRules.IsOwn(Change(actor: ColleagueId), MeId).ShouldBeFalse();
        LiveChangeRules.IsOwn(Change(actor: Guid.Empty), MeId).ShouldBeFalse();
        LiveChangeRules.IsOwn(Change(actor: MeId), null).ShouldBeFalse();
    }

    [Fact]
    public void A_system_change_has_no_actor_and_is_never_own()
    {
        var system = Change() with { ActorAgentId = null };

        LiveChangeRules.IsOwn(system, MeId).ShouldBeFalse();
    }

    [Fact]
    public void A_resync_is_never_own_even_when_it_somehow_names_the_agent()
    {
        LiveChangeRules.IsOwn(Resync(), MeId).ShouldBeFalse();
        LiveChangeRules.IsOwn(Resync() with { ActorAgentId = MeId }, MeId).ShouldBeFalse();
        LiveChangeRules.IsResync(Resync()).ShouldBeTrue();
        LiveChangeRules.IsResync(Change()).ShouldBeFalse();
    }

    [Fact]
    public void A_change_concerns_a_ticket_when_it_names_it_or_asks_for_a_full_reload()
    {
        LiveChangeRules.Concerns(Change(ticketId: TicketId), TicketId).ShouldBeTrue();
        LiveChangeRules.Concerns(Change(ticketId: OtherTicketId), TicketId).ShouldBeFalse();
        LiveChangeRules.Concerns(Resync(), TicketId).ShouldBeTrue();
    }

    [Fact]
    public void The_synthetic_resync_carries_the_wire_shape_of_the_servers()
    {
        var resync = LiveChangeRules.NewResync(At);

        resync.Kind.ShouldBe(TicketChangeKinds.Resync);
        resync.TicketId.ShouldBe(Guid.Empty);
        resync.ProductId.ShouldBe(Guid.Empty);
        resync.TicketNumber.ShouldBeEmpty();
        resync.EventType.ShouldBeEmpty();
        resync.ActorAgentId.ShouldBeNull();
        resync.EventId.ShouldNotBe(Guid.Empty);
        resync.OccurredAt.ShouldBe(At);
    }
}
```

`tests/TechStrap.Admin.Tests/Live/LiveRegistrationTests.cs` (new)

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Admin.Features.Live;
using TechStrap.Admin.Options;

namespace TechStrap.Admin.Tests.Live;

/// <summary>The kill switch <c>LiveUpdates:Enabled</c>: on by default; off, the Admin gets a client that does nothing and offers no connection, so nothing live is drawn.</summary>
public sealed class LiveRegistrationTests
{
    [Fact]
    public async Task The_default_is_the_real_client_and_each_scope_gets_its_own()
    {
        using var factory = new AdminFactory();
        await using var first = factory.Services.CreateAsyncScope();
        await using var second = factory.Services.CreateAsyncScope();

        var one = first.ServiceProvider.GetRequiredService<ITicketLiveClient>();

        one.ShouldBeOfType<SignalRTicketLiveClient>();
        one.IsEnabled.ShouldBeTrue();
        first.ServiceProvider.GetRequiredService<ITicketLiveClient>().ShouldBeSameAs(one);
        second.ServiceProvider.GetRequiredService<ITicketLiveClient>().ShouldNotBeSameAs(one);
        Should.Throw<InvalidOperationException>(() => factory.Services.GetService<ITicketLiveClient>()).Message.ShouldContain("scoped", Case.Insensitive);
    }

    [Fact]
    public async Task Switched_off_the_admin_gets_the_null_client()
    {
        using var factory = new AdminFactory(settings: new Dictionary<string, string?> { ["LiveUpdates:Enabled"] = "false" });
        await using var scope = factory.Services.CreateAsyncScope();

        var client = scope.ServiceProvider.GetRequiredService<ITicketLiveClient>();

        client.ShouldBeOfType<NullTicketLiveClient>();
        client.IsEnabled.ShouldBeFalse();
        client.State.ShouldBe(LiveConnectionState.Disconnected);
    }

    [Fact]
    public async Task The_null_client_does_nothing_and_never_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = new NullTicketLiveClient();
        var raised = 0;
        client.StateChanged += _ => raised++;
        client.TicketChanged += _ => raised++;
        client.PresenceChanged += _ => raised++;

        await client.StartAsync(ct);
        (await client.JoinTicketAsync(Guid.NewGuid(), ct)).ShouldBeNull();
        await client.LeaveTicketAsync(Guid.NewGuid(), ct);
        await client.SetComposingAsync(Guid.NewGuid(), true, ct);

        raised.ShouldBe(0);
        client.State.ShouldBe(LiveConnectionState.Disconnected);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    public void The_switch_reads_a_boolean_in_any_case(string value, bool expected)
    {
        using var factory = new AdminFactory(settings: new Dictionary<string, string?> { ["LiveUpdates:Enabled"] = value });

        factory.Services.GetRequiredService<IOptions<LiveUpdatesOptions>>().Value.Enabled.ShouldBe(expected);
    }

    [Fact]
    public void The_switch_is_on_when_the_setting_is_absent()
    {
        new LiveUpdatesOptions().Enabled.ShouldBeTrue();
        LiveUpdatesOptions.SectionName.ShouldBe("LiveUpdates");
    }

    [Fact]
    public void A_value_that_is_not_a_boolean_stops_the_start()
    {
        var failure = Should.Throw<Exception>(() =>
        {
            using var factory = new AdminFactory(settings: new Dictionary<string, string?> { ["LiveUpdates:Enabled"] = "maybe" });
            _ = factory.Services;
        });

        failure.ToString().ShouldContain("LiveUpdates");
    }

    [Fact]
    public void The_hub_address_is_the_api_base_address_plus_the_hub_path_even_under_a_base_path()
    {
        HubLiveConnectionFactory.HubUri("http://api/").ToString().ShouldBe("http://api/hubs/tickets");
        HubLiveConnectionFactory.HubUri("http://api").ToString().ShouldBe("http://api/hubs/tickets");
        HubLiveConnectionFactory.HubUri("https://example.test/techstrap/api/").ToString().ShouldBe("https://example.test/techstrap/api/hubs/tickets");
    }
}
```



Every component test gets the fake from the base class; the architecture pin says the Admin carries the one new package; the Pester pin says the switch is an operator setting:

`tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs` (modify)

```diff
diff --git a/tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs b/tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs
--- a/tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs
+++ b/tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs
@@ -1,6 +1,7 @@
 using Bunit;
 using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Time.Testing;
+using TechStrap.Admin.Features.Live;
 using TechStrap.Admin.Features.Shell;
 
 namespace TechStrap.Admin.Tests.Support;
@@ -17,6 +18,10 @@ public abstract class AdminComponentTest : BunitContext
         Services.AddShell();
         Services.AddSingleton<TimeProvider>(Time);
 
+        // Every component that draws live state asks for the client; this one never touches a hub. A test raises its events and reads what the components called.
+        LiveClient = new FakeTicketLiveClient();
+        Services.AddSingleton<ITicketLiveClient>(LiveClient);
+
         Shortcuts = JSInterop.SetupModule("./js/shortcuts.js");
         Shortcuts.SetupVoid("register", _ => true).SetVoidResult();
         Shortcuts.SetupVoid("unregister", _ => true).SetVoidResult();
@@ -47,6 +52,9 @@ public abstract class AdminComponentTest : BunitContext
 
     protected FakeTimeProvider Time { get; }
 
+    /// <summary>The live client every component under test receives (<see cref="ITicketLiveClient"/>): raise changes and presence on it, read the joins and the composing calls.</summary>
+    protected FakeTicketLiveClient LiveClient { get; }
+
     /// <summary>The <c>shortcuts.js</c> module double; use <c>VerifyInvoke("register")</c>.</summary>
     protected BunitJSModuleInterop Shortcuts { get; }
```

`tests/TechStrap.Architecture.Tests/AdminRuleTests.cs` (modify)

```diff
diff --git a/tests/TechStrap.Architecture.Tests/AdminRuleTests.cs b/tests/TechStrap.Architecture.Tests/AdminRuleTests.cs
--- a/tests/TechStrap.Architecture.Tests/AdminRuleTests.cs
+++ b/tests/TechStrap.Architecture.Tests/AdminRuleTests.cs
@@ -11,6 +11,7 @@ public sealed class AdminRuleTests
         var admin = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot())[ReferenceRules.Admin];
 
         admin.PackageReferences.ShouldContain("SyntaxCircus.Blazor.Auth", "the scan must read the real Admin project");
+        admin.PackageReferences.ShouldContain("Microsoft.AspNetCore.SignalR.Client", "the live client of PHASE-10b (D-046) is the one new Admin package");
         AdminRules.PackageViolations(admin).ShouldBeEmpty();
     }
```

`scripts/tests/ConfigContract.Tests.ps1` (modify)

```diff
diff --git a/scripts/tests/ConfigContract.Tests.ps1 b/scripts/tests/ConfigContract.Tests.ps1
--- a/scripts/tests/ConfigContract.Tests.ps1
+++ b/scripts/tests/ConfigContract.Tests.ps1
@@ -350,6 +350,19 @@ Describe 'the config contract of the local compose' {
         @($admin | Sort-Object) | Should -Be @('API__BASEURL', 'ASPNETCORE_ENVIRONMENT', 'DATAPROTECTION__KEYRINGPATH', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
     }
 
+    It 'the live-updates kill switch is an operator setting (PHASE-10b, D-043): both Admin env templates list it as true, and neither compose file sets it' {
+        foreach ($file in 'src/TechStrap.Admin/.env.example', 'deploy/.env.admin.example') {
+            $entries = @(Get-EnvEntries -Path (Join-Path $script:RepoRoot $file) | Where-Object { $_.Key -eq 'LIVEUPDATES__ENABLED' })
+            $entries.Count | Should -Be 1 -Because "$file lists LIVEUPDATES__ENABLED once"
+            $entries[0].Commented | Should -BeFalse
+            $entries[0].Value | Should -Be 'true'
+        }
+        foreach ($compose in 'docker-compose.yml', 'deploy/docker-compose.yml') {
+            (Get-ComposeEnvironmentKeys -File $compose)['admin'] | Should -Not -Contain 'LIVEUPDATES__ENABLED' -Because "$compose must leave the switch to the operator's env file, which a compose environment: entry would override"
+        }
+        (Get-Content -LiteralPath (Join-Path $script:RepoRoot 'src/TechStrap.Admin/appsettings.json') -Raw | ConvertFrom-Json).LiveUpdates.Enabled | Should -BeTrue
+    }
+
     It 'the local compose gives the Portal exactly the API address, the public URL, the trusted proxy and the key ring (D-045)' {
         $portal = (Get-ComposeEnvironmentKeys -File 'docker-compose.yml')['portal']
         @($portal | Sort-Object) | Should -Be @('API__BASEURL', 'ASPNETCORE_ENVIRONMENT', 'DATAPROTECTION__KEYRINGPATH', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
```



- [ ] **Step 3: Run the build to see it fail (RED)**

Run: `dotnet build tests/TechStrap.Admin.Tests -c Release`

Expected: FAIL to compile, counted by error (the real run):

```
      8 CS0246: The type or namespace name 'LiveConnectionOptions' could not be found
      8 CS0234: The type or namespace name 'Live' does not exist in the namespace 'TechStrap.Admin.Features'
      6 CS0246: The type or namespace name 'ILiveConnectionFactory' could not be found
      6 CS0246: The type or namespace name 'ILiveConnection' could not be found
      4 CS0246: The type or namespace name 'SignalRTicketLiveClient' could not be found
      2 CS0234: The type or namespace name 'Client' does not exist in the namespace 'Microsoft.AspNetCore.SignalR'
```

- [ ] **Step 4: Bump the package, reference the SignalR client and add the setting's default, and see the two contracts fail (RED)**

Edits to existing files (CRLF working tree: use `edit.py` or the editor's replace).

`Directory.Packages.props` (modify)

```diff
diff --git a/Directory.Packages.props b/Directory.Packages.props
--- a/Directory.Packages.props
+++ b/Directory.Packages.props
@@ -15,7 +15,7 @@
     <PackageVersion Include="SyntaxCircus.AspNetCore.Authentication" Version="0.1.5" />
     <PackageVersion Include="SyntaxCircus.AspNetCore.Common" Version="0.1.15" />
     <PackageVersion Include="SyntaxCircus.AspNetCore.Serilog" Version="0.1.4" />
-    <PackageVersion Include="SyntaxCircus.Blazor.Auth" Version="0.1.7" />
+    <PackageVersion Include="SyntaxCircus.Blazor.Auth" Version="0.2.0" />
     <PackageVersion Include="SyntaxCircus.Blazor.Components" Version="0.1.3" />
     <PackageVersion Include="SyntaxCircus.Blazor.Seo" Version="0.1.4" />
     <PackageVersion Include="SyntaxCircus.Common" Version="0.1.3" />
```

`src/TechStrap.Admin/TechStrap.Admin.csproj` (modify)

```diff
diff --git a/src/TechStrap.Admin/TechStrap.Admin.csproj b/src/TechStrap.Admin/TechStrap.Admin.csproj
--- a/src/TechStrap.Admin/TechStrap.Admin.csproj
+++ b/src/TechStrap.Admin/TechStrap.Admin.csproj
@@ -14,6 +14,7 @@
     <PackageReference Include="AspNetCore.SassCompiler" />
     <PackageReference Include="GitVersion.MsBuild" PrivateAssets="all" />
     <PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" />
+    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />
     <PackageReference Include="Microsoft.Web.LibraryManager.Build" PrivateAssets="all" />
     <PackageReference Include="SyntaxCircus.AspNetCore.Common" />
     <PackageReference Include="SyntaxCircus.AspNetCore.Serilog" />
```

`src/TechStrap.Admin/appsettings.json` (modify)

```diff
diff --git a/src/TechStrap.Admin/appsettings.json b/src/TechStrap.Admin/appsettings.json
--- a/src/TechStrap.Admin/appsettings.json
+++ b/src/TechStrap.Admin/appsettings.json
@@ -8,6 +8,9 @@
     "ClientId": "",
     "ClientSecret": ""
   },
+  "LiveUpdates": {
+    "Enabled": true
+  },
   "TECHSTRAP_AGENT_GROUP": "techstrap-agents",
   "TECHSTRAP_ADMIN_GROUP": "techstrap-admins",
   "TECHSTRAP_GROUP_CLAIM_TYPE": "groups",
```



Run: `dotnet restore src/TechStrap.Admin` (the restore fetches `SyntaxCircus.Blazor.Auth` 0.2.0 from nuget.org: it is not in the local cache), then:

```bash
dotnet test --project tests/TechStrap.Architecture.Tests -c Release --filter-method "*only_allowed_packages"
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/ConfigContract.Tests.ps1
```

Expected: FAIL. The architecture pin (the real run):

```
failed TechStrap.Architecture.Tests.AdminRuleTests.The_Admin_project_references_only_allowed_packages
  ["TechStrap.Admin must not reference package Microsoft.AspNetCore.SignalR.Client. The Admin talks to the API only; add the package to AdminRules.AllowedPackages if it is a reviewed choice."]
```

and the contract (Pester, the real run): `Tests Passed: 84, Failed: 3`: the two key-list tests (`src/TechStrap.Admin/.env.example is missing: LIVEUPDATES__ENABLED`, `deploy/.env.admin.example is missing: LIVEUPDATES__ENABLED`) and the new kill-switch pin.

- [ ] **Step 5: Allow the package, list the setting in both env templates, and map the packages**

The one-line allowlist, the two env templates (the Admin's own `.env.example` and the deploy template; compose is not touched) and the package map (Blazor.Auth 0.2.0 with the token provider, and the SignalR client row for the Admin):

`tests/TechStrap.Architecture.Tests/AdminRules.cs` (modify)

```diff
diff --git a/tests/TechStrap.Architecture.Tests/AdminRules.cs b/tests/TechStrap.Architecture.Tests/AdminRules.cs
--- a/tests/TechStrap.Architecture.Tests/AdminRules.cs
+++ b/tests/TechStrap.Architecture.Tests/AdminRules.cs
@@ -17,6 +17,7 @@ public static partial class AdminRules
         "AspNetCore.SassCompiler",
         "GitVersion.MsBuild",
         "Microsoft.AspNetCore.Authentication.OpenIdConnect",
+        "Microsoft.AspNetCore.SignalR.Client",
         "Microsoft.Web.LibraryManager.Build",
         "SyntaxCircus.AspNetCore.Common",
         "SyntaxCircus.AspNetCore.Serilog",
```

`src/TechStrap.Admin/.env.example` (modify)

```diff
diff --git a/src/TechStrap.Admin/.env.example b/src/TechStrap.Admin/.env.example
--- a/src/TechStrap.Admin/.env.example
+++ b/src/TechStrap.Admin/.env.example
@@ -9,6 +9,11 @@
 API__BASEURL=http://localhost:8080/
 API__TIMEOUTSECONDS=30
 
+# --- Live updates (PHASE-10b) ---
+# Kill switch. true (the default): the Admin keeps one server-to-server connection to the Api ticket hub per signed-in tab and shows a connection indicator, a "Queue updated - refresh" banner, a
+# "New activity - refresh" banner on an open ticket and who else is viewing or replying. false: no connection is ever opened and nothing live is drawn. The hub address is API__BASEURL plus /hubs/tickets.
+LIVEUPDATES__ENABLED=true
+
 # --- Agent sign-in (OpenID Connect code flow with PKCE, any provider; docs/development/ADMIN-APP.md) ---
 # Required: the Admin refuses to start without all three. A confidential client with redirect URI {admin url}/signin-oidc,
 # post-logout URI {admin url}/signout-callback-oidc and the scopes openid profile email offline_access (the scopes are the library default and are not listed here).
```

`deploy/.env.admin.example` (modify)

```diff
diff --git a/deploy/.env.admin.example b/deploy/.env.admin.example
--- a/deploy/.env.admin.example
+++ b/deploy/.env.admin.example
@@ -12,6 +12,10 @@
 # A request that takes longer than the timeout (1..300 seconds) fails.
 API__TIMEOUTSECONDS=30
 
+# -- Live updates [Admin] --
+# Kill switch for the live connection to the Api ticket hub (the address is API__BASEURL plus /hubs/tickets). false: no connection is opened and no indicator, banner or presence bar is drawn.
+LIVEUPDATES__ENABLED=true
+
 # -- Agent sign-in [Admin] (OpenID Connect code flow with PKCE, any provider; docs/development/ADMIN-APP.md) --
 # Required: the Admin refuses to start without all three. A confidential client with redirect URI {admin url}/signin-oidc, post-logout URI
 # {admin url}/signout-callback-oidc and the scopes openid profile email offline_access (the library default, not listed here). The Authority must be https.
```

`docs/architecture/03-PACKAGE-MAP.md` (modify)

```diff
diff --git a/docs/architecture/03-PACKAGE-MAP.md b/docs/architecture/03-PACKAGE-MAP.md
--- a/docs/architecture/03-PACKAGE-MAP.md
+++ b/docs/architecture/03-PACKAGE-MAP.md
@@ -22,7 +22,7 @@ All releases are 0.x. Pin exact versions (no floating ranges).
 | Transactional email | Selected | `SyntaxCircus.Email` | 0.1.6 | [0.1.6](https://www.nuget.org/packages/SyntaxCircus.Email/0.1.6) (sinforgiver 0.1.5) | `IEmailSender` over MailKit SMTP; Null sender for Development, in-memory sender for tests. Used only by the worker outbox drainer. Templates, outbox and per-product branding are project code (`IEmailTemplateRenderer`). Prereq: `Email__Smtp__*` env vars (Host, Port, credentials, `DefaultFrom`, `MaxRetryAttempts`) on the worker container; credentials from secrets. | P05 |
 | File/blob storage | Selected | `SyntaxCircus.Storage` | 0.2.1 | [0.2.1](https://www.nuget.org/packages/SyntaxCircus.Storage/0.2.1) (sinforgiver 0.2.1) | `IStorageProvider` behind `IAttachmentStore` (ticket attachments, KB images). Local-disk provider in core. Authorization, size limit, type allowlist and retention are application-owned. Prereq: `Storage__Provider=Local`, `Storage__Local__RootPath=/app/storage` on a shared named volume mounted by both the API (intake, download, KB upload, the public-read `kb-images/` prefix served as static assets) and the Worker (cleanup), writable by uid 10001. `PublicBaseUrl` is only needed for `GetAccessUrlAsync`, which is not planned: downloads stream through `GetAttachmentRequestHandler`. S3 is a later config switch. | P05 |
 | Resilient outbound HTTP | Selected | `SyntaxCircus.Http.Resilience` | 0.2.2 | [0.2.2](https://www.nuget.org/packages/SyntaxCircus.Http.Resilience/0.2.2) (sinforgiver 0.2.1) | Typed API clients in Admin and Portal (to the API). Per-client timeout/retry budgets; auth and idempotency stay caller-owned (no blind retry of non-idempotent POST). `TechStrap.Client` SDK uses plain `HttpClient` so consumers inherit no extra dependency (**Assumption**). | P07 (Admin), P09 (Portal) |
-| Blazor token forwarding | Selected | `SyntaxCircus.Blazor.Auth` | 0.1.7 | [0.1.7](https://www.nuget.org/packages/SyntaxCircus.Blazor.Auth/0.1.7) (dragon-poop 0.1.7) | Admin (Blazor Server) forwards the agent's OIDC access token to the API and manages session refresh. Cookie/OIDC sign-in config is app-owned. Prereq: save tokens, request `offline_access`. Portal is anonymous and does not use it. | P07 |
+| Blazor token forwarding | Selected | `SyntaxCircus.Blazor.Auth` | 0.2.0 | [0.2.0](https://www.nuget.org/packages/SyntaxCircus.Blazor.Auth/0.2.0) (dragon-poop 0.2.0) | Admin (Blazor Server) forwards the agent's OIDC access token to the API and manages session refresh. Cookie/OIDC sign-in config is app-owned. Prereq: save tokens, request `offline_access`. 0.2.0 adds the public, scoped `IUserAccessTokenProvider` (the refreshed token as a string, null when the session has lapsed), which the Admin's live client gives the hub connection's `AccessTokenProvider` (10b, D-046). Portal is anonymous and does not use it. | P07, P10 |
 | Blazor reusable UI | Selected | `SyntaxCircus.Blazor.Components` | 0.1.3 | [0.1.3](https://www.nuget.org/packages/SyntaxCircus.Blazor.Components/0.1.3) (dragon-poop 0.1.3) | Error boundaries, not-found, reconnect UI in Admin and Portal. Presentation only; no CSS, layout or authorization. | P07, P09 |
 | Blazor SEO | Selected | `SyntaxCircus.Blazor.Seo` | 0.1.4 | [0.1.4](https://www.nuget.org/packages/SyntaxCircus.Blazor.Seo/0.1.4) (sinforgiver 0.1.4) | Meta tags, canonical, Open Graph, robots.txt and sitemap support for portal pages (`AddSyntaxCircusSeo`, `UseSyntaxCircusSeo`, `MapSeoRobotsTxt`, `MapSeoSitemap`; D-045). Portal only. Prereq: public base URL env var `TECHSTRAP_PORTAL_PUBLIC_URL`. Ticket pages (`/t/{token}`) must be `noindex`. | P09 |
 | MAUI runtime environment switching | Not applicable | `SyntaxCircus.Maui.Environments` | n/a | [Source](https://github.com/Syntax-Circus/SyntaxCircus.Maui.Environments) (0.1.2 current; not selected) | `TechStrap.Client.Maui` is a **library** (device/app metadata capture and a submit helper) that takes a base URL and API key from the host app. The host app owns its environments, and referencing the package would force a dependency and policy on every consumer. The helper must not read or store environment state. Hosts needing runtime switching adopt the package themselves and pass the active URL in. | n/a |
@@ -45,7 +45,7 @@ Pin all Microsoft.* 10.0.x packages to the **same patch**. nuget.org latest stab
 | `Microsoft.EntityFrameworkCore` (+ `.Relational`, `.Design`) | Selected | 10.0.12 | [Design 10.0.12](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Design/10.0.12) | EF Core runtime and `dotnet ef` tooling (Design is `PrivateAssets=all`). | P01 |
 | `Microsoft.AspNetCore.OpenApi` | Selected | 10.0.12 | [10.0.12](https://www.nuget.org/packages/Microsoft.AspNetCore.OpenApi/10.0.12) | `/openapi/v1.json` that the SDK builds against. | P01 |
 | `Microsoft.AspNetCore.Authentication.OpenIdConnect` | Selected | 10.0.12 (dragon-poop 10.0.11) | [10.0.12](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.OpenIdConnect/10.0.12) (verified latest stable 2026-10-02) | Admin OIDC sign-in (cookie + OIDC). | P07 |
-| `Microsoft.AspNetCore.SignalR.Client` | Selected | 10.0.12 | [10.0.12](https://www.nuget.org/packages/Microsoft.AspNetCore.SignalR.Client/10.0.12) (sinforgiver 10.0.11) | The hub client: `TechStrap.Api.Tests` connects a real `HubConnection` in 10a (D-046); the Admin's server-side hub connection arrives in 10b (D-007). The hub itself ships in the shared framework. | P10 |
+| `Microsoft.AspNetCore.SignalR.Client` | Selected | 10.0.12 | [10.0.12](https://www.nuget.org/packages/Microsoft.AspNetCore.SignalR.Client/10.0.12) (sinforgiver 10.0.11) | The hub client: `TechStrap.Api.Tests` connects a real `HubConnection` in 10a (D-046); the Admin's server-side hub connection is `SignalRTicketLiveClient` in `TechStrap.Admin` (10b, D-007, D-046), the one package the Admin adds; the server-to-server address is `API__BASEURL` plus the hub path. The hub itself ships in the shared framework. | P10 |
 | `HtmlSanitizer` | Selected | 9.2.1039 | [9.2.1039](https://www.nuget.org/packages/HtmlSanitizer/9.2.1039) (10.x is beta-only) | Behind `IHtmlSanitizer`: sanitizes message bodies on write and KB HTML after rendering (D-014). Interface in Application, implementation in Infrastructure. | P05 |
 | `Markdig` | Selected | 1.4.0 | [1.4.0](https://www.nuget.org/packages/Markdig/1.4.0) (dragon-poop 1.3.2) | Behind `IMarkdownRenderer`: KB articles, agent replies and internal notes (D-035): Markdown to HTML, always followed by sanitization. | P06, P08 |
 | `AspNetCore.SassCompiler` | Selected | 1.105.1 | [1.105.1](https://www.nuget.org/packages/AspNetCore.SassCompiler/1.105.1) (dragon-poop 1.103.0) | Compiles Bootstrap 5 SCSS at build for Admin and Portal. No compiled CSS committed. | P01 (wiring), P02 (tokens) |
```



Run the two again: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release` (Expected: 295 passed) and `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/ConfigContract.Tests.ps1` (Expected: `Tests Passed: 87, Failed: 0`), and `pwsh -NoProfile -File scripts/Check-PackageVersions.ps1` (Expected: `Package version check passed: 41 packages match the package map.`).

- [ ] **Step 6: Write the client**

The interface and the enum, the shared rules, the connection abstraction, the retry policy, the real client, the null client, the real connection and its factory, the options and the registration. Points a reader should check: the token is asked for in `GetTokenAsync` on every attempt and never cached; a null token sets `_noToken`, which the start loop and the policy both read; a throwing provider propagates so SignalR counts a failed attempt; `StartAsync` returns the one running task (`_starting ??= RunAsync()`) and the start loop uses `LiveRetryPolicy.DelayFor(attempt)` with `Task.Delay(delay, TimeProvider, token)`; every handler is raised through `Raise`, which calls each subscriber on its own and logs only the exception type; `DisposeAsync` unhooks the events before it disposes the connection, awaits the start loop and clears the client's own events.

`src/TechStrap.Admin/Features/Live/ITicketLiveClient.cs` (new)

```csharp
using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Features.Live;

/// <summary>The state of the live connection, as the indicator draws it. An Admin type: Contracts holds no enums (D-046).</summary>
public enum LiveConnectionState
{
    /// <summary>Not connected and not trying: before the first start, after a stop, after a lapsed session or a refused token, and when live updates are switched off.</summary>
    Disconnected,

    /// <summary>The first connection attempt is running.</summary>
    Connecting,

    /// <summary>Connected: changes and presence arrive.</summary>
    Connected,

    /// <summary>The connection was lost, or the first attempt failed; the client keeps trying with a capped backoff.</summary>
    Reconnecting,
}

/// <summary>
/// The Admin's one live connection per circuit (PHASE-10b). Components depend on this and never on SignalR. Every method is safe to call at any time and never throws: a failure of the hub is logged
/// and shows as <see cref="LiveConnectionState.Reconnecting"/> or <see cref="LiveConnectionState.Disconnected"/>, so live updates can never break a page. The events are raised on a thread-pool thread: a component
/// marshals with <c>InvokeAsync</c>, and unsubscribes in its own disposal.
/// </summary>
public interface ITicketLiveClient : IAsyncDisposable
{
    /// <summary>False for the client that is used when <c>LiveUpdates:Enabled</c> is off: the indicator, the banners and the presence bar are not drawn at all.</summary>
    bool IsEnabled { get; }

    LiveConnectionState State { get; }

    /// <summary>Raised when <see cref="State"/> changes, with the new state.</summary>
    event Action<LiveConnectionState>? StateChanged;

    /// <summary>
    /// A change to a ticket, in the order it arrived, each event id once. After a reconnect (and whenever the hub says so) a change of kind <c>Resync</c> arrives: reload everything. It is not filtered for the
    /// agent's own changes: <see cref="LiveChangeRules.IsOwn"/> does that, where the signed-in agent is known.
    /// </summary>
    event Action<TicketChangedDto>? TicketChanged;

    /// <summary>The viewers of a ticket the client has joined, including the agent. Also raised with the answer of every re-join after a connect.</summary>
    event Action<TicketPresenceDto>? PresenceChanged;

    /// <summary>
    /// Opens the connection, once per client however many components ask. Call it only from <c>OnAfterRenderAsync</c>, never during prerendering: a prerender scope has no circuit and no token path.
    /// The task ends when the connection is first established or the client gives up (a lapsed session, no token, a disposal); callers need not await it.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks to see who else has the ticket open. The ticket is also remembered and joined again after every reconnect. Returns the current viewers, or null when not connected yet, when the hub does not
    /// know the ticket, or when the call failed.
    /// </summary>
    Task<TicketPresenceDto?> JoinTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);

    Task LeaveTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);

    Task SetComposingAsync(Guid ticketId, bool isComposing, CancellationToken cancellationToken = default);
}
```

`src/TechStrap.Admin/Features/Live/LiveChangeRules.cs` (new)

```csharp
using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Features.Live;

/// <summary>The rules every live component shares, in one place so the queue, the detail page and the presence bar cannot disagree (D-046).</summary>
public static class LiveChangeRules
{
    public static bool IsResync(TicketChangedDto change) => change.Kind == TicketChangeKinds.Resync;

    /// <summary>
    /// True when the signed-in agent made the change: their own write already refreshed their page, and another tab of theirs still meets the existing 409 on a send. A <c>Resync</c> is never own.
    /// A change with no actor (the Worker's auto-close, a customer reply) is never own.
    /// </summary>
    public static bool IsOwn(TicketChangedDto change, Guid? ownAgentId) =>
        !IsResync(change) && ownAgentId is { } own && change.ActorAgentId == own;

    /// <summary>
    /// Every <c>TicketChanged</c> goes to every connection (D-046), so a page filters by ticket id. A <c>Resync</c> names no ticket and concerns every page.
    /// </summary>
    public static bool Concerns(TicketChangedDto change, Guid ticketId) => IsResync(change) || change.TicketId == ticketId;

    /// <summary>The message a page gets after a reconnect: the same shape the Api's listener sends after it reconnects to Postgres, built here because the notifications of the gap are gone.</summary>
    public static TicketChangedDto NewResync(DateTimeOffset now) =>
        new(Guid.NewGuid(), Guid.Empty, string.Empty, Guid.Empty, string.Empty, null, now, TicketChangeKinds.Resync);
}
```

`src/TechStrap.Admin/Features/Live/ILiveConnection.cs` (new)

```csharp
using Microsoft.AspNetCore.SignalR.Client;
using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Features.Live;

/// <summary>What a connection needs from the client: where its access token comes from, and when to retry.</summary>
/// <param name="AccessToken">Asked for on every start and every reconnect. Null means "not signed in": the client stops. It may throw; that fails the attempt.</param>
/// <param name="RetryPolicy">Decides the delay before each reconnect attempt, or that there will be none.</param>
public sealed record LiveConnectionOptions(Func<Task<string?>> AccessToken, IRetryPolicy RetryPolicy);

/// <summary>
/// The hub connection the client drives, as a small abstraction, so the client's logic (state, de-duplication, re-joining, disposal) is tested with a scripted connection and the real one
/// is exercised against the real hub. The events mirror the hub's; the lifecycle ones are asynchronous because the client re-joins its tickets from them.
/// </summary>
public interface ILiveConnection : IAsyncDisposable
{
    event Action<TicketChangedDto>? TicketChanged;

    event Action<TicketPresenceDto>? PresenceChanged;

    /// <summary>The connection was lost and the retry policy is running.</summary>
    event Func<Task>? Reconnecting;

    /// <summary>A reconnect succeeded. The server has forgotten every group of the old connection.</summary>
    event Func<Task>? Reconnected;

    /// <summary>The connection ended for good: the retry policy said stop, or it was stopped.</summary>
    event Func<Task>? Closed;

    Task StartAsync(CancellationToken cancellationToken);

    Task<TicketPresenceDto> JoinTicketAsync(Guid ticketId, CancellationToken cancellationToken);

    Task LeaveTicketAsync(Guid ticketId, CancellationToken cancellationToken);

    Task SetComposingAsync(Guid ticketId, bool isComposing, CancellationToken cancellationToken);
}

public interface ILiveConnectionFactory
{
    ILiveConnection Create(LiveConnectionOptions options);
}
```

`src/TechStrap.Admin/Features/Live/LiveRetryPolicy.cs` (new)

```csharp
using Microsoft.AspNetCore.SignalR.Client;

namespace TechStrap.Admin.Features.Live;

/// <summary>
/// The reconnect schedule. SignalR's own <c>WithAutomaticReconnect()</c> gives up after four attempts (0, 2, 10 and 30 seconds) and stays disconnected; an agent's screen can stay open all day, so this policy
/// never gives up: 0, 2, 5, 10 seconds and then 30 seconds for ever. It stops only when <paramref name="shouldStop"/> says the session has lapsed or the token was refused; the client
/// then ends in <see cref="LiveConnectionState.Disconnected"/>. SignalR asks for a fresh access token on every attempt, so a refreshed token is picked up by the reconnect itself.
/// </summary>
/// <param name="shouldStop">True once retrying is pointless: <c>SessionExpiry.IsLapsed</c>, or the token provider returned no token.</param>
public sealed class LiveRetryPolicy(Func<bool> shouldStop) : IRetryPolicy
{
    /// <summary>The longest wait between two attempts.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>The wait before the first attempts; the last entry repeats.</summary>
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        MaxDelay,
    ];

    /// <summary>The wait before attempt number <paramref name="attempt"/> (0 is the first retry).</summary>
    public static TimeSpan DelayFor(long attempt) => Delays[(int)Math.Clamp(attempt, 0, Delays.Length - 1)];

    public TimeSpan? NextRetryDelay(RetryContext retryContext) => shouldStop() ? null : DelayFor(retryContext.PreviousRetryCount);
}
```

`src/TechStrap.Admin/Features/Live/SignalRTicketLiveClient.cs` (new)

```csharp
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Admin.Auth;
using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Features.Live;

/// <summary>
/// The Admin's live client (D-046): a scoped service, so one per circuit, and the only owner of the hub connection. It starts once, when a component that is already rendered in the circuit asks (never during
/// prerendering), asks <see cref="IUserAccessTokenProvider"/> for the agent's token on every connection attempt (the token travels in the Authorization header only), keeps retrying with
/// <see cref="LiveRetryPolicy"/> while the session lives, joins its tickets again after every connect, tells its subscribers to resync after a reconnect (the notifications of the gap are gone), and
/// drops an event id it has just delivered. It does not marshal threads and never throws: every failure is logged by type (never the message, which could carry a token or a name) and shows as a state.
/// </summary>
public sealed class SignalRTicketLiveClient : ITicketLiveClient
{
    /// <summary>How many event ids are remembered for de-duplication. A repeat older than this is delivered again, which a page treats as one more reason to offer a refresh.</summary>
    public const int DeduplicationCapacity = 256;

    private readonly ILiveConnectionFactory _connections;
    private readonly IUserAccessTokenProvider _tokens;
    private readonly SessionExpiry _expiry;
    private readonly TimeProvider _time;
    private readonly ILogger<SignalRTicketLiveClient> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly HashSet<Guid> _joined = [];
    private readonly Queue<Guid> _seenOrder = new();
    private readonly HashSet<Guid> _seen = [];
    private ILiveConnection? _connection;
    private Task? _starting;
    private LiveConnectionState _state;
    private bool _disposed;
    private volatile bool _noToken;

    public SignalRTicketLiveClient(ILiveConnectionFactory connections, IUserAccessTokenProvider tokens, SessionExpiry expiry, TimeProvider time, ILogger<SignalRTicketLiveClient> logger)
    {
        _connections = connections;
        _tokens = tokens;
        _expiry = expiry;
        _time = time;
        _logger = logger;
    }

    public bool IsEnabled => true;

    public LiveConnectionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public event Action<LiveConnectionState>? StateChanged;

    public event Action<TicketChangedDto>? TicketChanged;

    public event Action<TicketPresenceDto>? PresenceChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            return _starting ??= RunAsync();
        }
    }

    public async Task<TicketPresenceDto?> JoinTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        ILiveConnection? connection;
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            _joined.Add(ticketId);
            connection = _state == LiveConnectionState.Connected ? _connection : null;
        }

        return connection is null ? null : await JoinAsync(connection, ticketId, cancellationToken);
    }

    public async Task LeaveTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        ILiveConnection? connection;
        lock (_gate)
        {
            _joined.Remove(ticketId);
            connection = _state == LiveConnectionState.Connected && !_disposed ? _connection : null;
        }

        if (connection is not null)
        {
            await InvokeSafelyAsync(() => connection.LeaveTicketAsync(ticketId, cancellationToken), nameof(ITicketLiveClient.LeaveTicketAsync));
        }
    }

    public async Task SetComposingAsync(Guid ticketId, bool isComposing, CancellationToken cancellationToken = default)
    {
        ILiveConnection? connection;
        lock (_gate)
        {
            connection = _state == LiveConnectionState.Connected && !_disposed ? _connection : null;
        }

        if (connection is not null)
        {
            await InvokeSafelyAsync(() => connection.SetComposingAsync(ticketId, isComposing, cancellationToken), nameof(ITicketLiveClient.SetComposingAsync));
        }
    }

    public async ValueTask DisposeAsync()
    {
        ILiveConnection? connection;
        Task? starting;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _state = LiveConnectionState.Disconnected;
            connection = _connection;
            starting = _starting;
        }

        await _lifetime.CancelAsync();
        if (connection is not null)
        {
            connection.TicketChanged -= OnTicketChanged;
            connection.PresenceChanged -= OnPresenceChanged;
            connection.Reconnecting -= OnReconnectingAsync;
            connection.Reconnected -= OnReconnectedAsync;
            connection.Closed -= OnClosedAsync;
            try
            {
                await connection.DisposeAsync();
            }
            catch (Exception exception)
            {
                _logger.LogWarning("The live connection could not be closed cleanly ({ExceptionType}).", exception.GetType().Name);
            }
        }

        if (starting is not null)
        {
            await starting;
        }

        TicketChanged = null;
        PresenceChanged = null;
        StateChanged = null;
        _lifetime.Dispose();
    }

    /// <summary>The first connection: attempts with the retry policy's delays until one succeeds, the session lapses, no token comes, or the client is disposed. Never throws.</summary>
    private async Task RunAsync()
    {
        try
        {
            SetState(LiveConnectionState.Connecting);
            var connection = _connections.Create(new LiveConnectionOptions(GetTokenAsync, new LiveRetryPolicy(() => _noToken || _expiry.IsLapsed)));
            lock (_gate)
            {
                _connection = connection;
            }

            connection.TicketChanged += OnTicketChanged;
            connection.PresenceChanged += OnPresenceChanged;
            connection.Reconnecting += OnReconnectingAsync;
            connection.Reconnected += OnReconnectedAsync;
            connection.Closed += OnClosedAsync;

            for (var attempt = 0; !_lifetime.IsCancellationRequested; attempt++)
            {
                try
                {
                    await connection.StartAsync(_lifetime.Token);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning("The live connection could not be started ({ExceptionType}).", exception.GetType().Name);
                    if (_noToken || _expiry.IsLapsed)
                    {
                        SetState(LiveConnectionState.Disconnected);
                        return;
                    }

                    SetState(LiveConnectionState.Reconnecting);
                    await Task.Delay(LiveRetryPolicy.DelayFor(attempt), _time, _lifetime.Token);
                    continue;
                }

                SetState(LiveConnectionState.Connected);
                await RejoinAsync(connection);
                return;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Disposed while waiting to retry: nothing more to do.
        }
        catch (Exception exception)
        {
            _logger.LogWarning("The live connection stopped unexpectedly ({ExceptionType}).", exception.GetType().Name);
            SetState(LiveConnectionState.Disconnected);
        }
    }

    /// <summary>
    /// The hub calls this on every connect and reconnect. A null token means there is no signed-in user: the client stops for good (a hub request without a token is refused, and every retry would be too).
    /// An exception means the token could not be refreshed this time: it propagates, the attempt fails, and the next one asks again.
    /// </summary>
    private async Task<string?> GetTokenAsync()
    {
        try
        {
            var token = await _tokens.GetAccessTokenAsync(_lifetime.Token);
            if (string.IsNullOrEmpty(token))
            {
                _noToken = true;
                _logger.LogWarning("No access token is available for the live connection; it will not be retried.");
                return null;
            }

            return token;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning("The access token for the live connection could not be obtained ({ExceptionType}).", exception.GetType().Name);
            throw;
        }
    }

    private void OnTicketChanged(TicketChangedDto change)
    {
        if (change.EventId != Guid.Empty && !Remember(change.EventId))
        {
            return;
        }

        Raise(TicketChanged, change);
    }

    private void OnPresenceChanged(TicketPresenceDto presence) => Raise(PresenceChanged, presence);

    private Task OnReconnectingAsync()
    {
        SetState(LiveConnectionState.Reconnecting);
        return Task.CompletedTask;
    }

    private async Task OnReconnectedAsync()
    {
        ILiveConnection? connection;
        lock (_gate)
        {
            connection = _connection;
        }

        SetState(LiveConnectionState.Connected);

        // Whatever happened during the gap was not delivered (at most once): every page offers a refresh. The server forgot the old connection's groups: join the open tickets again.
        Raise(TicketChanged, LiveChangeRules.NewResync(_time.GetUtcNow()));
        if (connection is not null)
        {
            await RejoinAsync(connection);
        }
    }

    private Task OnClosedAsync()
    {
        SetState(LiveConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    private async Task RejoinAsync(ILiveConnection connection)
    {
        Guid[] tickets;
        lock (_gate)
        {
            tickets = [.. _joined];
        }

        foreach (var ticketId in tickets)
        {
            if (await JoinAsync(connection, ticketId, _lifetime.Token) is { } presence)
            {
                Raise(PresenceChanged, presence);
            }
        }
    }

    private async Task<TicketPresenceDto?> JoinAsync(ILiveConnection connection, Guid ticketId, CancellationToken cancellationToken)
    {
        try
        {
            return await connection.JoinTicketAsync(ticketId, cancellationToken);
        }
        catch (HubException exception) when (exception.Message.Contains(TicketHubMessages.TicketNotFound, StringComparison.Ordinal))
        {
            // The ticket is gone: the page finds out from its own load. Do not ask again after the next reconnect.
            lock (_gate)
            {
                _joined.Remove(ticketId);
            }

            return null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Joining a ticket on the live connection failed ({ExceptionType}).", exception.GetType().Name);
            return null;
        }
    }

    private async Task InvokeSafelyAsync(Func<Task> call, string operation)
    {
        try
        {
            await call();
        }
        catch (Exception exception)
        {
            _logger.LogWarning("The live call {Operation} failed ({ExceptionType}).", operation, exception.GetType().Name);
        }
    }

    /// <summary>True the first time an event id is seen; the newest <see cref="DeduplicationCapacity"/> ids are remembered, so the memory is bounded.</summary>
    private bool Remember(Guid eventId)
    {
        lock (_gate)
        {
            if (!_seen.Add(eventId))
            {
                return false;
            }

            _seenOrder.Enqueue(eventId);
            if (_seenOrder.Count > DeduplicationCapacity)
            {
                _seen.Remove(_seenOrder.Dequeue());
            }

            return true;
        }
    }

    private void SetState(LiveConnectionState state)
    {
        lock (_gate)
        {
            if (_disposed || _state == state)
            {
                return;
            }

            _state = state;
        }

        Raise(StateChanged, state);
    }

    /// <summary>Calls every subscriber on its own: one that throws is logged and does not keep the others (or the hub's receive loop) from running.</summary>
    private void Raise<T>(Action<T>? handlers, T argument)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(argument);
            }
            catch (Exception exception)
            {
                _logger.LogWarning("A live-update subscriber failed ({ExceptionType}).", exception.GetType().Name);
            }
        }
    }
}
```

`src/TechStrap.Admin/Features/Live/NullTicketLiveClient.cs` (new)

```csharp
using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Features.Live;

/// <summary>
/// The client used when <c>LiveUpdates:Enabled</c> is off: it never connects, never raises an event and answers every call at once. <see cref="IsEnabled"/> is false, so the indicator, the banners and
/// the presence bar are not drawn and the pages behave exactly as they did before live updates.
/// </summary>
public sealed class NullTicketLiveClient : ITicketLiveClient
{
    public bool IsEnabled => false;

    public LiveConnectionState State => LiveConnectionState.Disconnected;

    // Declared so a component can subscribe and unsubscribe; nothing ever raises them.
#pragma warning disable CS0067
    public event Action<LiveConnectionState>? StateChanged;

    public event Action<TicketChangedDto>? TicketChanged;

    public event Action<TicketPresenceDto>? PresenceChanged;
#pragma warning restore CS0067

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<TicketPresenceDto?> JoinTicketAsync(Guid ticketId, CancellationToken cancellationToken = default) => Task.FromResult<TicketPresenceDto?>(null);

    public Task LeaveTicketAsync(Guid ticketId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetComposingAsync(Guid ticketId, bool isComposing, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

`src/TechStrap.Admin/Features/Live/HubLiveConnection.cs` (new)

```csharp
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Features.Live;

/// <summary>
/// The real connection: a <see cref="HubConnection"/> to the Api's ticket hub, server to server (the browser never reaches <c>/hubs</c>, D-046). The address is the Api base address plus
/// <see cref="TicketHubRoutes.Path"/>; the token goes in the Authorization header through <c>AccessTokenProvider</c> (never in the URL); the method and group names come from Contracts.
/// </summary>
internal sealed class HubLiveConnection : ILiveConnection
{
    private readonly HubConnection _hub;

    public HubLiveConnection(Uri hubUri, LiveConnectionOptions options, Action<HttpConnectionOptions>? configure)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(hubUri, http =>
            {
                http.AccessTokenProvider = options.AccessToken;
                configure?.Invoke(http);
            })
            .WithAutomaticReconnect(options.RetryPolicy)
            .Build();
        _hub.On<TicketChangedDto>(TicketHubMethods.TicketChanged, change => TicketChanged?.Invoke(change));
        _hub.On<TicketPresenceDto>(TicketHubMethods.PresenceChanged, presence => PresenceChanged?.Invoke(presence));
        _hub.Reconnecting += _ => Reconnecting?.Invoke() ?? Task.CompletedTask;
        _hub.Reconnected += _ => Reconnected?.Invoke() ?? Task.CompletedTask;
        _hub.Closed += _ => Closed?.Invoke() ?? Task.CompletedTask;
    }

    public event Action<TicketChangedDto>? TicketChanged;

    public event Action<TicketPresenceDto>? PresenceChanged;

    public event Func<Task>? Reconnecting;

    public event Func<Task>? Reconnected;

    public event Func<Task>? Closed;

    public Task StartAsync(CancellationToken cancellationToken) => _hub.StartAsync(cancellationToken);

    public Task<TicketPresenceDto> JoinTicketAsync(Guid ticketId, CancellationToken cancellationToken) =>
        _hub.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, ticketId, cancellationToken);

    public Task LeaveTicketAsync(Guid ticketId, CancellationToken cancellationToken) =>
        _hub.InvokeAsync(TicketHubMethods.LeaveTicket, ticketId, cancellationToken);

    public Task SetComposingAsync(Guid ticketId, bool isComposing, CancellationToken cancellationToken) =>
        _hub.InvokeAsync(TicketHubMethods.SetComposing, ticketId, isComposing, cancellationToken);

    public ValueTask DisposeAsync() => _hub.DisposeAsync();
}

/// <summary>
/// Builds <see cref="HubLiveConnection"/>s for the Api address in the validated <c>Api</c> options. A singleton: it holds options only, never a scoped service. <paramref name="configure"/> is for tests
/// (a handler that reaches an in-memory server, a transport); production passes none.
/// </summary>
public sealed class HubLiveConnectionFactory(IOptions<ApiOptions> api, Action<HttpConnectionOptions>? configure = null) : ILiveConnectionFactory
{
    /// <summary>The Api base address with the hub path appended. The base may carry a path (a reverse proxy prefix), so the path is joined, not replaced.</summary>
    public static Uri HubUri(string apiBaseUrl) =>
        new(new Uri(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/"), TicketHubRoutes.Path.TrimStart('/'));

    public ILiveConnection Create(LiveConnectionOptions options) => new HubLiveConnection(HubUri(api.Value.BaseUrl), options, configure);
}
```

`src/TechStrap.Admin/Options/LiveUpdatesOptions.cs` (new)

```csharp
namespace TechStrap.Admin.Options;

/// <summary>
/// The kill switch for live updates (PHASE-10b): <c>LiveUpdates:Enabled</c>, env <c>LIVEUPDATES__ENABLED</c>, on by default. Off, the Admin never opens a hub connection and draws no indicator, banner or presence bar.
/// The hub address is not a setting: it is <c>API__BASEURL</c> plus the hub path. Backoff, debounce and throttle are constants.
/// </summary>
public sealed class LiveUpdatesOptions
{
    public const string SectionName = "LiveUpdates";

    public bool Enabled { get; set; } = true;
}
```

`src/TechStrap.Admin/Features/Live/LiveServiceCollectionExtensions.cs` (new)

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TechStrap.Admin.Options;

namespace TechStrap.Admin.Features.Live;

public static class LiveServiceCollectionExtensions
{
    /// <summary>
    /// Registers live updates. <see cref="ITicketLiveClient"/> is scoped (one per circuit, disposed with it): the real client, or the one that does nothing when <c>LiveUpdates:Enabled</c> is off. The connection
    /// factory is a singleton that holds options only; the real client takes the circuit's own token provider and session expiry from its scope.
    /// </summary>
    public static IServiceCollection AddLiveFeatures(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LiveUpdatesOptions>()
            .Bind(configuration.GetSection(LiveUpdatesOptions.SectionName))
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ILiveConnectionFactory>(sp => new HubLiveConnectionFactory(sp.GetRequiredService<IOptions<SyntaxCircus.Blazor.Auth.ApiOptions>>()));
        services.AddScoped<SignalRTicketLiveClient>();
        services.AddScoped<NullTicketLiveClient>();
        services.AddScoped<ITicketLiveClient>(sp => sp.GetRequiredService<IOptions<LiveUpdatesOptions>>().Value.Enabled
            ? sp.GetRequiredService<SignalRTicketLiveClient>()
            : sp.GetRequiredService<NullTicketLiveClient>());
        return services;
    }
}
```

`src/TechStrap.Admin/Program.cs` (modify)

```diff
diff --git a/src/TechStrap.Admin/Program.cs b/src/TechStrap.Admin/Program.cs
--- a/src/TechStrap.Admin/Program.cs
+++ b/src/TechStrap.Admin/Program.cs
@@ -5,6 +5,7 @@ using TechStrap.Admin.Auth;
 using TechStrap.Admin.Clients;
 using TechStrap.Admin.Components;
 using TechStrap.Admin.Features.Kb;
+using TechStrap.Admin.Features.Live;
 using TechStrap.Admin.Features.Shell;
 using TechStrap.Admin.Features.Tickets;
 using TechStrap.Admin.Options;
@@ -41,6 +42,8 @@ builder.Services.AddTechStrapApiClients();
 builder.Services.AddShell();
 builder.Services.AddTicketFeatures();
 builder.Services.AddKbFeatures();
+// The live connection to the Api ticket hub (one per circuit), or a client that does nothing when LiveUpdates:Enabled is false.
+builder.Services.AddLiveFeatures(builder.Configuration);
 
 builder.Services.AddRazorComponents()
     .AddInteractiveServerComponents();
```



- [ ] **Step 7: Run the Live tests, then the whole Admin suite (GREEN)**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter-namespace TechStrap.Admin.Tests.Live`

Expected: PASS, `total: 32, failed: 0`.

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release`

Expected: PASS, `total: 1994, failed: 0` (the fake client is registered by the base class and nothing renders it yet).

Run: `dotnet build TechStrap.slnx -c Release`

Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 8: Record the mutations**

`$T\specs\m1.py` (the commands are the Live suite, the one Architecture test and the ConfigContract Pester file):

```python
LIVE = ["dotnet", "test", "--project", "tests/TechStrap.Admin.Tests", "-c", "Release", "--filter-namespace", "TechStrap.Admin.Tests.Live"]
ARCH = ["dotnet", "test", "--project", "tests/TechStrap.Architecture.Tests", "-c", "Release", "--filter-method", "*Admin_project_references_only_allowed_packages"]
PESTER = ["pwsh", "-NoProfile", "-File", "scripts/Invoke-ScriptTests.ps1", "-Output", "Minimal", "-Path", "scripts/tests/ConfigContract.Tests.ps1"]
S = "src/TechStrap.Admin/Features/Live/"

MUTATIONS = [
    ("1 retry cap becomes 60 s", S + "LiveRetryPolicy.cs", [("TimeSpan.FromSeconds(30);", "TimeSpan.FromSeconds(60);")], LIVE),
    ("2 a lapsed session still gets a delay", S + "LiveRetryPolicy.cs", [("shouldStop() ? null :", "shouldStop() ? TimeSpan.Zero :")], LIVE),
    ("3 the id memory is unbounded", S + "SignalRTicketLiveClient.cs", [("_seenOrder.Count > DeduplicationCapacity", "_seenOrder.Count > int.MaxValue")], LIVE),
    ("4 no de-duplication", S + "SignalRTicketLiveClient.cs", [("!Remember(change.EventId)", "Remember(change.EventId) && false")], LIVE),
    ("5 start is not guarded", S + "SignalRTicketLiveClient.cs", [("return _starting ??= RunAsync();", "return _starting = RunAsync();")], LIVE),
    ("6 a null token is retried", S + "SignalRTicketLiveClient.cs", [("_noToken = true;", "_noToken = false;")], LIVE),
    ("7 no re-join after a reconnect", S + "SignalRTicketLiveClient.cs", [("        if (connection is not null)\n        {\n            await RejoinAsync(connection);\n        }\n    }\n\n    private Task OnClosedAsync", "        await Task.CompletedTask;\n    }\n\n    private Task OnClosedAsync")], LIVE),
    ("8 no resync after a reconnect", S + "SignalRTicketLiveClient.cs", [("Raise(TicketChanged, LiveChangeRules.NewResync(_time.GetUtcNow()));", "_ = LiveChangeRules.NewResync(_time.GetUtcNow());")], LIVE),
    ("9 disposal leaves the change handler hooked", S + "SignalRTicketLiveClient.cs", [("            connection.TicketChanged -= OnTicketChanged;\n", "")], LIVE),
    ("10 disposal does not dispose the connection", S + "SignalRTicketLiveClient.cs", [("await connection.DisposeAsync();", "await Task.CompletedTask;")], LIVE),
    ("11 a resync can be own", S + "LiveChangeRules.cs", [("!IsResync(change) && ownAgentId", "ownAgentId")], LIVE),
    ("12 a resync does not concern a ticket", S + "LiveChangeRules.cs", [("IsResync(change) || change.TicketId", "change.TicketId")], LIVE),
    ("13 the kill switch is inverted", S + "LiveServiceCollectionExtensions.cs", [(".Value.Enabled\n", ".Value.Enabled == false\n")], LIVE),
    ("14 the switch is off by default", "src/TechStrap.Admin/Options/LiveUpdatesOptions.cs", [("Enabled { get; set; } = true;", "Enabled { get; set; } = false;")], LIVE),
    ("15 the hub path replaces the base path", S + "HubLiveConnection.cs", [("TicketHubRoutes.Path.TrimStart('/')", "TicketHubRoutes.Path")], LIVE),
    ("16 an unknown ticket is joined again", S + "SignalRTicketLiveClient.cs", [("            lock (_gate)\n            {\n                _joined.Remove(ticketId);\n            }\n\n            return null;", "            return null;")], LIVE),
    ("17 a join is not remembered", S + "SignalRTicketLiveClient.cs", [("            _joined.Add(ticketId);\n", "")], LIVE),
    ("18 one failing subscriber stops the rest", S + "SignalRTicketLiveClient.cs", [("handlers.GetInvocationList().Cast<Action<T>>())", "handlers.GetInvocationList().Cast<Action<T>>().Take(1))")], LIVE),
    ("19 the package is not allowed", "tests/TechStrap.Architecture.Tests/AdminRules.cs", [('        "Microsoft.AspNetCore.SignalR.Client",\n', "")], ARCH),
    ("20 compose sets the switch", "docker-compose.yml", [("      Api__BaseUrl: http://api/\n      # The \"View on portal\"", "      Api__BaseUrl: http://api/\n      LiveUpdates__Enabled: \"true\"\n      # The \"View on portal\"")], PESTER),
    ("21 the deploy template says false", "deploy/.env.admin.example", [("LIVEUPDATES__ENABLED=true", "LIVEUPDATES__ENABLED=false")], PESTER),
    ("22 appsettings default is false", "src/TechStrap.Admin/appsettings.json", [('"Enabled": true', '"Enabled": false')], PESTER),
]
```

`git add -A`, then, from the repository root, one batch in the foreground (about two minutes for 22 mutations):

```bash
python $T/run_muts.py $T/specs/m1.py $T/res_m1.txt
```

Every mutation must be KILLED (the real run):

| # | Mutation | File | Result |
| --- | --- | --- | --- |
| 1 | retry cap becomes 60 s | `Features/Live/LiveRetryPolicy.cs` | KILLED (1 failing) |
| 2 | a lapsed session still gets a delay | `Features/Live/LiveRetryPolicy.cs` | KILLED (2 failing) |
| 3 | the id memory is unbounded | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (1 failing) |
| 4 | no de-duplication | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (1 failing) |
| 5 | start is not guarded | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (1 failing) |
| 6 | a null token is retried | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (1 failing) |
| 7 | no re-join after a reconnect | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (1 failing) |
| 8 | no resync after a reconnect | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (1 failing) |
| 9 | disposal leaves the change handler hooked | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (1 failing) |
| 10 | disposal does not dispose the connection | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (2 failing) |
| 11 | a resync can be own | `Features/Live/LiveChangeRules.cs` | KILLED (1 failing) |
| 12 | a resync does not concern a ticket | `Features/Live/LiveChangeRules.cs` | KILLED (1 failing) |
| 13 | the kill switch is inverted | `Features/Live/LiveServiceCollectionExtensions.cs` | KILLED (2 failing) |
| 14 | the switch is off by default | `Options/LiveUpdatesOptions.cs` | KILLED (1 failing) |
| 15 | the hub path replaces the base path | `Features/Live/HubLiveConnection.cs` | KILLED (1 failing) |
| 16 | an unknown ticket is joined again | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (1 failing) |
| 17 | a join is not remembered | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (1 failing) |
| 18 | one failing subscriber stops the rest | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (1 failing) |
| 19 | the package is not allowed | `tests/TechStrap.Architecture.Tests/AdminRules.cs` | KILLED (1 failing) |
| 20 | compose sets the switch | `docker-compose.yml` | KILLED |
| 21 | the deploy template says false | `deploy/.env.admin.example` | KILLED |
| 22 | appsettings default is false | `appsettings.json` | KILLED |

`git status` must then show nothing but staged files.

- [ ] **Step 9: Commit**

```bash
git add -A
git diff --cached --stat
git commit -F - <<'MSG'
feat: PHASE-10b live client core, kill switch and package bump (D-046)

Bump SyntaxCircus.Blazor.Auth to 0.2.0, add the SignalR client to the Admin (AdminRules allowlist),
Features/Live: ITicketLiveClient, SignalRTicketLiveClient, NullTicketLiveClient, retry policy, change rules,
LiveUpdates:Enabled kill switch with its D-043 env edits, fake client in AdminComponentTest.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
MSG
```

---

### Task 2: The connection indicator, the one start, and the queue banner

**Review Focus pin:** 1 (nothing starts during a prerender, one start, the pages never start it, disposal unsubscribes and releases the timer), 4 (a burst gives one banner and one timer, nothing reloads or reorders until the click, the polite announcements) and 5 (switched off, nothing is drawn or started; a failing client never reaches the page).

**Files:**

- Create: `src/TechStrap.Admin/Features/Live/LiveCopy.cs`, `LiveConnectionIndicator.razor`, `LiveConnectionIndicator.razor.cs`, `QueueLiveBanner.razor`, `src/TechStrap.Admin/Styles/_live.scss`
- Modify: `src/TechStrap.Admin/Components/Layout/MainLayout.razor`, `src/TechStrap.Admin/Components/_Imports.razor`, `src/TechStrap.Admin/Features/_Imports.razor`, `src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor`, `TicketQueuePage.razor.cs`, `src/TechStrap.Admin/Styles/app.scss`
- Test (create): `tests/TechStrap.Admin.Tests/Live/LiveConnectionIndicatorTests.cs`, `MainLayoutLiveTests.cs`, `LivePrerenderHostTests.cs`, `QueueLiveBannerTests.cs`
- Test (modify): `tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs`, `tests/TechStrap.Admin.Tests/Components/LayoutResilienceTests.cs`

**Interfaces:**
- Consumes: Task 1's `ITicketLiveClient`, `LiveConnectionState`, `LiveChangeRules`, `FakeTicketLiveClient` (`LiveClient` in the base test class); `AgentSession` (`State`, `Agent.Id`, `Changed`); `TimeProvider.CreateTimer` (the pattern of `QueueFilterBar`); the page's existing `LoadAsync`, `RefreshAsync` and the hidden `role="status"` announcement.
- Produces:
  - `LiveCopy` (`Label`, `Connected` "Live", `Connecting`, `Reconnecting`, `Disconnected` "Offline", `QueueUpdated` "Queue updated – refresh", `NewActivity` "New activity – refresh", `For(LiveConnectionState)`) and `LiveDefaults` (`QueueBannerWindow` one second, `ComposingThrottle` four seconds, used by Task 3).
  - `LiveConnectionIndicator` (no parameters; draws the polite status region only when the client is enabled and `AgentSession.State` is Ready; the only component that calls `ITicketLiveClient.StartAsync`, from `OnAfterRender`; never throws), placed in `MainLayout` between `<main>` and `<StatusBar />`.
  - `QueueLiveBanner` (`[Parameter] EventCallback OnRefresh`; a button with `LiveCopy.QueueUpdated`).
  - `TicketQueuePage`: subscribes to `TicketChanged` in `OnInitialized`, opens a one-second window on the first change that is not the agent's own (or is a `Resync`), shows the banner and announces it, clears it (and the timer) on every load, unsubscribes and releases the timer on disposal.
  - Test support: `AdminComponentTest` registers a default signed-in `AgentSession` (a test that needs another registers its own after the base constructor); `LayoutResilienceTests` registers a `FakeTicketLiveClient` (it derives from `BunitContext`, not the base).

- [ ] **Step 1: Check the branch**

```bash
git switch feat/phase-10b-live-admin     # Task 1 is committed; `git status` is clean
```

- [ ] **Step 2: Write the failing tests**

The indicator's tests prove the start rules (once, after the first render, only when the session is Ready, never for an agent without access) and the states. The prerender test is the host-level proof of the spike: the real client with a counting connection factory, a GET of two pages, and no connection created. The queue tests use `CountingTimeProvider` to prove one timer for a burst and none left behind; the one-second window is written as a literal in the test on purpose.

`tests/TechStrap.Admin.Tests/Live/LiveConnectionIndicatorTests.cs` (new)

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Live;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Live;

/// <summary>
/// The connection indicator, and the one place the live client is started: after the first render in the circuit (never during prerendering) and only once the session is Ready, however often the
/// layout renders again. Each state has its text in a polite live region, and a failing client never reaches the page.
/// </summary>
public sealed class LiveConnectionIndicatorTests : AdminComponentTest
{
    [Theory]
    [InlineData(LiveConnectionState.Connected, LiveCopy.Connected)]
    [InlineData(LiveConnectionState.Connecting, LiveCopy.Connecting)]
    [InlineData(LiveConnectionState.Reconnecting, LiveCopy.Reconnecting)]
    [InlineData(LiveConnectionState.Disconnected, LiveCopy.Disconnected)]
    public void Each_state_is_drawn_with_its_text_in_a_polite_status_region(LiveConnectionState state, string text)
    {
        LiveClient.SetState(state);

        var cut = Render<LiveConnectionIndicator>();

        var region = cut.Find(".ts-live");
        region.GetAttribute("role").ShouldBe("status");
        region.GetAttribute("aria-live").ShouldBe("polite");
        region.GetAttribute("data-live-state").ShouldBe(state.ToString().ToLowerInvariant());
        region.TextContent.ShouldContain(text);
        region.TextContent.ShouldContain(LiveCopy.Label, Case.Sensitive);
    }

    [Fact]
    public void A_change_of_state_updates_the_same_live_region_so_it_is_announced()
    {
        var cut = Render<LiveConnectionIndicator>();
        cut.Find(".ts-live").TextContent.ShouldContain(LiveCopy.Connected);

        LiveClient.SetState(LiveConnectionState.Reconnecting);

        cut.WaitForAssertion(() => cut.Find(".ts-live").TextContent.ShouldContain(LiveCopy.Reconnecting));
        cut.FindAll("[aria-live]").Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_state_raised_on_another_thread_is_marshalled_to_the_renderer()
    {
        var cut = Render<LiveConnectionIndicator>();

        await Task.Run(() => LiveClient.SetState(LiveConnectionState.Disconnected), Xunit.TestContext.Current.CancellationToken);

        cut.WaitForAssertion(() => cut.Find(".ts-live").TextContent.ShouldContain(LiveCopy.Disconnected));
    }

    [Fact]
    public void The_client_is_started_once_from_the_first_render_and_not_again_on_later_renders()
    {
        var cut = Render<LiveConnectionIndicator>();
        LiveClient.StartCalls.ShouldBe(1);

        LiveClient.SetState(LiveConnectionState.Reconnecting);
        LiveClient.SetState(LiveConnectionState.Connected);
        cut.WaitForAssertion(() => cut.Find(".ts-live").TextContent.ShouldContain(LiveCopy.Connected));
        cut.Render();

        LiveClient.StartCalls.ShouldBe(1);
    }

    [Fact]
    public void Switched_off_nothing_is_drawn_and_nothing_is_started()
    {
        LiveClient.IsEnabled = false;

        var cut = Render<LiveConnectionIndicator>();

        cut.Markup.Trim().ShouldBeEmpty();
        LiveClient.StartCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Before_the_session_is_ready_nothing_is_drawn_and_nothing_is_started_then_it_starts_once()
    {
        var agents = Substitute.For<IAgentsClient>();
        var me = new AgentDto(AgentSessions.SamId, "Sam Ortiz", "sam@example.com", AgentRoles.Agent, IsActive: true, PublicDisplayName: null, LastSeenAt: null);
        var answer = new TaskCompletionSource<Result<AgentDto>>();
        agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(_ => answer.Task);
        var session = new AgentSession(agents);
        Services.AddSingleton(session);
        var cut = Render<LiveConnectionIndicator>();
        var load = session.EnsureLoadedAsync(Xunit.TestContext.Current.CancellationToken);

        cut.Markup.Trim().ShouldBeEmpty();
        LiveClient.StartCalls.ShouldBe(0);

        answer.SetResult(Result<AgentDto>.Success(me));
        await load;

        cut.WaitForAssertion(() => cut.Find(".ts-live").TextContent.ShouldContain(LiveCopy.Connected));
        LiveClient.StartCalls.ShouldBe(1);
    }

    [Fact]
    public async Task An_agent_without_access_never_starts_a_connection()
    {
        var agents = Substitute.For<IAgentsClient>();
        agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Failure(new ResultError("agent-inactive", "No access.", ResultErrorKind.Forbidden)));
        var session = new AgentSession(agents);
        Services.AddSingleton(session);
        var cut = Render<LiveConnectionIndicator>();

        await session.EnsureLoadedAsync(Xunit.TestContext.Current.CancellationToken);

        session.State.ShouldBe(AgentSessionState.NoAccess);
        cut.Markup.Trim().ShouldBeEmpty();
        LiveClient.StartCalls.ShouldBe(0);
    }

    [Fact]
    public void A_client_that_fails_to_start_never_breaks_the_page_and_only_the_type_is_logged()
    {
        var logs = new RecordingLoggerProvider();
        Services.AddLogging(logging => logging.AddProvider(logs));
        LiveClient.Failure = new InvalidOperationException("hub down");
        LiveClient.SetState(LiveConnectionState.Reconnecting);

        var cut = Render<LiveConnectionIndicator>();

        LiveClient.StartCalls.ShouldBe(1);
        cut.Find(".ts-live").TextContent.ShouldContain(LiveCopy.Reconnecting);
        logs.Lines.ShouldContain(line => line.Contains("InvalidOperationException", StringComparison.Ordinal));
        logs.Lines.ShouldNotContain(line => line.Contains("hub down", StringComparison.Ordinal));
    }

    [Fact]
    public void Disposal_unsubscribes_from_the_client_and_the_session()
    {
        var cut = Render<LiveConnectionIndicator>();
        LiveClient.HasSubscribers.ShouldBeTrue();

        cut.Instance.Dispose();

        LiveClient.HasSubscribers.ShouldBeFalse();
        Should.NotThrow(() => LiveClient.SetState(LiveConnectionState.Disconnected));
        cut.Markup.ShouldContain(LiveCopy.Connected, Case.Sensitive);
    }
}
```

`tests/TechStrap.Admin.Tests/Live/MainLayoutLiveTests.cs` (new)

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Features.Live;
using TechStrap.Admin.Tests.Components;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Live;

/// <summary>The indicator sits in <c>MainLayout</c>, which is outside the agent gate: it draws and starts the connection only once the gate has admitted the agent.</summary>
public sealed class MainLayoutLiveTests : AdminComponentTest
{
    public MainLayoutLiveTests() => this.AddAgentShell();

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<p id=\"page\">page</p>"))));

    [Fact]
    public void The_layout_draws_one_indicator_and_starts_the_connection_once_the_agent_is_admitted()
    {
        var cut = RenderLayout();

        cut.WaitForAssertion(() => cut.FindAll(".ts-live").Count.ShouldBe(1));
        LiveClient.StartCalls.ShouldBe(1);
        cut.Find("main.ts-main #page").TextContent.ShouldBe("page");
    }

    [Fact]
    public void Switched_off_the_layout_draws_no_indicator_and_starts_nothing()
    {
        LiveClient.IsEnabled = false;

        var cut = RenderLayout();

        cut.WaitForAssertion(() => cut.Find("main.ts-main #page").TextContent.ShouldBe("page"));
        cut.FindAll(".ts-live").ShouldBeEmpty();
        LiveClient.StartCalls.ShouldBe(0);
    }

    [Fact]
    public void A_failing_client_leaves_the_layout_and_the_page_working()
    {
        LiveClient.Failure = new InvalidOperationException("hub down");

        var cut = RenderLayout();

        cut.WaitForAssertion(() => cut.Find("main.ts-main #page").TextContent.ShouldBe("page"));
        cut.Find(".ts-brand").ShouldNotBeNull();
    }
}
```

`tests/TechStrap.Admin.Tests/Live/LivePrerenderHostTests.cs` (new)

```csharp
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Admin.Features.Live;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Live;

/// <summary>
/// The pages are prerendered (<c>InteractiveServer</c> with prerendering on): the first render runs on the HTTP request with no circuit. The real client is resolved there (the indicator and the pages inject it)
/// but must never open a connection: starting is allowed only from <c>OnAfterRenderAsync</c>, which prerendering does not run.
/// </summary>
public sealed class LivePrerenderHostTests
{
    [Theory(Timeout = 60000)]
    [InlineData("/")]
    [InlineData("/queue/mine")]
    public async Task Prerendering_a_page_opens_no_hub_connection(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        var connections = new FakeLiveConnectionFactory();
        await using var factory = new AdminFactory(configureServices: services =>
        {
            services.RemoveAll<ILiveConnectionFactory>();
            services.AddSingleton<ILiveConnectionFactory>(connections);
        });
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var response = await client.GetAsync(path, ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("ts-shell");
        connections.Created.ShouldBeEmpty("a prerender must not start the live connection");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact(Timeout = 60000)]
    public async Task Switched_off_the_prerendered_page_carries_no_live_markup()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { ["LiveUpdates:Enabled"] = "false" });
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await (await client.GetAsync("/", ct)).Content.ReadAsStringAsync(ct);

        html.ShouldContain("ts-shell");
        html.ShouldNotContain("ts-live");
    }
}
```

`tests/TechStrap.Admin.Tests/Live/QueueLiveBannerTests.cs` (new)

```csharp
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Live;
using TechStrap.Admin.Features.Queue;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;
using static TechStrap.Admin.Tests.Live.LiveTestData;

namespace TechStrap.Admin.Tests.Live;

/// <summary>
/// The queue's live behavior (T13): a change by another agent, or a resync, raises ONE banner after a one-second window; the agent's own changes raise none; nothing reloads or reorders until the banner is
/// clicked, and the click is the ordinary load with the current filters. The page never starts the connection (the indicator does) and releases its timer and its subscription when it goes.
/// </summary>
public sealed class QueueLiveBannerTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly NavigationManager _navigation;
    private readonly CountingTimeProvider _timers;

    // The requirement is a one-second window, written here as a literal on purpose: a change of the constant must fail these tests.
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    public QueueLiveBannerTests()
    {
        _timers = new CountingTimeProvider(Time);
        Services.AddSingleton<TimeProvider>(_timers);
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => TestData.Ok(TestData.Page([TestData.Summary("ORB-1"), TestData.Summary("ORB-2", "Billing question")])));
        _tickets.GetCountsAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Counts()));
        var products = Substitute.For<IProductsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        var tags = Substitute.For<ITagsClient>();
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(products);
        Services.AddSingleton(tags);
        JSInterop.SetupModule("./js/queue.js").SetupVoid("scrollSelectedIntoView", _ => true).SetVoidResult();
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private IRenderedComponent<TicketQueuePage> RenderQueue(string? view = null, string query = "")
    {
        _navigation.NavigateTo($"/queue/{view}{query}");
        return Render<TicketQueuePage>(p => p.Add(c => c.View, view));
    }

    private int ListCalls() => _tickets.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ITicketsClient.ListAsync));

    private static string[] RowNumbers(IRenderedComponent<TicketQueuePage> cut) => [.. cut.FindAll("tbody tr").Select(row => row.QuerySelector("a")!.GetAttribute("href")!)];

    [Fact]
    public void A_change_by_another_agent_raises_the_banner_after_the_window_and_reloads_and_reorders_nothing()
    {
        var cut = RenderQueue();
        var rows = RowNumbers(cut);

        LiveClient.RaiseChange(Change(actor: ColleagueId));
        cut.WaitForAssertion(() => _timers.LiveTimers.ShouldBe(1));
        Time.Advance(OneSecond - TimeSpan.FromMilliseconds(1));
        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
        Time.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => cut.Find(".ts-live-banner button").TextContent.ShouldBe(LiveCopy.QueueUpdated));
        ListCalls().ShouldBe(1);
        RowNumbers(cut).ShouldBe(rows);
        _timers.LiveTimers.ShouldBe(0);
    }

    [Fact]
    public async Task A_burst_of_changes_gives_one_banner_and_one_timer()
    {
        var cut = RenderQueue();

        for (var i = 0; i < 20; i++)
        {
            LiveClient.RaiseChange(Change(ticketId: Guid.NewGuid(), actor: ColleagueId));
        }

        cut.WaitForAssertion(() => _timers.LiveTimers.ShouldBe(1));
        Time.Advance(OneSecond);

        cut.WaitForAssertion(() => cut.FindAll(".ts-live-banner").Count.ShouldBe(1));
        LiveClient.RaiseChange(Change(actor: ColleagueId));

        // The renderer runs its queued work in order, so once this returns the change has been handled: no second timer was started for it.
        await cut.InvokeAsync(() => { });
        _timers.LiveTimers.ShouldBe(0);
        Time.Advance(TimeSpan.FromSeconds(5));
        cut.FindAll(".ts-live-banner").Count.ShouldBe(1);
        _timers.LiveTimers.ShouldBe(0);
    }

    [Fact]
    public void The_agents_own_change_raises_no_banner_and_starts_no_timer()
    {
        var cut = RenderQueue();

        LiveClient.RaiseChange(Change(actor: MeId));
        Time.Advance(TimeSpan.FromSeconds(5));

        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
        _timers.LiveTimers.ShouldBe(0);
    }

    [Fact]
    public void A_change_with_no_actor_raises_the_banner()
    {
        var cut = RenderQueue();

        LiveClient.RaiseChange(Change() with { ActorAgentId = null });
        cut.WaitForAssertion(() => _timers.LiveTimers.ShouldBe(1));
        Time.Advance(OneSecond);

        cut.WaitForAssertion(() => cut.FindAll(".ts-live-banner").Count.ShouldBe(1));
    }

    [Fact]
    public void A_resync_raises_the_banner_even_when_it_names_the_agent()
    {
        var cut = RenderQueue();

        LiveClient.RaiseChange(Resync() with { ActorAgentId = MeId });
        cut.WaitForAssertion(() => _timers.LiveTimers.ShouldBe(1));
        Time.Advance(OneSecond);

        cut.WaitForAssertion(() => cut.FindAll(".ts-live-banner").Count.ShouldBe(1));
    }

    [Fact]
    public void The_banner_is_announced_in_the_polite_status_region()
    {
        var cut = RenderQueue();

        LiveClient.RaiseChange(Change());
        cut.WaitForAssertion(() => _timers.LiveTimers.ShouldBe(1));
        Time.Advance(OneSecond);

        cut.WaitForAssertion(() => cut.Find("p.visually-hidden[role=status]").TextContent.ShouldContain(LiveCopy.QueueUpdated));
    }

    [Fact]
    public void Clicking_the_banner_reloads_with_the_current_filters_and_clears_it()
    {
        var cut = RenderQueue("mine", "?status=Open");
        LiveClient.RaiseChange(Change());
        cut.WaitForAssertion(() => _timers.LiveTimers.ShouldBe(1));
        Time.Advance(OneSecond);
        cut.WaitForAssertion(() => cut.FindAll(".ts-live-banner").Count.ShouldBe(1));

        cut.Find(".ts-live-banner button").Click();

        cut.WaitForAssertion(() => ListCalls().ShouldBe(2));
        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
        var requests = _tickets.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ITicketsClient.ListAsync)).Select(call => (ListTicketsRequest)call.GetArguments()[0]!).ToList();
        requests[1].ShouldBe(requests[0]);
        requests[1].View.ShouldBe(TicketViews.Mine);
        requests[1].Status.ShouldBe(TicketStatuses.Open);
    }

    [Fact]
    public void The_refresh_button_of_the_filter_bar_clears_the_banner_too()
    {
        var cut = RenderQueue();
        LiveClient.RaiseChange(Change());
        cut.WaitForAssertion(() => _timers.LiveTimers.ShouldBe(1));
        Time.Advance(OneSecond);
        cut.WaitForAssertion(() => cut.FindAll(".ts-live-banner").Count.ShouldBe(1));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == QueueCopy.Refresh).Click();

        cut.WaitForAssertion(() => cut.FindAll(".ts-live-banner").ShouldBeEmpty());
    }

    [Fact]
    public void A_reload_cancels_a_pending_banner_and_its_timer()
    {
        var cut = RenderQueue();
        LiveClient.RaiseChange(Change());
        cut.WaitForAssertion(() => _timers.LiveTimers.ShouldBe(1));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == QueueCopy.Refresh).Click();
        cut.WaitForAssertion(() => ListCalls().ShouldBe(2));

        // The reload showed everything up to now, so the pending banner is canceled with its timer.
        _timers.LiveTimers.ShouldBe(0);
        Time.Advance(TimeSpan.FromSeconds(5));
        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
    }

    [Fact]
    public void Switched_off_a_change_raises_nothing()
    {
        LiveClient.IsEnabled = false;
        var cut = RenderQueue();

        LiveClient.RaiseChange(Change());
        Time.Advance(TimeSpan.FromSeconds(5));

        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
        _timers.LiveTimers.ShouldBe(0);
    }

    [Fact]
    public void The_page_never_starts_the_connection_and_never_joins_a_ticket()
    {
        RenderQueue();

        LiveClient.StartCalls.ShouldBe(0);
        LiveClient.Joined.ShouldBeEmpty();
    }

    [Fact]
    public async Task Disposal_unsubscribes_and_releases_a_pending_timer()
    {
        var cut = RenderQueue();
        LiveClient.RaiseChange(Change());
        cut.WaitForAssertion(() => _timers.LiveTimers.ShouldBe(1));

        await cut.Instance.DisposeAsync();

        LiveClient.HasSubscribers.ShouldBeFalse();
        _timers.LiveTimers.ShouldBe(0);
        Should.NotThrow(() => LiveClient.RaiseChange(Change()));
        Should.NotThrow(() => Time.Advance(TimeSpan.FromSeconds(5)));
    }
}
```



The base class gets a default session (the queue and the detail page now ask for `AgentSession`), and the one test class that renders the layout without the base registers the fake itself:

`tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs` (modify)

```diff
diff --git a/tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs b/tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs
--- a/tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs
+++ b/tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs
@@ -19,6 +19,8 @@ public abstract class AdminComponentTest : BunitContext
         Services.AddSingleton<TimeProvider>(Time);
 
         // Every component that draws live state asks for the client; this one never touches a hub. A test raises its events and reads what the components called.
+        // The signed-in agent (Sam) every live component compares a change's actor with; a test that needs another session registers its own after this.
+        Services.AddSingleton(_ => AgentSessions.SignedIn());
         LiveClient = new FakeTicketLiveClient();
         Services.AddSingleton<ITicketLiveClient>(LiveClient);
```

`tests/TechStrap.Admin.Tests/Components/LayoutResilienceTests.cs` (modify)

```diff
diff --git a/tests/TechStrap.Admin.Tests/Components/LayoutResilienceTests.cs b/tests/TechStrap.Admin.Tests/Components/LayoutResilienceTests.cs
--- a/tests/TechStrap.Admin.Tests/Components/LayoutResilienceTests.cs
+++ b/tests/TechStrap.Admin.Tests/Components/LayoutResilienceTests.cs
@@ -8,6 +8,7 @@ using TechStrap.Admin.Auth;
 using TechStrap.Admin.Clients;
 using TechStrap.Admin.Components.Layout;
 using TechStrap.Admin.Components.Ui;
+using TechStrap.Admin.Features.Live;
 using TechStrap.Admin.Features.Shell;
 using TechStrap.Admin.Tests.Support;
 using TechStrap.Contracts.Agents;
@@ -31,6 +32,9 @@ public sealed class LayoutResilienceTests : BunitContext
         Services.AddLogging(logging => logging.AddProvider(_logs));
         Services.AddShell();
         Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
+
+        // MainLayout draws the live indicator, which asks for the client; this class is not an AdminComponentTest, so it registers one itself.
+        Services.AddSingleton<ITicketLiveClient>(new FakeTicketLiveClient());
     }
 
     private void NoLeak() =>
```



- [ ] **Step 3: Run the build to see it fail (RED)**

Run: `dotnet build tests/TechStrap.Admin.Tests -c Release`

Expected: FAIL to compile (the real run): `8 CS0103: The name 'LiveCopy' does not exist in the current context`; the other new names (`LiveConnectionIndicator`, `LiveDefaults`, `QueueLiveBanner`) are reported once `LiveCopy` exists.

- [ ] **Step 4: Write the copy, the indicator, the banner, the styles and the layout slot**

`LiveCopy` holds the words and the two constants; the indicator is the only starter (read its `OnAfterRender`: the start is guarded by its own flag and by the client's guard, is not awaited, and its failure is logged by type); the banner is a button; `_live.scss` has no animation and never relies on color alone.

`src/TechStrap.Admin/Features/Live/LiveCopy.cs` (new)

```csharp
namespace TechStrap.Admin.Features.Live;

/// <summary>The words of the live-update screens. Plain text; the en dash is written as an escape so no source file holds a raw non-ASCII character.</summary>
public static class LiveCopy
{
    /// <summary>Said before the state by a screen reader (hidden on screen): "Live updates: Reconnecting".</summary>
    public const string Label = "Live updates:";

    public const string Connected = "Live";
    public const string Connecting = "Connecting";
    public const string Reconnecting = "Reconnecting";
    public const string Disconnected = "Offline";

    /// <summary>The queue banner: something changed somewhere; nothing has moved yet.</summary>
    public const string QueueUpdated = "Queue updated – refresh";

    /// <summary>The ticket banner: another agent or the customer did something here; the page shows the old version until it is clicked.</summary>
    public const string NewActivity = "New activity – refresh";

    public static string For(LiveConnectionState state) => state switch
    {
        LiveConnectionState.Connected => Connected,
        LiveConnectionState.Connecting => Connecting,
        LiveConnectionState.Reconnecting => Reconnecting,
        _ => Disconnected,
    };
}

/// <summary>The live screens' named constants (a literal number never sits at a call site).</summary>
public static class LiveDefaults
{
    /// <summary>
    /// The queue banner waits this long after the first change before it appears, and every change in the window joins it: a burst raises one banner, and a steady trickle still shows it within this time
    /// (a window that restarted on every change would never fire).
    /// </summary>
    public static readonly TimeSpan QueueBannerWindow = TimeSpan.FromSeconds(1);

    /// <summary>The composer sends "I am typing" at most this often while the agent types. The server's lease is 10 seconds, so a refresh every 4 keeps "replying" alive.</summary>
    public static readonly TimeSpan ComposingThrottle = TimeSpan.FromSeconds(4);
}
```

`src/TechStrap.Admin/Features/Live/LiveConnectionIndicator.razor` (new)

```razor
@if (Visible)
{
    <span class="ts-live ts-live--@StateKey" role="status" aria-live="polite" data-live-state="@StateKey">
        <span class="ts-live__dot" aria-hidden="true"></span>
        <span class="visually-hidden">@LiveCopy.Label </span><span class="ts-live__text">@LiveCopy.For(LiveClient.State)</span>
    </span>
}
```

`src/TechStrap.Admin/Features/Live/LiveConnectionIndicator.razor.cs` (new)

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using TechStrap.Admin.Auth;

namespace TechStrap.Admin.Features.Live;

/// <summary>
/// The live-connection indicator in <c>MainLayout</c> and the one component that starts the connection. The layout sits outside the agent gate, so it draws and starts only when the session is Ready.
/// The start is in <c>OnAfterRenderAsync</c> on purpose: prerendering (the pages render once on the HTTP request, with no circuit) never runs it, so only a real circuit ever opens a connection, and the
/// client's own start guard keeps it to one however often this runs. The status region is polite: a change of state is announced, never shouted.
/// </summary>
public sealed partial class LiveConnectionIndicator : IDisposable
{
    private bool _started;
    private bool _disposed;

    [Inject]
    private ITicketLiveClient LiveClient { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private ILogger<LiveConnectionIndicator> Logger { get; set; } = default!;

    private bool Visible => LiveClient.IsEnabled && Session.State == AgentSessionState.Ready;

    private string StateKey => LiveClient.State.ToString().ToLowerInvariant();

    protected override void OnInitialized()
    {
        LiveClient.StateChanged += OnStateChanged;
        Session.Changed += OnSessionChanged;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (!_started && Visible)
        {
            _started = true;
            _ = StartAsync();
        }
    }

    /// <summary>Never awaited by the render and never throws: a live failure shows as the indicator's state, not as an error in the page.</summary>
    private async Task StartAsync()
    {
        try
        {
            await LiveClient.StartAsync();
        }
        catch (Exception exception)
        {
            Logger.LogWarning("The live connection could not be started ({ExceptionType}).", exception.GetType().Name);
        }
    }

    private void OnStateChanged(LiveConnectionState state) => Refresh();

    private void OnSessionChanged() => Refresh();

    private void Refresh()
    {
        if (!_disposed)
        {
            _ = InvokeAsync(StateHasChanged);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        LiveClient.StateChanged -= OnStateChanged;
        Session.Changed -= OnSessionChanged;
    }
}
```

`src/TechStrap.Admin/Features/Live/QueueLiveBanner.razor` (new)

```razor
<div class="ts-live-banner">
    <button type="button" class="btn btn-outline-secondary" @onclick="OnRefresh">@LiveCopy.QueueUpdated</button>
</div>

@code {
    /// <summary>Raised on click: the page runs its ordinary load with the current filters.</summary>
    [Parameter, EditorRequired]
    public EventCallback OnRefresh { get; set; }
}
```

`src/TechStrap.Admin/Styles/_live.scss` (new)

```scss
// PHASE-10b: the live-connection indicator and the "refresh" banners. Nothing here moves the page: the banners only offer a reload, and the indicator never uses color alone (its text always says the state).

.ts-live {
  display: flex;
  flex: 0 0 auto;
  gap: 6px;
  align-items: center;
  justify-content: flex-end;
  padding: 2px 16px;
  font: 500 .6875rem var(--ts-font-mono);
  color: var(--ink-2);
}

.ts-live__dot {
  width: 8px;
  height: 8px;
  border: 1px solid var(--rule-strong);
  border-radius: 50%;
}

.ts-live--connected .ts-live__dot {
  background: var(--st-open);
}

.ts-live--connecting .ts-live__dot,
.ts-live--reconnecting .ts-live__dot {
  background: var(--st-pending);
}

.ts-live-banner {
  display: flex;
  gap: 12px;
  align-items: center;
  margin: 8px 0;
  padding: 8px 12px;
  background: var(--sheet);
  border: 2px solid var(--st-pending);

  p {
    margin: 0;
  }
}
```

`src/TechStrap.Admin/Styles/app.scss` (modify)

```diff
diff --git a/src/TechStrap.Admin/Styles/app.scss b/src/TechStrap.Admin/Styles/app.scss
--- a/src/TechStrap.Admin/Styles/app.scss
+++ b/src/TechStrap.Admin/Styles/app.scss
@@ -59,6 +59,7 @@
 @import "statusbar";
 @import "queue";
 @import "ticket";
+@import "live";
 @import "composer";
 @import "settings";
 @import "api-keys";
```

`src/TechStrap.Admin/Components/Layout/MainLayout.razor` (modify)

```diff
diff --git a/src/TechStrap.Admin/Components/Layout/MainLayout.razor b/src/TechStrap.Admin/Components/Layout/MainLayout.razor
--- a/src/TechStrap.Admin/Components/Layout/MainLayout.razor
+++ b/src/TechStrap.Admin/Components/Layout/MainLayout.razor
@@ -13,6 +13,7 @@
                 <AgentGate>@Body</AgentGate>
             </GlobalErrorBoundary>
         </main>
+        <LiveConnectionIndicator />
         <StatusBar />
     </div>
 </div>
```

`src/TechStrap.Admin/Components/_Imports.razor` (modify)

```diff
diff --git a/src/TechStrap.Admin/Components/_Imports.razor b/src/TechStrap.Admin/Components/_Imports.razor
--- a/src/TechStrap.Admin/Components/_Imports.razor
+++ b/src/TechStrap.Admin/Components/_Imports.razor
@@ -12,4 +12,5 @@
 @using TechStrap.Admin.Components.Pages
 @using TechStrap.Admin.Components.Layout
 @using TechStrap.Admin.Components.Ui
+@using TechStrap.Admin.Features.Live
 @using TechStrap.Admin.Features.Shell
```

`src/TechStrap.Admin/Features/_Imports.razor` (modify)

```diff
diff --git a/src/TechStrap.Admin/Features/_Imports.razor b/src/TechStrap.Admin/Features/_Imports.razor
--- a/src/TechStrap.Admin/Features/_Imports.razor
+++ b/src/TechStrap.Admin/Features/_Imports.razor
@@ -7,5 +7,6 @@
 @using TechStrap.Admin.Auth
 @using TechStrap.Admin.Clients
 @using TechStrap.Admin.Components.Ui
+@using TechStrap.Admin.Features.Live
 @using TechStrap.Admin.Features.Shell
 @using TechStrap.Contracts.Tickets
```



- [ ] **Step 5: Write the queue's live behavior**

The page asks for the client, the session and the clock. The handler hops to the renderer, ignores the agent's own change, and opens one window; the timer's callback shows the banner; every load clears both; disposal releases them. The click is the existing `RefreshAsync`, so the filters in the URL apply.

`src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor` (modify)

```diff
diff --git a/src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor b/src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor
--- a/src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor
+++ b/src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor
@@ -9,6 +9,10 @@
     <QueueViewTabs ActiveView="@_filter.View" Counts="_counts" HrefFor="TabHref" />
     <QueueFilterBar @ref="_filterBar" Filter="_filter" Products="_products" Tags="_tags" OnChanged="OnFilterChangedAsync" OnRefresh="RefreshAsync" />
     <p class="visually-hidden" role="status">@_announcement</p>
+    @if (_liveChanged)
+    {
+        <QueueLiveBanner OnRefresh="RefreshAsync" />
+    }
 
     @if (_loading && _page is null)
     {
```

`src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor.cs` (modify)

```diff
diff --git a/src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor.cs b/src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor.cs
--- a/src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor.cs
+++ b/src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor.cs
@@ -1,10 +1,13 @@
 using Microsoft.AspNetCore.Components;
 using Microsoft.JSInterop;
+using TechStrap.Admin.Auth;
 using TechStrap.Admin.Clients;
 using TechStrap.Admin.Components.Ui;
+using TechStrap.Admin.Features.Live;
 using TechStrap.Admin.Features.Shell;
 using TechStrap.Admin.Features.Tickets;
 using TechStrap.Contracts.Tickets;
+using TechStrap.Contracts.Live;
 using TechStrap.Contracts.Paging;
 using TechStrap.Contracts.Products;
 using TechStrap.Contracts.Tags;
@@ -38,6 +41,8 @@ public sealed partial class TicketQueuePage : IAsyncDisposable
     private bool _notSpamBusy;
     private bool _notSpamError;
     private bool _disposed;
+    private bool _liveChanged;
+    private ITimer? _liveTimer;
     private int _selected = -1;
 
     [Inject]
@@ -61,6 +66,15 @@ public sealed partial class TicketQueuePage : IAsyncDisposable
     [Inject]
     private IJSRuntime Js { get; set; } = default!;
 
+    [Inject]
+    private ITicketLiveClient LiveClient { get; set; } = default!;
+
+    [Inject]
+    private AgentSession Session { get; set; } = default!;
+
+    [Inject]
+    private TimeProvider Time { get; set; } = default!;
+
     /// <summary>The route segment: <c>unassigned</c>, <c>mine</c>, <c>open</c>, <c>pending</c>, <c>all</c> or <c>spam</c>. Absent means the default view.</summary>
     [Parameter]
     public string? View { get; set; }
@@ -100,7 +114,11 @@ public sealed partial class TicketQueuePage : IAsyncDisposable
         _ => QueueCopy.NoTicketsHeading,
     };
 
-    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;
+    protected override void OnInitialized()
+    {
+        Shortcuts.Pressed += OnShortcutAsync;
+        LiveClient.TicketChanged += OnLiveChange;
+    }
 
     protected override async Task OnParametersSetAsync()
     {
@@ -180,6 +198,9 @@ public sealed partial class TicketQueuePage : IAsyncDisposable
         _error = null;
         _notSpamError = false;
 
+        // The reload shows everything up to now: a banner that was waiting (or about to appear) is answered.
+        ClearLiveBanner();
+
         try
         {
             var list = Tickets.ListAsync(filter.ToRequest(), cts.Token);
@@ -247,6 +268,53 @@ public sealed partial class TicketQueuePage : IAsyncDisposable
 
     private Task RefreshAsync() => LoadAsync();
 
+    /// <summary>
+    /// A change reached the queue group (D-046: every change goes to every agent). The agent's own changes are ignored (their own write already refreshed their page); every other change, and a resync, asks for ONE banner
+    /// after a short window. Nothing reloads and nothing reorders until the banner is clicked. It runs on a pool thread, so it hops to the renderer first.
+    /// </summary>
+    private void OnLiveChange(TicketChangedDto change)
+    {
+        if (_disposed || !LiveClient.IsEnabled)
+        {
+            return;
+        }
+
+        _ = InvokeAsync(() =>
+        {
+            if (_disposed || _liveChanged || _liveTimer is not null || LiveChangeRules.IsOwn(change, Session.Agent?.Id))
+            {
+                return;
+            }
+
+            _liveTimer = Time.CreateTimer(_ => _ = InvokeAsync(ShowLiveBanner), null, LiveDefaults.QueueBannerWindow, Timeout.InfiniteTimeSpan);
+        });
+    }
+
+    private void ShowLiveBanner()
+    {
+        ReleaseLiveTimer();
+        if (_disposed)
+        {
+            return;
+        }
+
+        _liveChanged = true;
+        _announcement = LiveCopy.QueueUpdated;
+        StateHasChanged();
+    }
+
+    private void ClearLiveBanner()
+    {
+        ReleaseLiveTimer();
+        _liveChanged = false;
+    }
+
+    private void ReleaseLiveTimer()
+    {
+        _liveTimer?.Dispose();
+        _liveTimer = null;
+    }
+
     private Task RetryAsync() => LoadAsync();
 
     private string TabHref(string view) => QueueLinks.Uri(_filter with { View = view, Page = 1 });
@@ -383,6 +451,8 @@ public sealed partial class TicketQueuePage : IAsyncDisposable
     {
         _disposed = true;
         Shortcuts.Pressed -= OnShortcutAsync;
+        LiveClient.TicketChanged -= OnLiveChange;
+        ReleaseLiveTimer();
         _lifetime.Cancel();
         _cts?.Dispose();
         _lifetime.Dispose();
```



- [ ] **Step 6: Run the tests (GREEN)**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter-namespace TechStrap.Admin.Tests.Live`

Expected: PASS, `total: 62, failed: 0`.

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release` (Expected: `total: 2024, failed: 0`) and `dotnet test --project tests/TechStrap.Architecture.Tests -c Release` (Expected: `total: 295, failed: 0`: no inline script or style, no `HttpClient` in a component, the Admin still references Contracts and Hosting only).

- [ ] **Step 7: Record the mutations**

`$T\specs\m2.py`:

```python
LIVE = ["dotnet", "test", "--project", "tests/TechStrap.Admin.Tests", "-c", "Release", "--filter-namespace", "TechStrap.Admin.Tests.Live"]
S = "src/TechStrap.Admin/Features/Live/"
Q = "src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor.cs"

MUTATIONS = [
    ("1 the client is started on every render", S + "LiveConnectionIndicator.razor.cs", [("            _started = true;\n", "            _started = false;\n")], LIVE),
    ("2 the client is started before the session is ready", S + "LiveConnectionIndicator.razor.cs", [("if (!_started && Visible)", "if (!_started)")], LIVE),
    ("3 the indicator ignores the kill switch", S + "LiveConnectionIndicator.razor.cs", [("LiveClient.IsEnabled && Session.State", "Session.State")], LIVE),
    ("4 the region is not polite", S + "LiveConnectionIndicator.razor", [(' aria-live="polite"', "")], LIVE),
    ("5 disposal leaves the state handler hooked", S + "LiveConnectionIndicator.razor.cs", [("        LiveClient.StateChanged -= OnStateChanged;\n", "")], LIVE),
    ("6 a start failure is not caught", S + "LiveConnectionIndicator.razor.cs", [("catch (Exception exception)", "catch (ArgumentException exception)")], LIVE),
    ("7 the start moves into the initialization (prerender)", S + "LiveConnectionIndicator.razor.cs", [("        Session.Changed += OnSessionChanged;\n    }", "        Session.Changed += OnSessionChanged;\n        _ = StartAsync();\n    }")], LIVE),
    ("8 the window is two seconds", S + "LiveCopy.cs", [("TimeSpan.FromSeconds(1);\n\n    /// <summary>The composer", "TimeSpan.FromSeconds(2);\n\n    /// <summary>The composer")], LIVE),
    ("9 the agent's own change raises the banner", Q, [("LiveChangeRules.IsOwn(change, Session.Agent?.Id)", "LiveChangeRules.IsOwn(change, null)")], LIVE),
    ("10 every change starts a timer", Q, [(" || _liveTimer is not null", "")], LIVE),
    ("11 a change after the banner starts another timer", Q, [(" || _liveChanged ||", " ||")], LIVE),
    ("12 a reload leaves the banner up", Q, [("        ClearLiveBanner();\n", "")], LIVE),
    ("13 disposal leaves the change handler hooked", Q, [("        LiveClient.TicketChanged -= OnLiveChange;\n", "")], LIVE),
    ("14 disposal leaves the timer running", Q, [("        LiveClient.TicketChanged -= OnLiveChange;\n        ReleaseLiveTimer();\n", "        LiveClient.TicketChanged -= OnLiveChange;\n")], LIVE),
    ("15 the page ignores the kill switch", Q, [("if (_disposed || !LiveClient.IsEnabled)", "if (_disposed)")], LIVE),
    ("16 the banner is not announced", Q, [("        _announcement = LiveCopy.QueueUpdated;\n", "")], LIVE),
    ("17 the page starts the connection", Q, [("        LiveClient.TicketChanged += OnLiveChange;\n    }", "        LiveClient.TicketChanged += OnLiveChange;\n        _ = LiveClient.StartAsync();\n    }")], LIVE),
    ("18 the layout has no indicator", "src/TechStrap.Admin/Components/Layout/MainLayout.razor", [("        <LiveConnectionIndicator />\n", "")], LIVE),
    ("19 a resync is not a reason for the banner", S + "LiveChangeRules.cs", [("!IsResync(change) && ownAgentId", "ownAgentId")], LIVE),
]
```

`git add -A`, then run it in the foreground (about two batches of ten):

```bash
python $T/run_muts.py $T/specs/m2.py $T/res_m2.txt 1 2 3 4 5 6 7 8 9 10
python $T/run_muts.py $T/specs/m2.py $T/res_m2b.txt 11 12 13 14 15 16 17 18 19
```

Every mutation must be KILLED. The real results (the first draft of the burst test let #11 survive: a second change after the banner started another timer that fired and changed nothing; the test now waits for the renderer's queue with `await cut.InvokeAsync(() => { })` and asserts no timer, and #11 is killed; #1 as first written did not compile, `_started` was assigned and never read, and was rewritten):

| # | Mutation | File | Result |
| --- | --- | --- | --- |
| 1 | the client is started on every render | `Features/Live/LiveConnectionIndicator.razor.cs` | KILLED (2 failing) |
| 2 | the client is started before the session is ready | `Features/Live/LiveConnectionIndicator.razor.cs` | KILLED (4 failing) |
| 3 | the indicator ignores the kill switch | `Features/Live/LiveConnectionIndicator.razor.cs` | KILLED (3 failing) |
| 4 | the region is not polite | `Features/Live/LiveConnectionIndicator.razor` | KILLED (5 failing) |
| 5 | disposal leaves the state handler hooked | `Features/Live/LiveConnectionIndicator.razor.cs` | KILLED (1 failing) |
| 6 | a start failure is not caught | `Features/Live/LiveConnectionIndicator.razor.cs` | KILLED (1 failing) |
| 7 | the start moves into the initialization (prerender) | `Features/Live/LiveConnectionIndicator.razor.cs` | KILLED (9 failing) |
| 8 | the window is two seconds | `Features/Live/LiveCopy.cs` | KILLED (7 failing) |
| 9 | the agent's own change raises the banner | `Features/Queue/TicketQueuePage.razor.cs` | KILLED (1 failing) |
| 10 | every change starts a timer | `Features/Queue/TicketQueuePage.razor.cs` | KILLED (1 failing) |
| 11 | a change after the banner starts another timer | `Features/Queue/TicketQueuePage.razor.cs` | KILLED (1 failing) |
| 12 | a reload leaves the banner up | `Features/Queue/TicketQueuePage.razor.cs` | KILLED (3 failing) |
| 13 | disposal leaves the change handler hooked | `Features/Queue/TicketQueuePage.razor.cs` | KILLED (1 failing) |
| 14 | disposal leaves the timer running | `Features/Queue/TicketQueuePage.razor.cs` | KILLED (1 failing) |
| 15 | the page ignores the kill switch | `Features/Queue/TicketQueuePage.razor.cs` | KILLED (1 failing) |
| 16 | the banner is not announced | `Features/Queue/TicketQueuePage.razor.cs` | KILLED (1 failing) |
| 17 | the page starts the connection | `Features/Queue/TicketQueuePage.razor.cs` | KILLED (3 failing) |
| 18 | the layout has no indicator | `Components/Layout/MainLayout.razor` | KILLED (1 failing) |
| 19 | a resync is not a reason for the banner | `Features/Live/LiveChangeRules.cs` | KILLED (2 failing) |

- [ ] **Step 8: Commit**

```bash
git add -A
git diff --cached --stat
git commit -F - <<'MSG'
feat: PHASE-10b connection indicator and queue banner (D-046)

LiveConnectionIndicator in MainLayout is the one place the live client starts (OnAfterRender, session Ready);
the queue shows one "Queue updated - refresh" banner per one-second window for any non-own change or resync,
and reloads only when it is clicked.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
MSG
```

---

### Task 3: The detail page: the "New activity" banner, join and leave, presence and the composing hint

**Review Focus pin:** 3 (the banner never updates the row version or the draft; a send before the click gets the 409; own changes are ignored), 4 (presence excludes self; the hint is throttled and clears on blur, submit and disposal; polite announcements) and 5 (switched off, nothing joins and nothing is drawn; a failing client never breaks the page or the composer).

**Files:**

- Create: `src/TechStrap.Admin/Features/Live/PresenceViewModel.cs`, `TicketPresenceBar.razor`, `ChangedTicketBanner.razor`
- Modify: `src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor`, `TicketDetailPage.razor.cs`, `ReplyComposer.razor`, `ReplyComposer.razor.cs`, `src/TechStrap.Admin/Styles/_live.scss`
- Test (create): `tests/TechStrap.Admin.Tests/Live/PresenceViewModelFactoryTests.cs`, `TicketDetailLiveTests.cs`, `ReplyComposerLiveTests.cs`
- Test (modify): `tests/TechStrap.Admin.Tests/Live/LiveTestData.cs` (the ticket and the agent ids become those of `TestData.Detail()` and `AgentSessions.SignedIn()`)

**Interfaces:**
- Consumes: Task 1's client, `LiveChangeRules`, `FakeTicketLiveClient` (`JoinResult`, `Failure`, `RaiseChange`, `RaisePresence`, `Joined`, `Left`, `Composing`); Task 2's `LiveCopy.NewActivity`, `LiveDefaults.ComposingThrottle`; `TicketLiveLimits.ComposingTtlSeconds` (10), `TicketPresenceStates`; the page's `_model` (`Id`), `RefreshAsync()` and `LoadCoreAsync`, the composer's `Text` setter, `SubmitAsync` and `Dispose`; `DraftStore`.
- Produces:
  - `PresenceViewModel(Guid AgentId, string Name, bool IsReplying, string Text)`, `PresenceCopy` (`Label`, `AnotherAgent`, `Viewing`, `Replying`), `PresenceViewModelFactory.Create(TicketPresenceDto? presence, Guid? selfAgentId, bool composingExpired = false)` (self excluded, repliers first, then viewers, each by name ignoring case, one line per agent with "replying" winning, a blank name becomes "Another agent") and `AnyoneReplying(presence, selfAgentId)`.
  - `TicketPresenceBar` (`[Parameter] IReadOnlyList<PresenceViewModel> Viewers`; a polite `role="status"` region that stays in the page when empty so a newcomer is announced) and `ChangedTicketBanner` (`[Parameter] EventCallback OnRefresh`, `role="status"`).
  - `TicketDetailPage`: joins after each load for the ticket on screen (`SyncLiveGroup`), leaves the old group when the route changes or the ticket is gone and on disposal; `OnLiveChange` raises the banner for a change to this ticket by someone else (or a `Resync`) and touches nothing else; the click is `RefreshAsync`; the banner is cleared by a successful load only when no change arrived while it ran; presence from the join and from `PresenceChanged`, with a lease timer that shows a lapsed "replying" as "viewing".
  - `ReplyComposer`: `NoteTyping()` from the `Text` setter (at most one `SetComposing(true)` per `LiveDefaults.ComposingThrottle`), `StopComposing()` on `@onblur`, submit, empty text, another ticket and disposal; failures are logged by type and swallowed.

- [ ] **Step 1: Check the branch**

```bash
git switch feat/phase-10b-live-admin     # Tasks 1 and 2 are committed; `git status` is clean
```

- [ ] **Step 2: Write the failing tests**

The factory is pure and tested without a renderer. The detail tests are the conflict semantics of D-046: the page loads once, a change by another agent raises the banner and changes nothing else, a send before the click carries the old row version and meets the 409 with the draft kept, the click takes the new version and the draft is still there, and a change that arrives while the reload runs keeps the banner. A test that must know the page has handled an event from the test thread awaits `cut.InvokeAsync(() => { })`. The composer tests write the four-second requirement as a literal.

`tests/TechStrap.Admin.Tests/Live/PresenceViewModelFactoryTests.cs` (new)

```csharp
using TechStrap.Admin.Features.Live;
using TechStrap.Contracts.Live;
using static TechStrap.Admin.Tests.Live.LiveTestData;

namespace TechStrap.Admin.Tests.Live;

/// <summary>The pure part of the presence bar: who is shown, in which order, with which words. The agent never sees themselves.</summary>
public sealed class PresenceViewModelFactoryTests
{
    private static readonly Guid Ada = Guid.Parse("0197f2a0-0000-7000-8000-0000000000a1");
    private static readonly Guid Bo = Guid.Parse("0197f2a0-0000-7000-8000-0000000000a2");
    private static readonly Guid Cy = Guid.Parse("0197f2a0-0000-7000-8000-0000000000a3");

    private static string[] Texts(TicketPresenceDto? presence, Guid? self = null, bool composingExpired = false) =>
        [.. PresenceViewModelFactory.Create(presence, self, composingExpired).Select(v => v.Text)];

    [Fact]
    public void Nobody_else_gives_an_empty_list()
    {
        PresenceViewModelFactory.Create(null, MeId).ShouldBeEmpty();
        PresenceViewModelFactory.Create(Presence(TicketId), MeId).ShouldBeEmpty();
    }

    [Fact]
    public void Viewers_are_shown_as_viewing()
    {
        Texts(Presence(TicketId, Viewer(Ada, "Ada Admin"))).ShouldBe(["Ada Admin is viewing"]);
    }

    [Fact]
    public void A_composing_viewer_is_shown_as_replying()
    {
        var models = PresenceViewModelFactory.Create(Presence(TicketId, Viewer(Ada, "Ada Admin", TicketPresenceStates.Composing)), null);

        models.ShouldHaveSingleItem().Text.ShouldBe("Ada Admin is replying");
        models[0].IsReplying.ShouldBeTrue();
        models[0].AgentId.ShouldBe(Ada);
    }

    [Fact]
    public void Both_kinds_list_the_repliers_first_then_each_group_by_name_ignoring_case()
    {
        var presence = Presence(
            TicketId,
            Viewer(Cy, "cy cole"),
            Viewer(Bo, "Bo Bell", TicketPresenceStates.Composing),
            Viewer(Ada, "Ada Admin"),
            Viewer(Guid.Parse("0197f2a0-0000-7000-8000-0000000000a4"), "Abe Aldrin", TicketPresenceStates.Composing));

        Texts(presence).ShouldBe(["Abe Aldrin is replying", "Bo Bell is replying", "Ada Admin is viewing", "cy cole is viewing"]);
    }

    [Fact]
    public void The_agent_themself_is_never_listed_whatever_their_state()
    {
        var presence = Presence(TicketId, Viewer(MeId, "Sam Ortiz", TicketPresenceStates.Composing), Viewer(Ada, "Ada Admin"));

        Texts(presence, MeId).ShouldBe(["Ada Admin is viewing"]);
        Texts(Presence(TicketId, Viewer(MeId, "Sam Ortiz")), MeId).ShouldBeEmpty();
        Texts(Presence(TicketId, Viewer(MeId, "Sam Ortiz")), null).ShouldBe(["Sam Ortiz is viewing"]);
    }

    [Fact]
    public void A_lapsed_composing_hint_is_shown_as_viewing()
    {
        var presence = Presence(TicketId, Viewer(Ada, "Ada Admin", TicketPresenceStates.Composing));

        Texts(presence, MeId, composingExpired: true).ShouldBe(["Ada Admin is viewing"]);
    }

    [Fact]
    public void An_agent_listed_twice_is_shown_once_and_replying_wins()
    {
        var presence = Presence(TicketId, Viewer(Ada, "Ada Admin"), Viewer(Ada, "Ada Admin", TicketPresenceStates.Composing));

        Texts(presence).ShouldBe(["Ada Admin is replying"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_replaced_by_a_neutral_one(string name)
    {
        Texts(Presence(TicketId, Viewer(Ada, name))).ShouldBe([PresenceCopy.AnotherAgent + " is viewing"]);
    }
}
```

`tests/TechStrap.Admin.Tests/Live/TicketDetailLiveTests.cs` (new)

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Live;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;
using static TechStrap.Admin.Tests.Live.LiveTestData;

namespace TechStrap.Admin.Tests.Live;

/// <summary>
/// The detail page's live behavior (T14, T15). The rule that matters: a change by someone else only raises a banner. The page keeps its model, its row version and the agent's draft until the banner is clicked, so a send
/// before that still meets the existing 409 and an agent never acts on a version they have not seen. Own changes are ignored; the page joins the ticket on load, leaves it on navigation and disposal, and shows who else is here.
/// </summary>
public sealed class TicketDetailLiveTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly CountingTimeProvider _timers;
    private readonly RecordingLoggerProvider _logs = new();

    public TicketDetailLiveTests()
    {
        _timers = new CountingTimeProvider(Time);
        Services.AddSingleton<TimeProvider>(_timers);
        Services.AddLogging(logging => logging.AddProvider(_logs));
        var products = Substitute.For<IProductsClient>();
        var agents = Substitute.For<IAgentsClient>();
        var tags = Substitute.For<ITagsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.Agent("Sam Ortiz", TestData.SamAgentId)]));
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(products);
        Services.AddSingleton(agents);
        Services.AddSingleton(tags);
        Services.AddTicketFeatures();
        Services.AddSingleton(Substitute.For<IRequestersClient>());
        Show(TestData.Detail());
    }

    private void Show(TicketDetailDto detail, string number = "ORB-42") =>
        _tickets.GetAsync(number, Arg.Any<CancellationToken>()).Returns(TestData.Ok(detail));

    private IRenderedComponent<TicketDetailPage> RenderTicket(string number = "ORB-42") => Render<TicketDetailPage>(p => p.Add(c => c.Number, number));

    private int Loads(string number = "ORB-42") => _tickets.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ITicketsClient.GetAsync) && (string)c.GetArguments()[0]! == number);

    private static string Banner(IRenderedComponent<TicketDetailPage> cut) => cut.Find(".ts-live-banner").TextContent.Trim();

    private async Task ChangeAsync(IRenderedComponent<TicketDetailPage> cut, TicketChangedDto change)
    {
        LiveClient.RaiseChange(change);

        // The renderer runs its queued work in order, so once this returns the page has handled the change.
        await cut.InvokeAsync(() => { });
    }

    [Fact]
    public void The_page_joins_its_ticket_after_it_loads_and_never_starts_the_connection()
    {
        var cut = RenderTicket();

        cut.Find(".ts-ticket").ShouldNotBeNull();
        LiveClient.Joined.ShouldBe([TicketId]);
        LiveClient.StartCalls.ShouldBe(0);
    }

    [Fact]
    public async Task A_change_by_another_agent_raises_the_banner_and_changes_nothing_else()
    {
        var cut = RenderTicket();

        await ChangeAsync(cut, Change(actor: ColleagueId));

        Banner(cut).ShouldContain(LiveCopy.NewActivity);
        cut.Find(".ts-live-banner").GetAttribute("role").ShouldBe("status");
        Loads().ShouldBe(1);
        cut.FindAll(".ts-timeline *").Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_change_with_no_actor_and_a_resync_raise_the_banner()
    {
        var cut = RenderTicket();

        await ChangeAsync(cut, Change() with { ActorAgentId = null });
        cut.FindAll(".ts-live-banner").Count.ShouldBe(1);
        cut.Find(".ts-live-banner button").Click();
        cut.WaitForAssertion(() => cut.FindAll(".ts-live-banner").ShouldBeEmpty());

        await ChangeAsync(cut, Resync() with { ActorAgentId = MeId });

        cut.FindAll(".ts-live-banner").Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_change_to_another_ticket_and_the_agents_own_change_raise_nothing()
    {
        var cut = RenderTicket();

        await ChangeAsync(cut, Change(ticketId: OtherTicketId, actor: ColleagueId));
        await ChangeAsync(cut, Change(actor: MeId));

        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
        Loads().ShouldBe(1);
    }

    [Fact]
    public async Task Clicking_the_banner_reloads_the_timeline_and_takes_the_new_row_version_and_clears_the_banner()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent), TestData.State(rowVersion: 12))));
        var cut = RenderTicket();
        await ChangeAsync(cut, Change(actor: ColleagueId));
        Show(TestData.Detail(rowVersion: 11, messages: [TestData.Message(), TestData.Message(MessageAuthorTypes.Requester, bodyHtml: "<p>Still broken</p>")]));

        cut.Find(".ts-live-banner button").Click();

        cut.WaitForAssertion(() => cut.FindAll(".ts-live-banner").ShouldBeEmpty());
        Loads().ShouldBe(2);
        cut.Markup.ShouldContain("Still broken");
        cut.Find("textarea").Input("Now fixed.");
        cut.FindAll(".ts-composer-actions button")[0].Click();
        await _tickets.Received(1).ReplyAsync(TicketId, Arg.Is<AddAgentReplyRequest>(r => r.RowVersion == 11u), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Until_the_click_the_page_keeps_its_row_version_so_a_send_still_gets_the_409_and_the_draft_survives()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict));
        var cut = RenderTicket();
        cut.Find("textarea").Input("A reply I must not lose.");

        await ChangeAsync(cut, Change(actor: ColleagueId));
        Show(TestData.Detail(rowVersion: 99));
        cut.FindAll(".ts-composer-actions button")[0].Click();

        await _tickets.Received(1).ReplyAsync(TicketId, Arg.Is<AddAgentReplyRequest>(r => r.RowVersion == 7u && r.Body == "A reply I must not lose."), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>());
        cut.Find(".ts-conflict").GetAttribute("role").ShouldBe("alert");
        cut.Find("textarea").GetAttribute("value").ShouldBe("A reply I must not lose.");
        cut.FindAll(".ts-live-banner").Count.ShouldBe(1);
        Loads().ShouldBe(1);
    }

    [Fact]
    public async Task The_draft_is_never_touched_by_the_banner_or_by_the_reload_it_offers()
    {
        var cut = RenderTicket();
        cut.Find("textarea").Input("Half a reply");
        var drafts = Services.GetRequiredService<DraftStore>();

        await ChangeAsync(cut, Change(actor: ColleagueId));
        cut.Find("textarea").GetAttribute("value").ShouldBe("Half a reply");
        cut.Find(".ts-live-banner button").Click();
        cut.WaitForAssertion(() => cut.FindAll(".ts-live-banner").ShouldBeEmpty());

        cut.Find("textarea").GetAttribute("value").ShouldBe("Half a reply");
        drafts.Get(TicketId).PublicText.ShouldBe("Half a reply");
    }

    [Fact]
    public async Task A_change_that_arrives_while_the_reload_runs_keeps_the_banner()
    {
        var cut = RenderTicket();
        await ChangeAsync(cut, Change(actor: ColleagueId));
        var release = new TaskCompletionSource<Result<TicketDetailDto>>();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(_ => release.Task);

        cut.Find(".ts-live-banner button").Click();
        await ChangeAsync(cut, Change(actor: ColleagueId));
        release.SetResult(TestData.Ok(TestData.Detail(rowVersion: 8)));

        cut.WaitForAssertion(() => Loads().ShouldBe(2));
        await cut.InvokeAsync(() => { });
        cut.FindAll(".ts-live-banner").Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_reload_keeps_the_banner_and_the_page()
    {
        var cut = RenderTicket();
        await ChangeAsync(cut, Change(actor: ColleagueId));
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("api-unavailable", "Down.", ResultErrorKind.Failure));

        cut.Find(".ts-live-banner button").Click();

        cut.WaitForAssertion(() => Loads().ShouldBe(2));
        cut.FindAll(".ts-live-banner").Count.ShouldBe(1);
        cut.Find(".ts-ticket").ShouldNotBeNull();
    }

    [Fact]
    public void Another_agents_presence_is_shown_and_the_agent_themself_is_not()
    {
        LiveClient.JoinResult = id => Presence(id, Viewer(MeId, "Sam Ortiz"), Viewer(ColleagueId, "Ada Admin"));

        var cut = RenderTicket();

        cut.WaitForAssertion(() => cut.Find(".ts-presence").TextContent.ShouldBe("Ada Admin is viewing"));
        cut.Find(".ts-presence").GetAttribute("aria-live").ShouldBe("polite");
        cut.Find(".ts-presence").GetAttribute("role").ShouldBe("status");
    }

    [Fact]
    public async Task A_presence_update_for_this_ticket_replaces_the_list_and_one_for_another_ticket_is_ignored()
    {
        var cut = RenderTicket();
        cut.Find(".ts-presence").TextContent.Trim().ShouldBeEmpty();

        LiveClient.RaisePresence(Presence(TicketId, Viewer(ColleagueId, "Ada Admin", TicketPresenceStates.Composing)));
        cut.WaitForAssertion(() => cut.Find(".ts-presence").TextContent.ShouldBe("Ada Admin is replying"));
        LiveClient.RaisePresence(Presence(OtherTicketId, Viewer(Guid.NewGuid(), "Someone Else")));
        await cut.InvokeAsync(() => { });
        cut.Find(".ts-presence").TextContent.ShouldBe("Ada Admin is replying");
        LiveClient.RaisePresence(Presence(TicketId));

        cut.WaitForAssertion(() => cut.Find(".ts-presence").TextContent.Trim().ShouldBeEmpty());
    }

    [Fact]
    public void A_replying_hint_that_is_not_refreshed_lapses_after_the_servers_lease_and_a_refresh_extends_it()
    {
        var cut = RenderTicket();
        var replying = Presence(TicketId, Viewer(ColleagueId, "Ada Admin", TicketPresenceStates.Composing));

        LiveClient.RaisePresence(replying);
        cut.WaitForAssertion(() => cut.Find(".ts-presence").TextContent.ShouldBe("Ada Admin is replying"));
        Time.Advance(TimeSpan.FromSeconds(TicketLiveLimits.ComposingTtlSeconds - 1));
        LiveClient.RaisePresence(replying);
        Time.Advance(TimeSpan.FromSeconds(TicketLiveLimits.ComposingTtlSeconds - 1));
        cut.Find(".ts-presence").TextContent.ShouldBe("Ada Admin is replying");
        Time.Advance(TimeSpan.FromSeconds(1));

        cut.WaitForAssertion(() => cut.Find(".ts-presence").TextContent.ShouldBe("Ada Admin is viewing"));
        _timers.LiveTimers.ShouldBe(0);
    }

    [Fact]
    public void Navigating_to_another_ticket_leaves_the_old_group_and_joins_the_new_one()
    {
        var other = Guid.Parse("dddddddd-0000-0000-0000-000000000043");
        Show(TestData.Detail(number: "ORB-43") with { Id = other }, "ORB-43");
        var cut = RenderTicket();

        cut.Render(p => p.Add(c => c.Number, "ORB-43"));

        cut.WaitForAssertion(() => LiveClient.Joined.ShouldBe([TicketId, other]));
        LiveClient.Left.ShouldBe([TicketId]);
    }

    [Fact]
    public async Task A_refresh_of_the_same_ticket_does_not_join_again()
    {
        var cut = RenderTicket();
        LiveClient.Joined.Count.ShouldBe(1);

        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        LiveClient.Joined.ShouldBe([TicketId]);
        LiveClient.Left.ShouldBeEmpty();
    }

    [Fact]
    public void Disposal_leaves_the_group_unsubscribes_and_releases_the_presence_timer()
    {
        var cut = RenderTicket();
        LiveClient.RaisePresence(Presence(TicketId, Viewer(ColleagueId, "Ada Admin", TicketPresenceStates.Composing)));
        cut.WaitForAssertion(() => _timers.LiveTimers.ShouldBe(1));

        cut.Instance.Dispose();

        LiveClient.Left.ShouldBe([TicketId]);
        LiveClient.HasSubscribers.ShouldBeFalse();
        _timers.LiveTimers.ShouldBe(0);
        Should.NotThrow(() => LiveClient.RaiseChange(Change(actor: ColleagueId)));
        Should.NotThrow(() => LiveClient.RaisePresence(Presence(TicketId)));
    }

    [Fact]
    public async Task Switched_off_the_page_joins_nothing_and_draws_nothing_live()
    {
        LiveClient.IsEnabled = false;
        var cut = RenderTicket();

        await ChangeAsync(cut, Change(actor: ColleagueId));

        cut.FindAll(".ts-live-banner").ShouldBeEmpty();
        cut.FindAll(".ts-presence").ShouldBeEmpty();
        LiveClient.Joined.ShouldBeEmpty();
        LiveClient.Left.ShouldBeEmpty();
    }

    [Fact]
    public void A_failing_client_leaves_the_page_working_and_logs_only_the_type()
    {
        LiveClient.Failure = new InvalidOperationException("hub down");

        var cut = RenderTicket();

        cut.Find(".ts-ticket").ShouldNotBeNull();
        _logs.Lines.ShouldContain(line => line.Contains("Joining the live group", StringComparison.Ordinal) && line.Contains("InvalidOperationException", StringComparison.Ordinal));

        cut.Instance.Dispose();

        _logs.Lines.ShouldContain(line => line.Contains("Leaving the live group", StringComparison.Ordinal) && line.Contains("InvalidOperationException", StringComparison.Ordinal));
        _logs.Lines.ShouldNotContain(line => line.Contains("hub down", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_ticket_that_is_gone_leaves_its_group()
    {
        var cut = RenderTicket();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("not-found", "Gone.", ResultErrorKind.NotFound));

        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        cut.WaitForAssertion(() => LiveClient.Left.ShouldBe([TicketId]));
    }
}
```

`tests/TechStrap.Admin.Tests/Live/ReplyComposerLiveTests.cs` (new)

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Tickets;
using static TechStrap.Admin.Tests.Live.LiveTestData;

namespace TechStrap.Admin.Tests.Live;

/// <summary>
/// The composer's "I am replying" hint (T15): sent when the agent types, at most once per four seconds so the server's ten-second lease stays alive, and cleared on blur, on submit, when the text is emptied, when the ticket changes and
/// when the composer goes. A failing hub never breaks the composer.
/// </summary>
public sealed class ReplyComposerLiveTests : AdminComponentTest
{
    // The requirement is four seconds, written as a literal on purpose: a change of the constant must fail these tests.
    private static readonly TimeSpan FourSeconds = TimeSpan.FromSeconds(4);

    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly RecordingLoggerProvider _logs = new();

    public ReplyComposerLiveTests()
    {
        Services.AddLogging(logging => logging.AddProvider(_logs));
        Services.AddSingleton(_tickets);
        Services.AddScoped<DraftStore>();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(_ => TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent), TestData.State(rowVersion: 8))));
    }

    private IRenderedComponent<ReplyComposer> RenderComposer(Guid? ticketId = null) =>
        Render<ReplyComposer>(p => p
            .Add(c => c.TicketId, ticketId ?? TicketId)
            .Add(c => c.TicketNumber, "ORB-42")
            .Add(c => c.RequesterEmail, "ada@example.com")
            .Add(c => c.RowVersion, 7u)
            .Add(c => c.ProductId, TestData.OrbitlyId));

    [Fact]
    public void Typing_sends_one_composing_hint_and_more_typing_inside_the_window_sends_none()
    {
        var cut = RenderComposer();

        cut.Find("textarea").Input("H");
        cut.Find("textarea").Input("He");
        Time.Advance(FourSeconds - TimeSpan.FromMilliseconds(1));
        cut.Find("textarea").Input("Hel");

        LiveClient.Composing.ShouldBe([(TicketId, true)]);
    }

    [Fact]
    public void Typing_after_the_window_sends_the_hint_again_so_the_lease_stays_alive()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("H");

        Time.Advance(FourSeconds);
        cut.Find("textarea").Input("He");
        Time.Advance(FourSeconds - TimeSpan.FromMilliseconds(1));
        cut.Find("textarea").Input("Hel");
        Time.Advance(TimeSpan.FromMilliseconds(1));
        cut.Find("textarea").Input("Hell");

        LiveClient.Composing.ShouldBe([(TicketId, true), (TicketId, true), (TicketId, true)]);
    }

    [Fact]
    public void Blur_clears_the_hint_once_and_typing_again_sends_a_new_one_at_once()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("Hello");

        cut.Find("textarea").Blur();
        cut.Find("textarea").Blur();
        cut.Find("textarea").Input("Hello again");

        LiveClient.Composing.ShouldBe([(TicketId, true), (TicketId, false), (TicketId, true)]);
    }

    [Fact]
    public void Emptying_the_text_clears_the_hint()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("Hello");

        cut.Find("textarea").Input(string.Empty);

        LiveClient.Composing.ShouldBe([(TicketId, true), (TicketId, false)]);
    }

    [Fact]
    public void Nothing_is_sent_when_the_agent_has_not_typed()
    {
        var cut = RenderComposer();

        cut.Find("textarea").Blur();
        cut.Instance.Dispose();

        LiveClient.Composing.ShouldBeEmpty();
    }

    [Fact]
    public void Sending_clears_the_hint()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("A reply");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.WaitForAssertion(() => LiveClient.Composing.ShouldBe([(TicketId, true), (TicketId, false)]));
    }

    [Fact]
    public void Disposal_clears_a_hint_that_is_on()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("Hello");

        cut.Instance.Dispose();

        LiveClient.Composing.ShouldBe([(TicketId, true), (TicketId, false)]);
    }

    [Fact]
    public void A_composer_that_moves_to_another_ticket_clears_the_hint_for_the_old_one()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("Hello");
        var other = Guid.Parse("dddddddd-0000-0000-0000-000000000043");

        cut.Render(p => p.Add(c => c.TicketId, other));

        LiveClient.Composing.ShouldBe([(TicketId, true), (TicketId, false)]);
    }

    [Fact]
    public void Switched_off_the_composer_sends_nothing()
    {
        LiveClient.IsEnabled = false;
        var cut = RenderComposer();

        cut.Find("textarea").Input("Hello");
        cut.Find("textarea").Blur();

        LiveClient.Composing.ShouldBeEmpty();
    }

    [Fact]
    public void A_failing_client_never_breaks_the_composer_and_only_the_type_is_logged()
    {
        LiveClient.Failure = new InvalidOperationException("hub down");
        var cut = RenderComposer();

        cut.Find("textarea").Input("Hello");
        cut.Find("textarea").Blur();
        cut.Instance.Dispose();

        cut.Find("textarea").GetAttribute("value").ShouldBe("Hello");
        _logs.Lines.ShouldContain(line => line.Contains("InvalidOperationException", StringComparison.Ordinal));
        _logs.Lines.ShouldNotContain(line => line.Contains("hub down", StringComparison.Ordinal));
    }
}
```

`tests/TechStrap.Admin.Tests/Live/LiveTestData.cs` (modify)

```diff
diff --git a/tests/TechStrap.Admin.Tests/Live/LiveTestData.cs b/tests/TechStrap.Admin.Tests/Live/LiveTestData.cs
--- a/tests/TechStrap.Admin.Tests/Live/LiveTestData.cs
+++ b/tests/TechStrap.Admin.Tests/Live/LiveTestData.cs
@@ -1,3 +1,4 @@
+using TechStrap.Admin.Tests.Support;
 using TechStrap.Contracts.Live;
 using TechStrap.Contracts.Tickets;
 
@@ -5,9 +6,14 @@ namespace TechStrap.Admin.Tests.Live;
 
 internal static class LiveTestData
 {
-    public static readonly Guid TicketId = Guid.Parse("0197f2a0-0000-7000-8000-000000000001");
+    /// <summary>The ticket of <c>TestData.Detail()</c>, so a change for it concerns the page the component tests open.</summary>
+    public static readonly Guid TicketId = TestData.TicketId;
+
     public static readonly Guid OtherTicketId = Guid.Parse("0197f2a0-0000-7000-8000-000000000009");
-    public static readonly Guid MeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
+
+    /// <summary>The agent of <c>AgentSessions.SignedIn()</c>.</summary>
+    public static readonly Guid MeId = AgentSessions.SamId;
+
     public static readonly Guid ColleagueId = Guid.Parse("0197f2a0-0000-7000-8000-0000000000aa");
     public static readonly DateTimeOffset At = new(2026, 10, 7, 9, 30, 0, TimeSpan.Zero);
```



- [ ] **Step 3: Run the build to see it fail (RED)**

Run: `dotnet build tests/TechStrap.Admin.Tests -c Release`

Expected: FAIL to compile (the real run): `8 CS0103: The name 'PresenceViewModelFactory' does not exist in the current context` and `2 CS0103: The name 'PresenceCopy' does not exist in the current context`.

- [ ] **Step 4: Write the presence types and the two small components**

The factory is a static class with one method; the bar and the banner are tiny `.razor` files with `@code` parameters (the spec's inline ceiling: simple parameters and one forwarder); the styles add `.ts-presence`.

`src/TechStrap.Admin/Features/Live/PresenceViewModel.cs` (new)

```csharp
using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Features.Live;

/// <summary>One line of the presence bar: who, whether they are typing a reply, and the words to show. A feature-local view model: the Contracts DTO is never drawn directly.</summary>
public sealed record PresenceViewModel(Guid AgentId, string Name, bool IsReplying, string Text);

/// <summary>The words of the presence bar.</summary>
public static class PresenceCopy
{
    public const string Label = "Who else is on this ticket";

    /// <summary>Used when a viewer has no name at all (the server falls back to the email, so this is a last resort).</summary>
    public const string AnotherAgent = "Another agent";

    public const string Viewing = "is viewing";
    public const string Replying = "is replying";
}

/// <summary>
/// Builds the presence bar's lines from what the hub reports (D-046). The hub reports every agent on the ticket, the signed-in one included, so the agent is removed by id here: nobody sees themselves.
/// Repliers come first, then viewers, each group by name. An agent listed twice is one line and "replying" wins. A "replying" hint the hub has not refreshed within its lease is shown as "viewing" when
/// <paramref name="composingExpired"/> says so (the hub does not push the lapse).
/// </summary>
public static class PresenceViewModelFactory
{
    public static IReadOnlyList<PresenceViewModel> Create(TicketPresenceDto? presence, Guid? selfAgentId, bool composingExpired = false)
    {
        if (presence is null)
        {
            return [];
        }

        return
        [
            .. presence.Viewers
                .Where(viewer => selfAgentId is not { } self || viewer.AgentId != self)
                .GroupBy(viewer => viewer.AgentId)
                .Select(group => group.OrderByDescending(viewer => IsComposing(viewer)).First())
                .Select(viewer =>
                {
                    var name = string.IsNullOrWhiteSpace(viewer.DisplayName) ? PresenceCopy.AnotherAgent : viewer.DisplayName.Trim();
                    var replying = IsComposing(viewer) && !composingExpired;
                    return new PresenceViewModel(viewer.AgentId, name, replying, $"{name} {(replying ? PresenceCopy.Replying : PresenceCopy.Viewing)}");
                })
                .OrderByDescending(model => model.IsReplying)
                .ThenBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(model => model.AgentId),
        ];
    }

    /// <summary>True when any viewer other than <paramref name="selfAgentId"/> is replying, so the page knows whether a lease timer is needed.</summary>
    public static bool AnyoneReplying(TicketPresenceDto? presence, Guid? selfAgentId) =>
        presence?.Viewers.Any(viewer => IsComposing(viewer) && (selfAgentId is not { } self || viewer.AgentId != self)) == true;

    private static bool IsComposing(TicketViewerDto viewer) => viewer.State == TicketPresenceStates.Composing;
}
```

`src/TechStrap.Admin/Features/Live/TicketPresenceBar.razor` (new)

```razor
<div class="ts-presence @(Viewers.Count == 0 ? "ts-presence--empty" : null)" role="status" aria-live="polite" aria-label="@PresenceCopy.Label">
    @foreach (var viewer in Viewers)
    {
        <span @key="viewer.AgentId" class="ts-presence__item @(viewer.IsReplying ? "ts-presence__item--replying" : null)">@viewer.Text</span>
    }
</div>

@code {
    /// <summary>The other agents on this ticket (the agent themself already removed by <see cref="PresenceViewModelFactory"/>).</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<PresenceViewModel> Viewers { get; set; } = [];
}
```

`src/TechStrap.Admin/Features/Live/ChangedTicketBanner.razor` (new)

```razor
<div class="ts-live-banner" role="status">
    <button type="button" class="btn btn-outline-secondary" @onclick="OnRefresh">@LiveCopy.NewActivity</button>
</div>

@code {
    /// <summary>Raised on click: the page reloads the timeline and the status and takes the new row version. Until then nothing on the page has changed.</summary>
    [Parameter, EditorRequired]
    public EventCallback OnRefresh { get; set; }
}
```

`src/TechStrap.Admin/Styles/_live.scss` (modify)

```diff
diff --git a/src/TechStrap.Admin/Styles/_live.scss b/src/TechStrap.Admin/Styles/_live.scss
--- a/src/TechStrap.Admin/Styles/_live.scss
+++ b/src/TechStrap.Admin/Styles/_live.scss
@@ -40,3 +40,21 @@
     margin: 0;
   }
 }
+
+// The presence bar sits above the timeline; an empty one takes no room but stays in the page so a newcomer is announced.
+.ts-presence {
+  display: flex;
+  flex-wrap: wrap;
+  gap: 4px 16px;
+  margin: 0 0 8px;
+  font: 500 .75rem var(--ts-font-mono);
+  color: var(--ink-2);
+}
+
+.ts-presence--empty {
+  margin: 0;
+}
+
+.ts-presence__item--replying {
+  color: var(--ink);
+}
```



- [ ] **Step 5: Write the detail page's live behavior and the composer's hint**

Read these as the conflict semantics. `OnLiveChange` only sets `_liveChanged` and counts the change (`_liveChanges`); it never touches `_model`, the row version or the draft. `LoadCoreAsync` clears the banner only when `changesSeen == _liveChanges`. `SyncLiveGroup` runs after every load, so a route change leaves one group and joins the other, and a ticket that is gone is left. In the composer, `NoteTyping` compares `TimeProvider.GetUtcNow()` with the last send; `StopComposing` sends the one `false`; `Dispose` calls it first.

`src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor` (modify)

```diff
diff --git a/src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor b/src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor
--- a/src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor
+++ b/src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor
@@ -18,11 +18,19 @@ else
 {
     <article class="ts-ticket">
         <TicketHeader Ticket="_model" />
+        @if (LiveClient.IsEnabled)
+        {
+            <TicketPresenceBar Viewers="_viewers" />
+        }
         @if (!string.IsNullOrEmpty(_error))
         {
             <ErrorState Message="@_error" OnRetry="RefreshAsync" />
         }
         <TicketActions Ticket="_model" OnState="OnStateChangedAsync" OnConflict="OnConflictAsync" OnGone="RefreshAsync" OnReload="ReloadAfterConflictAsync" />
+        @if (_liveChanged)
+        {
+            <ChangedTicketBanner OnRefresh="ApplyLiveRefreshAsync" />
+        }
         <ConflictBanner State="_conflict" LatestChange="@_latestChange" Reloading="_reloading" OnReload="ReloadAfterConflictAsync" OnDismiss="DismissConflict" />
         <div class="ts-ticket-layout">
             <section class="ts-conversation" aria-label="@TicketCopy.ConversationLabel">
```

`src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor.cs` (modify)

```diff
diff --git a/src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor.cs b/src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor.cs
--- a/src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor.cs
+++ b/src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor.cs
@@ -1,8 +1,11 @@
 using Microsoft.AspNetCore.Components;
+using Microsoft.Extensions.Logging;
 using SyntaxCircus.Common;
 using TechStrap.Admin.Auth;
 using TechStrap.Admin.Clients;
+using TechStrap.Admin.Features.Live;
 using TechStrap.Admin.Features.Shell;
+using TechStrap.Contracts.Live;
 using TechStrap.Contracts.Tickets;
 
 namespace TechStrap.Admin.Features.Tickets;
@@ -28,6 +31,13 @@ public sealed partial class TicketDetailPage : IDisposable
     private bool _reloading;
     private string? _latestChange;
     private IDisposable? _palette;
+    private bool _liveChanged;
+    private int _liveChanges;
+    private Guid? _joinedTicket;
+    private TicketPresenceDto? _presence;
+    private IReadOnlyList<PresenceViewModel> _viewers = [];
+    private bool _composingExpired;
+    private ITimer? _composingTimer;
 
     [Inject]
     private TicketDetailPresenter Presenter { get; set; } = default!;
@@ -44,13 +54,27 @@ public sealed partial class TicketDetailPage : IDisposable
     [Inject]
     private AgentSession Session { get; set; } = default!;
 
+    [Inject]
+    private ITicketLiveClient LiveClient { get; set; } = default!;
+
+    [Inject]
+    private TimeProvider Time { get; set; } = default!;
+
+    [Inject]
+    private ILogger<TicketDetailPage> Logger { get; set; } = default!;
+
     /// <summary>The ticket number from the route, for example <c>ORB-42</c>.</summary>
     [Parameter]
     public string Number { get; set; } = string.Empty;
 
     private sealed record GoneMessage(string Heading, string Body);
 
-    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;
+    protected override void OnInitialized()
+    {
+        Shortcuts.Pressed += OnShortcutAsync;
+        LiveClient.TicketChanged += OnLiveChange;
+        LiveClient.PresenceChanged += OnPresenceChanged;
+    }
 
     protected override async Task OnParametersSetAsync()
     {
@@ -102,6 +126,9 @@ public sealed partial class TicketDetailPage : IDisposable
         _load?.Dispose();
         var cts = _load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
 
+        // A change that arrives while this load runs is newer than what it fetches: the banner is cleared only when nothing arrived meanwhile.
+        var changesSeen = _liveChanges;
+
         Result<TicketDetailViewModel> result;
         try
         {
@@ -123,6 +150,12 @@ public sealed partial class TicketDetailPage : IDisposable
             _model = result.Value;
             SyncPalette();
             _error = string.Empty;
+            if (changesSeen == _liveChanges)
+            {
+                _liveChanged = false;
+            }
+
+            SyncLiveGroup();
             return true;
         }
 
@@ -131,6 +164,7 @@ public sealed partial class TicketDetailPage : IDisposable
         {
             _model = null;
             SyncPalette();
+            SyncLiveGroup();
             _gone = silent
                 ? new GoneMessage(TicketCopy.GoneHeading, TicketCopy.GoneBody)
                 : new GoneMessage(TicketCopy.NotFoundHeading, TicketCopy.NotFoundBody);
@@ -142,6 +176,143 @@ public sealed partial class TicketDetailPage : IDisposable
         return true;
     }
 
+    /// <summary>
+    /// Another agent, the customer or the Worker changed a ticket (D-046: every change reaches every agent, so the page filters). A change to THIS ticket by someone else, or a resync, only raises the banner:
+    /// <c>_model</c>, its row version and the composer's draft stay exactly as they are until the agent clicks it, so a send in between still gets the 409 and nobody acts on a version they have not seen.
+    /// The agent's own changes are ignored (their own write already refreshed the page). It runs on a pool thread, so it hops to the renderer first.
+    /// </summary>
+    private void OnLiveChange(TicketChangedDto change)
+    {
+        if (_disposed || !LiveClient.IsEnabled)
+        {
+            return;
+        }
+
+        _ = InvokeAsync(() =>
+        {
+            if (_disposed || _model is null || !LiveChangeRules.Concerns(change, _model.Id) || LiveChangeRules.IsOwn(change, Session.Agent?.Id))
+            {
+                return;
+            }
+
+            _liveChanges++;
+            _liveChanged = true;
+            StateHasChanged();
+        });
+    }
+
+    /// <summary>The click: the ordinary silent refresh, which replaces the timeline, the status and the row version. The draft lives in <c>DraftStore</c> and is not part of it.</summary>
+    private Task ApplyLiveRefreshAsync() => RefreshAsync();
+
+    private void OnPresenceChanged(TicketPresenceDto presence)
+    {
+        if (_disposed || !LiveClient.IsEnabled)
+        {
+            return;
+        }
+
+        _ = InvokeAsync(() => ApplyPresence(presence));
+    }
+
+    private void ApplyPresence(TicketPresenceDto? presence)
+    {
+        if (_disposed || presence is null || presence.TicketId != _joinedTicket)
+        {
+            return;
+        }
+
+        _presence = presence;
+        _composingExpired = false;
+        RebuildViewers();
+        ArmComposingTimer();
+        StateHasChanged();
+    }
+
+    private void RebuildViewers() => _viewers = PresenceViewModelFactory.Create(_presence, Session.Agent?.Id, _composingExpired);
+
+    /// <summary>
+    /// The hub does not tell us when a "replying" hint lapses (its lease is 10 seconds and the composer refreshes it every 4): after the lease with no news, the hint is shown as "viewing". Every presence message restarts it.
+    /// </summary>
+    private void ArmComposingTimer()
+    {
+        _composingTimer?.Dispose();
+        _composingTimer = null;
+        if (PresenceViewModelFactory.AnyoneReplying(_presence, Session.Agent?.Id))
+        {
+            _composingTimer = Time.CreateTimer(_ => _ = InvokeAsync(ExpireComposing), null, TimeSpan.FromSeconds(TicketLiveLimits.ComposingTtlSeconds), Timeout.InfiniteTimeSpan);
+        }
+    }
+
+    private void ExpireComposing()
+    {
+        _composingTimer?.Dispose();
+        _composingTimer = null;
+        if (_disposed)
+        {
+            return;
+        }
+
+        _composingExpired = true;
+        RebuildViewers();
+        StateHasChanged();
+    }
+
+    /// <summary>
+    /// Keeps the hub group in step with the ticket on screen: joins after a load (the model has the id the hub needs), leaves the old one when the route moves to another ticket or the ticket is gone.
+    /// A prerender instance records the join on a client that is never started, which does nothing.
+    /// </summary>
+    private void SyncLiveGroup()
+    {
+        var wanted = LiveClient.IsEnabled ? _model?.Id : null;
+        if (wanted == _joinedTicket)
+        {
+            return;
+        }
+
+        var previous = _joinedTicket;
+        _joinedTicket = wanted;
+        _presence = null;
+        _viewers = [];
+        _composingExpired = false;
+        _composingTimer?.Dispose();
+        _composingTimer = null;
+        _liveChanged = false;
+        if (previous is { } old)
+        {
+            _ = LeaveGroupAsync(old);
+        }
+
+        if (wanted is { } joined)
+        {
+            _ = JoinGroupAsync(joined);
+        }
+    }
+
+    private async Task JoinGroupAsync(Guid ticketId)
+    {
+        try
+        {
+            var presence = await LiveClient.JoinTicketAsync(ticketId);
+            await InvokeAsync(() => ApplyPresence(presence));
+        }
+        catch (Exception exception)
+        {
+            Logger.LogWarning("Joining the live group of a ticket failed ({ExceptionType}).", exception.GetType().Name);
+        }
+    }
+
+    private async Task LeaveGroupAsync(Guid ticketId)
+    {
+        try
+        {
+            await LiveClient.LeaveTicketAsync(ticketId);
+        }
+        catch (Exception exception)
+        {
+            Logger.LogWarning("Leaving the live group of a ticket failed ({ExceptionType}).", exception.GetType().Name);
+        }
+    }
+
     /// <summary>Replaces the status fields and RowVersion from a write's response. Used by the composer, the sidebar and the actions.</summary>
     internal void ApplyState(TicketStateDto state)
     {
@@ -245,6 +416,15 @@ public sealed partial class TicketDetailPage : IDisposable
         _disposed = true;
         _palette?.Dispose();
         Shortcuts.Pressed -= OnShortcutAsync;
+        LiveClient.TicketChanged -= OnLiveChange;
+        LiveClient.PresenceChanged -= OnPresenceChanged;
+        _composingTimer?.Dispose();
+        if (_joinedTicket is { } joined)
+        {
+            _joinedTicket = null;
+            _ = LeaveGroupAsync(joined);
+        }
+
         _lifetime.Cancel();
         _load?.Dispose();
         _lifetime.Dispose();
```

`src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor` (modify)

```diff
diff --git a/src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor b/src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor
--- a/src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor
+++ b/src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor
@@ -24,7 +24,7 @@
     <label for="@TextId" class="visually-hidden">@(IsPublic ? ReplyComposerCopy.PublicTab : ReplyComposerCopy.NoteTab)</label>
     <textarea id="@TextId" @ref="_text" class="form-control" rows="6" disabled="@Sending"
               placeholder="@(IsPublic ? ReplyComposerCopy.ReplyPlaceholder : ReplyComposerCopy.NotePlaceholder)"
-              @bind="Text" @bind:event="oninput"></textarea>
+              @bind="Text" @bind:event="oninput" @onblur="OnBlur"></textarea>
 
     @if (IsPublic)
     {
```

`src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor.cs` (modify)

```diff
diff --git a/src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor.cs b/src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor.cs
--- a/src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor.cs
+++ b/src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor.cs
@@ -5,6 +5,7 @@ using SyntaxCircus.Common;
 using TechStrap.Admin.Clients;
 using TechStrap.Admin.Components.Ui;
 using TechStrap.Admin.Features.Kb;
+using TechStrap.Admin.Features.Live;
 using TechStrap.Admin.Features.Shell;
 using TechStrap.Contracts.Intake;
 using TechStrap.Contracts.Tickets;
@@ -29,6 +30,9 @@ public sealed partial class ReplyComposer : IDisposable
     private bool _disposed;
     private bool _filesDropped;
     private bool _pickerOpen;
+    private bool _composing;
+    private Guid _composingTicket;
+    private DateTimeOffset _composingSentAt;
 
     [Inject]
     private ITicketsClient Tickets { get; set; } = default!;
@@ -45,6 +49,12 @@ public sealed partial class ReplyComposer : IDisposable
     [Inject]
     private ShortcutService Shortcuts { get; set; } = default!;
 
+    [Inject]
+    private ITicketLiveClient LiveClient { get; set; } = default!;
+
+    [Inject]
+    private TimeProvider Time { get; set; } = default!;
+
     [Parameter, EditorRequired]
     public Guid TicketId { get; set; }
 
@@ -116,6 +126,15 @@ public sealed partial class ReplyComposer : IDisposable
             {
                 _draft.NoteText = value;
             }
+
+            if (string.IsNullOrEmpty(value))
+            {
+                StopComposing();
+            }
+            else
+            {
+                NoteTyping();
+            }
         }
     }
 
@@ -129,6 +148,12 @@ public sealed partial class ReplyComposer : IDisposable
 
     protected override void OnParametersSet()
     {
+        // The hint belongs to the ticket it was sent for: moving to another ticket clears it first.
+        if (_draftTicket != TicketId)
+        {
+            StopComposing();
+        }
+
         // A ticket that moves to another product can no longer link that product's own articles (the API refuses them): those chips go and the shared ones stay.
         if (_draftTicket == TicketId && _draftProduct != Guid.Empty && _draftProduct != ProductId)
         {
@@ -219,6 +244,8 @@ public sealed partial class ReplyComposer : IDisposable
             return;
         }
 
+        StopComposing();
+
         var mode = _draft.Mode;
         var text = mode == ComposerMode.PublicReply ? _draft.PublicText : _draft.NoteText;
         if (string.IsNullOrWhiteSpace(text))
@@ -416,9 +443,59 @@ public sealed partial class ReplyComposer : IDisposable
         await _text.FocusAsync();
     }
 
+    /// <summary>
+    /// The agent is typing: tell the hub, at most once per <see cref="LiveDefaults.ComposingThrottle"/> (the server's lease is 10 seconds, so a refresh every 4 keeps "replying" alive for the others).
+    /// Never awaited and never throws: a hub that is down must not touch the composer.
+    /// </summary>
+    private void NoteTyping()
+    {
+        if (!LiveClient.IsEnabled)
+        {
+            return;
+        }
+
+        var now = Time.GetUtcNow();
+        if (_composing && now - _composingSentAt < LiveDefaults.ComposingThrottle)
+        {
+            return;
+        }
+
+        _composing = true;
+        _composingSentAt = now;
+        _composingTicket = TicketId;
+        _ = SendComposingAsync(_composingTicket, true);
+    }
+
+    /// <summary>Blur, submit, an emptied text box, another ticket and disposal all end the hint, once.</summary>
+    private void StopComposing()
+    {
+        if (!_composing)
+        {
+            return;
+        }
+
+        _composing = false;
+        _ = SendComposingAsync(_composingTicket, false);
+    }
+
+    private void OnBlur() => StopComposing();
+
+    private async Task SendComposingAsync(Guid ticketId, bool isComposing)
+    {
+        try
+        {
+            await LiveClient.SetComposingAsync(ticketId, isComposing);
+        }
+        catch (Exception ex)
+        {
+            Logger.LogWarning("The composing hint could not be sent ({ExceptionType}).", ex.GetType().Name);
+        }
+    }
+
     public void Dispose()
     {
         _disposed = true;
+        StopComposing();
         Shortcuts.Pressed -= OnShortcutAsync;
         _draft.Changed -= OnDraftChanged;
```



- [ ] **Step 6: Run the tests (GREEN)**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter-namespace TechStrap.Admin.Tests.Live`

Expected: PASS, `total: 99, failed: 0`.

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release` (Expected: `total: 2061, failed: 0`: every existing ticket and composer test still passes with the fake client registered) and `dotnet test --project tests/TechStrap.Architecture.Tests -c Release` (Expected: `total: 295, failed: 0`).

- [ ] **Step 7: Record the mutations**

`$T\specs\m3.py` (mutation 4 of the draft, removing `_model is null ||`, does not compile under nullable analysis and was dropped):

```python
LIVE = ["dotnet", "test", "--project", "tests/TechStrap.Admin.Tests", "-c", "Release", "--filter-namespace", "TechStrap.Admin.Tests.Live"]
D = "src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor.cs"
C = "src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor.cs"
F = "src/TechStrap.Admin/Features/Live/PresenceViewModel.cs"

MUTATIONS = [
    ("1 a live change reloads the page by itself", D, [("            _liveChanged = true;\n            StateHasChanged();", "            _liveChanged = true;\n            _ = RefreshAsync();\n            StateHasChanged();")], LIVE),
    ("2 the agent's own change raises the banner", D, [(" || LiveChangeRules.IsOwn(change, Session.Agent?.Id)", " || LiveChangeRules.IsOwn(change, null)")], LIVE),
    ("3 a change to another ticket raises the banner", D, [("!LiveChangeRules.Concerns(change, _model.Id) || ", "")], LIVE),
    ("5 a successful reload leaves the banner up", D, [("if (changesSeen == _liveChanges)", "if (changesSeen != _liveChanges)")], LIVE),
    ("6 a reload clears a newer change too", D, [("if (changesSeen == _liveChanges)", "if (changesSeen == _liveChanges || true)")], LIVE),
    ("7 the page never joins", D, [("            _ = JoinGroupAsync(joined);", "            _ = Task.CompletedTask;")], LIVE),
    ("8 navigation does not leave the old group", D, [("            _ = LeaveGroupAsync(old);", "            _ = Task.CompletedTask;")], LIVE),
    ("9 disposal does not leave the group", D, [("            _ = LeaveGroupAsync(joined);", "            _ = Task.CompletedTask;")], LIVE),
    ("10 disposal leaves the presence handler hooked", D, [("        LiveClient.PresenceChanged -= OnPresenceChanged;\n", "")], LIVE),
    ("11 disposal leaves the lease timer running", D, [("        _composingTimer?.Dispose();\n        if (_joinedTicket is { } joined)\n", "        if (_joinedTicket is { } joined)\n")], LIVE),
    ("12 presence of another ticket is shown", D, [(" || presence.TicketId != _joinedTicket", "")], LIVE),
    ("13 the lease is a minute", D, [("TimeSpan.FromSeconds(TicketLiveLimits.ComposingTtlSeconds)", "TimeSpan.FromSeconds(60)")], LIVE),
    ("14 the agent sees themself", F, [(".Where(viewer => selfAgentId is not { } self || viewer.AgentId != self)", ".Where(viewer => true)")], LIVE),
    ("15 viewers come before repliers", F, [(".OrderByDescending(model => model.IsReplying)", ".OrderBy(model => model.IsReplying)")], LIVE),
    ("16 a lapsed hint stays replying", F, [("var replying = IsComposing(viewer) && !composingExpired;", "var replying = IsComposing(viewer);")], LIVE),
    ("17 viewing wins over replying", F, [(".OrderByDescending(viewer => IsComposing(viewer)).First()", ".OrderBy(viewer => IsComposing(viewer)).First()")], LIVE),
    ("18 the presence bar is not polite", "src/TechStrap.Admin/Features/Live/TicketPresenceBar.razor", [(' aria-live="polite"', "")], LIVE),
    ("19 the throttle is five seconds", "src/TechStrap.Admin/Features/Live/LiveCopy.cs", [("ComposingThrottle = TimeSpan.FromSeconds(4);", "ComposingThrottle = TimeSpan.FromSeconds(5);")], LIVE),
    ("20 the throttle survives a blur", C, [("if (_composing && now - _composingSentAt", "if (now - _composingSentAt")], LIVE),
    ("21 blur does not clear the hint", C, [("private void OnBlur() => StopComposing();", "private void OnBlur() { }")], LIVE),
    ("22 submit does not clear the hint", C, [("        StopComposing();\n\n        var mode = _draft.Mode;", "        var mode = _draft.Mode;")], LIVE),
    ("23 disposal does not clear the hint", C, [("        _disposed = true;\n        StopComposing();\n", "        _disposed = true;\n")], LIVE),
    ("24 an emptied box does not clear the hint", C, [("if (string.IsNullOrEmpty(value))", "if (value.Length < 0)")], LIVE),
    ("25 another ticket does not clear the hint", C, [("        if (_draftTicket != TicketId)\n        {\n            StopComposing();", "        if (_draftTicket == TicketId)\n        {\n            StopComposing();")], LIVE),
    ("26 a failing composing call is not caught", C, [("        catch (Exception ex)\n        {\n            Logger.LogWarning(\"The composing hint", "        catch (ArgumentException ex)\n        {\n            Logger.LogWarning(\"The composing hint")], LIVE),
    ("27 a failing join is not caught", D, [("        catch (Exception exception)\n        {\n            Logger.LogWarning(\"Joining the live group", "        catch (ArgumentException exception)\n        {\n            Logger.LogWarning(\"Joining the live group")], LIVE),
    ("28 the page ignores the kill switch when joining", D, [("var wanted = LiveClient.IsEnabled ? _model?.Id : null;", "var wanted = _model?.Id;")], LIVE),
    ("29 a failing leave is not caught", D, [("        catch (Exception exception)\n        {\n            Logger.LogWarning(\"Leaving the live group", "        catch (ArgumentException exception)\n        {\n            Logger.LogWarning(\"Leaving the live group")], LIVE),
]
```

`git add -A`, then in the foreground, two batches (each takes about five minutes):

```bash
python $T/run_muts.py $T/specs/m3.py $T/res_m3.txt 1 2 3 5 6 7 8 9 10 11 12 13
python $T/run_muts.py $T/specs/m3.py $T/res_m3b.txt 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29
```

Every mutation must be KILLED. The first draft of the tests let two survive, and the real results below are after the fix: #12 (presence of another ticket was applied and then replaced by the next update; the test now checks the text between the two) and #27 (a failing join was not logged; the failing-client test now asserts the "Joining" and "Leaving" log lines, and #29 pins the leave):

| # | Mutation | File | Result |
| --- | --- | --- | --- |
| 1 | a live change reloads the page by itself | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (7 failing) |
| 2 | the agent's own change raises the banner | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (1 failing) |
| 3 | a change to another ticket raises the banner | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (1 failing) |
| 5 | a successful reload leaves the banner up | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (4 failing) |
| 6 | a reload clears a newer change too | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (1 failing) |
| 7 | the page never joins | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (4 failing) |
| 8 | navigation does not leave the old group | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (2 failing) |
| 9 | disposal does not leave the group | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (1 failing) |
| 10 | disposal leaves the presence handler hooked | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (1 failing) |
| 11 | disposal leaves the lease timer running | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (1 failing) |
| 12 | presence of another ticket is shown | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (1 failing) |
| 13 | the lease is a minute | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (1 failing) |
| 14 | the agent sees themself | `Features/Live/PresenceViewModel.cs` | KILLED (2 failing) |
| 15 | viewers come before repliers | `Features/Live/PresenceViewModel.cs` | KILLED (1 failing) |
| 16 | a lapsed hint stays replying | `Features/Live/PresenceViewModel.cs` | KILLED (2 failing) |
| 17 | viewing wins over replying | `Features/Live/PresenceViewModel.cs` | KILLED (1 failing) |
| 18 | the presence bar is not polite | `Features/Live/TicketPresenceBar.razor` | KILLED (1 failing) |
| 19 | the throttle is five seconds | `Features/Live/LiveCopy.cs` | KILLED (1 failing) |
| 20 | the throttle survives a blur | `Features/Tickets/ReplyComposer.razor.cs` | KILLED (1 failing) |
| 21 | blur does not clear the hint | `Features/Tickets/ReplyComposer.razor.cs` | KILLED (1 failing) |
| 22 | submit does not clear the hint | `Features/Tickets/ReplyComposer.razor.cs` | KILLED (1 failing) |
| 23 | disposal does not clear the hint | `Features/Tickets/ReplyComposer.razor.cs` | KILLED (1 failing) |
| 24 | an emptied box does not clear the hint | `Features/Tickets/ReplyComposer.razor.cs` | KILLED (1 failing) |
| 25 | another ticket does not clear the hint | `Features/Tickets/ReplyComposer.razor.cs` | KILLED (1 failing) |
| 26 | a failing composing call is not caught | `Features/Tickets/ReplyComposer.razor.cs` | KILLED (1 failing) |
| 27 | a failing join is not caught | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (1 failing) |
| 28 | the page ignores the kill switch when joining | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (1 failing) |
| 29 | a failing leave is not caught | `Features/Tickets/TicketDetailPage.razor.cs` | KILLED (1 failing) |

- [ ] **Step 8: Commit**

```bash
git add -A
git diff --cached --stat
git commit -F - <<'MSG'
feat: PHASE-10b detail live refresh, presence bar and composing hint (D-046)

TicketDetailPage joins its ticket group, shows a "New activity - refresh" banner for another agent's change
without touching the model, row version or draft, and a presence bar; ReplyComposer sends a throttled
composing hint cleared on blur, submit, empty text, ticket change and disposal.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
MSG
```

---

### Task 4: The end-to-end test (T17): the Worker's auto-close reaches the Admin's real client

**Review Focus pin:** 1 and 2 against the real hub (the client connects once; a missing token ends in "Offline" after one request; an expiring token is closed by the server, the reconnect asks for a fresh token and a `Resync` follows) and the T17 success criterion (a worker-originated change reaches an open Admin client exactly once).

**Files:**

- Test (create): `tests/TechStrap.Api.Tests/Live/AdminLiveClientHostTests.cs`

**Interfaces:**
- Consumes: Tasks 1 to 3 (`SignalRTicketLiveClient`, `HubLiveConnectionFactory` and its test seam, `LiveChangeRules`); the 10a test support of `TechStrap.Api.Tests`: `ApiFactory`, `WorkerFactory`, `ApiTestDatabase`, `TestPostgres`, `TicketTestData` (`SeedAsync`, `AgentClient`, `VersionAsync`), `HubTestSupport` (`AgentToken`, `Patience`), `TicketChangeNotify.ListenerApplicationName`, `ITicketChangeBroadcaster`, `IAutoCloseSolvedTicketsHandler`; `TestJwt` (a token with an expiry).
- Produces: `AdminLiveClientHostTests` (four tests). Nothing later depends on it. It lives in `TechStrap.Api.Tests` because that project already references the Api, the Worker and the Admin, and the Architecture rules constrain the source projects only (the spike, (d) above); no project reference or package changes.

- [ ] **Step 1: Check the branch and Docker**

```bash
git switch feat/phase-10b-live-admin     # Tasks 1 to 3 are committed; `git status` is clean
docker ps                                 # Testcontainers needs the daemon; leave other containers alone
```

- [ ] **Step 2: Write the end-to-end test and the real-hub checks**

The first test is T17. It seeds the 10a ticket set, solves ticket 0 through the real endpoint and backdates `solved_at` by eight days, builds the Admin's real client over the Api's `TestServer` (`HttpMessageHandlerFactory = api.Server.CreateHandler()`, long polling, a fixed test JWT in place of the circuit's token), starts it and joins the ticket (an answered call proves the connection is in the queue group, because `StartAsync` returns before `OnConnectedAsync` has finished), waits until the Api's listener shows in `pg_stat_activity`, runs the Worker's real handler, and reads exactly one change: the ticket, the number, `StatusChanged`, kind `Updated`, no actor (so no agent treats it as their own). A later NOTIFY is the barrier that proves no second copy follows. The other three tests use the real hub for the cases the fake cannot: an unknown ticket, a missing token, and a token that expires after four seconds.

`tests/TechStrap.Api.Tests/Live/AdminLiveClientHostTests.cs` (new)

```csharp
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Features.Live;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Application.Live;
using TechStrap.Application.Tickets.AutoClose;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;
using TechStrap.Infrastructure.Live;

namespace TechStrap.Api.Tests.Live;

/// <summary>
/// PHASE-10b end to end (T17), with the Admin's REAL <see cref="SignalRTicketLiveClient"/> on one side and the real Api hub on the other, in one process: the Worker's real auto-close handler closes a Solved ticket in
/// a real Postgres, the Worker's publisher NOTIFYs, the Api's real listener relays, the hub pushes, and the client raises exactly one change. Only the transport is a test double (long polling over the test server's handler,
/// the way the 10a hub tests connect) and the token provider (a fixed test JWT in place of the circuit's OIDC token). This test lives in Api.Tests because it is the one project that references the Api, the Worker and the Admin.
/// </summary>
public sealed class AdminLiveClientHostTests(TestPostgres postgres)
{
    private const string ListenerQuery = "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND application_name = '" + TicketChangeNotify.ListenerApplicationName + "' AND query LIKE 'LISTEN %'";

    private static SignalRTicketLiveClient NewClient(ApiFactory api, Func<string?> token, SessionExpiry? expiry = null) =>
        new(
            new HubLiveConnectionFactory(
                Microsoft.Extensions.Options.Options.Create(new ApiOptions { BaseUrl = api.Server.BaseAddress.ToString() }),
                http =>
                {
                    http.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                    http.Transports = HttpTransportType.LongPolling;
                }),
            new TestTokenProvider(token),
            expiry ?? new SessionExpiry(),
            TimeProvider.System,
            NullLogger<SignalRTicketLiveClient>.Instance);

    private static async Task WaitForAsync(Func<Task<bool>> condition, string what)
    {
        using var timeout = new CancellationTokenSource(HubTestSupport.Patience);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
        while (!await condition())
        {
            await Task.Delay(50, linked.Token);
        }

        what.ShouldNotBeNull();
    }

    private static Task<T> NextAsync<T>(Channel<T> channel)
    {
        return NextCoreAsync();

        async Task<T> NextCoreAsync()
        {
            using var timeout = new CancellationTokenSource(HubTestSupport.Patience);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
            return await channel.Reader.ReadAsync(linked.Token);
        }
    }

    [Fact(Timeout = 240000)]
    public async Task A_ticket_the_worker_auto_closes_reaches_the_admin_client_exactly_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var apiSettings = new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" };
        await using var api = new ApiFactory(settings: apiSettings);
        await using var worker = new WorkerFactory(settings: new Dictionary<string, string?> { ["ConnectionStrings:TechStrap"] = database.ConnectionString });
        var seed = await TicketTestData.SeedAsync(api, ct);
        var ticketId = seed.Tickets[0].Id;

        // A Solved ticket whose solve is eight days old (the Worker closes after seven): solved through the real endpoint, then backdated in the database.
        using (var sam = TicketTestData.AgentClient(api, "sam"))
        {
            var solved = await sam.PutAsJsonAsync($"/api/tickets/{ticketId}/status", new ChangeTicketStatusRequest(TicketStatuses.Solved, await TicketTestData.VersionAsync(sam, ticketId)), ct);
            solved.EnsureSuccessStatusCode();
        }

        await database.ExecuteAsync($"UPDATE tickets SET solved_at = now() - interval '8 days' WHERE id = '{ticketId}'");

        await using var client = NewClient(api, () => HubTestSupport.AgentToken("sam", "Sam"));
        var changes = Channel.CreateUnbounded<TicketChangedDto>();
        client.TicketChanged += change => changes.Writer.TryWrite(change);
        await client.StartAsync(ct);
        client.State.ShouldBe(LiveConnectionState.Connected);

        // A answered call proves the connection is in the queue group (StartAsync returns before OnConnectedAsync completes); the join of the real ticket returns its presence.
        (await client.JoinTicketAsync(ticketId, ct)).ShouldNotBeNull().TicketId.ShouldBe(ticketId);
        await WaitForAsync(async () => await database.ScalarAsync<long>(ListenerQuery) == 1, "the Api listener is listening");

        await using (var scope = worker.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IAutoCloseSolvedTicketsHandler>().HandleAsync(ct);
            result.IsSuccess.ShouldBeTrue();
            result.Value.Closed.ShouldBe(1);
        }

        var closed = await NextAsync(changes);
        closed.TicketId.ShouldBe(ticketId);
        closed.TicketNumber.ShouldBe(seed.Tickets[0].Number.ToString());
        closed.EventType.ShouldBe(TicketEventTypes.StatusChanged);
        closed.Kind.ShouldBe(TicketChangeKinds.Updated);
        closed.ActorAgentId.ShouldBeNull("the Worker acts as the system, so no agent's page treats it as their own change");
        LiveChangeRules.IsOwn(closed, seed.Sam.Id).ShouldBeFalse();
        LiveChangeRules.Concerns(closed, ticketId).ShouldBeTrue();

        // No second copy follows. A barrier proves it: a later message on the same connection arrives after every earlier one, so once it is here nothing else for the ticket is coming.
        var barrier = new TicketChange(Guid.NewGuid(), Guid.NewGuid(), "ORB-99", Guid.NewGuid(), TicketEventTypes.Assigned, null, DateTimeOffset.UtcNow, TicketChangeKinds.Updated);
        await worker.Services.GetRequiredService<ITicketChangeBroadcaster>().PublishAsync(barrier, ct);
        var received = new List<TicketChangedDto>();
        TicketChangedDto next;
        do
        {
            next = await NextAsync(changes);
            received.Add(next);
        }
        while (next.EventId != barrier.EventId);

        received.Where(change => change.TicketId == ticketId).ShouldBeEmpty("auto-close must reach the client exactly once");
        (await database.ScalarAsync<string>($"SELECT status FROM tickets WHERE id = '{ticketId}'")).ShouldBe(TicketStatuses.Closed);
    }

    [Fact(Timeout = 120000)]
    public async Task A_ticket_the_hub_does_not_know_is_not_joined_and_nothing_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var api = new ApiFactory(settings: new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" });
        await TicketTestData.SeedAsync(api, ct);
        await using var client = NewClient(api, () => HubTestSupport.AgentToken("sam", "Sam"));
        await client.StartAsync(ct);

        (await client.JoinTicketAsync(Guid.NewGuid(), ct)).ShouldBeNull();
        await client.SetComposingAsync(Guid.NewGuid(), true, ct);
        await client.LeaveTicketAsync(Guid.NewGuid(), ct);

        client.State.ShouldBe(LiveConnectionState.Connected);
    }

    [Fact(Timeout = 120000)]
    public async Task Without_a_token_the_hub_refuses_and_the_client_stops_for_good()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = new ApiFactory();
        var asked = 0;
        await using var client = NewClient(api, () =>
        {
            Interlocked.Increment(ref asked);
            return null;
        });

        await client.StartAsync(ct);

        client.State.ShouldBe(LiveConnectionState.Disconnected);
        asked.ShouldBe(1);
    }

    [Fact(Timeout = 180000)]
    public async Task When_the_token_expires_the_hub_closes_the_connection_and_the_reconnect_uses_a_fresh_token_and_asks_for_a_resync()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var api = new ApiFactory(settings: new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" });
        await TicketTestData.SeedAsync(api, ct);
        var tokensIssued = 0;
        await using var client = NewClient(api, () =>
        {
            // The first token lives four seconds (the server closes the connection at its expiry); every later one is fresh.
            return Interlocked.Increment(ref tokensIssued) == 1
                ? HubTestSupport.AgentToken("sam", "Sam", expires: DateTime.UtcNow.AddSeconds(4))
                : HubTestSupport.AgentToken("sam", "Sam");
        });
        var changes = Channel.CreateUnbounded<TicketChangedDto>();
        client.TicketChanged += change => changes.Writer.TryWrite(change);
        await client.StartAsync(ct);

        var resync = await NextAsync(changes);

        resync.Kind.ShouldBe(TicketChangeKinds.Resync);
        resync.TicketId.ShouldBe(Guid.Empty);
        tokensIssued.ShouldBeGreaterThanOrEqualTo(2, "the reconnect asked for a fresh token");
        await WaitForAsync(() => Task.FromResult(client.State == LiveConnectionState.Connected), "connected again");
    }

    private sealed class TestTokenProvider(Func<string?> token) : IUserAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(token());
    }
}
```



- [ ] **Step 3: See it fail when the chain is broken (RED by mutation)**

These tests cover behavior that Tasks 1 to 3 and 10a already built, so they pass at once; their RED is the failure of a mutation that cuts the chain. From the repository root, with Docker running:

```bash
python $T/mut.py src/TechStrap.Infrastructure/Live/PgNotifyTicketChangeBroadcaster.cs --replace "        await command.ExecuteNonQueryAsync(cancellationToken);" "        await Task.CompletedTask;" -- dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-class "*AdminLiveClientHostTests"
```

Expected: `failed: 1`, `succeeded: 3`, then `KILLED (the command failed)`: the Worker never sends the NOTIFY, the auto-close test waits for its ceiling (30 seconds) and fails. The file is put back by the tool.

- [ ] **Step 4: Run the tests (GREEN)**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-class "*AdminLiveClientHostTests"`

Expected: PASS, `total: 4, failed: 0` (about 20 seconds).

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-namespace TechStrap.Api.Tests.Live`

Expected: PASS, `total: 31, failed: 0` (the 10a hub, listener, metrics and relay tests are unchanged and still pass beside it), and `dotnet test --project tests/TechStrap.Architecture.Tests -c Release` (Expected: `total: 295, failed: 0`).

Run: `docker ps`

Expected: no container of yours is left (Testcontainers disposes them; do not stop containers you did not start).

- [ ] **Step 5: Record the mutations**

`$T\specs\m4.py`:

```python
T17 = ["dotnet", "test", "--project", "tests/TechStrap.Api.Tests", "-c", "Release", "--filter-class", "*AdminLiveClientHostTests"]
S = "src/TechStrap.Admin/Features/Live/"
LIVE = ["dotnet", "test", "--project", "tests/TechStrap.Admin.Tests", "-c", "Release", "--filter-namespace", "TechStrap.Admin.Tests.Live"]

MUTATIONS = [
    ("1 the client listens to the wrong hub method", S + "HubLiveConnection.cs", [("_hub.On<TicketChangedDto>(TicketHubMethods.TicketChanged,", "_hub.On<TicketChangedDto>(TicketHubMethods.PresenceChanged,")], T17),
    ("2 the client never sends a token", S + "SignalRTicketLiveClient.cs", [("if (string.IsNullOrEmpty(token))", "if (true || string.IsNullOrEmpty(token))")], T17),
    ("3 the first token is reused for every reconnect", S + "SignalRTicketLiveClient.cs", [("    private volatile bool _noToken;\n", "    private volatile bool _noToken;\n    private string? _cached;\n"), ("var token = await _tokens.GetAccessTokenAsync(_lifetime.Token);", "var token = _cached ??= await _tokens.GetAccessTokenAsync(_lifetime.Token);")], T17),
    ("4 the hub address ignores the hub path", S + "HubLiveConnection.cs", [("TicketHubRoutes.Path.TrimStart('/')", "string.Empty")], T17),
    ("5 the client never joins a ticket", S + "SignalRTicketLiveClient.cs", [("return connection is null ? null : await JoinAsync(connection, ticketId, cancellationToken);", "return null;")], T17),
    ("6 the retry policy retries a refused token (unit suite)", S + "SignalRTicketLiveClient.cs", [("new LiveRetryPolicy(() => _noToken || _expiry.IsLapsed)", "new LiveRetryPolicy(() => _expiry.IsLapsed)")], LIVE),
    ("7 the worker never sends the NOTIFY", "src/TechStrap.Infrastructure/Live/PgNotifyTicketChangeBroadcaster.cs", [("        await command.ExecuteNonQueryAsync(cancellationToken);", "        await Task.CompletedTask;")], T17),
]
```

`git add -A`, then in the foreground, one at a time (each run waits up to the 30-second ceiling before it fails, so a batch takes several minutes; give the shell call the full ten minutes and keep it to the first four, then run the rest):

```bash
python $T/run_muts.py $T/specs/m4.py $T/res_m4.txt 1 2 3 4
python $T/run_muts.py $T/specs/m4.py $T/res_m4b.txt 5
python $T/run_muts.py $T/specs/m4.py $T/res_m4c.txt 6 7
```

Every mutation must be KILLED. (A first batch that was started with a shell `timeout` shorter than its runtime was killed inside mutation 5: the source stayed mutated and the next run reported a false survivor from a stale build. `git checkout -- <file>` and a run of that one mutation alone gave the result below.) Mutation 6 is the Admin unit suite's, listed here because it is the retry policy's refusal of a missing token, which no real-hub test can see (the start loop stops first):

| # | Mutation | File | Result |
| --- | --- | --- | --- |
| 1 | the client listens to the wrong hub method | `Features/Live/HubLiveConnection.cs` | KILLED (1 failing) |
| 2 | the client never sends a token | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (3 failing) |
| 3 | the first token is reused for every reconnect | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (1 failing) |
| 4 | the hub address ignores the hub path | `Features/Live/HubLiveConnection.cs` | KILLED (2 failing) |
| 5 | the client never joins a ticket | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (1 failing) |
| 6 | the retry policy retries a refused token (unit suite) | `Features/Live/SignalRTicketLiveClient.cs` | KILLED (1 failing) |
| 7 | the worker never sends the NOTIFY | `Infrastructure/Live/PgNotifyTicketChangeBroadcaster.cs` | KILLED (1 failing) |

- [ ] **Step 6: Commit**

```bash
git add -A
git diff --cached --stat
git commit -F - <<'MSG'
test: PHASE-10b end-to-end auto-close reaches the real Admin live client (T17)

One in-process test wires Postgres (Testcontainers), the Worker's real auto-close handler and NOTIFY publisher,
the Api's real listener and hub on a TestServer, and the Admin's real SignalRTicketLiveClient; plus real-hub
checks for an unknown ticket, a missing token and a token that expires (fresh token, resync).

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
MSG
```

---

### Task 5: Close-out: the decisions log, the Admin guide, the spec ticks, the rows and the pins

**Review Focus pin:** the whole branch (the docs say what was built and what is not: kill switch, manual real-IdP checklist, known gaps), and the full verification of the pull request.

**Files:**

- Modify: `docs/architecture/04-DECISION-LOG.md`, `docs/development/ADMIN-APP.md`, `docs/self-hosting/DEPLOYMENT.md`, `docs/architecture/PHASE-10-live-updates.md`, `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`, `docs/architecture/00-DISCOVERY-INDEX.md`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`

**Interfaces:**
- Consumes: everything built in Tasks 1 to 4 (the names, the numbers, the test names the docs quote), the 03-PACKAGE-MAP rows of Task 1, the D-046 text and pins of 10a (header bullet, index row, phrases), the log's addendum pattern (D-045's 09c and 09d addenda).
- Produces: the **D-046 addendum (2026-10-07, PHASE-10b live updates in the Admin)** (no D-047: see "Decisions made while drafting"), the ADMIN-APP.md sections "Live updates (10b)" (behavior, kill switch, the manual real-identity-provider checklist) and "Known gaps in 10b", the configuration row and the `Live/` row of the code tree, the corrected stale PHASE-10 sentences, the DEPLOYMENT.md sentence about the one Admin switch (it keeps the pinned phrase "No new settings", now "No new settings for the Api or the Worker"), P10-T11 to T17 and every deliverable and success criterion ticked with as-built notes, the roadmap and discovery rows ("10a merged (PR #18); 10b complete (pending merge); PHASE-10 complete (pending merge)"), and the Pester pins that hold all of it.

- [ ] **Step 1: Check the branch**

```bash
git switch feat/phase-10b-live-admin     # Tasks 1 to 4 are committed; `git status` is clean
```

- [ ] **Step 2: Write the pins first**

The two pins that said "ticks only what 10a delivers" and "10a is complete pending merge" become pins for the finished phase; three new tests hold the addendum, the Admin guide, the runbook and the package map.

`scripts/tests/RepositoryDocs.Tests.ps1` (modify)

```diff
diff --git a/scripts/tests/RepositoryDocs.Tests.ps1 b/scripts/tests/RepositoryDocs.Tests.ps1
--- a/scripts/tests/RepositoryDocs.Tests.ps1
+++ b/scripts/tests/RepositoryDocs.Tests.ps1
@@ -492,26 +492,30 @@ Describe 'D-046 (live updates)' {
         }
     }
 
-    It 'ticks only what 10a delivers: P10-T01 to T10 and the three server deliverables, with the Admin work still open' {
+    It 'ticks all of PHASE-10 after 10b: P10-T01 to T17, every deliverable and every success criterion' {
         $spec = Get-RepoText 'docs/architecture/PHASE-10-live-updates.md'
-        foreach ($number in 1..10) {
+        foreach ($number in 1..17) {
             $id = 'P10-T{0:00}' -f $number
-            $spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is delivered by 10a"
+            $spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is delivered by 10a or 10b"
         }
         foreach ($number in 11..17) {
             $id = 'P10-T{0:00}' -f $number
-            $spec | Should -Match ('(?m)^- \[ \] \*\*' + $id + '\*\*') -Because "$id is 10b"
+            $spec | Should -Match ('(?s)- \[x\] \*\*' + $id + '\*\*.*?\*\*As built \(10b\):\*\*') -Because "$id carries its as-built note"
         }
-        $spec | Should -Match '(?m)^- \[x\] `TicketHub`, `UpdateTicketPresenceHandler`'
-        $spec | Should -Match '(?m)^- \[x\] Post-commit publishing hook in Infrastructure'
-        $spec | Should -Match '(?m)^- \[x\] Contracts: `TicketChangedDto`'
-        $spec | Should -Match '(?m)^- \[ \] Admin `ITicketLiveClient`'
-        $spec | Should -Match '(?m)^- \[ \] Reverse-proxy/WebSocket notes'
+        $deliverables = ($spec -split '(?m)^## Deliverables')[1] -split '(?m)^## Actionable Tasks' | Select-Object -First 1
+        $deliverables | Should -Not -Match '- \[ \]'
+        $criteria = ($spec -split '(?m)^## Success Criteria')[1] -split '(?m)^## Boundary Validation' | Select-Object -First 1
+        $criteria | Should -Not -Match '- \[ \]'
+        $spec | Should -Match '(?m)^- \[x\] Admin `ITicketLiveClient`'
+        $spec | Should -Match '(?m)^- \[x\] Reverse-proxy/WebSocket notes'
+        $spec | Should -Match '\*\*Kill switch \(10b addendum, 2026-10-07\)\.\*\*'
     }
 
-    It 'says in the roadmap and discovery rows that 10a is complete pending merge and 10b is ready to start using Blazor.Auth 0.2.0' {
+    It 'says in the roadmap and discovery rows that 10a is merged and PHASE-10 is complete pending merge' {
         foreach ($file in 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md', 'docs/architecture/00-DISCOVERY-INDEX.md') {
-            (Get-RepoText $file) | Should -Match '(?m)^\| 10 \|.*\| 10a complete \(pending merge\); 10b \(Admin\) ready to start after 10a merges, using SyntaxCircus\.Blazor\.Auth 0\.2\.0 \|'
+            $text = Get-RepoText $file
+            $text | Should -Match '(?m)^\| 10 \|.*\| 10a merged \(PR #18\); 10b complete \(pending merge\); PHASE-10 complete \(pending merge\): the owner'
+            $text | Should -Not -Match '10a complete \(pending merge\)'
         }
     }
 
@@ -530,3 +534,36 @@ Describe 'D-046 (live updates)' {
     }
 }
 
+Describe 'D-046 addendum (PHASE-10b, live updates in the Admin)' {
+    BeforeAll { $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md' }
+
+    It 'records the owner rulings and the spike findings as an addendum of D-046, not as a new decision' {
+        $script:Log | Should -Match '(?m)^### Addendum \(2026-10-07, PHASE-10b live updates in the Admin\)'
+        $script:Log | Should -Not -Match '(?m)^## D-047'
+        $addendum = ($script:Log -split '(?m)^### Addendum \(2026-10-07, PHASE-10b live updates in the Admin\)')[1]
+        foreach ($phrase in 'LiveUpdates:Enabled', 'LIVEUPDATES__ENABLED', 'NullTicketLiveClient', 'AdminLiveClientHostTests', 'Own changes are ignored', 'Queue updated - refresh', 'IUserAccessTokenProvider',
+                'OnAfterRenderAsync', 'compose is not edited', 'Known limits (10b)', 'There is no new decision number') {
+            $addendum | Should -Match ([regex]::Escape($phrase)) -Because "the 10b addendum must mention $phrase"
+        }
+    }
+
+    It 'documents live updates for agents and operators, with the manual check and the known gaps, and no longer says they are coming' {
+        $admin = Get-RepoText 'docs/development/ADMIN-APP.md'
+        $admin | Should -Match '(?m)^## Live updates \(10b\)'
+        $admin | Should -Match '(?m)^### Manual check against a real identity provider \(owner\)'
+        $admin | Should -Match '(?m)^## Known gaps in 10b'
+        $admin | Should -Match '(?m)^\| `LIVEUPDATES__ENABLED` \| no \| `true` \|'
+        $admin | Should -Not -Match 'live updates arrive with PHASE-10'
+        $admin | Should -Not -Match 'it is not live \(PHASE-10\)'
+        $runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md'
+        $runbook | Should -Match 'LIVEUPDATES__ENABLED'
+    }
+
+    It 'maps the packages 10b uses: Blazor.Auth 0.2.0 with the token provider, and the SignalR client in the Admin' {
+        $map = Get-RepoText 'docs/architecture/03-PACKAGE-MAP.md'
+        $map | Should -Match '\| `SyntaxCircus\.Blazor\.Auth` \| 0\.2\.0 \|'
+        $map | Should -Match 'IUserAccessTokenProvider'
+        $map | Should -Match 'SignalRTicketLiveClient'
+    }
+}
+
```



- [ ] **Step 3: Run the pins to see them fail (RED)**

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1`

Expected: FAIL (the real run): `Tests Passed: 55, Failed: 4`:

```
[-] D-046 (live updates).ticks all of PHASE-10 after 10b: P10-T01 to T17, every deliverable and every success criterion
[-] D-046 (live updates).says in the roadmap and discovery rows that 10a is merged and PHASE-10 is complete pending merge
[-] D-046 addendum (PHASE-10b, live updates in the Admin).records the owner rulings and the spike findings as an addendum of D-046, not as a new decision
[-] D-046 addendum (PHASE-10b, live updates in the Admin).documents live updates for agents and operators, with the manual check and the known gaps, and no longer says they are coming
```

(the package-map pin already passes: Task 1 mapped the packages.)

- [ ] **Step 3b: Write the documents**

Apply these edits to existing files (CRLF working tree: use `edit.py` or the editor's replace; the added ADMIN-APP.md sections go at the end of the file, after "Known gaps in 08"; the decision-log addendum goes at the end of the file, after D-046's "Approval" block). The text is quoted from the proven commit, so the names, the numbers and the test names are those that ran.

`docs/architecture/04-DECISION-LOG.md` (modify)

```diff
diff --git a/docs/architecture/04-DECISION-LOG.md b/docs/architecture/04-DECISION-LOG.md
--- a/docs/architecture/04-DECISION-LOG.md
+++ b/docs/architecture/04-DECISION-LOG.md
@@ -1891,3 +1891,25 @@ PHASE-09 is merged, so PHASE-10 can start. Reading the code before planning foun
 ### Approval
 - **Approved by:** Jon Seeley (owner, PHASE-10 planning)
 - **Approved on:** 2026-10-07
+
+### Addendum (2026-10-07, PHASE-10b live updates in the Admin)
+The owner's rulings for PHASE-10b, and what the plan's spikes proved. They extend D-046; where they differ from the text above, they win. There is no new decision number, as for the PHASE-09c and 09d addenda of D-045.
+
+**Rulings**
+- **One kill switch.** `LiveUpdates:Enabled` (`LIVEUPDATES__ENABLED`, default `true`) is the one new setting, so "No new setting" above holds for the Api and the Worker only. Off, `NullTicketLiveClient` is registered: no connection, no indicator, no banner, no presence bar, and the pages behave exactly as before. The hub address is `API__BASEURL` plus `TicketHubRoutes.Path`; backoff, debounce and throttle are constants. The four edits of D-043 are `appsettings.json`, the host `.env.example`, `deploy/.env.admin.example` and `scripts/tests/ConfigContract.Tests.ps1`; compose is not edited because the switch is an operator setting (an `environment:` entry would override the operator's env file), and a new pin asserts that.
+- **T17.** An automated in-process integration test (`AdminLiveClientHostTests`, in `TechStrap.Api.Tests`) plus a short manual real-identity-provider checklist in ADMIN-APP.md. The compose smoke script is unchanged.
+- **Own changes are ignored.** A `TicketChanged` whose `ActorAgentId` is the signed-in agent raises no banner (a `Resync`, and a change with no actor, never count as own). Another tab of the same agent still meets the 409 on a send.
+- **The queue banner.** One "Queue updated - refresh" banner for any other agent's change or a `Resync`, after a one-second window. Nothing is reordered or reloaded until the agent clicks; the click is the existing load with the current filters.
+- **Carried from D-046.** The detail banner does not take the new row version until it is clicked, so a send before that gets the 409; the draft is never touched; the presence name is `Agent.Name`, shown to other agents only, and the agent's own entry is removed by `AgentSession.Agent.Id`.
+
+**Spike findings (proved in a scratch clone before the plan was written)**
+- **The start.** Pages are prerendered (`InteractiveServer` with prerendering): the first render runs on the HTTP request with no circuit, and `OnAfterRenderAsync` does not run there. `LiveConnectionIndicator` in `MainLayout` is the one starter: it asks only from `OnAfterRender`, only when `AgentSession` is Ready (the layout is outside `AgentGate`, so it waits for `Session.Changed`), and the client's own guard (`_starting ??=`) keeps a scope to one connection however often it is asked. A host test with the real client and a counting connection factory proves a prerender of `/` and `/queue/mine` creates none.
+- **The token.** `IUserAccessTokenProvider` (0.2.0, scoped, `ValueTask<string?> GetAccessTokenAsync(CancellationToken)`) goes into the hub's `AccessTokenProvider`, so SignalR asks for a token on every start and every reconnect: a reconnect gets a fresh one (proved against the real hub: a token that expires after four seconds is closed by the server, and the second token is used). A null token stops the client for good (a hub request without a token is refused, so every retry would be); a throw is logged by type and retried. The hub connection does not use the named API `HttpClient`s (they carry the retry and the auth handler); `HubLiveConnectionFactory` is a singleton that holds the validated `Api` options only and has a test seam for the message handler and the transport.
+- **The seam.** `ITicketLiveClient` is what components see; `ILiveConnection` hides `HubConnection` from the client's own logic, so the unit tests script the reconnect, the duplicates and the disposal. A `FakeTicketLiveClient` is registered once in `AdminComponentTest`; one test class that derives `BunitContext` directly (`LayoutResilienceTests`) needed its own registration.
+- **T17.** `TechStrap.Api.Tests` references the Api, the Worker and the Admin, and the Architecture rules constrain the source projects only, so it is the one place the whole chain can run: Testcontainers Postgres, the Worker's real `AutoCloseSolvedTicketsHandler` and NOTIFY publisher, the Api's real listener and hub on a `TestServer`, and the Admin's real `SignalRTicketLiveClient` with a fixed test token over long polling.
+
+**Consequences of the addendum (as built in 10b)**
+- **As built in 10b: the client.** `SignalRTicketLiveClient` is public, scoped and `IAsyncDisposable`; `LiveRetryPolicy` never gives up (0, 2, 5, 10, then 30 seconds) and stops for a lapsed `SessionExpiry` or a missing token; after a reconnect it joins its tickets again and raises a synthetic `Resync`; it remembers the last 256 event ids; every failure is logged by exception type and shows as a state, never as an exception in a page.
+- **As built in 10b: the pages.** The queue's banner is a one-second window that the first change opens (a window that restarted on every change would never fire in a steady trickle). The detail page joins after each load, leaves on navigation and disposal, keeps the banner when a change arrives while its reload runs, and shows a "replying" hint as "viewing" when the 10-second lease passes with no news (the hub does not push the lapse). The composer sends its hint from the `Text` setter at most every four seconds and clears it on blur, send, empty text, another ticket and disposal.
+- **As built in 10b: packages.** `SyntaxCircus.Blazor.Auth` 0.2.0; `Microsoft.AspNetCore.SignalR.Client` is now also an Admin package (`AdminRules.AllowedPackages`); nothing else is new and there is no migration.
+- **Known limits (10b).** See "Known gaps in 10b" in ADMIN-APP.md: one connection per tab, both banners after any reconnect, no reconnect jitter, a hub that ends because the session ended is not reported to the session, and the failed-emails badge is not driven by the hub. The D-046 limits (a deactivated agent's open socket, erasure publishing nothing, one Api instance) stand.
```

`docs/development/ADMIN-APP.md` (modify)

```diff
diff --git a/docs/development/ADMIN-APP.md b/docs/development/ADMIN-APP.md
--- a/docs/development/ADMIN-APP.md
+++ b/docs/development/ADMIN-APP.md
@@ -49,6 +49,7 @@ The Admin refuses to start with a message that names the missing variable. Value
 | `TECHSTRAP_ADMIN_GROUP` | no | `techstrap-admins` | Same. |
 | `TECHSTRAP_GROUP_CLAIM_TYPE` | no | `groups` | Same. |
 | `TECHSTRAP_PORTAL_PUBLIC_URL` | no | | The customer portal's public base URL, the same key the API reads. The Admin uses it only for the "View on portal" link of a published article; blank hides the link. Absolute http or https, no query or fragment (the Admin refuses to start otherwise). |
+| `LIVEUPDATES__ENABLED` | no | `true` | The live-updates kill switch (10b): `false` opens no hub connection and draws no indicator, banner or presence bar (see Live updates (10b)). The hub address is `API__BASEURL` plus `/hubs/tickets`. |
 | `DATAPROTECTION__KEYRINGPATH` | in containers | | Persistent folder for the cookie and antiforgery keys. |
 | `TRUSTEDPROXY__*`, `ALLOWEDHOSTS` | production | | Trust only your reverse proxy, so the sign-in redirect URI is built with the public scheme and host. |
 | `SENTRY__*`, `OPENTELEMETRY__*`, `SERILOG__MINIMUMLEVEL__DEFAULT` | no | | Observability; see the `.env.example`. |
@@ -137,6 +138,7 @@ src/TechStrap.Admin/
     Shell/        StatusMessageService, ShortcutService, ShortcutCatalog, CommandRegistry (the palette's commands), LocalTimeService (the browser's time zone), UncertainMarks (writes held until a later read)
     Queue/        TicketQueuePage and its filter bar, tabs and row
     Tickets/      TicketDetailPage, presenter, timeline factory, ReplyComposer, TicketSidebar, TagPicker, TicketActions
+    Live/         Live updates (10b): ITicketLiveClient and SignalRTicketLiveClient (one per circuit; NullTicketLiveClient when the switch is off), LiveConnectionIndicator (the one place the connection starts), QueueLiveBanner, ChangedTicketBanner, TicketPresenceBar, PresenceViewModelFactory, LiveChangeRules (own change, this ticket), LiveRetryPolicy
     Settings/     Admin only. Products/ (ProductsPage, ProductEditorPage, ProductKeysPage, ApiKeysPanel, NewApiKeyDialog; the logo rule is `BrandingRules.IsAcceptableLogoUrl` in Contracts.Branding), Agents/ (AgentsPage), Tags/ (TagsPage),
                   Audit/ (AdminEventsPage, AdminEventSummaryFactory), EmailKinds
     Ops/          Admin only. DeadLetters/ (DeadLettersPage)
@@ -203,7 +205,7 @@ The ticket commands raise the same shortcut action as their key, so the composer
 ## What an agent can do here (07a)
 
 - **Queue**: six views (Unassigned is the default, then Mine, Open, Pending, All and the separate Spam view), counts per view (Spam muted), filters (product, status, priority, tag), search, 25 per page. Every filter is in
-  the URL, so views can be bookmarked. The queue refreshes when you press Refresh; live updates arrive with PHASE-10.
+  the URL, so views can be bookmarked. The queue refreshes when you press Refresh, and a banner offers the same refresh when something changes (Live updates (10b), below).
 - **Ticket**: one timeline of messages and changes (customer white, public reply canary, internal note pink with a dashed edge), the requester, metadata labeled **Untrusted** unless it came from a trusted key,
   attachments as downloads (served through the Admin, never inline), and a link to the parent of a follow-up.
 - **Reply or note**: separate drafts per mode, files (up to 5, 10 MB each), Pending by default or "Send and solve". A failed send, or a conflict, never loses the text or the files.
@@ -364,7 +366,7 @@ render, so read it before the action; services cannot be added after the first r
 
 Recorded in D-040 and tracked for later phases: no counts in the erase and delete dialogs and no list of a ticket's follow-ups (the API offers neither); the requester card has no ticket count or first-seen date;
 times are shown in UTC (07c shows them in the browser's zone); "Apply my change again" after a conflict is not built (Reload only); a lost circuit loses an unsent draft (the leave-warning covers a reload);
-no knowledge-base article picker (added in 08); no presence or live updates (PHASE-10); no command palette (added in 07c); the manual sign-in check against a real Authentik is outstanding.
+no knowledge-base article picker (added in 08); no presence or live updates (added in 10b); no command palette (added in 07c); the manual sign-in check against a real Authentik is outstanding.
 
 ## Decisions that changed during 07b
 
@@ -383,7 +385,7 @@ Recorded in D-041 and tracked for later phases:
 - Ticket counts on the tags page, and the count in the delete dialog, are read when the list loads. A tag that gains tickets meanwhile is caught by the API (409 `tag-in-use`) and shown again with its new count.
 - The audit log filters by what changed and by who only: the API has no event-type or date filter. Events show ids, slugs, prefixes and counts and never names, because a payload carries none.
 - My settings has the new-ticket alerts only: the UX brief's assignment-alert switch has no API field yet.
-- Discard on a failed email takes no reason (the API has none). The failed-emails count in the navigation is read when the shell loads and after you act on the page; it is not live (PHASE-10).
+- Discard on a failed email takes no reason (the API has none). The failed-emails count in the navigation is read when the shell loads and after you act on the page; it is not live: the hub does not drive it (see Known gaps in 10b).
 - Times were shown in UTC, as in 07a, until 07c.
 - The OpenAPI security schemes, the CSP and the responsive and accessibility pass were done in 07c.
 
@@ -411,3 +413,44 @@ Recorded in D-044 and tracked for later phases:
 - Pictures (API side, for the operator): an upload is checked by its real type (PNG, JPEG, GIF, WebP) and only the first 1024 bytes are scanned for markup; the defences are the sniffed type, `X-Content-Type-Options: nosniff`, a CSP `sandbox` on the picture and serving it from the API's own origin. `/kb-images/{key}` (GET and HEAD) has no rate limit: rely on the proxy or CDN. Picture addresses are built from `TECHSTRAP_API_PUBLIC_URL` (the local compose sets `http://localhost:8080`); a path prefix in that address needs the proxy to strip it before the API.
 - Public API (for the portal): every text field of the public knowledge base DTOs, snippets included, is **plain text**; only `Html` is HTML, so the portal must HTML-encode the rest. A malformed slug or key answers an empty result or 404, never a 500. The agent `/api/kb` responses carry `Cache-Control: no-store`, and the JSON body limit is 2 MiB. Every mutating knowledge base call checks that the agent is still active.
 - The manual checks (write and publish an article with a picture; the picture loads through the API's public address behind the proxy; a linked article appears in a reply email) are the owner's.
+
+## Live updates (10b)
+
+What an agent sees (PHASE-10, D-046). Nothing here moves a page by itself: a change only raises a banner, and the agent decides when to take it.
+
+- **Indicator.** A small status line above the status bar says "Live", "Connecting", "Reconnecting" or "Offline". It is a polite live region, so a screen reader hears a change without being interrupted, and the state is always written, never color alone. It is drawn once the agent is admitted, and it is the only place the connection starts.
+- **Queue.** When another agent, the customer or the Worker (auto-close) changes any ticket, or the connection comes back after a gap, one banner "Queue updated – refresh" appears after a one-second window, however many changes there were. Nothing reloads or reorders until the banner is clicked; the click is the ordinary Refresh with the current filters. Your own changes raise nothing.
+- **Ticket.** A change to the open ticket by someone else raises "New activity – refresh". Until the click the page keeps what it shows, including the row version, so a send in between still gets "This ticket changed since you opened it" and the draft is kept. The click reloads the timeline, the status and the row version. Your own changes (this tab or another) raise nothing; a send from another tab of yours still meets the 409.
+- **Presence.** "Ada Admin is viewing" or "is replying" above the timeline: the other agents on the ticket, by their agent name (the email when there is none), never the customer-facing name and never yourself. "Replying" is sent while you type (at most every 4 seconds); it ends when you leave the box, send, empty the box, open another ticket or close the tab, and it lapses on its own after 10 seconds.
+- **Reconnect.** While the session lives the Admin keeps trying (at once, then after 2, 5 and 10 seconds, then every 30), asks for a fresh token each time, joins the open ticket again and raises both banners (notifications of the gap are gone). It stops, showing "Offline", when the session has lapsed or no token is available.
+- **A live failure never reaches a page.** A hub, token or network failure is logged (the exception type only, never a token or a message) and shows as "Reconnecting" or "Offline"; the page, the composer and the draft work as before.
+
+### The kill switch
+
+`LIVEUPDATES__ENABLED` (`LiveUpdates:Enabled`, default `true`) in `.env.admin` or `.env.local`. `false`: no connection is ever opened and no indicator, banner or presence bar is drawn; the Admin behaves exactly as it did before PHASE-10. Restart the Admin to change it. Compose does not set it, so the env file is the one place; the numbers (backoff, the one-second window, the 4-second hint) are constants.
+
+### Manual check against a real identity provider (owner)
+
+Two browsers, two agents (A and B), the stack running with the Worker.
+
+1. A and B open the queue: both show "Live". B changes a ticket's status: A sees "Queue updated – refresh" within about two seconds and B does not. Clicking it reloads A's queue with A's filters.
+2. A and B open the same ticket: each sees the other as viewing. B types: A sees "B is replying" within about two seconds, and it goes when B leaves the box, sends or closes the tab.
+3. A types a draft; B replies. A sees "New activity – refresh" and the draft is untouched. A sends before clicking: "This ticket changed since you opened it", the draft is kept; the banner then shows B's reply.
+4. Worker auto-close: with the seed data (`TECHSTRAP_SEED_DEV_DATA=true`, `docs/development/DEV-DATA.md`), make a Solved ticket whose `solved_at` is older than the auto-close days (the smallest is `TECHSTRAP_AUTOCLOSE_DAYS=1`, so backdate it in the database), start the Worker with `AUTOCLOSE__INTERVALMINUTES=1`: within a minute A's open queue shows the banner once, and the ticket is Closed.
+5. Stop the Api for a minute: the indicator says "Reconnecting"; start it again: "Live", and both banners appear.
+6. Set `LIVEUPDATES__ENABLED=false` and restart the Admin: no indicator, no banner, no presence bar, and no request to `/hubs/tickets` in the Api log.
+
+The automated end-to-end test (`AdminLiveClientHostTests`) runs the Worker's auto-close handler, NOTIFY, the Api's listener and hub and the Admin's real client in one process; the compose smoke script is unchanged.
+
+## Known gaps in 10b
+
+- The connection is per circuit (per open tab): two tabs are two connections. The browser tests that would prove "one connection per circuit" in a real browser do not exist (Playwright is deferred); it is proved by the client's start guard, the single starter and the prerender test.
+- After a reconnect both banners are raised even when nothing changed, because notifications are at most once and the gap cannot be told from a quiet minute.
+- A "replying" hint that is not refreshed within 10 seconds is shown as "viewing" by a timer in the page; the hub does not push the lapse.
+- The reconnect schedule has no jitter: after an Api restart every open tab retries on the same schedule.
+- A hub that ends because the session ended is not reported to the session: the indicator says "Offline" and the session-expired banner appears with the next call to the API.
+- The failed-emails badge in the navigation is still read when the shell loads and after an action; it is not driven by the hub.
+- Erasing a requester publishes nothing, so an open ticket keeps its old text until the next load (D-046, known limit).
+- A deactivated agent's already-open connection keeps receiving changes until its token expires (D-046, known limit).
+- Presence and the in-process publish are for one Api instance (D-007).
+- A live change that arrives while the page is reloading is not lost: the banner stays, but a reload that fails keeps the banner too.
```

`docs/self-hosting/DEPLOYMENT.md` (modify)

```diff
diff --git a/docs/self-hosting/DEPLOYMENT.md b/docs/self-hosting/DEPLOYMENT.md
--- a/docs/self-hosting/DEPLOYMENT.md
+++ b/docs/self-hosting/DEPLOYMENT.md
@@ -172,7 +172,7 @@ Agents see ticket changes and who else has a ticket open without reloading. Two
 
 - **The hub is internal.** The Admin connects to the Api's `/hubs/tickets` server to server (`API__BASEURL`, `http://api/` in the deploy compose). A browser never connects to the hub, so the reverse proxy needs no WebSocket rule or idle-timeout change for it; the Admin's own `/_blazor` circuit already needs upgrade support. Do not publish `/hubs` on the public host. The hub accepts an agent's token in the `Authorization` header only; a token in the URL is refused.
 - **`LISTEN` needs a direct Postgres connection.** The Api holds one long-lived connection that listens for the changes the Worker makes (auto-close) and reconnects by itself with a backoff. Point `ConnectionStrings__TechStrap` at Postgres itself, or at a pooler in session mode; **PgBouncer in transaction mode breaks `LISTEN`**. The connection shows in `pg_stat_activity` with the application name `techstrap-ticket-change-listener`. Presence and the in-process publish live in one Api process, so run one Api instance (D-007).
-- **No new settings.** Nothing in the `deploy/.env.*` files changes for live updates.
+- **No new settings for the Api or the Worker.** The Admin has one switch, `LIVEUPDATES__ENABLED` (default `true`, in `.env.admin`): `false` opens no hub connection and draws no indicator, banner or presence bar. Compose does not set it, so the operator's env file decides. The hub address is `API__BASEURL` plus `/hubs/tickets`; the backoff and the debounce are constants.
 
 ## Health checks and acceptance
```

`docs/architecture/PHASE-10-live-updates.md` (modify)

```diff
diff --git a/docs/architecture/PHASE-10-live-updates.md b/docs/architecture/PHASE-10-live-updates.md
--- a/docs/architecture/PHASE-10-live-updates.md
+++ b/docs/architecture/PHASE-10-live-updates.md
@@ -29,6 +29,7 @@ Where this page and D-046 differ, D-046 wins.
 - **Unknown ticket.** `JoinTicket` answers `HubException("Ticket not found")` after an existence check, and returns the current presence to its caller.
 - **Metrics.** The Api and the Worker pass the meter name to `AddSyntaxCircusObservability`.
 - **Detail banner (10b).** A "New activity - refresh" banner; the row version changes only when the agent clicks it. **Presence name (10b):** `Agent.Name`, to other agents only.
+- **Kill switch (10b addendum, 2026-10-07).** One setting, `LiveUpdates:Enabled` (`LIVEUPDATES__ENABLED`, default `true`), supersedes "no new setting" for the Admin only; off, a no-op client is used and nothing live is drawn. P10-T16's ".env.example keys" is this one key; the hub address is derived from `API__BASEURL`.
 
 ## Architecture Decisions
 
@@ -100,9 +101,9 @@ Versions in [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md). `Microsoft.AspNetCore.Signal
 - [x] `TicketHub`, `UpdateTicketPresenceHandler`, `ITicketPresenceStore`, `RelayTicketChangeHandler`, `ITicketChangeBroadcaster` (+ SignalR and pg_notify implementations), `TicketChangeListener` (10a, D-046).
 - [x] Post-commit publishing hook in Infrastructure used by API and Worker (10a, D-046: capture plus publishing interceptors).
 - [x] Contracts: `TicketChangedDto`, `TicketPresenceDto`, hub method/group constants, change-kind constants (10a; `LiveConnectionState` is an Admin type, 10b).
-- [ ] Admin `ITicketLiveClient`, live indicator, queue banner, detail refresh and presence bar.
-- [ ] Reverse-proxy/WebSocket notes added to the compose docs.
-- [ ] Unit, API (in-memory hub), integration (NOTIFY) and bUnit tests.
+- [x] Admin `ITicketLiveClient`, live indicator, queue banner, detail refresh and presence bar (10b, D-046).
+- [x] Reverse-proxy/WebSocket notes added to the compose docs (10a: DEPLOYMENT.md "Live updates (PHASE-10)"; 10b adds the kill switch).
+- [x] Unit, API (in-memory hub), integration (NOTIFY) and bUnit tests.
 
 ## Actionable Tasks
 
@@ -136,37 +137,44 @@ Versions in [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md). `Microsoft.AspNetCore.Signal
 - [x] **P10-T10** Add observability: connected-agents gauge, changes-relayed counter, relay failures counter, listener-reconnect counter
   - **Depends on:** P10-T09
   - **Validation:** Meter listener test asserts instruments update; names in constants.
-- [ ] **P10-T11** Implement admin `ITicketLiveClient` / `SignalRTicketLiveClient` (token provider from Blazor.Auth, automatic reconnect with backoff, event de-duplication, `IAsyncDisposable`)
+- [x] **P10-T11** Implement admin `ITicketLiveClient` / `SignalRTicketLiveClient` (token provider from Blazor.Auth, automatic reconnect with backoff, event de-duplication, `IAsyncDisposable`)
   - **Depends on:** P10-T05, P07-T02
   - **Validation:** Unit tests with a fake connection abstraction: reconnect state transitions raised; duplicate event ids ignored; disposal stops the connection and unsubscribes.
-- [ ] **P10-T12** Build `LiveConnectionIndicator` and place it in `MainLayout`
+  - **As built (10b):** `SignalRTicketLiveClient` over `ILiveConnection` (the unit tests use a scripted connection), scoped per circuit, started once by the indicator after its first render; token from `IUserAccessTokenProvider` (0.2.0) on every attempt, header only; a custom retry policy that never gives up (0, 2, 5, 10, then 30 s) and stops for a lapsed session or a missing token; re-joins its tickets and raises a synthetic `Resync` after a reconnect; last 256 event ids de-duplicated; `NullTicketLiveClient` behind `LiveUpdates:Enabled`.
+- [x] **P10-T12** Build `LiveConnectionIndicator` and place it in `MainLayout`
   - **Depends on:** P10-T11
   - **Validation:** bUnit: renders each state with appropriate text/`aria-live`; unsubscribes on dispose.
-- [ ] **P10-T13** Add queue live refresh: subscription in `TicketQueuePage`, `QueueLiveBanner`, debounced counter and explicit refresh
+  - **As built (10b):** `LiveConnectionIndicator` in `MainLayout` (outside `AgentGate`: it draws and starts only when the session is Ready); states Live, Connecting, Reconnecting, Offline in a polite status region; the one place the client starts.
+- [x] **P10-T13** Add queue live refresh: subscription in `TicketQueuePage`, `QueueLiveBanner`, debounced counter and explicit refresh
   - **Depends on:** P10-T11, P07-T07
   - **Validation:** bUnit with `FakeTimeProvider`: a burst of changes yields one banner update; accepting refreshes with current filters; rows not reordered before acceptance.
-- [ ] **P10-T14** Add detail live refresh: join/leave ticket group in `TicketDetailPage`, `ChangedTicketBanner`, reload timeline preserving composer draft and updating the concurrency token
+  - **As built (10b):** `QueueLiveBanner` ("Queue updated - refresh"): one banner per one-second window for any change by another agent or a resync, the agent's own changes ignored; the click is the existing load with the current filters; the debounce is a window, not a restarted timer, so a steady trickle still shows it.
+- [x] **P10-T14** Add detail live refresh: join/leave ticket group in `TicketDetailPage`, `ChangedTicketBanner`, reload timeline preserving composer draft and updating the concurrency token
   - **Depends on:** P10-T11, P07-T08, P07-T11
   - **Validation:** bUnit: change for the open ticket reloads timeline; draft text untouched; change for another ticket ignored; leaves the group on navigation/dispose.
-- [ ] **P10-T15** Build `TicketPresenceBar` + `PresenceViewModelFactory` and throttled `SetComposing` in `ReplyComposer`
+  - **As built (10b):** `ChangedTicketBanner` ("New activity - refresh"): the model, the row version and the draft stay as they are until the click (D-046), so a send before it still gets the 409; join after load, leave on navigation and disposal; a change that arrives while a reload runs keeps the banner.
+- [x] **P10-T15** Build `TicketPresenceBar` + `PresenceViewModelFactory` and throttled `SetComposing` in `ReplyComposer`
   - **Depends on:** P10-T14
   - **Validation:** Factory theory (viewing only, replying only, both, self excluded, ordering); bUnit with fake timers: typing sends at most one `SetComposing(true)` per throttle window and `false` on submit/blur/dispose.
-- [ ] **P10-T16** Document and verify reverse-proxy WebSocket requirements (upgrade headers, idle timeout, buffering) in compose docs; add the connection settings as `.env.example` keys
+  - **As built (10b):** `TicketPresenceBar` and `PresenceViewModelFactory` (self excluded by agent id, repliers first); the composer sends the hint from the `Text` setter at most every 4 seconds and clears it on blur, send, empty text, another ticket and disposal; a hint not refreshed within the server's 10-second lease is shown as "viewing".
+- [x] **P10-T16** Document and verify reverse-proxy WebSocket requirements (upgrade headers, idle timeout, buffering) in compose docs; add the connection settings as `.env.example` keys
   - **Depends on:** P10-T06
   - **Validation:** Manual UAT check: two browsers behind the proxy see presence and refresh within 2 s; connection survives a 5-minute idle period (keepalive configured).
-- [ ] **P10-T17** End-to-end verification script/test: worker auto-close of a Solved ticket updates an open admin queue without manual reload
+  - **As built (10b):** Superseded by D-046 for the proxy (browsers never reach `/hubs`); the runbook note of 10a stays, and the one setting is `LIVEUPDATES__ENABLED` (the four edits of D-043). The manual UAT is the checklist in ADMIN-APP.md ("Manual check against a real identity provider").
+- [x] **P10-T17** End-to-end verification script/test: worker auto-close of a Solved ticket updates an open admin queue without manual reload
   - **Depends on:** P10-T08, P10-T09, P10-T13
   - **Validation:** Compose-based test (or documented manual run with seed data): shorten auto-close to 1 minute, observe banner in admin; no duplicate notifications.
+  - **As built (10b):** `AdminLiveClientHostTests` in `TechStrap.Api.Tests` (the one test project that references the Api, the Worker and the Admin): the Worker's real auto-close handler closes a backdated Solved ticket in Postgres, NOTIFY reaches the Api's real listener and hub, and the Admin's real client raises exactly one change (a barrier message proves no second copy); the compose smoke script is unchanged and the compose-based run is the owner's manual check.
 
 ## Success Criteria
 
-- [ ] An agent opening a ticket sees other agents viewing it and, within about 2 seconds, sees "replying…" when another agent types; presence clears after leaving or the TTL.
-- [ ] A change made by one agent (reply, status, assignment) shows on another agent's open queue (banner) and open ticket (timeline refresh) without reloading; typed drafts are never lost.
-- [ ] A worker-originated change (auto-close) reaches admin clients through NOTIFY -> listener -> hub.
-- [ ] Losing the DB listener or the hub connection results in automatic recovery and a resync; the UI shows connection state.
-- [ ] Unauthenticated or non-agent users cannot connect or join; payloads contain no message content.
-- [ ] Rolled-back transactions never produce notifications; broadcast failure never fails a user request.
-- [ ] `dotnet build`/`dotnet test` green including Testcontainers integration tests; no new database migration required.
+- [x] An agent opening a ticket sees other agents viewing it and, within about 2 seconds, sees "replying…" when another agent types; presence clears after leaving or the TTL.
+- [x] A change made by one agent (reply, status, assignment) shows on another agent's open queue (banner) and open ticket (banner, then a click refreshes the timeline; D-046) without reloading the page; typed drafts are never lost.
+- [x] A worker-originated change (auto-close) reaches admin clients through NOTIFY -> listener -> hub.
+- [x] Losing the DB listener or the hub connection results in automatic recovery and a resync; the UI shows connection state.
+- [x] Unauthenticated or non-agent users cannot connect or join; payloads contain no message content.
+- [x] Rolled-back transactions never produce notifications; broadcast failure never fails a user request.
+- [x] `dotnet build`/`dotnet test` green including Testcontainers integration tests; no new database migration required.
 
 ## Boundary Validation
```

`docs/architecture/99-IMPLEMENTATION-ROADMAP.md` (modify)

```diff
diff --git a/docs/architecture/99-IMPLEMENTATION-ROADMAP.md b/docs/architecture/99-IMPLEMENTATION-ROADMAP.md
--- a/docs/architecture/99-IMPLEMENTATION-ROADMAP.md
+++ b/docs/architecture/99-IMPLEMENTATION-ROADMAP.md
@@ -26,7 +26,7 @@ Cross-cutting conventions every phase follows (fixed during the consistency revi
 | 07 | [Admin app](PHASE-07-admin-app.md) | 02, 06 | 08 (editor UI), 10 | 11 alongside; 08 API work alongside | D-017, D-022, D-040, D-041 | Complete: 07a merged (PR #9), 07b merged (PR #10), 07c merged (PR #11); owner action 7 (Authentik) still open, so P07-T02 stays unticked |
 | 08 | [Knowledge base](PHASE-08-knowledge-base.md) | 06 (07 for the editor UI) | 09 | API tasks T01 to T12 alongside 07; editor tasks wait for 07 | D-011, D-014, D-021, D-044 | PHASE-08 merged (PR #13): the API (Tasks 1-8) and the Admin (editor, categories, article picker); the owner's manual checks are open |
 | 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 10 and 11 alongside | D-002, D-017, D-019, D-045 | 09a merged (PR #14); 09b merged (PR #15); 09c merged (PR #16); 09d merged (PR #17); PHASE-09 complete: the owner evidence for P09-T16 (axe, Lighthouse, the JavaScript-off walk, screenshots) and the compose run of P09-T18 are open, P09-T20 is deferred |
-| 10 | [Live updates](PHASE-10-live-updates.md) | 07 | 12 | 08, 09, 11 alongside | D-007, D-018, D-046 | 10a complete (pending merge); 10b (Admin) ready to start after 10a merges, using SyntaxCircus.Blazor.Auth 0.2.0 |
+| 10 | [Live updates](PHASE-10-live-updates.md) | 07 | 12 | 08, 09, 11 alongside | D-007, D-018, D-046 | 10a merged (PR #18); 10b complete (pending merge); PHASE-10 complete (pending merge): the owner's manual checks with a real identity provider (two browsers, a worker auto-close, the kill switch) are open |
 | 11 | [Client SDK](PHASE-11-client-sdk.md) | 05 | 12 | Alongside 06 to 10 | D-005, D-020 | Not started |
 | 12 | [Release hardening](PHASE-12-release-hardening.md) | all | v1.0.0 | Last; security, load, restore and UAT tasks can overlap once their inputs exist | D-003, D-022 | Not started |
```

`docs/architecture/00-DISCOVERY-INDEX.md` (modify)

```diff
diff --git a/docs/architecture/00-DISCOVERY-INDEX.md b/docs/architecture/00-DISCOVERY-INDEX.md
--- a/docs/architecture/00-DISCOVERY-INDEX.md
+++ b/docs/architecture/00-DISCOVERY-INDEX.md
@@ -31,7 +31,7 @@
 | 07 | [Admin app](PHASE-07-admin-app.md) | 02, 06 | 08 (editor UI), 10 | Complete: 07a merged (PR #9), 07b merged (PR #10), 07c merged (PR #11); owner action 7 (Authentik) still open, so P07-T02 stays unticked |
 | 08 | [Knowledge base](PHASE-08-knowledge-base.md) | 06 (07 for the editor UI) | 09 | PHASE-08 merged (PR #13): the API (Tasks 1-8) and the Admin (editor, categories, article picker); the owner's manual checks are open |
 | 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 09a merged (PR #14); 09b merged (PR #15); 09c merged (PR #16); 09d merged (PR #17); PHASE-09 complete: the owner evidence for P09-T16 (axe, Lighthouse, the JavaScript-off walk, screenshots) and the compose run of P09-T18 are open, P09-T20 is deferred |
-| 10 | [Live updates](PHASE-10-live-updates.md) | 07 | 12 | 10a complete (pending merge); 10b (Admin) ready to start after 10a merges, using SyntaxCircus.Blazor.Auth 0.2.0 |
+| 10 | [Live updates](PHASE-10-live-updates.md) | 07 | 12 | 10a merged (PR #18); 10b complete (pending merge); PHASE-10 complete (pending merge): the owner's manual checks with a real identity provider (two browsers, a worker auto-close, the kill switch) are open |
 | 11 | [Client SDK](PHASE-11-client-sdk.md) | 05 | 12 | Not started |
 | 12 | [Release hardening](PHASE-12-release-hardening.md) | all | v1.0.0 | Not started |
```



- [ ] **Step 4: Run the pins (GREEN)**

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1`

Expected: PASS, `Tests Passed: 59, Failed: 0`.

- [ ] **Step 5: Record the mutations**

`$T\specs\m5.py`:

```python
DOCS = ["pwsh", "-NoProfile", "-File", "scripts/Invoke-ScriptTests.ps1", "-Output", "Minimal", "-Path", "scripts/tests/RepositoryDocs.Tests.ps1"]
PKG = ["pwsh", "-NoProfile", "-File", "scripts/Check-PackageVersions.ps1"]
SPEC = "docs/architecture/PHASE-10-live-updates.md"
LOG = "docs/architecture/04-DECISION-LOG.md"
ADMIN = "docs/development/ADMIN-APP.md"

MUTATIONS = [
    ("1 T14 is not ticked", SPEC, [("- [x] **P10-T14**", "- [ ] **P10-T14**")], DOCS),
    ("2 T17 has no as-built note", SPEC, [("  - **As built (10b):** `AdminLiveClientHostTests`", "  - **Built:** `AdminLiveClientHostTests`")], DOCS),
    ("3 a deliverable is open", SPEC, [("- [x] Unit, API (in-memory hub), integration (NOTIFY) and bUnit tests.", "- [ ] Unit, API (in-memory hub), integration (NOTIFY) and bUnit tests.")], DOCS),
    ("4 a success criterion is open", SPEC, [("- [x] Unauthenticated or non-agent users cannot connect", "- [ ] Unauthenticated or non-agent users cannot connect")], DOCS),
    ("5 the roadmap row still says 10a is pending", "docs/architecture/99-IMPLEMENTATION-ROADMAP.md", [("10a merged (PR #18); 10b complete (pending merge);", "10a complete (pending merge); 10b complete (pending merge);")], DOCS),
    ("6 the discovery row loses PHASE-10 complete", "docs/architecture/00-DISCOVERY-INDEX.md", [("PHASE-10 complete (pending merge): the owner", "10b done: the owner")], DOCS),
    ("7 the addendum becomes a new decision", LOG, [("### Addendum (2026-10-07, PHASE-10b live updates in the Admin)", "## D-047: PHASE-10b")], DOCS),
    ("8 the addendum forgets the kill switch ruling", LOG, [("**One kill switch.** `LiveUpdates:Enabled`", "**One kill switch.** `LiveUpdate`")], DOCS),
    ("9 the Admin guide loses its live section", ADMIN, [("## Live updates (10b)", "## Live (10b)")], DOCS),
    ("10 the Admin guide says live updates are coming", ADMIN, [("and a banner offers the same refresh when something changes", "live updates arrive with PHASE-10; and a banner offers the same refresh when something changes")], DOCS),
    ("11 the runbook forgets the switch", "docs/self-hosting/DEPLOYMENT.md", [("The Admin has one switch, `LIVEUPDATES__ENABLED`", "The Admin has one switch, `LIVEUPDATES`")], DOCS),
    ("12 the package map keeps 0.1.7", "docs/architecture/03-PACKAGE-MAP.md", [("| 0.2.0 | [0.2.0](https://www.nuget.org/packages/SyntaxCircus.Blazor.Auth/0.2.0) (dragon-poop 0.2.0)", "| 0.1.7 | [0.1.7](https://www.nuget.org/packages/SyntaxCircus.Blazor.Auth/0.1.7) (dragon-poop 0.1.7)")], DOCS),
    ("13 props and map disagree", "Directory.Packages.props", [('SyntaxCircus.Blazor.Auth" Version="0.2.0"', 'SyntaxCircus.Blazor.Auth" Version="0.1.7"')], PKG),
]
```

`git add -A`, then one batch in the foreground (about two minutes):

```bash
python $T/run_muts.py $T/specs/m5.py $T/res_m5.txt
```

Every mutation must be KILLED (the real run):

| # | Mutation | File | Result |
| --- | --- | --- | --- |
| 1 | T14 is not ticked | `docs/architecture/PHASE-10-live-updates.md` | KILLED |
| 2 | T17 has no as-built note | `docs/architecture/PHASE-10-live-updates.md` | KILLED |
| 3 | a deliverable is open | `docs/architecture/PHASE-10-live-updates.md` | KILLED |
| 4 | a success criterion is open | `docs/architecture/PHASE-10-live-updates.md` | KILLED |
| 5 | the roadmap row still says 10a is pending | `docs/architecture/99-IMPLEMENTATION-ROADMAP.md` | KILLED |
| 6 | the discovery row loses PHASE-10 complete | `docs/architecture/00-DISCOVERY-INDEX.md` | KILLED |
| 7 | the addendum becomes a new decision | `docs/architecture/04-DECISION-LOG.md` | KILLED |
| 8 | the addendum forgets the kill switch ruling | `docs/architecture/04-DECISION-LOG.md` | KILLED |
| 9 | the Admin guide loses its live section | `docs/development/ADMIN-APP.md` | KILLED |
| 10 | the Admin guide says live updates are coming | `docs/development/ADMIN-APP.md` | KILLED |
| 11 | the runbook forgets the switch | `docs/self-hosting/DEPLOYMENT.md` | KILLED |
| 12 | the package map keeps 0.1.7 | `docs/architecture/03-PACKAGE-MAP.md` | KILLED |
| 13 | props and map disagree | `Directory.Packages.props` | KILLED |

`git status` must then show nothing but staged files.

- [ ] **Step 6: Verify the whole branch**

```bash
dotnet build TechStrap.slnx -c Release                      # 0 Warning(s), 0 Error(s)
dotnet test --solution TechStrap.CI.slnf -c Release --no-build   # total: 7174, failed: 0 (about 50 seconds)
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1        # Tests Passed: 332, Failed: 0
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build   # No changes have been made to the model since the last migration.
docker ps                                                   # none of your Testcontainers left
```

Expected: as written in the comments (the real run of the proven branch). The suite count is the total of the CI solution filter: the Admin suite alone is 2061, the Architecture suite 295.

- [ ] **Step 7: Commit**

```bash
git add -A
git diff --cached --stat
git commit -F - <<'MSG'
docs: PHASE-10b close-out (D-046 addendum, Live updates in ADMIN-APP, T11-T17 ticked, rows and pins)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
MSG
```

Then the whole-branch review, one fix pass and the pull request with the owner's OK (the pull request body ends with the Claude Code line and the session link).

---

## Self-review (done while drafting)

**Spec coverage (T11 to T17 and the Corrections block).**
- **T11** `ITicketLiveClient` / `SignalRTicketLiveClient`: Task 1 (token provider, reconnect with backoff, de-duplication, `IAsyncDisposable`; the validation "unit tests with a fake connection abstraction" is `SignalRTicketLiveClientTests` over `FakeLiveConnection`).
- **T12** `LiveConnectionIndicator` in `MainLayout`: Task 2 (each state with its text and `aria-live`; unsubscribes on dispose).
- **T13** queue live refresh: Task 2 (a burst gives one banner and one timer; the click refreshes with the current filters; rows are not reordered before it).
- **T14** detail live refresh: Task 3 (a change for the open ticket raises the banner and, after the click, reloads the timeline and takes the new row version; the draft is untouched; a change for another ticket is ignored; leaves the group on navigation and disposal). The spec text said "reloading ... updating the concurrency token" at once; D-046's owner ruling (the click) wins and is what is built.
- **T15** `TicketPresenceBar`, `PresenceViewModelFactory`, the throttled `SetComposing`: Task 3 (the factory theory-style tests: viewing only, replying only, both, self excluded, ordering; the composer's tests with fake time).
- **T16** proxy notes and settings: superseded by D-046 for the proxy (the 10a runbook note stands); the one setting is the kill switch (Task 1, Task 5 docs); the manual UAT is the checklist in ADMIN-APP.md.
- **T17** the end-to-end verification: Task 4 (automated, in process) plus the owner's manual checklist (Task 5).
- **Corrections block:** `LiveConnectionState` is an Admin enum in `Features/Live`; no browser hops (the hub address is `API__BASEURL` plus `TicketHubRoutes.Path`); the token is in the header only (`AccessTokenProvider`); `EventId` de-duplication; `TicketChanged` is filtered by ticket id on the client; `JoinTicket` for an unknown ticket is caught; the detail banner and the presence name are as ruled.

**Placeholder scan.** No "TBD", no "similar to Task N", no step without its code: every new file is quoted whole and every edit to an existing file is a diff, both taken from the commits that were built and tested.

**Type consistency.** `ITicketLiveClient` (Task 1) is used by the indicator and the queue page (Task 2) and by the detail page and the composer (Task 3) with the same member names; `LiveChangeRules.IsOwn/Concerns/IsResync/NewResync` (Task 1) are the only own-change and ticket filters (Tasks 2 and 3); `LiveCopy.QueueUpdated` and `NewActivity` (Task 2) are used by the two banners; `LiveDefaults.QueueBannerWindow` (Task 2) and `ComposingThrottle` (declared in Task 2, used in Task 3); `PresenceViewModelFactory.Create(presence, selfAgentId, composingExpired)` (Task 3) is used by the detail page only; `HubLiveConnectionFactory(IOptions<ApiOptions>, Action<HttpConnectionOptions>?)` (Task 1) is how Task 4 builds the real client.

**Review Focus.** Each of the five lines names the tests that pin it, in the owning task, above.

## What was run to prove this plan (in a scratch clone of the repository, never in the real tree)

| Step | Command | Result |
| --- | --- | --- |
| Restore | `dotnet restore src/TechStrap.Admin` | `SyntaxCircus.Blazor.Auth` 0.2.0 fetched from nuget.org (the local cache has 0.1.4 to 0.1.7) |
| Task 1 | `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter-namespace TechStrap.Admin.Tests.Live` | RED: compile errors (counted above); GREEN: 32 passed; whole Admin suite 1994 passed; Architecture 295; ConfigContract 87 (RED 84 passed, 3 failed); `Check-PackageVersions` passed; 22 of 22 mutants killed |
| Task 2 | same filter | RED: `CS0103 LiveCopy`; GREEN: 62 passed; whole Admin suite 2024; Architecture 295; 19 of 19 mutants killed (one survived the first draft, then killed) |
| Task 3 | same filter | RED: `CS0103 PresenceViewModelFactory`, `PresenceCopy`; GREEN: 99 passed; whole Admin suite 2061; Architecture 295; 28 of 28 mutants killed (two survived the first draft, then killed) |
| Task 4 | `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-class "*AdminLiveClientHostTests"` | RED by mutation (the Worker's NOTIFY removed: 1 failed, 3 passed); GREEN: 4 passed in about 22 seconds against a Testcontainers Postgres; the Api's Live namespace 31 passed; 7 of 7 mutants killed |
| Task 5 | `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1` | RED: 55 passed, 4 failed; GREEN: 59 passed; 13 of 13 mutants killed |
| Whole branch | `dotnet build TechStrap.slnx -c Release` | 0 warnings, 0 errors |
| Whole branch | `dotnet test --solution TechStrap.CI.slnf -c Release --no-build` | 7174 passed, 0 failed |
| Whole branch | `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1` | 332 passed, 0 failed |
| Whole branch | `dotnet ef migrations has-pending-model-changes ...` | No changes have been made to the model since the last migration. |

The five commits of the scratch clone (`1022ac2`, `20f78bd`, `53cc1b3`, `009c95c`, `df064d3`, on a branch cut from `ecf07f1`) are the code and the docs this plan quotes.
