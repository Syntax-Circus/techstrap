// Runs with `node --test` (no browser): the module takes the document, the window, the navigator and the timers as arguments, so a fake of each is enough.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';
import {
    COPIED_RESET_MS, COUNT_ANNOUNCE_MS, NEAR_RATIO, SENDING_RESET_MS, copyText, copyWords, counterText, createSendingState, defineElements, fill, groupDigits, installSendingState, selectContents,
    serverLength, submitButtonOf,
} from '../../../src/TechStrap.Portal/wwwroot/js/portal-forms.js';

const sourcePath = new URL('../../../src/TechStrap.Portal/wwwroot/js/portal-forms.js', import.meta.url);

// A manual clock: timers run only when a test advances it.
function fakeClock() {
    let now = 0;
    let next = 1;
    const timers = new Map();
    return {
        setTimer: (fn, ms) => { const id = next++; timers.set(id, { fn, at: now + ms }); return id; },
        clearTimer: (id) => { timers.delete(id); },
        pending: () => timers.size,
        advance(ms) {
            now += ms;
            for (const [id, timer] of [...timers]) {
                if (timer.at <= now) { timers.delete(id); timer.fn(); }
            }
        },
    };
}

// A small fake DOM: elements that record what the module does to them.
function fakeElement(tag, attrs = {}) {
    const listeners = new Map();
    const classes = new Set();
    return {
        tag,
        attrs: { ...attrs },
        children: [],
        className: '',
        textContent: '',
        hidden: false,
        disabled: false,
        type: '',
        value: '',
        classList: { toggle: (name, on) => { if (on) { classes.add(name); } else { classes.delete(name); } }, contains: (name) => classes.has(name) },
        getAttribute(name) { return Object.hasOwn(this.attrs, name) ? this.attrs[name] : null; },
        setAttribute(name, value) { this.attrs[name] = value; },
        replaceChildren(...children) { this.children = children; },
        appendChild(child) { this.children.push(child); return child; },
        addEventListener(type, fn) { listeners.set(type, [...(listeners.get(type) ?? []), fn]); },
        removeEventListener(type, fn) { listeners.set(type, (listeners.get(type) ?? []).filter((x) => x !== fn)); },
        listenerCount: (type) => (listeners.get(type) ?? []).length,
        fire(type, event = {}) { for (const fn of listeners.get(type) ?? []) { fn({ type, target: this, ...event }); } },
        querySelector(selector) { return selector === 'button[type="submit"]' ? this.submit ?? null : null; },
    };
}

function fakeDocument() {
    const byId = new Map();
    const selections = [];
    return {
        byId,
        selections,
        createElement: (tag) => fakeElement(tag),
        getElementById: (id) => byId.get(id) ?? null,
        getSelection: () => ({ selectAllChildren: (node) => selections.push(node) }),
        listeners: new Map(),
        addEventListener(type, fn, capture) { this.listeners.set(type, { fn, capture }); },
    };
}

function formWith(label = 'Sending...') {
    const form = fakeElement('form', label === null ? {} : { 'data-sending-label': label });
    const submit = fakeElement('button');
    submit.textContent = 'Send message';
    form.submit = submit;
    return { form, submit };
}

describe('constants', () => {
    it('a stopped post frees the button after a minute, the counter shows from 80 percent, speech waits one second and the copy confirmation lasts four', () => {
        assert.equal(SENDING_RESET_MS, 60000);
        assert.equal(NEAR_RATIO, 0.8);
        assert.equal(COUNT_ANNOUNCE_MS, 1000);
        assert.equal(COPIED_RESET_MS, 4000);
    });
});

describe('serverLength', () => {
    it('counts every line break as two characters, because the browser posts CR LF', () => {
        assert.equal(serverLength(''), 0);
        assert.equal(serverLength('abc'), 3);
        assert.equal(serverLength('a\nb'), 4);
        assert.equal(serverLength('\n\n\n'), 6);
        assert.equal(serverLength('a\r\nb'), 4, 'a CR LF that is already there is counted once as two');
    });

    it('is 0 for anything that is not text', () => {
        for (const value of [null, undefined, 5, {}]) {
            assert.equal(serverLength(value), 0);
        }
    });
});

