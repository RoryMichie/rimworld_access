using System.Collections.Generic;
using HarmonyLib;
using LudeonTK;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for RimWorld's runtime debug option picker
    /// (<see cref="LudeonTK.Dialog_DebugOptionListLister"/> — the "choose which
    /// thing / faction / hediff" list that many dev actions open after the main
    /// <see cref="LudeonTK.Dialog_Debug"/> menu closes), registered through
    /// <see cref="ScopeForWindow"/>. A little sibling of <see cref="DevDebugScope"/>:
    /// one content region of <see cref="DebugMenuOption"/> rows (named by the
    /// dialog's own <c>header</c> when it has one) plus the automatic Buttons
    /// region for the window's Close-X.
    ///
    /// Activation mirrors <c>Dialog_DebugOptionListLister.OnAcceptKeyPressed</c>
    /// exactly: the window closes first, then an Action option runs its method
    /// and a Tool option arms <c>DebugTools.curTool</c> with the same
    /// <c>DebugTool</c> construction vanilla makes. Vanilla hides Tool options
    /// entirely while the world view is selected (its <c>DebugToolMap</c>
    /// early-returns under <c>WorldRendererUtility.WorldSelected</c>), so this
    /// scope drops them from the item list under the same condition.
    ///
    /// Like Dialog_Debug, the base <see cref="LudeonTK.Dialog_OptionLister"/>
    /// force-focuses its "DebugFilter" IMGUI text box once on open (the private
    /// <c>focusFilter</c> latch), which would steal every keystroke from the
    /// shell; <see cref="OnPush"/> clears it before the first GUI pass so the
    /// shared typeahead handles searching instead.
    ///
    /// Subclasses <see cref="OptionListScope"/> for the single-list grammar
    /// (region mapping, typeahead, announce-once-on-open) and fills its four
    /// seams with the dialog's own options.
    /// </summary>
    internal sealed class DevOptionListScope : OptionListScope
    {
        /// <summary>The single content region <see cref="OptionListScope"/> seals in place.</summary>
        private const int OptionRegion = 0;

        private static readonly AccessTools.FieldRef<Dialog_DebugOptionListLister, List<DebugMenuOption>> OptionsRef =
            AccessTools.FieldRefAccess<Dialog_DebugOptionListLister, List<DebugMenuOption>>("options");
        private static readonly AccessTools.FieldRef<Dialog_DebugOptionListLister, string> HeaderRef =
            AccessTools.FieldRefAccess<Dialog_DebugOptionListLister, string>("header");
        private static readonly System.Reflection.FieldInfo FocusFilterField =
            AccessTools.Field(typeof(Dialog_OptionLister), "focusFilter");

        // The highlight index lives on the base Dialog_DebugOptionLister, which
        // declares it; a FieldRef against the derived type would miss it.
        private static readonly AccessTools.FieldRef<Dialog_DebugOptionLister, int> PrioritizedHighlightIndexRef =
            AccessTools.FieldRefAccess<Dialog_DebugOptionLister, int>("prioritizedHighlightedIndex");

        private readonly Dialog_DebugOptionListLister dialog;
        private readonly List<DebugMenuOption> items = new List<DebugMenuOption>();
        private readonly List<int> sourceIndices = new List<int>();

        public DevOptionListScope(Dialog_DebugOptionListLister dialog)
        {
            this.dialog = dialog;
        }

        public override string Name => "dev-option-list";

        /// <summary>Dialog_OptionLister draws its rows via DevGUI, not Widgets.ButtonText, so there is nothing to scrape.</summary>
        protected override bool CaptureWindowButtons => false;

        public override void OnPush()
        {
            base.OnPush();

            // Dialog_OptionLister force-focuses its "DebugFilter" text box once on
            // open (the private focusFilter latch), which would steal every
            // keystroke from the shell. Clearing it before the first GUI pass
            // hands typing to the shell's typeahead instead.
            if (FocusFilterField != null)
            {
                // MUTATION-C: mirrors Dialog_OptionLister's one-shot focusFilter
                // latch. No public accessor exists; this is UI focus state, not
                // game state.
                FocusFilterField.SetValue(dialog, false);
            }
            UI.UnfocusCurrentControl();
        }

        // ------------------------------------------------------------------
        // OptionListScope seams.
        // ------------------------------------------------------------------

        protected override string OptionRegionName
        {
            get
            {
                string header = HeaderRef(dialog);
                return string.IsNullOrEmpty(header)
                    ? "RimWorldAccess.Dev.OptionsRegion".Translate().ToString()
                    : header;
            }
        }

        protected override int OptionCount => items.Count;

        /// <summary>
        /// Snapshots the option list, dropping Tool-mode options while the world
        /// view is selected — vanilla's <c>DebugToolMap</c> early-returns under
        /// the same condition, so those rows are not clickable there.
        /// </summary>
        protected override void RefreshContent()
        {
            items.Clear();
            sourceIndices.Clear();
            List<DebugMenuOption> options = OptionsRef(dialog);
            if (options == null)
            {
                return;
            }
            bool worldSelected = WorldRendererUtility.WorldSelected;
            for (int i = 0; i < options.Count; i++)
            {
                DebugMenuOption option = options[i];
                if (worldSelected && option.mode == DebugMenuOptionMode.Tool)
                {
                    continue;
                }
                items.Add(option);
                sourceIndices.Add(i);
            }
        }

        /// <summary>
        /// Puts vanilla's own yellow highlight box on the focused row. The
        /// dialog already draws one around the option at its highlight index
        /// (the Dev_ChangeSelectedDebugAction machinery), so the cursor rides
        /// that index rather than a ring of our own. The index counts against
        /// the dialog's FULL option list, which the dropped Tool rows above
        /// make wider than this scope's row list. Off the option list — the
        /// Buttons region — the field is left alone.
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            if (region != OptionRegion || index < 0 || index >= sourceIndices.Count)
            {
                return;
            }
            PrioritizedHighlightIndexRef(dialog) = sourceIndices[index];
        }

        protected override ElementDescription DescribeOption(int index)
        {
            var d = new ElementDescription();
            if (index >= 0 && index < items.Count)
            {
                // Tool options carry vanilla's own "T: " label prefix; present verbatim.
                d.Label = items[index].label;
            }
            d.Role = ElementRole.Button;
            return d;
        }

        protected override void ActivateOption(int index)
        {
            if (index < 0 || index >= items.Count)
            {
                return;
            }
            DebugMenuOption option = items[index];

            // Mirrors Dialog_DebugOptionListLister.OnAcceptKeyPressed: close the
            // window first, then an Action option runs its method and a Tool
            // option arms DebugTools.curTool with the same DebugTool construction
            // vanilla makes. RunAndAnnounce wraps the whole close-and-dispatch so
            // a silent or throwing action speaks a result; it stays silent when
            // the method opens a window or the Tool branch arms curTool (the
            // DevToolTargeting mirror speaks that, with the keyboard usage hint).
            DevActionOutcome.RunAndAnnounce(option.label, delegate
            {
                dialog.Close();
                if (option.mode == DebugMenuOptionMode.Action)
                {
                    option.method();
                }
                else
                {
                    DebugTools.curTool = new DebugTool(option.label, option.method);
                }
            });
        }
    }
}
