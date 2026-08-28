# ZArch

ZArch 是一个面向 Unity 的分层架构框架。它保留 Model、System、Utility、Command、Query、Event 和 BindableProperty 这一套清晰的业务组织方式，并使用 Architecture 与层级 Scope 管理服务、生命周期、场景和独立游戏模块。

框架没有静态全局架构实例。每个 `Architecture` 都是一个相互隔离的运行环境，可以拥有一棵或多棵 Scope 树。

## 适合解决什么问题

- 用 Model 保存游戏状态，用 System 承载可复用规则。
- 用 Command 表达一次状态变更，用 Query 表达一次读取。
- 用 Event 或 BindableProperty 从逻辑层通知表现层。
- 让场景、战斗、关卡、预览环境拥有明确的服务作用域。
- 自动初始化、反初始化和释放由 Scope 拥有的对象。
- 在一个 App 中安全切换多个独立游戏模块。
- 在 Unity 对象销毁、禁用或场景卸载时自动注销订阅。

## 核心模型

```text
Architecture
└── Root Scope
    ├── Model
    ├── System
    ├── Utility
    └── Child Scope
        ├── 覆盖父级服务
        ├── Command / Query
        ├── Scope Event
        └── 随 Scope 一起结束的资源
```

Model、System 等能力接口用于表达分层权限：例如 Model 可以访问 Utility，System 可以访问 Model，Controller 可以发送 Command。它们是编译期规则，不要求业务代码手动管理依赖容器。

## Unity 快速开始

### 1. 定义 Model

```csharp
using ZArch;

public interface ICounterModel : IModel {
    BindableProperty<int> Count { get; }
}

public sealed class CounterModel : AbstractModel, ICounterModel {
    public BindableProperty<int> Count { get; } = new(0);

    protected override void OnInit() { }
}
```

### 2. 定义 Command

```csharp
using ZArch;

public sealed class IncreaseCountCommand : AbstractCommand {
    protected override void OnExecute() {
        this.GetModel<ICounterModel>().Count.Value++;
    }
}
```

### 3. 创建 Bootstrap

```csharp
using ZArch;
using ZArch.Unity;

public sealed class GameBootstrap : ArchitectureBootstrap {
    protected override Architecture CreateArchitecture() => new Architecture();

    protected override void ConfigureRoot(ArchitectureScope scope) {
        scope.Register<ICounterModel>(new CounterModel());
    }
}
```

把 `GameBootstrap` 挂到启动场景的 GameObject。它会创建 Architecture、启动 Root Scope，并在销毁时关闭架构。

### 4. 绑定 Controller

```csharp
using UnityEngine;
using UnityEngine.UI;
using ZArch;
using ZArch.Unity;

public sealed class CounterController : ArchitectureController {
    [SerializeField] private GameBootstrap m_Bootstrap;
    [SerializeField] private Button m_Button;
    [SerializeField] private Text m_Text;

    private void Start() {
        BindScope(m_Bootstrap.RootScope);
        m_Button.onClick.AddListener(OnClicked);

        this.GetModel<ICounterModel>()
            .Count
            .RegisterWithInitValue(count => m_Text.text = count.ToString())
            .UnregisterWhenGameObjectDestroyed(gameObject);
    }

    private void OnClicked() {
        this.SendCommand(new IncreaseCountCommand());
    }
}
```

这个例子的运行路径只有：

```text
Bootstrap 注册 Model
→ Controller 绑定 Scope
→ Controller 发送 Command
→ Command 修改 Model
→ BindableProperty 刷新 UI
```

## 只使用 Core

Patterns 和 Unity 集成都不是强制依赖。纯 C# 代码可以只使用 `ZArch.Core`：

```csharp
using ZArch;

using var architecture = new Architecture();
architecture.Start();

var app = architecture.CreateRootScope("App", scope => {
    scope.Register<IClock>(new SystemClock());
    scope.RegisterFactory<IRepository>(resolver =>
        new Repository(resolver.Resolve<IClock>()));
});

var repository = app.Resolve<IRepository>();
```

## 程序集

```text
ZArch.Core ← ZArch.Patterns ← ZArch.Unity ← ZArch.Unity.Editor
     └─────← ZArch.GameModules ← ZArch.GameModules.Unity
```

