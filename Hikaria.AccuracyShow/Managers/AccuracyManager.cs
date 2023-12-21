using GTFO.API;
using SNetwork;
using static Hikaria.AccuracyShow.Features.AccuracyShower;
using static Hikaria.AccuracyShow.Handlers.AccuracyUpdater;

namespace Hikaria.AccuracyShow.Patches;

public static class AccuracyManager
{
    public static void Setup()
    {
        NetworkAPI.RegisterEvent<pAccuracyData>(typeof(pAccuracyData).FullName, ReceiveAccuracyData);
    }

    public static void ReceiveAccuracyData(ulong senderID, pAccuracyData data)
    {
        PlayersHasAccShower.Add(senderID);
        if (Instance != null && data.m_player.TryGetPlayer(out var player) && !player.IsLocal)
        {
            Instance.UpdateAccuracyData(data);
        }
    }

    public static void SendAccuracyData(AccuracyData data)
    {
        NetworkAPI.InvokeEvent(typeof(pAccuracyData).FullName, data.GetAccuracyData(), SNet_ChannelType.GameNonCritical);
    }

    public static void OnSessionMemberChanged(SNet_Player player, SessionMemberEvent playerEvent)
    {
        if (playerEvent == SessionMemberEvent.JoinSessionHub)
        {
            RegisterPlayer(player);
        }
        else if (playerEvent == SessionMemberEvent.LeftSessionHub)
        {
            if (player.IsLocal)
            {
                UnregisterAllPlayers();
            }
            else
            {
                UnregisterPlayer(player);
            }
        }
    }

    public static bool IsPlayerHasAccShower(ulong lookup)
    {
        return PlayersHasAccShower.Contains(lookup);
    }

    private static HashSet<ulong> PlayersHasAccShower = new();

    public struct pRequestListenAccuracy
    {
        public pRequestListenAccuracy(SNet_Player player)
        {
            m_player = new();
            m_player.SetPlayer(player);
        }

        public SNetStructs.pPlayer m_player;
    }

    public struct pAccuracyData
    {
        public pAccuracyData(SNet_Player player, ulong hitted, ulong shotted, ulong weakspotHitted)
        {
            m_player = new();
            m_player.SetPlayer(player);
            m_Hitted = hitted;
            m_Shotted = shotted;
            m_WeakspotHitted = weakspotHitted;
        }

        public SNetStructs.pPlayer m_player;

        public ulong m_Hitted;

        public ulong m_Shotted;

        public ulong m_WeakspotHitted;
    }
}
