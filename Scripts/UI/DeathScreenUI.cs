using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace RPG.UI
{
    /// <summary>
    /// Tela de morte com botão de respawn. Faz fade in suave.
    /// Desabilitada em servidor dedicado.
    /// </summary>
    public class DeathScreenUI : MonoBehaviour
    {
        public static DeathScreenUI Instance { get; private set; }

        [Header("Referências")]
        [SerializeField] private GameObject deathScreenPanel;
        [SerializeField] private Button     reviveButton;
        [SerializeField] private TMP_Text   titleText;
        [SerializeField] private TMP_Text   subtitleText;

        [Header("Textos")]
        [SerializeField] private string deathTitle    = "VOCÊ MORREU";
        [SerializeField] private string deathSubtitle = "Deseja reviver?";
        [SerializeField] private string reviveLabel   = "REVIVER";

        [Header("Animação")]
        [SerializeField] private float fadeInDuration = 0.5f;

        private const float BUTTON_REENABLE_DELAY = 1f;

        private RPG.Network.NetworkPlayer _localPlayer;
        private CanvasGroup _canvasGroup;
        private float       _fadeTimer;
        private bool        _fadingIn;
        private bool        _isReady;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (Application.isBatchMode) { enabled = false; return; }

            if (deathScreenPanel == null)
            {
                Debug.LogError("[DeathScreenUI] 'Death Screen Panel' não configurado.");
                enabled = false;
                return;
            }

            _canvasGroup = deathScreenPanel.GetComponent<CanvasGroup>()
                        ?? deathScreenPanel.AddComponent<CanvasGroup>();

            deathScreenPanel.SetActive(false);
            _canvasGroup.alpha          = 0f;
            _canvasGroup.interactable   = false;
            _canvasGroup.blocksRaycasts = false;

            if (titleText    != null) titleText.text    = deathTitle;
            if (subtitleText != null) subtitleText.text = deathSubtitle;

            if (reviveButton != null)
            {
                var label = reviveButton.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = reviveLabel;
                reviveButton.onClick.RemoveAllListeners();
                reviveButton.onClick.AddListener(OnReviveClicked);
            }

            _isReady = true;
        }

        private void Update()
        {
            if (!_fadingIn || _canvasGroup == null) return;

            _fadeTimer += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Clamp01(_fadeTimer / fadeInDuration);
            if (_fadeTimer >= fadeInDuration) _fadingIn = false;
        }

        // ══════════════════════════════════════════════════════════════════
        // API estática
        // ══════════════════════════════════════════════════════════════════

        public static void Show(RPG.Network.NetworkPlayer localPlayer)
        {
            if (Instance == null || !Instance._isReady)
            {
                Debug.LogError("[DeathScreenUI] Não encontrado ou não configurado.");
                return;
            }
            Instance.ShowInternal(localPlayer);
        }

        public static void Hide()
        {
            if (Instance != null && Instance._isReady)
                Instance.HideInternal();
        }

        // ══════════════════════════════════════════════════════════════════
        // Internas
        // ══════════════════════════════════════════════════════════════════

        private void ShowInternal(RPG.Network.NetworkPlayer localPlayer)
        {
            _localPlayer = localPlayer;
            deathScreenPanel.SetActive(true);
            _canvasGroup.alpha          = 0f;
            _canvasGroup.interactable   = true;
            _canvasGroup.blocksRaycasts = true;
            if (reviveButton != null) reviveButton.interactable = true;
            _fadeTimer = 0f;
            _fadingIn  = true;

            Cursor.visible   = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void HideInternal()
        {
            deathScreenPanel.SetActive(false);
            _canvasGroup.alpha          = 0f;
            _canvasGroup.interactable   = false;
            _canvasGroup.blocksRaycasts = false;
            _fadingIn    = false;
            _localPlayer = null;

            Cursor.visible   = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void OnReviveClicked()
        {
            if (_localPlayer == null) return;
            if (reviveButton != null) reviveButton.interactable = false;

            _localPlayer.CmdRequestRespawn();
            StartCoroutine(ReenableButtonCoroutine());
        }

        /// <summary>
        /// Coroutine null-safe — Invoke(string) lança exceção se o objeto for destruído.
        /// </summary>
        private IEnumerator ReenableButtonCoroutine()
        {
            yield return new WaitForSecondsRealtime(BUTTON_REENABLE_DELAY);
            if (this == null) yield break;
            if (reviveButton != null) reviveButton.interactable = true;
        }
    }
}
