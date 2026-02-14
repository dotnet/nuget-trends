// Small UI interop helpers for Blazor (avoids eval)
window.uiInterop = {
    getAppVersion: function () {
        return window.__appVersion || 'unknown';
    },

    focusElement: function (selector) {
        var el = document.querySelector(selector);
        if (el) el.focus();
    }
};
