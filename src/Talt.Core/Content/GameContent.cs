using System.Globalization;
using System.Reflection;
using Talt.Core.Determinism;
using Talt.Core.Maps;

namespace Talt.Core.Content;

/// <summary>
/// The validated cargo and industry catalogue of the game (constitution VII). Loaded once from the
/// embedded <c>content/cargo.json</c> and <c>content/industries.json</c>; every value the simulation
/// uses comes from here, never from code.
/// </summary>
/// <remarks>
/// Immutable and safe to share between threads. A broken data file throws
/// <see cref="InvalidDataException"/> naming the file and the offending id or field, so the game stops
/// at startup instead of running with partial content.
/// </remarks>
public sealed class GameContent
{
    private static readonly Lazy<GameContent> LazyDefault = new(LoadDefault, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly CargoDefinition[] _cargo;
    private readonly IndustryKindDefinition[] _kinds;
    private readonly Dictionary<string, int> _cargoIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _kindIndex = new(StringComparer.Ordinal);
    private readonly ulong _fingerprint;

    private GameContent(CargoDefinition[] cargo, IndustryKindDefinition[] kinds, EconomyRules economy)
    {
        _cargo = cargo;
        _kinds = kinds;
        Economy = economy;
        for (var i = 0; i < cargo.Length; i++)
        {
            _cargoIndex.Add(cargo[i].Id, i);
        }

        for (var i = 0; i < kinds.Length; i++)
        {
            _kindIndex.Add(kinds[i].Id, i);
        }

        _fingerprint = ComputeFingerprint();
    }

    /// <summary>The default content embedded in this assembly; loaded on first use.</summary>
    public static GameContent Default => LazyDefault.Value;

    /// <summary>Economy-wide rules (growth, decline, openings, storage-independent thresholds).</summary>
    public EconomyRules Economy { get; }

    /// <summary>Number of cargo types.</summary>
    public int CargoCount => _cargo.Length;

    /// <summary>Number of industry kinds.</summary>
    public int IndustryKindCount => _kinds.Length;

    /// <summary>All cargo types, in data-file order.</summary>
    public IReadOnlyList<CargoDefinition> AllCargo => _cargo;

    /// <summary>All industry kinds, in data-file order.</summary>
    public IReadOnlyList<IndustryKindDefinition> AllIndustryKinds => _kinds;

    /// <summary>Cargo at a catalogue index (simulation uses indices, not strings).</summary>
    public CargoDefinition Cargo(int index) => _cargo[index];

    /// <summary>Industry kind at a catalogue index.</summary>
    public IndustryKindDefinition IndustryKind(int index) => _kinds[index];

    /// <summary>Index of a cargo id, or -1 when it does not exist.</summary>
    public int CargoIndex(string id) => _cargoIndex.TryGetValue(id, out var index) ? index : -1;

    /// <summary>Index of an industry kind id, or -1 when it does not exist.</summary>
    public int IndustryKindIndex(string id) => _kindIndex.TryGetValue(id, out var index) ? index : -1;

    /// <summary>64-bit fingerprint of the catalogue; included in the simulation state when industries exist.</summary>
    public ulong Fingerprint() => _fingerprint;

    /// <summary>Loads the data files embedded in the <c>Talt.Core</c> assembly.</summary>
    public static GameContent LoadDefault()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var cargo = ReadResource(assembly, "content/cargo.json");
        var industries = ReadResource(assembly, "content/industries.json");
        return FromJson(cargo, industries);
    }

