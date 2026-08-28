using System;

namespace ZArch {
    public class BindableProperty<T> : IBindableProperty<T> {
        protected T mValue;
        private readonly Signal<T> m_OnValueChanged = new();
        private Func<T, T, bool> m_Comparer;

        public static Func<T, T, bool> Comparer { get; set; } = (a, b) => a == null ? b == null : a.Equals(b);

        public BindableProperty(T defaultValue = default) {
            mValue = defaultValue;
            m_Comparer = Comparer;
        }

        public BindableProperty<T> WithComparer(Func<T, T, bool> comparer) {
            m_Comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
            return this;
        }

        public T Value {
            get => GetValue();
            set {
                var currentValue = GetValue();

                if (value == null && currentValue == null) {
                    return;
                }

                if (value != null && m_Comparer(value, currentValue)) {
                    return;
                }

                SetValue(value);
                m_OnValueChanged.Emit(value);
            }
        }

        protected virtual void SetValue(T newValue) => mValue = newValue;
        protected virtual T GetValue() => mValue;

        public void SetValueWithoutNotify(T newValue) => SetValue(newValue);

        public IUnregister Subscribe(Action<T> onValueChanged) =>
            m_OnValueChanged.Subscribe(onValueChanged ?? throw new ArgumentNullException(nameof(onValueChanged)));

        public IUnregister SubscribeAndInvoke(Action<T> onValueChanged) {
            if (onValueChanged == null) {
                throw new ArgumentNullException(nameof(onValueChanged));
            }

            onValueChanged(GetValue());
            return Subscribe(onValueChanged);
        }

        public void Unsubscribe(Action<T> onValueChanged) => m_OnValueChanged.Unsubscribe(onValueChanged);

        IUnregister ISignal.Subscribe(Action onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            return Subscribe(_ => onEvent());
        }

        public override string ToString() => Value?.ToString();
    }
}
