# ZArch

> 面向 Unity 与 .NET 的作用域应用架构框架。

ZArch 以相互隔离的 **Host** 和层级化的 **Scope Tree** 为核心，用于组织应用的依赖、生命周期与事件。Core 不依赖 Unity，也不维护静态默认实例；每个 Host 都拥有独立的 Scope、服务和事件，因此可以在同一进程内安全地运行游戏主世界、预览环境、测试环境或多个子应用。

## 学习与文档

第一次接触架构、依赖注入或生命周期管理时，请从 [ZArch 完整学习指南](Documentation/README.md) 开始，不需要预先理解这些概念。

- [基础篇：从零完成第一个功能](Documentation/01-Beginner.md)
- [中级篇：Scope、生命周期、事件与服务容器](Documentation/02-Intermediate.md)
- [高级篇：异步、多 Host 与架构扩展](Documentation/03-Advanced.md)
- [Unity 与第三方 SDK 实战](Documentation/04-Unity-and-SDK.md)
- [常用配方与 API 选择](Documentation/05-Cookbook.md)
- [故障排查与生产检查表](Documentation/06-Troubleshooting-and-Production.md)

## 分层

```text
Assets/ZArch
├── Core/                  ZArch.Core：Host、Scope、服务容器、生命周期、事件
├── Patterns/              ZArch.Patterns：Model/System、Command/Query、BindableProperty
├── GameModules/           可选多游戏模块：GameLauncher、GameSession、Unity 场景适配
├── Documentation/         从基础到高级的完整教程、配方与上线检查表
├── Unity/                 ZArch.Unity：MonoBehaviour、Scene、Unity 类型适配
│   └── Editor/            ZArch.Unity.Editor：多 Host 调试窗口
└── Tests/Editor/          ZArch.Tests：核心行为测试
```

依赖方向固定为：

```text
ZArch.Core ← ZArch.Patterns ← ZArch.Unity ← ZArch.Unity.Editor
     └─────← ZArch.GameModules ← ZArch.GameModules.Unity
```

`ZArch.Core` 和 `ZArch.Patterns` 均启用了 `noEngineReferences`，可以脱离 Unity 使用。Patterns 是可选策略层；应用也可以只引用 Core 并建立自己的领域模式。

`ZArch.GameModules` 是可选的多游戏会话层，不依赖 Unity；`ZArch.GameModules.Unity` 使用 ScriptableObject Catalog 注册独立游戏模块，以 Additive Scene 加载游戏内容，并通过 `GameSceneEntry` 将场景 Controller 显式绑定到当前游戏 Scope。完整用法见[多游戏模块指南](Documentation/07-Multi-Game-Modules.md)。

## 设计原则

- 没有静态 `Arch`、默认 Host 或隐式全局 Scope。
- Scope 可拥有任意名称、任意深度和多个根节点，不内置 App/Mode/Scene 层级。
- 依赖从请求 Scope 开始向 Parent 查找，子 Scope 可以覆盖父级注册。
- Scope 激活后注册表被冻结，运行期变化通过创建或销毁子 Scope 表达。
- 初始化失败会回滚 Scope；销毁按子 Scope、已初始化服务、Owned 对象的反向顺序进行。
- Unity 只是适配层，不决定 Core 的生命周期或组织方式。

## 快速开始

```csharp
using ZArch;

var host = new ArchitectureHost();
host.Start();

var root = host.CreateRootScope("Game", scope => {
    scope.Register<IConfig>(new GameConfig());
    scope.RegisterFactory<IRepository>(resolver =>
        new Repository(resolver.Resolve<IConfig>()));
});

var battle = root.CreateChild("Battle", scope => {
    scope.Register<IRuleSet>(new BattleRuleSet());
});

var repository = battle.Resolve<IRepository>(); // 当前 Scope 没有时回退到 Parent

host.Shutdown(); // 也可以 host.Dispose()
```

`Architecture` 是一次性实例：`Shutdown()` 后不能再次 `Start()`；需要重新启动应用环境时创建新的 Architecture。异步创建中的 Scope 只有在全部初始化成功后才会进入 `RootScopes`、`Scopes` 或 Parent 的 `Children`。Dispose/Shutdown 会取消仍在进行的初始化，并且已销毁 Scope 不会被异步续体重新激活。

`ArchitectureHost` 是可以直接实例化的通用 Host。需要 Host 级启动逻辑时继承 `Architecture`：

