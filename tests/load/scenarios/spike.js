import { postIntake } from '../intake-trusted.js';
import { SPIKE_IP, nextIp } from '../lib/clients.js';

const PEAK = Number(__ENV.TS_SPIKE_PEAK || 100);

// Spike: a ramp to TS_SPIKE_PEAK requests per second against the trusted intake from ONE client IP, so the
// per-key-and-IP limit (120 per 60 s) is crossed and 429s are the correct answer. Failed-request counts
// (http_req_failed) are therefore not asserted here: a 429 is a valid response under the limit, a 5xx is not.
// The recovery phase uses rotated IPs, because the pinned IP stays throttled for the rest of its window; it
// proves the service answers within budget again once the spike has passed.
export function spikeCall() {
  postIntake(SPIKE_IP, 'spike');
}

export function recoveryCall() {
  postIntake(nextIp(), 'recovery');
}

export const options = {
  scenarios: {
    spike: {
      executor: 'ramping-arrival-rate',
      startRate: 5,
      timeUnit: '1s',
      stages: [
        { target: PEAK, duration: '10s' },
        { target: PEAK, duration: '60s' },
        { target: 0, duration: '5s' },
      ],
      preAllocatedVUs: 200,
      maxVUs: 500,
      exec: 'spikeCall',
    },
    recovery: {
      executor: 'constant-arrival-rate',
      rate: 5,
      timeUnit: '1s',
      startTime: '75s',
      duration: '30s',
      preAllocatedVUs: 10,
      maxVUs: 50,
      exec: 'recoveryCall',
    },
  },
  thresholds: {
    server_errors: ['rate==0'],
    'throttled_429{phase:spike}': ['count>0'],
    'http_req_duration{phase:recovery}': ['p(95)<500'],
  },
};
