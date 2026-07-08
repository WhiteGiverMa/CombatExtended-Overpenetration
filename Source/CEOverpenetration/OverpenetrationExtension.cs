using Verse;

namespace CEOverpenetration;

/// <summary>
/// ModExtension on ammo/Projectile ThingDefs to enable overpenetration.
/// Add to a projectile def via XML:
/// <code>
/// &lt;modExtensions&gt;
///   &lt;li Class="CEOverpenetration.OverpenetrationExtension"&gt;
///     &lt;enabled&gt;true&lt;/enabled&gt;
///     &lt;dragFactor&gt;0.15&lt;/dragFactor&gt;
///     &lt;maxTargets&gt;3&lt;/maxTargets&gt;
///   &lt;/li&gt;
/// &lt;/modExtensions&gt;
/// </code>
/// </summary>
public class OverpenetrationExtension : DefModExtension
{
    public bool enabled = false;
    public float dragFactor = 0.15f;
    public int maxTargets = 3;
}
