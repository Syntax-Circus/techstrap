// Runs with `node --test` (no browser): copyText and selectText take the navigator and the element as arguments.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { copyText, selectText } from '../../../src/TechStrap.Admin/wwwroot/js/clipboard.js';

describe('copyText', () => {
    it('writes the text to the clipboard and answers true', async () => {
        const written = [];
        const nav = { clipboard: { writeText: async (text) => { written.push(text); } } };

        assert.equal(await copyText('tsk_secret', nav), true);
        assert.deepEqual(written, ['tsk_secret']);
    });

    it('answers false, and does not throw, when the browser refuses', async () => {
        const nav = { clipboard: { writeText: async () => { throw new Error('NotAllowedError'); } } };

        assert.equal(await copyText('tsk_secret', nav), false);
    });

    it('answers false when there is no clipboard (an insecure page) or no navigator', async () => {
        assert.equal(await copyText('x', {}), false);
        assert.equal(await copyText('x', { clipboard: {} }), false);
        assert.equal(await copyText('x', null), false);
    });
});

describe('selectText', () => {
    it('focuses and selects the whole value', () => {
        const calls = [];
        const element = {
            value: 'tsk_secret',
            focus: () => calls.push('focus'),
            select: () => calls.push('select'),
            setSelectionRange: (start, end) => calls.push(`range ${start}-${end}`),
        };

        assert.equal(selectText(element), true);
        assert.deepEqual(calls, ['focus', 'select', 'range 0-10']);
    });

    it('answers false, and does not throw, for a missing or unusable element', () => {
        assert.equal(selectText(null), false);
        assert.equal(selectText({ focus: () => { throw new Error('detached'); } }), false);
    });
});
