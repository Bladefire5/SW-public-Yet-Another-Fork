using Content.Client.Imperial.Medieval.Factions;
using Content.Shared.Imperial.Medieval.Factions;
using Content.Shared.Imperial.Medieval.Factions.Components;
using Content.Shared.Imperial.Medieval.Factions.Prototypes;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Prototypes;

namespace Content.Client.Imperial.Medieval.Factions.UI;

[UsedImplicitly]
public sealed class FactionMenuUiController : UIController
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private FactionMenu? _menu;

    public void ToggleMenu()
    {
        if (_menu == null)
        {
            // setup window
            _menu = UIManager.CreateWindow<FactionMenu>();
            _menu.OnClose += () => _menu = null;

            _menu.ObjectiveSet += ObjectiveSet;
            _menu.GroupSet += GroupSet;
            _menu.SetLeaderPressed += SetLeader;
            _menu.FirePressed += args => Fire(args, "", false);
            _menu.HeadhuntPressed += (id, details) => Fire(id, details, true);
            _menu.WarPressed += DispatchWar;

            _menu.OpenCentered();
        }
        else
        {
            _menu.ObjectiveSet -= ObjectiveSet;
            _menu.GroupSet -= GroupSet;
            _menu.SetLeaderPressed -= SetLeader;
            _menu.FirePressed -= args => Fire(args, "", false);
            _menu.HeadhuntPressed -= (id, details) => Fire(id, details, true);
            _menu.WarPressed -= DispatchWar;

            _menu.Close();
            _menu = null;
        }
    }

    public void PopulateMenu(FactionMenuData data)
    {
        if (_menu == null)
            return;

        var previous = _menu.Data;
        _menu.Data = data;

        switch (_menu.Mode)
        {
            case FactionMenu.MenuMode.Goals:
                _menu.PopulateGoals(data.Goals);
                break;
            case FactionMenu.MenuMode.Relations:
                // the networked state generator clones Dictionary fields, data.Relations is a fresh instance on every state.
                if (ReferenceEquals(data.Relations, previous.Relations))
                    return;

                _menu.PopulateRelations(data);
                break;
            default:
                _menu.Populate(data);
                break;
        }
    }

    private void Fire(int ent, string details, bool headhunt = false)
    {
        var playerMan = IoCManager.Resolve<IPlayerManager>();
        if (_entityManager.TryGetComponent<MedievalFactionMemberComponent>(playerMan.LocalEntity, out var friends))
            _entityManager.RaisePredictiveEvent(new RemoveFactionMemberMessage(ent, friends.MemberID, details, headhunt));
    }

    private void ObjectiveSet(FactionMemberGroup group, string obj)
    {
        if (_menu == null)
            return;

        _entityManager.RaisePredictiveEvent(new SetFactionMemberObjectiveMessage(_menu.Data.Faction, group, obj));
    }

    private void GroupSet(int ent, FactionMemberGroup obj)
    {
        _entityManager.RaisePredictiveEvent(new SetFactionMemberGroupMessage(ent, obj));
    }

    private void SetLeader(int ent, bool leader)
    {
        _entityManager.RaisePredictiveEvent(new SetGroupLeaderMessage(ent, leader));
    }

    private void DispatchWar(ProtoId<MedievalFactionPrototype> faction)
    {
        if (_menu == null)
            return;

        // raises DispatchWarEvent after confirmation
        UIManager.GetUIController<FactionRelationsUiController>().OpenDeclareWarMenu(_menu.Data.Faction, faction);
    }
}
