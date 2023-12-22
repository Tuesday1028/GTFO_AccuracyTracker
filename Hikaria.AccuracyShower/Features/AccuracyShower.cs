using CellMenu;
using Gear;
using Hikaria.AccuracyShower.Handlers;
using Hikaria.AccuracyShower.Managers;
using Player;
using SNetwork;
using TheArchive.Core.Attributes;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Loader;
using UnityEngine;

namespace Hikaria.AccuracyShower.Features;

[HideInModSettings]
[EnableFeatureByDefault]
[DisallowInGameToggle]
[DoNotSaveToConfig]
public class AccuracyShower : Feature
{
    public override string Name => "AccuracyShower";

    public override string Group => FeatureGroups.GetOrCreate("AccuracyShower");

    public override void OnGameStateChanged(int state)
    {
        if (state == (int)eGameStateName.Lobby)
        {
            AccuracyUpdater.DoClear();
        }
    }

    public override void Init()
    {
        LoaderWrapper.ClassInjector.RegisterTypeInIl2Cpp<AccuracyUpdater>();
        AccuracyManager.Setup();
    }

    #region SetupAccuracyShower
    public static bool IsSetup { get; private set; }

    [ArchivePatch(typeof(CM_PageRundown_New), nameof(CM_PageRundown_New.Setup))]
    public class CM_PageRundown_New__Setup__Postfix
    {
        private static void Postfix()
        {
            if (!IsSetup)
            {
                GameObject gameObject = new("AccuracyShower");
                GameObject.DontDestroyOnLoad(gameObject);
                if (gameObject.GetComponent<AccuracyUpdater>() == null)
                {
                    gameObject.AddComponent<AccuracyUpdater>();
                }
                IsSetup = true;
            }
        }
    }
    #endregion

    #region HookOnSessionMemberChanged
    [ArchivePatch(typeof(SNet_GlobalManager), nameof(SNet_GlobalManager.Setup))]
    private class SNet_GlobalManager__Setup__Patch
    {
        private static void Postfix()
        {
            SNet_Events.OnMasterChanged += new Action(OnMasterChanged);
            SNet_Events.OnPlayerEvent += new Action<SNet_Player, SNet_PlayerEvent, SNet_PlayerEventReason>(OnPlayerEvent);
        }
    }

    private static void OnPlayerEvent(SNet_Player player, SNet_PlayerEvent playerEvent, SNet_PlayerEventReason reason)
    {
        switch (playerEvent)
        {
            case SNet_PlayerEvent.PlayerLeftSessionHub:
            case SNet_PlayerEvent.PlayerAgentDeSpawned:
                AccuracyManager.OnSessionMemberChanged(player, SessionMemberEvent.LeftSessionHub);
                break;
            case SNet_PlayerEvent.PlayerAgentSpawned:
                AccuracyManager.OnSessionMemberChanged(player, SessionMemberEvent.JoinSessionHub);
                break;
        }
    }

    private static void OnMasterChanged()
    {
        AccuracyUpdater.OnMasterChanged();
    }

    public enum SessionMemberEvent
    {
        JoinSessionHub,
        LeftSessionHub
    }
    #endregion

    #region FetchWeaponFire

    // 只有主机才需要获取炮台是否开火
    public static bool IsSentryGunFire { get; private set; }

    [ArchivePatch(typeof(SentryGunInstance_Firing_Bullets), nameof(SentryGunInstance_Firing_Bullets.FireBullet))]
    private class SentryGunInstance_Firing_Bullets__FireBullet__Patch
    {
        private static void Prefix()
        {
            IsSentryGunFire = true;
        }

        private static void Postfix()
        {
            IsSentryGunFire = false;
        }
    }

    [ArchivePatch(typeof(SentryGunInstance_Firing_Bullets), nameof(SentryGunInstance_Firing_Bullets.UpdateFireShotgunSemi))]
    private class SentryGunInstance_Firing_Bullets__UpdateFireShotgunSemi__Patch
    {
        private static void Prefix()
        {
            IsSentryGunFire = true;
        }

        private static void Postfix()
        {
            IsSentryGunFire = false;
        }
    }
    private static Dictionary<ulong, InventorySlot> LastWieldValidSlot { get; set; } = new();

    [ArchivePatch(typeof(PlayerSync), nameof(PlayerSync.SyncInventoryStatus))]
    private class PlayerSync__SyncInventoryStatus__Patch
    {
        private static void Prefix(PlayerSync __instance)
        {
            var player = __instance.m_agent.Owner;
            if (player.IsLocal || !SNet.IsMaster || player.IsBot)
            {
                return;
            }
            if (AccuracyManager.IsAccuracyListener(player.Lookup))
            {
                return;
            }
            var slot = __instance.GetWieldedSlot();
            if (slot == InventorySlot.GearStandard || slot == InventorySlot.GearSpecial)
            {
                LastWieldValidSlot[player.Lookup] = slot;
            }
        }
    }

