using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Presentation;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System.Text;
using SkiaSharp;
using PDFtoImage;

namespace AIChat.Services;

/// <summary>
/// 文档处理策略
/// </summary>
public enum DocumentStrategy
{
    /// <summary>纯文本 → Chat 模型</summary>
    TextOnly,
    /// <summary>文本 + 提取图片 → VL 模型</summary>
    TextWithImages,
    /// <summary>整页转图片 → VL 模型</summary>
    PageAsImages
}

/// <summary>
/// 嵌入图片信息
/// </summary>
public class EmbeddedImage
{
    public string Base64Data { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/png";
    public string? Description { get; set; }
}

/// <summary>
/// 文档内容提取结果
/// </summary>
public class DocumentContent
{
    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>错误信息</summary>
    public string? Error { get; set; }

    /// <summary>文件名</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>推荐的处理策略</summary>
    public DocumentStrategy Strategy { get; set; }

    /// <summary>提取的文本内容</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>嵌入的图片列表</summary>
    public List<EmbeddedImage> EmbeddedImages { get; set; } = new();

    /// <summary>整页图片列表（用于 PageAsImages 策略）</summary>
    public List<EmbeddedImage> PageImages { get; set; } = new();

    /// <summary>总页数</summary>
    public int PageCount { get; set; }

    /// <summary>文件类型</summary>
    public string FileType { get; set; } = string.Empty;
}

/// <summary>
/// 文件内容提取服务接口
/// </summary>
public interface IFileExtractionService
{
    /// <summary>
    /// 从文件中提取内容（智能混合方案）
    /// </summary>
    Task<DocumentContent> ExtractContentAsync(string base64Data, string mimeType, string fileName);
}

/// <summary>
/// 文件内容提取服务实现 - 智能混合方案
/// </summary>
public class FileExtractionService : IFileExtractionService
{
    private readonly ILogger<FileExtractionService> _logger;

    // 最大提取文本长度
    private const int MaxTextLength = 50000;
    // 最大图片数量（调整为20以支持更多页面的PDF）
    private const int MaxImages = 20;
    // 最大页数（转图片时）
    private const int MaxPages = 20;
    // 扫描件检测阈值：每页平均字符数少于此值视为扫描件
    private const int ScanThreshold = 50;

    public FileExtractionService(ILogger<FileExtractionService> logger)
    {
        _logger = logger;
    }

