using UnityEngine;

// 第 3 波的 Boss：单一大型敌人，循环「漂移 → 蓄力 → 出招 → 硬直」。
// 出招在「扇形弹幕」和「横向冲撞」之间交替，出招前有 0.5 秒变色蓄力，给玩家反应时间。
// 死亡不自己结束一局，而是回调 RunManager.BossDefeated()，保持"谁管流程"的边界。
public class Boss : MonoBehaviour
{
    public static Boss Instance { get; private set; }

    private enum State { Enter, Idle, Telegraph, Burst, Charge, Recover, Dead }

    [SerializeField] private float driftSpeed = 1.1f;
    [SerializeField] private float chargeSpeed = 9f;
    [SerializeField] private float contactRadiusX = 1.05f;
    [SerializeField] private float contactRadiusY = 1.3f;

    // 体型：仍要明显大于其他单位，但整体跟着角色缩小一档（原来 2.4）
    private const float BossScale = 1.9f;
    private const float BossEnterScale = 0.45f;

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
            if (!hasArt) return enraged ? EnragedColor : BaseColor;
            return enraged ? new Color(1f, 0.55f, 0.58f) : Color.white;
        }
    }

    private int hp;
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

    public int Hp { get { return hp; } }
    public int MaxHp { get { return maxHp; } }
    public bool Alive { get { return state != State.Dead; } }
    public bool Enraged { get { return enraged; } }

    // 二阶段：预警更短、间隔更小、冲得更快。数值下降但都还在"能反应"的范围内。
    private float TelegraphTime { get { return enraged ? 0.34f : 0.5f; } }
    private float IdleTime { get { return enraged ? 0.70f : 1.1f; } }
    private float RecoverBurst { get { return enraged ? 0.30f : 0.45f; } }
    private float RecoverCharge { get { return enraged ? 0.45f : 0.70f; } }
    private float DriftSpeed { get { return enraged ? driftSpeed * 1.6f : driftSpeed; } }
    private float ChargeSpeed { get { return enraged ? chargeSpeed * 1.2f : chargeSpeed; } }

    public void Init(int hpValue, float duration, Vector3 from, Vector3 to)
    {
        Instance = this;
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

        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        hasArt = sr != null && sr.sprite != null && ArtStore.Boss != null && sr.sprite == ArtStore.Boss;
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

        // 交替出招：弹幕 → 冲撞 → 弹幕 …
        alternate = !alternate;
        if (alternate)
        {
            FireFan();
            EnterState(State.Recover, RecoverBurst);
        }
        else
        {
            EnterState(State.Charge, enraged ? 0.7f : 0.85f);
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

    // 扇形弹幕，正中指向玩家。二阶段从 7 发加到 9 发、扇面更宽。
    private void FireFan()
    {
        Vector2 origin = transform.position;
        Vector2 toPlayer = ((Vector2)PlayerHealth.Instance.transform.position - origin).normalized;
        int half = enraged ? 4 : 3;
        float step = enraged ? 0.135f : 0.14f;
        for (int i = -half; i <= half; i++)
            EnemyBullet.Spawn(origin, origin + Rotate(toPlayer, i * step) * 3f, "Boss");
    }

    private void TryContactDamage()
    {
        Vector3 p = PlayerHealth.Instance.transform.position;
        if (Mathf.Abs(p.x - transform.position.x) < contactRadiusX &&
            Mathf.Abs(p.y - transform.position.y) < contactRadiusY)
            PlayerHealth.Instance.TakeDamage(1, "Boss 冲撞");
    }

    public void Hit(Vector2 direction, int damage = 1)
    {
        if (state == State.Dead || state == State.Enter) return;

        hp -= Mathf.Max(1, damage);
        StartCoroutine(FlashWhite());
        Flinch();                       // 受击抖一下，大 Boss 也要有"被打到"的实感
        HitParticles.Spawn(transform.position, CurrentColor, 3);

        if (hp <= 0)
        {
            Die();
            return;
        }

        // 半血进二阶段：只用改一次，之后所有时长/速度都读 enraged
        if (!enraged && hp <= maxHp / 2) Enrage();

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
        Debug.Log("[Boss] 半血进入第二阶段：预警更短、扇形 9 发、冲撞更快");
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
