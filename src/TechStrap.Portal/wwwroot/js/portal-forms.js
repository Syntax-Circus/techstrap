// The Portal's small form helpers (PHASE-09d, D-045 09d addendum). Vanilla JavaScript, no framework and no build step, loaded once from the document shell (App.razor).
//
// It does four things, and every page works without it:
//   1. "Sending" state: when a form that carries data-sending-label is submitted, its submit button is disabled and shows that label, so a double click cannot post twice (the server's one-time id is the
//      real guard; this is the visible half). The button comes back on pageshow (the back button) and after SENDING_RESET_MS (a post the visitor stopped).
//   2. <ts-copy-text target="ticket-number" data-label="Copy ticket number" data-copied="Copied" data-failed="..."></ts-copy-text>: a button that copies the text of the element with that id; when the browser
//      refuses (an insecure page, no permission) the text is selected instead, so Ctrl+C works.
//   3. <ts-char-count for="body" data-limit="100000" data-template="{0} of {1} characters" data-over="{0} over the limit"></ts-char-count>: a counter for a textarea, shown once the text reaches
//      NEAR_RATIO of the limit. A line break counts as two characters, because the browser sends CR LF and the server counts what it receives, while the textarea's own maxlength counts one.
//   4. A fresh one-time id for every form when the page comes back from the back/forward cache (pageshow with persisted): the id the server uses to answer a double click with the first answer (D-045) must
//      not make a message that the visitor edits after pressing Back look like a repeat. The server also compares what was posted, so this is the second half of the same rule.
//
// Why custom elements and document listeners: the Portal's pages are swapped into the open document by Blazor's enhanced navigation, which does NOT run a script that arrives with the new content (the
// spike of 09d proved it: the kb-suggestions module in a page body never ran after an enhanced click). A module in the document shell runs once, and the browser calls connectedCallback for an element
// the swap inserts; a listener on the document sees the forms of every later page.
//
// Same-page enhanced navigation (a form post that shows errors, a page link on the same route) keeps these elements in the document but replaces their content with the server's (empty, or the no-script fallback),
// and connectedCallback does not run again. So each element watches its own children with a MutationObserver and builds them again when they are gone. (data-permanent was rejected: it would also keep the old
// page's attributes, such as the field a counter belongs to or the suggest path of another product.)
//
// The words come from the server as data attributes (the *Copy constants), so the page owns its copy. Everything the module touches is passed in (the document, the window, the navigator, the timers), so the
// behavior is tested with `node --test` and no browser. Text is put on the page with textContent only; this file never parses text as markup, and a test fails if it starts to.

export const SENDING_RESET_MS = 60000;
export const NEAR_RATIO = 0.8;
export const COUNT_ANNOUNCE_MS = 1000;
export const COPIED_RESET_MS = 4000;
export const SUBMIT_ID_SELECTOR = 'input[name$=".SubmitId"]';

/** The length the server will see for a textarea's value: the browser sends each line break as CR LF, so every LF counts as two characters. */
export function serverLength(value) {
    const text = typeof value === 'string' ? value.replace(/\r\n/g, '\n') : '';
    let breaks = 0;
    for (let i = 0; i < text.length; i += 1) {
        if (text.charCodeAt(i) === 10) {
            breaks += 1;
        }
    }

    return text.length + breaks;
}

/** 100000 -> "100,000" (the Portal's copy is English). */
export function groupDigits(number) {
    return String(Math.trunc(Math.abs(Number(number)) || 0)).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
}

/** Replaces {0} and {1} in a template; a missing template gives an empty string. */
export function fill(template, first, second) {
    if (typeof template !== 'string') {
        return '';
    }

    return template.split('{0}').join(first).split('{1}').join(second);
}

/**
 * What the counter shows for a text of `used` characters against `limit`: nothing below NEAR_RATIO of the limit; the count from there; and, above the limit, how far over it is.
 * Returns { visible, over, text }.
 */
export function counterText({ used, limit, template, overTemplate }) {
    if (!(limit > 0) || used < limit * NEAR_RATIO) {
        return { visible: false, over: false, text: '' };
    }

    if (used > limit) {
        return { visible: true, over: true, text: fill(overTemplate, groupDigits(used - limit), groupDigits(limit)) };
    }

    return { visible: true, over: false, text: fill(template, groupDigits(used), groupDigits(limit)) };
}

