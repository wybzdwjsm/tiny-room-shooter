using UnityEngine;

// 敌人：三种行为，由 RunManager 按 seed 决定，血量 / 体型 / 颜色 / 射速各不相同。
//   Shooter（普通 / 快速 / 精英）：保持距离、定时朝玩家射击、上下浮动
//   Charger（冲锋）：高速水平冲撞，靠接触扣血，逼玩家跳起来
//   Turret （炮台）：原地不动，三连散射，逼玩家走位
// 被命中后闪白、击退，归零时爆出粒子并上报清怪。
public class Enemy : MonoBehaviour
{
    public enum Kind { Shooter, Charger, Turret }

    public string TypeName = "普通怪";
    public Kind kind = Kind.Shooter;

    // 给视觉层读：开火预警进度 0~1，用来做"蓄力膨胀"，让"要开枪了"除了变色还有形变
    public float TelegraphProgress
    {
        get
        {
            if (kind == Kind.Charger) return 0f;
            return fireTimer <= 0.28f ? 1f - Mathf.Clamp01(fireTimer / 0.28f) : 0f;
        }
    }

    [SerializeField] private int hp = 3;
    [SerializeField] private float knockback = 0.25f;
    [SerializeField] private float flashTime = 0.08f;
    [SerializeField] private float fireInterval = 2.2f;

    // 优先取视觉层精灵：EnemyVisual 会把精灵移到 Visual 子节点，
    // 这里动态解析，避免缓存到被关掉的根节点 sr 导致闪白 / 染色看不见
    private SpriteRenderer sr
    {
        get
        {
            EnemyVisual vis = GetComponent<EnemyVisual>();
            if (vis != null && vis.LiveSprite != null) return vis.LiveSprite;
            return cachedSr;
        }
    }
    private SpriteRenderer cachedSr;
    private Collider2D col;
    private Color baseColor;
    private float fireTimer;
    private float moveSpeed;
    private float baseY;
    private float bobOffset;

    // 入场动画：从房间外侧飞到位，飞行途中不行动、不可被击中
    private Vector3 introTarget;
    private Vector3 introFrom;
    private float introTime;
    private float introTotal;
    private float introDelay;

    // 死亡动画：不再"啪"一下消失，先膨胀再淡出
    private const float DeathDuration = 0.22f;
    private bool dying;
    private float dieTime;
    private Vector3 dieBaseScale = Vector3.one;
    private Color dieColor = Color.white;

    private float hitFlashUntil;     // 受击闪白期间不被开火预警色覆盖

    private void Awake()
    {
        cachedSr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        if (cachedSr != null) baseColor = cachedSr.color;
    }

    // 所有随机参数都由 RunManager 的同一个 System.Random 传入，保证 seed 可复现
    public void Init(int hpValue, float interval, Color color, float firstShotDelay)
    {
        hp = hpValue;
        fireInterval = interval;
        fireTimer = firstShotDelay;
        if (sr != null) { sr.color = color; baseColor = color; }

        // 由射速推断射手类型的速度：快速怪最快、精英最慢；冲锋 / 炮台单独指定
        switch (kind)
        {
            case Kind.Charger: moveSpeed = 2.9f; break;
            case Kind.Turret: moveSpeed = 0f; break;
            default: moveSpeed = interval < 1.5f ? 1.6f : (interval > 2.5f ? 0.45f : 0.8f); break;
        }

        baseY = transform.position.y;
        bobOffset = transform.position.x * 1.3f;   // 确定性，不引入新的随机源
    }

    // 入场：从房间边缘飞向目标点。延迟期间隐藏，飞行期间无碰撞、不行动，落地才开始正常行为。
    public void PlayIntro(Vector3 target, Vector3 from, float duration, float delay)
    {
        introTarget = target;
        introFrom = from;
        introTotal = Mathf.Max(0.01f, duration);
        introTime = 0f;
        introDelay = Mathf.Max(0f, delay);
        baseY = target.y;
        transform.position = from;

        if (sr != null) sr.enabled = false;
        if (col != null) col.enabled = false;
    }

    private void Update()
    {
        // 死亡动画期间不再参与任何行为，只播自己的消散
        if (dying) { UpdateDeath(); return; }

        if (!RunManager.HasInstance || !RunManager.IsPlaying) return;
        if (PauseMenu.IsPaused) return;
        if (!PlayerHealth.HasInstance || PlayerHealth.Instance.IsDead) return;

        if (introDelay > 0f) { introDelay -= Time.deltaTime; return; }
        if (introTime < introTotal) { UpdateIntro(); return; }

        // 进场期间其余敌人保持静止（不射击、不移动、不撞人）
        if (RunManager.IsSpawning) return;

        switch (kind)
        {
            case Kind.Charger: UpdateCharger(); break;
            case Kind.Turret: UpdateTurret(); break;
            default: UpdateShooter(); break;
        }
    }

    // 死亡：先向外"撑"一下再缩没，配合淡出。比直接 SetActive(false) 好读很多。
    private void UpdateDeath()
    {
        dieTime += Time.deltaTime;
        float k = Mathf.Clamp01(dieTime / DeathDuration);

        // 0 -> 1.35 -> 0：先膨胀 35%，再收回（收得比撑得快，看起来是"爆开"）
        float s = k < 0.35f
            ? Mathf.Lerp(1f, 1.35f, k / 0.35f)
            : Mathf.Lerp(1.35f, 0f, (k - 0.35f) / 0.65f);

        transform.localScale = dieBaseScale * s;
        if (sr != null) sr.color = new Color(dieColor.r, dieColor.g, dieColor.b, 1f - k * k);

        if (k >= 1f) gameObject.SetActive(false);
    }

