# CE Overpenetration

Combat Extended 子 mod — 子弹穿过敌人继续飞行 + 破墙 AI LoS 修复。

## 功能

### 1. 过穿透 (Overpenetration)

Ballistic 子弹穿透目标的护甲后如果还有剩余穿深，会穿过目标继续飞行，伤害和穿深按速度衰减。

**数值模型：**
```
speedRetention = Pow(1 - dragFactor * armorRatio, min(bodySize, 4))
```
- `armorRatio` = 被目标护甲消耗的穿深比例
- `bodySize` = 目标体型（人类=1，大象=4，cap=4）
- 穿过 bodySize=4 的大象 ≈ 穿过 4 个 bodySize=1 的人类
- 穿透后直接缩放 CE projectile 的 `velocity/shotSpeed`
- 穿深/伤害、空气阻力和重力由 CE 原生 `RemainingSpeedPct/RemainingKineticEnergyPct` 后续接管

**配置** — 在子弹 ThingDef 上加 ModExtension：
```xml
<modExtensions>
  <li Class="CEOverpenetration.OverpenetrationExtension">
    <enabled>true</enabled>
    <dragFactor>0.15</dragFactor>
    <maxTargets>3</maxTargets>
  </li>
</modExtensions>
```

### 2. 破墙 AI LoS 修复

修复 CE 严格 3D LoS 检查导致原版 `JobGiver_AIBreaching` 无限循环报错（"started 10 jobs in one tick"）。

CE 的射线检查对墙/山体岩石过于严格，但破墙的本质就是「隔着障碍物打障碍物」——LoS 容易失败。本 mod 对 `Building`（`Fillage=Full` 或 `mineable`）在 `CanHitTargetFrom` 和 `TryFindCEShootLineFromTo` 两层放宽 LoS，保留射程检查。

同时，本 mod 对原版 `JobGiver_AIBreaching` 加了同 tick 重发熔断：同一个 pawn 对同一个山体/墙体如果立刻重复拿到同一个 `UseVerbOnThing`，会短暂等待，避免 `started 10 jobs in one tick` 红字雪崩。

对已经由 `JobGiver_AIBreaching` 找到 firing position 的破墙 job，`GotoCastPosition` 会直接使用 `targetB`，避免二次调用 `CastPositionFinder.TryFindCastPosition` 触发其他 mod 对 `VerbProperties.AdjustedRange` 的异常。

## 不修改 CE 源码

纯 Harmony patch + `ConditionalWeakTable` + `DefModExtension`，完全独立于 CE 源码。

## 依赖

- RimWorld 1.6
- Combat Extended
- Harmony

## 构建

```bash
cd Source/CEOverpenetration
dotnet build
```

dll 输出到 `Assemblies/CEOverpenetration.dll`。
