# ZFramework 基础 + QFramework 业务

ZFramework.Unity 提供资源池、资源更新、UI、实体、声音、场景和网络等基础能力。QFramework 提供 Model / System / Command / Query 的业务规范，Session 管理局部业务生命周期。

先从项目根目录的 [业务阅读路径与 GameC 接入教程](../../README.md) 看 AppLogic、游戏命令和 GameEntry，再按需要查阅本说明。

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
- `Runtime/Session.cs`：数据与服务的作用范围、共享默认事件以及订阅生命周期。
- `Runtime/AppFlow.cs`：启动、认证、大厅、游戏、注销和关闭流程。
- `Runtime/Games`：游戏入口、游戏切换及游戏资源访问合同。
- `Runtime/Loader/ZFramework`：资源池、按需下载、场景、Procedure、会话 UI 与基础组件访问。
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
    // 读取服务器配置，等待 ZFramework 基础组件就绪；不下载所有游戏。
    await PrepareAppAsync(token);
});
// 此时 State == Login，显示登录界面。

await flow.LoginAsync(async (user, token) => {
    var response = await backend.LoginAsync(credentials, token);
    token.ThrowIfCancellationRequested();
    user.RegisterModel(new AccountModel(response));
    user.RegisterModel(new WalletModel(response.coins));
    user.RegisterSystem(new LobbySystem());
}, async (user, token) => {
    // 此时 OnInit 已执行，可以访问已绑定架构的服务与模型。
    await user.GetSystem<LobbySystem>().ConnectAndLoadUserAsync(token);
});
// 连接与必要账号数据全部就绪，State 才变为 Lobby。
```

注册阶段就持有的凭证或连接应实现 IDisposable，或在 closeAccount 中清理；认证中途取消时对象可能尚未 Init，不应只依赖 OnDeinit。

认证回调只负责认证和配置候选会话，不提前切换大厅或保存全局 Token。即使后端忽略取消并迟到返回，AppFlow 也会在发布账号之前再次检查取消。

`StartAsync` 失败后停在 Created，可重试；认证失败清理候选会话后回到 Login。需要等待的账号连接清理放在构造函数的 `closeAccount` 回调，它必须能处理认证中途失败、尚未注册账号数据的情况。清理回调不要复用已经取消的账号 LifetimeToken，也不要再次 await 自身的注销流程。网络登出通知可以尽力发送，但不应因服务器不可达而无限阻止本地注销。

## 大厅、游戏与注销

```csharp
await flow.OpenGameAsync("game-a", new EnterGameData(gameId, roomId, channelId));
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
// App 持有一份，供游戏加载器复用。只准备用户选择的游戏。
var packages = new ZFrameworkGamePackages(resourceComponent);
var loader = new ZFrameworkGamePackageLoader(resourceComponent, packages: packages);
// 可选：大厅提前下载某个游戏（不创建游戏会话）。
await packages.WaitForReadyAsync(60, token);
await packages.PrepareAsync(selectedGame, token);
// packages.Progress 提供当前游戏、阶段、进度与下载字节数。
```

`GameInfo.id` 是目录中选择游戏的键；`packageName` 指定资源包，留空才使用 id；`entryAddress` 是包内 GameDefinition 地址。服务器 GameId、房间、渠道等业务参数通过 OpenGameAsync 的第二个参数传入，不与资源包名混用。

准备器通过 ZFramework 初始化包、请求版本、加载清单、按需下载，再交给加载器。默认沿用 ResourceComponent.PlayMode；本地/远程混合项目可传入 `game => EPlayMode...` 选择模式。CDN、主备服务器、文件系统仍在 ZFramework 的资源配置中设置。已准备的包在本次 App 运行中复用；重新检查远程版本应在没有游戏使用者的安全时机由项目自己的更新流程处理。

准备器只销毁自己创建的包，外部包必须由其拥有者先准备好。取消时等待在途操作结束后回滚，回滚失败允许重试。关闭 App 时先 `await flow.ShutdownAsync()`，再 `await packages.ShutdownAsync()`，最后释放 App 架构。游戏退出与注销不删除磁盘缓存。若由现有项目全权准备资源，可不传 packages，加载器只借用已就绪的包。

入口使用 `ResourceComponent.LoadAssetAsync<GameDefinition>`，归还使用 `UnloadAsset`。直接 YooAsset 句柄接口会绕过 ZFramework 资源池，不作为本项目的业务加载路径。

## 一个游戏可以加载多个资源

GameEntry 中提供 `GameResources`，绑定当前游戏包与生命周期：

```csharp
protected override async Task OnEnterAsync(CancellationToken token) {
    var config = await GameResources.LoadAsync<TextAsset>("GameConfig", token);
    var enemy = await GameResources.InstantiateAsync("Enemy", transform, token);
}
```

- `LoadAsync` 的每次成功借用都分别记录，重复加载同一对象也分别归还。
- 可以 `GameResources.Release(asset)` 提前归还一次借用。
- `InstantiateAsync` 使用 ZFramework 的自动归还实例化接口；销毁实例后不再次 UnloadAsset。
- 可以 `GameResources.Destroy(instance)` 提前销毁实例，退出时仍等待其真正销毁。
- 关闭时拒绝新加载，取消并等待在途请求，销毁实例；Model/System Deinit 后，归还剩余资源与入口引用。
- 取消后迟到返回的资源或实例会立即归还/销毁，不再交给已关闭的游戏。

游戏入口资源只保存配置，运行数据放 Model。GameDefinition 选择一种入口：

- Prefab：填写 Prefab，SceneAddress 留空。
- 场景：填写 SceneAddress，Prefab 留空，并配置 ZFramework SceneComponent。场景以 Additive 加载，退出时框架等待卸载。

两种入口均要求一个未激活的 GameEntry 根对象；场景内只能有一个 GameEntry。加载器先绑定它与子层级的 SessionBehaviour、等待 OnEnterAsync，再激活入口。Awake 不读取尚未绑定的会话，游戏场景里不放 App/Foundation 或另一个 ProcedureComponent。

游戏入口用 `this.GetUtility<GameArguments>().Get<EnterGameData>()` 取得本次参数；可选参数用 `TryGet<T>()`。游戏的配置/Model/System 在 GameDefinition.Configure 注册，服务的异步进入房间写在 GameEntry.OnEnterAsync。即使同一个游戏再次进入，也创建新会话，以支持更换房间。

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

框架管理的入口场景会自动卸载；游戏额外加载的其他场景仍由 OnExitAsync 显式等待卸载，不能用全局“卸载所有场景”代替。

退出游戏或注销不会默认销毁资源包。归还资源池引用与真正卸载底层资源是两件事，包缓存由 ZFramework 管理。只有 App 明确拥有且引用已归还的非默认包，才调用 ResourceComponent.DestroyPackageAsync；它会拒绝销毁仍有在途加载或有效资源引用的包。

## Session 的业务约束

- 本地优先解析，未注册时查找父级；父级服务保持原来的上下文。
- 注册完成后调用 Initialize，先初始化 Model，再初始化 System。
- 只注册新建的本地对象，不重复注册父级实例。
- 普通 SendEvent / RegisterEvent 直接使用 App 根架构的同一个事件系统，与 QF 的架构范围事件一致。会话负责自身订阅的生命周期，退出不会清空根架构的事件系统。
- 共享模型的 BindableProperty 订阅用 `UnregisterWhenSessionEnds(session)` 或 `session.Track()` 跟踪。
- ZFramework 的 EventComponent 是另一套独立事件系统，其订阅需要显式绑定生命周期。订阅后可用 `session.Track(new CustomUnregister(() => events.Unsubscribe(id, handler)))` 登记解除动作；账号连接和异步回调也应检查账号 LifetimeToken。
- 初始化失败会清理已尝试初始化的对象；Dispose 在单个清理失败时继续清理其他对象。
- 需要等待的清理写在退出回调中。正常注销通过 AppFlow，不直接 Dispose 账号 Session，以免绕过游戏异步退出。
- QF 角色接口保持原有业务能力约束，不是阻止强制转换或任意全局访问的安全沙箱。

## 事件：与 QF 一样，默认在架构范围内共享

App 根架构持有唯一的业务事件系统，账号和游戏 Session 共用。会话负责数据、服务和订阅的生命周期，不划分事件通信范围。业务只需使用 QF 原有写法：

```csharp
// 账号的 WalletModel 或业务 System 更新金币后发送。
this.SendEvent(new CoinsChanged(newCoins));

