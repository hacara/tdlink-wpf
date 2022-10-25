using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Net.Sockets;
using System.Security.Policy;
using System.Text;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace tdlink_wpf
{
    public class FileInfomation
    {
        public string? Name { get; set; }
        public string? Path { get; set; }
        public long Size { get; set; }
        public string FullName { get { return Path + '\\' + Name; } }

        public static FileInfomation New(string name, long size)
        {
            return new()
            {
                Name = name,
                Path = ".\\RecvFiles",
                Size = size,
            };
        }

        public static FileInfomation New(string path)
        {
            var fileInfo = new FileInfo(path);
            return new()
            {
                Name = fileInfo.Name,
                Path = fileInfo.Directory!.FullName,
                Size = fileInfo.Length,
            };
        }

        public string GetFormatedSize()
        {
            const long kb = 1024;
            const long mb = 1024 * 1024;
            const long gb = 1024 * 1024 * 1024;
            string size_str;
            if (Size < kb)
                size_str = Size.ToString() + " B";
            else if (Size < mb)
                size_str = string.Format("{0:##.##} KB", (double)Size / kb);
            else if (Size < gb)
                size_str = string.Format("{0:##.##} MB", (double)Size / mb);
            else
                size_str = string.Format("{0:##.##} GB", (double)Size / gb);
            return size_str;
        }
    }

    public class Contact
    { 
        public string Addr { get; set; }
        public string Name { get; set; }

        public ObservableCollection<IMessage> Messages = new();
        
        public List<FileInfomation> SendFiles = new();

        public Contact(string addr, string name)
        {
            Addr = addr;
            Name = name;
        }

        public void SendMessage(string content)
        {
            Messages.Add(new TextMessage
            {
                Type = MessageType.Text,
                Align = HorizontalAlignment.Right,
                Content = content,
            });
            TDLink.Send(TDLink.PacketType.MESSAGE, Encoding.UTF8.GetBytes(content), Addr!);
        }

        public void SendFile(string path)
        {
            var id = SendFiles.Count;
            var fileInfo = FileInfomation.New(path);
            var buf = new List<byte>();
            buf.AddRange(BitConverter.GetBytes(id));
            
            buf.AddRange(BitConverter.GetBytes(fileInfo.Size));
            buf.AddRange(Encoding.UTF8.GetBytes(fileInfo.Name!));

            Messages.Add(FileMessage.New(id, fileInfo, true));

            SendFiles.Add(fileInfo);

            TDLink.Send(TDLink.PacketType.FILE, buf.ToArray(), Addr);
        }

        public async Task<bool> ReciveFile(FileMessage msg, bool overwrite = false)
        {
            var server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            server.Bind(new IPEndPoint(IPAddress.Any, 0));
            server.Listen();
            server.ReceiveTimeout = 5000;

            //构造发送UDP包通知对方
            var packet = new List<byte>();
            packet.AddRange(BitConverter.GetBytes(msg.Id));
            packet.AddRange(Encoding.UTF8.GetBytes(server.LocalEndPoint!.ToString()!));

            TDLink.Send(TDLink.PacketType.ACCEPTFILE, packet.ToArray(), Addr);

            var client = await server.AcceptAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Directory.CreateDirectory(msg.Path!);

            var path = msg.FullName;
            if (!overwrite)
            {
                //如果重名就追加(数字) file(1) file(2)
                var dot = path.LastIndexOf('.');
                var destPath = path;
                var i = 1;
                for (; File.Exists(destPath); i++)
                    destPath = path.Insert(dot, string.Format("({0})", i));
                if (i != 1)
                {
                    path = destPath;
                    var slash = destPath.LastIndexOf('\\');
                    msg.Path = destPath[..slash];
                    msg.Name = destPath[(slash+1)..];
                }
            }
            var file = File.Open(path, FileMode.OpenOrCreate);
            var remainder = msg.Size;
            var buf = new byte[1024];
            while (true)
            {
                var size = client.Receive(buf);
                remainder -= size;
                file.Write(buf, 0, size);
                if (remainder <= 0 || size == 0)
                    break;
            }
            file.Close();
            client.Close();
            server.Close();

            return remainder == 0;
            //if (MessageBox.Show("下载结束\n是否打开所在目录?", "提示", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            //    System.Diagnostics.Process.Start("Explorer.exe", fileInfo.Path);
        }
    }
    static class TDLink
    {
        public enum PacketType
        {
            ONLINE,
            OFFLINE,
            MESSAGE,
            FILE,
            ACCEPTFILE
        }

        public const int TDPort = 19524;

        public static ObservableCollection<Contact> Contacts = new();

        public static string Name = Environment.MachineName;

        private static bool running = true;
        private readonly static UdpClient udp = new(new IPEndPoint(IPAddress.Any, TDPort));

        private readonly static Thread mainThread = new(Run);

        private static void Run()
        {
            SendOnlineMessage("255.255.255.255");
            while (running)
            {
                var raddr = new IPEndPoint(IPAddress.Any, 0);
                var buf = udp.Receive(ref raddr);
                var header = BitConverter.ToUInt16(buf);
                // 4C44是TDLINK协议的识别头
                if (header == 0x4C44)
                {
                    var ip = (raddr as IPEndPoint)!.Address.ToString();
                    var data = buf[3..];
                    switch ((PacketType)buf[2])
                    {
                        case PacketType.ONLINE:
                            Online(data, ip);
                            break;
                        case PacketType.OFFLINE:
                            Offline(ip);
                            break;
                        case PacketType.MESSAGE:
                            Message(data, ip);
                            break;
                        case PacketType.FILE:
                            File(data, ip);
                            break;
                        case PacketType.ACCEPTFILE:
                            AcceptFile(data, ip);
                            break;
                        default:
                            break;
                    }
                }
            }
            udp.Close();
        }
        public static void Start()
        {
            mainThread.Start();
        }

        public static void Exit()
        {
            running = false;
            BoardcastOffline();
        }

        public static void SendOnlineMessage(string ip)
        {
            Send(PacketType.ONLINE, Encoding.UTF8.GetBytes(Name), ip);
        }

        public static void BoardcastOffline()
        {
            Send(PacketType.OFFLINE, null, "255.255.255.255");
        }

        public static void Send(PacketType type, byte[]? data, string ip)
        {
            var buf = new List<byte> { 0x44, 0x4C, (byte)type };
            if (data != null)
                buf.AddRange(data);
            udp.Send(buf.ToArray(), ip, TDPort);
        }

        private static void Online(byte[] data, string ip)
        {
            var name = data.Length > 0 ? Encoding.UTF8.GetString(data) : ip;
            //Contacts[ip] = new Contact { Name = name };

            Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                foreach (var contact in Contacts)
                {
                    if (contact.Addr == ip)
                    {
                        contact.Name = name;
                        return;
                    }
                }
                Contacts.Add(new Contact(ip, name));
                SendOnlineMessage(ip);
            }));
            

        }

        private static void Offline(string ip)
        {
            for (int i = 0; i < Contacts.Count; i++)
            {
                if (Contacts[i].Addr == ip)
                {
                    Application.Current.Dispatcher.Invoke(new Action(() => Contacts.RemoveAt(i)));
                    break;
                }
            }
        }

        private static void Message(byte[] data, string ip)
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                foreach (var contact in Contacts)
                {
                    if (contact.Addr == ip)
                    {
                        contact.Messages.Add(new TextMessage
                        {
                            Type = MessageType.Text,
                            Content = Encoding.UTF8.GetString(data),
                            Align = HorizontalAlignment.Left
                        });
                        break;
                    }
                }
            }));
        }
        private static void File(byte[] data, string ip)
        {
            var id = BitConverter.ToInt32(data);
            var size = BitConverter.ToInt64(data, 4);
            var name = Encoding.UTF8.GetString(data[12..]);
            var fileInfo = FileInfomation.New(name, size);

            foreach (var contact in Contacts)
            {
                if (contact.Addr == ip)
                {
                    Application.Current.Dispatcher.Invoke(new Action(() =>
                        contact.Messages.Add(FileMessage.New(id, fileInfo, false))
                    ));

                    return;
                }
            }
        }

        private static async void AcceptFile(byte[] data, string ip)
        {
            int id = BitConverter.ToInt32(data);
            string addr = Encoding.UTF8.GetString(data[4..]);
            foreach (var contact in Contacts)
            {
                if (contact.Addr == ip)
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    var endPoint = IPEndPoint.Parse(addr);
                    endPoint.Address = IPAddress.Parse("127.0.0.1");
                    socket.ConnectAsync(endPoint).Wait(5000);
                    await socket.SendFileAsync(contact.SendFiles[id].FullName);
                    return;
                }
            }
        }
    }
}
