// Runs with `node --test` (no browser): the module takes fetch, the timers, AbortController and the document as arguments, so a fake of each is enough.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';
import {
    DEBOUNCE_MS, DEFAULT_COPY, MAX_ITEMS, MIN_CHARS, MAX_QUERY_CHARS, copyFrom, createSuggester, defineKbSuggestions, isSafeHref, parseItems, render,
} from '../../../src/TechStrap.Portal/wwwroot/js/kb-suggestions.js';

const sourcePath = new URL('../../../src/TechStrap.Portal/wwwroot/js/kb-suggestions.js', import.meta.url);

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

// A fetch that records its calls and answers when told to, so a test can answer them out of order.
function fakeFetch() {
    const calls = [];
    const fn = (url, options) => new Promise((resolve, reject) => {
        const call = { url, options, resolve, reject };
        options.signal.addEventListener?.('abort', () => reject(Object.assign(new Error('aborted'), { name: 'AbortError' })));
        calls.push(call);
    });
    fn.calls = calls;
    return fn;
}

function fakeAbortController() {
    const listeners = [];
    const signal = { aborted: false, addEventListener: (_, fn) => listeners.push(fn) };
    return { signal, abort() { signal.aborted = true; listeners.forEach((fn) => fn()); } };
}

const ok = (body) => ({ ok: true, status: 200, json: async () => body });
const flush = () => new Promise((resolve) => setImmediate(resolve));

function suggester(overrides = {}) {
    const clock = fakeClock();
    const fetchFn = fakeFetch();
    const events = [];
    const instance = createSuggester({
        url: '/p/paperplane/suggest',
        fetchFn,
        setTimer: clock.setTimer,
        clearTimer: clock.clearTimer,
        newAbortController: fakeAbortController,
        onItems: (items) => events.push(['items', items]),
        onError: () => events.push(['error']),
        ...overrides,
    });
    return { clock, fetchFn, events, instance };
}

const item = (n) => ({ title: `Article ${n}`, snippet: `About ${n}`, href: `/p/paperplane/kb/guides/a${n}` });

describe('constants', () => {
    it('debounces 300 ms, needs 3 characters, lists at most 5 and cuts the query at the API limit of 200', () => {
        assert.equal(DEBOUNCE_MS, 300);
        assert.equal(MIN_CHARS, 3);
        assert.equal(MAX_ITEMS, 5);
        assert.equal(MAX_QUERY_CHARS, 200);
    });
});

describe('isSafeHref', () => {
    it('accepts a root-relative path', () => {
        assert.equal(isSafeHref('/p/paperplane/kb/guides/dark-mode'), true);
    });

    it('refuses everything that could leave the site or run script', () => {
        for (const bad of ['//evil.example/x', 'https://evil.example', 'http://x', 'javascript:alert(1)', 'data:text/html,x', '/\\evil.example', '/a\nb', '/a\u0000b', '', '/', 'p/x', null, undefined, 5, {}]) {
            assert.equal(isSafeHref(bad), false, String(bad));
        }
    });
});

describe('parseItems', () => {
    it('keeps title, snippet and href of each usable item, at most five', () => {
        const items = parseItems([1, 2, 3, 4, 5, 6, 7].map(item));

        assert.equal(items.length, 5);
        assert.deepEqual(items[0], item(1));
    });

    it('drops an item with no title, a non-text field or an unsafe href, and anything that is not an array', () => {
        const body = [
            item(1),
            { title: '', snippet: 's', href: '/p/x' },
            { title: 't', snippet: 5, href: '/p/x' },
            { title: 't', snippet: 's', href: 'https://evil.example' },
            { title: 't', snippet: 's', href: '//evil.example' },
            null,
            'text',
        ];

        assert.deepEqual(parseItems(body), [item(1)]);
        for (const notAnArray of [null, undefined, {}, 'x', 5, { items: [item(1)] }]) {
            assert.deepEqual(parseItems(notAnArray), []);
        }
    });

    it('does not copy fields it was not asked for', () => {
        const [only] = parseItems([{ ...item(1), html: '<img src=x onerror=alert(1)>', extra: 1 }]);

        assert.deepEqual(Object.keys(only).sort(), ['href', 'snippet', 'title']);
    });
});

