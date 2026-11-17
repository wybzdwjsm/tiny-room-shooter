# 守灯人 · Tiny Room Shooter

Unity / 团结引擎制作的 2D Roguelite 射击游戏，可浏览器游玩。一局 3~5 分钟：4 夜通关或无尽刷波，波间三选一词条，4 种 Boss 共 22 种攻击。

- 源码：https://github.com/wybzdwjsm/tiny-room-shooter
- WebGL 可玩包：https://github.com/wybzdwjsm/tiny-room-shooter/releases/tag/v2.9
- 版本：v2.9

---

## 一、技术要点速览

| 领域 | 做法 | 解决的问题 |
| --- | --- | --- |
| 跳跃手感 | 非对称重力（跳高 3.4 / 下落重力 ×1.15）+ 土狼时间 0.12s + 跳跃缓冲 0.12s + 松键截断 0.8 | 解决"月球飘"、离台按跳无响应、落地前按键被吞 |
| 输入与物理 | 输入在 `Update`，施力在 `FixedUpdate`，全部用 deltaTime | 高刷屏与低帧率下手感一致 |
| Roguelite | `RunStats` 用 **struct**；`Modifier` 数据驱动（id/target/op/value/maxStacks），一个 switch 写回 | 重开不污染数值；加词条零改战斗代码 |
| 可复现随机 | 全局单一 `System.Random(seed)`，绝不混用 `UnityEngine.Random` | 同 seed 的地图/怪物/词条完全一致 |
| 对象池 | `Weapon` 预创建 48 发子弹池，命中只 `SetActive(false)` | 连射高峰零 GC Alloc |
| Boss 攻击 | 6 状态状态机 + 22 个协程 + 4 个发射原语 | 加招不改状态机；重开时旧协程靠 `attackSerial` 自动失效 |
| 暂停 | 全项目 `Time.timeScale` 恒为 1，各系统自己门控 | 避免与编辑器暂停态冲突；四道保险杜绝锁死 |
| 打击反馈 | Hit/Kill/Hurt 三级预算 + 0.05s 窗口取 Max + 二次衰减 + 玩家可调强度；不用顿帧 | 高频命中不再淹没低频击杀的反馈 |
| 美术/音频 | 全部运行时生成（逐像素贴图、渐变背景、合成 BGM/音效） | WebGL 包仅 4.5 MB，不依赖外部资源 |

---

## 二、核心系统设计

### 1. 跳跃：反推参数而非手调重力

非对称重力：上升与下落用不同重力，下落段更快，得到干脆的落地节奏（不物理真实，但手感更好）。`gravityScale = 0`，自己每步积分，不依赖引擎默认重力。跳高 3.4、下落重力倍率 1.15 等参数放在 `PlayerStats.asset` 里，可在 Inspector 调。

三个跟手机制：土狼时间 0.12s（离开平台后仍可跳）、跳跃缓冲 0.12s（落地前的按键先记下）、松键截断 0.8（松开 Space 时竖直速度乘 0.8 实现可变跳高）。计时与意图在 `Update` 采集、速度在 `FixedUpdate` 施加；土狼计时消费后必须清零，否则会意外二段跳。

落地检测 `Physics2D.OverlapBoxAll` 并排除玩家自身 collider。`JumpCurveRecorder` 按 `K` 导出 `jump_curve_*.csv`（time, height）用于画曲线调参。

### 2. 词条与数值：数据驱动 + 值类型

`RunStats` 是 **struct** 而非 class——C# class 赋值是引用拷贝，用值类型才能保证 `stats = RunStats.Base()` 重开时不串味。`Modifier` 只含 `id / displayName / target / op / value / maxStacks`，`Apply` 一个 switch 把效果写回 `RunStats`，加新词条 = 加一条数据、战斗代码零改动。`maxStacks` + 射速上限 30 + 子弹数上限 5 防止乘法叠加爆炸。

20+ 词条涵盖高射速小弹丸、低射速大伤害、体质流，部分带代价。结算页显示 seed 并支持重玩本局。

### 3. 确定性随机

地图（2~4 块平台位置/宽度）、敌人（位置抖动、类型权重、首发延迟）、词条抽取、Boss 酸雨落点全部消费**同一个** `System.Random(seed)`。早期用 `Random.Range`（引擎全局随机）导致同 seed 结果不同，改单一实例后复现。**绝不混用** `UnityEngine.Random`。

### 4. Boss 攻击：协程 + 发射原语

从"每招一个 State + 一个 Update"重构成：状态机只留 `Enter / Idle / Telegraph / Attacking / Recover / Dead`，`RunAttack` 分发到 22 个 `A_Xxx` 协程，复用 `EmitFan`（扇形）、`EmitRing`（环形）、`EmitSkyRain`（天降）、`Shoot`（单发）四个原语。

- `attackSerial` 在重开/死亡时递增，旧协程 `if (serial != attackSerial) yield break` 自动失效——Boss 死后不会继续发弹；
- `WaitGameplay(seconds, serial)` 只在游戏进行中推进，暂停/进场冻结时攻击不偷跑；
- `FinishAttack` 统一收招回 `Recover` 并恢复颜色，酸雨/激光的变色不残留。

