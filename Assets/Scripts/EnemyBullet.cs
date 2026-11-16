using UnityEngine;

// 敌方子弹：朝玩家当前位置直线飞行，命中扣 1 血，超时自动销毁。
// 频率低（每敌人 1~3 秒一发），暂用 Destroy；若要上规模再池化。
public class EnemyBullet : MonoBehaviour
{
    private const float Speed = 6f;
    private const float Life = 5f;
    private const float Radius = 0.16f;

    private static Sprite sharedSprite;

    private Vector2 dir;
    private float timer;
    private string source = "敌方子弹";

    public static void Spawn(Vector2 from, Vector2 target, string source = "敌方子弹")
    {
        GameObject go = new GameObject("EnemyBullet");
        go.transform.position = from;
        go.transform.localScale = new Vector3(0.22f, 0.22f, 1f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetSprite();
        sr.color = new Color(1f, 0.4f, 0.45f, 1f);
        sr.sortingOrder = 8;

        EnemyBullet b = go.AddComponent<EnemyBullet>();
        b.dir = ((Vector2)(target - from)).normalized;
        b.timer = Life;
        b.source = source;
    }

    private void Update()
    {
        // 进场 / 暂停期间子弹停在原地（不能用 timeScale = 0，否则 Unity 会当成编辑器暂停）
        if (PauseMenu.IsPaused || RunManager.IsSpawning) return;

        transform.position += (Vector3)(dir * Speed * Time.deltaTime);
        timer -= Time.deltaTime;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, Radius);

        // 先判玩家：贴在平台边缘时应该扣血而不是"被平台吃掉"
        foreach (Collider2D hit in hits)
        {
            PlayerHealth ph = hit.GetComponent<PlayerHealth>();
            if (ph != null)
            {
                ph.TakeDamage(1, source);
                Destroy(gameObject);
                return;
            }
        }

        // 再判地形：平台和墙同样会挡住敌方子弹 —— 于是"躲在平台后面"成了有效战术
        foreach (Collider2D hit in hits)
        {
            if (hit.GetComponent<Solid>() != null)
            {
                HitParticles.Spawn(transform.position, new Color(1f, 0.55f, 0.6f, 1f), 3);
                Destroy(gameObject);
                return;
            }
        }

        if (timer <= 0f) Destroy(gameObject);
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
