//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ {
    public class RectPacker()
    {
        public enum Method {
            BestShortSideFit,   ///< -BSSF: Positions the rectangle against the short side of a free rectangle into which it fits the best.
			BestLongSideFit,    ///< -BLSF: Positions the rectangle against the long side of a free rectangle into which it fits the best.
			BestAreaFit,        ///< -BAF: Positions the rectangle into the smallest free rect into which it fits.
			BottomLeftRule,     ///< -BL: Does the Tetris placement.
			ContactPointRule    ///< -CP: Choosest the placement where the rectangle touches other rects as much as possible.
		};

        private Vector2Int _size = Vector2Int.Zero;
        private readonly List<RectInt> _used = [];
        private readonly List<RectInt> _free = [];

        public RectPacker(int width, int height) : this() {
            Resize(width, height);
        }

        public bool IsEmpty => _used.Count == 0;

        public Vector2Int Size => _size;

        /// <summary>
        /// Returns the rectangle for the given index
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public RectInt GetRect(int index) => _used[index];

        public void Resize(int width, int height) {
            _size.X = width;
            _size.Y = height;

            _used.Clear();
            _free.Clear();
            _free.Add(new RectInt(1, 1, width - 2, height - 2));
        }

        public int Insert(in Vector2Int size, Method method, out RectInt outRect) {
            RectInt rect = RectInt.Zero;
            int score1 = 0;
            int score2 = 0;

            switch (method) {
                case Method.BestShortSideFit:
                    rect = FindPositionForNewNodeBestShortSideFit(size.X, size.Y, ref score1, ref score2);
                    break;
                case Method.BottomLeftRule:
                    rect = FindPositionForNewNodeBottomLeft(size.X, size.Y, ref score1, ref score2);
                    break;
                case Method.ContactPointRule:
                    rect = FindPositionForNewNodeContactPoint(size.X, size.Y, ref score1);
                    break;
                case Method.BestLongSideFit:
                    rect = FindPositionForNewNodeBestLongSideFit(size.X, size.Y, ref score2, ref score1);
                    break;
                case Method.BestAreaFit:
                    rect = FindPositionForNewNodeBestAreaFit(size.X, size.Y, ref score1, ref score2);
                    break;
            }

            outRect = rect;

            if (rect.Height == 0)
                return -1;

            return PlaceRect(rect);
        }

        private int PlaceRect(in RectInt rect) {
            int freeCount = _free.Count;
            for (int i = 0; i < freeCount; ++i) {
                if (SplitFreeNode(_free[i], rect)) {
                    _free.RemoveAt(i);
                    --i;
                    --freeCount;
                }
            }

            PruneFreeList();

            _used.Add(rect);

            return _used.Count - 1;
        }

        private RectInt ScoreRect(in Vector2Int size, Method method, ref int score1, ref int score2) {
            RectInt rect;
            score1 = int.MaxValue;
            score2 = int.MaxValue;

            switch (method) {
                case Method.BestShortSideFit:
                    rect = FindPositionForNewNodeBestShortSideFit(size.X, size.Y, ref score1, ref score2);
                    break;
                case Method.BottomLeftRule:
                    rect = FindPositionForNewNodeBottomLeft(size.X, size.Y, ref score1, ref score2);
                    break;
                case Method.ContactPointRule:
                    rect = FindPositionForNewNodeContactPoint(size.X, size.Y, ref score1);
                    score1 = -score1; // Reverse since we are minimizing, but for contact point score bigger is better.
                    break;
                case Method.BestLongSideFit:
                    rect = FindPositionForNewNodeBestLongSideFit(size.X, size.Y, ref score2, ref score1);
                    break;
                case Method.BestAreaFit:
                    rect = FindPositionForNewNodeBestAreaFit(size.X, size.Y, ref score1, ref score2);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(method), method, null);
            }

            // Cannot fit the current rectangle.
            if (rect.Height != 0)
                return rect;
            
            score1 = int.MaxValue;
            score2 = int.MaxValue;

            return rect;
        }

        /// Computes the ratio of used surface area.
        private float GetOccupancy() {
            ulong area = 0;
            for(int i=0; i<_used.Count; i++)
                area += (ulong)_used[i].Width * (ulong)_used[i].Height;

            return (float)area / (_size.X * _size.Y);
        }

        RectInt FindPositionForNewNodeBottomLeft(int width, int height, ref int bestY, ref int bestX) {
            RectInt rect = RectInt.Zero;

            bestY = int.MaxValue;

            for (int i = 0; i < _free.Count; ++i) {
                // Try to place the rectangle in upright (non-flipped) orientation.
                if (_free[i].Width >= width && _free[i].Height >= height) {
                    int topSideY = _free[i].Y + height;
                    if (topSideY < bestY || (topSideY == bestY && _free[i].X < bestX)) {
                        rect.X = _free[i].X;
                        rect.Y = _free[i].Y;
                        rect.Width = width;
                        rect.Height = height;
                        bestY = topSideY;
                        bestX = _free[i].X;
                    }
                }
                if (_free[i].Width >= height && _free[i].Height >= width) {
                    int topSideY = _free[i].Y + width;
                    if (topSideY < bestY || (topSideY == bestY && _free[i].X < bestX)) {
                        rect.X = _free[i].X;
                        rect.Y = _free[i].Y;
                        rect.Width = width;
                        rect.Height = height;
                        bestY = topSideY;
                        bestX = _free[i].X;
                    }
                }
            }
            return rect;
        }

        RectInt FindPositionForNewNodeBestShortSideFit(
            int width,
            int height,
            ref int bestShortSideFit,
            ref int bestLongSideFit) {

            RectInt rect = RectInt.Zero;

            bestShortSideFit = int.MaxValue;

            for (int i = 0; i < _free.Count; ++i) {
                // Try to place the rectangle in upright (non-flipped) orientation.
                if (_free[i].Width >= width && _free[i].Height >= height) {
                    int leftoverHoriz = Math.Abs(_free[i].Width - width);
                    int leftoverVert = Math.Abs(_free[i].Height - height);
                    int shortSideFit = Math.Min(leftoverHoriz, leftoverVert);
                    int longSideFit = Math.Max(leftoverHoriz, leftoverVert);

                    if (shortSideFit < bestShortSideFit || (shortSideFit == bestShortSideFit && longSideFit < bestLongSideFit)) {
                        rect.X = _free[i].X;
                        rect.Y = _free[i].Y;
                        rect.Width = width;
                        rect.Height = height;
                        bestShortSideFit = shortSideFit;
                        bestLongSideFit = longSideFit;
                    }
                }

                if (_free[i].Width >= height && _free[i].Height >= width) {
                    int flippedLeftoverHoriz = Math.Abs(_free[i].Width - height);
                    int flippedLeftoverVert = Math.Abs(_free[i].Height - width);
                    int flippedShortSideFit = Math.Min(flippedLeftoverHoriz, flippedLeftoverVert);
                    int flippedLongSideFit = Math.Max(flippedLeftoverHoriz, flippedLeftoverVert);

                    if (flippedShortSideFit < bestShortSideFit || (flippedShortSideFit == bestShortSideFit && flippedLongSideFit < bestLongSideFit)) {
                        rect.X = _free[i].X;
                        rect.Y = _free[i].Y;
                        rect.Width = height;
                        rect.Height = width;
                        bestShortSideFit = flippedShortSideFit;
                        bestLongSideFit = flippedLongSideFit;
                    }
                }
            }
            return rect;
        }

        RectInt FindPositionForNewNodeBestLongSideFit(
            int width,
            int height,
            ref int bestShortSideFit,
            ref int bestLongSideFit) {

            RectInt rect = RectInt.Zero;

            bestLongSideFit = int.MaxValue;

            for (int i = 0; i < _free.Count; ++i) {
                // Try to place the rectangle in upright (non-flipped) orientation.
                if (_free[i].Width >= width && _free[i].Height >= height) {
                    int leftoverHoriz = Math.Abs(_free[i].Width - width);
                    int leftoverVert = Math.Abs(_free[i].Height - height);
                    int shortSideFit = Math.Min(leftoverHoriz, leftoverVert);
                    int longSideFit = Math.Max(leftoverHoriz, leftoverVert);

                    if (longSideFit < bestLongSideFit || (longSideFit == bestLongSideFit && shortSideFit < bestShortSideFit)) {
                        rect.X = _free[i].X;
                        rect.Y = _free[i].Y;
                        rect.Width = width;
                        rect.Height = height;
                        bestShortSideFit = shortSideFit;
                        bestLongSideFit = longSideFit;
                    }
                }
                /*
                    if (_free[i].Width >= height && _free[i].Height >= width)
                    {
                        int leftoverHoriz = Math.Abs(_free[i].Width - height);
                        int leftoverVert = Math.Abs(_free[i].Height - width);
                        int shortSideFit = Math.Min(leftoverHoriz, leftoverVert);
                        int longSideFit = Math.Max(leftoverHoriz, leftoverVert);

                        if (longSideFit < bestLongSideFit || (longSideFit == bestLongSideFit && shortSideFit < bestShortSideFit))
                        {
                            rect.X = _free[i].X;
                            rect.Y = _free[i].Y;
                            rect.Width = height;
                            rect.Height = width;
                            bestShortSideFit = shortSideFit;
                            bestLongSideFit = longSideFit;
                        }
                    }
            */

            }
            return rect;
        }

        RectInt FindPositionForNewNodeBestAreaFit(
            int width,
            int height,
            ref int bestAreaFit,
            ref int bestShortSideFit) {

            RectInt rect = RectInt.Zero;

            bestAreaFit = int.MaxValue;

            for (int i = 0; i < _free.Count; ++i) {
                int areaFit = _free[i].Width * _free[i].Height - width * height;

                // Try to place the rectangle in upright (non-flipped) orientation.
                if (_free[i].Width >= width && _free[i].Height >= height) {
                    int leftoverHoriz = Math.Abs(_free[i].Width - width);
                    int leftoverVert = Math.Abs(_free[i].Height - height);
                    int shortSideFit = Math.Min(leftoverHoriz, leftoverVert);

                    if (areaFit < bestAreaFit || (areaFit == bestAreaFit && shortSideFit < bestShortSideFit)) {
                        rect.X = _free[i].X;
                        rect.Y = _free[i].Y;
                        rect.Width = width;
                        rect.Height = height;
                        bestShortSideFit = shortSideFit;
                        bestAreaFit = areaFit;
                    }
                }

                if (_free[i].Width >= height && _free[i].Height >= width) {
                    int leftoverHoriz = Math.Abs(_free[i].Width - height);
                    int leftoverVert = Math.Abs(_free[i].Height - width);
                    int shortSideFit = Math.Min(leftoverHoriz, leftoverVert);

                    if (areaFit < bestAreaFit || (areaFit == bestAreaFit && shortSideFit < bestShortSideFit)) {
                        rect.X = _free[i].X;
                        rect.Y = _free[i].Y;
                        rect.Width = height;
                        rect.Height = width;
                        bestShortSideFit = shortSideFit;
                        bestAreaFit = areaFit;
                    }
                }
            }
            return rect;
        }

        /// Returns 0 if the two intervals i1 and i2 are disjoint, or the length of their overlap otherwise.
        int CommonIntervalLength(int i1start, int i1end, int i2start, int i2end) {
            if (i1end < i2start || i2end < i1start)
                return 0;
            return Math.Min(i1end, i2end) - Math.Max(i1start, i2start);
        }

        private int ContactPointScoreNode(int x, int y, int width, int height) {
            int score = 0;

            if (x == 0 || x + width == _size.X)
                score += height;
            if (y == 0 || y + height == _size.Y)
                score += width;

            for (int i = 0; i < _used.Count; ++i) {
                if (_used[i].X == x + width || _used[i].X + _used[i].Width == x)
                    score += CommonIntervalLength(_used[i].Y, _used[i].Y + _used[i].Height, y, y + height);
                if (_used[i].Y == y + height || _used[i].Y + _used[i].Height == y)
                    score += CommonIntervalLength(_used[i].X, _used[i].X + _used[i].Width, x, x + width);
            }

            return score;
        }

        RectInt FindPositionForNewNodeContactPoint(int width, int height, ref int bestContactScore) {
            RectInt rect = RectInt.Zero;

            bestContactScore = -1;

            for (int i = 0; i < _free.Count; ++i) {
                // Try to place the rectangle in upright (non-flipped) orientation.
                if (_free[i].Width >= width && _free[i].Height >= height) {
                    int score = ContactPointScoreNode(_free[i].X, _free[i].Y, width, height);
                    if (score > bestContactScore) {
                        rect.X = _free[i].X;
                        rect.Y = _free[i].Y;
                        rect.Width = width;
                        rect.Height = height;
                        bestContactScore = score;
                    }
                }
                if (_free[i].Width >= height && _free[i].Height >= width) {
                    int score = ContactPointScoreNode(_free[i].X, _free[i].Y, width, height);
                    if (score > bestContactScore) {
                        rect.X = _free[i].X;
                        rect.Y = _free[i].Y;
                        rect.Width = height;
                        rect.Height = width;
                        bestContactScore = score;
                    }
                }
            }
            return rect;
        }

        bool SplitFreeNode(RectInt freeNode, in RectInt usedNode) {
            // Test with SAT if the rectangles even intersect.
            if (usedNode.X >= freeNode.X + freeNode.Width || usedNode.X + usedNode.Width <= freeNode.X ||
               usedNode.Y >= freeNode.Y + freeNode.Height || usedNode.Y + usedNode.Height <= freeNode.Y)
                return false;

            if (usedNode.X < freeNode.X + freeNode.Width && usedNode.X + usedNode.Width > freeNode.X) {
                // New node at the top side of the used node.
                if (usedNode.Y > freeNode.Y && usedNode.Y < freeNode.Y + freeNode.Height) {
                    RectInt newNode = freeNode;
                    newNode.Height = usedNode.Y - newNode.Y;
                    _free.Add(newNode);
                }

                // New node at the bottom side of the used node.
                if (usedNode.Y + usedNode.Height < freeNode.Y + freeNode.Height) {
                    RectInt newNode = freeNode;
                    newNode.Y = usedNode.Y + usedNode.Height;
                    newNode.Height = freeNode.Y + freeNode.Height - (usedNode.Y + usedNode.Height);
                    _free.Add(newNode);
                }
            }

            if (usedNode.Y < freeNode.Y + freeNode.Height && usedNode.Y + usedNode.Height > freeNode.Y) {
                // New node at the left side of the used node.
                if (usedNode.X > freeNode.X && usedNode.X < freeNode.X + freeNode.Width) {
                    RectInt newNode = freeNode;
                    newNode.Width = usedNode.X - newNode.X;
                    _free.Add(newNode);
                }

                // New node at the right side of the used node.
                if (usedNode.X + usedNode.Width < freeNode.X + freeNode.Width) {
                    RectInt newNode = freeNode;
                    newNode.X = usedNode.X + usedNode.Width;
                    newNode.Width = freeNode.X + freeNode.Width - (usedNode.X + usedNode.Width);
                    _free.Add(newNode);
                }
            }

            return true;
        }

        private bool IsContainedIn(in RectInt a, in RectInt b) {
			return a.X>=b.X && a.Y>=b.Y && a.X+a.Width<=b.X+b.Width && a.Y+a.Height<=b.Y+b.Height;
		}

        private void PruneFreeList() {
            // Remoe redundance rectangles
            for (int i = 0; i < _free.Count; ++i) {
                for (int j = i + 1; j < _free.Count; ++j) {
                    if (IsContainedIn(_free[i], _free[j])) {
                        _free.RemoveAt(i);
                        --i;
                        break;
                    }
                    if (IsContainedIn(_free[j], _free[i])) {
                        _free.RemoveAt(j);
                        --j;
                    }
                }
            }
        }
    }
}
