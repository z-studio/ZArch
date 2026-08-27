# 大厅与多游戏模块

本扩展适用于一个常驻 App、大厅和多个独立游戏程序集。它不改变 ZArch 的显式 Scope 原则，也不会通过静态 Host 或场景层级猜测 Controller 属于哪个 Scope。

## 1. 运行结构

```text
AppRootScope
├── 公共服务：账号、音频、网络、配置
├── LobbyScope
└── GameSession.Scope
    └── 当前游戏的 Model、System、Utility
```

同一 `GameLauncher` 只维护一个活动 `GameSession`。切换游戏时先创建并加载新 Session；如果失败，旧 Session 保持活动；成功后才卸载并 Dispose 旧 Session。

Unity 游戏场景始终使用 `LoadSceneMode.Additive`。保存 `AppBootstrap` 的启动场景应常驻，游戏场景由 Launcher 加载和卸载。

## 2. 程序集依赖

下面的 `A → B` 表示 **A.asmdef 引用 B.asmdef**：

```text
App   → ZArch.Core + ZArch.Unity
        + ZArch.GameModules + ZArch.GameModules.Unity

Game1 → App + ZArch.Core + ZArch.GameModules + ZArch.GameModules.Unity
Game2 → App + ZArch.Core + ZArch.GameModules + ZArch.GameModules.Unity
```

`Game1` 和 `Game2` 依赖 App 提供的账号、音频、网络、配置等公共能力；App 不引用任何具体游戏，Game1/Game2 也不互相引用。

具体游戏不是通过 C# 中的 `new Game1Module()` 组装，而是通过 Unity Asset 引用连接：

```text
Boot Scene
└── AppBootstrap
    └── GameModuleCatalog.asset
        ├── Game1Module.asset   类型来自 Game1 程序集
        └── Game2Module.asset   类型来自 Game2 程序集
```

Asset 序列化引用不会让 App.asmdef 反向引用 Game1/Game2.asmdef，因此不会形成程序集循环。

`App.asmdef` 示例：

```json
{
    "name": "App",
    "references": [
        "ZArch.Core",
        "ZArch.Unity",
        "ZArch.GameModules",
        "ZArch.GameModules.Unity"
    ]
}
```

`Game1.asmdef` 示例：

```json
{
    "name": "Game1",
    "references": [
        "App",
        "ZArch.Core",
        "ZArch.Patterns",
        "ZArch.Unity",
        "ZArch.GameModules",
        "ZArch.GameModules.Unity"
    ]
}
```

`Game2.asmdef` 采用相同引用方向。大厅如果就在 App 中，不需要额外的 Lobby 程序集。

## 3. 声明游戏模块

每个游戏程序集提供一个继承 `UnityGameModuleAsset` 的 ScriptableObject 类型。Module Asset 是无运行状态的模块描述，一次实际运行由 `GameSession` 表示。

```csharp
using ZArch;
using ZArch.GameModules;
using ZArch.GameModules.Unity;
using UnityEngine;

[CreateAssetMenu(menuName = "Games/Game1 Module")]
public sealed class Game1ModuleAsset : UnityGameModuleAsset {
    public override void Configure(
        ArchitectureScope scope,
        GameLaunchContext context
    ) {
        var arguments = context.GetArguments<Game1Arguments>();

        scope.Register<IGame1Model>(new Game1Model(arguments));
        scope.Register<IGame1System>(new Game1System());
    }
}
```

在 Unity 中选择 `Assets → Create → Games → Game1 Module`，然后配置继承自基类的字段：

```text
Id:                 game-1
Scene Name Or Path: Game1
```

Game2 使用同样方式创建 `Game2ModuleAsset` 和 `Game2Module.asset`。Module ID 区分大小写且必须唯一。

异步初始化应放在注册服务的 `IAsyncInitializable.InitializeAsync` 中。`GameScopeFactory` 使用异步 Scope API，服务全部初始化成功后才加载场景。

## 4. 创建 GameModuleCatalog

在 Unity 中选择：

```text
Assets → Create → ZArch → Game Module Catalog
```

将 Module Asset 拖入 `Modules` 数组：

```text
GameModuleCatalog.asset
└── Modules
    ├── Game1Module.asset
    └── Game2Module.asset
```

Catalog 会在启动时校验空引用、空 ID、重复 ID 和空场景名；配置错误会阻止 RootScope 启动，避免进入游戏后才暴露问题。

## 5. 在 AppBootstrap 注册 Launcher