    public async Task<DocumentContent> ExtractContentAsync(string base64Data, string mimeType, string fileName)
    {
        var result = new DocumentContent
        {
            FileName = fileName,
            FileType = GetFileType(mimeType)
        };

        try
        {
            var bytes = Convert.FromBase64String(base64Data);
            using var stream = new MemoryStream(bytes);

            // 根据文件类型处理
            switch (mimeType.ToLower())
            {
                case "application/pdf":
                    await ProcessPdfAsync(stream, bytes, result);
                    break;

                case "application/msword":
                case "application/vnd.openxmlformats-officedocument.wordprocessingml.document":
                    await ProcessWordAsync(stream, result);
                    break;

                case "application/vnd.ms-excel":
                case "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet":
                    await ProcessExcelAsync(stream, result);
                    break;

                case "application/vnd.ms-powerpoint":
                case "application/vnd.openxmlformats-officedocument.presentationml.presentation":
                    await ProcessPptAsync(stream, bytes, result);
                    break;

                case "text/plain":
                case "text/csv":
                case "text/markdown":
                    await ProcessTextAsync(stream, result);
                    break;

                default:
                    result.Success = false;
                    result.Error = $"不支持的文件类型: {mimeType}";
                    return result;
            }

            // 智能检测策略
            result.Strategy = DetectStrategy(result);
            result.Success = true;

            _logger.LogInformation(
                "Document processed: {FileName}, Strategy: {Strategy}, Text: {TextLength} chars, Images: {ImageCount}, Pages: {PageCount}",
                fileName, result.Strategy, result.Text.Length, result.EmbeddedImages.Count, result.PageCount);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract content from file: {FileName}", fileName);
            result.Success = false;
            result.Error = $"文件处理失败: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// 智能检测处理策略
    /// </summary>
    private DocumentStrategy DetectStrategy(DocumentContent doc)
    {
        // 1. PPT 始终使用整页图片（布局重要）
        if (doc.FileType == "ppt")
        {
            return DocumentStrategy.PageAsImages;
        }

        // 2. 扫描件检测：文本很少但有页面
        if (doc.PageCount > 0)
        {
            var avgCharsPerPage = doc.Text.Length / (double)doc.PageCount;
            if (avgCharsPerPage < ScanThreshold)
            {
                _logger.LogInformation("Detected as scanned document (avg {AvgChars} chars/page)", avgCharsPerPage);
                return DocumentStrategy.PageAsImages;
            }
        }

        // 3. 有嵌入图片 → 文本 + 图片
        if (doc.EmbeddedImages.Count > 0)
        {
            return DocumentStrategy.TextWithImages;
        }

        // 4. 纯文本
        return DocumentStrategy.TextOnly;
    }

    /// <summary>
    /// 获取简化的文件类型
    /// </summary>
    private string GetFileType(string mimeType)
    {
        return mimeType.ToLower() switch
        {
            "application/pdf" => "pdf",
            "application/msword" or
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "word",
            "application/vnd.ms-excel" or
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "excel",
            "application/vnd.ms-powerpoint" or
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "ppt",
            _ => "text"
        };
    }

    #region PDF 处理

    /// <summary>
    /// 处理 PDF 文件
    /// </summary>
    private async Task ProcessPdfAsync(Stream stream, byte[] pdfBytes, DocumentContent result)
    {
        await Task.Run(() =>
        {
            var sb = new StringBuilder();
            var images = new List<EmbeddedImage>();

            using var document = PdfDocument.Open(stream);
            result.PageCount = document.NumberOfPages;

            foreach (var page in document.GetPages())
            {
                // 提取文本
                var text = page.Text;
                sb.AppendLine(text);

                // 提取图片
                if (images.Count < MaxImages)
                {
                    foreach (var image in page.GetImages())
                    {
                        try
                        {
                            if (image.TryGetPng(out var pngBytes))
                            {
                                images.Add(new EmbeddedImage
                                {
                                    Base64Data = Convert.ToBase64String(pngBytes),
                                    ContentType = "image/png"
                                });

                                if (images.Count >= MaxImages)
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to extract image from PDF");
                        }
                    }
                }

                if (sb.Length > MaxTextLength)
                    break;
            }

            result.Text = TruncateText(sb.ToString());
            result.EmbeddedImages = images;

            // 预生成页面图片（用于扫描件场景）
            GeneratePdfPageImages(pdfBytes, result);
        });
    }

    /// <summary>
    /// 将 PDF 页面转换为图片
    /// </summary>
    private void GeneratePdfPageImages(byte[] pdfBytes, DocumentContent result)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _logger.LogWarning("PDF page rendering is not supported on this operating system");
            return;
        }

        try
        {
            var pageCount = Math.Min(result.PageCount, MaxPages);
            _logger.LogInformation("GeneratePdfPageImages: Starting conversion, TotalPages={TotalPages}, PageCount={PageCount}, MaxPages={MaxPages}",
                result.PageCount, pageCount, MaxPages);

            for (int i = 0; i < pageCount; i++)
            {
                try
                {
                    using var imageStream = new MemoryStream();
                    Conversion.SavePng(imageStream, pdfBytes, page: new Index(i), options: new RenderOptions
                    {
                        Dpi = 150 // 平衡质量和大小
                    });

                    var imageBytes = imageStream.ToArray();
                    _logger.LogInformation("GeneratePdfPageImages: Page {PageIndex} converted, Size={Size} bytes",
                        i + 1, imageBytes.Length);

                    result.PageImages.Add(new EmbeddedImage
                    {
                        Base64Data = Convert.ToBase64String(imageBytes),
                        ContentType = "image/png",
                        Description = $"Page {i + 1}"
                    });
                }
                catch (Exception pageEx)
                {
                    _logger.LogWarning(pageEx, "Failed to convert PDF page {PageIndex} to image", i + 1);
                }
            }

            _logger.LogInformation("GeneratePdfPageImages: Completed, TotalPageImages={Count}", result.PageImages.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert PDF pages to images");
        }
    }

    #endregion

    #region Word 处理

    /// <summary>
    /// 处理 Word 文档
    /// </summary>
    private Task ProcessWordAsync(Stream stream, DocumentContent result)
    {
        return Task.Run(() =>
        {
            var sb = new StringBuilder();
            var images = new List<EmbeddedImage>();

            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            using var doc = WordprocessingDocument.Open(memoryStream, false);
            var body = doc.MainDocumentPart?.Document.Body;

            if (body != null)
            {
                // 提取文本
                foreach (var paragraph in body.Elements<Paragraph>())
                {
                    var text = paragraph.InnerText;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine(text);
                    }
                }
            }

            // 提取图片
            if (doc.MainDocumentPart != null)
            {
                foreach (var imagePart in doc.MainDocumentPart.ImageParts)
                {
                    if (images.Count >= MaxImages)
                        break;

                    try
                    {
                        using var imageStream = imagePart.GetStream();
                        using var ms = new MemoryStream();
                        imageStream.CopyTo(ms);

                        var contentType = imagePart.ContentType;
                        var base64 = Convert.ToBase64String(ms.ToArray());

                        // 转换为 PNG（如果需要）
                        if (!contentType.Contains("png") && !contentType.Contains("jpeg"))
                        {
                            base64 = ConvertToPng(ms.ToArray());
                            contentType = "image/png";
                        }

                        images.Add(new EmbeddedImage
                        {
                            Base64Data = base64,
                            ContentType = contentType
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to extract image from Word");
                    }
                }
            }

            result.Text = TruncateText(sb.ToString());
            result.EmbeddedImages = images;
            result.PageCount = 1; // Word 不容易确定页数
        });
    }

    #endregion

    #region Excel 处理

    /// <summary>
    /// 处理 Excel 文件
    /// </summary>
    private Task ProcessExcelAsync(Stream stream, DocumentContent result)
    {
        return Task.Run(() =>
        {
            var sb = new StringBuilder();

            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            using var doc = SpreadsheetDocument.Open(memoryStream, false);
            var workbookPart = doc.WorkbookPart;

            if (workbookPart == null)
            {
                result.Text = string.Empty;
                return;
            }

            var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>() ?? Enumerable.Empty<Sheet>();
            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
                                ?.Elements<SharedStringItem>()
                                .Select(s => s.InnerText)
                                .ToList() ?? new List<string>();

            foreach (var sheet in sheets)
            {
                if (sheet.Id?.Value == null) continue;

                var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
                var sheetData = worksheetPart.Worksheet.Elements<SheetData>().FirstOrDefault();

                if (sheetData == null) continue;

                sb.AppendLine($"\n=== 工作表: {sheet.Name} ===\n");

                // 转换为 Markdown 表格格式
                var rows = sheetData.Elements<Row>().ToList();
                if (rows.Count > 0)
                {
                    foreach (var row in rows.Take(500)) // 限制行数
                    {
                        var cells = row.Elements<Cell>().ToList();
                        var rowValues = cells.Select(c => GetCellValue(c, sharedStrings)).ToList();
                        sb.AppendLine("| " + string.Join(" | ", rowValues) + " |");
                    }
                }

                sb.AppendLine();

                if (sb.Length > MaxTextLength)
                    break;
            }

            result.Text = TruncateText(sb.ToString());
            result.PageCount = 1;
            result.Strategy = DocumentStrategy.TextOnly; // Excel 通常不需要图片处理
        });
    }

    private string GetCellValue(Cell cell, List<string> sharedStrings)
    {
        if (cell.CellValue == null)
            return string.Empty;

        var value = cell.CellValue.InnerText;

        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (int.TryParse(value, out int index) && index < sharedStrings.Count)
            {
                return sharedStrings[index];
            }
        }

        return value;
    }

    #endregion

    #region PPT 处理

    /// <summary>
    /// 处理 PowerPoint 文件 - 整页转图片
    /// </summary>
    private async Task ProcessPptAsync(Stream stream, byte[] pptBytes, DocumentContent result)
    {
        await Task.Run(() =>
        {
            var sb = new StringBuilder();

            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            using var doc = PresentationDocument.Open(memoryStream, false);
            var presentationPart = doc.PresentationPart;

            if (presentationPart == null)
            {
                result.Text = string.Empty;
                return;
            }

            var slideIds = presentationPart.Presentation.SlideIdList?.Elements<SlideId>()
                          ?? Enumerable.Empty<SlideId>();

            int slideNumber = 1;
            foreach (var slideId in slideIds)
            {
                if (slideId.RelationshipId?.Value == null) continue;

                var slidePart = (SlidePart)presentationPart.GetPartById(slideId.RelationshipId.Value);

                sb.AppendLine($"\n=== 幻灯片 {slideNumber} ===\n");

                // 提取文本
                var texts = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>();
                foreach (var text in texts)
                {
                    if (!string.IsNullOrWhiteSpace(text.Text))
                    {
                        sb.AppendLine(text.Text);
                    }
                }

                // 提取幻灯片中的图片
                if (result.EmbeddedImages.Count < MaxImages)
                {
                    foreach (var imagePart in slidePart.ImageParts)
                    {
                        if (result.EmbeddedImages.Count >= MaxImages)
                            break;

                        try
                        {
                            using var imageStream = imagePart.GetStream();
                            using var ms = new MemoryStream();
                            imageStream.CopyTo(ms);

                            result.EmbeddedImages.Add(new EmbeddedImage
                            {
                                Base64Data = Convert.ToBase64String(ms.ToArray()),
                                ContentType = imagePart.ContentType,
                                Description = $"Slide {slideNumber} image"
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to extract image from PPT slide {SlideNumber}", slideNumber);
                        }
                    }
                }

                slideNumber++;

                if (sb.Length > MaxTextLength)
                    break;
            }

            result.Text = TruncateText(sb.ToString());
            result.PageCount = slideNumber - 1;

            // PPT 强制使用 PageAsImages 策略，但我们用提取的图片代替
            // 因为 .NET 渲染 PPT 为图片比较复杂，我们使用提取的内容 + 嵌入图片
        });
    }

    #endregion

    #region 文本处理

    /// <summary>
    /// 处理纯文本文件
    /// </summary>
    private async Task ProcessTextAsync(Stream stream, DocumentContent result)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync();
        result.Text = TruncateText(text);
        result.PageCount = 1;
        result.Strategy = DocumentStrategy.TextOnly;
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 截断文本
    /// </summary>
    private string TruncateText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        text = text.Trim();

        if (text.Length > MaxTextLength)
        {
            return text.Substring(0, MaxTextLength) + "\n\n[内容已截断，仅显示前50000字符]";
        }

        return text;
    }

    /// <summary>
    /// 将图片转换为 PNG 格式
    /// </summary>
    private string ConvertToPng(byte[] imageBytes)
    {
        try
        {
            using var inputStream = new MemoryStream(imageBytes);
            using var bitmap = SKBitmap.Decode(inputStream);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            return Convert.ToBase64String(data.ToArray());
        }
        catch
        {
            // 如果转换失败，返回原始数据
            return Convert.ToBase64String(imageBytes);
        }
    }

    #endregion
}
