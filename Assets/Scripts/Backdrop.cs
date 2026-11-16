using UnityEngine;

// 背景：程序生成的竖向渐变，铺满相机视野并挂在相机下面，永远在最底层。
// 纯代码生成，不依赖任何美术资源，WebGL 打包也不会丢。
public class Backdrop : MonoBehaviour
{
    private const int Height = 64;
    private static readonly Color Bottom = new Color(0.17f, 0.13f, 0.23f, 1f);
    private static readonly Color Top = new Color(0.05f, 0.06f, 0.10f, 1f);

    public static void Ensure(Camera cam)
    {
        if (cam == null) return;
        if (cam.GetComponentInChildren<Backdrop>() != null) return;

        GameObject go = new GameObject("Backdrop");
        go.transform.SetParent(cam.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, 5f);
        go.transform.localRotation = Quaternion.identity;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = BuildSprite();
        sr.color = Color.white;
        sr.sortingOrder = -100;      // 排在所有游戏对象后面

        Backdrop b = go.AddComponent<Backdrop>();
        b.cam = cam;
        b.Fit();
    }

    private Camera cam;

    private void LateUpdate()
    {
        Fit();     // 窗口改大小 / 相机移动时自动铺满
    }

    private void Fit()
    {
        if (cam == null) cam = GetComponentInParent<Camera>();
        if (cam == null) return;

        float h = cam.orthographicSize * 2f * 1.05f;
        float w = h * cam.aspect;
        // 贴图是 1×64 像素、PPU = 1，所以世界尺寸是 1×64，缩放到铺满视野
        transform.localScale = new Vector3(w, h / Height, 1f);
    }

    private static Sprite BuildSprite()
    {
        Texture2D tex = new Texture2D(1, Height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < Height; y++)
        {
            float k = (float)y / (Height - 1);        // 0 = 底部，1 = 顶部
            tex.SetPixel(0, y, Color.Lerp(Bottom, Top, k));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, 1f, Height), new Vector2(0.5f, 0.5f), 1f);
    }
}
