using UnityEngine;

// 地形标记：地板、墙、悬浮平台都挂这个组件。
// 子弹靠它区分"打到地形"和"打到角色"——没有用 LayerMask，因为层是项目级配置，
// 而这些东西全是运行时 new 出来的，挂组件比拼 Layer 更不容易出错，也不会和用户的层设置冲突。
public class Solid : MonoBehaviour
{
}
