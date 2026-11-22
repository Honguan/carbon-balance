namespace CarbonFootprint.Domain.Modules.Standards;

public enum PcrImpactReason
{
    None,
    Superseded,
    Withdrawn,
    Expired
}

public static class PcrImpactAnalysis
{
    /// <summary>
    /// Why a project bound to this PCR version can no longer rely on it. Expiry is judged against the
    /// inventory period end, the same date <see cref="PcrRuleEngine"/> uses, so both always agree.
    /// </summary>
    public static PcrImpactReason Classify(PcrRuleSetVersion ruleSet, DateOnly inventoryPeriodEnd)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        if (ruleSet.PublicationStatus == PcrPublicationStatus.Withdrawn) return PcrImpactReason.Withdrawn;
        if (ruleSet.DeprecatedAt is not null) return PcrImpactReason.Superseded;
        if (ruleSet.ExpiryDate is { } expiry && expiry < inventoryPeriodEnd) return PcrImpactReason.Expired;
        return PcrImpactReason.None;
    }
}
