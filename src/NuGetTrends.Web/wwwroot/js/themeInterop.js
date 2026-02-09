// Theme management interop for Blazor
window.themeInterop = {
    storageKey: 'nuget-trends-theme',
    dotNetRef: null,
    _mediaQuery: null,
    _listener: null,

    getPreference: function () {
        return localStorage.getItem(this.storageKey);
    },

    setPreference: function (preference) {
        localStorage.setItem(this.storageKey, preference);
    },

    getSystemPreference: function () {
        return window.matchMedia('(prefers-color-scheme: dark)').matches;
    },

    applyTheme: function (theme) {
        const body = document.body;
        if (theme === 'dark') {
            body.classList.add('dark-theme');
            body.classList.remove('light-theme');
        } else {
            body.classList.add('light-theme');
            body.classList.remove('dark-theme');
        }
    },

    watchSystemPreference: function (dotNetRef) {
        this.unwatchSystemPreference();
        this.dotNetRef = dotNetRef;
        this._mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');

        this._listener = (e) => {
            if (this.dotNetRef) {
                this.dotNetRef.invokeMethodAsync('OnSystemPreferenceChanged', e.matches);
            }
        };

        this._mediaQuery.addEventListener('change', this._listener);
    },

    unwatchSystemPreference: function () {
        if (this._mediaQuery && this._listener) {
            this._mediaQuery.removeEventListener('change', this._listener);
        }
        this._mediaQuery = null;
        this._listener = null;
        if (this.dotNetRef) {
            this.dotNetRef.dispose();
            this.dotNetRef = null;
        }
    }
};
