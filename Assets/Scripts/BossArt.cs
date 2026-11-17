using UnityEngine;

/// <summary>
/// 程序化生成 4 种 Boss 贴图，完全不依赖 Resources / .meta 文件导入。
/// 之前外部 PNG 因 Unity 导入问题反复加载失败，这个方案保证 100% 可用：
/// 用 Texture2D.SetPixels32 逐像素绘制，运行时 Sprite.Create，无文件、无缓存问题。
/// 每种 Boss 用不同的配色 + 身体轮廓 + 眼睛 + 装饰（触手/尖鳍/火焰），辨识度高。
/// </summary>
public static class BossArt
{
    private const int S = 256;                 // 贴图边长
    private static Sprite[] cached;

    public static Sprite Get(int kind)
    {
        if (cached == null) cached = new Sprite[4];
        if (kind < 0 || kind >= cached.Length) kind = 0;
        if (cached[kind] == null) cached[kind] = Build(kind);
        return cached[kind];
    }

    // 每种 Boss 的调色板
    private struct Palette
    {
        public Color body, bodyDark, eye, pupil, accent;
    }
    private static Palette PaletteOf(int k)
    {
        switch (k)
        {
            case 0: // 潮眼：粉红眼球怪
                return new Palette {
                    body = new Color(0.98f, 0.45f, 0.62f), bodyDark = new Color(0.55f, 0.12f, 0.3f),
                    eye = new Color(1f, 0.95f, 0.9f), pupil = new Color(0.15f, 0.02f, 0.1f),
                    accent = new Color(1f, 0.75f, 0.85f) };
            case 1: // 孢潮：紫色水母
                return new Palette {
                    body = new Color(0.62f, 0.4f, 0.92f), bodyDark = new Color(0.28f, 0.12f, 0.5f),
                    eye = new Color(0.85f, 1f, 0.9f), pupil = new Color(0.1f, 0.05f, 0.2f),
                    accent = new Color(0.8f, 0.55f, 1f) };
            case 2: // 裂鳍：青色海兽
                return new Palette {
                    body = new Color(0.3f, 0.9f, 0.85f), bodyDark = new Color(0.05f, 0.4f, 0.42f),
                    eye = new Color(1f, 1f, 0.85f), pupil = new Color(0.02f, 0.15f, 0.16f),
                    accent = new Color(0.6f, 1f, 0.95f) };
            default: // 深渊：深红魔眼
                return new Palette {
                    body = new Color(0.85f, 0.18f, 0.2f), bodyDark = new Color(0.3f, 0.02f, 0.05f),
                    eye = new Color(1f, 0.85f, 0.55f), pupil = new Color(0.12f, 0.0f, 0.0f),
                    accent = new Color(1f, 0.55f, 0.15f) };
        }
    }

    private static Sprite Build(int kind)
    {
        Palette p = PaletteOf(kind);
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color32[] px = new Color32[S * S];
        // 透明背景
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);

        float cx = S * 0.5f, cy = S * 0.5f;

