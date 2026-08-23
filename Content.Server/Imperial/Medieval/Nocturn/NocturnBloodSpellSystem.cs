using System.Linq;
using Content.Shared.Imperial.Medieval.Magic;
using Content.Shared.Nocturn.Components;
using Content.Shared.Popups;

namespace Content.Server.Nocturn;

public sealed class NocturnBloodSpellSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NocturnBloodDrainSpellComponent, MedievalBeforeCastSpellEvent>(OnBeforeCast);
        SubscribeLocalEvent<NocturnBloodDrainSpellComponent, MedievalAfterCastSpellEvent>(OnAfterCast);
        SubscribeLocalEvent<NocturnBloodDrainSpellComponent, MedievalFailCastSpellEvent>(OnFailedCast);
    }

    private void OnBeforeCast(
        EntityUid uid,
        NocturnBloodDrainSpellComponent component,
        ref MedievalBeforeCastSpellEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryComp<NocturnComponent>(args.Performer, out var nocturn))
        {
            _popup.PopupEntity(
                Loc.GetString("medieval-nocturn-cant-use-blood-spells"),
                args.Performer,
                args.Performer,
                PopupType.LargeCaution);
            args.Cancelled = true;
            return;
        }

        var reservedBlood = nocturn.CastedBloodSpells.Values.Sum();
        if (nocturn.CastedBloodSpells.TryGetValue(uid, out var existingReservation))
            reservedBlood -= existingReservation;

        if (nocturn.BloodLevel - reservedBlood < component.BloodDrain)
        {
            _popup.PopupEntity(
                Loc.GetString("medieval-nocturn-not-enough-blood"),
                args.Performer,
                args.Performer,
                PopupType.LargeCaution);
            args.Cancelled = true;
            return;
        }

        nocturn.CastedBloodSpells[uid] = component.BloodDrain;
    }

    private void OnAfterCast(
        EntityUid uid,
        NocturnBloodDrainSpellComponent component,
        MedievalAfterCastSpellEvent args)
    {
        if (!TryComp<NocturnComponent>(args.Performer, out var nocturn) ||
            !nocturn.CastedBloodSpells.Remove(uid, out var bloodCost))
            return;

        nocturn.BloodLevel = MathF.Max(0f, nocturn.BloodLevel - bloodCost);
        Dirty(args.Performer, nocturn);

        if (args.ShowManaPopup)
        {
            _popup.PopupEntity(
                Loc.GetString("medieval-nocturn-blood-cast-spell", ("bloodCost", component.BloodDrain)),
                args.Performer,
                args.Performer,
                PopupType.Large);
        }
    }

    private void OnFailedCast(
        EntityUid uid,
        NocturnBloodDrainSpellComponent component,
        MedievalFailCastSpellEvent args)
    {
        ClearReservation(args.Performer, uid);
    }

    public void ClearReservation(EntityUid performer, EntityUid action)
    {
        if (TryComp<NocturnComponent>(performer, out var nocturn))
            nocturn.CastedBloodSpells.Remove(action);
    }
}
