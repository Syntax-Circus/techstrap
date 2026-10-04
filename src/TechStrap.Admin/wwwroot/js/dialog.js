// Opens and closes a native <dialog> for ConfirmDialog. showModal() gives the focus trap and the inert background;
// the caller names the element that takes the first focus (the typed-confirmation input, or the heading), never a button.

export function open(dialog, focusTarget) {
    if (!dialog.open) {
        dialog.showModal();
    }

    (focusTarget || dialog).focus();
}

export function close(dialog) {
    if (dialog.open) {
        dialog.close();
    }
}
