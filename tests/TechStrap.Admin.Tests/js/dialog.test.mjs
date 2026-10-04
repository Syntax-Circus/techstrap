// Runs with `node --test`: the dialog module takes the native <dialog> and the .NET handle as arguments, so a fake of each is enough.
import assert from 'node:assert/strict';
import { beforeEach, describe, it } from 'node:test';
import { open, close } from '../../../src/TechStrap.Admin/wwwroot/js/dialog.js';

// A fake document: the module listens on it (capture phase) while a dialog is open, so Esc is seen even when focus has fallen to <body>.
function fakeDocument() {
    const listeners = [];
    const doc = {
        listeners,
        addEventListener: (name, handler, capture) => { listeners.push({ name, handler, capture }); },
        removeEventListener: (name, handler) => {
            const at = listeners.findIndex((l) => l.name === name && l.handler === handler);
            if (at >= 0) { listeners.splice(at, 1); }
        },
        fire(name, event = {}) { [...listeners].filter((l) => l.name === name).forEach((l) => l.handler(event)); },
    };
    return doc;
}

beforeEach(() => { globalThis.document = fakeDocument(); });

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

describe('Escape with focus outside the dialog (a disabled button hands focus to <body>)', () => {
    it('is prevented at the document capture phase while the dialog is locked', () => {
        for (const lock of ['busy', 'hold']) {
            const dialog = fakeDialog(lock);
            const handle = fakeHandle();
            open(dialog, null, handle);
            const event = escape();

            assert.equal(document.listeners.every((l) => l.capture === true), true, lock);
            document.fire('keydown', event);

            assert.equal(event.prevented, true, lock);
        }
    });

    it('tells .NET once for a held dialog, never for a busy one, even when the dialog sees the same event too', () => {
        const held = fakeDialog('hold');
        const heldHandle = fakeHandle();
        open(held, null, heldHandle);
        const event = escape();
        document.fire('keydown', event);
        held.fire('keydown', event);
        assert.deepEqual(heldHandle.calls, ['EscapePressed']);

        const busy = fakeDialog('busy');
        const busyHandle = fakeHandle();
        open(busy, null, busyHandle);
        document.fire('keydown', escape());
        assert.deepEqual(busyHandle.calls, []);
    });

    it('does not interfere while the dialog is unlocked, or for other keys', () => {
        const dialog = fakeDialog();
        open(dialog, null, fakeHandle());
        const event = escape();
        const other = { key: 'a', prevented: false, preventDefault() { other.prevented = true; } };

        document.fire('keydown', event);
        document.fire('keydown', other);

        assert.equal(event.prevented, false);
        assert.equal(other.prevented, false);
    });

    it('stops listening after the dialog is closed, and opening twice adds one listener', () => {
        const dialog = fakeDialog('busy');
        open(dialog, null, fakeHandle());
        open(dialog, null, fakeHandle());
        assert.equal(document.listeners.length, 1);

        close(dialog);

        assert.equal(document.listeners.length, 0);
        const event = escape();
        document.fire('keydown', event);
        assert.equal(event.prevented, false);
    });
});

describe('a stray close event', () => {
    it('is ignored when the dialog is already open again', () => {
        const dialog = fakeDialog();
        const handle = fakeHandle();
        open(dialog, null, handle);

        dialog.fire('close'); // dialog.open is still true: the close belongs to an earlier cycle

        assert.deepEqual(handle.calls, []);
    });
});
