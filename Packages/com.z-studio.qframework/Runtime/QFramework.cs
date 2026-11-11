/****************************************************************************
 * Copyright (c) 2015 ~ 2025 liangxiegame MIT License
 *
 * QFramework v1.0
 *
 * https://qframework.cn
 * https://github.com/liangxiegame/QFramework
 * https://gitee.com/liangxiegame/QFramework
 *
 * Author:
 *  liangxie        https://github.com/liangxie
 *  soso            https://github.com/so-sos-so
 *
 * Contributor
 *  TastSong        https://github.com/TastSong
 *  京产肠饭         https://gitee.com/JingChanChangFan/hk_-unity-tools
 *  猫叔(一只皮皮虾) https://space.bilibili.com/656352/
 *  misakiMeiii     https://github.com/misakiMeiii
 *  New一天
 *  幽飞冷凝雪～冷
 *
 * Community
 *  QQ 群 1: 623597263(已满)
 *  QQ 群 2: 541745166
 *  腾讯频道：https://pd.qq.com/s/cfe1690jf
 *  Discord：https://discord.gg/PHqHX5v5SE
 *
 * Latest Update: 2026.8.12 14:56 return module instance after register
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QFramework {
    #region Architecture

    public interface IArchitecture {
        TSystem RegisterSystem<TSystem>(TSystem system) where TSystem : ISystem;

        TModel RegisterModel<TModel>(TModel model) where TModel : IModel;

        TUtility RegisterUtility<TUtility>(TUtility utility) where TUtility : IUtility;

        TSystem GetSystem<TSystem>() where TSystem : class, ISystem;

        TModel GetModel<TModel>() where TModel : class, IModel;

        TUtility GetUtility<TUtility>() where TUtility : class, IUtility;

        void SendCommand<T>(T command) where T : ICommand;

        TResult SendCommand<TResult>(ICommand<TResult> command);

        TResult SendQuery<TResult>(IQuery<TResult> query);

        void SendEvent<T>() where T : new();
        void SendEvent<T>(T e);

        IUnregister RegisterEvent<T>(Action<T> onEvent);
        void UnregisterEvent<T>(Action<T> onEvent);

        void Deinit();
    }

    public abstract class Architecture<T> : IArchitecture where T : Architecture<T>, new() {
        private bool m_Inited = false;

        public static Action<T> OnRegisterPatch = architecture => { };

        protected static T Arch;

        public static IArchitecture Interface {
            get {
                if (Arch == null) {
                    InitArchitecture();
                }

                return Arch;
            }
        }


        public static void InitArchitecture() {
            if (Arch == null) {
                Arch = new T();
                Arch.Init();

                OnRegisterPatch?.Invoke(Arch);

                foreach (var model in Arch.m_Container.GetInstancesByType<IModel>()
                             .Where(m => !m.Initialized)) {
                    model.Init();
                    model.Initialized = true;
                }

                foreach (var system in Arch.m_Container.GetInstancesByType<ISystem>()
                             .Where(m => !m.Initialized)) {
                    system.Init();
                    system.Initialized = true;
                }

                Arch.m_Inited = true;
            }
        }

        protected abstract void Init();

        public void Deinit() {
            OnDeinit();

            foreach (var system in m_Container.GetInstancesByType<ISystem>().Where(s => s.Initialized)) {
                system.Deinit();
            }

            foreach (var model in m_Container.GetInstancesByType<IModel>().Where(m => m.Initialized)) {
                model.Deinit();
            }

            m_Container.Clear();
            Arch = null;
        }

        protected virtual void OnDeinit() {
        }

        private readonly IOCContainer m_Container = new();

        // 注册类型就是查询的键：注册接口时应显式写 RegisterSystem<IMySystem>，之后按同一接口获取。
        public TSystem RegisterSystem<TSystem>(TSystem system) where TSystem : ISystem {
            system.SetArchitecture(this);
            m_Container.Register<TSystem>(system);

            if (m_Inited) {
                system.Init();
                system.Initialized = true;
            }

            return system;
        }

        public TModel RegisterModel<TModel>(TModel model) where TModel : IModel {
            model.SetArchitecture(this);
            m_Container.Register<TModel>(model);

            if (m_Inited) {
                model.Init();
                model.Initialized = true;
            }

            return model;
        }

        public TUtility RegisterUtility<TUtility>(TUtility utility) where TUtility : IUtility {
            m_Container.Register<TUtility>(utility);
            return utility;
        }


        public TSystem GetSystem<TSystem>() where TSystem : class, ISystem => m_Container.Get<TSystem>();

        public TModel GetModel<TModel>() where TModel : class, IModel => m_Container.Get<TModel>();

        public TUtility GetUtility<TUtility>() where TUtility : class, IUtility => m_Container.Get<TUtility>();

        public TResult SendCommand<TResult>(ICommand<TResult> command) => ExecuteCommand(command);

        public void SendCommand<TCommand>(TCommand command) where TCommand : ICommand => ExecuteCommand(command);

        protected virtual TResult ExecuteCommand<TResult>(ICommand<TResult> command) {
            command.SetArchitecture(this);
            return command.Execute();
        }

        protected virtual void ExecuteCommand(ICommand command) {
            command.SetArchitecture(this);
            command.Execute();
        }

        public TResult SendQuery<TResult>(IQuery<TResult> query) => DoQuery<TResult>(query);

        protected virtual TResult DoQuery<TResult>(IQuery<TResult> query) {
            query.SetArchitecture(this);
            return query.Do();
        }

        // 架构持有一个普通事件系统；Session 也把普通事件交到它，而不是各建一个隔离通道。
        private readonly TypeEventSystem m_TypeEventSystem = new();

        public void SendEvent<TEvent>() where TEvent : new() => m_TypeEventSystem.Send<TEvent>();

        public void SendEvent<TEvent>(TEvent e) => m_TypeEventSystem.Send<TEvent>(e);

        public IUnregister RegisterEvent<TEvent>(Action<TEvent> onEvent) => m_TypeEventSystem.Register<TEvent>(onEvent);

        public void UnregisterEvent<TEvent>(Action<TEvent> onEvent) => m_TypeEventSystem.Unregister<TEvent>(onEvent);
    }

    public interface IOnEvent<T> {
        void OnEvent(T e);
    }

    public static class OnGlobalEventExtension {
        public static IUnregister RegisterEvent<T>(this IOnEvent<T> self) where T : struct =>
            TypeEventSystem.Global.Register<T>(self.OnEvent);

        public static void UnregisterEvent<T>(this IOnEvent<T> self) where T : struct =>
            TypeEventSystem.Global.Unregister<T>(self.OnEvent);
    }

    #endregion

    #region Controller

    public interface IController : IBelongToArchitecture, ICanSendCommand, ICanGetSystem, ICanGetModel,
        ICanRegisterEvent, ICanSendQuery, ICanGetUtility {
    }

    #endregion

    #region System

    public interface ISystem : IBelongToArchitecture, ICanSetArchitecture, ICanGetModel, ICanGetUtility,
        ICanRegisterEvent, ICanSendEvent, ICanGetSystem, ICanInit {
    }

    public abstract class AbstractSystem : ISystem {
        private IArchitecture m_Architecture;

        IArchitecture IBelongToArchitecture.GetArchitecture() => m_Architecture;

        void ICanSetArchitecture.SetArchitecture(IArchitecture architecture) => m_Architecture = architecture;

        public bool Initialized { get; set; }
        void ICanInit.Init() => OnInit();

        public void Deinit() => OnDeinit();

        protected virtual void OnDeinit() {
        }

        protected abstract void OnInit();
    }

    #endregion

    #region Model

    public interface IModel : IBelongToArchitecture, ICanSetArchitecture, ICanGetUtility, ICanSendEvent, ICanInit {
    }

    public abstract class AbstractModel : IModel {
        private IArchitecture m_Architecture;

        IArchitecture IBelongToArchitecture.GetArchitecture() => m_Architecture;

        void ICanSetArchitecture.SetArchitecture(IArchitecture architecture) => m_Architecture = architecture;

        public bool Initialized { get; set; }
        void ICanInit.Init() => OnInit();
        public void Deinit() => OnDeinit();

        protected virtual void OnDeinit() {
        }

        protected abstract void OnInit();
    }

    #endregion

    #region Utility

    public interface IUtility {
    }

    #endregion

    #region Command

    public interface ICommand : IBelongToArchitecture, ICanSetArchitecture, ICanGetSystem, ICanGetModel, ICanGetUtility,
        ICanSendEvent, ICanSendCommand, ICanSendQuery {
        void Execute();
    }

    public interface ICommand<TResult> : IBelongToArchitecture, ICanSetArchitecture, ICanGetSystem, ICanGetModel,
        ICanGetUtility, ICanSendEvent, ICanSendCommand, ICanSendQuery {
        TResult Execute();
    }

    public abstract class AbstractCommand : ICommand {
        private IArchitecture m_Architecture;

        IArchitecture IBelongToArchitecture.GetArchitecture() => m_Architecture;

        void ICanSetArchitecture.SetArchitecture(IArchitecture architecture) => m_Architecture = architecture;

        void ICommand.Execute() => OnExecute();

        protected abstract void OnExecute();
    }

    public abstract class AbstractCommand<TResult> : ICommand<TResult> {
        private IArchitecture m_Architecture;

        IArchitecture IBelongToArchitecture.GetArchitecture() => m_Architecture;

        void ICanSetArchitecture.SetArchitecture(IArchitecture architecture) => m_Architecture = architecture;

        TResult ICommand<TResult>.Execute() => OnExecute();

        protected abstract TResult OnExecute();
    }

    #endregion

    #region Query

    public interface IQuery<TResult> : IBelongToArchitecture, ICanSetArchitecture, ICanGetModel, ICanGetSystem,
        ICanSendQuery {
        TResult Do();
    }

    public abstract class AbstractQuery<T> : IQuery<T> {
        public T Do() => OnDo();

        protected abstract T OnDo();

        private IArchitecture m_Architecture;

        public IArchitecture GetArchitecture() => m_Architecture;

        public void SetArchitecture(IArchitecture architecture) => m_Architecture = architecture;
    }

    #endregion

    #region Rule

    public interface IBelongToArchitecture {
        IArchitecture GetArchitecture();
    }

    public interface ICanSetArchitecture {
        void SetArchitecture(IArchitecture architecture);
    }

    public interface ICanGetModel : IBelongToArchitecture {
    }

    public static class CanGetModelExtension {
        public static T GetModel<T>(this ICanGetModel self) where T : class, IModel =>
            self.GetArchitecture().GetModel<T>();
    }

    public interface ICanGetSystem : IBelongToArchitecture {
    }

    public static class CanGetSystemExtension {
        public static T GetSystem<T>(this ICanGetSystem self) where T : class, ISystem =>
            self.GetArchitecture().GetSystem<T>();
    }

    public interface ICanGetUtility : IBelongToArchitecture {
    }

    public static class CanGetUtilityExtension {
        public static T GetUtility<T>(this ICanGetUtility self) where T : class, IUtility =>
            self.GetArchitecture().GetUtility<T>();
    }

    public interface ICanRegisterEvent : IBelongToArchitecture {
    }

    /// <summary>由有自身显示周期的 Controller 接管订阅的生命周期；不改变事件来源和传播范围。</summary>
    public interface IEventRegistrationOwner : ICanRegisterEvent {
        IUnregister RegisterEvent<T>(Action<T> handler);
        void UnregisterEvent<T>(Action<T> handler);
    }

    public static class CanRegisterEventExtension {
        public static IUnregister RegisterEvent<T>(this ICanRegisterEvent self, Action<T> onEvent) =>
            self is IEventRegistrationOwner owner
                ? owner.RegisterEvent(onEvent) : self.GetArchitecture().RegisterEvent(onEvent);

        public static void UnregisterEvent<T>(this ICanRegisterEvent self, Action<T> onEvent) {
            if (self is IEventRegistrationOwner owner) {
                owner.UnregisterEvent(onEvent);
            } else {
                self.GetArchitecture().UnregisterEvent(onEvent);
            }
        }
    }

    public interface ICanSendCommand : IBelongToArchitecture {
    }

    public static class CanSendCommandExtension {
        public static void SendCommand<T>(this ICanSendCommand self) where T : ICommand, new() =>
            self.GetArchitecture().SendCommand<T>(new T());

        public static void SendCommand<T>(this ICanSendCommand self, T command) where T : ICommand =>
            self.GetArchitecture().SendCommand<T>(command);

        public static TResult SendCommand<TResult>(this ICanSendCommand self, ICommand<TResult> command) =>
            self.GetArchitecture().SendCommand(command);
    }

    public interface ICanSendEvent : IBelongToArchitecture {
    }

    public static class CanSendEventExtension {
        public static void SendEvent<T>(this ICanSendEvent self) where T : new() =>
            self.GetArchitecture().SendEvent<T>();

        public static void SendEvent<T>(this ICanSendEvent self, T e) => self.GetArchitecture().SendEvent<T>(e);
    }

    public interface ICanSendQuery : IBelongToArchitecture {
    }

    public static class CanSendQueryExtension {
        public static TResult SendQuery<TResult>(this ICanSendQuery self, IQuery<TResult> query) =>
            self.GetArchitecture().SendQuery(query);
    }

    public interface ICanInit {
        bool Initialized { get; set; }
        void Init();
        void Deinit();
    }

    #endregion

    #region TypeEventSystem

    public interface IUnregister {
        void Unregister();
    }

    public interface IUnregisterList {
        List<IUnregister> UnregisterList { get; }
    }

    public static class IUnregisterListExtension {
        public static void AddToUnregisterList(this IUnregister self, IUnregisterList unregisterList) =>
            unregisterList.UnregisterList.Add(self);

        public static void UnregisterAll(this IUnregisterList self) {
            foreach (var unRegister in self.UnregisterList) {
                unRegister.Unregister();
            }

            self.UnregisterList.Clear();
        }
    }

    public struct CustomUnregister : IUnregister {
        private Action OnUnregister { get; set; }
        public CustomUnregister(Action onUnregister) => OnUnregister = onUnregister;

        public void Unregister() {
            OnUnregister.Invoke();
            OnUnregister = null;
        }
    }

    public abstract class UnregisterTrigger : MonoBehaviour {
        private readonly HashSet<IUnregister> m_Unregisters = new();

        public IUnregister AddUnregister(IUnregister unregister) {
            m_Unregisters.Add(unregister);
            return unregister;
        }

        public void RemoveUnregister(IUnregister unregister) => m_Unregisters.Remove(unregister);

        public void Unregister() {
            foreach (var unRegister in m_Unregisters) {
                unRegister.Unregister();
            }

            m_Unregisters.Clear();
        }
    }

    public class UnregisterOnDestroyTrigger : UnregisterTrigger {
        private void OnDestroy() {
            Unregister();
        }
    }

    public class UnregisterOnDisableTrigger : UnregisterTrigger {
        private void OnDisable() {
            Unregister();
        }
    }

    public class UnregisterCurrentSceneUnloadedTrigger : UnregisterTrigger {
        private static UnregisterCurrentSceneUnloadedTrigger s_Default;

        public static UnregisterCurrentSceneUnloadedTrigger Get {
            get {
                if (!s_Default) {
                    s_Default = new GameObject("UnregisterCurrentSceneUnloadedTrigger")
                        .AddComponent<UnregisterCurrentSceneUnloadedTrigger>();
                }

                return s_Default;
            }
        }

        private void Awake() {
            DontDestroyOnLoad(this);
            hideFlags = HideFlags.HideInHierarchy;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDestroy() => SceneManager.sceneUnloaded -= OnSceneUnloaded;

        private void OnSceneUnloaded(Scene scene) => Unregister();
    }

    public static class UnregisterExtension {
        static T GetOrAddComponent<T>(GameObject gameObject) where T : Component {
            var trigger = gameObject.GetComponent<T>();

            if (!trigger) {
                trigger = gameObject.AddComponent<T>();
            }

            return trigger;
        }

        public static IUnregister UnregisterWhenGameObjectDestroyed(this IUnregister unregister,
            UnityEngine.GameObject gameObject) =>
            GetOrAddComponent<UnregisterOnDestroyTrigger>(gameObject)
                .AddUnregister(unregister);

        public static IUnregister UnregisterWhenGameObjectDestroyed<T>(this IUnregister self, T component)
            where T : UnityEngine.Component =>
            self.UnregisterWhenGameObjectDestroyed(component.gameObject);

        public static IUnregister UnregisterWhenDisabled<T>(this IUnregister self, T component)
            where T : UnityEngine.Component =>
            self.UnregisterWhenDisabled(component.gameObject);

        public static IUnregister UnregisterWhenDisabled(this IUnregister unregister,
            UnityEngine.GameObject gameObject) =>
            GetOrAddComponent<UnregisterOnDisableTrigger>(gameObject)
                .AddUnregister(unregister);

        public static IUnregister UnregisterWhenCurrentSceneUnloaded(this IUnregister self) =>
            UnregisterCurrentSceneUnloadedTrigger.Get.AddUnregister(self);
    }

    public class TypeEventSystem {
        private readonly EasyEvents m_Events = new();

        public static readonly TypeEventSystem Global = new();

        public void Send<T>() where T : new() => m_Events.GetEvent<EasyEvent<T>>()?.Trigger(new T());

        public void Send<T>(T e) => m_Events.GetEvent<EasyEvent<T>>()?.Trigger(e);

        public IUnregister Register<T>(Action<T> onEvent) => m_Events.GetOrAddEvent<EasyEvent<T>>().Register(onEvent);

        public void Unregister<T>(Action<T> onEvent) {
            var e = m_Events.GetEvent<EasyEvent<T>>();
            e?.Unregister(onEvent);
        }
    }

    #endregion

    #region IOC

    public class IOCContainer {
        private readonly Dictionary<Type, object> m_Instances = new();

        // 容器按 typeof(T) 精确匹配，不会遍历实例并猜测它实现了哪些接口。
        public void Register<T>(T instance) {
            var key = typeof(T);
            m_Instances[key] = instance;
        }

        public T Get<T>() where T : class {
            var key = typeof(T);

            if (m_Instances.TryGetValue(key, out var retInstance)) {
                return retInstance as T;
            }

            return null;
        }

        public IEnumerable<T> GetInstancesByType<T>() {
            var type = typeof(T);
            return m_Instances.Values.Where(instance => type.IsInstanceOfType(instance)).Cast<T>();
        }

        public void Clear() => m_Instances.Clear();
    }

    #endregion

    #region BindableProperty

    public interface IBindableProperty<T> : IReadonlyBindableProperty<T> {
        new T Value { get; set; }
        void SetValueWithoutEvent(T newValue);
    }

    public interface IReadonlyBindableProperty<T> : IEasyEvent {
        T Value { get; }

        IUnregister RegisterAndInvoke(Action<T> action);
        void Unregister(Action<T> onValueChanged);
        IUnregister Register(Action<T> onValueChanged);
    }

    public class BindableProperty<T> : IBindableProperty<T> {
        public BindableProperty(T defaultValue = default) => value = defaultValue;

        protected T value;

        public static Func<T, T, bool> Comparer { get; set; } = (a, b) => a.Equals(b);

        public BindableProperty<T> WithComparer(Func<T, T, bool> comparer) {
            Comparer = comparer;
            return this;
        }

        public T Value {
            get => GetValue();
            set {
                if (value == null && this.value == null) {
                    return;
                }

                if (value != null && Comparer(value, this.value)) {
                    return;
                }

                SetValue(value);
                m_OnValueChanged.Trigger(value);
            }
        }

        protected virtual void SetValue(T newValue) => value = newValue;

        protected virtual T GetValue() => value;

        public void SetValueWithoutEvent(T newValue) => value = newValue;

        private readonly EasyEvent<T> m_OnValueChanged = new();

        public IUnregister Register(Action<T> onValueChanged) {
            return m_OnValueChanged.Register(onValueChanged);
        }

        public IUnregister RegisterAndInvoke(Action<T> onValueChanged) {
            onValueChanged(value);
            return Register(onValueChanged);
        }

        public void Unregister(Action<T> onValueChanged) => m_OnValueChanged.Unregister(onValueChanged);

        IUnregister IEasyEvent.Register(Action onEvent) {
            return Register(Action);
            void Action(T _) => onEvent();
        }

        public override string ToString() => Value.ToString();
    }

    internal class ComparerAutoRegister {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void AutoRegister() {
            BindableProperty<int>.Comparer = (a, b) => a == b;
            BindableProperty<float>.Comparer = (a, b) => a == b;
            BindableProperty<double>.Comparer = (a, b) => a == b;
            BindableProperty<string>.Comparer = (a, b) => a == b;
            BindableProperty<long>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Vector2>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Vector3>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Vector4>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Color>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Color32>.Comparer =
                (a, b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
            BindableProperty<UnityEngine.Bounds>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Rect>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Quaternion>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Vector2Int>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Vector3Int>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.BoundsInt>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.RangeInt>.Comparer = (a, b) => a.start == b.start && a.length == b.length;
            BindableProperty<UnityEngine.RectInt>.Comparer = (a, b) => a.Equals(b);
        }
    }

    #endregion

    #region EasyEvent

    public interface IEasyEvent {
        IUnregister Register(Action onEvent);
    }

    public class EasyEvent : IEasyEvent {
        private Action m_OnEvent = () => { };

        public IUnregister Register(Action onEvent) {
            m_OnEvent += onEvent;
            return new CustomUnregister(() => { Unregister(onEvent); });
        }

        public IUnregister RegisterWithACall(Action onEvent) {
            onEvent.Invoke();
            return Register(onEvent);
        }

        public void Unregister(Action onEvent) => m_OnEvent -= onEvent;

        public void Trigger() => m_OnEvent?.Invoke();
    }

    public class EasyEvent<T> : IEasyEvent {
        private Action<T> m_OnEvent = e => { };

        public IUnregister Register(Action<T> onEvent) {
            m_OnEvent += onEvent;
            return new CustomUnregister(() => { Unregister(onEvent); });
        }

        public void Unregister(Action<T> onEvent) => m_OnEvent -= onEvent;


        public void Trigger(T t) => m_OnEvent?.Invoke(t);

        IUnregister IEasyEvent.Register(Action onEvent) {
            return Register(Action);
            void Action(T _) => onEvent();
        }
    }

    public class EasyEvent<T, K> : IEasyEvent {
        private Action<T, K> m_OnEvent = (t, k) => { };

        public IUnregister Register(Action<T, K> onEvent) {
            m_OnEvent += onEvent;
            return new CustomUnregister(() => { Unregister(onEvent); });
        }

        public void Unregister(Action<T, K> onEvent) => m_OnEvent -= onEvent;

        public void Trigger(T t, K k) => m_OnEvent?.Invoke(t, k);

        IUnregister IEasyEvent.Register(Action onEvent) {
            return Register(Action);
            void Action(T _, K __) => onEvent();
        }
    }

    public class EasyEvent<T, K, S> : IEasyEvent {
        private Action<T, K, S> m_OnEvent = (t, k, s) => { };

        public IUnregister Register(Action<T, K, S> onEvent) {
            m_OnEvent += onEvent;
            return new CustomUnregister(() => { Unregister(onEvent); });
        }

        public void Unregister(Action<T, K, S> onEvent) => m_OnEvent -= onEvent;

        public void Trigger(T t, K k, S s) => m_OnEvent?.Invoke(t, k, s);

        IUnregister IEasyEvent.Register(Action onEvent) {
            return Register(Action);
            void Action(T _, K __, S ___) => onEvent();
        }
    }

    public class EasyEvents {
        private static readonly EasyEvents s_GlobalEvents = new();

        public static T Get<T>() where T : IEasyEvent => s_GlobalEvents.GetEvent<T>();

        public static void Register<T>() where T : IEasyEvent, new() => s_GlobalEvents.AddEvent<T>();

        private readonly Dictionary<Type, IEasyEvent> m_TypeEvents = new();

        public void AddEvent<T>() where T : IEasyEvent, new() => m_TypeEvents.Add(typeof(T), new T());

        public T GetEvent<T>() where T : IEasyEvent {
            return m_TypeEvents.TryGetValue(typeof(T), out var e) ? (T)e : default;
        }

        public T GetOrAddEvent<T>() where T : IEasyEvent, new() {
            var eType = typeof(T);

            if (m_TypeEvents.TryGetValue(eType, out var e)) {
                return (T)e;
            }

            var t = new T();
            m_TypeEvents.Add(eType, t);
            return t;
        }
    }

    #endregion


    #region Event Extension

    public class OrEvent : IUnregisterList {
        public OrEvent Or(IEasyEvent easyEvent) {
            easyEvent.Register(Trigger).AddToUnregisterList(this);
            return this;
        }

        private Action m_OnEvent = () => { };

        public IUnregister Register(Action onEvent) {
            m_OnEvent += onEvent;
            return new CustomUnregister(() => { Unregister(onEvent); });
        }

        public IUnregister RegisterWithACall(Action onEvent) {
            onEvent.Invoke();
            return Register(onEvent);
        }

        public void Unregister(Action onEvent) {
            m_OnEvent -= onEvent;
            this.UnregisterAll();
        }

        private void Trigger() => m_OnEvent?.Invoke();

        public List<IUnregister> UnregisterList { get; } = new();
    }

    public static class OrEventExtensions {
        public static OrEvent Or(this IEasyEvent self, IEasyEvent e) => new OrEvent().Or(self).Or(e);
    }

    #endregion
}