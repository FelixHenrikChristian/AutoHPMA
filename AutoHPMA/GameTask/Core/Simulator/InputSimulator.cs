using System.Threading;
using System.Threading.Tasks;
using AutoHPMA.Helpers;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace AutoHPMA.GameTask.Core.Simulator
{
    public class InputSimulator
    {
        private readonly nint _gameHwnd;
        private readonly double _scale;
        private readonly int _offsetX;
        private readonly int _offsetY;

        public InputSimulator(nint gameHwnd, double scale, int offsetX, int offsetY)
        {
            _gameHwnd = gameHwnd;
            _scale = scale;
            _offsetX = offsetX;
            _offsetY = offsetY;
        }

        public async Task ClickAsync(Point location, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await WindowInteractionHelper.SendMouseClickAsync(
                _gameHwnd,
                (uint)(location.X * _scale - _offsetX),
                (uint)(location.Y * _scale - _offsetY),
                token
            );
        }

        public async Task<bool> DragMoveAsync(Point start, Point end, int duration, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await WindowInteractionHelper.SendMouseDragWithNoiseAsync(
                _gameHwnd,
                (uint)(start.X * _scale - _offsetX),
                (uint)(start.Y * _scale - _offsetY),
                (uint)(end.X * _scale - _offsetX),
                (uint)(end.Y * _scale - _offsetY),
                duration,
                token
            );
            return true;
        }

        public async Task SendSpaceAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await WindowInteractionHelper.SendSpaceAsync(_gameHwnd, token);
        }

        public async Task SendEnterAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await WindowInteractionHelper.SendEnterAsync(_gameHwnd, token);
        }

        public async Task SendESCAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await WindowInteractionHelper.SendESCAsync(_gameHwnd, token);
        }
    }
}
