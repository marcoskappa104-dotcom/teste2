using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using RPG.Data;

namespace RPG.UI
{
    /// <summary>
    /// Slot visual de uma Joia do Poder (Q/W/E/R) na janela de Joias.
    /// Configurado pelo PowerGemUI via callbacks (OnClicked, OnHoverEnter/Exit).
    /// </summary>
    public class GemSlotWidget : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler
    {
        [Header("Referências visuais")]
        [SerializeField] private Image    background;
        [SerializeField] private Image    gemIcon;
        [SerializeField] private TMP_Text hotkeyLabel;
        [SerializeField] private TMP_Text gemNameLabel;
        [SerializeField] private Image    selectionBorder;
        [SerializeField] private Image    highlightOverlay;

        [Header("Cores")]
        [SerializeField] private Color emptyColor     = new Color(0.12f, 0.12f, 0.15f, 0.9f);
        [SerializeField] private Color filledColor    = new Color(0.10f, 0.20f, 0.30f, 0.95f);
        [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.2f, 0.25f);

        // ── Callbacks atribuídos pelo PowerGemUI ───────────────────────────
        public System.Action OnClicked;
        public System.Action OnHoverEnter;
        public System.Action OnHoverExit;

        private bool _hasGem;

        public bool HasGem => _hasGem;

        private void Awake()
        {
            SetGem(null, null);
            SetSelected(false);
            SetHighlight(false);
        }

        public void SetHotkeyLabel(string label)
        {
            if (hotkeyLabel != null) hotkeyLabel.text = label;
        }

        public void SetGem(ItemData item, string itemId)
        {
            _hasGem = item != null && !string.IsNullOrEmpty(itemId);

            if (background != null)
                background.color = _hasGem ? filledColor : emptyColor;

            if (gemIcon != null)
            {
                gemIcon.gameObject.SetActive(_hasGem);
                if (_hasGem && item.Icon != null)
                    gemIcon.sprite = item.Icon;
            }

            if (gemNameLabel != null)
            {
                if (_hasGem)
                {
                    gemNameLabel.text  = item.DisplayName;
                    gemNameLabel.color = item.RarityColor;
                }
                else
                {
                    gemNameLabel.text  = "<color=#555>Vazio</color>";
                    gemNameLabel.color = Color.white;
                }
            }
        }

        public void SetSelected(bool selected)
        {
            if (selectionBorder != null)
                selectionBorder.gameObject.SetActive(selected);
        }

        public void SetHighlight(bool highlight)
        {
            if (highlightOverlay != null)
            {
                highlightOverlay.gameObject.SetActive(highlight);
                if (highlight)
                    highlightOverlay.color = highlightColor;
            }
        }

        // ── Pointer events ─────────────────────────────────────────────────

        public void OnPointerClick(PointerEventData eventData) => OnClicked?.Invoke();
        public void OnPointerEnter(PointerEventData eventData) => OnHoverEnter?.Invoke();
        public void OnPointerExit (PointerEventData eventData) => OnHoverExit?.Invoke();
    }
}
