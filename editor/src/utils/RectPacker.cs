//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor
{
    public class RectPacker()
    {
        private Vector2Int _size = Vector2Int.Zero;
        private readonly List<RectInt> _used = [];
        private readonly List<RectInt> _free = [];

        public RectPacker(int width, int height) : this()
        {
            Resize(width, height);
        }

        public static RectPacker FromRects(in Vector2Int size, IEnumerable<RectInt> rects)
        {
            var packer = new RectPacker();
            packer._size = size;
            packer._free.Add(new RectInt(1, 1, size.X - 2, size.Y - 2));

            foreach (var rect in rects)
            {
                packer._used.Add(rect);

                var freeCount = packer._free.Count;
                for (var i = freeCount - 1; i >= 0; --i)
                {
                    if (packer.SplitFreeNode(packer._free[i], rect))
                        packer.SwapRemoveAt(i);
                }
            }

            packer.PruneFreeList();
            return packer;
        }

        public bool IsEmpty => _used.Count == 0;

        public Vector2Int Size => _size;

        public RectInt GetRect(int index) => _used[index];

        public void Resize(int width, int height)
        {
            _size.X = width;
            _size.Y = height;

            _used.Clear();
            _free.Clear();
            _free.Add(new RectInt(1, 1, width - 2, height - 2));
        }

        public int Insert(in Vector2Int size, out RectInt outRect)
        {
            var rect = FindPosition(size.X, size.Y);
            outRect = rect;

            if (rect.Height == 0)
                return -1;

            return PlaceRect(rect);
        }

        private int PlaceRect(in RectInt rect)
        {
            var freeCount = _free.Count;
            for (var i = freeCount - 1; i >= 0; --i)
            {
                if (SplitFreeNode(_free[i], rect))
                    SwapRemoveAt(i);
            }

            PruneFreeList();
            _used.Add(rect);

            return _used.Count - 1;
        }

        private RectInt FindPosition(int width, int height)
        {
            var rect = RectInt.Zero;
            var bestShortSideFit = int.MaxValue;
            var bestLongSideFit = int.MaxValue;

            for (var i = 0; i < _free.Count; ++i)
            {
                if (_free[i].Width >= width && _free[i].Height >= height)
                {
                    var leftoverHoriz = _free[i].Width - width;
                    var leftoverVert = _free[i].Height - height;
                    var shortSideFit = Math.Min(leftoverHoriz, leftoverVert);
                    var longSideFit = Math.Max(leftoverHoriz, leftoverVert);

                    if (shortSideFit < bestShortSideFit || (shortSideFit == bestShortSideFit && longSideFit < bestLongSideFit))
                    {
                        rect.X = _free[i].X;
                        rect.Y = _free[i].Y;
                        rect.Width = width;
                        rect.Height = height;
                        bestShortSideFit = shortSideFit;
                        bestLongSideFit = longSideFit;
                    }
                }
            }

            return rect;
        }

        private bool SplitFreeNode(RectInt freeNode, in RectInt usedNode)
        {
            if (usedNode.X >= freeNode.X + freeNode.Width || usedNode.X + usedNode.Width <= freeNode.X ||
                usedNode.Y >= freeNode.Y + freeNode.Height || usedNode.Y + usedNode.Height <= freeNode.Y)
                return false;

            if (usedNode.X < freeNode.X + freeNode.Width && usedNode.X + usedNode.Width > freeNode.X)
            {
                if (usedNode.Y > freeNode.Y && usedNode.Y < freeNode.Y + freeNode.Height)
                {
                    var newNode = freeNode;
                    newNode.Height = usedNode.Y - newNode.Y;
                    _free.Add(newNode);
                }

                if (usedNode.Y + usedNode.Height < freeNode.Y + freeNode.Height)
                {
                    var newNode = freeNode;
                    newNode.Y = usedNode.Y + usedNode.Height;
                    newNode.Height = freeNode.Y + freeNode.Height - (usedNode.Y + usedNode.Height);
                    _free.Add(newNode);
                }
            }

            if (usedNode.Y < freeNode.Y + freeNode.Height && usedNode.Y + usedNode.Height > freeNode.Y)
            {
                if (usedNode.X > freeNode.X && usedNode.X < freeNode.X + freeNode.Width)
                {
                    var newNode = freeNode;
                    newNode.Width = usedNode.X - newNode.X;
                    _free.Add(newNode);
                }

                if (usedNode.X + usedNode.Width < freeNode.X + freeNode.Width)
                {
                    var newNode = freeNode;
                    newNode.X = usedNode.X + usedNode.Width;
                    newNode.Width = freeNode.X + freeNode.Width - (usedNode.X + usedNode.Width);
                    _free.Add(newNode);
                }
            }

            return true;
        }

        private void SwapRemoveAt(int index)
        {
            var lastIndex = _free.Count - 1;
            if (index < lastIndex)
                _free[index] = _free[lastIndex];
            _free.RemoveAt(lastIndex);
        }

        private static bool IsContainedIn(in RectInt a, in RectInt b)
        {
            return a.X >= b.X && a.Y >= b.Y && a.X + a.Width <= b.X + b.Width && a.Y + a.Height <= b.Y + b.Height;
        }

        private void PruneFreeList()
        {
            var count = _free.Count;
            Span<bool> removed = count <= 256 ? stackalloc bool[count] : new bool[count];

            for (var i = 0; i < count; ++i)
            {
                if (removed[i]) continue;

                for (var j = i + 1; j < count; ++j)
                {
                    if (removed[j]) continue;

                    if (IsContainedIn(_free[i], _free[j]))
                    {
                        removed[i] = true;
                        break;
                    }
                    if (IsContainedIn(_free[j], _free[i]))
                    {
                        removed[j] = true;
                    }
                }
            }

            var writeIndex = 0;
            for (var i = 0; i < count; ++i)
            {
                if (!removed[i])
                    _free[writeIndex++] = _free[i];
            }
            _free.RemoveRange(writeIndex, count - writeIndex);
        }
    }
}
