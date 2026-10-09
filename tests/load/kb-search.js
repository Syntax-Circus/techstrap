import http from 'k6/http';
import { forwarded, nextIp } from './lib/clients.js';
import { record, baseThresholds } from './lib/checks.js';

const BASE = (__ENV.TS_BASE_URL || 'http://localhost:8080').replace(/\/+$/, '');
const PRODUCT = __ENV.TS_PRODUCT_KEY || 'orbitly';
// Terms from the seeded dev articles. Responses are cached for 60 s, so repeated terms mostly measure the cache.
const TERMS = ['welcome', 'password', 'dark mode', 'reset'];

export const options = {
  thresholds: Object.assign({}, baseThresholds, { 'http_req_duration{name:kb-search}': ['p(95)<500'] }),
};

export function kbSearch() {
  const term = TERMS[(__VU + __ITER) % TERMS.length];
  const res = http.get(BASE + '/api/public/kb/' + PRODUCT + '/search?q=' + encodeURIComponent(term) + '&page=1&pageSize=10', {
    headers: forwarded(nextIp()),
    tags: { name: 'kb-search' },
  });
  record(res, 200);
}

export default function () {
  kbSearch();
}
