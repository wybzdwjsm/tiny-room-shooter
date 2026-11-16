#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildHelper
{
    // W4：一键打包 WebGL，产物 Builds/WebGL 可直接压缩上传到 itch.io
    [MenuItem("Tools/Build WebGL (for itch.io)")]
    public static void BuildWebGL()
    {
        // 1) 编译中禁止打包：否则 BuildPipeline 会直接中止并只报一个 "Unknown"
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorUtility.DisplayDialog("还不能打包",
                "Unity 正在编译脚本或导入资源。\n\n等右下角的转圈结束（Console 不再刷新）后再点一次。", "好");
            Debug.LogWarning("[Build] 已跳过：脚本正在编译，等编译结束再打包。");
            return;
        }

        // 2) 没装 WebGL 模块时给出可执行的指引，而不是让构建失败
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            Debug.LogError("[Build] 当前编辑器没有 WebGL Build Support 模块。\n"
                + "打开 Tuanjie Hub → 安装 → 该版本右侧「管理/齿轮」→ 添加模块 → 勾选 WebGL Build Support → 安装，然后重试。");
            return;
        }

        const string scenePath = "Assets/Scenes/W4Scene.unity";
        if (!File.Exists(scenePath))
        {
            Debug.LogError("[Build] 请先执行 Tools → Build W4 Scene 生成场景。");
            return;
        }

        const string outDir = "Builds/WebGL";
        Directory.CreateDirectory(outDir);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = outDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log("[Build] WebGL 打包成功：" + Path.GetFullPath(outDir)
                + "，大小 " + (summary.totalSize / 1024UL / 1024UL) + " MB，用时 "
                + summary.totalTime.TotalSeconds.ToString("0") + " 秒");
            EditorUtility.RevealInFinder(Path.GetFullPath(outDir));
            return;
        }

        // 3) 失败时把真实原因打出来，而不是只留一个 result
        Debug.LogError("[Build] 打包失败：" + summary.result
            + "，错误 " + summary.totalErrors + " 个，用时 "
            + summary.totalTime.TotalSeconds.ToString("0") + " 秒");

        bool printed = false;
        foreach (BuildStep step in report.steps)
        {
            foreach (BuildStepMessage msg in step.messages)
            {
                if (msg.type != LogType.Error && msg.type != LogType.Exception) continue;
                Debug.LogError("[Build] " + step.name + " :: " + msg.content);
                printed = true;
            }
        }
        if (!printed)
            Debug.LogError("[Build] 构建步骤没有返回错误详情，请把 Console 里这一行之前的红字一起发出来。");
    }
}
#endif
