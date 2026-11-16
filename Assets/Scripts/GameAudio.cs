using UnityEngine;

// 程序合成音效：不依赖任何音频文件，运行时用 AudioClip.Create 生成波形。
// 静态 Play 会懒加载自己，旧场景无需挂载；WebGL 下首次点击「开始」即解锁音频。
// 背景音乐同理，由 MusicSynth 合成一个 4 小节循环，走单独的 AudioSource（循环播放）。
// 按 M 静音（音效与音乐一起）。
public class GameAudio : MonoBehaviour
{
    public enum Sfx { Shoot, Hit, Hurt, Upgrade, Wave, Win, Fail }

    public static GameAudio Instance { get; private set; }
    public static bool Muted { get; private set; }

    // 音乐压低到"听得见但不抢戏"：战斗音效是主要信息源
    private const float MusicVolume = 0.20f;

    private AudioSource source;
    private AudioSource music;
    private AudioClip shoot, hit, hurt, upgrade, wave, win, fail;

    private float musicScale = 1f;          // 当前聚焦度（实际音量 = 基础 × Volume × 它）
    private float musicScaleTarget = 1f;    // 目标聚焦度，每帧趋近

    private void Awake()
    {
        Instance = this;
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.volume = 0.5f * GameSettings.Volume;

        // 名称、起始频率、结束频率、时长、音量、是否加泛音（滑音做出「上升/下降」情绪）
        shoot   = MakeClip("shoot",   900f, 880f, 0.06f, 0.30f, false);
        hit     = MakeClip("hit",     520f, 260f, 0.10f, 0.45f, true);
        hurt    = MakeClip("hurt",    220f, 110f, 0.20f, 0.55f, true);
        upgrade = MakeClip("upgrade", 420f, 980f, 0.22f, 0.40f, false);
        wave    = MakeClip("wave",    260f, 390f, 0.24f, 0.35f, false);
        win     = MakeClip("win",     440f, 1200f, 0.45f, 0.40f, false);
        fail    = MakeClip("fail",    380f, 90f,  0.50f, 0.45f, false);

        BuildMusic();
    }

    private void BuildMusic()
    {
        try
        {
            music = gameObject.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.loop = true;
            music.clip = MusicSynth.BuildLoop();
            music.volume = MusicVolume * GameSettings.Volume;
            if (!Muted) music.Play();
        }
        catch (System.Exception e)
        {
            // 合成失败不能影响玩法：没有音乐也要能玩
            Debug.LogWarning("[GameAudio] 背景音乐合成失败，已跳过：" + e.Message);
        }
    }

    private void Update()
    {
        // 音乐"聚焦度"平滑过渡：结算时压低，回到战斗时抬回来
        if (music != null)
        {
            musicScale = Mathf.MoveTowards(musicScale, musicScaleTarget, 1.6f * Time.unscaledDeltaTime);
            music.volume = MusicVolume * GameSettings.Volume * musicScale;
        }

        if (!Input.GetKeyDown(KeyCode.M)) return;

        Muted = !Muted;
        if (music != null)
        {
            music.mute = Muted;
            // 启动时就是静音状态的话，音乐从来没开始播过，取消静音要补一次 Play
            if (!Muted && !music.isPlaying) music.Play();
        }
        Debug.Log("[GameAudio] " + (Muted ? "已静音（M 恢复）" : "已开启声音"));
    }

    // 1 = 正常音量，0.3 = 压低（结算页用）。用目标值 + 每帧趋近，避免音量突变。
    public static void SetMusicFocus(float scale)
    {
        if (Instance != null) Instance.musicScaleTarget = Mathf.Clamp01(scale);
    }

    // 浏览器（尤其 WebGL）要求先有用户交互才允许播放音频。
    // 开局那一下点击/空格就是最好的时机，所以每次 BeginRun 都确认一遍。
    public static void KickMusic()
    {
        if (Muted || Instance == null) return;
        if (Instance.music != null && !Instance.music.isPlaying) Instance.music.Play();
    }

    // 需要提前有 BGM（例如标题页）时调用；不播任何音效也会创建实例
    public static void Ensure()
    {
        if (Instance == null)
        {
            GameObject go = new GameObject("GameAudio");
            Instance = go.AddComponent<GameAudio>();
        }
    }

    public static void Play(Sfx s)
    {
        if (Muted) return;
        if (Instance == null)
        {
            GameObject go = new GameObject("GameAudio");
            Instance = go.AddComponent<GameAudio>();   // Awake 同步执行，clips 立即就绪
        }
        AudioClip clip = Instance.Get(s);
        if (clip != null) Instance.source.PlayOneShot(clip);
    }

    // 设置里改音量后由 GameSettings 调用，立即生效
    public static void ApplyVolume()
    {
        if (Instance == null) return;
        if (Instance.source != null) Instance.source.volume = 0.5f * GameSettings.Volume;
        if (Instance.music != null)
            Instance.music.volume = MusicVolume * GameSettings.Volume * Instance.musicScale;
    }

    private AudioClip Get(Sfx s)
    {
        switch (s)
        {
            case Sfx.Shoot: return shoot;
            case Sfx.Hit: return hit;
            case Sfx.Hurt: return hurt;
            case Sfx.Upgrade: return upgrade;
            case Sfx.Wave: return wave;
            case Sfx.Win: return win;
            case Sfx.Fail: return fail;
            default: return null;
        }
    }

    private static AudioClip MakeClip(string name, float f0, float f1, float duration, float volume, bool overtone)
    {
        const int rate = 22050;
        int n = Mathf.Max(1, Mathf.CeilToInt(rate * duration));
        float[] data = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float k = (float)i / n;
            float freq = Mathf.Lerp(f0, f1, k);
            phase += 2f * Mathf.PI * freq / rate;
            float s = Mathf.Sin(phase);
            if (overtone) s = s * 0.7f + Mathf.Sin(phase * 2.37f) * 0.3f;
            data[i] = s * (1f - k) * volume;   // 线性衰减包络
        }
        AudioClip c = AudioClip.Create(name, n, 1, rate, false);
        c.SetData(data, 0);
        return c;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
