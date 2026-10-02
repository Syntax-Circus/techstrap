# Security policy

## Reporting a vulnerability

Please report security problems privately through GitHub:

1. Open the repository's **Security** tab.
2. Choose **Report a vulnerability** (GitHub private vulnerability reporting).
3. Describe the issue, the affected version and how to reproduce it.

Do not open a public issue or pull request for a suspected vulnerability. You will get an acknowledgement
within a few days. Fixes are developed in a private advisory and released together with the advisory.

## Supported versions

TechStrap is pre-1.0. Until v1.0.0 only the latest release and the `main` branch receive security fixes.
After v1.0.0 this section will list the supported release lines.

## Scope

In scope:

- The API, Admin app, customer Portal and Worker in this repository, and the container images published
  to `ghcr.io/syntax-circus`.
- The Docker Compose files and example configuration shipped here, where they lead to an insecure
  default.
- The `TechStrap.Contracts`, `TechStrap.Client` and `TechStrap.Client.Maui` packages once published.

Out of scope:

- Vulnerabilities in third-party dependencies that are already public and fixed upstream (please report
  those upstream; tell us if a version bump is needed).
- Problems that need a misconfigured deployment, such as trusting `0.0.0.0/0` as a proxy network or
  exposing Postgres to the internet.
- Denial of service through volumetric traffic against a deployment without a reverse proxy or rate
  limits in front of it.
- Findings from automated scanners without a demonstrated impact.
