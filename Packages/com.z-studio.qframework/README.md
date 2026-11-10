# ZFramework 基础 + QFramework 业务

ZFramework.Unity 提供资源池、资源更新、UI、实体、声音、场景和网络等基础能力。QFramework 提供 Model / System / Command / Query 的业务规范，Session 管理局部业务生命周期。

## 三种生命周期

```text
App 主架构（启动至 App 关闭）
  服务器配置、基础组件访问入口、公共服务
  └─ 账号 Session（登录成功至注销）
       账号信息、Token、钱包、账号缓存、账号级连接与订阅
       └─ 游戏 Session（进入游戏至退出）
            游戏规则、回合状态、本次游戏的资源引用和实例
```

App 常驻不代表登录状态常驻。钱包、Token 等不得注册到 App 根架构；每次登录重新向账号 Session 注册新对象。游戏从账号会话查找服务，账号会话再从 App 查找，因此游戏访问的是当前账号的钱包和同一份基础服务。

## 项目结构

- `Runtime/QFramework.cs`：QF 内核与角色规范。
- `Runtime/Session.cs`、`SessionEvents.cs`：子会话、本地事件和显式 App 事件。
- `Runtime/AppFlow.cs`：启动、认证、大厅、游戏、注销和关闭流程。
- `Runtime/Games`：游戏入口、游戏切换及游戏资源访问合同。
- `Runtime/Loader/ZFramework`：资源池接入、资源所有权记录、基础组件访问。
- 项目的 `Assets/SessionDemo`：App、GameA、GameB、Editor，示例与资源放在一起。

QFramework 主程序集不引用 ZFramework。接入程序集 `QFramework.ZFramework` 引用两边；ZFramework 无需反向依赖 QFramework。App 程序集不引用 GameA/GameB，只有编辑器生成器引用游戏实现。

## 启动与登录

`AppFlow` 不规定服务器协议、账号类型或认证方式。应用提供异步回调：

```csharp
var flow = new AppFlow(
    App.Interface,
    user => new GameLauncher(user, gamePackageLoader, gameCatalog),
    user => CloseAccountConnectionsAsync(user));

await flow.StartAsync(async token => {
    // 读取服务器配置、准备 ZFramework 和资源包。
    await PrepareAppAsync(token);
});
// 此时 State == Login，显示登录界面。

await flow.LoginAsync(async (user, token) => {
    var response = await backend.LoginAsync(credentials, token);
    token.ThrowIfCancellationRequested();
    user.RegisterModel(new AccountModel(response));
    user.RegisterModel(new WalletModel(response.coins));
    user.RegisterSystem(new LobbySystem());
});
// 此时账号 Model/System 已初始化，State == Lobby。
```

注册阶段就持有的凭证或连接应实现 IDisposable，或在 closeAccount 中清理；认证中途取消时对象可能尚未 Init，不应只依赖 OnDeinit。

认证回调只负责认证和配置候选会话，不提前切换大厅或保存全局 Token。即使后端忽略取消并迟到返回，AppFlow 也会在发布账号之前再次检查取消。

`StartAsync` 失败后停在 Created，可重试；认证失败清理候选会话后回到 Login。需要等待的账号连接清理放在构造函数的 `closeAccount` 回调，它必须能处理认证中途失败、尚未注册账号数据的情况。清理回调不要复用已经取消的账号 LifetimeToken，也不要再次 await 自身的注销流程。网络登出通知可以尽力发送，但不应因服务器不可达而无限阻止本地注销。

## 大厅、游戏与注销

```csharp
await flow.OpenGameAsync("game-a");
await flow.ReturnToLobbyAsync();
await flow.LogoutAsync();
// 现在可以重新登录，也可以换账号。
```

- 同时运行一个游戏，切换游戏先完整关闭前一个。
- 返回大厅只释放游戏，不释放账号数据。
- 注销可打断认证和进入游戏；先取消并等待当前操作，再关闭游戏及资源，执行账号异步清理，最后释放账号 Session。
- 游戏退出失败时保留游戏和账号会话，State 为 LogoutFailed；可以再次 LogoutAsync 重试，清理完成前不允许新登录。
- `GameCloseFailed` 表示普通返回/切换时游戏未关闭，可以 ReturnToLobbyAsync 或注销重试。
- 常规操作不允许并发；注销/Shutdown 是能取消并等待当前操作的特殊入口。
- AppFlow 的状态应驱动登录/大厅界面；按钮调用异步流程，不使用 async void Command 执行整个登录或切换。
- 账号失效、被踢下线也调用同一个 LogoutAsync，避免另外实现一条清理路径。

所有操作在 Unity 主线程执行。业务任务应遵守传入的取消令牌；忽略取消的异步后端返回之前，流程仍需等待，以免留下旧账号任务。

## ZFramework 接入

App 根架构可以注册 `new ZFrameworkServices()`，业务通过 QF 的 Utility 能力访问配置好的基础组件：

```csharp
var web = this.GetUtility<ZFrameworkServices>().Get<WebRequestComponent>();
```

此入口不创建、重绑或销毁基础组件。HTTP 传输等能力可以常驻；身份凭证、账号级 WebSocket 通道、消息订阅与缓存归账号会话，在 closeAccount 中停止并等待。

资源入口改为：

```csharp
var loader = new ZFrameworkGamePackageLoader(resourceComponent);
```

默认游戏 ID 就是资源包名，也可以传入 `Func<GameInfo, string>` 自定义映射。App 负责通过 ZFramework 准备资源包、版本、CDN、下载与清单；加载器不重复初始化 YooAsset，不建立另一套资源缓存。

