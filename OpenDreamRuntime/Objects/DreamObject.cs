﻿using OpenDreamRuntime.Procs;
using OpenDreamShared.Dream;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenDreamRuntime.Objects {

    public class DreamObject {
        public DreamRuntime Runtime { get; }

        public DreamObjectDefinition ObjectDefinition;
        public bool Deleted = false;

        /// <summary>
        /// Any variables that may differ from the default
        /// </summary>
        private Dictionary<string, DreamValue> _variables = new();

        public DreamObject(DreamRuntime runtime, DreamObjectDefinition objectDefinition) {
            Runtime = runtime;
            ObjectDefinition = objectDefinition;
        }

        public void InitInstant(DreamProcArguments creationArguments) {
            ObjectDefinition.InitializionProc?.Spawn(this, new DreamProcArguments(null));

            if (ObjectDefinition.MetaObject != null) {
                ObjectDefinition.MetaObject?.OnObjectCreated(this, creationArguments);

                if (ObjectDefinition.MetaObject.ShouldCallNew) {
                    var newProc = GetProc("New");
                    newProc.Spawn(this, creationArguments);
                }
            }
        }

        public static AsyncNativeProc InitProc(DreamRuntime runtime) {
            return new AsyncNativeProc("DreamObject/(init)", runtime, null, null, null, null, InitAsync);
        }

        private static async Task<DreamValue> InitAsync(AsyncNativeProc.State state) {
            var src = state.Src;

            if (src.ObjectDefinition.InitializionProc != null) {
                await state.Call(src.ObjectDefinition.InitializionProc, src, null, new DreamProcArguments(null));
            }

            if (src.ObjectDefinition.MetaObject != null) {
                src.ObjectDefinition.MetaObject?.OnObjectCreated(src, state.Arguments);

                if (src.ObjectDefinition.MetaObject.ShouldCallNew) {
                    var newProc = src.GetProc("New");
                    await state.Call(newProc, src, state.Usr, state.Arguments);
                }
            }

            return new DreamValue(src);
        }

        public static DreamObject GetFromReferenceID(DreamRuntime runtime, int refID) {
            foreach (KeyValuePair<DreamObject, int> referenceIDPair in runtime.ReferenceIDs) {
                if (referenceIDPair.Value == refID) return referenceIDPair.Key;
            }

            return null;
        }

        public int CreateReferenceID() {
            int referenceID;

            if (!Runtime.ReferenceIDs.TryGetValue(this, out referenceID)) {
                referenceID = Runtime.ReferenceIDs.Count;

                Runtime.ReferenceIDs.Add(this, referenceID);
            }

            return referenceID;
        }

        public void Delete() {
            if (Deleted) return;
            ObjectDefinition.MetaObject?.OnObjectDeleted(this);

            Runtime.ReferenceIDs.Remove(this);
            Deleted = true;
        }

        public void CopyFrom(DreamObject from) {
            ObjectDefinition = from.ObjectDefinition;
            _variables = from._variables;
        }

        public bool IsSubtypeOf(DreamPath path) {
            return ObjectDefinition.IsSubtypeOf(path);
        }

        public bool HasVariable(string name) {
            return ObjectDefinition.HasVariable(name);
        }

        public DreamValue GetVariable(string name) {
            if (TryGetVariable(name, out DreamValue variableValue)) {
                return variableValue;
            } else {
                throw new Exception("Variable " + name + " doesn't exist");
            }
        }

        public List<DreamValue> GetVariableNames() {
            List<DreamValue> list = new(_variables.Count);
            foreach (String key in _variables.Keys) {
                list.Add(new(key));
            }
            return list;
        }

        public bool TryGetVariable(string name, out DreamValue variableValue) {
            if (_variables.TryGetValue(name, out variableValue) || ObjectDefinition.Variables.TryGetValue(name, out variableValue)) {
                if (ObjectDefinition.MetaObject != null) variableValue = ObjectDefinition.MetaObject.OnVariableGet(this, name, variableValue);

                return true;
            }

            return false;
        }

        public void SetVariable(string name, DreamValue value) {
            DreamValue oldValue = _variables.ContainsKey(name) ? _variables[name] : ObjectDefinition.Variables[name];

            _variables[name] = value;
            if (ObjectDefinition.MetaObject != null) ObjectDefinition.MetaObject.OnVariableSet(this, name, value, oldValue);
        }

        public DreamProc GetProc(string procName) {
            return ObjectDefinition.GetProc(procName);
        }

        public bool TryGetProc(string procName, out DreamProc proc) {
            return ObjectDefinition.TryGetProc(procName, out proc);
        }

        public DreamValue SpawnProc(string procName, DreamProcArguments arguments, DreamObject usr = null) {
            var proc = GetProc(procName);
            return DreamThread.Run(proc, this, usr, arguments);
        }

        public DreamValue SpawnProc(string procName) {
            return SpawnProc(procName, new DreamProcArguments(null));
        }

        public override string ToString() {
            return "DreamObject(" + ObjectDefinition.Type + ")";
        }
    }
}
