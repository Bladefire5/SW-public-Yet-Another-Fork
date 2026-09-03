using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Imperial.Medieval.Ships;
using Content.Shared.Imperial.Medieval.Administration.Ships;
using Content.Shared.Imperial.Medieval.Ships.Sea;
using Content.Shared.Imperial.Medieval.Ships.ShipDrowning;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Imperial.Medieval.Ships.Wave.Spawn;

public sealed class SpawnWindWaveSystem : EntitySystem
{
    private const int MaxWavesPerShip = 64;

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly WaveSystem _wave = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    private TimeSpan _nextCheckTime;
    private readonly HashSet<MapId> _activeSeaMaps = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        if (curTime <= _nextCheckTime)
            return;

        var configuredDelay = _cfg.GetCVar(ShipsCCVars.WaveDelay);
        var delay = float.IsFinite(configuredDelay) ? MathF.Max(0.1f, configuredDelay) : 1f;
        _nextCheckTime = curTime + TimeSpan.FromSeconds(delay);

        var configuredStormLevel = _cfg.GetCVar(ShipsCCVars.StormLevel);
        var stormLevel = float.IsFinite(configuredStormLevel)
            ? Math.Clamp(configuredStormLevel, 0f, MaxWavesPerShip)
            : 0f;
        var maxWaves = Math.Clamp((int) MathF.Ceiling(stormLevel), 0, MaxWavesPerShip);
        var configuredWaveForce = _cfg.GetCVar(ShipsCCVars.WaveForce);
        var waveForce = float.IsFinite(configuredWaveForce) ? MathF.Max(0f, configuredWaveForce) : 0f;

        _activeSeaMaps.Clear();
        foreach (var seaComponent in EntityManager.EntityQuery<SeaComponent>())
        {
            if (seaComponent.Disabled)
                continue;

            _activeSeaMaps.Add(_transform.GetMapId(seaComponent.Owner));
        }

        var ships = EntityQueryEnumerator<ShipGridComponent, ShipDrowningComponent, MapGridComponent>();
        while (ships.MoveNext(out var ship, out var shipGrid, out _, out var grid))
        {
            var seaMapId = _transform.GetMapId(ship);
            if (!_activeSeaMaps.Contains(seaMapId) ||
                shipGrid.WavesDisabledAt is { } disabledAt && disabledAt <= curTime)
            {
                continue;
            }

            var waveCount = _random.Next(0, maxWaves + 1);
            var shipCenter = _transform.ToMapCoordinates(new EntityCoordinates(ship, grid.LocalAABB.Center));
            var shipRadius = grid.LocalAABB.Size.Length() * 0.5f;

            for (var i = 0; i < waveCount; i++)
            {
                var waveOffset = GenerateWave();
                var offsetLength = waveOffset.Length();
                if (offsetLength <= 0f)
                    continue;

                var windDirection = waveOffset / offsetLength;
                var spawnDirection = -windDirection;
                var configuredMinDistance = _cfg.GetCVar(ShipsCCVars.WaveMinSpawnDistance);
                var minDistance = float.IsFinite(configuredMinDistance)
                    ? MathF.Max(0f, configuredMinDistance)
                    : 0f;
                var spawnDistance = shipRadius + minDistance + offsetLength;
                if (!TryFindValidSpawnPosition(seaMapId, shipCenter.Position, spawnDirection, spawnDistance, out var waveCoords))
                    continue;

                var wavePosition = waveCoords.Position;
                var velocityDirection = shipCenter.Position - wavePosition;
                var velocityLengthSquared = velocityDirection.LengthSquared();
                if (!float.IsFinite(velocityLengthSquared) || velocityLengthSquared <= 0.0001f)
                    continue;

                var velocity = velocityDirection / MathF.Sqrt(velocityLengthSquared) * waveForce;
                _wave.SpawnWave(waveCoords, velocity);
            }
        }
    }

    private bool TryFindValidSpawnPosition(MapId mapId, Vector2 shipCenter, Vector2 direction, float initialDistance, out MapCoordinates coords)
    {
        var distance = initialDistance;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var wavePosition = shipCenter + direction * distance;
            if (!_mapManager.TryFindGridAt(mapId, wavePosition, out _, out _))
            {
                coords = new MapCoordinates(wavePosition, mapId);
                return true;
            }

            distance += 1f;
        }

        coords = default;
        return false;
    }

    private Vector2 GenerateWave()
    {
        var configuredRadius = _cfg.GetCVar(ShipsCCVars.WaveSpawnRange);
        var radius = float.IsFinite(configuredRadius) ? MathF.Max(0f, configuredRadius) : 0f;
        var configuredRotation = _cfg.GetCVar(ShipsCCVars.WindRotation);
        var targetAngle = Angle.FromDegrees(float.IsFinite(configuredRotation) ? configuredRotation : 0f);
        var configuredAngle = _cfg.GetCVar(ShipsCCVars.WaveSpawnAngle);
        var configuredStorm = _cfg.GetCVar(ShipsCCVars.StormLevel);
        var angle = float.IsFinite(configuredAngle) ? MathF.Abs(configuredAngle) : 0f;
        var storm = float.IsFinite(configuredStorm)
            ? Math.Clamp(configuredStorm, 0f, MaxWavesPerShip)
            : 0f;
        var halfAngle = MathF.Min(360f, angle * storm);

        var rho = radius * MathF.Sqrt(_random.NextFloat());
        var angleOffset = Angle.FromDegrees(_random.NextFloat(-halfAngle, halfAngle));
        var direction = (targetAngle + angleOffset).ToWorldVec();
        return direction * rho;
    }
}
