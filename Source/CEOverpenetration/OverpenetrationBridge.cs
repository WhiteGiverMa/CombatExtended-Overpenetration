using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CombatExtended;
using RimWorld;
using UnityEngine;
using Verse;
using HarmonyLib;

namespace CEOverpenetration;

/// <summary>
/// Central bridge for overpenetration state management.
/// Uses ConditionalWeakTable to associate OverpenState with ProjectileCE instances
/// without modifying CE source code.
/// </summary>
public static class OverpenetrationBridge
{
    // ponytail: Static "current bullet" — set in BulletCE.Impact Prefix, read AND cleared
    // in GetAfterArmorDamage Postfix (which runs inside TakeDamage, BEFORE finally).
    // RimWorld game loop is single-threaded; no concurrency risk.
    public static ProjectileCE CurrentBullet;

    private static readonly ConditionalWeakTable<ProjectileCE, OverpenState> states
        = new ConditionalWeakTable<ProjectileCE, OverpenState>();

    public static OverpenState GetOrCreateState(ProjectileCE proj)
    {
        return states.GetOrCreateValue(proj);
    }

    public static OverpenState GetState(ProjectileCE proj)
    {
        return states.TryGetValue(proj, out var state) ? state : null;
    }

    /// <summary>
    /// Get the OverpenetrationExtension from a projectile's def, or null if not configured.
    /// </summary>
    public static OverpenetrationExtension GetExtension(ThingDef def)
    {
        return def?.GetModExtension<OverpenetrationExtension>();
    }

    // ─── Core logic ───

    /// <summary>
    /// Called from ArmorUtilityCE.GetAfterArmorDamage Postfix.
    /// This runs INSIDE TakeDamage, BEFORE BulletCE.Impact's finally block.
    /// 
    /// Execution order:
    /// 1. TryCollideWith → landed=true, Impact(thing)
    /// 2. BulletCE.Impact Prefix → set CurrentBullet
    /// 3. try block → hitThing.TakeDamage(dinfo)
    /// 4.   TakeDamage internal → GetAfterArmorDamage → THIS POSTFIX
    /// 5.   ← Here we do penetration check + set skipBaseImpact + handle overpenetration
    /// 6.   Clear CurrentBullet (prevent secondary/extra damage from re-triggering)
    /// 7. TakeDamage returns
    /// 8. try block continues (secondary/extra damage — won't re-trigger, CurrentBullet is null)
    /// 9. finally → base.Impact(hitThing) → ProjectileCE.Impact Prefix → skipBaseImpact=true → SKIP Destroy
    /// 10. BulletCE.Impact Postfix → CurrentBullet already null, nothing to do
    /// 
    /// Derives remaining penetration from the damage ratio:
    /// CE's TryPenetrateArmor uses dmgMult = Clamp01(newPen/pen), so newDmg/dmg = newPen/pen.
    /// Therefore remainingPen = originalPen * (resultDmg / originalDmg).
    /// </summary>
    public static void OnArmorCalculated(DamageInfo originalDinfo, DamageInfo resultDinfo, bool armorDeflected, Pawn pawn)
    {
        var bullet = CurrentBullet;
        if (bullet == null) return;

        var state = GetOrCreateState(bullet);

        // ── Store armor result ──
        state.armorDeflected = armorDeflected;

        bool isSharp = originalDinfo.Def?.armorCategory == DamageArmorCategoryDefOf.Sharp;
        bool resultIsBlunt = resultDinfo.Def?.armorCategory != DamageArmorCategoryDefOf.Sharp;
        state.sharpWasDeflected = isSharp && resultIsBlunt;

        if (armorDeflected || state.sharpWasDeflected || originalDinfo.Amount <= 0f)
        {
            state.remainingPen = 0f;
            // Clear CurrentBullet so secondary/extra damage doesn't re-trigger
            CurrentBullet = null;
            return;
        }

        // CE formula: dmgMult = Clamp01(newPen/pen) → newDmg = dmg * dmgMult
        // So: newPen/pen = newDmg/dmg → newPen = pen * (newDmg/dmg)
        float dmgRatio = Mathf.Clamp01((float)resultDinfo.Amount / (float)originalDinfo.Amount);
        state.remainingPen = originalDinfo.ArmorPenetrationInt * dmgRatio;

        // ── Try overpenetration ──
        TryOverpenetrate(bullet, pawn, state);

        // Clear CurrentBullet so secondary/extra damage TakeDamage calls don't re-trigger
        CurrentBullet = null;
    }

