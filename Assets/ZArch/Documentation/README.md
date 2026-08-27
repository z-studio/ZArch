# ZArch 完整学习指南

这套教程面向第一次接触架构、依赖注入、生命周期和事件系统的 Unity 开发者。阅读时不要求预先理解这些概念。

## 学习路线

| 阶段 | 教程 | 学完后能够做什么 |
|---|---|---|
| 基础 | [01-基础篇：从零完成第一个功能](01-Beginner.md) | 创建 Host、注册服务，使用 Model、System、Command 和 Controller |
| 中级 | [02-中级篇：Scope、生命周期、事件与服务容器](02-Intermediate.md) | 组织大厅/战斗/场景模块，正确管理事件和资源 |
| 高级 | [03-高级篇：异步、多 Host 与架构扩展](03-Advanced.md) | 接入异步服务、预览环境、多世界和复杂初始化流程 |
| Unity | [04-Unity 与第三方 SDK 实战](04-Unity-and-SDK.md) | 使用 Bootstrap、SceneScopeBinder、安全接收 SDK 回调 |
| 多游戏 | [07-大厅与多游戏模块](07-Multi-Game-Modules.md) | 以独立程序集组织大厅和多个游戏，安全切换 GameSession |
| 速查 | [05-常用配方与 API 选择](05-Cookbook.md) | 快速找到常见业务需求的推荐写法 |
| 上线 | [06-故障排查与生产检查表](06-Troubleshooting-and-Production.md) | 定位常见错误并完成商业项目上线检查 |
| 维护 | [08-源码结构与维护指南](08-Codebase-Guide.md) | 理解功能目录、程序集边界和大型类型的拆分规则 |

建议按顺序完成基础篇和中级篇。高级篇可以在项目真正遇到对应需求时再学习。

## 先记住这张图

```text
Architecture（一个独立应用/世界）
└── Root Scope（整个应用）
    ├── Model：保存状态
    ├── System：实现规则
    ├── Utility：提供工具能力
    └── Child Scope（战斗、场景、临时流程）
        ├── Command：执行一次操作
        ├── Query：读取一次结果
        └── Event：通知已经发生的事情
```

ZArch 最重要的思想不是“用了多少设计模式”，而是让对象的职责和存活时间变得明确。

## 三条必须遵守的规则

1. ZArch 采用串行执行模型。Unity 项目统一在主线程访问 Host 和 Scope。
2. 创建了 Scope，就要明确谁负责 Dispose；父 Scope Dispose 会自动 Dispose 子 Scope。
3. 注册事件后必须保存并注销 `IUnregister`，或者使用自动注销扩展。

## 推荐练习项目

按下面顺序实现，每次只增加一个概念：

1. Counter：按钮让数字增加。
2. Shop：金币不足时禁止购买。
3. Battle：进入战斗创建 Child Scope，退出时销毁。
4. Scene Battle：场景加载和卸载自动管理 Scope。
5. Login：用异步 Scope 和 CancellationToken 包装 SDK 登录。

## 分层与程序集

- `ZArch.Core`：Host、Scope、服务、生命周期和事件，不依赖 Unity。
- `ZArch.Patterns`：Model、System、Utility、Command、Query、BindableProperty。
- `ZArch.Unity`：Bootstrap、Controller、Scene 和 Unity 自动注销。
- `ZArch.Unity.Editor`：运行时 Scope 调试窗口。
- `ZArch.GameModules`：可选的游戏模块、Launcher、Session 与切换回滚协议。
- `ZArch.GameModules.Unity`：可选的 Additive Scene 加载与显式场景入口绑定。

业务程序集只引用实际需要的层。纯逻辑测试可以只引用 Core 和 Patterns。

源码目录采用“程序集内按功能分组”：目录用于快速定位职责，asmdef 仍只用于真正的依赖边界。目录调整不会改变 `ZArch`、`ZArch.Unity` 或 `ZArch.GameModules` namespace。需要修改框架源码时，继续阅读[源码结构与维护指南](08-Codebase-Guide.md)。
