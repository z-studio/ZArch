# 08 源码结构与维护指南

本篇面向需要修改 ZArch 源码的维护者。业务项目只使用框架时，不需要理解这里的文件组织细节。

## 1. 先区分目录与程序集

ZArch 使用 asmdef 表达依赖边界，使用功能目录表达源码职责：

```text
ZArch.Core ← ZArch.Patterns ← ZArch.Unity ← ZArch.Unity.Editor
     └─────← ZArch.GameModules ← ZArch.GameModules.Unity
```

`Architecture/`、`Events/`、`Binding/` 等子目录不会创建额外 asmdef。不要为了文件分类增加程序集；只有需要独立引用、独立发布或隔离 UnityEngine 依赖时，才考虑新的 asmdef。

## 2. Core

```text
Core
├── Architecture/      Host 定义、Scope 创建、Host 事件、启动与关闭
├── Scopes/            Scope 契约、Scope 树、服务注册与解析、局部事件
├── Services/          服务生命周期枚举、解析契约和内部注册描述
├── Events/            EasyEvent、类型事件和事件组合
└── Lifecycle/         初始化/反初始化契约与订阅注销工具
```

`Architecture` 使用同名 partial 文件按行为分组：

- `Architecture.cs` 只保存类型定义、状态和扩展点；
- `Architecture.Scopes.cs` 处理 Scope 创建、激活和挂接；
- `Architecture.Events.cs` 处理 Host 事件；
- `Architecture.Lifecycle.cs` 处理启动、异常报告和关闭。

这些文件共同组成同一个类型。拆分的目的只是导航和阅读，不应在 partial 文件之间复制状态。

`ArchitectureScope` 采用相同规则：主文件保存状态与构造，`.Registration`、`.Resolution`、`.Events`、`.Hierarchy`、`.Lifecycle` 和 `.Debug` 分别保存对应行为。涉及初始化或销毁顺序的修改应集中检查 `.Lifecycle`，不要跨文件复制清理逻辑。

## 3. Patterns

```text
Patterns
├── Binding/       BindableProperty 和只读/可写契约
├── ModelSystem/   Model、System、Controller 和能力接口
└── Operations/    Command、Query 和执行扩展
```

`ICanGetModel`、`ICanSendCommand` 等接口是分层权限，不是可删除的空接口。新增能力时需要回答：

1. 哪些角色应该拥有该能力？
2. 哪些角色必须在编译期被禁止使用？
3. 能否通过已有的 `IBelongToScope` 和扩展方法表达？

`BindablePropertyContracts.cs` 保存只读与可写契约，`BindableProperty.cs` 保存实现。Unity 类型比较器属于 Unity 适配，因此放在 `Unity/Runtime/Binding/`，不放回 Patterns。

## 4. Unity

```text
Unity/Runtime
├── Hosting/       Bootstrap 与 Controller
├── Scopes/        Scene 和 GameObject 的 Scope 绑定
├── Binding/       Unity 类型比较器注册
├── Lifecycle/     GameObject/Component 自动注销
└── Debugging/     运行时调试数据
```

Core 和 Patterns 必须继续保持 `noEngineReferences`。任何使用 `MonoBehaviour`、`ScriptableObject`、场景 API、`Vector3` 或 `Mathf` 的实现都应留在 Unity 程序集。

## 5. GameModules

GameModules 是可选扩展，不是基础使用路径：

```text
GameModules/Runtime
├── Contracts/     模块、加载器和启动上下文契约
├── Launching/     GameLauncher 与 Scope 工厂
└── Sessions/      单次运行会话

GameModules/Unity
├── Catalog/       ScriptableObject 模块描述和目录
├── Loading/       Unity 内容加载实现
└── Scenes/        Scene Provider 与场景入口
```

第三方资源系统适配器应放在项目自己的可选程序集，不要让 `ZArch.GameModules.Unity` 直接依赖 Addressables、YooAsset 或其他 SDK。

## 6. 文件拆分原则

- 一个文件应能用一句话描述职责。
- 同一组很短、总是一起变化的契约可以保存在 `*Contracts.cs`。
- 同一概念的泛型重载保存在同一个文件，例如 `EasyEvent<T>` 的多个参数版本。
- 大型状态对象优先使用 partial 做零行为拆分；确认边界稳定后，再考虑提取内部协作者。
- 不为了消除少量重复而引入新的公开基类。
- 文件移动时必须同时移动 `.meta`，保持 GUID 不变。
- 测试目录镜像源码功能目录，测试名描述行为而不是内部方法。

## 7. 修改检查表

- [ ] namespace 和 asmdef 引用方向没有意外变化。
- [ ] Core、Patterns 没有新增 UnityEngine 引用。
- [ ] public/protected API 的变化已经明确记录；纯整理不应产生 API 变化。
- [ ] 初始化、回滚、反初始化和 Dispose 的顺序保持不变。
- [ ] 新增源码和目录包含 `.meta`。
- [ ] Editor 测试全部通过。
- [ ] README 的目录树与实际结构一致。
