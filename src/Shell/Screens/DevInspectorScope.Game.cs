using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focus scope for dev mode's <see cref="LudeonTK.EditWindow_DebugInspector"/>, the live
    /// state dump driven by the <c>DebugViewSettings.write*</c> toggles.
    /// Attachment is opt-in: this is a non-modal EditWindow coexisting with the map beneath, so
    /// the <c>ShellBootstrap</c> factory yields a scope only while <see cref="Arming"/> is set by
    /// the deliberate F12 &gt; Development opener; any other open leaves the window scopeless.
    /// Escape is vanilla's — <c>closeOnCancel</c> closes the window and this scope pops with it.
    /// Vanilla's dump follows the MOUSE, so <see cref="RefreshContent"/> invokes the window's own
    /// <c>CurrentDebugString()</c> inside <see cref="DevToolTargeting.WithCursorOverride{T}"/> to
    /// resolve every mouse-cell read to the keyboard cursor; that call walks all game/map state,
    /// so the snapshot is cached by inspected cell plus a toggle stamp. The window's own
    /// per-Repaint rebuild is left following the mouse, for sighted parity.
    /// One content region (the dump, one read-only row per line) plus the Buttons region carrying
    /// the mirrored top-row controls; the two column-width buttons are omitted deliberately —
    /// they only widen the visual pane.
    /// </summary>
    internal sealed class DevInspectorScope : ScreenScope
    {
        /// <summary>
        /// Set by the F12 &gt; Development opener around the attach path so the factory yields a
        /// scope for that one open; cleared in a finally so no other open can inherit it.
        /// </summary>
        internal static bool Arming;

        private const int InspectorRegion = 0;

        private static readonly MethodInfo CurrentDebugStringMethod =
            AccessTools.Method(typeof(EditWindow_DebugInspector), "CurrentDebugString");

        private readonly EditWindow_DebugInspector window;
        private readonly List<string> lines = new List<string>();

        // Un-stripped snapshot for the clipboard: what vanilla's right-click copies, but taken
        // at the keyboard cursor.
        private string snapshotText = "";
        private IntVec3 inspectedCell;
        private bool inspectedCellValid;
        private bool announcedOpen;

        // Cache key: rebuild only when the cell moves or a toggle bumps the stamp
        // (CurrentDebugString is expensive).
        private bool built;
        private int stateStamp;
        private int cachedStamp = -1;
        private IntVec3 cachedCell = IntVec3.Invalid;

        public DevInspectorScope(EditWindow_DebugInspector window)
        {
            this.window = window;
        }

        public override string Name => "dev-inspector";

        /// <summary>The dump is many lines of named state worth jumping through.</summary>
        protected override bool EnableTypeahead => true;

        /// <summary>EditWindow_DebugInspector draws its controls via DevGUI, not Widgets.ButtonText, so there is nothing to scrape.</summary>
        protected override bool CaptureWindowButtons => false;

        public override void OnFocus()
        {
            base.OnFocus();
            if (!announcedOpen)
            {
                announcedOpen = true;
                AnnounceRegion();
                return;
            }
            AnnounceCurrentItem();
        }

        protected override int ContentRegionCount => 1;

        protected override string ContentRegionName(int region)
        {
            string name = "RimWorldAccess.Dev.InspectorRegion".Translate().ToString();
            if (inspectedCellValid)
            {
                name += ". " + "RimWorldAccess.Dev.InspectorAt"
                    .Translate(inspectedCell.x, inspectedCell.z).ToString();
            }
            return name;
        }

        protected override int ContentItemCount(int region)
        {
            return lines.Count;
        }

        /// <summary>Rebuilds the snapshot only when the inspected cell or the toggle stamp moved.</summary>
        protected override void RefreshContent()
        {
            IntVec3 cell = MapNavigationState.CurrentCursorPosition;
            if (built && cell == cachedCell && stateStamp == cachedStamp)
            {
                return;
            }
            built = true;
            cachedCell = cell;
            cachedStamp = stateStamp;
            RebuildSnapshot(cell);
        }

        private void RebuildSnapshot(IntVec3 cell)
        {
            // Vehicle A: vanilla's own dump builder, with UI.MouseCell/MouseMapPosition forced
            // to the keyboard cursor.
            snapshotText = DevToolTargeting.WithCursorOverride(cell,
                () => (string)CurrentDebugStringMethod.Invoke(window, null)) ?? "";

            inspectedCell = cell;
            inspectedCellValid = Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null;

            // Runs of blank lines collapse to a single separator row.
            lines.Clear();
            string[] raw = snapshotText.StripTags().Split('\n');
            foreach (string r in raw)
            {
                string line = r.TrimEnd('\r');
                if (line.Trim().Length == 0)
                {
                    if (lines.Count == 0 || lines[lines.Count - 1].Length == 0)
                    {
                        continue;
                    }
                    lines.Add("");
                    continue;
                }
                lines.Add(line);
            }
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            d.Label = index >= 0 && index < lines.Count ? lines[index] : "";
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            // Read-only text; Enter just re-reads the current line.
            AnnounceCurrentItem();
        }

        // The mirrored top-row controls, rebuilt on each read so toggle labels stay live.
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                return new List<ScreenAction>
                {
                    new ScreenAction(
                        ToggleLabel("RimWorldAccess.Dev.InspectorDeep".Translate().ToString(), window.fullMode),
                        ToggleDeep),
                    new ScreenAction(
                        ToggleLabel("RimWorldAccess.Dev.InspectorShallow".Translate().ToString(),
                            DebugViewSettings.writeCellContents),
                        ToggleShallow),
                    new ScreenAction("RimWorldAccess.Dev.InspectorVisibility".Translate().ToString(), OpenVisibility),
                    new ScreenAction("Copy to clipboard", CopyToClipboard),
                };
            }
        }

        private void ToggleDeep()
        {
            // MUTATION-C: mirrors EditWindow_DebugInspector.DoWindowContents'
            // DoImageToggle for deep inspection (flips the public fullMode field
            // by ref). Inline in the draw pass; no invocable vanilla method.
            window.fullMode = !window.fullMode;
            InvalidateSnapshot();
            RefreshModel();
            TolkHelper.SpeakData(
                ToggleLabel("RimWorldAccess.Dev.InspectorDeep".Translate().ToString(), window.fullMode));
        }

        private void ToggleShallow()
        {
            // MUTATION-C: mirrors EditWindow_DebugInspector.DoWindowContents'
            // DoImageToggle for shallow cell contents (flips
            // DebugViewSettings.writeCellContents by ref). Inline in the draw
            // pass; no invocable vanilla method.
            DebugViewSettings.writeCellContents = !DebugViewSettings.writeCellContents;
            InvalidateSnapshot();
            RefreshModel();
            TolkHelper.SpeakData(
                ToggleLabel("RimWorldAccess.Dev.InspectorShallow".Translate().ToString(),
                    DebugViewSettings.writeCellContents));
        }

        private void OpenVisibility()
        {
            // Vanilla's own inline "Visbility" [sic] row-button body.
            Find.WindowStack.Add(new Dialog_Debug(DebugTabMenuDefOf.Settings));
            // write* flags toggled there change what the dump reports.
            InvalidateSnapshot();
        }

        private void CopyToClipboard()
        {
            // The window's own right-click handler, but copying this scope's cursor-based
            // snapshot rather than the mouse-following buffer.
            GUIUtility.systemCopyBuffer = snapshotText ?? "";
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.SpeakData("RimWorldAccess.Dev.InspectorCopied".Translate().ToString());
        }

        private void InvalidateSnapshot()
        {
            stateStamp++;
        }

        private static string ToggleLabel(string caption, bool on)
        {
            string state = (on
                ? "RimWorldAccess.Shell.State.Checked"
                : "RimWorldAccess.Shell.State.Unchecked").Translate().ToString();
            return caption + ", " + state;
        }
    }
}
