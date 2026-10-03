# TechStrap Brand & Visual Identity

**Status:** complete (P02-T01, P02-T03). Direction selected by the owner on 2026-10-02: **Carbon Copy v2** (D-023). Reference implementation: `docs/design/mockups/direction-carbon-copy-v2.html`. If this file and a mockup disagree, this file wins.

`DESIGN.md` (in `_template/`) defines **how we design**. This file defines **what TechStrap looks and feels like**. Once a direction is selected it becomes a constraint for UI work in PHASE-07 (Admin) and PHASE-09 (Portal). Do not silently fill unspecified decisions with generic SaaS or Bootstrap-admin conventions; explore or ask.

Tags: **Decision** (owner decided), **Assumption** (decided by the drafter, not the owner; review these).

---

# 1. Product Identity

**Name:** TechStrap ("Support for Technical Support")

**One-line description:** A lightweight, open-source (MIT), self-hosted helpdesk for one company that supports many products.

**Who it is for:** a small company (fewer than 50 agents) that ships several products and wants one place to receive, triage and answer support requests, from web forms and from inside its own apps, without running a heavy helpdesk. Secondary: other companies who self-host their own copy.

**What it does:** one installation, many products. Each product gets its own ticket prefix (`ACME-142`), contact form, knowledge base and branding. Customers need no account and follow a ticket through a private emailed link. Agents work in one Admin app with queues, replies, internal notes, tags and live updates.

**Why it exists:** it revives the owner's college project, a WinForms call-logging tool. The heritage is a plain, fast tool for logging calls and getting them answered. TechStrap keeps that spirit: small, direct, no ceremony.

**What differentiates it** (specific, demonstrable):

- Many products, one company, deliberately **not multi-tenant**. Others run their own copy.
- Customer-facing pages are **the product's own**, not ours: the portal wears each product's name, logo and accent.
- No customer accounts or passwords; possession of the emailed link is the identity.
- Small footprint: four Docker images and one Postgres, started with `docker compose up`.
- A cheeky name and mascot that sit **outside** the work. The tools themselves are plain and serious.

---

# 2. Audience and Personas

| Persona | Who | Sees | TechStrap brand exposure |
| --- | --- | --- | --- |
| Agent | Company staff answering tickets, in the app all day | Admin | Full chrome: header logo, empty states, sign-in, 404. Working screens stay calm and dense. |
| Admin | An agent with configuration rights (products, branding, keys, agents) | Admin | Same as agent, plus settings screens (still serious). |
| Customer | End user of one of the company's products; anonymous | Portal, emails | **None** beyond a small "Powered by TechStrap" footer mark. They see the product's brand. |
| Self-hoster | Another company's operator installing their own copy | README/GitHub, docs, `.env.example`, then Admin as above | README and docs carry the mascot and voice (a brand moment). Their agents then see Admin as above; their customers see only their products. |

Product-app developers (SDK users) meet the brand in the README and API docs only. **Assumption:** they get the README voice rules, nothing more.

---

# 3. Brand Personality and Voice

## Principle: cheeky frame, serious tools (Decision)

The mascot and its voice are a **frame** around the product, not a layer over it. Where someone is working, the interface is calm, dense and plain-spoken. Where someone is arriving, leaving, waiting or lost, we are allowed to wink.

## Primary traits

1. **Plainspoken** — says what happened and what to do next, in few words. Agents are mid-task.
2. **Dependable** — dense, predictable, keyboard-friendly. Never surprising in a working screen.
3. **Nostalgic** — a real tool with a 90s lineage (call-logging, beige plastic), not a retro costume.
4. **Cheeky, in its place** — the jockstrap pun is a confident wink at the edge of the product.
5. **Open** — honest about being small, open source and self-hosted. No marketing gloss.

## Where humour is allowed (brand moments)

Mascot and wink-y copy may appear **only** in the closed list below. The retro window and full-figure mascot are further limited to three Admin screens (section 18).

- Admin header logo (the mascot mark; no copy)
- Admin "all caught up" state (retro window)
- Admin sign-in screen (retro window)
- Admin 404 and generic not-found (retro window, head mark)
- README and GitHub repository (including social preview)
- Style guide

## Where humour is banned

- **Everything customers see:** portal pages, emails, confirmation and error text, ticket links. No mascot, no puns.
- **Error messages that block work** (failed save, lost connection, permission denied, validation failure). Plain cause plus next step.
- **Legal, privacy and security copy** (privacy page, erasure confirmations, token and link-expiry messages, security docs, `SECURITY.md`).
- Queues, ticket detail, reply composers, forms, settings, dialogs, destructive-action confirmations, toasts that report an outcome.
- Notifications and anything sent to another person.

If in doubt, it is not a brand moment. Do not add to the allowed list without an owner decision.

## The jockstrap pun

The name and mascot (a retro beige CRT in a jockstrap, thumbs up, tube socks) are the joke. It is **affectionate, never crude**: a supportive-gear gag ("strapped in", "we've got your back"), not innuendo. No anatomy, no sexual wordplay, no body humour, no jokes at a user's expense. One wink per brand moment, then stop. The mascot is cheerful and competent, never leering. The pun explains itself in the README once; it is not repeated in the product.

## Copy examples

| Moment | Good | Bad |
| --- | --- | --- |
| Admin, empty queue | "All caught up. Strapped in and nothing to do." | "Nothing here, you slacker!" / any innuendo |
| Admin, 404 | "Page not found. This one slipped out of the queue. Back to tickets." | "Oops! Something went wrong 😬" |
| Sign-in | "Sign in to TechStrap. Support for Technical Support." | "Ready to get strapped?" |
| Admin, save fails | "Couldn't save. The server didn't respond. Your changes are still here; try again." | "Whoops, the CRT hiccuped!" |
| Admin, dead letter | "3 emails failed to send. Review them." | "Email gremlins strike again!" |
| Portal confirmation | "We've got it. Your ticket is ORB-38. We've emailed you a link to follow it." | Any mascot line or pun |
| Portal, expired link | "This link has expired. Enter your email and we'll send a new one." | "This ticket has left the building!" |
| Privacy / erasure | "We'll delete your name, email and messages. This can't be undone." | A joke of any kind |
| README tagline | "Support for Technical Support." plus the mascot | Feature-list buzzwords ("powerful, modern, AI-ready") |

