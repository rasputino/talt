using Talt.Core.Construction;
using Talt.Core.Content;
using Talt.Core.Maps;
using Talt.Core.Simulation;

namespace Talt.Core.Saves;

/// <summary>
/// A captured save, ready to be written (data-model.md SaveSnapshot, research R7): the details, the thumbnail and one
/// pooled buffer per section. Capturing is the only part that runs on the main thread; writing can run anywhere. A
/// snapshot can be captured into again once its write has finished, which keeps repeated autosaves allocation-free.
/// </summary>
public sealed class SaveSnapshot : IDisposable
{
    private readonly List<SaveSectionData> _sections = [];
    private readonly Dictionary<long, (SaveSectionData Data, SaveWriter Writer)> _parts = [];

    /// <summary>The details of the captured game.</summary>
    public SaveDetails? Details { get; internal set; }

    /// <summary>The map overview, or null.</summary>
    public SaveThumbnailData? Thumbnail { get; internal set; }

    /// <summary>The captured sections in restore order.</summary>
    public IReadOnlyList<SaveSectionData> Sections => _sections;

    /// <summary>Total uncompressed bytes captured.</summary>
    public long Length
    {
        get
        {
            long total = 0;
            foreach (var section in _sections)
            {
                total += section.Payload.Length;
            }

            return total;
        }
    }

    internal SaveWriter Begin(SaveSectionId id, int version)
    {
        // Buffers, writers and section records are kept per (id, version), so capturing again allocates nothing.
        var key = ((long)id.Value << 16) | (uint)version;
        if (!_parts.TryGetValue(key, out var part))
        {
            var buffer = new SaveBuffer();
            part = (new SaveSectionData(id, version, buffer), new SaveWriter(buffer));
            _parts[key] = part;
        }

        _sections.Add(part.Data);
        return part.Writer;
    }

    private readonly SectionCapture[] _workers = SaveSections.Systems.Select(section => new SectionCapture(section)).ToArray();

    internal void CaptureSystems(GameSimulation simulation, SaveWriter? mapWriter)
    {
        var map = simulation.Construction?.Map;
        var parallel = map is not null && Math.Max(map.Width, map.Depth) >= 1024 && Environment.ProcessorCount > 1;
        try
        {
            // Reserve buffers in file order before dispatch. Each worker reads the same frozen tick
            // and owns a separate section. Only the caller touches the snapshot registry.
            foreach (var worker in _workers)
            {
                var section = worker.Section;
                if (section.IsPresent(simulation))
                {
                    var writer = Begin(section.Id, section.Version);
                    if (parallel)
                    {
                        worker.Start(simulation, writer);
                    }
                    else
                    {
                        section.Write(simulation, writer);
                    }
                }
            }

            if (map is not null)
            {
                map.WriteState(mapWriter!, simulation.SaveCache!.MapText ?? map.Settings.ToText());
            }
        }
        finally
        {
            if (parallel)
            {
                Exception? error = null;
                foreach (var worker in _workers)
                {
                    var problem = worker.Join();
                    error ??= problem;
                }

                if (error is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
                }
            }
        }
    }

    private sealed class SectionCapture(SaveSection section) : IThreadPoolWorkItem
    {
        private readonly object _gate = new();
        private GameSimulation? _simulation;
        private SaveWriter? _writer;
        private Exception? _error;

        public SaveSection Section { get; } = section;

        public void Start(GameSimulation simulation, SaveWriter writer)
        {
            _simulation = simulation;
            _writer = writer;
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
        }

        public void Execute()
        {
            try
            {
                Section.Write(_simulation!, _writer!);
            }
            catch (Exception e)
            {
                _error = e;
            }
            finally
            {
                lock (_gate)
                {
                    _simulation = null;
                    Monitor.Pulse(_gate);
                }
            }
        }

        public Exception? Join()
        {
            lock (_gate)
            {
                while (_simulation is not null)
                {
                    Monitor.Wait(_gate);
                }

                var error = _error;
                _error = null;
                return error;
            }
        }
    }

    internal void Reset()
    {
        foreach (var (_, part) in _parts)
        {
            part.Data.Payload.Clear();
        }

        _sections.Clear();
        Details = null;
        Thumbnail = null;
    }

