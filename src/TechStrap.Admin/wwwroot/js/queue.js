// Keeps the keyboard-selected queue row on screen (j / k). Rows never reorder; only the scroll position moves.

export function scrollSelectedIntoView() {
    const row = document.querySelector('tr[aria-current="true"]');
    if (row) {
        row.scrollIntoView({ block: 'nearest' });
    }
}
