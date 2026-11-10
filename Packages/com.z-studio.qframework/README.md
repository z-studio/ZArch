# QFramework：子会话与独立游戏包

本包提供 QFramework 内核、子会话和 Unity 游戏加载。项目示例集中在 `Assets/SessionDemo`，框架包不包含示例业务代码。

## 目录

```text
Runtime/
  QFramework.cs               QF 内核
  Session.cs                  子会话与本地生命周期
  SessionEvents.cs            显式 App 事件
  Games/                      游戏切换、Unity 入口和资源句柄
  Loading/
    YooAsset/                 默认资源适配器
    Addressables/             可选资源适配器
```

YooAsset、Addressables 的适配器保留独立程序集；App、GameA、GameB 的程序集边界也保留。目录整理不改变这些依赖方向。

## 安装

默认依赖 YooAsset 3.0.7。先在 Unity Package Manager 中安装官方 Git 包，再安装本包：

```text
https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset#3.0.7
```

## 子会话

依赖原生 QFramework，不重新定义 Model、System、Command、Query 或 BindableProperty。

### 最小用法

```csharp
using QFramework;
using QFramework.Sessions;

// App 是你正常使用的 QF Architecture<App>。
var game = new Session(App.Interface, "GameA");
game.RegisterModel(new RoundModel());
game.RegisterSystem(new RoundSystem());
game.Initialize();

// 使用 QF 原有命令和查询。
game.SendCommand(new PlayRoundCommand());
// 本地未注册 WalletModel 时，返回 App 的同一个实例。
var wallet = game.GetModel<WalletModel>();

game.Dispose(); // 只释放本地对象、子会话和跟踪的订阅。
```

游戏的 Model/System/Command 内仍写 `this.GetModel<T>()`、`this.GetSystem<T>()`、`this.GetUtility<T>()`，无需手动判断服务来自哪个架构。父级对象不会被重新绑定。

### 生命周期与所有权

- 注册阶段只注册本次会话新建的本地对象；不要把父级实例重复注册进子会话。
- `Initialize()` 按注册顺序先初始化全部 Model，再初始化 System；注册完成后才能初始化。
- 初始化中途抛异常时，也会清理已经尝试初始化的对象。
- `Dispose()` 先取消 `LifetimeToken`，递归关闭子会话，再按初始化逆序 Deinit，取消订阅，逆序 Dispose 本地 IDisposable。
- 某个清理失败仍继续清理其余对象，最终抛 AggregateException；释放是幂等的。
- 需要依赖的 Model 应提前注册；不要依赖同组对象的 OnInit 已执行，除非已经通过注册顺序安排好。
- App 仍由 QF 管理；App 结束前必须先 `await launcher.ShutdownAsync()`，再 `App.Interface.Deinit()`。扩展不会偷偷接管 QF 静态架构的生命周期。
- 所有 API 在 Unity 主线程使用，不是线程安全容器。

### 事件边界

```csharp
// QF 标准事件 API：在当前会话内，不向父级或兄弟广播。
this.SendEvent(new RoundFinished());
this.RegisterEvent<RoundFinished>(OnRoundFinished);

// 显式 App 事件，沿用 QF 的 ICanSendEvent / ICanRegisterEvent 约束。
this.SendAppEvent(new AccountExpired());
this.RegisterAppEvent<AccountExpired>(OnAccountExpired);

// 共享模型的 BindableProperty 订阅也必须跟随会话释放。
wallet.coins.Register(OnCoinsChanged).UnRegisterWhenSessionEnds(session);
```

通过 Session.RegisterEvent、会话对象上的 RegisterAppEvent 注册的订阅自动跟踪。直接使用 `App.Interface.RegisterEvent`、`TypeEventSystem.Global` 或 C# event 时，需要自己解除订阅，或将其 IUnregister 交给 `session.Track()`。

QF 的角色能力接口保持原样；它们是业务约束，不是防止强制转换或直接 GetArchitecture 的安全沙箱。

### 游戏切换

`GameLauncher` 只依赖 `IGamePackageLoader / IGamePackage / IGame`，不依赖 Unity，也不认识游戏实现。

```text
Load package → Create Session → Configure → Initialize → CreateGame → Enter
Exit → Dispose Session → Unload package
```

- 同时只运行一个游戏；切换先完整退出前一个。
- 未知 ID 不影响当前游戏；重复打开正在运行的游戏直接返回。
- 操作期间再次 Open/Close 会抛 InvalidOperationException，UI 应禁用重复操作。
- Enter 失败会尝试 Exit、释放会话并卸载包。
- Exit 失败保留游戏、会话与包，之后 Close/Shutdown 可以重试；LifetimeToken 已取消。
- Unload 失败保留包以供重试，会话已经释放。
- Shutdown 会取消进行中的加载/进入并等待操作结束，再清理；游戏必须响应传入取消令牌。关闭失败可再次调用 Shutdown。
- Session 自身只有同步 QF 生命周期；需要等待的任务和资源清理写在 IGame.ExitAsync 中，等待完成后才释放会话。

与 Unity 无关的部分也可接到其他引擎；本仓库引入的是 QF Unity 版原始源码，因此要在纯 .NET/Godot 使用时，还需选择对应 QF 内核版本或处理其文件级 Unity using。

## 游戏加载

### 职责边界

```text
App：初始化资源系统 → 更新版本/清单 → 下载 → 准备游戏资源包
GameLauncher：切换游戏、创建/关闭子会话
UnityGamePackageLoader：加载入口 → 实例化游戏 → 退出 → 销毁对象
资源适配器：按地址加载入口，持有并释放资源句柄
YooAsset / Addressables：依赖资源、缓存、引用计数、底层资源包
```