describe('groupDigits and fill', () => {
    it('writes thousands with commas', () => {
        assert.equal(groupDigits(100000), '100,000');
        assert.equal(groupDigits(999), '999');
        assert.equal(groupDigits(1234567), '1,234,567');
        assert.equal(groupDigits(-5), '5');
        assert.equal(groupDigits('x'), '0');
    });

    it('replaces {0} and {1} everywhere and gives an empty string for no template', () => {
        assert.equal(fill('{0} of {1} ({0})', '5', '10'), '5 of 10 (5)');
        assert.equal(fill(null, '5', '10'), '');
        assert.equal(fill(undefined, '5', '10'), '');
    });
});

describe('counterText', () => {
    const base = { limit: 1000, template: '{0} of {1} characters', overTemplate: '{0} over the limit of {1}' };

    it('is hidden below 80 percent of the limit', () => {
        assert.equal(NEAR_RATIO, 0.8);
        assert.deepEqual(counterText({ ...base, used: 0 }), { visible: false, over: false, text: '' });
        assert.deepEqual(counterText({ ...base, used: 799 }), { visible: false, over: false, text: '' });
    });

    it('shows the count from 80 percent up to the limit', () => {
        assert.deepEqual(counterText({ ...base, used: 800 }), { visible: true, over: false, text: '800 of 1,000 characters' });
        assert.deepEqual(counterText({ ...base, used: 1000 }), { visible: true, over: false, text: '1,000 of 1,000 characters' });
    });

    it('says how far over the limit a text is', () => {
        assert.deepEqual(counterText({ ...base, used: 1003 }), { visible: true, over: true, text: '3 over the limit of 1,000' });
    });

    it('shows nothing without a usable limit', () => {
        for (const limit of [0, -1, NaN, undefined]) {
            assert.equal(counterText({ ...base, limit, used: 5000 }).visible, false);
        }
    });
});

describe('the sending state', () => {
    it('disables the submit button and shows the form\'s own sending words, once the form is submitted', () => {
        const clock = fakeClock();
        const state = createSendingState(clock);
        const { form, submit } = formWith('Sending\u2026');

        const handled = state.onSubmit({ target: form, preventDefault() { assert.fail('the first submit must go through'); } });

        assert.equal(handled, true);
        assert.equal(submit.disabled, true);
        assert.equal(submit.textContent, 'Sending\u2026');
        assert.equal(state.isSending(form), true);
    });

    it('cancels a second submit of a form that is already sending', () => {
        const state = createSendingState(fakeClock());
        const { form } = formWith();
        state.onSubmit({ target: form, preventDefault() {} });
        let prevented = 0;

        state.onSubmit({ target: form, preventDefault() { prevented += 1; } });

        assert.equal(prevented, 1);
    });

    it('leaves a form without a sending label, and a form without a submit button, alone', () => {
        const state = createSendingState(fakeClock());
        const plain = formWith(null);
        const noButton = fakeElement('form', { 'data-sending-label': 'Sending...' });

        assert.equal(state.onSubmit({ target: plain.form, preventDefault() {} }), false);
        assert.equal(plain.submit.disabled, false);
        assert.equal(state.onSubmit({ target: noButton, preventDefault() {} }), false);
        assert.equal(state.onSubmit({ target: null, preventDefault() {} }), false);
        assert.equal(state.onSubmit({ target: {}, preventDefault() {} }), false);
        const empty = formWith('');
        assert.equal(state.onSubmit({ target: empty.form, preventDefault() {} }), false);
    });

    it('brings the button back after a minute, for a post the visitor stopped', () => {
        const clock = fakeClock();
        const state = createSendingState(clock);
        const { form, submit } = formWith();
        state.onSubmit({ target: form, preventDefault() {} });

        clock.advance(SENDING_RESET_MS - 1);
        assert.equal(submit.disabled, true);
        clock.advance(1);

        assert.equal(submit.disabled, false);
        assert.equal(submit.textContent, 'Send message');
        assert.equal(state.isSending(form), false);
    });

    it('brings every sending form back on pageshow after the back button, and not on an ordinary pageshow', () => {
        const clock = fakeClock();
        const state = createSendingState(clock);
        const one = formWith();
        const two = formWith('Sending...');
        state.onSubmit({ target: one.form, preventDefault() {} });
        state.onSubmit({ target: two.form, preventDefault() {} });

        state.onPageShow({ persisted: false });
        assert.equal(one.submit.disabled, true);
        state.onPageShow(undefined);
        assert.equal(one.submit.disabled, true);
        state.onPageShow({ persisted: true });

        assert.equal(one.submit.disabled, false);
        assert.equal(two.submit.disabled, false);
        assert.equal(one.submit.textContent, 'Send message');
        assert.equal(clock.pending(), 0, 'the reset timers are cleared');
    });

    it('can send again after a reset', () => {
        const clock = fakeClock();
        const state = createSendingState(clock);
        const { form, submit } = formWith();
        state.onSubmit({ target: form, preventDefault() {} });
        state.onPageShow({ persisted: true });

        assert.equal(state.onSubmit({ target: form, preventDefault() { assert.fail('not a duplicate any more'); } }), true);
        assert.equal(submit.disabled, true);
    });

    it('is installed as a capturing listener on the document and a pageshow listener on the window', () => {
        const doc = fakeDocument();
        const win = fakeElement('window');

        const state = installSendingState({ document: doc, window: win }, fakeClock());

        assert.equal(doc.listeners.get('submit').capture, true);
        assert.equal(win.listenerCount('pageshow'), 1);
        const { form, submit } = formWith();
        doc.listeners.get('submit').fn({ target: form, preventDefault() {} });
        assert.equal(submit.disabled, true);
        win.fire('pageshow', { persisted: true });
        assert.equal(submit.disabled, false);
        assert.equal(state.isSending(form), false);
    });

    it('finds the submit button by its type', () => {
        const { form, submit } = formWith();
        assert.equal(submitButtonOf(form), submit);
    });
});

