using System;
using System.Collections.Generic;
using System.Threading;

namespace QFramework {
    /// <summary>管理一段使用期间的数据、服务与订阅；服务向父级查找，事件共用根架构。</summary>
    public sealed class Session : IArchitecture, IDisposable {
        private readonly Dictionary<Type, object> m_Instances = new();
        private readonly List<object> m_RegistrationOrder = new();
        private readonly List<ICanInit> m_InitOrder = new();
        private readonly List<Subscription> m_Subscriptions = new();
        private readonly List<Session> m_Children = new();
        private readonly CancellationTokenSource m_Lifetime = new();
        private bool m_Initialized;
        private bool m_Initializing;
        private bool m_Disposing;

        public string Name { get; }
        public IArchitecture Parent { get; }
        public IArchitecture App => Parent is Session session ? session.App : Parent;
        public bool IsDisposed { get; private set; }
        public CancellationToken LifetimeToken { get; }

        public Session(IArchitecture parent, string name) {
            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
            Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Session name is empty.", nameof(name))
                : name;
            LifetimeToken = m_Lifetime.Token;

            if (parent is Session session) {
                session.EnsureReady();
                session.m_Children.Add(this);
            }
        }

        public T RegisterModel<T>(T model) where T : IModel => Register(model);
        public T RegisterSystem<T>(T system) where T : ISystem => Register(system);
        public T RegisterUtility<T>(T utility) where T : IUtility => Register(utility);

        private T Register<T>(T instance) {
            EnsureAlive();

            if (m_Initialized || m_Initializing || m_Disposing) {
                throw new InvalidOperationException("Register dependencies before Session.Initialize().");
            }

            if (instance == null) {
                throw new ArgumentNullException(nameof(instance));
            }

            if (m_Instances.ContainsKey(typeof(T)) || m_RegistrationOrder.Exists(x => ReferenceEquals(x, instance))) {
                throw new InvalidOperationException($"{typeof(T).Name} is already registered in {Name}.");
            }

            if (instance is IBelongToArchitecture bound && bound.GetArchitecture() != null) {
                throw new InvalidOperationException(
                    "Register a fresh local instance; parent instances are resolved automatically.");
            }

            if (instance is ICanInit initialized && initialized.Initialized) {
                throw new InvalidOperationException("An initialized instance cannot be registered again.");
            }

            m_Instances.Add(typeof(T), instance);
            m_RegistrationOrder.Add(instance);

            if (instance is ICanSetArchitecture aware) {
                aware.SetArchitecture(this);
            }

            return instance;
        }

        /// <summary>配置完成后调用一次，保持 QF 的 Model → System 初始化顺序。</summary>
        public void Initialize() {
            EnsureAlive();

            if (m_Initialized) {
                return;
            }

            if (m_Initializing || m_Disposing) {
                throw new InvalidOperationException("Session is changing lifecycle state.");
            }

            m_Initializing = true;

            try {
                foreach (var instance in m_RegistrationOrder) {
                    if (instance is IModel model) {
                        Init(model);
                    }
                }

                foreach (var instance in m_RegistrationOrder) {
                    if (instance is ISystem system) {
                        Init(system);
                    }
                }

                m_Initialized = true;
            } catch (Exception error) {
                try {
                    Dispose();
                } catch (Exception cleanup) {
                    throw new AggregateException(error, cleanup);
                }

                throw;
            } finally {
                m_Initializing = false;
            }
        }

        private void Init(ICanInit instance) {
            // 先记录，初始化中途失败的对象也能执行 OnDeinit 清理。
            m_InitOrder.Add(instance);
            instance.Init();
            instance.Initialized = true;
        }

        public T GetModel<T>() where T : class, IModel => Get<T>() ?? Parent.GetModel<T>();
        public T GetSystem<T>() where T : class, ISystem => Get<T>() ?? Parent.GetSystem<T>();
        public T GetUtility<T>() where T : class, IUtility => Get<T>() ?? Parent.GetUtility<T>();

        private T Get<T>() where T : class {
            EnsureAlive();
            return m_Instances.TryGetValue(typeof(T), out var value) ? (T)value : null;
        }

        public void SendCommand<T>(T command) where T : ICommand {
            EnsureReady();
            command.SetArchitecture(this);
            command.Execute();
        }

        public TResult SendCommand<TResult>(ICommand<TResult> command) {
            EnsureReady();
            command.SetArchitecture(this);
            return command.Execute();
        }

        public TResult SendQuery<TResult>(IQuery<TResult> query) {
            EnsureReady();
            query.SetArchitecture(this);
            return query.Do();
        }

