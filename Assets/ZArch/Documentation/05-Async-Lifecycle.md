# 异步生命周期

需要网络连接、资源预热或异步 SDK 初始化时，让服务实现 `IAsyncInitializable`，并使用异步 Scope 创建 API。

## 1. 定义异步服务

```csharp
using System.Threading;
using System.Threading.Tasks;
using ZArch;

public sealed class OnlineService : IAsyncInitializable, IAsyncDeinitializable
{
    private bool m_Connected;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken);
        m_Connected = true;
    }

    public async Task DeinitializeAsync(CancellationToken cancellationToken)
    {
        if (!m_Connected) return;
        await DisconnectAsync(cancellationToken);
        m_Connected = false;
    }

    private Task ConnectAsync(CancellationToken cancellationToken)
        => Task.Delay(100, cancellationToken); // 替换为真实 SDK 初始化

    private Task DisconnectAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
```

## 2. 异步创建 Scope

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using ZArch;

using var architecture = new Architecture();
architecture.Start();
using var cancellation = new CancellationTokenSource();

var root = await architecture.CreateRootScopeAsync(
    "Root",
    (scope, token) =>
    {
        scope.Register<OnlineService>(new OnlineService());
        return Task.CompletedTask;
    },
    timeout: TimeSpan.FromSeconds(10),
    cancellationToken: cancellation.Token);
```

子 Scope 使用相同模式：

```csharp
var battle = await root.CreateChildAsync(
    "Battle",
    (scope, token) =>
    {
        scope.Register<BattlePreloader>(new BattlePreloader());
        return Task.CompletedTask;
    },
    timeout: TimeSpan.FromSeconds(30),
    cancellationToken: cancellation.Token);
```

没有异步服务时继续使用同步的 `CreateRootScope` / `CreateChild`，代码更直接。

## 3. 初始化顺序

注册时可以设置 `initializationOrder`：

```csharp
scope.Register(new ConfigService(), initializationOrder: -100);
scope.Register(new OnlineService(), initializationOrder: 0);
scope.Register(new PlayerSession(), initializationOrder: 100);
```

数值较小的服务先初始化；关闭 Scope 时按相反顺序执行 `Deinitialize` 或 `DeinitializeAsync`。相同顺序按注册次序处理。

不要在配置委托执行期间调用 `Resolve`，因为 Scope 还没有完成激活。服务之间有明确依赖时，在构造或配置阶段保存依赖，或通过 Factory 延迟构建。

## 4. 失败、取消与回滚

异步创建过程中发生以下任一情况，Scope 都不会进入 Active 状态：

- 初始化方法抛出异常；
- 调用方取消 `CancellationToken`；
- 超过指定 `timeout`；
- 架构在创建期间开始关闭。

已经初始化成功的 Owned 服务会按逆序清理，然后异常继续传给调用方。因此初始化实现应满足：

- 尊重传入的 `CancellationToken`；
- 在成功完成前不要向外发布“可用”状态；
- `InitializeAsync` 失败前自行清理尚未完成初始化的临时资源；
- 成功后的反初始化保持幂等；
- 不在后台遗留无法取消的任务。

## 5. Unity 主线程上下文

服务若在等待后访问 Unity API，应确保所使用的异步库会回到 Unity 主线程，或显式切换回主线程。避免在生命周期实现里使用 `async void`，让完成、取消和异常都通过返回的 `Task` 传播。

## 6. 异步关闭

包含 `IAsyncDeinitializable` 时必须等待异步清理：

```csharp
await battle.DisposeAsync(cancellationToken);
await architecture.ShutdownAsync(cancellationToken);
```

同步 `Dispose`/`Shutdown` 仍可用于完全同步的服务树。如果同步关闭遇到只能异步清理的服务，会完成其余清理并报告错误。

## 7. Unity 异步 Bootstrap

`AsyncArchitectureBootstrap` 在 `Awake` 启动初始化，并通过 `Initialization` 暴露可等待状态：

```csharp
using System.Threading;
using System.Threading.Tasks;
using ZArch;
using ZArch.Unity;

public sealed class GameBootstrap : AsyncArchitectureBootstrap
{
    protected override Task ConfigureRootAsync(
        ArchitectureScope scope,
        CancellationToken cancellationToken)
    {
        scope.Register<OnlineService>(new OnlineService());
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
        => ShutdownArchitectureAsync(cancellationToken);
}
```

依赖根 Scope 的组件先等待：

```csharp
await bootstrap.Initialization;
var root = bootstrap.RootScope;
```

异步 Bootstrap 默认要求显式关闭。退出时等待 `ShutdownAsync` 完成后再销毁 Bootstrap，不要使用 `async void` 承载关闭流程。

下一篇：[GameModules](06-Game-Modules.md)
