using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;
using RPG.Data;
using RPG.Combat;
using System.Text;

namespace RPG.UI
{
    /// <summary>
    /// ItemTooltipUI v4 — CORREÇÕES sobre v3:
    ///
    ///   1. ADICIONADO overload Show(ItemData item) sem anchor — consumido por
    ///      PowerGemUI.OnGemSlotHoverEnter (linha 417). Posiciona o tooltip
    ///      próximo ao cursor do mouse quando nenhum anchor é fornecido.
    ///
    ///   2. CORRIGIDO ambiguidade NetworkPlayer:
    ///      Substituído `NetworkPlayer` por `RPG.Network.NetworkPlayer`
    ///      (totalmente qualificado) porque `using Mirror;` re-exporta
    ///      `UnityEngine.NetworkPlayer` (legacy) causando conflito.
    ///      Também removido `using RPG.Network;` para evitar reintroduzir
    ///      o conflito acidentalmente.
    ///
    ///   3. MANTIDO: Instance singleton, ShowForItem(ItemData, RectTransform),
    ///      Hide, seções de stats / requisitos / skill / consumable.
    /// </summary>
    public class ItemTooltipUI : MonoBehaviour
    {
        public static ItemTooltipUI Instance { get; private set; }

        [Header("Refs principais")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private RectTransform _root;

        [Header("Cabeçalho")]
        [SerializeField] private TMP_Text _itemNameText;
        [SerializeField] private TMP_Text _itemTypeText;
        [SerializeField] private TMP_Text _descriptionText;

        [Header("Seção de Stats (Equipment)")]
        [SerializeField] private GameObject _statsSection;
        [SerializeField] private TMP_Text   _statsText;

        [Header("Seção de Requisitos (Equipment)")]
        [SerializeField] private GameObject _requirementsSection;
        [SerializeField] private TMP_Text   _requirementsText;

        [Header("Seção de Skill (PowerGem)")]
        [SerializeField] private GameObject _skillSection;
        [SerializeField] private TMP_Text   _skillText;

        [Header("Seção de Consumível")]
        [SerializeField] private GameObject _consumableSection;
        [SerializeField] private TMP_Text   _consumableText;

        [Header("Posicionamento")]
        [Tooltip("Offset em pixels do tooltip relativo ao anchor (ou cursor, no overload sem anchor).")]
        [SerializeField] private Vector2 _offset = new Vector2(20f, 0f);

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Hide();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ══════════════════════════════════════════════════════════════════
        // API pública
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Mostra tooltip ancorado em um RectTransform específico (slot, ícone, etc).
        /// Usado pelo InventorySlotUI e EquipmentSlotUI.
        /// </summary>
        public void ShowForItem(ItemData item, RectTransform anchor)
        {
            if (item == null) { Hide(); return; }

            PopulateContent(item);
            PositionByAnchor(anchor);
            ApplyVisibility(true);
        }

        /// <summary>
        /// NOVO v4 — overload simples para callers que não têm um anchor
        /// (PowerGemUI usa esta variante). Posiciona próximo ao cursor.
        /// </summary>
        public void Show(ItemData item)
        {
            if (item == null) { Hide(); return; }

            PopulateContent(item);
            PositionByCursor();
            ApplyVisibility(true);
        }

        public void Hide()
        {
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
            gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════════════════
        // Conteúdo
        // ══════════════════════════════════════════════════════════════════

        private void PopulateContent(ItemData item)
        {
            if (_itemNameText != null)
            {
                _itemNameText.text  = item.DisplayName;
                _itemNameText.color = item.RarityColor;
            }
            if (_itemTypeText != null)
                _itemTypeText.text = $"{item.RarityDisplayName} — {GetTypeDisplay(item)}";

            if (_descriptionText != null)
            {
                _descriptionText.text = item.Description;
                _descriptionText.gameObject.SetActive(!string.IsNullOrEmpty(item.Description));
            }

            ShowStatsSection(item);
            ShowRequirementsSection(item);
            ShowSkillSection(item);
            ShowConsumableSection(item);
        }

        private void ApplyVisibility(bool visible)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha          = visible ? 1f : 0f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable   = false;
            }
            gameObject.SetActive(visible);
        }

