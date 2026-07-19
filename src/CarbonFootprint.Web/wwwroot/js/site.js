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

    document.querySelectorAll("select[data-auto-submit-select]").forEach((select) => {
        select.addEventListener("change", () => {
            if (!select.value || !(select.form instanceof HTMLFormElement)) {
                return;
            }

            if (typeof select.form.requestSubmit === "function") {
                select.form.requestSubmit();
                return;
            }

            select.form.submit();
        });
    });
});
