using QFramework;
using System;

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

    public sealed class WalletModel : AbstractModel, IDisposable {
        public readonly BindableProperty<int> coins;

        public WalletModel(int value) {
            coins = new BindableProperty<int>(value);
        }

        protected override void OnInit() {
        }

        protected override void OnDeinit() => Dispose();

        public void Dispose() {
            coins.Value = 0;
        }
    }
}