入口使用 `ResourceComponent.LoadAssetAsync<GameDefinition>`，归还使用 `UnloadAsset`。直接 YooAsset 句柄接口会绕过 ZFramework 资源池，不作为本项目的业务加载路径。

## 一个游戏可以加载多个资源

GameEntry 中提供 `Resources`，绑定当前游戏包与生命周期：

```csharp
protected override async Task OnEnterAsync(CancellationToken token) {
    var config = await Resources.LoadAsync<TextAsset>("GameConfig", token);
    var enemy = await Resources.InstantiateAsync("Enemy", transform, token);
}
```

- `LoadAsync` 的每次成功借用都分别记录，重复加载同一对象也分别归还。
- 可以 `Resources.Release(asset)` 提前归还一次借用。
- `InstantiateAsync` 使用 ZFramework 的自动归还实例化接口；销毁实例后不再次 UnloadAsset。
- 可以 `Resources.Destroy(instance)` 提前销毁实例，退出时仍等待其真正销毁。
- 关闭时拒绝新加载，取消并等待在途请求，销毁实例；Model/System Deinit 后，归还剩余资源与入口引用。
- 取消后迟到返回的资源或实例会立即归还/销毁，不再交给已关闭的游戏。

游戏入口资源只保存配置，运行数据放 Model。根 Prefab 保持 inactive；框架绑定会话、等待 OnEnterAsync 后才激活。Awake 不读取尚未绑定的会话。

## 退出顺序与资源包所有权

```text
取消游戏任务与资源请求
→ 等待游戏 OnExitAsync、销毁游戏根对象
→ 释放游戏包的 UI / 实体 / 声音，等待动态实例销毁
→ 释放散图绑定
→ 释放游戏 Session（Model/System Deinit）
→ 归还动态资源、入口资源
→ 注销时再关闭账号连接并释放账号 Session
```

默认按包清理 UI、实体、声音，仅适用于游戏独占包。共享 UI/声音/图标放 App 公共包；如果多个使用者共享游戏包，可向加载器传入自定义 `closePresentation` 回调，按自己的实例/序列号清理。

通过 SceneComponent 动态加载的游戏场景，由游戏在 OnExitAsync 中显式等待卸载，不能用全局“卸载所有场景”代替。

退出游戏或注销不会默认销毁资源包。归还资源池引用与真正卸载底层资源是两件事，包缓存由 ZFramework 管理。只有 App 明确拥有且引用已归还的非默认包，才调用 ResourceComponent.DestroyPackageAsync；它会拒绝销毁仍有在途加载或有效资源引用的包。

## Session 的业务约束

- 本地优先解析，未注册时查找父级；父级服务保持原来的上下文。
- 注册完成后调用 Initialize，先初始化 Model，再初始化 System。
- 只注册新建的本地对象，不重复注册父级实例。
- 普通事件只在当前 Session 传播；跨 App 使用显式 SendAppEvent / RegisterAppEvent。
- 共享模型的 BindableProperty 订阅用 `UnregisterWhenSessionEnds(session)` 或 `session.Track()` 跟踪。
- ZFramework 的全局 EventComponent 订阅不会自动成为局部事件。订阅后可用 `session.Track(new CustomUnregister(() => events.Unsubscribe(id, handler)))` 登记解除动作；账号连接和异步回调也应检查账号 LifetimeToken。
- 初始化失败会清理已尝试初始化的对象；Dispose 在单个清理失败时继续清理其他对象。
- 需要等待的清理写在退出回调中。正常注销通过 AppFlow，不直接 Dispose 账号 Session，以免绕过游戏异步退出。
- QF 角色接口保持原有业务能力约束，不是阻止强制转换或任意全局访问的安全沙箱。

## App 关闭

```csharp
await flow.ShutdownAsync();
// 再销毁 App 拥有的非默认资源包、账号外的基础服务。
App.Interface.Deinit();
// 最后才卸载基础组件所在场景或退出。
```

AppFlow 不拥有调用方的根架构和 ZFramework 组件，不会在注销时调用全局 ZFrameworkEntry.Shutdown 或 YooAssets.Destroy。正常场景切换必须先 await 关闭；OnDestroy 只是意外销毁时的兜底，应用被强杀时无法保证异步清理完成。

## 示例与验证

示例仅配置 ZFramework 的 Base、ObjectPool、Resource；界面使用场景中可见的 uGUI，便于查看按钮绑定。正式项目可启用已有 UI、实体、声音、网络组件，接入层会复用它们。

示例启动读取 DemoBackend 的模拟服务器配置，进入登录界面。Alice 初始钱包 100，Bob 为 200；游戏 A 每局加 10，游戏 B 每局加 20。注销清空旧账号，重新登录从后端重新取得数据。示例不是实际认证服务器；生产实现替换 `IAppBackend`，接入项目的配置、HTTP 和登录协议。

运行 `Assets/SessionDemo/App/App.unity`。示例等待基础资源初始化的超时可在 AppEntry Inspector 设置，超时会回到可重试的启动状态。需要生成场景或构建随包资源，使用 QFramework/Sessions 菜单。App 常驻包为 app，游戏包为 game-a/game-b。示例的便捷构建菜单使用 YooAsset/SBP，正式项目沿用 ZFramework 的 BuildWindow/BuildTools。示例保存 link.xml 以保留资源包中的入口类型；C# 代码热更新仍由项目选择。

核心流程验证：`dotnet run --project Tests/AppFlow/AppFlow.csproj --disable-build-servers`。Unity 集成验证入口见仓库 `Tests/Unity`。默认 .NET 测试引用本机 Unity 6000.3.9f1 的托管程序集，可通过 `-p:UnityManagedDirectory=...` 指定其他安装路径。
