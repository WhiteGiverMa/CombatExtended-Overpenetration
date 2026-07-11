using System;
using CombatExtended;
using HarmonyLib;
using Verse;

namespace CEOverpenetration;

public static class Patches
{
    [HarmonyPatch(typeof(BulletCE), nameof(BulletCE.Impact))]
    public static class Patch_BulletCE_Impact
    {
        static void Prefix(BulletCE __instance, Thing hitThing)
        {
            OverpenetrationBridge.BeginImpact(__instance, hitThing);
        }

        static void Postfix(BulletCE __instance)
        {
            OverpenetrationBridge.EndImpact(__instance);
        }

        static Exception Finalizer(BulletCE __instance, Exception __exception)
        {
            OverpenetrationBridge.EndImpact(__instance);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ArmorUtilityCE), nameof(ArmorUtilityCE.GetAfterArmorDamage))]
    public static class Patch_GetAfterArmorDamage
    {
        static void Prefix()
        {
            OverpenetrationBridge.BeginArmorCalculation();
        }

        static void Postfix(
            DamageInfo originalDinfo,
            Pawn pawn,
            DamageInfo __result,
            bool armorDeflected,
            bool shieldAbsorbed)
        {
            OverpenetrationBridge.EndArmorCalculation(
                originalDinfo,
                __result,
                armorDeflected,
                shieldAbsorbed,
                pawn);
        }
    }

    [HarmonyPatch(typeof(ProjectileCE), nameof(ProjectileCE.Impact), typeof(Thing))]
    public static class Patch_ProjectileCE_Impact
    {
        static bool Prefix(ProjectileCE __instance)
        {
            return !OverpenetrationBridge.ConsumeSkipBaseImpact(__instance);
        }
    }

    [HarmonyPatch(typeof(ProjectileCE), "CanCollideWith")]
    public static class Patch_CanCollideWith
    {
        static void Postfix(ProjectileCE __instance, Thing thing, ref bool __result)
        {
            if (__result && OverpenetrationBridge.WasAlreadyHit(__instance, thing))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(ProjectileCE), nameof(ProjectileCE.Tick))]
    public static class Patch_ProjectileCE_Tick
    {
        static void Prefix(ProjectileCE __instance)
        {
            OverpenetrationBridge.MaintainCollisionHorizon(__instance);
        }
    }

    [HarmonyPatch(typeof(ProjectileCE), nameof(ProjectileCE.ExposeData))]
    public static class Patch_ProjectileCE_ExposeData
    {
        static void Postfix(ProjectileCE __instance)
        {
            OverpenetrationBridge.ExposeData(__instance);
        }
    }
}
