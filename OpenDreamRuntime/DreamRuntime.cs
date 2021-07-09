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

            ObjectTree.LoadFromJson(CompiledJson.RootObject);
            ObjectTree.SetMetaObject(DreamPath.Root, new DreamMetaObjectRoot(this));
            ObjectTree.SetMetaObject(DreamPath.List, new DreamMetaObjectList(this));
            ObjectTree.SetMetaObject(DreamPath.Savefile, new DreamMetaObjectSavefile(this));
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
            WorldInstance.InitSpawn(new DreamProcArguments(null));

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
                int tickLength = (int)(100 * WorldInstance.GetVariable("tick_lag").GetValueAsFloat());

                //TODO: sleep_offline = 0
                if (Server.GetConnectedClientCount() == 0 &&
                    WorldInstance.GetVariable("sleep_offline").TryGetValueAsInteger(out int val) && val == 1)
                {
                    Server.Process();
                    Thread.Sleep(tickLength);
                    continue;
                }

                TickStartTime = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds();

                _taskScheduler.Process();
                StateManager.FinalizeCurrentDeltaState();
                Server.Process();

                foreach (DreamConnection connection in Server.Connections) {
                    connection.UpdateStat();
                }

                TickCount++;

                int elapsedTime = (int)(new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds() - TickStartTime);
                int timeToSleep = tickLength - elapsedTime;
                WorldInstance.SetVariable("cpu", new DreamValue((float)elapsedTime / tickLength * 100));
                if (timeToSleep > 0) Thread.Sleep(timeToSleep);
                Console.WriteLine($"Tick: {TickCount}, Connections: {Server.GetConnectedClientCount()}");
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

            return compiledJson;
        }

        private void RegisterPacketCallbacks() {
            Server.RegisterPacketCallback<PacketRequestResource>(PacketID.RequestResource, ResourceManager.HandleRequestResourcePacket);
            Server.RegisterPacketCallback(PacketID.ClickAtom, (DreamConnection connection, PacketClickAtom pClickAtom) => connection.HandlePacketClickAtom(pClickAtom));
            Server.RegisterPacketCallback(PacketID.Topic, (DreamConnection connection, PacketTopic pTopic) => connection.HandlePacketTopic(pTopic));
            Server.RegisterPacketCallback(PacketID.PromptResponse, (DreamConnection connection, PacketPromptResponse pPromptResponse) => connection.HandlePacketPromptResponse(pPromptResponse));
            Server.RegisterPacketCallback(PacketID.CallVerb, (DreamConnection connection, PacketCallVerb pCallVerb) => connection.HandlePacketCallVerb(pCallVerb));
            Server.RegisterPacketCallback(PacketID.SelectStatPanel, (DreamConnection connection, PacketSelectStatPanel pSelectStatPanel) => connection.HandlePacketSelectStatPanel(pSelectStatPanel));
        }

        private void OnDeltaStateFinalized(DreamDeltaState deltaState) {
            foreach (DreamConnection dreamConnection in Server.Connections) {
                dreamConnection.SendPacket(new PacketDeltaGameState(deltaState, dreamConnection.CKey));
            }
        }

        private void OnDreamConnectionRequest(DreamConnection connection) {
            Console.WriteLine("Connection request from '" + connection.CKey + "'");
            StateManager.AddClient(connection.CKey);

            var client = ObjectTree.CreateObject(DreamPath.Client);
            connection.ClientDreamObject = client;

            DreamResource interfaceResource = ResourceManager.LoadResource(CompiledJson.Interface);
            connection.SendPacket(new PacketConnectionResult(true, null, interfaceResource.ReadAsString()));
            connection.SendPacket(new PacketFullGameState(StateManager.FullState, connection.CKey));

            client.InitSpawn(new DreamProcArguments(new() { DreamValue.Null }));
        }

    }
}
