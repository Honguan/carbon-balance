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

    document.querySelectorAll("[data-emission-form]").forEach((form) => {
        const kindSelect = form.querySelector("[name='activityKind']");
        const valueInput = form.querySelector("[name='rawValue']");
        const distanceInput = form.querySelector("[name='transportDistanceKm']");
        const weightInput = form.querySelector("[name='transportWeightKg']");
        const lifetimeInput = form.querySelector("[name='useLifetime']");
        const frequencyInput = form.querySelector("[name='useFrequency']");
        const consumptionInput = form.querySelector("[name='useConsumptionPerUse']");
        const rawUnitSelect = form.querySelector("[name='rawUnitCode']");
        const canonicalUnitSelect = form.querySelector("[name='canonicalUnitCode']");
        const factorSelect = form.querySelector("[name='factorVersionId']");
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
            factorSelect?.querySelectorAll("option[value]").forEach((option) => {
                option.disabled = Boolean(option.value) && option.dataset.factorUnit !== requiredFactorUnit;
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
                if (output) {
                    output.textContent = "完成此項目的活動公式並選擇係數後顯示計算式。";
                }
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
});
