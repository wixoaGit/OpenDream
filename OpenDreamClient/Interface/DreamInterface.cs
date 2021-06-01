using OpenDreamClient.Interface.Elements;
using OpenDreamClient.Interface.Prompts;
using OpenDreamShared.Dream.Procs;
using OpenDreamShared.Interface;
using OpenDreamShared.Net.Packets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace OpenDreamClient.Interface {
    class DreamInterface {
        public Dictionary<string, ElementWindow> Windows = new();
        public Dictionary<string, Window> PopupWindows = new();
        public InterfaceDescriptor InterfaceDescriptor { get; private set; } = null;

        public ElementWindow DefaultWindow;
        public ElementOutput DefaultOutput;
        public ElementInfo DefaultInfo;

        private Window _defaultWindow;

        public void LoadInterfaceDescriptor(InterfaceDescriptor interfaceDescriptor) {
            InterfaceDescriptor = interfaceDescriptor;

            foreach (WindowDescriptor windowDescriptor in InterfaceDescriptor.WindowDescriptors) {
                ElementWindow window = InterfaceHelpers.CreateWindowFromDescriptor(windowDescriptor);

                Windows.Add(windowDescriptor.Name, window);
                if (window.ElementDescriptor.IsDefault) {
                    DefaultWindow = window;
                }

                foreach (IElement element in window.ChildElements) {
                    if (element.ElementDescriptor.IsDefault) {
                        if (element is ElementOutput elementOutput) DefaultOutput = elementOutput;
                        else if (element is ElementInfo elementInfo) DefaultInfo = elementInfo;
                    }
                }
            }
        }

        public IElement FindElementWithName(string name) {
            string[] split = name.Split(".");

            if (split.Length == 2) {
                string windowName = split[0];
                string elementName = split[1];
                ElementWindow window = null;

                if (Windows.ContainsKey(windowName)) {
                    window = Windows[windowName];
                } else if (PopupWindows.ContainsKey(windowName)) {
                    window = (ElementWindow)PopupWindows[windowName].Content;
                }

                if (window != null) {
                    foreach (IElement element in window.ChildElements) {
                        if (element.ElementDescriptor.Name == elementName) return element;
                    }
                }
            } else {
                string elementName = split[0];

                foreach (ElementWindow window in Windows.Values) {
                    foreach (IElement element in window.ChildElements) {
                        if (element.ElementDescriptor.Name == elementName) return element;
                    }
                }
            }

            return null;
        }

        public Window CreateWindow(ElementWindow windowElement) {
            Window window = new Window();

            window.Width = windowElement.ElementDescriptor.Size.Value.Width;
            window.Height = windowElement.ElementDescriptor.Size.Value.Height;
            window.Content = windowElement;

            windowElement.UpdateVisuals();
            return window;
        }

        public Window CreatePopupWindow(string windowName, System.Drawing.Size windowSize) {
            WindowDescriptor popupWindowDescriptor = new WindowDescriptor(windowName, new() {
                new ElementDescriptorMain("main") {
                    Size = windowSize
                },
                new ElementDescriptorBrowser("browser") {
                    Size = windowSize,
                    Anchor1 = new System.Drawing.Point(0, 0),
                    Anchor2 = new System.Drawing.Point(100, 100)
                }
            });

            Window popup = CreateWindow(InterfaceHelpers.CreateWindowFromDescriptor(popupWindowDescriptor));
            popup.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            popup.Owner = _defaultWindow;
            popup.Width = windowSize.Width;
            popup.Height = windowSize.Height;
            popup.Closed += (object sender, EventArgs e) => {
                PopupWindows.Remove(windowName);
            };

            PopupWindows.Add(windowName, popup);
            return popup;
        }

        public void HandlePacketInterfaceData(PacketInterfaceData pInterfaceData) {
            LoadInterfaceDescriptor(pInterfaceData.InterfaceDescriptor);

            _defaultWindow = CreateDefaultWindow();
        }

        public void HandlePacketOutput(PacketOutput pOutput) {
            IElement interfaceElement;
            string data = null;

            if (pOutput.Control != null) {
                string[] split = pOutput.Control.Split(":");

                interfaceElement = FindElementWithName(split[0]);
                if (split.Length > 1) data = split[1];
            } else {
                interfaceElement = DefaultOutput;
            }

            if (interfaceElement != null) interfaceElement.Output(pOutput.Value, data);
        }

        public void HandlePacketBrowse(PacketBrowse pBrowse) {
            if (pBrowse.HtmlSource == null && pBrowse.Window != null) { //Closing a popup
                if (PopupWindows.TryGetValue(pBrowse.Window, out Window popup)) {
                    popup.Owner = null; //Without this, the main window ends up minimized
                    popup.Close();
                }
            } else if (pBrowse.HtmlSource != null) { //Outputting to a browser
                string htmlFileName;
                ElementBrowser outputBrowser;

                if (pBrowse.Window != null) {
                    htmlFileName = pBrowse.Window;
                    outputBrowser = FindElementWithName(pBrowse.Window) as ElementBrowser;

                    if (outputBrowser == null) {
                        Window popup;

                        if (!PopupWindows.TryGetValue(pBrowse.Window, out popup)) popup = CreatePopupWindow(pBrowse.Window, pBrowse.Size);

                        ElementWindow windowElement = (ElementWindow)popup.Content;
                        outputBrowser = (ElementBrowser)windowElement.ChildElements[0];
                        popup.Show();
                        popup.Focus();
                    }
                } else {
                    //TODO: Find embedded browser panel
                    htmlFileName = null;
                    outputBrowser = null;
                }

                FileInfo cacheFile = Program.OpenDream.ResourceManager.CreateCacheFile(htmlFileName + ".html", pBrowse.HtmlSource);
                if (outputBrowser != null) outputBrowser.SetFileSource(cacheFile.FullName);
            }
        }

        public void HandlePacketPrompt(PacketPrompt pPrompt) {
            PromptWindow prompt = null;
            int promptTypeBitflag = (int)pPrompt.Types;

            if ((promptTypeBitflag & (int)DMValueType.Text) != 0) {
                prompt = new TextPrompt(pPrompt.PromptId, pPrompt.Title, pPrompt.Message, pPrompt.DefaultValue);
            } else if ((promptTypeBitflag & (int)DMValueType.Num) != 0) {
                prompt = new NumberPrompt(pPrompt.PromptId, pPrompt.Title, pPrompt.Message, pPrompt.DefaultValue);
            } else if ((promptTypeBitflag & (int)DMValueType.Message) != 0) {
                prompt = new MessagePrompt(pPrompt.PromptId, pPrompt.Title, pPrompt.Message, pPrompt.DefaultValue);
            }

            if (prompt != null) {
                prompt.Owner = _defaultWindow;
                prompt.Show();
            }
        }

        public void HandlePacketUpdateAvailableVerbs(PacketUpdateAvailableVerbs pUpdateAvailableVerbs) {
            if (DefaultInfo != null) DefaultInfo.UpdateVerbs(pUpdateAvailableVerbs);
        }

        public void HandlePacketUpdateStatPanels(PacketUpdateStatPanels pUpdateStatPanels) {
            if (DefaultInfo != null) DefaultInfo.UpdateStatPanels(pUpdateStatPanels);
        }

        private Window CreateDefaultWindow() {
            Window defaultWindow = CreateWindow(DefaultWindow);

            defaultWindow.Title = "OpenDream World";
            defaultWindow.Closed += OnDefaultWindowClosed;
            defaultWindow.KeyDown += OnDefaultWindowKeyDown;
            defaultWindow.KeyUp += OnDefaultWindowKeyUp;
            defaultWindow.Show();

            return defaultWindow;
        }

        public void SetDefaultWindowTitle(String title) {
            _defaultWindow.Title = title;
        }

        private void OnDefaultWindowClosed(object sender, EventArgs e) {
            _defaultWindow = null;

            Program.OpenDream.DisconnectFromServer();
        }

        private void OnDefaultWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
            int keyCode = InterfaceHelpers.KeyToKeyCode(e.Key);

            if (keyCode != -1) {
                e.Handled = true;

                Program.OpenDream.Connection.SendPacket(new PacketKeyboardInput(new int[1] { keyCode }, new int[0] { }));
            }
        }

        private void OnDefaultWindowKeyUp(object sender, System.Windows.Input.KeyEventArgs e) {
            int keyCode = InterfaceHelpers.KeyToKeyCode(e.Key);

            if (keyCode != -1) {
                e.Handled = true;

                Program.OpenDream.Connection.SendPacket(new PacketKeyboardInput(new int[0] { }, new int[1] { keyCode }));
            }
        }
    }
}
