// Theme management interop for Blazor
window.themeInterop = {
    storageKey: 'nuget-trends-theme',
    dotNetRef: null,

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
        this.dotNetRef = dotNetRef;
        const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');

        mediaQuery.addEventListener('change', (e) => {
            if (this.dotNetRef) {
                this.dotNetRef.invokeMethodAsync('OnSystemPreferenceChanged', e.matches);
            }
        });
    }
};

// Loading indicator interop
window.loadingInterop = {
    dotNetRef: null,
    originalFetch: null,

    initialize: function (dotNetRef) {
        this.dotNetRef = dotNetRef;

        // Intercept fetch requests
        this.originalFetch = window.fetch;
        window.fetch = async (...args) => {
            if (this.dotNetRef) {
                await this.dotNetRef.invokeMethodAsync('OnRequestStarted');
            }

            try {
                return await this.originalFetch(...args);
            } finally {
                if (this.dotNetRef) {
                    await this.dotNetRef.invokeMethodAsync('OnRequestEnded');
                }
            }
        };
    },

    dispose: function () {
        if (this.originalFetch) {
            window.fetch = this.originalFetch;
        }
        this.dotNetRef = null;
    }
};
