using UnityEngine;

[CreateAssetMenu(menuName = "Game/Player Stats", fileName = "PlayerStats")]
public class PlayerStats : ScriptableObject
{
    [Header("跳跃：先定高度与到顶时间，其余由代码反推")]
    public float jumpHeight = 3.6f;
    public float timeToApex = 0.72f;          // 0.58 -> 0.72：上升更慢更飘，"减速上跳"更明显
    [Range(1f, 3f)] public float fallGravityMultiplier = 1.05f;   // 1.25 -> 1.05：下落几乎和上升一样慢，"加速下坠"但不突兀
    public float maxFallSpeed = 14f;          // 18 -> 14：下坠终速更低，长距离下落也有反应时间

    [Header("水平移动")]
    public float moveSpeed = 8f;
    public float groundAccel = 60f;
    public float airAccel = 30f;
    public float groundFriction = 50f;

    [Header("跟手机制")]
    public float coyoteTime = 0.12f;
    public float jumpBuffer = 0.12f;
    [Range(0f, 1f)] public float jumpCutMultiplier = 0.8f;   // 0.5 -> 0.8：松手不再砍一半速度（"一跳就落地"的真凶）

    // h=3.6, t=0.72 -> v0=2h/t=10.0, gUp=v0/t≈13.9, gDown≈14.6
    // 上升 0.72s / 下落约 0.70s，滞空约 1.42s。
    // 物理模型：上升 gUp 小（减速慢、飘），下落 gDown 略大（倍率仅 1.05，加速但不突兀）。
    // jumpCut 0.8：短按跳也有 64% 高度，既符合物理又不会"一跳就落地"。
    public float JumpVelocity => 2f * jumpHeight / Mathf.Max(0.01f, timeToApex);
    public float GravityUp => JumpVelocity / Mathf.Max(0.01f, timeToApex);
    public float GravityDown => GravityUp * fallGravityMultiplier;
}
