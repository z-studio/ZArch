using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ZFramework.Unity;
using Object = UnityEngine.Object;

namespace QFramework {
    /// <summary>只记录本次游戏借用的引用和实例，不另建资源缓存。</summary>
    public sealed class ZFrameworkGameResources : IGameResources {
        private readonly ResourceComponent m_Resource;
        private readonly string m_PackageName;
        private readonly CancellationTokenSource m_Lifetime = new();
        private readonly List<Object> m_Assets = new();
        private readonly List<GameObject> m_Instances = new();
        private int m_Loading;
        private bool m_Closing;
        private bool m_Released;
        private CancellationTokenRegistration m_SessionCancellation;

        public ZFrameworkGameResources(ResourceComponent resource, string packageName) {
            m_Resource = resource ? resource : throw new ArgumentNullException(nameof(resource));
            m_PackageName = string.IsNullOrWhiteSpace(packageName)
                ? throw new ArgumentException("Package is empty.", nameof(packageName)) : packageName;
        }

        public async Task<T> LoadAsync<T>(string address, CancellationToken cancellation = default) where T : Object {
            EnsureOpen();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, m_Lifetime.Token);
            linked.Token.ThrowIfCancellationRequested();
            m_Loading++;

            try {
                var asset = await m_Resource.LoadAssetAsync<T>(address, linked.Token, m_PackageName);

                if (linked.IsCancellationRequested || m_Closing) {
                    if (asset) {
                        m_Resource.UnloadAsset(asset);
                    }

                    throw new OperationCanceledException(linked.Token);
                }

                if (!asset) {
                    throw new InvalidOperationException($"Cannot load '{address}' from '{m_PackageName}'.");
                }

                // 每次成功加载都记录一次，即使底层返回同一个对象，也要配对归还。
                m_Assets.Add(asset);
                return asset;
            } finally {
                m_Loading--;
            }
        }

        public async Task<GameObject> InstantiateAsync(string address, Transform parent = null,
            CancellationToken cancellation = default) {
            EnsureOpen();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, m_Lifetime.Token);
            linked.Token.ThrowIfCancellationRequested();
            m_Loading++;

            try {
                var instance = await m_Resource.LoadGameObjectAsync(address, parent, linked.Token, m_PackageName);

                if (linked.IsCancellationRequested || m_Closing) {
                    if (instance) {
                        Object.Destroy(instance);

                        while (instance) {
                            await Task.Yield();
                        }
                    }

                    throw new OperationCanceledException(linked.Token);
                }

                if (!instance) {
                    throw new InvalidOperationException($"Cannot instantiate '{address}' from '{m_PackageName}'.");
                }

                m_Instances.Add(instance);
                return instance;
            } finally {
                m_Loading--;
            }
        }

        public bool Release(Object asset) {
            var index = m_Assets.FindIndex(value => ReferenceEquals(value, asset));

            if (index < 0) {
                return false;
            }

            m_Resource.UnloadAsset(asset);
            m_Assets.RemoveAt(index);
            return true;
        }

        public bool Destroy(GameObject instance) {
            // 保留记录直到实际销毁完成，退出时还需要等待 OnDestroy 自动归还引用。
            if (!m_Instances.Exists(value => ReferenceEquals(value, instance))) {
                return false;
            }

            if (instance) {
                Object.Destroy(instance);
            }

            return true;
        }

        internal void BindLifetime(CancellationToken cancellation) {
            m_SessionCancellation = cancellation.Register(BeginClose);
        }

        internal void BeginClose() {
            if (m_Closing) {
                return;
            }

            m_Closing = true;
            m_Lifetime.Cancel();
        }

        internal async Task DrainAsync() {
            BeginClose();

            while (m_Loading > 0) {
                await Task.Yield();
            }

            foreach (var instance in m_Instances) {
                if (instance) {
                    Object.Destroy(instance);
                }
            }

            while (m_Instances.Exists(instance => instance)) {
                await Task.Yield();
            }

            m_Instances.Clear();
        }

        internal async Task ReleaseAsync() {
            if (m_Released) {
                return;
            }

            await DrainAsync();

            while (m_Assets.Count > 0) {
                var index = m_Assets.Count - 1;
                m_Resource.UnloadAsset(m_Assets[index]);
                m_Assets.RemoveAt(index);
            }

            m_SessionCancellation.Dispose();
            m_Lifetime.Dispose();
            m_Released = true;
        }

        private void EnsureOpen() {
            if (m_Closing) {
                throw new ObjectDisposedException(nameof(ZFrameworkGameResources));
            }
        }
    }
}