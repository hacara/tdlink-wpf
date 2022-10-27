using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml.Linq;

namespace tdlink_wpf
{
    public enum MessageType
    {
        Text,
        File,
        Image,
        Media,
        //TODO
        //Stream,
    }
    public interface IMessage
    {
        public MessageType Type { get; set; }
        public string? Time { get; }
    }

    public class TextMessage : IMessage
    {
        public MessageType Type { get; set; }

        private readonly string time = DateTime.Now.ToLongTimeString();
        public string? Time { get { return time; } }
        public HorizontalAlignment Align { get; set; }
        public string? Content { get; set; }
    }

    public class FileMessage : FileInfomation, IMessage
    {
        public MessageType Type { get; set; }
        public string? Time { get; set; }
        public int Id { get; set; }
        public string? FormatedSize { get; set; }
        public HorizontalAlignment Alignment { get; set; }
        public static FileMessage New(int id, FileInfomation fileInfo, bool isSend)
        {
            var name = fileInfo.Name!;
            var extension = name[(name.LastIndexOf('.')+1)..];
            var type = extension switch
            {
                "bmp" or "jpg" or "png" or "webp" => MessageType.Image,
                "asf" or "wav" or "mp3" or "m4a" or "m3u" or "aac" or
                "gif" or "mp4" or "avi" or "mov"=> MessageType.Media,
                _ => MessageType.File,
            };
            return new()
            {
                Name = name,
                Size = fileInfo.Size,
                Path = fileInfo.Path,
                IsDirectory = fileInfo.IsDirectory,

                Type = type,
                Time = DateTime.Now.ToLongTimeString(),
                Id = id,
                FormatedSize = fileInfo.GetFormatedSize(),
                Alignment = isSend ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };
        }
    }
    internal partial class MessageSelector : DataTemplateSelector
    {
        public DataTemplate? TextTemplate { get; set; }
        public DataTemplate? FileTemplate { get; set; }
        public DataTemplate? ImageTemplate { get; set; }
        public DataTemplate? MediaTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item != null)
            {
                var msg = (IMessage)item;
                return msg.Type switch
                {
                    MessageType.Text => TextTemplate!,
                    MessageType.File => FileTemplate!,
                    MessageType.Image => ImageTemplate!,
                    MessageType.Media => MediaTemplate!,
                    _ => null!
                };
            }
            return null!;
        }
    }
}
