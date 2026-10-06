# PHASE-09b Customer Flows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `TechStrap.Portal` the customer flows on top of the 09a foundation.
- **Contact:** the contact form with attachments, the honeypot, the prefill and the "received" page, with article suggestions beside the subject.
- **Ticket:** the ticket page with the customer's status wording, the public conversation, replies (including the Closed -> follow-up flow) and the attachment pass-through.
- **Recovery and proof:** the lost-link page, the form-page headers and redaction, and the compose check that the rate limit sees the real client address.

**Architecture:**
- **Static server rendering, forms without script.** Every form is a plain `<form method="post" @formname=...>` with `<AntiforgeryToken />`, a `[SupplyParameterFromForm]` model, a request size limit on the page and a redirect after the post. The files are the browser's own `IBrowserFile`s, checked against the Contracts limits and streamed on to the API as multipart (`StreamContent`, never buffered by the Portal).
- **Typed clients over the one `ApiConnection`.** Submit and reply go through the write client (never retried) and read calls through the read client; every call forwards the visitor's address; a customer call carries the token as the `X-Ticket-Token` header of that request only. A new streaming GET (`OpenStreamAsync`) backs the attachment pass-through.
- **One vanilla-JS element.** `<ts-kb-suggestions>` (a module in `wwwroot/js`, tested with `node --test`) calls a Portal-hosted `GET /p/{key}/suggest` adapter that asks the API's public search with the visitor's address. The server renders the element with a plain link inside it, so the form never depends on it.
- **Uniform failure.** A malformed, unknown, expired or revoked token, a wrong attachment id and an unknown product are one neutral 404; the lost-link answer is byte-identical for any well-formed address; an inactive product on a valid ticket is the neutral theme, not a 404.

**Tech Stack:** .NET 10, ASP.NET Core Blazor static SSR, `Microsoft.AspNetCore.DataProtection` (the time-limited protector, shared framework), vanilla ES modules with `node:test`, Serilog, Sentry, bUnit, xUnit v3, Shouldly and Pester.

**Spec:** `docs/architecture/PHASE-09-public-portal.md` (T02 for the ticket and KB-search clients, T06 to T11, T18, T21, T23) and `docs/architecture/UX-BRIEF-portal.md` (the contact, received, ticket, lost-link and status wording sections), `docs/development/PORTAL-APP.md`, `docs/architecture/04-DECISION-LOG.md` (D-002, D-017, D-019, D-024, D-038, D-039, D-043, D-044, D-045), and the owner rulings of 2026-10-06 (the 09b scope plan), recorded as a dated addendum to D-045 in Task 1. This plan covers **09b only**. 09c (the knowledge base pages, SEO, the sitemap and the polish pass) gets its own plan.

### Owner rulings (2026-10-06), recorded as the D-045 addendum
1. **The suggest adapter is `GET /p/{key}/suggest`** (it replaces `/p/{key}/kb/suggest`, so no category slug needs reserving).
2. **`IPublicKbClient` has `SearchAsync` only** in 09b; the adapter returns plain `{title, snippet, href}` JSON with links built by `PortalRoutes.KbArticle`, caps the items, passes the API's 429 through and answers an empty list for any other failure.
3. **The form limits are Contracts constants** (`IntakeLimits`), kept equal to the Domain's by a parity test.
4. **Static SSR forms** with a plain multiple file input and the size limit set by endpoint metadata, proven with a Kestrel test (the spike below).
5. **The honeypot goes to the API**, which answers a believable 201 and creates nothing; the Portal does not short-circuit.
6. **The "received" page** carries the ticket number in a protected, 10-minute `?ref=` (`CreateProtector("TechStrap.Portal.ContactReceived.v1").ToTimeLimitedDataProtector()`).
7. **The ticket page**: `ProductKey` on `CustomerTicketDto`; the token is parsed first; an inactive product is the neutral theme; `CustomerMessageBody` is the first `MarkupString` site; the status wording of the UX brief.
8. **Replies**: redirect to the same page, or, on a Closed ticket, to the follow-up read from `FollowUpViewUrl` and built with `PortalRoutes.Ticket`, never off-site; a 409 has its own message.
9. **The attachment pass-through** uses `ApiConnection.OpenStreamAsync` and always sends a download.
10. **Lost link**: byte-identical responses for a well-formed address; a malformed address is a validation error.
11. **noindex and no-store** on the four form pages.
12. **Redaction** of `subject` and `ref` (Serilog and Sentry) and `q` (Serilog).
13. **T18**: the compose smoke checks the Portal and the real client address.
14. **Vanilla JS** for the suggestions, tested with node.

### Decisions made while drafting (the D-045 addendum records them)
- **Task split.** The six tasks of the brief, unchanged. One addition inside Task 1: `IntakeLimits.FormBodyBytes` (27,262,976, the API's own request limit) moves into Contracts beside the text limits, so the Portal's `[RequestSizeLimit]` and the API's are one number; the API's `IntakeRequestLimits.FormBodyBytes` becomes an alias of it.
- **The spike (ruling 4), proved in a scratch copy before Task 3 was written; Task 3's tests are its durable form.**
  - **File binding.** A model class with `IReadOnlyList<IBrowserFile>? Files` binds `<input type="file" name="Form.Files" multiple>`: two files arrive with their `Name`, `Size` and `ContentType`. A form with no file part, and a part with an empty file name (what a browser sends for an empty file input), both bind a count of 0, so an empty part needs no handling.
  - **Reading.** `OpenReadStream()` throws above 512,000 bytes ("exceeds the maximum of 512000 bytes"), so every read passes `IntakeLimits.MaxFileBytes`. Cmsify's media upload does the same (`OpenReadStream(maxAllowedSize)` inside `await using`, the stream passed straight to the API client).
  - **Form binding rules.** A `[SupplyParameterFromForm]` property may not have an initializer (analyzer BL0008): the page sets it in `OnInitializedAsync` with `??=`. The framework renders the form's `action` as the current URL including the query, so the pages set `action` themselves (the contact form would otherwise post to a URL carrying the prefill). A missing or wrong antiforgery token is a 400.
  - **Request size limit, and its order against antiforgery.** `[RequestSizeLimit(IntakeLimits.FormBodyBytes)]` on the page component is read as endpoint metadata and applied by endpoint routing: a middleware placed before `UseAntiforgery` already sees `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` equal to the attribute's value and not read-only (the limit feature does not exist on `TestServer`, so the proof used `UseKestrel(0)` and `StartServer()`, reading the address from `IServerAddressesFeature`: `CreateClient()` keeps the default `http://localhost/` base address). A body over the limit is rejected without being buffered, but the form read inside the antiforgery check fails and the framework answers a 400 with its text about a missing token. A small middleware (`RequestTooLargeMiddleware`, before `UseAntiforgery`) answers a declared `Content-Length` over the applied limit with a plain 413 and the Portal's own sentence; a chunked body over the limit is still the 400. Cmsify's `[RequestSizeLimit]` on its upload endpoint is the same attribute.
  - **Redirect after post.** `NavigationManager.NavigateTo` in a static SSR handler answers 302 with an absolute `Location` (no exception; the handler returns straight after it). A refresh of the target is a GET.
- **A finding that corrects 09a.** A post to an unknown product (with a valid antiforgery token) is not a 404: `ProductPageBase` ends in `NavigationManager.NotFound()` before the handler, the framework re-executes the post against the not-found page, which has no handler named `contact`, and the answer is the framework's empty 400. Nothing is created and no form comes back; a GET is the neutral 404. The tests assert "404 or 400, no ticket call" and the D-045 as-built notes say so.
- **Cmsify patterns adopted** (read-only reference: `MediaLibrary.razor`, `Packages.razor`, `MediaController.cs`): `OpenReadStream(maxAllowedSize)` with an explicit constant, in `await using` or opened lazily and disposed with the request (`AttachmentUpload.OpenRead`, closed by the multipart request); the browser file's stream handed to the API client without buffering; and the `[RequestSizeLimit(const)]` attribute on the receiving endpoint, here on the two form pages. Not adopted: `@rendermode InteractiveServer` and `<InputFile OnChange>` (the Portal is static SSR and `PortalRules` forbids interactivity), and a 1 GiB limit.
- **Namespaces.** `TechStrap.Portal.Forms` (the shared form pieces: `FormError`, `FormFields`, `FormCopy`, `FormFailure`, `AttachmentRules`, `EmailRules`, the contact, reply and lost-link models and validators, `ReceivedReference`), `TechStrap.Portal.Tickets` (the presenter, the follow-up link, the pass-through), `TechStrap.Portal.Suggestions`, `TechStrap.Portal.Uploads`, and `Components/Tickets` for the ticket components. `Contact` is a page class, so the folder is not called `Contact`.
- **Route constants.** `KbSuggestTemplate` and `KbSuggest` are renamed `SuggestTemplate` and `Suggest` (they are no longer under the KB). The four form pages' last segments are constants (`ContactSegment`, `ReceivedSegment`, `LostLinkSegment`, `SuggestSegment`) that the header rule matches on, because `RouteLiteralTests` forbids a `/p...` literal outside `PortalRoutes`.
- **Checks are Portal validators, not data-annotation attributes.** `ContactFormValidator`, `ReplyFormValidator`, `EmailRules` and `AttachmentRules` are pure functions over the Contracts constants and return `FormError`s with the API's own codes, so a failure before the call and a failure from the API read the same (`FormCopy.For`). The spec said "validation attributes"; the constants and the single source of truth are what matter. Name is required (the UX brief) although the API only limits its length.
- **The API's wording never reaches a visitor.** `FormFailure` maps a refused call to field errors (the API's codes, the Portal's sentences), the attachment error for 413 and 415, a calm notice with a status for 429 (429), `reply-conflict` (409) and an outage (503), and the uniform 404. `ProblemMapping` maps 409 to `reply-conflict` (it was `api-error`).
- **The received page shows the number only.** The protected reference carries the ticket number (not the subject and not the address), and a missing, expired, tampered or foreign value shows the generic confirmation (never an error and never a 404; the UX brief's "direct visit is a 404" yields to the owner's ruling 6). The "copy" affordance, a character counter and a sending state need script and are deferred to the 09c polish pass; this is recorded as a known gap.
- **The form-page header rule is a third rule** in `PortalHeaderRules` (`no-store` and `noindex`, the shared referrer policy kept) matching by the segment constants without regard to case or a trailing slash. A 404 on those paths carries them too (the rule is by path, as the `/t` rule is).
- **The ticket page.** It is not a `ProductPageBase` (no key in its address): it parses the token, loads the ticket, then the ticket's product (any failure there is the neutral theme). Its failure page shows no Try again link, so the page body never repeats the token. Times are shown in UTC so the page reads the same everywhere. `CustomerMessageBody` is the only file with the word the architecture rule scans for.
- **Replies.** Success redirects to `PortalRoutes.Ticket(token)` (the token is in the path, which is the point of the page; never in a query). `FollowUpLink.TryGetToken` takes the last path segment of an absolute http(s) link and checks it with `TicketToken.TryParse`; the host, scheme, query and fragment are ignored, so the redirect never leaves the site; an unreadable link renders a generic "follow-up started" message in place of the form.
- **The attachment pass-through** answers a bad token, a non-GUID id (only the `D` format is accepted) and an upstream 404 with an empty 404, which the host re-executes into the one neutral page; the API's 429 is a 429 and any other failure a 502. `AttachmentDisposition` always sends `attachment` with the cleaned name (`filename` and `filename*`). `TicketHeaderHostTests`' attachment test now uses the real route with a file behind the stub API: the old probe path `/t/probe-token/attachments/probe-id` is matched by the real route (which answers its own 404), so the probe could no longer be reached.
- **Lost link** redirects to `?sent=1` (post, redirect, get), which shows one sentence and no form, so two responses cannot differ by a per-request antiforgery value. A not-found from the API (it cannot happen on that route) is shown as the same calm notice as an outage.
- **The suggest adapter** is a minimal-API endpoint in `Suggestions/` (it calls `IPublicKbClient`, not `HttpClient`, so `PortalRules` allows it outside `Clients/`; it is exempt like D-017: no workflow). Its own malformed-key check was dead code (the client already refuses a malformed key without a call): a mutation survived, so the check was removed rather than pinned.
- **The element removes its fallback** when it connects (the fallback is for browsers without script) and stays empty until there is something to suggest ("no results: the region stays absent", UX brief); it hides itself on a failed request and tries again on the next keystroke.
- **The compose smoke makes its calls from inside the network** (`docker compose exec -T api curl http://portal/...`), so the source is a container on the pinned subnet, which the override makes the Portal trust. A call from the host would arrive from the Docker gateway, whose address differs between Linux and Docker Desktop, so it could not be trusted portably. The override lowers the Api's public limit to 3 for the smoke project only. This plan's Task 6 shows the real run (the stack was built and started in the scratch copy and stopped again; no volume was removed).
- **The harness enforces the visitor's address.** The brief asks that every API-calling host test calls `AssertEveryCallBore`. Instead of remembering it in each of about a hundred tests, `PortalFactory.ExpectedClientIp` makes the factory assert it when it is disposed, and `FormTestKit.Factory` builds every host behind a trusted proxy with the same visitor (`FormTestKit.Client` sends it). A test that made no call passes; a lost forwarded-IP handler fails every host test that calls the API (the Task 3 mutations 31 and 32). Explicit `AssertEveryCallBore` calls stay where a test is about the address.
- **Tests of 09a that change meaning** (each edited in the task that makes it change): `ProblemMappingTests` (a 409 is no longer `api-error`), `PortalHeaderRulesTests` (three rules), `TicketHeaderHostTests` (a contact path is no longer an "outside /t" example; the attachment test uses the real route), `RequestLogRedactionHostTests` (the contact page is a 200 and `subject` is masked), `PiiRedactionQueryValueTests` and `SensitiveQuerySentryProcessorTests` (`subject` and `ref` are now masked), `PortalRuleTests` and `PortalRules` (the one markup site), `RepositoryDocs.Tests.ps1` (the ticks, the guide headings).
- **Shared redaction.** Masking `subject`, `ref` and `q` is in Hosting, so the Api's and the Admin's logs mask those query values too; this is accepted (D-045 already accepted it for `name` and `email`).
- **Honest survivors.** Two mutations of Task 3 are equivalent (the `MaxReferenceLength` guard, because a longer value fails to unprotect anyway, and the explicit not-found branch of the contact post, because the 404 status it would set renders the same page); both are listed under Task 3.
- **No new package and no new configuration key**, so there is no D-043 edit in 09b. `ToTimeLimitedDataProtector` is in the shared framework.
- **T18 is not ticked by this PR.** The smoke is written, pinned and was run for real in the scratch copy (Task 6), but the task's own validation is the owner's compose run, so the PHASE-09 text says so.
- **Shared files between tasks** (the tasks run in order, so the overlaps are safe): `PortalRoutes.cs` (Tasks 1, 2, 3 and 5), `Program.cs` (Tasks 3, 4 and 5), `_components.scss` (Tasks 3, 4 and 5), `Contact.razor` (Tasks 3 and 4), `ICustomerTicketClient.cs` and `CustomerTicketClient.cs` (Tasks 2 and 5), `ContactFormValidator.cs` (Tasks 3 and 5), `PortalRoutesTests.cs` (Tasks 1, 3 and 5), `StubApiHandler.cs` (Tasks 2 and 5), `RequestLogRedactionHostTests.cs` (Tasks 2 and 3), `TicketHeaderHostTests.cs` (Tasks 2 and 5), `RepositoryDocs.Tests.ps1` and the decision log, spec and PORTAL-APP.md (Tasks 1 and 6).

## Global Constraints

- **Build.** .NET SDK 10.0.401 targeting `net10.0`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild`. Private fields are `_camelCase`, constants are PascalCase, and namespaces are file-scoped.
- **Packages.** No new package in any project. `ToTimeLimitedDataProtector` and `IBrowserFile` are in the shared framework; `Microsoft.Extensions.TimeProvider.Testing` is already a Portal test reference (09a).
- **Project references.** The Portal references exactly Contracts and Hosting. Hosting stays a leaf: it may reference only packages.
- **Config (D-043).** 09b adds no configuration key. A key added later gets four edits: the host's `appsettings.json`, `src/TechStrap.Portal/.env.example`, `deploy/.env.portal.example` and compose where compose owns it; `scripts/tests/ConfigContract.Tests.ps1` must pass.
- **Portal conventions.**
  - Static SSR only, with no interactive render mode.
  - Components never inject `HttpClient`; only `Clients/` mentions it.
  - Copy lives in `*Copy` constants; no inline script or style.
  - All plain-text DTO fields are encoded; the `MarkupString` sites are exactly `CustomerMessageBody` (09b) and, in 09c, `KbArticleBody`. The word must not appear in any other Portal file, comments included.
  - Every route is a `PortalRoutes` constant or builder; no `/p...` or `/t...` literal outside `PortalRoutes`.
  - A token is read (`TicketToken.Value`) only in `ApiConnection` (the header) and `PortalRoutes` (a link); it never prints, and no page uses its `ToString`.
- **Encoding.** Write non-ASCII as `\u` escapes, using Python or .NET, never GNU sed. The Write tool decodes `\uXXXX`, so grep afterwards (`grep -nP "[^\x00-\x7F]"`). `SourceEncodingTests` and `SourceEscapeTests` must pass. Everything in this plan is ASCII; tests build non-ASCII text from `(char)0x...`.
- **Secrets and PII.** Never log tokens, emails, names, subjects, references or search text.
- **Tests.**
  - Failing test first, with RED and GREEN recorded.
  - Prove each pin with a recorded mutation.
  - Leak tests run at Verbose.
  - A test that needs the real server uses `UseKestrel(0)`, `StartServer()` and `IServerAddressesFeature`.
  - Every host test that makes an API call asserts the visitor's address (`AssertEveryCallBore`): the form and ticket kits build each host with `PortalFactory.ExpectedClientIp`, which asserts it when the factory is disposed.
  - Tests that set environment variables go in `ProcessEnvironmentCollection` (09b adds none).
  - Run a `--no-incremental` rebuild before the final runs, because mutation runs can leave stale DLLs.
  - No background mutation runs: every mutation runs in the foreground and the file is restored before the next.
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
- **Branch.** Work on `feat/phase-09b-customer-flows` (cut from main at 9b9c130, which includes 09a, #14). Never edit or commit on `main`.

## Review Focus

1. **Token safety on the ticket flows.** The token stays in the path and in the `X-Ticket-Token` header only: never in logs, Sentry, a Referer, a link built from `ToString`, a query or a redirect target outside the site; the follow-up redirect never leaves the site.
   - Pinned in Task 2 (`ApiConnectionStreamTests`, `CustomerTicketClientTests`, `PiiRedactionQueryValueTests`, `SensitiveQuerySentryProcessorTests`) and Task 5 (`FollowUpLinkTests`, `TicketReplyHostTests`, `TicketPageHostTests`, `TicketAttachmentHostTests`).
2. **Uniform 404 and no enumeration.** A malformed, unknown, expired or revoked token and a wrong attachment id are the identical 404; the lost-link answers are identical; an inactive product on a valid ticket is the neutral theme.
   - Pinned in Task 5 (`TicketUniformNotFoundHostTests`, `LostLinkHostTests`, `TicketPageHostTests`).
3. **Form abuse.** Antiforgery is enforced; the size limit applies before buffering; attachments are pre-validated and file names cleaned; the honeypot is passed through; the prefill is judged like typed input and never hidden or auto-submitted.
   - Pinned in Task 3 (`ContactPostHostTests`, `RequestTooLargeMiddlewareTests`, `AttachmentRulesTests`, `AttachmentFileNameTests` in Task 2, `ContactPageHostTests`).
4. **XSS.** `CustomerMessageBody` is the only markup site; the suggest JSON is plain text and the JS uses `textContent` only; the prefill values, agent names, subjects and file names are encoded.
   - Pinned in Tasks 4 and 5 (`kb-suggestions.test.mjs`, `SuggestEndpointHostTests`, `PortalRuleTests`, `TicketPageHostTests`).
5. **Real client IP and no write retry.** Every new client call forwards `X-Forwarded-For`; submit, reply and lost link use the write client; the smoke check passes.
   - Pinned in Tasks 2, 4 and 6 (`PublicTicketClientTests`, `CustomerTicketClientTests`, `PublicKbClientTests`, `SuggestEndpointHostTests`, `ComposeSmoke.Tests.ps1` and the real run).

---

### Task 1: API and Contracts: `ProductKey` on the customer ticket, the form limits in Contracts, the suggest route, and the D-045 addendum

**Review Focus pin:** none of the five alone. This task gives the ticket page its product (the link has none), puts the form limits and the request size limit in one place (so the Portal never offers more than the API accepts) and records the owner's rulings and the spike's findings.

**Files:**

- Modify: `docs/architecture/02-ARCHITECTURE.md`
- Modify: `docs/architecture/04-DECISION-LOG.md`
- Modify: `docs/architecture/PHASE-06-ticket-operations.md`
- Modify: `docs/architecture/PHASE-09-public-portal.md`
- Modify: `src/TechStrap.Api/Startup/IntakeHosting.cs`
- Modify: `src/TechStrap.Application/Tickets/Customer/GetCustomerTicketRequestHandler.cs`
- Modify: `src/TechStrap.Contracts/Intake/IntakeLimits.cs`
- Modify: `src/TechStrap.Contracts/Tickets/CustomerDtos.cs`
- Modify: `src/TechStrap.Portal/Routing/PortalRoutes.cs`
- Test (create): `tests/TechStrap.Api.Tests/Intake/IntakeRequestLimitsTests.cs`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`
- Test (modify): `tests/TechStrap.Api.Tests/Customer/CustomerTicketEndpointTests.cs`
- Test (modify): `tests/TechStrap.Application.Tests/Intake/IntakeLimitsParityTests.cs`
- Test (modify): `tests/TechStrap.Architecture.Tests/CustomerDtoShapeTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Routing/PortalRoutesTests.cs`

**Interfaces:**
- Consumes: `DomainLimits` (Domain), `IntakeRequestLimits` (Api), `CustomerTestData.SeedAsync` (Api.Tests: the product `orbitly`), `TechStrap.Tests.Shared.RepositoryRoot`.
- Produces:
  - `CustomerTicketDto(string Number, string ProductKey, string Subject, string Status, DateTimeOffset CreatedAt, IReadOnlyList<CustomerMessageDto> Messages)`: the public key of the ticket's product (never its internal id). `CustomerDtoShapeTests` still forbids `ProductId`.
  - `IntakeLimits.NameMaxLength` (100), `EmailMaxLength` (320), `SubjectMaxLength` (200), `BodyMaxLength` (100,000) and `FormBodyBytes` (`long`, `MaxMessageBytes + 1 MiB` = 27,262,976); `IntakeRequestLimits.FormBodyBytes` is an alias of the last. `IntakeLimitsParityTests` keeps the text limits equal to `DomainLimits`.
  - `PortalRoutes.SuggestTemplate` (`/p/{key}/suggest`) and `PortalRoutes.Suggest(key)`; `KbSuggestTemplate` and `KbSuggest` are gone.
  - The D-045 addendum (dated 2026-10-06, inside D-045: no new decision number), the PHASE-09 spec's "Corrections (D-045 addendum, 2026-10-06)" block, and the product-key wording in the 02-ARCHITECTURE and PHASE-06 tables.

- [ ] **Step 1: Keep the helper tools outside the repository**

The mutation steps of every task use this small tool (the same as 09a's; keep it OUTSIDE the repository, for example `C:\tmp\p09b-tools\mut.py`; the plan calls that folder `$T`). It applies one or more replacements to a file, runs a command, reports KILLED or SURVIVED and always puts the file back.

`mut.py`

```python
"""Mutation helper for the PHASE-09b plan. Keep it OUTSIDE the repository (for example in a temp folder).

usage: python mut.py <file> --replace <old> <new> [--replace <old> <new> ...] -- <command ...>

Applies each replacement to <file> (each <old> must match exactly once; "\\n" in an argument means a newline), runs the command in the current
directory, prints the lines that summarise the run, and ALWAYS puts the file back. The mutation is KILLED when the command fails and a SURVIVOR
(exit code 3) when it passes. Run it from the repository root, after `git add`-ing the task's files, so a failed run can also be undone with
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
    print("KILLED (the command failed)" if result.returncode != 0 else "SURVIVED (the command passed)")
    code = 0 if result.returncode != 0 else 3
finally:
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(original)
sys.exit(code)
```

Run every mutation from the repository root after `git add`-ing the task's files, so `git checkout -- <file>` also restores a file if a run is interrupted. A mutation that SURVIVES is a failed task unless this plan lists it as an honest survivor. In the tables, `PT` stands for `dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query` and `PESTER` for `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path`.

Example (Task 1, row 2), from the repository root:

```bash
python $T/mut.py src/TechStrap.Contracts/Intake/IntakeLimits.cs --replace "NameMaxLength = 100;" "NameMaxLength = 101;" -- dotnet test --project tests/TechStrap.Application.Tests -c Release --filter-query "/*/*/IntakeLimitsParityTests/*"
```

- [ ] **Step 2: Write the failing tests**

Four .NET test files change or arrive, and one Pester block. The API test reads the new field from the real endpoint; the shape test pins that the DTO carries the public key (and the existing test keeps `ProductId` out); the parity tests keep Contracts equal to the Domain and pin the 27,262,976 bytes; the route tests move the suggest adapter beside the KB; the Pester block pins the addendum and the corrected docs.

`scripts/tests/RepositoryDocs.Tests.ps1`

```diff
@@ -196,6 +196,37 @@ Describe 'D-045 (the public portal)' {
     }
 }
 
+Describe 'D-045 addendum (PHASE-09b rulings, 2026-10-06)' {
+    BeforeAll {
+        $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
+        $script:Section = [regex]::Match($script:Log, '(?s)## D-045:.*?(?=\r?\n## D-\d+:|\z)').Value
+    }
+
+    It 'is a dated addendum inside D-045, not a new decision number' {
+        $script:Section | Should -Match '(?m)^### Addendum \(2026-10-06, PHASE-09b customer flows\)'
+        $script:Log | Should -Not -Match '(?m)^## D-046'
+    }
+
+    It 'records each ruling the 09b plan rests on' {
+        foreach ($phrase in '/p/{key}/suggest', 'IntakeLimits', 'ProductKey', 'reply-conflict', 'IBrowserFile', 'RequestSizeLimit', 'CreateProtector', 'ToTimeLimitedDataProtector', 'DATAPROTECTION__KEYRINGPATH',
+                'honeypot', 'noindex', 'FollowUpViewUrl', 'CustomerMessageBody', 'OpenStreamAsync', 'PortalScripts.Tests.ps1', 'Test-ComposeSmoke.ps1') {
+            $script:Section | Should -Match ([regex]::Escape($phrase)) -Because "the addendum must mention $phrase"
+        }
+    }
+
+    It 'moves the suggest adapter in the spec and the decision text, so no category slug needs reserving' {
+        $spec = Get-RepoText 'docs/architecture/PHASE-09-public-portal.md'
+        $spec | Should -Not -Match 'kb/suggest'
+        $spec | Should -Match '(?m)^### Corrections \(D-045 addendum, 2026-10-06\)'
+        $script:Log | Should -Not -Match 'A category named `suggest` would be unreachable'
+    }
+
+    It 'says in the architecture and PHASE-06 tables that the customer ticket carries the product key' {
+        (Get-RepoText 'docs/architecture/02-ARCHITECTURE.md') | Should -Match 'CustomerTicketDto` \(public messages only; carries the product key\)'
+        (Get-RepoText 'docs/architecture/PHASE-06-ticket-operations.md') | Should -Match 'CustomerTicketDto` \(public messages only; carries the product key\)'
+    }
+}
+
 Describe 'the deployment runbook' {
     BeforeAll { $script:Runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md' }
 
```

`tests/TechStrap.Api.Tests/Customer/CustomerTicketEndpointTests.cs`

```diff
@@ -63,6 +63,7 @@ public sealed class CustomerTicketEndpointTests(TestPostgres postgres)
         raw.ShouldNotContain("Other reply");
         var dto = System.Text.Json.JsonSerializer.Deserialize<CustomerTicketDto>(raw, System.Text.Json.JsonSerializerOptions.Web)!;
         dto.Number.ShouldBe(seed.Number);
+        dto.ProductKey.ShouldBe("orbitly");
         dto.Subject.ShouldBe("Login broken");
         dto.Messages.Count.ShouldBe(2);
         var agentMessage = dto.Messages.Single(m => m.AuthorType == "Agent");
```

`tests/TechStrap.Api.Tests/Intake/IntakeRequestLimitsTests.cs` (new)

```csharp
using TechStrap.Api.Startup;
using TechStrap.Contracts.Intake;

namespace TechStrap.Api.Tests.Intake;

/// <summary>The Portal's form size limit is Contracts' <see cref="IntakeLimits.FormBodyBytes"/>; the API's own endpoint limit must be the same number, so the Portal never accepts a body the API would refuse (or the reverse).</summary>
public sealed class IntakeRequestLimitsTests
{
    [Fact]
    public void The_api_form_limit_is_the_contract_limit()
    {
        IntakeRequestLimits.FormBodyBytes.ShouldBe(IntakeLimits.FormBodyBytes);
    }
}
```

`tests/TechStrap.Application.Tests/Intake/IntakeLimitsParityTests.cs`

```diff
@@ -18,6 +18,23 @@ public sealed class IntakeLimitsParityTests
     public void The_api_key_header_matches_the_authentication_package_default() =>
         HeaderNames.ApiKey.ShouldBe("X-Api-Key");
 
+    [Fact]
+    public void The_text_limits_the_portal_forms_use_match_the_domain()
+    {
+        // D-045 addendum (2026-10-06): Contracts repeats these so the contact form never offers a longer value than the API accepts.
+        IntakeLimits.NameMaxLength.ShouldBe(DomainLimits.NameMaxLength);
+        IntakeLimits.EmailMaxLength.ShouldBe(DomainLimits.EmailMaxLength);
+        IntakeLimits.SubjectMaxLength.ShouldBe(DomainLimits.SubjectMaxLength);
+        IntakeLimits.BodyMaxLength.ShouldBe(DomainLimits.MessageBodyMaxLength);
+    }
+
+    [Fact]
+    public void The_form_body_limit_is_the_message_limit_plus_one_mebibyte_for_the_text_fields_and_multipart_framing()
+    {
+        IntakeLimits.FormBodyBytes.ShouldBe(IntakeLimits.MaxMessageBytes + (1024 * 1024));
+        IntakeLimits.FormBodyBytes.ShouldBe(27_262_976);
+    }
+
     [Fact]
     public void Allowed_extensions_are_lower_case_and_dotted() =>
         IntakeLimits.AllowedExtensions.ShouldAllBe(extension => extension.StartsWith('.') && extension == extension.ToLowerInvariant());
```

`tests/TechStrap.Architecture.Tests/CustomerDtoShapeTests.cs`

```diff
@@ -15,6 +15,13 @@ public sealed class CustomerDtoShapeTests
     public void Customer_dtos_expose_no_internal_or_agent_private_fields(Type dto) =>
         Properties(dto).ShouldNotContain(name => _forbidden.Contains(name));
 
+    [Fact]
+    public void The_customer_ticket_carries_the_products_public_key_so_the_portal_can_theme_the_page()
+    {
+        // D-045 addendum (2026-10-06): the key is what the public product endpoint is asked for; the internal ProductId stays forbidden above.
+        typeof(CustomerTicketDto).GetProperty("ProductKey")!.PropertyType.ShouldBe(typeof(string));
+    }
+
     [Fact]
     public void Every_type_reachable_from_a_customer_dto_is_customer_safe()
     {
```

`tests/TechStrap.Portal.Tests/Routing/PortalRoutesTests.cs`

```diff
@@ -37,7 +37,7 @@ public sealed class PortalRoutesTests
         PortalRoutes.KbCategoryTemplate.ShouldBe("/p/{key}/kb/{category}");
         PortalRoutes.KbArticleTemplate.ShouldBe("/p/{key}/kb/{category}/{slug}");
         PortalRoutes.KbSearchTemplate.ShouldBe("/p/{key}/kb/search");
-        PortalRoutes.KbSuggestTemplate.ShouldBe("/p/{key}/kb/suggest");
+        PortalRoutes.SuggestTemplate.ShouldBe("/p/{key}/suggest");
         PortalRoutes.TicketTemplate.ShouldBe("/t/{token}");
         PortalRoutes.TicketAttachmentTemplate.ShouldBe("/t/{token}/attachments/{id}");
     }
@@ -53,11 +53,19 @@ public sealed class PortalRoutesTests
         PortalRoutes.KbCategory("paperplane", "guides").ShouldBe("/p/paperplane/kb/guides");
         PortalRoutes.KbArticle("paperplane", "guides", "dark-mode").ShouldBe("/p/paperplane/kb/guides/dark-mode");
         PortalRoutes.KbSearch("paperplane").ShouldBe("/p/paperplane/kb/search");
-        PortalRoutes.KbSuggest("paperplane").ShouldBe("/p/paperplane/kb/suggest");
+        PortalRoutes.Suggest("paperplane").ShouldBe("/p/paperplane/suggest");
         PortalRoutes.Ticket("abc").ShouldBe("/t/abc");
         PortalRoutes.TicketAttachment("abc", Guid.Parse("11111111-2222-3333-4444-555555555555")).ShouldBe("/t/abc/attachments/11111111-2222-3333-4444-555555555555");
     }
 
+    [Fact]
+    public void The_suggest_route_is_beside_the_kb_not_under_it_so_no_category_slug_can_ever_shadow_it()
+    {
+        // D-045 addendum (2026-10-06): /p/{key}/kb/suggest would have made a category called "suggest" unreachable.
+        PortalRoutes.SuggestTemplate.ShouldNotStartWith(PortalRoutes.KbHomeTemplate);
+        PortalRoutes.Suggest("paperplane").ShouldNotStartWith(PortalRoutes.KbHome("paperplane"));
+    }
+
     [Fact]
     public void A_builder_escapes_each_value_so_it_can_never_add_a_segment_a_query_or_a_fragment()
     {
```


- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet build tests/TechStrap.Portal.Tests -c Release`
Expected: FAIL to compile, 4 errors: `error CS0117: 'PortalRoutes' does not contain a definition for 'SuggestTemplate'` (twice, in `PortalRoutesTests.cs`) and the same for `Suggest`.

Run: `dotnet build tests/TechStrap.Api.Tests -c Release`
Expected: FAIL to compile: `error CS1061: 'CustomerTicketDto' does not contain a definition for 'ProductKey'` (`CustomerTicketEndpointTests.cs`) and `error CS0117: 'IntakeLimits' does not contain a definition for 'FormBodyBytes'` (`IntakeRequestLimitsTests.cs`).

Run: `dotnet build tests/TechStrap.Application.Tests -c Release`
Expected: FAIL to compile: `error CS0117: 'IntakeLimits' does not contain a definition for` `NameMaxLength`, `EmailMaxLength`, `SubjectMaxLength`, `BodyMaxLength` and `FormBodyBytes` (`IntakeLimitsParityTests.cs`).

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release --filter-query "/*/*/CustomerDtoShapeTests/*"`
Expected: FAIL: `total: 9, failed: 1` (`The_customer_ticket_carries_the_products_public_key_so_the_portal_can_theme_the_page`).

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1 -Output Minimal`
Expected: FAIL: `Tests Passed: 25, Failed: 4` (the four tests of "D-045 addendum").

- [ ] **Step 4: Implement the Contracts, the handler, the API alias and the route rename**

The DTO gains the public key and the handler fills it from the product it already loaded (a one-line change). Contracts gains the limits, with `FormBodyBytes` defined once; the API's own limit becomes an alias so the two can never drift. The suggest route moves beside the KB.

`src/TechStrap.Contracts/Tickets/CustomerDtos.cs`

```diff
@@ -3,9 +3,11 @@ namespace TechStrap.Contracts.Tickets;
 /// <summary>
 /// What a customer sees through their link (D-024, D-038). Public data only: no agent ids, emails or surnames, tags, internal
 /// notes, events, LastActivityAt or other requesters. Status is a TicketStatuses name; the Portal maps it to words.
+/// ProductKey is the public key of the ticket's product (never its internal id): the link has no product in it, so the Portal asks the
+/// public product endpoint for this key to theme the page (D-045 addendum, 2026-10-06).
 /// </summary>
 public sealed record CustomerTicketDto(
-    string Number, string Subject, string Status, DateTimeOffset CreatedAt, IReadOnlyList<CustomerMessageDto> Messages);
+    string Number, string ProductKey, string Subject, string Status, DateTimeOffset CreatedAt, IReadOnlyList<CustomerMessageDto> Messages);
 
 /// <summary>
 /// One public message. AuthorType is a MessageAuthorTypes name; AuthorDisplayName is the resolved public agent name for agent
```

`src/TechStrap.Application/Tickets/Customer/GetCustomerTicketRequestHandler.cs`

```diff
@@ -82,7 +82,7 @@ public sealed class GetCustomerTicketRequestHandler(
         }
 
         return Result<CustomerTicketDto>.Success(
-            new CustomerTicketDto(ticket.Number.ToString(), ticket.Subject, ticket.Status.ToWire(), ticket.CreatedAt, dtos));
+            new CustomerTicketDto(ticket.Number.ToString(), product.Key, ticket.Subject, ticket.Status.ToWire(), ticket.CreatedAt, dtos));
     }
 
     private static Result<CustomerTicketDto> NotFound() => Result<CustomerTicketDto>.Failure(CustomerErrors.NotFound());
```

`src/TechStrap.Contracts/Intake/IntakeLimits.cs`

```diff
@@ -5,7 +5,20 @@ public static class IntakeLimits
 {
     public const long MaxFileBytes = 10L * 1024 * 1024;
     public const long MaxMessageBytes = 25L * 1024 * 1024;
+
+    /// <summary>
+    /// The largest multipart request body of a ticket or a reply: 25 MiB of files plus 1 MiB for the text fields and the multipart framing. The API and the Portal both refuse a larger body before reading it;
+    /// the handler still enforces the exact file limits.
+    /// </summary>
+    public const long FormBodyBytes = MaxMessageBytes + (1024 * 1024);
+
     public const int MaxFiles = 5;
+
+    // The text limits of the contact and reply forms (D-045 addendum, 2026-10-06). The Domain owns the values (DomainLimits); IntakeLimitsParityTests keeps these copies equal.
+    public const int NameMaxLength = 100;
+    public const int EmailMaxLength = 320;
+    public const int SubjectMaxLength = 200;
+    public const int BodyMaxLength = 100_000;
     public const int MaxMetadataKeys = 50;
     public const int MaxMetadataKeyLength = 64;
     public const int MaxMetadataValueLength = 1_000;
```

`src/TechStrap.Api/Startup/IntakeHosting.cs`

```diff
@@ -13,8 +13,8 @@ public static class IntakeRequestLimits
 {
     public const long JsonBodyBytes = 256 * 1024; // text fields and metadata only (D-034: no API attachments in v1)
 
-    // 25 MiB of files plus room for the text fields and multipart framing; the handler enforces the exact file limits.
-    public const long FormBodyBytes = IntakeLimits.MaxMessageBytes + (1024 * 1024);
+    // 25 MiB of files plus room for the text fields and multipart framing; the handler enforces the exact file limits. The Portal's forms use the same number.
+    public const long FormBodyBytes = IntakeLimits.FormBodyBytes;
 }
 
 /// <summary>Maps an over-limit request body (a 413 BadHttpRequestException) to a problem+json 413 instead of a 500.</summary>
```

`src/TechStrap.Portal/Routing/PortalRoutes.cs`

```diff
@@ -25,11 +25,13 @@ public static class PortalRoutes
     public const string LostLinkTemplate = "/p/{key}/lost-link";
     public const string KbHomeTemplate = "/p/{key}/kb";
 
-    // A literal segment wins over a parameter in endpoint routing: /p/{key}/kb/search and /kb/suggest are not categories. The API reserves the category slug "search" (KbLimits.ReservedCategorySlug).
+    // A literal segment wins over a parameter in endpoint routing: /p/{key}/kb/search is not a category. The API reserves the category slug "search" (KbLimits.ReservedCategorySlug).
     public const string KbCategoryTemplate = "/p/{key}/kb/{category}";
     public const string KbArticleTemplate = "/p/{key}/kb/{category}/{slug}";
     public const string KbSearchTemplate = "/p/{key}/kb/search";
-    public const string KbSuggestTemplate = "/p/{key}/kb/suggest";
+
+    // The KB suggestion adapter sits beside the KB, not under it, so no category slug can ever shadow it (D-045 addendum, 2026-10-06).
+    public const string SuggestTemplate = "/p/{key}/suggest";
 
     public const string TicketTemplate = "/t/{token}";
     public const string TicketAttachmentTemplate = "/t/{token}/attachments/{id}";
@@ -50,7 +52,7 @@ public static class PortalRoutes
 
     public static string KbSearch(string key) => $"{KbHome(key)}/search";
 
-    public static string KbSuggest(string key) => $"{KbHome(key)}/suggest";
+    public static string Suggest(string key) => $"{ProductHome(key)}/suggest";
 
     public static string Ticket(string token) => $"{TicketPrefix}/{Escape(token)}";
 
```


- [ ] **Step 5: Record the addendum and correct the docs**

The addendum is a dated section inside D-045 (the owner confirmed the rulings; the spike findings are recorded so later readers do not redo them). The two decision-text sentences that named `/p/{key}/kb/suggest` and the "category named suggest" consequence are updated in place; the spec gets a "Corrections (D-045 addendum, 2026-10-06)" block; the two tables that name `CustomerTicketDto` say it carries the product key.

`docs/architecture/02-ARCHITECTURE.md`

```diff
@@ -285,7 +285,7 @@ Conventions:
 | `POST /api/requesters/{id}/erase` (Admin) | `EraseRequesterRequestHandler` | `IRequesterRepository`, `IRequesterErasure`, `IAttachmentStore`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims`, `IUnitOfWork`, `TimeProvider`, `ILogger` | EF repos, `RequesterErasure`, storage store, UoW | 204; 404 | H, C, I | D-006, D-022 |
 | `GET /api/attachments/{id}` (Agent JWT; Admin reaches it through its pass-through adapter, D-017) | `GetAttachmentRequestHandler` | `ITicketRepository`, `IAttachmentStore`, `ICurrentAgentClaims`, `TimeProvider` | EF repos, storage store | 200 file stream (`Content-Disposition: attachment`, `nosniff`); 404 | H, C, I | none |
 | `GET /api/customer/attachments/{id}` (customer token header; `Public` policy, `token-access` limit; Portal reaches it through its pass-through adapter, D-017) | `GetCustomerAttachmentRequestHandler` | `IAccessTokenService`, `ITicketRepository`, `IRequesterRepository`, `IAttachmentStore`, `TimeProvider`, `ILogger` | EF repos, storage store, token service | 200 file stream (`Content-Disposition: attachment`, `nosniff`); 404 uniform | H, C, I | D-001, D-038 |
-| `GET /api/customer/ticket` (customer token header; `token-access` limit) | `GetCustomerTicketRequestHandler` | `IAccessTokenService`, `ITicketRepository`, `IRequesterRepository`, `IAgentRepository`, `IProductRepository`, `IUnitOfWork`, `TimeProvider` | EF repos, token service | 200 `CustomerTicketDto` (public messages only); 404 uniform | H, C, I | D-001 (token scheme) |
+| `GET /api/customer/ticket` (customer token header; `token-access` limit) | `GetCustomerTicketRequestHandler` | `IAccessTokenService`, `ITicketRepository`, `IRequesterRepository`, `IAgentRepository`, `IProductRepository`, `IUnitOfWork`, `TimeProvider` | EF repos, token service | 200 `CustomerTicketDto` (public messages only; carries the product key); 404 uniform | H, C, I | D-001 (token scheme) |
 | `POST /api/customer/ticket/replies` (customer token header; multipart) | `AddCustomerReplyRequestHandler` (Closed ticket creates follow-up) | `IAccessTokenService`, `ITicketRepository`, `IRequesterRepository`, `ITicketNumberAllocator`, `IAttachmentStore`, `IHtmlSanitizer`, `ITicketNotificationPlanner`, `IUnitOfWork`, `TimeProvider`, `IOptions<PortalLinkOptions>`, `ILogger` | EF repos, allocator, token service, storage store, sanitizer, planner, Outbox, UoW | 201 `CustomerReplyResponse` (message id, or new ticket view URL for follow-up); 404 uniform; 400 | H, C, I | D-008 |
 | `POST /api/customer/access-link` (`Public` policy, `lost-link` limit) | `RequestNewAccessLinkRequestHandler` | `IRequesterRepository`, `ITicketRepository`, `IEmailOutboxStore`, `ITicketNotificationPlanner`, `IUnitOfWork`, `TimeProvider`, `IOptions<LostLinkOptions>`, `ILogger` | EF repos, token service, Outbox, UoW | 202 uniform response always; 429 via limiter | H, C, I | D-006 |
 | `GET /api/dead-letters` (Admin) | `ListDeadLettersRequestHandler` | `IEmailOutboxStore` | Outbox store | 200 paged `DeadLetterDto` | H, C, I | D-010, D-022 |
```

`docs/architecture/04-DECISION-LOG.md`

```diff
@@ -1649,7 +1649,7 @@ PHASE-02, PHASE-06 and PHASE-08 are merged, so PHASE-09 can start. Reading the c
 ### Decision
 **Owner decisions (2026-10-05)**
 - **Delivery.** Three pull requests, each with its own branch, plan and review: 09a the foundation, 09b the customer flows, 09c the knowledge base, SEO and polish.
-- **KB suggestions are vanilla JS, not an InteractiveServer island.** There is no framework, no Blazor interactivity and no build step. The server renders a `<ts-kb-suggestions>` custom element with fallback markup inside it (a help-centre search link). A module in `wwwroot/js` defines the element: `connectedCallback` attaches a debounced listener to the subject field and fetches suggestions, and `disconnectedCallback` removes it and aborts any request in flight. It fetches from a Portal-hosted `GET /p/{key}/kb/suggest?q=` adapter, which forwards the real client IP to the API's public search. The adapter is exempt like D-017 (no workflow) and returns plain-text JSON that the module renders with `textContent`. There is no SignalR, no WebAssembly and no CSP change.
+- **KB suggestions are vanilla JS, not an InteractiveServer island.** There is no framework, no Blazor interactivity and no build step. The server renders a `<ts-kb-suggestions>` custom element with fallback markup inside it (a help-centre search link). A module in `wwwroot/js` defines the element: `connectedCallback` attaches a debounced listener to the subject field and fetches suggestions, and `disconnectedCallback` removes it and aborts any request in flight. It fetches from a Portal-hosted `GET /p/{key}/suggest?q=` adapter (moved from `/p/{key}/kb/suggest` by the 2026-10-06 addendum below), which forwards the real client IP to the API's public search. The adapter is exempt like D-017 (no workflow) and returns plain-text JSON that the module renders with `textContent`. There is no SignalR, no WebAssembly and no CSP change.
 - **Two small public API additions** (anonymous, the Public rate limit, the same cache headers as D-044), made in 09c: a paged list of a category's articles (published only, the product's plus shared, newest updated first), and `GET api/public/products` returning the active products (key and display name only), for the sitemap.
 - **`/` redirects to `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` when it is set.** Otherwise it shows a neutral page with no product list, so nothing can be enumerated.
 - **Ticket theming.** `CustomerTicketDto` gains `ProductKey` (additive), in 09b. `/t/{token}` loads that product's branding. `CustomerDtoShapeTests` is updated.
@@ -1679,7 +1679,7 @@ PHASE-02, PHASE-06 and PHASE-08 are merged, so PHASE-09 can start. Reading the c
 - **The Portal needs two settings in Production.** `API__BASEURL` (set by the deploy compose) and `TECHSTRAP_PORTAL_PUBLIC_URL` (the operator's). The Portal refuses to start without them and names the key.
 - **The PHASE-09 spec text was corrected where it named things that do not exist.** The Seo names, `BrandingThemeFactory`, the lost-link timing test and "Portal references Contracts only" (it is Contracts plus Hosting).
 - **A malformed or unknown product key is a 404 without calling the API.** Only a well-formed key is sent on.
-- **A category named `suggest` would be unreachable.** The Portal serves `/p/{key}/kb/suggest` (09b) beside `/p/{key}/kb/{category}`, and the API reserves only the slug `search`; 09b decides whether to reserve `suggest` too.
+- **The suggest adapter has no slug to reserve.** The 09b addendum below puts it at `/p/{key}/suggest`, beside the KB rather than under it, so no category slug can shadow it and the API's reserved slug stays `search` only.
 - **09a leaves the sitemap unmapped.** `/robots.txt` is served and disallows `/t/`; its `Sitemap:` line points at `/sitemap.xml`, which answers 404 until 09c.
 - **As built in 09a: log and Sentry redaction.** The shared PII redactor masks the value of a `name` or `email` query parameter (the contact page prefill) in every host's logs, and the Sentry processors mask the same, plus a 43-character token directly under `/t/` in any address they scrub. The Api and the Admin get this too; masking a `name=` value in their logs is accepted.
 - **As built in 09a: T17 redaction of the contact prefill.** The `name` and `email` query values of the contact page prefill are masked in logs and in Sentry (see above), pinned by `RequestLogRedactionHostTests`, `PiiRedactionQueryValueTests` and `SensitiveQuerySentryProcessorTests`.
@@ -1690,6 +1690,32 @@ PHASE-02, PHASE-06 and PHASE-08 are merged, so PHASE-09 can start. Reading the c
 - **Known: OpenTelemetry server spans would carry the ticket path.** If OpenTelemetry tracing were enabled, a server span would carry `url.path=/t/<token>`. Tracing is off by default; masking it is a follow-up.
 - **Known: the product pages link ahead.** Contact support and the footer's lost-link link (09b) and the search box (09c) answer 404 until their pages exist.
 
+### Addendum (2026-10-06, PHASE-09b customer flows)
+The owner's rulings for PHASE-09b, and what the plan's spike proved. They extend D-045; where they differ from the text above, they win. There is no new decision number.
+
+**Rulings**
+- **The suggest adapter is `GET /p/{key}/suggest?q=`.** It replaces `/p/{key}/kb/suggest`, so no category slug can collide with it and nothing needs reserving in the Domain, the Admin, Contracts or the docs. The route constants are `PortalRoutes.SuggestTemplate` and `PortalRoutes.Suggest(key)`.
+- **`IPublicKbClient` has `SearchAsync` only in 09b; 09c extends it.** The adapter returns plain JSON `{title, snippet, href}`, each `href` built with `PortalRoutes.KbArticle` from the product key the visitor is on. It caps the items, cuts the query at the API's 200 characters and answers an empty list for blank text. An API 429 is passed through as a 429; any other failure is an empty list. The response is `no-store`.
+- **The form limits move into Contracts.** `IntakeLimits` gains `NameMaxLength` (100), `EmailMaxLength` (320), `SubjectMaxLength` (200), `BodyMaxLength` (100,000) and `FormBodyBytes` (27,262,976, the API's own request limit). `IntakeLimitsParityTests` keeps them equal to `DomainLimits`. The contact form, the reply form and the prefill `maxlength` attributes use them.
+- **Static SSR forms** use `[SupplyParameterFromForm]`, `<AntiforgeryToken/>`, a plain `<input type="file" multiple>` and a redirect after the post. Attachments are sent on as `StreamContent`; the framework buffers an upload above 64 KB to a temporary file, so "streamed" means not held in memory.
+- **The honeypot is passed through.** The Portal sends the `Website` field to the API, which validates the product and answers a believable 201 without creating a ticket, so the response to a bot is the real one and product validation stays uniform. This replaces the spec's "silently success page without calling the API".
+- **The "received" page.** The ticket number travels in `?ref=`, protected with `CreateProtector("TechStrap.Portal.ContactReceived.v1").ToTimeLimitedDataProtector()` for 10 minutes. An expired or tampered value shows the generic confirmation. Production needs a persisted key ring (`DATAPROTECTION__KEYRINGPATH`, which both compose files already set) or a restart makes every pending reference generic. The page shows the ticket number only: not the subject and not the address, so the value carries nothing personal.
+- **The ticket page.** `CustomerTicketDto` gains `ProductKey` (a one-line handler change; the product was already loaded). The page parses the token first (a malformed one is the uniform 404 with no API call), loads the ticket, then loads the product's theme from `ProductKey`; an inactive or unknown product gets the neutral theme, never a 404. `CustomerMessageBody` is the first `MarkupString` site (the API sanitises the HTML). Status wording: New "Received", Open "In progress", Pending "Waiting for your reply", Solved "Solved", Closed "Closed".
+- **Replies.** A reply on a ticket that is not Closed redirects to the same page. A reply on a Closed ticket redirects to the follow-up: the token is the last segment of `FollowUpViewUrl`, run through `TicketToken.TryParse`, and the redirect is `PortalRoutes.Ticket(newToken)`, so a visitor is never sent to another host; if it does not parse, the page shows a generic confirmation. A 409 (`reply-conflict`) has its own friendly message (`ProblemMapping` maps it; it was `api-error`).
+- **The attachment pass-through** `GET /t/{token}/attachments/{id}` uses a new `ApiConnection.OpenStreamAsync(uri, TicketToken, ct)`, which reads only the response headers (through the read client) before streaming. A bad token or a non-GUID id is the uniform 404; an upstream 404 is the uniform 404; a transport failure is 502. The response is `Content-Disposition: attachment` with `nosniff`; the existing sandbox rule covers the CSP.
+- **Lost link.** For any well-formed address the Portal shows the same byte-identical confirmation; a malformed address shows the validation message; a 429 shows a friendly message.
+- **noindex and no-store** are added by a path rule to `/p/{key}/contact`, `/p/{key}/contact/received`, `/p/{key}/lost-link` and `/p/{key}/suggest`.
+- **Redaction.** The `subject` and `ref` query values are masked in Serilog and in Sentry, and `q` in Serilog (Sentry already masks it).
+- **T18.** `scripts/Test-ComposeSmoke.ps1` gains a Portal readiness check and a check that the rate limit sees the real client address through `/p/{key}/suggest`; the DryRun output and its Pester pins are updated.
+- **Vanilla JS.** `wwwroot/js/kb-suggestions.js` defines `<ts-kb-suggestions>`; its dependencies (`fetch`, timers, `AbortController`) are passed in so `node --test` can run it, through the new `scripts/tests/PortalScripts.Tests.ps1`. It loads as `<script type="module" src=...>`, so the CSP does not change.
+
+**Spike findings (proven in a scratch copy before the plan was written)**
+- **File binding.** A model class with `IReadOnlyList<IBrowserFile>? Files` binds `<input type="file" name="Form.Files" multiple>`: two files arrive with their `Name`, `Size` and `ContentType`. An empty file input (the part a browser sends with `filename=""`) and a form with no file part both give a count of 0, so an empty part needs no handling.
+- **Reading a file.** `IBrowserFile.OpenReadStream()` throws above 512,000 bytes; the Portal always passes `IntakeLimits.MaxFileBytes`.
+- **Forms.** A `[SupplyParameterFromForm]` property may not have an initializer (analyzer BL0008): the page sets it in `OnInitialized` when the framework left it null. A missing or invalid antiforgery token answers 400.
+- **Request size limit.** `[RequestSizeLimit(IntakeLimits.FormBodyBytes)]` on the page component is read as endpoint metadata and applied by endpoint routing, before the antiforgery middleware and before the form is read (the limit feature is already `2000` and not read-only in a middleware placed before `UseAntiforgery`, tested with `UseKestrel(0)` because `TestServer` has no such feature). A body over the limit is rejected without being buffered, but the framework reports it as a 400 with the antiforgery text, so a small middleware answers a declared `Content-Length` over the applied limit with a plain 413 first; a chunked body over the limit is still the 400.
+- **Redirect after post.** `NavigationManager.NavigateTo` in a static SSR handler answers 302 with an absolute `Location`; the handler returns straight after it.
+
 ### Approval
 - **Approved by:** Jon Seeley (owner, PHASE-09 planning)
 - **Approved on:** 2026-10-05
```

`docs/architecture/PHASE-06-ticket-operations.md`

```diff
@@ -53,7 +53,7 @@ Follow _template `APPLICATION_ARCHITECTURE.md` (not copied into this repo). Comm
 | `POST /api/requesters/{id}/erase` | `EraseRequesterRequestHandler` (Admin, D-022) | `IRequesterRepository`, `IRequesterErasure`, `IAttachmentStore`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims`, `IUnitOfWork`, `TimeProvider`, `ILogger` | EF repositories, `RequesterErasure`, `AttachmentStore` | 204; 404 | Mandatory flow |
 | `GET /api/attachments/{id}` (agent JWT; Admin reaches it through its pass-through adapter, D-017; 06a) | `GetAttachmentRequestHandler` | `ITicketRepository`, `IAttachmentStore`, `ICurrentAgentClaims`, `TimeProvider` | EF repository, `AttachmentStore` | 200 file stream with safe `Content-Disposition` and `nosniff`; 404 | Agent route only (D-036) |
 | `GET /api/customer/attachments/{id}` (`X-Ticket-Token`; `Public` policy, `token-access` limit; Portal reaches it through its pass-through adapter, D-017; 06b, D-038) | `GetCustomerAttachmentRequestHandler` | `IAccessTokenService`, `ITicketRepository`, `IRequesterRepository`, `IAttachmentStore`, `TimeProvider`, `ILogger` | EF repository, `AttachmentStore`, `AccessTokenService` | 200 file stream with safe `Content-Disposition` and `nosniff`; 404 uniform for customers | Separate customer route keeps `Public` standing alone (D-034, D-038) |
-| `GET /api/customer/ticket` (`X-Ticket-Token`) | `GetCustomerTicketRequestHandler` | `IAccessTokenService`, `ITicketRepository`, `IRequesterRepository`, `IAgentRepository`, `IProductRepository`, `IUnitOfWork`, `TimeProvider` | EF repository, `AccessTokenService`, `UnitOfWork` | 200 `CustomerTicketDto` (public messages only); 404 uniform | Mandatory flow |
+| `GET /api/customer/ticket` (`X-Ticket-Token`) | `GetCustomerTicketRequestHandler` | `IAccessTokenService`, `ITicketRepository`, `IRequesterRepository`, `IAgentRepository`, `IProductRepository`, `IUnitOfWork`, `TimeProvider` | EF repository, `AccessTokenService`, `UnitOfWork` | 200 `CustomerTicketDto` (public messages only; carries the product key); 404 uniform | Mandatory flow |
 | `POST /api/customer/ticket/replies` (`X-Ticket-Token`) | `AddCustomerReplyRequestHandler` (on Closed creates follow-up) | `IAccessTokenService`, `ITicketRepository`, `IRequesterRepository`, `ITicketNumberAllocator`, `IAttachmentStore`, `IHtmlSanitizer`, `ITicketNotificationPlanner`, `IUnitOfWork`, `TimeProvider`, `IOptions<PortalLinkOptions>`, `ILogger` | EF repositories, allocator, `AttachmentStore`, sanitizer, planner | 201 `CustomerReplyResponse` (message, or follow-up number and link); 404 uniform; 400; 429 at host | Mandatory flow |
 | `POST /api/customer/access-link` | `RequestNewAccessLinkRequestHandler` | `IRequesterRepository`, `ITicketRepository`, `IEmailOutboxStore`, `ITicketNotificationPlanner`, `IUnitOfWork`, `TimeProvider`, `IOptions<LostLinkOptions>`, `ILogger` | EF repositories, `AccessTokenService`, planner | Always 202 with identical body; 429 at host | Mandatory flow; uniform response by design |
 | `GET /api/dead-letters` | `ListDeadLettersRequestHandler` (Admin, D-022) | `IEmailOutboxStore` | EF `EmailOutboxStore` | 200 paged `DeadLetterDto` | Mandatory flow |
```

`docs/architecture/PHASE-09-public-portal.md`

```diff
@@ -37,7 +37,7 @@ sitemap, robots) through `SyntaxCircus.Blazor.Seo`.
 Where this page and D-045 differ, D-045 wins.
 - **Delivery.** Three pull requests: 09a (the foundation: T01, T03, T05, T17, T19, T22 and parts of T02 and T04), 09b (the customer flows: T06 to T11, T18, T21, T23, and it also finishes T02, the ticket clients) and 09c (the knowledge base, SEO and polish: T12 to T16, and it also finishes T02, the KB client, and T04, the sitemap). T20 is deferred.
 - **References.** The Portal references `TechStrap.Contracts` and `TechStrap.Hosting` (the shared host wiring, D-042), not Contracts only.
-- **KB suggestions.** A vanilla-JS custom element `<ts-kb-suggestions>` and a Portal-hosted `GET /p/{key}/kb/suggest` adapter replace the `KbDeflectionSuggestions` InteractiveServer island. No page has a circuit.
+- **KB suggestions.** A vanilla-JS custom element `<ts-kb-suggestions>` and a Portal-hosted `GET /p/{key}/suggest` adapter replace the `KbDeflectionSuggestions` InteractiveServer island. No page has a circuit.
 - **API additions.** 09c adds a paged list of a category's articles and `GET api/public/products`; 09b adds `ProductKey` to `CustomerTicketDto`. `/` redirects to `TECHSTRAP_PORTAL_DEFAULT_PRODUCT`, or shows a neutral page.
 - **Blazor.Seo.** The real names are `AddSyntaxCircusSeo`, `UseSyntaxCircusSeo`, `MapSeoRobotsTxt(extraDirectives)` and `MapSeoSitemap(staticEntries, provider)`; `UseCanonicalHost`, `MapRobotsTxt`, `MapSitemap` and `ISitemapEntryProvider` do not exist. `Seo:BaseUrl` is derived from `TECHSTRAP_PORTAL_PUBLIC_URL`.
 - **Theming.** `BrandingThemeFactory` is replaced by a thin `ProductThemeViewModel` over `AccentScope` and the DTO's derived colours; the logo address is re-checked.
@@ -46,6 +46,16 @@ Where this page and D-045 differ, D-045 wins.
 - **Received page.** The ticket number travels in a data-protection-protected `?ref=` value that expires after 10 minutes.
 - **Headers.** Per-path header rules in `UseTechStrapWebHost` give `/t/*` its `no-referrer`, `no-store` and `noindex`, and give only `/t/{token}/attachments/{id}` the sandbox CSP.
 
+### Corrections (D-045 addendum, 2026-10-06)
+
+The 09b rulings; where this page and the addendum differ, the addendum wins.
+- **Suggest adapter.** It is `GET /p/{key}/suggest` (not under `/kb`), so no category slug can shadow it.
+- **Honeypot.** A filled honeypot is sent to the API like any other submission; the API validates the product and answers a believable 201 without creating a ticket. The Portal does not short-circuit (P09-T06).
+- **Ticket product.** `CustomerTicketDto` carries `ProductKey`; `/t/{token}` loads that product's branding and falls back to the neutral theme for an inactive or unknown product.
+- **Limits.** The text and size limits of the forms are Contracts constants (`IntakeLimits`), kept equal to the Domain's by a parity test.
+- **Message bodies.** `CustomerMessageBody` is the only `MarkupString` site in 09b; `KbArticleBody` follows in 09c.
+- **Received page.** It shows the ticket number only.
+
 ## Application Boundaries
 
 Follow _template APPLICATION_ARCHITECTURE.md. This phase adds **no new server
```


- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet build TechStrap.slnx -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release`
Expected: PASS: `total: 728, failed: 0`.

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: PASS: `total: 293, failed: 0`.

Run: `dotnet test --project tests/TechStrap.Portal.Tests -c Release`
Expected: PASS: `total: 467, failed: 0`.

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release`
Expected: PASS: `total: 885, failed: 0` (the OpenAPI surface tests still pass: the DTO change is additive).

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1 -Output Minimal`
Expected: PASS: `Tests Passed: 29, Failed: 0`.

- [ ] **Step 7: Prove each pin with a mutation**

Run `git add -A` first. Every row is run from the repository root with the tool from Step 1; "Expected" is the result of the run.

| # | File | Replace | With | Command (after `--`) | Expected |
| --- | --- | --- | --- | --- | --- |
| 1 | `src/TechStrap.Application/Tickets/Customer/GetCustomerTicketRequestHandler.cs` | `ticket.Number.ToString(), product.Key,` | `ticket.Number.ToString(), "",` | `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/CustomerTicketEndpointTests/The_customer_sees_the_ticket_with_public_messages_and_no_store"` | KILLED, 1 failed of 1 |
| 2 | `src/TechStrap.Contracts/Intake/IntakeLimits.cs` | `NameMaxLength = 100;` | `NameMaxLength = 101;` | `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter-query "/*/*/IntakeLimitsParityTests/*"` | KILLED, 1 failed of 6 |
| 3 | `src/TechStrap.Contracts/Intake/IntakeLimits.cs` | `FormBodyBytes = MaxMessageBytes + (1024 * 1024);` | `FormBodyBytes = MaxMessageBytes;` | `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter-query "/*/*/IntakeLimitsParityTests/*"` | KILLED, 1 failed of 6 |
| 4 | `src/TechStrap.Api/Startup/IntakeHosting.cs` | `FormBodyBytes = IntakeLimits.FormBodyBytes;` | `FormBodyBytes = IntakeLimits.FormBodyBytes - 1;` | `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/IntakeRequestLimitsTests/*"` | KILLED, 1 failed of 1 |
| 5 | `src/TechStrap.Portal/Routing/PortalRoutes.cs` | `SuggestTemplate = "/p/{key}/suggest";` | `SuggestTemplate = "/p/{key}/kb/suggest";` | `PT "/*/*/PortalRoutesTests/*"` | KILLED, 2 failed of 7 |
| 6 | `src/TechStrap.Portal/Routing/PortalRoutes.cs` | `Suggest(string key) => $"{ProductHome(key)}/suggest"` | `Suggest(string key) => $"{KbHome(key)}/suggest"` | `PT "/*/*/PortalRoutesTests/*"` | KILLED, 2 failed of 7 |
| 7 | `docs/architecture/04-DECISION-LOG.md` | `### Addendum (2026-10-06, PHASE-09b customer flows)` | `### Addendum (2026-10-06)` | `PESTER scripts/tests/RepositoryDocs.Tests.ps1` | KILLED, 1 failed of 29 |
| 8 | `docs/architecture/PHASE-09-public-portal.md` | `` It is `GET /p/{key}/suggest` (not under `/kb`) `` | `` It is `GET /p/{key}/kb/suggest` (not under `/kb`) `` | `PESTER scripts/tests/RepositoryDocs.Tests.ps1` | KILLED, 1 failed of 29 |
| 9 | `docs/architecture/02-ARCHITECTURE.md` | `(public messages only; carries the product key); 404 uniform \| H, C, I \| D-001` | `(public messages only); 404 uniform \| H, C, I \| D-001` | `PESTER scripts/tests/RepositoryDocs.Tests.ps1` | KILLED, 1 failed of 29 |

- [ ] **Step 8: Verify and commit**

Run: `dotnet build TechStrap.slnx -c Release --no-incremental`
Expected: `0 Warning(s)`, `0 Error(s)`.

```bash
git status --short
git add -A src tests scripts docs
git diff --cached --stat
git commit -F - <<'EOF'
feat(contracts): product key on the customer ticket, the form limits, the suggest route and the D-045 addendum (PHASE-09b)

CustomerTicketDto carries the ticket's product key so the Portal can theme /t/{token}. IntakeLimits gains the
text limits and FormBodyBytes, kept equal to the Domain's by a parity test. The suggest adapter moves to
/p/{key}/suggest. D-045 gets a dated addendum with the 09b rulings and the spike findings.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
EOF
```

The commit must not contain `.superpowers/`, `bin/` or `obj/` (they are git-ignored); check `git diff --cached --stat` first.

### Task 2: Clients and plumbing: the ticket, customer and KB-search clients, the streaming GET, the multipart builder, the form-page headers and the redaction keys

**Review Focus pin:** 1 (the token is a header on each customer call and nowhere else, a streamed download needs no buffering) and 5 (every new call forwards `X-Forwarded-For`; submit, reply and lost link use the write client and are never retried). Review Focus 3 starts here too: a file name is cleaned before it reaches a multipart part.

**Files:**

- Create: `src/TechStrap.Portal/Clients/ApiDownload.cs`
- Create: `src/TechStrap.Portal/Clients/ApiQuery.cs`
- Create: `src/TechStrap.Portal/Clients/AttachmentFileName.cs`
- Create: `src/TechStrap.Portal/Clients/AttachmentUpload.cs`
- Create: `src/TechStrap.Portal/Clients/CustomerTicketClient.cs`
- Create: `src/TechStrap.Portal/Clients/ICustomerTicketClient.cs`
- Create: `src/TechStrap.Portal/Clients/IPublicKbClient.cs`
- Create: `src/TechStrap.Portal/Clients/IPublicTicketClient.cs`
- Create: `src/TechStrap.Portal/Clients/MultipartForm.cs`
- Create: `src/TechStrap.Portal/Clients/PublicKbClient.cs`
- Create: `src/TechStrap.Portal/Clients/PublicTicketClient.cs`
- Modify: `src/TechStrap.Hosting/Logging/PiiRedactionEnricher.cs`
- Modify: `src/TechStrap.Hosting/Sentry/SensitiveQuerySentryProcessor.cs`
- Modify: `src/TechStrap.Portal/Clients/ApiClientRegistration.cs`
- Modify: `src/TechStrap.Portal/Clients/ApiConnection.cs`
- Modify: `src/TechStrap.Portal/Clients/ApiErrorCodes.cs`
- Modify: `src/TechStrap.Portal/Clients/ProblemCopy.cs`
- Modify: `src/TechStrap.Portal/Clients/ProblemMapping.cs`
- Modify: `src/TechStrap.Portal/Headers/PortalHeaderRules.cs`
- Modify: `src/TechStrap.Portal/Routing/PortalRoutes.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Clients/ApiConnectionStreamTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Clients/ApiQueryTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Clients/AttachmentFileNameTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Clients/CustomerTicketClientTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Clients/MultipartFormTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Clients/PublicKbClientTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Clients/PublicTicketClientTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Headers/FormPageHeaderHostTests.cs`
- Test (modify): `tests/TechStrap.Api.Tests/Redaction/PiiRedactionQueryValueTests.cs`
- Test (modify): `tests/TechStrap.Api.Tests/SensitiveQuerySentryProcessorTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Api/StubApiHandler.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Clients/ProblemMappingTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Headers/PortalHeaderRulesTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Headers/TicketHeaderHostTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Logging/RequestLogRedactionHostTests.cs`

**Interfaces:**
- Consumes: `ApiConnection` (`GetAsync`, `SendAsync`, `SendContentAsync`; the private `Attach`), `ProblemMapping`, `TicketToken`, `ProductKeyShape.IsWellFormed`, `PortalRoutes` (Task 1), `HeaderNames.TicketToken`, `PagedResponse<T>`, `PublicKbSearchResultDto`, `SubmitTicketResponse`, `CustomerTicketDto`, `CustomerReplyResponse`, `RequestNewAccessLinkRequest`, `PathHeaderRule.Set`, the 09a harness (`ApiHarness`, `StubApiHandler`, `PortalFactory`, `AssertEveryCallBore`).
- Produces:
  - `AttachmentUpload(string FileName, string? ContentType, Func<Stream> OpenRead)` (public record): the stream is opened when the multipart body is built and closed with the request.
  - `AttachmentFileName.Clean(string?)` and `Fallback` (`"attachment"`); `MultipartForm.Build(IEnumerable<KeyValuePair<string,string?>> fields, IReadOnlyList<AttachmentUpload> attachments)` returning a `MultipartFormDataContent` that owns the streams (`AttachmentsField` is `"Attachments"`); `ApiQuery.Build(string path, params (string Name, string? Value)[] pairs)`.
  - `ApiConnection.OpenStreamAsync(string uri, TicketToken token, CancellationToken)` returning `Result<ApiDownload>`, and `ApiDownload` (public sealed: `Body`, `ContentType`, `ContentLength`, `FileName`, `DisposeAsync`).
  - `IPublicTicketClient.SubmitAsync(string productKey, NewTicketRequest request, CancellationToken)` with `NewTicketRequest(string Email, string Name, string Subject, string Body, string? Website, IReadOnlyList<AttachmentUpload> Attachments)`; `ICustomerTicketClient.GetAsync(TicketToken, ct)`, `ReplyAsync(TicketToken, CustomerReply, ct)` with `CustomerReply(string Body, IReadOnlyList<AttachmentUpload> Attachments)` and `RequestAccessLinkAsync(string email, ct)` returning `Result`; `IPublicKbClient.SearchAsync(string productKey, string text, int pageSize, ct)`. All registered scoped.
  - `ApiErrorCodes.ReplyConflict` (`"reply-conflict"`), `ProblemCopy.ReplyConflict`, and `ProblemMapping` mapping 409 to it (kind Conflict).
  - `PortalRoutes.ContactSegment`, `ReceivedSegment`, `LostLinkSegment`, `SuggestSegment`; `PortalHeaderRules.IsFormPagePath(PathString)` and a third rule (`Cache-Control: no-store`, `X-Robots-Tag: noindex`).
  - Redaction: the `subject` and `ref` query values in Serilog and Sentry, and `q` in Serilog.
  - Test harness: `StubApiHandler.OnFile(method, path, bytes, contentType, fileName?)` and `FileResponse(Stream, contentType, fileName?)` (a streaming-response helper).

- [ ] **Step 1: Write the failing tests**

The tests pin each helper in isolation (the file-name rule, the multipart body, the query escaping, the stream's headers-only read and its disposal), then each client through the real handler pipeline (the write or read client, the token header, the visitor's address, no retry for a write), the 409 mapping, the header rule at its path boundaries and at the host, and the new redaction keys. The 09a tests that change meaning are edited in place (a 409, the number of rules, the contact path in the "outside /t" theory, the redaction expectations).

`tests/TechStrap.Api.Tests/Redaction/PiiRedactionQueryValueTests.cs`

```diff
@@ -21,7 +21,7 @@ public sealed class PiiRedactionQueryValueTests
     [Theory]
     [InlineData("?name=Jane%20Doe", "?name=[redacted]")]
     [InlineData("?name=Jane+Doe&email=jane%40example.com", "?name=[redacted]&email=[redacted]")]
-    [InlineData("?subject=Hi&name=Jane&page=2", "?subject=Hi&name=[redacted]&page=2")]
+    [InlineData("?subject=Hi&name=Jane&page=2", "?subject=[redacted]&name=[redacted]&page=2")]
     [InlineData("?NAME=Jane&Email=a@b.example", "?NAME=[redacted]&Email=[redacted]")]
     [InlineData("?%6Eame=Jane&%65mail=x", "?%6Eame=[redacted]&%65mail=[redacted]")]
     [InlineData("?name=", "?name=[redacted]")]
@@ -29,7 +29,13 @@ public sealed class PiiRedactionQueryValueTests
     [InlineData("name=Jane", "name=[redacted]")]
     [InlineData("Request starting HTTP/1.1 GET http://localhost/p/paperplane/contact?name=Jane%20Doe&email=jane.doe%40example.com - -", "Request starting HTTP/1.1 GET http://localhost/p/paperplane/contact?name=[redacted]&email=[redacted] - -")]
     [InlineData("https://portal.test/p/x/contact?name=O%27Brien#top", "https://portal.test/p/x/contact?name=[redacted]#top")]
-    [InlineData("?subject=Hi%20there", "?subject=Hi%20there")]
+    [InlineData("?subject=Hi%20there", "?subject=[redacted]")]
+    [InlineData("?ref=CfDJ8NaYTe_x-1&other=1", "?ref=[redacted]&other=1")]
+    [InlineData("/p/x/contact/received?ref=CfDJ8NaYTe_x-1", "/p/x/contact/received?ref=[redacted]")]
+    [InlineData("?q=reset%20my%20password&page=2", "?q=[redacted]&page=2")]
+    [InlineData("GET /p/x/suggest?q=jane.doe+printer HTTP/1.1", "GET /p/x/suggest?q=[redacted] HTTP/1.1")]
+    [InlineData("?SUBJECT=a&Ref=b&Q=c", "?SUBJECT=[redacted]&Ref=[redacted]&Q=[redacted]")]
+    [InlineData("?%73ubject=a&%72ef=b&%71=c", "?%73ubject=[redacted]&%72ef=[redacted]&%71=[redacted]")]
     [InlineData("?name=O'Brien&page=2", "?name=[redacted]&page=2")]
     [InlineData("?email=a\"b@x.y#top", "?email=[redacted]#top")]
     public void The_value_of_name_and_email_in_a_query_string_is_masked_and_the_rest_is_kept(string text, string expected) =>
@@ -38,6 +44,9 @@ public sealed class PiiRedactionQueryValueTests
     [Theory]
     [InlineData("a message that says name=nothing but is not a query")]
     [InlineData("?username=jane&nickname=j&surname=d&filename=f.txt&email2=x")]
+    [InlineData("?subjects=1&reference=2&refs=3&query=4&sq=5&xq=6")]
+    [InlineData("text/html;q=0.9")]
+    [InlineData("a message that says q=1 and ref=2 and subject=3 but is not a query")]
     [InlineData("?%6Eam%65s=1")]
     [InlineData("hostname=db&email-from=x")]
     [InlineData("Name is Jane")]
```

`tests/TechStrap.Api.Tests/SensitiveQuerySentryProcessorTests.cs`

```diff
@@ -38,7 +38,11 @@ public sealed class SensitiveQuerySentryProcessorTests
     // P09-T17 / T21: the Portal's contact page may be opened with a name and an email in the address, and its ticket address carries the access token in the path.
     [Theory]
     [InlineData("?name=Jane%20Doe", "?name=[redacted]")]
-    [InlineData("?subject=Hi&name=Jane+Doe&email=jane%40example.com&page=2", "?subject=Hi&name=[redacted]&email=[redacted]&page=2")]
+    [InlineData("?subject=Hi&name=Jane+Doe&email=jane%40example.com&page=2", "?subject=[redacted]&name=[redacted]&email=[redacted]&page=2")]
+    [InlineData("?ref=CfDJ8NaYTe_x-1&page=2", "?ref=[redacted]&page=2")]
+    [InlineData("https://portal.test/p/x/contact/received?ref=CfDJ8NaYTe_x-1", "https://portal.test/p/x/contact/received?ref=[redacted]")]
+    [InlineData("?SUBJECT=a&Ref=b", "?SUBJECT=[redacted]&Ref=[redacted]")]
+    [InlineData("?%73ubject=a&%72ef=b", "?%73ubject=[redacted]&%72ef=[redacted]")]
     [InlineData("?NAME=Jane&Email=a@b.example", "?NAME=[redacted]&Email=[redacted]")]
     [InlineData("?%6Eame=Jane&%65mail=x", "?%6Eame=[redacted]&%65mail=[redacted]")]
     [InlineData("https://portal.test/p/orbitly/contact?name=Jane&email=jane%40example.com#top", "https://portal.test/p/orbitly/contact?name=[redacted]&email=[redacted]#top")]
@@ -113,6 +117,7 @@ public sealed class SensitiveQuerySentryProcessorTests
     [Theory]
     [InlineData("?status=Open&page=2")]
     [InlineData("?research=1&faq=2&query=3&squash=4")]
+    [InlineData("?subjects=1&reference=2&refs=3")]
     [InlineData("/queue/search")]
     [InlineData("?rese%61rch=1&f%61q=2")]
     [InlineData("https://admin.test/queue/search?status=Open")]
```

`tests/TechStrap.Portal.Tests/Api/StubApiHandler.cs`

```diff
@@ -59,6 +59,27 @@ public sealed class StubApiHandler : HttpMessageHandler
     /// <summary>An RFC 7807 answer in the shape the API produces: <c>type</c> is the error code, <c>detail</c> the message.</summary>
     public StubApiHandler OnProblem(HttpMethod method, string path, HttpStatusCode status, string type, string detail) => On(method, path, _ => Problem(status, type, detail));
 
+    /// <summary>A file download as the API sends it: the body as a stream (so a test can see whether it was read), a content type and, when given, a <c>Content-Disposition</c> with the stored name.</summary>
+    public StubApiHandler OnFile(HttpMethod method, string path, byte[] bytes, string contentType, string? fileName = null) =>
+        On(method, path, _ => FileResponse(new MemoryStream(bytes), contentType, fileName));
+
+    public static HttpResponseMessage FileResponse(Stream body, string contentType, string? fileName = null)
+    {
+        var content = new StreamContent(body);
+        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
+        if (body.CanSeek)
+        {
+            content.Headers.ContentLength = body.Length;
+        }
+
+        if (fileName is not null)
+        {
+            content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = $"\"{fileName}\"" };
+        }
+
+        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
+    }
+
     public static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body) => new(status) { Content = JsonContent.Create(body, options: Json) };
 
     public static HttpResponseMessage Problem(HttpStatusCode status, string type, string detail) => new(status)
```

`tests/TechStrap.Portal.Tests/Clients/ApiConnectionStreamTests.cs` (new)

```csharp
using System.Net;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// <see cref="ApiConnection.OpenStreamAsync"/>: the one streaming GET of the Portal (the attachment pass-through). It sends the ticket token like any customer call, goes through the retrying read client,
/// reads only the response headers before it returns (the body is never buffered), maps every failure to a <see cref="Result"/> and gives the caller a body it must dispose.
/// </summary>
public sealed class ApiConnectionStreamTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly string Text = new('t', TicketToken.Length);
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly string Path = $"/api/customer/attachments/{Id}";

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int Reads { get; private set; }

        public bool Closed { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Reads++;
            return base.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++;
            return base.ReadAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingContent(byte[] bytes) : HttpContent
    {
        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = bytes.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private static TicketToken Token()
    {
        TicketToken.TryParse(Text, out var token).ShouldBeTrue();
        return token;
    }

    [Fact]
    public async Task A_download_carries_the_token_through_the_read_client_and_gives_the_body_type_length_and_name()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Get, Path, _ => StubApiHandler.FileResponse(new MemoryStream("hello"u8.ToArray()), "text/plain", "log.txt"));

        var result = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        result.IsSuccess.ShouldBeTrue();
        await using var download = result.Value;
        download.ContentType.ShouldBe("text/plain");
        download.ContentLength.ShouldBe(5);
        download.FileName.ShouldBe("log.txt");
        using var reader = new StreamReader(download.Body);
        (await reader.ReadToEndAsync(Ct)).ShouldBe("hello");
        var request = api.Stub.Requests.ShouldHaveSingleItem();
        request.TicketToken.ShouldBe(Text);
        request.Client.ShouldBe(ApiClientNames.Read);
        request.Query.ShouldBeEmpty();
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task Only_the_response_headers_are_read_before_the_call_returns()
    {
        using var api = ApiHarness.Create();
        var body = new CountingStream(new byte[100_000]);
        api.Stub.On(HttpMethod.Get, Path, _ => StubApiHandler.FileResponse(body, "application/octet-stream"));

        var result = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        result.IsSuccess.ShouldBeTrue();
        body.Reads.ShouldBe(0, "the default completion option would have buffered the whole body here");
        await using var download = result.Value;
        (await download.Body.ReadAsync(new byte[10], Ct)).ShouldBeGreaterThan(0);
        body.Reads.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_missing_content_type_is_octet_stream_and_a_missing_name_is_null()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Get, Path, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });

        var result = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        await using var download = result.Value;
        download.ContentType.ShouldBe("application/octet-stream");
        download.FileName.ShouldBeNull();
    }

    [Fact]
    public async Task Disposing_the_download_closes_the_upstream_body_and_the_response()
    {
        using var api = ApiHarness.Create();
        var body = new CountingStream([1, 2, 3]);
        var content = new TrackingContent([1, 2, 3]);
        api.Stub.On(HttpMethod.Get, Path, _ => StubApiHandler.FileResponse(body, "application/octet-stream"));
        var result = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        body.Closed.ShouldBeFalse();
        await result.Value.DisposeAsync();

        body.Closed.ShouldBeTrue();

        api.Stub.On(HttpMethod.Get, Path, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        var second = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        content.Disposed.ShouldBeFalse("the response stays open while the caller streams it");
        await second.Value.DisposeAsync();

        content.Disposed.ShouldBeTrue("disposing the download releases the upstream response, so its connection goes back to the pool");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ApiErrorCodes.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, ApiErrorCodes.ApiUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized, ApiErrorCodes.ApiError)]
    public async Task A_failure_is_a_result_error_decided_by_the_status_alone(HttpStatusCode status, string code)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnProblem(HttpMethod.Get, Path, status, "x", "Npgsql host=10.0.0.5 stack trace");

        var result = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        result.IsFailure.ShouldBeTrue();
        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(code);
        error.Message.ShouldNotContain("Npgsql");
    }

    [Fact]
    public async Task A_503_is_retried_twice_like_any_read_and_every_attempt_carries_the_token()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, Path, HttpStatusCode.ServiceUnavailable);

        var unavailable = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        unavailable.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Get, Path).ShouldBe(1 + ApiClientRegistration.ReadRetryCount);
        api.Stub.Requests.ShouldAllBe(r => r.TicketToken == Text, "every attempt carries the token");
    }

    [Fact]
    public async Task A_transport_failure_is_api_unavailable_with_no_exception_text()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Get, Path, _ => throw new HttpRequestException("No connection could be made (10.1.2.3:5432)"));

        var result = await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), Ct);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        error.Message.ShouldNotContain("10.1.2.3");
    }

    [Fact]
    public async Task A_cancellation_by_the_caller_propagates_and_is_never_a_result()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Get, Path, _ => StubApiHandler.FileResponse(new MemoryStream([1]), "text/plain"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await api.Get<ApiConnection>().OpenStreamAsync($"api/customer/attachments/{Id}", Token(), cancelled.Token));
    }
}
```

`tests/TechStrap.Portal.Tests/Clients/ApiQueryTests.cs` (new)

```csharp
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>A query string built for the API: every value is escaped, so the visitor's text can never add a parameter, end the query or start a fragment.</summary>
public sealed class ApiQueryTests
{
    [Fact]
    public void A_path_with_no_pairs_is_unchanged()
    {
        ApiQuery.Build("api/public/kb/x/search").ShouldBe("api/public/kb/x/search");
    }

    [Fact]
    public void Pairs_are_joined_in_order_and_a_null_value_is_left_out()
    {
        ApiQuery.Build("api/x", ("q", "reset"), ("category", null), ("pageSize", "5")).ShouldBe("api/x?q=reset&pageSize=5");
    }

    [Fact]
    public void Every_value_is_escaped_so_it_cannot_add_a_parameter_or_a_fragment()
    {
        ApiQuery.Build("api/x", ("q", "a&b=c d#e?f+g")).ShouldBe("api/x?q=a%26b%3Dc%20d%23e%3Ff%2Bg");
    }

    [Fact]
    public void A_name_is_escaped_too()
    {
        ApiQuery.Build("api/x", ("a&b", "1")).ShouldBe("api/x?a%26b=1");
    }

    [Fact]
    public void Only_null_values_leave_the_bare_path()
    {
        ApiQuery.Build("api/x", ("q", null)).ShouldBe("api/x");
    }

    [Fact]
    public void Non_ascii_text_is_escaped_as_utf8()
    {
        ApiQuery.Build("api/x", ("q", "caf" + (char)0xE9)).ShouldBe("api/x?q=caf%C3%A9");
    }
}
```

`tests/TechStrap.Portal.Tests/Clients/AttachmentFileNameTests.cs` (new)

```csharp
using System.Globalization;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// Review Focus 3: a file name comes from the visitor's browser and goes into a multipart part header. <c>MultipartFormDataContent.Add</c> throws on an empty name, and the API connection does not map an
/// <see cref="ArgumentException"/>, so whatever the browser sent must come out as a safe, non-empty name. The Portal's own copy of the Admin's rule, with one addition: a path (some browsers send the whole
/// path) is cut to its last segment before the separators are removed.
/// </summary>
public sealed class AttachmentFileNameTests
{
    private static string RightToLeftOverride => ((char)0x202E).ToString();

    private static string ZeroWidthSpace => ((char)0x200B).ToString();

    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("  report final.pdf  ", "report final.pdf")]
    [InlineData("C:\\Users\\jo\\Desktop\\report.pdf", "report.pdf")]
    [InlineData("/home/jo/report.pdf", "report.pdf")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("a\"b'c.txt", "abc.txt")]
    [InlineData("tab\there\r\n.txt", "tabhere.txt")]
    public void A_name_is_cut_to_its_last_path_segment_and_stripped_of_quotes_and_control_characters(string name, string expected) =>
        AttachmentFileName.Clean(name).ShouldBe(expected);

    [Fact]
    public void Unicode_format_characters_such_as_the_right_to_left_override_are_removed_and_the_extension_survives()
    {
        var spoofed = "invoice" + RightToLeftOverride + "fdp.exe" + ZeroWidthSpace;

        var cleaned = AttachmentFileName.Clean(spoofed);

        cleaned.ShouldBe("invoicefdp.exe");
        cleaned.ShouldAllBe(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    [InlineData("\\\\")]
    [InlineData("\"'\"")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/..")]
    public void A_name_that_ends_up_empty_or_only_dots_becomes_the_fallback(string? name) =>
        AttachmentFileName.Clean(name).ShouldBe(AttachmentFileName.Fallback);

    [Fact]
    public void The_fallback_is_a_plain_name()
    {
        AttachmentFileName.Fallback.ShouldBe("attachment");
    }
}
```

`tests/TechStrap.Portal.Tests/Clients/CustomerTicketClientTests.cs` (new)

```csharp
using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Contracts.Tickets;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// P09-T02, the customer's calls. The token is the <c>X-Ticket-Token</c> header of each request and nowhere else (Review Focus 1). A view is a read (retried); a reply is a write (never retried, multipart);
/// the lost-link request carries no token and no product (the API route takes none). Every call forwards the visitor's address (Review Focus 5).
/// </summary>
public sealed class CustomerTicketClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly string Text = "Zk9_-" + new string('q', TicketToken.Length - 5);
    private const string TicketPath = "/api/customer/ticket";
    private const string ReplyPath = "/api/customer/ticket/replies";
    private const string LinkPath = "/api/customer/access-link";

    private static TicketToken Token()
    {
        TicketToken.TryParse(Text, out var token).ShouldBeTrue();
        return token;
    }

    private static CustomerTicketDto Ticket() => new(
        "PAP-42", "paperplane", "Printer jam", "Open", new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
        [new CustomerMessageDto(Guid.Parse("11111111-2222-3333-4444-555555555555"), "Agent", "Sam from Paperplane Support", "<p>Hello</p>", new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero), [])]);

    [Fact]
    public async Task A_view_sends_the_token_in_the_header_through_the_read_client_and_returns_the_ticket_with_its_product_key()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, TicketPath, Ticket());

        var result = await api.Get<ICustomerTicketClient>().GetAsync(Token(), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ProductKey.ShouldBe("paperplane");
        result.Value.Messages.ShouldHaveSingleItem().AuthorDisplayName.ShouldBe("Sam from Paperplane Support");
        var sent = api.Stub.Requests.ShouldHaveSingleItem();
        sent.TicketToken.ShouldBe(Text);
        sent.Client.ShouldBe(ApiClientNames.Read);
        sent.Query.ShouldBeEmpty();
        sent.Path.ShouldNotContain(Text);
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task A_view_that_the_api_refuses_is_the_uniform_not_found_whatever_the_reason()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnProblem(HttpMethod.Get, TicketPath, HttpStatusCode.NotFound, "token-expired", "This token expired yesterday.");

        var result = await api.Get<ICustomerTicketClient>().GetAsync(Token(), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
    }

    [Fact]
    public async Task A_view_is_retried_on_a_503_and_every_attempt_carries_the_token()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, TicketPath, HttpStatusCode.ServiceUnavailable);

        var result = await api.Get<ICustomerTicketClient>().GetAsync(Token(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Get, TicketPath).ShouldBe(1 + ApiClientRegistration.ReadRetryCount);
        api.Stub.Requests.ShouldAllBe(r => r.TicketToken == Text);
    }

    [Fact]
    public async Task A_reply_is_a_multipart_post_through_the_write_client_with_the_token_header_only()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Post, ReplyPath, new CustomerReplyResponse("PAP-42", Guid.NewGuid(), false, null), HttpStatusCode.Created);
        var file = new AttachmentUpload("shot.png", "image/png", () => new MemoryStream([1, 2, 3]));

        var result = await api.Get<ICustomerTicketClient>().ReplyAsync(Token(), new CustomerReply("Still broken.", [file]), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FollowUpCreated.ShouldBeFalse();
        var sent = api.Stub.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.Client.ShouldBe(ApiClientNames.Write);
        sent.TicketToken.ShouldBe(Text);
        sent.ContentType.ShouldNotBeNull().ShouldStartWith("multipart/form-data");
        var body = sent.Body.ShouldNotBeNull();
        body.ShouldContain("name=Body");
        body.ShouldContain("Still broken.");
        body.ShouldContain("name=Attachments; filename=shot.png");
        body.ShouldNotContain(Text, Case.Sensitive, "the token is a header, never a form field");
        sent.Path.ShouldNotContain(Text);
        sent.Query.ShouldBeEmpty();
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task A_reply_on_a_closed_ticket_returns_the_follow_up_link_untouched()
    {
        using var api = ApiHarness.Create();
        const string Link = "https://help.example.com/t/AbC-_0123456789AbC-_0123456789AbC-_01234567";
        api.Stub.OnJson(HttpMethod.Post, ReplyPath, new CustomerReplyResponse("PAP-43", Guid.NewGuid(), true, Link), HttpStatusCode.Created);

        var result = await api.Get<ICustomerTicketClient>().ReplyAsync(Token(), new CustomerReply("Back again.", []), Ct);

        result.Value.FollowUpCreated.ShouldBeTrue();
        result.Value.FollowUpViewUrl.ShouldBe(Link);
    }

    [Fact]
    public async Task A_409_is_the_reply_conflict_error_with_its_own_copy()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnProblem(HttpMethod.Post, ReplyPath, HttpStatusCode.Conflict, "reply-conflict", "Your reply could not be saved. Please try again.");

        var result = await api.Get<ICustomerTicketClient>().ReplyAsync(Token(), new CustomerReply("x", []), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ReplyConflict, ProblemCopy.ReplyConflict, ResultErrorKind.Conflict));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType)]
    [InlineData(HttpStatusCode.NotFound, ApiErrorCodes.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited)]
    public async Task A_reply_failure_is_mapped_by_the_status_alone(HttpStatusCode status, string code)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnProblem(HttpMethod.Post, ReplyPath, status, "x", "detail");

        var result = await api.Get<ICustomerTicketClient>().ReplyAsync(Token(), new CustomerReply("x", []), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(code);
    }

    [Fact]
    public async Task A_reply_is_never_retried()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Post, ReplyPath, HttpStatusCode.ServiceUnavailable);

        var result = await api.Get<ICustomerTicketClient>().ReplyAsync(Token(), new CustomerReply("x", []), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Post, ReplyPath).ShouldBe(1);
    }

    [Fact]
    public async Task A_lost_link_request_is_a_json_post_through_the_write_client_with_no_token_and_no_product()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Post, LinkPath, HttpStatusCode.Accepted);

        var result = await api.Get<ICustomerTicketClient>().RequestAccessLinkAsync("ada@example.com", Ct);

        result.IsSuccess.ShouldBeTrue();
        var sent = api.Stub.Requests.ShouldHaveSingleItem();
        sent.Client.ShouldBe(ApiClientNames.Write);
        sent.TicketToken.ShouldBeNull();
        sent.ContentType.ShouldNotBeNull().ShouldStartWith("application/json");
        JsonDocument.Parse(sent.Body!).RootElement.GetProperty("email").GetString().ShouldBe("ada@example.com");
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task A_malformed_address_is_a_field_error_with_the_apis_code_and_a_429_is_rate_limited()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Post, LinkPath, _ => StubApiHandler.ValidationProblem("email", "email-invalid", "Enter a valid email address."));

        var invalid = await api.Get<ICustomerTicketClient>().RequestAccessLinkAsync("not an address", Ct);
        api.Stub.OnProblem(HttpMethod.Post, LinkPath, HttpStatusCode.TooManyRequests, "rate-limited", "slow down");
        var limited = await api.Get<ICustomerTicketClient>().RequestAccessLinkAsync("ada@example.com", Ct);

        invalid.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError("email-invalid", "Enter a valid email address.", ResultErrorKind.Validation, "email"));
        limited.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.RateLimited);
    }

    [Fact]
    public async Task A_lost_link_request_is_not_retried()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Post, LinkPath, HttpStatusCode.BadGateway);

        await api.Get<ICustomerTicketClient>().RequestAccessLinkAsync("ada@example.com", Ct);

        api.Stub.Count(HttpMethod.Post, LinkPath).ShouldBe(1);
    }
}
```

`tests/TechStrap.Portal.Tests/Clients/MultipartFormTests.cs` (new)

```csharp
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// The Portal's multipart body for a new ticket and for a reply. The text fields come first, each file is a part named <c>Attachments</c> with its cleaned name and its own content type (octet-stream when the
/// browser's is unusable), the form owns the file streams, and a failure half way through opens nothing it does not close.
/// </summary>
public sealed class MultipartFormTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Closed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }

    private static AttachmentUpload Upload(string name, string? type, Stream stream) => new(name, type, () => stream);

    [Fact]
    public async Task Text_fields_and_files_become_parts_with_the_api_field_names()
    {
        using var form = MultipartForm.Build(
            [new("Email", "ada@example.com"), new("Subject", "Help")],
            [Upload("log.txt", "text/plain", new MemoryStream("hello"u8.ToArray()))]);

        var text = await form.ReadAsStringAsync(Ct);

        text.ShouldContain("name=Email");
        text.ShouldContain("ada@example.com");
        text.ShouldContain("name=Subject");
        text.ShouldContain("name=Attachments; filename=log.txt");
        text.ShouldContain("Content-Type: text/plain");
        text.ShouldContain("hello");
        text.IndexOf("name=Subject", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("name=Attachments", StringComparison.Ordinal), "the text fields come first");
    }

    [Fact]
    public async Task A_field_with_no_value_is_left_out()
    {
        using var form = MultipartForm.Build([new("Name", "Ada"), new("Website", null)], []);

        var text = await form.ReadAsStringAsync(Ct);

        text.ShouldContain("name=Name");
        text.ShouldNotContain("name=Website");
    }

    [Fact]
    public async Task A_file_name_is_cleaned_and_an_unusable_content_type_is_octet_stream()
    {
        using var form = MultipartForm.Build([], [Upload("C:\\fakepath\\a\"b.bin", "not a type", new MemoryStream([1, 2, 3]))]);

        var text = await form.ReadAsStringAsync(Ct);

        text.ShouldContain("filename=ab.bin");
        text.ShouldNotContain("fakepath");
        text.ShouldContain("Content-Type: application/octet-stream");
    }

    [Fact]
    public void A_missing_content_type_is_octet_stream_too()
    {
        using var form = MultipartForm.Build([], [Upload("a.bin", null, new MemoryStream([1]))]);

        form.ShouldHaveSingleItem().Headers.ContentType!.MediaType.ShouldBe("application/octet-stream");
    }

    [Fact]
    public void An_empty_name_never_reaches_the_multipart_content_which_would_throw_on_it()
    {
        var build = () => MultipartForm.Build([], [Upload(string.Empty, "text/plain", new MemoryStream([1]))]);

        using var form = build();

        form.ShouldHaveSingleItem().Headers.ContentDisposition!.FileName.ShouldBe(AttachmentFileName.Fallback);
    }

    [Fact]
    public void Disposing_the_form_closes_the_file_streams()
    {
        var stream = new TrackingStream([1, 2, 3]);
        var form = MultipartForm.Build([], [Upload("a.bin", "application/octet-stream", stream)]);

        stream.Closed.ShouldBeFalse("the stream stays open until the request has been sent");
        form.Dispose();

        stream.Closed.ShouldBeTrue();
    }

    [Fact]
    public void A_file_that_cannot_be_opened_disposes_the_streams_already_opened_and_the_error_surfaces()
    {
        var first = new TrackingStream([1]);
        var build = () => MultipartForm.Build([], [Upload("a.bin", null, first), new AttachmentUpload("b.bin", null, () => throw new IOException("gone"))]);

        Should.Throw<IOException>(build);

        first.Closed.ShouldBeTrue();
    }
}
```

`tests/TechStrap.Portal.Tests/Clients/ProblemMappingTests.cs`

```diff
@@ -103,7 +103,6 @@ public sealed class ProblemMappingTests
     [Theory]
     [InlineData(401)]
     [InlineData(403)]
-    [InlineData(409)]
     [InlineData(408)]
     [InlineData(418)]
     public async Task Any_other_status_is_a_generic_api_error_with_fixed_copy(int status)
@@ -113,6 +112,15 @@ public sealed class ProblemMappingTests
         errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ApiError, ProblemCopy.ApiError, ResultErrorKind.Failure));
     }
 
+    [Fact]
+    public async Task A_409_is_the_reply_conflict_with_its_own_fixed_copy_whatever_the_api_said()
+    {
+        var errors = await MapAsync(StubApiHandler.Problem(HttpStatusCode.Conflict, "reply-conflict", "internal detail: rowversion 17 != 18"));
+
+        errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ReplyConflict, ProblemCopy.ReplyConflict, ResultErrorKind.Conflict));
+        errors[0].Message.ShouldNotContain("rowversion");
+    }
+
     [Theory]
     [InlineData("")]
     [InlineData("not json")]
@@ -134,7 +142,7 @@ public sealed class ProblemMappingTests
     [Fact]
     public void Every_fixed_message_is_plain_text_a_visitor_can_act_on()
     {
-        foreach (var message in new[] { ProblemCopy.Invalid, ProblemCopy.NotFound, ProblemCopy.PayloadTooLarge, ProblemCopy.UnsupportedMediaType, ProblemCopy.RateLimited, ProblemCopy.ApiUnavailable, ProblemCopy.ApiError, ProblemCopy.UnexpectedResponse })
+        foreach (var message in new[] { ProblemCopy.Invalid, ProblemCopy.NotFound, ProblemCopy.PayloadTooLarge, ProblemCopy.UnsupportedMediaType, ProblemCopy.RateLimited, ProblemCopy.ApiUnavailable, ProblemCopy.ApiError, ProblemCopy.UnexpectedResponse, ProblemCopy.ReplyConflict })
         {
             message.ShouldNotBeNullOrWhiteSpace();
             message.ShouldNotContain("<");
```

`tests/TechStrap.Portal.Tests/Clients/PublicKbClientTests.cs` (new)

```csharp
using System.Net;
using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>P09-T02, the KB search call behind the contact page's suggestions (09b); 09c extends the client. A read: retried, anonymous, forwarding the visitor's address.</summary>
public sealed class PublicKbClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Path = "/api/public/kb/paperplane/search";

    private static PagedResponse<PublicKbSearchResultDto> Page() =>
        new([new PublicKbSearchResultDto("reset-password", "Reset your password", "Use the reset link.", "accounts", "Accounts", "paperplane")], 1, 5, 1);

    [Fact]
    public async Task A_search_is_a_read_with_the_text_and_page_size_in_the_query_and_the_visitors_address()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, Path, Page());

        var result = await api.Get<IPublicKbClient>().SearchAsync("paperplane", "reset password", 5, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Page(), new PagedComparer());
        var sent = api.Stub.Requests.ShouldHaveSingleItem();
        sent.Client.ShouldBe(ApiClientNames.Read);
        sent.Query.ShouldBe("?q=reset%20password&pageSize=5");
        sent.TicketToken.ShouldBeNull();
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task The_visitors_text_is_escaped_so_it_cannot_add_a_parameter()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, Path, Page());

        await api.Get<IPublicKbClient>().SearchAsync("paperplane", "a&category=secret#x", 5, Ct);

        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe("?q=a%26category%3Dsecret%23x&pageSize=5");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Paperplane")]
    [InlineData("../admin")]
    [InlineData("paperplane/kb")]
    public async Task A_key_that_is_not_a_slug_is_not_found_and_no_call_is_made(string? key)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, Path, Page());

        var result = await api.Get<IPublicKbClient>().SearchAsync(key!, "x", 5, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
        api.Stub.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_429_is_rate_limited_and_a_503_is_retried_then_unavailable()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, Path, HttpStatusCode.TooManyRequests);
        var limited = await api.Get<IPublicKbClient>().SearchAsync("paperplane", "x", 5, Ct);
        api.Stub.OnStatus(HttpMethod.Get, Path, HttpStatusCode.ServiceUnavailable);
        var down = await api.Get<IPublicKbClient>().SearchAsync("paperplane", "x", 5, Ct);

        limited.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.RateLimited);
        down.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Get, Path).ShouldBe(1 + 1 + ApiClientRegistration.ReadRetryCount);
    }

    private sealed class PagedComparer : IEqualityComparer<PagedResponse<PublicKbSearchResultDto>>
    {
        public bool Equals(PagedResponse<PublicKbSearchResultDto>? x, PagedResponse<PublicKbSearchResultDto>? y) =>
            x is not null && y is not null && x.Page == y.Page && x.PageSize == y.PageSize && x.TotalCount == y.TotalCount && x.Items.SequenceEqual(y.Items);

        public int GetHashCode(PagedResponse<PublicKbSearchResultDto> obj) => obj.TotalCount;
    }
}
```

`tests/TechStrap.Portal.Tests/Clients/PublicTicketClientTests.cs` (new)

```csharp
using System.Net;
using SyntaxCircus.Common;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// P09-T02, the contact form's call: a multipart POST to the product's public intake endpoint. It goes through the write client (a retry could duplicate a ticket), forwards the visitor's address, sends no
/// ticket token, passes the honeypot through to the API (D-045 addendum) and maps every answer to a Result.
/// </summary>
public sealed class PublicTicketClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Path = "/api/public/products/paperplane/tickets";

    private static NewTicketRequest Request(string? website = null, params AttachmentUpload[] files) =>
        new("ada@example.com", "Ada Lovelace", "Printer jam", "It jams every time.", website, files);

    private static SubmitTicketResponse Created() => new("PAP-42", null, []);

    [Fact]
    public async Task A_submission_is_a_multipart_post_through_the_write_client_with_every_field_and_file()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Post, Path, Created(), HttpStatusCode.Created);
        var file = new AttachmentUpload("log.txt", "text/plain", () => new MemoryStream("the log"u8.ToArray()));

        var result = await api.Get<IPublicTicketClient>().SubmitAsync("paperplane", Request(null, file), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TicketNumber.ShouldBe("PAP-42");
        var sent = api.Stub.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.Client.ShouldBe(ApiClientNames.Write);
        sent.ContentType.ShouldNotBeNull().ShouldStartWith("multipart/form-data");
        sent.TicketToken.ShouldBeNull("a visitor with no ticket sends no token");
        sent.Query.ShouldBeEmpty();
        var body = sent.Body.ShouldNotBeNull();
        body.ShouldContain("name=Email");
        body.ShouldContain("ada@example.com");
        body.ShouldContain("name=Name");
        body.ShouldContain("Ada Lovelace");
        body.ShouldContain("name=Subject");
        body.ShouldContain("Printer jam");
        body.ShouldContain("name=Body");
        body.ShouldContain("It jams every time.");
        body.ShouldContain("name=Attachments; filename=log.txt");
        body.ShouldContain("the log");
        body.ShouldNotContain("name=Website", Case.Sensitive, "no honeypot value, no field");
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task A_filled_honeypot_is_passed_through_to_the_api_and_the_answer_is_the_apis_own()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Post, Path, Created(), HttpStatusCode.Created);

        var result = await api.Get<IPublicTicketClient>().SubmitAsync("paperplane", Request("http://spam.example"), Ct);

        result.IsSuccess.ShouldBeTrue();
        var body = api.Stub.Requests.ShouldHaveSingleItem().Body.ShouldNotBeNull();
        body.ShouldContain("name=Website");
        body.ShouldContain("http://spam.example");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Paperplane")]
    [InlineData("../admin")]
    [InlineData("paperplane/tickets")]
    [InlineData("paperplane?x=1")]
    public async Task A_key_that_is_not_a_slug_is_not_found_and_no_call_is_made(string? key)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Post, Path, Created(), HttpStatusCode.Created);

        var result = await api.Get<IPublicTicketClient>().SubmitAsync(key!, Request(), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
        api.Stub.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Field_errors_keep_the_apis_codes_and_targets()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Post, Path, _ => StubApiHandler.ValidationProblem(
            [("email", "email-invalid", "Enter a valid email address."), ("attachments", "attachments-too-many", "Attach at most 5 files.")]));

        var result = await api.Get<IPublicTicketClient>().SubmitAsync("paperplane", Request(), Ct);

        result.Errors.Select(e => (e.Code, e.Target)).ShouldBe([("email-invalid", "email"), ("attachments-too-many", "attachments")]);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ApiErrorCodes.NotFound)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, ApiErrorCodes.ApiUnavailable)]
    public async Task Every_other_status_is_mapped_by_the_status_alone(HttpStatusCode status, string code)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnProblem(HttpMethod.Post, Path, status, "x", "Npgsql host=10.0.0.5");

        var result = await api.Get<IPublicTicketClient>().SubmitAsync("paperplane", Request(), Ct);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(code);
        error.Message.ShouldNotContain("Npgsql");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_submission_is_never_retried_whatever_the_failure(HttpStatusCode status)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Post, Path, status);

        var result = await api.Get<IPublicTicketClient>().SubmitAsync("paperplane", Request(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Post, Path).ShouldBe(1, "a retry could create the ticket twice");
    }

    [Fact]
    public async Task A_transport_failure_is_one_call_and_api_unavailable()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Post, Path, _ => throw new HttpRequestException("No connection could be made (10.1.2.3:5432)"));

        var result = await api.Get<IPublicTicketClient>().SubmitAsync("paperplane", Request(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Post, Path).ShouldBe(1);
    }

    [Fact]
    public async Task The_files_are_not_opened_for_a_key_that_is_refused_before_the_call()
    {
        using var api = ApiHarness.Create();
        var opened = false;
        var file = new AttachmentUpload("a.txt", "text/plain", () =>
        {
            opened = true;
            return new MemoryStream();
        });

        await api.Get<IPublicTicketClient>().SubmitAsync("Not A Slug", Request(null, file), Ct);

        opened.ShouldBeFalse();
    }
}
```

`tests/TechStrap.Portal.Tests/Headers/FormPageHeaderHostTests.cs` (new)

```csharp
using System.Net;

namespace TechStrap.Portal.Tests.Headers;

/// <summary>
/// D-045 addendum (09b): the four form pages of a product (contact, the received page, lost link and the suggest adapter) are never indexed and never stored, because their address can carry a visitor's name, email
/// address and subject, and the page shows what they typed. The rule is a header rule, so it applies to whatever the Portal answers on those paths: these tests assert the final response whatever its status (an
/// unknown product is a 404 here, and the page tests of 09b assert the same headers on the real 200s).
/// </summary>
public sealed class FormPageHeaderHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static string[] Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? [.. values] : [];

    [Theory]
    [InlineData("/p/probe/contact")]
    [InlineData("/p/probe/contact?subject=Printer&name=Jane&email=jane%40example.com")]
    [InlineData("/p/probe/contact/received?ref=x")]
    [InlineData("/p/probe/lost-link")]
    [InlineData("/p/probe/suggest?q=printer")]
    [InlineData("/P/Probe/CONTACT")]
    public async Task A_form_page_is_noindex_and_no_store_whatever_the_status(string path)
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        Header(response, "X-Robots-Tag").ShouldBe(["noindex"], path);
        Header(response, "Cache-Control").ShouldBe(["no-store"], path);
        Header(response, "Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"], "only the ticket pages send no referrer");
        Header(response, "X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    [Theory]
    [InlineData("/p/probe")]
    [InlineData("/p/probe/kb")]
    [InlineData("/p/probe/kb/search?q=x")]
    [InlineData("/not-found")]
    [InlineData("/error")]
    public async Task Other_pages_are_not_marked_noindex_by_the_form_page_rule(string path)
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        Header(response, "X-Robots-Tag").ShouldBeEmpty(path);
        Header(response, "Cache-Control").ShouldNotContain("no-store", path);
        response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError, path);
    }
}
```

`tests/TechStrap.Portal.Tests/Headers/PortalHeaderRulesTests.cs`

```diff
@@ -1,5 +1,6 @@
 using Microsoft.AspNetCore.Http;
 using TechStrap.Portal.Headers;
+using TechStrap.Portal.Routing;
 
 namespace TechStrap.Portal.Tests.Headers;
 
@@ -52,10 +53,58 @@ public sealed class PortalHeaderRulesTests
     [InlineData("/p/paperplane/attachments/" + Id)]
     public void The_ticket_page_and_everything_else_is_not(string path) => PortalHeaderRules.IsTicketAttachmentPath(path).ShouldBeFalse(path);
 
+    [Theory]
+    [InlineData("/p/paperplane/contact")]
+    [InlineData("/p/paperplane/contact/")]
+    [InlineData("/P/Paperplane/CONTACT")]
+    [InlineData("/p/paperplane/contact/received")]
+    [InlineData("/p/paperplane/contact/received/")]
+    [InlineData("/p/paperplane/Contact/Received")]
+    [InlineData("/p/paperplane/lost-link")]
+    [InlineData("/p/paperplane/LOST-LINK/")]
+    [InlineData("/p/paperplane/suggest")]
+    [InlineData("/p/paperplane/Suggest/")]
+    [InlineData("/p//paperplane/contact")]
+    public void The_four_form_pages_of_a_product_are_form_pages(string path) => PortalHeaderRules.IsFormPagePath(path).ShouldBeTrue(path);
+
+    [Theory]
+    [InlineData("/")]
+    [InlineData("/p")]
+    [InlineData("/p/")]
+    [InlineData("/p/paperplane")]
+    [InlineData("/p/contact")]
+    [InlineData("/p/paperplane/contact/extra")]
+    [InlineData("/p/paperplane/contact/received/more")]
+    [InlineData("/p/paperplane/received")]
+    [InlineData("/p/paperplane/lost-link/more")]
+    [InlineData("/p/paperplane/lostlink")]
+    [InlineData("/p/paperplane/suggest/more")]
+    [InlineData("/p/paperplane/kb")]
+    [InlineData("/p/paperplane/kb/suggest")]
+    [InlineData("/p/paperplane/kb/search")]
+    [InlineData("/p/paperplane/kb/guides/contact")]
+    [InlineData("/contact")]
+    [InlineData("/t/x/contact")]
+    [InlineData("/pp/paperplane/contact")]
+    [InlineData("")]
+    public void The_product_home_the_kb_and_every_other_path_are_not(string path) => PortalHeaderRules.IsFormPagePath(path).ShouldBeFalse(path);
+
+    [Fact]
+    public void The_segments_of_the_form_pages_are_the_ones_the_route_templates_end_with()
+    {
+        PortalRoutes.ContactTemplate.ShouldEndWith("/" + PortalRoutes.ContactSegment);
+        PortalRoutes.ContactReceivedTemplate.ShouldEndWith("/" + PortalRoutes.ContactSegment + "/" + PortalRoutes.ReceivedSegment);
+        PortalRoutes.LostLinkTemplate.ShouldEndWith("/" + PortalRoutes.LostLinkSegment);
+        PortalRoutes.SuggestTemplate.ShouldEndWith("/" + PortalRoutes.SuggestSegment);
+    }
+
     [Fact]
-    public void The_rules_are_the_ticket_headers_and_the_attachment_sandbox_and_nothing_else()
+    public void The_rules_are_the_ticket_headers_the_attachment_sandbox_and_the_form_page_headers_and_nothing_else()
     {
-        PortalHeaderRules.Rules.Count.ShouldBe(2);
+        PortalHeaderRules.Rules.Count.ShouldBe(3);
+        PortalHeaderRules.Rules[2].Matches(new PathString("/p/x/contact")).ShouldBeTrue();
+        PortalHeaderRules.Rules[2].Matches(new PathString("/p/x")).ShouldBeFalse();
+        PortalHeaderRules.Rules[2].Matches(new PathString("/t/x")).ShouldBeFalse();
         PortalHeaderRules.Rules[0].Matches(new PathString("/t/x")).ShouldBeTrue();
         PortalHeaderRules.Rules[0].Matches(new PathString("/p/x")).ShouldBeFalse();
         PortalHeaderRules.Rules[1].Matches(new PathString("/t/" + Token + "/attachments/" + Id)).ShouldBeTrue();
```

`tests/TechStrap.Portal.Tests/Headers/TicketHeaderHostTests.cs`

```diff
@@ -99,7 +99,7 @@ public sealed class TicketHeaderHostTests
     }
 
     [Theory]
-    [InlineData("/p/paperplane/contact", HttpStatusCode.NotFound)]
+    [InlineData("/p/paperplane/kb/search", HttpStatusCode.NotFound)]
     [InlineData("/p/paperplane/kb/guides/dark-mode", HttpStatusCode.NotFound)]
     [InlineData("/no-such-page", HttpStatusCode.NotFound)]
     [InlineData("/not-found", HttpStatusCode.OK)]
```

`tests/TechStrap.Portal.Tests/Logging/RequestLogRedactionHostTests.cs`

```diff
@@ -54,13 +54,34 @@ public sealed class RequestLogRedactionHostTests
         response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound, "the contact page arrives in PHASE-09b; the request is logged all the same");
         AssertVerboseWasCaptured(factory);
         factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("name=[redacted]", StringComparison.Ordinal), "control: the query string was logged, with the name masked");
+        factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("subject=[redacted]", StringComparison.Ordinal), "control: the subject was masked too");
         factory.LogSink.Events.Select(Everything).ShouldAllBe(text =>
-            !text.Contains("Jane", StringComparison.OrdinalIgnoreCase)
+            !text.Contains("Printer", StringComparison.Ordinal)
+            && !text.Contains("Jane", StringComparison.OrdinalIgnoreCase)
             && !text.Contains("Doe", StringComparison.Ordinal)
             && !text.Contains("jane.doe", StringComparison.OrdinalIgnoreCase)
             && !text.Contains("example.com", StringComparison.OrdinalIgnoreCase));
     }
 
+    [Fact]
+    public async Task The_received_reference_and_the_suggest_text_never_reach_a_log_event()
+    {
+        await using var factory = Verbose();
+        using var client = factory.CreateClient();
+
+        using var received = await client.GetAsync("/p/paperplane/contact/received?ref=CfDJ8SECRETREFERENCEVALUE", Ct);
+        using var suggest = await client.GetAsync("/p/paperplane/suggest?q=jane.doe%27s+private+printer", Ct);
+
+        AssertVerboseWasCaptured(factory);
+        factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("ref=[redacted]", StringComparison.Ordinal), "control: the reference was logged, masked");
+        factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("q=[redacted]", StringComparison.Ordinal), "control: the suggest text was logged, masked");
+        factory.LogSink.Events.Select(Everything).ShouldAllBe(text =>
+            !text.Contains("SECRETREFERENCE", StringComparison.Ordinal)
+            && !text.Contains("private printer", StringComparison.OrdinalIgnoreCase)
+            && !text.Contains("private+printer", StringComparison.OrdinalIgnoreCase)
+            && !text.Contains("jane.doe", StringComparison.OrdinalIgnoreCase));
+    }
+
     [Theory]
     [InlineData("/p/paperplane?name=Jane+Doe")]
     [InlineData("/p/paperplane?NAME=Jane%20Doe&EMAIL=jane%40example.com")]
```


- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/TechStrap.Portal.Tests -c Release`
Expected: FAIL to compile, 4 errors, the first of them `error CS0246: The type or namespace name 'AttachmentUpload' could not be found` (`MultipartFormTests.cs`), then `NewTicketRequest` and `SubmitTicketResponse` (`PublicTicketClientTests.cs`; the compiler reports the missing types first and the missing members once they exist).

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/PiiRedactionQueryValueTests/*"`
Expected: FAIL: `total: 30, failed: 8` (`subject`, `ref` and `q` are not masked yet).

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/SensitiveQuerySentryProcessorTests/*"`
Expected: FAIL: `total: 62, failed: 5` (`subject` and `ref`).

- [ ] **Step 3: Implement the plumbing**

The file-name cleaner, the multipart builder, the query builder and the download are small and pure. `OpenStreamAsync` is the one streaming call: it sets the token on the request, goes through the retrying read client with `ResponseHeadersRead`, maps every failure to a `Result` like any call, and hands the live response to the caller inside an `ApiDownload` that disposes the body, the response and the request. A cancellation by the caller still propagates.

`src/TechStrap.Portal/Clients/AttachmentUpload.cs` (new)

```csharp
namespace TechStrap.Portal.Clients;

/// <summary>
/// One file to send on with a new ticket or a reply. <see cref="OpenRead"/> is called when the multipart body is built (never before the product key or token has been checked), and the stream it returns is
/// owned by that body: it is closed when the request is disposed, so the file is read once, as it is sent, and is never held in memory by the Portal.
/// </summary>
public sealed record AttachmentUpload(string FileName, string? ContentType, Func<Stream> OpenRead);
```

`src/TechStrap.Portal/Clients/AttachmentFileName.cs` (new)

```csharp
using System.Globalization;

namespace TechStrap.Portal.Clients;

/// <summary>
/// Makes a browser-supplied file name safe to put in a multipart part (the Portal's own copy of the Admin's rule). <c>MultipartFormDataContent.Add</c> throws on an empty name and <see cref="ApiConnection"/>
/// does not map an <see cref="ArgumentException"/>, so an odd name must never reach it. Some browsers send the whole path, so the name is first cut to its last path segment; then quotes, control characters and
/// Unicode format characters (such as the right-to-left override, which makes <c>fdp.exe</c> read as <c>exe.pdf</c>) are removed and the extension is kept. A name that ends up empty, or only dots, becomes
/// <see cref="Fallback"/>.
/// </summary>
internal static class AttachmentFileName
{
    public const string Fallback = "attachment";

    public static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fallback;
        }

        var lastSeparator = name.LastIndexOfAny(['/', '\\']);
        var segment = lastSeparator >= 0 ? name[(lastSeparator + 1)..] : name;
        var cleaned = string.Concat(segment.Where(c => !char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format && c is not ('"' or '\''))).Trim();
        return cleaned.Length == 0 || cleaned.All(c => c == '.') ? Fallback : cleaned;
    }
}
```

`src/TechStrap.Portal/Clients/MultipartForm.cs` (new)

```csharp
using System.Net.Http.Headers;

namespace TechStrap.Portal.Clients;

/// <summary>
/// The multipart body of a new ticket and of a reply: the text fields first (a field with no value is left out), then one <c>Attachments</c> part per file with its cleaned name and its own content type
/// (octet-stream when the browser's is missing or unusable). The returned content owns the file streams: disposing it closes them. If a file cannot be opened the streams already opened are closed and the
/// error surfaces, so nothing leaks.
/// </summary>
internal static class MultipartForm
{
    /// <summary>The name of the file parts, as both intake endpoints bind them.</summary>
    public const string AttachmentsField = "Attachments";

    private static readonly MediaTypeHeaderValue OctetStream = new("application/octet-stream");

    public static MultipartFormDataContent Build(IEnumerable<KeyValuePair<string, string?>> fields, IReadOnlyList<AttachmentUpload> attachments)
    {
        var form = new MultipartFormDataContent();
        try
        {
            foreach (var (name, value) in fields)
            {
                if (value is not null)
                {
                    form.Add(new StringContent(value), name);
                }
            }

            foreach (var attachment in attachments)
            {
                var file = new StreamContent(attachment.OpenRead());
                file.Headers.ContentType = MediaTypeHeaderValue.TryParse(attachment.ContentType, out var type) ? type : OctetStream;
                form.Add(file, AttachmentsField, AttachmentFileName.Clean(attachment.FileName));
            }

            return form;
        }
        catch
        {
            form.Dispose();
            throw;
        }
    }
}
```

`src/TechStrap.Portal/Clients/ApiQuery.cs` (new)

```csharp
namespace TechStrap.Portal.Clients;

/// <summary>A path with a query string for an API call. Every name and value is escaped, so a visitor's text can never add a parameter, end the query or start a fragment; a pair with no value is left out.</summary>
internal static class ApiQuery
{
    public static string Build(string path, params (string Name, string? Value)[] pairs)
    {
        var query = string.Join('&', pairs.Where(p => p.Value is not null).Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.Value!)}"));
        return query.Length == 0 ? path : $"{path}?{query}";
    }
}
```

`src/TechStrap.Portal/Clients/ApiDownload.cs` (new)

```csharp
namespace TechStrap.Portal.Clients;

/// <summary>
/// A file the API is sending, opened as soon as its response headers arrived: <see cref="Body"/> is the live stream (nothing is buffered). The caller copies it to the visitor and must dispose the download, which
/// closes the upstream response. Only the headers the pass-through needs are exposed; the API's own <c>Content-Disposition</c> type is never passed on (the pass-through always forces a download).
/// </summary>
public sealed class ApiDownload : IAsyncDisposable
{
    public const string FallbackContentType = "application/octet-stream";

    private readonly HttpRequestMessage _request;
    private readonly HttpResponseMessage _response;

    internal ApiDownload(HttpRequestMessage request, HttpResponseMessage response, Stream body)
    {
        _request = request;
        _response = response;
        Body = body;
    }

    public Stream Body { get; }

    public string ContentType => _response.Content.Headers.ContentType?.ToString() ?? FallbackContentType;

    public long? ContentLength => _response.Content.Headers.ContentLength;

    /// <summary>The file name the API stored, from <c>Content-Disposition</c>; null when it sent none.</summary>
    public string? FileName
    {
        get
        {
            var disposition = _response.Content.Headers.ContentDisposition;
            return disposition?.FileNameStar ?? disposition?.FileName?.Trim('"');
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Body.DisposeAsync();
        _response.Dispose();
        _request.Dispose();
    }
}
```

`src/TechStrap.Portal/Clients/ApiConnection.cs`

```diff
@@ -53,6 +53,48 @@ internal sealed class ApiConnection(IHttpClientFactory httpClients)
     public Task<Result<T>> SendContentAsync<T>(HttpMethod method, string uri, HttpContent content, TicketToken token, CancellationToken cancellationToken) =>
         SendAsync<T>(WriteClient, new HttpRequestMessage(method, uri) { Content = content }, token, cancellationToken);
 
+    /// <summary>
+    /// GET through the retrying read client as a ticket's customer, returning as soon as the response headers have arrived (<see cref="HttpCompletionOption.ResponseHeadersRead"/>): the body is a live
+    /// stream and is never buffered, so an attachment is copied to the visitor as it comes. On success the caller owns the <see cref="ApiDownload"/> and must dispose it. Every failure is a Result (the status
+    /// decides, as for any call), and a cancellation by the caller propagates.
+    /// </summary>
+    public async Task<Result<ApiDownload>> OpenStreamAsync(string uri, TicketToken token, CancellationToken cancellationToken)
+    {
+        var request = new HttpRequestMessage(HttpMethod.Get, uri);
+        Attach(request, token);
+        HttpResponseMessage? response = null;
+        var handedOver = false;
+        try
+        {
+            response = await ReadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
+            if (!response.IsSuccessStatusCode)
+            {
+                var errors = ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
+                return Result<ApiDownload>.Failure(errors[0], [.. errors.Skip(1)]);
+            }
+
+            var body = await response.Content.ReadAsStreamAsync(cancellationToken);
+            handedOver = true;
+            return Result<ApiDownload>.Success(new ApiDownload(request, response, body));
+        }
+        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
+        {
+            return Result<ApiDownload>.Failure(ProblemMapping.Unexpected());
+        }
+        catch (Exception ex) when (Transport(ex, cancellationToken) is { } error)
+        {
+            return Result<ApiDownload>.Failure(error);
+        }
+        finally
+        {
+            if (!handedOver)
+            {
+                response?.Dispose();
+                request.Dispose();
+            }
+        }
+    }
+
     private static HttpRequestMessage JsonRequest(HttpMethod method, string uri, object? body) =>
         new(method, uri) { Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: Json) };
 
```


- [ ] **Step 4: Implement the clients, the 409 mapping and the registration**

The ticket client builds its multipart body only after the key has been checked (a refused key opens no file). A reply carries the token as the header of that request and the text as a form field, never the token. The lost-link request has no token and no product. The KB client puts the visitor's text in the query through `ApiQuery`.

`src/TechStrap.Portal/Clients/IPublicTicketClient.cs` (new)

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Intake;

namespace TechStrap.Portal.Clients;

/// <summary>
/// A new ticket from the contact form. The text is what the visitor typed (already validated by the page, and validated again by the API, which is the authority); <paramref name="Website"/> is the honeypot field,
/// sent on as it arrived so the API can answer a bot with the same 201 a person gets (D-045 addendum).
/// </summary>
public sealed record NewTicketRequest(string Email, string Name, string Subject, string Body, string? Website, IReadOnlyList<AttachmentUpload> Attachments);

/// <summary>The anonymous intake call of a product's contact form (P09-T02, T06).</summary>
public interface IPublicTicketClient
{
    /// <summary>
    /// Sends the ticket as a multipart POST through the write client (never retried, so a transient failure cannot create two tickets). A key that is not a slug is the uniform not-found error and no call is
    /// made. 400 keeps the API's field codes (<c>email-invalid</c>, <c>attachments-too-many</c> and so on), 413 and 415 are the attachment errors, 429 is rate limited.
    /// </summary>
    Task<Result<SubmitTicketResponse>> SubmitAsync(string productKey, NewTicketRequest request, CancellationToken cancellationToken);
}
```

`src/TechStrap.Portal/Clients/PublicTicketClient.cs` (new)

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Clients;

internal sealed class PublicTicketClient(ApiConnection api) : IPublicTicketClient
{
    public Task<Result<SubmitTicketResponse>> SubmitAsync(string productKey, NewTicketRequest request, CancellationToken cancellationToken)
    {
        if (!ProductKeyShape.IsWellFormed(productKey))
        {
            return Task.FromResult(Result<SubmitTicketResponse>.Failure(ProblemMapping.NotFound()));
        }

        // The form is built only now, so a refused key opens no file. The request disposes it, which closes the file streams.
        var form = MultipartForm.Build(
            [
                new("Email", request.Email),
                new("Name", request.Name),
                new("Subject", request.Subject),
                new("Body", request.Body),
                new("Website", request.Website),
            ],
            request.Attachments);
        return api.SendContentAsync<SubmitTicketResponse>(HttpMethod.Post, $"api/public/products/{productKey}/tickets", form, cancellationToken);
    }
}
```

`src/TechStrap.Portal/Clients/ICustomerTicketClient.cs` (new)

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Portal.Clients;

/// <summary>A reply from the ticket page: the text the customer typed and the files they attached.</summary>
public sealed record CustomerReply(string Body, IReadOnlyList<AttachmentUpload> Attachments);

/// <summary>
/// The calls of a ticket's customer (P09-T02, T08 to T10). The ticket calls take the <see cref="TicketToken"/> from the link, which becomes the <c>X-Ticket-Token</c> header of that one request and appears
/// nowhere else (Review Focus 1). Every failure to see a ticket is the same not-found error, whatever the API's reason (an unknown, expired or revoked token).
/// </summary>
public interface ICustomerTicketClient
{
    /// <summary>The ticket and its public messages, through the retrying read client.</summary>
    Task<Result<CustomerTicketDto>> GetAsync(TicketToken token, CancellationToken cancellationToken);

    /// <summary>
    /// A reply as a multipart POST through the write client (never retried). On a Closed ticket the API starts a follow-up and answers with its link (<see cref="CustomerReplyResponse.FollowUpViewUrl"/>). A 409 is
    /// <see cref="ApiErrorCodes.ReplyConflict"/>.
    /// </summary>
    Task<Result<CustomerReplyResponse>> ReplyAsync(TicketToken token, CustomerReply reply, CancellationToken cancellationToken);

    /// <summary>
    /// Asks for a new link to be emailed (the lost-link page). No token and no product: the API route takes neither. The API answers 202 for any well-formed address; a malformed one is a field error with the code
    /// <c>email-invalid</c> and a 429 is rate limited. The call is never retried.
    /// </summary>
    Task<Result> RequestAccessLinkAsync(string email, CancellationToken cancellationToken);
}
```

`src/TechStrap.Portal/Clients/CustomerTicketClient.cs` (new)

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Portal.Clients;

internal sealed class CustomerTicketClient(ApiConnection api) : ICustomerTicketClient
{
    private const string TicketPath = "api/customer/ticket";

    public Task<Result<CustomerTicketDto>> GetAsync(TicketToken token, CancellationToken cancellationToken) =>
        api.GetAsync<CustomerTicketDto>(TicketPath, token, cancellationToken);

    public Task<Result<CustomerReplyResponse>> ReplyAsync(TicketToken token, CustomerReply reply, CancellationToken cancellationToken)
    {
        var form = MultipartForm.Build([new("Body", reply.Body)], reply.Attachments);
        return api.SendContentAsync<CustomerReplyResponse>(HttpMethod.Post, $"{TicketPath}/replies", form, token, cancellationToken);
    }

    public Task<Result> RequestAccessLinkAsync(string email, CancellationToken cancellationToken) =>
        api.SendAsync(HttpMethod.Post, "api/customer/access-link", new RequestNewAccessLinkRequest(email), cancellationToken);
}
```

`src/TechStrap.Portal/Clients/IPublicKbClient.cs` (new)

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;

namespace TechStrap.Portal.Clients;

/// <summary>The public knowledge base (P09-T02). 09b needs the search only, for the contact page's suggestions; 09c extends this interface with the categories and the articles.</summary>
public interface IPublicKbClient
{
    /// <summary>
    /// The published articles of the product (and the shared ones) that match the text, best first, <paramref name="pageSize"/> at most. Every text field of a hit is plain text: a consumer encodes it. A key that
    /// is not a slug is the uniform not-found error and no call is made. The API cuts a longer text at <see cref="KbLimits.MaxSearchTextChars"/> and answers a blank text or an unknown product with an empty page.
    /// </summary>
    Task<Result<PagedResponse<PublicKbSearchResultDto>>> SearchAsync(string productKey, string text, int pageSize, CancellationToken cancellationToken);
}
```

`src/TechStrap.Portal/Clients/PublicKbClient.cs` (new)

```csharp
using System.Globalization;
using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Clients;

internal sealed class PublicKbClient(ApiConnection api) : IPublicKbClient
{
    public Task<Result<PagedResponse<PublicKbSearchResultDto>>> SearchAsync(string productKey, string text, int pageSize, CancellationToken cancellationToken) =>
        ProductKeyShape.IsWellFormed(productKey)
            ? api.GetAsync<PagedResponse<PublicKbSearchResultDto>>(
                ApiQuery.Build($"api/public/kb/{productKey}/search", ("q", text), ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture))),
                cancellationToken)
            : Task.FromResult(Result<PagedResponse<PublicKbSearchResultDto>>.Failure(ProblemMapping.NotFound()));
}
```

`src/TechStrap.Portal/Clients/ApiErrorCodes.cs`

```diff
@@ -17,6 +17,9 @@ public static class ApiErrorCodes
 
     public const string UnexpectedResponse = "api-unexpected-response";
 
-    /// <summary>Any other status (401, 403, 409 and so on): the public routes do not answer them, so a page treats it as a failure.</summary>
+    /// <summary>A reply that could not be saved because the ticket changed at the same moment (409): the customer is asked to send it again.</summary>
+    public const string ReplyConflict = "reply-conflict";
+
+    /// <summary>Any other status (401, 403 and so on): the public routes do not answer them, so a page treats it as a failure.</summary>
     public const string ApiError = "api-error";
 }
```

`src/TechStrap.Portal/Clients/ProblemCopy.cs`

```diff
@@ -11,6 +11,7 @@ public static class ProblemCopy
     public const string PayloadTooLarge = "That is too large to send. Remove a file or two and try again.";
     public const string UnsupportedMediaType = "That could not be sent in that form. Reload the page and try again.";
     public const string RateLimited = "You have sent a lot in a short time. Wait a minute and try again.";
+    public const string ReplyConflict = "Your reply could not be saved this time. Your text is still here: send it again.";
     public const string ApiUnavailable = "We could not reach our support system. Try again in a moment.";
     public const string ApiError = "Something went wrong on our side. Try again in a moment.";
     public const string UnexpectedResponse = "We got an answer we did not expect. Try again in a moment.";
```

`src/TechStrap.Portal/Clients/ProblemMapping.cs`

```diff
@@ -7,7 +7,7 @@ namespace TechStrap.Portal.Clients;
 /// <summary>
 /// Turns a non-success API response into <see cref="Result"/> errors (D-045). The API answers with RFC 7807 problem details; a validation failure (400) carries the specific codes in the
 /// <c>errorCodes</c> extension, keyed by field, and the messages in <c>errors</c>. Every other status is mapped by the status alone, to a fixed code and a fixed sentence (<see cref="ProblemCopy"/>):
-/// 404 is one not-found whatever the API called it (so nothing can tell an unknown product from an unknown ticket), 413 and 415 are the attachment errors, 429 is rate limited, and any 5xx is
+/// 404 is one not-found whatever the API called it (so nothing can tell an unknown product from an unknown ticket), 409 is the reply conflict, 413 and 415 are the attachment errors, 429 is rate limited, and any 5xx is
 /// "unavailable" (a write may or may not have been applied). The API's own text is never shown for any status except 400: a 400's own <c>detail</c> and per-field messages may be shown, because
 /// they are validation text written for the visitor ("Add a subject."), never a token, a host or exception text.
 /// </summary>
@@ -25,6 +25,8 @@ internal static class ProblemMapping
                     : [new ResultError(ApiErrorCodes.ValidationFailed, problem.Detail ?? ProblemCopy.Invalid, ResultErrorKind.Validation)];
             case 404:
                 return [NotFound()];
+            case 409:
+                return [new ResultError(ApiErrorCodes.ReplyConflict, ProblemCopy.ReplyConflict, ResultErrorKind.Conflict)];
             case 413:
                 return [new ResultError(ApiErrorCodes.PayloadTooLarge, ProblemCopy.PayloadTooLarge, ResultErrorKind.Failure)];
             case 415:
```

`src/TechStrap.Portal/Clients/ApiClientRegistration.cs`

```diff
@@ -53,6 +53,9 @@ public static class ApiClientRegistration
 
         services.AddScoped<ApiConnection>();
         services.AddScoped<IPublicProductClient, PublicProductClient>();
+        services.AddScoped<IPublicTicketClient, PublicTicketClient>();
+        services.AddScoped<ICustomerTicketClient, CustomerTicketClient>();
+        services.AddScoped<IPublicKbClient, PublicKbClient>();
         return services;
     }
 
```


- [ ] **Step 5: Implement the form-page headers and the redaction keys**

The form pages keep the shared referrer policy (a visitor's own address is no secret from the same site) but are never indexed and never stored: their address can carry a name, an address and a subject. The rule matches by path, case-insensitively, so the 404 on those paths carries it too. The two Hosting redactors learn the new parameter names.

`src/TechStrap.Hosting/Logging/PiiRedactionEnricher.cs`

```diff
@@ -6,7 +6,8 @@ namespace TechStrap.Hosting.Logging;
 
 /// <summary>
 /// Rewrites PII-shaped text in every property value before any sink sees the event (D-039): email addresses (also URL-encoded), 43-character access tokens, JWT-shaped bearer tokens and
-/// "sha256:" hashes, and (D-045) the value of a <c>name</c> or <c>email</c> query parameter, which is how the Portal's contact page is prefilled and which a request log would otherwise carry.
+/// "sha256:" hashes, and (D-045) the value of a <c>name</c>, <c>email</c>, <c>subject</c>, <c>ref</c> or <c>q</c> query parameter: the Portal's contact page is prefilled with the first three, its "received" page carries the
+/// protected ticket reference in <c>ref</c> and its suggest adapter the visitor's search text in <c>q</c>, which a request log would otherwise carry.
 /// It cannot touch LogEvent.Exception or the template, and it cannot recognise a name by shape; application code never logs either
 /// (exceptions are logged by type name, requesters by id).
 /// Residual risk, accepted: names cannot be pattern-redacted, and an attached Exception is not rewritten. The worker loops that attach an
@@ -96,7 +97,8 @@ public sealed partial class PiiRedactionEnricher : ILogEventEnricher
             decoded = name;
         }
 
-        return decoded.Equals("name", StringComparison.OrdinalIgnoreCase) || decoded.Equals("email", StringComparison.OrdinalIgnoreCase);
+        return decoded.Equals("name", StringComparison.OrdinalIgnoreCase) || decoded.Equals("email", StringComparison.OrdinalIgnoreCase)
+            || decoded.Equals("subject", StringComparison.OrdinalIgnoreCase) || decoded.Equals("ref", StringComparison.OrdinalIgnoreCase) || decoded.Equals("q", StringComparison.OrdinalIgnoreCase);
     }
 
     private LogEventPropertyValue Redact(LogEventPropertyValue value)
```

`src/TechStrap.Hosting/Sentry/SensitiveQuerySentryProcessor.cs`

```diff
@@ -9,8 +9,8 @@ namespace TechStrap.Hosting.Sentry;
 /// Masks the text an agent searched for in what Sentry records. The queue keeps its search in the address (<c>/queue/mine?search=...</c>) so a view can be bookmarked, and the same text goes to the
 /// API as <c>GET /api/tickets?search=...</c>. A search is often a requester's email address or a subject line, so on an unhandled exception it must not reach Sentry in the request's query string
 /// or URL, in a breadcrumb, or in the description of a span. The value becomes <c>[redacted]</c> and the rest of the address is left alone, so the event still shows which page failed.
-/// The parameters are <c>search</c> (the queue and the ticket list) and <c>q</c> (a free-text query), and, for the Portal's contact page (D-045), <c>name</c> and <c>email</c> (the prefill, which a customer's own
-/// app puts in the address). The Portal's ticket address carries the access token in its path (<c>/t/{token}</c>), so a 43-character token directly under <c>/t/</c> is masked wherever an address appears.
+/// The parameters are <c>search</c> (the queue and the ticket list) and <c>q</c> (a free-text query), and, for the Portal's contact page (D-045), <c>name</c>, <c>email</c> and <c>subject</c> (the prefill, which a
+/// customer's own app puts in the address) and <c>ref</c> (the protected ticket reference of the "received" page). The Portal's ticket address carries the access token in its path (<c>/t/{token}</c>), so a 43-character token directly under <c>/t/</c> is masked wherever an address appears.
 /// Registered with the header scrubber by <see cref="SentryOptionsExtensions.AddSensitiveHeaderScrubbing"/>.
 /// </summary>
 public sealed partial class SensitiveQuerySentryProcessor : ISentryEventProcessor, ISentryTransactionProcessor
@@ -40,7 +40,8 @@ public sealed partial class SensitiveQuerySentryProcessor : ISentryEventProcesso
         }
 
         return decoded.Equals("search", StringComparison.OrdinalIgnoreCase) || decoded.Equals("q", StringComparison.OrdinalIgnoreCase)
-            || decoded.Equals("name", StringComparison.OrdinalIgnoreCase) || decoded.Equals("email", StringComparison.OrdinalIgnoreCase);
+            || decoded.Equals("name", StringComparison.OrdinalIgnoreCase) || decoded.Equals("email", StringComparison.OrdinalIgnoreCase)
+            || decoded.Equals("subject", StringComparison.OrdinalIgnoreCase) || decoded.Equals("ref", StringComparison.OrdinalIgnoreCase);
     }
 
     // A non-sensitive value is looked into once more per level, but only this deep: "last=/queue/mine?search=x" needs one. The bound keeps hostile text such as "x=a=a=a=..." from
```

`src/TechStrap.Portal/Headers/PortalHeaderRules.cs`

```diff
@@ -15,16 +15,31 @@ internal static class PortalHeaderRules
     public const string CacheControl = "no-store";
     public const string RobotsTag = "noindex";
 
-    /// <summary>The rules for <c>UseTechStrapWebHost</c>: the ticket headers, then the attachment sandbox.</summary>
+    /// <summary>The rules for <c>UseTechStrapWebHost</c>: the ticket headers, the attachment sandbox, then the form pages' headers.</summary>
     public static IReadOnlyList<PathHeaderRule> Rules { get; } =
     [
         PathHeaderRule.Set(IsTicketPath, ("Referrer-Policy", ReferrerPolicy), ("Cache-Control", CacheControl), ("X-Robots-Tag", RobotsTag)),
         PathHeaderRule.Sandbox(IsTicketAttachmentPath),
+
+        // The form pages keep the shared referrer policy (a visitor's own address is no secret from the same site) but are never indexed and never stored: their address can carry a name, an address and a subject.
+        PathHeaderRule.Set(IsFormPagePath, ("Cache-Control", CacheControl), ("X-Robots-Tag", RobotsTag)),
     ];
 
     /// <summary><c>/t</c> and everything under it, without regard to case or a trailing slash.</summary>
     public static bool IsTicketPath(PathString path) => path.StartsWithSegments(PortalRoutes.TicketPrefix);
 
+    /// <summary>
+    /// The four form pages of a product (D-045 addendum): <c>/p/{key}/contact</c>, <c>/p/{key}/contact/received</c>, <c>/p/{key}/lost-link</c> and <c>/p/{key}/suggest</c>, without regard to case or a trailing slash (routing
+    /// matches without regard to case, so the rule must too). Nothing deeper and nothing else, so the product home and the help centre stay cacheable.
+    /// </summary>
+    public static bool IsFormPagePath(PathString path) =>
+        path.StartsWithSegments(PortalRoutes.ProductPrefix, out var rest)
+        && rest.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries) is [_, var page, .. var tail]
+        && (Is(page, PortalRoutes.ContactSegment) ? tail.Length == 0 || (tail.Length == 1 && Is(tail[0], PortalRoutes.ReceivedSegment))
+            : (Is(page, PortalRoutes.LostLinkSegment) || Is(page, PortalRoutes.SuggestSegment)) && tail.Length == 0);
+
+    private static bool Is(string segment, string expected) => segment.Equals(expected, StringComparison.OrdinalIgnoreCase);
+
     /// <summary>Exactly <c>/t/{token}/attachments/{id}</c>: the three segments after <c>/t</c>, the middle one <c>attachments</c>.</summary>
     public static bool IsTicketAttachmentPath(PathString path) =>
         path.StartsWithSegments(PortalRoutes.TicketPrefix, out var rest)
```

`src/TechStrap.Portal/Routing/PortalRoutes.cs`

```diff
@@ -14,6 +14,12 @@ public static class PortalRoutes
     /// <summary>The ticket pages. The Portal's header rules and its robots.txt exclusion apply to everything under it.</summary>
     public const string TicketPrefix = "/t";
 
+    // The last segments of the product's form pages: the header rule that keeps them out of the index and out of every cache matches on these (PortalHeaderRules.IsFormPagePath).
+    public const string ContactSegment = "contact";
+    public const string ReceivedSegment = "received";
+    public const string LostLinkSegment = "lost-link";
+    public const string SuggestSegment = "suggest";
+
     public const string HomeTemplate = "/";
     public const string NotFoundTemplate = "/not-found";
     public const string ErrorTemplate = "/error";
```


- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet build TechStrap.slnx -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

Run: `dotnet test --project tests/TechStrap.Portal.Tests -c Release`
Expected: PASS: `total: 593, failed: 0`.

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release`
Expected: PASS: `total: 899, failed: 0`.

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: PASS: `total: 293, failed: 0` (HTTP is still only in `Clients/`, the source files are ASCII).

- [ ] **Step 7: Prove each pin with a mutation**

Run `git add -A` first.

| # | File | Replace | With | Command (after `--`) | Expected |
| --- | --- | --- | --- | --- | --- |
| 1 | `src/TechStrap.Portal/Clients/ApiConnection.cs` | `HttpCompletionOption.ResponseHeadersRead, cancellationToken` | `HttpCompletionOption.ResponseContentRead, cancellationToken` | `PT "/*/*/ApiConnectionStreamTests/*"` | KILLED, 1 failed of 11 |
| 2 | `src/TechStrap.Portal/Clients/ApiConnection.cs` | `var request = new HttpRequestMessage(HttpMethod.Get, uri);\n        Attach(request, token);\n        HttpResponseMessage? response = null;` | `var request = new HttpRequestMessage(HttpMethod.Get, uri);\n        HttpResponseMessage? response = null;` | `PT "/*/*/ApiConnectionStreamTests/*"` | KILLED, 2 failed of 11 |
| 3 | `src/TechStrap.Portal/Clients/ApiConnection.cs` | `response = await ReadClient.SendAsync(request, HttpCompletionOption` | `response = await WriteClient.SendAsync(request, HttpCompletionOption` | `PT "/*/*/ApiConnectionStreamTests/*"` | KILLED, 2 failed of 11 |
| 4 | `src/TechStrap.Portal/Clients/ApiDownload.cs` | `_response.Dispose();\n        _request.Dispose();` | `_request.Dispose();` | `PT "/*/*/ApiConnectionStreamTests/*"` | KILLED, 1 failed of 11 |
| 5 | `src/TechStrap.Portal/Clients/AttachmentFileName.cs` | `var segment = lastSeparator >= 0 ? name[(lastSeparator + 1)..] : name;` | `var segment = name;` | `PT "/*/*/AttachmentFileNameTests/*"` | KILLED, 6 failed of 18 |
| 6 | `src/TechStrap.Portal/Clients/AttachmentFileName.cs` | `CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format &&` | (nothing) | `PT "/*/*/AttachmentFileNameTests/*"` | KILLED, 1 failed of 18 |
| 7 | `src/TechStrap.Portal/Clients/MultipartForm.cs` | `form.Dispose();\n            throw;` | `throw;` | `PT "/*/*/MultipartFormTests/*"` | KILLED, 1 failed of 7 |
| 8 | `src/TechStrap.Portal/Clients/MultipartForm.cs` | `if (value is not null)` | `if (true)` | `PT "/*/*/MultipartFormTests/*"` | KILLED (the run fails) |
| 9 | `src/TechStrap.Portal/Clients/ApiQuery.cs` | `Uri.EscapeDataString(p.Value!)` | `p.Value!` | `PT "/*/*/ApiQueryTests/*"` | KILLED, 2 failed of 6 |
| 10 | `src/TechStrap.Portal/Clients/PublicTicketClient.cs` | `if (!ProductKeyShape.IsWellFormed(productKey))` | `if (false)` | `PT "/*/*/PublicTicketClientTests/*"` | KILLED (the run fails) |
| 11 | `src/TechStrap.Portal/Clients/PublicTicketClient.cs` | `new("Website", request.Website),` | (nothing) | `PT "/*/*/PublicTicketClientTests/*"` | KILLED, 1 failed of 19 |
| 12 | `src/TechStrap.Portal/Clients/CustomerTicketClient.cs` | `$"{TicketPath}/replies", form, token, cancellationToken)` | `$"{TicketPath}/replies", form, cancellationToken)` | `PT "/*/*/CustomerTicketClientTests/*"` | KILLED, 1 failed of 14 |
| 13 | `src/TechStrap.Portal/Clients/CustomerTicketClient.cs` | `api.GetAsync<CustomerTicketDto>(TicketPath, token, cancellationToken)` | `api.GetAsync<CustomerTicketDto>(TicketPath, cancellationToken)` | `PT "/*/*/CustomerTicketClientTests/*"` | KILLED, 2 failed of 14 |
| 14 | `src/TechStrap.Portal/Clients/PublicKbClient.cs` | `ProductKeyShape.IsWellFormed(productKey)\n            ?` | `true\n            ?` | `PT "/*/*/PublicKbClientTests/*"` | KILLED, 5 failed of 8 |
| 15 | `src/TechStrap.Portal/Clients/ProblemMapping.cs` | `case 409:\n                return [new ResultError(ApiErrorCodes.ReplyConflict, ProblemCopy.ReplyConflict, ResultErrorKind.Conflict)];` | (nothing) | `PT "/*/*/ProblemMappingTests/*"` | KILLED, 1 failed of 25 |
| 16 | `src/TechStrap.Portal/Clients/ApiClientRegistration.cs` | `services.AddScoped<IPublicKbClient, PublicKbClient>();` | (nothing) | `PT "/*/*/PublicKbClientTests/*" H=src/TechStrap.Portal/Headers/PortalHeaderRules.cs` | KILLED, 8 failed of 8 |
| 17 | `src/TechStrap.Portal/Headers/PortalHeaderRules.cs` | `PathHeaderRule.Set(IsFormPagePath, ("Cache-Control", CacheControl), ("X-Robots-Tag", RobotsTag)),` | `PathHeaderRule.Set(IsFormPagePath, ("Cache-Control", CacheControl)),` | `PT "/*/*/FormPageHeaderHostTests/*"` | KILLED, 6 failed of 11 |
| 18 | `src/TechStrap.Portal/Headers/PortalHeaderRules.cs` | `\|\| (tail.Length == 1 && Is(tail[0], PortalRoutes.ReceivedSegment))` | (nothing) | `PT "/*/*/PortalHeaderRulesTests/*"` | KILLED, 3 failed of 63 |
| 19 | `src/TechStrap.Portal/Headers/PortalHeaderRules.cs` | `(Is(page, PortalRoutes.LostLinkSegment) \|\| Is(page, PortalRoutes.SuggestSegment)) && tail.Length == 0` | `(Is(page, PortalRoutes.LostLinkSegment) \|\| Is(page, PortalRoutes.SuggestSegment))` | `PT "/*/*/PortalHeaderRulesTests/*"` | KILLED, 2 failed of 63 |
| 20 | `src/TechStrap.Portal/Headers/PortalHeaderRules.cs` | `segment.Equals(expected, StringComparison.OrdinalIgnoreCase)` | `segment.Equals(expected, StringComparison.Ordinal)` | `PT "/*/*/PortalHeaderRulesTests/*" E=src/TechStrap.Hosting/Logging/PiiRedactionEnricher.cs` | KILLED, 4 failed of 63 |
| 21 | `src/TechStrap.Hosting/Logging/PiiRedactionEnricher.cs` | `\|\| decoded.Equals("q", StringComparison.OrdinalIgnoreCase);` | `;` | `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/PiiRedactionQueryValueTests/*"` | KILLED, 4 failed of 30 |
| 22 | `src/TechStrap.Hosting/Logging/PiiRedactionEnricher.cs` | `decoded.Equals("subject", StringComparison.OrdinalIgnoreCase) \|\| decoded.Equals("ref", StringComparison.OrdinalIgnoreCase) \|\|` | (nothing) | `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/PiiRedactionQueryValueTests/*" S=src/TechStrap.Hosting/Sentry/SensitiveQuerySentryProcessor.cs` | KILLED, 6 failed of 30 |
| 23 | `src/TechStrap.Hosting/Sentry/SensitiveQuerySentryProcessor.cs` | `\|\| decoded.Equals("subject", StringComparison.OrdinalIgnoreCase) \|\| decoded.Equals("ref", StringComparison.OrdinalIgnoreCase);` | `;` | `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/SensitiveQuerySentryProcessorTests/*"` | KILLED, 5 failed of 62 |
| 24 | `src/TechStrap.Hosting/Logging/PiiRedactionEnricher.cs` | `\|\| decoded.Equals("q", StringComparison.OrdinalIgnoreCase);` | `;` | `PT "/*/*/RequestLogRedactionHostTests/*"` | KILLED, 1 failed of 10 |

Row 4 first SURVIVED: the download test only watched the body stream, which the framework closes with the response's content anyway. The test now also tracks the response's own content (`TrackingContent`), so releasing the upstream response (and its pooled connection) is pinned; the table shows the re-run.

- [ ] **Step 8: Verify and commit**

Run: `dotnet build TechStrap.slnx -c Release --no-incremental`
Expected: `0 Warning(s)`, `0 Error(s)`.

```bash
git status --short
git add -A src tests
git diff --cached --stat
git commit -F - <<'EOF'
feat(portal): ticket, customer and KB-search clients, the streaming GET and the form-page headers (PHASE-09b)

IPublicTicketClient (multipart submit), ICustomerTicketClient (view, reply, lost link) and IPublicKbClient
(search), ApiConnection.OpenStreamAsync, the multipart builder with file-name cleaning, the 409 mapping,
noindex and no-store on the four form pages, and the subject, ref and q redaction keys.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
EOF
```

### Task 3: The contact page and the received page: the form, the honeypot, the attachments, the prefill, the size limit and the protected reference

**Review Focus pin:** 3 (antiforgery, the size limit before buffering, pre-validated attachments, cleaned names, the honeypot passed through, a prefill that is judged like typed text and never hidden or auto-submitted), 4 (the prefill values and file names are encoded), 5 (every call of the contact flow forwards the visitor's address; the submit goes through the write client once) and 1 (the posted text and the reference never reach a log).

**Files:**

- Create: `src/TechStrap.Portal/Components/Pages/Contact.razor`
- Create: `src/TechStrap.Portal/Components/Pages/Contact.razor.cs`
- Create: `src/TechStrap.Portal/Components/Pages/ContactReceived.razor`
- Create: `src/TechStrap.Portal/Components/Pages/ContactReceived.razor.cs`
- Create: `src/TechStrap.Portal/Components/Ui/AttachmentInput.razor`
- Create: `src/TechStrap.Portal/Components/Ui/ErrorSummary.razor`
- Create: `src/TechStrap.Portal/Components/Ui/FieldError.razor`
- Create: `src/TechStrap.Portal/Components/Ui/FormField.razor`
- Create: `src/TechStrap.Portal/Components/Ui/HoneypotField.razor`
- Create: `src/TechStrap.Portal/Forms/AttachmentRules.cs`
- Create: `src/TechStrap.Portal/Forms/ContactCopy.cs`
- Create: `src/TechStrap.Portal/Forms/ContactFormValidator.cs`
- Create: `src/TechStrap.Portal/Forms/ContactFormViewModel.cs`
- Create: `src/TechStrap.Portal/Forms/FormCopy.cs`
- Create: `src/TechStrap.Portal/Forms/FormError.cs`
- Create: `src/TechStrap.Portal/Forms/FormFailure.cs`
- Create: `src/TechStrap.Portal/Forms/ReceivedReference.cs`
- Create: `src/TechStrap.Portal/Uploads/RequestTooLargeMiddleware.cs`
- Modify: `src/TechStrap.Portal/Components/_Imports.razor`
- Modify: `src/TechStrap.Portal/Program.cs`
- Modify: `src/TechStrap.Portal/Routing/PortalRoutes.cs`
- Modify: `src/TechStrap.Portal/Styles/_components.scss`
- Test (create): `tests/TechStrap.Portal.Tests/Forms/AttachmentRulesTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Forms/ContactFormValidatorTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Forms/ContactPageHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Forms/ContactPostHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Forms/ContactReceivedHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Forms/FakeBrowserFile.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Forms/FormCopyTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Forms/FormFailureTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Forms/FormTestKit.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Forms/ReceivedReferenceTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Forms/RequestTooLargeMiddlewareTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Logging/RequestLogRedactionHostTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/PortalFactory.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Routing/PortalRoutesTests.cs`

**Interfaces:**
- Consumes: Task 2's `IPublicTicketClient`, `NewTicketRequest`, `AttachmentUpload`, `AttachmentFileName`, `ProblemCopy`, `ApiErrorCodes`, the form-page header rule; Task 1's `IntakeLimits`; 09a's `ProductPageBase` (`Key`, `Theme`, `UnavailableMessage`, `RetryHref`), `ProductScope`, `ShellCopy`, `ProductUnavailable`, the harness (`PortalFactory`, `ProxyHopStartupFilter`, `AssertEveryCallBore`).
- Produces:
  - `TechStrap.Portal.Forms`: `FormFields` (`Name`, `Email`, `Subject`, `Body`, `Attachments`, `ErrorId(field)`, `FromTarget(target)`), `FormError(string? Field, string Code, string Message)`, `FormCopy` (`For(code)`, `AttachmentRules`, `Accept`, the notices, the file-naming sentences), `FormFailure.From(IReadOnlyList<ResultError>)` (`Errors`, `Notice`, `Status`, `IsNotFound`), `AttachmentRules.Validate(IReadOnlyList<IBrowserFile>?)` and `ToUploads(...)`, `ContactFormViewModel` (`Name`, `Email`, `Subject`, `Body`, `Website`, `Files`, `FromPrefill`), `ContactFormValidator.Validate`, `ContactCopy`, `ReceivedReference` (`Protect(number)`, `TryUnprotect(reference, out number)`, `Lifetime` 10 minutes, `Purpose`, `MaxReferenceLength`).
  - `TechStrap.Portal.Uploads.RequestTooLargeMiddleware` (a plain 413 for a declared length over the applied limit; before `UseAntiforgery`).
  - Components `ErrorSummary`, `FieldError`, `FormField`, `AttachmentInput`, `HoneypotField`; pages `Contact` and `ContactReceived`; `PortalRoutes.ContactReceived(key, reference)` and `ReceivedReferenceParameter`.
  - Test harness: `PortalFactory.ExpectedClientIp` (disposing the factory asserts that every API call the host made carried `X-Forwarded-For: {ExpectedClientIp}`; a test that made no call passes), `FormTestKit` (`Visitor`, `Factory(environment, configure, settings, product)` which builds every host behind a trusted proxy with `ExpectedClientIp` set, `Client(factory)` which sends the visitor's address, `TokenAsync`, `ContactForm`) and `FakeBrowserFile`.

- [ ] **Step 1: Write the failing tests**

Every host test of 09b makes its API calls through a host built by the kit, so the harness itself enforces Review Focus 5: `PortalFactory.ExpectedClientIp` is asserted when the factory is disposed (`AssertEveryCallBore`), which means a host test that makes an API call can never pass with a lost forwarded-IP handler (rows 31 and 32 prove it). The unit tests pin the validator, the attachment rules, the failure mapping, the copy and the protected reference at their boundaries. The host tests drive the real pages through the stub API: the page and its prefill, the post (redirect, validation, the honeypot, the files, every API failure, antiforgery), the received page (valid, missing, expired, tampered), the size limit on the real server, and the logs at Verbose.

`tests/TechStrap.Portal.Tests/Forms/AttachmentRulesTests.cs` (new)

```csharp
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// Review Focus 3: the files a visitor picked are checked against the API's own limits (count, size per file, total, type) before anything is sent, each failure names its file by the cleaned name, and a file is
/// opened only with the per-file limit (the framework's default of 512,000 bytes would throw on a normal screenshot).
/// </summary>
public sealed class AttachmentRulesTests
{
    private static string[] Codes(params FakeBrowserFile[] files) => [.. AttachmentRules.Validate(files).Select(e => e.Code)];

    [Fact]
    public void No_files_is_fine()
    {
        AttachmentRules.Validate(null).ShouldBeEmpty();
        AttachmentRules.Validate([]).ShouldBeEmpty();
    }

    [Fact]
    public void Five_files_of_the_biggest_size_that_together_fit_are_fine()
    {
        var files = Enumerable.Range(0, IntakeLimits.MaxFiles).Select(i => new FakeBrowserFile($"f{i}.pdf", IntakeLimits.MaxMessageBytes / IntakeLimits.MaxFiles)).ToArray();

        Codes(files).ShouldBeEmpty();
    }

    [Fact]
    public void A_sixth_file_is_too_many()
    {
        var files = Enumerable.Range(0, IntakeLimits.MaxFiles + 1).Select(i => new FakeBrowserFile($"f{i}.txt", 1)).ToArray();

        Codes(files).ShouldBe(["attachments-too-many"]);
    }

    [Fact]
    public void The_per_file_limit_is_inclusive()
    {
        Codes(new FakeBrowserFile("ok.zip", IntakeLimits.MaxFileBytes)).ShouldBeEmpty();
        var error = AttachmentRules.Validate([new FakeBrowserFile("big report.zip", IntakeLimits.MaxFileBytes + 1)]).ShouldHaveSingleItem();

        error.Code.ShouldBe("attachment-too-large");
        error.Field.ShouldBe(FormFields.Attachments);
        error.Message.ShouldBe("big report.zip is over 10 MB. Send a smaller file.");
    }

    [Fact]
    public void An_empty_file_is_named()
    {
        var error = AttachmentRules.Validate([new FakeBrowserFile("empty.txt", 0)]).ShouldHaveSingleItem();

        error.Code.ShouldBe("attachment-empty");
        error.Message.ShouldBe("empty.txt is empty. Remove it or choose another.");
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("noextension")]
    [InlineData("archive.tar.gz")]
    [InlineData("photo.png.exe")]
    [InlineData("script.html")]
    public void A_type_that_is_not_on_the_list_is_refused_and_named(string name)
    {
        var error = AttachmentRules.Validate([new FakeBrowserFile(name, 5)]).ShouldHaveSingleItem();

        error.Code.ShouldBe("attachment-type-not-allowed");
        error.Message.ShouldStartWith($"{name} is a type we cannot accept.");
        error.Message.ShouldContain(".png, .jpg");
    }

    [Theory]
    [InlineData("photo.PNG")]
    [InlineData("Photo.JpEg")]
    [InlineData("notes.TXT")]
    [InlineData("data.csv")]
    [InlineData("server.log")]
    [InlineData("bundle.zip")]
    public void Every_allowed_type_is_accepted_whatever_its_case(string name)
    {
        Codes(new FakeBrowserFile(name, 5)).ShouldBeEmpty();
    }

    [Fact]
    public void Together_the_files_may_not_pass_the_message_limit_even_when_each_is_fine()
    {
        var third = IntakeLimits.MaxMessageBytes / 3 + 1;

        Codes(new FakeBrowserFile("a.zip", third), new FakeBrowserFile("b.zip", third), new FakeBrowserFile("c.zip", third)).ShouldBe(["attachments-too-large"]);
    }

    [Fact]
    public void A_path_in_the_name_is_cut_to_the_file_name_in_the_sentence()
    {
        var error = AttachmentRules.Validate([new FakeBrowserFile("C:\\Users\\jo\\Desktop\\virus.exe", 5)]).ShouldHaveSingleItem();

        error.Message.ShouldStartWith("virus.exe is a type");
        error.Message.ShouldNotContain("Users");
    }

    [Fact]
    public void Every_failing_file_is_reported_not_only_the_first()
    {
        Codes(new FakeBrowserFile("a.exe", 5), new FakeBrowserFile("b.txt", 0), new FakeBrowserFile("c.zip", IntakeLimits.MaxFileBytes + 1))
            .ShouldBe(["attachment-type-not-allowed", "attachment-empty", "attachment-too-large"]);
    }

    [Fact]
    public async Task A_file_is_opened_only_when_the_upload_is_read_and_always_with_the_per_file_limit()
    {
        var file = new FakeBrowserFile("shot.png", 600_000, "image/png");
        var uploads = AttachmentRules.ToUploads([file]);

        file.Opened.ShouldBe(0, "nothing is opened until the request is built");
        await using var stream = uploads.ShouldHaveSingleItem().OpenRead();

        file.Opened.ShouldBe(1);
        file.LastLimit.ShouldBe(IntakeLimits.MaxFileBytes, "the framework's own default of 512,000 bytes would have thrown on this 600,000 byte file");
        stream.Length.ShouldBe(600_000);
    }

    [Fact]
    public void An_upload_carries_the_browsers_name_and_content_type_for_the_client_to_clean()
    {
        var upload = AttachmentRules.ToUploads([new FakeBrowserFile("C:\\x\\a.txt", 1, "text/plain")]).ShouldHaveSingleItem();

        upload.FileName.ShouldBe("C:\\x\\a.txt");
        upload.ContentType.ShouldBe("text/plain");
        AttachmentRules.ToUploads(null).ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Portal.Tests/Forms/ContactFormValidatorTests.cs` (new)

```csharp
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// P09-T06 and T21: the Portal's check of the contact form before it asks the API. Each rule at its boundary, with the Contracts constants (the same ones the inputs' <c>maxlength</c> attributes use), the API's own
/// codes, the Portal's own sentences, and values judged trimmed. Name is required (the UX brief) although the API only limits its length.
/// </summary>
public sealed class ContactFormValidatorTests
{
    private static ContactFormViewModel Valid() => new() { Name = "Ada Lovelace", Email = "ada@example.com", Subject = "Printer jam", Body = "It jams every time." };

    private static string[] Codes(ContactFormViewModel form) => [.. ContactFormValidator.Validate(form).Select(e => e.Code)];

    [Fact]
    public void A_valid_form_has_no_errors()
    {
        ContactFormValidator.Validate(Valid()).ShouldBeEmpty();
    }

    [Fact]
    public void A_form_with_nothing_in_it_fails_every_required_field_in_the_order_of_the_form()
    {
        var errors = ContactFormValidator.Validate(new ContactFormViewModel());

        errors.Select(e => (e.Field, e.Code)).ShouldBe(
        [
            (FormFields.Name, "name-required"),
            (FormFields.Email, "email-required"),
            (FormFields.Subject, "subject-required"),
            (FormFields.Body, "body-required"),
        ]);
        errors.ShouldAllBe(e => e.Message.Length > 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void A_blank_value_is_a_missing_value(string blank)
    {
        var form = Valid();
        form.Name = blank;
        form.Email = blank;
        form.Subject = blank;
        form.Body = blank;

        Codes(form).ShouldBe(["name-required", "email-required", "subject-required", "body-required"]);
    }

    [Fact]
    public void Each_text_limit_is_inclusive_and_one_more_fails_with_the_apis_code()
    {
        var form = Valid();
        form.Name = new string('n', IntakeLimits.NameMaxLength);
        form.Subject = new string('s', IntakeLimits.SubjectMaxLength);
        form.Body = new string('b', IntakeLimits.BodyMaxLength);
        form.Email = new string('e', IntakeLimits.EmailMaxLength - "@example.com".Length) + "@example.com";
        ContactFormValidator.Validate(form).ShouldBeEmpty();

        form.Name += "n";
        form.Subject += "s";
        form.Body += "b";
        form.Email = "e" + form.Email;

        Codes(form).ShouldBe(["name-too-long", "email-invalid", "subject-too-long", "body-too-long"]);
    }

    [Fact]
    public void A_value_is_judged_after_trimming()
    {
        var form = Valid();
        form.Subject = "  " + new string('s', IntakeLimits.SubjectMaxLength) + "  ";
        form.Email = "  ada@example.com  ";

        ContactFormValidator.Validate(form).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("ada")]
    [InlineData("ada@")]
    [InlineData("@example.com")]
    [InlineData("ada@example")]
    [InlineData("ada example@example.com")]
    [InlineData("ada@example.com, bob@example.com")]
    [InlineData("Ada <ada@example.com>")]
    [InlineData("ada@@example.com")]
    [InlineData("jane@")]
    public void An_email_that_is_not_one_plain_dotted_address_is_invalid(string email)
    {
        var form = Valid();
        form.Email = email;

        var error = ContactFormValidator.Validate(form).ShouldHaveSingleItem();

        error.Field.ShouldBe(FormFields.Email);
        error.Code.ShouldBe("email-invalid");
        error.Message.ShouldBe("Enter a valid email address, like name@example.com.");
    }

    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("ada.lovelace+support@sub.example.co.uk")]
    [InlineData("a@b.io")]
    public void A_plain_address_is_valid(string email)
    {
        var form = Valid();
        form.Email = email;

        ContactFormValidator.Validate(form).ShouldBeEmpty();
    }

    [Fact]
    public void The_honeypot_is_never_a_validation_error()
    {
        var form = Valid();
        form.Website = "http://spam.example";

        ContactFormValidator.Validate(form).ShouldBeEmpty();
    }

    [Fact]
    public void The_attachments_are_checked_too_and_their_errors_come_last()
    {
        var form = Valid();
        form.Subject = "";
        form.Files = [new FakeBrowserFile("virus.exe", 10)];

        Codes(form).ShouldBe(["subject-required", "attachment-type-not-allowed"]);
    }

    [Fact]
    public void A_prefill_becomes_the_three_inputs_and_nothing_else()
    {
        var form = ContactFormViewModel.FromPrefill("Printer jam", "Jane Doe", "jane@example.com");

        form.Subject.ShouldBe("Printer jam");
        form.Name.ShouldBe("Jane Doe");
        form.Email.ShouldBe("jane@example.com");
        form.Body.ShouldBeNull();
        form.Website.ShouldBeNull("the honeypot is never prefilled");
        form.Files.ShouldBeNull();
    }

    [Fact]
    public void A_prefill_is_judged_exactly_like_typed_text()
    {
        var typed = new ContactFormViewModel { Name = "Jane Doe", Email = "jane@", Subject = new string('s', 201), Body = "x" };
        var prefilled = ContactFormViewModel.FromPrefill(typed.Subject, typed.Name, typed.Email);
        prefilled.Body = "x";

        ContactFormValidator.Validate(prefilled).ShouldBe(ContactFormValidator.Validate(typed));
    }
}
```

`tests/TechStrap.Portal.Tests/Forms/ContactPageHostTests.cs` (new)

```csharp
using System.Net;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// P09-T06 and T21 at the host: the contact page as a visitor first sees it. A themed product page with a labelled, antiforgery-protected multipart form, the limits of the API on its inputs, an honeypot a person
/// cannot reach, the attachment rule stated before a file is picked, and the prefill (<c>?subject&amp;name&amp;email</c>) in visible, editable inputs: validated like typed text, never hidden, never echoed from any
/// other parameter and never submitted for the visitor.
/// </summary>
public sealed class ContactPageHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<(HttpResponseMessage Response, string Html)> GetAsync(PortalFactory factory, string path)
    {
        using var client = FormTestKit.Client(factory);
        var response = await client.GetAsync(path, Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task The_page_is_themed_and_has_a_labelled_antiforgery_protected_multipart_form()
    {
        await using var factory = FormTestKit.Factory();

        var (response, html) = await GetAsync(factory, FormTestKit.Path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<title>Contact Paperplane support</title>");
        html.ShouldContain("--ts-accent:#F59E0B");
        html.ShouldContain("<h1>Contact support</h1>");
        html.ShouldContain("<form method=\"post\" action=\"/p/paperplane/contact\" enctype=\"multipart/form-data\" novalidate");
        html.ShouldContain("name=\"__RequestVerificationToken\"");
        html.ShouldContain("name=\"_handler\" value=\"contact\"");
        foreach (var (id, text) in new[] { ("name", "Your name"), ("email", "Email address"), ("subject", "Subject"), ("body", "Message"), ("attachments", "Attachments (optional)") })
        {
            html.ShouldContain($"<label for=\"{id}\" class=\"form-label\">{text}</label>");
            html.ShouldContain($"id=\"{id}\"");
        }

        html.ShouldContain("<button type=\"submit\" class=\"btn btn-primary\">Send message</button>");
        html.ShouldContain("href=\"/p/paperplane/kb\">Browse help articles</a>");
        html.ShouldNotContain("<script>", Case.Sensitive, "the form works without script");
        factory.Api.Count(HttpMethod.Get, "/api/public/products/paperplane").ShouldBe(1);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0, "opening the page never creates anything");
    }

    [Fact]
    public async Task The_inputs_carry_the_apis_limits_and_the_right_keyboards_and_autocomplete_values()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.Path);

        html.ShouldContain($"name=\"Form.Name\" type=\"text\" class=\"form-control\" maxlength=\"{IntakeLimits.NameMaxLength}\" autocomplete=\"name\"");
        html.ShouldContain($"name=\"Form.Email\" type=\"email\" class=\"form-control\" maxlength=\"{IntakeLimits.EmailMaxLength}\" autocomplete=\"email\"");
        html.ShouldContain($"name=\"Form.Subject\" type=\"text\" class=\"form-control\" maxlength=\"{IntakeLimits.SubjectMaxLength}\"");
        html.ShouldContain($"name=\"Form.Body\" class=\"form-control\" rows=\"8\" maxlength=\"{IntakeLimits.BodyMaxLength}\"");
    }

    [Fact]
    public async Task The_attachment_rule_is_stated_before_a_file_is_picked_and_the_input_is_a_plain_multiple_file_input()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.Path);

        html.ShouldContain("Up to 5 files: images and documents of 10 MB each and 25 MB in all (.png, .jpg, .jpeg, .gif, .webp, .pdf, .txt, .log, .csv, .zip).");
        html.ShouldContain("name=\"Form.Files\" type=\"file\" multiple accept=\".png,.jpg,.jpeg,.gif,.webp,.pdf,.txt,.log,.csv,.zip\"");
        html.ShouldContain("aria-describedby=\"attachments-hint\"");
    }

    [Fact]
    public async Task The_honeypot_is_out_of_sight_out_of_the_tab_order_out_of_assistive_technology_and_not_autofilled()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.Path);

        html.ShouldContain("<div class=\"ts-hp\" aria-hidden=\"true\">");
        html.ShouldContain("<input id=\"contact-website\" name=\"Form.Website\" type=\"text\" tabindex=\"-1\" autocomplete=\"off\"");
        html.ShouldNotContain("style=\"position", Case.Sensitive, "the CSP allows no inline style: the class is in the style sheet");
    }

    [Fact]
    public async Task The_page_is_noindex_and_no_store_and_keeps_the_shared_headers_and_the_normal_policy()
    {
        await using var factory = FormTestKit.Factory();

        var (response, _) = await GetAsync(factory, FormTestKit.Path);

        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("strict-origin-when-cross-origin");
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.ShouldNotContain("sandbox");
        policy.ShouldContain("form-action 'self'");
    }

    [Fact]
    public async Task The_visitors_address_behind_a_trusted_proxy_reaches_the_api_on_the_product_call_of_the_page()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync(FormTestKit.Path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Api.AssertEveryCallBore(FormTestKit.Visitor);
    }

    [Theory]
    [InlineData("/p/nope/contact")]
    [InlineData("/p/Paperplane/contact")]
    [InlineData("/p/paper%20plane/contact")]
    public async Task An_unknown_inactive_or_malformed_product_is_the_uniform_404_and_shows_no_form(string path)
    {
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/nope", HttpStatusCode.NotFound, "product-not-found", "No such product.");

        var (response, html) = await GetAsync(factory, path);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldContain("Page not found");
        html.ShouldNotContain("<form");
        html.ShouldNotContain("--ts-accent");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
    }

    [Fact]
    public async Task When_the_api_cannot_be_asked_the_page_says_so_calmly_and_shows_no_form()
    {
        await using var factory = FormTestKit.Factory("Production", product: false);
        factory.Api.OnStatus(HttpMethod.Get, "/api/public/products/paperplane", HttpStatusCode.ServiceUnavailable);

        var (response, html) = await GetAsync(factory, FormTestKit.Path);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        html.ShouldContain("This page could not be loaded.");
        html.ShouldNotContain("<form");
    }

    // ---- P09-T21: the prefill ----

    [Fact]
    public async Task The_prefill_fills_three_visible_editable_inputs_and_nothing_else()
    {
        await using var factory = FormTestKit.Factory();

        var (response, html) = await GetAsync(factory, FormTestKit.Path + "?subject=Printer%20jam&name=Jane%20Doe&email=jane%40example.com");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("name=\"Form.Subject\" type=\"text\" class=\"form-control\" value=\"Printer jam\" maxlength=\"200\"");
        html.ShouldContain("name=\"Form.Name\" type=\"text\" class=\"form-control\" value=\"Jane Doe\" maxlength=\"100\"");
        html.ShouldContain("name=\"Form.Email\" type=\"email\" class=\"form-control\" value=\"jane@example.com\" maxlength=\"320\"");
        html.ShouldNotContain("type=\"hidden\" name=\"Form.", Case.Sensitive, "no hidden field carries prefill data");
        html.ShouldNotContain("value=\"Printer jam\" type=\"hidden\"");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0, "a prefill never submits anything");
    }

    [Fact]
    public async Task Prefilled_markup_is_encoded_wherever_it_appears()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.Path + "?subject=%22%20autofocus%20onfocus%3Dalert(1)%20x%3D%22&name=%3Cscript%3Ealert(1)%3C%2Fscript%3E&email=a%22%3E%3Cimg%20src%3Dx%3E");

        html.ShouldNotContain("<script>alert(1)");
        html.ShouldNotContain("<img src=x>");
        html.ShouldNotContain("\" autofocus onfocus", Case.Sensitive, "the quote is encoded, so the value cannot end its attribute");
        html.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;");
        html.ShouldContain("value=\"&quot; autofocus onfocus=alert(1) x=&quot;\"");
    }

    [Fact]
    public async Task Unknown_parameters_change_nothing_and_are_never_echoed()
    {
        await using var factory = FormTestKit.Factory();

        var (_, plain) = await GetAsync(factory, FormTestKit.Path);
        var (_, extra) = await GetAsync(factory, FormTestKit.Path + "?product=orbitly&token=AbC-_0123456789AbC-_0123456789AbC-_01234567&handler=other&Website=spam&ref=1");

        extra.ShouldNotContain("orbitly");
        extra.ShouldNotContain("AbC-_0123456789");
        extra.ShouldNotContain("spam");
        // The same page, token for token (the antiforgery value differs every time).
        System.Text.RegularExpressions.Regex.Replace(extra, "value=\"CfDJ[^\"]+\"", "value=\"T\"")
            .ShouldBe(System.Text.RegularExpressions.Regex.Replace(plain, "value=\"CfDJ[^\"]+\"", "value=\"T\""));
        factory.Api.Count(HttpMethod.Get, "/api/public/products/paperplane").ShouldBe(2, "the product is the page's own, never the query's");
    }

    [Fact]
    public async Task A_prefill_longer_than_the_inputs_limit_is_shown_as_it_came_and_judged_on_post()
    {
        await using var factory = FormTestKit.Factory();
        var subject = new string('s', IntakeLimits.SubjectMaxLength + 50);

        var (response, html) = await GetAsync(factory, FormTestKit.Path + "?subject=" + subject);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain($"value=\"{subject}\" maxlength=\"200\"");
    }

    [Fact]
    public async Task The_prefill_values_never_reach_the_api_or_a_log_until_the_visitor_posts_them()
    {
        await using var factory = FormTestKit.Factory();

        await GetAsync(factory, FormTestKit.Path + "?subject=Printer&name=Jane&email=jane%40example.com");

        factory.Api.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/public/products/paperplane");
        factory.Api.Requests.Single().Query.ShouldBeEmpty("the Portal sends the API the product key and nothing from the query");
    }
}
```

`tests/TechStrap.Portal.Tests/Forms/ContactPostHostTests.cs` (new)

```csharp
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// P09-T06 at the host: the post of the contact form. A valid post creates the ticket through the API, redirects (302) to the received page with a protected reference and never shows the address, subject or
/// name in the redirect; an invalid post shows the error summary and every field error, keeps what was typed and calls nothing; every API failure is shown in the Portal's own words with a fitting status. Review
/// Focus 3: antiforgery is enforced (a post without a valid token is a 400 and reaches no handler), the files are checked before anything is sent, the honeypot is passed through, and every call forwards the
/// visitor's address (Review Focus 5). Every test that makes an API call asserts it (<c>AssertEveryCallBore</c>).
/// </summary>
public sealed class ContactPostHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Visitor = FormTestKit.Visitor;

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, HttpContent content) => client.PostAsync(FormTestKit.Path, content, Ct);

    private static PortalFactory Host(Action<IServiceCollection>? configure = null) => FormTestKit.Factory(configure: configure);

    private static async Task<(HttpClient Client, string Token)> OpenAsync(PortalFactory factory)
    {
        var client = FormTestKit.Client(factory);
        return (client, await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct));
    }

    private static string Text(string html, string id) => Regex.Match(html, $"id=\"{id}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    // ---- the happy path and the redirect ----

    [Fact]
    public async Task A_valid_post_creates_the_ticket_and_redirects_to_the_received_page_with_a_protected_reference()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created("PAP-42"), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token));

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var location = response.Headers.Location.ShouldNotBeNull().ToString();
        location.ShouldStartWith("http://localhost/p/paperplane/contact/received?ref=");
        location.ShouldNotContain("PAP-42", Case.Sensitive, "the number travels protected");
        location.ShouldNotContain("ada", Case.Insensitive);
        location.ShouldNotContain("jam", Case.Insensitive);
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        var sent = factory.Api.Requests.Single(r => r.Method == HttpMethod.Post);
        sent.Client.ShouldBe(ApiClientNames.Write);
        sent.Path.ShouldBe(FormTestKit.ApiTicketsPath);
        sent.Body.ShouldNotBeNull().ShouldContain("ada@example.com");
        sent.Body.ShouldContain("Printer jam");
        sent.Body.ShouldNotContain("name=Website", Case.Sensitive, "an empty honeypot is not sent");
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Fact]
    public async Task Following_the_redirect_shows_the_ticket_number_and_a_refresh_sends_nothing_again()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created("PAP-42"), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        using var posted = await PostAsync(client, FormTestKit.ContactForm(token));
        var next = posted.Headers.Location!.PathAndQuery;

        var first = await client.GetStringAsync(next, Ct);
        var second = await client.GetStringAsync(next, Ct);

        first.ShouldContain("We have received your request.");
        first.ShouldContain("<strong class=\"ts-ticket-number\">PAP-42</strong>");
        second.ShouldContain("PAP-42");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1, "post, redirect, get: reloading the confirmation never posts again");
    }

    [Fact]
    public async Task Text_with_surrounding_spaces_is_sent_trimmed()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, email: "  ada@example.com ", name: " Ada ", subject: "  Printer jam  ", body: "\r\n It jams.\r\n"));

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var body = factory.Api.Requests.Single(r => r.Method == HttpMethod.Post).Body!;
        body.ShouldContain("\r\n\r\nada@example.com\r\n");
        body.ShouldContain("\r\n\r\nAda\r\n");
        body.ShouldContain("\r\n\r\nPrinter jam\r\n");
        body.ShouldContain("\r\n\r\nIt jams.\r\n");
    }

    // ---- antiforgery ----

    [Fact]
    public async Task A_post_without_an_antiforgery_token_is_a_400_and_reaches_no_handler()
    {
        await using var factory = Host();
        var (client, _) = await OpenAsync(factory);
        using var __ = client;
        var apiCallsBefore = factory.Api.Requests.Count;

        using var response = await PostAsync(client, FormTestKit.ContactForm(null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Api.Requests.Count.ShouldBe(apiCallsBefore, "no handler ran: not even the product was asked again");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    [Fact]
    public async Task A_post_with_a_wrong_token_or_without_the_cookie_is_a_400()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        using var stranger = FormTestKit.Client(factory);

        using var wrong = await PostAsync(client, FormTestKit.ContactForm(token + "x"));
        using var noCookie = await PostAsync(stranger, FormTestKit.ContactForm(token));

        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noCookie.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    // ---- validation: nothing is sent, the text is kept ----

    [Fact]
    public async Task An_invalid_post_shows_the_summary_and_the_field_errors_keeps_the_text_and_calls_nothing()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        var before = factory.Api.Requests.Count;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, email: "not an address", name: "Ada", subject: "", body: "It jams <b>every</b> time."));
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<section class=\"ts-error-summary alert alert-danger\" role=\"alert\" tabindex=\"-1\" autofocus aria-labelledby=\"error-summary-heading\">");
        html.ShouldContain("<a href=\"#email\">Enter a valid email address, like name@example.com.</a>");
        html.ShouldContain("<a href=\"#subject\">Enter a subject.</a>");
        html.ShouldContain("<p id=\"email-error\" class=\"ts-field-error\">Enter a valid email address, like name@example.com.</p>");
        html.ShouldContain("aria-describedby=\"email-error\" aria-invalid=\"true\"");
        html.ShouldNotContain("<a href=\"#name\">", Case.Sensitive, "a valid field has no error");
        Text(html, "name").ShouldBe("Ada");
        Text(html, "email").ShouldBe("not an address");
        html.ShouldContain("It jams &lt;b&gt;every&lt;/b&gt; time.</textarea>");
        html.ShouldContain("files are not kept");
        factory.Api.Requests.Count.ShouldBe(before + 1, "the post asked for the product again (the page is rebuilt) and nothing else");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Fact]
    public async Task Everything_missing_lists_every_field_once_in_the_order_of_the_form()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, email: "", name: "", subject: "", body: ""));
        var html = await response.Content.ReadAsStringAsync(Ct);

        var summary = FormTestKit.Between(html, "<ul>", "</ul>");
        Regex.Matches(summary, "<a href=\"#(\\w+)\">").Select(m => m.Groups[1].Value).ShouldBe(["name", "email", "subject", "body"]);
    }

    [Fact]
    public async Task A_prefill_posted_back_is_judged_exactly_like_typed_text()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        var tooLong = new string('s', IntakeLimits.SubjectMaxLength + 1);

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, email: "jane@", subject: tooLong));
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain("The subject must be at most 200 characters.");
        html.ShouldContain("Enter a valid email address, like name@example.com.");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    // ---- the honeypot ----

    [Fact]
    public async Task A_filled_honeypot_is_passed_to_the_api_and_the_visitor_gets_the_same_redirect()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created("PAP-43"), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, website: "http://spam.example"));

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.ToString().ShouldStartWith("http://localhost/p/paperplane/contact/received?ref=");
        var body = factory.Api.Requests.Single(r => r.Method == HttpMethod.Post).Body!;
        body.ShouldContain("name=Website");
        body.ShouldContain("http://spam.example");
        factory.Api.AssertEveryCallBore(Visitor);
    }

    // ---- attachments ----

    [Fact]
    public async Task Files_are_sent_on_with_their_cleaned_names_types_and_content()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, files: [new("C:\\fakepath\\log.txt", "the log"u8.ToArray()), new("shot.png", [1, 2, 3], "image/png")]));

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var body = factory.Api.Requests.Single(r => r.Method == HttpMethod.Post).Body!;
        body.ShouldContain("name=Attachments; filename=log.txt");
        body.ShouldContain("the log");
        body.ShouldContain("name=Attachments; filename=shot.png");
        body.ShouldContain("Content-Type: image/png");
        body.ShouldNotContain("fakepath");
    }

    [Fact]
    public async Task A_part_with_no_file_chosen_is_ignored_and_a_post_without_files_is_an_ordinary_post()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        var form = FormTestKit.ContactForm(token);
        var empty = new ByteArrayContent([]);
        empty.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("form-data") { Name = "\"Form.Files\"", FileName = "\"\"" };
        form.Add(empty);

        using var response = await PostAsync(client, form);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Requests.Single(r => r.Method == HttpMethod.Post).Body!.ShouldNotContain("name=Attachments");
    }

    [Fact]
    public async Task Six_files_a_type_that_is_not_allowed_an_empty_file_and_a_big_file_are_each_named_before_anything_is_sent()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        var files = new List<PostedFile>
        {
            new("a.txt", [1]), new("b.txt", [1]), new("c.txt", [1]), new("d.txt", [1]),
            new("virus.exe", [1], "application/octet-stream"),
            new("empty.txt", []),
        };

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, files: [.. files]));
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Attach at most 5 files.");
        html.ShouldContain("virus.exe is a type we cannot accept.");
        html.ShouldContain("empty.txt is empty. Remove it or choose another.");
        html.ShouldContain("<a href=\"#attachments\">");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    [Fact]
    public async Task A_file_over_the_per_file_limit_is_named_and_not_sent()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        var big = new PostedFile("big report.pdf", new byte[(int)IntakeLimits.MaxFileBytes + 1], "application/pdf");

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, files: [big]));
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain("big report.pdf is over 10 MB. Send a smaller file.");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    [Fact]
    public async Task Files_that_together_pass_the_message_limit_are_refused_before_anything_is_sent()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        PostedFile[] files = [new("a.zip", new byte[8_900_000], "application/zip"), new("b.zip", new byte[8_900_000], "application/zip"), new("c.zip", new byte[8_900_000], "application/zip")];

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, files: files));
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain("Your files add up to more than 25 MB.");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    [Fact]
    public async Task A_file_name_with_markup_is_encoded_in_the_error()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, files: [new("<img src=x onerror=alert(1)>.exe", [1])]));
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldNotContain("<img src=x");
        html.ShouldContain("&lt;img src=x onerror=alert(1)&gt;.exe is a type we cannot accept.");
    }

    // ---- what the API says ----

    [Fact]
    public async Task The_apis_field_codes_become_the_portals_own_sentences_on_the_right_fields()
    {
        await using var factory = Host();
        factory.Api.On(HttpMethod.Post, FormTestKit.ApiTicketsPath, _ => StubApiHandler.ValidationProblem(
            [("email", "email-invalid", "API TEXT must not show"), ("body", "body-too-long", "Domain text must not show"), ("attachments", "attachment-type-not-allowed", "x")]));
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token));
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Enter a valid email address, like name@example.com.");
        html.ShouldContain("The message must be at most 100,000 characters.");
        html.ShouldContain("One of the files is a type we cannot accept.");
        html.ShouldNotContain("API TEXT");
        html.ShouldNotContain("Domain text");
        html.ShouldContain("<a href=\"#attachments\">");
        Text(html, "email").ShouldBe("ada@example.com", "what was typed is kept");
    }

    [Fact]
    public async Task An_unknown_code_is_the_generic_check_your_answers_sentence_never_the_apis_text()
    {
        await using var factory = Host();
        factory.Api.On(HttpMethod.Post, FormTestKit.ApiTicketsPath, _ => StubApiHandler.ValidationProblem("whatever", "a-new-code", "Column secret of table x"));
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token));
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain("Some of what you entered needs another look.");
        html.ShouldNotContain("Column secret");
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, "Too many attempts. Wait a few minutes and try again.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, "We could not send that just now.")]
    [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable, "We could not send that just now.")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.OK, "That is too large to send.")]
    [InlineData(HttpStatusCode.UnsupportedMediaType, HttpStatusCode.OK, "That could not be sent in that form.")]
    public async Task A_failure_of_the_api_is_shown_calmly_with_a_fitting_status_and_the_text_is_kept(HttpStatusCode api, HttpStatusCode page, string sentence)
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, FormTestKit.ApiTicketsPath, api, "x", "System.InvalidOperationException at Npgsql host=10.0.0.5");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token));
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(page);
        html.ShouldContain(sentence);
        html.ShouldNotContain("Npgsql");
        html.ShouldNotContain("10.0.0.5");
        Text(html, "subject").ShouldBe("Printer jam");
        html.ShouldContain("It jams every time.</textarea>");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1, "a post is never retried");
    }

    [Fact]
    public async Task A_transport_failure_is_the_same_calm_page_with_one_attempt()
    {
        await using var factory = Host();
        factory.Api.On(HttpMethod.Post, FormTestKit.ApiTicketsPath, _ => throw new HttpRequestException("No connection could be made (10.1.2.3:5432)"));
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token));
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        html.ShouldContain("We could not send that just now.");
        html.ShouldNotContain("10.1.2.3");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
    }

    [Fact]
    public async Task A_product_that_vanishes_between_the_page_and_the_post_is_the_uniform_404()
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, FormTestKit.ApiTicketsPath, HttpStatusCode.NotFound, "product-not-found", "No such product.");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token));
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldContain("Page not found");
        html.ShouldNotContain("<form");
    }

    [Fact]
    public async Task A_valid_post_to_an_unknown_product_is_a_404_before_the_handler_and_never_calls_the_ticket_endpoint()
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/nope", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        factory.Api.OnJson(HttpMethod.Post, "/api/public/products/nope/tickets", FormTestKit.Created(), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        // The token and cookie are good (they came from a real product's page); only the product is unknown.
        using var response = await client.PostAsync("/p/nope/contact", FormTestKit.ContactForm(token), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        // The page asks for the product before the handler runs, finds none and ends in NavigationManager.NotFound(). The framework re-executes the post against the not-found page, which has no handler named
        // "contact", so the answer is the framework's own 400 (an empty body), not the 404 page a GET gets. What matters is that nothing was created and no form came back.
        (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest).ShouldBeTrue($"was {(int)response.StatusCode}");
        html.ShouldNotContain("<form");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        factory.Api.Count(HttpMethod.Post, "/api/public/products/nope/tickets").ShouldBe(0);
    }

    // ---- logs ----

    [Fact]
    public async Task What_the_visitor_posted_never_reaches_a_log_event_at_any_level()
    {
        await using var factory = FormTestKit.Factory(settings: PortalFactory.VerboseLogging);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", FormTestKit.Product());
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created("PAP-77"), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, email: "private.person@example.com", name: "Zebediah Quux", subject: "Sensitive subject line", body: "A very private body"));
        var location = response.Headers.Location!.PathAndQuery;
        await client.GetStringAsync(location, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect");
        factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("ref=[redacted]", StringComparison.Ordinal), "control: the redirect target was logged, masked");
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text =>
            !text.Contains("private.person", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("Zebediah", StringComparison.Ordinal)
            && !text.Contains("Quux", StringComparison.Ordinal)
            && !text.Contains("Sensitive subject", StringComparison.Ordinal)
            && !text.Contains("very private body", StringComparison.Ordinal)
            && !text.Contains("PAP-77", StringComparison.Ordinal)
            && !text.Contains(token, StringComparison.Ordinal));
    }

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);
}
```

`tests/TechStrap.Portal.Tests/Forms/ContactReceivedHostTests.cs` (new)

```csharp
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// P09-T06 at the host: the received page. The ticket number comes from the protected, 10-minute <c>?ref=</c> value and from nothing else. A value that is missing, expired, tampered with or made by another
/// key ring shows the generic confirmation (never an error, never a number); the number and every other string on the page are encoded; the page is noindex and no-store.
/// </summary>
public sealed class ContactReceivedHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<(HttpResponseMessage Response, string Html)> GetAsync(PortalFactory factory, string path)
    {
        using var client = FormTestKit.Client(factory);
        var response = await client.GetAsync(path, Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    private static string ReferenceFor(PortalFactory factory, string number)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ReceivedReference>().Protect(number);
    }

    [Fact]
    public async Task A_valid_reference_shows_the_number_and_what_happens_next()
    {
        await using var factory = FormTestKit.Factory();
        using var host = FormTestKit.Client(factory);
        var reference = ReferenceFor(factory, "PAP-42");

        var (response, html) = await GetAsync(factory, FormTestKit.ReceivedPath + "?ref=" + Uri.EscapeDataString(reference));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<title>Request received: Paperplane</title>");
        html.ShouldContain("<h1>We have received your request.</h1>");
        html.ShouldContain("Your ticket number is <strong class=\"ts-ticket-number\">PAP-42</strong>");
        html.ShouldContain("We have emailed you a link. Use it to follow the conversation and reply.");
        html.ShouldContain("href=\"/p/paperplane/kb\">Browse help articles</a>");
        html.ShouldContain("href=\"/p/paperplane\">Back to Paperplane</a>");
        html.ShouldContain("href=\"/p/paperplane/lost-link\">Can&#x27;t find the email?</a>");
        html.ShouldNotContain("/t/", Case.Sensitive, "the ticket link is never on this page: it is proved by owning the mailbox");
    }

    [Fact]
    public async Task The_page_is_noindex_and_no_store()
    {
        await using var factory = FormTestKit.Factory();

        var (response, _) = await GetAsync(factory, FormTestKit.ReceivedPath);

        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
    }

    [Theory]
    [InlineData("")]
    [InlineData("?ref=")]
    [InlineData("?ref=PAP-42")]
    [InlineData("?ref=CfDJ8NotARealProtectedValue")]
    [InlineData("?ref=%00%00")]
    public async Task A_missing_or_invalid_reference_shows_the_generic_confirmation_and_no_number(string query)
    {
        await using var factory = FormTestKit.Factory();

        var (response, html) = await GetAsync(factory, FormTestKit.ReceivedPath + query);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("We have received your request.");
        html.ShouldContain("We have emailed you a link to follow it. Check your inbox and your spam folder.");
        html.ShouldNotContain("ts-ticket-number");
        html.ShouldNotContain("PAP-42");
    }

    [Fact]
    public async Task A_tampered_reference_shows_the_generic_confirmation()
    {
        await using var factory = FormTestKit.Factory();
        using var host = FormTestKit.Client(factory);
        var reference = ReferenceFor(factory, "PAP-42");
        var tampered = reference[..^4] + (reference[^4] == 'A' ? "B" : "A") + reference[^3..];

        var (response, html) = await GetAsync(factory, FormTestKit.ReceivedPath + "?ref=" + Uri.EscapeDataString(tampered));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldNotContain("ts-ticket-number");
        html.ShouldContain("Check your inbox and your spam folder.");
    }

    [Fact]
    public async Task A_reference_older_than_ten_minutes_shows_the_generic_confirmation()
    {
        // The clock that made the reference ran an hour behind the real one, so the reference expired 50 minutes ago (the unprotect check uses the real clock).
        var past = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1));
        await using var factory = FormTestKit.Factory(configure: services => services.AddSingleton<TimeProvider>(past));
        using var host = FormTestKit.Client(factory);
        var reference = ReferenceFor(factory, "PAP-42");

        var (_, html) = await GetAsync(factory, FormTestKit.ReceivedPath + "?ref=" + Uri.EscapeDataString(reference));

        html.ShouldNotContain("ts-ticket-number");
        html.ShouldContain("Check your inbox and your spam folder.");
    }

    [Fact]
    public async Task The_page_of_an_unknown_product_is_the_uniform_404_even_with_a_good_reference()
    {
        await using var factory = FormTestKit.Factory();
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/nope", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        using var host = FormTestKit.Client(factory);
        var reference = ReferenceFor(factory, "PAP-42");

        var (response, html) = await GetAsync(factory, "/p/nope/contact/received?ref=" + Uri.EscapeDataString(reference));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldNotContain("PAP-42");
        html.ShouldContain("Page not found");
    }

    [Fact]
    public async Task When_the_api_cannot_be_asked_the_page_says_so_calmly()
    {
        await using var factory = FormTestKit.Factory("Production", product: false);
        factory.Api.OnStatus(HttpMethod.Get, "/api/public/products/paperplane", HttpStatusCode.ServiceUnavailable);

        var (response, html) = await GetAsync(factory, FormTestKit.ReceivedPath);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        html.ShouldContain("This page could not be loaded.");
    }
}
```

`tests/TechStrap.Portal.Tests/Forms/FakeBrowserFile.cs` (new)

```csharp
using Microsoft.AspNetCore.Components.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>A file as the form binder hands it over: a name, a declared size and a content type. Reading it with a limit below its size throws, as the framework's does.</summary>
internal sealed class FakeBrowserFile(string name, long size, string contentType = "text/plain", byte[]? content = null) : IBrowserFile
{
    public int Opened { get; private set; }

    public long? LastLimit { get; private set; }

    public string Name => name;

    public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

    public long Size => size;

    public string ContentType => contentType;

    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
    {
        Opened++;
        LastLimit = maxAllowedSize;
        if (size > maxAllowedSize)
        {
            throw new IOException($"Supplied file with size {size} bytes exceeds the maximum of {maxAllowedSize} bytes.");
        }

        return new MemoryStream(content ?? new byte[size]);
    }
}
```

`tests/TechStrap.Portal.Tests/Forms/FormCopyTests.cs` (new)

```csharp
using TechStrap.Portal.Clients;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>The sentences of the forms: every code the API can send for a ticket or a reply has a sentence of the Portal's own, built from the Contracts limits, and nothing else leaks through.</summary>
public sealed class FormCopyTests
{
    private static readonly string[] ApiCodes =
    [
        "email-required", "email-invalid", "subject-required", "subject-too-long", "body-required", "body-too-long", "name-too-long",
        "attachments-too-many", "attachments-too-large", "attachment-too-large", "attachment-empty", "attachment-type-not-allowed",
    ];

    [Fact]
    public void Every_code_the_api_sends_for_a_ticket_or_a_reply_has_a_sentence_of_its_own()
    {
        foreach (var code in ApiCodes.Append(FormCopy.NameRequiredCode))
        {
            var sentence = FormCopy.For(code);
            sentence.ShouldNotBe(ProblemCopy.Invalid, $"{code} fell through to the generic sentence");
            sentence.ShouldNotBeNullOrWhiteSpace();
            sentence.ShouldNotContain("<");
            sentence.ShouldNotContain("API", Case.Sensitive, "the visitor does not know there is one");
        }
    }

    [Theory]
    [InlineData("name-too-long", "Your name must be at most 100 characters.")]
    [InlineData("subject-too-long", "The subject must be at most 200 characters.")]
    [InlineData("body-too-long", "The message must be at most 100,000 characters.")]
    [InlineData("attachments-too-many", "Attach at most 5 files.")]
    [InlineData("attachment-too-large", "A file is over 10 MB. Send a smaller one.")]
    [InlineData("attachments-too-large", "Your files add up to more than 25 MB. Remove a file or send smaller ones.")]
    public void The_limits_in_the_sentences_are_the_contract_limits(string code, string expected) => FormCopy.For(code).ShouldBe(expected);

    [Fact]
    public void An_unknown_code_is_the_generic_sentence()
    {
        FormCopy.For("something-new").ShouldBe(ProblemCopy.Invalid);
        FormCopy.For(string.Empty).ShouldBe(ProblemCopy.Invalid);
    }

    [Fact]
    public void The_attachment_rule_is_stated_with_the_size_count_and_every_allowed_type()
    {
        FormCopy.AttachmentRules.ShouldBe("Up to 5 files: images and documents of 10 MB each and 25 MB in all (.png, .jpg, .jpeg, .gif, .webp, .pdf, .txt, .log, .csv, .zip).");
        FormCopy.Accept.ShouldBe(".png,.jpg,.jpeg,.gif,.webp,.pdf,.txt,.log,.csv,.zip");
    }

    [Fact]
    public void The_notices_keep_what_the_visitor_wrote_and_say_what_to_do()
    {
        FormCopy.RateLimited.ShouldContain("still here");
        FormCopy.Unavailable.ShouldContain("still here");
        FormCopy.Unavailable.ShouldNotContain("API", Case.Sensitive);
        FormCopy.AttachmentsKept.ShouldContain("choose them again");
    }

    [Theory]
    [InlineData("name", "name-error")]
    [InlineData("attachments", "attachments-error")]
    public void An_error_paragraph_id_is_the_field_and_a_suffix(string field, string id) => FormFields.ErrorId(field).ShouldBe(id);

    [Theory]
    [InlineData("email", "email")]
    [InlineData("Email", "email")]
    [InlineData(" BODY ", "body")]
    [InlineData("attachments", "attachments")]
    [InlineData("Subject", "subject")]
    [InlineData("name", "name")]
    [InlineData("website", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("Idempotency-Key", null)]
    public void An_api_target_maps_to_a_field_or_to_nothing(string? target, string? field) => FormFields.FromTarget(target).ShouldBe(field);
}
```

`tests/TechStrap.Portal.Tests/Forms/FormFailureTests.cs` (new)

```csharp
using Microsoft.AspNetCore.Http;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>What a form shows when the API refused it, decided from the errors alone, in the Portal's own words (never the API's) and with the status a visitor and a monitor can use.</summary>
public sealed class FormFailureTests
{
    private static ResultError Validation(string code, string target, string message = "API TEXT") => new(code, message, ResultErrorKind.Validation, target);

    private static ResultError Failure(string code) => new(code, "API TEXT", ResultErrorKind.Failure);

    [Fact]
    public void Field_errors_keep_the_apis_codes_and_become_the_portals_sentences_on_the_matching_fields()
    {
        var failure = FormFailure.From([Validation("email-invalid", "email"), Validation("body-too-long", "Body"), Validation("subject-required", "subject")]);

        failure.Status.ShouldBe(StatusCodes.Status200OK);
        failure.Notice.ShouldBeNull();
        failure.IsNotFound.ShouldBeFalse();
        failure.Errors.Select(e => (e.Field, e.Code)).ShouldBe([(FormFields.Email, "email-invalid"), (FormFields.Body, "body-too-long"), (FormFields.Subject, "subject-required")]);
        failure.Errors.ShouldAllBe(e => !e.Message.Contains("API TEXT", StringComparison.Ordinal));
        failure.Errors[0].Message.ShouldBe("Enter a valid email address, like name@example.com.");
    }

    [Theory]
    [InlineData("attachments-too-many", "attachments")]
    [InlineData("attachments-too-large", "attachments")]
    [InlineData("attachment-too-large", "somewhere-else")]
    [InlineData("attachment-empty", null)]
    [InlineData("attachment-type-not-allowed", "attachment")]
    public void An_attachment_code_always_belongs_to_the_attachments_field(string code, string? target)
    {
        var failure = FormFailure.From([Validation(code, target!)]);

        failure.Errors.ShouldHaveSingleItem().Field.ShouldBe(FormFields.Attachments);
    }

    [Fact]
    public void An_unknown_code_or_target_is_the_generic_sentence_and_a_summary_item_without_a_link()
    {
        var failure = FormFailure.From([Validation("a-new-code", "mystery")]);

        var error = failure.Errors.ShouldHaveSingleItem();
        error.Field.ShouldBeNull();
        error.Message.ShouldBe(ProblemCopy.Invalid);
    }

    [Fact]
    public void A_rate_limit_is_a_429_with_a_calm_notice_and_no_field_errors()
    {
        var failure = FormFailure.From([Failure(ApiErrorCodes.RateLimited)]);

        failure.Status.ShouldBe(StatusCodes.Status429TooManyRequests);
        failure.Notice.ShouldBe(FormCopy.RateLimited);
        failure.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ApiErrorCodes.ApiUnavailable)]
    [InlineData(ApiErrorCodes.UnexpectedResponse)]
    [InlineData(ApiErrorCodes.ApiError)]
    public void An_outage_or_an_unreadable_answer_is_a_503_with_the_unavailable_notice(string code)
    {
        var failure = FormFailure.From([Failure(code)]);

        failure.Status.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        failure.Notice.ShouldBe(FormCopy.Unavailable);
    }

    [Fact]
    public void A_reply_conflict_is_a_409_with_its_own_notice()
    {
        var failure = FormFailure.From([new ResultError(ApiErrorCodes.ReplyConflict, "API TEXT", ResultErrorKind.Conflict)]);

        failure.Status.ShouldBe(StatusCodes.Status409Conflict);
        failure.Notice.ShouldBe(ProblemCopy.ReplyConflict);
    }

    [Theory]
    [InlineData(ApiErrorCodes.PayloadTooLarge, "That is too large to send.")]
    [InlineData(ApiErrorCodes.UnsupportedMediaType, "That could not be sent in that form.")]
    public void A_413_or_a_415_is_an_attachment_error_on_a_normal_page(string code, string sentence)
    {
        var failure = FormFailure.From([Failure(code)]);

        failure.Status.ShouldBe(StatusCodes.Status200OK);
        var error = failure.Errors.ShouldHaveSingleItem();
        error.Field.ShouldBe(FormFields.Attachments);
        error.Message.ShouldStartWith(sentence);
    }

    [Fact]
    public void A_not_found_anywhere_in_the_errors_is_the_uniform_404()
    {
        var failure = FormFailure.From([new ResultError(ApiErrorCodes.NotFound, "API TEXT", ResultErrorKind.NotFound)]);

        failure.IsNotFound.ShouldBeTrue();
        failure.Status.ShouldBe(StatusCodes.Status404NotFound);
        failure.Errors.ShouldBeEmpty();
        failure.Notice.ShouldBeNull();
    }
}
```

`tests/TechStrap.Portal.Tests/Forms/FormTestKit.cs` (new)

```csharp
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>A file a test posts: its name as the browser sends it, its bytes and its content type.</summary>
internal sealed record PostedFile(string Name, byte[] Bytes, string ContentType = "text/plain");

/// <summary>
/// What the form host tests share: a product behind the stub API, the antiforgery token a real browser would get from the page, and the multipart post a browser would send for the contact form (the inputs are
/// named <c>Form.Email</c> and so on, with the handler name and the token as extra fields). The client keeps cookies, so the antiforgery cookie from the first GET goes with the post.
/// </summary>
internal static class FormTestKit
{
    public const string Path = "/p/paperplane/contact";
    public const string ReceivedPath = "/p/paperplane/contact/received";
    public const string ApiTicketsPath = "/api/public/products/paperplane/tickets";

    /// <summary>The visitor every host test pretends to be (a documentation address, RFC 5737).</summary>
    public const string Visitor = "203.0.113.9";

    public static PublicProductDto Product(string name = "Paperplane") => new("paperplane", name, null, "#F59E0B", "#000000", "#9D6507");

    /// <summary>
    /// A host behind a trusted reverse proxy (the connection's peer is the proxy) whose every API call must carry <see cref="Visitor"/> in <c>X-Forwarded-For</c>: the factory asserts it when it is disposed
    /// (<see cref="PortalFactory.ExpectedClientIp"/>). With <paramref name="product"/> the paperplane product is configured, so a page under /p/paperplane loads.
    /// </summary>
    public static PortalFactory Factory(string environment = "Development", Action<IServiceCollection>? configure = null, IReadOnlyDictionary<string, string?>? settings = null, bool product = true)
    {
        var factory = new PortalFactory(
            environment,
            settings,
            services =>
            {
                ProxyHopStartupFilter.Add("192.0.2.10")(services);
                configure?.Invoke(services);
            })
        {
            ExpectedClientIp = Visitor,
        };
        if (product)
        {
            factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Product());
        }

        return factory;
    }

    /// <summary>A client that does not follow redirects and sends the visitor's address the way the reverse proxy would.</summary>
    public static HttpClient Client(PortalFactory factory)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", Visitor);
        return client;
    }

    /// <summary>The value of the antiforgery field of the page at <paramref name="path"/>; the cookie that goes with it stays in the client.</summary>
    public static async Task<string> TokenAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        var html = await client.GetStringAsync(path, cancellationToken);
        return TokenFrom(html);
    }

    public static string TokenFrom(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        match.Success.ShouldBeTrue("the page must carry an antiforgery field");
        return match.Groups[1].Value;
    }

    public static MultipartFormDataContent ContactForm(
        string? token, string? email = "ada@example.com", string? name = "Ada Lovelace", string? subject = "Printer jam", string? body = "It jams every time.", string? website = null, params PostedFile[] files)
    {
        var form = new MultipartFormDataContent { { new StringContent("contact"), "_handler" } };
        if (token is not null)
        {
            form.Add(new StringContent(token), "__RequestVerificationToken");
        }

        Add(form, "Form.Name", name);
        Add(form, "Form.Email", email);
        Add(form, "Form.Subject", subject);
        Add(form, "Form.Body", body);
        Add(form, "Form.Website", website);
        foreach (var file in files)
        {
            var part = new ByteArrayContent(file.Bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            form.Add(part, "Form.Files", file.Name);
        }

        return form;
    }

    public static SubmitTicketResponse Created(string number = "PAP-42") => new(number, null, []);

    private static void Add(MultipartFormDataContent form, string name, string? value)
    {
        if (value is not null)
        {
            form.Add(new StringContent(value), name);
        }
    }

    /// <summary>The text between two markers, for the few places a test reads a value back (a redirect's query, an input's value).</summary>
    public static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        from.ShouldBeGreaterThanOrEqualTo(0, $"'{start}' was not found");
        from += start.Length;
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        to.ShouldBeGreaterThan(from, $"'{end}' was not found after '{start}'");
        return text[from..to];
    }
}
```

`tests/TechStrap.Portal.Tests/Forms/ReceivedReferenceTests.cs` (new)

```csharp
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// The protected <c>?ref=</c> value of the received page, without a host: it round trips a ticket number, expires after ten minutes (the clock that protects it is injected; the check uses the real one), refuses
/// a tampered value, a value made under another key ring or another purpose, a value that is too long and a payload that is not a ticket number. The time-limited protector is in the shared framework.
/// </summary>
public sealed class ReceivedReferenceTests
{
    private static IDataProtectionProvider Provider() => new EphemeralDataProtectionProvider();

    private static ReceivedReference Reference(IDataProtectionProvider? provider = null, TimeProvider? clock = null) => new(provider ?? Provider(), clock ?? TimeProvider.System);

    [Fact]
    public void A_ticket_number_round_trips()
    {
        var reference = Reference();

        reference.TryUnprotect(reference.Protect("PAP-42"), out var number).ShouldBeTrue();

        number.ShouldBe("PAP-42");
    }

    [Fact]
    public void The_protected_value_is_url_safe_and_does_not_contain_the_number()
    {
        var value = Reference().Protect("PAP-42");

        value.ShouldNotContain("PAP-42");
        value.ShouldMatch("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void The_lifetime_is_ten_minutes()
    {
        ReceivedReference.Lifetime.ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void A_value_made_with_an_expiry_already_past_is_refused()
    {
        // Unprotect checks the real clock, so the protecting clock runs an hour behind it: the value expired 50 minutes ago.
        var reference = Reference(clock: new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1)));

        reference.TryUnprotect(reference.Protect("PAP-42"), out var number).ShouldBeFalse();

        number.ShouldBeEmpty();
    }

    [Fact]
    public void A_value_inside_its_ten_minutes_is_accepted()
    {
        var reference = Reference(clock: new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-5)));

        reference.TryUnprotect(reference.Protect("PAP-42"), out _).ShouldBeTrue();
    }

    [Fact]
    public void A_tampered_value_is_refused()
    {
        var reference = Reference();
        var value = reference.Protect("PAP-42");
        var tampered = value[..10] + (value[10] == 'A' ? 'B' : 'A') + value[11..];

        reference.TryUnprotect(tampered, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_value_from_another_key_ring_is_refused()
    {
        var made = Reference(Provider()).Protect("PAP-42");

        Reference(Provider()).TryUnprotect(made, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_value_protected_for_the_features_own_purpose_by_the_same_key_ring_is_accepted()
    {
        var provider = Provider();
        var made = provider.CreateProtector(ReceivedReference.Purpose).ToTimeLimitedDataProtector().Protect("PAP-42", DateTimeOffset.UtcNow.AddMinutes(10));

        Reference(provider).TryUnprotect(made, out var number).ShouldBeTrue();

        number.ShouldBe("PAP-42");
    }

    [Fact]
    public void A_value_protected_for_another_purpose_is_refused()
    {
        var provider = Provider();
        var otherPurpose = provider.CreateProtector("Some.Other.Purpose").ToTimeLimitedDataProtector().Protect("PAP-42", DateTimeOffset.UtcNow.AddMinutes(10));

        Reference(provider).TryUnprotect(otherPurpose, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PAP-42")]
    [InlineData("not base64 !!!")]
    [InlineData("AAAA")]
    public void Garbage_is_refused_without_throwing(string? value)
    {
        Reference().TryUnprotect(value, out var number).ShouldBeFalse();

        number.ShouldBeEmpty();
    }

    [Fact]
    public void A_value_over_the_length_limit_is_refused_before_it_is_unprotected()
    {
        Reference().TryUnprotect(new string('A', ReceivedReference.MaxReferenceLength + 1), out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("<script>")]
    [InlineData("-leading-dash")]
    [InlineData("PAP-42\r\nSet-Cookie: x=1")]
    public void An_authentic_payload_that_is_not_a_ticket_number_is_refused(string payload)
    {
        var reference = Reference();

        reference.TryUnprotect(reference.Protect(payload), out var number).ShouldBeFalse();

        number.ShouldBeEmpty();
    }

    [Fact]
    public void The_purpose_names_the_feature_and_its_version()
    {
        ReceivedReference.Purpose.ShouldBe("TechStrap.Portal.ContactReceived.v1");
    }
}
```

`tests/TechStrap.Portal.Tests/Forms/RequestTooLargeMiddlewareTests.cs` (new)

```csharp
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Uploads;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// Review Focus 3: the request size limit applies before the form is buffered. The unit tests drive the middleware with a feature the way Kestrel supplies it (the in-memory test server has none, so the host
/// tests below use the real server, <c>UseKestrel(0)</c>): a declared length over the applied limit is a plain 413, one at the limit passes, and a server without the feature does nothing. The host tests prove
/// the page's own <c>[RequestSizeLimit(IntakeLimits.FormBodyBytes)]</c> is the applied limit and that it is in force before antiforgery reads the form, with a real post over the wire.
/// </summary>
public sealed class RequestTooLargeMiddlewareTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class LimitFeature(long? limit) : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;

        public long? MaxRequestBodySize { get; set; } = limit;
    }

    private static async Task<(DefaultHttpContext Context, bool Passed)> RunAsync(long? limit, long? declared, bool withFeature = true)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        if (withFeature)
        {
            context.Features.Set<IHttpMaxRequestBodySizeFeature>(new LimitFeature(limit));
        }

        context.Request.ContentLength = declared;
        var passed = false;
        await new RequestTooLargeMiddleware(_ =>
        {
            passed = true;
            return Task.CompletedTask;
        }).InvokeAsync(context);
        return (context, passed);
    }

    [Fact]
    public async Task A_declared_length_over_the_limit_is_a_plain_413_with_the_sentence_and_nothing_after_it_runs()
    {
        var (context, passed) = await RunAsync(1000, 1001);

        passed.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status413PayloadTooLarge);
        context.Response.ContentType.ShouldBe("text/plain; charset=utf-8");
        context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
        context.Response.Body.Position = 0;
        new StreamReader(context.Response.Body).ReadToEnd().ShouldBe(ProblemCopy.PayloadTooLarge);
    }

    [Theory]
    [InlineData(1000L, 1000L)]
    [InlineData(1000L, 0L)]
    [InlineData(1000L, null)]
    public async Task A_length_at_the_limit_or_no_declared_length_passes(long limit, long? declared)
    {
        var (context, passed) = await RunAsync(limit, declared);

        passed.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task A_server_without_the_feature_or_with_no_limit_does_nothing()
    {
        (await RunAsync(null, 99_999_999, withFeature: false)).Passed.ShouldBeTrue();
        (await RunAsync(null, 99_999_999)).Passed.ShouldBeTrue();
    }

    // ---- the real server ----

    private static (HttpClient Client, PortalFactory Factory) Kestrel()
    {
        var factory = FormTestKit.Factory();
        factory.UseKestrel(0);
        factory.StartServer();
        var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.Add("X-Forwarded-For", FormTestKit.Visitor);
        return (client, factory);
    }

    private static HttpRequestMessage Post(string token, int fileBytes, bool expectContinue)
    {
        var form = FormTestKit.ContactForm(token, files: [new("big.zip", new byte[fileBytes], "application/zip")]);
        var request = new HttpRequestMessage(HttpMethod.Post, FormTestKit.Path) { Content = form };
        request.Headers.ExpectContinue = expectContinue;
        return request;
    }

    [Fact]
    public async Task On_the_real_server_a_post_over_the_forms_limit_is_a_413_and_nothing_is_sent_to_the_api()
    {
        var (client, factory) = Kestrel();
        using var _ = client;
        await using var __ = factory;
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);
        var apiCalls = factory.Api.Requests.Count;

        // Expect: 100-continue lets the server refuse on the declared length before the client streams 26 MB into a closing socket.
        using var response = await client.SendAsync(Post(token, (int)IntakeLimits.FormBodyBytes, expectContinue: true), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe(ProblemCopy.PayloadTooLarge);
        factory.Api.Requests.Count.ShouldBe(apiCalls, "the form was never read, so the page never even asked for the product");
    }

    [Fact]
    public async Task On_the_real_server_a_post_under_the_limit_with_a_large_file_still_works_and_reaches_the_api_in_full()
    {
        var (client, factory) = Kestrel();
        using var _ = client;
        await using var __ = factory;
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);

        using var response = await client.SendAsync(Post(token, 9 * 1024 * 1024, expectContinue: true), Ct);

        // 9 MiB is under the 10 MiB file limit and under the form limit: it is accepted and redirected (HttpClient follows the redirect to the received page).
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("We have received your request.");
        var sent = factory.Api.Requests.Single(r => r.Method == HttpMethod.Post);
        sent.Body.ShouldNotBeNull().Length.ShouldBeGreaterThan(9 * 1024 * 1024);
    }

    [Fact]
    public async Task On_the_real_server_the_antiforgery_check_still_comes_first_for_a_small_post_without_a_token()
    {
        var (client, factory) = Kestrel();
        using var _ = client;
        await using var __ = factory;
        await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);

        using var response = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    [Fact]
    public async Task The_limit_the_page_applies_is_exactly_the_contract_form_limit()
    {
        var (client, factory) = Kestrel();
        using var _ = client;
        await using var __ = factory;
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);

        // One byte over the declared limit is refused; the same request one byte under is not (it fails later, on the file rule, because it is a big file of no allowed shape for the stub).
        using var content = FormTestKit.ContactForm(token);
        var small = await content.ReadAsByteArrayAsync(Ct);
        using var over = new HttpRequestMessage(HttpMethod.Post, FormTestKit.Path) { Content = new ByteArrayContent(new byte[(int)IntakeLimits.FormBodyBytes + 1]) };
        over.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(content.Headers.ContentType!.ToString());
        over.Headers.ExpectContinue = true;

        using var refused = await client.SendAsync(over, Ct);

        small.Length.ShouldBeLessThan((int)IntakeLimits.FormBodyBytes);
        refused.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }
}
```

`tests/TechStrap.Portal.Tests/Logging/RequestLogRedactionHostTests.cs`

```diff
@@ -51,7 +51,7 @@ public sealed class RequestLogRedactionHostTests
 
         using var response = await client.GetAsync("/p/paperplane/contact?subject=Printer&name=Jane%20Doe&email=jane.doe%40example.com", Ct);
 
-        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound, "the contact page arrives in PHASE-09b; the request is logged all the same");
+        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, "the contact page of a known product; the request is logged all the same");
         AssertVerboseWasCaptured(factory);
         factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("name=[redacted]", StringComparison.Ordinal), "control: the query string was logged, with the name masked");
         factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("subject=[redacted]", StringComparison.Ordinal), "control: the subject was masked too");
```

`tests/TechStrap.Portal.Tests/PortalFactory.cs`

```diff
@@ -55,6 +55,27 @@ internal sealed class PortalFactory(
 
     public CollectingSink LogSink { get; } = new();
 
+    /// <summary>
+    /// When set, disposing the factory asserts that every API call the host made carried <c>X-Forwarded-For: {ExpectedClientIp}</c> (<c>AssertEveryCallBore</c>), so a host test that makes an API call can never
+    /// pass with a lost forwarded-IP handler (Review Focus 5). A test that made no call passes. The form and ticket kits set it for every host they build.
+    /// </summary>
+    public string? ExpectedClientIp { get; set; }
+
+    public override async ValueTask DisposeAsync()
+    {
+        try
+        {
+            if (ExpectedClientIp is not null && Api.Requests.Count > 0)
+            {
+                Api.AssertEveryCallBore(ExpectedClientIp);
+            }
+        }
+        finally
+        {
+            await base.DisposeAsync();
+        }
+    }
+
     protected override void ConfigureWebHost(IWebHostBuilder builder)
     {
         builder.UseEnvironment(environment);
```

`tests/TechStrap.Portal.Tests/Routing/PortalRoutesTests.cs`

```diff
@@ -74,6 +74,14 @@ public sealed class PortalRoutesTests
         PortalRoutes.Ticket("a b").ShouldBe("/t/a%20b");
     }
 
+    [Fact]
+    public void The_received_page_builder_adds_the_reference_escaped_so_it_can_never_add_a_parameter()
+    {
+        PortalRoutes.ContactReceived("paperplane", "CfDJ8_a-b").ShouldBe("/p/paperplane/contact/received?ref=CfDJ8_a-b");
+        PortalRoutes.ContactReceived("paperplane", "a&b=c#d e").ShouldBe("/p/paperplane/contact/received?ref=a%26b%3Dc%23d%20e");
+        PortalRoutes.ReceivedReferenceParameter.ShouldBe("ref");
+    }
+
     [Fact]
     public void A_builder_given_a_ticket_token_uses_its_real_value_and_never_the_printed_marker()
     {
```


- [ ] **Step 2: Run the tests to see them fail**

The source of Steps 3 to 5 does not exist yet, so:

Run: `dotnet build tests/TechStrap.Portal.Tests -c Release`
Expected: FAIL to compile, the first errors `error CS0234: The type or namespace name 'Forms' does not exist in the namespace 'TechStrap.Portal'` (in `AttachmentRulesTests.cs`, `ContactFormValidatorTests.cs`, `ContactReceivedHostTests.cs`, `FormCopyTests.cs`, `FormFailureTests.cs`, `ReceivedReferenceTests.cs`), `error CS0246: ... 'ContactFormViewModel' could not be found`, `'ReceivedReference' could not be found` and `error CS0234 ... 'Uploads' does not exist` (`RequestTooLargeMiddlewareTests.cs`).

- [ ] **Step 3: Implement the shared form pieces**

`FormFailure` keeps the API's words out of the page; `AttachmentRules` is the Portal's check of the files against the Contracts limits, naming the file at fault by its cleaned name, and `ToUploads` opens a file only when the request is built, always with the per-file limit (the framework's default of 512,000 bytes would throw on a normal screenshot). `ReceivedReference` is the protected, 10-minute reference: it carries the ticket number only, refuses a value that is too long without unprotecting it, and refuses a payload that is not a ticket number even if it is authentic.

`src/TechStrap.Portal/Forms/FormError.cs` (new)

```csharp
namespace TechStrap.Portal.Forms;

/// <summary>The input ids of the Portal's forms, one per field. They are also what an error summary links to (<c>#email</c>) and what an input's <c>aria-describedby</c> is built from (<c>email-error</c>).</summary>
public static class FormFields
{
    public const string Name = "name";
    public const string Email = "email";
    public const string Subject = "subject";
    public const string Body = "body";
    public const string Attachments = "attachments";

    /// <summary>The id of the error paragraph that belongs to a field.</summary>
    public static string ErrorId(string field) => $"{field}-error";

    /// <summary>The field an API validation target belongs to (<c>email</c>, <c>attachments</c> and so on, case-insensitive); null for anything else, which the summary shows without a link.</summary>
    public static string? FromTarget(string? target) => target?.Trim().ToLowerInvariant() switch
    {
        Name => Name,
        Email => Email,
        Subject => Subject,
        Body => Body,
        Attachments => Attachments,
        _ => null,
    };
}

/// <summary>
/// One thing a visitor must fix on a form: the field it is about (null for the whole form), the machine code (the API's own codes, such as <c>email-invalid</c>, plus the few the Portal adds before it asks
/// the API) and the sentence to show. The sentence is always the Portal's own copy (<see cref="FormCopy"/>), never the API's text.
/// </summary>
public sealed record FormError(string? Field, string Code, string Message);
```

`src/TechStrap.Portal/Forms/FormCopy.cs` (new)

```csharp
using System.Globalization;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Forms;

/// <summary>
/// The words the Portal's forms share (the contact form and the reply form): plain, human, blame-free, saying what happened, what is kept and what to do (UX brief). <see cref="For"/> maps every code the API
/// can send for a ticket or a reply to a sentence of the Portal's own, so the API's wording never reaches a visitor; an unknown code gets <see cref="ProblemCopy.Invalid"/>.
/// </summary>
public static class FormCopy
{
    private const long Mebibyte = 1024 * 1024;

    public const string SummaryHeading = "Please check the following";
    public const string AttachmentsLabel = "Attachments (optional)";
    public const string AttachmentsKept = "If anything goes wrong, files are not kept: choose them again.";

    // The codes the Portal itself adds before it asks the API.
    public const string NameRequiredCode = "name-required";

    public const string RateLimited = "Too many attempts. Wait a few minutes and try again. What you wrote is still here.";
    public const string Unavailable = "We could not send that just now. What you wrote is still here: try again in a moment.";

    /// <summary>The rule for attachments, stated before anyone picks a file (UX brief): size, count and type, from the same limits the API enforces.</summary>
    public static string AttachmentRules { get; } =
        $"Up to {IntakeLimits.MaxFiles} files: images and documents of {IntakeLimits.MaxFileBytes / Mebibyte} MB each and {IntakeLimits.MaxMessageBytes / Mebibyte} MB in all ({string.Join(", ", IntakeLimits.AllowedExtensions)}).";

    /// <summary>The value of the file input's <c>accept</c> attribute: a hint to the file picker, never the check (the API decides).</summary>
    public static string Accept { get; } = string.Join(",", IntakeLimits.AllowedExtensions);

    public static string For(string code) => code switch
    {
        NameRequiredCode => "Enter your name.",
        "name-too-long" => Invariant($"Your name must be at most {IntakeLimits.NameMaxLength} characters."),
        "email-required" => "Enter your email address.",
        "email-invalid" => "Enter a valid email address, like name@example.com.",
        "subject-required" => "Enter a subject.",
        "subject-too-long" => Invariant($"The subject must be at most {IntakeLimits.SubjectMaxLength} characters."),
        "body-required" => "Write a message.",
        "body-too-long" => Invariant($"The message must be at most {IntakeLimits.BodyMaxLength:N0} characters."),
        "attachments-too-many" => Invariant($"Attach at most {IntakeLimits.MaxFiles} files."),
        "attachments-too-large" => Invariant($"Your files add up to more than {IntakeLimits.MaxMessageBytes / Mebibyte} MB. Remove a file or send smaller ones."),
        "attachment-too-large" => Invariant($"A file is over {IntakeLimits.MaxFileBytes / Mebibyte} MB. Send a smaller one."),
        "attachment-empty" => "A file is empty. Remove it or choose another.",
        "attachment-type-not-allowed" => "One of the files is a type we cannot accept. " + AttachmentRules,
        ApiErrorCodes.PayloadTooLarge => ProblemCopy.PayloadTooLarge,
        ApiErrorCodes.UnsupportedMediaType => ProblemCopy.UnsupportedMediaType,
        _ => ProblemCopy.Invalid,
    };

    // The sentences that name a file, for the checks the Portal runs before it sends anything (the visitor learns which file, UX brief). The name is the cleaned one; the page encodes it.
    public static string TooLarge(string fileName) => Invariant($"{fileName} is over {IntakeLimits.MaxFileBytes / Mebibyte} MB. Send a smaller file.");

    public static string Empty(string fileName) => $"{fileName} is empty. Remove it or choose another.";

    public static string TypeNotAllowed(string fileName) => $"{fileName} is a type we cannot accept. {AttachmentRules}";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
```

`src/TechStrap.Portal/Forms/AttachmentRules.cs` (new)

```csharp
using Microsoft.AspNetCore.Components.Forms;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Forms;

/// <summary>
/// The Portal's check of the files a visitor picked, run before anything is sent (Review Focus 3): the count, each file's size and type, and the total, from the same <see cref="IntakeLimits"/> the API enforces.
/// It spares a round trip and names the file at fault; the API is still the authority and checks again. A file name comes from the visitor's browser, so every sentence uses the cleaned name
/// (<see cref="AttachmentFileName"/>) and the page encodes it. The checks use the size the browser declared; <see cref="ToUploads"/> opens each file with the per-file limit, so a body that is larger than it
/// said cannot be read past it.
/// </summary>
public static class AttachmentRules
{
    public static IReadOnlyList<FormError> Validate(IReadOnlyList<IBrowserFile>? files)
    {
        var errors = new List<FormError>();
        if (files is not { Count: > 0 })
        {
            return errors;
        }

        if (files.Count > IntakeLimits.MaxFiles)
        {
            errors.Add(new FormError(FormFields.Attachments, "attachments-too-many", FormCopy.For("attachments-too-many")));
        }

        foreach (var file in files)
        {
            var name = AttachmentFileName.Clean(file.Name);
            if (file.Size == 0)
            {
                errors.Add(new FormError(FormFields.Attachments, "attachment-empty", FormCopy.Empty(name)));
            }
            else if (file.Size > IntakeLimits.MaxFileBytes)
            {
                errors.Add(new FormError(FormFields.Attachments, "attachment-too-large", FormCopy.TooLarge(name)));
            }

            if (!IsAllowedType(name))
            {
                errors.Add(new FormError(FormFields.Attachments, "attachment-type-not-allowed", FormCopy.TypeNotAllowed(name)));
            }
        }

        if (files.Sum(file => file.Size) > IntakeLimits.MaxMessageBytes)
        {
            errors.Add(new FormError(FormFields.Attachments, "attachments-too-large", FormCopy.For("attachments-too-large")));
        }

        return errors;
    }

    /// <summary>The files as uploads for a client. Each is opened only when the request is built, with the per-file limit (without it the framework reads at most 512,000 bytes and throws).</summary>
    public static IReadOnlyList<AttachmentUpload> ToUploads(IReadOnlyList<IBrowserFile>? files) =>
        [.. (files ?? []).Select(file => new AttachmentUpload(file.Name, file.ContentType, () => file.OpenReadStream(IntakeLimits.MaxFileBytes)))];

    private static bool IsAllowedType(string cleanedName) =>
        IntakeLimits.AllowedExtensions.Contains(Path.GetExtension(cleanedName), StringComparer.OrdinalIgnoreCase);
}
```

`src/TechStrap.Portal/Forms/FormFailure.cs` (new)

```csharp
using Microsoft.AspNetCore.Http;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Forms;

/// <summary>
/// What a form shows when the API refused it (the contact form and the reply form), decided from the errors alone: the field errors for a 400 (the API's codes, the Portal's sentences), the attachment error for a
/// 413 or 415, a calm notice with a status for a rate limit, a conflict or an outage, and the uniform not-found when the product or ticket is gone. The visitor's text stays on the page in every case. Nothing the API
/// said is ever shown: the sentences are <see cref="FormCopy"/> and <see cref="ProblemCopy"/>.
/// </summary>
public sealed record FormFailure(IReadOnlyList<FormError> Errors, string? Notice, int Status, bool IsNotFound)
{
    public static FormFailure From(IReadOnlyList<ResultError> errors)
    {
        if (errors.Any(error => error.Kind == ResultErrorKind.NotFound))
        {
            return new FormFailure([], null, StatusCodes.Status404NotFound, true);
        }

        var first = errors[0];
        return first.Code switch
        {
            ApiErrorCodes.RateLimited => WithNotice(FormCopy.RateLimited, StatusCodes.Status429TooManyRequests),
            ApiErrorCodes.ReplyConflict => WithNotice(ProblemCopy.ReplyConflict, StatusCodes.Status409Conflict),
            ApiErrorCodes.ApiUnavailable or ApiErrorCodes.UnexpectedResponse or ApiErrorCodes.ApiError => WithNotice(FormCopy.Unavailable, StatusCodes.Status503ServiceUnavailable),
            ApiErrorCodes.PayloadTooLarge or ApiErrorCodes.UnsupportedMediaType =>
                new FormFailure([new FormError(FormFields.Attachments, first.Code, FormCopy.For(first.Code))], null, StatusCodes.Status200OK, false),
            _ => new FormFailure([.. errors.Select(Field)], null, StatusCodes.Status200OK, false),
        };
    }

    private static FormFailure WithNotice(string text, int status) => new([], text, status, false);

    // An attachment code always belongs to the attachments field, whatever target the API gave it.
    private static FormError Field(ResultError error) => new(
        error.Code.StartsWith("attachment", StringComparison.Ordinal) ? FormFields.Attachments : FormFields.FromTarget(error.Target),
        error.Code,
        FormCopy.For(error.Code));
}
```

`src/TechStrap.Portal/Forms/ContactFormViewModel.cs` (new)

```csharp
using Microsoft.AspNetCore.Components.Forms;

namespace TechStrap.Portal.Forms;

/// <summary>
/// What the contact form binds (D-045 addendum): the five text fields and the files, as a visitor typed or picked them. It is a plain class with settable properties because the static-SSR form binder fills it
/// from the post (the inputs are named <c>Form.Email</c> and so on). <see cref="Website"/> is the honeypot: a field no person sees, which the Portal sends to the API as it arrives. The files are the browser's
/// own <see cref="IBrowserFile"/>s (a part with no file chosen is not bound at all).
/// </summary>
public sealed class ContactFormViewModel
{
    public string? Name { get; set; }

    public string? Email { get; set; }

    public string? Subject { get; set; }

    public string? Body { get; set; }

    public string? Website { get; set; }

    public IReadOnlyList<IBrowserFile>? Files { get; set; }

    /// <summary>
    /// The form as the page opens it from <c>?subject=&amp;name=&amp;email=</c> (P09-T21): exactly these three, as typed text in the visible, editable inputs, never in a hidden field. They are checked on post
    /// like any typed value (same rules, same limits), so a prefill can be neither longer nor stranger than what a person could type.
    /// </summary>
    public static ContactFormViewModel FromPrefill(string? subject, string? name, string? email) => new() { Subject = subject, Name = name, Email = email };
}
```

`src/TechStrap.Portal/Forms/ContactFormValidator.cs` (new)

```csharp
using System.Net.Mail;
using TechStrap.Contracts.Intake;

namespace TechStrap.Portal.Forms;

/// <summary>
/// The Portal's check of the contact form before it asks the API (the API checks again and is the authority). Every limit is a Contracts constant (<see cref="IntakeLimits"/>), the same ones the inputs'
/// <c>maxlength</c> attributes use, and the codes are the API's own, so a failure here and a failure from the API read the same (<see cref="FormCopy.For"/>). Values are judged trimmed. Name is required here
/// (the UX brief) although the API only limits its length. The honeypot is not checked: a filled one is sent on, and the API answers it.
/// </summary>
public static class ContactFormValidator
{
    public static IReadOnlyList<FormError> Validate(ContactFormViewModel form)
    {
        var errors = new List<FormError>();

        var name = form.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            errors.Add(Error(FormFields.Name, FormCopy.NameRequiredCode));
        }
        else if (name.Length > IntakeLimits.NameMaxLength)
        {
            errors.Add(Error(FormFields.Name, "name-too-long"));
        }

        var email = form.Email?.Trim() ?? string.Empty;
        if (email.Length == 0)
        {
            errors.Add(Error(FormFields.Email, "email-required"));
        }
        else if (email.Length > IntakeLimits.EmailMaxLength || !LooksLikeAnAddress(email))
        {
            errors.Add(Error(FormFields.Email, "email-invalid"));
        }

        var subject = form.Subject?.Trim() ?? string.Empty;
        if (subject.Length == 0)
        {
            errors.Add(Error(FormFields.Subject, "subject-required"));
        }
        else if (subject.Length > IntakeLimits.SubjectMaxLength)
        {
            errors.Add(Error(FormFields.Subject, "subject-too-long"));
        }

        var body = form.Body?.Trim() ?? string.Empty;
        if (body.Length == 0)
        {
            errors.Add(Error(FormFields.Body, "body-required"));
        }
        else if (body.Length > IntakeLimits.BodyMaxLength)
        {
            errors.Add(Error(FormFields.Body, "body-too-long"));
        }

        errors.AddRange(AttachmentRules.Validate(form.Files));
        return errors;
    }

    private static FormError Error(string field, string code) => new(field, code, FormCopy.For(code));

    // Not a full address grammar (the API decides): one address, no display name, a dotted domain.
    private static bool LooksLikeAnAddress(string text) =>
        MailAddress.TryCreate(text, out var address) && address.Address == text && address.Host.Contains('.', StringComparison.Ordinal);
}
```

`src/TechStrap.Portal/Forms/ContactCopy.cs` (new)

```csharp
namespace TechStrap.Portal.Forms;

/// <summary>The words of the contact page and the received page (UX brief). The product's name is the only name on them: TechStrap's appears only in the "Powered by" footer.</summary>
public static class ContactCopy
{
    public const string Heading = "Contact support";
    public const string BrowseHelp = "Browse help articles";
    public const string NameLabel = "Your name";
    public const string EmailLabel = "Email address";
    public const string SubjectLabel = "Subject";
    public const string BodyLabel = "Message";
    public const string Submit = "Send message";

    // The received page.
    public const string ReceivedHeading = "We have received your request.";
    public const string YourNumber = "Your ticket number is";
    public const string ReceivedNext = "We have emailed you a link. Use it to follow the conversation and reply.";
    public const string ReceivedGeneric = "We have emailed you a link to follow it. Check your inbox and your spam folder.";
    public const string LostLinkPrompt = "Can't find the email?";

    public static string Title(string productName) => $"Contact {productName} support";

    public static string ReceivedTitle(string productName) => $"Request received: {productName}";

    public static string BackTo(string productName) => $"Back to {productName}";
}
```

`src/TechStrap.Portal/Forms/ReceivedReference.cs` (new)

```csharp
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace TechStrap.Portal.Forms;

/// <summary>
/// The <c>?ref=</c> value that carries a ticket number from the contact form to the "received" page (D-045 addendum): data-protected with a purpose of its own and time-limited to
/// <see cref="Lifetime"/>, so there is no cookie, no stored state and no access token in it. A value that has expired, was tampered with, was made by another key ring (a restart without a persisted ring) or is
/// not a ticket number is simply not accepted, and the page shows its generic confirmation. It carries the ticket number only: not the subject, not the address. <see cref="IDataProtectionProvider"/> has a persisted
/// key ring in production (<c>DATAPROTECTION__KEYRINGPATH</c>, which both compose files set).
/// </summary>
public sealed partial class ReceivedReference
{
    /// <summary>The data-protection purpose: another feature's protector cannot read this one's values, and a change of format becomes a new version.</summary>
    public const string Purpose = "TechStrap.Portal.ContactReceived.v1";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>A protected number is far shorter than this; a longer value is refused without being unprotected.</summary>
    public const int MaxReferenceLength = 512;

    private readonly ITimeLimitedDataProtector _protector;
    private readonly TimeProvider _clock;

    public ReceivedReference(IDataProtectionProvider provider, TimeProvider clock)
    {
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();
        _clock = clock;
    }

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9\-]{0,39}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TicketNumberShape();

    public string Protect(string ticketNumber) => _protector.Protect(ticketNumber, _clock.GetUtcNow().Add(Lifetime));

    public bool TryUnprotect(string? reference, out string ticketNumber)
    {
        ticketNumber = string.Empty;
        if (string.IsNullOrEmpty(reference) || reference.Length > MaxReferenceLength)
        {
            return false;
        }

        try
        {
            var number = _protector.Unprotect(reference);
            if (!TicketNumberShape().IsMatch(number))
            {
                return false;
            }

            ticketNumber = number;
            return true;
        }
        catch (CryptographicException)
        {
            // Expired, tampered with, not base64url, or made under another key: all the same to the page.
            return false;
        }
    }
}
```

`src/TechStrap.Portal/Uploads/RequestTooLargeMiddleware.cs` (new)

```csharp
using Microsoft.AspNetCore.Http.Features;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Uploads;

/// <summary>
/// Answers a post whose declared <c>Content-Length</c> is over the limit that applies to its endpoint (<c>[RequestSizeLimit]</c> on the form page; endpoint routing has already put it on
/// <see cref="IHttpMaxRequestBodySizeFeature"/> before any later middleware runs) with a plain 413 and a sentence, before anything is read. Without it the same post is rejected by Kestrel without being
/// buffered, but the antiforgery check reads the form first and reports the failed read as a 400 with the framework's own text about a token. A chunked body (no declared length) over the limit still
/// gets that 400; it is just as unbuffered. Must run before <c>UseAntiforgery</c>. A server without the feature (the in-memory test server) is a no-op.
/// </summary>
public sealed class RequestTooLargeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize is { } limit
            && context.Request.ContentLength is { } declared
            && declared > limit)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(ProblemCopy.PayloadTooLarge, context.RequestAborted);
            return;
        }

        await next(context);
    }
}
```


- [ ] **Step 4: Implement the components and the pages**

The form is a plain `<form method="post" action=... @formname=...>`: the framework would render the current URL as the action (the prefill included), so the page sets it. The honeypot is hidden by a style sheet class (the CSP allows no inline style), out of the tab order, `aria-hidden` and not autofilled. The error summary takes focus through the `autofocus` attribute (no script). `[RequestSizeLimit(IntakeLimits.FormBodyBytes)]` on the page is the limit. The page does nothing on a post until the base class has loaded the product, so an unknown product never reaches the handler.

`src/TechStrap.Portal/Components/Ui/ErrorSummary.razor` (new)

```razor
@if (Errors.Count > 0)
{
    <section class="ts-error-summary alert alert-danger" role="alert" tabindex="-1" autofocus aria-labelledby="error-summary-heading">
        <h2 id="error-summary-heading" class="h5">@Heading</h2>
        <ul>
            @foreach (var error in Errors)
            {
                <li>@if (error.Field is { } field){<a href="#@field">@error.Message</a>}else{@error.Message}</li>
            }
        </ul>
    </section>
}

@code {
    /// <summary>What to fix. Each item with a field links to its input; the summary takes focus when the page opens (the <c>autofocus</c> attribute, so it needs no script).</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<FormError> Errors { get; set; } = [];

    [Parameter]
    public string Heading { get; set; } = FormCopy.SummaryHeading;
}
```

`src/TechStrap.Portal/Components/Ui/FieldError.razor` (new)

```razor
@if (Message is not null)
{
    <p id="@FormFields.ErrorId(Field)" class="ts-field-error">@Message</p>
}

@code {
    /// <summary>The field the message is about: its error paragraph is <c>{field}-error</c>, which the input's <c>aria-describedby</c> names.</summary>
    [Parameter, EditorRequired]
    public string Field { get; set; } = string.Empty;

    [Parameter]
    public string? Message { get; set; }
}
```

`src/TechStrap.Portal/Components/Ui/FormField.razor` (new)

```razor
<div class="mb-3">
    <label for="@Field" class="form-label">@Label</label>
    @if (Rows > 0)
    {
        <textarea id="@Field" name="@Name" class="form-control" rows="@Rows" maxlength="@MaxLength" required aria-describedby="@(Error is null ? null : FormFields.ErrorId(Field))" aria-invalid="@(Error is null ? null : "true")">@Value</textarea>
    }
    else
    {
        <input id="@Field" name="@Name" type="@Type" class="form-control" value="@Value" maxlength="@MaxLength" autocomplete="@Autocomplete" required aria-describedby="@(Error is null ? null : FormFields.ErrorId(Field))" aria-invalid="@(Error is null ? null : "true")" />
    }
    <FieldError Field="@Field" Message="@Error" />
</div>

@code {
    /// <summary>The input id, from <see cref="FormFields"/>.</summary>
    [Parameter, EditorRequired]
    public string Field { get; set; } = string.Empty;

    /// <summary>The form-binder name of the input (<c>Form.Email</c>).</summary>
    [Parameter, EditorRequired]
    public string Name { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string Label { get; set; } = string.Empty;

    [Parameter]
    public string? Value { get; set; }

    [Parameter, EditorRequired]
    public int MaxLength { get; set; }

    [Parameter]
    public string Type { get; set; } = "text";

    [Parameter]
    public string? Autocomplete { get; set; }

    /// <summary>More than zero makes the field a textarea of that many rows.</summary>
    [Parameter]
    public int Rows { get; set; }

    [Parameter]
    public string? Error { get; set; }
}
```

`src/TechStrap.Portal/Components/Ui/AttachmentInput.razor` (new)

```razor
<div class="mb-3">
    <label for="@FormFields.Attachments" class="form-label">@FormCopy.AttachmentsLabel</label>
    <p id="attachments-hint" class="form-text">@FormCopy.AttachmentRules</p>
    <input id="@FormFields.Attachments" name="@Name" type="file" multiple accept="@FormCopy.Accept" class="form-control" aria-describedby="@Described" aria-invalid="@(Error is null ? null : "true")" />
    @if (Kept)
    {
        <p class="form-text">@FormCopy.AttachmentsKept</p>
    }
    <FieldError Field="@FormFields.Attachments" Message="@Error" />
</div>

@code {
    /// <summary>
    /// A plain multiple file input, which works without script (the files travel in the same post). The rule is stated before anyone picks a file; the Portal checks the files against it after the post, and the API
    /// checks again. A browser never keeps a chosen file across a round trip, so after an error the form says so (<paramref name="Kept"/>).
    /// </summary>
    [Parameter, EditorRequired]
    public string Name { get; set; } = string.Empty;

    [Parameter]
    public string? Error { get; set; }

    /// <summary>True after a post that came back with errors: the files have to be chosen again.</summary>
    [Parameter]
    public bool Kept { get; set; }

    private string Described => Error is null ? "attachments-hint" : $"attachments-hint {FormFields.ErrorId(FormFields.Attachments)}";
}
```

`src/TechStrap.Portal/Components/Ui/HoneypotField.razor` (new)

```razor
<div class="ts-hp" aria-hidden="true">
    <label for="contact-website">Website</label>
    <input id="contact-website" name="@Name" type="text" value="@Value" tabindex="-1" autocomplete="off" />
</div>

@code {
    /// <summary>
    /// The honeypot (D-045 addendum): a field no person sees. It is hidden by a style sheet class, not an inline style (the CSP allows none), left out of the tab order, hidden from assistive technology and marked
    /// not to be autofilled; a bot that fills it is answered by the API. It takes no part in the layout.
    /// </summary>
    [Parameter, EditorRequired]
    public string Name { get; set; } = string.Empty;

    [Parameter]
    public string? Value { get; set; }
}
```

`src/TechStrap.Portal/Components/Pages/Contact.razor` (new)

```razor
@attribute [Route(PortalRoutes.ContactTemplate)]
@attribute [Microsoft.AspNetCore.Mvc.RequestSizeLimit(IntakeLimits.FormBodyBytes)]
@using TechStrap.Contracts.Intake
@inherits ProductPageBase

@if (Theme is { } theme)
{
    <PageTitle>@ContactCopy.Title(theme.DisplayName)</PageTitle>
    <h1>@ContactCopy.Heading</h1>
    <p class="ts-help-link"><a href="@PortalRoutes.KbHome(theme.Key)">@ContactCopy.BrowseHelp</a></p>
    <ErrorSummary Errors="Errors" />
    @if (Notice is not null)
    {
        <p class="alert alert-warning" role="alert">@Notice</p>
    }
    <form method="post" action="@PortalRoutes.Contact(theme.Key)" enctype="multipart/form-data" novalidate @formname="@FormHandler" @onsubmit="SubmitAsync" class="ts-form">
        <AntiforgeryToken />
        <FormField Field="@FormFields.Name" Name="Form.Name" Label="@ContactCopy.NameLabel" Value="@Form?.Name" MaxLength="@IntakeLimits.NameMaxLength" Autocomplete="name" Error="@ErrorOf(FormFields.Name)" />
        <FormField Field="@FormFields.Email" Name="Form.Email" Label="@ContactCopy.EmailLabel" Value="@Form?.Email" MaxLength="@IntakeLimits.EmailMaxLength" Type="email" Autocomplete="email" Error="@ErrorOf(FormFields.Email)" />
        <FormField Field="@FormFields.Subject" Name="Form.Subject" Label="@ContactCopy.SubjectLabel" Value="@Form?.Subject" MaxLength="@IntakeLimits.SubjectMaxLength" Error="@ErrorOf(FormFields.Subject)" />
        <FormField Field="@FormFields.Body" Name="Form.Body" Label="@ContactCopy.BodyLabel" Value="@Form?.Body" MaxLength="@IntakeLimits.BodyMaxLength" Rows="8" Error="@ErrorOf(FormFields.Body)" />
        <AttachmentInput Name="Form.Files" Error="@ErrorOf(FormFields.Attachments)" Kept="@(Errors.Count > 0 || Notice is not null)" />
        <HoneypotField Name="Form.Website" Value="@Form?.Website" />
        <button type="submit" class="btn btn-primary">@ContactCopy.Submit</button>
    </form>
}
else if (UnavailableMessage is not null)
{
    <PageTitle>@ShellCopy.UnavailableTitle</PageTitle>
    <ProductUnavailable Message="@UnavailableMessage" RetryHref="@RetryHref" />
}
```

`src/TechStrap.Portal/Components/Pages/Contact.razor.cs` (new)

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The contact form of a product (P09-T06, T21): a static-SSR form with antiforgery that creates a ticket through <see cref="IPublicTicketClient"/> and redirects to the "received" page (post, redirect, get: a
/// refresh never sends it twice). Review Focus 3: the antiforgery token is checked by the framework before the handler runs (a post without one is a 400); the request size limit is on the page and applies before
/// the form is read; the files are checked against <c>IntakeLimits</c> before anything is sent; the honeypot goes to the API as it arrived; a prefilled value from <c>?subject&amp;name&amp;email</c> is shown in the
/// visible, editable inputs and checked on post exactly like typed text, and nothing is ever submitted for the visitor. A failure keeps what the visitor wrote (but not their files: a browser never keeps those).
/// An unknown, inactive or malformed product is the uniform 404, before the handler runs, because the base class loads the product on every request including the post.
/// </summary>
public partial class Contact : ProductPageBase
{
    /// <summary>The form's handler name: the framework posts it with the form and binds <see cref="Form"/> only when it matches.</summary>
    public const string FormHandler = "contact";

    [SupplyParameterFromForm(FormName = FormHandler)]
    private ContactFormViewModel? Form { get; set; }

    [SupplyParameterFromQuery(Name = "subject")]
    private string? PrefillSubject { get; set; }

    [SupplyParameterFromQuery(Name = "name")]
    private string? PrefillName { get; set; }

    [SupplyParameterFromQuery(Name = "email")]
    private string? PrefillEmail { get; set; }

    [Inject]
    private IPublicTicketClient Tickets { get; set; } = default!;

    [Inject]
    private ReceivedReference References { get; set; } = default!;

    [Inject]
    private NavigationManager Redirects { get; set; } = default!;

    [Inject]
    private IHttpContextAccessor Http { get; set; } = default!;

    private IReadOnlyList<FormError> Errors { get; set; } = [];

    private string? Notice { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is not null)
        {
            // A post binds Form; a first visit has none, so it opens with the prefill (empty when the query has none).
            Form ??= ContactFormViewModel.FromPrefill(PrefillSubject, PrefillName, PrefillEmail);
        }
    }

    private string? ErrorOf(string field) => Errors.FirstOrDefault(error => error.Field == field)?.Message;

    private async Task SubmitAsync()
    {
        if (Theme is null)
        {
            return;
        }

        var form = Form ??= new ContactFormViewModel();
        Errors = ContactFormValidator.Validate(form);
        if (Errors.Count > 0)
        {
            return;
        }

        var request = new NewTicketRequest(
            form.Email!.Trim(), form.Name!.Trim(), form.Subject!.Trim(), form.Body!.Trim(), form.Website, AttachmentRules.ToUploads(form.Files));
        var cancellation = Http.HttpContext?.RequestAborted ?? CancellationToken.None;
        var result = await Tickets.SubmitAsync(Key, request, cancellation);
        if (result.IsSuccess)
        {
            // Straight after the redirect: nothing else may run or render.
            Redirects.NavigateTo(PortalRoutes.ContactReceived(Key, References.Protect(result.Value.TicketNumber)));
            return;
        }

        var failure = FormFailure.From(result.Errors);
        if (failure.IsNotFound)
        {
            Redirects.NotFound();
            return;
        }

        Errors = failure.Errors;
        Notice = failure.Notice;
        if (failure.Status != StatusCodes.Status200OK && Http.HttpContext is { Response.HasStarted: false } context)
        {
            context.Response.StatusCode = failure.Status;
        }
    }
}
```

`src/TechStrap.Portal/Components/Pages/ContactReceived.razor` (new)

```razor
@attribute [Route(PortalRoutes.ContactReceivedTemplate)]
@inherits ProductPageBase

@if (Theme is { } theme)
{
    <PageTitle>@ContactCopy.ReceivedTitle(theme.DisplayName)</PageTitle>
    <h1>@ContactCopy.ReceivedHeading</h1>
    @if (TicketNumber is not null)
    {
        <p>@ContactCopy.YourNumber <strong class="ts-ticket-number">@TicketNumber</strong></p>
        <p>@ContactCopy.ReceivedNext</p>
    }
    else
    {
        <p>@ContactCopy.ReceivedGeneric</p>
    }
    <ul class="ts-next-links">
        <li><a href="@PortalRoutes.KbHome(theme.Key)">@ContactCopy.BrowseHelp</a></li>
        <li><a href="@PortalRoutes.ProductHome(theme.Key)">@ContactCopy.BackTo(theme.DisplayName)</a></li>
        <li><a href="@PortalRoutes.LostLink(theme.Key)">@ContactCopy.LostLinkPrompt</a></li>
    </ul>
}
else if (UnavailableMessage is not null)
{
    <PageTitle>@ShellCopy.UnavailableTitle</PageTitle>
    <ProductUnavailable Message="@UnavailableMessage" RetryHref="@RetryHref" />
}
```

`src/TechStrap.Portal/Components/Pages/ContactReceived.razor.cs` (new)

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Products;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The confirmation after the contact form (P09-T06). The ticket number arrives in a protected, 10-minute <c>?ref=</c> value (<see cref="ReceivedReference"/>). A value that is missing, expired, tampered with or
/// not a ticket number is not an error: the page shows its generic confirmation without a number, so a visitor who reloads late, or opens the address from history, still reads something true. The link to the ticket
/// is never shown here: it is proved by owning the mailbox.
/// </summary>
public partial class ContactReceived : ProductPageBase
{
    [SupplyParameterFromQuery(Name = "ref")]
    private string? Reference { get; set; }

    [Inject]
    private ReceivedReference References { get; set; } = default!;

    private string? TicketNumber { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is not null && References.TryUnprotect(Reference, out var number))
        {
            TicketNumber = number;
        }
    }
}
```


- [ ] **Step 5: Wire the host, the route builder and the styles**

`RequestTooLargeMiddleware` goes before `UseAntiforgery` (the spike: the framework reads the form there and reports a failed read as a 400 about a token). The reference needs the data-protection key ring and a clock.

`src/TechStrap.Portal/Components/_Imports.razor`

```diff
@@ -1,3 +1,4 @@
+@using Microsoft.AspNetCore.Components.Forms
 @using Microsoft.AspNetCore.Components.Routing
 @using Microsoft.AspNetCore.Components.Web
 @using TechStrap.Contracts.Kb
@@ -5,5 +6,6 @@
 @using TechStrap.Portal.Components
 @using TechStrap.Portal.Components.Layout
 @using TechStrap.Portal.Components.Ui
+@using TechStrap.Portal.Forms
 @using TechStrap.Portal.Products
 @using TechStrap.Portal.Routing
```

`src/TechStrap.Portal/Program.cs`

```diff
@@ -1,3 +1,4 @@
+using Microsoft.Extensions.DependencyInjection.Extensions;
 using SyntaxCircus.AspNetCore.Common;
 using SyntaxCircus.DotEnv;
 using TechStrap.Hosting.Security;
@@ -5,10 +6,12 @@ using TechStrap.Hosting.Wiring;
 using TechStrap.Portal.Clients;
 using TechStrap.Portal.Components;
 using TechStrap.Portal.Components.Ui;
+using TechStrap.Portal.Forms;
 using TechStrap.Portal.Headers;
 using TechStrap.Portal.Products;
 using TechStrap.Portal.Seo;
 using TechStrap.Portal.Settings;
+using TechStrap.Portal.Uploads;
 
 const string ServiceName = "techstrap-portal";
 
@@ -37,6 +40,10 @@ builder.Services.AddPortalApiClients();
 // The product the current request is about, read by the layout (one per request).
 builder.Services.AddScoped<ProductScope>();
 
+// The protected, 10-minute reference that carries a ticket number to the "received" page (D-045 addendum). It needs the data-protection key ring (persisted in production) and a clock.
+builder.Services.TryAddSingleton(TimeProvider.System);
+builder.Services.AddSingleton<ReceivedReference>();
+
 // Meta tags, robots.txt and the canonical-host redirect; Seo:BaseUrl comes from the public address (D-045).
 builder.Services.AddPortalSeo(builder.Configuration);
 // Installation-wide switch for the "Powered by TechStrap" footer (D-024); shown unless set to false.
@@ -58,6 +65,9 @@ telemetry.LogStartupWarning(app.Logger);
 app.UseTechStrapWebHost(PortalHeaderRules.Rules);
 app.UsePortalSeo();
 app.UseTechStrapErrorPages();
+
+// Before the antiforgery check, which reads the form: a post over the endpoint's size limit is a plain 413, not the framework's 400 about a token.
+app.UseMiddleware<RequestTooLargeMiddleware>();
 app.UseAntiforgery();
 app.MapStandardHealthChecks();
 app.MapPortalSeo();
```

`src/TechStrap.Portal/Routing/PortalRoutes.cs`

```diff
@@ -20,6 +20,9 @@ public static class PortalRoutes
     public const string LostLinkSegment = "lost-link";
     public const string SuggestSegment = "suggest";
 
+    /// <summary>The query parameter that carries the protected ticket reference to the "received" page.</summary>
+    public const string ReceivedReferenceParameter = "ref";
+
     public const string HomeTemplate = "/";
     public const string NotFoundTemplate = "/not-found";
     public const string ErrorTemplate = "/error";
@@ -48,6 +51,9 @@ public static class PortalRoutes
 
     public static string ContactReceived(string key) => $"{Contact(key)}/received";
 
+    /// <summary>The "received" page with its protected reference.</summary>
+    public static string ContactReceived(string key, string reference) => $"{ContactReceived(key)}?{ReceivedReferenceParameter}={Escape(reference)}";
+
     public static string LostLink(string key) => $"{ProductHome(key)}/lost-link";
 
     public static string KbHome(string key) => $"{ProductHome(key)}/kb";
```

`src/TechStrap.Portal/Styles/_components.scss`

```diff
@@ -128,3 +128,41 @@
 .ts-help-contact {
   margin-top: 24px;
 }
+
+// The contact form and the received page (PHASE-09b). The honeypot is hidden here, not with an inline style (the CSP allows none): out of the layout, out of sight, and never focusable (tabindex -1 on the input).
+.ts-hp {
+  position: absolute;
+  left: -10000px;
+  width: 1px;
+  height: 1px;
+  overflow: hidden;
+}
+
+.ts-help-link {
+  margin-bottom: 16px;
+}
+
+.ts-error-summary {
+  margin-bottom: 16px;
+
+  ul {
+    margin: 0;
+    padding-left: 20px;
+  }
+}
+
+.ts-field-error {
+  margin: 4px 0 0;
+  font-size: .875rem;
+  color: var(--p-error, #b3261e);
+}
+
+.ts-ticket-number {
+  font: 600 1.125rem var(--ts-font-mono);
+  user-select: all;
+}
+
+.ts-next-links {
+  margin-top: 24px;
+  padding-left: 20px;
+}
```


- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet build TechStrap.slnx -c Release --no-incremental`
Expected: `0 Warning(s)`, `0 Error(s)`.

Run: `dotnet test --project tests/TechStrap.Portal.Tests -c Release`
Expected: PASS: `total: 757, failed: 0` (593 before this task; the real-server tests start Kestrel on a free port).

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: PASS: `total: 293, failed: 0` (no inline script or style, no interactive render mode, HTTP only in `Clients/`; `Contact` uses `@onsubmit`, which the rule allows).

- [ ] **Step 7: Prove each pin with a mutation**

Run `git add -A` first.

| # | File | Replace | With | Command (after `--`) | Expected |
| --- | --- | --- | --- | --- | --- |
| 1 | `src/TechStrap.Portal/Components/Pages/Contact.razor` | `@attribute [Microsoft.AspNetCore.Mvc.RequestSizeLimit(IntakeLimits.FormBodyBytes)]` | (nothing) | `PT "/*/*/RequestTooLargeMiddlewareTests/*"` | KILLED, 2 failed of 9 |
| 2 | `src/TechStrap.Portal/Uploads/RequestTooLargeMiddleware.cs` | `declared > limit` | `declared >= limit` | `PT "/*/*/RequestTooLargeMiddlewareTests/*"` | KILLED, 1 failed of 9 |
| 3 | `src/TechStrap.Portal/Program.cs` | `app.UseMiddleware<RequestTooLargeMiddleware>();` | (nothing) | `PT "/*/*/RequestTooLargeMiddlewareTests/*"` | KILLED, 2 failed of 9 |
| 4 | `src/TechStrap.Portal/Forms/ReceivedReference.cs` | `TimeSpan.FromMinutes(10)` | `TimeSpan.FromMinutes(100)` | `PT "/*/*/ReceivedReferenceTests/*"` | KILLED, 2 failed of 20 |
| 5 | `src/TechStrap.Portal/Forms/ReceivedReference.cs` | `_clock.GetUtcNow().Add(Lifetime)` | `_clock.GetUtcNow().AddYears(1)` | `PT "/*/*/ReceivedReferenceTests/*"` | KILLED, 1 failed of 20 |
| 6 | `src/TechStrap.Portal/Forms/ReceivedReference.cs` | `if (!TicketNumberShape().IsMatch(number))` | `if (false)` | `PT "/*/*/ReceivedReferenceTests/*"` | KILLED (the run fails) |
| 8 | `src/TechStrap.Portal/Forms/ReceivedReference.cs` | `CreateProtector(Purpose)` | `CreateProtector("shared")` | `PT "/*/*/ReceivedReferenceTests/*"` | KILLED, 1 failed of 21 |
| 9 | `src/TechStrap.Portal/Components/Pages/Contact.razor.cs` | `form.Body!.Trim(), form.Website,` | `form.Body!.Trim(), null,` | `PT "/*/*/ContactPostHostTests/*"` | KILLED, 1 failed of 26 |
| 10 | `src/TechStrap.Portal/Components/Pages/Contact.razor.cs` | `References.Protect(result.Value.TicketNumber)` | `result.Value.TicketNumber` | `PT "/*/*/ContactPostHostTests/*"` | KILLED, 2 failed of 26 |
| 11 | `src/TechStrap.Portal/Components/Pages/Contact.razor.cs` | `Form ??= ContactFormViewModel.FromPrefill(PrefillSubject, PrefillName, PrefillEmail);` | `Form ??= new ContactFormViewModel();` | `PT "/*/*/ContactPageHostTests/*"` | KILLED, 3 failed of 15 |
| 12 | `src/TechStrap.Portal/Components/Pages/Contact.razor.cs` | `if (Errors.Count > 0)\n        {\n            return;\n        }` | `if (false)\n        {\n            return;\n        }` | `PT "/*/*/ContactPostHostTests/*"` | KILLED (the run fails) |
| 14 | `src/TechStrap.Portal/Components/Pages/Contact.razor` | `<AntiforgeryToken />` | (nothing) | `PT "/*/*/ContactPostHostTests/*"` | KILLED, 26 failed of 26 |
| 15 | `src/TechStrap.Portal/Components/Pages/Contact.razor` | `action="@PortalRoutes.Contact(theme.Key)"` | (nothing) | `PT "/*/*/ContactPageHostTests/*"` | KILLED, 2 failed of 15 |
| 16 | `src/TechStrap.Portal/Components/Ui/HoneypotField.razor` | `tabindex="-1"` | (nothing) | `PT "/*/*/ContactPageHostTests/*"` | KILLED, 1 failed of 15 |
| 17 | `src/TechStrap.Portal/Components/Ui/HoneypotField.razor` | `<div class="ts-hp" aria-hidden="true">` | `<div class="ts-hp">` | `PT "/*/*/ContactPageHostTests/*"` | KILLED, 1 failed of 15 |
| 18 | `src/TechStrap.Portal/Components/Ui/ErrorSummary.razor` | `tabindex="-1" autofocus` | (nothing) | `PT "/*/*/ContactPostHostTests/*"` | KILLED, 1 failed of 26 |
| 19 | `src/TechStrap.Portal/Forms/ContactFormValidator.cs` | `if (name.Length == 0)` | `if (false)` | `PT "/*/*/ContactFormValidatorTests/*"` | KILLED (the run fails) |
| 20 | `src/TechStrap.Portal/Forms/ContactFormValidator.cs` | `subject.Length > IntakeLimits.SubjectMaxLength` | `subject.Length >= IntakeLimits.SubjectMaxLength` | `PT "/*/*/ContactFormValidatorTests/*"` | KILLED, 2 failed of 23 |
| 21 | `src/TechStrap.Portal/Forms/ContactFormValidator.cs` | `address.Host.Contains` | `address.Host.Length > 0 \|\| address.Host.Contains` | `PT "/*/*/ContactFormValidatorTests/*"` | KILLED (the run fails) |
| 22 | `src/TechStrap.Portal/Forms/AttachmentRules.cs` | `file.Size > IntakeLimits.MaxFileBytes` | `file.Size >= IntakeLimits.MaxFileBytes` | `PT "/*/*/AttachmentRulesTests/*"` | KILLED, 1 failed of 21 |
| 23 | `src/TechStrap.Portal/Forms/AttachmentRules.cs` | `file.OpenReadStream(IntakeLimits.MaxFileBytes)` | `file.OpenReadStream()` | `PT "/*/*/AttachmentRulesTests/*"` | KILLED, 1 failed of 21 |
| 24 | `src/TechStrap.Portal/Forms/AttachmentRules.cs` | `files.Sum(file => file.Size) > IntakeLimits.MaxMessageBytes` | `false` | `PT "/*/*/AttachmentRulesTests/*"` | KILLED (the run fails) |
| 25 | `src/TechStrap.Portal/Forms/AttachmentRules.cs` | `files.Count > IntakeLimits.MaxFiles` | `files.Count > IntakeLimits.MaxFiles + 1` | `PT "/*/*/AttachmentRulesTests/*"` | KILLED, 1 failed of 21 |
| 26 | `src/TechStrap.Portal/Forms/FormFailure.cs` | `WithNotice(FormCopy.RateLimited, StatusCodes.Status429TooManyRequests)` | `WithNotice(FormCopy.RateLimited, StatusCodes.Status200OK)` | `PT "/*/*/FormFailureTests/*"` | KILLED, 1 failed of 15 |
| 27 | `src/TechStrap.Portal/Forms/FormFailure.cs` | `FormCopy.For(error.Code));` | `error.Message);` | `PT "/*/*/FormFailureTests/*"` | KILLED, 2 failed of 15 |
| 29 | `src/TechStrap.Portal/Forms/FormCopy.cs` | `"email-invalid" => "Enter a valid email address, like name@example.com.",` | `"email-invalid" => "Invalid.",` | `PT "/*/*/ContactFormValidatorTests/*"` | KILLED, 9 failed of 23 |
| 30 | `src/TechStrap.Portal/Forms/FormFailure.cs` | `error.Code.StartsWith("attachment", StringComparison.Ordinal) ? FormFields.Attachments : FormFields.FromTarget(error.Target)` | `FormFields.FromTarget(error.Target)` | `PT "/*/*/FormFailureTests/*"` | KILLED, 3 failed of 15 |
| 31 | `src/TechStrap.Portal/Clients/ApiClientRegistration.cs` | `.ConfigureHttpClient((sp, client) => ConfigureClient(sp, client, WriteTimeoutSeconds))\n            .AddForwardedClientIp()` | `.ConfigureHttpClient((sp, client) => ConfigureClient(sp, client, WriteTimeoutSeconds))` | `dotnet test --project tests/TechStrap.Portal.Tests -c Release` | KILLED, 22 failed of 757 |
| 32 | `src/TechStrap.Portal/Clients/ApiClientRegistration.cs` | `.ConfigureHttpClient((sp, client) => ConfigureClient(sp, client, ReadTimeoutSeconds))\n            .AddForwardedClientIp()` | `.ConfigureHttpClient((sp, client) => ConfigureClient(sp, client, ReadTimeoutSeconds))` | `dotnet test --project tests/TechStrap.Portal.Tests -c Release` | KILLED, 65 failed of 757 |

Rows 31 and 32 remove `AddForwardedClientIp()` from the write and the read client: every contact test that makes a call then fails when its factory is disposed. Honest survivors (equivalent mutants; they were removed from the table): replacing `reference.Length > MaxReferenceLength` with `false` (a value over 512 characters fails to unprotect anyway, so the guard only saves the work), and deleting the explicit `failure.IsNotFound` branch of the post (the 404 status the generic path would set renders the same neutral page). Rows 8 and 29 first SURVIVED under the wrong test filter or a missing positive test; the table shows the re-runs after `A_value_protected_for_the_features_own_purpose_by_the_same_key_ring_is_accepted` was added and the filter was corrected.

- [ ] **Step 8: Verify and commit**

Run: `dotnet build TechStrap.slnx -c Release --no-incremental`
Expected: `0 Warning(s)`, `0 Error(s)`.

```bash
git status --short
git add -A src tests
git diff --cached --stat
git commit -F - <<'EOF'
feat(portal): the contact page, the received page and the form plumbing (PHASE-09b)

A static-SSR contact form with antiforgery, a request size limit that applies before the form is read, a
plain multiple file input checked against IntakeLimits, a honeypot passed through to the API, the
?subject&name&email prefill, the Portal's own sentences for every API failure, and a received page that
shows the ticket number from a protected 10-minute reference.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
EOF
```

### Task 4: Suggestions: the `/p/{key}/suggest` adapter and the `<ts-kb-suggestions>` element beside the subject

**Review Focus pin:** 4 (the adapter returns plain text and links the Portal builds; the module writes text with `textContent` only and never builds markup) and 5 (a request to `/p/x/suggest` behind a trusted proxy reaches the API with the visitor's address). The form never depends on the element.

**Files:**

- Create: `src/TechStrap.Portal/Suggestions/SuggestEndpoint.cs`
- Create: `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js`
- Modify: `src/TechStrap.Portal/Components/Pages/Contact.razor`
- Modify: `src/TechStrap.Portal/Forms/ContactCopy.cs`
- Modify: `src/TechStrap.Portal/Program.cs`
- Modify: `src/TechStrap.Portal/Styles/_components.scss`
- Test (create): `scripts/tests/PortalScripts.Tests.ps1`
- Test (create): `tests/TechStrap.Portal.Tests/Suggestions/ContactSuggestionsHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Suggestions/SuggestEndpointHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs`

**Interfaces:**
- Consumes: Task 2's `IPublicKbClient.SearchAsync`, `ApiErrorCodes.RateLimited`, the form-page header rule (it already covers `/p/{key}/suggest`); Task 3's `Contact.razor`, `ContactCopy`, `FormFields`, `FormTestKit`; 09a's `PortalRoutes.KbArticle`/`KbSearch`/`Suggest` (Task 1), `ProxyHopStartupFilter`, `AssertEveryCallBore`, the static asset pipeline (`@Assets[...]`).
- Produces:
  - `TechStrap.Portal.Suggestions.SuggestEndpoint.MapSuggest(IEndpointRouteBuilder)` and `MaxItems` (5); `SuggestionDto(string Title, string Snippet, string Href)` (JSON `title`, `snippet`, `href`).
  - `wwwroot/js/kb-suggestions.js`, an ES module defining `<ts-kb-suggestions>` and exporting `DEBOUNCE_MS` (300), `MIN_CHARS` (3), `MAX_ITEMS` (5), `MAX_QUERY_CHARS` (200), `isSafeHref`, `parseItems`, `createSuggester`, `render` and `defineKbSuggestions`. Attributes: `field` (the input's id) and `src` (a root-relative path).
  - `ContactCopy.SuggestFallback`; the element and the module script on the contact page only.
  - `tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` and `scripts/tests/PortalScripts.Tests.ps1` (skipped without node).

- [ ] **Step 1: Write the failing tests**

The node tests drive the module with a manual clock, a fake `fetch` that can answer out of order, a fake `AbortController` and a small fake DOM: the debounce and the minimum length, one request after rapid typing, a stale answer dropped, an error versus an abort, `dispose`, text written as text (a title with an `<img onerror>` stays a string), unsafe hrefs refused, the element's connect and disconnect, and a scan of the source for anything that builds markup. The host tests cover the adapter (the shape of the JSON, five items, an escaped query cut at 200 characters without splitting a surrogate pair, a blank query, 429 passed through, any other failure an empty list, the headers, the real address behind a proxy) and the contact page (the element after the subject, the module as a script file under the unchanged CSP, served as JavaScript, on no other page).

`scripts/tests/PortalScripts.Tests.ps1` (new)

```powershell
BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Node = Get-Command node -ErrorAction SilentlyContinue
}

Describe 'Portal browser scripts' {
    It 'passes the node:test suite for the KB suggestions element (debounce, stale requests, text only, clean-up)' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the kb-suggestions.js tests cannot run'
            return
        }

        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs'

        $output = & node --test $testFile 2>&1 | Out-String

        $LASTEXITCODE | Should -Be 0 -Because $output
    }

    It 'has the test file and the module it tests' {
        Test-Path -LiteralPath (Join-Path $script:RepoRoot 'tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $script:RepoRoot 'src/TechStrap.Portal/wwwroot/js/kb-suggestions.js') | Should -BeTrue
    }
}
```

`tests/TechStrap.Portal.Tests/Suggestions/ContactSuggestionsHostTests.cs` (new)

```csharp
using System.Net;
using System.Text.RegularExpressions;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Suggestions;

/// <summary>
/// P09-T07 at the host: the contact page carries the <c>ts-kb-suggestions</c> element beside the subject with its fallback inside, loads the module as a plain script file (no CSP change), the module is served as
/// JavaScript, and no other page loads it. The form itself never depends on the element: every post test of the contact page runs without script.
/// </summary>
public sealed class ContactSuggestionsHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<(HttpResponseMessage Response, string Html)> GetAsync(PortalFactory factory, string path)
    {
        using var client = FormTestKit.Client(factory);
        var response = await client.GetAsync(path, Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task The_element_sits_after_the_subject_field_with_its_field_its_url_a_polite_live_region_and_the_fallback_link_inside()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.Path);

        html.ShouldContain("<ts-kb-suggestions field=\"subject\" src=\"/p/paperplane/suggest\" class=\"ts-suggestions\" aria-live=\"polite\"><a href=\"/p/paperplane/kb/search\">Search the help articles first</a></ts-kb-suggestions>");
        html.IndexOf("id=\"subject\"", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("<ts-kb-suggestions", StringComparison.Ordinal));
        html.IndexOf("<ts-kb-suggestions", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("id=\"body\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_element_url_is_built_from_the_product_key_and_the_prefill_never_reaches_it()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.Path + "?subject=Printer&name=Jane&email=jane%40example.com");

        var element = Regex.Match(html, "<ts-kb-suggestions[^>]*>").Value;
        element.ShouldContain("src=\"/p/paperplane/suggest\"");
        element.ShouldNotContain("Printer");
        element.ShouldNotContain("jane");
    }

    [Fact]
    public async Task The_page_loads_the_module_as_a_script_file_and_has_no_inline_script()
    {
        await using var factory = FormTestKit.Factory();

        var (response, html) = await GetAsync(factory, FormTestKit.Path);

        var script = Regex.Match(html, "<script type=\"module\" src=\"([^\"]*kb-suggestions[^\"]*\\.js)\"></script>");
        script.Success.ShouldBeTrue("the contact page must load the module");
        Regex.Matches(html, "<script(?![^>]*\\bsrc=)").Count.ShouldBe(0, "no inline script");
        var directives = response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);
        directives.ShouldContain("script-src 'self'", "the module is a same-origin file: the policy is unchanged");
        directives.ShouldContain("connect-src 'self'");
    }

    [Fact]
    public async Task The_module_is_served_as_javascript_with_the_element_in_it_and_no_markup_from_text()
    {
        await using var factory = FormTestKit.Factory();
        var (_, html) = await GetAsync(factory, FormTestKit.Path);
        var src = Regex.Match(html, "<script type=\"module\" src=\"([^\"]*kb-suggestions[^\"]*\\.js)\"></script>").Groups[1].Value;
        using var client = FormTestKit.Client(factory);

        using var fingerprinted = await client.GetAsync("/" + src.TrimStart('/'), Ct);
        using var plain = await client.GetAsync("/js/kb-suggestions.js", Ct);

        foreach (var response in new[] { fingerprinted, plain })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("text/javascript");
            var text = await response.Content.ReadAsStringAsync(Ct);
            text.ShouldContain("ts-kb-suggestions");
            text.ShouldNotContain("innerHTML");
        }
    }

    [Theory]
    [InlineData("/p/paperplane")]
    [InlineData("/p/paperplane/contact/received")]
    [InlineData("/p/paperplane/lost-link")]
    public async Task No_other_page_loads_the_module(string path)
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, path);

        html.ShouldNotContain("kb-suggestions");
    }

    [Fact]
    public async Task The_form_still_posts_with_the_element_present_because_the_element_is_not_part_of_the_post()
    {
        await using var factory = FormTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        using var client = FormTestKit.Client(factory);
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);

        using var response = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(token), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Requests.Where(r => r.Path.Contains("/kb/")).ShouldBeEmpty("the page never searches by itself");
    }
}
```

`tests/TechStrap.Portal.Tests/Suggestions/SuggestEndpointHostTests.cs` (new)

```csharp
using System.Net;
using System.Text.Json;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Suggestions;

/// <summary>
/// P09-T07 at the host: the Portal-hosted suggest adapter behind the contact page's element (D-017 exempt: no workflow, plain JSON). It answers <c>[{title, snippet, href}]</c> with plain text and a Portal link
/// built from the product the visitor is on, at most five, for a query cut at the API's 200 characters; a blank query is an empty list without a call; the API's 429 is passed through and any other failure is an
/// empty list; the response is never stored and never indexed. Every call forwards the visitor's address through a real page request behind a trusted proxy (Review Focus 5), and the visitor's text never reaches a log.
/// </summary>
public sealed class SuggestEndpointHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Api = "/api/public/kb/paperplane/search";
    private const string Visitor = FormTestKit.Visitor;

    private static PublicKbSearchResultDto Hit(int n, string? title = null, string? snippet = null, string? slug = null) =>
        new(slug ?? $"article-{n}", title ?? $"Article {n}", snippet ?? $"About {n}.", "guides", "Guides", n % 2 == 0 ? null : "paperplane");

    private static PagedResponse<PublicKbSearchResultDto> Page(params PublicKbSearchResultDto[] hits) => new(hits, 1, 5, hits.Length);

    private static PortalFactory Host(PagedResponse<PublicKbSearchResultDto>? page = null)
    {
        var factory = FormTestKit.Factory(product: false);
        factory.Api.OnJson(HttpMethod.Get, Api, page ?? Page(Hit(1), Hit(2)));
        return factory;
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> GetAsync(PortalFactory factory, string pathAndQuery)
    {
        using var client = FormTestKit.Client(factory);
        var response = await client.GetAsync(pathAndQuery, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement);
    }

    [Fact]
    public async Task A_search_returns_title_snippet_and_a_link_built_from_the_visitors_product()
    {
        await using var factory = Host();

        var (response, body) = await GetAsync(factory, "/p/paperplane/suggest?q=reset%20password");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        body.GetArrayLength().ShouldBe(2);
        body[0].GetProperty("title").GetString().ShouldBe("Article 1");
        body[0].GetProperty("snippet").GetString().ShouldBe("About 1.");
        body[0].GetProperty("href").GetString().ShouldBe("/p/paperplane/kb/guides/article-1");
        body[1].GetProperty("href").GetString().ShouldBe("/p/paperplane/kb/guides/article-2", "a shared article is linked under the product the visitor is on");
        body[0].EnumerateObject().Select(p => p.Name).ShouldBe(["title", "snippet", "href"]);
    }

    [Fact]
    public async Task The_api_is_asked_for_five_through_the_read_client_with_the_visitors_address_and_the_text_escaped()
    {
        await using var factory = Host();

        await GetAsync(factory, "/p/paperplane/suggest?q=a%26category%3Dsecret%23x");

        var sent = factory.Api.Requests.ShouldHaveSingleItem();
        sent.Path.ShouldBe(Api);
        sent.Query.ShouldBe("?q=a%26category%3Dsecret%23x&pageSize=5");
        sent.Client.ShouldBe(ApiClientNames.Read);
        sent.TicketToken.ShouldBeNull();
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Fact]
    public async Task Text_in_the_answer_stays_text_in_the_json_and_the_href_never_comes_from_the_api()
    {
        var evil = new PublicKbSearchResultDto("../../x", "<img src=x onerror=alert(1)>", "<script>alert(1)</script>", "javascript:alert(1)", "<b>Guides</b>", null);
        await using var factory = Host(Page(evil));

        var (_, body) = await GetAsync(factory, "/p/paperplane/suggest?q=anything");

        body[0].GetProperty("title").GetString().ShouldBe("<img src=x onerror=alert(1)>");
        body[0].GetProperty("snippet").GetString().ShouldBe("<script>alert(1)</script>");
        body[0].GetProperty("href").GetString().ShouldBe("/p/paperplane/kb/javascript%3Aalert%281%29/..%2F..%2Fx", "every segment is escaped, so the link is a path of this site and nothing else");
        body[0].GetProperty("href").GetString()!.ShouldStartWith("/p/paperplane/kb/");
    }

    [Fact]
    public async Task At_most_five_items_come_back()
    {
        await using var factory = Host(Page([.. Enumerable.Range(1, 8).Select(n => Hit(n))]));

        var (_, body) = await GetAsync(factory, "/p/paperplane/suggest?q=many");

        body.GetArrayLength().ShouldBe(5);
    }

    [Fact]
    public async Task A_hit_with_no_slug_or_no_title_is_left_out()
    {
        await using var factory = Host(Page(Hit(1, slug: ""), Hit(2, title: " "), Hit(3)));

        var (_, body) = await GetAsync(factory, "/p/paperplane/suggest?q=x3x");

        body.GetArrayLength().ShouldBe(1);
        body[0].GetProperty("title").GetString().ShouldBe("Article 3");
    }

    [Theory]
    [InlineData("/p/paperplane/suggest")]
    [InlineData("/p/paperplane/suggest?q=")]
    [InlineData("/p/paperplane/suggest?q=%20%20%09")]
    public async Task A_blank_or_missing_query_is_an_empty_list_and_no_call(string path)
    {
        await using var factory = Host();

        var (response, body) = await GetAsync(factory, path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetArrayLength().ShouldBe(0);
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_long_query_is_cut_at_the_apis_200_characters()
    {
        await using var factory = Host();

        await GetAsync(factory, "/p/paperplane/suggest?q=" + new string('x', 500));

        var query = factory.Api.Requests.ShouldHaveSingleItem().Query;
        query.ShouldBe("?q=" + new string('x', KbLimits.MaxSearchTextChars) + "&pageSize=5");
    }

    [Fact]
    public async Task A_cut_never_splits_a_surrogate_pair()
    {
        await using var factory = Host();
        var text = new string('a', KbLimits.MaxSearchTextChars - 1) + char.ConvertFromUtf32(0x1F600);

        await GetAsync(factory, "/p/paperplane/suggest?q=" + Uri.EscapeDataString(text));

        factory.Api.Requests.ShouldHaveSingleItem().Query.ShouldBe("?q=" + new string('a', KbLimits.MaxSearchTextChars - 1) + "&pageSize=5");
    }

    [Theory]
    [InlineData("/p/Not_A_Slug/suggest?q=printer")]
    [InlineData("/p/paper%20plane/suggest?q=printer")]
    [InlineData("/p/..%2Fadmin/suggest?q=printer")]
    public async Task A_key_that_is_not_a_slug_is_an_empty_list_and_no_call(string path)
    {
        await using var factory = Host();

        var (response, body) = await GetAsync(factory, path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetArrayLength().ShouldBe(0);
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_apis_429_is_passed_through_as_a_429_with_an_empty_list()
    {
        await using var factory = Host();
        factory.Api.OnStatus(HttpMethod.Get, Api, HttpStatusCode.TooManyRequests);

        var (response, body) = await GetAsync(factory, "/p/paperplane/suggest?q=printer");

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        body.GetArrayLength().ShouldBe(0);
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Any_other_failure_is_an_empty_list(HttpStatusCode status)
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Get, Api, status, "x", "Npgsql host=10.0.0.5");

        var (response, body) = await GetAsync(factory, "/p/paperplane/suggest?q=printer");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_transport_failure_is_an_empty_list_too()
    {
        await using var factory = Host();
        factory.Api.On(HttpMethod.Get, Api, _ => throw new HttpRequestException("No connection could be made (10.1.2.3:5432)"));

        var (response, body) = await GetAsync(factory, "/p/paperplane/suggest?q=printer");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_response_is_no_store_noindex_and_has_the_shared_security_headers()
    {
        await using var factory = Host();

        var (response, _) = await GetAsync(factory, "/p/paperplane/suggest?q=printer");

        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
    }

    [Fact]
    public async Task Only_a_get_is_answered()
    {
        await using var factory = Host();
        using var client = FormTestKit.Client(factory);

        using var post = await client.PostAsync("/p/paperplane/suggest?q=printer", new StringContent(string.Empty), Ct);

        post.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task It_is_not_a_kb_category_and_the_kb_path_does_not_answer_it()
    {
        await using var factory = Host();

        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/paperplane/kb/suggest?q=printer", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, "the adapter is /p/{key}/suggest, beside the kb and not under it");
        response.Content.Headers.ContentType!.MediaType.ShouldNotBe("application/json");
    }
}
```

`tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` (new)

```javascript
// Runs with `node --test` (no browser): the module takes fetch, the timers, AbortController and the document as arguments, so a fake of each is enough.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';
import {
    DEBOUNCE_MS, MAX_ITEMS, MIN_CHARS, MAX_QUERY_CHARS, createSuggester, defineKbSuggestions, isSafeHref, parseItems, render,
} from '../../../src/TechStrap.Portal/wwwroot/js/kb-suggestions.js';

const sourcePath = new URL('../../../src/TechStrap.Portal/wwwroot/js/kb-suggestions.js', import.meta.url);

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

// A fetch that records its calls and answers when told to, so a test can answer them out of order.
function fakeFetch() {
    const calls = [];
    const fn = (url, options) => new Promise((resolve, reject) => {
        const call = { url, options, resolve, reject };
        options.signal.addEventListener?.('abort', () => reject(Object.assign(new Error('aborted'), { name: 'AbortError' })));
        calls.push(call);
    });
    fn.calls = calls;
    return fn;
}

function fakeAbortController() {
    const listeners = [];
    const signal = { aborted: false, addEventListener: (_, fn) => listeners.push(fn) };
    return { signal, abort() { signal.aborted = true; listeners.forEach((fn) => fn()); } };
}

const ok = (body) => ({ ok: true, status: 200, json: async () => body });
const flush = () => new Promise((resolve) => setImmediate(resolve));

function suggester(overrides = {}) {
    const clock = fakeClock();
    const fetchFn = fakeFetch();
    const events = [];
    const instance = createSuggester({
        url: '/p/paperplane/suggest',
        fetchFn,
        setTimer: clock.setTimer,
        clearTimer: clock.clearTimer,
        newAbortController: fakeAbortController,
        onItems: (items) => events.push(['items', items]),
        onError: () => events.push(['error']),
        ...overrides,
    });
    return { clock, fetchFn, events, instance };
}

const item = (n) => ({ title: `Article ${n}`, snippet: `About ${n}`, href: `/p/paperplane/kb/guides/a${n}` });

describe('constants', () => {
    it('debounces 300 ms, needs 3 characters, lists at most 5 and cuts the query at the API limit of 200', () => {
        assert.equal(DEBOUNCE_MS, 300);
        assert.equal(MIN_CHARS, 3);
        assert.equal(MAX_ITEMS, 5);
        assert.equal(MAX_QUERY_CHARS, 200);
    });
});

describe('isSafeHref', () => {
    it('accepts a root-relative path', () => {
        assert.equal(isSafeHref('/p/paperplane/kb/guides/dark-mode'), true);
    });

    it('refuses everything that could leave the site or run script', () => {
        for (const bad of ['//evil.example/x', 'https://evil.example', 'http://x', 'javascript:alert(1)', 'data:text/html,x', '/\\evil.example', '/a\nb', '/a\u0000b', '', '/', 'p/x', null, undefined, 5, {}]) {
            assert.equal(isSafeHref(bad), false, String(bad));
        }
    });
});

describe('parseItems', () => {
    it('keeps title, snippet and href of each usable item, at most five', () => {
        const items = parseItems([1, 2, 3, 4, 5, 6, 7].map(item));

        assert.equal(items.length, 5);
        assert.deepEqual(items[0], item(1));
    });

    it('drops an item with no title, a non-text field or an unsafe href, and anything that is not an array', () => {
        const body = [
            item(1),
            { title: '', snippet: 's', href: '/p/x' },
            { title: 't', snippet: 5, href: '/p/x' },
            { title: 't', snippet: 's', href: 'https://evil.example' },
            { title: 't', snippet: 's', href: '//evil.example' },
            null,
            'text',
        ];

        assert.deepEqual(parseItems(body), [item(1)]);
        for (const notAnArray of [null, undefined, {}, 'x', 5, { items: [item(1)] }]) {
            assert.deepEqual(parseItems(notAnArray), []);
        }
    });

    it('does not copy fields it was not asked for', () => {
        const [only] = parseItems([{ ...item(1), html: '<img src=x onerror=alert(1)>', extra: 1 }]);

        assert.deepEqual(Object.keys(only).sort(), ['href', 'snippet', 'title']);
    });
});

describe('the suggester', () => {
    it('makes no request until 300 ms after the last keystroke, then exactly one with the text escaped', async () => {
        const { clock, fetchFn, instance } = suggester();

        instance.input('pri');
        clock.advance(299);
        assert.equal(fetchFn.calls.length, 0);
        instance.input('printer & more?');
        clock.advance(299);
        assert.equal(fetchFn.calls.length, 0);
        clock.advance(1);

        assert.equal(fetchFn.calls.length, 1);
        assert.equal(fetchFn.calls[0].url, '/p/paperplane/suggest?q=printer%20%26%20more%3F');
        assert.equal(fetchFn.calls[0].options.credentials, 'same-origin');
        assert.equal(fetchFn.calls[0].options.headers.Accept, 'application/json');
    });

    it('rapid typing yields one search', () => {
        const { clock, fetchFn, instance } = suggester();

        for (const text of ['pri', 'prin', 'print', 'printe', 'printer']) {
            instance.input(text);
            clock.advance(100);
        }
        clock.advance(300);

        assert.equal(fetchFn.calls.length, 1);
        assert.ok(fetchFn.calls[0].url.endsWith('q=printer'));
    });

    it('trims the text, needs three characters and clears the list below that without a request', () => {
        const { clock, fetchFn, events, instance } = suggester();

        instance.input('  ab  ');
        instance.input('');
        instance.input(null);
        clock.advance(1000);

        assert.equal(fetchFn.calls.length, 0);
        assert.deepEqual(events, [['items', []], ['items', []], ['items', []]]);
    });

    it('cuts a long text at 200 characters', () => {
        const { clock, fetchFn, instance } = suggester();

        instance.input('x'.repeat(500));
        clock.advance(300);

        assert.equal(fetchFn.calls[0].url.split('q=')[1].length, MAX_QUERY_CHARS);
    });

    it('delivers the usable items of the answer', async () => {
        const { clock, fetchFn, events, instance } = suggester();
        instance.input('printer');
        clock.advance(300);

        fetchFn.calls[0].resolve(ok([item(1), item(2)]));
        await flush();

        assert.deepEqual(events, [['items', [item(1), item(2)]]]);
    });

    it('aborts a stale request and never shows its answer', async () => {
        const { clock, fetchFn, events, instance } = suggester();
        instance.input('first');
        clock.advance(300);
        instance.input('second');

        assert.equal(fetchFn.calls[0].options.signal.aborted, true, 'typing again aborts the request in flight');
        clock.advance(300);
        fetchFn.calls[0].resolve(ok([item(1)]));
        fetchFn.calls[1].resolve(ok([item(2)]));
        await flush();

        assert.deepEqual(events, [['items', [item(2)]]]);
    });

    it('does not show an answer that arrives after a shorter text cleared the list', async () => {
        const { clock, fetchFn, events, instance } = suggester();
        instance.input('printer');
        clock.advance(300);
        instance.input('pr');
        fetchFn.calls[0].resolve(ok([item(1)]));
        await flush();

        assert.deepEqual(events, [['items', []]]);
    });

    it('reports an error for a failed status, a network error and a body that is not json, but not for an abort', async () => {
        const { clock, fetchFn, events, instance } = suggester();

        instance.input('one');
        clock.advance(300);
        fetchFn.calls[0].resolve({ ok: false, status: 429, json: async () => [] });
        await flush();
        instance.input('two');
        clock.advance(300);
        fetchFn.calls[1].reject(new TypeError('network down'));
        await flush();
        instance.input('three');
        clock.advance(300);
        fetchFn.calls[2].resolve({ ok: true, status: 200, json: async () => { throw new SyntaxError('bad json'); } });
        await flush();
        instance.input('four');
        clock.advance(300);
        instance.input('five');

        assert.deepEqual(events, [['error'], ['error'], ['error']]);
    });

    it('treats a body that is not a list as nothing to suggest, not as an error', async () => {
        const { clock, fetchFn, events, instance } = suggester();
        instance.input('printer');
        clock.advance(300);

        fetchFn.calls[0].resolve(ok({ error: 'x' }));
        await flush();

        assert.deepEqual(events, [['items', []]]);
    });

    it('dispose clears the timer, aborts the request in flight and silences every callback', async () => {
        const { clock, fetchFn, events, instance } = suggester();
        instance.input('first');
        clock.advance(300);
        instance.input('second');
        assert.equal(clock.pending(), 1);

        instance.dispose();

        assert.equal(clock.pending(), 0, 'the timer is cleared');
        assert.equal(fetchFn.calls[0].options.signal.aborted, true, 'the request in flight is aborted');
        fetchFn.calls[0].resolve(ok([item(1)]));
        await flush();
        instance.input('third');
        clock.advance(1000);
        assert.deepEqual(events, []);
        assert.equal(fetchFn.calls.length, 1);
    });
});

// A small fake DOM: elements that record what the module does to them.
function fakeDocument() {
    const element = (tag) => ({
        tag,
        children: [],
        attrs: {},
        className: '',
        textContent: '',
        hidden: false,
        setAttribute(name, value) { this.attrs[name] = value; },
        appendChild(child) { this.children.push(child); return child; },
        replaceChildren() { this.children = []; },
    });
    return { createElement: element, byId: new Map(), getElementById(id) { return this.byId.get(id) ?? null; } };
}

describe('render', () => {
    it('writes titles and snippets with textContent and builds links with set href, new tab and noopener', () => {
        const doc = fakeDocument();
        const container = doc.createElement('ts-kb-suggestions');
        container.children = ['old'];

        render(doc, container, [{ title: '<img src=x onerror=alert(1)>', snippet: '<b>bold</b>', href: '/p/paperplane/kb/g/a' }, item(2)]);

        const [heading, list] = container.children;
        assert.equal(heading.textContent, '2 articles may help');
        const [first, second] = list.children;
        const link = first.children[0];
        assert.equal(link.tag, 'a');
        assert.equal(link.textContent, '<img src=x onerror=alert(1)> (opens in a new tab)', 'text goes in as text, never as markup');
        assert.equal(link.attrs.href, '/p/paperplane/kb/g/a');
        assert.equal(link.attrs.target, '_blank');
        assert.equal(link.attrs.rel, 'noopener noreferrer');
        assert.equal(first.children[1].textContent, '<b>bold</b>');
        assert.equal(second.children[0].attrs.href, item(2).href);
        assert.equal(container.children.length, 2, 'the old content is replaced');
    });

    it('says "1 article may help" for one and leaves the container empty for none', () => {
        const doc = fakeDocument();
        const container = doc.createElement('ts-kb-suggestions');

        render(doc, container, [item(1)]);
        assert.equal(container.children[0].textContent, '1 article may help');
        render(doc, container, []);
        assert.deepEqual(container.children, []);
    });

    it('leaves out the snippet line when the snippet is empty', () => {
        const doc = fakeDocument();
        const container = doc.createElement('x');

        render(doc, container, [{ title: 't', snippet: '', href: '/p/x' }]);

        assert.equal(container.children[1].children[0].children.length, 1);
    });
});

describe('the custom element', () => {
    function define(doc, overrides = {}) {
        const registry = new Map();
        const clock = fakeClock();
        const fetchFn = fakeFetch();
        class FakeHTMLElement {
            constructor() { this.attrs = {}; this.children = []; this.hidden = false; }
            getAttribute(name) { return this.attrs[name] ?? null; }
            replaceChildren() { this.children = []; }
            appendChild(child) { this.children.push(child); return child; }
        }
        const env = {
            customElements: { get: (name) => registry.get(name), define: (name, cls) => registry.set(name, cls) },
            HTMLElement: FakeHTMLElement,
            document: doc,
            fetch: fetchFn,
            setTimeout: clock.setTimer,
            clearTimeout: clock.clearTimer,
            AbortController: class { constructor() { return fakeAbortController(); } },
            ...overrides,
        };
        defineKbSuggestions(env);
        return { Element: registry.get('ts-kb-suggestions'), registry, clock, fetchFn };
    }

    function inputField() {
        const listeners = new Map();
        return {
            value: '',
            addEventListener: (name, fn) => { listeners.set(name, fn); },
            removeEventListener: (name, fn) => { if (listeners.get(name) === fn) { listeners.delete(name); } },
            type(text) { this.value = text; listeners.get('input')?.(); },
            listening: () => listeners.has('input'),
        };
    }

    function connected(doc, attrs = { field: 'subject', src: '/p/paperplane/suggest' }) {
        const kit = define(doc);
        const element = new kit.Element();
        element.attrs = attrs;
        element.children = ['fallback link'];
        return { ...kit, element };
    }

    it('is defined once, as ts-kb-suggestions, and defining it again changes nothing', () => {
        const doc = fakeDocument();
        const { registry } = define(doc);

        assert.deepEqual([...registry.keys()], ['ts-kb-suggestions']);
        defineKbSuggestions({ customElements: { get: () => registry.get('ts-kb-suggestions'), define: () => assert.fail('defined twice') }, HTMLElement: class {} });
    });

    it('does nothing in an environment with no custom elements', () => {
        defineKbSuggestions({ HTMLElement: class {} });
    });

    it('reads the field and the url from its attributes, removes the fallback and listens to the field', () => {
        const doc = fakeDocument();
        const field = inputField();
        doc.byId.set('subject', field);
        const { element } = connected(doc);

        element.connectedCallback();

        assert.deepEqual(element.children, []);
        assert.equal(element.hidden, false);
        assert.equal(field.listening(), true);
    });

    it('shows suggestions after the debounce and hides itself when the request fails, trying again on the next keystroke', async () => {
        const doc = fakeDocument();
        const field = inputField();
        doc.byId.set('subject', field);
        const { element, clock, fetchFn } = connected(doc);
        element.connectedCallback();

        field.type('printer');
        clock.advance(300);
        fetchFn.calls[0].resolve(ok([item(1)]));
        await flush();
        assert.equal(element.children[0].textContent, '1 article may help');

        field.type('printer jam');
        clock.advance(300);
        fetchFn.calls[1].resolve({ ok: false, status: 503, json: async () => [] });
        await flush();
        assert.equal(element.hidden, true);
        assert.deepEqual(element.children, []);

        field.type('printer jam again');
        clock.advance(300);
        fetchFn.calls[2].resolve(ok([item(2)]));
        await flush();
        assert.equal(element.hidden, false, 'a later success shows the region again');
    });

    it('hides itself, and never throws, when the field is missing or the url is not a path of this site', () => {
        const doc = fakeDocument();
        const { element } = connected(doc);
        element.connectedCallback();
        assert.equal(element.hidden, true);

        const field = inputField();
        doc.byId.set('subject', field);
        for (const src of ['https://evil.example/suggest', '//evil.example', 'javascript:alert(1)', null]) {
            const kit = connected(doc, { field: 'subject', src });
            kit.element.connectedCallback();
            assert.equal(kit.element.hidden, true, String(src));
            assert.equal(field.listening(), false);
        }
    });

    it('cleans up when it is removed: the listener goes, the timer is cleared and the request in flight is aborted', async () => {
        const doc = fakeDocument();
        const field = inputField();
        doc.byId.set('subject', field);
        const { element, clock, fetchFn } = connected(doc);
        element.connectedCallback();
        field.type('printer');
        clock.advance(300);

        element.disconnectedCallback();

        assert.equal(field.listening(), false);
        assert.equal(fetchFn.calls[0].options.signal.aborted, true);
        field.type('another');
        assert.equal(clock.pending(), 0);
        fetchFn.calls[0].resolve(ok([item(1)]));
        await flush();
        assert.deepEqual(element.children, []);
    });

    it('can be removed without ever having connected', () => {
        const doc = fakeDocument();
        const { element } = connected(doc);

        element.disconnectedCallback();
    });
});

describe('the source', () => {
    const source = readFileSync(sourcePath, 'utf8');
    const code = source.split('\n').filter((line) => !line.trim().startsWith('//') && !line.trim().startsWith('*') && !line.trim().startsWith('/**')).join('\n');

    it('never builds markup from text: no innerHTML, outerHTML, insertAdjacentHTML, document.write, eval or Function', () => {
        for (const forbidden of ['innerHTML', 'outerHTML', 'insertAdjacentHTML', 'document.write', 'eval(', 'new Function', 'setAttribute(\'on', 'srcdoc']) {
            assert.equal(code.includes(forbidden), false, `${forbidden} must not appear`);
        }
    });

    it('puts server text on the page with textContent', () => {
        assert.ok(code.includes('.textContent ='));
    });

    it('is plain ASCII', () => {
        assert.equal(/[^\x09\x0a\x0d\x20-\x7e]/.test(source), false);
    });
});
```


- [ ] **Step 2: Run the tests to see them fail**

The source of Steps 3 and 4 does not exist yet, so:

Run: `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs`
Expected: FAIL: `Error [ERR_MODULE_NOT_FOUND]: Cannot find module '.../src/TechStrap.Portal/wwwroot/js/kb-suggestions.js'`.

Run: `dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/SuggestEndpointHostTests/*"`
Expected: FAIL: `total: 22, failed: 21` (the one that passes is the check that `/p/{key}/kb/suggest` is not the adapter).

Run: `dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/ContactSuggestionsHostTests/*"`
Expected: FAIL: `total: 8, failed: 4` (the element, the script tag and the served module; the "no other page" cases and the post pass).

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/PortalScripts.Tests.ps1 -Output Minimal`
Expected: FAIL: `Tests Passed: 0, Failed: 2`.

(These tests compile against the Task 3 code, so there is no compile error to see.)

- [ ] **Step 3: Implement the module**

All of the module's dependencies are arguments (`fetch`, the timers, `AbortController`, the document), so `node --test` needs no browser. `createSuggester` holds the debounce and the request logic; `render` writes the list with `textContent` and links from root-relative paths only; `defineKbSuggestions` is the thin custom element (`connectedCallback` attaches the listener and removes the server-rendered fallback, `disconnectedCallback` removes the listener, clears the timer and aborts the request). The module defines the element when it runs in a browser.

`src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` (new)

```javascript
// <ts-kb-suggestions>: article suggestions beside the contact form's subject field (PHASE-09b, D-045 addendum). Vanilla JavaScript, no framework and no build step.
//
// Markup (server-rendered; the content of the element is the fallback shown when this script does not run):
//   <ts-kb-suggestions field="subject" src="/p/paperplane/suggest" aria-live="polite"><a href="...">Search help articles</a></ts-kb-suggestions>
//
// The module lists suggestions only: the form works without it and is never blocked by it. Everything it needs from the browser is passed in (fetch, the timers, AbortController, the document), so the
// behaviour is tested with `node --test` and no browser. Text from the server is plain text and is only ever put on the page with textContent; a link is only ever built from a root-relative path
// (isSafeHref). This file never parses text as markup, and a test fails if it starts to.

export const DEBOUNCE_MS = 300;
export const MIN_CHARS = 3;
export const MAX_ITEMS = 5;
export const MAX_QUERY_CHARS = 200;

/** A root-relative path on this site ("/p/x/kb/a/b"): not protocol-relative ("//host"), not an absolute URL, no backslash, no control character. */
export function isSafeHref(value) {
    return typeof value === 'string'
        && value.length > 1
        && value.startsWith('/')
        && !value.startsWith('//')
        && !/[\\\u0000-\u001f\u007f]/.test(value);
}

/** The usable items of a response body: an array of {title, snippet, href} with text titles and safe hrefs, at most MAX_ITEMS. Anything else is an empty list. */
export function parseItems(body) {
    if (!Array.isArray(body)) {
        return [];
    }

    return body
        .filter((item) => item && typeof item.title === 'string' && item.title.length > 0 && typeof item.snippet === 'string' && isSafeHref(item.href))
        .slice(0, MAX_ITEMS)
        .map((item) => ({ title: item.title, snippet: item.snippet, href: item.href }));
}

/**
 * The debounce and request logic, with no DOM. `input(text)` is called on every keystroke; after DEBOUNCE_MS of quiet (and at least MIN_CHARS characters) one request is made, and a newer
 * request aborts the older one, whose answer is dropped. `onItems(items)` gets the list (empty clears the region); `onError()` is called when the request failed in any way except being aborted.
 * `dispose()` clears the timer, aborts the request in flight and silences every callback.
 */
export function createSuggester({ url, fetchFn, setTimer, clearTimer, newAbortController, onItems, onError }) {
    let timer = null;
    let inflight = null;
    let disposed = false;

    function cancel() {
        if (timer !== null) {
            clearTimer(timer);
            timer = null;
        }

        if (inflight !== null) {
            inflight.abort();
            inflight = null;
        }
    }

    async function run(text) {
        const controller = newAbortController();
        inflight = controller;
        try {
            const response = await fetchFn(`${url}?q=${encodeURIComponent(text)}`, { signal: controller.signal, headers: { Accept: 'application/json' }, credentials: 'same-origin' });
            if (controller.signal.aborted || disposed) {
                return;
            }

            if (!response.ok) {
                throw new Error(`suggest ${response.status}`);
            }

            const body = await response.json();
            if (controller.signal.aborted || disposed) {
                return;
            }

            inflight = null;
            onItems(parseItems(body));
        } catch (error) {
            if (controller.signal.aborted || disposed || (error && error.name === 'AbortError')) {
                return;
            }

            inflight = null;
            onError();
        }
    }

    return {
        input(text) {
            if (disposed) {
                return;
            }

            cancel();
            const query = String(text ?? '').trim().slice(0, MAX_QUERY_CHARS);
            if (query.length < MIN_CHARS) {
                onItems([]);
                return;
            }

            timer = setTimer(() => {
                timer = null;
                void run(query);
            }, DEBOUNCE_MS);
        },
        dispose() {
            disposed = true;
            cancel();
        },
    };
}

/** Replaces the content of `container` with the list: a count line and the links, each opening in a new tab with the cue in its text. An empty list leaves the container empty (no "nothing found" noise). */
export function render(doc, container, items) {
    container.replaceChildren();
    if (items.length === 0) {
        return;
    }

    const heading = doc.createElement('p');
    heading.className = 'ts-suggest-heading';
    heading.textContent = items.length === 1 ? '1 article may help' : `${items.length} articles may help`;
    const list = doc.createElement('ul');
    list.className = 'ts-suggest-list';
    for (const item of items) {
        const row = doc.createElement('li');
        const link = doc.createElement('a');
        link.setAttribute('href', item.href);
        link.setAttribute('target', '_blank');
        link.setAttribute('rel', 'noopener noreferrer');
        link.textContent = `${item.title} (opens in a new tab)`;
        row.appendChild(link);
        if (item.snippet.length > 0) {
            const snippet = doc.createElement('span');
            snippet.className = 'ts-suggest-snippet';
            snippet.textContent = item.snippet;
            row.appendChild(snippet);
        }

        list.appendChild(row);
    }

    container.appendChild(heading);
    container.appendChild(list);
}

/**
 * Defines <ts-kb-suggestions> with the given environment (the browser's own, by default at the bottom of this file). The fallback content is removed when the element connects (it is only for browsers
 * without script) and the element stays empty until there is something to suggest. When the request fails the element hides itself; the next keystroke tries again.
 */
export function defineKbSuggestions(env) {
    if (!env.customElements || env.customElements.get('ts-kb-suggestions')) {
        return;
    }

    env.customElements.define('ts-kb-suggestions', class extends env.HTMLElement {
        connectedCallback() {
            const input = env.document.getElementById(this.getAttribute('field') ?? '');
            const url = this.getAttribute('src');
            if (!input || !isSafeHref(url)) {
                this.hidden = true;
                return;
            }

            this.replaceChildren();
            this._input = input;
            this._suggester = createSuggester({
                url,
                fetchFn: env.fetch,
                setTimer: env.setTimeout,
                clearTimer: env.clearTimeout,
                newAbortController: () => new env.AbortController(),
                onItems: (items) => {
                    this.hidden = false;
                    render(env.document, this, items);
                },
                onError: () => {
                    this.replaceChildren();
                    this.hidden = true;
                },
            });
            this._listener = () => this._suggester.input(input.value);
            input.addEventListener('input', this._listener);
        }

        disconnectedCallback() {
            if (this._input && this._listener) {
                this._input.removeEventListener('input', this._listener);
            }

            this._suggester?.dispose();
            this._suggester = null;
            this._listener = null;
            this._input = null;
        }
    });
}

if (typeof customElements !== 'undefined' && typeof document !== 'undefined') {
    defineKbSuggestions({
        customElements,
        HTMLElement,
        document,
        fetch: (...args) => fetch(...args),
        setTimeout: (...args) => setTimeout(...args),
        clearTimeout: (...args) => clearTimeout(...args),
        AbortController,
    });
}
```


- [ ] **Step 4: Implement the adapter and place the element**

The adapter asks the client for five, never trusts the API for a link (each `href` is `PortalRoutes.KbArticle(key, category, slug)`, every segment escaped), and passes the 429 through so the module can hide itself. The contact page renders the element after the subject field with a plain link to the help search inside it (what a browser without script shows) and loads the module with `<script type="module" src="@Assets[...]">`, which needs no CSP change.

`src/TechStrap.Portal/Components/Pages/Contact.razor`

```diff
@@ -18,11 +18,13 @@
         <FormField Field="@FormFields.Name" Name="Form.Name" Label="@ContactCopy.NameLabel" Value="@Form?.Name" MaxLength="@IntakeLimits.NameMaxLength" Autocomplete="name" Error="@ErrorOf(FormFields.Name)" />
         <FormField Field="@FormFields.Email" Name="Form.Email" Label="@ContactCopy.EmailLabel" Value="@Form?.Email" MaxLength="@IntakeLimits.EmailMaxLength" Type="email" Autocomplete="email" Error="@ErrorOf(FormFields.Email)" />
         <FormField Field="@FormFields.Subject" Name="Form.Subject" Label="@ContactCopy.SubjectLabel" Value="@Form?.Subject" MaxLength="@IntakeLimits.SubjectMaxLength" Error="@ErrorOf(FormFields.Subject)" />
+        <ts-kb-suggestions field="@FormFields.Subject" src="@PortalRoutes.Suggest(theme.Key)" class="ts-suggestions" aria-live="polite"><a href="@PortalRoutes.KbSearch(theme.Key)">@ContactCopy.SuggestFallback</a></ts-kb-suggestions>
         <FormField Field="@FormFields.Body" Name="Form.Body" Label="@ContactCopy.BodyLabel" Value="@Form?.Body" MaxLength="@IntakeLimits.BodyMaxLength" Rows="8" Error="@ErrorOf(FormFields.Body)" />
         <AttachmentInput Name="Form.Files" Error="@ErrorOf(FormFields.Attachments)" Kept="@(Errors.Count > 0 || Notice is not null)" />
         <HoneypotField Name="Form.Website" Value="@Form?.Website" />
         <button type="submit" class="btn btn-primary">@ContactCopy.Submit</button>
     </form>
+    <script type="module" src="@Assets["js/kb-suggestions.js"]"></script>
 }
 else if (UnavailableMessage is not null)
 {
```

`src/TechStrap.Portal/Forms/ContactCopy.cs`

```diff
@@ -11,6 +11,9 @@ public static class ContactCopy
     public const string BodyLabel = "Message";
     public const string Submit = "Send message";
 
+    /// <summary>The fallback inside the suggestions element, shown only without script: a plain link to the help search.</summary>
+    public const string SuggestFallback = "Search the help articles first";
+
     // The received page.
     public const string ReceivedHeading = "We have received your request.";
     public const string YourNumber = "Your ticket number is";
```

`src/TechStrap.Portal/Program.cs`

```diff
@@ -11,6 +11,7 @@ using TechStrap.Portal.Headers;
 using TechStrap.Portal.Products;
 using TechStrap.Portal.Seo;
 using TechStrap.Portal.Settings;
+using TechStrap.Portal.Suggestions;
 using TechStrap.Portal.Uploads;
 
 const string ServiceName = "techstrap-portal";
@@ -71,6 +72,7 @@ app.UseMiddleware<RequestTooLargeMiddleware>();
 app.UseAntiforgery();
 app.MapStandardHealthChecks();
 app.MapPortalSeo();
+app.MapSuggest();
 app.MapRazorComponentsWithStaticAssets<App>();
 
 app.Run();
```

`src/TechStrap.Portal/Styles/_components.scss`

```diff
@@ -157,6 +157,32 @@
   color: var(--p-error, #b3261e);
 }
 
+// The KB suggestions beside the subject field: empty until there is something to suggest, then a short list. Plain text; the links open in a new tab, so the form is never lost.
+.ts-suggestions {
+  display: block;
+  margin: -8px 0 16px;
+  font-size: .9375rem;
+
+  &:empty {
+    display: none;
+  }
+}
+
+.ts-suggest-heading {
+  margin: 0 0 4px;
+  font-weight: 600;
+}
+
+.ts-suggest-list {
+  margin: 0;
+  padding-left: 20px;
+}
+
+.ts-suggest-snippet {
+  display: block;
+  color: var(--p-ink2);
+}
+
 .ts-ticket-number {
   font: 600 1.125rem var(--ts-font-mono);
   user-select: all;
```

`src/TechStrap.Portal/Suggestions/SuggestEndpoint.cs` (new)

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Suggestions;

/// <summary>One suggestion for the contact page's element: plain text for the title and the snippet, and a path of this site for the link.</summary>
public sealed record SuggestionDto(string Title, string Snippet, string Href);

/// <summary>
/// <c>GET /p/{key}/suggest?q=</c>: the Portal-hosted adapter behind the contact page's <c>ts-kb-suggestions</c> element (D-045 addendum; exempt like D-017: it runs no workflow of its own, it asks the API's public
/// search and reshapes the answer). A browser script cannot call the API itself (no CSP allowance, and the API would see the Portal's address, not the visitor's), and a circuit would lose the visitor's address
/// (D-019), so this is an ordinary request through <see cref="IPublicKbClient"/>, which forwards the visitor's address like every Portal call. Rules: a blank or missing text is an empty list and no call (so is a key that is not a slug: the client refuses it without a call and any failure is an empty list); the text
/// is cut at <see cref="KbLimits.MaxSearchTextChars"/> without splitting a surrogate pair; at most <see cref="MaxItems"/> items; every link is built here with <see cref="PortalRoutes.KbArticle"/> from the
/// product the visitor is on (a shared article is linked under it), never from anything the API sent; the API's 429 is passed on as a 429 and any other failure is an empty list, because suggestions must never
/// get in the way of the form. The response is plain JSON and never stored (the form-page header rule adds noindex).
/// </summary>
public static class SuggestEndpoint
{
    public const int MaxItems = 5;

    public static IEndpointRouteBuilder MapSuggest(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(PortalRoutes.SuggestTemplate, HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(string key, string? q, IPublicKbClient kb, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var text = Clean(q);
        if (text.Length == 0)
        {
            return Results.Json(Array.Empty<SuggestionDto>());
        }

        var result = await kb.SearchAsync(key, text, MaxItems, cancellationToken);
        if (result.IsFailure)
        {
            return result.Errors[0].Code == ApiErrorCodes.RateLimited
                ? Results.Json(Array.Empty<SuggestionDto>(), statusCode: StatusCodes.Status429TooManyRequests)
                : Results.Json(Array.Empty<SuggestionDto>());
        }

        var items = result.Value.Items
            .Where(hit => !string.IsNullOrWhiteSpace(hit.Slug) && !string.IsNullOrWhiteSpace(hit.CategorySlug) && !string.IsNullOrWhiteSpace(hit.Title))
            .Take(MaxItems)
            .Select(hit => new SuggestionDto(hit.Title, hit.Snippet ?? string.Empty, PortalRoutes.KbArticle(key, hit.CategorySlug, hit.Slug)))
            .ToList();
        return Results.Json(items);
    }

    private static string Clean(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length <= KbLimits.MaxSearchTextChars)
        {
            return trimmed;
        }

        var cut = trimmed[..KbLimits.MaxSearchTextChars];
        return char.IsHighSurrogate(cut[^1]) ? cut[..^1] : cut;
    }
}
```


- [ ] **Step 5: Run the tests to see them pass**

Run: `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs`
Expected: PASS: `tests 29, pass 29, fail 0`.

Run: `dotnet build TechStrap.slnx -c Release --no-incremental`
Expected: `0 Warning(s)`, `0 Error(s)`.

Run: `dotnet test --project tests/TechStrap.Portal.Tests -c Release`
Expected: PASS: `total: 787, failed: 0` (757 before this task).

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: PASS: `total: 293, failed: 0` (an external `<script src>` is allowed; there is no inline script).

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/PortalScripts.Tests.ps1 -Output Minimal`
Expected: PASS: `Tests Passed: 2, Failed: 0`.

- [ ] **Step 6: Prove each pin with a mutation**

Run `git add -A` first. The module rows run the node suite (a KILLED node run prints no summary line); the others run the host tests. A mutation of the adapter's own key check survived (the client already refuses a malformed key without a call), so the dead check was removed instead of pinned.

| # | File | Replace | With | Command (after `--`) | Expected |
| --- | --- | --- | --- | --- | --- |
| 1 | `src/TechStrap.Portal/Suggestions/SuggestEndpoint.cs` | `.Take(MaxItems)` | (nothing) | `PT "/*/*/SuggestEndpointHostTests/*"` | KILLED, 1 failed of 22 |
| 2 | `src/TechStrap.Portal/Suggestions/SuggestEndpoint.cs` | `if (trimmed.Length <= KbLimits.MaxSearchTextChars)` | `if (true)` | `PT "/*/*/SuggestEndpointHostTests/*"` | KILLED (the run fails) |
| 3 | `src/TechStrap.Portal/Suggestions/SuggestEndpoint.cs` | `char.IsHighSurrogate(cut[^1]) ? cut[..^1] : cut` | `cut` | `PT "/*/*/SuggestEndpointHostTests/*"` | KILLED, 1 failed of 22 |
| 4 | `src/TechStrap.Portal/Suggestions/SuggestEndpoint.cs` | `statusCode: StatusCodes.Status429TooManyRequests` | `statusCode: StatusCodes.Status200OK` | `PT "/*/*/SuggestEndpointHostTests/*"` | KILLED, 1 failed of 22 |
| 5 | `src/TechStrap.Portal/Suggestions/SuggestEndpoint.cs` | `PortalRoutes.KbArticle(key, hit.CategorySlug, hit.Slug)` | `hit.Slug` | `PT "/*/*/SuggestEndpointHostTests/*"` | KILLED, 2 failed of 22 |
| 6 | `src/TechStrap.Portal/Suggestions/SuggestEndpoint.cs` | `text.Length == 0 \|\|` | (nothing) | `PT "/*/*/SuggestEndpointHostTests/*"` | KILLED, 3 failed of 22 |
| 8 | `src/TechStrap.Portal/Suggestions/SuggestEndpoint.cs` | `Code == ApiErrorCodes.RateLimited` | `Code != ApiErrorCodes.RateLimited` | `PT "/*/*/SuggestEndpointHostTests/*"` | KILLED, 6 failed of 22 |
| 9 | `src/TechStrap.Portal/Program.cs` | `app.MapSuggest();` | (nothing) | `PT "/*/*/SuggestEndpointHostTests/*"` | KILLED, 20 failed of 22 |
| 10 | `src/TechStrap.Portal/Components/Pages/Contact.razor` | `<script type="module" src="@Assets["js/kb-suggestions.js"]"></script>` | (nothing) | `PT "/*/*/ContactSuggestionsHostTests/*"` | KILLED, 2 failed of 8 |
| 11 | `src/TechStrap.Portal/Components/Pages/Contact.razor` | `aria-live="polite"><a href="@PortalRoutes.KbSearch(theme.Key)">` | `><a href="@PortalRoutes.KbSearch(theme.Key)">` | `PT "/*/*/ContactSuggestionsHostTests/*"` | KILLED, 1 failed of 8 |
| 12 | `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` | `export const DEBOUNCE_MS = 300;` | `export const DEBOUNCE_MS = 200;` | `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` | KILLED (the node suite fails) |
| 13 | `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` | `export const MIN_CHARS = 3;` | `export const MIN_CHARS = 2;` | `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` | KILLED (the node suite fails) |
| 14 | `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` | `export const MAX_ITEMS = 5;` | `export const MAX_ITEMS = 6;` | `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` | KILLED (the node suite fails) |
| 15 | `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` | `&& !value.startsWith('//')` | (nothing) | `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` | KILLED (the node suite fails) |
| 16 | `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` | `if (inflight !== null) {\n            inflight.abort();\n            inflight = null;\n        }` | `if (inflight !== null) {\n            inflight = null;\n        }` | `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` | KILLED (the node suite fails) |
| 17 | `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` | `disposed = true;\n            cancel();` | `cancel();` | `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` | KILLED (the node suite fails) |
| 18 | `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` | `'noopener noreferrer'` | `'noopener'` | `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` | KILLED (the node suite fails) |
| 19 | `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` | `this.replaceChildren();\n            this._input = input;` | `this._input = input;` | `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` | KILLED (the node suite fails) |
| 20 | `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` | `this._input.removeEventListener('input', this._listener);` | `void 0;` | `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` | KILLED (the node suite fails) |
| 21 | `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` | `onError: () => {\n                    this.replaceChildren();\n                    this.hidden = true;\n                },` | `onError: () => {\n                    this.replaceChildren();\n                },` | `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` | KILLED (the node suite fails) |
| 22 | `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` | `.slice(0, MAX_QUERY_CHARS)` | (nothing) | `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` | KILLED (the node suite fails) |
| 23 | `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` | `credentials: 'same-origin'` | `credentials: 'include'` | `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` | KILLED (the node suite fails) |
| 24 | `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` | `Array.isArray(body)` | `body` | `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` | KILLED (the node suite fails) |
| 25 | `src/TechStrap.Portal/wwwroot/js/kb-suggestions.js` | `\|\| !isSafeHref(url)` | `\|\| false` | `node --test tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` | KILLED, 0 failed of 2 |

- [ ] **Step 7: Verify and commit**

Run: `dotnet build TechStrap.slnx -c Release --no-incremental`
Expected: `0 Warning(s)`, `0 Error(s)`.

```bash
git status --short
git add -A src tests scripts
git diff --cached --stat
git commit -F - <<'EOF'
feat(portal): KB suggestions beside the subject: the suggest adapter and the ts-kb-suggestions element (PHASE-09b)

GET /p/{key}/suggest asks the API's public search with the visitor's address and returns plain
{title, snippet, href} JSON; a vanilla-JS custom element (tested with node) lists the suggestions beside the
subject field, debounced, with stale requests aborted, text written with textContent and clean-up on
disconnect. The contact form works without it.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
EOF
```

### Task 5: The ticket page, replies and the Closed follow-up, the attachment pass-through and the lost-link page

**Review Focus pin:** 1 (the token stays in the path and the header; the follow-up redirect never leaves the site), 2 (every way to fail to find a ticket or an attachment is the identical 404; the lost-link answers are byte-identical; an inactive product is the neutral theme), 3 (antiforgery and the size limit on the reply form), 4 (`CustomerMessageBody` is the only markup site; subjects, names and file names are encoded) and 5 (every ticket call forwards the visitor's address; reply and lost link are never retried).

**Files:**

- Create: `src/TechStrap.Portal/Components/Pages/LostLink.razor`
- Create: `src/TechStrap.Portal/Components/Pages/LostLink.razor.cs`
- Create: `src/TechStrap.Portal/Components/Pages/Ticket.razor`
- Create: `src/TechStrap.Portal/Components/Pages/Ticket.razor.cs`
- Create: `src/TechStrap.Portal/Components/Tickets/CustomerMessageBody.razor`
- Create: `src/TechStrap.Portal/Components/Tickets/MessageThread.razor`
- Create: `src/TechStrap.Portal/Components/Tickets/TicketStatusBanner.razor`
- Create: `src/TechStrap.Portal/Forms/EmailRules.cs`
- Create: `src/TechStrap.Portal/Forms/LostLinkForm.cs`
- Create: `src/TechStrap.Portal/Forms/ReplyForm.cs`
- Create: `src/TechStrap.Portal/Tickets/AttachmentPassThrough.cs`
- Create: `src/TechStrap.Portal/Tickets/CustomerTicketPresenter.cs`
- Create: `src/TechStrap.Portal/Tickets/CustomerTicketViewModel.cs`
- Create: `src/TechStrap.Portal/Tickets/FollowUpLink.cs`
- Create: `src/TechStrap.Portal/Tickets/TicketCopy.cs`
- Modify: `src/TechStrap.Portal/Clients/CustomerTicketClient.cs`
- Modify: `src/TechStrap.Portal/Clients/ICustomerTicketClient.cs`
- Modify: `src/TechStrap.Portal/Components/_Imports.razor`
- Modify: `src/TechStrap.Portal/Forms/ContactFormValidator.cs`
- Modify: `src/TechStrap.Portal/Program.cs`
- Modify: `src/TechStrap.Portal/Routing/PortalRoutes.cs`
- Modify: `src/TechStrap.Portal/Styles/_components.scss`
- Test (create): `tests/TechStrap.Portal.Tests/Forms/ReplyAndEmailRulesTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Tickets/CustomerTicketPresenterTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Tickets/FollowUpLinkTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Tickets/LostLinkHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Tickets/TicketAttachmentHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Tickets/TicketPageHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Tickets/TicketReplyHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Tickets/TicketTestKit.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Tickets/TicketUniformNotFoundHostTests.cs`
- Test (modify): `tests/TechStrap.Architecture.Tests/PortalRuleTests.cs`
- Test (modify): `tests/TechStrap.Architecture.Tests/PortalRules.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Api/StubApiHandler.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Clients/CustomerTicketClientTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Headers/TicketHeaderHostTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Routing/PortalRoutesTests.cs`

**Interfaces:**
- Consumes: Task 2's `ICustomerTicketClient` (`GetAsync`, `ReplyAsync`, `RequestAccessLinkAsync`), `ApiConnection.OpenStreamAsync`, `ApiDownload`, `CustomerReply`, `AttachmentUpload`, `AttachmentFileName`, `ProblemCopy`, `ApiErrorCodes`; Task 3's `FormError`, `FormFields`, `FormCopy`, `FormFailure`, `AttachmentRules`, the components (`ErrorSummary`, `FormField`, `AttachmentInput`), `FormTestKit`, `FakeBrowserFile`, `RequestTooLargeMiddleware`; Task 1's `CustomerTicketDto.ProductKey`; 09a's `TicketToken`, `PortalRoutes.Ticket`/`TicketAttachment`, `IPublicProductClient`, `ProductScope`, `ProductThemeViewModel`, `ProductPageBase`, `PortalHeaderRules` (the `/t` rules and the attachment sandbox), `ShellCopy`, the harness.
- Produces:
  - `TechStrap.Portal.Tickets`: `CustomerTicketPresenter.Present(CustomerTicketDto, TicketToken)` returning `CustomerTicketViewModel(Number, ProductKey, Subject, StatusLabel, StatusNote, IsSolved, IsClosed, Messages)` with `CustomerMessageViewModel(Id, Author, IsCustomer, BodyHtml, CreatedAt, Attachments)` and `CustomerAttachmentViewModel(Id, FileName, SizeText, Href)`; `CustomerTicketPresenter.Status`, `Author`, `Size`; `TicketCopy`; `FollowUpLink.TryGetToken(string?, out TicketToken)`; `AttachmentPassThrough.MapAttachmentPassThrough` and `AttachmentDisposition.Create(string?)`.
  - `ICustomerTicketClient.OpenAttachmentAsync(TicketToken, Guid, CancellationToken)` returning `Result<ApiDownload>`.
  - `TechStrap.Portal.Forms`: `EmailRules.Check(string?)`, `ReplyFormViewModel` and `ReplyFormValidator.Validate`, `LostLinkFormViewModel` and `LostLinkCopy`.
  - Components `CustomerMessageBody` (the single markup site), `MessageThread`, `TicketStatusBanner`; pages `Ticket` (`/t/{token}`) and `LostLink`; `PortalRoutes.LostLinkSent(key)` and `SentParameter`.
  - `PortalRules.MarkupStringSites` is `["Components/Tickets/CustomerMessageBody.razor"]`.
  - Test harness: `TicketTestKit` (`Token`, `Ticket(...)`, `Factory`, `ReplyForm`, `TokenContexts`).

- [ ] **Step 1: Write the failing tests**

The unit tests pin the presenter (the five statuses, the authors, the attachment links from the real token, sizes), the follow-up link (only the last segment of an http(s) link, only if it is a token) and the reply and email rules. The host tests drive the real pages: the ticket (theme from `ProductKey`, the neutral theme for an inactive product, the statuses, raw sanitised bodies and encoded everything else, the token only in the form action and the attachment links, the headers, calm failures), the uniform 404 (malformed, unknown, expired, revoked and every attachment variant, byte for byte), the reply (open and Closed, the redirect on this site whatever host the API's link names, an unreadable link, antiforgery, the real-server size limit, validation, 409, 413, 415, 429, outages, logs), the pass-through (streaming, the disposition and the headers, the statuses) and the lost-link page (identical responses, malformed address, 429, logs). The 09a header test's attachment case moves to the real route, the architecture rule pins the one markup site, and two earlier tests are adjusted.

`tests/TechStrap.Architecture.Tests/PortalRuleTests.cs`

```diff
@@ -127,12 +127,12 @@ public sealed class PortalRuleTests
     // ---- MarkupString sites -----------------------------------------------------------------------------------------------------------
 
     [Fact]
-    public void In_09a_no_Portal_file_turns_text_into_markup()
+    public void In_09b_exactly_CustomerMessageBody_turns_text_into_markup()
     {
         var files = PortalRules.Sources(ProjectGraph.FindRepositoryRoot()).ToList();
 
         files.Count.ShouldBeGreaterThan(20, "the scan must see the Portal sources");
-        PortalRules.MarkupStringSites.ShouldBeEmpty("09b adds CustomerMessageBody and 09c adds KbArticleBody, each in the commit that argues for it");
+        PortalRules.MarkupStringSites.ShouldBe(["Components/Tickets/CustomerMessageBody.razor"], "09b adds CustomerMessageBody (the API sanitises the message body); 09c adds KbArticleBody in its own commit");
         PortalRules.MarkupStringViolations(files).ShouldBeEmpty();
     }
 
@@ -143,14 +143,25 @@ public sealed class PortalRuleTests
     [InlineData(Code, "var m = new MarkupString(html);")]
     [InlineData(Code, "builder.AddMarkupContent(0, html);")]
     [InlineData("src/TechStrap.Portal/Components/Pages/Page.razor.cs", "public MarkupString Body => (MarkupString)Html;")]
-    public void A_deliberate_markup_site_is_flagged_while_the_allow_list_is_empty(string path, string text)
+    public void A_deliberate_markup_site_is_flagged_when_it_is_not_on_the_allow_list(string path, string text)
     {
-        var violations = PortalRules.MarkupStringViolations([(path, text)]);
+        var violations = PortalRules.MarkupStringViolations([(path, text)], []);
 
         violations.Count.ShouldBe(1);
         violations[0].ShouldContain(path.Replace("src/TechStrap.Portal/", string.Empty, StringComparison.Ordinal));
     }
 
+    [Fact]
+    public void The_real_allow_list_passes_for_its_own_file_and_flags_the_same_text_anywhere_else()
+    {
+        const string Real = "src/TechStrap.Portal/Components/Tickets/CustomerMessageBody.razor";
+
+        PortalRules.MarkupStringViolations([(Real, "<div>@((MarkupString)Html)</div>")]).ShouldBeEmpty();
+        PortalRules.MarkupStringViolations([("src/TechStrap.Portal/Components/Tickets/MessageThread.razor", "<div>@((MarkupString)Html)</div>"), (Real, "<div>@((MarkupString)Html)</div>")])
+            .ShouldHaveSingleItem().ShouldContain("MessageThread.razor");
+        PortalRules.MarkupStringViolations([(Real, "<div>@Html</div>")]).ShouldHaveSingleItem().ShouldContain("no longer");
+    }
+
     [Fact]
     public void A_listed_site_passes_and_an_unlisted_one_and_a_listed_file_that_no_longer_uses_it_are_flagged()
     {
```

`tests/TechStrap.Architecture.Tests/PortalRules.cs`

```diff
@@ -29,10 +29,10 @@ public static partial class PortalRules
 
     /// <summary>
     /// The files (relative to src/TechStrap.Portal) that may turn API text into markup, which is where a stored-XSS bug would live. The API sanitises the HTML before it sends it, and the Portal does not
-    /// sanitise again, so each site is argued for in the commit that adds it: 09b adds <c>CustomerMessageBody</c> (a ticket message body) and 09c adds <c>KbArticleBody</c> (a published article). In 09a the
-    /// list is empty: every other string the Portal shows is plain text, and Razor encodes it.
+    /// sanitise again, so each site is argued for in the commit that adds it: 09b adds <c>CustomerMessageBody</c> (a ticket message body: the API's sanitised HTML, D-045 addendum) and 09c adds
+    /// <c>KbArticleBody</c> (a published article). Every other string the Portal shows is plain text, and Razor encodes it.
     /// </summary>
-    public static IReadOnlyList<string> MarkupStringSites { get; } = [];
+    public static IReadOnlyList<string> MarkupStringSites { get; } = ["Components/Tickets/CustomerMessageBody.razor"];
 
     [GeneratedRegex(@"\b(?:I|Add)?HttpClient(?:Factory)?\b", RegexOptions.CultureInvariant)]
     private static partial Regex HttpClientUse();
```

`tests/TechStrap.Portal.Tests/Api/StubApiHandler.cs`

```diff
@@ -66,7 +66,7 @@ public sealed class StubApiHandler : HttpMessageHandler
     public static HttpResponseMessage FileResponse(Stream body, string contentType, string? fileName = null)
     {
         var content = new StreamContent(body);
-        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
+        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
         if (body.CanSeek)
         {
             content.Headers.ContentLength = body.Length;
```

`tests/TechStrap.Portal.Tests/Clients/CustomerTicketClientTests.cs`

```diff
@@ -180,6 +180,40 @@ public sealed class CustomerTicketClientTests
         limited.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.RateLimited);
     }
 
+    [Fact]
+    public async Task An_attachment_is_opened_through_the_read_client_with_the_token_header_and_the_id_in_the_api_path()
+    {
+        using var api = ApiHarness.Create();
+        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
+        api.Stub.OnFile(HttpMethod.Get, $"/api/customer/attachments/{id}", "file text"u8.ToArray(), "text/plain", "log.txt");
+
+        var result = await api.Get<ICustomerTicketClient>().OpenAttachmentAsync(Token(), id, Ct);
+
+        result.IsSuccess.ShouldBeTrue();
+        await using var download = result.Value;
+        using var reader = new StreamReader(download.Body);
+        (await reader.ReadToEndAsync(Ct)).ShouldBe("file text");
+        download.FileName.ShouldBe("log.txt");
+        var sent = api.Stub.Requests.ShouldHaveSingleItem();
+        sent.TicketToken.ShouldBe(Text);
+        sent.Client.ShouldBe(ApiClientNames.Read);
+        sent.Query.ShouldBeEmpty();
+        sent.Path.ShouldNotContain(Text);
+        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
+    }
+
+    [Fact]
+    public async Task An_attachment_the_api_refuses_is_the_uniform_not_found()
+    {
+        using var api = ApiHarness.Create();
+        var id = Guid.NewGuid();
+        api.Stub.OnProblem(HttpMethod.Get, $"/api/customer/attachments/{id}", HttpStatusCode.NotFound, "attachment-not-found", "No such attachment on this ticket.");
+
+        var result = await api.Get<ICustomerTicketClient>().OpenAttachmentAsync(Token(), id, Ct);
+
+        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
+    }
+
     [Fact]
     public async Task A_lost_link_request_is_not_retried()
     {
```

`tests/TechStrap.Portal.Tests/Forms/ReplyAndEmailRulesTests.cs` (new)

```csharp
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>The checks the reply form and the lost-link form make before they ask the API, each at its boundary, with the same codes and sentences as the contact form.</summary>
public sealed class ReplyAndEmailRulesTests
{
    [Fact]
    public void A_reply_with_a_body_and_no_files_is_valid()
    {
        ReplyFormValidator.Validate(new ReplyFormViewModel { Body = "Still broken." }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n\t ")]
    public void A_blank_body_is_required(string? body)
    {
        var error = ReplyFormValidator.Validate(new ReplyFormViewModel { Body = body }).ShouldHaveSingleItem();

        error.Field.ShouldBe(FormFields.Body);
        error.Code.ShouldBe("body-required");
        error.Message.ShouldBe("Write a message.");
    }

    [Fact]
    public void The_body_limit_is_inclusive_and_judged_after_trimming()
    {
        ReplyFormValidator.Validate(new ReplyFormViewModel { Body = "  " + new string('b', IntakeLimits.BodyMaxLength) + "  " }).ShouldBeEmpty();
        ReplyFormValidator.Validate(new ReplyFormViewModel { Body = new string('b', IntakeLimits.BodyMaxLength + 1) }).ShouldHaveSingleItem().Code.ShouldBe("body-too-long");
    }

    [Fact]
    public void The_files_of_a_reply_are_checked_like_the_contact_forms()
    {
        var codes = ReplyFormValidator.Validate(new ReplyFormViewModel { Body = "x", Files = [new FakeBrowserFile("virus.exe", 5), new FakeBrowserFile("empty.txt", 0)] }).Select(e => e.Code);

        codes.ShouldBe(["attachment-type-not-allowed", "attachment-empty"]);
    }

    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("  ada@example.com  ")]
    [InlineData("a.b+c@sub.example.co.uk")]
    public void A_plain_dotted_address_is_acceptable(string email)
    {
        EmailRules.Check(email).ShouldBeNull();
    }

    [Theory]
    [InlineData(null, "email-required")]
    [InlineData("", "email-required")]
    [InlineData("   ", "email-required")]
    [InlineData("ada", "email-invalid")]
    [InlineData("ada@example", "email-invalid")]
    [InlineData("Ada <ada@example.com>", "email-invalid")]
    [InlineData("a@b.example, c@d.example", "email-invalid")]
    public void A_missing_or_malformed_address_is_an_error_on_the_email_field(string? email, string code)
    {
        var error = EmailRules.Check(email).ShouldNotBeNull();

        error.Field.ShouldBe(FormFields.Email);
        error.Code.ShouldBe(code);
        error.Message.ShouldBe(FormCopy.For(code));
    }

    [Fact]
    public void The_email_limit_is_inclusive()
    {
        EmailRules.Check(new string('e', IntakeLimits.EmailMaxLength - "@example.com".Length) + "@example.com").ShouldBeNull();
        EmailRules.Check("e" + new string('e', IntakeLimits.EmailMaxLength - "@example.com".Length) + "@example.com").ShouldNotBeNull().Code.ShouldBe("email-invalid");
    }
}
```

`tests/TechStrap.Portal.Tests/Headers/TicketHeaderHostTests.cs`

```diff
@@ -7,7 +7,9 @@ namespace TechStrap.Portal.Tests.Headers;
 /// <summary>
 /// Review Focus 2 at the host: whatever the Portal answers under <c>/t</c> carries <c>Referrer-Policy: no-referrer</c>, <c>Cache-Control: no-store</c> and <c>X-Robots-Tag: noindex</c>, and
 /// only the attachment route is sandboxed, so the ticket page keeps the normal policy. The shared security-header middleware overwrites a value a page sets itself; these assert the final
-/// response. There is no ticket page until PHASE-09b, so the 404 for an unknown <c>/t</c> path is checked (its headers must apply anyway), and a probe answers 200 on paths no later route can match.
+/// response. The 404 for an unknown <c>/t</c> path is checked (its headers must apply anyway). Since PHASE-09b the ticket page and the attachment pass-through are real routes, so the delivered attachment is
+/// tested through the real route with a file behind the stub API (a probe at an attachment-shaped path can no longer be reached: the pass-through answers it), and a probe still answers 200 for a path under
+/// <c>/t</c> that no Portal route matches.
 /// </summary>
 public sealed class TicketHeaderHostTests
 {
@@ -15,9 +17,9 @@ public sealed class TicketHeaderHostTests
     private const string Token = "AbC-_0123456789AbC-_0123456789AbC-_01234567";
     private static readonly string AttachmentPath = $"/t/{Token}/attachments/{Guid.NewGuid()}";
 
-    // A page under /t that no Portal route will ever match (the ticket page is /t/{token}), and the real shape of the attachment route with an id the pass-through (a Guid route) cannot match.
+    // A page under /t that no Portal route will ever match (the ticket page is /t/{token}, the attachment route /t/{token}/attachments/{id}).
     private static readonly Action<Microsoft.Extensions.DependencyInjection.IServiceCollection> Probes = OkProbeStartupFilter.Add(path =>
-        path.StartsWithSegments("/t/probe-token/probe-page") || path.StartsWithSegments("/t/probe-token/attachments/probe-id"));
+        path.StartsWithSegments("/t/probe-token/probe-page"));
 
     private static string[] Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? [.. values] : [];
 
@@ -72,14 +74,15 @@ public sealed class TicketHeaderHostTests
     [Fact]
     public async Task A_delivered_attachment_has_the_ticket_headers_and_a_sandbox_on_top_of_the_page_policy()
     {
-        await using var factory = new PortalFactory(configureServices: Probes);
+        await using var factory = new PortalFactory();
+        factory.Api.OnFile(HttpMethod.Get, $"/api/customer/attachments/{Guid.Empty}", "file text"u8.ToArray(), "text/plain", "log.txt");
         using var client = factory.CreateClient();
 
-        // The probe answers the attachment shape; the id is not a guid, which a later pass-through route will not match, so the response is the probe's 200.
-        using var response = await client.GetAsync("/t/probe-token/attachments/probe-id", Ct);
+        // The real pass-through route, with a file behind the stub API: a 2xx response under /t/{token}/attachments/{id}.
+        using var response = await client.GetAsync($"/t/{Token}/attachments/{Guid.Empty}", Ct);
 
         response.StatusCode.ShouldBe(HttpStatusCode.OK);
-        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("probe");
+        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("file text", "the pass-through must really have answered, or this proves nothing");
         AssertTicketHeaders(response, "attachment");
         Policy(response).Count(directive => directive == "sandbox").ShouldBe(1);
         Policy(response).ShouldContain("script-src 'self'", "the page policy is still there, with sandbox on top");
```

`tests/TechStrap.Portal.Tests/Routing/PortalRoutesTests.cs`

```diff
@@ -74,6 +74,13 @@ public sealed class PortalRoutesTests
         PortalRoutes.Ticket("a b").ShouldBe("/t/a%20b");
     }
 
+    [Fact]
+    public void The_lost_link_confirmation_is_the_lost_link_page_with_the_sent_flag_and_nothing_about_an_address()
+    {
+        PortalRoutes.LostLinkSent("paperplane").ShouldBe("/p/paperplane/lost-link?sent=1");
+        PortalRoutes.SentParameter.ShouldBe("sent");
+    }
+
     [Fact]
     public void The_received_page_builder_adds_the_reference_escaped_so_it_can_never_add_a_parameter()
     {
```

`tests/TechStrap.Portal.Tests/Tickets/CustomerTicketPresenterTests.cs` (new)

```csharp
using TechStrap.Contracts.Tickets;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tickets;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// P09-T08 and T23 without a host: the view model the ticket page is built from. The five statuses in the customer's words (UX brief), "You" for the customer's own messages, the API's resolved name for an agent
/// exactly as it came, a neutral label for the system, the Portal's own attachment links built from the real token, and plain text kept as plain text (the page encodes it).
/// </summary>
public sealed class CustomerTicketPresenterTests
{
    private static TicketToken Token()
    {
        TicketToken.TryParse(TicketTestKit.Token, out var token).ShouldBeTrue();
        return token;
    }

    private static CustomerTicketViewModel Present(CustomerTicketDto? ticket = null) => CustomerTicketPresenter.Present(ticket ?? TicketTestKit.Ticket(), Token());

    [Theory]
    [InlineData("New", "Received", null)]
    [InlineData("Open", "In progress", null)]
    [InlineData("Pending", "Waiting for your reply", null)]
    [InlineData("Solved", "Solved", "This ticket is solved. If you reply, it will be reopened.")]
    [InlineData("Closed", "Closed", "This ticket is closed. If you reply, we will start a new follow-up ticket linked to it.")]
    public void Each_status_has_the_customers_wording_and_its_note(string status, string label, string? note)
    {
        var model = Present(TicketTestKit.Ticket(status));

        model.StatusLabel.ShouldBe(label);
        model.StatusNote.ShouldBe(note);
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("CLOSED")]
    public void A_status_is_matched_without_regard_to_case(string status)
    {
        var model = Present(TicketTestKit.Ticket(status));

        model.IsClosed.ShouldBeTrue();
        model.StatusLabel.ShouldBe("Closed");
    }

    [Theory]
    [InlineData("Escalated")]
    [InlineData("")]
    [InlineData(null)]
    public void A_status_this_build_does_not_know_is_in_progress_and_never_an_empty_label(string? status)
    {
        var (label, note) = CustomerTicketPresenter.Status(status);

        label.ShouldBe("In progress");
        note.ShouldBeNull();
    }

    [Fact]
    public void Only_a_closed_ticket_is_closed_and_only_a_solved_one_is_solved()
    {
        Present(TicketTestKit.Ticket("Closed")).IsClosed.ShouldBeTrue();
        Present(TicketTestKit.Ticket("Closed")).IsSolved.ShouldBeFalse();
        Present(TicketTestKit.Ticket("Solved")).IsSolved.ShouldBeTrue();
        Present(TicketTestKit.Ticket("Solved")).IsClosed.ShouldBeFalse();
        Present(TicketTestKit.Ticket("Open")).IsClosed.ShouldBeFalse();
    }

    [Fact]
    public void The_authors_are_you_the_agents_name_as_it_came_and_a_neutral_word_for_the_system()
    {
        var messages = Present().Messages;

        messages.Select(m => m.Author).ShouldBe(["You", "Sam from Paperplane Support", "Update"]);
        messages.Select(m => m.IsCustomer).ShouldBe([true, false, false]);
    }

    [Fact]
    public void An_agent_override_name_is_shown_unchanged()
    {
        var model = Present(TicketTestKit.Ticket(agentName: "Samantha from Paperplane Support"));

        model.Messages[1].Author.ShouldBe("Samantha from Paperplane Support");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_agent_message_with_no_name_is_attributed_to_support(string? name)
    {
        CustomerTicketPresenter.Author("Agent", name).ShouldBe("Support");
    }

    [Fact]
    public void A_name_with_markup_stays_text_for_the_page_to_encode()
    {
        var model = Present(TicketTestKit.Ticket(agentName: "<img src=x onerror=alert(1)>"));

        model.Messages[1].Author.ShouldBe("<img src=x onerror=alert(1)>");
    }

    [Theory]
    [InlineData("Requester", "You")]
    [InlineData("requester", "You")]
    [InlineData("System", "Update")]
    [InlineData("Whatever", "Update")]
    public void The_author_type_decides_the_label_whatever_its_case_and_a_system_or_unknown_one_is_neutral(string type, string label)
    {
        CustomerTicketPresenter.Author(type, "Should not show").ShouldBe(label);
    }

    [Fact]
    public void The_view_model_has_no_agent_email_id_or_avatar_and_no_internal_ids()
    {
        var names = new[] { typeof(CustomerTicketViewModel), typeof(CustomerMessageViewModel), typeof(CustomerAttachmentViewModel) }
            .SelectMany(t => t.GetProperties().Select(p => p.Name)).ToList();

        names.ShouldNotContain(n => n.Contains("Email", StringComparison.OrdinalIgnoreCase) || n.Contains("Avatar", StringComparison.OrdinalIgnoreCase) || n.Contains("AgentId", StringComparison.OrdinalIgnoreCase)
            || n.Contains("AuthorId", StringComparison.OrdinalIgnoreCase) || n.Contains("ProductId", StringComparison.OrdinalIgnoreCase) || n.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_attachment_is_the_portals_own_link_built_from_the_real_token_with_a_readable_size()
    {
        var attachment = Present().Messages[1].Attachments.ShouldHaveSingleItem();

        attachment.Href.ShouldBe($"/t/{TicketTestKit.Token}/attachments/{TicketTestKit.AttachmentId}");
        attachment.Href.ShouldNotContain("[token]");
        attachment.FileName.ShouldBe("log.txt");
        attachment.SizeText.ShouldBe("2 KB");
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1_048_575L, "1024 KB")]
    [InlineData(1_048_576L, "1 MB")]
    [InlineData(10_485_760L, "10 MB")]
    [InlineData(-5L, "0 B")]
    public void A_size_is_written_in_bytes_kilobytes_or_megabytes(long bytes, string text) => CustomerTicketPresenter.Size(bytes).ShouldBe(text);

    [Fact]
    public void The_messages_keep_the_apis_order_and_bodies_unchanged()
    {
        var model = Present();

        model.Messages.Select(m => m.Id).ShouldBe(
            [Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003")]);
        model.Messages[1].BodyHtml.ShouldBe("<p>Try <b>this</b> first.</p>");
        model.Number.ShouldBe("PAP-42");
        model.ProductKey.ShouldBe("paperplane");
        model.Subject.ShouldBe("Printer jam");
    }

    [Fact]
    public void A_ticket_with_no_messages_has_an_empty_thread()
    {
        var dto = new CustomerTicketDto("PAP-1", "paperplane", "x", "New", DateTimeOffset.UnixEpoch, []);

        CustomerTicketPresenter.Present(dto, Token()).Messages.ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Portal.Tests/Tickets/FollowUpLinkTests.cs` (new)

```csharp
using TechStrap.Portal.Tickets;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// Review Focus 1: the follow-up redirect never leaves the site. Only the last path segment of the API's link is used, and only if it is a valid token; the host, the scheme, a query and a fragment are ignored.
/// Anything that is not an absolute http(s) link with such a segment is no token, and the page then shows a generic confirmation instead of redirecting.
/// </summary>
public sealed class FollowUpLinkTests
{
    private const string New = "Zk9_-qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq";

    [Theory]
    [InlineData("https://help.example.com/t/" + New)]
    [InlineData("http://localhost:8082/t/" + New)]
    [InlineData("https://help.example.com/t/" + New + "/")]
    [InlineData("https://help.example.com/t/" + New + "?utm=1#top")]
    [InlineData("https://evil.example/anything/" + New)]
    [InlineData("HTTPS://HELP.EXAMPLE.COM/T/" + New)]
    [InlineData("https://help.example.com/base/path/t/" + New)]
    public void The_last_path_segment_is_the_token_whatever_the_host_or_the_query(string url)
    {
        FollowUpLink.TryGetToken(url, out var token).ShouldBeTrue(url);

        token.Value.ShouldBe(New);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/t/" + New)]
    [InlineData(New)]
    [InlineData("//evil.example/t/" + New)]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://help.example.com/t/" + New)]
    [InlineData("data:text/plain,x")]
    [InlineData("https://help.example.com/t/short")]
    [InlineData("https://help.example.com/t/" + New + "x")]
    [InlineData("https://help.example.com/")]
    [InlineData("https://help.example.com")]
    [InlineData("https://help.example.com/t/" + New + "/attachments")]
    [InlineData("not a url")]
    public void Anything_else_is_no_token_and_gives_a_default_token(string? url)
    {
        FollowUpLink.TryGetToken(url, out var token).ShouldBeFalse(url);

        token.ShouldBe(default);
    }
}
```

`tests/TechStrap.Portal.Tests/Tickets/LostLinkHostTests.cs` (new)

```csharp
using System.Net;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Tests.Api;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// P09-T10 at the host: the lost-link page. For any well-formed address the Portal shows the same confirmation, byte for byte, because it never looks at anything the API answered beyond success (Review Focus 2); a
/// malformed address is an ordinary field error and the API is not asked; a 429 or an outage is a calm notice. The page is themed, noindex and no-store, and every call forwards the visitor's address. Timing is out
/// of scope (D-038 accepts the residual difference).
/// </summary>
public sealed class LostLinkHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Path = "/p/paperplane/lost-link";
    private const string LinkApi = "/api/customer/access-link";
    private const string Visitor = FormTestKit.Visitor;

    private static PortalFactory Host() => FormTestKit.Factory();

    private static MultipartFormDataContent Form(string? token, string? email = "ada@example.com")
    {
        var form = new MultipartFormDataContent { { new StringContent("lost-link"), "_handler" } };
        if (token is not null)
        {
            form.Add(new StringContent(token), "__RequestVerificationToken");
        }

        if (email is not null)
        {
            form.Add(new StringContent(email), "Form.Email");
        }

        return form;
    }

    private static async Task<(HttpClient Client, string Token)> OpenAsync(PortalFactory factory)
    {
        var client = FormTestKit.Client(factory);
        return (client, await FormTestKit.TokenAsync(client, Path, Ct));
    }

    [Fact]
    public async Task The_page_is_themed_and_has_a_labelled_antiforgery_protected_form_and_the_form_page_headers()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync(Path, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<title>New ticket link: Paperplane</title>");
        html.ShouldContain("--ts-accent:#F59E0B");
        html.ShouldContain("<h1>Lost your ticket link?</h1>");
        html.ShouldContain("<form method=\"post\" action=\"/p/paperplane/lost-link\" novalidate");
        html.ShouldContain("name=\"__RequestVerificationToken\"");
        html.ShouldContain("name=\"_handler\" value=\"lost-link\"");
        html.ShouldContain("<label for=\"email\" class=\"form-label\">Email address</label>");
        html.ShouldContain("name=\"Form.Email\" type=\"email\" class=\"form-control\" maxlength=\"320\" autocomplete=\"email\"");
        html.ShouldContain("<button type=\"submit\" class=\"btn btn-primary\">Send me a new link</button>");
        html.ShouldNotContain("Confirmation");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
    }

    [Fact]
    public async Task A_post_asks_the_api_once_through_the_write_client_with_no_token_and_redirects_to_the_confirmation()
    {
        await using var factory = Host();
        factory.Api.OnStatus(HttpMethod.Post, LinkApi, HttpStatusCode.Accepted);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(Path, Form(token, "  ada@example.com "), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.ToString().ShouldBe("http://localhost/p/paperplane/lost-link?sent=1");
        var sent = factory.Api.Requests.Single(r => r.Method == HttpMethod.Post);
        sent.Client.ShouldBe(ApiClientNames.Write);
        sent.TicketToken.ShouldBeNull();
        sent.Path.ShouldBe(LinkApi);
        sent.Body.ShouldNotBeNull().ShouldContain("\"email\":\"ada@example.com\"");
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Fact]
    public async Task The_response_to_a_known_and_an_unknown_address_is_byte_identical_whatever_the_api_says_besides_success()
    {
        async Task<(string Redirect, string Page)> RunAsync(Func<HttpResponseMessage> answer, string email)
        {
            await using var factory = Host();
            factory.Api.On(HttpMethod.Post, LinkApi, _ => answer());
            var (client, token) = await OpenAsync(factory);
            using var _ = client;
            using var posted = await client.PostAsync(Path, Form(token, email), Ct);
            posted.StatusCode.ShouldBe(HttpStatusCode.Found);
            var location = posted.Headers.Location!;
            var page = await client.GetStringAsync(location.PathAndQuery, Ct);
            return (location.ToString() + string.Join(",", posted.Headers.Select(h => h.Key)), page);
        }

        // The API answers 202 whatever the address; these answers differ in everything the Portal could (wrongly) look at.
        var known = await RunAsync(() => new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{\"sent\":true,\"matches\":3}") }, "known@example.com");
        var unknown = await RunAsync(() => new HttpResponseMessage(HttpStatusCode.Accepted), "nobody@example.org");
        var odd = await RunAsync(() => new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new StringContent("different body") }, "x@y.example");

        unknown.Page.ShouldBe(known.Page, "byte for byte");
        odd.Page.ShouldBe(known.Page);
        unknown.Redirect.ShouldBe(known.Redirect);
        odd.Redirect.ShouldBe(known.Redirect);
        known.Page.ShouldContain("If we have tickets for that address, we have sent a new link. Check your inbox and your spam folder.");
        known.Page.ShouldNotContain("known@example.com");
        known.Page.ShouldNotContain("nobody@example.org");
        known.Page.ShouldNotContain("<form", Case.Sensitive, "no form, so no per-request antiforgery value to make two responses differ");
    }

    [Fact]
    public async Task The_confirmation_page_has_a_link_back_and_the_form_page_headers()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync(Path + "?sent=1", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("role=\"status\"");
        html.ShouldContain("href=\"/p/paperplane\">Back to Paperplane</a>");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        factory.Api.Requests.Select(r => r.Path).ShouldBe(["/api/public/products/paperplane"], "opening the confirmation asks nothing about any address");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not an address")]
    [InlineData("ada@")]
    [InlineData("ada@example")]
    public async Task A_malformed_address_is_a_field_error_and_the_api_is_not_asked(string email)
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(Path, Form(token, email), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<a href=\"#email\">");
        html.ShouldContain("class=\"ts-field-error\"");
        html.ShouldNotContain("role=\"status\"", Case.Sensitive, "no confirmation for a malformed address");
        factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(0);
    }

    [Fact]
    public async Task What_was_typed_is_kept_encoded_when_the_address_is_refused()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(Path, Form(token, "\"><script>alert(1)</script>"), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldNotContain("<script>alert(1)");
        html.ShouldContain("value=\"&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt;\"");
    }

    [Fact]
    public async Task An_address_the_api_calls_invalid_is_the_same_field_error()
    {
        await using var factory = Host();
        factory.Api.On(HttpMethod.Post, LinkApi, _ => StubApiHandler.ValidationProblem("email", "email-invalid", "API TEXT"));
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(Path, Form(token, "ada@example.com"), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain("Enter a valid email address, like name@example.com.");
        html.ShouldNotContain("API TEXT");
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, "Too many attempts.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, "We could not send that just now.")]
    [InlineData(HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable, "We could not send that just now.")]
    public async Task A_429_or_an_outage_is_a_calm_notice_with_a_fitting_status_and_the_address_kept(HttpStatusCode api, HttpStatusCode page, string sentence)
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, LinkApi, api, "x", "Npgsql host=10.0.0.5");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(Path, Form(token, "ada@example.com"), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(page);
        html.ShouldContain(sentence);
        html.ShouldNotContain("Npgsql");
        html.ShouldContain("value=\"ada@example.com\"");
        factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(1, "never retried");
    }

    [Fact]
    public async Task A_post_without_an_antiforgery_token_is_a_400_and_asks_nothing()
    {
        await using var factory = Host();
        var (client, _) = await OpenAsync(factory);
        using var __ = client;

        using var response = await client.PostAsync(Path, Form(null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(0);
    }

    [Theory]
    [InlineData("/p/nope/lost-link")]
    [InlineData("/p/Paperplane/lost-link")]
    public async Task An_unknown_inactive_or_malformed_product_is_the_uniform_404(string path)
    {
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/nope", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Page not found");
    }

    [Fact]
    public async Task The_footer_link_of_every_product_page_reaches_this_page()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        var home = await client.GetStringAsync("/p/paperplane", Ct);

        home.ShouldContain("href=\"/p/paperplane/lost-link\"");
    }

    [Fact]
    public async Task The_address_never_reaches_a_log_event_at_any_level()
    {
        await using var factory = FormTestKit.Factory(settings: PortalFactory.VerboseLogging);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", FormTestKit.Product());
        factory.Api.OnStatus(HttpMethod.Post, LinkApi, HttpStatusCode.Accepted);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(Path, Form(token, "private.person@example.com"), Ct);
        await client.GetStringAsync(response.Headers.Location!.PathAndQuery, Ct);

        factory.LogSink.Events.ShouldContain(e => e.Level <= Serilog.Events.LogEventLevel.Debug);
        factory.LogSink.Events.Select(e => string.Join('\n', [e.RenderMessage(), .. e.Properties.Values.Select(v => v.ToString())]))
            .ShouldAllBe(text => !text.Contains("private.person", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_lost_link_copy_says_the_same_thing_whatever_the_address()
    {
        LostLinkCopy.Confirmation.ShouldBe("If we have tickets for that address, we have sent a new link. Check your inbox and your spam folder.");
        LostLinkCopy.Confirmation.ShouldNotContain("no account", Case.Insensitive);
        LostLinkCopy.Confirmation.ShouldNotContain("not found", Case.Insensitive);
    }
}
```

`tests/TechStrap.Portal.Tests/Tickets/TicketAttachmentHostTests.cs` (new)

```csharp
using System.Net;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tickets;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// P09-T11 at the host: <c>GET /t/{token}/attachments/{id}</c>, the pass-through adapter (D-017). It streams the API's file for the visitor with the token as the header of the API call, always as a download
/// (<c>Content-Disposition: attachment</c>, <c>nosniff</c>), under the ticket headers and the sandbox policy; an upstream 404 is the uniform 404, the API's 429 is a 429 and every other failure, a transport failure
/// included, is a 502. A bad token or an id that is not a GUID is the uniform 404 and the API is not asked (the uniform-page comparison is in <c>TicketUniformNotFoundHostTests</c>).
/// </summary>
public sealed class TicketAttachmentHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly string Url = $"/t/{TicketTestKit.Token}/attachments/{TicketTestKit.AttachmentId}";
    private const string Visitor = FormTestKit.Visitor;

    private static PortalFactory Host(Action<PortalFactory>? configure = null)
    {
        var factory = FormTestKit.Factory(product: false);
        configure?.Invoke(factory);
        return factory;
    }

    private static async Task<HttpResponseMessage> GetAsync(PortalFactory factory, string? url = null)
    {
        using var client = FormTestKit.Client(factory);
        return await client.GetAsync(url ?? Url, HttpCompletionOption.ResponseContentRead, Ct);
    }

    private static string[] Policy(HttpResponseMessage response) => response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);

    [Fact]
    public async Task The_file_is_streamed_with_the_token_as_a_header_through_the_read_client_and_the_visitors_address()
    {
        await using var factory = Host(f => f.Api.OnFile(HttpMethod.Get, TicketTestKit.AttachmentApi, "the log text"u8.ToArray(), "text/plain", "log.txt"));

        using var response = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("the log text");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        response.Content.Headers.ContentLength.ShouldBe(12);
        var sent = factory.Api.Requests.ShouldHaveSingleItem();
        sent.Path.ShouldBe(TicketTestKit.AttachmentApi);
        sent.TicketToken.ShouldBe(TicketTestKit.Token);
        sent.Client.ShouldBe(ApiClientNames.Read);
        sent.Query.ShouldBeEmpty();
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Fact]
    public async Task The_response_is_a_download_nosniff_no_store_noindex_no_referrer_and_sandboxed_on_top_of_the_page_policy()
    {
        await using var factory = Host(f => f.Api.OnFile(HttpMethod.Get, TicketTestKit.AttachmentApi, [1, 2, 3], "application/pdf", "report.pdf"));

        using var response = await GetAsync(factory);

        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName.ShouldBe("report.pdf");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        Policy(response).Count(d => d == "sandbox").ShouldBe(1);
        Policy(response).ShouldContain("script-src 'self'", "the page policy is still there, with sandbox on top");
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/xhtml+xml")]
    [InlineData("text/html; charset=utf-8")]
    public async Task A_type_a_browser_would_run_is_still_a_download_under_a_sandbox_with_nosniff(string contentType)
    {
        await using var factory = Host(f => f.Api.OnFile(HttpMethod.Get, TicketTestKit.AttachmentApi, "<script>alert(1)</script>"u8.ToArray(), contentType, "page.html"));

        using var response = await GetAsync(factory);

        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment", "never inline");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        Policy(response).ShouldContain("sandbox");
    }

    [Fact]
    public async Task A_disposition_the_api_sent_as_inline_is_never_passed_on()
    {
        await using var factory = Host(f => f.Api.On(HttpMethod.Get, TicketTestKit.AttachmentApi, _ =>
        {
            var upstream = StubApiHandler.FileResponse(new MemoryStream([1]), "image/png");
            upstream.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("inline") { FileName = "x.png" };
            return upstream;
        }));

        using var response = await GetAsync(factory);

        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName.ShouldBe("x.png");
    }

    [Fact]
    public async Task A_file_with_no_name_is_a_download_called_attachment()
    {
        await using var factory = Host(f => f.Api.On(HttpMethod.Get, TicketTestKit.AttachmentApi, _ => StubApiHandler.FileResponse(new MemoryStream([1]), "application/octet-stream")));

        using var response = await GetAsync(factory);

        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName.ShouldBe("attachment");
    }

    [Fact]
    public async Task A_big_file_arrives_complete()
    {
        var bytes = new byte[5 * 1024 * 1024];
        new Random(7).NextBytes(bytes);
        await using var factory = Host(f => f.Api.OnFile(HttpMethod.Get, TicketTestKit.AttachmentApi, bytes, "application/zip", "big.zip"));

        using var response = await GetAsync(factory);

        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(bytes);
    }

    [Fact]
    public async Task An_upstream_404_is_the_uniform_404_not_sandboxed_and_with_the_ticket_headers()
    {
        await using var factory = Host(f => f.Api.OnProblem(HttpMethod.Get, TicketTestKit.AttachmentApi, HttpStatusCode.NotFound, "attachment-not-found", "No such attachment."));

        using var response = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Page not found");
        Policy(response).ShouldNotContain("sandbox", "the not-found page is an ordinary page");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Content.Headers.ContentDisposition.ShouldBeNull();
    }

    [Theory]
    [InlineData("/t/short/attachments/11111111-2222-3333-4444-555555555555")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567/attachments/not-a-guid")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567/attachments/11111111222233334444555555555555")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567/attachments/%7B11111111-2222-3333-4444-555555555555%7D")]
    public async Task A_bad_token_or_an_id_that_is_not_a_guid_is_a_404_and_the_api_is_never_asked(string path)
    {
        await using var factory = Host(f => f.Api.OnFile(HttpMethod.Get, TicketTestKit.AttachmentApi, [1], "text/plain", "x.txt"));

        using var response = await GetAsync(factory, path);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_apis_429_is_a_429()
    {
        await using var factory = Host(f => f.Api.OnStatus(HttpMethod.Get, TicketTestKit.AttachmentApi, HttpStatusCode.TooManyRequests));

        using var response = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Any_other_upstream_failure_is_a_502_with_nothing_from_the_api(HttpStatusCode upstream)
    {
        await using var factory = Host(f => f.Api.OnProblem(HttpMethod.Get, TicketTestKit.AttachmentApi, upstream, "x", "Npgsql host=10.0.0.5"));

        using var response = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("Npgsql");
    }

    [Fact]
    public async Task A_transport_failure_is_a_502()
    {
        await using var factory = Host(f => f.Api.On(HttpMethod.Get, TicketTestKit.AttachmentApi, _ => throw new HttpRequestException("No connection could be made (10.1.2.3:5432)")));

        using var response = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Only_a_get_is_answered()
    {
        await using var factory = Host(f => f.Api.OnFile(HttpMethod.Get, TicketTestKit.AttachmentApi, [1], "text/plain", "x.txt"));
        using var client = FormTestKit.Client(factory);

        using var response = await client.PostAsync(Url, new StringContent("x"), Ct);

        response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        factory.Api.Requests.ShouldBeEmpty();
    }

    // ---- the disposition header alone ----

    [Theory]
    [InlineData("report.pdf", "attachment; filename=report.pdf; filename*=UTF-8''report.pdf")]
    [InlineData("my report.pdf", "attachment; filename=\"my report.pdf\"; filename*=UTF-8''my%20report.pdf")]
    [InlineData(null, "attachment; filename=attachment; filename*=UTF-8''attachment")]
    [InlineData("", "attachment; filename=attachment; filename*=UTF-8''attachment")]
    [InlineData("C:\\x\\a.txt", "attachment; filename=a.txt; filename*=UTF-8''a.txt")]
    [InlineData("a\"b.txt", "attachment; filename=ab.txt; filename*=UTF-8''ab.txt")]
    public void The_disposition_is_always_attachment_with_the_cleaned_name(string? name, string expected) => AttachmentDisposition.Create(name).ShouldBe(expected);

    [Fact]
    public void A_name_that_could_split_a_header_cannot()
    {
        var header = AttachmentDisposition.Create("evil\r\nSet-Cookie: x=1.txt");

        header.ShouldNotContain("\r");
        header.ShouldNotContain("\n");
        header.ShouldStartWith("attachment");
    }

    [Fact]
    public void A_non_ascii_name_travels_as_an_encoded_filename_star()
    {
        var header = AttachmentDisposition.Create("caf" + (char)0xE9 + ".pdf");

        header.ShouldContain("filename*=UTF-8''caf%C3%A9.pdf");
        header.ShouldStartWith("attachment");
    }
}
```

`tests/TechStrap.Portal.Tests/Tickets/TicketPageHostTests.cs` (new)

```csharp
using System.Net;
using System.Text.RegularExpressions;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// P09-T08 and T23 at the host: the ticket page. A valid token shows the public conversation, themed with the ticket's own product (loaded from the ticket's <c>ProductKey</c>) or, when that product is inactive
/// or unknown, in the neutral theme: never a 404. The status is in the customer's words, a Closed ticket carries the follow-up notice, an agent shows as the API named them and the customer as "You", the message
/// bodies are the API's sanitised HTML rendered as it came and every other string is encoded. Review Focus 1: the token is a header on the API call and in the page's own links and nowhere else.
/// </summary>
public sealed class TicketPageHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Visitor = FormTestKit.Visitor;

    private static async Task<(HttpResponseMessage Response, string Html)> GetAsync(PortalFactory factory, string? path = null)
    {
        using var client = FormTestKit.Client(factory);
        var response = await client.GetAsync(path ?? TicketTestKit.Path, Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    private static PortalFactory Host() => TicketTestKit.Factory();

    [Fact]
    public async Task A_valid_token_shows_the_ticket_in_its_products_theme()
    {
        await using var factory = Host();

        var (response, html) = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<title>Ticket PAP-42</title>");
        html.ShouldContain("--ts-accent:#F59E0B");
        html.ShouldContain("class=\"ts-product-name\"");
        html.ShouldContain(">Paperplane</a>");
        html.ShouldContain("<span class=\"ts-ticket-number\">PAP-42</span>");
        html.ShouldContain("<span class=\"ts-ticket-subject\">Printer jam</span>");
        html.ShouldContain("<strong>In progress</strong>");
        html.ShouldContain("href=\"/p/paperplane/lost-link\"");
    }

    [Fact]
    public async Task The_api_is_called_with_the_token_as_a_header_only_and_the_visitors_address_and_then_asked_for_the_products_theme()
    {
        await using var factory = Host();

        await GetAsync(factory);

        factory.Api.Requests.Select(r => r.Path).ShouldBe([TicketTestKit.TicketApi, "/api/public/products/paperplane"]);
        var ticket = factory.Api.Requests[0];
        ticket.TicketToken.ShouldBe(TicketTestKit.Token);
        ticket.Client.ShouldBe(ApiClientNames.Read);
        ticket.Query.ShouldBeEmpty();
        ticket.Path.ShouldNotContain(TicketTestKit.Token);
        factory.Api.Requests[1].TicketToken.ShouldBeNull("the product call is anonymous");
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Fact]
    public async Task The_conversation_shows_each_author_each_time_and_each_message_in_order()
    {
        await using var factory = Host();

        var (_, html) = await GetAsync(factory);

        html.ShouldContain("<strong class=\"ts-message-author\">You</strong>");
        html.ShouldContain("<strong class=\"ts-message-author\">Sam from Paperplane Support</strong>");
        html.ShouldContain("<strong class=\"ts-message-author\">Update</strong>");
        html.ShouldContain("<time datetime=\"2026-10-01T09:00:00Z\">1 Oct 2026 09:00 UTC</time>");
        html.ShouldContain("<time datetime=\"2026-10-01T10:30:00Z\">1 Oct 2026 10:30 UTC</time>");
        var order = new[] { "It jams every time.", "Try <b>this</b> first.", "Status changed." }.Select(text => html.IndexOf(text, StringComparison.Ordinal)).ToArray();
        order.ShouldAllBe(i => i > 0);
        order.ShouldBe([.. order.Order()], "oldest first, as the API gave them");
        html.ShouldContain("ts-message ts-message-own");
    }

    [Fact]
    public async Task No_agent_email_id_or_avatar_is_on_the_page()
    {
        await using var factory = Host();

        var (_, html) = await GetAsync(factory);

        html.ShouldNotContain("@");
        html.ShouldNotContain("avatar", Case.Insensitive);
        html.ShouldNotContain("aaaaaaaa-0000", Case.Insensitive, "message ids are not rendered");
    }

    [Fact]
    public async Task A_message_body_is_the_apis_sanitised_html_rendered_as_it_came()
    {
        await using var factory = TicketTestKit.Factory(TicketTestKit.Ticket(agentBody: "<p>Hello <b>there</b>, see <a href=\"https://help.example.com/a\">this</a>.</p><ul><li>one</li></ul>"));

        var (_, html) = await GetAsync(factory);

        html.ShouldContain("<div class=\"ts-message-body\"><p>Hello <b>there</b>, see <a href=\"https://help.example.com/a\">this</a>.</p><ul><li>one</li></ul></div>");
    }

    [Fact]
    public async Task Every_other_string_is_encoded_the_subject_the_author_and_the_file_name()
    {
        const string Evil = "<img src=x onerror=alert(1)>";
        await using var factory = TicketTestKit.Factory(TicketTestKit.Ticket(subject: Evil + "Subject", agentName: Evil + "Sam", fileName: Evil + ".txt"));

        var (_, html) = await GetAsync(factory);

        html.ShouldNotContain("<img src=x");
        html.ShouldContain("&lt;img src=x onerror=alert(1)&gt;Subject");
        html.ShouldContain("&lt;img src=x onerror=alert(1)&gt;Sam");
        html.ShouldContain("&lt;img src=x onerror=alert(1)&gt;.txt");
    }

    [Fact]
    public async Task An_attachment_is_a_link_to_the_portals_pass_through_with_its_name_and_size()
    {
        await using var factory = Host();

        var (_, html) = await GetAsync(factory);

        html.ShouldContain($"<a href=\"/t/{TicketTestKit.Token}/attachments/{TicketTestKit.AttachmentId}\">log.txt</a> <span class=\"ts-attachment-size\">(2 KB)</span>");
        html.ShouldNotContain("/api/customer/attachments", Case.Sensitive, "the browser is never sent to the API");
    }

    [Theory]
    [InlineData("New", "Received", false)]
    [InlineData("Open", "In progress", false)]
    [InlineData("Pending", "Waiting for your reply", false)]
    [InlineData("Solved", "Solved", false)]
    [InlineData("Closed", "Closed", true)]
    public async Task The_status_banner_uses_the_customers_wording_and_only_a_closed_ticket_says_a_reply_starts_a_follow_up(string status, string label, bool closed)
    {
        await using var factory = TicketTestKit.Factory(TicketTestKit.Ticket(status));

        var (_, html) = await GetAsync(factory);

        html.ShouldContain($"<span class=\"ts-status-label\">Status:</span> <strong>{label}</strong>");
        (html.Contains("we will start a new follow-up ticket linked to it.", StringComparison.Ordinal)).ShouldBe(closed);
        html.Contains(">Send and start a new follow-up ticket</button>", StringComparison.Ordinal).ShouldBe(closed);
        html.Contains(">Send reply</button>", StringComparison.Ordinal).ShouldBe(!closed);
        html.Contains("If you reply, it will be reopened.", StringComparison.Ordinal).ShouldBe(status == "Solved");
        html.Contains("ts-status-closed", StringComparison.Ordinal).ShouldBe(closed);
    }

    [Fact]
    public async Task A_closed_ticket_still_shows_the_full_history_and_the_reply_form_for_the_follow_up()
    {
        await using var factory = TicketTestKit.Factory(TicketTestKit.Ticket("Closed"));

        var (_, html) = await GetAsync(factory);

        html.ShouldContain("It jams every time.");
        html.ShouldContain("name=\"Reply.Body\"");
        html.ShouldContain("This ticket is closed. If you reply, we will start a new follow-up ticket linked to it.");
    }

    [Fact]
    public async Task The_reply_form_is_an_antiforgery_protected_multipart_post_to_the_same_page_with_a_labelled_body_and_attachments()
    {
        await using var factory = Host();

        var (_, html) = await GetAsync(factory);

        html.ShouldContain($"<form method=\"post\" action=\"/t/{TicketTestKit.Token}\" enctype=\"multipart/form-data\" novalidate");
        html.ShouldContain("name=\"__RequestVerificationToken\"");
        html.ShouldContain("name=\"_handler\" value=\"reply\"");
        html.ShouldContain("<label for=\"body\" class=\"form-label\">Your reply</label>");
        html.ShouldContain("name=\"Reply.Body\" class=\"form-control\" rows=\"6\" maxlength=\"100000\"");
        html.ShouldContain("name=\"Reply.Files\" type=\"file\" multiple");
        html.ShouldContain("Up to 5 files: images and documents of 10 MB each and 25 MB in all");
    }

    [Fact]
    public async Task The_headers_are_the_ticket_headers_and_the_policy_is_the_normal_one()
    {
        await using var factory = Host();

        var (response, _) = await GetAsync(factory);

        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        var policy = response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);
        policy.ShouldNotContain("sandbox", "the ticket page is an ordinary page: only the attachment is sandboxed");
        policy.ShouldContain("script-src 'self'");
    }

    [Fact]
    public async Task The_token_is_only_in_the_forms_action_and_the_attachment_links_never_in_a_marker_a_query_a_script_or_a_title()
    {
        await using var factory = Host();

        var (_, html) = await GetAsync(factory);

        html.ShouldNotContain("[token]");
        var contexts = TicketTestKit.TokenContexts(html);
        contexts.Count.ShouldBe(2, "the form action and the one attachment link");
        contexts.ShouldAllBe(c => c.Contains("action=\"/t/", StringComparison.Ordinal) || c.Contains("href=\"/t/", StringComparison.Ordinal));
        html.ShouldNotContain("?" + TicketTestKit.Token);
        html.ShouldNotContain("=" + TicketTestKit.Token);
        Regex.Match(html, "<title>.*?</title>").Value.ShouldNotContain(TicketTestKit.Token);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task An_inactive_or_unknown_product_is_the_neutral_theme_and_the_ticket_still_shows(HttpStatusCode productStatus)
    {
        await using var factory = TicketTestKit.Factory();
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/paperplane", productStatus, "product-not-found", "Npgsql host=10.0.0.5");

        var (response, html) = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, "an inactive product on a valid ticket is never a 404");
        html.ShouldContain("It jams every time.");
        html.ShouldContain("<title>Ticket PAP-42</title>");
        html.ShouldNotContain("--ts-accent");
        html.ShouldNotContain("ts-product-header");
        html.ShouldNotContain("Npgsql");
        html.ShouldContain("ts-powered");
    }

    [Fact]
    public async Task A_product_with_unacceptable_branding_is_re_checked_like_every_product_page()
    {
        await using var factory = TicketTestKit.Factory(environment: "Production");
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", new PublicProductDto("paperplane", "Paperplane", "javascript:alert(1)", "#F59E0B;background:url(//evil.example/x)", "#000000", "#9D6507"));

        var (_, html) = await GetAsync(factory);

        html.ShouldNotContain("evil.example");
        html.ShouldNotContain("javascript:");
        html.ShouldNotContain("ts-product-logo");
        html.ShouldNotContain("--ts-accent");
    }

    [Fact]
    public async Task A_ticket_with_no_public_messages_says_so()
    {
        await using var factory = TicketTestKit.Factory(new(
            "PAP-7", "paperplane", "Empty", "New", DateTimeOffset.UnixEpoch, []));

        var (_, html) = await GetAsync(factory);

        html.ShouldContain("There are no messages to show yet.");
        html.ShouldContain("<strong>Received</strong>");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, "This ticket could not be loaded just now.")]
    [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable, "This ticket could not be loaded just now.")]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, "You have sent a lot in a short time.")]
    public async Task When_the_api_fails_the_page_says_so_calmly_with_no_token_and_no_internals(HttpStatusCode api, HttpStatusCode page, string sentence)
    {
        await using var factory = FormTestKit.Factory("Production", product: false);
        factory.Api.OnProblem(HttpMethod.Get, TicketTestKit.TicketApi, api, "internal-error", "System.InvalidOperationException at Npgsql host=10.0.0.5");

        var (response, html) = await GetAsync(factory);

        response.StatusCode.ShouldBe(page);
        html.ShouldContain(sentence);
        html.ShouldNotContain("Npgsql");
        html.ShouldNotContain(TicketTestKit.Token, Case.Sensitive, "not even the address of this page is repeated in the body");
        html.ShouldNotContain("ts-product-header");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
    }

    [Fact]
    public async Task A_transport_failure_is_the_same_calm_page()
    {
        await using var factory = FormTestKit.Factory("Production", product: false);
        factory.Api.On(HttpMethod.Get, TicketTestKit.TicketApi, _ => throw new HttpRequestException("No connection could be made (10.1.2.3:5432)"));

        var (response, html) = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        html.ShouldNotContain("10.1.2.3");
        factory.Api.Count(HttpMethod.Get, TicketTestKit.TicketApi).ShouldBe(1 + 2, "a read is retried twice");
    }
}
```

`tests/TechStrap.Portal.Tests/Tickets/TicketReplyHostTests.cs` (new)

```csharp
using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Tickets;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// P09-T09 at the host: the reply form on the ticket page. A reply on an open ticket is sent through the write client (once, with the token as a header) and redirects to the same page; a reply on a Closed ticket
/// redirects to the follow-up's own page, whose token is read from the API's link and checked, and never leaves the site (Review Focus 1); a link that cannot be read gives a generic confirmation and no redirect.
/// Validation, 409, 413, 415, 429 and outages each have their own message and the text is kept. Antiforgery is enforced (Review Focus 3), the size limit applies before the form is read, and a double submit is
/// guarded by redirect after post. Every call forwards the visitor's address (Review Focus 5).
/// </summary>
public sealed class TicketReplyHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Visitor = FormTestKit.Visitor;
    private const string NewToken = "Zk9_-qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq";

    private static PortalFactory Host(CustomerTicketDto? ticket = null, Action<IServiceCollection>? configure = null) => TicketTestKit.Factory(ticket, configure: configure);

    private static async Task<(HttpClient Client, string Token)> OpenAsync(PortalFactory factory)
    {
        var client = TicketTestKit.Client(factory);
        return (client, await FormTestKit.TokenAsync(client, TicketTestKit.Path, Ct));
    }

    private static CustomerReplyResponse Replied() => new("PAP-42", Guid.NewGuid(), false, null);

    private static CustomerReplyResponse FollowUp(string? link) => new("PAP-43", Guid.NewGuid(), true, link);

    // ---- open ticket ----

    [Fact]
    public async Task A_reply_is_sent_once_through_the_write_client_with_the_token_header_and_redirects_to_the_same_page()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, Replied(), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "Still broken.", new PostedFile("shot.png", [1, 2, 3], "image/png")), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location.ShouldNotBeNull().ToString().ShouldBe("http://localhost" + TicketTestKit.Path);
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        var sent = factory.Api.Requests.Single(r => r.Method == HttpMethod.Post);
        sent.Client.ShouldBe(ApiClientNames.Write);
        sent.TicketToken.ShouldBe(TicketTestKit.Token);
        sent.Path.ShouldBe(TicketTestKit.ReplyApi);
        sent.Body.ShouldNotBeNull().ShouldContain("Still broken.");
        sent.Body.ShouldContain("name=Attachments; filename=shot.png");
        sent.Body.ShouldNotContain(TicketTestKit.Token, Case.Sensitive, "the token is a header, never a form field");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Fact]
    public async Task A_refresh_after_the_redirect_sends_nothing_again()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, Replied(), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        using var posted = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), Ct);

        await client.GetStringAsync(posted.Headers.Location!.PathAndQuery, Ct);
        await client.GetStringAsync(posted.Headers.Location!.PathAndQuery, Ct);

        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1, "post, redirect, get: reloading never posts again");
    }

    [Fact]
    public async Task The_reply_text_is_sent_trimmed()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, Replied(), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "\r\n  Hello.  \r\n"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Requests.Single(r => r.Method == HttpMethod.Post).Body!.ShouldContain("\r\n\r\nHello.\r\n");
    }

    // ---- the Closed ticket and the follow-up ----

    [Fact]
    public async Task A_reply_on_a_closed_ticket_redirects_to_the_follow_ups_own_page_on_this_site()
    {
        await using var factory = Host(TicketTestKit.Ticket("Closed"));
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, FollowUp("https://help.example.com/t/" + NewToken), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "Back again."), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.ToString().ShouldBe("http://localhost/t/" + NewToken, "the Portal's own host and its own route, whatever host the API's link had");
    }

    [Theory]
    [InlineData("https://evil.example/t/" + NewToken)]
    [InlineData("https://help.example.com/t/" + NewToken + "?next=https://evil.example")]
    [InlineData("//evil.example/t/" + NewToken)]
    public async Task The_redirect_never_leaves_the_site_whatever_host_the_apis_link_names(string link)
    {
        await using var factory = Host(TicketTestKit.Ticket("Closed"));
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, FollowUp(link), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), Ct);

        if (response.StatusCode == HttpStatusCode.Found)
        {
            response.Headers.Location!.Host.ShouldBe("localhost");
            response.Headers.Location.AbsolutePath.ShouldBe("/t/" + NewToken);
            response.Headers.Location.Query.ShouldBeEmpty();
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.Contains("Location").ShouldBeFalse();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://evil.example/t/short")]
    [InlineData("https://evil.example/")]
    [InlineData("not a link")]
    public async Task A_follow_up_link_that_cannot_be_read_gives_a_generic_confirmation_and_no_redirect(string? link)
    {
        await using var factory = Host(TicketTestKit.Ticket("Closed"));
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, FollowUp(link), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Location").ShouldBeFalse();
        html.ShouldContain("Your reply was received and we started a new follow-up ticket for it. We have emailed you its link.");
        html.ShouldNotContain("name=\"Reply.Body\"", Case.Sensitive, "the form is gone, so the same reply cannot be sent twice from this page");
        html.ShouldNotContain("evil.example");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
    }

    // ---- antiforgery and the size limit ----

    [Fact]
    public async Task A_post_without_an_antiforgery_token_is_a_400_and_nothing_reaches_the_api()
    {
        await using var factory = Host();
        var (client, _) = await OpenAsync(factory);
        using var __ = client;
        var before = factory.Api.Requests.Count;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Api.Requests.Count.ShouldBe(before);
    }

    [Fact]
    public async Task On_the_real_server_a_post_over_the_forms_limit_is_a_413_before_anything_is_read()
    {
        await using var factory = TicketTestKit.Factory();
        factory.UseKestrel(0);
        factory.StartServer();
        var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.Add("X-Forwarded-For", FormTestKit.Visitor);
        var token = await FormTestKit.TokenAsync(client, TicketTestKit.Path, Ct);
        var apiCalls = factory.Api.Requests.Count;
        using var request = new HttpRequestMessage(HttpMethod.Post, TicketTestKit.Path) { Content = TicketTestKit.ReplyForm(token, "x", new PostedFile("big.zip", new byte[(int)IntakeLimits.FormBodyBytes], "application/zip")) };
        request.Headers.ExpectContinue = true;

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        factory.Api.Requests.Count.ShouldBe(apiCalls);
    }

    // ---- validation: nothing is sent, the text is kept ----

    [Fact]
    public async Task An_empty_reply_is_an_error_with_the_summary_and_calls_nothing()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "   "), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<a href=\"#body\">Write a message.</a>");
        html.ShouldContain("<p id=\"body-error\" class=\"ts-field-error\">Write a message.</p>");
        html.ShouldContain("It jams every time.", Case.Sensitive, "the conversation is still shown");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(0);
    }

    [Fact]
    public async Task A_reply_over_the_limit_and_files_that_break_the_rules_are_refused_before_anything_is_sent()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        var tooLong = new string('x', IntakeLimits.BodyMaxLength + 1);

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, tooLong, new PostedFile("virus.exe", [1]), new PostedFile("empty.txt", [])), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain("The message must be at most 100,000 characters.");
        html.ShouldContain("virus.exe is a type we cannot accept.");
        html.ShouldContain("empty.txt is empty. Remove it or choose another.");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(0);
    }

    [Fact]
    public async Task What_the_visitor_typed_is_kept_encoded_when_a_reply_fails()
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, TicketTestKit.ReplyApi, HttpStatusCode.Conflict, "reply-conflict", "rowversion 17 != 18");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "I wrote <b>this</b> & more"), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain("I wrote &lt;b&gt;this&lt;/b&gt; &amp; more</textarea>");
        html.ShouldNotContain("I wrote <b>this</b>");
    }

    // ---- what the API says ----

    [Fact]
    public async Task A_409_has_its_own_message_a_409_status_and_keeps_the_text()
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, TicketTestKit.ReplyApi, HttpStatusCode.Conflict, "reply-conflict", "rowversion 17 != 18");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "Still broken."), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        html.ShouldContain("Your reply could not be saved this time. Your text is still here: send it again.");
        html.ShouldContain("Still broken.</textarea>");
        html.ShouldNotContain("rowversion");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1, "never retried");
    }

    [Fact]
    public async Task The_apis_field_codes_become_the_portals_sentences()
    {
        await using var factory = Host();
        factory.Api.On(HttpMethod.Post, TicketTestKit.ReplyApi, _ => StubApiHandler.ValidationProblem(
            [("body", "body-required", "API TEXT"), ("attachments", "attachments-too-many", "API TEXT")]));
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Write a message.");
        html.ShouldContain("Attach at most 5 files.");
        html.ShouldNotContain("API TEXT");
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.OK, "That is too large to send.")]
    [InlineData(HttpStatusCode.UnsupportedMediaType, HttpStatusCode.OK, "That could not be sent in that form.")]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, "Too many attempts.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, "We could not send that just now.")]
    public async Task Every_other_failure_is_calm_with_a_fitting_status_and_the_text_is_kept(HttpStatusCode api, HttpStatusCode page, string sentence)
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, TicketTestKit.ReplyApi, api, "x", "System.InvalidOperationException at Npgsql host=10.0.0.5");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "Still broken."), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(page);
        html.ShouldContain(sentence);
        html.ShouldNotContain("Npgsql");
        html.ShouldContain("Still broken.</textarea>");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
    }

    [Fact]
    public async Task A_token_that_stops_working_between_the_page_and_the_post_is_the_uniform_404()
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, TicketTestKit.ReplyApi, HttpStatusCode.NotFound, "token-revoked", "An agent revoked this token.");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldContain("Page not found");
        html.ShouldNotContain("revoked");
    }

    // ---- logs ----

    [Fact]
    public async Task The_token_and_the_reply_text_never_reach_a_log_event_at_any_level()
    {
        await using var factory = FormTestKit.Factory(settings: PortalFactory.VerboseLogging);
        factory.Api.OnJson(HttpMethod.Get, TicketTestKit.TicketApi, TicketTestKit.Ticket("Closed"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", FormTestKit.Product());
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, FollowUp("https://help.example.com/t/" + NewToken), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "A very private reply"), Ct);
        await client.GetStringAsync(response.Headers.Location!.PathAndQuery, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect");
        factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("[token]", StringComparison.Ordinal), "control: the request line was logged, with the token masked");
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text =>
            !text.Contains(TicketTestKit.Token, StringComparison.Ordinal)
            && !text.Contains(NewToken, StringComparison.Ordinal)
            && !text.Contains("very private reply", StringComparison.Ordinal)
            && !text.Contains(token, StringComparison.Ordinal));
    }

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);
}
```

`tests/TechStrap.Portal.Tests/Tickets/TicketTestKit.cs` (new)

```csharp
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Contracts.Tickets;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// What the ticket host tests share: a ticket and its product behind the stub API, the token of the link, the antiforgery value a browser would get from the page, and the multipart post of the reply form (inputs
/// <c>Reply.Body</c> and <c>Reply.Files</c>, the handler name <c>reply</c> and the token as extra fields).
/// </summary>
internal static class TicketTestKit
{
    // Exactly 43 base64url characters, the shape of every access token.
    public const string Token = "AbC-_0123456789AbC-_0123456789AbC-_01234567";
    public const string OtherToken = "Zk9_-qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq";
    public const string TicketApi = "/api/customer/ticket";
    public const string ReplyApi = "/api/customer/ticket/replies";
    public static readonly Guid AttachmentId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    public static readonly string Path = "/t/" + Token;
    public static readonly string AttachmentApi = $"/api/customer/attachments/{AttachmentId}";

    public static CustomerTicketDto Ticket(string status = "Open", string productKey = "paperplane", string subject = "Printer jam", string agentName = "Sam from Paperplane Support", string agentBody = "<p>Try <b>this</b> first.</p>", string fileName = "log.txt") =>
        new(
            "PAP-42", productKey, subject, status, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
            [
                new CustomerMessageDto(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "Requester", null, "<p>It jams every time.</p>", new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), []),
                new CustomerMessageDto(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), "Agent", agentName, agentBody, new DateTimeOffset(2026, 10, 1, 10, 30, 0, TimeSpan.Zero), [new AttachmentDto(AttachmentId, fileName, "text/plain", 2048)]),
                new CustomerMessageDto(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), "System", null, "<p>Status changed.</p>", new DateTimeOffset(2026, 10, 1, 11, 0, 0, TimeSpan.Zero), []),
            ]);

    /// <summary>A host (see <see cref="FormTestKit.Factory"/>) with the ticket and the paperplane product configured.</summary>
    public static PortalFactory Factory(CustomerTicketDto? ticket = null, string environment = "Development", Action<IServiceCollection>? configure = null, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var factory = FormTestKit.Factory(environment, configure, settings);
        factory.Api.OnJson(HttpMethod.Get, TicketApi, ticket ?? Ticket());
        return factory;
    }

    public static HttpClient Client(PortalFactory factory) => FormTestKit.Client(factory);

    public static MultipartFormDataContent ReplyForm(string? antiforgery, string? body = "Still broken.", params PostedFile[] files)
    {
        var form = new MultipartFormDataContent { { new StringContent("reply"), "_handler" } };
        if (antiforgery is not null)
        {
            form.Add(new StringContent(antiforgery), "__RequestVerificationToken");
        }

        if (body is not null)
        {
            form.Add(new StringContent(body), "Reply.Body");
        }

        foreach (var file in files)
        {
            var part = new ByteArrayContent(file.Bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            form.Add(part, "Reply.Files", file.Name);
        }

        return form;
    }

    /// <summary>Every place the token text occurs in a page, with the eleven characters before it (the attribute: <c>action="/t/</c> or <c>href="/t/</c>), so a test can say it occurs nowhere else.</summary>
    public static IReadOnlyList<string> TokenContexts(string html, string token = Token) =>
        [.. Regex.Matches(html, ".{0,11}" + Regex.Escape(token)).Select(m => m.Value)];
}
```

`tests/TechStrap.Portal.Tests/Tickets/TicketUniformNotFoundHostTests.cs` (new)

```csharp
using System.Net;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// Review Focus 2: a malformed, unknown, expired or revoked token, and a wrong attachment id or a wrong attachment token, all answer the identical 404: the same status, the same neutral page byte for byte and the same
/// headers, so nothing tells a visitor which part failed. A malformed token never reaches the API.
/// </summary>
public sealed class TicketUniformNotFoundHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed record Seen(HttpStatusCode Status, string Body, string Headers, int ApiCalls);

    private static async Task<Seen> GetAsync(string path, Action<PortalFactory>? configure = null)
    {
        await using var factory = FormTestKit.Factory(product: false);
        configure?.Invoke(factory);
        using var client = FormTestKit.Client(factory);
        using var response = await client.GetAsync(path, Ct);
        var headers = string.Join(
            "\n",
            response.Headers.Concat(response.Content.Headers)
                .Where(h => h.Key is "Content-Type" or "Cache-Control" or "Referrer-Policy" or "X-Robots-Tag" or "X-Content-Type-Options" or "X-Frame-Options" or "Content-Security-Policy" or "blazor-enhanced-nav")
                .OrderBy(h => h.Key, StringComparer.Ordinal)
                .Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
        return new Seen(response.StatusCode, await response.Content.ReadAsStringAsync(Ct), headers, factory.Api.Requests.Count);
    }

    private static Action<PortalFactory> ApiAnswers404(string type, string detail) => factory =>
    {
        factory.Api.OnProblem(HttpMethod.Get, TicketTestKit.TicketApi, HttpStatusCode.NotFound, type, detail);
        factory.Api.OnProblem(HttpMethod.Get, TicketTestKit.AttachmentApi, HttpStatusCode.NotFound, type, detail);
    };

    [Fact]
    public async Task Every_way_to_fail_to_find_a_ticket_is_the_same_page()
    {
        var malformed = await GetAsync("/t/x");
        var tooLong = await GetAsync("/t/" + TicketTestKit.Token + "x");
        var unknown = await GetAsync(TicketTestKit.Path, ApiAnswers404("token-not-found", "No such token."));
        var expired = await GetAsync(TicketTestKit.Path, ApiAnswers404("token-expired", "This token expired yesterday."));
        var revoked = await GetAsync(TicketTestKit.Path, ApiAnswers404("token-revoked", "An agent revoked this token."));
        var unconfigured = await GetAsync(TicketTestKit.Path);

        foreach (var seen in new[] { malformed, tooLong, unknown, expired, revoked, unconfigured })
        {
            seen.Status.ShouldBe(HttpStatusCode.NotFound);
            seen.Body.ShouldContain("Page not found");
            seen.Body.ShouldNotContain("expired");
            seen.Body.ShouldNotContain("revoked");
            seen.Body.ShouldNotContain("token", Case.Insensitive);
            seen.Body.ShouldBe(malformed.Body, "byte for byte");
            seen.Headers.ShouldBe(malformed.Headers);
        }
    }

    [Fact]
    public async Task The_page_shows_no_product_branding_and_no_ticket_text()
    {
        var seen = await GetAsync(TicketTestKit.Path, ApiAnswers404("token-expired", "x"));

        seen.Body.ShouldNotContain("--ts-accent");
        seen.Body.ShouldNotContain("ts-product-header");
        seen.Body.ShouldNotContain("PAP-");
        seen.Headers.ShouldContain("X-Robots-Tag: noindex");
        seen.Headers.ShouldContain("Cache-Control: no-store");
        seen.Headers.ShouldContain("Referrer-Policy: no-referrer");
    }

    [Theory]
    [InlineData("/t/x")]
    [InlineData("/t/%20")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_0123456")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_012345678")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_0123456!")]
    [InlineData("/t/..%2F..%2Fadmin")]
    public async Task A_malformed_token_is_a_404_and_the_api_is_never_asked(string path)
    {
        var seen = await GetAsync(path);

        seen.Status.ShouldBe(HttpStatusCode.NotFound);
        seen.ApiCalls.ShouldBe(0);
    }

    [Fact]
    public async Task A_wrong_attachment_is_the_same_page_as_a_wrong_ticket_whatever_part_was_wrong()
    {
        var wrongTicket = await GetAsync(TicketTestKit.Path, ApiAnswers404("token-not-found", "x"));
        var badToken = await GetAsync($"/t/short/attachments/{TicketTestKit.AttachmentId}");
        var badId = await GetAsync($"/t/{TicketTestKit.Token}/attachments/not-a-guid");
        var badIdTraversal = await GetAsync($"/t/{TicketTestKit.Token}/attachments/..%2F..%2Fx");
        var noDashes = await GetAsync($"/t/{TicketTestKit.Token}/attachments/{TicketTestKit.AttachmentId:N}");
        var wrongId = await GetAsync($"/t/{TicketTestKit.Token}/attachments/{TicketTestKit.AttachmentId}", ApiAnswers404("attachment-not-found", "No such attachment on this ticket."));
        var otherTicket = await GetAsync($"/t/{TicketTestKit.OtherToken}/attachments/{TicketTestKit.AttachmentId}", ApiAnswers404("attachment-of-another-ticket", "Belongs to another ticket."));

        foreach (var seen in new[] { badToken, badId, badIdTraversal, noDashes, wrongId, otherTicket })
        {
            seen.Status.ShouldBe(HttpStatusCode.NotFound);
            seen.Body.ShouldBe(wrongTicket.Body, "byte for byte");
        }

        badToken.ApiCalls.ShouldBe(0);
        badId.ApiCalls.ShouldBe(0);
        badIdTraversal.ApiCalls.ShouldBe(0);
        noDashes.ApiCalls.ShouldBe(0);
    }
}
```


- [ ] **Step 2: Run the tests to see them fail**

The source of Steps 3 to 6 does not exist yet, so:

Run: `dotnet build tests/TechStrap.Portal.Tests -c Release`
Expected: FAIL to compile, the first errors `error CS0234: The type or namespace name 'Tickets' does not exist in the namespace 'TechStrap.Portal'` (`CustomerTicketPresenterTests.cs`, `FollowUpLinkTests.cs`, `TicketAttachmentHostTests.cs`) and `error CS0246: ... 'CustomerTicketViewModel' could not be found`.

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release --filter-query "/*/*/PortalRuleTests/*"`
Expected: FAIL: `total: 38, failed: 1` (`In_09b_exactly_CustomerMessageBody_turns_text_into_markup`: the allow list names a file that does not exist yet).

- [ ] **Step 3: Implement the presenter, the copy and the follow-up link**

`CustomerTicketPresenter` is pure: the customer's words for the status, "You" for the customer, the API's resolved agent name exactly as it came (blank becomes "Support"), a neutral word for the system, the Portal's own attachment link built from the real token, a readable size. `FollowUpLink` is Review Focus 1: only the last path segment of an absolute http(s) link, checked by `TicketToken.TryParse`; the host is never used.

`src/TechStrap.Portal/Tickets/TicketCopy.cs` (new)

```csharp
namespace TechStrap.Portal.Tickets;

/// <summary>The words of the ticket page (UX brief): the customer's status wording, the notes under it, who wrote a message, and the reply form. The product's name is the only name on the page.</summary>
public static class TicketCopy
{
    // The status words a customer sees, for the five statuses (UX brief: "Received", "In progress", "Waiting for your reply", "Solved", "Closed").
    public const string Received = "Received";
    public const string InProgress = "In progress";
    public const string WaitingForYou = "Waiting for your reply";
    public const string Solved = "Solved";
    public const string Closed = "Closed";

    public const string SolvedNote = "This ticket is solved. If you reply, it will be reopened.";
    public const string ClosedNote = "This ticket is closed. If you reply, we will start a new follow-up ticket linked to it.";

    // Who wrote a message. An agent is named by the API (first name and the product's support name, or the public display name) and shown as it came; the customer's own messages read "You".
    public const string You = "You";
    public const string Support = "Support";
    public const string System = "Update";

    public const string StatusLabel = "Status";
    public const string ConversationHeading = "Conversation";
    public const string NoMessages = "There are no messages to show yet.";
    public const string AttachmentsLabel = "Attachments";

    public const string ReplyHeading = "Reply";
    public const string ReplyLabel = "Your reply";
    public const string ReplyButton = "Send reply";
    public const string FollowUpButton = "Send and start a new follow-up ticket";

    /// <summary>Shown instead of the reply form when the API started a follow-up but its link could not be read: a true statement, with nothing to follow.</summary>
    public const string FollowUpStarted = "Your reply was received and we started a new follow-up ticket for it. We have emailed you its link.";

    public const string Unavailable = "This ticket could not be loaded just now. Reload the page in a moment.";

    public static string Title(string number) => $"Ticket {number}";
}
```

`src/TechStrap.Portal/Tickets/CustomerTicketViewModel.cs` (new)

```csharp
namespace TechStrap.Portal.Tickets;

/// <summary>A file attached to a public message: the name the API stored (plain text, encoded by the page), a readable size and the Portal's own link to it (the pass-through, never the API's).</summary>
public sealed record CustomerAttachmentViewModel(Guid Id, string FileName, string SizeText, string Href);

/// <summary>
/// One message of the public conversation. <see cref="Author"/> is a plain-text label: "You" for the customer's own, the name the API resolved for an agent (shown as it came: no email, no id, no avatar) and a
/// neutral word for a system message. <see cref="BodyHtml"/> is the API's sanitised HTML and the only thing on the page that is not encoded; <c>CustomerMessageBody</c> is the single place it is rendered.
/// </summary>
public sealed record CustomerMessageViewModel(Guid Id, string Author, bool IsCustomer, string BodyHtml, DateTimeOffset CreatedAt, IReadOnlyList<CustomerAttachmentViewModel> Attachments);

/// <summary>What the ticket page needs and nothing more: no internal id, no agent detail, no product beyond its key (which only selects the theme).</summary>
public sealed record CustomerTicketViewModel(
    string Number,
    string ProductKey,
    string Subject,
    string StatusLabel,
    string? StatusNote,
    bool IsSolved,
    bool IsClosed,
    IReadOnlyList<CustomerMessageViewModel> Messages);
```

`src/TechStrap.Portal/Tickets/CustomerTicketPresenter.cs` (new)

```csharp
using System.Globalization;
using TechStrap.Contracts.Tickets;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tickets;

/// <summary>
/// Builds what the ticket page shows from the API's <see cref="CustomerTicketDto"/> (P09-T08, T23). The status becomes the customer's words (UX brief); a message's author becomes "You" for the customer, the name
/// the API resolved for an agent exactly as it came (no email, id or avatar exists in the DTO or here) and a neutral word for a system message; an attachment becomes the Portal's own link
/// (<see cref="PortalRoutes.TicketAttachment(TicketToken, Guid)"/>, built from the real token, never from its printed form). Every text field stays plain text: the page encodes them, and only the sanitised message
/// body is ever rendered as markup. The messages keep the order the API gave (chronological).
/// </summary>
public static class CustomerTicketPresenter
{
    public static CustomerTicketViewModel Present(CustomerTicketDto ticket, TicketToken token)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var (label, note) = Status(ticket.Status);
        return new CustomerTicketViewModel(
            ticket.Number,
            ticket.ProductKey,
            ticket.Subject,
            label,
            note,
            string.Equals(ticket.Status, TicketStatuses.Solved, StringComparison.OrdinalIgnoreCase),
            string.Equals(ticket.Status, TicketStatuses.Closed, StringComparison.OrdinalIgnoreCase),
            [.. ticket.Messages.Select(message => Message(message, token))]);
    }

    /// <summary>The customer's wording for a status, and the note shown with it. A status this build does not know is "In progress": the ticket is not finished as far as the customer can tell.</summary>
    public static (string Label, string? Note) Status(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "new" => (TicketCopy.Received, null),
        "open" => (TicketCopy.InProgress, null),
        "pending" => (TicketCopy.WaitingForYou, null),
        "solved" => (TicketCopy.Solved, TicketCopy.SolvedNote),
        "closed" => (TicketCopy.Closed, TicketCopy.ClosedNote),
        _ => (TicketCopy.InProgress, null),
    };

    public static string Author(string authorType, string? displayName) => authorType.Trim().ToLowerInvariant() switch
    {
        "requester" => TicketCopy.You,
        "agent" => string.IsNullOrWhiteSpace(displayName) ? TicketCopy.Support : displayName,
        _ => TicketCopy.System,
    };

    /// <summary>A size a person can read: bytes, kilobytes or megabytes, one decimal at most.</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.InvariantCulture, $"{Math.Max(bytes, 0)} B"),
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB"),
    };

    private static CustomerMessageViewModel Message(CustomerMessageDto message, TicketToken token) => new(
        message.Id,
        Author(message.AuthorType, message.AuthorDisplayName),
        string.Equals(message.AuthorType, MessageAuthorTypes.Requester, StringComparison.OrdinalIgnoreCase),
        message.BodyHtml,
        message.CreatedAt,
        [.. message.Attachments.Select(a => new CustomerAttachmentViewModel(a.Id, a.FileName, Size(a.Size), PortalRoutes.TicketAttachment(token, a.Id)))]);
}
```

`src/TechStrap.Portal/Tickets/FollowUpLink.cs` (new)

```csharp
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tickets;

/// <summary>
/// Reads the new ticket's token out of <c>FollowUpViewUrl</c>, the absolute link the API makes for a follow-up (<c>{public url}/t/{token}</c>), so the Portal can send the visitor to its own page for it. Review Focus 1:
/// only the last path segment is used, and only if <see cref="TicketToken.TryParse"/> accepts it; the host, the scheme, a query and a fragment are all ignored, and the redirect is built by the Portal's own route builder, so
/// a visitor can never be sent to another site whatever the API's configuration or a poisoned value says. Anything that is not an absolute http(s) link with a valid last segment is no token.
/// </summary>
public static class FollowUpLink
{
    public static bool TryGetToken(string? url, out TicketToken token)
    {
        token = default;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && TicketToken.TryParse(segments[^1], out token);
    }
}
```


- [ ] **Step 4: Implement the pass-through and the client method**

The pass-through is an exempt adapter (D-017): a bad token or a non-GUID id is the empty 404 (the host re-executes only a 404 with no body into the neutral page), an upstream 404 the same, the API's 429 a 429 and anything else a 502; the file is streamed and is always a download.

`src/TechStrap.Portal/Tickets/AttachmentPassThrough.cs` (new)

```csharp
using Microsoft.Net.Http.Headers;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tickets;

/// <summary>
/// <c>GET /t/{token}/attachments/{id}</c>: the exempt pass-through adapter of D-017 for a customer. A browser link cannot carry the <c>X-Ticket-Token</c> header, so the Portal asks
/// <c>GET api/customer/attachments/{id}</c> for the visitor, with the token from the path as the header, and copies the answer through without buffering it. It runs no workflow and holds no data. Review Focus 2: a token
/// that is not a token, an id that is not a GUID, and an upstream not-found are the same empty 404, which the host re-executes into the one neutral not-found page, so nothing tells a wrong id from a wrong token or a
/// revoked one. The file is always a download whatever the API called it (<c>Content-Disposition: attachment</c> with a cleaned name, <c>nosniff</c>), and the host's header rules add <c>no-store</c>,
/// <c>no-referrer</c>, <c>noindex</c> and the sandbox CSP to the response (they apply to everything under <c>/t</c>, and the sandbox only to a 2xx here). A transport failure or any other upstream failure is a 502; the API's
/// 429 is a 429.
/// </summary>
public static class AttachmentPassThrough
{
    public static IEndpointRouteBuilder MapAttachmentPassThrough(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(PortalRoutes.TicketAttachmentTemplate, StreamAsync);
        return endpoints;
    }

    private static async Task StreamAsync(string token, string id, ICustomerTicketClient tickets, HttpContext http, CancellationToken cancellationToken)
    {
        if (!TicketToken.TryParse(token, out var ticketToken) || !Guid.TryParseExact(id, "D", out var attachmentId))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var result = await tickets.OpenAttachmentAsync(ticketToken, attachmentId, cancellationToken);
        if (result.IsFailure)
        {
            http.Response.StatusCode = StatusFor(result.Errors[0]);
            return;
        }

        await using var download = result.Value;
        var response = http.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = download.ContentType;
        if (download.ContentLength is { } length)
        {
            response.ContentLength = length;
        }

        response.Headers[HeaderNames.ContentDisposition] = AttachmentDisposition.Create(download.FileName);
        response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        await download.Body.CopyToAsync(response.Body, cancellationToken);
    }

    private static int StatusFor(ResultError error) =>
        error.Kind == ResultErrorKind.NotFound ? StatusCodes.Status404NotFound
        : error.Code == ApiErrorCodes.RateLimited ? StatusCodes.Status429TooManyRequests
        : StatusCodes.Status502BadGateway;
}

/// <summary>The <c>Content-Disposition</c> of a download: always <c>attachment</c> (never <c>inline</c>), with the cleaned file name (non-ASCII names become <c>filename*</c>), whatever the API sent.</summary>
public static class AttachmentDisposition
{
    public static string Create(string? fileName)
    {
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(AttachmentFileName.Clean(fileName));
        return disposition.ToString();
    }
}
```

`src/TechStrap.Portal/Clients/ICustomerTicketClient.cs`

```diff
@@ -26,4 +26,10 @@ public interface ICustomerTicketClient
     /// <c>email-invalid</c> and a 429 is rate limited. The call is never retried.
     /// </summary>
     Task<Result> RequestAccessLinkAsync(string email, CancellationToken cancellationToken);
+
+    /// <summary>
+    /// Opens one attachment of the ticket for streaming (the pass-through, P09-T11): the token is the header of the request and the id goes in the API path, through the retrying read client, returning as soon as the
+    /// response headers have arrived. The caller owns the <see cref="ApiDownload"/> and disposes it. An unknown id, another ticket's id and a bad token are all the API's uniform not-found.
+    /// </summary>
+    Task<Result<ApiDownload>> OpenAttachmentAsync(TicketToken token, Guid attachmentId, CancellationToken cancellationToken);
 }
```

`src/TechStrap.Portal/Clients/CustomerTicketClient.cs`

```diff
@@ -18,4 +18,7 @@ internal sealed class CustomerTicketClient(ApiConnection api) : ICustomerTicketC
 
     public Task<Result> RequestAccessLinkAsync(string email, CancellationToken cancellationToken) =>
         api.SendAsync(HttpMethod.Post, "api/customer/access-link", new RequestNewAccessLinkRequest(email), cancellationToken);
+
+    public Task<Result<ApiDownload>> OpenAttachmentAsync(TicketToken token, Guid attachmentId, CancellationToken cancellationToken) =>
+        api.OpenStreamAsync($"api/customer/attachments/{attachmentId}", token, cancellationToken);
 }
```


- [ ] **Step 5: Implement the form models and the components**

The email check moves out of the contact validator so the lost-link form shares it. `CustomerMessageBody` is the only file that renders markup; the thread shows each author, each time (always UTC) and each file.

`src/TechStrap.Portal/Forms/EmailRules.cs` (new)

```csharp
using System.Net.Mail;
using TechStrap.Contracts.Intake;

namespace TechStrap.Portal.Forms;

/// <summary>The one check of an email address the Portal's forms make before they ask the API (which is the authority): present, within <see cref="IntakeLimits.EmailMaxLength"/>, and one plain dotted address.</summary>
public static class EmailRules
{
    /// <summary>The error for this value, or null when it is acceptable. The value is judged trimmed.</summary>
    public static FormError? Check(string? value)
    {
        var email = value?.Trim() ?? string.Empty;
        if (email.Length == 0)
        {
            return new FormError(FormFields.Email, "email-required", FormCopy.For("email-required"));
        }

        return email.Length > IntakeLimits.EmailMaxLength || !LooksLikeAnAddress(email)
            ? new FormError(FormFields.Email, "email-invalid", FormCopy.For("email-invalid"))
            : null;
    }

    // Not a full address grammar (the API decides): one address, no display name, a dotted domain.
    private static bool LooksLikeAnAddress(string text) =>
        MailAddress.TryCreate(text, out var address) && address.Address == text && address.Host.Contains('.', StringComparison.Ordinal);
}
```

`src/TechStrap.Portal/Forms/ContactFormValidator.cs`

```diff
@@ -1,4 +1,3 @@
-using System.Net.Mail;
 using TechStrap.Contracts.Intake;
 
 namespace TechStrap.Portal.Forms;
@@ -24,14 +23,9 @@ public static class ContactFormValidator
             errors.Add(Error(FormFields.Name, "name-too-long"));
         }
 
-        var email = form.Email?.Trim() ?? string.Empty;
-        if (email.Length == 0)
+        if (EmailRules.Check(form.Email) is { } emailError)
         {
-            errors.Add(Error(FormFields.Email, "email-required"));
-        }
-        else if (email.Length > IntakeLimits.EmailMaxLength || !LooksLikeAnAddress(email))
-        {
-            errors.Add(Error(FormFields.Email, "email-invalid"));
+            errors.Add(emailError);
         }
 
         var subject = form.Subject?.Trim() ?? string.Empty;
@@ -59,8 +53,4 @@ public static class ContactFormValidator
     }
 
     private static FormError Error(string field, string code) => new(field, code, FormCopy.For(code));
-
-    // Not a full address grammar (the API decides): one address, no display name, a dotted domain.
-    private static bool LooksLikeAnAddress(string text) =>
-        MailAddress.TryCreate(text, out var address) && address.Address == text && address.Host.Contains('.', StringComparison.Ordinal);
 }
```

`src/TechStrap.Portal/Forms/ReplyForm.cs` (new)

```csharp
using Microsoft.AspNetCore.Components.Forms;
using TechStrap.Contracts.Intake;

namespace TechStrap.Portal.Forms;

/// <summary>What the reply form on the ticket page binds: the text and the files (inputs <c>Reply.Body</c> and <c>Reply.Files</c>).</summary>
public sealed class ReplyFormViewModel
{
    public string? Body { get; set; }

    public IReadOnlyList<IBrowserFile>? Files { get; set; }
}

/// <summary>The Portal's check of a reply before it asks the API: a body (judged trimmed) of at most <see cref="IntakeLimits.BodyMaxLength"/> characters, and the files against the same limits as the contact form.</summary>
public static class ReplyFormValidator
{
    public static IReadOnlyList<FormError> Validate(ReplyFormViewModel form)
    {
        var errors = new List<FormError>();
        var body = form.Body?.Trim() ?? string.Empty;
        if (body.Length == 0)
        {
            errors.Add(new FormError(FormFields.Body, "body-required", FormCopy.For("body-required")));
        }
        else if (body.Length > IntakeLimits.BodyMaxLength)
        {
            errors.Add(new FormError(FormFields.Body, "body-too-long", FormCopy.For("body-too-long")));
        }

        errors.AddRange(AttachmentRules.Validate(form.Files));
        return errors;
    }
}
```

`src/TechStrap.Portal/Forms/LostLinkForm.cs` (new)

```csharp
namespace TechStrap.Portal.Forms;

/// <summary>What the lost-link form binds: one address (input <c>Form.Email</c>).</summary>
public sealed class LostLinkFormViewModel
{
    public string? Email { get; set; }
}

/// <summary>The words of the lost-link page (UX brief). The confirmation is one sentence that says nothing about whether the address matched: it is the same for every well-formed address.</summary>
public static class LostLinkCopy
{
    public const string Heading = "Lost your ticket link?";
    public const string Intro = "Enter the email address you used when you contacted us. If we have tickets for that address, we will send you a new link.";
    public const string EmailLabel = "Email address";
    public const string Submit = "Send me a new link";
    public const string Confirmation = "If we have tickets for that address, we have sent a new link. Check your inbox and your spam folder.";

    public static string Title(string productName) => $"New ticket link: {productName}";

    public static string BackTo(string productName) => $"Back to {productName}";
}
```

`src/TechStrap.Portal/Components/Tickets/CustomerMessageBody.razor` (new)

```razor
<div class="ts-message-body">@((MarkupString)Html)</div>

@code {
    /// <summary>
    /// The body of one public message, rendered as the markup it is. This is the Portal's single place that turns text into markup (PortalRules.MarkupStringSites; D-045 addendum): the API sanitises every message body
    /// before it sends it (the single source of truth, so the Portal never sanitises again), and every other string on the ticket page (subject, author, file names) is encoded by Razor.
    /// </summary>
    [Parameter, EditorRequired]
    public string Html { get; set; } = string.Empty;
}
```

`src/TechStrap.Portal/Components/Tickets/MessageThread.razor` (new)

```razor
@if (Messages.Count == 0)
{
    <p>@TicketCopy.NoMessages</p>
}
else
{
    <ol class="ts-thread" aria-label="@TicketCopy.ConversationHeading">
        @foreach (var message in Messages)
        {
            <li class="ts-message @(message.IsCustomer ? "ts-message-own" : null)">
                <header class="ts-message-head">
                    <strong class="ts-message-author">@message.Author</strong>
                    <time datetime="@message.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)">@message.CreatedAt.UtcDateTime.ToString("d MMM yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) UTC</time>
                </header>
                <CustomerMessageBody Html="@message.BodyHtml" />
                @if (message.Attachments.Count > 0)
                {
                    <ul class="ts-attachments" aria-label="@TicketCopy.AttachmentsLabel">
                        @foreach (var attachment in message.Attachments)
                        {
                            <li><a href="@attachment.Href">@attachment.FileName</a> <span class="ts-attachment-size">(@attachment.SizeText)</span></li>
                        }
                    </ul>
                }
            </li>
        }
    </ol>
}

@code {
    /// <summary>The public conversation in the order the API gave (oldest first), each message with its author label, its time (always UTC, so the same page reads the same everywhere) and its files.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<CustomerMessageViewModel> Messages { get; set; } = [];
}
```

`src/TechStrap.Portal/Components/Tickets/TicketStatusBanner.razor` (new)

```razor
<section class="ts-status-banner @(IsClosed ? "ts-status-closed" : null)" aria-label="@TicketCopy.StatusLabel">
    <p class="ts-status"><span class="ts-status-label">@TicketCopy.StatusLabel:</span> <strong>@Label</strong></p>
    @if (Note is not null)
    {
        <p class="ts-status-note">@Note</p>
    }
</section>

@code {
    [Parameter, EditorRequired]
    public string Label { get; set; } = string.Empty;

    /// <summary>The note under the status: that a reply reopens a solved ticket, or that a reply to a closed one starts a follow-up.</summary>
    [Parameter]
    public string? Note { get; set; }

    [Parameter]
    public bool IsClosed { get; set; }
}
```


- [ ] **Step 6: Implement the pages, the route builder, the host wiring and the rule**

The ticket page parses the token first, loads the ticket, then the ticket's own product (a failure there is the neutral theme), and never repeats the token in a failure page. A reply redirects to the same page; on a Closed ticket the API's link is read by `FollowUpLink` and the redirect is `PortalRoutes.Ticket(newToken)`. The lost-link page redirects to `?sent=1`, which shows one sentence and no form.

`src/TechStrap.Portal/Components/Pages/LostLink.razor` (new)

```razor
@attribute [Route(PortalRoutes.LostLinkTemplate)]
@inherits ProductPageBase

@if (Theme is { } theme)
{
    <PageTitle>@LostLinkCopy.Title(theme.DisplayName)</PageTitle>
    <h1>@LostLinkCopy.Heading</h1>
    @if (SentFlag == "1")
    {
        <p role="status" class="ts-confirmation">@LostLinkCopy.Confirmation</p>
        <p><a href="@PortalRoutes.ProductHome(theme.Key)">@LostLinkCopy.BackTo(theme.DisplayName)</a></p>
    }
    else
    {
        <p>@LostLinkCopy.Intro</p>
        <ErrorSummary Errors="Errors" />
        @if (Notice is not null)
        {
            <p class="alert alert-warning" role="alert">@Notice</p>
        }
        <form method="post" action="@PortalRoutes.LostLink(theme.Key)" novalidate @formname="@FormHandler" @onsubmit="SubmitAsync" class="ts-form">
            <AntiforgeryToken />
            <FormField Field="@FormFields.Email" Name="Form.Email" Label="@LostLinkCopy.EmailLabel" Value="@Form?.Email" MaxLength="@TechStrap.Contracts.Intake.IntakeLimits.EmailMaxLength" Type="email" Autocomplete="email" Error="@ErrorOf(FormFields.Email)" />
            <button type="submit" class="btn btn-primary">@LostLinkCopy.Submit</button>
        </form>
    }
}
else if (UnavailableMessage is not null)
{
    <PageTitle>@ShellCopy.UnavailableTitle</PageTitle>
    <ProductUnavailable Message="@UnavailableMessage" RetryHref="@RetryHref" />
}
```

`src/TechStrap.Portal/Components/Pages/LostLink.razor.cs` (new)

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The lost-link page of a product (P09-T10). One address; a post asks the API to email a new link and redirects (post, redirect, get) to the same page with <c>?sent=1</c>, which shows one sentence. Review Focus 2:
/// the page never looks at what the API answered beyond success or failure, shows no address and no hint, so every well-formed address gets the byte-identical response (the timing difference D-038 accepts is not
/// addressed here); a malformed address is an ordinary field error (it leaks nothing and the API is not asked); a 429 is a calm notice. Timing is out of scope (D-038); the responses are not.
/// </summary>
public partial class LostLink : ProductPageBase
{
    public const string FormHandler = "lost-link";

    [SupplyParameterFromForm(FormName = FormHandler)]
    private LostLinkFormViewModel? Form { get; set; }

    [SupplyParameterFromQuery(Name = PortalRoutes.SentParameter)]
    private string? SentFlag { get; set; }

    [Inject]
    private ICustomerTicketClient Customers { get; set; } = default!;

    [Inject]
    private NavigationManager Redirects { get; set; } = default!;

    [Inject]
    private IHttpContextAccessor Http { get; set; } = default!;

    private IReadOnlyList<FormError> Errors { get; set; } = [];

    private string? Notice { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is not null)
        {
            Form ??= new LostLinkFormViewModel();
        }
    }

    private string? ErrorOf(string field) => Errors.FirstOrDefault(error => error.Field == field)?.Message;

    private async Task SubmitAsync()
    {
        if (Theme is null)
        {
            return;
        }

        var form = Form ??= new LostLinkFormViewModel();
        Errors = EmailRules.Check(form.Email) is { } error ? [error] : [];
        if (Errors.Count > 0)
        {
            return;
        }

        var result = await Customers.RequestAccessLinkAsync(form.Email!.Trim(), Http.HttpContext?.RequestAborted ?? CancellationToken.None);
        if (result.IsSuccess)
        {
            Redirects.NavigateTo(PortalRoutes.LostLinkSent(Key));
            return;
        }

        // The route has no product or ticket for the API to not find, so a not-found here is the API misrouted: the same calm notice as an outage.
        var failure = FormFailure.From(result.Errors);
        if (failure.IsNotFound)
        {
            failure = new FormFailure([], FormCopy.Unavailable, StatusCodes.Status503ServiceUnavailable, false);
        }
        Errors = failure.Errors;
        Notice = failure.Notice;
        if (failure.Status != StatusCodes.Status200OK && Http.HttpContext is { Response.HasStarted: false } context)
        {
            context.Response.StatusCode = failure.Status;
        }
    }
}
```

`src/TechStrap.Portal/Components/Pages/Ticket.razor` (new)

```razor
@attribute [Route(PortalRoutes.TicketTemplate)]
@attribute [Microsoft.AspNetCore.Mvc.RequestSizeLimit(IntakeLimits.FormBodyBytes)]
@using TechStrap.Contracts.Intake

@if (Model is { } ticket)
{
    <PageTitle>@TicketCopy.Title(ticket.Number)</PageTitle>
    <h1><span class="ts-ticket-number">@ticket.Number</span> <span class="ts-ticket-subject">@ticket.Subject</span></h1>
    <TicketStatusBanner Label="@ticket.StatusLabel" Note="@ticket.StatusNote" IsClosed="@ticket.IsClosed" />
    <MessageThread Messages="@ticket.Messages" />
    <h2 class="h5">@TicketCopy.ReplyHeading</h2>
    @if (FollowUpConfirmation)
    {
        <p role="status">@TicketCopy.FollowUpStarted</p>
    }
    else
    {
        <ErrorSummary Errors="Errors" />
        @if (Notice is not null)
        {
            <p class="alert alert-warning" role="alert">@Notice</p>
        }
        <form method="post" action="@PortalRoutes.Ticket(_token)" enctype="multipart/form-data" novalidate @formname="@ReplyHandler" @onsubmit="ReplyAsync" class="ts-form">
            <AntiforgeryToken />
            <FormField Field="@FormFields.Body" Name="Reply.Body" Label="@TicketCopy.ReplyLabel" Value="@Reply?.Body" MaxLength="@IntakeLimits.BodyMaxLength" Rows="6" Error="@ErrorOf(FormFields.Body)" />
            <AttachmentInput Name="Reply.Files" Error="@ErrorOf(FormFields.Attachments)" Kept="@(Errors.Count > 0 || Notice is not null)" />
            <button type="submit" class="btn btn-primary">@(ticket.IsClosed ? TicketCopy.FollowUpButton : TicketCopy.ReplyButton)</button>
        </form>
    }
}
else if (UnavailableMessage is not null)
{
    <PageTitle>@ShellCopy.UnavailableTitle</PageTitle>
    <section class="ts-state" role="alert">
        <h1>@ShellCopy.UnavailableTitle</h1>
        <p>@UnavailableMessage</p>
    </section>
}
```

`src/TechStrap.Portal/Components/Pages/Ticket.razor.cs` (new)

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Tickets;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The customer's ticket at <c>/t/{token}</c> (P09-T08, T09, T23): the public conversation, the status in the customer's words and the reply form, themed with the product the ticket belongs to. Review Focus 1: the
/// token stays in the path and in the <c>X-Ticket-Token</c> header of the API call; it is parsed first (a malformed one is the uniform 404 and the API is never asked), it is never put in a query, a log or a link built
/// from its printed form, and the redirects are built by <see cref="PortalRoutes"/>. Review Focus 2: an unknown, expired or revoked token is the same 404 as a malformed one. An inactive or unknown product is not a
/// 404: the ticket shows in the neutral theme. Reply: redirect after post to the same page, or, when the API started a follow-up (a reply to a Closed ticket), to the follow-up's own page, whose token is read out of
/// the API's link and checked (<see cref="FollowUpLink"/>); if it cannot be read the page says the follow-up was started and sends the visitor nowhere.
/// </summary>
public partial class Ticket
{
    public const string ReplyHandler = "reply";

    [Parameter]
    public string Token { get; set; } = string.Empty;

    [SupplyParameterFromForm(FormName = ReplyHandler)]
    private ReplyFormViewModel? Reply { get; set; }

    [Inject]
    private ICustomerTicketClient Tickets { get; set; } = default!;

    [Inject]
    private IPublicProductClient Products { get; set; } = default!;

    [Inject]
    private ProductScope Scope { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IHostEnvironment Environment { get; set; } = default!;

    [Inject]
    private IHttpContextAccessor Http { get; set; } = default!;

    private TicketToken _token;

    private CustomerTicketViewModel? Model { get; set; }

    private string? UnavailableMessage { get; set; }

    private IReadOnlyList<FormError> Errors { get; set; } = [];

    private string? Notice { get; set; }

    private bool FollowUpConfirmation { get; set; }

    private CancellationToken Cancellation => Http.HttpContext?.RequestAborted ?? CancellationToken.None;

    protected override async Task OnInitializedAsync()
    {
        if (!TicketToken.TryParse(Token, out _token))
        {
            Navigation.NotFound();
            return;
        }

        var result = await Tickets.GetAsync(_token, Cancellation);
        if (result.IsFailure)
        {
            var failure = FormFailure.From(result.Errors);
            if (failure.IsNotFound)
            {
                Navigation.NotFound();
                return;
            }

            UnavailableMessage = failure.Status == StatusCodes.Status429TooManyRequests ? ProblemCopy.RateLimited : TicketCopy.Unavailable;
            SetStatus(failure.Status);
            return;
        }

        Model = CustomerTicketPresenter.Present(result.Value, _token);

        // The link carries no product, so the page asks for the ticket's own. A product that is inactive or unknown is the neutral theme, never a 404: the customer still has a ticket.
        var product = await Products.GetAsync(result.Value.ProductKey, Cancellation);
        if (product.IsSuccess)
        {
            Scope.Set(ProductThemeViewModel.From(product.Value, Environment.IsDevelopment()));
        }

        Reply ??= new ReplyFormViewModel();
    }

    private string? ErrorOf(string field) => Errors.FirstOrDefault(error => error.Field == field)?.Message;

    private async Task ReplyAsync()
    {
        if (Model is null)
        {
            return;
        }

        var form = Reply ??= new ReplyFormViewModel();
        Errors = ReplyFormValidator.Validate(form);
        if (Errors.Count > 0)
        {
            return;
        }

        var result = await Tickets.ReplyAsync(_token, new CustomerReply(form.Body!.Trim(), AttachmentRules.ToUploads(form.Files)), Cancellation);
        if (result.IsSuccess)
        {
            // Straight after each redirect: nothing else may run or render.
            if (!result.Value.FollowUpCreated)
            {
                Navigation.NavigateTo(PortalRoutes.Ticket(_token));
                return;
            }

            if (FollowUpLink.TryGetToken(result.Value.FollowUpViewUrl, out var followUp))
            {
                Navigation.NavigateTo(PortalRoutes.Ticket(followUp));
                return;
            }

            FollowUpConfirmation = true;
            return;
        }

        var failure = FormFailure.From(result.Errors);
        if (failure.IsNotFound)
        {
            Navigation.NotFound();
            return;
        }

        Errors = failure.Errors;
        Notice = failure.Notice;
        SetStatus(failure.Status);
    }

    private void SetStatus(int status)
    {
        if (status != StatusCodes.Status200OK && Http.HttpContext is { Response.HasStarted: false } context)
        {
            context.Response.StatusCode = status;
        }
    }
}
```

`src/TechStrap.Portal/Components/_Imports.razor`

```diff
@@ -5,7 +5,9 @@
 @using TechStrap.Portal
 @using TechStrap.Portal.Components
 @using TechStrap.Portal.Components.Layout
+@using TechStrap.Portal.Components.Tickets
 @using TechStrap.Portal.Components.Ui
 @using TechStrap.Portal.Forms
 @using TechStrap.Portal.Products
 @using TechStrap.Portal.Routing
+@using TechStrap.Portal.Tickets
```

`src/TechStrap.Portal/Program.cs`

```diff
@@ -12,6 +12,7 @@ using TechStrap.Portal.Products;
 using TechStrap.Portal.Seo;
 using TechStrap.Portal.Settings;
 using TechStrap.Portal.Suggestions;
+using TechStrap.Portal.Tickets;
 using TechStrap.Portal.Uploads;
 
 const string ServiceName = "techstrap-portal";
@@ -73,6 +74,7 @@ app.UseAntiforgery();
 app.MapStandardHealthChecks();
 app.MapPortalSeo();
 app.MapSuggest();
+app.MapAttachmentPassThrough();
 app.MapRazorComponentsWithStaticAssets<App>();
 
 app.Run();
```

`src/TechStrap.Portal/Routing/PortalRoutes.cs`

```diff
@@ -20,6 +20,9 @@ public static class PortalRoutes
     public const string LostLinkSegment = "lost-link";
     public const string SuggestSegment = "suggest";
 
+    /// <summary>The query parameter that makes the lost-link page show its confirmation.</summary>
+    public const string SentParameter = "sent";
+
     /// <summary>The query parameter that carries the protected ticket reference to the "received" page.</summary>
     public const string ReceivedReferenceParameter = "ref";
 
@@ -56,6 +59,9 @@ public static class PortalRoutes
 
     public static string LostLink(string key) => $"{ProductHome(key)}/lost-link";
 
+    /// <summary>The lost-link page as it is shown after a request: the same address for every request, whatever the address was.</summary>
+    public static string LostLinkSent(string key) => $"{LostLink(key)}?{SentParameter}=1";
+
     public static string KbHome(string key) => $"{ProductHome(key)}/kb";
 
     public static string KbCategory(string key, string category) => $"{KbHome(key)}/{Escape(category)}";
```

`src/TechStrap.Portal/Styles/_components.scss`

```diff
@@ -183,6 +183,75 @@
   color: var(--p-ink2);
 }
 
+// The ticket page (PHASE-09b): the status banner, the conversation and the reply form.
+.ts-ticket-subject {
+  font-weight: 500;
+}
+
+.ts-status-banner {
+  margin: 8px 0 20px;
+  padding: 12px 16px;
+  border: 1px solid var(--p-line);
+  border-left: 6px solid var(--ts-accent, var(--p-line));
+}
+
+.ts-status-closed {
+  border-left-color: var(--p-ink2);
+}
+
+.ts-status,
+.ts-status-note {
+  margin: 0;
+}
+
+.ts-status-note {
+  margin-top: 4px;
+  color: var(--p-ink2);
+}
+
+.ts-thread {
+  margin: 0 0 24px;
+  padding: 0;
+  list-style: none;
+}
+
+.ts-message {
+  margin-bottom: 16px;
+  padding: 12px 16px;
+  border: 1px solid var(--p-line);
+}
+
+.ts-message-own {
+  border-left: 4px solid var(--ts-accent, var(--p-line));
+}
+
+.ts-message-head {
+  display: flex;
+  flex-wrap: wrap;
+  gap: 4px 12px;
+  align-items: baseline;
+  margin-bottom: 8px;
+  font-size: .875rem;
+
+  time {
+    color: var(--p-ink2);
+  }
+}
+
+.ts-message-body {
+  overflow-wrap: anywhere;
+}
+
+.ts-attachments {
+  margin: 8px 0 0;
+  padding-left: 20px;
+  font-size: .875rem;
+}
+
+.ts-attachment-size {
+  color: var(--p-ink2);
+}
+
 .ts-ticket-number {
   font: 600 1.125rem var(--ts-font-mono);
   user-select: all;
```


- [ ] **Step 7: Run the tests to see them pass**

Run: `dotnet build TechStrap.slnx -c Release --no-incremental`
Expected: `0 Warning(s)`, `0 Error(s)`.

Run: `dotnet test --project tests/TechStrap.Portal.Tests -c Release`
Expected: PASS: `total: 972, failed: 0` (787 before this task).

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: PASS: `total: 294, failed: 0` (the markup-string rule now lists exactly `CustomerMessageBody`; the 09a samples that must be flagged pass an explicit empty allow list).

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release`
Expected: PASS: `total: 899, failed: 0`.

- [ ] **Step 8: Prove each pin with a mutation**

Run `git add -A` first.

| # | File | Replace | With | Command (after `--`) | Expected |
| --- | --- | --- | --- | --- | --- |
| 1 | `src/TechStrap.Portal/Tickets/CustomerTicketPresenter.cs` | `"closed" => (TicketCopy.Closed, TicketCopy.ClosedNote),` | `"closed" => (TicketCopy.Closed, null),` | `PT "/*/*/CustomerTicketPresenterTests/*"` | KILLED, 1 failed of 33 |
| 2 | `src/TechStrap.Portal/Tickets/CustomerTicketPresenter.cs` | `"requester" => TicketCopy.You,` | `"requester" => displayName ?? TicketCopy.You,` | `PT "/*/*/CustomerTicketPresenterTests/*"` | KILLED, 2 failed of 33 |
| 3 | `src/TechStrap.Portal/Tickets/CustomerTicketPresenter.cs` | `PortalRoutes.TicketAttachment(token, a.Id)` | `"/api/customer/attachments/" + a.Id` | `PT "/*/*/CustomerTicketPresenterTests/*"` | KILLED, 1 failed of 33 |
| 4 | `src/TechStrap.Portal/Tickets/CustomerTicketPresenter.cs` | `< 1024 * 1024 =>` | `< 2 * 1024 * 1024 =>` | `PT "/*/*/CustomerTicketPresenterTests/*"` | KILLED, 1 failed of 33 |
| 5 | `src/TechStrap.Portal/Tickets/CustomerTicketPresenter.cs` | `_ => (TicketCopy.InProgress, null),` | `_ => (status ?? string.Empty, null),` | `PT "/*/*/CustomerTicketPresenterTests/*"` | KILLED, 3 failed of 33 |
| 6 | `src/TechStrap.Portal/Tickets/FollowUpLink.cs` | `TicketToken.TryParse(segments[^1], out token)` | `TicketToken.TryParse(segments[0], out token)` | `PT "/*/*/FollowUpLinkTests/*"` | KILLED, 7 failed of 22 |
| 7 | `src/TechStrap.Portal/Tickets/FollowUpLink.cs` | `(uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)` | `false` | `PT "/*/*/FollowUpLinkTests/*"` | KILLED, 2 failed of 22 |
| 8 | `src/TechStrap.Portal/Tickets/AttachmentPassThrough.cs` | `Guid.TryParseExact(id, "D", out var attachmentId)` | `Guid.TryParse(id, out var attachmentId)` | `PT "/*/*/TicketUniformNotFoundHostTests/*"` | KILLED, 1 failed of 9 |
| 9 | `src/TechStrap.Portal/Tickets/AttachmentPassThrough.cs` | `new ContentDispositionHeaderValue("attachment")` | `new ContentDispositionHeaderValue("inline")` | `PT "/*/*/TicketAttachmentHostTests/*"` | KILLED, 15 failed of 29 |
| 10 | `src/TechStrap.Portal/Tickets/AttachmentPassThrough.cs` | `: error.Code == ApiErrorCodes.RateLimited ? StatusCodes.Status429TooManyRequests` | `: error.Code == ApiErrorCodes.RateLimited ? StatusCodes.Status502BadGateway` | `PT "/*/*/TicketAttachmentHostTests/*"` | KILLED, 1 failed of 29 |
| 11 | `src/TechStrap.Portal/Tickets/AttachmentPassThrough.cs` | `error.Kind == ResultErrorKind.NotFound ? StatusCodes.Status404NotFound` | `error.Kind == ResultErrorKind.NotFound ? StatusCodes.Status502BadGateway` | `PT "/*/*/TicketAttachmentHostTests/*"` | KILLED, 1 failed of 29 |
| 12 | `src/TechStrap.Portal/Tickets/AttachmentPassThrough.cs` | `response.Headers[HeaderNames.ContentDisposition] = AttachmentDisposition.Create(download.FileName);` | (nothing) | `PT "/*/*/TicketAttachmentHostTests/*"` | KILLED, 7 failed of 29 |
| 13 | `src/TechStrap.Portal/Components/Pages/Ticket.razor.cs` | `Navigation.NavigateTo(PortalRoutes.Ticket(followUp));` | `Navigation.NavigateTo(PortalRoutes.Ticket(_token));` | `PT "/*/*/TicketReplyHostTests/*"` | KILLED, 3 failed of 26 |
| 14 | `src/TechStrap.Portal/Components/Pages/Ticket.razor.cs` | `if (product.IsSuccess)\n        {` | `if (true)\n        {` | `PT "/*/*/TicketPageHostTests/*"` | KILLED, 4 failed of 26 |
| 15 | `src/TechStrap.Portal/Components/Pages/Ticket.razor.cs` | `if (!TicketToken.TryParse(Token, out _token))` | `if (false)` | `PT "/*/*/TicketUniformNotFoundHostTests/*"` | KILLED (the run fails) |
| 16 | `src/TechStrap.Portal/Components/Pages/Ticket.razor.cs` | `FollowUpConfirmation = true;` | (nothing) | `PT "/*/*/TicketReplyHostTests/*"` | KILLED, 6 failed of 26 |
| 17 | `src/TechStrap.Portal/Components/Pages/Ticket.razor.cs` | `Errors = ReplyFormValidator.Validate(form);\n        if (Errors.Count > 0)` | `Errors = ReplyFormValidator.Validate(form);\n        if (false)` | `PT "/*/*/TicketReplyHostTests/*"` | KILLED (the run fails) |
| 18 | `src/TechStrap.Portal/Components/Pages/Ticket.razor` | `@attribute [Microsoft.AspNetCore.Mvc.RequestSizeLimit(IntakeLimits.FormBodyBytes)]` | (nothing) | `PT "/*/*/TicketReplyHostTests/*"` | KILLED, 1 failed of 26 |
| 19 | `src/TechStrap.Portal/Components/Pages/Ticket.razor` | `@(ticket.IsClosed ? TicketCopy.FollowUpButton : TicketCopy.ReplyButton)` | `@(ticket.IsClosed ? TicketCopy.ReplyButton : TicketCopy.FollowUpButton)` | `PT "/*/*/TicketPageHostTests/*"` | KILLED, 5 failed of 26 |
| 20 | `src/TechStrap.Portal/Components/Tickets/CustomerMessageBody.razor` | `@((MarkupString)Html)` | `@Html` | `PT "/*/*/TicketPageHostTests/*"` | KILLED, 2 failed of 26 |
| 21 | `src/TechStrap.Portal/Components/Pages/LostLink.razor.cs` | `Errors = EmailRules.Check(form.Email) is { } error ? [error] : [];` | `Errors = [];` | `PT "/*/*/LostLinkHostTests/*"` | KILLED, 5 failed of 20 |
| 22 | `src/TechStrap.Portal/Components/Pages/LostLink.razor` | `@if (SentFlag == "1")` | `@if (false)` | `PT "/*/*/LostLinkHostTests/*"` | KILLED (the run fails) |
| 23 | `src/TechStrap.Portal/Program.cs` | `app.MapAttachmentPassThrough();` | (nothing) | `PT "/*/*/TicketAttachmentHostTests/*"` | KILLED, 15 failed of 29 |
| 24 | `src/TechStrap.Portal/Clients/CustomerTicketClient.cs` | `$"api/customer/attachments/{attachmentId}"` | `$"api/customer/attachments/{attachmentId:N}"` | `PT "/*/*/CustomerTicketClientTests/*"` | KILLED, 1 failed of 16 |
| 25 | `src/TechStrap.Portal/Forms/EmailRules.cs` | `email.Length > IntakeLimits.EmailMaxLength` | `email.Length >= IntakeLimits.EmailMaxLength` | `PT "/*/*/ReplyAndEmailRulesTests/*"` | KILLED, 1 failed of 17 |
| 26 | `src/TechStrap.Portal/Forms/ReplyForm.cs` | `body.Length > IntakeLimits.BodyMaxLength` | `body.Length >= IntakeLimits.BodyMaxLength` | `PT "/*/*/ReplyAndEmailRulesTests/*"` | KILLED, 1 failed of 17 |
| 27 | `src/TechStrap.Portal/Tickets/TicketCopy.cs` | `public const string Received = "Received";` | `public const string Received = "New";` | `PT "/*/*/CustomerTicketPresenterTests/*"` | KILLED, 1 failed of 33 |
| 28 | `tests/TechStrap.Architecture.Tests/PortalRules.cs` | `MarkupStringSites { get; } = ["Components/Tickets/CustomerMessageBody.razor"];` | `MarkupStringSites { get; } = [];` | `dotnet test --project tests/TechStrap.Architecture.Tests -c Release --filter-query "/*/*/PortalRuleTests/*"` | KILLED, 2 failed of 38 |
| 29 | `src/TechStrap.Portal/Components/Pages/Ticket.razor.cs` | `Navigation.NavigateTo(PortalRoutes.Ticket(_token));\n                return;` | `Navigation.NavigateTo("/");\n                return;` | `PT "/*/*/TicketReplyHostTests/*"` | KILLED, 1 failed of 26 |
| 30 | `src/TechStrap.Portal/Components/Pages/Ticket.razor.cs` | `UnavailableMessage = failure.Status == StatusCodes.Status429TooManyRequests ? ProblemCopy.RateLimited : TicketCopy.Unavailable;` | `UnavailableMessage = TicketCopy.Unavailable;` | `PT "/*/*/TicketPageHostTests/*"` | KILLED, 1 failed of 26 |

- [ ] **Step 9: Verify and commit**

Run: `dotnet build TechStrap.slnx -c Release --no-incremental`
Expected: `0 Warning(s)`, `0 Error(s)`.

```bash
git status --short
git add -A src tests
git diff --cached --stat
git commit -F - <<'EOF'
feat(portal): the ticket page, replies and follow-up, the attachment pass-through and the lost-link page (PHASE-09b)

/t/{token} shows the public conversation in the customer's words, themed from the ticket's product (neutral
for an inactive one), with a reply form; a reply on a Closed ticket redirects to the follow-up on this site.
GET /t/{token}/attachments/{id} streams a download behind the ticket headers and the sandbox. Every way to
fail to find a ticket or an attachment is one 404. The lost-link page answers every well-formed address
identically. CustomerMessageBody is the first markup-string site.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
EOF
```

### Task 6: Close-out: the T18 compose smoke, PORTAL-APP.md, the PHASE-09 ticks, the roadmap rows and the D-045 as-built notes

**Review Focus pin:** 5 (the Api's rate limit sees the real client address through the Portal, proven end to end under compose) and the documentation of Review Focus 1 to 4 for the security review that PHASE-12 needs (the markup site, the token paths, the honeypot, the limits).

**Files:**

- Modify: `docs/architecture/00-DISCOVERY-INDEX.md`
- Modify: `docs/architecture/04-DECISION-LOG.md`
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`
- Modify: `docs/architecture/PHASE-09-public-portal.md`
- Modify: `docs/development/ADMIN-APP.md`
- Modify: `docs/development/PORTAL-APP.md`
- Modify: `scripts/Test-ComposeSmoke.ps1`
- Test (modify): `scripts/tests/ComposeSmoke.Tests.ps1`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`

**Interfaces:**
- Consumes: Task 4's `GET /p/{key}/suggest` (one Api public-search call per request, the Api's 429 passed through), Task 2's forwarding of `X-Forwarded-For`, the 09a compose wiring (`TRUSTEDPROXY__TRUSTEDNETWORKS__0` on the Api trusts the pinned subnet, the Portal trusts `REVERSE_PROXY_CIDR`), the Api's `RateLimiting__Public__PermitLimit` and `__WindowSeconds`, the existing smoke helpers (`Invoke-Compose`, `Get-PublishedPort`, `Assert-Ready`).
- Produces:
  - `scripts/Test-ComposeSmoke.ps1`: an override that sets `RateLimiting__Public__PermitLimit: "3"` and `__WindowSeconds: "600"` on the Api and `TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${TECHSTRAP_SUBNET:-172.16.31.0/24}` on the Portal (map-style entries after the `ports: !override` lists, so the four-list-item pin still holds); the Portal's `/health/ready`; `Get-SuggestStatus` and `Assert-StatusCode`; three 200s then a 429 as `203.0.113.10`, then a 200 as `203.0.113.11`; the DryRun lines and the Pester pins. It still never runs `down -v`.
  - `docs/development/PORTAL-APP.md` (the 09b flows, the one markup site, the smoke, the known gaps), the D-045 "as built in 09b" consequences, the PHASE-09 ticks (T06 to T11, T21, T23 and two deliverables; T02, T04 and T18 stay open with a 09b note), the roadmap and discovery rows "09b complete (pending merge)".

- [ ] **Step 1: Write the failing tests**

The smoke pins check the dry run and the script text (the Portal's readiness, the lowered limit and the subnet trust in the override only, the call from inside the network, the two visitors); the documentation pins check the ticks (and what must stay open), the roadmap rows, the guide's headings and phrases, the as-built notes and the Admin guide's smoke paragraph.

`scripts/tests/ComposeSmoke.Tests.ps1`

```diff
@@ -50,6 +50,30 @@ Describe 'Test-ComposeSmoke.ps1' {
         foreach ($port in $ports) { $port | Should -Match '^127\.0\.0\.1::\d+$' }
     }
 
+    It 'checks the Portal too: it must answer /health/ready, and its published port is read like the others' {
+        $script:DryRun | Should -Match 'port portal 80'
+        $script:DryRun | Should -Match 'GET /health/ready on the Api, the Admin and the Portal, expecting 200'
+        $script:SmokeText | Should -Match "Get-PublishedPort -Service 'portal' -ContainerPort 80"
+        $script:SmokeText | Should -Match 'Assert-Ready -Name ''Portal'' -Port \$portalPort'
+    }
+
+    It 'lowers the Api public limit to 3 and makes the Portal trust the compose subnet, in the override only' {
+        $script:DryRun | Should -Match '(?s)api:\s+image: techstrap-smoke-api:local\s+ports: !override\s+- "127\.0\.0\.1::80"\s+environment:\s+RateLimiting__Public__PermitLimit: "3"\s+RateLimiting__Public__WindowSeconds: "600"'
+        $script:DryRun | Should -Match '(?s)portal:\s+image: techstrap-smoke-portal:local\s+ports: !override\s+- "127\.0\.0\.1::80"\s+environment:\s+TRUSTEDPROXY__TRUSTEDNETWORKS__0: \$\{TECHSTRAP_SUBNET:-172\.16\.31\.0/24\}'
+        # The lowered limit lives in the smoke override only: neither compose file may carry it.
+        (Get-Content -LiteralPath (Join-Path $script:RepoRoot 'docker-compose.yml') -Raw) | Should -Not -Match 'RateLimiting__Public'
+        (Get-Content -LiteralPath (Join-Path $script:RepoRoot 'deploy' 'docker-compose.yml') -Raw) | Should -Not -Match 'RateLimiting__Public'
+    }
+
+    It 'proves the rate limit sees the real client address through the Portal suggest adapter, from inside the compose network' {
+        $script:DryRun | Should -Match 'exec -T api curl .*http://portal/p/smoke/suggest\?q=printer'
+        $script:DryRun | Should -Match 'X-Forwarded-For: 203\.0\.113\.10 .* 200 three times, then 429'
+        $script:DryRun | Should -Match 'X-Forwarded-For: 203\.0\.113\.11 .* 200'
+        $script:SmokeText | Should -Match "'exec', '-T', 'api', 'curl'"
+        $script:SmokeText | Should -Match 'Assert-StatusCode'
+        $script:SmokeText | Should -Match '\$smokePermitLimit = 3'
+    }
+
     It 'builds the four images one after another, never with up --build (parallel restores corrupt the shared NuGet cache mount)' {
         foreach ($service in 'api', 'worker', 'admin', 'portal') {
             $script:DryRun | Should -Match "build $service"
```

`scripts/tests/RepositoryDocs.Tests.ps1`

```diff
@@ -163,26 +163,30 @@ Describe 'D-045 (the public portal)' {
         (Get-RepoText 'docs/architecture/03-PACKAGE-MAP.md') | Should -Match 'MapSeoRobotsTxt'
     }
 
-    It 'ticks only the tasks and deliverables 09a fully delivers, and the roadmap and discovery rows say 09a is complete, pending merge' {
+    It 'ticks only the tasks and deliverables 09a and 09b fully deliver, and the roadmap and discovery rows say 09b is complete, pending merge' {
         $spec = Get-RepoText 'docs/architecture/PHASE-09-public-portal.md'
-        foreach ($number in 1, 3, 5, 17, 19, 22) {
+        foreach ($number in 1, 3, 5, 6, 7, 8, 9, 10, 11, 17, 19, 21, 22, 23) {
             $id = 'P09-T{0:00}' -f $number
-            $spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is delivered by 09a"
+            $spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is delivered by 09a or 09b"
         }
-        foreach ($number in 2, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 21, 23) {
+        # T02 waits for the KB client of 09c, T04 for the sitemap, T18 for the owner's compose run of the smoke, T12 to T16 are 09c, T20 is deferred.
+        foreach ($number in 2, 4, 12, 13, 14, 15, 16, 18, 20) {
             $id = 'P09-T{0:00}' -f $number
-            $spec | Should -Match ('(?m)^- \[ \] \*\*' + $id + '\*\*') -Because "$id is not finished by 09a"
+            $spec | Should -Match ('(?m)^- \[ \] \*\*' + $id + '\*\*') -Because "$id is not finished by 09b"
         }
         $spec | Should -Match '(?m)^- \[x\] `TechStrap\.Portal` host with `\.env\.example`, forwarded-headers and client-IP forwarding to the API\.'
         $spec | Should -Match '(?m)^- \[x\] Branded layout with per-product theming and NotFound handling\.'
+        $spec | Should -Match '(?m)^- \[x\] Contact page with honeypot, attachments, deflection island, submitted page\.'
+        $spec | Should -Match '(?m)^- \[x\] Customer ticket view, reply \(incl\. Closed -> follow-up\), lost-link, attachment pass-through\.'
         $spec | Should -Match '(?m)^- \[ \] Typed clients for public product, public ticket, customer ticket, public KB\.'
-        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 09 \|.*D-045.*\| 09a complete \(pending merge\)'
-        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 09 \|.*\| 09a complete \(pending merge\)'
+        $spec | Should -Match '(?m)^- \[ \] KB home/category/search/article pages'
+        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 09 \|.*D-045.*\| 09a merged \(PR #14\); 09b complete \(pending merge\); 09c not started'
+        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 09 \|.*\| 09a merged \(PR #14\); 09b complete \(pending merge\); 09c not started'
     }
 
     It 'has a Portal developer guide, linked from the README, that lists every setting and the known gaps' {
         $guide = Get-RepoText 'docs/development/PORTAL-APP.md'
-        foreach ($heading in '## Run it locally', '### Configuration', '## How a page is served', '## Where things live', '## Tests', '## Known gaps in 09a') {
+        foreach ($heading in '## Run it locally', '### Configuration', '## How a page is served', '### Forms and uploads', '### Suggestions beside the subject', '### The ticket page and attachments', '### The lost-link page', '## Where things live', '## Tests', '## Known gaps') {
             $guide | Should -Match ('(?m)^' + [regex]::Escape($heading))
         }
         foreach ($key in 'API__BASEURL', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_PORTAL_DEFAULT_PRODUCT', 'TECHSTRAP_PORTAL_SHOW_POWERED_BY', 'CANONICALHOST__CANONICALHOST') {
@@ -227,6 +231,36 @@ Describe 'D-045 addendum (PHASE-09b rulings, 2026-10-06)' {
     }
 }
 
+Describe 'D-045 as built in 09b' {
+    BeforeAll {
+        $log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
+        $script:Section = [regex]::Match($log, '(?s)## D-045:.*?(?=\r?\n## D-\d+:|\z)').Value
+        $script:Guide = Get-RepoText 'docs/development/PORTAL-APP.md'
+    }
+
+    It 'records what the 09b build found, as consequences of the addendum' {
+        foreach ($phrase in 'As built in 09b: a post to an unknown product', 'As built in 09b: the request size limit', 'As built in 09b: the honeypot', 'As built in 09b: the ticket page',
+                'As built in 09b: the suggest adapter', 'As built in 09b: the smoke', 'Known in 09b: no copy button') {
+            $script:Section | Should -Match ([regex]::Escape($phrase)) -Because "the as-built list must carry: $phrase"
+        }
+    }
+
+    It 'has a developer guide that describes the 09b flows, the one markup site and the smoke check' {
+        foreach ($phrase in 'ts-kb-suggestions', 'ReceivedReference', 'CustomerMessageBody', 'FollowUpLink', 'AttachmentPassThrough', 'RequestTooLargeMiddleware', 'PortalScripts.Tests.ps1', 'Test-ComposeSmoke.ps1', 'reply-conflict', '/p/{key}/suggest') {
+            $script:Guide | Should -Match ([regex]::Escape($phrase)) -Because "PORTAL-APP.md must mention $phrase"
+        }
+        $script:Guide | Should -Not -Match 'answer 404 until 09b'
+        $script:Guide | Should -Not -Match '`MarkupString` is used nowhere yet'
+    }
+
+    It 'tells the Admin guide that the compose smoke now covers the Portal and the real client address' {
+        $admin = Get-RepoText 'docs/development/ADMIN-APP.md'
+        $paragraph = $admin -split '(?:\r?\n){2}' | Where-Object { $_ -match '\*\*Compose smoke\.\*\*' } | Select-Object -First 1
+        $paragraph | Should -Match 'Portal'
+        $paragraph | Should -Match 'real client address'
+    }
+}
+
 Describe 'the deployment runbook' {
     BeforeAll { $script:Runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md' }
 
```


- [ ] **Step 2: Run the tests to see them fail**

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ComposeSmoke.Tests.ps1 -Output Minimal`
Expected: FAIL: `Tests Passed: 11, Failed: 3` (the Portal's readiness, the override, the real-address check).

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1 -Output Minimal`
Expected: FAIL: `Tests Passed: 27, Failed: 5` (the ticks and rows, the guide headings, the as-built notes, the guide phrases, the Admin paragraph).

- [ ] **Step 3: Implement the smoke check**

The check is made from inside the compose network on purpose: the source is the api container, an address on the pinned subnet, so the Portal (which the override makes trust that subnet) takes the visitor from `X-Forwarded-For` and forwards it to the Api, whose public limit the override lowers to 3. A call from the host through the published port would arrive from the Docker gateway, whose address differs between Linux and Docker Desktop, so it could not be trusted portably. If the Api counted the Portal's address instead, visitor B would be refused too. The adapter is the probe because it needs no seed data (an unknown product is an empty page from the Api) and passes the Api's 429 through.

`scripts/Test-ComposeSmoke.ps1`

```diff
@@ -1,8 +1,9 @@
 <#
 .SYNOPSIS
-Starts the local compose stack, checks that the Api and the Admin answer, and stops it again.
+Starts the local compose stack, checks that the Api, the Admin and the Portal answer and that the rate limit sees the real client address, and stops it again.
 .DESCRIPTION
-The PHASE-07 T20 check: "docker compose up" gives a healthy Admin and an Api whose /health/ready answers 200. It is opt-in (it builds four images and needs Docker),
+The PHASE-07 T20 check: "docker compose up" gives a healthy Admin and an Api whose /health/ready answers 200. PHASE-09 T18 adds the Portal to it: its /health/ready must answer 200 too, and the
+Api's rate limit must count the visitor, not the Portal's container. It is opt-in (it builds four images and needs Docker),
 so it is not part of the default CI run; run it by hand before merging a change to a Dockerfile, a compose file or the host wiring, or start the "Compose smoke" workflow.
 
 It never touches a stack you already run. It uses its own compose project name (techstrap-smoke, never the default "techstrap"), publishes every host port on a free
@@ -11,6 +12,13 @@ key-ring volumes of the smoke project stay for the next run (they are named tech
 
 The Admin starts with placeholder OIDC settings (docker-compose.yml), so nothing here signs in; it checks the container's health and its /health/ready endpoint.
 
+The real-address check (D-019): the override below lowers the Api's public rate limit to 3 requests per 10 minutes and makes the Portal trust the compose subnet. The script then calls the Portal's
+suggest adapter, GET /p/smoke/suggest, which makes one call to the Api's public KB search per request, from inside the network (docker compose exec in the api container, which has curl):
+the source is a container address on the pinned subnet, so the Portal treats it as the proxy hop and takes the visitor from X-Forwarded-For. Three calls as 203.0.113.10 answer 200 and the fourth 429
+(the Api counted that visitor and passed its 429 through the adapter); then 203.0.113.11 answers 200 (another visitor is unaffected). If the Api counted the Portal's address instead, the second visitor
+would be refused too. A call from the host through the published port would arrive from the Docker gateway, whose address differs between Linux and Docker Desktop, so it could not be trusted portably;
+the call from the api container always comes from the pinned subnet. The override changes nothing in docker-compose.yml, and the lowered limit belongs to the smoke project only.
+
 With -CheckDeployCompose it also resolves deploy/docker-compose.yml (the image-only UAT and production stack) with the committed UAT input template and dummy scoped env files:
 "docker compose config --quiet" only. It never pulls an image and never starts that stack, which needs real images and an external Postgres.
 .PARAMETER ProjectName
@@ -57,6 +65,9 @@ services:
     image: techstrap-smoke-api:local
     ports: !override
       - "127.0.0.1::80"
+    environment:
+      RateLimiting__Public__PermitLimit: "3"
+      RateLimiting__Public__WindowSeconds: "600"
   worker:
     image: techstrap-smoke-worker:local
   admin:
@@ -67,11 +78,19 @@ services:
     image: techstrap-smoke-portal:local
     ports: !override
       - "127.0.0.1::80"
+    environment:
+      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${TECHSTRAP_SUBNET:-172.16.31.0/24}
   mailpit:
     ports: !override
       - "127.0.0.1::8025"
 '@
 
+# The public limit the override sets (RateLimiting__Public__PermitLimit above) and the two visitors the check pretends to be (documentation addresses, RFC 5737).
+$smokePermitLimit = 3
+$visitorA = '203.0.113.10'
+$visitorB = '203.0.113.11'
+$suggestUrl = 'http://portal/p/smoke/suggest?q=printer'
+
 $overridePath = Join-Path ([System.IO.Path]::GetTempPath()) "techstrap-smoke-$([guid]::NewGuid().ToString('N')).override.yml"
 $composeArguments = @('compose', '-p', $ProjectName, '-f', (Resolve-Path -LiteralPath $ComposeFile).Path, '-f', $overridePath)
 
@@ -97,6 +116,24 @@ function Get-PublishedPort {
     return [int] $Matches['port']
 }
 
+# One call to the Portal's suggest adapter from inside the compose network, as the given visitor; returns the HTTP status the Portal answered.
+function Get-SuggestStatus {
+    param([Parameter(Mandatory)][string] $Visitor)
+
+    $code = Invoke-Compose -Arguments @('exec', '-T', 'api', 'curl', '--silent', '--output', '/dev/null', '--write-out', '%{http_code}', '--max-time', '30', '--header', "X-Forwarded-For: $Visitor", $suggestUrl)
+    return [int] $code.Trim()
+}
+
+function Assert-StatusCode {
+    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][int] $Actual, [Parameter(Mandatory)][int] $Expected)
+
+    if ($Actual -ne $Expected) {
+        throw "$Name answered $Actual (expected $Expected)."
+    }
+
+    Write-Output "ok   $Name -> $Expected"
+}
+
 function Assert-Ready {
     param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][int] $Port)
 
@@ -173,8 +210,12 @@ if ($DryRun) {
 
     Write-Output "docker $($composeArguments -join ' ') port api 80"
     Write-Output "docker $($composeArguments -join ' ') port admin 80"
-    Write-Output "GET /health/ready on the Api and on the Admin, expecting 200"
+    Write-Output "docker $($composeArguments -join ' ') port portal 80"
+    Write-Output "GET /health/ready on the Api, the Admin and the Portal, expecting 200"
     Write-Output "docker $($composeArguments -join ' ') ps admin --format json   (Health must be healthy)"
+    $curl = "docker $($composeArguments -join ' ') exec -T api curl --silent --output /dev/null --write-out %{http_code} --header"
+    Write-Output "$curl 'X-Forwarded-For: $visitorA' $suggestUrl   (X-Forwarded-For: $visitorA -> 200 three times, then 429: the Api counts the visitor)"
+    Write-Output "$curl 'X-Forwarded-For: $visitorB' $suggestUrl   (X-Forwarded-For: $visitorB -> 200: another visitor is unaffected)"
     if (-not $KeepRunning) {
         Write-Output "docker $($composeArguments -join ' ') down"
     }
@@ -205,8 +246,10 @@ try {
 
     $apiPort = Get-PublishedPort -Service 'api' -ContainerPort 80
     $adminPort = Get-PublishedPort -Service 'admin' -ContainerPort 80
+    $portalPort = Get-PublishedPort -Service 'portal' -ContainerPort 80
     Assert-Ready -Name 'Api' -Port $apiPort
     Assert-Ready -Name 'Admin' -Port $adminPort
+    Assert-Ready -Name 'Portal' -Port $portalPort
 
     $admin = (Invoke-Compose -Arguments @('ps', 'admin', '--format', 'json') | ConvertFrom-Json)
     if ($admin.Health -ne 'healthy') {
@@ -214,11 +257,19 @@ try {
     }
 
     Write-Output 'ok   Admin container is healthy'
+
+    # The Api's rate limit must see the visitor behind the Portal (D-019): the limit is 3, so visitor A is refused on the fourth call and visitor B is not.
+    for ($call = 1; $call -le $smokePermitLimit; $call++) {
+        Assert-StatusCode -Name "suggest $call of $smokePermitLimit as $visitorA" -Actual (Get-SuggestStatus -Visitor $visitorA) -Expected 200
+    }
+
+    Assert-StatusCode -Name "suggest $($smokePermitLimit + 1) as $visitorA (over the limit)" -Actual (Get-SuggestStatus -Visitor $visitorA) -Expected 429
+    Assert-StatusCode -Name "suggest 1 as $visitorB (another visitor)" -Actual (Get-SuggestStatus -Visitor $visitorB) -Expected 200
     $failed = $false
 }
 finally {
     if ($failed) {
-        Write-Output (Invoke-Compose -Arguments @('logs', '--tail', '60', 'api', 'admin') -AllowFailure)
+        Write-Output (Invoke-Compose -Arguments @('logs', '--tail', '60', 'api', 'admin', 'portal') -AllowFailure)
     }
 
     if (-not $KeepRunning) {
```


- [ ] **Step 4: Write the documentation**

PORTAL-APP.md describes the 09b flows (forms and uploads, the suggestions, the ticket page and attachments, the lost-link page), the new headers and redaction keys, the smoke, the file layout, the tests and the known gaps (including the findings of this phase). The D-045 addendum gets its as-built consequences. PHASE-09 ticks only what 09b fully delivers: T02 waits for the rest of the KB client (09c), T04 for the sitemap, T18 for the owner's compose run, and the two deliverable lines for the contact and ticket flows are ticked. Replace the whole of `docs/development/PORTAL-APP.md` with the content below, then apply the other diffs.

`docs/development/PORTAL-APP.md` (new)

````markdown
# Portal app (customers)

`TechStrap.Portal` is the public site customers use: a product's own help and support pages, with the product's name, logo and accent. It never touches the database: every page asks the TechStrap API,
anonymously. This page is for people who run it and people who extend it. How agents work the tickets customers send is in [ADMIN-APP.md](ADMIN-APP.md); the decisions behind the Portal are D-045 in the
[decision log](../architecture/04-DECISION-LOG.md) (with its 2026-10-06 addendum for 09b) and the spec is [PHASE-09](../architecture/PHASE-09-public-portal.md).

PHASE-09 is delivered in three pull requests. **09a** is the foundation: the settings, the API client, the per-product theme and shell, the product home, the root page, the ticket-page headers, robots.txt, log
redaction and the architecture rules. **09b** (this page describes both) adds the customer flows: the contact form with its suggestions and received page, the ticket page with replies and the Closed follow-up,
the attachment pass-through and the lost-link page. **09c** adds the knowledge base pages, the sitemap and the polish pass. The routes of all three are in `PortalRoutes`.

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
| `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` | No | blank | A product key. When set, `/` redirects (302) to `/p/<key>`; blank shows a neutral page with no product list |
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
- **Copy** lives in `ShellCopy`, `ProblemCopy`, `FormCopy`, `ContactCopy`, `LostLinkCopy` and `TicketCopy`; routes in `PortalRoutes` (a page declares `@attribute [Route(PortalRoutes.XTemplate)]` and builds every link with a builder).

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

### Suggestions beside the subject

The contact page puts `<ts-kb-suggestions field="subject" src="/p/{key}/suggest">` after the subject field, with a plain link to the help search inside it (shown only without script), and loads
`wwwroot/js/kb-suggestions.js` as `<script type="module" src=...>` (no CSP change). The module defines the element: a 300 ms debounce, at least 3 characters, one request at a time (a newer one aborts the older
and its answer is dropped), results written with `textContent` and links built only from root-relative paths, hide-on-error, and clean-up in `disconnectedCallback`. It never parses text as markup, and a test fails
if the file ever contains `innerHTML`. `GET /p/{key}/suggest?q=` (`SuggestEndpoint`) is the Portal-hosted adapter: it asks `IPublicKbClient.SearchAsync` (a read, forwarding the visitor's address), cuts the text at 200
characters, returns at most 5 `{title, snippet, href}` items with the links built by `PortalRoutes.KbArticle`, passes the API's 429 through and answers an empty list for a blank text or any other failure.
The module is tested with `node --test` (`tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs`, run by `scripts/tests/PortalScripts.Tests.ps1`, skipped without node).

### The ticket page and attachments

`/t/{token}` (`Ticket.razor`) parses the token first (`TicketToken.TryParse`; a malformed one is the uniform 404 and the API is never asked), loads the ticket through `ICustomerTicketClient`, then the ticket's
product theme (an inactive or unknown product is the neutral theme, never a 404). `CustomerTicketPresenter` builds the view model: the status in the customer's words (New "Received", Open "In progress",
Pending "Waiting for your reply", Solved "Solved" with a note that a reply reopens it, Closed "Closed" with the note that a reply starts a follow-up), "You" for the customer's messages, the API's resolved name
for an agent exactly as it came, a neutral label for the system. **`CustomerMessageBody` is the single place the Portal turns text into markup** (`PortalRules.MarkupStringSites`): the API sanitises the message HTML.
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

`ApiConnection` (internal, `Clients/`) is the only place the Portal uses HTTP. It sends reads through a client that retries transport errors, 408 and 502 to 504 (twice, honouring `Retry-After` up to
2 seconds, no circuit breaker) and writes through a client that never retries. Both forward the visitor's address in `X-Forwarded-For` (`AddForwardedClientIp`; the API trusts it only from the compose subnet,
D-019) and have no logging handlers. `ProblemMapping` turns every answer into a `Result`: 400 keeps the API's field codes, 404 is one not-found whatever the API called it, 409 is `reply-conflict`, 413 and 415 are the
attachment errors, 429 is rate limited, any 5xx or transport error is `api-unavailable`, with fixed sentences from `ProblemCopy`. A call made as a ticket's customer takes a `TicketToken` (43 base64url characters;
it prints as `[token]`), which becomes the `X-Ticket-Token` header of that request only. The typed clients are `IPublicProductClient`, `IPublicTicketClient` (multipart intake), `ICustomerTicketClient` (view, reply,
lost link, attachment stream) and `IPublicKbClient` (search only; 09c extends it). `MultipartForm` builds the bodies (text fields first, one `Attachments` part per file, the file streams owned by the request).

### Headers, robots.txt and the canonical host

`UseTechStrapWebHost(PortalHeaderRules.Rules)` applies per-path rules after the shared security headers: everything under `/t` gets `Referrer-Policy: no-referrer`, `Cache-Control: no-store` and
`X-Robots-Tag: noindex`, only `/t/{token}/attachments/{id}` is sandboxed (so the ticket page keeps the normal CSP), and the four form pages of a product (`/p/{key}/contact`, `/contact/received`, `/lost-link` and
`/suggest`) get `no-store` and `noindex`. `/robots.txt` disallows `/t/` and names `{public url}/sitemap.xml`, which answers 404 until 09c. The canonical-host redirect is an allow-list of legacy hosts and does
nothing until configured.

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
  Clients/        ApiConnection, ProblemMapping and ProblemCopy, TicketToken, ApiClientRegistration (the two named clients), the typed clients (IPublicProductClient, IPublicTicketClient,
                  ICustomerTicketClient, IPublicKbClient), MultipartForm, AttachmentFileName, ApiQuery, ApiDownload
  Components/
    Layout/       PortalLayout, ProductHeader, ProductFooter
    Pages/        Home (the root), ProductHome, Contact, ContactReceived, Ticket, LostLink, NotFound, Error, StyleGuide (Development only)
    Tickets/      CustomerMessageBody (the one markup site), MessageThread, TicketStatusBanner
    Ui/           AccentScope, PoweredByFooter, ProductUnavailable, DevelopmentOnly, ErrorSummary, FieldError, FormField, AttachmentInput, HoneypotField
  Forms/          FormError and FormFields, FormCopy, FormFailure, AttachmentRules, EmailRules, ContactFormViewModel and its validator, ReplyForm, LostLinkForm, ContactCopy, ReceivedReference
  Headers/        PortalHeaderRules (the /t rules, the attachment sandbox and the form pages)
  Products/       ProductThemeViewModel, ProductScope, ProductPageBase
  Routing/        PortalRoutes, ProductKeyShape
  Seo/            PortalSeoRegistration (Blazor.Seo: base URL, robots.txt, canonical host)
  Settings/       PortalOptions and its validator
  Suggestions/    SuggestEndpoint (GET /p/{key}/suggest)
  Tickets/        CustomerTicketPresenter and its view models, TicketCopy, FollowUpLink, AttachmentPassThrough
  Uploads/        RequestTooLargeMiddleware
  Styles/         SCSS partials over the brand tokens (docs/BRAND.md)
  wwwroot/js/     kb-suggestions.js
```

Rules the code follows (the architecture tests check them): the Portal references **Contracts and Hosting only**; **components never inject `HttpClient`** (only `Clients/` mentions it); no inline script
or style; no interactive render mode; `MarkupString` is used in one file, `Components/Tickets/CustomerMessageBody.razor` (09c adds `KbArticleBody`, argued for in its commit); every plain-text DTO field is encoded.

## Tests

`tests/TechStrap.Portal.Tests` runs the host in memory with a stub API behind its two named clients (`PortalFactory.Api`, `StubApiHandler`), so a test sees what the API would see, including the headers the
Portal added. `ProxyHopStartupFilter` puts the host behind a trusted reverse proxy and every API-calling host test asserts the visitor's address with `AssertEveryCallBore`; `OkProbeStartupFilter` answers 200 on a
path no Portal route matches. A test that needs the real server (a request size limit) calls `UseKestrel(0)` and `StartServer()` and reads the address from `IServerAddressesFeature`. bUnit covers the layout.
`tests/TechStrap.Portal.Tests/js` holds the node tests of the browser module. The shared rules are in `TechStrap.Architecture.Tests` (`PortalRules`), the Hosting header-rule mechanism in `TechStrap.Api.Tests`
(`PathHeaderRuleHostTests`), and the log and Sentry redaction in `TechStrap.Api.Tests`.

## Known gaps

- `/robots.txt` names `/sitemap.xml`, which answers 404 until 09c maps it. The search box on the product home and the KB links answer 404 until 09c.
- There is no "copy" button for the ticket number, no live character counter and no "sending" state on the submit button: they need script, and 09b keeps every flow script-free (09c's polish pass decides).
- A chunked post over the size limit is the framework's 400 about an antiforgery token, not a 413 (a browser form post always declares its length).
- A post to an unknown product is the framework's 400 with no body (the not-found page is re-executed with the post and has no handler), not the 404 page a GET gets. Nothing is created.
- A legacy-host redirect decodes percent-escapes in the query string (the package builds the target with `Uri.ToString()`), so a value that holds an encoded `&` or `#` changes meaning. It affects only hosts
  in `CANONICALHOST__LEGACYHOSTS`; report it upstream before using the redirect with the contact prefill.
- `NavigationManager.NotFound()` adds the framework's `blazor-enhanced-nav: allow` response header to an unknown product's 404, which the router's own unknown-route 404 does not carry. The bodies are identical,
  and both are 404, so it reveals nothing about which products exist.
- The Portal does not use `GlobalErrorBoundary`: its Try again button needs interactivity, and catching a render error in the page would answer 200 with a branded page instead of the plain 500 error page.
- Sentry has no general email rule: it masks the `name`, `email`, `subject` and `ref` query values and the `/t/{token}` path only (Serilog's email pattern does catch addresses in logs).
- If OpenTelemetry tracing were enabled, server spans would carry `url.path=/t/<token>`. It is off by default; masking it is a follow-up.
- A double click on "Send message" can send twice before the redirect arrives (there is no script to disable the button); the API creates a ticket for each.
````


`docs/architecture/00-DISCOVERY-INDEX.md`

```diff
@@ -30,7 +30,7 @@
 | 06 | [Ticket operations](PHASE-06-ticket-operations.md) | 05 | 07, 08, 09 | Complete |
 | 07 | [Admin app](PHASE-07-admin-app.md) | 02, 06 | 08 (editor UI), 10 | 07 complete (pending merge): 07a merged (PR #9), 07b merged (PR #10), 07c implemented; owner action 7 (Authentik) still open, so P07-T02 stays unticked |
 | 08 | [Knowledge base](PHASE-08-knowledge-base.md) | 06 (07 for the editor UI) | 09 | PHASE-08 complete (pending merge): the API (Tasks 1-8) and the Admin (editor, categories, article picker) are implemented; the owner's manual checks are open |
-| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 09a complete (pending merge); 09b and 09c not started |
+| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 09a merged (PR #14); 09b complete (pending merge); 09c not started |
 | 10 | [Live updates](PHASE-10-live-updates.md) | 07 | 12 | Not started |
 | 11 | [Client SDK](PHASE-11-client-sdk.md) | 05 | 12 | Not started |
 | 12 | [Release hardening](PHASE-12-release-hardening.md) | all | v1.0.0 | Not started |
```

`docs/architecture/04-DECISION-LOG.md`

```diff
@@ -1716,6 +1716,17 @@ The owner's rulings for PHASE-09b, and what the plan's spike proved. They extend
 - **Request size limit.** `[RequestSizeLimit(IntakeLimits.FormBodyBytes)]` on the page component is read as endpoint metadata and applied by endpoint routing, before the antiforgery middleware and before the form is read (the limit feature is already `2000` and not read-only in a middleware placed before `UseAntiforgery`, tested with `UseKestrel(0)` because `TestServer` has no such feature). A body over the limit is rejected without being buffered, but the framework reports it as a 400 with the antiforgery text, so a small middleware answers a declared `Content-Length` over the applied limit with a plain 413 first; a chunked body over the limit is still the 400.
 - **Redirect after post.** `NavigationManager.NavigateTo` in a static SSR handler answers 302 with an absolute `Location`; the handler returns straight after it.
 
+**Consequences of the addendum (as built in 09b)**
+- **As built in 09b: a post to an unknown product is a 400, not a 404.** The product page asks the API for the product before the form handler runs and ends in `NavigationManager.NotFound()`; the framework re-executes the post against the not-found page, which has no handler named `contact`, so a post to an unknown, inactive or malformed product (with a valid antiforgery token) gets the framework's own empty 400. Nothing is created. A GET is the neutral 404 page. The 09a note that "an unknown product 404s before the form handler runs" was right about the order and wrong about the status.
+- **As built in 09b: the request size limit.** `[RequestSizeLimit(IntakeLimits.FormBodyBytes)]` on the contact and ticket pages is the limit; `RequestTooLargeMiddleware` (before `UseAntiforgery`) turns a declared `Content-Length` over it into a plain 413 with the Portal's own sentence, because the framework reports the failed form read as an antiforgery 400. A chunked body over the limit is still that 400. A file is read with `OpenReadStream(IntakeLimits.MaxFileBytes)` (the default is 512,000 bytes); Cmsify's media upload uses the same pattern (an explicit maximum, the stream passed on without buffering, `[RequestSizeLimit]` on the receiving endpoint).
+- **As built in 09b: the honeypot** is sent to the API as `Website` and a filled one gets the API's believable 201; the Portal never short-circuits, so product validation and the response stay uniform. The PHASE-09 text that said otherwise is corrected.
+- **As built in 09b: the ticket page.** The token is parsed first; the API is never asked for a malformed one; every way to fail to find a ticket (and a wrong attachment id) is the same neutral 404, byte for byte. An inactive or unknown product on a valid ticket is the neutral theme, not a 404. `CustomerMessageBody` is the only place the Portal renders markup. The follow-up redirect is built from the last segment of `FollowUpViewUrl` by `FollowUpLink` and `PortalRoutes.Ticket`, so it never leaves the site; an unreadable link shows a generic confirmation.
+- **As built in 09b: the suggest adapter** is `GET /p/{key}/suggest`, a minimal-API endpoint in `Suggestions/` (it calls `IPublicKbClient`, so `PortalRules` allows it outside `Clients/`), and the element is `wwwroot/js/kb-suggestions.js`, tested with `node --test` through `scripts/tests/PortalScripts.Tests.ps1`.
+- **As built in 09b: the smoke** (`scripts/Test-ComposeSmoke.ps1`) starts the Portal, checks its `/health/ready` and proves the real client address through `/p/smoke/suggest`: its override lowers the Api's public limit to 3 and makes the Portal trust the compose subnet, and the calls are made from inside the network (`docker compose exec api curl`), because a call from the host arrives from the Docker gateway, whose address differs between Linux and Docker Desktop. It never runs `down -v`.
+- **As built in 09b: redaction.** The `subject` and `ref` query values are masked in Serilog and in Sentry, and `q` in Serilog (Hosting, shared by every host: masking those parameter names in the Api's and the Admin's logs is accepted).
+- **Known in 09b: no copy button, counter or sending state.** The ticket number has no copy affordance, the message has no character counter and the submit button has no in-flight state, because each needs script and every 09b flow is script-free; a double click can send twice. The 09c polish pass decides.
+- **Known in 09b: `TicketToken.Value` is internal** and read in two places only (`ApiConnection` for the header and `PortalRoutes` for a link).
+
 ### Approval
 - **Approved by:** Jon Seeley (owner, PHASE-09 planning)
 - **Approved on:** 2026-10-05
```

`docs/architecture/99-IMPLEMENTATION-ROADMAP.md`

```diff
@@ -25,7 +25,7 @@ Cross-cutting conventions every phase follows (fixed during the consistency revi
 | 06 | [Ticket operations](PHASE-06-ticket-operations.md) | 05 | 07, 08, 09 | Alongside 02 and 11 | D-006, D-008, D-009, D-022, D-035, D-036, D-037, D-038, D-039 | Complete (PRs #6, #7 and #8 merged) |
 | 07 | [Admin app](PHASE-07-admin-app.md) | 02, 06 | 08 (editor UI), 10 | 11 alongside; 08 API work alongside | D-017, D-022, D-040, D-041 | 07 complete (pending merge): 07a merged (PR #9), 07b merged (PR #10), 07c implemented; owner action 7 (Authentik) still open, so P07-T02 stays unticked |
 | 08 | [Knowledge base](PHASE-08-knowledge-base.md) | 06 (07 for the editor UI) | 09 | API tasks T01 to T12 alongside 07; editor tasks wait for 07 | D-011, D-014, D-021, D-044 | PHASE-08 complete (pending merge): the API (Tasks 1-8) and the Admin (editor, categories, article picker) are implemented; the owner's manual checks are open |
-| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 10 and 11 alongside | D-002, D-017, D-019, D-045 | 09a complete (pending merge); 09b and 09c not started |
+| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 10 and 11 alongside | D-002, D-017, D-019, D-045 | 09a merged (PR #14); 09b complete (pending merge); 09c not started |
 | 10 | [Live updates](PHASE-10-live-updates.md) | 07 | 12 | 08, 09, 11 alongside | D-007, D-018 | Not started |
 | 11 | [Client SDK](PHASE-11-client-sdk.md) | 05 | 12 | Alongside 06 to 10 | D-005, D-020 | Not started |
 | 12 | [Release hardening](PHASE-12-release-hardening.md) | all | v1.0.0 | Last; security, load, restore and UAT tasks can overlap once their inputs exist | D-003, D-022 | Not started |
```

`docs/architecture/PHASE-09-public-portal.md`

```diff
@@ -130,8 +130,8 @@ Not used: `Blazor.Auth` (portal is anonymous), `Blazor.Tracking` (Not applicable
 - [x] `TechStrap.Portal` host with `.env.example`, forwarded-headers and client-IP forwarding to the API.
 - [ ] Typed clients for public product, public ticket, customer ticket, public KB.
 - [x] Branded layout with per-product theming and NotFound handling.
-- [ ] Contact page with honeypot, attachments, deflection island, submitted page.
-- [ ] Customer ticket view, reply (incl. Closed -> follow-up), lost-link, attachment pass-through.
+- [x] Contact page with honeypot, attachments, deflection island, submitted page.
+- [x] Customer ticket view, reply (incl. Closed -> follow-up), lost-link, attachment pass-through.
 - [ ] KB home/category/search/article pages with SEO, JSON-LD, sitemap, robots.
 - [ ] Page-level SSR tests and bUnit tests; portal container healthy under compose.
 
@@ -145,6 +145,7 @@ Not used: `Blazor.Auth` (portal is anonymous), `Blazor.Tracking` (Not applicable
   - **Depends on:** P09-T01, P05, P06, P08 DTOs
   - **Validation:** Stub-handler tests: success/400/404/429/503; POST not retried; `X-Forwarded-For` set from the trusted inbound header; token header never logged (log assertion).
   - **09a:** done: `ApiConnection`, `ProblemMapping`, the token capability and `IPublicProductClient` (`ApiConnectionTests`, `ProblemMappingTests`, `PublicProductClientTests`, `ForwardedClientIpHostTests`, `TicketTokenLeakTests`), with the fake-API harness. The ticket and KB clients arrive with 09b and 09c, so this task stays open.
+  - **09b:** 09b: the public ticket, customer ticket and KB (search) clients are done (`PublicTicketClientTests`, `CustomerTicketClientTests`, `PublicKbClientTests`, `ApiConnectionStreamTests`); the rest of the KB client arrives with 09c, so this task stays open.
 - [x] **P09-T03** Implement `BrandingThemeFactory`, `PortalLayout`, header/footer and the product-scope resolution (unknown/inactive -> NotFound)
   - **Depends on:** P09-T02, P02 tokens
   - **Validation:** Theory over accent colours (black, white, mid-gray, brand) asserts the computed `--ts-on-accent` meets 4.5:1 on the accent and `--ts-accent-ink` meets 4.5:1 on white; invalid colour falls back to the default; bUnit: unknown key renders NotFound.
@@ -157,24 +158,30 @@ Not used: `Blazor.Auth` (portal is anonymous), `Blazor.Tracking` (Not applicable
   - **Depends on:** P09-T03
   - **Validation:** bUnit/host test: home renders branded name and KB search box; unmatched route -> NotFound with 404 status.
   - **09a evidence:** `ProductHomeHostTests`, `RootPageHostTests`, `NotFoundHostTests`. `GlobalErrorBoundary` is not used: its retry button needs interactivity and it would turn the plain 500 page into a branded 200 (D-045).
-- [ ] **P09-T06** Build `ContactPage`, `ContactFormViewModel`, `HoneypotField`, `AttachmentInput`, and `SubmittedPage` (redirect-after-post)
+- [x] **P09-T06** Build `ContactPage`, `ContactFormViewModel`, `HoneypotField`, `AttachmentInput`, and `SubmittedPage` (redirect-after-post)
   - **Depends on:** P09-T03
   - **Validation:** Host test with fake API: valid post -> 302 to `/p/{key}/contact/received`; invalid -> 200 with error summary and preserved input; honeypot filled -> silently success-page without calling the API; 429 -> friendly message; antiforgery missing -> 400.
-- [ ] **P09-T07** Build `KbDeflectionSuggestions` island and place it on the contact page
+  - **09b evidence:** `ContactPageHostTests`, `ContactPostHostTests`, `ContactReceivedHostTests`, `ReceivedReferenceTests`, `ContactFormValidatorTests`, `AttachmentRulesTests`, `FormFailureTests`, `RequestTooLargeMiddlewareTests` (the real server). The honeypot is passed to the API instead of short-circuited (D-045 addendum).
+- [x] **P09-T07** Build `KbDeflectionSuggestions` island and place it on the contact page
   - **Depends on:** P09-T06, P08-T08
   - **Validation:** bUnit with fake `IPublicKbClient` and `TimeProvider`: rapid typing yields one search; stale responses discarded; cancellation on dispose; failure hides the panel; contact form still posts without the island.
-- [ ] **P09-T08** Build `CustomerTicketPage`, `CustomerTicketPresenter`, `MessageThread`, `CustomerMessageBody`, `TicketStatusBanner` with security headers (`no-store`, `no-referrer`, `noindex`)
+  - **09b evidence:** `SuggestEndpointHostTests`, `ContactSuggestionsHostTests` and `tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs` (run by `scripts/tests/PortalScripts.Tests.ps1`). It is a vanilla-JS `<ts-kb-suggestions>` element and a Portal `GET /p/{key}/suggest` adapter, not a Blazor island (D-045).
+- [x] **P09-T08** Build `CustomerTicketPage`, `CustomerTicketPresenter`, `MessageThread`, `CustomerMessageBody`, `TicketStatusBanner` with security headers (`no-store`, `no-referrer`, `noindex`)
   - **Depends on:** P09-T03, P06
   - **Validation:** Host test: valid token renders public messages only; invalid/expired/revoked all return the identical 404 body; response headers asserted; presenter unit test for Closed state.
-- [ ] **P09-T09** Build `CustomerReplyForm` with attachments, including Closed -> follow-up flow handling
+  - **09b evidence:** `TicketPageHostTests`, `TicketUniformNotFoundHostTests`, `CustomerTicketPresenterTests`, `TicketHeaderHostTests`; `CustomerMessageBody` is the one markup site (`PortalRuleTests`).
+- [x] **P09-T09** Build `CustomerReplyForm` with attachments, including Closed -> follow-up flow handling
   - **Depends on:** P09-T08
   - **Validation:** Host test: reply on Open ticket refreshes thread; reply on Closed ticket shows follow-up ticket link; oversize/disallowed attachment shows error; double-submit guarded.
-- [ ] **P09-T10** Build `LostLinkPage` (`/p/{key}/lost-link`)
+  - **09b evidence:** `TicketReplyHostTests`, `FollowUpLinkTests`, `ReplyAndEmailRulesTests`: a reply redirects to the same page, a reply on a Closed ticket redirects to the follow-up's own page on this site, a link that cannot be read gives a generic confirmation.
+- [x] **P09-T10** Build `LostLinkPage` (`/p/{key}/lost-link`)
   - **Depends on:** P09-T03
   - **Validation:** Host test: matching and non-matching emails produce byte-identical responses and timing within a small tolerance (no early-return branch visible in code review); 429 handled generically.
-- [ ] **P09-T11** Implement the portal `GET /t/{token}/attachments/{id}` pass-through adapter (D-017)
+  - **09b evidence:** `LostLinkHostTests`: the responses are byte-identical for any well-formed address; the timing assertion is dropped (D-038).
+- [x] **P09-T11** Implement the portal `GET /t/{token}/attachments/{id}` pass-through adapter (D-017)
   - **Depends on:** P09-T08
   - **Validation:** Host test: streams bytes with `attachment` disposition and `nosniff`; other tokens/ids -> uniform 404; no Infrastructure reference (architecture test).
+  - **09b evidence:** `TicketAttachmentHostTests`, `ApiConnectionStreamTests`, `TicketUniformNotFoundHostTests`, `TicketHeaderHostTests` (the real route under the sandbox rule).
 - [ ] **P09-T12** Build `KbHomePage` and `KbCategoryPage` with paging and `KbArticleCard`/`KbBreadcrumbs`
   - **Depends on:** P09-T03, P08-T08
   - **Validation:** bUnit: empty category shows empty state; paging links preserve query; shared + product articles appear.
@@ -197,6 +204,7 @@ Not used: `Blazor.Auth` (portal is anonymous), `Blazor.Tracking` (Not applicable
 - [ ] **P09-T18** Add portal to compose with forwarded-headers/subnet trust (D-019) and the end-to-end intake path (portal -> API) rate-limit check
   - **Depends on:** P09-T06, P05
   - **Validation:** `docker compose up`; hitting the contact form repeatedly from one client IP behind the proxy trips the API 429 for that IP only (second source IP unaffected); `/health/ready` 200.
+  - **09b:** 09b: the smoke check is written and pinned (`scripts/Test-ComposeSmoke.ps1`, `scripts/tests/ComposeSmoke.Tests.ps1`: the Portal's `/health/ready`, and the Api's rate limit seen through `GET /p/smoke/suggest` as two visitors); the owner's manual run of it and of the contact flow under compose ticks this task.
 - [x] **P09-T19** Add architecture rules: Portal references only Contracts; no `[Inject] HttpClient` in components; `MarkupString` restricted
   - **Depends on:** P09-T14
   - **Validation:** Architecture.Tests fail on a deliberate violation sample.
@@ -204,16 +212,18 @@ Not used: `Blazor.Auth` (portal is anonymous), `Blazor.Tracking` (Not applicable
 - [ ] **P09-T20** (Optional, **Assumption**) Playwright e2e: submit ticket -> read email link (from test SMTP sink) -> view -> reply
   - **Depends on:** P09-T09, P09-T18
   - **Validation:** Passes in nightly CI against compose with MailPit/test sink.
-- [ ] **P09-T21** (D-024) Contact-page prefill: bind `subject`, `name` and `email` from the query string into `ContactFormViewModel` through the same validation attributes and length constants as posted input; all three stay visible and editable (inputs carry `maxlength` equal to the model limit), no hidden field carries prefill data, nothing auto-submits, unknown parameters are ignored and never echoed; add `name` and `email` query values to the request-log redaction (P09-T17). App context (version, device) is not a URL concern: it goes through the SDK/API
+- [x] **P09-T21** (D-024) Contact-page prefill: bind `subject`, `name` and `email` from the query string into `ContactFormViewModel` through the same validation attributes and length constants as posted input; all three stay visible and editable (inputs carry `maxlength` equal to the model limit), no hidden field carries prefill data, nothing auto-submits, unknown parameters are ignored and never echoed; add `name` and `email` query values to the request-log redaction (P09-T17). App context (version, device) is not a URL concern: it goes through the SDK/API
   - **Depends on:** P09-T06, P09-T17
   - **Validation:** host tests: `?subject=&name=&email=` render three visible editable inputs with the values, HTML-encoded (injected markup is escaped); an over-length subject and an invalid email fail on post exactly as typed input does; extra parameters such as `product` or `token` change nothing; a log-capture test shows no prefilled name or email
+  - **09b evidence:** `ContactPageHostTests` (the prefill: visible, editable, encoded, unknown parameters ignored, nothing submitted), `ContactPostHostTests` (judged on post like typed text), `ContactFormViewModel.FromPrefill`, `RequestLogRedactionHostTests` (`name`, `email`, `subject` masked).
 - [x] **P09-T22** (D-024) "Powered by TechStrap" link and setting: portal options `ShowPoweredBy` bound from `TECHSTRAP_PORTAL_SHOW_POWERED_BY` (default `true`); the footer renders the line as a link to https://github.com/Syntax-Circus/techstrap (constant, neutral secondary ink, underlined, 4.5:1) on every page including NotFound and error pages, and omits it entirely when false
   - **Depends on:** P09-T03
   - **Validation:** host tests: with the default every page type contains exactly one link with the exact href and no other TechStrap text; with the option false none contains the line; the option defaults to true when the variable is unset; an invalid value fails startup validation
   - **09a evidence:** `PoweredByHostTests` (exactly one link on every page type, none when false, true by default, an invalid value fails the start) and `PoweredByFooterTests`.
-- [ ] **P09-T23** (D-024) Agent identity on the ticket view: `CustomerMessage` shows the API-resolved `AuthorDisplayName` as-is (HTML-encoded) for agent messages and "You" for the customer's own; no agent email, id or avatar is rendered
+- [x] **P09-T23** (D-024) Agent identity on the ticket view: `CustomerMessage` shows the API-resolved `AuthorDisplayName` as-is (HTML-encoded) for agent messages and "You" for the customer's own; no agent email, id or avatar is rendered
   - **Depends on:** P09-T08, P06-T22
   - **Validation:** bUnit: an agent message shows "Sam from Orbitly Support"; a fixture with override "Samantha from Orbitly Support" shows it unchanged; markup in the name is encoded; a DTO shape test shows the portal model has no agent email property
+  - **09b evidence:** `CustomerTicketPresenterTests` and `TicketPageHostTests`: an agent shows as the API named them, markup in a name is encoded, the customer is "You", and the view model has no email, id or avatar.
 
 
 ## Success Criteria
```

`docs/development/ADMIN-APP.md`

```diff
@@ -316,7 +316,7 @@ scrolls inside its own region (`ScrollRegion`, a named, focusable box, wrapping
 19. The mid-session 401. Type a draft (a reply or a note). Make the API refuse the token, for example by revoking the session in Authentik. Trigger a load. Then: the session-expired banner shows; the draft stays; nothing else saves; "Sign in again" returns to the same page.
 20. Portal. The Portal `/` shows no `Refused to` lines in the console.
 
-**Compose smoke.** `pwsh scripts/Test-ComposeSmoke.ps1` builds the four images one after another, starts the local compose stack under its own project name (`techstrap-smoke`) on free ports, waits for every healthcheck, checks that the Api and the
+**Compose smoke.** `pwsh scripts/Test-ComposeSmoke.ps1` also starts the Portal, checks its `/health/ready` and proves that the Api's rate limit sees the real client address through the Portal (PHASE-09 T18; see [PORTAL-APP.md](PORTAL-APP.md)). It builds the four images one after another, starts the local compose stack under its own project name (`techstrap-smoke`) on free ports, waits for every healthcheck, checks that the Api and the
 Admin answer `/health/ready` with 200 and that the Admin container is healthy, and stops the project (never `down -v`). It needs Docker and runs by hand or from the "Compose smoke" workflow; it is not part of every pull request.
 
 ## Known limits
```


- [ ] **Step 5: Run the tests to see them pass**

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ComposeSmoke.Tests.ps1 -Output Minimal`
Expected: PASS: `Tests Passed: 14, Failed: 0`.

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1 -Output Minimal`
Expected: PASS: `Tests Passed: 32, Failed: 0`.

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal`
Expected: PASS: `Tests Passed: 302, Failed: 0` (the whole Pester suite, node tests included).

- [ ] **Step 6: Run the smoke for real, once**

This needs Docker and builds the four images (several minutes; the first run in the scratch copy built them from cold). It uses its own project name, free ports and never removes a volume; it stops its stack when it ends.

Run: `pwsh -NoProfile -File scripts/Test-ComposeSmoke.ps1`
Expected (as run in the scratch copy):

```text
Starting project techstrap-smoke (this builds the images unless -NoBuild is given)...
Building api...
Building worker...
Building admin...
Building portal...
ok   Api http://127.0.0.1:63485/health/ready -> 200
ok   Admin http://127.0.0.1:63491/health/ready -> 200
ok   Portal http://127.0.0.1:63490/health/ready -> 200
ok   Admin container is healthy
ok   suggest 1 of 3 as 203.0.113.10 -> 200
ok   suggest 2 of 3 as 203.0.113.10 -> 200
ok   suggest 3 of 3 as 203.0.113.10 -> 200
ok   suggest 4 as 203.0.113.10 (over the limit) -> 429
ok   suggest 1 as 203.0.113.11 (another visitor) -> 200
Compose smoke passed.
```

The ports differ on every run. `docker ps --filter name=techstrap-smoke -q` prints nothing afterwards.

- [ ] **Step 7: Prove each pin with a mutation**

Run `git add -A` first.

| # | File | Replace | With | Command (after `--`) | Expected |
| --- | --- | --- | --- | --- | --- |
| 1 | `scripts/Test-ComposeSmoke.ps1` | `$smokePermitLimit = 3` | `$smokePermitLimit = 4` | `PESTER scripts/tests/ComposeSmoke.Tests.ps1` | KILLED, 1 failed of 14 |
| 2 | `scripts/Test-ComposeSmoke.ps1` | `RateLimiting__Public__PermitLimit: "3"` | (nothing) | `PESTER scripts/tests/ComposeSmoke.Tests.ps1` | KILLED, 1 failed of 14 |
| 3 | `scripts/Test-ComposeSmoke.ps1` | `TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${TECHSTRAP_SUBNET:-172.16.31.0/24}` | (nothing) | `PESTER scripts/tests/ComposeSmoke.Tests.ps1` | KILLED, 1 failed of 14 |
| 4 | `scripts/Test-ComposeSmoke.ps1` | `'exec', '-T', 'api', 'curl'` | `'exec', '-T', 'portal', 'curl'` | `PESTER scripts/tests/ComposeSmoke.Tests.ps1` | KILLED, 1 failed of 14 |
| 5 | `scripts/Test-ComposeSmoke.ps1` | `Assert-Ready -Name 'Portal' -Port \$portalPort` | (nothing) | `PESTER scripts/tests/ComposeSmoke.Tests.ps1` | KILLED, 1 failed of 14 |
| 6 | `scripts/Test-ComposeSmoke.ps1` | `Write-Output "docker \$(\$composeArguments -join ' ') port portal 80"` | (nothing) | `PESTER scripts/tests/ComposeSmoke.Tests.ps1` | KILLED, 1 failed of 14 |
| 7 | `docker-compose.yml` | `DataProtection__KeyRingPath: /app/dataprotection-keys\n    depends_on:\n      api:\n        condition: service_healthy\n    volumes:\n      - portal-keys` | `DataProtection__KeyRingPath: /app/dataprotection-keys\n      RateLimiting__Public__PermitLimit: "3"\n    depends_on:\n      api:\n        condition: service_healthy\n    volumes:\n      - portal-keys` | `PESTER scripts/tests/ComposeSmoke.Tests.ps1` | KILLED, 1 failed of 14 |
| 8 | `docs/architecture/04-DECISION-LOG.md` | `As built in 09b: the smoke` | `As built: the smoke` | `PESTER scripts/tests/RepositoryDocs.Tests.ps1` | KILLED, 1 failed of 32 |
| 9 | `docs/architecture/PHASE-09-public-portal.md` | `- [x] **P09-T09**` | `- [ ] **P09-T09**` | `PESTER scripts/tests/RepositoryDocs.Tests.ps1` | KILLED, 1 failed of 32 |
| 10 | `docs/architecture/PHASE-09-public-portal.md` | `- [ ] **P09-T18**` | `- [x] **P09-T18**` | `PESTER scripts/tests/RepositoryDocs.Tests.ps1` | KILLED, 1 failed of 32 |
| 11 | `docs/architecture/99-IMPLEMENTATION-ROADMAP.md` | `09b complete (pending merge); 09c not started` | `09b not started; 09c not started` | `PESTER scripts/tests/RepositoryDocs.Tests.ps1` | KILLED, 1 failed of 32 |
| 12 | `docs/development/PORTAL-APP.md` | `### The lost-link page` | `### Lost link` | `PESTER scripts/tests/RepositoryDocs.Tests.ps1` | KILLED, 1 failed of 32 |
| 14 | `docs/development/ADMIN-APP.md` | `proves that the Api's rate limit sees the real client address through the Portal` | `checks the Portal` | `PESTER scripts/tests/RepositoryDocs.Tests.ps1` | KILLED, 1 failed of 32 |
| 15 | `docs/architecture/PHASE-09-public-portal.md` | `- [x] Contact page with honeypot` | `- [ ] Contact page with honeypot` | `PESTER scripts/tests/RepositoryDocs.Tests.ps1` | KILLED, 1 failed of 32 |
| 16 | `docs/architecture/PHASE-09-public-portal.md` | `- [ ] Typed clients for public product` | `- [x] Typed clients for public product` | `PESTER scripts/tests/RepositoryDocs.Tests.ps1` | KILLED, 1 failed of 32 |

- [ ] **Step 8: Final verification and commit**

Run: `dotnet build TechStrap.slnx -c Release --no-incremental`
Expected: `0 Warning(s)`, `0 Error(s)`.

Run: `dotnet test --solution TechStrap.CI.slnf -c Release`
Expected: PASS: `total: 5963, failed: 0`.

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal`
Expected: PASS: `Tests Passed: 302, Failed: 0`.

```bash
git status --short
git add -A scripts docs
git diff --cached --stat
git commit -F - <<'EOF'
feat(portal): the compose smoke covers the Portal and the real client address; PHASE-09b docs (PHASE-09b)

Test-ComposeSmoke.ps1 starts the Portal, checks its /health/ready and proves, from inside the compose
network, that the Api's rate limit counts the visitor (three 200s then a 429 for one X-Forwarded-For, a 200
for another) through GET /p/{key}/suggest. PORTAL-APP.md describes the customer flows, PHASE-09 ticks what
09b delivers, the roadmap and discovery rows say 09b is complete pending merge, and D-045 records what the
build found.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
EOF
```

After this commit the branch is ready for the whole-branch review. Do not open the pull request without the owner's OK.
