using UnityEngine;

// Boss：单一大型敌人，循环「漂移 → 蓄力 → 出招 → 硬直」。
// 有 4 种变体（Kind），共用一套状态机，靠参数表区分外观 tint、体型、攻击模式和速度：
//   TideEye 潮眼      —— 原版：扇形弹幕 + 冲撞
//   Spore   孢潮      —— 环形弹幕（360°）+ 慢速大范围漂移
//   Spear   裂鳍      —— 高频冲撞 + 窄扇形，快而凶
//   Abyss   深渊之瞳  —— 双扇形 + 冲撞，最终 Boss，狂暴更早
// 死亡不自己结束一局，而是回调 RunManager.BossDefeated()，保持"谁管流程"的边界。
public class Boss : MonoBehaviour
{
    public enum Kind { TideEye, Spore, Spear, Abyss }

    public static Boss Instance { get; private set; }

    // 每个变体的配置。tint 是乘到原画上的色调（保留插画细节，不是纯色覆盖）。
    private struct Spec
    {
        public string name;
        public Color tint;
        public float scale;
        public float driftMul;
        public float chargeMul;
        public float fanStepMul;     // 扇形角度间隔倍率（越小越密）
        public int fanCount;         // 单侧弹丸数（环形弹种另算）
        public bool ring;            // 是否用 360° 环形弹幕
        public bool doubleFan;       // 是否发射双层扇形
        public float enrageAt;       // 狂暴血量比例（越小越早狂暴）

        public Spec(string n, Color t, float s, float dm, float cm,
            float fsm, int fc, bool r, bool df, float ea)
        {
            name = n; tint = t; scale = s; driftMul = dm; chargeMul = cm;
            fanStepMul = fsm; fanCount = fc; ring = r; doubleFan = df; enrageAt = ea;
        }
    }

    private static readonly Spec[] Specs =
    {
        // TideEye：原版，扇形 + 冲撞
        new Spec("潮眼", new Color(1f, 0.95f, 0.95f), 1.15f, 1.0f, 1.0f, 1.0f, 3, false, false, 0.5f),
        // Spore：紫色，环形弹幕，慢漂移但冲撞范围大
        new Spec("孢潮", new Color(0.72f, 0.55f, 1f), 1.28f, 0.7f, 0.8f, 1.0f, 5, true,  false, 0.45f),
        // Spear：青色，小而快，窄扇 + 高频冲撞
        new Spec("裂鳍", new Color(0.45f, 1f, 0.92f), 1.02f, 1.5f, 1.4f, 0.65f, 2, false, false, 0.4f),
        // Abyss：深红，最大，双扇形 + 冲撞，最早狂暴
        new Spec("深渊之瞳", new Color(1f, 0.5f, 0.42f), 1.45f, 1.2f, 1.15f, 0.9f, 4, false, true, 0.55f),
    };

    private Spec spec { get { return Specs[(int)kind]; } }

    private Kind kind = Kind.TideEye;
    public Kind Type { get { return kind; } }
    public string TypeName { get { return spec.name; } }

    private enum State { Enter, Idle, Telegraph, Attacking, Recover, Dead }

    // 全部攻击方式。执行统一走 RunAttack 协程 + 下面的发射原语，
    // 不再为每一招单独加 State —— 否则 22 招就是 22 套 Update，维护会崩。
    private enum Attack
    {
        // 基础
        Fan, Charge, Rain, Laser, Wall,
        // 潮眼（水/眼）
        Spiral, Homing, Burst, TearRain,
        // 孢潮（孢子/毒）
        SporeCloud, Cross, TwinSpiral, Mine, PoisonMist,
        // 裂鳍（速度/激光）
        QuickShot, Backshot, Gale, CrossLaser,
        // 深渊（终极）
        RingSpiral, Meteor, SeekerSwarm, CrossFire,
    }

    // 每种 Boss 的攻击池（轮转使用）。每个至少 7 招。
    private static readonly Attack[][] AttackPools =
    {
        // TideEye 潮眼：水/眼 —— 扇形、冲撞、水幕、螺旋、追踪、散爆、泪雨
        new[] { Attack.Fan, Attack.Charge, Attack.Wall, Attack.Spiral,
                Attack.Homing, Attack.Burst, Attack.TearRain },
        // Spore 孢潮：孢子/毒 —— 环形、酸雨、孢子云、交叉、双螺旋、地雷、毒雾
        new[] { Attack.Fan, Attack.Rain, Attack.SporeCloud, Attack.Cross,
                Attack.TwinSpiral, Attack.Mine, Attack.PoisonMist },
        // Spear 裂鳍：速度/激光 —— 激光、冲撞、扇形、点射、回头弹、疾风、十字激光
        new[] { Attack.Laser, Attack.Charge, Attack.Fan, Attack.QuickShot,
                Attack.Backshot, Attack.Gale, Attack.CrossLaser },
        // Abyss 深渊之瞳：终极，全招式 —— 双扇、弹幕、激光、酸雨、冲撞、螺旋环、陨石、追踪群、十字
        new[] { Attack.Fan, Attack.Wall, Attack.Laser, Attack.Rain, Attack.Charge,
                Attack.RingSpiral, Attack.Meteor, Attack.SeekerSwarm, Attack.CrossFire },
    };

    [SerializeField] private float driftSpeed = 1.1f;
    [SerializeField] private float chargeSpeed = 9f;
    [SerializeField] private float contactRadiusX = 1.05f;
    [SerializeField] private float contactRadiusY = 1.3f;

