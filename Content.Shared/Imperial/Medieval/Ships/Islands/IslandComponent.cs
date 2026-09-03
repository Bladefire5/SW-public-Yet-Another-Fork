namespace Content.Shared.Imperial.Medieval.Ships.Islands;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class IslandComponent : Component
{
    [DataField]
    public IslandGenerationGroup GenerationGroup = IslandGenerationGroup.NonGenerated;
}

public enum IslandGenerationGroup : byte
{
    Low,
    Medium,
    High,
    NonGenerated,
}