describe('copyText and selectContents', () => {
    it('writes to the clipboard and answers true', async () => {
        const written = [];

        assert.equal(await copyText('PAP-42', { clipboard: { writeText: async (text) => { written.push(text); } } }), true);
        assert.deepEqual(written, ['PAP-42']);
    });

    it('answers false, and never throws, when the browser refuses or has no clipboard', async () => {
        assert.equal(await copyText('x', { clipboard: { writeText: async () => { throw new Error('NotAllowedError'); } } }), false);
        assert.equal(await copyText('x', {}), false);
        assert.equal(await copyText('x', { clipboard: {} }), false);
        assert.equal(await copyText('x', null), false);
    });

    it('selects the contents of the element', () => {
        const doc = fakeDocument();
        const node = fakeElement('strong');

        assert.equal(selectContents(node, doc), true);
        assert.deepEqual(doc.selections, [node]);
    });

    it('answers false when there is nothing to select or no selection API', () => {
        assert.equal(selectContents(null, fakeDocument()), false);
        assert.equal(selectContents(fakeElement('x'), {}), false);
        assert.equal(selectContents(fakeElement('x'), { getSelection: () => { throw new Error('no'); } }), false);
    });
});

describe('copyWords', () => {
    it('reads the words from the data attributes and falls back to English for a missing or empty one', () => {
        const full = fakeElement('ts-copy-text', { 'data-label': 'Copier', 'data-copied': 'Copie', 'data-failed': 'Ctrl+C' });
        assert.deepEqual(copyWords(full), { label: 'Copier', copied: 'Copie', failed: 'Ctrl+C' });
        assert.deepEqual(copyWords(fakeElement('x', { 'data-label': '' })), { label: 'Copy', copied: 'Copied', failed: 'Press Ctrl+C to copy' });
    });
});

function elementsEnv() {
    const doc = fakeDocument();
    const clock = fakeClock();
    const defined = new Map();
    const written = [];
    const env = {
        customElements: { get: (name) => defined.get(name), define: (name, ctor) => defined.set(name, ctor) },
        HTMLElement: class {
            constructor() {
                Object.assign(this, fakeElement('custom'));
            }
        },
        document: doc,
        navigator: { clipboard: { writeText: async (text) => { written.push(text); } } },
        setTimeout: clock.setTimer,
        clearTimeout: clock.clearTimer,
    };
    return { env, doc, clock, defined, written };
}

const flush = () => new Promise((resolve) => setImmediate(resolve));