4 种 Boss 各有攻击池轮转（7/7/7/9 招），半血狂暴后节奏更快。代表招式：全屏弹幕墙留大缝、无敌酸雨（18 列落雨仅 1 个绿光安全点且逐波轮换）、长蓄力激光锁高度、陨石雨、螺旋环、追踪群。

### 5. 暂停：不用 timeScale

全项目 `Time.timeScale` 恒为 1。暂停与进场冻结靠各系统自己的门控（`PauseMenu.IsPaused`、`RunManager.IsSpawning`）。这不是风格选择——`timeScale = 0` 会让编辑器认为进入暂停态，Game 视图失焦、暂停键亮起，且 Update 仍在跑导致各类计时错乱。四道保险保证不锁死：选卡 12 秒自动选第一项、进场超过硬上限强制恢复、敌人越界拉回场内、敌人存活超 30 秒且够不着时自毁并计入击杀。

### 6. 打击反馈：按事件频率分配预算

初版把震屏和顿帧绑在**每次命中**上，6 发/秒时等于每秒十几次震动叠加卡顿。诊断是"分配错了"而非参数太大：强反馈给了高频事件。改成 Hit/Kill/Hurt 三档（震幅 0.020 / 0.055 / 0.085，时长 0.055 / 0.13 / 0.19 秒），连续命中在 0.05 秒窗口内取 `Max` 而非线性相加，幅度按 `(remaining/total)²` 二次衰减。

顿帧被**完全移除**——旧版用 `timeScale = 0.05` 做命中停顿，只要有一次没还原成功，全局时间就永久卡在 5% 速度，画面几乎静止且查不出来（因为不是 0）。现在只用震屏表达重量。整体再乘玩家可调的「关 / 弱 / 标准 / 强」。

可迁移结论：反馈强度应按**事件频率**分配预算，把强反馈留给低频高价值事件。

### 7. 程序化美术与音频

角色/Boss 贴图、渐变背景、粒子、音效、BGM 全部代码生成。Boss 的 4 张贴图由 `ArtStore.cs` 内联的 `BossArt` 类用 `Texture2D.SetPixels32` 逐像素绘制：径向渐变身体 + 触手/尖鳍/火焰 + 眼白/虹膜/瞳孔/高光，按类型缓存只生成一次。`MusicSynth` 合成循环 BGM，`GameAudio` 合成射击/命中/受伤音效。WebGL 包因此仅 4.5 MB。

### 8. 视觉与物理分层

`EnemyVisual` / `PlayerVisual` 负责呼吸、蓄力膨胀、冲刺前倾等动效，只改视觉层缩放/颜色，不碰碰撞体；`Boss.ApplyVisualScale` 同理。形变不会造成判定错位——角色缩到 0.62 倍后，"看着小、判定还大"是靠这套分层避免的。

---

## 三、代码结构

```
Assets/
  Scripts/
    RunManager.cs       关卡流程/波次章节/随机源/平台生成/Boss调度/死锁保险
    Boss.cs             Boss状态机 + 22招协程 + 发射原语
    Enemy.cs            Charger/Turret/Shooter 行为 + 卡住自愈
    PlayerMotor.cs      非对称重力/土狼时间/跳跃缓冲/落地检测
    PlayerHealth.cs     血量/无敌帧
    Weapon.cs Bullet.cs EnemyBullet.cs   射击与双方子弹（玩家侧对象池）
    Modifier.cs RunStats.cs Story.cs     词条/数值/剧情
    GameAudio.cs MusicSynth.cs Ambience.cs  程序化音频
    ArtStore.cs Backdrop.cs FxRings.cs HitParticles.cs  程序化美术与特效
    HitFeedback.cs      三级震屏预算
    Solid.cs            平台/墙碰撞与挡子弹
    PauseMenu.cs TuningPanel.cs GameSettings.cs  暂停/调参/设置
    JumpCurveRecorder.cs  跳跃曲线导出
  Scenes/               Starter / W2 / W3 / W4
  Resources/Art/        角色贴图
  Data/                 参数资产
  Editor/               BuildHelper（WebGL打包）、SceneHealth（缺失脚本诊断）
Docs/                   ITCH-PAGE / RELEASE-CHECKLIST / POSTMORTEM / PLAYTEST-NOTES / DEVLOG
```

---

## 四、运行

- **编辑器**：Unity 6.3 LTS 或团结引擎打开 `Assets/Scenes/StarterScene.unity`，Play。用旧 Input Manager，若输入异常把 Active Input Handling 设为 Both。
- **WebGL 打包**：菜单 `Tools → Build WebGL (for itch.io)` → `Builds/WebGL`。编译结束后再点，`BuildHelper` 会检查 `EditorApplication.isCompiling` 并提示。
- **本地预览**：`node serve-webgl.js "Builds/WebGL" 8788` → `http://localhost:8788`。不要用 `file://`（wasm 会被当普通二进制导致黑屏）。

### 操作

`A`/`D` 移动 · `Space` 跳（按住更高）· `J`/鼠标左键 射击 · `1`/`2`/`3` 选卡 · `Esc` 暂停 · `M` 静音 · `Ctrl+F9` 跳波

---

详细的技术复盘（含失败案例与取舍）见 `Docs/POSTMORTEM.md`，完整开发过程见 `Docs/DEVLOG.md`。
