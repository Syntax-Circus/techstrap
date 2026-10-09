import http from 'k6/http';
import { TRUSTED_KEY, keyHeaders } from './lib/auth.js';
import { forwarded, clientIp, nextIp } from './lib/clients.js';
import { ticket } from './lib/payloads.js';
import { record, baseThresholds } from './lib/checks.js';

const BASE = (__ENV.TS_BASE_URL || 'http://localhost:8080').replace(/\/+$/, '');

export const options = {
  thresholds: Object.assign({}, baseThresholds, { 'http_req_duration{name:customer-view}': ['p(95)<500'] }),
};

// Creates one ticket through the trusted intake and returns the customer token taken from viewUrl (never logged).
export function captureViewToken() {
  const headers = keyHeaders(TRUSTED_KEY, Object.assign({
    'Content-Type': 'application/json',
    'Idempotency-Key': 'ts-load-setup-' + Date.now(),
  }, forwarded(clientIp(0))));
  const res = http.post(BASE + '/api/intake/tickets', JSON.stringify(ticket(0)), { headers: headers, tags: { name: 'intake-setup' } });
  if (res.status !== 201) {
    throw new Error('setup intake failed with status ' + res.status);
  }
  const match = /\/t\/([^/?#]+)/.exec(res.json('viewUrl'));
  if (!match) {
    throw new Error('viewUrl did not contain a ticket token');
  }
  return match[1];
}

export function customerView(token) {
  const res = http.get(BASE + '/api/customer/ticket', {
    headers: Object.assign({ 'X-Ticket-Token': token }, forwarded(nextIp())),
    tags: { name: 'customer-view' },
  });
  record(res, 200);
}

export function setup() {
  return { token: captureViewToken() };
}

export default function (data) {
  customerView(data.token);
}
