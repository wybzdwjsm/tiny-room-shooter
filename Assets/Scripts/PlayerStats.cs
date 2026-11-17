using UnityEngine;

[CreateAssetMenu(menuName = "Game/Player Stats", fileName = "PlayerStats")]
public class PlayerStats : ScriptableObject
{
    [Header("跳跃：先定高度与到顶时间，其余由代码反推")]
    public float jumpHeight = 3.4f;
    public float timeToApex = 0.62f;          // 0.72 -> 0.62：滞空从 1.42s 收到约 1.15s，不再飘太久
    [Range(1f, 3f)] public float fallGravityMultiplier = 1.15f;  // 1.05 -> 1.15：下落略快于上升（加速下坠），幅度温和
    public float maxFallSpeed = 15f;          // 14 -> 15

    [Header("水平移动")]
    public float moveSpeed = 6.5f;
    public float groundAccel = 60f;
    public float airAccel = 30f;
    public float groundFriction = 50f;

    [Header("跟手机制")]
    public float coyoteTime = 0.12f;
    public float jumpBuffer = 0.12f;
    [Range(0f, 1f)] public float jumpCutMultiplier = 0.8f;   // 0.5 -> 0.8：松手不再砍一半速度（"一跳就落地"的真凶）

    // h=3.4, t=0.62 -> v0=2h/t≈11.0, gUp=v0/t≈17.7, gDown≈20.4
    // 上升 0.62s / 下落约 0.58s，滞空约 1.20s（比 v2.3 的 1.42s 收短，更利落）。
    // 物理模型：上升 gUp 小（减速上跳），下落 gDown 大 15%（加速下坠，但不突兀）。
    // jumpCut 0.8：短按跳也有 64% 高度。
    public float JumpVelocity => 2f * jumpHeight / Mathf.Max(0.01f, timeToApex);
    public float GravityUp => JumpVelocity / Mathf.Max(0.01f, timeToApex);
    public float GravityDown => GravityUp * fallGravityMultiplier;
}
