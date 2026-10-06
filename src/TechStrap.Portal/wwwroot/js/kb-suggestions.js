// <ts-kb-suggestions>: article suggestions beside the contact form's subject field (PHASE-09b, D-045 addendum). Vanilla JavaScript, no framework and no build step.
//
// Markup (server-rendered; the content of the element is the fallback shown when this script does not run):
//   <ts-kb-suggestions field="subject" src="/p/paperplane/suggest" aria-live="polite"><a href="...">Search help articles</a></ts-kb-suggestions>
//
// The module lists suggestions only: the form works without it and is never blocked by it. Everything it needs from the browser is passed in (fetch, the timers, AbortController, the document), so the
// behaviour is tested with `node --test` and no browser. Text from the server is plain text and is only ever put on the page with textContent; a link is only ever built from a root-relative path
// (isSafeHref). This file never parses text as markup, and a test fails if it starts to.

export const DEBOUNCE_MS = 300;
export const MIN_CHARS = 3;
export const MAX_ITEMS = 5;
export const MAX_QUERY_CHARS = 200;

/** A root-relative path on this site ("/p/x/kb/a/b"): not protocol-relative ("//host"), not an absolute URL, no backslash, no control character. */
export function isSafeHref(value) {
    return typeof value === 'string'
        && value.length > 1
        && value.startsWith('/')
        && !value.startsWith('//')
        && !/[\\\u0000-\u001f\u007f]/.test(value);
}

/** The usable items of a response body: an array of {title, snippet, href} with text titles and safe hrefs, at most MAX_ITEMS. Anything else is an empty list. */
export function parseItems(body) {
    if (!Array.isArray(body)) {
        return [];
    }

    return body
        .filter((item) => item && typeof item.title === 'string' && item.title.length > 0 && typeof item.snippet === 'string' && isSafeHref(item.href))
        .slice(0, MAX_ITEMS)
        .map((item) => ({ title: item.title, snippet: item.snippet, href: item.href }));
}

/**
 * The debounce and request logic, with no DOM. `input(text)` is called on every keystroke; after DEBOUNCE_MS of quiet (and at least MIN_CHARS characters) one request is made, and a newer
 * request aborts the older one, whose answer is dropped. `onItems(items)` gets the list (empty clears the region); `onError()` is called when the request failed in any way except being aborted.
 * `dispose()` clears the timer, aborts the request in flight and silences every callback.
 */
export function createSuggester({ url, fetchFn, setTimer, clearTimer, newAbortController, onItems, onError }) {
    let timer = null;
    let inflight = null;
    let disposed = false;

    function cancel() {
        if (timer !== null) {
            clearTimer(timer);
            timer = null;
        }

        if (inflight !== null) {
            inflight.abort();
            inflight = null;
        }
    }

    async function run(text) {
        const controller = newAbortController();
        inflight = controller;
        try {
            const response = await fetchFn(`${url}?q=${encodeURIComponent(text)}`, { signal: controller.signal, headers: { Accept: 'application/json' }, credentials: 'same-origin' });
            if (controller.signal.aborted || disposed) {
                return;
            }

            if (!response.ok) {
                throw new Error(`suggest ${response.status}`);
            }

            const body = await response.json();
            if (controller.signal.aborted || disposed) {
                return;
            }

            inflight = null;
            onItems(parseItems(body));
        } catch (error) {
            if (controller.signal.aborted || disposed || (error && error.name === 'AbortError')) {
                return;
            }

            inflight = null;
            onError();
        }
    }

    return {
        input(text) {
            if (disposed) {
                return;
            }

            cancel();
            const query = String(text ?? '').trim().slice(0, MAX_QUERY_CHARS);
            if (query.length < MIN_CHARS) {
                onItems([]);
                return;
            }

            timer = setTimer(() => {
                timer = null;
                void run(query);
            }, DEBOUNCE_MS);
        },
        dispose() {
            disposed = true;
            cancel();
        },
    };
}

/** Replaces the content of `container` with the list: a count line and the links, each opening in a new tab with the cue in its text. An empty list leaves the container empty (no "nothing found" noise). */
export function render(doc, container, items) {
    container.replaceChildren();
    if (items.length === 0) {
        return;
    }

    const heading = doc.createElement('p');
    heading.className = 'ts-suggest-heading';
    heading.textContent = items.length === 1 ? '1 article may help' : `${items.length} articles may help`;
    const list = doc.createElement('ul');
    list.className = 'ts-suggest-list';
    for (const item of items) {
        const row = doc.createElement('li');
        const link = doc.createElement('a');
        link.setAttribute('href', item.href);
        link.setAttribute('target', '_blank');
        link.setAttribute('rel', 'noopener noreferrer');
        link.textContent = `${item.title} (opens in a new tab)`;
        row.appendChild(link);
        if (item.snippet.length > 0) {
            const snippet = doc.createElement('span');
            snippet.className = 'ts-suggest-snippet';
            snippet.textContent = item.snippet;
            row.appendChild(snippet);
        }

        list.appendChild(row);
    }

    container.appendChild(heading);
    container.appendChild(list);
}

/**
 * Defines <ts-kb-suggestions> with the given environment (the browser's own, by default at the bottom of this file). The fallback content is removed when the element connects (it is only for browsers
 * without script) and the element stays empty until there is something to suggest. When the request fails the element hides itself; the next keystroke tries again.
 */
export function defineKbSuggestions(env) {
    if (!env.customElements || env.customElements.get('ts-kb-suggestions')) {
        return;
    }

    env.customElements.define('ts-kb-suggestions', class extends env.HTMLElement {
        connectedCallback() {
            const input = env.document.getElementById(this.getAttribute('field') ?? '');
            const url = this.getAttribute('src');
            if (!input || !isSafeHref(url)) {
                this.hidden = true;
                return;
            }

            this.replaceChildren();
            this._input = input;
            this._suggester = createSuggester({
                url,
                fetchFn: env.fetch,
                setTimer: env.setTimeout,
                clearTimer: env.clearTimeout,
                newAbortController: () => new env.AbortController(),
                onItems: (items) => {
                    this.hidden = false;
                    render(env.document, this, items);
                },
                onError: () => {
                    this.replaceChildren();
                    this.hidden = true;
                },
            });
            this._listener = () => this._suggester.input(input.value);
            input.addEventListener('input', this._listener);
        }

        disconnectedCallback() {
            if (this._input && this._listener) {
                this._input.removeEventListener('input', this._listener);
            }

            this._suggester?.dispose();
            this._suggester = null;
            this._listener = null;
            this._input = null;
        }
    });
}

if (typeof customElements !== 'undefined' && typeof document !== 'undefined') {
    defineKbSuggestions({
        customElements,
        HTMLElement,
        document,
        fetch: (...args) => fetch(...args),
        setTimeout: (...args) => setTimeout(...args),
        clearTimeout: (...args) => clearTimeout(...args),
        AbortController,
    });
}