    /// <summary>Returns every buffer to the pool.</summary>
    public void Dispose()
    {
        Reset();
        foreach (var (_, part) in _parts)
        {
            part.Writer.Dispose();
        }

        _parts.Clear();
        SaveBufferPool.Trim();
    }
}

/// <summary>
/// Saving and loading games (contracts/save-api.md). <see cref="Capture"/> copies the state at a tick boundary into
/// pooled buffers on the calling (main) thread; <see cref="WriteAsync"/> compresses and writes it atomically on any
/// thread; <see cref="Load(string, SaveLoadOptions?, IProgress{float}?)"/> verifies the whole file, then restores an
/// empty, wired game section by section (research R7–R9). Loading never throws for bad files: it returns a status.
/// </summary>
public static class SaveGame
{
    /// <summary>Captures <paramref name="simulation"/> into a new snapshot. Never changes the simulation (SC-007).</summary>
    public static SaveSnapshot Capture(GameSimulation simulation, SaveDetailsInput details, IReadOnlyList<ExtraSection>? extraSections = null)
    {
        var snapshot = new SaveSnapshot();
        try
        {
            CaptureInto(snapshot, simulation, details, extraSections);
            return snapshot;
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    /// <summary>Captures into an existing snapshot (its previous content is discarded).</summary>
    public static void CaptureInto(SaveSnapshot snapshot, GameSimulation simulation, SaveDetailsInput details, IReadOnlyList<ExtraSection>? extraSections = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(simulation);
        details.Validate();
        var previousThumbnail = snapshot.Thumbnail;
        var previousDetails = snapshot.Details;
        snapshot.Reset();
        CaptureSections(snapshot, simulation);
        if (extraSections is not null)
        {
            for (var i = 0; i < extraSections.Count; i++)
            {
                var extra = extraSections[i];
                snapshot.Begin(extra.Id, extra.Version).WriteRaw(extra.Payload.Span);
            }
        }

        snapshot.Details = SaveDetails.Of(simulation, details, previousDetails);
        snapshot.Thumbnail = MapOverview.RenderInto(simulation, previousThumbnail);
    }

    /// <summary>Writes every built-in section of <paramref name="simulation"/> (the main-thread part of capture).</summary>
    internal static void CaptureSections(SaveSnapshot snapshot, GameSimulation simulation)
    {
        // A route job running on a worker thread finishes first, so its result is part of the state (research R1).
        simulation.Router?.WaitForJob();
        var core = snapshot.Begin(SaveSections.Core, SaveSections.CoreVersion);
        var content = snapshot.Begin(SaveSections.Content, SaveSections.ContentVersion);
        ContentIdentity.Write(simulation, content); // Also fills the per-game cache the core section uses.
        core.WriteString(simulation.SaveCache!.SettingsJson);
        simulation.WriteCoreState(core);
        var mapWriter = simulation.Construction is null ? null : snapshot.Begin(SaveSections.Map, SaveSections.MapVersion);
        snapshot.CaptureSystems(simulation, mapWriter);
    }

    /// <summary>Compresses and writes a snapshot to <paramref name="path"/> through a temporary file (FR-007). Never throws for I/O problems.</summary>
    public static Task<SaveWriteResult> WriteAsync(SaveSnapshot snapshot, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Task.Run(() => Write(snapshot, path), cancellationToken);
    }

    /// <summary>Compresses and writes a snapshot synchronously. Never throws for I/O problems.</summary>
    public static SaveWriteResult Write(SaveSnapshot snapshot, string path, SaveCodec codec = SaveCodec.Deflate)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var details = snapshot.Details ?? throw new InvalidOperationException("The snapshot was not captured.");
        var full = Path.GetFullPath(path);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var folder = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            long bytes;
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16))
            {
                SaveFile.Write(stream, details.ToJson(), snapshot.Thumbnail, snapshot.Sections, codec);
                stream.Flush(flushToDisk: true);
                bytes = stream.Length;
            }

            File.Move(temporary, full, overwrite: true);
            return new SaveWriteResult(true, full, bytes, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            TryDelete(temporary);
            return new SaveWriteResult(false, full, 0, e.Message, e);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
        finally
        {
            SaveBufferPool.Trim();
        }
    }

