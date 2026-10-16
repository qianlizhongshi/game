# 寻路 · Unity 追逐小游戏

基于 **Unity + C#** 的限时逃生小游戏。怪物使用**有限状态机 + NavMesh 寻路**追击玩家，
玩家在 **120 秒**内边走位边累计分数，被抓或时间耗尽即结算。

## 玩法

- **WASD** 移动
- 被怪物追击时**每秒 +1 分**（两只同时追就是每秒 +2 分），坚持越久分越高
- **120 秒**倒计时结束，或**被怪物抓住** → 游戏结束
- 结算界面显示本局分数、名次，以及排行榜前 5 名
- 点击**「重新开始」**按钮再来一局

## 技术要点

| 模块 | 实现 |
| --- | --- |
| 怪物 AI | 有限状态机（巡逻 / 追击 / 攻击）三态自动切换 |
| 寻路 | `NavMeshAgent` 沿烘焙的导航网格移动；巡逻点先用 `NavMesh.SamplePosition` 吸附到最近可走位置，并带超时兜底，避免巡逻点掉出网格后永久卡死 |
| 视野判定 | 按 0.1 秒间隔向玩家打射线，被障碍物层遮挡即丢失目标 |
| 玩家移动 | `CharacterController` + 重力；方向向量用 `sqrMagnitude` 比较，省一次开方 |
| 性能优化 | 寻路与视线检测按间隔节流（不再每帧调用）；UI 用变更检测刷新，避免每帧拼字符串产生 GC |
| 排行榜 | 纯内存实现（容量 10 条），不落盘，重开游戏即清空 |
| UI | TextMeshPro，含实时分数 / 倒计时 / 结算弹窗 / 排行榜 |

## 环境

- **Unity 2021.3.45f2c1**（内置渲染管线、旧版 Input Manager）
- 依赖：TextMeshPro

## 运行

1. 用 Unity Hub 以 **2021.3.x** 打开本工程
2. 打开场景 `Assets/Scenes/SampleScene.unity`
3. 点击 **Play**

## 关于中文界面

场景默认使用**英文文案**（`TIME` / `SCORE` / `RANKING` 等，都可以在 `GameManager` 的 Inspector 里改）。

仓库内**不包含中文字体** —— 中文字体普遍有授权限制，不宜随公开仓库分发。如需中文界面：

1. 自行放入一个 **SIL OFL** 授权的中文字体（如思源黑体、Noto Sans SC 静态版）到
   `Assets/TextMesh Pro/Fonts/`
2. 运行菜单 **`Tools > 寻路 > 启用中文 UI`**

该菜单会自动生成 TMP 中文字体资源、把它接入 TMP 设置的回退字体列表，并把全部文案切成中文
（冠军 / 亚军 / 季军 / 排行榜 / 本局 / 时间 / 分数 / 名次 …）。

## 目录结构

```
Assets/
├─ Scenes/SampleScene.unity    主场景（含已烘焙的 NavMesh）
├─ GameManager.cs              游戏流程：倒计时、计分、结算、排行榜、UI 刷新
├─ PlayerController.cs         玩家 WASD 移动与重力
├─ MonsterFSM.cs               怪物状态机、寻路与视野检测
├─ Leaderboard.cs              纯内存排行榜（容量 10，前三名可显示冠 / 亚 / 季军）
└─ Editor/ChineseUISetup.cs    编辑器菜单：一键配置中文界面
```

## 可调参数

| 脚本 | 参数 |
| --- | --- |
| `GameManager` | 总时长 `totalGameTime` (120)、结算显示名次 `leaderboardEntriesToShow` (5)、各类文案 |
| `MonsterFSM` | 侦测距离 `detectDistance` (16)、攻击距离 `attackDistance` (2)、巡逻 / 追击速度 `patrolSpeed` (3.5) / `chaseSpeed` (7.5)、到点等待 `patrolWaitTime` (2) |
| `PlayerController` | 移动速度 `moveSpeed` (6)、重力 `gravity` (-9.81) |

> 改动场景里的数值后，若怪物寻路表现异常（例如贴墙穿插），请检查
> `Window > AI > Navigation` 里的烘焙代理半径是否与怪物模型尺寸匹配，必要时重新 Bake。
