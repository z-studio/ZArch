# GameModules：游戏内容模块

GameModules 用来管理“大厅、战斗、关卡、玩法”等可进入和退出的内容单元。它把模块 Scope 与内容加载绑定为一次事务：进入失败会回滚，退出时先卸载内容，再释放 Scope。

## 1. 定义模块

纯 C# 模块实现 `IGameModule`：

```csharp
using ZArch;
using ZArch.GameModules;

public sealed class BattleModule : IGameModule
{
    public string Id => "battle";

    public void Configure(ArchitectureScope scope, GameLaunchContext context)
    {
        scope.Register<BattleModel>(new BattleModel());
        scope.Register<BattleSystem>(new BattleSystem());
    }
}
```

`GameLaunchContext` 可携带本次进入需要的参数。模块只负责注册其 Scope 内的服务，不负责直接加载场景。

## 2. 创建 Unity 模块资产

Unity 项目通常继承 `UnityGameModuleAsset`，在 Inspector 中设置：

- `Id`：模块唯一标识，例如 `battle`；
- `Scene Provider Id`：默认 `build-settings`；
- `Scene Location`：Build Settings 中的场景名或路径。

```csharp
using ZArch;
using ZArch.GameModules;
using ZArch.GameModules.Unity;
using ZArch.Unity;

[UnityEngine.CreateAssetMenu(menuName = "Game/Battle Module")]
public sealed class BattleModuleAsset : UnityGameModuleAsset
{
    public override void Configure(
        ArchitectureScope scope,
        GameLaunchContext context)
    {
        scope.Register<BattleModel>(new BattleModel());
        scope.Register<BattleSystem>(new BattleSystem());
    }
}
```

把创建出的资产加入 `GameModuleCatalog`。Catalog 会检查空配置与重复 Id。

## 3. 在根 Scope 注册 Launcher

```csharp
using ZArch;
using ZArch.GameModules;
using ZArch.GameModules.Unity;
using ZArch.Unity;

public sealed class GameBootstrap : ArchitectureHostBootstrap
{
    [UnityEngine.SerializeField]
    private GameModuleCatalog m_ModuleCatalog;

    protected override Architecture CreateArchitecture()
        => new ArchitectureHost();

    protected override void ConfigureRoot(ArchitectureScope root)
    {
        var launcher = new GameLauncher(
            new GameScopeFactory(root),
            new UnityGameContentLoader(),
            m_ModuleCatalog);

        root.Register<IGameLauncher>(launcher);
    }
}
```

默认 `UnityGameContentLoader` 使用 Build Settings Provider，以 Additive 模式加载场景。若接入 Addressables 或自研资源系统，实现并传入自己的 `IGameSceneProvider`。

## 4. 进入与退出

```csharp
using System.Threading;
using ZArch.GameModules;

var launcher = root.Resolve<IGameLauncher>();

var context = new GameLaunchContext(12); // Arguments 可放任意业务参数对象
await launcher.EnterAsync("battle", context, cancellationToken);

// 返回大厅或结束玩法
await launcher.ExitAsync();
```

Launcher 同一时间只允许一个切换操作，也只维护一个 Active 模块。已有模块处于 Active 时，必须先等待 `ExitAsync()`，再进入目标模块。

进入流程为：

1. 从 Catalog 查找模块。
2. 创建模块子 Scope，并执行 `Configure` 与生命周期初始化。
3. 通过内容加载器加载场景。
4. 将场景入口绑定到模块 Scope。
5. 成功后公布为当前模块。

任何一步失败都会卸载已加载内容并释放新 Scope，不会留下半激活模块。

## 5. 场景入口

每个模块场景必须恰好包含一个 `GameSceneEntry`。继承它并在 `OnBindScope` 中绑定场景对象：

```csharp
using UnityEngine;
using ZArch;
using ZArch.GameModules.Unity;

public sealed class BattleSceneEntry : GameSceneEntry
{
    [SerializeField] private BattleHud m_Hud;
    [SerializeField] private BattleWorld m_World;

    protected override void OnBindScope(ArchitectureScope scope)
    {
        m_Hud.BindScope(scope);
        m_World.Initialize(scope);
    }
}
```

这样场景对象不会自行搜索全局 Bootstrap，也不会误用根 Scope。

## 6. 退出策略

应用退出或测试结束时调用：

```csharp
await launcher.ShutdownAsync();
```

`ShutdownAsync` 会结束当前模块并阻止后续进入。Unity 项目若要求完整等待场景卸载，应配合 Bootstrap 的 `RequiresExplicitShutdown`，先等待 Launcher，再关闭 Architecture。

## 7. 何时使用 GameModules

适合：

- 内容以场景或资源组为边界；
- 每个玩法有独立 Model/System 生命周期；
- 需要可验证的进入失败回滚；
- 计划替换场景加载后端。

简单项目只有一个永久场景时，直接使用根 Scope 或 `SceneScopeBinder` 即可，不必引入模块层。

下一篇：[API 速查](07-API-Reference.md)
