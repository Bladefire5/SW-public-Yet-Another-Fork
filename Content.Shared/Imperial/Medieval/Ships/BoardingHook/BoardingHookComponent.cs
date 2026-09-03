using Robust.Shared.Utility;

namespace Content.Shared.Imperial.Medieval.Ships.BoardingHook;

[RegisterComponent]
public sealed partial class BoardingHookComponent : Component
{
    [DataField]
    public float BaseThrowDistance = 7f;

    [DataField]
    public float ThrowDistancePerStrength = 0.015f;

    [DataField]
    public float MaxTetherDistance = 10f;

    [DataField]
    public float Power = 10f;

    [DataField]
    public string UnwrappedInhandPrefix = "unwrapped";

    [DataField]
    public SpriteSpecifier RopeSprite =
        new SpriteSpecifier.Rsi(new ResPath("Objects/Weapons/Guns/Launchers/grappling_gun.rsi"), "rope");

    public EntityUid? Projectile;

    public EntityUid? User;
}
