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

### 池化 UIForm

会被对象池复用的 UIForm 可以继承 `ReusableArchitectureController`。关闭时必须显式 `UnbindScope`，它会先清理绑定期间加入 `UnregisterList` 的订阅，再允许绑定另一个 Scope：

```csharp
public sealed class ShopForm : ReusableArchitectureController {
    public void Open(ArchitectureScope scope) {
        BindScope(scope);

        this.SubscribeEvent<ShopChangedEvent>(Refresh).AddToUnregisterList(this);
    }

    public void Close() {
        UnbindScope();
    }

    private void Refresh(ShopChangedEvent _) { }
}
```

默认 `ArchitectureController` 仍然保持一次绑定。只有对象确实会经历“关闭但不销毁、随后进入另一 Scope”时才使用可复用版本。`UnbindScope` 只管理加入其 `UnregisterList` 的绑定期资源，因此池化 Controller 的订阅应显式调用 `AddToUnregisterList(this)`。

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

Scope Dispose 会自动解除默认 Event 和 Scoped Event 的 Patterns 订阅；Unity 自动注销仍然有价值，因为 GameObject 可能早于 Scope 被销毁或禁用。

ZArch 的 Unity 入口按主线程串行模型设计。Game Framework 或网络模块从后台线程回调时，应先切回 Unity 主线程，再访问 Controller、Architecture、Scope 或事件 API。

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

场景中的 Setting、Camera 或 MonoBehaviour 由 Unity 持有，不应注册为 Scope 生命周期服务。可以在场景入口临时绑定：

```csharp
using System;
using UnityEngine;
using ZArch;

public sealed class BattleSceneEntry : MonoBehaviour {
    [SerializeField] private BattleSetting m_Setting;
    [SerializeField] private BattleWorld m_World;

    private IDisposable m_SettingBinding;
    private IDisposable m_WorldBinding;

    public void BindScope(ArchitectureScope scope) {
        m_SettingBinding = scope.Bind(m_Setting);
        m_WorldBinding = scope.Bind(m_World);
    }

    private void OnDestroy() {
        m_WorldBinding?.Dispose();
        m_SettingBinding?.Dispose();
    }
}
```

绑定完成后，System、Controller 和其他服务仍然通过 `scope.Resolve<BattleSetting>()`、`scope.Resolve<BattleWorld>()` 获取对象。解绑不会销毁 Unity 对象。

## 5. BindableProperty 在 Unity 中的比较规则

Unity Runtime 会在场景加载前为常见 Unity 类型注册比较器。例如 `float` 使用 `Mathf.Approximately`，可以减少浮点微小误差导致的重复通知。单个属性仍可以使用 `WithComparer` 覆盖全局规则。

## 6. 调试 Scope 树

运行时可以用 `ArchitectureDebug.Capture(architecture)` 获取当前架构快照。编辑器中的 Architecture Debug 窗口用于查看 Scope 层级、服务、外部绑定、事件和状态；`Services` 与 `Bindings` 会分组展示。

排查问题时优先确认：

1. Bootstrap 是否已经执行并成功创建根 Scope。
2. Controller 是否已绑定仍处于 Active 状态的 Scope。
3. 服务是否注册在当前 Scope 或祖先 Scope。
4. 场景卸载后是否仍有对象引用旧 Scope。

下一篇：[异步生命周期](05-Async-Lifecycle.md)