    /// <summary>
    /// Check if the bullet should pass through the target and, if so, perform overpenetration.
    /// </summary>
    private static void TryOverpenetrate(ProjectileCE bullet, Thing hitThing, OverpenState state)
    {
        if (hitThing == null) return;

        var ext = GetExtension(bullet.def);
        if (ext == null || !ext.enabled) return;

        // Max targets cap
        if (ext.maxTargets > 0 && state.overpenCount >= ext.maxTargets) return;

        // Full-fillage buildings stop bullets
        if (hitThing is Building && hitThing.def.Fillage == FillCategory.Full) return;

        // Deflected or sharp→blunt conversion → no overpenetration
        if (state.armorDeflected || state.sharpWasDeflected) return;

        // No remaining penetration → stopped
        if (state.remainingPen <= 0f) return;

        float currentPen = bullet.PenetrationAmount;
        if (currentPen <= 0f) return;

        // ─── Calculate speed retention ───
        float armorConsumed = currentPen - state.remainingPen;
        float armorRatio = Mathf.Clamp01(armorConsumed / currentPen);

        float effectiveBodySize = 1f;
        if (hitThing is Pawn pawn)
        {
            effectiveBodySize = Mathf.Min(pawn.BodySize, 4f);
        }

        float dragFactor = ext.dragFactor > 0f ? ext.dragFactor : 0.15f;
        float speedRetention = Mathf.Pow(1f - dragFactor * armorRatio, effectiveBodySize);

        // ─── Perform overpenetration ───
        state.alreadyHit.Add(hitThing);
        state.overpenCount++;
        state.kineticMult *= speedRetention;
        state.skipBaseImpact = true;

        // Restore flying state — bullet was set to landed=true in TryCollideWith
        var landedField = AccessTools.Field(typeof(ProjectileCE), "landed");
        if (landedField != null) landedField.SetValue(bullet, false);

        // Move projectile past target bounds to avoid re-collision
        var exactPosProp = AccessTools.Property(typeof(ProjectileCE), "ExactPosition");
        var lastPosField = AccessTools.Field(typeof(ProjectileCE), "LastPos");

        Vector3 exactPos = (Vector3)exactPosProp.GetValue(bullet, null);
        Vector3 lastPos = (Vector3)lastPosField.GetValue(bullet);

        Vector3 dir = exactPos - lastPos;
        if (dir.sqrMagnitude < 0.0001f)
        {
            // Fallback: use shot rotation/angle
            float shotAngle = (float)AccessTools.Field(typeof(ProjectileCE), "shotAngle").GetValue(bullet);
            float shotRotation = (float)AccessTools.Field(typeof(ProjectileCE), "shotRotation").GetValue(bullet);
            dir = new Vector3(
                Mathf.Cos(shotRotation) * Mathf.Cos(shotAngle),
                Mathf.Sin(shotAngle),
                Mathf.Sin(shotRotation) * Mathf.Cos(shotAngle));
        }
        dir.Normalize();

        // Get target bounds for skip distance
        var bounds = CE_Utility.GetBoundsFor(hitThing);
        float skipDist = bounds.size.magnitude * 0.6f + 0.5f;

        // Update positions: LastPos = collision point, ExactPosition = past target
        lastPosField.SetValue(bullet, exactPos);
        Vector3 newPos = exactPos + dir * skipDist;
        exactPosProp.SetValue(bullet, newPos, null);

        // Clear cached damage so next access recalculates with new kineticMult
        var damageAmountField = AccessTools.Field(typeof(ProjectileCE), "damageAmount");
        if (damageAmountField != null) damageAmountField.SetValue(bullet, null);

        if (Controller.settings.DebugDrawInterceptChecks)
        {
            MoteMakerCE.ThrowText(hitThing.Position.ToVector3Shifted(), bullet.Map, ">>", Color.cyan);
        }
    }

    /// <summary>
    /// Apply kinetic multiplier to a damage/penetration value.
    /// Called from DamageAmount and PenetrationAmount Postfix patches.
    /// </summary>
    public static float ApplyKineticMult(ProjectileCE proj, float value)
    {
        var state = GetState(proj);
        if (state == null) return value;
        return value * state.kineticMult;
    }

    /// <summary>
    /// Check if a thing was already hit by this projectile.
    /// Called from CanCollideWith Postfix.
    /// </summary>
    public static bool WasAlreadyHit(ProjectileCE proj, Thing thing)
    {
        var state = GetState(proj);
        if (state == null) return false;
        return state.alreadyHit.Contains(thing);
    }

    /// <summary>
    /// Check if the projectile is currently in overpenetration flight.
    /// Called from ImpactSomething Prefix.
    /// </summary>
    public static bool IsOverpenetrating(ProjectileCE proj)
    {
        var state = GetState(proj);
        return state != null && state.overpenCount > 0;
    }

    // ─── Breaching AI LoS fix ───

    /// <summary>
    /// CE's CanHitTargetFrom does strict 3D LoS checks that fail for breaching targets
    /// (walls, mountain rocks) — you can't have LoS to a wall you're trying to breach
    /// because the wall itself (or adjacent walls) block the ray.
    /// This relaxes LoS for Building targets with Fillage=Full or mineable,
    /// matching vanilla behavior where breaching weapons can target walls without LoS.
    /// </summary>
    public static bool ShouldAllowHitWithoutLoS(Thing target)
    {
        if (target is Building building)
        {
            // Walls, mountain rocks, and other full-fillage structures
            if (building.def.Fillage == FillCategory.Full)
                return true;
            // Mineable rocks (Thing_Granite etc.)
            if (building.def.mineable)
                return true;
        }
        return false;
    }
}
