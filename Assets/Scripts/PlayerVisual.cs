using UnityEngine;

// 玩家辨识度：所有敌人都是没有脸的纯色方块，玩家必须有"脸"和"边"，否则一混进红色方块堆里就找不到自己。
// 做法是给玩家挂两个子物体：一圈深色描边 + 两只跟着准星转的眼睛，本体改成暖白色。
// 不引入任何新资源——描边和眼睛都复用玩家自己的那张方块精灵。
public class PlayerVisual : MonoBehaviour
{
    private static PlayerVisual instance;

    private SpriteRenderer body;
    private SpriteRenderer outline;
    private SpriteRenderer eyeL;
    private SpriteRenderer eyeR;
    private Camera cam;
    private float faceDir = 1f;

    // 暖白 + 近黑：敌人全是饱和色（红/橙/绿/蓝/紫），这两个颜色不会和任何敌人撞
    private static readonly Color BodyColor = new Color(0.98f, 0.94f, 0.85f, 1f);
    private static readonly Color InkColor = new Color(0.07f, 0.09f, 0.14f, 1f);

    private const int BodyOrder = 12;

    public static void Attach(GameObject player)
    {
        if (player == null || player.GetComponent<PlayerVisual>() != null) return;
        player.AddComponent<PlayerVisual>();
    }

    // 受伤闪烁时由 PlayerHealth 调用，让描边和眼睛一起闪，不然会出现"只剩一个轮廓在原地"
    public static void SetVisible(bool visible)
    {
        if (instance != null) instance.ApplyVisible(visible);
    }

    private void Awake()
    {
        instance = this;
        cam = Camera.main;

        body = GetComponent<SpriteRenderer>();
        if (body == null || body.sprite == null)
        {
            enabled = false;      // 没有精灵就什么都不做，不要因为美化把玩家弄消失
            return;
        }

        body.color = BodyColor;
        body.sortingOrder = BodyOrder;

        // 描边：同一张精灵放大 1.34 倍垫在下面
        outline = MakePart("Outline", body.sprite, InkColor, BodyOrder - 1);
        outline.transform.localScale = Vector3.one * 1.34f;

        // 眼睛：两小块，位置随朝向偏移
        eyeL = MakePart("EyeL", body.sprite, InkColor, BodyOrder + 1);
        eyeR = MakePart("EyeR", body.sprite, InkColor, BodyOrder + 1);
        eyeL.transform.localScale = Vector3.one * 0.15f;
        eyeR.transform.localScale = Vector3.one * 0.15f;
        UpdateEyes();

        // 让 PlayerHealth 的"复活/闪白收尾"也用这个新颜色，
        // 否则场景里原来的颜色会在下次重置时把这个白盖回去
        if (PlayerHealth.HasInstance && PlayerHealth.Instance.gameObject == gameObject)
            PlayerHealth.Instance.SetBaseColor(BodyColor);
    }

    private SpriteRenderer MakePart(string name, Sprite sprite, Color color, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    private void Update()
    {
        // 眼睛看着准星：不用看准星也能知道自己在朝哪边打。
        // 关键保护：鼠标不在 Game 视图内时 Input.mousePosition 可能是 NaN，
        // 直接喂给 ScreenToWorldPoint 会每帧抛 "Screen position out of view frustum"，
        // 把 Console 刷满红字（而且编辑器的暂停按钮会被这些异常拖住没反应）。
        if (cam != null)
        {
            Vector3 mp = Input.mousePosition;
            if (!float.IsNaN(mp.x) && !float.IsNaN(mp.y) && mp.z >= 0f)
            {
                float mx = cam.ScreenToWorldPoint(mp).x;
                if (!float.IsNaN(mx)) faceDir = mx >= transform.position.x ? 1f : -1f;
            }
        }
        UpdateEyes();
    }

    private void UpdateEyes()
    {
        if (eyeL == null || eyeR == null) return;

        const float spread = 0.13f;
        const float lift = 0.10f;
        float lean = 0.05f * faceDir;

        eyeL.transform.localPosition = new Vector3(-spread + lean, lift, 0f);
        eyeR.transform.localPosition = new Vector3(spread + lean, lift, 0f);
    }

    private void ApplyVisible(bool visible)
    {
        if (body != null) body.enabled = visible;
        if (outline != null) outline.enabled = visible;
        if (eyeL != null) eyeL.enabled = visible;
        if (eyeR != null) eyeR.enabled = visible;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
