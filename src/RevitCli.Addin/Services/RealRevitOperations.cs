using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCli.Addin.Bridge;
using RevitCli.Shared;
using CliElementFilter = RevitCli.Shared.ElementFilter;

namespace RevitCli.Addin.Services;

public sealed class RealRevitOperations : IRevitOperations
{
    private readonly RevitBridge _bridge;

    public RealRevitOperations(RevitBridge bridge)
    {
        _bridge = bridge;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Shared helpers
    // ═══════════════════════════════════════════════════════════════

    private static Document RequireActiveDocument(UIApplication app)
    {
        return app.ActiveUIDocument?.Document
            ?? throw new InvalidOperationException("No active document is open.");
    }

    private static long ToCliElementId(ElementId elementId) =>
        elementId.Value;

    private const int MaxMatches = 200;

    // ── Element mapping ─────────────────────────────────────────

    private static ElementInfo MapElement(Document doc, Element element)
    {
        return new ElementInfo
        {
            Id = ToCliElementId(element.Id),
            Name = !string.IsNullOrWhiteSpace(element.Name) ? element.Name : element.GetType().Name,
            Category = element.Category?.Name ?? "",
            TypeName = ResolveTypeName(doc, element),
            Parameters = ReadVisibleParameters(doc, element),
            ParameterMetadata = ReadParameterMetadata(doc, element)
        };
    }

    private static string ResolveTypeName(Document doc, Element element)
    {
        if (element is ElementType)
            return element.Name;

        var typeId = element.GetTypeId();
        if (typeId == ElementId.InvalidElementId)
            return "";

        var typeElement = doc.GetElement(typeId) as ElementType;
        return typeElement?.Name ?? "";
    }

    private static Dictionary<string, string> ReadVisibleParameters(Document doc, Element element)
    {
        var result = new Dictionary<string, string>();
        var nameCounts = new Dictionary<string, int>();

        foreach (var param in element.GetOrderedParameters())
        {
            if (!param.HasValue && param.StorageType != StorageType.String)
                continue;

            var value = FormatParameterValue(doc, param);
            if (value == null)
                continue;

            var baseName = param.Definition.Name;

            if (!nameCounts.TryGetValue(baseName, out var count))
            {
                nameCounts[baseName] = 1;
                result[baseName] = value;
            }
            else
            {
                nameCounts[baseName] = count + 1;
                result[$"{baseName} [{count + 1}]"] = value;
            }
        }

        return result;
    }

    private static List<ElementParameterInfo> ReadParameterMetadata(Document doc, Element element)
    {
        var result = new List<ElementParameterInfo>();
        var nameCounts = new Dictionary<string, int>();

        foreach (var param in element.GetOrderedParameters())
        {
            var definitionName = param.Definition?.Name ?? "";
            if (string.IsNullOrWhiteSpace(definitionName))
                continue;

            if (!nameCounts.TryGetValue(definitionName, out var count))
            {
                count = 1;
                nameCounts[definitionName] = count;
            }
            else
            {
                count++;
                nameCounts[definitionName] = count;
            }

            var commandName = count == 1
                ? definitionName
                : $"{definitionName} [{count}]";
            var value = param.HasValue ? FormatParameterValue(doc, param) : null;

            result.Add(new ElementParameterInfo
            {
                Name = commandName,
                DefinitionName = definitionName,
                Value = value,
                StorageType = param.StorageType.ToString(),
                HasValue = !string.IsNullOrWhiteSpace(value),
                IsReadOnly = param.IsReadOnly,
                CanWrite = !param.IsReadOnly && IsSupportedWritableStorageType(param.StorageType)
            });
        }

        return result;
    }

    private static bool IsSupportedWritableStorageType(StorageType storageType) =>
        storageType is StorageType.String or StorageType.Integer or StorageType.Double or StorageType.ElementId;

    private static bool IsViewLockedByTemplateParameter(IEnumerable<ElementParameterInfo> parameters) =>
        parameters.Any(parameter =>
            (string.Equals(parameter.DefinitionName, "View Template", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(parameter.Name, "View Template", StringComparison.OrdinalIgnoreCase)) &&
            !parameter.CanWrite);

    private static string? ResolveWorksetName(Document doc, WorksetId worksetId)
    {
        try
        {
            return doc.GetWorksetTable()?.GetWorkset(worksetId)?.Name;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool CanWriteParameter(Parameter? parameter) =>
        parameter != null && !parameter.IsReadOnly && IsSupportedWritableStorageType(parameter.StorageType);

    private static string? FormatOptionalParameterValue(Document doc, Parameter? parameter) =>
        parameter == null || !parameter.HasValue ? null : FormatParameterValue(doc, parameter);

    private static (bool Exists, string? LastWriteTimeUtc, long? SizeBytes) TryGetLocalFileInfo(string path, bool isCloud)
    {
        if (isCloud || string.IsNullOrWhiteSpace(path))
            return (false, null, null);

        try
        {
            var info = new FileInfo(path);
            return info.Exists
                ? (true, info.LastWriteTimeUtc.ToString("o"), info.Length)
                : (false, null, null);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or UnauthorizedAccessException)
        {
            return (false, null, null);
        }
    }

    private static string FormatXyz(XYZ value) =>
        string.Join(",",
            value.X.ToString("G9", CultureInfo.InvariantCulture),
            value.Y.ToString("G9", CultureInfo.InvariantCulture),
            value.Z.ToString("G9", CultureInfo.InvariantCulture));

    private static string FormatTransformFingerprint(Transform transform) =>
        $"origin={FormatXyz(transform.Origin)};x={FormatXyz(transform.BasisX)};y={FormatXyz(transform.BasisY)};z={FormatXyz(transform.BasisZ)}";

    private static string? FormatParameterValue(Document doc, Parameter parameter)
    {
        var valueString = parameter.AsValueString();
        if (!string.IsNullOrEmpty(valueString))
            return valueString;

        return parameter.StorageType switch
        {
            StorageType.String => parameter.AsString() ?? "",
            StorageType.Integer => parameter.AsInteger().ToString(),
            StorageType.Double => null,
            StorageType.ElementId => FormatElementIdValue(doc, parameter.AsElementId()),
            _ => null
        };
    }

    private static string? FormatElementIdValue(Document doc, ElementId refId)
    {
        if (refId == ElementId.InvalidElementId)
            return null;

        var refElement = doc.GetElement(refId);
        if (refElement != null)
            return $"{refId.Value}: {refElement.Name}";

        return refId.Value.ToString();
    }

    // ── Category resolution ─────────────────────────────────────

    private static readonly Dictionary<string, BuiltInCategory> CategoryAliases = BuildCategoryAliases();

    private static readonly string[] DefaultSnapshotCategories = new[]
    {
        "walls", "doors", "windows", "rooms",
        "floors", "roofs", "stairs", "columns",
        "structuralcolumns", "ceilings", "furniture", "levels"
    };

    private static Dictionary<string, BuiltInCategory> BuildCategoryAliases()
    {
        var map = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(BuiltInCategory cat, params string[] names)
        {
            foreach (var n in names) map[Normalize(n)] = cat;
        }
        Add(BuiltInCategory.OST_Walls, "walls", "wall", "墙", "墙体");
        Add(BuiltInCategory.OST_Doors, "doors", "door", "门");
        Add(BuiltInCategory.OST_Windows, "windows", "window", "窗", "窗户");
        Add(BuiltInCategory.OST_Floors, "floors", "floor", "楼板");
        Add(BuiltInCategory.OST_Roofs, "roofs", "roof", "屋顶");
        Add(BuiltInCategory.OST_Columns, "columns", "column", "柱");
        Add(BuiltInCategory.OST_StructuralColumns, "structuralcolumns", "结构柱");
        Add(BuiltInCategory.OST_Levels, "levels", "level", "标高");
        Add(BuiltInCategory.OST_Rooms, "rooms", "room", "房间");
        Add(BuiltInCategory.OST_Furniture, "furniture", "家具");
        Add(BuiltInCategory.OST_Ceilings, "ceilings", "ceiling", "天花板");
        Add(BuiltInCategory.OST_Stairs, "stairs", "stair", "楼梯");
        return map;
    }

    private static string Normalize(string s) =>
        s.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");

    private static BuiltInCategory ResolveCategory(Document doc, string category)
    {
        var key = Normalize(category);

        if (CategoryAliases.TryGetValue(key, out var resolved))
            return resolved;

        foreach (Category cat in doc.Settings.Categories)
        {
            if (cat.Id.Value < 0 && Normalize(cat.Name) == key)
                return (BuiltInCategory)cat.Id.Value;
        }

        throw new ArgumentException($"Unknown category: '{category}'");
    }

    // ── Parameter lookup (shared by query filter & set) ─────────

    /// <summary>
    /// Find a parameter by name using the same visibility/counting rules as ReadVisibleParameters.
    /// Supports "Foo [2]" duplicate suffix syntax.
    /// </summary>
    private static Parameter? FindParameterByFilterName(Document doc, Element element, string filterName)
    {
        var baseName = filterName;
        var targetIndex = 1;

        var bracketStart = filterName.LastIndexOf('[');
        if (bracketStart > 0 && filterName.EndsWith("]"))
        {
            var indexStr = filterName.Substring(bracketStart + 1, filterName.Length - bracketStart - 2).Trim();
            if (int.TryParse(indexStr, out var parsed) && parsed >= 2)
            {
                baseName = filterName.Substring(0, bracketStart).TrimEnd();
                targetIndex = parsed;
            }
        }

        var count = 0;
        foreach (var param in element.GetOrderedParameters())
        {
            if (!param.HasValue)
                continue;
            if (!string.Equals(param.Definition.Name, baseName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (FormatParameterValue(doc, param) == null)
                continue;
            count++;
            if (count == targetIndex)
                return param;
        }
        return null;
    }

    /// <summary>
    /// Find a parameter for writing. Supports "Foo [2]" duplicate suffix syntax.
    /// Rejects ambiguous names when duplicates exist without explicit index.
    /// Unlike FindParameterByFilterName, does NOT require HasValue (we want to SET values).
    /// </summary>
    private static Parameter? FindWritableParameter(Element element, string paramName)
    {
        var baseName = paramName;
        var targetIndex = 1;
        var explicitIndex = false;

        // Parse "Foo [N]" syntax
        var bracketStart = paramName.LastIndexOf('[');
        if (bracketStart > 0 && paramName.EndsWith("]"))
        {
            var indexStr = paramName.Substring(bracketStart + 1, paramName.Length - bracketStart - 2).Trim();
            if (int.TryParse(indexStr, out var parsed) && parsed >= 2)
            {
                baseName = paramName.Substring(0, bracketStart).TrimEnd();
                targetIndex = parsed;
                explicitIndex = true;
            }
        }

        // First pass: count all matching parameters
        var totalMatches = 0;
        foreach (var param in element.GetOrderedParameters())
        {
            if (string.Equals(param.Definition.Name, baseName, StringComparison.OrdinalIgnoreCase))
                totalMatches++;
        }

        if (totalMatches == 0)
            return null;

        // Reject ambiguous names unless caller used [N] syntax
        if (totalMatches > 1 && !explicitIndex)
            throw new ArgumentException(
                $"Parameter '{baseName}' has {totalMatches} definitions on this element. " +
                $"Use '{baseName} [N]' syntax to disambiguate.");

        // Second pass: return the Nth match
        var count = 0;
        foreach (var param in element.GetOrderedParameters())
        {
            if (!string.Equals(param.Definition.Name, baseName, StringComparison.OrdinalIgnoreCase))
                continue;
            count++;
            if (count == targetIndex)
                return param;
        }

        return null;
    }

    // ── Value coercion for Set ──────────────────────────────────

    /// <summary>
    /// Validate that the value can be coerced to the parameter's storage type
    /// without actually modifying the parameter. Used by dry-run, so it must
    /// exercise every check that <see cref="SetParameterValue"/> would do —
    /// including the display→internal unit conversion for <c>Double</c>
    /// parameters — otherwise dry-run can succeed for inputs that the real
    /// write would reject.
    /// </summary>
    private static void ValidateCoercion(Document doc, Parameter param, string value)
    {
        switch (param.StorageType)
        {
            case StorageType.String:
                break; // Always valid
            case StorageType.Integer:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new ArgumentException(
                        $"Cannot convert '{value}' to integer for parameter '{param.Definition.Name}'.");
                break;
            case StorageType.Double:
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dblVal))
                    throw new ArgumentException(
                        $"Cannot convert '{value}' to number for parameter '{param.Definition.Name}'.");
                // Same unit-conversion check SetParameterValue performs — dry-run
                // must fail in lockstep with the real write, not silently pass.
                ConvertDoubleToInternalUnits(doc, param, dblVal);
                break;
            case StorageType.ElementId:
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new ArgumentException(
                        $"Cannot convert '{value}' to ElementId for parameter '{param.Definition.Name}'.");
                break;
            default:
                throw new ArgumentException(
                    $"Unsupported storage type for parameter '{param.Definition.Name}'.");
        }
    }

    /// <summary>
    /// Map a Double parameter value from display units to Revit's internal
    /// units. Throws <see cref="ArgumentException"/> when the parameter's
    /// display unit cannot be determined — callers must propagate so that
    /// <see cref="ValidateCoercion"/> and <see cref="SetParameterValue"/>
    /// stay in lockstep.
    /// </summary>
    private static double ConvertDoubleToInternalUnits(Document doc, Parameter param, double dblVal)
    {
        try
        {
            var specTypeId = param.Definition.GetDataType();
            var formatOptions = doc.GetUnits().GetFormatOptions(specTypeId);
            var unitTypeId = formatOptions.GetUnitTypeId();
            return UnitUtils.ConvertToInternalUnits(dblVal, unitTypeId);
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException
                                    or InvalidOperationException
                                    or ArgumentException)
        {
            throw new ArgumentException(
                $"Cannot determine display unit for parameter '{param.Definition.Name}': {ex.Message}. " +
                "Pass the value in Revit internal units or fix the project's unit settings.", ex);
        }
    }

    private static void SetParameterValue(Document doc, Parameter param, string value)
    {
        if (param.IsReadOnly)
            throw new InvalidOperationException($"Parameter '{param.Definition.Name}' is read-only.");

        switch (param.StorageType)
        {
            case StorageType.String:
                param.Set(value);
                break;

            case StorageType.Integer:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
                    throw new ArgumentException($"Cannot convert '{value}' to integer for parameter '{param.Definition.Name}'.");
                param.Set(intVal);
                break;

            case StorageType.Double:
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dblVal))
                    throw new ArgumentException($"Cannot convert '{value}' to number for parameter '{param.Definition.Name}'.");
                param.Set(ConvertDoubleToInternalUnits(doc, param, dblVal));
                break;

            case StorageType.ElementId:
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var eidVal))
                    throw new ArgumentException($"Cannot convert '{value}' to ElementId for parameter '{param.Definition.Name}'.");
                param.Set(new ElementId(eidVal));
                break;

            default:
                throw new ArgumentException($"Unsupported storage type for parameter '{param.Definition.Name}'.");
        }
    }

