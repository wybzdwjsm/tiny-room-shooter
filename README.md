# 守灯人 · Tiny Room Shooter

一款单房间、快节奏的 2D Roguelite 射击游戏，Unity（团结引擎）制作，可直接在浏览器游玩。

**一局 3~5 分钟**：守住房间、清完每波敌人、从三张词条里选一张强化自己，打过第 4 夜的 Boss 即通关。通关后解锁无尽模式，看你能撑到第几波。

- 试玩 / 下载：https://github.com/wybzdwjsm/tiny-room-shooter/releases/tag/v2.9 （下载 `tiny-room-shooter-v2.9-webgl.zip`）
- 源码：https://github.com/wybzdwjsm/tiny-room-shooter

---

## 玩法

| 模式 | 结构 | 终点 |
| --- | --- | --- |
| 标准 | 4 夜，每夜 2~4 波，第 4 夜是 Boss | 打赢 Boss 通关 |
| 无尽 | 一波接一波，每 5 波一个 Boss | 无终点，比拼波数 |

- 每波清场后从 **3 张词条**里选 1 张（20+ 词条，含带代价的取舍），build 在几分钟内明显成型。
- 平台每波重新随机生成，**悬浮平台能挡子弹**——站位本身是决策。
- 敌人靠行为分化：冲锋怪逼你跳、炮台逼你换位、射手逼你走位；4 种 Boss 共 **22 种攻击**。
- 通关/阵亡的结算页给出命中率、挨打次数、最后一击来源。

## 操作

| 操作 | 按键 |
| --- | --- |
| 移动 | `A` / `D` |
| 跳跃（按住跳更高） | `Space` |
| 射击 | `J` 或鼠标左键 |
| 选卡 | 鼠标点击 / `1` `2` `3` |
| 暂停 / 设置 | `Esc` |
| 静音 | `M` |
| 标题页开局 | `空格`（标准）/ `E`（无尽） |
| 调试：跳波 / 重编译 | `Ctrl` + `F9` / `Ctrl` + `R` |

---

## 技术实现

### 全部资源运行时生成，不依赖外部美术/音频文件

角色、Boss、背景、粒子、音效、BGM 都在代码里生成：

- **角色美术**：`ArtStore` 从 `Resources/Art` 加载贴图；Boss 有 4 张程序化贴图，由 `ArtStore.cs` 中内联的 `BossArt` 类用 `Texture2D.SetPixels32` 逐像素绘制（配色 + 渐变身体 + 触手/尖鳍/火焰 + 眼睛），按 Boss 类型缓存。
- **背景**：`Backdrop` 生成渐变；`Ambience` 叠加氛围层。
- **音乐/音效**：`MusicSynth` 合成循环 BGM，`GameAudio` 合成射击/命中/受伤等音效。
- 因此 WebGL 包很小（约 4.5 MB），加载快，也不会丢资源。

### 架构分层

- **视觉与物理分离**：`EnemyVisual` / `PlayerVisual` 负责呼吸、蓄力膨胀、冲刺前倾等动效，只改视觉层缩放/颜色，不碰碰撞体；`Boss.ApplyVisualScale` 同理。这样形变不会造成判定错位。
- **确定性随机**：所有随机（地图、怪物、词条、Boss 酸雨落点）统一由 `RunManager` 的单个 `System.Random` 驱动，seed 固定则整局可复现。
- **Boss 攻击系统**：统一状态机（6 个状态）+ 协程调度。`RunAttack` 分发到各招协程，复用 4 个发射原语（扇形 / 环形 / 天降 / 单发）。`attackSerial` 在重开/死亡时递增，让旧协程自动失效；`WaitGameplay` 让暂停和进场冻结时不推进攻击计时。4 种 Boss 各有专属攻击池，轮转出招（每种 7~9 招）。
- **无 timeScale 暂停**：全项目 `Time.timeScale` 恒为 1。暂停/进场冻结靠各系统自己的门控（`PauseMenu.IsPaused`、`RunManager.IsSpawning`），避免与编辑器暂停态冲突。四道保险（选卡超时、进场硬上限、敌人越界拉回、存活超时自毁）保证任何情况下都不会被锁死。
- **打击反馈**：`HitFeedback` 震屏（强度可调）、`HitParticles` 命中粒子、`FxRings` 落地/爆发/残影光环、`EnemyVisual` 开火前发亮预警、受伤红闪与低血量脉动。特效统一限流，避免拖垮帧率。

### 关键文件

| 文件 | 职责 |
| --- | --- |
| `RunManager.cs` | 关卡流程、波次/章节、随机源、平台生成、Boss 调度 |
| `Boss.cs` | Boss 状态机 + 22 招协程 + 发射原语（程序化贴图 `BossArt` 内联其中） |
| `Enemy.cs` / `Weapon.cs` / `Bullet.cs` | 敌人行为、玩家射击、子弹 |
| `PlayerMotor.cs` / `PlayerHealth.cs` | 移动跳跃、血量/无敌帧 |
| `GameAudio.cs` / `MusicSynth.cs` | 程序化音效与 BGM |
| `ArtStore.cs` / `Backdrop.cs` / `FxRings.cs` | 贴图加载、程序化背景、视觉特效 |
| `Modifier.cs` / `Story.cs` | 20+ 词条与剧情文本 |

---

## 运行与打包

- **编辑器**：用团结引擎 / Unity 打开项目，打开 `Assets/Scenes/StarterScene.unity`，按 Play。
- **打包 WebGL**：菜单 `Tools → Build WebGL (for itch.io)`，输出到 `Builds/WebGL`。
- **本地预览**：项目根目录运行 `node serve-webgl.js "Builds/WebGL" 8788`，浏览器打开 `http://localhost:8788`。不要用 `file://` 直接打开 `index.html`。
- **发布**：把 `Builds/WebGL` 打成 zip（`index.html` 在根目录），传 itch.io 并勾选 “This file will be played in the browser”。

项目结构：`Assets/Scripts`（逻辑）、`Assets/Scenes`（场景）、`Assets/Resources/Art`（贴图）、`Assets/Data`（参数资产）、`Assets/Editor`（构建/诊断菜单）。

开发过程记录见 `Docs/DEVLOG.md`。