describe('the suggester', () => {
    it('makes no request until 300 ms after the last keystroke, then exactly one with the text escaped', async () => {
        const { clock, fetchFn, instance } = suggester();

        instance.input('pri');
        clock.advance(299);
        assert.equal(fetchFn.calls.length, 0);
        instance.input('printer & more?');
        clock.advance(299);
        assert.equal(fetchFn.calls.length, 0);
        clock.advance(1);

        assert.equal(fetchFn.calls.length, 1);
        assert.equal(fetchFn.calls[0].url, '/p/paperplane/suggest?q=printer%20%26%20more%3F');
        assert.equal(fetchFn.calls[0].options.credentials, 'same-origin');
        assert.equal(fetchFn.calls[0].options.headers.Accept, 'application/json');
    });

    it('rapid typing yields one search', () => {
        const { clock, fetchFn, instance } = suggester();

        for (const text of ['pri', 'prin', 'print', 'printe', 'printer']) {
            instance.input(text);
            clock.advance(100);
        }
        clock.advance(300);

        assert.equal(fetchFn.calls.length, 1);
        assert.ok(fetchFn.calls[0].url.endsWith('q=printer'));
    });

    it('trims the text, needs three characters and clears the list below that without a request', () => {
        const { clock, fetchFn, events, instance } = suggester();

        instance.input('  ab  ');
        instance.input('');
        instance.input(null);
        clock.advance(1000);

        assert.equal(fetchFn.calls.length, 0);
        assert.deepEqual(events, [['items', []], ['items', []], ['items', []]]);
    });

    it('cuts a long text at 200 characters', () => {
        const { clock, fetchFn, instance } = suggester();

        instance.input('x'.repeat(500));
        clock.advance(300);

        assert.equal(fetchFn.calls[0].url.split('q=')[1].length, MAX_QUERY_CHARS);
    });

    it('delivers the usable items of the answer', async () => {
        const { clock, fetchFn, events, instance } = suggester();
        instance.input('printer');
        clock.advance(300);

        fetchFn.calls[0].resolve(ok([item(1), item(2)]));
        await flush();

        assert.deepEqual(events, [['items', [item(1), item(2)]]]);
    });

    it('aborts a stale request and never shows its answer', async () => {
        const { clock, fetchFn, events, instance } = suggester();
        instance.input('first');
        clock.advance(300);
        instance.input('second');

        assert.equal(fetchFn.calls[0].options.signal.aborted, true, 'typing again aborts the request in flight');
        clock.advance(300);
        fetchFn.calls[0].resolve(ok([item(1)]));
        fetchFn.calls[1].resolve(ok([item(2)]));
        await flush();

        assert.deepEqual(events, [['items', [item(2)]]]);
    });

    it('does not show an answer that arrives after a shorter text cleared the list', async () => {
        const { clock, fetchFn, events, instance } = suggester();
        instance.input('printer');
        clock.advance(300);
        instance.input('pr');
        fetchFn.calls[0].resolve(ok([item(1)]));
        await flush();

        assert.deepEqual(events, [['items', []]]);
    });

    it('reports an error for a failed status, a network error and a body that is not json, but not for an abort', async () => {
        const { clock, fetchFn, events, instance } = suggester();

        instance.input('one');
        clock.advance(300);
        fetchFn.calls[0].resolve({ ok: false, status: 429, json: async () => [] });
        await flush();
        instance.input('two');
        clock.advance(300);
        fetchFn.calls[1].reject(new TypeError('network down'));
        await flush();
        instance.input('three');
        clock.advance(300);
        fetchFn.calls[2].resolve({ ok: true, status: 200, json: async () => { throw new SyntaxError('bad json'); } });
        await flush();
        instance.input('four');
        clock.advance(300);
        instance.input('five');

        assert.deepEqual(events, [['error'], ['error'], ['error']]);
    });

    it('treats a body that is not a list as nothing to suggest, not as an error', async () => {
        const { clock, fetchFn, events, instance } = suggester();
        instance.input('printer');
        clock.advance(300);

        fetchFn.calls[0].resolve(ok({ error: 'x' }));
        await flush();

        assert.deepEqual(events, [['items', []]]);
    });

    it('dispose clears the timer, aborts the request in flight and silences every callback', async () => {
        const { clock, fetchFn, events, instance } = suggester();
        instance.input('first');
        clock.advance(300);
        instance.input('second');
        assert.equal(clock.pending(), 1);

        instance.dispose();

        assert.equal(clock.pending(), 0, 'the timer is cleared');
        assert.equal(fetchFn.calls[0].options.signal.aborted, true, 'the request in flight is aborted');
        fetchFn.calls[0].resolve(ok([item(1)]));
        await flush();
        instance.input('third');
        clock.advance(1000);
        assert.deepEqual(events, []);
        assert.equal(fetchFn.calls.length, 1);
    });
});

// A small fake DOM: elements that record what the module does to them.
function fakeDocument() {
    const element = (tag) => ({
        tag,
        children: [],
        attrs: {},
        className: '',
        textContent: '',
        hidden: false,
        setAttribute(name, value) { this.attrs[name] = value; },
        appendChild(child) { this.children.push(child); return child; },
        replaceChildren() { this.children = []; },
    });
    return { createElement: element, byId: new Map(), getElementById(id) { return this.byId.get(id) ?? null; } };
}

