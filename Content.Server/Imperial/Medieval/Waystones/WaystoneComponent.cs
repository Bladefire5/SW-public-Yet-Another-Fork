using Content.Shared.DoAfter;
using Content.Shared.Imperial.Medieval.Factions.Prototypes;
using Robust.Shared.Prototypes;

[RegisterComponent]
public sealed partial class WaystoneComponent : Component
{
    [DataField]
    public string Name = "Waystone";

    [DataField]
    public ProtoId<MedievalFactionPrototype> Faction { get; set; } = string.Empty;

    [DataField]
    public string LinkId = string.Empty;

    [DataField]
    public float TimeToTeleport = 30f;

    [DataField]
    public int DeparturePrice = 12;
    [DataField]
    public int ArrivalPrice = 6;

    [DataField]
    public bool IsEnable = true;

    [DataField]
    public EntityUid? SelectedWaystone;

    [DataField]
    public int CurrentPaid = 0;

    [DataField]
    public EntityUid? User;

    [DataField]
    public TimeSpan BookedTime = TimeSpan.Zero;
    [DataField]
    public float BookedSeconds = 10f;

    public EntityUid? BookedAudioStream;

    public DoAfterId? ActiveDoAfterId;

    [DataField]
    public int CollectedMoney = 0;

    [DataField]
    public float MaxEnergy = 100f;

    [DataField]
    public float CurrentEnergy = 100f;

    [DataField]
    public float EnergyPrice = 30f;

    [DataField]
    public float EnergyRegenRate = 0.25f;

    [DataField]
    public string LinkedCircle = string.Empty;

    public TimeSpan LastMessageTime = TimeSpan.Zero;

    /// <summary>
    /// First array element - Departy, Second - Arrival
    /// </summary>
    public int[] TeleportationMoney = new int[2];
}
