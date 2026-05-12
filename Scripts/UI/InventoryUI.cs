using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RPG.Data;
using RPG.Network;

// Resolve ambiguidade com UnityEngine.NetworkPlayer (UNet legado)
using NetworkPlayer = RPG.Network.NetworkPlayer;

namespace RPG.UI
{
    /// <summary>
    /// Janela de inventário com grid de slots, painel de ação (Usar / Equipar /
    /// Descartar) e tooltip.
    ///
    /// Coopera com EquipmentPanelUI: quando o jogador clica num item já equipado
    /// no painel lateral, este UI abre o ActionPanel com a opção "Desequipar".
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        [Header("Painel raiz")]
        [SerializeField] private GameObject _root;

        [Header("Grid")]
        [SerializeField] private Transform  _slotParent;
        [SerializeField] private GameObject _slotPrefab;

        [Header("Painel de ação")]
        [SerializeField] private GameObject _actionPanel;
        [SerializeField] private Button     _btnUse;
        [SerializeField] private Button     _btnEquipGem;
        [SerializeField] private Button     _btnEquipItem;
        [SerializeField] private Button     _btnUnequip;
        [SerializeField] private Button     _btnDiscard;
        [SerializeField] private Button     _btnCancelAction;
        [SerializeField] private TMP_Text   _actionPanelTitle;

        [Header("Tooltip")]
        [SerializeField] private ItemTooltipUI _tooltip;

        [Header("Header (peso, ouro)")]
        [SerializeField] private TMP_Text _goldText;
        [SerializeField] private TMP_Text _weightText;

        [Header("Botão fechar")]
        [SerializeField] private Button _btnClose;

        // ── Estado ─────────────────────────────────────────────────────────
        private NetworkInventory _inventory;
        private NetworkPlayer    _player;

        private readonly List<InventorySlotUI> _slotWidgets = new();
        private InventorySlotUI _currentSelectedSlot;

        /// <summary>
        /// Quando != None, o ActionPanel foi aberto a partir do
        /// EquipmentPanelUI (não do grid).
        /// </summary>
        private EquipmentSlot _currentEquipmentContext = EquipmentSlot.None;
        private ItemData      _currentEquipmentItem;

        // ══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (_root         != null) _root.SetActive(false);
            if (_actionPanel  != null) _actionPanel.SetActive(false);

            if (_btnUse          != null) _btnUse.onClick.AddListener(OnUseClicked);
            if (_btnEquipGem     != null) _btnEquipGem.onClick.AddListener(OnEquipGemClicked);
            if (_btnEquipItem    != null) _btnEquipItem.onClick.AddListener(OnEquipItemClicked);
            if (_btnUnequip      != null) _btnUnequip.onClick.AddListener(OnUnequipClicked);
            if (_btnDiscard      != null) _btnDiscard.onClick.AddListener(OnDiscardClicked);
            if (_btnCancelAction != null) _btnCancelAction.onClick.AddListener(CloseActionPanel);

