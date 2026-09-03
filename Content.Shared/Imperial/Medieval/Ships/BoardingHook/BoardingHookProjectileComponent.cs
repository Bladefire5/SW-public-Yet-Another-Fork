namespace Content.Shared.Imperial.Medieval.Ships.BoardingHook;

[RegisterComponent]
public sealed partial class BoardingHookProjectileComponent : Component
{
    [DataField]
    public string Fixture = "hook";

    [DataField]
    public string RangeCheckTrigger = "boarding-hook-range-check";

    public EntityUid HookItem;

    public EntityUid User;

    public EntityUid OriginGrid;

    public bool Anchored;
}