```csharp
using System;
using ZArch;
using ZArch.GameModules;
using ZArch.GameModules.Unity;
using UnityEngine;

public sealed class AppBootstrap : ArchitectureHostBootstrap {
    [SerializeField]
    private GameModuleCatalog m_GameCatalog;

    protected override Architecture CreateArchitecture() =>
        new AppArchitecture();

    protected override void ConfigureRoot(ArchitectureScope root) {
        if (m_GameCatalog == null) {
            throw new InvalidOperationException("Game module catalog is missing.");
        }

        m_GameCatalog.Validate();

        root.Register<IAccountService>(new AccountService());
        root.Register<IAudioService>(new AudioService());

        var scopeFactory = new GameScopeFactory(root);
        var contentLoader = new UnityGameContentLoader();
        var launcher = new GameLauncher(
            scopeFactory,
            contentLoader,
            m_GameCatalog
        );

        root.Register<IGameScopeFactory>(scopeFactory);
        root.Register<IGameContentLoader>(contentLoader);
        root.Register<IGameLauncher>(launcher);
    }
}
```

`AppBootstrap` 只知道 `GameModuleCatalog` 和 `UnityGameModuleAsset` 基类，没有出现任何 Game1/Game2 类型，所以它可以继续留在 App 程序集。将 `GameModuleCatalog.asset` 拖到启动场景中 `AppBootstrap` 的 `Game Catalog` 字段即可。

`GameLauncher` 不持有 RootScope；只有 `GameScopeFactory` 知道 GameScope 的父节点。大厅和游戏 Controller 通过自己已绑定的 Scope 向父级解析 `IGameLauncher`，不要再从 Bootstrap 暴露 Launcher 属性。

## 6. 显式绑定场景 Controller

每个游戏场景必须有且只有一个 `GameSceneEntry`。Entry 明确列出属于该游戏 Scope 的 Controller：

```csharp
using ZArch;
using ZArch.GameModules.Unity;
using UnityEngine;

public sealed class Game1Entry : GameSceneEntry {
    [SerializeField] private BattleController m_Battle;
    [SerializeField] private BattleHudController m_Hud;

    protected override void OnBindScope(ArchitectureScope scope) {
        m_Battle.BindScope(scope);
        m_Hud.BindScope(scope);
    }
}
```

`UnityGameContentLoader` 加载场景后找到 Entry，并调用一次 `BindScope`。框架不会使用 `FindFirstObjectByType<AppBootstrap>()` 或静态默认 Host。

场景对象的 `Awake` 早于运行期 Scope 绑定。不要在 Controller 的 `Awake` 中 Resolve；在 Entry 完成绑定后调用业务初始化，或等待用户输入、`Start` 之后的明确流程。

## 7. 从大厅进入游戏

大厅 Controller 仍然使用现有的显式绑定：

```csharp
public sealed class LobbyController : ArchitectureController {
    public async void EnterGame1() {
        try {
            var launcher = GetScope().Resolve<IGameLauncher>();

            await launcher.EnterAsync(
                "game-1",
                new GameLaunchContext(
                    new Game1Arguments("room-001")
                )
            );
        } catch (System.Exception exception) {
            UnityEngine.Debug.LogException(exception);
        }
    }
}
```

`async void` 只用于 Unity Button 等事件边界，并且必须捕获异常。普通业务方法应返回 `Task`。

## 8. 退出游戏

```csharp
public async void BackToLobby() {
    try {
        var launcher = GetScope().Resolve<IGameLauncher>();
        await launcher.ExitAsync();
    } catch (System.Exception exception) {
        UnityEngine.Debug.LogException(exception);
    }
}
```

退出顺序固定为：卸载游戏内容，然后 Dispose GameScope。即使内容卸载抛出异常，Scope 仍然会被 Dispose，`Current` 也会清空。

应用主动销毁 Bootstrap 前应先等待 `ExitAsync` 完成。应用退出时，RootScope 会兜底 Dispose 当前游戏 Scope，但同步 Shutdown 不会等待 Unity 的异步场景卸载。

## 9. 行为约束

- Module ID 区分大小写，并且必须唯一、非空。
- 同一 Launcher 同时只允许一个 Enter/Exit；重入会抛出 `InvalidOperationException`。
- 新 Session 创建、服务初始化、场景加载或 Entry 绑定失败时会回滚新 Scope。
- 切换成功前旧 Session 保持活动，因此切换过程中可能短暂同时存在两个兄弟 GameScope。
- Game 内部事件优先使用 Scope 事件；Architecture 事件属于整个 App Host。
- ZArch 管理对象和场景生命周期，不会在运行时卸载 Unity managed assembly。
