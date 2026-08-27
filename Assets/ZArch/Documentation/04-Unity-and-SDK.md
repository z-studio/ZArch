# 04 Unity 与第三方 SDK 实战

本篇专门处理 MonoBehaviour、场景、自动注销和不确定线程的 SDK 回调。

## 1. ArchitectureHostBootstrap

```csharp
public sealed class GameBootstrap : ArchitectureHostBootstrap {
    protected override bool DontDestroy => true;
    protected override string RootScopeName => "App";

    protected override Architecture CreateArchitecture() {
        return new GameArchitecture();
    }

    protected override void ConfigureRoot(ArchitectureScope scope) {
        scope.Register<IConfig>(new GameConfig());
        scope.Register<ISaveSystem>(new SaveSystem());
    }
}
```

Bootstrap 的 `Awake` 会依次：创建 Architecture、Start、设置 Unity 异常处理器、创建 Root Scope。GameObject 销毁时自动 Shutdown。

需要先异步释放资源的项目应覆盖 `RequiresExplicitShutdown => true`，在自己的异步关闭方法完成后调用受保护的 `ShutdownArchitecture()`。如果 Bootstrap 被直接销毁，框架会输出错误并仅执行同步兜底。

一个项目可以有多个 Bootstrap，但它们会创建彼此隔离的 Host。不要无意中在多个场景重复放置同一个全局 Bootstrap。

## 2. ArchitectureController

Controller 不会搜索静态 Host，必须显式绑定：

```csharp
controller.BindScope(bootstrap.RootScope);
```

绑定同一个 Scope 是幂等操作；Controller 已绑定后不能静默改绑到另一个 Scope。需要复用 Controller 时，应让原 GameObject 随所属 Scope/Scene 销毁，而不是跨 Scope 复用旧实例。

绑定后可以使用：

```csharp
this.GetModel<IPlayerModel>();
this.GetSystem<IBattleSystem>();
this.GetUtility<IClock>();
this.SendCommand(new AttackCommand(10));
this.SendQuery(new GetGoldQuery());
```

如果 Controller 属于 Battle，应绑定 Battle Scope，而不是总是绑定 Root。

## 3. Unity 自动注销

GameObject 销毁时注销：

```csharp
subscription.UnregisterWhenGameObjectDestroyed(gameObject);
```

GameObject Disable 时注销：

```csharp
subscription.UnregisterWhenDisabled(gameObject);
```

指定 Scene 卸载时注销：

```csharp
subscription.UnregisterWhenCurrentSceneUnloaded();
subscription.UnregisterWhenSceneUnloaded(scene);
subscription.UnregisterWhenGameObjectSceneUnloaded(gameObject);
```

Additive Scene 项目优先使用显式 Scene 或 GameObject 版本。参数为空的 Current 版本记录调用当时的 Active Scene，不是订阅者自动所在的 Scene。

## 4. SceneScopeBinder

每个 Host 自己创建 Binder：

```csharp
private SceneScopeBinder m_SceneBinder;

private void Start() {
    m_SceneBinder = new SceneScopeBinder(m_Bootstrap.Architecture);

    m_SceneBinder.Bind(
        "Battle",
        scope => {
            scope.Register<IBattleModel>(new BattleModel());
            scope.Register<IBattleSystem>(new BattleSystem());
        },
        _ => m_Bootstrap.RootScope
    );

    m_SceneBinder.Enable();
}

private void OnDestroy() {
    m_SceneBinder?.Dispose();
}
```

可以使用完整 path 避免同名 Scene：

```csharp
m_SceneBinder.Bind("Assets/Scenes/Battle.unity", ConfigureBattle);
```

规则：

- Enable 会扫描已经加载的 Scene；
- Enable 后新增 Bind 也会重新扫描；
- 同名 Additive Scene 按 handle 创建不同 Scope；
- Scene 卸载时只 Dispose 对应 Scope；
- Binder Dispose 会解除监听并 Dispose 它创建的所有 Scene Scope；
- Dispose 后 Binder 不可复用。