describe('<ts-copy-text>', () => {
    function copyElement(setup, attrs = {}) {
        const { env, doc, clock, defined, written } = setup;
        defineElements(env);
        const target = fakeElement('strong');
        target.textContent = ' PAP-42 ';
        doc.byId.set('ticket-number', target);
        const element = new (defined.get('ts-copy-text'))();
        Object.assign(element.attrs, { target: 'ticket-number', 'data-label': 'Copy ticket number', 'data-copied': 'Copied', 'data-failed': 'Select it and press Ctrl+C', ...attrs });
        return { element, target, doc, clock, written };
    }

    it('is defined once, even when the module runs twice', () => {
        const setup = elementsEnv();
        defineElements(setup.env);
        const first = setup.defined.get('ts-copy-text');

        defineElements(setup.env);

        assert.equal(setup.defined.get('ts-copy-text'), first);
        assert.ok(setup.defined.get('ts-char-count'));
    });

    it('renders a button with the server\'s words as text and copies the trimmed text of its target', async () => {
        const { element, written } = copyElement(elementsEnv());

        element.connectedCallback();
        const [button, status] = element.children;
        assert.equal(button.tag, 'button');
        assert.equal(button.type, 'button');
        assert.equal(button.textContent, 'Copy ticket number');
        assert.equal(status.attrs.role, 'status');
        button.fire('click');
        await flush();

        assert.deepEqual(written, ['PAP-42']);
        assert.equal(status.textContent, 'Copied');
    });

    it('clears the confirmation after a few seconds', async () => {
        const { element, clock } = copyElement(elementsEnv());
        element.connectedCallback();
        const [button, status] = element.children;
        button.fire('click');
        await flush();

        clock.advance(COPIED_RESET_MS);

        assert.equal(status.textContent, '');
    });

    it('selects the number and says so when the browser refuses the clipboard', async () => {
        const setup = elementsEnv();
        setup.env.navigator = { clipboard: { writeText: async () => { throw new Error('NotAllowedError'); } } };
        const { element, target, doc } = copyElement(setup);
        element.connectedCallback();
        const [button, status] = element.children;

        button.fire('click');
        await flush();

        assert.deepEqual(doc.selections, [target]);
        assert.equal(status.textContent, 'Select it and press Ctrl+C');
    });

    it('does nothing when its target is not on the page, and stays clean when removed', () => {
        const { element, doc } = copyElement(elementsEnv());
        doc.byId.clear();

        element.connectedCallback();
        element.disconnectedCallback();

        assert.deepEqual(element.children, []);
    });

    it('removes its listener and timer when disconnected', async () => {
        const { element, clock } = copyElement(elementsEnv());
        element.connectedCallback();
        const [button] = element.children;
        button.fire('click');
        await flush();

        element.disconnectedCallback();

        assert.equal(button.listenerCount('click'), 0);
        assert.equal(clock.pending(), 0);
    });

    it('puts text on the page only as text, whatever the words and the number contain', async () => {
        const { element, target } = copyElement(elementsEnv(), { 'data-label': '<img src=x onerror=alert(1)>' });
        target.textContent = '<b>PAP-42</b>';

        element.connectedCallback();

        assert.equal(element.children[0].textContent, '<img src=x onerror=alert(1)>');
        assert.equal(element.children[0].children.length, 0);
    });
});

