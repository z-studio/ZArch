# QFramework：子会话与独立游戏包

一个 App 主架构管理大厅和共享服务。每次进入游戏创建子会话，游戏共享钱包、账号和网络，退出时释放本地状态与资源句柄。默认使用 YooAsset 3.0.7。

```text
Packages/com.z-studio.qframework/
  Runtime/                    框架、子会话、游戏加载和资源适配器
  README.md                   完整接入说明

Assets/SessionDemo/
  App/                        大厅场景、共享钱包和资源初始化
  GameA/                      游戏 A 的代码、入口资源和 Prefab
  GameB/                      游戏 B 的代码、入口资源和 Prefab
  Editor/                     示例生成器、收集配置和裁剪保留配置
```

App、GameA、GameB 是三个独立程序集，只有游戏依赖 App。每个游戏的代码和资源放在同一个目录。

## 运行

打开 [Lobby 场景](Assets/SessionDemo/App/Lobby.unity)，点击 Play。进入 A 玩一局，再切换 B、返回大厅、重进 A：钱包保留，游戏回合重置。

编辑器使用 YooAsset 模拟模式。构建 Player 前，先执行 **QFramework → Sessions → Build Demo YooAsset Packages**。

需要重建示例时，先保存或关闭未命名场景，再执行 **QFramework → Sessions → Create Lobby And Games Demo**。

依赖安装、子会话、事件、加载器和资源生命周期统一见 [框架使用说明](Packages/com.z-studio.qframework/README.md)。
