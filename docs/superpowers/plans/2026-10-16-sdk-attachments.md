# PHASE-11d: SDK attachments

Approved in conversation: separate API-key multipart route, .NET attachment factories, MAUI FileResult adapter; apps own selection and capture.

## Tasks
- [x] API: multipart form at `api/intake/tickets/with-attachments`, existing handler/storage, replay-only duplicate recovery and endpoint tests.
- [x] SDK: TicketAttachment with reopening factories and local-file helper; three submission overloads, bounded streaming, safe errors and retry tests.
- [x] MAUI: non-positional draft attachment property and FileResult adapter, preserving metadata behavior.
- [x] Docs and verification: decision log, architecture catalog, guides/examples, Release build/tests/scripts/package dry runs/EF drift and independent review.

## Constraints and review focus
Keep existing JSON APIs and limits. Existing authentication, rate limits, validation, idempotency namespace and 201 response apply. No migration or new dependency. Check stream disposal on every exit, cancellation, local-read failures, unknown sizes, duplicate storage cleanup and no duplicate notification on replay. Do not retry local file errors or silently drop attachments.

## Results
Release build clean; 8,791 .NET tests and 521 script tests passed. Four amd64 container builds, NuGet vulnerability audit, package dry runs/contents, central version check and EF drift check passed. Independent review issue fixed with RED/GREEN regression tests and a green full suite. MAUI platform opening and release/UAT remain documented follow-ups; no tag or deploy.
