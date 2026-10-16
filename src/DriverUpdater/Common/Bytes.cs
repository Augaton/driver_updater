namespace DriverUpdater.Common;

public static class Bytes
{
    public static string Format(long bytes)
    {
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < 4)
        {
            value /= 1024;
            unit++;
        }
        // "Ko" in French, "KB" in English.
        return unit switch
        {
            0 => L.F("{0} o", bytes),
            1 => L.F("{0:0.#} Ko", value),
            2 => L.F("{0:0.#} Mo", value),
            3 => L.F("{0:0.#} Go", value),
            _ => L.F("{0:0.#} To", value),
        };
    }
}
