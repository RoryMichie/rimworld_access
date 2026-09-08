using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The engine's automatic tooltip channel: while armed, records every tooltip registration
    /// reaching <see cref="TooltipHandler"/> during the real draw pass, so a scope can append the
    /// focused element's tooltip without a mouse anywhere near it.
    ///
    /// Reach: <c>TipRegion(Rect, TipSignal)</c> and the four <c>TipRegionByKey</c> overloads do their
    /// Mouse.IsOver check inside the method body, so the prefixes always see them. A call site that
    /// wraps its OWN <c>if (Mouse.IsOver(rect))</c> around the call is invisible to any prefix; the
    /// harvest channel recovers most of those, with <see cref="TooltipGateAnalysis"/> certifying that
    /// a guarded branch exists only to register a tooltip and a transpiler widening that site's
    /// Mouse.IsOver to also answer <see cref="HarvestWantsHover"/>. Sites the analyzer refuses stay
    /// invisible, and surfaces built on them must fill <see cref="ElementDescription.Extras"/> from
    /// their own source data; both channels feed that same slot, explicit data winning.
    ///
    /// A scope arms on push and disarms on pop, and the owning surface's draw is bracketed by
    /// <see cref="BeginPass"/>/<see cref="EndPass"/> so only registrations inside that draw are
    /// recorded. The bracket earns its keep twice: other UI drawn the same frame registers tips in
    /// SCREEN space whose numbers can collide with group-local rects, and scoping to the surface's
    /// pass pins every recorded rect to the same GUI-group space the surface's element rects use.
    /// Text resolves lazily at query time, since registrations arrive every repaint and vanilla
    /// textGetter delegates can be expensive.
    ///
    /// Every registration carries a screen-space rect and its GUIClip context alongside the local
    /// rect. Local-only matching steals tooltips across coordinate spaces, and a screen rect alone is
    /// not enough either: a row scrolled below a ScrollView's fold unclips to an off-screen rect that
    /// can collide numerically with an unrelated widget. <see cref="TryResolveAtScreen"/> therefore
    /// requires clip-context equality as well as rect overlap.
    /// </summary>
    public static class TooltipCapture
    {
        // NEST-SAFE BRACKET: Arm/Disarm are called independently by seven scope types and real
        // sequences overlap two of them (opening a modded main tab from the F12 extras menu arms the
        // new window's scope BEFORE the float menu pops and disarms). A bare bool would let that
        // Disarm kill the channel for the rest of the new window's session. Only the OUTERMOST Arm
        // clears the index, and armed stays true until every armer has disarmed.
        private static bool armed;
        private static int armDepth;
        private static bool passOpen;
        private static readonly TipIndex index = new TipIndex();

        // Detached pass: registrations made while a detached WidgetCapture pass draws a tab offscreen
        // go to their own index, leaving an armed scope's records untouched. Two independent callers
        // open it — the inspect-tab capture harness, and ScreenScopeDrawPatch around an extras-region
        // window's draw (folded extras rows resolve out of THIS index). A Begin without its End must
        // never leave the channel open, since RecordSignal routes every registration here while it
        // is, so the bracket counts depth (the outermost caller owns the index) and self-heals on
        // frame INEQUALITY — never a same-frame heuristic, since a pass spans one frame's draw.
        private static bool detachedPass;
        private static int detachedDepth;
        private static int detachedFrame;
        private static readonly TipIndex detachedIndex = new TipIndex();

        // Harvest pass: the caller-gated tips a certified Mouse.IsOver site only builds under a
        // pointer. One bounded LAYOUT pass per navigation forces every certified site to register, so
        // the cost is one frame's string building per cursor move; Layout paints nothing, so a forced
        // branch that also draws is visually inert. Same nest-safe, self-healing bracket as above.
        private static bool harvestRequested;
        private static bool harvestPass;
        private static int harvestDepth;
        private static int harvestFrame;
        private static readonly TipIndex harvestIndex = new TipIndex();

        public static void Arm()
        {
            HealLeakedDetachedPass();
            HealLeakedHarvestPass();
            if (armDepth == 0)
            {
                // Only the outermost Arm clears; a nested Arm leaves the other scope's live
                // registrations alone, and that scope's own BeginPass re-clears next frame anyway.
                index.Clear();
            }
            armDepth++;
            armed = true;
            passOpen = false;
        }

        public static void Disarm()
        {
            if (armDepth > 0)
            {
                armDepth--;
            }
            // armed reflects whether ANY armer still holds the channel. Deliberately does not clear
            // the index: a still-armed outer scope's registrations must survive this Disarm.
            armed = armDepth > 0;
            passOpen = false;
        }

        /// <summary>
        /// Start of the armed surface's draw: drops the previous pass's registrations and starts
        /// recording. Call from the surface draw method's prefix.
        /// </summary>
        public static void BeginPass()
        {
            // An armed surface draws every frame, so a leaked detached bracket — which would route
            // the registrations below away from this index — is noticed soonest here.
            HealLeakedDetachedPass();
            HealLeakedHarvestPass();
            if (armed)
            {
                index.Clear();
                passOpen = true;
            }
        }

        /// <summary>
        /// End of the armed surface's draw: stops recording but keeps this pass's registrations, so
        /// the scope can query them from its window postfix.
        /// </summary>
        public static void EndPass()
        {
            passOpen = false;
        }

        /// <summary>
        /// The tooltip for an element occupying <paramref name="elementRect"/>, or null when none was
        /// registered this pass. Rects are compared in the GUI-group space the surface registered
        /// them in, so querying with a rect from the same widget code agrees by construction.
        /// Deliberately does NOT consult the harvest index: this query's consumers never request a
        /// harvest, so a hit there could only be another surface's tips matched across coordinate
        /// spaces. The clip-aware queries keep their harvest fallback — clip-context equality makes
        /// them theft-proof, and their callers are the harvest's real consumers.
        /// </summary>
        public static string TryResolveAt(Rect elementRect)
        {
            if (!armed)
            {
                return null;
            }
            return index.QueryAt(ToTipRect(elementRect));
        }

        /// <summary>
        /// Like <see cref="TryResolveAt"/>, but matches SCREEN rects and requires GUIClip context
        /// equality. A caller whose widgets span unrelated GUI-group spaces (a dialog title at
        /// window-space (0,0) and a checkbox whose ScrollView origin also resets to (0,0)) queries in
        /// one shared space, and the context requirement stops a row scrolled below the fold from
        /// matching a registration made under a different clip.
        /// </summary>
        public static string TryResolveAtScreen(Rect elementScreenRect, GuiSpace.ClipKey clip)
        {
            if (!armed)
            {
                return null;
            }
            TipRect query = ToTipRect(elementScreenRect);
            TipClip context = ToTipClip(clip);
            return index.QueryAtScreen(query, context) ?? harvestIndex.QueryAtScreen(query, context);
        }

        /// <summary>
        /// Starts recording into the detached index, cleared here on the outermost bracket. Wrap a
        /// detached WidgetCapture pass, or the window pass whose captured rows fold out of this
        /// index; independent of Arm/BeginPass so an armed scope's records survive the capture.
        /// </summary>
        public static void BeginDetachedPass()
        {
            HealLeakedDetachedPass();
            if (detachedDepth == 0)
            {
                detachedIndex.Clear();
            }
            detachedDepth++;
            detachedFrame = Time.frameCount;
            detachedPass = true;
        }

        public static void EndDetachedPass()
        {
            if (detachedDepth > 0)
            {
                detachedDepth--;
            }
            detachedPass = detachedDepth > 0;
        }

        /// <summary>
        /// Closes a detached bracket whose Begin ran but whose End never did (an exception escaping
        /// the draw skips the postfix). Frame INEQUALITY is the only valid signal — a real bracket
        /// opens and closes inside one frame's draw.
        /// </summary>
        private static void HealLeakedDetachedPass()
        {
            if (detachedDepth > 0 && Time.frameCount != detachedFrame)
            {
                detachedDepth = 0;
                detachedPass = false;
            }
        }

        /// <summary>
        /// Requests one gated Layout harvest after a content rebuild or cursor move. Bounded to a
        /// single pass, so a certified branch's string building costs one frame per navigation.
        /// </summary>
        public static void RequestHarvest()
        {
            harvestRequested = true;
        }

        /// <summary>
        /// Opened by ScreenScopeDrawPatch on the LAYOUT pass only; returns whether the caller owes an
        /// <see cref="EndHarvestPass"/>. Clears the harvest index on the outermost bracket only, not
        /// every Layout pass — results must survive until the next harvest for the Repaint-pass
        /// resolution to read them.
        /// </summary>
        public static bool BeginHarvestPass()
        {
            HealLeakedHarvestPass();
            if (!harvestRequested && harvestDepth == 0)
            {
                return false;
            }
            if (harvestDepth == 0)
            {
                harvestRequested = false;
                harvestIndex.Clear();
            }
            harvestDepth++;
            harvestFrame = Time.frameCount;
            harvestPass = true;
            return true;
        }

        public static void EndHarvestPass()
        {
            if (harvestDepth > 0)
            {
                harvestDepth--;
            }
            harvestPass = harvestDepth > 0;
        }

        /// <summary>
        /// The gate's only question (see <see cref="TooltipHoverGate"/>). True while a harvest pass is
        /// open on a Layout event, and on Repaint only for a detached pass whose clip is genuinely
        /// offscreen.
        /// </summary>
        public static bool HarvestWantsHover()
        {
            EventType type = Event.current == null ? EventType.Ignore : Event.current.type;
            if (type == EventType.Repaint)
            {
                // A detached bracket is not proof of an offscreen draw — ScreenScopeDrawPatch opens
                // one around a real window — and forcing a tooltip branch during a visible repaint
                // would paint its hover highlight.
                return detachedPass && GuiSpace.ClipIsOffscreen();
            }
            return (detachedPass || harvestPass) && type == EventType.Layout;
        }

        private static void HealLeakedHarvestPass()
        {
            if (harvestDepth > 0 && Time.frameCount != harvestFrame)
            {
                harvestDepth = 0;
                harvestPass = false;
            }
        }

        /// <summary>
        /// Whether the detached channel is recording — the gate on naming an icon-only button after
        /// the tooltip over it, which would otherwise double up with a scope presenting the armed
        /// channel's tips itself.
        /// </summary>
        internal static bool DetachedPassOpen
        {
            get { return detachedPass; }
        }

        /// <summary>
        /// Detached twin of <see cref="TryResolveAtScreen"/>, valid until the next
        /// BeginDetachedPass — callers fold rows in the same frame the capture pass closes.
        /// Deliberately the ONLY detached query: a detached pass spans a whole window or tab draw and
        /// so always crosses group spaces, where group-local coordinates restart near (0,0) and a
        /// local-rect query fuses every nested group's tips into one wall of text per row.
        /// </summary>
        internal static string TryResolveDetachedAtScreen(Rect elementScreenRect, GuiSpace.ClipKey clip)
        {
            TipRect query = ToTipRect(elementScreenRect);
            TipClip context = ToTipClip(clip);
            return detachedIndex.QueryAtScreen(query, context) ?? harvestIndex.QueryAtScreen(query, context);
        }

        /// <summary>
        /// Detached routing takes precedence: an offscreen capture pass and a
        /// harvest pass cannot legitimately overlap, and if one ever did the
        /// capture's caller is the one owed its records.
        /// </summary>
        private static TipIndex TargetIndex()
        {
            if (detachedPass)
            {
                return detachedIndex;
            }
            return harvestPass ? harvestIndex : index;
        }

        private static TipRect ToTipRect(Rect rect)
        {
            return new TipRect(rect.x, rect.y, rect.width, rect.height);
        }

        private static TipClip ToTipClip(GuiSpace.ClipKey clip)
        {
            return new TipClip(ToTipRect(clip.VisibleRect), clip.Depth);
        }

        internal static void RecordSignal(Rect rect, TipSignal tip)
        {
            // No armed surface draws during a detached pass (the capture harness runs after every
            // real window's pass closes), so detached routing and the armed check are exclusive.
            if (!detachedPass && !harvestPass && (!armed || !passOpen))
            {
                return;
            }
            // Nothing inside the filter panel's suppression bracket records, tooltips included; the
            // synthetic FilterPanelHandoff row speaks for it. An off-screen measurement pass is the
            // same case: nothing under that clip is visible and its widgets are not recording, so its
            // tips must not enter the pool a real row could match. The harness's own detached pass is
            // exempt — its group is off-screen by construction.
            if (WidgetCapture.FilterPanelSuppressed || (!WidgetCapture.DetachedPassOpen && WidgetCapture.ClipUnseen()))
            {
                return;
            }
            if (tip.textGetter == null && string.IsNullOrEmpty(tip.text))
            {
                return;
            }
            // Copy into locals so the closure captures values, not a struct that later mutates.
            Func<string> getter = tip.textGetter;
            string text = tip.text;
            Func<string> resolve = getter != null ? getter : () => text;
            // Records happen inside OnGUI by construction, so the GUIClip stack is live here and both
            // the screen rect and the context it was unclipped from resolve correctly.
            GuiSpace.ClipKey liveClip = GuiSpace.CurrentClip();
            TipRect screenRect = ToTipRect(GuiSpace.ToScreen(rect));
            TipClip clip = ToTipClip(liveClip);
            TargetIndex().Add(
                ToTipRect(rect),
                screenRect,
                clip,
                tip.uniqueId,
                (int)tip.priority,
                resolve);
            // A tip may land on a rect with no captured widget under it, or ahead of a texture drawn
            // moments later on the same rect that turns out to be the widget it was about.
            WidgetCapture.TryRecordOrphanTipRow(rect, liveClip, resolve);
            WidgetCapture.NoteLastBareTip(rect, liveClip, resolve);
        }

        internal static void RecordByKey(Rect rect, string key, int argCount, Func<string> resolve)
        {
            if (string.IsNullOrEmpty(key) || (!detachedPass && !harvestPass && (!armed || !passOpen)))
            {
                return;
            }
            if (WidgetCapture.FilterPanelSuppressed || (!WidgetCapture.DetachedPassOpen && WidgetCapture.ClipUnseen()))
            {
                return;
            }
            // ByKey tips build no TipSignal without a mouse, so there is no vanilla uniqueId to
            // reuse; key plus arg count is unique enough for within-pass dedup, and a collision only
            // merges two keyed tips that read identically anyway.
            GuiSpace.ClipKey liveClip = GuiSpace.CurrentClip();
            TipRect screenRect = ToTipRect(GuiSpace.ToScreen(rect));
            TipClip clip = ToTipClip(liveClip);
            TargetIndex().Add(ToTipRect(rect), screenRect, clip, key.GetHashCode() ^ (argCount << 16), 0, resolve);
            WidgetCapture.TryRecordOrphanTipRow(rect, liveClip, resolve);
            WidgetCapture.NoteLastBareTip(rect, liveClip, resolve);
        }
    }

    [HarmonyPatch(typeof(TooltipHandler), "TipRegion", new Type[] { typeof(Rect), typeof(TipSignal) })]
    public static class TooltipCaptureSignalPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, TipSignal tip)
        {
            TooltipCapture.RecordSignal(rect, tip);
        }
    }

    [HarmonyPatch(typeof(TooltipHandler), "TipRegionByKey", new Type[] { typeof(Rect), typeof(string) })]
    public static class TooltipCaptureByKeyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, string key)
        {
            TooltipCapture.RecordByKey(rect, key, 0, () => key.Translate().Resolve());
        }
    }

    [HarmonyPatch(typeof(TooltipHandler), "TipRegionByKey", new Type[] { typeof(Rect), typeof(string), typeof(NamedArgument) })]
    public static class TooltipCaptureByKey1Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, string key, NamedArgument arg1)
        {
            TooltipCapture.RecordByKey(rect, key, 1, () => key.Translate(arg1).Resolve());
        }
    }

    [HarmonyPatch(typeof(TooltipHandler), "TipRegionByKey", new Type[] { typeof(Rect), typeof(string), typeof(NamedArgument), typeof(NamedArgument) })]
    public static class TooltipCaptureByKey2Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, string key, NamedArgument arg1, NamedArgument arg2)
        {
            TooltipCapture.RecordByKey(rect, key, 2, () => key.Translate(arg1, arg2).Resolve());
        }
    }

    [HarmonyPatch(typeof(TooltipHandler), "TipRegionByKey", new Type[] { typeof(Rect), typeof(string), typeof(NamedArgument), typeof(NamedArgument), typeof(NamedArgument) })]
    public static class TooltipCaptureByKey3Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, string key, NamedArgument arg1, NamedArgument arg2, NamedArgument arg3)
        {
            TooltipCapture.RecordByKey(rect, key, 3, () => key.Translate(arg1, arg2, arg3).Resolve());
        }
    }
}
