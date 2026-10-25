using System;
using System.Collections.Generic;
using System.Linq;

namespace VRFatigue.Signals
{
    internal static class SignalMath
    {
        internal const double Degrees = 180 / Math.PI;
        internal static bool Normalize(Direction3 value, out Direction3 normalized)
        {
            normalized = new Direction3();
            if (!value.IsUsable) return false;
            double scale = Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
            double x = value.X / scale, y = value.Y / scale, z = value.Z / scale;
            double norm = Math.Sqrt(x * x + y * y + z * z);
            normalized = new Direction3(x / norm, y / norm, z / norm);
            return true;
        }
        // Unit vectors in, robust angles even near zero and 180 degrees.
        internal static double Angle(Direction3 a, Direction3 b)
        {
            double x = a.Y * b.Z - a.Z * b.Y, y = a.Z * b.X - a.X * b.Z, z = a.X * b.Y - a.Y * b.X;
            double dot = Math.Max(-1, Math.Min(1, a.X * b.X + a.Y * b.Y + a.Z * b.Z));
            return Math.Atan2(Math.Sqrt(x * x + y * y + z * z), dot) * Degrees;
        }
        internal static double Quantile(IEnumerable<WeightedValue> values, double fraction)
        {
            var sorted = values.OrderBy(v => v.Value).ToArray();
            long total = sorted.Sum(v => v.DurationUs), cumulative = 0;
            double target = fraction * total;
            foreach (var v in sorted) { cumulative += v.DurationUs; if (cumulative >= target) return v.Value; }
            return sorted[sorted.Length - 1].Value;
        }
        internal static double Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            int middle = sorted.Length / 2;
            return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
        }

        // Generalized Savitzky-Golay: quadratic local least squares at actual irregular timestamps.
        // Reorthogonalized QR avoids normal-equation conditioning. No padding or crossing gaps.
        internal static Direction3? Smooth(List<GazePoint> points, int index, int window)
        {
            int start = Math.Max(0, Math.Min(points.Count - window, index - window / 2));
            long origin = points[index].Time;
            double radius = Math.Max(origin - points[start].Time, points[start + window - 1].Time - origin);
            if (radius <= 0) return null;
            var q = new double[window, 3];
            var r = new double[3, 3];
            for (int column = 0; column < 3; column++)
            {
                var v = new double[window];
                for (int row = 0; row < window; row++)
                {
                    double t = (points[start + row].Time - origin) / radius;
                    v[row] = column == 0 ? 1 : column == 1 ? t : t * t;
                }
                for (int pass = 0; pass < 2; pass++)
                for (int previous = 0; previous < column; previous++)
                {
                    double dot = 0;
                    for (int row = 0; row < window; row++) dot += q[row, previous] * v[row];
                    r[previous, column] += dot;
                    for (int row = 0; row < window; row++) v[row] -= dot * q[row, previous];
                }
                double norm = Math.Sqrt(v.Sum(x => x * x));
                if (norm < 1e-10) return null;
                r[column, column] = norm;
                for (int row = 0; row < window; row++) q[row, column] = v[row] / norm;
            }
            var intercepts = new double[3];
            for (int axis = 0; axis < 3; axis++)
            {
                var coefficients = new double[3];
                for (int col = 2; col >= 0; col--)
                {
                    double value = 0;
                    for (int row = 0; row < window; row++)
                    {
                        Direction3 direction = points[start + row].Direction;
                        value += q[row, col] * (axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z);
                    }
                    for (int next = col + 1; next < 3; next++) value -= r[col, next] * coefficients[next];
                    coefficients[col] = value / r[col, col];
                }
                intercepts[axis] = coefficients[0];
            }
            Direction3 result;
            if (intercepts.Sum(v => v * v) < 1e-18) return null;
            return Normalize(new Direction3(intercepts[0], intercepts[1], intercepts[2]), out result) ? (Direction3?)result : null;
        }

        internal static bool Quaternion(HeadPose head, out double[] q)
        {
            q = null;
            if (head == null || !Direction3.Finite(head.PositionMetres.X) || !Direction3.Finite(head.PositionMetres.Y) || !Direction3.Finite(head.PositionMetres.Z)) return false;
            var values = new[] { head.RotationX, head.RotationY, head.RotationZ, head.RotationW };
            if (values.Any(v => !Direction3.Finite(v))) return false;
            double squared = values.Sum(v => v * v);
            if (!Direction3.Finite(squared) || Math.Abs(squared - 1) > 0.001) return false;
            double norm = Math.Sqrt(squared);
            q = values.Select(v => v / norm).ToArray();
            return true;
        }
        internal static double HeadAngle(double[] a, double[] b)
        {
            // b * conjugate(a); q and -q represent the same orientation.
            double x = -b[3] * a[0] + b[0] * a[3] - b[1] * a[2] + b[2] * a[1];
            double y = -b[3] * a[1] + b[0] * a[2] + b[1] * a[3] - b[2] * a[0];
            double z = -b[3] * a[2] - b[0] * a[1] + b[1] * a[0] + b[2] * a[3];
            double w = b[3] * a[3] + b[0] * a[0] + b[1] * a[1] + b[2] * a[2];
            return 2 * Math.Atan2(Math.Sqrt(x * x + y * y + z * z), Math.Abs(w)) * Degrees;
        }
    }
}
