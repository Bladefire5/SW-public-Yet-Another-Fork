namespace Content.Shared.Nocturn.Components;

[RegisterComponent]
public sealed partial class AncientNocturneMindConnectionComponent : Component
{
    [DataField]
    public string ChatPrefix = ":е";

    [DataField]
    public string AlternateChatPrefix = ":t";

    [DataField]
    public Color ChatColor = Color.FromHex("#A060E8");

    public HashSet<EntityUid> Tralls = new();

    public EntityUid? ActiveEntity;
}

[RegisterComponent]
public sealed partial class AncientNocturneTrallMindConnectionComponent : Component
{
    public EntityUid Master;

    public bool IsMasterRelay;
}
