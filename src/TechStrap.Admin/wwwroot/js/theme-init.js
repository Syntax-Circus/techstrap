// Applies the stored colour theme before the first paint (D-041, D-042). It is a classic, blocking script in <head>, loaded before the stylesheets, so a light or
// dark choice does not flash the other theme while the circuit connects. A module would be deferred and would run after the page had painted.
//
// It reads the same localStorage key as preferences.js ('techstrap.admin.theme') and sets or removes data-bs-theme on <html> exactly as applyTheme() there does:
// 'light' and 'dark' set it, anything else (auto, nothing stored, a bad value) removes it so the system preference decides (_color-mode.scss).
// It must never throw: storage can be missing, blocked or full, and a page that cannot be themed still works. tests/TechStrap.Admin.Tests/js/theme-init.test.mjs
// runs it under node:test and checks it stays in step with preferences.js.
(function () {
    try {
        var stored = null;
        try {
            stored = window.localStorage.getItem('techstrap.admin.theme');
        } catch (storageError) {
            stored = null;
        }

        var root = document.documentElement;
        if (stored === 'light' || stored === 'dark') {
            root.setAttribute('data-bs-theme', stored);
        } else {
            root.removeAttribute('data-bs-theme');
        }
    } catch (error) {
        // No window, no document, a frozen element: leave the page as it is.
    }
})();