Style rules: sentence case, active voice, short sentences, no exclamation marks outside brand moments, no emoji, no all-caps shouting. Brand-moment lines are one line, under 12 words where possible.

---

# 4. Desired Response

We want an agent to think: *"Fast and clear. Oh, and it has a sense of humour."*

We want people to feel: in control of a busy queue; that the tool respects their time; a small smile at the door, not in the work.

We want to avoid making people feel: that the product is a joke; that support is taken lightly (customers); talked down to or distracted mid-ticket (agents).

---

# 5. This Should Feel Like...

> TechStrap should feel like **a well-worn beige workstation with a funny sticker on the case**: all business inside, a bit of personality on the outside.

> It should have the confidence of a **90s productivity app** (clear menus, obvious state, no fluff) without copying Windows 95 or any real OS chrome.

> More **utility drawer** than **launch event**. More **call-log ledger** than **conversational chat product**.

---

# 6. This Should NOT Feel Like...

- **A generic SaaS helpdesk** (Zendesk-clone gloss, onboarding checklists, gradient heroes).
- **A Bootstrap admin template.** The Admin uses Bootstrap 5 SCSS as a base, not as its look.
- **A novelty retro toy** or a full Windows 95 skin: no bevel everywhere, no fake window title bars on working screens, no pixel fonts for working text.
- **A joke product.** Customers must never doubt that their request is taken seriously.
- **A crude or "bro" brand.** The pun stays affectionate.
- **An AI startup.** No sparkles, glowing orbs or "AI-powered" language.
- **A branded customer experience for TechStrap.** The portal belongs to the product, not to us.

---

# 7. Visual Metaphor

## Primary metaphor

**Metaphor:** retro-90s computing, drawn from the mascot: beige plastic, CRT blue, chunky navy outlines (**Decision**: used with restraint).

Why it belongs: the product revives a college-era WinForms call-logging tool, and the mascot is a beige CRT. The metaphor connects the heritage, the name and the mascot, and it signals "plain, honest software."

Vocabulary as narrowed by the direction (Carbon Copy v2). Working screens take the ledger feel and a navy-ink palette; beige and CRT blue are the frame:

- warm beige plates and chunky navy outlines (brand-moment windows only)
- CRT blue as the brand-moment button colour; Admin working screens use navy ink and a blue link accent
- a little LED-style indicator (presence dot; the title-bar LED in brand moments)
- a call-log ledger feel: ruled rows, ticket numbers as the primary identifier

Mascot palette sampled from the logo: beige `#EFDDBB`, CRT blue `#3B95E0`, navy outline `#0B1F4B`, sock red `#D9262E`, spark yellow `#F6C12B`, LED green `#22A447`. These became the `--bm-*` brand-moment tokens (section 12), used on brand moments only.

## Secondary influence: the call-log heritage

What we borrow:

- a list-first layout: the log of calls is the product, with number, who, what, status, when
- plain, labelled controls; state you can read at a glance
- keyboard-first speed

What we explicitly do NOT borrow:

- WinForms grey-gradient chrome and default control styling
- modal-heavy workflows

## Retro-90s computing

What we borrow:

- beige plastic and CRT blue as a **palette mood**
- chunky outlines and flat, confident shapes
- the beige plate, navy outline and title bar as a **retro window at brand moments only** (section 18)

What is novelty to avoid:

- full Windows 95 or Mac OS 8 imitation on working screens: title bars, bevels on every control, system sounds
- pixel or "terminal green on black" typography for working text
- CRT curvature, flicker, noise or scanline effects over working surfaces
- vaporwave, synthwave or neon 80s tropes (wrong decade, wrong mood)
- skeuomorphic floppy disks and cassettes as icons

**Restraint rule:** on working surfaces the metaphor shows up as palette, outline weight and small details. It must never cost density, legibility or contrast.

---

# 8. Brand Surfaces and the Mascot Boundary

| Surface | Mascot | Wink-y copy | TechStrap palette/metaphor | Notes |
| --- | --- | --- | --- | --- |
| Admin chrome (header, nav, working screens) | Head mark in header only | No | Yes, restrained | Calm and dense. Queues, ticket detail, composers, forms, settings. |
| Admin all-caught-up | Head mark, inside a retro window | Yes, one line | Yes (`--bm-*`) | Other empty states (no results, empty KB) are plain text (**Assumption**). Never on error states. |
| Sign-in | Head mark, inside a retro window | Yes, one line | Yes (`--bm-*`) | Legal and consent text stays plain. |
| 404 / not found (Admin) | Head mark, inside a retro window | Yes, one line | Yes (`--bm-*`) | Always offer a clear way back. |
| Portal pages | **No** | **No** | **No**: product name, logo and accent lead | Plain and product-led (Decision, section 10). |
| Portal footer | 16px head mark allowed (**Decision**) | No | Neutral | "Powered by TechStrap" small text linking to https://github.com/Syntax-Circus/techstrap, optional 16px head mark; the only TechStrap element. Shown by default; the installation setting `TECHSTRAP_PORTAL_SHOW_POWERED_BY=false` hides it (D-024). |
| Emails (customer-facing) | **No** | **No** | **No**: product branding | Same footer rule as the portal: GitHub link in HTML, bare URL in text, same installation setting. |
| README / GitHub | Yes | Yes | Yes | Social preview, badges, repository banner. |
| Style guide / brand pages | Yes | Yes | Yes | Shows the system, including the mascot rules. |

Rules:

