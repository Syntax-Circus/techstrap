# Agent authentication

Agents sign in with your OpenID Connect provider. The TechStrap API validates the access token (JWT) and lets a person in only when
the token carries one of two groups (D-004, D-029):

| Setting | Default | Meaning |
| --- | --- | --- |
| `Authentication__JwtBearer__Authority` | (required) | Your issuer URL, for example `https://auth.example.com/application/o/techstrap/` |
| `Authentication__JwtBearer__Audiences__0` | (required) | The audience in TechStrap's access tokens (usually the client id) |
| `TECHSTRAP_AGENT_GROUP` | `techstrap-agents` | Members can work tickets |
| `TECHSTRAP_ADMIN_GROUP` | `techstrap-admins` | Members can also manage products, API keys, tags and agents |
| `TECHSTRAP_GROUP_CLAIM_TYPE` | `groups` | The token claim that lists group names |

Roles come from these groups only. To make someone an admin, add them to the admin group in your identity provider; to remove
access, remove them from both groups or turn them off in TechStrap's agent list. The first admin is simply the first person in the
admin group to sign in.

The token must also carry `sub` and `email`; TechStrap refuses sign-in without an email (`agent-email-required`).

## Authentik example

1. Create an OAuth2/OpenID provider for TechStrap. Use the scopes `openid`, `email` and `profile`.
2. Add a scope mapping that includes the user's groups in a `groups` claim, and attach it to the provider.
3. Create the groups `techstrap-agents` and `techstrap-admins` and add people to them.
4. Set `Authentication__JwtBearer__Authority` to the provider's issuer and `Authentication__JwtBearer__Audiences__0` to its client id.

Other providers work the same way: release `sub`, `email` and a groups claim, and set `TECHSTRAP_GROUP_CLAIM_TYPE` if the claim
is not called `groups` (for example `roles`).
