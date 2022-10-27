using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace tdlink_wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ContactsList.ItemsSource = TDLink.Contacts;
            Title = TDLink.Name;
            TDLink.Start();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            TDLink.Exit();
            Environment.Exit(0);
        }

        private void ContactsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var contact = (Contact)ContactsList.SelectedItem;
            if (contact != null)
            {
                MessageList.ItemsSource = contact.Messages;
                ContactColumn.Width = GridLength.Auto;
                MessageColumn.Width = new (1, GridUnitType.Star);
                MessageColumn.MinWidth = 256;
            }
            else
            {
                MessageColumn.Width = new GridLength(0);
                MessageColumn.MinWidth = 0;
                ContactColumn.Width = new(1, GridUnitType.Star);
            }
        }
        private void MessageInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var textBox = (TextBox)sender;
                if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
                {
                    int caretIndex = textBox.CaretIndex;
                    textBox.Text = textBox.Text.Insert(caretIndex, Environment.NewLine);
                    textBox.CaretIndex = caretIndex + Environment.NewLine.Length;
                }
                else
                    SendButton_Click(null!, null!);
            }
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageInputBox.Text != ""
                && ContactsList.SelectedItem is Contact contact)
            {
                contact.SendMessage(MessageInputBox.Text);
                MessageInputBox.Text = "";
            }
        }

        private void FileButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContactsList.SelectedItem is Contact contact)
            {
                var dialog = new OpenFileDialog();
                if ((bool)dialog.ShowDialog()!)
                    contact.SendFile(dialog.FileName);
            }
        }

        private void Message_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.Text) ||
                e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
                e.Handled = false;
            }
        }
        private void Message_Drop(object sender, DragEventArgs e)
        {
            Message_SendData(e.Data);
        }

        private void MessagePasting_CanExecuted(object sender, CanExecuteRoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                e.CanExecute = true;
            }
            else
            {
                Message_SendData(Clipboard.GetDataObject());
                e.CanExecute = false;
            }
        }
        private async void Message_SendData(IDataObject dataObject)
        {
            if (ContactsList.SelectedItem is Contact contact)
            {
                if (dataObject.GetDataPresent(DataFormats.Bitmap))
                {
                    var path = "ImageCache\\" + Path.GetRandomFileName() + ".png";
                    var bitmap = (InteropBitmap)dataObject.GetData(DataFormats.Bitmap);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
                    encoder.Save(stream);
                    stream.Close();
                    contact.SendFile(path);
                }
                else if (dataObject.GetDataPresent(DataFormats.FileDrop))
                {
                    foreach (var path in (string[])dataObject.GetData(DataFormats.FileDrop))
                            contact.SendFile(path);
                }
                else if (dataObject.GetDataPresent(DataFormats.Html))
                {
                    var data = (string)dataObject.GetData(DataFormats.Html);
                    // 正则匹配img
                    var regImg = new Regex(@"<img\b[^<>]*?\bsrc[\s\t\r\n]*=[\s\t\r\n]*[""']?[\s\t\r\n]*(?<imgUrl>[^\t\r\n""'<>]*)[^<>]*?/?[\s\t\r\n]*>", RegexOptions.IgnoreCase);
                    var matches = regImg.Matches(data);
                    if (matches.Count > 0)
                    {
                        Directory.CreateDirectory("ImageCache");
                        foreach (var match in matches.Cast<Match>())
                        {
                            var url = match.Groups["imgUrl"].Value;
                            var uri = new Uri(url);
                            string path;
                            // 如果是本地文件 验证存在后发送
                            if (uri.IsFile)
                            {
                                path = uri.LocalPath;
                                if (!File.Exists(path))
                                    continue;
                            }
                            else //网络图片进行下载
                            {
                                var client = new HttpClient();
                                var buf = await client.GetByteArrayAsync(uri);
                                var name = url[(url.LastIndexOf('/') + 1)..];
                                path = "ImageCache\\" + name;
                                //为无扩展名的添加
                                if (name.LastIndexOf('.') == -1)
                                {
                                    path += BitConverter.ToUInt16(buf) switch
                                    {
                                        0x4D42 => ".bmp",
                                        0x8DFF => ".jpg",
                                        0x5089 => ".png",
                                        0x4952 => ".webp",
                                        0x4947 => ".gif",
                                        _ => "",
                                    };
                                }
                                var file = File.Create(path);
                                file.Write(buf);
                                file.Close();
                            }
                            contact.SendFile(path);
                        }
                        return;
                    }
                }
                if (dataObject.GetDataPresent(DataFormats.Text))
                {
                    contact.SendMessage((string)dataObject.GetData(DataFormats.Text));
                }
            }
        }

        private async void ReciveFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContactsList.SelectedItem is Contact contact)
            {
                var msg = (FileMessage)((Hyperlink)sender).DataContext;
                await contact.ReciveFile(msg);
                ((StackPanel)FindListViewItemElementWithTemplateSelector(MessageList, msg, "afterSaved")).Visibility = Visibility.Visible;
            }
        }

        private void ReciveFileAsButton_Click(object sender, RoutedEventArgs e)
        {
            var msg = (FileMessage)((Hyperlink)sender).DataContext;
            var dialog = new SaveFileDialog
            {
                FileName = msg.Name
            };
            if ((bool)dialog.ShowDialog()!)
            {
                var fileName = dialog.FileName!;
                msg.Path = fileName[..fileName.LastIndexOf('.')];
                ReciveFileButton_Click(sender, e);
            }
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            var msg = (FileMessage)((Hyperlink)sender).DataContext;
            Process.Start(new ProcessStartInfo { FileName = msg.FullName, UseShellExecute = true });
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var msg = (FileMessage)((Hyperlink)sender).DataContext;
            Process.Start("explorer.exe", msg.Path!);
        }

        private async void ReviewImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContactsList.SelectedItem is Contact contact)
            {
                var msg = (FileMessage)((Hyperlink)sender).DataContext;
                string path;
                if (!File.Exists(msg.FullName))
                {
                    var pathTemp = msg.Path;
                    msg.Path = "Cache";
                    path = msg.FullName;
                    await contact.ReciveFile(msg);
                    msg.Path = pathTemp;
                }
                else
                    path = msg.FullName!;
                //获取绝对路径
                var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                file.Close();
                var image = (Image)FindListViewItemElementWithTemplateSelector(MessageList, msg, "image");
                image.Source ??= new BitmapImage(new Uri(file.Name, UriKind.Absolute));
                image.Visibility = image.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async void ReviewMediaButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContactsList.SelectedItem is Contact contact)
            {
                var msg = (FileMessage)((Hyperlink)sender).DataContext;
                string path;
                if (!File.Exists(msg.FullName))
                {
                    var pathTemp = msg.Path;
                    msg.Path = "Cache";
                    path = msg.FullName;
                    await contact.ReciveFile(msg);
                    msg.Path = pathTemp;
                }
                else
                    path = msg.FullName!;
                //获取绝对路径
                var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                file.Close();
                var mediaElement = (MediaElement)FindListViewItemElementWithTemplateSelector(MessageList, msg, "mediaElement");
                if (mediaElement.Visibility == Visibility.Collapsed)
                {
                    mediaElement.Visibility = Visibility.Visible;
                    mediaElement.Source ??= new Uri(file.Name, UriKind.Absolute);
                    mediaElement.Play();
                }
                else
                {
                    mediaElement.Visibility = Visibility.Collapsed;
                    mediaElement.Stop();
                }
            }
        }

        private void ContactsList_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ((ListView)sender).SelectedItem = null;
        }

        private object FindListViewItemElementWithTemplateSelector(ListView container, object item, string name)
        {
            var listViewItem = (ListViewItem)container.ItemContainerGenerator.ContainerFromItem(item);
            ContentPresenter myContentPresenter = FindVisualChild<ContentPresenter>(listViewItem)!;
            var myDataTemplate = ((MessageSelector)listViewItem.ContentTemplateSelector).SelectTemplate(item, container);
            return myDataTemplate.FindName(name, myContentPresenter);
        }

        // https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/how-to-find-datatemplate-generated-elements?view=netframeworkdesktop-4.8
        private childItem? FindVisualChild<childItem>(DependencyObject obj)
            where childItem : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child != null && child is childItem item)
                {
                    return item;
                }
                else
                {
                    childItem childOfChild = FindVisualChild<childItem>(child!)!;
                    if (childOfChild != null)
                        return childOfChild;
                }
            }
            return null;
        }

        private void MediaElement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var mediaPlayer = (MediaElement)sender;
            if (mediaPlayer.ToolTip.ToString() == "暂停")
            {
                mediaPlayer.Pause();
                mediaPlayer.ToolTip = "播放";
            }
            else
            {
                mediaPlayer.Play();
                mediaPlayer.ToolTip = "暂停";
            }
        }

        private void MediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            var mediaElement = (MediaElement)sender;
            mediaElement.Stop();
            mediaElement.Position = TimeSpan.MinValue;
            mediaElement.ToolTip = "播放";
        }
    }

    public class ProgressConverter : IValueConverter
    {
        object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ((TimeSpan)value).TotalSeconds;
        }

        object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return TimeSpan.FromSeconds((double)value);
        }
    }
}
