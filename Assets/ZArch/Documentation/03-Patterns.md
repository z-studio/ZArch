# 03 Patterns

Patterns 是可选策略层，提供 Model、System、Utility、Controller、Command、Query 和 BindableProperty。它们通过所属 Scope 工作，不依赖静态 Architecture。

## 1. 分层能力

| 角色 | 主要职责 | 可用能力 |
|---|---|---|
| Model | 保存数据和数据操作 | Utility、发送 Architecture Event |
| System | 实现共享规则 | Model、System、Utility、Architecture Event |
| Controller | 连接输入和表现 | Model、System、Utility、Command、Query、Architecture Event |
| Command | 执行一次操作 | Model、System、Utility、Command、Query、Architecture Event |
| Query | 读取一次结果 | Model、System、Query |
| Utility | 基础设施 | 不获得架构能力 |

`ICanGetModel`、`ICanSendCommand` 等接口用于编译期约束这些权限。

## 2. Model

```csharp
public interface IPlayerModel : IModel {
    BindableProperty<int> Hp { get; }
    BindableProperty<int> Gold { get; }
}

public sealed class PlayerModel : AbstractModel, IPlayerModel {
    public BindableProperty<int> Hp { get; } = new(100);
    public BindableProperty<int> Gold { get; } = new(0);

    protected override void OnInit() {
        var storage = this.GetUtility<IStorage>();
        Gold.SetValueWithoutEvent(storage.LoadGold());
    }

    protected override void OnDeinit() { }
}
```

`AbstractModel` 会在反初始化时先清理 `UnregisterList`，再调用 `OnDeinit`。

## 3. System

```csharp
public interface ICombatSystem : ISystem {
    void Damage(int amount);
}

public sealed class CombatSystem : AbstractSystem, ICombatSystem {
    protected override void OnInit() { }

    public void Damage(int amount) {
        if (amount <= 0) return;

        var hp = this.GetModel<IPlayerModel>().Hp;
        hp.Value = System.Math.Max(0, hp.Value - amount);
    }
}
```

多个界面共享的业务规则适合放入 System。只被一个组件使用的简单表现逻辑不必强行抽成 System。

## 4. Utility

Utility 封装基础设施：

```csharp
public interface IStorage : IUtility {
    int LoadGold();
    void SaveGold(int value);
}
```

它可以由 Scope 拥有，也可以作为外部对象以 `owned: false` 注册。

## 5. Command

无返回值：

```csharp
public sealed class DamageCommand : AbstractCommand {
    private readonly int m_Amount;

    public DamageCommand(int amount) {
        m_Amount = amount;
    }

    protected override void OnExecute() {
        this.GetSystem<ICombatSystem>().Damage(m_Amount);
    }
}

scope.SendCommand(new DamageCommand(10));
```

带返回值：

```csharp
public sealed class BuyCommand : AbstractCommand<bool> {
    protected override bool OnExecute() {
        // 执行购买并返回结果。
        return true;
    }
}

bool success = scope.SendCommand(new BuyCommand());
```

Command 在执行前会获得调用 Scope，因此同一个 Command 类型可以在不同子 Scope 中解析不同服务。Command 应当作为一次性对象使用。

## 6. Query

```csharp
public sealed class CanBuyQuery : AbstractQuery<bool> {
    private readonly int m_Price;

    public CanBuyQuery(int price) {
        m_Price = price;
    }

    protected override bool OnExecute() {
        return this.GetModel<IPlayerModel>().Gold.Value >= m_Price;
    }
}

bool canBuy = scope.SendQuery(new CanBuyQuery(100));
```

简单属性读取可以直接读取 Model；Query 更适合组合多项数据、统一权限或需要独立测试的读取规则。

## 7. BindableProperty

监听后续变化：

```csharp
IUnregister subscription = model.Hp.Register(OnHpChanged);
```

监听并立即获得当前值：

```csharp
model.Hp.RegisterWithInitValue(OnHpChanged);
```

初始化数据但不广播：

```csharp
model.Hp.SetValueWithoutEvent(saveData.Hp);
```

自定义比较器：

```csharp
var progress = new BindableProperty<float>()
    .WithComparer((a, b) => System.Math.Abs(a - b) < 0.001f);
```

Unity Runtime 会为常用 Unity 值类型注册比较器，`float` 默认使用 `Mathf.Approximately`。

只向外暴露读取权限：

```csharp
private readonly BindableProperty<int> m_Hp = new(100);
public IReadOnlyBindableProperty<int> Hp => m_Hp;
```

## 8. Event 与 BindableProperty 的选择

- “玩家当前血量”：BindableProperty。
- “玩家刚刚死亡”：Event。
- 状态需要新订阅者立即显示：BindableProperty。
- 消息只代表一次已经发生的事实：Event。

## 9. 自动管理订阅

Model/System：

```csharp
this.RegisterArchitectureEvent<PlayerLoggedIn>(OnLoggedIn)
    .AddToUnregisterList(this);
```

Controller：

```csharp
model.Hp.RegisterWithInitValue(UpdateHp)
    .UnregisterWhenGameObjectDestroyed(gameObject);
```

下一篇：[Unity 集成](04-Unity.md)。
