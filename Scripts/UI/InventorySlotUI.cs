using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using RPG.Data;

namespace RPG.UI
{
    /// <summary>
    /// Slot do grid de inventário. Exibe ícone, quantidade e raridade.
    /// O InventoryUI registra callbacks para reagir a cliques e hovers.
    /// </summary>
    public class InventorySlotUI : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("Referências visuais")]
        [SerializeField] private Image    iconImage;
        [SerializeField] private Image    backgroundImage;
        [SerializeField] private Image    selectionBorder;
        [SerializeField] private TMP_Text quantityText;

        [Header("Cores de fundo por raridade")]
        [SerializeField] private Color colorEmpty     = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        [SerializeField] private Color colorCommon    = new Color(0.20f, 0.20f, 0.20f, 0.9f);
        [SerializeField] private Color colorUncommon  = new Color(0.10f, 0.25f, 0.10f, 0.9f);
        [SerializeField] private Color colorRare      = new Color(0.10f, 0.15f, 0.30f, 0.9f);
        [SerializeField] private Color colorEpic      = new Color(0.20f, 0.08f, 0.28f, 0.9f);
        [SerializeField] private Color colorLegendary = new Color(0.30f, 0.18f, 0.05f, 0.9f);

        // ── Dados ──────────────────────────────────────────────────────────
        private InventorySlotData _slotData;
        private ItemData          _itemData;
        private bool              _isEmpty = true;

        // ── Callbacks do InventoryUI ───────────────────────────────────────
        public System.Action<InventorySlotUI> OnSlotClicked;
        public System.Action<InventorySlotUI> OnSlotHoverEnter;
        public System.Action<InventorySlotUI> OnSlotHoverExit;

        // ── Getters ────────────────────────────────────────────────────────
        public InventorySlotData SlotData => _slotData;
        public ItemData          ItemData => _itemData;
        public bool              IsEmpty  => _isEmpty;

        // ══════════════════════════════════════════════════════════════════
        // Setup
        // ══════════════════════════════════════════════════════════════════

        public void Setup(InventorySlotData slotData, ItemData itemData)
        {
            _slotData = slotData;
            _itemData = itemData;
            _isEmpty  = itemData == null || string.IsNullOrEmpty(slotData.ItemId);

            RefreshVisual();
        }

        public void SetEmpty(int slotIndex = -1)
        {
            _slotData = InventorySlotData.Empty(slotIndex);
            _itemData = null;
            _isEmpty  = true;
            RefreshVisual();
        }

        // ══════════════════════════════════════════════════════════════════
        // Visual
        // ══════════════════════════════════════════════════════════════════

        private void RefreshVisual()
        {
            if (_isEmpty)
            {
                if (iconImage       != null) { iconImage.sprite = null; iconImage.enabled = false; }
                if (quantityText    != null) quantityText.gameObject.SetActive(false);
                if (backgroundImage != null) backgroundImage.color = colorEmpty;
                SetSelected(false);
                return;
            }

            if (iconImage != null)
            {
                iconImage.enabled = true;
                iconImage.sprite  = _itemData.Icon;
                iconImage.color   = _itemData.Icon != null ? Color.white : new Color(1f, 1f, 1f, 0.3f);
            }

            if (quantityText != null)
            {
                bool showQty = _slotData.Quantity > 1;
                quantityText.gameObject.SetActive(showQty);
                if (showQty) quantityText.text = $"x{_slotData.Quantity}";
            }

            if (backgroundImage != null)
                backgroundImage.color = ColorForRarity(_itemData.Rarity);
        }

        private Color ColorForRarity(ItemRarity r) => r switch
        {
            ItemRarity.Common    => colorCommon,
            ItemRarity.Uncommon  => colorUncommon,
            ItemRarity.Rare      => colorRare,
            ItemRarity.Epic      => colorEpic,
            ItemRarity.Legendary => colorLegendary,
            _                    => colorCommon
        };

        public void SetSelected(bool selected)
        {
            if (selectionBorder != null)
                selectionBorder.gameObject.SetActive(selected);
        }

        // ── Pointer events ─────────────────────────────────────────────────

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_isEmpty) OnSlotHoverEnter?.Invoke(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            OnSlotHoverExit?.Invoke(this);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_isEmpty) OnSlotClicked?.Invoke(this);
        }
    }
}
