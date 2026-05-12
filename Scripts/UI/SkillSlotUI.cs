using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace RPG.UI
{
    /// <summary>
    /// Slot visual de uma skill (Q/W/E/R) na HUD.
    /// Controla apenas o visual: ícone, overlay de cooldown, texto.
    ///
    /// O UIManager configura este slot com o ícone da Joia equipada,
    /// e o SkillSystem dispara StartCooldown() quando o servidor confirma uso.
    /// </summary>
    public class SkillSlotUI : MonoBehaviour
    {
        [Header("Referências visuais")]
        [SerializeField] private Image    iconImage;
        [SerializeField] private Image    cooldownOverlay;
        [SerializeField] private TMP_Text cooldownText;
        [SerializeField] private TMP_Text hotkeyText;

        private float     _totalCooldown;
        private float     _remainingCooldown;
        private Coroutine _cooldownCoroutine;

        public bool OnCooldown { get; private set; }

        private void Awake()
        {
            if (cooldownOverlay != null)
            {
                cooldownOverlay.type       = Image.Type.Filled;
                cooldownOverlay.fillMethod = Image.FillMethod.Radial360;
                cooldownOverlay.fillOrigin = (int)Image.Origin360.Top;
                cooldownOverlay.fillAmount = 0f;
                cooldownOverlay.enabled    = false;
            }

            if (cooldownText != null) cooldownText.text = "";
        }

        // ══════════════════════════════════════════════════════════════════
        // API pública
        // ══════════════════════════════════════════════════════════════════

        public void SetIcon(Sprite icon)
        {
            if (iconImage == null) return;

            if (icon == null)
            {
                iconImage.sprite  = null;
                iconImage.enabled = false;
            }
            else
            {
                iconImage.sprite  = icon;
                iconImage.enabled = true;
            }
        }

        public void SetHotkey(string key)
        {
            if (hotkeyText != null) hotkeyText.text = key;
        }

        /// <summary>
        /// Inicia o cooldown visual. Se já houver um em andamento, é reiniciado.
        /// </summary>
        public void StartCooldown(float duration)
        {
            if (duration <= 0f) return;

            if (_cooldownCoroutine != null)
            {
                StopCoroutine(_cooldownCoroutine);
                _cooldownCoroutine = null;
            }

            _totalCooldown     = duration;
            _remainingCooldown = duration;
            OnCooldown         = true;

            _cooldownCoroutine = StartCoroutine(CooldownCoroutine());
        }

        public void StopCooldown()
        {
            if (_cooldownCoroutine != null)
            {
                StopCoroutine(_cooldownCoroutine);
                _cooldownCoroutine = null;
            }

            _remainingCooldown = 0f;
            OnCooldown         = false;

            if (cooldownOverlay != null)
            {
                cooldownOverlay.fillAmount = 0f;
                cooldownOverlay.enabled    = false;
            }
            if (cooldownText != null) cooldownText.text = "";
        }

        // ══════════════════════════════════════════════════════════════════
        // Coroutine
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator CooldownCoroutine()
        {
            if (cooldownOverlay != null)
            {
                cooldownOverlay.fillAmount = 1f;
                cooldownOverlay.enabled    = true;
            }
            if (cooldownText != null)
                cooldownText.text = $"{_totalCooldown:0.0}";

            while (_remainingCooldown > 0f)
            {
                yield return null;
                _remainingCooldown = Mathf.Max(0f, _remainingCooldown - Time.deltaTime);

                float fill = _totalCooldown > 0f
                    ? _remainingCooldown / _totalCooldown
                    : 0f;

                if (cooldownOverlay != null)
                    cooldownOverlay.fillAmount = fill;

                if (cooldownText != null)
                    cooldownText.text = _remainingCooldown > 0.05f
                        ? $"{_remainingCooldown:0.0}"
                        : "";
            }

            OnCooldown         = false;
            _cooldownCoroutine = null;

            if (cooldownOverlay != null)
            {
                cooldownOverlay.fillAmount = 0f;
                cooldownOverlay.enabled    = false;
            }
            if (cooldownText != null) cooldownText.text = "";
        }
    }
}
