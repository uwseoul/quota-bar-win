using System.Drawing;
using System.Drawing.Imaging;

if (args.Length < 2)
{
    Console.WriteLine("Usage: IcoConverter <input.png> <output.ico>");
    return 1;
}

var inputPath = args[0];
var outputPath = args[1];

using var source = new Bitmap(inputPath);
var sizes = new[] { 16, 32, 48, 256 };

using var ms = new MemoryStream();
using var writer = new BinaryWriter(ms);

writer.Write((short)0); // reserved
writer.Write((short)1); // type: icon
writer.Write((short)sizes.Length); // count

var imageDataList = new List<byte[]>();
int offset = 6 + sizes.Length * 16;

foreach (var size in sizes)
{
    int width = size == 256 ? 0 : size;
    int height = size == 256 ? 0 : size;

    using var resized = new Bitmap(size, size);
    using (var g = Graphics.FromImage(resized))
    {
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        g.DrawImage(source, 0, 0, size, size);
    }

    using var pngMs = new MemoryStream();
    resized.Save(pngMs, ImageFormat.Png);
    var data = pngMs.ToArray();
    imageDataList.Add(data);

    writer.Write((byte)width);
    writer.Write((byte)height);
    writer.Write((byte)0); // colors
    writer.Write((byte)0); // reserved
    writer.Write((short)1); // color planes
    writer.Write((short)32); // bits per pixel
    writer.Write(data.Length);
    writer.Write(offset);
    offset += data.Length;
}

foreach (var data in imageDataList)
{
    writer.Write(data);
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllBytes(outputPath, ms.ToArray());
Console.WriteLine($"Created {outputPath}");
return 0;
