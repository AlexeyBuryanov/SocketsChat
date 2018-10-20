using System;
using System.Net.Sockets;

namespace DataLib
{
    public class ClientInfo
    {
        public string Username { get; set; }
        public DateTime ConnectionTime { get; set; }
        public TcpClient TcpClient { get; set; }
        public string Ip { get; set; }
    } // ClientInfo
}
