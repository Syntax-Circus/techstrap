# Load test results

This page records the runs of the k6 suite in `tests/load/` (see `tests/load/README.md`). The budgets: p95 under 500 ms for the intake, KB search,
customer view and Portal form calls, zero 5xx responses, and an email outbox that drains within 2 minutes of the end of a spike with no dead letters caused by load.
The first real run on the UAT box is recorded in PHASE-12c.

## Box specs

| Item | Value |
| --- | --- |
| Host | recorded in 12c (T12) |
| CPU | recorded in 12c (T12) |
| RAM | recorded in 12c (T12) |
| Disk | recorded in 12c (T12) |
| Docker version | recorded in 12c (T12) |
| Postgres version | recorded in 12c (T12) |
| Image tags | recorded in 12c (T12) |
| k6 version | recorded in 12c (T12) |

## Runs

| Date | Scenario | Rate | Duration | p95 | Server errors | 429s | Outbox drained | Dead letters | Result |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| no run recorded yet (first run is 12c T12) | | | | | | | | | |

## Tuning

Record with every run what was changed from the defaults, so a result can be reproduced:

- Database connection pool size.
- Rate-limit overrides (intake, public submit, public read, token access).
- Proxy settings (`REVERSE_PROXY_CIDR` and the trusted-proxy networks on the Api and Portal), and that they were removed again afterwards.
