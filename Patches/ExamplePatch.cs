using FirstMod.Relics;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;



namespace FirstMod.Patches;

[HarmonyPatch(typeof(Player), nameof(Player.CreateForNewRun), new Type[] { typeof(CharacterModel), typeof(UnlockState), typeof(ulong) })]
public class ExamplePatch
{
    static void Postfix(Player __result)
    {
        // Add LLM Controller Relic
        var llmRelic = ModelDb.Relic<LlmControllerRelic>().ToMutable();
        __result.AddRelicInternal(llmRelic);

    }
}
