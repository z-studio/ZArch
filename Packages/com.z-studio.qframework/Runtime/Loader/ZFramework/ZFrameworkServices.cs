using System;
using ZFramework.Unity;

namespace QFramework {
    /// <summary>App 常驻的基础组件访问入口，不创建或销毁 ZFramework 组件。</summary>
    public sealed class ZFrameworkServices : IUtility {
        public T Get<T>() where T : ZFrameworkUnityComponent {
            var component = ZFrameworkUnityEntry.GetComponent<T>();
            return component ? component : throw new InvalidOperationException($"ZFramework component '{typeof(T).Name}' is not configured.");
        }
    }
}