```csharp
public sealed class GameArchitecture : Architecture {
    protected override void OnStart() {
        // 这里只处理 Host 自身启动；服务在 Scope setup 中注册。
    }

    protected override void OnShutdown() {
        // 释放 OnStart 创建的 Host 级资源；Scope 此时已经完成销毁。
    }
}
```

一个进程可以创建多个 `Architecture`，它们互不共享注册、Scope 或事件。

## 执行模型

ZArch Core 采用串行执行模型，不提供内部锁，也不承诺并发线程安全。`Start`、Scope 创建/解析/销毁、服务注册和事件收发必须由应用在同一个逻辑线程执行；Unity 项目应统一在主线程调用。后台任务完成后，应先切回所属执行上下文再访问 Architecture 或 Scope。

异步建 Scope API 会保留当前同步上下文。带 timeout 或外部取消需求时使用接收 `CancellationToken` 的双参数 setup 重载；取消是协作式的，setup 和 `IAsyncInitializable` 都必须观察 token。单参数异步 setup 只适合无需取消的配置工作，因此不提供 timeout 参数。

## Scope 树与解析

```text
Game
├── Lobby
└── Battle
    ├── Gameplay
    ├── UI
    └── Level-01
```

```csharp
var root = host.CreateRootScope("Game", scope => {
    scope.Register<IConfig>(defaultConfig);
});

var battle = root.CreateChild("Battle", scope => {
    scope.Register<IConfig>(battleConfig);
});

root.Resolve<IConfig>();    // defaultConfig
battle.Resolve<IConfig>();  // battleConfig
```

必需依赖使用 `Resolve<T>()`；可选依赖使用 `TryResolve<T>()`。框架按注册键精确解析，不自动映射实现类、基类或其他接口。

父 Scope Dispose 时会先按反向创建顺序 Dispose 所有子 Scope。Scope 状态为：

```text
Created → Configuring → Initializing → Active → Disposing → Disposed
                                ↓
                              Faulted
```

## 服务注册

```csharp
scope.Register<IStorage>(new LocalStorage());
scope.Register<IStorage, LocalStorage>();

scope.RegisterFactory<IInventory>(resolver =>
    new Inventory(resolver.Resolve<IStorage>()));
```

实现类型注册需要公开无参构造函数；有构造参数时使用 Factory。Scoped Factory 每个 Scope 只创建一次，并在激活阶段物化；循环依赖会被检测并抛出异常。

Transient Factory 每次解析创建一个对象：

```csharp
scope.RegisterFactory<IEnemy>(
    _ => new Enemy(),
    EServiceLifetime.Transient,
    owned: false
);
```

为保证确定性，Owned Transient 不能实现 ZArch 生命周期接口。有生命周期的服务应使用 Scoped；外部管理的临时对象应使用 `owned: false`。

Alias 让多个注册键指向同一个实例：

```csharp
scope.Register<PlayerModel, PlayerModel>();
scope.RegisterAlias<IPlayerModel, PlayerModel>();
```

默认 `owned: true`。Scope 会管理 Owned 服务的生命周期，并在结束时 Dispose 实现 `IDisposable` 的对象。

## 生命周期

Core 提供三个正交接口：

```csharp
public interface IInitializable {
    void Initialize();
}

public interface IAsyncInitializable {
    Task InitializeAsync(CancellationToken cancellationToken);
}

public interface IDeinitializable {
    void Deinitialize();
}
```

服务默认按注册顺序初始化，也可以显式指定顺序：

```csharp
scope.Register(config, initializationOrder: -100);
scope.Register(repository, initializationOrder: 0);
scope.Register(gameplay, initializationOrder: 100);
```

相同 order 按注册顺序执行，反初始化按实际初始化顺序逆序执行。如果初始化中途失败，已经完成初始化的服务会被反初始化，整个 Scope 从 Host 中移除。

同步 Scope 不接受异步初始化服务。异步服务使用异步建 Scope API：

```csharp
var online = await root.CreateChildAsync(
    "Online",
    (scope, token) => {
        scope.Register<IAccountService>(new AccountService());
        return Task.CompletedTask;
    },
    timeout: TimeSpan.FromSeconds(10),
    cancellationToken: cancellationToken
);
```

`timeout` 通过 `CancellationToken` 协作取消 setup 和异步服务初始化；异步实现必须观察传入的 token。无需取消能力时可以继续使用单参数 setup 重载。

## 事件

Architecture 事件属于单个 Host：

```csharp
IUnregister unregister = host.RegisterEvent<PlayerLoggedIn>(OnPlayerLoggedIn);
host.SendEvent(new PlayerLoggedIn());
unregister.Unregister();
```

Scope 还拥有局部事件：

