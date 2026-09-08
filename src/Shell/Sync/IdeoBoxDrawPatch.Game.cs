using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using DeityType = RimWorld.IdeoFoundation_Deity.Deity;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focus-ring and scroll-follow source for the three windowless ideoligion overlay editors
    /// (issue-based precepts, one typed precept list, deities). They draw nothing themselves — the
    /// host page underneath keeps rendering the whole ideoligion — so the ring rides the host's own
    /// boxes as vanilla draws them, a windowless scope having no owned window for
    /// <c>FocusedContentRect</c> to answer with.
    ///
    /// Precept boxes funnel through <c>Precept.DrawPreceptBox</c> (decompiled Precept.cs:616); its
    /// one override calls base, so patching the declaring type covers every precept kind. Meme boxes
    /// funnel through <c>IdeoUIUtility.DoMeme</c> (:1186); the exception is the STRUCTURE meme, which
    /// <c>DoMemes</c> filters out (:1143-1149) and <c>DoName</c> paints inline, so
    /// <see cref="RecordNameIcons"/> feeds it into the same family and one <c>MemeDef</c> identity
    /// answers wherever it is drawn. Deity boxes have no per-box method (<c>DoInfo</c> draws them
    /// inline, :98-190), so a bracket arms recording and each box's own
    /// <c>Widgets.DrawLightHighlight</c> (:137) contributes one rect in deity order. Style tiles,
    /// appearance boxes, the faction icon row, and the name/symbol block carry no identity+rect method
    /// either, so each is reconstructed from its vanilla layout (cited at every recorder) and
    /// published through <see cref="RecordExtraBox"/>.
    ///
    /// Every recorder keeps TWO rects. The screen rect is clip-aware and empties as a box scrolls out
    /// of the pane, right for a ring but useless for deciding where to scroll TO; the raw rect is
    /// view-space inside <c>DoIdeoDetails</c>' scroll view, which is what <see cref="FollowTarget"/>
    /// answers with and <see cref="ApplyFollow"/> clamps against.
    ///
    /// Recording runs while any consumer has declared interest
    /// (<see cref="AddInterest"/>/<see cref="RemoveInterest"/> from its own OnPush/OnPop). Counted
    /// rather than flagged: the issue editor and a typed precept list are separate scopes over the
    /// same host page, and the first to pop must not silence the one still driving.
    /// </summary>
    internal static class IdeoBoxDrawPatch
    {
        /// <summary>The precept the ring belongs on, published by the two precept scopes.</summary>
        internal static Func<Precept> PreceptRingProvider;

        /// <summary>The deity the ring belongs on, published by <see cref="IdeoDeityScreenScope"/>.</summary>
        internal static Func<DeityType> DeityRingProvider;

        /// <summary>The whole faction icon row of the details pane, which has no per-row object of its own.</summary>
        internal static readonly object FactionsRowKey = new object();

        /// <summary>The name/symbol block of the details pane, likewise identified by nothing vanilla owns.</summary>
        internal static readonly object NameSymbolKey = new object();

        /// <summary>Boxed once: the identity tables compare by reference, and a per-frame box would not.</summary>
        internal static readonly object HairAndBeardKey = StyleItemTab.HairAndBeard;

        /// <summary>Boxed once, like <see cref="HairAndBeardKey"/>.</summary>
        internal static readonly object TattooKey = StyleItemTab.Tattoo;

        private static readonly RowDrawCapture Precepts = new RowDrawCapture();
        private static readonly RowDrawCapture Memes = new RowDrawCapture();
        private static readonly RowDrawCapture Extras = new RowDrawCapture();
        private static readonly RawBoxCapture RawBoxes = new RawBoxCapture();
        private static readonly List<Rect> deityBoxes = new List<Rect>();
        private static readonly List<Rect> deityScreenBoxes = new List<Rect>();
        private static IdeoFoundation_Deity deityBoxFoundation;

        /// <summary>Read once per <c>Widgets.DrawLightHighlight</c> call game-wide, so it stays a plain field.</summary>
        internal static bool RecordingDeityBoxes;

        /// <summary>Read once per <c>DrawFactionIconWithTooltip</c> call game-wide, for the same reason.</summary>
        internal static bool RecordingFactionIcons;

        private static Rect factionRowBox;
        private static bool hasFactionRow;

        private static int interest;

        private static Precept passFocus;
        private static int passFocusFrame = -1;
        private static EventType passFocusEvent = EventType.Ignore;

        internal static void AddInterest()
        {
            interest++;
        }

        internal static void RemoveInterest()
        {
            if (interest > 0)
            {
                interest--;
            }
        }

        internal static bool HasInterest
        {
            get { return interest > 0; }
        }

        internal static void RecordPreceptBox(Precept precept, Rect preceptBox)
        {
            try
            {
                if (interest == 0 || precept == null)
                {
                    return;
                }
                Rect visible = GuiSpace.VisibleScreenRect(preceptBox);
                Precepts.Record(precept, preceptBox);
                RawBoxes.Record(precept, preceptBox);

                if (!ReferenceEquals(FocusedPrecept(), precept))
                {
                    return;
                }
                // Nothing stops a host from drawing one ideoligion twice in a pass; ring the
                // first box only, so the pointer has one target.
                if (Precepts.FindFirst(precept) != visible)
                {
                    return;
                }
                FocusRing.Draw(preceptBox);
                UiPointerFollow.NotifyFocusedRect(visible);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Precept box capture error", ex);
            }
        }

        internal static void RecordMemeBox(MemeDef meme, Rect memeBox)
        {
            try
            {
                if (interest == 0 || meme == null)
                {
                    return;
                }
                Memes.Record(meme, memeBox);
                RawBoxes.Record(meme, memeBox);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Meme box capture error", ex);
            }
        }

        /// <summary>The shared recorder for the four families reconstructed from layout math; past this point they are indistinguishable from a precept or a meme.</summary>
        private static void RecordExtraBox(object identity, Rect box)
        {
            if (identity == null)
            {
                return;
            }
            // A Layout pass is where vanilla's geometry is still provisional (DoIdeoDetails sizes
            // its view rect there), so reconstructed math cannot be trusted yet.
            Event ev = Event.current;
            if (ev != null && ev.type == EventType.Layout)
            {
                return;
            }
            Extras.Record(identity, box);
            RawBoxes.Record(identity, box);
        }

        /// <summary>
        /// Records the structure meme and culture icons <c>IdeoUIUtility.DoName</c> draws inline
        /// (decompiled :600-622): two 35f squares at the ENTRY <c>curY</c>, side by side from
        /// <c>x2 = (width - PreceptBoxSize.x * 3f - 16f) / 2f</c>, the culture one 4f past the
        /// structure one. The structure icon joins the MEME family, not the reconstructed one, so
        /// one <c>MemeDef</c> identity answers on every host (<c>Dialog_ReformIdeo</c> draws its own
        /// through <c>DoMeme</c>).
        /// </summary>
        internal static void RecordNameIcons(Ideo ideo, float entryY, float width)
        {
            try
            {
                if (interest == 0 || ideo == null)
                {
                    return;
                }
                // Reconstructed geometry, so skip the Layout pass as RecordExtraBox does;
                // RecordMemeBox below has no skip of its own.
                Event ev = Event.current;
                if (ev != null && ev.type == EventType.Layout)
                {
                    return;
                }
                float x = (width - IdeoUIUtility.PreceptBoxSize.x * 3f - 16f) / 2f;
                if (ideo.StructureMeme != null)
                {
                    RecordMemeBox(ideo.StructureMeme, new Rect(x, entryY, 35f, 35f));
                }
                if (ideo.culture != null)
                {
                    RecordExtraBox(ideo.culture, new Rect(x + 35f + 4f, entryY, 35f, 35f));
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Ideo name icon capture error", ex);
            }
        }

        /// <summary>
        /// Records the style category tiles of <c>IdeoUIUtility.DoStyles</c> (decompiled :775-782):
        /// three fixed slots, <c>curX</c> advancing one icon per slot whichever branch runs, and only
        /// the first <c>thingStyleCategories.Count</c> carrying a real category. Reconstructed
        /// because the tiles' <c>ButtonInvisible</c> is short-circuited outside edit mode.
        /// </summary>
        internal static void RecordStyleTiles(Ideo ideo, float startX, float startY, int styleIconSize)
        {
            try
            {
                List<ThingStyleCategoryWithPriority> categories = ideo != null ? ideo.thingStyleCategories : null;
                if (interest == 0 || categories == null)
                {
                    return;
                }
                int count = Mathf.Min(3, categories.Count);
                for (int i = 0; i < count; i++)
                {
                    StyleCategoryDef category = categories[i].category;
                    if (category != null)
                    {
                        RecordExtraBox(category, new Rect(startX + i * styleIconSize, startY, styleIconSize, styleIconSize));
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Ideo style tile capture error", ex);
            }
        }

        /// <summary>Records the hair and tattoo boxes of <c>DoAppearanceItems</c> (decompiled :1996-2038). The label above has a dynamic height, but <c>curY</c> exits exactly one box plus 17f below their top, so the exit value pins them without measuring.</summary>
        internal static void RecordAppearanceBoxes(float exitY)
        {
            try
            {
                if (interest == 0)
                {
                    return;
                }
                Vector2 size = IdeoUIUtility.PreceptBoxSize;
                float boxY = exitY - size.y - 17f;
                RecordExtraBox(HairAndBeardKey, new Rect(4f, boxY, size.x, size.y));
                RecordExtraBox(TattooKey, new Rect(4f + size.x + 8f, boxY, size.x, size.y));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Ideo appearance box capture error", ex);
            }
        }

        /// <summary>Arms the faction icon bracket. <c>DoFactionIcons</c> serves both the ideo list rows (decompiled :495, no label) and the details pane (:968, labelled), and only the latter is a row the cursor can sit on, so the label tells them apart.</summary>
        internal static void BeginFactionIcons(string label)
        {
            hasFactionRow = false;
            RecordingFactionIcons = interest > 0 && label != null;
        }

        /// <summary>Grows the row to cover one more icon (decompiled :1059, the bracket's only tap).</summary>
        internal static void RecordFactionIcon(Rect icon)
        {
            factionRowBox = hasFactionRow
                ? Rect.MinMaxRect(
                    Mathf.Min(factionRowBox.xMin, icon.xMin),
                    Mathf.Min(factionRowBox.yMin, icon.yMin),
                    Mathf.Max(factionRowBox.xMax, icon.xMax),
                    Mathf.Max(factionRowBox.yMax, icon.yMax))
                : icon;
            hasFactionRow = true;
        }

        /// <summary>Publishes the whole icon row as one box, still inside the pane's own clip.</summary>
        internal static void EndFactionIcons()
        {
            try
            {
                if (hasFactionRow)
                {
                    RecordExtraBox(FactionsRowKey, factionRowBox);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Ideo faction icon capture error", ex);
            }
            finally
            {
                RecordingFactionIcons = false;
            }
        }

        /// <summary>Records the hoverable name/symbol block of <c>DoNameAndSymbol</c> (decompiled :729-773, rect3 at :760). Its width follows the wider of the two text lines, each measured under the font vanilla draws it with; <c>curY</c> exits 80f below the block's top.</summary>
        internal static void RecordNameSymbolBlock(float exitY, float width, Ideo ideo, IdeoEditMode editMode)
        {
            try
            {
                if (interest == 0 || ideo == null)
                {
                    return;
                }
                GameFont font = Text.Font;
                float x;
                try
                {
                    Text.Font = GameFont.Medium;
                    x = Text.CalcSize(ideo.name).x;
                    Text.Font = GameFont.Small;
                    x = Mathf.Max(x, Text.CalcSize(ideo.adjective.CapitalizeFirst() + " / " + ideo.memberName.CapitalizeFirst()).x);
                }
                finally
                {
                    Text.Font = font;
                }
                x = Mathf.Min(x, editMode != IdeoEditMode.None ? 220f : 275f);
                RecordExtraBox(NameSymbolKey, new Rect((width - (87f + x)) / 2f, exitY - 80f, x + 91f, 70f));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Ideo name and symbol capture error", ex);
            }
        }

        /// <summary>The focused precept, cached per draw pass: the issue editor's provider rebuilds the issue's current-precept list on every call.</summary>
        private static Precept FocusedPrecept()
        {
            Event ev = Event.current;
            EventType type = ev == null ? EventType.Ignore : ev.type;
            if (Time.frameCount == passFocusFrame && type == passFocusEvent)
            {
                return passFocus;
            }
            passFocusFrame = Time.frameCount;
            passFocusEvent = type;
            Func<Precept> provider = PreceptRingProvider;
            passFocus = provider != null ? provider() : null;
            return passFocus;
        }

        internal static void BeginDeityBoxes(IdeoFoundation_Deity foundation)
        {
            deityBoxes.Clear();
            deityScreenBoxes.Clear();
            deityBoxFoundation = foundation;
            RecordingDeityBoxes = interest > 0;
        }

        internal static void RecordDeityBox(Rect box)
        {
            deityBoxes.Add(box);
            // The screen rect must be taken here, inside the box's own clip context; a reader after
            // the pass cannot reconstruct it. The two lists stay parallel by ordinal.
            deityScreenBoxes.Add(GuiSpace.VisibleScreenRect(box));
        }

        /// <summary>The box a details-tree row belongs to: the row's own recorded identity, else its nearest ancestor's, so a detail line rings the box it describes. Null for a row with no recorded box.</summary>
        internal static object RingIdentityFor(InspectionTreeItem item, InspectionTreeItem root)
        {
            InspectionTreeItem node = item;
            while (node != null && node != root)
            {
                if (node.RingTarget != null)
                {
                    return node.RingTarget;
                }
                node = node.Parent;
            }
            return null;
        }

        /// <summary>The FIRST screen rect this pass drew for any recorded identity, or empty.</summary>
        internal static Rect ScreenRectFor(object identity)
        {
            if (identity is Precept)
            {
                return Precepts.FindFirst(identity);
            }
            if (identity is MemeDef)
            {
                return Memes.FindFirst(identity);
            }
            var deity = identity as DeityType;
            if (deity == null)
            {
                return Extras.FindFirst(identity);
            }
            int deityIndex = DeityBoxIndex(deity);
            return deityIndex >= 0 ? deityScreenBoxes[deityIndex] : default(Rect);
        }

        /// <summary>The raw view-space rect this pass drew for a precept, meme, or deity, or null.</summary>
        internal static Rect? RawRectFor(object identity)
        {
            var deity = identity as DeityType;
            if (deity == null)
            {
                return RawBoxes.FindFirst(identity);
            }
            int index = DeityBoxIndex(deity);
            return index >= 0 ? deityBoxes[index] : (Rect?)null;
        }

        /// <summary>The deity's box ordinal within the foundation the pass drew, or -1 when a count mismatch makes the whole ordinal mapping untrustworthy (see <see cref="EndDeityBoxes"/>).</summary>
        private static int DeityBoxIndex(DeityType deity)
        {
            List<DeityType> deities = deityBoxFoundation != null ? deityBoxFoundation.DeitiesListForReading : null;
            if (deities == null || deity == null || deities.Count != deityBoxes.Count)
            {
                return -1;
            }
            for (int i = 0; i < deities.Count; i++)
            {
                if (ReferenceEquals(deities[i], deity))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Rings the focused deity's box once the whole deity section has been drawn.</summary>
        internal static void EndDeityBoxes(IdeoFoundation_Deity foundation)
        {
            try
            {
                Func<DeityType> provider = DeityRingProvider;
                DeityType focused = provider != null ? provider() : null;
                if (focused == null || foundation == null)
                {
                    return;
                }
                List<DeityType> deities = foundation.DeitiesListForReading;
                // One highlight per deity box is what makes the Nth rect deity N; a mismatch means
                // a game or mod change broke that, so this pass rings nothing.
                if (deities == null || deities.Count != deityBoxes.Count)
                {
                    return;
                }
                int index = deities.IndexOf(focused);
                if (index < 0)
                {
                    return;
                }
                Rect box = deityBoxes[index];
                FocusRing.Draw(box);
                UiPointerFollow.NotifyFocusedRect(GuiSpace.VisibleScreenRect(box));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Deity box capture error", ex);
            }
        }

        // ------------------------------------------------------------------
        // Scroll-follow: keep the focused box inside the details pane.
        // ------------------------------------------------------------------

        /// <summary>The focused element's raw view-space rect, published by whichever consumer drives the details pane, or null when the focused row has no box on the page.</summary>
        internal static Func<Rect?> FollowTarget;

        /// <summary>Armed when a consumer's cursor SETTLES, disarmed by <see cref="ApplyFollow"/>. At most one corrective write per settle, so a player scrolling the pane by hand is never fought.</summary>
        internal static bool FollowRequested;

        private static int followRequestFrame = -1;

        /// <summary>A target the pane has not drawn yet is worth waiting a few frames for; forever is not.</summary>
        private const int FollowRequestFrameBudget = 5;

        internal static void RequestFollow()
        {
            FollowRequested = true;
            followRequestFrame = Time.frameCount;
        }

        /// <summary>
        /// Scrolls the details pane just far enough to bring the focused box into view by writing
        /// <c>DoIdeoDetails</c>' <c>ref</c> scroll position, which lands back in whichever field the
        /// caller passed, so every host (modded ones included) needs no per-host FieldRef. The clamp
        /// needs no coordinate conversion: that scroll view starts at <c>curY = 0f</c> (decompiled
        /// IdeoUIUtility.cs:527-530), so the recorders' rects are already content-relative. The write
        /// takes effect next frame, this frame's pane having already drawn.
        /// </summary>
        internal static void ApplyFollow(Rect inRect, ref Vector2 scrollPosition)
        {
            try
            {
                Func<Rect?> provider = FollowTarget;
                Rect? target = provider != null ? provider() : null;
                if (target == null)
                {
                    // Stay armed: the boxes this request is about may only be recorded by the pass
                    // now finishing, so the next one can resolve what this one could not.
                    if (Time.frameCount - followRequestFrame > FollowRequestFrameBudget)
                    {
                        FollowRequested = false;
                    }
                    return;
                }
                Rect raw = target.Value;
                FollowRequested = false;
                scrollPosition.y = Mathf.Max(0f, Mathf.Clamp(scrollPosition.y, raw.yMax - inRect.height, raw.yMin));
            }
            catch (Exception ex)
            {
                FollowRequested = false;
                ModLogger.LimitedError("Ideo details scroll follow error", ex);
            }
        }

        /// <summary>The unconverted counterpart of <see cref="RowDrawCapture"/>: the rects vanilla passed, kept for the pass in flight under the same frame/event boundary.</summary>
        private sealed class RawBoxCapture
        {
            private readonly List<KeyValuePair<object, Rect>> entries = new List<KeyValuePair<object, Rect>>();
            private int passFrame = -1;
            private EventType passEvent = EventType.Ignore;

            internal void Record(object identity, Rect guiRect)
            {
                Event ev = Event.current;
                EventType type = ev == null ? EventType.Ignore : ev.type;
                if (Time.frameCount != passFrame || type != passEvent)
                {
                    entries.Clear();
                    passFrame = Time.frameCount;
                    passEvent = type;
                }
                entries.Add(new KeyValuePair<object, Rect>(identity, guiRect));
            }

            internal Rect? FindFirst(object identity)
            {
                if (identity == null)
                {
                    return null;
                }
                for (int i = 0; i < entries.Count; i++)
                {
                    if (Equals(entries[i].Key, identity))
                    {
                        return entries[i].Value;
                    }
                }
                return null;
            }
        }
    }

    /// <summary>Records (and rings) every precept box the host page draws.</summary>
    [HarmonyPatch(typeof(Precept), "DrawPreceptBox")]
    internal static class PreceptBoxDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Precept __instance, Rect preceptBox)
        {
            IdeoBoxDrawPatch.RecordPreceptBox(__instance, preceptBox);
        }
    }

    /// <summary>Records (and rings) every meme box, wherever vanilla draws one.</summary>
    [HarmonyPatch(typeof(IdeoUIUtility), "DoMeme")]
    internal static class MemeBoxDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect memeBox, MemeDef meme)
        {
            IdeoBoxDrawPatch.RecordMemeBox(meme, memeBox);
        }
    }

    /// <summary>The inline name-icon bracket: <c>DoName</c> draws its icons at the entry <c>curY</c>, which only a prefix can read, while the ring belongs over icons a postfix has let vanilla paint.</summary>
    [HarmonyPatch(typeof(IdeoUIUtility), "DoName")]
    internal static class IdeoNameIconsDrawPatch
    {
        private static float entryY;
        private static bool armed;

        [HarmonyPrefix]
        public static void Prefix(ref float curY)
        {
            armed = IdeoBoxDrawPatch.HasInterest;
            entryY = curY;
        }

        [HarmonyPostfix]
        public static void Postfix(float width, Ideo ideo)
        {
            if (!armed)
            {
                return;
            }
            IdeoBoxDrawPatch.RecordNameIcons(ideo, entryY, width);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            armed = false;
        }
    }

    /// <summary>The style tile bracket: only a prefix can read <c>DoStyles</c>' <c>ref</c> cursor where the first tile starts, and only a postfix runs after the tiles are painted.</summary>
    [HarmonyPatch(typeof(IdeoUIUtility), "DoStyles")]
    internal static class IdeoStylesDrawPatch
    {
        private static float entryX;
        private static float entryY;
        private static bool armed;

        [HarmonyPrefix]
        public static void Prefix(ref float curY, ref float curX)
        {
            armed = IdeoBoxDrawPatch.HasInterest;
            entryX = curX;
            entryY = curY;
        }

        [HarmonyPostfix]
        public static void Postfix(Ideo ideo, int styleIconSize)
        {
            if (!armed)
            {
                return;
            }
            IdeoBoxDrawPatch.RecordStyleTiles(ideo, entryX, entryY, styleIconSize);
        }

        /// <summary>Disarms even when modded style content throws mid-draw (see <see cref="DeityInfoDrawPatch"/>).</summary>
        [HarmonyFinalizer]
        public static void Finalizer()
        {
            armed = false;
        }
    }

    /// <summary>Records the two appearance boxes from the <c>curY</c> their method leaves behind.</summary>
    [HarmonyPatch(typeof(IdeoUIUtility), "DoAppearanceItems")]
    internal static class IdeoAppearanceItemsDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float curY)
        {
            IdeoBoxDrawPatch.RecordAppearanceBoxes(curY);
        }
    }

    /// <summary>The faction icon row bracket: see <see cref="IdeoBoxDrawPatch.BeginFactionIcons"/>.</summary>
    [HarmonyPatch(typeof(IdeoUIUtility), "DoFactionIcons")]
    internal static class IdeoFactionIconsDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string label)
        {
            IdeoBoxDrawPatch.BeginFactionIcons(label);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            IdeoBoxDrawPatch.EndFactionIcons();
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            IdeoBoxDrawPatch.RecordingFactionIcons = false;
        }
    }

    /// <summary>Contributes one icon per faction while the bracket above is armed. It runs on every faction icon drawn game-wide, so the bracket's flag is the first statement and all an off-path icon costs.</summary>
    [HarmonyPatch(typeof(FactionUIUtility), "DrawFactionIconWithTooltip")]
    internal static class FactionIconRowTapPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect r)
        {
            if (!IdeoBoxDrawPatch.RecordingFactionIcons)
            {
                return;
            }
            IdeoBoxDrawPatch.RecordFactionIcon(r);
        }
    }

    /// <summary>Records the name/symbol block from the <c>curY</c> its method leaves behind.</summary>
    [HarmonyPatch(typeof(IdeoUIUtility), "DoNameAndSymbol")]
    internal static class IdeoNameAndSymbolDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float curY, float width, Ideo ideo, IdeoEditMode editMode)
        {
            IdeoBoxDrawPatch.RecordNameSymbolBlock(curY, width, ideo, editMode);
        }
    }

    /// <summary>The scroll-follow seam: <c>DoIdeoDetails</c> takes its scroll position by reference, so writing it here writes the caller's own field.</summary>
    [HarmonyPatch(typeof(IdeoUIUtility), "DoIdeoDetails")]
    internal static class IdeoDetailsScrollFollowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect inRect, ref Vector2 scrollPosition)
        {
            if (!IdeoBoxDrawPatch.FollowRequested || IdeoBoxDrawPatch.FollowTarget == null)
            {
                return;
            }
            // The reform dialog draws over a live DoIdeoDetails host that keeps drawing beneath it,
            // so without this gate the postfix would spend the reform's request on the tab behind.
            if (FocusStack.Top is IdeoReformScreenScope)
            {
                return;
            }
            IdeoBoxDrawPatch.ApplyFollow(inRect, ref scrollPosition);
        }
    }

    /// <summary>
    /// The same follow engine reached through a <see cref="AccessTools.FieldRefAccess{T, F}"/> seam:
    /// <c>Dialog_ReformIdeo</c> never calls <c>DoIdeoDetails</c>, scrolling its own private
    /// <c>scrollPosition</c> field instead, so <see cref="IdeoDetailsScrollFollowPatch"/> never fires
    /// for this window and no double-write is possible.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ReformIdeo), "DoWindowContents")]
    internal static class ReformIdeoScrollFollowPatch
    {
        private static readonly AccessTools.FieldRef<Dialog_ReformIdeo, Vector2> ScrollRef =
            AccessTools.FieldRefAccess<Dialog_ReformIdeo, Vector2>("scrollPosition");

        [HarmonyPostfix]
        public static void Postfix(Dialog_ReformIdeo __instance, Rect inRect)
        {
            if (Event.current.type == EventType.Layout)
            {
                return;
            }
            if (!IdeoBoxDrawPatch.FollowRequested || IdeoBoxDrawPatch.FollowTarget == null)
            {
                return;
            }
            // Only THIS dialog's own settle may spend the request; a stale one armed by another host
            // must never steer it.
            if (!(FocusStack.Top is IdeoReformScreenScope))
            {
                return;
            }
            // mirrors Dialog_ReformIdeo.DoWindowContents (decompiled :130-131): 145 = 55 + 40 + 50.
            float outRectHeight = inRect.height - 145f;
            ref Vector2 scroll = ref ScrollRef(__instance);
            IdeoBoxDrawPatch.ApplyFollow(new Rect(0f, 0f, inRect.width, outRectHeight), ref scroll);
        }
    }

    /// <summary>The deity-box bracket: see <see cref="IdeoBoxDrawPatch"/>'s remarks.</summary>
    [HarmonyPatch(typeof(IdeoFoundation_Deity), "DoInfo")]
    internal static class DeityInfoDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(IdeoFoundation_Deity __instance)
        {
            IdeoBoxDrawPatch.BeginDeityBoxes(__instance);
        }

        [HarmonyPostfix]
        public static void Postfix(IdeoFoundation_Deity __instance)
        {
            IdeoBoxDrawPatch.EndDeityBoxes(__instance);
        }

        /// <summary>Disarming is a finalizer, not part of the postfix: modded ideo content does throw mid-draw, and a skipped postfix would leave the flag armed game-wide. The exception still propagates.</summary>
        [HarmonyFinalizer]
        public static void Finalizer()
        {
            IdeoBoxDrawPatch.RecordingDeityBoxes = false;
        }
    }

    /// <summary>Contributes one rect per deity box while the bracket above is armed. It runs on every light highlight drawn game-wide, so the bracket's static bool is the first gate; the interest check behind it catches a flag that outlived its bracket.</summary>
    [HarmonyPatch(typeof(Widgets), "DrawLightHighlight")]
    internal static class DeityBoxHighlightPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect)
        {
            if (!IdeoBoxDrawPatch.RecordingDeityBoxes)
            {
                return;
            }
            if (!IdeoBoxDrawPatch.HasInterest)
            {
                return;
            }
            IdeoBoxDrawPatch.RecordDeityBox(rect);
        }
    }
}
