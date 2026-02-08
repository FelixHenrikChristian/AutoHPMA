using OpenCvSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace AutoHPMA.Helpers.RecognizeHelper;

/// <summary>
/// Windows OCR 辅助类（单例模式）
/// 使用 Windows 10+ 内置的 OCR 引擎
/// </summary>
public class WindowsOCRHelper : IDisposable
{
    private static readonly Lazy<WindowsOCRHelper> _instance = new(() => new WindowsOCRHelper());
    
    public static WindowsOCRHelper Instance => _instance.Value;

    private readonly OcrEngine? _ocrEngine;
    private bool _isDisposed;

    private WindowsOCRHelper()
    {
        // 优先使用简体中文，如果不可用则使用英文
        var language = new Windows.Globalization.Language("zh-Hans-CN");
        if (OcrEngine.IsLanguageSupported(language))
        {
            _ocrEngine = OcrEngine.TryCreateFromLanguage(language);
        }
        else
        {
            // 回退到英文
            language = new Windows.Globalization.Language("en-US");
            if (OcrEngine.IsLanguageSupported(language))
            {
                _ocrEngine = OcrEngine.TryCreateFromLanguage(language);
            }
            else
            {
                // 使用用户配置文件语言
                _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            }
        }

        if (_ocrEngine == null)
        {
            Debug.WriteLine("WindowsOCR: Failed to create OCR engine. No supported languages found.");
        }
    }

    /// <summary>
    /// 识别图像中的文字
    /// </summary>
    public string Ocr(Mat mat)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        
        if (_ocrEngine == null)
        {
            Debug.WriteLine("WindowsOCR: OCR engine not available");
            return string.Empty;
        }

        try
        {
            // 同步调用异步方法
            return OcrAsync(mat).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WindowsOCR Error: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 异步识别图像中的文字
    /// </summary>
    private async Task<string> OcrAsync(Mat mat)
    {
        // 将 Mat 转换为 SoftwareBitmap
        using var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;

        // 转换为 IRandomAccessStream
        var randomAccessStream = new InMemoryRandomAccessStream();
        await randomAccessStream.WriteAsync(stream.ToArray().AsBuffer());
        randomAccessStream.Seek(0);

        // 解码为 SoftwareBitmap
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        // 执行 OCR
        var result = await _ocrEngine!.RecognizeAsync(softwareBitmap);
        
        // 清理资源
        softwareBitmap.Dispose();
        randomAccessStream.Dispose();

        return result.Text ?? string.Empty;
    }

    /// <summary>
    /// 检查是否支持指定语言
    /// </summary>
    public static bool IsLanguageSupported(string languageTag)
    {
        var language = new Windows.Globalization.Language(languageTag);
        return OcrEngine.IsLanguageSupported(language);
    }

    /// <summary>
    /// 获取可用的 OCR 语言列表
    /// </summary>
    public static IReadOnlyList<Windows.Globalization.Language> AvailableLanguages => OcrEngine.AvailableRecognizerLanguages;

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
