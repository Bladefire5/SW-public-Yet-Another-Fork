using Content.Shared.Preferences;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Recipient;

[Serializable, NetSerializable]
public sealed class RecipientData
{
    public readonly HumanoidCharacterProfile Profile;
    public readonly string JobName;
    public readonly string? JobId;

    public RecipientData(HumanoidCharacterProfile profile, string jobName, string? jobId)
    {
        Profile = profile;
        JobName = jobName;
        JobId = jobId;
    }
}