        // ── Stats ──────────────────────────────────────────────────────────

        private void ShowStatsSection(ItemData item)
        {
            if (_statsSection == null) return;

            if (!item.IsEquipment || !item.HasAnyBonus())
            {
                _statsSection.SetActive(false);
                return;
            }

            var sb = new StringBuilder(256);
            AppendStatLine(sb, "ATK",  item.BonusATK);
            AppendStatLine(sb, "DEF",  item.BonusDEF);
            AppendStatLine(sb, "MATK", item.BonusMATK);
            AppendStatLine(sb, "MDEF", item.BonusMDEF);
            AppendStatLine(sb, "STR",  item.BonusSTR);
            AppendStatLine(sb, "AGI",  item.BonusAGI);
            AppendStatLine(sb, "VIT",  item.BonusVIT);
            AppendStatLine(sb, "DEX",  item.BonusDEX);
            AppendStatLine(sb, "INT",  item.BonusINT);
            AppendStatLine(sb, "LUK",  item.BonusLUK);
            AppendStatLine(sb, "HP Máx.", item.BonusHP);
            AppendStatLine(sb, "MP Máx.", item.BonusMP);
            AppendResistLine(sb, "Resist. Fogo",   item.BonusResistFire);
            AppendResistLine(sb, "Resist. Gelo",   item.BonusResistIce);
            AppendResistLine(sb, "Resist. Veneno", item.BonusResistPoison);
            AppendResistLine(sb, "Resist. Raio",   item.BonusResistLightning);

            if (item.MaxDurability > 0)
            {
                sb.AppendLine();
                sb.Append($"<color=#AAAAAA>Durabilidade: {item.MaxDurability}/{item.MaxDurability}</color>");
            }

            if (_statsText != null) _statsText.text = sb.ToString().TrimEnd();
            _statsSection.SetActive(sb.Length > 0);
        }

        private static void AppendStatLine(StringBuilder sb, string label, int value)
        {
            if (value == 0) return;
            string sign  = value > 0 ? "+" : "";
            string color = value > 0 ? "#66FF66" : "#FF6666";
            sb.AppendLine($"<color={color}>{sign}{value} {label}</color>");
        }

        private static void AppendStatLine(StringBuilder sb, string label, float value)
        {
            if (Mathf.Approximately(value, 0f)) return;
            string sign  = value > 0f ? "+" : "";
            string color = value > 0f ? "#66FF66" : "#FF6666";
            sb.AppendLine($"<color={color}>{sign}{value:0.#} {label}</color>");
        }

        private static void AppendResistLine(StringBuilder sb, string label, float value)
        {
            if (Mathf.Approximately(value, 0f)) return;
            string sign  = value > 0f ? "+" : "";
            string color = value > 0f ? "#66FFAA" : "#FF6666";
            sb.AppendLine($"<color={color}>{sign}{value:0.#}% {label}</color>");
        }

        // ── Requisitos ─────────────────────────────────────────────────────