        // —— 身体：椭圆，径向渐变（中心亮、边缘暗）——
        float rx = S * 0.30f, ry = S * (kind == 2 ? 0.24f : 0.32f);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float nx = (x - cx) / rx, ny = (y - cy) / ry;
                float d = nx * nx + ny * ny;
                if (d <= 1f)
                {
                    Color col = Color.Lerp(p.body, p.bodyDark, Mathf.Clamp01(d * 0.9f));
                    // 边缘描边 darker
                    if (d > 0.85f) col = Color.Lerp(col, p.bodyDark, (d - 0.85f) / 0.15f);
                    Set(px, x, y, col);
                }
            }
        }

        // —— 装饰（按类型不同）——
        switch (kind)
        {
            case 0: DrawTentacles(px, p.accent, 7, 0.42f); DrawHorns(px, p.accent); break;
            case 1: DrawTentacles(px, p.accent, 10, 0.5f); DrawSpores(px, p.accent); break;
            case 2: DrawFins(px, p.accent); break;
            default: DrawTentacles(px, p.accent, 12, 0.55f); DrawFlames(px, p.accent); break;
        }

        // —— 大眼睛：白色眼白 + 彩色虹膜 + 瞳孔 + 高光 ——
        float eyeR = S * (kind == 2 ? 0.13f : 0.17f);
        DrawDisc(px, cx, cy + S * 0.02f, eyeR, p.eye);
        DrawDisc(px, cx, cy + S * 0.02f, eyeR * 0.55f, p.accent);
        DrawDisc(px, cx, cy + S * 0.02f, eyeR * 0.3f, p.pupil);
        // 高光点（左上）
        DrawDisc(px, cx - eyeR * 0.32f, cy + S * 0.02f + eyeR * 0.34f, eyeR * 0.14f, Color.white);

        tex.SetPixels32(px);
        tex.Apply(false, true);   // makeNoLongerReadable 省内存
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S / 1.6f);
        sp.name = "boss_art_" + kind;
        return sp;
    }

    private static void Set(Color32[] px, int x, int y, Color c)
    {
        if (x < 0 || x >= S || y < 0 || y >= S) return;
        px[y * S + x] = c;
    }

    private static void DrawDisc(Color32[] px, float cx, float cy, float r, Color c)
    {
        int x0 = Mathf.Max(0, (int)(cx - r)), x1 = Mathf.Min(S - 1, (int)(cx + r));
        int y0 = Mathf.Max(0, (int)(cy - r)), y1 = Mathf.Min(S - 1, (int)(cy + r));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                    Set(px, x, y, c);
    }

    // 触手：从身体底部伸出的正弦曲线
    private static void DrawTentacles(Color32[] px, Color c, int count, float spread)
    {
        float cx = S * 0.5f;
        for (int t = 0; t < count; t++)
        {
            float phase = t * 1.7f;
            float startX = cx + Mathf.Lerp(-S * 0.22f, S * 0.22f, t / (float)(count - 1));
            for (int i = 0; i < 40; i++)
            {
                float tt = i / 39f;
                float y = cy0() - tt * S * spread;
                float x = startX + Mathf.Sin(tt * 6f + phase) * S * 0.05f * tt;
                float w = Mathf.Lerp(5f, 1.5f, tt);
                DrawDisc(px, x, y, w, Color.Lerp(c, new Color(c.r, c.g, c.b, 0f), tt * 0.5f));
            }
        }
    }
    private static float cy0() { return S * 0.62f; }

    // 尖角（潮眼）
    private static void DrawHorns(Color32[] px, Color c)
    {
        for (int s = -1; s <= 1; s += 2)
            for (int i = 0; i < 26; i++)
            {
                float tt = i / 25f;
                float x = S * 0.5f + s * (S * 0.2f + tt * S * 0.12f);
                float y = S * 0.32f - tt * S * 0.2f;
                DrawDisc(px, x, y, Mathf.Lerp(4f, 1f, tt), c);
            }
    }

    // 孢子点（孢潮）
    private static void DrawSpores(Color32[] px, Color c)
    {
        for (int i = 0; i < 14; i++)
        {
            float a = i * 2.399f;
            float r = S * (0.3f + (i % 3) * 0.06f);
            DrawDisc(px, S * 0.5f + Mathf.Cos(a) * r, S * 0.5f + Mathf.Sin(a) * r, 3f + (i % 2), c);
        }
    }

    // 尖鳍（裂鳍）
    private static void DrawFins(Color32[] px, Color c)
    {
        for (int s = -1; s <= 1; s += 2)
            for (int i = 0; i < 30; i++)
            {
                float tt = i / 29f;
                float x = S * 0.5f + s * (S * 0.26f + tt * S * 0.18f);
                float y = S * 0.5f + Mathf.Sin(tt * 3f) * S * 0.08f;
                DrawDisc(px, x, y, Mathf.Lerp(5f, 1f, tt), c);
            }
    }

    // 火焰（深渊）
    private static void DrawFlames(Color32[] px, Color c)
    {
        for (int t = 0; t < 9; t++)
        {
            float phase = t * 2.1f;
            float startX = S * 0.5f + Mathf.Lerp(-S * 0.24f, S * 0.24f, t / 8f);
            for (int i = 0; i < 22; i++)
            {
                float tt = i / 21f;
                float y = S * 0.34f - tt * S * 0.26f;
                float x = startX + Mathf.Sin(tt * 5f + phase) * S * 0.04f;
                DrawDisc(px, x, y, Mathf.Lerp(4f, 1f, tt), c);
            }
        }
    }
}
