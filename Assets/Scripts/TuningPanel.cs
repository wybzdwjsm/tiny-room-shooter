using UnityEngine;

// 调试用的运行时调参面板：默认隐藏，按 F1 切换。
// 位置在屏幕左侧中部，避开顶部 HUD、屏幕正中的卡片和底部提示；
// 当标题 / 三选一 / 结算等模态界面出现时自动隐藏，保证任何情况下都不重叠。
public class TuningPanel : MonoBehaviour
{
    [SerializeField] private PlayerStats stats;
    private bool visible;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) visible = !visible;
    }

    private void OnGUI()
    {
        if (stats == null || !visible) return;
        if (RunManager.IsModal) return;

        float y = Mathf.Clamp((Screen.height - 165f) * 0.5f, 90f, Screen.height - 190f);
        GUILayout.BeginArea(new Rect(10f, y, 300f, 165f), GUI.skin.box);
        GUILayout.Label("手感调参（F1 开关 · 实时生效）");

        stats.jumpHeight = Slider("跳跃高度", stats.jumpHeight, 1f, 6f);
        stats.timeToApex = Slider("到顶时间", stats.timeToApex, 0.15f, 0.8f);
        stats.fallGravityMultiplier = Slider("下落重力倍率", stats.fallGravityMultiplier, 1f, 3f);

        GUILayout.Label(
            $"v0 = {stats.JumpVelocity:0.0}   gUp = {stats.GravityUp:0.0}   gDown = {stats.GravityDown:0.0}");
        GUILayout.EndArea();
    }

    private float Slider(string label, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label} {value:0.00}", GUILayout.Width(140));
        float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(140));
        GUILayout.EndHorizontal();
        return v;
    }
}