        private void ShowRequirementsSection(ItemData item)
        {
            if (_requirementsSection == null) return;

            if (!item.IsEquipment || item.Requirements == null)
            {
                _requirementsSection.SetActive(false);
                return;
            }

            var req = item.Requirements;
            bool hasAny = req.MinLevel > 1
                       || req.MinSTR > 0 || req.MinAGI > 0 || req.MinVIT > 0
                       || req.MinDEX > 0 || req.MinINT > 0 || req.MinLUK > 0
                       || req.AllowedRaces != CharacterRaceFlags.All;

            if (!hasAny) { _requirementsSection.SetActive(false); return; }

            var sb = new StringBuilder(128);
            sb.AppendLine("<b>Requisitos:</b>");

            bool hasPlayer = TryGetLocalPlayerStats(
                out int level, out int str, out int agi, out int vit,
                out int dex, out int intt, out int luk, out CharacterRace race);

            if (req.MinLevel > 1)
                sb.AppendLine(FormatReq($"Nível {req.MinLevel}+", hasPlayer && level >= req.MinLevel));
            if (req.MinSTR > 0) sb.AppendLine(FormatReq($"STR {req.MinSTR}+", hasPlayer && str  >= req.MinSTR));
            if (req.MinAGI > 0) sb.AppendLine(FormatReq($"AGI {req.MinAGI}+", hasPlayer && agi  >= req.MinAGI));
            if (req.MinVIT > 0) sb.AppendLine(FormatReq($"VIT {req.MinVIT}+", hasPlayer && vit  >= req.MinVIT));
            if (req.MinDEX > 0) sb.AppendLine(FormatReq($"DEX {req.MinDEX}+", hasPlayer && dex  >= req.MinDEX));
            if (req.MinINT > 0) sb.AppendLine(FormatReq($"INT {req.MinINT}+", hasPlayer && intt >= req.MinINT));
            if (req.MinLUK > 0) sb.AppendLine(FormatReq($"LUK {req.MinLUK}+", hasPlayer && luk  >= req.MinLUK));

            if (req.AllowedRaces != CharacterRaceFlags.All)
            {
                bool ok = !hasPlayer || (req.AllowedRaces & EquipmentSlotEx.ToFlag(race)) != 0;
                sb.AppendLine(FormatReq(EquipmentSlotEx.FlagsDisplayName(req.AllowedRaces), ok));
            }

            if (_requirementsText != null) _requirementsText.text = sb.ToString().TrimEnd();
            _requirementsSection.SetActive(true);
        }

        private static string FormatReq(string text, bool met)
            => met
                ? $"<color=#88FF88>✓ {text}</color>"
                : $"<color=#FF6666>✗ {text}</color>";

        /// <summary>
        /// Busca atributos totais do jogador local via NetworkClient.localPlayer
        /// e NetworkPlayer (SyncVars BaseSTR + AllocatedSTR + Level + RaceStr).
        ///
        /// CORREÇÃO v4: usa o tipo TOTALMENTE QUALIFICADO RPG.Network.NetworkPlayer
        /// para evitar ambiguidade com UnityEngine.NetworkPlayer (legacy, exposto
        /// por `using Mirror;`).
        /// </summary>
        private static bool TryGetLocalPlayerStats(out int level, out int str, out int agi, out int vit,
                                                   out int dex, out int intt, out int luk,
                                                   out CharacterRace race)
        {
            level = 1; str = 0; agi = 0; vit = 0; dex = 0; intt = 0; luk = 0;
            race  = CharacterRace.Human;

            var localId = NetworkClient.localPlayer;
            if (localId == null) return false;

            // CORREÇÃO v4: tipo totalmente qualificado
            var np = localId.GetComponent<RPG.Network.NetworkPlayer>();
            if (np == null) return false;

            level = np.Level;

            if (!System.Enum.TryParse<CharacterRace>(np.RaceStr, out race))
                race = CharacterRace.Human;

            var raceBonus = StatsCalculator.GetRaceBonus(race);

            str  = np.BaseSTR + raceBonus.STR + np.AllocatedSTR;
            agi  = np.BaseAGI + raceBonus.AGI + np.AllocatedAGI;
            vit  = np.BaseVIT + raceBonus.VIT + np.AllocatedVIT;
            dex  = np.BaseDEX + raceBonus.DEX + np.AllocatedDEX;
            intt = np.BaseINT + raceBonus.INT + np.AllocatedINT;
            luk  = np.BaseLUK + raceBonus.LUK + np.AllocatedLUK;

            return true;
        }

        // ── Skill ──────────────────────────────────────────────────────────

