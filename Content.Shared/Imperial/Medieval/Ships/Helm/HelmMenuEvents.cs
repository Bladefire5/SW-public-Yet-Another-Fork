using System;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Ships.Helm;

[Serializable, NetSerializable]
public enum HelmUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class HelmBoundUserInterfaceState : BoundUserInterfaceState, IEquatable<HelmBoundUserInterfaceState>
{
    public float HelmRotation;
    public float RotationStep;

    public HelmBoundUserInterfaceState(float helmRotation, float rotationStep)
    {
        HelmRotation = helmRotation;
        RotationStep = rotationStep;
    }

    public bool Equals(HelmBoundUserInterfaceState? other)
    {
        return other != null &&
               HelmRotation.Equals(other.HelmRotation) &&
               RotationStep.Equals(other.RotationStep);
    }

    public override bool Equals(object? obj)
    {
        return obj is HelmBoundUserInterfaceState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(HelmRotation, RotationStep);
    }
}

[Serializable, NetSerializable]
public sealed class HelmRotationChangeMessage : BoundUserInterfaceMessage
{
    public float HelmRotation;
    public bool Turning;

    public HelmRotationChangeMessage(float helmRotation, bool turning)
    {
        HelmRotation = helmRotation;
        Turning = turning;
    }
}