    // 体型基准：明显大于其他单位；实际大小再乘变体的 scale
    private const float BossBaseScale = 1.9f;
    private const float BossEnterScale = 0.45f;
    private float BossScale { get { return BossBaseScale * spec.scale; } }

    // 有美术时用淡染色做蓄力/狂暴（整块纯色会把画好的 Boss 盖成色块）；
    // 没图时退回原来的纯色方块表现
    private static readonly Color BaseColor = new Color(0.95f, 0.32f, 0.52f, 1f);
    private static readonly Color TelegraphColor = new Color(1f, 0.86f, 0.35f, 1f);
    private static readonly Color EnragedColor = new Color(1f, 0.22f, 0.30f, 1f);

    private bool hasArt;
    private bool hasVariantArt;      // 是否加载到了专属贴图（而非原版 boss.png 兜底）
    private Color CurrentColor
    {
        get
        {
            Color tint = spec.tint;
            // 有专属贴图：用白色显示贴图原色，不再叠 tint（否则会把插画染成单色，
            // 用户会觉得"只是变色"）。狂暴时轻微偏红作为二阶段提示。
            if (hasVariantArt)
                return enraged ? new Color(1f, 0.78f, 0.78f) : Color.white;
            // 只有原版 boss.png 兜底：靠 tint 区分不同 Boss
            if (hasArt)
                return enraged ? new Color(tint.r, tint.g * 0.55f, tint.b * 0.55f) : tint;
            // 完全没图：纯色方块表现
            return enraged ? EnragedColor : Color.Lerp(BaseColor, tint, 0.5f);
        }
    }

    private int hp;
    private float damageCarry;    // 零散伤害累积（子弹 0.25，攒够 4 发扣 1 血）
    private int maxHp;
    private SpriteRenderer sr;
    private Collider2D col;

    private State state = State.Enter;
    private float stateTimer;
    private float stateDuration = 1f;      // 进入状态时的总时长，蓄力脉冲要用它算进度
    private float baseY;
    private float hoverSeed;
    private bool enraged;
    private Vector3 enterFrom;         // 入场起点 / 终点 / 时长（v1.4 曾误删，导致 CS0103）
    private Vector3 enterTo;
    private float enterDuration;
    // 视觉层参数（只影响 scale/颜色，不碰位置和碰撞开关）
    private float flinch;              // 受击/阶段转换的短暂抖动，0~1
    private float chargeTrailTimer;    // 冲撞残影节流
    private float telegraphRingTimer;  // 蓄力光环节流
    private int attackCursor;          // 当前攻击池轮转到的位置
    private Attack pendingAttack;      // 蓄力结束后要执行的招式
    private float laserY;              // 激光锁定的高度
    private bool invincible;           // 无敌（酸雨/毒雾阶段，打不动）
    private System.Collections.IEnumerator attackRoutine;   // 当前攻击协程
    private int attackSerial;          // 攻击序号，重开/死亡时让旧协程失效

    public int Hp { get { return hp; } }
    public int MaxHp { get { return maxHp; } }
    public bool Alive { get { return state != State.Dead; } }
    public bool Enraged { get { return enraged; } }

    // 攻击欲望：idle 和硬直都缩短，攻击更密集（数值仍留反应窗口）
    private float TelegraphTime { get { return enraged ? 0.30f : 0.42f; } }
    private float IdleTime { get { return enraged ? 0.45f : 0.70f; } }
    private float RecoverBurst { get { return enraged ? 0.22f : 0.34f; } }
    private float RecoverCharge { get { return enraged ? 0.32f : 0.50f; } }
    private float DriftSpeed { get { return driftSpeed * spec.driftMul * (enraged ? 1.4f : 1f); } }
    private float ChargeSpeed { get { return chargeSpeed * spec.chargeMul * (enraged ? 1.2f : 1f); } }

    public void Init(int hpValue, float duration, Vector3 from, Vector3 to, Kind bossKind = Kind.TideEye)
    {
        Instance = this;
        kind = bossKind;
        maxHp = hpValue;
        hp = maxHp;
        enterFrom = from;
        enterTo = to;
        enterDuration = Mathf.Max(0.2f, duration);
        baseY = to.y;
        hoverSeed = to.x * 1.7f;
        flinch = 0f;
        chargeTrailTimer = 0f;
        telegraphRingTimer = 0f;
        attackCursor = 0;
        invincible = false;

        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        // 有美术 = 贴图是 ArtStore 加载的 或 BossArt 程序化生成的（不是兜底方块）
        hasArt = sr != null && sr.sprite != null
            && (ArtStore.IsBossSprite(sr.sprite) || ArtStore.IsVariantBossSprite(sr.sprite));
        // 专属贴图（外部变体 或 程序化）：显示原色，不叠 tint
        hasVariantArt = sr != null && ArtStore.IsVariantBossSprite(sr.sprite);
        if (sr != null) sr.color = CurrentColor;

        state = State.Enter;
        stateTimer = enterDuration;
        stateDuration = enterDuration;

        transform.position = from;
        ApplyVisualScale(BossEnterScale);
        if (col != null) col.enabled = false;      // 入场时不可被打，也不撞人
    }

    private void Update()
    {
        if (!RunManager.HasInstance || !RunManager.IsPlaying) return;
        if (PauseMenu.IsPaused) return;
        if (RunManager.IsSpawning) return;
        if (!PlayerHealth.HasInstance || PlayerHealth.Instance.IsDead) return;
        if (state == State.Dead) return;

        stateTimer -= Time.deltaTime;
        flinch = Mathf.MoveTowards(flinch, 0f, Time.deltaTime * 5.5f);

        switch (state)
        {
            case State.Enter: UpdateEnter(); break;
            case State.Idle: UpdateIdle(); break;
            case State.Telegraph: UpdateTelegraph(); break;
            case State.Attacking: UpdateAttacking(); break;
            case State.Recover: UpdateRecover(); break;
        }
    }

