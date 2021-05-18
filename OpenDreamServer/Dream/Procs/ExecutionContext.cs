using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using OpenDreamServer.Dream.Objects;
using OpenDreamShared.Dream.Procs;

namespace OpenDreamServer.Dream.Procs {
    enum ProcStatus {
        Returned,
        Deferred,
        Called,
    }

    abstract class DreamProc {
        public string Name { get; }

        // This is currently publically settable because the loading code doesn't know what our super is until after we are instantiated
        public DreamProc SuperProc { set; get; }

        public List<String> ArgumentNames { get; }
        public List<DMValueType> ArgumentTypes { get; }

        protected DreamProc(string name, DreamProc superProc, List<String> argumentNames, List<DMValueType> argumentTypes) {
            Name = name;
            SuperProc = superProc;
            ArgumentNames = argumentNames ?? new();
            ArgumentTypes = argumentTypes ?? new();
        }

        public abstract ProcState CreateState(ExecutionContext context, DreamObject src, DreamObject usr, DreamProcArguments arguments);

        // Execute this proc. This will behave as if the proc has `set waitfor = 0`
        public DreamValue Call(DreamObject src, DreamProcArguments arguments, DreamObject usr = null) {
            var context = new ExecutionContext();
            var state = CreateState(context, src, usr, arguments);
            context.PushProcState(state);
            return context.Resume();
        }

        // Execute the given proc and pass its return value to the callback when it has returned.
        // This may call the callback synchronously!!
        public void CallAsync(Action<DreamValue> callback, DreamObject src, DreamProcArguments arguments, DreamObject usr) {
            var context = new ExecutionContext();
            var state = AsyncResultProc.Instance.CreateState(context, callback, this, src, usr, arguments);
            context.PushProcState(state);
            context.Resume();
        }
    }

    class ProcRuntime : Exception {
        public ProcRuntime(string message)
            : base(message)
        {}
    }

    abstract class ProcState {
        public ExecutionContext Context { get; }
        public DreamValue Result { set; get; } = DreamValue.Null;
        
        public ProcState(ExecutionContext context) {
            Context = context;
        }
        
        public ProcStatus Resume() {
            try {
                return InternalResume();
            } catch (Exception exception) {
                Context.HandleException(exception);
                return ProcStatus.Returned;
            }
        }

        public abstract DreamProc Proc { get; }
        protected abstract ProcStatus InternalResume();

        public abstract void AppendStackFrame(StringBuilder builder);

        // Most implementations won't require this, so give it a default
        public virtual void ReturnedInto(DreamValue value) {}
    }

    class ExecutionContext {
        private const int MaxStackDepth = 256;

        private ProcState _current; 
        private Stack<ProcState> _stack = new();

        public DreamValue Resume() {
            if (!Program.IsMainThread) {
                throw new InvalidOperationException();
            }

            while (_current != null) {
                // _current.Resume may mutate our state!!!
                switch (_current.Resume()) {
                    // Our top-most proc just returned a value
                    case ProcStatus.Returned:
                        var returned = _current.Result;

                        // If our stack is empty, the context has finished execution
                        // so we can return the result to our native caller
                        if (!_stack.TryPop(out _current)) {
                            return returned;
                        }

                        // ... otherwise we just push the return value onto the dm caller's stack
                        _current.ReturnedInto(returned);
                        break;

                    // The context is done executing for now
                    case ProcStatus.Deferred:
                        // We return the current return value here even though it may not be the final result
                        return _current.Result;

                    // Our top-most proc just called a function
                    // This means _current has changed!
                    case ProcStatus.Called:
                        // Nothing to do. The loop will call into _current.Resume for us.
                        break;
                }
            }

            throw new InvalidOperationException();
        }

        public void PushProcState(ProcState state) {
            if (_stack.Count >= MaxStackDepth) {
                throw new ProcRuntime("stack depth limit reached");
            }

            if (_current != null) {
                _stack.Push(_current);
            }
            _current = state;
        }

        public void AppendStackTrace(StringBuilder builder) {
            builder.Append("   ");
            _current.AppendStackFrame(builder);
            builder.AppendLine();

            foreach (var frame in _stack) {
                builder.Append("   ");
                frame.AppendStackFrame(builder);
                builder.AppendLine();
            }
        }

        public void HandleException(Exception exception) {
            StringBuilder builder = new();
            builder.AppendLine($"Exception Occured: {exception.Message}");

            builder.AppendLine("=DM StackTrace=");
            AppendStackTrace(builder);
            builder.AppendLine();

            builder.AppendLine("=C# StackTrace=");
            builder.AppendLine(exception.StackTrace);
            builder.AppendLine();

            Console.WriteLine(builder.ToString());
        }
    }
}