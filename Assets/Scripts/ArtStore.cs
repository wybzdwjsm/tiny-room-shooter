using UnityEngine;

// 运行时加载 Resources/Art 下的角色美术，转成 Sprite 供玩家 / 敌人 / Boss 使用。
//
// 为什么不直接把图片设成 Sprite 类型再拖进场景：
//   1) 现有代码已经用 transform.localScale 统一控制体型（CharacterScale=0.62、BossScale=1.9），
//      这里用 PPU=1024 让一张 1024px 的图正好等于 1 个世界单位，原有缩放数值全部不用改；
//      生成图里角色大约只占画布中间 70%，用 ART_FILL 补偿，让可见角色和原来的方块差不多大；
//   2) Resources.Load 在 WebGL 下可用，不依赖场景里手动拖引用，旧场景也能直接生效；
//   3) 加载失败时返回 null，各调用点保留原来的方块兜底，不会因为缺图白屏。
public static class ArtStore
{
    private const float PPU = 1024f;
    // 生成图角色约占画布 70%，放大 1.35 倍让可见主体和原方块尺寸接近（碰撞体不变）
    private const float ART_FILL = 1.35f;

    private static Sprite player, shooter, charger, turret, boss;
    // 4 种 Boss 的专属贴图（按 Boss.Kind 顺序：TideEye/Spore/Spear/Abyss）
    private static Sprite[] bossVariants = new Sprite[4];
    private static bool tried;

    public static Sprite Player { get { Ensure(); return player; } }
    public static Sprite Shooter { get { Ensure(); return shooter; } }
    public static Sprite Charger { get { Ensure(); return charger; } }
    public static Sprite Turret { get { Ensure(); return turret; } }
    public static Sprite Boss { get { Ensure(); return boss; } }

    // 按 Boss 类型取贴图。
    // 优先用 Resources 里的外部专属图；若加载失败（Unity 导入问题），
    // 退回 BossArt 程序化生成的 4 种贴图——保证 4 个 Boss 外观一定不同，绝不再出现纯色方块。
    public static Sprite BossFor(int kindIndex)
    {
        Ensure();
        if (kindIndex >= 0 && kindIndex < bossVariants.Length && bossVariants[kindIndex] != null)
            return bossVariants[kindIndex];
        return BossArt.Get(kindIndex);
    }

    // 是否专属 Boss 贴图（外部变体 或 程序化生成）——用于决定显示原色还是叠 tint
    public static bool IsVariantBossSprite(Sprite s)
    {
        if (s == null) return false;
        for (int i = 0; i < 4; i++)
            if (s == BossArt.Get(i)) return true;
        for (int i = 0; i < bossVariants.Length; i++)
            if (bossVariants[i] != null && s == bossVariants[i]) return true;
        return false;
    }

    // 判断某个 Sprite 是不是我们加载的 Boss 美术（原版或 4 种变体），用于 Boss 的有图/无图分支
    public static bool IsBossSprite(Sprite s)
    {
        Ensure();
        if (s == null) return false;
        if (s == boss) return true;
        for (int i = 0; i < bossVariants.Length; i++)
            if (s == bossVariants[i]) return true;
        return false;
    }

    // 判断某个 SpriteRenderer 用的是不是我们加载的美术（而不是兜底的纯色方块）
    public static bool IsEnemySprite(Sprite s)
    {
        Ensure();
        return s != null && (s == shooter || s == charger || s == turret || s == boss);
    }

    private static void Ensure()
    {
        if (tried) return;
        tried = true;

        player = Load("player");
        shooter = Load("shooter");
        charger = Load("charger");
        turret = Load("turret");
        boss = Load("boss");                       // 原版兜底

        // 外部专属图：尝试加载但静默失败（加载不了就用 BossArt 程序化贴图）。
        // 之前反复出现"未找到"警告是因为 Unity 导入 meta 的问题，现在有兜底就不再吵用户。
        bossVariants[0] = LoadQuiet("boss_tideeye");
        bossVariants[1] = LoadQuiet("boss_spore");
        bossVariants[2] = LoadQuiet("boss_spear");
        bossVariants[3] = LoadQuiet("boss_abyss");
    }

    // 静默版加载：失败只返回 null，不打印警告
    private static Sprite LoadQuiet(string name)
    {
        Texture2D tex = Resources.Load<Texture2D>("Art/" + name);
        if (tex == null) return null;
        float ppu = (tex.width > 0 ? tex.width / 1f : PPU) / ART_FILL;
        Sprite s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f), ppu, 0u, SpriteMeshType.FullRect);
        s.name = "art_" + name;
        return s;
    }

    private static Sprite Load(string name)
    {
        Texture2D tex = Resources.Load<Texture2D>("Art/" + name);
        if (tex == null)
        {
            Debug.LogWarning("[ArtStore] 未找到 Resources/Art/" + name + "，该角色退回纯色方块");
            return null;
        }

        // 一张 1024px 图 = 1 个世界单位，保持所有既有缩放数值的含义不变
        float ppu = (tex.width > 0 ? tex.width / 1f : PPU) / ART_FILL;
        Sprite s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f), ppu, 0u, SpriteMeshType.FullRect);
        s.name = "art_" + name;
        return s;
    }
}

// ===== 程序化 Boss 贴图（内联于此文件，避免新建 .cs 触发 Unity 导入失败）=====
internal static class BossArt
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

