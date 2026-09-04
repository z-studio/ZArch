# GameModules：游戏内容模块

GameModules 用来管理“大厅、战斗、关卡、玩法”等可进入和退出的内容单元。它把模块 Scope 与内容加载绑定为一次事务：进入失败会回滚，退出时先卸载内容，再释放 Scope。

## 1. 定义模块

纯 C# 模块实现 `IGameModule`：

```csharp
using ZArch;
using ZArch.GameModules;

public sealed class BattleModule : IGameModule {
    public string Id => "battle";

    public IGameModuleRuntime Configure(ArchitectureScope scope, GameEnterContext context) {
        scope.Register<BattleModel>(new BattleModel());
        scope.Register<BattleSystem>(new BattleSystem());
        return GameModuleRuntime.Empty;
    }
}
```

`GameEnterContext` 可携带本次进入需要的参数。模块只负责注册其 Scope 内的服务，不负责直接加载场景。

## 2. 创建 Unity 模块资产

Unity 项目通常继承 `UnityGameModuleAsset`，在 Inspector 中设置：

- `Id`：模块唯一标识，例如 `battle`；
- `Scene Provider Id`：默认 `build-settings`；
- `Scene Location`：Build Settings 中的场景名或路径。

```csharp
using ZArch;
using ZArch.GameModules;
using ZArch.GameModules.Unity;
using ZArch.Unity;

[UnityEngine.CreateAssetMenu(menuName = "Game/Battle Module")]
public sealed class BattleModuleAsset : UnityGameModuleAsset {
    public override IGameModuleRuntime Configure(ArchitectureScope scope, GameEnterContext context) {
        scope.Register<BattleModel>(new BattleModel());
        scope.Register<BattleSystem>(new BattleSystem());
        return GameModuleRuntime.Empty;
    }
}
```

把创建出的资产加入 `GameModuleCatalog`。Catalog 会检查空配置与重复 Id。

## 3. 在根 Scope 注册 Host

```csharp
using ZArch;
using ZArch.GameModules;
using ZArch.GameModules.Unity;
using ZArch.Unity;

public sealed class GameBootstrap : ArchitectureBootstrap {
    [UnityEngine.SerializeField]
    private GameModuleCatalog m_ModuleCatalog;

    protected override void ConfigureRoot(ArchitectureScope root) {
        var host = new GameModuleHost(
            root,
            new UnityGameContentLoader(),
            m_ModuleCatalog);

        root.Register<IGameModuleHost>(host);
    }
}
```

默认 `UnityGameContentLoader` 使用 Build Settings Provider，以 Additive 模式加载场景。若接入 Addressables 或自研资源系统，实现并传入自己的 `IGameSceneProvider`。

## 4. 进入与退出

```csharp
using System.Threading;
using ZArch.GameModules;

var host = root.Resolve<IGameModuleHost>();

var context = new GameEnterContext(12); // Arguments 可放任意业务参数对象
await host.EnterAsync("battle", context, cancellationToken);

// 返回大厅或结束玩法
await host.ExitAsync();
```

Host 同一时间只允许一个切换操作，也只维护一个 Active 模块。已有模块处于 Active 时，必须先等待 `ExitAsync()`，再进入目标模块。

进入流程为：

1. 从 Catalog 查找模块。
2. 创建模块子 Scope，并执行 `Configure`，取得本次会话的 `IGameModuleRuntime`。
3. 通过内容加载器加载场景。
4. 将场景入口绑定到模块 Scope。
5. 调用模块返回的 `IGameModuleRuntime.EnterAsync`。
6. 成功后公布为当前模块。

任何一步失败都会卸载已加载内容并释放新 Scope，不会留下半激活模块。

模块若有必须在场景绑定后执行的逻辑（例如打开玩法 UI），应注册运行态服务：

```csharp
public sealed class BattleRuntime : IGameModuleRuntime {
    public Task EnterAsync(CancellationToken cancellationToken) {
        // 场景已经加载且 GameModuleSceneEntry 已绑定 Scope。
        return OpenBattleUiAsync(cancellationToken);
    }

    public Task ExitAsync() {
        // 此时场景和模块 Scope 仍然有效。
        return CloseBattleUiAsync();
    }
}
```

