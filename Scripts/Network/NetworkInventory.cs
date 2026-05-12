using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mirror;
using RPG.Data;
using RPG.Combat;
using RPG.UI;

namespace RPG.Network
{
    /// <summary>
    /// Inventário do jogador. Server-authoritative.
    ///
    /// Coleções:
    ///   - Slots: inventário livre (SyncList&lt;InventorySlotData&gt;).
    ///   - EquippedItems: itens equipados (SyncList&lt;EquippedItemData&gt;).
    ///   - GemSlotQ/W/E/R: SyncVars com IDs das Joias do Poder equipadas.
    ///
    /// Toda mutação passa por Commands e é validada no servidor.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class NetworkInventory : NetworkBehaviour
    {
        // ── Sincronização ──────────────────────────────────────────────────
        public readonly SyncList<InventorySlotData> Slots         = new SyncList<InventorySlotData>();
        public readonly SyncList<EquippedItemData>  EquippedItems = new SyncList<EquippedItemData>();

        [SyncVar(hook = nameof(OnGemSlotQChanged))] public string GemSlotQ = "";
        [SyncVar(hook = nameof(OnGemSlotWChanged))] public string GemSlotW = "";
        [SyncVar(hook = nameof(OnGemSlotEChanged))] public string GemSlotE = "";
        [SyncVar(hook = nameof(OnGemSlotRChanged))] public string GemSlotR = "";

        // ── Eventos (cliente) ──────────────────────────────────────────────
        public event Action OnInventoryChanged;
        public event Action OnGemLoadoutChanged;
        public event Action OnEquipmentChanged;

        // ── Estado do servidor ─────────────────────────────────────────────
        private int           _nextSlotIndex;
        private NetworkPlayer _netPlayer;

        // ── Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            _netPlayer = GetComponent<NetworkPlayer>();
        }

        public override void OnStartClient()
        {
            Slots.Callback         += OnSlotsChangedClient;
            EquippedItems.Callback += OnEquippedItemsChangedClient;
        }

        public override void OnStopClient()
        {
            Slots.Callback         -= OnSlotsChangedClient;
            EquippedItems.Callback -= OnEquippedItemsChangedClient;
        }

        public override void OnStartLocalPlayer()
        {
            StartCoroutine(BindUIDelayed());
        }

        private IEnumerator BindUIDelayed()
        {
            yield return null;
            yield return null;

            InventoryUI.Instance?.BindInventory(this);
            PowerGemUI.Instance?.BindInventory(this);
        }

        // ── Hooks ──────────────────────────────────────────────────────────

        private void OnSlotsChangedClient(SyncList<InventorySlotData>.Operation op,
                                          int index, InventorySlotData oldItem, InventorySlotData newItem)
            => OnInventoryChanged?.Invoke();

        private void OnEquippedItemsChangedClient(SyncList<EquippedItemData>.Operation op,
                                                  int index, EquippedItemData oldItem, EquippedItemData newItem)
            => OnEquipmentChanged?.Invoke();

        private void OnGemSlotQChanged(string oldVal, string newVal) => OnGemLoadoutChanged?.Invoke();
        private void OnGemSlotWChanged(string oldVal, string newVal) => OnGemLoadoutChanged?.Invoke();
        private void OnGemSlotEChanged(string oldVal, string newVal) => OnGemLoadoutChanged?.Invoke();
        private void OnGemSlotRChanged(string oldVal, string newVal) => OnGemLoadoutChanged?.Invoke();

        // ══════════════════════════════════════════════════════════════════
        // INVENTÁRIO — API do servidor
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Adiciona item. Retorna SlotIndex usado, ou -1 se falhou.</summary>
        [Server]
        public int ServerAddItem(string itemId, int quantity = 1)
        {
            if (string.IsNullOrEmpty(itemId)) return -1;

            var db = ItemDatabase.Instance;
            if (db == null || !db.Contains(itemId))
            {
                Debug.LogWarning($"[NetworkInventory] Item '{itemId}' não existe.");
                return -1;
            }

            var slot = new InventorySlotData
            {
                SlotIndex = _nextSlotIndex++,
                ItemId    = itemId,
                Quantity  = quantity
            };

            Slots.Add(slot);
            return slot.SlotIndex;
        }

