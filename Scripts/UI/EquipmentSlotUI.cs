using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using RPG.Data;
using System;

namespace RPG.UI
{
    /// <summary>
    /// Widget de um slot de equipamento (Arma, Escudo, Elmo, etc).
    /// Dispara eventos para o EquipmentPanelUI ao receber cliques e hovers.
    /// </summary>
    public class EquipmentSlotUI : MonoBehaviour, IPointerClickHandler,
                                                  IPointerEnterHandler,
                                                  IPointerExitHandler
    {
        [Header("Configuração")]
        [Tooltip("Slot que este widget representa (defina no Inspector).")]
        [SerializeField] private EquipmentSlot _slot = EquipmentSlot.None;

        [Header("Referências")]
        [SerializeField] private Image      _itemIcon;
        [SerializeField] private TMP_Text   _slotLabel;
        [SerializeField] private Image      _rarityFrame;
        [SerializeField] private GameObject _emptyState;
        [SerializeField] private GameObject _filledState;

        [Header("Cores")]
        [SerializeField] private Color _commonColor   = new Color(0.6f, 0.6f, 0.6f, 0.8f);
        [SerializeField] private Color _selectedColor = new Color(1f, 0.85f, 0.3f, 1f);

        // ── Estado ─────────────────────────────────────────────────────────
        private string   _currentItemId;
        private ItemData _currentItem;

        // ── Eventos ────────────────────────────────────────────────────────
        public event Action<EquipmentSlot>                          OnLeftClick;
        public event Action<EquipmentSlot>                          OnRightClick;
        public event Action<EquipmentSlot, ItemData, RectTransform> OnHoverEnter;
        public event Action                                         OnHoverExit;

        // ── API ────────────────────────────────────────────────────────────

        public EquipmentSlot Slot          => _slot;
        public bool          IsEmpty       => string.IsNullOrEmpty(_currentItemId);
        public string        CurrentItemId => _currentItemId;

        private void Awake()
        {
            ApplyStaticLabel();
            SetEmpty();
        }

        private void ApplyStaticLabel()
        {
            if (_slotLabel != null && _slot != EquipmentSlot.None)
                _slotLabel.text = EquipmentSlotEx.DisplayName(_slot);
        }

        public void Configure(EquipmentSlot slot)
        {
            _slot = slot;
            ApplyStaticLabel();
        }

        public void SetEquipment(ItemData item)
        {
            if (item == null) { SetEmpty(); return; }

            _currentItemId = item.ItemId;
            _currentItem   = item;

            if (_itemIcon != null)
            {
                _itemIcon.sprite = item.Icon;
                _itemIcon.color  = item.Icon != null ? Color.white : new Color(1, 1, 1, 0.3f);
                _itemIcon.gameObject.SetActive(true);
            }

            if (_rarityFrame != null)
            {
                _rarityFrame.color = item.RarityColor;
                _rarityFrame.gameObject.SetActive(true);
            }

            if (_emptyState  != null) _emptyState.SetActive(false);
            if (_filledState != null) _filledState.SetActive(true);
            if (_slotLabel   != null) _slotLabel.gameObject.SetActive(false);
        }

        public void SetEmpty()
        {
            _currentItemId = null;
            _currentItem   = null;

            if (_itemIcon != null)
            {
                _itemIcon.sprite = null;
                _itemIcon.gameObject.SetActive(false);
            }
            if (_rarityFrame != null)
            {
                _rarityFrame.color = _commonColor;
                _rarityFrame.gameObject.SetActive(false);
            }

            if (_emptyState  != null) _emptyState.SetActive(true);
            if (_filledState != null) _filledState.SetActive(false);
            if (_slotLabel != null)
            {
                _slotLabel.gameObject.SetActive(true);
                _slotLabel.text = EquipmentSlotEx.DisplayName(_slot);
            }
        }

        public void SetSelected(bool selected)
        {
            if (_rarityFrame == null) return;

            if (selected)
                _rarityFrame.color = _selectedColor;
            else if (_currentItem != null)
                _rarityFrame.color = _currentItem.RarityColor;
            else
                _rarityFrame.color = _commonColor;
        }

        // ── Pointer events ─────────────────────────────────────────────────

        public void OnPointerClick(PointerEventData ev)
        {
            switch (ev.button)
            {
                case PointerEventData.InputButton.Left:  OnLeftClick?.Invoke(_slot);  break;
                case PointerEventData.InputButton.Right: OnRightClick?.Invoke(_slot); break;
            }
        }

        public void OnPointerEnter(PointerEventData ev)
        {
            if (IsEmpty) return;
            OnHoverEnter?.Invoke(_slot, _currentItem, transform as RectTransform);
        }

        public void OnPointerExit(PointerEventData ev)
        {
            OnHoverExit?.Invoke();
        }
    }
}
