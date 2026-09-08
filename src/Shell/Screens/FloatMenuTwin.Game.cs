using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The visual twin of WindowlessFloatMenuState, drawn as a vanilla ImmediateWindow because
    /// that type alone cannot steal Escape/Enter (decompiled Verse/WindowStack.cs:260-282),
    /// cannot attract a ScopeForWindow attachment, is exempted by every ForeignWindowAbove
    /// guard so a modal scope beneath keeps its modality, and never reaches
    /// DialogInterceptionPatch's FloatMenu branches. Rows are painted by the option's own
    /// FloatMenuOption.DoGUI, so they are pixel-identical to vanilla; the twin composes no text.
    /// A left-click activates through WindowlessFloatMenuState.ActivateIndex, the same path
    /// Enter runs. The twin draws the SAME option-list instance the state navigates, so the
    /// list-swap detection below is ReferenceEquals on purpose.
    /// </summary>
    internal static class FloatMenuTwin
    {
        private const int TwinWindowID = 74112601;
        private static List<FloatMenuOption> sizedOptions; // reference-identity marker
        private static FloatMenuTwinLayout layout;
        private static Vector2 scrollPosition;
        private static int lastFocusedIndex = -1;
        private static Rect titleRect;
        private static Rect windowRect;

        /// <summary>
        /// Each row's geometry from the last draw pass, index-aligned with the option list.
        /// Recorded at draw time: only there are the scroll offset, the multi-column wrap and
        /// the window's own group all applied.
        /// </summary>
        private static readonly List<PointerHitCandidate> rowHits = new List<PointerHitCandidate>();

        internal static IReadOnlyList<PointerHitCandidate> RowHits => rowHits;

        /// <summary>The twin's own window rect — the positive ownership test for pointer routing, since an ImmediateWindow is invisible to the guards that ask what covers the pointer.</summary>
        internal static Rect WindowRect => windowRect;

        /// <summary>
        /// The twin's live ImmediateWindow, or null when it is not on the stack — the surface
        /// <see cref="FloatMenuOverlayScope"/> hands <see cref="PointerRouting.PointerOwnedBy"/>.
        /// WindowStack negates the id it is opened with (decompiled Verse/WindowStack.cs:380).
        /// </summary>
        internal static Window LiveWindow
        {
            get
            {
                IList<Window> windows = Find.WindowStack != null ? Find.WindowStack.Windows : null;
                if (windows == null)
                {
                    return null;
                }
                for (int i = 0; i < windows.Count; i++)
                {
                    if (windows[i].ID == -TwinWindowID)
                    {
                        return windows[i];
                    }
                }
                return null;
            }
        }

        // Cached: a fresh lambda every frame would thrash the immediate window's identity check.
        private static readonly Action DrawContentsAction = DrawContents;
        private static readonly Action DrawTitleAction = DrawTitle;

        internal static void Request()
        {
            try
            {
                // UIRootOnGUI runs during early startup, before the stack exists.
                if (Find.WindowStack == null) return;

                if (!WindowlessFloatMenuState.IsActive)
                {
                    sizedOptions = null;
                    scrollPosition = Vector2.zero;
                    lastFocusedIndex = -1;
                    rowHits.Clear();
                    windowRect = default(Rect);
                    return;
                }

                List<FloatMenuOption> options = WindowlessFloatMenuState.CurrentOptions;
                if (options == null || options.Count == 0) return;

                if (!ReferenceEquals(options, sizedOptions))
                {
                    // New (or nested) menu. Compute the layout once for its SizeMode, apply that
                    // to every option, then recompute so the cached RequiredWidth/RequiredHeight
                    // are the sized ones (vanilla's ctor order, decompiled Verse/FloatMenu.cs:195-198).
                    FloatMenuTwinLayout provisional = FloatMenuTwinLayout.Compute(options);
                    for (int i = 0; i < options.Count; i++)
                    {
                        options[i].SetSizeMode(provisional.SizeMode);
                    }
                    sizedOptions = options;
                    scrollPosition = Vector2.zero;
                    lastFocusedIndex = -1;
                }

                // Recomputed every frame: screen size and option labels can change, and this is
                // O(n) over a list vanilla also walks several times per frame.
                layout = FloatMenuTwinLayout.Compute(options);

                Rect rect = ResolveRect(layout);
                windowRect = rect;
                // FloatMenu's own chrome settings (decompiled Verse/FloatMenu.cs:199-203).
                Find.WindowStack.ImmediateWindow(TwinWindowID, rect, WindowLayer.Super, DrawContentsAction,
                    doBackground: false, absorbInputAroundWindow: false, shadowAlpha: 0f);

                string title = WindowlessFloatMenuState.DisplayTitle;
                if (!string.IsNullOrEmpty(title))
                {
                    // A second ImmediateWindow, as FloatMenu.ExtraOnGUI draws its own title strip
                    // (decompiled Verse/FloatMenu.cs:228-251): it draws ABOVE the twin's rect and
                    // would otherwise be clipped by the twin's GUI.Window group. Our id, never
                    // vanilla's 6830963.
                    titleRect = ResolveTitleRect(rect, title);
                    Find.WindowStack.ImmediateWindow(TwinWindowID - 1, titleRect, WindowLayer.Super, DrawTitleAction,
                        doBackground: false, absorbInputAroundWindow: false, shadowAlpha: 0f);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Float-menu twin draw error", ex);
            }
        }

        private static Rect ResolveRect(FloatMenuTwinLayout l)
        {
            Vector2 size = new Vector2(l.TotalWidth, l.TotalWindowHeight);
            Vector2 pos;
            if (MapAnchorAvailable())
            {
                // FloatMenu.InitialPositionShift, decompiled Verse/FloatMenu.cs:36.
                pos = GenMapUI.LabelDrawPosFor(MapNavigationState.CurrentCursorPosition) + new Vector2(4f, 0f);
            }
            else
            {
                pos = new Vector2(((float)UI.screenWidth - size.x) / 2f, (float)UI.screenHeight * 0.25f);
            }

            // Vanilla's own clamp, decompiled Verse/FloatMenu.cs:217-224.
            if (pos.x + size.x > UI.screenWidth) pos.x = UI.screenWidth - size.x;
            if (pos.y + size.y > UI.screenHeight) pos.y = UI.screenHeight - size.y;
            // Ours, not vanilla's: vanilla starts from a mouse position already inside the
            // screen and cannot go negative, but the map-anchored cell can sit near an edge.
            if (pos.x < 0f) pos.x = 0f;
            if (pos.y < 0f) pos.y = 0f;
            return new Rect(pos.x, pos.y, size.x, size.y);
        }

        private static bool MapAnchorAvailable()
        {
            if (!WorldRendererUtility.DrawingMap) return false;
            if (Find.CurrentMap == null) return false;
            if (!MapNavigationState.IsInitialized) return false;
            if (!MapNavigationState.CurrentCursorPosition.InBounds(Find.CurrentMap)) return false;

            // A real dialog anywhere on the stack means the menu belongs to that dialog's
            // world, not the map's.
            IList<Window> windows = Find.WindowStack.Windows;
            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                if (window is ImmediateWindow || window is MainTabWindow) continue;
                return false;
            }
            return true;
        }

        /// <summary>FloatMenu.TitleOffset and its width formula, decompiled Verse/FloatMenu.cs:28, :235-236.</summary>
        private static Rect ResolveTitleRect(Rect twinRect, string title)
        {
            Text.Font = GameFont.Small;
            float width = Mathf.Max(150f, 15f + Text.CalcSize(title).x);
            float x = twinRect.x + 30f;
            float y = twinRect.y + -25f;
            // Ours: a menu anchored near the top would push the strip off-screen.
            if (y < 0f) y = 0f;
            return new Rect(x, y, width, 23f);
        }

        /// <summary>FloatMenu.ExtraOnGUI's title-strip drawer, decompiled Verse/FloatMenu.cs:237-249, verbatim.</summary>
        private static void DrawTitle()
        {
            string title = WindowlessFloatMenuState.DisplayTitle;
            if (string.IsNullOrEmpty(title)) return;

            GUI.color = Color.white; // Never baseColor: no vanish-if-distant fade.
            Text.Font = GameFont.Small;
            Rect position = titleRect.AtZero();
            position.width = 150f;
            GUI.DrawTexture(position, TexUI.TextBGBlack);
            Rect labelRect = titleRect.AtZero();
            labelRect.x += 15f;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, title);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawContents()
        {
            rowHits.Clear();

            List<FloatMenuOption> options = WindowlessFloatMenuState.CurrentOptions;
            if (options == null || options.Count == 0) return;

            int focused = WindowlessFloatMenuState.SelectedIndex;

            // ImmediateWindow's Margin is 0f, matching FloatMenu.Margin, so window-local (0,0)
            // is the first row's top-left (decompiled Verse/ImmediateWindow.cs:29-32).
            Rect rect = new Rect(0f, 0f, layout.TotalWidth, layout.TotalWindowHeight);

            // Pre-pass: the focused row's rect without drawing, so auto-scroll can clamp
            // scrollPosition before BeginScrollView consumes it this frame.
            Rect focusedRect = LayoutRowRect(options, focused);
            if (layout.UsingScrollbar && focused != lastFocusedIndex)
            {
                ClampScrollTo(focusedRect, rect.width - 10f);
                lastFocusedIndex = focused;
            }

            GUI.color = Color.white; // Never baseColor: no vanish-if-distant fade.
            Text.Font = GameFont.Small;
            Vector2 zero = Vector2.zero;
            float maxViewHeight = layout.MaxViewHeight;
            float columnWidth = layout.ColumnWidth;
            if (layout.UsingScrollbar)
            {
                rect.width -= 10f;
                Widgets.BeginScrollView(rect, ref scrollPosition, new Rect(0f, 0f, layout.TotalWidth - 16f, layout.TotalViewHeight));
            }
            int pendingActivation = -1;
            for (int i = 0; i < options.Count; i++)
            {
                float requiredHeight = options[i].RequiredHeight;
                if (zero.y + requiredHeight + -1f > maxViewHeight)
                {
                    zero.y = 0f;
                    zero.x += columnWidth + -1f;
                }
                Rect rowRect = new Rect(zero.x, zero.y, columnWidth, requiredHeight);
                zero.y += requiredHeight + -1f;
                rowHits.Add(new PointerHitCandidate
                {
                    Primary = GuiSpace.VisibleScreenRect(rowRect),
                    ClipDepth = GuiSpace.CurrentClip().Depth,
                });

                // FloatMenuOption.DoGUI ends in Widgets.ButtonInvisible, which fires on mouse-up
                // (decompiled Verse/FloatMenuOption.cs:435-447). Consuming the mouse-down here
                // means DoGUI can never return true and the option's action can never run behind
                // the state's back; activation goes through ActivateIndex, the path Enter uses.
                // Every extraPartOnGUI rect sits inside the row rect and is drawn before
                // ButtonInvisible, so the same consume makes the extra-part button mouse-inert
                // by design — Alt+I remains its keyboard equivalent.
                bool hit = false;
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                    && rowRect.Contains(Event.current.mousePosition))
                {
                    hit = true;
                    Event.current.Use();
                }
                // Return value discarded: activation never rides DoGUI's own bool.
                options[i].DoGUI(rowRect, WindowlessFloatMenuState.GivesColonistOrders, null);
                if (hit) pendingActivation = i;
                if (i == focused) FocusRing.Draw(rowRect);
            }
            if (layout.UsingScrollbar) Widgets.EndScrollView();
            GUI.color = Color.white;

            if (pendingActivation >= 0)
            {
                WindowlessFloatMenuState.ActivateIndex(pendingActivation);
            }
        }

        /// <summary>Same row-placement walk as DrawContents, with no drawing, so the two cannot drift.</summary>
        private static Rect LayoutRowRect(List<FloatMenuOption> options, int index)
        {
            Vector2 zero = Vector2.zero;
            float maxViewHeight = layout.MaxViewHeight;
            float columnWidth = layout.ColumnWidth;
            Rect result = new Rect(0f, 0f, columnWidth, 0f);
            for (int i = 0; i < options.Count; i++)
            {
                float requiredHeight = options[i].RequiredHeight;
                if (zero.y + requiredHeight + -1f > maxViewHeight)
                {
                    zero.y = 0f;
                    zero.x += columnWidth + -1f;
                }
                Rect rowRect = new Rect(zero.x, zero.y, columnWidth, requiredHeight);
                zero.y += requiredHeight + -1f;
                if (i == index) result = rowRect;
            }
            return result;
        }

        private static void ClampScrollTo(Rect r, float viewWidth)
        {
            if (r.xMax > scrollPosition.x + viewWidth) scrollPosition.x = r.xMax - viewWidth;
            if (r.xMin < scrollPosition.x) scrollPosition.x = r.xMin;
            scrollPosition.x = Mathf.Clamp(scrollPosition.x, 0f, Mathf.Max(0f, layout.TotalWidth - 16f - viewWidth));
            // Vertical scroll is never needed: vanilla caps each column at 90% of screen height
            // and wraps into a new column (decompiled Verse/FloatMenu.cs:276-280).
            scrollPosition.y = Mathf.Max(0f, scrollPosition.y);
        }
    }

    /// <summary>
    /// Re-requests the twin's ImmediateWindow every frame while WindowlessFloatMenuState is
    /// active. Both UIRoot subclasses call base.UIRootOnGUI() first, so this postfix runs before
    /// WindowStackOnGUI() and the twin draws in the same frame. ImmediateWindow self-gates on
    /// Repaint and expires unrequested windows on the same gate (decompiled
    /// Verse/WindowStack.cs:371-374, :507-512), so the request needs no event-type check and the
    /// window disappears one Repaint after the state closes.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
    internal static class FloatMenuTwinRequestPatch
    {
        [HarmonyPostfix]
        internal static void Postfix()
        {
            try
            {
                FloatMenuTwin.Request();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Float-menu twin request patch error", ex);
            }
        }
    }
}
