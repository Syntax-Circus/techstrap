# PHASE-09d Portal Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish PHASE-09 on top of the 09a foundation, the 09b customer flows and the 09c help center.
- **Double send:** a one-time id per rendered form and a process-local guard make a double click on the contact, reply or lost-link form send once.
- **Form helpers:** one small module for the "Sending" state, a copy button for the ticket number and a character counter, loaded once from the document shell.
- **T16:** the layout, the accessibility pass (skip link, landmarks, one `h1`, targets, forced colors, reduced motion) and the fix for the `<base href="/">` bug in the error summary, with the tests that pin them.
- **Test hardening:** one shared start-failure helper for the Admin, Api and Portal tests, a `Seen` that compares every header, and the corrected D-045 wording.
- **Close-out:** the developer guide, the PHASE-09 ticks, the roadmap rows, the D-045 as-built notes and the owner's manual checklist.

**Architecture:**
- **One guard, three forms.** `FormGuard` renders a fresh 128-bit `SubmitId` on every render; the handler claims it with `SubmitGuard` (its own capped `MemoryCache`, an atomic claim, a 2-minute life) after validation and before the API call, and the write runs on its own token with a timeout. A repeat never writes: it reads the first answer, waits for it, or is sent to a safe fallback. A failure releases the claim.
- **Modules that survive enhanced navigation.** Blazor's enhanced navigation does not run a script that arrives with swapped content, so both modules (`kb-suggestions.js`, whose 09b placement was broken by it, and the new `portal-forms.js`) load from `App.razor`; the new one uses custom elements and document listeners and keeps its state outside the markup.
- **Links to the current page are written in full.** `PageLinks.ToFragment` builds a root-relative path plus the fragment (the document's `base` is `/` and a bare `#email` is the home page) and the links opt out of enhanced navigation so the browser moves the focus to the field.
- **Styles from tokens, tests from the compiled CSS.** Two widths in two tokens, six semantic tokens in BRAND.md, two new partials, and a Portal `ResponsiveStyleTests` that also scans the markup for a `ts-*` class without a rule.

**Tech Stack:** .NET 10, ASP.NET Core Blazor static SSR, `IMemoryCache` (`MemoryCache` with a size limit, no new package), vanilla JavaScript with `node --test`, SCSS over Bootstrap 5.3, EF Core on Postgres (unchanged), bUnit and AngleSharp (which bUnit brings), xUnit v3, Shouldly, NSubstitute and Pester.

**Spec:** `docs/architecture/PHASE-09-public-portal.md` (T09, T16 and the Success Criteria), `docs/architecture/UX-BRIEF-portal.md` (the checklist and acceptance criteria), `BRAND.md` (`docs/BRAND.md`), `docs/development/PORTAL-APP.md`, `docs/architecture/04-DECISION-LOG.md` (D-045 and its 09b and 09c addenda) and the owner rulings of 2026-10-06 (the 09d section of the scope plan), recorded as a dated "09d addendum" inside D-045 in Task 1 (no new decision number). This plan covers **09d only**; it is the last PHASE-09 pull request.

### Owner rulings (2026-10-06), recorded as the D-045 09d addendum
1. **Double-send guard (T09).** A fresh 128-bit `SubmitId` per render in a hidden input (the `FormGuard` component); the handler claims it only after validation and before the API call with `SubmitGuard`: its own `MemoryCache` with a size cap, an atomic claim under a lock, 2 minutes. A duplicate waits for the running first request or reads its stored redirect target (memory only, never logged; for a follow-up the target holds the new ticket's token, which is accepted). The claimed write runs on its own token with a timeout, not on `RequestAborted`. If the first answer is unknown the duplicate goes to the fallback (a reply: the same ticket page; contact: the received page with no reference; lost link: the sent page) and never sends again. A 429, 409 or 503 releases the claim. A missing or malformed id means unguarded. Per instance, lost on restart (documented).
2. **`portal-forms.js`.** Custom elements and delegated listeners, loaded once from the layout; the "Sending..." state, the copy button and the character counter; every word from a `*Copy` constant through a data attribute, `textContent` only, ASCII only; node tests through `PortalScripts.Tests.ps1`; `kb-suggestions` fixed if the spike shows enhanced navigation breaks it.
3. **T16.** One reading-width token and a wide container; a rule for every class used in the markup; the error, success and warning colors from brand tokens; targets of at least 44px; reduced motion and forced colors keeping the 3px focus outline; a skip link, a `nav` landmark and one `h1` per page with a distinct search `h1`; the `<base href>` fragment fix, also for the skip link; the duplicate `ProductHome` search box merged into `KbSearchBox`; `ResponsiveStyleTests`, `CspStyleTests`, contrast tests and a `HeadingHostTests` in the Admin's pattern; the axe run, Lighthouse of at least 90, the JavaScript-off walk and screenshots at three widths are the owner's manual checklist.
4. **Test hardening.** `StartupFailure` generic over `WebApplicationFactory<T>` in `tests/Shared`, taking a log-events function; the Admin factory gains a `CollectingSink`; the Api tests link the shared file; the twelve start-failure sites (three Admin, seven Api, two Portal) use it; `Seen` compares every header except the per-request ones.
5. **Docs.** The D-045 "re-executes the post" wording is corrected; PORTAL-APP.md, the PHASE-09 ticks (T09; T16 only when the owner's evidence is in; T18 the owner's; T20 deferred), the roadmap and discovery rows ("PHASE-09 complete (pending merge)"), the success criteria and the as-built notes.

### Decisions made while drafting (the D-045 addendum records them)
- **Task split.** The five tasks of the brief, each a RED step, a GREEN step, a recorded mutation run and a commit. Task 1 has no `src/` change at all (the helpers are test code); Task 2 is the only one that adds a service; Task 3 adds the module; Task 4 is the biggest (the styles, the links and the landmarks, with the tests that pin them).
- **The spike, proved in a scratch clone (a test host, and Edge headless over the DevTools protocol against the real Portal with a stand-in API) before Tasks 2 to 4 were written.** Each finding is now a durable test or a recorded manual check.
  - **(a) Concurrency.** Eight concurrent posts with one id make exactly one API call and all go to the same place; two concurrent posts of each form through the host do too (`SubmitGuardTests`, `DoubleSendHostTests`).
  - **(b) An aborted first post.** A client that cancels its call gets no answer from the in-memory test server until the server's handler has finished, so the first request has to keep waiting for its write (a precaution, not reproduced: it also keeps valid what the request is still reading, the uploaded files). The write's own token is not canceled when the browser goes away (`CancelledWhenAnswered` is false), it completes, and the duplicate gets the same redirect, whether it arrived while the write ran or after. A write that times out is unknown: the claim is kept, the duplicate goes to the fallback and the first request shows the calm notice with a 503.
  - **(c) Enhanced navigation.** In Edge, after an enhanced click from the product home to the contact page, `customElements.get('ts-kb-suggestions')` was **undefined**: a `<script type="module">` that arrives in swapped content is not run, so the 09b suggestions never worked for a visitor who arrived by a click. A module loaded once from `App.razor` ran once, and the browser called `connectedCallback` for the elements the swap inserted (the connect count rose on each navigation, and the suggestions then appeared). The swap also rewrote an attribute that a script had set on an element it kept, so the module keeps its state in a `WeakMap`. A listener on the document survived every navigation. Both modules therefore load from the shell, and the contact page no longer carries a script.
  - **(d) The `<base href="/">` fragment bug.** In Edge a click on an error-summary link went to `/#name` and Blazor then showed the home page ("Support"). Removing the base tag is not safe (the stylesheet, the favicon and `blazor.web.js` are relative to it and Blazor reads it for navigation), so the links become the current root-relative path plus the fragment (`PageLinks.ToFragment`): the document stays the same. Blazor still took the click (`defaultPrevented` true), scrolled and left the focus on the link; with `data-enhance-nav="false"` the browser moved the focus to the field (`INPUT#email`), with a real mouse click and with Enter. The skip link (also with the prefill kept, `?subject=Printer jam`) and the "jump to your reply" link work the same way.
  - **(e) The start-failure helper.** The Admin factory had no sink. With a `CollectingSink` registered as an `ILogEventSink`, the Admin, Api, Worker and Portal factories each put a failed start on the sink as a "Hosting failed to start" event that carries the `OptionsValidationException`, so the fallback has something to read (`StartFailureLogTests`); and `Serilog` reaches the Admin tests transitively. A failure thrown eagerly while the host is built (the Portal's sitemap mapping reads `IOptions<PortalOptions>` in `Program.cs`) throws the validation exception directly and logs nothing, so it never races.
- **Deviations from the brief, each with its reason.**
  - **`Seen` also ignores `Pragma` and `Set-Cookie`.** Widening it to every header failed two tests on `Pragma: no-cache`, which the antiforgery step adds to a response that rendered a form (a post to a product that vanished after its form was served). The visitor holding that form already knew the product; nothing leaks. The brief listed `Set-Cookie`; `Pragma` is the one found by the run.
  - **The skip link is on product pages only.** It is built from the address, and the neutral 404 must stay byte for byte the same whatever the address was (the first run of the new layout failed 50 tests on exactly that). A neutral page has no header to skip anyway.
  - **The skip link keeps only the query parameters the page reads.** Keeping all of them echoed a visitor's unknown parameters into the contact page (a 09b test, "never echoed") and would have stored the first visitor's parameters in the output cache's copy for the next. `PageLinks.KnownParameters` lists them per page.
  - **An `h1` in an author's body is shown as an `h2`.** The sanitizer allows `h1` in articles and messages, so "one `h1` per page" is only true if the Portal demotes it (`BodyHeadings`, the one change it makes to those bodies; the 09c "byte for byte" pin is unchanged for a body without an `h1`).
  - **`kb-suggestions.js` is fixed**, because the spike showed it is broken; the brief said "only if".
  - **The first request waits for its own write without its abort token**, not "waits unless aborted", for the reason in (b).
  - **Required is said in one sentence above each form**, not in each label, so the pinned label markup (`LostLinkHostTests`) does not change.
  - **Six semantic tokens are added to BRAND.md** (the same red, green and amber as the Admin's `--st-spam`, `--st-open` and `--st-pending`, and three grounds), because the old `var(--p-error, #b3261e)` pointed at a token that did not exist; the Admin's style-guide palette and its token-count test follow.
  - **A control's edge is `--p-ink2`**, because `--p-line` is 1.5:1 against the page and a control's edge must be 3:1 (WCAG 1.4.11).
  - **T16 stays unticked in PHASE-09** ("Owner evidence pending"); T09 is ticked; T18 stays the owner's; T20 stays deferred. The product home still has no category list (the header's help-center link is the way in); the category page still has no description.
  - **Not converted to `StartupFailure`** (the brief asked for the twelve): the four `ConfigHostSupport` hosts, `ApiPublicUrlTests`, `TrustedProxyStartupTests` (their failures do not come from `ValidateOnStart`, or do not race) and the Infrastructure tests (they resolve `IOptions<T>` and start no host).
- **Honest limits.** The re-execution claim of D-045 is pinned by a test that counts the dependency scopes of the post (it passes before the doc fix, because the claim was only ever wrong in the text). Nothing proves the guard across two Portal replicas (it is per instance, documented). The Blazor DOM diff can rewrite a child that a script added to an element the swap keeps; the elements rebuild in `connectedCallback`, and item 7 of the owner's checklist is the browser check.
- **A mutation-run hazard.** A guard mutation (the write on the request's token, no timeout) makes a test wait for a gate that never opens. Every test of `SubmitGuardTests` and `DoubleSendHostTests` that waits therefore carries `Timeout = 10000` and reads `TestContext.Current.CancellationToken` in its own body (analyzer xUnit1069), so a mutant fails in seconds instead of hanging a run. `mut.py` reports a mutation that does not compile as "NOT A MUTATION" (exit code 4).
- **One known flake.** `KbPlainTextTests.A_greater_than_sign_inside_an_attribute_value...` (09c) failed once in the dozens of full Portal runs of this plan's proof, on a busy machine: its patterns have a 100 ms match timeout. It is not touched here; run the Portal suite again before suspecting a change.
- **No new package, no new configuration key and no migration.** `MemoryCache` is in the shared framework; Bootstrap and the fonts are unchanged; `dotnet ef migrations has-pending-model-changes` is clean (Task 5, last step).
- **Shared files between tasks** (the tasks run in order, so the overlaps are safe): `Contact.razor`, `Ticket.razor`, `LostLink.razor` (Tasks 2, 3 and 4), `ContactCopy.cs` (Tasks 3 and 4), `LostLinkForm.cs` (Tasks 2 and 4), `ContactPostHostTests.cs` (Tasks 1, 3 and 4), `ContactReceivedHostTests.cs` (Tasks 3 and 4), `TicketReplyHostTests.cs` (Tasks 2 and 4), `04-DECISION-LOG.md` and `RepositoryDocs.Tests.ps1` (Tasks 1 and 5).

## Global Constraints

- **Build.** .NET SDK 10.0.401 targeting `net10.0`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild`. Private fields are `_camelCase`, constants are PascalCase, namespaces are file-scoped.
- **Packages.** No new package in any project.
- **Project references.** The Portal references exactly Contracts and Hosting. Hosting stays a leaf.
- **Config (D-043).** 09d adds no configuration key. A key added later gets four edits: the host's `appsettings.json`, `src/TechStrap.Portal/.env.example`, `deploy/.env.portal.example` and compose where compose owns it; `scripts/tests/ConfigContract.Tests.ps1` must pass.
- **Portal conventions.**
  - Static SSR only, with no interactive render mode.
  - Components never inject `HttpClient`; only `Clients/` mentions it.
  - Copy lives in `*Copy` constants; no inline script or style; no `on*` attribute.
  - **No new `MarkupString` site**: the sites are exactly `CustomerMessageBody` and `KbArticleBody` and the word must not appear in any other Portal file, comments included.
  - Every route is a `PortalRoutes` constant or builder; no `/p...` or `/t...` literal outside `PortalRoutes`.
  - A link to a place on the current page is written by `PageLinks.ToFragment`, never as a bare `#fragment`.
  - A new `ts-*` class used in markup needs a rule (`ResponsiveStyleTests` fails otherwise).
- **Encoding.** Write non-ASCII as `\u` escapes, using Python or .NET, never GNU sed. The Write tool decodes `\uXXXX`, so write a file that needs an escape with a Python script (`chr(92) + "u2026"`) and grep afterwards (`grep -nP "[^\x00-\x7F]"` over the changed `.cs`, `.razor`, `.js`, `.mjs` and `.scss` files). The sending words are `"Sending\u2026"` in C#; the JavaScript source stays ASCII (a node test checks it).
- **Secrets and PII.** Never log tokens, emails, names, subjects, references, ids or search text. `SubmitGuard` takes no logger and holds a hash of the key, never a token; a follow-up's token is in a stored target for two minutes, in memory only.
- **Tests.**
  - Failing test first, with RED and GREEN recorded.
  - Prove each pin with a recorded mutation; every mutation must keep the Release build compiling (avoid `if (false)`: CS0162; do not leave a parameter unused: xUnit1026).
  - A test that waits carries `Timeout = 10000` and reads `TestContext.Current.CancellationToken` in its own body (xUnit1069).
  - Every API-calling host test uses `FormTestKit.Factory` or `TicketTestKit.Factory`, whose factory asserts the visitor's address on every call when it is disposed.
  - Run a `--no-incremental` rebuild before the final runs, because mutation runs can leave stale DLLs.
  - No background mutation runs: every mutation runs in the foreground (a few batches of a dozen, each under ten minutes) and the file is restored before the next.
  - A browser check uses a scratch stand-in API and Edge headless started from a script outside the repository, and every process it starts is stopped before the task is finished.
- **Commits.** Use Conventional Commits. End each one with exactly these two lines, on separate lines:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- **Forbidden.** `git add -f`, committing `.superpowers/`, `docker compose down -v`, killing processes you did not start, and committing without first running `git diff --cached --stat`.
- **Verification.**
  - `dotnet build TechStrap.slnx -c Release`: 0 warnings.
  - `dotnet test --solution TechStrap.CI.slnf -c Release`: passes.
  - `pwsh -File scripts/Invoke-ScriptTests.ps1`: passes (node tests included when node is installed).
  - `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build`: no changes.
- **Branch.** Work on `feat/phase-09d-portal-polish` (cut from main at 3c4b1a3, which includes 09a, #14, 09b, #15, and 09c, #16). Never edit or commit on `main`.

## Review Focus

1. **Double sends.** No path sends twice for one `SubmitId`, including concurrent posts and an aborted first post; a failure lets a retry through; an unknown answer never sends again.
   - Pinned in Task 2 (`SubmitGuardTests`, `DoubleSendHostTests`) and, as the visible half, Task 3 (`portal-forms.test.mjs`).
2. **Guard secrecy and abuse.** A follow-up's token and a reference are memory-only and never logged; the cache is capped and separate from the sitemap's; an id cannot cross forms, products or tickets; a full cache never refuses a post.
   - Pinned in Task 2 (`SubmitGuardTests`: the key, the cap, the constructor; `DoubleSendHostTests`: the log scan at every level, the cross-form, cross-product and cross-ticket tests).
3. **XSS and CSP in the new JavaScript and markup.** Only `textContent` and data-attribute copy; no inline script, no `on*` attribute, the policy unchanged; a hostile `h1` in an author's body is an `h2`, nothing else is touched.
   - Pinned in Task 3 (`portal-forms.test.mjs` "the source", `PortalFormsHostTests`) and Task 4 (`CspStyleTests`, `BodyHeadingsTests`, `HeadingHostTests`).
4. **Accessibility correctness.** The error summary's links and the skip link work under every path (resolved against the base the way a browser does) and move the focus; one `h1` per page; focus stays visible in forced colors; a state is never color alone; every link a visitor has to hit is 44px.
   - Pinned in Task 4 (`ErrorSummaryLinkHostTests`, `LayoutLandmarkHostTests`, `HeadingHostTests`, `ResponsiveStyleTests`, `TokenContrastTests`, `PageLinksTests`), and the browser checks in Task 5's owner checklist.
5. **Test hardening stays strict.** The shared helper still fails when startup does not fail on validation; no test is weakened by the conversions or by the `Seen` widening; the neutral 404 stays byte for byte the same.
   - Pinned in Task 1 (`StartupFailureTests`, `StartFailureLogTests`, `SeenTests`) and Task 4 (`LayoutLandmarkHostTests`: a neutral page has no skip link).

---

### Task 1: Test hardening: the shared start-failure helper, the `Seen` comparison and the D-045 wording

**Review Focus pin:** 5 (the helper still fails when a start does not fail on validation; every site keeps its original assertion about the key or the message; `Seen` compares every header but the per-request ones and the neutral 404 stays byte for byte the same). This task also records the owner's rulings and the spike's findings in D-045 and fixes the "re-executes the post" wording. It changes test code and docs only: no `src/` file.

**Files:**

- Create: `tests/Shared/StartupFailure.cs` (moved from `tests/TechStrap.Portal.Tests/StartupFailure.cs`)
- Create: `tests/Shared/CollectingSink.cs`
- Modify: `tests/TechStrap.Admin.Tests/AdminFactory.cs`
- Modify: `tests/TechStrap.Api.Tests/HostFactory.cs`
- Modify: `tests/TechStrap.Api.Tests/TechStrap.Api.Tests.csproj`
- Modify: `tests/TechStrap.Portal.Tests/PortalFactory.cs`
- Modify: `tests/TechStrap.Portal.Tests/Tickets/Seen.cs`
- Modify (the twelve sites): `tests/TechStrap.Admin.Tests/AdminStartupSettingsTests.cs`, `tests/TechStrap.Admin.Tests/Options/PortalUrlOptionsTests.cs`, `tests/TechStrap.Api.Tests/Auth/AgentAccessOptionsTests.cs`, `tests/TechStrap.Api.Tests/Customer/CustomerRateLimitTests.cs`, `tests/TechStrap.Api.Tests/HostHealthSmokeTests.cs`, `tests/TechStrap.Api.Tests/Intake/IntakeRateLimitOptionsTests.cs`, `tests/TechStrap.Api.Tests/PublicRateLimitOptionsTests.cs`, `tests/TechStrap.Portal.Tests/Products/RootPageHostTests.cs`, `tests/TechStrap.Portal.Tests/Settings/PortalOptionsHostTests.cs`
- Modify (the two existing users and the files that name `CollectingSink`): `tests/TechStrap.Portal.Tests/PortalHostTests.cs`, `tests/TechStrap.Portal.Tests/PoweredByHostTests.cs`, `tests/TechStrap.Portal.Tests/Clients/TicketTokenTests.cs`, `tests/TechStrap.Api.Tests/AdminLeakTests.cs`, `tests/TechStrap.Api.Tests/Hosting/FactoryClientLeakTests.cs`, `tests/TechStrap.Api.Tests/Hosting/OtlpLeakTests.cs`
- Modify: `docs/architecture/04-DECISION-LOG.md`
- Test (create): `tests/TechStrap.Admin.Tests/StartFailureLogTests.cs`
- Test (create): `tests/TechStrap.Api.Tests/Hosting/StartFailureLogTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Tickets/SeenTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/StartupFailureTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Forms/ContactPostHostTests.cs`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`

**Interfaces:**
- Consumes: the Portal's current `StartupFailure` (`Capture(Action start, Func<IReadOnlyCollection<LogEvent>> events, TimeSpan logWait)`, which already returns the `OptionsValidationException` or fails), `PortalFactory.LogSink` and the Api's `HostFactory<TProgram>.LogSink`, `Seen`, `NeutralPagesGuardTests`' header exclusion list (`Date`, `X-Correlation-Id`, `Content-Length`), `ProductScope`.
- Produces:
  - `tests/Shared/StartupFailure.cs`: `internal static class StartupFailure` in `TechStrap.Tests.Shared` with `OptionsValidationException Capture<TEntry>(WebApplicationFactory<TEntry> factory, Func<IReadOnlyCollection<LogEvent>> events) where TEntry : class` (starts the host; returns the `OptionsValidationException` that stopped it, read from the log when `CreateClient()` throws an `ObjectDisposedException`; fails with an `InvalidOperationException` when the host starts or the evidence is not a validation failure) and `internal OptionsValidationException Capture(Action start, Func<IReadOnlyCollection<LogEvent>> events, TimeSpan logWait)`.
  - `tests/Shared/CollectingSink.cs`: `public sealed class CollectingSink : ILogEventSink` with `IReadOnlyCollection<LogEvent> Events` (the Api's and the Portal's copies are removed; it stays public because `HostFactory<TProgram>.LogSink` is public).
  - `AdminFactory.LogSink` (a `CollectingSink`, registered as an `ILogEventSink`) in `TechStrap.Admin.Tests`; `TechStrap.Api.Tests.csproj` links the two shared files.
  - `Seen` (Portal tests) compares every response header except `Date`, `X-Correlation-Id`, `Content-Length`, `Transfer-Encoding`, `Set-Cookie` and `Pragma`.
  - The D-045 09d addendum (rulings, spike findings, deviations) and the corrected 09b wording, with the Pester pins.

- [ ] **Step 1: Keep the helper tools outside the repository**

Every mutation step of this plan uses two small tools: `mut.py` applies one or more replacements to a file, runs a command, reports KILLED, SURVIVED or NOT A MUTATION (the mutated code does not compile) and always puts the file back; `run_muts.py` runs a list of mutations (a spec file) in the foreground, one at a time, and appends one line per mutation to a results file. Keep them OUTSIDE the repository, for example in `C:\tmp\p09d-tools\` (the plan calls that folder `$T`; the specs go in `$T\specs\`). They are the 09c tools unchanged.

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

Run every mutation from the repository root, after `git add -A`-ing the task's files, so `git checkout -- <file>` also restores a file if a run is interrupted. A mutation that SURVIVES is a failed task unless this plan lists it as an honest survivor; a NOT-COMPILING row is a bad mutation: pick another. In the spec files, `PORTAL`, `ADMIN` and `API` are `dotnet test --project tests/TechStrap.<App>.Tests -c Release --filter-query` and `PESTER` is `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1`. Example, from the repository root:

```bash
python $T/run_muts.py $T/specs/m1.py $T/res_m1.txt 1 2 3 4 5 6
```

- [ ] **Step 2: Write the failing `Seen` test**

`Seen` is the yardstick of every "uniform 404" test, so it is pinned itself. The test builds responses by hand: a header on no list must make two responses differ, a per-request header must not, and the two things that matter (the content type and the enhanced-navigation marker) are still compared.

`tests/TechStrap.Portal.Tests/Tickets/SeenTests.cs` (new)

```csharp
using System.Net;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// <see cref="Seen"/> is the yardstick of every "uniform 404" test, so it is pinned itself: it compares every header (an allow-list would let a new header slip through unseen) and ignores only the ones that
/// differ per request.
/// </summary>
public sealed class SeenTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<Seen> SeenAsync(Action<HttpResponseMessage> configure)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<h1>Page not found</h1>") };
        response.Headers.TryAddWithoutValidation("Cache-Control", "no-store");
        configure(response);
        return await Seen.OfAsync(response, 0, Ct);
    }

    [Theory]
    [InlineData("X-Product", "paperplane")]
    [InlineData("Link", "</p/paperplane>; rel=canonical")]
    [InlineData("Vary", "Accept-Encoding")]
    [InlineData("Strict-Transport-Security", "max-age=31536000")]
    [InlineData("Permissions-Policy", "camera=()")]
    public async Task A_header_that_is_not_on_any_list_makes_two_responses_differ(string name, string value)
    {
        var plain = await SeenAsync(_ => { });
        var extra = await SeenAsync(response => response.Headers.TryAddWithoutValidation(name, value));

        extra.Headers.ShouldNotBe(plain.Headers);
        Should.Throw<ShouldAssertException>(() => extra.ShouldBeTheNeutralNotFound(plain));
    }

    [Theory]
    [InlineData("Date", "Tue, 06 Oct 2026 10:00:00 GMT", "Tue, 06 Oct 2026 10:00:09 GMT")]
    [InlineData("X-Correlation-Id", "cid-one", "cid-two")]
    [InlineData("Set-Cookie", ".AspNetCore.Antiforgery.x=one; path=/", ".AspNetCore.Antiforgery.x=two; path=/")]
    [InlineData("Pragma", "no-cache", "no-cache, x")]
    public async Task A_header_that_varies_per_request_does_not(string name, string first, string second)
    {
        var one = await SeenAsync(response => response.Headers.TryAddWithoutValidation(name, first));
        var two = await SeenAsync(response => response.Headers.TryAddWithoutValidation(name, second));

        one.Headers.ShouldBe(two.Headers);
    }

    [Fact]
    public async Task The_framing_headers_do_not_count()
    {
        var plain = await SeenAsync(_ => { });
        var framed = await SeenAsync(response =>
        {
            response.Headers.TransferEncodingChunked = true;
            response.Content.Headers.ContentLength = 23;
        });

        framed.Headers.ShouldBe(plain.Headers);
    }

    [Fact]
    public async Task The_content_type_and_the_enhanced_navigation_marker_are_still_compared()
    {
        var plain = await SeenAsync(_ => { });
        var typed = await SeenAsync(response => response.Content.Headers.ContentType = new("text/plain"));
        var marked = await SeenAsync(response => response.Headers.TryAddWithoutValidation("blazor-enhanced-nav", "allow"));

        typed.Headers.ShouldNotBe(plain.Headers);
        marked.Headers.ShouldNotBe(plain.Headers);
        marked.HeadersWithoutEnhancedNav.ShouldBe(plain.HeadersWithoutEnhancedNav);
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

```bash
dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/SeenTests/*"
```

Expected: FAIL, because `Seen` still compares a hand-written allow-list (the five cases of a header on no list):

```text
failed TechStrap.Portal.Tests.Tickets.SeenTests.A_header_that_is_not_on_any_list_makes_two_responses_differ(name: "Permissions-Policy", value: "camera=()") (33ms)
failed TechStrap.Portal.Tests.Tickets.SeenTests.A_header_that_is_not_on_any_list_makes_two_responses_differ(name: "X-Product", value: "paperplane") (1ms)
failed TechStrap.Portal.Tests.Tickets.SeenTests.A_header_that_is_not_on_any_list_makes_two_responses_differ(name: "Link", value: "</p/paperplane>; rel=canonical") (0ms)
failed TechStrap.Portal.Tests.Tickets.SeenTests.A_header_that_is_not_on_any_list_makes_two_responses_differ(name: "Vary", value: "Accept-Encoding") (0ms)
failed TechStrap.Portal.Tests.Tickets.SeenTests.A_header_that_is_not_on_any_list_makes_two_responses_differ(name: "Strict-Transport-Security", value: "max-age=31536000") (0ms)
  total: 11
  failed: 5
  succeeded: 6
```

- [ ] **Step 4: Compare every header**

`tests/TechStrap.Portal.Tests/Tickets/Seen.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Tickets/Seen.cs
+++ b/tests/TechStrap.Portal.Tests/Tickets/Seen.cs
@@ -3,15 +3,24 @@ using TechStrap.Portal.Tests.Forms;
 
 namespace TechStrap.Portal.Tests.Tickets;
 
-/// <summary>What a visitor can observe of a response: the status, the body and the headers that matter to the uniform 404 (everything that could tell one failure from another or carry a product's branding).</summary>
+/// <summary>
+/// What a visitor can observe of a response: the status, the body and EVERY header except the few that differ per request by design. A header the Portal adds one day to one kind of 404 and not to another is
+/// then caught without anyone remembering to list it.
+/// </summary>
 internal sealed record Seen(HttpStatusCode Status, string Body, string Headers, int ApiCalls)
 {
+    /// <summary>
+    /// The headers that vary per request by design: the clock, the request's own correlation id and the framing; and the two the framework's antiforgery step adds only to a response that rendered a form (the
+    /// <c>Set-Cookie</c> of the token and <c>Pragma: no-cache</c>), which a post to a product that vanished after its form was served still carries. Nothing is leaked: a visitor holding that form already knew the product.
+    /// </summary>
+    private static readonly HashSet<string> PerRequest = new(["Date", "X-Correlation-Id", "Content-Length", "Transfer-Encoding", "Set-Cookie", "Pragma"], StringComparer.OrdinalIgnoreCase);
+
     public static async Task<Seen> OfAsync(HttpResponseMessage response, int apiCalls, CancellationToken cancellationToken)
     {
         var headers = string.Join(
             "\n",
             response.Headers.Concat(response.Content.Headers)
-                .Where(h => h.Key is "Content-Type" or "Cache-Control" or "Referrer-Policy" or "X-Robots-Tag" or "X-Content-Type-Options" or "X-Frame-Options" or "Content-Security-Policy" or "blazor-enhanced-nav")
+                .Where(h => !PerRequest.Contains(h.Key))
                 .OrderBy(h => h.Key, StringComparer.Ordinal)
                 .Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
         return new Seen(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken), headers, apiCalls);
```

The exclusions are the headers that vary per request by design. The first run of the suite with only `Date`, `X-Correlation-Id`, `Content-Length`, `Transfer-Encoding` and `Set-Cookie` excluded failed exactly three tests, which is the point of the change:

```text
failed TechStrap.Portal.Tests.Tickets.SeenTests.A_header_that_varies_per_request_does_not(name: "Pragma", first: "no-cache", second: "no-cache, x") (1ms)
failed TechStrap.Portal.Tests.Tickets.TicketReplyHostTests.A_token_that_stops_working_between_the_page_and_the_post_is_the_uniform_404 (510ms)
failed TechStrap.Portal.Tests.Forms.ContactPostHostTests.A_product_that_vanishes_between_the_page_and_the_post_is_the_uniform_404 (343ms)
  total: 1471
  failed: 3
  succeeded: 1468
```

The two host tests compare a post to a product that vanished after its form was served with the neutral 404. The framework's antiforgery step adds `Pragma: no-cache` to a response that rendered a form, and that response still carries it. It tells nobody anything (the visitor holding that form knew the product), so `Pragma` joins the exclusions (the last line of the exclusion list above, and the `Pragma` case of `SeenTests`). Run the suite again:

```bash
dotnet test --project tests/TechStrap.Portal.Tests -c Release
```

```text
  total: 1471
  failed: 0
  succeeded: 1471
```

- [ ] **Step 5: Write the failing start-failure tests**

Two things need to be true for the shared helper to work in all three test projects: the Admin factory must have a log sink, and each factory's sink must hold the failed start. `StartFailureLogTests` pins both for the Api, Worker, Admin and Portal hosts (the race is simulated by throwing an `ObjectDisposedException` after a real failed start), and `StartupFailureTests` gains a test for the Portal factory and a test that a warning, or an event about something else, is not evidence.

`tests/TechStrap.Admin.Tests/StartFailureLogTests.cs` (new)

```csharp
using Serilog.Events;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// <see cref="StartupFailure"/> reads a start failure from the log when <c>CreateClient()</c> loses a race with the disposal of a host that failed options validation. The Admin factory must therefore put
/// its failed start on <c>LogSink</c> as a "Hosting failed to start" event that carries the <c>OptionsValidationException</c>.
/// </summary>
public sealed class StartFailureLogTests
{
    [Fact]
    public async Task The_Admin_host_logs_a_failed_start_to_its_sink()
    {
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { ["Auth:Authority"] = null });

        var started = StartupFailure.Capture(factory, () => factory.LogSink.Events);
        started.Message.ShouldContain("AUTH__AUTHORITY");

        // The race case, simulated: the provider is gone, so only the sink can say why the start failed.
        var fromLog = StartupFailure.Capture(() => throw new ObjectDisposedException("IServiceProvider"), () => factory.LogSink.Events, TimeSpan.FromSeconds(5));

        fromLog.Message.ShouldBe(started.Message);
        factory.LogSink.Events.ShouldContain(e => e.Level == LogEventLevel.Error && e.MessageTemplate.Text.Contains("Hosting failed to start"));
    }
}
```

`tests/TechStrap.Api.Tests/Hosting/StartFailureLogTests.cs` (new)

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Serilog.Events;
using TechStrap.Tests.Shared;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// <see cref="StartupFailure"/> reads a start failure from the log when <c>CreateClient()</c> loses a race with the disposal of a host that failed options validation. That fallback is only as good as the
/// sink it reads, so each of the four hosts of this project must put its failed start on <c>LogSink</c> as a "Hosting failed to start" event that carries the <c>OptionsValidationException</c>.
/// </summary>
public sealed class StartFailureLogTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static void ProveTheLogHoldsTheFailure<TEntry>(HostFactory<TEntry> factory, string expectedInMessage)
        where TEntry : class
    {
        var started = StartupFailure.Capture(factory, () => factory.LogSink.Events);
        started.Message.ShouldContain(expectedInMessage);

        // The race case, simulated: the provider is gone, so only the sink can say why the start failed.
        var fromLog = StartupFailure.Capture(() => throw new ObjectDisposedException("IServiceProvider"), () => factory.LogSink.Events, Wait);

        fromLog.Message.ShouldBe(started.Message);
        factory.LogSink.Events.ShouldContain(e => e.Level == LogEventLevel.Error && e.MessageTemplate.Text.Contains("Hosting failed to start"));
    }

    [Fact]
    public async Task The_Api_host_logs_a_failed_start_to_its_sink()
    {
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { ["RateLimiting:Public:PermitLimit"] = "0" });

        ProveTheLogHoldsTheFailure(factory, "RateLimiting:Public:PermitLimit");
    }

    [Fact]
    public async Task The_Worker_host_logs_a_failed_start_to_its_sink()
    {
        await using var factory = new WorkerFactory(settings: new Dictionary<string, string?> { ["EmailOutbox:Enabled"] = "true" });

        ProveTheLogHoldsTheFailure(factory, "Email:Smtp:Host");
    }

    [Fact]
    public async Task The_Admin_host_logs_a_failed_start_to_its_sink()
    {
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { ["Auth:Authority"] = null });

        ProveTheLogHoldsTheFailure(factory, "AUTH__AUTHORITY");
    }

    [Fact]
    public async Task The_Portal_host_logs_a_failed_start_to_its_sink()
    {
        await using var factory = new PortalFactory(settings: new Dictionary<string, string?> { ["TECHSTRAP_PORTAL_SHOW_POWERED_BY"] = "maybe" });

        ProveTheLogHoldsTheFailure(factory, "TECHSTRAP_PORTAL_SHOW_POWERED_BY");
    }
}
```

`tests/TechStrap.Portal.Tests/StartupFailureTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/StartupFailureTests.cs
+++ b/tests/TechStrap.Portal.Tests/StartupFailureTests.cs
@@ -1,10 +1,14 @@
 using Microsoft.Extensions.Options;
 using Serilog.Events;
 using Serilog.Parsing;
+using TechStrap.Tests.Shared;
 
 namespace TechStrap.Portal.Tests;
 
-/// <summary>Both branches of <see cref="StartupFailure"/>: the direct exception and the disposed-provider race, plus the cases that must fail loudly.</summary>
+/// <summary>
+/// Both branches of <see cref="StartupFailure"/> (the helper itself is shared with the Admin and Api tests): the direct exception and the disposed-provider race, plus the cases that must fail loudly, and
+/// the Portal factory's sink as the source of the fallback.
+/// </summary>
 public sealed class StartupFailureTests
 {
     private static readonly TimeSpan NoWait = TimeSpan.Zero;
@@ -40,6 +44,33 @@ public sealed class StartupFailureTests
         Should.Throw<InvalidOperationException>(() => StartupFailure.Capture(() => throw new ObjectDisposedException("IServiceProvider"), () => [], NoWait));
     }
 
+    [Fact]
+    public void A_log_event_below_Error_or_about_something_else_is_not_evidence()
+    {
+        var events = new[]
+        {
+            Event(LogEventLevel.Warning, "Hosting failed to start", new InvalidOperationException("outer", Validation)),
+            Event(LogEventLevel.Error, "Something else failed", new InvalidOperationException("outer", Validation)),
+        };
+
+        Should.Throw<InvalidOperationException>(() => StartupFailure.Capture(() => throw new ObjectDisposedException("IServiceProvider"), () => events, NoWait))
+            .Message.ShouldContain("did not fail on validation");
+    }
+
+    [Fact]
+    public async Task The_Portal_host_logs_a_failed_start_to_its_sink()
+    {
+        await using var factory = new PortalFactory(settings: new Dictionary<string, string?> { ["TECHSTRAP_PORTAL_SHOW_POWERED_BY"] = "maybe" });
+
+        var started = StartupFailure.Capture(factory, () => factory.LogSink.Events);
+        started.Message.ShouldContain("TECHSTRAP_PORTAL_SHOW_POWERED_BY");
+
+        // The race case, simulated: the provider is gone, so only the sink can say why the start failed.
+        var fromLog = StartupFailure.Capture(() => throw new ObjectDisposedException("IServiceProvider"), () => factory.LogSink.Events, TimeSpan.FromSeconds(5));
+
+        fromLog.Message.ShouldBe(started.Message);
+    }
+
     [Fact]
     public void A_start_that_succeeds_fails_the_capture()
     {
```

- [ ] **Step 6: Run them to verify they fail**

```bash
dotnet build tests/TechStrap.Admin.Tests -c Release
dotnet build tests/TechStrap.Api.Tests -c Release
dotnet build tests/TechStrap.Portal.Tests -c Release
```

Expected: FAIL to compile (`StartupFailure` is not shared yet, the Admin factory has no `LogSink`):

```text
Admin.Tests:
tests\TechStrap.Admin.Tests\StartFailureLogTests.cs(17,23): error CS0103: The name 'StartupFailure' does not exist in the current context
tests\TechStrap.Admin.Tests\StartFailureLogTests.cs(21,23): error CS0103: The name 'StartupFailure' does not exist in the current context
tests\TechStrap.Admin.Tests\StartFailureLogTests.cs(24,17): error CS1061: 'AdminFactory' does not contain a definition for 'LogSink' and no accessible extension method 'LogSink' accepting a first argument of ty
Api.Tests:
tests\TechStrap.Api.Tests\Hosting\StartFailureLogTests.cs(18,23): error CS0103: The name 'StartupFailure' does not exist in the current context
tests\TechStrap.Api.Tests\Hosting\StartFailureLogTests.cs(22,23): error CS0103: The name 'StartupFailure' does not exist in the current context
Portal.Tests:
tests\TechStrap.Portal.Tests\StartupFailureTests.cs(65,58): error CS1660: Cannot convert lambda expression to type 'PortalFactory' because it is not a delegate type
```

- [ ] **Step 7: Share the helper and the sink**

`git mv tests/TechStrap.Portal.Tests/StartupFailure.cs tests/Shared/StartupFailure.cs` and change it: a new namespace, and the two `PortalFactory`-specific overloads become one generic overload that takes the events. `CollectingSink` is moved to `tests/Shared` (public, one copy: the Api's and the Portal's are deleted, and the files that name it get a `using`). The Admin factory gains a `LogSink`, and the Api tests link the two shared files (the Admin and Portal tests link `../Shared/*.cs` already; the Admin tests compile with Serilog reaching them through the Admin project).

`tests/Shared/CollectingSink.cs` (new)

```csharp
using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace TechStrap.Tests.Shared;

/// <summary>
/// Captures every Serilog event a test host writes so tests can assert on log lines, and so <see cref="StartupFailure"/> can read a start failure from the log. It is public because the host
/// factories of Api.Tests and Portal.Tests expose it.
/// </summary>
public sealed class CollectingSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events.ToArray();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}
```

`tests/TechStrap.Portal.Tests/StartupFailure.cs` moves to `tests/Shared/StartupFailure.cs` (`git mv`), with this change:

```diff
diff --git a/tests/TechStrap.Portal.Tests/StartupFailure.cs b/tests/Shared/StartupFailure.cs
similarity index 71%
rename from tests/TechStrap.Portal.Tests/StartupFailure.cs
rename to tests/Shared/StartupFailure.cs
--- a/tests/TechStrap.Portal.Tests/StartupFailure.cs
+++ b/tests/Shared/StartupFailure.cs
@@ -2,10 +2,10 @@ using Microsoft.AspNetCore.Mvc.Testing;
 using Microsoft.Extensions.Options;
 using Serilog.Events;
 
-namespace TechStrap.Portal.Tests;
+namespace TechStrap.Tests.Shared;
 
 /// <summary>
-/// Reads the real start failure of a Portal host that must refuse to start. WebApplicationFactory's deferred host can race its own disposal: when ValidateOnStart fails the app disposes its services and
+/// Reads the real start failure of a host (Admin, Api, Worker or Portal) that must refuse to start. WebApplicationFactory's deferred host can race its own disposal: when ValidateOnStart fails the app disposes its services and
 /// <c>CreateClient()</c> may throw an <see cref="ObjectDisposedException"/> instead of the <see cref="OptionsValidationException"/>. The real exception is then on the "Hosting failed to start" log event.
 /// Never passes when the start did not fail on options validation.
 /// </summary>
@@ -13,12 +13,18 @@ internal static class StartupFailure
 {
     private static readonly TimeSpan LogWait = TimeSpan.FromSeconds(5);
 
+    /// <summary>Starts <paramref name="factory"/> and returns the <see cref="OptionsValidationException"/> that stopped it.</summary>
     /// <param name="factory">The factory to start, as returned by <c>WithWebHostBuilder</c> when settings were added.</param>
-    /// <param name="logSource">The <see cref="PortalFactory"/> it was derived from: only that one owns the <see cref="PortalFactory.LogSink"/> the derived host logs to.</param>
-    public static OptionsValidationException Capture(WebApplicationFactory<TechStrap.Portal.Program> factory, PortalFactory logSource) =>
-        Capture(() => factory.CreateClient().Dispose(), () => logSource.LogSink.Events, LogWait);
-
-    public static OptionsValidationException Capture(PortalFactory factory) => Capture(factory, factory);
+    /// <param name="events">
+    /// The events the host logged, read from the sink of the factory the host was derived from (a factory made by <c>WithWebHostBuilder</c> logs to the root factory's sink only), for example
+    /// <c>() => root.LogSink.Events</c>.
+    /// </param>
+    public static OptionsValidationException Capture<TEntry>(WebApplicationFactory<TEntry> factory, Func<IReadOnlyCollection<LogEvent>> events)
+        where TEntry : class
+    {
+        ArgumentNullException.ThrowIfNull(factory);
+        return Capture(() => factory.CreateClient().Dispose(), events, LogWait);
+    }
 
     internal static OptionsValidationException Capture(Action start, Func<IReadOnlyCollection<LogEvent>> events, TimeSpan logWait)
     {
```

`tests/TechStrap.Admin.Tests/AdminFactory.cs`

```diff
--- a/tests/TechStrap.Admin.Tests/AdminFactory.cs
+++ b/tests/TechStrap.Admin.Tests/AdminFactory.cs
@@ -3,6 +3,8 @@ using Microsoft.AspNetCore.Mvc.Testing;
 using Microsoft.AspNetCore.TestHost;
 using Microsoft.Extensions.Configuration;
 using Microsoft.Extensions.DependencyInjection;
+using Serilog.Core;
+using TechStrap.Tests.Shared;
 using TechStrap.Tests.Shared.AdminHost;
 
 namespace TechStrap.Admin.Tests;
@@ -10,7 +12,8 @@ namespace TechStrap.Admin.Tests;
 /// <summary>
 /// Starts the Admin host in-process in the given environment. A developer's gitignored .env.local must never leak into tests, and Production needs a trusted network to start.
 /// It supplies the settings the options validation requires (<see cref="AdminTestSettings"/>, with <c>settings</c> applied on top) and a stub API (<see cref="Api"/>).
-/// It registers the <c>Test</c> authentication scheme: a request signs in with <c>AdminTestAuth.SignedInAs</c>, no header is anonymous.
+/// It registers the <c>Test</c> authentication scheme: a request signs in with <c>AdminTestAuth.SignedInAs</c>, no header is anonymous. <see cref="LogSink"/> records every log event, which
+/// <see cref="StartupFailure"/> reads when a start failure surfaces as a disposed provider.
 /// </summary>
 internal sealed class AdminFactory(
     string environment = "Development",
@@ -28,12 +31,16 @@ internal sealed class AdminFactory(
     /// <summary>The stub behind the Admin's API clients. By default it answers GET /api/agents/me for the three test principals.</summary>
     public StubApiHandler Api { get; } = new StubApiHandler().WithTestAgents();
 
+    /// <summary>Every event the host logs (a factory made by <c>WithWebHostBuilder</c> logs to the root factory's sink only).</summary>
+    public CollectingSink LogSink { get; } = new();
+
     protected override void ConfigureWebHost(IWebHostBuilder builder)
     {
         builder.UseEnvironment(environment);
         builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(AdminTestSettings.With(settings)));
         builder.ConfigureTestServices(services =>
         {
+            services.AddSingleton<ILogEventSink>(LogSink);
             services.AddAdminTestAuthentication();
             services.AddStubApi(Api);
             configureServices?.Invoke(services);
```

`tests/TechStrap.Api.Tests/AdminLeakTests.cs`

```diff
--- a/tests/TechStrap.Api.Tests/AdminLeakTests.cs
+++ b/tests/TechStrap.Api.Tests/AdminLeakTests.cs
@@ -10,6 +10,7 @@ using TechStrap.Contracts.DeadLetters;
 using TechStrap.Contracts.Paging;
 using TechStrap.Contracts.Products;
 using TechStrap.Contracts.Tags;
+using TechStrap.Tests.Shared;
 using TechStrap.Tests.Shared.AdminHost;
 
 namespace TechStrap.Api.Tests;
```

`tests/TechStrap.Api.Tests/HostFactory.cs`

```diff
--- a/tests/TechStrap.Api.Tests/HostFactory.cs
+++ b/tests/TechStrap.Api.Tests/HostFactory.cs
@@ -1,4 +1,3 @@
-using System.Collections.Concurrent;
 using Microsoft.AspNetCore.Hosting;
 using Microsoft.AspNetCore.Mvc.Testing;
 using Microsoft.Extensions.Configuration;
@@ -6,20 +5,11 @@ using Microsoft.Extensions.DependencyInjection;
 using Serilog.Core;
 using Serilog.Events;
 using TechStrap.Api.Startup;
+using TechStrap.Tests.Shared;
 using TechStrap.Tests.Shared.AdminHost;
 
 namespace TechStrap.Api.Tests;
 
-/// <summary>Captures every Serilog event the host writes so tests can assert on log lines.</summary>
-public sealed class CollectingSink : ILogEventSink
-{
-    private readonly ConcurrentQueue<LogEvent> _events = new();
-
-    public IReadOnlyCollection<LogEvent> Events => _events.ToArray();
-
-    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
-}
-
 /// <summary>
 /// Starts one of the TechStrap hosts in-process. Settings are applied as lazily-bound in-memory
 /// configuration. TrustedProxy is NOT overridable this way: AddTrustedProxyForwardedHeaders binds it
```

`tests/TechStrap.Api.Tests/Hosting/FactoryClientLeakTests.cs`

```diff
--- a/tests/TechStrap.Api.Tests/Hosting/FactoryClientLeakTests.cs
+++ b/tests/TechStrap.Api.Tests/Hosting/FactoryClientLeakTests.cs
@@ -1,6 +1,7 @@
 using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Logging;
 using Serilog.Events;
+using TechStrap.Tests.Shared;
 
 namespace TechStrap.Api.Tests.Hosting;
 
```

`tests/TechStrap.Api.Tests/Hosting/OtlpLeakTests.cs`

```diff
--- a/tests/TechStrap.Api.Tests/Hosting/OtlpLeakTests.cs
+++ b/tests/TechStrap.Api.Tests/Hosting/OtlpLeakTests.cs
@@ -1,4 +1,5 @@
 using Serilog.Events;
+using TechStrap.Tests.Shared;
 
 namespace TechStrap.Api.Tests.Hosting;
 
```

`tests/TechStrap.Api.Tests/TechStrap.Api.Tests.csproj`

```diff
--- a/tests/TechStrap.Api.Tests/TechStrap.Api.Tests.csproj
+++ b/tests/TechStrap.Api.Tests/TechStrap.Api.Tests.csproj
@@ -10,6 +10,7 @@
   <ItemGroup>
     <!-- The Admin host test helpers (settings, stub API, Test sign-in) are shared with TechStrap.Admin.Tests. -->
     <Compile Include="../Shared/AdminHost/*.cs" LinkBase="Shared/AdminHost" />
+    <Compile Include="../Shared/CollectingSink.cs;../Shared/StartupFailure.cs" LinkBase="Shared" />
   </ItemGroup>
 
   <ItemGroup>
```

`tests/TechStrap.Portal.Tests/Clients/TicketTokenTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Clients/TicketTokenTests.cs
+++ b/tests/TechStrap.Portal.Tests/Clients/TicketTokenTests.cs
@@ -2,6 +2,7 @@ using System.Text.Json;
 using Serilog;
 using Serilog.Events;
 using TechStrap.Portal.Clients;
+using TechStrap.Tests.Shared;
 
 namespace TechStrap.Portal.Tests.Clients;
 
```

`tests/TechStrap.Portal.Tests/PortalFactory.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/PortalFactory.cs
+++ b/tests/TechStrap.Portal.Tests/PortalFactory.cs
@@ -1,4 +1,3 @@
-using System.Collections.Concurrent;
 using Microsoft.AspNetCore.Hosting;
 using Microsoft.AspNetCore.Mvc.Testing;
 using Microsoft.Extensions.Configuration;
@@ -7,19 +6,10 @@ using Serilog.Core;
 using Serilog.Events;
 using TechStrap.Portal.Settings;
 using TechStrap.Portal.Tests.Api;
+using TechStrap.Tests.Shared;
 
 namespace TechStrap.Portal.Tests;
 
-/// <summary>Captures every Serilog event the host writes so tests can assert on log lines.</summary>
-public sealed class CollectingSink : ILogEventSink
-{
-    private readonly ConcurrentQueue<LogEvent> _events = new();
-
-    public IReadOnlyCollection<LogEvent> Events => _events.ToArray();
-
-    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
-}
-
 /// <summary>
 /// Starts the Portal host in-process in the given environment. A developer's gitignored .env.local must never leak into tests, and Production needs a trusted network to start.
 /// The two required settings (the API address and the public URL) get test values; <paramref name="settings"/> is applied on top, so a test can blank one to prove the start fails.
```

`tests/TechStrap.Portal.Tests/PortalHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/PortalHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/PortalHostTests.cs
@@ -1,6 +1,7 @@
 using System.Net;
 using Microsoft.AspNetCore.Hosting;
 using Microsoft.Extensions.Options;
+using TechStrap.Tests.Shared;
 
 namespace TechStrap.Portal.Tests;
 
@@ -56,7 +57,7 @@ public sealed class PortalHostTests
         await using var root = new PortalFactory();
         await using var factory = root.WithWebHostBuilder(b => b.UseSetting("TECHSTRAP_PORTAL_SHOW_POWERED_BY", "maybe"));
 
-        var failure = StartupFailure.Capture(factory, root);
+        var failure = StartupFailure.Capture(factory, () => root.LogSink.Events);
 
         failure.Message.ShouldContain("TECHSTRAP_PORTAL_SHOW_POWERED_BY");
     }
```

`tests/TechStrap.Portal.Tests/PoweredByHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/PoweredByHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/PoweredByHostTests.cs
@@ -3,6 +3,7 @@ using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Options;
 using TechStrap.Contracts.Products;
 using TechStrap.Portal.Components.Ui;
+using TechStrap.Tests.Shared;
 
 namespace TechStrap.Portal.Tests;
 
@@ -106,7 +107,7 @@ public sealed partial class PoweredByHostTests
     {
         await using var factory = Factory(setting);
 
-        var failure = StartupFailure.Capture(factory);
+        var failure = StartupFailure.Capture(factory, () => factory.LogSink.Events);
 
         failure.Message.ShouldContain("TECHSTRAP_PORTAL_SHOW_POWERED_BY");
         failure.Message.ShouldContain("true or false");
```

- [ ] **Step 8: Run them to verify they pass**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter-query "/*/*/StartFailureLogTests/*"
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/StartFailureLogTests/*"
dotnet test --project tests/TechStrap.Portal.Tests -c Release
```

Expected: 1, 4 and 1,473 tests pass:

```text
  total: 1
  failed: 0
  succeeded: 1
```
```text
  total: 4
  failed: 0
  succeeded: 4
```
```text
  total: 1473
  failed: 0
  succeeded: 1473
```

- [ ] **Step 9: Convert the twelve sites**

Each keeps its original assertion about the key or the message (the three `AgentAccessOptionsTests` sites had none and still have none). The four that asserted on `ToString()` now assert on the captured `OptionsValidationException`'s message, which holds the key. Do not convert `ConfigHostSupport`, `ApiPublicUrlTests`, `TrustedProxyStartupTests` or the Infrastructure tests (see the decisions above).

`tests/TechStrap.Admin.Tests/AdminStartupSettingsTests.cs`

```diff
--- a/tests/TechStrap.Admin.Tests/AdminStartupSettingsTests.cs
+++ b/tests/TechStrap.Admin.Tests/AdminStartupSettingsTests.cs
@@ -1,4 +1,4 @@
-using Microsoft.Extensions.Options;
+using TechStrap.Tests.Shared;
 
 namespace TechStrap.Admin.Tests;
 
@@ -14,7 +14,7 @@ public sealed class AdminStartupSettingsTests
     {
         await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { [key] = null });
 
-        var error = Should.Throw<OptionsValidationException>(() => factory.CreateClient());
+        var error = StartupFailure.Capture(factory, () => factory.LogSink.Events);
 
         error.Message.ShouldContain(variable);
     }
@@ -28,7 +28,7 @@ public sealed class AdminStartupSettingsTests
             ["TECHSTRAP_ADMIN_GROUP"] = "Staff",
         });
 
-        Should.Throw<OptionsValidationException>(() => factory.CreateClient()).Message.ShouldContain("different groups");
+        StartupFailure.Capture(factory, () => factory.LogSink.Events).Message.ShouldContain("different groups");
     }
 
     [Fact]
```

`tests/TechStrap.Admin.Tests/Options/PortalUrlOptionsTests.cs`

```diff
--- a/tests/TechStrap.Admin.Tests/Options/PortalUrlOptionsTests.cs
+++ b/tests/TechStrap.Admin.Tests/Options/PortalUrlOptionsTests.cs
@@ -1,5 +1,5 @@
-using Microsoft.Extensions.Options;
 using TechStrap.Admin.Options;
+using TechStrap.Tests.Shared;
 
 namespace TechStrap.Admin.Tests.Options;
 
@@ -68,6 +68,6 @@ public sealed class PortalUrlOptionsTests
     {
         await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { [PortalUrlOptions.PublicUrlKey] = value });
 
-        Should.Throw<OptionsValidationException>(() => factory.CreateClient()).Message.ShouldContain("TECHSTRAP_PORTAL_PUBLIC_URL");
+        StartupFailure.Capture(factory, () => factory.LogSink.Events).Message.ShouldContain("TECHSTRAP_PORTAL_PUBLIC_URL");
     }
 }
```

`tests/TechStrap.Api.Tests/Auth/AgentAccessOptionsTests.cs`

```diff
--- a/tests/TechStrap.Api.Tests/Auth/AgentAccessOptionsTests.cs
+++ b/tests/TechStrap.Api.Tests/Auth/AgentAccessOptionsTests.cs
@@ -1,6 +1,7 @@
 using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Options;
 using TechStrap.Api.Options;
+using TechStrap.Tests.Shared;
 
 namespace TechStrap.Api.Tests.Auth;
 
@@ -25,7 +26,7 @@ public sealed class AgentAccessOptionsTests
     {
         await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { [key] = value });
 
-        Should.Throw<OptionsValidationException>(() => factory.CreateClient());
+        StartupFailure.Capture(factory, () => factory.LogSink.Events);
     }
 
     [Fact]
@@ -47,7 +48,7 @@ public sealed class AgentAccessOptionsTests
             environment: "Production",
             settings: new Dictionary<string, string?> { ["ConnectionStrings:TechStrap"] = "Host=localhost;Database=unused;Username=u;Password=p", ["Authentication:JwtBearer:Audiences:0"] = "" });
 
-        Should.Throw<OptionsValidationException>(() => factory.CreateClient());
+        StartupFailure.Capture(factory, () => factory.LogSink.Events);
     }
 
     [Fact]
@@ -57,6 +58,6 @@ public sealed class AgentAccessOptionsTests
             environment: "Production",
             settings: new Dictionary<string, string?> { ["ConnectionStrings:TechStrap"] = "Host=localhost;Database=unused;Username=u;Password=p", ["Authentication:JwtBearer:Authority"] = "" });
 
-        Should.Throw<OptionsValidationException>(() => factory.CreateClient());
+        StartupFailure.Capture(factory, () => factory.LogSink.Events);
     }
 }
```

`tests/TechStrap.Api.Tests/Customer/CustomerRateLimitTests.cs`

```diff
--- a/tests/TechStrap.Api.Tests/Customer/CustomerRateLimitTests.cs
+++ b/tests/TechStrap.Api.Tests/Customer/CustomerRateLimitTests.cs
@@ -7,6 +7,7 @@ using TechStrap.Api.Options;
 using TechStrap.Api.Tests.Auth;
 using TechStrap.Contracts.Http;
 using TechStrap.Contracts.Tickets;
+using TechStrap.Tests.Shared;
 
 namespace TechStrap.Api.Tests.Customer;
 
@@ -96,10 +97,8 @@ public sealed class CustomerRateLimitTests(TestPostgres postgres)
         var key = $"{CustomerRateLimitOptions.SectionName}:{property}";
         await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { [key] = "0" });
 
-        var exception = Record.Exception(() => factory.CreateClient());
+        var failure = StartupFailure.Capture(factory, () => factory.LogSink.Events);
 
-        exception.ShouldNotBeNull();
-        exception.ToString().ShouldContain("OptionsValidationException");
-        exception.ToString().ShouldContain(key);
+        failure.Message.ShouldContain(key);
     }
 }
```

`tests/TechStrap.Api.Tests/HostHealthSmokeTests.cs`

```diff
--- a/tests/TechStrap.Api.Tests/HostHealthSmokeTests.cs
+++ b/tests/TechStrap.Api.Tests/HostHealthSmokeTests.cs
@@ -2,9 +2,9 @@ using System.Net;
 using Microsoft.EntityFrameworkCore;
 using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Hosting;
-using Microsoft.Extensions.Options;
 using SyntaxCircus.EntityFrameworkCore.Postgres;
 using TechStrap.Infrastructure.Persistence;
+using TechStrap.Tests.Shared;
 using TechStrap.Worker.Outbox;
 
 namespace TechStrap.Api.Tests;
@@ -87,7 +87,7 @@ public sealed class HostHealthSmokeTests(TestPostgres postgres)
         };
         await using var factory = new WorkerFactory(settings: settings);
 
-        var error = Should.Throw<OptionsValidationException>(() => factory.CreateClient());
+        var error = StartupFailure.Capture(factory, () => factory.LogSink.Events);
         error.Message.ShouldContain("Email:Smtp:Host");
     }
 
```

`tests/TechStrap.Api.Tests/Intake/IntakeRateLimitOptionsTests.cs`

```diff
--- a/tests/TechStrap.Api.Tests/Intake/IntakeRateLimitOptionsTests.cs
+++ b/tests/TechStrap.Api.Tests/Intake/IntakeRateLimitOptionsTests.cs
@@ -1,4 +1,5 @@
 using TechStrap.Api.Options;
+using TechStrap.Tests.Shared;
 
 namespace TechStrap.Api.Tests.Intake;
 
@@ -16,10 +17,8 @@ public sealed class IntakeRateLimitOptionsTests
         var key = $"{IntakeRateLimitOptions.SectionName}:{property}";
         await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { [key] = "0" });
 
-        var exception = Record.Exception(() => factory.CreateClient());
+        var failure = StartupFailure.Capture(factory, () => factory.LogSink.Events);
 
-        exception.ShouldNotBeNull();
-        exception.ToString().ShouldContain("OptionsValidationException");
-        exception.ToString().ShouldContain(key);
+        failure.Message.ShouldContain(key);
     }
 }
```

`tests/TechStrap.Api.Tests/PublicRateLimitOptionsTests.cs`

```diff
--- a/tests/TechStrap.Api.Tests/PublicRateLimitOptionsTests.cs
+++ b/tests/TechStrap.Api.Tests/PublicRateLimitOptionsTests.cs
@@ -1,3 +1,5 @@
+using TechStrap.Tests.Shared;
+
 namespace TechStrap.Api.Tests;
 
 public sealed class PublicRateLimitOptionsTests
@@ -11,11 +13,9 @@ public sealed class PublicRateLimitOptionsTests
     {
         await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { [key] = value });
 
-        var exception = Record.Exception(() => factory.CreateClient());
+        var failure = StartupFailure.Capture(factory, () => factory.LogSink.Events);
 
-        exception.ShouldNotBeNull();
-        exception.ToString().ShouldContain("OptionsValidationException");
-        exception.ToString().ShouldContain(key);
+        failure.Message.ShouldContain(key);
     }
 
     [Fact]
```

`tests/TechStrap.Portal.Tests/Products/RootPageHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Products/RootPageHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/Products/RootPageHostTests.cs
@@ -1,6 +1,7 @@
 using System.Net;
 using Microsoft.AspNetCore.Mvc.Testing;
 using TechStrap.Portal.Settings;
+using TechStrap.Tests.Shared;
 
 namespace TechStrap.Portal.Tests.Products;
 
@@ -66,6 +67,6 @@ public sealed class RootPageHostTests
     {
         await using var factory = new PortalFactory(settings: Default("Not A Slug"));
 
-        Should.Throw<Microsoft.Extensions.Options.OptionsValidationException>(() => factory.CreateClient());
+        StartupFailure.Capture(factory, () => factory.LogSink.Events);
     }
 }
```

`tests/TechStrap.Portal.Tests/Settings/PortalOptionsHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Settings/PortalOptionsHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/Settings/PortalOptionsHostTests.cs
@@ -1,6 +1,7 @@
 using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Options;
 using TechStrap.Portal.Settings;
+using TechStrap.Tests.Shared;
 
 namespace TechStrap.Portal.Tests.Settings;
 
@@ -34,7 +35,7 @@ public sealed class PortalOptionsHostTests
     {
         await using var factory = new PortalFactory(environment, new Dictionary<string, string?> { [key] = value });
 
-        var failure = Should.Throw<OptionsValidationException>(() => factory.CreateClient());
+        var failure = StartupFailure.Capture(factory, () => factory.LogSink.Events);
 
         failure.Message.ShouldContain(expectedInMessage);
     }
```

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release
dotnet test --project tests/TechStrap.Portal.Tests -c Release
```

Expected: every site passes through the helper (the Api suite needs Docker for its Postgres tests):

```text
  total: 1962
  failed: 0
  succeeded: 1962
```
```text
  total: 932
  failed: 0
  succeeded: 932
```
```text
  total: 1473
  failed: 0
  succeeded: 1473
```

- [ ] **Step 10: Pin what the D-045 wording must say**

The 09b note says "the framework re-executes the post against the not-found page". It does not: `NavigationManager.NotFound()` renders the not-found page in the same request, and only a plain 404 that reaches the host's status-code middleware is re-executed (and the 400 of this post is not a 404). The test counts the dependency scopes of the post (a re-execution would create a second one) and passes before the wording is fixed, because the code was always right and only the sentence was wrong.

`tests/TechStrap.Portal.Tests/Forms/ContactPostHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Forms/ContactPostHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/Forms/ContactPostHostTests.cs
@@ -4,6 +4,7 @@ using Microsoft.Extensions.DependencyInjection;
 using Serilog.Events;
 using TechStrap.Contracts.Intake;
 using TechStrap.Portal.Clients;
+using TechStrap.Portal.Products;
 using TechStrap.Portal.Tests.Api;
 using TechStrap.Portal.Tests.Tickets;
 
@@ -438,6 +439,28 @@ public sealed class ContactPostHostTests
         factory.Api.Count(HttpMethod.Post, "/api/public/products/nope/tickets").ShouldBe(0);
     }
 
+    [Fact]
+    public async Task A_post_to_an_unknown_product_is_answered_in_one_request_not_re_executed_against_the_not_found_page()
+    {
+        // The wording of D-045 depends on this: NavigationManager.NotFound() renders the not-found page in the SAME request (one dependency scope, and the layout is built once), and the framework then rejects the
+        // post because no form named "contact" was rendered. A re-execution (UseStatusCodePagesWithReExecute) would build a second scope.
+        var scopes = 0;
+        await using var factory = Host(services => services.AddScoped(_ =>
+        {
+            Interlocked.Increment(ref scopes);
+            return new ProductScope();
+        }));
+        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/nope", HttpStatusCode.NotFound, "product-not-found", "No such product.");
+        var (client, token) = await OpenAsync(factory);
+        using var _ = client;
+        var before = scopes;
+
+        using var response = await client.PostAsync("/p/nope/contact", FormTestKit.ContactForm(token), Ct);
+
+        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
+        (scopes - before).ShouldBe(1, "the post, the not-found page and the layout share one request scope");
+    }
+
     // ---- logs ----
 
     [Fact]
```

```bash
dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/ContactPostHostTests/A_post_to_an_unknown_product*"
```

```text
  total: 1
  failed: 0
  succeeded: 1
```

- [ ] **Step 11: Write the failing Pester block, then the D-045 addendum and the wording fix**

Add the Pester block first (the `RepositoryDocs.Tests.ps1` part of the change below) and run it:

```bash
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected: FAIL (3 tests: the addendum, its phrases, the wording):

```text
[-] D-045 addendum (PHASE-09d rulings, 2026-10-06).is a dated addendum inside D-045, not a new decision number 15ms
[-] D-045 addendum (PHASE-09d rulings, 2026-10-06).records each ruling the 09d plan rests on and the spike findings 5ms
[-] D-045 addendum (PHASE-09d rulings, 2026-10-06).words the 09b note about a post to an unknown product as the same request, not a re-execution 8ms
Tests Passed: 40, Failed: 3, Skipped: 0, Inconclusive: 0, NotRun: 0
```

Then write the addendum and fix the sentence (the addendum is the record of the rulings and of what the spike proved, in the form of the 09c addendum; it goes before `### Approval`):

`docs/architecture/04-DECISION-LOG.md`

```diff
--- a/docs/architecture/04-DECISION-LOG.md
+++ b/docs/architecture/04-DECISION-LOG.md
@@ -1717,7 +1717,7 @@ The owner's rulings for PHASE-09b, and what the plan's spike proved. They extend
 - **Redirect after post.** `NavigationManager.NavigateTo` in a static SSR handler answers 302 with an absolute `Location`; the handler returns straight after it.
 
 **Consequences of the addendum (as built in 09b)**
-- **As built in 09b: a post to an unknown product is a 400, not a 404.** The product page asks the API for the product before the form handler runs and ends in `NavigationManager.NotFound()`; the framework re-executes the post against the not-found page, which has no handler named `contact`, so a post to an unknown, inactive or malformed product (with a valid antiforgery token) gets the framework's own plain-text 400, "Cannot submit the form 'contact' because no form on the page currently has that name.", because the form was never rendered. Nothing is created. A GET is the neutral 404 page. A post to a malformed or an unknown ticket token is the same thing for the form `reply`: an identical body whichever part was wrong, so it tells a visitor nothing (pinned by `TicketReplyHostTests`). The 09a note that "an unknown product 404s before the form handler runs" was right about the order and wrong about the status.
+- **As built in 09b: a post to an unknown product is a 400, not a 404.** The product page asks the API for the product before the form handler runs and ends in `NavigationManager.NotFound()`; `NotFound()` renders the not-found page in the same request (it is not a re-execution of the post: the request has one dependency scope, `ContactPostHostTests` counts it, and only a plain 404 that reaches the host's status-code middleware is re-executed), and the framework then rejects the post, because no form named `contact` was rendered in that response. So a post to an unknown, inactive or malformed product (with a valid antiforgery token) gets the framework's own plain-text 400, "Cannot submit the form 'contact' because no form on the page currently has that name.". Nothing is created. A GET is the neutral 404 page. A post to a malformed or an unknown ticket token is the same thing for the form `reply`: an identical body whichever part was wrong, so it tells a visitor nothing (pinned by `TicketReplyHostTests`). The 09a note that "an unknown product 404s before the form handler runs" was right about the order and wrong about the status.
 - **As built in 09b: the request size limit.** `[RequestSizeLimit(IntakeLimits.FormBodyBytes)]` on the contact and ticket pages is the limit; `RequestTooLargeMiddleware` (before `UseAntiforgery`) turns a declared `Content-Length` over it into a plain 413 with the Portal's own sentence, because the framework reports the failed form read as an antiforgery 400. A chunked body over the limit is still that 400. A file is read with `OpenReadStream(IntakeLimits.MaxFileBytes)` (the default is 512,000 bytes); Cmsify's media upload uses the same pattern (an explicit maximum, the stream passed on without buffering, `[RequestSizeLimit]` on the receiving endpoint).
 - **As built in 09b: the honeypot** is sent to the API as `Website` and a filled one gets the API's believable 201; the Portal never short-circuits, so product validation and the response stay uniform. The PHASE-09 text that said otherwise is corrected.
 - **As built in 09b: the ticket page.** The token is parsed first; the API is never asked for a malformed one; every way to fail to find a ticket (and a wrong attachment id) is the same neutral 404, byte for byte. An inactive or unknown product on a valid ticket is the neutral theme, not a 404. `CustomerMessageBody` is the only place the Portal renders markup. The follow-up redirect is built from the last segment of `FollowUpViewUrl` by `FollowUpLink` and `PortalRoutes.Ticket`, so it never leaves the site; an unreadable link shows a generic confirmation.
@@ -1768,6 +1768,47 @@ The owner's rulings for PHASE-09c, and what the plan's spike proved. They extend
 - **Known in 09c: the package's `JsonLd` is still unsafe for any other caller.** The Portal avoids it for strings; the problem (a `</script>` in a value ends the block) is to be reported upstream to `SyntaxCircus.Blazor.Seo`.
 - **Resolved in 09c: the product pages no longer link ahead.** The search box, the KB links on the contact and received pages and the sitemap named by robots.txt all answer.
 
+### Addendum (2026-10-06, PHASE-09d portal polish)
+The owner's rulings for PHASE-09d, and what the plan's spike proved. They extend D-045 and the 09b and 09c addenda; where they differ from the text above, they win. There is no new decision number.
+
+**Rulings**
+- **A double click sends once (P09-T09).** Each of the reply, contact and lost-link forms carries a fresh 128-bit random `SubmitId` (22 base64url characters from `RandomNumberGenerator`) in a hidden input right after `<AntiforgeryToken />`, rendered by the `FormGuard` component and made anew on every render (a form shown again after an error carries a new id, never the posted one). The handler claims the id with the process-local `SubmitGuard` only after validation passes and before the API call.
+  - The guard has its own `MemoryCache` with a size cap of 10,000 claims (never the shared `IMemoryCache`, which holds the sitemap and has no size); a claim is made atomically under a lock and lives 2 minutes, which is shorter than the 10-minute reference it may hold. The key is a hash of the form name, the product key (or the ticket's access token) and the id, so an id cannot be used on another form, product or ticket, and the cache never holds a token.
+  - A repeat of an id never writes. It waits for the first request's answer when that is still running, or reads the stored one, and redirects to the same place. The stored target is memory-only and never logged (the guard takes no logger); for a follow-up reply it holds the new ticket's token, which is accepted for the 2 minutes.
+  - **Cancellation.** The claimed write runs on its own token with a 30-second timeout, never on `RequestAborted`, because a real double click makes the browser abort the first POST while the API may already have the ticket. The first request waits for its own write without its abort token (bounded by that timeout), so what it is reading, the uploaded files, stays valid (a precaution; losing them was not reproduced).
+  - **Unknown answers.** If the write times out or throws, nobody knows whether it happened. The claim is kept as unknown: a repeat is sent to the safe fallback and never sends again (a reply: the same ticket page; contact: the received page with no reference, which shows its generic confirmation; lost link: the sent page, which is a constant). The first request shows the calm "unavailable" notice.
+  - **Failures.** An API failure (429, 409, 503 or any other) releases the claim, so a retry sends; a repeat that was already waiting gets the same failure and does not write.
+  - **Not guarded.** A missing or malformed id means the post goes through as it always did (old pages and the test kits keep working); when the cache is full a new post is not guarded, never refused.
+  - **Known limits.** The guard is per instance and is lost on restart, like the sitemap cache; the API still has no idempotency key for `/api/customer` or the public ticket route.
+- **One small script, `portal-forms.js` (loaded once from the document shell).** Vanilla JavaScript with two custom elements and two document listeners: the "Sending..." state (the submit button is disabled and shows the form's own `data-sending-label`; it comes back on `pageshow` after the back button and after a minute), `<ts-copy-text>` (a copy button for the ticket number that selects the number when the clipboard is refused) and `<ts-char-count>` (a counter on a long text field that appears at 80 percent of the limit and counts a line break as two characters, because the browser posts CR LF while `maxlength` counts one). Every word comes from a `*Copy` constant through a data attribute, text is put on the page with `textContent` only, there is no inline script and no `on*` attribute, and the CSP does not change.
+- **T16 styling, accessibility and the no-JS pass.**
+  - **Widths.** One token for the reading column (`--ts-reading-width`, 40rem) and one for the wide container (`--ts-wide-width`, 64rem; the header, the footer, search, the categories and the product home); one column below 768px, category cards two across from 768px and three from 1200px.
+  - **Tokens.** BRAND.md gains six Portal tokens: `--p-error`, `--p-success` and `--p-warn` (the same red, green and amber as the Admin's `--st-spam`, `--st-open` and `--st-pending`) and their grounds `--p-error-bg`, `--p-success-bg` and `--p-warn-bg`. The old `var(--p-error, #b3261e)` pointed at a token that did not exist. A control's edge is `--p-ink2` (3:1), never the decorative `--p-line`.
+  - **Landmarks and headings.** A skip link and `main id="main" tabindex="-1"`; a named `nav` in the product header (the help centre and contact) and one in the footer, and one contentinfo (the "Powered by" footer); one `h1` per page, the search page's being "Search the help center"; an `h1` inside an article or a message body is shown as an `h2` (`BodyHeadings`, the only change the Portal makes to those bodies).
+  - **Targets and states.** Every link a visitor has to hit is at least 44px tall; required is said in words above each form; forced colours keep the 3px focus outline (the accent halo is dropped) and what colour alone would say; reduced motion switches every animation and transition off, and smooth scrolling is off in the build.
+  - **The `<base href="/">` bug.** A bare `#field` link in the error summary resolved to `/#field`, the home page. The links are written as the current root-relative path plus the fragment by `PageLinks.ToFragment`, and carry `data-enhance-nav="false"` (see the spike). The skip link and the "jump to your reply" link use the same helper.
+  - **Search box.** The product home's duplicate search box is replaced by `KbSearchBox`.
+  - **Tests.** `ResponsiveStyleTests`, `TokenContrastTests` and `CspStyleTests` in the Admin's pattern, `HeadingHostTests`, `LayoutLandmarkHostTests` and `ErrorSummaryLinkHostTests` (which resolve each link against the document's base, as a browser does).
+  - **Manual T16 evidence** (the axe run, Lighthouse accessibility of at least 90, the JavaScript-off walk, screenshots at 360, 768 and 1280, and the T18 smoke) is the owner's checklist in PORTAL-APP.md, as in ADMIN-APP.md, and is recorded in the pull request.
+- **Test hardening.** `tests/Shared/StartupFailure.cs` is generic over `WebApplicationFactory<TEntry>` and takes the log events of the factory's sink; the Portal, Admin and Api tests all use it, so a host that must refuse to start on options validation is read from the log when `CreateClient()` loses its race with the host's disposal, and a start that does not fail on validation still fails the test. The twelve start-failure sites (three Admin, seven Api, two Portal) are converted. `Seen` compares every response header except the per-request ones.
+- **Docs.** The wording of the 09b note about a post to an unknown product is corrected (above), PORTAL-APP.md, the PHASE-09 ticks, the roadmap and discovery rows and the as-built notes below.
+
+**Spike findings (proven in a scratch copy, in Edge headless and in the test host, before the plan was written)**
+- **Two concurrent posts with one id make one API call and both redirect to the same place.** Eight concurrent posts did too (`SubmitGuardTests`, `DoubleSendHostTests`).
+- **An aborted first post.** A client that cancels its call does not get an answer from the test server until the server's handler has finished, so the first request must not stop waiting for its write. The write's own token is not canceled when the browser goes away (`CancelledWhenAnswered` is false), it completes, and a repeat that arrives meanwhile, or after, gets the same redirect. A write that times out is unknown, and the repeat goes to the fallback.
+- **Enhanced navigation does not run a script that arrives with swapped content.** `kb-suggestions.js` was loaded by the contact page's own body (09b), so for a visitor who reached the contact page by clicking a link (enhanced navigation) `customElements.get('ts-kb-suggestions')` was undefined and the suggestions never worked. A module in the document shell (`App.razor`) runs once, and the browser then calls `connectedCallback` for every element the swap inserts (the count rose on each navigation), so both modules are loaded there and the contact page no longer carries a script. The swap also rewrites the attributes of an element it keeps, which wiped an attribute set from script, so the module keeps its state in a `WeakMap`. A listener on the document survives navigations.
+- **The `<base href="/">` bug is real, and removing the tag is not safe.** In Edge a click on a summary link went to the home page (the page then showed "Support"). The stylesheet, the favicon and `blazor.web.js` are written relative to the base, and Blazor reads the base for navigation, so the tag stays. A root-relative path plus the fragment is a jump within the page. Blazor still takes the click (`defaultPrevented` is true), scrolls and leaves the focus on the link; with `data-enhance-nav="false"` the browser moves the focus to the field (`INPUT#email`, with a real mouse click and with Enter). The skip link works the same way and keeps the prefill.
+- **`StartupFailure` works through every factory.** The Admin factory gains a `CollectingSink`, the Api, Worker and Portal factories had one, and each puts its failed start on the sink as a "Hosting failed to start" event that carries the `OptionsValidationException` (`StartFailureLogTests`). A failure thrown eagerly while the host is built (the Portal's sitemap mapping reads an option) throws the validation exception directly and logs nothing, so it never races.
+
+**Deviations from the brief, each with its reason**
+- `Seen` also ignores `Pragma` and `Set-Cookie`: widening it to every header failed two tests on `Pragma: no-cache`, which the framework's antiforgery step adds to a response that rendered a form (a post to a product that vanished after its form was served). A visitor holding that form already knew the product, so nothing leaks.
+- The skip link is rendered only on a product page (with the product header it skips). It is built from the address, and the neutral 404 must stay byte for byte the same for every address.
+- The skip link keeps only the query parameters the page itself reads (`PageLinks.KnownParameters`): keeping all of them echoed the extra parameters of the contact page into its markup, and the output cache would have shown the first visitor's to the next.
+- An `h1` in an article or a message body becomes an `h2` (`BodyHeadings`), so "the Portal does not change a byte of the API's HTML" is now "except that".
+- `kb-suggestions.js` is fixed (the brief said only if it breaks): the spike showed it does.
+- Required is said in one sentence above each form, not in each label, so the pinned label markup does not change.
+- The product home still has no category list (the header's help-center link is the way in).
+
 ### Approval
 - **Approved by:** Jon Seeley (owner, PHASE-09 planning)
 - **Approved on:** 2026-10-05
```

`scripts/tests/RepositoryDocs.Tests.ps1`

```diff
--- a/scripts/tests/RepositoryDocs.Tests.ps1
+++ b/scripts/tests/RepositoryDocs.Tests.ps1
@@ -259,6 +259,30 @@ Describe 'D-045 addendum (PHASE-09c rulings, 2026-10-06)' {
     }
 }
 
+Describe 'D-045 addendum (PHASE-09d rulings, 2026-10-06)' {
+    BeforeAll {
+        $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
+        $script:Section = [regex]::Match($script:Log, '(?s)## D-045:.*?(?=\r?\n## D-\d+:|\z)').Value
+    }
+
+    It 'is a dated addendum inside D-045, not a new decision number' {
+        $script:Section | Should -Match '(?m)^### Addendum \(2026-10-06, PHASE-09d portal polish\)'
+        $script:Log | Should -Not -Match '(?m)^## D-046'
+    }
+
+    It 'records each ruling the 09d plan rests on and the spike findings' {
+        foreach ($phrase in 'SubmitId', 'FormGuard', 'SubmitGuard', 'own `MemoryCache`', 'RequestAborted', 'portal-forms.js', 'ts-copy-text', 'ts-char-count', '--ts-reading-width', '--p-error', 'PageLinks.ToFragment',
+                'data-enhance-nav', 'BodyHeadings', 'StartupFailure', 'ResponsiveStyleTests', 'HeadingHostTests', 'Seen', 'Spike findings', 'does not run a script that arrives with swapped content', 'Deviations from the brief') {
+            $script:Section | Should -Match ([regex]::Escape($phrase)) -Because "the addendum must mention $phrase"
+        }
+    }
+
+    It 'words the 09b note about a post to an unknown product as the same request, not a re-execution' {
+        $script:Section | Should -Match ([regex]::Escape('renders the not-found page in the same request'))
+        $script:Section | Should -Not -Match ([regex]::Escape('the framework re-executes the post'))
+    }
+}
+
 Describe 'D-045 as built in 09b' {
     BeforeAll {
         $log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
```

Run the Pester file again. Expected: 43 pass:

```text
Tests Passed: 43, Failed: 0, Skipped: 0, Inconclusive: 0, NotRun: 0
```

- [ ] **Step 12: Prove each pin with a recorded mutation**

Save `$T\specs\m1.py`:

```python
"""Mutations of Task 1 (test hardening). Run from the repository root."""
PORTAL = ["dotnet", "test", "--project", "tests/TechStrap.Portal.Tests", "-c", "Release", "--filter-query"]
ADMIN = ["dotnet", "test", "--project", "tests/TechStrap.Admin.Tests", "-c", "Release", "--filter-query"]
API = ["dotnet", "test", "--project", "tests/TechStrap.Api.Tests", "-c", "Release", "--filter-query"]
PESTER = ["pwsh", "-NoProfile", "-File", "scripts/Invoke-ScriptTests.ps1", "-Output", "Minimal", "-Path", "scripts/tests/RepositoryDocs.Tests.ps1"]
SF = "tests/Shared/StartupFailure.cs"
SEEN = "tests/TechStrap.Portal.Tests/Tickets/Seen.cs"

MUTATIONS = [
    ("1 a start that succeeds passes the capture", SF,
     [('throw new InvalidOperationException("The host started, but its start was expected to fail on options validation.");', 'return new OptionsValidationException("n", typeof(object), ["x"]);')],
     PORTAL + ["/*/*/StartupFailureTests/*"]),
    ("2 the exception is not looked for down the inner exceptions", SF,
     [("        _ => Find(exception.InnerException),", "        _ => null,")],
     PORTAL + ["/*/*/StartupFailureTests/*"]),
    ("3 a warning counts as evidence", SF,
     [("logEvent.Level >= LogEventLevel.Error", "logEvent.Level >= LogEventLevel.Verbose")],
     PORTAL + ["/*/*/StartupFailureTests/*"]),
    ("4 any message counts as evidence", SF,
     [('logEvent.MessageTemplate.Text.Contains("Hosting failed to start", StringComparison.Ordinal)', "logEvent.MessageTemplate.Text.Length > 0")],
     PORTAL + ["/*/*/StartupFailureTests/*"]),
    ("5 a disposed provider is not read from the log at all", SF,
     [("        catch (ObjectDisposedException disposed)\n        {\n            return FromLog(events, logWait, disposed);\n        }", "        catch (ObjectDisposedException disposed)\n        {\n            throw new InvalidOperationException(\"x\", disposed);\n        }")],
     PORTAL + ["/*/*/StartupFailureTests/*"]),
    ("6 Seen is an allow-list again", SEEN,
     [(".Where(h => !PerRequest.Contains(h.Key))", '.Where(h => h.Key is "Content-Type" or "Cache-Control")')],
     PORTAL + ["/*/*/SeenTests/*"]),
    ("7 Seen no longer ignores Pragma", SEEN,
     [('"Set-Cookie", "Pragma"]', '"Set-Cookie"]')],
     PORTAL + ["/*/*/SeenTests/*"]),
    ("8 Seen no longer ignores the correlation id", SEEN,
     [('["Date", "X-Correlation-Id", "Content-Length"', '["Date", "Content-Length"')],
     PORTAL + ["/*/*/SeenTests/*"]),
    ("9 the Admin site no longer breaks the start", "tests/TechStrap.Admin.Tests/AdminStartupSettingsTests.cs",
     [("new AdminFactory(settings: new Dictionary<string, string?> { [key] = null });", 'new AdminFactory(settings: new Dictionary<string, string?> { [key] = variable.Length > 0 ? "https://auth.example.com/" : null });')],
     ADMIN + ["/*/*/AdminStartupSettingsTests/*"]),
    ("10 an Admin portal-url site no longer breaks the start", "tests/TechStrap.Admin.Tests/Options/PortalUrlOptionsTests.cs",
     [('[PortalUrlOptions.PublicUrlKey] = value });\n\n        StartupFailure', '[PortalUrlOptions.PublicUrlKey] = value.Length > 0 ? "https://help.example.com" : value });\n\n        StartupFailure')],
     ADMIN + ["/*/*/PortalUrlOptionsTests/*"]),
    ("11 an Api rate-limit site no longer breaks the start", "tests/TechStrap.Api.Tests/Customer/CustomerRateLimitTests.cs",
     [('settings: new Dictionary<string, string?> { [key] = "0" });\n\n        var failure', 'settings: new Dictionary<string, string?> { [key] = "5" });\n\n        var failure')],
     API + ["/*/*/CustomerRateLimitTests/*"]),
    ("12 a Portal site no longer breaks the start", "tests/TechStrap.Portal.Tests/Products/RootPageHostTests.cs",
     [('new PortalFactory(settings: Default("Not A Slug"));\n\n        StartupFailure', 'new PortalFactory(settings: Default("paperplane"));\n\n        StartupFailure')],
     PORTAL + ["/*/*/RootPageHostTests/*"]),
    ("13 the D-045 wording goes back to a re-execution", "docs/architecture/04-DECISION-LOG.md",
     [("`NotFound()` renders the not-found page in the same request (it is not a re-execution of the post:", "the framework re-executes the post against the not-found page (it is not a re-execution of the post:")],
     PESTER),
]
```

Run them (in two foreground batches, `1`..`8` and `9`..`13`, about a minute each) and record the results:

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | a start that succeeds passes the capture | `StartupFailure.cs` | KILLED (1 failing) |
| 2 | the exception is not looked for down the inner exceptions | `StartupFailure.cs` | KILLED (1 failing) |
| 3 | a warning counts as evidence | `StartupFailure.cs` | KILLED (1 failing) |
| 4 | any message counts as evidence | `StartupFailure.cs` | KILLED (1 failing) |
| 5 | a disposed provider is not read from the log at all | `StartupFailure.cs` | KILLED (4 failing) |
| 6 | Seen is an allow-list again | `Seen.cs` | KILLED (6 failing) |
| 7 | Seen no longer ignores Pragma | `Seen.cs` | KILLED (1 failing) |
| 8 | Seen no longer ignores the correlation id | `Seen.cs` | KILLED (1 failing) |
| 9 | the Admin site no longer breaks the start | `AdminStartupSettingsTests.cs` | KILLED (4 failing) |
| 10 | an Admin portal-url site no longer breaks the start | `PortalUrlOptionsTests.cs` | KILLED (2 failing) |
| 11 | an Api rate-limit site no longer breaks the start | `CustomerRateLimitTests.cs` | KILLED (4 failing) |
| 12 | a Portal site no longer breaks the start | `RootPageHostTests.cs` | KILLED (1 failing) |
| 13 | the D-045 wording goes back to a re-execution | `04-DECISION-LOG.md` | KILLED |

Every mutation is killed; there are no survivors in this task. (Mutations 9 and 10 first changed a theory's setting in a way that left a parameter unused, which does not compile (xUnit1026): they were rewritten to keep the parameter used.) The conversion of each site is proved by one mutation per project that makes the host start: the helper then fails the test, which is what "never passes when start did not fail on validation" means.

- [ ] **Step 13: Run the full suites and commit**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release
dotnet test --project tests/TechStrap.Portal.Tests -c Release
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected: 1,962, 932 and 1,474 tests pass; the Pester file passes.

```bash
git add -A
git diff --cached --stat
git commit -m "test: PHASE-09d shared start-failure helper, Seen compares every header, D-045 wording (D-045)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

### Task 2: The double-send guard: `SubmitIds`, `SubmitGuard`, `FormGuard` and the three handlers

**Review Focus pin:** 1 (no path sends twice for one `SubmitId`, including concurrent posts and an aborted first post; a failure lets a retry through; an unknown answer never sends again) and 2 (the guard's own capped cache, no token and no target in a log, an id that cannot cross forms, products or tickets, a full cache that never refuses a post).

**Files:**

- Create: `src/TechStrap.Portal/Forms/SubmitIds.cs`
- Create: `src/TechStrap.Portal/Forms/SubmitGuard.cs` (also holds `SubmitTarget`, `SubmitKey`, `SubmitStatus` and `SubmitOutcome`)
- Create: `src/TechStrap.Portal/Components/Ui/FormGuard.razor`
- Modify: `src/TechStrap.Portal/Forms/ContactFormViewModel.cs`, `src/TechStrap.Portal/Forms/ReplyForm.cs`, `src/TechStrap.Portal/Forms/LostLinkForm.cs` (a `SubmitId` property)
- Modify: `src/TechStrap.Portal/Forms/FormFailure.cs` (`FormFailure.Unknown`)
- Modify: `src/TechStrap.Portal/Components/Pages/Contact.razor`, `Contact.razor.cs`, `LostLink.razor`, `LostLink.razor.cs`, `Ticket.razor`, `Ticket.razor.cs`
- Modify: `src/TechStrap.Portal/Program.cs` (`AddSingleton<SubmitGuard>()`)
- Test (create): `tests/TechStrap.Portal.Tests/Forms/SubmitGuardTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Forms/DoubleSendHostTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Api/StubApiHandler.cs` (`OnAsync`: an answer a test holds back)
- Test (modify): `tests/TechStrap.Portal.Tests/Forms/FormTestKit.cs` (`SubmitIdFrom`, `WithSubmitId`)
- Test (modify): `tests/TechStrap.Portal.Tests/Forms/ContactPageHostTests.cs` (the one hidden `Form` field is now allowed), `tests/TechStrap.Portal.Tests/Tickets/TicketReplyHostTests.cs` (a comment)

**Interfaces:**
- Consumes: `IPublicTicketClient.SubmitAsync`, `ICustomerTicketClient.ReplyAsync` and `RequestAccessLinkAsync` (each returns a `Result`; a transport timeout of the caller's token is an `OperationCanceledException`), `ReceivedReference.Protect` and `ReceivedReference.Lifetime` (10 minutes), `FollowUpLink.TryGetToken`, `FormFailure.From`, `PortalRoutes.ContactReceived(key[, reference])`, `LostLinkSent`, `Ticket`, `TicketToken.Value`, `ProductPageBase.RequestAborted`, `SyntaxCircus.Common.Result<T>` and `ResultError`, `StubApiHandler`, `FormTestKit`, `TicketTestKit`.
- Produces:
  - `SubmitIds` (static): `const int Length = 22`; `string New()` (16 random bytes from `RandomNumberGenerator`, base64url); `bool IsWellFormed(string?)` (exactly `[A-Za-z0-9_-]{22}`).
  - `SubmitKey` (readonly record struct): `string Hash`; `static SubmitKey? TryCreate(string form, string scope, string? id)` (null for a missing or malformed id; otherwise the SHA-256 of the form name, the lower-cased scope and the id).
  - `SubmitTarget(string? Path)` (readonly record struct; `Path` null means "show the confirmation in place"; `SubmitTarget.None`), `SubmitStatus { Done, Failed, Unknown }`, `SubmitOutcome(SubmitStatus Status, SubmitTarget Target, IReadOnlyList<ResultError> Errors)`.
  - `SubmitGuard` (sealed, `IDisposable`, singleton): `static readonly TimeSpan Lifetime` (2 minutes), `WriteTimeout` (30 seconds), `const int MaxClaims` (10,000); constructors `SubmitGuard()` and `SubmitGuard(TimeSpan lifetime, TimeSpan writeTimeout, int maxClaims)`; `Task<SubmitOutcome> RunAsync(SubmitKey? key, SubmitTarget fallback, Func<CancellationToken, Task<Result<SubmitTarget>>> write, CancellationToken requestAborted)`.
  - `FormGuard` component: `[Parameter, EditorRequired] string Name` renders `<input type="hidden" name="{Name}" value="{fresh id}" />`; the view models gain `string? SubmitId`; `FormFailure.Unknown` (the outage notice and a 503).
  - Test support: `StubApiHandler.OnAsync(HttpMethod, string path, Func<StubApiRequest, CancellationToken, Task<HttpResponseMessage>>)`, `FormTestKit.SubmitIdFrom(string html, string name = "Form.SubmitId")` and `FormTestKit.WithSubmitId(this MultipartFormDataContent, string id, string name = "Form.SubmitId")`.

- [ ] **Step 1: Write the failing tests**

Two test files arrive. `SubmitGuardTests` tests the class alone with fake writes, one of which waits on a gate the test opens (one write per id, in a row and at once; the write on its own token that a request cannot cancel; the first request that keeps waiting; a repeat after an aborted first; a repeat that is itself aborted; a failure that releases the claim, and the repeat that was already waiting; a timeout and a fault that leave an unknown answer; the key's scope; the lifetime and the cap; no logger and no shared cache). `DoubleSendHostTests` runs the three forms through the host, holding the API call at a gate where it needs the first post to be "in flight". Both carry a 10-second timeout on every test that waits (see the decisions).

`tests/TechStrap.Portal.Tests/Forms/DoubleSendHostTests.cs` (new)

```csharp
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using TechStrap.Contracts.Tickets;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Tests.Api;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// PHASE-09 T09 at the host (D-045 09d addendum, Review Focus 1 and 2): a double click on the contact, reply or lost-link form sends once. The same id posted twice, one after the other or at the same time, makes exactly
/// one API call and both posts redirect to the same place; a first post that the browser aborted still completes its write on its own token and the repeat goes to its result (or to the safe fallback when that result
/// is unknown); a 429, 409 or 503 lets a retry send; an id cannot be used on another form, product or ticket; a post with no id or a malformed one is not guarded; and neither a follow-up's token, a reference nor an id
/// reaches a log. Every test that calls the API asserts the visitor's address (the factory does it on dispose).
/// </summary>
public sealed class DoubleSendHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string NewToken = "Zk9_-qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq";
    private const string LostLinkPath = "/p/paperplane/lost-link";
    private const string LinkApi = "/api/customer/access-link";
    private static readonly string ReceivedNoRef = "http://localhost" + FormTestKit.ReceivedPath;

    // ---- helpers ----

    private static async Task<(HttpClient Client, string Token, string Id)> OpenContactAsync(PortalFactory factory)
    {
        var client = FormTestKit.Client(factory);
        var html = await client.GetStringAsync(FormTestKit.Path, Ct);
        return (client, FormTestKit.TokenFrom(html), FormTestKit.SubmitIdFrom(html));
    }

    private static async Task<(HttpClient Client, string Token, string Id)> OpenTicketAsync(PortalFactory factory, string path)
    {
        var client = TicketTestKit.Client(factory);
        var html = await client.GetStringAsync(path, Ct);
        return (client, FormTestKit.TokenFrom(html), FormTestKit.SubmitIdFrom(html, "Reply.SubmitId"));
    }

    private static async Task<(HttpClient Client, string Token, string Id)> OpenLostLinkAsync(PortalFactory factory)
    {
        var client = FormTestKit.Client(factory);
        var html = await client.GetStringAsync(LostLinkPath, Ct);
        return (client, FormTestKit.TokenFrom(html), FormTestKit.SubmitIdFrom(html));
    }

    private static MultipartFormDataContent Contact(string token, string id) => FormTestKit.ContactForm(token).WithSubmitId(id);

    private static MultipartFormDataContent Reply(string token, string id, string body = "Still broken.") => TicketTestKit.ReplyForm(token, body).WithSubmitId(id, "Reply.SubmitId");

    private static MultipartFormDataContent LostLink(string token, string id)
    {
        var form = new MultipartFormDataContent { { new StringContent("lost-link"), "_handler" }, { new StringContent(token), "__RequestVerificationToken" }, { new StringContent("ada@example.com"), "Form.Email" } };
        return form.WithSubmitId(id);
    }

    private static Task<HttpResponseMessage> PostContact(HttpClient client, string token, string id, CancellationToken cancellation) =>
        client.PostAsync(FormTestKit.Path, Contact(token, id), cancellation);

    private static Task<HttpResponseMessage> PostReply(HttpClient client, string path, string token, string id, CancellationToken cancellation) => client.PostAsync(path, Reply(token, id), cancellation);

    private static Task<HttpResponseMessage> PostLostLink(HttpClient client, string token, string id, CancellationToken cancellation) => client.PostAsync(LostLinkPath, LostLink(token, id), cancellation);

    private static PortalFactory Host(Action<IServiceCollection>? configure = null)
    {
        var factory = FormTestKit.Factory(configure: configure);
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        return factory;
    }

    /// <summary>The API call of <paramref name="path"/> is held until the test lets it go; <c>Arrived</c> completes when the Portal's request reached it.</summary>
    private sealed class Gate
    {
        private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Arrived => _arrived.Task;

        /// <summary>Whether the Portal's call had been canceled at the moment the answer was given.</summary>
        public bool CancelledWhenAnswered { get; private set; }

        public void Release() => _release.TrySetResult();

        public void Hold(StubApiHandler api, string path, Func<HttpResponseMessage> answer) =>
            api.OnAsync(HttpMethod.Post, path, async (_, token) =>
            {
                _arrived.TrySetResult();
                await _release.Task.WaitAsync(token);
                CancelledWhenAnswered = token.IsCancellationRequested;
                return answer();
            });
    }

    private static HttpResponseMessage Json<T>(T body, HttpStatusCode status) => StubApiHandler.JsonResponse(status, body);

    // ---- the form carries a fresh id ----

    [Fact(Timeout = 10000)]
    public async Task Each_form_renders_a_hidden_22_character_id_right_after_the_antiforgery_field_and_a_fresh_one_every_time()
    {
        await using var factory = TicketTestKit.Factory();
        using var client = TicketTestKit.Client(factory);

        var contact = new[] { await client.GetStringAsync(FormTestKit.Path, TestContext.Current.CancellationToken), await client.GetStringAsync(FormTestKit.Path, TestContext.Current.CancellationToken) };
        var lostLink = new[] { await client.GetStringAsync(LostLinkPath, TestContext.Current.CancellationToken), await client.GetStringAsync(LostLinkPath, TestContext.Current.CancellationToken) };
        var reply = new[] { await client.GetStringAsync(TicketTestKit.Path, TestContext.Current.CancellationToken), await client.GetStringAsync(TicketTestKit.Path, TestContext.Current.CancellationToken) };

        foreach (var (pages, name) in new[] { (contact, "Form.SubmitId"), (lostLink, "Form.SubmitId"), (reply, "Reply.SubmitId") })
        {
            var ids = pages.Select(page => FormTestKit.SubmitIdFrom(page, name)).ToList();
            ids.ShouldAllBe(id => SubmitIds.IsWellFormed(id));
            ids[0].ShouldNotBe(ids[1]);
            System.Text.RegularExpressions.Regex.IsMatch(pages[0], $"name=\"__RequestVerificationToken\"[^>]*/>\\s*<input type=\"hidden\" name=\"{name}\"").ShouldBeTrue("the id sits straight after the antiforgery field");
        }
    }

    [Fact(Timeout = 10000)]
    public async Task A_form_shown_again_after_an_error_carries_a_new_id_not_the_posted_one()
    {
        await using var factory = Host();
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(token, email: "").WithSubmitId(id), TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("ts-error-summary");
        var again = FormTestKit.SubmitIdFrom(html);
        SubmitIds.IsWellFormed(again).ShouldBeTrue();
        again.ShouldNotBe(id);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    [Fact(Timeout = 10000)]
    public async Task A_post_that_fails_validation_does_not_claim_its_id()
    {
        await using var factory = Host();
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;

        using var invalid = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(token, email: "").WithSubmitId(id), TestContext.Current.CancellationToken);
        using var valid = await PostContact(client, token, id, TestContext.Current.CancellationToken);

        invalid.StatusCode.ShouldBe(HttpStatusCode.OK);
        valid.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
    }

    // ---- contact ----

    [Fact(Timeout = 10000)]
    public async Task Contact_the_same_id_posted_twice_in_a_row_calls_the_api_once_and_redirects_to_the_same_place()
    {
        await using var factory = Host();
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;

        using var first = await PostContact(client, token, id, TestContext.Current.CancellationToken);
        using var second = await PostContact(client, token, id, TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.Found);
        second.StatusCode.ShouldBe(HttpStatusCode.Found);
        second.Headers.Location.ShouldBe(first.Headers.Location);
        first.Headers.Location!.Query.ShouldStartWith("?ref=");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Contact_two_posts_at_the_same_time_make_one_api_call_and_both_redirect_to_the_same_place()
    {
        var gate = new Gate();
        await using var factory = Host();
        gate.Hold(factory.Api, FormTestKit.ApiTicketsPath, () => Json(FormTestKit.Created(), HttpStatusCode.Created));
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;

        var first = PostContact(client, token, id, TestContext.Current.CancellationToken);
        await gate.Arrived.WaitAsync(TestContext.Current.CancellationToken);
        var second = PostContact(client, token, id, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        gate.Release();
        using var firstResponse = await first;
        using var secondResponse = await second;

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Found);
        secondResponse.Headers.Location.ShouldBe(firstResponse.Headers.Location);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Contact_a_first_post_the_browser_aborted_still_completes_its_write_and_the_repeat_goes_to_its_result()
    {
        var gate = new Gate();
        await using var factory = Host();
        gate.Hold(factory.Api, FormTestKit.ApiTicketsPath, () => Json(FormTestKit.Created(), HttpStatusCode.Created));
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;
        using var abort = new CancellationTokenSource();

        var first = PostContact(client, token, id, abort.Token);
        await gate.Arrived.WaitAsync(TestContext.Current.CancellationToken);
        await abort.CancelAsync();
        var second = PostContact(client, token, id, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        gate.Release();
        using var response = await second;
        await Should.ThrowAsync<OperationCanceledException>(() => first);

        gate.CancelledWhenAnswered.ShouldBeFalse("the write runs on its own token, which the browser cannot cancel");
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.Query.ShouldStartWith("?ref="); // the repeat goes to the first request's result: the received page with its reference
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Contact_when_the_first_write_times_out_the_repeat_goes_to_the_received_page_without_a_reference_and_never_writes()
    {
        await using var factory = Host(services => services.AddSingleton(new SubmitGuard(SubmitGuard.Lifetime, TimeSpan.FromMilliseconds(300), 100)));
        factory.Api.OnAsync(HttpMethod.Post, FormTestKit.ApiTicketsPath, async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;

        using var first = await PostContact(client, token, id, TestContext.Current.CancellationToken);
        using var second = await PostContact(client, token, id, TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("We could not send that just now.");
        second.StatusCode.ShouldBe(HttpStatusCode.Found);
        second.Headers.Location!.ToString().ShouldBe(ReceivedNoRef);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
    }

    [Theory(Timeout = 10000)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Contact_a_failure_releases_the_claim_so_a_retry_with_the_same_id_calls_the_api_again(HttpStatusCode failure)
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, FormTestKit.ApiTicketsPath, failure, "x", "no");
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;

        using var failed = await PostContact(client, token, id, TestContext.Current.CancellationToken);
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        using var retry = await PostContact(client, token, id, TestContext.Current.CancellationToken);

        failed.StatusCode.ShouldNotBe(HttpStatusCode.Found);
        retry.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(2);
    }

    [Fact(Timeout = 10000)]
    public async Task Contact_a_repeat_that_was_waiting_when_the_first_failed_gets_the_same_failure_and_sends_nothing()
    {
        var gate = new Gate();
        await using var factory = Host();
        gate.Hold(factory.Api, FormTestKit.ApiTicketsPath, () => StubApiHandler.Problem(HttpStatusCode.TooManyRequests, "rate-limited", "slow down"));
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;

        var first = PostContact(client, token, id, TestContext.Current.CancellationToken);
        await gate.Arrived.WaitAsync(TestContext.Current.CancellationToken);
        var second = PostContact(client, token, id, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        gate.Release();
        using var firstResponse = await first;
        using var secondResponse = await second;

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
    }

    [Theory(Timeout = 10000)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AbC-_0123456789AbC-_0=")]
    public async Task Contact_a_missing_or_malformed_id_is_not_guarded(string? id)
    {
        await using var factory = Host();
        var (client, token, _) = await OpenContactAsync(factory);
        using var _c = client;

        for (var i = 0; i < 2; i++)
        {
            var form = FormTestKit.ContactForm(token);
            if (id is not null)
            {
                form.WithSubmitId(id);
            }

            using var response = await client.PostAsync(FormTestKit.Path, form, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.Found);
        }

        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(2);
    }

    // ---- an id belongs to one form, one product and one ticket ----

    [Fact(Timeout = 10000)]
    public async Task An_id_from_the_contact_form_cannot_claim_the_lost_link_form_and_the_other_way_round()
    {
        await using var factory = Host();
        factory.Api.OnStatus(HttpMethod.Post, LinkApi, HttpStatusCode.Accepted);
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;
        var lostToken = FormTestKit.TokenFrom(await client.GetStringAsync(LostLinkPath, TestContext.Current.CancellationToken));

        using var contact = await PostContact(client, token, id, TestContext.Current.CancellationToken);
        using var lost = await PostLostLink(client, lostToken, id, TestContext.Current.CancellationToken);
        using var lostAgain = await PostLostLink(client, lostToken, id, TestContext.Current.CancellationToken);

        contact.StatusCode.ShouldBe(HttpStatusCode.Found);
        lost.StatusCode.ShouldBe(HttpStatusCode.Found);
        lostAgain.Headers.Location.ShouldBe(lost.Headers.Location);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
        factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(1, "the lost-link claim is its own: one email, though the id was also used on the contact form");
    }

    [Fact(Timeout = 10000)]
    public async Task An_id_from_one_product_cannot_claim_another_products_contact_form()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/other", FormTestKit.Product("Other") with { Key = "other" });
        factory.Api.OnJson(HttpMethod.Post, "/api/public/products/other/tickets", FormTestKit.Created("OTH-1"), HttpStatusCode.Created);
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;
        var otherToken = FormTestKit.TokenFrom(await client.GetStringAsync("/p/other/contact", TestContext.Current.CancellationToken));

        using var paperplane = await PostContact(client, token, id, TestContext.Current.CancellationToken);
        using var other = await client.PostAsync("/p/other/contact", FormTestKit.ContactForm(otherToken).WithSubmitId(id), TestContext.Current.CancellationToken);

        paperplane.Headers.Location!.ToString().ShouldContain("/p/paperplane/contact/received");
        other.Headers.Location!.ToString().ShouldContain("/p/other/contact/received");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
        factory.Api.Count(HttpMethod.Post, "/api/public/products/other/tickets").ShouldBe(1);
    }

    // ---- lost link ----

    [Fact(Timeout = 10000)]
    public async Task Lost_link_the_same_id_twice_asks_for_one_email_and_both_posts_go_to_the_sent_page()
    {
        await using var factory = Host();
        factory.Api.OnStatus(HttpMethod.Post, LinkApi, HttpStatusCode.Accepted);
        var (client, token, id) = await OpenLostLinkAsync(factory);
        using var _ = client;

        using var first = await PostLostLink(client, token, id, TestContext.Current.CancellationToken);
        using var second = await PostLostLink(client, token, id, TestContext.Current.CancellationToken);

        first.Headers.Location!.ToString().ShouldBe("http://localhost/p/paperplane/lost-link?sent=1");
        second.Headers.Location.ShouldBe(first.Headers.Location);
        factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Lost_link_two_posts_at_the_same_time_and_an_aborted_first_post_send_one_email()
    {
        var gate = new Gate();
        await using var factory = Host();
        gate.Hold(factory.Api, LinkApi, () => new HttpResponseMessage(HttpStatusCode.Accepted));
        var (client, token, id) = await OpenLostLinkAsync(factory);
        using var _ = client;
        using var abort = new CancellationTokenSource();

        var first = client.PostAsync(LostLinkPath, LostLink(token, id), abort.Token);
        await gate.Arrived.WaitAsync(TestContext.Current.CancellationToken);
        await abort.CancelAsync();
        var second = PostLostLink(client, token, id, TestContext.Current.CancellationToken);
        var third = PostLostLink(client, token, id, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        gate.Release();
        using var secondResponse = await second;
        using var thirdResponse = await third;
        await Should.ThrowAsync<OperationCanceledException>(() => first);

        gate.CancelledWhenAnswered.ShouldBeFalse();
        secondResponse.Headers.Location!.ToString().ShouldBe("http://localhost/p/paperplane/lost-link?sent=1");
        thirdResponse.Headers.Location.ShouldBe(secondResponse.Headers.Location);
        factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Lost_link_a_rate_limit_releases_the_claim_so_a_retry_sends()
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, LinkApi, HttpStatusCode.TooManyRequests, "rate-limited", "slow");
        var (client, token, id) = await OpenLostLinkAsync(factory);
        using var _ = client;

        using var limited = await PostLostLink(client, token, id, TestContext.Current.CancellationToken);
        factory.Api.OnStatus(HttpMethod.Post, LinkApi, HttpStatusCode.Accepted);
        using var retry = await PostLostLink(client, token, id, TestContext.Current.CancellationToken);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        retry.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(2);
    }

    // ---- reply ----

    private static PortalFactory ReplyHost(CustomerTicketDto? ticket = null, CustomerReplyResponse? answer = null)
    {
        var factory = TicketTestKit.Factory(ticket);
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, answer ?? new CustomerReplyResponse("PAP-42", Guid.NewGuid(), false, null), HttpStatusCode.Created);
        return factory;
    }

    [Fact(Timeout = 10000)]
    public async Task Reply_the_same_id_twice_sends_one_reply_and_both_posts_go_back_to_the_ticket()
    {
        await using var factory = ReplyHost();
        var (client, token, id) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _ = client;

        using var first = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        using var second = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);

        first.Headers.Location!.ToString().ShouldBe("http://localhost" + TicketTestKit.Path);
        second.Headers.Location.ShouldBe(first.Headers.Location);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Reply_on_a_closed_ticket_a_repeat_goes_to_the_same_follow_up_page_and_starts_one_follow_up()
    {
        var link = "https://help.example.com/t/" + NewToken;
        await using var factory = ReplyHost(TicketTestKit.Ticket("Closed"), new CustomerReplyResponse("PAP-43", Guid.NewGuid(), true, link));
        var (client, token, id) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _ = client;

        using var first = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        using var second = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);

        first.Headers.Location!.ToString().ShouldBe("http://localhost/t/" + NewToken);
        second.Headers.Location.ShouldBe(first.Headers.Location);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Reply_on_a_closed_ticket_two_posts_at_the_same_time_and_an_aborted_first_post_start_one_follow_up()
    {
        var gate = new Gate();
        var link = "https://help.example.com/t/" + NewToken;
        await using var factory = ReplyHost(TicketTestKit.Ticket("Closed"));
        gate.Hold(factory.Api, TicketTestKit.ReplyApi, () => Json(new CustomerReplyResponse("PAP-43", Guid.NewGuid(), true, link), HttpStatusCode.Created));
        var (client, token, id) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _ = client;
        using var abort = new CancellationTokenSource();

        var first = client.PostAsync(TicketTestKit.Path, Reply(token, id), abort.Token);
        await gate.Arrived.WaitAsync(TestContext.Current.CancellationToken);
        await abort.CancelAsync();
        var second = PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        var third = PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        gate.Release();
        using var secondResponse = await second;
        using var thirdResponse = await third;
        await Should.ThrowAsync<OperationCanceledException>(() => first);

        gate.CancelledWhenAnswered.ShouldBeFalse();
        secondResponse.Headers.Location!.ToString().ShouldBe("http://localhost/t/" + NewToken);
        thirdResponse.Headers.Location.ShouldBe(secondResponse.Headers.Location);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Reply_when_the_first_answer_is_unknown_a_repeat_goes_back_to_the_ticket_page_and_never_sends()
    {
        await using var factory = TicketTestKit.Factory(TicketTestKit.Ticket("Closed"), configure: services => services.AddSingleton(new SubmitGuard(SubmitGuard.Lifetime, TimeSpan.FromMilliseconds(300), 100)));
        factory.Api.OnAsync(HttpMethod.Post, TicketTestKit.ReplyApi, async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var (client, token, id) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _ = client;

        using var first = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        using var second = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        second.Headers.Location!.ToString().ShouldBe("http://localhost" + TicketTestKit.Path);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Reply_a_follow_up_whose_link_cannot_be_read_shows_the_confirmation_for_the_repeat_too_and_sends_once()
    {
        await using var factory = ReplyHost(TicketTestKit.Ticket("Closed"), new CustomerReplyResponse("PAP-43", Guid.NewGuid(), true, "not a link"));
        var (client, token, id) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _ = client;

        using var first = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        using var second = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), "the same confirmation page, whichever post asks");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
    }

    [Theory(Timeout = 10000)]
    [InlineData(HttpStatusCode.Conflict, "reply-conflict")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate-limited")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "x")]
    public async Task Reply_a_failure_releases_the_claim_so_a_retry_with_the_same_id_sends(HttpStatusCode failure, string code)
    {
        await using var factory = ReplyHost();
        factory.Api.OnProblem(HttpMethod.Post, TicketTestKit.ReplyApi, failure, code, "no");
        var (client, token, id) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _ = client;

        using var failed = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, new CustomerReplyResponse("PAP-42", Guid.NewGuid(), false, null), HttpStatusCode.Created);
        using var retry = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);

        failed.StatusCode.ShouldNotBe(HttpStatusCode.Found);
        retry.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(2);
    }

    [Fact(Timeout = 10000)]
    public async Task Reply_an_id_cannot_claim_a_reply_to_another_ticket()
    {
        await using var factory = ReplyHost();
        var (client, token, id) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _ = client;
        var otherPath = "/t/" + TicketTestKit.OtherToken;
        var otherToken = FormTestKit.TokenFrom(await client.GetStringAsync(otherPath, TestContext.Current.CancellationToken));

        using var mine = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        using var theirs = await PostReply(client, otherPath, otherToken, id, TestContext.Current.CancellationToken);

        mine.Headers.Location!.ToString().ShouldBe("http://localhost" + TicketTestKit.Path);
        theirs.Headers.Location!.ToString().ShouldBe("http://localhost" + otherPath);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(2);
    }

    [Fact(Timeout = 10000)]
    public async Task Reply_a_post_with_no_id_is_not_guarded()
    {
        await using var factory = ReplyHost();
        var (client, token, _) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _c = client;

        using var first = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), TestContext.Current.CancellationToken);
        using var second = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.Found);
        second.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(2);
    }

    // ---- secrecy ----

    [Fact(Timeout = 10000)]
    public async Task No_log_event_at_any_level_carries_a_follow_ups_token_a_reference_or_an_id()
    {
        var link = "https://help.example.com/t/" + NewToken;
        await using var factory = FormTestKit.Factory(settings: PortalFactory.VerboseLogging);
        factory.Api.OnJson(HttpMethod.Get, TicketTestKit.TicketApi, TicketTestKit.Ticket("Closed"));
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, new CustomerReplyResponse("PAP-43", Guid.NewGuid(), true, link), HttpStatusCode.Created);
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        var (contactClient, contactToken, contactId) = await OpenContactAsync(factory);
        using var _ = contactClient;
        var (replyClient, replyToken, replyId) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var __ = replyClient;

        using var contact = await PostContact(contactClient, contactToken, contactId, TestContext.Current.CancellationToken);
        using var contactRepeat = await PostContact(contactClient, contactToken, contactId, TestContext.Current.CancellationToken);
        using var reply = await PostReply(replyClient, TicketTestKit.Path, replyToken, replyId, TestContext.Current.CancellationToken);
        using var replyRepeat = await PostReply(replyClient, TicketTestKit.Path, replyToken, replyId, TestContext.Current.CancellationToken);
        await replyClient.GetStringAsync(reply.Headers.Location!.PathAndQuery, TestContext.Current.CancellationToken);
        var reference = contact.Headers.Location!.Query["?ref=".Length..];

        reference.Length.ShouldBeGreaterThan(20);
        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect");
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text =>
            !text.Contains(NewToken, StringComparison.Ordinal)
            && !text.Contains(TicketTestKit.Token, StringComparison.Ordinal)
            && !text.Contains(Uri.UnescapeDataString(reference), StringComparison.Ordinal)
            && !text.Contains(reference, StringComparison.Ordinal)
            && !text.Contains(contactId, StringComparison.Ordinal)
            && !text.Contains(replyId, StringComparison.Ordinal));
    }

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);
}
```

`tests/TechStrap.Portal.Tests/Forms/SubmitGuardTests.cs` (new)

```csharp
using SyntaxCircus.Common;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// The double-send guard on its own (D-045 09d addendum, Review Focus 1 and 2): one write per id however the posts arrive, a repeat goes where the first went, an aborted first request cannot cut the write short, a
/// failure lets a retry through, an unknown answer sends a repeat to the fallback, and an id belongs to one form and one subject only.
/// </summary>
public sealed class SubmitGuardTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly SubmitTarget Fallback = new("/fallback");
    private static readonly string Id = SubmitIds.New();

    private static SubmitKey Key(string form = "contact", string scope = "paperplane", string? id = null) => SubmitKey.TryCreate(form, scope, id ?? Id).ShouldNotBeNull();

    private static Task<Result<SubmitTarget>> Ok(string path) => Task.FromResult(Result<SubmitTarget>.Success(new SubmitTarget(path)));

    private static Task<Result<SubmitTarget>> Fail(string code) => Task.FromResult(Result<SubmitTarget>.Failure(new ResultError(code, "failed", ResultErrorKind.Failure)));

    /// <summary>A write that counts its calls and finishes when the test says so.</summary>
    private sealed class GatedWrite(string path)
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public CancellationToken Token { get; private set; }

        public void Release() => _release.SetResult();

        public async Task<Result<SubmitTarget>> RunAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            Token = token;
            await _release.Task.WaitAsync(token);
            return Result<SubmitTarget>.Success(new SubmitTarget(path));
        }
    }

    // ---- ids and keys ----

    [Fact]
    public void An_id_is_22_url_safe_characters_and_never_repeats()
    {
        var ids = Enumerable.Range(0, 2000).Select(_ => SubmitIds.New()).ToList();

        ids.ShouldAllBe(id => id.Length == SubmitIds.Length && SubmitIds.IsWellFormed(id));
        ids.Distinct().Count().ShouldBe(ids.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AbC-_0123456789AbC-_01")]
    [InlineData("AbC-_0123456789AbC-_012")]
    [InlineData("AbC-_0123456789AbC-_0=")]
    [InlineData("AbC-_0123456789AbC-_0 ")]
    [InlineData("AbC-_0123456789AbC-_0\n")]
    public void Only_an_id_of_exactly_the_made_shape_is_well_formed(string? id)
    {
        SubmitIds.IsWellFormed(id).ShouldBe(id is "AbC-_0123456789AbC-_01");
        (SubmitKey.TryCreate("contact", "paperplane", id) is not null).ShouldBe(id is "AbC-_0123456789AbC-_01");
    }

    [Fact]
    public void A_key_depends_on_the_form_the_subject_and_the_id_and_holds_none_of_them()
    {
        var key = Key("reply", "AbC-_0123456789AbC-_0123456789AbC-_01234567");

        Key("reply", "AbC-_0123456789AbC-_0123456789AbC-_01234567").Hash.ShouldBe(key.Hash);
        Key("contact", "AbC-_0123456789AbC-_0123456789AbC-_01234567").Hash.ShouldNotBe(key.Hash);
        Key("reply", "Zk9_-qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq").Hash.ShouldNotBe(key.Hash);
        Key("reply", "AbC-_0123456789AbC-_0123456789AbC-_01234567", SubmitIds.New()).Hash.ShouldNotBe(key.Hash);
        key.Hash.ShouldNotContain("AbC-_0123456789");
        key.Hash.ShouldNotContain(Id);
    }

    // ---- one write per id ----

    [Fact]
    public async Task No_key_means_no_guard_and_the_write_runs_on_the_requests_own_token()
    {
        using var guard = new SubmitGuard();
        using var source = new CancellationTokenSource();
        var seen = new List<CancellationToken>();

        for (var i = 0; i < 2; i++)
        {
            var outcome = await guard.RunAsync(null, Fallback, token =>
            {
                seen.Add(token);
                return Ok("/first");
            }, source.Token);
            outcome.Status.ShouldBe(SubmitStatus.Done);
        }

        seen.Count.ShouldBe(2);
        seen.ShouldAllBe(token => token == source.Token);
    }

    [Fact(Timeout = 10000)]
    public async Task A_repeat_after_the_first_finished_goes_where_the_first_went_without_a_second_write()
    {
        using var guard = new SubmitGuard();
        var calls = 0;

        var first = await guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/received?ref=one"); }, TestContext.Current.CancellationToken);
        var second = await guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/received?ref=two"); }, TestContext.Current.CancellationToken);

        calls.ShouldBe(1);
        first.Target.Path.ShouldBe("/received?ref=one");
        second.Status.ShouldBe(SubmitStatus.Done);
        second.Target.Path.ShouldBe("/received?ref=one");
    }

    [Fact(Timeout = 10000)]
    public async Task Concurrent_repeats_make_one_write_and_all_go_to_the_same_place()
    {
        using var guard = new SubmitGuard();
        var write = new GatedWrite("/received?ref=one");

        var posts = Enumerable.Range(0, 8).Select(_ => Task.Run(() => guard.RunAsync(Key(), Fallback, write.RunAsync, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)).ToList();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        write.Calls.ShouldBe(1, "one claim, one write, however many posts are waiting");
        write.Release();
        var outcomes = await Task.WhenAll(posts);

        write.Calls.ShouldBe(1);
        outcomes.ShouldAllBe(outcome => outcome.Status == SubmitStatus.Done && outcome.Target.Path == "/received?ref=one");
    }

    [Fact(Timeout = 10000)]
    public async Task The_write_runs_on_its_own_token_that_the_request_cannot_cancel_and_the_first_request_still_waits_for_it()
    {
        using var guard = new SubmitGuard();
        var write = new GatedWrite("/received?ref=one");
        using var aborted = new CancellationTokenSource();

        var first = guard.RunAsync(Key(), Fallback, write.RunAsync, aborted.Token);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await aborted.CancelAsync();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        write.Token.IsCancellationRequested.ShouldBeFalse("the browser going away must not cancel a write the API may already have");
        write.Token.CanBeCanceled.ShouldBeTrue("it has its own timeout");
        first.IsCompleted.ShouldBeFalse("the first request keeps waiting so what it is reading stays valid");
        write.Release();
        (await first).Target.Path.ShouldBe("/received?ref=one");
    }

    [Fact(Timeout = 10000)]
    public async Task A_repeat_that_arrives_after_the_first_was_aborted_gets_the_first_ones_result()
    {
        using var guard = new SubmitGuard();
        var write = new GatedWrite("/received?ref=one");
        using var aborted = new CancellationTokenSource();
        var first = guard.RunAsync(Key(), Fallback, write.RunAsync, aborted.Token);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await aborted.CancelAsync();

        var second = guard.RunAsync(Key(), Fallback, _ => throw new InvalidOperationException("a repeat must not write"), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        write.Release();

        (await second).Target.Path.ShouldBe("/received?ref=one");
        (await first).Target.Path.ShouldBe("/received?ref=one");
        write.Calls.ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task A_repeat_whose_own_request_is_aborted_stops_waiting_and_the_write_is_not_affected()
    {
        using var guard = new SubmitGuard();
        var write = new GatedWrite("/received?ref=one");
        var first = guard.RunAsync(Key(), Fallback, write.RunAsync, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        using var gone = new CancellationTokenSource();

        var second = guard.RunAsync(Key(), Fallback, _ => throw new InvalidOperationException("a repeat must not write"), gone.Token);
        await gone.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => second);
        write.Release();
        (await first).Target.Path.ShouldBe("/received?ref=one");
    }

    // ---- failures ----

    [Fact(Timeout = 10000)]
    public async Task A_failure_releases_the_claim_so_a_retry_with_the_same_id_writes_again()
    {
        using var guard = new SubmitGuard();
        var calls = 0;

        var failed = await guard.RunAsync(Key(), Fallback, _ => { calls++; return Fail("rate-limited"); }, TestContext.Current.CancellationToken);
        var retry = await guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/received"); }, TestContext.Current.CancellationToken);

        calls.ShouldBe(2);
        failed.Status.ShouldBe(SubmitStatus.Failed);
        failed.Errors[0].Code.ShouldBe("rate-limited");
        retry.Status.ShouldBe(SubmitStatus.Done);
    }

    [Fact(Timeout = 10000)]
    public async Task A_repeat_that_was_already_waiting_gets_the_same_failure_and_does_not_write()
    {
        using var guard = new SubmitGuard();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var first = guard.RunAsync(Key(), Fallback, async _ => { calls++; await release.Task; return await Fail("api-unavailable"); }, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        var second = guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/never"); }, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        release.SetResult();

        (await first).Status.ShouldBe(SubmitStatus.Failed);
        var repeated = await second;
        repeated.Status.ShouldBe(SubmitStatus.Failed);
        repeated.Errors[0].Code.ShouldBe("api-unavailable");
        calls.ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task A_write_that_times_out_is_unknown_and_a_repeat_goes_to_the_fallback_without_writing()
    {
        using var guard = new SubmitGuard(SubmitGuard.Lifetime, TimeSpan.FromMilliseconds(200), 100);
        var calls = 0;

        var first = await guard.RunAsync(Key(), Fallback, async token => { calls++; await Task.Delay(Timeout.Infinite, token); return await Ok("/never"); }, TestContext.Current.CancellationToken);
        var repeat = await guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/never"); }, TestContext.Current.CancellationToken);

        calls.ShouldBe(1);
        first.Status.ShouldBe(SubmitStatus.Unknown);
        repeat.Status.ShouldBe(SubmitStatus.Done);
        repeat.Target.Path.ShouldBe("/fallback");
    }

    [Fact(Timeout = 10000)]
    public async Task A_write_that_throws_keeps_the_claim_as_unknown_and_the_first_request_sees_the_exception()
    {
        using var guard = new SubmitGuard();
        var calls = 0;

        await Should.ThrowAsync<InvalidOperationException>(() => guard.RunAsync(Key(), Fallback, _ => { calls++; throw new InvalidOperationException("boom"); }, TestContext.Current.CancellationToken));
        var repeat = await guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/never"); }, TestContext.Current.CancellationToken);

        calls.ShouldBe(1);
        repeat.Target.Path.ShouldBe("/fallback");
    }

    // ---- scope ----

    [Fact(Timeout = 10000)]
    public async Task An_id_cannot_cross_forms_products_or_tickets()
    {
        using var guard = new SubmitGuard();
        var calls = 0;
        Task<Result<SubmitTarget>> Write(CancellationToken _)
        {
            calls++;
            return Ok("/x");
        }

        await guard.RunAsync(Key("contact", "paperplane"), Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(Key("lost-link", "paperplane"), Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(Key("contact", "other"), Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(Key("reply", "AbC-_0123456789AbC-_0123456789AbC-_01234567"), Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(Key("reply", "Zk9_-qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq"), Fallback, Write, TestContext.Current.CancellationToken);

        calls.ShouldBe(5, "five different (form, subject) pairs with one id are five different claims");
    }

    [Fact(Timeout = 10000)]
    public async Task The_product_key_is_compared_without_regard_to_case()
    {
        using var guard = new SubmitGuard();
        var calls = 0;

        await guard.RunAsync(Key("contact", "Paperplane"), Fallback, _ => { calls++; return Ok("/x"); }, TestContext.Current.CancellationToken);
        await guard.RunAsync(Key("contact", "paperplane"), Fallback, _ => { calls++; return Ok("/x"); }, TestContext.Current.CancellationToken);

        calls.ShouldBe(1);
    }

    // ---- lifetime and cap ----

    [Fact(Timeout = 10000)]
    public async Task A_claim_expires_after_its_lifetime()
    {
        using var guard = new SubmitGuard(TimeSpan.FromMilliseconds(300), SubmitGuard.WriteTimeout, 100);
        var calls = 0;

        await guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/x"); }, TestContext.Current.CancellationToken);
        await Task.Delay(700, TestContext.Current.CancellationToken);
        await guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/x"); }, TestContext.Current.CancellationToken);

        calls.ShouldBe(2);
    }

    [Fact(Timeout = 10000)]
    public async Task The_cache_is_capped_and_a_full_cache_lets_a_new_post_through_unguarded_without_evicting_a_claim()
    {
        using var guard = new SubmitGuard(SubmitGuard.Lifetime, SubmitGuard.WriteTimeout, 2);
        var calls = 0;
        Task<Result<SubmitTarget>> Write(CancellationToken _)
        {
            calls++;
            return Ok("/x");
        }

        var a = Key(id: SubmitIds.New());
        var b = Key(id: SubmitIds.New());
        var c = Key(id: SubmitIds.New());
        await guard.RunAsync(a, Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(b, Fallback, Write, TestContext.Current.CancellationToken);
        var unguarded = await guard.RunAsync(c, Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(c, Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(a, Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(b, Fallback, Write, TestContext.Current.CancellationToken);

        unguarded.Status.ShouldBe(SubmitStatus.Done, "over the cap a post is not refused");
        calls.ShouldBe(4, "a, b and the two unguarded posts of c wrote; the repeats of a and b did not");
    }

    [Fact]
    public void The_guard_has_a_cache_of_its_own_and_never_logs()
    {
        // The shared IMemoryCache has no size limit and holds the sitemap (setting one would break it), and a log line could carry a target.
        var constructors = typeof(SubmitGuard).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToList();

        constructors.ShouldNotContain(typeof(Microsoft.Extensions.Caching.Memory.IMemoryCache));
        constructors.ShouldNotContain(t => t.Name.StartsWith("ILogger", StringComparison.Ordinal));
        (SubmitGuard.Lifetime < ReceivedReference.Lifetime).ShouldBeTrue("a stored reference must outlive the claim that holds it");
    }
}
```

`tests/TechStrap.Portal.Tests/Api/StubApiHandler.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Api/StubApiHandler.cs
+++ b/tests/TechStrap.Portal.Tests/Api/StubApiHandler.cs
@@ -22,7 +22,7 @@ public sealed class StubApiHandler : HttpMessageHandler
     private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
 
     private readonly List<StubApiRequest> _requests = [];
-    private readonly List<(HttpMethod Method, string Path, Func<StubApiRequest, HttpResponseMessage> Respond)> _routes = [];
+    private readonly List<(HttpMethod Method, string Path, Func<StubApiRequest, CancellationToken, Task<HttpResponseMessage>> Respond)> _routes = [];
     private readonly object _gate = new();
 
     /// <summary>Every request received so far, in order.</summary>
@@ -41,7 +41,13 @@ public sealed class StubApiHandler : HttpMessageHandler
     public int Count(HttpMethod method, string path) => Requests.Count(r => r.Method == method && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
 
     /// <summary>Answers requests for <paramref name="path"/> (path only, no query) with whatever <paramref name="respond"/> returns. A later route for the same method and path replaces an earlier one.</summary>
-    public StubApiHandler On(HttpMethod method, string path, Func<StubApiRequest, HttpResponseMessage> respond)
+    public StubApiHandler On(HttpMethod method, string path, Func<StubApiRequest, HttpResponseMessage> respond) => OnAsync(method, path, (request, _) => Task.FromResult(respond(request)));
+
+    /// <summary>
+    /// The same, for an answer a test holds back: the request is recorded when it arrives, and the answer is whatever the task gives, so a test can keep a write "in flight" while another request is made. The token is the
+    /// one the Portal's call was made with.
+    /// </summary>
+    public StubApiHandler OnAsync(HttpMethod method, string path, Func<StubApiRequest, CancellationToken, Task<HttpResponseMessage>> respond)
     {
         lock (_gate)
         {
@@ -126,14 +132,14 @@ public sealed class StubApiHandler : HttpMessageHandler
             body,
             request.Options.TryGetValue(ClientKey, out var client) ? client : null);
 
-        Func<StubApiRequest, HttpResponseMessage>? respond;
+        Func<StubApiRequest, CancellationToken, Task<HttpResponseMessage>>? respond;
         lock (_gate)
         {
             _requests.Add(seen);
             respond = _routes.FirstOrDefault(r => r.Method == seen.Method && string.Equals(r.Path, seen.Path, StringComparison.OrdinalIgnoreCase)).Respond;
         }
 
-        return respond is null ? Problem(HttpStatusCode.NotFound, "stub-not-configured", $"{seen.Method} {seen.Path} is not configured in the stub API.") : respond(seen);
+        return respond is null ? Problem(HttpStatusCode.NotFound, "stub-not-configured", $"{seen.Method} {seen.Path} is not configured in the stub API.") : await respond(seen, cancellationToken);
     }
 
     private static string? Header(HttpRequestMessage request, string name) => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
```

`tests/TechStrap.Portal.Tests/Forms/ContactPageHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Forms/ContactPageHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/Forms/ContactPageHostTests.cs
@@ -155,7 +155,8 @@ public sealed class ContactPageHostTests
         html.ShouldContain("name=\"Form.Subject\" type=\"text\" class=\"form-control\" value=\"Printer jam\" maxlength=\"200\"");
         html.ShouldContain("name=\"Form.Name\" type=\"text\" class=\"form-control\" value=\"Jane Doe\" maxlength=\"100\"");
         html.ShouldContain("name=\"Form.Email\" type=\"email\" class=\"form-control\" value=\"jane@example.com\" maxlength=\"320\"");
-        html.ShouldNotContain("type=\"hidden\" name=\"Form.", Case.Sensitive, "no hidden field carries prefill data");
+        // The one hidden Form field is the double-send id (a random value, never prefill data).
+        System.Text.RegularExpressions.Regex.IsMatch(html, "type=\"hidden\" name=\"Form\\.(?!SubmitId\")").ShouldBeFalse("no hidden field carries prefill data");
         html.ShouldNotContain("value=\"Printer jam\" type=\"hidden\"");
         factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0, "a prefill never submits anything");
     }
@@ -185,9 +186,10 @@ public sealed class ContactPageHostTests
         extra.ShouldNotContain("orbitly");
         extra.ShouldNotContain("AbC-_0123456789");
         extra.ShouldNotContain("spam");
-        // The same page, token for token (the antiforgery value differs every time).
-        System.Text.RegularExpressions.Regex.Replace(extra, "value=\"CfDJ[^\"]+\"", "value=\"T\"")
-            .ShouldBe(System.Text.RegularExpressions.Regex.Replace(plain, "value=\"CfDJ[^\"]+\"", "value=\"T\""));
+        // The same page, token for token (the antiforgery value and the double-send id differ every time).
+        static string Same(string page) => System.Text.RegularExpressions.Regex.Replace(
+            System.Text.RegularExpressions.Regex.Replace(page, "value=\"CfDJ[^\"]+\"", "value=\"T\""), "(name=\"Form.SubmitId\" value=\")[^\"]+", "$1I");
+        Same(extra).ShouldBe(Same(plain));
         factory.Api.Count(HttpMethod.Get, "/api/public/products/paperplane").ShouldBe(2, "the product is the page's own, never the query's");
     }
 
```

`tests/TechStrap.Portal.Tests/Forms/FormTestKit.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Forms/FormTestKit.cs
+++ b/tests/TechStrap.Portal.Tests/Forms/FormTestKit.cs
@@ -98,6 +98,21 @@ internal static class FormTestKit
 
     public static SubmitTicketResponse Created(string number = "PAP-42") => new(number, null, []);
 
+    /// <summary>The one-time id a rendered form carries in its hidden input <paramref name="name"/> (<c>Form.SubmitId</c> or <c>Reply.SubmitId</c>).</summary>
+    public static string SubmitIdFrom(string html, string name = "Form.SubmitId")
+    {
+        var match = Regex.Match(html, $"<input type=\"hidden\" name=\"{Regex.Escape(name)}\" value=\"([^\"]*)\"");
+        match.Success.ShouldBeTrue($"the form must carry a hidden {name} field");
+        return match.Groups[1].Value;
+    }
+
+    /// <summary>The form with the one-time id a browser would post back from the hidden input <paramref name="name"/>.</summary>
+    public static MultipartFormDataContent WithSubmitId(this MultipartFormDataContent form, string id, string name = "Form.SubmitId")
+    {
+        form.Add(new StringContent(id), name);
+        return form;
+    }
+
     private static void Add(MultipartFormDataContent form, string name, string? value)
     {
         if (value is not null)
```

`tests/TechStrap.Portal.Tests/Tickets/TicketReplyHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Tickets/TicketReplyHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/Tickets/TicketReplyHostTests.cs
@@ -14,7 +14,7 @@ namespace TechStrap.Portal.Tests.Tickets;
 /// <summary>
 /// P09-T09 at the host: the reply form on the ticket page. A reply on an open ticket is sent through the write client (once, with the token as a header) and redirects to the same page; a reply on a Closed ticket
 /// redirects to the follow-up's own page, whose token is read from the API's link and checked, and never leaves the site (Review Focus 1); a link that cannot be read gives a generic confirmation and no redirect.
-/// Validation, 409, 413, 415, 429 and outages each have their own message and the text is kept. Antiforgery is enforced (Review Focus 3), the size limit applies before the form is read, and the redirect after a post guards a refresh (not a double click: that is a known gap, deferred to 09c, so P09-T09 stays open). Every call forwards the visitor's address (Review Focus 5).
+/// Validation, 409, 413, 415, 429 and outages each have their own message and the text is kept. Antiforgery is enforced (Review Focus 3), the size limit applies before the form is read, and the redirect after a post guards a refresh, and a double click is guarded by the form's one-time id (<c>DoubleSendHostTests</c>, D-045 09d addendum). Every call forwards the visitor's address (Review Focus 5).
 /// </summary>
 public sealed class TicketReplyHostTests
 {
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build tests/TechStrap.Portal.Tests -c Release
```

Expected: FAIL to compile (the guard does not exist):

```text
5 errors, the first five:
tests\TechStrap.Portal.Tests\Forms\SubmitGuardTests.cs(16,20): error CS0246: The type or namespace name 'SubmitKey' could not be found (are you missing a using directive or an assembly reference?)
tests\TechStrap.Portal.Tests\Forms\SubmitGuardTests.cs(18,32): error CS0246: The type or namespace name 'SubmitTarget' could not be found (are you missing a using directive or an assembly reference?)
tests\TechStrap.Portal.Tests\Forms\SubmitGuardTests.cs(20,32): error CS0246: The type or namespace name 'SubmitTarget' could not be found (are you missing a using directive or an assembly reference?)
tests\TechStrap.Portal.Tests\Forms\SubmitGuardTests.cs(13,29): error CS0246: The type or namespace name 'SubmitTarget' could not be found (are you missing a using directive or an assembly reference?)
tests\TechStrap.Portal.Tests\Forms\SubmitGuardTests.cs(34,34): error CS0246: The type or namespace name 'SubmitTarget' could not be found (are you missing a using directive or an assembly reference?)
```

- [ ] **Step 3: Write the implementation**

`SubmitGuard` is the heart of it. The first request with an id claims it under a lock (an entry of size 1 in the guard's own `MemoryCache`, never evicted, expiring after the lifetime) and runs the write on a token with its own timeout; it waits for that write without its abort token. A repeat finds the claim and waits for the claim's task (with its own abort token) or reads it. A failed write removes the claim before it tells anybody. When the cache is full the claim is not stored and the post goes through unguarded.

`src/TechStrap.Portal/Components/Ui/FormGuard.razor` (new)

```razor
<input type="hidden" name="@Name" value="@_id" />

@code {
    // A fresh id for every render of the form (a component is made anew for each request, and a page that shows errors after a failed post must carry a new id, never the posted one).
    private readonly string _id = SubmitIds.New();

    /// <summary>The form-binder name of the field (<c>Form.SubmitId</c>, <c>Reply.SubmitId</c>), which binds the property of the same name on the form's view model.</summary>
    [Parameter, EditorRequired]
    public string Name { get; set; } = string.Empty;
}
```

`src/TechStrap.Portal/Forms/SubmitGuard.cs` (new)

```csharp
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using SyntaxCircus.Common;

namespace TechStrap.Portal.Forms;

/// <summary>Where a visitor goes after a guarded write. A null <see cref="Path"/> means "nowhere: show the confirmation in place" (a follow-up whose link could not be read).</summary>
public readonly record struct SubmitTarget(string? Path)
{
    public static SubmitTarget None { get; } = new(null);
}

/// <summary>
/// What a guarded form post is identified by: the form's name, what the post is about (the product key, or the ticket's access token) and the id the form carried, hashed together. Only the hash is kept, so the
/// cache never holds a token, and an id made for one form can never claim another form, product or ticket.
/// </summary>
public readonly record struct SubmitKey
{
    private SubmitKey(string hash) => Hash = hash;

    public string Hash { get; }

    /// <summary>The key for a post, or null when the id is missing or malformed (the post is then not guarded and goes through as it always did).</summary>
    public static SubmitKey? TryCreate(string form, string scope, string? id)
    {
        if (!SubmitIds.IsWellFormed(id))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{form}\0{scope.ToLowerInvariant()}\0{id}"));
        return new SubmitKey(Convert.ToBase64String(bytes));
    }
}

public enum SubmitStatus
{
    /// <summary>The write succeeded (this request's, or the first request's that this one repeats), or the first request's result is unknown and this is the fallback: go to <see cref="SubmitOutcome.Target"/>.</summary>
    Done,

    /// <summary>The write failed; <see cref="SubmitOutcome.Errors"/> says how, exactly as an unguarded call would have.</summary>
    Failed,

    /// <summary>Only for the request that made the write: it timed out, so nobody knows whether it happened. The page shows its calm "unavailable" notice, and a repeat of the id is sent to the fallback instead of writing again.</summary>
    Unknown,
}

public sealed record SubmitOutcome(SubmitStatus Status, SubmitTarget Target, IReadOnlyList<ResultError> Errors)
{
    internal static SubmitOutcome Done(SubmitTarget target) => new(SubmitStatus.Done, target, []);

    internal static SubmitOutcome Failed(IReadOnlyList<ResultError> errors) => new(SubmitStatus.Failed, default, errors);

    internal static SubmitOutcome Unknown { get; } = new(SubmitStatus.Unknown, default, []);
}

/// <summary>
/// Makes one visitor's double click send once (D-045 09d addendum, PHASE-09 T09). The first post with an id claims it, atomically, and does the write; a repeat of the id never writes: it waits for the first's answer
/// when that is still running, or reads the stored one, and goes where the first goes (or to a fallback when the answer is unknown).
/// <list type="bullet">
/// <item>The write runs on its own token with a timeout, never on the request's: a real double click aborts the first POST, but the API may already have the ticket, and a request that stops waiting could also lose the
/// files it is reading (a precaution, not reproduced). So the first request waits for its own write without its abort token (bounded by the timeout), and the write cannot be cut short by the browser.</item>
/// <item>A failure releases the claim, so a retry writes. A repeat that was already waiting gets the same failure, never a second write. A timeout or a fault keeps the claim as "unknown".</item>
/// <item>The cache is this class's own <see cref="MemoryCache"/> with a size cap, never the shared one (the sitemap cache lives there and has no size). A claim lives <see cref="Lifetime"/>, shorter than the
/// reference it may hold (<see cref="ReceivedReference.Lifetime"/>). When the cap is reached a new post is simply not guarded (the old behavior), never refused.</item>
/// <item>The stored target can hold another ticket's access token (a follow-up). It lives in memory only, for <see cref="Lifetime"/>, and this class never logs and never takes a logger.</item>
/// </list>
/// Per instance: a restart or a second Portal replica forgets the claims (documented in PORTAL-APP.md, like the sitemap cache).
/// </summary>
public sealed class SubmitGuard : IDisposable
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);

    public const int MaxClaims = 10_000;

    private readonly MemoryCache _claims;
    private readonly TimeSpan _lifetime;
    private readonly TimeSpan _writeTimeout;
    private readonly Lock _gate = new();

    public SubmitGuard()
        : this(Lifetime, WriteTimeout, MaxClaims)
    {
    }

    public SubmitGuard(TimeSpan lifetime, TimeSpan writeTimeout, int maxClaims)
    {
        _lifetime = lifetime;
        _writeTimeout = writeTimeout;
        _claims = new MemoryCache(new MemoryCacheOptions { SizeLimit = maxClaims, ExpirationScanFrequency = TimeSpan.FromSeconds(15) });
    }

    /// <summary>
    /// Runs <paramref name="write"/> at most once for <paramref name="key"/>. With no key (a missing or malformed id) it is the old behavior: the write runs on <paramref name="requestAborted"/>, unguarded.
    /// <paramref name="fallback"/> is where a repeat goes when the first answer is unknown.
    /// </summary>
    public async Task<SubmitOutcome> RunAsync(SubmitKey? key, SubmitTarget fallback, Func<CancellationToken, Task<Result<SubmitTarget>>> write, CancellationToken requestAborted)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (key is not { } submitKey)
        {
            return From(await write(requestAborted));
        }

        Claim? existing;
        Claim? mine = null;
        lock (_gate)
        {
            if (!_claims.TryGetValue(submitKey.Hash, out existing))
            {
                var claim = new Claim();
                using (var entry = _claims.CreateEntry(submitKey.Hash))
                {
                    entry.Value = claim;
                    entry.Size = 1;
                    entry.Priority = CacheItemPriority.NeverRemove;
                    entry.AbsoluteExpirationRelativeToNow = _lifetime;
                }

                // Over the cap the cache keeps nothing: this post goes unguarded rather than being refused.
                mine = _claims.TryGetValue(submitKey.Hash, out Claim? stored) && ReferenceEquals(stored, claim) ? claim : null;
            }
        }

        if (existing is not null)
        {
            var earlier = await existing.Done.Task.WaitAsync(requestAborted);
            return earlier.Status switch
            {
                AttemptStatus.Succeeded => SubmitOutcome.Done(earlier.Target),
                AttemptStatus.Failed => SubmitOutcome.Failed(earlier.Errors),
                _ => SubmitOutcome.Done(fallback),
            };
        }

        if (mine is null)
        {
            return From(await write(requestAborted));
        }

        var attempt = await WriteAsync(submitKey, mine, write);
        return attempt.Status switch
        {
            AttemptStatus.Succeeded => SubmitOutcome.Done(attempt.Target),
            AttemptStatus.Failed => SubmitOutcome.Failed(attempt.Errors),
            _ when attempt.Fault is { } fault => Rethrow(fault),
            _ => SubmitOutcome.Unknown,
        };
    }

    public void Dispose() => _claims.Dispose();

    private static SubmitOutcome From(Result<SubmitTarget> result) => result.IsSuccess ? SubmitOutcome.Done(result.Value) : SubmitOutcome.Failed(result.Errors);

    private static SubmitOutcome Rethrow(Exception fault)
    {
        ExceptionDispatchInfo.Capture(fault).Throw();
        return SubmitOutcome.Unknown;
    }

    private async Task<Attempt> WriteAsync(SubmitKey key, Claim claim, Func<CancellationToken, Task<Result<SubmitTarget>>> write)
    {
        Attempt attempt;
        using var timeout = new CancellationTokenSource(_writeTimeout);
        try
        {
            var result = await write(timeout.Token);
            attempt = result.IsSuccess ? new Attempt(AttemptStatus.Succeeded, result.Value, [], null) : new Attempt(AttemptStatus.Failed, default, result.Errors, null);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            attempt = new Attempt(AttemptStatus.Unknown, default, [], null);
        }
        catch (Exception ex)
        {
            attempt = new Attempt(AttemptStatus.Unknown, default, [], ex);
        }

        if (attempt.Status == AttemptStatus.Failed)
        {
            // Released before anyone is told, so a request that arrives after this finds no claim and may write.
            lock (_gate)
            {
                _claims.Remove(key.Hash);
            }
        }

        claim.Done.SetResult(attempt);
        return attempt;
    }

    private enum AttemptStatus
    {
        Succeeded,
        Failed,
        Unknown,
    }

    private sealed record Attempt(AttemptStatus Status, SubmitTarget Target, IReadOnlyList<ResultError> Errors, Exception? Fault);

    private sealed class Claim
    {
        public TaskCompletionSource<Attempt> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
```

`src/TechStrap.Portal/Forms/SubmitIds.cs` (new)

```csharp
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace TechStrap.Portal.Forms;

/// <summary>
/// The one-time id a form carries in a hidden input (D-045 09d addendum): 128 random bits from the operating system's generator, written as 22 base64url characters. A fresh one is made every time a form is
/// rendered, so a page that shows errors after a failed post carries a new id, never the posted one. It is a capability (the guard answers a repeat of it with the first answer), so it is random, never a counter or
/// a time, and it is only ever compared by the hash of its <see cref="SubmitKey"/>.
/// </summary>
public static partial class SubmitIds
{
    /// <summary>The length of an id: 16 random bytes, base64url, no padding.</summary>
    public const int Length = 22;

    [GeneratedRegex(@"\A[A-Za-z0-9_-]{22}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    public static string New() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

    /// <summary>True for a value of exactly the shape <see cref="New"/> makes. Anything else (missing, short, long, other characters) means the post is not guarded.</summary>
    public static bool IsWellFormed(string? value) => value is not null && Shape().IsMatch(value);
}
```

`src/TechStrap.Portal/Components/Pages/Contact.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/Contact.razor
+++ b/src/TechStrap.Portal/Components/Pages/Contact.razor
@@ -15,6 +15,7 @@
     }
     <form method="post" action="@PortalRoutes.Contact(theme.Key)" enctype="multipart/form-data" novalidate @formname="@FormHandler" @onsubmit="SubmitAsync" class="ts-form">
         <AntiforgeryToken />
+        <FormGuard Name="Form.SubmitId" />
         <FormField Field="@FormFields.Name" Name="Form.Name" Label="@ContactCopy.NameLabel" Value="@Form?.Name" MaxLength="@IntakeLimits.NameMaxLength" Autocomplete="name" Error="@ErrorOf(FormFields.Name)" />
         <FormField Field="@FormFields.Email" Name="Form.Email" Label="@ContactCopy.EmailLabel" Value="@Form?.Email" MaxLength="@IntakeLimits.EmailMaxLength" Type="email" Autocomplete="email" Error="@ErrorOf(FormFields.Email)" />
         <FormField Field="@FormFields.Subject" Name="Form.Subject" Label="@ContactCopy.SubjectLabel" Value="@Form?.Subject" MaxLength="@IntakeLimits.SubjectMaxLength" Error="@ErrorOf(FormFields.Subject)" />
```

`src/TechStrap.Portal/Components/Pages/Contact.razor.cs`

```diff
--- a/src/TechStrap.Portal/Components/Pages/Contact.razor.cs
+++ b/src/TechStrap.Portal/Components/Pages/Contact.razor.cs
@@ -1,4 +1,5 @@
 using Microsoft.AspNetCore.Components;
+using SyntaxCircus.Common;
 using TechStrap.Portal.Clients;
 using TechStrap.Portal.Forms;
 using TechStrap.Portal.Products;
@@ -41,6 +42,9 @@ public partial class Contact : ProductPageBase
     [Inject]
     private NavigationManager Redirects { get; set; } = default!;
 
+    [Inject]
+    private SubmitGuard Guard { get; set; } = default!;
+
     [Inject]
     private IHttpContextAccessor Http { get; set; } = default!;
 
@@ -76,16 +80,27 @@ public partial class Contact : ProductPageBase
 
         var request = new NewTicketRequest(
             form.Email!.Trim(), form.Name!.Trim(), form.Subject!.Trim(), form.Body!.Trim(), form.Website, AttachmentRules.ToUploads(form.Files));
-        var cancellation = Http.HttpContext?.RequestAborted ?? CancellationToken.None;
-        var result = await Tickets.SubmitAsync(Key, request, cancellation);
-        if (result.IsSuccess)
+
+        // A double click sends once: the id this form carried claims the write (D-045 09d addendum). A repeat is sent where the first went, or to the received page without a reference when the first's answer is unknown.
+        var outcome = await Guard.RunAsync(
+            SubmitKey.TryCreate(FormHandler, Key, form.SubmitId),
+            new SubmitTarget(PortalRoutes.ContactReceived(Key)),
+            async cancellation =>
+            {
+                var result = await Tickets.SubmitAsync(Key, request, cancellation);
+                return result.IsSuccess
+                    ? Result<SubmitTarget>.Success(new SubmitTarget(PortalRoutes.ContactReceived(Key, References.Protect(Key, result.Value.TicketNumber))))
+                    : Result<SubmitTarget>.Failure(result.Errors[0], [.. result.Errors.Skip(1)]);
+            },
+            RequestAborted);
+        if (outcome.Status == SubmitStatus.Done)
         {
             // Straight after the redirect: nothing else may run or render.
-            Redirects.NavigateTo(PortalRoutes.ContactReceived(Key, References.Protect(Key, result.Value.TicketNumber)));
+            Redirects.NavigateTo(outcome.Target.Path!);
             return;
         }
 
-        var failure = FormFailure.From(result.Errors);
+        var failure = outcome.Status == SubmitStatus.Failed ? FormFailure.From(outcome.Errors) : FormFailure.Unknown;
         if (failure.IsNotFound)
         {
             NotFoundAfterTheming();
```

`src/TechStrap.Portal/Components/Pages/LostLink.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/LostLink.razor
+++ b/src/TechStrap.Portal/Components/Pages/LostLink.razor
@@ -20,6 +20,7 @@
         }
         <form method="post" action="@PortalRoutes.LostLink(theme.Key)" novalidate @formname="@FormHandler" @onsubmit="SubmitAsync" class="ts-form">
             <AntiforgeryToken />
+            <FormGuard Name="Form.SubmitId" />
             <FormField Field="@FormFields.Email" Name="Form.Email" Label="@LostLinkCopy.EmailLabel" Value="@Form?.Email" MaxLength="@TechStrap.Contracts.Intake.IntakeLimits.EmailMaxLength" Type="email" Autocomplete="email" Error="@ErrorOf(FormFields.Email)" />
             <button type="submit" class="btn btn-primary">@LostLinkCopy.Submit</button>
         </form>
```

`src/TechStrap.Portal/Components/Pages/LostLink.razor.cs`

```diff
--- a/src/TechStrap.Portal/Components/Pages/LostLink.razor.cs
+++ b/src/TechStrap.Portal/Components/Pages/LostLink.razor.cs
@@ -1,4 +1,5 @@
 using Microsoft.AspNetCore.Components;
+using SyntaxCircus.Common;
 using TechStrap.Portal.Clients;
 using TechStrap.Portal.Forms;
 using TechStrap.Portal.Products;
@@ -27,6 +28,9 @@ public partial class LostLink : ProductPageBase
     [Inject]
     private NavigationManager Redirects { get; set; } = default!;
 
+    [Inject]
+    private SubmitGuard Guard { get; set; } = default!;
+
     [Inject]
     private IHttpContextAccessor Http { get; set; } = default!;
 
@@ -59,15 +63,26 @@ public partial class LostLink : ProductPageBase
             return;
         }
 
-        var result = await Customers.RequestAccessLinkAsync(form.Email!.Trim(), Http.HttpContext?.RequestAborted ?? CancellationToken.None);
-        if (result.IsSuccess)
+        // A double click asks for one email, not two: the id this form carried claims the write (D-045 09d addendum). The target never depends on the answer, so a repeat always goes to the same page.
+        var sent = new SubmitTarget(PortalRoutes.LostLinkSent(Key));
+        var email = form.Email!.Trim();
+        var outcome = await Guard.RunAsync(
+            SubmitKey.TryCreate(FormHandler, Key, form.SubmitId),
+            sent,
+            async cancellation =>
+            {
+                var result = await Customers.RequestAccessLinkAsync(email, cancellation);
+                return result.IsSuccess ? Result<SubmitTarget>.Success(sent) : Result<SubmitTarget>.Failure(result.Errors[0], [.. result.Errors.Skip(1)]);
+            },
+            Http.HttpContext?.RequestAborted ?? CancellationToken.None);
+        if (outcome.Status == SubmitStatus.Done)
         {
-            Redirects.NavigateTo(PortalRoutes.LostLinkSent(Key));
+            Redirects.NavigateTo(outcome.Target.Path!);
             return;
         }
 
         // The route has no product or ticket for the API to not find, so a not-found here is the API misrouted: the same calm notice as an outage.
-        var failure = FormFailure.From(result.Errors);
+        var failure = outcome.Status == SubmitStatus.Failed ? FormFailure.From(outcome.Errors) : FormFailure.Unknown;
         if (failure.IsNotFound)
         {
             failure = new FormFailure([], FormCopy.Unavailable, StatusCodes.Status503ServiceUnavailable, false);
```

`src/TechStrap.Portal/Components/Pages/Ticket.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/Ticket.razor
+++ b/src/TechStrap.Portal/Components/Pages/Ticket.razor
@@ -40,6 +40,7 @@ else if (UnavailableMessage is not null)
 @code {
     private RenderFragment ReplyForm(string button) => @<form method="post" action="@PortalRoutes.Ticket(_token)" enctype="multipart/form-data" novalidate @formname="@ReplyHandler" @onsubmit="ReplyAsync" class="ts-form">
         <AntiforgeryToken />
+        <FormGuard Name="Reply.SubmitId" />
         <FormField Field="@FormFields.Body" Name="Reply.Body" Label="@TicketCopy.ReplyLabel" Value="@Reply?.Body" MaxLength="@IntakeLimits.BodyMaxLength" Rows="6" Error="@ErrorOf(FormFields.Body)" />
         <AttachmentInput Name="Reply.Files" Error="@ErrorOf(FormFields.Attachments)" Kept="@(Errors.Count > 0 || Notice is not null)" />
         <button type="submit" class="btn btn-primary">@button</button>
```

`src/TechStrap.Portal/Components/Pages/Ticket.razor.cs`

```diff
--- a/src/TechStrap.Portal/Components/Pages/Ticket.razor.cs
+++ b/src/TechStrap.Portal/Components/Pages/Ticket.razor.cs
@@ -37,6 +37,9 @@ public partial class Ticket
     [Inject]
     private NavigationManager Navigation { get; set; } = default!;
 
+    [Inject]
+    private SubmitGuard Guard { get; set; } = default!;
+
     [Inject]
     private IHostEnvironment Environment { get; set; } = default!;
 
@@ -108,19 +111,34 @@ public partial class Ticket
             return;
         }
 
-        var result = await Tickets.ReplyAsync(_token, new CustomerReply(form.Body!.Trim(), AttachmentRules.ToUploads(form.Files)), Cancellation);
-        if (result.IsSuccess)
+        // A double click sends one reply: the id this form carried, with the ticket's token, claims the write (D-045 09d addendum). A repeat goes where the first went (the same page, or the follow-up's page), or back to this
+        // ticket's page when the first's answer is unknown. The stored target can be a follow-up's access token: it stays in memory, for two minutes, and is never logged.
+        var reply = new CustomerReply(form.Body!.Trim(), AttachmentRules.ToUploads(form.Files));
+        var outcome = await Guard.RunAsync(
+            SubmitKey.TryCreate(ReplyHandler, _token.Value, form.SubmitId),
+            new SubmitTarget(PortalRoutes.Ticket(_token)),
+            async cancellation =>
+            {
+                var result = await Tickets.ReplyAsync(_token, reply, cancellation);
+                if (result.IsFailure)
+                {
+                    return Result<SubmitTarget>.Failure(result.Errors[0], [.. result.Errors.Skip(1)]);
+                }
+
+                if (!result.Value.FollowUpCreated)
+                {
+                    return Result<SubmitTarget>.Success(new SubmitTarget(PortalRoutes.Ticket(_token)));
+                }
+
+                return Result<SubmitTarget>.Success(FollowUpLink.TryGetToken(result.Value.FollowUpViewUrl, out var followUp) ? new SubmitTarget(PortalRoutes.Ticket(followUp)) : SubmitTarget.None);
+            },
+            Cancellation);
+        if (outcome.Status == SubmitStatus.Done)
         {
             // Straight after each redirect: nothing else may run or render.
-            if (!result.Value.FollowUpCreated)
-            {
-                Navigation.NavigateTo(PortalRoutes.Ticket(_token));
-                return;
-            }
-
-            if (FollowUpLink.TryGetToken(result.Value.FollowUpViewUrl, out var followUp))
+            if (outcome.Target.Path is { } path)
             {
-                Navigation.NavigateTo(PortalRoutes.Ticket(followUp));
+                Navigation.NavigateTo(path);
                 return;
             }
 
@@ -128,7 +146,7 @@ public partial class Ticket
             return;
         }
 
-        var failure = FormFailure.From(result.Errors);
+        var failure = outcome.Status == SubmitStatus.Failed ? FormFailure.From(outcome.Errors) : FormFailure.Unknown;
         if (failure.IsNotFound)
         {
             Scope.Clear();
```

`src/TechStrap.Portal/Forms/ContactFormViewModel.cs`

```diff
--- a/src/TechStrap.Portal/Forms/ContactFormViewModel.cs
+++ b/src/TechStrap.Portal/Forms/ContactFormViewModel.cs
@@ -19,6 +19,9 @@ public sealed class ContactFormViewModel
 
     public string? Website { get; set; }
 
+    /// <summary>The one-time id of the form (<see cref="SubmitIds"/>), posted in a hidden input; missing or malformed means the post is not guarded.</summary>
+    public string? SubmitId { get; set; }
+
     public IReadOnlyList<IBrowserFile>? Files { get; set; }
 
     /// <summary>
```

`src/TechStrap.Portal/Forms/FormFailure.cs`

```diff
--- a/src/TechStrap.Portal/Forms/FormFailure.cs
+++ b/src/TechStrap.Portal/Forms/FormFailure.cs
@@ -11,6 +11,9 @@ namespace TechStrap.Portal.Forms;
 /// </summary>
 public sealed record FormFailure(IReadOnlyList<FormError> Errors, string? Notice, int Status, bool IsNotFound)
 {
+    /// <summary>The calm notice for a guarded write whose answer is unknown (it timed out): the same sentence and status as an outage.</summary>
+    public static FormFailure Unknown { get; } = WithNotice(FormCopy.Unavailable, StatusCodes.Status503ServiceUnavailable);
+
     public static FormFailure From(IReadOnlyList<ResultError> errors)
     {
         if (errors.Any(error => error.Kind == ResultErrorKind.NotFound))
```

`src/TechStrap.Portal/Forms/LostLinkForm.cs`

```diff
--- a/src/TechStrap.Portal/Forms/LostLinkForm.cs
+++ b/src/TechStrap.Portal/Forms/LostLinkForm.cs
@@ -4,6 +4,9 @@ namespace TechStrap.Portal.Forms;
 public sealed class LostLinkFormViewModel
 {
     public string? Email { get; set; }
+
+    /// <summary>The one-time id of the form (<see cref="SubmitIds"/>), posted in a hidden input; missing or malformed means the post is not guarded.</summary>
+    public string? SubmitId { get; set; }
 }
 
 /// <summary>The words of the lost-link page (UX brief). The confirmation is one sentence that says nothing about whether the address matched: it is the same for every well-formed address.</summary>
```

`src/TechStrap.Portal/Forms/ReplyForm.cs`

```diff
--- a/src/TechStrap.Portal/Forms/ReplyForm.cs
+++ b/src/TechStrap.Portal/Forms/ReplyForm.cs
@@ -8,6 +8,9 @@ public sealed class ReplyFormViewModel
 {
     public string? Body { get; set; }
 
+    /// <summary>The one-time id of the form (<see cref="SubmitIds"/>), posted in a hidden input; missing or malformed means the post is not guarded.</summary>
+    public string? SubmitId { get; set; }
+
     public IReadOnlyList<IBrowserFile>? Files { get; set; }
 }
 
```

`src/TechStrap.Portal/Program.cs`

```diff
--- a/src/TechStrap.Portal/Program.cs
+++ b/src/TechStrap.Portal/Program.cs
@@ -50,6 +50,9 @@ builder.Services.AddScoped<ProductScope>();
 builder.Services.TryAddSingleton(TimeProvider.System);
 builder.Services.AddSingleton<ReceivedReference>();
 
+// The double-send guard of the three forms: its own capped cache, claims kept for two minutes, in memory only (D-045 09d addendum).
+builder.Services.AddSingleton<SubmitGuard>();
+
 // Meta tags, robots.txt and the canonical-host redirect; Seo:BaseUrl comes from the public address (D-045).
 builder.Services.AddPortalSeo(builder.Configuration);
 // Installation-wide switch for the "Powered by TechStrap" footer (D-024); shown unless set to false.
```

The existing hidden-field pin of the prefill test and a stale comment are the two existing tests the change touches (both are in the test commit above: `ContactPageHostTests` allows exactly one hidden `Form` field, `SubmitId`, and compares pages without the random ids; `TicketReplyHostTests` no longer calls a double click a known gap).

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/SubmitGuardTests/*"
dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/DoubleSendHostTests/*"
dotnet test --project tests/TechStrap.Portal.Tests -c Release
```

Expected: 25 and 31 tests pass, in a few seconds each, and the whole suite (1,530) passes:

```text
  total: 25
  failed: 0
  succeeded: 25
  duration: 2s 662ms
```
```text
  total: 31
  failed: 0
  succeeded: 31
  duration: 5s 019ms
```
```text
  total: 1530
  failed: 0
  succeeded: 1530
```

- [ ] **Step 5: Prove each pin with a recorded mutation**

Save `$T\specs\m2.py`:

```python
"""Mutations of Task 2 (the double-send guard). Run from the repository root."""
PORTAL = ["dotnet", "test", "--project", "tests/TechStrap.Portal.Tests", "-c", "Release", "--filter-query"]
G = "src/TechStrap.Portal/Forms/SubmitGuard.cs"
CONTACT = "src/TechStrap.Portal/Components/Pages/Contact.razor.cs"
LOST = "src/TechStrap.Portal/Components/Pages/LostLink.razor.cs"
TICKET = "src/TechStrap.Portal/Components/Pages/Ticket.razor.cs"
UNIT = PORTAL + ["/*/*/SubmitGuardTests/*"]
HOST = PORTAL + ["/*/*/DoubleSendHostTests/*"]

MUTATIONS = [
    ("1 a failure no longer releases the claim", G, [("_claims.Remove(key.Hash);", "_claims.Get(key.Hash);")], UNIT),
    ("2 a claim never expires", G, [("entry.AbsoluteExpirationRelativeToNow = _lifetime;", "entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);")], UNIT),
    ("3 a claim has no size, so the cap never holds", G, [("entry.Size = 1;", "entry.Size = 0;")], UNIT),
    ("4 the write runs on the request's token", G, [("var attempt = await WriteAsync(submitKey, mine, write);", "var attempt = await WriteAsync(submitKey, mine, _ => write(requestAborted));")], UNIT),
    ("5 the first request stops waiting when it is aborted", G, [("var attempt = await WriteAsync(submitKey, mine, write);", "var attempt = await WriteAsync(submitKey, mine, write).WaitAsync(requestAborted);")], UNIT),
    ("6 the form name is not part of the key", G, [('$"{form}\\0{scope.ToLowerInvariant()}\\0{id}"', '$"\\0{scope.ToLowerInvariant()}\\0{id}"')], UNIT),
    ("7 the subject is not part of the key", G, [('$"{form}\\0{scope.ToLowerInvariant()}\\0{id}"', '$"{form}\\0\\0{id}"')], UNIT),
    ("8 the product key is compared with its case", G, [('{scope.ToLowerInvariant()}', '{scope}')], UNIT),
    ("9 an unknown answer is not sent to the fallback", G, [("                _ => SubmitOutcome.Done(fallback),", "                _ => SubmitOutcome.Unknown,")], UNIT),
    ("10 a malformed id is guarded", G, [("if (!SubmitIds.IsWellFormed(id))", "if (id is null)")], UNIT),
    ("11 a write that throws is forgotten", G, [("            _ when attempt.Fault is { } fault => Rethrow(fault),\n", "")], UNIT),
    ("12 a waiting repeat that sees a failure writes again", G, [("                AttemptStatus.Failed => SubmitOutcome.Failed(earlier.Errors),", "                AttemptStatus.Failed => SubmitOutcome.Unknown,")], UNIT),
    ("13 the claim lives longer than the reference it may hold", G, [("public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);", "public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);")], UNIT),
    ("14 the write has no timeout", G, [("using var timeout = new CancellationTokenSource(_writeTimeout);", "using var timeout = new CancellationTokenSource(TimeSpan.FromDays(1));")], UNIT),
    ("15 an id is random only in its first byte", "src/TechStrap.Portal/Forms/SubmitIds.cs", [("RandomNumberGenerator.GetBytes(16)", "new byte[16]")], UNIT),
    ("16 the contact page does not guard", CONTACT, [("SubmitKey.TryCreate(FormHandler, Key, form.SubmitId),", "null,")], HOST),
    ("17 the lost-link page does not guard", LOST, [("SubmitKey.TryCreate(FormHandler, Key, form.SubmitId),", "null,")], HOST),
    ("18 the reply page does not guard", TICKET, [("SubmitKey.TryCreate(ReplyHandler, _token.Value, form.SubmitId),", "null,")], HOST),
    ("19 the reply key ignores the ticket", TICKET, [("SubmitKey.TryCreate(ReplyHandler, _token.Value, form.SubmitId),", 'SubmitKey.TryCreate(ReplyHandler, "ticket", form.SubmitId),')], HOST),
    ("20 the contact key ignores the product", CONTACT, [("SubmitKey.TryCreate(FormHandler, Key, form.SubmitId),", 'SubmitKey.TryCreate(FormHandler, "product", form.SubmitId),')], HOST),
    ("21 a repeat of a follow-up goes to the old ticket", TICKET, [("new SubmitTarget(PortalRoutes.Ticket(followUp)) : SubmitTarget.None", "new SubmitTarget(PortalRoutes.Ticket(_token)) : SubmitTarget.None")], HOST),
    ("22 the contact fallback keeps a reference", CONTACT, [("new SubmitTarget(PortalRoutes.ContactReceived(Key)),", 'new SubmitTarget(PortalRoutes.ContactReceived(Key, "x")),')], HOST),
    ("23 the reply fallback is not the ticket page", TICKET, [("new SubmitTarget(PortalRoutes.Ticket(_token)),\n            async cancellation =>", 'new SubmitTarget("/"),\n            async cancellation =>')], HOST),
    ("24 an unknown answer is shown as a success", "src/TechStrap.Portal/Forms/FormFailure.cs", [("WithNotice(FormCopy.Unavailable, StatusCodes.Status503ServiceUnavailable);\n\n    public static FormFailure From", "WithNotice(FormCopy.Unavailable, StatusCodes.Status200OK);\n\n    public static FormFailure From")], HOST),
    ("25 the form shows the same id on every render", "src/TechStrap.Portal/Components/Ui/FormGuard.razor", [("private readonly string _id = SubmitIds.New();", 'private static readonly string _shared = SubmitIds.New();\n    private readonly string _id = _shared;')], HOST),
    ("26 the lost-link sent page depends on the answer", LOST, [("return result.IsSuccess ? Result<SubmitTarget>.Success(sent) :", "return result.IsSuccess ? Result<SubmitTarget>.Success(new SubmitTarget(sent.Path + \"&x=1\")) :")], HOST),
]
```

Run them (in three foreground batches, `1`..`6`, `7`..`18` and `19`..`26`, about a minute each) and record the results:

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | a failure no longer releases the claim | `SubmitGuard.cs` | KILLED (1 failing) |
| 2 | a claim never expires | `SubmitGuard.cs` | KILLED (1 failing) |
| 3 | a claim has no size, so the cap never holds | `SubmitGuard.cs` | KILLED (1 failing) |
| 4 | the write runs on the request's token | `SubmitGuard.cs` | KILLED (3 failing) |
| 5 | the first request stops waiting when it is aborted | `SubmitGuard.cs` | KILLED (2 failing) |
| 6 | the form name is not part of the key | `SubmitGuard.cs` | KILLED (2 failing) |
| 7 | the subject is not part of the key | `SubmitGuard.cs` | KILLED (2 failing) |
| 8 | the product key is compared with its case | `SubmitGuard.cs` | KILLED (1 failing) |
| 9 | an unknown answer is not sent to the fallback | `SubmitGuard.cs` | KILLED (2 failing) |
| 10 | a malformed id is guarded | `SubmitGuard.cs` | KILLED (6 failing) |
| 11 | a write that throws is forgotten | `SubmitGuard.cs` | KILLED (1 failing) |
| 12 | a waiting repeat that sees a failure writes again | `SubmitGuard.cs` | KILLED (1 failing) |
| 13 | the claim lives longer than the reference it may hold | `SubmitGuard.cs` | KILLED (1 failing) |
| 14 | the write has no timeout | `SubmitGuard.cs` | KILLED (1 failing) |
| 15 | an id is random only in its first byte | `SubmitIds.cs` | KILLED (3 failing) |
| 16 | the contact page does not guard | `Contact.razor.cs` | KILLED (5 failing) |
| 17 | the lost-link page does not guard | `LostLink.razor.cs` | KILLED (3 failing) |
| 18 | the reply page does not guard | `Ticket.razor.cs` | KILLED (5 failing) |
| 19 | the reply key ignores the ticket | `Ticket.razor.cs` | KILLED (1 failing) |
| 20 | the contact key ignores the product | `Contact.razor.cs` | KILLED (1 failing) |
| 21 | a repeat of a follow-up goes to the old ticket | `Ticket.razor.cs` | KILLED (2 failing) |
| 22 | the contact fallback keeps a reference | `Contact.razor.cs` | KILLED (1 failing) |
| 23 | the reply fallback is not the ticket page | `Ticket.razor.cs` | KILLED (1 failing) |
| 24 | an unknown answer is shown as a success | `FormFailure.cs` | KILLED (2 failing) |
| 25 | the form shows the same id on every render | `FormGuard.razor` | KILLED (2 failing) |
| 26 | the lost-link sent page depends on the answer | `LostLink.razor.cs` | KILLED (2 failing) |

Every mutation is killed; there are no survivors in this task. One mutant is not listed because it is equivalent: making a claim that the full cache did not store look stored (`mine = claim`) changes nothing observable, since an unstored claim is found by nobody and a repeat then makes a claim of its own, which is the unguarded behavior the cap test already pins.

- [ ] **Step 6: Run the full suite and commit**

```bash
dotnet test --project tests/TechStrap.Portal.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: 1,530 and the architecture suite pass.

```bash
git add -A
git diff --cached --stat
git commit -m "feat: PHASE-09d double-send guard for the contact, reply and lost-link forms (D-045)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

### Task 3: `portal-forms.js`: the sending state, the copy button and the counter, loaded once from the document shell

**Review Focus pin:** 1 (the visible half of the double-send guard: a form that is sending cancels a second submit) and 3 (only `textContent` and data-attribute copy, no inline script, no `on*` attribute, the policy unchanged, a script that survives enhanced navigation).

**Files:**

- Create: `src/TechStrap.Portal/wwwroot/js/portal-forms.js`
- Modify: `src/TechStrap.Portal/Components/App.razor` (both modules load here)
- Modify: `src/TechStrap.Portal/Components/Pages/Contact.razor` (no script; `data-sending-label`), `LostLink.razor`, `Ticket.razor` (`data-sending-label`), `ContactReceived.razor` (the number's id and the copy element), `src/TechStrap.Portal/Components/Ui/FormField.razor` (the counter)
- Modify: `src/TechStrap.Portal/Forms/FormCopy.cs` (`Sending`, `CounterTemplate`, `CounterOver`), `src/TechStrap.Portal/Forms/ContactCopy.cs` (`CopyNumber`, `CopyNumberDone`, `CopyNumberFailed`)
- Test (create): `tests/TechStrap.Portal.Tests/js/portal-forms.test.mjs`
- Test (create): `tests/TechStrap.Portal.Tests/Forms/PortalFormsHostTests.cs`
- Test (modify): `scripts/tests/PortalScripts.Tests.ps1`, `tests/TechStrap.Portal.Tests/Suggestions/ContactSuggestionsHostTests.cs`, `tests/TechStrap.Portal.Tests/Forms/ContactPostHostTests.cs`, `tests/TechStrap.Portal.Tests/Forms/ContactReceivedHostTests.cs`

**Interfaces:**
- Consumes: the `ts-kb-suggestions` module's pattern (an environment passed in, `textContent` only, `describe('the source')` checks), `FormField` (`Rows > 0` is a textarea), `IntakeLimits.BodyMaxLength` (100,000), `ContactReceived`'s `ts-ticket-number`, `FormTestKit`, `TicketTestKit`.
- Produces:
  - `portal-forms.js` exports `SENDING_RESET_MS` (60000), `NEAR_RATIO` (0.8), `COUNT_ANNOUNCE_MS` (1000), `COPIED_RESET_MS` (4000), `serverLength(value)` (the length the server will see: each LF counts twice), `groupDigits`, `fill`, `counterText({used, limit, template, overTemplate}) : {visible, over, text}`, `submitButtonOf(form)`, `createSendingState({setTimer, clearTimer, resetAfterMs}) : {onSubmit, onPageShow, isSending}`, `copyText(text, nav)`, `selectContents(node, doc)`, `copyWords(element)`, `defineElements(env)` (defines `ts-copy-text` and `ts-char-count`) and `installSendingState(env, options)`; the file defines the two elements and installs a capturing `submit` listener on the document and a `pageshow` listener on the window when it runs in a browser.
  - Markup contract: a form carries `data-sending-label`; `<ts-copy-text target="ticket-number" data-label data-copied data-failed>` sits beside `<strong id="ticket-number">`; `<ts-char-count for data-limit data-template data-over hidden>` follows each textarea.
  - Copy: `FormCopy.Sending` (`"Sending\u2026"`), `FormCopy.CounterTemplate`, `FormCopy.CounterOver`, `ContactCopy.CopyNumber`, `ContactCopy.CopyNumberDone`, `ContactCopy.CopyNumberFailed`.
  - `App.razor` loads `js/kb-suggestions.js` and `js/portal-forms.js` once, after `blazor.web.js`, and no page body carries a script.

- [ ] **Step 1: Write the failing tests**

The node tests cover every function with fakes of the document, the clock and the clipboard (the counter's thresholds, a line break counting twice, the screen-reader delay, the sending state's reset and its second submit, the clipboard's refusal selecting the number, clean-up on disconnect, and the source: no `innerHTML`, no request, no non-ASCII). The host tests pin the markup: both modules load once from the shell after `blazor.web.js` on every kind of page and no page body has a script; the sending words, the counter and the copy element carry their words from the constants and sit where the module looks for them; the policy is unchanged. Three existing tests change with the markup: the number now has an `id`, and the 09b "no other page loads the module" becomes "every page has it from the shell".

`tests/TechStrap.Portal.Tests/Forms/PortalFormsHostTests.cs` (new)

```csharp
using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// <c>portal-forms.js</c> at the host (D-045 09d addendum, Review Focus 3): the document shell loads both modules once and no page body carries a script, so Blazor's enhanced navigation (which does not run a script that
/// arrives with swapped content) cannot leave a form without its helpers; the sending label, the counter and the copy button get every word from a <c>*Copy</c> constant through a data attribute; there is no inline script
/// and no <c>on*</c> attribute; the policy is unchanged; and the pinned markup of the forms (the button, the form tag's start) is untouched.
/// </summary>
public sealed class PortalFormsHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static IDocument Parse(string html) => new HtmlParser().ParseDocument(html);

    private static async Task<(HttpResponseMessage Response, string Html)> GetAsync(PortalFactory factory, string path)
    {
        using var client = FormTestKit.Client(factory);
        var response = await client.GetAsync(path, Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    private static string ReferenceFor(PortalFactory factory, string number)
    {
        using var scope = factory.Services.CreateScope();
        return Uri.EscapeDataString(scope.ServiceProvider.GetRequiredService<ReceivedReference>().Protect("paperplane", number));
    }

    // ---- the shell loads the modules once ----

    [Theory]
    [InlineData("/")]
    [InlineData("/nope")]
    [InlineData("/p/paperplane")]
    [InlineData("/p/paperplane/contact")]
    [InlineData("/p/paperplane/contact/received")]
    [InlineData("/p/paperplane/lost-link")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567")]
    public async Task Every_page_loads_each_module_once_from_the_document_shell_after_blazor_and_no_page_body_has_a_script(string path)
    {
        await using var factory = TicketTestKit.Factory();

        var (_, html) = await GetAsync(factory, path);

        var document = Parse(html);
        var scripts = document.QuerySelectorAll("script").ToList();
        scripts.Select(s => s.GetAttribute("src") ?? "(inline)").Where(src => src.Contains("blazor.web")).ShouldHaveSingleItem();
        var modules = scripts.Where(s => s.GetAttribute("type") == "module").Select(s => s.GetAttribute("src")!).ToList();
        modules.Count(src => Regex.IsMatch(src, @"js/kb-suggestions\.[A-Za-z0-9]+\.js")).ShouldBe(1);
        modules.Count(src => Regex.IsMatch(src, @"js/portal-forms\.[A-Za-z0-9]+\.js")).ShouldBe(1);
        modules.Count.ShouldBe(2);
        scripts.Where(s => !s.HasAttribute("src")).ShouldBeEmpty("no inline script");
        document.QuerySelector("main")!.QuerySelectorAll("script").ShouldBeEmpty("a script in a page body would not run after an enhanced navigation");
        scripts.IndexOf(scripts.Single(s => s.GetAttribute("src")!.Contains("blazor.web"))).ShouldBeLessThan(scripts.IndexOf(scripts.First(s => s.GetAttribute("type") == "module")));
        Regex.Matches(html, @"\son[a-z]+\s*=", RegexOptions.IgnoreCase).Count.ShouldBe(0, "no inline event handler");
    }

    [Fact]
    public async Task The_policy_is_unchanged_and_the_modules_are_same_origin_files()
    {
        await using var factory = FormTestKit.Factory();

        var (response, _) = await GetAsync(factory, FormTestKit.Path);

        var directives = response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);
        directives.ShouldContain("script-src 'self'");
        directives.ShouldContain("connect-src 'self'");
        directives.ShouldNotContain(d => d.Contains("unsafe-inline", StringComparison.Ordinal) && d.StartsWith("script-src", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_module_is_served_as_javascript_under_its_fingerprinted_and_plain_name_and_builds_no_markup_from_text()
    {
        await using var factory = FormTestKit.Factory();
        var (_, html) = await GetAsync(factory, FormTestKit.Path);
        var src = Regex.Match(html, "<script type=\"module\" src=\"([^\"]*portal-forms[^\"]*\\.js)\"></script>").Groups[1].Value;
        src.ShouldNotBeNullOrEmpty();
        using var client = FormTestKit.Client(factory);

        using var fingerprinted = await client.GetAsync("/" + src.TrimStart('/'), Ct);
        using var plain = await client.GetAsync("/js/portal-forms.js", Ct);

        foreach (var response in new[] { fingerprinted, plain })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("text/javascript");
            var text = await response.Content.ReadAsStringAsync(Ct);
            text.ShouldContain("ts-copy-text");
            text.ShouldContain("ts-char-count");
            text.ShouldNotContain("innerHTML");
        }
    }

    // ---- the sending label ----

    [Fact]
    public async Task Each_form_carries_the_sending_words_and_keeps_its_pinned_markup()
    {
        await using var factory = TicketTestKit.Factory();

        var contact = (await GetAsync(factory, FormTestKit.Path)).Html;
        var lostLink = (await GetAsync(factory, "/p/paperplane/lost-link")).Html;
        var reply = (await GetAsync(factory, TicketTestKit.Path)).Html;

        foreach (var html in new[] { contact, lostLink, reply })
        {
            var form = Parse(html).QuerySelectorAll("form.ts-form").ShouldHaveSingleItem();
            form.GetAttribute("data-sending-label").ShouldBe(FormCopy.Sending);
            form.GetAttribute("data-sending-label").ShouldBe("Sending\u2026");
            // The button carries no data attribute: the form does.
            form.QuerySelectorAll("button[type=submit]").ShouldHaveSingleItem().Attributes.Select(a => a.Name).OrderBy(n => n).ShouldBe(["class", "type"]);
        }

        contact.ShouldContain("<form method=\"post\" action=\"/p/paperplane/contact\" enctype=\"multipart/form-data\" novalidate");
        contact.ShouldContain("<button type=\"submit\" class=\"btn btn-primary\">Send message</button>");
        lostLink.ShouldContain("<button type=\"submit\" class=\"btn btn-primary\">Send me a new link</button>");
    }

    [Fact]
    public async Task The_sending_words_are_the_one_constant_with_the_ellipsis_written_as_an_escape()
    {
        FormCopy.Sending.ShouldBe("Sending" + char.ConvertFromUtf32(0x2026));
        var source = await File.ReadAllTextAsync(TechStrap.Tests.Shared.RepositoryRoot.Combine("src", "TechStrap.Portal", "Forms", "FormCopy.cs"), Ct);
        source.ShouldContain("\"Sending\\u2026\"");
        source.Any(c => c > 0x7e).ShouldBeFalse("the source stays ASCII");
    }

    // ---- the counter ----

    [Fact]
    public async Task The_long_text_fields_have_a_counter_after_the_textarea_with_the_limit_and_the_words_from_the_constants_and_the_short_fields_have_none()
    {
        await using var factory = TicketTestKit.Factory();

        foreach (var (path, field) in new[] { (FormTestKit.Path, "body"), (TicketTestKit.Path, "body") })
        {
            var document = Parse((await GetAsync(factory, path)).Html);
            var counters = document.QuerySelectorAll("ts-char-count").ToList();
            var counter = counters.ShouldHaveSingleItem();
            counter.GetAttribute("for").ShouldBe(field);
            counter.GetAttribute("data-limit").ShouldBe(IntakeLimits.BodyMaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
            counter.GetAttribute("data-template").ShouldBe(FormCopy.CounterTemplate);
            counter.GetAttribute("data-over").ShouldBe(FormCopy.CounterOver);
            counter.HasAttribute("hidden").ShouldBeTrue("empty and hidden until script shows it");
            counter.TextContent.ShouldBeEmpty();
            counter.PreviousElementSibling!.TagName.ShouldBe("TEXTAREA");
            document.GetElementById(field)!.GetAttribute("maxlength").ShouldBe(counter.GetAttribute("data-limit"), "the counter and the textarea share one limit");
        }

        (await GetAsync(factory, "/p/paperplane/lost-link")).Html.ShouldNotContain("ts-char-count");
    }

    // ---- the copy button ----

    [Fact]
    public async Task The_received_page_has_a_copy_button_element_for_the_number_with_the_words_from_the_constants()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.ReceivedPath + "?ref=" + ReferenceFor(factory, "PAP-42"));

        var document = Parse(html);
        var number = document.GetElementById("ticket-number").ShouldNotBeNull();
        number.TextContent.ShouldBe("PAP-42");
        var copy = document.QuerySelectorAll("ts-copy-text").ShouldHaveSingleItem();
        copy.GetAttribute("target").ShouldBe("ticket-number");
        copy.GetAttribute("data-label").ShouldBe(ContactCopy.CopyNumber);
        copy.GetAttribute("data-copied").ShouldBe(ContactCopy.CopyNumberDone);
        copy.GetAttribute("data-failed").ShouldBe(ContactCopy.CopyNumberFailed);
        copy.TextContent.ShouldBeEmpty("the element is empty until the script renders its button; the number is still there to select");
    }

    [Fact]
    public async Task Without_a_valid_reference_there_is_no_number_and_so_no_copy_button()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.ReceivedPath);

        html.ShouldNotContain("ts-copy-text");
        html.ShouldNotContain("ticket-number");
    }

    [Fact]
    public async Task A_number_that_looks_like_markup_is_encoded_in_the_page_and_never_reaches_an_attribute_of_the_copy_element()
    {
        await using var factory = FormTestKit.Factory();

        // The number comes from a data-protected reference; only a number of the ticket shape is accepted, so a hostile one never gets this far. The element holds no copy of the number at all.
        var (_, html) = await GetAsync(factory, FormTestKit.ReceivedPath + "?ref=" + ReferenceFor(factory, "PAP-42"));

        Regex.Match(html, "<ts-copy-text[^>]*>").Value.ShouldNotContain("PAP-42");
    }
}
```

`tests/TechStrap.Portal.Tests/js/portal-forms.test.mjs` (new)

```javascript
// Runs with `node --test` (no browser): the module takes the document, the window, the navigator and the timers as arguments, so a fake of each is enough.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';
import {
    COPIED_RESET_MS, COUNT_ANNOUNCE_MS, NEAR_RATIO, SENDING_RESET_MS, copyText, copyWords, counterText, createSendingState, defineElements, fill, groupDigits, installSendingState, selectContents,
    serverLength, submitButtonOf,
} from '../../../src/TechStrap.Portal/wwwroot/js/portal-forms.js';

const sourcePath = new URL('../../../src/TechStrap.Portal/wwwroot/js/portal-forms.js', import.meta.url);

// A manual clock: timers run only when a test advances it.
function fakeClock() {
    let now = 0;
    let next = 1;
    const timers = new Map();
    return {
        setTimer: (fn, ms) => { const id = next++; timers.set(id, { fn, at: now + ms }); return id; },
        clearTimer: (id) => { timers.delete(id); },
        pending: () => timers.size,
        advance(ms) {
            now += ms;
            for (const [id, timer] of [...timers]) {
                if (timer.at <= now) { timers.delete(id); timer.fn(); }
            }
        },
    };
}

// A small fake DOM: elements that record what the module does to them.
function fakeElement(tag, attrs = {}) {
    const listeners = new Map();
    const classes = new Set();
    return {
        tag,
        attrs: { ...attrs },
        children: [],
        className: '',
        textContent: '',
        hidden: false,
        disabled: false,
        type: '',
        value: '',
        classList: { toggle: (name, on) => { if (on) { classes.add(name); } else { classes.delete(name); } }, contains: (name) => classes.has(name) },
        getAttribute(name) { return Object.hasOwn(this.attrs, name) ? this.attrs[name] : null; },
        setAttribute(name, value) { this.attrs[name] = value; },
        replaceChildren(...children) { this.children = children; },
        appendChild(child) { this.children.push(child); return child; },
        addEventListener(type, fn) { listeners.set(type, [...(listeners.get(type) ?? []), fn]); },
        removeEventListener(type, fn) { listeners.set(type, (listeners.get(type) ?? []).filter((x) => x !== fn)); },
        listenerCount: (type) => (listeners.get(type) ?? []).length,
        fire(type, event = {}) { for (const fn of listeners.get(type) ?? []) { fn({ type, target: this, ...event }); } },
        querySelector(selector) { return selector === 'button[type="submit"]' ? this.submit ?? null : null; },
    };
}

function fakeDocument() {
    const byId = new Map();
    const selections = [];
    return {
        byId,
        selections,
        createElement: (tag) => fakeElement(tag),
        getElementById: (id) => byId.get(id) ?? null,
        getSelection: () => ({ selectAllChildren: (node) => selections.push(node) }),
        listeners: new Map(),
        addEventListener(type, fn, capture) { this.listeners.set(type, { fn, capture }); },
    };
}

function formWith(label = 'Sending...') {
    const form = fakeElement('form', label === null ? {} : { 'data-sending-label': label });
    const submit = fakeElement('button');
    submit.textContent = 'Send message';
    form.submit = submit;
    return { form, submit };
}

describe('constants', () => {
    it('a stopped post frees the button after a minute, the counter shows from 80 percent, speech waits one second and the copy confirmation lasts four', () => {
        assert.equal(SENDING_RESET_MS, 60000);
        assert.equal(NEAR_RATIO, 0.8);
        assert.equal(COUNT_ANNOUNCE_MS, 1000);
        assert.equal(COPIED_RESET_MS, 4000);
    });
});

describe('serverLength', () => {
    it('counts every line break as two characters, because the browser posts CR LF', () => {
        assert.equal(serverLength(''), 0);
        assert.equal(serverLength('abc'), 3);
        assert.equal(serverLength('a\nb'), 4);
        assert.equal(serverLength('\n\n\n'), 6);
        assert.equal(serverLength('a\r\nb'), 4, 'a CR LF that is already there is counted once as two');
    });

    it('is 0 for anything that is not text', () => {
        for (const value of [null, undefined, 5, {}]) {
            assert.equal(serverLength(value), 0);
        }
    });
});

describe('groupDigits and fill', () => {
    it('writes thousands with commas', () => {
        assert.equal(groupDigits(100000), '100,000');
        assert.equal(groupDigits(999), '999');
        assert.equal(groupDigits(1234567), '1,234,567');
        assert.equal(groupDigits(-5), '5');
        assert.equal(groupDigits('x'), '0');
    });

    it('replaces {0} and {1} everywhere and gives an empty string for no template', () => {
        assert.equal(fill('{0} of {1} ({0})', '5', '10'), '5 of 10 (5)');
        assert.equal(fill(null, '5', '10'), '');
        assert.equal(fill(undefined, '5', '10'), '');
    });
});

describe('counterText', () => {
    const base = { limit: 1000, template: '{0} of {1} characters', overTemplate: '{0} over the limit of {1}' };

    it('is hidden below 80 percent of the limit', () => {
        assert.equal(NEAR_RATIO, 0.8);
        assert.deepEqual(counterText({ ...base, used: 0 }), { visible: false, over: false, text: '' });
        assert.deepEqual(counterText({ ...base, used: 799 }), { visible: false, over: false, text: '' });
    });

    it('shows the count from 80 percent up to the limit', () => {
        assert.deepEqual(counterText({ ...base, used: 800 }), { visible: true, over: false, text: '800 of 1,000 characters' });
        assert.deepEqual(counterText({ ...base, used: 1000 }), { visible: true, over: false, text: '1,000 of 1,000 characters' });
    });

    it('says how far over the limit a text is', () => {
        assert.deepEqual(counterText({ ...base, used: 1003 }), { visible: true, over: true, text: '3 over the limit of 1,000' });
    });

    it('shows nothing without a usable limit', () => {
        for (const limit of [0, -1, NaN, undefined]) {
            assert.equal(counterText({ ...base, limit, used: 5000 }).visible, false);
        }
    });
});

describe('the sending state', () => {
    it('disables the submit button and shows the form\'s own sending words, once the form is submitted', () => {
        const clock = fakeClock();
        const state = createSendingState(clock);
        const { form, submit } = formWith('Sending\u2026');

        const handled = state.onSubmit({ target: form, preventDefault() { assert.fail('the first submit must go through'); } });

        assert.equal(handled, true);
        assert.equal(submit.disabled, true);
        assert.equal(submit.textContent, 'Sending\u2026');
        assert.equal(state.isSending(form), true);
    });

    it('cancels a second submit of a form that is already sending', () => {
        const state = createSendingState(fakeClock());
        const { form } = formWith();
        state.onSubmit({ target: form, preventDefault() {} });
        let prevented = 0;

        state.onSubmit({ target: form, preventDefault() { prevented += 1; } });

        assert.equal(prevented, 1);
    });

    it('leaves a form without a sending label, and a form without a submit button, alone', () => {
        const state = createSendingState(fakeClock());
        const plain = formWith(null);
        const noButton = fakeElement('form', { 'data-sending-label': 'Sending...' });

        assert.equal(state.onSubmit({ target: plain.form, preventDefault() {} }), false);
        assert.equal(plain.submit.disabled, false);
        assert.equal(state.onSubmit({ target: noButton, preventDefault() {} }), false);
        assert.equal(state.onSubmit({ target: null, preventDefault() {} }), false);
        assert.equal(state.onSubmit({ target: {}, preventDefault() {} }), false);
        const empty = formWith('');
        assert.equal(state.onSubmit({ target: empty.form, preventDefault() {} }), false);
    });

    it('brings the button back after a minute, for a post the visitor stopped', () => {
        const clock = fakeClock();
        const state = createSendingState(clock);
        const { form, submit } = formWith();
        state.onSubmit({ target: form, preventDefault() {} });

        clock.advance(SENDING_RESET_MS - 1);
        assert.equal(submit.disabled, true);
        clock.advance(1);

        assert.equal(submit.disabled, false);
        assert.equal(submit.textContent, 'Send message');
        assert.equal(state.isSending(form), false);
    });

    it('brings every sending form back on pageshow after the back button, and not on an ordinary pageshow', () => {
        const clock = fakeClock();
        const state = createSendingState(clock);
        const one = formWith();
        const two = formWith('Sending...');
        state.onSubmit({ target: one.form, preventDefault() {} });
        state.onSubmit({ target: two.form, preventDefault() {} });

        state.onPageShow({ persisted: false });
        assert.equal(one.submit.disabled, true);
        state.onPageShow(undefined);
        assert.equal(one.submit.disabled, true);
        state.onPageShow({ persisted: true });

        assert.equal(one.submit.disabled, false);
        assert.equal(two.submit.disabled, false);
        assert.equal(one.submit.textContent, 'Send message');
        assert.equal(clock.pending(), 0, 'the reset timers are cleared');
    });

    it('can send again after a reset', () => {
        const clock = fakeClock();
        const state = createSendingState(clock);
        const { form, submit } = formWith();
        state.onSubmit({ target: form, preventDefault() {} });
        state.onPageShow({ persisted: true });

        assert.equal(state.onSubmit({ target: form, preventDefault() { assert.fail('not a duplicate any more'); } }), true);
        assert.equal(submit.disabled, true);
    });

    it('is installed as a capturing listener on the document and a pageshow listener on the window', () => {
        const doc = fakeDocument();
        const win = fakeElement('window');

        const state = installSendingState({ document: doc, window: win }, fakeClock());

        assert.equal(doc.listeners.get('submit').capture, true);
        assert.equal(win.listenerCount('pageshow'), 1);
        const { form, submit } = formWith();
        doc.listeners.get('submit').fn({ target: form, preventDefault() {} });
        assert.equal(submit.disabled, true);
        win.fire('pageshow', { persisted: true });
        assert.equal(submit.disabled, false);
        assert.equal(state.isSending(form), false);
    });

    it('finds the submit button by its type', () => {
        const { form, submit } = formWith();
        assert.equal(submitButtonOf(form), submit);
    });
});

describe('copyText and selectContents', () => {
    it('writes to the clipboard and answers true', async () => {
        const written = [];

        assert.equal(await copyText('PAP-42', { clipboard: { writeText: async (text) => { written.push(text); } } }), true);
        assert.deepEqual(written, ['PAP-42']);
    });

    it('answers false, and never throws, when the browser refuses or has no clipboard', async () => {
        assert.equal(await copyText('x', { clipboard: { writeText: async () => { throw new Error('NotAllowedError'); } } }), false);
        assert.equal(await copyText('x', {}), false);
        assert.equal(await copyText('x', { clipboard: {} }), false);
        assert.equal(await copyText('x', null), false);
    });

    it('selects the contents of the element', () => {
        const doc = fakeDocument();
        const node = fakeElement('strong');

        assert.equal(selectContents(node, doc), true);
        assert.deepEqual(doc.selections, [node]);
    });

    it('answers false when there is nothing to select or no selection API', () => {
        assert.equal(selectContents(null, fakeDocument()), false);
        assert.equal(selectContents(fakeElement('x'), {}), false);
        assert.equal(selectContents(fakeElement('x'), { getSelection: () => { throw new Error('no'); } }), false);
    });
});

describe('copyWords', () => {
    it('reads the words from the data attributes and falls back to English for a missing or empty one', () => {
        const full = fakeElement('ts-copy-text', { 'data-label': 'Copier', 'data-copied': 'Copie', 'data-failed': 'Ctrl+C' });
        assert.deepEqual(copyWords(full), { label: 'Copier', copied: 'Copie', failed: 'Ctrl+C' });
        assert.deepEqual(copyWords(fakeElement('x', { 'data-label': '' })), { label: 'Copy', copied: 'Copied', failed: 'Press Ctrl+C to copy' });
    });
});

function elementsEnv() {
    const doc = fakeDocument();
    const clock = fakeClock();
    const defined = new Map();
    const written = [];
    const env = {
        customElements: { get: (name) => defined.get(name), define: (name, ctor) => defined.set(name, ctor) },
        HTMLElement: class {
            constructor() {
                Object.assign(this, fakeElement('custom'));
            }
        },
        document: doc,
        navigator: { clipboard: { writeText: async (text) => { written.push(text); } } },
        setTimeout: clock.setTimer,
        clearTimeout: clock.clearTimer,
    };
    return { env, doc, clock, defined, written };
}

const flush = () => new Promise((resolve) => setImmediate(resolve));

describe('<ts-copy-text>', () => {
    function copyElement(setup, attrs = {}) {
        const { env, doc, clock, defined, written } = setup;
        defineElements(env);
        const target = fakeElement('strong');
        target.textContent = ' PAP-42 ';
        doc.byId.set('ticket-number', target);
        const element = new (defined.get('ts-copy-text'))();
        Object.assign(element.attrs, { target: 'ticket-number', 'data-label': 'Copy ticket number', 'data-copied': 'Copied', 'data-failed': 'Select it and press Ctrl+C', ...attrs });
        return { element, target, doc, clock, written };
    }

    it('is defined once, even when the module runs twice', () => {
        const setup = elementsEnv();
        defineElements(setup.env);
        const first = setup.defined.get('ts-copy-text');

        defineElements(setup.env);

        assert.equal(setup.defined.get('ts-copy-text'), first);
        assert.ok(setup.defined.get('ts-char-count'));
    });

    it('renders a button with the server\'s words as text and copies the trimmed text of its target', async () => {
        const { element, written } = copyElement(elementsEnv());

        element.connectedCallback();
        const [button, status] = element.children;
        assert.equal(button.tag, 'button');
        assert.equal(button.type, 'button');
        assert.equal(button.textContent, 'Copy ticket number');
        assert.equal(status.attrs.role, 'status');
        button.fire('click');
        await flush();

        assert.deepEqual(written, ['PAP-42']);
        assert.equal(status.textContent, 'Copied');
    });

    it('clears the confirmation after a few seconds', async () => {
        const { element, clock } = copyElement(elementsEnv());
        element.connectedCallback();
        const [button, status] = element.children;
        button.fire('click');
        await flush();

        clock.advance(COPIED_RESET_MS);

        assert.equal(status.textContent, '');
    });

    it('selects the number and says so when the browser refuses the clipboard', async () => {
        const setup = elementsEnv();
        setup.env.navigator = { clipboard: { writeText: async () => { throw new Error('NotAllowedError'); } } };
        const { element, target, doc } = copyElement(setup);
        element.connectedCallback();
        const [button, status] = element.children;

        button.fire('click');
        await flush();

        assert.deepEqual(doc.selections, [target]);
        assert.equal(status.textContent, 'Select it and press Ctrl+C');
    });

    it('does nothing when its target is not on the page, and stays clean when removed', () => {
        const { element, doc } = copyElement(elementsEnv());
        doc.byId.clear();

        element.connectedCallback();
        element.disconnectedCallback();

        assert.deepEqual(element.children, []);
    });

    it('removes its listener and timer when disconnected', async () => {
        const { element, clock } = copyElement(elementsEnv());
        element.connectedCallback();
        const [button] = element.children;
        button.fire('click');
        await flush();

        element.disconnectedCallback();

        assert.equal(button.listenerCount('click'), 0);
        assert.equal(clock.pending(), 0);
    });

    it('puts text on the page only as text, whatever the words and the number contain', async () => {
        const { element, target } = copyElement(elementsEnv(), { 'data-label': '<img src=x onerror=alert(1)>' });
        target.textContent = '<b>PAP-42</b>';

        element.connectedCallback();

        assert.equal(element.children[0].textContent, '<img src=x onerror=alert(1)>');
        assert.equal(element.children[0].children.length, 0);
    });
});

describe('<ts-char-count>', () => {
    function counter(setup, attrs = {}) {
        const { env, doc, clock, defined } = setup;
        defineElements(env);
        const field = fakeElement('textarea');
        doc.byId.set('body', field);
        const element = new (defined.get('ts-char-count'))();
        Object.assign(element.attrs, { for: 'body', 'data-limit': '1000', 'data-template': '{0} of {1} characters', 'data-over': '{0} over the limit', ...attrs });
        return { element, field, clock };
    }

    it('starts hidden and empty, and listens to its textarea', () => {
        const { element, field } = counter(elementsEnv());

        element.connectedCallback();

        assert.equal(element.hidden, true);
        assert.equal(field.listenerCount('input'), 1);
        assert.equal(element.children[0].textContent, '');
    });

    it('appears with the count once the text reaches 80 percent, and hides again below it', () => {
        const { element, field } = counter(elementsEnv());
        element.connectedCallback();

        field.value = 'x'.repeat(850);
        field.fire('input');
        assert.equal(element.hidden, false);
        assert.equal(element.children[0].textContent, '850 of 1,000 characters');
        assert.equal(element.children[0].attrs['aria-hidden'], 'true', 'the visible text is not read on every key');

        field.value = 'x'.repeat(100);
        field.fire('input');
        assert.equal(element.hidden, true);
    });

    it('counts a line break as two characters, as the server will', () => {
        const { element, field } = counter(elementsEnv());
        element.connectedCallback();

        field.value = 'a'.repeat(450) + '\n'.repeat(225); // 450 + 225 line breaks counted twice
        field.fire('input');

        assert.equal(element.children[0].textContent, '900 of 1,000 characters');
    });

    it('says how far over the limit the posted text will be, and marks it', () => {
        const { element, field } = counter(elementsEnv());
        element.connectedCallback();

        field.value = 'a'.repeat(1030) + '\n'.repeat(5); // 1030 + 5 line breaks counted twice
        field.fire('input');

        assert.equal(element.children[0].textContent, '40 over the limit');
        assert.equal(element.classList.contains('ts-count-over'), true);
        field.value = 'a';
        field.fire('input');
        assert.equal(element.classList.contains('ts-count-over'), false);
    });

    it('tells the screen reader once typing pauses, not on every key', () => {
        const { element, field, clock } = counter(elementsEnv());
        element.connectedCallback();
        const speech = element.children[1];
        assert.equal(speech.attrs.role, 'status');

        field.value = 'x'.repeat(850);
        field.fire('input');
        clock.advance(COUNT_ANNOUNCE_MS - 1);
        field.value = 'x'.repeat(860);
        field.fire('input');
        clock.advance(COUNT_ANNOUNCE_MS - 1);
        assert.equal(speech.textContent, '', 'still typing');
        clock.advance(1);

        assert.equal(speech.textContent, '860 of 1,000 characters');
    });

    it('shows the count at once for a textarea that already holds text (a form shown again after an error)', () => {
        const setup = elementsEnv();
        const { element, field } = counter(setup);
        field.value = 'x'.repeat(900);

        element.connectedCallback();

        assert.equal(element.hidden, false);
        assert.equal(element.children[0].textContent, '900 of 1,000 characters');
    });

    it('does nothing without its textarea or a usable limit', () => {
        const noLimit = counter(elementsEnv(), { 'data-limit': 'many' });
        noLimit.element.connectedCallback();
        assert.deepEqual(noLimit.element.children, []);
        assert.equal(noLimit.field.listenerCount('input'), 0);

        const noField = counter(elementsEnv(), { for: 'missing' });
        noField.element.connectedCallback();
        assert.deepEqual(noField.element.children, []);
    });

    it('removes its listener and timer when disconnected', () => {
        const { element, field, clock } = counter(elementsEnv());
        element.connectedCallback();
        field.value = 'x'.repeat(900);
        field.fire('input');

        element.disconnectedCallback();

        assert.equal(field.listenerCount('input'), 0);
        assert.equal(clock.pending(), 0);
    });
});

describe('the source', () => {
    const source = readFileSync(sourcePath, 'utf8');
    const code = source.split('\n').filter((line) => !line.trim().startsWith('//') && !line.trim().startsWith('*') && !line.trim().startsWith('/**')).join('\n');

    it('never builds markup from text: no innerHTML, outerHTML, insertAdjacentHTML, document.write, eval or Function', () => {
        for (const forbidden of ['innerHTML', 'outerHTML', 'insertAdjacentHTML', 'document.write', 'eval(', 'new Function', 'setAttribute(\'on', 'srcdoc', 'createContextualFragment', 'DOMParser']) {
            assert.equal(code.includes(forbidden), false, `${forbidden} must not appear`);
        }
    });

    it('puts server text on the page with textContent', () => {
        assert.ok(code.includes('.textContent ='));
    });

    it('does not store state in attributes, builds no URL and makes no request', () => {
        for (const forbidden of ['fetch(', 'XMLHttpRequest', 'sendBeacon', 'location.', 'localStorage', 'sessionStorage', 'document.cookie']) {
            assert.equal(code.includes(forbidden), false, `${forbidden} must not appear`);
        }
    });

    it('is plain ASCII', () => {
        assert.equal(/[^\x09\x0a\x0d\x20-\x7e]/.test(source), false);
    });
});
```

`scripts/tests/PortalScripts.Tests.ps1`

```diff
--- a/scripts/tests/PortalScripts.Tests.ps1
+++ b/scripts/tests/PortalScripts.Tests.ps1
@@ -17,8 +17,26 @@ Describe 'Portal browser scripts' {
         $LASTEXITCODE | Should -Be 0 -Because $output
     }
 
+    It 'passes the node:test suite for the form helpers (sending state, copy button, character counter, text only)' {
+        if (-not $script:Node) {
+            Set-ItResult -Skipped -Because 'node is not installed, so the portal-forms.js tests cannot run'
+            return
+        }
+
+        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Portal.Tests/js/portal-forms.test.mjs'
+
+        $output = & node --test $testFile 2>&1 | Out-String
+
+        $LASTEXITCODE | Should -Be 0 -Because $output
+    }
+
     It 'has the test file and the module it tests' {
         Test-Path -LiteralPath (Join-Path $script:RepoRoot 'tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs') | Should -BeTrue
         Test-Path -LiteralPath (Join-Path $script:RepoRoot 'src/TechStrap.Portal/wwwroot/js/kb-suggestions.js') | Should -BeTrue
     }
+
+    It 'has the test file and the module of the form helpers' {
+        Test-Path -LiteralPath (Join-Path $script:RepoRoot 'tests/TechStrap.Portal.Tests/js/portal-forms.test.mjs') | Should -BeTrue
+        Test-Path -LiteralPath (Join-Path $script:RepoRoot 'src/TechStrap.Portal/wwwroot/js/portal-forms.js') | Should -BeTrue
+    }
 }
```

`tests/TechStrap.Portal.Tests/Forms/ContactPostHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Forms/ContactPostHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/Forms/ContactPostHostTests.cs
@@ -77,7 +77,7 @@ public sealed class ContactPostHostTests
         var second = await client.GetStringAsync(next, Ct);
 
         first.ShouldContain("We have received your request.");
-        first.ShouldContain("<strong class=\"ts-ticket-number\">PAP-42</strong>");
+        first.ShouldContain("<strong class=\"ts-ticket-number\" id=\"ticket-number\">PAP-42</strong>");
         second.ShouldContain("PAP-42");
         factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1, "post, redirect, get: reloading the confirmation never posts again");
     }
```

`tests/TechStrap.Portal.Tests/Forms/ContactReceivedHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Forms/ContactReceivedHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/Forms/ContactReceivedHostTests.cs
@@ -39,7 +39,7 @@ public sealed class ContactReceivedHostTests
         response.StatusCode.ShouldBe(HttpStatusCode.OK);
         html.ShouldContain("<title>Request received: Paperplane</title>");
         html.ShouldContain("<h1>We have received your request.</h1>");
-        html.ShouldContain("Your ticket number is <strong class=\"ts-ticket-number\">PAP-42</strong>");
+        html.ShouldContain("Your ticket number is <strong class=\"ts-ticket-number\" id=\"ticket-number\">PAP-42</strong>");
         html.ShouldContain("We have emailed you a link. Use it to follow the conversation and reply.");
         html.ShouldContain("href=\"/p/paperplane/kb\">Browse help articles</a>");
         html.ShouldContain("href=\"/p/paperplane\">Back to Paperplane</a>");
```

`tests/TechStrap.Portal.Tests/Suggestions/ContactSuggestionsHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Suggestions/ContactSuggestionsHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/Suggestions/ContactSuggestionsHostTests.cs
@@ -45,7 +45,7 @@ public sealed class ContactSuggestionsHostTests
     }
 
     [Fact]
-    public async Task The_page_loads_the_module_as_a_script_file_and_has_no_inline_script()
+    public async Task The_page_loads_the_module_as_a_script_file_from_the_document_shell_and_has_no_inline_script()
     {
         await using var factory = FormTestKit.Factory();
 
@@ -53,6 +53,8 @@ public sealed class ContactSuggestionsHostTests
 
         var script = Regex.Match(html, "<script type=\"module\" src=\"([^\"]*kb-suggestions[^\"]*\\.js)\"></script>");
         script.Success.ShouldBeTrue("the contact page must load the module");
+        Regex.Matches(html, "kb-suggestions[^\"]*\\.js").Count.ShouldBe(1, "once");
+        Regex.Match(html, "<main[^>]*>.*</main>", RegexOptions.Singleline).Value.ShouldNotContain("<script", Case.Sensitive, "not in the page body: a script that arrives with an enhanced navigation does not run");
         Regex.Matches(html, "<script(?![^>]*\\bsrc=)").Count.ShouldBe(0, "no inline script");
         var directives = response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);
         directives.ShouldContain("script-src 'self'", "the module is a same-origin file: the policy is unchanged");
@@ -84,13 +86,16 @@ public sealed class ContactSuggestionsHostTests
     [InlineData("/p/paperplane")]
     [InlineData("/p/paperplane/contact/received")]
     [InlineData("/p/paperplane/lost-link")]
-    public async Task No_other_page_loads_the_module(string path)
+    public async Task Every_page_has_the_module_because_the_document_shell_loads_it_but_only_the_contact_page_has_the_element(string path)
     {
+        // Blazor's enhanced navigation swaps a page into the open document and does not run a script that arrives with it (the 09d spike), so the module cannot depend on the page it was first loaded with:
+        // the shell loads it, and the element upgrades wherever the swap inserts it.
         await using var factory = FormTestKit.Factory();
 
         var (_, html) = await GetAsync(factory, path);
 
-        html.ShouldNotContain("kb-suggestions");
+        Regex.Matches(html, "<script type=\"module\" src=\"[^\"]*kb-suggestions[^\"]*\\.js\"></script>").Count.ShouldBe(1);
+        html.ShouldNotContain("<ts-kb-suggestions");
     }
 
     [Fact]
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
node --test tests/TechStrap.Portal.Tests/js/portal-forms.test.mjs
dotnet build tests/TechStrap.Portal.Tests -c Release
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/PortalScripts.Tests.ps1
```

Expected: FAIL (the module does not exist; the constants do not exist):

```text
    throw new ERR_MODULE_NOT_FOUND(
Error [ERR_MODULE_NOT_FOUND]: Cannot find module 'src\TechStrap.Portal\wwwroot\js\portal-forms.js' imported from tests\TechStrap.Portal.Tests\js\porta

7 errors, the first three:
tests\TechStrap.Portal.Tests\Forms\PortalFormsHostTests.cs(115,71): error CS0117: 'FormCopy' does not contain a definition for 'Sending'
tests\TechStrap.Portal.Tests\Forms\PortalFormsHostTests.cs(129,18): error CS0117: 'FormCopy' does not contain a definition for 'Sending'
tests\TechStrap.Portal.Tests\Forms\PortalFormsHostTests.cs(149,69): error CS0117: 'FormCopy' does not contain a definition for 'CounterTemplate'

[-] Portal browser scripts.passes the node:test suite for the form helpers (sending state, copy button, character counter, text only) 120ms
[-] Portal browser scripts.has the test file and the module of the form helpers 6ms
Tests Passed: 2, Failed: 2, Skipped: 0, Inconclusive: 0, NotRun: 0
```

- [ ] **Step 3: Write the implementation**

The module defines two custom elements and two document-level listeners. It keeps the sending state in a `WeakMap` (a swap rewrites the attributes of an element it keeps), counts a line break as two characters (the browser posts CR LF; `maxlength` counts one), and reads the number to copy from the element that holds it, so the copy element carries no copy of the number. `App.razor` loads both modules; the contact page's own script tag goes (a script in a page body does not run after an enhanced navigation).

`src/TechStrap.Portal/wwwroot/js/portal-forms.js` (new)

```javascript
// The Portal's small form helpers (PHASE-09d, D-045 09d addendum). Vanilla JavaScript, no framework and no build step, loaded once from the document shell (App.razor).
//
// It does three things, and every page works without it:
//   1. "Sending" state: when a form that carries data-sending-label is submitted, its submit button is disabled and shows that label, so a double click cannot post twice (the server's one-time id is the
//      real guard; this is the visible half). The button comes back on pageshow (the back button) and after SENDING_RESET_MS (a post the visitor stopped).
//   2. <ts-copy-text target="ticket-number" data-label="Copy ticket number" data-copied="Copied" data-failed="..."></ts-copy-text>: a button that copies the text of the element with that id; when the browser
//      refuses (an insecure page, no permission) the text is selected instead, so Ctrl+C works.
//   3. <ts-char-count for="body" data-limit="100000" data-template="{0} of {1} characters" data-over="{0} over the limit"></ts-char-count>: a counter for a textarea, shown once the text reaches
//      NEAR_RATIO of the limit. A line break counts as two characters, because the browser sends CR LF and the server counts what it receives, while the textarea's own maxlength counts one.
//
// Why custom elements and document listeners: the Portal's pages are swapped into the open document by Blazor's enhanced navigation, which does NOT run a script that arrives with the new content (the
// spike of 09d proved it: the kb-suggestions module in a page body never ran after an enhanced click). A module in the document shell runs once, and the browser calls connectedCallback for an element
// the swap inserts; a listener on the document sees the forms of every later page.
//
// The words come from the server as data attributes (the *Copy constants), so the page owns its copy. Everything the module touches is passed in (the document, the window, the navigator, the timers), so the
// behavior is tested with `node --test` and no browser. Text is put on the page with textContent only; this file never parses text as markup, and a test fails if it starts to.

export const SENDING_RESET_MS = 60000;
export const NEAR_RATIO = 0.8;
export const COUNT_ANNOUNCE_MS = 1000;
export const COPIED_RESET_MS = 4000;

/** The length the server will see for a textarea's value: the browser sends each line break as CR LF, so every LF counts as two characters. */
export function serverLength(value) {
    const text = typeof value === 'string' ? value.replace(/\r\n/g, '\n') : '';
    let breaks = 0;
    for (let i = 0; i < text.length; i += 1) {
        if (text.charCodeAt(i) === 10) {
            breaks += 1;
        }
    }

    return text.length + breaks;
}

/** 100000 -> "100,000" (the Portal's copy is English). */
export function groupDigits(number) {
    return String(Math.trunc(Math.abs(Number(number)) || 0)).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
}

/** Replaces {0} and {1} in a template; a missing template gives an empty string. */
export function fill(template, first, second) {
    if (typeof template !== 'string') {
        return '';
    }

    return template.split('{0}').join(first).split('{1}').join(second);
}

/**
 * What the counter shows for a text of `used` characters against `limit`: nothing below NEAR_RATIO of the limit; the count from there; and, above the limit, how far over it is.
 * Returns { visible, over, text }.
 */
export function counterText({ used, limit, template, overTemplate }) {
    if (!(limit > 0) || used < limit * NEAR_RATIO) {
        return { visible: false, over: false, text: '' };
    }

    if (used > limit) {
        return { visible: true, over: true, text: fill(overTemplate, groupDigits(used - limit), groupDigits(limit)) };
    }

    return { visible: true, over: false, text: fill(template, groupDigits(used), groupDigits(limit)) };
}

/** The first submit button of a form, or null. */
export function submitButtonOf(form) {
    return form.querySelector('button[type="submit"]');
}

/**
 * The sending state of the forms of the page. `onSubmit` is the capturing document listener; `onPageShow` restores a form the back button brings back. State is kept in a WeakMap, never on the markup, because
 * an enhanced navigation rewrites the attributes of an element it keeps.
 */
export function createSendingState({ setTimer = (fn, ms) => setTimeout(fn, ms), clearTimer = (id) => clearTimeout(id), resetAfterMs = SENDING_RESET_MS } = {}) {
    const sending = new WeakMap();
    const active = new Set();

    function reset(form) {
        const state = sending.get(form);
        if (!state) {
            return;
        }

        clearTimer(state.timer);
        state.button.disabled = false;
        state.button.textContent = state.label;
        sending.delete(form);
        active.delete(form);
    }

    return {
        /** True when the form is now (or already was) in the sending state; a second submit of a form that is sending is canceled. */
        onSubmit(event) {
            const form = event.target;
            if (!form || typeof form.getAttribute !== 'function') {
                return false;
            }

            const text = form.getAttribute('data-sending-label');
            if (typeof text !== 'string' || text.length === 0) {
                return false;
            }

            if (sending.has(form)) {
                event.preventDefault();
                return true;
            }

            const button = submitButtonOf(form);
            if (!button) {
                return false;
            }

            const timer = setTimer(() => reset(form), resetAfterMs);
            sending.set(form, { button, label: button.textContent, timer });
            active.add(form);
            button.disabled = true;
            button.textContent = text;
            return true;
        },

        /** After the back button (a page restored from the cache), every form that was sending is usable again. */
        onPageShow(event) {
            if (event && event.persisted) {
                for (const form of [...active]) {
                    reset(form);
                }
            }
        },

        isSending: (form) => sending.has(form),
    };
}

/** Writes `text` to the clipboard; false (never an exception) when the browser refuses or has no clipboard. */
export async function copyText(text, nav = globalThis.navigator) {
    try {
        if (!nav || !nav.clipboard || typeof nav.clipboard.writeText !== 'function') {
            return false;
        }

        await nav.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

/** Selects the contents of `node`, so the visitor can press Ctrl+C; false when that is not possible. */
export function selectContents(node, doc) {
    try {
        const selection = doc.getSelection ? doc.getSelection() : null;
        if (!node || !selection) {
            return false;
        }

        selection.selectAllChildren(node);
        return true;
    } catch {
        return false;
    }
}

/** The words of a copy button: each data attribute, falling back to English when missing or empty. */
export function copyWords(element) {
    const read = (name, fallback) => {
        const value = element.getAttribute(name);
        return typeof value === 'string' && value.length > 0 ? value : fallback;
    };

    return { label: read('data-label', 'Copy'), copied: read('data-copied', 'Copied'), failed: read('data-failed', 'Press Ctrl+C to copy') };
}

/** Defines <ts-copy-text> and <ts-char-count> with the given environment (the browser's own, at the bottom of this file). */
export function defineElements(env) {
    if (!env.customElements) {
        return;
    }

    if (!env.customElements.get('ts-copy-text')) {
        env.customElements.define('ts-copy-text', class extends env.HTMLElement {
            connectedCallback() {
                const target = env.document.getElementById(this.getAttribute('target') ?? '');
                if (!target) {
                    return;
                }

                const words = copyWords(this);
                const button = env.document.createElement('button');
                button.type = 'button';
                button.className = 'btn btn-outline-primary ts-copy-button';
                button.textContent = words.label;
                const status = env.document.createElement('span');
                status.className = 'ts-copy-status';
                status.setAttribute('role', 'status');
                this.replaceChildren(button, status);
                this._timer = null;
                this._onClick = async () => {
                    const copied = await copyText((target.textContent ?? '').trim(), env.navigator);
                    if (!copied) {
                        selectContents(target, env.document);
                    }

                    status.textContent = copied ? words.copied : words.failed;
                    if (this._timer !== null) {
                        env.clearTimeout(this._timer);
                    }

                    this._timer = env.setTimeout(() => { status.textContent = ''; this._timer = null; }, COPIED_RESET_MS);
                };
                this._button = button;
                button.addEventListener('click', this._onClick);
            }

            disconnectedCallback() {
                if (this._button && this._onClick) {
                    this._button.removeEventListener('click', this._onClick);
                }

                if (this._timer !== null && this._timer !== undefined) {
                    env.clearTimeout(this._timer);
                }

                this._button = null;
                this._onClick = null;
                this._timer = null;
            }
        });
    }

    if (!env.customElements.get('ts-char-count')) {
        env.customElements.define('ts-char-count', class extends env.HTMLElement {
            connectedCallback() {
                const field = env.document.getElementById(this.getAttribute('for') ?? '');
                const limit = Number(this.getAttribute('data-limit'));
                if (!field || !(limit > 0)) {
                    return;
                }

                const visible = env.document.createElement('span');
                visible.className = 'ts-count-text';
                visible.setAttribute('aria-hidden', 'true');
                const speech = env.document.createElement('span');
                speech.className = 'visually-hidden';
                speech.setAttribute('role', 'status');
                this.replaceChildren(visible, speech);
                this._timer = null;
                this._field = field;
                this._onInput = () => {
                    const state = counterText({
                        used: serverLength(field.value),
                        limit,
                        template: this.getAttribute('data-template'),
                        overTemplate: this.getAttribute('data-over'),
                    });
                    visible.textContent = state.text;
                    this.hidden = !state.visible;
                    this.classList?.toggle('ts-count-over', state.over);
                    if (this._timer !== null) {
                        env.clearTimeout(this._timer);
                    }

                    // The screen reader hears the count once typing pauses, not on every key.
                    this._timer = env.setTimeout(() => { speech.textContent = state.text; this._timer = null; }, COUNT_ANNOUNCE_MS);
                };
                this.hidden = true;
                field.addEventListener('input', this._onInput);
                this._onInput();
            }

            disconnectedCallback() {
                if (this._field && this._onInput) {
                    this._field.removeEventListener('input', this._onInput);
                }

                if (this._timer !== null && this._timer !== undefined) {
                    env.clearTimeout(this._timer);
                }

                this._field = null;
                this._onInput = null;
                this._timer = null;
            }
        });
    }
}

/** Starts the document-level listeners of the sending state. Returns the state, for a test. */
export function installSendingState(env, options) {
    const state = createSendingState(options);
    env.document.addEventListener('submit', (event) => state.onSubmit(event), true);
    env.window.addEventListener('pageshow', (event) => state.onPageShow(event));
    return state;
}

if (typeof customElements !== 'undefined' && typeof document !== 'undefined') {
    const env = {
        customElements,
        HTMLElement,
        document,
        window,
        navigator,
        setTimeout: (...args) => setTimeout(...args),
        clearTimeout: (...args) => clearTimeout(...args),
    };
    defineElements(env);
    installSendingState(env);
}
```

`src/TechStrap.Portal/Components/App.razor`

```diff
--- a/src/TechStrap.Portal/Components/App.razor
+++ b/src/TechStrap.Portal/Components/App.razor
@@ -15,6 +15,10 @@
 <body>
     <Routes />
     <script src="@Assets["_framework/blazor.web.js"]"></script>
+    @* One module each, loaded once with the document. Blazor's enhanced navigation swaps page content into the open document and does not run a script that arrives with it, so a script in a page body would
+       never run for a visitor who got there by a click. A module here defines custom elements (and, for the forms, document listeners) that work on whatever the swap inserts. *@
+    <script type="module" src="@Assets["js/kb-suggestions.js"]"></script>
+    <script type="module" src="@Assets["js/portal-forms.js"]"></script>
 </body>
 
 </html>
```

`src/TechStrap.Portal/Components/Pages/Contact.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/Contact.razor
+++ b/src/TechStrap.Portal/Components/Pages/Contact.razor
@@ -13,7 +13,7 @@
     {
         <p class="alert alert-warning" role="alert">@Notice</p>
     }
-    <form method="post" action="@PortalRoutes.Contact(theme.Key)" enctype="multipart/form-data" novalidate @formname="@FormHandler" @onsubmit="SubmitAsync" class="ts-form">
+    <form method="post" action="@PortalRoutes.Contact(theme.Key)" enctype="multipart/form-data" novalidate @formname="@FormHandler" @onsubmit="SubmitAsync" class="ts-form" data-sending-label="@FormCopy.Sending">
         <AntiforgeryToken />
         <FormGuard Name="Form.SubmitId" />
         <FormField Field="@FormFields.Name" Name="Form.Name" Label="@ContactCopy.NameLabel" Value="@Form?.Name" MaxLength="@IntakeLimits.NameMaxLength" Autocomplete="name" Error="@ErrorOf(FormFields.Name)" />
@@ -25,7 +25,6 @@
         <HoneypotField Name="Form.Website" Value="@Form?.Website" />
         <button type="submit" class="btn btn-primary">@ContactCopy.Submit</button>
     </form>
-    <script type="module" src="@Assets["js/kb-suggestions.js"]"></script>
 }
 else if (UnavailableMessage is not null)
 {
```

`src/TechStrap.Portal/Components/Pages/ContactReceived.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/ContactReceived.razor
+++ b/src/TechStrap.Portal/Components/Pages/ContactReceived.razor
@@ -7,7 +7,7 @@
     <h1>@ContactCopy.ReceivedHeading</h1>
     @if (TicketNumber is not null)
     {
-        <p>@ContactCopy.YourNumber <strong class="ts-ticket-number">@TicketNumber</strong></p>
+        <p>@ContactCopy.YourNumber <strong class="ts-ticket-number" id="ticket-number">@TicketNumber</strong> <ts-copy-text target="ticket-number" data-label="@ContactCopy.CopyNumber" data-copied="@ContactCopy.CopyNumberDone" data-failed="@ContactCopy.CopyNumberFailed" class="ts-copy"></ts-copy-text></p>
         <p>@ContactCopy.ReceivedNext</p>
     }
     else
```

`src/TechStrap.Portal/Components/Pages/LostLink.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/LostLink.razor
+++ b/src/TechStrap.Portal/Components/Pages/LostLink.razor
@@ -18,7 +18,7 @@
         {
             <p class="alert alert-warning" role="alert">@Notice</p>
         }
-        <form method="post" action="@PortalRoutes.LostLink(theme.Key)" novalidate @formname="@FormHandler" @onsubmit="SubmitAsync" class="ts-form">
+        <form method="post" action="@PortalRoutes.LostLink(theme.Key)" novalidate @formname="@FormHandler" @onsubmit="SubmitAsync" class="ts-form" data-sending-label="@FormCopy.Sending">
             <AntiforgeryToken />
             <FormGuard Name="Form.SubmitId" />
             <FormField Field="@FormFields.Email" Name="Form.Email" Label="@LostLinkCopy.EmailLabel" Value="@Form?.Email" MaxLength="@TechStrap.Contracts.Intake.IntakeLimits.EmailMaxLength" Type="email" Autocomplete="email" Error="@ErrorOf(FormFields.Email)" />
```

`src/TechStrap.Portal/Components/Pages/Ticket.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/Ticket.razor
+++ b/src/TechStrap.Portal/Components/Pages/Ticket.razor
@@ -38,7 +38,7 @@ else if (UnavailableMessage is not null)
 }
 
 @code {
-    private RenderFragment ReplyForm(string button) => @<form method="post" action="@PortalRoutes.Ticket(_token)" enctype="multipart/form-data" novalidate @formname="@ReplyHandler" @onsubmit="ReplyAsync" class="ts-form">
+    private RenderFragment ReplyForm(string button) => @<form method="post" action="@PortalRoutes.Ticket(_token)" enctype="multipart/form-data" novalidate @formname="@ReplyHandler" @onsubmit="ReplyAsync" class="ts-form" data-sending-label="@FormCopy.Sending">
         <AntiforgeryToken />
         <FormGuard Name="Reply.SubmitId" />
         <FormField Field="@FormFields.Body" Name="Reply.Body" Label="@TicketCopy.ReplyLabel" Value="@Reply?.Body" MaxLength="@IntakeLimits.BodyMaxLength" Rows="6" Error="@ErrorOf(FormFields.Body)" />
```

`src/TechStrap.Portal/Components/Ui/FormField.razor`

```diff
--- a/src/TechStrap.Portal/Components/Ui/FormField.razor
+++ b/src/TechStrap.Portal/Components/Ui/FormField.razor
@@ -3,6 +3,7 @@
     @if (Rows > 0)
     {
         <textarea id="@Field" name="@Name" class="form-control" rows="@Rows" maxlength="@MaxLength" required aria-describedby="@(Error is null ? null : FormFields.ErrorId(Field))" aria-invalid="@(Error is null ? null : "true")">@Value</textarea>
+        <ts-char-count for="@Field" data-limit="@MaxLength" data-template="@FormCopy.CounterTemplate" data-over="@FormCopy.CounterOver" class="ts-char-count" hidden></ts-char-count>
     }
     else
     {
```

`src/TechStrap.Portal/Forms/ContactCopy.cs`

```diff
--- a/src/TechStrap.Portal/Forms/ContactCopy.cs
+++ b/src/TechStrap.Portal/Forms/ContactCopy.cs
@@ -27,6 +27,10 @@ public static class ContactCopy
     public const string YourNumber = "Your ticket number is";
     public const string ReceivedNext = "We have emailed you a link. Use it to follow the conversation and reply.";
     public const string ReceivedGeneric = "We have emailed you a link to follow it. Check your inbox and your spam folder.";
+    // The copy button next to the ticket number (<c>portal-forms.js</c>); without script the number is still there to select.
+    public const string CopyNumber = "Copy ticket number";
+    public const string CopyNumberDone = "Copied";
+    public const string CopyNumberFailed = "Could not copy. The number is selected: press Ctrl+C.";
     public const string LostLinkPrompt = "Can't find the email?";
 
     public static string Title(string productName) => $"Contact {productName} support";
```

`src/TechStrap.Portal/Forms/FormCopy.cs`

```diff
--- a/src/TechStrap.Portal/Forms/FormCopy.cs
+++ b/src/TechStrap.Portal/Forms/FormCopy.cs
@@ -19,6 +19,14 @@ public static class FormCopy
     // The codes the Portal itself adds before it asks the API.
     public const string NameRequiredCode = "name-required";
 
+    /// <summary>The words on the submit button while a form is being sent (<c>portal-forms.js</c> shows them; U+2026 is the ellipsis, written as an escape so the source stays ASCII).</summary>
+    public const string Sending = "Sending\u2026";
+
+    // The character counter under a long text field (<c>portal-forms.js</c>). {0} is the count, {1} the limit; it appears when the text reaches 80 percent of the limit. A line break is posted as two characters, which is why a
+    // text can pass the limit although the browser's own maxlength allowed it.
+    public const string CounterTemplate = "{0} of {1} characters used";
+    public const string CounterOver = "{0} characters over the limit of {1}. A line break counts as two.";
+
     public const string RateLimited = "Too many attempts. Wait a few minutes and try again. What you wrote is still here.";
     public const string Unavailable = "We could not send that just now. What you wrote is still here: try again in a moment.";
 
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
node --test tests/TechStrap.Portal.Tests/js/portal-forms.test.mjs
dotnet test --project tests/TechStrap.Portal.Tests -c Release
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/PortalScripts.Tests.ps1
```

Expected: 41 node tests, 1,545 Portal tests and 4 Pester tests pass:

```text
# tests 41
# pass 41
# fail 0
```
```text
  total: 1545
  failed: 0
  succeeded: 1545
```
```text
Tests Passed: 4, Failed: 0, Skipped: 0, Inconclusive: 0, NotRun: 0
```

Then check it in a browser once (the spike's check, kept as the scripts in the appendix, not as a test): start the stand-in API and the Portal, and run `node $T/browser_t3.mjs` (the product home in Edge headless over the DevTools protocol, the contact button, the subject box, the counter, the double click, the copy button). With the module in the shell the suggestions appear after the enhanced click (`customElements.get('ts-kb-suggestions')` is defined; the same click before this change left it undefined), a long text shows the counter, a double click on "Send message" posts once (the stand-in API counts one ticket), and "Copy ticket number" copies on the received page. Stop the Portal, the stand-in API and Edge when you are done.

- [ ] **Step 5: Prove each pin with a recorded mutation**

Save `$T\specs\m3.py` (`NODE` is `node --test tests/TechStrap.Portal.Tests/js/portal-forms.test.mjs`):

```python
"""Mutations of Task 3 (portal-forms.js and its markup). Run from the repository root."""
PORTAL = ["dotnet", "test", "--project", "tests/TechStrap.Portal.Tests", "-c", "Release", "--filter-query"]
NODE = ["node", "--test", "tests/TechStrap.Portal.Tests/js/portal-forms.test.mjs"]
JS = "src/TechStrap.Portal/wwwroot/js/portal-forms.js"
FORMS = PORTAL + ["/*/*/PortalFormsHostTests/*"]

MUTATIONS = [
    ("1 a line break counts as one character", JS, [("    return text.length + breaks;", "    return text.length;")], NODE),
    ("2 the counter shows from 90 percent", JS, [("export const NEAR_RATIO = 0.8;", "export const NEAR_RATIO = 0.9;")], NODE),
    ("3 the stopped-post reset is ten minutes", JS, [("export const SENDING_RESET_MS = 60000;", "export const SENDING_RESET_MS = 600000;")], NODE),
    ("4 a second submit is not canceled", JS, [("            if (sending.has(form)) {\n                event.preventDefault();\n                return true;\n            }", "            if (sending.has(form)) {\n                return true;\n            }")], NODE),
    ("5 pageshow resets even without the back button", JS, [("if (event && event.persisted) {", "if (event) {")], NODE),
    ("6 a refused clipboard does not select the number", JS, [("                    if (!copied) {\n                        selectContents(target, env.document);\n                    }", "                    if (copied) {\n                        selectContents(target, env.document);\n                    }")], NODE),
    ("7 the counter counts the textarea's own length", JS, [("used: serverLength(field.value),", "used: String(field.value).length,")], NODE),
    ("8 the button keeps its label while sending", JS, [("            button.textContent = text;\n            return true;", "            return true;")], NODE),
    ("9 the counter text is written as markup-capable property", JS, [("                    visible.textContent = state.text;", "                    visible.innerText = state.text;")], NODE),
    ("10 the screen reader hears every key", JS, [("this._timer = env.setTimeout(() => { speech.textContent = state.text; this._timer = null; }, COUNT_ANNOUNCE_MS);", "speech.textContent = state.text;")], NODE),
    ("11 the shell no longer loads portal-forms.js", "src/TechStrap.Portal/Components/App.razor", [('    <script type="module" src="@Assets["js/portal-forms.js"]"></script>\n', "")], FORMS),
    ("12 the contact page loads the module itself again", "src/TechStrap.Portal/Components/Pages/Contact.razor", [("    </form>\n    </div>\n", '    </form>\n    <script type="module" src="@Assets["js/kb-suggestions.js"]"></script>\n    </div>\n')], FORMS),
    ("13 the contact form has no sending words", "src/TechStrap.Portal/Components/Pages/Contact.razor", [(' data-sending-label="@FormCopy.Sending"', "")], FORMS),
    ("14 the counter limit is not the field's", "src/TechStrap.Portal/Components/Ui/FormField.razor", [('data-limit="@MaxLength"', 'data-limit="1000"')], FORMS),
    ("15 the counter is not hidden before script", "src/TechStrap.Portal/Components/Ui/FormField.razor", [(' class="ts-char-count" hidden>', ' class="ts-char-count">')], FORMS),
    ("16 the copy button's words are typed into the page", "src/TechStrap.Portal/Components/Pages/ContactReceived.razor", [('data-label="@ContactCopy.CopyNumber"', 'data-label="Copy"')], FORMS),
    ("17 the number has no id for the button to find", "src/TechStrap.Portal/Components/Pages/ContactReceived.razor", [(' id="ticket-number"', "")], FORMS),
    ("18 a copy button without a number", "src/TechStrap.Portal/Components/Pages/ContactReceived.razor", [("    else\n    {\n        <p>@ContactCopy.ReceivedGeneric</p>", '    else\n    {\n        <ts-copy-text target="ticket-number"></ts-copy-text>\n        <p>@ContactCopy.ReceivedGeneric</p>')], FORMS),
    ("19 the sending words are not escaped in the source", "src/TechStrap.Portal/Forms/FormCopy.cs", [('"Sending\\u2026"', '"Sending..."')], FORMS),
    ("20 the module serves an unknown type", "src/TechStrap.Portal/wwwroot/js/portal-forms.js", [("export const COPIED_RESET_MS = 4000;", "export const COPIED_RESET_MS = 4000;\nexport const X = '<b>'; document.body.innerHTML = X;")], NODE),
]
```

Run them (in two foreground batches, `1`..`10` and `11`..`20`) and record the results:

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | a line break counts as one character | `portal-forms.js` | KILLED |
| 2 | the counter shows from 90 percent | `portal-forms.js` | KILLED |
| 3 | the stopped-post reset is ten minutes | `portal-forms.js` | KILLED |
| 4 | a second submit is not canceled | `portal-forms.js` | KILLED |
| 5 | pageshow resets even without the back button | `portal-forms.js` | KILLED |
| 6 | a refused clipboard does not select the number | `portal-forms.js` | KILLED |
| 7 | the counter counts the textarea's own length | `portal-forms.js` | KILLED |
| 8 | the button keeps its label while sending | `portal-forms.js` | KILLED |
| 9 | the counter text is written as markup-capable property | `portal-forms.js` | KILLED |
| 10 | the screen reader hears every key | `portal-forms.js` | KILLED |
| 11 | the shell no longer loads portal-forms.js | `App.razor` | KILLED (8 failing) |
| 12 | the contact page loads the module itself again | `Contact.razor` | KILLED (1 failing) |
| 13 | the contact form has no sending words | `Contact.razor` | KILLED (1 failing) |
| 14 | the counter limit is not the field's | `FormField.razor` | KILLED (1 failing) |
| 15 | the counter is not hidden before script | `FormField.razor` | KILLED (1 failing) |
| 16 | the copy button's words are typed into the page | `ContactReceived.razor` | KILLED (1 failing) |
| 17 | the number has no id for the button to find | `ContactReceived.razor` | KILLED (1 failing) |
| 18 | a copy button without a number | `ContactReceived.razor` | KILLED (1 failing) |
| 19 | the sending words are not escaped in the source | `FormCopy.cs` | KILLED (2 failing) |
| 20 | the module serves an unknown type | `portal-forms.js` | KILLED |

Every mutation is killed; there are no survivors in this task. (A node mutation has no failing count: `node --test` is the command.)

- [ ] **Step 6: Run the full suites and commit**

```bash
dotnet test --project tests/TechStrap.Portal.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/PortalScripts.Tests.ps1
```

Expected: 1,545 and the architecture suite pass; the script tests pass.

```bash
git add -A
git diff --cached --stat
git commit -m "feat: PHASE-09d portal-forms.js (sending state, copy button, counter), modules loaded from the shell (D-045)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

### Task 4: T16: the styling, the accessibility pass, the `<base>` fix and the tests that pin them

**Review Focus pin:** 4 (the error summary's links and the skip link work under every path, resolved against the base the way a browser does, and move the focus; one `h1` per page; focus visible in forced colors; a state never color alone; every link a visitor has to hit is 44px) and 3 (the new stylesheet adds nothing the policy blocks; the only change to an author's body is an `h1` shown as an `h2`) and 5 (the neutral 404 stays byte for byte the same).

**Files:**

- Create: `src/TechStrap.Portal/Routing/PageLinks.cs`
- Create: `src/TechStrap.Portal/Components/BodyHeadings.cs`
- Create: `src/TechStrap.Portal/Styles/_layout.scss`, `src/TechStrap.Portal/Styles/_a11y.scss`
- Modify: `src/TechStrap.Portal/Styles/app.scss`, `_components.scss`, `_tokens.scss`
- Modify: `assets/brand/scss/_brand-tokens.scss`, `docs/BRAND.md` (six Portal tokens and the two widths)
- Modify: `src/TechStrap.Portal/Components/Layout/PortalLayout.razor`, `PortalLayout.razor.cs`, `ProductHeader.razor`, `ProductFooter.razor`
- Modify: `src/TechStrap.Portal/Components/Ui/ErrorSummary.razor`, `ProductUnavailable.razor`
- Modify: `src/TechStrap.Portal/Components/Pages/Contact.razor`, `ContactReceived.razor`, `LostLink.razor`, `Ticket.razor`, `KbArticle.razor`, `KbHome.razor`, `KbSearch.razor`, `ProductHome.razor`
- Modify: `src/TechStrap.Portal/Components/Kb/KbArticleBody.razor`, `src/TechStrap.Portal/Components/Tickets/CustomerMessageBody.razor`
- Modify: `src/TechStrap.Portal/Components/ShellCopy.cs`, `KbCopy.cs`, `src/TechStrap.Portal/Forms/ContactCopy.cs`, `LostLinkForm.cs`, `src/TechStrap.Portal/Tickets/TicketCopy.cs`, `src/TechStrap.Portal/Routing/PortalRoutes.cs`
- Modify: `src/TechStrap.Admin/Components/Showcase/PaletteSwatches.razor.cs` (the Admin's style-guide palette lists the six tokens)
- Test (create): `tests/TechStrap.Portal.Tests/Routing/PageLinksTests.cs`, `Components/LayoutLandmarkHostTests.cs`, `Components/PageKit.cs`, `Forms/ErrorSummaryLinkHostTests.cs`, `HeadingHostTests.cs`, `ResponsiveStyleTests.cs`, `TokenContrastTests.cs`, `CspStyleTests.cs`, `Components/BodyHeadingsTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/StyleBuildTests.cs`, `Components/PortalLayoutTests.cs`, `Forms/ContactPostHostTests.cs`, `Forms/ContactReceivedHostTests.cs`, `Tickets/LostLinkHostTests.cs`, `Tickets/TicketPageHostTests.cs`, `Tickets/TicketReplyHostTests.cs`, `tests/TechStrap.Admin.Tests/StyleBuildTests.cs`

**Interfaces:**
- Consumes: `NavigationManager.Uri` (absolute, never printed), `PortalRoutes` constants, `ShellCopy`, the 09c `KbSearchBox`, `CompiledCss` (`Declarations`, `InMedia`, `OutsideMedia`), `BrandTokenTable`, `ProductAccent.ContrastRatio`, `HeadingHostTests`' Admin pattern, `FormTestKit`, `TicketTestKit`, `KbTestKit`.
- Produces:
  - `PageLinks.ToFragment(string currentUri, string fragment, bool keepQuery) : string` (the current root-relative path, plus, with `keepQuery`, only the query parameters the page reads, plus `#fragment`) and `internal PageLinks.KnownParameters(string path)`; `PortalRoutes.PrefillParameters`.
  - `BodyHeadings.DemoteTitle(string html) : string` (`<h1>` and `</h1>` become `<h2>` and `</h2>`; nothing else changes), used by `KbArticleBody` and `CustomerMessageBody`.
  - Copy: `ShellCopy.SkipToMain`, `NavLabel`, `NavHelp`, `NavContact`, `FooterNavLabel`; `ContactCopy.RequiredNote`; `LostLinkCopy.RequiredNote`; `TicketCopy.JumpToReply`, `ReplyNote`; `KbCopy.SearchPageHeading` ("Search the help center").
  - Markup: `a.ts-skip-link` (product pages only, `data-enhance-nav="false"`), `main#main[tabindex=-1]`, `header.ts-product-header > nav[aria-label=Main]` (the product, the help center and contact), `nav.ts-product-footer[aria-label]`, `.ts-reading` around reading pages, `.ts-form-note`, `a.ts-jump-reply` and `h2#reply[tabindex=-1]` on the ticket page, `.ts-kb-list--cards`; the error summary's links are `PageLinks.ToFragment(Navigation.Uri, field, keepQuery: false)` and the summary has `data-enhance-nav="false"`.
  - Styles: `--ts-reading-width` (40rem), `--ts-wide-width` (64rem); BRAND.md tokens `--p-error`, `--p-error-bg`, `--p-success`, `--p-success-bg`, `--p-warn`, `--p-warn-bg`; `$enable-smooth-scroll: false`.
  - Tests: `PageLinksTests`, `LayoutLandmarkHostTests`, `ErrorSummaryLinkHostTests`, `HeadingHostTests`, `ResponsiveStyleTests`, `TokenContrastTests`, `CspStyleTests`, `BodyHeadingsTests` and the helper `PageKit` (`Resolve`, `IsJumpWithin`, `FirstFocusable`).

- [ ] **Step 1: Write the failing tests**

The tests read what a browser would: `PageKit.Resolve` resolves a link against the document's `base` element and the page's own address, so a bare `#email` comes out as the home page (and a control test proves it, so the tests would have caught the old markup). `ErrorSummaryLinkHostTests` posts the three forms invalid and checks every summary link jumps within the page to an element that exists; `LayoutLandmarkHostTests` checks the skip link is the first thing Tab reaches, that it works with the prefill, that `main` can take focus, that the navigation regions are named, that a neutral page has none of it, and that a kept KB page never carries another visitor's parameters; `HeadingHostTests` checks one `h1` on every kind of page (states included) and that an `h1` in a body is an `h2`. The style tests read the compiled CSS: the widths as tokens, the breakpoints, the 44px targets, the skip link, the error colors, forced colors, reduced motion and no smooth scrolling, a rule for every `ts-*` class the markup uses, and the contrast of every token pair. Existing tests change where the markup does (the summary's links and attribute, the footer's tag, the six-token count, the four places a ticket's token appears, and the received page's "no number" check, which now reads the page's text because the skip link keeps the query string).

`tests/TechStrap.Portal.Tests/Components/BodyHeadingsTests.cs` (new)

```csharp
using TechStrap.Portal.Components;

namespace TechStrap.Portal.Tests.Components;

/// <summary>A body an author wrote may carry an <c>h1</c> (a Markdown line that starts with <c>#</c>); the page's own title is its one <c>h1</c>, so the body's is shown as an <c>h2</c> and nothing else changes.</summary>
public sealed class BodyHeadingsTests
{
    [Fact]
    public void An_h1_becomes_an_h2_and_nothing_else_changes()
    {
        BodyHeadings.DemoteTitle("<h1>Title</h1><h2>Sub</h2><p>Text with h1 in it and &lt;h1&gt;.</p>").ShouldBe("<h2>Title</h2><h2>Sub</h2><p>Text with h1 in it and &lt;h1&gt;.</p>");
    }

    [Fact]
    public void Every_h1_is_demoted_and_the_other_levels_are_left_alone()
    {
        BodyHeadings.DemoteTitle("<h1>A</h1><h1>B</h1><h3>C</h3><h6>D</h6>").ShouldBe("<h2>A</h2><h2>B</h2><h3>C</h3><h6>D</h6>");
    }

    [Theory]
    [InlineData("")]
    [InlineData("<p>No headings.</p>")]
    [InlineData("<h2>Already h2</h2>")]
    [InlineData("<h10>Not a heading</h10>")]
    public void A_body_without_an_h1_is_returned_byte_for_byte(string html)
    {
        BodyHeadings.DemoteTitle(html).ShouldBeSameAs(html);
    }
}
```

`tests/TechStrap.Portal.Tests/Components/LayoutLandmarkHostTests.cs` (new)

```csharp
using System.Text.RegularExpressions;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Components;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Kb;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Components;

/// <summary>
/// The page frame at the host (PHASE-09 T16, UX brief, Review Focus 4): a product page starts with a skip link that is the first thing Tab reaches and that, resolved the way a browser resolves it under
/// <c>base href="/"</c>, jumps within the same page to <c>main</c>; <c>main</c> can take focus; there is a navigation landmark for the product's header and one for its footer, each named, and exactly one
/// contentinfo; a neutral page (the root, not-found, error) has none of the product frame, so every neutral 404 stays byte for byte the same.
/// </summary>
public sealed class LayoutLandmarkHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<(string Html, AngleSharp.Dom.IDocument Dom)> GetAsync(PortalFactory factory, string pathAndQuery)
    {
        using var client = FormTestKit.Client(factory);
        using var response = await client.GetAsync(pathAndQuery, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);
        return (html, PageKit.Parse(html));
    }

    public static TheoryData<string> ProductPages => ["/p/paperplane", "/p/paperplane/contact", "/p/paperplane/lost-link", "/p/paperplane/contact/received", "/p/paperplane/kb", "/p/paperplane/kb/search", "/t/AbC-_0123456789AbC-_0123456789AbC-_01234567"];

    private static PortalFactory Host()
    {
        var factory = TicketTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, new[] { new PublicKbCategoryDto("accounts", "Accounts", "About accounts", 1) });
        return factory;
    }

    [Theory]
    [MemberData(nameof(ProductPages))]
    public async Task A_product_page_starts_with_the_skip_link_which_jumps_within_the_page_to_main(string path)
    {
        await using var factory = Host();

        var (_, dom) = await GetAsync(factory, path);

        var first = PageKit.FirstFocusable(dom).ShouldNotBeNull();
        first.ClassList.ShouldContain("ts-skip-link");
        first.TextContent.ShouldBe(ShellCopy.SkipToMain);
        first.GetAttribute("data-enhance-nav").ShouldBe("false", "Blazor's enhanced navigation would scroll but leave the focus on the link");
        var href = first.GetAttribute("href")!;
        href.ShouldStartWith("/", Case.Sensitive, "root-relative, so the document's base cannot send it to the home page");
        PageKit.IsJumpWithin(dom, PageKit.Origin + path, href).ShouldBeTrue($"{href} must be a jump within {path}");
        PageKit.Resolve(dom, PageKit.Origin + path, href).Fragment.ShouldBe("#main");
        dom.QuerySelectorAll("a.ts-skip-link").Count.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(ProductPages))]
    public async Task Main_is_the_skip_links_target_and_can_take_focus_and_there_is_one_h1_inside_it(string path)
    {
        await using var factory = Host();

        var (_, dom) = await GetAsync(factory, path);

        var main = dom.QuerySelectorAll("main").ShouldHaveSingleItem();
        main.Id.ShouldBe("main");
        main.GetAttribute("tabindex").ShouldBe("-1");
        dom.GetElementById("main").ShouldBeSameAs(main);
        main.QuerySelectorAll("h1").Count.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(ProductPages))]
    public async Task A_product_page_has_a_named_navigation_for_its_header_and_one_for_its_footer_and_one_contentinfo(string path)
    {
        await using var factory = Host();

        var (_, dom) = await GetAsync(factory, path);

        var headerNav = dom.QuerySelector("header.ts-product-header nav").ShouldNotBeNull();
        headerNav.GetAttribute("aria-label").ShouldBe(ShellCopy.NavLabel);
        headerNav.QuerySelectorAll("a").Select(a => a.GetAttribute("href")).ShouldBe(["/p/paperplane", "/p/paperplane/kb", "/p/paperplane/contact"]);
        headerNav.QuerySelectorAll("li a").Select(a => a.TextContent).ShouldBe([ShellCopy.NavHelp, ShellCopy.NavContact]);
        var footerNav = dom.QuerySelector("nav.ts-product-footer").ShouldNotBeNull();
        footerNav.GetAttribute("aria-label").ShouldBe(ShellCopy.FooterNavLabel);
        footerNav.QuerySelector("a")!.GetAttribute("href").ShouldBe("/p/paperplane/lost-link");

        // Every navigation region has its own name, so a screen reader's landmark list tells them apart. (The breadcrumb trail and the pager are navigation regions of their own and are named too.)
        var names = dom.QuerySelectorAll("nav").Select(n => n.GetAttribute("aria-label")).ToList();
        names.ShouldAllBe(name => !string.IsNullOrWhiteSpace(name));
        names.Distinct().Count().ShouldBe(names.Count);
        dom.QuerySelectorAll("footer").ShouldHaveSingleItem().ClassList.ShouldContain("ts-powered");
        dom.QuerySelectorAll("header.ts-product-header").ShouldHaveSingleItem(); // (a message of the conversation has a header element of its own; it is not a banner)
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/nope")]
    [InlineData("/error")]
    public async Task A_neutral_page_has_no_skip_link_header_or_navigation_but_still_a_main_that_takes_focus(string path)
    {
        await using var factory = FormTestKit.Factory(product: false);

        var (_, dom) = await GetAsync(factory, path);

        dom.QuerySelectorAll("a.ts-skip-link").ShouldBeEmpty("it is built from the address, and a neutral page must read the same for every address");
        dom.QuerySelectorAll("header, nav").ShouldBeEmpty();
        var main = dom.QuerySelectorAll("main").ShouldHaveSingleItem();
        main.Id.ShouldBe("main");
        main.GetAttribute("tabindex").ShouldBe("-1");
        dom.QuerySelectorAll("h1").Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_skip_link_keeps_what_the_page_reads_from_its_query_so_a_jump_is_not_a_reload_and_drops_everything_else()
    {
        await using var factory = Host();
        const string Path = "/p/paperplane/contact?subject=Printer%20jam&name=Ada&utm_source=mail&Website=spam";

        var (html, dom) = await GetAsync(factory, Path);

        var href = dom.QuerySelector("a.ts-skip-link")!.GetAttribute("href")!;
        href.ShouldBe("/p/paperplane/contact?subject=Printer%20jam&name=Ada#main");
        html.ShouldNotContain("utm_source");
        html.ShouldNotContain("spam");
        var pageUrl = PageKit.Origin + "/p/paperplane/contact?subject=Printer%20jam&name=Ada";
        PageKit.IsJumpWithin(dom, pageUrl, href).ShouldBeTrue("on the address the visitor sees once the extra parameters are gone, the link is a jump");
    }

    [Fact]
    public async Task A_kept_help_centre_page_never_carries_a_visitors_other_query_parameters_in_its_markup()
    {
        // The output cache keeps one copy for every visitor of /p/{key}/kb/{category}; a campaign tag or any other parameter of the first request must never be in it.
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoryArticlesPath, KbTestKit.Page(1, 10, 1, KbTestKit.Article()));

        var (html, dom) = await GetAsync(factory, "/p/paperplane/kb/accounts?utm_source=newsletter-4711");

        html.ShouldNotContain("newsletter-4711");
        dom.QuerySelector("a.ts-skip-link")!.GetAttribute("href").ShouldBe("/p/paperplane/kb/accounts#main");
    }

    [Fact]
    public async Task The_ticket_page_has_a_jump_to_the_reply_form_that_works_under_the_base_and_lands_on_a_heading_that_can_take_focus()
    {
        await using var factory = TicketTestKit.Factory();
        var path = TicketTestKit.Path;

        var (_, dom) = await GetAsync(factory, path);

        var jump = dom.QuerySelector("a.ts-jump-reply").ShouldNotBeNull();
        jump.TextContent.ShouldBe(TechStrap.Portal.Tickets.TicketCopy.JumpToReply);
        jump.GetAttribute("data-enhance-nav").ShouldBe("false");
        PageKit.IsJumpWithin(dom, PageKit.Origin + path, jump.GetAttribute("href")!).ShouldBeTrue();
        var heading = dom.GetElementById("reply").ShouldNotBeNull();
        heading.TagName.ShouldBe("H2");
        heading.GetAttribute("tabindex").ShouldBe("-1");
        // The jump comes before the conversation it skips over.
        Regex.Match(dom.Body!.InnerHtml, "ts-jump-reply").Index.ShouldBeLessThan(Regex.Match(dom.Body.InnerHtml, "ts-thread").Index);
    }

    [Fact]
    public async Task The_error_summary_still_takes_focus_when_the_page_opens_and_is_announced_once()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);

        using var response = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(token, email: ""), Ct);
        var dom = PageKit.Parse(await response.Content.ReadAsStringAsync(Ct));

        var summary = dom.QuerySelector(".ts-error-summary").ShouldNotBeNull();
        summary.HasAttribute("autofocus").ShouldBeTrue();
        summary.GetAttribute("tabindex").ShouldBe("-1");
        summary.GetAttribute("role").ShouldBe("alert");
        summary.GetAttribute("aria-labelledby").ShouldBe("error-summary-heading");
        dom.GetElementById("error-summary-heading")!.TagName.ShouldBe("H2");
        dom.QuerySelectorAll("[role=alert]").Count(e => e.ClassList.Contains("ts-error-summary")).ShouldBe(1);
    }

    [Fact]
    public async Task Every_form_says_in_words_what_is_required()
    {
        await using var factory = TicketTestKit.Factory();

        var contact = (await GetAsync(factory, "/p/paperplane/contact")).Dom;
        var lostLink = (await GetAsync(factory, "/p/paperplane/lost-link")).Dom;
        var reply = (await GetAsync(factory, TicketTestKit.Path)).Dom;

        contact.QuerySelector(".ts-form-note")!.TextContent.ShouldBe(TechStrap.Portal.Forms.ContactCopy.RequiredNote);
        lostLink.QuerySelector(".ts-form-note")!.TextContent.ShouldBe(TechStrap.Portal.Forms.LostLinkCopy.RequiredNote);
        reply.QuerySelector(".ts-form-note")!.TextContent.ShouldBe(TechStrap.Portal.Tickets.TicketCopy.ReplyNote);
        foreach (var dom in new[] { contact, lostLink, reply })
        {
            dom.QuerySelector(".ts-form-note")!.NextElementSibling!.TagName.ShouldBe("FORM", "the note sits right above the form it describes");
        }
    }
}
```

`tests/TechStrap.Portal.Tests/Components/PageKit.cs` (new)

```csharp
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace TechStrap.Portal.Tests.Components;

/// <summary>What the accessibility host tests share: a parsed page and the way a browser resolves a link in it.</summary>
internal static class PageKit
{
    public const string Origin = "http://localhost";

    public static IDocument Parse(string html) => new HtmlParser().ParseDocument(html);

    /// <summary>
    /// Where a browser goes for <paramref name="href"/> on the page at <paramref name="pageUrl"/>: the link is resolved against the document's base URL, which is the page's <c>base</c> element resolved against the
    /// page's own address. (A bare <c>#email</c> under <c>base href="/"</c> comes out as <c>/#email</c>: the home page. That is the bug the Portal's links are written to avoid.)
    /// </summary>
    public static Uri Resolve(IDocument dom, string pageUrl, string href)
    {
        var page = new Uri(pageUrl, UriKind.Absolute);
        var baseHref = dom.QuerySelector("base")?.GetAttribute("href") ?? string.Empty;
        return new Uri(new Uri(page, baseHref), href);
    }

    /// <summary>True when following <paramref name="href"/> stays inside the document at <paramref name="pageUrl"/> (the same address, only the fragment differs), so the browser jumps and does not load a page.</summary>
    public static bool IsJumpWithin(IDocument dom, string pageUrl, string href)
    {
        var target = Resolve(dom, pageUrl, href);
        var page = new Uri(pageUrl, UriKind.Absolute);
        return target.GetLeftPart(UriPartial.Query) == page.GetLeftPart(UriPartial.Query) && target.Fragment.Length > 1;
    }

    /// <summary>The first element, in document order, a keyboard reaches with Tab: a link with an address, a button, a field that is not hidden or disabled and not taken out of the order.</summary>
    public static IElement? FirstFocusable(IDocument dom) =>
        dom.QuerySelectorAll("a[href], button:not([disabled]), input:not([type=hidden]):not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex^='-'])").FirstOrDefault();
}
```

`tests/TechStrap.Portal.Tests/CspStyleTests.cs` (new)

```csharp
using System.Text.RegularExpressions;
using TechStrap.Portal.Tests.Components;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests;

/// <summary>
/// The Portal's side of Review Focus 3 (the Admin's <c>CspStyleTests</c> already runs its compiled-CSS checks over the Portal too): the policy is unchanged (<c>script-src 'self'</c>, <c>style-src 'self'</c>), so the new
/// styles add no <c>url()</c> the policy does not allow, no <c>@import</c>, and no markup the policy would block: no <c>style</c> element, no <c>style</c> attribute but the product accent's own, no inline script and no
/// event-handler attribute, on the pages that got new markup in 09d.
/// </summary>
public sealed partial class CspStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Portal");

    [GeneratedRegex(@"url\(\s*(?<q>[""']?)(?<target>.*?)\k<q>\s*\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UrlReference();

    [Fact]
    public void The_compiled_portal_css_references_only_same_origin_fonts_and_data_images_and_imports_nothing()
    {
        var targets = UrlReference().Matches(Css.Text).Select(m => m.Groups["target"].Value).ToList();

        targets.ShouldNotBeEmpty("the fonts and Bootstrap's icons are url() references");
        targets.ShouldAllBe(t => t.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || t.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase));
        Css.Text.ShouldNotContain("@import");
        Css.Text.ShouldNotContain("javascript:");
        Css.Text.ShouldNotContain("expression(");
        Css.Text.ShouldNotContain("-moz-binding");
    }

    [Fact]
    public void The_portals_own_stylesheets_add_no_url_at_all_except_the_font_faces()
    {
        foreach (var file in Directory.EnumerateFiles(RepositoryRoot.Combine("src", "TechStrap.Portal", "Styles"), "*.scss", SearchOption.TopDirectoryOnly).Where(f => !f.EndsWith("_fonts.scss", StringComparison.Ordinal)))
        {
            File.ReadAllText(file).ShouldNotContain("url(", customMessage: file);
        }
    }

    public static TheoryData<string> Pages => ["/", "/p/paperplane", "/p/paperplane/contact", "/p/paperplane/contact/received", "/p/paperplane/lost-link", "/t/AbC-_0123456789AbC-_0123456789AbC-_01234567"];

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task A_page_has_no_style_element_no_inline_script_no_event_handler_and_one_style_attribute_the_accent_scope(string path)
    {
        await using var factory = Tickets.TicketTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        var html = await client.GetStringAsync(path, TestContext.Current.CancellationToken);

        var dom = PageKit.Parse(html);
        dom.QuerySelectorAll("style").ShouldBeEmpty();
        dom.QuerySelectorAll("script:not([src])").ShouldBeEmpty();
        dom.QuerySelectorAll("[style]").ShouldAllBe(e => e.ClassList.Contains("ts-accent-scope"));
        dom.All.SelectMany(e => e.Attributes).Where(a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Portal.Tests/Forms/ErrorSummaryLinkHostTests.cs` (new)

```csharp
using System.Net;
using TechStrap.Portal.Tests.Components;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// The error summary's links at the host (Review Focus 4). The document's <c>base</c> is <c>/</c>, so a bare <c>#email</c> is the home page: the Admin hit the same bug. Here every link of every form's summary is
/// resolved the way a browser resolves it, and has to stay on the page the post was answered at (a jump, not a page load) and land on an element that exists; and the summary opts out of Blazor's enhanced navigation,
/// which scrolls to a fragment but leaves the focus where it was (proved in a browser in the 09d spike).
/// </summary>
public sealed class ErrorSummaryLinkHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static void AssertLinksJumpToTheirFields(string pageUrl, string html, params string[] expectedFields)
    {
        var dom = PageKit.Parse(html);
        var summary = dom.QuerySelector(".ts-error-summary").ShouldNotBeNull();
        summary.GetAttribute("data-enhance-nav").ShouldBe("false");
        var links = summary.QuerySelectorAll("a").ToList();
        links.Select(a => PageKit.Resolve(dom, pageUrl, a.GetAttribute("href")!).Fragment.TrimStart('#')).ShouldBe(expectedFields);
        foreach (var link in links)
        {
            var href = link.GetAttribute("href")!;
            PageKit.IsJumpWithin(dom, pageUrl, href).ShouldBeTrue($"{href} must stay on {pageUrl}");
            href.ShouldStartWith("/", Case.Sensitive, "never a bare fragment");
            var target = dom.GetElementById(PageKit.Resolve(dom, pageUrl, href).Fragment.TrimStart('#'));
            target.ShouldNotBeNull($"{href} must land on an element of the page");
        }
    }

    [Fact]
    public async Task The_contact_forms_links_jump_to_its_four_fields()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);

        using var response = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(token, email: "", name: "", subject: "", body: ""), Ct);

        AssertLinksJumpToTheirFields(PageKit.Origin + FormTestKit.Path, await response.Content.ReadAsStringAsync(Ct), "name", "email", "subject", "body");
    }

    [Fact]
    public async Task The_contact_forms_attachment_link_jumps_to_the_file_input()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);
        var files = Enumerable.Range(0, 6).Select(i => new PostedFile($"f{i}.txt", [1, 2, 3])).ToArray();

        using var response = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(token, files: files), Ct);

        AssertLinksJumpToTheirFields(PageKit.Origin + FormTestKit.Path, await response.Content.ReadAsStringAsync(Ct), "attachments");
    }

    [Fact]
    public async Task The_lost_link_forms_link_jumps_to_its_field()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);
        var html = await client.GetStringAsync("/p/paperplane/lost-link", Ct);
        var form = new MultipartFormDataContent { { new StringContent("lost-link"), "_handler" }, { new StringContent(FormTestKit.TokenFrom(html)), "__RequestVerificationToken" }, { new StringContent("not an address"), "Form.Email" } };

        using var response = await client.PostAsync("/p/paperplane/lost-link", form, Ct);

        AssertLinksJumpToTheirFields(PageKit.Origin + "/p/paperplane/lost-link", await response.Content.ReadAsStringAsync(Ct), "email");
    }

    [Fact]
    public async Task The_reply_forms_link_jumps_to_its_field_on_the_ticket_page_whose_address_holds_the_token()
    {
        await using var factory = TicketTestKit.Factory();
        using var client = TicketTestKit.Client(factory);
        var token = await FormTestKit.TokenAsync(client, TicketTestKit.Path, Ct);

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, body: ""), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertLinksJumpToTheirFields(PageKit.Origin + TicketTestKit.Path, await response.Content.ReadAsStringAsync(Ct), "body");
    }

    [Fact]
    public async Task A_summary_link_that_was_a_bare_fragment_would_have_left_the_page()
    {
        // The control for the tests above: the same resolution says a bare #email is the home page, so the tests would have caught the old markup.
        var dom = PageKit.Parse("<html><head><base href=\"/\"></head><body><a href=\"#email\">x</a></body></html>");

        PageKit.IsJumpWithin(dom, PageKit.Origin + FormTestKit.Path, "#email").ShouldBeFalse();
        PageKit.Resolve(dom, PageKit.Origin + FormTestKit.Path, "#email").AbsolutePath.ShouldBe("/");
    }
}
```

`tests/TechStrap.Portal.Tests/HeadingHostTests.cs` (new)

```csharp
using System.Net;
using AngleSharp.Dom;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Tests.Components;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Kb;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests;

/// <summary>
/// Every Portal page has exactly one <c>h1</c> (PHASE-09 T16, UX brief, Review Focus 4), and the help-center search page's is not the home's. The Admin has the same test for its two odd pages; the Portal's check
/// covers every kind of page, including the states (an API failure, an empty result, a ticket that cannot be loaded) and the pages whose body is HTML an author wrote.
/// </summary>
public sealed class HeadingHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<IDocument> GetAsync(PortalFactory factory, string path, HttpStatusCode? expected = null)
    {
        using var client = FormTestKit.Client(factory);
        using var response = await client.GetAsync(path, Ct);
        if (expected is { } status)
        {
            response.StatusCode.ShouldBe(status);
        }

        return PageKit.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    private static string[] H1(IDocument dom) => [.. dom.QuerySelectorAll("h1").Select(h => h.TextContent.Trim())];

    private static PortalFactory KbHost()
    {
        var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, new[] { new PublicKbCategoryDto("accounts", "Accounts", "About accounts", 2) });
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoryArticlesPath, KbTestKit.Page(1, 10, 1, KbTestKit.Article()));
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.SearchPath, KbTestKit.Hits(1, 10, 1, KbTestKit.Hit()));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/paperplane/articles/accounts/reset-password",
            new PublishedKbArticleDto("paperplane", "accounts", "Accounts", "reset-password", "Reset your password", "How", "<h1>A title in the body</h1><h2>Steps</h2><p>Go.</p>", KbTestKit.Updated, KbTestKit.Updated));
        return factory;
    }

    public static TheoryData<string, string> Pages => new()
    {
        { "/", "Support" },
        { "/p/paperplane", "How can we help?" },
        { "/p/paperplane/contact", "Contact support" },
        { "/p/paperplane/contact/received", "We have received your request." },
        { "/p/paperplane/lost-link", "Lost your ticket link?" },
        { "/p/paperplane/lost-link?sent=1", "Lost your ticket link?" },
        { "/p/paperplane/kb", "Help center" },
        { "/p/paperplane/kb/accounts", "Accounts" },
        { "/p/paperplane/kb/search", "Search the help center" },
        { "/p/paperplane/kb/search?q=reset", "Search the help center" },
        { "/p/paperplane/kb/accounts/reset-password", "Reset your password" },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task A_page_has_exactly_one_h1_and_it_is_the_pages_own_title(string path, string heading)
    {
        await using var factory = KbHost();

        var dom = await GetAsync(factory, path, HttpStatusCode.OK);

        H1(dom).ShouldBe([heading]);
    }

    [Fact]
    public async Task The_search_pages_h1_is_not_the_help_centre_homes()
    {
        await using var factory = KbHost();

        var home = H1(await GetAsync(factory, "/p/paperplane/kb")).Single();
        var search = H1(await GetAsync(factory, "/p/paperplane/kb/search?q=reset")).Single();
        var empty = H1(await GetAsync(factory, "/p/paperplane/kb/search")).Single();

        search.ShouldNotBe(home);
        empty.ShouldBe(search);
    }

    [Fact]
    public async Task An_empty_search_result_and_an_empty_help_centre_keep_one_h1_and_make_the_states_h2()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.SearchPath, KbTestKit.Hits(1, 10, 0));
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Array.Empty<PublicKbCategoryDto>());

        var none = await GetAsync(factory, "/p/paperplane/kb/search?q=zzz");
        var empty = await GetAsync(factory, "/p/paperplane/kb");

        foreach (var dom in new[] { none, empty })
        {
            dom.QuerySelectorAll("h1").Length.ShouldBe(1);
            dom.QuerySelectorAll(".ts-state h2").Length.ShouldBe(1);
            dom.QuerySelectorAll(".ts-state h1").ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task An_h1_in_an_article_body_is_shown_as_an_h2_so_the_page_keeps_one()
    {
        await using var factory = KbHost();

        var dom = await GetAsync(factory, "/p/paperplane/kb/accounts/reset-password", HttpStatusCode.OK);

        H1(dom).ShouldBe(["Reset your password"]);
        dom.QuerySelectorAll(".ts-kb-article-body h2").Select(h => h.TextContent).ShouldBe(["A title in the body", "Steps"]);
        dom.QuerySelectorAll("h2").Select(h => h.TextContent).ShouldContain("Still need help?");
    }

    [Theory]
    [InlineData("Open", "PAP-42 Printer jam")]
    [InlineData("Closed", "PAP-42 Printer jam")]
    public async Task The_ticket_page_has_one_h1_even_when_a_message_has_its_own(string status, string heading)
    {
        await using var factory = TicketTestKit.Factory(TicketTestKit.Ticket(status, agentBody: "<h1>Agent heading</h1><p>Hello.</p>"));

        var dom = await GetAsync(factory, TicketTestKit.Path, HttpStatusCode.OK);

        H1(dom).Single().ShouldContain("PAP-42");
        dom.QuerySelectorAll(".ts-message-body h1").ShouldBeEmpty();
        dom.QuerySelectorAll(".ts-message-body h2").Select(h => h.TextContent).ShouldContain("Agent heading");
        dom.QuerySelectorAll("h2#reply").Count.ShouldBe(1);
        heading.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task The_unavailable_states_have_exactly_one_h1_and_it_is_the_state_s_own(HttpStatusCode api)
    {
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/paperplane", api, "x", "no");

        var product = await GetAsync(factory, "/p/paperplane");
        await using var ticketFactory = FormTestKit.Factory();
        ticketFactory.Api.OnProblem(HttpMethod.Get, TicketTestKit.TicketApi, api, "x", "no");
        var ticket = await GetAsync(ticketFactory, TicketTestKit.Path);

        foreach (var dom in new[] { product, ticket })
        {
            H1(dom).ShouldBe(["This page could not be loaded."]);
            dom.QuerySelectorAll(".ts-state h1").Length.ShouldBe(1);
        }
    }

    [Theory]
    [InlineData("/nope")]
    [InlineData("/p/nobody/contact")]
    [InlineData("/t/short")]
    public async Task The_not_found_page_has_exactly_one_h1(string path)
    {
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/nobody", HttpStatusCode.NotFound, "product-not-found", "No such product.");

        var dom = await GetAsync(factory, path, HttpStatusCode.NotFound);

        H1(dom).ShouldBe(["Page not found"]);
    }

    [Fact]
    public async Task The_error_and_style_guide_pages_have_exactly_one_h1()
    {
        await using var factory = FormTestKit.Factory(product: false);

        H1(await GetAsync(factory, "/error")).Length.ShouldBe(1);
        H1(await GetAsync(factory, "/_styleguide", HttpStatusCode.OK)).Length.ShouldBe(1);
    }

    [Fact]
    public async Task A_form_shown_again_after_an_error_has_one_h1_and_the_summary_is_an_h2()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);

        using var response = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(token, email: ""), Ct);
        var dom = PageKit.Parse(await response.Content.ReadAsStringAsync(Ct));

        H1(dom).ShouldBe(["Contact support"]);
        dom.QuerySelectorAll(".ts-error-summary h2").Length.ShouldBe(1);
    }
}
```

`tests/TechStrap.Portal.Tests/ResponsiveStyleTests.cs` (new)

```csharp
using System.Text.RegularExpressions;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests;

/// <summary>
/// The Portal's layout and accessibility rules (PHASE-09 T16, UX-BRIEF-portal, BRAND.md sections 14, 17 and 24), read from the compiled CSS so a rule that is renamed, moved to the wrong breakpoint or deleted fails
/// here: one token for the reading width and one for the wide container, one column on a phone and cards from a tablet up, a 44px target for every link a visitor has to hit, a skip link that appears on focus,
/// forced colors that keep the 3px focus outline, and no motion at all for a visitor who asked for none. Every class the markup uses has a rule. How it looks is the owner's checklist in PORTAL-APP.md.
/// </summary>
public sealed partial class ResponsiveStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Portal");
    private const string Root = ":root,[data-bs-theme=light]";
    private const string Phone = "(max-width: 479.98px)";

    [GeneratedRegex(@"(?<selectors>[^{}@]+)\{(?<body>[^{}]*)\}")]
    private static partial Regex Rule();

    /// <summary>The declarations of every rule (outside or inside a media block) whose selector LIST contains <paramref name="selector"/>, merged, last wins.</summary>
    private static IReadOnlyDictionary<string, string> Containing(CompiledCss css, string selector)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match rule in Rule().Matches(css.Text))
        {
            if (!rule.Groups["selectors"].Value.Split(',').Select(s => s.Trim()).Contains(selector))
            {
                continue;
            }

            foreach (var declaration in rule.Groups["body"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = declaration.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0)
                {
                    merged[declaration[..colon].Trim()] = declaration[(colon + 1)..].Trim();
                }
            }
        }

        return merged;
    }

    // ---- widths ----

    [Fact]
    public void The_reading_column_and_the_wide_container_are_one_token_each()
    {
        var root = Css.Declarations(Root);

        root["--ts-reading-width"].ShouldBe("40rem");
        root["--ts-wide-width"].ShouldBe("64rem");
        Css.OutsideMedia().Declarations(".ts-reading")["max-width"].ShouldBe("var(--ts-reading-width)");
        foreach (var wide in new[] { ".ts-portal-main", ".ts-product-header", ".ts-product-footer" })
        {
            Containing(Css, wide)["max-width"].ShouldBe("var(--ts-wide-width)", wide);
        }

        Css.Declarations(".shell-placeholder")["max-width"].ShouldBe("var(--ts-reading-width)");
    }

    [Fact]
    public void No_rule_of_the_portal_writes_the_old_640px_width_any_more()
    {
        Css.Text.ShouldNotContain("max-width:640px");
        foreach (var file in Directory.EnumerateFiles(RepositoryRoot.Combine("src", "TechStrap.Portal", "Styles"), "*.scss", SearchOption.TopDirectoryOnly))
        {
            File.ReadAllText(file).ShouldNotContain("640px", customMessage: file);
        }
    }

    // ---- breakpoints ----

    [Fact]
    public void The_help_centre_home_is_one_column_on_a_phone_two_cards_across_from_768px_and_three_from_1200px()
    {
        Css.OutsideMedia().Declarations(".ts-kb-list--cards").ContainsKey("display").ShouldBeFalse("below 768px the list is the plain single column");
        var tablet = Css.InMedia("(min-width: 768px)").Declarations(".ts-kb-list--cards");
        tablet["display"].ShouldBe("grid");
        tablet["grid-template-columns"].ShouldBe("repeat(2, minmax(0, 1fr))");
        Css.InMedia("(min-width: 1200px)").Declarations(".ts-kb-list--cards")["grid-template-columns"].ShouldBe("repeat(3, minmax(0, 1fr))");
    }

    [Fact]
    public void On_a_phone_the_search_box_stacks_and_the_primary_action_of_a_form_fills_the_row()
    {
        var phone = Css.InMedia(Phone);

        phone.Declarations(".ts-help-search-row")["flex-direction"].ShouldBe("column");
        phone.Declarations(".ts-help-search-row .btn")["width"].ShouldBe("100%");
        phone.Declarations(".ts-form .btn,.ts-help-contact .btn")["width"].ShouldBe("100%");
        Css.OutsideMedia().Declarations(".ts-help-search-row")["display"].ShouldBe("flex");
        Css.OutsideMedia().Declarations(".ts-help-search-row .form-control")["min-width"].ShouldBe("0", "the box may shrink; it must never push the page wider than the screen");
    }

    [Fact]
    public void Nothing_a_visitor_reads_can_widen_the_page_a_long_word_wraps_and_a_table_or_code_block_scrolls_in_its_own_box()
    {
        Css.Declarations(".ts-kb-article-body")["overflow-wrap"].ShouldBe("anywhere");
        Css.Declarations(".ts-message-body")["overflow-wrap"].ShouldBe("anywhere");
        Css.Declarations(".ts-kb-article-body pre,.ts-kb-article-body table")["overflow-x"].ShouldBe("auto");
        Css.Declarations(".ts-kb-article-body img")["max-width"].ShouldBe("100%");
        Css.Declarations(".ts-portal-main").ContainsKey("overflow-x").ShouldBeFalse("clipping the page would hide a problem instead of fixing it");
    }

    [Fact]
    public void The_page_fills_the_dynamic_viewport_so_a_phones_toolbar_does_not_leave_a_gap()
    {
        var portal = Css.Declarations(".ts-portal");

        portal["min-height"].ShouldBe("100dvh");
    }

    // ---- target size, 44px ----

    [Theory]
    [InlineData(".ts-site-nav-links a")]
    [InlineData(".ts-breadcrumbs a")]
    [InlineData(".ts-pager a")]
    [InlineData(".ts-next-links a")]
    [InlineData(".ts-product-footer a")]
    [InlineData(".ts-powered a")]
    [InlineData(".ts-error-summary a")]
    [InlineData(".ts-help-link a")]
    [InlineData(".ts-jump-reply")]
    [InlineData(".ts-attachments a")]
    [InlineData(".ts-kb-card h2 a")]
    [InlineData(".ts-product-name")]
    [InlineData(".btn")]
    [InlineData(".form-control")]
    [InlineData(".ts-skip-link")]
    public void A_link_or_control_a_visitor_has_to_hit_is_at_least_44px_tall(string selector)
    {
        Containing(Css, selector)["min-height"].ShouldBe("44px", selector);
    }

    // ---- skip link ----

    [Fact]
    public void The_skip_link_is_off_the_screen_until_it_has_focus_and_does_not_use_the_product_accent()
    {
        var link = Css.OutsideMedia().Declarations(".ts-skip-link");

        link["position"].ShouldBe("absolute");
        link["top"].ShouldBe("-100px");
        link["color"].ShouldBe("var(--p-ink)");
        link["background"].ShouldBe("var(--p-bg)");
        link["border"].ShouldBe("2px solid var(--p-ink)");
        Css.Declarations(".ts-skip-link:focus,.ts-skip-link:focus-visible")["top"].ShouldBe("8px");
        Css.Declarations(".ts-portal-main:focus")["outline"].ShouldBe("none", "main takes focus for the skip link but shows no ring around the whole page");
    }

    // ---- states are never color alone ----

    [Fact]
    public void Errors_use_the_error_tokens_and_a_field_in_error_gets_a_heavier_border_and_text()
    {
        Css.Declarations(".alert-danger.ts-error-summary")["--bs-alert-border-color"].ShouldBe("var(--p-error)");
        Css.Declarations(".alert-danger.ts-error-summary")["--bs-alert-bg"].ShouldBe("var(--p-error-bg)");
        Css.Declarations(".ts-field-error")["color"].ShouldBe("var(--p-error)");
        Css.Declarations(".form-control[aria-invalid=true]")["border-width"].ShouldBe("2px");
        Css.Declarations(".alert-warning")["--bs-alert-border-color"].ShouldBe("var(--p-warn)");
        Css.Declarations(".ts-confirmation")["border"].ShouldBe("2px solid var(--p-success)");
        Css.Text.ShouldNotContain("--p-error,", customMessage: "the old undefined-variable fallback is gone");
        Css.Declarations(".ts-count-over")["text-decoration"].ShouldBe("underline");
    }

    [Fact]
    public void A_form_control_has_an_edge_that_is_not_the_faint_decorative_line()
    {
        Css.Declarations(".form-control,.form-select")["border-color"].ShouldBe("var(--p-ink2)");
    }

    [Fact]
    public void The_counter_is_empty_and_hidden_until_the_script_shows_it()
    {
        Css.Declarations(".ts-char-count[hidden]")["display"].ShouldBe("none");
    }

    // ---- forced colors ----

    [Fact]
    public void Forced_colours_keep_the_3px_focus_outline_and_drop_the_halo()
    {
        var forced = Css.InMedia("(forced-colors: active)");

        var focus = forced.Declarations(":focus-visible,.btn:focus-visible,.form-control:focus,.form-select:focus,.form-check-input:focus");
        focus["outline"].ShouldBe("3px solid Highlight !important");
        focus["box-shadow"].ShouldBe("none !important");
    }

    [Fact]
    public void Forced_colours_keep_what_colour_alone_would_say_with_a_border_a_marker_or_an_underline()
    {
        var forced = Css.InMedia("(forced-colors: active)");

        forced.Declarations(".ts-skip-link")["border"].ShouldBe("2px solid CanvasText");
        forced.Declarations(".ts-error-summary,.ts-confirmation,.alert-warning")["border"].ShouldBe("2px solid CanvasText");
        forced.Declarations(".ts-status-banner,.ts-message-own")["border-left"].ShouldBe("6px solid Highlight");
        forced.Declarations(".form-control[aria-invalid=true]")["border"].ShouldBe("3px solid CanvasText");
        forced.Declarations(".ts-count-over")["border-bottom"].ShouldBe("2px solid CanvasText");
        forced.Declarations(".btn:disabled")["color"].ShouldBe("GrayText");
    }

    // ---- motion ----

    [Fact]
    public void Reduced_motion_switches_every_animation_and_transition_off_and_nothing_scrolls_smoothly()
    {
        var everything = Css.InMedia("(prefers-reduced-motion: reduce)").Declarations("*,*::before,*::after");
        everything["animation"].ShouldBe("none !important");
        everything["transition"].ShouldBe("none !important");

        Css.Text.ShouldNotContain("scroll-behavior", customMessage: "Bootstrap's smooth scrolling is switched off in the build ($enable-smooth-scroll)");
        Directory.EnumerateFiles(RepositoryRoot.Combine("src", "TechStrap.Portal", "Styles"), "*.scss", SearchOption.TopDirectoryOnly)
            .Where(file => File.ReadAllText(file).Contains("scroll-behavior", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    // ---- every class has a rule ----

    [GeneratedRegex(@"class=""(?<value>[^""]*)""|className\s*=\s*'(?<js>[^']*)'|ClassList\.Add\(""(?<list>[^""]*)""\)", RegexOptions.CultureInvariant)]
    private static partial Regex ClassAttribute();

    [GeneratedRegex(@"\bts-[a-z0-9]+(?:-[a-z0-9]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex TsClass();

    // A class of the markup that is deliberately not styled: the accent scope is styled through its custom properties, and the two style-guide wrappers (Development only) only group the samples.
    private static readonly string[] Markers = ["ts-accent-scope", "ts-sg", "ts-sg-form"];

    public static TheoryData<string> Sources()
    {
        var data = new TheoryData<string>();
        var portal = RepositoryRoot.Combine("src", "TechStrap.Portal");
        foreach (var file in Directory.EnumerateFiles(portal, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".js", StringComparison.Ordinal))
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            data.Add(Path.GetRelativePath(portal, file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void Every_ts_class_the_markup_of_a_file_uses_has_a_rule_in_the_compiled_css(string relativePath)
    {
        var source = File.ReadAllText(RepositoryRoot.Combine("src", "TechStrap.Portal", relativePath));
        var used = ClassAttribute().Matches(source)
            .SelectMany(m => new[] { m.Groups["value"].Value, m.Groups["js"].Value, m.Groups["list"].Value })
            .SelectMany(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .SelectMany(token => TsClass().Matches(token).Select(t => t.Value))
            .Where(c => !Markers.Contains(c))
            .Distinct();

        foreach (var name in used)
        {
            Regex.IsMatch(Css.Text, @"\." + Regex.Escape(name) + @"(?![\w-])").ShouldBeTrue($".{name} is used in {relativePath} and has no rule");
        }
    }

    [Fact]
    public void The_class_scan_finds_the_classes_it_is_meant_to_find()
    {
        var found = ClassAttribute().Matches("<div class=\"ts-a ts-b-c btn\"></div> x.className = 'ts-js-one ts-js-two';")
            .SelectMany(m => new[] { m.Groups["value"].Value, m.Groups["js"].Value })
            .SelectMany(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(c => c.StartsWith("ts-", StringComparison.Ordinal))
            .ToList();

        found.ShouldBe(["ts-a", "ts-b-c", "ts-js-one", "ts-js-two"]);
    }
}
```

`tests/TechStrap.Portal.Tests/Routing/PageLinksTests.cs` (new)

```csharp
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tests.Routing;

/// <summary>
/// A link to a place on the current page (the error summary, the skip link, "jump to your reply") is a root-relative address plus a fragment, never a bare <c>#fragment</c>: under the document's <c>base href="/"</c>
/// a bare fragment is the home page (D-045 09d addendum). The query string is kept only for the parameters the page itself reads.
/// </summary>
public sealed class PageLinksTests
{
    private const string Host = "http://localhost";

    [Theory]
    [InlineData("/p/paperplane/contact", "email", "/p/paperplane/contact#email")]
    [InlineData("/p/paperplane/lost-link", "email", "/p/paperplane/lost-link#email")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567", "body", "/t/AbC-_0123456789AbC-_0123456789AbC-_01234567#body")]
    [InlineData("/", "main", "/#main")]
    public void The_link_is_the_current_path_plus_the_fragment(string path, string fragment, string expected)
    {
        PageLinks.ToFragment(Host + path, fragment, keepQuery: false).ShouldBe(expected);
        PageLinks.ToFragment(Host + path, fragment, keepQuery: true).ShouldBe(expected);
    }

    [Fact]
    public void The_host_the_port_a_fragment_already_in_the_address_and_the_query_never_leak_into_it_without_keepQuery()
    {
        PageLinks.ToFragment("https://support.example.com:8443/p/paperplane/contact?subject=Jam&token=x#old", "main", keepQuery: false).ShouldBe("/p/paperplane/contact#main");
    }

    [Theory]
    [InlineData("/p/paperplane/contact?subject=Printer%20jam&name=Ada&email=ada%40example.com", "/p/paperplane/contact?subject=Printer%20jam&name=Ada&email=ada%40example.com#main")]
    [InlineData("/p/paperplane/contact?subject=Jam&utm_source=mail&Website=spam&ref=1", "/p/paperplane/contact?subject=Jam#main")]
    [InlineData("/p/paperplane/contact?utm=1", "/p/paperplane/contact#main")]
    [InlineData("/p/paperplane/contact/received?ref=CfDJ8abc", "/p/paperplane/contact/received?ref=CfDJ8abc#main")]
    [InlineData("/p/paperplane/contact/received?ref=CfDJ8abc&subject=x", "/p/paperplane/contact/received?ref=CfDJ8abc#main")]
    [InlineData("/p/paperplane/lost-link?sent=1&x=2", "/p/paperplane/lost-link?sent=1#main")]
    [InlineData("/p/paperplane/kb/search?q=reset%20password&page=2&utm=3", "/p/paperplane/kb/search?q=reset%20password&page=2#main")]
    [InlineData("/p/paperplane/kb/accounts?page=2&utm=3", "/p/paperplane/kb/accounts?page=2#main")]
    [InlineData("/p/paperplane/kb/accounts/reset-password?page=2&utm=3", "/p/paperplane/kb/accounts/reset-password#main")]
    [InlineData("/p/paperplane/kb?page=2&utm=3", "/p/paperplane/kb#main")]
    [InlineData("/p/paperplane?utm=3", "/p/paperplane#main")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567?x=1", "/t/AbC-_0123456789AbC-_0123456789AbC-_01234567#main")]
    public void With_keepQuery_only_the_parameters_the_page_reads_stay_in_the_order_they_came(string current, string expected)
    {
        PageLinks.ToFragment(Host + current, "main", keepQuery: true).ShouldBe(expected);
    }

    [Fact]
    public void A_kept_value_is_escaped_so_it_cannot_add_a_parameter_a_fragment_or_a_tag()
    {
        var link = PageLinks.ToFragment(Host + "/p/paperplane/kb/search?q=a%26b%3Dc%23d%22%3E%3Cscript%3E", "main", keepQuery: true);

        link.ShouldBe("/p/paperplane/kb/search?q=a%26b%3Dc%23d%22%3E%3Cscript%3E#main");
        link.Count(c => c == '#').ShouldBe(1);
        link.ShouldNotContain("<");
        link.ShouldNotContain("\"");
    }

    [Fact]
    public void A_path_with_a_capital_or_an_escape_is_still_the_pages_own_address()
    {
        PageLinks.ToFragment(Host + "/p/PaperPlane/Contact?subject=x", "main", keepQuery: true).ShouldBe("/p/PaperPlane/Contact?subject=x#main");
        PageLinks.ToFragment(Host + "/p/paperplane/kb/a%20b", "main", keepQuery: true).ShouldBe("/p/paperplane/kb/a%20b#main");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void A_blank_fragment_is_refused(string fragment)
    {
        Should.Throw<ArgumentException>(() => PageLinks.ToFragment(Host + "/", fragment, keepQuery: false));
    }
}
```

`tests/TechStrap.Portal.Tests/TokenContrastTests.cs` (new)

```csharp
using TechStrap.Contracts.Branding;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests;

/// <summary>
/// BRAND.md section 24: the Portal meets WCAG 2.2 AA. Every text and background pair of the Portal's own tokens (the neutral ones and the error, success and warning ones of 09d) is checked against the compiled CSS, so a
/// token edit that breaks a pair fails the build; the edge of a form control is held to the 3:1 of a non-text component (1.4.11); and the colors that carry a state sit on the grounds they are used on. (The product's
/// accent is checked per product, including hostile ones, in <c>ProductAccentContrastTests</c>.)
/// </summary>
public sealed class TokenContrastTests
{
    private const double Text = 4.5;
    private const double NonText = 3.0;

    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Portal");

    private static IReadOnlyDictionary<string, string> Tokens => Css.Declarations(":root,[data-bs-theme=light]");

    public static TheoryData<string, string> TextPairs =>
    [
        ("p-ink", "p-bg"), ("p-ink", "p-soft"), ("p-ink2", "p-bg"), ("p-ink2", "p-soft"),
        ("p-error", "p-bg"), ("p-error", "p-error-bg"), ("p-ink", "p-error-bg"),
        ("p-success", "p-bg"), ("p-success", "p-success-bg"), ("p-ink", "p-success-bg"),
        ("p-warn", "p-bg"), ("p-warn", "p-warn-bg"), ("p-ink", "p-warn-bg"),
    ];

    [Theory]
    [MemberData(nameof(TextPairs))]
    public void Text_pair_meets_AA(string foreground, string background)
    {
        ProductAccent.ContrastRatio(Tokens[$"--{foreground}"], Tokens[$"--{background}"]).ShouldBeGreaterThanOrEqualTo(Text, $"--{foreground} on --{background}");
    }

    [Fact]
    public void The_edge_of_a_form_control_is_the_secondary_ink_and_has_3_to_1_against_the_page()
    {
        Css.Declarations(".form-control,.form-select")["border-color"].ShouldBe("var(--p-ink2)");

        ProductAccent.ContrastRatio(Tokens["--p-ink2"], Tokens["--p-bg"]).ShouldBeGreaterThanOrEqualTo(NonText);
    }

    [Fact]
    public void The_decorative_line_colour_is_never_the_only_edge_of_a_control_it_is_too_faint_for_one()
    {
        // --p-line is for rules and card borders. This test records why a control does not use it, so nobody "tidies" the control border back to it.
        ProductAccent.ContrastRatio(Tokens["--p-line"], Tokens["--p-bg"]).ShouldBeLessThan(NonText);
    }

    [Fact]
    public void The_skip_link_and_the_focus_ring_use_the_ink_not_the_product_accent_so_a_hostile_accent_cannot_hide_them()
    {
        Css.Declarations(".ts-skip-link")["color"].ShouldBe("var(--p-ink)");
        Css.Declarations(".ts-skip-link")["background"].ShouldBe("var(--p-bg)");
        Css.OutsideMedia().Declarations(":focus-visible,.btn:focus-visible,.form-control:focus,.form-select:focus,.form-check-input:focus")["outline"].ShouldBe("3px solid var(--p-ink) !important");
        ProductAccent.ContrastRatio(Tokens["--p-ink"], Tokens["--p-bg"]).ShouldBeGreaterThanOrEqualTo(7.0, "the ring is ink on the page");
    }

    [Fact]
    public void The_semantic_tokens_are_the_brand_colours_the_admin_already_uses_for_the_same_meanings()
    {
        // BRAND.md: --p-error is --st-spam, --p-success is --st-open, --p-warn is --st-pending (light theme). One red, one green, one amber across both apps.
        var brand = BrandTokenTable.Read();
        string Light(string name) => CssColor.Normalize(brand.Single(t => t.Name == name).Light);

        Light("p-error").ShouldBe(Light("st-spam"));
        Light("p-success").ShouldBe(Light("st-open"));
        Light("p-warn").ShouldBe(Light("st-pending"));
    }
}
```

`tests/TechStrap.Admin.Tests/StyleBuildTests.cs`

```diff
--- a/tests/TechStrap.Admin.Tests/StyleBuildTests.cs
+++ b/tests/TechStrap.Admin.Tests/StyleBuildTests.cs
@@ -21,11 +21,11 @@ public sealed class StyleBuildTests
     [Fact]
     public void The_token_table_of_BRAND_md_was_read()
     {
-        // 18 surface + 5 tint + 6 status + 10 brand-moment tokens have a dark value; the 5 portal tokens do not.
+        // 18 surface + 5 tint + 6 status + 10 brand-moment tokens have a dark value; the 11 portal tokens (5 neutral, 6 semantic) do not.
         var tokens = BrandTokenTable.Read();
 
         tokens.Count(t => t.Dark is not null).ShouldBe(39);
-        tokens.Count(t => t.Dark is null).ShouldBe(5);
+        tokens.Count(t => t.Dark is null).ShouldBe(11);
     }
 
     [Fact]
```

`tests/TechStrap.Portal.Tests/Components/PortalLayoutTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Components/PortalLayoutTests.cs
+++ b/tests/TechStrap.Portal.Tests/Components/PortalLayoutTests.cs
@@ -29,7 +29,7 @@ public sealed class PortalLayoutTests : BunitContext
 
         cut.Find("#page").TextContent.ShouldBe("the page");
         cut.FindAll("header").ShouldBeEmpty();
-        cut.FindAll("footer.ts-product-footer").ShouldBeEmpty();
+        cut.FindAll("nav.ts-product-footer").ShouldBeEmpty();
         cut.Find("div.ts-accent-scope").HasAttribute("style").ShouldBeFalse();
         cut.FindAll("footer.ts-powered").Count.ShouldBe(1);
         cut.Markup.ShouldNotContain("--ts-accent");
@@ -49,7 +49,7 @@ public sealed class PortalLayoutTests : BunitContext
         header.QuerySelector("a.ts-product-name")!.GetAttribute("href").ShouldBe("/p/paperplane");
         header.QuerySelector("img.ts-product-logo")!.GetAttribute("src").ShouldBe("https://cdn.example.com/paperplane.png");
         cut.Find("main #page").TextContent.ShouldBe("the page");
-        cut.Find("footer.ts-product-footer a").GetAttribute("href").ShouldBe("/p/paperplane/lost-link");
+        cut.Find("nav.ts-product-footer a").GetAttribute("href").ShouldBe("/p/paperplane/lost-link");
         cut.FindAll("footer.ts-powered").Count.ShouldBe(1);
         cut.Markup.IndexOf("ts-product-header", StringComparison.Ordinal).ShouldBeLessThan(cut.Markup.IndexOf("id=\"page\"", StringComparison.Ordinal));
         cut.Markup.IndexOf("id=\"page\"", StringComparison.Ordinal).ShouldBeLessThan(cut.Markup.IndexOf("ts-product-footer", StringComparison.Ordinal));
@@ -127,6 +127,6 @@ public sealed class PortalLayoutTests : BunitContext
         var cut = RenderLayout();
 
         cut.FindAll("footer.ts-powered").ShouldBeEmpty();
-        cut.FindAll("footer.ts-product-footer").Count.ShouldBe(1);
+        cut.FindAll("nav.ts-product-footer").Count.ShouldBe(1);
     }
 }
```

`tests/TechStrap.Portal.Tests/Forms/ContactPostHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Forms/ContactPostHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/Forms/ContactPostHostTests.cs
@@ -147,12 +147,12 @@ public sealed class ContactPostHostTests
         var html = await response.Content.ReadAsStringAsync(Ct);
 
         response.StatusCode.ShouldBe(HttpStatusCode.OK);
-        html.ShouldContain("<section class=\"ts-error-summary alert alert-danger\" role=\"alert\" tabindex=\"-1\" autofocus aria-labelledby=\"error-summary-heading\">");
-        html.ShouldContain("<a href=\"#email\">Enter a valid email address, like name@example.com.</a>");
-        html.ShouldContain("<a href=\"#subject\">Enter a subject.</a>");
+        html.ShouldContain("<section class=\"ts-error-summary alert alert-danger\" role=\"alert\" tabindex=\"-1\" autofocus aria-labelledby=\"error-summary-heading\" data-enhance-nav=\"false\">");
+        html.ShouldContain("<a href=\"/p/paperplane/contact#email\">Enter a valid email address, like name@example.com.</a>");
+        html.ShouldContain("<a href=\"/p/paperplane/contact#subject\">Enter a subject.</a>");
         html.ShouldContain("<p id=\"email-error\" class=\"ts-field-error\">Enter a valid email address, like name@example.com.</p>");
         html.ShouldContain("aria-describedby=\"email-error\" aria-invalid=\"true\"");
-        html.ShouldNotContain("<a href=\"#name\">", Case.Sensitive, "a valid field has no error");
+        html.ShouldNotContain("<a href=\"/p/paperplane/contact#name\">", Case.Sensitive, "a valid field has no error");
         Text(html, "name").ShouldBe("Ada");
         Text(html, "email").ShouldBe("not an address");
         html.ShouldContain("It jams &lt;b&gt;every&lt;/b&gt; time.</textarea>");
@@ -173,7 +173,7 @@ public sealed class ContactPostHostTests
         var html = await response.Content.ReadAsStringAsync(Ct);
 
         var summary = FormTestKit.Between(html, "<ul>", "</ul>");
-        Regex.Matches(summary, "<a href=\"#(\\w+)\">").Select(m => m.Groups[1].Value).ShouldBe(["name", "email", "subject", "body"]);
+        Regex.Matches(summary, "<a href=\"/p/paperplane/contact#(\\w+)\">").Select(m => m.Groups[1].Value).ShouldBe(["name", "email", "subject", "body"]);
     }
 
     [Fact]
@@ -271,7 +271,7 @@ public sealed class ContactPostHostTests
         html.ShouldContain("Attach at most 5 files.");
         html.ShouldContain("virus.exe is a type we cannot accept.");
         html.ShouldContain("empty.txt is empty. Remove it or choose another.");
-        html.ShouldContain("<a href=\"#attachments\">");
+        html.ShouldContain("<a href=\"/p/paperplane/contact#attachments\">");
         factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
     }
 
@@ -339,7 +339,7 @@ public sealed class ContactPostHostTests
         html.ShouldContain("One of the files is a type we cannot accept.");
         html.ShouldNotContain("API TEXT");
         html.ShouldNotContain("Domain text");
-        html.ShouldContain("<a href=\"#attachments\">");
+        html.ShouldContain("<a href=\"/p/paperplane/contact#attachments\">");
         Text(html, "email").ShouldBe("ada@example.com", "what was typed is kept");
     }
 
```

`tests/TechStrap.Portal.Tests/Forms/ContactReceivedHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Forms/ContactReceivedHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/Forms/ContactReceivedHostTests.cs
@@ -74,7 +74,8 @@ public sealed class ContactReceivedHostTests
         html.ShouldContain("We have received your request.");
         html.ShouldContain("We have emailed you a link to follow it. Check your inbox and your spam folder.");
         html.ShouldNotContain("ts-ticket-number");
-        html.ShouldNotContain("PAP-42");
+        // No number is shown to the visitor. (The skip link keeps the query string of the page it is on, so the text may sit in an href; what matters is the text of the page.)
+        new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html).Body!.TextContent.ShouldNotContain("PAP-42");
     }
 
     [Fact]
```

`tests/TechStrap.Portal.Tests/StyleBuildTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/StyleBuildTests.cs
+++ b/tests/TechStrap.Portal.Tests/StyleBuildTests.cs
@@ -23,7 +23,7 @@ public sealed class StyleBuildTests
     public void Every_portal_token_of_BRAND_md_is_a_custom_property_on_root()
     {
         var portalTokens = BrandTokenTable.Read().Where(t => t.Name.StartsWith("p-", StringComparison.Ordinal)).ToList();
-        portalTokens.Count.ShouldBe(5);
+        portalTokens.Count.ShouldBe(11, "the five neutral tokens and the six semantic ones (error, success, warning and their grounds)");
         var root = Css.Declarations(LightScope);
 
         foreach (var token in portalTokens)
```

`tests/TechStrap.Portal.Tests/Tickets/LostLinkHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Tickets/LostLinkHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/Tickets/LostLinkHostTests.cs
@@ -149,7 +149,7 @@ public sealed class LostLinkHostTests
         var html = await response.Content.ReadAsStringAsync(Ct);
 
         response.StatusCode.ShouldBe(HttpStatusCode.OK);
-        html.ShouldContain("<a href=\"#email\">");
+        html.ShouldContain("<a href=\"/p/paperplane/lost-link#email\">");
         html.ShouldContain("class=\"ts-field-error\"");
         html.ShouldNotContain("role=\"status\"", Case.Sensitive, "no confirmation for a malformed address");
         factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(0);
```

`tests/TechStrap.Portal.Tests/Tickets/TicketPageHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Tickets/TicketPageHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/Tickets/TicketPageHostTests.cs
@@ -199,8 +199,11 @@ public sealed class TicketPageHostTests
 
         html.ShouldNotContain("[token]");
         var contexts = TicketTestKit.TokenContexts(html);
-        contexts.Count.ShouldBe(2, "the form action and the one attachment link");
+        // The form action, the one attachment link, and the two jumps within this very page (the skip link and "jump to your reply": a root-relative address plus a fragment, because a bare #fragment would resolve
+        // against the document's base to the home page).
+        contexts.Count.ShouldBe(4);
         contexts.ShouldAllBe(c => c.Contains("action=\"/t/", StringComparison.Ordinal) || c.Contains("href=\"/t/", StringComparison.Ordinal));
+        Regex.Matches(html, "href=\"/t/" + TicketTestKit.Token + "#(main|reply)\"").Count.ShouldBe(2);
         html.ShouldNotContain("?" + TicketTestKit.Token);
         html.ShouldNotContain("=" + TicketTestKit.Token);
         Regex.Match(html, "<title>.*?</title>").Value.ShouldNotContain(TicketTestKit.Token);
```

`tests/TechStrap.Portal.Tests/Tickets/TicketReplyHostTests.cs`

```diff
--- a/tests/TechStrap.Portal.Tests/Tickets/TicketReplyHostTests.cs
+++ b/tests/TechStrap.Portal.Tests/Tickets/TicketReplyHostTests.cs
@@ -205,7 +205,7 @@ public sealed class TicketReplyHostTests
         var html = await response.Content.ReadAsStringAsync(Ct);
 
         response.StatusCode.ShouldBe(HttpStatusCode.OK);
-        html.ShouldContain("<a href=\"#body\">Write a message.</a>");
+        html.ShouldContain("<a href=\"/t/" + TicketTestKit.Token + "#body\">Write a message.</a>");
         html.ShouldContain("<p id=\"body-error\" class=\"ts-field-error\">Write a message.</p>");
         html.ShouldContain("It jams every time.", Case.Sensitive, "the conversation is still shown");
         factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(0);
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build tests/TechStrap.Portal.Tests -c Release
```

Expected: FAIL to compile (`PageLinks`, `BodyHeadings` and the new copy do not exist):

```text
20 errors, the first six:
tests\TechStrap.Portal.Tests\Components\BodyHeadingsTests.cs(11,9): error CS0103: The name 'BodyHeadings' does not exist in the current context
tests\TechStrap.Portal.Tests\Components\BodyHeadingsTests.cs(17,9): error CS0103: The name 'BodyHeadings' does not exist in the current context
tests\TechStrap.Portal.Tests\Components\BodyHeadingsTests.cs(27,9): error CS0103: The name 'BodyHeadings' does not exist in the current context
tests\TechStrap.Portal.Tests\Components\LayoutLandmarkHostTests.cs(46,46): error CS0117: 'ShellCopy' does not contain a definition for 'SkipToMain'
tests\TechStrap.Portal.Tests\Components\LayoutLandmarkHostTests.cs(79,65): error CS0117: 'ShellCopy' does not contain a definition for 'NavLabel'
tests\TechStrap.Portal.Tests\Components\LayoutLandmarkHostTests.cs(81,91): error CS0117: 'ShellCopy' does not contain a definition for 'NavHelp'
```

- [ ] **Step 3: Write the implementation**

Work in this order, building and running the Portal tests as you go (the layout change breaks the "neutral 404 is byte for byte" tests if a neutral page gets a skip link, which is the reason the link is inside the product branch).

1. **Tokens and docs.** The six tokens in `_brand-tokens.scss` and in BRAND.md (the `StyleBuildTests` count and the Admin's palette follow), and the width sentence of BRAND.md section 14.
2. **`PageLinks`, `BodyHeadings`, the copy.** The helper and the two copy additions.
3. **Markup.** The layout (skip link, `main`, header and footer navigation), the error summary, the reading wrapper on the reading pages, the one-sentence required notes, the ticket page's jump link and `h2#reply`, the search page's `h1`, the merged `KbSearchBox`, the category cards' class and the two body components.
4. **Styles.** Two new partials (`_layout.scss`: the widths, the header navigation, the phone and tablet rules; `_a11y.scss`: the semantic colors, the control edge, the form helpers' styles, the 44px targets, forced colors and reduced motion), the missing rules in `_components.scss`, and `$enable-smooth-scroll: false`.

`src/TechStrap.Portal/Components/BodyHeadings.cs` (new)

```csharp
namespace TechStrap.Portal.Components;

/// <summary>
/// One <c>h1</c> per page (PHASE-09 T16, UX brief): the page's own title is the <c>h1</c>, so a body the API sanitized and sent (an article, a message) that carries an <c>h1</c> of its own (a Markdown line that
/// starts with a single <c>#</c>) is shown as an <c>h2</c>. This is the only change the Portal makes to such a body. The sanitizer allows no attribute on a heading and writes its tags in lower case, so the two
/// tag spellings below are the only forms there are, and nothing but the tag name changes: no text, no attribute and no other tag is touched.
/// </summary>
public static class BodyHeadings
{
    public static string DemoteTitle(string html) =>
        html.Replace("<h1>", "<h2>", StringComparison.Ordinal).Replace("</h1>", "</h2>", StringComparison.Ordinal);
}
```

`src/TechStrap.Portal/Routing/PageLinks.cs` (new)

```csharp
using Microsoft.AspNetCore.WebUtilities;

namespace TechStrap.Portal.Routing;

/// <summary>
/// Links to a place on the CURRENT page (the error summary's field links, the skip link and the "jump to your reply" link). The document's <c>base</c> is <c>/</c> (it has to be: the stylesheet, the favicon and
/// <c>blazor.web.js</c> are relative to it), and under that base a bare <c>href="#email"</c> resolves to <c>/#email</c>, the home page, not to the field (D-045 09d addendum; the Admin hit the same bug). So the link
/// is written out in full as a root-relative path plus the fragment, which the browser treats as a jump within the page, and the link opts out of Blazor's enhanced navigation (<c>data-enhance-nav="false"</c>),
/// which would otherwise take the click, scroll, and leave the focus on the link instead of moving it to the field.
/// </summary>
public static class PageLinks
{
    private static readonly string[] None = [];

    /// <summary>
    /// The current page's root-relative address plus <paramref name="fragment"/>, built from the absolute <paramref name="currentUri"/> (<c>NavigationManager.Uri</c>). With <paramref name="keepQuery"/> the query
    /// parameters THIS PAGE READS stay (the prefill of the contact form, the reference of the received page, the search text), so a jump to <c>#main</c> is a jump and not a reload that loses what the page shows.
    /// Every other parameter is dropped: it changes nothing on the page and is never echoed into it, and a copy the output cache keeps for one visitor must not carry another's parameters.
    /// </summary>
    public static string ToFragment(string currentUri, string fragment, bool keepQuery)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fragment);
        var uri = new Uri(currentUri, UriKind.Absolute);
        var path = uri.AbsolutePath;
        var query = string.Empty;
        if (keepQuery)
        {
            var known = KnownParameters(path);
            var kept = QueryHelpers.ParseQuery(uri.Query)
                .Where(pair => known.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .SelectMany(pair => pair.Value.Select(value => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(value ?? string.Empty)}"))
                .ToList();
            query = kept.Count == 0 ? string.Empty : "?" + string.Join('&', kept);
        }

        return $"{path}{query}#{fragment}";
    }

    /// <summary>The query parameters a page of this address reads (see <see cref="PortalRoutes"/>); none for any other page.</summary>
    internal static IReadOnlyList<string> KnownParameters(string path)
    {
        var segments = Uri.UnescapeDataString(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 || !segments[0].Equals(PortalRoutes.ProductPrefix.Trim('/'), StringComparison.OrdinalIgnoreCase))
        {
            return None;
        }

        var page = segments[2..];
        return page switch
        {
            [var contact] when Is(contact, PortalRoutes.ContactSegment) => PortalRoutes.PrefillParameters,
            [var contact, var received] when Is(contact, PortalRoutes.ContactSegment) && Is(received, PortalRoutes.ReceivedSegment) => [PortalRoutes.ReceivedReferenceParameter],
            [var lostLink] when Is(lostLink, PortalRoutes.LostLinkSegment) => [PortalRoutes.SentParameter],
            [var kb, var search] when Is(kb, PortalRoutes.KbSegment) && Is(search, PortalRoutes.KbSearchSegment) => [PortalRoutes.QueryParameter, PortalRoutes.PageParameter],
            [var kb, _] when Is(kb, PortalRoutes.KbSegment) => [PortalRoutes.PageParameter],
            _ => None,
        };
    }

    private static bool Is(string segment, string expected) => segment.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
```

`src/TechStrap.Portal/Styles/_a11y.scss` (new)

```scss
// Portal accessibility styling (PHASE-09d, T16; BRAND.md sections 17, 18 and 24; UX-BRIEF-portal). Imported last. WCAG 2.2 AA.
//
// The semantic colors are the --p-error, --p-success and --p-warn tokens of BRAND.md (never a product accent). A state is never color alone: the error summary has a heading and a list, a success has its sentence,
// a field in error has text and a heavier border, the counter over its limit is bold and underlined.

// ---- skip link and the focus target of main ----

.ts-skip-link {
  position: absolute;
  top: -100px;
  left: 8px;
  z-index: 1000;
  display: inline-flex;
  align-items: center;
  min-height: 44px;
  padding: 0 16px;
  font-weight: 600;
  color: var(--p-ink);
  text-decoration: underline;
  background: var(--p-bg);
  border: 2px solid var(--p-ink);
  border-radius: $border-radius;

  &:focus,
  &:focus-visible {
    top: 8px;
  }
}

// main takes focus when the skip link is used (tabindex -1); the container itself shows no ring.
.ts-portal-main:focus {
  outline: none;
  box-shadow: none !important;
}

// ---- messages and errors ----

.alert-danger.ts-error-summary {
  --bs-alert-color: var(--p-ink);
  --bs-alert-bg: var(--p-error-bg);
  --bs-alert-border-color: var(--p-error);
  --bs-alert-link-color: var(--p-error);
  border-width: 2px;

  h2 {
    color: var(--p-error);
  }

  a {
    color: var(--p-error);
    font-weight: 600;
  }
}

.alert-warning {
  --bs-alert-color: var(--p-ink);
  --bs-alert-bg: var(--p-warn-bg);
  --bs-alert-border-color: var(--p-warn);
  --bs-alert-link-color: var(--p-warn);
  border-width: 2px;
}

.ts-field-error {
  color: var(--p-error);
  font-weight: 500;
}

.form-control[aria-invalid="true"] {
  border-color: var(--p-error);
  border-width: 2px;
}

.ts-confirmation {
  padding: 12px 16px;
  color: var(--p-ink);
  background: var(--p-success-bg);
  border: 2px solid var(--p-success);
  border-radius: $border-radius;
}

// A control's edge must be seen by everyone (WCAG 1.4.11): the neutral border is the secondary ink, not the decorative line color.
.form-control,
.form-select {
  border-color: var(--p-ink2);
}

.form-text {
  color: var(--p-ink2);
}

// ---- the form helpers (portal-forms.js) ----

.ts-form-note {
  margin: 0 0 16px;
  color: var(--p-ink2);
}

.ts-form {
  display: block;
}

.ts-char-count {
  display: block;
  margin-top: 4px;
  font-size: .875rem;
  color: var(--p-ink2);
  text-align: right;

  &[hidden] {
    display: none;
  }
}

.ts-count-text {
  font-variant-numeric: tabular-nums;
}

.ts-count-over {
  font-weight: 700;
  color: var(--p-error);
  text-decoration: underline;
}

.ts-copy {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 4px 12px;
  align-items: center;
  margin-left: 8px;
  vertical-align: middle;
}

.ts-copy-button {
  white-space: nowrap;
}

.ts-copy-status {
  font-size: .875rem;
  color: var(--p-ink2);
}

// A form that is being sent: the button is disabled and says so; it must still read as a button (not faded to nothing).
.btn:disabled {
  opacity: .75;
  cursor: progress;
}

// ---- target sizes: every link a visitor may have to hit is at least 44px tall (BRAND.md section 24) ----

.ts-site-nav-links a,
.ts-breadcrumbs a,
.ts-pager a,
.ts-pager-state,
.ts-next-links a,
.ts-product-footer a,
.ts-powered a,
.ts-error-summary a,
.ts-help-link a,
.ts-jump-reply,
.ts-attachments a,
.ts-kb-card h2 a {
  display: inline-flex;
  align-items: center;
  min-height: 44px;
}

.ts-product-footer {
  padding-top: 8px;
}

.ts-powered a {
  min-height: 44px;
  padding: 0 4px;
}

.ts-next-links {
  padding-left: 0;
  list-style: none;
}

.ts-jump-reply {
  margin-bottom: 8px;
}

// ---- forced colors (Windows high contrast): keep what color alone would say, and keep the 3px focus outline (the accent halo is dropped) ----

@media (forced-colors: active) {
  :focus-visible,
  .btn:focus-visible,
  .form-control:focus,
  .form-select:focus,
  .form-check-input:focus {
    outline: 3px solid Highlight !important;
    box-shadow: none !important;
  }

  .ts-skip-link {
    border: 2px solid CanvasText;
  }

  .ts-error-summary,
  .ts-confirmation,
  .alert-warning {
    border: 2px solid CanvasText;
  }

  .ts-status-banner,
  .ts-message-own {
    border-left: 6px solid Highlight;
  }

  .form-control[aria-invalid="true"] {
    border: 3px solid CanvasText;
  }

  .btn:disabled {
    color: GrayText;
    border-color: GrayText;
    opacity: 1;
  }

  .ts-count-over {
    border-bottom: 2px solid CanvasText;
  }
}

// ---- reduced motion (BRAND.md section 17): nothing animates or transitions ----

@media (prefers-reduced-motion: reduce) {
  *,
  *::before,
  *::after {
    animation: none !important;
    transition: none !important;
  }
}
```

`src/TechStrap.Portal/Styles/_layout.scss` (new)

```scss
// Portal layout widths and the responsive rules (PHASE-09d, T16). Imported after _components, so a rule here refines one there.
//
// Two widths, each one token (BRAND.md section 14): a reading column (a form, the conversation, an article, a confirmation) and a wide container (the header, the footer, search, the categories, the product home).
// Mobile first: one column below 768px, nothing wider than the screen at 360px, and no horizontal scroll of the page (a wide table or code block scrolls in its own box, in _components).

:root,
[data-bs-theme="light"] {
  --ts-reading-width: 40rem;
  --ts-wide-width: 64rem;
  --ts-gutter: 16px;
}

// A reading page puts its content in this column; a wide page does not.
.ts-reading {
  max-width: var(--ts-reading-width);
}

.ts-portal-main,
.ts-product-header,
.ts-product-footer {
  max-width: var(--ts-wide-width);
}

.ts-portal-main {
  padding: 24px var(--ts-gutter) 32px;
}

.ts-portal {
  position: relative;
  min-height: 100vh;
  min-height: 100dvh;
}

// The header: the product's name and logo on the left, the two ways in on the right; they wrap under the name on a phone.
.ts-site-nav {
  display: flex;
  flex-wrap: wrap;
  gap: 0 24px;
  align-items: center;
  justify-content: space-between;
}

.ts-site-nav-links {
  display: flex;
  flex-wrap: wrap;
  gap: 0 16px;
  margin: 0;
  padding: 0;
  list-style: none;
}

// The search box: a text box and its button on one row, stacked on a phone.
.ts-help-search {
  max-width: var(--ts-reading-width);
  margin: 16px 0 24px;
}

.ts-help-search-row {
  .form-control {
    min-width: 0;
  }
}

@media (max-width: 479.98px) {
  .ts-help-search-row {
    flex-direction: column;

    .btn {
      width: 100%;
    }
  }

  // The primary action of a form fills the row on a phone.
  .ts-form .btn,
  .ts-help-contact .btn {
    width: 100%;
  }

  .ts-pager {
    justify-content: center;
  }
}

// The help center home lists its categories as cards, two across from a tablet up.
@media (min-width: 768px) {
  .ts-kb-list--cards {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 16px;

    li + li {
      margin-top: 0;
    }
  }

  .ts-portal-main {
    padding-top: 32px;
  }
}

@media (min-width: 1200px) {
  .ts-kb-list--cards {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}
```

`assets/brand/scss/_brand-tokens.scss`

```diff
--- a/assets/brand/scss/_brand-tokens.scss
+++ b/assets/brand/scss/_brand-tokens.scss
@@ -94,7 +94,13 @@ $ts-portal: (
   "p-soft": #F5F5F7,
   "p-ink": #1B1B22,
   "p-ink2": #4A4A57,
-  "p-line": #D4D4DC
+  "p-line": #D4D4DC,
+  "p-error": #B3141C,
+  "p-error-bg": #FDECEE,
+  "p-success": #14702F,
+  "p-success-bg": #E8F5EC,
+  "p-warn": #8A5300,
+  "p-warn-bg": #FFF3E0
 );
 
 // Fallback values for the three product-accent properties. The Portal sets the real ones at runtime on its root
```

`docs/BRAND.md`

```diff
--- a/docs/BRAND.md
+++ b/docs/BRAND.md
@@ -357,7 +357,13 @@ This code **never changes meaning and is never reused** for decoration, status,
 | `--p-soft` | Soft panels | `#F5F5F7` | `$light` |
 | `--p-ink` | Text | `#1B1B22` | `$body-color` |
 | `--p-ink2` | Secondary text | `#4A4A57` | `$secondary-color` |
-| `--p-line` | Borders | `#D4D4DC` | `$border-color` |
+| `--p-line` | Decorative borders and rules (never the only edge of a control) | `#D4D4DC` | `$border-color` |
+| `--p-error` | Error text, the error summary's border and a field's error text (the same red as `--st-spam`) | `#B3141C` | `$danger` |
+| `--p-error-bg` | Ground of the error summary | `#FDECEE` | none |
+| `--p-success` | Success text and border (the same green as `--st-open`) | `#14702F` | `$success` |
+| `--p-success-bg` | Ground of a success message | `#E8F5EC` | none |
+| `--p-warn` | Warning text and border (the same amber as `--st-pending`) | `#8A5300` | `$warning` |
+| `--p-warn-bg` | Ground of a notice | `#FFF3E0` | none |
 | `--accent`, `--on-accent`, `--accent-ink` | Product-supplied; see section 22 | per product | `$primary`, button text, `$link-color` |
 
 # 13. Geometry
@@ -378,7 +384,7 @@ This code **never changes meaning and is never reused** for decoration, status,
 - **Density:** dense and plain. No hero blocks, no oversized padding. Message line length capped at 68ch.
 - **Status bar:** Admin only. Sticky bottom strip with keycap hints and a transient message; hints hide under 900px.
 - **Brand-moment screens:** one centered retro window, 400px max, on the plain page background. Nothing competes with it.
-- **Portal:** 640px single column, product bar with a 6px accent top border, product name and logo top left, plain forms, "Powered by TechStrap" footer.
+- **Portal:** a 640px reading column (one token, `--ts-reading-width`) for forms, the conversation, an article and the confirmation, and a 1024px wide container (`--ts-wide-width`) for search, the categories and the product home; one column below 768px. Product bar with a 6px accent top border, product name and logo top left, plain forms, "Powered by TechStrap" footer.
 
 # 15. Imagery
 
```

`src/TechStrap.Admin/Components/Showcase/PaletteSwatches.razor.cs`

```diff
--- a/src/TechStrap.Admin/Components/Showcase/PaletteSwatches.razor.cs
+++ b/src/TechStrap.Admin/Components/Showcase/PaletteSwatches.razor.cs
@@ -11,7 +11,7 @@ public partial class PaletteSwatches
         [PaletteGroup.Tints] = ["canary", "canary-edge", "pink", "pink-edge", "note-ink"],
         [PaletteGroup.Status] = ["st-new", "st-open", "st-pending", "st-solved", "st-closed", "st-spam"],
         [PaletteGroup.BrandMoment] = ["bm-plate", "bm-edge", "bm-bar", "bm-on-bar", "bm-text", "bm-text2", "bm-crt", "bm-on-crt", "bm-led", "bm-shadow"],
-        [PaletteGroup.Portal] = ["p-bg", "p-soft", "p-ink", "p-ink2", "p-line"],
+        [PaletteGroup.Portal] = ["p-bg", "p-soft", "p-ink", "p-ink2", "p-line", "p-error", "p-error-bg", "p-success", "p-success-bg", "p-warn", "p-warn-bg"],
     };
 
     [Parameter, EditorRequired]
```

`src/TechStrap.Portal/Components/Kb/KbArticleBody.razor`

```diff
--- a/src/TechStrap.Portal/Components/Kb/KbArticleBody.razor
+++ b/src/TechStrap.Portal/Components/Kb/KbArticleBody.razor
@@ -1,9 +1,9 @@
-<div class="ts-kb-article-body">@((MarkupString)Html)</div>
+<div class="ts-kb-article-body">@((MarkupString)BodyHeadings.DemoteTitle(Html))</div>
 
 @code {
     /// <summary>
-    /// The article body exactly as the API sent it. The API renders the agent's Markdown and sanitizes the HTML (D-044); the Portal does not sanitize again and does not change a byte, so what a visitor reads is what the API
-    /// decided is safe. This and <c>CustomerMessageBody</c> are the only two places the Portal turns text into elements (PortalRules.MarkupStringSites); every other string it shows is encoded.
+    /// The article body as the API sent it. The API renders the agent's Markdown and sanitizes the HTML (D-044); the Portal does not sanitize again and changes one thing only: an <c>h1</c> in the body becomes an <c>h2</c>
+    /// (<see cref="BodyHeadings"/>), because the page's own title is its one <c>h1</c>. So what a visitor reads is what the API decided is safe. This and <c>CustomerMessageBody</c> are the only two places the Portal turns text into elements (PortalRules.MarkupStringSites); every other string it shows is encoded.
     /// </summary>
     [Parameter, EditorRequired]
     public string Html { get; set; } = string.Empty;
```

`src/TechStrap.Portal/Components/KbCopy.cs`

```diff
--- a/src/TechStrap.Portal/Components/KbCopy.cs
+++ b/src/TechStrap.Portal/Components/KbCopy.cs
@@ -24,6 +24,9 @@ public static class KbCopy
     public const string StillNeedHelpText = "If this did not answer your question, contact support and we will help you.";
 
     // Search.
+    /// <summary>The h1 of the search page: different from the help center's, so two tabs of the two pages are told apart.</summary>
+    public const string SearchPageHeading = "Search the help center";
+
     public const string SearchHeading = "Search results";
     public const string SearchPromptHeading = "What are you looking for?";
     public const string SearchPromptText = "Type a few words about your question and search the help articles.";
```

`src/TechStrap.Portal/Components/Layout/PortalLayout.razor`

```diff
--- a/src/TechStrap.Portal/Components/Layout/PortalLayout.razor
+++ b/src/TechStrap.Portal/Components/Layout/PortalLayout.razor
@@ -4,9 +4,11 @@
     <div class="ts-portal">
         @if (Theme is { } header)
         {
+            @* The skip link belongs to the product header it skips. A neutral page (the root, not-found, error) has none: its body must stay the same for every address, byte for byte, and the link is built from the address. *@
+            <a class="ts-skip-link" href="@SkipHref" data-enhance-nav="false">@ShellCopy.SkipToMain</a>
             <ProductHeader Theme="header" />
         }
-        <main class="ts-portal-main">@Body</main>
+        <main id="main" tabindex="-1" class="ts-portal-main">@Body</main>
         @if (Theme is { } footer)
         {
             <ProductFooter Theme="footer" />
```

`src/TechStrap.Portal/Components/Layout/PortalLayout.razor.cs`

```diff
--- a/src/TechStrap.Portal/Components/Layout/PortalLayout.razor.cs
+++ b/src/TechStrap.Portal/Components/Layout/PortalLayout.razor.cs
@@ -1,5 +1,6 @@
 using Microsoft.AspNetCore.Components;
 using TechStrap.Portal.Products;
+using TechStrap.Portal.Routing;
 
 namespace TechStrap.Portal.Components.Layout;
 
@@ -12,8 +13,13 @@ public partial class PortalLayout : LayoutComponentBase, IDisposable
     [Inject]
     private ProductScope Scope { get; set; } = default!;
 
+    [Inject]
+    private NavigationManager Navigation { get; set; } = default!;
+
     private ProductThemeViewModel? Theme => Scope.Theme;
 
+    private string SkipHref => PageLinks.ToFragment(Navigation.Uri, "main", keepQuery: true);
+
     protected override void OnInitialized() => Scope.Changed += OnScopeChanged;
 
     public void Dispose() => Scope.Changed -= OnScopeChanged;
```

`src/TechStrap.Portal/Components/Layout/ProductFooter.razor`

```diff
--- a/src/TechStrap.Portal/Components/Layout/ProductFooter.razor
+++ b/src/TechStrap.Portal/Components/Layout/ProductFooter.razor
@@ -1,6 +1,6 @@
-<footer class="ts-product-footer">
+<nav class="ts-product-footer" aria-label="@ShellCopy.FooterNavLabel">
     <a href="@PortalRoutes.LostLink(Theme.Key)">@ShellCopy.LostLinkPrompt</a>
-</footer>
+</nav>
 
 @code {
     [Parameter, EditorRequired]
```

`src/TechStrap.Portal/Components/Layout/ProductHeader.razor`

```diff
--- a/src/TechStrap.Portal/Components/Layout/ProductHeader.razor
+++ b/src/TechStrap.Portal/Components/Layout/ProductHeader.razor
@@ -1,5 +1,11 @@
 <header class="ts-product-header">
+  <nav class="ts-site-nav" aria-label="@ShellCopy.NavLabel">
     <a class="ts-product-name" href="@PortalRoutes.ProductHome(Theme.Key)">@if (Theme.LogoUrl is { } logo){<img class="ts-product-logo" src="@logo" alt="" height="32" />}@Theme.DisplayName</a>
+    <ul class="ts-site-nav-links">
+        <li><a href="@PortalRoutes.KbHome(Theme.Key)">@ShellCopy.NavHelp</a></li>
+        <li><a href="@PortalRoutes.Contact(Theme.Key)">@ShellCopy.NavContact</a></li>
+    </ul>
+  </nav>
 </header>
 
 @code {
```

`src/TechStrap.Portal/Components/Pages/Contact.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/Contact.razor
+++ b/src/TechStrap.Portal/Components/Pages/Contact.razor
@@ -6,6 +6,7 @@
 @if (Theme is { } theme)
 {
     <PageTitle>@ContactCopy.Title(theme.DisplayName)</PageTitle>
+    <div class="ts-reading">
     <h1>@ContactCopy.Heading</h1>
     <p class="ts-help-link"><a href="@PortalRoutes.KbHome(theme.Key)">@ContactCopy.BrowseHelp</a></p>
     <ErrorSummary Errors="Errors" />
@@ -13,6 +14,7 @@
     {
         <p class="alert alert-warning" role="alert">@Notice</p>
     }
+    <p class="ts-form-note">@ContactCopy.RequiredNote</p>
     <form method="post" action="@PortalRoutes.Contact(theme.Key)" enctype="multipart/form-data" novalidate @formname="@FormHandler" @onsubmit="SubmitAsync" class="ts-form" data-sending-label="@FormCopy.Sending">
         <AntiforgeryToken />
         <FormGuard Name="Form.SubmitId" />
@@ -25,6 +27,7 @@
         <HoneypotField Name="Form.Website" Value="@Form?.Website" />
         <button type="submit" class="btn btn-primary">@ContactCopy.Submit</button>
     </form>
+    </div>
 }
 else if (UnavailableMessage is not null)
 {
```

`src/TechStrap.Portal/Components/Pages/ContactReceived.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/ContactReceived.razor
+++ b/src/TechStrap.Portal/Components/Pages/ContactReceived.razor
@@ -4,6 +4,7 @@
 @if (Theme is { } theme)
 {
     <PageTitle>@ContactCopy.ReceivedTitle(theme.DisplayName)</PageTitle>
+    <div class="ts-reading">
     <h1>@ContactCopy.ReceivedHeading</h1>
     @if (TicketNumber is not null)
     {
@@ -19,6 +20,7 @@
         <li><a href="@PortalRoutes.ProductHome(theme.Key)">@ContactCopy.BackTo(theme.DisplayName)</a></li>
         <li><a href="@PortalRoutes.LostLink(theme.Key)">@ContactCopy.LostLinkPrompt</a></li>
     </ul>
+    </div>
 }
 else if (UnavailableMessage is not null)
 {
```

`src/TechStrap.Portal/Components/Pages/KbArticle.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/KbArticle.razor
+++ b/src/TechStrap.Portal/Components/Pages/KbArticle.razor
@@ -9,6 +9,7 @@
 else if (Theme is { } theme && Article is { } article)
 {
     <SeoHead Title="@KbCopy.ArticleTitle(article.Title, theme.DisplayName)" Description="@Description" RelativeUrl="@PortalRoutes.KbArticle(theme.Key, article.CategorySlug, article.Slug)" ImageUrl="@KbSeo.Image(theme)" ImageAlt="@theme.DisplayName" OgType="article" StructuredData="@StructuredData" />
+    <div class="ts-reading">
     <KbBreadcrumbs Crumbs="@Trail" />
     <article class="ts-kb-article">
         <h1>@article.Title</h1>
@@ -20,4 +21,5 @@ else if (Theme is { } theme && Article is { } article)
         <p>@KbCopy.StillNeedHelpText</p>
         <p><a class="btn btn-primary" href="@PortalRoutes.Contact(theme.Key)">@KbCopy.ContactUs</a></p>
     </aside>
+    </div>
 }
```

`src/TechStrap.Portal/Components/Pages/KbHome.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/KbHome.razor
+++ b/src/TechStrap.Portal/Components/Pages/KbHome.razor
@@ -20,7 +20,7 @@ else if (Theme is { } theme && Categories is { } categories)
     }
     else
     {
-        <ul class="ts-kb-list">
+        <ul class="ts-kb-list ts-kb-list--cards">
             @foreach (var category in categories)
             {
                 <li>
```

`src/TechStrap.Portal/Components/Pages/KbSearch.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/KbSearch.razor
+++ b/src/TechStrap.Portal/Components/Pages/KbSearch.razor
@@ -10,7 +10,7 @@ else if (Theme is { } theme)
 {
     <SeoHead Title="@KbCopy.SearchTitle(theme.DisplayName)" Description="@KbCopy.SearchDescription(theme.DisplayName)" RelativeUrl="@PortalRoutes.KbSearch(theme.Key)" ImageUrl="@KbSeo.Image(theme)" ImageAlt="@theme.DisplayName" NoIndex="@(Text.Length > 0)" />
     <KbBreadcrumbs Crumbs="@Crumbs(theme)" />
-    <h1>@KbCopy.HomeHeading</h1>
+    <h1>@KbCopy.SearchPageHeading</h1>
     <KbSearchBox ProductKey="@theme.Key" Query="@Text" />
     @if (Text.Length == 0)
     {
```

`src/TechStrap.Portal/Components/Pages/LostLink.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/LostLink.razor
+++ b/src/TechStrap.Portal/Components/Pages/LostLink.razor
@@ -4,6 +4,7 @@
 @if (Theme is { } theme)
 {
     <PageTitle>@LostLinkCopy.Title(theme.DisplayName)</PageTitle>
+    <div class="ts-reading">
     <h1>@LostLinkCopy.Heading</h1>
     @if (SentFlag == "1")
     {
@@ -18,6 +19,7 @@
         {
             <p class="alert alert-warning" role="alert">@Notice</p>
         }
+        <p class="ts-form-note">@LostLinkCopy.RequiredNote</p>
         <form method="post" action="@PortalRoutes.LostLink(theme.Key)" novalidate @formname="@FormHandler" @onsubmit="SubmitAsync" class="ts-form" data-sending-label="@FormCopy.Sending">
             <AntiforgeryToken />
             <FormGuard Name="Form.SubmitId" />
@@ -25,6 +27,7 @@
             <button type="submit" class="btn btn-primary">@LostLinkCopy.Submit</button>
         </form>
     }
+    </div>
 }
 else if (UnavailableMessage is not null)
 {
```

`src/TechStrap.Portal/Components/Pages/ProductHome.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/ProductHome.razor
+++ b/src/TechStrap.Portal/Components/Pages/ProductHome.razor
@@ -5,13 +5,7 @@
 {
     <PageTitle>@ShellCopy.ProductTitle(theme.DisplayName)</PageTitle>
     <h1>@ShellCopy.HomeHeading</h1>
-    <form method="get" action="@PortalRoutes.KbSearch(theme.Key)" role="search" class="ts-help-search">
-        <label for="kb-search" class="form-label">@ShellCopy.SearchLabel</label>
-        <div class="ts-help-search-row">
-            <input id="kb-search" name="q" type="search" class="form-control" maxlength="@KbLimits.MaxSearchTextChars" autocomplete="off" />
-            <button type="submit" class="btn btn-primary">@ShellCopy.SearchButton</button>
-        </div>
-    </form>
+    <KbSearchBox ProductKey="@theme.Key" />
     <p class="ts-help-contact"><a class="btn btn-primary" href="@PortalRoutes.Contact(theme.Key)">@ShellCopy.ContactSupport</a></p>
 }
 else if (UnavailableMessage is not null)
```

`src/TechStrap.Portal/Components/Pages/Ticket.razor`

```diff
--- a/src/TechStrap.Portal/Components/Pages/Ticket.razor
+++ b/src/TechStrap.Portal/Components/Pages/Ticket.razor
@@ -5,10 +5,12 @@
 @if (Model is { } ticket)
 {
     <PageTitle>@TicketCopy.Title(ticket.Number)</PageTitle>
+    <div class="ts-reading">
     <h1><span class="ts-ticket-number">@ticket.Number</span> <span class="ts-ticket-subject">@ticket.Subject</span></h1>
     <TicketStatusBanner Label="@ticket.StatusLabel" Note="@ticket.StatusNote" IsClosed="@ticket.IsClosed" />
+    <a class="ts-jump-reply" href="@PageLinks.ToFragment(Navigation.Uri, "reply", keepQuery: false)" data-enhance-nav="false">@TicketCopy.JumpToReply</a>
     <MessageThread Messages="@ticket.Messages" />
-    <h2 class="h5">@TicketCopy.ReplyHeading</h2>
+    <h2 id="reply" tabindex="-1" class="h5">@TicketCopy.ReplyHeading</h2>
     @if (FollowUpConfirmation)
     {
         <p role="status">@TicketCopy.FollowUpStarted</p>
@@ -20,13 +22,15 @@
         {
             <p class="alert alert-warning" role="alert">@Notice</p>
         }
+        <p class="ts-form-note">@TicketCopy.ReplyNote</p>
         @ReplyForm(ticket.IsClosed ? TicketCopy.FollowUpButton : TicketCopy.ReplyButton)
     }
+    </div>
 }
 else if (UnavailableMessage is not null)
 {
     <PageTitle>@ShellCopy.UnavailableTitle</PageTitle>
-    <section class="ts-state" role="alert">
+    <section class="ts-state ts-reading" role="alert">
         <h1>@ShellCopy.UnavailableTitle</h1>
         <p>@UnavailableMessage</p>
     </section>
```

`src/TechStrap.Portal/Components/ShellCopy.cs`

```diff
--- a/src/TechStrap.Portal/Components/ShellCopy.cs
+++ b/src/TechStrap.Portal/Components/ShellCopy.cs
@@ -19,6 +19,14 @@ public static class ShellCopy
     public const string LostLinkPrompt = "Lost your ticket link?";
 
     // The calm failure state of a product page.
+    public const string SkipToMain = "Skip to main content";
+
+    // The landmarks (the accessible names of the two navigation regions).
+    public const string NavLabel = "Main";
+    public const string NavHelp = "Help articles";
+    public const string NavContact = "Contact support";
+    public const string FooterNavLabel = "More help";
+
     public const string UnavailableTitle = "This page could not be loaded.";
     public const string UnavailableRetry = "Try again";
 
```

`src/TechStrap.Portal/Components/Tickets/CustomerMessageBody.razor`

```diff
--- a/src/TechStrap.Portal/Components/Tickets/CustomerMessageBody.razor
+++ b/src/TechStrap.Portal/Components/Tickets/CustomerMessageBody.razor
@@ -1,9 +1,9 @@
-<div class="ts-message-body">@((MarkupString)Html)</div>
+<div class="ts-message-body">@((MarkupString)BodyHeadings.DemoteTitle(Html))</div>
 
 @code {
     /// <summary>
     /// The body of one public message, rendered as the markup it is. This is the Portal's single place that turns text into markup (PortalRules.MarkupStringSites; D-045 addendum): the API sanitizes every message body
-    /// before it sends it (the single source of truth, so the Portal never sanitizes again), and every other string on the ticket page (subject, author, file names) is encoded by Razor.
+    /// before it sends it (the single source of truth, so the Portal never sanitizes again; it only shows an <c>h1</c> in a body as an <c>h2</c>, <see cref="BodyHeadings"/>), and every other string on the ticket page (subject, author, file names) is encoded by Razor.
     /// </summary>
     [Parameter, EditorRequired]
     public string Html { get; set; } = string.Empty;
```

`src/TechStrap.Portal/Components/Ui/ErrorSummary.razor`

```diff
--- a/src/TechStrap.Portal/Components/Ui/ErrorSummary.razor
+++ b/src/TechStrap.Portal/Components/Ui/ErrorSummary.razor
@@ -1,11 +1,12 @@
+@inject NavigationManager Navigation
 @if (Errors.Count > 0)
 {
-    <section class="ts-error-summary alert alert-danger" role="alert" tabindex="-1" autofocus aria-labelledby="error-summary-heading">
+    <section class="ts-error-summary alert alert-danger" role="alert" tabindex="-1" autofocus aria-labelledby="error-summary-heading" data-enhance-nav="false">
         <h2 id="error-summary-heading" class="h5">@Heading</h2>
         <ul>
             @foreach (var error in Errors)
             {
-                <li>@if (error.Field is { } field){<a href="#@field">@error.Message</a>}else{@error.Message}</li>
+                <li>@if (error.Field is { } field){<a href="@PageLinks.ToFragment(Navigation.Uri, field, keepQuery: false)">@error.Message</a>}else{@error.Message}</li>
             }
         </ul>
     </section>
```

`src/TechStrap.Portal/Components/Ui/ProductUnavailable.razor`

```diff
--- a/src/TechStrap.Portal/Components/Ui/ProductUnavailable.razor
+++ b/src/TechStrap.Portal/Components/Ui/ProductUnavailable.razor
@@ -1,4 +1,4 @@
-<section class="ts-state" role="alert">
+<section class="ts-state ts-reading" role="alert">
     <h1>@ShellCopy.UnavailableTitle</h1>
     <p>@Message</p>
     <p><a class="btn btn-outline-primary" href="@RetryHref">@ShellCopy.UnavailableRetry</a></p>
```

`src/TechStrap.Portal/Forms/ContactCopy.cs`

```diff
--- a/src/TechStrap.Portal/Forms/ContactCopy.cs
+++ b/src/TechStrap.Portal/Forms/ContactCopy.cs
@@ -11,6 +11,9 @@ public static class ContactCopy
     public const string BodyLabel = "Message";
     public const string Submit = "Send message";
 
+    /// <summary>Required is said in words, not by an asterisk (UX brief): the one sentence above the form.</summary>
+    public const string RequiredNote = "All fields are required, except attachments.";
+
     /// <summary>The fallback inside the suggestions element, shown only without script: a plain link to the help search.</summary>
     public const string SuggestFallback = "Search the help articles first";
 
```

`src/TechStrap.Portal/Forms/LostLinkForm.cs`

```diff
--- a/src/TechStrap.Portal/Forms/LostLinkForm.cs
+++ b/src/TechStrap.Portal/Forms/LostLinkForm.cs
@@ -16,6 +16,7 @@ public static class LostLinkCopy
     public const string Intro = "Enter the email address you used when you contacted us. If we have tickets for that address, we will send you a new link.";
     public const string EmailLabel = "Email address";
     public const string Submit = "Send me a new link";
+    public const string RequiredNote = "The email address is required.";
     public const string Confirmation = "If we have tickets for that address, we have sent a new link. Check your inbox and your spam folder.";
 
     public static string Title(string productName) => $"New ticket link: {productName}";
```

`src/TechStrap.Portal/Routing/PortalRoutes.cs`

```diff
--- a/src/TechStrap.Portal/Routing/PortalRoutes.cs
+++ b/src/TechStrap.Portal/Routing/PortalRoutes.cs
@@ -36,6 +36,9 @@ public static class PortalRoutes
     /// <summary>The query parameter that carries the protected ticket reference to the "received" page.</summary>
     public const string ReceivedReferenceParameter = "ref";
 
+    /// <summary>The query parameters of the contact page that prefill its inputs (<c>?subject=&amp;name=&amp;email=</c>).</summary>
+    public static readonly IReadOnlyList<string> PrefillParameters = ["subject", "name", "email"];
+
     public const string HomeTemplate = "/";
     public const string NotFoundTemplate = "/not-found";
     public const string ErrorTemplate = "/error";
```

`src/TechStrap.Portal/Styles/_components.scss`

```diff
--- a/src/TechStrap.Portal/Styles/_components.scss
+++ b/src/TechStrap.Portal/Styles/_components.scss
@@ -11,7 +11,7 @@
 .ts-portal-main {
   flex: 1;
   width: 100%;
-  max-width: 640px;
+  max-width: var(--ts-wide-width);
   margin: 0 auto;
   padding: 24px 16px 32px;
 }
@@ -85,7 +85,7 @@
 // A product's header and footer (PHASE-09a): the logo and name are one link to the product home; the colors are the product's through --ts-accent*.
 .ts-product-header {
   width: 100%;
-  max-width: 640px;
+  max-width: var(--ts-wide-width);
   margin: 0 auto;
   padding: 16px 16px 0;
 }
@@ -109,7 +109,7 @@
 
 .ts-product-footer {
   width: 100%;
-  max-width: 640px;
+  max-width: var(--ts-wide-width);
   margin: 0 auto;
   padding: 0 16px 16px;
   font-size: .875rem;
@@ -154,7 +154,6 @@
 .ts-field-error {
   margin: 4px 0 0;
   font-size: .875rem;
-  color: var(--p-error, #b3261e);
 }
 
 // The KB suggestions beside the subject field: empty until there is something to suggest, then a short list. Plain text; the links open in a new tab, so the form is never lost.
@@ -204,6 +203,10 @@
   margin: 0;
 }
 
+.ts-status-label {
+  color: var(--p-ink2);
+}
+
 .ts-status-note {
   margin-top: 4px;
   color: var(--p-ink2);
@@ -238,6 +241,10 @@
   }
 }
 
+.ts-message-author {
+  font-weight: 600;
+}
+
 .ts-message-body {
   overflow-wrap: anywhere;
 }
@@ -262,14 +269,14 @@
   padding-left: 20px;
 }
 
-// The help center (PHASE-09c): structure only (lists, the breadcrumb trail, the pager); the visual polish pass is PHASE-09d.
+// The help center: lists of articles and categories (a card each), the breadcrumb trail and the pager. The card grid of the categories is in _layout.scss.
 .ts-kb-list {
   margin: 24px 0;
   padding: 0;
   list-style: none;
 
   li + li {
-    margin-top: 20px;
+    margin-top: 16px;
   }
 
   h2 {
@@ -278,6 +285,24 @@
   }
 }
 
+.ts-kb-card {
+  height: 100%;
+  padding: 16px;
+  background: var(--p-bg);
+  border: 1px solid var(--p-line);
+  border-top: 4px solid var(--ts-accent, var(--p-line));
+  border-radius: $border-radius-lg;
+
+  h2 {
+    margin-top: 0;
+  }
+
+  &:hover,
+  &:focus-within {
+    border-color: var(--ts-accent-ink, var(--p-ink2));
+  }
+}
+
 .ts-kb-summary {
   margin: 0 0 4px;
   overflow-wrap: anywhere;
@@ -318,13 +343,92 @@
   margin: 24px 0;
 }
 
+.ts-pager-prev,
+.ts-pager-next {
+  font-weight: 600;
+}
+
+.ts-pager-state {
+  color: var(--p-ink2);
+}
+
 .ts-state {
   margin: 24px 0;
+  padding: 16px;
+  background: var(--p-soft);
+  border-radius: $border-radius-lg;
+
+  h1,
+  h2 {
+    margin-top: 0;
+  }
 }
 
-// An article's body is the API's sanitized HTML: a wide table, a code block or an image scrolls or shrinks inside its own box instead of widening the page.
+// The article (the reading column is the page's own wrapper): the title, the date and the body.
+.ts-kb-article {
+  margin-bottom: 8px;
+}
+
+// An article's body is the API's sanitized HTML: a wide table, a code block or an image scrolls or shrinks inside its own box instead of widening the page. Its prose keeps a readable measure and rhythm.
 .ts-kb-article-body {
   overflow-wrap: anywhere;
+  line-height: 1.6;
+
+  h2,
+  h3,
+  h4 {
+    margin: 1.5em 0 .5em;
+  }
+
+  h2 {
+    font-size: 1.25rem;
+  }
+
+  h3 {
+    font-size: 1.125rem;
+  }
+
+  ul,
+  ol {
+    padding-left: 1.5rem;
+  }
+
+  blockquote {
+    margin: 1em 0;
+    padding: .5em 1em;
+    color: var(--p-ink2);
+    border-left: 4px solid var(--p-line);
+  }
+
+  code {
+    padding: 0 .25em;
+    font-family: var(--ts-font-mono);
+    font-size: .875em;
+    background: var(--p-soft);
+    border-radius: 3px;
+  }
+
+  pre {
+    padding: 12px 16px;
+    background: var(--p-soft);
+    border: 1px solid var(--p-line);
+    border-radius: $border-radius;
+
+    code {
+      padding: 0;
+      background: none;
+    }
+  }
+
+  table {
+    border-collapse: collapse;
+  }
+
+  th,
+  td {
+    padding: 6px 10px;
+    border: 1px solid var(--p-line);
+  }
 
   img {
     max-width: 100%;
```

`src/TechStrap.Portal/Styles/_tokens.scss`

```diff
--- a/src/TechStrap.Portal/Styles/_tokens.scss
+++ b/src/TechStrap.Portal/Styles/_tokens.scss
@@ -4,6 +4,8 @@
 @import "../../../assets/brand/scss/brand-tokens";
 
 $enable-dark-mode: false;
+// Nothing in the Portal scrolls smoothly (BRAND.md section 17: reduced motion is respected, so there is no motion to opt out of).
+$enable-smooth-scroll: false;
 
 // Compile-time fallback for the product accent. At runtime the product's own accent arrives as --ts-accent,
 // --ts-on-accent and --ts-accent-ink (see _theme.scss) and replaces this everywhere it matters.
```

`src/TechStrap.Portal/Styles/app.scss`

```diff
--- a/src/TechStrap.Portal/Styles/app.scss
+++ b/src/TechStrap.Portal/Styles/app.scss
@@ -6,9 +6,11 @@
 @import "theme";
 @import "fonts";
 @import "components";
+@import "layout";
+@import "a11y";
 
 .shell-placeholder {
-  max-width: 40rem;
+  max-width: var(--ts-reading-width);
   margin: 4rem auto;
   padding: 0 1rem;
 }
```

`src/TechStrap.Portal/Tickets/TicketCopy.cs`

```diff
--- a/src/TechStrap.Portal/Tickets/TicketCopy.cs
+++ b/src/TechStrap.Portal/Tickets/TicketCopy.cs
@@ -23,6 +23,12 @@ public static class TicketCopy
     public const string NoMessages = "There are no messages to show yet.";
     public const string AttachmentsLabel = "Attachments";
 
+    /// <summary>The link at the top of a long conversation that jumps to the reply form.</summary>
+    public const string JumpToReply = "Jump to your reply";
+
+    /// <summary>Required is said in words, not by an asterisk (UX brief).</summary>
+    public const string ReplyNote = "A message is required. Attachments are optional.";
+
     public const string ReplyHeading = "Reply";
     public const string ReplyLabel = "Your reply";
     public const string ReplyButton = "Send reply";
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test --project tests/TechStrap.Portal.Tests -c Release
dotnet test --project tests/TechStrap.Admin.Tests -c Release
```

Expected: 1,805 Portal tests and 1,962 Admin tests pass:

```text
  total: 1805
  failed: 0
  succeeded: 1805
```
```text
  total: 1962
  failed: 0
  succeeded: 1962
```

Then check it in a browser (the spike's checks, kept as `$T/browser_t4.mjs` in the appendix): with the Portal and the stand-in API running, the script posts the empty contact form and click the first summary link with a real mouse click and with Enter: the address becomes `/p/paperplane/contact#email`, the document is the same (a marker set on `window` survives) and `document.activeElement` is `INPUT#email`; on `/p/paperplane/contact?subject=Printer%20jam` press Tab (the skip link has the focus), press Enter (`MAIN#main` has the focus, the address ends `#main`, the subject box still holds "Printer jam"); at 360, 768 and 1280 pixels `document.documentElement.scrollWidth <= window.innerWidth` on the product home, the contact form and the contact form with errors; with forced colors emulated the focused control has a `3px` outline. Stop everything you started.

- [ ] **Step 5: Prove each pin with a recorded mutation**

Save `$T\specs\m4.py`:

```python
"""Mutations of Task 4 (T16: styling, accessibility, the base-address fix). Run from the repository root."""
PORTAL = ["dotnet", "test", "--project", "tests/TechStrap.Portal.Tests", "-c", "Release", "--filter-query"]
ADMIN = ["dotnet", "test", "--project", "tests/TechStrap.Admin.Tests", "-c", "Release", "--filter-query"]
S = "src/TechStrap.Portal/Styles/"
C = "src/TechStrap.Portal/Components/"
LINKS = PORTAL + ["/*/*/PageLinksTests/*"]
LAND = PORTAL + ["/*/*/LayoutLandmarkHostTests/*"]
ERR = PORTAL + ["/*/*/ErrorSummaryLinkHostTests/*"]
HEAD = PORTAL + ["/*/*/HeadingHostTests/*"]
RESP = PORTAL + ["/*/*/ResponsiveStyleTests/*"]
CONTRAST = PORTAL + ["/*/*/TokenContrastTests/*"]
BUILD = PORTAL + ["/*/*/StyleBuildTests/*"]
BODY = PORTAL + ["/*/*/BodyHeadingsTests/*"]
NEUTRAL = PORTAL + ["/*/*/NeutralPagesGuardTests/*"]

MUTATIONS = [
    ("1 the link is a bare fragment again", "src/TechStrap.Portal/Routing/PageLinks.cs", [('return $"{path}{query}#{fragment}";', 'return $"#{fragment}";')], LINKS),
    ("2 the link keeps every query parameter", "src/TechStrap.Portal/Routing/PageLinks.cs", [(".Where(pair => known.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))", ".Where(pair => true)")], LINKS),
    ("3 a category page loses its page parameter", "src/TechStrap.Portal/Routing/PageLinks.cs", [("[var kb, _] when Is(kb, PortalRoutes.KbSegment) => [PortalRoutes.PageParameter],", "[var kb, _] when Is(kb, PortalRoutes.KbSegment) => None,")], LINKS),
    ("4 a kept value is not escaped", "src/TechStrap.Portal/Routing/PageLinks.cs", [("Uri.EscapeDataString(value ?? string.Empty)", "(value ?? string.Empty)")], LINKS),
    ("5 the prefill is dropped from the skip link", "src/TechStrap.Portal/Routing/PageLinks.cs", [("[var contact] when Is(contact, PortalRoutes.ContactSegment) => PortalRoutes.PrefillParameters,", "[var contact] when Is(contact, PortalRoutes.ContactSegment) => None,")], LINKS),
    ("6 the summary links are bare fragments again", C + "Ui/ErrorSummary.razor", [('<a href="@PageLinks.ToFragment(Navigation.Uri, field, keepQuery: false)">', '<a href="#@field">')], ERR),
    ("7 the summary lets Blazor take the click", C + "Ui/ErrorSummary.razor", [(' data-enhance-nav="false">', ">")], ERR),
    ("8 the skip link opts back into enhanced navigation", C + "Layout/PortalLayout.razor", [(' data-enhance-nav="false">@ShellCopy.SkipToMain', ">@ShellCopy.SkipToMain")], LAND),
    ("9 a neutral page gets a skip link", C + "Layout/PortalLayout.razor", [("        @if (Theme is { } header)\n        {\n            @*", "        <a class=\"ts-skip-link\" href=\"@SkipHref\" data-enhance-nav=\"false\">@ShellCopy.SkipToMain</a>\n        @if (Theme is { } header)\n        {\n            @*"), ("            <a class=\"ts-skip-link\" href=\"@SkipHref\" data-enhance-nav=\"false\">@ShellCopy.SkipToMain</a>\n            <ProductHeader", "            <ProductHeader")], PORTAL + ["/*/*/LayoutLandmarkHostTests/*"]),
    ("10 main cannot take focus", C + "Layout/PortalLayout.razor", [('<main id="main" tabindex="-1"', '<main id="main"')], LAND),
    ("11 the footer is a second contentinfo again", C + "Layout/ProductFooter.razor", [('<nav class="ts-product-footer" aria-label="@ShellCopy.FooterNavLabel">', '<footer class="ts-product-footer">'), ("</nav>", "</footer>")], LAND),
    ("12 the header navigation has no name", C + "Layout/ProductHeader.razor", [(' aria-label="@ShellCopy.NavLabel"', "")], LAND),
    ("13 the search page has the home's h1", C + "Pages/KbSearch.razor", [("<h1>@KbCopy.SearchPageHeading</h1>", "<h1>@KbCopy.HomeHeading</h1>")], HEAD),
    ("14 an h1 in an article body stays an h1", "src/TechStrap.Portal/Components/BodyHeadings.cs", [('.Replace("</h1>", "</h2>", StringComparison.Ordinal)', "")], PORTAL + ["/*/*/BodyHeadingsTests/*"]),
    ("15 a message body keeps its h1", C + "Tickets/CustomerMessageBody.razor", [("BodyHeadings.DemoteTitle(Html)", "Html")], HEAD),
    ("16 the ticket has no reply anchor target", C + "Pages/Ticket.razor", [('<h2 id="reply" tabindex="-1" class="h5">', '<h2 class="h5">')], LAND),
    ("17 a form says nothing about what is required", C + "Pages/Contact.razor", [('    <p class="ts-form-note">@ContactCopy.RequiredNote</p>\n', "")], LAND),
    ("18 the skip link is always on screen", S + "_a11y.scss", [("  top: -100px;\n", "  top: 0;\n")], RESP),
    ("19 the pager links lose their 44px target", S + "_a11y.scss", [(".ts-pager a,\n", "")], RESP),
    ("20 the reading width changes", S + "_layout.scss", [("--ts-reading-width: 40rem;", "--ts-reading-width: 50rem;")], RESP),
    ("21 the header goes back to 640px", S + "_components.scss", [("""  width: 100%;
  max-width: var(--ts-wide-width);
  margin: 0 auto;
  padding: 16px 16px 0;""", """  width: 100%;
  max-width: 640px;
  margin: 0 auto;
  padding: 16px 16px 0;""")], RESP),
    ("22 smooth scrolling comes back", S + "_tokens.scss", [("$enable-smooth-scroll: false;", "$enable-smooth-scroll: true;")], RESP),
    ("23 forced colors drop the 3px outline", S + "_a11y.scss", [("    outline: 3px solid Highlight !important;", "    outline: none !important;")], RESP),
    ("24 reduced motion leaves transitions on", S + "_a11y.scss", [("    transition: none !important;\n  }\n}\n", "  }\n}\n")], RESP),
    ("25 the category cards stay one column", S + "_layout.scss", [("    grid-template-columns: repeat(2, minmax(0, 1fr));\n    gap: 16px;", "    gap: 16px;")], RESP),
    ("26 the phone search box stays on one row", S + "_layout.scss", [("    flex-direction: column;\n", "")], RESP),
    ("27 a class of the markup loses its rule", S + "_components.scss", [(".ts-message-author {\n  font-weight: 600;\n}", ".ts-message-author-x {\n  font-weight: 600;\n}")], RESP),
    ("28 a control's edge goes back to the faint line", S + "_a11y.scss", [("  border-color: var(--p-ink2);\n}\n\n.form-text", "  border-color: var(--p-line);\n}\n\n.form-text")], CONTRAST),
    ("29 the error red is too light", "assets/brand/scss/_brand-tokens.scss", [('"p-error": #B3141C,', '"p-error": #FF9AA0,')], CONTRAST),
    ("30 the error token drifts from BRAND.md by one digit", "assets/brand/scss/_brand-tokens.scss", [('"p-error": #B3141C,', '"p-error": #B3141D,')], BUILD),
    ("31 a semantic token is missing from the build", "assets/brand/scss/_brand-tokens.scss", [('  "p-warn-bg": #FFF3E0\n', '  "p-warn-bg-x": #FFF3E0\n')], BUILD),
    ("32 the skip link takes the product accent", S + "_a11y.scss", [("  color: var(--p-ink);\n  text-decoration: underline;", "  color: var(--ts-accent);\n  text-decoration: underline;")], CONTRAST),
    ("33 the product home keeps its own search box", C + "Pages/ProductHome.razor", [('    <KbSearchBox ProductKey="@theme.Key" />\n', '    <form method="get" action="@PortalRoutes.KbSearch(theme.Key)" role="search" class="ts-help-search"><input id="kb-search" name="q" type="search" class="form-control" /></form>\n')], PORTAL + ["/*/*/ProductHomeHostTests/*"]),
    ("34 the style rule uses a url", S + "_a11y.scss", [("  background: var(--p-bg);\n  border: 2px solid var(--p-ink);", "  background: url(https://cdn.example.com/x.png);\n  border: 2px solid var(--p-ink);")], PORTAL + ["/*/*/CspStyleTests/*"]),
    ("35 the Admin palette misses a Portal token", "src/TechStrap.Admin/Components/Showcase/PaletteSwatches.razor.cs", [(', "p-warn-bg"]', "]")], ADMIN + ["/*/*/StyleGuideContentTests/*"]),
]
```

Run them (in three foreground batches, `1`..`12`, `13`..`26` and `27`..`35`, about a minute each) and record the results:

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | the link is a bare fragment again | `PageLinks.cs` | KILLED (19 failing) |
| 2 | the link keeps every query parameter | `PageLinks.cs` | KILLED (10 failing) |
| 3 | a category page loses its page parameter | `PageLinks.cs` | KILLED (1 failing) |
| 4 | a kept value is not escaped | `PageLinks.cs` | KILLED (3 failing) |
| 5 | the prefill is dropped from the skip link | `PageLinks.cs` | KILLED (3 failing) |
| 6 | the summary links are bare fragments again | `ErrorSummary.razor` | KILLED (4 failing) |
| 7 | the summary lets Blazor take the click | `ErrorSummary.razor` | KILLED (4 failing) |
| 8 | the skip link opts back into enhanced navigation | `PortalLayout.razor` | KILLED (7 failing) |
| 9 | a neutral page gets a skip link | `PortalLayout.razor` | KILLED (3 failing) |
| 10 | main cannot take focus | `PortalLayout.razor` | KILLED (10 failing) |
| 11 | the footer is a second contentinfo again | `ProductFooter.razor` | KILLED (7 failing) |
| 12 | the header navigation has no name | `ProductHeader.razor` | KILLED (7 failing) |
| 13 | the search page has the home's h1 | `KbSearch.razor` | KILLED (3 failing) |
| 14 | an h1 in an article body stays an h1 | `BodyHeadings.cs` | KILLED (2 failing) |
| 15 | a message body keeps its h1 | `CustomerMessageBody.razor` | KILLED (2 failing) |
| 16 | the ticket has no reply anchor target | `Ticket.razor` | KILLED (1 failing) |
| 17 | a form says nothing about what is required | `Contact.razor` | KILLED (1 failing) |
| 18 | the skip link is always on screen | `_a11y.scss` | KILLED (1 failing) |
| 19 | the pager links lose their 44px target | `_a11y.scss` | KILLED (1 failing) |
| 20 | the reading width changes | `_layout.scss` | KILLED (1 failing) |
| 21 | the header goes back to 640px | `_components.scss` | KILLED (1 failing) |
| 22 | smooth scrolling comes back | `_tokens.scss` | KILLED (1 failing) |
| 23 | forced colors drop the 3px outline | `_a11y.scss` | KILLED (1 failing) |
| 24 | reduced motion leaves transitions on | `_a11y.scss` | KILLED (1 failing) |
| 25 | the category cards stay one column | `_layout.scss` | KILLED (1 failing) |
| 26 | the phone search box stays on one row | `_layout.scss` | KILLED (1 failing) |
| 27 | a class of the markup loses its rule | `_components.scss` | KILLED (1 failing) |
| 28 | a control's edge goes back to the faint line | `_a11y.scss` | KILLED (1 failing) |
| 29 | the error red is too light | `_brand-tokens.scss` | KILLED (2 failing) |
| 30 | the error token drifts from BRAND.md by one digit | `_brand-tokens.scss` | KILLED (1 failing) |
| 31 | a semantic token is missing from the build | `_brand-tokens.scss` | KILLED (1 failing) |
| 32 | the skip link takes the product accent | `_a11y.scss` | KILLED (1 failing) |
| 33 | the product home keeps its own search box | `ProductHome.razor` | KILLED (2 failing) |
| 34 | the style rule uses a url | `_a11y.scss` | KILLED (2 failing) |
| 35 | the Admin palette misses a Portal token | `PaletteSwatches.razor.cs` | KILLED (1 failing) |

Every mutation is killed; there are no survivors in this task. (Mutation 30 first ran under the contrast tests, which read BRAND.md for both sides and so could not see the drift; the right pin is `StyleBuildTests`, which compares the compiled CSS with BRAND.md, and it kills it.)

- [ ] **Step 6: Run the full suites and commit**

```bash
dotnet build TechStrap.slnx -c Release
dotnet test --project tests/TechStrap.Portal.Tests -c Release
dotnet test --project tests/TechStrap.Admin.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: 0 warnings; 1,805, 1,962 and the architecture suite pass.

```bash
git add -A
git diff --cached --stat
git commit -m "feat: PHASE-09d portal styling and accessibility, base-address-safe links, landmarks (D-045)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

### Task 5: Close-out: the developer guide, the PHASE-09 ticks, the roadmap rows, the D-045 as-built notes and the owner's checklist

**Review Focus pin:** 4 (the owner's browser, keyboard and screen-reader checks that no test can make are written down, numbered, in the form of ADMIN-APP.md's checklist) and 5 (the docs say what is true: T09 is ticked with its evidence, T16 says "Owner evidence pending", T18 and T20 stay open).

**Files:**

- Modify: `docs/development/PORTAL-APP.md`
- Modify: `docs/architecture/04-DECISION-LOG.md` (the 09d as-built notes; the 09b and 09c "Known" bullets)
- Modify: `docs/architecture/PHASE-09-public-portal.md` (T09 ticked, T16's note, the success criteria)
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`, `docs/architecture/00-DISCOVERY-INDEX.md`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`

**Interfaces:**
- Consumes: everything Tasks 1 to 4 produced (the names in the guide are checked against the code by the Pester block); `PHASE-09-public-portal.md`'s current ticks; the roadmap and discovery rows (`09c complete (pending merge); 09d not started`, stale since 09c merged as #16).
- Produces: the guide sections "The double-send guard", "Form helpers (`portal-forms.js`)", "Accessibility, layout and the base address" and "Manual checks (owner, before merging 09d)"; the D-045 "As built in 09d" and "Known in 09d" bullets; PHASE-09 with T09 ticked (T16, T18, T20 open), the success criteria the tests prove ticked, and the two rows reading "09a merged (PR #14); 09b merged (PR #15); 09c merged (PR #16); PHASE-09 complete (pending merge): ..."; the Pester pins for all of it.

- [ ] **Step 1: Write the failing Pester block**

The block changes in place: the ticks test now expects T09 ticked and T16, T18 and T20 open (T16 with "Owner evidence pending"), the two rows now read the new status, the 09c known-gap pin follows the merged search box, and a new `D-045 as built in 09d` block pins the as-built list, the guide's content and headings, the six tokens in BRAND.md and the ticks.

```bash
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected: FAIL until the documents are written (Step 2): six tests fail.

```text
[-] D-045 (the public portal).ticks only the tasks and deliverables 09a to 09d fully deliver, and the roadmap and discovery rows say PHASE-09 is complete, pending merge 30ms
[-] D-045 (the public portal).has a Portal developer guide, linked from the README, that lists every setting and the known gaps 17ms
[-] D-045 as built in 09d.records what the 09d build found, as consequences of the addendum 5ms
[-] D-045 as built in 09d.has a developer guide that describes the guard, the helpers, the base-address rule and the owner checklist 5ms
[-] D-045 as built in 09d.ticks T09 and the success criteria the tests prove, and leaves the owner evidence open 4ms
[-] D-045 as built in 09c.records the lower-case-only cache rule, the search-box merge and the article-only JSON-LD in the right places 5ms
Tests Passed: 41, Failed: 6, Skipped: 0, Inconclusive: 0, NotRun: 0
```


- [ ] **Step 2: Write the documentation**

`docs/architecture/00-DISCOVERY-INDEX.md`

```diff
--- a/docs/architecture/00-DISCOVERY-INDEX.md
+++ b/docs/architecture/00-DISCOVERY-INDEX.md
@@ -30,7 +30,7 @@
 | 06 | [Ticket operations](PHASE-06-ticket-operations.md) | 05 | 07, 08, 09 | Complete |
 | 07 | [Admin app](PHASE-07-admin-app.md) | 02, 06 | 08 (editor UI), 10 | 07 complete (pending merge): 07a merged (PR #9), 07b merged (PR #10), 07c implemented; owner action 7 (Authentik) still open, so P07-T02 stays unticked |
 | 08 | [Knowledge base](PHASE-08-knowledge-base.md) | 06 (07 for the editor UI) | 09 | PHASE-08 complete (pending merge): the API (Tasks 1-8) and the Admin (editor, categories, article picker) are implemented; the owner's manual checks are open |
-| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 09a merged (PR #14); 09b merged (PR #15); 09c complete (pending merge); 09d not started |
+| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 09a merged (PR #14); 09b merged (PR #15); 09c merged (PR #16); PHASE-09 complete (pending merge): 09d implemented; the owner evidence for P09-T16 (axe, Lighthouse, the JavaScript-off walk, screenshots) and the compose run of P09-T18 are open, P09-T20 is deferred |
 | 10 | [Live updates](PHASE-10-live-updates.md) | 07 | 12 | Not started |
 | 11 | [Client SDK](PHASE-11-client-sdk.md) | 05 | 12 | Not started |
 | 12 | [Release hardening](PHASE-12-release-hardening.md) | all | v1.0.0 | Not started |
```

`docs/architecture/04-DECISION-LOG.md`

```diff
--- a/docs/architecture/04-DECISION-LOG.md
+++ b/docs/architecture/04-DECISION-LOG.md
@@ -1724,7 +1724,7 @@ The owner's rulings for PHASE-09b, and what the plan's spike proved. They extend
 - **As built in 09b: the suggest adapter** is `GET /p/{key}/suggest`, a minimal-API endpoint in `Suggestions/` (it calls `IPublicKbClient`, so `PortalRules` allows it outside `Clients/`), and the element is `wwwroot/js/kb-suggestions.js`, tested with `node --test` through `scripts/tests/PortalScripts.Tests.ps1`.
 - **As built in 09b: the smoke** (`scripts/Test-ComposeSmoke.ps1`) starts the Portal, checks its `/health/ready` and proves the real client address through `/p/smoke/suggest`: its override lowers the Api's public limit to 3 and makes the Portal trust the compose subnet, and the calls are made from inside the network (`docker compose exec api curl`), because a call from the host arrives from the Docker gateway, whose address differs between Linux and Docker Desktop. It never runs `down -v`.
 - **As built in 09b: redaction.** The `subject` and `ref` query values are masked in Serilog and in Sentry, and `q` in Serilog (Hosting, shared by every host: masking those parameter names in the Api's and the Admin's logs is accepted).
-- **Known in 09b: no copy button, counter or sending state.** The ticket number has no copy affordance, the message has no character counter and the submit button has no in-flight state, because each needs script and every 09b flow is script-free; a double click can send twice. The 09c polish pass decides.
+- **Known in 09b: no copy button, counter or sending state.** The ticket number has no copy affordance, the message has no character counter and the submit button has no in-flight state, because each needs script and every 09b flow is script-free; a double click can send twice. The 09c polish pass decides. **Resolved in 09d:** `portal-forms.js` adds the three, and the one-time form id and `SubmitGuard` make a double click send once (see the 09d addendum).
 - **Known in 09b: `TicketToken.Value` is internal** and read in two places only (`ApiConnection` for the header and `PortalRoutes` for a link).
 - **Known in 09b: `ProductKey` sits in the middle of the positional `CustomerTicketDto`** (after `Number`, before `Subject`). Anything that builds or deconstructs that record by position (the PHASE-11 SDK, a generated client) must follow the new order; a JSON reader by name is not affected.
 
@@ -1764,7 +1764,7 @@ The owner's rulings for PHASE-09c, and what the plan's spike proved. They extend
 - **As built in 09c: layering and plain text.** `KbPaging` and `KbSearchText` live in `TechStrap.Portal.Kb`, not in the Components layer. `KbPlainText` patterns have a 100 ms match timeout (a body that exceeds it has no description). A `>` inside a quoted attribute value can leave stray words in the description; accepted as cosmetic, because the text is encoded and never markup and a real HTML parser (not a Portal package) would be needed.
 - **Known in 09c: the sitemap build's calls carry the first crawler's address.** The build's `X-Forwarded-For` is the address of the visitor whose request started it, so each sitemap build makes 1 + N API calls under one forwarded IP, and the API's public limit is 120 per minute per IP: with about 120 or more active products the sitemap build is rate-limited and the sitemap goes stale or becomes unavailable. Accepted for the products this install has; a bulk sitemap endpoint, or exempting the Portal's own build traffic from the limit, is the possible later fix.
 - **Known in 09c: a plain-http image in an article is blocked** by `img-src 'self' https: data:` in Production. That is the right posture and is documented, not changed.
-- **Known in 09c: the category page shows no description** (the article list does not carry it, and a second call per page was not worth it), the product home still has no category list, and the visual polish, the no-JS and accessibility pass, the double-send guard and the JavaScript niceties of 09b's known gaps are PHASE-09d.
+- **Known in 09c: the category page shows no description** (the article list does not carry it, and a second call per page was not worth it), the product home still has no category list, and the visual polish, the no-JS and accessibility pass, the double-send guard and the JavaScript niceties of 09b's known gaps were PHASE-09d (done there; the category description and the product home's category list are still open).
 - **Known in 09c: the package's `JsonLd` is still unsafe for any other caller.** The Portal avoids it for strings; the problem (a `</script>` in a value ends the block) is to be reported upstream to `SyntaxCircus.Blazor.Seo`.
 - **Resolved in 09c: the product pages no longer link ahead.** The search box, the KB links on the contact and received pages and the sitemap named by robots.txt all answer.
 
@@ -1809,6 +1809,16 @@ The owner's rulings for PHASE-09d, and what the plan's spike proved. They extend
 - Required is said in one sentence above each form, not in each label, so the pinned label markup does not change.
 - The product home still has no category list (the header's help-center link is the way in).
 
+**Consequences of the addendum (as built in 09d)**
+- **As built in 09d: the double-send guard.** `SubmitIds` (the id), `SubmitKey` (the hash of the form, the subject and the id), `SubmitGuard` (the cache, the lock, the claims) and `FormGuard` (the hidden input) are in `Forms/` and `Components/Ui/`; the three pages call `Guard.RunAsync(key, fallback, write, requestAborted)`, where `write` is the API call mapped to a redirect target (`Result<SubmitTarget>`). The guard's lifetime (2 minutes) is a constant pinned below `ReceivedReference.Lifetime`; `SubmitGuardTests` pins every rule above with a gate that holds the write, and `DoubleSendHostTests` pins the three forms, including the aborted first post, the unknown answer, the failure that releases the claim, the id that cannot cross forms, products or tickets, and a log scan at every level.
+- **As built in 09d: the form helpers.** `portal-forms.js` and its two custom elements; both modules are loaded from `App.razor`; the contact page no longer carries a script, which also fixes the suggestions for a visitor who arrived by an enhanced navigation (a 09b defect found by the spike). The element names and the data attributes are pinned by `PortalFormsHostTests`.
+- **As built in 09d: links to the current page.** `PageLinks.ToFragment` is the only way the Portal links to a place on the current page; `PageKit.Resolve` in the tests resolves a link against the base as a browser does, and a control test proves that the old bare fragment would have been caught. The ticket page's links to itself contain the access token in the path (`href="/t/{token}#main"`), the same address the visitor is on; `TicketPageHostTests` pins exactly four places the token appears.
+- **As built in 09d: styles.** `_layout.scss` and `_a11y.scss` after `_components.scss`; six new Portal tokens in BRAND.md and `_brand-tokens.scss` (the Admin's style guide palette lists them and its token-count test is 11); `$enable-smooth-scroll: false`. `ResponsiveStyleTests` scans every `.razor`, `.cs` and `.js` file of the Portal for `ts-*` classes and fails on one with no rule.
+- **As built in 09d: test hardening.** `tests/Shared/StartupFailure.cs` and `tests/Shared/CollectingSink.cs` are linked by the Admin, Api and Portal test projects (the Portal's own copies are gone); `AdminFactory` gains a `LogSink`; the twelve sites keep their original assertions about the key or the message, and the ones that asserted on `ToString()` now assert on the captured `OptionsValidationException`'s message. Not converted, because their failure does not come from `ValidateOnStart` or does not race: the four `ConfigHostSupport` hosts, `ApiPublicUrlTests`, `TrustedProxyStartupTests` and the Infrastructure tests (they resolve `IOptions<T>` and do not start a host). `Seen` compares every header except `Date`, `X-Correlation-Id`, `Content-Length`, `Transfer-Encoding`, `Set-Cookie` and `Pragma`; `SeenTests` pin both halves.
+- **Known in 09d: the guard is per instance** and is lost on a restart; with two Portal replicas a double click that reaches both is not caught.
+- **Known in 09d: the owner's evidence for P09-T16 is open**: the axe run, the Lighthouse score, the JavaScript-off walk and the screenshots (PORTAL-APP.md, "Manual checks"), and the owner's compose run of the smoke closes P09-T18. P09-T20 stays deferred.
+- **Known in 09d: an `h1` in a body is shown as an `h2`**, so an author's top heading is not the biggest heading on the page; an editor who wants a real section starts at `##`.
+
 ### Approval
 - **Approved by:** Jon Seeley (owner, PHASE-09 planning)
 - **Approved on:** 2026-10-05
```

`docs/architecture/99-IMPLEMENTATION-ROADMAP.md`

```diff
--- a/docs/architecture/99-IMPLEMENTATION-ROADMAP.md
+++ b/docs/architecture/99-IMPLEMENTATION-ROADMAP.md
@@ -25,7 +25,7 @@ Cross-cutting conventions every phase follows (fixed during the consistency revi
 | 06 | [Ticket operations](PHASE-06-ticket-operations.md) | 05 | 07, 08, 09 | Alongside 02 and 11 | D-006, D-008, D-009, D-022, D-035, D-036, D-037, D-038, D-039 | Complete (PRs #6, #7 and #8 merged) |
 | 07 | [Admin app](PHASE-07-admin-app.md) | 02, 06 | 08 (editor UI), 10 | 11 alongside; 08 API work alongside | D-017, D-022, D-040, D-041 | 07 complete (pending merge): 07a merged (PR #9), 07b merged (PR #10), 07c implemented; owner action 7 (Authentik) still open, so P07-T02 stays unticked |
 | 08 | [Knowledge base](PHASE-08-knowledge-base.md) | 06 (07 for the editor UI) | 09 | API tasks T01 to T12 alongside 07; editor tasks wait for 07 | D-011, D-014, D-021, D-044 | PHASE-08 complete (pending merge): the API (Tasks 1-8) and the Admin (editor, categories, article picker) are implemented; the owner's manual checks are open |
-| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 10 and 11 alongside | D-002, D-017, D-019, D-045 | 09a merged (PR #14); 09b merged (PR #15); 09c complete (pending merge); 09d not started |
+| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 10 and 11 alongside | D-002, D-017, D-019, D-045 | 09a merged (PR #14); 09b merged (PR #15); 09c merged (PR #16); PHASE-09 complete (pending merge): 09d implemented; the owner evidence for P09-T16 (axe, Lighthouse, the JavaScript-off walk, screenshots) and the compose run of P09-T18 are open, P09-T20 is deferred |
 | 10 | [Live updates](PHASE-10-live-updates.md) | 07 | 12 | 08, 09, 11 alongside | D-007, D-018 | Not started |
 | 11 | [Client SDK](PHASE-11-client-sdk.md) | 05 | 12 | Alongside 06 to 10 | D-005, D-020 | Not started |
 | 12 | [Release hardening](PHASE-12-release-hardening.md) | all | v1.0.0 | Last; security, load, restore and UAT tasks can overlap once their inputs exist | D-003, D-022 | Not started |
```

`docs/architecture/PHASE-09-public-portal.md`

```diff
--- a/docs/architecture/PHASE-09-public-portal.md
+++ b/docs/architecture/PHASE-09-public-portal.md
@@ -182,10 +182,11 @@ Not used: `Blazor.Auth` (portal is anonymous), `Blazor.Tracking` (Not applicable
   - **Depends on:** P09-T03, P06
   - **Validation:** Host test: valid token renders public messages only; invalid/expired/revoked all return the identical 404 body; response headers asserted; presenter unit test for Closed state.
   - **09b evidence:** `TicketPageHostTests`, `TicketUniformNotFoundHostTests`, `CustomerTicketPresenterTests`, `TicketHeaderHostTests`; `CustomerMessageBody` is the one markup site (`PortalRuleTests`).
-- [ ] **P09-T09** Build `CustomerReplyForm` with attachments, including Closed -> follow-up flow handling
+- [x] **P09-T09** Build `CustomerReplyForm` with attachments, including Closed -> follow-up flow handling
   - **Depends on:** P09-T08
   - **Validation:** Host test: reply on Open ticket refreshes thread; reply on Closed ticket shows follow-up ticket link; oversize/disallowed attachment shows error; double-submit guarded.
   - **09b:** delivered except double-submit (deferred to 09d, D-045 'Known in 09b').
+  - **09d evidence:** the double-send guard: `SubmitGuardTests` and `DoubleSendHostTests` (the same id twice, in a row and at the same time, makes one API call and the same redirect; an aborted first post; an unknown answer goes to the fallback; a 429, 409 or 503 releases the claim; an id cannot cross forms, products or tickets; no token, reference or id in a log), and the sending state in `portal-forms.js` (`portal-forms.test.mjs`).
   - **09b evidence:** `TicketReplyHostTests`, `FollowUpLinkTests`, `ReplyAndEmailRulesTests`: a reply redirects to the same page, a reply on a Closed ticket redirects to the follow-up's own page on this site, a link that cannot be read gives a generic confirmation.
 - [x] **P09-T10** Build `LostLinkPage` (`/p/{key}/lost-link`)
   - **Depends on:** P09-T03
@@ -214,6 +215,7 @@ Not used: `Blazor.Auth` (portal is anonymous), `Blazor.Tracking` (Not applicable
 - [ ] **P09-T16** Apply BRAND.md/UX-BRIEF-portal styling: responsive layout, error summaries, focus states, themed accent usage, no-JS verification
   - **Depends on:** P09-T06, P09-T08, P09-T14
   - **Validation:** UX-BRIEF-portal checklist completed; manual run with JavaScript disabled covers contact -> submitted and ticket view -> reply; axe run has no critical findings; Lighthouse accessibility >= 90 (**Assumption**).
+  - **09d:** the automated part is delivered (`ResponsiveStyleTests`, `TokenContrastTests`, `CspStyleTests`, `HeadingHostTests`, `LayoutLandmarkHostTests`, `ErrorSummaryLinkHostTests`, `PageLinksTests`; the checks in a real browser are in D-045 09d). **Owner evidence pending:** the axe run, the Lighthouse score, the JavaScript-off walk and the screenshots at 360, 768 and 1280 are the owner's checklist in PORTAL-APP.md ("Manual checks"); the task is ticked in the pull request's checklist once they are recorded.
 - [x] **P09-T17** Redact `/t/{token}` and `X-` token headers in portal request logs; add log-redaction test
   - **Depends on:** P09-T08
   - **Validation:** Test host captures Serilog output for a token request and asserts the token string never appears.
@@ -245,13 +247,13 @@ Not used: `Blazor.Auth` (portal is anonymous), `Blazor.Tracking` (Not applicable
 
 ## Success Criteria
 
-- [ ] A customer can open `/p/{key}/contact`, see matching KB suggestions as they type, submit with attachments, and see a confirmation with the ticket number; the form also works with JavaScript disabled (without suggestions).
-- [ ] Following the emailed `/t/{token}` link shows the public conversation; the customer can reply; replying on a Closed ticket creates and links a follow-up ticket.
-- [ ] Invalid, expired and revoked tokens are indistinguishable (identical 404); lost-link responses are identical for known and unknown emails.
-- [ ] Each product's portal pages use its name, logo and accent colour, including readable contrast; an unknown product key shows NotFound.
+- [x] A customer can open `/p/{key}/contact`, see matching KB suggestions as they type, submit with attachments, and see a confirmation with the ticket number; the form also works with JavaScript disabled (without suggestions).
+- [x] Following the emailed `/t/{token}` link shows the public conversation; the customer can reply; replying on a Closed ticket creates and links a follow-up ticket.
+- [x] Invalid, expired and revoked tokens are indistinguishable (identical 404); lost-link responses are identical for known and unknown emails.
+- [x] Each product's portal pages use its name, logo and accent colour, including readable contrast; an unknown product key shows NotFound.
 - [x] KB pages are browsable, searchable, SEO-tagged with sitemap/robots as specified; ticket pages are `noindex`/disallowed.
-- [ ] The contact URL prefills `subject`, `name` and `email` (visible, editable, validated like typed input); "Powered by TechStrap" links to the GitHub repo and disappears when `TECHSTRAP_PORTAL_SHOW_POWERED_BY=false`; agents appear as the resolved public name (D-024).
-- [ ] Portal request logs contain no access tokens; token pages send `no-store` and `no-referrer`.
+- [x] The contact URL prefills `subject`, `name` and `email` (visible, editable, validated like typed input); "Powered by TechStrap" links to the GitHub repo and disappears when `TECHSTRAP_PORTAL_SHOW_POWERED_BY=false`; agents appear as the resolved public name (D-024).
+- [x] Portal request logs contain no access tokens; token pages send `no-store` and `no-referrer`.
 - [ ] Rate limits observe the real client IP through the portal.
 - [ ] `dotnet build`/`dotnet test` green; portal container healthy under compose.
 
```

`docs/development/PORTAL-APP.md`

````diff
--- a/docs/development/PORTAL-APP.md
+++ b/docs/development/PORTAL-APP.md
@@ -2,12 +2,14 @@
 
 `TechStrap.Portal` is the public site customers use: a product's own help and support pages, with the product's name, logo and accent. It never touches the database: every page asks the TechStrap API,
 anonymously. This page is for people who run it and people who extend it. How agents work the tickets customers send is in [ADMIN-APP.md](ADMIN-APP.md); the decisions behind the Portal are D-045 in the
-[decision log](../architecture/04-DECISION-LOG.md) (with its 2026-10-06 addenda for 09b and 09c) and the spec is [PHASE-09](../architecture/PHASE-09-public-portal.md).
+[decision log](../architecture/04-DECISION-LOG.md) (with its 2026-10-06 addenda for 09b, 09c and 09d) and the spec is [PHASE-09](../architecture/PHASE-09-public-portal.md).
 
 PHASE-09 is delivered in four pull requests. **09a** is the foundation: the settings, the API client, the per-product theme and shell, the product home, the root page, the ticket-page headers, robots.txt, log
 redaction and the architecture rules. **09b** adds the customer flows: the contact form with its suggestions and received page, the ticket page with replies and the Closed follow-up, the attachment pass-through
 and the lost-link page. **09c** (this page describes all three) adds the help center: the knowledge base home, category, search and article pages, the SEO head and structured data, output caching and the sitemap.
-**09d** is the polish pass (styling, accessibility, the no-JS check, the double-send guard and the copy button, counter and sending state). The routes of all of them are in `PortalRoutes`.
+**09d** is the polish pass: the double-send guard, the form helpers (the sending state, the copy button and the counter), the styling, the accessibility and the no-JS pass, and the hardening of the start-failure and
+uniform-404 tests. PHASE-09 is complete pending merge: what is still the owner's (the axe run, Lighthouse, the JavaScript-off walk, the screenshots and the compose smoke) is the checklist under
+[Manual checks](#manual-checks-owner-before-merging-09d). The routes of all of them are in `PortalRoutes`.
 
 ## Run it locally
 
@@ -77,16 +79,73 @@ nothing needs script and a refresh never sends twice.
   exactly like typed text. The form posts to the page's own address without the query. The `name`, `email`, `subject`, `ref` and `q` query values are masked in logs and Sentry.
 - **The received page** (`/p/{key}/contact/received?ref=`) shows the ticket number from a data-protected, 10-minute reference (`ReceivedReference`, purpose `TechStrap.Portal.ContactReceived.v1`). A missing, expired, tampered or
   foreign reference shows the generic confirmation, never an error. It carries the number only.
+- **A required field is said in words.** One sentence above each form says what is required (`ContactCopy.RequiredNote`, `LostLinkCopy.RequiredNote`, `TicketCopy.ReplyNote`); the labels stay as they are.
+
+### The double-send guard
+
+A double click on a form must not send twice (P09-T09). Each of the three forms renders `<FormGuard Name="Form.SubmitId" />` (`Reply.SubmitId` on the ticket page) right after the antiforgery field: a hidden input with a
+fresh 22-character random `SubmitId` (`SubmitIds.New`: 128 bits from `RandomNumberGenerator`), made anew on every render, so a form shown again after an error carries a new id, not the posted one. The handler
+claims the id with the singleton `SubmitGuard` after validation passes and before the API call.
+
+- **What a repeat does.** The first post with an id claims it under a lock and does the write; a repeat of the id never writes. It waits for the first's answer when that is still running or reads the stored one, and redirects
+  to the same place (a reply: the same ticket page, or the follow-up's page; contact: the received page with its reference; lost link: the sent page).
+- **The claimed write runs on its own token** with a 30-second timeout, never on `RequestAborted`: a real double click makes the browser abort the first POST while the API may already have the ticket. The first
+  request waits for its own write without its abort token, so the files it is reading stay valid (a precaution, not reproduced).
+- **Unknown is not retried.** A write that times out or throws leaves the claim as unknown: a repeat goes to the fallback (a reply: the ticket page; contact: the received page with no reference, which shows its
+  generic confirmation; lost link: the sent page) and never sends. The first request shows the calm "unavailable" notice with a 503.
+- **A failure releases the claim**, so a retry sends (429, 409, 503 and anything else); a repeat that was waiting gets the same failure and does not write.
+- **A missing or malformed id means no guard**: the post goes through as it did before (old pages and the test kits keep working), and a full cache leaves a new post unguarded rather than refused.
+- **Scope and secrecy.** The key is a hash of the form name, the product key (or the ticket's access token) and the id, so an id cannot be used on another form, product or ticket, and the cache never holds a token.
+  `SubmitGuard` has its own `MemoryCache` with a cap of 10,000 claims (the shared `IMemoryCache` holds the sitemap and has no size), a claim lives 2 minutes (shorter than `ReceivedReference.Lifetime`), the stored
+  target is memory-only and the class takes no logger. For a follow-up reply the target holds the new ticket's access token; that is accepted for the 2 minutes.
+- **Limits.** The guard is per instance and is lost on a restart (like the sitemap cache), and the API still has no idempotency key for `/api/customer` or the public ticket route. The tests are
+  `SubmitGuardTests` (the class on its own, with a gate that holds the write) and `DoubleSendHostTests` (the three forms through the host).
 
 ### Suggestions beside the subject
 
-The contact page puts `<ts-kb-suggestions field="subject" src="/p/{key}/suggest">` after the subject field, with a plain link to the help search inside it (shown only without script), and loads
-`wwwroot/js/kb-suggestions.js` as `<script type="module" src=...>` (no CSP change). The module defines the element: a 300 ms debounce, at least 3 characters, one request at a time (a newer one aborts the older
+The contact page puts `<ts-kb-suggestions field="subject" src="/p/{key}/suggest">` after the subject field, with a plain link to the help search inside it (shown only without script). The document shell (`App.razor`)
+loads `wwwroot/js/kb-suggestions.js` as `<script type="module" src=...>` (no CSP change), not the page: Blazor's enhanced navigation does not run a script that arrives with swapped content, so a script in the contact page's own
+body never ran for a visitor who got there by a click (09d). The module defines the element: a 300 ms debounce, at least 3 characters, one request at a time (a newer one aborts the older
 and its answer is dropped), results written with `textContent` and links built only from root-relative paths, hide-on-error, and clean-up in `disconnectedCallback`. It never parses text as markup, and a test fails
 if the file ever contains `innerHTML`. `GET /p/{key}/suggest?q=` (`SuggestEndpoint`) is the Portal-hosted adapter: it asks `IPublicKbClient.SearchAsync` (a read, forwarding the visitor's address), cuts the text at 200
 characters, returns at most 5 `{title, snippet, href}` items with the links built by `PortalRoutes.KbArticle`, passes the API's 429 through and answers an empty list for a blank text or any other failure.
 The module is tested with `node --test` (`tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs`, run by `scripts/tests/PortalScripts.Tests.ps1`, skipped without node).
 
+### Form helpers (`portal-forms.js`)
+
+`wwwroot/js/portal-forms.js` is the second module the document shell loads (once). Every page works without it. It defines two custom elements and two document-level listeners, so it works on whatever
+Blazor's enhanced navigation swaps in (a listener on the document survives; the browser calls `connectedCallback` for an inserted element; a module in a page body would not run). It keeps its state in a `WeakMap`,
+never in an attribute of the markup, because the swap rewrites the attributes of an element it keeps.
+
+- **The sending state.** A form with `data-sending-label` (`FormCopy.Sending`, "Sending" and an ellipsis written as `\u2026`) gets its submit button disabled and relabeled when it is submitted, so a double click cannot post twice
+  (the server's one-time id is the real guard); a second submit of a form that is sending is cancelled; the button comes back on `pageshow` after the back button and after a minute (a post the visitor stopped).
+- **`<ts-copy-text target="ticket-number" data-label data-copied data-failed>`** on the received page renders a button that copies the number to the clipboard; when the browser refuses it selects the number, so Ctrl+C
+  works, and says so. Without script the number is still there to select (`.ts-ticket-number` is `user-select: all`).
+- **`<ts-char-count for="body" data-limit data-template data-over>`** under the long text fields shows "{0} of {1} characters used" from 80 percent of the limit, and how far over it is, bold and underlined, above it. A line
+  break counts as two characters, because the browser posts CR LF and the server counts what it receives, while the textarea's own `maxlength` counts one. The screen reader hears the count once typing pauses.
+- **Words and safety.** Every word comes from a `*Copy` constant through a data attribute; text goes on the page with `textContent` only (a node test fails if the file contains `innerHTML`, `eval`, a request or
+  a non-ASCII character); there is no inline script and no `on*` attribute, so the CSP is unchanged.
+- **Tests.** `tests/TechStrap.Portal.Tests/js/portal-forms.test.mjs` (run by `PortalScripts.Tests.ps1`) and `PortalFormsHostTests` (the shell loads both modules once and no page body has a script, the markup and the words).
+
+### Accessibility, layout and the base address
+
+- **Widths.** One token for the reading column (`--ts-reading-width`, 40rem: a form, the conversation, an article, the confirmation; the page puts its content in `.ts-reading`) and one for the wide container
+  (`--ts-wide-width`, 64rem: the header, the footer, search, the categories, the product home). One column below 768px; the category cards are two across from 768px and three from 1200px.
+- **Colours.** `--p-error`, `--p-success` and `--p-warn` and their grounds (BRAND.md, "Portal tokens") carry errors, confirmations and notices; a state is never colour alone (the summary has a heading and a list, a field in
+  error has text and a 2px border, the counter over its limit is bold and underlined). A control's edge is `--p-ink2`, not the decorative `--p-line`. Focus is a 3px ink ring with an accent halo; in forced colours the ring
+  stays and the halo is dropped; reduced motion switches every animation and transition off, and smooth scrolling is off in the build (`$enable-smooth-scroll: false`).
+- **Landmarks and headings.** A product page starts with a skip link (`.ts-skip-link`, off the screen until it has focus, in ink on the page so no accent can hide it) and `main` is `id="main" tabindex="-1"`. The header holds a
+  named `nav` (the product, the help centre and contact) and the footer a named `nav`; the "Powered by" footer is the one contentinfo. A neutral page (the root, not-found, error) has none of the product frame, so every
+  neutral 404 is still the same bytes. Every page has exactly one `h1` (`HeadingHostTests`); the search page's is "Search the help center"; an `h1` inside an article or a message body, which the API's sanitiser allows, is
+  shown as an `h2` (`BodyHeadings`, the only change the Portal makes to those bodies).
+- **The `<base href="/">` rule.** The document's `base` is `/` (the stylesheet, the favicon and `blazor.web.js` are relative to it, and Blazor reads it), so a bare `href="#email"` is the home page. A link to a place on the
+  current page is written by `PageLinks.ToFragment` as the root-relative path plus the fragment (the error summary's field links, the skip link and the "jump to your reply" link on the ticket page), which the browser
+  treats as a jump, and carries `data-enhance-nav="false"`: Blazor takes a click on an in-page link, scrolls and leaves the focus on the link, while the browser moves the focus to the field. The query string is kept only for
+  the parameters the page itself reads (the contact prefill, `ref`, `sent`, `q` and `page`), so a jump is not a reload that loses what the page shows, and an extra parameter is neither echoed nor kept in a copy the output
+  cache stores. `ErrorSummaryLinkHostTests` and `LayoutLandmarkHostTests` resolve each link against the base the way a browser does.
+- **Styles** are three partials after `_components`: `_layout.scss` (the widths and the breakpoints) and `_a11y.scss` (the semantic colors, the targets of at least 44px, forced colors, reduced motion). Every `ts-*` class the
+  markup uses has a rule (`ResponsiveStyleTests` scans the markup); `TokenContrastTests` checks every text pair of the Portal's tokens against WCAG AA in the compiled CSS.
+
 ### The help center
 
 Four pages, each on `ProductPageBase`, each static SSR with plain links and a GET form, so everything works without script:
@@ -97,7 +156,7 @@ Four pages, each on `ProductPageBase`, each static SSR with plain links and a GE
   characters; a page with a text is `noindex`; paging links keep the escaped text.
 - `/p/{key}/kb/{category}/{slug}` (`KbArticle`) shows the article: breadcrumbs, the title, the day it changed, the body and a "Still need help?" link. **`KbArticleBody` is the second and last place the Portal turns
   text into markup** (`PortalRules.MarkupStringSites` lists exactly it and `CustomerMessageBody`): the API renders the Markdown and sanitizes the HTML (D-044), and the Portal passes it on byte for byte
-  (`KbArticleHostTests` compares it with the API's string). A plain-http image in an article is blocked by the CSP's `img-src 'self' https: data:` in Production; that is the intended posture.
+  (`KbArticleHostTests` compares it with the API's string), except that an `h1` in the body is shown as an `h2` (`BodyHeadings`), because the page's own title is its one `h1`. A plain-http image in an article is blocked by the CSP's `img-src 'self' https: data:` in Production; that is the intended posture.
 
 Everything an agent wrote or a visitor typed (a name, a title, a summary, a snippet, a search text) is plain text shown by Razor, which encodes it. `/p/{key}/kb/search/{x}` matches the article route with the category
 `search`, which the API reserves, so it is a 404.
@@ -196,13 +255,15 @@ src/TechStrap.Portal/
     Layout/       PortalLayout, ProductHeader, ProductFooter
     Pages/        Home (the root), ProductHome, KbHome, KbCategory, KbSearch, KbArticle, Contact, ContactReceived, Ticket, LostLink, NotFound, Error, StyleGuide (Development only)
     Tickets/      CustomerMessageBody (a markup site), MessageThread, TicketStatusBanner
-    Ui/           AccentScope, PoweredByFooter, ProductUnavailable, DevelopmentOnly, ErrorSummary, FieldError, FormField, AttachmentInput, HoneypotField, Pager, StateMessage
+    Ui/           AccentScope, PoweredByFooter, ProductUnavailable, DevelopmentOnly, ErrorSummary, FieldError, FormField, FormGuard, AttachmentInput, HoneypotField, Pager, StateMessage
+    BodyHeadings.cs  an h1 in an author's body is shown as an h2
     KbCopy.cs     the words of the help center (beside ShellCopy)
-  Forms/          FormError and FormFields, FormCopy, FormFailure, AttachmentRules, EmailRules, ContactFormViewModel and its validator, ReplyForm, LostLinkForm, ContactCopy, ReceivedReference
+  Forms/          FormError and FormFields, FormCopy, FormFailure, AttachmentRules, EmailRules, ContactFormViewModel and its validator, ReplyForm, LostLinkForm, ContactCopy, ReceivedReference,
+                  SubmitIds, SubmitKey and SubmitGuard (the double-send guard)
   Kb/             KbPaging, KbSearchText (plain helpers the pages and the suggest adapter share)
   Headers/        PortalHeaderRules (the /t rules, the attachment sandbox, the form pages and the help center)
   Products/       ProductThemeViewModel, ProductScope, ProductPageBase
-  Routing/        PortalRoutes, ProductKeyShape, KbSlugShape
+  Routing/        PortalRoutes, PageLinks (a link to a place on the current page), ProductKeyShape, KbSlugShape
   Seo/            PortalSeoRegistration (Blazor.Seo: base URL, robots.txt, the sitemap, canonical host), PortalSitemap, PortalSitemapBuilder, PortalSitemapCache, JsonLdText,
                   KbStructuredData (the breadcrumb and article records), KbSeo
   Settings/       PortalOptions and its validator
@@ -210,7 +271,7 @@ src/TechStrap.Portal/
   Tickets/        CustomerTicketPresenter and its view models, TicketCopy, FollowUpLink, AttachmentPassThrough
   Uploads/        RequestTooLargeMiddleware
   Styles/         SCSS partials over the brand tokens (docs/BRAND.md)
-  wwwroot/js/     kb-suggestions.js
+  wwwroot/js/     kb-suggestions.js, portal-forms.js (both loaded once by App.razor)
 ```
 
 Rules the code follows (the architecture tests check them): the Portal references **Contracts and Hosting only**; **components never inject `HttpClient`** (only `Clients/` mentions it); no inline script
@@ -224,16 +285,43 @@ Portal added. `ProxyHopStartupFilter` puts the host behind a trusted reverse pro
 path no Portal route matches. A test that needs the real server (a request size limit) calls `UseKestrel(0)` and `StartServer()` and reads the address from `IServerAddressesFeature`. bUnit covers the layout and
 the shared help-center components; the help-center host tests (`Kb/`) parse the page with AngleSharp (which bUnit brings) and assert on elements, the JSON-LD is parsed as JSON, and `OutputCachePipelineTests` builds a
 small host with the real wiring to prove what the cache keeps. The cache and sitemap-cache tests that wait use short real lifetimes, because a `MemoryCache` has no `TimeProvider`.
-`tests/TechStrap.Portal.Tests/js` holds the node tests of the browser module. The shared rules are in `TechStrap.Architecture.Tests` (`PortalRules`), the Hosting header-rule mechanism in `TechStrap.Api.Tests`
+`tests/TechStrap.Portal.Tests/js` holds the node tests of the two browser modules. The start-failure tests of all three test projects use the one `tests/Shared/StartupFailure.cs`, which reads a host's refusal to start
+from the log sink of its factory when `CreateClient()` loses a race with the host's disposal, and the "neutral 404" tests compare `Seen`, which holds every response header except the per-request ones. The accessibility and
+style tests are `ResponsiveStyleTests`, `TokenContrastTests`, `CspStyleTests`, `HeadingHostTests`, `LayoutLandmarkHostTests` and `ErrorSummaryLinkHostTests`; the double-send tests are `SubmitGuardTests` and `DoubleSendHostTests`. The shared rules are in `TechStrap.Architecture.Tests` (`PortalRules`), the Hosting header-rule mechanism in `TechStrap.Api.Tests`
 (`PathHeaderRuleHostTests`), and the log and Sentry redaction in `TechStrap.Api.Tests`.
 
+## Manual checks (owner, before merging 09d)
+
+The tests read the compiled CSS and the markup and run the host in memory; they cannot see a layout, a screen reader or a real browser. These checks close **P09-T16** and, with the compose run, **P09-T18**, and each result is
+recorded in the pull request (the checklist there is ticked by the owner). Run the Portal against an API with the Development seed (`orbitly` and `paperplane`), in Chrome with the DevTools Console open.
+
+1. No CSP errors on the Portal's pages: `/`, `/p/paperplane`, the contact form, the received page, a ticket page, the help center, an article. No `Refused to ...` lines; the fonts and the CSS load; both modules load (`js/kb-suggestions.*.js` and `js/portal-forms.*.js`, status 200, `text/javascript`).
+2. Run the axe browser extension on the product home, the contact form (with the error summary showing), the received page, a ticket page, the help-center home, a category page, search results and an article: no critical findings. Record the result.
+3. Run Lighthouse (accessibility) on the contact form, a ticket page and an article: at least 90 each. Record the scores.
+4. JavaScript off (DevTools, Disable JavaScript): contact form -> submit -> received page, and a ticket -> reply -> the same page, work end to end; the received page still shows the number (it can be selected); the skip link jumps to the main content; an error summary link moves the focus to its field.
+5. Screenshots at 360, 768 and 1280 px of the product home, the contact form, the contact form with errors, the received page, the ticket page and an article: one column on a phone, no horizontal scroll, the category cards two across from 768 and three from 1200, nothing clipped. Attach them to the pull request.
+6. Keyboard only: Tab goes first to the skip link (visible), then the header navigation; Enter on the skip link moves the focus to the main content; on a form with errors the summary has the focus when the page opens and each link moves the focus to its field; focus is visible everywhere.
+7. Click the contact button on the product home (an enhanced navigation) and type in the subject: the article suggestions appear. Then do the same from a page that was loaded directly.
+8. Double click "Send message" on the contact form, "Send" on a ticket reply and "Send me a new link": one ticket, one reply, one email (check the Mailpit inbox and the Admin queue); the button shows "Sending" until the page changes. Press the back button after a send: the button is usable again.
+9. On the received page click "Copy ticket number": the number is on the clipboard and "Copied" shows; in a page opened over plain http from another address (no clipboard) the number is selected and the sentence says so.
+10. Type or paste about 85,000 characters into the message: the counter appears and counts; a long text with many line breaks shows it over the limit before the server would say so.
+11. Forced-colors emulation (DevTools > Rendering > Emulate CSS media feature forced-colors): a 3px outline on the focused control, the skip link bordered, the error summary and the status banner outlined. Reduced-motion emulation: no animation. The console shows no CSP violations in either.
+12. 400 percent zoom and increased text spacing (a bookmarklet or the Text Spacing extension): nothing is lost or overlaps.
+13. The hostile accents: set a product's accent to `#F59E0B`, `#0F3D2E`, a red, a gray, a near-black and a blue in the Admin and look at the contact form and the product home: text, buttons, the skip link and focus stay readable.
+14. A screen reader (NVDA or VoiceOver) on the contact form with errors, and on a ticket page: the landmarks are announced with their names (Main, the footer navigation, "Powered by"); the error summary is announced once; the counter speaks after a pause, not on every key.
+15. An invalid token, an unknown product and an unpublished article look the same (the neutral 404 page).
+16. The compose smoke (`pwsh scripts/Test-ComposeSmoke.ps1`) and the contact flow under `docker compose up` (P09-T18): the Portal is healthy, and hitting the contact form repeatedly from one client address trips the API's 429 for that address only.
+
 ## Known gaps
 
-- `ProductHome` and `KbHome` each carry a search box, so the product home shows a duplicate of the help-center one; merging them is PHASE-09d.
+- The product home has the shared search box (`KbSearchBox`, merged in 09d) and the header's link to the help center, but still no list of categories.
 - The sitemap build's API calls carry the address of the visitor whose request started it, so each sitemap build makes 1 + N API calls under one forwarded IP, and the API's public limit is 120 per minute per IP:
   with about 120 or more active products the sitemap build is rate-limited and the sitemap goes stale or becomes unavailable. Possible later fixes are a bulk sitemap endpoint, or exempting the Portal's own build traffic from the limit.
-- The help center has no category description on its category page and the product home has no category list; the visual polish, the accessibility and no-JS pass and the double-send guard are PHASE-09d.
-- There is no "copy" button for the ticket number, no live character counter and no "sending" state on the submit button: they need script, and the Portal keeps every flow script-free (09d decides).
+- The help center has no category description on its category page and the product home has no category list (the article list does not carry a description, and a second call per page was not worth it).
+- The form helpers need script (the sending state, the copy button, the counter) and are only extras: every flow works without them, which is the owner's JavaScript-off walk below.
+- The double-send guard is per instance and is lost on a restart, and two Portal replicas do not share it (like the sitemap cache); the API has no idempotency key for the two public writes.
+- The Blazor DOM diff of an enhanced navigation rewrites the attributes of an element it keeps, and a child a script added to such an element. The helpers are built for it (a custom element rebuilds in
+  `connectedCallback`; the sending state is in a `WeakMap`); a browser check of a link to the same page type is in the manual checklist.
 - The package's `JsonLd` component is unsafe for any text an author wrote (see SEO); the Portal does not use it for strings, and the problem is to be reported to `SyntaxCircus.Blazor.Seo`.
 - A chunked post over the size limit is the framework's 400 about an antiforgery token, not a 413 (a browser form post always declares its length).
 - A post to an unknown product is the framework's plain-text 400 ("Cannot submit the form 'contact' because no form on the page currently has that name."), not the 404 page a GET gets: the product page ends in `NotFound()` before the form is rendered, so there is no form to post to. Nothing is created. A post to a malformed or unknown ticket token is the same for the form `reply`, with an identical body, so nothing tells the two apart.
@@ -244,4 +332,4 @@ small host with the real wiring to prove what the cache keeps. The cache and sit
 - The Portal does not use `GlobalErrorBoundary`: its Try again button needs interactivity, and catching a render error in the page would answer 200 with a branded page instead of the plain 500 error page.
 - Sentry has no general email rule: it masks the `name`, `email`, `subject` and `ref` query values and the `/t/{token}` path only (Serilog's email pattern does catch addresses in logs).
 - If OpenTelemetry tracing were enabled, server spans would carry `url.path=/t/<token>`. It is off by default; masking it is a follow-up.
-- A double click on "Send message" can send twice before the redirect arrives (there is no script to disable the button); the API creates a ticket for each (09d adds a guard).
+- The Portal's own `Seen` comparison ignores `Pragma` and `Set-Cookie` (the framework's antiforgery step adds them to a response that rendered a form, which a post to a product that vanished after its form was served still carries): a visitor holding that form already knew the product.
````

`scripts/tests/RepositoryDocs.Tests.ps1`

```diff
--- a/scripts/tests/RepositoryDocs.Tests.ps1
+++ b/scripts/tests/RepositoryDocs.Tests.ps1
@@ -163,31 +163,32 @@ Describe 'D-045 (the public portal)' {
         (Get-RepoText 'docs/architecture/03-PACKAGE-MAP.md') | Should -Match 'MapSeoRobotsTxt'
     }
 
-    It 'ticks only the tasks and deliverables 09a, 09b and 09c fully deliver, and the roadmap and discovery rows say 09c is complete, pending merge' {
+    It 'ticks only the tasks and deliverables 09a to 09d fully deliver, and the roadmap and discovery rows say PHASE-09 is complete, pending merge' {
         $spec = Get-RepoText 'docs/architecture/PHASE-09-public-portal.md'
-        foreach ($number in 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 15, 17, 19, 21, 22, 23) {
+        foreach ($number in 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 17, 19, 21, 22, 23) {
             $id = 'P09-T{0:00}' -f $number
-            $spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is delivered by 09a, 09b or 09c"
+            $spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is delivered by 09a, 09b, 09c or 09d"
         }
-        # T09 waits for the double-submit guard (09d), T16 is the 09d polish pass, T18 waits for the owner's compose run of the smoke, T20 is deferred.
-        foreach ($number in 9, 16, 18, 20) {
+        # T16 waits for the owner's evidence (axe, Lighthouse, the JavaScript-off walk, screenshots), T18 for the owner's compose run of the smoke, T20 is deferred.
+        foreach ($number in 16, 18, 20) {
             $id = 'P09-T{0:00}' -f $number
-            $spec | Should -Match ('(?m)^- \[ \] \*\*' + $id + '\*\*') -Because "$id is not finished by 09c"
+            $spec | Should -Match ('(?m)^- \[ \] \*\*' + $id + '\*\*') -Because "$id is not closed by 09d"
         }
-        $spec | Should -Match 'delivered except double-submit \(deferred to 09d, D-045' -Because 'T09 stays open with the reason written down'
+        $spec | Should -Match 'delivered except double-submit \(deferred to 09d, D-045' -Because 'the 09b history of T09 is kept'
+        $spec | Should -Match '\*\*Owner evidence pending:\*\*' -Because 'T16 says what the owner still has to record'
         $spec | Should -Match '(?m)^- \[x\] `TechStrap\.Portal` host with `\.env\.example`, forwarded-headers and client-IP forwarding to the API\.'
         $spec | Should -Match '(?m)^- \[x\] Branded layout with per-product theming and NotFound handling\.'
         $spec | Should -Match '(?m)^- \[x\] Contact page with honeypot, attachments, deflection island, submitted page\.'
         $spec | Should -Match '(?m)^- \[x\] Customer ticket view, reply \(incl\. Closed -> follow-up\), lost-link, attachment pass-through\.'
         $spec | Should -Match '(?m)^- \[x\] Typed clients for public product, public ticket, customer ticket, public KB\.'
         $spec | Should -Match '(?m)^- \[x\] KB home/category/search/article pages'
-        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 09 \|.*D-045.*\| 09a merged \(PR #14\); 09b merged \(PR #15\); 09c complete \(pending merge\); 09d not started'
-        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 09 \|.*\| 09a merged \(PR #14\); 09b merged \(PR #15\); 09c complete \(pending merge\); 09d not started'
+        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 09 \|.*D-045.*\| 09a merged \(PR #14\); 09b merged \(PR #15\); 09c merged \(PR #16\); PHASE-09 complete \(pending merge\)'
+        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 09 \|.*\| 09a merged \(PR #14\); 09b merged \(PR #15\); 09c merged \(PR #16\); PHASE-09 complete \(pending merge\)'
     }
 
     It 'has a Portal developer guide, linked from the README, that lists every setting and the known gaps' {
         $guide = Get-RepoText 'docs/development/PORTAL-APP.md'
-        foreach ($heading in '## Run it locally', '### Configuration', '## How a page is served', '### Forms and uploads', '### Suggestions beside the subject', '### The help center', '### SEO and structured data', '### Caching and the sitemap', '### The ticket page and attachments', '### The lost-link page', '## Where things live', '## Tests', '## Known gaps') {
+        foreach ($heading in '## Run it locally', '### Configuration', '## How a page is served', '### Forms and uploads', '### The double-send guard', '### Suggestions beside the subject', '### Form helpers (`portal-forms.js`)', '### Accessibility, layout and the base address', '### The help center', '### SEO and structured data', '### Caching and the sitemap', '### The ticket page and attachments', '### The lost-link page', '## Where things live', '## Tests', '## Known gaps') {
             $guide | Should -Match ('(?m)^' + [regex]::Escape($heading))
         }
         foreach ($key in 'API__BASEURL', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_PORTAL_DEFAULT_PRODUCT', 'TECHSTRAP_PORTAL_SHOW_POWERED_BY', 'CANONICALHOST__CANONICALHOST') {
@@ -313,6 +314,45 @@ Describe 'D-045 as built in 09b' {
     }
 }
 
+Describe 'D-045 as built in 09d' {
+    BeforeAll {
+        $log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
+        $script:Section = [regex]::Match($log, '(?s)## D-045:.*?(?=\r?\n## D-\d+:|\z)').Value
+        $script:Guide = Get-RepoText 'docs/development/PORTAL-APP.md'
+        $script:Spec = Get-RepoText 'docs/architecture/PHASE-09-public-portal.md'
+    }
+
+    It 'records what the 09d build found, as consequences of the addendum' {
+        foreach ($phrase in 'As built in 09d: the double-send guard', 'As built in 09d: the form helpers', 'As built in 09d: links to the current page', 'As built in 09d: styles', 'As built in 09d: test hardening',
+                'Known in 09d: the guard is per instance', 'Known in 09d: the owner', 'Known in 09d: an `h1` in a body', 'Resolved in 09d') {
+            $script:Section | Should -Match ([regex]::Escape($phrase)) -Because "the as-built list must carry: $phrase"
+        }
+    }
+
+    It 'has a developer guide that describes the guard, the helpers, the base-address rule and the owner checklist' {
+        foreach ($phrase in 'SubmitGuard', 'SubmitIds', 'FormGuard', 'portal-forms.js', 'ts-copy-text', 'ts-char-count', 'PageLinks.ToFragment', 'data-enhance-nav', 'BodyHeadings', 'StartupFailure', 'ResponsiveStyleTests',
+                'HeadingHostTests', 'DoubleSendHostTests', '## Manual checks (owner, before merging 09d)', 'Lighthouse', 'axe', 'forced-colors', 'Double click') {
+            $script:Guide | Should -Match ([regex]::Escape($phrase)) -Because "PORTAL-APP.md must mention $phrase"
+        }
+        $script:Guide | Should -Not -Match 'merging them is PHASE-09d'
+        $script:Guide | Should -Not -Match 'there is no script to disable the button'
+    }
+
+    It 'lists the six semantic Portal tokens in BRAND.md' {
+        $brand = Get-RepoText 'docs/BRAND.md'
+        foreach ($token in '--p-error', '--p-error-bg', '--p-success', '--p-success-bg', '--p-warn', '--p-warn-bg') {
+            $brand | Should -Match ([regex]::Escape('`' + $token + '`')) -Because "BRAND.md must define $token"
+        }
+        $brand | Should -Match ([regex]::Escape('--ts-reading-width'))
+    }
+
+    It 'ticks T09 and the success criteria the tests prove, and leaves the owner evidence open' {
+        $script:Spec | Should -Match '(?m)^- \[x\] A customer can open'
+        $script:Spec | Should -Match '(?m)^- \[ \] Rate limits observe the real client IP through the portal\.'
+        $script:Spec | Should -Match '(?m)^- \[ \] `dotnet build`/`dotnet test` green; portal container healthy under compose\.'
+    }
+}
+
 Describe 'D-045 as built in 09c' {
     BeforeAll {
         $log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
@@ -330,10 +370,10 @@ Describe 'D-045 as built in 09c' {
         }
     }
 
-    It 'records the lower-case-only cache rule, the 09d search-box note and the article-only JSON-LD in the right places' {
+    It 'records the lower-case-only cache rule, the search-box merge and the article-only JSON-LD in the right places' {
         $script:Guide | Should -Match ([regex]::Escape('Only all-lowercase paths are kept'))
         $script:Guide | Should -Not -Match 'known exception'
-        $script:Guide | Should -Match ([regex]::Escape('merging them is PHASE-09d'))
+        $script:Guide | Should -Match ([regex]::Escape('merged in 09d'))
         $script:Section | Should -Match ([regex]::Escape('there is no exception'))
         $script:Section | Should -Not -Match 'timing-dependent'
         $script:Spec | Should -Match ([regex]::Escape('JSON-LD is on the article page only'))
```

The checklist in the guide is the owner's: each item is a thing no test here can see (a real browser's console, axe, Lighthouse, a screen reader, a real clipboard, a double click, 400 percent zoom, the compose stack). The owner records each result in the pull request, and ticks P09-T16 in the PR checklist when 1 to 15 are done and P09-T18 when 16 is.

- [ ] **Step 3: Run the Pester block and the script tests to verify they pass**

```bash
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal
```

Expected: 319 pass (node tests included):

```text
Tests Passed: 319, Failed: 0, Skipped: 0, Inconclusive: 0, NotRun: 0
```

- [ ] **Step 4: Prove each pin with a recorded mutation**

The docs are pinned by Pester, and each pin is a phrase. Save `$T\specs\m5.py`:

```python
"""Mutations of Task 5 (the docs pins). Run from the repository root."""
PESTER = ["pwsh", "-NoProfile", "-File", "scripts/Invoke-ScriptTests.ps1", "-Output", "Minimal", "-Path", "scripts/tests/RepositoryDocs.Tests.ps1"]
SPEC = "docs/architecture/PHASE-09-public-portal.md"

MUTATIONS = [
    ("1 T09 is not ticked", SPEC, [("- [x] **P09-T09** Build `CustomerReplyForm`", "- [ ] **P09-T09** Build `CustomerReplyForm`")], PESTER),
    ("2 T16 loses its owner-evidence note", SPEC, [("**Owner evidence pending:**", "**Owner evidence:**")], PESTER),
    ("3 the roadmap row is stale", "docs/architecture/99-IMPLEMENTATION-ROADMAP.md", [("09c merged (PR #16); PHASE-09 complete (pending merge)", "09c complete (pending merge); PHASE-09 not started")], PESTER),
    ("4 the guide loses the owner checklist heading", "docs/development/PORTAL-APP.md", [("## Manual checks (owner, before merging 09d)", "## Manual checks")], PESTER),
    ("5 BRAND.md loses a semantic token", "docs/BRAND.md", [("| `--p-warn-bg` | Ground of a notice |", "| `--p-warn-ground` | Ground of a notice |")], PESTER),
    ("6 a success criterion that needs the owner is ticked", SPEC, [("- [ ] Rate limits observe the real client IP through the portal.", "- [x] Rate limits observe the real client IP through the portal.")], PESTER),
]
```

Run them in one foreground batch (about a minute) and record the results:

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | T09 is not ticked | `PHASE-09-public-portal.md` | KILLED |
| 2 | T16 loses its owner-evidence note | `PHASE-09-public-portal.md` | KILLED |
| 3 | the roadmap row is stale | `99-IMPLEMENTATION-ROADMAP.md` | KILLED |
| 4 | the guide loses the owner checklist heading | `PORTAL-APP.md` | KILLED |
| 5 | BRAND.md loses a semantic token | `BRAND.md` | KILLED |
| 6 | a success criterion that needs the owner is ticked | `PHASE-09-public-portal.md` | KILLED |

Every mutation is killed; there are no survivors in this task.

- [ ] **Step 5: Verify the whole branch**

```bash
dotnet build TechStrap.slnx -c Release --no-incremental
dotnet test --solution TechStrap.CI.slnf -c Release
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build
```

Expected: 0 warnings and 0 errors; the CI suite passes (6,878 tests); 319 script tests pass; `No changes have been made to the model since the last migration.`:

```text
    0 Warning(s)
    0 Error(s)
Test run summary: Passed!
  total: 6878
  failed: 0
  succeeded: 6878
  skipped: 0
Tests Passed: 319, Failed: 0, Skipped: 0, Inconclusive: 0, NotRun: 0
No changes have been made to the model since the last migration.
```

- [ ] **Step 6: Commit and hand over**

```bash
git add -A
git diff --cached --stat
git commit -m "docs: PHASE-09d close-out: guide, ticks, rows, D-045 as built and the owner's checklist (D-045)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

The pull request carries the owner's checklist (PORTAL-APP.md, "Manual checks") as its test plan: the axe run, the Lighthouse scores, the JavaScript-off walk, the screenshots at 360, 768 and 1280, the double click, the copy button, the counter, forced colours and reduced motion, the hostile accents, and the compose smoke. P09-T16 and P09-T18 are ticked by the owner there; P09-T20 stays deferred.

---

## Appendix: the browser check tools (outside the repository)

Tasks 3 and 4 end with a check in a real browser that no test makes. These scripts are the spike's, kept so the check can be repeated. Put them in `$T` (outside the repository); they need Node 22 or later (the global `WebSocket`) and Microsoft Edge. Never commit them. Start order and clean-up: start the stand-in API (`node $T/stubapi.mjs 5999`) and the Portal (`API__BASEURL=http://127.0.0.1:5999/ ASPNETCORE_ENVIRONMENT=Development DotEnv__Enabled=false dotnet run --no-build -c Release --project src/TechStrap.Portal --urls http://127.0.0.1:5188`), run a check, then stop every process you started (`$T/killspike.ps1` stops the Portal, the stand-in API and the Edge instances whose profile folder starts with `edge-spike`, and nothing else). Set `SHOT_DIR` to a folder for the screenshots.

`stubapi.mjs` (a stand-in for the TechStrap API: the product, the ticket POST that counts tickets, the lost-link POST, the categories and the search)

```javascript
// A tiny stand-in for the TechStrap API, for browser spikes only (never committed). node stubapi.mjs <port>
import http from 'node:http';
const port = Number(process.argv[2] || 5999);
const product = { key: 'paperplane', displayName: 'Paperplane', logoPath: null, accentColour: '#F59E0B', onAccentColour: '#000000', accentInkColour: '#9D6507' };
const json = (res, status, body) => { res.writeHead(status, { 'content-type': 'application/json' }); res.end(JSON.stringify(body)); };
let tickets = 0;
http.createServer((req, res) => {
  const url = new URL(req.url, 'http://x');
  const chunks = [];
  req.on('data', c => chunks.push(c));
  req.on('end', () => {
    console.log(req.method, url.pathname + url.search);
    if (url.pathname === '/api/public/products/paperplane') return json(res, 200, product);
    if (url.pathname === '/api/public/products/paperplane/tickets' && req.method === 'POST') { tickets++; return json(res, 201, { ticketNumber: 'PAP-' + (40 + tickets), viewUrl: null, warnings: [] }); }
    if (url.pathname === '/api/customer/access-link' && req.method === 'POST') { res.writeHead(202); return res.end(); }
    if (url.pathname === '/api/public/kb/paperplane/categories') return json(res, 200, [{ slug: 'printing', name: 'Printing', description: 'Printers and paper', articleCount: 2 }]);
    if (url.pathname.startsWith('/api/public/kb/paperplane/search')) return json(res, 200, { items: [{ slug: 'jams', categorySlug: 'printing', title: 'Fixing a paper jam', snippet: 'Open the tray', productKey: 'paperplane', categoryName: 'Printing' }], page: 1, pageSize: 5, totalCount: 1 });
    if (url.pathname === '/__count') return json(res, 200, { tickets });
    json(res, 404, { type: 'not-found', title: 'Not Found', status: 404 });
  });
}).listen(port, '127.0.0.1', () => console.log('stub api on', port));
```

`cdp.mjs` (a minimal Chrome DevTools Protocol driver over Edge headless: `goto`, `eval`, `click`, `realClick`, `key`, `setViewport`, `shot`, `close`)

```javascript
// A minimal Chrome DevTools Protocol driver over Edge headless (spikes only, never committed). Always call close() in a finally.
import { spawn } from 'node:child_process';
const EDGE = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
export const sleep = ms => new Promise(r => setTimeout(r, ms));

export async function launch(port = 9444, extraArgs = []) {
  const dir = (process.env.TEMP || '/tmp') + '/edge-spike-' + Date.now();
  const proc = spawn(EDGE, ['--headless=new', '--disable-gpu', '--hide-scrollbars', `--remote-debugging-port=${port}`, '--user-data-dir=' + dir, ...extraArgs, 'about:blank'], { stdio: 'ignore' });
  let targets;
  for (let i = 0; i < 60; i++) { try { targets = await (await fetch(`http://127.0.0.1:${port}/json`)).json(); if (targets.some(t => t.type === 'page')) break; } catch { } await sleep(250); }
  const page = targets.find(t => t.type === 'page');
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise(r => ws.onopen = r);
  let id = 0; const pending = new Map(); const events = [];
  ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } else if (m.method) events.push(m); };
  const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
  await send('Page.enable'); await send('Runtime.enable'); await send('Network.enable');
  const api = {
    events, send,
    async goto(url) { await send('Page.navigate', { url }); await sleep(1200); },
    async eval(expr) { const r = await send('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }); if (r.result.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 400)); return r.result.result.value; },
    async click(selector) { return api.eval(`(() => { const e = document.querySelector(${JSON.stringify(selector)}); if (!e) return 'NOT FOUND'; e.click(); return 'clicked'; })()`); },
    async realClick(selector) {
      const r = await api.eval(`(() => { const e = document.querySelector(${JSON.stringify(selector)}); if (!e) return null; e.scrollIntoView({block:'center'}); const b = e.getBoundingClientRect(); return {x: b.x + b.width/2, y: b.y + b.height/2}; })()`);
      if (!r) return 'NOT FOUND';
      for (const type of ['mouseMoved','mousePressed','mouseReleased']) await send('Input.dispatchMouseEvent', { type, x: r.x, y: r.y, button: 'left', clickCount: 1 });
      return 'clicked';
    },
    async key(key, code, text) { for (const type of ['keyDown','keyUp']) await send('Input.dispatchKeyEvent', { type, key, code, text: type==='keyDown' ? text : undefined, windowsVirtualKeyCode: key==='Enter'?13:key==='Tab'?9:0 }); },
    async setViewport(width, height, mobile = false) { await send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile }); },
    async shot(file) { const r = await send('Page.captureScreenshot', { format: 'png' }); (await import('node:fs')).writeFileSync(file, Buffer.from(r.result.data, 'base64')); },
    async close() { try { ws.close(); } catch { } try { proc.kill(); } catch { } await sleep(300); },
  };
  return api;
}
```

`browser_t3.mjs` (Task 3: both modules defined by the shell, the suggestions after an enhanced click, the counter, the sending state)

```javascript
import { launch, sleep } from './cdp.mjs';
const b = await launch(9450);
const out = []; const say = (k, v) => out.push(k + ' = ' + JSON.stringify(v));
try {
  await b.goto('http://127.0.0.1:5188/p/paperplane');
  await b.eval('window.__marker = "doc1"');
  say('home: kb-suggestions defined by the shell', await b.eval('!!customElements.get("ts-kb-suggestions")'));
  say('home: portal-forms elements defined', await b.eval('!!customElements.get("ts-copy-text") && !!customElements.get("ts-char-count")'));
  await b.click('a.btn.btn-primary[href="/p/paperplane/contact"]');
  await sleep(1500);
  say('enhanced nav: same document', await b.eval('window.__marker'));
  say('contact: suggester element upgraded', await b.eval('(() => { const e = document.querySelector("ts-kb-suggestions"); return e ? e.constructor.name : "none"; })()'));
  await b.eval('(() => { const i = document.getElementById("subject"); i.value = "paper jam"; i.dispatchEvent(new Event("input", { bubbles: true })); })()');
  await sleep(1500);
  say('contact: suggestions after enhanced nav', await b.eval('[...document.querySelectorAll("ts-kb-suggestions a")].map(a => a.textContent)'));
  say('contact: counter hidden at start', await b.eval('document.querySelector("ts-char-count").hidden'));
  await b.eval('(() => { const t = document.getElementById("body"); t.value = "x".repeat(85000); t.dispatchEvent(new Event("input", { bubbles: true })); })()');
  say('contact: counter at 85000 chars', await b.eval('(() => { const c = document.querySelector("ts-char-count"); return { hidden: c.hidden, text: c.querySelector(".ts-count-text")?.textContent }; })()'));
  await b.eval('(() => { const t = document.getElementById("body"); t.value = "x".repeat(60) + String.fromCharCode(10).repeat(25000); t.dispatchEvent(new Event("input", { bubbles: true })); })()');
  say('contact: counter with 25000 line breaks', await b.eval('document.querySelector("ts-char-count .ts-count-text").textContent'));
  // sending state: fill the form and click submit; block the navigation to observe the state
  await b.eval('document.getElementById("body").value = "hello"; document.getElementById("body").dispatchEvent(new Event("input", {bubbles:true})); document.getElementById("name").value="Ada"; document.getElementById("email").value="ada@example.com"; document.getElementById("subject").value="Printer jam";');
  await b.eval('document.addEventListener("submit", e => { window.__prevented = true; e.preventDefault(); })');
  await b.eval('document.querySelector("form.ts-form button[type=submit]").click()');
  say('sending: button state', await b.eval('(() => { const bt = document.querySelector("form.ts-form button[type=submit]"); return { disabled: bt.disabled, text: bt.textContent }; })()'));
  say('sending: second click', await b.eval('(() => { const bt = document.querySelector("form.ts-form button[type=submit]"); bt.click(); return "second click on a disabled button does nothing: disabled=" + bt.disabled; })()'));
  await b.shot(process.env.SHOT_DIR + '/contact.png');
} finally { await b.close(); }
console.log(out.join('\n'));
```

`browser_t3b.mjs` (Task 3: a double click posts once; the received page's copy button)

```javascript
import { launch, sleep } from './cdp.mjs';
const b = await launch(9451);
const out = []; const say = (k, v) => out.push(k + ' = ' + JSON.stringify(v));
try {
  await b.goto('http://127.0.0.1:5188/p/paperplane/contact');
  await b.eval('document.getElementById("body").value = "hello"; document.getElementById("name").value="Ada"; document.getElementById("email").value="ada@example.com"; document.getElementById("subject").value="Printer jam";');
  await b.eval('document.querySelector("form.ts-form button[type=submit]").click(); document.querySelector("form.ts-form button[type=submit]").click();');
  await sleep(2500);
  say('landed on', await b.eval('location.pathname + (location.search ? "?ref=..." : "")'));
  say('number', await b.eval('document.getElementById("ticket-number")?.textContent'));
  say('copy element children', await b.eval('[...document.querySelector("ts-copy-text").children].map(c => c.tagName + ":" + c.textContent)'));
  await b.realClick('ts-copy-text button');
  await sleep(600);
  say('status after click (clipboard may be refused in headless)', await b.eval('document.querySelector("ts-copy-text .ts-copy-status").textContent'));
  say('selection after click', await b.eval('String(getSelection())'));
  say('tickets created by the double click', await (await fetch('http://127.0.0.1:5999/__count')).json());
  await b.shot(process.env.SHOT_DIR + '/received.png');
} finally { await b.close(); }
console.log(out.join('\n'));
```

`browser_t4.mjs` (Task 4: a real click and Enter on a summary link move the focus to the field, the skip link keeps the prefill, no horizontal scroll at 360, 768 and 1280, the forced-colors outline)

```javascript
import { launch, sleep } from './cdp.mjs';
const b = await launch(9452);
const out = []; const say = (k, v) => out.push(k + ' = ' + JSON.stringify(v));
const focus = () => b.eval('document.activeElement.tagName + "#" + document.activeElement.id + "." + document.activeElement.className');
try {
  // error summary: real click focuses the field
  await b.goto('http://127.0.0.1:5188/p/paperplane/contact');
  await b.eval('document.querySelector("form.ts-form button[type=submit]").click()');
  await sleep(1500);
  await b.eval('window.__marker = "doc2"');
  await b.realClick('.ts-error-summary a[href$="#email"]');
  await sleep(600);
  say('summary link click: location, same doc, focus', await b.eval('location.pathname + location.hash + " " + window.__marker') + ' | ' + await focus());
  // skip link by keyboard
  await b.goto('http://127.0.0.1:5188/p/paperplane/contact?subject=Printer%20jam');
  await b.key('Tab', 'Tab');
  say('first Tab', await focus());
  await b.key('Enter', 'Enter', '\r');
  await sleep(500);
  say('after skip: hash, focus, subject kept', await b.eval('location.hash + " " + document.activeElement.tagName + "#" + document.activeElement.id + " " + document.getElementById("subject").value'));
  // viewport shots
  for (const [w, h] of [[360, 800], [768, 900], [1280, 900]]) {
    await b.setViewport(w, h, w < 500);
    for (const [name, url] of [['home', '/p/paperplane'], ['contact', '/p/paperplane/contact']]) {
      await b.goto('http://127.0.0.1:5188' + url);
      await b.shot(`${process.env.SHOT_DIR}/${name}-${w}.png`);
      say(`${name}@${w}: scrollWidth<=innerWidth`, await b.eval('document.documentElement.scrollWidth <= window.innerWidth'));
    }
  }
  await b.setViewport(360, 800, true);
  await b.goto('http://127.0.0.1:5188/p/paperplane/contact');
  await b.eval('document.querySelector("form.ts-form button[type=submit]").click()');
  await sleep(1500);
  await b.shot(process.env.SHOT_DIR + '/contact-error-360.png');
  say('error@360 overflow', await b.eval('document.documentElement.scrollWidth <= window.innerWidth'));
  // forced colors + reduced motion emulation
  await b.send('Emulation.setEmulatedMedia', { features: [{ name: 'forced-colors', value: 'active' }, { name: 'prefers-reduced-motion', value: 'reduce' }] });
  await b.setViewport(1280, 900);
  await b.goto('http://127.0.0.1:5188/p/paperplane/contact');
  await b.key('Tab', 'Tab');
  say('forced colors: skip link outline + border', await b.eval('(() => { const s = getComputedStyle(document.activeElement); return s.outlineStyle + " " + s.outlineWidth + " | " + s.borderTopWidth; })()'));
  await b.shot(process.env.SHOT_DIR + '/forced-colors-1280.png');
} finally { await b.close(); }
console.log(out.join('\n'));
```

`killspike.ps1`

```powershell
Get-CimInstance Win32_Process | Where-Object { ($_.Name -in 'dotnet.exe','TechStrap.Portal.exe' -and $_.CommandLine -match 'TechStrap.Portal') -or ($_.Name -eq 'node.exe' -and $_.CommandLine -match 'stubapi') -or ($_.Name -eq 'msedge.exe' -and $_.CommandLine -match 'edge-spike') } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```
