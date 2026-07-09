# CE Overpenetration

Combat Extended 子 mod — 子弹穿过敌人继续飞行。

## 功能

### 1. 过穿透 (Overpenetration)

Ballistic 子弹穿透目标的护甲后如果还有剩余穿深，会穿过目标继续飞行，伤害和穿深按速度衰减。

**数值模型：**
```
speedRetention = Pow(1 - dragFactor * armorRatio, min(bodySize, 4))
```
- `remainingPen` = CE 护甲计算后剩余穿深，由 `resultDamage / originalDamage` 反推
- `armorConsumed` = `currentPen - remainingPen`
- `armorRatio` = `Clamp01(armorConsumed / currentPen)`，即被目标护甲消耗的穿深比例
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

## 职责拆分

- 破墙 AI / LoS / job 熔断兼容修复已拆到 `CEBreachingFix`。
- 瞄准时间、weapon handling、aiming accuracy 数值调谐已拆到 `CEEliteCombatTweaks`。

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
