using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace FaceEncodingProbe;

/// <summary>
/// Pencatat hasil dan helper assertion harness. Dipisah dari <c>Program.cs</c> supaya
/// logika tiap kasus uji tetap ringkas dan mudah dibaca.
/// </summary>
internal static class Probe
{
    private static readonly List<string> _failures = [];
    private static readonly List<string> _warnings = [];

    private static int _passed;

    public static int Passed => _passed;
    public static int Failed => _failures.Count;
    public static IReadOnlyList<string> Failures => _failures;
    public static IReadOnlyList<string> Warnings => _warnings;

    public static void Banner(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 78));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('=', 78));
    }

    public static void Case(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {title} " + new string('-', Math.Max(0, 74 - title.Length)));
    }

    public static void Info(string label, string value) =>
        Console.WriteLine($"    {label,-10}: {value}");

    /// <summary>
    /// Mencatat satu assertion. Mengembalikan <paramref name="condition"/> supaya bisa
    /// dirangkai dengan <c>&amp;=</c>; sengaja bukan <c>&amp;&amp;</c> agar kegagalan lain
    /// tidak ikut tersembunyi oleh short-circuit.
    /// </summary>
    public static bool Check(string label, bool condition, string detail)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"    [PASS] {label}: {detail}");
        }
        else
        {
            _failures.Add($"{label} -> {detail}");
            Console.WriteLine($"    [FAIL] {label}: {detail}");
        }

        return condition;
    }

    /// <summary>
    /// Observasi yang menarik tapi bukan bagian kontrak keras (mis. rentang nilai encoding).
    /// Tidak mempengaruhi exit code.
    /// </summary>
    public static void Warn(string label, string detail)
    {
        _warnings.Add($"{label}: {detail}");
        Console.WriteLine($"    [WARN] {label}: {detail}");
    }

    /// <summary>
    /// Menjalankan aksi yang diharapkan BERHASIL, lalu memvalidasi seluruh kontrak publik
    /// <c>IFaceRecognitionService</c>: tidak null, tepat 128 nilai, semuanya finite, dan
    /// bukan vektor nol. Mengembalikan vektornya (untuk perbandingan silang antar kasus)
    /// atau null bila ada assertion yang gagal.
    /// </summary>
    public static async Task<List<double>?> ExpectEncodingAsync(string label, Func<Task<List<double>>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        List<double> encoding;

        try
        {
            encoding = await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Check(label, false, $"melempar {ex.GetType().Name} ({stopwatch.ElapsedMilliseconds} ms): {ex.Message}");
            return null;
        }

        stopwatch.Stop();
        Info("waktu", $"{stopwatch.ElapsedMilliseconds} ms");

        var ok = Check(label, encoding is not null, "hasil bukan null");
        if (encoding is null)
            return null;

        ok &= Check(label, encoding.Count == 128, $"Count == 128 (aktual {encoding.Count})");
        ok &= Check(label, encoding.All(double.IsFinite), "semua 128 nilai finite (bukan NaN/Infinity)");

        var absSum = encoding.Sum(Math.Abs);
        ok &= Check(label, absSum > 0, $"bukan vektor nol (sum|x| = {absSum.ToString("F6", CultureInfo.InvariantCulture)})");

        var min = encoding.Min();
        var max = encoding.Max();
        Info("min/max", $"{min.ToString("F6", CultureInfo.InvariantCulture)} / {max.ToString("F6", CultureInfo.InvariantCulture)}");
        Info("6 pertama", string.Join(", ", encoding.Take(6).Select(x => x.ToString("F6", CultureInfo.InvariantCulture))));

        // dlib ResNet v1 secara empiris menghasilkan nilai sekitar -0.35 s.d. +0.40. Batas di
        // bawah sengaja longgar: ini penjaga "nilainya masuk akal", bukan bagian kontrak keras.
        if (Math.Abs(min) >= 1d || Math.Abs(max) >= 1d)
            Warn(label, $"rentang nilai [{Inv(min, 4)}, {Inv(max, 4)}] di luar kebiasaan dlib ResNet (|x| < 1)");

        return ok ? encoding : null;
    }

    /// <summary>
    /// Format angka yang tidak bergantung culture, supaya log harness terbaca sama di mesin
    /// berbahasa Indonesia (yang memakai koma desimal) maupun yang memakai titik.
    /// </summary>
    public static string Inv(double value, int digits = 6) =>
        value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>
    /// Menjalankan aksi yang diharapkan MELEMPAR <typeparamref name="TException"/>.
    /// Exception jenis lain - atau tidak melempar sama sekali - dihitung sebagai kegagalan,
    /// karena itu berarti kontrak error service tidak sesuai yang dijanjikan di XML doc-nya.
    /// </summary>
    public static async Task<TException?> ExpectThrowsAsync<TException>(string label, Func<Task> action)
        where TException : Exception
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await action().ConfigureAwait(false);
            stopwatch.Stop();
            Check(label, false, $"diharapkan {typeof(TException).Name} tapi aksi selesai normal ({stopwatch.ElapsedMilliseconds} ms)");
            return null;
        }
        catch (TException ex)
        {
            stopwatch.Stop();
            Info("waktu", $"{stopwatch.ElapsedMilliseconds} ms");
            Check(label, true, $"{typeof(TException).Name}: {ex.Message}");
            return ex;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Check(label, false, $"diharapkan {typeof(TException).Name}, dapat {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Mencetak ringkasan dan mengembalikan exit code proses (0 = semua lulus).
    /// </summary>
    public static int Summarize()
    {
        Banner("RINGKASAN");
        Console.WriteLine($"    Lulus      : {_passed}");
        Console.WriteLine($"    Gagal      : {_failures.Count}");

        if (_warnings.Count > 0)
        {
            Console.WriteLine($"    Peringatan : {_warnings.Count} (tidak mempengaruhi exit code)");
            foreach (var warning in _warnings)
                Console.WriteLine($"      - {warning}");
        }

        if (_failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("    Kegagalan:");
            foreach (var failure in _failures)
                Console.WriteLine($"      - {failure}");

            Console.WriteLine();
            Console.WriteLine("    HASIL: GAGAL");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("    HASIL: SEMUA KASUS LULUS");
        return 0;
    }
}
