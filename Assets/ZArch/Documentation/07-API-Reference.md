# API 速查

本页只列常用入口。完整签名以源码和 IDE 提示为准。

## Architecture

| API | 用途 |
| --- | --- |
| `Start()` | 启动 Architecture；创建 Scope 前必须调用 |
| `Shutdown()` | 关闭所有 Scope 与服务；实例不可再次启动 |
| `ShutdownAsync(token)` | 等待异步反初始化后关闭 |
| `CreateRootScope(name, setup, tag)` | 同步创建根 Scope |
| `CreateRootScopeAsync(...)` | 异步创建根 Scope，可设置 timeout/token |
| `RegisterEvent<T>(handler)` | 注册 Architecture 级事件 |
| `SendEvent<T>(event)` | 发送 Architecture 级事件 |
| `UnregisterEvent<T>(handler)` | 按处理函数解除注册 |
| `UnhandledExceptionHandler` | 接收框架捕获后报告的异常；为空时向调用方抛出 |

## ArchitectureScope

| API | 用途 |
| --- | --- |
| `CreateChild(...)` / `CreateChildAsync(...)` | 创建子 Scope |
| `Register<T>(instance, owned, initializationOrder)` | 注册实例 |
| `Register<TService,TImplementation>(...)` | 通过无参构造创建实现 |
| `RegisterScopedFactory<T>(factory, owned, order)` | 注册每个 Scope 一个实例的 Factory |
| `RegisterTransient<T>(factory)` | 注册调用方拥有的 Transient |
| `RegisterOwnedTransient<T>(factory)` | 注册由 Scope 统一 Dispose 的 Transient |
| `RegisterAlias<TAlias,TService>()` | 为同一 Scoped 实例添加服务键 |
| `Resolve<T>()` | 从当前 Scope 向父级解析必需服务 |
| `TryResolve<T>(out value)` | 尝试解析可选服务 |
| `IsRegisteredLocally<T>()` | 只检查当前 Scope |
| `RegisterEvent<T>(handler)` | 注册 Scope 事件 |
| `Publish<T>(event, propagation)` | 向当前 Scope 或父级发布 |
| `Dispose()` | 释放子 Scope、服务、事件与注册 |
| `DisposeAsync(token)` | 等待异步反初始化并释放 Scope |

常用枚举：

- `EServiceLifetime.Scoped`：每个注册只有一个实例；
- `EServiceLifetime.Transient`：每次解析创建实例；
- `EEventPropagation.Local`：只发布到当前 Scope；
- `EEventPropagation.Parents`：从当前 Scope 向父级传播。

## 生命周期

| 接口 | 方法 | 说明 |
| --- | --- | --- |
| `IInitializable` | `Initialize()` | Scope 激活时同步初始化 |
| `IAsyncInitializable` | `InitializeAsync(token)` | 仅异步 Scope 支持 |
| `IDeinitializable` | `Deinitialize()` | Scope 释放时逆序执行 |
| `IAsyncDeinitializable` | `DeinitializeAsync(token)` | 异步释放时逆序等待 |
| `IDisposable` | `Dispose()` | Owned 对象的最终释放 |

同步创建 Scope 时如果发现 `IAsyncInitializable`，会直接失败并回滚。

## Patterns

| 入口 | 用途 |
| --- | --- |
| `GetModel<T>()` | 解析 Model |
| `GetSystem<T>()` | 解析 System |
| `GetUtility<T>()` | 解析 Utility |
| `SendCommand(command)` | 执行 Command |
| `SendQuery(query)` | 执行 Query |
| `RegisterArchitectureEvent<T>()` | 注册 Architecture Event |
| `SendArchitectureEvent<T>()` | 发送 Architecture Event |
| `RegisterScopeEvent<T>()` | 注册 Scope Event |
| `PublishScopeEvent<T>()` | 发布 Scope Event |

Model 与 System 的基类为 `AbstractModel`、`AbstractSystem`；Command/Query 基类为 `AbstractCommand`、`AbstractCommand<TResult>`、`AbstractQuery<TResult>`。

## BindableProperty

| API | 用途 |
| --- | --- |
| `Value` | 读取或修改值；变化时通知 |
| `Register(handler)` | 监听后续变化 |
| `RegisterAndInvoke(handler)` | 注册并立即回调当前值 |
| `SetValueWithoutNotify(value)` | 修改但不通知 |
| `WithComparer(comparer)` | 设置实例级相等比较 |
| `BindableProperty<T>.Comparer` | 设置类型级默认比较器 |
| `Unregister(handler)` | 按处理函数解除监听 |

对外只读时暴露 `IReadOnlyBindableProperty<T>`，内部保留 `BindableProperty<T>`。

## Unity

| API | 用途 |
| --- | --- |
| `ArchitectureBootstrap` | 创建 Architecture 与根 Scope |
| `AsyncArchitectureBootstrap` | 创建异步根 Scope，并暴露 `Initialization` |
| `ArchitectureController.BindScope(scope)` | 绑定组件使用的 Scope |
| `SceneScopeManager` | 根据场景加载/卸载管理 Scope |
| `UnregisterWhenDisabled(...)` | Behaviour 禁用时解除监听 |
| `UnregisterWhenGameObjectDestroyed(...)` | 对象销毁时解除监听 |
| `UnregisterWhenSceneUnloaded(...)` | 场景卸载时解除监听 |
| `ArchitectureDebug.Capture(architecture)` | 捕获运行时架构快照 |

## GameModules

| API | 用途 |
| --- | --- |
| `IGameModule.Configure(scope, context)` | 配置模块 Scope |
| `GameModuleCatalog` | Unity 模块资产目录 |
| `GameModuleLauncher.EnterAsync(id, context, token)` | 进入模块 |
| `GameModuleLauncher.ExitAsync()` | 退出当前模块 |
| `GameModuleLauncher.ShutdownAsync()` | 完整关闭并禁止再次进入 |
| `UnityGameContentLoader` | 通过 Scene Provider 加载内容 |
| `GameModuleSceneEntry` | 将已加载场景绑定到模块 Scope |

## 常见异常定位

| 提示 | 常见原因 |
| --- | --- |
| Architecture is not started | 忘记调用 `Start()`，或已经 Shutdown |
| Scope must be Active | 在 setup 中解析，或使用已释放 Scope |
| Service is not registered | 注册键不一致，或服务不在当前/父 Scope |
| Circular factory dependency | Factory 之间循环解析 |
| Async initialization requires async creation | 使用同步 API 创建了异步服务 |
| Controller is already bound | 尝试把同一组件改绑到另一个 Scope |

下一篇：[维护与扩展](08-Maintenance.md)
