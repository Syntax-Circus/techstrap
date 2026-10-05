// Runs with `node --test` (no browser, no jsdom): the script is evaluated in a vm context whose window and document are plain objects.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';
import vm from 'node:vm';
import { THEMES, writePreference } from '../../../src/TechStrap.Admin/wwwroot/js/preferences.js';

const source = readFileSync(new URL('../../../src/TechStrap.Admin/wwwroot/js/theme-init.js', import.meta.url), 'utf8');

const memoryStorage = (initial = {}) => {
    const items = new Map(Object.entries(initial));
    return {
        items,
        getItem: (key) => (items.has(key) ? items.get(key) : null),
        setItem: (key, value) => items.set(key, String(value)),
    };
};

const page = (initial = {}) => {
    const attributes = new Map(Object.entries(initial));
    return {
        attributes,
        documentElement: {
            setAttribute: (name, value) => attributes.set(name, value),
            removeAttribute: (name) => attributes.delete(name),
        },
    };
};

/** Runs the script as the browser would: once, as a classic script, with no module scope and no exports. */
const run = (globals) => vm.runInNewContext(source, globals);

describe('theme-init.js', () => {
    it('sets data-bs-theme for a stored light or dark choice', () => {
        for (const theme of ['light', 'dark']) {
            const document = page();
            run({ window: { localStorage: memoryStorage({ 'techstrap.admin.theme': theme }) }, document });
            assert.equal(document.attributes.get('data-bs-theme'), theme);
        }
    });

    it('removes the attribute for auto, so the system preference decides', () => {
        const document = page({ 'data-bs-theme': 'dark' });
        run({ window: { localStorage: memoryStorage({ 'techstrap.admin.theme': 'auto' }) }, document });
        assert.equal(document.attributes.has('data-bs-theme'), false);
    });

    it('removes the attribute when nothing is stored or the stored value is not a theme', () => {
        for (const stored of [{}, { 'techstrap.admin.theme': 'purple' }, { 'techstrap.admin.theme': '' }, { 'techstrap.admin.theme': 'LIGHT' }]) {
            const document = page({ 'data-bs-theme': 'dark' });
            run({ window: { localStorage: memoryStorage(stored) }, document });
            assert.equal(document.attributes.has('data-bs-theme'), false, JSON.stringify(stored));
        }
    });

    it('never throws when the storage is blocked, and treats it as auto', () => {
        const blocked = { getItem: () => { throw new Error('SecurityError: storage is blocked'); } };
        const document = page({ 'data-bs-theme': 'dark' });
        assert.doesNotThrow(() => run({ window: { localStorage: blocked }, document }));
        assert.equal(document.attributes.has('data-bs-theme'), false);
    });

    it('never throws when reading window.localStorage itself throws', () => {
        const window = {};
        Object.defineProperty(window, 'localStorage', { get() { throw new Error('SecurityError'); } });
        assert.doesNotThrow(() => run({ window, document: page() }));
    });

    it('never throws when there is no window, no document, or the element refuses the change', () => {
        assert.doesNotThrow(() => run({}));
        assert.doesNotThrow(() => run({ window: { localStorage: memoryStorage({ 'techstrap.admin.theme': 'dark' }) } }));
        const refusing = { documentElement: { setAttribute: () => { throw new Error('frozen'); }, removeAttribute: () => { throw new Error('frozen'); } } };
        assert.doesNotThrow(() => run({ window: { localStorage: memoryStorage({ 'techstrap.admin.theme': 'dark' }) }, document: refusing }));
    });

    it('is a classic script: no import, no export, no top-level await', () => {
        assert.doesNotMatch(source, /^\s*(import|export)\s/m);
        assert.doesNotMatch(source, /\bawait\b/);
    });

    it('reads the key preferences.js writes, for every theme, so the two cannot drift apart', () => {
        for (const theme of THEMES) {
            const storage = memoryStorage();
            assert.equal(writePreference(storage, 'theme', theme), true);
            const document = page({ 'data-bs-theme': 'light' });
            run({ window: { localStorage: storage }, document });
            const expected = theme === 'auto' ? undefined : theme;
            assert.equal(document.attributes.get('data-bs-theme'), expected, theme);
        }
    });
});
