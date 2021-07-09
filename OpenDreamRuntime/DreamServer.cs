using OpenDreamRuntime.Objects;
using OpenDreamShared.Net.Packets;
using System;
using System.Collections.Generic;
using System.Net;

namespace OpenDreamRuntime {
    public delegate void DreamConnectionReadyEventHandler(DreamConnection connection);
    public abstract class DreamServer {
        // Interface
        public abstract void Start(DreamRuntime runtime);
        public abstract void Process();

        // Implementation
        public readonly List<DreamConnection> Connections = new();
        public abstract event DreamConnectionReadyEventHandler DreamConnectionRequest;

        protected Dictionary<PacketID, Action<DreamConnection, IPacket>> _packetIDToCallback = new();

        public IPAddress Address { protected set; get; }
        public int Port { protected set; get; }

        public void RegisterPacketCallback<PacketClass>(PacketID packetID, Action<DreamConnection, PacketClass> packetCallback) where PacketClass : IPacket, new() {
            if (_packetIDToCallback.ContainsKey(packetID)) throw new Exception("Packet ID '" + packetID + "' already has a callback");

            if (packetCallback != null) {
                _packetIDToCallback[packetID] = (DreamConnection connection, IPacket packet) => {
                    packetCallback.Invoke(connection, (PacketClass)packet);
                };
            } else {
                throw new ArgumentNullException("packetCallback");
            }
        }

        public DreamConnection GetConnectionFromCKey(string cKey) {
            foreach (DreamConnection connection in Connections) {
                if (connection.CKey == cKey) return connection;
            }

            return null;
        }

        public DreamConnection GetConnectionFromMob(DreamObject mobObject) {
            foreach (DreamConnection connection in Connections) {
                if (connection.MobDreamObject == mobObject) return connection;
            }

            return null;
        }

        public DreamConnection GetConnectionFromClient(DreamObject clientObject) {
            foreach (DreamConnection connection in Connections) {
                if (connection.ClientDreamObject == clientObject) return connection;
            }

            return null;
        }

        public int GetConnectedClientCount()
        {
            var clients = 0;

            for (var i = 0; i < Connections.Count; i++) {
                if (Connections[i].ClientDreamObject != null) clients++;
            }

            return clients;
        }
    }
}
