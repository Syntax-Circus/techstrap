# TechStrap.Contracts

The wire contracts of TechStrap, the self-hosted support desk: the request and response types, header names, route constants and size limits that the intake API and its clients share. It has no dependencies, so any .NET client can reference it.

## What it contains

- `SubmitTicketRequest` and `SubmitTicketResponse`, the JSON bodies of `POST /api/intake/tickets`.
- `HeaderNames`: the `X-Api-Key`, `X-Ticket-Token` and `Idempotency-Key` header names.
- `IntakeRoutes`: the intake route constants.
- `IntakeLimits`: the size limits the API enforces (subject, body, metadata keys and values, idempotency key length).
- `TicketMetadataKeys`: the well-known metadata keys (`app.version`, `os.platform` and the rest) that device-aware clients fill in.

## Stability

The package follows semantic versioning, scoped as follows. `v1.0.0` locks the SDK-facing surface of `TechStrap.Contracts`: the `TechStrap.Contracts.Intake` namespace (`SubmitTicketRequest`, `SubmitTicketResponse`, `IntakeLimits`, `IntakeRoutes`, `IntakeWarnings`, `TicketMetadataKeys`) and `TechStrap.Contracts.Http.HeaderNames`. The other namespaces (Admin, Agents, ApiKeys, Kb, Live, AdminEvents, Tickets and so on) are TechStrap's own app wire shapes, shared with its Admin and Portal, and may change in minor versions. Before 1.0.0 (the 0.x versions, starting with 0.1.0), the SDK-facing surface can still change in a minor version; after it, a breaking change there ships only in a new major version. Package validation is enabled; its baseline comparison against the previous release starts after 1.0.0. Changes since 0.1.0 are additive in source; the 0.2.0 and 0.3.0 notes below name the binary breaks (positional parameters) that a recompile resolves.

## Version notes

### 0.3.0

Trailing optional parameters were added to the positional records `ProductBrandingDto` (`Tagline`, `UploadedLogoUrl`), `ProductBrandingRequest` (`Tagline`), `ProductDto` (`ListedOnLanding`, default true), `CreateProductRequest` (`ListedOnLanding`, null means true), `UpdateProductRequest` (`ListedOnLanding`, null means unchanged), `PublicProductDto` (`Tagline`) and `PublicProductSummaryDto` (`Tagline`, `LogoUrl`, `AccentColour`, `ListedOnLanding`). This is source-compatible but changes their constructor and `Deconstruct` signatures: recompile consumers built against 0.2.0. A client that does not send `Tagline` (any 0.2.0 client) clears a product's tagline when it saves, because branding is replaced whole, exactly as `LogoPath` already behaves; `ListedOnLanding` and the uploaded logo are kept. `LogoPath` on `PublicProductDto` and `LogoUrl` on `PublicProductSummaryDto` now carry the effective logo: the uploaded logo when a product has one, else the linked address. `ProductLogoLimits` and `ProductLogoName` are new. `TechStrap.Client` and `TechStrap.Client.Maui` move with it.

### 0.2.0

The positional records `ProductDto`, `CreateProductRequest`, `UpdateProductRequest`, `PublicProductDto` and `PublicProductSummaryDto` gained a trailing `string? PortalHost = null` parameter since 0.1.0. This is source-compatible but changes their constructor and `Deconstruct` signatures: recompile consumers built against 0.1.0. `UpdateProductRequest.PortalHost` is new in 0.2.0: null leaves the stored host unchanged, an empty or whitespace string clears it, and any other value is normalised, validated and set. A 0.1.0 client that saves a product therefore leaves its host as it is. `TechStrap.Client` and `TechStrap.Client.Maui` move with it.

## Using it

You normally get this package through TechStrap.Client, which wraps these contracts in a typed HTTP client with retries and typed errors. Reference TechStrap.Contracts directly only if you write your own client.

More: https://github.com/Syntax-Circus/techstrap/blob/main/docs/development/CLIENT-SDK.md

Source and issues: https://github.com/Syntax-Circus/techstrap
