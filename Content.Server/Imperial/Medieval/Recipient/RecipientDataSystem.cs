using System.Linq;
using Content.Server.Mind;
using Content.Server.Roles.Jobs;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Imperial.Medieval.Recipient;
using Content.Shared.Preferences;

namespace Content.Server.Imperial.Medieval.Recipient;

public sealed class RecipientDataSystem : EntitySystem
{
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly JobSystem _job = default!;

    public RecipientData? GetRecipientData(EntityUid recipient)
    {
        var profile = BuildRecipientProfile(recipient);
        if (profile == null)
            return null;

        var jobName = Loc.GetString("job-name-unknown");
        string? jobId = null;

        if (_mind.TryGetMind(recipient, out var mindUid, out _))
        {
            if (_job.MindTryGetJobName(mindUid, out var recipientJobName) &&
                !string.IsNullOrWhiteSpace(recipientJobName))
            {
                jobName = recipientJobName;
            }

            if (_job.MindTryGetJobId(mindUid, out var recipientJobId) &&
                recipientJobId != null)
            {
                jobId = recipientJobId.Value.ToString();
            }
        }

        return new RecipientData(profile, jobName, jobId);
    }

    private HumanoidCharacterProfile? BuildRecipientProfile(EntityUid recipient)
    {
        if (!TryComp<HumanoidAppearanceComponent>(recipient, out var humanoid))
            return null;

        var appearance = new HumanoidCharacterAppearance
        {
            EyeColor = humanoid.EyeColor,
            SkinColor = humanoid.SkinColor,
            Markings = humanoid.MarkingSet.GetForwardEnumerator().ToList(),
        };

        if (humanoid.MarkingSet.TryGetCategory(MarkingCategories.Hair, out var hairMarkings) &&
            hairMarkings.Count > 0)
        {
            var hair = hairMarkings[0];
            appearance = appearance.WithHairStyleName(hair.MarkingId);
            if (hair.MarkingColors.Count > 0)
                appearance = appearance.WithHairColor(hair.MarkingColors[0]);
        }

        if (humanoid.MarkingSet.TryGetCategory(MarkingCategories.FacialHair, out var facialHairMarkings) &&
            facialHairMarkings.Count > 0)
        {
            var facialHair = facialHairMarkings[0];
            appearance = appearance.WithFacialHairStyleName(facialHair.MarkingId);
            if (facialHair.MarkingColors.Count > 0)
                appearance = appearance.WithFacialHairColor(facialHair.MarkingColors[0]);
        }

        return new HumanoidCharacterProfile()
            .WithCharacterAppearance(appearance)
            .WithSpecies(humanoid.Species)
            .WithSex(humanoid.Sex)
            .WithGender(humanoid.Gender)
            .WithAge(humanoid.Age)
            .WithName(Name(recipient));
    }
}