    // 攻击期间 Boss 的行为由协程控制（发射时机/移动），这里只管视觉和收尾检测。
    // 协程跑完会把 state 切回 Recover；用 serial 防止重开后旧协程还在动。
    private void UpdateAttacking()
    {
        DriftSlower();                  // 攻击时缓慢漂移，不是定桩
        if (sr != null && !invincible) sr.color = CurrentColor;
    }

    // 所有 scale 赋值都走这里：把"基准缩放"和"呼吸/受击抖动"叠加后再写根节点。
    // Boss 的碰撞是手算半径（contactRadiusX/Y），不读 transform.localScale，
    // 所以在这里做视觉形变不会造成判定错位。
    private void ApplyVisualScale(float baseScale)
    {
        float breath = 1f + Mathf.Sin(Time.time * 2.1f + hoverSeed) * 0.035f;
        float shake = flinch > 0f ? Mathf.Sin(Time.time * 45f) * flinch * 0.06f : 0f;
        float s = baseScale * (breath + shake);
        transform.localScale = new Vector3(s, s, 1f);
    }

    // 受击抖动：外部（Hit 回调）调用，0.18 秒内衰减完
    public void Flinch()
    {
        flinch = 1f;
    }

    // 入场：从上方落下 + 放大，三次缓出
    private void UpdateEnter()
    {
        float k = Mathf.Clamp01(1f - stateTimer / enterDuration);
        float ease = 1f - Mathf.Pow(1f - k, 3f);
        transform.position = Vector3.Lerp(enterFrom, enterTo, ease);
        ApplyVisualScale(Mathf.Lerp(BossEnterScale, BossScale, ease));

        if (k < 1f) return;

        transform.position = enterTo;
        ApplyVisualScale(BossScale);
        if (col != null) col.enabled = true;
        HitParticles.Spawn(transform.position, CurrentColor, 10);
        EnterState(State.Idle, IdleTime);
    }

    private void UpdateIdle()
    {
        Drift();
        ApplyVisualScale(BossScale);       // 待机呼吸（Boss 是所有单位里最"沉"的呼吸）
        if (stateTimer <= 0f) EnterState(State.Telegraph, TelegraphTime);
    }

    // 蓄力：变黄 + 轻微放大，给玩家反应时间（二阶段会缩短到 0.34 秒）
    private void UpdateTelegraph()
    {
        float progress = 1f - Mathf.Clamp01(stateTimer / Mathf.Max(0.01f, stateDuration));

        if (sr != null)
        {
            bool blink = Mathf.Repeat(stateTimer * 12f, 1f) > 0.5f;
            // 有美术时只做淡黄闪烁，不用纯色盖掉原画
            sr.color = blink ? (hasArt ? new Color(1f, 0.86f, 0.55f) : TelegraphColor) : CurrentColor;
        }

        // 蓄力越接近尾声，脚下光环越亮越收紧——这是"倒计时"的第二条视觉线索，
        // 颜色闪烁之外多一层形变，余光也能读到
        if (progress > 0.35f && stateTimer > 0f)
        {
            telegraphRingTimer -= Time.deltaTime;
            if (telegraphRingTimer <= 0f)
            {
                telegraphRingTimer = 0.12f;
                FxRings.Land(transform.position, 0.3f + progress * 0.5f);
            }
        }

        float pulse = 1f + Mathf.Sin(progress * Mathf.PI) * 0.08f;
        ApplyVisualScale(BossScale * pulse);

        if (stateTimer > 0f) return;

        if (sr != null) sr.color = CurrentColor;
        ApplyVisualScale(BossScale);

        // 按该 Boss 的攻击池轮转选招（不再是简单的弹幕/冲撞交替）
        Attack[] pool = AttackPools[(int)kind];
        pendingAttack = pool[attackCursor % pool.Length];
        attackCursor++;

        ExecuteAttack(pendingAttack);
    }

    // 蓄力结束后启动对应攻击的协程。所有攻击共用一个 Attacking 状态。
    private void ExecuteAttack(Attack atk)
    {
        attackSerial++;
        invincible = false;
        attackRoutine = RunAttack(atk, attackSerial);
        StartCoroutine(attackRoutine);
        EnterState(State.Attacking, 99f);   // 时长由协程控制，这里给个大值兜底
    }

    // 等待若干秒，但只在"游戏真正进行中"才推进（暂停 / 进场时不推进，且能被重开打断）
    private System.Collections.IEnumerator WaitGameplay(float seconds, int serial)
    {
        float t = 0f;
        while (t < seconds)
        {
            if (serial != attackSerial || state == State.Dead) yield break;
            if (!PauseMenu.IsPaused && !RunManager.IsSpawning && RunManager.IsPlaying)
                t += Time.deltaTime;
            yield return null;
        }
    }

    // 一路等到 Recover：让协程在收招时统一交还控制权
    private System.Collections.IEnumerator FinishAttack(int serial, float recoverTime)
    {
        invincible = false;
        if (sr != null) sr.color = CurrentColor;
        if (serial != attackSerial || state == State.Dead) yield break;
        EnterState(State.Recover, recoverTime);
    }

