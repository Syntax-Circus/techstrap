// The browser's IANA time zone (for example "Europe/London"), for LocalTimeService. The Admin shows every time in the agent's own zone, and the server has
// no other source: the API stores UTC and the profile has no zone (D-042). The Intl object is always handed in, so resolveZone is pure and
// tests/TechStrap.Admin.Tests/js/tz.test.mjs can run it under node:test. Nothing here may throw: a browser without Intl, or one that hides its zone,
// answers null and the page keeps showing UTC.
//
// LocalTimeService (.NET) imports this module once per circuit and calls zone().

/** The zone name Intl reports, or null when there is none (no Intl, an unreadable value, or an empty string). Never throws. */
export function resolveZone(intl) {
    try {
        const zone = intl.DateTimeFormat().resolvedOptions().timeZone;
        return typeof zone === 'string' && zone.length > 0 ? zone : null;
    } catch {
        return null;
    }
}

/** Called once by LocalTimeService after the first interactive render. */
export function zone() {
    return resolveZone(typeof Intl === 'undefined' ? null : Intl);
}
