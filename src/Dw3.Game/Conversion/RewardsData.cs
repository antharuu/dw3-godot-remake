namespace Dw3Production.Conversion;

/// <summary>The m6-rewards-v1 contract: every battle reward plus the growth tables.</summary>
public sealed record RewardsData(IReadOnlyList<RewardEntry> Rewards, GrowthTablesData Growth);
