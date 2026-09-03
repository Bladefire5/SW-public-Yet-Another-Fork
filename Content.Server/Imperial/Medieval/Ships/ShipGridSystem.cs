using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Content.Server.Imperial.Medieval.Ships.PlayerDrowning;
using Content.Shared._RD.Weight.Components;
using Content.Shared._RD.Weight.Events;
using Content.Shared._RD.Weight.Systems;
using Content.Shared.Imperial.Medieval.Administration.Ships;
using Content.Shared.Imperial.Medieval.Ships.Anchor;
using Content.Shared.Imperial.Medieval.Ships.Helm;
using Content.Shared.Imperial.Medieval.Ships.Hull;
using Content.Shared.Imperial.Medieval.Ships.Sail;
using Content.Shared.Imperial.Medieval.Ships.ShipDrowning;
using Content.Shared.Maps;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;

namespace Content.Server.Imperial.Medieval.Ships;

public sealed class ShipGridSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly RDWeightSystem _weight = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly MedievalAnchorSystem _anchor = default!;
    [Dependency] private readonly SharedShipHullSystem _shipHull = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, WeightEntry> _weights = new();
    private readonly Dictionary<EntityUid, EntityUid> _helmGrids = new();
    private readonly Dictionary<EntityUid, EntityUid> _sailGrids = new();
    private readonly Dictionary<EntityUid, float> _sailEfficiencies = new();
    private readonly Dictionary<EntityUid, SteeringOarEntry> _steeringOars = new();
    private readonly Dictionary<EntityUid, EntityUid> _anchorGrids = new();
    private readonly Dictionary<EntityUid, TimeSpan> _anchorWaveProtection = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<ShipGridComponent, ComponentStartup>(OnShipGridStartup);
        SubscribeLocalEvent<ShipGridComponent, TileChangedEvent>(OnGridTileChanged);
        SubscribeLocalEvent<ShipDrowningComponent, MapInitEvent>(OnShipMapInit);

        SubscribeLocalEvent<RDWeightComponent, MapInitEvent>(OnWeightMapInit);
        SubscribeLocalEvent<RDWeightComponent, ComponentShutdown>(OnWeightShutdown);
        SubscribeLocalEvent<RDWeightComponent, GridUidChangedEvent>(OnWeightGridChanged);
        SubscribeLocalEvent<RDWeightComponent, RDWeightRefreshEvent>(OnWeightRefresh);

        SubscribeLocalEvent<HelmComponent, MapInitEvent>(OnHelmMapInit);
        SubscribeLocalEvent<HelmComponent, ComponentShutdown>(OnHelmShutdown);
        SubscribeLocalEvent<HelmComponent, GridUidChangedEvent>(OnHelmGridChanged);

        SubscribeLocalEvent<SailComponent, MapInitEvent>(OnSailMapInit);
        SubscribeLocalEvent<SailComponent, ComponentShutdown>(OnSailShutdown);
        SubscribeLocalEvent<SailComponent, GridUidChangedEvent>(OnSailGridChanged);

        SubscribeLocalEvent<SteeringOarComponent, ComponentStartup>(OnSteeringOarStartup);
        SubscribeLocalEvent<SteeringOarComponent, ComponentShutdown>(OnSteeringOarShutdown);
        SubscribeLocalEvent<SteeringOarComponent, GridUidChangedEvent>(OnSteeringOarGridChanged);

        SubscribeLocalEvent<MedievalAnchorComponent, ComponentShutdown>(OnAnchorShutdown);
        SubscribeLocalEvent<MedievalAnchorComponent, GridUidChangedEvent>(OnAnchorGridChanged);
    }

    public ShipGridComponent EnsureGrid(EntityUid gridUid)
    {
        return EnsureComp<ShipGridComponent>(gridUid);
    }

    public bool TryGetGrid(EntityUid gridUid, [NotNullWhen(true)] out ShipGridComponent? component)
    {
        return TryComp(gridUid, out component);
    }

    public bool TryGetHelmGrid(
        EntityUid helmUid,
        out EntityUid gridUid,
        [NotNullWhen(true)] out ShipGridComponent? component)
    {
        if (_helmGrids.TryGetValue(helmUid, out gridUid) && TryComp(gridUid, out component))
            return true;

        gridUid = EntityUid.Invalid;
        component = null;
        return false;
    }

    public float GetTotalWeight(EntityUid gridUid)
    {
        return CompOrNull<ShipGridComponent>(gridUid)?.TotalWeight ?? 0f;
    }

    public float GetMaxWeight(EntityUid gridUid, ShipGridComponent component)
    {
        var perTile = _cfg.GetCVar(ShipsCCVars.OverloadCeilPerTile);
        if (TryComp<ShipWeightComponent>(gridUid, out var shipWeight))
            perTile = shipWeight.OverloadCeilPerTile;

        return GetMaxWeight(component, perTile);
    }

    public static float GetMaxWeight(ShipGridComponent component, float overloadCeilPerTile)
    {
        if (!float.IsFinite(overloadCeilPerTile) || overloadCeilPerTile <= 0f)
            return 0f;

        return component.TileCount * overloadCeilPerTile;
    }

    public float GetSailEfficiency(EntityUid sailUid)
    {
        return _sailEfficiencies.GetValueOrDefault(sailUid);
    }

    public void SetSailEfficiency(EntityUid sailUid, float efficiency)
    {
        if (!float.IsFinite(efficiency))
            efficiency = 0f;

        var previous = GetSailEfficiency(sailUid);
        if (MathF.Abs(previous - efficiency) < 0.001f)
            return;

        _sailEfficiencies[sailUid] = efficiency;
        if (_sailGrids.TryGetValue(sailUid, out var gridUid) &&
            TryComp<ShipGridComponent>(gridUid, out var grid))
        {
            grid.SailsEfficiency += efficiency - previous;
            ClampNearZero(ref grid.SailsEfficiency);
        }
    }

    public void NotifyAnchorChanged(EntityUid uid, MedievalAnchorComponent component)
    {
        _metaData.AddFlag(uid, MetaDataFlags.ExtraTransformEvents);
        RegisterAnchor(uid, GetGridUid(uid, Transform(uid)));

        if (_anchorGrids.TryGetValue(uid, out var gridUid) &&
            TryComp<ShipGridComponent>(gridUid, out var grid))
        {
            RecalculateAnchors(gridUid, grid);
        }
    }

    public void SetAnchorWaveProtection(EntityUid anchorUid, TimeSpan? disabledAt)
    {
        if (disabledAt == null)
            _anchorWaveProtection.Remove(anchorUid);
        else
            _anchorWaveProtection[anchorUid] = disabledAt.Value;
    }

    public TimeSpan? GetAnchorWaveProtection(EntityUid anchorUid)
    {
        return _anchorWaveProtection.TryGetValue(anchorUid, out var disabledAt) ? disabledAt : null;
    }

    private void OnShipGridStartup(Entity<ShipGridComponent> entity, ref ComponentStartup args)
    {
        if (TryComp<MapGridComponent>(entity, out var mapGrid))
        {
            var tiles = _map.GetAllTilesEnumerator(entity, mapGrid);
            while (tiles.MoveNext(out var tile))
            {
                entity.Comp.TileCount++;
                entity.Comp.FloodContribution += _shipHull.GetFloodContribution(tile.Value.Tile.TypeId);
            }
        }

        var weight = 0f;
        foreach (var entry in _weights.Values)
        {
            if (entry.GridUid == entity.Owner)
                weight += entry.Contribution;
        }

        entity.Comp.TotalWeight = weight;
    }

    private void OnShipMapInit(Entity<ShipDrowningComponent> entity, ref MapInitEvent args)
    {
        if (HasComp<MapGridComponent>(entity))
            EnsureGrid(entity);
    }

    private void OnGridTileChanged(Entity<ShipGridComponent> entity, ref TileChangedEvent args)
    {
        foreach (var change in args.Changes)
        {
            if (change.EmptyChanged)
                entity.Comp.TileCount += change.NewTile.IsEmpty ? -1 : 1;

            entity.Comp.FloodContribution +=
                _shipHull.GetFloodContribution(change.NewTile.TypeId) -
                _shipHull.GetFloodContribution(change.OldTile.TypeId);
        }

        entity.Comp.TileCount = Math.Max(0, entity.Comp.TileCount);
        entity.Comp.FloodContribution = Math.Max(0, entity.Comp.FloodContribution);
    }

    private void OnWeightMapInit(Entity<RDWeightComponent> entity, ref MapInitEvent args)
    {
        _metaData.AddFlag(entity, MetaDataFlags.ExtraTransformEvents);
        UpdateWeight(entity, GetGridUid(entity, Transform(entity)), GetDirectWeight(entity));
    }

    private void OnWeightShutdown(Entity<RDWeightComponent> entity, ref ComponentShutdown args)
    {
        RemoveWeight(entity);
    }

    private void OnWeightGridChanged(Entity<RDWeightComponent> entity, ref GridUidChangedEvent args)
    {
        UpdateWeight(entity, args.NewGrid, GetDirectWeight(entity));
    }

    private void OnWeightRefresh(Entity<RDWeightComponent> entity, ref RDWeightRefreshEvent args)
    {
        var directWeight = args.Total;
        var children = Transform(entity).ChildEnumerator;
        while (children.MoveNext(out var childUid))
        {
            directWeight -= _weight.GetTotal(childUid);
        }

        var gridUid = _weights.TryGetValue(entity, out var tracked) ? tracked.GridUid : GetGridUid(entity, Transform(entity));
        UpdateWeight(entity, gridUid, directWeight);
    }

    private float GetDirectWeight(EntityUid uid)
    {
        var directWeight = _weight.GetTotal(uid);
        var children = Transform(uid).ChildEnumerator;
        while (children.MoveNext(out var childUid))
        {
            directWeight -= _weight.GetTotal(childUid);
        }

        return directWeight;
    }

    private void UpdateWeight(EntityUid uid, EntityUid? gridUid, float contribution)
    {
        contribution = float.IsFinite(contribution) ? MathF.Max(0f, contribution) : 0f;
        gridUid = gridUid == uid ? null : ValidateGrid(gridUid);
        if (_weights.TryGetValue(uid, out var previous))
        {
            if (previous.GridUid == gridUid)
            {
                if (gridUid != null)
                    AdjustGridWeight(gridUid.Value, contribution - previous.Contribution);
            }
            else
            {
                if (previous.GridUid != null)
                    AdjustGridWeight(previous.GridUid.Value, -previous.Contribution);

                if (gridUid != null)
                    AdjustGridWeight(gridUid.Value, contribution);
            }
        }
        else if (gridUid != null)
        {
            AdjustGridWeight(gridUid.Value, contribution);
        }

        _weights[uid] = new WeightEntry(gridUid, contribution);
    }

    private void RemoveWeight(EntityUid uid)
    {
        if (!_weights.Remove(uid, out var entry) || entry.GridUid == null)
            return;

        AdjustGridWeight(entry.GridUid.Value, -entry.Contribution);
    }

    private void AdjustGridWeight(EntityUid gridUid, float delta)
    {
        if (!float.IsFinite(delta) || MathF.Abs(delta) < float.Epsilon ||
            TerminatingOrDeleted(gridUid) ||
            !TryComp<ShipGridComponent>(gridUid, out var grid))
        {
            return;
        }

        var currentWeight = float.IsFinite(grid.TotalWeight) ? grid.TotalWeight : 0f;
        grid.TotalWeight = MathF.Max(0f, currentWeight + delta);
        ClampNearZero(ref grid.TotalWeight);
    }

    private void OnHelmMapInit(Entity<HelmComponent> entity, ref MapInitEvent args)
    {
        _metaData.AddFlag(entity, MetaDataFlags.ExtraTransformEvents);
        RegisterHelm(entity, GetGridUid(entity, Transform(entity)));
    }

    private void OnHelmShutdown(Entity<HelmComponent> entity, ref ComponentShutdown args)
    {
        UnregisterHelm(entity);
    }

    private void OnHelmGridChanged(Entity<HelmComponent> entity, ref GridUidChangedEvent args)
    {
        RegisterHelm(entity, args.NewGrid);
    }

    private void RegisterHelm(EntityUid uid, EntityUid? gridUid)
    {
        gridUid = ValidateGrid(gridUid);
        if (_helmGrids.GetValueOrDefault(uid) == gridUid)
            return;

        UnregisterHelm(uid);
        if (gridUid == null)
            return;

        var grid = EnsureGrid(gridUid.Value);
        if (grid.Helm is { } existing && existing != uid && !TerminatingOrDeleted(existing))
        {
            Log.Error($"Ship grid {ToPrettyString(gridUid.Value)} contains more than one helm. " +
                      $"Keeping {ToPrettyString(existing)} and deleting {ToPrettyString(uid)}.");
            QueueDel(uid);
            return;
        }

        grid.Helm = uid;
        _helmGrids[uid] = gridUid.Value;
    }

    private void UnregisterHelm(EntityUid uid)
    {
        if (!_helmGrids.Remove(uid, out var gridUid))
            return;

        if (TryComp<ShipGridComponent>(gridUid, out var grid) && grid.Helm == uid)
            grid.Helm = null;
    }

    private void OnSailMapInit(Entity<SailComponent> entity, ref MapInitEvent args)
    {
        _metaData.AddFlag(entity, MetaDataFlags.ExtraTransformEvents);
        RegisterSail(entity, GetGridUid(entity, Transform(entity)));
    }

    private void OnSailShutdown(Entity<SailComponent> entity, ref ComponentShutdown args)
    {
        UnregisterSail(entity);
        _sailEfficiencies.Remove(entity);
    }

    private void OnSailGridChanged(Entity<SailComponent> entity, ref GridUidChangedEvent args)
    {
        RegisterSail(entity, args.NewGrid);
    }

    private void RegisterSail(EntityUid uid, EntityUid? gridUid)
    {
        gridUid = ValidateGrid(gridUid);
        if (_sailGrids.GetValueOrDefault(uid) == gridUid)
            return;

        UnregisterSail(uid);
        if (gridUid == null)
            return;

        var grid = EnsureGrid(gridUid.Value);
        if (grid.Sails.Add(uid))
            grid.SailsEfficiency += GetSailEfficiency(uid);

        _sailGrids[uid] = gridUid.Value;
    }

    private void UnregisterSail(EntityUid uid)
    {
        if (!_sailGrids.Remove(uid, out var gridUid) ||
            !TryComp<ShipGridComponent>(gridUid, out var grid) ||
            !grid.Sails.Remove(uid))
        {
            return;
        }

        grid.SailsEfficiency -= GetSailEfficiency(uid);
        ClampNearZero(ref grid.SailsEfficiency);
    }

    private void OnSteeringOarStartup(Entity<SteeringOarComponent> entity, ref ComponentStartup args)
    {
        _metaData.AddFlag(entity, MetaDataFlags.ExtraTransformEvents);
        RegisterSteeringOar(entity, entity.Comp.Power, GetGridUid(entity, Transform(entity)));
    }

    private void OnSteeringOarShutdown(Entity<SteeringOarComponent> entity, ref ComponentShutdown args)
    {
        UnregisterSteeringOar(entity);
    }

    private void OnSteeringOarGridChanged(Entity<SteeringOarComponent> entity, ref GridUidChangedEvent args)
    {
        RegisterSteeringOar(entity, entity.Comp.Power, args.NewGrid);
    }

    private void RegisterSteeringOar(EntityUid uid, float power, EntityUid? gridUid)
    {
        power = float.IsFinite(power) ? MathF.Max(0f, power) : 0f;
        gridUid = ValidateGrid(gridUid);
        if (_steeringOars.TryGetValue(uid, out var existing) && existing.GridUid == gridUid)
            return;

        UnregisterSteeringOar(uid);
        if (gridUid == null)
            return;

        var grid = EnsureGrid(gridUid.Value);
        grid.SteeringPower += power;
        _steeringOars[uid] = new SteeringOarEntry(gridUid.Value, power);
    }

    private void UnregisterSteeringOar(EntityUid uid)
    {
        if (!_steeringOars.Remove(uid, out var entry) ||
            !TryComp<ShipGridComponent>(entry.GridUid, out var grid))
        {
            return;
        }

        grid.SteeringPower -= entry.Power;
        ClampNearZero(ref grid.SteeringPower);
    }

    private void OnAnchorShutdown(Entity<MedievalAnchorComponent> entity, ref ComponentShutdown args)
    {
        UnregisterAnchor(entity);
        _anchorWaveProtection.Remove(entity);
        _anchor.ClearActiveUser(entity);
    }

    private void OnAnchorGridChanged(Entity<MedievalAnchorComponent> entity, ref GridUidChangedEvent args)
    {
        RegisterAnchor(entity, args.NewGrid);
    }

    private void RegisterAnchor(EntityUid uid, EntityUid? gridUid)
    {
        gridUid = ValidateGrid(gridUid);
        if (_anchorGrids.GetValueOrDefault(uid) == gridUid)
            return;

        UnregisterAnchor(uid);
        if (gridUid == null)
            return;

        var grid = EnsureGrid(gridUid.Value);
        grid.Anchors.Add(uid);
        _anchorGrids[uid] = gridUid.Value;
        RecalculateAnchors(gridUid.Value, grid);
    }

    private void UnregisterAnchor(EntityUid uid)
    {
        if (!_anchorGrids.Remove(uid, out var oldGrid) ||
            !TryComp<ShipGridComponent>(oldGrid, out var grid))
        {
            return;
        }

        grid.Anchors.Remove(uid);
        RecalculateAnchors(oldGrid, grid);
    }

    private void RecalculateAnchors(EntityUid gridUid, ShipGridComponent grid)
    {
        var previouslyLowered = grid.HasLoweredAnchor;
        var hasLowered = false;
        TimeSpan? wavesDisabledAt = null;

        foreach (var anchorUid in grid.Anchors)
        {
            if (!_anchorGrids.TryGetValue(anchorUid, out var anchorGrid) ||
                anchorGrid != gridUid ||
                !TryComp<MedievalAnchorComponent>(anchorUid, out var anchor) ||
                !anchor.Lowered)
            {
                continue;
            }

            hasLowered = true;
            if (_anchorWaveProtection.TryGetValue(anchorUid, out var disabledAt) &&
                (wavesDisabledAt == null || disabledAt < wavesDisabledAt.Value))
            {
                wavesDisabledAt = disabledAt;
            }
        }

        grid.HasLoweredAnchor = hasLowered;
        grid.WavesDisabledAt = wavesDisabledAt;
        if (previouslyLowered == hasLowered)
            return;

        var ev = new ShipAnchorStateChangedEvent(hasLowered);
        RaiseLocalEvent(gridUid, ref ev);
    }

    private EntityUid? ValidateGrid(EntityUid? gridUid)
    {
        return gridUid != null && HasComp<MapGridComponent>(gridUid.Value) ? gridUid : null;
    }

    private EntityUid? GetGridUid(EntityUid uid, TransformComponent xform)
    {
        var gridUid = _transform.GetMoverCoordinates(uid, xform).EntityId;
        return HasComp<MapGridComponent>(gridUid) ? gridUid : null;
    }

    private static void ClampNearZero(ref float value)
    {
        if (MathF.Abs(value) < 0.001f)
            value = 0f;
    }

    private readonly record struct WeightEntry(EntityUid? GridUid, float Contribution);

    private readonly record struct SteeringOarEntry(EntityUid GridUid, float Power);
}
