using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Shadowsocks.Properties;
using ZXing.QrCode.Internal;

namespace Shadowsocks.View;

public partial class ShowTextForm : Form
{
    public ShowTextForm(string title, string text)
    {
        Icon = Icon.FromHandle(Resources.ssw128.GetHicon());
        InitializeComponent();

        Text = title;
        PictureQRcode.Height = ClientSize.Height - textBox.Height;
        textBox.Text = text;
    }

    private void GenQR(string ssconfig)
    {
        var dpi_mul = Util.Utils.GetDpiMul();
        var width = Math.Min(PictureQRcode.Width, PictureQRcode.Height) * 4 / 4;
        try
        {
            var qrText = ssconfig;
            var code = ZXing.QrCode.Internal.Encoder.encode(qrText, ErrorCorrectionLevel.M);
            var m = code.Matrix;
            var blockSize = Math.Max(width / (m.Width + 2), 1);
            var drawArea = new Bitmap((m.Width + 2) * blockSize, (m.Height + 2) * blockSize);
            using (var g = Graphics.FromImage(drawArea))
            {
                g.Clear(Color.White);
                using (Brush b = new SolidBrush(Color.Black))
                {
                    for (var row = 0; row < m.Width; row++)
                    {
                        for (var col = 0; col < m.Height; col++)
                        {
                            if (m[row, col] != 0)
                            {
                                g.FillRectangle(b, blockSize * (row + 1), blockSize * (col + 1),
                                    blockSize, blockSize);
                            }
                        }
                    }
                }
                var ngnl = Resources.ngnl;
                int div = 13, div_l = 5, div_r = 8;
                int l = (m.Width * div_l + div - 1) / div * blockSize, r = (m.Width * div_r + div - 1) / div * blockSize;
                g.DrawImage(ngnl, new Rectangle(l + blockSize, l + blockSize, r - l, r - l));
            }
            PictureQRcode.Image = drawArea;
        }
        catch
        {

        }
    }

    private void textBox_TextChanged(object sender, EventArgs e)
    {
        GenQR(textBox.Text);
    }

    private void ShowTextForm_SizeChanged(object sender, EventArgs e)
    {
        PictureQRcode.Height = ClientSize.Height - textBox.Height;
        GenQR(textBox.Text);
    }

    private void textBox_KeyPress(object sender, KeyPressEventArgs e)
    {
        // Use KeyPress to avoid the beep when press Ctrl + A, don't do it in KeyDown
        if (e.KeyChar == '\x1')
        {
            textBox.SelectAll();
            e.Handled = true;
        }
    }
}