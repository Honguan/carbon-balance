document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("[data-history-back]").forEach((link) => {
        link.addEventListener("click", (event) => {
            if (!document.referrer || window.history.length <= 1) {
                return;
            }

            try {
                const previousUrl = new URL(document.referrer);
                if (previousUrl.origin !== window.location.origin) {
                    return;
                }

                event.preventDefault();
                window.history.back();
            } catch {
                // Keep the anchor fallback when the referrer cannot be parsed.
            }
        });
    });
});
