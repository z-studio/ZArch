# 游戏切换

本功能用于大厅进入独立游戏。背包、任务、音频等业务功能用普通 Model/System 组织。

## 三个对象

| 对象 | 职责 |
| --- | --- |
| GameDefinition | 配置游戏 ID、场景及每次会话的新服务 |
| GameEntry | 游戏场景唯一入口，处理进入、退出和 Views |
| GameLauncher | 应用入口，配置共享服务与游戏列表 |

## 配置流程

1. 继承 GameDefinition，重写 ConfigureServices 注册游戏 Model/System；没有规则配置时直接使用基础资产。
2. 创建该类型资产，填写 Id、Scene Location，默认提供器保持 build-settings。
3. 游戏场景挂一个启用的 GameEntry，拖入 Views 与 Standalone Game。
4. 大厅挂 GameLauncher 子类，在 ConfigureServices 注册共享玩家数据，把游戏资产拖入 Games。
5. 场景加入启用的 Build Settings，运行大厅。

```csharp
await launcher.Initialization;
await launcher.OpenAsync("battle", new BattleArgs(12), cancellationToken);
await launcher.CloseAsync();
await launcher.ShutdownAsync();
```

GameDefinition 是共享配置资产，不存放运行状态。每次 ConfigureServices 都注册新对象；应用共享状态在 Launcher 中注册。

## 游戏进入与退出

```csharp
protected override Task OnEnterAsync(object arguments, CancellationToken cancellationToken) {
    if (arguments is not BattleArgs args)
        throw new ArgumentException("Battle requires BattleArgs.");
    return PrepareBattleAsync(args, cancellationToken);
}
protected override Task OnExitAsync() => SaveBattleAsync();
```

OnEnterAsync 成功后才激活 View。独立 Play 默认参数为 null，需要参数的游戏应在回调中明确提供调试默认值或报出所需配置。

## 切换行为

OpenAsync 先验证目标定义，再退出当前游戏并进入目标。未知 ID、无效配置或预先取消不会先关闭当前游戏。切换后新游戏失败时保留大厅，不恢复已经销毁的旧会话。

同一时间只允许一个切换。UI 可以使用 Games.IsBusy 防止重复点击，并捕获错误进行显示。

State 显式暴露 Idle、Opening、Running、Closing、CloseFailed、ShuttingDown、ShutdownFailed、Closed。清理失败后必须先 CloseAsync 清理成功，再进入其他游戏。关闭失败用 ShutdownAsync 重试。

## 独立 Play

GameEntry.Standalone Game 指向相同定义。直接运行时创建临时应用和游戏环境；需要临时玩家数据时重写 ConfigureStandaloneServices。通过大厅加载时使用大厅的应用状态。

## 引擎扩展

Core 只有 IGameDefinition、IGameContentLoader、IGameContent 和一个 GameSwitcher。加载器返回内容对象，内容对象提供 EnterAsync、ExitAsync、UnloadAsync；会话及状态管理为内部实现。

UnityGameContentLoader 接受 IGameSceneProvider。自定义提供器在场景 Start 前通过 bind 回调交付场景，并保留资源句柄供卸载。开始加载后若无法中止，仍应交回句柄，由加载器处理取消和回滚。加载失败且回滚失败时通过 GameContentLoadException 交还未释放内容。

Godot 适配可实现同一组 Core 接口，不依赖 Unity 类或场景协议。

完整代码见 [Lobby](../Samples/Games/LobbyEntry.cs) 与 [RoundGameDefinition](../Samples/Games/RoundGameDefinition.cs)。
