// Runs with `node --test` (no browser): resolveZone in tz.js takes the Intl object, so a test hands it a plain object.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { resolveZone, zone } from '../../../src/TechStrap.Admin/wwwroot/js/tz.js';

const intlWith = (timeZone) => ({ DateTimeFormat: () => ({ resolvedOptions: () => ({ timeZone }) }) });

describe('resolveZone', () => {
    it('returns the zone Intl reports', () => {
        assert.equal(resolveZone(intlWith('Europe/London')), 'Europe/London');
        assert.equal(resolveZone(intlWith('UTC')), 'UTC');
    });

    it('returns null for an empty or a non-string zone', () => {
        for (const value of ['', undefined, null, 5, {}]) {
            assert.equal(resolveZone(intlWith(value)), null, String(value));
        }
    });

    it('never throws: no Intl, an Intl that throws, or one that hides the zone', () => {
        assert.equal(resolveZone(null), null);
        assert.equal(resolveZone(undefined), null);
        assert.equal(resolveZone({}), null);
        assert.equal(resolveZone({ DateTimeFormat: () => { throw new RangeError('no zone'); } }), null);
        assert.equal(resolveZone({ DateTimeFormat: () => ({ resolvedOptions: () => { throw new Error('blocked'); } }) }), null);
    });
});

describe('zone', () => {
    it('answers the zone of the real Intl, as a string that Intl itself accepts', () => {
        const name = zone();
        assert.equal(typeof name, 'string');
        assert.doesNotThrow(() => new Intl.DateTimeFormat('en-GB', { timeZone: name }));
    });
});
