// Copies the one-time API key to the clipboard (the key is never put anywhere else) and, when the browser refuses, selects the text so the agent can press Ctrl+C.
// Both functions take what they touch as arguments and never throw: a refused clipboard is an ordinary answer ("false"), not an error.

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

export function selectText(element) {
    try {
        if (!element) {
            return false;
        }

        element.focus();
        element.select();
        if (typeof element.setSelectionRange === 'function') {
            element.setSelectionRange(0, (element.value ?? '').length);
        }

        return true;
    } catch {
        return false;
    }
}
