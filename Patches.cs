using Gameplay.Entities.Creatures.Dragon;
using GenericServices.Foundation.GenericServices.Runtime.StatManagement;
using GlobalServices.PersistenceStructure;
using HarmonyLib;

namespace DragonShelterDragonNeedsV3
{
    [HarmonyPatch(typeof(Dragon), "Constructor", new[] { typeof(DragonData), typeof(EntitySaveData) })]
    internal static class DragonConstructedPatch
    {
        static void Postfix(Dragon __instance) => Runtime.Instance?.RegisterDragon(__instance, "Constructor");
    }

    [HarmonyPatch(typeof(StatManager), nameof(StatManager.Set), new[] { typeof(Stat) })]
    internal static class StatStoredPatch
    {
        [HarmonyPriority(Priority.Last)]
        static void Prefix(StatManager __instance, ref Stat __0, out WriteReceipt __state)
        {
            __state = null;
            Runtime.Instance?.BeforeStore(__instance, ref __0, out __state);
        }
        [HarmonyPriority(Priority.Last)]
        static void Postfix(StatManager __instance, WriteReceipt __state) => Runtime.Instance?.AfterStore(__instance, __state);
    }
}
