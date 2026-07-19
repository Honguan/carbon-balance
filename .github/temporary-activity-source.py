from pathlib import Path


def rep(path_text, old, new):
    path = Path(path_text)
    text = path.read_text()
    if old not in text:
        raise SystemExit(f"target not found: {path_text}: {old[:100]}")
    path.write_text(text.replace(old, new, 1))

rep(
    'src/CarbonFootprint.Infrastructure/Persistence/Records.cs',
    '''    public string SupplierOrScenario { get; set; } = string.Empty;
    public decimal RawValue { get; set; }
''',
    '''    public string SupplierOrScenario { get; set; } = string.Empty;
    public string EquipmentCategory { get; set; } = string.Empty;
    public string DataSourceType { get; set; } = string.Empty;
    public string DataProvider { get; set; } = string.Empty;
    public string CollectionMethod { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
    public decimal RawValue { get; set; }
''')

rep(
    'src/CarbonFootprint.Infrastructure/Persistence/CarbonFootprintDbContext.cs',
    '''            entity.Property(item => item.SupplierOrScenario).HasMaxLength(1000);
            entity.Property(item => item.RawValue).HasPrecision(30, 12);
''',
    '''            entity.Property(item => item.SupplierOrScenario).HasMaxLength(1000);
            entity.Property(item => item.EquipmentCategory).HasMaxLength(200);
            entity.Property(item => item.DataSourceType).HasMaxLength(200);
            entity.Property(item => item.DataProvider).HasMaxLength(300);
            entity.Property(item => item.CollectionMethod).HasMaxLength(300);
            entity.Property(item => item.SourceReference).HasMaxLength(500);
            entity.Property(item => item.RawValue).HasPrecision(30, 12);
''')

rep(
    'src/CarbonFootprint.Web/Pages/Workspace.cshtml.cs',
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
                RawValue = derivedAmount.Value,
''',
    '''                SupplierOrScenario = string.Join(
                    "｜",
                    new[] { supplierOrScenario?.Trim(), $"計算基礎：{derivedAmount.FormulaTrace}" }
                        .Where(item => !string.IsNullOrWhiteSpace(item))),
                EquipmentCategory = equipment,
                DataSourceType = sourceType,
                DataProvider = provider,
                CollectionMethod = method,
                SourceReference = sourceReference.Trim(),
                RawValue = derivedAmount.Value,
''')

rep(
    'src/CarbonFootprint.Domain/Modules/Inventories/InventoryModels.cs',
    '''    string DataQuality = "primary",
    string AmountFormulaId = "direct-activity-amount-v1",
    string FormulaInputsJson = "{}");
''',
    '''    string DataQuality = "primary",
    string AmountFormulaId = "direct-activity-amount-v1",
    string FormulaInputsJson = "{}",
    string EquipmentCategory = "",
    string DataSourceType = "",
    string DataProvider = "",
    string CollectionMethod = "",
    string SourceReference = "");
''')

rep(
    'src/CarbonFootprint.Web/Pages/Workspace.cshtml.cs',
    '''                    activity.DataQuality,
                    activity.AmountFormulaId,
                    activity.FormulaInputsJson);
''',
    '''                    activity.DataQuality,
                    activity.AmountFormulaId,
                    activity.FormulaInputsJson,
                    activity.EquipmentCategory,
                    activity.DataSourceType,
                    activity.DataProvider,
                    activity.CollectionMethod,
                    activity.SourceReference);
''')

rep(
    'src/CarbonFootprint.Domain/Modules/Calculations/CanonicalManifest.cs',
    '''                writer.WriteString("supplierOrScenario", activity.SupplierOrScenario);
                writer.WriteNumber("rawValue", activity.RawValue);
''',
    '''                writer.WriteString("supplierOrScenario", activity.SupplierOrScenario);
                writer.WriteString("equipmentCategory", activity.EquipmentCategory);
                writer.WriteString("dataSourceType", activity.DataSourceType);
                writer.WriteString("dataProvider", activity.DataProvider);
                writer.WriteString("collectionMethod", activity.CollectionMethod);
                writer.WriteString("sourceReference", activity.SourceReference);
                writer.WriteNumber("rawValue", activity.RawValue);
''')
