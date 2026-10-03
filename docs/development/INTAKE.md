# Ticket intake and email

How a ticket gets in, and how the confirmation email gets out. Run the local stack first (the README quick start), with
`TECHSTRAP_SEED_DEV_DATA=true` in your shell so the dev product and keys exist (see [DEV-DATA.md](DEV-DATA.md)). The
keys below are the fake dev keys, safe to paste.

## The three submission routes

| Route | Auth | Rate limit (defaults) | Used by |
| --- | --- | --- | --- |
| `POST /api/intake/tickets` (JSON) | `X-Api-Key`, trusted or public key | Trusted key: 120 per minute per key prefix. Public key: 10 per minute per key prefix and client IP | Product backends and the SDK |
| `POST /api/public/products/{productKey}/tickets` (multipart) | None | 5 per 10 minutes per client IP | The portal contact form |
| `GET /api/public/products/{productKey}` | None | 120 per minute per client IP | The portal, for product branding |

The limits come from `RateLimiting__Intake__*` and `RateLimiting__Public__*` (see `src/TechStrap.Api/.env.example`).
A trusted key may send `externalUserRef` and metadata; both are stored. A public key or the web form has the reference
dropped, and the response carries the warning `external-user-ref-ignored`.

API key, trusted:

```bash
curl -s -X POST http://localhost:8080/api/intake/tickets \
  -H "X-Api-Key: tsk_devOrbitlyServerKeyNotASecret00000000000000" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{"email":"ann@example.com","name":"Ann","subject":"Hi","body":"Hello","externalUserRef":"u-1"}'
```

API key, public:

```bash
curl -s -X POST http://localhost:8080/api/intake/tickets \
  -H "X-Api-Key: tsp_devOrbitlyAppKeyNotASecret00000000000000000" \
  -H "Content-Type: application/json" \
  -d '{"email":"ann@example.com","subject":"Hi","body":"Hello"}'
```

Web form (anonymous, multipart, up to five attachments):

```bash
curl -s -X POST http://localhost:8080/api/public/products/orbitly/tickets \
  -F email=ann@example.com -F name=Ann -F subject=Hi -F body=Hello \
  -F attachments=@screenshot.png
```

The key submission answers `201` with `ticketNumber`, `viewUrl` (the customer's private link) and `warnings`, with
`Cache-Control: no-store`. The web form answers `201` with the ticket number only; the link arrives by email.
A wrong, revoked or missing key is a uniform `401`.

## Honeypot

The web form has a hidden `website` field that people never see. If it is filled, the API answers the normal `201` with a
plausible ticket number, creates no ticket and sends no email (D-032). Do not fill it in your own tests.

## Idempotency

`POST /api/intake/tickets` accepts an optional `Idempotency-Key` header (up to 200 characters), scoped per API key and
kept for 24 hours. Sending the same key again returns the same ticket number with a fresh customer link, and creates no
second ticket, message or email. The stored response never holds a link, so each reply issues a new one. The same key on a
different API key is a separate ticket. An over-long key is a `400`.

## Attachments

Web form only (API-key intake is JSON-only in v1, D-034). At most 5 files, 10 MiB per file, 25 MiB per message. Allowed:
PNG, JPEG, GIF, WebP, PDF, plain text, `.log`, CSV and ZIP. Each file must match both its declared type and its leading
bytes; the stored content type is the canonical one for the matched kind. A disallowed file is a `400`, an oversize body a `413`.

## Mailpit and the smoke script

The local stack runs [Mailpit](https://mailpit.axllent.org/) as a mail catcher. The web UI is at <http://localhost:8025>
(loopback only; the SMTP port stays on the compose network). If port 8025 is taken, set `TECHSTRAP_MAILPIT_PORT` before `docker compose up` and use that port instead. The Worker sends through it without TLS.

```powershell
pwsh -File scripts/Send-TestTicket.ps1            # submits one ticket with the dev Orbitly trusted key
pwsh -File scripts/Send-TestTicket.ps1 -DryRun    # prints the request without sending it
```

It prints the ticket number and the customer link. Within about 10 seconds the confirmation email, with a subject that
starts `[ORB-`, shows in Mailpit; `curl -s http://localhost:8025/api/v1/messages` lists it. Parameters: `-BaseUrl`,
`-ApiKey`, `-Email`, `-Name`, `-Subject`, `-Body`, `-IdempotencyKey`. The script never prints the key.

## How the email outbox drains

The intake request writes an `email_outbox` row in the same transaction as the ticket, so an email failure never fails the
request. The Worker's outbox loop (`EmailOutbox__Enabled`) polls every `EmailOutbox__PollIntervalSeconds`, claims up to
`EmailOutbox__BatchSize` due rows with `FOR UPDATE SKIP LOCKED` (two Workers never take the same row), renders the email
with the product's current branding and sends it over SMTP.

- **Retries.** A failed send goes back to pending and waits 1, 2, 4, then 8 minutes. After 5 attempts the row is
  dead-lettered.
- **Lease.** A claim lasts `EmailOutbox__LeaseSeconds` (default 120) and must cover sending one whole batch. If a Worker
  crashes, its rows are reclaimed after the lease expires. A claim that keeps expiring counts toward the 5 attempts.
- **At-least-once.** A crash between sending and recording can send an email twice. Every message carries
  `Message-ID: <outbox-id>@techstrap.local`, so a duplicate is recognisable.
- **SMTP retries are off inside the sender.** The outbox owns retrying, so the SMTP client is set to
  `Email__Smtp__MaxRetryAttempts=1` and `Email__Smtp__RetryMode=TransientOnly`; a client that retried on its own could
  deliver twice while the outbox also retried. `Email__Smtp__TotalSendTimeout` (30 seconds) bounds one send.
- **Real servers.** Set `SMTP_TLS_MODE` (`StartTls` by default; `None`, `Auto`, `SslOnConnect` and `StartTlsWhenAvailable`
  also work) in the production env file.
- **Plaintext.** The outbox payload holds the customer link token until retention work in PHASE-12 (D-033). Treat the
  database as sensitive.

## Known limits

- **Rate-limit partitions.** Unauthenticated callers can create rate-limit partitions by inventing key prefixes. Each such
  request still costs one indexed hash lookup and ends in a `401`, and idle fixed-window partitions are evicted. Put the
  reverse proxy's own limits in front of the API on a public deployment.
