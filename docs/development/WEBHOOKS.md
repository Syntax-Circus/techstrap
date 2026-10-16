# Webhook notifications

Administrators configure destinations in **Settings → Webhooks**. In a product's settings, the **Product webhooks** section can inherit the global configuration, disable notifications for that product, or replace the entire destination list. Product destinations never add to global destinations.

Start with **Add destination**, choose **Discord**, paste the channel webhook URL, and save. **Test saved destination** queues a sample message; refresh delivery history to see its outcome. The Worker service must be running. Each destination has its own creation triggers, ticket-type selection, and status filters.

Creation notifications default on for support tickets, customer feature-request tickets, and new feature-board entries, including drafts. Subsequent status notifications default off. Ticket statuses are New, Open, Pending, Solved and Closed; feature progress statuses are UnderConsideration, Planned, InProgress, Shipped and NotPlanned. Filters match the status being entered. Publication changes and ordinary content edits do not notify. Customer-reply reopening and automatic closing follow the same filters as explicit changes. Configuration affects future events; it does not backfill existing records.

## Discord setup

In Discord, create an incoming webhook for the desired channel through the channel's integration settings, then copy its webhook URL. TechStrap accepts `https://discord.com/api/webhooks/{id}/{token}`. No bot account is needed. Messages use a summary embed and suppress all mentions, including user-supplied `@everyone` text. TechStrap requests acknowledgment of message creation and honors Discord rate-limit retry delays. See [Discord's webhook reference](https://docs.discord.com/developers/resources/webhook) and [rate-limit documentation](https://docs.discord.com/developers/topics/rate-limits).

The URL grants permission to post to the channel. Settings reads show only its hostname; leave the replacement URL blank when editing to retain the saved value. URLs are stored in the database, so database backups need the same access controls as other application secrets. Full URLs, destination response bodies and payloads are excluded from delivery history and application logs. The dedicated HTTP transport disables URL-bearing request diagnostics.

## Payload and generic receivers

Choose **Generic JSON** for an HTTPS receiver accepting TechStrap's versioned event envelope:

```json
{
  "schemaVersion": 1,
  "eventId": "0199e800-0000-7000-8000-000000000001",
  "eventType": "ticket.status_changed",
  "occurredAt": "2026-10-16T12:00:00Z",
  "product": { "id": "0199e800-0000-7000-8000-000000000002", "key": "orbitly", "name": "Orbitly" },
  "entity": {
    "id": "0199e800-0000-7000-8000-000000000003",
    "type": "Ticket",
    "title": "Export fails",
    "status": "Solved",
    "previousStatus": "Open",
    "number": "ORB-42",
    "ticketType": "Support",
    "publicationState": null,
    "adminUrl": "https://admin.example.com/tickets/ORB-42"
  }
}
```

Event types are `ticket.created`, `ticket.status_changed`, `feature.created`, `feature.status_changed`, and the explicit `webhook.test`. Feature entities use type `Feature`, include publication state, and have null ticket number/type. Bodies, requester details, internal notes, attachments, metadata and customer access tokens are never included. Set `TECHSTRAP_ADMIN_PUBLIC_URL` on both API and Worker to include authenticated admin links; leave blank to omit links.

Requests carry `Content-Type: application/json` and `X-TechStrap-Event-Id`. Receivers should deduplicate by event ID: delivery is at least once, and a lost acknowledgment can cause a duplicate. Generic webhook signing, additional authorization headers and custom payload templates are outside this version. HTTPS endpoints must resolve exclusively to public addresses. Private/local networks, URL authority credentials and redirects are refused.

## Delivery and operations

Events and deliveries commit in the same transaction as the ticket/feature change. Intake idempotency replays do not enqueue another creation event. One destination failing does not stop others or delay the submission request. The Worker polls every five seconds, claims batches of 20 with ten-minute leases, and recovers expired claims after restarts. Each request has a ten-second timeout.

Successful 2xx responses finish delivery. Network failures, timeouts, HTTP 408/429 and 5xx responses retry up to five total attempts, using one-, two-, four- and eight-minute delays. A longer valid server retry delay takes precedence. Other HTTP responses, including redirects, become dead letters immediately. History exposes status, attempt count and a safe error category. Retry dead letters after resolving the destination problem, or discard pending/dead-lettered rows.

Payload and destination details are snapshotted when queued. Endpoint edits affect future events. Disabling/deleting a destination or changing the product's effective destination list cancels pending deliveries that no longer apply; a request already in flight may finish. Sent/discarded rows follow the Worker's existing `OutboxRetention.Enabled`, `Days` and `BatchSize` settings (90 days by default); dead letters remain until handled. Successful/discarded rows clear their URL. Ticket deletion cascades its webhook records; requester erasure removes ticket summary snapshots from all delivery states. Notifications already accepted by external receivers cannot be retracted.

## Admin API

Global routes use `/api/settings/webhooks`; product routes use `/api/products/{productId}/webhooks`. All require an active administrator.

- `GET`: own mode/version/endpoints plus effective destinations; endpoint URLs are omitted and `destination` contains only a masked hostname display.
- `PUT`: `{ "mode": "Replace", "version": 0, "endpoints": [...] }`. Modes are `Replace`, `Disabled`, and product-only `Inherit`. Send the loaded version; stale writes return 409. An endpoint's empty GUID creates an ID; saved IDs must belong to this scope. A null URL retains an existing value. At most 20 endpoints are allowed.
- `GET /deliveries?page=1`: 25 rows per page with `hasMore`; product history includes only that product.
- `POST /actions`: `{ "action": "Test|Retry|Discard", "id": "..." }`. Test uses a saved enabled endpoint ID in Replace mode; Retry/Discard use a delivery ID. Actions and settings changes are audited without URL secrets.
