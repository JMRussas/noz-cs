//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System;

namespace NoZ.Editor
{
    partial class MSDF
    {
        public static void RenderGlyph(
            TrueTypeFont.Glyph glyph,
            PixelData<byte> output,
            Vector2Int outputPosition,
            Vector2Int outputSize,
            double range,
            in Vector2Double scale,
            in Vector2Double translate
            )
        {
            GenerateSDF(
                output,
                outputPosition,
                outputSize,
                Shape.FromGlyph(glyph, true),
                range,
                scale,
                translate
            );
        }

        private static void GenerateSDF(
            PixelData<byte> output,
            Vector2Int outputPosition,
            Vector2Int outputSize,
            Shape? shape,
            double range,
            Vector2Double scale,
            Vector2Double translate)
        {
            if (shape == null)
                return;

            int contourCount = shape.contours.Length;
            int w = outputSize.X;
            int h = outputSize.Y;

            // Get the windings..
            var windings = new int[contourCount];
            for (int i = 0; i < shape.contours.Length; i++)
                windings[i] = shape.contours[i].Winding();

            var contourSD = new double[contourCount];
            for (int y = 0; y < h; ++y)
            {
                int row = shape.InverseYAxis ? h - y - 1 : y;
                for (int x = 0; x < w; ++x)
                {
                    double dummy = 0;
                    Vector2Double p = new Vector2Double(x + .5, y + .5) / scale - translate;
                    double negDist = -SignedDistance.Infinite.distance;
                    double posDist = SignedDistance.Infinite.distance;
                    int winding = 0;

                    for (int i = 0; i < shape.contours.Length; i++)
                    {
                        SignedDistance minDistance = SignedDistance.Infinite;
                        foreach (var edge in shape.contours[i].edges)
                        {
                            SignedDistance distance = edge.GetSignedDistance(p, out dummy);
                            if (distance < minDistance)
                                minDistance = distance;
                        }
                        contourSD[i] = minDistance.distance;
                        if (windings[i] > 0 && minDistance.distance >= 0 && Math.Abs(minDistance.distance) < Math.Abs(posDist))
                            posDist = minDistance.distance;
                        if (windings[i] < 0 && minDistance.distance <= 0 && Math.Abs(minDistance.distance) < Math.Abs(negDist))
                            negDist = minDistance.distance;
                    }

                    double sd = SignedDistance.Infinite.distance;
                    if (posDist >= 0 && Math.Abs(posDist) <= Math.Abs(negDist))
                    {
                        sd = posDist;
                        winding = 1;
                        for (int i = 0; i < contourCount; ++i)
                            if (windings[i] > 0 && contourSD[i] > sd && Math.Abs(contourSD[i]) < Math.Abs(negDist))
                                sd = contourSD[i];
                    }
                    else if (negDist <= 0 && Math.Abs(negDist) <= Math.Abs(posDist))
                    {
                        sd = negDist;
                        winding = -1;
                        for (int i = 0; i < contourCount; ++i)
                            if (windings[i] < 0 && contourSD[i] < sd && Math.Abs(contourSD[i]) < Math.Abs(posDist))
                                sd = contourSD[i];
                    }
                    for (int i = 0; i < contourCount; ++i)
                        if (windings[i] != winding && Math.Abs(contourSD[i]) < Math.Abs(sd))
                            sd = contourSD[i];

                    // Set the SDF value in the output image (R8 format)
                    sd /= (range * 2.0f);
                    sd = Math.Clamp(sd, -0.5, 0.5) + 0.5;

                    output.Set(
                        x + outputPosition.X,
                        row + outputPosition.Y,
                        (byte)(sd * 255.0f));
                }
            }
        }
    }
}
