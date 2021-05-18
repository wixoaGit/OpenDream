using System;
using System.Buffers;
using System.Collections.Generic;
using OpenDreamServer.Dream.Objects;
using OpenDreamShared.Dream.Procs;

namespace OpenDreamServer.Dream.Procs {
    enum ProcStatus {
        Returned,
        Deferred,
        Called,
    }

    abstract class Proc {
        public string Name { get; }

        // This is currently publically settable because the loading code doesn't know what our super is until after we are instantiated
        public Proc SuperProc { set; get; }

        public List<String> ArgumentNames { get; }
        public List<DMValueType> ArgumentTypes { get; }

        protected Proc(string name, Proc superProc, List<String> argumentNames, List<DMValueType> argumentTypes) {
            Name = name;
            SuperProc = superProc;
            ArgumentNames = argumentNames ?? new();
            ArgumentTypes = argumentTypes ?? new();
        }

        public abstract ProcState CreateState(ExecutionContext context, DreamObject src, DreamObject usr, DreamProcArguments arguments);

        // Execute this proc. This will behave as if the proc has `set waitfor = 0`
        public DreamValue Run(DreamObject src, DreamProcArguments arguments, DreamObject usr = null) {
            var context = new ExecutionContext();
            var state = CreateState(context, src, usr, arguments);
            context.PushProcState(state);
            return context.Resume();
        }
    }

    abstract class ProcState {
        public ExecutionContext Context { get; }
        public DreamValue Result { set; get; } = DreamValue.Null;
        
        public ProcState(ExecutionContext context) {
            Context = context;
        }

        public abstract Proc Proc { get; }
        public abstract ProcStatus Resume();

        // Most implementations won't require this, so give it a default
        public virtual void ReturnedInto(DreamValue value) {}
    }

    class ExecutionContext {
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
            if (_current != null) {
                _stack.Push(_current);
            }
            _current = state;
        }
    }
}