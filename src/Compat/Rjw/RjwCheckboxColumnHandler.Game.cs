using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// The mod's abstract checkbox column base mirrors vanilla
    /// PawnColumnWorker_Checkbox's surface (HasCheckbox/GetValue/SetValue/GetTip)
    /// but subclasses PawnColumnWorker directly, so the vanilla handler never
    /// resolves. Registered by base type so every subclass — the mod's own columns
    /// and any addon's — inherits reading and toggling through the worker's own
    /// setter. The extra GetDisabled gate draws a greyed, unclickable box, which
    /// reads as disabled and declines activation.
    /// </summary>
    internal sealed class RjwCheckboxColumnHandler : PawnColumnHandler
    {
        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            PawnColumnWorker worker = def.Worker;
            if (!(bool)RjwReflection.ColumnHasCheckbox.Invoke(worker, new object[] { pawn }))
            {
                return "";
            }
            if ((bool)RjwReflection.ColumnGetDisabled.Invoke(worker, new object[] { pawn }))
            {
                return CheckStateWord(false) + ", " + "RimWorldAccess.Shell.State.Disabled".Loc();
            }
            return CheckStateWord((bool)RjwReflection.ColumnGetValue.Invoke(worker, new object[] { pawn }));
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            string tip = (string)RjwReflection.ColumnGetTip.Invoke(def.Worker, new object[] { pawn });
            return string.IsNullOrEmpty(tip) ? null : SpeechFlatten.ToSentences(tip.StripTags());
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            PawnColumnWorker worker = def.Worker;
            if (!(bool)RjwReflection.ColumnHasCheckbox.Invoke(worker, new object[] { pawn })
                || (bool)RjwReflection.ColumnGetDisabled.Invoke(worker, new object[] { pawn }))
            {
                return PawnColumnActivation.NotHandled;
            }
            bool value = (bool)RjwReflection.ColumnGetValue.Invoke(worker, new object[] { pawn });
            RjwReflection.ColumnSetValue.Invoke(worker, new object[] { pawn, !value });
            (!value ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            return PawnColumnActivation.StateChanged;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }
    }
}
