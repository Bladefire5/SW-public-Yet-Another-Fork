using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.SmithingSystem.Behaviours;

[Serializable] [NetSerializable]
public enum ItemQuality : byte
{
    Worst,
    ReallyBad,
    Bad,
    Default,
    Good,
    Excellent
}

public static class ItemQualityDurabilityMultipliers
{
    public static readonly IReadOnlyList<float> Values = new[]
    {
        0.5f,
        0.5f,
        0.75f,
        1f,
        1.125f,
        1.25f,
    };

    public static float Get(ItemQuality quality)
    {
        var index = (int) quality;
        return index >= 0 && index < Values.Count ? Values[index] : 1f;
    }
}
