# TechStrap.Contracts

The wire contracts of TechStrap, the self-hosted support desk: the request and response types, header names, route constants and size limits that the intake API and its clients share. It has no dependencies, so any .NET client can reference it.

## What it contains

- `SubmitTicketRequest` and `SubmitTicketResponse`, the JSON bodies of `POST /api/intake/tickets`.
- `HeaderNames`: the `X-Api-Key`, `X-Ticket-Token` and `Idempotency-Key` header names.
- `IntakeRoutes`: the intake route constants.
- `IntakeLimits`: the size limits the API enforces (subject, body, metadata keys and values, idempotency key length).
- `TicketMetadataKeys`: the well-known metadata keys (`app.version`, `os.platform` and the rest) that device-aware clients fill in.

## Stability

The package follows semantic versioning, scoped as follows. `v1.0.0` locks the SDK-facing surface of `TechStrap.Contracts`: the `TechStrap.Contracts.Intake` namespace (`SubmitTicketRequest`, `SubmitTicketResponse`, `IntakeLimits`, `IntakeRoutes`, `IntakeWarnings`, `TicketMetadataKeys`) and `TechStrap.Contracts.Http.HeaderNames`. The other namespaces (Admin, Agents, ApiKeys, Kb, Live, AdminEvents, Tickets and so on) are TechStrap's own app wire shapes, shared with its Admin and Portal, and may change in minor versions. Before 1.0.0, a release candidate can still change the SDK-facing surface; after it, a breaking change there ships only in a new major version. Package validation is enabled; its baseline comparison against the previous release starts after 1.0.0.

## Using it

You normally get this package through TechStrap.Client, which wraps these contracts in a typed HTTP client with retries and typed errors. Reference TechStrap.Contracts directly only if you write your own client.

More: https://github.com/Syntax-Circus/techstrap/blob/main/docs/development/CLIENT-SDK.md

Source and issues: https://github.com/Syntax-Circus/techstrap
