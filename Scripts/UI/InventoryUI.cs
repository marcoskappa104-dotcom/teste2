using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RPG.Data;
using RPG.Network;
using System.Collections.Generic;

namespace RPG.UI
{
    /// <summary>
    /// InventoryUI v7 — NOVO: botão "Desequipar" no ActionPanel.
    ///
    /// MUDANÇAS v7:
    ///   - Novo campo _unequipButton: botão que aparece apenas quando o item
    ///     atualmente selecionado é um equipamento JÁ EQUIPADO (clicado a
    ///     partir do EquipmentPanelUI).
    ///   - Novo estado _selectedEquipSlot (EquipmentSlot): identifica que a
    ///     seleção corrente vem do painel de equipamento (não do inventário).
    ///   - Novo método público ShowActionPanelForEquipment(slot, item):
    ///     chamado pelo EquipmentPanelUI ao clique esquerdo num slot ocupado.
    ///   - Novo método público CloseActionPanelExternal(): chamado pelo
    ///     EquipmentPanelUI ao clique em slot vazio.
    ///   - OnSlotClicked (inventário) agora limpa seleção do EquipmentPanel
    ///     e esconde o botão Desequipar.
    ///
    /// Mantido v6: fecha _windowRoot em Start(), tecla I/Escape, integração
    /// com PowerGemUI.OpenForEquip, e todas as APIs do InventorySlotUI.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        public static InventoryUI Instance { get; private set; }

        [Header("Grid do Inventário")]
        [SerializeField] private Transform        _slotsContainer;
        [SerializeField] private InventorySlotUI  _slotPrefab;

        [Header("Painel de Equipamento (lateral)")]
        [SerializeField] private EquipmentPanelUI _equipmentPanel;

        [Header("Painel de Ação (item selecionado)")]
        [SerializeField] private GameObject _actionPanel;
        [SerializeField] private TMP_Text   _selectedItemNameText;
        [SerializeField] private Button     _useButton;        // consumível
        [SerializeField] private Button     _equipGemButton;   // joia
        [SerializeField] private Button     _equipItemButton;  // equipamento
        [SerializeField] private Button     _unequipButton;    // NOVO v7 — desequipar
        [SerializeField] private Button     _discardButton;
        [SerializeField] private Button     _closeActionPanelButton;

        [Header("Tooltip")]
        [SerializeField] private ItemTooltipUI _tooltip;

        [Header("Janela")]
        [SerializeField] private GameObject _windowRoot;
        [SerializeField] private Button     _closeButton;

        [Header("Atalhos de teclado")]
        [SerializeField] private KeyCode _toggleKey     = KeyCode.I;
        [SerializeField] private bool    _closeOnEscape = true;

        // ── Estado ─────────────────────────────────────────────────────────
        private NetworkInventory _inventory;
        private readonly List<InventorySlotUI> _slots = new();

        // Seleção: se a fonte é o inventário, _selectedSlotIndex >= 0 e _selectedEquipSlot == None.
        // Se a fonte é o painel de equipamento, _selectedSlotIndex == -1 e _selectedEquipSlot != None.
        private int           _selectedSlotIndex = -1;
        private EquipmentSlot _selectedEquipSlot = EquipmentSlot.None;
        private ItemData      _selectedItemData;

        // ══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (_actionPanel != null) _actionPanel.SetActive(false);

