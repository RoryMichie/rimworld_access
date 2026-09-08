using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Reads the tooltips vanilla itself registered under the REAL pointer,
    /// straight out of <see cref="TooltipHandler"/>'s own active-tip table.
    /// This is the one channel that sees a caller-gated registration — an
    /// <c>if (Mouse.IsOver(rect)) TooltipHandler.TipRegion(...)</c> call site is
    /// invisible to <see cref="TooltipCapture"/> without a mouse, but it fires
    /// for a hovering one (see TooltipCapture's class remarks) — so hover hears
    /// every tooltip a sighted player sees. Hover only: the keyboard's focused
    /// row has no pointer over it.
    ///
    /// <c>TipRegion</c> stamps
    /// <c>lastTriggerFrame = frame</c> on Repaint under the mouse, and
    /// <c>DoTooltipGUI</c> increments <c>frame</c> once per Repaint after every
    /// window has drawn. So during a window's draw postfix, "registered inside
    /// this bracket" is exactly "stamped with the bracket's frame value AND not
    /// already carrying that stamp when the bracket opened" — which no rect
    /// query is needed for, and which cannot pick up a tip another window
    /// registered earlier in the same frame.
    /// </summary>
    internal static class LiveTooltips
    {
        private struct Tip
        {
            public double FirstTriggerTime;
            public string Text;
        }

        private static readonly AccessTools.FieldRef<Dictionary<int, ActiveTip>> activeTips;
        private static readonly AccessTools.FieldRef<int> tipFrame;
        private static readonly bool available;

        // lastTriggerFrame per live tip as the bracket opened; an entry that is
        // absent here, or that carries a different stamp, registered inside it.
        private static readonly Dictionary<int, int> snapshot = new Dictionary<int, int>();
        private static readonly List<Tip> collected = new List<Tip>();
        private static readonly Comparison<Tip> CompareTips = CompareTipsImpl;
        private static int bracketFrame = -1;

        static LiveTooltips()
        {
            try
            {
                activeTips = AccessTools.StaticFieldRefAccess<Dictionary<int, ActiveTip>>(
                    AccessTools.Field(typeof(TooltipHandler), "activeTips"));
                tipFrame = AccessTools.StaticFieldRefAccess<int>(
                    AccessTools.Field(typeof(TooltipHandler), "frame"));
                available = activeTips != null && tipFrame != null;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Live tooltip channel unavailable", ex);
                return;
            }
            if (!available)
            {
                // Unbound: both methods no-op.
                ModLogger.Error("Live tooltip channel unavailable: TooltipHandler.activeTips/frame did not bind");
            }
        }

        /// <summary>Open the bracket, from the same prefix that opens the surface's capture pass.</summary>
        internal static void BeginBracket()
        {
            if (!available || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            try
            {
                snapshot.Clear();
                foreach (KeyValuePair<int, ActiveTip> pair in activeTips())
                {
                    if (pair.Value != null)
                    {
                        snapshot[pair.Key] = pair.Value.lastTriggerFrame;
                    }
                }
                bracketFrame = tipFrame();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Live tooltip bracket error", ex);
            }
        }

        /// <summary>
        /// Appends the resolved text of every tip registered since
        /// <see cref="BeginBracket"/>, oldest tip first (a Dictionary's
        /// enumeration order is not a contract, so the order is earned by
        /// sorting).
        /// </summary>
        internal static void CollectSinceBracket(List<string> into)
        {
            if (!available || into == null || bracketFrame < 0)
            {
                return;
            }
            try
            {
                collected.Clear();
                foreach (KeyValuePair<int, ActiveTip> pair in activeTips())
                {
                    ActiveTip tip = pair.Value;
                    int before;
                    if (tip == null || tip.lastTriggerFrame != bracketFrame
                        || (snapshot.TryGetValue(pair.Key, out before) && before == tip.lastTriggerFrame))
                    {
                        continue;
                    }
                    string text = ResolveText(tip);
                    if (!string.IsNullOrEmpty(text))
                    {
                        collected.Add(new Tip { FirstTriggerTime = tip.firstTriggerTime, Text = text });
                    }
                }
                collected.Sort(CompareTips);
                for (int i = 0; i < collected.Count; i++)
                {
                    into.Add(collected[i].Text);
                }
                collected.Clear();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Live tooltip collection error", ex);
            }
        }

        private static int CompareTipsImpl(Tip a, Tip b)
        {
            int byTime = a.FirstTriggerTime.CompareTo(b.FirstTriggerTime);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.Text, b.Text);
        }

        private static string ResolveText(ActiveTip tip)
        {
            string text = tip.signal.text;
            if (string.IsNullOrEmpty(text) && tip.signal.textGetter != null)
            {
                try
                {
                    text = tip.signal.textGetter();
                }
                catch (Exception)
                {
                    // A mod's getter can throw; vanilla's own FinalText catches too.
                    return null;
                }
            }
            return SpeechFlatten.ToSentences(text);
        }
    }
}