    /// <summary>Parses and validates content from two JSON documents (tests and future modding).</summary>
    /// <param name="cargoJson">Contents of the cargo file.</param>
    /// <param name="industriesJson">Contents of the industry file.</param>
    /// <param name="cargoFileName">Name used in error messages.</param>
    /// <param name="industriesFileName">Name used in error messages.</param>
    /// <exception cref="InvalidDataException">The content is missing, malformed or inconsistent.</exception>
    public static GameContent FromJson(
        string cargoJson,
        string industriesJson,
        string cargoFileName = "cargo.json",
        string industriesFileName = "industries.json")
    {
        ArgumentNullException.ThrowIfNull(cargoJson);
        ArgumentNullException.ThrowIfNull(industriesJson);
        var cargoFile = ContentJson.Deserialize<CargoFileJson>(cargoJson, cargoFileName);
        var industryFile = ContentJson.Deserialize<IndustryFileJson>(industriesJson, industriesFileName);
        var cargo = ParseCargo(cargoFile, cargoFileName);
        var kinds = ParseKinds(industryFile, cargo, cargoFileName, industriesFileName);
        var economy = ParseEconomy(industryFile.Economy, industriesFileName);
        return new GameContent(cargo, kinds, economy);
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"Missing embedded data file '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static CargoDefinition[] ParseCargo(CargoFileJson file, string fileName)
    {
        var entries = file.Cargo ?? throw Fail(fileName, "missing 'cargo' array.");
        if (entries.Count == 0)
        {
            throw Fail(fileName, "'cargo' must contain at least one cargo type.");
        }

        var result = new CargoDefinition[entries.Count];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var id = RequireId(entry.Id, fileName, $"'cargo[{i}]'");
            if (!ids.Add(id))
            {
                throw Fail(fileName, $"duplicate cargo id '{id}'.");
            }

            var category = ParseCategory(entry.Category, fileName, id);
            var destination = entry.DestinationRequired
                ?? throw Fail(fileName, $"cargo '{id}' is missing 'destinationRequired'.");
            if (destination != (category is CargoCategory.Passenger or CargoCategory.Mail or CargoCategory.Goods))
            {
                throw Fail(fileName, $"cargo '{id}' has 'destinationRequired': {destination.ToString().ToLowerInvariant()}, which does not match its category.");
            }

            var decay = entry.ValueDecayPerDayPerMille
                ?? throw Fail(fileName, $"cargo '{id}' is missing 'valueDecayPerDayPerMille'.");
            var floor = entry.MinimumValuePercent
                ?? throw Fail(fileName, $"cargo '{id}' is missing 'minimumValuePercent'.");
            if (decay is < 0 or > 1000)
            {
                throw Fail(fileName, $"cargo '{id}' has valueDecayPerDayPerMille {decay}; it must be 0 to 1000.");
            }

            if (floor is < 0 or > 100)
            {
                throw Fail(fileName, $"cargo '{id}' has minimumValuePercent {floor}; it must be 0 to 100.");
            }

            var vehicleKinds = ParseVehicleKinds(entry.VehicleKinds, fileName, id);
            result[i] = new CargoDefinition(
                id,
                RequireKey(entry.NameKey, fileName, $"cargo '{id}' nameKey"),
                category,
                RequireKey(entry.UnitKey, fileName, $"cargo '{id}' unitKey"),
                destination,
                decay,
                floor,
                ParseColor(entry.Color, fileName, $"cargo '{id}' color"),
                vehicleKinds);
        }

        return result;
    }

    private static IndustryKindDefinition[] ParseKinds(
        IndustryFileJson file,
        CargoDefinition[] cargo,
        string cargoFileName,
        string fileName)
    {
        var entries = file.Industries ?? throw Fail(fileName, "missing 'industries' array.");
        if (entries.Count == 0)
        {
            throw Fail(fileName, "'industries' must contain at least one industry kind.");
        }

        var cargoIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in cargo)
        {
            cargoIds.Add(item.Id);
        }

