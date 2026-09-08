using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard reader for the credits / victory screen (<see cref="Screen_Credits"/>):
    /// every entry a sighted player watches scroll by — the victory message when a
    /// game was won, section titles, and the contributor roles — as the shared big-text
    /// buffer (<see cref="TextBufferScope"/>) in the screen's own draw order, with each
    /// title record starting a Page Up/Page Down section. Row navigation drives the
    /// screen's own scrolling (vanilla's private Scroll method, the same one its arrow
    /// keys call), so the visible credits track the keyboard cursor. Escape is deliberately NOT
    /// claimed: Screen_Credits.OnCancelKeyPressed runs the screen's own
    /// skip-fade-close choreography, and the pause-menu Escape stays suppressed by
    /// the screen's preventCameraMotion. That choreography is silent and eats the
    /// KeyDown in the window's own pass, so <see cref="CreditsSkipAnnouncementPatch"/>
    /// speaks it.
    ///
    /// <see cref="CreditsWindowArrowMaskPatch"/>: the window's own DoWindowContents
    /// consumes bare Up/Down KeyDowns for freehand scrolling, and the focused
    /// window's GUI pass can run BEFORE the dispatcher (QA R6) — so while this scope
    /// is live those two keys are masked around the vanilla body (keyCode swap +
    /// restore, the sanctioned window-pass guard shape) and row navigation owns them.
    /// </summary>
    public sealed class CreditsScope : TextBufferScope
    {
        private static readonly FieldInfo CredsField = AccessTools.Field(typeof(Screen_Credits), "creds");
        private static readonly FieldInfo ScrollPositionField = AccessTools.Field(typeof(Screen_Credits), "scrollPosition");
        private static readonly MethodInfo ScrollMethod = AccessTools.Method(typeof(Screen_Credits), "Scroll");
        private static readonly PropertyInfo ViewWidthProp = AccessTools.Property(typeof(Screen_Credits), "ViewWidth");

        private readonly Screen_Credits screen;
        private readonly List<float> rowOffsets = new List<float>();
        private readonly List<float> rowHeights = new List<float>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private int focusedRow = -1;

        public CreditsScope(Screen_Credits screen)
        {
            this.screen = screen;
        }

        public override string Name
        {
            get { return "credits"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return screen; }
        }

        protected override string TextRegionName
        {
            get { return "Credits".Translate(); }
        }

        /// <summary>
        /// Vanilla draws its Skip credits button only once the credits have
        /// scrolled off zero and hides it again during the fade; declaring the
        /// action keeps the keyboard affordance constant, and capture stays off
        /// so the same button cannot also arrive as a captured row.
        /// </summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction((string)"SkipCredits".Translate(), SkipCredits, "credits.skip"));
                return actions;
            }
        }

        /// <summary>Enter on any row offers this, the screen's only action.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "credits.skip"; }
        }

        /// <summary>
        /// Exactly what vanilla's own Skip button runs. The utterance belongs to
        /// <see cref="CreditsSkipAnnouncementPatch"/>, which also covers the Escape path.
        /// </summary>
        private void SkipCredits()
        {
            screen.OnCancelKeyPressed();
        }

        protected override void BuildTextBuffer()
        {
            rowOffsets.Clear();
            rowHeights.Clear();
            focusedRow = -1;

            if (!(CredsField?.GetValue(screen) is IEnumerable creds))
            {
                return;
            }

            float viewWidth = ViewWidthProp != null ? (float)ViewWidthProp.GetValue(screen) : 800f;

            // Mirror ViewHeight's own measurement conditions so the offsets match
            // what the screen actually draws.
            GameFont font = Text.Font;
            Text.Font = GameFont.Medium;
            try
            {
                float y = 0f;
                foreach (object entry in creds)
                {
                    string label = LabelFor(entry);
                    float height = entry is CreditsEntry heightEntry ? heightEntry.DrawHeight(viewWidth) : 0f;
                    if (!label.NullOrEmpty())
                    {
                        // The title records are the screen's own section headings
                        // (CreditsAssembler.AllCredits emits one per block).
                        if (entry is CreditRecord_Title || entry is CreditRecord_TitleLocalization)
                        {
                            AddSectionLine(label);
                        }
                        else
                        {
                            AddLine(label);
                        }
                        rowOffsets.Add(y);
                        rowHeights.Add(height);
                    }
                    if (entry is CreditsEntry cred)
                    {
                        y += height;
                    }
                }
            }
            finally
            {
                Text.Font = font;
            }
        }

        /// <summary>
        /// The entry's visible text, composed exactly from what its Draw shows: a
        /// role row speaks its role name only when the screen itself displays it
        /// (displayKey), then the creditee and the extra note.
        /// </summary>
        private static string LabelFor(object entry)
        {
            switch (entry)
            {
                case CreditRecord_Text text:
                    return FlattenLines(text.text);
                case CreditRecord_Title title:
                    return FlattenLines(title.title);
                case CreditRecord_TitleLocalization titleLoc:
                    return FlattenLines(titleLoc.title);
                case CreditRecord_Role role:
                {
                    string label = role.displayKey && !role.roleKey.NullOrEmpty()
                        ? role.roleKey + ": " + role.creditee
                        : role.creditee;
                    if (!role.extra.NullOrEmpty())
                    {
                        label += ", " + FlattenLines(role.extra);
                    }
                    return label;
                }
                case CreditRecord_RoleTwoCols two:
                {
                    string label = two.creditee1;
                    if (!two.creditee2.NullOrEmpty())
                    {
                        label += ", " + two.creditee2;
                    }
                    if (!two.extra.NullOrEmpty())
                    {
                        label += ", " + FlattenLines(two.extra);
                    }
                    return label;
                }
                default:
                    return null;
            }
        }

        private static string FlattenLines(string text)
        {
            if (text.NullOrEmpty())
            {
                return text;
            }
            return string.Join(". ", text.StripTags()
                .Split('\n'))
                .Trim();
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.Credits.Opened".Translate(TextLineCount, "SkipCredits".Translate());
        }

        /// <summary>
        /// Visual parity: scroll the screen's own view to the focused row through
        /// vanilla's private Scroll (the exact method its arrow keys call, which
        /// also pauses auto-scroll for a few seconds), never a raw field write.
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            focusedRow = (index >= 0 && index < rowOffsets.Count) ? index : -1;
            if (index < 0 || index >= rowOffsets.Count || ScrollMethod == null || ScrollPositionField == null)
            {
                return;
            }
            try
            {
                float current = (float)ScrollPositionField.GetValue(screen);
                ScrollMethod.Invoke(screen, new object[] { rowOffsets[index] - current });
            }
            catch (Exception)
            {
                // Scroll mirroring is cosmetic; never let it break navigation.
            }
        }

        /// <summary>
        /// Screen_Credits is a fullscreen window at origin, so window space is
        /// absolute UI points; the column geometry mirrors Screen_Credits.DoWindowContents.
        /// </summary>
        protected internal override Rect FocusedContentRect()
        {
            if (focusedRow < 0 || focusedRow >= rowOffsets.Count || focusedRow >= rowHeights.Count
                || ScrollPositionField == null)
            {
                return default(Rect);
            }
            try
            {
                float scrollPosition = (float)ScrollPositionField.GetValue(screen);
                float top = 30f + rowOffsets[focusedRow] - scrollPosition;
                float bottom = top + rowHeights[focusedRow];
                float columnTop = 30f;
                float columnBottom = UI.screenHeight - 30f;
                float clippedTop = Mathf.Max(top, columnTop);
                float clippedBottom = Mathf.Min(bottom, columnBottom);
                if (clippedBottom <= clippedTop)
                {
                    return default(Rect);
                }
                return new Rect(UI.screenWidth / 2f - 400f, clippedTop, 800f, clippedBottom - clippedTop);
            }
            catch (Exception)
            {
                return default(Rect);
            }
        }
    }

    /// <summary>
    /// While a CreditsScope is live, masks the bare Up/Down KeyDowns from
    /// Screen_Credits' own DoWindowContents freehand-scroll handling (keyCode swap
    /// before the vanilla body, restore after — the sanctioned window-pass guard
    /// shape), so the shared menu grammar's row navigation owns those keys
    /// regardless of whether the window's GUI pass runs before or after the
    /// dispatcher this frame.
    /// </summary>
    [HarmonyPatch(typeof(Screen_Credits), nameof(Screen_Credits.DoWindowContents))]
    internal static class CreditsWindowArrowMaskPatch
    {
        private static bool ScopeLive()
        {
            return FocusStackLookup.TopmostOfType<CreditsScope>() != null;
        }

        [HarmonyPrefix]
        public static void Prefix(out KeyCode __state)
        {
            __state = KeyCode.None;
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown)
            {
                return;
            }
            if ((e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow)
                && e.modifiers == EventModifiers.None
                && ScopeLive())
            {
                __state = e.keyCode;
                e.keyCode = KeyCode.None;
                // QA R6 close-the-hole tracing: an eaten arrow KeyDown otherwise
                // leaves no [key] line at all, so this is the only record that the
                // mask fired rather than the player simply not pressing the key.
                FlightRecorder.Record("mask", "Screen_Credits masked " + __state);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(KeyCode __state)
        {
            Event e = Event.current;
            if (__state == KeyCode.None)
            {
                return;
            }
            if (e != null && e.keyCode == KeyCode.None)
            {
                e.keyCode = __state;
                return;
            }
            // The one case the mask can lose: the vanilla body consumed or
            // replaced the key before the restore, so the swap never undoes.
            FlightRecorder.Record("mask", "Screen_Credits restore skipped for " + __state
                + ", vanilla left keyCode=" + (e != null ? e.keyCode.ToString() : "(null event)"));
        }
    }

    /// <summary>
    /// Speaks the skip Screen_Credits otherwise performs in silence: the first
    /// press arms a timed close plus a fade, every press during that window is
    /// ignored by vanilla's own closeDelay guard. Both entry points land here —
    /// Escape (eaten by the window's pass before the dispatcher sees it) and the
    /// scope's Skip credits action — so this is the one announcement site.
    /// </summary>
    [HarmonyPatch(typeof(Screen_Credits), nameof(Screen_Credits.OnCancelKeyPressed))]
    internal static class CreditsSkipAnnouncementPatch
    {
        private static readonly FieldInfo CloseDelayField = AccessTools.Field(typeof(Screen_Credits), "closeDelay");

        [HarmonyPrefix]
        public static void Prefix(Screen_Credits __instance, out float __state)
        {
            __state = CloseDelayField != null ? (float)CloseDelayField.GetValue(__instance) : 0f;
        }

        [HarmonyPostfix]
        public static void Postfix(Screen_Credits __instance, float __state)
        {
            if (CloseDelayField == null || FocusStackLookup.TopmostOfType<CreditsScope>() == null)
            {
                return;
            }
            if (__state > 0f)
            {
                TolkHelper.Speak("RimWorldAccess.Credits.AlreadySkipping".Loc(), SpeechPriority.High);
                return;
            }
            float remaining = (float)CloseDelayField.GetValue(__instance);
            TolkHelper.Speak("RimWorldAccess.Credits.Skipping".Loc(remaining.ToString("0.#")), SpeechPriority.High);
        }
    }
}
