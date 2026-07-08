using System.Collections.Generic;
using Verse;

namespace CEOverpenetration;

/// <summary>
/// Per-projectile overpenetration state, stored via ConditionalWeakTable.
/// Not serialized — on save/load, worst case is a re-hit on the same target (harmless).
/// </summary>
public class OverpenState
{
    public float kineticMult = 1f;
    public int overpenCount = 0;
    public HashSet<Thing> alreadyHit = new HashSet<Thing>();
    public bool skipBaseImpact = false;
    /// <summary>Armor result from the most recent GetAfterArmorDamage call for this bullet.</summary>
    public float remainingPen;
    public bool armorDeflected;
    public bool sharpWasDeflected;
}