`Configure` 应直接返回运行态对象，不要把它注册进 Scope 让 Host 隐式查找。没有进入/退出逻辑时返回
`GameModuleRuntime.Empty`。退出顺序为 `Runtime.ExitAsync → 卸载内容 → DisposeAsync(GameScope)`。
退出运行态失败时会保留内容和 Scope；再次调用 `ExitAsync` 会重试未完成阶段。

## 5. 场景入口

每个模块场景必须恰好包含一个 `GameModuleSceneEntry`。继承它并在 `OnBindScope` 中绑定场景对象：

```csharp
using System;
using UnityEngine;
using ZArch;
using ZArch.GameModules.Unity;

public sealed class BattleModuleSceneEntry : GameModuleSceneEntry {
    [SerializeField] 
    private BattleHud m_Hud;
    
    [SerializeField] 
    private BattleWorld m_World;

    private IDisposable m_HudBinding;
    private IDisposable m_WorldBinding;

    protected override void OnBindScope(ArchitectureScope scope) {
        m_HudBinding = scope.Bind(m_Hud);
        m_WorldBinding = scope.Bind(m_World);
        m_Hud.BindScope(scope);
    }

    private void OnDestroy() {
        m_WorldBinding?.Dispose();
        m_HudBinding?.Dispose();
    }
}
```

这样场景对象不会自行搜索全局 Bootstrap，也不会误用根 Scope。模块 System 或 UI 可以直接调用 `scope.Resolve<BattleHud>()`、`scope.Resolve<BattleWorld>()`。

`Bind` 只建立可解析关系，不接管 Unity 对象生命周期。它允许在模块 Scope 已经 Active、场景刚加载完成时调用，因此不需要为每种场景对象预先编写 Holder 或 SceneBindings 服务。务必保存返回的句柄，并在场景入口销毁时解除绑定。

## 6. 模块事件边界

GameModule 的 Scope 天然适合作为 Scoped Event 边界：

```csharp
// 只在当前游戏模块内部通知。
this.PublishScopedEvent(new RoundEndedEvent());

// 通知整个应用，例如玩家资产或登录状态发生变化。
this.PublishEvent(new PlayerBalanceChangedEvent());
```

子 Scope 需要向模块根 Scope 或 AppScope 汇报时可以冒泡：

```csharp
this.PublishScopedEvent(
    new SubGameExitedEvent(),
    EEventPropagation.Bubble
);
```

不要让 AppScope 通过 `Publish` 向游戏模块广播；父 Scope 的 Scoped Event 不会向子 Scope 传播。大厅和游戏都需要接收的消息应使用默认 `PublishEvent`。

推荐边界：

| 消息 | 事件空间 |
| --- | --- |
| 玩家资料、货币、登录状态 | 默认事件 |
| 当前游戏的回合、选牌、局部表现 | Scoped Event |
| 子玩法向模块上层汇报 | Scoped Event + `Bubble` |
| 当前值与 UI 展示状态 | 模块 Model 中的 `BindableProperty` |

## 7. 退出策略

应用退出或测试结束时调用：

```csharp
await host.ShutdownAsync();
```

`ShutdownAsync` 会结束当前模块并阻止后续进入。Host 实现了 `IAsyncDeinitializable`；使用 `Architecture.ShutdownAsync()` 时框架会等待它完成。Unity 项目仍应在销毁 Bootstrap 前显式等待整个关闭流程。

`ExitAsync`、`ShutdownAsync` 和进入失败回滚都会通过 `DisposeAsync` 清理 GameScope，因此模块内的 `IAsyncDeinitializable` 服务会被完整等待。不要用同步方式销毁仍有活动模块的 Host。

如果 Content 卸载失败，Host 会在内部保留待清理会话。此时不能进入其他模块；修复外部加载器状态后再次调用 `ExitAsync` 会只重试尚未完成的清理阶段。待清理状态属于 Host 的实现细节，普通调用方只需安全地重复调用 `ExitAsync`。

## 8. 何时使用 GameModules

适合：

- 内容以场景或资源组为边界；
- 每个玩法有独立 Model/System 生命周期；
- 需要可验证的进入失败回滚；
- 计划替换场景加载后端。

简单项目只有一个永久场景时，直接使用根 Scope 或 `SceneScopeManager` 即可，不必引入模块层。

下一篇：[API 速查](07-API-Reference.md)
