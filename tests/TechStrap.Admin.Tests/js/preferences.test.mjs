// Runs with `node --test` (no browser, no jsdom): the storage and the page are passed in as plain objects.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { DEFAULTS, THEMES, applyTheme, normaliseTheme, readPreferences, writePreference } from '../../../src/TechStrap.Admin/wwwroot/js/preferences.js';

const memoryStorage = (initial = {}) => {
    const items = new Map(Object.entries(initial));
    return {
        items,
        getItem: (key) => (items.has(key) ? items.get(key) : null),
        setItem: (key, value) => items.set(key, String(value)),
    };
};
const brokenStorage = () => ({
    getItem: () => { throw new Error('SecurityError: storage is blocked'); },
    setItem: () => { throw new Error('QuotaExceededError'); },
});
const page = () => {
    const attributes = new Map();
    return {
        attributes,
        documentElement: {
            setAttribute: (name, value) => attributes.set(name, value),
            removeAttribute: (name) => attributes.delete(name),
        },
    };
};

describe('readPreferences', () => {
    it('returns the defaults for an empty storage', () => {
        assert.deepEqual(readPreferences(memoryStorage()), { singleKeyShortcuts: true, theme: 'auto' });
        assert.deepEqual(DEFAULTS, { singleKeyShortcuts: true, theme: 'auto' });
    });

    it('returns the defaults when there is no storage at all', () => {
        assert.deepEqual(readPreferences(null), { singleKeyShortcuts: true, theme: 'auto' });
        assert.deepEqual(readPreferences(undefined), { singleKeyShortcuts: true, theme: 'auto' });
    });

    it('never throws when the storage is blocked and falls back to the defaults', () => {
        assert.deepEqual(readPreferences(brokenStorage()), { singleKeyShortcuts: true, theme: 'auto' });
    });

    it('reads stored values', () => {
        const storage = memoryStorage({ 'techstrap.admin.singleKeyShortcuts': 'false', 'techstrap.admin.theme': 'dark' });
        assert.deepEqual(readPreferences(storage), { singleKeyShortcuts: false, theme: 'dark' });
    });

    it('treats an unrecognised stored value as the default', () => {
        const storage = memoryStorage({ 'techstrap.admin.singleKeyShortcuts': 'maybe', 'techstrap.admin.theme': 'sepia' });
        assert.deepEqual(readPreferences(storage), { singleKeyShortcuts: true, theme: 'auto' });
    });

    it('keeps the shortcuts on for anything but the exact string false', () => {
        for (const value of ['true', '', '0', 'FALSE', 'no']) {
            assert.equal(readPreferences(memoryStorage({ 'techstrap.admin.singleKeyShortcuts': value })).singleKeyShortcuts, true, value);
        }
    });
});

describe('writePreference', () => {
    it('stores the shortcut toggle as true or false', () => {
        const storage = memoryStorage();
        assert.equal(writePreference(storage, 'singleKeyShortcuts', false), true);
        assert.equal(storage.items.get('techstrap.admin.singleKeyShortcuts'), 'false');
        assert.equal(writePreference(storage, 'singleKeyShortcuts', true), true);
        assert.equal(storage.items.get('techstrap.admin.singleKeyShortcuts'), 'true');
    });

    it('stores each theme and round-trips through readPreferences', () => {
        for (const theme of THEMES) {
            const storage = memoryStorage();
            assert.equal(writePreference(storage, 'theme', theme), true);
            assert.equal(readPreferences(storage).theme, theme);
        }
    });

    it('refuses an unknown key or value and stores nothing', () => {
        const storage = memoryStorage();
        assert.equal(writePreference(storage, 'colour', 'red'), false);
        assert.equal(writePreference(storage, 'theme', 'sepia'), false);
        assert.equal(writePreference(storage, 'theme', undefined), false);
        assert.equal(writePreference(storage, 'singleKeyShortcuts', 'false'), false);
        assert.equal(writePreference(storage, 'singleKeyShortcuts', 0), false);
        assert.equal(writePreference(storage, '__proto__', 'x'), false);
        assert.equal(writePreference(storage, 'toString', 'x'), false);
        assert.equal(storage.items.size, 0);
    });

    it('returns false instead of throwing when the storage is full, blocked or missing', () => {
        assert.equal(writePreference(brokenStorage(), 'theme', 'dark'), false);
        assert.equal(writePreference(null, 'theme', 'dark'), false);
        assert.equal(writePreference(undefined, 'singleKeyShortcuts', true), false);
    });
});

describe('applyTheme', () => {
    it('sets data-bs-theme for light and dark', () => {
        const doc = page();
        assert.equal(applyTheme(doc, 'dark'), 'dark');
        assert.equal(doc.attributes.get('data-bs-theme'), 'dark');
        assert.equal(applyTheme(doc, 'light'), 'light');
        assert.equal(doc.attributes.get('data-bs-theme'), 'light');
    });

    it('removes the attribute for auto so the system preference decides', () => {
        const doc = page();
        applyTheme(doc, 'dark');
        assert.equal(applyTheme(doc, 'auto'), 'auto');
        assert.equal(doc.attributes.has('data-bs-theme'), false);
    });

    it('treats an unknown theme as auto and never writes it to the page', () => {
        const doc = page();
        applyTheme(doc, 'dark');
        assert.equal(applyTheme(doc, 'sepia'), 'auto');
        assert.equal(doc.attributes.has('data-bs-theme'), false);
        assert.equal(normaliseTheme(null), 'auto');
    });

    it('never throws without a page or with a page that refuses', () => {
        assert.equal(applyTheme(null, 'dark'), 'dark');
        assert.equal(applyTheme({}, 'dark'), 'dark');
        const hostile = { documentElement: { setAttribute: () => { throw new Error('nope'); }, removeAttribute: () => { throw new Error('nope'); } } };
        assert.equal(applyTheme(hostile, 'dark'), 'dark');
        assert.equal(applyTheme(hostile, 'auto'), 'auto');
    });
});
