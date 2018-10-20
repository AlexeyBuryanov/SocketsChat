using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DataLib;

namespace Sockets
{
    public partial class MainForm : Form
    {
        // ������� �����-��������� ��������
        private Thread _listenerThread;

        // "���������" �� �� ������
        private TcpListener _listener;

        // ������
        private TcpClient _client;

        // ������ ������������ ��������
        private List<ClientInfo> _clients = new List<ClientInfo>();

        // ������� �����
        private NetworkStream _netStream;

        // ����� ��� �������� ����������
        private byte[] _readBuffer = new byte[1024];

        // ������� ��� ������ ������� ���������� ������ � ������� ������
        private delegate DialogResult ShowSaveFileDialogInvoker();

        // ������� ��� �������� ��������� � ����-���� � ������, �������� ����������� ����������.
        // � ������ ������ ��� � ��� listBox. ��������� � ����-����� �� �� ���� ������ � ������� �� ��� ������
        // �������� ������� ������ !!!
        delegate void SendTextToListBoxCallback(string text);
        private void SendTextToListBox(string text)
        {
            // �������� �� ��� ������� � ����� ������?
            if (listBoxMain.InvokeRequired) {
                SendTextToListBoxCallback d = new SendTextToListBoxCallback(SendTextToListBox);
                listBoxMain.Invoke(d, new object[] { text });
            } else {
                listBoxMain.Items.Add(text);
            } // if-else
        } // SendTextToListBox

        // ��������� ��������������� ������ � �������
        private delegate void UserConnectedCallback(string username, string ip, DateTime connectionTime);
        private void UserConnected(string username, string ip, DateTime connectionTime)
        {
            // �������� �� ��� ������� � ����� ������?
            if (listViewConnectedClients.InvokeRequired) {
                UserConnectedCallback d = new UserConnectedCallback(UserConnected);
                listViewConnectedClients.Invoke(d, new object[] { username, ip, connectionTime });
            } else {
                // ��������� � ListView ��������������� �������
                ListViewItem item = new ListViewItem($"{username}");
                item.SubItems.Add(ip);
                item.SubItems.Add(connectionTime.ToString());
                listViewConnectedClients.Items.Add(item);
            } // if-else
        } // UserConnected


        public MainForm()
        {
            InitializeComponent();
        } // MainForm


        // �������� ����� �� �������
        private byte[] GetResponse(NetworkStream stream)
        {
            byte[] data = new byte[1024];
            using (var memoryStream = new MemoryStream()) {
                while (!ValidateBinary(memoryStream)) {
                    stream.Read(data, 0, data.Length);
                    memoryStream.Write(data, 0, data.Length);
                } // while

                return memoryStream.ToArray();
            } // using
        } // GetResponse

        // ��������� ��������������� �������
        private bool ValidateBinary(MemoryStream memoryStream)
        {
            try {
                ChatMessage.FromArray(memoryStream.ToArray());
                return true;
            } catch (Exception) {
                return false;
            } // try-catch
        } // ValidateBinary
        

