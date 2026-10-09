import http from 'k6/http';
import { check } from 'k6';
import { forwarded, nextIp } from './lib/clients.js';
import { ticket } from './lib/payloads.js';
import { record, baseThresholds } from './lib/checks.js';

const PORTAL = (__ENV.TS_PORTAL_URL || 'http://localhost:8082').replace(/\/+$/, '');
const PRODUCT = __ENV.TS_PRODUCT_KEY || 'orbitly';

export const options = {
  thresholds: Object.assign({}, baseThresholds, { 'http_req_duration{name:portal-form}': ['p(95)<500'] }),
};

// Finds the <input ...> tag whose name attribute equals `name` and returns its value attribute, or null.
export function inputValue(html, name) {
  const tags = html.match(/<input\b[^>]*>/g) || [];
  for (let i = 0; i < tags.length; i++) {
    if (tags[i].indexOf('name="' + name + '"') !== -1) {
      const v = /\bvalue="([^"]*)"/.exec(tags[i]);
      return v ? v[1] : null;
    }
  }
  return null;
}

export function portalForm() {
  const ip = nextIp();
  const url = PORTAL + '/p/' + PRODUCT + '/contact';
  const page = http.get(url, { headers: forwarded(ip), tags: { name: 'portal-form-get' } });
  const antiforgery = inputValue(page.body || '', '__RequestVerificationToken');
  const submitId = inputValue(page.body || '', 'Form.SubmitId');
  const hasTokens = check(page, { 'contact form has the antiforgery token and Form.SubmitId': () => antiforgery !== null && submitId !== null });
  record(page, 200);
  if (!hasTokens) {
    return;
  }
  const t = ticket(__VU * 100003 + __ITER);
  // The per-VU cookie jar carries the antiforgery cookie; the PRG redirect is followed automatically.
  const res = http.post(url, {
    _handler: 'contact',
    __RequestVerificationToken: antiforgery,
    'Form.SubmitId': submitId,
    'Form.Name': t.name,
    'Form.Email': t.email,
    'Form.Subject': t.subject,
    'Form.Body': t.body,
    'Form.Website': '',
  }, { headers: forwarded(ip), tags: { name: 'portal-form' } });
  record(res, 200);
  if (res.status !== 429) {
    check(res, { 'redirected to /contact/received': (r) => /\/contact\/received(\?|$)/.test(r.url) });
  }
}

export default function () {
  portalForm();
}