// 当前游戏直接监听，无需知道父级、事件来源或传播方向。
this.RegisterEvent<CoinsChanged>(OnCoinsChanged);
```

事件类型定义在 App 公共程序集，游戏可以依赖它，App 不引用具体游戏程序集。默认事件不区分父级、子级或兄弟会话；同一根架构下已注册的监听器都能收到，其他独立根架构不受影响。各会话的 Model/System 服务查找仍保持本地优先、逐级查找父级。

订阅的清理由使用方会话负责：游戏退出解除游戏订阅，注销解除账号及游戏订阅，App 的订阅持续到显式解除或 App 根架构释放。`UnregisterEvent(handler)` 只解除当前会话最后一次匹配的注册，不影响其他会话使用同一处理方法的订阅；返回的 IUnregister 也可以提前解除，重复解除安全。

共享模型的 BindableProperty 仍适合显示持续变化的数据。示例钱包的 SetCoins 同时更新属性并发布 CoinsChanged，大厅订阅属性，游戏用普通 RegisterEvent 监听同一个架构事件。

发送/监听能力仍受 QF 角色接口约束。同步派发沿用 QF 的异常规则；会话已释放后不能再发送或注册。注销会取消在途请求，业务回包在修改模型或发送事件前仍应检查 LifetimeToken，尤其不要让旧请求通过保存的根架构引用发布消息。

## App 关闭

```csharp
await flow.ShutdownAsync();
// 再销毁 App 拥有的非默认资源包、账号外的基础服务。
App.Interface.Deinit();
// 最后才卸载基础组件所在场景或退出。
```

AppFlow 不拥有调用方的根架构和 ZFramework 组件，不会在注销时调用全局 ZFrameworkEntry.Shutdown 或 YooAssets.Destroy。正常场景切换必须先 await 关闭；OnDestroy 只是意外销毁时的兜底，应用被强杀时无法保证异步清理完成。

## 示例与验证

示例配置 ZFramework 的 Base、ObjectPool、Resource、Event、Fsm、Procedure、Scene；界面使用场景中可见的 uGUI，便于查看按钮绑定。正式项目可启用已有 UI、实体、声音、网络组件，接入层会复用它们。

示例启动读取 DemoBackend 的模拟服务器配置，进入登录界面。Alice 初始钱包 100，Bob 为 200；游戏 A 每局加 10，游戏 B 每局加 20。注销清空旧账号，重新登录从后端重新取得数据。示例不是实际认证服务器；生产实现替换 `IAppBackend`，接入项目的配置、HTTP 和登录协议。

运行 `Assets/SessionDemo/App/App.unity`。示例等待基础资源初始化的超时可在 AppEntry Inspector 设置，超时会回到可重试的启动状态。需要生成场景或构建随包资源，使用 QFramework/Sessions 菜单。App 常驻包为 app，游戏包为 game-a/game-b。示例的便捷构建菜单使用 YooAsset/SBP，正式项目沿用 ZFramework 的 BuildWindow/BuildTools。示例保存 link.xml 以保留资源包中的入口类型；C# 代码热更新仍由项目选择。

## 与现有 Procedure 一起使用

Procedure 与 AppFlow 分工固定：**Procedure 编排项目步骤与界面，AppFlow 拥有账号和游戏的生命周期**。

```text
ProcedureLaunch / Config / Agreement / CheckVersion / InitSdk
    项目原有启动步骤，完成准备后 Flow.State == Login
