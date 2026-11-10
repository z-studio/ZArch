using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using QFramework;
using YooAsset;

namespace Demo.App {
    /// <summary>示例 App 的资源准备。正式项目在这里接入自己的版本更新与下载流程。</summary>
    public sealed class SampleGameResources {
        private readonly List<ResourcePackage> m_OwnedPackages = new();

        public async Task InitializeAsync(IEnumerable<GameInfo> games) {
            if (!YooAssets.IsInitialized) {
                YooAssets.Initialize();
            }

            foreach (var game in games) {
                if (YooAssets.TryGetPackage(game.id, out var existing)) {
                    if (existing.InitializeStatus != EOperationStatus.Succeeded) {
                        throw new InvalidOperationException($"Existing package '{game.id}' is not ready.");
                    }

                    continue;
                }

                var package = YooAssets.CreatePackage(game.id);
                m_OwnedPackages.Add(package);
#if UNITY_EDITOR
                var build = EditorSimulateBuildInvoker.Build(game.id, (int)EBundleType.VirtualAssetBundle);
                var options = new EditorSimulateModeOptions {
                    EditorFileSystemParameters =
                        FileSystemParameters.CreateDefaultEditorFileSystemParameters(build.PackageRootDirectory),
                    AutoUnloadBundleWhenUnused = true
                };
#else
                // 随包资源示例；联网项目由 App 换成 HostPlayModeOptions 并处理下载。
                var options = new OfflinePlayModeOptions {
                    BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
                    AutoUnloadBundleWhenUnused = true
                };
#endif
                var initialize = package.InitializePackageAsync(options);

                while (!initialize.IsDone) {
                    await Task.Yield();
                }

                if (initialize.Status != EOperationStatus.Succeeded) {
                    throw new InvalidOperationException(initialize.Error);
                }

                var version = package.RequestPackageVersionAsync();

                while (!version.IsDone) {
                    await Task.Yield();
                }

                if (version.Status != EOperationStatus.Succeeded) {
                    throw new InvalidOperationException(version.Error);
                }

                var manifest =
                    package.LoadPackageManifestAsync(new LoadPackageManifestOptions(version.PackageVersion, 60));

                while (!manifest.IsDone) {
                    await Task.Yield();
                }

                if (manifest.Status != EOperationStatus.Succeeded) {
                    throw new InvalidOperationException(manifest.Error);
                }
            }
        }

        public async Task ShutdownAsync() {
            for (var i = m_OwnedPackages.Count - 1; i >= 0; i--) {
                var package = m_OwnedPackages[i];
                var destroy = package.DestroyPackageAsync();

                while (!destroy.IsDone) {
                    await Task.Yield();
                }

                if (destroy.Status != EOperationStatus.Succeeded) {
                    throw new InvalidOperationException(destroy.Error);
                }

                YooAssets.RemovePackage(package.PackageName);
                m_OwnedPackages.RemoveAt(i);
            }
            // 不调用 YooAssets.Destroy：全局驱动可能仍被 App 中其他资源使用者持有。
        }
    }
}