        var result = new IndustryKindDefinition[entries.Count];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            result[i] = ParseKind(entries[i], i, cargoIds, cargoFileName, fileName, ids);
        }

        return result;
    }

    private static IndustryKindDefinition ParseKind(
        IndustryJson entry,
        int index,
        HashSet<string> cargoIds,
        string cargoFileName,
        string fileName,
        HashSet<string> ids)
    {
        var id = RequireId(entry.Id, fileName, $"'industries[{index}]'");
        if (!ids.Add(id))
        {
            throw Fail(fileName, $"duplicate industry id '{id}'.");
        }

        var footprint = entry.Footprint ?? throw Fail(fileName, $"industry '{id}' is missing 'footprint'.");
        if (footprint.Count != 2 || footprint[0] is < 1 or > 5 || footprint[1] is < 1 or > 5)
        {
            throw Fail(fileName, $"industry '{id}' footprint must be two values between 1 and 5.");
        }

        var era = entry.Era ?? throw Fail(fileName, $"industry '{id}' is missing 'era'.");
        if (era.Count != 2 || era[0] is < 1800 or > 9999 || era[1] is < 1800 or > 9999 || era[0] > era[1])
        {
            throw Fail(fileName, $"industry '{id}' era must be two years between 1800 and 9999, start <= end.");
        }

        var weight = entry.Weight ?? throw Fail(fileName, $"industry '{id}' is missing 'weight'.");
        if (weight is < 1 or > 1000)
        {
            throw Fail(fileName, $"industry '{id}' weight {weight} must be 1 to 1000.");
        }

        var siteKinds = ParseSiteKinds(entry.SiteKinds, fileName, id);
        var production = ParseProduction(entry.Production, cargoIds, cargoFileName, fileName, id);
        var accepts = ParseAccepts(entry.Accepts, cargoIds, cargoFileName, fileName, id);
        var recipes = ParseRecipes(entry.Recipes, cargoIds, cargoFileName, fileName, id, accepts);
        var stock = ParseStock(entry.Stock, cargoIds, cargoFileName, fileName, id);
        var model = ParseModel(entry.Model, fileName, id, footprint[0], footprint[1]);

        foreach (var item in production)
        {
            if (item.PerMonth > 0 && CapacityOf(stock, item.CargoId) <= 0)
            {
                throw Fail(fileName, $"industry '{id}' produces '{item.CargoId}' but has no positive stock capacity for it.");
            }
        }

        foreach (var recipe in recipes)
        {
            if (CapacityOf(stock, recipe.InputCargoId) <= 0)
            {
                throw Fail(fileName, $"industry '{id}' recipe input '{recipe.InputCargoId}' has no positive stock capacity.");
            }

            if (CapacityOf(stock, recipe.OutputCargoId) <= 0)
            {
                throw Fail(fileName, $"industry '{id}' recipe output '{recipe.OutputCargoId}' has no positive stock capacity.");
            }
        }

        return new IndustryKindDefinition(
            id,
            RequireKey(entry.NameKey, fileName, $"industry '{id}' nameKey"),
            footprint[0],
            footprint[1],
            production,
            accepts,
            recipes,
            stock,
            model,
            siteKinds,
            era[0],
            era[1],
            weight);
    }

    private static ProductionEntry[] ParseProduction(
        List<ProductionJson>? entries,
        HashSet<string> cargoIds,
        string cargoFileName,
        string fileName,
        string id)
    {
        if (entries is null || entries.Count == 0)
        {
            return [];
        }

        var result = new ProductionEntry[entries.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            var cargo = RequireCargo(entries[i].Cargo, cargoIds, cargoFileName, fileName, id, "production");
            if (!seen.Add(cargo))
            {
                throw Fail(fileName, $"industry '{id}' produces '{cargo}' more than once.");
            }

            var perMonth = entries[i].PerMonth
                ?? throw Fail(fileName, $"industry '{id}' production of '{cargo}' is missing 'perMonth'.");
            if (perMonth is < 0 or > 1_000_000)
            {
                throw Fail(fileName, $"industry '{id}' production of '{cargo}' perMonth {perMonth} must be 0 to 1000000.");
            }

            result[i] = new ProductionEntry(cargo, perMonth);
        }

        return result;
    }

    private static string[] ParseAccepts(
        List<string>? entries,
        HashSet<string> cargoIds,
        string cargoFileName,
        string fileName,
        string id)
    {
        if (entries is null || entries.Count == 0)
        {
            return [];
        }

        var result = new string[entries.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            var cargo = RequireCargo(entries[i], cargoIds, cargoFileName, fileName, id, "accepts");
            if (!seen.Add(cargo))
            {
                throw Fail(fileName, $"industry '{id}' accepts '{cargo}' more than once.");
            }

            result[i] = cargo;
        }

        return result;
    }

    private static RecipeDefinition[] ParseRecipes(
        List<RecipeJson>? entries,
        HashSet<string> cargoIds,
        string cargoFileName,
        string fileName,
        string id,
        string[] accepts)
    {
        if (entries is null || entries.Count == 0)
        {
            return [];
        }

        var result = new RecipeDefinition[entries.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            var input = RequireCargo(entries[i].Input, cargoIds, cargoFileName, fileName, id, "recipe input");
            var output = RequireCargo(entries[i].Output, cargoIds, cargoFileName, fileName, id, "recipe output");
            var inputAmount = entries[i].InputAmount
                ?? throw Fail(fileName, $"industry '{id}' recipe for '{input}' is missing 'inputAmount'.");
            var outputAmount = entries[i].OutputAmount
                ?? throw Fail(fileName, $"industry '{id}' recipe for '{input}' is missing 'outputAmount'.");
            if (inputAmount is < 1 or > 10_000 || outputAmount is < 1 or > 10_000)
            {
                throw Fail(fileName, $"industry '{id}' recipe amounts must be between 1 and 10000.");
            }

            if (string.Equals(input, output, StringComparison.Ordinal))
            {
                throw Fail(fileName, $"industry '{id}' recipe input and output are both '{input}'.");
            }

            if (!seen.Add(input))
            {
                throw Fail(fileName, $"industry '{id}' has more than one recipe for '{input}'.");
            }

            var accepted = false;
            foreach (var cargo in accepts)
            {
                if (string.Equals(cargo, input, StringComparison.Ordinal))
                {
                    accepted = true;
                    break;
                }
            }

            if (!accepted)
            {
                throw Fail(fileName, $"industry '{id}' has a recipe for '{input}' but does not accept it.");
            }

            result[i] = new RecipeDefinition(input, inputAmount, output, outputAmount);
        }

        return result;
    }

    private static StockDefinition[] ParseStock(
        List<StockJson>? entries,
        HashSet<string> cargoIds,
        string cargoFileName,
        string fileName,
        string id)
    {
        if (entries is null || entries.Count == 0)
        {
            return [];
        }

        var result = new StockDefinition[entries.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            var cargo = RequireCargo(entries[i].Cargo, cargoIds, cargoFileName, fileName, id, "stock");
            if (!seen.Add(cargo))
            {
                throw Fail(fileName, $"industry '{id}' has more than one stock entry for '{cargo}'.");
            }

            var capacity = entries[i].Capacity
                ?? throw Fail(fileName, $"industry '{id}' stock for '{cargo}' is missing 'capacity'.");
            if (capacity <= 0)
            {
                throw Fail(fileName, $"industry '{id}' stock capacity for '{cargo}' must be positive.");
            }

            result[i] = new StockDefinition(cargo, capacity);
        }

        return result;
    }

    private static ModelBox[] ParseModel(List<ModelBoxJson>? entries, string fileName, string id, int footprintWidth, int footprintDepth)
    {
        if (entries is null || entries.Count == 0)
        {
            throw Fail(fileName, $"industry '{id}' needs at least one model box.");
        }

        var result = new ModelBox[entries.Count];
        var minX = -(footprintWidth / 2);
        var minZ = -(footprintDepth / 2);
        var maxX = minX + footprintWidth;
        var maxZ = minZ + footprintDepth;
        for (var i = 0; i < entries.Count; i++)
        {
            var box = entries[i];
            var x = box.X ?? throw Fail(fileName, $"industry '{id}' model box {i} is missing 'x'.");
            var y = box.Y ?? throw Fail(fileName, $"industry '{id}' model box {i} is missing 'y'.");
            var z = box.Z ?? throw Fail(fileName, $"industry '{id}' model box {i} is missing 'z'.");
            var width = box.Width ?? throw Fail(fileName, $"industry '{id}' model box {i} is missing 'width'.");
            var depth = box.Depth ?? throw Fail(fileName, $"industry '{id}' model box {i} is missing 'depth'.");
            var height = box.Height ?? throw Fail(fileName, $"industry '{id}' model box {i} is missing 'height'.");
            if (width < 1 || depth < 1 || height < 1 || height > 12 || y < 0 || y + height > 12)
            {
                throw Fail(fileName, $"industry '{id}' model box {i} must have positive sizes, height <= 12 and top <= 12.");
            }

            var left = x - (width / 2);
            var top = z - (depth / 2);
            if (left < minX || top < minZ || left + width > maxX || top + depth > maxZ)
            {
                throw Fail(fileName, $"industry '{id}' model box {i} does not fit its {footprintWidth}x{footprintDepth} footprint.");
            }

            result[i] = new ModelBox(x, y, z, width, depth, height, ParseColor(box.Color, fileName, $"industry '{id}' model box {i} color"));
        }

        return result;
    }

    private static List<SiteKind> ParseSiteKinds(List<string>? entries, string fileName, string id)
    {
        if (entries is null || entries.Count == 0)
        {
            throw Fail(fileName, $"industry '{id}' needs at least one site kind.");
        }

        var result = new List<SiteKind>(entries.Count);
        foreach (var entry in entries)
        {
            var kind = entry switch
            {
                "coast" => SiteKind.IndustryCoast,
                "forest" => SiteKind.IndustryForest,
                "hills" => SiteKind.IndustryHills,
                "plain" => SiteKind.IndustryPlain,
                _ => throw Fail(fileName, $"industry '{id}' has unknown site kind '{entry}'."),
            };
            if (result.Contains(kind))
            {
                throw Fail(fileName, $"industry '{id}' lists site kind '{entry}' more than once.");
            }

            result.Add(kind);
        }

        return result;
    }

    private static EconomyRules ParseEconomy(EconomyJson? entry, string fileName)
    {
        if (entry is null)
        {
            throw Fail(fileName, "missing 'economy' object.");
        }

        var rules = new EconomyRules(
            entry.InitialOccupancyPercent ?? 75,
            entry.DriftPerMille ?? 15,
            entry.GrowthPerMille ?? 25,
            entry.DeclineAfterMonths ?? 24,
            entry.DeclinePerMille ?? 20,
            entry.CloseAfterMonths ?? 60,
            entry.CloseChancePerMille ?? 8,
            entry.CloseAtRatePerMille ?? 700,
            entry.OpeningChancePerMillePerSite ?? 4,
            entry.MaxOpeningsPerMonth ?? 3,
            entry.MinRatePerMille ?? 300,
            entry.MaxRatePerMille ?? 1500);

        CheckRange(fileName, "initialOccupancyPercent", rules.InitialOccupancyPercent, 0, 100);
        CheckRange(fileName, "driftPerMille", rules.DriftPerMille, 0, 200);
        CheckRange(fileName, "growthPerMille", rules.GrowthPerMille, 0, 500);
        CheckRange(fileName, "declineAfterMonths", rules.DeclineAfterMonths, 0, 1000);
        CheckRange(fileName, "declinePerMille", rules.DeclinePerMille, 0, 500);
        CheckRange(fileName, "closeAfterMonths", rules.CloseAfterMonths, rules.DeclineAfterMonths, 2000);
        CheckRange(fileName, "closeChancePerMille", rules.CloseChancePerMille, 0, 1000);
        CheckRange(fileName, "closeAtRatePerMille", rules.CloseAtRatePerMille, 0, 3000);
        CheckRange(fileName, "openingChancePerMillePerSite", rules.OpeningChancePerMillePerSite, 0, 1000);
        CheckRange(fileName, "maxOpeningsPerMonth", rules.MaxOpeningsPerMonth, 0, 100);
        CheckRange(fileName, "minRatePerMille", rules.MinRatePerMille, 1, 10_000);
        CheckRange(fileName, "maxRatePerMille", rules.MaxRatePerMille, 2, 10_000);
        if (rules.MinRatePerMille >= rules.MaxRatePerMille)
        {
            throw Fail(fileName, "minRatePerMille must be lower than maxRatePerMille.");
        }

        return rules;
    }

    private static void CheckRange(string fileName, string field, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            throw Fail(fileName, $"economy {field} {value} must be between {min} and {max}.");
        }
    }

    private static int CapacityOf(StockDefinition[] stock, string cargoId)
    {
        foreach (var item in stock)
        {
            if (string.Equals(item.CargoId, cargoId, StringComparison.Ordinal))
            {
                return item.Capacity;
            }
        }

        return 0;
    }

    private static string RequireCargo(
        string? value,
        HashSet<string> cargoIds,
        string cargoFileName,
        string fileName,
        string id,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Fail(fileName, $"industry '{id}' {field} is missing a cargo id.");
        }

        if (!cargoIds.Contains(value))
        {
            throw Fail(fileName, $"industry '{id}' {field} refers to unknown cargo '{value}' (see {cargoFileName}).");
        }

        return value;
    }

    private static CargoCategory ParseCategory(string? value, string fileName, string id) => value switch
    {
        "raw" => CargoCategory.RawMaterial,
        "processed" => CargoCategory.ProcessedGood,
        "passenger" => CargoCategory.Passenger,
        "mail" => CargoCategory.Mail,
        "goods" => CargoCategory.Goods,
        _ => throw Fail(fileName, $"cargo '{id}' has unknown category '{value}'."),
    };

    private static VehicleKind ParseVehicleKinds(List<string>? entries, string fileName, string id)
    {
        if (entries is null || entries.Count == 0)
        {
            throw Fail(fileName, $"cargo '{id}' needs at least one vehicle kind.");
        }

        var result = VehicleKind.None;
        foreach (var entry in entries)
        {
            var kind = entry switch
            {
                "road" => VehicleKind.Road,
                "rail" => VehicleKind.Rail,
                "ship" => VehicleKind.Ship,
                "air" => VehicleKind.Air,
                _ => throw Fail(fileName, $"cargo '{id}' has unknown vehicle kind '{entry}'."),
            };
            if ((result & kind) != 0)
            {
                throw Fail(fileName, $"cargo '{id}' lists vehicle kind '{entry}' more than once.");
            }

            result |= kind;
        }

        return result;
    }

    private static uint ParseColor(string? value, string fileName, string field)
    {
        if (value is null || value.Length != 7 || value[0] != '#'
            || !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color))
        {
            throw Fail(fileName, $"{field} must be a colour like #RRGGBB (was '{value}').");
        }

        return color;
    }

    private static string RequireId(string? value, string fileName, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsValidId(value))
        {
            throw Fail(fileName, $"{field} needs an id made of lowercase letters, digits and single hyphens (was '{value}').");
        }

        return value;
    }

    private static bool IsValidId(string value)
    {
        if (value.Length == 0 || value[0] == '-' || value[^1] == '-')
        {
            return false;
        }

        var previousHyphen = false;
        foreach (var c in value)
        {
            if (c == '-')
            {
                if (previousHyphen)
                {
                    return false;
                }

                previousHyphen = true;
                continue;
            }

            if ((c < 'a' || c > 'z') && (c < '0' || c > '9'))
            {
                return false;
            }

            previousHyphen = false;
        }

        return true;
    }

    private static string RequireKey(string? value, string fileName, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Fail(fileName, $"{field} must not be empty.");
        }

        return value;
    }

    private static InvalidDataException Fail(string fileName, string problem) =>
        new($"Data file '{fileName}': {problem}");

    private ulong ComputeFingerprint()
    {
        var hasher = new StateHasher();
        hasher.Append(_cargo.Length);
        foreach (var cargo in _cargo)
        {
            hasher.Append(cargo.Id);
            hasher.Append(cargo.NameKey);
            hasher.Append((int)cargo.Category);
            hasher.Append(cargo.UnitKey);
            hasher.Append(cargo.DestinationRequired ? 1 : 0);
            hasher.Append(cargo.ValueDecayPerDayPerMille);
            hasher.Append(cargo.MinimumValuePercent);
            hasher.Append(cargo.Color);
            hasher.Append((int)cargo.VehicleKinds);
        }

        hasher.Append(_kinds.Length);
        foreach (var kind in _kinds)
        {
            hasher.Append(kind.Id);
            hasher.Append(kind.NameKey);
            hasher.Append(kind.FootprintWidth);
            hasher.Append(kind.FootprintDepth);
            hasher.Append(kind.Production.Count);
            foreach (var production in kind.Production)
            {
                hasher.Append(production.CargoId);
                hasher.Append(production.PerMonth);
            }

            hasher.Append(kind.Accepts.Count);
            foreach (var cargo in kind.Accepts)
            {
                hasher.Append(cargo);
            }

            hasher.Append(kind.Recipes.Count);
            foreach (var recipe in kind.Recipes)
            {
                hasher.Append(recipe.InputCargoId);
                hasher.Append(recipe.InputAmount);
                hasher.Append(recipe.OutputCargoId);
                hasher.Append(recipe.OutputAmount);
            }

            hasher.Append(kind.Stock.Count);
            foreach (var stock in kind.Stock)
            {
                hasher.Append(stock.CargoId);
                hasher.Append(stock.Capacity);
            }

            hasher.Append(kind.Model.Count);
            foreach (var box in kind.Model)
            {
                hasher.Append(box.X);
                hasher.Append(box.Y);
                hasher.Append(box.Z);
                hasher.Append(box.Width);
                hasher.Append(box.Depth);
                hasher.Append(box.Height);
                hasher.Append(box.Color);
            }

            hasher.Append(kind.SiteKinds.Count);
            foreach (var siteKind in kind.SiteKinds)
            {
                hasher.Append((int)siteKind);
            }

            hasher.Append(kind.EraStart);
            hasher.Append(kind.EraEnd);
            hasher.Append(kind.Weight);
        }

        hasher.Append(Economy.InitialOccupancyPercent);
        hasher.Append(Economy.DriftPerMille);
        hasher.Append(Economy.GrowthPerMille);
        hasher.Append(Economy.DeclineAfterMonths);
        hasher.Append(Economy.DeclinePerMille);
        hasher.Append(Economy.CloseAfterMonths);
        hasher.Append(Economy.CloseChancePerMille);
        hasher.Append(Economy.CloseAtRatePerMille);
        hasher.Append(Economy.OpeningChancePerMillePerSite);
        hasher.Append(Economy.MaxOpeningsPerMonth);
        hasher.Append(Economy.MinRatePerMille);
        hasher.Append(Economy.MaxRatePerMille);
        return hasher.GetFingerprint();
    }
}
