using System;

namespace ZArch {
    public interface IReadOnlyBindableProperty<T> : ISignal {
        T Value { get; }
        IUnregister SubscribeAndInvoke(Action<T> action);
        void Unsubscribe(Action<T> onValueChanged);
        IUnregister Subscribe(Action<T> onValueChanged);
    }

    public interface IBindableProperty<T> : IReadOnlyBindableProperty<T> {
        new T Value { get; set; }
        void SetValueWithoutNotify(T newValue);
    }
}
