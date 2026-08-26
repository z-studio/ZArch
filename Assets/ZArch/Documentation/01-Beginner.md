# 01 基础篇：从零完成第一个功能

本篇实现一个最小战斗功能：点击攻击按钮，敌人扣血，UI 自动刷新。

## 1. 用生活化语言理解组件

- `Architecture` 像一个独立游戏世界的总管。
- `Scope` 像一个房间，房间关闭时，里面由它管理的对象一起清理。
- `Model` 是数据本，例如金币、血量和背包。
- `System` 是规则，例如伤害计算和购买判断。
- `Command` 是一次明确操作，例如“攻击 10 点伤害”。
- `Event` 是已经发生的消息，例如“敌人血量变化了”。
- `Controller` 连接 Unity 界面和游戏逻辑。

## 2. 创建 Model

接口描述别人能读取或修改什么，实现类保存数据。

```csharp
using ZArch;

public interface IBattleModel : IModel {
    BindableProperty<int> EnemyHp { get; }
}

public sealed class BattleModel : AbstractModel, IBattleModel {
    public BindableProperty<int> EnemyHp { get; } = new(100);

    protected override void OnInit() {
        // Scope 激活时调用。初始值也可以从存档读取。
    }

    protected override void OnDeinit() {
        // Scope 销毁时调用。大多数 Model 不需要额外代码。
    }
}
```

`BindableProperty` 只在值真正变化时通知订阅者。

## 3. 创建 System

System 实现业务规则，不依赖按钮、Text 或 GameObject。

```csharp
using ZArch;

public interface IBattleSystem : ISystem {
    void Attack(int damage);
}

public sealed class BattleSystem : AbstractSystem, IBattleSystem {
    protected override void OnInit() {
    }

    public void Attack(int damage) {
        if (damage <= 0) {
            return;
        }

        var model = this.GetModel<IBattleModel>();
        model.EnemyHp.Value = System.Math.Max(0, model.EnemyHp.Value - damage);
    }
}
```

`GetModel<T>()` 会从当前 Scope 开始查找，没有时继续向父 Scope 查找。

## 4. 创建 Command

Command 把“一次操作”包装成对象。

```csharp
using ZArch;

public sealed class AttackCommand : AbstractCommand {
    private readonly int m_Damage;

    public AttackCommand(int damage) {
        m_Damage = damage;
    }

    protected override void OnExecute() {
        this.GetSystem<IBattleSystem>().Attack(m_Damage);
    }
}
```

简单项目可以直接调用 System；当操作需要参数、日志、回放或组合多个 System 时，Command 更清晰。

## 5. 创建 Bootstrap

Bootstrap 是 Unity 项目的组合入口，负责创建 Host 和注册根服务。

```csharp
using ZArch;

public sealed class GameBootstrap : ArchitectureHostBootstrap {
    protected override Architecture CreateArchitecture() {
        return new ArchitectureHost();
    }

    protected override void ConfigureRoot(ArchitectureScope scope) {
        scope.Register<IBattleModel>(new BattleModel());
        scope.Register<IBattleSystem>(new BattleSystem());
    }
}
```

把 `GameBootstrap` 挂到场景中的 GameObject。默认情况下它会 `DontDestroyOnLoad`。

## 6. 创建 Controller

```csharp
using UnityEngine;
using UnityEngine.UI;
using ZArch;

public sealed class BattleController : ArchitectureController {
    [SerializeField] private GameBootstrap m_Bootstrap;
    [SerializeField] private Button m_AttackButton;
    [SerializeField] private Text m_HpText;

    private void Start() {
        // 所有 Awake 都会先于 Start。此时 Bootstrap 已经创建好 RootScope。
        BindScope(m_Bootstrap.RootScope);
        m_AttackButton.onClick.AddListener(OnAttackClicked);

        this.GetModel<IBattleModel>()
            .EnemyHp
            .RegisterWithInitValue(OnEnemyHpChanged)
            .UnregisterWhenGameObjectDestroyed(gameObject);
    }

    private void OnAttackClicked() {
        this.SendCommand(new AttackCommand(10));
    }

    private void OnEnemyHpChanged(int hp) {
        m_HpText.text = $"Enemy HP: {hp}";
    }
}
```

如果 Controller 属于临时 Battle Scope，应由创建 Battle Scope 的入口调用 `BindScope(battleScope)`，不要绑定 Root。

## 7. 运行时发生了什么

```text
GameBootstrap.Awake
  → 创建 ArchitectureHost
  → Start Host
  → 创建 Root Scope
  → 注册 BattleModel、BattleSystem
  → 初始化 Model、System

点击按钮
  → AttackCommand
  → BattleSystem.Attack
  → BattleModel.EnemyHp 变化
  → BattleController 刷新 UI
```

## 8. 新手常见错误

### Controller 没有绑定 Scope

症状：`has not been bound to a scope`。

解决：在组合入口调用 `controller.BindScope(scope)`，不要让 Controller 自己寻找全局对象。

### 忘记注册接口

注册键是精确类型：

```csharp
scope.Register<IBattleModel>(new BattleModel()); // Resolve<IBattleModel>()
```

只注册 `BattleModel` 后，不能自动通过 `IBattleModel` 解析。

### UI 直接修改 Model

推荐让 UI 发送 Command，由 System 判断操作是否合法。这样规则不会散落在多个界面脚本中。

### 忘记注销

Unity 对象上的订阅优先使用：

```csharp
unregister.UnregisterWhenGameObjectDestroyed(gameObject);
```

## 9. 基础篇完成标准

在继续之前，尝试独立完成：

- 增加一个 HealCommand；
- 血量到 0 时发送 EnemyDeadEvent；
- 增加金币 Model 和购买 System；
- 让 UI 只通过 Command 修改数据。

下一篇：[02 中级篇：Scope、生命周期、事件与服务容器](02-Intermediate.md)