    /// <summary>Captures and writes synchronously (headless tool, tests).</summary>
    public static SaveWriteResult Save(GameSimulation simulation, string path, SaveDetailsInput details, IReadOnlyList<ExtraSection>? extraSections = null)
    {
        using var snapshot = Capture(simulation, details, extraSections);
        return Write(snapshot, path);
    }

    /// <summary>Saves to a stream (tests).</summary>
    public static void Save(GameSimulation simulation, Stream stream, SaveDetailsInput details, SaveCodec codec = SaveCodec.Deflate)
    {
        using var snapshot = Capture(simulation, details);
        SaveFile.Write(stream, snapshot.Details!.ToJson(), snapshot.Thumbnail, snapshot.Sections, codec);
    }

    /// <summary>Reads a save's details and thumbnail only (FR-016). Never throws for bad files.</summary>
    public static SavePeek Peek(string path, bool includeThumbnail = true)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 14);
            return Peek(stream, includeThumbnail);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SavePeek(SavePeekStatus.IoError, null, null, e.Message);
        }
    }

    /// <summary>Reads a save's details and thumbnail from a seekable stream.</summary>
    public static SavePeek Peek(Stream stream, bool includeThumbnail = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            var (json, thumbnail) = SaveFile.Peek(stream, includeThumbnail);
            return new SavePeek(SavePeekStatus.Ok, SaveDetails.FromJson(json), thumbnail, null);
        }
        catch (SaveFormatException e)
        {
            var status = e.Failure switch
            {
                SaveFailure.TooNew => SavePeekStatus.TooNew,
                SaveFailure.NotASave => SavePeekStatus.NotASave,
                _ => SavePeekStatus.Damaged,
            };
            return new SavePeek(status, null, null, e.Message);
        }
    }

    /// <summary>Loads a save file. Never throws for bad files (FR-019).</summary>
    public static SaveLoadResult Load(string path, SaveLoadOptions? options = null, IProgress<float>? progress = null)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            return Load(stream, options, progress);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return SaveLoadResult.Failed(SaveLoadStatus.IoError, e.Message);
        }
    }

    /// <summary>Loads a save from a seekable stream. Never throws for bad data (FR-019).</summary>
    public static SaveLoadResult Load(Stream stream, SaveLoadOptions? options = null, IProgress<float>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new SaveLoadOptions();
        SaveFileContents contents;
        try
        {
            contents = SaveFile.Read(stream);
        }
        catch (SaveFormatException e)
        {
            return SaveLoadResult.Failed(Status(e), e.Message);
        }

        progress?.Report(0.5f);
        using (contents)
        {
            try
            {
                return Restore(contents, options, progress);
            }
            catch (SaveFormatException e)
            {
                return SaveLoadResult.Failed(Status(e), e.Message);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // Data that passed every checksum but still cannot be restored (a damaged save written by a buggy build).
                return SaveLoadResult.Failed(SaveLoadStatus.Damaged, $"The save cannot be restored: {e.Message}");
            }
        }
    }

    private static SaveLoadStatus Status(SaveFormatException e) => e.Failure switch
    {
        SaveFailure.TooNew => SaveLoadStatus.TooNew,
        SaveFailure.NotASave => SaveLoadStatus.NotASave,
        _ => SaveLoadStatus.Damaged,
    };

    internal static SaveLoadResult Restore(SaveFileContents contents, SaveLoadOptions options, IProgress<float>? progress)
    {
        var details = SaveDetails.FromJson(contents.DetailsJson);
        var byId = new Dictionary<uint, SaveSectionData>();
        var extras = new List<ExtraSection>();
        foreach (var section in contents.Sections)
        {
            if (section.Id == SaveSections.Camera && section.Version > 1)
            {
                throw new SaveFormatException(SaveFailure.TooNew, $"Unsupported camera section version {section.Version}.");
            }

            if (section.Id == SaveSections.Camera && !options.ExtraSectionIds.Contains(section.Id))
            {
                continue;
            }

            if (options.ExtraSectionIds.Contains(section.Id))
            {
                extras.Add(new ExtraSection(section.Id, section.Version, Flatten(section.Payload)));
                continue;
            }

            var known = SaveSections.KnownVersion(section.Id) ?? KnownAdditional(options, section.Id);
            if (known is null)
            {
                throw new SaveFormatException(SaveFailure.TooNew, $"The save has a section ({section.Id}) this version does not know.");
            }

            if (section.Version > known)
            {
                throw new SaveFormatException(SaveFailure.TooNew, $"Section {section.Id} has version {section.Version}; this game reads up to {known}.");
            }

            byId[section.Id.Value] = section;
        }

        // CORE: settings first, then (after the map) the rest of the core state.
        var coreSection = byId.GetValueOrDefault(SaveSections.Core.Value) ?? throw new SaveFormatException("The save has no core section.");
        var core = new SaveReader(coreSection.Payload, SaveSections.Core);
        var settings = GameSimulation.SettingsFromJsonSafe(core.ReadRequiredString(), core);
        var content = options.Content ?? GameContent.Default;
        var contentSection = byId.GetValueOrDefault(SaveSections.Content.Value) ?? throw new SaveFormatException("The save has no content section.");
        // Other rule loaders resolve their own references during construction. Report removed basic
        // content by permanent ID before those loaders see a catalog that no longer contains it.
        var absent = ContentIdentity.MissingBasicContent(content, new SaveReader(contentSection.Payload, SaveSections.Content));
        if (absent.Count > 0)
        {
            return MissingContent(details, absent);
        }
        MapData? map = null;
        if (settings.Map is not null)
        {
            var mapSection = byId.GetValueOrDefault(SaveSections.Map.Value) ?? throw new SaveFormatException("The save has no map section.");
            var mapReader = new SaveReader(mapSection.Payload, SaveSections.Map);
            map = MapData.ReadState(mapReader, settings.Map);
            mapReader.ExpectEnd();
        }

        var simulation = GameSimulation.CreateForRestore(settings, map, content, options.Rules ?? ConstructionRules.Default);
        simulation.ReadCoreState(core);
        core.ExpectEnd();

        var contentReader = new SaveReader(contentSection.Payload, SaveSections.Content);
        var (contentMap, missing, changed) = ContentIdentity.Read(simulation, contentReader);
        contentReader.ExpectEnd();
        if (missing.Count > 0)
        {
            return MissingContent(details, missing);
        }

        var systems = SaveSections.Systems;
        var all = new List<SaveSection>(systems.Count + options.AdditionalSections.Count);
        all.AddRange(systems);
        all.AddRange(options.AdditionalSections);
        for (var i = 0; i < all.Count; i++)
        {
            var section = all[i];
            if (byId.TryGetValue(section.Id.Value, out var data))
            {
                var reader = new SaveReader(data.Payload, section.Id, contentMap);
                section.Read(simulation, reader, data.Version);
                reader.ExpectEnd();
            }
            else if (section.IsPresent(simulation))
            {
                // A section added after the save was written: the system keeps its new-game defaults (FR-021).
            }

            progress?.Report(0.5f + (0.5f * (i + 1) / all.Count));
        }

        var status = changed ? SaveLoadStatus.LoadedWithChangedData : SaveLoadStatus.Loaded;
        return new SaveLoadResult(status, simulation, details, changed ? "The installed game data differs from the save's." : null) { ExtraSections = extras };
    }

    private static SaveLoadResult MissingContent(SaveDetails details, IReadOnlyList<string> missing) =>
        new(SaveLoadStatus.MissingContent, null, details, $"The save needs game data that is not installed: {string.Join(", ", missing)}.")
        {
            MissingContent = missing,
        };

    private static int? KnownAdditional(SaveLoadOptions options, SaveSectionId id)
    {
        foreach (var section in options.AdditionalSections)
        {
            if (section.Id == id)
            {
                return section.Version;
            }
        }

        return null;
    }

    private static byte[] Flatten(SaveBuffer buffer)
    {
        var bytes = new byte[buffer.Length];
        var at = 0;
        for (var i = 0; i < buffer.SegmentCount; i++)
        {
            var segment = buffer.Segment(i);
            segment.CopyTo(bytes.AsSpan(at));
            at += segment.Length;
        }

        return bytes;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The temporary file stays; the next save overwrites it.
        }
    }
}
