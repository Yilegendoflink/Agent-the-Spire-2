import sys

with open(r'd:\WORK\ats2\mod example\sts2_example_mod\Relics\LlmControllerRelic.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Add SendLog
content = content.replace(
    'private void LogToFile(string message)',
    'private async Task SendLog(string message)\n        {\n            LogToFile(message);\n            try { await LlmClient.Instance.SendEventOnlyAsync(new { type = "LOG", message = message }); } catch { }\n        }\n\n        private void LogToFile(string message)'
)

# Replace all LogToFile with await SendLog inside the loop
content = content.replace('LogToFile("[LLM Mod] Global Decision Loop Started.");', 'await SendLog("[LLM Mod] Global Decision Loop Started.");')

# Catch errors using SendLog
content = content.replace('} catch(Exception ex) { LogToFile("[LLM Mod Error] " + ex.Message); }', '} catch(Exception ex) { await SendLog("[LLM Mod Error] " + ex.Message); }')

# Add Gold
content = content.replace(
    'stateDict["relics"]',
    'stateDict["gold"] = player.Gold;\n                stateDict["relics"]'
)

# Enhance choose_card in card_select
old_choose_card = '''                    else if (scenario == "card_select" && actionStr == "choose_card")
                    {
                        int idx = doc.RootElement.GetProperty("index").GetInt32();
                        var screenNode = NOverlayStack.Instance?.Peek() as Godot.Node;
                        if (screenNode != null) {
                            var holders = UiHelper.FindAll<NCardHolder>(screenNode).Where(h => (h as Godot.CanvasItem)?.IsVisibleInTree() ?? false).ToList();
                            if (idx >= 0 && idx < holders.Count) {
                                var holder = holders[idx];
                                ((GodotObject)holder).EmitSignal(NCardHolder.SignalName.Pressed, Godot.Variant.CreateFrom((GodotObject)(object)holder));
                            }
                        }
                    }'''

new_choose_card = '''                    else if (scenario == "card_select" && actionStr == "choose_card")
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
                                        ((Godot.GodotObject)grid).EmitSignal(MegaCrit.Sts2.Core.Nodes.Cards.NCardGrid.SignalName.HolderPressed, new Godot.Variant[] { Godot.Variant.op_Implicit((Godot.GodotObject)(object)holder) });
                                    } else {
                                        await SendLog($"[LLM Mod] Emitting Pressed on NCardHolder for {holder.CardModel?.Title}");
                                        ((Godot.GodotObject)holder).EmitSignal(NCardHolder.SignalName.Pressed, new Godot.Variant[] { Godot.Variant.op_Implicit((Godot.GodotObject)(object)holder) });
                                    }
                                    await SendLog("[LLM Mod] Signal emitted successfully.");
                                } catch (Exception e) {
                                    await SendLog($"[LLM Mod] Failed to click card: {e.Message}");
                                }
                            } else {
                                await SendLog("[LLM Mod] Invalid index for cards.");
                            }
                        }
                    }'''
content = content.replace(old_choose_card, new_choose_card)

# Let's add logging around confirm and skip as well
old_confirm = '''                    else if (scenario == "card_select" && actionStr == "confirm_selection")
                    {
                        var screenNode = NOverlayStack.Instance?.Peek() as Godot.Node;
                        if (screenNode != null) {
                            var confirmBtn = UiHelper.FindAll<NConfirmButton>(screenNode).FirstOrDefault(b => b.IsEnabled && b.IsVisibleInTree());
                            if (confirmBtn != null) {
                                await UiHelper.Click(confirmBtn);
                            }
                        }
                    }'''
new_confirm = '''                    else if (scenario == "card_select" && actionStr == "confirm_selection")
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
                    }'''
content = content.replace(old_confirm, new_confirm)

old_skip = '''                    else if (scenario == "card_select" && actionStr == "skip_selection")
                    {
                        var screenNode = NOverlayStack.Instance?.Peek() as Godot.Node;
                        if (screenNode != null) {
                            var skipBtn = UiHelper.FindAll<NChoiceSelectionSkipButton>(screenNode).FirstOrDefault(b => b.IsEnabled && b.IsVisibleInTree());
                            if (skipBtn != null) {
                                await UiHelper.Click(skipBtn);
                            }
                        }
                    }'''
new_skip = '''                    else if (scenario == "card_select" && actionStr == "skip_selection")
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
                    }'''
content = content.replace(old_skip, new_skip)

with open(r'd:\WORK\ats2\mod example\sts2_example_mod\Relics\LlmControllerRelic.cs', 'w', encoding='utf-8') as f:
    f.write(content)
print('Patch applied successfully!')