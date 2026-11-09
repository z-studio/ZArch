# 业务分层与调用约束

入口负责创建对象和管理生命周期；业务角色的常用 API 有编译期约束。

| 角色 | 获取 Model | 获取 System | 获取服务 | 订阅事件 | 发布事件 | 发送 Command | 发送 Query |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Model | 否 | 否 | 是 | 否 | 是 | 否 | 否 |
| System | 是 | 是 | 是 | 是 | 是 | 否 | 否 |
| View / IController | 是 | 是 | 是 | 是 | 否 | 是 | 是 |
| Command | 是 | 是 | 是 | 否 | 是 | 是 | 是 |
| Query | 是 | 是 | 否 | 否 | 否 | 否 | 是 |

局部事件与 AppEvent 使用相同的订阅/发布权限。带返回值的 Command 与普通 Command 权限一致。

## 从职责决定代码位置

- Model 管理自己的数据，使用存储等基础服务，发布数据变化。
- System 执行业务规则，访问 Model，与其他 System 协作。
- View 接收输入、展示状态，通过 Command 发起业务状态变更。
- Command 表达一次操作；简单操作可直接改 Model，共享规则调用 System。
- Query 组织组合读取；简单展示可以直接读取 Model，不必包装 Query。

用户继承 AbstractModel、AbstractSystem、ArchitectureView、AbstractCommand、AbstractQuery 即可。能力接口由框架基类组合，新手不需要逐项实现。

## 示例：完成一回合

[RoundView](../Samples/Games/RoundView.cs) 发送 [PlayRoundCommand](../Samples/Games/PlayRoundCommand.cs)，再调用 [RoundSystem](../Samples/Games/RoundSystem.cs) 更新当前回合和共享金币。按钮事件仍在 Inspector 中显式连接 View 的命名方法。

```csharp
public sealed class PlayRoundCommand : AbstractCommand {
    protected override void OnExecute() {
        this.GetSystem<RoundSystem>().PlayRound();
    }
}
```

UI 显隐、动画和通过 Launcher 导航场景属于表现/宿主流程；不要将它们包装成 async void Command。当前 Command 是同步操作接口。

## 基础服务使用独立类型角色

```csharp
public interface IStorage : IService {
    void Save(string key, string value);
}

// 在具有获取服务能力的角色中使用。
var storage = this.GetService<IStorage>();
```

GetService 的类型参数必须实现 IService。Model/System 不应实现 IService，不能通过 GetService<SomeSystem>() 绕过角色限制。Scope 注册仍可接受任意类型，供入口和高级组装代码使用。

## 约束的边界

这些约束控制常用架构 API，不是安全沙箱。公开的 Scope、Architecture 和外部对象引用仍是显式高级入口，不应在普通业务代码中用来绕过分层。

GetModel/GetSystem 返回的是业务类型；编译器不能判断某个方法有无副作用。因此 View/Query 仍需遵守只读意图；需要真正保护写入时，通过只读接口和受控修改方法设计业务模型。能力接口不会自动让可写 Model 变成只读对象。

Command 本身不自动提供回放、持久化或队列。它首先让状态变更有明确入口和可追踪路径。