    // ==================== 攻击总调度 ====================
    private System.Collections.IEnumerator RunAttack(Attack atk, int serial)
    {
        switch (atk)
        {
            case Attack.Fan:        yield return A_Fan(serial); break;
            case Attack.Charge:     yield return A_Charge(serial); break;
            case Attack.Wall:       yield return A_Wall(serial); break;
            case Attack.Rain:       yield return A_Rain(serial); break;
            case Attack.Laser:      yield return A_Laser(serial); break;
            case Attack.Spiral:     yield return A_Spiral(serial); break;
            case Attack.Homing:     yield return A_Homing(serial); break;
            case Attack.Burst:      yield return A_Burst(serial); break;
            case Attack.TearRain:   yield return A_TearRain(serial); break;
            case Attack.SporeCloud: yield return A_SporeCloud(serial); break;
            case Attack.Cross:      yield return A_Cross(serial); break;
            case Attack.TwinSpiral: yield return A_TwinSpiral(serial); break;
            case Attack.Mine:       yield return A_Mine(serial); break;
            case Attack.PoisonMist: yield return A_PoisonMist(serial); break;
            case Attack.QuickShot:  yield return A_QuickShot(serial); break;
            case Attack.Backshot:   yield return A_Backshot(serial); break;
            case Attack.Gale:       yield return A_Gale(serial); break;
            case Attack.CrossLaser: yield return A_CrossLaser(serial); break;
            case Attack.RingSpiral: yield return A_RingSpiral(serial); break;
            case Attack.Meteor:     yield return A_Meteor(serial); break;
            case Attack.SeekerSwarm: yield return A_SeekerSwarm(serial); break;
            case Attack.CrossFire:  yield return A_CrossFire(serial); break;
        }
    }

    // ==================== 发射原语 ====================
    private Vector2 AimDir() { return ((Vector2)PlayerHealth.Instance.transform.position - (Vector2)transform.position).normalized; }
    private Vector2 AimPoint() { return PlayerHealth.Instance.transform.position; }
    private void Shoot(Vector2 dir, string tag) { EnemyBullet.Spawn(transform.position, (Vector2)transform.position + dir * 3f, tag); }
    private void ShootFrom(Vector2 origin, Vector2 dir, string tag) { EnemyBullet.Spawn(origin, origin + dir * 3f, tag); }

