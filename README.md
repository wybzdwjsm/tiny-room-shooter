# 守灯人 · Tiny Room Shooter

一款单房间、快节奏的 2D Roguelite 射击游戏，用 Unity / 团结引擎制作，可直接在浏览器游玩。

世界被潮水淹了，你是最后一个守灯人。守过四个夜晚，或者一直守下去。

- **试玩 / 下载**：https://github.com/wybzdwjsm/tiny-room-shooter/releases/tag/v2.9 → `tiny-room-shooter-v2.9-webgl.zip`
- **源码**：https://github.com/wybzdwjsm/tiny-room-shooter
- **当前版本**：v2.9（标准模式 4 夜通关 / 无尽模式 / 22 种 Boss 攻击）

---

## 一、玩法

### 两种模式

| 模式 | 结构 | 终点 |
| --- | --- | --- |
| 标准 | 4 夜，每夜 2~4 波，第 4 夜是 Boss | 打赢最终 Boss 通关 |
| 无尽 | 一波接一波，每 5 波一个 Boss | 没有终点，比拼撑到第几波 |

一条完整流程：

1. 你在房间中央（或某块平台上）出生，敌人从两侧墙后飞进来，落地后才开始行动；
2. 边跑边跳边射，清完这一波；
3. 屏幕下方弹出三张词条，选一张（鼠标点或 `1`/`2`/`3`）；
4. 下一波平台布局重新随机，出生点也换位置；
5. 标准模式第 4 夜换成 Boss；无尽模式每 5 波一个，血量逐个递增；
6. 通关或阵亡后，结算页给出命中率、挨打次数、最后一击来自谁、是否超过历史最远波数。

### 核心设计

- **站位是决策**：悬浮平台不只是落脚点，还能挡子弹。躲到平台后，射手和炮台就打不到你——但你也失去了射击角度。
- **敌人靠行为分化，不靠堆血**：冲锋怪贴地撞人逼你跳，炮台定点三连散射逼你换位，射手保持距离逼你走位，精英怪又肉又慢。
- **词条有取舍**：20+ 词条涵盖高射速小弹丸、低射速大伤害、体质流等方向，部分词条带代价（换血量、换移速等）。
- **Boss 有 4 种、共 22 种攻击**：每种 Boss 有自己的攻击池轮转出招，半血狂暴后节奏更快。
- **失败要能解释**：结算页不写"你死了"，而是写清楚命中率、挨了几下、最后一击来自谁。

### 操作

| 操作 | 按键 |
| --- | --- |
| 移动 | `A` / `D` |
| 跳跃（按住跳更高，松手截断） | `Space` |
| 射击 | `J` 或鼠标左键 |
| 选卡 | 鼠标点击 / `1` `2` `3` |
| 暂停 / 设置 | `Esc` |
| 静音 | `M` |
| 标题页开局 | `空格`（标准）/ `E`（无尽） |
| 调试：跳波 / 重编译 | `Ctrl` + `F9` / `Ctrl` + `R` |

---

## 二、Boss 与攻击系统

4 种 Boss，各有专属外观与攻击池。攻击按池轮转，一场战斗能看到该 Boss 的绝大部分招式。

| Boss | 外观 | 招式（7~9 种） |
| --- | --- | --- |
| 潮眼 TideEye | 粉红眼球 + 触手 + 尖角 | 扇形、冲撞、全屏弹幕墙（留大缝）、螺旋、追踪水弹、散弹爆发、泪雨 |
| 孢潮 Spore | 紫色水母 + 孢子 | 环形弹幕、无敌酸雨、孢子云、交叉散射、双螺旋、地雷孢子、毒雾扩散 |
| 裂鳍 Spear | 青色海兽 + 尖鳍 | 大激光、冲撞、扇形、五连点射、回头弹、疾风三连冲、十字激光 |
| 深渊 Abyss | 深红魔眼 + 火焰触手 | 双扇形、弹幕墙、激光、酸雨、冲撞、螺旋环、陨石雨、追踪群、全屏十字 |

几个代表性大招：

- **全屏弹幕墙**：横贯全场的弹幕，开 3 格宽的大缺口，缺口位置在玩家侧但需要走位。
- **无敌酸雨**：Boss 变紫且打不动。全屏 18 列密集落雨，只有一个约 1.6 单位宽的安全点（绿色光柱提前 0.8 秒标记），每波安全点横跨屏幕轮换，逼玩家持续移动。
- **长蓄力大激光**：锁定玩家当前高度，闪烁蓄力 1.1~1.5 秒后，一整排密集子弹横扫，留 3 个缺口。
- **Boss 战高台**：不铺满平台，只生成 2~3 块高处小平台（宽 1.4~2.0，高 2.9~3.2），下层空旷便于走位。

### 实现架构

攻击系统从"每招一个状态 + 一个 Update 方法"重构成**协程 + 发射原语**：

- 状态机只保留 6 个状态：`Enter / Idle / Telegraph / Attacking / Recover / Dead`；
- `RunAttack` 协程按攻击类型分发到 22 个 `A_Xxx` 协程方法；
- 复用 4 个发射原语：`EmitFan`（扇形）、`EmitRing`（环形）、`EmitSkyRain`（天降）、`Shoot`/`ShootFrom`（单发）。

这套结构解决了三个问题：

1. `attackSerial` 攻击序号在重开 / Boss 死亡时递增，旧协程靠 `if (serial != attackSerial) yield break` 自动失效——Boss 死后不会还在发弹；
2. `WaitGameplay(seconds, serial)` 只在游戏真正进行时推进计时，暂停 / 进场冻结时攻击不会偷跑；
3. `FinishAttack` 统一收招回 `Recover` 并恢复 Boss 颜色，酸雨 / 激光的变色不会残留。

