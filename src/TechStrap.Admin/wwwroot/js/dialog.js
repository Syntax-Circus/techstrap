// Opens and closes a native <dialog> for ConfirmDialog. showModal() gives the focus trap and the inert background;
// the caller names the element that takes the first focus (the typed-confirmation input, or the heading), never a button.
//
// The native dialog must never close behind .NET's back. Chromium's close-watcher rule makes the cancel event of a repeated Esc (no user activation in
// between) non-cancelable, so preventing "cancel" is not enough. The dialog carries data-lock ("busy" while a request runs, "hold" while its owner
// refuses to be dismissed); while it is set, Esc is stopped at the keydown, and a "close" the script did not ask for re-shows the dialog. When the dialog
// may be dismissed, a stray close is reported to .NET, which treats it as a cancel.

const states = new WeakMap();

function lockOf(dialog) {
    return dialog.dataset ? dialog.dataset.lock || '' : '';
}

function tell(state, method) {
    try {
        const pending = state.handle && state.handle.invokeMethodAsync(method);
        if (pending && typeof pending.catch === 'function') {
            pending.catch(() => { });
        }
    } catch {
        // The circuit is gone; there is nobody to tell.
    }
}

function watch(dialog, state) {
    dialog.addEventListener('keydown', (event) => {
        const lock = lockOf(dialog);
        if (event.key !== 'Escape' || !lock) {
            return;
        }

        event.preventDefault();
        if (lock === 'hold') {
            tell(state, 'EscapePressed');
        }
    });

    dialog.addEventListener('close', () => {
        if (!state.wantOpen) {
            return;
        }

        if (lockOf(dialog)) {
            if (!dialog.open) {
                dialog.showModal();
                dialog.focus();
            }

            return;
        }

        tell(state, 'NativeClosed');
    });
}

export function open(dialog, focusTarget, handle) {
    let state = states.get(dialog);
    if (!state) {
        state = { handle: null, wantOpen: false };
        states.set(dialog, state);
        watch(dialog, state);
    }

    state.handle = handle || state.handle;
    state.wantOpen = true;
    if (!dialog.open) {
        dialog.showModal();
    }

    (focusTarget || dialog).focus();
}

export function close(dialog) {
    const state = states.get(dialog);
    if (state) {
        state.wantOpen = false;
    }

    if (dialog.open) {
        dialog.close();
    }
}