describe('render', () => {
    it('writes titles and snippets with textContent and builds links with set href, new tab and noopener', () => {
        const doc = fakeDocument();
        const container = doc.createElement('ts-kb-suggestions');
        container.children = ['old'];

        render(doc, container, [{ title: '<img src=x onerror=alert(1)>', snippet: '<b>bold</b>', href: '/p/paperplane/kb/g/a' }, item(2)]);

        const [heading, list] = container.children;
        assert.equal(heading.textContent, '2 articles may help');
        const [first, second] = list.children;
        const link = first.children[0];
        assert.equal(link.tag, 'a');
        assert.equal(link.textContent, '<img src=x onerror=alert(1)> (opens in a new tab)', 'text goes in as text, never as markup');
        assert.equal(link.attrs.href, '/p/paperplane/kb/g/a');
        assert.equal(link.attrs.target, '_blank');
        assert.equal(link.attrs.rel, 'noopener noreferrer');
        assert.equal(first.children[1].textContent, '<b>bold</b>');
        assert.equal(second.children[0].attrs.href, item(2).href);
        assert.equal(container.children.length, 2, 'the old content is replaced');
    });

    it('says "1 article may help" for one and leaves the container empty for none', () => {
        const doc = fakeDocument();
        const container = doc.createElement('ts-kb-suggestions');

        render(doc, container, [item(1)]);
        assert.equal(container.children[0].textContent, '1 article may help');
        render(doc, container, []);
        assert.deepEqual(container.children, []);
    });

    it('leaves out the snippet line when the snippet is empty', () => {
        const doc = fakeDocument();
        const container = doc.createElement('x');

        render(doc, container, [{ title: 't', snippet: '', href: '/p/x' }]);

        assert.equal(container.children[1].children[0].children.length, 1);
    });

    it('uses the words it is given, for the heading and for the new-tab cue', () => {
        const doc = fakeDocument();
        const container = doc.createElement('x');
        const copy = { one: 'Un article peut aider', many: '{0} articles peuvent aider', newTab: '(nouvel onglet)' };

        render(doc, container, [item(1), item(2), item(3)], copy);
        assert.equal(container.children[0].textContent, '3 articles peuvent aider');
        assert.ok(container.children[1].children[0].children[0].textContent.endsWith(' (nouvel onglet)'));

        render(doc, container, [item(1)], copy);
        assert.equal(container.children[0].textContent, 'Un article peut aider');
    });

    it('falls back to the built-in words when no copy is given', () => {
        assert.deepEqual({ ...DEFAULT_COPY }, { one: '1 article may help', many: '{0} articles may help', newTab: '(opens in a new tab)' });
    });
});

describe('copyFrom', () => {
    it('reads the three data attributes and falls back to the defaults for a missing or empty one', () => {
        const attrs = { 'data-count-one': 'Un', 'data-count-many': '{0} plusieurs', 'data-new-tab': '' };
        const element = { getAttribute: (name) => attrs[name] ?? null };

        assert.deepEqual(copyFrom(element), { one: 'Un', many: '{0} plusieurs', newTab: DEFAULT_COPY.newTab });
        assert.deepEqual(copyFrom({ getAttribute: () => null }), { ...DEFAULT_COPY });
    });
});

