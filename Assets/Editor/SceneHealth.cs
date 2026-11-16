#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 场景体检：定位并清理"引用的脚本丢失"的组件。
// 这类警告通常来自旧场景里被删掉/改名的脚本，或者从别的项目拷过来的对象。
public static class SceneHealth
{
    [MenuItem("Tools/诊断/列出缺失脚本")]
    public static void ListMissing()
    {
        int count = 0;
        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (!go.scene.IsValid()) continue;      // 跳过 Project 里的资源，只看场景对象

            Component[] comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] != null) continue;
                count++;
                Debug.LogWarning("[缺失脚本] 对象「" + go.name + "」上有 " + (i + 1) + " 号组件找不到脚本", go);
            }
        }

        if (count == 0) Debug.Log("[缺失脚本] 当前场景没有缺失脚本。若 Console 仍有警告，多半来自其它场景或预制体。");
        else Debug.Log("[缺失脚本] 共 " + count + " 处。点日志可以把对应对象高亮出来，确认后运行「清理缺失脚本」。");
    }

    [MenuItem("Tools/诊断/清理缺失脚本（当前场景）")]
    public static void CleanMissing()
    {
        int removed = 0;
        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (!go.scene.IsValid()) continue;

            int n = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            if (n > 0)
            {
                removed += n;
                Debug.Log("[缺失脚本] 从「" + go.name + "」移除了 " + n + " 个丢失组件", go);
            }
        }

        if (removed > 0)
        {
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[缺失脚本] 共清理 " + removed + " 个，记得 Ctrl+S 保存场景。");
        }
        else
        {
            Debug.Log("[缺失脚本] 当前场景没有可清理的丢失组件。");
        }
    }
}
#endif
