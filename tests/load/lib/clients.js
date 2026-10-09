// Client identity rotation: the Api and Portal rate-limit per client IP, so each request carries a synthetic
// X-Forwarded-For (honoured only when the runner is a trusted proxy; see README).
export const PREFIX = __ENV.TS_FORWARDED_PREFIX || '10.99.0.';
const requested = Number(__ENV.TS_IP_COUNT || 60);
export const IP_COUNT = Math.min(240, Math.max(1, Number.isFinite(requested) ? Math.floor(requested) : 60));
export const SPIKE_IP = PREFIX + '250';

export function clientIp(n) {
  return PREFIX + (1 + (n % IP_COUNT));
}

export function forwarded(ip) {
  return { 'X-Forwarded-For': ip };
}

// Spreads concurrent VUs over the address range.
export function nextIp() {
  return clientIp(__VU * 100003 + __ITER);
}
