using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RPG.Data;
using RPG.Network;
using System.Text;

// Resolve ambiguidade com UnityEngine.NetworkPlayer (UNet legado)
using NetworkPlayer = RPG.Network.NetworkPlayer;

namespace RPG.UI
{
    /// <summary>
    /// Índice de atributo enviado a <see cref="NetworkPlayer.CmdAllocateAttribute"/>.
    /// A ordem (0..5) DEVE coincidir com o switch do servidor.
    /// </summary>
    internal enum AttributeType
    {
        STR = 0,
        AGI = 1,
        VIT = 2,
        DEX = 3,
        INT = 4,
        LUK = 5
    }

    /// <summary>
    /// Janela de atributos (C). Exibe base+race+allocated, permite gastar pontos
    /// e mostra stats secundários.
    ///
    /// Pontos pendentes: lê de NetworkPlayer.AttributePoints. Mostra botões "+"
    /// apenas para o jogador local e quando há pontos.
    /// </summary>
    public class AttributeWindowUI : MonoBehaviour
    {
        [Header("Painel")]
        [SerializeField] private GameObject _root;

        [Header("Cabeçalho")]
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _classRaceText;
        [SerializeField] private TMP_Text _levelText;
        [SerializeField] private TMP_Text _xpText;
        [SerializeField] private TMP_Text _pointsText;
        [SerializeField] private Slider   _xpSlider;

        [Header("Atributos primários (texto)")]
        [SerializeField] private TMP_Text _strText;
        [SerializeField] private TMP_Text _agiText;
        [SerializeField] private TMP_Text _vitText;
        [SerializeField] private TMP_Text _dexText;
        [SerializeField] private TMP_Text _intText;
        [SerializeField] private TMP_Text _lukText;

        [Header("Botões '+' para alocar")]
        [SerializeField] private Button _btnAddSTR;
        [SerializeField] private Button _btnAddAGI;
        [SerializeField] private Button _btnAddVIT;
        [SerializeField] private Button _btnAddDEX;
        [SerializeField] private Button _btnAddINT;
        [SerializeField] private Button _btnAddLUK;

        [Header("Stats secundárias")]
        [SerializeField] private TMP_Text _statsText;

        [Header("Botão fechar")]
        [SerializeField] private Button _btnClose;

        // ── Estado ─────────────────────────────────────────────────────────
        private NetworkPlayer _player;
        private StringBuilder _sb = new StringBuilder(512);

        // ══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (_root != null) _root.SetActive(false);

            if (_btnAddSTR != null) _btnAddSTR.onClick.AddListener(() => TryAllocate(AttributeType.STR));
            if (_btnAddAGI != null) _btnAddAGI.onClick.AddListener(() => TryAllocate(AttributeType.AGI));
            if (_btnAddVIT != null) _btnAddVIT.onClick.AddListener(() => TryAllocate(AttributeType.VIT));
            if (_btnAddDEX != null) _btnAddDEX.onClick.AddListener(() => TryAllocate(AttributeType.DEX));
            if (_btnAddINT != null) _btnAddINT.onClick.AddListener(() => TryAllocate(AttributeType.INT));
            if (_btnAddLUK != null) _btnAddLUK.onClick.AddListener(() => TryAllocate(AttributeType.LUK));

            if (_btnClose != null) _btnClose.onClick.AddListener(Close);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        // ══════════════════════════════════════════════════════════════════
        // Bind / Open / Close
        // ══════════════════════════════════════════════════════════════════

        public void Bind(NetworkPlayer player)
        {
            Unbind();

            _player = player;
            if (_player == null) return;

            _player.OnStatsChanged       += RefreshStats;
            _player.OnLevelChanged       += RefreshLevel;
            _player.OnExperienceChanged  += RefreshExperience;
            _player.OnAttributesChanged  += RefreshAttributes;
            _player.OnPointsChanged      += RefreshPoints;

            RefreshAll();
        }

        private void Unbind()
        {
            if (_player == null) return;
            _player.OnStatsChanged       -= RefreshStats;
            _player.OnLevelChanged       -= RefreshLevel;
            _player.OnExperienceChanged  -= RefreshExperience;
            _player.OnAttributesChanged  -= RefreshAttributes;
            _player.OnPointsChanged      -= RefreshPoints;
            _player = null;
        }

        public void Open()
        {
            if (_root != null) _root.SetActive(true);
            RefreshAll();
        }

        public void Close()
        {
            if (_root != null) _root.SetActive(false);
        }

        public void Toggle()
        {
            if (_root == null) return;
            if (_root.activeSelf) Close(); else Open();
        }

        // ══════════════════════════════════════════════════════════════════
        // Refresh
        // ══════════════════════════════════════════════════════════════════

        private void RefreshAll()
        {
            if (_player == null) return;
            RefreshHeader();
            RefreshAttributes();
            RefreshPoints();
            RefreshStats();
        }

        private void RefreshHeader()
        {
            if (_player == null) return;

            if (_nameText != null)
                _nameText.text = _player.CharacterName;

            if (_classRaceText != null)
                _classRaceText.text = _player.GetRaceEnum().ToString();

            RefreshLevel();
            RefreshExperience();
        }