    // 用于解决延迟问题导致的开火次数错误，非完美解决方法，针对主机时其他玩家
    [ArchivePatch(typeof(PlayerInventorySynced), nameof(PlayerInventorySynced.GetSync))]
    private class PlayerInventorySynced__GetSync__Patch
    {
        private static void Prefix(PlayerInventorySynced __instance)
        {
            if (__instance.Owner == null)
            {
                return;
            }
            var player = __instance.Owner.Owner;
            if (!SNet.IsMaster || player.IsBot || player.IsLocal)
            {
                return;
            }
            if (AccuracyManager.IsAccuracyListener(player.Lookup))
            {
                return;
            }
            var wieldSlot = __instance.Owner.Inventory.WieldedSlot;
            if (wieldSlot == InventorySlot.GearClass)
            {
                return;
            }
            uint count = (uint)__instance.Owner.Sync.FireCountSync;
            if (__instance.WieldedItem != null && (wieldSlot == InventorySlot.GearStandard || wieldSlot == InventorySlot.GearSpecial))
            {
                var bulletWeapon = __instance.WieldedItem.TryCast<BulletWeaponSynced>();
                if (bulletWeapon != null)
                {
                    var shotGun = bulletWeapon.TryCast<ShotgunSynced>();
                    if (shotGun != null && shotGun.ArchetypeData != null)
                    {
                        count *= (uint)shotGun.ArchetypeData.ShotgunBulletCount;
                    }
                }
            }
            else if (LastWieldValidSlot.TryGetValue(player.Lookup, out var slot))
            {
                if (!PlayerBackpackManager.TryGetBackpack(player, out var backpack) || !backpack.TryGetBackpackItem(slot, out var backpackItem))
                {
                    return;
                }
                var bulletWeapon = backpackItem.Instance.TryCast<BulletWeaponSynced>();
                if (bulletWeapon != null)
                {
                    var shotGun = bulletWeapon.TryCast<ShotgunSynced>();
                    if (shotGun != null && shotGun.ArchetypeData != null)
                    {
                        count *= (uint)shotGun.ArchetypeData.ShotgunBulletCount;
                    }
                }
                else
                {
                    Logs.LogError("Not BulletWeapon but fire bullets?");
                }
            }
            AccuracyUpdater.AddShotted(player.Lookup, count);
            AccuracyUpdater.MarkAccuracyDataNeedUpdate(player.Lookup);
        }
    }

