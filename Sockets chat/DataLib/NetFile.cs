using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DataLib
{
    [Serializable]
    public class NetFile
    {
        private byte[] _data;
        public string FileName { get; set; }
        public string Extension { get; set; }
        public string Checksum { get; set; }


        public NetFile() {}
        public NetFile(byte[] data)
        {
            NetFile file = FromArray(data);
            FileName = file.FileName;
            Extension = Path.GetExtension(file.FileName);
            Data = file.Data;
        } // NetFile


        public byte[] Data {
            get {
                return _data;
            } // get
            set {
                _data = value;
                Checksum = GetMD5Hash(_data);
            } // set
        } // Data


        public static string GetMD5Hash(byte[] source)
        {
            StringBuilder hash = new StringBuilder();
            using (MD5 md5Hasher = MD5.Create()) {
                byte[] data = md5Hasher.ComputeHash(source);
                for (int index = 0; index < data.Length; index++) {
                    hash.Append(data[index].ToString("x2"));
                } // for

                return hash.ToString();
            } // using
        } // GetMD5Hash


        public byte[] ToArray()
        {
            BinaryFormatter formatter = new BinaryFormatter();
            using (MemoryStream stream = new MemoryStream()) {
                formatter.Serialize(stream, this);
                return stream.ToArray();
            } // using
        } // ToArray


        public static NetFile FromArray(byte[] data)
        {
            BinaryFormatter formatter = new BinaryFormatter();
            using (MemoryStream stream = new MemoryStream(data)) {
                stream.Position = 0;
                return formatter.Deserialize(stream) as NetFile;
            } // using
        } // FromArray
    } // class NetFile
} // NetFile