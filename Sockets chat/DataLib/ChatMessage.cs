using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace DataLib
{
    [Serializable]
    public enum MessageType
    {
        Text,
        File
    } // MessageType

    [Serializable]
    public class ChatMessage
    {
        public string Message { get; set; }
        public string UserFrom { get; set; }
        public string UserTo { get; set; }
        public NetFile File { get; set; }
        public MessageType Type { get; set; }

        public byte[] ToArray()
        {
            BinaryFormatter formatter = new BinaryFormatter();
            using (MemoryStream stream = new MemoryStream()) {
                formatter.Serialize(stream, this);
                return stream.ToArray();
            } // using
        } // ToArray

        public static ChatMessage FromArray(byte[] data)
        {
            BinaryFormatter formatter = new BinaryFormatter();
            using (MemoryStream stream = new MemoryStream(data)) {
                return formatter.Deserialize(stream) as ChatMessage;
            } // using
        } // FromArray
    } // ChatMessage
}
