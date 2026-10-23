# PHASE-11d: SDK attachments

Status: Merged (PR #40; implemented and verified 2026-10-16). Decision: D-055.

## Outcome

API-key clients can attach files to a new ticket. JSON intake stays unchanged. Both formats use the same handler, key permissions, rate-limit partition, response and idempotency namespace. This closes P11-T05's D-047 deferral.

## Interfaces

- `POST api/intake/tickets/with-attachments`: multipart fields corresponding to `SubmitTicketRequest`, JSON-encoded `metadata`, repeated `attachments` files. Uses `SubmitTicketRequestHandler` with an API context built from the authenticated principal.
- `TicketAttachment` in Client: filename, declared content type, optional byte length, cancellation-aware factory returning a fresh SDK-owned stream per attempt. `FromFile` reopens a local file.
- Three `ITechStrapClient` attachment overloads: generated key, caller key, send once. Existing signatures remain. Empty lists use JSON.
- Optional non-positional `MauiTicketDraft.Attachments` property and `FileResult.ToTicketAttachment` adapter. Apps own selection and screenshot capture. No new package, platform target, persistence model or migration.

## Guarantees and validation

Existing limits apply: five files, 10 MiB per file, 25 MiB combined, 26 MiB request body. Clients precheck known lengths/counts/extensions and bound actual bytes while streaming. The server checks signatures and MIME agreement. Each attempt reopens files; every stream is disposed. Local read/close failures are fixed Result errors, not retryable transport errors. Caller cancellation propagates and total timeout includes uploads.

The same idempotency key never appends files to an earlier ticket. Concurrent losing attempts delete stored files and look up the winning record in a fresh transaction, without rereading consumed streams. Replay adds no confirmation or webhook event. A duplicate without a winning idempotency record remains a conflict.

Coverage includes trust/classification, shared rate limits, malformed requests, signature failures and cleanup, request size, concurrent replays, lost responses, real-socket/nonseekable uploads, local failures, byte budgets and MAUI forwarding. Actual `FileResult.OpenReadAsync` needs a smoke test in a platform MAUI app; portable Essentials deliberately does not implement file opening.

## Remaining release work

Deploy an attachment-capable server before enabling the new SDK calls. Older servers produce a failure; files are never silently dropped. Publish through the existing tag workflow. PHASE-12c UAT/load/restore/soak and the later 1.0 API lock remain separate.

## Verification record (2026-10-16)

Release CI solution build: zero warnings/errors. All 8,791 .NET tests and 521 script checks passed. All four Linux amd64 images built locally. Three package dry runs and their contents/dependency checks passed; 44 pinned package versions match; direct/transitive vulnerability audit reports zero findings/problems; EF reports no pending model changes. Independent review found one source-exception classification issue, reproduced with failing tests and fixed; final full suite passed. Cancellation and timeout tests observe stream disposal without assuming canceled serialization unwinds before the hard timeout returns. Trivy image scanning remains a CI check; actual MAUI file opening and PHASE-12c UAT remain manual follow-ups. No tag, publish or deployment was performed.