            _useButton?.onClick.AddListener(OnUseButtonClicked);
            _equipGemButton?.onClick.AddListener(OnEquipGemButtonClicked);
            _equipItemButton?.onClick.AddListener(OnEquipItemButtonClicked);
            _unequipButton?.onClick.AddListener(OnUnequipButtonClicked);
            _discardButton?.onClick.AddListener(OnDiscardButtonClicked);
            _closeActionPanelButton?.onClick.AddListener(CloseActionPanel);
            _closeButton?.onClick.AddListener(Close);
        }

        private void Start()
        {
            if (_windowRoot != null)
                _windowRoot.SetActive(false);
            else
                Debug.LogWarning("[InventoryUI] _windowRoot não foi atribuído — a janela pode ficar visível desde o início.");

            if (_actionPanel != null) _actionPanel.SetActive(false);
        }

        private void Update()
        {
            if (_toggleKey != KeyCode.None && Input.GetKeyDown(_toggleKey))
            {
                Toggle();
                return;
            }

            if (_closeOnEscape && IsOpen && Input.GetKeyDown(KeyCode.Escape))
                Close();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            UnbindFromInventory();

            _useButton?.onClick.RemoveListener(OnUseButtonClicked);
            _equipGemButton?.onClick.RemoveListener(OnEquipGemButtonClicked);
            _equipItemButton?.onClick.RemoveListener(OnEquipItemButtonClicked);
            _unequipButton?.onClick.RemoveListener(OnUnequipButtonClicked);
            _discardButton?.onClick.RemoveListener(OnDiscardButtonClicked);
            _closeActionPanelButton?.onClick.RemoveListener(CloseActionPanel);
            _closeButton?.onClick.RemoveListener(Close);
        }

        // ══════════════════════════════════════════════════════════════════
        // Bind
        // ══════════════════════════════════════════════════════════════════

        public void BindInventory(NetworkInventory inventory)
        {
            UnbindFromInventory();
            _inventory = inventory;

            if (_inventory != null)
            {
                _inventory.OnInventoryChanged += RefreshGrid;
                _inventory.OnEquipmentChanged += OnEquipmentChangedRefreshSelection;
                RefreshGrid();
            }
            else
            {
                RefreshGrid();
            }

            _equipmentPanel?.BindInventory(_inventory);
        }

        private void UnbindFromInventory()
        {
            if (_inventory != null)
            {
                _inventory.OnInventoryChanged -= RefreshGrid;
                _inventory.OnEquipmentChanged -= OnEquipmentChangedRefreshSelection;
            }
            _inventory = null;
        }

        /// <summary>
        /// Se a seleção corrente é um item EQUIPADO e ele foi desequipado
        /// pelo servidor (ex: via clique direito no painel ou auto), fecha
        /// o ActionPanel.
        /// </summary>
        private void OnEquipmentChangedRefreshSelection()
        {
            if (_selectedEquipSlot == EquipmentSlot.None) return;
            if (_inventory == null) return;

            if (string.IsNullOrEmpty(_inventory.GetEquipped(_selectedEquipSlot)))
                CloseActionPanel();
        }

        // ══════════════════════════════════════════════════════════════════
        // Refresh do grid
        // ══════════════════════════════════════════════════════════════════

        public void RefreshGrid()
        {
            if (_slotsContainer == null || _slotPrefab == null) return;

            int total = _inventory != null ? _inventory.Slots.Count : 0;

            while (_slots.Count < total)
            {
                var slot = Instantiate(_slotPrefab, _slotsContainer);
                slot.OnSlotClicked    += OnSlotClicked;
                slot.OnSlotHoverEnter += OnSlotHoverEnter;
                slot.OnSlotHoverExit  += OnSlotHoverExit;
                _slots.Add(slot);
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                if (i >= total)
                {
                    _slots[i].SetEmpty();
                    _slots[i].gameObject.SetActive(false);
                    continue;
                }

                var slotData = _inventory.Slots[i];
                var item     = ItemDatabase.Instance?.GetItem(slotData.ItemId);
                _slots[i].gameObject.SetActive(true);
                _slots[i].Setup(slotData, item);
            }

            UpdateSelectionVisual();

            // Se a seleção atual do inventário sumiu, fecha o painel
            if (_selectedSlotIndex >= 0 && !SlotExistsInInventory(_selectedSlotIndex))
                CloseActionPanel();
        }

        private void UpdateSelectionVisual()
        {
            foreach (var slot in _slots)
            {
                if (slot == null || !slot.gameObject.activeSelf) continue;
                bool isSelected = !slot.IsEmpty
                                  && _selectedSlotIndex >= 0
                                  && slot.SlotData.SlotIndex == _selectedSlotIndex;
                slot.SetSelected(isSelected);
            }
        }

        private bool SlotExistsInInventory(int slotIndex)
        {
            if (_inventory == null) return false;
            foreach (var s in _inventory.Slots)
                if (s.SlotIndex == slotIndex) return true;
            return false;
        }

        // ══════════════════════════════════════════════════════════════════
        // Callbacks do grid (inventário)
        // ══════════════════════════════════════════════════════════════════

        private void OnSlotClicked(InventorySlotUI slot)
        {
            if (slot == null || slot.IsEmpty)
            {
                CloseActionPanel();
                return;
            }

            // Seleção do inventário — limpa seleção do EquipmentPanel
            _selectedSlotIndex = slot.SlotData.SlotIndex;
            _selectedEquipSlot = EquipmentSlot.None;
            _selectedItemData  = slot.ItemData;

            _equipmentPanel?.ClearSelection();
            UpdateSelectionVisual();

            ShowActionPanelButtonsForInventoryItem(_selectedItemData);
        }

        private void OnSlotHoverEnter(InventorySlotUI slot)
        {
            if (_tooltip == null || slot == null || slot.IsEmpty) return;
            _tooltip.ShowForItem(slot.ItemData, slot.transform as RectTransform);
        }

        private void OnSlotHoverExit(InventorySlotUI slot)
        {
            _tooltip?.Hide();
        }

        // ══════════════════════════════════════════════════════════════════
        // API pública usada pelo EquipmentPanelUI
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// NOVO v7 — chamado pelo EquipmentPanelUI quando o jogador clica
        /// (esquerdo) num slot equipado. Abre o ActionPanel com o botão
        /// "Desequipar" visível.
        /// </summary>
        public void ShowActionPanelForEquipment(EquipmentSlot slot, ItemData item)
        {
            if (item == null) return;

            // Limpa seleção do inventário
            _selectedSlotIndex = -1;
            _selectedEquipSlot = slot;
            _selectedItemData  = item;

            UpdateSelectionVisual();
            ShowActionPanelButtonsForEquippedItem(item, slot);
        }

        /// <summary>
        /// Fecha o ActionPanel externamente (chamado pelo EquipmentPanelUI
        /// quando o usuário clica em slot vazio).
        /// </summary>
        public void CloseActionPanelExternal() => CloseActionPanel();

        // ══════════════════════════════════════════════════════════════════
        // Visibilidade dos botões
        // ══════════════════════════════════════════════════════════════════

        private void ShowActionPanelButtonsForInventoryItem(ItemData item)
        {
            if (_actionPanel != null) _actionPanel.SetActive(true);

            if (_selectedItemNameText != null)
            {
                _selectedItemNameText.text  = item.DisplayName;
                _selectedItemNameText.color = item.RarityColor;
            }

            if (_useButton       != null) _useButton.gameObject.SetActive(item.IsConsumable);
            if (_equipGemButton  != null) _equipGemButton.gameObject.SetActive(item.IsPowerGem);
            if (_equipItemButton != null) _equipItemButton.gameObject.SetActive(item.IsEquipment);
            if (_unequipButton   != null) _unequipButton.gameObject.SetActive(false); // NUNCA para item do inventário
            if (_discardButton   != null) _discardButton.gameObject.SetActive(true);
        }

        private void ShowActionPanelButtonsForEquippedItem(ItemData item, EquipmentSlot slot)
        {
            if (_actionPanel != null) _actionPanel.SetActive(true);

            if (_selectedItemNameText != null)
            {
                _selectedItemNameText.text  = $"{item.DisplayName}  <size=70%><color=#AAAAAA>({EquipmentSlotEx.DisplayName(slot)})</color></size>";
                _selectedItemNameText.color = item.RarityColor;
            }

            // Para item equipado, mostramos APENAS o Desequipar
            if (_useButton       != null) _useButton.gameObject.SetActive(false);
            if (_equipGemButton  != null) _equipGemButton.gameObject.SetActive(false);
            if (_equipItemButton != null) _equipItemButton.gameObject.SetActive(false);
            if (_discardButton   != null) _discardButton.gameObject.SetActive(false);
            if (_unequipButton   != null) _unequipButton.gameObject.SetActive(true);
        }

        // ══════════════════════════════════════════════════════════════════
        // Handlers de botões
        // ══════════════════════════════════════════════════════════════════

        private void OnUseButtonClicked()
        {
            if (_inventory == null || _selectedItemData == null) return;
            if (!_selectedItemData.IsConsumable) return;
            _inventory.CmdUseConsumable(_selectedSlotIndex);
            CloseActionPanel();
        }

        private void OnEquipGemButtonClicked()
        {
            if (_inventory == null || _selectedItemData == null) return;
            if (!_selectedItemData.IsPowerGem) return;

            InventorySlotData targetSlot = default;
            bool found = false;
            foreach (var s in _inventory.Slots)
            {
                if (s.SlotIndex == _selectedSlotIndex)
                {
                    targetSlot = s;
                    found      = true;
                    break;
                }
            }
            if (!found) { CloseActionPanel(); return; }

            PowerGemUI.Instance?.OpenForEquip(targetSlot);
            CloseActionPanel();
        }

        private void OnEquipItemButtonClicked()
        {
            if (_inventory == null || _selectedItemData == null) return;
            if (!_selectedItemData.IsEquipment) return;
            _inventory.CmdAutoEquip(_selectedSlotIndex);
            CloseActionPanel();
        }

        /// <summary>
        /// NOVO v7 — desequipa o item atualmente selecionado no EquipmentPanel,
        /// devolvendo-o ao inventário (lógica no servidor via CmdUnequipItem).
        /// </summary>
        private void OnUnequipButtonClicked()
        {
            if (_inventory == null) return;
            if (_selectedEquipSlot == EquipmentSlot.None)
            {
                Debug.LogWarning("[InventoryUI] OnUnequipButtonClicked sem slot equipado selecionado.");
                return;
            }

            _inventory.CmdUnequipItem((byte)_selectedEquipSlot);
            CloseActionPanel();
        }

        private void OnDiscardButtonClicked()
        {
            if (_inventory == null) return;
            if (_selectedSlotIndex < 0) return;
            _inventory.CmdRemoveItem(_selectedSlotIndex);
            CloseActionPanel();
        }

        private void CloseActionPanel()
        {
            _selectedSlotIndex = -1;
            _selectedEquipSlot = EquipmentSlot.None;
            _selectedItemData  = null;

            UpdateSelectionVisual();
            _equipmentPanel?.ClearSelection();

            if (_actionPanel != null) _actionPanel.SetActive(false);
            _tooltip?.Hide();
        }

        // ══════════════════════════════════════════════════════════════════
        // Janela
        // ══════════════════════════════════════════════════════════════════

        public void Open()
        {
            if (_windowRoot != null) _windowRoot.SetActive(true);
            RefreshGrid();
            _equipmentPanel?.RefreshAll();
        }

        public void Close()
        {
            CloseActionPanel();
            if (_windowRoot != null) _windowRoot.SetActive(false);
        }

        public void Toggle()
        {
            if (_windowRoot == null)
            {
                Debug.LogWarning("[InventoryUI] Toggle ignorado: _windowRoot não atribuído.");
                return;
            }
            if (_windowRoot.activeSelf) Close();
            else                        Open();
        }

        public bool IsOpen => _windowRoot != null && _windowRoot.activeSelf;
    }
}