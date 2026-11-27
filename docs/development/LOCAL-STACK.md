# Local stack (.NET Aspire)

One command starts a working, signed-in, seeded TechStrap on your machine: Postgres, Mailpit (mail capture), Authentik (sign-in), a webhook catcher and the four TechStrap hosts. Mail and
webhooks stay on your machine. The only things it needs from outside are the downloads (NuGet packages, container images and the client libraries the build restores).

This guide has been verified on Windows only.

It is for local development only. It is never deployed: UAT and production use `deploy/docker-compose.yml` ([DEPLOYMENT.md](../self-hosting/DEPLOYMENT.md)). The code is in
`tools/TechStrap.AppHost` and `tools/TechStrap.WebhookCatcher`; the decision is D-059 in the [decision log](../architecture/04-DECISION-LOG.md).

## Prerequisites

- Docker, running.
- The .NET SDK that `global.json` names.
- The Aspire CLI 17.0. On Windows: `winget install Microsoft.Aspire`. For other systems see <https://aspire.dev>. Check with `aspire --version`.
- A trusted .NET development certificate, once per machine: `dotnet dev-certs https --trust`. The Api, Admin and Portal use https, and they call each other and are opened in your browser with that certificate.
- PowerShell 7.2 or later, for the reset script.

You do not need any `.env` file, user secret or identity provider of your own.

## Start and stop

From the repository root (`aspire.config.json` points the CLI at the AppHost):

```bash
aspire run
```

The terminal prints the dashboard link, which carries a login token. Use that link. The first run downloads images and generates secrets, and Authentik takes about a minute before it is
healthy. Everything is ready when the dashboard shows each resource healthy. `sample-client` stays stopped until you start it.

Ctrl+C stops the AppHost. `aspire start` runs it in the background instead, and `aspire stop` stops it.

**Stopping the AppHost does not stop the containers.** Postgres, Mailpit and the two Authentik containers are persistent: they keep running (and keep their ports) after Ctrl+C or `aspire stop`, so the next
start is quick. To stop them, run `docker ps` and `docker stop <name>` for this stack's four containers. Their names end in the same eight-character suffix (for example `postgres-0123abcd`). Other Aspire projects use
the same base names, so check the suffix against the network named `aspire-persistent-network-<suffix>-TechStrap`.

The AppHost runs only in a Debug build and throws in Release.

## Addresses

| What | Address | Notes |
| --- | --- | --- |
| Aspire dashboard | <http://localhost:55200> | Use the login link from the terminal |
| Admin (agents) | <https://localhost:55240> | Sign-in works only on https. The http port (55241) is not for signing in. |
| Portal (customers) | <https://localhost:55250> | For example `/p/orbitly`. Also <http://localhost:55251> |
| API | <https://localhost:55230> | `/health/ready`. Also <http://localhost:55231> |
| Worker (health only) | <http://localhost:55260> | `/health/ready` |
| Mailpit (captured mail) | <http://localhost:55221> | SMTP on 55220 |
| Webhook catcher | <http://localhost:55270> | |
| Authentik | <http://localhost:55290> | Issuer `http://localhost:55290/application/o/techstrap/` |
| Postgres | `localhost:55210` | Databases `techstrap` and `authentik`, user `techstrap` |

The other ports of the block are 55201 (dashboard telemetry intake) and 55202 (resource service). The whole block is 55200-55299; the numbers are in `tools/TechStrap.AppHost/LocalStackPorts.cs`. The
dashboard may show other, random ports for the .NET apps in their logs: Aspire puts a proxy on the fixed port in front of each.

## Sign in

| User | Authentik group | What you get in the Admin |
| --- | --- | --- |
| `sam` | `techstrap-admins` | The seeded admin, Sam Whitfield |
| `riley` | `techstrap-agents` | The seeded agent, Riley Chen |
| `casey` | none | The no-access page: "You don't have access to TechStrap." |

`akadmin` is Authentik's own administrator (sign in at <http://localhost:55290>).

