using System.Linq;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;

namespace Content.Server.Imperial.Medieval.Magic.MedievalSpawnInFreeSlot;

/// <summary>
/// Places an item in a free carried slot without displacing other items.
/// Already carried items stay in their current hand, inventory slot, or storage.
/// </summary>
public sealed class MedievalSpawnInFreeSlotSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _handsSystem = default!;
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    [Dependency] private readonly SharedStorageSystem _storageSystem = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;

    public bool TryPlaceInFreeSlot(EntityUid playerUid, EntityUid itemUid)
    {
        var carriedEntities = _inventorySystem.GetHandOrInventoryEntities(playerUid).ToList();

        if (carriedEntities.Contains(itemUid))
            return true;

        var current = itemUid;
        while (_containerSystem.TryGetContainingContainer(current, out var container))
        {
            if (carriedEntities.Contains(container.Owner))
                return true;

            current = container.Owner;
        }

        if (_handsSystem.TryPickupAnyHand(playerUid, itemUid))
            return true;

        foreach (var carried in carriedEntities)
        {
            if (carried == itemUid || !TryComp<StorageComponent>(carried, out var storage))
                continue;

            if (_storageSystem.Insert(carried, itemUid, out _, storageComp: storage, playSound: false))
                return true;
        }

        return false;
    }
}