- The mascot is the **head mark** in the product; the full figure (thumbs-up, tube socks) is for README, style guide and sign-in (**Assumption**). The 404 uses the head mark. The retro window appears only on Admin all-caught-up, sign-in and 404, plus the style guide.
- Any surface a customer can see follows the Portal rows. If unsure whether a customer might see it (previews, shared links, exported tickets), treat it as customer-facing.
- Product branding preview inside Admin settings shows the product's look, not TechStrap's.

---

# 9. Visual Exploration

Three directions were rendered as interactive mockups (queue, ticket, portal, light and dark). The owner picked one base and asked for three revisions, producing v2. The mockups are throwaway exploration artifacts; this file is the system of record. Index: `docs/design/directions.md`.

| Direction | Idea | Strength | Risk | Outcome |
| --- | --- | --- | --- | --- |
| **Beige Box** (`direction-beige-box.html`) | Late-90s office workstation: beige plates, 2px navy outlines, CRT-blue accent, window title bars, bevels. | Closest to the mascot and the product heritage; very recognisable. | Heavy outlines raise weight and feel boxy at density; bevels and title bars tip into Win95 parody. | **Rejected** as a working-screen look. Its window frame and palette survive, **only** for Admin brand moments. |
| **Night Shift Console** (`direction-night-shift.html`) | Keyboard-first dark ops console: mono data, bracketed badges, hairline grid, status bar. | Best keyboard model and density. | Cold and intimidating; mono everywhere is slow to read; hatching is noisy; the mascot is the only warmth. | **Rejected** as a look. Its keyboard layer (kbd, j/k, palette, status bar) survives, re-skinned. |
| **Carbon Copy** (`direction-carbon-copy.html`) | Triplicate call-log form: ledger rows, rubber stamps, white/canary/pink colour code. | Ownable look; the tint code carries real meaning; matches the call-log heritage. | Tilted stamps misalign dense rows; no keyboard model; tints gimmicky if overused. | **Owner-picked base**, revised into v2. Original superseded. |
| **Carbon Copy v2** (`direction-carbon-copy-v2.html`) | Carbon Copy plus the Night Shift keyboard layer, calmer stamps, and Beige Box windows on brand moments only. | One system with a calm queue, a characterful ticket and a contained joke. | Tints and mono labels need discipline; two visual registers (ledger vs retro window) must stay separated. | **Selected** (D-023, owner, 2026-10-02). |

