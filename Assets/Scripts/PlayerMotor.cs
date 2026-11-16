using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMotor : MonoBehaviour
{
    [SerializeField] private PlayerStats stats;
    [SerializeField] private InputReader input;

    // 第一步先用 Everything，靠排除自身 Rigidbody 判断落地；
    // 正式项目应改成 Ground Layer 并在 Inspector 里指定。
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundCheckOffset = -0.5f;
    [SerializeField] private Vector2 groundCheckBox = new Vector2(0.6f, 0.08f);

    private Rigidbody2D rb;
    private Transform groundCheck;
    private float coyoteTimer;
    private float bufferTimer;
    private bool grounded;
    private bool jumpQueued;
    private bool cutQueued;

    // Unity 6 用 linearVelocity；Unity 2022 / 团结引擎等分支用 velocity。
    // 用版本宏自动适配，避免一打开项目就编译报错。
    private Vector2 Velocity
    {
#if UNITY_6000_0_OR_NEWER
        get { return rb.linearVelocity; }
        set { rb.linearVelocity = value; }
#else
        get { return rb.velocity; }
        set { rb.velocity = value; }
#endif
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;              // 自己算重力，才能实现非对称
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        groundCheck = new GameObject("GroundCheck").transform;
        groundCheck.SetParent(transform, false);
        groundCheck.localPosition = new Vector3(0f, groundCheckOffset, 0f);
    }

    private void Update()
    {
        if (stats == null || input == null) return;
        if (PauseMenu.IsPaused) return;
        // 标题 / 结算期间冻结；三选一期间可以自由走动（界面出问题也不会被锁死）
        if (RunManager.HasInstance && !RunManager.IsControllable) return;

        // 敌人进场期间锁住操作，同时把缓冲清空，避免解锁瞬间突然起跳
        if (RunManager.IsSpawning)
        {
            jumpQueued = false;
            cutQueued = false;
            bufferTimer = 0f;
            coyoteTimer = 0f;
            return;
        }

        grounded = CheckGrounded();

        coyoteTimer = grounded ? stats.coyoteTime : coyoteTimer - Time.deltaTime;
        bufferTimer = input.JumpPressed ? stats.jumpBuffer : bufferTimer - Time.deltaTime;

        if (bufferTimer > 0f && coyoteTimer > 0f)
        {
            bufferTimer = 0f;
            coyoteTimer = 0f;   // 必须清零，否则会意外二段跳
            jumpQueued = true;
        }

        if (input.JumpReleased && Velocity.y > 0f)
            cutQueued = true;
    }

    private bool CheckGrounded()
    {
        Collider2D[] hits = Physics2D.OverlapBoxAll(
            groundCheck.position, groundCheckBox, 0f, groundMask);

        foreach (Collider2D hit in hits)
        {
            if (hit == null || hit.isTrigger) continue;
            if (hit.attachedRigidbody == rb) continue;  // 排除玩家自己

            // 只认"顶面在脚边附近"的碰撞体。房间两侧的墙是竖直的，顶面远在头顶之上，
            // 以前 groundMask 是 Everything 会把墙当成地面，贴着墙能无限跳。
            if (hit.bounds.max.y > groundCheck.position.y + 0.35f) continue;

            return true;
        }
        return false;
    }

    private void FixedUpdate()
    {
        if (stats == null || input == null) return;
        if (PauseMenu.IsPaused) { Velocity = Vector2.zero; return; }
        if (RunManager.HasInstance && !RunManager.IsControllable) { Velocity = Vector2.zero; return; }
        if (RunManager.IsSpawning) { Velocity = Vector2.zero; return; }

        Vector2 v = Velocity;

        // 移速受词条倍率影响（+15% / +25% 之类），基础速度仍在 PlayerStats 里
        float speedMul = RunManager.HasInstance ? RunManager.Current.moveSpeedMul : 1f;
        float targetX = input.MoveX * stats.moveSpeed * speedMul;
        float accel = Mathf.Abs(input.MoveX) > 0.01f
            ? (grounded ? stats.groundAccel : stats.airAccel)
            : stats.groundFriction;
        v.x = Mathf.MoveTowards(v.x, targetX, accel * Time.fixedDeltaTime);

        float g = v.y > 0f ? stats.GravityUp : stats.GravityDown;
        v.y = Mathf.Max(v.y - g * Time.fixedDeltaTime, -stats.maxFallSpeed);

        if (jumpQueued)
        {
            jumpQueued = false;
            v.y = stats.JumpVelocity;
        }
        if (cutQueued)
        {
            cutQueued = false;
            if (v.y > 0f) v.y *= stats.jumpCutMultiplier;
        }

        Velocity = v;
    }
}
