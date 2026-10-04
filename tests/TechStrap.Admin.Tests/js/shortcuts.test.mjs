// Runs with `node --test` (no browser, no jsdom): the key filter in shortcuts.js is pure and takes plain objects.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { decide, isRelevantKey, isTyping } from '../../../src/TechStrap.Admin/wwwroot/js/shortcuts.js';

const element = (tagName, extra = {}) => ({ tagName, type: '', isContentEditable: false, getAttribute: () => null, ...extra });
const event = (key, extra = {}) => ({
    key, ctrlKey: false, metaKey: false, altKey: false, repeat: false, isComposing: false, defaultPrevented: false, ...extra,
});
const env = (extra = {}) => ({ active: null, dialogOpen: false, scope: null, queueOnScreen: false, ...extra });

describe('isTyping', () => {
    for (const type of ['text', 'search', 'email', 'password', 'number', '']) {
        it(`treats input type "${type}" as typing`, () => assert.equal(isTyping(element('INPUT', { type })), true));
    }

    for (const type of ['button', 'checkbox', 'radio', 'submit', 'reset', 'file', 'range', 'color', 'image']) {
        it(`does not treat input type "${type}" as typing`, () => assert.equal(isTyping(element('INPUT', { type })), false));
    }

    it('treats textarea and select as typing', () => {
        assert.equal(isTyping(element('TEXTAREA')), true);
        assert.equal(isTyping(element('SELECT')), true);
    });

    it('treats contenteditable "", "true" and "plaintext-only" as typing, and "false" as not', () => {
        for (const value of ['', 'true', 'plaintext-only']) {
            assert.equal(isTyping(element('DIV', { getAttribute: (name) => (name === 'contenteditable' ? value : null) })), true, value);
        }

        assert.equal(isTyping(element('DIV', { getAttribute: (name) => (name === 'contenteditable' ? 'false' : null) })), false);
    });

    it('treats an element inside an editable region as typing (isContentEditable is inherited)', () => {
        assert.equal(isTyping(element('SPAN', { isContentEditable: true })), true);
    });

    it('is false for plain elements, the body and nothing', () => {
        assert.equal(isTyping(element('DIV')), false);
        assert.equal(isTyping(element('BODY')), false);
        assert.equal(isTyping(null), false);
    });
});

describe('isRelevantKey', () => {
    it('accepts the keys the layer maps, case-insensitively for letters', () => {
        for (const key of ['j', 'J', 'k', 'ArrowDown', 'ArrowUp', 'Enter', '/', 'r', 'n', 'e', 'u', '?', 'Escape']) {
            assert.equal(isRelevantKey(key), true, key);
        }
    });

    it('rejects every other key', () => {
        for (const key of ['a', 'Tab', 'Shift', 'F5', ' ', 'x']) {
            assert.equal(isRelevantKey(key), false, key);
        }
    });
});

describe('decide', () => {
    it('reports a mapped key when nothing is focused', () => {
        const result = decide(event('j'), env());

        assert.equal(result.payload.key, 'j');
        assert.equal(result.payload.typing, false);
        assert.equal(result.payload.onBody, true);
    });

    it('reports u (Not spam) in either case when nothing is focused, and nothing while typing or in a dialog', () => {
        assert.equal(decide(event('u'), env()).payload.key, 'u');
        assert.equal(decide(event('U'), env()).payload.key, 'U');
        assert.equal(decide(event('u'), env({ active: element('TEXTAREA') })), null);
        assert.equal(decide(event('u'), env({ dialogOpen: true })), null);
    });

    it('reports nothing for an unmapped key', () => assert.equal(decide(event('a'), env()), null));

    it('reports nothing while the user types, and blurs on Escape instead', () => {
        const active = element('INPUT', { type: 'text' });

        assert.equal(decide(event('j'), env({ active })), null);
        assert.deepEqual(decide(event('Escape'), env({ active })), { blur: true });
    });

    it('reports nothing for IME composition, a repeat, or a key something else already handled', () => {
        assert.equal(decide(event('j', { isComposing: true }), env()), null);
        assert.equal(decide(event('j', { repeat: true }), env()), null);
        assert.equal(decide(event('j', { defaultPrevented: true }), env()), null);
    });

    it('reports nothing while a modal dialog is open, not even Escape', () => {
        assert.equal(decide(event('j'), env({ dialogOpen: true })), null);
        assert.equal(decide(event('Escape'), env({ dialogOpen: true, active: element('INPUT', { type: 'text' }) })), null);
    });

    it('sends Ctrl+Enter and Cmd+Enter from a text field, scope included', () => {
        const active = element('TEXTAREA');

        const ctrl = decide(event('Enter', { ctrlKey: true }), env({ active, scope: 'composer' }));
        const meta = decide(event('Enter', { metaKey: true }), env({ active, scope: 'composer' }));

        assert.equal(ctrl.payload.scope, 'composer');
        assert.equal(ctrl.payload.typing, true);
        assert.equal(ctrl.preventDefault, true);
        assert.equal(meta.payload.meta, true);
    });

    it('does not report Ctrl+Alt+Enter, so it can never map to Send', () => {
        assert.equal(decide(event('Enter', { ctrlKey: true, altKey: true }), env({ scope: 'composer' })), null);
    });

    it('does not report other chords or Alt combinations', () => {
        assert.equal(decide(event('j', { ctrlKey: true }), env()), null);
        assert.equal(decide(event('j', { altKey: true }), env()), null);
    });

    it('marks onBody false when a control has focus', () => {
        const result = decide(event('Enter'), env({ active: element('BUTTON') }));

        assert.equal(result.payload.onBody, false);
    });

    it('prevents the browser default only where the layer takes the key over', () => {
        assert.equal(decide(event('/'), env()).preventDefault, true);
        assert.equal(decide(event('?'), env()).preventDefault, true);
        assert.equal(decide(event('ArrowDown'), env({ queueOnScreen: true })).preventDefault, true);
        assert.equal(decide(event('ArrowDown'), env()).preventDefault, false);
        assert.equal(decide(event('j'), env()).preventDefault, false);
    });
});
