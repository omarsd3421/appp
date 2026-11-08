using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibraryPrintApp;

/// <summary>
/// فئة جاهزة تظهر في خانة «الفئة» بشاشة الماكينة لما تختار خدمة معينة.
/// Label = الاسم اللي يظهر (مثلاً 25)، Amount = السعر على الماكينة (22.5)،
/// SellPrice = سعر البيع للعميل (25). العمولة = SellPrice - Amount.
/// </summary>
internal sealed class PriceTier
{
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal SellPrice { get; set; }
}

/// <summary>
/// الاختيارات اللي تتحكم فيها من «إعدادات الإدارة ← القوائم والاختيارات».
/// بتتحفظ في options.json جنب pricing.json. أي قايمة فاضية بترجع للافتراضي.
/// </summary>
internal sealed class AppOptionsData
{
    public List<string> MachineServicesOPay { get; set; } = AppOptions.DefaultMachineServices();
    public List<string> MachineServicesBee { get; set; } = AppOptions.DefaultMachineServices();

    /// <summary> قديم (قبل فصل الماكينتين) — بيتقرأ مرة واحدة من options.json القديم وبعدين بيتحوّل. </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? MachineServices { get; set; }

    /// <summary> الفئات: المفتاح = «OPay|اسم الخدمة» أو «BEE|اسم الخدمة». </summary>
    public Dictionary<string, List<PriceTier>> PriceTiers { get; set; } = new();
    public List<string> PaymentServices { get; set; } = AppOptions.DefaultPaymentServices();
    public List<string> TreasuryCategories { get; set; } = AppOptions.DefaultTreasuryCategories();

    // الإعدادات الافتراضية المحفوظة للطباعة. صفحات كل ملف جديد تظل صفحة واحدة افتراضيًا.
    public PrintOptions DefaultPrintOptions { get; set; } = new PrintOptions();

    // قوالب طباعة مسماة لتكرار إعدادات شائعة دون إعادة ضبطها كل مرة.
    public Dictionary<string, PrintOptions> PrintTemplates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string ShopName { get; set; } = "مكتبة الشريف";
    public string Phones { get; set; } = "01151808050  -  01098441488";
    public string Address { get; set; } = "أجهور الصغرى — الموقف، أمام المدرسة القديمة وعصائر زمزم";
}

internal static class AppOptions
{
    private static readonly string DirectoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibraryPrintApp");

    private static readonly string FilePath = Path.Combine(DirectoryPath, "options.json");

    private static readonly object Gate = new();
    private static AppOptionsData? cache;

    public static List<string> DefaultMachineServices() => new()
    {
        "كارت شحن", "باقة نت", "شحن رصيد", "شحن كارت كهرباء",
        "دفع تأمينات", "دفع كود فوري باي", "أخرى"
    };

    public static List<string> DefaultPaymentServices() => new()
    {
        "كارت شحن", "باقة نت", "شحن رصيد", "شحن كارت كهرباء",
        "دفع تأمينات", "دفع كود فوري باي", "سحب", "إيداع", "أخرى"
    };

    public static List<string> DefaultTreasuryCategories() => new()
    {
        "إضافة سيولة", "مرتجع كتب / بضائع", "تحصيل مناديب", "فاتورة كهرباء", "فاتورة مياه",
        "فاتورة إنترنت", "شراء بضاعة", "مصروفات", "مرتبات", "صيانة", "سحب شخصي", "أخرى"
    };

    public static AppOptionsData Data
    {
        get
        {
            lock (Gate)
            {
                cache ??= LoadFromDisk();
                return cache;
            }
        }
    }

    public const string OtherService = "أخرى";

    public static IReadOnlyList<string> MachineServicesFor(string machineKey)
        => machineKey == "BEE" ? Data.MachineServicesBee : Data.MachineServicesOPay;

    public static string TierKey(string machineKey, string service) => $"{machineKey}|{service}";