        private void ServerDoListen(object client)
        {
            // �������� ����� ��� �������� ����������
            using (NetworkStream netStream = (client as TcpClient)?.GetStream()) {
                while (true) {
                    if (netStream.CanRead) {
                        byte[] response;

                        try {
                            // �������� ����� �� �������
                            response = GetResponse(netStream);

                            // ������������� ���������
                            ChatMessage msg = ChatMessage.FromArray(response);

                            // ���������� ��� ���������
                            switch (msg.Type) {
                                // ���� ��� ��������� ����� - ������� ��� � ���� ����
                                case MessageType.Text:
                                    SendTextToListBox($"{msg.UserFrom}: {msg.Message}");
                                    break;

                                // ���� ��� ��������� ���� - ���������� ��� ���������
                                case MessageType.File:
                                    // �������� ������ � �����
                                    NetFile f = msg.File;

                                    // ���������� ��������� ���
                                    DialogResult saveFile = MessageBox.Show($"The client sent a file.\n\"{f.FileName}{f.Extension}\"\nSave it?",
                                        "File from the client", MessageBoxButtons.YesNo);

                                    // ���� �� - ���������
                                    if (saveFile == DialogResult.Yes)
                                        SaveFile(f);
                                    break;
                            } // switch
                        } catch (Exception ex) {
                            MessageBox.Show($"The server will be stopped\n{ex.Message}", 
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            listViewConnectedClients.Items.Clear();
                            _listener.Stop();
                            break;
                        } // try-catch
                    } // if
                } // while
            } // using
        } // ServerDoListen


        // �������� ����������� ������ ���������� ����� � ��������� ����
        private void SaveFile(NetFile f)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog() {
                InitialDirectory = @"d:\",
                Filter = $"(*{f.Extension})|*{f.Extension}|All files (*.*)|*.*",
                FileName = $"{f.FileName}",
                ValidateNames = true
            };

            ShowSaveFileDialogInvoker invoker = saveFileDialog.ShowDialog;

            if (Invoke(invoker).Equals(DialogResult.Cancel)) return;

            string filename = saveFileDialog.FileName;

            File.WriteAllBytes(filename, f.Data);
            MessageBox.Show("File successfully saved!", "Save", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } // SaveFile


        private void ButtonStartServer_Click(object sender, EventArgs e)
        {
            try {
                // �������� � ����� ��������� ��� ����������� �������� � �������
                _listener = new TcpListener(IPAddress.Parse(textBoxHostName.Text), Convert.ToInt32(textBoxPort.Text));
                _listener.Start();

                SendTextToListBox("Server started. Waiting for clients...");

                _listenerThread = new Thread(WaitForClients) {
                    IsBackground = true
                };
                _listenerThread.Start();
            } catch (Exception ex) {
                MessageBox.Show(ex.Message, "Can not start the server", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } // try-catch
        } // buttonStartServer_Click

        private void WaitForClients()
        {
            try {
                while (true) {
                    // ������������� ����� � �������� ������ �������
                    _client = _listener.AcceptTcpClient();

                    ClientInfo clientInfo = new ClientInfo {
                        TcpClient = _client,
                        Username = "",
                        ConnectionTime = DateTime.Now,
                        Ip = IPAddress.Parse(((IPEndPoint)_client.Client.RemoteEndPoint).Address.ToString()).ToString()
                    };

                    // �������� � ������ �������� ������ ������������� �������
                    _clients.Add(clientInfo);

                    SendTextToListBox($"Connection with client opened");

                    // ��������� � ListView ��������������� �������
                    UserConnected(clientInfo.Username, clientInfo.Ip, clientInfo.ConnectionTime);

                    // ����� ������ ��� ��������� ��������� �� �������
                    ParameterizedThreadStart p1 = ServerDoListen;
                    Thread thread = new Thread(p1) {
                        IsBackground = true
                    };
                    thread.Start(_client);
                } // while
            } catch (Exception ex) {
                MessageBox.Show($"The request to connect clients is not possible.\n{ex.Message}", 
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } // try-catch
        } // WaitForClients


        private void ButtonStopServer_Click(object sender, EventArgs e)
        {
            try {
                _listener.Stop();

                SendTextToListBox($"Server stopped.");
            } catch (Exception ex) {
                MessageBox.Show(ex.Message, "Can not stop the server", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } // try-catch
        } // ButtonStopServer_Click


        private void ButtonSendMessage_Click(object sender, EventArgs e)
        {
            string msg = textBoxMessage.Text;
            foreach (ClientInfo client in _clients) {
                SendMessage(client.TcpClient, msg);
            } // foreach
              // ������������� ��������� ��������� � ����-���� ������� �������� ���
            SendTextToListBox($"Admin: {textBoxMessage.Text}");
            textBoxMessage.Text = "";
        } // buttonSendMessage_Click

        // ���������� ��������� ����������� �������
        private void SendMessage(TcpClient client, string message)
        {
            // �������� ������� �����
            _netStream = client.GetStream();

            // ���� ����� ����� ������
            if (_netStream.CanWrite) {
                // �������������� ����� ��������� � ���
                ChatMessage msg = new ChatMessage {
                    UserFrom = "Admin",
                    Message = message,
                    Type = MessageType.Text
                };

                // ������������ ���������� � ��������� � ������ ���� ��� �������� �� ����
                byte[] buf = msg.ToArray();

                // ������ ���� � ������� �����
                _netStream.Write(buf, 0, buf.Length);

                // �������� �������� �� ����
                _netStream.Flush();
            } else {
                MessageBox.Show("Error", "Can not write NetworkStream", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } // if-else
        } // SendMessage


        private void ButtonSendFile_Click(object sender, EventArgs e)
        {
            // ��������� ����������� ������ 
            OpenFileDialog openFileDialog = new OpenFileDialog() {
                InitialDirectory = @"d:\",
                Filter = "All files (*.*)|*.*",
                Multiselect = false,
                ValidateNames = true
            };
            if (openFileDialog.ShowDialog() != DialogResult.OK) return;

            // �������� ���� � �����
            string filename = openFileDialog.FileName;

            foreach (ClientInfo client in _clients) {
                SendFile(client.TcpClient, filename);
            } // foreach
        } // buttonSendFile_Click

        // ���������� ���� ����������� �������
        private void SendFile(TcpClient client, string filename)
        {
            // �������� ������� ����� �������
            _netStream = client.GetStream();

            // ���� ����� ����� ������
            if (_netStream.CanWrite) {
                // �������� ���������� �� ������������ �����
                NetFile f = new NetFile() {
                    Data = File.ReadAllBytes(filename),
                    FileName = Path.GetFileNameWithoutExtension(filename),
                    Extension = Path.GetExtension(filename)
                };

                // ���� ����������� ����� �� ��� ��� null - ������ ����
                if (string.IsNullOrEmpty(f.Checksum)) return;

                // �������������� ����� ���������
                ChatMessage msg = new ChatMessage {
                    UserFrom = "Admin",
                    File = f,
                    Type = MessageType.File
                };

                // ������������ ���������� � ��������� � ������ ���� ��� �������� �� ����
                byte[] buf = msg.ToArray();

                // ���������� ����� � ������� �����
                _netStream.Write(buf, 0, buf.Length);

                // ������� �������� �� ����
                _netStream.Flush();

                // ���������� � ���� ���� ���� ��� ��� ���� ��� ���������
                SendTextToListBox($"Admin sent the file \"{Path.GetFileName(filename)}\"");
                textBoxMessage.Text = "";
            } // if
        } // SendFile


        // �������� ��������� �� ������� Enter
        private void TextBoxMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) {
                ButtonSendMessage_Click(sender, e);
            } // if
        } // textBoxMessage_KeyDown


        // ����� �� ������������ ���� NotifyIcon
        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        } // ExitToolStripMenuItem_Click

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (Left == -32000 && Top == -32000) {
                notifyIcon.Visible = true;
                Visible = false;
                notifyIcon.ShowBalloonTip(2000);
            } // if
        } // MainForm_Resize

        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            Visible = true;
            notifyIcon.Visible = false;
            WindowState = FormWindowState.Normal;
        } // notifyIcon_DoubleClick
    } // class MainForm
} // namespace Sockets