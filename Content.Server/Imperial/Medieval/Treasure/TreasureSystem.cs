using System.Linq;
using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Content.Server.Imperial.Medieval.WormDigging;
using Content.Server.Storage.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.Imperial.Medieval.Ships.Islands;
using Content.Shared.Imperial.Medieval.Treasure;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server.Imperial.Medieval.Treasure;

public sealed class TreasureSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StorageSystem _storage = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TreasureBoardComponent, MapInitEvent>(OnBoardMapInit);
        SubscribeLocalEvent<TreasureMapComponent, MapInitEvent>(OnTreasureMapInit);
        SubscribeLocalEvent<TreasureMapComponent, UseInHandEvent>(OnTreasureMapUsed);
        SubscribeLocalEvent<TreasureMarkerComponent, EntityTerminatingEvent>(OnMarkerTerminating);
        SubscribeLocalEvent<TreasureDiggerComponent, AfterInteractEvent>(OnDiggerAfterInteract,
            before: [typeof(WormDiggingSystem)]);
        SubscribeLocalEvent<TreasureDiggerComponent, TreasureDigDoAfterEvent>(OnTreasureDigComplete);
    }

    private void OnBoardMapInit(Entity<TreasureBoardComponent> ent, ref MapInitEvent args)
    {
        _ = RunBoardTimer(ent.Owner);
    }

    private async Task RunBoardTimer(EntityUid uid)
    {
        var delay = TimeSpan.FromSeconds(10);
        while (true)
        {
            await Robust.Shared.Timing.Timer.Delay(delay);

            if (TerminatingOrDeleted(uid) ||
                !TryComp<TreasureBoardComponent>(uid, out var board) ||
                !TryComp<StorageComponent>(uid, out var storage))
            {
                return;
            }

            var map = Spawn(board.MapPrototype);
            if (!TryComp<TreasureMapComponent>(map, out var mapComponent) || mapComponent.Marker == null)
            {
                QueueDel(map);
                delay = TimeSpan.FromSeconds(30);
                continue;
            }

            if (!_storage.CanInsert(uid, map, out _, storageComp: storage) ||
                !_storage.Insert(uid, map, out _, storageComp: storage))
            {
                QueueDel(map);
                delay = GetNextSpawnDelay(board);
                continue;
            }

            delay = GetNextSpawnDelay(board);
        }
    }

    private TimeSpan GetNextSpawnDelay(TreasureBoardComponent component)
    {
        return _random.Next(
            component.MinimumSpawnDelay,
            component.MaximumSpawnDelay);
    }

    private void OnTreasureMapInit(Entity<TreasureMapComponent> ent, ref MapInitEvent args)
    {
        var grids = new List<Entity<MapGridComponent>>();
        var gridQuery = EntityQueryEnumerator<IslandComponent, MapGridComponent>();
        while (gridQuery.MoveNext(out var gridUid, out var island, out var grid))
        {
            if (island.GenerationGroup == IslandGenerationGroup.High &&
                _map.GetAllTiles(gridUid, grid).Any())
            {
                grids.Add((gridUid, grid));
            }
        }

        if (grids.Count == 0)
            return;

        var selectedGrid = _random.Pick(grids);
        var tiles = _map.GetAllTiles(selectedGrid.Owner, selectedGrid.Comp).ToList();
        if (tiles.Count == 0)
            return;

        var tile = _random.Pick(tiles);
        var coordinates = _map.GridTileToLocal(selectedGrid.Owner, selectedGrid.Comp, tile.GridIndices);
        var marker = Spawn("MedievalTreasureMarker", coordinates);
        var markerComponent = Comp<TreasureMarkerComponent>(marker);

        markerComponent.Map = ent.Owner;
        var worldPosition = _transform.GetWorldPosition(marker);
        markerComponent.WorldPosition = new Vector2i(
            (int) MathF.Round(worldPosition.X),
            (int) MathF.Round(worldPosition.Y));

        ent.Comp.Marker = marker;
    }

    private void OnTreasureMapUsed(Entity<TreasureMapComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (ent.Comp.Completed ||
            ent.Comp.Marker is not { } marker ||
            TerminatingOrDeleted(marker) ||
            !TryComp<TreasureMarkerComponent>(marker, out var markerComponent))
        {
            _popup.PopupEntity(Loc.GetString("treasure-map-empty"), args.User, args.User, PopupType.Small);
            return;
        }

        var position = markerComponent.WorldPosition;
        var message = Loc.GetString("treasure-map-coordinates", ("x", position.X), ("y", position.Y));
        _popup.PopupEntity(message, args.User, args.User, PopupType.Small);

        if (TryComp<ActorComponent>(args.User, out var actor))
            _chat.DispatchServerMessage(actor.PlayerSession, message);
    }

    private void OnMarkerTerminating(Entity<TreasureMarkerComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Map is not { } map ||
            TerminatingOrDeleted(map) ||
            !TryComp<TreasureMapComponent>(map, out var mapComponent))
        {
            return;
        }

        mapComponent.Marker = null;
        mapComponent.Completed = true;
    }

    private void OnDiggerAfterInteract(Entity<TreasureDiggerComponent> digger, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !TryGetNonEmptyTile(args.ClickLocation, out _))
            return;

        var clickMapCoordinates = _transform.ToMapCoordinates(args.ClickLocation);
        EntityUid? closestMarker = null;
        var closestDistanceSquared = float.MaxValue;

        var markerQuery = EntityQueryEnumerator<TreasureMarkerComponent, TransformComponent>();
        while (markerQuery.MoveNext(out var markerUid, out var marker, out var markerTransform))
        {
            var markerCoordinates = _transform.GetMapCoordinates(markerUid, markerTransform);
            if (markerCoordinates.MapId != clickMapCoordinates.MapId)
                continue;

            var distanceSquared = (markerCoordinates.Position - clickMapCoordinates.Position).LengthSquared();
            if (distanceSquared > marker.DigRadius * marker.DigRadius || distanceSquared >= closestDistanceSquared)
                continue;

            closestMarker = markerUid;
            closestDistanceSquared = distanceSquared;
        }

        if (closestMarker is not { } selectedMarker)
            return;

        var diggingEvent = new TreasureDigDoAfterEvent(
            GetNetCoordinates(args.ClickLocation),
            GetNetEntity(selectedMarker));
        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            args.User,
            digger.Comp.DiggingDuration,
            diggingEvent,
            digger.Owner,
            used: digger.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BreakOnDropItem = true,
            BreakOnHandChange = true,
            DuplicateCondition = DuplicateConditions.SameTool | DuplicateConditions.SameEvent,
            BlockDuplicate = true,
            CancelDuplicate = false,
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs))
            return;

        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("treasure-dig-found"), args.User, args.User, PopupType.Small);
    }

    private void OnTreasureDigComplete(Entity<TreasureDiggerComponent> digger, ref TreasureDigDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (!TryGetEntity(args.Marker, out var markerUid) ||
            !TryComp<TreasureMarkerComponent>(markerUid.Value, out var markerComponent))
        {
            return;
        }

        if (args.Cancelled)
            return;

        var coordinates = GetCoordinates(args.ClickLocation);
        if (!coordinates.IsValid(EntityManager) || !TryGetNonEmptyTile(coordinates, out _))
            return;

        var clickMapCoordinates = _transform.ToMapCoordinates(coordinates);
        var markerMapCoordinates = _transform.GetMapCoordinates(markerUid.Value);
        if (clickMapCoordinates.MapId != markerMapCoordinates.MapId ||
            (clickMapCoordinates.Position - markerMapCoordinates.Position).LengthSquared() >
            markerComponent.DigRadius * markerComponent.DigRadius)
        {
            return;
        }

        Spawn("MedievalTreasureChest", coordinates);
        QueueDel(markerUid.Value);
    }

    private bool TryGetNonEmptyTile(EntityCoordinates coordinates, out TileRef tile)
    {
        var tileMaybe = _turf.GetTileRef(coordinates);
        if (tileMaybe is not { } found || found.Tile.IsEmpty)
        {
            tile = default;
            return false;
        }

        tile = found;
        return true;
    }

}
