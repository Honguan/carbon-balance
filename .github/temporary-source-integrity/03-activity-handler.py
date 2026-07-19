from pathlib import Path

path = Path("src/CarbonFootprint.Web/Pages/Workspace.cshtml.cs")
text = path.read_text()


def rep(old, new):
    global text
    if old not in text:
        raise SystemExit(f"activity handler target not found: {old[:100]}")
    text = text.replace(old, new, 1)

rep(
'''        string activityName,
        string supplierOrScenario,
        decimal? rawValue,
''',
'''        string activityName,
        string activityNameOther,
        string supplierOrScenario,
        string equipmentCategory,
        string equipmentCategoryOther,
        string dataSourceType,
        string dataSourceTypeOther,
        string dataProviderType,
        string dataProviderOther,
        string collectionMethod,
        string collectionMethodOther,
        string sourceReference,
        decimal? rawValue,
''')

rep(
'''        if (string.IsNullOrWhiteSpace(activityName)
            || !ActivityKindRules.IsAllowed(lifecycleStage, activityKind)
            || allocationFactor is null or <= 0m or > 1m
            || string.IsNullOrWhiteSpace(dataQuality)
            || (isEstimated && string.IsNullOrWhiteSpace(activityEstimationReason)))
        {
            ModelState.AddModelError("activity", "活動名稱與非負活動量為必填。");
''',
'''        var hasName = TryResolveControlledValue(activityName, activityNameOther, out var resolvedName);
        var hasEquipment = TryResolveOptionalControlledValue(equipmentCategory, equipmentCategoryOther, out var equipment);
        var hasSource = TryResolveControlledValue(dataSourceType, dataSourceTypeOther, out var sourceType);
        var hasProvider = TryResolveControlledValue(dataProviderType, dataProviderOther, out var provider);
        var hasMethod = TryResolveControlledValue(collectionMethod, collectionMethodOther, out var method);
        if (!hasName
            || !hasEquipment
            || !hasSource
            || !hasProvider
            || !hasMethod
            || string.IsNullOrWhiteSpace(sourceReference)
            || !ActivityKindRules.IsAllowed(lifecycleStage, activityKind)
            || allocationFactor is null or <= 0m or > 1m
            || string.IsNullOrWhiteSpace(dataQuality)
            || (isEstimated && string.IsNullOrWhiteSpace(activityEstimationReason)))
        {
            ModelState.AddModelError("activity", "活動項目、來源類型、提供者、取得方式與來源參照皆為必填。");
''')

rep('Name = activityName.Trim(),', 'Name = resolvedName,')
rep(
'''                SupplierOrScenario = string.Join(
                    "｜",
                    new[] { supplierOrScenario?.Trim(), $"計算基礎：{derivedAmount.FormulaTrace}" }
                        .Where(item => !string.IsNullOrWhiteSpace(item))),
''',
'''                SupplierOrScenario = string.Join(
                    "｜",
                    new[]
                    {
                        supplierOrScenario?.Trim(),
                        string.IsNullOrWhiteSpace(equipment) ? null : $"設備類別：{equipment}",
                        $"資料來源：{sourceType}",
                        $"資料提供者：{provider}",
                        $"取得方式：{method}",
                        $"來源參照：{sourceReference.Trim()}",
                        $"計算基礎：{derivedAmount.FormulaTrace}"
                    }.Where(item => !string.IsNullOrWhiteSpace(item))),
''')
path.write_text(text)
