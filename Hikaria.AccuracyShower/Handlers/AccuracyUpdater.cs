using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils;
using Player;
using SNetwork;
using System.Collections;
using TMPro;
using UnityEngine;
using static Hikaria.AccuracyShower.Managers.AccuracyManager;

namespace Hikaria.AccuracyShower.Handlers;

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
                    gameObject.transform.localPosition = new Vector3(-70f, -62 + -35 * (i + offset), 0f);
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

    public static void OnMasterChanged()
    {
        if (!SNet.IsMaster)
        {
            foreach (var lookup in AccuracyDataLookup.Keys.ToList())
            {
                if (!IsAccuracyListener(lookup) && AccuracyRegisteredCharacterIndex.TryGetValue(lookup, out var index))
                {
                    SetVisible(index, false);
                }
            }
        }
        else
        {
            foreach (var pair in AccuracyRegisteredCharacterIndex)
            {
                SetVisible(pair.Value, true);
            }
        }
    }

    public static void RegisterPlayer(SNet_Player player)
    {
        if (RegisterPlayerCoroutines.ContainsKey(player.Lookup))
        {
            Instance.StopCoroutine(RegisterPlayerCoroutines[player.Lookup]);
        }
        Instance.StartCoroutine(RegisterPlayerCoroutine(player));
    }

    private static IEnumerator RegisterPlayerCoroutine(SNet_Player player)
    {
        var yielder = new WaitForSecondsRealtime(1f);
        while (true)
        {
            if (player.HasCharacterSlot && player.CharacterIndex != -1)
            {
                AccuracyRegisteredCharacterIndex[player.Lookup] = player.CharacterIndex;
                AccuracyDataLookup[player.Lookup] = new(player);
                AccuracyDataNeedUpdate[player.Lookup] = true;
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
            foreach (var data in AccuracyDataLookup.Values.ToList())
            {
                var owner = data.m_Owner;
                if (AccuracyDataNeedUpdate[owner.Lookup] && AccuracyRegisteredCharacterIndex.TryGetValue(owner.Lookup, out var index))
                {
                    UpdateAccuracyData(index, data.GetAccuracyText());
                    AccuracyDataNeedUpdate[owner.Lookup] = false;
                    if (SNet.IsMaster && (owner.IsBot || !IsAccuracyListener(owner.Lookup)) || owner.IsLocal)
                    {
                        SendAccuracyData(data);
                    }
                    if (IsAccuracyListener(owner.Lookup) || owner.IsLocal || (owner.IsBot && IsMasterHasAcc) || IsMasterHasAcc)
                    {
                        if (!AccuracyTextMeshesVisible[index])
                        {
                            SetVisible(index, true);
                        }
                    }
                    else if (AccuracyTextMeshesVisible[index])
                    {
                        SetVisible(index, false);
                    }
                }
            }
            yield return yielder;
        }
    }

    public void UpdateAccuracyData(pAccuracyData data)
    {
        if (!data.m_player.TryGetPlayer(out var player))
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
                AccuracyTextMeshes[i].transform.parent.parent.transform.localPosition = new(-70f, -62f + -35f * (i + offset - preInvisible), 0f);
            }
            else
            {
                AccuracyTextMeshes[i].transform.parent.parent.transform.localPosition = new(-70f, 1000f, 0f);
            }
        }
    }

    public static void UnregisterAllPlayers()
    {
        foreach (var lookup in AccuracyRegisteredCharacterIndex.Keys)
        {
            UnregisterPlayer(lookup);
        }
        UpdateVisible();
    }

    public static void UnregisterPlayer(ulong lookup)
    {
        if (AccuracyRegisteredCharacterIndex.ContainsKey(lookup))
        {
            SetVisible(AccuracyRegisteredCharacterIndex[lookup], false);
        }
        AccuracyDataLookup.Remove(lookup);
        AccuracyDataNeedUpdate.Remove(lookup);
        AccuracyRegisteredCharacterIndex.Remove(lookup);
    }

    public static void AddHitted(ulong lookup, uint count)
    {
        if (AccuracyDataLookup.ContainsKey(lookup))
        {
            AccuracyDataLookup[lookup].AddHitted(count);
        }
    }

    public static void AddShotted(ulong lookup, uint count)
    {
        if (AccuracyDataLookup.ContainsKey(lookup))
        {
            AccuracyDataLookup[lookup].AddShotted(count);
        }
    }

    public static void AddWeakspotHitted(ulong lookup, uint count)
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

    private static Dictionary<ulong, Coroutine> RegisterPlayerCoroutines = new();

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

        public void AddShotted(uint count)
        {
            m_Shotted += count;
        }

        public void AddHitted(uint count)
        {
            m_Hitted += count;
        }

        public void AddWeakspotHitted(uint count)
        {
            m_WeakspotHitted += count;
        }

        public SNet_Player m_Owner;

        public uint m_Hitted;

        public uint m_Shotted;

        public uint m_WeakspotHitted;

        public static readonly string[] ShowNames = { "Red", "Gre", "Blu", "Pur" };

        public string GetAccuracyText()
        {
            if (!m_Owner.HasCharacterSlot)
            {
                return "-%(0/0)";
            }
            string prefix = IsAccuracyListener(m_Owner.Lookup) || (IsMasterHasAcc && m_Owner.IsBot) || m_Owner.IsLocal ? "": "*";
            if (m_Shotted == 0)
            {
                return $"{prefix}{ShowNames[m_Owner.CharacterIndex]}: -%(0/0)";
            }
            else
            {
                return $"{prefix}{ShowNames[m_Owner.CharacterIndex]}: {(int)(100 * m_Hitted / m_Shotted)}%({m_Hitted}/{m_Shotted})";
            }
        }

        public pAccuracyData GetAccuracyData()
        {
            return new(m_Owner, m_Hitted, m_Shotted, m_WeakspotHitted);
        }
    }
}