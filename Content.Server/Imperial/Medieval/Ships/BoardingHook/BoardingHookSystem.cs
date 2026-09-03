using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Shared.DoAfter;
using Content.Shared.Hands;
using Content.Shared.Imperial.Medieval.Ships.BoardingHook;
using Content.Shared.Imperial.Medieval.Ships.Islands;
using Content.Shared.Imperial.Medieval.Skills;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Content.Shared.Throwing;
using Content.Shared.Trigger;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Server.Imperial.Medieval.Ships.BoardingHook;

public sealed class BoardingHookSystem : SharedBoardingHookSystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ShipGridSystem _shipGrid = default!;
    [Dependency] private readonly ThrownItemSystem _thrown = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BoardingHookComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<BoardingHookComponent, GotUnequippedHandEvent>(OnUnequipped);
        SubscribeLocalEvent<BoardingHookComponent, ItemUnwieldedEvent>(OnUnwielded);
        SubscribeLocalEvent<BoardingHookComponent, ComponentShutdown>(OnHookItemShutdown);
        SubscribeLocalEvent<BoardingHookComponent, BoardingHookPullDoAfterEvent>(OnPullDoAfter);

        SubscribeLocalEvent<BoardingHookProjectileComponent, LandEvent>(OnProjectileLand);
        SubscribeLocalEvent<BoardingHookProjectileComponent, InteractHandEvent>(OnProjectileInteract);
        SubscribeLocalEvent<BoardingHookProjectileComponent, BoardingHookRemoveDoAfterEvent>(OnRemoveDoAfter);
        SubscribeLocalEvent<BoardingHookProjectileComponent, ComponentShutdown>(OnProjectileShutdown);
        SubscribeLocalEvent<BoardingHookProjectileComponent, StartCollideEvent>(OnProjectileCollide);
        SubscribeLocalEvent<BoardingHookProjectileComponent, TriggerEvent>(OnRangeCheck);
    }

    protected override void OnShotAttempted(Entity<BoardingHookComponent> ent, ref ShotAttemptedEvent args)
    {
        if (ent.Comp.Projectile != null)
        {
            TryStartPullDoAfter(ent, args.User);
            args.Cancel();
            return;
        }

        base.OnShotAttempted(ent, ref args);
    }

    protected override void OnGunShot(Entity<BoardingHookComponent> ent, ref GunShotEvent args)
    {
        base.OnGunShot(ent, ref args);

        if (!TryGetGrid(args.User, out var originGrid) ||
            !TryComp<GunComponent>(ent, out var gun))
        {
            return;
        }

        if (!TryGetThrowVector(args.User, gun, GetThrowDistance(ent.Comp, args.User), out var throwVector))
        {
            foreach (var (projectileUid, _) in args.Ammo)
            {
                if (projectileUid is { } projectile)
                    QueueDel(projectile);
            }

            _gun.UpdateBasicEntityAmmoCount(ent.Owner, 1);
            return;
        }

        _item.SetHeldPrefix(ent.Owner, ent.Comp.UnwrappedInhandPrefix);

        foreach (var (projectileUid, _) in args.Ammo)
        {
            if (projectileUid is not { } projectile ||
                !TryComp<BoardingHookProjectileComponent>(projectile, out var projectileComp))
            {
                continue;
            }

            ent.Comp.Projectile = projectile;
            ent.Comp.User = args.User;

            projectileComp.HookItem = ent.Owner;
            projectileComp.User = args.User;
            projectileComp.OriginGrid = originGrid;

            if (TryComp<ThrownItemComponent>(projectile, out var thrown) &&
                TryComp<PhysicsComponent>(projectile, out var body))
            {
                _thrown.StopThrow(projectile, thrown);
                _physics.SetLinearVelocity(projectile, Vector2.Zero, body: body);
                _physics.SetAngularVelocity(projectile, 0f, body: body);
                _throwing.TryThrow(
                    projectile,
                    throwVector,
                    gun.ProjectileSpeedModified,
                    args.User,
                    pushbackRatio: 0f,
                    compensateFriction: true,
                    recoil: false);
            }

            var visuals = EnsureComp<JointVisualsComponent>(projectile);
            visuals.Sprite = ent.Comp.RopeSprite;
            visuals.OffsetA = new Vector2(0f, 0.5f);
            visuals.Target = GetNetEntity(ent.Owner);
            Dirty(projectile, visuals);
            break;
        }
    }

    private bool TryGetPullProjectile(
        Entity<BoardingHookComponent> ent,
        EntityUid user,
        [NotNullWhen(true)] out BoardingHookProjectileComponent? projectileComp)
    {
        projectileComp = null;

        if (ent.Comp.Projectile is not { } projectile ||
            !TryComp(projectile, out projectileComp) ||
            projectileComp.User != user ||
            !CombatMode.IsInCombatMode(user) ||
            Hands.GetActiveItem(user) != ent.Owner ||
            !TryComp<WieldableComponent>(ent, out var wieldable) ||
            !wieldable.Wielded ||
            !Skills.HasSkill(user, SharedSkillsSystem.StrengthId))
        {
            return false;
        }

        return true;
    }

    private void TryStartPullDoAfter(Entity<BoardingHookComponent> ent, EntityUid user)
    {
        if (!TryGetPullProjectile(ent, user, out var projectileComp))
            return;

        if (!projectileComp.Anchored)
        {
            _popup.PopupClient(
                Loc.GetString("boarding-hook-not-anchored"),
                user,
                user,
                PopupType.Small);
            return;
        }

        var time = Math.Max(1f, 7f - Skills.GetSkillLevel(user, SharedSkillsSystem.AgilityId) * 0.3f);
        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            time,
            new BoardingHookPullDoAfterEvent(),
            eventTarget: ent.Owner,
            target: null,
            used: ent.Owner)
        {
            MovementThreshold = 0.1f,
            BreakOnMove = true,
            BlockDuplicate = true,
            CancelDuplicate = false,
            DistanceThreshold = null,
            BreakOnDamage = true,
            RequireCanInteract = false,
            BreakOnDropItem = true,
            BreakOnHandChange = true,
            NeedHand = true,
        };

        _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnActivate(Entity<BoardingHookComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || ent.Comp.Projectile == null)
            return;

        DeleteProjectile(ent);
        args.Handled = true;
    }

    private void OnUnequipped(Entity<BoardingHookComponent> ent, ref GotUnequippedHandEvent args)
    {
        DeleteProjectile(ent);
    }

    private void OnUnwielded(Entity<BoardingHookComponent> ent, ref ItemUnwieldedEvent args)
    {
        DeleteProjectile(ent);
    }

    private void OnHookItemShutdown(Entity<BoardingHookComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Projectile is { } projectile)
            QueueDel(projectile);

        ent.Comp.Projectile = null;
        ent.Comp.User = null;
    }

    private void OnProjectileLand(Entity<BoardingHookProjectileComponent> ent, ref LandEvent args)
    {
        if (ent.Comp.Anchored || !TryGetGrid(ent.Owner, out var grid) || grid == ent.Comp.OriginGrid)
            return;

        TryAnchorProjectile(ent, grid, Transform(ent).Coordinates);
    }

    private void OnProjectileCollide(Entity<BoardingHookProjectileComponent> ent, ref StartCollideEvent args)
    {
        if (ent.Comp.Anchored ||
            args.OurFixtureId != ent.Comp.Fixture ||
            !args.OtherFixture.Hard ||
            !TryComp<ThrownItemComponent>(ent, out var thrown) ||
            !TryComp<PhysicsComponent>(ent, out var body))
        {
            return;
        }

        if (TryGetGrid(args.OtherEntity, out var grid))
            TryAnchorProjectile(ent, grid, TransformSystem.GetMoverCoordinates(args.OtherEntity));

        _physics.SetLinearVelocity(ent, Vector2.Zero, body: body);
        _physics.SetAngularVelocity(ent, 0f, body: body);
        _thrown.LandComponent(ent, thrown, body, thrown.PlayLandSound);
        _thrown.StopThrow(ent, thrown);
    }

    private void OnProjectileInteract(Entity<BoardingHookProjectileComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || !ent.Comp.Anchored || ent.Comp.User == args.User ||
            !Skills.HasSkill(args.User, SharedSkillsSystem.StrengthId))
        {
            return;
        }

        var strength = Math.Clamp(Skills.GetSkillLevel(args.User, SharedSkillsSystem.StrengthId), 1, 20);
        var timeMultiplier = strength >= 10
            ? 1f - (strength - 10) * 0.05f
            : 1f + (10 - strength) * 0.05f;
        var doAfter = new DoAfterArgs(
            EntityManager,
            args.User,
            2f * timeMultiplier,
            new BoardingHookRemoveDoAfterEvent(),
            eventTarget: ent.Owner,
            target: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            BreakOnHandChange = true,
            NeedHand = true,
            BlockDuplicate = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            args.Handled = true;
    }

    private void OnRemoveDoAfter(Entity<BoardingHookProjectileComponent> ent, ref BoardingHookRemoveDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !ent.Comp.Anchored)
            return;

        args.Handled = true;
        QueueDel(ent);
    }

    private void OnPullDoAfter(Entity<BoardingHookComponent> ent, ref BoardingHookPullDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled ||
            ent.Comp.Projectile is not { } projectile ||
            !TryComp<BoardingHookProjectileComponent>(projectile, out var projectileComp) ||
            !projectileComp.Anchored ||
            projectileComp.User != args.User ||
            Hands.GetActiveItem(args.User) != ent.Owner ||
            !TryComp<WieldableComponent>(ent, out var wieldable) ||
            !wieldable.Wielded ||
            !Skills.HasSkill(args.User, SharedSkillsSystem.StrengthId) ||
            !TryGetGrid(args.User, out var userGrid) ||
            !TryGetGrid(projectile, out var hookGrid) ||
            userGrid == hookGrid)
        {
            return;
        }

        var userGridIsIsland = HasComp<IslandComponent>(userGrid);
        var hookGridIsIsland = HasComp<IslandComponent>(hookGrid);
        if (userGridIsIsland && hookGridIsIsland)
        {
            args.Handled = true;
            QueueDel(projectile);
            return;
        }

        var userPosition = TransformSystem.GetMapCoordinates(args.User);
        var hookPosition = TransformSystem.GetMapCoordinates(projectile);
        if (userPosition.MapId != hookPosition.MapId)
            return;

        var strengthPower = ent.Comp.Power *
            (1f + (Skills.GetSkillLevel(args.User, SharedSkillsSystem.StrengthId) - 10) * 0.03f);
        bool success;

        if (hookGridIsIsland)
        {
            success = TryPushGrid(userGrid, hookPosition.Position - userPosition.Position,
                strengthPower);
        }
        else if (userGridIsIsland)
        {
            success = TryPushGrid(hookGrid, userPosition.Position - hookPosition.Position,
                strengthPower);
        }
        else
        {
            var power = strengthPower * 0.75f;
            if (TryGetGridImpulse(userGrid, hookPosition.Position - userPosition.Position,
                    power, out var userBody, out var userImpulse) &&
                TryGetGridImpulse(hookGrid, userPosition.Position - hookPosition.Position,
                    power, out var hookBody, out var hookImpulse))
            {
                ApplyGridImpulse(userGrid, userBody, userImpulse);
                ApplyGridImpulse(hookGrid, hookBody, hookImpulse);
                success = true;
            }
            else
                success = false;
        }

        if (!success)
            return;

        args.Handled = true;
        args.Repeat = true;
    }

    private void OnProjectileShutdown(Entity<BoardingHookProjectileComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent.Comp.HookItem) ||
            !TryComp<BoardingHookComponent>(ent.Comp.HookItem, out var hook) ||
            hook.Projectile != ent.Owner)
        {
            return;
        }

        hook.Projectile = null;
        hook.User = null;
        _gun.UpdateBasicEntityAmmoCount(ent.Comp.HookItem, 1);

        if (TryComp<WieldableComponent>(ent.Comp.HookItem, out var wieldable))
        {
            var prefix = wieldable.Wielded
                ? wieldable.WieldedInhandPrefix
                : wieldable.OldInhandPrefix;
            _item.SetHeldPrefix(ent.Comp.HookItem, prefix);
        }

        UseDelay.TryResetDelay(ent.Comp.HookItem);
    }

    private void OnRangeCheck(Entity<BoardingHookProjectileComponent> ent, ref TriggerEvent args)
    {
        if (args.Key != ent.Comp.RangeCheckTrigger)
            return;

        args.Handled = true;
        if (TerminatingOrDeleted(ent.Comp.HookItem) ||
            TerminatingOrDeleted(ent.Comp.User) ||
            !TryComp<BoardingHookComponent>(ent.Comp.HookItem, out var hook) ||
            hook.Projectile != ent.Owner)
        {
            QueueDel(ent);
            return;
        }

        if (!ent.Comp.Anchored)
            return;

        var projectileMap = TransformSystem.GetMapCoordinates(ent.Owner);
        var userMap = TransformSystem.GetMapCoordinates(ent.Comp.User);
        if (projectileMap.MapId != userMap.MapId ||
            Vector2.Distance(projectileMap.Position, userMap.Position) > hook.MaxTetherDistance)
        {
            QueueDel(ent);
        }
    }

    private bool TryPushGrid(EntityUid gridUid, Vector2 direction, float power)
    {
        if (!TryGetGridImpulse(gridUid, direction, power, out var body, out var impulse))
            return false;

        ApplyGridImpulse(gridUid, body, impulse);
        return true;
    }

    private bool TryGetGridImpulse(
        EntityUid gridUid,
        Vector2 direction,
        float power,
        out PhysicsComponent body,
        out Vector2 impulse)
    {
        body = default!;
        impulse = Vector2.Zero;
        var lengthSquared = direction.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= 0.0001f ||
            !float.IsFinite(power) || power <= 0f ||
            TryComp<ShuttleComponent>(gridUid, out var shuttle) && !shuttle.Enabled ||
            !_shipGrid.TryGetGrid(gridUid, out var grid) ||
            grid.HasLoweredAnchor || grid.TileCount <= 0 ||
            !TryComp<PhysicsComponent>(gridUid, out var foundBody))
        {
            return false;
        }

        body = foundBody!;

        var overloadCeil = _shipGrid.GetMaxWeight(gridUid, grid);
        var impulsePower = grid.TotalWeight <= 0f || grid.TotalWeight <= overloadCeil
            ? power
            : power * overloadCeil / grid.TotalWeight;
        impulse = direction / MathF.Sqrt(lengthSquared) * impulsePower;
        return true;
    }

    private void ApplyGridImpulse(EntityUid gridUid, PhysicsComponent body, Vector2 impulse)
    {
        _physics.WakeBody(gridUid);
        _physics.ApplyLinearImpulse(gridUid, impulse, body: body);
    }

    private bool TryAnchorProjectile(
        Entity<BoardingHookProjectileComponent> ent,
        EntityUid grid,
        EntityCoordinates coordinates)
    {
        if (grid == ent.Comp.OriginGrid ||
            !TryComp<MapGridComponent>(grid, out var mapGrid) ||
            !_map.TryGetTileRef(grid, mapGrid, coordinates, out var tile) ||
            tile.Tile.IsEmpty)
        {
            return false;
        }

        TransformSystem.AnchorEntity((ent.Owner, Transform(ent)), (grid, mapGrid), tile.GridIndices);
        ent.Comp.Anchored = true;
        return true;
    }

    private void DeleteProjectile(Entity<BoardingHookComponent> ent)
    {
        if (ent.Comp.Projectile is not { } projectile)
            return;

        QueueDel(projectile);
    }
}
