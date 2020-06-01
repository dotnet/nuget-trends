"use strict";

window.appSearchInput = {
    assemblyname: "NuGetTrends.Portal.BlazorWasm",
    setFocus: function (element) {
        if (element) {
            element.focus();
        }
    },
    // No need to remove the event listeners later, the browser will clean this up automagically.
    addKeyDownEventListener: function (element) {
        element.addEventListener('keydown', function (event) {
            var key = event.key;

            if (key === "Enter") {
                event.preventDefault();
            }
        });
    }
};