        // 默认事件直接使用根架构的事件系统，与 QF 的普通 SendEvent/RegisterEvent 一致。
        public void SendEvent<T>() where T : new() => SendEvent(new T());

        public void SendEvent<T>(T message) {
            EnsureAlive();
            App.SendEvent(message);
        }

        public IUnregister RegisterEvent<T>(Action<T> handler) {
            EnsureAlive();

            if (handler == null) {
                throw new ArgumentNullException(nameof(handler));
            }

            // 为每次注册建立独立委托，解除游戏订阅不会误删根架构或其他会话中的同名处理方法。
            void Receive(T message) {
                if (!IsDisposed && !m_Disposing) {
                    handler(message);
                }
            }

            return Track(App.RegisterEvent<T>(Receive), typeof(T), handler);
        }

        public void UnregisterEvent<T>(Action<T> handler) {
            EnsureAlive();

            // 与 QF 的委托解除规则一致：每次解除最后一次匹配的注册，只处理当前会话拥有的订阅。
            for (var i = m_Subscriptions.Count - 1; i >= 0; i--) {
                var subscription = m_Subscriptions[i];

                if (subscription.EventType == typeof(T) && Equals(subscription.Handler, handler)) {
                    subscription.Unregister();
                    return;
                }
            }
        }

        /// <summary>跟踪事件或 BindableProperty 等订阅，在会话结束时自动取消。</summary>
        public IUnregister Track(IUnregister subscription) => Track(subscription, null, null);

        private IUnregister Track(IUnregister subscription, Type eventType, Delegate handler) {
            EnsureAlive();

            if (subscription == null) {
                throw new ArgumentNullException(nameof(subscription));
            }

            if (m_Disposing) {
                subscription.Unregister();
                throw new ObjectDisposedException(Name);
            }

            var tracked = new Subscription(this, subscription, eventType, handler);
            m_Subscriptions.Add(tracked);
            return tracked;
        }

        public void CancelLifetime() {
            if (!IsDisposed && !m_Lifetime.IsCancellationRequested) {
                m_Lifetime.Cancel();
            }
        }

        public void Deinit() => Dispose();

        public void Dispose() {
            if (IsDisposed || m_Disposing) {
                return;
            }

            m_Disposing = true;
            var errors = new List<Exception>();

            void Attempt(Action action) {
                try {
                    action();
                } catch (Exception e) {
                    errors.Add(e);
                }
            }

            Attempt(CancelLifetime);

            foreach (var child in m_Children.ToArray()) {
                Attempt(child.Dispose);
            }

            for (var i = m_InitOrder.Count - 1; i >= 0; i--) {
                var instance = m_InitOrder[i];
                Attempt(instance.Deinit);
                instance.Initialized = false;
            }

            foreach (var subscription in new List<Subscription>(m_Subscriptions)) {
                Attempt(subscription.Unregister);
            }

            for (var i = m_RegistrationOrder.Count - 1; i >= 0; i--) {
                if (m_RegistrationOrder[i] is IDisposable disposable) {
                    Attempt(disposable.Dispose);
                }
            }

            m_Instances.Clear();
            m_RegistrationOrder.Clear();
            m_InitOrder.Clear();

            if (Parent is Session parent) {
                parent.m_Children.Remove(this);
            }

            m_Lifetime.Dispose();
            IsDisposed = true;
            m_Disposing = false;

            if (errors.Count > 0) {
                throw new AggregateException($"Session '{Name}' cleanup failed.", errors);
            }
        }

        private void EnsureAlive() {
            if (IsDisposed) {
                throw new ObjectDisposedException(Name);
            }
        }

        private void EnsureReady() {
            EnsureAlive();

            if (!m_Initialized || m_Disposing) {
                throw new InvalidOperationException("Session is not active.");
            }
        }

        private sealed class Subscription : IUnregister {
            private Session m_Owner;
            private IUnregister m_Inner;

            public Type EventType { get; private set; }
            public Delegate Handler { get; private set; }

            public Subscription(Session owner, IUnregister inner, Type eventType, Delegate handler) {
                m_Owner = owner;
                m_Inner = inner;
                EventType = eventType;
                Handler = handler;
            }

            public void Unregister() {
                var inner = m_Inner;

                if (inner == null) {
                    return;
                }

                m_Inner = null;
                EventType = null;
                Handler = null;
                m_Owner.m_Subscriptions.Remove(this);
                m_Owner = null;
                inner.Unregister();
            }
        }
    }

    public static class SessionExtensions {
        /// <summary>将属性或其他外部订阅绑定到会话，结束时自动解除。</summary>
        public static IUnregister UnregisterWhenSessionEnds(this IUnregister subscription, Session session) =>
            session.Track(subscription);
    }
}