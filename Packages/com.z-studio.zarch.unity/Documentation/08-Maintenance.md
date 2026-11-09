# 源码维护

## 依赖与目录

```text
Core/                    ZArch.Core，noEngineReferences
  Architecture/          应用环境
  Scopes/                子环境、服务与关闭前置流程
  ModelSystem/           业务上下文与状态/规则
  Operations/            Command / Query
  Binding/ Events/       状态绑定与通知
  Lifecycle/ Services/   生命周期与服务注册
  Games/                 单一切换状态机与引擎扩展接口
Unity/Runtime/           ZArch.Unity → Core
  Hosting/               统一入口、场景与 View
  Games/                 Unity 定义、场景内容与提供器
Unity/Editor/            ZArch.Unity.Editor
  Debugging/ Validation/ 调试与配置检查
Samples/                 完整教学场景
Tests/Editor/            核心、Unity 边界与真实场景测试
```

Core 不引用 UnityEngine/UnityEditor。引擎适配之间互不依赖。新引擎扩展通过现有的定义、内容加载与内容生命周期接口接入，不往 Core 引入引擎对象。

## 所有权

- Architecture 拥有根 Scope，Scope 拥有服务与子 Scope。
- GameSwitcher 拥有单个游戏会话，父 Scope 的关闭前置流程自动等待它退出。
- 内容对象拥有引擎加载句柄，只有卸载成功后才能释放对应服务。
- ArchitectureEntry 拥有 View 绑定；View 拥有自己的订阅。
- Bind 的外部对象不由 Scope 销毁。

关闭前置流程仍允许解析服务和发布应用事件，但禁止创建新子环境；失败时不进入破坏性的 Dispose 阶段。普通对象 Dispose 错误继续聚合报告，不能假设它们具有重试能力。

## 简化原则

使用显式注册与明确操作入口。角色能力由框架接口组合，入门示例只需继承相应基类。不要把扩展方法重新开放给全部 IArchitectureContext，也不要通过无类型限制的 GetService 绕过角色边界。

源代码和文档只描述当前设计，不维护旧接口或迁移分支。

## 验证要求

1. 核心独立 .NET 编译，且无引擎引用。
2. Unity 核心和状态机测试：取消、失败回滚、关闭失败重试、父环境退出顺序。
3. View 测试：多 Owner 拒绝、初始化失败清理、解除和重新绑定。
4. 实际 Play 测试：计数器、背包同步与销毁清理、游戏切换、共享金币、回合重置、独立运行。
5. 配置检查、GUID/资源引用、文档链接和 git diff --check。

SampleSceneBuilder 是编辑器生成工具，运行时无依赖。源码项目中重新生成会覆盖样例；通过 UPM 使用时生成到 Assets/ZArchSamples。定制业务前请复制到自己的目录。

样例场景只保存 SampleInput 组件，由它按当前启用的输入后端创建 EventSystem 与输入模块，避免在 UPM 使用方缺少 Input System 时出现丢失脚本。

## 分层约束回归

在仓库根目录执行：

```sh
dotnet run --project Tests/Architecture/RoleCapabilities.csproj
```

测试使用 .NET SDK 自带的 Roslyn，不依赖 NuGet 测试包。它实际编译每种角色的允许与禁止调用，验证服务类型边界，并运行 Command → System → Model、Query 和事件流程。测试工具位于两个 UPM 包之外，不进入 Unity Player。
