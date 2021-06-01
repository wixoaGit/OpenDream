using OpenDreamShared.Net;
using OpenDreamShared.Net.Packets;
using OpenDreamRuntime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace OpenDreamServer {
    class Connection : DreamConnection
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _tcpStream;
        private readonly BinaryReader _tcpStreamBinaryReader;
        private readonly BinaryWriter _tcpStreamBinaryWriter;

        public Connection(DreamRuntime runtime, TcpClient tcpClient)
            : base(runtime)
        {
            _tcpClient = tcpClient;
            _tcpStream = _tcpClient.GetStream();
            _tcpStreamBinaryReader = new BinaryReader(_tcpStream);
            _tcpStreamBinaryWriter = new BinaryWriter(_tcpStream);
            Address = tcpClient.Client.RemoteEndPoint != null ? ((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address : IPAddress.Any;
        }

        public override byte[] ReadPacketData() {
            if (_tcpClient.Connected && _tcpStream.DataAvailable) {
                UInt32 packetDataLength = _tcpStreamBinaryReader.ReadUInt32();
                byte[] packetData = new byte[packetDataLength];

                int bytesRead = _tcpStream.Read(packetData, 0, (int)packetDataLength);
                while (bytesRead < packetDataLength) {
                    bytesRead += _tcpStream.Read(packetData, bytesRead, (int)packetDataLength - bytesRead);
                }

                return packetData;
            }
            return null;
        }

        public override void SendPacket(IPacket packet)
        {
            PacketStream stream = new PacketStream();

            stream.WriteByte((byte)packet.PacketID);
            packet.WriteToStream(stream);

            _tcpStreamBinaryWriter.Write((UInt32)stream.Length);
            _tcpStream.Write(stream.ToArray());
        }
    }

    class Server : DreamServer
    {
        public DreamRuntime Runtime { private set; get; }

        private readonly TcpListener _tcpListener;
        private Dictionary<string, DreamConnection> _ckeyToConnection = new Dictionary<string, DreamConnection>();

        public Server(String addr, int port) {
            if (!IPAddress.TryParse(addr, out IPAddress ipAddress)) {
                Console.Error.WriteLine("Error while parsing address " + ipAddress + ", falling back to localhost.");
                ipAddress = IPAddress.Any;
            }
            _tcpListener = new TcpListener(ipAddress, port);

            IPEndPoint endpoint = (IPEndPoint)_tcpListener.LocalEndpoint;
            Address = endpoint.Address;
            Port = endpoint.Port;

            RegisterPacketCallback<PacketRequestConnect>(PacketID.RequestConnect, OnPacketRequestConnect);
        }

        public override event DreamConnectionReadyEventHandler DreamConnectionRequest;

        public override void Start(DreamRuntime runtime)
        {
            Runtime = runtime;
            _tcpListener.Start();
        }

        public override void Process()
        {
            ProcessConnections();
            ProcessPackets();
        }

        private void ProcessConnections() {
            while (_tcpListener.Pending()) {
                TcpClient tcpClient = _tcpListener.AcceptTcpClient();
                DreamConnection dreamConnection = new Connection(Runtime, tcpClient);

                Connections.Add(dreamConnection);
            }
        }

        private void ProcessPackets() {
            foreach (DreamConnection dreamConnection in Connections) {
                try {
                    byte[] packetData;

                    while ((packetData = dreamConnection.ReadPacketData()) != null) {
                        IPacket packet = IPacket.CreatePacketFromData(packetData);

                        try {
                            _packetIDToCallback[packet.PacketID]?.Invoke(dreamConnection, packet);
                        } catch (Exception e) {
                            Console.Error.WriteLine("Error while handling received packet (" + packet.PacketID + "): " + e.Message);
                        }
                    }
                } catch (Exception e) {
                    Console.Error.WriteLine("Error while processing received packet from user '" + dreamConnection.CKey + "': " + e.Message);
                }
            }
        }

        private void OnPacketRequestConnect(DreamConnection connection, PacketRequestConnect pRequestConnect) {
            if (!_ckeyToConnection.ContainsKey(pRequestConnect.CKey)) {
                connection.CKey = pRequestConnect.CKey;
                connection.ClientData = pRequestConnect.ClientData;

                DreamConnectionRequest.Invoke(connection);
            } else {
                connection.SendPacket(new PacketConnectionResult(false, "A connection with your ckey already exists", null));
            }
        }
    }
}
