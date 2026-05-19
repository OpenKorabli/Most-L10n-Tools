using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Forms;
// using Microsoft.VisualBasic; (not used)

namespace Most_L10n_File_Extractor
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Load saved path or default
            var saved = Properties.Settings.Default["MostKorabliPath"] as string;
            if (string.IsNullOrEmpty(saved))
            {
                saved = @"C:\Program Files\Lesta\Most Korabli";
            }

            var tb = this.FindName("InstallPathTextBox") as System.Windows.Controls.TextBox;
            if (tb != null)
            {
                tb.Text = saved;
            }
            // Load Mod cache path or default
            var modSaved = Properties.Settings.Default["MostModCachePath"] as string;
            if (string.IsNullOrEmpty(modSaved))
            {
                modSaved = @"C:\ProgramData\Lesta\Most Korabli\.Cache";
            }
            var modTb = this.FindName("ModCachePathTextBox") as System.Windows.Controls.TextBox;
            if (modTb != null)
            {
                modTb.Text = modSaved;
            }
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            var status = this.FindName("StatusTextBlock") as System.Windows.Controls.TextBlock;
            if (status == null) return;
            // Prompt for locale code using WinForms dialog
            var input = ShowInputDialog("请输入地区语言代码 (例如 zh-CN):", "应用翻译", "zh-CN");
            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            // validate format: two letters, optional - then two letters
            var parts = input.Split('-');
            if (parts.Length != 1 && parts.Length != 2)
            {
                System.Windows.MessageBox.Show("请输入合法的地区代码，例如 zh-CN 或 zh-CN 的变体。", "无效输入", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var lang = parts[0];
            var region = parts.Length == 2 ? parts[1] : string.Empty;
            if (lang.Length < 2 || !lang.All(char.IsLetter) || (region != string.Empty && region.Length < 1))
            {
                System.Windows.MessageBox.Show("请输入合法的地区代码，例如 zh-CN。", "无效输入", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // normalize to lower-case for processing but keep original case for output filenames
            var localeLower = input.ToLowerInvariant();

            status.Text = "正在应用翻译...";
            await Task.Run(() =>
            {
                var exeFolder = AppDomain.CurrentDomain.BaseDirectory;
                var paraOut = System.IO.Path.Combine(exeFolder, "ParaTranzOutput");
                var conv = new AssemblyResourcesConverter();
                var res = conv.ConvertFromParaTranz(paraOut, input);
                this.Dispatcher.Invoke(() =>
                {
                    if (res.Errors.Count > 0)
                    {
                        status.Text = $"应用完成：{res.FileCount} 个文件，替换 {res.ItemCount} 项（错误 {res.Errors.Count} 个）";
                    }
                    else
                    {
                        status.Text = $"应用完成：{res.FileCount} 个文件，替换 {res.ItemCount} 项";
                    }
                });
            });
        }

        private string ShowInputDialog(string text, string caption, string defaultValue)
        {
            string result = null;
            System.Windows.Forms.Form prompt = new System.Windows.Forms.Form()
            {
                Width = 400,
                Height = 150,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                Text = caption,
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
            };
            System.Windows.Forms.Label textLabel = new System.Windows.Forms.Label() { Left = 10, Top = 10, Text = text, Width = 360 };
            System.Windows.Forms.TextBox textBox = new System.Windows.Forms.TextBox() { Left = 10, Top = 35, Width = 360, Text = defaultValue };
            System.Windows.Forms.Button confirmation = new System.Windows.Forms.Button() { Text = "确定", Left = 210, Width = 75, Top = 70, DialogResult = System.Windows.Forms.DialogResult.OK };
            System.Windows.Forms.Button cancel = new System.Windows.Forms.Button() { Text = "取消", Left = 295, Width = 75, Top = 70, DialogResult = System.Windows.Forms.DialogResult.Cancel };
            confirmation.Click += (sender, e) => { prompt.Close(); };
            cancel.Click += (sender, e) => { textBox.Text = string.Empty; prompt.Close(); };
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.Controls.Add(cancel);
            prompt.Controls.Add(textLabel);
            prompt.AcceptButton = confirmation;

            var dr = prompt.ShowDialog();
            if (dr == System.Windows.Forms.DialogResult.OK) result = textBox.Text;
            return result;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var tb2 = this.FindName("InstallPathTextBox") as System.Windows.Controls.TextBox;
            var initial = tb2 != null ? tb2.Text : string.Empty;
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "请选择 Most Korabli 的安装目录";
                dlg.SelectedPath = initial;
                dlg.ShowNewFolderButton = false;
                var result = dlg.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    if (tb2 != null)
                    {
                        tb2.Text = dlg.SelectedPath;
                    }
                    SavePath(dlg.SelectedPath);
                }
            }
        }

        private void BrowseModCacheButton_Click(object sender, RoutedEventArgs e)
        {
            var tb2 = this.FindName("ModCachePathTextBox") as System.Windows.Controls.TextBox;
            var initial = tb2 != null ? tb2.Text : string.Empty;
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "请选择 Mod 缓存目录";
                dlg.SelectedPath = initial;
                dlg.ShowNewFolderButton = false;
                var result = dlg.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    if (tb2 != null)
                    {
                        tb2.Text = dlg.SelectedPath;
                    }
                    SaveModCachePath(dlg.SelectedPath);
                }
            }
        }

        private void InstallPathTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var tb3 = this.FindName("InstallPathTextBox") as System.Windows.Controls.TextBox;
            if (tb3 != null)
            {
                SavePath(tb3.Text);
            }
        }

        private void ModCachePathTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var tb3 = this.FindName("ModCachePathTextBox") as System.Windows.Controls.TextBox;
            if (tb3 != null)
            {
                SaveModCachePath(tb3.Text);
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var tb = this.FindName("InstallPathTextBox") as System.Windows.Controls.TextBox;
            var status = this.FindName("StatusTextBlock") as System.Windows.Controls.TextBlock;
            if (tb == null || status == null) return;
            status.Text = "正在导出...";
            var appFolder = tb.Text;
            var exeFolder = AppDomain.CurrentDomain.BaseDirectory;
            var outRoot = System.IO.Path.Combine(exeFolder, "AssemblyResourcesOutput");

            await System.Threading.Tasks.Task.Run(() =>
            {
                var results = AssemblyResourcesExtractor.Extract(appFolder, outRoot);
                this.Dispatcher.Invoke(() =>
                {
                    var errors = results.Count(r => r != null && r.StartsWith("ERROR:"));
                    var files = results.Count - errors;
                    status.Text = $"导出完成：{files} 个文件（错误 {errors} 个）";
                });
            });
        }

        private async void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            var modTb = this.FindName("ModCachePathTextBox") as System.Windows.Controls.TextBox;
            var status = this.FindName("StatusTextBlock") as System.Windows.Controls.TextBlock;
            if (status == null) return;
            status.Text = "正在转换为 ParaTranz 格式...";
            var modPath = modTb.Text;

            await Task.Run(() =>
            {
                var exeFolder = AppDomain.CurrentDomain.BaseDirectory;
                var asmOut = System.IO.Path.Combine(exeFolder, "AssemblyResourcesOutput");
                var paraOut = System.IO.Path.Combine(exeFolder, "ParaTranzOutput");
                var conv = new AssemblyResourcesConverter();
                var res = conv.ConvertToParaTranz(asmOut, modPath, paraOut);
                this.Dispatcher.Invoke(() =>
                {
                    if (res.Errors.Count > 0)
                    {
                        status.Text = $"转换完成：{res.FileCount} 个文件，条目 {res.ItemCount} 个（错误 {res.Errors.Count} 个）";
                    }
                    else
                    {
                        status.Text = $"转换完成：{res.FileCount} 个文件，条目 {res.ItemCount} 个";
                    }
                });
            });
        }

        private void SavePath(string path)
        {
            Properties.Settings.Default["MostKorabliPath"] = path;
            Properties.Settings.Default.Save();
        }

        private void SaveModCachePath(string path)
        {
            Properties.Settings.Default["MostModCachePath"] = path;
            Properties.Settings.Default.Save();
        }
    }
}
