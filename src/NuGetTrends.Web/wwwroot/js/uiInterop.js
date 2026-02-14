// Small UI interop helpers for Blazor (avoids eval)
window.uiInterop = {
    getAppVersion: function () {
        var meta = document.querySelector('meta[name="app-version"]');
        return (meta && meta.getAttribute('content')) || 'unknown';
    },

    focusElement: function (selector) {
        var el = document.querySelector(selector);
        if (el) el.focus();
    }
};
