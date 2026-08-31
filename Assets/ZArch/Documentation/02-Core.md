# 02 Architecture、Scope 与服务

Core 是 ZArch 的运行时底座，不依赖 Unity。它负责隔离运行环境、组织服务、执行生命周期和传播事件。

## 1. Architecture

`Architecture` 表示一个独立应用或世界：

```csharp
using var architecture = new Architecture();
architecture.Start();

var root = architecture.CreateRootScope("App", _ => { });
```

一个进程可以创建多个 Architecture。它们不共享服务、Scope 或事件。

`Shutdown()` 后实例不能再次 `Start()`：

```csharp
architecture.Shutdown();

// 需要重新运行时创建新实例。
using var nextArchitecture = new Architecture();
nextArchitecture.Start();
```

需要自定义架构级启动逻辑时继承 `Architecture`：

```csharp
public sealed class GameArchitecture : Architecture {
    protected override void OnStart() {
        CreateRootScope("Infrastructure", scope => {
            scope.Register<IClock>(new SystemClock());
        });
    }

    protected override void OnShutdown() { }
}
```

执行 `OnStart` 时 Architecture 已处于 Started 状态，可以创建 Scope、订阅事件和使用其他 Architecture API。启动失败时，已创建的 Scope 会自动回滚；如果启动和回滚同时失败，调用方会收到包含两者的 `AggregateException`。

Unity 项目通常仍把根 Scope 组装放在 Bootstrap 的 `ConfigureRoot` 中；`OnStart` 更适合不使用 Bootstrap 的宿主，或真正属于 Architecture 子类的基础设施。

## 2. Scope 树

Scope 同时表达服务查找边界和生命周期边界：

```text
App
├── Lobby
└── Battle
    ├── Gameplay
    └── UI
```

```csharp
var app = architecture.CreateRootScope("App", scope => {
    scope.Register<IConfig>(new AppConfig());
});

var battle = app.CreateChild("Battle", scope => {
    scope.Register<IRuleSet>(new BattleRuleSet());
});
```

解析从当前 Scope 开始，没有时向 Parent 查找：

```csharp
var config = battle.Resolve<IConfig>();
var rules = battle.Resolve<IRuleSet>();
```

子 Scope 可以覆盖父级注册：

```csharp
var test = app.CreateChild("Test", scope => {
    scope.Register<IConfig>(new TestConfig());
});
```

父 Scope 的服务仍绑定父 Scope，不会因为被子级解析而改变上下文。

## 3. 注册服务

### 实例

```csharp
scope.Register<IStorage>(new LocalStorage());
```

默认 `owned: true`，Scope 会管理初始化和释放。

外部拥有的对象：

```csharp
scope.RegisterExternal<IAnalytics>(externalAnalytics);
```

`RegisterExternal` 只把对象加入解析表，不调用 `SetScope`、初始化、反初始化或 `Dispose`。适合直接注册 Game Framework Component、SDK 单例以及由其他容器管理的对象。底层 `Register(..., owned: false)` 仍可使用，但它仍会注入 Scope；外部对象优先使用语义更明确的 `RegisterExternal`。

### 实现类型

```csharp
scope.Register<IStorage, LocalStorage>();
```

实现类必须有公开无参构造函数。

### Factory

```csharp
scope.RegisterScopedFactory<IRepository>(resolver =>
    new Repository(resolver.Resolve<IStorage>()));
```

默认生命周期是 `Scoped`，每个注册所在 Scope 只有一个实例。

Transient 每次解析创建一个实例：

```csharp
scope.RegisterTransient<IEnemy>(_ => new Enemy());
```

Transient 默认由调用方管理，不会被 Scope 持有。确实需要 Scope 在结束时统一 `Dispose` 每个实例时，显式注册：

```csharp
scope.RegisterOwnedTransient<ITemporaryBuffer>(_ => new TemporaryBuffer());
```

Owned Transient 不能实现 ZArch 生命周期接口。需要初始化或反初始化的服务应使用 Scoped。

