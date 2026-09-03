using Content.Shared.Alert;
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Examine;
using Content.Shared.Imperial.Dash;
using Content.Shared.Imperial.Medieval.Sprint;
using Content.Shared.Movement.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Myrmex.Hive;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.Imperial.Medieval.Myrmex
{
    public sealed partial class SharedMyrmexHungerSystem : EntitySystem
    {
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly MovementSpeedModifierSystem _speedModifier = default!;
        [Dependency] private readonly AlertsSystem _alertsSystem = default!;
        [Dependency] private readonly INetManager _net = default!;
        [Dependency] private readonly SharedMyrmexHiveSystem _hive = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<MyrmexHungerComponent, RefreshMovementSpeedModifiersEvent>(OnSpeedRefresh);
            SubscribeLocalEvent<MyrmexHungerComponent, ComponentInit>(OnInit);
            SubscribeLocalEvent<MyrmexHungerComponent, BeforeStaminaDamageEvent>(OnModifyStaminaDamage);
            SubscribeLocalEvent<MyrmexHungerComponent, GetSprintStaminaDamageModifiersEvent>(OnModifySprintStaminaCost);
            SubscribeLocalEvent<MyrmexHungerComponent, CheckDashStaminaCostModifiersEvent>(OnModifyDashStaminaCost);
            SubscribeLocalEvent<MyrmexHungerComponent, GetMeleeDamageEvent>(OnGetDamage);
            SubscribeLocalEvent<MyrmexHungerComponent, GunShotEvent>(OnGunShot);
            SubscribeLocalEvent<MyrmexHungerComponent, DamageModifyEvent>(OnGetDamageModifiers);
            SubscribeLocalEvent<MyrmexHungerComponent, ExaminedEvent>(OnExamined);
        }

        private void OnInit(EntityUid uid, MyrmexHungerComponent comp, ref ComponentInit args)
        {
            if (!_net.IsServer)
                return;

            var initialCooldown = TimeSpan.FromSeconds(comp.EatCooldownSeconds + 1);
            comp.LastEaten = _gameTiming.CurTime - initialCooldown;
            Clamp(uid, comp);
        }

        private void Clamp(EntityUid uid, MyrmexHungerComponent comp)
        {
            if (!_hive.TryEnsureHive(out var hive))
                return;

            var maxBuffs = hive!.Value.Comp.MaxBuffs;

            if (comp.Buffs.Count > maxBuffs)
            {
                comp.Buffs.RemoveRange(maxBuffs, comp.Buffs.Count - maxBuffs);
                Dirty(uid, comp);
            }
        }

        #region Buffs

        private void OnExamined(EntityUid uid, MyrmexHungerComponent comp, ref ExaminedEvent args)
        {
            var buff = MyrmexBuff.MultiplyBuffs(comp.Buffs);
            args.PushMarkup(Loc.GetString("medieval-myrmex-buff-health-examine", ("value", Math.Round(buff.Health, 2))));
            args.PushMarkup(Loc.GetString("medieval-myrmex-buff-damage-examine", ("value", Math.Round(buff.Damage, 2))));
            args.PushMarkup(Loc.GetString("medieval-myrmex-buff-stamina-examine", ("value", Math.Round(buff.Stamina, 2))));
        }

        private void OnModifyStaminaDamage(EntityUid uid, MyrmexHungerComponent comp, ref BeforeStaminaDamageEvent args)
        {
            if (args.Value <= 0f)
                return;

            var buff = MyrmexBuff.MultiplyBuffs(comp.Buffs);
            args.Value *= buff.Stamina;
        }

        private void OnModifySprintStaminaCost(EntityUid uid, MyrmexHungerComponent comp, ref GetSprintStaminaDamageModifiersEvent args)
        {
            var buff = MyrmexBuff.MultiplyBuffs(comp.Buffs);
            args.Modifier *= buff.Stamina;
        }

        private void OnModifyDashStaminaCost(EntityUid uid, MyrmexHungerComponent comp, ref CheckDashStaminaCostModifiersEvent args)
        {
            var buff = MyrmexBuff.MultiplyBuffs(comp.Buffs);
            args.Modifier *= buff.Stamina;
        }

        private void OnGetDamage(EntityUid uid, MyrmexHungerComponent comp, ref GetMeleeDamageEvent args)
        {
            if (!args.RaisedOnUser)
                return;

            var buff = MyrmexBuff.MultiplyBuffs(comp.Buffs);
            var damage = args.Damage * buff.Damage;

            if (args.Damage.DamageDict.TryGetValue("ParryAble", out var parryAble))
                damage.DamageDict["ParryAble"] = parryAble;

            args.Damage = damage;
        }

        private void OnGunShot(EntityUid uid, MyrmexHungerComponent comp, ref GunShotEvent args)
        {
            var buff = MyrmexBuff.MultiplyBuffs(comp.Buffs);

            foreach (var (ammo, _) in args.Ammo)
            {
                if (TryComp<ProjectileComponent>(ammo, out var projectile))
                    projectile.Damage *= buff.Damage;
            }
        }

        private void OnGetDamageModifiers(EntityUid uid, MyrmexHungerComponent comp, ref DamageModifyEvent args)
        {
            var buff = MyrmexBuff.MultiplyBuffs(comp.Buffs);
            args.Damage *= buff.Health;
        }

        #endregion

        private void OnSpeedRefresh(EntityUid uid, MyrmexHungerComponent comp, RefreshMovementSpeedModifiersEvent args)
        {
            var curTime = _gameTiming.CurTime;
            var diff = (curTime - comp.LastEaten);

            if ((diff.HasValue && diff.Value.Duration() > TimeSpan.FromSeconds(comp.SecondsToHungry)))
            {
                _alertsSystem.ShowAlert(uid, "MyrmexHungry");
                args.ModifySpeed(comp.HungrySpeedModifier, comp.HungrySpeedModifier);
            }
            else
            {
                _alertsSystem.ClearAlert(uid, "MyrmexHungry");
            }
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var query = EntityQueryEnumerator<MyrmexHungerComponent>();
            while (query.MoveNext(out var uid, out var hunger))
            {
                _speedModifier.RefreshMovementSpeedModifiers(uid);
            }
        }
    }
}
