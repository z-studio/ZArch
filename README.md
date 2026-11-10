# 先运行，再读业务

这个项目演示：**启动 → 登录 → 大厅 → 游戏 → 注销 → 换账号登录**。

打开 [App 场景](Assets/SessionDemo/App/App.unity) 并运行：Alice 登录 → 打开 A → 玩一局 → 打开 B → 返回大厅 → 注销 → Bob 登录。

你会看到：换游戏时金币保留、游戏回合重新开始；换账号时账号数据重新建立。示例使用模拟服务器，不会发起真实登录或下注。

## 第一次只读这三个位置

| 顺序 | 看哪里 | 先看什么 |
| --- | --- | --- |
| 1 | [AppLogic.cs](Assets/SessionDemo/App/AppLogic.cs) | 文件上半部分的 Start、Login、OpenGame、ReturnToLobby、Logout 五个入口 |
| 2 | [PlayRoundCommand.cs](Assets/SessionDemo/GameA/PlayRoundCommand.cs) | 一局怎样改变游戏回合和账号金币 |
| 3 | [GameAEntry.cs](Assets/SessionDemo/GameA/GameAEntry.cs) | 游戏进入时怎样绑定显示、监听金币和加载内容 |

按钮都走同一条路径：**Inspector → AppEntry → AppLogic → 框架**。所有游戏按钮共用 `AppEntry.OpenGame(string)`，Inspector 参数填 `game-a` 或 `game-b`，无需为每个游戏再加按钮方法。

第一次可以跳过 AppEntry 中的异常显示与关闭兜底、AppView 的显示细节、AppProcedures 的流程转接，以及包内加载器实现。它们不是新增游戏时需要复制的代码。

## 只记住三种使用期间

| 数据属于谁 | 例子 | 什么时候清理 |
| --- | --- | --- |
| App | 服务器配置、基础组件 | App 关闭 |
| 当前账号 | 身份、钱包、大厅连接 | 注销 |
| 当前游戏 | 回合、游戏服务、资源引用 | 退出游戏 |

Session 就是管理这一段使用期间的容器。游戏获取钱包时，自动找到当前账号的钱包。业务无需手动创建、初始化或销毁 Session。

普通 `SendEvent / RegisterEvent` 与 QF 一样在整个 App 架构范围内通信。账号发送金币变化，游戏直接监听；游戏退出自动解除游戏订阅，注销解除账号及游戏订阅，App 订阅保留。

Procedure 继续编排项目步骤和界面：示例为 Launch → Login → Lobby → Game。它跟随 AppLogic 的操作结果切换，不在 OnLeave 重复卸载游戏或销毁账号。

## 修改现有业务

| 想做什么 | 修改位置 |
| --- | --- |
| 每局奖励 | GameA/PlayRoundCommand.cs |
| 接配置与登录服务器 | App/AppBackend.cs，实现 IAppBackend |
| 账号和钱包结构 | App/DemoApp.cs |
| 登录后注册服务、等待账号数据 | AppLogic.ConfigureAccountAsync、PrepareAccountAsync |
| 展示界面或调整按钮状态 | App/AppView.cs |
| 项目自己的启动 Procedure 步骤 | App/AppProcedures.cs |

资源初始化等待、按需下载、取消、回收由已有适配器处理。真实列表项有房间、渠道等参数时，调用 `AppEntry.OpenGameAsync(id, new GameLaunchData(roomId))`；普通 Inspector 按钮只需填写游戏 ID。

## 照着接入一个 GameC

下面从最少的业务开始：点击一次按钮，游戏分数加 1。它不需要复制 A/B 的资源演示、金币事件或任何生命周期清理代码。

### 1. 建独立程序集与三个脚本

新建 `Assets/SessionDemo/GameC`，在 Unity 中创建 Assembly Definition，命名为 `Demo.GameC`，引用 `QFramework`、`Demo.App`、`Unity.ugui`。App 的程序集不增加对它的引用。

`GameCDefinition.cs`：只注册本游戏的数据。