        private void RefreshLevel()
        {
            if (_player == null || _levelText == null) return;
            _levelText.text = $"Lv {_player.Level}";
        }

        private void RefreshExperience()
        {
            if (_player == null) return;

            int exp     = _player.Experience;
            int toNext  = _player.ExpToNextLevel;

            if (_xpText != null)
                _xpText.text = toNext > 0 ? $"{exp} / {toNext}" : $"{exp} (MAX)";

            if (_xpSlider != null)
            {
                _xpSlider.minValue = 0;
                _xpSlider.maxValue = toNext > 0 ? toNext : 1;
                _xpSlider.value    = toNext > 0 ? exp : 1;
            }
        }

        private void RefreshAttributes()
        {
            if (_player == null) return;

            var race      = _player.GetRaceEnum();
            var raceBonus = StatsCalculator.GetRaceBonus(race);

            SetAttrText(_strText, "STR",
                _player.BaseSTR, raceBonus.STR, _player.AllocatedSTR);
            SetAttrText(_agiText, "AGI",
                _player.BaseAGI, raceBonus.AGI, _player.AllocatedAGI);
            SetAttrText(_vitText, "VIT",
                _player.BaseVIT, raceBonus.VIT, _player.AllocatedVIT);
            SetAttrText(_dexText, "DEX",
                _player.BaseDEX, raceBonus.DEX, _player.AllocatedDEX);
            SetAttrText(_intText, "INT",
                _player.BaseINT, raceBonus.INT, _player.AllocatedINT);
            SetAttrText(_lukText, "LUK",
                _player.BaseLUK, raceBonus.LUK, _player.AllocatedLUK);
        }

        private static void SetAttrText(TMP_Text field, string label,
                                        int baseVal, int raceBonus, int allocated)
        {
            if (field == null) return;
            int total = baseVal + raceBonus + allocated;

            if (raceBonus != 0 || allocated != 0)
            {
                string raceStr = raceBonus != 0 ? $"<color=#88FF88>+{raceBonus}</color>" : "";
                string allStr  = allocated != 0 ? $"<color=#FFCC66>+{allocated}</color>" : "";
                field.text = $"{label}: <b>{total}</b> ({baseVal}{raceStr}{allStr})";
            }
            else
            {
                field.text = $"{label}: <b>{total}</b>";
            }
        }

        private void RefreshPoints()
        {
            if (_player == null) return;

            int pts = _player.AttributePoints;

            if (_pointsText != null)
                _pointsText.text = $"Pontos: <b>{pts}</b>";

            bool isLocal = _player.isLocalPlayer;
            bool canSpend = isLocal && pts > 0;

            SetBtn(_btnAddSTR, canSpend && CanAllocateMore(_player.AllocatedSTR));
            SetBtn(_btnAddAGI, canSpend && CanAllocateMore(_player.AllocatedAGI));
            SetBtn(_btnAddVIT, canSpend && CanAllocateMore(_player.AllocatedVIT));
            SetBtn(_btnAddDEX, canSpend && CanAllocateMore(_player.AllocatedDEX));
            SetBtn(_btnAddINT, canSpend && CanAllocateMore(_player.AllocatedINT));
            SetBtn(_btnAddLUK, canSpend && CanAllocateMore(_player.AllocatedLUK));
        }

        private static bool CanAllocateMore(int allocated)
            => allocated < CharacterData.MAX_ALLOCATED_PER_STAT;

        private static void SetBtn(Button btn, bool interactable)
        {
            if (btn == null) return;
            btn.gameObject.SetActive(true);
            btn.interactable = interactable;
        }

        private void RefreshStats()
        {
            if (_player == null || _statsText == null) return;

            _sb.Clear();
            _sb.AppendLine($"HP: <b>{_player.HP:0} / {_player.MaxHP:0}</b>");
            _sb.AppendLine($"MP: <b>{_player.MP:0} / {_player.MaxMP:0}</b>");
            _sb.AppendLine();
            _sb.AppendLine($"ATK: {_player.ATK:0.#}");
            _sb.AppendLine($"DEF: {_player.DEF:0.#}");
            _sb.AppendLine($"MATK: {_player.MATK:0.#}");
            _sb.AppendLine($"MDEF: {_player.MDEF:0.#}");
            _sb.AppendLine();
            _sb.AppendLine($"Crit: {_player.CritChance:0.#}%");
            _sb.AppendLine($"Esquiva: {_player.DodgeChance:0.#}%");
            _sb.AppendLine($"Velocidade Atq: {_player.AttackSpeed:0.##}");
            _sb.AppendLine($"Velocidade Mov: {_player.MoveSpeed:0.##}");

            _statsText.text = _sb.ToString().TrimEnd();
        }

        // ══════════════════════════════════════════════════════════════════
        // Ações
        // ══════════════════════════════════════════════════════════════════

        private void TryAllocate(AttributeType attr)
        {
            if (_player == null || !_player.isLocalPlayer) return;
            if (_player.AttributePoints <= 0) return;
            _player.CmdAllocateAttribute((int)attr);
        }
    }
}
