// Runs with `node --test`: the dialog module takes the native <dialog> and the .NET handle as arguments, so a fake of each is enough.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { open, close } from '../../../src/TechStrap.Admin/wwwroot/js/dialog.js';

function fakeDialog(lock) {
    const listeners = {};
    const dialog = {
        open: false,
        shown: 0,
        focused: 0,
        dataset: lock ? { lock } : {},
        addEventListener: (name, handler) => { (listeners[name] ??= []).push(handler); },
        showModal() { dialog.open = true; dialog.shown++; },
        close() { dialog.open = false; },
        focus() { dialog.focused++; },
        fire(name, event = {}) { (listeners[name] ?? []).forEach((handler) => handler(event)); },
    };
    return dialog;
}

function fakeHandle() {
    const calls = [];
    return { calls, invokeMethodAsync: (name) => { calls.push(name); return Promise.resolve(); } };
}

function escape() {
    const event = { key: 'Escape', prevented: false, preventDefault() { event.prevented = true; } };
    return event;
}

describe('Escape keydown', () => {
    it('is prevented while the dialog is busy, and .NET is not told', () => {
        const dialog = fakeDialog('busy');
        const handle = fakeHandle();
        open(dialog, null, handle);
        const event = escape();

        dialog.fire('keydown', event);

        assert.equal(event.prevented, true);
        assert.deepEqual(handle.calls, []);
    });

    it('is prevented while the dialog must not be dismissed, and .NET is told so it can explain', () => {
        const dialog = fakeDialog('hold');
        const handle = fakeHandle();
        open(dialog, null, handle);
        const event = escape();

        dialog.fire('keydown', event);

        assert.equal(event.prevented, true);
        assert.deepEqual(handle.calls, ['EscapePressed']);
    });

    it('is left alone when the dialog can be dismissed, and other keys are never prevented', () => {
        const dialog = fakeDialog();
        const handle = fakeHandle();
        open(dialog, null, handle);
        const event = escape();
        const other = { key: 'a', prevented: false, preventDefault() { other.prevented = true; } };

        dialog.fire('keydown', event);
        dialog.fire('keydown', other);

        assert.equal(event.prevented, false);
        assert.equal(other.prevented, false);
    });
});

describe('an unexpected native close', () => {
    it('re-shows the dialog while it is locked, so the browser can never dismiss it behind .NET', () => {
        for (const lock of ['busy', 'hold']) {
            const dialog = fakeDialog(lock);
            const handle = fakeHandle();
            open(dialog, null, handle);
            dialog.open = false; // the browser closed it (a repeated Esc whose cancel was not cancelable)

            dialog.fire('close');

            assert.equal(dialog.open, true, lock);
            assert.equal(dialog.shown, 2, lock);
            assert.deepEqual(handle.calls, [], lock);
        }
    });

    it('is reported to .NET when the dialog may be dismissed, which treats it as a cancel', () => {
        const dialog = fakeDialog();
        const handle = fakeHandle();
        open(dialog, null, handle);
        dialog.open = false;

        dialog.fire('close');

        assert.equal(dialog.shown, 1);
        assert.deepEqual(handle.calls, ['NativeClosed']);
    });

    it('is ignored when .NET closed the dialog itself', () => {
        const dialog = fakeDialog('hold');
        const handle = fakeHandle();
        open(dialog, null, handle);

        close(dialog);
        dialog.fire('close');

        assert.equal(dialog.open, false);
        assert.equal(dialog.shown, 1);
        assert.deepEqual(handle.calls, []);
    });

    it('opening twice adds the listeners once', () => {
        const dialog = fakeDialog('hold');
        const handle = fakeHandle();
        open(dialog, null, handle);
        close(dialog);
        open(dialog, null, handle);

        dialog.fire('keydown', escape());

        assert.deepEqual(handle.calls, ['EscapePressed']);
    });
});
