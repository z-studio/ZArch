# 02 中级篇：Scope、生命周期、事件与服务容器

基础篇解决“功能怎么跑起来”，本篇解决“项目变大以后怎么保持清楚”。

## 1. Scope 表示存活范围

假设游戏包含大厅和战斗：

```text
App Root
├── Lobby
└── Battle
    ├── Battle UI
    └── Level-01
```

Root 放全局配置、账号和存档。Battle 放本场战斗的数据和规则。退出战斗时只 Dispose Battle，不影响 Root。

```csharp
var battleScope = root.CreateChild("Battle", scope => {
    scope.Register<IBattleModel>(new BattleModel());
    scope.Register<IBattleSystem>(new BattleSystem());
}, tag: "PVE");

// 退出战斗
battleScope.Dispose();
```

父 Scope Dispose 时，会先按反向创建顺序 Dispose 所有子 Scope。

## 2. 服务解析规则

解析从当前 Scope 开始，逐级向 Parent 查找：

```csharp
var root = host.CreateRootScope("App", scope => {
    scope.Register<IConfig>(new GameConfig("Production"));
});

var battle = root.CreateChild("Battle", scope => {
    scope.Register<IBattleModel>(new BattleModel());
});

var config = battle.Resolve<IConfig>(); // 来自 Root
```

Child 可以覆盖 Parent：

```csharp
var preview = root.CreateChild("Preview", scope => {
    scope.Register<IConfig>(new GameConfig("Preview"));
});
```

必须存在的依赖使用 `Resolve<T>()`，可选依赖使用：

```csharp
if (scope.TryResolve<IAnalytics>(out var analytics)) {
    analytics.Track("battle_start");
}
```

## 3. 四种注册方式

### 注册已有实例

```csharp
scope.Register<IConfig>(new GameConfig());
```

### 注册无参实现

```csharp
scope.Register<IStorage, LocalStorage>();
```

### 使用 Factory 构造依赖

```csharp
scope.RegisterFactory<IRepository>(resolver =>
    new Repository(resolver.Resolve<IStorage>())
);
```

Factory 内形成循环依赖时，ZArch 会拒绝激活整个 Scope 并回滚。

### 使用 Alias 暴露多个接口

```csharp
scope.Register<PlayerModel, PlayerModel>();
scope.RegisterAlias<IPlayerReader, PlayerModel>();
```

Alias 指向同一个实例，不拥有它，也不会重复初始化或改写它所属的 Scope。

## 4. Scoped、Transient 和 owned

`EServiceLifetime.Scoped` 在一个 Scope 中只创建一次，也是默认值：

```csharp
scope.RegisterFactory<IInventory>(_ => new Inventory());
```

Transient 每次 Resolve 都创建新对象：

```csharp
scope.RegisterFactory<IRequestBuilder>(
    _ => new RequestBuilder(),
    EServiceLifetime.Transient,
    owned: false
);
```

`owned: true` 表示 Scope 负责对象生命周期。外部单例、SDK 对象或其他容器拥有的对象应使用 `owned: false`。

Owned Transient 不允许实现 ZArch 生命周期接口，因为“每次解析一个对象”无法形成确定的初始化顺序。需要生命周期时改用 Scoped。

## 5. 初始化和销毁顺序

Scope 激活时：

1. 物化所有 Scoped 服务；
2. 按 `initializationOrder` 从小到大初始化；
3. 相同 order 按注册顺序初始化；
4. 全部成功后 Scope 才进入 Active。

```csharp
scope.Register(config, initializationOrder: -100);
scope.Register(repository, initializationOrder: 0);
scope.Register(gameplay, initializationOrder: 100);
```

销毁顺序与实际初始化顺序相反。实现：

```csharp
public sealed class SaveService : IInitializable, IDeinitializable, IDisposable {
    public void Initialize() {
    }

    public void Deinitialize() {
        // 停止业务、解除订阅
    }

    public void Dispose() {
        // 释放底层资源
    }
}
```

初始化失败时，已经完成初始化的服务会反向 Deinitialize，Owned 的 `IDisposable` 仍会被释放。

## 6. Model、System 和 Utility 如何分工

| 类型 | 适合放什么 | 不适合放什么 |
|---|---|---|
| Model | 状态、存档映射、BindableProperty | Unity UI、复杂跨模块规则 |
| System | 规则、流程协调、事件响应 | Text、Button、场景查找 |
| Utility | 时间、随机、网络适配、序列化 | 会变化的游戏状态 |

Utility 只需实现标记接口：

```csharp
public interface IClock : IUtility {
    long UnixSeconds { get; }
}

public sealed class SystemClock : IClock {
    public long UnixSeconds => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
```

注册后，Model、System、Command 和 Controller 可以通过 `GetUtility<IClock>()` 使用。

## 7. Command 和 Query

无返回值 Command：

```csharp
public sealed class SpendGoldCommand : AbstractCommand {
    private readonly int m_Amount;

    public SpendGoldCommand(int amount) => m_Amount = amount;

    protected override void OnExecute() {
        this.GetSystem<IShopSystem>().SpendGold(m_Amount);
    }
}
```

