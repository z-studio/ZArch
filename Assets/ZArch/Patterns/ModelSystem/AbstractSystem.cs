using System;
using System.Collections.Generic;

namespace ZArch {
    public abstract class AbstractSystem : ISystem, IUnregisterList {
        private ArchitectureScope m_Scope;

        public List<IUnregister> UnregisterList { get; } = new();
        public ArchitectureScope GetScope() => m_Scope;
        public void SetScope(ArchitectureScope scope) => m_Scope = scope;
        public void Initialize() => OnInit();

        public void Deinitialize() {
            DeinitializeSafely(OnDeinit);
        }

        private void DeinitializeSafely(Action onDeinit) {
            Exception unregisterException = null;

            try {
                this.UnregisterAll();
            } catch (Exception exception) {
                unregisterException = exception;
            }

            try {
                onDeinit();
            } catch (Exception exception) {
                if (unregisterException != null) {
                    throw new AggregateException(unregisterException, exception);
                }

                throw;
            }

            if (unregisterException != null) {
                throw unregisterException;
            }
        }

        protected abstract void OnInit();
        protected virtual void OnDeinit() { }
    }
}
