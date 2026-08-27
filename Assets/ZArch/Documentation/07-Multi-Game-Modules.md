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

同一 `GameLauncher` 只维护一个活动 `GameSession`。存在活动游戏时再次 `EnterAsync` 会直接报错；必须先等待 `ExitAsync` 完成，再进入另一个游戏，确保不会同时保留两套游戏 Scope、场景和资源。

`UnityGameContentLoader` 使用可插拔的场景 Provider，并统一以 `LoadSceneMode.Additive` 加载游戏场景。保存 `AppBootstrap` 的启动场景应常驻，游戏场景由 Launcher 加载和卸载。Module 和 Launcher 不直接依赖具体资源系统。

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
Id:                game-1
Scene Provider Id: build-settings
Scene Location:    Game1
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

Catalog 会在启动时校验空引用、空 ID、重复 ID 和空场景位置；配置错误会阻止 RootScope 启动，避免进入游戏后才暴露问题。Provider 是否已经注册由 `UnityGameContentLoader` 在进入游戏时检查。

## 5. 在 AppBootstrap 注册 Launcher

```csharp
using System;
using System.Threading.Tasks;
using ZArch;
using ZArch.GameModules;
using ZArch.GameModules.Unity;
using UnityEngine;

public sealed class AppBootstrap : ArchitectureHostBootstrap {
    [SerializeField]
    private GameModuleCatalog m_GameCatalog;
    private IGameLauncher m_GameLauncher;

    protected override bool RequiresExplicitShutdown => true;

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
        m_GameLauncher = new GameLauncher(
            scopeFactory,
            contentLoader,
            m_GameCatalog
        );

        root.Register<IGameLauncher>(m_GameLauncher);
    }

    public async Task ShutdownAppAsync() {
        try {
            if (m_GameLauncher != null) {
                await m_GameLauncher.ShutdownAsync();
            }
        } finally {
            m_GameLauncher = null;
            ShutdownArchitecture();
        }
    }
}
```

`AppBootstrap` 只知道 `GameModuleCatalog` 和 `UnityGameModuleAsset` 基类，没有出现任何 Game1/Game2 类型，所以它可以继续留在 App 程序集。将 `GameModuleCatalog.asset` 拖到启动场景中 `AppBootstrap` 的 `Game Catalog` 字段即可。

`GameLauncher` 不持有 RootScope；只有 `GameScopeFactory` 知道 GameScope 的父节点。ScopeFactory 和 ContentLoader 只是 Launcher 的组装依赖，不需要注册成业务可解析服务。大厅和游戏 Controller 通过自己已绑定的 Scope 向父级解析 `IGameLauncher`，Bootstrap 只在受控关闭时保留私有引用。

`RequiresExplicitShutdown` 会在 Bootstrap 未经过 `ShutdownAppAsync()` 就被销毁时输出错误。同步 `OnDestroy` 仍会执行最后的 Scope 兜底清理，但它不能代替第三方资源系统的异步卸载。

## 6. 选择场景 Provider

`UnityGameContentLoader` 负责公共流程：按模块配置选择 Provider、加载场景、查找唯一的 `GameSceneEntry`、绑定 GameScope，并在失败时通过同一个 Provider 回滚。Provider 只负责自己的资源系统及其原生 Handle 生命周期。

未传参数时自动注册内置的 `BuildSettingsGameSceneProvider`：

```csharp
var contentLoader = new UnityGameContentLoader();
```

它使用 `SceneManager.LoadSceneAsync`，因此场景必须启用在 Build Settings 中。新建 Module Asset 的 Provider ID 默认填写为 `build-settings`；如果该字段被清空，Catalog 校验会直接报错。

### 6.1 混合多个资源系统

每种资源系统实现 `IGameSceneProvider`，加载结果实现 `IGameSceneHandle`：

```csharp
public sealed class ProjectSceneHandle : IGameSceneHandle {
    public Scene Scene { get; }

    // 必须同时保存 Addressables AsyncOperationHandle、
    // YooAsset SceneHandle 或项目自己的 Bundle lease。
}

public sealed class ProjectSceneProvider : IGameSceneProvider {
    public string Id => "project-assets";

    public Task<IGameSceneHandle> LoadAsync(
        string location,
        CancellationToken cancellationToken
    ) {
        // 使用资源系统加载 Additive Scene，并返回持有原生 Handle 的对象。
    }

    public Task UnloadAsync(IGameSceneHandle handle) {
        // 使用同一个原生 Handle 卸载并释放引用。
    }
}
```

Bootstrap 注册所有要使用的 Provider：

```csharp
var contentLoader = new UnityGameContentLoader(
    new BuildSettingsGameSceneProvider(),
    new AddressablesGameSceneProvider(),
    new YooAssetGameSceneProvider(gamePackage)
);
```

显式传入 Provider 后不会再隐式追加 Build Settings Provider；需要混用时像上面一样明确注册。Provider ID 区分大小写，并且必须非空、唯一。

Module Asset 对应配置：

```text
Build Settings
Scene Provider Id: build-settings
Scene Location:    Assets/Games/Game1.unity

Addressables
Scene Provider Id: addressables
Scene Location:    game-2-main-scene

YooAsset
Scene Provider Id: yooasset
Scene Location:    Assets/Games/Game3.unity
```

Addressables 适配器应在 Handle 中保存加载得到的 `AsyncOperationHandle<SceneInstance>`，卸载时将该 Handle 交还给 `Addressables.UnloadSceneAsync`。YooAsset 适配器同样保存 `SceneHandle`，并使用当前安装版本对应的卸载/释放 API。它们应放在引用相应资源包的项目程序集或可选适配程序集里，`ZArch.GameModules.Unity` 本身不硬依赖任何第三方包。

Provider 加载收到取消请求时，如果底层操作已经无法取消，应等待加载完成后立即卸载并释放原生 Handle，再抛出取消异常，避免泄漏 Bundle 或引用计数。卸载不接受取消；一旦开始清理就必须完成。

GameModule Scene 不要再注册到 `SceneScopeBinder`。GameLauncher 已经创建 GameScope 并由 `GameSceneEntry` 显式绑定；Binder 再介入会为同一 Scene 创建第二个 Scope。

## 7. 显式绑定场景 Controller

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

## 8. 从大厅进入游戏

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

## 9. 退出游戏

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

普通返回大厅使用 `ExitAsync()`。需要销毁 Bootstrap 或重建整个 App 时，调用并等待组合根的 `ShutdownAppAsync()`：它先让 Launcher 取消/等待正在进入的游戏、卸载当前内容并 Dispose GameScope，然后才同步 Shutdown Architecture。

```csharp
await appBootstrap.ShutdownAppAsync();
Destroy(appBootstrap.gameObject);
```

直接 `Destroy` Bootstrap 或直接调用 `Architecture.Shutdown()` 只是同步兜底，无法等待 Addressables/YooAsset 场景释放。

## 10. 行为约束

- Module ID 区分大小写，并且必须唯一、非空。
- 同一 Launcher 同时只允许一个 Enter/Exit；重入会抛出 `InvalidOperationException`。
- 存在活动 Session 时不能再次 Enter；必须严格 `ExitAsync → EnterAsync`。
- 新 Session 创建、服务初始化、场景加载或 Entry 绑定失败时会回滚新 Scope。
- `ShutdownAsync` 会阻止新操作、取消并等待正在进行的 Enter，再清理当前 Session。
- Game 内部事件优先使用 Scope 事件；Architecture 事件属于整个 App Host。
- ZArch 管理对象和场景生命周期，不会在运行时卸载 Unity managed assembly。
