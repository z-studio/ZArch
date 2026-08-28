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
    protected override void OnStart() { }
    protected override void OnShutdown() { }
}
```

服务应在 Scope setup 中注册，不要在 `OnStart` 中偷偷创建默认 Scope。

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
scope.Register<IAnalytics>(externalAnalytics, owned: false);
```

### 实现类型

```csharp
scope.Register<IStorage, LocalStorage>();
```

实现类必须有公开无参构造函数。

### Factory

```csharp
scope.RegisterFactory<IRepository>(resolver =>
    new Repository(resolver.Resolve<IStorage>()));
```

默认生命周期是 `Scoped`，每个注册所在 Scope 只有一个实例。

Transient 每次解析创建一个实例：

```csharp
scope.RegisterFactory<IEnemy>(
    _ => new Enemy(),
    EServiceLifetime.Transient,
    owned: false
);
```

Owned Transient 不能实现 ZArch 生命周期接口。需要生命周期的服务应使用 Scoped。

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
```

Owned Scoped 服务按 `initializationOrder`、再按注册顺序初始化：

```csharp
scope.Register(config, initializationOrder: -100);
scope.Register(repository, initializationOrder: 0);
scope.Register(gameplay, initializationOrder: 100);
```

销毁时：

1. 取消 Scope 生命周期令牌，并先销毁所有子 Scope；
2. 解除并清理 Scope Event；
3. 按实际初始化顺序反向 `Deinitialize`；
4. 按拥有顺序反向 `Dispose`；
5. 清理注册表并脱离父 Scope。

初始化失败会回滚已经初始化和拥有的对象，失败 Scope 不会挂到 Architecture 树中。

## 6. Architecture Event 与 Scope Event

Architecture Event 在单个 Architecture 内广播：

```csharp
var unregister = architecture.RegisterEvent<PlayerLoggedIn>(OnLoggedIn);
architecture.SendEvent(new PlayerLoggedIn());
unregister.Unregister();
```

Scope Event 默认只发送到当前 Scope：

```csharp
scope.RegisterEvent<DamageEvent>(OnDamage);
scope.Publish(new DamageEvent());
```

向父级传播：

```csharp
scope.Publish(damage, EEventPropagation.Parents);
```

Scope Event 不会自动进入 Architecture Event。事件处理器发生异常时，ZArch 会继续调用其余处理器，最后向发送方抛出 `AggregateException`。

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
- `owned: false`：调用方负责对象生命周期。
- 创建 Child Scope 的代码负责决定它何时结束。
- 父 Scope Dispose 会自动 Dispose 全部子 Scope。

下一篇：[Patterns](03-Patterns.md)。