```csharp
scope.RegisterEvent<DamageEvent>(OnDamage);
scope.Publish(damage);                           // 当前 Scope
scope.Publish(damage, EEventPropagation.Parents); // 当前 Scope 到根
```

`Publish` 只用于 Scope 事件；发送到当前 Host 使用 `scope.Architecture.SendEvent(message)`。Patterns 中的 `this.RegisterEvent` 与 `this.SendEvent` 都对应 Architecture 事件，注册和发送保持对称。框架没有跨 Host 的静态全局事件总线。

事件会调用本次发送开始时的全部订阅者。某个订阅者抛异常不会阻止后续订阅者或 Parent Scope；发送结束后，所有处理器异常通过 `AggregateException` 一次性抛给发送方。

## 可选 Patterns 层

`ZArch.Patterns` 提供 Model/System/Controller、Command/Query 和 BindableProperty。它们都通过所属 Scope 工作，不访问默认 Host。

```csharp
var gameplay = root.CreateChild("Gameplay", scope => {
    scope.Register<IPlayerModel>(new PlayerModel());
    scope.Register<ICombatSystem>(new CombatSystem());
});
```

Model/System 使用统一的 `Register`，框架不内置 `RegisterModel`、`RegisterSystem` 或固定初始化优先级。依赖方应排在依赖项之后注册，或通过 `initializationOrder` 明确顺序。

```csharp
public sealed class AttackCommand : AbstractCommand {
    protected override void OnExecute() {
        this.GetSystem<ICombatSystem>().Attack();
    }
}

gameplay.SendCommand(new AttackCommand());
```

Command 与 Query 是一次性操作对象，执行时由调用 Scope 注入上下文。

## Unity 集成

继承 `ArchitectureHostBootstrap` 可以让一个 GameObject 显式拥有一个 Host：

```csharp
public sealed class GameBootstrap : ArchitectureHostBootstrap {
    protected override Architecture CreateArchitecture() => new GameArchitecture();

    protected override void ConfigureRoot(ArchitectureScope scope) {
        scope.Register<IConfig>(new GameConfig());
    }
}
```

`ArchitectureController` 不会寻找全局 Scope，必须由组合根显式绑定：

```csharp
controller.BindScope(bootstrap.RootScope);
```

Scene 生命周期是可选适配策略。每个 Host 创建自己的 `SceneScopeBinder`，因此多个 Host 可以使用不同的 Scene 绑定：

```csharp
var binder = new SceneScopeBinder(bootstrap.Architecture);
binder.Bind("Battle", scope => {
    scope.Register<IBattleSession>(new BattleSession());
}, _ => bootstrap.RootScope);
binder.Enable();

// Host 结束前
binder.Dispose();
```

同名 Additive Scene 会根据 scene handle 创建独立 Scope。可以按 Scene 名称或完整 path 绑定；卸载 Scene 时对应 Scope 自动销毁。

Scene 相关的事件注销会记录具体 Scene handle，不会再因其他 Additive Scene 卸载而误注销：

```csharp
subscription.UnregisterWhenCurrentSceneUnloaded();          // 注册调用时的 Active Scene
subscription.UnregisterWhenSceneUnloaded(targetScene);      // 显式 Scene
subscription.UnregisterWhenGameObjectSceneUnloaded(gameObject); // GameObject 所属 Scene
```

`SceneScopeBinder` 应先配置 Bind 再 Enable；启用后新增 Bind 也会立即扫描当前已加载 Scene。Binder Dispose 后不可再次启用或修改绑定。

由 `GameLauncher` 管理的 GameModule Scene 不要同时注册到 `SceneScopeBinder`：Binder 采用“Scene 创建 Scope”，GameModules 采用“Scope 加载 Scene”，混用会为同一场景建立两套 Scope。

运行时可通过 `Tools/ZArch/Arch Debug` 查看当前场景中的所有 `ArchitectureHostBootstrap`，并切换 Host 检查 Scope 树、状态与服务实例。

## 测试覆盖

Editor 测试位于 `Tests/Editor`，覆盖：

- 子 Scope 覆盖与父级回退解析；
- 同 Scope 初始化依赖；
- 激活失败回滚；
- Factory 循环依赖检测；
- 异步初始化完成后才进入 Active；
- 多 Host 的服务、Scope 与事件隔离；
- 纯反初始化服务与父子 Scope 的销毁顺序；
- Shutdown/Dispose 期间禁止重入创建 Scope；
- 异常上报器抛错时仍完成清理；
- Alias 不污染父级服务上下文；
- 非法 timeout 与协作取消后的 Scope 回滚。
