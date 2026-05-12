using UnityEngine;
using RPG.Data;
using RPG.Network;
using System.Collections.Generic;

namespace RPG.UI
{
    /// <summary>
    /// Painel lateral com os slots de equipamento (Arma, Escudo, Elmo, etc).
    /// Faz parte do InventoryUI — comunica-se com ele para abrir o ActionPanel
    /// quando o jogador clica em um item equipado.
    /// </summary>
    public class EquipmentPanelUI : MonoBehaviour
    {
        [Header("Slots")]
        [Tooltip("Configure cada widget com seu enum no Inspector.")]
        [SerializeField] private EquipmentSlotUI[] _slots;

        [Header("Tooltip (opcional)")]
        [SerializeField] private ItemTooltipUI _tooltip;

        [Header("Referência ao InventoryUI")]
        [Tooltip("InventoryUI usado para abrir o ActionPanel com 'Desequipar'.")]
        [SerializeField] private InventoryUI _inventoryUI;

        // ── Estado ─────────────────────────────────────────────────────────
        private NetworkInventory _inventory;
        private readonly Dictionary<EquipmentSlot, EquipmentSlotUI> _slotByEnum = new();
        private EquipmentSlot _currentSelectedSlot = EquipmentSlot.None;

        // ══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            BuildSlotMap();
            WireUpHandlers();
        }

        private void OnDestroy()
        {
            UnwireUpHandlers();
            UnbindFromInventory();
        }

        private void BuildSlotMap()
        {
            _slotByEnum.Clear();
            if (_slots == null) return;

            foreach (var w in _slots)
            {
                if (w == null) continue;
                if (w.Slot == EquipmentSlot.None)
                {
                    Debug.LogWarning($"[EquipmentPanelUI] Widget '{w.name}' tem EquipmentSlot.None.");
                    continue;
                }
                _slotByEnum[w.Slot] = w;
            }
        }

        private void WireUpHandlers()
        {
            if (_slots == null) return;
            foreach (var w in _slots)
            {
                if (w == null) continue;
                w.OnLeftClick  += HandleLeftClick;
                w.OnRightClick += HandleRightClick;
                w.OnHoverEnter += HandleHoverEnter;
                w.OnHoverExit  += HandleHoverExit;
            }
        }

        private void UnwireUpHandlers()
        {
            if (_slots == null) return;
            foreach (var w in _slots)
            {
                if (w == null) continue;
                w.OnLeftClick  -= HandleLeftClick;
                w.OnRightClick -= HandleRightClick;
                w.OnHoverEnter -= HandleHoverEnter;
                w.OnHoverExit  -= HandleHoverExit;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Bind
        // ══════════════════════════════════════════════════════════════════

        public void BindInventory(NetworkInventory inventory)
        {
            UnbindFromInventory();

            _inventory = inventory;
            if (_inventory == null)
            {
                ClearAllSlots();
                return;
            }

            _inventory.OnEquipmentChanged += RefreshAll;
            RefreshAll();
        }

        private void UnbindFromInventory()
        {
            if (_inventory != null)
                _inventory.OnEquipmentChanged -= RefreshAll;
            _inventory = null;
        }

        // ══════════════════════════════════════════════════════════════════
        // Refresh
        // ══════════════════════════════════════════════════════════════════

        public void RefreshAll()
        {
            if (_slots == null) return;

            if (_inventory == null)
            {
                ClearAllSlots();
                return;
            }

            var db = ItemDatabase.Instance;

            foreach (var w in _slots)
            {
                if (w == null) continue;

                string itemId = _inventory.GetEquipped(w.Slot);
                if (string.IsNullOrEmpty(itemId))
                {
                    w.SetEmpty();
                    continue;
                }

                var item = db?.GetItem(itemId);
                if (item == null)
                {
                    Debug.LogWarning($"[EquipmentPanelUI] Item '{itemId}' não no ItemDatabase.");
                    w.SetEmpty();
                    continue;
                }

                w.SetEquipment(item);
            }

            // Limpa seleção se o slot selecionado ficou vazio
            if (_currentSelectedSlot != EquipmentSlot.None
                && string.IsNullOrEmpty(_inventory.GetEquipped(_currentSelectedSlot)))
            {
                ClearSelection();
            }
            else
            {
                // Re-aplica visual de seleção
                foreach (var kv in _slotByEnum)
                    kv.Value.SetSelected(kv.Key == _currentSelectedSlot);
            }
        }

        private void ClearAllSlots()
        {
            if (_slots == null) return;
            foreach (var w in _slots) w?.SetEmpty();
        }

        public void ClearSelection()
        {
            _currentSelectedSlot = EquipmentSlot.None;
            foreach (var kv in _slotByEnum)
                kv.Value.SetSelected(false);
        }

        // ══════════════════════════════════════════════════════════════════
        // Pointer handlers
        // ══════════════════════════════════════════════════════════════════

        private void HandleLeftClick(EquipmentSlot slot)
        {
            if (_inventory == null) return;

            string itemId = _inventory.GetEquipped(slot);
            if (string.IsNullOrEmpty(itemId))
            {
                ClearSelection();
                _inventoryUI?.CloseActionPanelExternal();
                return;
            }

            var item = ItemDatabase.Instance?.GetItem(itemId);
            if (item == null) return;

            _currentSelectedSlot = slot;
            foreach (var kv in _slotByEnum)
                kv.Value.SetSelected(kv.Key == slot);

            if (_inventoryUI != null)
                _inventoryUI.ShowActionPanelForEquipment(slot, item);
            else
                Debug.LogWarning("[EquipmentPanelUI] _inventoryUI não atribuído.");
        }

        private void HandleRightClick(EquipmentSlot slot)
        {
            if (_inventory == null) return;
            if (string.IsNullOrEmpty(_inventory.GetEquipped(slot))) return;
            _inventory.CmdUnequipItem((byte)slot);
        }

        private void HandleHoverEnter(EquipmentSlot slot, ItemData item, RectTransform anchor)
        {
            if (_tooltip == null || item == null) return;
            _tooltip.ShowForItem(item, anchor);
        }

        private void HandleHoverExit()
        {
            _tooltip?.Hide();
        }
    }
}
