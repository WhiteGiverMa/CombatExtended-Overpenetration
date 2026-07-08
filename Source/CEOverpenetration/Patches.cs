using CombatExtended;
using RimWorld;
using Verse;
using HarmonyLib;

namespace CEOverpenetration;

/// <summary>
/// All Harmony patches for overpenetration.
/// </summary>
public static class Patches
{
    // ═══════════════════════════════════════════════════════════════
    // BulletCE.Impact — set CurrentBullet in Prefix so GetAfterArmorDamage
    // Postfix can access it. Clear in Postfix as safety net.
    // ═══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(BulletCE), nameof(BulletCE.Impact))]
    public static class Patch_BulletCE_Impact
    {
        // Prefix: set CurrentBullet so GetAfterArmorDamage Postfix can access it
        static void Prefix(BulletCE __instance)
        {
            OverpenetrationBridge.CurrentBullet = __instance;
        }

        // Postfix: safety net — CurrentBullet should already be null (cleared in
        // GetAfterArmorDamage Postfix). But in case GetAfterArmorDamage was never
        // called (non-Pawn target), clear it here.
        static void Postfix(BulletCE __instance)
        {
            OverpenetrationBridge.CurrentBullet = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ArmorUtilityCE.GetAfterArmorDamage — THE critical patch.
    // Runs inside TakeDamage, BEFORE BulletCE.Impact's finally block.
    // This is where we do the penetration check, set skipBaseImpact,
    // and handle overpenetration (restore flight, move position, etc).
    // ═══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(ArmorUtilityCE), nameof(ArmorUtilityCE.GetAfterArmorDamage))]
    public static class Patch_GetAfterArmorDamage
    {
        static void Postfix(DamageInfo originalDinfo, Pawn pawn, DamageInfo __result, bool armorDeflected)
        {
            OverpenetrationBridge.OnArmorCalculated(originalDinfo, __result, armorDeflected, pawn);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ProjectileCE.Impact — skip when overpenetrating (avoid Destroy)
    // This is base.Impact called from BulletCE.Impact's finally block.
    // ═══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(ProjectileCE), nameof(ProjectileCE.Impact), typeof(Thing))]
    public static class Patch_ProjectileCE_Impact
    {
        static bool Prefix(ProjectileCE __instance)
        {
            var state = OverpenetrationBridge.GetState(__instance);
            if (state != null && state.skipBaseImpact)
            {
                state.skipBaseImpact = false; // Reset flag
                return false; // Skip base.Impact → no Destroy, no explosion effects
            }
            return true;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ProjectileCE.DamageAmount — apply kinetic multiplier
    // ═══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(ProjectileCE), "DamageAmount")]
    [HarmonyPatch(MethodType.Getter)]
    public static class Patch_DamageAmount
    {
        static void Postfix(ProjectileCE __instance, ref float __result)
        {
            __result = OverpenetrationBridge.ApplyKineticMult(__instance, __result);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ProjectileCE.PenetrationAmount — apply kinetic multiplier
    // ═══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(ProjectileCE), "PenetrationAmount")]
    [HarmonyPatch(MethodType.Getter)]
    public static class Patch_PenetrationAmount
    {
        static void Postfix(ProjectileCE __instance, ref float __result)
        {
            __result = OverpenetrationBridge.ApplyKineticMult(__instance, __result);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ProjectileCE.CanCollideWith — skip already-hit things
    // ═══════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════
    // ProjectileCE.ImpactSomething — skip during overpenetration flight
    // ═══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(ProjectileCE), nameof(ProjectileCE.ImpactSomething))]
    public static class Patch_ImpactSomething
    {
        static bool Prefix(ProjectileCE __instance)
        {
            if (OverpenetrationBridge.IsOverpenetrating(__instance))
            {
                return false; // Skip ImpactSomething — let CheckForCollisionBetween handle new targets
            }
            return true;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Verb_LaunchProjectileCE.CanHitTargetFrom — fix breaching AI LoS loop.
    // CE's strict 3D LoS makes CanHitTargetFrom return false for walls/rocks,
    // causing CastPositionFinder to fail → job ends → JobGiver_AIBreaching
    // re-issues same job → "started 10 jobs in one tick" infinite loop.
    // Fix: for Building targets with Fillage=Full or mineable, allow hit
    // regardless of LoS (matching vanilla breaching behavior).
    // ═══════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(Verb_LaunchProjectileCE), nameof(Verb_LaunchProjectileCE.CanHitTargetFrom), typeof(IntVec3), typeof(LocalTargetInfo))]
    public static class Patch_CanHitTargetFrom_BreachingFix
    {
        static void Postfix(Verb_LaunchProjectileCE __instance, IntVec3 root, LocalTargetInfo targ, ref bool __result)
        {
            if (!__result && targ.HasThing)
            {
                if (OverpenetrationBridge.ShouldAllowHitWithoutLoS(targ.Thing))
                {
                    // Still respect range limits — just skip LoS for breaching targets
                    float distSq = (root - targ.Cell).LengthHorizontalSquared;
                    float maxRange = __instance.EffectiveRange;
                    if (distSq <= maxRange * maxRange)
                    {
                        __result = true;
                    }
                }
            }
        }
    }
}