- `ZArch.Core`：Architecture、Scope、服务、生命周期和事件，不依赖 Unity。
- `ZArch.Patterns`：Model/System、Command/Query、BindableProperty。
- `ZArch.Unity`：Bootstrap、Controller、Scene Scope 和 Unity 自动注销。
- `ZArch.Unity.Editor`：运行时架构调试窗口。
- `ZArch.GameModules`：可选的游戏会话、加载和切换协议。
- `ZArch.GameModules.Unity`：可选的 Catalog 与 Additive Scene 适配。

Core 和 Patterns 均启用 `noEngineReferences`。

## 教程与学习路线

第一次使用建议按基础路线阅读，遇到实际需求时再进入进阶或可选扩展。

### 基础路线

1. [快速开始](Documentation/01-Getting-Started.md)：从空场景完成 Counter。
2. [Architecture、Scope 与服务](Documentation/02-Core.md)：理解注册、解析、生命周期和事件。
3. [Patterns](Documentation/03-Patterns.md)：使用 Model/System、Command/Query 和 BindableProperty。
4. [Unity 集成](Documentation/04-Unity.md)：正确绑定 MonoBehaviour、场景和订阅生命周期。

### 进阶路线

5. [异步与生命周期](Documentation/05-Async-Lifecycle.md)：异步初始化、取消、超时、回滚和安全关闭。
6. [API 速查](Documentation/07-API-Reference.md)：按需求选择 API，并定位常见错误。

### 可选扩展

- [多游戏模块](Documentation/06-Game-Modules.md)：一个大厅切换多个独立游戏或玩法。
- [源码维护](Documentation/08-Maintenance.md)：修改 ZArch 本身时使用。

### 概念索引

| 概念 | 负责什么 | 谁创建或拥有 |
|---|---|---|
| `Architecture` | 隔离一个完整运行环境 | Bootstrap 或应用入口 |
| `ArchitectureScope` | 注册服务、限定生命周期、组织父子关系 | Architecture 或父 Scope |
| Model | 保存状态和数据操作 | Scope |
| System | 实现跨界面共享的业务规则 | Scope |
| Utility | 封装存储、网络、SDK 等基础设施 | Scope 或外部 |
| Command | 表达一次操作或状态变更 | 调用方，执行后丢弃 |
| Query | 表达一次读取 | 调用方，执行后丢弃 |
| Event | 通知已经发生的事实 | Architecture 或 Scope 的事件系统 |
| BindableProperty | 保存值并通知变化 | 通常属于 Model |
| `IUnregister` | 表示一条可取消订阅 | 订阅者 |

### 最小选择规则

- 多个对象共享的数据放入 Model。
- 多个 Controller 共用的规则放入 System。
- 外部 SDK、文件和网络封装为 Utility 或普通服务。
- 修改状态可直接调用 System；需要日志、回放或排队时使用 Command。
- 组合读取使用 Query，简单属性读取不必包装。
- 一次性事实使用 Event，持续状态展示使用 BindableProperty。
- 生命周期与场景一致时创建 Child Scope，并在场景或流程结束时 Dispose。

## 重要约束

- ZArch 采用串行执行模型。Unity 项目应在主线程访问 Architecture 和 Scope。
- `Architecture` 调用 `Shutdown()` 后不能重新启动；需要重新创建实例。
- Scope 激活后注册表被冻结，运行时变化通过创建或销毁子 Scope 表达。
- 创建 Scope 的一方必须明确负责其释放；父 Scope 会自动释放所有子 Scope。
- 同步 Scope 不能包含 `IAsyncInitializable` 服务。
- 注册事件后必须保存 `IUnregister`，或使用 Unity 自动注销扩展。
- ZArch 不提供内部锁，也不承诺并发线程安全；后台任务完成后应先回到所属同步上下文。

## 源码目录

```text
Assets/ZArch
├── Core/
│   ├── Architecture/
│   ├── Scopes/
│   ├── Services/
│   ├── Events/
│   └── Lifecycle/
├── Patterns/
│   ├── Binding/
│   ├── ModelSystem/
│   └── Operations/
├── Unity/
├── GameModules/
├── Tests/
└── Documentation/
```

目录用于源码导航，asmdef 用于依赖隔离。功能子目录不会额外创建程序集。
