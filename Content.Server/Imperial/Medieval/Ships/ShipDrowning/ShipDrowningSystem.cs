using System;
using System.Collections.Generic;
using Content.Server.Imperial.Medieval.Ships;
using Content.Shared.Imperial.Medieval.Ships.ShipDrowning;
using Content.Shared.Movement.Components;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Configuration;
using Content.Shared.Imperial.Medieval.Administration.Ships;

namespace Content.Server.Imperial.Medieval.Ships.ShipDrowning;

public sealed class ShipDrowningSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ShipGridSystem _shipGrid = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private const float UpdateDelaySeconds = 1f;

    private readonly List<EntityUid> _gridChildren = new();
    private TimeSpan _nextCheckTime;
    private TimeSpan _lastUpdateTime;

    public override void Initialize()
    {
        _nextCheckTime = _timing.CurTime + TimeSpan.FromSeconds(UpdateDelaySeconds);
        _lastUpdateTime = _timing.CurTime;
        SubscribeLocalEvent<ShipDrowningComponent, EntityTerminatingEvent>(OnShipTerminating);
    }

    public override void Update(float frameTime)
    {
        var curTime = _timing.CurTime;
        if (curTime < _nextCheckTime)
            return;

        _nextCheckTime = curTime + TimeSpan.FromSeconds(UpdateDelaySeconds);
        var elapsedSeconds = Math.Max(0f, (float) (curTime - _lastUpdateTime).TotalSeconds);
        _lastUpdateTime = curTime;

        var enumerator = EntityQueryEnumerator<ShipDrowningComponent, ShipGridComponent>();
        while (enumerator.MoveNext(out var uid, out var drowning, out var grid))
        {
            if (!float.IsFinite(drowning.DrownLevel))
                drowning.DrownLevel = 0f;

            var previousDrownLevel = drowning.DrownLevel;
            var previousDrownMaxLevel = drowning.DrownMaxLevel;
            if (grid.TileCount == 0 || drowning.MaxFloodPerTile <= 0)
                continue;

            var maxWeight = _shipGrid.GetMaxWeight(uid, grid);

            if (grid.TotalWeight > maxWeight * 3f)
            {
                var configuredRate = _cfg.GetCVar(ShipsCCVars.OverloadDrownRate);
                var overloadRate = float.IsFinite(configuredRate) ? MathF.Max(0f, configuredRate) : 0f;
                drowning.DrownLevel += overloadRate * elapsedSeconds;
            }

            drowning.DrownMaxLevel = (float) grid.TileCount * drowning.MaxFloodPerTile;
            var floodPerStage = float.IsFinite(drowning.FloodPerDamageStage)
                ? MathF.Max(0f, drowning.FloodPerDamageStage)
                : 0f;
            drowning.DrownLevel += grid.FloodContribution * floodPerStage;

            if (drowning.DrownLevel < drowning.DrownMaxLevel * 0.5f)
                drowning.DrownLevel -= Math.Max(0, drowning.PassiveDrainPerTick);
            else
                drowning.DrownLevel += Math.Max(0, drowning.PassiveRisePerTick);

            drowning.DrownLevel = Math.Max(0, drowning.DrownLevel);

            if (drowning.DrownLevel >= drowning.DrownMaxLevel)
            {
                SinkShip(uid);
                continue;
            }

            if (drowning.DrownLevel != previousDrownLevel || Math.Abs(drowning.DrownMaxLevel - previousDrownMaxLevel) > float.Epsilon)
                Dirty(uid, drowning);
        }
    }

    private void SinkShip(EntityUid ship)
    {
        EntityManager.QueueDeleteEntity(ship);
    }

    private void OnShipTerminating(EntityUid uid, ShipDrowningComponent component, ref EntityTerminatingEvent args)
    {
        RescueGridChildrenToMap(uid);
    }

    private void RescueGridChildrenToMap(EntityUid uid)
    {
        var shipXform = Transform(uid);
        if (!_map.TryGetMap(shipXform.MapID, out var mapUid) || TerminatingOrDeleted(mapUid.Value))
            return;

        _gridChildren.Clear();
        var entityEnumerator = EntityQueryEnumerator<TransformComponent>();
        while (entityEnumerator.MoveNext(out var child, out var childXform))
        {
            if (child == uid || TerminatingOrDeleted(child))
                continue;

            if (childXform.ParentUid != uid)
                continue;

            _gridChildren.Add(child);
        }

        foreach (var child in _gridChildren)
        {
            var childXform = Transform(child);
            var mapCoordinates = _transform.GetMapCoordinates(child, childXform);
            var worldRotation = _transform.GetWorldRotation(child);
            var traversal = childXform.GridTraversal;
            childXform.GridTraversal = false;
            _transform.SetCoordinates(child, childXform, new EntityCoordinates(mapUid.Value, mapCoordinates.Position), rotation: worldRotation);
            childXform.GridTraversal = traversal;
        }

        UpdateMoverRelativeEntities(uid, mapUid.Value);
    }

    private void UpdateMoverRelativeEntities(EntityUid gridUid, EntityUid mapUid)
    {
        var oldRelativeRotation = Angle.Zero;
        if (TryComp<TransformComponent>(gridUid, out var oldRelativeXform))
            oldRelativeRotation = _transform.GetWorldRotation(oldRelativeXform);

        var newRelativeRotation = Angle.Zero;
        if (TryComp<TransformComponent>(mapUid, out var newRelativeXform))
            newRelativeRotation = _transform.GetWorldRotation(newRelativeXform);

        var diff = newRelativeRotation - oldRelativeRotation;
        var moverEnumerator = EntityQueryEnumerator<InputMoverComponent>();
        while (moverEnumerator.MoveNext(out var moverUid, out var mover))
        {
            if (mover.RelativeEntity != gridUid)
                continue;

            mover.TargetRelativeRotation -= diff;
            mover.RelativeRotation -= diff;
            mover.RelativeEntity = mapUid;
            mover.LerpTarget = TimeSpan.Zero;
            Dirty(moverUid, mover);
        }
    }
}