describe('<ts-char-count>', () => {
    function counter(setup, attrs = {}) {
        const { env, doc, clock, defined } = setup;
        defineElements(env);
        const field = fakeElement('textarea');
        doc.byId.set('body', field);
        const element = new (defined.get('ts-char-count'))();
        Object.assign(element.attrs, { for: 'body', 'data-limit': '1000', 'data-template': '{0} of {1} characters', 'data-over': '{0} over the limit', ...attrs });
        return { element, field, clock };
    }

    it('starts hidden and empty, and listens to its textarea', () => {
        const { element, field } = counter(elementsEnv());

        element.connectedCallback();

        assert.equal(element.hidden, true);
        assert.equal(field.listenerCount('input'), 1);
        assert.equal(element.children[0].textContent, '');
    });

    it('appears with the count once the text reaches 80 percent, and hides again below it', () => {
        const { element, field } = counter(elementsEnv());
        element.connectedCallback();

        field.value = 'x'.repeat(850);
        field.fire('input');
        assert.equal(element.hidden, false);
        assert.equal(element.children[0].textContent, '850 of 1,000 characters');
        assert.equal(element.children[0].attrs['aria-hidden'], 'true', 'the visible text is not read on every key');

        field.value = 'x'.repeat(100);
        field.fire('input');
        assert.equal(element.hidden, true);
    });

    it('counts a line break as two characters, as the server will', () => {
        const { element, field } = counter(elementsEnv());
        element.connectedCallback();

        field.value = 'a'.repeat(450) + '\n'.repeat(225); // 450 + 225 line breaks counted twice
        field.fire('input');

        assert.equal(element.children[0].textContent, '900 of 1,000 characters');
    });

    it('says how far over the limit the posted text will be, and marks it', () => {
        const { element, field } = counter(elementsEnv());
        element.connectedCallback();

        field.value = 'a'.repeat(1030) + '\n'.repeat(5); // 1030 + 5 line breaks counted twice
        field.fire('input');

        assert.equal(element.children[0].textContent, '40 over the limit');
        assert.equal(element.classList.contains('ts-count-over'), true);
        field.value = 'a';
        field.fire('input');
        assert.equal(element.classList.contains('ts-count-over'), false);
    });

    it('tells the screen reader once typing pauses, not on every key', () => {
        const { element, field, clock } = counter(elementsEnv());
        element.connectedCallback();
        const speech = element.children[1];
        assert.equal(speech.attrs.role, 'status');

        field.value = 'x'.repeat(850);
        field.fire('input');
        clock.advance(COUNT_ANNOUNCE_MS - 1);
        field.value = 'x'.repeat(860);
        field.fire('input');
        clock.advance(COUNT_ANNOUNCE_MS - 1);
        assert.equal(speech.textContent, '', 'still typing');
        clock.advance(1);

        assert.equal(speech.textContent, '860 of 1,000 characters');
    });

    it('shows the count at once for a textarea that already holds text (a form shown again after an error)', () => {
        const setup = elementsEnv();
        const { element, field } = counter(setup);
        field.value = 'x'.repeat(900);

        element.connectedCallback();

        assert.equal(element.hidden, false);
        assert.equal(element.children[0].textContent, '900 of 1,000 characters');
    });

    it('does nothing without its textarea or a usable limit', () => {
        const noLimit = counter(elementsEnv(), { 'data-limit': 'many' });
        noLimit.element.connectedCallback();
        assert.deepEqual(noLimit.element.children, []);
        assert.equal(noLimit.field.listenerCount('input'), 0);

        const noField = counter(elementsEnv(), { for: 'missing' });
        noField.element.connectedCallback();
        assert.deepEqual(noField.element.children, []);
    });

    it('removes its listener and timer when disconnected', () => {
        const { element, field, clock } = counter(elementsEnv());
        element.connectedCallback();
        field.value = 'x'.repeat(900);
        field.fire('input');

        element.disconnectedCallback();

        assert.equal(field.listenerCount('input'), 0);
        assert.equal(clock.pending(), 0);
    });
});

describe('the source', () => {
    const source = readFileSync(sourcePath, 'utf8');
    const code = source.split('\n').filter((line) => !line.trim().startsWith('//') && !line.trim().startsWith('*') && !line.trim().startsWith('/**')).join('\n');

    it('never builds markup from text: no innerHTML, outerHTML, insertAdjacentHTML, document.write, eval or Function', () => {
        for (const forbidden of ['innerHTML', 'outerHTML', 'insertAdjacentHTML', 'document.write', 'eval(', 'new Function', 'setAttribute(\'on', 'srcdoc', 'createContextualFragment', 'DOMParser']) {
            assert.equal(code.includes(forbidden), false, `${forbidden} must not appear`);
        }
    });

    it('puts server text on the page with textContent', () => {
        assert.ok(code.includes('.textContent ='));
    });

    it('does not store state in attributes, builds no URL and makes no request', () => {
        for (const forbidden of ['fetch(', 'XMLHttpRequest', 'sendBeacon', 'location.', 'localStorage', 'sessionStorage', 'document.cookie']) {
            assert.equal(code.includes(forbidden), false, `${forbidden} must not appear`);
        }
    });

    it('is plain ASCII', () => {
        assert.equal(/[^\x09\x0a\x0d\x20-\x7e]/.test(source), false);
    });
});
