using System.Globalization;
using System.Linq;
using System.Text;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Shared.Imperial.Medieval.Factions;
using Content.Shared.Imperial.Medieval.Factions.Components;
using Content.Shared.Imperial.Medieval.Factions.Prototypes;
using Content.Shared.Database;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Content.Server.Imperial.Medieval.Factions.Components;
using Content.Shared.Paper;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Content.Server.Administration.Managers;

namespace Content.Server.Imperial.Medieval.Factions;

public sealed partial class MedievalFactionsSystem
{
    [Dependency] private readonly IChatManager _chatMan = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly IBanManager _ban = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;

    private static readonly ProtoId<FactionRelationsPrototype> WarRelation = "War";
    private static readonly ProtoId<FactionRelationsPrototype> UnionRelation = "Union";
    private static readonly SoundSpecifier RelationChangedSound = new SoundPathSpecifier("/Audio/Imperial/Medieval/faction_group_assigned.ogg");

    private void InitializeRelations()
    {
        SubscribeLocalEvent<MedievalFactionMemberComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAltVerbs);
        SubscribeLocalEvent<FactionDataContainerComponent, MapInitEvent>(OnFactionDataContainerInit);
        SubscribeLocalEvent<MedievalRelationRequestPaperComponent, GetVerbsEvent<AlternativeVerb>>(OnGetRequestVerbs);
        SubscribeNetworkEvent<OfferFactionRelationsEvent>(OnOfferRelations);
        SubscribeNetworkEvent<AcceptFactionRelationsEvent>(OnAcceptRelations);
        SubscribeNetworkEvent<SetFactionRelationsByRequestEvent>(OnSetRelationsByRequest);
        SubscribeNetworkEvent<CreateFactionRelationsRequestEvent>(OnCreateRequest);
        SubscribeNetworkEvent<DispatchWarEvent>(OnDispatchWar);
    }

    private void OnGetAltVerbs(EntityUid uid, MedievalFactionMemberComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (comp.MenuAccess != FactionMenuAccess.Full)
            return;

        if (!TryComp<MedievalFactionMemberComponent>(args.User, out var friends) || friends.MenuAccess != FactionMenuAccess.Full)
            return;

        if (friends.Faction == comp.Faction)
            return;

        if (Proto.Index(friends.Faction).BlockedRelations.Contains(comp.Faction) ||
            Proto.Index(comp.Faction).BlockedRelations.Contains(friends.Faction))
            return;

        AlternativeVerb verb = new()
        {
            Text = Loc.GetString("faction-relations-verb-change"),
            Act = () =>
            {
                var ev = new OpenOfferFactionRelationsEvent(GetNetEntity(uid), friends.Faction, comp.Faction);
                RaiseNetworkEvent(ev, args.User);
            }
        };

        args.Verbs.Add(verb);
    }

    private void OnGetRequestVerbs(EntityUid uid, MedievalRelationRequestPaperComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!TryComp<MedievalFactionMemberComponent>(args.User, out var friends) || friends.MenuAccess != FactionMenuAccess.Full)
            return;

        if (HasComp<MedievalFactionRelationsRequestComponent>(uid))
            return;

        AlternativeVerb verb = new()
        {
            Text = Loc.GetString("faction-relations-verb-request"),
            Act = () =>
            {
                var ev = new OpenFactionRelationsRequestEvent(GetNetEntity(uid), friends.Faction);
                RaiseNetworkEvent(ev, args.User);
            }
        };

        args.Verbs.Add(verb);
    }

    private void OnOfferRelations(OfferFactionRelationsEvent ev, EntitySessionEventArgs args)
    {
        var senderSession = args.SenderSession;
        var senderUid = senderSession.AttachedEntity;
        if (senderUid == null)
        {
            BanPerson(senderSession, Loc.GetString("medieval-relations-error"));
            return;
        }
        if (!TryComp<MedievalFactionMemberComponent>(senderUid, out var friends) || friends.MenuAccess != FactionMenuAccess.Full || friends.Faction != ev.UserFaction)
        {
            BanPerson(senderSession, Loc.GetString("medieval-relations-error"));
            return;
        }

        var targetUid = GetEntity(ev.Target);
        StorePendingRelationsOffer(
            targetUid,
            ev.UserFaction,
            ev.TargetFaction,
            ev.Relation,
            senderUid.Value);

        var openEv = new OpenAcceptFactionRelationsEvent(ev.UserFaction, ev.TargetFaction, ev.Relation);
        RaiseNetworkEvent(openEv, targetUid);
    }

    private void OnAcceptRelations(AcceptFactionRelationsEvent ev, EntitySessionEventArgs args)
    {
        var senderSession = args.SenderSession;
        var senderUid = senderSession.AttachedEntity;
        if (senderUid == null)
        {
            BanPerson(senderSession, Loc.GetString("medieval-relations-error"));
            return;
        }
        if (!TryComp<MedievalFactionMemberComponent>(senderUid, out var friends) || friends.MenuAccess != FactionMenuAccess.Full || friends.Faction != ev.TargetFaction)
        {
            BanPerson(senderSession, Loc.GetString("medieval-relations-error"));
            return;
        }

        EntityUid? offeredBy = null;
        TryTakePendingRelationsOffer(senderUid.Value, ev.UserFaction, ev.TargetFaction, ev.Relation, out offeredBy);

        SetRelations(ev.UserFaction, ev.TargetFaction, ev.Relation);
        LogRelationsChanged(offeredBy, senderUid.Value, ev.UserFaction, ev.TargetFaction, ev.Relation);
    }

    private void OnSetRelationsByRequest(SetFactionRelationsByRequestEvent ev, EntitySessionEventArgs args)
    {
        var targetUid = GetEntity(ev.Target);
        if (!TryComp<MedievalFactionRelationsRequestComponent>(targetUid, out var request))
            return;

        var senderSession = args.SenderSession;
        var senderUid = senderSession.AttachedEntity;
        if (senderUid == null)
        {
            BanPerson(senderSession, Loc.GetString("medieval-relations-error"));
            return;
        }
        if (!TryComp<MedievalFactionMemberComponent>(senderUid, out var friends) || friends.MenuAccess != FactionMenuAccess.Full || friends.Faction != request.To)
        {
            BanPerson(senderSession, Loc.GetString("medieval-relations-error"));
            return;
        }
        if (ev.Decline)
        {
            RemComp<MedievalFactionRelationsRequestComponent>(targetUid);
            RemComp<MedievalFactionRelationsRequestInitiatorComponent>(targetUid);
            return;
        }

        SetRelations(request.From, request.To, request.Relation);

        EntityUid? requestedBy = null;
        if (TryComp<MedievalFactionRelationsRequestInitiatorComponent>(targetUid, out var initiatorComp))
            requestedBy = initiatorComp.RequestedBy;

        LogRelationsChanged(requestedBy, senderUid.Value, request.From, request.To, request.Relation);
        RemComp<MedievalFactionRelationsRequestComponent>(targetUid);
        RemComp<MedievalFactionRelationsRequestInitiatorComponent>(targetUid);
    }

    private void OnCreateRequest(CreateFactionRelationsRequestEvent ev, EntitySessionEventArgs args)
    {
        var senderSession = args.SenderSession;
        var senderUid = senderSession.AttachedEntity;
        if (senderUid == null)
        {
            BanPerson(senderSession, Loc.GetString("medieval-relations-error"));
            return;
        }
        if (!TryComp<MedievalFactionMemberComponent>(senderUid, out var friends) || friends.MenuAccess != FactionMenuAccess.Full || friends.Faction != ev.UserFaction)
        {
            BanPerson(senderSession, Loc.GetString("medieval-relations-error"));
            return;
        }
        var target = GetEntity(ev.Target);
        var faction = Proto.Index(ev.UserFaction);

        var coords = Transform(target).Coordinates;
        if (_container.TryGetContainingContainer(target, out var container))
            coords = Transform(container.Owner).Coordinates;

        var env = Spawn(faction.EnvelopeProto, coords);

        var comp = EnsureComp<MedievalFactionRelationsRequestComponent>(env);
        comp.From = ev.UserFaction;
        comp.To = ev.TargetFaction;
        comp.Relation = ev.Relation;
        Dirty(env, comp);

        var initiatorComp = EnsureComp<MedievalFactionRelationsRequestInitiatorComponent>(env);
        initiatorComp.RequestedBy = senderUid.Value;

        _paper.SetContent(env, Comp<PaperComponent>(target).Content);

        Comp<PaperComponent>(target).EditingDisabled = true;
        QueueDel(target);

        if (container != null)
            _container.InsertOrDrop(env, container);
    }
    private void BanPerson(ICommonSession session, string mes)
    {
        _ban.CreateServerBan(session.UserId, session.Name, null, null, null, 0, Shared.Database.NoteSeverity.High, mes);
    }
    /// <summary>
    /// Normalises untrusted war reason text: a bad reason is clamped.
    /// </summary>
    private static string SanitizeWarReason(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var trimmed = raw.AsSpan().Trim();
        if (trimmed.Length > DispatchWarEvent.MaxReasonLength)
            trimmed = trimmed[..DispatchWarEvent.MaxReasonLength];

        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            // Newlines and tabs collapse into a single space so a reason cannot blow up the popup height.
            if (c is '\n' or '\r' or '\t')
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                    sb.Append(' ');

                continue;
            }

            // drops bad (zero-width, etc) unicode characters
            if (char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format)
                continue;

            sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Snapshot of every faction currently holding Union with <paramref name="faction"/>.
    /// Returned as a fresh set so the caller may change Relations afterwards.
    /// Never returns <paramref name="faction"/> itself or <paramref name="exclude"/>, which is
    /// the other belligerent and is handled separately.
    /// </summary>
    private HashSet<ProtoId<MedievalFactionPrototype>> GetUnionAllies(
        Entity<FactionDataContainerComponent> cont,
        ProtoId<MedievalFactionPrototype> faction,
        ProtoId<MedievalFactionPrototype> exclude)
    {
        var result = new HashSet<ProtoId<MedievalFactionPrototype>>();

        if (!cont.Comp.Relations.TryGetValue(faction, out var row))
            return result;

        foreach (var (other, relation) in row)
        {
            if (other == faction || other == exclude || relation != UnionRelation)
                continue;

            result.Add(other);
        }

        return result;
    }

    /// <summary>
    /// False when the pair is the same faction, is already at war, or is permanently locked by <see cref="MedievalFactionPrototype.BlockedRelations"/>.
    /// </summary>
    private bool CanEnterWar(
        Entity<FactionDataContainerComponent> cont,
        ProtoId<MedievalFactionPrototype> a,
        ProtoId<MedievalFactionPrototype> b)
    {
        if (a == b)
            return false;

        if (Proto.Index(a).BlockedRelations.Contains(b) || Proto.Index(b).BlockedRelations.Contains(a))
            return false;

        if (!cont.Comp.Relations.TryGetValue(a, out var row) || !row.TryGetValue(b, out var current))
            return false;

        return current != WarRelation;
    }

    private void OnDispatchWar(DispatchWarEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetFactionDataContainer(out var cont))
            return;

        var senderSession = args.SenderSession;
        var senderUid = senderSession.AttachedEntity;
        if (senderUid == null)
        {
            BanPerson(senderSession, Loc.GetString("medieval-relations-error"));
            return;
        }
        if (!TryComp<MedievalFactionMemberComponent>(senderUid, out var friends) || friends.MenuAccess != FactionMenuAccess.Full || friends.Faction != ev.UserFaction)
        {
            BanPerson(senderSession, Loc.GetString("medieval-relations-error"));
            return;
        }

        // bad war reason is clamped.
        var reason = SanitizeWarReason(ev.Reason);

        var declarer = ev.UserFaction;
        var target = ev.TargetFaction;

        // Already at war, blocked, or self-targeted: nothing to change. The client button is disabled
        // in that state, so this is only reachable from a modified client, returning prevents this
        if (!CanEnterWar(cont.Value, declarer, target))
            return;

        // Snapshot alliances BEFORE any changes. Writing Relations[X][B] = War would otherwise
        // destroy a Union that a later read depends on, making the outcome order-dependent.
        var declarerAllies = GetUnionAllies(cont.Value, declarer, target);
        var targetAllies = GetUnionAllies(cont.Value, target, declarer);

        // Build the full pair list, still reading pre-change state.
        // ONE HOP ONLY: declarerAllies and targetAllies are never re-expanded, so allies of allies
        // are left out of the war.
        var pairs = new List<(ProtoId<MedievalFactionPrototype> Ally, ProtoId<MedievalFactionPrototype> Foe)>
        {
            (declarer, target)
        };

        foreach (var ally in declarerAllies)
        {
            if (CanEnterWar(cont.Value, ally, target))
                pairs.Add((ally, target));
        }

        foreach (var ally in targetAllies)
        {
            // To make a faction allied to BOTH sides stay out of the war instead, add here:
            //     if (declarerAllies.Contains(ally)) continue;
            // ...and the matching skip in the loop above.
            if (CanEnterWar(cont.Value, ally, declarer))
                pairs.Add((ally, declarer));
        }

        // Apply every pair, then Dirty once.
        ref var relations = ref cont.Value.Comp.Relations;
        foreach (var (a, b) in pairs)
        {
            relations[a][b] = WarRelation;
            relations[b][a] = WarRelation;
        }
        Dirty(cont.Value);

        // De-duplicated map of faction -> how that faction got dragged in, used to pick the popup variant.
        var involvement = new Dictionary<ProtoId<MedievalFactionPrototype>, WarInvolvement>
        {
            [declarer] = WarInvolvement.Declarer,
            [target] = WarInvolvement.Target
        };

        foreach (var (ally, foe) in pairs)
        {
            if (ally == declarer && foe == target)
                continue;

            // foe is always either declarer or target.
            var side = foe == target ? WarInvolvement.AllyOfDeclarer : WarInvolvement.AllyOfTarget;

            involvement[ally] = involvement.TryGetValue(ally, out var existing) && existing != side
                ? WarInvolvement.AllyOfBoth
                : side;
        }

        // One radio line per changed pair. The sound is suppressed here so the popup pass below can
        // play it exactly once per player, otherwise anyone in two changed pairs hears it twice.
        foreach (var (a, b) in pairs)
            AnnounceRelationChange(cont.Value, a, b, WarRelation, playSound: false);

        foreach (var (faction, kind) in involvement)
        {
            foreach (var member in cont.Value.Comp.CachedMembers.GetOrNew(faction))
            {
                if (!GetFactionMemberById(member.Key, out var memberUid) || !_sharedPlayerManager.TryGetSessionByEntity(memberUid.Value, out var session))
                    continue;

                RaiseNetworkEvent(new MedievalWarDeclaredEvent(declarer, target, reason, kind), session);
                _audio.PlayGlobal(RelationChangedSound, session);
            }
        }

        var dragged = pairs.Count > 1
            ? string.Join(", ", pairs.Skip(1).Select(p => Proto.Index(p.Ally).Name))
            : "none";

        _adminLogger.Add(LogType.MedievalFactionRelations, LogImpact.High,
            $"Leader {ToPrettyString(senderUid.Value):leader} of faction {Proto.Index(declarer).Name:declarer} declared war on {Proto.Index(target).Name:target}. Reason: {reason:reason}. Allies dragged in: {dragged:allies}");
    }

    private void SetRelations(ProtoId<MedievalFactionPrototype> userFaction, ProtoId<MedievalFactionPrototype> targetFaction, ProtoId<FactionRelationsPrototype> relation)
    {
        if (!TryGetFactionDataContainer(out var cont))
            return;

        ref var relations = ref cont.Value.Comp.Relations;
        relations[userFaction][targetFaction] = relation;
        relations[targetFaction][userFaction] = relation;
        Dirty(cont.Value);

        AnnounceRelationChange(cont.Value, userFaction, targetFaction, relation);
    }

    /// <summary>
    /// Radio-channel notice to every member of both factions telling them their relation changed.
    /// </summary>
    private void AnnounceRelationChange(
        Entity<FactionDataContainerComponent> cont,
        ProtoId<MedievalFactionPrototype> userFaction,
        ProtoId<MedievalFactionPrototype> targetFaction,
        ProtoId<FactionRelationsPrototype> relation,
        bool playSound = true)
    {
        var relationProto = Proto.Index(relation);
        var userFactionProto = Proto.Index(userFaction);
        var targetFactionProto = Proto.Index(targetFaction);

        var userMembers = cont.Comp.CachedMembers.GetOrNew(userFaction);
        var targetMembers = cont.Comp.CachedMembers.GetOrNew(targetFaction);

        foreach (var item in userMembers.Union(targetMembers))
        {
            if (!GetFactionMemberById(item.Key, out var target) || !_sharedPlayerManager.TryGetSessionByEntity(target.Value, out var session))
                continue;

            // The recipient is told about the OTHER faction, so pick whichever side is not their own.
            var otherFaction = item.Value.Faction == userFaction ? targetFactionProto : userFactionProto;

            var announcement = Loc.GetString("faction-relations-changed-announcement",
                ("faction", otherFaction.Name),
                ("relation", relationProto.Name));

            _chatMan.ChatMessageToOne(Shared.Chat.ChatChannel.Radio, announcement, announcement, EntityUid.Invalid, false, session.Channel, relationProto.Color);

            if (playSound)
                _audio.PlayGlobal(RelationChangedSound, session);
        }
    }

    private void StorePendingRelationsOffer(
        EntityUid targetUid,
        ProtoId<MedievalFactionPrototype> userFaction,
        ProtoId<MedievalFactionPrototype> targetFaction,
        ProtoId<FactionRelationsPrototype> relation,
        EntityUid offeredBy)
    {
        var pendingComp = EnsureComp<MedievalFactionRelationsPendingOffersComponent>(targetUid);
        pendingComp.Offers.RemoveAll(offer => offer.UserFaction == userFaction && offer.TargetFaction == targetFaction);
        pendingComp.Offers.Add(new MedievalFactionRelationsPendingOfferData
        {
            UserFaction = userFaction,
            TargetFaction = targetFaction,
            Relation = relation,
            OfferedBy = offeredBy
        });
    }

    private bool TryTakePendingRelationsOffer(
        EntityUid targetUid,
        ProtoId<MedievalFactionPrototype> userFaction,
        ProtoId<MedievalFactionPrototype> targetFaction,
        ProtoId<FactionRelationsPrototype> relation,
        out EntityUid? offeredBy)
    {
        offeredBy = null;
        if (!TryComp<MedievalFactionRelationsPendingOffersComponent>(targetUid, out var pendingComp))
            return false;

        for (var i = 0; i < pendingComp.Offers.Count; i++)
        {
            var offer = pendingComp.Offers[i];
            if (offer.UserFaction != userFaction || offer.TargetFaction != targetFaction || offer.Relation != relation)
                continue;

            offeredBy = offer.OfferedBy;
            pendingComp.Offers.RemoveAt(i);
            if (pendingComp.Offers.Count == 0)
                RemComp<MedievalFactionRelationsPendingOffersComponent>(targetUid);

            return true;
        }

        return false;
    }

    private void LogRelationsChanged(
        EntityUid? offeredBy,
        EntityUid acceptedBy,
        ProtoId<MedievalFactionPrototype> userFaction,
        ProtoId<MedievalFactionPrototype> targetFaction,
        ProtoId<FactionRelationsPrototype> relation)
    {
        // Both leaders were tagged ":leader" before, which collided in the log and pushed the second one out to a "leader_2" key. They are now tagged distinctly.
        if (offeredBy != null)
        {
            _adminLogger.Add(LogType.MedievalFactionRelations, LogImpact.Medium,
                $"Faction leaders {ToPrettyString(offeredBy.Value):offeredBy} and {ToPrettyString(acceptedBy):acceptedBy} changed relations between factions {Proto.Index(userFaction).Name:userFaction} and {Proto.Index(targetFaction).Name:targetFaction} to {relation.Id:relation}");
            return;
        }

        _adminLogger.Add(LogType.MedievalFactionRelations, LogImpact.Medium,
            $"Faction leaders unknown and {ToPrettyString(acceptedBy):acceptedBy} changed relations between factions {Proto.Index(userFaction).Name:userFaction} and {Proto.Index(targetFaction).Name:targetFaction} to {relation.Id:relation}");
    }

    private void OnFactionDataContainerInit(EntityUid uid, FactionDataContainerComponent comp, MapInitEvent args)
    {
        var factions = Proto.EnumeratePrototypes<MedievalFactionPrototype>();
        foreach (var item in factions)
        {
            foreach (var item2 in factions)
            {
                if (item == item2)
                    continue;

                comp.Relations.TryAdd(item.ID, new());
                comp.Relations[item.ID].Add(item2.ID, item.DefaultRelations.GetValueOrDefault(item2.ID, "Neutral"));
            }
        }

        Dirty(uid, comp);
    }
}
