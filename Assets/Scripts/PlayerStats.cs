using UnityEngine;

[CreateAssetMenu(menuName = "Game/Player Stats", fileName = "PlayerStats")]
public class PlayerStats : ScriptableObject
{
    [Header("跳跃：先定高度与到顶时间，其余由代码反推")]
    public float jumpHeight = 3.4f;
    public float timeToApex = 0.58f;          // 0.46 -> 0.58：上升更慢、滞空更久（约 1.2 秒一轮）
    [Range(1f, 3f)] public float fallGravityMultiplier = 1.25f;   // 1.35 -> 1.25：下落更飘，留出反应时间
    public float maxFallSpeed = 18f;          // 25 -> 18：终端速度降低，长距离下坠也有反应时间

    [Header("水平移动")]
    public float moveSpeed = 8f;
    public float groundAccel = 60f;
    public float airAccel = 30f;
    public float groundFriction = 50f;

    [Header("跟手机制")]
    public float coyoteTime = 0.12f;
    public float jumpBuffer = 0.12f;
    [Range(0f, 1f)] public float jumpCutMultiplier = 0.5f;

    // h=3.4u, t=0.58s -> v0=2h/t≈11.7, gUp=v0/t≈20.3, gDown≈25.3
    // 总滞空 ≈ 1.17s（原来约 0.86s）：跳得更高、飘得更久，平台间距更好读；
    // 配合 jumpCut（松手 0.5 倍）仍能做短跳，不会所有跳都变成"飘半天"。
    public float JumpVelocity => 2f * jumpHeight / Mathf.Max(0.01f, timeToApex);
    public float GravityUp => JumpVelocity / Mathf.Max(0.01f, timeToApex);
    public float GravityDown => GravityUp * fallGravityMultiplier;
}
