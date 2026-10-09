import http from 'k6/http';
import { TRUSTED_KEY, keyHeaders } from './lib/auth.js';
import { forwarded, nextIp } from './lib/clients.js';
import { ticket, idempotencyKey } from './lib/payloads.js';
import { record, baseThresholds } from './lib/checks.js';

const BASE = (__ENV.TS_BASE_URL || 'http://localhost:8080').replace(/\/+$/, '');

export const options = {
  thresholds: Object.assign({}, baseThresholds, { 'http_req_duration{name:intake-trusted}': ['p(95)<500'] }),
};

// Posts one trusted (server key) intake call from the given client IP and returns the response.
// `phase` tags the request so the spike composer can split its metrics.
export function postIntake(ip, phase) {
  const n = __VU * 100003 + __ITER;
  const headers = keyHeaders(TRUSTED_KEY, Object.assign({
    'Content-Type': 'application/json',
    'Idempotency-Key': idempotencyKey(),
  }, forwarded(ip)));
  const tags = phase ? { name: 'intake-trusted', phase: phase } : { name: 'intake-trusted' };
  const res = http.post(BASE + '/api/intake/tickets', JSON.stringify(ticket(n)), { headers: headers, tags: tags });
  record(res, 201, phase);
  return res;
}

export function intakeTrusted() {
  const res = postIntake(nextIp());
  if (res.status === 201) {
    res.json('ticketNumber');
  }
}

export default function () {
  intakeTrusted();
}