/** A fresh one-time id in the shape the server accepts (SubmitIds.IsWellFormed): 16 random bytes as 22 base64url characters. Null when the browser has no random source. */
export function newSubmitId(cryptoApi = globalThis.crypto, encode = (text) => globalThis.btoa(text)) {
    if (!cryptoApi || typeof cryptoApi.getRandomValues !== 'function') {
        return null;
    }

    const bytes = cryptoApi.getRandomValues(new Uint8Array(16));
    return encode(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** Gives every form of the page a new id (after the back button restored the old one with its text). A form whose id cannot be renewed keeps the old one. */
export function refreshSubmitIds(doc, cryptoApi, encode) {
    for (const input of doc.querySelectorAll(SUBMIT_ID_SELECTOR)) {
        const id = newSubmitId(cryptoApi, encode);
        if (id !== null) {
            input.value = id;
        }
    }
}

/** Watches the children of `element` and calls `rebuild` when `intact()` turns false (an enhanced navigation replaced them). Returns the observer, or null without MutationObserver. */
function watchChildren(env, element, intact, rebuild) {
    if (typeof env.MutationObserver !== 'function') {
        return null;
    }

    const observer = new env.MutationObserver(() => {
        if (!intact()) {
            rebuild();
        }
    });
    observer.observe(element, { childList: true });
    return observer;
}

/** The first submit button of a form, or null. */
export function submitButtonOf(form) {
    return form.querySelector('button[type="submit"]');
}

/**
 * The sending state of the forms of the page. `onSubmit` is the capturing document listener; `onPageShow` restores a form the back button brings back. State is kept in a WeakMap, never on the markup, because
 * an enhanced navigation rewrites the attributes of an element it keeps.
 */
export function createSendingState({ setTimer = (fn, ms) => setTimeout(fn, ms), clearTimer = (id) => clearTimeout(id), resetAfterMs = SENDING_RESET_MS } = {}) {
    const sending = new WeakMap();
    const active = new Set();

    function reset(form) {
        const state = sending.get(form);
        if (!state) {
            return;
        }

        clearTimer(state.timer);
        state.button.disabled = false;
        state.button.textContent = state.label;
        sending.delete(form);
        active.delete(form);
    }

    return {
        /** True when the form is now (or already was) in the sending state; a second submit of a form that is sending is canceled. */
        onSubmit(event) {
            const form = event.target;
            if (!form || typeof form.getAttribute !== 'function') {
                return false;
            }

            const text = form.getAttribute('data-sending-label');
            if (typeof text !== 'string' || text.length === 0) {
                return false;
            }

            // A submit that something else already canceled sends nothing, so the button must not be left disabled.
            if (event.defaultPrevented) {
                return false;
            }

            if (sending.has(form)) {
                event.preventDefault();
                return true;
            }

            const button = submitButtonOf(form);
            if (!button) {
                return false;
            }

            const timer = setTimer(() => reset(form), resetAfterMs);
            sending.set(form, { button, label: button.textContent, timer });
            active.add(form);
            button.disabled = true;
            button.textContent = text;
            return true;
        },

        /** After the back button (a page restored from the cache), every form that was sending is usable again. */
        onPageShow(event) {
            if (event && event.persisted) {
                for (const form of [...active]) {
                    reset(form);
                }
            }
        },

        isSending: (form) => sending.has(form),
    };
}

/** Writes `text` to the clipboard; false (never an exception) when the browser refuses or has no clipboard. */
export async function copyText(text, nav = globalThis.navigator) {
    try {
        if (!nav || !nav.clipboard || typeof nav.clipboard.writeText !== 'function') {
            return false;
        }

        await nav.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

/** Selects the contents of `node`, so the visitor can press Ctrl+C; false when that is not possible. */
export function selectContents(node, doc) {
    try {
        const selection = doc.getSelection ? doc.getSelection() : null;
        if (!node || !selection) {
            return false;
        }

        selection.selectAllChildren(node);
        return true;
    } catch {
        return false;
    }
}

/** The words of a copy button: each data attribute, falling back to English when missing or empty. */
export function copyWords(element) {
    const read = (name, fallback) => {
        const value = element.getAttribute(name);
        return typeof value === 'string' && value.length > 0 ? value : fallback;
    };

    return { label: read('data-label', 'Copy'), copied: read('data-copied', 'Copied'), failed: read('data-failed', 'Press Ctrl+C to copy') };
}

/** Defines <ts-copy-text> and <ts-char-count> with the given environment (the browser's own, at the bottom of this file). */
export function defineElements(env) {
    if (!env.customElements) {
        return;
    }

    if (!env.customElements.get('ts-copy-text')) {
        env.customElements.define('ts-copy-text', class extends env.HTMLElement {
            connectedCallback() {
                const target = env.document.getElementById(this.getAttribute('target') ?? '');
                if (!target) {
                    return;
                }

                const words = copyWords(this);
                const button = env.document.createElement('button');
                button.type = 'button';
                button.className = 'btn btn-outline-primary ts-copy-button';
                button.textContent = words.label;
                const status = env.document.createElement('span');
                status.className = 'ts-copy-status';
                status.setAttribute('role', 'status');
                this.replaceChildren(button, status);
                this._timer = null;
                this._rendered = [button, status];
                this._onClick = async () => {
                    const copied = await copyText((target.textContent ?? '').trim(), env.navigator);
                    if (!copied) {
                        selectContents(target, env.document);
                    }

                    status.textContent = copied ? words.copied : words.failed;
                    if (this._timer !== null) {
                        env.clearTimeout(this._timer);
                    }

                    this._timer = env.setTimeout(() => { status.textContent = ''; this._timer = null; }, COPIED_RESET_MS);
                };
                this._button = button;
                button.addEventListener('click', this._onClick);
                this._observer = watchChildren(
                    env,
                    this,
                    () => this._rendered.every((node) => this.contains(node)),
                    () => { this.disconnectedCallback(); this.connectedCallback(); });
            }

            disconnectedCallback() {
                this._observer?.disconnect();
                this._observer = null;
                if (this._button && this._onClick) {
                    this._button.removeEventListener('click', this._onClick);
                }

                if (this._timer !== null && this._timer !== undefined) {
                    env.clearTimeout(this._timer);
                }

                this._button = null;
                this._onClick = null;
                this._timer = null;
            }
        });
    }

    if (!env.customElements.get('ts-char-count')) {
        env.customElements.define('ts-char-count', class extends env.HTMLElement {
            connectedCallback() {
                const field = env.document.getElementById(this.getAttribute('for') ?? '');
                const limit = Number(this.getAttribute('data-limit'));
                if (!field || !(limit > 0)) {
                    return;
                }

                const visible = env.document.createElement('span');
                visible.className = 'ts-count-text';
                visible.setAttribute('aria-hidden', 'true');
                const speech = env.document.createElement('span');
                speech.className = 'visually-hidden';
                speech.setAttribute('role', 'status');
                this.replaceChildren(visible, speech);
                this._rendered = [visible, speech];
                this._timer = null;
                this._field = field;
                this._onInput = () => {
                    const state = counterText({
                        used: serverLength(field.value),
                        limit,
                        template: this.getAttribute('data-template'),
                        overTemplate: this.getAttribute('data-over'),
                    });
                    visible.textContent = state.text;
                    this.hidden = !state.visible;
                    this.classList?.toggle('ts-count-over', state.over);
                    if (this._timer !== null) {
                        env.clearTimeout(this._timer);
                    }

                    // The screen reader hears the count once typing pauses, not on every key.
                    this._timer = env.setTimeout(() => { speech.textContent = state.text; this._timer = null; }, COUNT_ANNOUNCE_MS);
                };
                this.hidden = true;
                field.addEventListener('input', this._onInput);
                this._onInput();
                this._observer = watchChildren(
                    env,
                    this,
                    () => this._rendered.every((node) => this.contains(node)) && env.document.getElementById(this.getAttribute('for') ?? '') === this._field,
                    () => { this.disconnectedCallback(); this.connectedCallback(); });
            }

            disconnectedCallback() {
                this._observer?.disconnect();
                this._observer = null;
                if (this._field && this._onInput) {
                    this._field.removeEventListener('input', this._onInput);
                }

                if (this._timer !== null && this._timer !== undefined) {
                    env.clearTimeout(this._timer);
                }

                this._field = null;
                this._onInput = null;
                this._timer = null;
            }
        });
    }
}

/** Starts the document-level listeners of the sending state. Returns the state, for a test. */
export function installSendingState(env, options) {
    const state = createSendingState(options);
    env.document.addEventListener('submit', (event) => state.onSubmit(event), true);
    env.window.addEventListener('pageshow', (event) => {
        state.onPageShow(event);
        if (event && event.persisted) {
            refreshSubmitIds(env.document, env.crypto, env.btoa);
        }
    });
    return state;
}

if (typeof customElements !== 'undefined' && typeof document !== 'undefined') {
    const env = {
        customElements,
        HTMLElement,
        document,
        window,
        navigator,
        crypto: globalThis.crypto,
        btoa: (text) => globalThis.btoa(text),
        MutationObserver: globalThis.MutationObserver,
        setTimeout: (...args) => setTimeout(...args),
        clearTimeout: (...args) => clearTimeout(...args),
    };
    defineElements(env);
    installSendingState(env);
}
