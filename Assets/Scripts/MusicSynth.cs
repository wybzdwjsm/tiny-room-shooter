using UnityEngine;

// 程序化背景音乐：仍然不依赖任何音频文件，运行时合成为一个可无缝循环的片段。
// 结构是一个 4 小节的和声进行 i - VI - III - VII（A 小调：Am - F - C - G），
// 分四层叠出来：低音（每拍）、琶音（八分音符）、底鼓（1/3 拍）、反拍 hi-hat。
//
// 为什么不用 System.Random：噪声层也要可复现，否则每次进 Play 的 hi-hat 都不一样，
// 调音色时无法判断到底是自己改的参数起作用还是随机变了。这里用固定种子的 LCG。
public static class MusicSynth
{
    public const int Rate = 22050;

    private const float Bpm = 104f;
    private const int Bars = 4;

    public static AudioClip BuildLoop()
    {
        float beat = 60f / Bpm;
        float total = beat * 4f * Bars;
        int n = Mathf.Max(1, Mathf.RoundToInt(Rate * total));
        float[] buf = new float[n];

        // A 小调 i - VI - III - VII 的根音（Hz）
        float[] roots = { 110.00f, 87.31f, 130.81f, 98.00f };
        // 琶音的音级：根音、五度、八度、小十度、十二度，再折回来
        int[] arp = { 0, 7, 12, 15, 19, 15, 12, 7 };

        uint seed = 0x9E3779B9u;

        for (int bar = 0; bar < Bars; bar++)
        {
            float t0 = bar * beat * 4f;
            float root = roots[bar];

            // 低音：每拍一下，八度在下，撑住和声
            for (int q = 0; q < 4; q++)
                AddBass(buf, t0 + q * beat, beat * 0.85f, root * 0.5f, 0.30f);

            // 琶音：八分音符，做律动和亮度
            for (int s = 0; s < 8; s++)
            {
                float f = root * 2f * Mathf.Pow(2f, arp[s] / 12f);
                AddPluck(buf, t0 + s * beat * 0.5f, beat * 0.42f, f, 0.15f);
            }

            // 节奏：底鼓在 1、3 拍；hi-hat 在每拍的反面
            for (int q = 0; q < 4; q++)
            {
                if (q == 0 || q == 2) AddKick(buf, t0 + q * beat, 0.28f);
                AddHat(buf, t0 + q * beat + beat * 0.5f, 0.085f, ref seed);
            }
        }

        // 循环接缝：首尾各 25ms 做一次极短淡入淡出，避免听到"咔"的一下
        int fade = Mathf.Max(1, Mathf.RoundToInt(Rate * 0.025f));
        for (int i = 0; i < fade; i++)
        {
            float g = (float)i / fade;
            buf[i] *= g;
            buf[n - 1 - i] *= g;
        }

        // 软限幅：叠加后个别位置可能超过 1，硬截断会变成刺耳的失真
        for (int i = 0; i < n; i++) buf[i] = SoftClip(buf[i]);

        AudioClip clip = AudioClip.Create("music_loop", n, 1, Rate, false);
        clip.SetData(buf, 0);
        return clip;
    }

    // 低音：正弦 + 少量二次谐波，慢慢衰减，听起来像拨弦贝斯
    private static void AddBass(float[] buf, float start, float dur, float freq, float amp)
    {
        int i0 = Mathf.RoundToInt(start * Rate);
        int len = Mathf.RoundToInt(dur * Rate);
        float phase = 0f;

        for (int i = 0; i < len; i++)
        {
            int idx = i0 + i;
            if (idx < 0 || idx >= buf.Length) break;

            float k = (float)i / len;
            float env = Mathf.Exp(-3.2f * k) * (1f - k);   // 快起慢落
            phase += 2f * Mathf.PI * freq / Rate;

            float s = Mathf.Sin(phase) * 0.85f + Mathf.Sin(phase * 2f) * 0.15f;
            buf[idx] += s * env * amp;
        }
    }

    // 琶音：三角波近似（正弦加奇次谐波），衰减更快，像拨片
    private static void AddPluck(float[] buf, float start, float dur, float freq, float amp)
    {
        int i0 = Mathf.RoundToInt(start * Rate);
        int len = Mathf.RoundToInt(dur * Rate);
        float phase = 0f;

        for (int i = 0; i < len; i++)
        {
            int idx = i0 + i;
            if (idx < 0 || idx >= buf.Length) break;

            float k = (float)i / len;
            float env = Mathf.Exp(-6.5f * k) * (1f - k);
            phase += 2f * Mathf.PI * freq / Rate;

            float s = Mathf.Sin(phase) * 0.75f + Mathf.Sin(phase * 3f) * 0.25f;
            buf[idx] += s * env * amp;
        }
    }

    // 底鼓：频率从 130Hz 迅速扫到 45Hz，衰减 0.18 秒
    private static void AddKick(float[] buf, float start, float amp)
    {
        int i0 = Mathf.RoundToInt(start * Rate);
        int len = Mathf.RoundToInt(0.18f * Rate);
        float phase = 0f;

        for (int i = 0; i < len; i++)
        {
            int idx = i0 + i;
            if (idx < 0 || idx >= buf.Length) break;

            float k = (float)i / len;
            float freq = Mathf.Lerp(130f, 45f, Mathf.Sqrt(k));
            phase += 2f * Mathf.PI * freq / Rate;

            float env = Mathf.Exp(-7f * k);
            buf[idx] += Mathf.Sin(phase) * env * amp;
        }
    }

    // hi-hat：白噪声 + 极短包络（2 个采样平滑一下，避免高频毛刺）
    private static void AddHat(float[] buf, float start, float amp, ref uint seed)
    {
        int i0 = Mathf.RoundToInt(start * Rate);
        int len = Mathf.RoundToInt(0.05f * Rate);
        float prev = 0f;

        for (int i = 0; i < len; i++)
        {
            int idx = i0 + i;
            if (idx < 0 || idx >= buf.Length) break;

            seed = seed * 1664525u + 1013904223u;
            float noise = ((seed >> 8) & 0xFFFF) / 32768f - 1f;   // [-1, 1)
            float s = noise * 0.5f + prev * 0.5f;                 // 一阶低通，去掉最刺耳的部分
            prev = s;

            float k = (float)i / len;
            float env = Mathf.Exp(-18f * k);
            buf[idx] += s * env * amp;
        }
    }

    // tanh 近似软限幅：小信号几乎线性，大信号平滑压住
    private static float SoftClip(float x)
    {
        const float k = 1.6f;
        return (float)System.Math.Tanh(x * k) * 0.92f;
    }
}