describe('the custom element', () => {
    function define(doc, overrides = {}) {
        const registry = new Map();
        const clock = fakeClock();
        const fetchFn = fakeFetch();
        class FakeHTMLElement {
            constructor() { this.attrs = {}; this.children = []; this.hidden = false; }
            getAttribute(name) { return this.attrs[name] ?? null; }
            replaceChildren() { this.children = []; }
            appendChild(child) { this.children.push(child); return child; }
        }
        const env = {
            customElements: { get: (name) => registry.get(name), define: (name, cls) => registry.set(name, cls) },
            HTMLElement: FakeHTMLElement,
            document: doc,
            fetch: fetchFn,
            setTimeout: clock.setTimer,
            clearTimeout: clock.clearTimer,
            AbortController: class { constructor() { return fakeAbortController(); } },
            ...overrides,
        };
        defineKbSuggestions(env);
        return { Element: registry.get('ts-kb-suggestions'), registry, clock, fetchFn };
    }

    function inputField() {
        const listeners = new Map();
        return {
            value: '',
            addEventListener: (name, fn) => { listeners.set(name, fn); },
            removeEventListener: (name, fn) => { if (listeners.get(name) === fn) { listeners.delete(name); } },
            type(text) { this.value = text; listeners.get('input')?.(); },
            listening: () => listeners.has('input'),
        };
    }

    function connected(doc, attrs = { field: 'subject', src: '/p/paperplane/suggest' }) {
        const kit = define(doc);
        const element = new kit.Element();
        element.attrs = attrs;
        element.children = ['fallback link'];
        return { ...kit, element };
    }

    it('is defined once, as ts-kb-suggestions, and defining it again changes nothing', () => {
        const doc = fakeDocument();
        const { registry } = define(doc);

        assert.deepEqual([...registry.keys()], ['ts-kb-suggestions']);
        defineKbSuggestions({ customElements: { get: () => registry.get('ts-kb-suggestions'), define: () => assert.fail('defined twice') }, HTMLElement: class {} });
    });

    it('shows the words the server put in its data attributes', async () => {
        const doc = fakeDocument();
        const field = inputField();
        doc.byId.set('subject', field);
        const { element, clock, fetchFn } = connected(doc, { field: 'subject', src: '/p/paperplane/suggest', 'data-count-one': 'Un article', 'data-count-many': '{0} articles ici', 'data-new-tab': '(autre onglet)' });
        element.connectedCallback();

        field.type('printer');
        clock.advance(300);
        fetchFn.calls[0].resolve(ok([item(1), item(2)]));
        await flush();

        assert.equal(element.children[0].textContent, '2 articles ici');
        assert.ok(element.children[1].children[0].children[0].textContent.endsWith(' (autre onglet)'));
    });

    it('does nothing in an environment with no custom elements', () => {
        defineKbSuggestions({ HTMLElement: class {} });
    });

    it('reads the field and the url from its attributes, removes the fallback and listens to the field', () => {
        const doc = fakeDocument();
        const field = inputField();
        doc.byId.set('subject', field);
        const { element } = connected(doc);

        element.connectedCallback();

        assert.deepEqual(element.children, []);
        assert.equal(element.hidden, false);
        assert.equal(field.listening(), true);
    });

    it('shows suggestions after the debounce and hides itself when the request fails, trying again on the next keystroke', async () => {
        const doc = fakeDocument();
        const field = inputField();
        doc.byId.set('subject', field);
        const { element, clock, fetchFn } = connected(doc);
        element.connectedCallback();

        field.type('printer');
        clock.advance(300);
        fetchFn.calls[0].resolve(ok([item(1)]));
        await flush();
        assert.equal(element.children[0].textContent, '1 article may help');

        field.type('printer jam');
        clock.advance(300);
        fetchFn.calls[1].resolve({ ok: false, status: 503, json: async () => [] });
        await flush();
        assert.equal(element.hidden, true);
        assert.deepEqual(element.children, []);

        field.type('printer jam again');
        clock.advance(300);
        fetchFn.calls[2].resolve(ok([item(2)]));
        await flush();
        assert.equal(element.hidden, false, 'a later success shows the region again');
    });

    it('hides itself, and never throws, when the field is missing or the url is not a path of this site', () => {
        const doc = fakeDocument();
        const { element } = connected(doc);
        element.connectedCallback();
        assert.equal(element.hidden, true);

        const field = inputField();
        doc.byId.set('subject', field);
        for (const src of ['https://evil.example/suggest', '//evil.example', 'javascript:alert(1)', null]) {
            const kit = connected(doc, { field: 'subject', src });
            kit.element.connectedCallback();
            assert.equal(kit.element.hidden, true, String(src));
            assert.equal(field.listening(), false);
        }
    });

    it('cleans up when it is removed: the listener goes, the timer is cleared and the request in flight is aborted', async () => {
        const doc = fakeDocument();
        const field = inputField();
        doc.byId.set('subject', field);
        const { element, clock, fetchFn } = connected(doc);
        element.connectedCallback();
        field.type('printer');
        clock.advance(300);

        element.disconnectedCallback();

        assert.equal(field.listening(), false);
        assert.equal(fetchFn.calls[0].options.signal.aborted, true);
        field.type('another');
        assert.equal(clock.pending(), 0);
        fetchFn.calls[0].resolve(ok([item(1)]));
        await flush();
        assert.deepEqual(element.children, []);
    });

    it('can be removed without ever having connected', () => {
        const doc = fakeDocument();
        const { element } = connected(doc);

        element.disconnectedCallback();
    });
});

describe('the source', () => {
    const source = readFileSync(sourcePath, 'utf8');
    const code = source.split('\n').filter((line) => !line.trim().startsWith('//') && !line.trim().startsWith('*') && !line.trim().startsWith('/**')).join('\n');

    it('never builds markup from text: no innerHTML, outerHTML, insertAdjacentHTML, document.write, eval or Function', () => {
        for (const forbidden of ['innerHTML', 'outerHTML', 'insertAdjacentHTML', 'document.write', 'eval(', 'new Function', 'setAttribute(\'on', 'srcdoc']) {
            assert.equal(code.includes(forbidden), false, `${forbidden} must not appear`);
        }
    });

    it('puts server text on the page with textContent', () => {
        assert.ok(code.includes('.textContent ='));
    });

    it('is plain ASCII', () => {
        assert.equal(/[^\x09\x0a\x0d\x20-\x7e]/.test(source), false);
    });
});
