using CellMenu;
using Gear;
using Hikaria.AccuracyShow.Handlers;
using Hikaria.AccuracyShow.Patches;
using Player;
using SNetwork;
using TheArchive.Core.Attributes;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Loader;
using UnityEngine;
using static Il2CppSystem.Globalization.CultureInfo;

namespace Hikaria.AccuracyShow.Features;

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
    [ArchivePatch(typeof(SNet_SessionHub), nameof(SNet_SessionHub.AddPlayerToSession))]
    private class SNet_SessionHub__AddPlayerToSession__Patch
    {
        private static void Postfix(SNet_Player player)
        {
            if (player == null)
            {
                return;
            }
            OnSessionMemberChanged(player, SessionMemberEvent.JoinSessionHub);
        }
    }

    [ArchivePatch(typeof(SNet_SessionHub), nameof(SNet_SessionHub.RemovePlayerFromSession))]
    private class SNet_SessionHub__RemovePlayerFromSession__Patch
    {
        private static void Prefix(SNet_Player player)
        {
            if (player == null)
            {
                return;
            }
            OnSessionMemberChanged(player, SessionMemberEvent.LeftSessionHub);
        }
    }

    [ArchivePatch(typeof(SNet_SessionHub), nameof(SNet_SessionHub.OnSessionMemberChange))]
    private class SNet_SessionHub__OnSessionMemberChange__Patch
    {
        private static readonly List<SessionMemberChangeType> leftTypes = new()
        {
            SessionMemberChangeType.Kicked,
            SessionMemberChangeType.Banned,
            SessionMemberChangeType.Left,
            SessionMemberChangeType.TimeOut
        };

        private static void Prefix(pSessionMemberStateChange data)
        {
            if (!data.player.TryGetPlayer(out var player) || SNet.IsMaster)
            {
                return;
            }
            if (leftTypes.Contains(data.type))
            {
                OnSessionMemberChanged(player, SessionMemberEvent.LeftSessionHub);
            }
        }

        private static void Postfix(pSessionMemberStateChange data)
        {
            if (!data.player.TryGetPlayer(out var player) || SNet.IsMaster)
            {
                return;
            }
            if (data.type == SessionMemberChangeType.Joined)
            {
                OnSessionMemberChanged(player, SessionMemberEvent.JoinSessionHub);
            }
        }
    }

    [ArchivePatch(typeof(GS_Lobby), nameof(GS_Lobby.OnPlayerEvent))]
    private class GS_Lobby__OnPlayerEvent__Patch
    {
        private static void Prefix(SNet_Player player, SNet_PlayerEvent playerEvent)
        {
            if (player == null || SNet.IsMaster)
            {
                return;
            }
            if (playerEvent == SNet_PlayerEvent.PlayerAgentDeSpawned || playerEvent == SNet_PlayerEvent.PlayerLeftSessionHub)
            {
                OnSessionMemberChanged(player, SessionMemberEvent.LeftSessionHub);
            }
        }

        private static void Postfix(SNet_Player player, SNet_PlayerEvent playerEvent)
        {
            if (player == null || SNet.IsMaster)
            {
                return;
            }
            if (playerEvent == SNet_PlayerEvent.PlayerAgentSpawned || playerEvent == SNet_PlayerEvent.PlayerIsSynced)
            {
                OnSessionMemberChanged(player, SessionMemberEvent.JoinSessionHub);
            }
        }
    }

    [ArchivePatch(typeof(SNet_SessionHub), nameof(SNet_SessionHub.LeaveHub))]
    private class SNet_SessionHub__LeaveHub__Patch
    {
        private static void Prefix()
        {
            OnSessionMemberChanged(SNet.LocalPlayer, SessionMemberEvent.LeftSessionHub);
        }
    }

    private static void OnSessionMemberChanged(SNet_Player player, SessionMemberEvent playerEvent)
    {
        try
        {
            AccuracyManager.OnSessionMemberChanged(player, playerEvent);
        }
        catch
        {
        }
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

    public static bool IsBotFiring { get; private set; }

    private static SNet_Player BotUsingFirearm { get; set; }

    [ArchivePatch(typeof(BulletWeaponSynced), nameof(BulletWeaponSynced.Fire))]
    private class BulletWeaponSynced__Fire__Patch
    {
        private static void Prefix(BulletWeaponSynced __instance)
        {
            var player = __instance.Owner.Owner;
            if (SNet.IsMaster && player.IsBot)
            {
                IsBotFiring = true;
                BotUsingFirearm = player;
            }
        }

        private static void Postfix(BulletWeaponSynced __instance)
        {
            var player = __instance.Owner.Owner;
            if (SNet.IsMaster && player.IsBot)
            {
                AccuracyUpdater.AddShotted(BotUsingFirearm.Lookup, 1);
                AccuracyUpdater.MarkAccuracyDataNeedUpdate(BotUsingFirearm.Lookup);
                IsBotFiring = false;
                BotUsingFirearm = null;
            }
        }
    }

    [ArchivePatch(typeof(ShotgunSynced), nameof(ShotgunSynced.Fire))]
    private class ShotgunSynced__Fire__Patch
    {
        private static void Prefix(ShotgunSynced __instance)
        {
            var player = __instance.Owner.Owner;
            if (SNet.IsMaster && player.IsBot)
            {
                IsBotFiring = true;
                BotUsingFirearm = player;
            }
        }

        private static void Postfix(ShotgunSynced __instance)
        {
            var player = __instance.Owner.Owner;
            if (SNet.IsMaster && player.IsBot)
            {
                AccuracyUpdater.AddShotted(BotUsingFirearm.Lookup, 1);
                AccuracyUpdater.MarkAccuracyDataNeedUpdate(BotUsingFirearm.Lookup);
                IsBotFiring = false;
                BotUsingFirearm = null;
            }
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
            var slot = __instance.GetWieldedSlot();
            if (slot == InventorySlot.GearStandard || slot == InventorySlot.GearSpecial)
            {
                LastWieldValidSlot[player.Lookup] = slot;
            }
        }
    }

    // 用于解决延迟问题导致的开火次数错误，非完美解决方法
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
            if (AccuracyManager.IsPlayerHasAccShower(player.Lookup))
            {
                return;
            }
            var wieldSlot = __instance.Owner.Inventory.WieldedSlot;
            ulong count = (ulong)__instance.Owner.Sync.FireCountSync;
            if (__instance.WieldedItem != null && (wieldSlot == InventorySlot.GearStandard || wieldSlot == InventorySlot.GearSpecial))
            {
                var bulletWeapon = __instance.WieldedItem.TryCast<BulletWeaponSynced>();
                if (bulletWeapon != null)
                {
                    var shotGun = bulletWeapon.TryCast<ShotgunSynced>();
                    if (shotGun != null && shotGun.ArchetypeData != null)
                    {
                        count *= (ulong)shotGun.ArchetypeData.ShotgunBulletCount;
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
                        count *= (ulong)shotGun.ArchetypeData.ShotgunBulletCount;
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
                if (AccuracyManager.IsPlayerHasAccShower(player.Lookup))
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

    // 自身或者做主机时的Bot使用该方法获取命中次数
    [ArchivePatch(typeof(Dam_EnemyDamageLimb), nameof(Dam_EnemyDamageLimb.BulletDamage))]
    private class Dam_EnemyDamageLimb__BulletDamage__Patch
    {
        private static void Postfix(Dam_EnemyDamageLimb __instance)
        {
            if (IsSentryGunFire || SNet.IsMaster)
                return;
            SNet_Player player = SNet.LocalPlayer;
            if (__instance.m_type == eLimbDamageType.Weakspot)
            {
                AccuracyUpdater.AddWeakspotHitted(player.Lookup, 1);
            }
            AccuracyUpdater.AddHitted(player.Lookup, 1);
            AccuracyUpdater.MarkAccuracyDataNeedUpdate(player.Lookup);
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

    [ArchivePatch(typeof(BulletWeapon), nameof(BulletWeapon.Fire))]
    private class BulletWeapon__Fire__Patch
    {
        private static void Postfix(BulletWeapon __instance)
        {
            if (!IsWeaponOwner(__instance))
            {
                return;
            }
            AccuracyUpdater.AddShotted(SNet.LocalPlayer.Lookup, 1);
            AccuracyUpdater.MarkAccuracyDataNeedUpdate(SNet.LocalPlayer.Lookup);
        }
    }

    [ArchivePatch(typeof(Shotgun), nameof(Shotgun.Fire))]
    private class Shotgun__Fire__Patch
    {
        private static void Postfix(Shotgun __instance)
        {
            if (!IsWeaponOwner(__instance))
            {
                return;
            }
            AccuracyUpdater.AddShotted(SNet.LocalPlayer.Lookup, (ulong)__instance.ArchetypeData.ShotgunBulletCount);
            AccuracyUpdater.MarkAccuracyDataNeedUpdate(SNet.LocalPlayer.Lookup);
        }
    }
    #endregion
}