The four passwords are fixed, fake, development-only values in the `Parameters` section of `tools/TechStrap.AppHost/appsettings.Development.json`. Never use them anywhere else. The Postgres password, the Authentik
secret key and the Admin's client secret are different: they are generated on the first run and kept in `~/.aspire/secrets/<UserSecretsId>/` (the id is in the AppHost's project file).

Sam and Riley sign in **as** the agents the seeder created, because the blueprint gives them the subjects `dev|sam` and `dev|riley` and the same names and email addresses (sign-in overwrites the agent row
with these). If you change one of them, sign-in creates a second agent or renames the seeded one. Sign out ends the Authentik session too, so the next sign-in asks for the password again.

The provider is defined by `tools/TechStrap.AppHost/Authentik/blueprints/techstrap.yaml`. It differs on purpose from the [deployment guide](../self-hosting/AUTHENTIK.md) (`sub_mode: user_upn`) and must never be applied to a
deployment.

## What the apps are told

All four hosts run in Development with `DotEnv__Enabled=false`, so **your `.env` and `.env.local` files have no effect here**, whether they are in a project folder or at the repository root. `docker compose` and a
plain `dotnet run` still read them. Sentry is blanked, mail always goes to Mailpit and there is no switch that sends it anywhere else.

The settings come from the committed `appsettings*.json` and from `tools/TechStrap.AppHost/HostWiring.cs`. Stored files and the Admin and Portal key rings go under `local/.state/` (git-ignored).

## Demo data

The development seeder is on by default (what it creates: [DEV-DATA.md](DEV-DATA.md)). To start without it:

```bash
dotnet user-secrets set "LocalStack:SeedDevData" false --project tools/TechStrap.AppHost
```

The value must be `true` or `false`; anything else stops the AppHost with a message. `dotnet user-secrets remove "LocalStack:SeedDevData" --project tools/TechStrap.AppHost` returns to the default. Turning it off
deletes nothing: reset the stack if you want an empty database. With it off, the sample client gets no API key and logs a warning.

The seed has no knowledge base pictures or product logos. To try one, upload it in the Admin's article editor.

## The sample client

`sample-client` is the console sample of the [client SDK](CLIENT-SDK.md). It does not start by itself. Start it from the dashboard, or:

```bash
aspire resource sample-client start
```

It files one ticket for Orbitly with the seeded trusted key, prints the ticket number and the Portal link, and exits (the dashboard then shows it as stopped). The confirmation to the requester and the new-ticket alert
to an agent appear in Mailpit.

With the demo data off, a database that was never seeded has no products. In the Admin, create a product first, then an API key for it, and run the sample yourself:

```bash
dotnet run --project samples/TechStrap.Client.Samples.Console -- --base-address https://localhost:55230 --api-key <key>
```

## Webhook capture

The Worker sends **every** webhook delivery to the catcher instead of its real destination, as all mail goes to Mailpit.

1. In the Admin as `sam`, open **Webhooks**, choose **Add destination**, pick a format, enter a URL and **Save webhooks**.
2. Choose **Test saved destination**, then open <http://localhost:55270> (**Refresh deliveries** in the Admin shows the outcome).

Only the checks on the URL itself still apply: https, no user name, password or `#` fragment, a host that contains a dot and does not end in `.localhost`, and, when the host is written as an IP address, a public one.
Something like `https://example.com/hook` works, and nothing is sent to it.

**Capture does not check where a host name leads.** In a deployment the sender resolves the name when it connects, and blocks it if any of its addresses is not public ([WEBHOOKS.md](WEBHOOKS.md)). Capture replaces that
part of the sender, so names are not resolved here. A name that resolves to a private address, or does not resolve at all, shows `Sent` locally but would be blocked or fail in a deployment.

The catcher shows the
time, the original **host** only (webhook paths carry tokens), the event id, the content type and the body. It also has `/api/requests` (JSON) and a Clear button. It keeps the newest 200 in memory,
refuses bodies over 64 KiB, forgets everything when it restarts, and has no sign-in, so it runs only in Development and accepts only local requests.

