using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;
using AutoHPMA.GameTask.Model;
using AutoHPMA.Helpers.ImageHelper;

namespace AutoHPMA.GameTask.Core.Recognition
{
    public class TemplateMatchService
    {
        private readonly double _scale;

        public TemplateMatchService(double scale)
        {
            _scale = scale;
        }

        private Rect ScaleRect(Rect rect, double scale)
        {
            return new Rect(
                (int)(rect.X * scale),
                (int)(rect.Y * scale),
                (int)(rect.Width * scale),
                (int)(rect.Height * scale)
            );
        }

        public MatchResult FindInSource(Mat source, Mat template, MatchOptions? options = null)
        {
            options ??= new MatchOptions();

            Mat? resultRoiMask = null;
            if (options.SourceMask != null)
            {
                try
                {
                    resultRoiMask = MatchTemplateHelper.BuildResultRoiMaskFromSourceMask(
                        options.SourceMask, source.Width, source.Height, template.Width, template.Height);
                }
                catch
                {
                    resultRoiMask?.Dispose();
                    resultRoiMask = null;
                }
            }

            try
            {
                Mat templateBGR;
                Mat? mask = options.Mask;

                if (template.Channels() == 4)
                {
                    if (options.UseAlphaMask && mask == null)
                        mask = MatchTemplateHelper.GenerateMask(template);
                    templateBGR = new Mat();
                    Cv2.CvtColor(template, templateBGR, ColorConversionCodes.BGRA2BGR);
                }
                else
                {
                    templateBGR = template;
                }

                if (options.FindMultiple)
                {
                    var rects = MatchTemplateHelper.MatchOnePicForOnePic(
                        source, templateBGR, options.MatchMode, mask, options.Threshold, maxCount: -1, resultRoiMask);

                    if (rects.Count == 0) return MatchResult.Failed;

                    return new MatchResult
                    {
                        Success = true,
                        Location = new Point(rects[0].X, rects[0].Y),
                        Rects = rects.Select(rr => ScaleRect(rr, _scale)).ToList(),
                        RectsUnscaled = rects,
                        TemplateSize = new Size(template.Width, template.Height)
                    };
                }
                else
                {
                    var matchPoint = MatchTemplateHelper.MatchTemplate(
                        source, templateBGR, options.MatchMode, mask, options.Threshold, resultRoiMask);

                    if (matchPoint == default) return MatchResult.Failed;

                    var unscaledRect = new Rect(matchPoint.X, matchPoint.Y, template.Width, template.Height);

                    return new MatchResult
                    {
                        Success = true,
                        Location = matchPoint,
                        Rects = new List<Rect> { ScaleRect(unscaledRect, _scale) },
                        RectsUnscaled = new List<Rect> { unscaledRect },
                        TemplateSize = new Size(template.Width, template.Height)
                    };
                }
            }
            finally
            {
                resultRoiMask?.Dispose();
            }
        }
    }
}
