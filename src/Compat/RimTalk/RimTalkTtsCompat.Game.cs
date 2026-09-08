using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Registration entry point for the RimTalk TTS addon (nitoritech.rimtalk.tts): the voice
    /// manager's settings page and its five windows.
    ///
    /// ONLY TWO of those five need any code here. Both VoiceRuleEditorWindow and
    /// CustomProviderEditorWindow set <c>absorbInputAroundWindow = true</c> in their own
    /// constructors -- ScopeForWindow's existing generic-reader fallback
    /// (Attach -&gt; TryCreateGenericReader -&gt; GenericReaderEligible, ScopeForWindow.Game.cs)
    /// already attaches GenericWindowScope to any window that declares itself modal that way, with
    /// no registration call at all. Their entire widget vocabulary -- Label, ButtonText opening
    /// FloatMenu, TextField/TextArea, CheckboxLabeled, RadioButton, and the Label+ButtonInvisible
    /// fusion GenericWindowScope's own rule (c) already applies to VoiceRuleEditorWindow's
    /// collapsible section headers ("&#9660; Gender" etc, exactly the same shape as the main
    /// settings page's own voice-rules-list row) -- is squarely inside that reader's documented
    /// reach; verified by reading every DoWindowContents body directly, not assumed. The settings
    /// page itself (TTSMod.DoSettingsWindowContents) needs nothing either: it draws inside the
    /// shared vanilla Dialog_ModSettings, already covered mod-agnostically by
    /// ModSettingsDialogPatch + GenericWindowScope for any active mod's settings --
    /// there is no per-mod registration surface for a Dialog_ModSettings host at all.
    ///
    /// VoiceSelectionWindow (opened per-pawn from the Bio tab) explicitly sets
    /// <c>absorbInputAroundWindow = false</c>, so it is NOT auto-eligible, and its hand-rolled
    /// option rows (a real Widgets.Checkbox icon, two separate Labels, and a whole-row
    /// Widgets.ButtonInvisible all overlapping) are exactly the geometry GenericWindowScope's own
    /// fusion heuristic is documented to mishandle (the row's InvisibleButton would fuse with
    /// whichever Label the backward search reaches, not necessarily the option's own name) -- a
    /// bespoke scope is the correct call, not a registered-generic one. VoiceLibraryWindow IS
    /// auto-eligible (absorbInputAroundWindow = true) but its ~90-entry reference table draws five
    /// separate Label columns per row inside one whole-row InvisibleButton, which the SAME fusion
    /// heuristic would collapse into a single caption (whichever Label the backward search finds
    /// first, losing the other four columns) -- also a bespoke call, overriding the automatic
    /// fallback with <see cref="ScopeForWindow.Register"/>.
    ///
    /// POISONED ASSEMBLY: the TTS addon ships one type, RimTalk.TTS.Service.SiliconFlowClient,
    /// whose fields reference NAudio types that fail to resolve on macOS -- touching that type even
    /// via GetFields()/IsAssignableFrom throws TypeLoadException (CLAUDE.md gotcha). Every type
    /// resolved anywhere in this file and its sibling scope files is named exactly
    /// (AccessTools.TypeByName/AccessTools.Inner with one specific name), never swept via
    /// GenTypes/reflection-over-all-types, and none of them is SiliconFlowClient or references it.
    /// </summary>
    internal static class RimTalkTtsCompat
    {
        private const string RimTalkPackageId = "cj.rimtalk";
        private const string TtsPackageId = "nitoritech.rimtalk.tts";

        private static readonly LazyReflectionGate cacheGate =
            new LazyReflectionGate("RimTalk TTS compat (Bio-tab voice opener)", ResolveCacheReflection);

        private static MethodInfo cacheGetMethod;

        public static void Register()
        {
            if (!ModsConfig.IsActive(TtsPackageId))
            {
                return;
            }

            try
            {
                RimTalkTtsVoiceSelectionCompat.Register();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk TTS compat: VoiceSelectionWindow registration failed: {ex.Message}");
            }

            try
            {
                RimTalkTtsVoiceLibraryCompat.Register();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk TTS compat: VoiceLibraryWindow registration failed: {ex.Message}");
            }

            // Bio-tab opener row: mirrors RimTalk.TTS.Patch.BioTabVoicePatch's own chip (a
            // CharacterCardUtility.DoTopStack transpiler this codebase does not and cannot ride --
            // that surface is vanilla's own stack drawer, not our inspection tree) with a registered
            // extender row instead, the same shape RimTalkPersonaDialogCompat.Register uses for
            // RimTalk core's persona-editor chip. Requires the RimTalk core assembly for the
            // eligibility probe below (RimTalk.Data.Cache), which the TTS addon itself already
            // hard-depends on at runtime (BioTabVoicePatch.ShouldShowVoiceUI calls it directly).
            if (ModsConfig.IsActive(RimTalkPackageId) && cacheGate.Ensure())
            {
                InspectNodeRegistry.RegisterCategoryExtender("Character", AddVoiceOpenerRow);
            }
        }

        private static bool ResolveCacheReflection(ReflectionSurface surface)
        {
            MethodInfo getMethod = surface.Method(surface.Type("RimTalk.Data.Cache"), "Get", new[] { typeof(Pawn) });

            if (!surface.Ready)
            {
                return false;
            }

            cacheGetMethod = getMethod;
            return true;
        }

        /// <summary>Mirrors BioTabVoicePatch.ShouldShowVoiceUI exactly: a pawn is eligible iff RimTalk's own per-pawn cache already holds a PawnState for it.</summary>
        private static bool ShouldShowVoiceUi(Pawn pawn)
        {
            if (pawn == null || !cacheGate.Ensure())
            {
                return false;
            }
            try
            {
                return cacheGetMethod.Invoke(null, new object[] { pawn }) != null;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk TTS compat", ex);
                return false;
            }
        }

        private static void AddVoiceOpenerRow(InspectionTreeItem categoryItem, object obj)
        {
            if (!(obj is Pawn pawn) || !ShouldShowVoiceUi(pawn))
            {
                return;
            }
            string label = "RimWorldAccess.Compat.RimTalk.TTS.VoiceOpenerRow".Translate();
            InspectNodeFactory.ActionRow(categoryItem, label, pawn, () => RimTalkTtsVoiceSelectionCompat.OpenVoiceSelection(pawn));
        }

    }
}
