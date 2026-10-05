// The command palette's keyboard plumbing (UX-BRIEF-admin, command palette). The palette is a native <dialog> with a combobox input and a listbox. Typing is the browser's;
// the keys that move the selection are taken here, because only a script can stop the browser from moving the caret on ArrowUp and ArrowDown, and Blazor cannot cancel a key
// per press. The script reports the key to .NET (CommandPalette.NavigateKey), which owns the selection and runs the command.
//
// decideKey is pure so tests/TechStrap.Admin.Tests/js/palette.test.mjs can run it under node:test.

const NAVIGATION_KEYS = new Set(['ArrowDown', 'ArrowUp', 'Home', 'End', 'Enter']);

const attached = new WeakMap();

/** The key to report to .NET, or null when the browser keeps it (typing, Escape, any chord, IME composition). */
export function decideKey(event) {
    if (event.isComposing || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
        return null;
    }

    return NAVIGATION_KEYS.has(event.key) ? event.key : null;
}

/** Starts reporting the selection keys of the input to .NET. Safe to call again for the same input. */
export function attach(input, handle) {
    if (!input || attached.has(input)) {
        return;
    }

    const listener = (event) => {
        const key = decideKey(event);
        if (key === null) {
            return;
        }

        event.preventDefault();
        try {
            const pending = handle.invokeMethodAsync('NavigateKey', key);
            if (pending && typeof pending.catch === 'function') {
                pending.catch(() => { });
            }
        } catch {
            // The circuit is gone; there is nobody to tell.
        }
    };

    input.addEventListener('keydown', listener);
    attached.set(input, listener);
}

export function detach(input) {
    const listener = input ? attached.get(input) : null;
    if (listener) {
        input.removeEventListener('keydown', listener);
        attached.delete(input);
    }
}

/** Scrolls the selected option into view inside the list, so ArrowDown past the visible lines does not select something unseen. */
export function reveal(list) {
    const selected = list ? list.querySelector('[aria-selected="true"]') : null;
    if (selected && typeof selected.scrollIntoView === 'function') {
        selected.scrollIntoView({ block: 'nearest' });
    }
}
