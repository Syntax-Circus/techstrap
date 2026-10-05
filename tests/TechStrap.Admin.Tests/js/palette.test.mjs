// Runs with `node --test` (no browser): decideKey in palette.js takes a plain event object.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { attach, decideKey, detach, reveal } from '../../../src/TechStrap.Admin/wwwroot/js/palette.js';

const event = (key, extra = {}) => ({ key, isComposing: false, altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, ...extra });

describe('decideKey', () => {
    it('takes the keys that move the selection and run the command', () => {
        for (const key of ['ArrowDown', 'ArrowUp', 'Home', 'End', 'Enter']) {
            assert.equal(decideKey(event(key)), key, key);
        }
    });

    it('leaves typing, Escape and Tab to the browser', () => {
        for (const key of ['a', 'Z', ' ', 'Backspace', 'ArrowLeft', 'ArrowRight', 'Escape', 'Tab', 'Delete']) {
            assert.equal(decideKey(event(key)), null, key);
        }
    });

    it('leaves every chord alone, so Ctrl+Home, Shift+Arrow selection and Alt+Arrow keep their browser meaning', () => {
        for (const extra of [{ ctrlKey: true }, { metaKey: true }, { altKey: true }, { shiftKey: true }]) {
            assert.equal(decideKey(event('ArrowDown', extra)), null, JSON.stringify(extra));
            assert.equal(decideKey(event('Enter', extra)), null, JSON.stringify(extra));
        }
    });

    it('leaves a key during IME composition alone, because Enter there confirms the composition', () => {
        assert.equal(decideKey(event('Enter', { isComposing: true })), null);
    });
});

describe('attach, detach and reveal', () => {
    const fakeInput = () => {
        const listeners = new Map();
        return {
            listeners,
            addEventListener: (name, fn) => listeners.set(name, fn),
            removeEventListener: (name) => listeners.delete(name),
        };
    };

    it('reports a selection key to .NET once and stops the browser from handling it', () => {
        const input = fakeInput();
        const told = [];
        attach(input, { invokeMethodAsync: (method, key) => { told.push([method, key]); return Promise.resolve(); } });
        let prevented = 0;

        input.listeners.get('keydown')({ ...event('ArrowDown'), preventDefault: () => { prevented++; } });
        input.listeners.get('keydown')({ ...event('a'), preventDefault: () => { prevented++; } });

        assert.deepEqual(told, [['NavigateKey', 'ArrowDown']]);
        assert.equal(prevented, 1);
    });

    it('attaches once per input and detaches cleanly', () => {
        const input = fakeInput();
        const handle = { invokeMethodAsync: () => Promise.resolve() };

        attach(input, handle);
        const first = input.listeners.get('keydown');
        attach(input, handle);
        assert.equal(input.listeners.get('keydown'), first);

        detach(input);
        assert.equal(input.listeners.has('keydown'), false);
        assert.doesNotThrow(() => detach(input));
        assert.doesNotThrow(() => detach(null));
        assert.doesNotThrow(() => attach(null, handle));
    });

    it('never throws when the circuit is gone', () => {
        const input = fakeInput();
        attach(input, { invokeMethodAsync: () => { throw new Error('disconnected'); } });

        assert.doesNotThrow(() => input.listeners.get('keydown')({ ...event('Enter'), preventDefault: () => { } }));

        const rejected = fakeInput();
        attach(rejected, { invokeMethodAsync: () => Promise.reject(new Error('gone')) });
        assert.doesNotThrow(() => rejected.listeners.get('keydown')({ ...event('Enter'), preventDefault: () => { } }));
    });

    it('reveals the selected option, and does nothing without a list or a selection', () => {
        const revealed = [];
        const list = { querySelector: () => ({ scrollIntoView: (options) => revealed.push(options) }) };

        reveal(list);
        reveal(null);
        reveal({ querySelector: () => null });

        assert.deepEqual(revealed, [{ block: 'nearest' }]);
    });
});