            if (_btnClose != null) _btnClose.onClick.AddListener(Close);
        }

        private void OnDestroy() => Unbind();

        // ══════════════════════════════════════════════════════════════════
        // Bind
        // ══════════════════════════════════════════════════════════════════

        public void Bind(NetworkPlayer player, NetworkInventory inventory)
        {
            Unbind();

            _player    = player;
            _inventory = inventory;
            if (_inventory == null) return;

            _inventory.OnInventoryChanged += RefreshGrid;
            _inventory.OnGoldChanged      += RefreshHeader;

            BuildOrResizeSlotPool();
            RefreshGrid();
            RefreshHeader();
        }

        private void Unbind()
        {
            if (_inventory != null)
            {
                _inventory.OnInventoryChanged -= RefreshGrid;
                _inventory.OnGoldChanged      -= RefreshHeader;
            }

            foreach (var sw in _slotWidgets)
            {
                if (sw == null) continue;
                sw.OnSlotClicked    = null;
                sw.OnSlotHoverEnter = null;
                sw.OnSlotHoverExit  = null;
            }

            _inventory             = null;
            _player                = null;
            _currentSelectedSlot   = null;
            _currentEquipmentContext = EquipmentSlot.None;
            _currentEquipmentItem    = null;
        }

        // ══════════════════════════════════════════════════════════════════
        // Open / Close
        // ══════════════════════════════════════════════════════════════════

        public void Open()
        {
            if (_root != null) _root.SetActive(true);
            RefreshGrid();
            RefreshHeader();
        }

        public void Close()
        {
            CloseActionPanel();
            if (_tooltip != null) _tooltip.Hide();
            if (_root != null) _root.SetActive(false);
        }

        public void Toggle()
        {
            if (_root == null) return;
            if (_root.activeSelf) Close(); else Open();
        }

        // ══════════════════════════════════════════════════════════════════
        // Grid
        // ══════════════════════════════════════════════════════════════════

        private void BuildOrResizeSlotPool()
        {
            if (_inventory == null || _slotParent == null || _slotPrefab == null) return;

            int desired = _inventory.Capacity;
            int current = _slotWidgets.Count;

            // Cresce
            for (int i = current; i < desired; i++)
            {
                var go = Instantiate(_slotPrefab, _slotParent);
                var w  = go.GetComponent<InventorySlotUI>();
                if (w == null)
                {
                    Debug.LogError("[InventoryUI] Prefab de slot sem componente InventorySlotUI.");
                    Destroy(go);
                    return;
                }
                int slotIndex = i;
                w.OnSlotClicked    = (sw) => OnSlotClicked(sw, slotIndex);
                w.OnSlotHoverEnter = (sw) => OnSlotHoverEnter(sw);
                w.OnSlotHoverExit  = (sw) => OnSlotHoverExit(sw);
                _slotWidgets.Add(w);
            }

            // Desativa excedentes
            for (int i = desired; i < _slotWidgets.Count; i++)
            {
                if (_slotWidgets[i] != null)
                    _slotWidgets[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < desired && i < _slotWidgets.Count; i++)
            {
                if (_slotWidgets[i] != null)
                    _slotWidgets[i].gameObject.SetActive(true);
            }
        }

        private void RefreshGrid()
        {
            if (_inventory == null) return;

            var db = ItemDatabase.Instance;

            BuildOrResizeSlotPool();

            for (int i = 0; i < _slotWidgets.Count && i < _inventory.Capacity; i++)
            {
                var w    = _slotWidgets[i];
                var slot = _inventory.GetSlot(i);

                if (string.IsNullOrEmpty(slot.ItemId))
                {
                    w.SetEmpty(i);
                    continue;
                }

                var item = db?.GetItem(slot.ItemId);
                w.Setup(slot, item);
            }

            // Se o slot selecionado ficou vazio ou mudou, fecha o ActionPanel
            if (_currentSelectedSlot != null
                && _currentEquipmentContext == EquipmentSlot.None
                && _currentSelectedSlot.IsEmpty)
            {
                CloseActionPanel();
            }
        }

        private void RefreshHeader()
        {
            if (_inventory == null) return;
            if (_goldText   != null) _goldText.text   = $"{_inventory.Gold:N0} G";
            if (_weightText != null) _weightText.text = "";
        }

        // ══════════════════════════════════════════════════════════════════
        // Pointer handlers (grid)
        // ══════════════════════════════════════════════════════════════════

        private void OnSlotClicked(InventorySlotUI w, int slotIndex)
        {
            if (w == null || _inventory == null) return;
            if (w.IsEmpty) { CloseActionPanel(); return; }
            ShowActionPanelForInventorySlot(w);
        }

        private void OnSlotHoverEnter(InventorySlotUI w)
        {
            if (w == null || w.IsEmpty || _tooltip == null) return;
            _tooltip.ShowForItem(w.ItemData, w.transform as RectTransform);
        }

        private void OnSlotHoverExit(InventorySlotUI w)
        {
            _tooltip?.Hide();
        }

        // ══════════════════════════════════════════════════════════════════
        // ActionPanel
        // ══════════════════════════════════════════════════════════════════

        private void ShowActionPanelForInventorySlot(InventorySlotUI w)
        {
            ClearAllSelections();
            _currentSelectedSlot   = w;
            _currentEquipmentContext = EquipmentSlot.None;
            _currentEquipmentItem    = null;
            w.SetSelected(true);

            var item = w.ItemData;

            if (_actionPanelTitle != null)
                _actionPanelTitle.text = item != null ? item.DisplayName : "";

            SetBtn(_btnUse,       item != null && item.IsConsumable);
            SetBtn(_btnEquipGem,  item != null && item.IsPowerGem);
            SetBtn(_btnEquipItem, item != null && item.IsEquipment);
            SetBtn(_btnUnequip,   false);
            SetBtn(_btnDiscard,   item != null);

            if (_actionPanel != null) _actionPanel.SetActive(true);
        }

        /// <summary>
        /// Chamado pelo EquipmentPanelUI quando o jogador clica num item já
        /// equipado. Abre apenas o botão "Desequipar".
        /// </summary>
        public void ShowActionPanelForEquipment(EquipmentSlot slot, ItemData item)
        {
            if (item == null) return;

            ClearAllSelections();
            _currentSelectedSlot     = null;
            _currentEquipmentContext = slot;
            _currentEquipmentItem    = item;

            if (_actionPanelTitle != null)
                _actionPanelTitle.text = item.DisplayName;

            SetBtn(_btnUse,       false);
            SetBtn(_btnEquipGem,  false);
            SetBtn(_btnEquipItem, false);
            SetBtn(_btnUnequip,   true);
            SetBtn(_btnDiscard,   false);

            if (_actionPanel != null) _actionPanel.SetActive(true);
        }

        /// <summary>
        /// API externa para o EquipmentPanelUI fechar o ActionPanel ao
        /// desmarcar seleção (ex: clicou em slot vazio).
        /// </summary>
        public void CloseActionPanelExternal() => CloseActionPanel();

        private void CloseActionPanel()
        {
            if (_actionPanel != null) _actionPanel.SetActive(false);
            ClearAllSelections();
            _currentSelectedSlot     = null;
            _currentEquipmentContext = EquipmentSlot.None;
            _currentEquipmentItem    = null;
        }

        private void ClearAllSelections()
        {
            foreach (var w in _slotWidgets)
                if (w != null) w.SetSelected(false);
        }

        private static void SetBtn(Button btn, bool active)
        {
            if (btn == null) return;
            btn.gameObject.SetActive(active);
        }

        // ══════════════════════════════════════════════════════════════════
        // Ações
        // ══════════════════════════════════════════════════════════════════

        private void OnUseClicked()
        {
            if (_currentSelectedSlot == null || _inventory == null) return;
            var slot = _currentSelectedSlot.SlotData;
            if (string.IsNullOrEmpty(slot.ItemId)) return;
            _inventory.CmdUseItem(slot.SlotIndex);
            CloseActionPanel();
        }

        private void OnEquipGemClicked()
        {
            if (_currentSelectedSlot == null) return;
            var slot = _currentSelectedSlot.SlotData;
            var item = _currentSelectedSlot.ItemData;
            if (item == null || !item.IsPowerGem) return;

            // Abre a PowerGemUI no modo "escolher slot Q/W/E/R para esta joia"
            UIManager.Instance?.OpenPowerGemUIForEquip(slot.SlotIndex, item);
            CloseActionPanel();
        }

        private void OnEquipItemClicked()
        {
            if (_currentSelectedSlot == null || _inventory == null) return;
            var slot = _currentSelectedSlot.SlotData;
            var item = _currentSelectedSlot.ItemData;
            if (item == null || !item.IsEquipment) return;

            _inventory.CmdEquipItem(slot.SlotIndex);
            CloseActionPanel();
        }

        private void OnUnequipClicked()
        {
            if (_inventory == null || _currentEquipmentContext == EquipmentSlot.None) return;
            _inventory.CmdUnequipItem((byte)_currentEquipmentContext);
            CloseActionPanel();
        }

        private void OnDiscardClicked()
        {
            if (_currentSelectedSlot == null || _inventory == null) return;
            var slot = _currentSelectedSlot.SlotData;
            if (string.IsNullOrEmpty(slot.ItemId)) return;
            _inventory.CmdDropItem(slot.SlotIndex, slot.Quantity);
            CloseActionPanel();
        }
    }
}
