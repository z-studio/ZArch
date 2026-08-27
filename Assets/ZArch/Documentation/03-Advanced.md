# 03 高级篇：异步、多 Host 与架构扩展

本篇面向登录、热更新、联网战斗、编辑器预览和多世界等复杂场景。

## 1. 异步 Scope

同步 API 不接受 `IAsyncInitializable`。涉及网络、数据库或远程配置时使用异步 API：

```csharp
var onlineScope = await root.CreateChildAsync(
    "Online",
    (scope, token) => {
        scope.Register<IAccountService>(new AccountService());
        scope.Register<IMatchService>(new MatchService());
        return Task.CompletedTask;
    },
    timeout: TimeSpan.FromSeconds(15),
    cancellationToken: destroyCancellationToken
);
```

异步 setup 完成后，ZArch 会按初始化顺序调用服务的异步或同步初始化，全部成功才返回 Active Scope。

创建中的 Scope 不会提前出现在 `RootScopes`、`Scopes` 或 Parent 的 `Children`。Dispose 父 Scope 或 Shutdown Host 会取消仍在进行的初始化；异步实现仍必须协作观察 token。

## 2. 实现异步服务

```csharp
using System.Threading;
using System.Threading.Tasks;
using ZArch;

public sealed class AccountService : IAsyncInitializable, IDeinitializable {
    private CancellationTokenSource m_RuntimeCts;

    public async Task InitializeAsync(CancellationToken token) {
        var profile = await LoadProfileAsync(token);
        ApplyProfile(profile);
        m_RuntimeCts = new CancellationTokenSource();
    }

    public void Deinitialize() {
        m_RuntimeCts?.Cancel();
        m_RuntimeCts?.Dispose();
        m_RuntimeCts = null;
    }
}
```

取消是协作式的。setup 和服务必须把 token 继续传给底层异步 API。忽略 token 的任务不会被强制终止。

## 3. timeout 和外部取消

- `timeout`：限制本次 Scope 配置和初始化允许等待的时间。
- `cancellationToken`：由场景退出、用户取消或应用关闭触发。
- 两者会链接，任意一个取消都会使创建失败并回滚 Scope。

需要取消时必须使用双参数 setup：

```csharp
async (scope, token) => {
    RemoteConfig config = await api.LoadConfigAsync(token);
    scope.Register(config);
}
```

单参数 `scope => Task` 重载不提供 timeout，因为 setup 无法观察 timeout token。

## 4. 初始化依赖与顺序

构造依赖和初始化依赖是两件事：

```csharp
scope.Register<IConfig>(config, initializationOrder: -100);
scope.RegisterFactory<IRepository>(
    resolver => new Repository(resolver.Resolve<IConfig>()),
    initializationOrder: 0
);
scope.Register<IGameplay>(gameplay, initializationOrder: 100);
```

Factory 解决“如何创建”，`initializationOrder` 解决“谁先 Initialize”。如果一个服务的 Initialize 会使用另一个服务已经初始化后的状态，应显式指定顺序。

## 5. 自定义 Architecture

```csharp
public sealed class GameArchitecture : Architecture {
    private IPlatformLogSink m_LogSink;

    protected override void OnStart() {
        m_LogSink = CreateLogSink();
        ExceptionHandler += m_LogSink.Report;
    }

    protected override void OnShutdown() {
        if (m_LogSink != null) {
            ExceptionHandler -= m_LogSink.Report;
            m_LogSink.Dispose();
            m_LogSink = null;
        }
    }
}
```

`OnShutdown` 在所有 Scope 完成销毁后调用。它只清理 `OnStart` 创建的 Host 级资源；普通业务服务仍应注册到 Scope。

Architecture 是一次性实例。调用 `Shutdown()` 或 `Dispose()` 后不能再次 `Start()`，需要新一轮生命周期时创建新的 Architecture，避免上一轮异步任务污染新的 Scope 和事件。

初始化失败、反初始化失败和 Dispose 失败会交给 `ExceptionHandler`。异常上报器自身抛错不会中断后续清理。

## 6. ScopeConfiguring 横切配置

