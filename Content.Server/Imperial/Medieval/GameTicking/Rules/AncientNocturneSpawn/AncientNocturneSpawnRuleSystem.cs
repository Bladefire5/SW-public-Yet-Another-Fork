using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Rules;
using Content.Server.Ghost.Roles.Events;
using Content.Server.Humanoid;
using Content.Server.Preferences.Managers;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Nocturn.Components;
using Content.Shared.Preferences;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Imperial.Medieval.GameTicking.Rules;

public sealed class AncientNocturneSpawnRuleSystem : GameRuleSystem<AncientNocturneSpawnRuleComponent>
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _humanoidAppearance = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly IServerPreferencesManager _preferences = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private readonly ConcurrentQueue<InquisitionSpawnRequest> _completedInquisitionTimers = new();
    private uint _roundGeneration;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AncientNocturneComponent, GhostRoleSpawnerUsedEvent>(OnAncientNocturneSpawned);
        SubscribeLocalEvent<HellfireInquisitionMemberComponent, GhostRoleSpawnerUsedEvent>(OnInquisitorSpawned);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        while (_completedInquisitionTimers.TryDequeue(out var request))
        {
            if (request.RoundGeneration != _roundGeneration)
                continue;

            SpawnInquisition(request);
        }
    }

    protected override void Started(
        EntityUid uid,
        AncientNocturneSpawnRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        var markers = EntityManager.AllEntities<AncientNocturneSpawnMarkerComponent>()
            .Where(marker => !marker.Comp.Used)
            .ToList();
        _random.Shuffle(markers);

        var spawnCount = Math.Min(Math.Max(component.SpawnCount, 0), markers.Count);
        for (var i = 0; i < spawnCount; i++)
        {
            var marker = markers[i];
            Spawn(component.SpawnerPrototype, Transform(marker.Owner).Coordinates);
            marker.Comp.Used = true;
        }

        if (spawnCount == 0)
        {
            Log.Error("Ancient nocturne spawn rule started without available spawn markers");
        }
        else
        {
            _chat.DispatchGlobalAnnouncement(
                Loc.GetString("medieval-ancient-nocturne-event-announcement", ("count", spawnCount)),
                playSound: true,
                colorOverride: Color.MediumPurple,
                sender: Loc.GetString("medieval-ancient-nocturne-event-sender"));

            StartInquisitionTimer(component);
        }

        GameTicker.EndGameRule(uid, gameRule);
    }

    private void OnAncientNocturneSpawned(
        EntityUid uid,
        AncientNocturneComponent component,
        GhostRoleSpawnerUsedEvent args)
    {
        if (!_preferences.TryGetCachedPreferences(args.Player.UserId, out var preferences))
        {
            Log.Error($"Ancient nocturne ghost role taken without cached preferences for {args.Player.UserId}");
            return;
        }

        var profiles = preferences.Characters.Values
            .OfType<HumanoidCharacterProfile>()
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .ToList();

        if (profiles.Count == 0)
        {
            Log.Error($"Ancient nocturne ghost role taken without character profiles for {args.Player.UserId}");
            return;
        }

        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
        {
            Log.Error($"Ancient nocturne ghost role spawned without humanoid appearance for {args.Player.UserId}");
            return;
        }

        var profile = _random.Pick(profiles);
        _metaData.SetEntityName(uid, profile.Name);
        _humanoidAppearance.SetSex(uid, profile.Sex, false, humanoid);
        _humanoidAppearance.SetGender((uid, humanoid), profile.Gender);
        humanoid.MarkingSet.RemoveCategory(MarkingCategories.Hair);
        _humanoidAppearance.AddMarking(
            uid,
            profile.Appearance.HairStyleId,
            profile.Appearance.HairColor,
            humanoid: humanoid);
    }

    private void OnInquisitorSpawned(
        EntityUid uid,
        HellfireInquisitionMemberComponent component,
        GhostRoleSpawnerUsedEvent args)
    {
        if (!_preferences.TryGetCachedPreferences(args.Player.UserId, out var preferences))
        {
            Log.Error($"Hellfire inquisitor ghost role taken without cached preferences for {args.Player.UserId}");
            return;
        }

        var profiles = preferences.Characters.Values
            .OfType<HumanoidCharacterProfile>()
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .ToList();

        if (profiles.Count == 0)
        {
            Log.Error($"Hellfire inquisitor ghost role taken without character profiles for {args.Player.UserId}");
            return;
        }

        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
        {
            Log.Error($"Hellfire inquisitor ghost role spawned without humanoid appearance for {args.Player.UserId}");
            return;
        }

        var profile = _random.Pick(profiles).WithSpecies("Human");
        _metaData.SetEntityName(uid, profile.Name);
        _humanoidAppearance.LoadProfile(uid, profile, humanoid);
    }

    private void StartInquisitionTimer(AncientNocturneSpawnRuleComponent component)
    {
        var request = new InquisitionSpawnRequest(
            _roundGeneration,
            component.InquisitionLeaderSpawnerPrototype,
            component.InquisitionKnightSpawnerPrototype,
            component.InquisitionChaplainSpawnerPrototype,
            Math.Max(component.InquisitionKnightCount, 0),
            Math.Max(component.InquisitionSpawnOffset, 0f));

        var delay = component.InquisitionDelay < TimeSpan.Zero
            ? TimeSpan.Zero
            : component.InquisitionDelay;
        _ = RunInquisitionTimer(request, delay);
    }

    private async Task RunInquisitionTimer(InquisitionSpawnRequest request, TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        _completedInquisitionTimers.Enqueue(request);
    }

    private void SpawnInquisition(InquisitionSpawnRequest request)
    {
        var markers = EntityManager.AllEntities<HellfireInquisitionSpawnMarkerComponent>()
            .Where(marker => !marker.Comp.Used)
            .ToList();

        if (markers.Count == 0)
        {
            Log.Error("Hellfire Inquisition timer completed without available spawn markers");
            return;
        }

        var marker = _random.Pick(markers);
        marker.Comp.Used = true;

        var spawners = new List<EntProtoId>
        {
            request.LeaderSpawnerPrototype,
            request.ChaplainSpawnerPrototype
        };

        for (var i = 0; i < request.KnightCount; i++)
            spawners.Add(request.KnightSpawnerPrototype);

        var coordinates = Transform(marker.Owner).Coordinates;
        var angleOffset = _random.NextFloat(0f, MathF.Tau);
        for (var i = 0; i < spawners.Count; i++)
        {
            var angle = angleOffset + MathF.Tau * i / spawners.Count;
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * request.SpawnOffset;
            Spawn(spawners[i], coordinates.Offset(offset));
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _roundGeneration++;
        _completedInquisitionTimers.Clear();
    }

    private readonly record struct InquisitionSpawnRequest(
        uint RoundGeneration,
        EntProtoId LeaderSpawnerPrototype,
        EntProtoId KnightSpawnerPrototype,
        EntProtoId ChaplainSpawnerPrototype,
        int KnightCount,
        float SpawnOffset);
}
