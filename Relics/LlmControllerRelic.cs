using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Rooms;
using FirstMod.Network;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace FirstMod.Relics
{
    public class LlmControllerRelic : RelicModel
    {
        public override RelicRarity Rarity => RelicRarity.Starter;
        public override bool IsAllowed(IRunState runState) => true;
        public override bool ShouldReceiveCombatHooks => true;

        private bool _isLoopRunning = false;

        private async Task SendLog(string message)
        {
            LogToFile(message);
            try { await LlmClient.Instance.SendEventOnlyAsync(new { type = "LOG", message = message }); } catch { }
        }

        private void LogToFile(string message)
        {
            try
            {
                string logPath = @"d:\WORK\ats2\mod example\sts2_example_mod\llm_debug.log";
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
                Console.WriteLine(message);
            }
            catch { }
        }

        public override async Task AfterObtained()
        {
            if (!_isLoopRunning)
            {
                _isLoopRunning = true;
                _ = GlobalDecisionLoopBackground();
            }
            await base.AfterObtained();
        }

        private int SafeGetIntentDamage(Creature enemy, CombatState combatState)
        {
            try {
                if (enemy.Monster?.NextMove?.Intents.FirstOrDefault() is AttackIntent attack)
                    return attack.GetSingleDamage(combatState.PlayerCreatures, enemy);
            } catch { }
            return 0;
        }

        private int SafeGetIntentRepeats(Creature enemy)
        {
            try {
                if (enemy.Monster?.NextMove?.Intents.FirstOrDefault() is AttackIntent attack)
                    return attack.Repeats;
            } catch { }
            return 0;
        }

        private List<object> PickPowers(Creature creature)
        {
            if (creature?.Powers == null) return new List<object>();
            return creature.Powers.Select(p => {
                string desc = "";
                try {
                    desc = p.HasSmartDescription ? p.SmartDescription.GetFormattedText() : p.Description.GetFormattedText();
                } catch { }
                return new {
                    id = p.Id.Entry ?? "",
                    amount = p.Amount,
                    description = desc
                };
            }).Cast<object>().ToList();
        }

        private async Task GlobalDecisionLoopBackground()
        {
            await SendLog("[LLM Mod] Global Decision Loop Started.");
            Player player = this.Owner as Player;

            while (this.Owner != null)
            {
                await Task.Delay(1000);

                await RunManager.Instance.ActionExecutor.FinishedExecutingActions();

                var state = RunManager.Instance.DebugOnlyGetState();
                if (state == null) continue;

                var currentRoom = state.CurrentRoom;
                if (currentRoom == null) continue;

                string scenario = "unknown";
                Dictionary<string, object> stateDict = new Dictionary<string, object>();
                
                var overlayScreen = NOverlayStack.Instance?.Peek();

                if ((NMapScreen.Instance?.IsOpen ?? false) && NMapScreen.Instance.IsTravelEnabled && !NMapScreen.Instance.IsTraveling)
                {
                    scenario = "map";
                    var actMap = state.Map;
                    stateDict["act_index"] = state.CurrentActIndex;
                    stateDict["boss"] = actMap?.BossMapPoint?.Quests?.FirstOrDefault()?.Id.Entry ?? "Unknown";
                    stateDict["visited"] = state.VisitedMapCoords.Select(c => new { x = c.col, y = c.row }).ToList();
                    stateDict["current_location"] = state.CurrentLocation.coord != null ? new { x = state.CurrentLocation.coord.Value.col, y = state.CurrentLocation.coord.Value.row } : null;
                    stateDict["map"] = SerializableActMap.FromActMap(actMap);
                }
                else if (overlayScreen is NRewardsScreen rewardsScreen)
                {
                    scenario = "reward";
                    var buttons = UiHelper.FindAll<NRewardButton>((Node)(object)rewardsScreen).Where(b => b.IsEnabled).ToList();
                    stateDict["rewards"] = buttons.Select((b, i) => new {
                        index = i,
                        type = b.Reward.GetType().Name
                    }).ToList();
                }
                else if (overlayScreen is NCardRewardSelectionScreen cardRewardScreen)
                {
                    scenario = "card_reward";
                    var holders = UiHelper.FindAll<NCardHolder>((Node)(object)cardRewardScreen);
                    stateDict["cards"] = holders.Select((h, i) => new {
                        index = i,
                        title = h.CardModel?.Title ?? "Unknown",
                        description = h.CardModel?.GetDescriptionForPile(PileType.None, null) ?? ""
                    }).ToList();
                }
                else if (overlayScreen is ICardSelector && overlayScreen is Godot.Node screenNode)
                {
                    scenario = "card_select";
                    var holders = UiHelper.FindAll<NCardHolder>(screenNode).Where(h => (h as Godot.CanvasItem)?.IsVisibleInTree() ?? false).ToList();
                    stateDict["cards"] = holders.Select((h, i) => new {
                        index = i,
                        title = h.CardModel?.Title ?? "Unknown",
                        description = h.CardModel?.GetDescriptionForPile(PileType.None, null) ?? ""
                    }).ToList();
                    var confirmBtn = UiHelper.FindAll<NConfirmButton>(screenNode).FirstOrDefault(b => b.IsEnabled && b.IsVisibleInTree());
                    stateDict["can_confirm"] = confirmBtn != null;
                    var skipBtn = UiHelper.FindAll<NChoiceSelectionSkipButton>(screenNode).FirstOrDefault(b => b.IsEnabled && b.IsVisibleInTree());
                    stateDict["can_skip"] = skipBtn != null;

                    // Support for Selection Limits and Selected Cards reading
                    try {
                        var screenType = screenNode.GetType();
                        var prefsField = screenType.GetField("_prefs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (prefsField != null) {
                            object prefsObj = prefsField.GetValue(screenNode);
                            if (prefsObj != null) {
                                var prefsType = prefsObj.GetType();
                                var minProp = prefsType.GetProperty("MinSelect");
                                var maxProp = prefsType.GetProperty("MaxSelect");
                                if (minProp != null) stateDict["min_select"] = minProp.GetValue(prefsObj);
                                if (maxProp != null) stateDict["max_select"] = maxProp.GetValue(prefsObj);
                            }
                        }
                        var selectedCardsField = screenType.GetField("_selectedCards", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (selectedCardsField != null) {
                            var scObj = selectedCardsField.GetValue(screenNode) as IEnumerable<CardModel>;
                            if (scObj != null) {
                                stateDict["selected_cards"] = scObj.Select(c => c.Title).ToList();
                            }
                        }
                    } catch { }
                }
                else if (currentRoom.RoomType == RoomType.Event && NEventRoom.Instance != null && NEventRoom.Instance.IsInsideTree() && (NOverlayStack.Instance == null || NOverlayStack.Instance.ScreenCount == 0))
                {
                    scenario = "event";
                    var buttons = UiHelper.FindAll<NEventOptionButton>((Node)(object)NEventRoom.Instance).Where(b => b.IsEnabled && !b.Option.IsLocked).ToList();
                    stateDict["event_options"] = buttons.Select((b, i) => new {
                        index = i,
                        title = b.Option.Title?.GetRawText() ?? "Unknown",
                        desc = b.Option.Description?.GetRawText() ?? ""
                    }).ToList();
                }
                else if (currentRoom.RoomType == RoomType.Shop && NMerchantRoom.Instance != null && NMerchantRoom.Instance.IsInsideTree() && (NOverlayStack.Instance == null || NOverlayStack.Instance.ScreenCount == 0))
                {
                    scenario = "shop";
                    var room = NMerchantRoom.Instance;
                    stateDict["inventory_open"] = room.Inventory?.IsOpen ?? false;
                    
                    if (room.Inventory?.IsOpen ?? false)
                    {
                        var slots = room.Inventory.GetAllSlots().ToList();
                        stateDict["items"] = slots.Select((s, i) => {
                            string title = "Unknown";
                            string desc = "";
                            if (s.Entry is MerchantCardEntry cardEntry) { 
                                title = cardEntry.CreationResult?.Card?.Title ?? "Unknown Card"; 
                                desc = cardEntry.CreationResult?.Card?.GetDescriptionForPile(PileType.None, null) ?? "";
                            }
                            else if (s.Entry is MerchantRelicEntry relicEntry) { 
                                  try { title = relicEntry.Model?.Title?.GetFormattedText() ?? "Unknown Relic"; } catch { title = "Unknown Relic"; }
                                  try { desc = relicEntry.Model?.DynamicDescription?.GetFormattedText() ?? ""; } catch { desc = ""; }
                              }
                              else if (s.Entry is MerchantPotionEntry potionEntry) {
                                  try { title = potionEntry.Model?.Title?.GetFormattedText() ?? "Unknown Potion"; } catch { title = "Unknown Potion"; }
                                  try { desc = potionEntry.Model?.DynamicDescription?.GetFormattedText() ?? ""; } catch { desc = ""; }
                            }
                            else if (s.Entry is MerchantCardRemovalEntry) title = "Card Removal";
                            
                            return new {
                                index = i,
                                title = title,
                                type = s.Entry.GetType().Name.Replace("Merchant", "").Replace("Entry", ""),
                                cost = s.Entry.Cost,
                                can_afford = s.Entry.EnoughGold,
                                is_stocked = s.Entry.IsStocked
                            };
                        }).ToList();
                    }
                }
                else if (currentRoom.RoomType.IsCombatRoom() && CombatManager.Instance.IsPlayPhase)
                {
                    scenario = "combat";
                    CombatState combatState = CombatManager.Instance.DebugOnlyGetState();
                    stateDict["hp"] = player.Creature.CurrentHp;
                    stateDict["max_hp"] = player.Creature.MaxHp;
                    stateDict["energy"] = player.PlayerCombatState?.Energy ?? 0;
                    
                    stateDict["hand"] = player.PlayerCombatState?.Hand.Cards.Select((c, i) => new { 
                        uuid = c.Id.Entry ?? "", index = i, title = c.Title, cost = c.EnergyCost.GetResolved(), target_type = c.TargetType.ToString(),
                        description = c.GetDescriptionForPile(PileType.None, null)
                    }).Cast<object>().ToList() ?? new List<object>();

                    stateDict["draw_pile"] = player.PlayerCombatState?.DrawPile.Cards.Select(c => c.Title).ToList() ?? new List<string>();
                    stateDict["discard_pile"] = player.PlayerCombatState?.DiscardPile.Cards.Select(c => c.Title).ToList() ?? new List<string>();
                    stateDict["exhaust_pile"] = player.PlayerCombatState?.ExhaustPile.Cards.Select(c => c.Title).ToList() ?? new List<string>();
                    // using Owner.Deck instead of Player.Deck just to be safe
                    stateDict["deck"] = player.Deck?.Cards.Select(c => c.Title).ToList() ?? new List<string>();

                    stateDict["player_powers"] = PickPowers(player.Creature);

                    stateDict["enemies"] = combatState.HittableEnemies.Select((e, i) => new {
                        uuid = e.ModelId.Entry ?? "", index = i, title = e.Name ?? "", hp = e.CurrentHp, max_hp = e.MaxHp, block = e.Block,
                        intent = e.Monster?.NextMove?.Intents.FirstOrDefault()?.IntentType.ToString() ?? "None",
                        intent_damage = SafeGetIntentDamage(e, combatState), intent_repeats = SafeGetIntentRepeats(e),
                        powers = PickPowers(e)
                    }).ToList();
                }
                
                // Fetch Relics and Potions anytime
                stateDict["gold"] = player.Gold;
stateDict["relics"] = (player.Relics?.Select(r => {
                      string desc = "";
                      try { desc = r.DynamicDescription?.GetFormattedText() ?? ""; } catch { }
                      return new {
                          id = r.Id.Entry ?? "",
                          description = desc
                      };
                  }).Cast<object>().ToList()) ?? new List<object>();

                  stateDict["potions"] = (player.Potions?.Select(p => {
                      string desc = "";
                      try { desc = p.DynamicDescription?.GetFormattedText() ?? ""; } catch { }
                      return new {
                          id = p.Id.Entry ?? "",
                          target_type = p.TargetType.ToString(),
                          can_use = p.PassesCustomUsabilityCheck,
                          description = desc
                      };
                }).Cast<object>().ToList()) ?? new List<object>();

                if (scenario == "unknown") continue;
                stateDict["scenario"] = scenario;

                try 
                {
                    string json = await LlmClient.Instance.SendStateAndGetResponseAsync(stateDict);
                    if (string.IsNullOrEmpty(json)) continue;

                    using JsonDocument doc = JsonDocument.Parse(json);
                    string actionStr = doc.RootElement.GetProperty("action").GetString();

                    if (scenario == "combat" && actionStr == "play_card")
                    {
                        int cardIdx = doc.RootElement.GetProperty("card_idx").GetInt32();
                        var handCards = player.PlayerCombatState?.Hand.Cards;
                        if (handCards != null && cardIdx >= 0 && cardIdx < handCards.Count)
                        {
                            var targetIdxProp = doc.RootElement.GetProperty("target_idx");
                            Creature targetEnemy = null;
                            if (targetIdxProp.ValueKind != JsonValueKind.Null) {
                                var enemies = CombatManager.Instance.DebugOnlyGetState().HittableEnemies;
                                int ti = targetIdxProp.GetInt32();
                                if(ti >= 0 && ti < enemies.Count) targetEnemy = enemies[ti];
                            }
                            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(handCards[cardIdx], targetEnemy));
                        }
                    }
                    else if (actionStr == "use_potion")
                    {
                        int potionIdx = doc.RootElement.GetProperty("potion_idx").GetInt32();
                        var potions = player.Potions as List<PotionModel> ?? player.Potions?.ToList();
                        if (potions != null && potionIdx >= 0 && potionIdx < potions.Count)
                        {
                            var potion = potions[potionIdx];
                            if (potion.PassesCustomUsabilityCheck)
                            {
                                var targetIdxProp = doc.RootElement.GetProperty("target_idx");
                                Creature targetEnemy = null;
                                if (targetIdxProp.ValueKind != JsonValueKind.Null && targetIdxProp.ValueKind != JsonValueKind.Undefined && CombatManager.Instance.IsInProgress) {
                                    var enemies = CombatManager.Instance.DebugOnlyGetState()?.HittableEnemies;
                                    int ti = targetIdxProp.GetInt32();
                                    if(enemies != null && ti >= 0 && ti < enemies.Count) targetEnemy = enemies[ti];
                                }
                                potion.EnqueueManualUse(targetEnemy);
                            }
                        }
                    }
                    else if (actionStr == "discard_potion")
                    {
                        int potionIdx = doc.RootElement.GetProperty("potion_idx").GetInt32();
                        var potions = player.Potions as List<PotionModel> ?? player.Potions?.ToList();
                        if (potions != null && potionIdx >= 0 && potionIdx < potions.Count)
                        {
                            var potion = potions[potionIdx];
                            potion.Discard();
                        }
                    }
                    else if (scenario == "combat" && actionStr == "end_turn")
                    {
                        var cs = CombatManager.Instance.DebugOnlyGetState();
                        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(player, cs.RoundNumber));
                    }
                    else if (scenario == "map" && actionStr == "choose_map_node")
                    {
                        int x = doc.RootElement.GetProperty("x").GetInt32();
                        int y = doc.RootElement.GetProperty("y").GetInt32();
                        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new MoveToMapCoordAction(player, new MapCoord(x, y)));
                    }
                    else if (scenario == "reward" && actionStr == "take_reward")
                    {
                        int idx = doc.RootElement.GetProperty("index").GetInt32();
                        var screen = NOverlayStack.Instance?.Peek() as NRewardsScreen;
                        var buttons = UiHelper.FindAll<NRewardButton>((Node)(object)screen).Where(b => b.IsEnabled).ToList();
                        if (idx >= 0 && idx < buttons.Count) {
                            await UiHelper.Click(buttons[idx]);
                        }
                    }
                    else if (scenario == "reward" && actionStr == "skip_rewards")
                    {
                        var screen = NOverlayStack.Instance?.Peek() as NRewardsScreen;
                        var proceedBtn = UiHelper.FindFirst<NProceedButton>((Node)(object)screen);
                        if (proceedBtn != null && proceedBtn.IsEnabled) {
                            await UiHelper.Click(proceedBtn);
                        }
                    }
                    else if (scenario == "card_reward" && actionStr == "choose_card")
                    {
                        int idx = doc.RootElement.GetProperty("index").GetInt32();
                        var screen = NOverlayStack.Instance?.Peek() as NCardRewardSelectionScreen;
                        var holders = UiHelper.FindAll<NCardHolder>((Node)(object)screen);
                        if (idx >= 0 && idx < holders.Count) {
                            var holder = holders[idx];
                            ((GodotObject)holder).EmitSignal(NCardHolder.SignalName.Pressed, Godot.Variant.CreateFrom((GodotObject)(object)holder));
                        }
                    }
                    else if (scenario == "card_reward" && actionStr == "skip_rewards")
                    {
                        var screen = NOverlayStack.Instance?.Peek() as NCardRewardSelectionScreen;
                        if (screen != null) {
                            var skipBtns = UiHelper.FindAll<NCardRewardAlternativeButton>((Node)(object)screen).Where(b => b.IsEnabled).ToList();
                            NCardRewardAlternativeButton targetBtn = null;
                            foreach(var btn in skipBtns) {
                                try {
                                    var optField = btn.GetType().GetField("_optionName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                    if (optField != null) {
                                        string optName = optField.GetValue(btn) as string;
                                        if (optName != null && (optName.Contains("跳过") || optName.ToLower().Contains("skip"))) {
                                            targetBtn = btn;
                                            break;
                                        }
                                    }
                                } catch { }
                            }
                            if (targetBtn == null && skipBtns.Count > 0) targetBtn = skipBtns.FirstOrDefault();
                            if (targetBtn != null) await UiHelper.Click(targetBtn);
                        }
                    }
                    else if (scenario == "card_select" && actionStr == "choose_card")
                    {
                        int idx = doc.RootElement.GetProperty("index").GetInt32();
                        var screenNode = NOverlayStack.Instance?.Peek() as Godot.Node;
                        if (screenNode != null) {
                            var holders = UiHelper.FindAll<NCardHolder>(screenNode).Where(h => (h as Godot.CanvasItem)?.IsVisibleInTree() ?? false).ToList();
                            await SendLog($"[LLM Mod] Attempting to select card {idx} out of {holders.Count} on screen {screenNode.GetType().Name}");
                            if (idx >= 0 && idx < holders.Count) {
                                var holder = holders[idx];
                                try {
                                    bool isTransformOrSimple = screenNode.GetType().Name.Contains("Transform") || screenNode.GetType().Name.Contains("SimpleCardSelect");
                                    var grid = UiHelper.FindFirst<MegaCrit.Sts2.Core.Nodes.Cards.NCardGrid>(screenNode);
                                    if (grid != null && isTransformOrSimple) {
                                        await SendLog($"[LLM Mod] Emitting target on NCardGrid for {holder.CardModel?.Title}");
                                        ((Godot.GodotObject)grid).EmitSignal(MegaCrit.Sts2.Core.Nodes.Cards.NCardGrid.SignalName.HolderPressed, new Godot.Variant[] { Godot.Variant.CreateFrom((Godot.GodotObject)(object)holder) });
                                    } else {
                                        await SendLog($"[LLM Mod] Emitting Pressed on NCardHolder for {holder.CardModel?.Title}");
                                        ((Godot.GodotObject)holder).EmitSignal(NCardHolder.SignalName.Pressed, new Godot.Variant[] { Godot.Variant.CreateFrom((Godot.GodotObject)(object)holder) });
                                    }
                                    await SendLog("[LLM Mod] Signal emitted successfully.");
                                } catch (Exception e) {
                                    await SendLog($"[LLM Mod] Failed to click card: {e.Message}");
                                }
                            } else {
                                await SendLog("[LLM Mod] Invalid index for cards.");
                            }
                        }
                    }
                    else if (scenario == "card_select" && actionStr == "confirm_selection")
                    {
                        var screenNode = NOverlayStack.Instance?.Peek() as Godot.Node;
                        if (screenNode != null) {
                            var confirmBtn = UiHelper.FindAll<NConfirmButton>(screenNode).FirstOrDefault(b => b.IsEnabled && b.IsVisibleInTree());
                            if (confirmBtn != null) {
                                await SendLog("[LLM Mod] Clicking confirm button.");
                                await UiHelper.Click(confirmBtn);
                            } else {
                                await SendLog("[LLM Mod] Confirm button not found or enabled.");
                            }
                        }
                    }
                    else if (scenario == "card_select" && actionStr == "skip_selection")
                    {
                        var screenNode = NOverlayStack.Instance?.Peek() as Godot.Node;
                        if (screenNode != null) {
                            var skipBtn = UiHelper.FindAll<NChoiceSelectionSkipButton>(screenNode).FirstOrDefault(b => b.IsEnabled && b.IsVisibleInTree());
                            if (skipBtn != null) {
                                await SendLog("[LLM Mod] Clicking skip button.");
                                await UiHelper.Click(skipBtn);
                            } else {
                                await SendLog("[LLM Mod] Skip button not found or enabled.");
                            }
                        }
                    }
                    else if (scenario == "event" && actionStr == "choose_event")
                    {
                        int idx = doc.RootElement.GetProperty("index").GetInt32();
                        var buttons = UiHelper.FindAll<NEventOptionButton>((Node)(object)NEventRoom.Instance).Where(b => b.IsEnabled && !b.Option.IsLocked).ToList();
                        if (idx >= 0 && idx < buttons.Count) {
                            await UiHelper.Click(buttons[idx]);
                        }
                    }
                    else if (scenario == "shop" && actionStr == "open_inventory")
                    {
                        if (NMerchantRoom.Instance != null && !(NMerchantRoom.Instance.Inventory?.IsOpen ?? false))
                        {
                            NMerchantRoom.Instance.OpenInventory();
                        }
                    }
                    else if (scenario == "shop" && actionStr == "buy_item")
                    {
                        int idx = doc.RootElement.GetProperty("index").GetInt32();
                        var room = NMerchantRoom.Instance;
                        if (room != null && (room.Inventory?.IsOpen ?? false))
                        {
                            var slots = room.Inventory.GetAllSlots().ToList();
                            if (idx >= 0 && idx < slots.Count)
                            {
                                var slot = slots[idx];
                                if (slot.Entry.IsStocked && slot.Entry.EnoughGold) {
                                    await slot.Entry.OnTryPurchaseWrapper(room.Inventory.Inventory);
                                }
                            }
                        }
                    }
                    else if (scenario == "shop" && actionStr == "leave_shop")
                    {
                        var room = NMerchantRoom.Instance;
                        if (room != null)
                        {
                            if (room.Inventory?.IsOpen ?? false)
                            {
                                var nBackButton = UiHelper.FindFirst<NBackButton>((Node)(object)room);
                                if (nBackButton != null) {
                                    await UiHelper.Click(nBackButton);
                                    await Task.Delay(300); // give time for the inventory to close visually before the next loop
                                }
                            }
                            else
                            {
                                if (room.ProceedButton != null) {
                                    await UiHelper.Click(room.ProceedButton);
                                }
                            }
                        }
                    }                } catch(Exception ex) { await SendLog("[LLM Mod Error] " + ex.Message); }
            }
        }
    }
}