Published artifacts (all **private**: only the owner's account can open them, so the repo mockups are the durable copies):

- Beige Box: https://claude.ai/artifact/Kq3jpq1EEA7wNGz6LGXG8w
- Carbon Copy: https://claude.ai/artifact/4ioTi5WTYacq89QDPKpJTb
- Night Shift: https://claude.ai/artifact/4DfKAQdc3vQCuVK5VGSq33
- Carbon Copy v2: https://claude.ai/artifact/9CMFMLqRSp9WC5t7ySb84Z

# 10. Selected Direction

**Carbon Copy v2** (Decision, owner, 2026-10-02; D-023). A triplicate call-log form brought to the screen.

| Element | Decision |
| --- | --- |
| Base | Carbon Copy: light and dark themes, ruled ledger queue, form-header ticket view, tint-coded timeline. |
| Keyboard | Night Shift layer in Carbon Copy terms: ledger-ruled keycaps, j/k selection with margin marker, Ctrl/Cmd+K palette, bottom status bar. |
| Stamps | Calmer: straight, single border in queue rows; tilted with a stamp-down animation on the ticket view only. |
| Brand moments | Beige Box retro window and mascot palette on Admin all-caught-up, sign-in and 404 only (plus the style guide and README). |
| Portal | **Plain and product-led (Decision).** Light only in v1; the product's name, logo and accent; Plex Sans. TechStrap appears only as "Powered by TechStrap" with an optional 16px head mark. No carbon tints, stamps, ledger rules, windows or mascot. A portal screen that needs one of these for its own clarity is a new owner decision. |

Personality fit: the working screens are the "serious tools" (dense, ruled, calm); the retro window is the "cheeky frame" (section 3).

# 11. Typography

Fonts are **self-hosted** woff2 files served from each app (`wwwroot/fonts`, `font-display: swap`, Latin subset). **No Google Fonts or other CDN at runtime**: the portal is privacy-sensitive and Admin must work offline. All three families are SIL OFL; ship the licence text with the files. The mockup's Google Fonts `<link>` is a mockup shortcut only.

| Face | Weights | Where it is allowed |
| --- | --- | --- |
| **IBM Plex Sans** | 400, 500, 600 | UI chrome: body, nav, form controls, table cells, field values, ticket subject. The only face in the portal. |
| **IBM Plex Mono** | 400, 500, 600 | Ticket ids, field and column labels (uppercase), stamps, timestamps, tags, counters, `kbd`, Admin page titles and buttons, the palette, the status bar, brand-moment window headings. In the portal: the ticket id only. |
| **Source Serif 4** (variable, opsz 8..60) | 400, 600 | Message bodies and the reply composer in Admin, plus brand-moment body copy. Not used in the portal in v1 (**Assumption**: portal ticket-follow page uses Plex Sans). |

Banned: any other face; Mono for sentences longer than a line; Serif for UI chrome; pixel or terminal fonts (section 20). Fallbacks: Sans `system-ui, "Segoe UI", Arial, sans-serif`; Mono `ui-monospace, Consolas, monospace`; Serif `Georgia, serif`.

## Type scale (from the v2 mockup)

| Role | Face | Size / line | Weight | Treatment |
| --- | --- | --- | --- | --- |
| Admin base text | Sans | 14 / 1.45 | 400 | `tabular-nums` on the body |
| Page title (Admin) | Mono | 18 | 600 | |
| Ticket problem line | Sans | 16 / 1.3 | 600 | form field 5 |
| Field value | Sans | 13 | 500 | single line, ellipsis |
| Message body | Serif | 15 / 1.55 | 400 | max 68ch |
| Composer text | Serif | 15 / 1.5 | 400 | |
| Ticket id (list) | Mono | 13 | 500 | |
| Timestamp, hint, tag | Mono | 11 to 12 | 400 | |
| Label (column, field, card header) | Mono | 10 | 500 | uppercase, tracking .06 to .1em |
| Message header, button | Mono | 11 to 12 | 500 to 600 | uppercase |
| Stamp, ticket / queue | Mono | 11 / 10 | 600 | uppercase, tracking .08 / .07em |
| `kbd` | Mono | 11 / 1.5 | 500 | |
| Brand-moment heading / body | Mono / Serif | 19 / 15 | 600 / 400 | |
| Portal body, h1, input, button | Sans | 15, 26, 16, 16 | 400, 600, 400, 600 | inputs stay 16px (no mobile zoom) |
| Portal ticket id | Mono | 15 | 600 | |

# 12. Color

Source of truth: the `:root` block of the v2 mockup. Dark values apply under `prefers-color-scheme: dark` unless the user chose a theme. **Assumption:** the mockup's `data-theme` attribute maps to Bootstrap's `data-bs-theme`, and Admin ships Light / Dark / Auto (default Auto). Every text-on-background pair below was computed on 2026-10-02 and meets WCAG AA (4.5:1) in both themes; keep an automated test for this in the token work (P02-T05).

## Surface, ink and structure tokens

| Token | Role | Light | Dark | Bootstrap 5 mapping |
| --- | --- | --- | --- | --- |
| `--paper` | Page background | `#F7F5EE` | `#0B1E40` | `$body-bg` |
| `--sheet` | Raised sheet: ledger, cards, inputs, customer message | `#FFFFFF` | `#10285A` | `$card-bg`, `$input-bg` |
| `--rail` | Left navigation rail | `#EFEBDD` | `#08172F` | custom |
| `--head` | Header bars: filter bar, ledger header, card header, status bar | `#E8EDF8` | `#0E2650` | `$card-cap-bg` |
| `--ink` | Primary text | `#14213D` | `#E8EFFF` | `$body-color` |
| `--ink-2` | Secondary text, labels | `#44506B` | `#B7C6E6` | `$secondary-color` |
| `--ink-3` | Tertiary text, placeholders, unassigned | `#5B667E` | `#9DAECF` | `$tertiary-color` |
| `--rule` | Ledger row rules | `#C5D0E6` | `#274A87` | `$border-color` |
| `--rule-strong` | Sheet and control borders | `#8E9FC4` | `#4A71B8` | `$input-border-color`, `$card-border-color` |
| `--margin` | Red margin line, selected-nav marker | `#D9262E` | `#FF6B73` | custom |
| `--hover` | Row and nav hover | `#EAF0FB` | `#163670` | `$table-hover-bg` |
| `--sel` | Selected row, nav item, palette item | `#DDE8FA` | `#1C4286` | `$table-active-bg` |
| `--overlay` | Spam-row hatch | `rgba(20,33,61,.06)` | `rgba(255,255,255,.04)` | custom |
| `--shadow` | Hard offset shadow colour | `rgba(20,33,61,.14)` | `rgba(0,0,0,.4)` | `$box-shadow` colour |
| `--scrim` | Palette backdrop | `rgba(20,33,61,.45)` | `rgba(0,0,0,.6)` | `$modal-backdrop-bg` |
| `--accent` | Links, TechStrap primary (Admin) | `#1D4FA8` | `#8FC0FF` | `$primary`, `$link-color` |
| `--on-accent` | Text on accent | `#FFFFFF` | `#0B1E40` | `color-contrast($primary)` |
| `--focus` | Focus ring (3px) and selected-row ring | `#B3141C` | `#FFD84A` | `$focus-ring-color` |

Mapping column is **Assumption**-level (the mockup is plain CSS; P02-T05 confirms the Bootstrap variables). In the mockup, Admin buttons are ink-filled, not accent-filled; `--accent` is for links and portal controls (**Assumption**: Admin primary action is ink fill; confirm in the UX brief).

**Contrast exception (found in P02-T06, pinned by `TokenContrastTests`):** `--ink-3` on `--sel` in the dark theme is 4.32:1, below AA. Never place tertiary text (placeholders, "unassigned") on a selected row; use `--ink-2` there. Every other text pair in this section reaches 4.5:1 in both themes.

## Carbon tint tokens

| Token | Role | Light | Dark | Bootstrap |
| --- | --- | --- | --- | --- |
| `--canary` | Public-reply sheet, reply composer, avatar fill, status-bar message | `#FFF4B0` | `#3A3A1B` | custom |
| `--canary-edge` | Public-reply edge, composer border | `#C9B23A` | `#8C8A3A` | custom |
| `--pink` | Internal-note sheet and note composer | `#FFE0E3` | `#4A1E33` | custom |
| `--pink-edge` | Internal-note dashed edge | `#D26E7B` | `#C46A86` | custom |
| `--note-ink` | Internal-note text, label, warning, send button | `#7A1022` | `#FFC9D6` | custom |

## Status and priority tokens

| Token | Meaning | Light | Dark | Bootstrap |
| --- | --- | --- | --- | --- |
| `--st-new` | New: untriaged, needs a first look | `#1D5FB8` | `#8FC0FF` | `$info` (**Assumption**) |
| `--st-open` | Open: being worked. Also the presence dot | `#14702F` | `#6EE29A` | `$success` |
| `--st-pending` | Pending: waiting on someone else. Also High priority | `#8A5300` | `#F6C12B` | `$warning` |
| `--st-solved` | Solved: resolved, can reopen | `#1E5A6B` | `#86DCEB` | custom |
| `--st-closed` | Closed: archived, inert | `#5B6475` | `#AAB6D0` | `$secondary` |
| `--st-spam` | Spam, Urgent priority, destructive | `#B3141C` | `#FF9AA0` | `$danger` |

Rules: status is **label plus stamp shape plus colour**, never colour alone. Priority is a square marker plus a word: Urgent = `--st-spam`, High = `--st-pending`, Normal and Low = `--ink-2`; Low has a dashed marker. Spam rows also get a hatched ground and a double-border "Spam?" stamp. Red-family tokens (`--margin`, `--focus` in light, `--st-spam`) carry different roles; never use one for another's job.

## The carbon tint code (hard rule)

| Tint | Meaning | Non-colour cues |
| --- | --- | --- |
| **White** (`--sheet`) | Customer message | "customer" label, grey left bar (`--ink-3`) |
| **Canary** (`--canary`) | Public reply: the customer sees it | "agent reply" label, canary left bar |
| **Pink** (`--pink`) + **dashed edge** + **notched corner** | Internal note: the customer never sees it | "INTERNAL NOTE" label, dashed border, clipped top-right corner, composer warning "the customer will NOT see this note" |

This code **never changes meaning and is never reused** for decoration, status, tags, banners or anything else. A new feature that needs a tint takes a different token and shape. A legend (Customer / Public reply / Internal note) is shown under the timeline.

## Brand-moment tokens (retro window and mascot only)

| Token | Role | Light | Dark |
| --- | --- | --- | --- |
| `--bm-plate` | Window body (beige plate) | `#EFDDBB` | `#1A2548` |
| `--bm-edge` | 2px window outline | `#0B1F4B` | `#6F84B8` |
| `--bm-bar` | Title bar | `#0B1F4B` | `#0A1430` |
| `--bm-on-bar` | Title-bar text, LED ring | `#EFDDBB` | `#EFDDBB` |
| `--bm-text` | Window text | `#0B1F4B` | `#F1E6CC` |
| `--bm-text2` | Window fine print | `#434E6C` | `#B7C2DE` |
| `--bm-crt` | Window button fill (CRT blue) | `#3B95E0` | `#6FB4F2` |
| `--bm-on-crt` | Text on CRT blue | `#0B1F4B` | `#06152E` |
| `--bm-led` | Title-bar LED | `#22A447` | `#3DDC6B` |
| `--bm-shadow` | Hard window shadow | `#0B1F4B` | `#05070E` |

`--bm-*` tokens are valid **only** inside brand-moment windows. Never on a working screen or in the portal.

## Portal tokens (light only in v1)

| Token | Role | Value | Bootstrap |
| --- | --- | --- | --- |
| `--p-bg` | Portal background | `#FFFFFF` | `$body-bg` (portal build) |
| `--p-soft` | Soft panels | `#F5F5F7` | `$light` |
| `--p-ink` | Text | `#1B1B22` | `$body-color` |
| `--p-ink2` | Secondary text | `#4A4A57` | `$secondary-color` |
| `--p-line` | Borders | `#D4D4DC` | `$border-color` |
| `--accent`, `--on-accent`, `--accent-ink` | Product-supplied; see section 22 | per product | `$primary`, button text, `$link-color` |

# 13. Geometry

- **Square corners** in Admin: `border-radius: 0` on sheets, inputs, `kbd` and buttons. The only exceptions are the irregular stamp corners (`3px 6px 3px 5px / 5px 3px 6px 3px`), the note label, and the round presence dot and LED. The portal uses small radii (4 to 6px) as a neutral product UI.
- **Ledger rules:** 1px `--rule` between rows, 42px minimum row height, 2px `--rule-strong` under the header.
- **Red margin line:** 2px `--margin` at 70% opacity, 30px from the left edge of the queue ledger. Hidden under 820px.
- **Borders:** 1px `--rule-strong` on sheets; 2px on the segmented control, palette and retro window; 6px coloured left bar on messages.
- **Hard offset shadow:** `3px 3px 0 var(--shadow)` on sheets and the ledger, `2px 2px 0` on cards and messages, `4px 4px 0` on the palette. Never blurred. Internal notes carry no shadow (they read as a cut-out).
- **Dashed edges** mean "off the record": dashed rail dividers, dashed internal-note border, dashed unassigned avatar. Do not use dashes for anything else.
- **Focus:** 3px solid `--focus`, 1px offset, on every interactive element.

# 14. Composition & Layout

- **Admin shell:** 208px left rail (brand, views with counts, manage, signed-in agent) plus main. Under 820px the rail becomes a horizontal strip and the ledger collapses to two-line rows. Under 1100px the product column is hidden and the properties card stacks under the thread.
- **Queue:** filter bar (labelled Mono selects plus search), title row with keyboard hint, then the ledger: marker, No., Subject with tags, Product, Requester, Status stamp, Priority, Assignee initials, Last activity.
- **Ticket view:** breadcrumb, numbered form header (fields 1 to 5), presence line with the status stamp, timeline (customer, system events, replies, notes), legend, composer; a 300px sticky properties column (Properties, Requester, Linked KB article).
- **Density:** dense and plain. No hero blocks, no oversized padding. Message line length capped at 68ch.
- **Status bar:** Admin only. Sticky bottom strip with keycap hints and a transient message; hints hide under 900px.
- **Brand-moment screens:** one centred retro window, 400px max, on the plain page background. Nothing competes with it.
- **Portal:** 640px single column, product bar with a 6px accent top border, product name and logo top left, plain forms, "Powered by TechStrap" footer.

# 15. Imagery

- **Mascot:** a retro beige CRT in a jockstrap with tube socks, thumbs up. Two forms: the **head mark** (`assets/brand/mark*.png`: Admin rail at 36px, favicon, avatars, 16px portal footer, 96px inside the retro window) and the **full figure** (`assets/brand/logo*.png`: README, style guide and sign-in only; unreadable below about 64px). The 404 uses the head mark.
- **Current format is PNG.** Auto-traced SVGs (cleaner scaling, themeable) are pending in task P02-T08; until then use the PNGs generated by `scripts/brand/generate-brand-assets.py`. Never redraw or recolour the mascot by hand.
- The mascot never appears on working screens (queue, ticket, composer, forms, settings), in the portal body, in emails, or beside an error. Product logos in the portal are the product's own.
- No stock photography or stock illustration, no decorative blobs or hero art. Screenshots (README, docs) show the real app.

# 16. Iconography

- **No stock icon set is brand language.** The visual vocabulary is typographic: Mono labels, stamps, square priority markers, the margin-line selection mark (`▸`), keycaps and initials boxes.
- Where a functional icon is genuinely needed (attachment, external link), use one minimal outline set at 16px in `--ink-2`, with a text label or `aria-label`. Choose it in PHASE-07 and record it here (**Assumption**: default to Bootstrap Icons, subset and self-hosted).
- Icons never carry status or the tint code alone. No floppy disks, cassettes or other nostalgia props (section 7).
- Avatars are square initials boxes (1.5px `--ink` border, canary fill); unassigned is dashed and empty.

# 17. Motion

Minimal, mechanical, always optional.

| Motion | Where | Spec |
| --- | --- | --- |
| **Stamp-down** | Status stamp on the ticket view when status changes | 0.35s ease-out; scale 1.6 to 0.95 to 1, opacity 0 to 1, ends at the -2deg tilt |
| **Presence blink** | Dot beside "X is replying" | 1.4s infinite, opacity to .25 at 50% |
| **Palette open** | Command palette | Appears with a scrim; no slide or fade (**Assumption**); focus moves to its input and is restored on close |
| **Button press** | Brand-moment buttons | 1px translate with shadow shrink |

`prefers-reduced-motion: reduce` disables all animation and transition. State is never conveyed by motion alone. No scanlines, flicker, glow or CRT effects (section 7).

# 18. UI Components

## Keyboard conventions (Admin)

Single-key shortcuts, shown in `kbd` keycaps beside the control they trigger.

| Key | Context | Action |
| --- | --- | --- |
| `j` / `ArrowDown` | Queue | Select next row (margin marker `▸` and 2px focus ring) |
| `k` / `ArrowUp` | Queue | Select previous row |
| `Enter` | Queue (focus on page body) | Open the selected ticket |
| `/` | Queue, ticket | Focus the queue search |
| `r` | Ticket | Focus the composer in public-reply mode |
| `n` | Ticket | Focus the composer in internal-note mode |
| `e` | Ticket | Focus the assignee control |
| `Esc` | Ticket | Back to the queue |
| `Esc` | In a field | Leave the field |
| `Ctrl/Cmd + Enter` | In the composer | Send reply or add note |
| `Ctrl/Cmd + K` | Anywhere in Admin | Open or close the command palette |

- **Never while typing.** Single-key shortcuts do not fire when focus is in an input, textarea, select or contenteditable, nor when Ctrl, Cmd or Alt is held. While typing, only `Esc` (leave field) and `Ctrl/Cmd+Enter` (in the composer) work.
- **Command palette:** form-sheet dialog (`--sheet`, 2px `--ink` border, 4px hard shadow, Mono "COMMAND PALETTE" header), 520px max. Mono filter input with word-AND matching; ledger-ruled list, margin marker on the active item, shortcut in a `kbd`; footer hints `↑↓ select`, `↵ run`, `Esc close`. `role="dialog"`, `aria-modal`, combobox and listbox with `aria-activedescendant`; focus is trapped and restored. Every shortcut has a palette command, so the palette is the discoverability layer.
- **Status bar:** persistent in Admin, with keycap hints and a transient message area (canary chip, `role="status"`, cleared after about 3.5s) for confirmations such as "Assigned ACME-142 to Sam". Errors that block work are never left to the status bar alone.
- **`kbd` styling:** Mono 11px 500, square, 1px `--rule-strong` border with a 2px bottom edge, `--sheet` fill. Inside buttons: transparent fill, `currentColor` border. Keys in a chord sit 2px apart.
- The portal has no keyboard layer beyond standard browser behaviour and visible focus.

## Core components

- **Stamps (status):** Mono uppercase badge in the status colour. **Queue:** straight, 10px, single 1.5px border, no inner ring. **Ticket view:** 11px, 2px border plus an inner hairline ring, tilted -2deg, stamp-down on change. **Spam:** double 3px border, "Spam?" in the queue (straight), tilted +2deg on the ticket.
- **Buttons (Admin):** Mono 12px 600 uppercase, 2px `--ink` border, ink fill; the alternate button has a sheet fill. Minimum height 34px.
- **Segmented control:** public reply / internal note, 2px ink border, pressed = ink fill. Switching to note recolours the composer pink and dashed, relabels send as "Add internal note" and shows the warning that the customer will not see it.
- **Cards:** sheet, 1px border, 2px hard shadow, header bar with a Mono uppercase label.
- **Messages:** the tint code (section 12); Serif body; Mono uppercase header with the time on the right.
- **System events:** Mono 12px, dotted left rule, inline `code` for tag names.

## Brand moments (retro window)

Allowed **only** on Admin all-caught-up, agent sign-in, Admin 404 and the style guide. README and GitHub use the mascot and palette as images, not the window component. The queue, the ticket and the portal never use it.

**Window anatomy:** beige plate (`--bm-plate`), 2px `--bm-edge` outline, hard 3px `--bm-shadow`, square corners. Title bar: `--bm-bar`, Mono 12px 600 in `--bm-on-bar`, a 9px green LED (`--bm-led`) and a short title. No close, minimise or maximise buttons, no bevels, no other fake OS chrome. Body: centred 96px head mark, Mono 19px heading, Serif 15px copy (max 34ch), optional Mono 12px fine print, then one `bm-btn` (CRT blue, 2px edge, 2px hard shadow) or one underlined link.

| Screen | Title bar | Heading and copy (tone examples) | Action |
| --- | --- | --- | --- |
| All caught up | `queue.exe — 0 items` | "All caught up" / "Zero tickets, fully supported." | View open tickets |
| Sign-in | `techstrap — sign in` | "Agent sign-in" / "Single sign-on through your company's Authentik. No passwords are entered here." | Sign in with Authentik |
| 404 | `ERROR 404 — not found` | "This page fell out of its strap." / "The address you followed doesn't match any page here. It may have moved, or the link may have a typo." | Back to the queue |

Tone: one wink per window, in the heading or one line; the explanatory sentence is plain. Consent and security lines on sign-in stay plain. Always offer the way back. Window copy must still follow the voice rules in section 3.

## Portal (decision: plain and product-led)

- Product name, logo and accent lead. TechStrap appears only as "Powered by TechStrap" (a link to https://github.com/Syntax-Circus/techstrap), optionally with the 16px head mark; an installation setting can hide it (D-024).
- **No** carbon tints, stamps, ledger rules, margin line, retro window, mascot, Mono labels, keyboard layer, status bar or hard shadows in the portal. Exception only if a portal screen needs one for its own clarity and the owner approves. Ticket status shown to customers is a plain text label.
- Light only in v1 (`--p-*` tokens). Inputs 16px, 44px minimum touch target, focus ring 3px `--p-ink` plus a 5px accent halo.
- Customer emails follow the portal: product branding, the same accent rule, the same footer line, no mascot.

# 19. Characteristic Motifs

Together these make Admin recognisable: (1) ruled ledger rows with a red margin line; (2) the numbered form header (fields 1 to 5), **ticket view only**; (3) rubber-stamp status badges, straight in lists and tilted with stamp-down on the ticket view only; (4) the white / canary / pink timeline with a perforated, notched pink note; (5) square corners with a hard offset shadow; (6) ledger-ruled keycaps and the status bar; (7) dashed "off the record" edges. The beige retro window with the mascot is the **frame motif**, confined to brand moments.

---

# 20. Things We Do Not Do

In addition to the defaults in `DESIGN.md` §7 (generic centered hero, three-card feature rows, glassmorphism, gradient blobs, pill-everything, bento grids and similar), TechStrap does not:

- Put the mascot, a pun or TechStrap colours on any customer-facing page or email. The customer's relationship is with the product, not with us.
- Use humour in working screens, blocking errors, destructive confirmations, or legal and security copy.
- Make crude, sexual or body-based jokes. The pun is affectionate or it is cut.
- Imitate a real operating system (Windows 95 chrome, system sounds) or use CRT effects over working surfaces. The brand-moment window title bar is the single, contained exception.
- Use pixel, terminal or "retro" display fonts for working text or customer-facing text.
- Trade density for personality: no large decorative illustrations, hero blocks or oversized padding in queues and ticket detail.
- Rely on colour alone to tell public replies from internal notes, or statuses from each other; shape and label carry meaning too.
- Use the mascot's palette as a portal theme. Portal accents come from the product, with a derived on-accent colour to keep contrast.
- Write product-marketing copy ("powerful", "modern", "AI-powered", "seamless") anywhere, including the README.
- Add brand moments without an owner decision; the list in section 3 is closed.

- Tilt stamps in dense lists, or use a double border except for spam. Queue stamps are straight and single-border.
- Reuse a carbon tint (white, canary, pink, dashed edge, notched corner) for any meaning other than customer, public reply and internal note.
- Show the numbered form header anywhere except the ticket view.
- Put the mascot, retro window or `--bm-*` tokens on working screens, the portal or emails. No Win95 parody chrome (bevels, minimise/maximise buttons, taskbars) on working screens; the window title bar exists only inside brand moments.
- Add a brand moment, including a retro window on another empty or error state, without an owner decision.
- Fire a single-key shortcut while the user is typing, or add a shortcut without a palette entry.
- Load fonts, icons or scripts from a CDN at runtime.
- Use stock icon sets, stock photos or sparkle-style "AI" imagery as brand language.
- Let a product accent set anything beyond `--accent`, `--on-accent` and `--accent-ink`, or use `--accent` as text on white.

---

# 21. Reference Material

- Reference implementation: `docs/design/mockups/direction-carbon-copy-v2.html` (open it in a browser; the Brand moments tab shows the three windows). Index and history: `docs/design/directions.md`.
- Mascot and icon files: `assets/brand/` (see its README).
- Process and anti-pattern defaults: `_template/docs/DESIGN.md` (sections 6, 7, 11, 13, 14).
- Decision record: D-023 in `docs/architecture/04-DECISION-LOG.md`.

# 22. Design Tokens

Implementation target (P02-T05): the `--*` tokens in section 12 become CSS custom properties on `:root` (Admin) with dark overrides, and Bootstrap 5 SCSS variables are set in each app's `_tokens.scss` per the mapping column. Admin and Portal share `_brand-tokens.scss`; Admin loads the full set, the Portal loads only the `--p-*` set plus the product accent properties. Tokens that have no Bootstrap equivalent stay plain CSS custom properties.

## Product-accent override rule (Portal, and product-branded emails)

A product (PHASE-04) stores one accent colour as `#RRGGBB`. From it exactly three properties are derived and set at runtime on the portal root. SCSS is never recompiled.

| Property | Meaning | Rule |
| --- | --- | --- |
| `--accent` | Fills and borders: top bar, primary button, logo tile, suggestion edge | The product's colour, as entered |
| `--on-accent` | Text on an accent fill | Whichever of `#FFFFFF` or `#000000` has the higher WCAG contrast against the accent. Pure black (not the `#1B1B22` body ink) is deliberate: with white/black the better of the two is always at least 4.58:1 for any colour, so every valid accent is usable |
| `--accent-ink` | Accent used as **text** or an outline on white: links, ghost buttons, nav hover | If the accent already has at least 4.5:1 against `#FFFFFF`, use it unchanged. Otherwise darken it until it does: repeatedly scale R, G and B by (1 - 0.04 k), k = 1, 2, 3 and so on, until contrast is at least 4.5:1 |

Naming: this table uses the unprefixed mockup names; the implementation prefixes them with `--ts-` (`--ts-accent`, `--ts-on-accent`, `--ts-accent-ink`).

An accent may set **only** these three. It may not change `--p-*` neutrals, typography, radii, shadows, focus colour, or any Admin token. Focus stays `--p-ink` with an accent halo. Nothing may use `--accent` as text on white; use `--accent-ink`.

- **Validation at save:** reject only values that are not valid `#RRGGBB`. No contrast rejection is needed: the white-or-black on-accent always reaches at least 4.58:1 (worst case around `#4B7D87`), and `--accent-ink` is darkened to 4.5:1 on white. A test asserts both properties across a colour sweep.
- **Single implementation:** one pure function, living in a layer both the product handler and the email renderer can reach (name and location in PHASE-04), takes the accent and returns the three values or a failure. It is **enforced at product save time (PHASE-04)** and **reused by email rendering (PHASE-05)**. The portal reads the derived values; it never recomputes them a second way. The JavaScript in the mockup is a reference for the maths only.
- **Test vectors** (computed from the rule above, not from the mockup's hand-picked values): `#7C3AED` gives on-accent `#FFFFFF`, ink `#7C3AED`; `#F59E0B` gives `#000000`, ink `#9D6507`; `#0F3D2E` gives `#FFFFFF`, ink `#0F3D2E`; `#2E9AFF` gives `#000000`, ink `#2375C2`; `#4B7D87` (worst case) gives `#FFFFFF` at 4.58:1.
- **Assumption:** the mockup darkens until 7:1; this file uses the owner-stated 4.5:1 (AA, normal text). The mockup's inline Orbitly `--accent-ink` (`#5B21B6`) is a hand-picked value and is superseded.
- **Assumption:** the 6px top bar and logo tile are decorative; the accent need not reach 3:1 against white for them, but a product name and nav text must be present so the page never relies on the accent to identify the product.

# 23. Responsive Identity

- Admin: designed desktop-first, usable on a tablet and a phone for triage (rail becomes a strip, ledger becomes two-line rows, hints hide under 900px).
- Portal: mobile-first, single column, 16px inputs, 44px targets, no horizontal scroll.
- Brand-moment windows stay centred and shrink to the viewport (full width under 400px) and the three-up preview in the style guide stacks under 760px.
- Identity that survives small screens: Mono labels, stamps and tint code. Identity that is dropped: margin line, numbered header columns collapse to two per row.

# 24. Accessibility

Target: **WCAG 2.2 AA** (decided).

- Contrast: every token pair in section 12 meets 4.5:1 for text in both themes; verify in tests, and re-verify when a token changes.
- Visible focus: 3px `--focus` ring on every interactive element; never remove it.
- Reduced motion: respected (section 17).
- **Keyboard:** all shortcuts are additive; every action is also reachable by tab and enter. Single-key shortcuts never fire while typing, which also satisfies WCAG 2.1.4 (a way to avoid accidental activation).
- **Meaning is never colour alone:** status = label + stamp; priority = marker + word; the tint code adds labels, dashes and a notch.
- Live regions: the status bar message and the composer warning use `role="status"`. The palette is a modal dialog with focus trap and restore.
- Touch targets: 44px in the portal; Admin controls at least 30px.

# 25. Logo-Removal Test

**Result: pass.** With the mascot and name removed, Admin is still recognisable by: ruled ledger rows with a red margin line; tilted rectangular rubber-stamp badges on the ticket view; the white / canary / pink timeline with a perforated, notched pink internal note; ledger-ruled keycaps and the form-sheet palette. No other helpdesk looks like a stack of carbon forms. Confidence: high in Admin. The **portal fails the test by design**: it is the product's, and not ours to make recognisable.

# 26. Art-Direction Review Checklist

Before merging UI work, confirm:

- [ ] Tokens only: no hard-coded hex, no new shadow or radius.
- [ ] Tint code used only for customer / public reply / internal note.
- [ ] Stamps are straight and single-border in lists; tilted only on the ticket view.
- [ ] Numbered form header appears only on the ticket view.
- [ ] No mascot, retro window or `--bm-*` outside the three Admin brand moments and the style guide.
- [ ] Portal and emails: product-led, light only, no TechStrap element other than the footer line.
- [ ] Shortcuts do not fire while typing; every shortcut is in the palette and status bar.
- [ ] Contrast, focus, reduced motion and 320px width checked in both themes.
- [ ] Copy follows the voice rules; no humour outside brand moments.
- [ ] Logo-removal test (section 25) still passes.

# 27. Rules for AI Agents

Before significant visual work: read `DESIGN.md` and this file, and respect the surface boundaries (section 8) and voice rules (section 3). Then:

1. Use the tokens in section 12. Never invent a colour, shadow or radius, and never copy hex values out of a mockup.
2. The tint code is fixed: white customer, canary public reply, pink dashed notched internal note. Never reuse a tint for anything else.
3. Stamps: straight and single-border in lists; tilt and stamp-down only on the ticket view.
4. The mascot, the retro window and `--bm-*` appear only on Admin all-caught-up, sign-in, 404, the style guide, and README/GitHub. Nowhere else, including empty states other than all-caught-up, errors and the portal.
5. The portal stays plain and product-led: only the product's accent (three derived properties, section 22) and the "Powered by TechStrap" footer. Do not carry Admin styling into it.
6. Single-key shortcuts must never fire while typing; add every new shortcut to the palette and the status bar hints.
7. Self-host fonts. Never add a CDN, Google Fonts link or telemetry to either app.
8. The mockups are reference, not code to paste. If this file is silent, stop and ask, or run the exploration; do not default to generic Bootstrap or SaaS styling.

# Brand Summary

TechStrap is a plain, fast helpdesk with a wink at the door. **Carbon Copy v2**: a call-log ledger with rubber-stamped statuses, a white / canary / pink carbon tint code, square sheets with hard offset shadows, IBM Plex Sans / Mono and Source Serif 4, and a keyboard-first layer. The mascot and a beige retro window appear only on three Admin brand moments. Customers see the product, never us, except one "Powered by TechStrap" line.
