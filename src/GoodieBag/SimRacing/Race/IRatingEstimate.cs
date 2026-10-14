namespace GoodieBag.SimRacing.Race;

internal static class IRatingEstimate
{
    private static readonly double Br1 = 1600 / Math.Log(2);

    public static double[] Changes(IReadOnlyList<(int Rating, int Position)> field)
    {
        int n = field.Count;
        var result = new double[n];
        if (n < 2) return result;
        for (int i = 0; i < n; i++)
        {
            double expected = -0.5;
            for (int j = 0; j < n; j++) expected += Chance(field[i].Rating, field[j].Rating);
            double fudge = (n / 2.0 - field[i].Position) / 100;
            result[i] = (n - field[i].Position - expected - fudge) * 200 / n;
        }
        return result;
    }

    private static double Chance(double a, double b)
    {
        double ea = Math.Exp(-a / Br1), eb = Math.Exp(-b / Br1);
        return (1 - ea) * eb / ((1 - eb) * ea + (1 - ea) * eb);
    }
}
