using System;
using System.Collections.Generic;
using Content.Server.Imperial.Medieval.Ships;
using Content.Server.Shuttles.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Examine;
using Content.Shared.Imperial.Medieval.Administration.Ships;
using Content.Shared.Imperial.Medieval.Ships.Helm;
using Content.Shared.Interaction;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.UserInterface;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server.Imperial.Medieval.Ships.Helm;

public sealed class HelmSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly ShipGridSystem _shipGrid = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly AudioSystem _audio = default!;

    private TimeSpan _nextCheckTime;
    private readonly Dictionary<EntityUid, PilotState> _pilots = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<HelmComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<HelmComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<HelmComponent, BeforeActivatableUIOpenEvent>(OnBeforeUiOpen);
        SubscribeLocalEvent<HelmComponent, BoundUIClosedEvent>(OnAfterUiClosed);
        SubscribeLocalEvent<HelmComponent, HelmRotationChangeMessage>(OnRotationChangeMessage);

        SubscribeLocalEvent<MedievalPilotComponent, UpdateCanMoveEvent>(OnUpdateCanMove);
        SubscribeLocalEvent<MedievalPilotComponent, ComponentShutdown>(OnPilotShutdown);
    }

    private void OnUpdateCanMove(EntityUid uid, MedievalPilotComponent component, ref UpdateCanMoveEvent args)
    {
        args.Cancel();
    }

    private void OnStartup(EntityUid uid, HelmComponent component, ComponentStartup args)
    {
        component.HelmRotation = NormalizeHelmRotation(component.HelmRotation);
    }

    private void OnBeforeUiOpen(EntityUid uid, HelmComponent component, BeforeActivatableUIOpenEvent args)
    {
        EnsureComp<MedievalPilotComponent>(args.User);
        if (_pilots.Remove(args.User, out var previous))
            StopUsingSound(previous);

        _pilots[args.User] = new PilotState(uid, _timing.CurTime);
        _actionBlocker.UpdateCanMove(args.User);

        UpdateUi(uid, component);
    }

    private void OnAfterUiClosed(EntityUid uid, HelmComponent component, BoundUIClosedEvent args)
    {
        if (!_pilots.TryGetValue(args.Actor, out var pilot) || pilot.Helm != uid)
            return;

        RemComp<MedievalPilotComponent>(args.Actor);
        _actionBlocker.UpdateCanMove(args.Actor);
    }

    private void OnPilotShutdown(Entity<MedievalPilotComponent> entity, ref ComponentShutdown args)
    {
        if (_pilots.Remove(entity, out var pilot))
            StopUsingSound(pilot);
    }

    private void OnRotationChangeMessage(EntityUid uid, HelmComponent component, HelmRotationChangeMessage msg)
    {
        var player = msg.Actor;
        if (!HasComp<MedievalPilotComponent>(player) ||
            !_pilots.TryGetValue(player, out var pilot) ||
            pilot.Helm != uid ||
            !_actionBlocker.CanInteract(player, uid) ||
            !_actionBlocker.CanComplexInteract(player) ||
            !_interaction.InRangeAndAccessible(player, uid) ||
            !float.IsFinite(msg.HelmRotation))
        {
            return;
        }

        var curTime = _timing.CurTime;
        var elapsed = Math.Max(0f, (float) (curTime - pilot.LastRotationUpdate).TotalSeconds);
        var rotationStep = float.IsFinite(component.RotationStep) ? MathF.Abs(component.RotationStep) : 0f;
        var budgetSeconds = float.IsFinite(component.RotationSyncMaxBudgetSeconds)
            ? MathF.Max(0f, component.RotationSyncMaxBudgetSeconds)
            : 0f;
        var maxBudget = rotationStep * budgetSeconds;
        pilot.RotationBudget = MathF.Min(maxBudget, pilot.RotationBudget + rotationStep * elapsed);
        pilot.LastRotationUpdate = curTime;

        var requestedRotation = Math.Clamp(msg.HelmRotation, -180f, 180f);
        var requestedDelta = requestedRotation - component.HelmRotation;
        var appliedDelta = Math.Clamp(requestedDelta, -pilot.RotationBudget, pilot.RotationBudget);
        component.HelmRotation = Math.Clamp(component.HelmRotation + appliedDelta, -180f, 180f);
        pilot.RotationBudget = MathF.Max(0f, pilot.RotationBudget - MathF.Abs(appliedDelta));

        if (msg.Turning && MathF.Abs(appliedDelta) >= 0.001f)
            StartUsingSound(uid, pilot);
        else
            StopUsingSound(pilot);
    }

    private void StartUsingSound(EntityUid helm, PilotState pilot)
    {
        if (pilot.UsingSound != null)
            return;

        var audioParams = AudioParams.Default.WithLoop(true);
        pilot.UsingSound = _audio.PlayPvs(
            new SoundPathSpecifier("/Audio/Imperial/Medieval/hitting_wood_4times.ogg"),
            helm,
            audioParams)?.Entity;
    }

    private void StopUsingSound(PilotState pilot)
    {
        pilot.UsingSound = _audio.Stop(pilot.UsingSound);
    }

    private void OnExamine(EntityUid uid, HelmComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (component.HelmRotation == 0f)
        {
            args.PushMarkup(Loc.GetString("helm-examine-center"));
        }
        else
        {
            var degrees = MathF.Abs(component.HelmRotation).ToString("0.##");
            if (component.HelmRotation > 0f)
                args.PushMarkup(Loc.GetString("helm-examine-right", ("degrees", degrees)));
            else
                args.PushMarkup(Loc.GetString("helm-examine-left", ("degrees", degrees)));
        }

        var sailsEfficiency = _shipGrid.TryGetHelmGrid(uid, out _, out var shipGrid)
                ? shipGrid.SailsEfficiency
                : 0f;
        args.PushMarkup(Loc.GetString(
            "helm-examine-sails-efficiency",
            ("efficiency", FormatEfficiency(sailsEfficiency))));

        if (TryGetShipLoad(uid, out var weight, out var overloadCeil))
        {
            args.PushMarkup(Loc.GetString(
                "helm-examine-ship-load",
                ("weight", FormatWeight(weight)),
                ("overloadCeil", FormatWeight(overloadCeil))));
        }
    }

    private void UpdateUi(EntityUid uid, HelmComponent component)
    {
        var rotation = NormalizeHelmRotation(component.HelmRotation);
        var rotationStep = float.IsFinite(component.RotationStep)
            ? MathF.Abs(component.RotationStep)
            : 0f;

        _ui.SetUiState(
            uid,
            HelmUiKey.Key,
            new HelmBoundUserInterfaceState(rotation, rotationStep));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime <= _nextCheckTime)
            return;

        _nextCheckTime = _timing.CurTime + GetUpdateDelay();
        var windEnabled = _cfg.GetCVar(ShipsCCVars.WindEnabled);

        var query = EntityQueryEnumerator<ShipGridComponent>();
        while (query.MoveNext(out var boat, out var grid))
        {
            if (grid.Helm is not { } helmUid ||
                !TryComp<HelmComponent>(helmUid, out var helmComponent))
            {
                continue;
            }

            if (windEnabled)
                RotateShip(boat, grid, helmComponent);
        }
    }

    private void RotateShip(EntityUid boat, ShipGridComponent grid, HelmComponent helmComponent)
    {
        if (TryComp<ShuttleComponent>(boat, out var shuttle) && !shuttle.Enabled)
            return;

        if (grid.HasLoweredAnchor)
            return;

        var steeringPower = grid.SteeringPower;
        if (!float.IsFinite(steeringPower) || steeringPower <= 0f)
            return;

        if (!TryComp<PhysicsComponent>(boat, out var body))
            return;

        var steeringInput = GetSteeringInput(helmComponent);
        if (MathF.Abs(steeringInput) < 0.001f)
        {
            if (MathF.Abs(body.AngularVelocity) < 0.001f)
                return;

            var weight = GetShipWeight(helmComponent, grid);
            var weightDivider = 1f + weight * 0.01f;
            StabilizeShipRotation(boat, helmComponent, steeringPower, weightDivider, body);
            return;
        }

        var shipWeight = GetShipWeight(helmComponent, grid);
        var shipWeightDivider = 1f + shipWeight * 0.01f;
        var angularImpulse = steeringInput * helmComponent.MinMotionFactor * steeringPower * helmComponent.TurnImpulseScalar / shipWeightDivider;
        if (!float.IsFinite(angularImpulse))
            return;

        _physics.ApplyAngularImpulse(boat, angularImpulse, body: body);
    }

    private void StabilizeShipRotation(
        EntityUid boat,
        HelmComponent helmComponent,
        float steeringPower,
        float weightDivider,
        PhysicsComponent body)
    {
        var angularVelocity = body.AngularVelocity;
        if (body.InvI <= 0f)
            return;

        var stabilizingImpulseMagnitude = helmComponent.MinMotionFactor * steeringPower * helmComponent.StabilizingImpulseScalar / weightDivider;
        if (!float.IsFinite(stabilizingImpulseMagnitude) || stabilizingImpulseMagnitude <= 0f)
            return;

        var desiredImpulse = -MathF.Sign(angularVelocity) * stabilizingImpulseMagnitude;
        var stopImpulse = -angularVelocity / body.InvI;
        var stopNow = MathF.Abs(desiredImpulse) >= MathF.Abs(stopImpulse);
        var angularImpulse = stopNow ? stopImpulse : desiredImpulse;

        _physics.ApplyAngularImpulse(boat, angularImpulse, body: body);

        if (stopNow)
            _physics.SetAngularVelocity(boat, 0f, body: body);
    }

    private bool TryGetShipLoad(EntityUid helmUid, out float weight, out float overloadCeil)
    {
        if (!_shipGrid.TryGetHelmGrid(helmUid, out var gridUid, out var grid))
        {
            weight = 0f;
            overloadCeil = 0f;
            return false;
        }

        weight = grid.TotalWeight;
        overloadCeil = _shipGrid.GetMaxWeight(gridUid, grid);
        return true;
    }

    private static float GetSteeringInput(HelmComponent helmComponent)
    {
        var diffDegrees = helmComponent.HelmRotation;
        var maxTurnAngle = float.IsFinite(helmComponent.SteeringAngleForMaxTurn)
            ? MathF.Max(1f, MathF.Abs(helmComponent.SteeringAngleForMaxTurn))
            : 45f;
        return Math.Clamp(-diffDegrees / maxTurnAngle, -1f, 1f);
    }

    private float GetShipWeight(HelmComponent helm, ShipGridComponent grid)
    {
        var minWeight = float.IsFinite(helm.MinShipWeight) ? MathF.Max(0f, helm.MinShipWeight) : 0f;
        var totalWeight = float.IsFinite(grid.TotalWeight) ? MathF.Max(0f, grid.TotalWeight) : 0f;
        return MathF.Max(minWeight, totalWeight);
    }

    private TimeSpan GetUpdateDelay()
    {
        var seconds = _cfg.GetCVar(ShipsCCVars.WindDelay);
        return TimeSpan.FromSeconds(float.IsFinite(seconds) ? MathF.Max(0.1f, seconds) : 1f);
    }

    private static string FormatEfficiency(float value)
    {
        return value.ToString("0.##");
    }

    private static string FormatWeight(float value)
    {
        return value.ToString("0.##");
    }

    private static float NormalizeHelmRotation(float helmRotation)
    {
        if (!float.IsFinite(helmRotation))
            return 0f;

        helmRotation %= 360f;
        if (helmRotation > 180f)
            helmRotation -= 360f;

        if (helmRotation < -180f)
            helmRotation += 360f;

        return helmRotation;
    }

    private sealed class PilotState(EntityUid helm, TimeSpan lastRotationUpdate)
    {
        public readonly EntityUid Helm = helm;
        public EntityUid? UsingSound;
        public TimeSpan LastRotationUpdate = lastRotationUpdate;
        public float RotationBudget;
    }
}
