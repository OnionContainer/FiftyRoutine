using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Playground.ImageCrypto;

public partial class MainWindow : Window
{
    private byte[]? _decryptedBytes;
    private string _decryptedExt = ".png";

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Encrypt_Click(object sender, RoutedEventArgs e)
    {
        var password = PinBox.Password;
        if (string.IsNullOrEmpty(password))
        {
            MessageBox.Show("请先填写密码。");
            return;
        }

        var open = new OpenFileDialog
        {
            Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp|所有文件|*.*"
        };
        if (open.ShowDialog(this) != true) return;

        try
        {
            var bytes = File.ReadAllBytes(open.FileName);
            SrcPreview.Source = LoadBitmap(bytes);
            DecPreview.Source = null;
            _decryptedBytes = null;

            var ext = Path.GetExtension(open.FileName);
            var blob = ImageCryptoService.Encrypt(bytes, ext, password);

            var save = new SaveFileDialog
            {
                Filter = "加密图|*.pmimg",
                FileName = Path.GetFileNameWithoutExtension(open.FileName) + ".pmimg"
            };
            if (save.ShowDialog(this) != true)
            {
                StatusText.Text = "已预览原图，取消了保存密文。";
                return;
            }
            File.WriteAllBytes(save.FileName, blob);
            StatusText.Text = $"已加密 → {save.FileName}（{blob.Length} 字节）。可用「打开密文解密」核对。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "加密失败");
            StatusText.Text = "加密失败。";
        }
    }

    private void Decrypt_Click(object sender, RoutedEventArgs e)
    {
        var password = PinBox.Password;
        if (string.IsNullOrEmpty(password))
        {
            MessageBox.Show("请先填写密码。");
            return;
        }

        var open = new OpenFileDialog { Filter = "加密图|*.pmimg|所有文件|*.*" };
        if (open.ShowDialog(this) != true) return;

        try
        {
            var blob = File.ReadAllBytes(open.FileName);
            var (image, ext) = ImageCryptoService.Decrypt(blob, password);
            _decryptedBytes = image;
            _decryptedExt = ext;
            DecPreview.Source = LoadBitmap(image);
            StatusText.Text = $"解密成功（{image.Length} 字节，扩展名 {ext}）。可「导出解密图」对照原图。";
        }
        catch (Exception ex)
        {
            DecPreview.Source = null;
            _decryptedBytes = null;
            MessageBox.Show(ex.Message, "解密失败");
            StatusText.Text = "解密失败。";
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_decryptedBytes is null)
        {
            MessageBox.Show("还没有解密结果。");
            return;
        }
        var save = new SaveFileDialog
        {
            Filter = $"图片|*{_decryptedExt}|所有文件|*.*",
            FileName = "decrypted" + _decryptedExt
        };
        if (save.ShowDialog(this) != true) return;
        File.WriteAllBytes(save.FileName, _decryptedBytes);
        StatusText.Text = "已导出：" + save.FileName;
    }

    private static BitmapImage LoadBitmap(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
