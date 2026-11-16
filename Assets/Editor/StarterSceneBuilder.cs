#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class StarterSceneBuilder
{
    private const string ArtFolder = "Assets/Art";
    private const string DataFolder = "Assets/Data";
    private const string ScenesFolder = "Assets/Scenes";
    private const string PngPath = "Assets/Art/White.png";
    private const string StatsPath = "Assets/Data/PlayerStats.asset";
    private const string ScenePath = "Assets/Scenes/StarterScene.unity";

    [MenuItem("Tools/Build Starter Scene")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[StarterScene] 请先停止 Play 模式，再执行 Build Starter Scene。");
            return;
        }

        EnsureFolder(ArtFolder);
        EnsureFolder(DataFolder);
        EnsureFolder(ScenesFolder);

        Sprite white = GetOrCreateWhiteSprite();

        PlayerStats stats = AssetDatabase.LoadAssetAtPath<PlayerStats>(StatsPath);
        if (stats == null)
        {
            stats = ScriptableObject.CreateInstance<PlayerStats>();
            AssetDatabase.CreateAsset(stats, StatsPath);
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        Camera cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 6f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.11f, 0.12f, 0.15f, 1f);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        camGo.AddComponent<AudioListener>();

        Color platformColor = new Color(0.35f, 0.42f, 0.55f, 1f);
        MakeBox("Platform_A", new Vector3(-3f, -2f, 0f), new Vector2(4f, 0.5f), platformColor, white);
        MakeBox("Platform_B", new Vector3(0f, -1.2f, 0f), new Vector2(2f, 0.5f), platformColor, white);
        MakeBox("Platform_C", new Vector3(3f, -0.4f, 0f), new Vector2(2f, 0.5f), platformColor, white);

        GameObject player = MakeBox("Player", new Vector3(-3f, -1f, 0f), Vector2.one,
            new Color(0.95f, 0.55f, 0.25f, 1f), white);
        Rigidbody2D rb = player.AddComponent<Rigidbody2D>();
        rb.freezeRotation = true;

        InputReader input = player.AddComponent<InputReader>();
        PlayerMotor motor = player.AddComponent<PlayerMotor>();
        SerializedObject so = new SerializedObject(motor);
        so.FindProperty("stats").objectReferenceValue = stats;
        so.FindProperty("input").objectReferenceValue = input;
        so.ApplyModifiedPropertiesWithoutUndo();

        TuningPanel panel = player.AddComponent<TuningPanel>();
        SerializedObject so2 = new SerializedObject(panel);
        so2.FindProperty("stats").objectReferenceValue = stats;
        so2.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = player;

        Debug.Log("[StarterScene] 完成，按 Play 运行：A/D 移动，Space 跳跃，轻点 Space 跳得矮。");
    }

    private static GameObject MakeBox(string name, Vector3 pos, Vector2 size, Color color, Sprite sprite)
    {
        GameObject go = new GameObject(name);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        go.AddComponent<BoxCollider2D>();
        return go;
    }

    private static Sprite GetOrCreateWhiteSprite()
    {
        // 团结引擎 / Unity 默认的 Sprite PPU 是 100，因此生成 100x100 像素贴图，
        // 刚好等于 1 世界单位；若用 1x1 贴图，方块只有 0.01 单位，肉眼看不见。
        const int size = 100;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(PngPath, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(PngPath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(PngPath) as TextureImporter;
        if (importer != null && importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 100f;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PngPath);
        if (sprite == null)
            Debug.LogError("[StarterScene] White.png 未导入为 Sprite，请检查其 Texture Type。");
        else
            Debug.Log("[StarterScene] 贴图世界尺寸 = " + sprite.bounds.size.x + " 单位（应为 1）");
        return sprite;
    }

    [MenuItem("Tools/Build W2 Scene")]
    public static void BuildW2()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[W2] 请先停止 Play 模式，再执行 Build W2 Scene。");
            return;
        }

        Build();   // 先复用 W1 灰盒

        Sprite white = GetOrCreateWhiteSprite();
        GameObject player = GameObject.Find("Player");
        if (player != null && player.GetComponent<Weapon>() == null)
            player.AddComponent<Weapon>();

        MakeBox("Enemy_1", new Vector3(0f, -0.45f, 0f), Vector2.one,
            new Color(0.85f, 0.25f, 0.3f, 1f), white).AddComponent<Enemy>();

        GameObject camGo = GameObject.Find("Main Camera");
        if (camGo != null && camGo.GetComponent<HitFeedback>() == null)
            camGo.AddComponent<HitFeedback>();

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/Scenes/W2Scene.unity");
        AssetDatabase.SaveAssets();
        Debug.Log("[W2] 完成：按 Play，鼠标指向红色敌人，按 J 或鼠标左键开火。");
    }

    [MenuItem("Tools/Build W3 Scene")]
    public static void BuildW3()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[W3] 请先停止 Play 模式，再执行 Build W3 Scene。");
            return;
        }

        Build();   // 复用 W1 灰盒（W3 的敌人由 RunManager 运行时生成）

        GameObject player = GameObject.Find("Player");
        if (player != null && player.GetComponent<Weapon>() == null)
            player.AddComponent<Weapon>();

        GameObject camGo = GameObject.Find("Main Camera");
        if (camGo != null && camGo.GetComponent<HitFeedback>() == null)
            camGo.AddComponent<HitFeedback>();

        GameObject runGo = new GameObject("RunManager");
        runGo.AddComponent<RunManager>();

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/Scenes/W3Scene.unity");
        AssetDatabase.SaveAssets();
        Debug.Log("[W3] 完成：按 Play，清怪后三选一，3 波后结算，可同 seed 重开。");
    }

    [MenuItem("Tools/Build W4 Scene")]
    public static void BuildW4()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[W4] 请先停止 Play 模式，再执行 Build W4 Scene。");
            return;
        }

        BuildW3();   // 复用 W3 场景（含 Weapon / HitFeedback / RunManager）

        GameObject player = GameObject.Find("Player");
        if (player != null && player.GetComponent<JumpCurveRecorder>() == null)
            player.AddComponent<JumpCurveRecorder>();

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/Scenes/W4Scene.unity");
        AssetDatabase.SaveAssets();
        Debug.Log("[W4] 完成：跳几次后按 K 导出跳跃曲线 CSV；Tools → Build WebGL 可打包。");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
