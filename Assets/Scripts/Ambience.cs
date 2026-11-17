using UnityEngine;

// 环境氛围：房间里缓慢漂浮的尘埃 / 潮气泡泡。
//
// 设定上这是「潮水淹没的世界」，空气里飘着的东西让静止的房间有"还在呼吸"的感觉；
// 同时给玩家的提灯一个参照物——只有画面里有微光在动，"灯是唯一的光"才成立。
//
// 用固定数量的对象池（不 Instantiate/Destroy），位置用确定性伪随机推进，
// 不引入新的 System.Random，避免破坏 seed 复现。
public class Ambience : MonoBehaviour
{
    private const int Count = 18;
    private static Sprite dotSprite;

    private Transform[] dots;
    private float[] speeds;
    private float[] phases;
    private float left, right, bottom, top;

    public static void Ensure(Camera cam)
    {
        if (cam == null) return;
        if (FindObjectOfType<Ambience>() != null) return;   // Unity 2022 / 团结引擎兼容写法

        GameObject go = new GameObject("Ambience");
        Ambience a = go.AddComponent<Ambience>();
        a.Build(cam);
    }

    private void Build(Camera cam)
    {
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;
        left = -halfW - 1f;
        right = halfW + 1f;
        bottom = -halfH - 1f;
        top = halfH + 1f;

        dots = new Transform[Count];
        speeds = new float[Count];
        phases = new float[Count];

        for (int i = 0; i < Count; i++)
        {
            GameObject d = new GameObject("Mote_" + i);
            d.transform.SetParent(transform, false);
            SpriteRenderer sr = d.AddComponent<SpriteRenderer>();
            sr.sprite = GetDot();
            sr.color = i % 3 == 0
                ? new Color(1f, 0.85f, 0.55f, 0.5f)   // 暖光尘（靠近灯的）
                : new Color(0.55f, 0.68f, 0.9f, 0.32f); // 冷潮气泡
            sr.sortingOrder = -50;                       // 在背景之上、游戏对象之下

            float k = i / (float)Count;
            d.transform.position = new Vector3(
                Mathf.Lerp(left, right, Fract(k * 7.13f)),
                Mathf.Lerp(bottom, top, Fract(k * 3.71f)),
                0f);
            float s = 0.05f + Fract(k * 11.3f) * 0.07f;
            d.transform.localScale = Vector3.one * s;

            dots[i] = d.transform;
            speeds[i] = 0.12f + Fract(k * 5.7f) * 0.28f;
            phases[i] = k * 6.28f;
        }
    }

    private void Update()
    {
        if (PauseMenu.IsPaused || (RunManager.HasInstance && RunManager.IsSpawning)) return;

        float dt = Time.deltaTime;
        for (int i = 0; i < Count; i++)
        {
            Transform t = dots[i];
            Vector3 p = t.position;

            // 缓慢上浮 + 横向摆动，像尘埃在空气里被轻微气流带着走
            p.y += speeds[i] * dt;
            p.x += Mathf.Sin(Time.time * 0.7f + phases[i]) * 0.12f * dt;

            // 飘出顶部就从底部回来，循环不产生新对象
            if (p.y > top)
            {
                p.y = bottom;
                p.x = Mathf.Lerp(left, right, Fract(phases[i] * 0.37f + Time.time * 0.013f));
            }

            t.position = p;
        }
    }

    private static float Fract(float v) { return v - Mathf.Floor(v); }

    private static Sprite GetDot()
    {
        if (dotSprite != null) return dotSprite;

        const int n = 16;
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
                px[y * n + x] = new Color(1f, 1f, 1f, a * a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        dotSprite = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), n);
        return dotSprite;
    }
}
