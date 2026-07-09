using CombatExtended;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

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
            if (OverpenetrationBridge.IsOverpenetrating(__instance) && __instance.ExactPosition.y > 0f)
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
            if (!__result && OverpenetrationBridge.TryAllowBreachingShotWithoutLoS(__instance, root, targ, out _, out _))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_CanHitTargetFrom_Report_BreachingFix
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Verb_LaunchProjectileCE),
                nameof(Verb_LaunchProjectileCE.CanHitTargetFrom),
                new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(string).MakeByRefType() });
        }

        static void Postfix(Verb_LaunchProjectileCE __instance, IntVec3 root, LocalTargetInfo targ, ref string report, ref bool __result)
        {
            if (!__result && OverpenetrationBridge.TryAllowBreachingShotWithoutLoS(__instance, root, targ, out _, out _))
            {
                report = "";
                __result = true;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_TryFindCEShootLineFromTo_BreachingFix
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Verb_LaunchProjectileCE),
                nameof(Verb_LaunchProjectileCE.TryFindCEShootLineFromTo),
                new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(ShootLine).MakeByRefType(), typeof(Vector3).MakeByRefType() });
        }

        static void Postfix(Verb_LaunchProjectileCE __instance, IntVec3 root, LocalTargetInfo targ, ref ShootLine resultingLine, ref Vector3 targetPos, ref bool __result)
        {
            if (!__result && OverpenetrationBridge.TryAllowBreachingShotWithoutLoS(__instance, root, targ, out var line, out var pos))
            {
                resultingLine = line;
                targetPos = pos;
                __result = true;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_JobGiver_AIBreaching_RepeatGuard
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(JobGiver_AIBreaching), "TryGiveJob");
        }

        static void Postfix(Pawn pawn, ref Job __result)
        {
            if (OverpenetrationBridge.ShouldReplaceInvalidBreachingJob(pawn, __result))
            {
                __result = JobMaker.MakeJob(JobDefOf.Wait, 60);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_PawnJobTracker_StartJob_BreachingGuard
    {
        static void Prefix(Pawn ___pawn, ref Job newJob, ThinkNode jobGiver)
        {
            if (OverpenetrationBridge.ShouldThrottleBreachingStartJob(___pawn, newJob, jobGiver))
            {
                newJob = JobMaker.MakeJob(JobDefOf.Wait, 60);
            }
        }
    }

    [HarmonyPatch(typeof(Toils_Combat), nameof(Toils_Combat.GotoCastPosition))]
    public static class Patch_GotoCastPosition_BreachingFix
    {
        static void Postfix(TargetIndex targetInd, TargetIndex castPositionInd, Toil __result)
        {
            var originalInit = __result.initAction;
            __result.initAction = () =>
            {
                if (OverpenetrationBridge.TryStartKnownBreachingCastPosition(__result.actor, targetInd, castPositionInd))
                {
                    return;
                }

                originalInit?.Invoke();
            };
        }
    }
}
