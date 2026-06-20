namespace BaiduAoiExtractor.Win;

internal static class CoordinateConverter
{
    private const double XPi = Math.PI * 3000.0 / 180.0;
    private const double Pi = Math.PI;
    private const double A = 6378245.0;
    private const double Ee = 0.00669342162296594323;

    private static readonly double[] McBand =
    [
        12890594.86, 8362377.87, 5591021.0, 3481989.83, 1678043.12, 0.0
    ];

    private static readonly double[][] Mc2Ll =
    [
        [1.410526172116255e-8, 0.00000898305509648872, -1.9939833816331, 200.9824383106796, -187.2403703815547, 91.6087516669843, -23.38765649603339, 2.57121317296198, -0.03801003308653, 17337981.2],
        [-7.435856389565537e-9, 0.000008983055097726239, -0.78625201886289, 96.32687599759846, -1.85204757529826, -59.36935905485877, 47.40033549296737, -16.50741931063887, 2.28786674699375, 10260144.86],
        [-3.030883460898826e-8, 0.00000898305509983578, 0.30071316287616, 59.74293618442277, 7.357984074871, -25.38371002664745, 13.45380521110908, -3.29883767235584, 0.32710905363475, 6856817.37],
        [-1.981981304930552e-8, 0.000008983055099779535, 0.03278182852591, 40.31678527705744, 0.65659298677277, -4.44255534477492, 0.85341911805263, 0.12923347998204, -0.04625736007561, 4482777.06],
        [3.09191371068437e-9, 0.000008983055096812155, 0.00006995724062, 23.10934304144901, -0.00023663490511, -0.6321817810242, -0.00663494467273, 0.03430082397953, -0.00466043876332, 2555164.4],
        [2.890871144776878e-9, 0.000008983055095805407, -3.068298e-8, 7.47137025468032, -0.00000353937994, -0.02145144861037, -0.00001234426596, 0.00010322952773, -0.00000323890364, 826088.5]
    ];

    public static IReadOnlyList<AoiPoint> Bd09McToWgs84(IReadOnlyList<AoiPoint> bd09McPoints)
    {
        var result = new List<AoiPoint>(bd09McPoints.Count);

        foreach (var point in bd09McPoints)
        {
            var bd09 = Bd09McToBd09(point.X, point.Y);
            var gcj02 = Bd09ToGcj02(bd09.X, bd09.Y);
            var wgs84 = Gcj02ToWgs84(gcj02.X, gcj02.Y);
            result.Add(new AoiPoint(Math.Round(wgs84.X, 6), Math.Round(wgs84.Y, 6)));
        }

        return result;
    }

    public static AoiPoint Bd09ToWgs84(double lon, double lat)
    {
        var gcj02 = Bd09ToGcj02(lon, lat);
        var wgs84 = Gcj02ToWgs84(gcj02.X, gcj02.Y);
        return new AoiPoint(Math.Round(wgs84.X, 6), Math.Round(wgs84.Y, 6));
    }

    private static AoiPoint Bd09McToBd09(double x, double y)
    {
        double[]? coef = null;
        var absY = Math.Abs(y);

        for (var i = 0; i < McBand.Length; i++)
        {
            if (absY >= McBand[i])
            {
                coef = Mc2Ll[i];
                break;
            }
        }

        coef ??= Mc2Ll[^1];
        return Convertor(x, y, coef);
    }

    private static AoiPoint Convertor(double x, double y, IReadOnlyList<double> coef)
    {
        var xTemp = coef[0] + coef[1] * Math.Abs(x);
        var yTemp = Math.Abs(y) / coef[9];
        var yResult = coef[2] +
                      coef[3] * yTemp +
                      coef[4] * Math.Pow(yTemp, 2) +
                      coef[5] * Math.Pow(yTemp, 3) +
                      coef[6] * Math.Pow(yTemp, 4) +
                      coef[7] * Math.Pow(yTemp, 5) +
                      coef[8] * Math.Pow(yTemp, 6);

        xTemp *= x < 0 ? -1 : 1;
        yResult *= y < 0 ? -1 : 1;
        return new AoiPoint(xTemp, yResult);
    }

    private static AoiPoint Bd09ToGcj02(double bdLon, double bdLat)
    {
        var x = bdLon - 0.0065;
        var y = bdLat - 0.006;
        var z = Math.Sqrt(x * x + y * y) - 0.00002 * Math.Sin(y * XPi);
        var theta = Math.Atan2(y, x) - 0.000003 * Math.Cos(x * XPi);
        return new AoiPoint(z * Math.Cos(theta), z * Math.Sin(theta));
    }

    private static AoiPoint Gcj02ToWgs84(double lon, double lat)
    {
        if (OutOfChina(lon, lat))
        {
            return new AoiPoint(lon, lat);
        }

        var dLat = TransformLat(lon - 105.0, lat - 35.0);
        var dLon = TransformLon(lon - 105.0, lat - 35.0);
        var radLat = lat / 180.0 * Pi;
        var magic = Math.Sin(radLat);
        magic = 1 - Ee * magic * magic;
        var sqrtMagic = Math.Sqrt(magic);
        dLat = dLat * 180.0 / ((A * (1 - Ee)) / (magic * sqrtMagic) * Pi);
        dLon = dLon * 180.0 / (A / sqrtMagic * Math.Cos(radLat) * Pi);
        var mgLat = lat + dLat;
        var mgLon = lon + dLon;
        return new AoiPoint(lon * 2 - mgLon, lat * 2 - mgLat);
    }

    private static bool OutOfChina(double lon, double lat)
    {
        return lon < 72.004 || lon > 137.8347 || lat < 0.8293 || lat > 55.8271;
    }

    private static double TransformLat(double x, double y)
    {
        var ret = -100.0 + 2.0 * x + 3.0 * y + 0.2 * y * y + 0.1 * x * y + 0.2 * Math.Sqrt(Math.Abs(x));
        ret += (20.0 * Math.Sin(6.0 * x * Pi) + 20.0 * Math.Sin(2.0 * x * Pi)) * 2.0 / 3.0;
        ret += (20.0 * Math.Sin(y * Pi) + 40.0 * Math.Sin(y / 3.0 * Pi)) * 2.0 / 3.0;
        ret += (160.0 * Math.Sin(y / 12.0 * Pi) + 320.0 * Math.Sin(y * Pi / 30.0)) * 2.0 / 3.0;
        return ret;
    }

    private static double TransformLon(double x, double y)
    {
        var ret = 300.0 + x + 2.0 * y + 0.1 * x * x + 0.1 * x * y + 0.1 * Math.Sqrt(Math.Abs(x));
        ret += (20.0 * Math.Sin(6.0 * x * Pi) + 20.0 * Math.Sin(2.0 * x * Pi)) * 2.0 / 3.0;
        ret += (20.0 * Math.Sin(x * Pi) + 40.0 * Math.Sin(x / 3.0 * Pi)) * 2.0 / 3.0;
        ret += (150.0 * Math.Sin(x / 12.0 * Pi) + 300.0 * Math.Sin(x / 30.0 * Pi)) * 2.0 / 3.0;
        return ret;
    }
}
