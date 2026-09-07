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
            target.disabled = !enabled;
        };
        select.addEventListener("change", sync);
        sync();
    });

    root.querySelectorAll("select[data-auto-submit-select]").forEach((select) => {
        let previousValue = select.value;
        select.addEventListener("change", () => {
            if (!select.value || !(select.form instanceof HTMLFormElement)) {
                return;
            }

            select.form.requestSubmit();
            if (select.form.dataset.submitting !== "true") {
                select.value = previousValue;
            } else {
                previousValue = select.value;
            }
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

        const status = document.createElement("p");
        status.setAttribute("role", "status");
        input.after(status);
        const filter = () => {
            const query = input.value.trim().toLocaleLowerCase("zh-TW");
            let visibleCount = 0;
            list.querySelectorAll("[data-factor-list-item]").forEach((item) => {
                item.hidden = Boolean(query) && !item.textContent.toLocaleLowerCase("zh-TW").includes(query);
                if (!item.hidden) visibleCount++;
            });
            status.textContent = visibleCount ? `顯示 ${visibleCount} 筆係數。` : "沒有符合的係數，請調整搜尋條件。";
        };
        input.addEventListener("input", filter);
        filter();
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
                    input.disabled = !enabled;
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
                option.hidden = Boolean(option.value) && !matchesQuery && !option.selected;
            });
            if (factorSelect?.selectedOptions[0]?.disabled) {
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

document.addEventListener("DOMContentLoaded", () => {
    initializeSite();
    const snapshot = (form) => JSON.stringify(Array.from(new FormData(form), ([name, value]) =>
        [name, value instanceof File ? [value.name, value.size, value.name ? value.lastModified : 0] : value]));
    const originals = new Map(Array.from(document.querySelectorAll("form[method='post']"),
        (form) => [form, snapshot(form)]));
    let leaving = false;
    const hasChanges = (except) => Array.from(originals).some(([form, original]) =>
        form !== except && (form.dataset.unsaved === "true" || snapshot(form) !== original));

    window.addEventListener("beforeunload", (event) => {
        if (!leaving && hasChanges()) {
            event.preventDefault();
            event.returnValue = "";
        }
    });
    document.addEventListener("submit", (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || event.defaultPrevented) return;
        if (form.dataset.submitting === "true" || !form.checkValidity()) {
            event.preventDefault();
            return;
        }
        if (form.dataset.download !== undefined) return;
        if (hasChanges(form) && !window.confirm("其他表單尚未儲存，確定離開並捨棄變更？")) {
            event.preventDefault();
            return;
        }
        form.dataset.submitting = "true";
        form.setAttribute("aria-busy", "true");
        form.querySelectorAll("button[type='submit'], input[type='submit']").forEach((button) => {
            // Keep named submitters enabled so their values reach the server.
            button.setAttribute("aria-disabled", "true");
        });
        leaving = true;
    });
    window.addEventListener("pageshow", () => {
        leaving = false;
        document.querySelectorAll("form[data-submitting='true']").forEach((form) => {
            delete form.dataset.submitting;
            form.removeAttribute("aria-busy");
            form.querySelectorAll("[aria-disabled='true']").forEach((button) => button.removeAttribute("aria-disabled"));
        });
    });
});
