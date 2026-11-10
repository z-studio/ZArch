using System;
using System.Threading;
using System.Threading.Tasks;
using YooAsset;

namespace QFramework {
    /// <summary>App 先准备 ResourcePackage。本适配器只持有并释放入口资源句柄。</summary>
    public sealed class YooAssetGameDefinitionLoader : IGameDefinitionLoader {
        private readonly Func<GameInfo, ResourcePackage> m_GetPackage;

        // 默认按游戏 ID 查找同名资源包，也可以注入项目自己的包映射。
        public YooAssetGameDefinitionLoader() : this(info => YooAssets.GetPackage(info.id)) {
        }

        public YooAssetGameDefinitionLoader(Func<GameInfo, ResourcePackage> getPackage) {
            m_GetPackage = getPackage ?? throw new ArgumentNullException(nameof(getPackage));
        }

        public async Task<GameDefinitionHandle> LoadAsync(GameInfo info, CancellationToken cancellation) {
            if (info == null) {
                throw new ArgumentNullException(nameof(info));
            }

            cancellation.ThrowIfCancellationRequested();
            var package = m_GetPackage(info)
                          ?? throw new InvalidOperationException($"Game '{info.id}' has no resource package.");

            if (package.InitializeStatus != EOperationStatus.Succeeded) {
                throw new InvalidOperationException($"Resource package '{package.PackageName}' is not initialized.");
            }

            var handle = package.LoadAssetAsync<GameDefinition>(info.entryAddress);

            try {
                // 不使用 Task 属性，避免依赖 WebGL 不支持的句柄 Task 接口。
                // 等原加载完成后再处理取消，确保失败/取消路径也有对应 Release。
                while (!handle.IsDone) {
                    await Task.Yield();
                }

                cancellation.ThrowIfCancellationRequested();

                if (handle.Status != EOperationStatus.Succeeded) {
                    throw new InvalidOperationException(
                        $"Cannot load '{info.entryAddress}' from '{package.PackageName}': {handle.Error}");
                }

                return new GameDefinitionHandle(handle.GetAssetObject<GameDefinition>(), handle.Release);
            } catch {
                handle.Release();
                throw;
            }
        }
    }
}