---

## 三、技术实现

### 全部资源运行时生成，不依赖外部美术 / 音频文件

角色、Boss、背景、粒子、音效、BGM 都在代码里生成，因此 WebGL 包仅约 4.5 MB，加载快且不会丢资源。

- **角色贴图**：`ArtStore` 从 `Resources/Art` 加载角色图；Boss 的 4 张程序化贴图由 `ArtStore.cs` 中内联的 `BossArt` 类用 `Texture2D.SetPixels32` 逐像素绘制——配色、径向渐变的身体、触手 / 尖鳍 / 火焰、白色眼白 + 彩色虹膜 + 瞳孔 + 高光，按类型缓存只生成一次。
- **背景**：`Backdrop` 生成渐变，`Ambience` 叠加氛围层。
- **音乐 / 音效**：`MusicSynth` 合成循环 BGM，`GameAudio` 合成射击、命中、受伤、胜利等音效。

### 架构分层

- **视觉与物理彻底分离**：`EnemyVisual` / `PlayerVisual` 负责呼吸、蓄力膨胀、冲刺前倾等动效，只改视觉层缩放 / 颜色，不碰碰撞体；`Boss.ApplyVisualScale` 同理。形变不会造成判定错位。
- **确定性随机**：地图、怪物、词条、Boss 酸雨落点全部由 `RunManager` 的单个 `System.Random` 驱动。seed 固定则整局可完整复现。
- **无 timeScale 暂停**：全项目 `Time.timeScale` 恒为 1。暂停与进场冻结靠各系统自己的门控（`PauseMenu.IsPaused`、`RunManager.IsSpawning`），避免与编辑器暂停态冲突。四道保险（选卡超时自动选、进场硬上限、敌人越界拉回、敌人存活超时自毁）保证任何情况下都不会被锁死。
- **打击反馈**：`HitFeedback` 震屏（强度可调：关 / 弱 / 标准 / 强）、`HitParticles` 命中粒子、`FxRings` 落地 / 爆发 / 残影光环、`EnemyVisual` 开火前发亮预警、受伤红闪与低血量脉动。特效统一限流，避免拖垮帧率。
- **调试工具**：`Ctrl+F9` 直接跳到下一波；`Tools → 诊断` 菜单可列出 / 清理场景里的缺失脚本；`JumpCurveRecorder` 按 `K` 导出跳跃曲线 CSV 用于调参。

### 关键文件

| 文件 | 职责 |
| --- | --- |
| `RunManager.cs` | 关卡流程、波次 / 章节、全局随机源、平台生成、Boss 调度、四道死锁保险 |
| `Boss.cs` | Boss 状态机 + 22 招协程 + 发射原语 + 狂暴 / 接触伤害 |
| `Enemy.cs` / `Weapon.cs` / `Bullet.cs` / `EnemyBullet.cs` | 敌人行为、玩家射击、双方子弹 |
| `PlayerMotor.cs` / `PlayerHealth.cs` / `PlayerStats.cs` | 移动跳跃、血量 / 无敌帧、玩家参数资产 |
| `Modifier.cs` / `Story.cs` / `RunStats.cs` | 20+ 词条、剧情文本、结算统计 |
| `GameAudio.cs` / `MusicSynth.cs` / `Ambience.cs` | 程序化音效、BGM、氛围 |
| `ArtStore.cs` / `Backdrop.cs` / `FxRings.cs` / `HitParticles.cs` | 贴图加载、程序化背景、视觉特效 |
| `Solid.cs` | 平台 / 墙的碰撞与挡子弹 |
| `PauseMenu.cs` / `TuningPanel.cs` / `GameSettings.cs` | 暂停设置、调参面板、全局设置 |
| `Assets/Editor/BuildHelper.cs` | WebGL 打包菜单 |

---

## 四、运行与打包

### 编辑器运行

用 Unity 6.3 LTS 或团结引擎打开项目，打开 `Assets/Scenes/StarterScene.unity`，按 Play。代码用旧 Input Manager，若报输入异常需把 Active Input Handling 设为 Both。

### 打包 WebGL

菜单 `Tools → Build WebGL (for itch.io)`，输出到 `Builds/WebGL`。等编译结束（右下角转圈停）再点打包；`BuildHelper` 会先检查 `EditorApplication.isCompiling`，避免报无信息的 `Unknown` 错误。打包会自动开启 `decompressionFallback`，让包在 itch.io 上不依赖服务器配置。

### 本地预览

```bash
node serve-webgl.js "Builds/WebGL" 8788
```

浏览器打开 `http://localhost:8788`。不要用 `file://` 直接打开 `index.html`——浏览器会把 wasm 当普通二进制，页面黑屏。

### 发布

把 `Builds/WebGL` 打成 zip（**`index.html` 必须在压缩包根目录**，不能多一层文件夹），传 itch.io 并勾选 "This file will be played in the browser"，Embed 尺寸 1280×720、开全屏按钮。

### 项目结构

```
Assets/
  Scenes/            场景（Starter / W2 / W3 / W4）
  Scripts/           游戏逻辑（含内联的 BossArt 程序化贴图）
  Resources/Art/     角色贴图
  Data/              参数资产（PlayerStats 等）
  Editor/            构建与诊断菜单
Docs/                发布文档、技术复盘、开发日志
Builds/WebGL/        WebGL 产物（被 gitignore）
```

发布字段文案见 `Docs/ITCH-PAGE.md`，发布前检查清单见 `Docs/RELEASE-CHECKLIST.md`，完整开发过程记录见 `Docs/DEVLOG.md`。