```csharp
using QFramework;
using UnityEngine;

namespace Demo.GameC {
    [CreateAssetMenu(menuName = "Session Demo/GameC")]
    public sealed class GameCDefinition : GameDefinition {
        public override void Configure(Session session) {
            session.RegisterModel(new GameModel());
        }
    }
}
```

`GameCLogic.cs`：保存数据并通过 Command 修改。

```csharp
using QFramework;

namespace Demo.GameC {
    public sealed class GameModel : AbstractModel {
        public readonly BindableProperty<int> score = new(0);
        protected override void OnInit() { }
    }
    public sealed class AddScoreCommand : AbstractCommand {
        protected override void OnExecute() {
            this.GetModel<GameModel>().score.Value++;
        }
    }
}
```

`GameCEntry.cs`：只绑定显示和按钮。

```csharp
using System.Threading;
using System.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Demo.GameC {
    public sealed class GameCEntry : GameEntry {
        [SerializeField] private UnityEngine.UI.Text m_Score;

        protected override Task OnEnterAsync(CancellationToken cancellation) {
            this.GetModel<GameModel>().score
                .RegisterAndInvoke(ShowScore)
                .UnregisterWhenSessionEnds(Session);
            return Task.CompletedTask;
        }
        private void ShowScore(int value) => m_Score.text = $"Score: {value}";
        public void PlayRound() => this.SendCommand(new AddScoreCommand());
    }
}
```

### 2. 配置入口与资源包

- 创建 GameC 根对象，挂 GameCEntry，在其下放 Canvas、Text 和 Button。Text 拖给 m_Score，按钮 OnClick 绑定 GameCEntry.PlayRound。
- 将根对象设为未激活，再保存为 `GameC/Game.prefab`。加载器会先绑定会话、完成 OnEnterAsync，再激活它。
- 用 **Create → Session Demo → GameC** 创建 GameCDefinition，保存为 `GameC/Entry.asset`，Prefab 填 Game.prefab，SceneAddress 留空。
- 在 YooAsset 资源收集配置中新建包 `game-c`、分组 `SessionDemo`，收集 GameC 目录；地址规则用 `AddressByFileName`，打包规则用 `PackDirectory`。

GameB 已演示场景入口：Prefab 留空、SceneAddress 填场景地址。场景与普通资源需要分别打包，业务入口写法相同。

### 3. 加入目录，绑定一个按钮

示例的游戏目录来自 DemoBackend，在 AppBackend.cs 返回的 games 数组中增加一行：

```csharp
new GameInfo { id = "game-c", packageName = "game-c", entryAddress = "Entry" }
```

真实项目由服务器配置返回该行，不需要为每个游戏增加 App 业务分支。

在大厅添加或复制一个游戏按钮，OnClick 绑定 **AppEntry.OpenGame(string)**，字符串填 `game-c`。将按钮加入 AppView 的 GameButtons 数组，使加载、注销时能统一禁用。

**新增游戏到这里就完成了。** 无需修改 AppLogic、Procedure、AppFlow，也无需编写 Session 创建、取消、卸载和订阅清理。

编辑器资源模拟模式可直接运行。构建 Player 前执行 **QFramework → Sessions → Build Demo YooAsset Packages**；该菜单会构建包含 `SessionDemo` 分组的包，包括新加的 game-c。正式项目沿用 ZFramework 的资源构建工具。

**Create Lobby And Games Demo** 用于重新生成内置 A/B 示例；手动添加 GameC 按钮后，不要用它覆盖已经调整的 App 场景。

## 接真实项目时再看

当前工程通过 UPM 引用本机 ZFramework 等基础包，其他机器需要安装相同依赖。模拟配置和账号由 DemoBackend 提供，生产接口按项目协议实现 IAppBackend；必要的取消信号继续向下传递。

完整生命周期、失败重试、ZFramework UI 以及 DropBall / Super-Ace 迁移位置见 [框架说明](Packages/com.z-studio.qframework/README.md)。
