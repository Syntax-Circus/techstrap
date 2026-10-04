# Ticket operations (agent API)

What an agent can do to a ticket through the API: list, read, reply, note, change status, assignee, priority, product,
tags and spam, and download attachments. Submission is covered in [INTAKE.md](INTAKE.md); sign-in is covered in
[AGENT-AUTHENTICATION.md](../self-hosting/AGENT-AUTHENTICATION.md). The customer routes, delete and erase, dead letters and
auto-close arrive with PHASE-06b.

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

Notes, priority, tags, product moves and spam send nothing. Tickets flagged as spam never email the customer: replies and Solved notices are saved but not emailed (owner decision, D-035); agent assignment alerts are unaffected. The customer's access link exists only in the outbox payload.

## Try it end to end

`TicketLifecycleEndToEndTests` runs the whole path against a real database: a form submission, assign, note, reply with an
attachment, priority, tag, product move, Solved, then the timeline and the download.