### Alias

多个接口指向同一实例：

```csharp
scope.Register<PlayerService, PlayerService>();
scope.RegisterAlias<IPlayerReader, PlayerService>();
scope.RegisterAlias<IPlayerWriter, PlayerService>();
```

Alias 不重复拥有或初始化实例。

## 4. 解析规则

必需依赖：

```csharp
var storage = scope.Resolve<IStorage>();
```

可选依赖：

```csharp
if (scope.TryResolve<IAnalytics>(out var analytics)) {
    analytics.Track("start");
}
```

检查当前 Scope 是否注册：

```csharp
bool local = scope.IsRegisteredLocally<IStorage>();
```

ZArch 使用精确注册键，不自动映射实现类型、基类或其他接口。循环 Factory 依赖会抛出异常。

Scope setup 期间只能注册，不能解析：

```csharp
architecture.CreateRootScope("App", scope => {
    scope.Register<IStorage>(new LocalStorage());
    // 此时不要 scope.Resolve<IStorage>()。
});
```

Factory 会在激活阶段创建，依赖解析也在这时发生。

## 5. 生命周期

```csharp
public interface IInitializable {
    void Initialize();
}

public interface IDeinitializable {
    void Deinitialize();
}

public interface IAsyncDeinitializable {
    Task DeinitializeAsync(CancellationToken cancellationToken);
}
```

Owned Scoped 服务按 `initializationOrder`、再按注册顺序初始化：

```csharp
scope.Register(config, initializationOrder: -100);
scope.Register(repository, initializationOrder: 0);
scope.Register(gameplay, initializationOrder: 100);
```

销毁时：

1. 取消 Scope 生命周期令牌，并先销毁所有子 Scope；
2. 解除并清理作用域事件；
3. 按实际初始化顺序反向 `Deinitialize`；
4. 按拥有顺序反向 `Dispose`；
5. 清理注册表并脱离父 Scope。

初始化失败会回滚已经初始化和拥有的对象，失败 Scope 不会挂到 Architecture 树中。

包含异步反初始化服务时，等待完整清理：

```csharp
await scope.DisposeAsync(cancellationToken);
await architecture.ShutdownAsync(cancellationToken);
```

同步 `Dispose` 遇到仅支持异步清理的服务会报告错误。清理过程仍会继续处理其余服务，最后将异常交给 `UnhandledExceptionHandler`；没有设置处理器时向调用方抛出。

Architecture、Scope、注册表和事件总线采用串行模型，不提供内部锁。Unity 项目应在主线程创建/销毁 Scope、解析服务和发布事件；后台任务完成后先切回所属同步上下文。

## 6. 默认事件与 Scoped Event

Core 内部保留两套相互隔离的事件空间：Architecture Event Bus 和每个 Scope 自己的 Event Bus。它们使用相同的 `Subscribe / Unsubscribe / Publish` 动词，但接收者不同。

| 发布方式 | 谁能收到 | 是否跨 Scope |
| --- | --- | --- |
| `architecture.Publish(message)` | 订阅同一个 Architecture 的处理器 | 是 |
| `scope.Publish(message)` | 只订阅当前 Scope 的处理器 | 否 |
| `scope.Publish(message, Bubble)` | 当前 Scope 和所有祖先 Scope 的处理器 | 只向上 |

Patterns 层把 Architecture Event 作为默认事件，因此业务代码使用更短的 `SubscribeEvent / PublishEvent`；需要局部隔离时才显式使用 `ScopedEvent`。

### 6.1 默认事件：Architecture 范围广播

Architecture Event 在单个 Architecture 内广播，不关心订阅者属于哪棵 Scope 树：

```csharp
var unregister = architecture.Subscribe<PlayerLoggedInEvent>(OnLoggedIn);
architecture.Publish(new PlayerLoggedInEvent());
unregister.Unregister();
```

一个进程可以有多个 Architecture，它们的默认事件仍然彼此隔离。Architecture Event 不会自动进入任何 Scope Event Bus。

