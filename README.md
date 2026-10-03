# Solar Flare Shield

RimWorld 1.6 Mod：通过研究和建造设施，屏蔽所在地图的太阳耀斑断电效果。

## 实现

- 太阳耀斑事件和游戏条件照常创建、显示并结束。
- Harmony 对 `GameConditionManager.ElectricityDisabled(Map)` 做 Postfix。
- 建筑平时保持待机并消耗 50W；检测到本地图太阳耀斑后尝试切换为 15000W 运行。
- 只有电网能提供运行所需功率时才屏蔽断电；耀斑开始后再建造或恢复供电也会自动检测。
- 地图上建成至少一个 `SolarFlareShield` 时，将该地图的耀斑断电判定改为不生效；拆除设施后恢复原版行为。
- 带有涅瓦莲无线输电适配器；建成后可使用其无线供电开关。
- 建筑由 `SolarFlareShieldResearch` 研究解锁，使用独立的“太阳耀斑屏蔽”研究页签，前置微电子基础，需要高科技研究台。
- 建筑的体积、材料、造价、美观度、贴图和旋转动效按 RT Solar Flare Shield 的 1.6 Def 对齐。

## 安装

编译产物已经部署到：

`D:/Steam/steamapps/common/RimWorld/Mods/SolarFlareShield`

在 Mod 列表中确认 `Solar Flare Shield` 位于 Harmony 后面并启用。研究“太阳耀斑屏蔽”后建造“太阳耀斑屏蔽器”。

## 构建

项目使用 RimWorld 游戏 DLL 和 Harmony DLL 编译。源码工程文件位于 `SolarFlareShield/SolarFlareShield.csproj`；默认路径针对本机安装位置，需要换机器时覆盖 `RimWorldDir` 和 `HarmonyDll`。包标识保持不变，以便已有存档继续识别这个 Mod。
