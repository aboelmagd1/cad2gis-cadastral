using System;
using System.ComponentModel;

namespace Cad2Gis.Core.Models;

public enum LayerRole
{
    Boundary,
    PlotText,
    UsageText,
    Dimensions,
    Street,
    Service,
    WaterChannel,
    Ignore
}

public class CadLayerInfo : INotifyPropertyChanged
{
    private string _layerName = string.Empty;
    private int _entityCount;
    private LayerRole _role = LayerRole.Ignore;
    private bool _isSelected = true;
    private string _color = "#FACC15";
    private string _geometryType = "Polygon";
    private string _rawCadText = string.Empty;
    private string _decodedArabicText = string.Empty;
    private string _category = "Main Parcels";

    public string LayerName
    {
        get => _layerName;
        set
        {
            if (_layerName != value)
            {
                _layerName = value;
                OnPropertyChanged(nameof(LayerName));
            }
        }
    }

    public int EntityCount
    {
        get => _entityCount;
        set
        {
            if (_entityCount != value)
            {
                _entityCount = value;
                OnPropertyChanged(nameof(EntityCount));
                OnPropertyChanged(nameof(FormattedCount));
            }
        }
    }

    public string FormattedCount => EntityCount.ToString("N0");

    public LayerRole Role
    {
        get => _role;
        set
        {
            if (_role != value)
            {
                _role = value;
                OnPropertyChanged(nameof(Role));
                Category = DetermineCategory(_role, _layerName);
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    public string Color
    {
        get => _color;
        set
        {
            if (_color != value)
            {
                _color = value;
                OnPropertyChanged(nameof(Color));
            }
        }
    }

    public string GeometryType
    {
        get => _geometryType;
        set
        {
            if (_geometryType != value)
            {
                _geometryType = value;
                OnPropertyChanged(nameof(GeometryType));
            }
        }
    }

    public string RawCadText
    {
        get => _rawCadText;
        set
        {
            if (_rawCadText != value)
            {
                _rawCadText = value;
                OnPropertyChanged(nameof(RawCadText));
            }
        }
    }

    public string DecodedArabicText
    {
        get => _decodedArabicText;
        set
        {
            if (_decodedArabicText != value)
            {
                _decodedArabicText = value;
                OnPropertyChanged(nameof(DecodedArabicText));
            }
        }
    }

    public string Category
    {
        get => _category;
        set
        {
            if (_category != value)
            {
                _category = value;
                OnPropertyChanged(nameof(Category));
            }
        }
    }

    public string SampleText
    {
        get => _rawCadText;
        set => RawCadText = value;
    }

    public static string DetermineCategory(LayerRole role, string layerName)
    {
        switch (role)
        {
            case LayerRole.Boundary:
                return "Main Parcels";
            case LayerRole.Service:
                return "Sub-Parcels / Utilities";
            case LayerRole.PlotText:
            case LayerRole.UsageText:
            case LayerRole.Dimensions:
                return "Text & Annotations";
            case LayerRole.Street:
            case LayerRole.WaterChannel:
                return "Roads & References";
        }

        string l = (layerName ?? "").ToLowerInvariant().Trim();
        string trimmed = l.TrimEnd('.');

        // 1. Text & Numbers & Dimensions
        if (l.Contains("num") || l.Contains("text") || l.Contains("memo") || l.Contains("txt") ||
            l.Contains("رقم") || l.Contains("ارقام") || l.Contains("أرقام") || l.Contains("ترقيم") || l.Contains("نصوص") ||
            l.Contains("dim") || l.Contains("meas") || l.Contains("ابعاد") || l.Contains("أبعاد") || l.Contains("شطف") || l.Contains("كتابة"))
            return "Text & Annotations";

        // 2. Roads & Water Channels
        if (l.Contains("road") || l.Contains("street") || l.Contains("str") || l.Contains("path") || l.Contains("canal") || l.Contains("water") ||
            l.Contains("شوارع") || l.Contains("ممرات") || l.Contains("طريق") || l.Contains("شارع") || l.Contains("قنوات") || l.Contains("تنظيم") || l.Contains("deed"))
            return "Roads & References";

        // 3. Sub-Parcels / Utilities / Services
        if (trimmed == "msq" || trimmed.StartsWith("msq") || trimmed == "kh" || trimmed.StartsWith("kh") ||
            l.Contains("serv") || l.Contains("util") || l.Contains("park") || l.Contains("garden") || l.Contains("mosq") || l.Contains("room") ||
            l.Contains("خدمات") || l.Contains("خدمة") || l.Contains("مرفق") || l.Contains("مرافق") || l.Contains("حدائق") || l.Contains("حديقة") ||
            l.Contains("مساجد") || l.Contains("مسجد") || l.Contains("parking") || l.Contains("مواقف") || l.Contains("حكومي") || l.Contains("hospital") || l.Contains("كهرباء"))
            return "Sub-Parcels / Utilities";

        // 4. Main Parcels & Boundaries
        if (trimmed == "s" || trimmed == "gr" || trimmed == "t" || trimmed == "st" ||
            trimmed.StartsWith("s1") || trimmed.StartsWith("s2") || trimmed.StartsWith("s3") || trimmed.StartsWith("s4") || trimmed.StartsWith("s5") || trimmed.StartsWith("s_") || trimmed.StartsWith("s-") ||
            trimmed.StartsWith("t1") || trimmed.StartsWith("t2") || trimmed.StartsWith("t3") || trimmed.StartsWith("t_") || trimmed.StartsWith("t-") ||
            trimmed.StartsWith("gr_") || trimmed.StartsWith("gr-") || trimmed.StartsWith("ground") ||
            l.Contains("outline") || l.Contains("inner") || l.Contains("outer") || l.Contains("border") ||
            l.Contains("parcel") || l.Contains("plot") || l.Contains("residential") || l.Contains("commercial") || l.Contains("comm") || l.Contains("mixed") ||
            l.Contains("حدود") || l.Contains("قطع") || l.Contains("قطعة") || l.Contains("اراضي") || l.Contains("أراضي") || l.Contains("مخطط") ||
            l.Contains("سكني") || l.Contains("تجاري") || l.Contains("استثماري") || l.Contains("الاوجه") || l.Contains("الاجهه") || l.Contains("واجهات"))
            return "Main Parcels";

        return "Roads & References";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public override string ToString() => $"{LayerName} ({EntityCount} entities, Role: {Role}, Category: {Category})";
}
