// The keyboard layer's one document listener (UX-BRIEF-admin, Density and keyboard shortcuts). It only reports key presses; ShortcutService decides
// what they mean. To keep the circuit quiet it reports nothing but the keys the layer can use, and nothing while the user types (except Ctrl/Cmd+Enter,
// which sends from the composer). Escape while typing blurs the field and keeps the text.

const TEXT_TARGET = 'input, textarea, select, [contenteditable=""], [contenteditable="true"]';
const NON_TEXT_INPUTS = new Set(['button', 'checkbox', 'radio', 'submit', 'reset', 'file', 'range', 'color', 'image']);

// The keys ShortcutService maps; any other key is never sent. (u is Not spam.)
const RELEVANT = new Set(['j', 'k', 'ArrowDown', 'ArrowUp', 'Enter', '/', 'r', 'n', 'e', 'u', '?', 'Escape']);

let reference = null;
let listener = null;

function isTyping(element) {
    if (!element || !element.closest) {
        return false;
    }

    const target = element.closest(TEXT_TARGET);
    return !!target && !(target.tagName === 'INPUT' && NON_TEXT_INPUTS.has(target.type));
}

export function register(dotNetReference) {
    unregister();
    reference = dotNetReference;
    listener = (event) => {
        if (event.defaultPrevented || event.isComposing) {
            return;
        }

        // A modal dialog owns the keyboard: Esc and Enter belong to it.
        if (document.querySelector('dialog[open]')) {
            return;
        }

        const active = document.activeElement;
        const typing = isTyping(active);
        const chord = event.ctrlKey || event.metaKey;

        if (event.key === 'Escape' && typing) {
            active.blur();
            return;
        }

        const key = event.key.length === 1 ? event.key.toLowerCase() : event.key;
        const sendFromComposer = chord && event.key === 'Enter';
        if (!sendFromComposer && (typing || chord || event.altKey || !RELEVANT.has(key))) {
            return;
        }

        const scopeElement = active && active.closest ? active.closest('[data-shortcut-scope]') : null;
        const scope = scopeElement ? scopeElement.dataset.shortcutScope : null;
        const onBody = !active || active === document.body || active === document.documentElement;

        // Stop the browser's own use of the key (Firefox quick-find on "/", page scroll on the arrows in the queue, a form submit on Ctrl+Enter).
        if (sendFromComposer && scope === 'composer') {
            event.preventDefault();
        } else if (key === '/' || key === '?') {
            event.preventDefault();
        } else if ((key === 'ArrowDown' || key === 'ArrowUp') && onBody && document.querySelector('[data-shortcut-scope="queue"]')) {
            event.preventDefault();
        }

        reference.invokeMethodAsync('OnKeyAsync', {
            key: event.key,
            ctrl: event.ctrlKey,
            meta: event.metaKey,
            alt: event.altKey,
            typing,
            onBody,
            scope,
        });
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
