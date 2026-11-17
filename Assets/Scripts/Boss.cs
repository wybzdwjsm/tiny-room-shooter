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

    private enum State { Enter, Idle, Telegraph, Burst, Charge, Recover, Dead,
        InvincibleRain, LaserCharge, LaserFire, WallBarrage }
    private enum Attack { Fan, Charge, Rain, Laser, Wall }

    // 每种 Boss 的攻击池（轮转使用）。原版只会扇形/冲撞，新 Boss 有专属大招。
    private static readonly Attack[][] AttackPools =
    {
        // TideEye 潮眼：原版两招 + 全屏弹幕
        new[] { Attack.Fan, Attack.Charge, Attack.Wall, Attack.Fan, Attack.Charge },
        // Spore 孢潮：环形 + 酸雨（"孢潮"从天上撒孢子）
        new[] { Attack.Fan, Attack.Rain, Attack.Wall, Attack.Fan, Attack.Rain },
        // Spear 裂鳍：高频冲撞 + 激光
        new[] { Attack.Charge, Attack.Laser, Attack.Fan, Attack.Charge, Attack.Laser },
        // Abyss 深渊之瞳：全部招式都会
        new[] { Attack.Fan, Attack.Wall, Attack.Laser, Attack.Rain, Attack.Charge },
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
    private Color CurrentColor
    {
        get
        {
            Color tint = spec.tint;
            // 有美术时：把 tint 乘到原画上（保留插画细节），狂暴再偏红一点
            if (hasArt)
                return enraged ? new Color(tint.r, tint.g * 0.55f, tint.b * 0.55f) : tint;
            // 没图时退回纯色方块表现
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
    private bool alternate;
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
    private float actionTimer;         // 大招内部计时（酸雨间隔 / 激光持续）
    private float laserY;              // 激光锁定的高度
    private bool invincible;           // 酸雨阶段无敌（打不动）

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
        actionTimer = 0f;
        invincible = false;

        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        // 有美术 = 当前贴图是 ArtStore 里加载的任意一张（原版或 4 种变体之一）
        hasArt = sr != null && sr.sprite != null && ArtStore.IsBossSprite(sr.sprite);
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
            case State.Charge: UpdateCharge(); break;
            case State.Recover: UpdateRecover(); break;
            case State.InvincibleRain: UpdateRain(); break;
            case State.LaserCharge: UpdateLaserCharge(); break;
            case State.LaserFire: UpdateLaserFire(); break;
            case State.WallBarrage: UpdateWallBarrage(); break;
        }
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

    // 蓄力结束后进入对应招式的执行状态
    private void ExecuteAttack(Attack atk)
    {
        switch (atk)
        {
            case Attack.Fan:
                FireFan();
                EnterState(State.Recover, RecoverBurst);
                break;
            case Attack.Charge:
                EnterState(State.Charge, enraged ? 0.7f : 0.85f);
                break;
            case Attack.Rain:
                // 酸雨：先进入无敌，Boss 悬停不动开始下酸雨
                invincible = true;
                actionTimer = 0f;
                EnterState(State.InvincibleRain, enraged ? 3.2f : 2.6f);
                GameAudio.Play(GameAudio.Sfx.Wave);
                break;
            case Attack.Laser:
                // 激光：锁定玩家当前高度，长蓄力后射出
                laserY = PlayerHealth.Instance.transform.position.y;
                actionTimer = 0f;
                EnterState(State.LaserCharge, enraged ? 1.1f : 1.5f);
                break;
            case Attack.Wall:
                // 全屏弹幕墙：一次性铺满留缝
                FireWall();
                EnterState(State.WallBarrage, 0.5f);
                break;
        }
    }

    private void UpdateCharge()
    {
        float dx = PlayerHealth.Instance.transform.position.x - transform.position.x;        float step = ChargeSpeed * Time.deltaTime;
        float x = transform.position.x + Mathf.Sign(dx) * Mathf.Min(Mathf.Abs(dx), step);
        float limit = Mathf.Max(4f, RunManager.RoomRight - 0.9f);
        x = Mathf.Clamp(x, -limit, limit);
        transform.position = new Vector3(x, transform.position.y, transform.position.z);
        ApplyVisualScale(BossScale * 1.08f);   // 冲撞时略微"撑开"，显得更有压迫感

        // 高速冲撞留残影，和冲锋怪同一套语言，但boss 更大更慢，节流放宽到 0.1 秒
        chargeTrailTimer -= Time.deltaTime;
        if (chargeTrailTimer <= 0f)
        {
            chargeTrailTimer = 0.1f;
            FxRings.AfterImage(transform.position, sr.sprite, sr.color, false);
        }

        TryContactDamage();

        // 撞到边界或时间到了就收手
        if (stateTimer <= 0f || Mathf.Abs(x) >= limit) EnterState(State.Recover, RecoverCharge);
    }

    // —— 新招 1：无敌 + 天降酸雨 ——
    private void UpdateRain()
    {
        Drift();                                    // 悬停漂移，但此时打不动
        ApplyVisualScale(BossScale * 1.06f);

        // 视觉： Boss 上方持续冒紫色粒子，提示"正在降下什么"
        if (sr != null) sr.color = Color.Lerp(CurrentColor, new Color(0.8f, 0.4f, 1f),
            0.5f + Mathf.Sin(Time.time * 14f) * 0.3f);

        actionTimer -= Time.deltaTime;
        if (actionTimer <= 0f)
        {
            actionTimer = enraged ? 0.22f : 0.32f;
            // 在房间范围的随机 x 位置从天上生成一滴酸雨，从上往下落
            float limit = Mathf.Max(3f, RunManager.RoomRight - 1f);
            float x = Mathf.Lerp(-limit, limit, (float)rainRandom());
            // 相机视野约 y=-6.8~6.8，从顶部 7 落到地板下 -5
            Vector2 from = new Vector2(x, 7f);
            Vector2 to = new Vector2(x, -5f);
            EnemyBullet.Spawn(from, to, "酸雨");
        }

        if (stateTimer <= 0f)
        {
            invincible = false;
            if (sr != null) sr.color = CurrentColor;
            EnterState(State.Recover, RecoverBurst);
        }
    }

    // 确定性伪随机（不引入新 Random 源，保护 seed 复现）
    private float rainRandom()
    {
        rainSeed = rainSeed * 0.6180339887f + 0.31415926f;
        rainSeed -= Mathf.Floor(rainSeed);
        return rainSeed;
    }
    private float rainSeed = 0.37f;

    // —— 新招 2：长蓄力大激光 ——
    private void UpdateLaserCharge()
    {
        // 蓄力期间 Boss 定住，锁定高度越来越亮的光环
        ApplyVisualScale(BossScale * (1f + (1f - stateTimer / Mathf.Max(0.01f, stateDuration)) * 0.14f));
        telegraphRingTimer -= Time.deltaTime;
        if (telegraphRingTimer <= 0f)
        {
            telegraphRingTimer = 0.1f;
            FxRings.Land(new Vector2(transform.position.x, laserY), 0.4f);
        }
        if (sr != null) sr.color = Mathf.Repeat(Time.time * 18f, 1f) > 0.5f
            ? new Color(1f, 0.3f, 0.3f) : CurrentColor;

        if (stateTimer <= 0f)
        {
            if (sr != null) sr.color = CurrentColor;
            FireLaserVolley();
            EnterState(State.LaserFire, enraged ? 0.55f : 0.4f);
            HitFeedback.Play(HitFeedback.Feel.Kill);
        }
    }

    private void UpdateLaserFire()
    {
        ApplyVisualScale(BossScale * 1.1f);
        if (stateTimer <= 0f) EnterState(State.Recover, RecoverCharge);
    }

    // 激光弹幕：沿锁定高度一整排密集子弹，留几个缺口让玩家躲
    private void FireLaserVolley()
    {
        float limit = Mathf.Max(3f, RunManager.RoomRight - 1f);
        const int beams = 24;
        for (int i = 0; i < beams; i++)
        {
            // 留 3 个大缺口（相邻两发不发），缝隙足够玩家通过
            if (i % 8 == 3 || i % 8 == 4) continue;
            float x = Mathf.Lerp(-limit, limit, i / (float)(beams - 1));
            EnemyBullet.Spawn(new Vector2(x, laserY + 0.02f),
                new Vector2(x, laserY - 3f), "激光");
        }
    }

    // —— 新招 3：全屏弹幕墙，留一个大缝隙 ——
    private void FireWall()
    {
        float limit = Mathf.Max(3f, RunManager.RoomRight - 1f);
        Vector2 origin = transform.position;
        // 一道横贯全场的弹幕墙，朝玩家方向推进；中间 3 格留空 = 大缝隙
        const int cols = 16;
        int gapCenter = Mathf.Clamp(PlayerHealth.Instance.transform.position.x > 0
            ? cols - 5 : 4, 2, cols - 3);   // 缝隙开在玩家侧，但留一点距离需要走位
        for (int i = 0; i < cols; i++)
        {
            if (Mathf.Abs(i - gapCenter) <= 1) continue;   // 3 格宽的大缝隙
            float x = Mathf.Lerp(-limit, limit, i / (float)(cols - 1));
            Vector2 from = new Vector2(x, origin.y);
            EnemyBullet.Spawn(from, from + Vector2.down * 3f, "弹幕墙");
        }
    }

    private void UpdateWallBarrage()
    {
        ApplyVisualScale(BossScale);
        if (stateTimer <= 0f) EnterState(State.Recover, RecoverBurst);
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
