using System;

namespace ZArch {
    public interface ICommand : IBelongToScope,
                                ICanSetScope,
                                ICanGetSystem,
                                ICanGetModel,
                                ICanGetUtility,
                                ICanSendEvent,
                                ICanSendCommand,
                                ICanSendQuery {
        void Execute();
    }

    public interface ICommand<TResult> : IBelongToScope,
                                         ICanSetScope,
                                         ICanGetSystem,
                                         ICanGetModel,
                                         ICanGetUtility,
                                         ICanSendEvent,
                                         ICanSendCommand,
                                         ICanSendQuery {
        TResult Execute();
    }

    public abstract class AbstractCommand : ICommand {
        private ArchitectureScope m_Scope;

        public ArchitectureScope GetScope() => m_Scope;
        public void SetScope(ArchitectureScope scope) => m_Scope = scope;
        void ICommand.Execute() => OnExecute();
        protected abstract void OnExecute();
    }

    public abstract class AbstractCommand<TResult> : ICommand<TResult> {
        private ArchitectureScope m_Scope;

        public ArchitectureScope GetScope() => m_Scope;
        public void SetScope(ArchitectureScope scope) => m_Scope = scope;
        TResult ICommand<TResult>.Execute() => OnExecute();
        protected abstract TResult OnExecute();
    }

    public interface IQuery<TResult> : IBelongToScope,
                                       ICanSetScope,
                                       ICanGetModel,
                                       ICanGetSystem,
                                       ICanSendQuery {
        TResult Execute();
    }

    public abstract class AbstractQuery<TResult> : IQuery<TResult> {
        private ArchitectureScope m_Scope;

        public ArchitectureScope GetScope() => m_Scope;
        public void SetScope(ArchitectureScope scope) => m_Scope = scope;
        public TResult Execute() => OnExecute();
        protected abstract TResult OnExecute();
    }

    public static class ScopeOperationExtension {
        public static void SendCommand<T>(this ArchitectureScope scope, T command) where T : ICommand {
            if (scope == null) {
                throw new ArgumentNullException(nameof(scope));
            }

            if (command == null) {
                throw new ArgumentNullException(nameof(command));
            }

            command.SetScope(scope);
            command.Execute();
        }

        public static TResult SendCommand<TResult>(
            this ArchitectureScope scope,
            ICommand<TResult> command
        ) {
            if (scope == null) {
                throw new ArgumentNullException(nameof(scope));
            }

            if (command == null) {
                throw new ArgumentNullException(nameof(command));
            }

            command.SetScope(scope);
            return command.Execute();
        }

        public static TResult SendQuery<TResult>(this ArchitectureScope scope, IQuery<TResult> query) {
            if (scope == null) {
                throw new ArgumentNullException(nameof(scope));
            }

            if (query == null) {
                throw new ArgumentNullException(nameof(query));
            }

            query.SetScope(scope);
            return query.Execute();
        }
    }
}