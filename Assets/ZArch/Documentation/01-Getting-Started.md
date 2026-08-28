# 01 快速开始

本篇从空场景实现一个可点击加一并自动刷新文本的 Counter。你只需要先认识 Bootstrap、Model、Command 和 Controller。

## 1. 程序集引用

业务 asmdef 至少引用：

```text
ZArch.Core
ZArch.Patterns
ZArch.Unity
```

如果项目没有业务 asmdef，Unity 默认程序集会自动引用 ZArch。

## 2. 创建 Model

Model 保存状态。接口供其他模块依赖，实现类负责初始化。

```csharp
using ZArch;

public interface ICounterModel : IModel {
    BindableProperty<int> Count { get; }
}

public sealed class CounterModel : AbstractModel, ICounterModel {
    public BindableProperty<int> Count { get; } = new(0);

    protected override void OnInit() {
        // 可以在这里加载初始数据。
    }
}
```

`BindableProperty` 会在值真正变化时通知订阅者。

## 3. 创建 Command

Command 表达一次操作：

```csharp
using ZArch;

public sealed class IncreaseCountCommand : AbstractCommand {
    protected override void OnExecute() {
        this.GetModel<ICounterModel>().Count.Value++;
    }
}
```

简单项目可以直接调用 System。Command 适合需要日志、回放、排队或组合多个模块的操作。

## 4. 创建 Bootstrap

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

在启动场景创建 `GameBootstrap` GameObject，并挂载脚本。Bootstrap 在 `Awake` 中：

1. 创建 Architecture；
2. 调用 `Start()`；
3. 创建名为 `App` 的 Root Scope；
4. 注册并初始化 Model；
5. 在 GameObject 销毁时关闭 Architecture。

## 5. 创建 Controller

场景中准备 Button 和 Text，然后添加：

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
            .RegisterWithInitValue(UpdateText)
            .UnregisterWhenGameObjectDestroyed(gameObject);
    }

    private void OnClicked() {
        this.SendCommand(new IncreaseCountCommand());
    }

    private void UpdateText(int count) {
        m_Text.text = count.ToString();
    }
}
```

把 Bootstrap、Button 和 Text 拖入 Inspector。

## 6. 运行过程

```text
GameBootstrap.Awake
→ 创建 Architecture 和 Root Scope
→ CounterModel.OnInit

CounterController.Start
→ 绑定 Root Scope
→ 立即显示 Count 当前值

点击 Button
→ IncreaseCountCommand
→ CounterModel.Count 改变
→ Controller.UpdateText
```

## 7. 常见问题

### Controller 未绑定 Scope

异常包含 `has not been bound to a scope`。在使用 `GetModel` 或 `SendCommand` 前调用：

```csharp
BindScope(m_Bootstrap.RootScope);
```

### RootScope 还是 null

Bootstrap 在 `Awake` 创建 Scope，Controller 建议在 `Start` 绑定。不要让 Controller 的 `Awake` 依赖另一个对象的 `Awake` 顺序。

### Resolve 找不到接口

注册键必须和解析键一致：

```csharp
scope.Register<ICounterModel>(new CounterModel());
```

只注册 `CounterModel` 后，不能自动通过 `ICounterModel` 解析。

### UI 销毁后仍收到回调

为订阅添加 Unity 生命周期：

```csharp
subscription.UnregisterWhenGameObjectDestroyed(gameObject);
```

## 下一步

继续阅读 [Architecture、Scope 与服务](02-Core.md)，理解 Root Scope、Child Scope 和服务生命周期。
