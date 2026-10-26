using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SchulnetzSync.Core.Profiles;

namespace SchulnetzSync.UI;

/// <summary>
/// The optional picture in place of the initials. Whatever the person picks is cut to the
/// centre square and stored small as PNG in the profile folder, so a 12-megapixel photo
/// does not have to be decoded on every start.
/// </summary>
public static class ProfilePicture
{
    /// <summary>Edge length of the stored picture; enough for the avatar on a 4K screen.</summary>
    private const int Size = 128;

    /// <summary>Filter for the file dialog.</summary>
    public const string FileFilter = "Bilder|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.heic|Alle Dateien|*.*";

    private static string PathFor(Guid profileId) => DataPaths.PictureFor(DataPaths.Root, profileId);

    public static bool Exists(Guid profileId) => File.Exists(PathFor(profileId));

    /// <summary>The stored picture, or null when there is none or it cannot be read.</summary>
    public static ImageSource? Load(Guid profileId)
    {
        var path = PathFor(profileId);
        if (!File.Exists(path)) return null;

        try
        {
            // OnLoad reads the whole file now, so it is not locked when it is replaced later
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption   = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource     = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or FormatException)
        {
            App.LogLine($"Profilbild nicht lesbar: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Crops the chosen image to its centre square, scales it down and stores it.
    /// Throws when the file is no image WPF can read; the caller tells the user.
    /// </summary>
    public static void Save(Guid profileId, string sourcePath)
    {
        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource   = new Uri(sourcePath);
        source.EndInit();

        int edge = Math.Min(source.PixelWidth, source.PixelHeight);
        var square = new CroppedBitmap(source, new System.Windows.Int32Rect(
            (source.PixelWidth - edge) / 2, (source.PixelHeight - edge) / 2, edge, edge));

        double scale = Math.Min(1.0, (double)Size / edge);
        var small = new TransformedBitmap(square, new ScaleTransform(scale, scale));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(small));

        var path = PathFor(profileId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static void Remove(Guid profileId)
    {
        var path = PathFor(profileId);
        if (File.Exists(path)) File.Delete(path);
    }
}