    private void UpdateIntro()
    {
        // 进场动画用普通缩放时间：这样暂停菜单一按，入场也会跟着停住
        introTime += Time.deltaTime;
        float k = Mathf.Clamp01(introTime / introTotal);

        // 三次 ease-out：刚进场几乎是全速冲进来，接近目标点时快速收速
        float ease = 1f - Mathf.Pow(1f - k, 3f);
        Vector3 arc = Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.3f);   // 轻微上抛弧线，不是平移
        transform.position = Vector3.Lerp(introFrom, introTarget, ease) + arc;

        if (sr != null) sr.enabled = true;

        if (k < 1f) return;

        transform.position = introTarget;
        baseY = introTarget.y;
        if (col != null) col.enabled = true;
        HitParticles.Spawn(transform.position, baseColor, 4);   // 落位时的一下“显形”
    }

    // 射手：靠近但保持 2.5 单位距离，避免贴脸；上下正弦浮动
    private void UpdateShooter()
    {
        float px = PlayerHealth.Instance.transform.position.x;
        float x = transform.position.x;
        if (Mathf.Abs(px - x) > 2.5f)
            x = Mathf.MoveTowards(x, px, moveSpeed * Time.deltaTime);
        float limit = Mathf.Max(3f, RunManager.RoomRight - 0.8f);
        x = Mathf.Clamp(x, -limit, limit);
        float y = baseY + Mathf.Sin(Time.time * 1.5f + bobOffset) * 0.35f;
        transform.position = new Vector3(x, y, transform.position.z);

        fireTimer -= Time.deltaTime;
        UpdateFireTelegraph();
        if (fireTimer <= 0f)
        {
            fireTimer = fireInterval;
            if (sr != null) sr.color = baseColor;
            EnemyBullet.Spawn(transform.position, PlayerHealth.Instance.transform.position, TypeName);
        }
    }

    // 开火前 0.28 秒逐渐发亮。没有预警的远程攻击在平台游戏里只能靠背板，
    // 加这一层之后，"这只马上要开枪"是可以直接看出来的。
    private void UpdateFireTelegraph()
    {
        if (sr == null) return;
        if (Time.time < hitFlashUntil) return;      // 受击闪白优先，别被预警色盖掉

        const float lead = 0.28f;
        sr.color = fireTimer <= lead
            ? Color.Lerp(baseColor, Color.white, Mathf.InverseLerp(lead, 0.02f, fireTimer))
            : baseColor;
    }

    // 冲锋兵：高速水平冲撞，靠接触扣血（玩家有无敌帧兜底，不会一帧掉光）
    private void UpdateCharger()
    {
        Vector3 target = PlayerHealth.Instance.transform.position;
        float x = Mathf.MoveTowards(transform.position.x, target.x, moveSpeed * Time.deltaTime);
        float limit = Mathf.Max(3f, RunManager.RoomRight - 0.8f);
        x = Mathf.Clamp(x, -limit, limit);
        float y = baseY + Mathf.Sin(Time.time * 6f + bobOffset) * 0.12f;
        transform.position = new Vector3(x, y, transform.position.z);

        // 接触判定按缩放后的体型收紧（玩家半宽约 0.31，冲锋怪半宽约 0.29）
        bool sameHeight = Mathf.Abs(target.y - transform.position.y) < 0.72f;
        if (sameHeight && Mathf.Abs(target.x - transform.position.x) < 0.62f)
            PlayerHealth.Instance.TakeDamage(1, TypeName);
    }

    // 炮台：不移动，每轮打三发小角度散射
    private void UpdateTurret()
    {
        float y = baseY + Mathf.Sin(Time.time * 1.2f + bobOffset) * 0.1f;
        transform.position = new Vector3(transform.position.x, y, transform.position.z);

        fireTimer -= Time.deltaTime;
        UpdateFireTelegraph();
        if (fireTimer > 0f) return;

        fireTimer = fireInterval;
        if (sr != null) sr.color = baseColor;
        Vector2 origin = transform.position;
        Vector2 toPlayer = ((Vector2)PlayerHealth.Instance.transform.position - origin).normalized;
        for (int i = -1; i <= 1; i++)
            EnemyBullet.Spawn(origin, origin + Rotate(toPlayer, i * 0.16f) * 3f, TypeName);
    }

    public void Hit(Vector2 direction, int damage = 1)
    {
        if (dying) return;                       // 死亡动画期间不再吃伤害，避免重复计数

        hp -= Mathf.Max(1, damage);
        transform.position += (Vector3)(direction.normalized * knockback);

        if (sr != null) sr.color = Color.white;
        hitFlashUntil = Time.time + flashTime;
        Invoke(nameof(RestoreColor), flashTime);

        if (hp <= 0)
        {
            dying = true;
            dieTime = 0f;
            dieBaseScale = transform.localScale;
            dieColor = baseColor;
            CancelInvoke(nameof(RestoreColor));   // 否则 0.08 秒后会把淡出中的颜色刷回去

            if (col != null) col.enabled = false;                  // 立刻不能被再次命中
            HitFeedback.Play(HitFeedback.Feel.Kill);               // 击杀才给更强的震动
            HitParticles.Spawn(transform.position, baseColor, 12); // 击杀爆散
            if (RunManager.HasInstance) RunManager.Instance.NotifyKill(this);   // 立刻推进波次
            Destroy(gameObject, DeathDuration + 0.2f);
        }
        else
        {
            HitFeedback.Play(HitFeedback.Feel.Hit);                 // 普通命中：轻抖，不卡帧
        }
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    private void RestoreColor()
    {
        if (sr != null) sr.color = baseColor;
    }
}
