using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils;
using Player;
using SNetwork;
using System.Collections;
using TMPro;
using UnityEngine;
using static Hikaria.AccuracyShow.Patches.AccuracyManager;

namespace Hikaria.AccuracyShow.Handlers;

public class AccuracyUpdater : MonoBehaviour
{
    public void Awake()
    {
        Instance = this;
        Setup();
        this.StartCoroutine(UpdateAccuracyDataCoroutine());
    }

    private void Setup()
    {
        if (IL2CPPChainloader.Instance.Plugins.ContainsKey("com.catrice.DamageIndicator"))
        {
            offset += 4;
        }

        AccuracyTextMeshesVisible[0] = false;
        AccuracyTextMeshesVisible[1] = false;
        AccuracyTextMeshesVisible[2] = false;
        AccuracyTextMeshesVisible[3] = false;

        PUI_Inventory inventory = GuiManager.Current.m_playerLayer.Inventory;
        foreach (RectTransform rectTransform in inventory.m_iconDisplay.GetComponentsInChildren<RectTransform>(true))
        {
            if (rectTransform.name == "Background Fade")
            {
                TextMeshPro textMeshPro = inventory.m_inventorySlots[InventorySlot.GearMelee].m_slim_archetypeName;
                for (int i = 0; i < 4; i++)
                {
                    GameObject gameObject = Instantiate(rectTransform.gameObject, rectTransform.parent);
                    RectTransform component = gameObject.GetComponent<RectTransform>();
                    gameObject.gameObject.SetActive(true);
                    foreach (Transform transform in gameObject.GetComponentsInChildren<Transform>(true))
                    {
                        if (transform.name == "TimerShowObject")
                        {
                            transform.gameObject.active = false;
                        }
                    }
                    gameObject.transform.localPosition = new Vector3(-70f, -52 + -35 * (i + offset), 0f);
                    AccuracyTextMeshes[i] = Instantiate(textMeshPro);
                    GameObject gameObject2 = new GameObject($"AccuracyShower{i}")
                    {
                        layer = 5,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    gameObject2.transform.SetParent(component.transform, false);
                    AccuracyTextMeshes[i].transform.SetParent(gameObject2.transform, false);
                    AccuracyTextMeshes[i].GetComponent<RectTransform>().anchoredPosition = new(-5f, 9f);
                    AccuracyTextMeshes[i].SetText("-%(0/0)", true);
                    AccuracyTextMeshes[i].ForceMeshUpdate(false, false);
                }
                break;
            }
        }
        UpdateVisible();
    }

    private static IEnumerator WaitingForCharacterSlotSetup(SNet_Player player)
    {
        var yielder = new WaitForSecondsRealtime(1f);
        while (true)
        {
            if (player.HasCharacterSlot && player.CharacterIndex != -1)
            {
                AccuracyRegisteredCharacterIndex[player.Lookup] = player.CharacterIndex;
                AccuracyDataLookup.TryAdd(player.Lookup, new(player));
                AccuracyDataNeedUpdate.TryAdd(player.Lookup, true);
                MarkAccuracyDataNeedUpdate(player.Lookup);
                SetVisible(player.CharacterIndex, true);
                WaitForRegistCoroutine.Remove(player.Lookup);
                yield break;
            }
            yield return yielder;
        }
    }

    private IEnumerator UpdateAccuracyDataCoroutine()
    {
        var yielder = new WaitForSecondsRealtime(3f);
        while (true)
        {
            foreach (var data in AccuracyDataLookup.Values)
            {
                var player = data.m_Owner;
                if (AccuracyDataNeedUpdate[player.Lookup] && AccuracyRegisteredCharacterIndex.TryGetValue(player.Lookup, out var index))
                {
                    UpdateAccuracyData(index, data.GetAccuracyText());
                    if (SNet.IsMaster && player.IsBot || player.IsLocal)
                    {
                        SendAccuracyData(data);
                    }
                    AccuracyDataNeedUpdate[player.Lookup] = false;
                }
            }
            yield return yielder;
        }
    }

    public void UpdateAccuracyData(pAccuracyData data)
    {
        if (!data.m_player.TryGetPlayer(out var player) || !player.HasCharacterSlot)
        {
            return;
        }
        AccuracyDataLookup[player.Lookup].Set(data);
        MarkAccuracyDataNeedUpdate(player.Lookup);
    }

    private void UpdateAccuracyData(int index, string text)
    {
        if (AccuracyTextMeshes.TryGetValue(index, out var textMesh))
        {
            textMesh.SetText(text);
            textMesh.ForceMeshUpdate();
        }
    }

    public static void MarkAccuracyDataNeedUpdate(ulong lookup)
    {
        AccuracyDataNeedUpdate[lookup] = true;
    }

    public static void DoClear()
    {
        foreach (var lookup in AccuracyDataLookup.Keys.ToList())
        {
            var data = AccuracyDataLookup[lookup];
            data.m_Hitted = 0;
            data.m_Shotted = 0;
            data.m_WeakspotHitted = 0;
            MarkAccuracyDataNeedUpdate(lookup);
        }
        foreach (var lookup in AccuracyDataNeedUpdate.Keys.ToList())
        {
            AccuracyDataNeedUpdate[lookup] = false;
        }
    }

    private static void SetVisible(int index, bool visible, bool update = true)
    {
        AccuracyTextMeshesVisible[index] = visible;
        if (update)
        {
            UpdateVisible();
        }
    }

    private static void UpdateVisible()
    {
        for (int i = 0; i < 4; i++)
        {
            int preInvisible = 0;
            for (int j = 0; j < 4; j++)
            {
                if (j <= i && !AccuracyTextMeshesVisible[j])
                {
                    preInvisible++;
                }
            }
            if (AccuracyTextMeshesVisible[i])
            {
                AccuracyTextMeshes[i].transform.parent.parent.transform.localPosition = new(-70f, -52f + -35f * (i + offset - preInvisible), 0f);
            }
            else
            {
                AccuracyTextMeshes[i].transform.parent.parent.transform.localPosition = new(-70f, 1000f, 0f);
            }
        }
    }

    public static void RegisterPlayer(SNet_Player player)
    {
        if (!WaitForRegistCoroutine.ContainsKey(player.Lookup))
        {
            WaitForRegistCoroutine[player.Lookup] = Instance.StartCoroutine(WaitingForCharacterSlotSetup(player));
        }
    }

    public static void UnregisterAllPlayers()
    {
        AccuracyDataLookup.Clear();
        AccuracyDataNeedUpdate.Clear();
        AccuracyRegisteredCharacterIndex.Clear();
        for (int i = 0; i < 4; i++)
        {
            AccuracyTextMeshesVisible[i] = false;
        }
        UpdateVisible();
    }

    public static void UnregisterPlayer(SNet_Player player)
    {
        SetVisible(AccuracyRegisteredCharacterIndex[player.Lookup], false);
        AccuracyDataLookup.Remove(player.Lookup);
        AccuracyDataNeedUpdate.Remove(player.Lookup);
        AccuracyRegisteredCharacterIndex.Remove(player.Lookup);
    }

    public static void AddHitted(ulong lookup, ulong count)
    {
        if (AccuracyDataLookup.ContainsKey(lookup))
        {
            AccuracyDataLookup[lookup].AddHitted(count);
        }
    }

    public static void AddShotted(ulong lookup, ulong count)
    {
        if (AccuracyDataLookup.ContainsKey(lookup))
        {
            AccuracyDataLookup[lookup].AddShotted(count);
        }
    }

    public static void AddWeakspotHitted(ulong lookup, ulong count)
    {
        if (AccuracyDataLookup.ContainsKey(lookup))
        {
            AccuracyDataLookup[lookup].AddWeakspotHitted(count);
        }
    }

    public static AccuracyUpdater Instance { get; private set; }

    private static int offset = 0;

    public static int CurrentSize => AccuracyTextMeshesVisible.Count(p => p.Value == true);

    private static Dictionary<int, TextMeshPro> AccuracyTextMeshes { get; set; } = new();

    private static Dictionary<ulong, AccuracyData> AccuracyDataLookup { get; set; } = new();

    private static Dictionary<ulong, bool> AccuracyDataNeedUpdate { get; set; } = new();

    private static Dictionary<int, bool> AccuracyTextMeshesVisible { get; set; } = new();

    private static Dictionary<ulong, int> AccuracyRegisteredCharacterIndex { get; set; } = new();

    private static Dictionary<ulong, Coroutine> WaitForRegistCoroutine = new();

    public class AccuracyData
    {
        public AccuracyData(pAccuracyData data)
        {
            data.m_player.TryGetPlayer(out var player);
            m_Owner = player;
            m_Hitted = 0;
            m_Shotted = 0;
            m_WeakspotHitted = 0;
        }

        public AccuracyData(SNet_Player player)
        {
            m_Owner = player;
            m_Hitted = 0;
            m_Shotted = 0;
            m_WeakspotHitted = 0;
        }

        public void Set(pAccuracyData data)
        {
            data.m_player.TryGetPlayer(out var player);
            m_Owner = player;
            m_Hitted = data.m_Hitted;
            m_Shotted = data.m_Shotted;
            m_WeakspotHitted = data.m_WeakspotHitted;
        }

        public void AddShotted(ulong count)
        {
            m_Shotted += count;
        }

        public void AddHitted(ulong count)
        {
            m_Hitted += count;
        }

        public void AddWeakspotHitted(ulong count)
        {
            m_WeakspotHitted += count;
        }

        public SNet_Player m_Owner;

        public ulong m_Hitted;

        public ulong m_Shotted;

        public ulong m_WeakspotHitted;

        public static readonly string[] ShowNames = { "Red", "Gre", "Blu", "Pur" };

        public string GetAccuracyText()
        {
            if (!m_Owner.HasCharacterSlot)
            {
                return "-%(0/0)";
            }
            if (m_Shotted == 0)
            {
                return $"{ShowNames[m_Owner.CharacterIndex]}: -%(0/0)";
            }
            else
            {
                return $"{ShowNames[m_Owner.CharacterIndex]}: {(int)(100 * m_Hitted / m_Shotted)}%({m_Hitted}/{m_Shotted})";
            }
        }

        public pAccuracyData GetAccuracyData()
        {
            return new(m_Owner, m_Hitted, m_Shotted, m_WeakspotHitted);
        }
    }
}