# 快速开始：按钮计数

打开 [Counter.unity](../Samples/Counter/Counter.unity)，Play 后点击 Increase。查看入口的 Views 和按钮 OnClick，可以直接看到引用与调用关系。

## 四个脚本

| 文件 | 职责 |
| --- | --- |
| [CounterModel](../Samples/Counter/CounterModel.cs) | 保存 Count |
| [IncreaseCountCommand](../Samples/Counter/IncreaseCountCommand.cs) | 执行一次加一操作 |
| [CounterEntry](../Samples/Counter/CounterEntry.cs) | 注册 Model，拥有环境生命周期 |
| [CounterView](../Samples/Counter/CounterView.cs) | 显示 Count，发送 Command |

```text
按钮 → View.Increase → IncreaseCountCommand → CounterModel.Count → View.ShowCount
```

## 注册状态

```csharp
protected override void ConfigureServices(ArchitectureScope services) {
    services.Register(new CounterModel());
}
```

Command 每次操作创建，不注册为长寿命服务。Model 没有初始化逻辑时无需重写 OnInit。

## 定义操作

```csharp
public sealed class IncreaseCountCommand : AbstractCommand {
    protected override void OnExecute() {
        this.GetModel<CounterModel>().Count.Value++;
    }
}
```

## 接收输入并刷新界面

```csharp
[SerializeField] private UnityEngine.UI.Text m_Count;

protected override void OnReady() {
    Track(GetModel<CounterModel>().Count.SubscribeAndInvoke(ShowCount));
}
private void ShowCount(int count) => m_Count.text = count.ToString();
public void Increase() {
    if (IsReady) this.SendCommand(new IncreaseCountCommand());
}
```

把 View 拖入入口的 Views，拖好文本引用，Button.onClick 连接 Increase。OnReady 表示所属入口初始化已完成；不要在 View 的 Awake/OnEnable 中获取尚未绑定的业务对象。

这个功能不需要 System。涉及多个界面共用的规则时，参考 Inventory：View → AddAppleCommand → InventorySystem → InventoryModel。

下一篇：[业务代码怎么写](03-Application-Logic.md)。
