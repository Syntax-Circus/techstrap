# Feature requests

Customers can select **Support** or **Feature request** on a product's contact form. Existing integrations that omit `type` continue to create Support tickets. JSON and multipart intake accept `Support` and `FeatureRequest`; unknown values are rejected.

## Agent workflow

1. Open **Feature requests** to review private requests. This queue includes every non-spam feature-request ticket, including solved and closed requests.
2. Open **Features**, choose **New feature**, select its product, and write a public title and Markdown description. The URL slug and product are permanent. Saving creates a private draft.
3. Save changes, then **Publish** when the public copy is ready. Progress is separate from ticket status: Under consideration, Planned, In progress, Shipped, or Not planned.
4. On a private feature-request ticket, use **Public feature** to search that product's features and link the request. Multiple requests can link to one feature. Unlink before changing the request to Support or moving it to another product.
5. Use **Unpublish** to remove a feature from public pages. Published edits take effect when saved; drafts remain private.

The board appears at `/p/{product-key}/features`, or `/features` on the product's mapped host. It uses the product's existing skin. Public pages show only curated title, description, progress, and last update. Customer identities, conversations, attachments, metadata, linked-request counts, and ticket URLs remain private.

Voting, public comments, customer accounts, subscriptions, and automatic progress emails are outside this version. Customers receive the normal private ticket receipt and can continue the conversation through their private link.

## Deployment

Apply the `AddFeatureRequests` EF migration before deploying the updated applications. It backfills old tickets as Support and adds the feature board tables and links. No production database is changed by adding the migration to the repository.

Contract consumers should recompile after upgrading: the additive optional ticket type and feature ID parameters change positional record constructors and deconstruction signatures. The submission wire contract remains compatible with callers that omit `type`.

## Product skins

Skins already exist. In **Settings → Products → Appearance (advanced)**, paste JSON into **Skin (JSON)** and save. The product's mapped portal host uses the same skin.

The product-specific sample is [`../skins/dragon-poop.skin.json`](../skins/dragon-poop.skin.json); see [`../skins/README.md`](../skins/README.md) for application instructions and current styling limits. Built-in packs are Classic, Slate, Paper, Contrast, and Midnight.
