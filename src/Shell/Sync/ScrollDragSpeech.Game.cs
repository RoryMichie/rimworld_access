using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Speaks how far a scroll view has been scrolled while its bar is being
    /// dragged. The thumb sliding is all a sighted player is shown, and the
    /// only thing derivable from what the game holds is the offset against the
    /// scrollable extent — hence a percentage. No row count is spoken: a
    /// scroll view is a pixel rect with no row structure of its own, and the
    /// screens that DO know their rows already say so through row navigation.
    ///
    /// Identity and attribution both come from Unity. The scrollbars live
    /// inside <c>GUI.BeginScrollView</c> (decompiled
    /// UnityEngine.IMGUIModule/UnityEngine/GUI.cs:1911-1919), so the offset
    /// they produce is the difference across
    /// <c>Widgets.BeginScrollView</c>'s own <c>ref</c> parameter
    /// (Verse/Widgets.cs:2863-2882); a mouse holding one of them is
    /// <c>GUIUtility.hotControl</c>, which is also the drag's stable id. The
    /// pair excludes the wheel and the keyboard, which move the same offset
    /// from outside this call with no control held.
    ///
    /// Announce-only, ungated by the hover-speech setting, and paced by the announced percentage
    /// changing rather than by pixels.
    /// </summary>
    [HarmonyPatch]
    internal static class ScrollDragSpeech
    {
        private static int spokenControl;
        private static int spokenPercentX;
        private static int spokenPercentY;

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "BeginScrollView",
                new Type[] { typeof(Rect), typeof(Vector2).MakeByRefType(), typeof(Rect), typeof(bool) });
        }

        [HarmonyPrefix]
        public static void Prefix(ref Vector2 scrollPosition, out Vector2 __state)
        {
            __state = scrollPosition;
        }

        [HarmonyPostfix]
        public static void Postfix(Rect outRect, ref Vector2 scrollPosition, Rect viewRect, bool showScrollbars, Vector2 __state)
        {
            try
            {
                int hot = GUIUtility.hotControl;
                if (hot == 0)
                {
                    spokenControl = 0;
                    return;
                }
                if (!showScrollbars || scrollPosition == __state || WidgetCapture.DetachedPass)
                {
                    return;
                }
                if (hot != spokenControl)
                {
                    spokenControl = hot;
                    spokenPercentX = -1;
                    spokenPercentY = -1;
                }

                float extentY = Mathf.Max(0f, viewRect.height - outRect.height);
                if (extentY > 0f && !Mathf.Approximately(scrollPosition.y, __state.y))
                {
                    SpeakPercent(scrollPosition.y / extentY, ref spokenPercentY,
                        "RimWorldAccess.UI.Drag.ScrollPercent");
                    return;
                }
                float extentX = Mathf.Max(0f, viewRect.width - outRect.width);
                if (extentX > 0f && !Mathf.Approximately(scrollPosition.x, __state.x))
                {
                    SpeakPercent(scrollPosition.x / extentX, ref spokenPercentX,
                        "RimWorldAccess.UI.Drag.ScrollPercentAcross");
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Scroll drag speech error", ex);
            }
        }

        private static void SpeakPercent(float fraction, ref int spoken, string key)
        {
            int percent = Mathf.RoundToInt(Mathf.Clamp01(fraction) * 100f);
            if (percent == spoken)
            {
                return;
            }
            spoken = percent;
            TolkHelper.Speak(key.Loc(percent), SpeechPriority.High);
        }
    }
}
