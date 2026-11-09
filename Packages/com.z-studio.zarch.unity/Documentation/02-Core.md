# 核心与 Scope

## 一个 Architecture 是一个独立环境

普通纯 C# 项目：

```csharp
using var app = new Architecture();
app.Start(services => {
    services.Register(new PlayerModel());
    services.Register(new PlayerSystem());
});
var player = app.GetModel<PlayerModel>();
```

`RootScope` 是应用根环境。`Start()` 可用于无服务的环境；首次访问 RootScope 时创建空根。异步服务使用 `await app.StartAsync(configure)`。

需要两个独立世界时创建两个 Architecture，不共享静态全局实例。框架公开使用路径只有一个应用根；需要短期环境时从根创建子 Scope。

## 注册与获取

- `Register(new PlayerModel())`：按具体类型注册，由 Scope 持有生命周期。
- `Register<IClock>(new Clock())`：按接口注册，必须以 IClock 获取。
- `Resolve<T>()` / `TryResolve<T>()`：取得服务；子级找不到时沿父级查找。
- `RegisterScopedFactory<T>(resolver => ...)`：显式声明依赖构造。
- `RegisterAlias<TAlias,TService>()`：为同一对象添加获取类型，不重复拥有生命周期。

```csharp
services.Register<IClock>(new Clock());
services.RegisterScopedFactory<Repository>(resolver => new Repository(resolver.Resolve<IClock>()));
```

配置阶段只注册，不解析服务。初始化阶段会先创建 Scoped 服务，再按初始化顺序调用生命周期方法。循环依赖会报错。

## 子 Scope

```csharp
var battle = app.RootScope.CreateChild("Battle", services => {
    services.Register(new BattleModel());
});
// 战斗结束后
await battle.DisposeAsync();
```

子级可以覆盖父级的注册；父级不会向下查找。服务实例不能在两个 Scope 中重复绑定。父级释放会先处理资源所有者的退出动作，再释放子级和本级服务。

## 外部对象与临时服务

`Bind(instance)` 只建立获取关系，不初始化或销毁外部对象；保存 IDisposable 并在外部对象失效时解除。适合 SDK 或由其他系统拥有的实例。

Transient 是高级功能：`RegisterTransient` 每次获取新对象，由调用方释放；`RegisterOwnedTransient` 显式交给 Scope 释放。默认使用实例注册即可。

## 事件只有两个明确范围

拥有相应能力的业务角色通过扩展方法使用（例如 System 可以订阅和发布）：

```csharp
this.SubscribeEvent<RoundEnded>(OnRoundEnded); // 当前 Scope
this.PublishEvent(new RoundEnded());
this.SubscribeAppEvent<PlayerLoggedIn>(OnPlayerLoggedIn); // 整个应用
this.PublishAppEvent(new PlayerLoggedIn());
```

Scope.Subscribe/Publish 是局部底层 API；Architecture.Subscribe/Publish 是应用级底层 API。局部事件不自动跨父子或兄弟传播。极少数需要父链通知的场景可以调用 `Scope.Publish(message, EEventPropagation.Bubble)`，没有第三套事件总线。

Scope 托管通过业务扩展创建的订阅；短寿命 View 还应 Track。直接 Architecture.Subscribe 需要调用方保存 IUnregister。发布会通知剩余处理器，再聚合抛出处理器错误。

## 生命周期与线程

IInitializable、IAsyncInitializable、IDeinitializable、IAsyncDeinitializable 提供服务初始化和释放。初始化失败会回滚；异步服务必须使用异步启动 API。

Scope 关闭前先执行内部资源所有者退出动作；失败会保留服务供重试。进入实际 Dispose 阶段后，普通服务清理失败仍会继续清理其他服务并报告错误，不保证任意 IDisposable 都可重试。

Architecture 关闭成功后不能重新启动。Unity 中在主线程使用；后台任务完成后先切回所属同步上下文。
