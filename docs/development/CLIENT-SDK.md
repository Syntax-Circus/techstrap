# Client SDK

`TechStrap.Client` is the NuGet package a company app uses to submit a ticket to TechStrap with an API key. It is a thin typed client over `POST /api/intake/tickets`
and the DTOs in `TechStrap.Contracts`. This page is for people who maintain the SDK: how it is built, how it retries, how to pack and test it. What the endpoint
itself accepts is in [INTAKE.md](INTAKE.md). The decisions behind the SDK are in D-047 in [04-DECISION-LOG.md](../architecture/04-DECISION-LOG.md), and the phase
page is [PHASE-11-client-sdk.md](../architecture/PHASE-11-client-sdk.md).

PHASE-11 is delivered in three pull requests. **11a** (this page describes it): `TechStrap.Contracts` and `TechStrap.Client` as packable projects, the real-API
tests, the pack dry run in CI. **11b**: `TechStrap.Client.Maui` (device and app metadata, a submit helper; see the section [TechStrap.Client.Maui](#techstrapclientmaui)
below, D-048). **11c**: per-package READMEs, samples, the publish workflow, nuget.org and `v1.0.0-rc.1`. Nothing is published to nuget.org yet.

## Packages

| Package | What it is | Dependencies |
| --- | --- | --- |
| `TechStrap.Contracts` | The public DTOs, header names, limits and routes the API and the SDK share (`SubmitTicketRequest`, `SubmitTicketResponse`, `HeaderNames`, `IntakeLimits`, `IntakeRoutes`). | None. |
| `TechStrap.Client` | `ITechStrapClient`, `TechStrapClientOptions`, `AddTechStrapClient`, `ApiKeyHandler`, the `Result` mapping. | `TechStrap.Contracts`, `SyntaxCircus.Http.Resilience`, `SyntaxCircus.Common`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.DependencyInjection.Abstractions`. |
| `TechStrap.Client.Maui` | `IMauiTicketSubmitter`, `MauiTicketDraft`, `IDeviceContextCollector`, `AddTechStrapMaui` (11b). | `TechStrap.Client`, `TechStrap.Contracts`, `Microsoft.Maui.Essentials`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`. |

All three packages target `net10.0` and carry their pack metadata from `eng/Packaging.props` (author, MIT license, repository URL, symbol package, package validation).
The root `Directory.Build.props` sets `IsPackable=false`, so no other project is ever packed by accident. `SyntaxCircus.Common` is web-neutral since 0.2.0: a consumer
inherits no `Microsoft.AspNetCore.App` framework reference. The set of dependencies is pinned by an architecture rule (`ClientRules`), so adding one is a deliberate edit.

The submit takes JSON only. There is no attachment type: the intake endpoint is JSON-only (D-034), and attachments wait for multipart intake (11d).

## Local pack

GitVersion is not wired in yet (11c), so a pack needs an explicit version. Publishing to nuget.org (a `v*` tag, Trusted Publishing, rollback) is described in [RELEASING.md](RELEASING.md).

```
dotnet pack src/TechStrap.Contracts -c Release -p:Version=0.0.0-local -o ./pack
dotnet pack src/TechStrap.Client -c Release -p:Version=0.0.0-local -o ./pack
dotnet pack src/TechStrap.Client.Maui -c Release -p:Version=0.0.0-local -o ./pack
pwsh -File scripts/Test-PackageContents.ps1 -PackageDirectory ./pack -Expected @{
    'TechStrap.Contracts' = @()
    'TechStrap.Client'    = @('TechStrap.Contracts', 'SyntaxCircus.Http.Resilience', 'SyntaxCircus.Common', 'Microsoft.Extensions.Http', 'Microsoft.Extensions.Options', 'Microsoft.Extensions.DependencyInjection.Abstractions')
    'TechStrap.Client.Maui' = @('TechStrap.Client', 'TechStrap.Contracts', 'Microsoft.Maui.Essentials', 'Microsoft.Extensions.DependencyInjection.Abstractions', 'Microsoft.Extensions.Options')
}
```

Each package carries its XML documentation file (`lib/net10.0/<id>.xml`), so the doc comments show up in IntelliSense; `Test-PackageContents.ps1` fails when it is missing.

`Test-PackageContents.ps1` fails (exit 1, one line per defect, as `id: defect`) when a package is missing, when `README.md` is not at the package root and named in the
nuspec, when the license is not the MIT expression, when the repository URL is missing, when the dependency ids differ from the expected set, or when the `.snupkg` or the XML documentation file
is missing. CI runs the same three packs with `-p:Version=0.0.0-ci` after the tests (the "Pack dry run" step of `ci.yml`). Keep the `./pack` folder out of git.

## Configuration

Register the client once. There are two overloads, and both end in the same registration:

```csharp
// In code
services.AddTechStrapClient(options =>
{
    options.BaseAddress = new Uri("https://support.example.com/");
    options.ApiKey = apiKey;
});

// From configuration: pass the section that holds the six keys (by convention "TechStrap")
services.AddTechStrapClient(configuration.GetSection("TechStrap"));
```

| Key | Default | Meaning |
| --- | --- | --- |
| `BaseAddress` | none | The TechStrap API address. Absolute http or https; no user info, query or fragment. Plain `http` is accepted only for loopback (`localhost`, `127.0.0.1`, `[::1]`). A path is kept: `https://host/support` and `https://host/support/` both send to `https://host/support/api/intake/tickets`. |
| `ApiKey` | none | The product's API key. Not blank, and safe to put in a header. |
| `Timeout` | `00:00:30` | The total budget of one call: every attempt and every delay between them. At most 10 minutes. |
| `MaxAttempts` | `3` | Attempts in total, the first included. `1` means never retry. 1 to 10. |
| `RetryBaseDelay` | `00:00:00.500` | The first backoff delay; later delays grow from it. |
| `MaxRetryDelay` | `00:00:05` | The longest single delay. Not below `RetryBaseDelay`. |

Bad values fail loudly. The options validator throws `OptionsValidationException` the first time the client is used (there is no `ValidateOnStart`, because a MAUI
app has no generic host), and its message names the key and never contains the API key. The configuration overload fails at registration, naming the key, when `BaseAddress` is not an absolute URL, and
also when `Timeout`, `MaxAttempts`, `RetryBaseDelay` or `MaxRetryDelay` does not parse. A second `AddTechStrapClient` call changes
nothing. `ITechStrapClient` and `TimeProvider` are added with `TryAddSingleton`, so a host that registered its own earlier (a fake client in a test, a fake clock) wins.

## Retry and idempotency

Creating a ticket is not naturally idempotent, so the SDK retries a submit only when the server can recognise the repeat. The server keeps an `Idempotency-Key`
per API key and answers a repeat with the original ticket (D-020).

The SDK owns one `HttpRequestResiliencePipeline("techstrap-submit")` and does not use `AddResilientHttpClient`, which would retry every POST. Each send passes a
replay flag:

| Method | Key | Replay safety | Sends |
| --- | --- | --- | --- |
| `SubmitTicketAsync(request, ct)` | A generated GUID in "N" format | `Replayable` | Up to `MaxAttempts`; a fresh request and the identical `Idempotency-Key` on every attempt. |
| `SubmitTicketAsync(request, idempotencyKey, ct)` | The caller's | `Replayable` | The same as above. |
| `SubmitTicketOnceAsync(request, ct)` | None | `NotReplayable` | Exactly one. |

A supplied key (the second overload) must be non-blank, visible ASCII and at most `IntakeLimits.MaxIdempotencyKeyLength` characters, else the call throws `ArgumentException` (a programmer
error, not a `Result`).

**If you retry a failed submit yourself, supply your own stable key.** The generated key is not returned, so a second call to the overload without a key uses a new
key and can create a duplicate ticket. Keep one key per logical submission and reuse it on every retry. `api-unavailable` means the ticket may or may not have been
created (the response can be lost after the server stored the ticket); retry with the same key and the server returns the first ticket. `rate-limited` means retry later
with the same key.

- **Retried:** transport errors, timeouts, and the statuses 408, 502, 503 and 504.
- **Not retried:** 429 and 500. A 500 is the API's own answer. A 429 is returned as `rate-limited`: the server does send `Retry-After`, but `ResultError` has no slot to carry it
  and the pipeline cannot honour it per response. A caller who wants to wait and retry does so on the `Result` itself.
- **Budget:** `Timeout` is one deadline over all sends and all backoff delays. When it runs out the call ends as `api-unavailable`. A cancellation by the caller always wins and
  propagates as `OperationCanceledException`.
- **Circuit:** the circuit breaker counts logical calls, not attempts. Its constants are in the internal `ResilienceDefaults`: failure ratio 0.5, minimum throughput 5, sampling
  duration 30 s, break duration 30 s (the package defaults, made explicit). It opens when at least half of the last 5+ calls in a 30 s window failed (the package defaults) and stays open 30 s; while it is open a call returns `api-unavailable` without a send;
  after the break it lets a call through. It applies to `SubmitTicketOnceAsync` too.
- **One circuit per DI container (per `TechStrapClient` singleton).** The pipeline is built once, lazily, under a lock, and shared by every caller that resolves the same `ITechStrapClient`. A failing API opens the
  circuit for all of them.
- **Server retention.** The server keeps a key for 24 hours (D-020). A key replayed after that creates one new ticket. A caller that supplies its own key and reuses it for
  longer than the retention window gets a duplicate, not the first ticket.

## Error codes

Expected failures are `Result` failures, never exceptions. The codes are constants in `TechStrapClientErrorCodes` (in the Client, not in Contracts, because they never
cross the wire):

| Code | When |
| --- | --- |
| `invalid-api-key` | 401 or 403. |
| `validation-failed` | 400 or 422 with no per-field codes. The field errors, when there are some, come through as validation errors on their field; the wire codes pass through unchanged. |
| `payload-too-large` | 413. |
| `unsupported-media-type` | 415. |
| `rate-limited` | 429. Not retried; no `Retry-After`. |
| `api-unavailable` | 408 (after the retries) or any 5xx (500 is never retried; 502/503/504 after the retries), transport failure, timeout, open circuit. |
| `api-unexpected-response` | A 2xx with no readable body or a blank ticket number. |
| `api-error` | Any other status. |

Server text reaches the `Result` only for a 400 or 422 (its detail and field messages); every other failure has a fixed message.

## Key safety

The API key travels in the `X-Api-Key` header only. It is never in a URL, a log, an exception message, a validation message or a `ToString()`.

- The named client (`"TechStrap"`) has `RemoveAllLoggers()`, so no logging handler sees the request, and the client itself does not log.
- The primary handler is `SocketsHttpHandler { AllowAutoRedirect = false }`: a redirect would replay the key to another host. The response buffer is capped at 1 MiB.
- `ApiKeyHandler` sets the header and replaces a value the caller set. It refuses a request whose scheme, host and port differ from `BaseAddress` (`InvalidOperationException`, nothing is sent; a default port equals no port, and the message never prints user info).
- `TechStrapClientOptions` is a sealed class, and its `ToString()` prints the scheme, host and port of `BaseAddress` only (never user info).
- Handlers a host adds to every client with `ConfigureHttpClientDefaults` (the Aspire ServiceDefaults template adds a standard resilience handler) are removed from the SDK's
  client. Such a handler would sit outside `ApiKeyHandler`, retry a call that has no `Idempotency-Key` (and a 500 or 429 the SDK does not retry) and could see the key header.
  `AddTechStrapClient` drops every handler that comes before `ApiKeyHandler` in the named client's chain. To add a handler of your own, register it on the named client after
  `AddTechStrapClient`: `services.AddHttpClient(TechStrapClientDefaults.HttpClientName).AddHttpMessageHandler(...)`. Handlers registered there afterwards are kept. A handler registered on the named client before `AddTechStrapClient`, or added by an `IHttpMessageHandlerBuilderFilter` or service discovery, is removed too: register yours after.
- Use a Trusted key only from server-side code. A Public key is extractable from an app by design; the server marks its metadata untrusted and ignores `ExternalUserRef`.
- The wire literals (`"X-Api-Key"`, `"X-Ticket-Token"`, `"Idempotency-Key"`, `"api/intake/tickets"` and the leading-slash `"/api/intake/tickets"`) exist in `src/` only in `HeaderNames.cs` and `IntakeRoutes.cs` (and the Sentry
  header scrubber); the architecture tests (`WireLiteralRules`) fail otherwise.

## Running the sample

`samples/TechStrap.Client.Samples.Console` is the SDK-based counterpart of `scripts/Send-TestTicket.ps1` (which does the same over raw HTTP). It submits one ticket through `ITechStrapClient` and prints the ticket number and view URL. Its three code blocks are the ones in the `TechStrap.Client` README, and a Pester test keeps them identical (`scripts/tests/ReadmeSnippets.Tests.ps1`).

1. Start the local stack with the development seed data: `TECHSTRAP_SEED_DEV_DATA=true docker compose up -d` (the API listens on `http://localhost:8080`).
2. Take a development Trusted key from the "Dev API keys" table in [DEV-DATA.md](DEV-DATA.md). It is a fake key that works only against a seeded development database; the sample never embeds a key.
3. Run it:

```
dotnet run --project samples/TechStrap.Client.Samples.Console -- --base-address http://localhost:8080 --api-key <key>
```

The address and key can also come from `appsettings.json` (section `TechStrap`) or from the environment variables `TECHSTRAP__BASEADDRESS` and `TECHSTRAP__APIKEY`; the switches win.

| Exit code | Meaning |
| --- | --- |
| `0` | The ticket was created; the output is `Ticket <number>` and `View: <url>`. |
| `1` | The SDK reported a failure; each error is printed as `<code>: <message>` (for example `invalid-api-key: ...`). |
| `2` | The configuration is missing or invalid (`OptionsValidationException`); the failures are printed. |

Anything else is a bug and crashes the process visibly.

## Tests

`tests/TechStrap.Client.Tests` has two kinds of test.

- **Unit tests** do not themselves use Docker (but see below: the project needs Docker to run at all today). They use a scripted handler and `FakeTimeProvider`: options and validator, the handler, registration, the status mapping, the retry rules (the
  same key on every attempt; 429, 500 and 400 not retried; `SubmitTicketOnceAsync` sends once), the budget, the circuit, cancellation and the request shape.
- **Integration tests** carry `[Trait("Integration","Docker")]` and need Docker running. `ClientApiFactory` hosts `src/TechStrap.Api` over Postgres (Testcontainers) and the SDK
  talks to it: Trusted and Public key submits with SQL assertions, revoked, unknown and deactivated keys, a validation error, a 413 over Kestrel, the rate limit, the
  lost-response retry proof (one ticket row, the same ticket number) and the OpenAPI contract test. The project references `src/TechStrap.Api` and links a few `Api.Tests`
  helpers, because `Api.Tests` is an executable with an assembly fixture.

```
dotnet test --project tests/TechStrap.Client.Tests -c Release
dotnet test --project tests/TechStrap.Client.Tests -c Release --filter-not-trait "Integration=Docker"
dotnet test --project tests/TechStrap.Client.Tests -c Release --filter-trait "Integration=Docker"
```

Docker is required for any run of this project today: the linked `TestPostgres` carries an assembly fixture that xUnit starts eagerly, so even the second command starts Postgres. The first runs everything; the second skips only the Docker-tagged classes; the third runs the Docker tests alone. The Pester pins for the docs are in
`scripts/tests/RepositoryDocs.Tests.ps1` and for the pack check in `scripts/tests/Test-PackageContents.Tests.ps1` (`pwsh -File scripts/Invoke-ScriptTests.ps1`).

## Known limits

- A 429 is surfaced as `rate-limited` and not retried, and `Retry-After` is not carried (the server sends it; `ResultError` has no slot). Follow-up: a slot in `SyntaxCircus.Common`.
- The Api documents no response schemas, so the OpenAPI contract test cannot pin the 201 `SubmitTicketResponse` schema; the real-API tests pin the response shape. A PHASE-05 follow-up.
- One circuit per DI container (per `TechStrapClient` singleton): a failing API opens it for every caller that shares that client.
- `net10.0` only; multi-targeting is a post-1.0 question.
- AOT-compatible: serialization is source-generated (`TechStrapJsonContext`).
- No attachments (P11-T05): deferred to 11d, which first needs multipart intake.
- GitVersion and a SourceLink package are deferred to 11c, so a local pack needs `-p:Version=`.
- An idempotency key older than the server's retention window (24 hours) no longer protects a retry: replaying it creates one new ticket.
- Docker is needed for every run of `TechStrap.Client.Tests`, not only the Docker-tagged classes (the eagerly started assembly fixture). A lazily started fixture used by the Docker-tagged classes only is a follow-up.
- Interface additions before 1.0 are breaking for implementers of `ITechStrapClient` (fakes in consumers' tests); there are no default interface methods.
- The generated idempotency key is not returned to the caller. Exposing it (so a caller can retry a no-key submit safely) is deferred to the 11c design.

## TechStrap.Client.Maui

`TechStrap.Client.Maui` (11b, D-048) adds two things on top of `TechStrap.Client`: a collector that reads device and app facts through the MAUI Essentials
interfaces, and a submit helper that merges them into the ticket's `metadata`. It targets plain `net10.0` and depends on `Microsoft.Maui.Essentials`, not on
`Microsoft.Maui.Controls`: no workload, no `UseMaui`, no platform target frameworks, no macOS runner. An architecture rule (`ClientMauiRules`) pins the package set, allows
project references to Client and Contracts only and the architecture tests fail if the csproj sets `<UseMaui>`.

### Registration

`AddTechStrapMaui` has two overloads. Use the first when `AddTechStrapClient` is already called, the second to register both at once:

```csharp
// The client is registered separately (AddTechStrapClient must come first)
services.AddTechStrapClient(options => { options.BaseAddress = baseAddress; options.ApiKey = apiKey; });
services.AddTechStrapMaui(device => device.IncludeBattery = true);

// Both in one call
services.AddTechStrapMaui(
    client => { client.BaseAddress = baseAddress; client.ApiKey = apiKey; },
    device => device.IncludeDisplay = true);

// In a page or view model
var draft = new MauiTicketDraft("Crash on save", "It closes when I tap Save.", "ana@example.com",
    RequesterName: "Ana", Metadata: new Dictionary<string, string> { ["plan"] = "pro" });
Result<SubmitTicketResponse> result = await submitter.SubmitAsync(draft, cancellationToken);
```

Resolving `IMauiTicketSubmitter` without `AddTechStrapClient` throws `InvalidOperationException` naming `AddTechStrapClient`; resolution is lazy, so the order of the two calls does not matter. The Essentials defaults (`AppInfo.Current`,
`DeviceInfo.Current`, `Connectivity.Current`, `DeviceDisplay.Current`, `Battery.Default`) are registered with `TryAddSingleton` as factories: nothing reads them at
registration (on plain `net10.0` `DeviceInfo.Current.Model` throws), an app that registered its own `IDeviceInfo` and the like wins, and service registrations are idempotent (configure delegates stack like any Options configure).

### What is collected

The keys are the constants in `TicketMetadataKeys` (Contracts). `Defaults` are collected when `IncludeDeviceContext` is true (the default); the extras only when the
matching option is switched on.

| Key | Source | Default or extra |
| --- | --- | --- |
| `app.name` | `IAppInfo.Name` | Default |
| `app.version` | `IAppInfo.VersionString` | Default |
| `app.build` | `IAppInfo.BuildString` | Default |
| `app.package` | `IAppInfo.PackageName` | Default |
| `os.platform` | `IDeviceInfo.Platform` | Default |
| `os.version` | `IDeviceInfo.VersionString` | Default |
| `device.manufacturer` | `IDeviceInfo.Manufacturer` | Default |
| `device.model` | `IDeviceInfo.Model` | Default |
| `device.idiom` | `IDeviceInfo.Idiom` | Default |
| `device.type` | `IDeviceInfo.DeviceType` | Default |
| `locale` | Current culture name | Default |
| `timezone` | Local time zone id | Default |
| `network.access` | `IConnectivity.NetworkAccess` (Android needs the `ACCESS_NETWORK_STATE` permission) | Default |
| `display.width` | `IDeviceDisplay.MainDisplayInfo` | Extra (`IncludeDisplay`) |
| `display.height` | `IDeviceDisplay.MainDisplayInfo` | Extra (`IncludeDisplay`) |
| `display.density` | `IDeviceDisplay.MainDisplayInfo`, invariant culture, for example "2.625" | Extra (`IncludeDisplay`) |
| `display.orientation` | `IDeviceDisplay.MainDisplayInfo` | Extra (`IncludeDisplay`) |
| `battery.state` | `IBattery.State` (Android needs `BATTERY_STATS`) | Extra (`IncludeBattery`) |
| `battery.level` | `IBattery.ChargeLevel` as a rounded whole percent; left out when the level is negative (Android needs `BATTERY_STATS`) | Extra (`IncludeBattery`) |

Each field is read in its own try/catch. A field whose accessor throws is skipped (the package does no logging), so `Collect()` never throws. A missing Android permission (`BATTERY_STATS`, `ACCESS_NETWORK_STATE`) skips its field silently. Values are trimmed, a blank
value is dropped, a value is cut to `IntakeLimits.MaxMetadataValueLength` and trimmed again.

### Privacy and redaction

The helper never collects advertising or device ids, location, contacts, an IP address or user names. The set above is the whole set, and `IncludeDeviceContext = false`
turns it all off. The metadata is sent under the product's API key like any other metadata: from a Public key it is stored but flagged untrusted (D-001), so treat it as a
hint, not as proof.

`DeviceContextOptions.Redact` is a `Func<string, string, string?>` called with the key and the value after the trimming and truncation. It is applied to each collected device-context value only; the draft's own `Metadata` is not passed through it. Return a changed value to
replace it, or `null` to drop the field. A redactor that throws drops only that field, and its result is validated again (blank is dropped, too long is cut).

### Merge rule and local limits

`SubmitAsync(MauiTicketDraft, CancellationToken)` takes the draft (`Subject`, `Message`, `RequesterEmail`, optional `RequesterName`, `Metadata` and `IdempotencyKey`) and
sends one `SubmitTicketRequest`:
- The collected keys and every key in `TicketMetadataKeys.All` are reserved. An app value for a reserved key is ignored; the collected value (or none) is sent.
- A blank key, or one over 64 characters, is a failure. A blank value is skipped. A value is cut to 1000 characters. The same checks apply to collected entries, so a custom `IDeviceContextCollector` cannot break the "nothing is sent" promise.
- The collected keys (up to 19) count toward the 50, so an app can rely on 31 of its own.
- More than 50 keys, or more than 16,000 characters when serialized (`JsonSerializerDefaults.Web`), is a local failure with the code `metadata-invalid`
  (`TechStrapMauiErrorCodes.MetadataInvalid`, a validation error on target `metadata`). Nothing is sent. The code repeats the server's wire string on purpose.
- With no `IdempotencyKey` the helper calls the unkeyed `SubmitTicketAsync`, which generates one; with a key it calls the keyed overload. A caller that retries a failed
  submit itself must supply a stable key (the same rule as `TechStrap.Client`).
- The client's `Result<SubmitTicketResponse>` is returned unchanged. A null draft throws `ArgumentNullException`, a blank, non-ASCII or over-200-character `IdempotencyKey` throws `ArgumentException`, and cancellation throws `OperationCanceledException`.

### Tests

`tests/TechStrap.Client.Maui.Tests` is a plain `net10.0` test project (52 tests) that fakes the Essentials interfaces with NSubstitute. It needs no device and no Docker:

```
dotnet test --project tests/TechStrap.Client.Maui.Tests -c Release
```

### Known limits

- Consumers need MAUI >= 10.0.0; the package pins Essentials at 10.0.0, the lowest version with every API the helper uses, so it forces no patch upgrade of Core, Graphics or WindowsAppSDK.
- A field that fails to read is skipped silently (the package does no logging).
- A caller who retries a failed submit must supply a stable `IdempotencyKey`, or a retry can create a second ticket.
- On iOS, display values may be missing when `SubmitAsync` runs off the UI thread (the UIKit thread check). `IncludeDisplay` is opt-in and a failure never crashes.
- AOT-compatible: serialization is source-generated (`TechStrapJsonContext`).
- No screenshot and no attachments until 11d, which first needs multipart intake.
- No platform target frameworks. Platform-specific code would need them later; adding them is non-breaking.
- `MainDisplayInfo` is read per field, so a rotation between reads can mix values.
- Metadata from a Public key is untrusted on the server (D-001).