GameLauncher 和 Session 不引用资源库。UnityGamePackageLoader 注入 IGameDefinitionLoader，不直接调用 Resources 或 AssetBundle API。GameInfo 只保存 `id` 与 `entryAddress`，不再包含 bundlePath。

### 默认接入 YooAsset

使用 YooAsset 3.0.7 API。App 先初始化 YooAsset，准备 ResourcePackage 的清单和需要的资源，再创建加载器：

```csharp
var launcher = new GameLauncher(
    App.Interface,
    new UnityGamePackageLoader(new YooAssetGameDefinitionLoader()),
    new[] {
        new GameInfo { id = "game-a", entryAddress = "Entry" },
        new GameInfo { id = "game-b", entryAddress = "Entry" }
    });
await launcher.OpenAsync("game-a");
await launcher.CloseAsync();
```

默认查找与游戏 ID 同名的包：game-a / game-b。两个包内都能使用入口地址 Entry。适配器不使用全局 DefaultPackage，因此切换不会改变大厅或其他游戏的默认包。

若项目的包命名不同，可以显式映射，也可以让多个游戏复用一个包：

```csharp
var definitions = new YooAssetGameDefinitionLoader(
    info => resourceService.GetPreparedPackage(info.id));
var loader = new UnityGamePackageLoader(definitions);
```

resourceService 是项目已有资源服务，不属于本框架。包的 Host/Offline/Web 模式、版本选择、下载进度和重试由 App 决定。加载器不会在后台静默更换版本或强制卸载整个包。

### 入口与游戏生命周期

游戏资源包含一个 GameDefinition 入口资源和一个 inactive 根 Prefab：

1. GameDefinition.Configure 注册本次游戏的新 Model/System。
2. Session 初始化本地对象，本地缺少的服务从 App 查找。
3. 创建新的 GameEntry 实例，绑定会话并等待 OnEnterAsync，然后激活。
4. 退出时取消会话令牌，等待 OnExitAsync，再销毁游戏对象。
5. 对象销毁后释放会话，最后释放入口资源句柄。

GameDefinition 只保存配置，运行状态放 Model。业务初始化放 OnEnterAsync，异步清理放 OnExitAsync；不要在 Awake 中读取尚未绑定的会话。

入口引用的 Prefab 及其依赖会随入口句柄一起保留。游戏动态加载的额外资源，由游戏在 OnExitAsync 中销毁实例并释放对应句柄，之后才允许子会话和入口释放。

### 引用计数与取消

- YooAsset 适配器使用指定包的 LoadAssetAsync，退出时 Release 该次句柄。
- Addressables 适配器使用 LoadAssetAsync，退出时 Release 该次句柄。
- 不调用 AssetBundle.Unload(true)、Resources.UnloadAsset 或包级强制卸载来实现游戏切换。
- 游戏的普通实例由 Object.Instantiate 创建，所以由 Object.Destroy 销毁；加载句柄一直保留到实例销毁之后，不调用 Addressables.ReleaseInstance。
- 失败、验证入口不合法、取消均释放已经取得的句柄。
- 取消不会强行打断底层加载；等待底层操作结束后处理取消并释放，避免失去句柄。网络超时策略由资源系统设置。
- 释放句柄代表归还本次引用，资源是否立即卸载由资源系统和其他持有者决定。

App 结束时先 `await launcher.ShutdownAsync()`，再清理自己拥有的资源包，最后 Deinit App。游戏加载器不拥有 App 的全局 YooAsset 驱动。

### 示例配置

Assets/SessionDemo/App 的 SampleGameResources 是示例 App 的资源准备流程：编辑器模拟模式，Player 离线模式。它只销毁由自己创建的资源包；复用的外部包不由它销毁。

生成器将资源放到 `Assets/SessionDemo/GameA`、`GameB`，为 game-a/game-b 建立 YooAsset 的 SessionDemo 收集分组。每个包启用寻址，按文件名寻址，因此入口地址为 Entry。资源目录无需放进 Resources。构建菜单还会将 YooAsset 生成的 link.xml 保存到 Assets/SessionDemo/Editor/Linker，保留仅被资源包引用的游戏入口及组件类型；其他代码热更新方案仍需遵循自身裁剪规则。

App、GameA、GameB 分别编译为 Demo.App、Demo.GameA、Demo.GameB。App 程序集没有反向引用游戏程序集，只有编辑器生成器为了组装示例引用全部游戏。

### 可选 Addressables

安装 Addressables 后，版本定义会启用 QFramework.Games.Addressables 程序集。业务程序集引用它，改用：

```csharp
new UnityGamePackageLoader(new AddressablesGameDefinitionLoader())
```

App 应先准备需要的 catalog 与依赖。入口使用 Addressables 中的唯一地址，不采用 YooAsset 的“包内 Entry”命名方式。切换此适配器时，不需要改 Session 或 GameLauncher；示例的 SampleGameResources 也要替换为 App 的 Addressables 准备流程。

### 程序集

QFramework 主程序集不引用 YooAsset/Addressables。默认适配器单独编译到 QFramework.Games.YooAsset，可选适配器单独编译到 QFramework.Games.Addressables。

本包默认依赖 YooAsset；如果项目只使用 Addressables，可以去掉 YooAsset 适配器和对应 package.json 依赖，保留核心与 Addressables 接入。示例位于项目 Assets 中，可单独移除或改用项目的资源准备流程。不要为切换资源库改写子会话。

参考官方文档：[YooAsset 源码与说明](https://github.com/tuyoogame/YooAsset/tree/3.0.7)、[Addressables 加载句柄](https://docs.unity3d.com/Packages/com.unity.addressables@2.3/manual/AddressableAssetsAsyncOperationHandle.html)。