    // ── Filter matching ─────────────────────────────────────────

    private static readonly HashSet<string> PseudoFields = new(StringComparer.OrdinalIgnoreCase)
        { "id", "name", "category", "type", "typename" };

    private static readonly HashSet<string> NumericPseudoFields = new(StringComparer.OrdinalIgnoreCase)
        { "id" };

    private static bool IsNumericOperator(string op) =>
        op is ">" or "<" or ">=" or "<=";

    private static string? GetPseudoFieldValue(Document doc, Element element, string field)
    {
        return field.ToLowerInvariant() switch
        {
            "id" => ToCliElementId(element.Id).ToString(),
            "name" => element.Name,
            "category" => element.Category?.Name,
            "type" or "typename" => ResolveTypeName(doc, element),
            _ => null
        };
    }

    private static bool CompareNumeric(double actual, double compareTo, string op)
    {
        return op switch
        {
            "=" => Math.Abs(actual - compareTo) < 0.001,
            "!=" => Math.Abs(actual - compareTo) >= 0.001,
            ">" => actual > compareTo,
            "<" => actual < compareTo,
            ">=" => actual >= compareTo,
            "<=" => actual <= compareTo,
            _ => false
        };
    }

    // De-dupe stderr warnings for filter unit-conversion failures. Without this,
    // ConvertFilterValueToInternal — invoked once per candidate Element inside
    // MatchesFilter — would emit one warning per element on a large model and
    // flood stderr / slow query and audit paths.
    private static readonly ConcurrentDictionary<string, byte> _filterUnitConversionWarnings = new();

    private static double ConvertFilterValueToInternal(Document doc, Parameter param, double filterValue)
    {
        if (param.StorageType != StorageType.Double)
            return filterValue;

        try
        {
            var specTypeId = param.Definition.GetDataType();
            var formatOptions = doc.GetUnits().GetFormatOptions(specTypeId);
            var unitTypeId = formatOptions.GetUnitTypeId();
            return UnitUtils.ConvertToInternalUnits(filterValue, unitTypeId);
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException
                                    or InvalidOperationException
                                    or ArgumentException)
        {
            // Filter compare against unconvertible parameter — fall back to raw value with a warning
            // so users notice when their filter on a unit-bearing field may not match. Warn at most
            // once per (parameter, filterValue) pair across the lifetime of the addin.
            var key = $"{param.Definition.Name}|{filterValue.ToString("R", CultureInfo.InvariantCulture)}";
            if (_filterUnitConversionWarnings.TryAdd(key, 0))
                Console.Error.WriteLine(
                    $"[RevitCli] Filter on '{param.Definition.Name}' could not be unit-converted ({ex.Message}); " +
                    "comparing against raw internal value.");
            return filterValue;
        }
    }

