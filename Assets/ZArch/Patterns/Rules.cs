namespace ZArch {
    public interface ICanGetModel : IBelongToScope { }
    public interface ICanGetSystem : IBelongToScope { }
    public interface ICanGetUtility : IBelongToScope { }
    public interface ICanRegisterArchitectureEvent : IBelongToScope { }
    public interface ICanSendArchitectureEvent : IBelongToScope { }
    public interface ICanSendCommand : IBelongToScope { }
    public interface ICanSendQuery : IBelongToScope { }

    public static class ArchitectureRuleExtension {
        public static T GetModel<T>(this ICanGetModel self) where T : class, IModel => self.GetScope().Resolve<T>();

        public static T GetSystem<T>(this ICanGetSystem self) where T : class, ISystem => self.GetScope().Resolve<T>();

        public static T GetUtility<T>(this ICanGetUtility self) where T : class, IUtility =>
            self.GetScope().Resolve<T>();

        public static IUnregister RegisterArchitectureEvent<T>(
            this ICanRegisterArchitectureEvent self,
            System.Action<T> onEvent
        ) =>
            self.GetScope().Architecture.RegisterEvent(onEvent);

        public static void UnregisterArchitectureEvent<T>(
            this ICanRegisterArchitectureEvent self,
            System.Action<T> onEvent
        ) =>
            self.GetScope().Architecture.UnregisterEvent(onEvent);

        public static void SendArchitectureEvent<T>(this ICanSendArchitectureEvent self) where T : new() =>
            self.GetScope().Architecture.SendEvent<T>();

        public static void SendArchitectureEvent<T>(this ICanSendArchitectureEvent self, T message) =>
            self.GetScope().Architecture.SendEvent(message);

        public static IUnregister RegisterScopeEvent<T>(this IBelongToScope self, System.Action<T> onEvent) =>
            self.GetScope().RegisterEvent(onEvent);

        public static void PublishScopeEvent<T>(
            this IBelongToScope self,
            T message,
            EEventPropagation propagation = EEventPropagation.Local
        ) =>
            self.GetScope().Publish(message, propagation);

        public static void SendCommand<T>(this ICanSendCommand self) where T : ICommand, new() =>
            self.GetScope().SendCommand(new T());

        public static void SendCommand<T>(this ICanSendCommand self, T command) where T : ICommand =>
            self.GetScope().SendCommand(command);

        public static TResult SendCommand<TResult>(this ICanSendCommand self, ICommand<TResult> command) =>
            self.GetScope().SendCommand(command);

        public static TResult SendQuery<TResult>(this ICanSendQuery self, IQuery<TResult> query) =>
            self.GetScope().SendQuery(query);
    }
}