ProcedureLogin
    用户提交认证 → await Flow.LoginAsync(认证与注册, 账号准备)
ProcedureLobby
    用户选游戏 → await Flow.OpenGameAsync(目录键, 业务参数)
ProcedureGame
    返回大厅 → await Flow.ReturnToLobbyAsync()
任意登录后流程
    注销 / 被踢 → await Flow.LogoutAsync()
```

保留项目自己的启动 Procedure 顺序。配置、协议、版本检查和 SDK 初始化仍按现有顺序执行；在最后一个启动 Procedure 中调用一次 `Flow.StartAsync`，其准备回调完成剩余的基础准备，成功后再进入登录。此前的启动流程可以访问 App 根架构，但不创建账号 Session。不要每个启动 Procedure 都再调用 StartAsync，也不要在准备回调中等待 ProcedureLogin，形成循环等待。

登录、大厅和游戏 Procedure 可继承 `AppProcedure`，实现 Flow 属性，在 OnUpdate 调用 `FollowFlow<ProcedureLogin, ProcedureLobby, ProcedureGame>(owner)`。只有 Login/Lobby/Game 稳定状态触发显示流程切换；加载、注销和失败期间保留当前流程显示进度或重试。示例 AppProcedures.cs 实际注册了四个 Procedure，启动 Procedure 调用 AppEntry.StartApp，所有按钮经 AppLogic 进入 Flow，切换跟随 Flow 结果。新增游戏不用新增 Procedure。

项目已有界面切换时，可在各 Procedure.OnEnter 中打开对应界面。进入游戏期间保留大厅，只有 Flow.Game（游戏 OnEnterAsync 完成）后才隐藏大厅。显示逻辑不应修改 Flow.State。

**OnLeave 不再执行游戏退出、场景卸载或账号 Dispose**，这些工作由 Flow 统一等待。切到另一个 Procedure 不代表要注销。AppProcedure.VisibleToken 仅取消这个显示流程的界面任务，不代表账号或游戏的生命周期；Procedure 自己注册的界面事件仍需在 OnLeave 解除，不能只等待账号注销。Observe 用于观察无法返回 Task 的框架回调，交互按钮可像示例 Run 一样显示异常和重试入口。

## ZFramework UI 与子组件

游戏/账号界面继承 `SessionUIForm`，用 ZFramework 原有 OpenUIFormAsync 打开，userData 传入 `new SessionUIContext(session, viewData)`。资源包参数继续使用项目的包名；不要再通过静态 GameGlobal.Interface 查架构。

- OnOpen 中通过 GetModel/GetSystem 访问当前会话，ViewData 是业务打开参数。
- `Listen<T>(handler)` 监听共享默认事件并跟踪界面的订阅；`Listen(subscription)` 跟踪 BindableProperty 等订阅。关闭界面和会话结束都会解除登记的订阅。
- UI 池复用时重新绑定会话，关闭时清空；子组件继承 SessionBehaviour 并随界面绑定。
- 实例化在游戏根对象之外的业务组件，需要由创建方调用 BindSession；组件不拥有会话，不应自行 Dispose。
- 公共包中的账号界面必须在 closeAccount 中先关闭，再释放账号；游戏独占包的界面由游戏退出路径统一回收。框架不会全局关闭其他业务的窗口。

## 两个业务项目的迁移位置

| 现有代码 | 接入位置 |
| --- | --- |
| DropBall 的 AppEntry.ConfigureSession | 登录注册回调，注册账号 Model/System |
| DropBall 的 RecreateSessionScope / Super-Ace 的账号重置 | Flow.LogoutAsync，再次登录创建新账号会话 |
| DropBall 的 GameSessionCoordinator / ProcedureGame 加载与清理 | Flow.OpenGameAsync / ReturnToLobbyAsync，删除重复清理路径 |
| GameResourcePreparation / InternalGameItem 中的下载逻辑 | ZFrameworkGamePackages，列表项只触发准备并显示进度 |
| DropBallGameModule / SuperAceGlobal.Init | GameDefinition.Configure，只注册游戏服务 |
| InitService / SuperAceGlobal.Startup | GameEntry.OnEnterAsync，等待房间与配置就绪 |
| SuperAceGlobal.Shutdown / 游戏关闭回调 | GameEntry.OnExitAsync，停止业务、连接与额外场景 |
| AppEntry.LobbyArchitecture.GetSystem | 当前游戏 this.GetSystem，向账号会话查找 |

HTTP/Socket 组件可常驻，账号 Token 与大厅连接放账号服务；游戏 Token 与房间连接放游戏服务。包装旧回调接口时，取消不仅要结束等待，还要阻止迟到回包更新模型。两个真实项目的接口、UI 与游戏逻辑仍需按该映射迁移，本仓库示例没有冒充生产服务器。

新增一个独立游戏的完整最小步骤见根目录 GameC 教程。内置 A/B 按钮统一绑定 AppEntry.OpenGame(string)，包准备器负责资源组件等待，不需要复制 App 资源辅助类或增加游戏专用按钮方法。
