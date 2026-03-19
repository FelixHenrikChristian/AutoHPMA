using System.Collections.Generic;
using OpenCvSharp;
using Rect = OpenCvSharp.Rect;
using AutoHPMA.Views.Windows;
using AutoHPMA.GameTask.Model;

namespace AutoHPMA.GameTask.Core.Overlay
{
    public class OverlayService
    {
        private readonly MaskWindow? _maskWindow;

        public OverlayService(MaskWindow? maskWindow)
        {
            _maskWindow = maskWindow;
        }

        public void ShowMatchRects(MatchResult result, int durationMs = 500)
        {
            if (result.Success)
            {
                _maskWindow?.AddTemporaryRects(result.Rects, durationMs: durationMs);
            }
        }

        public void SetStateRects(List<Rect> rects, Dictionary<Rect, string>? textContents = null)
        {
            _maskWindow?.SetTaskStateRects(rects, textContents);
        }

        public void ClearStateRects()
        {
            _maskWindow?.ClearTaskStateRects();
        }
    }
}
