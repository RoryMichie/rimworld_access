using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the Character Editor mod's <c>DialogColorPicker</c>. One
    /// registration serves all nine modes the dialog opens in, read live off the open instance via
    /// <see cref="CharEditorColorPickerCompat.GetMode"/> rather than at construction. Five content
    /// regions: Values (RGBA and random-brightness sliders, an RGBA exact-entry session, and a Hex
    /// session validated through vanilla's <c>ColorUtility.TryParseHtmlString</c>); Palette (the 52
    /// preset swatches then the 14 brightness-derived ones); Channel (two radio rows, present only
    /// where the dialog's own radios would draw a second channel — SkinColor always, HairColor only
    /// under Gradient Hair); Genes (Biotech with a gene tracker: one row per hair-or-skin gene plus
    /// an "add as new gene" row, REAL mutations labeled plainly rather than as color picks); and
    /// Mode extras (hair's Hair/Beard rows, apparel's worn list, weapon's equipped list).
    /// The dialog's one real button arrives through the base's own capture and click injection, so
    /// no <see cref="ScreenScope.DeclaredActions"/> is needed.
    ///
    /// EVERYTHING APPLIES LIVE. This scope buffers no pending color, any more than the mod's own
    /// sliders do: every adjust applies immediately and there is no cancel or revert path. The
    /// one-shot notice is spoken on entry, not per adjustment.
    ///
    /// LIVE RE-TARGET: switching the edited pawn underneath makes the dialog silently re-Init
    /// against the new pawn every frame the mismatch persists, so <see cref="RefreshContent"/>
    /// re-reads the temp pawn every model refresh and the Genes and Mode-extras rows never describe
    /// a stale pawn.
    /// </summary>
    internal sealed class CharEditorColorScope : ScreenScope
    {
        private const int ValuesRegion = 0;
        private const int PaletteRegion = 1;
        private const int ChannelRegion = 2;
        private const int GenesRegion = 3;
        private const int ExtrasRegion = 4;

        private const int RedRow = 0;
        private const int GreenRow = 1;
        private const int BlueRow = 2;
        private const int AlphaRow = 3;
        private const int MinBrightRow = 4;
        private const int MaxBrightRow = 5;
        private const int RgbaRow = 6;
        private const int HexRow = 7;
        private const int ValuesRowCount = 8;

        private const int PaletteSwatchCount = 52;
        private const int DerivedStripCount = 14;

        private const float RgbStep = 1f / 255f;
        private const float BrightStep = 0.05f;
        private const int HexDigitCount = 6;

        private readonly Window dialog;
        private readonly TextFieldEditSession valueSession = new TextFieldEditSession();
        private readonly Regex hexRegex = new Regex("^[0-9A-Fa-f]+$");
        private readonly Regex rgbaRegex = new Regex("^[0-9,]*$");

        private bool announcedOpen;
        private Pawn cachedPawn;
        private List<Gene> cachedGenes = new List<Gene>();
        private bool cachedHasAddGeneRow;
        private List<Apparel> cachedWorn = new List<Apparel>();
        private List<ThingWithComps> cachedEquipped = new List<ThingWithComps>();

        public CharEditorColorScope(Window dialog)
        {
            this.dialog = dialog;

            // The unified Alt+I drill-in; Ctrl-remove is promoted to this discrete action rather
            // than a chord. One target only, so it acts directly rather than opening a picker.
            Claim(SharedMenuGrammar.Info, e => RemoveGeneAt(Model.CurrentRegion.Index), when: OnGeneEntryRow);

            RegisterPopTeardown(valueSession.CancelIfActive);
        }

        private bool OnGeneEntryRow()
        {
            return Model.RegionIndex == GenesRegion && !Model.CurrentRegionIsEmpty
                && Model.CurrentRegion.Index < cachedGenes.Count;
        }

        public override string Name => "char-editor-color";

        /// <summary>Palette/gene/mode-extras rows are named items worth searching (table-model T4).</summary>
        protected override bool EnableTypeahead => true;

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;

            string title = ModeTitle(CharEditorColorPickerCompat.GetMode(dialog));
            // Reuses the key the Mutant picker already minted for this exact phrase rather than
            // duplicating a near-identical string.
            TolkHelper.SpeakData(title + " " + "RimWorldAccess.CharEd.Character.AppliesImmediately".Translate());
            AnnounceCurrentItem();
        }

        private static string ModeTitle(CharEditorColorPickerCompat.Mode mode)
        {
            switch (mode)
            {
                case CharEditorColorPickerCompat.Mode.HairColor: return "RimWorldAccess.CharEd.Appearance.HairColor".Translate().ToString();
                case CharEditorColorPickerCompat.Mode.SkinColor: return "RimWorldAccess.CharEd.Appearance.SkinColor".Translate().ToString();
                case CharEditorColorPickerCompat.Mode.EyeColor: return "RimWorldAccess.CharEd.Appearance.EyeColor1".Translate().ToString();
                case CharEditorColorPickerCompat.Mode.ApparelColor: return "RimWorldAccess.CharEd.Color.ApparelTitle".Translate().ToString();
                case CharEditorColorPickerCompat.Mode.WeaponColor: return "RimWorldAccess.CharEd.Color.WeaponTitle".Translate().ToString();
                case CharEditorColorPickerCompat.Mode.FavColor: return "RimWorldAccess.CharEd.Character.FavoriteColor".Translate().ToString();
                default: return "RimWorldAccess.CharEd.Color.GeneTitle".Translate().ToString();
            }
        }

        // ------------------------------------------------------------------
        // ScreenScope content contract.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount => 5;

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case ValuesRegion: return "RimWorldAccess.CharEd.Color.ValuesRegion".Translate().ToString();
                case PaletteRegion: return "RimWorldAccess.CharEd.Color.PaletteRegion".Translate().ToString();
                case ChannelRegion: return "RimWorldAccess.CharEd.Color.ChannelRegion".Translate().ToString();
                case GenesRegion: return "RimWorldAccess.CharEd.Color.GenesRegion".Translate().ToString();
                case ExtrasRegion: return "RimWorldAccess.CharEd.Color.ExtrasRegion".Translate().ToString();
                default: return "";
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case ValuesRegion: return ValuesRowCount;
                case PaletteRegion: return PaletteSwatchCount + DerivedStripCount;
                case ChannelRegion: return HasChannelB() ? 2 : 0;
                case GenesRegion: return cachedGenes.Count + (cachedHasAddGeneRow ? 1 : 0);
                case ExtrasRegion: return ExtrasCount();
                default: return 0;
            }
        }

        private int ExtrasCount()
        {
            CharEditorColorPickerCompat.Mode mode = CharEditorColorPickerCompat.GetMode(dialog);
            if (mode == CharEditorColorPickerCompat.Mode.HairColor)
                return 2;
            if (mode == CharEditorColorPickerCompat.Mode.ApparelColor)
                return cachedWorn.Count;
            if (mode == CharEditorColorPickerCompat.Mode.WeaponColor)
                return cachedEquipped.Count;
            return 0;
        }

        /// <summary>Whether THIS open dialog's own radio buttons would draw a channel-B row: SkinColor always, HairColor only under Gradient Hair.</summary>
        private bool HasChannelB()
        {
            CharEditorColorPickerCompat.Mode mode = CharEditorColorPickerCompat.GetMode(dialog);
            if (mode == CharEditorColorPickerCompat.Mode.SkinColor)
                return true;
            if (mode == CharEditorColorPickerCompat.Mode.HairColor)
                return CharEditorCompat.GradientHairActive;
            return false;
        }

        /// <summary>Rebuilds the Genes and Mode-extras row caches every cycle: cheap, and it re-derives from the currently re-targeted pawn.</summary>
        protected override void RefreshContent()
        {
            cachedPawn = CharEditorColorPickerCompat.GetTempPawn(dialog);

            cachedGenes.Clear();
            cachedHasAddGeneRow = false;
            if (cachedPawn != null && ModsConfig.BiotechActive && cachedPawn.genes != null)
            {
                cachedGenes.AddRange(CharEditorColorPickerCompat.ModeGenes(dialog, cachedPawn));
                cachedHasAddGeneRow = CharEditorColorPickerCompat.ClosestGeneDef(dialog) != null;
            }

            cachedWorn.Clear();
            cachedEquipped.Clear();
            CharEditorColorPickerCompat.Mode mode = CharEditorColorPickerCompat.GetMode(dialog);
            if (mode == CharEditorColorPickerCompat.Mode.ApparelColor && cachedPawn?.apparel != null)
            {
                cachedWorn.AddRange(cachedPawn.apparel.WornApparel);
            }
            else if (mode == CharEditorColorPickerCompat.Mode.WeaponColor && cachedPawn?.equipment != null)
            {
                cachedEquipped.AddRange(cachedPawn.equipment.AllEquipmentListForReading);
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            switch (region)
            {
                case ValuesRegion: return DescribeValuesRow(index);
                case PaletteRegion: return DescribePaletteRow(index);
                case ChannelRegion: return DescribeChannelRow(index);
                case GenesRegion: return DescribeGeneRow(index);
                case ExtrasRegion: return DescribeExtrasRow(index);
                default: return new ElementDescription();
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch (region)
            {
                case ValuesRegion: ActivateValuesRow(index); break;
                case PaletteRegion: ActivatePaletteRow(index); break;
                case ChannelRegion: ActivateChannelRow(index); break;
                case GenesRegion: ActivateGeneRow(index); break;
                case ExtrasRegion: ActivateExtrasRow(index); break;
            }
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return region == ValuesRegion && index != RgbaRow && index != HexRow;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (region == ValuesRegion)
                AdjustValuesRow(index, direction);
        }

        // ------------------------------------------------------------------
        // Region 0: Values.
        // ------------------------------------------------------------------

        private ElementDescription DescribeValuesRow(int index)
        {
            var d = new ElementDescription();
            Color color = CharEditorColorPickerCompat.GetSelectedColor(dialog);
            switch (index)
            {
                case RedRow:
                    d.Label = "RimWorldAccess.CharEd.Color.Red".Translate();
                    d.Role = ElementRole.Slider;
                    d.Value = color.r.ToStringPercent();
                    d.AtMinimum = color.r <= 0f;
                    d.AtMaximum = color.r >= CharEditorColorPickerCompat.ColorMax;
                    break;
                case GreenRow:
                    d.Label = "RimWorldAccess.CharEd.Color.Green".Translate();
                    d.Role = ElementRole.Slider;
                    d.Value = color.g.ToStringPercent();
                    d.AtMinimum = color.g <= 0f;
                    d.AtMaximum = color.g >= CharEditorColorPickerCompat.ColorMax;
                    break;
                case BlueRow:
                    d.Label = "RimWorldAccess.CharEd.Color.Blue".Translate();
                    d.Role = ElementRole.Slider;
                    d.Value = color.b.ToStringPercent();
                    d.AtMinimum = color.b <= 0f;
                    d.AtMaximum = color.b >= CharEditorColorPickerCompat.ColorMax;
                    break;
                case AlphaRow:
                    d.Label = "RimWorldAccess.CharEd.Color.Alpha".Translate();
                    d.Role = ElementRole.Slider;
                    d.Value = color.a.ToStringPercent();
                    d.AtMinimum = color.a <= 0f;
                    d.AtMaximum = color.a >= CharEditorColorPickerCompat.ColorMax;
                    break;
                case MinBrightRow:
                {
                    float v = CharEditorColorPickerCompat.GetMinBrightness(dialog);
                    d.Label = "RimWorldAccess.CharEd.Color.MinBrightness".Translate();
                    d.Role = ElementRole.Slider;
                    d.Value = v.ToStringPercent();
                    d.AtMinimum = v <= 0f;
                    d.AtMaximum = v >= CharEditorColorPickerCompat.BrightnessMax;
                    break;
                }
                case MaxBrightRow:
                {
                    float v = CharEditorColorPickerCompat.GetMaxBrightness(dialog);
                    d.Label = "RimWorldAccess.CharEd.Color.MaxBrightness".Translate();
                    d.Role = ElementRole.Slider;
                    d.Value = v.ToStringPercent();
                    d.AtMinimum = v <= 0f;
                    d.AtMaximum = v >= CharEditorColorPickerCompat.BrightnessMax;
                    // This slider retints the shared 52-swatch palette process-wide.
                    d.Extras = "RimWorldAccess.CharEd.Color.MaxBrightnessAffectsPalette".Translate().ToString();
                    break;
                }
                case RgbaRow:
                    d.Label = "RimWorldAccess.CharEd.Color.RgbaRow".Translate(FormatRgba(color)).ToString();
                    d.Role = ElementRole.TextField;
                    break;
                case HexRow:
                    d.Label = "RimWorldAccess.CharEd.Color.HexRow".Translate(ColorUtility.ToHtmlStringRGB(color)).ToString();
                    d.Role = ElementRole.TextField;
                    break;
            }
            return d;
        }

        private static string FormatRgba(Color c)
        {
            return string.Format("{0},{1},{2},{3}",
                Mathf.RoundToInt(c.r * 255f), Mathf.RoundToInt(c.g * 255f),
                Mathf.RoundToInt(c.b * 255f), Mathf.RoundToInt(c.a * 255f));
        }

        private void ActivateValuesRow(int index)
        {
            if (index == RgbaRow)
            {
                BeginRgbaEdit();
                return;
            }
            if (index == HexRow)
            {
                BeginHexEdit();
                return;
            }
            AnnounceCurrentItem();
        }

        private void AdjustValuesRow(int index, int direction)
        {
            Color color = CharEditorColorPickerCompat.GetSelectedColor(dialog);
            switch (index)
            {
                case RedRow:
                    color.r = Mathf.Clamp01(color.r + RgbStep * direction);
                    CommitColor(color);
                    return;
                case GreenRow:
                    color.g = Mathf.Clamp01(color.g + RgbStep * direction);
                    CommitColor(color);
                    return;
                case BlueRow:
                    color.b = Mathf.Clamp01(color.b + RgbStep * direction);
                    CommitColor(color);
                    return;
                case AlphaRow:
                    color.a = Mathf.Clamp01(color.a + RgbStep * direction);
                    CommitColor(color);
                    return;
                case MinBrightRow:
                {
                    float v = Mathf.Clamp01(CharEditorColorPickerCompat.GetMinBrightness(dialog) + BrightStep * direction);
                    CharEditorColorPickerCompat.SetMinBrightness(dialog, v);
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    AnnounceRow(ValuesRegion, MinBrightRow);
                    return;
                }
                case MaxBrightRow:
                {
                    float v = Mathf.Clamp01(CharEditorColorPickerCompat.GetMaxBrightness(dialog) + BrightStep * direction);
                    CharEditorColorPickerCompat.SetMaxBrightness(dialog, v);
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    AnnounceRow(ValuesRegion, MaxBrightRow);
                    return;
                }
            }
        }

        private void CommitColor(Color color)
        {
            CharEditorColorPickerCompat.ApplyColor(dialog, color);
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            RefreshModel();
            AnnounceCurrentItem();
        }

        private void AnnounceRow(int region, int index)
        {
            RefreshModel();
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                DescribeContentItem(region, index), TranslatedShellVocabulary.Instance));
        }

        // ------------------------------------------------------------------
        // RGBA / Hex exact-entry sessions.
        // ------------------------------------------------------------------

        private void BeginRgbaEdit()
        {
            Color color = CharEditorColorPickerCompat.GetSelectedColor(dialog);
            // MUTATION-C: maxLength 15 is harvested from the widest string the dialog's own
            // RGBA field commit accepts -- four comma-separated 0-255 channels
            // ("255,255,255,255", the TextValuesFromSelectedColor round trip);
            // ApplyRgbaEdit re-validates each channel to the same 0-255 bounds.
            var spec = new TextFieldSpec(labelKey: null, maxLength: 15, minLength: 0, allowedChars: rgbaRegex);
            valueSession.EnterEdit(FormatRgba(color), spec,
                "RimWorldAccess.CharEd.Color.RgbaFieldLabel".Translate().ToString(),
                apply: ApplyRgbaEdit, onExit: OnValueExit, announcePrompt: true, onConfirm: OnRgbaConfirmed);
        }

        private string pendingRgba = "";

        private void ApplyRgbaEdit(string value)
        {
            pendingRgba = value ?? "";
        }

        private void OnValueExit()
        {
            // Silent: the confirm path below owns the one announcement, and a generic re-announce
            // here would double-speak it.
        }

        private void OnRgbaConfirmed()
        {
            string[] parts = pendingRgba.Split(',');
            if (parts.Length != 4)
            {
                TolkHelper.SpeakData("RimWorldAccess.CharEd.Color.InvalidRgba".Translate().ToString());
                return;
            }
            var values = new int[4];
            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i].Trim(), out values[i]) || values[i] < 0 || values[i] > 255)
                {
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.Color.InvalidRgba".Translate().ToString());
                    return;
                }
            }
            var color = new Color(values[0] / 255f, values[1] / 255f, values[2] / 255f, values[3] / 255f);
            CharEditorColorPickerCompat.ApplyColor(dialog, color);
            RefreshModel();
            AnnounceCurrentItem();
        }

        private void BeginHexEdit()
        {
            Color color = CharEditorColorPickerCompat.GetSelectedColor(dialog);
            var spec = new TextFieldSpec(labelKey: null, maxLength: HexDigitCount, minLength: 0, allowedChars: hexRegex);
            valueSession.EnterEdit(ColorUtility.ToHtmlStringRGB(color), spec,
                "RimWorldAccess.CharEd.Color.HexFieldLabel".Translate().ToString(),
                apply: ApplyHexEdit, onExit: OnValueExit, announcePrompt: true, onConfirm: OnHexConfirmed);
        }

        private string pendingHex = "";

        private void ApplyHexEdit(string value)
        {
            pendingHex = value ?? "";
        }

        private void OnHexConfirmed()
        {
            // Vanilla's own parser, never a string compare: it accepts 3-digit shorthand too,
            // though the field caps at 6 to match the mod's byte-per-channel granularity.
            if (!ColorUtility.TryParseHtmlString("#" + pendingHex, out Color parsed))
            {
                TolkHelper.SpeakData("RimWorldAccess.CharEd.Color.InvalidHex".Translate().ToString());
                return;
            }
            float alpha = CharEditorColorPickerCompat.GetSelectedColor(dialog).a;
            parsed.a = alpha;
            CharEditorColorPickerCompat.ApplyColor(dialog, parsed);
            RefreshModel();
            AnnounceCurrentItem();
        }

        // ------------------------------------------------------------------
        // Region 1: Palette.
        // ------------------------------------------------------------------

        private ElementDescription DescribePaletteRow(int index)
        {
            var d = new ElementDescription();
            d.Role = ElementRole.Button;
            Color color = index < PaletteSwatchCount
                ? PaletteColorAt(index)
                : CharEditorColorPickerCompat.DerivedColor(CharEditorColorPickerCompat.GetSelectedColor(dialog), index - PaletteSwatchCount);
            d.Label = ColorSwatchLabel(color);
            return d;
        }

        private Color PaletteColorAt(int index)
        {
            List<Color> palette = CharEditorColorPickerCompat.Palette();
            return index >= 0 && index < palette.Count ? palette[index] : Color.white;
        }

        /// <summary>Hex phrasing for a swatch with no resolvable name, mirroring the nameless-ColorDef fallback.</summary>
        private static string ColorSwatchLabel(Color color)
        {
            string name = ColorNameHelper.NameForColor(color);
            return !string.IsNullOrEmpty(name)
                ? name
                : "RimWorldAccess.UI.GenericWindow.ColorSwatch".Translate(ColorUtility.ToHtmlStringRGB(color)).ToString();
        }

        private void ActivatePaletteRow(int index)
        {
            Color color = index < PaletteSwatchCount
                ? PaletteColorAt(index)
                : CharEditorColorPickerCompat.DerivedColor(CharEditorColorPickerCompat.GetSelectedColor(dialog), index - PaletteSwatchCount);
            CharEditorColorPickerCompat.ApplyColor(dialog, color);
            SoundDefOf.Click.PlayOneShotOnCamera();
            RefreshModel();
            TolkHelper.SpeakData(ColorSwatchLabel(color) + ".");
        }

        // ------------------------------------------------------------------
        // Region 2: Channel.
        // ------------------------------------------------------------------

        private ElementDescription DescribeChannelRow(int index)
        {
            var d = new ElementDescription();
            bool isFirst = CharEditorColorPickerCompat.GetChannelIsFirst(dialog);
            d.Role = ElementRole.RadioButton;
            if (index == 0)
            {
                d.Label = "RimWorldAccess.CharEd.Color.ChannelA".Translate();
                d.Check = isFirst ? CheckState.Checked : CheckState.Unchecked;
            }
            else
            {
                d.Label = "RimWorldAccess.CharEd.Color.ChannelB".Translate();
                d.Check = !isFirst ? CheckState.Checked : CheckState.Unchecked;
            }
            return d;
        }

        private void ActivateChannelRow(int index)
        {
            bool wantFirst = index == 0;
            if (CharEditorColorPickerCompat.GetChannelIsFirst(dialog) == wantFirst)
            {
                AnnounceCurrentItem();
                return;
            }
            CharEditorColorPickerCompat.SetChannelIsFirst(dialog, wantFirst);
            // Re-seed selectedColor from the newly active channel, mirroring DrawRadioButtons' own
            // branch, so the Values region reflects the channel just switched to rather than the
            // previous channel's color under a new label.
            CharEditorColorPickerCompat.Mode mode = CharEditorColorPickerCompat.GetMode(dialog);
            Pawn pawn = cachedPawn;
            Color seeded = mode == CharEditorColorPickerCompat.Mode.HairColor
                ? CharEditorCompat.GetHairColor(pawn, wantFirst)
                : CharEditorCompat.GetSkinColor(pawn, wantFirst);
            CharEditorColorPickerCompat.ApplyColor(dialog, seeded);
            SoundDefOf.Click.PlayOneShotOnCamera();
            RefreshModel();
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                DescribeContentItem(ChannelRegion, index), TranslatedShellVocabulary.Instance));
        }

        // ------------------------------------------------------------------
        // Region 3: Genes (Biotech only -- REAL mutations, not color previews).
        // ------------------------------------------------------------------

        private ElementDescription DescribeGeneRow(int index)
        {
            var d = new ElementDescription();
            if (index < cachedGenes.Count)
            {
                Gene gene = cachedGenes[index];
                d.Label = "RimWorldAccess.CharEd.Color.AdoptGene".Translate(gene.def.LabelCap).ToString();
                d.Role = ElementRole.Button;
            }
            else
            {
                GeneDef def = CharEditorColorPickerCompat.ClosestGeneDef(dialog);
                d.Label = "RimWorldAccess.CharEd.Color.AddClosestGene".Translate(def?.LabelCap ?? "").ToString();
                d.Role = ElementRole.Button;
            }
            return d;
        }

        private void ActivateGeneRow(int index)
        {
            if (index < cachedGenes.Count)
            {
                Gene gene = cachedGenes[index];
                CharEditorColorPickerCompat.AdoptGeneColor(dialog, gene);
                SoundDefOf.Click.PlayOneShotOnCamera();
                RefreshModel();
                TolkHelper.SpeakData("RimWorldAccess.CharEd.Color.GeneAdopted".Translate(gene.def.LabelCap).ToString());
            }
            else
            {
                CharEditorColorPickerCompat.AddClosestColorAsGene(dialog);
                SoundDefOf.Click.PlayOneShotOnCamera();
                RefreshModel();
                TolkHelper.SpeakData("RimWorldAccess.CharEd.Color.GeneAdded".Translate().ToString());
            }
        }

        /// <summary>Ctrl-remove, promoted to Alt+I on a gene row.</summary>
        private void RemoveGeneAt(int index)
        {
            if (index < 0 || index >= cachedGenes.Count)
                return;
            Gene gene = cachedGenes[index];
            CharEditorColorPickerCompat.RemoveGeneViaSwatch(dialog, gene);
            SoundDefOf.Click.PlayOneShotOnCamera();
            RefreshModel();
            TolkHelper.SpeakData("RimWorldAccess.CharEd.XenoGenes.GeneRemoved".Translate(gene.def.LabelCap).ToString());
        }

        // ------------------------------------------------------------------
        // Region 4: Mode extras (hair's Hair/Beard rows, apparel's worn list, weapon's equipped list).
        // ------------------------------------------------------------------

        private ElementDescription DescribeExtrasRow(int index)
        {
            var d = new ElementDescription();
            CharEditorColorPickerCompat.Mode mode = CharEditorColorPickerCompat.GetMode(dialog);
            if (mode == CharEditorColorPickerCompat.Mode.HairColor)
            {
                d.Role = ElementRole.ComboBox;
                if (index == 0)
                {
                    d.Label = "RimWorldAccess.CharEd.Appearance.HairSection".Translate();
                    d.Value = cachedPawn?.story?.hairDef != null ? cachedPawn.story.hairDef.LabelCap.ToString() : "None".Translate().ToString();
                }
                else
                {
                    d.Label = "Beard".Translate();
                    d.Value = cachedPawn?.style?.beardDef != null ? cachedPawn.style.beardDef.LabelCap.ToString() : "None".Translate().ToString();
                }
                return d;
            }
            if (mode == CharEditorColorPickerCompat.Mode.ApparelColor && index < cachedWorn.Count)
            {
                Apparel a = cachedWorn[index];
                d.Label = a.def.label.CapitalizeFirst();
                d.Role = ElementRole.Button;
                return d;
            }
            if (mode == CharEditorColorPickerCompat.Mode.WeaponColor && index < cachedEquipped.Count)
            {
                ThingWithComps w = cachedEquipped[index];
                d.Label = w.def.label.CapitalizeFirst();
                d.Role = ElementRole.Button;
            }
            return d;
        }

        private void ActivateExtrasRow(int index)
        {
            CharEditorColorPickerCompat.Mode mode = CharEditorColorPickerCompat.GetMode(dialog);
            if (mode == CharEditorColorPickerCompat.Mode.HairColor)
            {
                Window editorWindow = CharEditorCompat.FindEditorWindow();
                if (index == 0)
                {
                    CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.OpenHairPicker(editorWindow));
                }
                else
                {
                    CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.OpenBeardPicker(editorWindow));
                }
                return;
            }
            if (mode == CharEditorColorPickerCompat.Mode.ApparelColor && index < cachedWorn.Count)
            {
                CharEditorColorPickerCompat.SelectApparel(dialog, cachedWorn[index]);
                SoundDefOf.Click.PlayOneShotOnCamera();
                RefreshModel();
                AnnounceCurrentItem();
                return;
            }
            if (mode == CharEditorColorPickerCompat.Mode.WeaponColor && index < cachedEquipped.Count)
            {
                CharEditorColorPickerCompat.SelectWeapon(dialog, cachedEquipped[index]);
                SoundDefOf.Click.PlayOneShotOnCamera();
                RefreshModel();
                AnnounceCurrentItem();
            }
        }
    }
}
