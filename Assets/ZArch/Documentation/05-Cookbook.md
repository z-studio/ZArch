# 05 常用配方与 API 选择

遇到业务需求时，先从本页选择最小方案。

## 我应该把代码放在哪里

| 需求 | 推荐位置 |
|---|---|
| 金币、血量、背包数据 | Model |
| 伤害、购买、匹配规则 | System |
| 时间、随机数、序列化适配 | Utility |
| 点击攻击、领取奖励 | Command |
| 查询金币、生成 UI 显示数据 | Query |
| Button/Text/GameObject 交互 | Controller |
| 跨模块通知已经发生的事情 | Event |
| 同生命周期的一组服务 | Scope |

## 注册和解析

```csharp
scope.Register<IConfig>(new GameConfig());
scope.Register<IStorage, LocalStorage>();
scope.RegisterFactory<IRepository>(r => new Repository(r.Resolve<IStorage>()));

IConfig required = scope.Resolve<IConfig>();

if (scope.TryResolve<IAnalytics>(out var optional)) {
    optional.Track("start");
}
```

## 一个实现暴露两个接口

```csharp
scope.Register<PlayerModel, PlayerModel>();
scope.RegisterAlias<IPlayerReader, PlayerModel>();
scope.RegisterAlias<IPlayerWriter, PlayerModel>();
```

如果业务主要通过接口注册，也可以直接把同一实例注册为多个键，但必须保证只有一个注册负责 owned 生命周期。Alias 更不容易写错。

## 覆盖父级配置

```csharp
var preview = root.CreateChild("Preview", scope => {
    scope.Register<IConfig>(new PreviewConfig());
});
```

Preview 内解析到 PreviewConfig，Root 和其他 Child 仍使用正式配置。

## 临时模块

```csharp
ArchitectureScope shop = root.CreateChild("Shop", ConfigureShop);

try {
    OpenShopUi(shop);
} finally {
    shop.Dispose();
}
```

Unity 项目通常由界面/场景入口持有 Scope，并在 `OnDestroy` 或关闭流程 Dispose。

## 发送 Command

```csharp
scope.SendCommand(new AttackCommand(10));
bool success = scope.SendCommand(new TryBuyCommand(itemId));
```

无参 Command：

```csharp
this.SendCommand<RefreshInventoryCommand>();
```

## 执行 Query

```csharp
int gold = scope.SendQuery(new GetGoldQuery());
```

Query 不应修改 Model 或发送副作用操作。

## 注册事件

Architecture 级：

```csharp
var unregister = host.RegisterEvent<LoginEvent>(OnLogin);
host.SendEvent(new LoginEvent());
```

Scope 级：

```csharp
var unregister = battle.RegisterEvent<DamageEvent>(OnDamage);
battle.Publish(new DamageEvent());
```

System 自动注销：

```csharp
protected override void OnInit() {
    this.RegisterArchitectureEvent<LoginEvent>(OnLogin)
        .AddToUnregisterList(this);
}
```

MonoBehaviour 自动注销：

```csharp
unregister.UnregisterWhenGameObjectDestroyed(gameObject);
```

## 监听属性并立即刷新 UI

```csharp
model.Gold
    .RegisterWithInitValue(gold => goldText.text = gold.ToString())
    .UnregisterWhenGameObjectDestroyed(gameObject);
```

只修改不广播：

```csharp
model.Gold.SetValueWithoutEvent(saveData.Gold);
```

## 自定义值比较

```csharp
var position = new BindableProperty<Vector3>()
    .WithComparer((a, b) => Vector3.SqrMagnitude(a - b) < 0.0001f);
```

## 初始化有先后关系的服务

```csharp
scope.Register(config, initializationOrder: -100);
scope.Register(database, initializationOrder: 0);
scope.Register(gameplay, initializationOrder: 100);
```

## 外部拥有的 SDK

```csharp
scope.Register<IPaymentSdk>(PaymentSdk.Instance, owned: false);
```

如果 SDK 回调线程未知，使用主线程 Dispatcher，不要在回调中直接操作 Scope。

## 可取消的在线 Scope

```csharp
var online = await root.CreateChildAsync(
    "Online",
    async (scope, token) => {
        var config = await api.LoadConfigAsync(token);
        scope.Register(config);
        scope.Register<IOnlineSystem>(new OnlineSystem());
    },
    timeout: TimeSpan.FromSeconds(15),
    cancellationToken: cancellationToken
);
```

## Scene 自动 Scope

```csharp
var binder = new SceneScopeBinder(host);
binder.Bind("Battle", ConfigureBattle, _ => root);
binder.Enable();

// Host Shutdown 前
binder.Dispose();
```

## 多个独立运行环境

```csharp
var production = new ArchitectureHost();
var preview = new ArchitectureHost();

production.Start();
preview.Start();
```

Host 之间不能共享 Scope 或事件。共享无状态 Utility 时，注册为 `owned: false` 并由外部统一管理。

## 选择决策

```text
对象是否需要和一组业务一起销毁？
├── 是 → 注册到对应 Scope
└── 否 → 是否由外部/Unity/SDK 管理？
    ├── 是 → owned: false
    └── 否 → 放到更长生命周期的父 Scope

消息只属于当前战斗吗？
├── 是 → Scope Event
└── 否 → Architecture Event

操作是否有副作用？
├── 是 → Command/System
└── 否 → Query
```

上一篇：[04 Unity 与第三方 SDK 实战](04-Unity-and-SDK.md)｜下一篇：[06 故障排查与生产检查表](06-Troubleshooting-and-Production.md)