How it works: the setting `TECHSTRAP_WEBHOOK_CAPTURE_URL` (set only by the AppHost) replaces the transport under the real sender, after the URL checks above. The source of the webhook policy and sender is unchanged, and shipped builds behave as before. The code exists **only in Debug builds**. A
Release Worker refuses to start if the setting is present, so it can never quietly deliver to real destinations. A Debug Worker accepts it only in Development, as plain http to a loopback address.

CI builds Release only, so the tests of the capture behavior do not run there. Run them locally with `-c Debug` after you change this area:

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Debug --filter-class "*WebhookCaptureTests"   # needs Docker
dotnet test --project tests/TechStrap.Api.Tests -c Debug --filter-class "*WorkerWebhookCaptureTests"
dotnet test --project tests/TechStrap.Architecture.Tests -c Debug --filter-class "*WebhookCaptureAbsenceTests"
```

## Telemetry

The apps send logs and traces to the dashboard. **Each app appears under two names:** traces and spans as `techstrap-api`, `techstrap-admin`, `techstrap-portal` and `techstrap-worker`; structured logs as `api`, `admin`,
`portal` and `worker`. Search under both. (Metrics have not been checked.)

On a fresh database the Api logs one `ERR` line, a failed `SELECT` from `__EFMigrationsHistory`. EF Core is looking for a table that does not exist yet; the migrations then run. It is harmless, and it does not appear on later
starts.

## Reset

`scripts/Reset-LocalStack.ps1` returns the stack to a clean slate. **All local data goes.** Stop the AppHost first (the script refuses while port 55200 is in use); the containers may still be running.

```powershell
./scripts/Reset-LocalStack.ps1 -WhatIf    # list what would be removed
./scripts/Reset-LocalStack.ps1            # asks before each removal
./scripts/Reset-LocalStack.ps1 -Force     # no questions (needed in a non-interactive shell)
./scripts/Reset-LocalStack.ps1 -Secrets   # also delete the generated secrets
```

It removes this stack's four containers (captured mail goes with Mailpit), its persistent network, the volumes `techstrap-local-postgres` and `techstrap-local-authentik-media` (the TechStrap and Authentik databases and
Authentik's media) and `local/.state/` (attachments, pictures and key rings).

`-Secrets` also deletes the generated secrets folder, so the next start makes new ones. It does so only when the database volume is gone, because a new Postgres password cannot open an old volume. Your own AppHost user
secrets (such as `LocalStack:SeedDevData`) are stored elsewhere and are never touched.

It touches nothing else: no other project's containers, networks, volumes or secrets, and not the `docker compose` stacks.

**It stops before removing anything** when it cannot be sure: a selected container does not mount what this stack's container mounts (or mounts another named volume), there is more than one TechStrap AppHost suffix, something
else uses one of the volumes or the network, or `local/.state` is a link. The message names the problem. For containers it cannot prove are this stack's (a first start that failed halfway can cause this, for example when only
Mailpit exists), it prints the manual steps: check each with `docker inspect --type container <name>`; if they are this stack's leftovers, `docker rm -f <names>` (without `-v`) and run the script again; if any belongs to another
project, leave it and remove this stack's own containers, network and volumes by hand.

### Changing a password or the blueprint

Authentik applies the blueprint when its file changes. **Restarting the `authentik-worker` does not re-apply an unchanged file**, so do not expect a restart to pick up a changed password or secret.

- **A changed password** (edit `appsettings.Development.json`, or override it with `dotnet user-secrets set "Parameters:authentik-sam-password" <value> --project tools/TechStrap.AppHost`): stop the AppHost, run
  `Reset-LocalStack.ps1`, start again. The reset removes Authentik's database, so the blueprint is applied to an empty one with the new value. `-Secrets` is not needed for this. **You lose all local data, not only
  Authentik's:** the reset also deletes the TechStrap database (tickets, articles and everything else), `local/.state/` (attachments, pictures and key rings) and the captured mail.
- **New generated secrets:** `Reset-LocalStack.ps1 -Secrets`. This is the same full reset, with the same data loss. Do not delete or edit single generated secrets while the volume exists.
- **An edited blueprint file:** restart the `authentik-worker` resource, or run `docker exec authentik-worker-<suffix> ak apply_blueprint custom/techstrap.yaml`.

## Smoke test

An opt-in test boots the real stack and checks it. It never runs in CI. Docker must be running, no AppHost may be running (it checks the application ports first), and the demo data must be on.

```powershell
$env:TECHSTRAP_LOCALSTACK_SMOKE = '1'
dotnet test --project tests/TechStrap.LocalStack.Tests -c Release
Remove-Item Env:TECHSTRAP_LOCALSTACK_SMOKE
```

It starts the AppHost with `dotnet run` (in Debug, whatever the tests were built as), checks Authentik's discovery document, the Api and Worker health, a Portal product page and the Admin's redirects to sign-in, files one
ticket and waits for the requester's confirmation in Mailpit. It then stops the AppHost; the persistent containers keep running, and the ticket stays in your database. It does not sign in. On failure it prints the path of the
AppHost log. Without the variable it is skipped, and the other tests of that project run as usual.

## Manual check

The smoke test does not sign in. After a change to sign-in, the blueprint or the wiring, check in your own browser, from a fresh reset:

1. Every resource is healthy.
2. `sam` signs in and sees the admin pages; **Agents** lists exactly Riley Chen and Sam Whitfield. `riley` signs in and has no admin pages. `casey` gets the no-access page.
3. After **Sign out**, signing in asks for the password again.
4. After a restart of the AppHost, there are still two agents.
5. The sample client's ticket shows in Mailpit; a webhook test shows in the catcher; a picture uploaded to an article shows in the Admin preview and on the Portal; the dashboard shows logs and traces.

## Relation to Docker Compose

`docker compose up` (the root [README](../../README.md#quick-start)) still works and has not changed. It builds the four images and has no identity provider, so signing in to its Admin needs one of your own. It reads `.env` and
`.env.local`; the Aspire stack does not. Their fixed ports do not overlap (compose uses 8080-8083 and 8025), and their volumes are separate.

**A plain `dotnet run` has changed.** Each of the four hosts now has a launch profile called `aspire`, which this stack uses, and `dotnet run --project src/TechStrap.<Host>` picks it up too. Without other arguments the host now
starts in **Development** (so it reads `.env.local`) on its 552xx ports. The profile's `ASPNETCORE_ENVIRONMENT=Development` replaces a value you set in your shell. The Api, Admin and Portal profiles bind https as well as http,
so they need the trusted development certificate unless you pass `--urls` with only `http://` addresses (the Worker's profile is http only). `--urls` overrides the ports, so `dotnet run --project src/TechStrap.Admin --urls
http://localhost:8081` listens on 8081 only. `dotnet run --no-launch-profile` skips the whole profile, the environment included.

## Limits and troubleshooting

- **One stack per machine.** Every clone and git worktree of this repository uses the same volume names, the same user secrets id (so the same generated secrets and your own settings) and the same ports. Run one at a time, and
  stop its containers before you start another copy. `local/.state` is per folder, so a second copy sees the same database without the first one's stored files. Each folder also gets its own container suffix, and the reset script
  refuses when it finds two; remove one copy's containers and network by hand first. A reset deletes the shared volumes, but it clears `local/.state` only in the copy you run it from; the other copies keep theirs.
- **Another stack on `localhost`.** Browsers do not separate cookies by port. Another local stack with its own Authentik on `localhost` (or another app on these cookie names) can replace this one's session and cause a sign-in loop or a
  sign-out. Use a separate browser profile or a private window.
- **A port is already in use.** Something else, often this stack's own containers or a second copy, holds a 552xx port.
- **Authentik is unhealthy for the first minute.** It is healthy only after the worker has applied the blueprint. If it stays that way, read the `authentik-worker` logs.
- **Certificate errors,** or the Admin cannot reach the API: run `dotnet dev-certs https --trust` and restart the browser.
- **The reset says the stack is running:** stop the AppHost (`aspire stop`); the containers can stay.