`ScopeConfiguring` 在用户 setup 完成、Scope 初始化开始前触发。它适合为所有 Scope 注入日志、监控或公共约束：

```csharp
architecture.ScopeConfiguring += scope => {
    if (!scope.IsRegisteredLocally<IScopeMetrics>()) {
        scope.Register<IScopeMetrics>(new ScopeMetrics(scope.Name));
    }
};
```

注意：回调仍处于配置阶段，可以注册或检查本地注册，但不能 Resolve 服务。需要依赖其他服务时应注册 Factory，让容器在初始化阶段统一解析。回调抛异常会导致当前 Scope 整体回滚。

## 7. 多 Host

每个 Architecture 拥有独立的 Scope、服务和事件：

```csharp
using var gameHost = new ArchitectureHost();
using var previewHost = new ArchitectureHost();

gameHost.Start();
previewHost.Start();

var game = gameHost.CreateRootScope("Game", ConfigureProduction);
var preview = previewHost.CreateRootScope("Preview", ConfigurePreview);
```

典型用途：

- 正式世界与战斗回放；
- 游戏运行时与角色预览窗口；
- 客户端预测世界与权威快照；
- EditMode 测试环境；
- 同进程多个子应用。

不要跨 Host 创建父子 Scope，也不要让同一个有 Scope 上下文的 Model/System 实例同时属于多个 Host。

## 8. Owned 与外部资源

默认 `owned: true`，Scope 会负责：

1. Initialize；
2. Deinitialize；
3. IDisposable.Dispose。

以下对象通常使用 `owned: false`：

- Unity 或平台提供的全局 SDK 单例；
- 另一个容器拥有的对象；
- 只在当前 Scope 建立别名但实例来自 Parent；
- 调用方负责释放的 Transient。

```csharp
scope.Register<IPlatformSdk>(PlatformSdk.Instance, owned: false);
```

`owned: false` 的生命周期完全由调用方负责，ZArch 不会 Initialize、Deinitialize 或 Dispose。

## 9. 错误边界和回滚

以下阶段任何一个抛异常，Scope 都不会进入 Active：

- setup；
- `ScopeConfiguring`；
- Factory 创建；
- 同步或异步 Initialize；
- timeout/外部取消检查。

调用方应只在 await 成功后保存 Scope：

```csharp
ArchitectureScope online = null;

try {
    online = await root.CreateChildAsync(
        "Online",
        ConfigureOnlineAsync,
        timeout: TimeSpan.FromSeconds(15),
        cancellationToken: token
    );
} catch (OperationCanceledException) {
    // 用户取消或超时；Scope 已经回滚。
} catch (Exception exception) {
    root.Architecture.ReportException(exception);
}
```

## 10. 自定义服务而不使用 Patterns

Core 并不要求使用 Model/System：

```csharp
public sealed class MatchSession : IInitializable, IDeinitializable {
    private readonly IServiceResolver m_Resolver;

    public MatchSession(IServiceResolver resolver) {
        m_Resolver = resolver;
    }

    public void Initialize() {
        var config = m_Resolver.Resolve<IMatchConfig>();
    }

    public void Deinitialize() {
    }
}
```

Patterns 是推荐的业务组织方式，不是 Core 的强制要求。

## 11. 性能意识

- Scoped Factory 只在激活时创建一次，适合常驻服务。
- Transient 每次 Resolve 都分配对象，不要在每帧热点中滥用。
- Command/Query 通常是短命小对象；极端热点可以直接调用 System。
- 事件适合状态变化通知，不适合替代每帧数据流。
- Scope 是模块生命周期边界，不要为每颗子弹创建 Scope。

## 12. 高级篇完成标准

实现一个可取消的在线模块：

- 创建 Online Child Scope；
- 远程配置先于账号服务初始化；
- 15 秒 timeout；
- 用户返回登录页时取消；
- 初始化失败不留下任何 Scope；
- 同时创建一个独立 Preview Host，验证事件互不影响。

上一篇：[02 中级篇](02-Intermediate.md)｜下一篇：[04 Unity 与第三方 SDK 实战](04-Unity-and-SDK.md)
