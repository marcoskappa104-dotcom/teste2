using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;
using RPG.Data;
using RPG.Network;

// Resolve ambiguidade com UnityEngine.NetworkPlayer (UNet legado)
using NetworkPlayer = RPG.Network.NetworkPlayer;

namespace RPG.UI
{
    /// <summary>
    /// Orquestrador da HUD durante o jogo:
    ///
    ///   - Barras de HP/MP e XP
    ///   - Barra de skills (4 slots Q/W/E/R)
    ///   - Botões de abrir Inventário, Atributos, Joias
    ///   - Coordena abertura/fechamento das janelas
    ///
    /// Vinculação: BindLocalPlayer() é chamada quando o NetworkPlayer local
    /// fica disponível. RegisterHudButtonsSafe() é chamada em Start e novamente
    /// em BindLocalPlayer para resistir a ordem de inicialização.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Barras de status")]
        [SerializeField] private Slider   _hpSlider;
        [SerializeField] private Slider   _mpSlider;
        [SerializeField] private Slider   _xpSlider;
        [SerializeField] private TMP_Text _hpText;
        [SerializeField] private TMP_Text _mpText;
        [SerializeField] private TMP_Text _xpText;
        [SerializeField] private TMP_Text _levelText;
        [SerializeField] private TMP_Text _nameText;

        [Header("Barra de skills (Q/W/E/R)")]
        [SerializeField] private SkillSlotUI[] _skillSlots = new SkillSlotUI[4];

        [Header("Botões da HUD")]
        [SerializeField] private Button _btnInventory;
        [SerializeField] private Button _btnAttributes;
        [SerializeField] private Button _btnPowerGems;

        [Header("Janelas")]
        [SerializeField] private InventoryUI         _inventoryUI;
        [SerializeField] private AttributeWindowUI   _attributeWindow;
        [SerializeField] private PowerGemUI          _powerGemUI;
        [SerializeField] private EquipmentPanelUI    _equipmentPanel;

        // ── Estado ─────────────────────────────────────────────────────────
        private NetworkPlayer    _player;
        private NetworkInventory _inventory;
        private bool             _buttonsRegistered;

        private static readonly string[] HOTKEY_LABELS = { "Q", "W", "E", "R" };