### 6.2 Scoped Event：当前 Scope 内部通知

Scope Event 默认只发布到当前 Scope：

```csharp
var unregister = battleScope.Subscribe<CardSelectedEvent>(OnCardSelected);
battleScope.Publish(new CardSelectedEvent());
```

子 Scope 的订阅者不会收到父 Scope 发布的消息。Scoped Event 没有向下广播语义：

```csharp
appScope.Publish(new RefreshEvent());

// LobbyScope 和 BattleScope 都不会因为是 AppScope 的子级而收到。
```

因此 `AppScope.Publish(...)` 不等于 `architecture.Publish(...)`，也不应被当作全局事件入口。

### 6.3 向祖先 Scope 冒泡

需要让局部消息逐级通知父 Scope 时使用 `Bubble`：

```csharp
battleScope.Publish(
    new GameExitedEvent(),
    EEventPropagation.Bubble
);
```

传播顺序为：

```text
当前 Scope → Parent → 更上层祖先
```

`Bubble` 仍然不会进入 Architecture Event Bus，也不会通知当前 Scope 的子级或兄弟 Scope。

### 6.4 如何选择

| 需求 | 选择 |
| --- | --- |
| 登录状态、玩家资料、跨模块刷新 | 默认事件 |
| 大厅与当前游戏都需要接收 | 默认事件 |
| 战斗内部选牌、回合、局部动画消息 | Scoped Event |
| 子玩法向 GameScope 或 AppScope 汇报 | Scoped Event + `Bubble` |
| 持续存在的状态 | `BindableProperty`，不是 Event |

业务代码应优先选择默认事件。只有事件确实需要 Scope 隔离或父链传播时，才选择 Scoped Event。不要让同一个事件类型同时出现在两套总线上，否则发布代码看似正确，订阅者却可能永远收不到。

### 6.5 订阅生命周期与异常

直接调用 `Architecture.Subscribe` 创建的是脱离 Scope 的订阅，不会因为某个 Scope Dispose 而自动解除，调用方必须保存 `IUnregister`。

Patterns 层的 `this.SubscribeEvent` 会把 Architecture Event 订阅交给调用者所属 Scope 托管；Scope Dispose 时自动解除。它仍然返回 `IUnregister`，可用于提前取消，或进一步绑定到更短的 Unity 对象生命周期。

`ArchitectureScope.Subscribe` 会被当前 Scope 跟踪，Scope Dispose 时自动解除；返回的 `IUnregister` 仍可用于提前取消订阅。

事件处理器发生异常时，ZArch 会继续调用同一发布路径上的其余处理器，最后向发布方抛出包含全部错误的 `AggregateException`。

### 6.6 Signal 与 Event Bus

`Core/Events` 中的 `Signal<T>` 是无路由的本地通知原语，使用 `Subscribe / Unsubscribe / Emit`；`TypeEventBus` 是 Architecture 和 Scope 内部按消息类型路由的实现。业务层通常只需要使用 Architecture、Scope 或 `BindableProperty`，不必直接依赖内部 EventBus。

## 7. Scope 状态

```text
Created → Configuring → Initializing → Active → Disposing → Disposed
                                ↓
                              Faulted
```

- `Configuring`：允许注册，不允许解析。
- `Initializing`：创建服务并执行生命周期。
- `Active`：允许解析、事件和创建子 Scope。
- `Disposing/Disposed/Faulted`：拒绝继续使用。

## 8. 所有权原则

- `owned: true`：Scope 负责初始化、反初始化和 Dispose。
- `owned: false`：调用方负责对象生命周期，但普通 `Register` 仍会为 `ICanSetScope` 注入当前 Scope。
- `RegisterExternal`：既不接管生命周期，也不向实例注入 Scope。
- 创建 Child Scope 的代码负责决定它何时结束。
- 父 Scope Dispose 会自动 Dispose 全部子 Scope。

下一篇：[Patterns](03-Patterns.md)。
