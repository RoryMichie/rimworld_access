using System;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// A float menu row with checkbox semantics: announced with the shell's checkbox grammar,
    /// toggled in place (the windowless menu stays open and speaks the refreshed state), and
    /// drawn with a live check mark in both the vanilla menu and the visual twin.
    /// </summary>
    public class CheckboxFloatMenuOption : FloatMenuOption
    {
        private const float CheckSize = 24f;

        public readonly Func<bool> StateGetter;

        public CheckboxFloatMenuOption(string label, Action toggle, Func<bool> stateGetter)
            : base(label, toggle)
        {
            StateGetter = stateGetter;
            extraPartWidth = CheckSize;
            extraPartOnGUI = DrawCheckbox;
        }

        private bool DrawCheckbox(Rect rect)
        {
            Texture2D tex = StateGetter() ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex;
            GUI.DrawTexture(
                new Rect(rect.x, rect.y + (rect.height - CheckSize) / 2f, CheckSize, CheckSize),
                tex);
            return false;
        }
    }
}
