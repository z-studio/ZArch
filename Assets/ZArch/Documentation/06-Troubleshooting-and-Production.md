# 06 故障排查与生产检查表

## 常见异常

### Call Architecture.Start first

原因：Host 没启动、已经 Shutdown，或者正在关闭。

检查：

- 是否先调用 `host.Start()`；
- Bootstrap 是否已执行 Awake；
- SDK 晚到回调是否发生在退出之后；
- 是否在 Deinitialize/Dispose 内创建新 Scope。

Architecture 在 Shutdown 后不能重新 Start；需要创建新的实例。

### Service ... is not registered

原因：注册键不一致或当前 Scope 无法访问目标服务。

```csharp
scope.Register<PlayerModel>(new PlayerModel());
scope.Resolve<IPlayerModel>(); // 不会自动成功
```

修复为接口注册或 Alias，并使用 Arch Debug 检查 Scope 树。

### Type ... is already registered

同一 Scope 的同一个服务键只能注册一次。需要替换实现时，在 Child Scope 覆盖父级注册。

### Scope is immutable

Scope Active 后不能继续 Register。把运行期新增服务放进新的 Child Scope。

### Circular dependency detected

两个 Factory 互相 Resolve：

```text
A Factory → Resolve B
B Factory → Resolve A
```

解决方法：提取第三个依赖、改用事件，或者把一个方向改成初始化后的显式绑定。

### requires asynchronous initialization

同步 Scope 注册了 `IAsyncInitializable`。改用 `CreateRootScopeAsync` 或 `CreateChildAsync`。

### Transient service has a managed lifecycle

Transient 每次 Resolve 都创建新对象，无法提供确定生命周期。改成 Scoped，或去掉 ZArch 生命周期接口并使用 `owned: false` 自行管理。

### Controller has not been bound

在组合入口调用：

```csharp
controller.BindScope(correctScope);
```

注意 Controller 应绑定实际所属模块 Scope，不一定是 Root。

### ObjectDisposedException

常见原因：

- 保存了已经退出模块的 Scope；
- SDK 晚到回调仍访问旧 Scope；
- 父 Scope Dispose 后仍使用 Child；
- MonoBehaviour 生命周期比 Scope 更长。

回调进入主线程后检查：

```csharp
if (!host.IsStarted || !scope.IsActivated) {
    return;
}
```

## 排查顺序

1. 打开 `Tools/ZArch/Arch Debug`；
2. 选择正确 Host；
3. 确认 Controller 绑定的 Scope；
4. 确认服务注册键和所在 Scope；
5. 查看 Scope 是否 Active；
6. 检查事件属于 Local、Parents 还是 Architecture；
7. 检查对象是否在场景卸载后仍收到回调；
8. 检查 SDK 回调线程。

## 开发规范

### 线程

- Unity 项目统一在主线程调用 ZArch；
- 未声明线程保证的 SDK 回调一律投递主线程；
- 不在 Task.Run 内直接 Resolve、Publish 或 Dispose Scope；
- 第三方回调建议排到下一帧，避免同步重入。

### 生命周期

- Scope 必须有明确拥有者；
- Root 由 Host/Bootstrap 销毁；
- Scene Scope 由 SceneScopeBinder 销毁；
- 临时 Scope 由创建它的流程 Dispose；
- `owned: false` 对象由外部负责清理。

### 事件

- 所有 Register 都必须对应 Unregister；
- Model/System 使用 `AddToUnregisterList(this)`；
- MonoBehaviour 使用 Unity 自动注销扩展；
- 约定事件传播范围，避免重复发送；
- Event 表示已经发生的事实，不用 Event 代替有返回值的 Query。
- 所有订阅者都会执行，异常会在发送结束后组成 `AggregateException` 抛出；事件边界必须记录或处理。

### 异步

- timeout 使用双参数 setup；
- token 继续传入全部底层异步调用；
- 只在 await 成功后保存 Scope；
- 取消或异常后不要继续使用配置中的 Scope；
- SDK 不支持取消时，晚到回调不得访问旧 Scope。

## Code Review 检查表

- [ ] UI 是否绕过 System 直接修改核心 Model；
- [ ] 服务是否注册在正确生命周期的 Scope；
- [ ] 接口注册键和 Resolve 类型是否一致；
- [ ] Factory 是否可能循环 Resolve；
- [ ] 初始化顺序是否显式表达依赖；
- [ ] 外部 SDK 是否错误使用 `owned: true`；
- [ ] 所有事件订阅是否有注销路径；
- [ ] SDK 回调是否统一进入主线程；
- [ ] 异步代码是否观察 CancellationToken；
- [ ] Scene/临时 Scope 是否一定会 Dispose；
- [ ] Command/Query 是否被错误长期保存或注册成服务。

## 商业上线检查表

### 自动化与生命周期

- [ ] 全部 EditMode 测试通过；
- [ ] Bootstrap 创建和销毁 PlayMode 测试通过；
- [ ] 单场景和 Additive Scene 加载/卸载测试通过；
- [ ] SceneScopeBinder Enable/Disable/Dispose 测试通过；
- [ ] 关闭 Domain Reload 后重复进入 Play Mode 正常；
- [ ] 初始化失败、timeout 和取消均不留下 Scope；
- [ ] 异步初始化期间 Shutdown 不会让已销毁 Scope 重新 Active；
- [ ] 异常处理器自身抛错仍能完成 Shutdown。

### 目标平台

- [ ] 每个目标平台完成 IL2CPP Development Build；
- [ ] 登录、切场景、退出账号和应用退出冒烟通过；
- [ ] 平台 SDK 回调线程已经确认或统一派发；
- [ ] AOT 环境下所有业务程序集和泛型路径正常。

### 压力与泄漏

- [ ] 重复创建/销毁临时 Scope 后 `Scopes` 回到基线；
- [ ] 重复 Additive Scene 加载/卸载无残留 Scope；
- [ ] 重复注册/注销事件后订阅者可被回收；
- [ ] Owned 服务恰好 Deinitialize/Dispose 一次；
- [ ] 长时间运行无持续托管内存增长；
- [ ] Transient 未出现在每帧高频热点。

### 发布契约

- [ ] 团队已阅读串行执行和协作取消规则；
- [ ] 公共服务接口有稳定的命名和职责；
- [ ] Scope 命名能够从调试窗口识别业务；
- [ ] ExceptionHandler 已连接项目日志/崩溃上报；
- [ ] 框架升级有版本记录和回滚方案。

## 推荐 Scope 命名

使用可读且可搜索的名称：

```text
App
Lobby
Battle:PVE:Chapter-01
Battle:PVP:Room-12345
Scene:Battle#42
Preview:Character:1001
Online:Login
```

Tag 放机器可读的标识或上下文对象，Name 用于日志和调试显示。

返回：[ZArch 完整学习指南](README.md)
