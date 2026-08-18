// DeepSeek Harness Desktop - 黑色鲸鱼图标构建工具（C# 5，供 Windows PowerShell Add-Type 编译）
// 输入：官方 Harness UI favicon.svg 的 path 数据（apps/web/public/favicon.svg，viewBox 0 0 50 50）
// 输出：Windows 多尺寸 app.ico（16/20/24/32/40/48/64/128/256，PNG 条目，透明背景）
// 渲染方式：WPF Geometry.Parse 原样解析官方矢量轮廓，仅以纯黑 #000000 填充（SVG 默认填充色），
//           不修改任何形状/比例/控制点，未重新绘制。
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

public static class SvgWhaleIconBuilder
{
    private const double ViewBoxSize = 50.0; // viewBox 0 0 50 50

    public static void BuildIco(string pathData, string icoPath)
    {
        Exception error = null;
        var thread = new System.Threading.Thread(delegate()
        {
            try { BuildIcoCore(pathData, icoPath); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
    }

    private static void BuildIcoCore(string pathData, string icoPath)
    {
        Geometry geometry = Geometry.Parse(pathData);
        PathGeometry pg = geometry as PathGeometry;
        if (pg != null) pg.FillRule = FillRule.Nonzero; // SVG 默认填充规则

        int[] sizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        List<int> usedSizes = new List<int>();
        List<byte[]> blobs = new List<byte[]>();

        foreach (int s in sizes)
        {
            double scale = s / ViewBoxSize;
            RenderTargetBitmap rtb = new RenderTargetBitmap(s, s, 96, 96, PixelFormats.Pbgra32);
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0, 0, 0)), null, geometry); // 纯黑
                dc.Pop();
            }
            rtb.Render(dv);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (MemoryStream ms = new MemoryStream())
            {
                encoder.Save(ms);
                usedSizes.Add(s);
                blobs.Add(ms.ToArray());
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

        Console.WriteLine("SvgWhaleIconBuilder: " + usedSizes.Count + " 个尺寸已写入 " + icoPath);
    }
}
