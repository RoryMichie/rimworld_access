using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    internal sealed class HotelModule : CompatModule
    {
        public override string TargetPackageId => "Orion.Hospitality";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("Hospitality.Gizmo_GuestBed") != null
            || AccessTools.TypeByName("CashRegister.Building_CashRegister") != null;

        public override void Activate(Harmony harmony)
        {
            // CashRegister's gizmo handlers ride Hospitality's ModifyNumberGizmoHandler;
            // Hospitality registers first.
            HospitalityCompat.Register(harmony);
            HospitalityCompat.RegisterGizmoHandlers();
            CashRegisterCompat.RegisterGizmoHandlers();
            CashRegisterShiftsAdapter.TryRegister();
            HospitalityAreaColumnHandler.RegisterHospitalityColumns();
        }
    }
}
