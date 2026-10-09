// Keys are read from the environment; the defaults are the public dev keys seeded by TECHSTRAP_SEED_DEV_DATA (not secrets).
// Never log either value: keys travel in the X-Api-Key header only.
export const TRUSTED_KEY = __ENV.TS_TRUSTED_KEY || 'tsk_devOrbitlyServerKeyNotASecret00000000000000';
export const PUBLIC_KEY = __ENV.TS_PUBLIC_KEY || 'tsp_devOrbitlyAppKeyNotASecret00000000000000000';

export function keyHeaders(key, extra) {
  return Object.assign({ 'X-Api-Key': key }, extra || {});
}
