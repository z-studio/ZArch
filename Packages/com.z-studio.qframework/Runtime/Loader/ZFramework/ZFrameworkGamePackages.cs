using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YooAsset;
using UnityEngine;
using ZFramework.Unity;

namespace QFramework {
    public sealed class GamePackageProgress {
        public string GameId { get; }
        public string Stage { get; }
        public float Progress { get; }
        public long DownloadedBytes { get; }
        public long TotalBytes { get; }

        public GamePackageProgress(string gameId, string stage, float progress, long downloaded = 0, long total = 0) {
            GameId = gameId;
            Stage = stage;
            Progress = progress;
            DownloadedBytes = downloaded;
            TotalBytes = total;
        }
    }

    /// <summary>App 持有的按需资源包准备器。游戏退出归还引用，App 关闭才销毁自己创建的包；磁盘缓存保留。</summary>
    public sealed class ZFrameworkGamePackages {
        private readonly ResourceComponent m_Resource;
        private readonly Func<GameInfo, EPlayMode> m_PlayMode;
        private readonly List<string> m_Owned = new();
        private readonly HashSet<string> m_Ready = new();
        private readonly SemaphoreSlim m_Gate = new(1, 1);
        private readonly CancellationTokenSource m_Lifetime = new();
        private bool m_Closing;

        public GamePackageProgress Progress { get; private set; }

        public ZFrameworkGamePackages(ResourceComponent resource, Func<GameInfo, EPlayMode> playMode = null) {
            m_Resource = resource ? resource : throw new ArgumentNullException(nameof(resource));
            m_PlayMode = playMode ?? (_ => resource.PlayMode);
        }

        /// <summary>等待项目已有资源组件就绪，不创建组件或下载游戏；启动失败可重试。</summary>
        public async Task WaitForReadyAsync(float timeoutSeconds = 60, CancellationToken cancellation = default) {
            if (timeoutSeconds <= 0 || float.IsNaN(timeoutSeconds) || float.IsInfinity(timeoutSeconds)) {
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            }

            if (m_Closing) {
                throw new ObjectDisposedException(nameof(ZFrameworkGamePackages));
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, m_Lifetime.Token);
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;

            while (!m_Resource.IsInitialized) {
                linked.Token.ThrowIfCancellationRequested();

                if (Time.realtimeSinceStartup >= deadline) {
                    throw new TimeoutException(
                        "ZFramework resources did not initialize. Check the foundation configuration before retrying startup.");
                }

                await Task.Yield();
            }

            linked.Token.ThrowIfCancellationRequested();
        }

        public static string PackageName(GameInfo info) =>
            string.IsNullOrWhiteSpace(info.packageName) ? info.id : info.packageName;

        public async Task PrepareAsync(GameInfo info, CancellationToken cancellation = default) {
            if (info == null || string.IsNullOrWhiteSpace(PackageName(info))) {
                throw new ArgumentException("A package name is required.", nameof(info));
            }

            if (m_Closing) {
                throw new ObjectDisposedException(nameof(ZFrameworkGamePackages));
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, m_Lifetime.Token);
            await m_Gate.WaitAsync(linked.Token);
            var name = PackageName(info);
            bool created = false;

            try {
                linked.Token.ThrowIfCancellationRequested();

                if (!m_Resource.IsInitialized) {
                    throw new InvalidOperationException("ZFramework resources are not ready.");
                }

                if (m_Ready.Contains(name) && YooAssets.TryGetPackage(name, out var ready) && ready.PackageValid) {
                    Report(info, "Ready", 1);
                    return;
                }

                if (YooAssets.TryGetPackage(name, out var existing)) {
                    // 外部包由其拥有者更新/下载。本准备器不会修改或销毁它。
                    if (!m_Owned.Contains(name)) {
                        if (existing.InitializeStatus != EOperationStatus.Succeeded || !existing.PackageValid) {
                            throw new InvalidOperationException($"External package '{name}' is not ready.");
                        }

                        Report(info, "Ready", 1);
                        return;
                    }

                    // 上次准备失败且销毁未成功，先完成回滚再创建，不能把半成品当作就绪。
                    await m_Resource.DestroyPackageAsync(name);
                    m_Owned.Remove(name);
                }

                linked.Token.ThrowIfCancellationRequested();
                m_Owned.Add(name);
                created = true;
                var mode = m_PlayMode(info);
                Report(info, "Initializing", 0);
                var initialize = await m_Resource.InitPackage(name, mode, false);
                Ensure(initialize.Status, initialize.Error, name);
                linked.Token.ThrowIfCancellationRequested();
                Report(info, "Checking version", 0.05f);
                var version = m_Resource.RequestPackageVersionAsync(true, 15, name);
                await version;
                Ensure(version.Status, version.Error, name);
                linked.Token.ThrowIfCancellationRequested();
                var manifest = m_Resource.LoadPackageManifestAsync(version.PackageVersion, 15, name);
                await manifest;
                Ensure(manifest.Status, manifest.Error, name);
                linked.Token.ThrowIfCancellationRequested();

                if (mode is EPlayMode.HostPlayMode or EPlayMode.WebPlayMode) {
                    var downloader = m_Resource.CreateResourceDownloader(name);

                    void OnProgress(DownloadProgressChangedEventArgs args) =>
                        Report(info, "Downloading", args.Progress, args.CurrentDownloadBytes, args.TotalDownloadBytes);

                    downloader.DownloadProgressChanged += OnProgress;

                    try {
                        Report(info, "Downloading", 0, 0, downloader.TotalDownloadBytes);
                        downloader.StartDownload();

                        while (!downloader.IsDone) {
                            if (linked.IsCancellationRequested) {
                                downloader.CancelDownload();
                            }

                            await Task.Yield();
                        }

                        linked.Token.ThrowIfCancellationRequested();
                        Ensure(downloader.Status, downloader.Error, name);
                    } finally {
                        downloader.DownloadProgressChanged -= OnProgress;
                    }
                }

                linked.Token.ThrowIfCancellationRequested();
                m_Ready.Add(name);
                Report(info, "Ready", 1);
            } catch (Exception error) {
                if (created) {
                    try {
                        await m_Resource.DestroyPackageAsync(name);
                        m_Owned.Remove(name);
                        m_Ready.Remove(name);
                    } catch (Exception cleanup) {
                        throw new AggregateException(error, cleanup);
                    }
                }

                throw;
            } finally {
                m_Gate.Release();
            }
        }

        private void Report(GameInfo game, string stage, float progress, long downloaded = 0, long total = 0) =>
            Progress = new GamePackageProgress(game.id, stage, progress, downloaded, total);

        private static void Ensure(EOperationStatus status, string error, string name) {
            if (status != EOperationStatus.Succeeded) {
                throw new InvalidOperationException($"Package '{name}': {error}");
            }
        }

        public async Task ShutdownAsync() {
            m_Closing = true;
            m_Lifetime.Cancel();
            await m_Gate.WaitAsync();

            try {
                while (m_Owned.Count > 0) {
                    var name = m_Owned[m_Owned.Count - 1];
                    await m_Resource.DestroyPackageAsync(name);
                    m_Owned.Remove(name);
                    m_Ready.Remove(name);
                }
            } finally {
                m_Gate.Release();
            }
        }
    }
}