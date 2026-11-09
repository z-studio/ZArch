# API 速查

| 需求 | API |
| --- | --- |
| 配置应用 | Architecture.Start / StartAsync |
| 获取服务 | Get<T> / GetModel<T> / GetSystem<T> |
| 配置 Unity 服务 | SceneArchitecture.ConfigureServices |
| 异步准备场景 | SceneArchitecture.OnInitializeAsync |
| 等待入口就绪 | entry.Initialization |
| 绑定动态界面 | entry.BindView / UnbindView |
| 界面访问服务 | ArchitectureView.OnReady |
| View 发起业务修改 | SendCommand |
| 组合读取 | SendQuery |
| 界面订阅清理 | Track / OnUnbind |
| 当前 Scope 事件 | SubscribeEvent / PublishEvent |
| 应用事件 | SubscribeAppEvent / PublishAppEvent |
| 基础服务（T : IService） | GetService<T> |
| 进入游戏 | GameEntry.OnEnterAsync |
| 退出游戏 | GameEntry.OnExitAsync |
| 打开、切换游戏 | GameLauncher.OpenAsync |
| 返回大厅 | CloseAsync |
| 关闭完整环境 | await entry.ShutdownAsync / Architecture.ShutdownAsync |
| 检查配置 | ZArch/Validate Loaded Scenes |

## 高级服务 API

RootScope.CreateChild/CreateChildAsync、RegisterScopedFactory、RegisterAlias、Bind、Resolve、TryResolve、RegisterTransient、RegisterOwnedTransient、DisposeAsync。

Scope.Publish(message, Bubble) 可沿父链通知，不向子级广播。Architecture.Subscribe/Publish 属于应用；Scope.Subscribe/Publish 属于局部。

## 错误定位

- View 未绑定：放入入口 Views，或初始化完成后 BindView。
- View 已有 Owner：先由原入口 UnbindView。
- 找不到服务：核对注册与获取的类型；不会自动注册实现的所有接口。
- 配置期解析失败：改用工厂，在初始化阶段获取依赖。
- 场景找不到：检查 Scene Location 与启用的 Build Settings。
- 独立 Play 失败：配置 Standalone Game 与临时应用服务。
- CloseFailed：再次 CloseAsync 完成清理；不要直接进入另一游戏。
- ShutdownFailed：修复外部失败原因后再次 ShutdownAsync。

核心内部类型不作为业务依赖。游戏业务使用定义、入口和 Launcher 即可。

各角色可用 API 见[权限表](03-Application-Logic.md)。IArchitectureContext 本身只表示所属 Scope，不赋予获取服务、发送操作或发布事件等能力。
