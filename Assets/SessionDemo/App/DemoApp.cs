using QFramework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Demo.App {
    /// <summary>仅注册 App 常驻数据。账号和钱包在每次登录后注册到账号会话。</summary>
    public sealed class DemoApp : Architecture<DemoApp> {
        protected override void Init() {
            RegisterModel(new ServerSettingsModel());
            RegisterUtility(new ZFrameworkServices());
        }
    }

    public sealed class ServerSettingsModel : AbstractModel {
        public ServerSettings settings;

        protected override void OnInit() {
        }
    }

    /// <summary>模拟账号连接；真实项目在这里使用 ZFramework 网络组件，连接状态属于账号。</summary>
    public sealed class AccountSystem : AbstractSystem {
        public bool Connected { get; private set; }

        protected override void OnInit() {
        }

        public async Task ConnectAsync(CancellationToken cancellation) {
            await Task.Delay(20, cancellation);
            cancellation.ThrowIfCancellationRequested();
            Connected = true;
        }

        public Task DisconnectAsync() {
            Connected = false;
            return Task.CompletedTask;
        }

        protected override void OnDeinit() => Connected = false;
    }

    public sealed class AccountModel : AbstractModel, IDisposable {
        public string AccountId { get; private set; }
        public string Token { get; private set; }

        public AccountModel(AccountData account) {
            AccountId = account.id;
            Token = account.token;
        }

        protected override void OnInit() {
        }

        protected override void OnDeinit() => Dispose();

        public void Dispose() {
            AccountId = null;
            Token = null;
        }
    }

    public readonly struct CoinsChanged {
        public int Coins { get; }
        public CoinsChanged(int coins) => Coins = coins;
    }

    public sealed class WalletModel : AbstractModel, IDisposable {
        public readonly BindableProperty<int> coins;

        public WalletModel(int value) {
            coins = new BindableProperty<int>(value);
        }

        public void SetCoins(int value) {
            coins.Value = value;
            // 与 QF 一样直接发送，App、大厅和当前游戏都可用普通 RegisterEvent 监听。
            this.SendEvent(new CoinsChanged(value));
        }

        protected override void OnInit() {
        }

        protected override void OnDeinit() => Dispose();

        public void Dispose() {
            coins.Value = 0;
        }
    }
}