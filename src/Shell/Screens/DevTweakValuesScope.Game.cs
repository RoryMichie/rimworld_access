using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// RimWorld's dev-mode tweak-values window: the scrolling list of every <c>[TweakValue]</c>
    /// static field, a slider for numeric fields or a checkbox for bools, plus an inline
    /// reset-to-initial control.
    ///
    /// ATTACHMENT IS OPT-IN. This is a non-modal EditWindow that coexists with the surface beneath,
    /// and attaching a scope to a non-absorbing window masks the map or menu the player is actually
    /// driving. The factory in <c>ShellBootstrap</c> therefore yields a scope only while
    /// <see cref="Arming"/> is set, which only the deliberate F12 &gt; Development opener does;
    /// opening the window any other way leaves it scopeless.
    ///
    /// Escape is vanilla's: <c>EditWindow</c> inherits <c>closeOnCancel = true</c>, so the base
    /// <see cref="ScreenScope.OwnsCancel"/> lets <c>OnCancelKeyPressed</c> close the window and this
    /// scope pops with it.
    ///
    /// Regions mirror the window's own category grouping: the field list is sorted by
    /// <c>"category.DeclaringType"</c> once in the window ctor, so consecutive same-category fields
    /// group naturally into one content region each. A row's identity is vanilla's
    /// <c>DeclaringType.FieldName</c>. Numeric rows are sliders, bool rows checkboxes; a row whose
    /// value differs from its initial appends the delta so changed rows are audibly marked, and
    /// Space resets it. All writes ride vanilla's <c>SetFromFloat</c> and its
    /// <c>&lt;Field&gt;_Changed</c> companion dispatch.
    /// </summary>
    internal sealed class DevTweakValuesScope : ScreenScope
    {
        /// <summary>
        /// Set by the F12 &gt; Development opener around the attach path so the
        /// <see cref="ScopeForWindow"/> factory yields a scope for that one deliberate open, and
        /// cleared in a finally so a non-armed open can never inherit it.
        /// </summary>
        internal static bool Arming;

        private enum Kind { Float, Int, Ushort, Bool, Unknown }

        private static readonly FieldInfo TweakFieldsField =
            AccessTools.Field(typeof(EditWindow_TweakValues), "tweakValueFields");
        private static readonly MethodInfo SetFromFloatMethod =
            AccessTools.Method(typeof(EditWindow_TweakValues), "SetFromFloat");
        private static readonly AccessTools.FieldRef<EditWindow_TweakValues, Vector2> ScrollPositionRef =
            AccessTools.FieldRefAccess<EditWindow_TweakValues, Vector2>("scrollPosition");
        private static readonly Func<Window, float> MarginOf =
            AccessTools.MethodDelegate<Func<Window, float>>(AccessTools.PropertyGetter(typeof(Window), "Margin"));

        // Fields of the private nested struct TweakInfo, resolved off the list's element type.
        private static readonly System.Type TweakInfoType =
            TweakFieldsField.FieldType.GetGenericArguments()[0];
        private static readonly FieldInfo TiFieldField = AccessTools.Field(TweakInfoType, "field");
        private static readonly FieldInfo TiTweakValueField = AccessTools.Field(TweakInfoType, "tweakValue");
        private static readonly FieldInfo TiInitialField = AccessTools.Field(TweakInfoType, "initial");

        // Digits only, optionally signed, plus a decimal point for float. minLength 0 so the buffer
        // may be cleared mid-edit; the clamp to the attribute's min/max is the harvested game limit.
        private static readonly TextFieldSpec IntSpec = new TextFieldSpec(
            labelKey: null, minLength: 0, allowedChars: new Regex("^-?[0-9]*$"));
        private static readonly TextFieldSpec FloatSpec = new TextFieldSpec(
            labelKey: null, minLength: 0, allowedChars: new Regex(@"^-?[0-9]*\.?[0-9]*$"));

        private sealed class TweakRow
        {
            public FieldInfo Field;
            public string Identity;
            /// <summary>Position in the window's flat draw order, which the category groups break up.</summary>
            public int DrawIndex;
            public float Min;
            public float Max;
            public float Initial;
            public Kind Kind;
            public MethodInfo Changed;
        }

        private sealed class CategoryGroup
        {
            public string Name;
            public readonly List<TweakRow> Rows = new List<TweakRow>();
        }

        private readonly EditWindow_TweakValues window;
        private readonly List<CategoryGroup> groups = new List<CategoryGroup>();
        private readonly TextFieldEditSession editSession = new TextFieldEditSession();

        private bool built;
        private bool announcedOpen;
        private int editRegion = -1;
        private int editIndex = -1;

        public DevTweakValuesScope(EditWindow_TweakValues window)
        {
            this.window = window;

            // Space resets a tweak row to its initial value, the same reset vanilla's inline control
            // performs on click; the when-gate keeps Space falling through on the Buttons row.
            Claim("devTweak.reset", delegate { ResetCurrentRow(); }, when: OnTweakRow);

            RegisterPopTeardown(editSession.CancelIfActive);
        }

        public override string Name => "dev-tweak";

        /// <summary>The tweakable fields are named data worth searching by field name.</summary>
        protected override bool EnableTypeahead => true;

        /// <summary>EditWindow_TweakValues draws its controls via DevGUI, not Widgets.ButtonText, so there is nothing to scrape.</summary>
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

        // ScreenScope content contract.

        protected override int ContentRegionCount => groups.Count;

        protected override string ContentRegionName(int region)
        {
            if (region < 0 || region >= groups.Count)
            {
                return "";
            }
            string name = groups[region].Name;
            return string.IsNullOrEmpty(name)
                ? "RimWorldAccess.Dev.TweakRegion".Translate().ToString()
                : name;
        }

        protected override int ContentItemCount(int region)
        {
            if (region < 0 || region >= groups.Count)
            {
                return 0;
            }
            return groups[region].Rows.Count;
        }

        /// <summary>
        /// Builds the category-group snapshot once from the window's static <c>tweakValueFields</c>
        /// list, which is built lazily in the window ctor and never changes, so the structure is
        /// stable for this scope's whole life. Live VALUES are read fresh in
        /// <see cref="DescribeContentItem"/>.
        /// </summary>
        protected override void RefreshContent()
        {
            if (built)
            {
                return;
            }
            built = true;

            IEnumerable list = TweakFieldsField.GetValue(null) as IEnumerable;
            if (list == null)
            {
                return;
            }

            CategoryGroup current = null;
            int drawIndex = -1;
            foreach (object boxed in list)
            {
                drawIndex++;
                var field = (FieldInfo)TiFieldField.GetValue(boxed);
                var attr = (TweakValue)TiTweakValueField.GetValue(boxed);
                float initial = (float)TiInitialField.GetValue(boxed);
                if (field == null || attr == null)
                {
                    continue;
                }

                var row = new TweakRow
                {
                    Field = field,
                    Identity = field.DeclaringType.Name + "." + field.Name,
                    DrawIndex = drawIndex,
                    Min = attr.min,
                    Max = attr.max,
                    Initial = initial,
                    Kind = KindOf(field.FieldType),
                    Changed = field.DeclaringType.GetMethod(field.Name + "_Changed",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
                };

                if (current == null || current.Name != attr.category)
                {
                    current = new CategoryGroup { Name = attr.category ?? "" };
                    groups.Add(current);
                }
                current.Rows.Add(row);
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            TweakRow row = RowAt(region, index);
            if (row == null)
            {
                return d;
            }
            d.Label = row.Identity;

            if (row.Kind == Kind.Bool)
            {
                bool on = (bool)row.Field.GetValue(null);
                d.Role = ElementRole.Checkbox;
                d.Check = on ? CheckState.Checked : CheckState.Unchecked;
                if ((on ? 1f : 0f) != row.Initial)
                {
                    d.Extras = "RimWorldAccess.Dev.TweakChanged".Translate(
                        StateWord(on), StateWord(row.Initial != 0f)).ToString();
                }
                return d;
            }

            if (row.Kind == Kind.Unknown)
            {
                object raw = row.Field.GetValue(null);
                d.Value = raw == null ? "" : raw.ToString();
                d.ReadOnly = true;
                return d;
            }

            float value = CurrentFloat(row);
            string valueStr = value.ToString(CultureInfo.InvariantCulture);
            d.Role = ElementRole.Slider;
            d.Value = value != row.Initial
                ? "RimWorldAccess.Dev.TweakChanged".Translate(
                    valueStr, row.Initial.ToString(CultureInfo.InvariantCulture)).ToString()
                : valueStr;
            d.AtMinimum = value <= row.Min;
            d.AtMaximum = value >= row.Max;
            return d;
        }

        // The focus ring, rebuilt from the window's own layout: the window draws nothing per-row the
        // capture engines can see, but its layout is closed-form — a uniform row height under
        // GameFont.Small, rows sequential from y=0 inside a scroll view at inRect.ContractedBy(4f)
        // whose content width drops the scrollbar gutter.

        private const float ScrollViewInset = 4f;
        private const float ScrollBarGutter = 33f;
        private const float TitleHeight = 25f;

        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return default(Rect);
            }
            TweakRow row = RowAt(Model.RegionIndex, region.Index);
            if (row == null)
            {
                return default(Rect);
            }

            Rect outRect = WindowContentRect().ContractedBy(ScrollViewInset);
            float rowHeight = RowHeight();
            float y = outRect.y + row.DrawIndex * rowHeight - ScrollPositionRef(window).y;
            float yMin = Mathf.Max(y, outRect.yMin);
            float yMax = Mathf.Min(y + rowHeight, outRect.yMax);
            if (yMax <= yMin)
            {
                return default(Rect);
            }
            return GuiSpace.ToScreen(
                new Rect(outRect.x, yMin, outRect.width - ScrollBarGutter, yMax - yMin));
        }

        /// <summary>
        /// The rect Window.InnerWindowOnGUI hands DoWindowContents, in window-local space: contracted
        /// by the window's Margin and pushed down by Margin plus the title bar this window carries.
        /// The contents group has already closed by the time the ring draws, so the offset is
        /// recomputed rather than inherited from the clip stack.
        /// </summary>
        private Rect WindowContentRect()
        {
            float margin = MarginOf(window);
            float titleOffset = string.IsNullOrEmpty(window.optionalTitle) ? 0f : margin + TitleHeight;
            return new Rect(
                margin,
                margin + titleOffset,
                window.windowRect.width - margin * 2f,
                window.windowRect.height - margin * 2f - titleOffset);
        }

        /// <summary>The window's uniform row height, measured under the font its own draw sets first.</summary>
        private static float RowHeight()
        {
            GameFont font = Text.Font;
            Text.Font = GameFont.Small;
            float height = Text.CalcHeight("test", 1000f);
            Text.Font = font;
            return height;
        }

        // Left/Right step numeric values. Bools have no direction-sensitive adjust, as vanilla
        // checkboxes do not either; Enter and Space toggle them through ActivateContentItem.

        protected override bool CanAdjustContentItem(int region, int index)
        {
            TweakRow row = RowAt(region, index);
            return row != null && row.Kind != Kind.Unknown && row.Kind != Kind.Bool;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            TweakRow row = RowAt(region, index);
            if (row == null)
            {
                return;
            }

            float stepped = SliderStep.Stepped(CurrentFloat(row), direction, row.Min, row.Max, RoundTo(row.Kind));
            WriteValue(row, stepped);
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            RefreshModel();
            TolkHelper.SpeakData(CurrentFloat(row).ToString(CultureInfo.InvariantCulture));
        }

        // Enter: toggle a bool, or open exact numeric entry.

        protected override void ActivateContentItem(int region, int index)
        {
            TweakRow row = RowAt(region, index);
            if (row == null)
            {
                return;
            }

            if (row.Kind == Kind.Bool)
            {
                bool on = (bool)row.Field.GetValue(null);
                WriteValue(row, on ? 0f : 1f);
                RefreshModel();
                TolkHelper.SpeakData(StateWord(!on));
                return;
            }

            if (row.Kind == Kind.Unknown)
            {
                AnnounceCurrentItem();
                return;
            }

            editRegion = region;
            editIndex = index;
            editSession.EnterEdit(
                CurrentFloat(row).ToString(CultureInfo.InvariantCulture),
                row.Kind == Kind.Float ? FloatSpec : IntSpec,
                row.Identity,
                ApplyEdit,
                OnEditExit,
                announcePrompt: true);
        }

        /// <summary>Writes the typed value into the field, once on Enter-confirm and never on Escape; <see cref="WriteValue"/> clamps.</summary>
        private void ApplyEdit(string value)
        {
            TweakRow row = RowAt(editRegion, editIndex);
            if (row != null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            {
                WriteValue(row, v);
            }
            RefreshModel();
        }

        private void OnEditExit()
        {
            AnnounceCurrentItem();
        }

        // Space: reset the current row to its initial value.

        /// <summary>True only on a content row, so Space falls through on the Buttons row.</summary>
        private bool OnTweakRow()
        {
            RefreshModel();
            if (Model.RegionIndex < 0 || Model.RegionIndex >= groups.Count)
            {
                return false;
            }
            ListModel region = Model.CurrentRegion;
            return region != null && !region.IsEmpty;
        }

        private void ResetCurrentRow()
        {
            RefreshModel();
            TweakRow row = CurrentRow();
            if (row == null)
            {
                return;
            }
            if (CurrentFloat(row) == row.Initial)
            {
                TolkHelper.SpeakData("RimWorldAccess.Dev.TweakUnchanged".Translate().ToString());
                return;
            }
            WriteValue(row, row.Initial);
            RefreshModel();
            TolkHelper.SpeakData("RimWorldAccess.Dev.TweakReset".Translate(InitialDisplay(row)).ToString());
        }

        // The write path.

        /// <summary>
        /// Writes <paramref name="value"/> into the field along vanilla's own typed-write path.
        /// Numeric values are clamped first to the attribute's declared [min, max], the bound
        /// vanilla's <c>HorizontalSlider</c> enforces; bools are 0/1 and never clamped. The write
        /// invokes the window's own <c>SetFromFloat</c>, then the <c>&lt;Field&gt;_Changed</c>
        /// companion.
        /// </summary>
        private void WriteValue(TweakRow row, float value)
        {
            if (row.Kind == Kind.Float || row.Kind == Kind.Int || row.Kind == Kind.Ushort)
            {
                if (value < row.Min)
                {
                    value = row.Min;
                }
                if (value > row.Max)
                {
                    value = row.Max;
                }
            }
            // The window's own SetFromFloat, the identical typed write DoWindowContents runs for the
            // slider and checkbox, then vanilla's <Field>_Changed companion. DoWindowContents decides
            // whether to invoke the companion from the pre- versus post-write value and skips it when
            // they are equal; mirror that, since the clamp above can leave the field unchanged even
            // though the caller asked for a different value.
            float before = CurrentFloat(row);
            SetFromFloatMethod.Invoke(window, new object[] { row.Field, value });
            if (CurrentFloat(row) != before)
            {
                row.Changed?.Invoke(null, null);
            }
        }

        // Row helpers.

        private TweakRow CurrentRow()
        {
            if (Model.RegionIndex < 0 || Model.RegionIndex >= groups.Count)
            {
                return null;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return null;
            }
            return RowAt(Model.RegionIndex, region.Index);
        }

        private TweakRow RowAt(int region, int index)
        {
            if (region < 0 || region >= groups.Count)
            {
                return null;
            }
            List<TweakRow> rows = groups[region].Rows;
            if (index < 0 || index >= rows.Count)
            {
                return null;
            }
            return rows[index];
        }

        private float CurrentFloat(TweakRow row)
        {
            switch (row.Kind)
            {
                case Kind.Float:
                    return (float)row.Field.GetValue(null);
                case Kind.Int:
                    return (int)row.Field.GetValue(null);
                case Kind.Ushort:
                    return (ushort)row.Field.GetValue(null);
                case Kind.Bool:
                    return (bool)row.Field.GetValue(null) ? 1f : 0f;
                default:
                    return row.Initial;
            }
        }

        private string InitialDisplay(TweakRow row)
        {
            return row.Kind == Kind.Bool
                ? StateWord(row.Initial != 0f)
                : row.Initial.ToString(CultureInfo.InvariantCulture);
        }

        private static float RoundTo(Kind kind)
        {
            // Whole-number step for integer fields; -1 lets SliderStep pick its default for floats.
            return kind == Kind.Float ? -1f : 1f;
        }

        private static Kind KindOf(System.Type type)
        {
            if (type == typeof(float))
            {
                return Kind.Float;
            }
            if (type == typeof(int))
            {
                return Kind.Int;
            }
            if (type == typeof(ushort))
            {
                return Kind.Ushort;
            }
            if (type == typeof(bool))
            {
                return Kind.Bool;
            }
            return Kind.Unknown;
        }

        private static string StateWord(bool on)
        {
            return (on
                ? "RimWorldAccess.Shell.State.Checked"
                : "RimWorldAccess.Shell.State.Unchecked").Translate().ToString();
        }
    }
}
