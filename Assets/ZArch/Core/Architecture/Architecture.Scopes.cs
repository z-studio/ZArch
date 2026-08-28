using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZArch {
    public partial class Architecture {
        public ArchitectureScope CreateRootScope(string name, Action<ArchitectureScope> setup, object tag = null) {
            EnsureStarted();

            if (setup == null) {
                throw new ArgumentNullException(nameof(setup));
            }

            var scope = CreateUnconfiguredScope(name, null, tag);
            ActivateScope(scope, setup);
            return scope;
        }

        public Task<ArchitectureScope> CreateRootScopeAsync(
            string name,
            Func<ArchitectureScope, Task> setup,
            object tag = null
        ) {
            if (setup == null) {
                throw new ArgumentNullException(nameof(setup));
            }

            return CreateRootScopeAsync(name, (scope, _) => setup(scope), tag, null, CancellationToken.None);
        }

        public Task<ArchitectureScope> CreateRootScopeAsync(
            string name,
            Func<ArchitectureScope, CancellationToken, Task> setup,
            object tag = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        ) {
            EnsureStarted();

            if (setup == null) {
                throw new ArgumentNullException(nameof(setup));
            }

            ValidateTimeout(timeout);
            var scope = CreateUnconfiguredScope(name, null, tag);
            return ActivateScopeAsync(scope, setup, timeout, cancellationToken);
        }

        internal ArchitectureScope CreateChildScope(
            ArchitectureScope parent,
            string name,
            Action<ArchitectureScope> setup,
            object tag
        ) {
            ValidateParent(parent);

            if (setup == null) {
                throw new ArgumentNullException(nameof(setup));
            }

            var scope = CreateUnconfiguredScope(name, parent, tag);
            ActivateScope(scope, setup);
            return scope;
        }

        internal Task<ArchitectureScope> CreateChildScopeAsync(
            ArchitectureScope parent,
            string name,
            Func<ArchitectureScope, Task> setup,
            object tag
        ) {
            if (setup == null) {
                throw new ArgumentNullException(nameof(setup));
            }

            return CreateChildScopeAsync(parent, name, (scope, _) => setup(scope), tag, null, CancellationToken.None);
        }

        internal Task<ArchitectureScope> CreateChildScopeAsync(
            ArchitectureScope parent,
            string name,
            Func<ArchitectureScope, CancellationToken, Task> setup,
            object tag,
            TimeSpan? timeout,
            CancellationToken cancellationToken
        ) {
            ValidateParent(parent);

            if (setup == null) {
                throw new ArgumentNullException(nameof(setup));
            }

            ValidateTimeout(timeout);
            var scope = CreateUnconfiguredScope(name, parent, tag);
            return ActivateScopeAsync(scope, setup, timeout, cancellationToken);
        }

        private ArchitectureScope CreateUnconfiguredScope(string name, ArchitectureScope parent, object tag) {
            EnsureStarted();
            var scope = new ArchitectureScope(this, name, parent, tag);
            m_PendingScopes.Add(scope);
            return scope;
        }

        private void ActivateScope(ArchitectureScope scope, Action<ArchitectureScope> setup) {
            scope.BeginConfiguration();

            try {
                setup(scope);
                ScopeConfiguring?.Invoke(scope);
                scope.Activate();
                AttachActivatedScope(scope);
            } catch {
                scope.Dispose();
                throw;
            }
        }

        private async Task<ArchitectureScope> ActivateScopeAsync(
            ArchitectureScope scope,
            Func<ArchitectureScope, CancellationToken, Task> setup,
            TimeSpan? timeout,
            CancellationToken cancellationToken
        ) {
            scope.BeginConfiguration();

            CancellationTokenSource timeoutCts = null;
            CancellationTokenSource linkedCts = null;

            try {
                timeoutCts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
                var parent = scope.Parent;

                if (parent == null) {
                    linkedCts = timeoutCts == null
                        ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, scope.LifetimeToken)
                        : CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken,
                            timeoutCts.Token,
                            scope.LifetimeToken
                        );
                } else {
                    linkedCts = timeoutCts == null
                        ? CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken,
                            scope.LifetimeToken,
                            parent.LifetimeToken
                        )
                        : CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken,
                            timeoutCts.Token,
                            scope.LifetimeToken,
                            parent.LifetimeToken
                        );
                }

                linkedCts.Token.ThrowIfCancellationRequested();
                var setupTask = setup(scope, linkedCts.Token)
                                ?? throw new InvalidOperationException(
                                    $"Async setup for scope '{scope.Name}' returned null."
                                );
                await setupTask.ConfigureAwait(true);
                linkedCts.Token.ThrowIfCancellationRequested();
                ScopeConfiguring?.Invoke(scope);
                await scope.ActivateAsync(linkedCts.Token).ConfigureAwait(true);
                linkedCts.Token.ThrowIfCancellationRequested();
                AttachActivatedScope(scope);
                return scope;
            } catch {
                scope.Dispose();
                throw;
            } finally {
                linkedCts?.Dispose();
                timeoutCts?.Dispose();
            }
        }

        private void AttachActivatedScope(ArchitectureScope scope) {
            EnsureStarted();

            if (scope.State != EScopeState.Active) {
                throw new InvalidOperationException(
                    $"Scope '{scope.Name}' cannot be attached from state {scope.State}."
                );
            }

            if (scope.Parent != null) {
                ValidateParent(scope.Parent);
            }

            if (!m_PendingScopes.Remove(scope)) {
                throw new InvalidOperationException($"Scope '{scope.Name}' is not pending activation.");
            }

            m_AllScopes.Add(scope);

            if (scope.Parent == null) {
                m_RootScopes.Add(scope);
            } else {
                scope.Parent.AddChild(scope);
            }
        }

        internal void OnScopeDisposed(ArchitectureScope scope) {
            m_PendingScopes.Remove(scope);
            m_AllScopes.Remove(scope);
            m_RootScopes.Remove(scope);
        }

        private void ValidateParent(ArchitectureScope parent) {
            EnsureStarted();

            if (parent == null) {
                throw new ArgumentNullException(nameof(parent));
            }

            if (!ReferenceEquals(parent.Architecture, this)) {
                throw new InvalidOperationException("A child scope must belong to the same Architecture.");
            }

            if (parent.State is EScopeState.Disposing or EScopeState.Disposed) {
                throw new ObjectDisposedException(parent.Name);
            }

            if (parent.State == EScopeState.Faulted) {
                throw new InvalidOperationException($"Scope '{parent.Name}' is faulted.");
            }
        }

        private static void ValidateTimeout(TimeSpan? timeout) {
            if (!timeout.HasValue) {
                return;
            }

            using var validationCts = new CancellationTokenSource(timeout.Value);
        }
    }
}
