using UnityEngine;

// 命中粒子：运行时生成几个小方块向外飞散并淡出，不依赖 ParticleSystem 或贴图资源。
// 方向用「索引 + 命中位置」做确定性伪随机，不引入新的随机源，避免破坏 seed 复现。
public class HitParticles : MonoBehaviour
{
    private const float Life = 0.4f;
    private const float Size = 0.12f;      // 角色缩小后粒子也跟着收，冲击感才协调
    private static Sprite sharedSprite;

    private SpriteRenderer sr;
    private Vector2 vel;
    private Color color;
    private float t;

    public static void Spawn(Vector2 pos, Color color, int count = 5)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = i * 2.1f + pos.x * 1.7f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            GameObject go = new GameObject("HitParticle");
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * Size;
            SpriteRenderer r = go.AddComponent<SpriteRenderer>();
            r.sprite = GetSprite();
            r.color = color;
            r.sortingOrder = 12;

            HitParticles p = go.AddComponent<HitParticles>();
            p.sr = r;
            p.color = color;
            p.vel = dir * (2.2f + (i % 3) * 1.1f);
        }
    }

    private void Update()
    {
        t += Time.deltaTime;
        transform.position += (Vector3)(vel * Time.deltaTime);
        vel *= 0.9f;

        float k = Mathf.Clamp01(1f - t / Life);
        sr.color = new Color(color.r, color.g, color.b, k);
        transform.localScale = Vector3.one * (Size * (0.4f + 0.6f * k));
        if (t >= Life) Destroy(gameObject);
    }

    private static Sprite GetSprite()
    {
        if (sharedSprite != null) return sharedSprite;
        const int size = 8;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] px = new Color[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        sharedSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        return sharedSprite;
    }
}
