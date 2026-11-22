using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace CampusManager.App.Services;

/// <summary>Aperçu PDF par le moteur PDF de Windows : les pages sont rendues en images, sans navigateur web.</summary>
public static class PdfPreview
{
    public static async Task<(int Pages, List<ImageSource> Images)> RenderAsync(string path, int maxPages, double width)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        using var mem = new InMemoryRandomAccessStream();
        await mem.WriteAsync(bytes.AsBuffer());
        mem.Seek(0);
        var doc = await PdfDocument.LoadFromStreamAsync(mem);
        var images = new List<ImageSource>();
        for (uint i = 0; i < Math.Min((uint)maxPages, doc.PageCount); i++)
        {
            using var page = doc.GetPage(i);
            using var output = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(output, new PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(100, width) });
            output.Seek(0);
            var png = new byte[output.Size];
            await output.ReadAsync(png.AsBuffer(), (uint)output.Size, InputStreamOptions.None);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(png);
            bmp.EndInit();
            bmp.Freeze();
            images.Add(bmp);
        }
        return ((int)doc.PageCount, images);
    }
}
