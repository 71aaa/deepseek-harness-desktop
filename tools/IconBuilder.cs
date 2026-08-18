// DeepSeek Harness Desktop - 图标构建工具（C# 5，供 Windows PowerShell Add-Type 编译）
// 输入：官方 DeepSeek 鲸鱼 Logo 原图（ICO/PNG）
// 输出：Windows 多尺寸 app.ico（16/20/24/32/40/48/64/128/256，PNG 条目，透明背景，高质量缩放，不改绘）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IconBuilder
{
    public static void BuildIco(string sourcePath, string icoPath)
    {
        int[] sizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        List<int> usedSizes = new List<int>();
        List<byte[]> blobs = new List<byte[]>();

        using (Bitmap src = new Bitmap(sourcePath))
        {
            foreach (int s in sizes)
            {
                using (Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(src, new Rectangle(0, 0, s, s),
                            new Rectangle(0, 0, src.Width, src.Height), GraphicsUnit.Pixel);
                    }
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        usedSizes.Add(s);
                        blobs.Add(ms.ToArray());
                    }
                }
            }
        }

        string dir = Path.GetDirectoryName(icoPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using (FileStream fs = new FileStream(icoPath, FileMode.Create, FileAccess.Write))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);                       // reserved
            bw.Write((ushort)1);                       // type: icon
            bw.Write((ushort)usedSizes.Count);         // count
            int offset = 6 + 16 * usedSizes.Count;
            for (int i = 0; i < usedSizes.Count; i++)
            {
                int s = usedSizes[i];
                byte dim = s >= 256 ? (byte)0 : (byte)s; // 256 用 0 表示
                bw.Write(dim);                          // width
                bw.Write(dim);                          // height
                bw.Write((byte)0);                      // colors
                bw.Write((byte)0);                      // reserved
                bw.Write((ushort)1);                    // planes
                bw.Write((ushort)32);                   // bpp
                bw.Write((uint)blobs[i].Length);        // size
                bw.Write((uint)offset);                 // offset
                offset += blobs[i].Length;
            }
            for (int i = 0; i < blobs.Count; i++)
                bw.Write(blobs[i]);
        }

        Console.WriteLine("IconBuilder: " + usedSizes.Count + " 个尺寸已写入 " + icoPath);
    }
}
