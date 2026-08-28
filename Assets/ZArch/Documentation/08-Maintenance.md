# 源码维护与扩展

ZArch 按“程序集负责依赖边界，目录负责阅读边界”组织。新增功能前先判断它属于哪一层，再决定文件位置。

## 1. 目录结构

```text
Assets/ZArch
├── Core/
│   ├── Architecture/       Architecture、Scope 创建与架构级事件
│   ├── Scopes/             Scope 生命周期、注册、解析和事件
│   ├── Services/           服务注册协议
│   ├── Events/             通用事件实现
│   └── Lifecycle/          初始化与注销协议
├── Patterns/
│   ├── Binding/            BindableProperty
│   ├── ModelSystem/        Model/System 与能力接口
│   └── Operations/         Command/Query
├── Unity/
│   ├── Runtime/            Unity 运行时适配
│   └── Editor/             编辑器工具
├── GameModules/
│   ├── Runtime/            纯 C# 模块协议与 Launcher
│   └── Unity/              Catalog 与 Scene Provider
├── Tests/Editor/           EditMode 测试
└── Documentation/          使用教程
```

不要为了每个小功能继续增加顶层目录。优先放入已有职责目录；只有形成独立依赖边界时才新增程序集。

## 2. 程序集依赖

```text
ZArch.Core
├── ZArch.Patterns ── ZArch.Unity ── ZArch.Unity.Editor
└── ZArch.GameModules ── ZArch.GameModules.Unity
                           └─ 还依赖 ZArch.Unity
```

约束：

- `Core` 不引用 Patterns 或 Unity；
- `Patterns` 只引用 Core，并保持 `noEngineReferences`；
- `GameModules` Runtime 只引用 Core，并保持 `noEngineReferences`；
- Unity API 只能出现在 Unity 程序集中；
- Editor API 只能出现在 Editor 程序集中；
- Core 不为上层功能添加特殊分支。

这样 Core、Patterns 和 GameModules Runtime 可以进行普通 .NET 思维下的单元测试，不要求场景或 `MonoBehaviour`。

## 3. 一个类型拆成多个文件的规则

复杂核心类型使用 `partial` 按职责拆分，而不是把全部实现堆在一个文件中：

```text
Architecture.cs
Architecture.Lifecycle.cs
Architecture.Scopes.cs
Architecture.Events.cs

ArchitectureScope.cs
ArchitectureScope.Registration.cs
ArchitectureScope.Resolution.cs
ArchitectureScope.Lifecycle.cs
ArchitectureScope.Hierarchy.cs
ArchitectureScope.Events.cs
ArchitectureScope.Debug.cs
```

拆分原则：

- 主文件保存字段、构造和最核心状态；
- 每个 partial 文件只表达一个职责；
- 文件名直接对应开发者要寻找的行为；
- 不按“public/private”拆分，也不建立只有一两个方法的碎片文件；
- 同一职责的校验与私有辅助方法留在同一文件附近。

## 4. 新功能放在哪里

| 新需求 | 推荐位置 |
| --- | --- |
| 与 Unity 无关的 Scope/服务能力 | `Core/` |
| Model/System/Command/Query 策略 | `Patterns/` |
| `MonoBehaviour`、Scene、Unity 值类型 | `Unity/Runtime/` |
| Inspector、菜单、调试窗口 | `Unity/Editor/` |
| 通用游戏模块切换协议 | `GameModules/Runtime/` |
| Unity 场景模块适配 | `GameModules/Unity/` |

某项功能只服务一个业务项目时，优先写在业务程序集，不要直接并入框架。

## 5. 修改公开 API

修改前检查：

1. 能否通过扩展方法完成，而不扩大核心类型？
2. 是否会改变注册键、所有权或生命周期语义？
3. 同步与异步路径是否仍然一致？
4. 失败后能否完整回滚？
5. 父子 Scope 销毁顺序是否保持？
6. 是否需要更新 README、教程和 API 速查？

重命名或移动公开类型时必须更新所有 IDE 可解析引用。纯文件移动还要保留对应 `.meta`，避免 Unity GUID 变化。

## 6. 测试清单

至少覆盖与改动相关的行为：

- Architecture 启动、关闭以及禁止重启；
- 根/子 Scope 创建、父级解析和覆盖；
- Scoped Factory、无所有权 Transient、Owned Transient、Alias 与循环 Factory；
- 初始化顺序、同步/异步逆序释放、清理异常聚合与失败回滚；
- 异步成功、取消、超时以及初始化/关闭竞态；
- Architecture Event、Scope Event 和订阅异常；
- BindableProperty 比较、立即回调和注销；
- Scene Scope 的创建与卸载；
- GameModuleLauncher 进入、退出、重复切换和加载失败回滚。

提交前在 Unity Test Runner 运行 `ZArch.Tests.Editor`，并确认 Console 没有意外异常。涉及 Player Runtime 行为时，再补一次目标平台构建验证。

## 7. 文档维护

- README 只保留定位、最小示例、程序集和文档入口；
- 教程解释选择与完整流程，不复制整份源码；
- 示例优先使用公开 API，不依赖 internal 实现；
- 新增文档时同时添加 Unity `.meta`；
- 修改 API 后全文搜索旧名称，并逐个检查 Markdown 相对链接；
- 明确标出可选层，避免让读者误以为必须使用所有功能。

## 8. 公开 API 边界

业务代码可以直接依赖 `Architecture`、`ArchitectureScope`、生命周期接口、Patterns 和 Unity 扩展。事件注册表、事件调度器、调试快照生成细节以及 Unity 注销触发组件保持 `internal`。

跨程序集确实需要访问的内部调试数据通过定向 `InternalsVisibleTo` 开放，不要为了省事把整个实现类型改成 `public`。

返回：[项目 README](../README.md)
