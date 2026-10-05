// The keyboard of a menu button (WAI-ARIA menu button pattern), for the ticket's "More actions" menu. The button has aria-haspopup="menu"; the list has role="menu" and
// its buttons role="menuitem". Which items are shown is .NET's (the menu element is hidden while it is closed); this module only moves focus and reports open and close.
//
//   ArrowDown on the button   opens the menu and focuses the first item (.NET renders it open, then calls focusFirst)
//   ArrowDown / ArrowUp       next / previous item, wrapping round
//   Home / End                first / last item
//   Escape                    closes the menu and returns focus to the button; the key is handled here, so the page's own Escape (back to the queue) does not also fire
//   Tab                       closes the menu and lets focus move on
//
// decideKey is pure (plain objects in, a plain answer out) so tests/TechStrap.Admin.Tests/js/menu.test.mjs can run it under node:test.

const attached = new WeakMap();

/**
 * state: { open (the menu is showing), onButton (focus is on the menu button), count (menu items), current (index of the focused item, -1 for none) }.
 * Returns null (the browser keeps the key), or { preventDefault, action }: action is 'open', 'close', 'close-focus-button' or { focus: index }.
 */
export function decideKey(event, state) {
    if (event.isComposing || event.altKey || event.ctrlKey || event.metaKey) {
        return null;
    }

    if (!state.open) {
        return state.onButton && event.key === 'ArrowDown' ? { preventDefault: true, action: 'open' } : null;
    }

    switch (event.key) {
        case 'Escape':
            return { preventDefault: true, action: 'close-focus-button' };
        case 'Tab':
            return { preventDefault: false, action: 'close' };
        case 'Home':
            return state.count > 0 ? { preventDefault: true, action: { focus: 0 } } : null;
        case 'End':
            return state.count > 0 ? { preventDefault: true, action: { focus: state.count - 1 } } : null;
        case 'ArrowDown':
            return state.count > 0 ? { preventDefault: true, action: { focus: (state.current + 1) % state.count } } : null;
        case 'ArrowUp':
            return state.count > 0 ? { preventDefault: true, action: { focus: state.current <= 0 ? state.count - 1 : state.current - 1 } } : null;
        default:
            return null;
    }
}

function tell(handle, method) {
    try {
        const pending = handle.invokeMethodAsync(method);
        if (pending && typeof pending.catch === 'function') {
            pending.catch(() => { });
        }
    } catch {
        // The circuit is gone; there is nobody to tell.
    }
}

/** Starts the keyboard handling of one menu button wrapper (the element that holds the button and the menu). Safe to call again. */
export function attach(root, handle) {
    if (!root || attached.has(root)) {
        return;
    }

    const listener = (event) => {
        const button = root.querySelector('[aria-haspopup="menu"]');
        const menu = root.querySelector('[role="menu"]');
        if (!button || !menu) {
            return;
        }

        const items = Array.from(menu.querySelectorAll('[role="menuitem"]'));
        const result = decideKey(event, {
            open: !menu.hidden,
            onButton: document.activeElement === button,
            count: items.length,
            current: items.indexOf(document.activeElement),
        });
        if (!result) {
            return;
        }

        if (result.preventDefault) {
            event.preventDefault();
        }

        if (result.action === 'open') {
            tell(handle, 'OpenMenu');
        } else if (result.action === 'close' || result.action === 'close-focus-button') {
            tell(handle, 'CloseMenu');
            if (result.action === 'close-focus-button') {
                button.focus();
            }
        } else if (items[result.action.focus]) {
            items[result.action.focus].focus();
        }
    };

    root.addEventListener('keydown', listener);
    attached.set(root, listener);
}

export function detach(root) {
    const listener = root ? attached.get(root) : null;
    if (listener) {
        root.removeEventListener('keydown', listener);
        attached.delete(root);
    }
}

/** Called after .NET rendered the menu open: puts focus on the first item. */
export function focusFirst(menu) {
    const first = menu ? menu.querySelector('[role="menuitem"]') : null;
    if (first) {
        first.focus();
    }
}
