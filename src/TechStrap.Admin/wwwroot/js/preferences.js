// The agent's browser preferences (UX-BRIEF-admin, My settings): the single-key keyboard shortcuts and the colour theme. They live in the browser, per
// browser, not in the API. The storage and the page are always handed in, so the functions below are pure and tests/TechStrap.Admin.Tests/js/preferences.test.mjs
// can run them under node:test without a DOM. Nothing here may throw: storage can be missing, full or blocked (private windows, site data off), and a bad
// stored value must fall back to the default instead of breaking the page.
//
// PreferencesService (.NET) imports this module once per circuit and calls load() and save().

export const SINGLE_KEY = 'singleKeyShortcuts';
export const THEME = 'theme';

/** The theme values. 'auto' follows the operating system and is the default. */
export const THEMES = ['auto', 'light', 'dark'];

export const DEFAULTS = Object.freeze({ singleKeyShortcuts: true, theme: 'auto' });

// The storage keys carry the app name because the browser shares one localStorage per origin.
const STORAGE_KEYS = Object.freeze({
    [SINGLE_KEY]: 'techstrap.admin.singleKeyShortcuts',
    [THEME]: 'techstrap.admin.theme',
});

function readItem(storage, name) {
    try {
        return storage ? storage.getItem(STORAGE_KEYS[name]) : null;
    } catch {
        return null;
    }
}

/** A stored or requested theme, or 'auto' for anything that is not one of the three values. */
export function normaliseTheme(value) {
    return THEMES.includes(value) ? value : DEFAULTS.theme;
}

/** Reads both preferences. Never throws; a missing, unreadable or unrecognised value is the default. */
export function readPreferences(storage) {
    const singleKey = readItem(storage, SINGLE_KEY);
    const theme = readItem(storage, THEME);
    return {
        singleKeyShortcuts: singleKey === 'false' ? false : DEFAULTS.singleKeyShortcuts,
        theme: normaliseTheme(theme),
    };
}

/**
 * Stores one preference. key is 'singleKeyShortcuts' (value true or false) or 'theme' (value 'auto', 'light' or 'dark'). Returns true when the value was
 * written, false when the key or value is not recognised or the storage refused it (full, blocked, missing). Never throws.
 */
export function writePreference(storage, key, value) {
    if (!storage || !Object.hasOwn(STORAGE_KEYS, key)) {
        return false;
    }

    let text;
    if (key === SINGLE_KEY) {
        if (value !== true && value !== false) {
            return false;
        }

        text = String(value);
    } else {
        if (!THEMES.includes(value)) {
            return false;
        }

        text = value;
    }

    try {
        storage.setItem(STORAGE_KEYS[key], text);
        return true;
    } catch {
        return false;
    }
}

/**
 * Applies the theme to the page. The Admin styles read the data-bs-theme attribute of the root element: 'light' and 'dark' set it, 'auto' removes it so
 * the system preference decides (_color-mode.scss). Returns the theme applied. Never throws.
 */
export function applyTheme(doc, theme) {
    const applied = normaliseTheme(theme);
    try {
        const root = doc && doc.documentElement;
        if (root) {
            if (applied === 'auto') {
                root.removeAttribute('data-bs-theme');
            } else {
                root.setAttribute('data-bs-theme', applied);
            }
        }
    } catch {
        // A page that cannot be themed still works.
    }

    return applied;
}

function browserStorage() {
    try {
        return typeof window === 'undefined' ? null : window.localStorage ?? null;
    } catch {
        return null;
    }
}

function browserDocument() {
    return typeof document === 'undefined' ? null : document;
}

/** Called once by PreferencesService on the first interactive render: reads the stored values, applies the theme, returns the values. */
export function load() {
    const preferences = readPreferences(browserStorage());
    applyTheme(browserDocument(), preferences.theme);
    return preferences;
}

/** Called when the agent changes a choice. The theme is applied even when it could not be stored, so the choice still holds for this page. */
export function save(key, value) {
    const stored = writePreference(browserStorage(), key, value);
    if (key === THEME) {
        applyTheme(browserDocument(), value);
    }

    return stored;
}
