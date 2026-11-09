namespace ZArch {
    public static class ArchitectureExtensions {
        public static T GetModel<T>(this ICanGetModel self) where T : class, IModel => self.Scope.Resolve<T>();

        public static T GetSystem<T>(this ICanGetSystem self) where T : class, ISystem =>
            self.Scope.Resolve<T>();

        public static T GetService<T>(this ICanGetService self) where T : class, IService => self.Scope.Resolve<T>();

        public static IUnregister SubscribeEvent<T>(this ICanSubscribeEvent self, System.Action<T> onEvent) =>
            self.Scope.Subscribe(onEvent);

        public static void UnsubscribeEvent<T>(this ICanSubscribeEvent self, System.Action<T> onEvent) =>
            self.Scope.Unsubscribe(onEvent);

        public static void PublishEvent<T>(this ICanPublishEvent self) where T : new() =>
            self.Scope.Publish(new T());

        public static void PublishEvent<T>(this ICanPublishEvent self, T message) => self.Scope.Publish(message);

        // 应用事件显式命名，避免不同游戏之间意外串消息。
        public static IUnregister SubscribeAppEvent<T>(this ICanSubscribeEvent self, System.Action<T> onEvent) =>
            self.Scope.SubscribeArchitectureEvent(onEvent);

        public static void UnsubscribeAppEvent<T>(this ICanSubscribeEvent self, System.Action<T> onEvent) =>
            self.Scope.UnsubscribeArchitectureEvent(onEvent);

        public static void PublishAppEvent<T>(this ICanPublishEvent self, T message) =>
            self.Scope.Architecture.Publish(message);

        public static void SendCommand<T>(this ICanSendCommand self) where T : ICommand, new() =>
            self.Scope.SendCommand(new T());

        public static void SendCommand<T>(this ICanSendCommand self, T command) where T : ICommand =>
            self.Scope.SendCommand(command);

        public static TResult SendCommand<TResult>(this ICanSendCommand self, ICommand<TResult> command) =>
            self.Scope.SendCommand(command);

        public static TResult SendQuery<TResult>(this ICanSendQuery self, IQuery<TResult> query) =>
            self.Scope.SendQuery(query);
    }
}