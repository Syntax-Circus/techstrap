// Runs with `node --test` (no browser): decideKey in menu.js takes plain objects.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { decideKey, focusIfWithin } from '../../../src/TechStrap.Admin/wwwroot/js/menu.js';

const event = (key, extra = {}) => ({ key, isComposing: false, altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, ...extra });
const closed = (extra = {}) => ({ open: false, onButton: true, count: 3, current: -1, ...extra });
const open = (extra = {}) => ({ open: true, onButton: false, count: 3, current: 0, ...extra });

describe('a closed menu', () => {
    it('opens on ArrowDown from the button and stops the page from scrolling', () => {
        assert.deepEqual(decideKey(event('ArrowDown'), closed()), { preventDefault: true, action: 'open' });
    });

    it('ignores every other key, and ArrowDown when focus is not on the button', () => {
        for (const key of ['ArrowUp', 'Escape', 'Tab', 'Home', 'End', 'a']) {
            assert.equal(decideKey(event(key), closed()), null, key);
        }

        assert.equal(decideKey(event('ArrowDown'), closed({ onButton: false })), null);
    });
});

describe('an open menu', () => {
    it('moves to the next and previous item and wraps round at both ends', () => {
        assert.deepEqual(decideKey(event('ArrowDown'), open({ current: 0 })).action, { focus: 1 });
        assert.deepEqual(decideKey(event('ArrowDown'), open({ current: 2 })).action, { focus: 0 });
        assert.deepEqual(decideKey(event('ArrowUp'), open({ current: 2 })).action, { focus: 1 });
        assert.deepEqual(decideKey(event('ArrowUp'), open({ current: 0 })).action, { focus: 2 });
    });

    it('starts from the first item going down and the last going up when nothing in the menu has focus yet', () => {
        assert.deepEqual(decideKey(event('ArrowDown'), open({ current: -1 })).action, { focus: 0 });
        assert.deepEqual(decideKey(event('ArrowUp'), open({ current: -1 })).action, { focus: 2 });
    });

    it('jumps to the first and last item with Home and End', () => {
        assert.deepEqual(decideKey(event('Home'), open({ current: 2 })).action, { focus: 0 });
        assert.deepEqual(decideKey(event('End'), open({ current: 0 })).action, { focus: 2 });
    });

    it('takes every navigation key from the browser', () => {
        for (const key of ['ArrowDown', 'ArrowUp', 'Home', 'End']) {
            assert.equal(decideKey(event(key), open()).preventDefault, true, key);
        }
    });

    it('closes on Escape and returns focus to the button, and the key is taken so the page does not also go back to the queue', () => {
        assert.deepEqual(decideKey(event('Escape'), open()), { preventDefault: true, action: 'close-focus-button' });
    });

    it('closes on Tab and Shift+Tab but lets focus move on', () => {
        assert.deepEqual(decideKey(event('Tab'), open()), { preventDefault: false, action: 'close' });
        assert.deepEqual(decideKey(event('Tab', { shiftKey: true }), open()), { preventDefault: false, action: 'close' });
    });

    it('does nothing for an empty menu or an unrelated key', () => {
        for (const key of ['ArrowDown', 'ArrowUp', 'Home', 'End']) {
            assert.equal(decideKey(event(key), open({ count: 0, current: -1 })), null, key);
        }

        assert.equal(decideKey(event('a'), open()), null);
        assert.equal(decideKey(event('Enter'), open()), null);
    });
});

describe('chords and composition', () => {
    it('are never handled', () => {
        for (const extra of [{ ctrlKey: true }, { metaKey: true }, { altKey: true }, { isComposing: true }]) {
            assert.equal(decideKey(event('ArrowDown', extra), open()), null, JSON.stringify(extra));
            assert.equal(decideKey(event('Escape', extra), open()), null, JSON.stringify(extra));
            assert.equal(decideKey(event('ArrowDown', extra), closed()), null, JSON.stringify(extra));
        }
    });
});

describe('focusIfWithin', () => {
    const focusable = () => ({ focused: 0, focus() { this.focused++; } });

    it('moves focus to the target when focus is inside the container', () => {
        const link = {};
        const target = focusable();
        globalThis.document = { activeElement: link };

        focusIfWithin({ contains: (element) => element === link }, target);

        assert.equal(target.focused, 1);
    });

    it('leaves focus alone when it is somewhere else or the elements are missing', () => {
        const target = focusable();
        globalThis.document = { activeElement: {} };

        focusIfWithin({ contains: () => false }, target);
        focusIfWithin(null, target);
        focusIfWithin({ contains: () => true }, null);

        assert.equal(target.focused, 0);
    });
});