    // 扇形：count 发，张角 spread 弧度，围绕瞄准方向
    private void EmitFan(int count, float spread, string tag)
    {
        Vector2 aim = AimDir();
        if (count <= 1) { Shoot(aim, tag); return; }
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.Lerp(-spread * 0.5f, spread * 0.5f, i / (float)(count - 1));
            Shoot(Rotate(aim, a), tag);
        }
    }

    // 环形：n 发 360 度，可带起始角度
    private void EmitRing(int n, float startAngle, string tag)
    {
        for (int i = 0; i < n; i++)
        {
            float a = startAngle + i * Mathf.PI * 2f / n;
            Shoot(new Vector2(Mathf.Cos(a), Mathf.Sin(a)), tag);
        }
    }

    // 从天上落下的一排弹幕（酸雨 / 陨石）
    private void EmitSkyRain(int count, string tag, bool clustered)
    {
        float limit = Mathf.Max(3f, RunManager.RoomRight - 1f);
        for (int i = 0; i < count; i++)
        {
            float x = clustered
                ? Mathf.Lerp(AimPoint().x - 3f, AimPoint().x + 3f, (float)rainRandom())
                : Mathf.Lerp(-limit, limit, (float)rainRandom());
            x = Mathf.Clamp(x, -limit, limit);
            Vector2 from = new Vector2(x, 7f);
            ShootFrom(from, Vector2.down, tag);
        }
    }

    private float rainRandom()
    {
        rainSeed = rainSeed * 0.6180339887f + 0.31415926f;
        rainSeed -= Mathf.Floor(rainSeed);
        return rainSeed;
    }
    private float rainSeed = 0.37f;

    // ==================== 22 招实现 ====================

    // 基础扇形（深渊会自动升级为双扇，见 FireFan 复用）
    private System.Collections.IEnumerator A_Fan(int s)
    {
        FireFan();
        yield return WaitGameplay(0.25f, s);
        yield return FinishAttack(s, RecoverBurst);
    }

    // 冲撞
    private System.Collections.IEnumerator A_Charge(int s)
    {
        float dur = enraged ? 0.7f : 0.85f;
        float t = 0f;
        while (t < dur)
        {
            if (s != attackSerial || state == State.Dead) yield break;
            if (!PauseMenu.IsPaused && !RunManager.IsSpawning)
            {
                float dx = AimPoint().x - transform.position.x;
                float step = ChargeSpeed * Time.deltaTime;
                float x = transform.position.x + Mathf.Sign(dx) * Mathf.Min(Mathf.Abs(dx), step);
                float limit = Mathf.Max(4f, RunManager.RoomRight - 0.9f);
                x = Mathf.Clamp(x, -limit, limit);
                transform.position = new Vector3(x, transform.position.y, transform.position.z);
                ApplyVisualScale(BossScale * 1.08f);
                chargeTrailTimer -= Time.deltaTime;
                if (chargeTrailTimer <= 0f)
                {
                    chargeTrailTimer = 0.1f;
                    FxRings.AfterImage(transform.position, sr.sprite, sr.color, false);
                }
                TryContactDamage();
                if (Mathf.Abs(x) >= limit) break;
                t += Time.deltaTime;
            }
            yield return null;
        }
        yield return FinishAttack(s, RecoverCharge);
    }

    // 全屏弹幕墙（留大缝）
    private System.Collections.IEnumerator A_Wall(int s)
    {
        FireWall();
        yield return WaitGameplay(0.5f, s);
        yield return FinishAttack(s, RecoverBurst);
    }

    // 无敌酸雨（全屏版）：整条屏幕宽度密集落雨，只有一个安全点能站人。
    // 安全点用醒目的绿色光柱标记 0.8 秒预警，玩家必须跑到那一点才能躲。
    // 安全点每波轮换，逼玩家持续移动，压迫感强但始终有唯一解。
    private System.Collections.IEnumerator A_Rain(int s)
    {
        invincible = true;
        GameAudio.Play(GameAudio.Sfx.Wave);
        const int columns = 18;                 // 全屏 18 列，密集到几乎无缝（控制数量避免 WebGL GC 尖峰）
        float limit = Mathf.Max(3f, RunManager.RoomRight - 1f);
        float colW = (limit * 2f) / columns;
        const float safeWidth = 1.6f;           // 安全点宽度：只够站一个人
        int waves = enraged ? 4 : 3;
        int lastSafe = -1;

        for (int w = 0; w < waves; w++)
        {
            if (s != attackSerial || state == State.Dead) yield break;

            // 选安全点：和上一波拉开距离，逼玩家横跨屏幕
            int safeCol;
            int tries = 0;
            do
            {
                safeCol = (int)(rainRandom() * columns) % columns;
                tries++;
            } while (lastSafe >= 0 && Mathf.Abs(safeCol - lastSafe) < columns / 3 && tries < 12);
            lastSafe = safeCol;
            float safeX = -limit + colW * (safeCol + 0.5f);

            // —— 预警 0.8 秒：绿色光柱标记唯一安全点 ——
            float warn = 0f, markerTimer = 0f;
            while (warn < 0.8f)
            {
                if (s != attackSerial || state == State.Dead) yield break;
                if (!PauseMenu.IsPaused && !RunManager.IsSpawning)
                {
                    if (sr != null) sr.color = Color.Lerp(CurrentColor, new Color(0.8f, 0.4f, 1f),
                        0.5f + Mathf.Sin(Time.time * 14f) * 0.3f);
                    markerTimer -= Time.deltaTime;
                    if (markerTimer <= 0f)
                    {
                        markerTimer = 0.12f;
                        // 地面绿圈 + 上空绿光，明确指出"站这里"
                        FxRings.Burst(new Vector2(safeX, -3.4f), new Color(0.3f, 1f, 0.45f, 0.95f), 1.1f);
                        FxRings.Burst(new Vector2(safeX, 0f), new Color(0.3f, 1f, 0.45f, 0.5f), 0.7f);
                    }
                    warn += Time.deltaTime;
                }
                yield return null;
            }

            // —— 全屏落雨：除安全点覆盖的列外，每一列都下 ——
            for (int c = 0; c < columns; c++)
            {
                float x = -limit + colW * (c + 0.5f);
                if (Mathf.Abs(x - safeX) < safeWidth * 0.5f) continue;   // 唯一安全点
                // 每列 2 发错开高度，形成连续弹幕
                ShootFrom(new Vector2(x, 7f), Vector2.down, "酸雨");
                ShootFrom(new Vector2(x, 4.5f), Vector2.down, "酸雨");
            }
            GameAudio.Play(GameAudio.Sfx.Shoot);

            // 波间隔，让酸雨落下并留出反应时间
            yield return WaitGameplay(enraged ? 0.75f : 0.9f, s);
        }
        yield return FinishAttack(s, RecoverBurst);
    }

    // 大激光（锁高度）
    private System.Collections.IEnumerator A_Laser(int s)
    {
        laserY = AimPoint().y;
        float charge = enraged ? 1.1f : 1.5f;
        float t = 0f;
        while (t < charge)
        {
            if (s != attackSerial || state == State.Dead) yield break;
            if (!PauseMenu.IsPaused && !RunManager.IsSpawning)
            {
                telegraphRingTimer -= Time.deltaTime;
                if (telegraphRingTimer <= 0f) { telegraphRingTimer = 0.1f; FxRings.Land(new Vector2(transform.position.x, laserY), 0.4f); }
                if (sr != null) sr.color = Mathf.Repeat(Time.time * 18f, 1f) > 0.5f ? new Color(1f, 0.3f, 0.3f) : CurrentColor;
                t += Time.deltaTime;
            }
            yield return null;
        }
        FireLaserVolley();
        HitFeedback.Play(HitFeedback.Feel.Kill);
        yield return WaitGameplay(enraged ? 0.55f : 0.4f, s);
        yield return FinishAttack(s, RecoverCharge);
    }

    // 螺旋：连续旋转发射
    private System.Collections.IEnumerator A_Spiral(int s)
    {
        int shots = enraged ? 30 : 22;
        float angle = 0f;
        for (int i = 0; i < shots; i++)
        {
            if (s != attackSerial || state == State.Dead) yield break;
            // 对向两发，每步旋转 0.42 弧度 —— 形成螺旋
            float a = angle;
            Shoot(new Vector2(Mathf.Cos(a), Mathf.Sin(a)), "螺旋");
            Shoot(new Vector2(-Mathf.Cos(a), -Mathf.Sin(a)), "螺旋");
            angle += 0.42f;
            yield return WaitGameplay(enraged ? 0.045f : 0.06f, s);
        }
        yield return FinishAttack(s, RecoverBurst);
    }

    // 追踪水弹：一次 3 发朝玩家方向
    private System.Collections.IEnumerator A_Homing(int s)
    {
        for (int i = 0; i < 3; i++)
        {
            if (s != attackSerial || state == State.Dead) yield break;
            EmitFan(3, 0.18f, "追踪");
            yield return WaitGameplay(0.22f, s);
        }
        yield return FinishAttack(s, RecoverBurst);
    }

    // 散弹爆发：广角大散射
    private System.Collections.IEnumerator A_Burst(int s)
    {
        EmitFan(enraged ? 13 : 9, 2.4f, "散爆");
        yield return WaitGameplay(0.3f, s);
        yield return FinishAttack(s, RecoverBurst);
    }

    // 泪雨：bowser 式向下洒一排
    private System.Collections.IEnumerator A_TearRain(int s)
    {
        EmitSkyRain(enraged ? 16 : 10, "泪雨", false);
        yield return WaitGameplay(0.5f, s);
        yield return FinishAttack(s, RecoverBurst);
    }

    // 孢子云：近身大范围慢速环
    private System.Collections.IEnumerator A_SporeCloud(int s)
    {
        EmitRing(enraged ? 24 : 16, (float)rainRandom() * 6.28f, "孢子云");
        yield return WaitGameplay(0.5f, s);
        yield return FinishAttack(s, RecoverBurst);
    }

    // 交叉散射：四个方向各一束
    private System.Collections.IEnumerator A_Cross(int s)
    {
        Vector2 aim = AimDir();
        for (int d = 0; d < 4; d++)
        {
            Vector2 baseDir = Rotate(aim, d * Mathf.PI * 0.5f);
            for (int i = -1; i <= 1; i++) Shoot(Rotate(baseDir, i * 0.14f), "交叉");
        }
        yield return WaitGameplay(0.35f, s);
        yield return FinishAttack(s, RecoverBurst);
    }

    // 双螺旋：两组反向旋转
    private System.Collections.IEnumerator A_TwinSpiral(int s)
    {
        int shots = enraged ? 18 : 12;
        float a1 = 0f, a2 = Mathf.PI;
        for (int i = 0; i < shots; i++)
        {
            if (s != attackSerial || state == State.Dead) yield break;
            Shoot(new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)), "双螺旋");
            Shoot(new Vector2(Mathf.Cos(a2), Mathf.Sin(a2)), "双螺旋");
            a1 += 0.38f; a2 -= 0.38f;
            yield return WaitGameplay(enraged ? 0.05f : 0.07f, s);
        }
        yield return FinishAttack(s, RecoverBurst);
    }

    // 地雷孢子：地上放几颗延迟弹
    private System.Collections.IEnumerator A_Mine(int s)
    {
        float limit = Mathf.Max(3f, RunManager.RoomRight - 2f);
        int n = enraged ? 6 : 4;
        for (int i = 0; i < n; i++)
        {
            if (s != attackSerial || state == State.Dead) yield break;
            float x = Mathf.Lerp(-limit, limit, (i + 0.5f) / n);
            Vector2 from = new Vector2(x, -3.4f);
            ShootFrom(from, Vector2.up, "孢子雷");
            yield return WaitGameplay(0.12f, s);
        }
        yield return FinishAttack(s, RecoverBurst);
    }

    // 毒雾扩散：无敌 + 从中心向外多圈
    private System.Collections.IEnumerator A_PoisonMist(int s)
    {
        invincible = true;
        int rings = enraged ? 4 : 3;
        for (int r = 0; r < rings; r++)
        {
            if (s != attackSerial || state == State.Dead) yield break;
            EmitRing(12, r * 0.26f, "毒雾");
            yield return WaitGameplay(0.42f, s);
        }
        yield return FinishAttack(s, RecoverBurst);
    }

    // 五连点射
    private System.Collections.IEnumerator A_QuickShot(int s)
    {
        for (int i = 0; i < 5; i++)
        {
            if (s != attackSerial || state == State.Dead) yield break;
            EmitFan(1, 0f, "点射");
            yield return WaitGameplay(0.14f, s);
        }
        yield return FinishAttack(s, RecoverBurst);
    }

    // 回头弹：先朝玩家打，再反方向补一发
    private System.Collections.IEnumerator A_Backshot(int s)
    {
        Vector2 aim = AimDir();
        Shoot(aim, "回头弹");
        yield return WaitGameplay(0.3f, s);
        Shoot(-aim, "回头弹");
        EmitFan(3, 0.2f, "回头弹");
        yield return FinishAttack(s, RecoverBurst);
    }

    // 疾风三连：三次冲撞（缩时版）
    private System.Collections.IEnumerator A_Gale(int s)
    {
        for (int k = 0; k < 3; k++)
        {
            if (s != attackSerial || state == State.Dead) yield break;
            float t = 0f;
            while (t < 0.32f)
            {
                if (s != attackSerial || state == State.Dead) yield break;
                if (!PauseMenu.IsPaused && !RunManager.IsSpawning)
                {
                    float dx = AimPoint().x - transform.position.x;
                    float x = transform.position.x + Mathf.Sign(dx) * Mathf.Min(Mathf.Abs(dx), ChargeSpeed * 1.2f * Time.deltaTime);
                    float limit = Mathf.Max(4f, RunManager.RoomRight - 0.9f);
                    x = Mathf.Clamp(x, -limit, limit);
                    transform.position = new Vector3(x, transform.position.y, transform.position.z);
                    TryContactDamage();
                    t += Time.deltaTime;
                }
                yield return null;
            }
            yield return WaitGameplay(0.18f, s);
        }
        yield return FinishAttack(s, RecoverCharge);
    }

    // 十字激光：横竖两条
    private System.Collections.IEnumerator A_CrossLaser(int s)
    {
        float t = 0f;
        while (t < 0.9f)
        {
            if (s != attackSerial || state == State.Dead) yield break;
            if (!PauseMenu.IsPaused && !RunManager.IsSpawning)
            {
                if (sr != null) sr.color = Mathf.Repeat(Time.time * 20f, 1f) > 0.5f ? new Color(1f, 0.3f, 0.3f) : CurrentColor;
                t += Time.deltaTime;
            }
            yield return null;
        }
        float limit = Mathf.Max(3f, RunManager.RoomRight - 1f);
        // 横
        for (int i = 0; i < 18; i++)
        {
            if (i % 7 == 3) continue;
            float x = Mathf.Lerp(-limit, limit, i / 17f);
            ShootFrom(new Vector2(x, AimPoint().y), Vector2.down, "十字激光");
        }
        // 竖
        for (int i = 0; i < 12; i++)
        {
            if (i % 6 == 3) continue;
            float y = Mathf.Lerp(-3f, 5f, i / 11f);
            ShootFrom(new Vector2(transform.position.x, y), Vector2.right, "十字激光");
        }
        yield return WaitGameplay(0.4f, s);
        yield return FinishAttack(s, RecoverCharge);
    }

    // 螺旋环：多圈同时旋转（终极）
    private System.Collections.IEnumerator A_RingSpiral(int s)
    {
        int shots = enraged ? 26 : 18;
        float a = 0f;
        for (int i = 0; i < shots; i++)
        {
            if (s != attackSerial || state == State.Dead) yield break;
            EmitRing(6, a, "螺旋环");
            a += 0.3f;
            yield return WaitGameplay(enraged ? 0.07f : 0.09f, s);
        }
        yield return FinishAttack(s, RecoverBurst);
    }

    // 陨石雨：大颗密集天降
    private System.Collections.IEnumerator A_Meteor(int s)
    {
        int waves = enraged ? 5 : 3;
        for (int w = 0; w < waves; w++)
        {
            if (s != attackSerial || state == State.Dead) yield break;
            EmitSkyRain(enraged ? 8 : 5, "陨石", w % 2 == 0);
            yield return WaitGameplay(0.45f, s);
        }
        yield return FinishAttack(s, RecoverBurst);
    }

    // 追踪群：围绕玩家散开的一圈
    private System.Collections.IEnumerator A_SeekerSwarm(int s)
    {
        Vector2 c = AimPoint();
        int n = enraged ? 14 : 9;
        float a0 = (float)rainRandom() * 6.28f;
        for (int i = 0; i < n; i++)
        {
            float a = a0 + i * Mathf.PI * 2f / n;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            ShootFrom(c + dir * 3f, -dir, "追踪群");   // 从外向内收拢
        }
        yield return WaitGameplay(0.45f, s);
        yield return FinishAttack(s, RecoverBurst);
    }

    // 全屏十字（终极）：扇幕 + 天雨同时
    private System.Collections.IEnumerator A_CrossFire(int s)
    {
        EmitFan(enraged ? 15 : 11, 2.8f, "十字火");
        yield return WaitGameplay(0.3f, s);
        EmitSkyRain(enraged ? 12 : 7, "十字火", true);
        yield return WaitGameplay(0.4f, s);
        yield return FinishAttack(s, RecoverBurst);
    }
    private void UpdateRecover()
    {
        // 硬直期间不移动，给玩家反打窗口
        ApplyVisualScale(BossScale * 0.96f);   // 微微收缩，明确"现在虚弱"的读数
        if (stateTimer <= 0f) EnterState(State.Idle, IdleTime);
    }

    private void Drift()
    {
        float targetX = PlayerHealth.Instance.transform.position.x;
        float x = Mathf.MoveTowards(transform.position.x, targetX, DriftSpeed * Time.deltaTime);
        float limit = Mathf.Max(3f, RunManager.RoomRight - 2f);
        x = Mathf.Clamp(x, -limit, limit);
        float y = baseY + Mathf.Sin(Time.time * 1.6f + hoverSeed) * 0.22f;
        transform.position = new Vector3(x, y, transform.position.z);
    }

    // 攻击时的慢速漂移（比待机慢，让弹幕轨迹可预判）
    private void DriftSlower()
    {
        float x = transform.position.x;
        float limit = Mathf.Max(3f, RunManager.RoomRight - 2f);
        x = Mathf.Clamp(x, -limit, limit);
        float y = baseY + Mathf.Sin(Time.time * 1.2f + hoverSeed) * 0.14f;
        transform.position = new Vector3(x, y, transform.position.z);
        ApplyVisualScale(BossScale);
    }

    // 全屏弹幕墙（留一个大缝隙）
    private void FireWall()
    {
        float limit = Mathf.Max(3f, RunManager.RoomRight - 1f);
        Vector2 origin = transform.position;
        const int cols = 16;
        int gapCenter = AimPoint().x > 0 ? cols - 5 : 4;
        gapCenter = Mathf.Clamp(gapCenter, 2, cols - 3);
        for (int i = 0; i < cols; i++)
        {
            if (Mathf.Abs(i - gapCenter) <= 1) continue;   // 3 格宽的大缝隙
            float x = Mathf.Lerp(-limit, limit, i / (float)(cols - 1));
            Vector2 from = new Vector2(x, origin.y);
            ShootFrom(from, Vector2.down, "弹幕墙");
        }
    }

    // 激光弹幕：沿锁定高度一整排密集子弹，留几个缺口
    private void FireLaserVolley()
    {
        float limit = Mathf.Max(3f, RunManager.RoomRight - 1f);
        const int beams = 24;
        for (int i = 0; i < beams; i++)
        {
            if (i % 8 == 3 || i % 8 == 4) continue;   // 3 个大缺口
            float x = Mathf.Lerp(-limit, limit, i / (float)(beams - 1));
            ShootFrom(new Vector2(x, laserY + 0.02f), Vector2.down, "激光");
        }
    }

    // 主攻击，按变体分三种模式：环形 / 双扇形 / 普通扇形
    private void FireFan()
    {
        Vector2 origin = transform.position;
        Vector2 toPlayer = ((Vector2)PlayerHealth.Instance.transform.position - origin).normalized;

        if (spec.ring)
        {
            // 孢潮：360° 环形弹幕，没有安全方向，逼玩家跳跃穿过缝隙
            int n = enraged ? 20 : 14;
            float offset = Mathf.Atan2(toPlayer.y, toPlayer.x);
            for (int i = 0; i < n; i++)
            {
                float a = offset + i * Mathf.PI * 2f / n;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                EnemyBullet.Spawn(origin, origin + d * 3f, "Boss");
            }
            return;
        }

        int half = spec.fanCount + (enraged ? 1 : 0);
        float step = (enraged ? 0.14f : 0.16f) * spec.fanStepMul;

        if (spec.doubleFan)
        {
            // 深渊之瞳：上下两层错开的扇形，正面也有弹幕，封死直线走位
            for (int layer = -1; layer <= 1; layer += 2)
            {
                Vector2 aim = Rotate(toPlayer, layer * 0.22f);
                for (int i = -half; i <= half; i++)
                    EnemyBullet.Spawn(origin, origin + Rotate(aim, i * step) * 3f, "Boss");
            }
        }
        else
        {
            for (int i = -half; i <= half; i++)
                EnemyBullet.Spawn(origin, origin + Rotate(toPlayer, i * step) * 3f, "Boss");
        }
    }

    private void TryContactDamage()
    {
        Vector3 p = PlayerHealth.Instance.transform.position;
        // 判定半径按变体体型缩放，大 Boss 的碰撞范围和它的视觉尺寸一致
        float rx = contactRadiusX * spec.scale;
        float ry = contactRadiusY * spec.scale;
        if (Mathf.Abs(p.x - transform.position.x) < rx &&
            Mathf.Abs(p.y - transform.position.y) < ry)
            PlayerHealth.Instance.TakeDamage(1, spec.name + " 冲撞");
    }

    public void Hit(Vector2 direction, float damage = 0.25f)
    {
        if (state == State.Dead || state == State.Enter) return;
        if (invincible)
        {
            // 酸雨阶段无敌：弹开但不受伤，给玩家明确反馈
            HitParticles.Spawn(transform.position, new Color(0.8f, 0.5f, 1f), 2);
            return;
        }

        damageCarry += Mathf.Max(0.01f, damage);
        int whole = Mathf.FloorToInt(damageCarry);
        if (whole > 0)
        {
            hp -= whole;
            damageCarry -= whole;
        }
        StartCoroutine(FlashWhite());
        Flinch();                       // 受击抖一下，大 Boss 也要有"被打到"的实感
        HitParticles.Spawn(transform.position, CurrentColor, 3);

        if (hp <= 0)
        {
            Die();
            return;
        }

        // 半血进二阶段：只用改一次，阈值按变体（有的 Boss 更早狂暴）
        if (!enraged && hp <= maxHp * spec.enrageAt) Enrage();

        HitFeedback.Play(HitFeedback.Feel.Hit);
        HitParticles.Spawn(transform.position, CurrentColor, 4);
    }

    private void Enrage()
    {
        enraged = true;
        if (sr != null) sr.color = CurrentColor;

        Flinch();
        FxRings.Burst(transform.position, new Color(1f, 0.62f, 0.28f, 0.85f), 1.6f);
        HitParticles.Spawn(transform.position, new Color(1f, 0.78f, 0.32f, 1f), 20);
        HitFeedback.Play(HitFeedback.Feel.Kill);
        GameAudio.Play(GameAudio.Sfx.Wave);
        Debug.Log("[Boss] " + spec.name + " 进入第二阶段：弹幕更密、冲撞更快");
    }

    private System.Collections.IEnumerator FlashWhite()
    {
        if (sr == null) yield break;
        sr.color = Color.white;
        yield return new WaitForSeconds(0.06f);
        if (sr != null && state != State.Dead) sr.color = CurrentColor;
    }

    private void Die()
    {
        state = State.Dead;
        hp = 0;
        attackSerial++;                 // 让正在跑的攻击协程失效，死亡后不再发弹
        invincible = false;
        StopAllCoroutines();            // 停掉攻击协程和闪白协程
        if (col != null) col.enabled = false;
        FxRings.Burst(transform.position, new Color(1f, 0.72f, 0.4f, 0.95f), 2.6f);
        HitFeedback.Play(HitFeedback.Feel.Kill);
        HitParticles.Spawn(transform.position, CurrentColor, 26);
        HitParticles.Spawn(transform.position, new Color(1f, 0.9f, 0.5f, 1f), 12);
        GameAudio.Play(GameAudio.Sfx.Win);

        if (RunManager.HasInstance) RunManager.Instance.BossDefeated();
        Destroy(gameObject, 0.6f);       // 多留一会儿让粒子铺开，不再"瞬间消失"
    }

    // 被重开 / 回标题时直接清掉，不触发通关
    public void Despawn()
    {
        state = State.Dead;
        attackSerial++;
        invincible = false;
        StopAllCoroutines();
        Destroy(gameObject);
    }

    private void EnterState(State next, float duration)
    {
        state = next;
        stateTimer = duration;
        stateDuration = Mathf.Max(0.01f, duration);
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
