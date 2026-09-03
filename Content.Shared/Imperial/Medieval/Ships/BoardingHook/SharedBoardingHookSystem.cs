using System.Numerics;
using Content.Shared.CombatMode;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Imperial.Medieval.Skills;
using Content.Shared.Timing;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;

namespace Content.Shared.Imperial.Medieval.Ships.BoardingHook;

public abstract class SharedBoardingHookSystem : EntitySystem
{
    [Dependency] protected readonly SharedCombatModeSystem CombatMode = default!;
    [Dependency] protected readonly SharedHandsSystem Hands = default!;
    [Dependency] protected readonly SharedSkillsSystem Skills = default!;
    [Dependency] protected readonly SharedTransformSystem TransformSystem = default!;
    [Dependency] protected readonly UseDelaySystem UseDelay = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BoardingHookComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<BoardingHookComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<BoardingHookComponent, ShotAttemptedEvent>(OnShotAttempted);
    }

    private void OnAttemptShoot(Entity<BoardingHookComponent> ent, ref AttemptShootEvent args)
    {
        if (!CombatMode.IsInCombatMode(args.User) ||
            Hands.GetActiveItem(args.User) != ent.Owner ||
            !TryComp<WieldableComponent>(ent, out var wieldable) ||
            !wieldable.Wielded ||
            !Skills.HasSkill(args.User, SharedSkillsSystem.StrengthId) ||
            !TryGetGrid(args.User, out _) ||
            !TryComp<GunComponent>(ent, out var gun) ||
            !TryGetThrowVector(args.User, gun, GetThrowDistance(ent.Comp, args.User), out _))
        {
            args.Cancelled = true;
            args.Message = Loc.GetString("boarding-hook-cannot-throw");
            return;
        }

        args.ThrowItems = true;
    }

    protected virtual void OnGunShot(Entity<BoardingHookComponent> ent, ref GunShotEvent args)
    {
        if (TryComp<GunComponent>(ent, out var gun))
            _audio.PlayPredicted(gun.SoundGunshotModified, ent.Owner, args.User);
    }

    protected virtual void OnShotAttempted(Entity<BoardingHookComponent> ent, ref ShotAttemptedEvent args)
    {
        if (UseDelay.IsDelayed(ent.Owner) ||
            TryComp<BasicEntityAmmoProviderComponent>(ent, out var ammo) && ammo.Count == 0)
        {
            args.Cancel();
        }
    }

    protected float GetThrowDistance(BoardingHookComponent component, EntityUid user)
    {
        var strength = Skills.GetSkillLevel(user, SharedSkillsSystem.StrengthId);
        return component.BaseThrowDistance * (1f + strength * component.ThrowDistancePerStrength);
    }

    protected bool TryGetThrowVector(EntityUid user, GunComponent gun, float maxDistance, out Vector2 throwVector)
    {
        throwVector = Vector2.Zero;
        if (gun.ShootCoordinates is not { } shootCoordinates)
            return false;

        var sourceMap = TransformSystem.GetMapCoordinates(user);
        var targetMap = TransformSystem.ToMapCoordinates(shootCoordinates);
        if (sourceMap.MapId != targetMap.MapId)
            return false;

        throwVector = targetMap.Position - sourceMap.Position;
        var throwLength = throwVector.Length();
        if (!float.IsFinite(throwLength) || throwLength <= 0.0001f)
            return false;

        if (throwLength > maxDistance)
            throwVector *= maxDistance / throwLength;

        return true;
    }

    protected bool TryGetGrid(EntityUid uid, out EntityUid grid)
    {
        grid = TransformSystem.GetMoverCoordinates(uid).EntityId;
        return HasComp<MapGridComponent>(grid);
    }
}
