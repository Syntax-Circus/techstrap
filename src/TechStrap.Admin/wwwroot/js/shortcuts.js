// The keyboard layer's one document listener (UX-BRIEF-admin, Density and keyboard shortcuts). It only reports key presses; ShortcutService decides
// what they mean. To keep the circuit quiet it reports nothing but the keys the layer can use, and nothing while the user types (except Ctrl/Cmd+Enter,
// which sends from the composer). Escape while typing blurs the field and keeps the text.
//
// The filtering is pure (isTyping, isRelevantKey, decide take plain objects) so tests/TechStrap.Admin.Tests/js/shortcuts.test.mjs can run it under node:test.

const NON_TEXT_INPUTS = new Set(['button', 'checkbox', 'radio', 'submit', 'reset', 'file', 'range', 'color', 'image']);
const EDITABLE_VALUES = new Set(['', 'true', 'plaintext-only']);

// The keys ShortcutService maps; any other key is never sent. 'u' (Not spam) is listed ahead of its mapping: Task 12 adds it to ShortcutService.Map,
// and until then the service simply ignores it.
const RELEVANT = new Set(['j', 'k', 'ArrowDown', 'ArrowUp', 'Enter', '/', 'r', 'n', 'e', 'u', '?', 'Escape']);

let reference = null;
let listener = null;

/** True when the element takes typed text: a text-like input, textarea, select, or an editable region (contenteditable, including plaintext-only). */
export function isTyping(element) {
    if (!element) {
        return false;
    }

    const tag = (element.tagName || '').toUpperCase();
    if (tag === 'TEXTAREA' || tag === 'SELECT') {
        return true;
    }

    if (tag === 'INPUT') {
        return !NON_TEXT_INPUTS.has((element.type || '').toLowerCase());
    }

    // isContentEditable is inherited, so an element inside an editable region counts too.
    if (element.isContentEditable) {
        return true;
    }

    const attribute = typeof element.getAttribute === 'function' ? element.getAttribute('contenteditable') : null;
    return attribute !== null && attribute !== undefined && EDITABLE_VALUES.has(attribute.toLowerCase());
}

/** True for the keys the layer can use (letters compared case-insensitively). */
export function isRelevantKey(key) {
    return RELEVANT.has(key.length === 1 ? key.toLowerCase() : key);
}

/**
 * Decides what one key press means for the page.
 * env: { active (focused element), dialogOpen, scope (nearest data-shortcut-scope), queueOnScreen }.
 * Returns null (ignore), { blur: true } (Escape in a field), or { payload, preventDefault } (report to .NET).
 */
export function decide(event, env) {
    if (event.defaultPrevented || event.isComposing || event.repeat) {
        return null;
    }

    // A modal dialog owns the keyboard: Esc and Enter belong to it.
    if (env.dialogOpen) {
        return null;
    }

    const typing = isTyping(env.active);
    if (event.key === 'Escape' && typing) {
        return { blur: true };
    }

    const key = event.key.length === 1 ? event.key.toLowerCase() : event.key;
    const chord = event.ctrlKey || event.metaKey;
    // Send is Ctrl/Cmd+Enter only: Ctrl+Alt+Enter is a different chord (AltGr on some layouts) and never sends.
    const sendFromComposer = chord && event.key === 'Enter' && !event.altKey;
    if (!sendFromComposer && (typing || chord || event.altKey || !isRelevantKey(key))) {
        return null;
    }

    const active = env.active;
    const onBody = !active || active.tagName === 'BODY' || active.tagName === 'HTML';
    const scope = env.scope ?? null;

    // Stop the browser's own use of the key (Firefox quick-find on "/", page scroll on the arrows in the queue, a form submit on Ctrl+Enter).
    const preventDefault = (sendFromComposer && scope === 'composer')
        || key === '/'
        || key === '?'
        || ((key === 'ArrowDown' || key === 'ArrowUp') && onBody && !!env.queueOnScreen);

    return {
        preventDefault,
        payload: {
            key: event.key,
            ctrl: !!event.ctrlKey,
            meta: !!event.metaKey,
            alt: !!event.altKey,
            typing,
            onBody,
            scope,
        },
    };
}

export function register(dotNetReference) {
    unregister();
    reference = dotNetReference;
    listener = (event) => {
        const active = document.activeElement;
        const scopeElement = active && active.closest ? active.closest('[data-shortcut-scope]') : null;
        const result = decide(event, {
            active,
            dialogOpen: !!document.querySelector('dialog[open]'),
            scope: scopeElement ? scopeElement.dataset.shortcutScope : null,
            queueOnScreen: !!document.querySelector('[data-shortcut-scope="queue"]'),
        });

        if (!result) {
            return;
        }

        if (result.blur) {
            active.blur();
            return;
        }

        if (result.preventDefault) {
            event.preventDefault();
        }

        // A circuit that is gone or reconnecting rejects the call; a key press is not worth an unhandled rejection.
        reference.invokeMethodAsync('OnKeyAsync', result.payload).catch(() => { });
    };
    document.addEventListener('keydown', listener);
}

export function unregister() {
    if (listener) {
        document.removeEventListener('keydown', listener);
    }

    listener = null;
    reference = null;
}
