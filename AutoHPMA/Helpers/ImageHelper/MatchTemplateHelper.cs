using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace AutoHPMA.Helpers.ImageHelper
{
    /// <summary>
    /// 模板匹配
    /// </summary>
    public class MatchTemplateHelper
    {
        /// <param name="resultRoiMask">可选。与 result 同尺寸的 ROI 遮罩，仅在此遮罩非零位置取极值；用于源图区域限制（如 SourceMask）。</param>
        public static Point MatchTemplate(Mat srcMat, Mat dstMat, TemplateMatchModes matchMode, Mat? maskMat = null, double threshold = 0.8, Mat? resultRoiMask = null)
        {
            try
            {
                using var result = new Mat();
                Cv2.MatchTemplate(srcMat, dstMat, result, matchMode, maskMat!);

                if (matchMode is TemplateMatchModes.SqDiff or TemplateMatchModes.CCoeff or TemplateMatchModes.CCorr)
                {
                    Cv2.Normalize(result, result, 0, 1, NormTypes.MinMax);
                }

                double minValue, maxValue;
                Point minLoc, maxLoc;
                if (resultRoiMask != null && !resultRoiMask.Empty())
                    Cv2.MinMaxLoc(result, out minValue, out maxValue, out minLoc, out maxLoc, resultRoiMask);
                else
                    Cv2.MinMaxLoc(result, out minValue, out maxValue, out minLoc, out maxLoc);

                if (matchMode is TemplateMatchModes.SqDiff or TemplateMatchModes.SqDiffNormed)
                {
                    if (minValue <= 1 - threshold)
                    {
                        return minLoc;
                    }
                }
                else
                {
                    if (maxValue >= threshold)
                    {
                        return maxLoc;
                    }
                }

                return default;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MatchTemplate 异常: {ex}");
                return default;
            }
        }

        public static List<Point> MatchTemplateMulti(Mat srcMat, Mat dstMat, Mat? maskMat = null, double threshold = 0.8, int maxCount = 8)
        {
            var points = new List<Point>();
            try
            {
                using var result = new Mat();
                Cv2.MatchTemplate(srcMat, dstMat, result, TemplateMatchModes.CCoeffNormed, maskMat!);

                using var mask = new Mat(result.Height, result.Width, MatType.CV_8UC1, Scalar.White);
                using var maskSub = new Mat(result.Height, result.Width, MatType.CV_8UC1, Scalar.Black);
                while (points.Count < maxCount)
                {
                    Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLoc, mask);
                    if (maxValue < threshold)
                        break;

                    points.Add(maxLoc);

                    // 使用矩形遮罩排除已匹配区域
                    var maskRect = new Rect(maxLoc.X, maxLoc.Y, dstMat.Width, dstMat.Height);
                    maskSub.Rectangle(maskRect, Scalar.White, -1);
                    Cv2.Subtract(mask, maskSub, mask);
                    maskSub.Rectangle(maskRect, Scalar.Black, -1); // 重置 maskSub
                }

                return points;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MatchTemplateMulti 异常: {ex}");
                return points;
            }
        }

        public static List<Rect> MatchOnePicForOnePic(Mat srcMat, Mat dstMat, TemplateMatchModes matchMode, Mat? maskMat, double threshold, int maxCount = -1, Mat? resultRoiMask = null)
        {
            List<Rect> list = [];

            if (maxCount < 0)
            {
                maxCount = srcMat.Width * srcMat.Height / dstMat.Width / dstMat.Height;
            }

            using var workMat = srcMat.Clone();
            using var roiMask = resultRoiMask != null ? resultRoiMask.Clone() : null;

            for (int i = 0; i < maxCount; i++)
            {
                var point = MatchTemplate(workMat, dstMat, matchMode, maskMat, threshold, roiMask);
                if (point != new Point())
                {
                    Cv2.Rectangle(workMat, point, new Point(point.X + dstMat.Width, point.Y + dstMat.Height), Scalar.Black, -1);
                    if (roiMask != null)
                        Cv2.Rectangle(roiMask, point, new Point(point.X + dstMat.Width, point.Y + dstMat.Height), Scalar.Black, -1);
                    list.Add(new Rect(point.X, point.Y, dstMat.Width, dstMat.Height));
                }
                else
                {
                    break;
                }
            }

            return list;
        }

        /// <summary>
        /// 根据源图遮罩和模板尺寸生成与 MatchTemplate 结果图同尺寸的 ROI 遮罩：仅当模板左上角落在 (x,y) 时，
        /// 模板覆盖区域完全在 sourceMask 非零像素内，该 (x,y) 在结果图中才为 255，否则为 0。用于仅在不规则遮罩区域内匹配。
        /// </summary>
        /// <param name="sourceMask">与源图同尺寸的遮罩（非零表示可匹配区域）</param>
        /// <param name="sourceWidth">源图宽度</param>
        /// <param name="sourceHeight">源图高度</param>
        /// <param name="templateWidth">模板宽度</param>
        /// <param name="templateHeight">模板高度</param>
        /// <returns>与 result 同尺寸的 8UC1 遮罩，调用方负责释放</returns>
        public static Mat BuildResultRoiMaskFromSourceMask(Mat sourceMask, int sourceWidth, int sourceHeight, int templateWidth, int templateHeight)
        {
            Mat mask = sourceMask;
            bool needDisposeMask = false;
            if (sourceMask.Width != sourceWidth || sourceMask.Height != sourceHeight)
            {
                mask = new Mat();
                Cv2.Resize(sourceMask, mask, new Size(sourceWidth, sourceHeight));
                needDisposeMask = true;
            }
            try
            {
                // 侵蚀：仅当以 (x,y) 为左上角、大小为 (tw,th) 的矩形内全为非零时，该位置才可参与匹配
                using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(templateWidth, templateHeight));
                using var eroded = new Mat();
                Cv2.Erode(mask, eroded, kernel);

                int rw = sourceWidth - templateWidth + 1;
                int rh = sourceHeight - templateHeight + 1;
                if (rw <= 0 || rh <= 0)
                    return new Mat();

                var resultRoi = new Mat(rh, rw, MatType.CV_8UC1);
                int th2 = templateHeight / 2, tw2 = templateWidth / 2;
                for (int y = 0; y < rh; y++)
                for (int x = 0; x < rw; x++)
                    resultRoi.Set(y, x, eroded.At<byte>(y + th2, x + tw2));
                return resultRoi;
            }
            finally
            {
                if (needDisposeMask)
                    mask?.Dispose();
            }
        }

        /// <summary>
        /// 从输入图像生成Mask，透明区域为黑色，非透明区域为白色
        /// </summary>
        /// <param name="inputMat">输入图像（需要带 Alpha 通道，即 BGRA 格式）</param>
        /// <returns>生成的 Mask（调用方负责释放）</returns>
        public static Mat GenerateMask(Mat inputMat)
        {
            try
            {
                // 确保输入图像是 BGRA 格式
                Mat bgraMat;
                bool needDispose = false;

                if (inputMat.Channels() == 4)
                {
                    bgraMat = inputMat;
                }
                else
                {
                    bgraMat = new Mat();
                    Cv2.CvtColor(inputMat, bgraMat, ColorConversionCodes.BGR2BGRA);
                    needDispose = true;
                }

                try
                {
                    // 使用向量化操作提取 Alpha 通道并生成 Mask
                    // Split 分离 BGRA 通道: [0]=B, [1]=G, [2]=R, [3]=A
                    Cv2.Split(bgraMat, out Mat[] channels);

                    // 释放不需要的通道
                    channels[0].Dispose();
                    channels[1].Dispose();
                    channels[2].Dispose();

                    // 使用 Alpha 通道，阈值处理生成二值 Mask
                    using var alphaChannel = channels[3];
                    var mask = new Mat();
                    Cv2.Threshold(alphaChannel, mask, 0, 255, ThresholdTypes.Binary);

                    return mask;
                }
                finally
                {
                    if (needDispose)
                    {
                        bgraMat.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                // 发生异常时返回空 Mat
                return new Mat();
            }
        }

    }
}
