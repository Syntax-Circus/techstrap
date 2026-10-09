import { baseThresholds } from '../lib/checks.js';
import { share, DURATION } from '../lib/profile.js';
import { customerView, captureViewToken } from '../customer-view.js';

export { intakeTrusted } from '../intake-trusted.js';
export { intakePublic } from '../intake-public.js';
export { kbSearch } from '../kb-search.js';
export { portalForm } from '../portal-form.js';

// Sustained mix of TS_RATE requests per second: 50% trusted intake, 10% public intake, 30% KB search,
// 10% customer view, plus the Portal form at one request a minute (the form is limited to 5 per 10 minutes
// per IP; with 60 rotated IPs each IP submits about once an hour).
export function setup() {
  return { token: captureViewToken() };
}

export function customerViewScenario(data) {
  customerView(data.token);
}

export const options = {
  scenarios: {
    intakeTrusted: share(0.5, 'intakeTrusted'),
    intakePublic: share(0.1, 'intakePublic'),
    kbSearch: share(0.3, 'kbSearch'),
    customerView: share(0.1, 'customerViewScenario'),
    portalForm: {
      executor: 'constant-arrival-rate',
      rate: 1,
      timeUnit: '1m',
      duration: DURATION,
      preAllocatedVUs: 2,
      maxVUs: 10,
      exec: 'portalForm',
    },
  },
  thresholds: Object.assign({}, baseThresholds, {
    server_errors: ['rate==0'],
    // http_req_failed is deliberately not asserted: a 429 is the correct answer under the rate limits (see README),
    // so the budget is p95 under 500 ms and zero server errors (5xx or no response), as in the spike.
    checks: ['rate>0.99'],
    'http_req_duration{name:intake-trusted}': ['p(95)<500'],
    'http_req_duration{name:intake-public}': ['p(95)<500'],
    'http_req_duration{name:kb-search}': ['p(95)<500'],
    'http_req_duration{name:customer-view}': ['p(95)<500'],
    'http_req_duration{name:portal-form}': ['p(95)<500'],
  }),
};
