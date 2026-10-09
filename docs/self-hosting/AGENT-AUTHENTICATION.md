# Agent authentication

Agents sign in with your OpenID Connect provider. The TechStrap Api validates the access token (JWT) and lets a person in only when the token carries one of two groups (D-004, D-029). This page is the short reference for what the token must contain; the setup lives elsewhere:

- [AUTHENTIK.md](AUTHENTIK.md): a complete worked example with Authentik.
- [SELF-HOSTING.md](SELF-HOSTING.md): the provider-neutral OIDC contract and every environment key.

## Claim requirements

The access token (not only the ID token) must carry:

| Claim | Requirement |
| --- | --- |
| `sub` | Required. Identifies the agent. |
| `email` | Required. A token without it is refused (`agent-email-required`). |
| `groups` | Required. A list of group names containing `TECHSTRAP_AGENT_GROUP` (default `techstrap-agents`) or `TECHSTRAP_ADMIN_GROUP` (default `techstrap-admins`). Set `TECHSTRAP_GROUP_CLAIM_TYPE` if your provider names the claim differently, for example `roles`. |
| `aud` | The Admin client id, which is the value of `Authentication__JwtBearer__Audiences__0`. |
| `iss` | The issuer, exactly as in `AUTH__AUTHORITY` and `Authentication__JwtBearer__Authority`, trailing slash included. |

## Settings

| Setting | Default | Meaning |
| --- | --- | --- |
| `Authentication__JwtBearer__Authority` | (required) | Your issuer URL, for example `https://auth.example.com/application/o/techstrap/` |
| `Authentication__JwtBearer__Audiences__0` | (required) | The audience in TechStrap's access tokens (the Admin client id) |
| `TECHSTRAP_AGENT_GROUP` | `techstrap-agents` | Members can work tickets |
| `TECHSTRAP_ADMIN_GROUP` | `techstrap-admins` | Members can also manage products, API keys, tags and agents |
| `TECHSTRAP_GROUP_CLAIM_TYPE` | `groups` | The token claim that lists group names |

Roles come from these groups only. To make someone an admin, add them to the admin group in your identity provider; to remove access, remove them from both groups or turn them off in TechStrap's agent list. The first admin is simply the first person in the admin group to sign in.

- Group names may contain spaces (for example `TechStrap Admins`). The setting must match the group name exactly as your identity provider emits it, apart from letter case.
- The agent group and the admin group must be different, otherwise the Api will not start.
- Some providers, such as Entra ID and Keycloak, need configuration to put `email` and the groups claim in the access token.
- The group settings must hold the value your provider emits in the claim. Entra ID emits group object IDs, not names, so set the settings to those IDs.
- Recovery: if every admin is deactivated, add a fresh account to the admin group in your identity provider and sign in with it.
