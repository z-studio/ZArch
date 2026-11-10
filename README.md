# 从示例开始读

这个项目做一件事：**启动 → 登录 → 大厅 → 游戏 → 注销 → 再次登录**。

打开 [App 场景](Assets/SessionDemo/App/App.unity)，先以 Alice 登录玩一局，再注销并以 Bob 登录。你会看到：换游戏时钱包保留，换账号时钱包重新建立。

示例已经使用 ZFramework 的 Procedure：`ProcedureLaunch → ProcedureLogin → ProcedureLobby → ProcedureGame`。Procedure 跟随操作结果切换界面；账号和游戏的创建、取消、释放由 AppFlow 统一负责。

## 先记住三种数据

| 数据属于谁 | 例子 | 什么时候清理 |
| --- | --- | --- |
| App | 服务器配置、ZFramework 基础组件 | App 关闭 |
| 当前账号 | Token、钱包 | 注销 |
| 当前游戏 | 回合数、游戏资源和实例 | 退出游戏 |

代码里的 **Session 就是这一段使用期间的数据容器**。游戏查找钱包时，自己没有，就向账号会话查找；账号查找服务器配置时，再向 App 查找。

## 按这个顺序看代码

### 1. 看按钮调用哪里

[AppEntry.cs](Assets/SessionDemo/App/AppEntry.cs) 是场景入口，只连接业务和界面。按钮在 Inspector 中直接绑定命名方法：

```csharp
public void LoginAlice() => Run(() => LoginAsync("Alice"));
public void OpenGameA() => Run(() => Flow.OpenGameAsync("game-a"));
public void Logout() => Run(() => Flow.LogoutAsync());
```

`Run` 负责等待异步操作、显示错误。业务流程返回 Task，按钮不用各自重复编写异常处理。

### 2. 看启动和登录实际做什么

[AppLogic.cs](Assets/SessionDemo/App/AppLogic.cs) 是业务流程，重点看三个命名方法：

- `PrepareAppAsync`：读取服务器配置，等待基础资源组件。游戏包在点击时才准备。
- `ConfigureAccountAsync`：认证成功后，向当前账号会话注册账号和钱包。
- `CreateGameLauncher`：让游戏以当前账号会话为父级。

登录的调用顺序是：

```text
点击 LoginAlice
→ AppLogic.LoginAsync("Alice")
→ 框架调用 ConfigureAccountAsync
→ 后端返回账号数据
→ 新建 AccountModel、WalletModel、AccountSystem
→ 初始化账号服务，等待账号连接就绪
→ 框架进入大厅状态
→ AppView 显示大厅
```

注销调用 `Flow.LogoutAsync()`，框架会等待游戏退出、归还资源，再释放账号数据。登录和加载过程中发生注销，也走这条路径。

### 3. 看一局游戏怎样修改数据

[PlayRoundCommand.cs](Assets/SessionDemo/GameA/PlayRoundCommand.cs) 中只有两个业务动作：

```csharp
var roundModel = this.GetModel<RoundModel>();
var wallet = this.GetModel<WalletModel>();

roundModel.rounds.Value++;   // 当前游戏的回合。
wallet.SetCoins(wallet.coins.Value + 10); // 当前账号的钱包，同时发布金币变化。
```

再看 [GameAEntry.cs](Assets/SessionDemo/GameA/GameAEntry.cs)：进入游戏时接收房间参数、绑定回合文字、加载内容；按钮发送 PlayRoundCommand。A 使用 Prefab 入口，B 使用 Additive 场景入口，二者的业务写法一致。

`GameResources` 由 GameEntry 基类提供，已经绑定当前游戏包。通过它加载的资源和实例会在游戏退出时统一归还。

## 想改什么，去哪里改

| 想修改 | 文件 |
| --- | --- |
| 每局奖励 | GameA/PlayRoundCommand.cs |
| 登录返回的数据、服务器接口 | App/AppBackend.cs |
| 账号和钱包的数据结构 | App/DemoApp.cs |
| 启动、登录的业务步骤 | App/AppLogic.cs |
| 界面显示、按钮是否可点击 | App/AppView.cs |
| 资源组件就绪、按需包准备 | App/AppResources.cs |
| Procedure 的入口与切换 | App/AppProcedures.cs |

首次阅读从 AppProcedures 的启动入口开始，随后看 AppEntry 的按钮和 AppLogic 的业务步骤。Procedure.OnLeave 不再重复清理游戏或账号。

`AppView` 只显示状态；`AppResources` 只处理资源准备。阅读业务流程时，从 AppEntry 跳到 AppLogic 即可。

## 事件按 QF 的方式直接使用

默认事件由 App 根架构持有，账号和游戏共用。账号发送 `SendEvent(new CoinsChanged(value))`，游戏直接 `RegisterEvent<CoinsChanged>(OnCoinsChanged)`，不需要寻找父级或指定传播方向。游戏退出自动解除游戏订阅，注销自动解除账号及游戏订阅，App 订阅保留。

示例中大厅通过钱包属性刷新，游戏通过普通金币事件刷新；两种方式都使用同一份账号数据。会话只管理数据、服务和订阅的生命周期，事件统一在 App 架构范围内通信。

## 基础约定

- ZFramework.Unity 提供资源池、UI、实体、声音、场景和网络等基础能力。
- QFramework 提供 Model / System / Command / Query 的业务规范。
- 示例后端 DemoBackend 返回模拟配置和账号，不连接真实认证服务器；接真实服务时实现 IAppBackend。
- CancellationToken 是注销或退出的取消信号，异步操作把它继续传下去。
- App、GameA、GameB 是独立程序集，只有游戏依赖 App。

编辑器直接使用资源模拟模式。构建 Player 前，执行 **QFramework → Sessions → Build Demo YooAsset Packages**。需要重新生成场景时，先保存或关闭未命名场景，再执行 **Create Lobby And Games Demo**。

当前工程通过 UPM 引用本机 ZFramework 等基础包，其他机器需先安装同一套依赖。完整接入、生命周期和失败处理见 [框架说明](Packages/com.z-studio.qframework/README.md)。
