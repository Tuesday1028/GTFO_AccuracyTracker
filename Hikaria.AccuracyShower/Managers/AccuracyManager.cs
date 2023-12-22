using GTFO.API;
using Hikaria.AccuracyShower.Handlers;
using SNetwork;
using static Hikaria.AccuracyShower.Features.AccuracyShower;
using static Hikaria.AccuracyShower.Handlers.AccuracyUpdater;

namespace Hikaria.AccuracyShower.Managers;

public static class AccuracyManager
{
    public static void Setup()
    {
        NetworkAPI.RegisterEvent<pAccuracyData>(typeof(pAccuracyData).FullName, ReceiveAccuracyData);
        NetworkAPI.RegisterEvent<pBroadcastListenAccuracyData>(typeof(pBroadcastListenAccuracyData).FullName, ReceiveBroadcastListenAccuracyData);
    }

    public static void ReceiveBroadcastListenAccuracyData(ulong senderID, pBroadcastListenAccuracyData data)
    {
        if (SNet.Core.TryGetPlayer(senderID, out var player, true))
        {
            AccuracyDataListeners.TryAdd(player.Lookup, player);
        }
    }

    public static void ReceiveAccuracyData(ulong senderID, pAccuracyData data)
    {
        if (Instance != null && data.m_player.TryGetPlayer(out var player) && !player.IsLocal)
        {
            Instance.UpdateAccuracyData(data);
        }
    }

    public static void SendAccuracyData(AccuracyData data)
    {
        NetworkAPI.InvokeEvent(typeof(pAccuracyData).FullName, data.GetAccuracyData(), AccuracyDataListeners.Values.ToList(), SNet_ChannelType.GameNonCritical);
    }

    public static void BroadcastAccuracyDataListener()
    {
        NetworkAPI.InvokeEvent(typeof(pBroadcastListenAccuracyData).FullName, new pBroadcastListenAccuracyData(), SNet_ChannelType.GameNonCritical);
    }

    public static void OnSessionMemberChanged(SNet_Player player, SessionMemberEvent playerEvent)
    {
        if (playerEvent == SessionMemberEvent.JoinSessionHub)
        {
            LobbyPlayers.TryAdd(player.Lookup, player);
            if (player.IsLocal)
            {
                AccuracyDataListeners.TryAdd(player.Lookup, player);
            }
            AccuracyUpdater.RegisterPlayer(player);
        }
        else if (playerEvent == SessionMemberEvent.LeftSessionHub)
        {
            LobbyPlayers.Remove(player.Lookup);
            if (player.IsLocal)
            {
                AccuracyDataListeners.Clear();
                AccuracyUpdater.UnregisterAllPlayers();
            }
            else
            {
                AccuracyDataListeners.Remove(player.Lookup);
                AccuracyUpdater.UnregisterPlayer(player.Lookup);
            }

        }
    }

    public static bool IsAccuracyListener(ulong lookup)
    {
        return AccuracyDataListeners.ContainsKey(lookup);
    }

    public static bool IsLobbyPlayer(ulong lookup)
    {
        return LobbyPlayers.ContainsKey(lookup);
    }

    public static bool IsMasterHasAcc => AccuracyDataListeners.Any(p => p.Key == SNet.Master.Lookup);

    private static Dictionary<ulong, SNet_Player> AccuracyDataListeners { get; set; } = new();

    public static Dictionary<ulong, SNet_Player> LobbyPlayers { get; private set; } = new();

    public struct pBroadcastListenAccuracyData
    {
    }

    public struct pAccuracyData
    {
        public pAccuracyData(SNet_Player player, uint hitted, uint shotted, uint weakspotHitted)
        {
            m_player = new();
            m_player.SetPlayer(player);
            m_Hitted = hitted;
            m_Shotted = shotted;
            m_WeakspotHitted = weakspotHitted;
        }

        public SNetStructs.pPlayer m_player;

        public uint m_Hitted;

        public uint m_Shotted;

        public uint m_WeakspotHitted;
    }
}
