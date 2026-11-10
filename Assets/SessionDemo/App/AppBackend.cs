using System.Threading;
using System.Threading.Tasks;
using QFramework;

namespace Demo.App {
    public sealed class ServerSettings {
        public string apiAddress;
        public GameInfo[] games;
    }

    public sealed class AccountData {
        public string id;
        public string token;
        public int coins;
    }

    /// <summary>实际项目在这里调用 ZFramework 的 HTTP/网络能力，解析真实服务器协议。</summary>
    public interface IAppBackend {
        Task<ServerSettings> LoadSettingsAsync(CancellationToken cancellation);
        Task<AccountData> LoginAsync(string account, CancellationToken cancellation);
        Task CloseAccountAsync(AccountModel account);
    }

    /// <summary>明确的演示后端，不连接真实服务器、不保存真实凭证。</summary>
    public sealed class DemoBackend : IAppBackend {
        public async Task<ServerSettings> LoadSettingsAsync(CancellationToken cancellation) {
            await Task.Delay(30, cancellation);
            
            return new ServerSettings {
                apiAddress = "demo://server",
                games = new[] {
                    new GameInfo { id = "game-a", entryAddress = "Entry" },
                    new GameInfo { id = "game-b", entryAddress = "Entry" }
                }
            };
        }

        public async Task<AccountData> LoginAsync(string account, CancellationToken cancellation) {
            await Task.Delay(30, cancellation);
            
            return new AccountData {
                id = account, token = "demo-session-" + account, coins = account == "Alice" ? 100 : 200
            };
        }

        public Task CloseAccountAsync(AccountModel account) => Task.CompletedTask;
    }
}