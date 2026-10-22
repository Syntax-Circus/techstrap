# PHASE-12c: UAT and release

**Status:** Planned (2026-10-22). Decision: D-056. Splits the open tasks of [PHASE-12](PHASE-12-release-hardening.md) (P12-T12, T14, T15, T18, T20, T21) into an ordered runbook. 12a (PR #28) and 12b (PR #29) are merged. Nothing here runs in CI.

## Objective and boundaries

Prove the published release on a real second environment, then sign it off. UAT does not exist yet: this phase builds it, fills it by restoring a backup of the owner's running production deployment (which is also the restore drill, P12-T15), and runs the load test, a browser UAT pass, and a 48-hour soak against it.

In scope: the UAT stack, the production-to-UAT restore, the load run, the browser UAT checklist, the soak, the final docs pass and the release tag.

Out of scope: new features, the `v1.0.0` API lock (a separate decision), antivirus scanning, multi-instance API, and any new CI job or gate (load, backup and soak are on demand only; see the keep-CI-short rule).

## Decisions (D-056, owner 2026-10-22)

1. **UAT runs the latest published tag**, the same as production (currently v0.4.6). It is pinned by tag in the env files, never by a floating `latest`.
2. **UAT is filled by restoring a production backup.** `deploy/backup.sh` against production, `deploy/restore.sh` into UAT. The production data holds nothing sensitive (one test ticket), so there is no scrub or sanitisation step.
3. **UAT shares production's outbound services.** The same SMTP path and the same Discord webhooks. UAT activity can therefore reach real inboxes and channels; the owner accepts this. The load run is the one exception (see P12c-T04).
4. **Claude in Chrome may drive the UAT UI** and may create and work tickets. The owner signs in to the identity provider; Claude never handles credentials.
5. **The end state is the next tag after v0.4.6, not `v0.3.0`.** D-051 named `v0.3.0` when the latest was v0.2.0; releases have since passed it. The tag number is the owner's call at P12c-T08, and the phase stays 0.x (`v1.0.0` is a later decision).

## Dependencies

12a and 12b merged. Owner actions #7 (Authentik clients and groups for UAT), #8 (proxy and trusted network values, shared Postgres Docker network, scoped env files under `/etc/techstrap/uat/`), #11 (scratch environment and load-test scheduling), #14 (DNS and a Caddy site per product host) and #17 (landing and logo settings). Production must be reachable for a backup.

## Actionable tasks

Original PHASE-12 IDs are in brackets. Each task's validation is the source of truth; the original text in PHASE-12 still applies where this phase does not change it.

- [ ] **P12c-T01** Stand up UAT from scratch with the latest published tag [P12-T14, first half]: env files from `deploy/.env.<app>.example` and `deploy/.env.uat.example` (D-043), `deploy/docker-compose.yml`, DNS and Caddy, migrations applied once by the API.
  - **Depends on:** owner actions #7, #8, #14
  - **Validation:** All four containers healthy; an Authentik agent signs in to the UAT Admin and a non-member is refused; `/health/ready` is green on all hosts.
- [ ] **P12c-T02** Restore drill: back up production and restore it into UAT [P12-T15]. Time it against RPO 24 h and RTO 4 h, run the runbook verification checklist, and settle the "Open checks for 12c" items in `docs/runbooks/backup-restore.md` (`pg_restore --clean` against the real role, `citext` and ownership, volume-root ownership). Schedule the backup timer on production.
  - **Depends on:** P12c-T01, P12-T13
  - **Validation:** Migration state and ticket, product and attachment counts match production; an old attachment downloads; measured times and any fixes are recorded in the runbook; the backup timer fires once.
- [ ] **P12c-T03** Re-point the restored data at UAT. After a restore, product `PortalHost` values, OIDC redirect URIs and public URLs are production's. Rewrite or alias them for UAT, then finish the product-host DNS and Caddy sites and the landing-page settings (owner actions #14 and #17).
  - **Depends on:** P12c-T02
  - **Validation:** Each product opens on its UAT host with its skin and logo; the landing page lists the products; no UAT page links to a production host.
- [ ] **P12c-T04** Seed volume and run the load suite [P12-T12]. The restored copy is almost empty, so seed realistic volume first (tickets, messages, KB articles) and run `scripts/Invoke-LoadTest.ps1` with the D-051 budgets: 20 req/s for 10 min, p95 under 500 ms, no 5xx; a spike of 100 req/s for 60 s answered with 429s and recovery within 30 s; the outbox drains within 2 minutes with no dead letters. Load mail must not go to real inboxes: switch UAT SMTP to a capture sink, or use a sink domain for the load requesters, for the duration of the run, then restore the setting. Check the ticket search GIN plan on the seeded data (carried forward from PHASE-03).
  - **Depends on:** P12c-T03
  - **Validation:** Results and box specs recorded in `docs/load-test-results.md`; budgets met or a documented tuning change landed with a test; SMTP setting confirmed restored.
- [ ] **P12c-T05** Browser UAT with Claude in Chrome [P12-T14 validation and the open owner manual checks from PHASE-08, 09 and 10]. Claude drives a checklist in a new browser tab, may create and work tickets, and logs each result in a UAT log. Checklist: portal landing and each product host and skin; ticket submit and customer view and lost-link; KB browse and search; feature boards; Admin sign-in, ticket work, appearance editor and product settings; webhook test send; live updates across two tabs; the SDK sample. The owner signs in; the JavaScript-off walk, axe, Lighthouse and the MAUI device smoke test stay with the owner.
  - **Depends on:** P12c-T03
  - **Validation:** The UAT log lists every checklist item as pass or an issue; issues are fixed or deferred with a reason; the PHASE-08/09/10 owner evidence items are ticked or carried forward.
- [ ] **P12c-T06** 48-hour soak [P12-T18] on UAT with real use, after T04 and T05 are done.
  - **Depends on:** P12c-T02, P12c-T04, P12c-T05
  - **Validation:** No unexplained 5xx, no dead letters, stable memory and connections, listener reconnects explained; issues filed and fixed or deferred with reasons; SDK feedback collected.
- [ ] **P12c-T07** Final documentation pass [P12-T20]: README quickstart and screenshots, CONTRIBUTING, SECURITY contact and process, statuses in `00-DISCOVERY-INDEX.md` and `99-IMPLEMENTATION-ROADMAP.md`, the original spec marked superseded, release notes.
  - **Depends on:** P12c-T05, P12c-T06
  - **Validation:** Link check passes; docs name the shipped env keys and image names; the owner approves the release notes.
- [ ] **P12c-T08** Tag and publish the next release from `main` [P12-T21] and verify images, packages and the GitHub Release; upgrade UAT from the soaked tag without data loss.
  - **Depends on:** P12c-T06, P12c-T07
  - **Validation:** CI publishes the four GHCR images and the NuGet packages for the new tag; `docker compose pull && up` on UAT upgrades cleanly; production upgrade is the owner's call.

## Owner actions

| Needed for | Action |
| --- | --- |
| P12c-T01 | Authentik clients and groups for UAT (roadmap action #7); proxy values, DB network and env files (#8); DNS and Caddy per host (#14) |
| P12c-T02 | Production backup access; a passphrase file for the backup |
| P12c-T04 | Agree the load window on the shared UAT host (#11) |
| P12c-T05 | Sign in to the identity provider in the browser Claude drives; run axe, Lighthouse, the JavaScript-off walk and the MAUI device smoke test |
| P12c-T08 | Choose the version number; approve the release notes |

## Risks and open questions

- [ ] The restored database carries production's product hosts, OIDC redirect URIs and API keys. Verify UAT cannot be confused with production (hosts, cookies, links) before testing.
- [ ] UAT shares SMTP and Discord with production, so test tickets and webhook sends reach real recipients. Accepted; keep test content obviously labelled as UAT.
- [ ] UAT shares a host with other services; schedule the load run off-hours and cap resources.
- [ ] If UAT is a clone of production configuration, the soak proves less about a clean install; the clean-room run is covered by the 12b self-hosting guide check.
- [ ] D-051 says `v0.3.0`; this phase supersedes that with "the next tag after v0.4.6".

## Handoff

The soaked tag is published, UAT has been upgraded to it, `docs/load-test-results.md` and the runbook carry the real numbers, and the PHASE-12 tasks T12, T14, T15, T18, T20 and T21 are ticked with as-built notes. `v1.0.0` remains a separate decision that locks the Contracts public surface.
