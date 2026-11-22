using CarbonFootprint.Domain.Modules.Inventories;

namespace CarbonFootprint.Application.Exports;

/// <summary>Traditional Chinese labels shared by workspace messages and exports.</summary>
public static class InventoryDisplayNames
{
    public static string Stage(LifecycleStage stage) => stage switch
    {
        LifecycleStage.RawMaterial => "原料取得階段",
        LifecycleStage.Manufacturing => "製造階段",
        LifecycleStage.Distribution => "配送與銷售階段",
        LifecycleStage.Use => "使用階段",
        LifecycleStage.EndOfLife => "廢棄處理階段",
        _ => "未定義階段"
    };

    public static string ActivityKind(ActivityDataKind kind) => kind switch
    {
        ActivityDataKind.Material => "原物料投入",
        ActivityDataKind.MaterialTransport => "原物料運輸",
        ActivityDataKind.Energy => "能資源使用",
        ActivityDataKind.ManufacturingWaste => "製造廢棄物",
        ActivityDataKind.OutsourcedTreatmentTransport => "委外處理運輸",
        ActivityDataKind.DistributionTransport => "配送銷售運輸",
        ActivityDataKind.UseEnergy => "使用能源",
        ActivityDataKind.UseConsumable => "使用耗材",
        ActivityDataKind.EndOfLifeTreatment => "廢棄處理",
        ActivityDataKind.EndOfLifeTransport => "廢棄處理運輸",
        _ => "其他活動"
    };

    public static string Geography(string geography) => geography switch
    {
        "TW" => "台灣",
        "Global" => "全球",
        "East Asia" => "東亞",
        "EU" => "歐盟",
        "US" => "美國",
        "CN" => "中國",
        "JP" => "日本",
        _ => geography
    };
}
