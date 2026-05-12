using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RPG.Data;
using RPG.Network;

namespace RPG.UI
{
    /// <summary>
    /// Janela das Joias do Poder. Mostra os 4 slots equipados (Q/W/E/R) e permite:
    ///
    ///   - OpenForEquip(invSlot, gem): jogador clicou "Equipar Joia" no inventário.
    ///       Os 4 slots ficam destacados; clicar em um deles envia o CmdEquipPowerGem.
    ///
    ///   - OpenBrowse(): só visualização, com botão "Desequipar" por slot.
    ///
    /// Se o NetworkInventory ainda não estiver pronto quando a UI abre, faz retry.
    /// </summary>
    public class PowerGemUI : MonoBehaviour
    {
        private const float BIND_RETRY_INTERVAL = 0.25f;
        private const int   POWER_GEM_SLOT_COUNT = 4;

        [Header("Painel raiz")]
        [SerializeField] private GameObject _root;

        [Header("Slots")]
        [SerializeField] private GemSlotWidget[] _slotWidgets = new GemSlotWidget[POWER_GEM_SLOT_COUNT];

        [Header("Tooltip")]
        [SerializeField] private ItemTooltipUI _tooltip;

        [Header("Texto")]
        [SerializeField] private TMP_Text _instructionText;

        [Header("Botões")]
        [SerializeField] private Button _btnUnequipSelected;
        [SerializeField] private Button _btnClose;
        [SerializeField] private Button _btnCancelEquip;

        // ── Estado ─────────────────────────────────────────────────────────
        private NetworkInventory _inventory;

        private enum Mode { Closed, Browse, Equip }
        private Mode _mode = Mode.Closed;

        // Em modo Equip:
        private int      _pendingInvSlot = -1;
        private ItemData _pendingGem;

        // Slot Q/W/E/R selecionado em modo Browse
        private int _selectedGemSlotIndex = -1;

        private Coroutine _retryBindCo;

        private static readonly string[] HOTKEY_LABELS = { "Q", "W", "E", "R" };

        // ══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (_root != null) _root.SetActive(false);
            WireUpSlots();

            if (_btnClose            != null) _btnClose.onClick.AddListener(Close);
            if (_btnCancelEquip      != null) _btnCancelEquip.onClick.AddListener(CancelEquipMode);
            if (_btnUnequipSelected  != null) _btnUnequipSelected.onClick.AddListener(OnUnequipSelected);
        }

        private void OnDestroy() => Unbind();