    // 做主机时使用该方法
    [ArchivePatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveBulletDamage))]
    private class Dam_EnemyDamageBase__ReceiveBulletDamage__Patch
    {
        private static void Postfix(Dam_EnemyDamageBase __instance, pBulletDamageData data)
        {
            if (IsSentryGunFire)
                return;
            if (data.source.TryGet(out var agent))
            {
                var playerAgent = agent.TryCast<PlayerAgent>();
                if (playerAgent == null)
                {
                    return;
                }
                var player = playerAgent.Owner;
                if (AccuracyManager.IsAccuracyListener(player.Lookup) || player.IsLocal || player.IsBot)
                {
                    return;
                }
                if (__instance.DamageLimbs[data.limbID].m_type == eLimbDamageType.Weakspot)
                {
                    AccuracyUpdater.AddWeakspotHitted(player.Lookup, 1);
                }
                AccuracyUpdater.AddHitted(player.Lookup, 1);
                AccuracyUpdater.MarkAccuracyDataNeedUpdate(player.Lookup);
            }
        }
    }

    private static bool IsWeaponOwner(BulletWeapon weapon)
    {
        if (weapon == null || weapon.Owner == null || weapon.Owner.Owner == null)
        {
            return false;
        }
        return weapon.Owner.Owner.IsLocal;
    }

    private static uint HitCount;

    private static int BulletPiercingLimit;

    private static int BulletsCountPerFire;

    private static int BulletHitCalledCount;

    private static bool IsPiercingBullet;

    private static bool IsInWeaponFire;

    private static bool CanCalc;

    [ArchivePatch(typeof(BulletWeaponSynced), nameof(BulletWeaponSynced.Fire))]
    private class BulletWeaponSynced__Fire__Patch
    {
        private static void Prefix(BulletWeaponSynced __instance)
        {
            if (!__instance.Owner.Owner.IsBot || !SNet.IsMaster)
            {
                return;
            }
            CanCalc = true;
            IsInWeaponFire = true;
            IsPiercingBullet = false;
            BulletPiercingLimit = 1;
            HitCount = 0;
            BulletHitCalledCount = 0;
            BulletsCountPerFire = 1;
            var data = __instance.ArchetypeData;
            if (data != null)
            {
                IsPiercingBullet = data.PiercingBullets;
                BulletPiercingLimit = data.PiercingBullets ? data.PiercingDamageCountLimit : 0;
            }
        }
        private static void Postfix(BulletWeapon __instance)
        {
            if (!__instance.Owner.Owner.IsBot || !SNet.IsMaster)
            {
                return;
            }
            CanCalc = false;
            IsInWeaponFire = false;
            var player = __instance.Owner.Owner;
            AccuracyUpdater.AddShotted(player.Lookup, (uint)BulletsCountPerFire);
            AccuracyUpdater.AddHitted(player.Lookup, HitCount);
            AccuracyUpdater.MarkAccuracyDataNeedUpdate(player.Lookup);
        }
    }

    [ArchivePatch(typeof(ShotgunSynced), nameof(ShotgunSynced.Fire))]
    private class ShotgunSynced__Fire__Patch
    {
        private static void Prefix(Shotgun __instance)
        {
            if (!__instance.Owner.Owner.IsBot || !SNet.IsMaster)
            {
                return;
            }
            CanCalc = true;
            IsInWeaponFire = true;
            IsPiercingBullet = false;
            BulletPiercingLimit = 1;
            HitCount = 0;
            BulletHitCalledCount = 0;
            BulletsCountPerFire = 1;
            var data = __instance.ArchetypeData;
            if (data != null)
            {
                IsPiercingBullet = data.PiercingBullets;
                BulletPiercingLimit = data.PiercingBullets ? data.PiercingDamageCountLimit : 0;
                BulletsCountPerFire = data.ShotgunBulletCount;
            }
        }
        private static void Postfix(BulletWeapon __instance)
        {
            if (!__instance.Owner.Owner.IsBot || !SNet.IsMaster)
            {
                return;
            }
            CanCalc = false;
            IsInWeaponFire = false;
            var player = __instance.Owner.Owner;
            AccuracyUpdater.AddShotted(player.Lookup, (uint)BulletsCountPerFire);
            AccuracyUpdater.AddHitted(player.Lookup, HitCount);
            AccuracyUpdater.MarkAccuracyDataNeedUpdate(player.Lookup);
        }
    }

    [ArchivePatch(typeof(BulletWeapon), nameof(BulletWeapon.Fire))]
    private class BulletWeapon__Fire__Patch
    {
        private static void Prefix(BulletWeapon __instance)
        {
            if (!IsWeaponOwner(__instance))
            {
                return;
            }
            CanCalc = true;
            IsInWeaponFire = true;
            IsPiercingBullet = false;
            BulletPiercingLimit = 1;
            HitCount = 0;
            BulletHitCalledCount = 0;
            BulletsCountPerFire = 1;
            var data = __instance.ArchetypeData;
            if (data != null)
            {
                IsPiercingBullet = data.PiercingBullets;
                BulletPiercingLimit = data.PiercingBullets ? data.PiercingDamageCountLimit : 0;
            }
        }

        private static void Postfix(BulletWeapon __instance)
        {
            if (!IsWeaponOwner(__instance))
            {
                return;
            }
            CanCalc = false;
            IsInWeaponFire = false;
            var player = __instance.Owner.Owner;
            AccuracyUpdater.AddShotted(player.Lookup, (uint)BulletsCountPerFire);
            AccuracyUpdater.AddHitted(player.Lookup, HitCount);
            AccuracyUpdater.MarkAccuracyDataNeedUpdate(player.Lookup);
        }
    }

    [ArchivePatch(typeof(Shotgun), nameof(Shotgun.Fire))]
    private class Shotgun__Fire__Patch
    {
        private static void Prefix(Shotgun __instance)
        {
            if (!IsWeaponOwner(__instance))
            {
                return;
            }
            CanCalc = true;
            IsInWeaponFire = true;
            IsPiercingBullet = false;
            BulletPiercingLimit = 1;
            HitCount = 0;
            BulletHitCalledCount = 0;
            BulletsCountPerFire = 1;
            var data = __instance.ArchetypeData;
            if (data != null)
            {
                IsPiercingBullet = data.PiercingBullets;
                BulletPiercingLimit = data.PiercingBullets ? data.PiercingDamageCountLimit : 0;
                BulletsCountPerFire = data.ShotgunBulletCount;
            }
        }

        private static void Postfix(Shotgun __instance)
        {
            if (!IsWeaponOwner(__instance))
            {
                return;
            }
            CanCalc = false;
            IsInWeaponFire = false;
            var player = __instance.Owner.Owner;
            AccuracyUpdater.AddShotted(player.Lookup, (uint)BulletsCountPerFire);
            AccuracyUpdater.AddHitted(player.Lookup, HitCount);
            AccuracyUpdater.MarkAccuracyDataNeedUpdate(player.Lookup);
        }
    }

    [ArchivePatch(typeof(BulletWeapon), nameof(BulletWeapon.BulletHit))]
    private class BulletWeapon__BulletHit__Patch
    {
        private static bool CanCalcHitted;
        private static void Postfix(bool __result)
        {
            if (!IsInWeaponFire || IsSentryGunFire || !CanCalc)
            {
                return;
            }
            if (BulletHitCalledCount >= BulletsCountPerFire)
            {
                return;
            }
            if (IsPiercingBullet)
            {
                if (BulletHitCalledCount % BulletPiercingLimit == 0)
                {
                    CanCalcHitted = true;
                }
            }
            else
            {
                CanCalcHitted = true;
            }
            BulletHitCalledCount++;
            if (__result && CanCalcHitted)
            {
                HitCount++;
                CanCalcHitted = false;
            }
        }
    }
    #endregion
}
