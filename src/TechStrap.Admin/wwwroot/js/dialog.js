// Opens and closes a native <dialog> for ConfirmDialog. showModal() gives the focus trap and the inert background;
// the caller names the element that takes the first focus (the typed-confirmation input, or the heading), never a button.
//
// The native dialog must never close behind .NET's back. Chromium's close-watcher rule makes the cancel event of a repeated Esc (no user activation in
// between) non-cancelable, so preventing "cancel" is not enough. The dialog carries data-lock ("busy" while a request runs, "hold" while its owner
// refuses to be dismissed); while it is set, Esc is stopped at the keydown, and a "close" the script did not ask for re-shows the dialog. When the dialog
// may be dismissed, a stray close is reported to .NET, which treats it as a cancel. Esc is also stopped on the document (capture phase) while the
// dialog is open and locked, because a disabled focused control drops focus to <body> and the dialog's own listener would never see the key.

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

function onKey(dialog, state, event) {
    const lock = lockOf(dialog);
    if (event.key !== 'Escape' || !lock) {
        return;
    }

    event.preventDefault();
    // The same event reaches the document (capture) and, when focus is inside, the dialog: .NET hears about it once.
    if (lock === 'hold' && state.lastEscape !== event) {
        tell(state, 'EscapePressed');
    }

    state.lastEscape = event;
}

function watch(dialog, state) {
    dialog.addEventListener('keydown', (event) => onKey(dialog, state, event));

    dialog.addEventListener('close', () => {
        // A close for an earlier cycle: the dialog is showing again, so there is nothing to report or to re-show.
        if (!state.wantOpen || dialog.open) {
            return;
        }

        if (lockOf(dialog)) {
            dialog.showModal();
            dialog.focus();
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
    if (!state.onDocumentKey) {
        // While a request runs, the focused button or input becomes disabled and focus falls to <body>, so the dialog never sees Esc.
        // A capture-phase listener on the document does, and stops it.
        state.onDocumentKey = (event) => onKey(dialog, state, event);
        document.addEventListener('keydown', state.onDocumentKey, true);
    }

    if (!dialog.open) {
        dialog.showModal();
    }

    (focusTarget || dialog).focus();
}

export function close(dialog) {
    const state = states.get(dialog);
    if (state) {
        state.wantOpen = false;
        if (state.onDocumentKey) {
            document.removeEventListener('keydown', state.onDocumentKey, true);
            state.onDocumentKey = null;
        }
    }

    if (dialog.open) {
        dialog.close();
    }
}