        private void WireUpSlots()
        {
            if (_slotWidgets == null) return;
            for (int i = 0; i < _slotWidgets.Length; i++)
            {
                var w = _slotWidgets[i];
                if (w == null) continue;

                int idx = i;
                w.SetHotkeyLabel(idx < HOTKEY_LABELS.Length ? HOTKEY_LABELS[idx] : (idx + 1).ToString());
                w.OnClicked     = () => OnSlotClicked(idx);
                w.OnHoverEnter  = () => OnSlotHoverEnter(idx);
                w.OnHoverExit   = () => OnSlotHoverExit(idx);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Bind
        // ══════════════════════════════════════════════════════════════════

        public void Bind(NetworkInventory inventory)
        {
            Unbind();

            _inventory = inventory;
            if (_inventory == null) return;

            _inventory.OnEquipmentChanged += RefreshSlots;
            RefreshSlots();
        }

        private void Unbind()
        {
            if (_inventory != null)
                _inventory.OnEquipmentChanged -= RefreshSlots;
            _inventory = null;

            if (_retryBindCo != null)
            {
                StopCoroutine(_retryBindCo);
                _retryBindCo = null;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // API pública
        // ══════════════════════════════════════════════════════════════════

        public void OpenBrowse()
        {
            _mode = Mode.Browse;
            ShowRoot();

            HighlightAllSlots(false);
            _selectedGemSlotIndex = -1;
            UpdateUnequipButton();

            if (_instructionText != null)
                _instructionText.text = "Clique em uma joia para selecionar.";
            if (_btnCancelEquip != null)
                _btnCancelEquip.gameObject.SetActive(false);

            EnsureBoundAndRefresh();
        }

        public void OpenForEquip(int invSlot, ItemData gem)
        {
            if (gem == null || !gem.IsPowerGem) return;

            _mode           = Mode.Equip;
            _pendingInvSlot = invSlot;
            _pendingGem     = gem;
            ShowRoot();

            HighlightAllSlots(true);
            _selectedGemSlotIndex = -1;
            UpdateUnequipButton();

            if (_instructionText != null)
                _instructionText.text = $"Escolha um slot para <color=#FFCC66>{gem.DisplayName}</color>";
            if (_btnCancelEquip != null)
                _btnCancelEquip.gameObject.SetActive(true);

            EnsureBoundAndRefresh();
        }

        public void Close()
        {
            _mode = Mode.Closed;
            _pendingInvSlot = -1;
            _pendingGem     = null;
            _selectedGemSlotIndex = -1;
            HighlightAllSlots(false);
            _tooltip?.Hide();
            if (_root != null) _root.SetActive(false);
        }

        public void Toggle()
        {
            if (_root != null && _root.activeSelf) Close();
            else OpenBrowse();
        }

        // ══════════════════════════════════════════════════════════════════
        // Pointer handlers
        // ══════════════════════════════════════════════════════════════════

        private void OnSlotClicked(int slotIdx)
        {
            if (_inventory == null) return;

            if (_mode == Mode.Equip)
            {
                if (_pendingInvSlot < 0 || _pendingGem == null) return;
                _inventory.CmdEquipPowerGem(_pendingInvSlot, (byte)slotIdx);
                _mode = Mode.Closed;
                Close();
                return;
            }

            // Browse: seleciona
            _selectedGemSlotIndex = slotIdx;
            for (int i = 0; i < _slotWidgets.Length; i++)
                if (_slotWidgets[i] != null)
                    _slotWidgets[i].SetSelected(i == slotIdx);
            UpdateUnequipButton();
        }

        private void OnSlotHoverEnter(int slotIdx)
        {
            if (_inventory == null || _tooltip == null) return;
            if (slotIdx < 0 || slotIdx >= POWER_GEM_SLOT_COUNT) return;

            string gemId = _inventory.GetEquippedPowerGem(slotIdx);
            if (string.IsNullOrEmpty(gemId)) return;

            var gem = ItemDatabase.Instance?.GetItem(gemId);
            if (gem == null) return;

            var w = _slotWidgets[slotIdx];
            _tooltip.ShowForItem(gem, w != null ? w.transform as RectTransform : null);
        }

        private void OnSlotHoverExit(int _)
        {
            _tooltip?.Hide();
        }

        private void OnUnequipSelected()
        {
            if (_inventory == null) return;
            if (_selectedGemSlotIndex < 0) return;

            string id = _inventory.GetEquippedPowerGem(_selectedGemSlotIndex);
            if (string.IsNullOrEmpty(id)) return;

            _inventory.CmdUnequipPowerGem((byte)_selectedGemSlotIndex);
            _selectedGemSlotIndex = -1;
            UpdateUnequipButton();
        }

        private void CancelEquipMode()
        {
            if (_mode != Mode.Equip) return;
            _mode           = Mode.Closed;
            _pendingInvSlot = -1;
            _pendingGem     = null;
            Close();
        }

        // ══════════════════════════════════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════════════════════════════════

        private void ShowRoot()
        {
            if (_root != null) _root.SetActive(true);
        }

        private void HighlightAllSlots(bool on)
        {
            foreach (var w in _slotWidgets)
                w?.SetHighlight(on);
        }

        private void UpdateUnequipButton()
        {
            if (_btnUnequipSelected == null) return;

            if (_mode != Mode.Browse || _inventory == null || _selectedGemSlotIndex < 0)
            {
                _btnUnequipSelected.gameObject.SetActive(false);
                return;
            }

            string id = _inventory.GetEquippedPowerGem(_selectedGemSlotIndex);
            bool hasGem = !string.IsNullOrEmpty(id);
            _btnUnequipSelected.gameObject.SetActive(hasGem);
            _btnUnequipSelected.interactable = hasGem;
        }

        private void EnsureBoundAndRefresh()
        {
            if (_inventory != null)
            {
                RefreshSlots();
                return;
            }

            // Tenta achar via UIManager / local player
            var inv = TryFindLocalInventory();
            if (inv != null)
            {
                Bind(inv);
                return;
            }

            // Inicia retry
            if (_retryBindCo == null)
                _retryBindCo = StartCoroutine(RetryBindCoroutine());
        }

        private static NetworkInventory TryFindLocalInventory()
        {
            var local = Mirror.NetworkClient.localPlayer;
            if (local == null) return null;
            return local.GetComponent<NetworkInventory>();
        }

        private IEnumerator RetryBindCoroutine()
        {
            while (_inventory == null)
            {
                yield return new WaitForSecondsRealtime(BIND_RETRY_INTERVAL);
                var inv = TryFindLocalInventory();
                if (inv != null)
                {
                    Bind(inv);
                    break;
                }
            }
            _retryBindCo = null;
        }

        private void RefreshSlots()
        {
            if (_inventory == null || _slotWidgets == null) return;

            var db = ItemDatabase.Instance;

            for (int i = 0; i < _slotWidgets.Length && i < POWER_GEM_SLOT_COUNT; i++)
            {
                var w = _slotWidgets[i];
                if (w == null) continue;

                string id = _inventory.GetEquippedPowerGem(i);
                if (string.IsNullOrEmpty(id))
                {
                    w.SetGem(null, null);
                    continue;
                }

                var gem = db?.GetItem(id);
                w.SetGem(gem, id);
            }

            UpdateUnequipButton();
        }
    }
}
