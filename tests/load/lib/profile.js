const RATE = Number(__ENV.TS_RATE || 20);
export const DURATION = __ENV.TS_DURATION || '10m';

// A constant-arrival-rate executor for one share of the total TS_RATE requests per second.
export function share(weight, exec) {
  const r = RATE * weight;
  const rate = r >= 1 ? { rate: Math.round(r), timeUnit: '1s' } : { rate: 1, timeUnit: Math.max(1, Math.round(1 / r)) + 's' };
  return Object.assign({
    executor: 'constant-arrival-rate',
    preAllocatedVUs: 20,
    maxVUs: 200,
    duration: DURATION,
    exec: exec,
  }, rate);
}
