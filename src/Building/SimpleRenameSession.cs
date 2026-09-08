using System;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared modal rename engine behind <see cref="PenRenameState"/>,
    /// <see cref="ZoneRenameState"/>, and <see cref="PlanRenameState"/>. Each facade
    /// supplies the target's label accessors, the TextInput label key, and the
    /// Rename error key; this class owns the <see cref="TextInputController"/>
    /// session and the confirm/cancel plumbing so the three facades stay
    /// near-trivial parameterizations of one engine.
    /// </summary>
    internal sealed class SimpleRenameSession<T> where T : class, IRenameable
    {
        private readonly TextInputController controller = new TextInputController();
        private readonly string targetNoun;
        private readonly string labelKey;
        private readonly string errorKey;
        private readonly Func<T, string> getLabel;
        private readonly Action<T, string> setLabel;
        private readonly bool logSuccess;

        private T currentTarget;
        private string originalName;

        /// <param name="targetNoun">Lowercase noun used in log text (e.g. "pen marker", "zone", "plan").</param>
        /// <param name="labelKey">TextInput label translation key for the edit field.</param>
        /// <param name="errorKey">Translation key for the commit-failure announcement.</param>
        /// <param name="getLabel">Reads the target's current display label.</param>
        /// <param name="setLabel">Writes the confirmed name back onto the target.</param>
        /// <param name="logSuccess">Whether a successful rename also writes a Log.Message.</param>
        public SimpleRenameSession(
            string targetNoun,
            string labelKey,
            string errorKey,
            Func<T, string> getLabel,
            Action<T, string> setLabel,
            bool logSuccess = true)
        {
            this.targetNoun = targetNoun;
            this.labelKey = labelKey;
            this.errorKey = errorKey;
            this.getLabel = getLabel;
            this.setLabel = setLabel;
            this.logSuccess = logSuccess;
        }

        public bool IsActive => TextInputManager.Active == controller;

        public void Open(T target)
        {
            if (target == null)
            {
                Log.Error($"Cannot open rename dialog: {targetNoun} is null");
                return;
            }
            currentTarget = target;
            originalName = getLabel(target);
            var spec = TextFieldSpec.ForIRenameable(target, labelKey);
            controller.Begin(originalName, spec, OnConfirm, OnCancel, replaceOnType: true);
        }

        private void OnConfirm(string newName)
        {
            try
            {
                setLabel(currentTarget, newName);
                TolkHelper.Speak("RimWorldAccess.UI.Name.Renamed".Loc(newName), SpeechPriority.High);
                if (logSuccess)
                    ModLogger.Dev($"Renamed {targetNoun} from '{originalName}' to '{newName}'");
            }
            catch (Exception ex)
            {
                TolkHelper.Speak(errorKey.Loc(ex.Message), SpeechPriority.High);
                Log.Error($"Error renaming {targetNoun}: {ex}");
            }
            finally
            {
                ClearTarget();
            }
        }

        private void OnCancel()
        {
            TolkHelper.Speak("RimWorldAccess.Building.Rename.Cancelled".Loc());
            ClearTarget();
        }

        private void ClearTarget()
        {
            currentTarget = null;
            originalName = null;
        }
    }
}
