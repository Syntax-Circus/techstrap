# Development seed data

With `ASPNETCORE_ENVIRONMENT=Development` and `TECHSTRAP_SEED_DEV_DATA=true`, the Api seeds two products, two agents, three
requesters, tags, tickets in every status and knowledge base articles. Seeding runs once per database (markers: product
`orbitly`, shared article `welcome`).

## Dev knowledge base

Three categories (shared `getting-started`, Orbitly `account`, Paperplane `guides`) and five articles. Three are published, so the
public KB endpoints return something for both products (D-044):

| Article | Product | Category | Status |
| --- | --- | --- | --- |
| `welcome` | shared | `getting-started` | Published |
| `reset-password` | Orbitly | `account` | Published |
| `using-dark-mode` | Paperplane | `guides` | Published |
| `export-csv` | Orbitly | none | Draft |
| `old-pricing` | Orbitly | none | Archived |

For example `GET /api/public/kb/paperplane/categories` lists `getting-started` and `guides` (each with one article), and a reply may link
any Published article that is shared or belongs to the ticket's product. A database seeded before PHASE-08 keeps its four articles: the seed
runs once, so start from a fresh development database to get the Paperplane article.

## Dev API keys

These keys are fake and work only against a database the development seeder filled. Never use them anywhere else.

| Product | Kind | Key |
| --- | --- | --- |
| Orbitly | Trusted | `tsk_devOrbitlyServerKeyNotASecret00000000000000` |
| Orbitly | Public | `tsp_devOrbitlyAppKeyNotASecret00000000000000000` |
| Paperplane | Trusted | `tsk_devPaperplaneServerKeyNotASecret00000000000` |

A database seeded before PHASE-04 holds placeholder hashes that match no key. Start from a fresh development database to get
working keys (remove the dev database volume yourself; the seeder never deletes data).

## Dev agents

Seeded agents are `dev|sam` (Admin) and `dev|riley` (Agent, public display name "Ry"). A token from your own identity provider
signs you in as a new agent on your first `GET /api/agents/me`; roles come from your IdP groups (D-029).
