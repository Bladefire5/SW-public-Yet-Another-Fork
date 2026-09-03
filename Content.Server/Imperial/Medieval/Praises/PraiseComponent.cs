namespace Content.Server.Imperial.Medieval.Praises;

//should be present both on the player sending praise, and on the one receiving it
[RegisterComponent]
public sealed partial class PraiseComponent : Component
{
    [DataField]
    public int Weight = 1;
}
