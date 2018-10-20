using System;
using System.Collections.Generic;
using System.IO;
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
        // ������
        private TcpClient _client;
 
        // ������� �����
        private NetworkStream _netStream;

        // ������ ������������ ��������
        private List<ClientInfo> _clients = new List<ClientInfo>();

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


        void ClientDoListen(object client)
        {
            SendTextToListBox($"Connection with server {textBoxHostName.Text}:{textBoxPort.Text} opened");

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
                                    // �������� ������ � �����.
                                    NetFile f = msg.File;

                                    // ���������� ��������� ���.
                                    DialogResult saveFile = MessageBox.Show($"You have been sent a file. \"{f.FileName}{f.Extension}\"\nSave it?",
                                        "File from the server", MessageBoxButtons.YesNo);

                                    // ���� �� - ���������
                                    if (saveFile == DialogResult.Yes)
                                        SaveFile(f);
                                    break;
                            } // switch
                        } catch (Exception) {
                            SendTextToListBox($"Connection with server {textBoxHostName.Text}:{textBoxPort.Text} closed");
                            break;
                        } // try-catch
                    } // if
                } // while
            } // using
        } // ClientDoListen


        // �������� ����������� ������ ���������� ����� � ���������
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


        private void ButtonStartClient_Click(object sender, EventArgs e)
        {
            try {
                // ������������� �������
                _client = new TcpClient(textBoxHostName.Text, Convert.ToInt32(textBoxPort.Text));

                ParameterizedThreadStart p1 = ClientDoListen;
                Thread thread = new Thread(p1) {
                    IsBackground = true
                };
                thread.Start(_client);
            } catch (Exception ex) {
                MessageBox.Show(ex.Message, "Can not connect for this server", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } // try-catch
        } // buttonStartClient_Click


        private void ButtonStopClient_Click(object sender, EventArgs e)
        {
            try {
                _client.Close();
            } catch (Exception ex) {
                MessageBox.Show(ex.Message, "Error. Can not disconnect", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } // try-catch
        } // buttonStopClient_Click


        private void ButtonSendMessage_Click(object sender, EventArgs e)
        {
            SendMessageToServer();
            SendMessage(textBoxUsername.Text, textBoxWhom.Text, textBoxMessage.Text);
        } // buttonSendMessage_Click

        private void SendMessage(string from, string to, string message)
        {
            foreach (ClientInfo c in _clients) {
                if (c.Username.Equals(to) || (to.Length == 0 && message.Length == 0)) {
                    // �������� ������� ����� ��� �������� ��������� �������� �������
                    _netStream = c.TcpClient.GetStream();

                    // ���� ����� ����� ������
                    if (_netStream.CanWrite) {
                        // �������������� ����� ��������� � ���
                        ChatMessage msg = new ChatMessage {
                            UserFrom = from,
                            UserTo = to,
                            Message = message,
                            Type = MessageType.Text
                        };

                        // ������������ ���������� � ��������� � ������ ���� ��� �������� �� ����
                        byte[] buf = msg.ToArray();

                        // ������ ���� � ������� �����
                        _netStream.Write(buf, 0, buf.Length);

                        // �������� �������� �� ����
                        _netStream.Flush();

                        // ������������� ��������� ��������� � ����-���� ������� �������� ���
                        SendTextToListBox($"{msg.UserFrom}: {textBoxMessage.Text}");
                        textBoxMessage.Text = "";
                    } else {
                        MessageBox.Show("Error", "Can not write NetworkStream", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    } // if-else
                } // if
            } // foreachy
        } // SendMessage

        private void SendMessageToServer()
        {
            // �������� ������� ����� �������
            _netStream = _client.GetStream();
            // ���� ����� ����� ������
            if (_netStream.CanWrite) {
                // �������������� ����� ��������� � ���
                ChatMessage msg = new ChatMessage {
                    UserFrom = textBoxUsername.Text,
                    UserTo = textBoxWhom.Text,
                    Message = textBoxMessage.Text,
                    Type = MessageType.Text
                };

                // ������������ ���������� � ��������� � ������ ���� ��� �������� �� ����
                byte[] buf = msg.ToArray();

                // ������ ���� � ������� �����
                _netStream.Write(buf, 0, buf.Length);

                // �������� �������� �� ����
                _netStream.Flush();

                // ������������� ��������� ��������� � ����-���� ������� �������� ���
                SendTextToListBox($"{msg.UserFrom}: {textBoxMessage.Text}");
                textBoxMessage.Text = "";
            } else {
                MessageBox.Show("Error", "Can not write NetworkStream", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } // if-else
        } // SendMessageToServer

        private void ButtonSendFile_Click(object sender, EventArgs e)
        {
            // �������� ������� ����� �������
            _netStream = _client.GetStream();

            // ���� ����� ����� ������
            if (_netStream.CanWrite) {
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

                // ������ ����� ���������� �������
                NetFile f = new NetFile() {
                    Data = File.ReadAllBytes(filename),
                    FileName = Path.GetFileNameWithoutExtension(filename),
                    Extension = Path.GetExtension(filename)
                };

                // ���� ����������� ����� �� ��� ��� null - ������ ����
                if (string.IsNullOrEmpty(f.Checksum)) return;

                // �������������� ����� ���������
                ChatMessage msg = new ChatMessage {
                    UserFrom = textBoxUsername.Text,
                    UserTo = textBoxWhom.Text,
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
                SendTextToListBox($"{msg.UserFrom} sent the file \"{Path.GetFileName(filename)}\" to the user.");
                textBoxMessage.Text = "";
            } // if
        } // buttonSendFile_Click


        // �������� ��������� �� ������� Enter
        private void TextBoxMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) {
                ButtonSendMessage_Click(sender, e);
            } // if
        } // textBoxMessage_KeyDown
    } // class MainForm
} // namespace Sockets