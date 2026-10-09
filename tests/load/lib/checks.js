import { check } from 'k6';
import { Rate, Counter } from 'k6/metrics';

export const serverErrors = new Rate('server_errors');
export const throttled = new Counter('throttled_429');

// A 429 is a valid answer under the rate limits; a 5xx never is.
export function record(res, expected, phase) {
  serverErrors.add(res.status >= 500 || res.status === 0);
  if (res.status === 429) {
    throttled.add(1, { phase: phase || 'steady' });
  }
  const label = 'status is ' + expected;
  return check(res, { [label]: (r) => r.status === expected || r.status === 429 });
}

export const baseThresholds = {
  http_req_duration: ['p(95)<500'],
  server_errors: ['rate==0'],
};
