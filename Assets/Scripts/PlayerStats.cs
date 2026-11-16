using UnityEngine;

[CreateAssetMenu(menuName = "Game/Player Stats", fileName = "PlayerStats")]
public class PlayerStats : ScriptableObject
{
    [Header("跳跃：先定高度与到顶时间，其余由代码反推")]
    public float jumpHeight = 3.2f;
    public float timeToApex = 0.46f;          // 0.35 -> 0.46：上升更慢、滞空更久
    [Range(1f, 3f)] public float fallGravityMultiplier = 1.35f;   // 1.6 -> 1.35：下落不再“砸下去”
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

    // h = 3.2u, t = 0.46s -> v0 = 2h/t ≈ 13.9, gUp = v0/t ≈ 30.2, gDown ≈ 40.8
    // 总滞空 ≈ 0.86s（原来约 0.63s），跳跃手感更“飘”，平台间距也更容易读
    public float JumpVelocity => 2f * jumpHeight / Mathf.Max(0.01f, timeToApex);
    public float GravityUp => JumpVelocity / Mathf.Max(0.01f, timeToApex);
    public float GravityDown => GravityUp * fallGravityMultiplier;
}
