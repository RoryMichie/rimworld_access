using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Leaves the game paused when the player opens the research screen from a research-finished
    /// dialog. Vanilla force-pauses that dialog only while it is open and resumes the prior speed
    /// on close; its "research screen" option additionally jumps to the Research tab. We wrap that
    /// option so it also pauses, letting the player study the tree without the clock running. The
    /// "OK" option carries no action and is left untouched, so it resumes as before.
    /// </summary>
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    public static class ResearchFinishedPausePatch
    {
        private static readonly AccessTools.FieldRef<Dialog_NodeTree, DiaNode> curNodeRef =
            AccessTools.FieldRefAccess<Dialog_NodeTree, DiaNode>("curNode");

        [HarmonyPostfix]
        public static void Postfix(bool doCompletionDialog)
        {
            if (!doCompletionDialog)
                return;
            if (!(RimWorldAccessMod_Settings.Settings?.StayPausedAfterResearch ?? true))
                return;

            // WindowStack.Add clears any prior Dialog_NodeTree, so the completion dialog just built
            // is the only one on the stack.
            Dialog_NodeTree dialog = Find.WindowStack?.WindowOfType<Dialog_NodeTree>();
            DiaNode node = dialog != null ? curNodeRef(dialog) : null;
            if (node?.options == null)
                return;

            foreach (DiaOption option in node.options)
            {
                // DefaultOK carries no action; the "research screen" option is the one that does.
                if (option.action == null)
                    continue;

                Action vanilla = option.action;
                option.action = delegate
                {
                    vanilla();
                    TimeControlAccessibilityPatch.PauseSilently();
                };
            }
        }
    }
}