        // ══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            UnbindLocalPlayer();
        }

        private void Start()
        {
            ConfigureSkillSlotHotkeys();
            RegisterHudButtonsSafe();
            // O BindLocalPlayer é chamado pelo NetworkPlayer.OnStartLocalPlayer.
        }

        private void Update()
        {
            // Hotkeys de janelas
            if (Input.GetKeyDown(KeyCode.I)) ToggleInventory();
            if (Input.GetKeyDown(KeyCode.C)) ToggleAttributes();
            if (Input.GetKeyDown(KeyCode.G)) TogglePowerGems();
            if (Input.GetKeyDown(KeyCode.Escape)) CloseAllWindows();
        }

        // ══════════════════════════════════════════════════════════════════
        // Bind do jogador local
        // ══════════════════════════════════════════════════════════════════

        public void BindLocalPlayer(NetworkPlayer player)
        {
            UnbindLocalPlayer();

            _player    = player;
            _inventory = player != null ? player.GetComponent<NetworkInventory>() : null;

            if (_player == null) return;

            // Sub eventos de UI
            _player.OnHPChanged          += RefreshHP;
            _player.OnMPChanged          += RefreshMP;
            _player.OnLevelChanged       += RefreshLevel;
            _player.OnExperienceChanged  += RefreshXP;
            _player.OnStatsChanged       += RefreshStatsHud;

            // Barra de skills atualiza quando muda Joia equipada
            if (_inventory != null)
                _inventory.OnEquipmentChanged += RefreshSkillBar;

            // Garante botões registrados (alguns prefabs entram só agora)
            RegisterHudButtonsSafe();

            // Vincula janelas
            if (_inventoryUI     != null) _inventoryUI.Bind(_player, _inventory);
            if (_attributeWindow != null) _attributeWindow.Bind(_player);
            if (_powerGemUI      != null) _powerGemUI.Bind(_inventory);
            if (_equipmentPanel  != null) _equipmentPanel.BindInventory(_inventory);

            RefreshAll();
        }

        private void UnbindLocalPlayer()
        {
            if (_player != null)
            {
                _player.OnHPChanged          -= RefreshHP;
                _player.OnMPChanged          -= RefreshMP;
                _player.OnLevelChanged       -= RefreshLevel;
                _player.OnExperienceChanged  -= RefreshXP;
                _player.OnStatsChanged       -= RefreshStatsHud;
            }
            if (_inventory != null)
                _inventory.OnEquipmentChanged -= RefreshSkillBar;

            _player    = null;
            _inventory = null;
        }

        // ══════════════════════════════════════════════════════════════════
        // Botões
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Registra listeners dos botões de HUD. Resistente à ordem de
        /// inicialização: se os botões ainda não estiverem atribuídos no
        /// inspector, tenta achar via FindObjectOfType.
        /// </summary>
        public void RegisterHudButtonsSafe()
        {
            if (_buttonsRegistered) return;

            // Fallback se inspector não atribuído
            if (_btnInventory  == null) _btnInventory  = FindButtonByName("BtnInventory");
            if (_btnAttributes == null) _btnAttributes = FindButtonByName("BtnAttributes");
            if (_btnPowerGems  == null) _btnPowerGems  = FindButtonByName("BtnPowerGems");

            bool any = false;
            if (_btnInventory != null)
            {
                _btnInventory.onClick.RemoveAllListeners();
                _btnInventory.onClick.AddListener(ToggleInventory);
                any = true;
            }
            if (_btnAttributes != null)
            {
                _btnAttributes.onClick.RemoveAllListeners();
                _btnAttributes.onClick.AddListener(ToggleAttributes);
                any = true;
            }
            if (_btnPowerGems != null)
            {
                _btnPowerGems.onClick.RemoveAllListeners();
                _btnPowerGems.onClick.AddListener(TogglePowerGems);
                any = true;
            }

            if (any) _buttonsRegistered = true;
        }

        private static Button FindButtonByName(string objName)
        {
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b != null && b.name == objName) return b;
            return null;
        }

        // ══════════════════════════════════════════════════════════════════
        // Toggles
        // ══════════════════════════════════════════════════════════════════

        public void ToggleInventory()    => _inventoryUI?.Toggle();
        public void ToggleAttributes()   => _attributeWindow?.Toggle();
        public void TogglePowerGems()    => _powerGemUI?.Toggle();

        public void CloseAllWindows()
        {
            _inventoryUI?.Close();
            _attributeWindow?.Close();
            _powerGemUI?.Close();
        }

        /// <summary>
        /// Chamado pelo InventoryUI quando o jogador clica "Equipar Joia"
        /// em uma joia do grid — abre a PowerGemUI no modo "escolher slot".
        /// </summary>
        public void OpenPowerGemUIForEquip(int invSlot, ItemData gem)
            => _powerGemUI?.OpenForEquip(invSlot, gem);

        // ══════════════════════════════════════════════════════════════════
        // Skill slots
        // ══════════════════════════════════════════════════════════════════

        private void ConfigureSkillSlotHotkeys()
        {
            if (_skillSlots == null) return;
            for (int i = 0; i < _skillSlots.Length; i++)
            {
                if (_skillSlots[i] == null) continue;
                string label = i < HOTKEY_LABELS.Length ? HOTKEY_LABELS[i] : (i + 1).ToString();
                _skillSlots[i].SetHotkey(label);
            }
        }

        public SkillSlotUI GetSkillSlot(int index)
        {
            if (_skillSlots == null || index < 0 || index >= _skillSlots.Length) return null;
            return _skillSlots[index];
        }

        /// <summary>
        /// Atualiza ícones dos 4 slots Q/W/E/R com base nas Joias equipadas.
        /// </summary>
        private void RefreshSkillBar()
        {
            if (_skillSlots == null || _inventory == null) return;

            var db = ItemDatabase.Instance;

            for (int i = 0; i < _skillSlots.Length; i++)
            {
                var slot = _skillSlots[i];
                if (slot == null) continue;

                string gemId = _inventory.GetEquippedPowerGem(i);
                if (string.IsNullOrEmpty(gemId))
                {
                    slot.SetIcon(null);
                    continue;
                }

                var gem = db?.GetItem(gemId);
                slot.SetIcon(gem?.Icon);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Refresh de status
        // ══════════════════════════════════════════════════════════════════

        private void RefreshAll()
        {
            if (_player == null) return;

            if (_nameText != null) _nameText.text = _player.CharacterName;

            RefreshHP();
            RefreshMP();
            RefreshLevel();
            RefreshXP();
            RefreshSkillBar();
        }

        private void RefreshHP()
        {
            if (_player == null) return;

            float max = Mathf.Max(1f, _player.MaxHP);
            float cur = Mathf.Clamp(_player.HP, 0f, max);

            if (_hpSlider != null)
            {
                _hpSlider.maxValue = max;
                _hpSlider.value    = cur;
            }
            if (_hpText != null) _hpText.text = $"{cur:0} / {max:0}";
        }

        private void RefreshMP()
        {
            if (_player == null) return;

            float max = Mathf.Max(1f, _player.MaxMP);
            float cur = Mathf.Clamp(_player.MP, 0f, max);

            if (_mpSlider != null)
            {
                _mpSlider.maxValue = max;
                _mpSlider.value    = cur;
            }
            if (_mpText != null) _mpText.text = $"{cur:0} / {max:0}";
        }

        private void RefreshLevel()
        {
            if (_player == null || _levelText == null) return;
            _levelText.text = $"Lv {_player.Level}";
        }

        private void RefreshXP()
        {
            if (_player == null) return;

            int exp    = _player.Experience;
            int toNext = _player.ExpToNextLevel;

            if (_xpSlider != null)
            {
                _xpSlider.minValue = 0;
                _xpSlider.maxValue = toNext > 0 ? toNext : 1;
                _xpSlider.value    = toNext > 0 ? exp : 1;
            }
            if (_xpText != null)
                _xpText.text = toNext > 0 ? $"{exp} / {toNext}" : $"{exp} (MAX)";
        }

        private void RefreshStatsHud()
        {
            // Stats mudaram (equip, alocação): rebarra HP/MP pois max muda
            RefreshHP();
            RefreshMP();
        }
    }
}
