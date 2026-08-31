namespace ZArch {
    public interface ICanGetModel : IBelongToScope { }
    public interface ICanGetSystem : IBelongToScope { }
    public interface ICanGetUtility : IBelongToScope { }
    public interface ICanSubscribeEvent : IBelongToScope { }
    public interface ICanPublishEvent : IBelongToScope { }
    public interface ICanSendCommand : IBelongToScope { }
    public interface ICanSendQuery : IBelongToScope { }

    public static class ArchitectureCapabilityExtensions {
        public static T GetModel<T>(this ICanGetModel self) where T : class, IModel => self.GetScope().Resolve<T>();

        public static T GetSystem<T>(this ICanGetSystem self) where T : class, ISystem => self.GetScope().Resolve<T>();

        public static T GetUtility<T>(this ICanGetUtility self) where T : class, IUtility =>
            self.GetScope().Resolve<T>();

        public static IUnregister SubscribeEvent<T>(this ICanSubscribeEvent self, System.Action<T> onEvent) =>
            self.GetScope().Architecture.Subscribe(onEvent);

        public static void UnsubscribeEvent<T>(this ICanSubscribeEvent self, System.Action<T> onEvent) =>
            self.GetScope().Architecture.Unsubscribe(onEvent);

        public static void PublishEvent<T>(this ICanPublishEvent self) where T : new() =>
            self.GetScope().Architecture.Publish<T>();

        public static void PublishEvent<T>(this ICanPublishEvent self, T message) =>
            self.GetScope().Architecture.Publish(message);

        public static IUnregister SubscribeScopedEvent<T>(this ICanSubscribeEvent self, System.Action<T> onEvent) =>
            self.GetScope().Subscribe(onEvent);

        public static void UnsubscribeScopedEvent<T>(this ICanSubscribeEvent self, System.Action<T> onEvent) =>
            self.GetScope().Unsubscribe(onEvent);

        public static void PublishScopedEvent<T>(
            this ICanPublishEvent self,
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
