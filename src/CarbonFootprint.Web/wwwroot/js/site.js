const initializeSite = (root = document) => {
    root.querySelectorAll("[data-history-back]").forEach((link) => {
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

    root.querySelectorAll("select[data-controlled-other]").forEach((select) => {
        const target = root.querySelector(select.dataset.otherTarget ?? "");
        if (!(target instanceof HTMLInputElement) && !(target instanceof HTMLTextAreaElement)) {
            return;
        }
        const sync = () => {
            const enabled = select.value === "__other__";
            target.hidden = !enabled;
            target.required = enabled;
            if (!enabled) target.value = "";
        };
        select.addEventListener("change", sync);
        sync();
    });

    root.querySelectorAll("select[data-auto-submit-select]").forEach((select) => {
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

    root.querySelectorAll("select:has(option[data-pcr-option])").forEach((select) => {
        const form = select.form;
        const periodEnd = form?.querySelector("[name='periodEnd']");
        if (!(periodEnd instanceof HTMLInputElement)) {
            return;
        }

        const syncPcrValidity = () => {
            const selectedDate = periodEnd.value;
            select.querySelectorAll("option[data-pcr-option]").forEach((option) => {
                const validFrom = option.dataset.validFrom ?? "";
                const validTo = option.dataset.validTo ?? "";
                const unavailable = Boolean(selectedDate)
                    && ((validFrom && selectedDate < validFrom) || (validTo && selectedDate > validTo));
                option.disabled = unavailable;
                if (unavailable && option.selected) {
                    select.value = "";
                }
            });
        };

        periodEnd.addEventListener("change", syncPcrValidity);
        syncPcrValidity();
    });

    root.querySelectorAll("[data-factor-list-filter]").forEach((input) => {
        const list = root.querySelector("[data-factor-list]");
        if (!(input instanceof HTMLInputElement) || !list) {
            return;
        }

        input.addEventListener("input", () => {
            const query = input.value.trim().toLocaleLowerCase("zh-TW");
            list.querySelectorAll("[data-factor-list-item]").forEach((item) => {
                item.hidden = Boolean(query) && !item.textContent.toLocaleLowerCase("zh-TW").includes(query);
            });
        });
    });

    root.querySelectorAll("[data-emission-form]").forEach((form) => {
        const kindSelect = form.querySelector("[name='activityKind']");
        const valueInput = form.querySelector("[name='rawValue']");
        const distanceInput = form.querySelector("[name='transportDistanceKm']");
        const weightInput = form.querySelector("[name='transportWeightKg']");
        const lifetimeInput = form.querySelector("[name='useLifetime']");
        const frequencyInput = form.querySelector("[name='useFrequency']");
        const consumptionInput = form.querySelector("[name='useConsumptionPerUse']");
        const rawUnitSelect = form.querySelector("[name='rawUnitCode']");
        const canonicalUnitSelect = form.querySelector("[name='canonicalUnitCode']");
        const factorFilter = form.querySelector("[data-factor-filter]");
        const factorSelect = form.querySelector("[data-factor-select]");
        const allocationInput = form.querySelector("[name='allocationFactor']");
        const output = form.querySelector("[data-emission-preview]");
        const selectedFormulaKind = () => kindSelect?.selectedOptions[0]?.dataset.formulaKind;

        const setGroupState = (selector, enabled) => {
            form.querySelectorAll(selector).forEach((container) => {
                container.hidden = !enabled;
                container.querySelectorAll("input, select").forEach((input) => {
                    input.required = enabled;
                });
            });
        };

        const deriveActivity = () => {
            const formulaKind = selectedFormulaKind();
            if (formulaKind === "transport") {
                const distance = Number(distanceInput?.value);
                const weight = Number(weightInput?.value);
                return distanceInput?.value && weightInput?.value
                    ? { value: distance * weight / 1000, unit: "tonne-km", trace: `${distanceInput.value} km × ${weightInput.value} kg ÷ 1000` }
                    : null;
            }

            if (formulaKind === "use") {
                const lifetime = Number(lifetimeInput?.value);
                const frequency = Number(frequencyInput?.value);
                const consumption = Number(consumptionInput?.value);
                return lifetimeInput?.value && frequencyInput?.value && consumptionInput?.value
                    ? { value: lifetime * frequency * consumption, unit: rawUnitSelect?.value, trace: `${lifetimeInput.value} × ${frequencyInput.value} × ${consumptionInput.value}` }
                    : null;
            }

            return valueInput?.value
                ? { value: Number(valueInput.value), unit: rawUnitSelect?.value, trace: valueInput.value }
                : null;
        };

        const updatePreview = () => {
            const formulaKind = selectedFormulaKind();
            const isTransport = formulaKind === "transport";
            const isUse = formulaKind === "use";
            setGroupState("[data-direct-input]", !isTransport && !isUse);
            setGroupState("[data-transport-input]", isTransport);
            setGroupState("[data-use-input]", isUse);
            setGroupState("[data-unit-input]", true);
            if (isTransport) {
                rawUnitSelect.value = "tonne-km";
                canonicalUnitSelect.value = "tonne-km";
            }

            const requiredFactorUnit = isTransport ? "tonne-km" : canonicalUnitSelect?.value;
            const factorQuery = factorFilter?.value.trim().toLocaleLowerCase("zh-TW") ?? "";
            factorSelect?.querySelectorAll("option[value]").forEach((option) => {
                const matchesUnit = !option.value || option.dataset.factorUnit === requiredFactorUnit;
                const matchesQuery = !factorQuery
                    || (option.dataset.factorSearch ?? option.textContent).toLocaleLowerCase("zh-TW").includes(factorQuery);
                option.disabled = Boolean(option.value) && !matchesUnit;
                option.hidden = Boolean(option.value) && !matchesQuery;
            });
            if (factorSelect?.selectedOptions[0]?.disabled || factorSelect?.selectedOptions[0]?.hidden) {
                factorSelect.value = "";
            }

            const factorOption = factorSelect?.selectedOptions[0];
            const activity = deriveActivity();
            const factorValue = Number(factorOption?.dataset.factorValue);
            const allocation = Number(allocationInput?.value);
            const canonicalUnit = isTransport ? "tonne-km" : canonicalUnitSelect?.value;
            const factorUnit = factorOption?.dataset.factorUnit;
            if (!output || !activity || !factorOption?.value || !allocationInput?.value) {
                if (output) output.textContent = "完成活動量輸入並選擇係數後顯示計算式。";
                return;
            }

            const expression = `${activity.trace} = ${activity.value} ${activity.unit} → ${canonicalUnit} × ${factorOption.dataset.factorValue} kgCO2e/${factorUnit} × ${allocationInput.value}`;
            output.textContent = activity.unit === canonicalUnit && canonicalUnit === factorUnit
                ? `${expression} = ${(activity.value * factorValue * allocation).toLocaleString("zh-TW")} kgCO2e`
                : `${expression}；儲存時先執行受控單位換算，再計算排放量。`;
        };

        form.addEventListener("input", updatePreview);
        form.addEventListener("change", updatePreview);
        updatePreview();
    });
};

let workspaceNavigationSequence = 0;
let workspaceNavigationController;

const loadWorkspaceContent = async (link, replaceHistory) => {
    const url = new URL(link.href, window.location.origin);
    const workspacePath = url.pathname.toLowerCase();
    if (url.origin !== window.location.origin || (workspacePath !== "/workspace" && !workspacePath.startsWith("/workspace/"))) {
        return false;
    }

    const sequence = ++workspaceNavigationSequence;
    workspaceNavigationController?.abort();
    const controller = new AbortController();
    workspaceNavigationController = controller;
    let response;
    try {
        response = await fetch(url, {
            headers: { "X-Requested-With": "XMLHttpRequest" },
            credentials: "same-origin",
            signal: controller.signal
        });
    } catch (error) {
        if (error?.name === "AbortError") {
            return true;
        }
        throw error;
    }
    if (sequence !== workspaceNavigationSequence) {
        return true;
    }
    if (!response.ok) {
        return false;
    }

    const html = await response.text();
    if (sequence !== workspaceNavigationSequence) {
        return true;
    }
    const parsed = new DOMParser().parseFromString(html, "text/html");
    const nextContent = parsed.querySelector("[data-workspace-content]");
    const currentContent = document.querySelector("[data-workspace-content]");
    if (!nextContent || !currentContent) {
        return false;
    }

    currentContent.replaceWith(nextContent);
    document.querySelectorAll("[data-workspace-nav]").forEach((navLink) => {
        const navUrl = new URL(navLink.href, window.location.origin);
        navLink.setAttribute("aria-current", navUrl.href === url.href ? "page" : "");
    });
    if (replaceHistory) {
        window.history.pushState({ workspace: true }, "", url.href);
    }
    document.title = parsed.title;
    initializeSite(nextContent);
    window.scrollTo({ top: 0, behavior: "smooth" });
    return true;
};

document.addEventListener("DOMContentLoaded", () => {
    initializeSite();
    document.addEventListener("click", async (event) => {
        const link = event.target instanceof Element
            ? event.target.closest("a[data-workspace-nav]")
            : null;
        if (!(link instanceof HTMLAnchorElement) || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
            return;
        }

        event.preventDefault();
        try {
            if (!await loadWorkspaceContent(link, true)) {
                window.location.assign(link.href);
            }
        } catch {
            window.location.assign(link.href);
        }
    });

    window.addEventListener("popstate", async () => {
        const link = document.createElement("a");
        link.href = window.location.href;
        try {
            if (!await loadWorkspaceContent(link, false)) {
                window.location.reload();
            }
        } catch {
            window.location.reload();
        }
    });
});
