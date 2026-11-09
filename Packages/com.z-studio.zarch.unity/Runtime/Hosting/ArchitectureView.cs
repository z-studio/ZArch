using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZArch.Unity {
    public abstract class ArchitectureView : MonoBehaviour, IController, IUnregisterList {
        public ArchitectureEntry Owner { get; private set; }

        public ArchitectureScope Scope =>
            Owner ? Owner.Scope : throw new InvalidOperationException($"{name}: View is not bound to an entry.");

        public bool IsReady => !ReferenceEquals(Owner, null);
        public List<IUnregister> UnregisterList { get; } = new();

        internal void Attach(ArchitectureEntry owner) {
            if (!ReferenceEquals(Owner, null)) {
                if (ReferenceEquals(Owner, owner)) {
                    return;
                }

                throw new InvalidOperationException(
                    $"View '{name}' already belongs to '{Owner.name}'; unbind it before binding to '{owner.name}'.");
            }

            Owner = owner;

            try {
                OnReady();
            } catch (Exception startupError) {
                try {
                    Detach();
                } catch (Exception cleanupError) {
                    throw new AggregateException(startupError, cleanupError);
                }

                throw;
            }
        }

        internal void Detach() {
            if (ReferenceEquals(Owner, null)) {
                return;
            }

            var owner = Owner;

            try {
                OnUnbind();
            } finally {
                try {
                    this.UnregisterAll();
                } finally {
                    Owner = null;
                    owner.ForgetView(this);
                }
            }
        }

        protected T GetModel<T>() where T : class, IModel => Scope.Resolve<T>();
        protected T GetSystem<T>() where T : class, ISystem => Scope.Resolve<T>();
        protected T GetService<T>() where T : class, IService => Scope.Resolve<T>();

        protected void Track(IUnregister subscription) =>
            UnregisterList.Add(subscription ?? throw new ArgumentNullException(nameof(subscription)));

        protected virtual void OnReady() {
        }

        protected virtual void OnUnbind() {
        }

        protected virtual void OnDestroy() => Detach();
    }
}