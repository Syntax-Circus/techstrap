import http from 'k6/http';
import { PUBLIC_KEY, keyHeaders } from './lib/auth.js';
import { forwarded, nextIp } from './lib/clients.js';
import { ticket, attachment, multipart } from './lib/payloads.js';
import { record, baseThresholds } from './lib/checks.js';

const BASE = (__ENV.TS_BASE_URL || 'http://localhost:8080').replace(/\/+$/, '');
const PRODUCT = __ENV.TS_PRODUCT_KEY || 'orbitly';

export const options = {
  thresholds: Object.assign({}, baseThresholds, { 'http_req_duration{name:intake-public}': ['p(95)<500'] }),
};

export function intakePublic() {
  const n = __VU * 100003 + __ITER;
  const t = ticket(n);
  const form = multipart(
    { Email: t.email, Name: t.name, Subject: t.subject, Body: t.body, Website: '' },
    __ITER % 4 === 0 ? attachment(n) : null,
    'Attachments');
  const res = http.post(BASE + '/api/public/products/' + PRODUCT + '/tickets', form.body, {
    headers: Object.assign(keyHeaders(PUBLIC_KEY, { 'Content-Type': form.contentType }), forwarded(nextIp())),
    tags: { name: 'intake-public' },
  });
  record(res, 201);
}

export default function () {
  intakePublic();
}
