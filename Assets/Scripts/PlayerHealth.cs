using System.Collections;
using UnityEngine;

// 玩家血量：受伤扣血、短暂无敌帧、闪烁反馈，血量归零触发失败结算。
// 另外负责「掉出地图」：低于 fallLimitY 就扣 1 血并送回出生点，避免无限下坠卡死。
// 由 RunManager 在 Start 时自动挂到 Player 上，不需要重建场景。
public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }
    public static bool HasInstance { get { return Instance != null; } }

    [SerializeField] private int maxHp = 5;
    [SerializeField] private float invincibleTime = 0.8f;
    [SerializeField] private float fallLimitY = -7f;

    private int hp;
    private float invincibleUntil;
    private SpriteRenderer sr;
    private Color baseColor;
    private bool dead;
    private Rigidbody2D rb;
    private Vector3 spawnPos;

    public int Hp { get { return hp; } }
    public bool IsDead { get { return dead; } }

    // 最大生命 = 基础值 + 词条加成（可能为负）
    public int MaxHp
    {
        get
        {
            int bonus = RunManager.HasInstance ? RunManager.Current.maxHpBonus : 0;
            return Mathf.Max(1, maxHp + bonus);
        }
    }

    // 词条把上限调低后，当前血量不能超过它
    public void ClampToMax()
    {
        if (hp > MaxHp) hp = MaxHp;
    }

    // Unity 6 用 linearVelocity；Unity 2022 / 团结引擎用 velocity —— 和 PlayerMotor 保持同一套版本适配
    private Vector2 Velocity
    {
        get
        {
            if (rb == null) return Vector2.zero;
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }
        set
        {
            if (rb == null) return;
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = value;
#else
            rb.velocity = value;
#endif
        }
    }

    private void Awake()
    {
        Instance = this;
        sr = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        baseColor = sr != null ? sr.color : Color.white;
        spawnPos = transform.position;
        hp = maxHp;
    }

    private void Update()
    {
        if (dead) return;
        if (PauseMenu.IsPaused) return;
        if (!RunManager.HasInstance || !RunManager.IsPlaying) return;
        if (transform.position.y > fallLimitY) return;

        // 掉出边界：回到出生点、清速度、扣 1 血（扣血本身会触发震动与音效）
        Teleport(spawnPos);
        TakeDamage(1, "掉出平台");
    }

    public bool Heal(int amount)
    {
        if (dead || hp >= MaxHp) return false;
        hp = Mathf.Min(MaxHp, hp + amount);
        HitParticles.Spawn(transform.position, new Color(0.45f, 1f, 0.6f, 1f), 5);
        return true;
    }

    // 受伤闪烁状态：根节点 sr 由 PlayerVisual 接管，这里只记录并转发给视觉层
    private bool bodyVisible = true;

    private void ApplyVisible()
    {
        // 根节点 sr 由 PlayerVisual 关掉以避免重影，闪烁统一交给视觉层处理；
        // 只有 PlayerVisual 不存在时才退回直接开关根节点精灵
        if (PlayerVisual.HasInstance) PlayerVisual.SetVisible(bodyVisible);
        else if (sr != null) sr.enabled = bodyVisible;
    }

    // PlayerVisual 会把本体换成角色美术，颜色基准要跟着更新，
    // 否则下次 ResetHealth / 复活会把场景里的旧颜色盖回去
    public void SetBaseColor(Color c)
    {
        baseColor = c;
        if (sr != null) sr.color = c;
        PlayerVisual.SetTint(c);       // 同步到真正的美术精灵
    }

    public void ResetHealth()
    {
        hp = MaxHp;
        dead = false;
        invincibleUntil = 0f;
        bodyVisible = true;
        if (sr != null) sr.color = baseColor;
        ApplyVisible();
        StopAllCoroutines();
        Teleport(spawnPos);
    }

    // 每局地图随机生成，出生点由 RunManager 指定
    public void SetSpawn(Vector3 pos)
    {
        spawnPos = pos;
        Teleport(pos);
    }

    // 每关开始回到出生点（房间正中），并清掉残余速度
    public void RespawnAtSpawn()
    {
        Teleport(spawnPos);
    }

    public void TakeDamage(int amount, string source = "敌方子弹")
    {
        if (dead || Time.time < invincibleUntil) return;
        if (RunManager.HasInstance && !RunManager.IsPlaying) return;

        if (RunManager.HasInstance) RunManager.Instance.NotifyDamaged(amount, source);

        hp -= amount;
        invincibleUntil = Time.time + invincibleTime;
        HitFeedback.Play(HitFeedback.Feel.Hurt);   // 受伤才有命中停顿 + 较强震动
        GameAudio.Play(GameAudio.Sfx.Hurt);
        HitParticles.Spawn(transform.position, new Color(1f, 0.4f, 0.45f, 1f), 6);
        StartCoroutine(Flash());

        if (hp <= 0)
        {
            dead = true;
            bodyVisible = true;
            ApplyVisible();
            if (sr != null) sr.color = new Color(0.45f, 0.45f, 0.5f, 1f);
            Velocity = Vector2.zero;

            // 死亡表现：一次更明显的爆散 + 二次震动，让"我死了"这件事在画面上先成立，
            // 结算页随后盖上来。以前只有 sprite 变灰，配上结算页会显得很突兀。
            HitParticles.Spawn(transform.position, new Color(1f, 0.78f, 0.35f, 1f), 16);
            HitFeedback.Play(HitFeedback.Feel.Hurt);

            if (RunManager.HasInstance) RunManager.Instance.Finish(false);
        }
    }

    private void Teleport(Vector3 pos)
    {
        transform.position = pos;
        Velocity = Vector2.zero;
    }

    // 外部（例如宽高比变化后重新贴边）要求把玩家挪到某个位置时用这个，
    // 顺带清速度，避免直接改 transform 被 Rigidbody2D 的插值覆盖
    public void PlaceAt(Vector3 pos)
    {
        Teleport(pos);
    }

    private IEnumerator Flash()
    {
        float t = invincibleTime;
        while (t > 0f && !dead)
        {
            bodyVisible = !bodyVisible;
            ApplyVisible();
            t -= 0.08f;
            yield return new WaitForSecondsRealtime(0.08f);
        }
        bodyVisible = true;
        ApplyVisible();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
