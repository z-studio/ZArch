# 异步、取消与关闭

## 初始化

纯 C# 用 `await architecture.StartAsync(ConfigureServices, token)`。Unity 在入口提供服务配置与异步回调：

```csharp
protected override void ConfigureServices(ArchitectureScope services) {
    services.Register(new PlayerModel());
}
protected override async Task OnInitializeAsync(CancellationToken cancellationToken) {
    await LoadPlayerAsync(cancellationToken);
}
```

需要异步初始化的服务实现 IAsyncInitializable。Unity 入口统一使用异步服务激活。

异步实现必须响应传入的 CancellationToken；框架会等待它结束，不能安全地强行终止仍在访问对象的业务 Task。

## 就绪顺序

单场景：配置服务 → 初始化服务 → OnInitializeAsync → 激活 View → View.OnReady → 入口 OnReady → Initialization 完成。

游戏场景：配置并初始化游戏服务 → 加载场景、绑定环境 → OnEnterAsync → 激活 View → View.OnReady → 入口 OnReady → 游戏进入成功。

OnEnterAsync 可以访问服务和自己序列化引用的场景对象，但此时 View 尚未就绪。

## 关闭顺序

```text
Architecture / Scope / Unity 入口请求关闭
→ 阻止新的游戏进入，取消正在进入的流程并等待回滚
→ 游戏 OnExitAsync（服务仍可访问，应用事件仍可发布）
→ 解除游戏 View 绑定
→ 卸载游戏场景
→ 释放游戏服务
→ 解除应用 View 绑定并释放应用服务
```

不需要调用者分别关闭 GameSwitcher 和 Architecture。父环境关闭会自动执行资源所有者退出动作。

## 失败重试

- 退出失败：保留场景与服务，再次 CloseAsync 重试。
- 卸载失败：不重复已经成功的 OnExitAsync，保留服务，重试卸载。
- ShutdownAsync 失败：保留尚未释放的环境，再次 ShutdownAsync 发起新的清理尝试；不会永久缓存失败任务。
- 关闭一旦开始，不允许重新进入游戏；关闭失败后应继续关闭。
- 普通服务 Dispose 发生错误时，框架继续清理其余对象并报告；这不等于任意服务都支持重试。

活动游戏不能通过同步 Dispose/Shutdown 安全退出，框架会在破坏服务之前拒绝，提示使用 ShutdownAsync。

## 初始化时销毁

Unity 入口取消自己的生命周期令牌，等待初始化结束，再清理已创建环境；不会在初始化 Task 仍访问服务时继续激活界面。OnDestroy 是尽力清理，业务正常退出应显式等待 ShutdownAsync。

入口在初始化开始前被关闭时，Initialization 立即进入取消状态，后续激活不会再次启动它。Architecture 的配置统一通过 Start/StartAsync 提供，不使用继承式启动钩子。
