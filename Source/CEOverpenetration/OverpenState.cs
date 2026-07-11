using System.Collections.Generic;

namespace CEOverpenetration;

public sealed class OverpenState
{
    public int overpenCount;
    public readonly HashSet<int> alreadyHitThingIds = new();
    public bool skipBaseImpact;
    public bool continuationActive;
}
