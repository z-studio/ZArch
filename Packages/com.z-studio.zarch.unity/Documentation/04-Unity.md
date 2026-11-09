# Unity 入口与界面

## 所有入口使用相同约定

SceneArchitecture、GameLauncher、GameEntry 都继承 ArchitectureEntry：

- `Initialization`：等待入口初始化完成；失败或取消会传递给等待方。
- `IsReady`：所属入口初始化完成、界面已激活。
- `Scope`：所属服务环境。
- `OnReady`：服务和入口初始化完成后调用，可开始业务访问。
- `ShutdownAsync`：统一的可等待关闭；失败后允许重试。

入口只关闭自己拥有的资源：SceneArchitecture/GameLauncher 拥有应用环境，GameEntry 拥有进入/退出逻辑和界面；托管游戏的场景与服务由 Launcher.CloseAsync 释放。正常流程中先等待对应关闭操作，再销毁对象。Unity 不会等待 OnDestroy，强制退出进程也无法保证异步清理完成。

## 单场景入口

继承 SceneArchitecture，在 ConfigureServices 显式注册服务。需要异步准备时重写 OnInitializeAsync；它完成之前不会激活 Views。

SceneArchitecture 默认随场景存活，GameLauncher 则常驻。游戏场景使用 GameEntry，不能再创建另一个 SceneArchitecture。

## View 的业务权限

ArchitectureView 实现 IController，可以获取 Model/System/IService、订阅事件、发送 Command/Query，不能通过业务扩展直接发布事件。业务状态变更从 View 发送 Command；GetSystem 用于读取系统状态，不能据此绕过操作入口。

## View 生命周期

ArchitectureView.OnReady 表示所属入口的初始化已完成。订阅使用 Track；OnUnbind 清理自己添加的回调和表现状态。覆写 OnDestroy 时必须调用基类。

隐藏面板保留绑定，重新显示时数据已更新。解除绑定会清理订阅，并允许对象池复用时再次调用 OnReady。

## 动态弹窗与对象池

```csharp
await entry.Initialization;
entry.BindView(popup);
// 回收之前
entry.UnbindView(popup);
popup.gameObject.SetActive(false);
// 下次取出
popup.gameObject.SetActive(true);
entry.BindView(popup);
```

同一入口重复 Bind 同一个 View 是幂等的；两个入口不能同时拥有同一个 View，即使它们使用同一个 Scope。运行时没有全局搜索或自动选择入口。

## 配置检查

Play 前检查当前入口的空 Views、重复 Views、多个入口争用 View、重复游戏 ID、游戏场景入口、Build Settings 路径。游戏场景用只读预览方式检查。

菜单 `ZArch/Validate Loaded Scenes` 可主动检查。入口和游戏定义的组件上下文菜单也提供基础检查。配置错误会说明对象与字段；修复后再 Play。
