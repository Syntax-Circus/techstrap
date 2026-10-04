# Ticket operations (agent API)

What an agent can do to a ticket through the API: list, read, reply, note, change status, assignee, priority, product,
tags and spam, and download attachments. Submission is covered in [INTAKE.md](INTAKE.md); sign-in is covered in
[AGENT-AUTHENTICATION.md](../self-hosting/AGENT-AUTHENTICATION.md). The customer routes (read, reply, lost link, attachments) and auto-close are covered under [Customer access](#customer-access).
Delete and erase and dead letters arrive with PHASE-06c.

All routes below need an agent bearer token (a member of the agent group). Set it once, together with a ticket id:

```bash
TOKEN="<access token from your OIDC provider>"   # a dev token, never a production one
API=http://localhost:8080
AUTH="Authorization: Bearer $TOKEN"
```

Every route that takes `{id}` wants the ticket's GUID. Only `GET /api/tickets/{reference}` also accepts the number
(`ORB-1`). Errors are ProblemDetails: 400 for validation, 404 for an unknown ticket, 409 for a stale `RowVersion`
(`concurrency-conflict`) or a Closed ticket (`ticket-closed`).

## RowVersion

Each ticket carries a `rowVersion`. It changes on every write.

- Read it from `GET /api/tickets/{reference}` (the `rowVersion` field).
- Every state-changing route (status, assignee, priority, product, tags, spam) requires it. Send the value you last read; if someone
  else changed the ticket meanwhile you get 409 and should re-read.
- Replies and notes accept it optionally.
- Every write returns the fresh state with the new `rowVersion` (`TicketStateDto`; replies and notes return
  `{ message, ticket }`), so chain writes with the value you just got back instead of re-reading.

## Read

```bash
# Queue views: unassigned, mine, open, pending, all, spam. Filters: productId, status, priority, assigneeId, tagId,
# requesterId, search, page, pageSize.
curl -s -H "$AUTH" "$API/api/tickets?view=unassigned&page=1&pageSize=25"

# Numbers on each queue tab
curl -s -H "$AUTH" "$API/api/tickets/counts"

# One ticket with its timeline (messages, internal notes and events), by number or id
curl -s -H "$AUTH" "$API/api/tickets/ORB-1"
```

Spam tickets appear only in the `spam` view.

## Reply and note

A reply is multipart so files can travel with it. The body is Markdown, rendered and sanitised on the server.
`statusAfter` is empty or `Pending` (the default) or `Solved` ("send and solve"). `linkedArticleIds` may repeat.

```bash
curl -s -H "$AUTH" -X POST "$API/api/tickets/$ID/replies" \
  -F 'body=Hi **Ada**, try resetting your password.' \
  -F 'statusAfter=Pending' \
  -F "rowVersion=$ROW_VERSION" \
  -F 'attachments=@screenshot.png;type=image/png'
```

The reply goes to the customer by email and sets the ticket to Pending. An executable attachment is 400
`attachment-type-not-allowed`. A note is for agents only and is never emailed:

```bash
curl -s -H "$AUTH" -H 'Content-Type: application/json' -X POST "$API/api/tickets/$ID/notes" \
  -d '{"body":"Checked the **logs**: token expired."}'
```

## Change the ticket

```bash
curl -s -H "$AUTH" -H 'Content-Type: application/json' -X PUT "$API/api/tickets/$ID/status" \
  -d "{\"status\":\"Solved\",\"rowVersion\":$ROW_VERSION}"

# Assign (null unassigns). Assigning to someone else queues a ticket-assigned alert to them.
curl -s -H "$AUTH" -H 'Content-Type: application/json' -X PUT "$API/api/tickets/$ID/assignee" \
  -d "{\"assigneeId\":\"$AGENT_ID\",\"rowVersion\":$ROW_VERSION}"

curl -s -H "$AUTH" -H 'Content-Type: application/json' -X PUT "$API/api/tickets/$ID/priority" \
  -d "{\"priority\":\"High\",\"rowVersion\":$ROW_VERSION}"

# Move product: the ticket number never changes.
curl -s -H "$AUTH" -H 'Content-Type: application/json' -X PUT "$API/api/tickets/$ID/product" \
  -d "{\"productId\":\"$PRODUCT_ID\",\"rowVersion\":$ROW_VERSION}"

# Add and remove a tag. Both are idempotent.
curl -s -H "$AUTH" -H 'Content-Type: application/json' -X POST "$API/api/tickets/$ID/tags" \
  -d "{\"tagId\":\"$TAG_ID\",\"rowVersion\":$ROW_VERSION}"
curl -s -H "$AUTH" -X DELETE "$API/api/tickets/$ID/tags/$TAG_ID?rowVersion=$ROW_VERSION"

# Mark spam, or send false to mark it not spam.
curl -s -H "$AUTH" -H 'Content-Type: application/json' -X PUT "$API/api/tickets/$ID/spam" \
  -d "{\"isSpam\":true,\"rowVersion\":$ROW_VERSION}"
```

Status is one of New, Open, Pending, Solved, Closed. A forbidden transition is 409. A Closed ticket rejects every agent change.

## Attachments

```bash
curl -s -H "$AUTH" -o file.bin "$API/api/attachments/$ATTACHMENT_ID"
```

The id comes from the message's `attachments` in the ticket detail. The response is sent as an attachment with
`X-Content-Type-Options: nosniff` and a sandboxing Content-Security-Policy.

## Emails these routes queue

Rows are written to `email_outbox` in the same transaction as the change; the Worker sends them (see INTAKE.md for Mailpit).

| Kind | Queued when | Goes to |
| --- | --- | --- |
| `ticket-confirmation` | A ticket is submitted (PHASE-05) | The requester |
| `agent-reply` | An agent sends a public reply | The requester, signed with the agent's public name, never the agent's email or surname |
| `ticket-solved` | The status route sets Solved. Send-and-solve queues no separate notice: the agent-reply email is marked `Solved = true` instead | The requester |
| `ticket-assigned` | A ticket is assigned to another agent | The new assignee. Self-assignment sends nothing |
| `new-ticket-alert` | A ticket or follow-up is created | Agents opted in for the product. No Powered-by line |
| `customer-reply-alert` | A customer replies | The active assignee, else the opted-in agents. Not sent for spam tickets |
| `access-links` | A lost-link request matches an address | That requester's own address only |

Notes, priority, tags, product moves and spam send nothing. Tickets flagged as spam never email the customer: replies and Solved notices are saved but not emailed (owner decision, D-035); agent assignment alerts are unaffected. The customer's access link exists only in the outbox payload and in the one follow-up reply response that hands the customer its view link (D-033).

## Customer access

The customer view is a separate surface from the agent API. Every `/api/customer` route uses the `Public` policy, authorises by the
ticket token inside the handler, and answers with `Cache-Control: no-store`. The token travels only in the `X-Ticket-Token` header, never
in the path, so it stays out of access logs.

```bash
TICKET_TOKEN="<the token from the emailed link: https://portal/t/{token}>"

# Read the ticket: public messages only. Slides the token's expiry.
curl -s -H "X-Ticket-Token: $TICKET_TOKEN" "$API/api/customer/ticket"

# Reply (multipart: body plus optional attachments). Slides the expiry.
curl -s -H "X-Ticket-Token: $TICKET_TOKEN" -F "body=Still failing" "$API/api/customer/ticket/replies"

# Download a public attachment. A read: it does not slide the expiry.
curl -s -H "X-Ticket-Token: $TICKET_TOKEN" -o file.bin "$API/api/customer/attachments/$ATTACHMENT_ID"

# Lost link: always 202 for a well-formed address, 400 email-invalid otherwise.
curl -s -H 'Content-Type: application/json' -X POST "$API/api/customer/access-link" -d '{"email":"ada@example.com"}'
```

- **Uniform 404.** An unknown, expired or revoked token, a token for another ticket, an erased requester and an attachment that is
  not public all return code `not-found`, byte-identical apart from the echoed `instance`. A caller cannot tell which case it hit.
- **What the customer sees.** Public messages, attachments on them and the agent's public name ("Sam from Orbitly Support"). Never tags,
  internal notes, events, agent ids, emails or surnames, `lastActivityAt`, `rowVersion` or another requester's data (D-024).
  Attachments are sent with `nosniff` and a sandboxing Content-Security-Policy.
- **Replying.** Every non-spam customer reply alerts the active assignee, or else the agents opted in for the product. A reply on a
  Pending or Solved ticket also reopens it. A reply on a Closed ticket creates a
  follow-up ticket (new number, linked to the parent, a `FollowUpCreated` event on the parent, a confirmation email) and returns its
  view link in the response (`followUpCreated`, `followUpViewUrl`); the parent stays read-only. The same text sent again within
  2 minutes replays the existing follow-up with a fresh link and no email, and concurrent duplicates produce one follow-up.
  A follow-up of a spam ticket is spam itself and sends no email or alert.
- **Lost link.** `POST /api/customer/access-link` answers 202 with an identical body whether or not the address matched. Matching
  requesters get one `access-links` email to their own address listing up to 5 of their tickets, each with a fresh link. Earlier
  links are not revoked. The accepted residual risks are recorded in [D-038](../architecture/04-DECISION-LOG.md#d-038-customer-api-public-routes-with-in-handler-token-auth-uniform-404-separate-customer-attachment-route-lost-link-rules-alert-recipients-reopen-window-from-autocloseoptions-per-ticket-auto-close).
- **Rate limits.** Per client IP: 60 requests per 60 seconds on the token routes (`RateLimiting:Customer:TokenAccessPermitLimit`,
  `TokenAccessWindowSeconds`), 5 per hour on the lost-link route (`LostLinkPermitLimit`, `LostLinkWindowSeconds`). On top of that
  each address gets at most 3 lost-link emails per hour (`LostLink:PerAddressLimit`, `PerAddressWindowMinutes`). Only the per-IP limit answers 429; over the per-address cap the route still answers 202 and sends nothing.

### Auto-close

The Worker (never the Api) closes tickets that have been Solved for at least N days, one unit of work per ticket, as the `System` actor
(`StatusChanged` event). It skips spam and Closed tickets, sends no email (D-037), and a customer reply that races a close conflicts that
ticket alone, which is re-evaluated on the next run.

| Setting | Default | Meaning |
| --- | --- | --- |
| `TECHSTRAP_AUTOCLOSE_DAYS` | 7 | Days a ticket stays Solved before it is closed (1 to 365) |
| `AutoClose__Enabled` | true | Turns the loop off |
| `AutoClose__IntervalMinutes` | 15 | Delay between runs |
| `AutoClose__BatchSize` | 50 | Tickets examined per run |

### Sentry

The Api scrubs `X-Ticket-Token`, `X-Api-Key`, `Authorization` and `Cookie` from Sentry events before they leave the process. The Portal
makes no API calls yet, so its scrub moves to PHASE-09 with its first proxied token (D-039).

## Admin operations (06c)

All of these are Admin-only (D-022) and write an `AdminEvent` whose payload holds ids and counts, never ticket content or personal data.

```bash
# Hard-delete a ticket: its messages, attachment rows and files, events, tokens, tags and outbox rows go. 204, or 404 for an unknown id.
curl -s -X DELETE -H "$ADMIN_AUTH" "$API/api/tickets/$TICKET_ID"

# Erase a requester (GDPR). 204; running it again is safe and also 204.
curl -s -X POST -H "$ADMIN_AUTH" "$API/api/requesters/$REQUESTER_ID/erase"

# Dead letters: list (paged), retry, discard.
curl -s -H "$ADMIN_AUTH" "$API/api/dead-letters?page=1&pageSize=25"
curl -s -X POST -H "$ADMIN_AUTH" "$API/api/dead-letters/$OUTBOX_ID/retry"
curl -s -X DELETE -H "$ADMIN_AUTH" "$API/api/dead-letters/$OUTBOX_ID"
```

- **Delete.** A follow-up of the deleted ticket survives with its parent link cleared. Its own `Created` event still records the old parent id. Files are removed after the commit, best effort; a failure is logged by storage key only.
- **Erase.** The requester row stays as a tombstone, so ticket numbers and event ids survive. Erase replaces the subject of every ticket the requester opened, and the body of every message the requester wrote, with `[erased]`. It clears the tickets' `metadata` and `custom_fields`, deletes the attachments on the requester's messages (rows and files), revokes every access token, and deletes every `email_outbox` row addressed to the requester or belonging to one of their tickets, in any status. Agent replies and internal notes stay. Agents holding an old row version of those tickets get a 409 and must reload.
- **Dead letters.** `Recipient` is masked (`a***@example.com`) and the payload is never returned. Retry makes the row `Pending` with no attempts, and the Worker sends it on its next poll. Discard marks it `Discarded`. Either answers 409 `outbox-not-dead-lettered` for a row that is not a dead letter.

### Outbox retention

The Worker deletes `Sent` and `Discarded` outbox rows older than N days, measured from `created_at`, in batches. Dead letters, `Pending` and `Sending` rows are never deleted.

| Setting | Default | Meaning |
| --- | --- | --- |
| `OutboxRetention__Enabled` (`TECHSTRAP_OUTBOX_RETENTION_ENABLED` in compose) | true | Turns the sweep off |
| `OutboxRetention__Days` (`TECHSTRAP_OUTBOX_RETENTION_DAYS` in compose) | 90 | Age at which a finished row is deleted (1 to 3650) |
| `OutboxRetention__IntervalMinutes` | 60 | Delay between sweeps |
| `OutboxRetention__BatchSize` | 500 | Rows deleted per statement; a full batch runs again at once |

### Logging and personal data

Every Serilog event in the Api and the Worker passes through `PiiRedactionEnricher` before any sink: email addresses become `[email]`, 43-character access tokens (with or without a `tsk_` or `tsp_` API key prefix, so API keys too) `[token]` and `sha256:` hashes `[hash]`, in every property, including nested ones. It cannot rewrite an attached exception or recognise a name, so application code logs ids and exception type names only, and nothing may enable `EnableSensitiveDataLogging` or `Include Error Detail` (`LoggingSafetyTests` fails the build if one does). The Admin host has joined since PHASE-07a, through the shared `TechStrap.Hosting` project (D-040); the Portal joins in PHASE-09.

## Try it end to end

`TicketLifecycleEndToEndTests` runs the whole path against a real database: a form submission, assign, note, reply with an
attachment, priority, tag, product move, Solved, then the timeline and the download.

`TicketLifecycleEndToEndTests` also covers the customer side: the emailed link, a customer view, an agent reply, a customer reply
that reopens the ticket and alerts the assignee, Solved then auto-close with an advanced clock, a reply on the Closed ticket that creates a
follow-up, and a lost-link request. `SensitiveDataLeakTests` checks that the customer view and customer emails carry no internal or agent-private data. Three more
`TicketLifecycleEndToEndTests` cover the admin side: deleting a Closed ticket whose follow-up survives, erasing a requester (nothing
personal left in rows, files, search or the customer link), and listing, retrying and discarding dead letters. `LogRedactionTests` runs the
intake, customer view and lost-link flow and scans every captured log event.