    private static bool MatchesFilter(Document doc, Element element, CliElementFilter filter,
        double? filterRhsNumeric, ref bool fieldFound)
    {
        if (PseudoFields.Contains(filter.Property))
        {
            fieldFound = true;

            if (IsNumericOperator(filter.Operator) && !NumericPseudoFields.Contains(filter.Property))
                throw new ArgumentException(
                    $"Operator '{filter.Operator}' cannot be used with string field '{filter.Property}'.");

            if (NumericPseudoFields.Contains(filter.Property))
            {
                if (!filterRhsNumeric.HasValue)
                    throw new ArgumentException(
                        $"Field '{filter.Property}' requires a numeric value, got: '{filter.Value}'.");
                return CompareNumeric(ToCliElementId(element.Id), filterRhsNumeric.Value, filter.Operator);
            }

            var val = GetPseudoFieldValue(doc, element, filter.Property);
            if (val == null)
                return false;

            return filter.Operator switch
            {
                "=" => string.Equals(val, filter.Value, StringComparison.OrdinalIgnoreCase),
                "!=" => !string.Equals(val, filter.Value, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        var param = FindParameterByFilterName(doc, element, filter.Property);
        if (param == null)
            return false;

        fieldFound = true;

        if (IsNumericOperator(filter.Operator) &&
            param.StorageType is not (StorageType.Integer or StorageType.Double))
            throw new ArgumentException(
                $"Operator '{filter.Operator}' cannot be used with non-numeric parameter '{filter.Property}'.");

        if (filterRhsNumeric.HasValue &&
            param.StorageType is StorageType.Integer or StorageType.Double)
        {
            double actual = param.StorageType == StorageType.Integer
                ? param.AsInteger()
                : param.AsDouble();

            var compareTo = ConvertFilterValueToInternal(doc, param, filterRhsNumeric.Value);
            return CompareNumeric(actual, compareTo, filter.Operator);
        }

        var displayVal = FormatParameterValue(doc, param);
        if (displayVal == null)
            return false;

        return filter.Operator switch
        {
            "=" => string.Equals(displayVal, filter.Value, StringComparison.OrdinalIgnoreCase),
            "!=" => !string.Equals(displayVal, filter.Value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  status
    // ═══════════════════════════════════════════════════════════════

    private static readonly string AddinVersionString =
        AddinVersionProvider.Current();

    public Task<StatusInfo> GetStatusAsync()
    {
        return _bridge.InvokeAsync(app =>
        {
            var uiDoc = app.ActiveUIDocument;
            var doc = uiDoc?.Document;
            int.TryParse(app.Application.VersionNumber, out var year);
            return new StatusInfo
            {
                RevitVersion = app.Application.VersionNumber,
                RevitYear = year,
                AddinVersion = AddinVersionString,
                DocumentName = doc?.Title,
                DocumentPath = string.IsNullOrWhiteSpace(doc?.PathName) ? null : doc.PathName,
                Capabilities = BuildCapabilities(year)
            };
        });
    }

    internal static List<string> BuildCapabilities(int revitYear)
    {
        var caps = new List<string>
        {
            "status",
            "query",
            "query.filter",
            "query.id",
            "set",
            "set.dry-run",
            "audit",
            "export.dwg",
            "export.ifc"
        };

        // PDF export requires Revit 2022+
        if (revitYear >= 2022)
            caps.Add("export.pdf");

        caps.AddRange(new[]
        {
            "schedule",
            "schedule.list",
            "schedule.export",
            "schedule.create",
            "schedule.create.dry-run",
            "schedules",
            "schedules.ensure.dry-run",
            "schedules.batch-export",
            "views",
            "views.audit",
            "views.template-apply.dry-run",
            "views.clone-set.dry-run",
            "links",
            "links.audit",
            "links.repair",
            "links.repair.dry-run",
            "links.repair.apply",
            "model.map",
            "model.map.check",
            "model.map.fix",
            "model.map.fix.dry-run",
            "model.map.fix.apply",
            "snapshot",
            "snapshot.capture",
            "family",
            "family.list",
            "family.validate",
            "family.purge.dry-run",
            "family.purge.apply",
            "family.export.dry-run",
            "family.export.apply"
        });

        return caps;
    }

    // ═══════════════════════════════════════════════════════════════
    //  query
    // ═══════════════════════════════════════════════════════════════

    public Task<ElementInfo?> GetElementByIdAsync(long id)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id), "Element id must be a positive integer.");

        return _bridge.InvokeAsync<ElementInfo?>(app =>
        {
            var doc = RequireActiveDocument(app);
            var element = doc.GetElement(new ElementId(id));
            if (element == null)
                return null;

            return MapElement(doc, element);
        });
    }

    public Task<ElementInfo[]> QueryElementsAsync(string? category, string? filter)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Category is required.");

        CliElementFilter? parsedFilter = null;
        double? filterRhsNumeric = null;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            parsedFilter = CliElementFilter.Parse(filter);
            if (parsedFilter == null)
                throw new ArgumentException($"Invalid filter expression: '{filter}'");

            if (double.TryParse(parsedFilter.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var num))
                filterRhsNumeric = num;

            if (IsNumericOperator(parsedFilter.Operator) && !filterRhsNumeric.HasValue)
                throw new ArgumentException(
                    $"Operator '{parsedFilter.Operator}' requires a numeric value, got: '{parsedFilter.Value}'");
        }

        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);
            var builtInCat = ResolveCategory(doc, category!);

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfCategory(builtInCat);

            var results = new List<ElementInfo>();
            var sawAnyElement = false;
            var fieldFound = parsedFilter == null;

            foreach (var element in collector)
            {
                sawAnyElement = true;

                if (parsedFilter != null)
                {
                    if (!MatchesFilter(doc, element, parsedFilter, filterRhsNumeric, ref fieldFound))
                        continue;
                }

                if (results.Count >= MaxMatches)
                    throw new InvalidOperationException(
                        $"Query matched more than {MaxMatches} elements. Narrow the category or add --filter.");

                results.Add(MapElement(doc, element));
            }

            if (sawAnyElement && !fieldFound)
                throw new ArgumentException(
                    $"Filter field '{parsedFilter!.Property}' not found on any {category} element.");

            results.Sort((a, b) => a.Id.CompareTo(b.Id));
            return results.ToArray();
        });
    }

    // ═══════════════════════════════════════════════════════════════
    //  set — parameter modification with transaction
    // ═══════════════════════════════════════════════════════════════

    public Task<SetResult> SetParametersAsync(SetRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Param))
            throw new ArgumentException("Parameter name (--param) is required.");

        var hasElementIds = request.ElementIds != null && request.ElementIds.Count > 0;
        if (request.ElementId == null && string.IsNullOrWhiteSpace(request.Category) && !hasElementIds)
            throw new ArgumentException("Provide a category, --id, or --stdin to target elements.");

        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);

            // Step 1: Resolve target elements
            var targets = ResolveSetTargets(doc, request);
            if (targets.Count == 0)
                throw new InvalidOperationException("No elements matched the target criteria.");

            // Step 2: Validate ALL targets — all-or-nothing, fail on any error
            var preview = new List<SetPreviewItem>();

            foreach (var element in targets)
            {
                var param = FindWritableParameter(element, request.Param);
                if (param == null)
                    throw new ArgumentException(
                        $"Element {ToCliElementId(element.Id)} ({element.Name}): parameter '{request.Param}' not found.");

                if (param.IsReadOnly)
                    throw new InvalidOperationException(
                        $"Element {ToCliElementId(element.Id)} ({element.Name}): parameter '{request.Param}' is read-only.");

                // Validate type coercion (and unit conversion for Double) before
                // adding to preview, so dry-run rejects the same inputs SetParameterValue
                // would reject during the actual write.
                ValidateCoercion(doc, param, request.Value);

                var oldValue = FormatParameterValue(doc, param);
                preview.Add(new SetPreviewItem
                {
                    Id = ToCliElementId(element.Id),
                    Name = element.Name ?? element.GetType().Name,
                    OldValue = oldValue,
                    NewValue = request.Value
                });
            }

            // Step 3: Dry-run — return preview without modifying
            if (request.DryRun)
            {
                return new SetResult
                {
                    Affected = preview.Count,
                    Preview = preview
                };
            }

            // Step 4: Real write — single transaction, all-or-nothing
            using var tx = new Transaction(doc, $"RevitCLI Set {request.Param}");
            tx.Start();
            try
            {
                foreach (var item in preview)
                {
                    var element = doc.GetElement(new ElementId(item.Id));
                    var param = FindWritableParameter(element, request.Param)!;
                    SetParameterValue(doc, param, request.Value);
                }

                var commitStatus = tx.Commit();
                if (commitStatus != TransactionStatus.Committed)
                    throw new InvalidOperationException(
                        $"Transaction failed with status: {commitStatus}. Changes were not saved.");
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                throw;
            }

            return new SetResult
            {
                Affected = preview.Count,
                Preview = preview
            };
        });
    }

    private List<Element> ResolveSetTargets(Document doc, SetRequest request)
    {
        // Multiple element IDs (from --stdin pipe)
        if (request.ElementIds != null && request.ElementIds.Count > 0)
        {
            var elementResults = new List<Element>();
            foreach (var eid in request.ElementIds)
            {
                var element = doc.GetElement(new ElementId(eid));
                if (element == null)
                    throw new ArgumentException($"Element with ID {eid} not found.");
                elementResults.Add(element);
            }
            return elementResults;
        }

        // Single element by ID
        if (request.ElementId.HasValue)
        {
            var element = doc.GetElement(new ElementId(request.ElementId.Value));
            if (element == null)
                throw new ArgumentException($"Element with ID {request.ElementId.Value} not found.");
            return new List<Element> { element };
        }

        // By category, optionally filtered
        var builtInCat = ResolveCategory(doc, request.Category!);
        var collector = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfCategory(builtInCat);

        CliElementFilter? parsedFilter = null;
        double? filterRhsNumeric = null;

        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            parsedFilter = CliElementFilter.Parse(request.Filter);
            if (parsedFilter == null)
                throw new ArgumentException($"Invalid filter expression: '{request.Filter}'");

            if (double.TryParse(parsedFilter.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var num))
                filterRhsNumeric = num;
        }

        var results = new List<Element>();
        var fieldFound = parsedFilter == null;

        foreach (var element in collector)
        {
            if (parsedFilter != null)
            {
                if (!MatchesFilter(doc, element, parsedFilter, filterRhsNumeric, ref fieldFound))
                    continue;
            }

            results.Add(element);

            if (results.Count > MaxMatches)
                throw new InvalidOperationException(
                    $"Set target matched more than {MaxMatches} elements. Add --filter to narrow down.");
        }

        return results;
    }

    // ═══════════════════════════════════════════════════════════════
    //  audit — model quality checking
    // ═══════════════════════════════════════════════════════════════

    private static readonly Dictionary<string, Func<Document, List<AuditIssue>>> AuditRuleRegistry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["unplaced-rooms"] = AuditUnplacedRooms,
            ["room-bounds"] = AuditRoomBounds,
            ["level-consistency"] = AuditLevelConsistency,
            ["naming"] = AuditNaming,
            ["views-not-on-sheets"] = AuditViewsNotOnSheets,
            ["imported-dwg"] = AuditImportedDwg,
            ["in-place-families"] = AuditInPlaceFamilies,
            ["duplicate-room-numbers"] = AuditDuplicateRoomNumbers,
            ["room-metadata"] = AuditRoomMetadata,
            ["sheets-missing-info"] = AuditSheetsMissingInfo,
        };

    public Task<AuditResult> RunAuditAsync(AuditRequest request)
    {
        var rulesToRun = request.Rules.Count > 0
            ? request.Rules
            : AuditRuleRegistry.Keys.ToList();

        var unknownRules = rulesToRun.Where(r => !AuditRuleRegistry.ContainsKey(r)).ToList();
        if (unknownRules.Count > 0)
            throw new ArgumentException($"Unknown audit rule(s): {string.Join(", ", unknownRules)}");

        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);

            var allIssues = new List<AuditIssue>();
            var passed = 0;
            var failed = 0;

            foreach (var ruleName in rulesToRun)
            {
                var ruleFunc = AuditRuleRegistry[ruleName];
                var issues = ruleFunc(doc);

                if (issues.Count == 0)
                    passed++;
                else
                    failed++;

                allIssues.AddRange(issues);
            }

            // Run required-parameter checks (server-side, batch)
            foreach (var spec in request.RequiredParameters)
            {
                var issues = AuditRequiredParameter(doc, spec);
                if (issues.Count == 0)
                    passed++;
                else
                    failed++;
                allIssues.AddRange(issues);
            }

            // Run custom naming pattern checks
            foreach (var spec in request.NamingPatterns)
            {
                var issues = AuditNamingPattern(doc, spec);
                if (issues.Count == 0)
                    passed++;
                else
                    failed++;
                allIssues.AddRange(issues);
            }

            return new AuditResult
            {
                Passed = passed,
                Failed = failed,
                Issues = allIssues
            };
        });
    }

    // ── Audit: required-parameter (server-side batch) ───────────

    private static List<AuditIssue> AuditRequiredParameter(Document doc, RequiredParameterSpec spec)
    {
        var issues = new List<AuditIssue>();

        BuiltInCategory builtInCat;
        try { builtInCat = ResolveCategory(doc, spec.Category); }
        catch (ArgumentException ex) { return new List<AuditIssue> { new() {
            Rule = "required-parameter", Severity = "error",
            Message = $"Unknown category '{spec.Category}': {ex.Message}"
        }}; }

        var collector = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfCategory(builtInCat);

        var count = 0;
        foreach (var element in collector)
        {
            // Use case-insensitive ordered scan instead of ambiguous LookupParameter
            Parameter? param = null;
            var matchCount = 0;
            foreach (var p in element.GetOrderedParameters())
            {
                if (string.Equals(p.Definition.Name, spec.Parameter, StringComparison.OrdinalIgnoreCase))
                {
                    param ??= p;
                    matchCount++;
                }
            }
            var missing = param == null;
            var value = "";

            if (param != null)
            {
                if (param.StorageType == StorageType.String)
                {
                    value = param.AsString() ?? "";
                    if (spec.RequireNonEmpty)
                        missing = string.IsNullOrWhiteSpace(value);
                }
                else
                {
                    if (param.HasValue)
                        value = FormatParameterValue(doc, param) ?? "";

                    if (spec.RequireNonEmpty)
                        missing = !param.HasValue;
                }
            }

            if (missing)
            {
                count++;
                if (count <= 20)
                {
                    issues.Add(new AuditIssue
                    {
                        Rule = "required-parameter",
                        Severity = spec.Severity,
                        Message = $"{spec.Category} '{element.Name}' is missing required parameter '{spec.Parameter}'.",
                        ElementId = ToCliElementId(element.Id),
                        Category = spec.Category,
                        Parameter = spec.Parameter,
                        Target = spec.Category,
                        CurrentValue = value,
                        ExpectedValue = spec.RequireNonEmpty ? null : value,
                        Source = "structured"
                    });
                }
            }
        }

        if (count > 20)
        {
            issues.Add(new AuditIssue
            {
                Rule = "required-parameter",
                Severity = "info",
                Message = $"... and {count - 20} more {spec.Category} elements missing '{spec.Parameter}'."
            });
        }

        return issues;
    }

    // ── Audit: custom naming pattern ────────────────────────────

    private static List<AuditIssue> AuditNamingPattern(Document doc, NamingPatternSpec spec)
    {
        var issues = new List<AuditIssue>();
        var regex = new Regex(spec.Pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        var target = spec.Target.ToLowerInvariant();

        IEnumerable<Element> elements;
        if (target is "views" or "view")
        {
            elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .Cast<Element>();
        }
        else if (target is "sheets" or "sheet")
        {
            elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfClass(typeof(ViewSheet))
                .Cast<Element>();
        }
        else
        {
            // Try as category
            try
            {
                var cat = ResolveCategory(doc, spec.Target);
                elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .OfCategory(cat);
            }
            catch
            {
                return new List<AuditIssue> { new() {
                    Rule = "naming-pattern", Severity = "error",
                    Message = $"Unknown naming target: '{spec.Target}'"
                }};
            }
        }

        var count = 0;
        foreach (var element in elements)
        {
            var currentName = element.Name;
            bool matched;
            try
            {
                matched = regex.IsMatch(currentName);
            }
            catch (RegexMatchTimeoutException)
            {
                issues.Add(new AuditIssue
                {
                    Rule = "naming-pattern",
                    Severity = "warning",
                    Message = $"Regex pattern '{spec.Pattern}' timed out on '{currentName}'. Skipping remaining checks.",
                    ElementId = ToCliElementId(element.Id)
                });
                break;
            }

            if (!matched)
            {
                count++;
                if (count <= 50)
                {
                    issues.Add(new AuditIssue
                    {
                        Rule = "naming-pattern",
                        Severity = spec.Severity,
                        Message = $"{spec.Target} '{currentName}' does not match pattern '{spec.Pattern}'.",
                        ElementId = ToCliElementId(element.Id),
                        Category = spec.Target,
                        Parameter = "Name",
                        Target = spec.Target,
                        CurrentValue = currentName,
                        ExpectedValue = null,
                        Source = "structured"
                    });
                }
            }
        }

        if (count > 50)
        {
            issues.Add(new AuditIssue
            {
                Rule = "naming-pattern",
                Severity = "info",
                Message = $"... and {count - 50} more {spec.Target} elements violating naming pattern."
            });
        }

        return issues;
    }

    // ── Audit rule: unplaced-rooms ──────────────────────────────

    private static List<AuditIssue> AuditUnplacedRooms(Document doc)
    {
        var issues = new List<AuditIssue>();
        var rooms = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfCategory(BuiltInCategory.OST_Rooms);

        foreach (var element in rooms)
        {
            if (element.Location == null)
            {
                issues.Add(new AuditIssue
                {
                    Rule = "unplaced-rooms",
                    Severity = "warning",
                    Message = $"Room '{element.Name}' is not placed in the model.",
                    ElementId = ToCliElementId(element.Id)
                });
            }
        }

        return issues;
    }

    // ── Audit rule: room-bounds ─────────────────────────────────

    private static List<AuditIssue> AuditRoomBounds(Document doc)
    {
        var issues = new List<AuditIssue>();
        var rooms = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfCategory(BuiltInCategory.OST_Rooms);

        foreach (var element in rooms)
        {
            if (element.Location == null)
                continue; // Unplaced rooms handled by unplaced-rooms rule

            // Check Area parameter — 0 area means unbounded or redundant
            var areaParam = element.get_Parameter(BuiltInParameter.ROOM_AREA);
            if (areaParam != null && areaParam.AsDouble() <= 0)
            {
                issues.Add(new AuditIssue
                {
                    Rule = "room-bounds",
                    Severity = "error",
                    Message = $"Room '{element.Name}' has zero area — likely not enclosed or redundant.",
                    ElementId = ToCliElementId(element.Id)
                });
            }
        }

        return issues;
    }

    // ── Audit rule: level-consistency ───────────────────────────

    private static List<AuditIssue> AuditLevelConsistency(Document doc)
    {
        var issues = new List<AuditIssue>();
        var levels = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfCategory(BuiltInCategory.OST_Levels)
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();

        if (levels.Count < 2)
            return issues;

        // Check for duplicate elevations
        for (var i = 1; i < levels.Count; i++)
        {
            var prev = levels[i - 1];
            var curr = levels[i];
            var gap = Math.Abs(curr.Elevation - prev.Elevation);

            if (gap < 0.001) // Essentially same elevation
            {
                issues.Add(new AuditIssue
                {
                    Rule = "level-consistency",
                    Severity = "warning",
                    Message = $"Level '{curr.Name}' has same elevation as '{prev.Name}' ({curr.Elevation:F2} ft).",
                    ElementId = ToCliElementId(curr.Id)
                });
            }
        }

        return issues;
    }

    // ── Audit rule: naming ──────────────────────────────────────

    // Known Revit default view name prefixes (English + Chinese + other locales)
    // These are system-generated and should be flagged as default names.
    private static readonly string[] DefaultViewPrefixes =
    {
        // English
        "Floor Plan", "Ceiling Plan", "Structural Plan",
        "Section", "Callout", "Detail", "Elevation",
        "3D View", "Drafting View", "Legend",
        "{3D}", "Copy of",
        // Chinese
        "楼层平面", "天花板平面", "结构平面",
        "剖面", "详图索引", "详图", "立面",
        "三维视图", "起草视图", "图例",
    };

    // System-generated names that are NOT user-fixable (level/grid names used as view names)
    private static readonly HashSet<string> SystemNamePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Level names commonly used as plan view names — these are fine
        "标高", "Level", "Ebene", "Niveau",
        // Grid-based names
        "Grid", "轴网",
    };

    private static bool IsDefaultViewName(string name)
    {
        // Skip system-generated names (level plans, etc.) — these are normal
        foreach (var sys in SystemNamePrefixes)
        {
            if (name.StartsWith(sys, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Check against known default prefixes
        foreach (var prefix in DefaultViewPrefixes)
        {
            // Match "Section 1", "Elevation 2", "3D View 5", etc.
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var remainder = name.Substring(prefix.Length).TrimStart();
                // Exact prefix match (e.g. "{3D}") or prefix + number
                if (remainder.Length == 0 || int.TryParse(remainder, out _))
                    return true;
                // "Copy of X" pattern
                if (prefix == "Copy of")
                    return true;
            }
        }

        return false;
    }

    private static List<AuditIssue> AuditNaming(Document doc)
    {
        var issues = new List<AuditIssue>();

        var views = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate);

        foreach (var view in views)
        {
            if (IsDefaultViewName(view.Name))
            {
                issues.Add(new AuditIssue
                {
                    Rule = "naming",
                    Severity = "info",
                    Message = $"View '{view.Name}' appears to use a default name.",
                    ElementId = ToCliElementId(view.Id)
                });

                if (issues.Count >= 50)
                    break;
            }
        }

        return issues;
    }

    // ── Audit rule: duplicate-room-numbers ─────────────────────

    private static List<AuditIssue> AuditDuplicateRoomNumbers(Document doc)
    {
        var issues = new List<AuditIssue>();
        var rooms = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfCategory(BuiltInCategory.OST_Rooms)
            .Where(r => r.Location != null); // only placed rooms

        var numberGroups = new Dictionary<string, List<(string Name, long Id)>>();

        foreach (var room in rooms)
        {
            var numberParam = room.get_Parameter(BuiltInParameter.ROOM_NUMBER);
            var number = numberParam?.AsString();
            if (string.IsNullOrWhiteSpace(number))
                continue;

            if (!numberGroups.TryGetValue(number, out var group))
            {
                group = new List<(string, long)>();
                numberGroups[number] = group;
            }
            group.Add((room.Name, ToCliElementId(room.Id)));
        }

        foreach (var kvp in numberGroups)
        {
            var number = kvp.Key;
            var group = kvp.Value;
            if (group.Count > 1)
            {
                foreach (var entry in group)
                {
                    var name = entry.Item1;
                    var id = entry.Item2;
                    issues.Add(new AuditIssue
                    {
                        Rule = "duplicate-room-numbers",
                        Severity = "error",
                        Message = $"Room number '{number}' is used by {group.Count} rooms ('{name}').",
                        ElementId = id
                    });
                }

                if (issues.Count >= 50)
                    break;
            }
        }

        return issues;
    }

    // ── Audit rule: room-metadata ───────────────────────────────

    private static List<AuditIssue> AuditRoomMetadata(Document doc)
    {
        var issues = new List<AuditIssue>();
        var rooms = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfCategory(BuiltInCategory.OST_Rooms)
            .Where(r => r.Location != null); // only placed rooms

        foreach (var room in rooms)
        {
            // Check room number
            var numberParam = room.get_Parameter(BuiltInParameter.ROOM_NUMBER);
            if (numberParam == null || string.IsNullOrWhiteSpace(numberParam.AsString()))
            {
                issues.Add(new AuditIssue
                {
                    Rule = "room-metadata",
                    Severity = "warning",
                    Message = $"Room '{room.Name}' has no room number.",
                    ElementId = ToCliElementId(room.Id)
                });
            }

            // Check room name is not default
            if (string.IsNullOrWhiteSpace(room.Name) || room.Name == "Room")
            {
                issues.Add(new AuditIssue
                {
                    Rule = "room-metadata",
                    Severity = "warning",
                    Message = $"Room (ID {ToCliElementId(room.Id)}) has default or empty name.",
                    ElementId = ToCliElementId(room.Id)
                });
            }

            if (issues.Count >= 50)
                break;
        }

        return issues;
    }

    // ── Audit rule: sheets-missing-info ─────────────────────────

    private static List<AuditIssue> AuditSheetsMissingInfo(Document doc)
    {
        var issues = new List<AuditIssue>();
        var sheets = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>();

        foreach (var sheet in sheets)
        {
            // Check sheet number
            if (string.IsNullOrWhiteSpace(sheet.SheetNumber))
            {
                issues.Add(new AuditIssue
                {
                    Rule = "sheets-missing-info",
                    Severity = "error",
                    Message = $"Sheet '{sheet.Name}' has no sheet number.",
                    ElementId = ToCliElementId(sheet.Id)
                });
            }

            // Check sheet has viewports or schedule instances (not empty)
            var viewports = sheet.GetAllViewports();
            var hasViewports = viewports != null && viewports.Count > 0;
            var hasSchedules = new FilteredElementCollector(doc, sheet.Id)
                .OfClass(typeof(ScheduleSheetInstance))
                .Any();
            if (!hasViewports && !hasSchedules)
            {
                issues.Add(new AuditIssue
                {
                    Rule = "sheets-missing-info",
                    Severity = "warning",
                    Message = $"Sheet '{sheet.SheetNumber} - {sheet.Name}' has no views or schedules placed on it.",
                    ElementId = ToCliElementId(sheet.Id)
                });
            }

            if (issues.Count >= 50)
                break;
        }

        return issues;
    }

    // ── Audit rule: views-not-on-sheets ────────────────────────

    private static List<AuditIssue> AuditViewsNotOnSheets(Document doc)
    {
        var issues = new List<AuditIssue>();

        // Collect all placed view IDs (viewports + schedule instances)
        var placedViewIds = new HashSet<ElementId>();

        var viewports = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfClass(typeof(Viewport));
        foreach (Viewport vp in viewports)
            placedViewIds.Add(vp.ViewId);

        // Schedules are placed via ScheduleSheetInstance, not Viewport
        var scheduleInstances = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfClass(typeof(ScheduleSheetInstance));
        foreach (ScheduleSheetInstance ssi in scheduleInstances)
            placedViewIds.Add(ssi.ScheduleId);

        // Check all non-template, non-sheet, printable views
        var views = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate && v is not ViewSheet && v.CanBePrinted);

        foreach (var view in views)
        {
            if (!placedViewIds.Contains(view.Id))
            {
                issues.Add(new AuditIssue
                {
                    Rule = "views-not-on-sheets",
                    Severity = "warning",
                    Message = $"View '{view.Name}' ({view.ViewType}) is not placed on any sheet.",
                    ElementId = ToCliElementId(view.Id)
                });

                if (issues.Count >= 50)
                    break;
            }
        }

        return issues;
    }

    // ── Audit rule: imported-dwg ────────────────────────────────

    private static List<AuditIssue> AuditImportedDwg(Document doc)
    {
        var issues = new List<AuditIssue>();

        var importInstances = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfClass(typeof(ImportInstance));

        foreach (ImportInstance import in importInstances)
        {
            // Linked DWGs are also ImportInstance but with IsLinked = true
            if (!import.IsLinked)
            {
                issues.Add(new AuditIssue
                {
                    Rule = "imported-dwg",
                    Severity = "warning",
                    Message = $"Imported (not linked) CAD file detected: '{import.Name}'. Consider using Link CAD instead.",
                    ElementId = ToCliElementId(import.Id)
                });

                if (issues.Count >= 30)
                    break;
            }
        }

        return issues;
    }

    // ── Audit rule: in-place-families ────────────────────────────

    private static List<AuditIssue> AuditInPlaceFamilies(Document doc)
    {
        var issues = new List<AuditIssue>();

        var familyInstances = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>();

        var seenFamilies = new HashSet<string>();

        foreach (var fi in familyInstances)
        {
            var family = fi.Symbol?.Family;
            if (family != null && family.IsInPlace)
            {
                var familyName = family.Name;
                if (seenFamilies.Add(familyName))
                {
                    issues.Add(new AuditIssue
                    {
                        Rule = "in-place-families",
                        Severity = "warning",
                        Message = $"In-place family '{familyName}' found. Consider converting to a loadable family.",
                        ElementId = ToCliElementId(fi.Id)
                    });

                    if (issues.Count >= 30)
                        break;
                }
            }
        }

        return issues;
    }

    // ═══════════════════════════════════════════════════════════════
    //  export — synchronous view/sheet export
    // ═══════════════════════════════════════════════════════════════

    public Task<ExportProgress> ExportAsync(ExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Format))
            throw new ArgumentException("Export format is required.");

        if (string.IsNullOrWhiteSpace(request.OutputDir))
            throw new ArgumentException("Output directory is required.");

        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);
            var format = request.Format.ToLowerInvariant();
            var taskId = Guid.NewGuid().ToString("N").Substring(0, 8);

            // Ensure output directory exists (skip when dry-run — we don't want to
            // create empty directories on disk for a no-op).
            if (!request.DryRun && !Directory.Exists(request.OutputDir))
                Directory.CreateDirectory(request.OutputDir);

            // IFC exports entire model — no view/sheet targets needed
            if (format == "ifc")
            {
                if (request.DryRun)
                {
                    return new ExportProgress
                    {
                        TaskId = taskId,
                        Status = "completed",
                        Progress = 100,
                        Message = $"Dry run: would export 1 file(s) to {request.OutputDir}"
                    };
                }

                ExportIfc(doc, request.OutputDir);
                return new ExportProgress
                {
                    TaskId = taskId,
                    Status = "completed",
                    Progress = 100,
                    Message = $"Exported model (IFC) to {request.OutputDir}"
                };
            }

            // DWG/PDF require view/sheet targets — resolution runs even on dry-run
            // so callers learn early about empty selectors.
            var targets = ResolveExportTargets(doc, request);
            if (targets.Count == 0)
                throw new InvalidOperationException("No matching views or sheets found.");

            if (request.DryRun)
            {
                return new ExportProgress
                {
                    TaskId = taskId,
                    Status = "completed",
                    Progress = 100,
                    Message = $"Dry run: would export {targets.Count} file(s) to {request.OutputDir}"
                };
            }

            switch (format)
            {
                case "dwg":
                    ExportDwg(doc, targets, request.OutputDir);
                    break;
                case "pdf":
                    ExportPdf(doc, targets, request.OutputDir);
                    break;
                default:
                    throw new ArgumentException($"Unsupported export format: '{request.Format}'");
            }

            return new ExportProgress
            {
                TaskId = taskId,
                Status = "completed",
                Progress = 100,
                Message = $"Exported {targets.Count} view(s) to {request.OutputDir}"
            };
        });
    }

    public Task<ExportProgress> GetExportProgressAsync(string taskId)
    {
        // v1 is synchronous — export is always completed when this is called
        return Task.FromResult(new ExportProgress
        {
            TaskId = taskId,
            Status = "completed",
            Progress = 100
        });
    }

    private static List<View> ResolveExportTargets(Document doc, ExportRequest request)
    {
        var allViews = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate && v.CanBePrinted)
            .ToList();

        var sheetPatterns = request.Sheets ?? new List<string>();
        var viewPatterns = request.Views ?? new List<string>();

        // If no targets specified, export sheets only (not views)
        if (sheetPatterns.Count == 0 && viewPatterns.Count == 0)
        {
            // Check if any sheets exist first
            var hasSheets = allViews.Any(v => v is ViewSheet);
            if (hasSheets)
                sheetPatterns = new List<string> { "all" };
            else
                throw new InvalidOperationException(
                    "No sheets found in document. Use --views to export specific views, or --sheets to specify sheet patterns.");
        }

        var matched = new HashSet<ElementId>();
        var results = new List<View>();

        // Match sheets
        foreach (var pattern in sheetPatterns)
        {
            var isAll = string.Equals(pattern, "all", StringComparison.OrdinalIgnoreCase);
            foreach (var view in allViews)
            {
                if (view is not ViewSheet sheet)
                    continue;
                if (!isAll && !MatchesPattern(sheet.SheetNumber + " - " + sheet.Name, pattern)
                           && !MatchesPattern(sheet.SheetNumber, pattern)
                           && !MatchesPattern(sheet.Name, pattern))
                    continue;

                if (matched.Add(view.Id))
                    results.Add(view);
            }
        }

        // Match views (non-sheet)
        foreach (var pattern in viewPatterns)
        {
            var isAll = string.Equals(pattern, "all", StringComparison.OrdinalIgnoreCase);
            foreach (var view in allViews)
            {
                if (view is ViewSheet)
                    continue; // Sheets handled above
                if (!isAll && !MatchesPattern(view.Name, pattern))
                    continue;

                if (matched.Add(view.Id))
                    results.Add(view);
            }
        }

        return results;
    }

    /// <summary>
    /// Simple wildcard matching: "A1*" matches "A101 - Floor Plan".
    /// Supports * as wildcard only.
    /// </summary>
    private static bool MatchesPattern(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return false;

        if (!pattern.Contains('*'))
            return string.Equals(text, pattern, StringComparison.OrdinalIgnoreCase);

        // Convert simple wildcard to regex
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
    }

    // ── DWG export ──────────────────────────────────────────────

    private static void ExportDwg(Document doc, List<View> views, string outputDir)
    {
        var options = new DWGExportOptions
        {
            MergedViews = true,
            FileVersion = ACADVersion.R2018
        };

        var viewIds = views.Select(v => v.Id).ToList();
        var success = doc.Export(outputDir, "RevitCLI_Export", viewIds, options);
        if (!success)
            throw new InvalidOperationException("DWG export failed. Check that the views are valid and the output directory is writable.");
    }

    // ── PDF export ──────────────────────────────────────────────

    private static void ExportPdf(Document doc, List<View> views, string outputDir)
    {
        var viewIds = views.Select(v => v.Id).ToList();

        var pdfOptions = new PDFExportOptions
        {
            FileName = "RevitCLI_Export",
            Combine = true // Combine all views into single PDF
        };

        var success = doc.Export(outputDir, viewIds, pdfOptions);
        if (!success)
            throw new InvalidOperationException("PDF export failed. Check that the views are printable and the output directory is writable.");
    }

    // ── IFC export ──────────────────────────────────────────────

    private static void ExportIfc(Document doc, string outputDir)
    {
        // IFC exports the entire model, not individual views
        using var tx = new Transaction(doc, "RevitCLI IFC Export");
        tx.Start();
        try
        {
            var ifcOptions = new IFCExportOptions
            {
                FileVersion = IFCVersion.IFC2x3
            };

            var success = doc.Export(outputDir, "RevitCLI_Export.ifc", ifcOptions);

            var commitStatus = tx.Commit();
            if (commitStatus != TransactionStatus.Committed)
                throw new InvalidOperationException(
                    $"IFC export transaction failed with status: {commitStatus}.");

            if (!success)
                throw new InvalidOperationException("IFC export failed. Check that the model is valid and the output directory is writable.");
        }
        catch
        {
            if (tx.GetStatus() == TransactionStatus.Started)
                tx.RollBack();
            throw;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  schedules
    // ═══════════════════════════════════════════════════════════════

    public Task<ScheduleInfo[]> ListSchedulesAsync()
    {
        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);
            var schedules = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => !vs.IsTitleblockRevisionSchedule
                          && !vs.IsTemplate
                          && !vs.Name.StartsWith("<"))
                .Select(vs =>
                {
                    var body = vs.GetTableData()?.GetSectionData(SectionType.Body);
                    var rawRows = body?.NumberOfRows ?? 0;
                    return new ScheduleInfo
                    {
                        Id = ToCliElementId(vs.Id),
                        Name = vs.Name,
                        Category = vs.Definition.CategoryId != ElementId.InvalidElementId
                            ? (Category.GetCategory(doc, vs.Definition.CategoryId)?.Name ?? "")
                            : "",
                        FieldCount = vs.Definition.GetFieldCount(),
                        RowCount = rawRows > 0 ? rawRows - 1 : 0
                    };
                })
                .ToArray();
            return schedules;
        });
    }

    public Task<ScheduleData> ExportScheduleAsync(ScheduleExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ExistingName) && string.IsNullOrWhiteSpace(request.Category))
            throw new ArgumentException("Either ExistingName or Category is required.");

        // Path A — export existing ViewSchedule by name
        if (!string.IsNullOrWhiteSpace(request.ExistingName))
        {
            return _bridge.InvokeAsync(app =>
            {
                var doc = RequireActiveDocument(app);
                var schedule = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(vs =>
                        string.Equals(vs.Name, request.ExistingName, StringComparison.OrdinalIgnoreCase));

                if (schedule == null)
                    throw new InvalidOperationException(
                        $"Schedule '{request.ExistingName}' not found.");

                var columns = new List<string>();
                var visibleColIndices = new List<int>();
                var fieldCount = schedule.Definition.GetFieldCount();
                for (var i = 0; i < fieldCount; i++)
                {
                    var field = schedule.Definition.GetField(i);
                    if (!field.IsHidden)
                    {
                        columns.Add(field.GetName());
                        visibleColIndices.Add(i);
                    }
                }

                var tableData = schedule.GetTableData();
                var bodySection = tableData.GetSectionData(SectionType.Body);
                var rows = new List<Dictionary<string, string>>();
                // Row 0 is the header row in the body section; data starts at row 1
                for (var r = 1; r < bodySection.NumberOfRows; r++)
                {
                    var row = new Dictionary<string, string>();
                    for (var colIdx = 0; colIdx < columns.Count; colIdx++)
                        row[columns[colIdx]] = schedule.GetCellText(SectionType.Body, r, colIdx);
                    rows.Add(row);
                }

                return new ScheduleData
                {
                    Columns = columns,
                    Rows = rows,
                    TotalRows = rows.Count
                };
            });
        }

        // Path B — ad-hoc export: collect elements by category, read params, filter, sort
        // Pre-parse filter outside the Revit thread for early validation
        CliElementFilter? parsedFilter = null;
        double? filterRhsNumeric = null;

        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            parsedFilter = CliElementFilter.Parse(request.Filter);
            if (parsedFilter == null)
                throw new ArgumentException($"Invalid filter expression: '{request.Filter}'");

            if (double.TryParse(parsedFilter.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var num))
                filterRhsNumeric = num;
        }

        const int MaxScheduleRows = 2000;

        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);
            var builtInCat = ResolveCategory(doc, request.Category!);

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfCategory(builtInCat);

            // Collect elements, applying filter if present
            var elements = new List<Element>();
            var sawAnyElement = false;
            var fieldFound = parsedFilter == null;

            foreach (var element in collector)
            {
                sawAnyElement = true;

                if (parsedFilter != null)
                {
                    if (!MatchesFilter(doc, element, parsedFilter, filterRhsNumeric, ref fieldFound))
                        continue;
                }
                elements.Add(element);
            }

            if (sawAnyElement && !fieldFound)
                throw new ArgumentException(
                    $"Filter field '{parsedFilter!.Property}' not found on any {request.Category} element.");

            // Map elements to ElementInfo to access Parameters dictionary
            var mappedElements = elements.Select(e => MapElement(doc, e)).ToList();

            // Determine fields
            var fields = request.Fields;
            if (fields == null || fields.Count == 0)
            {
                fields = new List<string> { "Name", "Level", "Type Name" };
            }
            else if (fields.Count == 1 && string.Equals(fields[0], "all", StringComparison.OrdinalIgnoreCase))
            {
                // Collect all parameter names from all elements, plus top-level pseudo-fields
                var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Name", "Category", "Type Name"
                };
                foreach (var info in mappedElements)
                {
                    foreach (var key in info.Parameters.Keys)
                        allNames.Add(key);
                }
                fields = allNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
            else if (mappedElements.Count > 0)
            {
                // Validate at least one requested field is resolvable — otherwise the caller
                // likely passed the wrong language (e.g. English "Mark" against a Chinese
                // Revit where the parameter is named "标记"). Silently returning an empty
                // table was worse than a clear error.
                var pseudo = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Name", "Category", "Type", "Type Name", "TypeName" };
                var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var info in mappedElements)
                    foreach (var key in info.Parameters.Keys)
                        available.Add(key);

                if (!fields.Any(f => pseudo.Contains(f) || available.Contains(f)))
                {
                    var preview = available.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Take(12).ToList();
                    var more = available.Count > preview.Count ? $" …+{available.Count - preview.Count} more" : "";
                    throw new ArgumentException(
                        $"None of the requested fields ({string.Join(", ", fields)}) exist on {request.Category} elements. " +
                        $"Available parameter names (first {preview.Count}): {string.Join(", ", preview)}{more}");
                }
            }

            // Build rows from element parameters, resolving pseudo-fields first
            var rows = new List<Dictionary<string, string>>();
            foreach (var info in mappedElements)
            {
                var row = new Dictionary<string, string>();
                foreach (var fieldName in fields)
                {
                    var pseudoValue = fieldName.ToLowerInvariant() switch
                    {
                        "name" => info.Name,
                        "category" => info.Category,
                        "type" or "type name" or "typename" => info.TypeName,
                        _ => null
                    };
                    row[fieldName] = pseudoValue
                        ?? (info.Parameters.TryGetValue(fieldName, out var val) ? val : "");
                }
                rows.Add(row);
            }

            // Sort by requested field
            if (!string.IsNullOrWhiteSpace(request.Sort))
            {
                var sortField = request.Sort;
                rows.Sort((a, b) =>
                {
                    a.TryGetValue(sortField, out var va);
                    b.TryGetValue(sortField, out var vb);
                    va ??= "";
                    vb ??= "";

                    // Try numeric comparison first
                    if (double.TryParse(va, NumberStyles.Float, CultureInfo.InvariantCulture, out var na)
                        && double.TryParse(vb, NumberStyles.Float, CultureInfo.InvariantCulture, out var nb))
                    {
                        var cmp = na.CompareTo(nb);
                        return request.SortDescending ? -cmp : cmp;
                    }

                    var strCmp = string.Compare(va, vb, StringComparison.OrdinalIgnoreCase);
                    return request.SortDescending ? -strCmp : strCmp;
                });
            }

            var totalRows = rows.Count;

            // Truncate to MaxScheduleRows
            if (rows.Count > MaxScheduleRows)
                rows = rows.Take(MaxScheduleRows).ToList();

            return new ScheduleData
            {
                Columns = fields.ToList(),
                Rows = rows,
                TotalRows = totalRows
            };
        });
    }

    public Task<ScheduleCreateResult> CreateScheduleAsync(ScheduleCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Schedule name is required.");
        if (string.IsNullOrWhiteSpace(request.Category))
            throw new ArgumentException("Schedule category is required.");

        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);

            // Verify no schedule with same name exists
            var existing = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(vs => string.Equals(vs.Name, request.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                throw new InvalidOperationException($"A schedule named '{request.Name}' already exists.");

            var builtInCat = ResolveCategory(doc, request.Category!);

            ViewSchedule? schedule = null;
            string? placedOnSheet = null;
            using var tx = new Transaction(doc, $"RevitCLI Create Schedule '{request.Name}'");
            tx.Start();
            try
            {
                schedule = ViewSchedule.CreateSchedule(doc, new ElementId(builtInCat));
                schedule.Name = request.Name;

                var definition = schedule.Definition;
                var schedulableFields = definition.GetSchedulableFields();

                // Determine fields to add
                var fieldsToAdd = request.Fields;
                if (fieldsToAdd != null && fieldsToAdd.Count == 1
                    && string.Equals(fieldsToAdd[0], "all", StringComparison.OrdinalIgnoreCase))
                {
                    // Add all schedulable fields
                    fieldsToAdd = schedulableFields.Select(sf => sf.GetName(doc)).ToList();
                }

                // Track sort field id for later
                ScheduleFieldId? sortFieldId = null;

                if (fieldsToAdd != null)
                {
                    foreach (var fieldName in fieldsToAdd)
                    {
                        var match = schedulableFields.FirstOrDefault(sf =>
                            string.Equals(sf.GetName(doc), fieldName, StringComparison.OrdinalIgnoreCase));
                        if (match == null)
                        {
                            var available = string.Join(", ",
                                schedulableFields.Select(sf => sf.GetName(doc)).OrderBy(n => n).Take(20));
                            throw new ArgumentException(
                                $"Field '{fieldName}' not found for category {request.Category}. Available: {available}");
                        }

                        var addedField = definition.AddField(match);

                        // Remember the field id if this is the sort field
                        if (!string.IsNullOrWhiteSpace(request.Sort)
                            && string.Equals(fieldName, request.Sort, StringComparison.OrdinalIgnoreCase))
                        {
                            sortFieldId = addedField.FieldId;
                        }
                    }
                }

                // Validate --filter: not yet supported on schedule create
                if (!string.IsNullOrWhiteSpace(request.Filter))
                    throw new ArgumentException(
                        "--filter on schedule create is not yet supported. Use schedule export --filter instead.");

                // Validate sort field was found among --fields
                if (!string.IsNullOrWhiteSpace(request.Sort) && sortFieldId == null)
                    throw new ArgumentException(
                        $"Sort field '{request.Sort}' must be included in --fields.");

                // Sort support
                if (!string.IsNullOrWhiteSpace(request.Sort) && sortFieldId != null)
                {
                    var sortGroup = new ScheduleSortGroupField(sortFieldId);
                    if (request.SortDescending)
                        sortGroup.SortOrder = ScheduleSortOrder.Descending;
                    definition.AddSortGroupField(sortGroup);
                }

                // Place on sheet
                if (!string.IsNullOrWhiteSpace(request.PlaceOnSheet))
                {
                    var sheets = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .Where(s => MatchesPattern(s.SheetNumber, request.PlaceOnSheet)
                                 || MatchesPattern(s.Name, request.PlaceOnSheet))
                        .ToList();
                    if (sheets.Count == 0)
                        throw new ArgumentException(
                            $"No sheets matching pattern '{request.PlaceOnSheet}'.");
                    var sheet = sheets[0];
                    ScheduleSheetInstance.Create(doc, sheet.Id, schedule.Id, new XYZ(0.1, 0.9, 0));
                    placedOnSheet = $"{sheet.SheetNumber} - {sheet.Name}";
                }

                var commitStatus = tx.Commit();
                if (commitStatus != TransactionStatus.Committed)
                    throw new InvalidOperationException(
                        $"Create schedule transaction failed with status: {commitStatus}.");
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                throw;
            }

            var resultBody = schedule.GetTableData()?.GetSectionData(SectionType.Body);
            var resultRowCount = resultBody?.NumberOfRows ?? 0;

            return new ScheduleCreateResult
            {
                ViewId = ToCliElementId(schedule.Id),
                Name = schedule.Name,
                FieldCount = schedule.Definition.GetFieldCount(),
                RowCount = resultRowCount > 0 ? resultRowCount - 1 : 0,
                PlacedOnSheet = placedOnSheet
            };
        });
    }

    public Task<ViewInfo[]> ListViewsAsync()
    {
        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);
            var placedViewIds = new HashSet<long>();
            foreach (var sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
            {
                foreach (var viewId in sheet.GetAllPlacedViews())
                    placedViewIds.Add(ToCliElementId(viewId));
            }

            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(view => view is not ViewSheet)
                .OrderBy(view => view.Name, StringComparer.OrdinalIgnoreCase)
                .Select(view =>
                {
                    var parameters = ReadVisibleParameters(doc, view);
                    var parameterMetadata = ReadParameterMetadata(doc, view);
                    var templateId = view.ViewTemplateId != ElementId.InvalidElementId
                        ? ToCliElementId(view.ViewTemplateId)
                        : (long?)null;
                    var template = templateId.HasValue
                        ? doc.GetElement(view.ViewTemplateId) as View
                        : null;

                    return new ViewInfo
                    {
                        Id = ToCliElementId(view.Id),
                        Name = view.Name,
                        ViewType = view.ViewType.ToString(),
                        IsTemplate = view.IsTemplate,
                        TemplateId = templateId,
                        TemplateName = template?.Name,
                        CanBePrinted = view.CanBePrinted,
                        IsPlacedOnSheet = placedViewIds.Contains(ToCliElementId(view.Id)),
                        IsLocked = IsViewLockedByTemplateParameter(parameterMetadata),
                        Parameters = parameters
                    };
                })
                .ToArray();
        });
    }

    public Task<LinkInfo[]> ListLinksAsync()
    {
        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .OrderBy(link => link.Name, StringComparer.OrdinalIgnoreCase)
                .Select(link =>
                {
                    var type = doc.GetElement(link.GetTypeId()) as RevitLinkType;
                    var path = "";
                    var status = "";
                    var isCloud = false;
                    try
                    {
                        if (type != null)
                        {
                            var reference = ExternalFileUtils.GetExternalFileReference(doc, type.Id);
                            if (reference != null)
                            {
                                var modelPath = reference.GetPath();
                                path = modelPath == null
                                    ? ""
                                    : ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                                status = reference.GetLinkedFileStatus().ToString();
                                isCloud = path.StartsWith("BIM 360://", StringComparison.OrdinalIgnoreCase) ||
                                          path.StartsWith("Autodesk Docs://", StringComparison.OrdinalIgnoreCase) ||
                                          path.StartsWith("RSN://", StringComparison.OrdinalIgnoreCase);
                            }
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                    {
                        status = "Unknown";
                    }

                    var fileInfo = TryGetLocalFileInfo(path, isCloud);
                    var transform = link.GetTransform();
                    return new LinkInfo
                    {
                        Id = ToCliElementId(link.Id),
                        LinkTypeId = type != null ? ToCliElementId(type.Id) : null,
                        Name = link.Name,
                        TypeName = type?.Name ?? "",
                        Path = path,
                        LinkedFileStatus = status,
                        IsLoaded = link.GetLinkDocument() != null || string.Equals(status, "Loaded", StringComparison.OrdinalIgnoreCase),
                        PathExists = fileInfo.Exists,
                        IsCloud = isCloud,
                        WorksetName = ResolveWorksetName(doc, link.WorksetId),
                        TransformOrigin = FormatXyz(transform.Origin),
                        TransformFingerprint = FormatTransformFingerprint(transform),
                        LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                        SizeBytes = fileInfo.SizeBytes
                    };
                })
                .ToArray();
        });
    }

    public Task<LinkRepairResult> ApplyLinkRepairAsync(LinkRepairRequest request)
    {
        if (request.Actions.Count == 0)
            throw new ArgumentException("At least one link repair action is required.");

        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);
            var result = new LinkRepairResult();
            var validated = new List<(RevitLinkType Type, LinkRepairOperation Operation)>();

            foreach (var action in request.Actions)
            {
                try
                {
                    var type = ResolveLinkType(doc, action);
                    var current = CreateLinkRepairPreview(doc, type, action);
                    ValidateLinkRepairCurrentState(action, current);
                    validated.Add((type, current));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    result.Failures.Add(new CoordinationRepairFailure
                    {
                        Id = action.LinkTypeId ?? action.LinkId,
                        Name = action.LinkName,
                        Code = "link-validation-failed",
                        Message = ex.Message
                    });
                }
            }

            if (result.Failures.Count > 0)
                return result;

            if (request.DryRun)
            {
                result.Affected = validated.Count;
                result.Preview = validated.Select(item => item.Operation).ToList();
                return result;
            }

            foreach (var item in validated)
            {
                try
                {
                    ApplyLinkRepair(item.Type, item.Operation);
                    result.Affected++;
                    result.Preview.Add(item.Operation);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    result.Failures.Add(new CoordinationRepairFailure
                    {
                        Id = item.Operation.LinkTypeId ?? item.Operation.LinkId,
                        Name = item.Operation.LinkName,
                        Code = "link-repair-failed",
                        Message = ex.Message
                    });
                }
            }

            return result;
        });
    }

    public Task<ModelMapElementInfo[]> ListModelMapElementsAsync()
    {
        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);
            var availableWorksets = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .Select(workset => workset.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var availablePhases = doc.Phases
                .Cast<Phase>()
                .Select(phase => phase.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(element => element.Category != null)
                .Where(element => element is not RevitLinkInstance)
                .OrderBy(element => element.Category?.Name ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(element => element.Name, StringComparer.OrdinalIgnoreCase)
                .Select(element =>
                {
                    var worksetParam = element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                    var phaseCreatedParam = element.get_Parameter(BuiltInParameter.PHASE_CREATED);
                    var phaseDemolishedParam = element.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED);
                    return new ModelMapElementInfo
                    {
                        Id = ToCliElementId(element.Id),
                        Name = !string.IsNullOrWhiteSpace(element.Name) ? element.Name : element.GetType().Name,
                        Category = element.Category?.Name ?? "",
                        TypeName = ResolveTypeName(doc, element),
                        WorksetName = ResolveWorksetName(doc, element.WorksetId),
                        PhaseCreated = FormatOptionalParameterValue(doc, phaseCreatedParam),
                        PhaseDemolished = FormatOptionalParameterValue(doc, phaseDemolishedParam),
                        CanWriteWorkset = CanWriteParameter(worksetParam),
                        CanWritePhaseCreated = CanWriteParameter(phaseCreatedParam),
                        CanWritePhaseDemolished = CanWriteParameter(phaseDemolishedParam),
                        AvailableWorksets = availableWorksets,
                        AvailablePhases = availablePhases
                    };
                })
                .ToArray();
        });
    }

    public Task<ModelMapFixResult> ApplyModelMapFixAsync(ModelMapFixRequest request)
    {
        if (request.Actions.Count == 0)
            throw new ArgumentException("At least one model map fix action is required.");

        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);
            var result = new ModelMapFixResult();
            var validated = new List<(Element Element, Parameter Parameter, ElementId TargetId, ModelMapFixOperation Operation)>();

            foreach (var action in request.Actions)
            {
                try
                {
                    var element = doc.GetElement(new ElementId(action.ElementId))
                        ?? throw new ArgumentException($"Element {action.ElementId} was not found.");
                    var parameter = ResolveModelMapParameter(element, action.Field)
                        ?? throw new ArgumentException($"Element {action.ElementId}: field '{action.Field}' is not available.");
                    if (!CanWriteParameter(parameter))
                        throw new InvalidOperationException($"Element {action.ElementId}: field '{action.Field}' is read-only.");

                    var current = ResolveModelMapCurrentValue(doc, element, parameter, action.Field);
                    if (!ValueEquals(current, action.OldValue))
                        throw new InvalidOperationException(
                            $"Element {action.ElementId}: field '{action.Field}' expected '{action.OldValue ?? ""}' but current value is '{current ?? ""}'.");

                    var targetId = ResolveModelMapTargetId(doc, action.Field, action.NewValue);
                    validated.Add((element, parameter, targetId, new ModelMapFixOperation
                    {
                        ElementId = ToCliElementId(element.Id),
                        ElementName = !string.IsNullOrWhiteSpace(element.Name) ? element.Name : element.GetType().Name,
                        Category = element.Category?.Name ?? action.Category,
                        Field = action.Field,
                        OldValue = current,
                        NewValue = action.NewValue
                    }));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    result.Failures.Add(new CoordinationRepairFailure
                    {
                        Id = action.ElementId,
                        Name = action.ElementName,
                        Code = "model-map-validation-failed",
                        Message = ex.Message
                    });
                }
            }

            if (result.Failures.Count > 0)
                return result;

            result.Preview = validated.Select(item => item.Operation).ToList();
            result.Affected = validated.Count;
            if (request.DryRun)
                return result;

            using var tx = new Transaction(doc, "RevitCLI Model Map Fix");
            tx.Start();
            try
            {
                foreach (var item in validated)
                    SetModelMapParameter(item.Parameter, item.Operation.Field, item.TargetId);

                var commitStatus = tx.Commit();
                if (commitStatus != TransactionStatus.Committed)
                    throw new InvalidOperationException(
                        $"Transaction failed with status: {commitStatus}. Changes were not saved.");
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                throw;
            }

            return result;
        });
    }

    private static RevitLinkType ResolveLinkType(Document doc, LinkRepairOperation action)
    {
        if (action.LinkTypeId.HasValue)
        {
            var element = doc.GetElement(new ElementId(action.LinkTypeId.Value));
            if (element is RevitLinkType type)
                return type;
            throw new ArgumentException($"Link type {action.LinkTypeId.Value} was not found.");
        }

        if (action.LinkId > 0)
        {
            var instance = doc.GetElement(new ElementId(action.LinkId)) as RevitLinkInstance
                ?? throw new ArgumentException($"Link instance {action.LinkId} was not found.");
            return doc.GetElement(instance.GetTypeId()) as RevitLinkType
                ?? throw new ArgumentException($"Link instance {action.LinkId} has no Revit link type.");
        }

        var matches = new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkType))
            .Cast<RevitLinkType>()
            .Where(type =>
                string.Equals(type.Name, action.TypeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type.Name, action.LinkName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 1)
            return matches[0];
        throw new ArgumentException($"Link '{action.LinkName}' matched {matches.Length} link type(s).");
    }

    private static LinkRepairOperation CreateLinkRepairPreview(
        Document doc,
        RevitLinkType type,
        LinkRepairOperation action)
    {
        var path = "";
        var status = "";
        var isCloud = false;
        try
        {
            var reference = ExternalFileUtils.GetExternalFileReference(doc, type.Id);
            if (reference != null)
            {
                var modelPath = reference.GetPath();
                path = modelPath == null ? "" : ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                status = reference.GetLinkedFileStatus().ToString();
                isCloud = IsExternallyResolvedLinkPath(path);
            }
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            status = "Unknown";
        }

        var instances = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .Where(instance => instance.GetTypeId() == type.Id)
            .OrderBy(instance => ToCliElementId(instance.Id))
            .ToArray();
        var representative = instances.FirstOrDefault();
        var fileInfo = TryGetLocalFileInfo(path, isCloud);
        return new LinkRepairOperation
        {
            LinkId = representative != null ? ToCliElementId(representative.Id) : action.LinkId,
            LinkTypeId = ToCliElementId(type.Id),
            LinkName = representative?.Name ?? action.LinkName,
            TypeName = type.Name,
            OldPath = path,
            NewPath = action.NewPath,
            OldLoaded = RevitLinkType.IsLoaded(doc, type.Id) || string.Equals(status, "Loaded", StringComparison.OrdinalIgnoreCase),
            NewLoaded = action.NewLoaded,
            OldPathExists = fileInfo.Exists,
            NewPathExists = action.NewPathExists,
            OldPathLastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
            NewPathLastWriteTimeUtc = action.NewPathLastWriteTimeUtc,
            OldPathSizeBytes = fileInfo.SizeBytes,
            NewPathSizeBytes = action.NewPathSizeBytes
        };
    }

    private static void ValidateLinkRepairCurrentState(LinkRepairOperation planned, LinkRepairOperation current)
    {
        if (!PathEquals(current.OldPath, planned.OldPath))
            throw new InvalidOperationException(
                $"Link '{planned.LinkName}' expected old path '{planned.OldPath}' but current path is '{current.OldPath}'.");
        if (current.OldLoaded != planned.OldLoaded)
            throw new InvalidOperationException(
                $"Link '{planned.LinkName}' expected loaded={planned.OldLoaded.ToString().ToLowerInvariant()} but current loaded={current.OldLoaded.ToString().ToLowerInvariant()}.");
        if (!PathEquals(planned.OldPath, planned.NewPath) && IsExternallyResolvedLinkPath(planned.NewPath))
            throw new InvalidOperationException("Cloud/server link path changes are not supported by links repair apply.");
        if (!PathEquals(planned.OldPath, planned.NewPath) && !File.Exists(planned.NewPath))
            throw new InvalidOperationException($"New link path does not exist: '{planned.NewPath}'.");
    }

    private static void ApplyLinkRepair(RevitLinkType type, LinkRepairOperation action)
    {
        var changesPath = !PathEquals(action.OldPath, action.NewPath);
        if (changesPath)
        {
            var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(action.NewPath);
            type.LoadFrom(modelPath, null);
        }
        else if (action.NewLoaded && !action.OldLoaded)
        {
            type.Load();
        }

        if (!action.NewLoaded && (action.OldLoaded || changesPath))
            type.Unload(null);
    }

    private static bool IsExternallyResolvedLinkPath(string path) =>
        path.StartsWith("BIM 360://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("Autodesk Docs://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("RSN://", StringComparison.OrdinalIgnoreCase);

    private static bool PathEquals(string? left, string? right) =>
        string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static Parameter? ResolveModelMapParameter(Element element, string field)
    {
        var builtIn = field switch
        {
            "workset" => BuiltInParameter.ELEM_PARTITION_PARAM,
            "phaseCreated" => BuiltInParameter.PHASE_CREATED,
            "phaseDemolished" => BuiltInParameter.PHASE_DEMOLISHED,
            _ => throw new ArgumentException($"Unsupported model map field '{field}'.")
        };
        return element.get_Parameter(builtIn);
    }

    private static string? ResolveModelMapCurrentValue(Document doc, Element element, Parameter parameter, string field) =>
        string.Equals(field, "workset", StringComparison.OrdinalIgnoreCase)
            ? ResolveWorksetName(doc, element.WorksetId)
            : FormatOptionalParameterValue(doc, parameter);

    private static ElementId ResolveModelMapTargetId(Document doc, string field, string newValue)
    {
        if (string.Equals(field, "workset", StringComparison.OrdinalIgnoreCase))
        {
            var workset = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .FirstOrDefault(item => ValueEquals(item.Name, newValue));
            if (workset == null)
                throw new ArgumentException($"Target workset '{newValue}' was not found.");
            return new ElementId(workset.Id.IntegerValue);
        }

        if (string.Equals(field, "phaseDemolished", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(newValue))
        {
            return ElementId.InvalidElementId;
        }

        var phase = doc.Phases
            .Cast<Phase>()
            .FirstOrDefault(item => ValueEquals(item.Name, newValue) || ValueEquals($"{ToCliElementId(item.Id)}: {item.Name}", newValue));
        if (phase == null)
            throw new ArgumentException($"Target phase '{newValue}' was not found.");
        return phase.Id;
    }

    private static void SetModelMapParameter(Parameter parameter, string field, ElementId targetId)
    {
        if (parameter.StorageType == StorageType.ElementId)
        {
            parameter.Set(targetId);
            return;
        }

        if (parameter.StorageType == StorageType.Integer)
        {
            var value = checked((int)targetId.Value);
            parameter.Set(value);
            return;
        }

        throw new ArgumentException(
            $"Unsupported storage type '{parameter.StorageType}' for model map field '{field}'.");
    }

    private static bool ValueEquals(string? left, string? right) =>
        string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    public Task<ModelSnapshot> CaptureSnapshotAsync(SnapshotRequest request)
    {
        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);

            var snapshot = new ModelSnapshot
            {
                SchemaVersion = 1,
                TakenAt = DateTime.UtcNow.ToString("o"),
                Revit = new SnapshotRevit
                {
                    Version = app.Application.VersionNumber ?? "",
                    Document = string.IsNullOrEmpty(doc.Title)
                        ? "" : System.IO.Path.GetFileNameWithoutExtension(doc.Title),
                    DocumentPath = doc.PathName ?? ""
                },
                Model = new SnapshotModel { SizeBytes = 0, FileHash = "" }
            };

            // Elements
            var requested = request.IncludeCategories ?? new List<string>(DefaultSnapshotCategories);
            foreach (var catName in requested)
            {
                BuiltInCategory bic;
                try { bic = ResolveCategory(doc, catName); }
                catch (ArgumentException) { continue; }

                var items = new List<SnapshotElement>();
                foreach (var element in new FilteredElementCollector(doc)
                    .OfCategory(bic).WhereElementIsNotElementType())
                {
                    if (request.SummaryOnly)
                    {
                        items.Add(new SnapshotElement { Id = ToCliElementId(element.Id) });
                    }
                    else
                    {
                        var info = MapElement(doc, element);
                        var snap = new SnapshotElement
                        {
                            Id = ToCliElementId(element.Id),
                            Name = info.Name ?? "",
                            TypeName = info.TypeName ?? "",
                            Parameters = new Dictionary<string, string>(info.Parameters)
                        };
                        snap.Hash = SnapshotHasher.HashElement(snap);
                        items.Add(snap);
                    }
                }
                // Normalize category key so `--categories structural_columns` and the
                // default `structuralcolumns` produce the same dictionary key, keeping
                // diff stable across snapshots with different input casing/separators.
                snapshot.Categories[Normalize(catName)] = items;
            }

            // Build an element-hash index by Id for ContentHash computation below.
            // SummaryOnly elements have empty Hash; SummaryOnly short-circuits sheets
            // anyway, so the index is only read in the full-snapshot path.
            var elementHashById = new Dictionary<long, string>();
            foreach (var kv in snapshot.Categories)
            {
                foreach (var el in kv.Value)
                    elementHashById[el.Id] = el.Hash;
            }

            // Sheets
            if (request.IncludeSheets)
            {
                foreach (var sheet in new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
                {
                    if (request.SummaryOnly)
                    {
                        // Fast path: count only, no parameter reads or placed-view resolution.
                        snapshot.Sheets.Add(new SnapshotSheet { ViewId = ToCliElementId(sheet.Id) });
                        continue;
                    }

                    var placedIds = new List<long>();
                    try
                    {
                        foreach (var viewId in sheet.GetAllPlacedViews())
                            placedIds.Add(ToCliElementId(viewId));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[RevitCli] GetAllPlacedViews failed on sheet {sheet.SheetNumber}: {ex.Message}");
                    }

                    var sheetSnap = new SnapshotSheet
                    {
                        Number = sheet.SheetNumber ?? "",
                        Name = sheet.Name ?? "",
                        ViewId = ToCliElementId(sheet.Id),
                        PlacedViewIds = placedIds,
                        Parameters = ReadVisibleParameters(doc, sheet)
                    };
                    sheetSnap.MetaHash = SnapshotHasher.HashSheetMeta(sheetSnap);
                    sheetSnap.ContentHash = ComputeSheetContentHash(
                        doc, sheetSnap.MetaHash, placedIds, elementHashById);
                    snapshot.Sheets.Add(sheetSnap);
                }
            }

            // Schedules
            if (request.IncludeSchedules)
            {
                foreach (var vs in new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>())
                {
                    if (vs.IsTitleblockRevisionSchedule) continue;

                    if (request.SummaryOnly)
                    {
                        // Fast path: count only.
                        snapshot.Schedules.Add(new SnapshotSchedule { Id = ToCliElementId(vs.Id) });
                        continue;
                    }

                    // Some schedules (key schedules, note blocks) return null table data.
                    var tableData = vs.GetTableData();
                    if (tableData == null) continue;
                    var bodySection = tableData.GetSectionData(SectionType.Body);
                    if (bodySection == null) continue;

                    var columns = new List<string>();
                    var fieldCount = vs.Definition.GetFieldCount();
                    for (var i = 0; i < fieldCount; i++)
                    {
                        var f = vs.Definition.GetField(i);
                        if (!f.IsHidden) columns.Add(f.GetName());
                    }

                    var rows = new List<Dictionary<string, string>>();
                    for (var r = 1; r < bodySection.NumberOfRows; r++)
                    {
                        var row = new Dictionary<string, string>();
                        for (var c = 0; c < columns.Count; c++)
                            row[columns[c]] = vs.GetCellText(SectionType.Body, r, c);
                        rows.Add(row);
                    }

                    var cat = "";
                    try { cat = vs.Definition.CategoryId is { } catId
                        ? Category.GetCategory(doc, catId)?.Name ?? "" : ""; }
                    catch { cat = ""; }

                    snapshot.Schedules.Add(new SnapshotSchedule
                    {
                        Id = ToCliElementId(vs.Id),
                        Name = vs.Name ?? "",
                        Category = cat,
                        RowCount = rows.Count,
                        Hash = SnapshotHasher.HashSchedule(cat, vs.Name ?? "", columns, rows)
                    });
                }
            }

            // Summary — counts are now accurate in SummaryOnly mode because the
            // sheets/schedules loops above run with a count-only fast path.
            snapshot.Summary = new SnapshotSummary
            {
                SheetCount = snapshot.Sheets.Count,
                ScheduleCount = snapshot.Schedules.Count
            };
            foreach (var kv in snapshot.Categories)
                snapshot.Summary.ElementCounts[kv.Key] = kv.Value.Count;

            // If SummaryOnly, clear bulky lists so the snapshot is light
            if (request.SummaryOnly)
            {
                // Keep counts; drop element lists, sheets, schedules
                foreach (var key in new List<string>(snapshot.Categories.Keys))
                    snapshot.Categories[key] = new List<SnapshotElement>();
                snapshot.Sheets = new List<SnapshotSheet>();
                snapshot.Schedules = new List<SnapshotSchedule>();
            }

            return snapshot;
        });
    }

    /// <summary>
    /// Compute a sheet's ContentHash: per placed view, enumerate non-type elements in view scope
    /// and look up each element's already-computed Hash from the snapshot's element index. Elements
    /// outside snapshot categories (annotations, detail items) are intentionally skipped — ContentHash
    /// tracks the structural scope we snapshot, not everything rendered.
    /// </summary>
    private static string ComputeSheetContentHash(
        Document doc,
        string metaHash,
        List<long> placedViewIds,
        Dictionary<long, string> elementHashById)
    {
        var perView = new List<(long viewId, List<string> elementHashes)>();
        foreach (var viewIdLong in placedViewIds)
        {
            var hashes = new List<string>();
            try
            {
                var viewId = new ElementId(viewIdLong);
                foreach (var element in new FilteredElementCollector(doc, viewId)
                    .WhereElementIsNotElementType())
                {
                    var id = ToCliElementId(element.Id);
                    if (elementHashById.TryGetValue(id, out var h))
                        hashes.Add(h);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[RevitCli] ComputeSheetContentHash view {viewIdLong}: {ex.Message}");
            }
            perView.Add((viewIdLong, hashes));
        }
        return SnapshotHasher.HashSheetContent(metaHash, perView);
    }

    // ═══════════════════════════════════════════════════════════════
    //  families
    // ═══════════════════════════════════════════════════════════════

    public Task<FamilyInfo[]> ListFamiliesAsync(FamilyListRequest request)
    {
        var req = request ?? new FamilyListRequest();

        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);
            var placedFamilyIds = CollectPlacedFamilyIds(doc);

            var families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Select(family => MapFamilyInfo(family, placedFamilyIds))
                .Where(fi =>
                {
                    if (!string.IsNullOrWhiteSpace(req.Category)
                        && !string.Equals(fi.Category, req.Category, StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (req.IncludeUnplaced && fi.IsPlaced)
                        return false;
                    return true;
                })
                .OrderBy(fi => fi.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(fi => fi.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return families;
        });
    }

    public Task<FamilyPurgeResult> PurgeFamiliesAsync(FamilyPurgeRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var ids = NormalizeFamilyIds(request.Ids);
        if (ids.Count == 0)
            throw new ArgumentException("At least one family id is required.", nameof(request));

        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);
            var placedFamilyIds = CollectPlacedFamilyIds(doc);
            var result = new FamilyPurgeResult { DryRun = request.DryRun };
            Transaction? tx = null;

            try
            {
                if (!request.DryRun)
                {
                    tx = new Transaction(doc, "RevitCli family purge");
                    tx.Start();
                }

                foreach (var id in ids)
                {
                    var family = doc.GetElement(new ElementId(id)) as Family;
                    if (family == null)
                    {
                        result.Skipped.Add(new FamilyPurgeSkipped
                        {
                            Id = id,
                            Name = "",
                            Reason = "Family not found"
                        });
                        continue;
                    }

                    var info = MapFamilyInfo(family, placedFamilyIds);
                    if (family.IsInPlace)
                    {
                        result.Skipped.Add(new FamilyPurgeSkipped
                        {
                            Id = id,
                            Name = info.Name,
                            Reason = "In-place families cannot be purged as loadable families"
                        });
                        continue;
                    }

                    if (placedFamilyIds.Contains(id))
                    {
                        result.Skipped.Add(new FamilyPurgeSkipped
                        {
                            Id = id,
                            Name = info.Name,
                            Reason = "Family has placed instances"
                        });
                        continue;
                    }

                    if (request.DryRun)
                    {
                        result.Purged.Add(new FamilyPurgedItem
                        {
                            Id = info.Id,
                            Name = info.Name,
                            Category = info.Category
                        });
                        continue;
                    }

                    try
                    {
                        doc.Delete(family.Id);
                        result.Purged.Add(new FamilyPurgedItem
                        {
                            Id = info.Id,
                            Name = info.Name,
                            Category = info.Category
                        });
                    }
                    catch (Exception ex)
                    {
                        result.Skipped.Add(new FamilyPurgeSkipped
                        {
                            Id = id,
                            Name = info.Name,
                            Reason = $"Revit refused delete: {ex.Message}"
                        });
                    }
                }

                tx?.Commit();
                return result;
            }
            catch
            {
                if (tx?.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                throw;
            }
        });
    }

    public Task<FamilyExportResult> ExportFamiliesAsync(FamilyExportRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var ids = NormalizeFamilyIds(request.Ids);
        if (ids.Count == 0)
            throw new ArgumentException("At least one family id is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.OutputDir))
            throw new ArgumentException("OutputDir is required.", nameof(request));

        string outputDir;
        try
        {
            outputDir = Path.GetFullPath(request.OutputDir);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException($"OutputDir is invalid: {ex.Message}", nameof(request));
        }

        if (!request.DryRun)
            Directory.CreateDirectory(outputDir);

        return _bridge.InvokeAsync(app =>
        {
            var doc = RequireActiveDocument(app);
            var placedFamilyIds = CollectPlacedFamilyIds(doc);
            var result = new FamilyExportResult
            {
                DryRun = request.DryRun,
                OutputDir = outputDir
            };

            foreach (var id in ids)
            {
                var family = doc.GetElement(new ElementId(id)) as Family;
                if (family == null)
                {
                    result.Failed.Add(new FamilyExportFailure
                    {
                        Id = id,
                        Name = "",
                        Reason = "Family not found"
                    });
                    continue;
                }

                var info = MapFamilyInfo(family, placedFamilyIds);
                if (family.IsInPlace)
                {
                    result.Failed.Add(new FamilyExportFailure
                    {
                        Id = id,
                        Name = info.Name,
                        Reason = "In-place families cannot be exported as standalone .rfa files"
                    });
                    continue;
                }

                var filePath = Path.Combine(outputDir, MakeFamilyFileName(info.Name, id));
                if (File.Exists(filePath) && !request.Overwrite)
                {
                    result.Failed.Add(new FamilyExportFailure
                    {
                        Id = id,
                        Name = info.Name,
                        Reason = $"File already exists: {filePath}"
                    });
                    continue;
                }

                if (request.DryRun)
                {
                    result.Exported.Add(new FamilyExportedItem
                    {
                        Id = info.Id,
                        Name = info.Name,
                        Category = info.Category,
                        FilePath = filePath,
                        SizeBytes = 0
                    });
                    continue;
                }

                Document? familyDoc = null;
                try
                {
                    familyDoc = doc.EditFamily(family);
                    var options = new SaveAsOptions
                    {
                        OverwriteExistingFile = request.Overwrite
                    };
                    familyDoc.SaveAs(filePath, options);

                    var size = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;
                    result.Exported.Add(new FamilyExportedItem
                    {
                        Id = info.Id,
                        Name = info.Name,
                        Category = info.Category,
                        FilePath = filePath,
                        SizeBytes = size
                    });
                }
                catch (Exception ex)
                {
                    result.Failed.Add(new FamilyExportFailure
                    {
                        Id = id,
                        Name = info.Name,
                        Reason = $"Revit export failed: {ex.Message}"
                    });
                }
                finally
                {
                    try { familyDoc?.Close(false); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[RevitCli] Family export close failed for {info.Name}: {ex.Message}");
                    }
                }
            }

            return result;
        });
    }

    private static List<long> NormalizeFamilyIds(IEnumerable<long>? ids)
    {
        if (ids == null) return new List<long>();
        return ids.Where(id => id > 0).Distinct().ToList();
    }

    private static HashSet<long> CollectPlacedFamilyIds(Document doc)
    {
        var result = new HashSet<long>();
        foreach (var instance in new FilteredElementCollector(doc)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>())
        {
            var family = instance.Symbol?.Family;
            if (family != null)
                result.Add(ToCliElementId(family.Id));
        }
        return result;
    }

    private static FamilyInfo MapFamilyInfo(Family family, HashSet<long> placedFamilyIds)
    {
        var id = ToCliElementId(family.Id);
        return new FamilyInfo
        {
            Id = id,
            Name = family.Name ?? "",
            Category = family.FamilyCategory?.Name ?? "",
            IsInPlace = family.IsInPlace,
            IsLoadable = !family.IsInPlace,
            FilePath = null,
            IsPlaced = placedFamilyIds.Contains(id)
        };
    }

    private static string MakeFamilyFileName(string familyName, long id)
    {
        var baseName = string.IsNullOrWhiteSpace(familyName) ? $"family-{id}" : familyName.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(c, '_');
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = $"family-{id}";
        return $"{baseName}.rfa";
    }
}
