using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZArch {
    public sealed partial class ArchitectureScope {
        private sealed class ShutdownAction {
            public Func<Task> async;
            public Action sync;
        }

        private readonly List<ShutdownAction> m_ShutdownActions = new();
        private Task m_PrepareShutdown;
        internal bool IsClosing { get; private set; }

        internal void RegisterShutdown(Func<Task> shutdownAsync, Action shutdown) {
            if (IsClosing) {
                throw new InvalidOperationException($"Scope '{Name}' is closing.");
            }

            EnsureNotDisposed();
            m_ShutdownActions.Add(new ShutdownAction { async = shutdownAsync, sync = shutdown });
        }

        // 资源所有者先退出；此时 Scope 仍可解析服务。失败时不进入破坏性的释放阶段。
        internal void PrepareShutdown() {
            if (IsDisposed) {
                return;
            }

            if (m_PrepareShutdown is { IsCompleted: false }) {
                throw new InvalidOperationException($"Scope '{Name}' is closing asynchronously.");
            }

            IsClosing = true;

            foreach (var action in m_ShutdownActions.ToArray()) {
                action.sync();
            }

            foreach (var child in m_Children.ToArray()) {
                child.PrepareShutdown();
            }
        }

        internal Task PrepareShutdownAsync() {
            if (IsDisposed) {
                return Task.CompletedTask;
            }

            if (m_PrepareShutdown is { IsFaulted: false, IsCanceled: false }) {
                return m_PrepareShutdown;
            }

            IsClosing = true;
            return m_PrepareShutdown = PrepareShutdownCoreAsync();
        }

        private async Task PrepareShutdownCoreAsync() {
            foreach (var action in m_ShutdownActions.ToArray()) {
                await action.async();
            }

            foreach (var child in m_Children.ToArray()) {
                await child.PrepareShutdownAsync();
            }
        }
    }
}