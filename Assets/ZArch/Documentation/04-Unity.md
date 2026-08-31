# Unity 集成

ZArch 的 Unity 层只负责把纯 C# 架构接入 Unity 生命周期。核心容器、事件和 Model/System 不依赖 `MonoBehaviour`，因此可以直接进行 EditMode 测试。

## 1. 创建启动入口

在首个场景中放置一个继承 `ArchitectureBootstrap` 的组件：

```csharp
using ZArch;
using ZArch.Unity;

public sealed class GameBootstrap : ArchitectureBootstrap {
    protected override void ConfigureRoot(ArchitectureScope scope) {
        scope.Register<IPlayerRepository>(new PlayerRepository());
        scope.Register<PlayerModel>(new PlayerModel());
        scope.Register<PlayerSystem>(new PlayerSystem());
    }
}
```

组件在 `Awake` 中创建并启动 `Architecture`，然后建立根 Scope。默认会跨场景保留，并在销毁时关闭架构。

常用覆写项：

| 成员 | 默认值 | 用途 |
| --- | --- | --- |
| `RootScopeName` | `App` | 根 Scope 名称 |
| `DontDestroy` | `true` | 是否调用 `DontDestroyOnLoad` |
| `RequiresExplicitShutdown` | `false` | 未显式关闭时是否报告错误 |
| `CreateArchitecture()` | 新 `Architecture` | 需要自定义 Architecture 子类时覆写 |
| `ConfigureRoot(scope)` | 必须实现 | 注册根服务 |

如果设置 `RequiresExplicitShutdown = true`，必须在自己的退出流程中调用受保护的 `ShutdownArchitecture()`。

## 2. 给 Controller 绑定 Scope

需要访问架构的组件继承 `ArchitectureController`：

```csharp
using UnityEngine;
using ZArch;
using ZArch.Unity;

public sealed class PlayerPanel : ArchitectureController {
    public void Initialize(ArchitectureScope scope) {
        BindScope(scope);

        this.SubscribeEvent<PlayerChangedEvent>(Refresh)
            .UnregisterWhenGameObjectDestroyed(gameObject);

        Refresh(default);
    }

    private void Refresh(PlayerChangedEvent _) {
        var model = this.GetModel<PlayerModel>();
        // 刷新 UI
    }
}
```

Controller 必须在使用扩展方法前调用 `BindScope(activeScope)`。推荐由场景入口或对象工厂统一绑定：

```csharp
public sealed class SceneEntry : MonoBehaviour {
    [SerializeField] 
    private PlayerPanel m_PlayerPanel;

    public void Initialize(ArchitectureScope scope) {
        m_PlayerPanel.Initialize(scope);
    }
}
```

一个 Controller 只能绑定一次；尝试绑定另一个 Scope 会抛出异常。这样可以避免场景对象意外持有已经释放的旧 Scope。

## 3. 自动解除注册

ZArch 为 `IUnregister` 提供了 Unity 生命周期扩展：

```csharp
this.SubscribeEvent<PlayerChangedEvent>(Refresh)
    .UnregisterWhenGameObjectDestroyed(gameObject);

this.SubscribeEvent<PanelRefreshEvent>(RefreshPanel)
    .UnregisterWhenDisabled(this);

this.SubscribeEvent<SceneStateChangedEvent>(OnSceneStateChanged)
    .UnregisterWhenGameObjectSceneUnloaded(gameObject);
```

| 扩展方法 | 解除时机 | 适合场景 |
| --- | --- | --- |
| `UnregisterWhenGameObjectDestroyed` | GameObject/Component 销毁 | 与对象寿命相同的监听 |
| `UnregisterWhenDisabled` | Behaviour 禁用 | 仅激活时接收事件的面板 |
| `UnregisterWhenCurrentSceneUnloaded` | 当前激活场景卸载 | 跟随当前场景的普通对象 |
| `UnregisterWhenSceneUnloaded(scene)` | 指定场景卸载 | 已知目标场景 |
| `UnregisterWhenGameObjectSceneUnloaded` | 对象所属场景卸载 | Additive Scene 中的对象 |

`OnEnable` 中注册的监听通常配合 `UnregisterWhenDisabled`，并在每次重新启用时重新注册；只在初始化时注册一次的监听通常配合销毁或场景卸载。

`SubscribeEvent` 监听默认的 Architecture 范围事件。如果组件只应接收所属场景或模块 Scope 的消息，改用：

```csharp
this.SubscribeScopedEvent<SelectionChangedEvent>(RefreshSelection)
    .UnregisterWhenGameObjectDestroyed(gameObject);
```

Scope Dispose 会自动解除 Scoped Event 订阅；Unity 自动注销仍然有价值，因为 GameObject 可能早于 Scope 被销毁或禁用。

## 4. 场景 Scope

`SceneScopeManager` 可以让场景加载时创建 Scope、卸载时自动释放：

```csharp
using ZArch;
using ZArch.Unity;

public sealed class GameBootstrap : ArchitectureBootstrap {
    private SceneScopeManager m_SceneScopeManager;

    protected override void ConfigureRoot(ArchitectureScope root) {
        root.Register<GameSettings>(new GameSettings());
    }

    protected override void Awake() {
        base.Awake();

        m_SceneScopeManager = new SceneScopeManager(Architecture);
        m_SceneScopeManager.Bind(
            "Battle",
            battle =>
            {
                battle.Register<BattleModel>(new BattleModel());
                battle.Register<BattleSystem>(new BattleSystem());
            },
            _ => RootScope);
        m_SceneScopeManager.Enable();
    }

    protected override void OnDestroy() {
        m_SceneScopeManager?.Dispose();
        base.OnDestroy();
    }
}
```

第三个参数返回父 Scope。省略它时会创建独立的根 Scope；返回 `RootScope` 时会创建根 Scope 的子 Scope。绑定标识既可以是场景名，也可以是场景路径。

`SceneScopeManager` 管理 Scope 生命周期，但不会自动寻找并绑定所有 Controller。场景入口仍应显式决定哪些对象使用哪个 Scope。

## 5. BindableProperty 在 Unity 中的比较规则

Unity Runtime 会在场景加载前为常见 Unity 类型注册比较器。例如 `float` 使用 `Mathf.Approximately`，可以减少浮点微小误差导致的重复通知。单个属性仍可以使用 `WithComparer` 覆盖全局规则。

## 6. 调试 Scope 树

运行时可以用 `ArchitectureDebug.Capture(architecture)` 获取当前架构快照。编辑器中的 Architecture Debug 窗口用于查看 Scope 层级、服务注册和状态。

排查问题时优先确认：

1. Bootstrap 是否已经执行并成功创建根 Scope。
2. Controller 是否已绑定仍处于 Active 状态的 Scope。
3. 服务是否注册在当前 Scope 或祖先 Scope。
4. 场景卸载后是否仍有对象引用旧 Scope。

下一篇：[异步生命周期](05-Async-Lifecycle.md)
