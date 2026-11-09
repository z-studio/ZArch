using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ZArch.Unity {
    /// <summary>所有 Unity 入口共用的就绪、界面绑定与关闭规则。</summary>
    public abstract class ArchitectureEntry : MonoBehaviour, IArchitectureContext {
        [SerializeField]
        private ArchitectureView[] m_Views = Array.Empty<ArchitectureView>();

        private readonly HashSet<ArchitectureView> m_BoundViews = new();
        private readonly CancellationTokenSource m_Lifetime = new();
        private readonly TaskCompletionSource<bool> m_Initialization = new();
        private bool m_InitializationStarted;
        private bool m_ShutdownRequested;
        private Task m_Shutdown;

        public ArchitectureScope Scope { get; protected set; }
        public bool IsReady { get; private set; }
        public Task Initialization => m_Initialization.Task;
        protected CancellationToken LifetimeToken => m_Lifetime.Token;
        public IReadOnlyList<ArchitectureView> Views => m_Views;

        protected Task InitializeOnceAsync(Func<Task> initialize) {
            if (m_ShutdownRequested) {
                m_Initialization.TrySetCanceled();
                return Initialization;
            }

            if (!m_InitializationStarted) {
                m_InitializationStarted = true;
                _ = RunInitializationAsync(initialize);
            }

            return Initialization;
        }

        private async Task RunInitializationAsync(Func<Task> initialize) {
            try {
                await initialize();
                LifetimeToken.ThrowIfCancellationRequested();
                m_Initialization.TrySetResult(true);
            } catch (OperationCanceledException) {
                m_Initialization.TrySetCanceled();
            } catch (Exception error) {
                m_Initialization.TrySetException(error);
            }
        }

        protected async Task ObserveInitializationAsync(Task task) {
            try {
                await task;
            } catch (OperationCanceledException) when (m_Lifetime.IsCancellationRequested) {
            } catch (Exception error) {
                Debug.LogException(error, this);
            }
        }

        [ContextMenu("ZArch/Validate Views")]
        public void ValidateViews() {
            if (m_Views == null) {
                throw new InvalidOperationException($"{name}: Views is null.");
            }

            var seen = new HashSet<ArchitectureView>();

            for (var i = 0; i < m_Views.Length; i++) {
                var view = m_Views[i];

                if (!view) {
                    throw new InvalidOperationException(
                        $"{name}: Views[{i}] is missing. Assign a View or remove this item.");
                }

                if (!seen.Add(view)) {
                    throw new InvalidOperationException($"{name}: View '{view.name}' appears more than once.");
                }
            }
        }

        protected void ActivateViews() {
            if (m_ShutdownRequested) {
                throw new ObjectDisposedException(name);
            }

            ValidateViews();
            IsReady = true;

            try {
                foreach (var view in m_Views) {
                    BindView(view);
                }

                OnReady();
            } catch (Exception startupError) {
                try {
                    DeactivateViews();
                } catch (Exception cleanupError) {
                    throw new AggregateException(startupError, cleanupError);
                }

                throw;
            }
        }

        /// <summary>动态弹窗/对象池使用。入口初始化完成后调用。</summary>
        public void BindView(ArchitectureView view) {
            if (m_ShutdownRequested || !IsReady || Scope?.IsActive != true) {
                throw new InvalidOperationException($"{name}: await Initialization before binding Views.");
            }

            if (!view) {
                throw new ArgumentNullException(nameof(view));
            }

            view.Attach(this);
            m_BoundViews.Add(view);
        }

        public void UnbindView(ArchitectureView view) {
            if (ReferenceEquals(view, null)) {
                throw new ArgumentNullException(nameof(view));
            }

            if (!ReferenceEquals(view.Owner, this)) {
                throw new InvalidOperationException(
                    $"{name}: View '{view.name}' belongs to another entry or is not bound.");
            }

            try {
                view.Detach();
            } finally {
                m_BoundViews.Remove(view);
            }
        }

        internal void ForgetView(ArchitectureView view) => m_BoundViews.Remove(view);

        protected void DeactivateViews() {
            IsReady = false;
            var errors = new List<Exception>();

            foreach (var view in new List<ArchitectureView>(m_BoundViews)) {
                if (!view) {
                    continue;
                }

                try {
                    view.Detach();
                } catch (Exception error) {
                    errors.Add(error);
                }
            }

            m_BoundViews.Clear();

            if (errors.Count > 0) {
                throw new AggregateException(errors);
            }
        }

        public Task ShutdownAsync() {
            if (m_Shutdown != null && !m_Shutdown.IsFaulted && !m_Shutdown.IsCanceled) {
                return m_Shutdown;
            }

            m_ShutdownRequested = true;

            if (!m_InitializationStarted) {
                m_Initialization.TrySetCanceled();
            }

            m_Lifetime.Cancel();
            return m_Shutdown = ShutdownEntryAsync();
        }

        private async Task ShutdownEntryAsync() {
            if (m_InitializationStarted) {
                try {
                    await Initialization;
                } catch {
                    /* 初始化错误由调用方观察；仍要清理资源。 */
                }
            }

            await ShutdownCoreAsync();
        }

        protected virtual void OnReady() {
        }

        protected abstract Task ShutdownCoreAsync();

        // Unity 不会等待 OnDestroy。正常业务流程应显式 await ShutdownAsync。
        protected virtual async void OnDestroy() {
            try {
                await ShutdownAsync();
            } catch (Exception error) {
                Debug.LogException(error);
            }
        }
    }
}