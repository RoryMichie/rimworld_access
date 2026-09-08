using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for vanilla's <see cref="Dialog_StyleSelection"/>
    /// (the Architect tab's "change style" icon strip) — DLC coverage
    /// remediation, finding G2. This dialog draws only
    /// <c>Widgets.ButtonImage</c> calls (each selected style category's icon,
    /// plus the "add" plus-icon), which the generic capture engine explicitly
    /// SKIPS (<see cref="WidgetCapture"/>'s own coverage remarks) — the
    /// generic reader cannot drive it at all, matching the audit's "activation
    /// unverified" note.
    ///
    /// One content region ("Styles in use"): one row per
    /// <c>Find.IdeoManager.selectedStyleCategories</c> entry, plus a trailing
    /// "Add style category" row while the list has fewer than 3 (vanilla's
    /// own <c>MaxButtonCount</c>). Enter on either kind opens a REAL
    /// <see cref="FloatMenu"/> built from the SAME options vanilla's own click
    /// handler builds (decompiled :53-73 for a slot, :85-104 for Add) — the
    /// menu is intercepted to <c>WindowlessFloatMenuState</c> by the existing
    /// <c>DialogInterceptionPatch</c> like any other game-spawned float menu,
    /// so it is fully keyboard-driven for free.
    ///
    /// MUTATION-C: <c>Find.IdeoManager.selectedStyleCategories.Replace/Remove/Add</c>
    /// are the same public list-mutator calls vanilla's own delegates use (no
    /// gate exists to bypass — vanilla itself validates nothing beyond "not
    /// already selected"), but the delegate bodies are re-authored here rather
    /// than extracted from vanilla's compiled closures (not reflectable
    /// cleanly). <c>ClearCaches()</c> is invoked via reflection on the LIVE
    /// dialog instance (not hand-copied) so any future change to its body
    /// stays in sync automatically.
    ///
    /// <b>Escape — deviates from vanilla (accessibility UX over vanilla
    /// parity).</b> The dialog sets <c>closeOnCancel = false</c>
    /// (decompiled :34) and never overrides <c>OnCancelKeyPressed</c>, so
    /// vanilla's own Escape does NOTHING — the popup is designed to dismiss
    /// only via a mouse click outside it or by drifting the mouse more than
    /// 145px away (<c>UpdateBaseColor</c>'s fade-and-close). A keyboard user
    /// has no mouse to drift, so this scope claims Escape to close the dialog
    /// directly through its own public <c>Close()</c> — the same method
    /// vanilla's click-outside path calls.
    /// </summary>
    public sealed class StyleSelectionScope : ScreenScope
    {
        /// <summary>Vanilla's own cap on selected style categories (decompiled Dialog_StyleSelection.cs:17, a private const with no accessor).</summary>
        private const int MaxButtonCount = 3;

        private static readonly MethodInfo clearCachesMethod =
            AccessTools.Method(typeof(Dialog_StyleSelection), "ClearCaches");
        private static readonly System.Func<Window, float> MarginOf =
            AccessTools.MethodDelegate<System.Func<Window, float>>(AccessTools.PropertyGetter(typeof(Window), "Margin"));

        /// <summary>Vanilla's tile pitch and size (decompiled Dialog_StyleSelection.cs:80, tile Rects at (i*28f, ..., 24f, 24f)).</summary>
        private const float TilePitch = 28f;
        private const float TileSize = 24f;

        /// <summary>Vanilla's tile-row y offset below the label (decompiled Dialog_StyleSelection.cs:44-80: label at y 0, tiles at Text.LineHeight + 10f).</summary>
        private const float TileRowYOffset = 10f;

        private readonly Dialog_StyleSelection dialog;
        private bool announcedOpen;

        public StyleSelectionScope(Dialog_StyleSelection dialog)
        {
            this.dialog = dialog;
            Claim(SharedMenuGrammar.Cancel, delegate
            {
                ShellFrameStamps.MarkCancelConsumed();
                dialog.Close();
            });
        }

        public override string Name
        {
            get { return "style-selection"; }
        }

        /// <summary>See the class remarks: vanilla's own Escape is inert here.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (!announcedOpen)
            {
                announcedOpen = true;
                AnnounceRegion();
                return;
            }
            // A float menu (swap/remove/add) just resolved and handed focus
            // back — the row set may have changed shape.
            AnnounceCurrentItem();
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "StylesInUse".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            int selected = Find.IdeoManager.selectedStyleCategories.Count;
            return selected + (selected < MaxButtonCount ? 1 : 0);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            List<StyleCategoryDef> selected = Find.IdeoManager.selectedStyleCategories;
            ElementDescription d = new ElementDescription();
            d.Role = ElementRole.Button;
            if (index < selected.Count)
            {
                StyleCategoryDef cat = selected[index];
                d.Label = cat.LabelCap;
                d.Extras = FlattenNewlines(IdeoUIUtility.StyleTooltip(
                    cat, IdeoEditMode.GameStart, null, new List<ThingStyleCategoryWithPriority>(), skipDominanceDesc: true).Resolve());
            }
            else
            {
                d.Label = "AddStyleCategory".Translate().ToString().CapitalizeFirst();
                d.Extras = FlattenNewlines("StyleCategoryDescriptionAbstract".Translate().ToString());
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            List<StyleCategoryDef> selected = Find.IdeoManager.selectedStyleCategories;
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            if (index < selected.Count)
            {
                // Decompiled Dialog_StyleSelection.cs:53-73 — one swap option
                // per not-yet-selected category, plus Remove.
                StyleCategoryDef cat = selected[index];
                foreach (StyleCategoryDef candidate in DefDatabase<StyleCategoryDef>.AllDefs)
                {
                    if (candidate == cat || selected.Contains(candidate))
                    {
                        continue;
                    }
                    StyleCategoryDef captured = candidate;
                    options.Add(new FloatMenuOption(captured.LabelCap, delegate
                    {
                        selected.Replace(cat, captured);
                        InvokeClearCaches();
                    }, captured.Icon, Color.white));
                }
                options.Add(new FloatMenuOption("Remove".Translate().CapitalizeFirst(), delegate
                {
                    selected.Remove(cat);
                    InvokeClearCaches();
                }));
            }
            else
            {
                // Decompiled Dialog_StyleSelection.cs:85-104 — one Add option
                // per not-yet-selected category.
                foreach (StyleCategoryDef candidate in DefDatabase<StyleCategoryDef>.AllDefs)
                {
                    if (selected.Contains(candidate))
                    {
                        continue;
                    }
                    StyleCategoryDef captured = candidate;
                    options.Add(new FloatMenuOption(captured.LabelCap, delegate
                    {
                        selected.Add(captured);
                        InvokeClearCaches();
                    }, captured.Icon, Color.white));
                }
                if (options.Count == 0)
                {
                    return;
                }
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void InvokeClearCaches()
        {
            clearCachesMethod?.Invoke(dialog, null);
        }

        /// <summary>
        /// Closed-form ring rect, mirroring decompiled Dialog_StyleSelection.cs:40-114:
        /// DoWindowContents runs GUI.BeginGroup(inRect.ContractedBy(Margin)) where inRect
        /// is ITSELF the window rect already contracted by Margin (the DOUBLE margin, :40),
        /// then draws tile i at (i*28f, Text.LineHeight + 10f, 24f, 24f) for each selected
        /// category (:80) followed by the Add tile at the next pitch slot while Count &lt; 3.
        /// </summary>
        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || Model.RegionIndex != 0)
            {
                return default(Rect);
            }

            List<StyleCategoryDef> selected = Find.IdeoManager.selectedStyleCategories;
            int expectedRows = selected.Count + (selected.Count < MaxButtonCount ? 1 : 0);
            if (ContentItemCount(0) != expectedRows)
            {
                return default(Rect);
            }

            GameFont font = Text.Font;
            Text.Font = GameFont.Small;
            float lineHeight = Text.LineHeight;
            Text.Font = font;

            float margin = MarginOf(dialog);
            float x = margin * 2f + region.Index * TilePitch;
            float y = margin * 2f + lineHeight + TileRowYOffset;
            return GuiSpace.ToScreen(new Rect(x, y, TileSize, TileSize));
        }

        /// <summary>Vanilla's style tooltips use literal "\n\n" paragraph breaks; announcements never use newlines as separators.</summary>
        private static string FlattenNewlines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return text.StripTags().Replace("\n\n", ". ").Replace("\n", " ");
        }
    }
}
