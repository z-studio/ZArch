using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using QFramework;
using ZFramework.Unity;
using YooAsset;

namespace Demo.App {
    /// <summary>示例 App 使用 ZFramework 准备资源包；注销不销毁 App 基础服务。</summary>
    public sealed class SampleGameResources {
        private readonly List<string> m_OwnedPackages = new();

        public async Task InitializeAsync(ResourceComponent resource, IEnumerable<GameInfo> games,
            CancellationToken cancellation) {
            foreach (var game in games) {
                cancellation.ThrowIfCancellationRequested();

                if (m_OwnedPackages.Contains(game.id)) {
                    continue;
                }

                // 项目若已准备这些包，应复用既有包；不要在这里重复初始化。
                if (YooAssets.TryGetPackage(game.id, out var existing)) {
                    if (existing.InitializeStatus == EOperationStatus.Succeeded && existing.PackageValid) {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Existing package '{game.id}' is not ready; its owner must prepare it first.");
                }

                m_OwnedPackages.Add(game.id);
                var operation = await resource.InitPackage(game.id, resource.PlayMode, true);
                cancellation.ThrowIfCancellationRequested();

                if (operation == null || operation.Status != EOperationStatus.Succeeded
                                      || !resource.GetPackage(game.id).PackageValid) {
                    throw new InvalidOperationException($"Cannot prepare package '{game.id}'.");
                }
            }
        }

        public async Task ShutdownAsync(ResourceComponent resource) {
            if (!resource || !resource.IsInitialized) {
                return;
            }

            while (m_OwnedPackages.Count > 0) {
                var index = m_OwnedPackages.Count - 1;
                await resource.DestroyPackageAsync(m_OwnedPackages[index]);
                m_OwnedPackages.RemoveAt(index);
            }
        }
    }
}