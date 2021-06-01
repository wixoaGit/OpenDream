using OpenDreamShared.Compiler;
using OpenDreamShared.Compiler.DMF;
using OpenDreamShared.Dream;
using OpenDreamShared.Interface;
using OpenDreamShared.Json;
using OpenDreamShared.Net.Packets;
using OpenDreamRuntime.Objects;
using OpenDreamRuntime.Objects.MetaObjects;
using OpenDreamRuntime.Procs;
using OpenDreamRuntime.Procs.Native;
using OpenDreamRuntime.Resources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenDreamRuntime
{
    public class DreamRuntime
    {
        public readonly Thread MainThread;

        public TaskFactory TaskFactory { get; }
        public TaskScheduler TaskScheduler => _taskScheduler;
        DreamTaskScheduler _taskScheduler;

        public readonly DreamMap Map;
        public readonly DreamObjectTree ObjectTree;
        public readonly DreamStateManager StateManager;
        public readonly DreamServer Server;

        public readonly DreamResourceManager ResourceManager;
        public readonly DreamCompiledJson CompiledJson;

        public readonly DreamObjectDefinition ListDefinition;

        public readonly DreamObject WorldInstance;
        public uint ExceptionCount { get; internal set; }

        public int TickCount = 0;
        public long TickStartTime = 0;

        private readonly InterfaceDescriptor _clientInterface;

        public bool Shutdown;

        // Global state that may not really (really really) belong here
        public Dictionary<ServerIconAppearance, int> AppearanceToID = new();
        public Dictionary<DreamObject, int> ReferenceIDs = new();
        public Dictionary<DreamObject, DreamList> AreaContents = new();
        public Dictionary<DreamObject, UInt32> AtomIDs = new();
        public Dictionary<UInt32, DreamObject> AtomIDToAtom = new();
        public ConcurrentDictionary<DreamObject, ServerIconAppearance> AtomToAppearance = new();
        public UInt32 AtomIDCounter;
        public Dictionary<DreamList, DreamObject> OverlaysListToAtom = new();
        public Dictionary<DreamList, DreamObject> UnderlaysListToAtom = new();
        public List<DreamObject> Mobs = new ();
        public DreamList WorldContentsList;

        public DreamRuntime(DreamServer server, string executablePath) {
            MainThread = Thread.CurrentThread;
            Server = server;

            // Something is doing something fucky with relative dirs, somewhere
            executablePath = Path.GetFullPath(executablePath);

            ResourceManager = new DreamResourceManager(this, Path.GetDirectoryName(executablePath));

            // This initialization isn't great
            ObjectTree = new(this);
            StateManager = new(this);

            _taskScheduler = new();
            TaskFactory = new TaskFactory(_taskScheduler);

            StateManager.DeltaStateFinalized += OnDeltaStateFinalized;
            Server.DreamConnectionRequest += OnDreamConnectionRequest;

            TickStartTime = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds();

            CompiledJson = LoadCompiledJson(executablePath);
            if (CompiledJson == null) {
                throw new InvalidOperationException();
            }

            _clientInterface = LoadInterface(CompiledJson.Interface);
            if (_clientInterface == null) {
                throw new InvalidOperationException();
            }

            ObjectTree.LoadFromJson(CompiledJson.RootObject);
            ObjectTree.SetMetaObject(DreamPath.Root, new DreamMetaObjectRoot(this));
            ObjectTree.SetMetaObject(DreamPath.List, new DreamMetaObjectList(this));
            ObjectTree.SetMetaObject(DreamPath.Sound, new DreamMetaObjectSound(this));
            ObjectTree.SetMetaObject(DreamPath.Image, new DreamMetaObjectImage(this));
            ObjectTree.SetMetaObject(DreamPath.World, new DreamMetaObjectWorld(this));
            ObjectTree.SetMetaObject(DreamPath.Client, new DreamMetaObjectClient(this));
            ObjectTree.SetMetaObject(DreamPath.Datum, new DreamMetaObjectDatum(this));
            ObjectTree.SetMetaObject(DreamPath.Regex, new DreamMetaObjectRegex(this));
            ObjectTree.SetMetaObject(DreamPath.Atom, new DreamMetaObjectAtom(this));
            ObjectTree.SetMetaObject(DreamPath.Area, new DreamMetaObjectArea(this));
            ObjectTree.SetMetaObject(DreamPath.Turf, new DreamMetaObjectTurf(this));
            ObjectTree.SetMetaObject(DreamPath.Movable, new DreamMetaObjectMovable(this));
            ObjectTree.SetMetaObject(DreamPath.Mob, new DreamMetaObjectMob(this));
            DreamProcNative.SetupNativeProcs(ObjectTree);

            ListDefinition = ObjectTree.GetObjectDefinitionFromPath(DreamPath.List);

            WorldInstance = ObjectTree.CreateObject(DreamPath.World);
            ObjectTree.GetObjectDefinitionFromPath(DreamPath.Root).GlobalVariables["world"].Value = new DreamValue(WorldInstance);

            RegisterPacketCallbacks();

            if (CompiledJson.GlobalInitProc != null) {
                var globalInitProc = new DMProc("(global init)", this, null, null, null, CompiledJson.GlobalInitProc.Bytecode, true);
                globalInitProc.Spawn(WorldInstance, new DreamProcArguments(new(), new()));
            }

            Map = new DreamMap(this);
            Map.LoadMap(CompiledJson.Maps[0]);

            WorldInstance.SpawnProc("New");
        }

        public void Run() {
            Server.Start(this);

            while (!Shutdown) {
                TickStartTime = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds();

                _taskScheduler.Process();

                foreach (DreamConnection connection in Server.Connections) {
                    connection.UpdateStat();

                    if (connection.PressedKeys.Contains(38)) {
                        connection.ClientDreamObject?.SpawnProc("North");
                    } else if (connection.PressedKeys.Contains(39)) {
                        connection.ClientDreamObject?.SpawnProc("East");
                    } else if (connection.PressedKeys.Contains(40)) {
                        connection.ClientDreamObject?.SpawnProc("South");
                    } else if (connection.PressedKeys.Contains(37)) {
                        connection.ClientDreamObject?.SpawnProc("West");
                    }
                }

                StateManager.FinalizeCurrentDeltaState();
                Server.Process();

                TickCount++;

                int elapsedTime = (int)(new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds() - TickStartTime);
                int tickLength = (int)(100 * WorldInstance.GetVariable("tick_lag").GetValueAsFloat());
                int timeToSleep = tickLength - elapsedTime;
                WorldInstance.SetVariable("cpu", new DreamValue((float)elapsedTime / tickLength * 100));
                if (timeToSleep > 0) Thread.Sleep(timeToSleep);
            }
        }

        private DreamCompiledJson LoadCompiledJson(string path) {
            DreamResource compiledJsonResource = ResourceManager.LoadResource(path);
            var compiledJson = JsonSerializer.Deserialize<DreamCompiledJson>(compiledJsonResource.ReadAsString());

            if (compiledJson.Maps == null || compiledJson.Maps.Count == 0) {
                Console.WriteLine("The game does not include a map");
                return null;
            } else if (compiledJson.Maps.Count > 1) {
                Console.WriteLine("The game includes more than one map");
                return null;
            }

            if (compiledJson.Interface == null) {
                Console.WriteLine("The game does not include an interface file");
                return null;
            }

            compiledJson.SourceFileName = Path.GetFileNameWithoutExtension(path);

            return compiledJson;
        }

        private InterfaceDescriptor LoadInterface(string path) {
            DreamResource interfaceResource = ResourceManager.LoadResource(path);
            DMFLexer dmfLexer = new DMFLexer(path, interfaceResource.ReadAsString());
            DMFParser dmfParser = new DMFParser(dmfLexer);

            var descriptor = dmfParser.Interface();

            if (dmfParser.Errors.Count > 0) {
                Console.WriteLine("Errors while parsing the interface file");

                foreach (CompilerError error in dmfParser.Errors) {
                    Console.WriteLine(error);
                }

                return null;
            }

            return descriptor;
        }
        private void RegisterPacketCallbacks() {
            Server.RegisterPacketCallback<PacketRequestResource>(PacketID.RequestResource, ResourceManager.HandleRequestResourcePacket);
            Server.RegisterPacketCallback(PacketID.KeyboardInput, (DreamConnection connection, PacketKeyboardInput pKeyboardInput) => connection.HandlePacketKeyboardInput(pKeyboardInput));
            Server.RegisterPacketCallback(PacketID.ClickAtom, (DreamConnection connection, PacketClickAtom pClickAtom) => connection.HandlePacketClickAtom(pClickAtom));
            Server.RegisterPacketCallback(PacketID.Topic, (DreamConnection connection, PacketTopic pTopic) => connection.HandlePacketTopic(pTopic));
            Server.RegisterPacketCallback(PacketID.PromptResponse, (DreamConnection connection, PacketPromptResponse pPromptResponse) => connection.HandlePacketPromptResponse(pPromptResponse));
            Server.RegisterPacketCallback(PacketID.CallVerb, (DreamConnection connection, PacketCallVerb pCallVerb) => connection.HandlePacketCallVerb(pCallVerb));
        }

        private void OnDeltaStateFinalized(DreamDeltaState deltaState) {
            foreach (DreamConnection dreamConnection in Server.Connections) {
                dreamConnection.SendPacket(new PacketDeltaGameState(deltaState, dreamConnection.CKey));
            }
        }

        private void OnDreamConnectionRequest(DreamConnection connection) {
            Console.WriteLine("Connection request from '" + connection.CKey + "'");
            StateManager.AddClient(connection.CKey);

            connection.ClientDreamObject = ObjectTree.CreateObject(DreamPath.Client, new DreamProcArguments(new List<DreamValue>() { DreamValue.Null }));
            connection.SendPacket(new PacketInterfaceData(_clientInterface));
            connection.SendPacket(new PacketFullGameState(StateManager.FullState));

            // Set to <world> part of <world>.json unless we define world.name - final fallback to "OpenDream World"
            String worldName;
            DreamValue worldInstanceName = WorldInstance.GetVariable("name");
            if (worldInstanceName != DreamValue.Null) {
                worldName = worldInstanceName.Stringify();
            } else if (CompiledJson.SourceFileName != null) {
                worldName = CompiledJson.SourceFileName;
            } else {
                worldName = "OpenDream World";
            }

            DreamThread.Run(this, async (state) => {
                var client = connection.ClientDreamObject;
                var newProc = client.GetProc("New");

                var mob = await state.Call(newProc, client, null, new DreamProcArguments(null));

                if (mob.Value != null) {
                    connection.SendPacket(new PacketConnectionResult(true, "", worldName));
                } else {
                    connection.SendPacket(new PacketConnectionResult(false, "The connection was disallowed", null));
                }

                return DreamValue.Null;
            });
        }

    }
}
