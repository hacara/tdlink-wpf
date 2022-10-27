using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace tdlink_wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // 捕获未处理异常
            DispatcherUnhandledException += (object sender, DispatcherUnhandledExceptionEventArgs e) => {
                e.Handled = true;
                MessageBox.Show("发生了一个错误:" + Environment.NewLine + e.Exception.Message, "ERR", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }
    }
}
