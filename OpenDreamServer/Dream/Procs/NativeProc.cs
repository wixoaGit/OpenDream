using System;
using System.Buffers;
using System.Collections.Generic;
using OpenDreamServer.Dream.Objects;
using OpenDreamShared.Dream.Procs;

namespace OpenDreamServer.Dream.Procs {
    class NativeProc : Proc {
        public delegate DreamValue NativeProcHandler(DreamObject src, DreamObject usr, DreamProcArguments arguments);

        public NativeProcHandler Handler { get; }

        public NativeProc(string name, Proc superProc, List<String> argumentNames, List<DMValueType> argumentTypes, NativeProcHandler handler)
            : base(name, superProc, argumentNames, argumentTypes)
        {
            Handler = handler;
        }

        public override NativeProcState CreateState(ExecutionContext context, DreamObject src, DreamObject usr, DreamProcArguments arguments)
        {
            return new NativeProcState(this, context, src, usr, arguments);
        }
    }

    class NativeProcState : ProcState
    {
        public DreamObject Src;
        public DreamObject Usr;
        public DreamProcArguments Arguments;
        
        private NativeProc _proc;
        public override Proc Proc => _proc;

        public NativeProcState(NativeProc proc, ExecutionContext context, DreamObject src, DreamObject usr, DreamProcArguments arguments)
            : base(context)
        {
            _proc = proc;
            Src = src;
            Usr = usr;
            Arguments = arguments;
        }

        public override ProcStatus Resume()
        {
            Result = _proc.Handler.Invoke(Src, Usr, Arguments);
            return ProcStatus.Returned;
        }

        public override void ReturnedInto(DreamValue value) {
            
        }
    }
}