        [Server]
        public bool ServerRemoveSlot(int slotIndex)
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].SlotIndex == slotIndex)
                {
                    Slots.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        [Server]
        public bool ServerRemoveItemById(string itemId)
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].ItemId == itemId)
                {
                    Slots.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public bool HasItem(string itemId)
        {
            foreach (var slot in Slots)
                if (slot.ItemId == itemId) return true;
            return false;
        }

        public int FindSlotByItemId(string itemId)
        {
            foreach (var slot in Slots)
                if (slot.ItemId == itemId) return slot.SlotIndex;
            return -1;
        }

        [Server]
        public void ServerLoadFromDatabase(string characterId)
        {
            var db = Managers.DatabaseManager.Instance;
            if (db == null) return;

            Slots.Clear();
            _nextSlotIndex = 0;

            var rows = db.LoadInventory(characterId);
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.ItemId)) continue;
                var slot = new InventorySlotData
                {
                    SlotIndex = row.SlotIndex >= 0 ? row.SlotIndex : _nextSlotIndex,
                    ItemId    = row.ItemId,
                    Quantity  = row.Quantity
                };
                Slots.Add(slot);
            }

            if (Slots.Count > 0)
                _nextSlotIndex = Slots.Max(s => s.SlotIndex) + 1;
        }

        [Server]
        public void ServerLoadGemLoadout(string characterId)
        {
            var db = Managers.DatabaseManager.Instance;
            if (db == null) return;

            var loadout = db.LoadGemLoadout(characterId);
            GemSlotQ = loadout.SlotQ ?? "";
            GemSlotW = loadout.SlotW ?? "";
            GemSlotE = loadout.SlotE ?? "";
            GemSlotR = loadout.SlotR ?? "";
        }

        [Server]
        public void ServerLoadEquippedFromDatabase(string characterId)
        {
            var db = Managers.DatabaseManager.Instance;
            if (db == null) return;

            EquippedItems.Clear();

            var rows = db.LoadEquipped(characterId);
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.ItemId)) continue;
                EquippedItems.Add(new EquippedItemData
                {
                    Slot          = (byte)row.Slot,
                    ItemId        = row.ItemId,
                    Durability    = row.Durability,
                    MaxDurability = row.MaxDurability
                });
            }
        }

        [Server]
        public void ServerSaveAll(string characterId, string username)
        {
            var db = Managers.DatabaseManager.Instance;
            if (db == null) return;

            db.SaveInventory(characterId, username, new List<InventorySlotData>(Slots));
            db.SaveGemLoadout(characterId, new PowerGemLoadout
            {
                SlotQ = GemSlotQ, SlotW = GemSlotW,
                SlotE = GemSlotE, SlotR = GemSlotR
            });
            db.SaveEquipped(characterId, new List<EquippedItemData>(EquippedItems));
        }

        // ══════════════════════════════════════════════════════════════════
        // EQUIPAMENTOS — leitura
        // ══════════════════════════════════════════════════════════════════

        [Server]
        private int ServerFindEquippedIndex(EquipmentSlot slot)
        {
            for (int i = 0; i < EquippedItems.Count; i++)
                if (EquippedItems[i].Slot == (byte)slot) return i;
            return -1;
        }

        [Server]
        public string ServerGetEquipped(EquipmentSlot slot)
        {
            int idx = ServerFindEquippedIndex(slot);
            return idx >= 0 ? EquippedItems[idx].ItemId : "";
        }

        public string GetEquipped(EquipmentSlot slot)
        {
            for (int i = 0; i < EquippedItems.Count; i++)
                if (EquippedItems[i].Slot == (byte)slot) return EquippedItems[i].ItemId;
            return "";
        }

        public bool IsSlotOccupied(EquipmentSlot slot) => !string.IsNullOrEmpty(GetEquipped(slot));

        // ══════════════════════════════════════════════════════════════════
        // EQUIPAMENTOS — Commands
        // ══════════════════════════════════════════════════════════════════

        [Command]
        public void CmdEquipItem(int inventorySlotIndex, byte targetSlotByte)
            => ServerEquipItem(inventorySlotIndex, targetSlotByte);

        [Command]
        public void CmdAutoEquip(int inventorySlotIndex)
            => ServerEquipItem(inventorySlotIndex, (byte)EquipmentSlot.None);

        [Command]
        public void CmdUnequipItem(byte slotByte)
        {
            if (_netPlayer == null || _netPlayer.Dead) return;

            EquipmentSlot slot = (EquipmentSlot)slotByte;
            int idx = ServerFindEquippedIndex(slot);
            if (idx < 0)
            {
                _netPlayer.RpcShowMessageToOwner("Slot já está vazio.");
                return;
            }

            string itemId = EquippedItems[idx].ItemId;
            if (string.IsNullOrEmpty(itemId))
            {
                EquippedItems.RemoveAt(idx);
                _netPlayer.ServerOnEquipmentChanged();
                return;
            }

            // Devolve ao inventário ANTES de remover do slot equipado
            int returnedSlot = ServerAddItem(itemId, 1);
            if (returnedSlot < 0)
            {
                _netPlayer.RpcShowMessageToOwner("Inventário cheio — não foi possível desequipar.");
                return;
            }

            EquippedItems.RemoveAt(idx);
            _netPlayer.ServerOnEquipmentChanged();
        }

        /// <summary>
        /// Equipa um item do inventário no slot escolhido (ou no slot natural se None).
        ///
        /// Ordem (atômica do ponto de vista do jogador):
        ///   1. Valida o item, o slot e os requisitos.
        ///   2. Lê dados do item antigo (se houver) ANTES de qualquer mutação.
        ///   3. Remove o item novo do inventário.
        ///   4. Devolve o item antigo ao inventário (se houver swap).
        ///   5. Atualiza EquippedItems.
        ///   6. Notifica o player para recalcular stats.
        ///
        /// Esta ordem garante que o item novo nunca apareça duplicado e o item
        /// antigo nunca seja perdido.
        /// </summary>
        [Server]
        private void ServerEquipItem(int inventorySlotIndex, byte targetSlotByte)
        {
            if (_netPlayer == null || _netPlayer.Dead) return;

            // 1) Encontra item no inventário
            if (!TryGetInventorySlot(inventorySlotIndex, out var foundSlot))
            {
                _netPlayer.RpcShowMessageToOwner("Item não encontrado no inventário.");
                return;
            }

            // 2) Valida tipo
            var itemData = ItemDatabase.Instance?.GetItem(foundSlot.ItemId);
            if (itemData == null || !itemData.IsEquipment)
            {
                _netPlayer.RpcShowMessageToOwner("Este item não pode ser equipado.");
                return;
            }

            // 3) Resolve slot final
            EquipmentSlot itemSlot   = itemData.EquipSlot;
            EquipmentSlot targetSlot = (EquipmentSlot)targetSlotByte;

            if (targetSlot == EquipmentSlot.None)
                targetSlot = ResolveAutoEquipSlot(itemSlot);

            if (!EquipmentSlotEx.IsActive(targetSlot))
            {
                _netPlayer.RpcShowMessageToOwner("Slot de equipamento inválido.");
                return;
            }

            if (!EquipmentSlotEx.CanItemFitInSlot(itemSlot, targetSlot))
            {
                _netPlayer.RpcShowMessageToOwner(
                    $"Este item não vai no slot {EquipmentSlotEx.DisplayName(targetSlot)}.");
                return;
            }

            // 4) Valida requisitos
            if (!ServerValidateRequirements(itemData, out string reason))
            {
                _netPlayer.RpcShowMessageToOwner(reason);
                return;
            }

            // 5) Snapshot do item antigo (se houver) ANTES de qualquer mutação
            int    existingIdx    = ServerFindEquippedIndex(targetSlot);
            string oldItemId      = "";
            if (existingIdx >= 0)
                oldItemId = EquippedItems[existingIdx].ItemId;

            // 6) Mutações em ordem segura:
            //    a) Remove o item NOVO do inventário primeiro (libera slot).
            //    b) Adiciona o item ANTIGO ao inventário (não pode falhar
            //       porque acabamos de liberar pelo menos 1 slot).
            //    c) Substitui o equipado.

            if (!ServerRemoveSlot(inventorySlotIndex))
            {
                Debug.LogError($"[NetworkInventory] ServerRemoveSlot({inventorySlotIndex}) falhou.");
                return;
            }

            if (!string.IsNullOrEmpty(oldItemId))
            {
                int returnedSlot = ServerAddItem(oldItemId, 1);
                if (returnedSlot < 0)
                {
                    // Caso patológico (item antigo não existe mais no banco).
                    // Devolve o item novo ao inventário e cancela.
                    Debug.LogError($"[NetworkInventory] Item antigo '{oldItemId}' não pôde voltar — abortando swap.");
                    ServerAddItem(itemData.ItemId, 1);
                    _netPlayer.RpcShowMessageToOwner("Erro ao trocar equipamento.");
                    return;
                }

                EquippedItems.RemoveAt(existingIdx);
            }

            // 7) Adiciona o item novo aos equipados
            int maxDur = Mathf.Max(0, itemData.MaxDurability);
            EquippedItems.Add(new EquippedItemData
            {
                Slot          = (byte)targetSlot,
                ItemId        = itemData.ItemId,
                Durability    = maxDur > 0 ? maxDur : -1,
                MaxDurability = maxDur
            });

            // 8) Recalcula stats
            _netPlayer.ServerOnEquipmentChanged();
        }

        [Server]
        private bool TryGetInventorySlot(int slotIndex, out InventorySlotData found)
        {
            foreach (var s in Slots)
            {
                if (s.SlotIndex == slotIndex)
                {
                    found = s;
                    return true;
                }
            }
            found = default;
            return false;
        }

        /// <summary>
        /// Valida requisitos de equip usando os atributos cacheados no NetworkPlayer.
        /// O race do player é parseado uma única vez via _netPlayer.GetRaceEnum().
        /// </summary>
        [Server]
        private bool ServerValidateRequirements(ItemData item, out string failReason)
        {
            failReason = null;
            if (item?.Requirements == null) return true;

            CharacterRace race  = _netPlayer.GetRaceEnum();
            var           bonus = StatsCalculator.GetRaceBonus(race);

            int totalSTR = _netPlayer.BaseSTR + bonus.STR + _netPlayer.AllocatedSTR;
            int totalAGI = _netPlayer.BaseAGI + bonus.AGI + _netPlayer.AllocatedAGI;
            int totalVIT = _netPlayer.BaseVIT + bonus.VIT + _netPlayer.AllocatedVIT;
            int totalDEX = _netPlayer.BaseDEX + bonus.DEX + _netPlayer.AllocatedDEX;
            int totalINT = _netPlayer.BaseINT + bonus.INT + _netPlayer.AllocatedINT;
            int totalLUK = _netPlayer.BaseLUK + bonus.LUK + _netPlayer.AllocatedLUK;

            return item.Requirements.Check(
                _netPlayer.Level,
                totalSTR, totalAGI, totalVIT, totalDEX, totalINT, totalLUK,
                race, out failReason);
        }

        /// <summary>
        /// Determina slot apropriado para auto-equip.
        /// Para anéis/brincos: prefere o primeiro slot livre.
        /// </summary>
        [Server]
        private EquipmentSlot ResolveAutoEquipSlot(EquipmentSlot itemSlot)
        {
            if (EquipmentSlotEx.IsRing(itemSlot))
            {
                if (string.IsNullOrEmpty(ServerGetEquipped(EquipmentSlot.Ring1))) return EquipmentSlot.Ring1;
                if (string.IsNullOrEmpty(ServerGetEquipped(EquipmentSlot.Ring2))) return EquipmentSlot.Ring2;
                return EquipmentSlot.Ring1; // ambos cheios — sobrescreve Ring1
            }

            if (EquipmentSlotEx.IsEarring(itemSlot))
            {
                if (string.IsNullOrEmpty(ServerGetEquipped(EquipmentSlot.Earring1))) return EquipmentSlot.Earring1;
                if (string.IsNullOrEmpty(ServerGetEquipped(EquipmentSlot.Earring2))) return EquipmentSlot.Earring2;
                return EquipmentSlot.Earring1;
            }

            return itemSlot;
        }

        // ══════════════════════════════════════════════════════════════════
        // JOIAS DO PODER — Commands
        // ══════════════════════════════════════════════════════════════════

        [Command]
        public void CmdEquipGem(int skillSlotIndex, int inventorySlotIndex)
        {
            if (skillSlotIndex < 0 || skillSlotIndex > 3) return;

            if (!TryGetInventorySlot(inventorySlotIndex, out var foundSlot)) return;

            var itemData = ItemDatabase.Instance?.GetItem(foundSlot.ItemId);
            if (itemData == null || !itemData.IsPowerGem) return;

            // Snapshot da joia antiga
            string currentGemId = GetGemItemId(skillSlotIndex);

            // Remove a joia nova do inventário
            ServerRemoveSlot(inventorySlotIndex);

            // Devolve a joia antiga (se houver)
            if (!string.IsNullOrEmpty(currentGemId))
                ServerAddItem(currentGemId, 1);

            ServerSetGemSlot(skillSlotIndex, foundSlot.ItemId);
        }

        [Command]
        public void CmdUnequipGem(int skillSlotIndex)
        {
            if (skillSlotIndex < 0 || skillSlotIndex > 3) return;

            string gemId = GetGemItemId(skillSlotIndex);
            if (string.IsNullOrEmpty(gemId)) return;

            int newSlot = ServerAddItem(gemId, 1);
            if (newSlot < 0)
            {
                Debug.LogError($"[NetworkInventory] Falha ao devolver '{gemId}' ao inventário.");
                return;
            }

            ServerSetGemSlot(skillSlotIndex, "");
        }

        [Server]
        private void ServerSetGemSlot(int index, string itemId)
        {
            switch (index)
            {
                case 0: GemSlotQ = itemId; break;
                case 1: GemSlotW = itemId; break;
                case 2: GemSlotE = itemId; break;
                case 3: GemSlotR = itemId; break;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // INVENTÁRIO — Commands diversos
        // ══════════════════════════════════════════════════════════════════

        [Command]
        public void CmdRemoveItem(int inventorySlotIndex)
        {
            ServerRemoveSlot(inventorySlotIndex);
        }

        [Command]
        public void CmdUseConsumable(int inventorySlotIndex)
        {
            if (!TryGetInventorySlot(inventorySlotIndex, out var foundSlot)) return;

            var itemData = ItemDatabase.Instance?.GetItem(foundSlot.ItemId);
            if (itemData == null || !itemData.IsConsumable) return;

            if (_netPlayer == null || _netPlayer.Dead) return;

            if (itemData.HealAmount > 0f) _netPlayer.ServerApplyHeal(itemData.HealAmount);
            if (itemData.ManaAmount > 0f) _netPlayer.ServerRestoreMP(itemData.ManaAmount);

            ServerRemoveSlot(foundSlot.SlotIndex);
        }

        // ══════════════════════════════════════════════════════════════════
        // JOIAS — Leitura
        // ══════════════════════════════════════════════════════════════════

        public string GetGemItemId(int skillSlotIndex) => skillSlotIndex switch
        {
            0 => GemSlotQ ?? "",
            1 => GemSlotW ?? "",
            2 => GemSlotE ?? "",
            3 => GemSlotR ?? "",
            _ => ""
        };

        public SkillData GetEquippedSkill(int skillSlotIndex)
        {
            string gemId = GetGemItemId(skillSlotIndex);
            if (string.IsNullOrEmpty(gemId)) return null;
            return ItemDatabase.Instance?.GetItem(gemId)?.EmbeddedSkill;
        }

        public int EquippedGemCount()
        {
            int count = 0;
            for (int i = 0; i < 4; i++)
                if (!string.IsNullOrEmpty(GetGemItemId(i))) count++;
            return count;
        }

        // ══════════════════════════════════════════════════════════════════
        // EQUIPAMENTO — Agregação de bônus
        // ══════════════════════════════════════════════════════════════════

        public EquipmentBonuses BuildEquipmentBonuses()
            => EquipmentSlotEx.AggregateBonuses(EquippedItems);

        public int EquippedItemCount() => EquippedItems.Count;
    }
}
