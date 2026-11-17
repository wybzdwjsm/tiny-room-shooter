using UnityEngine;

// 通用动效：冲击环 / 枪口闪光 / 冲刺残影。
// 全部用同一张程序化生成的柔边圆精灵，不引入任何贴图资源。
// 每个实例自己驱动生命周期，到时间自动 Destroy —— 调用方只需要一行 FxRings.XXX(pos)。
//
// 为什么单独做一个组件而不是塞进 HitParticles：
//   HitParticles 是"方块向外飞散"，表达的是"打中了"；
//   冲击环是"一个圈向外扩散"，表达的是"有力作用在地面上"；
//   两者视觉语言不同，混在一起会让画面语义变乱。
public class FxRings : MonoBehaviour
{
    private enum Kind { Ring, Flash, AfterImage }

    private static Sprite softCircle;

    // 总量上限：高射速 + 多冲锋怪时特效对象会堆积，超过上限就跳过新的，
    // 避免帧率被特效本身拖垮（这是本末倒置）
    private const int MaxLive = 40;
    private static int liveCount;
    private SpriteRenderer sr;
    private Kind kind;
    private Vector3 from;
    private Vector3 to;
    private Color color;
    private float life;
    private float duration;
    private float size;
    private float stretchX = 1f;       // Flash 用：朝射击方向拉长
    private float angle;

    // 落地冲击环：一个贴地的椭圆快速扩散并淡出
    public static void Land(Vector2 pos, float strength = 1f)
    {
        if (strength < 0.05f) return;
        Spawn(pos, Kind.Ring,
            new Color(0.78f, 0.86f, 1f, 0.55f),
            0.28f, 0.25f + strength * 0.45f);
    }

    // 起跳扬尘环：比落地更小更淡，向上收
    public static void Jump(Vector2 pos)
    {
        Spawn(pos, Kind.Ring, new Color(0.72f, 0.8f, 0.95f, 0.32f), 0.22f, 0.42f);
    }

    // 空中爆发环：不压扁成正圆，用于 Boss 阶段转换 / 死亡
    public static void Burst(Vector2 pos, Color tint, float size = 1.2f)
    {
        GameObject go = Spawn(pos, Kind.Flash, tint, 0.36f, size);
        if (go == null) return;
        FxRings fx = go.GetComponent<FxRings>();
        fx.stretchX = 1f;              // 正圆，不是枪口那种拉长的火光
    }

    // 枪口闪光：短促、亮，朝射击方向拉长成火光
    public static void Muzzle(Vector2 pos, Vector2 dir, Color tint)
    {
        GameObject go = Spawn(pos, Kind.Flash, tint, 0.09f, 0.20f);
        if (go == null) return;
        FxRings fx = go.GetComponent<FxRings>();
        fx.stretchX = 1.7f;
        fx.angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        go.transform.rotation = Quaternion.Euler(0f, 0f, fx.angle);
    }

    // 冲刺残影：不是粒子，而是"上一刻的自己"淡出
    public static void AfterImage(Vector2 pos, Sprite sprite, Color tint, bool flipX)
    {
        if (sprite == null || liveCount >= MaxLive) return;
        GameObject go = new GameObject("AfterImage");
        go.transform.position = pos;
        SpriteRenderer r = go.AddComponent<SpriteRenderer>();
        r.sprite = sprite;
        r.color = new Color(tint.r, tint.g, tint.b, 0.42f);
        r.flipX = flipX;
        r.sortingOrder = 11;
        FxRings fx = go.AddComponent<FxRings>();
        fx.sr = r;
        fx.kind = Kind.AfterImage;
        fx.color = r.color;
        fx.duration = 0.16f;
        fx.from = go.transform.localScale;
        fx.to = go.transform.localScale;
        liveCount++;
    }

    private static GameObject Spawn(Vector2 pos, Kind kind, Color color, float duration, float size)
    {
        if (liveCount >= MaxLive) return null;

        GameObject go = new GameObject("Fx_" + kind);
        go.transform.position = pos;
        SpriteRenderer r = go.AddComponent<SpriteRenderer>();
        r.sprite = GetCircle();
        r.color = color;
        r.sortingOrder = 11;

        FxRings fx = go.AddComponent<FxRings>();
        fx.sr = r;
        fx.kind = kind;
        fx.color = color;
        fx.duration = duration;
        fx.size = size;
        fx.from = Vector3.one * (size * 0.35f);
        fx.to = Vector3.one * size;
        go.transform.localScale = fx.from;
        liveCount++;
        return go;
    }

    private bool counted;              // 是否已经从 liveCount 里扣过，避免重复扣

    private void Update()
    {
        life += Time.deltaTime;
        float k = Mathf.Clamp01(life / Mathf.Max(0.0001f, duration));

        switch (kind)
        {
            case Kind.Ring:
                // 先快后慢地扩散（ease-out），尾巴平滑
                float e = 1f - Mathf.Pow(1f - k, 2.5f);
                transform.localScale = Vector3.Lerp(from, to, e);
                // 压扁成贴地的椭圆，读起来是"地面上的冲击波"而不是气泡
                transform.localScale = new Vector3(transform.localScale.x, transform.localScale.y * 0.35f, 1f);
                break;

            case Kind.Flash:
                Vector3 fs = Vector3.Lerp(from, to, 1f - Mathf.Pow(1f - k, 3f));
                transform.localScale = new Vector3(fs.x * stretchX, fs.y, 1f);
                break;

            case Kind.AfterImage:
                transform.localScale = Vector3.Lerp(from, to * 1.06f, k);
                break;
        }

        if (sr != null)
            sr.color = new Color(color.r, color.g, color.b, color.a * (1f - k) * (1f - k));

        if (k >= 1f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // Update 正常结束时也会走这里；用 counted 保证只扣一次。
        // 场景切换 / 外部销毁时也能正确释放配额。
        if (!counted)
        {
            counted = true;
            liveCount = Mathf.Max(0, liveCount - 1);
        }
    }

    private static Sprite GetCircle()
    {
        if (softCircle != null) return softCircle;

        // 32x32 柔边圆： alpha 按到中心的距离平滑衰减，避免硬边像个色块
        const int n = 32;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[n * n];
        float c = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);     // smoothstep
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        softCircle = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), n);
        return softCircle;
    }
}
