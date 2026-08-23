using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Rules;
using Content.Server.Ghost.Roles.Events;
using Content.Server.Humanoid;
using Content.Server.Preferences.Managers;
using Content.Shared.GameTicking.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Nocturn.Components;
using Content.Shared.Preferences;
using Robust.Shared.Random;

namespace Content.Server.Imperial.Medieval.GameTicking.Rules;

public sealed class AncientNocturneSpawnRuleSystem : GameRuleSystem<AncientNocturneSpawnRuleComponent>
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _humanoidAppearance = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly IServerPreferencesManager _preferences = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AncientNocturneComponent, GhostRoleSpawnerUsedEvent>(OnAncientNocturneSpawned);
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
}
