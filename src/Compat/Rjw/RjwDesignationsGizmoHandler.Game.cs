using System.Collections;
using System.Collections.Generic;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// rjw.RJWdesignations is one Command faking a group of small pseudo-buttons;
    /// its ProcessInput only works after a mouse click primed a static
    /// last-clicked field, so the generic Command path would throw. Presents each
    /// applicable sub-icon as a checkbox menu row toggling through the sub-icon's
    /// own apply/unapply. The Hero icon opens the mod's designation dialog, so it
    /// is a plain row; the help icon is informational and gets a disabled row.
    /// </summary>
    internal sealed class RjwDesignationsGizmoHandler : GizmoHandlerBase
    {
        /// <summary>Sub-icons whose live desc resolves empty fall back to the mod's short label keys.</summary>
        private static readonly Dictionary<string, string> fallbackLabelKeys = new Dictionary<string, string>
        {
            { "Comfort", "ForComfort" },
            { "Service", "ForService" },
            { "BreedingHuman", "ForBreeding" },
            { "BreedingAnimal", "ForBreeding" },
            { "Breeder", "ForBreedingAnimal" },
            { "Milking", "ForMilking" },
            { "Hero", "ForHero" },
        };

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = "RimWorldAccess.Compat.Rjw.DesignationsLabel".Loc().ToString();
            return true;
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;
            Pawn pawn = OwnerOf(gizmo);
            if (pawn == null)
            {
                return false;
            }
            List<string> sentences = new List<string>();
            foreach (object icon in ApplicableIcons(gizmo, pawn))
            {
                string label = OptionLabel(icon, pawn);
                sentences.Add(IsInformational(icon) ? label : label + ", " + StateWord(Applied(icon, pawn)));
            }
            if (sentences.Count == 0)
            {
                return false;
            }
            description = string.Join(". ", sentences) + ".";
            return true;
        }

        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (!TryGetExtraOptions(gizmo, options) || options.Count == 0)
            {
                return false;
            }
            WindowlessFloatMenuState.OpenTitled(
                "RimWorldAccess.Compat.Rjw.DesignationsLabel".Loc().ToString(), options);
            return true;
        }

        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            Pawn pawn = OwnerOf(gizmo);
            if (pawn == null)
            {
                return false;
            }
            int before = options.Count;
            foreach (object icon in ApplicableIcons(gizmo, pawn))
            {
                object captured = icon;
                string label = OptionLabel(icon, pawn);
                if (IsInformational(icon))
                {
                    options.Add(new FloatMenuOption(label, null));
                }
                else if (OpensDialog(icon))
                {
                    options.Add(new FloatMenuOption(label + ", " + StateWord(Applied(icon, pawn)),
                        () => Toggle(captured, pawn)));
                }
                else
                {
                    options.Add(new CheckboxFloatMenuOption(label,
                        () => Toggle(captured, pawn),
                        () => Applied(captured, pawn)));
                }
            }
            return options.Count > before;
        }

        /// <summary>The mod's own toggle vehicle: SubIcon.apply/unapply, chosen off the live state as ProcessInput does.</summary>
        private static void Toggle(object icon, Pawn pawn)
        {
            if (Applied(icon, pawn))
            {
                Guarded.Call(RjwReflection.SubIconUnapply, icon, "Rjw designation toggle", pawn);
            }
            else
            {
                Guarded.Call(RjwReflection.SubIconApply, icon, "Rjw designation toggle", pawn);
            }
        }

        private static IEnumerable<object> ApplicableIcons(Gizmo gizmo, Pawn pawn)
        {
            if (!(Guarded.FieldOf<object>(RjwReflection.DesignationsSubIcons, gizmo,
                "Rjw designations list", null) is IEnumerable icons))
            {
                yield break;
            }
            foreach (object icon in icons)
            {
                if (icon != null
                    && Guarded.Call(RjwReflection.SubIconApplicable, icon, "Rjw designation gate", pawn)
                        is bool applicable
                    && applicable)
                {
                    yield return icon;
                }
            }
        }

        /// <summary>The drawn tooltip: desc returns a translation key resolved at draw time, so mirror that.</summary>
        private static string OptionLabel(object icon, Pawn pawn)
        {
            string descKey = Guarded.Call(RjwReflection.SubIconDesc, icon, "Rjw designation desc", pawn) as string;
            string text = string.IsNullOrEmpty(descKey) ? "" : CompatText.ModText(descKey);
            if (string.IsNullOrEmpty(text)
                && fallbackLabelKeys.TryGetValue(icon.GetType().Name, out string shortKey)
                && shortKey.CanTranslate())
            {
                text = CompatText.ModText(shortKey);
            }
            if (string.IsNullOrEmpty(text))
            {
                text = GenText.SplitCamelCase(icon.GetType().Name);
            }
            return SpeechFlatten.ToSentences(text);
        }

        private static bool Applied(object icon, Pawn pawn)
        {
            return Guarded.Call(RjwReflection.SubIconApplied, icon, "Rjw designation state", pawn)
                is bool applied && applied;
        }

        /// <summary>The help icon's apply is a no-op; the text itself is the whole content.</summary>
        private static bool IsInformational(object icon)
        {
            return icon.GetType().Name == "HelpImStupid";
        }

        /// <summary>The Hero icon's apply opens a confirmation window, so its row must close the menu first.</summary>
        private static bool OpensDialog(object icon)
        {
            return icon.GetType().Name == "Hero";
        }

        private static Pawn OwnerOf(Gizmo gizmo)
        {
            return Guarded.FieldOf<Pawn>(RjwReflection.DesignationsParent, gizmo, "Rjw designations owner", null);
        }

        private static string StateWord(bool value)
        {
            return (value
                ? "RimWorldAccess.Shell.State.Checked"
                : "RimWorldAccess.Shell.State.Unchecked").Loc().ToString();
        }
    }
}
