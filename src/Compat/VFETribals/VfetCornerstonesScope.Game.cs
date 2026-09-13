using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Vanilla Factions Expanded - Tribals'
    /// <c>Window_CustomizeCornerstones</c>, registered by <see cref="VfetModule"/>; all
    /// reflection lives in <see cref="VfetCompat"/>. The window itself is keyboard-hostile in
    /// two ways this scope routes around: its per-cornerstone Unlock button is only drawn while
    /// the mouse hovers that row, and the ethos editor opens from an invisible click region.
    ///
    /// Two content regions. Ethos: the ethos text (Enter opens the mod's own
    /// <c>Dialog_EditEthos</c>, as the invisible click region does) and the ethos lock checkbox.
    /// Cornerstones: the available-points line, the mod's explanation, then one row per
    /// cornerstone in the window's own order (owned first); Enter on a locked row unlocks it
    /// through <c>AddCornerstone</c> behind the same points gate the hover button uses.
    /// </summary>
    internal sealed class VfetCornerstonesScope : ScreenScope
    {
        private const int EthosRegion = 0;
        private const int CornerstonesRegion = 1;

        private const int EthosTextRow = 0;
        private const int EthosLockRow = 1;
        private const int EthosRowCount = 2;

        /// <summary>Rows before the first cornerstone in the cornerstones region.</summary>
        private const int PointsRow = 0;
        private const int ExplanationRow = 1;
        private const int CornerstoneRowOffset = 2;

        private readonly Window window;
        private readonly List<Def> cornerstones = new List<Def>();

        public VfetCornerstonesScope(Window w)
        {
            window = w;
        }

        public override string Name => "vfet-cornerstones";

        protected internal override Window OwnedWindow => window;

        protected override bool EnableTypeahead => true;

        /// <summary>The window's only ButtonText is Close; a declared action presents it stably
        /// (the hover-only Unlock button would otherwise flicker in and out of capture).</summary>
        protected override bool CaptureWindowButtons => false;

        protected override IReadOnlyList<ScreenAction> DeclaredActions => new[]
        {
            new ScreenAction("Close".Translate(), () => window.Close()),
        };

        protected override string ComposeOpenAnnouncement()
        {
            var parts = new List<string> { CompatText.ModText("VFET.CustomizeCornerstones") };
            Faction player = Faction.OfPlayerSilentFail;
            if (player != null)
            {
                parts.Add(player.Name);
            }
            parts.Add(CompatText.ModArgs("VFET.AvailableCornerstonePoints", VfetCompat.Points()));
            return CompatText.JoinSentences(parts);
        }

        protected override void RefreshContent()
        {
            cornerstones.Clear();
            cornerstones.AddRange(VfetCompat.AllCornerstones());
        }

        protected override int ContentRegionCount => 2;

        protected override string ContentRegionName(int region)
        {
            return region == EthosRegion
                ? CompatText.ModText("VFET.Ethos")
                : CompatText.ModText("VFET.Cornerstones");
        }

        protected override int ContentItemCount(int region)
        {
            return region == EthosRegion
                ? EthosRowCount
                : CornerstoneRowOffset + cornerstones.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == EthosRegion)
            {
                if (index == EthosTextRow)
                {
                    d.Label = CompatText.ModText("VFET.Ethos");
                    d.Role = ElementRole.Button;
                    d.Value = CompatText.Flatten(VfetCompat.Ethos());
                    d.Extras = CompatText.Flatten(CompatText.ModText("VFET.EthosTooltip"));
                    d.Hint = "RimWorldAccess.Compat.Vfet.EthosEditHint".Translate();
                }
                else if (index == EthosLockRow)
                {
                    d.Label = "RimWorldAccess.Compat.Vfet.EthosLockRow".Translate();
                    d.Role = ElementRole.Checkbox;
                    d.Check = VfetCompat.EthosLocked() ? CheckState.Checked : CheckState.Unchecked;
                    d.Extras = CompatText.Flatten(CompatText.ModText(
                        VfetCompat.EthosLocked() ? "VFET.EthosLocked" : "VFET.EthosUnlocked"));
                }
                return d;
            }

            if (index == PointsRow)
            {
                d.Label = CompatText.ModArgs("VFET.AvailableCornerstonePoints", VfetCompat.Points());
                d.ReadOnly = true;
                return d;
            }
            if (index == ExplanationRow)
            {
                d.Label = CompatText.Flatten(CompatText.ModText("VFET.CornerstonesExplanation"));
                d.ReadOnly = true;
                return d;
            }

            Def def = CornerstoneAt(index);
            if (def == null)
            {
                return d;
            }
            d.Label = def.LabelCap;
            d.Extras = CompatText.Flatten(def.description);
            if (VfetCompat.IsOwned(def))
            {
                d.ReadOnly = true;
                d.Value = "RimWorldAccess.Compat.Vfet.Unlocked".Translate();
            }
            else
            {
                d.Role = ElementRole.Button;
                d.Value = "RimWorldAccess.Compat.Vfet.Locked".Translate();
                if (VfetCompat.Points() <= 0)
                {
                    d.Disabled = true;
                }
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == EthosRegion)
            {
                if (index == EthosTextRow)
                {
                    // Same window the mod's invisible click region opens; its generic reader
                    // hosts the text-edit session.
                    VfetCompat.OpenEditEthosDialog();
                }
                else if (index == EthosLockRow)
                {
                    bool locked = !VfetCompat.EthosLocked();
                    VfetCompat.SetEthosLocked(locked);
                    (locked ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff)
                        .PlayOneShotOnCamera();
                    RefreshModel();
                    AnnounceCurrentItem();
                }
                return;
            }

            Def def = CornerstoneAt(index);
            if (def == null || VfetCompat.IsOwned(def))
            {
                return;
            }
            // Same gate the window's hover-drawn Unlock button applies before AddCornerstone.
            if (VfetCompat.Points() <= 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Compat.Vfet.NoPoints".Loc());
                return;
            }
            // AddCornerstone regenerates the ethos unless it is locked.
            bool ethosRewrites = !VfetCompat.EthosLocked();
            VfetCompat.Unlock(def);
            RefreshModel();
            string announcement = "RimWorldAccess.Compat.Vfet.UnlockedAnnouncement".Translate(
                def.LabelCap, VfetCompat.Points());
            if (ethosRewrites)
            {
                announcement = CompatText.JoinSentences(new List<string>
                {
                    announcement,
                    "RimWorldAccess.Compat.Vfet.EthosRewritten".Translate(
                        CompatText.Flatten(VfetCompat.Ethos())),
                });
            }
            TolkHelper.SpeakData(announcement);
        }

        private Def CornerstoneAt(int index)
        {
            int i = index - CornerstoneRowOffset;
            return i >= 0 && i < cornerstones.Count ? cornerstones[i] : null;
        }
    }
}
