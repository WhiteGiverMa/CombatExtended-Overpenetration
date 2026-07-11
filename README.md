# CE Overpenetration

Combat Extended 子 mod — 子弹穿过敌人继续飞行。

## 功能

### 1. 过穿透 (Overpenetration)

所有普通 ballistic 锐伤子弹在穿透 Pawn 的护甲后都会自动判断是否继续飞行，不需要逐弹种配置。伤害和穿深由 CE 根据穿透后的实际速度继续计算。

**行为模型：**
- 仅处理 `BulletCE + BallisticsTrajectoryWorker + Sharp` 的 Pawn 命中。
- 完全偏转、护盾吸收、锐伤转钝伤、爆炸弹和 overhead 弹丸不会过穿。
- 目标护甲吸收比例和 BodySize 决定速度损失。
- 穿透后原弹丸从命中点继续正常 Tick，不传送，因此 CE/VEF 护盾和 BlockerRegistry 仍能拦截。
- 后续穿深、伤害、空气阻力和重力由 CE 原生速度模型接管。
- 命中历史和链式穿透计数会随存档保存。

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
