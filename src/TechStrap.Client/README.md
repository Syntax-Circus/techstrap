# TechStrap.Client

A small .NET client for the TechStrap support desk: submit a support ticket from your own app over the intake API, with an API key, retries and typed errors.

## Install

    dotnet add package TechStrap.Client

## Register

Register the client once. `AddTechStrapClient` reads the section `TechStrap` (keys `BaseAddress` and `ApiKey`, plus the optional ones listed under Configuration keys), or takes a delegate.

```csharp
builder.Services.AddTechStrapClient(builder.Configuration.GetSection(TechStrapClientDefaults.ConfigurationSection));
```

## Submit

Resolve `ITechStrapClient` and send a `SubmitTicketRequest`. Supply a stable idempotency key so a retry never creates a second ticket.

```csharp
var request = new SubmitTicketRequest(
    Email: "ana@example.com",
    Name: "Ana",
    Subject: "Printer on floor 2 is offline",
    Body: "It stopped answering this morning.",
    ExternalUserRef: null,
    Metadata: null);

// Keep this key and reuse it if you retry this ticket; a new key on a retry can create a duplicate.
Result<SubmitTicketResponse> result = await client.SubmitTicketAsync(request, Guid.NewGuid().ToString("N"));
```

## Handling results

Expected failures are `Result` failures, never exceptions. Check `IsSuccess`, then read `Value` or the `Errors`.

```csharp
if (result.IsSuccess)
{
    Console.WriteLine($"Ticket {result.Value.TicketNumber}");
    Console.WriteLine($"View: {result.Value.ViewUrl}");
    return 0;
}

foreach (var error in result.Errors)
{
    Console.Error.WriteLine($"{error.Code}: {error.Message}");
}

return 1;
```

The error codes are constants in `TechStrapClientErrorCodes`:

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

Server text reaches the `Result` only for a 400 or 422 (its detail and field messages); every other failure has a fixed message. Invalid options are the exception: `OptionsValidationException` is thrown the first time the client is used, and its message names the key and never contains the API key.

## Keys

Use a Trusted key only from server-side code you control. A Public key is meant for apps you ship to customers: it can be extracted from the app by design, so the server marks the metadata it sends as untrusted and ignores `ExternalUserRef`. Never embed a Trusted key in an app. The key travels in the `X-Api-Key` header only and is never logged or put in an exception message.

## Retries and idempotency

A submit is retried only when the server can recognize the repeat: the SDK sends the same `Idempotency-Key` on every attempt (transport errors, timeouts and the statuses 408, 502, 503 and 504 are retried, up to `MaxAttempts`). The server keeps a key for 24 hours.

- If you retry a failed submit yourself, supply your own stable key and reuse it. The overload without a key generates one and does not return it, so a second call can create a duplicate ticket.
- `api-unavailable` means the ticket may or may not have been created (the response can be lost after the server stored it). Retry with the same key and the server returns the first ticket.
- `rate-limited` means retry later with the same key.
- `SubmitTicketOnceAsync` sends exactly once and never retries.

## Configuration keys

| Key | Default | Meaning |
| --- | --- | --- |
| `BaseAddress` | none | The TechStrap API address. Absolute http or https; plain `http` only for loopback (`localhost`, `127.0.0.1`, `[::1]`). |
| `ApiKey` | none | The product's API key. Not blank. |
| `Timeout` | `00:00:30` | The total budget of one call: every attempt and every delay between them. At most 10 minutes. |
| `MaxAttempts` | `3` | Attempts in total, the first included. `1` means never retry. 1 to 10. |
| `RetryBaseDelay` | `00:00:00.500` | The first backoff delay; later delays grow from it. |
| `MaxRetryDelay` | `00:00:05` | The longest single delay. Not below `RetryBaseDelay`. |

## Compatibility

Targets `net10.0`. The package is AOT-compatible (serialization is source-generated). It is versioned in lockstep with TechStrap.Contracts. Attachments require a server containing PHASE-11d (D-055); existing JSON calls work with older servers.

More: https://github.com/Syntax-Circus/techstrap/blob/main/docs/development/CLIENT-SDK.md

Source and issues: https://github.com/Syntax-Circus/techstrap

Set `Type: TicketTypes.FeatureRequest` on `SubmitTicketRequest` to submit a feature request (`TechStrap.Contracts.Tickets`). Omit it for Support. Existing callers remain compatible; unknown values are rejected by the server. The MAUI helper accepts the same optional `Type` on `MauiTicketDraft`.

## Attach files

```csharp
var attachments = new[] { TicketAttachment.FromFile("trace.log", "text/plain") };
// Persist this key for this logical ticket, including later caller retries.
var submissionKey = Guid.NewGuid().ToString("N");
var uploaded = await client.SubmitTicketAsync(request, attachments, submissionKey, ct);
```

Attachment overloads also support a generated key and `SubmitTicketOnceAsync`. Empty lists use JSON. Limits: five files, 10 MiB each, 25 MiB combined; the server validates types and signatures. Keep source files unchanged until retries finish.

Custom sources use `TicketAttachment(fileName, contentType, openReadAsync, length: optionalLength)`. Return a **fresh readable stream at its beginning on every attempt**. The SDK disposes it on success, cancellation and failure. Nonseekable streams and unknown lengths work; actual bytes are bounded. Never return a shared stream.

Local errors target `attachments`. Match the constants on `TechStrapClientErrorCodes`: `AttachmentsTooMany`, `AttachmentsTooLarge`, `AttachmentTooLarge`, `AttachmentTypeNotAllowed`, `AttachmentNameInvalid`, `AttachmentContentTypeInvalid` and `AttachmentSizeInvalid`. `AttachmentReadFailed` means a source could not be opened, read or closed; paths are never returned and the SDK does not retry it. Creation can be uncertain after sending begins: reuse the same submission key. Cancellation still throws; the total timeout includes file operations.

Deploy the attachment-capable server first. An older server returns failure; files are never silently dropped.
