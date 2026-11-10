using System;
using System.Threading;
using System.Threading.Tasks;
using QFramework;
using UnityEngine;
using ZFramework.Unity;

namespace Demo.App {
    /// <summary>App 的资源准备与回收细节。业务流程只调用 PrepareAsync / ShutdownAsync。</summary>
    public sealed class AppResources {
        private readonly ResourceComponent m_Resource;
        private readonly float m_TimeoutSeconds;

        public AppResources(ResourceComponent resource, float timeoutSeconds) {
            m_Resource = resource;
            m_TimeoutSeconds = timeoutSeconds;
            Packages = new ZFrameworkGamePackages(resource);
        }
        public ZFrameworkGamePackages Packages { get; }
        public Task PrepareAsync(CancellationToken cancellation) => WaitForFoundationAsync(cancellation);
        private async Task WaitForFoundationAsync(CancellationToken cancellation) {
            if (!m_Resource) throw new InvalidOperationException("Assign the App ZFramework ResourceComponent.");
            var deadline = Time.realtimeSinceStartup + m_TimeoutSeconds;
            while (!m_Resource.IsInitialized) {
                cancellation.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartup >= deadline)
                    throw new TimeoutException("ZFramework resources did not initialize. Check the foundation configuration before retrying startup.");
                await Task.Yield();
            }
        }
        public Task ShutdownAsync() => Packages.ShutdownAsync();
    }
}