    public static IReadOnlyList<PriceTier> TiersFor(string machineKey, string service)
        => Data.PriceTiers.TryGetValue(TierKey(machineKey, service), out var list)
            ? list
            : Array.Empty<PriceTier>();

    public static string FormatNumber(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static IReadOnlyList<string> PaymentServices => Data.PaymentServices;
    public static IReadOnlyList<string> TreasuryCategories => Data.TreasuryCategories;

    public static PrintOptions DefaultPrintOptions => Data.DefaultPrintOptions.Clone();

    public static void SaveDefaultPrintOptions(PrintOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        var data = CloneForEdit();
        var defaults = options.Clone();
        // لا نحفظ نطاق صفحة خاص بملف بعينه كافتراضي عام؛ كل ملف جديد يبدأ بكل الصفحات.
        defaults.PageRangeMode = PrintPageRangeMode.AllPages;
        defaults.FromPage = 1;
        defaults.ToPage = 1;
        defaults.PageRangesText = string.Empty;
        defaults.ClampToPageCount(1);
        data.DefaultPrintOptions = defaults;
        Save(data);
    }

    public static void ResetDefaultPrintOptions() => SaveDefaultPrintOptions(new PrintOptions());

    public static IReadOnlyList<string> GetPrintTemplateNames()
        => Data.PrintTemplates.Keys.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();

    public static bool TryGetPrintTemplate(string name, out PrintOptions options)
    {
        if (Data.PrintTemplates.TryGetValue((name ?? string.Empty).Trim(), out var stored) && stored != null)
        {
            options = stored.Clone();
            options.PageRangeMode = PrintPageRangeMode.AllPages;
            options.FromPage = 1; options.ToPage = 1; options.PageRangesText = string.Empty;
            return true;
        }
        options = new PrintOptions();
        return false;
    }

    public static void SavePrintTemplate(string name, PrintOptions options)
    {
        string cleanName = (name ?? string.Empty).Trim();
        if (cleanName.Length is < 2 or > 50) throw new InvalidOperationException("اسم القالب يجب أن يكون بين حرفين و50 حرفًا.");
        if (options == null) throw new ArgumentNullException(nameof(options));
        var data = CloneForEdit();
        var copy = options.Clone();
        copy.PageRangeMode = PrintPageRangeMode.AllPages;
        copy.FromPage = 1; copy.ToPage = 1; copy.PageRangesText = string.Empty;
        data.PrintTemplates[cleanName] = copy;
        Save(data);
    }

    public static void DeletePrintTemplate(string name)
    {
        var data = CloneForEdit();
        if (data.PrintTemplates.Remove((name ?? string.Empty).Trim())) Save(data);
    }

    /// <summary> نسخة مستقلة للتعديل عليها قبل الحفظ. </summary>
    public static AppOptionsData CloneForEdit()
    {
        string json = JsonSerializer.Serialize(Data);
        return Normalize(JsonSerializer.Deserialize<AppOptionsData>(json) ?? new AppOptionsData());
    }

    /// <summary>إعادة تحميل الاختيارات من القرص بعد استعادة نسخة احتياطية.</summary>
    public static void Reload()
    {
        lock (Gate) cache = LoadFromDisk();
    }

    public static void Save(AppOptionsData data)
    {
        data = Normalize(data);
        Directory.CreateDirectory(DirectoryPath);
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        File.WriteAllText(FilePath, json);
        lock (Gate) cache = data;
    }

    private static AppOptionsData LoadFromDisk()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppOptionsData();
            var value = JsonSerializer.Deserialize<AppOptionsData>(File.ReadAllText(FilePath));
            return Normalize(value ?? new AppOptionsData());
        }
        catch (Exception ex)
        {
            AppLog.Error("تعذر قراءة options.json — هيتم استخدام الاختيارات الافتراضية", ex);
            return new AppOptionsData();
        }
    }

    private static AppOptionsData Normalize(AppOptionsData d)
    {
        // ترحيل options.json القديم: القايمة الواحدة بتتنسخ للماكينتين مرة واحدة.
        if (d.MachineServices != null)
        {
            d.MachineServicesOPay = new List<string>(d.MachineServices);
            d.MachineServicesBee = new List<string>(d.MachineServices);
            d.MachineServices = null;
        }
        d.MachineServicesOPay = Clean(d.MachineServicesOPay, DefaultMachineServices);
        d.MachineServicesBee = Clean(d.MachineServicesBee, DefaultMachineServices);
        d.PriceTiers = CleanTiers(d);
        d.PaymentServices = Clean(d.PaymentServices, DefaultPaymentServices);
        d.TreasuryCategories = Clean(d.TreasuryCategories, DefaultTreasuryCategories);
        d.DefaultPrintOptions ??= new PrintOptions();
        d.PrintTemplates ??= new Dictionary<string, PrintOptions>(StringComparer.OrdinalIgnoreCase);
        var cleanTemplates = new Dictionary<string, PrintOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in d.PrintTemplates)
        {
            string name = (pair.Key ?? string.Empty).Trim();
            if (name.Length < 2 || pair.Value == null) continue;
            var template = pair.Value.Clone();
            template.PageRangeMode = PrintPageRangeMode.AllPages;
            template.FromPage = 1; template.ToPage = 1; template.PageRangesText = string.Empty;
            cleanTemplates[name] = template;
        }
        d.PrintTemplates = cleanTemplates;
        d.DefaultPrintOptions = d.DefaultPrintOptions.Clone();
        d.DefaultPrintOptions.PageRangeMode = PrintPageRangeMode.AllPages;
        d.DefaultPrintOptions.FromPage = 1;
        d.DefaultPrintOptions.ToPage = 1;
        d.DefaultPrintOptions.PageRangesText = string.Empty;
        d.DefaultPrintOptions.ClampToPageCount(1);
        if (string.IsNullOrWhiteSpace(d.DefaultPrintOptions.PaperName)) d.DefaultPrintOptions.PaperName = "افتراضي";
        if (string.IsNullOrWhiteSpace(d.ShopName)) d.ShopName = new AppOptionsData().ShopName;
        d.ShopName = d.ShopName.Trim();
        d.Phones = (d.Phones ?? string.Empty).Trim();
        d.Address = (d.Address ?? string.Empty).Trim();
        return d;
    }

    private static Dictionary<string, List<PriceTier>> CleanTiers(AppOptionsData d)
    {
        var result = new Dictionary<string, List<PriceTier>>();
        var source = d.PriceTiers ?? new Dictionary<string, List<PriceTier>>();
        foreach (var (machine, services) in new[] { ("OPay", d.MachineServicesOPay), ("BEE", d.MachineServicesBee) })
        {
            foreach (var service in services)
            {
                if (service == OtherService) continue; // «أخرى» اسمها بيتكتب بإيدك، مفيهاش فئات
                if (!source.TryGetValue(TierKey(machine, service), out var tiers) || tiers == null) continue;

                var cleaned = new List<PriceTier>();
                foreach (var t in tiers)
                {
                    if (t == null || t.Amount <= 0) continue;
                    decimal amount = Math.Round(t.Amount, 2, MidpointRounding.AwayFromZero);
                    decimal sell = Math.Round(t.SellPrice, 2, MidpointRounding.AwayFromZero);
                    if (sell < amount) sell = amount;
                    string label = (t.Label ?? string.Empty).Trim();
                    if (label.Length == 0) label = FormatNumber(sell);
                    cleaned.Add(new PriceTier { Label = label, Amount = amount, SellPrice = sell });
                }
                if (cleaned.Count > 0) result[TierKey(machine, service)] = cleaned;
            }
        }
        return result;
    }

    private static List<string> Clean(List<string>? list, Func<List<string>> defaults)
    {
        var cleaned = (list ?? new List<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return cleaned.Count == 0 ? defaults() : cleaned;
    }
}
