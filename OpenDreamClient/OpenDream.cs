using OpenDreamClient.Audio;
using OpenDreamClient.Dream;
using OpenDreamClient.Interface;
using OpenDreamClient.Net;
using OpenDreamClient.Resources;
using OpenDreamShared.Dream;
using OpenDreamShared.Net;
using OpenDreamShared.Net.Packets;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;

namespace OpenDreamClient {
    class OpenDream : Application {
        public DreamSoundEngine SoundEngine = null;
        public DreamStateManager StateManager = null;
        public DreamResourceManager ResourceManager = null;
        public DreamInterface Interface = null;
        public ClientConnection Connection = new ClientConnection();
        public ClientData ClientData = new ClientData(setDefaults: true);

        public Map Map;
        public ATOM Eye;
        public List<IconAppearance> IconAppearances { get; private set; } = null;
        public Dictionary<UInt32, ATOM> ATOMs { get; private set; } = null;
        public List<ATOM> ScreenObjects { get; private set; } = null;

        private DispatcherTimer _updateTimer = new DispatcherTimer();

        public OpenDream() {
            _updateTimer.Interval = TimeSpan.FromMilliseconds(1000 / 20);
            _updateTimer.Tick += UpdateTimerTick;

            RegisterPacketCallbacks();
        }

        public void AddATOM(ATOM atom) {
            ATOMs.Add(atom.ID, atom);
        }

        public void RemoveATOM(ATOM atom) {
            atom.Loc = null;
            ATOMs.Remove(atom.ID);
        }

        public void ConnectToServer(string ip, int port, string username) {
            if (Connection.Connected) throw new InvalidOperationException("Already connected to a server!");
            Connection.Connect(ip, port);

            PacketRequestConnect pRequestConnect = new PacketRequestConnect(username, ClientData);
            Connection.SendPacket(pRequestConnect);

            Interface = new DreamInterface();
            SoundEngine = new DreamSoundEngine();
            ResourceManager = new DreamResourceManager();
            StateManager = new DreamStateManager();

            IconAppearances = new List<IconAppearance>();
            ATOMs = new Dictionary<UInt32, ATOM>();
            ScreenObjects = new List<ATOM>();

            MainWindow.Hide();
            _updateTimer.Start();
        }

        public void DisconnectFromServer() {
            if (!Connection.Connected) return;

            _updateTimer.Stop();
            SoundEngine.StopAllChannels();
            Connection.Close();

            Interface = null;
            SoundEngine = null;
            ResourceManager = null;
            StateManager = null;
            ClientData = null;

            Map = null;
            ATOMs = null;
            ScreenObjects = null;
            IconAppearances = null;

            MainWindow.Show();
        }

        private void RegisterPacketCallbacks() {
            Connection.RegisterPacketCallback<PacketConnectionResult>(PacketID.ConnectionResult, HandlePacketConnectionResult);
            Connection.RegisterPacketCallback<PacketInterfaceData>(PacketID.InterfaceData, packet => Interface.HandlePacketInterfaceData(packet));
            Connection.RegisterPacketCallback<PacketOutput>(PacketID.Output, packet => Interface.HandlePacketOutput(packet));
            Connection.RegisterPacketCallback<PacketResource>(PacketID.Resource, packet => ResourceManager.HandlePacketResource(packet));
            Connection.RegisterPacketCallback<PacketFullGameState>(PacketID.FullGameState, packet => StateManager.HandlePacketFullGameState(packet));
            Connection.RegisterPacketCallback<PacketDeltaGameState>(PacketID.DeltaGameState, packet => StateManager.HandlePacketDeltaGameState(packet));
            Connection.RegisterPacketCallback<PacketSound>(PacketID.Sound, packet => SoundEngine.HandlePacketSound(packet));
            Connection.RegisterPacketCallback<PacketBrowse>(PacketID.Browse, packet => Interface.HandlePacketBrowse(packet));
            Connection.RegisterPacketCallback<PacketBrowseResource>(PacketID.BrowseResource, packet => ResourceManager.HandlePacketBrowseResource(packet));
            Connection.RegisterPacketCallback<PacketPrompt>(PacketID.Prompt, packet => Interface.HandlePacketPrompt(packet));
            Connection.RegisterPacketCallback<PacketUpdateAvailableVerbs>(PacketID.UpdateAvailableVerbs, packet => Interface.HandlePacketUpdateAvailableVerbs(packet));
            Connection.RegisterPacketCallback<PacketUpdateStatPanels>(PacketID.UpdateStatPanels, packet => Interface.HandlePacketUpdateStatPanels(packet));
        }

        private void UpdateTimerTick(object sender, EventArgs e) {
            if (Connection.Connected) {
                Connection.ProcessPackets();
            }
        }

        private void HandlePacketConnectionResult(PacketConnectionResult pConnectionResult) {
            if (!pConnectionResult.ConnectionSuccessful) {
                Console.WriteLine("Connection was unsuccessful: " + pConnectionResult.ErrorMessage);
                DisconnectFromServer();
            }
            Interface.SetDefaultWindowTitle(pConnectionResult.WorldName);
        }
    }
}
