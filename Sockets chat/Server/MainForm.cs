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
        // Главный поток-слушатель клиентов
        private Thread _listenerThread;

        // "Слушатель" он же сервер
        private TcpListener _listener;

        // Клиент
        private TcpClient _client;

        // Список подключенных клиентов
        private List<ClientInfo> _clients = new List<ClientInfo>();

        // Сетевой поток
        private NetworkStream _netStream;

        // Буфер для передачи информации
        private byte[] _readBuffer = new byte[1024];

        // Делегат для вызова диалога сохранения файлов в базовом потоке
        private delegate DialogResult ShowSaveFileDialogInvoker();

        // Делегат для отправки сообщений в лист-бокс в потоке, которому принадлежит дескриптор.
        // В данном случае это у нас listBox. Обращение к лист-боксу не из того потока в котором он был создан
        // вызывает неявную ошибку !!!
        delegate void SendTextToListBoxCallback(string text);
        private void SendTextToListBox(string text)
        {
            // Запущена ли эта функция в чужом потоке?
            if (listBoxMain.InvokeRequired) {
                SendTextToListBoxCallback d = new SendTextToListBoxCallback(SendTextToListBox);
                listBoxMain.Invoke(d, new object[] { text });
            } else {
                listBoxMain.Items.Add(text);
            } // if-else
        } // SendTextToListBox

        // Добавляет подключившегося юзверя в листвью
        private delegate void UserConnectedCallback(string username, string ip, DateTime connectionTime);
        private void UserConnected(string username, string ip, DateTime connectionTime)
        {
            // Запущена ли эта функция в чужом потоке?
            if (listViewConnectedClients.InvokeRequired) {
                UserConnectedCallback d = new UserConnectedCallback(UserConnected);
                listViewConnectedClients.Invoke(d, new object[] { username, ip, connectionTime });
            } else {
                // Добавляем в ListView подключившегося клиента
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


        // Получает ответ от клиента
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

        // Проверяет сериализуемость объекта
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
            // Получить поток для передачи информации
            using (NetworkStream netStream = (client as TcpClient)?.GetStream()) {
                while (true) {
                    if (netStream.CanRead) {
                        byte[] response;

                        try {
                            // Получаем ответ от клиента
                            response = GetResponse(netStream);

                            // Десериализуем сообщение
                            ChatMessage msg = ChatMessage.FromArray(response);

                            // Определяем тип сообщения
                            switch (msg.Type) {
                                // Если был отправлен текст - выводим его в окно чата
                                case MessageType.Text:
                                    SendTextToListBox($"{msg.UserFrom}: {msg.Message}");
                                    break;

                                // Если был отправлен файл - предлагаем его сохранить
                                case MessageType.File:
                                    // Получаем данные о файле
                                    NetFile f = msg.File;

                                    // Предлагаем сохранить его
                                    DialogResult saveFile = MessageBox.Show($"The client sent a file.\n\"{f.FileName}{f.Extension}\"\nSave it?",
                                        "File from the client", MessageBoxButtons.YesNo);

                                    // Если да - сохраняем
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


        // Вызывает стандартный диалог сохранения файла и сохраняет файл
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
                // Создание и старт слушателя для подключения клиентов к серверу
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
                    // Прослушивание порта и ожидание нового клиента
                    _client = _listener.AcceptTcpClient();

                    ClientInfo clientInfo = new ClientInfo {
                        TcpClient = _client,
                        Username = "",
                        ConnectionTime = DateTime.Now,
                        Ip = IPAddress.Parse(((IPEndPoint)_client.Client.RemoteEndPoint).Address.ToString()).ToString()
                    };

                    // Добавить в список клиентов нового подключенного клиента
                    _clients.Add(clientInfo);

                    SendTextToListBox($"Connection with client opened");

                    // Добавляем в ListView подключившегося клиента
                    UserConnected(clientInfo.Username, clientInfo.Ip, clientInfo.ConnectionTime);

                    // Старт потока для получения сообщений от клиента
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
              // Дополнительно добавляем сообщение в лист-бокс сервера эмитируя чат
            SendTextToListBox($"Admin: {textBoxMessage.Text}");
            textBoxMessage.Text = "";
        } // buttonSendMessage_Click

        // Отправляет сообщение конкретному клиенту
        private void SendMessage(TcpClient client, string message)
        {
            // Получаем сетевой поток
            _netStream = client.GetStream();

            // Если поток может писать
            if (_netStream.CanWrite) {
                // Инициализируем новое сообщение в чат
                ChatMessage msg = new ChatMessage {
                    UserFrom = "Admin",
                    Message = message,
                    Type = MessageType.Text
                };

                // Конвертируем информацию о сообщении в массив байт для отправки по сети
                byte[] buf = msg.ToArray();

                // Запись байт в сетевой поток
                _netStream.Write(buf, 0, buf.Length);

                // Ожидание отправки по сети
                _netStream.Flush();
            } else {
                MessageBox.Show("Error", "Can not write NetworkStream", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } // if-else
        } // SendMessage


        private void ButtonSendFile_Click(object sender, EventArgs e)
        {
            // Формируем стандартный диалог 
            OpenFileDialog openFileDialog = new OpenFileDialog() {
                InitialDirectory = @"d:\",
                Filter = "All files (*.*)|*.*",
                Multiselect = false,
                ValidateNames = true
            };
            if (openFileDialog.ShowDialog() != DialogResult.OK) return;

            // Получаем путь к файлу
            string filename = openFileDialog.FileName;

            foreach (ClientInfo client in _clients) {
                SendFile(client.TcpClient, filename);
            } // foreach
        } // buttonSendFile_Click

        // Отправляет файл конкретному клиенту
        private void SendFile(TcpClient client, string filename)
        {
            // Получаем сетевой поток клиента
            _netStream = client.GetStream();

            // Если поток может писать
            if (_netStream.CanWrite) {
                // Получаем информацию об отправляемом файле
                NetFile f = new NetFile() {
                    Data = File.ReadAllBytes(filename),
                    FileName = Path.GetFileNameWithoutExtension(filename),
                    Extension = Path.GetExtension(filename)
                };

                // Если контрольная сумма не дай бог null - уносим ноги
                if (string.IsNullOrEmpty(f.Checksum)) return;

                // Инициализируем новое сообщение
                ChatMessage msg = new ChatMessage {
                    UserFrom = "Admin",
                    File = f,
                    Type = MessageType.File
                };

                // Конвертируем информацию о сообщении в массив байт для отправки по сети
                byte[] buf = msg.ToArray();

                // Записываем байты в сетевой поток
                _netStream.Write(buf, 0, buf.Length);

                // Ожидаем отправки по сети
                _netStream.Flush();

                // Отображаем в чате факт того что наш файл был отправлен
                SendTextToListBox($"Admin sent the file \"{Path.GetFileName(filename)}\"");
                textBoxMessage.Text = "";
            } // if
        } // SendFile


        // Отправка сообщения по клавише Enter
        private void TextBoxMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) {
                ButtonSendMessage_Click(sender, e);
            } // if
        } // textBoxMessage_KeyDown


        // Выход по контекстному меню NotifyIcon
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