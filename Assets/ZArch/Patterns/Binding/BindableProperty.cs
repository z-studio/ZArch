using System;

namespace ZArch {
    public class BindableProperty<T> : IBindableProperty<T> {
        protected T m_Value;
        private readonly EasyEvent<T> m_OnValueChanged = new();
        private Func<T, T, bool> m_Comparer;

        public static Func<T, T, bool> Comparer { get; set; } = (a, b) => a == null ? b == null : a.Equals(b);

        public BindableProperty(T defaultValue = default) {
            m_Value = defaultValue;
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
                m_OnValueChanged.Trigger(value);
            }
        }

        protected virtual void SetValue(T newValue) => m_Value = newValue;
        protected virtual T GetValue() => m_Value;

        public void SetValueWithoutNotify(T newValue) => SetValue(newValue);

        public IUnregister Register(Action<T> onValueChanged) =>
            m_OnValueChanged.Register(onValueChanged ?? throw new ArgumentNullException(nameof(onValueChanged)));

        public IUnregister RegisterAndInvoke(Action<T> onValueChanged) {
            if (onValueChanged == null) {
                throw new ArgumentNullException(nameof(onValueChanged));
            }

            onValueChanged(GetValue());
            return Register(onValueChanged);
        }

        public void Unregister(Action<T> onValueChanged) => m_OnValueChanged.Unregister(onValueChanged);

        IUnregister IEasyEvent.Register(Action onEvent) {
            if (onEvent == null) {
                throw new ArgumentNullException(nameof(onEvent));
            }

            return Register(_ => onEvent());
        }

        public override string ToString() => Value?.ToString();
    }
}
