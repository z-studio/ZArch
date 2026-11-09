# ZArch

ZArch 帮助你把游戏状态、业务规则和界面分开。业务代码按 Model、System、View、Command、Query 分工，通过能力接口约束常用调用方向；入口负责注册与生命周期。子 Scope 和游戏切换按需使用。

## 两层依赖

```text
ZArch ← ZArch.Unity
```

- **Core**：纯 C# 的状态、规则、事件、绑定、服务与生命周期，以及单一游戏切换状态机。无 Unity 引用。
- **Unity**：场景入口、界面绑定、场景加载和编辑器检查。`ZArch.Unity.Editor` 是这一支持层的编辑器程序集。

未来 Godot 适配单独引用 Core。可复用的业务代码也应避免引用具体引擎类型，通过状态和事件驱动表现。

## 从完整场景开始

1. [Counter](Samples/Counter/Counter.unity)：四个脚本完成按钮计数。
2. [Inventory](Samples/Inventory/Inventory.unity)：两个界面共享数据、显示与隐藏面板。
3. [Lobby](Samples/Games/Lobby.unity)：切换 Game A/B、返回大厅；金币保留，回合数重置。
4. [Game A](Samples/Games/GameA.unity) 和 [Game B](Samples/Games/GameB.unity)：可直接 Play，使用临时玩家数据。

样例使用 uGUI，包清单已声明对应依赖。按钮 OnClick、入口的 Views、文本引用、游戏列表均保存在 Inspector。运行时不动态生成 UI。菜单 `ZArch/Samples/Rebuild Sample Scenes` 可生成样例并补充 Build Settings；在 UPM 使用方生成到 `Assets/ZArchSamples`，源码项目中覆盖包内样例。生成前先保存或打开一个场景。

通过 UPM 使用时，先打开包内的 Counter 场景，再执行生成菜单；随后从 `Assets/ZArchSamples` 打开场景。菜单会补齐游戏切换需要的 Build Settings。

## 一个功能如何运行

```text
入口配置服务 → 初始化完成 → 绑定 View / OnReady
→ 按钮发送 Command → Command 修改 Model 或调用 System
→ 状态/事件通知 View
```

- View 通过 Command 发起业务状态变更；简单 Command 可直接修改 Model，共享规则放入 System。
- 普通业务类继承基类即可，无需手动组合能力接口；基础服务接口继承 IService。
- 单场景入口继承 `SceneArchitecture`，配置放在 `ConfigureServices`。
- `ArchitectureView.OnReady` 表示所属入口初始化已完成。`Track` 记录的订阅随解除绑定清理。
- 动态弹窗/对象池使用 `entry.BindView(view)` 和 `entry.UnbindView(view)`。
- 普通事件局限于当前 Scope；应用级通知显式使用 AppEvent。
- 正常关闭统一等待 `entry.ShutdownAsync()`；父 Architecture/Scope 关闭时也会先结束游戏会话。

## 文档

- [快速开始](Documentation/01-Getting-Started.md)
- [核心与 Scope](Documentation/02-Core.md)
- [业务代码怎么写](Documentation/03-Application-Logic.md)
- [Unity 入口与界面](Documentation/04-Unity.md)
- [异步与关闭](Documentation/05-Async-Lifecycle.md)
- [游戏切换](Documentation/06-Game-Switching.md)
- [API 速查](Documentation/07-API-Reference.md)
- [源码维护](Documentation/08-Maintenance.md)

这是全新 API，不提供旧版接口或迁移适配层。框架在 Unity 主线程串行使用，不提供并发线程安全保证。