不要把 GameModule Scene 同时注册到 `SceneScopeBinder`。Binder 是“Scene 加载后创建 Scope”，而 `GameLauncher` 是“先创建 GameScope 再加载 Scene”；同一 Scene 混用会产生两套 Scope 和两套服务生命周期。

## 5. 调试 Scope 树

进入 Play Mode 后打开：

```text
Tools → ZArch → Arch Debug
```

窗口可以切换 Bootstrap/Host，查看：

- Root 与 Child Scope；
- Scope 状态和 Tag；
- 绑定的 Scene；
- 服务是否创建、初始化和 Owned；
- 服务实际实现类型。

出现解析错误时，先用调试窗口确认服务注册在哪个 Scope、Controller 又绑定在哪个 Scope。

## 6. SDK 回调为什么危险

部分 SDK 在 Unity 主线程回调，部分在后台线程回调，还有一些没有公开保证。ZArch Core 采用串行执行模型，后台线程不能直接 Resolve、Publish、创建或 Dispose Scope。

不安全写法：

```csharp
sdk.Login(result => {
    scope.Architecture.SendEvent(new LoginCompleted(result)); // 回调线程未知
});
```

可能造成并发修改、事件顺序错乱、访问已销毁 Scope，或者间接触发 Unity 主线程限制。

## 7. 主线程回调队列

```csharp
using System;
using System.Collections.Concurrent;
using UnityEngine;

public sealed class MainThreadDispatcher : MonoBehaviour {
    private readonly ConcurrentQueue<Action> m_Queue = new();

    public void Post(Action action) {
        if (action != null) {
            m_Queue.Enqueue(action);
        }
    }

    private void Update() {
        while (m_Queue.TryDequeue(out var action)) {
            try {
                action();
            } catch (Exception exception) {
                Debug.LogException(exception);
            }
        }
    }
}
```

安全接入：

```csharp
sdk.Login(result => {
    dispatcher.Post(() => {
        if (!host.IsStarted || !scope.IsActivated) {
            return;
        }

        scope.Architecture.SendEvent(new LoginCompleted(result));
    });
});
```

即使 SDK 声明主线程回调，也可以排到下一帧，避免 SDK 在某个 ZArch 调用栈内部同步回调造成重入。

## 8. 把回调 SDK 包装成 Task

```csharp
private async Task<LoginResult> LoginAsync(
    string token,
    CancellationToken cancellationToken
) {
    var completion = new TaskCompletionSource<LoginResult>(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    var request = sdk.Login(
        token,
        result => completion.TrySetResult(result),
        error => completion.TrySetException(new SdkException(error))
    );

    using var registration = cancellationToken.Register(() => {
        request.Cancel(); // SDK 支持取消时调用
        completion.TrySetCanceled();
    });

    return await completion.Task;
}
```

如果 SDK 不支持取消：

1. timeout 后让 Task 进入取消状态；
2. SDK 底层请求可能继续运行；
3. 晚到回调只尝试完成 Task，不再直接访问 Scope；
4. 能解除 SDK 回调时立即解除；
5. 回到主线程后再次检查 Host 和 Scope 状态。

## 9. Unity 对象是否应该注册为服务

可以，但要明确所有权：

- 场景中的 MonoBehaviour 通常由 Unity 拥有，使用 `owned: false`；
- 纯 C# 服务通常由 Scope 拥有；
- 不要让 Scope 对 UnityEngine.Object 调用普通 `IDisposable` 来代替 `Destroy`；
- Controller 通常显式 Bind，不必注册为服务。

```csharp
scope.Register<ICameraRig>(cameraRig, owned: false);
```

## 10. Domain Reload 与应用退出

项目关闭 Domain Reload 后，更要避免业务静态单例。ZArch Host 由 Bootstrap GameObject 显式拥有，退出 Play Mode 或对象销毁时会 Shutdown。

应用退出阶段不要启动新的异步 Scope。取消应用级 token，等待关键存档流程按项目策略完成，然后让 Bootstrap 正常销毁。

上一篇：[03 高级篇](03-Advanced.md)｜下一篇：[05 常用配方](05-Cookbook.md)
