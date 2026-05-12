using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.Data
{
    /// <summary>
    /// Dados persistentes de um personagem.
    /// Toda mudança que afeta stats derivados deve passar pelos setters/métodos
    /// públicos — campos públicos existem apenas por compatibilidade com serialização.
    /// </summary>
    [Serializable]
    public class CharacterData
    {
        public const int MAX_LEVEL              = 99;
        public const int MAX_ALLOCATED_PER_STAT = 300;
        public const int POINTS_PER_LEVEL_UP    = 5;

        public string        CharacterId;
        public string        CharacterName;
        public CharacterRace Race;

        public int RaceInt
        {
            get => (int)Race;
            set => Race = (CharacterRace)value;
        }

        private int _level = 1;
        public int Level
        {
            get => _level;
            set => _level = Math.Max(1, Math.Min(value, MAX_LEVEL));
        }

        public long Experience            = 0;
        public long ExperienceToNextLevel = 100;

        public BaseAttributes    BaseAttributes   = new BaseAttributes();
        public EquipmentBonuses  EquipmentBonuses = new EquipmentBonuses();

        public float  PosX, PosY, PosZ;
        public string CurrentMap = "World_01";

        public float CurrentHP;
        public float CurrentMP;

        public int FreeAttributePoints = 0;

        public int AllocatedSTR, AllocatedAGI, AllocatedVIT;
        public int AllocatedDEX, AllocatedINT, AllocatedLUK;

        public DerivedStats GetDerivedStats(BuffBonuses buff = null)
        {
            return StatsCalculator.Calculate(
                BaseAttributes,
                Level,
                Race,
                AllocatedSTR, AllocatedAGI, AllocatedVIT,
                AllocatedDEX, AllocatedINT, AllocatedLUK,
                EquipmentBonuses,
                buff);
        }

        /// <summary>
        /// Retorna o total de XP necessário para subir do nível atual.
        /// Static para permitir cálculos sem instanciar.
        /// </summary>
        public static long GetExperienceForLevel(int level)
        {
            int clamped = Math.Max(1, Math.Min(level, MAX_LEVEL));
            return (long)(100.0 * Math.Pow(clamped, 1.5));
        }

        /// <summary>
        /// Adiciona experiência e processa level-ups em cascata.
        /// Retorna true se houve ao menos um level up.
        ///
        /// Comportamento no nível máximo:
        ///   - XP atual é mantido para mostrar na UI ("99 — XP excedente perdido")
        ///   - ExpToNext fica em 0
        ///   - Tentativas subsequentes de adicionar XP são silenciosamente ignoradas
        /// </summary>
        public bool AddExperience(long amount)
        {
            if (amount <= 0) return false;
            if (Level >= MAX_LEVEL) return false;

            Experience += amount;
            bool leveled = false;

            while (Experience >= ExperienceToNextLevel && Level < MAX_LEVEL)
            {
                Experience          -= ExperienceToNextLevel;
                Level++;
                FreeAttributePoints += POINTS_PER_LEVEL_UP;

                ExperienceToNextLevel = Level >= MAX_LEVEL
                    ? 0L
                    : GetExperienceForLevel(Level);

                leveled = true;
            }

            // No nível máximo, descarta XP excedente sem zerar o histórico
            // (anteriormente zerava Experience, perdendo informação útil para UI).
            if (Level >= MAX_LEVEL)
            {
                ExperienceToNextLevel = 0;
                if (Experience < 0) Experience = 0;
            }

            return leveled;
        }

        /// <summary>
        /// Cria uma cópia completa e independente. Útil para snapshots
        /// e para evitar mutação acidental ao passar referências.
        /// </summary>
        public CharacterData Clone()
        {
            return new CharacterData
            {
                CharacterId           = CharacterId,
                CharacterName         = CharacterName,
                Race                  = Race,
                Level                 = Level,
                Experience            = Experience,
                ExperienceToNextLevel = ExperienceToNextLevel,
                PosX = PosX, PosY = PosY, PosZ = PosZ,
                CurrentMap            = CurrentMap,
                CurrentHP             = CurrentHP,
                CurrentMP             = CurrentMP,
                FreeAttributePoints   = FreeAttributePoints,
                AllocatedSTR = AllocatedSTR, AllocatedAGI = AllocatedAGI,
                AllocatedVIT = AllocatedVIT, AllocatedDEX = AllocatedDEX,
                AllocatedINT = AllocatedINT, AllocatedLUK = AllocatedLUK,
                BaseAttributes = new BaseAttributes
                {
                    STR = BaseAttributes.STR, AGI = BaseAttributes.AGI,
                    VIT = BaseAttributes.VIT, DEX = BaseAttributes.DEX,
                    INT = BaseAttributes.INT, LUK = BaseAttributes.LUK
                },
                EquipmentBonuses = new EquipmentBonuses
                {
                    STR             = EquipmentBonuses.STR,
                    AGI             = EquipmentBonuses.AGI,
                    VIT             = EquipmentBonuses.VIT,
                    DEX             = EquipmentBonuses.DEX,
                    INT             = EquipmentBonuses.INT,
                    LUK             = EquipmentBonuses.LUK,
                    ATK             = EquipmentBonuses.ATK,
                    DEF             = EquipmentBonuses.DEF,
                    MATK            = EquipmentBonuses.MATK,
                    MDEF            = EquipmentBonuses.MDEF,
                    HPBonus         = EquipmentBonuses.HPBonus,
                    MPBonus         = EquipmentBonuses.MPBonus,
                    ResistFire      = EquipmentBonuses.ResistFire,
                    ResistIce       = EquipmentBonuses.ResistIce,
                    ResistPoison    = EquipmentBonuses.ResistPoison,
                    ResistLightning = EquipmentBonuses.ResistLightning
                }
            };
        }
    }

    /// <summary>
    /// Container leve usado apenas em mensagens de rede.
    /// Personagens são carregados separadamente pelo DatabaseManager.
    /// </summary>
    [Serializable]
    public class AccountData
    {
        public string              Username;
        public string              PasswordHash;
        public List<CharacterData> Characters = new List<CharacterData>();
        public string              LastLogin;
    }
}
