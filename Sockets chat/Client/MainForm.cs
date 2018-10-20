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
        // Клиент
        private TcpClient _client;
 
        // Сетевой поток
        private NetworkStream _netStream;

        // Список подключенных клиентов
        private List<ClientInfo> _clients = new List<ClientInfo>();

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


        void ClientDoListen(object client)
        {
            SendTextToListBox($"Connection with server {textBoxHostName.Text}:{textBoxPort.Text} opened");

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
                                    // Получаем данные о файле.
                                    NetFile f = msg.File;

                                    // Предлагаем сохранить его.
                                    DialogResult saveFile = MessageBox.Show($"You have been sent a file. \"{f.FileName}{f.Extension}\"\nSave it?",
                                        "File from the server", MessageBoxButtons.YesNo);

                                    // Если да - сохраняем
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


        // Вызывает стандартный диалог сохранения файла и сохраняет
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
                // Инициализация клиента
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
                    // Получить сетевой поток для отправки сообщения текущему клиенту
                    _netStream = c.TcpClient.GetStream();

                    // Если поток может писать
                    if (_netStream.CanWrite) {
                        // Инициализируем новое сообщение в чат
                        ChatMessage msg = new ChatMessage {
                            UserFrom = from,
                            UserTo = to,
                            Message = message,
                            Type = MessageType.Text
                        };

                        // Конвертируем информацию о сообщении в массив байт для отправки по сети
                        byte[] buf = msg.ToArray();

                        // Запись байт в сетевой поток
                        _netStream.Write(buf, 0, buf.Length);

                        // Ожидание отправки по сети
                        _netStream.Flush();

                        // Дополнительно добавляем сообщение в лист-бокс сервера эмитируя чат
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
            // Получаем сетевой поток клиента
            _netStream = _client.GetStream();
            // Если поток может писать
            if (_netStream.CanWrite) {
                // Инициализируем новое сообщение в чат
                ChatMessage msg = new ChatMessage {
                    UserFrom = textBoxUsername.Text,
                    UserTo = textBoxWhom.Text,
                    Message = textBoxMessage.Text,
                    Type = MessageType.Text
                };

                // Конвертируем информацию о сообщении в массив байт для отправки по сети
                byte[] buf = msg.ToArray();

                // Запись байт в сетевой поток
                _netStream.Write(buf, 0, buf.Length);

                // Ожидание отправки по сети
                _netStream.Flush();

                // Дополнительно добавляем сообщение в лист-бокс сервера эмитируя чат
                SendTextToListBox($"{msg.UserFrom}: {textBoxMessage.Text}");
                textBoxMessage.Text = "";
            } else {
                MessageBox.Show("Error", "Can not write NetworkStream", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } // if-else
        } // SendMessageToServer

        private void ButtonSendFile_Click(object sender, EventArgs e)
        {
            // Получаем сетевой поток клиента
            _netStream = _client.GetStream();

            // Если поток может писать
            if (_netStream.CanWrite) {
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

                // Читаем байты выбранного файлика
                NetFile f = new NetFile() {
                    Data = File.ReadAllBytes(filename),
                    FileName = Path.GetFileNameWithoutExtension(filename),
                    Extension = Path.GetExtension(filename)
                };

                // Если контрольная сумма не дай бог null - уносим ноги
                if (string.IsNullOrEmpty(f.Checksum)) return;

                // Инициализируем новое сообщение
                ChatMessage msg = new ChatMessage {
                    UserFrom = textBoxUsername.Text,
                    UserTo = textBoxWhom.Text,
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
                SendTextToListBox($"{msg.UserFrom} sent the file \"{Path.GetFileName(filename)}\" to the user.");
                textBoxMessage.Text = "";
            } // if
        } // buttonSendFile_Click


        // Отправка сообщения по клавише Enter
        private void TextBoxMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) {
                ButtonSendMessage_Click(sender, e);
            } // if
        } // textBoxMessage_KeyDown
    } // class MainForm
} // namespace Sockets