        private void ShowSkillSection(ItemData item)
        {
            if (_skillSection == null) return;

            if (!item.IsPowerGem || item.EmbeddedSkill == null)
            {
                _skillSection.SetActive(false);
                return;
            }

            var skill = item.EmbeddedSkill;
            var sb = new StringBuilder(128);
            sb.AppendLine($"<b>Concede: {skill.Name}</b>");
            sb.AppendLine($"Custo: {skill.ManaCost} MP");
            sb.AppendLine($"Cooldown: {skill.Cooldown:0.#}s");

            if (_skillText != null) _skillText.text = sb.ToString().TrimEnd();
            _skillSection.SetActive(true);
        }

        // ── Consumível ─────────────────────────────────────────────────────

        private void ShowConsumableSection(ItemData item)
        {
            if (_consumableSection == null) return;

            if (!item.IsConsumable) { _consumableSection.SetActive(false); return; }

            var sb = new StringBuilder(64);
            if (item.HealAmount > 0f)   sb.AppendLine($"<color=#66FF66>+{item.HealAmount:0} HP</color>");
            if (item.ManaAmount > 0f)   sb.AppendLine($"<color=#66AAFF>+{item.ManaAmount:0} MP</color>");
            if (item.BuffDuration > 0f) sb.AppendLine($"Duração: {item.BuffDuration:0.#}s");

            if (_consumableText != null) _consumableText.text = sb.ToString().TrimEnd();
            _consumableSection.SetActive(sb.Length > 0);
        }

        // ══════════════════════════════════════════════════════════════════
        // Posicionamento
        // ══════════════════════════════════════════════════════════════════

        private void PositionByAnchor(RectTransform anchor)
        {
            if (_root == null || anchor == null)
            {
                // Sem anchor: cai no posicionamento por cursor
                PositionByCursor();
                return;
            }

            Canvas canvas = _root.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            Vector3[] corners = new Vector3[4];
            anchor.GetWorldCorners(corners);

            Vector3 worldPos = corners[2]; // top-right do anchor
            worldPos += new Vector3(_offset.x, _offset.y, 0f) * canvas.scaleFactor / 100f;
            _root.position = worldPos;

            ClampToScreen(canvas);
        }

        /// <summary>
        /// NOVO v4 — posiciona o tooltip à direita do cursor com offset.
        /// Usado quando o caller não fornece um anchor (PowerGemUI.OnGemSlotHoverEnter).
        /// </summary>
        private void PositionByCursor()
        {
            if (_root == null) return;
            Canvas canvas = _root.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            Vector3 mousePos = Input.mousePosition;
            mousePos.x += _offset.x;
            mousePos.y += _offset.y;
            mousePos.z = 0f;

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                _root.position = mousePos;
            }
            else
            {
                // ScreenSpaceCamera ou WorldSpace: converte via câmera do canvas
                Camera cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
                if (cam != null)
                {
                    Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y,
                                                                          canvas.planeDistance));
                    _root.position = worldPos;
                }
            }

            ClampToScreen(canvas);
        }

        private void ClampToScreen(Canvas canvas)
        {
            if (_root == null || canvas == null) return;

            Vector3[] corners = new Vector3[4];
            _root.GetWorldCorners(corners);

            var canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null) return;

            Vector3[] canvasCorners = new Vector3[4];
            canvasRect.GetWorldCorners(canvasCorners);

            float dx = 0f, dy = 0f;
            if (corners[2].x > canvasCorners[2].x) dx = canvasCorners[2].x - corners[2].x;
            if (corners[0].x < canvasCorners[0].x) dx = canvasCorners[0].x - corners[0].x;
            if (corners[1].y > canvasCorners[1].y) dy = canvasCorners[1].y - corners[1].y;
            if (corners[0].y < canvasCorners[0].y) dy = canvasCorners[0].y - corners[0].y;

            if (dx != 0f || dy != 0f)
                _root.position += new Vector3(dx, dy, 0f);
        }

        private static string GetTypeDisplay(ItemData item)
        {
            if (item.IsEquipment)  return EquipmentSlotEx.DisplayName(item.EquipSlot);
            if (item.IsPowerGem)   return "Joia do Poder";
            if (item.IsConsumable) return "Consumível";
            return item.Type.ToString();
        }
    }
}