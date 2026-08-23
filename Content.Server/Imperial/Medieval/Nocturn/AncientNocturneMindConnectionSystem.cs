using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Nocturn.Components;
using Content.Shared.Polymorph;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Nocturn;

public sealed class AncientNocturneMindConnectionSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AncientNocturneMindConnectionComponent, ComponentStartup>(OnMasterStartup);
        SubscribeLocalEvent<AncientNocturneMindConnectionComponent, ComponentShutdown>(OnMasterShutdown);
        SubscribeLocalEvent<AncientNocturneTrallMindConnectionComponent, ComponentShutdown>(OnTrallShutdown);
        SubscribeLocalEvent<AncientNocturneMindConnectionComponent, BeforeInGameICMessageEvent>(OnMasterMessage);
        SubscribeLocalEvent<AncientNocturneTrallMindConnectionComponent, BeforeInGameICMessageEvent>(OnTrallMessage);
        SubscribeLocalEvent<AncientNocturneMindConnectionComponent, PolymorphedEvent>(OnMasterPolymorphed);
        SubscribeLocalEvent<AncientNocturneTrallMindConnectionComponent, PolymorphedEvent>(OnTrallPolymorphed);
    }

    private void OnMasterStartup(
        Entity<AncientNocturneMindConnectionComponent> ent,
        ref ComponentStartup args)
    {
        ent.Comp.ActiveEntity = ent.Owner;
    }

    private void OnMasterShutdown(
        Entity<AncientNocturneMindConnectionComponent> ent,
        ref ComponentShutdown args)
    {
        foreach (var trallUid in ent.Comp.Tralls.ToArray())
        {
            if (!TryComp<AncientNocturneTrallMindConnectionComponent>(trallUid, out var trall) ||
                trall.Master != ent.Owner)
                continue;

            SendConnectionSevered(trallUid, ent.Comp.ChatColor);
            RemComp<AncientNocturneTrallMindConnectionComponent>(trallUid);
        }

        if (ent.Comp.ActiveEntity is { } active &&
            active != ent.Owner &&
            TryComp<AncientNocturneTrallMindConnectionComponent>(active, out var relay) &&
            relay.IsMasterRelay &&
            relay.Master == ent.Owner)
        {
            RemComp<AncientNocturneTrallMindConnectionComponent>(active);
        }

        ent.Comp.Tralls.Clear();
    }

    private void OnTrallShutdown(
        Entity<AncientNocturneTrallMindConnectionComponent> ent,
        ref ComponentShutdown args)
    {
        if (ent.Comp.IsMasterRelay)
            return;

        if (TryComp<AncientNocturneMindConnectionComponent>(ent.Comp.Master, out var master))
            master.Tralls.Remove(ent.Owner);
    }

    private void OnMasterMessage(
        Entity<AncientNocturneMindConnectionComponent> ent,
        ref BeforeInGameICMessageEvent args)
    {
        SendMindMessage((ent.Owner, ent.Comp), ent.Owner, ent.Owner, ref args);
    }

    private void OnTrallMessage(
        Entity<AncientNocturneTrallMindConnectionComponent> ent,
        ref BeforeInGameICMessageEvent args)
    {
        if (!TryComp<AncientNocturneMindConnectionComponent>(ent.Comp.Master, out var master))
            return;

        var nameSource = ent.Comp.IsMasterRelay ? ent.Comp.Master : ent.Owner;
        SendMindMessage((ent.Comp.Master, master), ent.Owner, nameSource, ref args);
    }

    private void SendMindMessage(
        Entity<AncientNocturneMindConnectionComponent> master,
        EntityUid source,
        EntityUid nameSource,
        ref BeforeInGameICMessageEvent args)
    {
        var prefix = args.Message.StartsWith(master.Comp.ChatPrefix, StringComparison.OrdinalIgnoreCase)
            ? master.Comp.ChatPrefix
            : args.Message.StartsWith(master.Comp.AlternateChatPrefix, StringComparison.OrdinalIgnoreCase)
                ? master.Comp.AlternateChatPrefix
                : null;

        if (prefix == null)
            return;

        args.Handled = true;
        var message = args.Message[prefix.Length..].TrimStart();
        if (string.IsNullOrWhiteSpace(message))
            return;

        var recipients = Filter.Empty();
        if (master.Comp.ActiveEntity is { } activeMaster)
            AddRecipient(recipients, activeMaster);
        else
            AddRecipient(recipients, master.Owner);

        foreach (var trallUid in master.Comp.Tralls.ToArray())
        {
            if (TerminatingOrDeleted(trallUid) ||
                !TryComp<AncientNocturneTrallMindConnectionComponent>(trallUid, out var trall) ||
                trall.Master != master.Owner)
            {
                master.Comp.Tralls.Remove(trallUid);
                continue;
            }

            AddRecipient(recipients, trallUid);
        }

        var escapedName = FormattedMessage.EscapeText(Name(nameSource));
        var escapedMessage = FormattedMessage.EscapeText(message);
        var wrappedMessage = Loc.GetString(
            "medieval-ancient-nocturne-mind-connection-wrap-message",
            ("name", escapedName),
            ("message", escapedMessage));
        wrappedMessage = $"[color={master.Comp.ChatColor.ToHex()}]{wrappedMessage}[/color]";

        _chat.ChatMessageToManyFiltered(
            recipients,
            ChatChannel.Radio,
            message,
            wrappedMessage,
            source,
            false,
            false,
            master.Comp.ChatColor);
    }

    private void OnMasterPolymorphed(
        Entity<AncientNocturneMindConnectionComponent> ent,
        ref PolymorphedEvent args)
    {
        if (args.IsRevert)
            return;

        ent.Comp.ActiveEntity = args.NewEntity;
        var relay = EnsureComp<AncientNocturneTrallMindConnectionComponent>(args.NewEntity);
        relay.Master = ent.Owner;
        relay.IsMasterRelay = true;
    }

    private void OnTrallPolymorphed(
        Entity<AncientNocturneTrallMindConnectionComponent> ent,
        ref PolymorphedEvent args)
    {
        if (!ent.Comp.IsMasterRelay ||
            !TryComp<AncientNocturneMindConnectionComponent>(ent.Comp.Master, out var master))
            return;

        master.ActiveEntity = args.NewEntity;
        if (args.IsRevert)
            return;

        var relay = EnsureComp<AncientNocturneTrallMindConnectionComponent>(args.NewEntity);
        relay.Master = ent.Comp.Master;
        relay.IsMasterRelay = true;
    }

    private void SendConnectionSevered(EntityUid trall, Color color)
    {
        if (!TryComp<ActorComponent>(trall, out var actor))
            return;

        var message = Loc.GetString("medieval-ancient-nocturne-mind-connection-severed");
        var wrappedMessage = $"[color={color.ToHex()}]{FormattedMessage.EscapeText(message)}[/color]";
        _chat.ChatMessageToOne(
            ChatChannel.Radio,
            message,
            wrappedMessage,
            EntityUid.Invalid,
            false,
            actor.PlayerSession.Channel,
            color);
    }

    private void AddRecipient(Filter filter, EntityUid uid)
    {
        if (TryComp<ActorComponent>(uid, out var actor))
            filter.AddPlayer(actor.PlayerSession);
    }
}
