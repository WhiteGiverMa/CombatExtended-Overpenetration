using Verse;
using HarmonyLib;

namespace CEOverpenetration;

public class CEOverpenetrationMod : Mod
{
    public CEOverpenetrationMod(ModContentPack content) : base(content)
    {
        var harmony = new Harmony("WhiteGiverMa.CEOverpenetration");
        harmony.PatchAll();
        Log.Message("[CE Overpenetration] Initialized");
    }
}
