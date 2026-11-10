# ZFramework 基础，QFramework 业务

启动读取服务器配置 → 登录 → 大厅 → 进入游戏 → 返回大厅；注销会关闭游戏、清理账号，再回到登录界面，可以重登或换账号。

```text
App 常驻架构
  └─ 账号会话（每次登录新建）
       └─ 游戏会话（每次进入新建）
```

ZFramework.Unity 统一提供资源池、UI、实体、声音、场景和网络。QFramework 保留 Model / System / Command / Query 业务规范，接入层记录游戏拥有的资源引用。

- `Packages/com.z-studio.qframework/Runtime`：框架、流程和 ZFramework 接入。
- `Assets/SessionDemo/App`：启动、登录、大厅与账号数据。
- `Assets/SessionDemo/GameA`、`GameB`：独立程序集的游戏代码和资源。
- `Assets/SessionDemo/Editor`：场景生成、资源收集和构建。

直接运行 [App 场景](Assets/SessionDemo/App/App.unity)。先以 Alice 登录玩一局，再注销并以 Bob 登录，检查钱包和回合是否重新建立。

示例使用明确的模拟后端 `IAppBackend / DemoBackend`，不连接真实认证服务器。生产项目在此接入服务器配置和登录协议。编辑器使用 ZFramework 的资源模拟模式；构建 Player 前执行 **QFramework → Sessions → Build Demo YooAsset Packages**。

当前工程通过 UPM 引用本机 ZFramework、ZFramework.Unity、UniTask、UniUtils、Shell 包，不复制基础框架源码。其他机器需要先安装同一套基础依赖，再安装本包。

完整职责、生命周期、资源所有权和接入方式见 [框架说明](Packages/com.z-studio.qframework/README.md)。验证方法见 [Tests/Unity](Tests/Unity/README.md)。
