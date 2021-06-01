using System;

namespace OpenDreamShared.Net.Packets {
    public class PacketConnectionResult : IPacket {
        public PacketID PacketID => PacketID.ConnectionResult;

        public bool ConnectionSuccessful;
        public String ErrorMessage = String.Empty;
        public String WorldName;

        public PacketConnectionResult() { }

        public PacketConnectionResult(bool connectionSuccessful, String errorMessage, String worldName) {
            ConnectionSuccessful = connectionSuccessful;
            ErrorMessage = errorMessage;
            WorldName = worldName;
        }

        public void ReadFromStream(PacketStream stream) {
            ConnectionSuccessful = stream.ReadBool();
            if (ConnectionSuccessful) {
                WorldName = stream.ReadString();
            } else {
                ErrorMessage = stream.ReadString();
            }
        }

        public void WriteToStream(PacketStream stream) {
            stream.WriteBool(ConnectionSuccessful);
            stream.WriteString(ConnectionSuccessful ? WorldName : ErrorMessage);
        }
    }
}