有返回值 Command：

```csharp
public sealed class TryBuyCommand : AbstractCommand<bool> {
    private readonly int m_ItemId;

    public TryBuyCommand(int itemId) => m_ItemId = itemId;

    protected override bool OnExecute() {
        return this.GetSystem<IShopSystem>().TryBuy(m_ItemId);
    }
}

bool bought = scope.SendCommand(new TryBuyCommand(1001));
```

Query 只读取结果：

```csharp
public sealed class GetGoldQuery : AbstractQuery<int> {
    protected override int OnExecute() {
        return this.GetModel<IPlayerModel>().Gold.Value;
    }
}

int gold = scope.SendQuery(new GetGoldQuery());
```

Command 和 Query 是一次性操作对象，不要把它们注册成服务或长期保存。

## 8. 两级事件系统

### Architecture 事件

适合跨模块通知，同一个 Host 内可见：

```csharp
IUnregister unregister = host.RegisterEvent<PlayerLoggedIn>(OnPlayerLoggedIn);
host.SendEvent(new PlayerLoggedIn(playerId));
unregister.Unregister();
```

`AbstractSystem.RegisterEvent` 扩展默认注册到 Architecture，并且应加入 System 的注销列表：

```csharp
protected override void OnInit() {
    this.RegisterEvent<PlayerLoggedIn>(OnLoggedIn)
        .AddToUnregisterList(this);
}
```

`AbstractSystem` 和 `AbstractModel` 在 Deinitialize 时会自动执行 `UnregisterAll()`。

### Scope 本地事件

适合同一战斗或临时流程内部：

```csharp
scope.RegisterEvent<DamageEvent>(OnDamage);
scope.Publish(damage); // 只在当前 Scope
scope.Publish(damage, EEventPropagation.Parents); // 当前 Scope 到 Root
```

`Publish` 只负责 Scope 内传播。发送到当前 Host 使用：

```csharp
scope.Architecture.SendEvent(damage);
```

Patterns 中的 `this.RegisterEvent` 和 `this.SendEvent` 都操作 Architecture 事件，因此注册与发送是对称的。团队应约定某类消息属于 Scope 还是 Host，不要在两级总线重复注册同一个处理器。

一次发送会尝试调用全部订阅者。处理器异常不会阻断后续订阅者或 Parent Scope；发送结束后统一抛出 `AggregateException`，调用方应在事件边界记录或处理它。

## 9. BindableProperty

```csharp
public BindableProperty<int> Gold { get; } = new(0);

Gold.RegisterWithInitValue(value => RefreshGold(value));
Gold.Value += 10;               // 值变化才通知
Gold.SetValueWithoutEvent(100); // 修改但不通知
```

浮点数等 Unity 类型在运行时已注册合适的比较器，也可以单独指定：

```csharp
var progress = new BindableProperty<float>()
    .WithComparer((a, b) => Mathf.Abs(a - b) < 0.001f);
```

## 10. EasyEvent、OrEvent 与手动注销

不需要按消息类型分发时，可以直接使用轻量事件：

```csharp
var onReady = new EasyEvent();
var onProgress = new EasyEvent<float>();

IUnregister readySubscription = onReady.Register(OnReady);
IUnregister progressSubscription = onProgress.Register(OnProgress);

onReady.Trigger();
onProgress.Trigger(0.5f);
```

`OrEvent` 可以把多个无参数事件合成一个触发源：

```csharp
using var changed = firstEvent.Or(secondEvent);
changed.Register(Refresh);
```

自定义 API 可以返回 `CustomUnregister`：

```csharp
public IUnregister Listen(Action callback) {
    m_Callback += callback;
    return new CustomUnregister(() => m_Callback -= callback);
}
```

需要集中管理多个注销器时实现 `IUnregisterList`，然后使用：

```csharp
subscription.AddToUnregisterList(owner);
owner.UnregisterAll();
```

`CustomUnregister.Unregister()` 可以重复调用，实际注销动作最多执行一次。

`EasyEvents` 和 `TypeEventSystem` 是更底层的事件容器。一般业务优先使用 Architecture/Scope Event，只有在构建独立组件时才直接创建它们。

## 11. Scope 状态

```text
Created → Configuring → Initializing → Active → Disposing → Disposed
                                ↓
                              Faulted
```

- 只有配置阶段可以 Register。
- Initializing 阶段允许服务解析同 Scope 中已经物化的依赖。
- Active 后注册表冻结；需要变化时创建 Child Scope。
- Faulted Scope 会立即回滚，不应继续持有引用。

## 12. 中级篇完成标准

尝试完成一个商店模块：

- Root 注册账号和配置；
- Shop Child Scope 注册商店 Model/System；
- Query 返回金币；
- Command 执行购买；
- Event 通知购买成功；
- Dispose Shop 后确认所有订阅解除。

上一篇：[01 基础篇](01-Beginner.md)｜下一篇：[03 高级篇](03-Advanced.md)
