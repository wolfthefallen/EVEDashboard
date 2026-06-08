namespace EveDashboard.Sde.Conversion;

using System.Runtime.CompilerServices;
using System.Text.Json;
using EveDashboard.Core.Storage;
using EveDashboard.Sde.Schema;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="ISdeFormatConverter"/> implementation reading the everef.net
/// reference-data JSON layout (one folder, four required files: <c>meta.json</c>,
/// <c>categories.json</c>, <c>groups.json</c>, <c>types.json</c>).
/// </summary>
/// <remarks>
/// The small files (meta, categories, groups) are parsed via <see cref="JsonDocument.Parse(System.IO.Stream, JsonDocumentOptions)"/>.
/// <c>types.json</c> is streamed via <see cref="Utf8JsonReader"/> over a
/// growable <c>byte[]</c> buffer that is refilled from the file stream — keeps
/// peak memory bounded to roughly the size of the largest single record (a few KB
/// in practice). No <c>System.IO.Pipelines</c> dependency.
/// </remarks>
public sealed class EverefSdeFormatConverter : ISdeFormatConverter
{
    private readonly ILogger<EverefSdeFormatConverter> _log;

    /// <summary>Construct a converter with a logger for lifecycle / error events.</summary>
    /// <param name="log">Logger.</param>
    public EverefSdeFormatConverter(ILogger<EverefSdeFormatConverter> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public async Task ImportAsync(
        string jsonFolderPath,
        ISqliteDatabase target,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(jsonFolderPath);
        ArgumentNullException.ThrowIfNull(target);
        if (!Directory.Exists(jsonFolderPath))
            throw new DirectoryNotFoundException($"SDE JSON folder not found: {jsonFolderPath}");

        // Schema init happens BEFORE the transaction. PRAGMA journal_mode = WAL
        // (run inside SchemaInitializer) cannot be issued inside an open
        // transaction. Per plan 08 decision 8.22.
        await SchemaInitializer.EnsureSchemaAsync(target, ct).ConfigureAwait(false);

        await target.ExecuteNonQueryAsync("BEGIN TRANSACTION", null, ct).ConfigureAwait(false);
        try
        {
            var totalRows = await ImportInnerAsync(jsonFolderPath, target, progress, ct).ConfigureAwait(false);
            await target.ExecuteNonQueryAsync("COMMIT", null, ct).ConfigureAwait(false);
            _log.LogInformation("SDE import committed: {TotalRows} rows total", totalRows);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SDE import failed; rolling back transaction");
            try
            {
                await target.ExecuteNonQueryAsync("ROLLBACK", null, ct).ConfigureAwait(false);
            }
            catch (Exception rollbackEx)
            {
                _log.LogError(rollbackEx, "Rollback also failed");
            }
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ImportToOpenTransactionAsync(
        string jsonFolderPath,
        ISqliteDatabase target,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(jsonFolderPath);
        ArgumentNullException.ThrowIfNull(target);
        if (!Directory.Exists(jsonFolderPath))
            throw new DirectoryNotFoundException($"SDE JSON folder not found: {jsonFolderPath}");

        // No schema init here — caller's responsibility (decision 8.22). The
        // updater calls this against the live SDE which has been schema-initialised
        // at app startup; a caller invoking against an uninitialised database
        // will surface a clear missing-table error on the first INSERT.
        _ = await ImportInnerAsync(jsonFolderPath, target, progress, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared body of <see cref="ImportAsync"/> and
    /// <see cref="ImportToOpenTransactionAsync"/>. Resolves the four required
    /// JSON files and runs the four import stages (meta, categories, groups,
    /// types — the last also imports buffered <c>type_materials</c>). Assumes
    /// schema is initialised and a transaction is open on <paramref name="target"/>.
    /// </summary>
    /// <returns>Total row count written across all stages.</returns>
    private async Task<long> ImportInnerAsync(
        string jsonFolderPath,
        ISqliteDatabase target,
        IProgress<ImportProgress>? progress,
        CancellationToken ct)
    {
        var metaPath       = RequireFile(jsonFolderPath, "meta.json");
        var categoriesPath = RequireFile(jsonFolderPath, "categories.json");
        var groupsPath     = RequireFile(jsonFolderPath, "groups.json");
        var typesPath      = RequireFile(jsonFolderPath, "types.json");

        _log.LogInformation("Beginning SDE import from {Path}", jsonFolderPath);

        long totalRows = 0;
        totalRows = await ImportMetaAsync(metaPath, target, progress, totalRows, ct).ConfigureAwait(false);

        var (totalAfterCats, importedCategoryIds) =
            await ImportCategoriesAsync(categoriesPath, target, progress, totalRows, ct).ConfigureAwait(false);
        totalRows = totalAfterCats;

        var (totalAfterGroups, importedGroupIds) =
            await ImportGroupsAsync(groupsPath, target, progress, totalRows, importedCategoryIds, ct).ConfigureAwait(false);
        totalRows = totalAfterGroups;

        totalRows = await ImportTypesAsync(typesPath, target, progress, totalRows, importedGroupIds, ct).ConfigureAwait(false);

        return totalRows;
    }

    private static string RequireFile(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required everef.net file is missing: {name}", path);
        return path;
    }

    private static string ReadNameEn(JsonElement nameDict)
    {
        // everef names are { "en": "...", "de": "...", ... }. en is what we keep.
        if (nameDict.ValueKind == JsonValueKind.Object &&
            nameDict.TryGetProperty("en", out var en) &&
            en.ValueKind == JsonValueKind.String)
        {
            return en.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // meta.json
    // ─────────────────────────────────────────────────────────────────────────

    private const string MetaUpsertSql = """
        INSERT INTO meta (id, build_time, esi_sha256, sde_sha256, hoboleaks_sha256)
        VALUES (1, $build_time, $esi, $sde, $hobo)
        ON CONFLICT(id) DO UPDATE SET
            build_time       = excluded.build_time,
            esi_sha256       = excluded.esi_sha256,
            sde_sha256       = excluded.sde_sha256,
            hoboleaks_sha256 = excluded.hoboleaks_sha256;
        """;

    private async Task<long> ImportMetaAsync(
        string path,
        ISqliteDatabase target,
        IProgress<ImportProgress>? progress,
        long totalRows,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(false);
        var root = doc.RootElement;

        string? buildTime = root.TryGetProperty("build_time", out var bt) ? bt.GetString() : null;
        string? esi  = TryGetSha(root, "esi");
        string? sde  = TryGetSha(root, "sde");
        string? hobo = TryGetSha(root, "hoboleaks");

        if (buildTime is null)
            throw new JsonException("meta.json missing required 'build_time' field");

        await target.ExecuteNonQueryAsync(MetaUpsertSql,
            new Dictionary<string, object?>
            {
                ["$build_time"] = buildTime,
                ["$esi"]        = esi,
                ["$sde"]        = sde,
                ["$hobo"]       = hobo,
            }, ct).ConfigureAwait(false);

        totalRows += 1;
        progress?.Report(new ImportProgress("meta", 1, totalRows));
        return totalRows;
    }

    private static string? TryGetSha(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var section) &&
            section.ValueKind == JsonValueKind.Object &&
            section.TryGetProperty("sha256", out var sha) &&
            sha.ValueKind == JsonValueKind.String)
        {
            return sha.GetString();
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // categories.json — { "<id>": { category_id, name: { en, ... }, published } }
    // ─────────────────────────────────────────────────────────────────────────

    private const string CategoryUpsertSql = """
        INSERT INTO categories (category_id, name)
        VALUES ($category_id, $name)
        ON CONFLICT(category_id) DO UPDATE SET name = excluded.name;
        """;

    private async Task<(long Total, HashSet<long> ImportedIds)> ImportCategoriesAsync(
        string path,
        ISqliteDatabase target,
        IProgress<ImportProgress>? progress,
        long totalRows,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(false);

        var imported = new HashSet<long>();
        long thisTable = 0;
        foreach (var entry in doc.RootElement.EnumerateObject())
        {
            ct.ThrowIfCancellationRequested();
            var v = entry.Value;
            if (!v.TryGetProperty("published", out var pub) || !pub.GetBoolean())
                continue;

            var categoryId = v.GetProperty("category_id").GetInt64();
            var name = ReadNameEn(v.GetProperty("name"));

            await target.ExecuteNonQueryAsync(CategoryUpsertSql,
                new Dictionary<string, object?>
                {
                    ["$category_id"] = categoryId,
                    ["$name"]        = name,
                }, ct).ConfigureAwait(false);

            imported.Add(categoryId);
            thisTable++;
            totalRows++;
        }

        progress?.Report(new ImportProgress("categories", thisTable, totalRows));
        _log.LogInformation("Imported {Count} categories", thisTable);
        return (totalRows, imported);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // groups.json — { "<id>": { group_id, category_id, name, published } }
    // ─────────────────────────────────────────────────────────────────────────

    private const string GroupUpsertSql = """
        INSERT INTO groups (group_id, category_id, name)
        VALUES ($group_id, $category_id, $name)
        ON CONFLICT(group_id) DO UPDATE SET
            category_id = excluded.category_id,
            name        = excluded.name;
        """;

    private async Task<(long Total, HashSet<long> ImportedIds)> ImportGroupsAsync(
        string path,
        ISqliteDatabase target,
        IProgress<ImportProgress>? progress,
        long totalRows,
        HashSet<long> importedCategoryIds,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(false);

        var imported = new HashSet<long>();
        long thisTable = 0;
        long orphans = 0;
        foreach (var entry in doc.RootElement.EnumerateObject())
        {
            ct.ThrowIfCancellationRequested();
            var v = entry.Value;
            if (!v.TryGetProperty("published", out var pub) || !pub.GetBoolean())
                continue;

            var groupId    = v.GetProperty("group_id").GetInt64();
            var categoryId = v.GetProperty("category_id").GetInt64();

            // Orphan check: a published group whose category_id was filtered out
            // (because the category itself was unpublished). everef has ~46 of these.
            if (!importedCategoryIds.Contains(categoryId))
            {
                orphans++;
                continue;
            }

            var name = ReadNameEn(v.GetProperty("name"));

            await target.ExecuteNonQueryAsync(GroupUpsertSql,
                new Dictionary<string, object?>
                {
                    ["$group_id"]    = groupId,
                    ["$category_id"] = categoryId,
                    ["$name"]        = name,
                }, ct).ConfigureAwait(false);

            imported.Add(groupId);
            thisTable++;
            totalRows++;
        }

        progress?.Report(new ImportProgress("groups", thisTable, totalRows));
        if (orphans > 0)
        {
            _log.LogWarning("Skipped {Orphans} groups whose category_id was filtered (published group → unpublished category)", orphans);
        }
        _log.LogInformation("Imported {Count} groups", thisTable);
        return (totalRows, imported);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // types.json — streamed via Utf8JsonReader over a PipeReader
    // ─────────────────────────────────────────────────────────────────────────

    private const string TypeUpsertSql = """
        INSERT INTO types (type_id, group_id, name, volume, portion_size, is_ore, icon_id)
        VALUES ($type_id, $group_id, $name, $volume, $portion_size, $is_ore, $icon_id)
        ON CONFLICT(type_id) DO UPDATE SET
            group_id     = excluded.group_id,
            name         = excluded.name,
            volume       = excluded.volume,
            portion_size = excluded.portion_size,
            is_ore       = excluded.is_ore,
            icon_id      = excluded.icon_id;
        """;

    private const string TypeMaterialUpsertSql = """
        INSERT INTO type_materials (type_id, material_type_id, quantity)
        VALUES ($type_id, $material_type_id, $quantity)
        ON CONFLICT(type_id, material_type_id) DO UPDATE SET
            quantity = excluded.quantity;
        """;

    private async Task<long> ImportTypesAsync(
        string path,
        ISqliteDatabase target,
        IProgress<ImportProgress>? progress,
        long totalRows,
        HashSet<long> importedGroupIds,
        CancellationToken ct)
    {
        long typesImported = 0;
        long typeOrphans = 0;
        const long progressEvery = 1000;

        // Buffer type_materials and apply them after the type pass — guarantees that
        // when we UPSERT a material edge, BOTH the parent type AND the referenced
        // material_type exist in the DB (no FK timing problems within a single pass).
        var importedTypeIds = new HashSet<long>();
        var bufferedMaterials = new List<(long TypeId, long MaterialTypeId, long Quantity)>();

        await foreach (var typeJson in StreamTypeRecordsAsync(path, ct).ConfigureAwait(false))
        {
            // Filter published=false at the source.
            if (!typeJson.Published) continue;

            // Orphan check: published type whose group_id was filtered out.
            if (!importedGroupIds.Contains(typeJson.GroupId))
            {
                typeOrphans++;
                continue;
            }

            await target.ExecuteNonQueryAsync(TypeUpsertSql,
                new Dictionary<string, object?>
                {
                    ["$type_id"]      = typeJson.TypeId,
                    ["$group_id"]     = typeJson.GroupId,
                    ["$name"]         = typeJson.Name,
                    ["$volume"]       = typeJson.Volume,
                    ["$portion_size"] = typeJson.PortionSize,
                    ["$is_ore"]       = typeJson.IsOre ? 1 : 0,
                    ["$icon_id"]      = (object?)typeJson.IconId ?? DBNull.Value,
                }, ct).ConfigureAwait(false);

            importedTypeIds.Add(typeJson.TypeId);
            typesImported++;
            totalRows++;

            foreach (var m in typeJson.Materials)
            {
                bufferedMaterials.Add((typeJson.TypeId, m.MaterialTypeId, m.Quantity));
            }

            if (typesImported % progressEvery == 0)
            {
                progress?.Report(new ImportProgress("types", typesImported, totalRows));
            }
        }

        progress?.Report(new ImportProgress("types", typesImported, totalRows));
        if (typeOrphans > 0)
        {
            _log.LogWarning("Skipped {Orphans} types whose group_id was filtered (published type → unpublished group)", typeOrphans);
        }
        _log.LogInformation("Imported {Types} types", typesImported);

        // Now import the buffered materials, filtering material_type_id orphans.
        long materialsImported = 0;
        long materialOrphans = 0;
        foreach (var (typeId, matId, qty) in bufferedMaterials)
        {
            ct.ThrowIfCancellationRequested();

            // Both endpoints must exist in the imported types set. typeId is
            // guaranteed by construction (we only buffer materials of imported types),
            // but check for symmetry / future-proofing. matId may be an orphan
            // (everef has ~1 such material in practice).
            if (!importedTypeIds.Contains(matId))
            {
                materialOrphans++;
                continue;
            }

            await target.ExecuteNonQueryAsync(TypeMaterialUpsertSql,
                new Dictionary<string, object?>
                {
                    ["$type_id"]          = typeId,
                    ["$material_type_id"] = matId,
                    ["$quantity"]         = qty,
                }, ct).ConfigureAwait(false);

            materialsImported++;
            totalRows++;

            if (materialsImported % progressEvery == 0)
            {
                progress?.Report(new ImportProgress("type_materials", materialsImported, totalRows));
            }
        }

        progress?.Report(new ImportProgress("type_materials", materialsImported, totalRows));
        if (materialOrphans > 0)
        {
            _log.LogWarning("Skipped {Orphans} type_materials whose material_type_id was filtered (published type → unpublished material)", materialOrphans);
        }
        _log.LogInformation("Imported {Materials} type_materials", materialsImported);

        return totalRows;
    }

    /// <summary>
    /// Streams one <see cref="TypeRecord"/> at a time from a top-level dictionary
    /// JSON file (everef <c>types.json</c>). Uses a growable <c>byte[]</c> buffer
    /// refilled from the file stream and a state-preserving <see cref="Utf8JsonReader"/>
    /// to keep peak memory bounded regardless of file size.
    /// </summary>
    private static async IAsyncEnumerable<TypeRecord> StreamTypeRecordsAsync(
        string path,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const int initialBufferSize = 256 * 1024;     // 256 KB — fits any single record many times over
        const int readChunkSize = 64 * 1024;          // 64 KB per refill from disk

        await using var stream = File.OpenRead(path);

        var buffer = new byte[initialBufferSize];
        int bufferUsed = 0;                           // bytes of valid data starting at buffer[0]
        bool isFinalBlock = false;
        var state = new ParseState();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Refill: read from disk into the unused tail of the buffer.
            if (!isFinalBlock)
            {
                if (bufferUsed == buffer.Length)
                {
                    // Couldn't consume anything last round AND buffer is full → grow.
                    var grown = new byte[buffer.Length * 2];
                    Buffer.BlockCopy(buffer, 0, grown, 0, bufferUsed);
                    buffer = grown;
                }

                int wanted = Math.Min(readChunkSize, buffer.Length - bufferUsed);
                int read = await stream.ReadAsync(buffer.AsMemory(bufferUsed, wanted), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    isFinalBlock = true;
                }
                else
                {
                    bufferUsed += read;
                }
            }

            // Run a synchronous parse pass over the current buffer.
            // The sync helper uses ref Utf8JsonReader (a ref struct), which
            // C# 12 forbids inside async iterators — hence the split.
            var pendingRecords = new List<TypeRecord>();
            int consumed = ProcessTypesBuffer(
                buffer.AsSpan(0, bufferUsed), isFinalBlock, ref state, pendingRecords);

            // Slide unconsumed bytes to the start of the buffer.
            if (consumed > 0 && consumed < bufferUsed)
            {
                Buffer.BlockCopy(buffer, consumed, buffer, 0, bufferUsed - consumed);
            }
            bufferUsed -= consumed;

            // Yield any records we captured this round.
            foreach (var rec in pendingRecords) yield return rec;

            if (state.SawOuterEnd) yield break;

            if (isFinalBlock && consumed == 0 && bufferUsed > 0)
            {
                throw new JsonException(
                    $"types.json: unable to parse trailing {bufferUsed} bytes after end of stream");
            }

            if (isFinalBlock && bufferUsed == 0)
            {
                yield break;
            }
        }
    }

    private struct ParseState
    {
        public JsonReaderState ReaderState;
        public bool SawOuterStart;
        public bool SawOuterEnd;

        public ParseState()
        {
            ReaderState = new JsonReaderState(new JsonReaderOptions { AllowTrailingCommas = true });
            SawOuterStart = false;
            SawOuterEnd = false;
        }
    }

    private static int ProcessTypesBuffer(
        ReadOnlySpan<byte> data,
        bool isFinalBlock,
        ref ParseState state,
        List<TypeRecord> output)
    {
        var reader = new Utf8JsonReader(data, isFinalBlock, state.ReaderState);

        // Track the last "safely processed" position. When a record's value
        // spans past the current buffer, we need to rewind to BEFORE the
        // PropertyName/value pair so the next buffer pass re-attempts the
        // full record from a clean state. Capturing pre-iteration state and
        // bytesConsumed at the top of each loop gives us that rewind point.
        long safeBytesConsumed = reader.BytesConsumed;
        var safeState = reader.CurrentState;

        while (true)
        {
            // Snapshot before this iteration's Read.
            long preReadBytesConsumed = reader.BytesConsumed;
            var preReadState = reader.CurrentState;

            if (!reader.Read())
            {
                // Couldn't complete a token from the current buffer. Stop;
                // outer loop will refill. Save the pre-read position so we
                // re-attempt the same Read with more bytes available.
                safeBytesConsumed = preReadBytesConsumed;
                safeState = preReadState;
                break;
            }

            if (!state.SawOuterStart)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                    throw new JsonException("types.json: expected top-level object");
                state.SawOuterStart = true;
                safeBytesConsumed = reader.BytesConsumed;
                safeState = reader.CurrentState;
                continue;
            }

            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 0)
            {
                state.SawOuterEnd = true;
                safeBytesConsumed = reader.BytesConsumed;
                safeState = reader.CurrentState;
                break;
            }

            if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
            {
                // Advance to the value.
                if (!reader.Read())
                {
                    // Out of buffer between PropertyName and value — rewind to
                    // before the PropertyName so the next pass re-starts here.
                    safeBytesConsumed = preReadBytesConsumed;
                    safeState = preReadState;
                    break;
                }

                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    if (!reader.TrySkip())
                    {
                        safeBytesConsumed = preReadBytesConsumed;
                        safeState = preReadState;
                        break;
                    }
                    safeBytesConsumed = reader.BytesConsumed;
                    safeState = reader.CurrentState;
                    continue;
                }

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.ParseValue(ref reader);
                }
                catch (JsonException) when (!isFinalBlock)
                {
                    // Value spans past the buffer. Rewind to BEFORE the
                    // PropertyName so the entire record is retried after refill.
                    safeBytesConsumed = preReadBytesConsumed;
                    safeState = preReadState;
                    break;
                }

                using (doc)
                {
                    var record = ParseTypeRecord(doc.RootElement);
                    if (record is not null) output.Add(record);
                }
                safeBytesConsumed = reader.BytesConsumed;
                safeState = reader.CurrentState;
            }
        }

        state.ReaderState = safeState;
        return (int)safeBytesConsumed;
    }

    private static TypeRecord? ParseTypeRecord(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;

        if (!value.TryGetProperty("type_id", out var typeIdEl)) return null;
        if (!value.TryGetProperty("group_id", out var groupIdEl)) return null;
        if (!value.TryGetProperty("volume", out var volumeEl)) return null;
        if (!value.TryGetProperty("portion_size", out var portionSizeEl)) return null;

        var typeId = typeIdEl.GetInt64();
        var groupId = groupIdEl.GetInt64();
        var volume = volumeEl.ValueKind == JsonValueKind.Number ? volumeEl.GetDouble() : 0.0;
        var portionSize = portionSizeEl.ValueKind == JsonValueKind.Number ? portionSizeEl.GetInt64() : 0L;

        var name = value.TryGetProperty("name", out var nameEl) ? ReadNameEn(nameEl) : string.Empty;

        var published = value.TryGetProperty("published", out var pubEl) && pubEl.GetBoolean();
        var isOre     = value.TryGetProperty("is_ore",    out var oreEl) && oreEl.GetBoolean();

        long? iconId = null;
        if (value.TryGetProperty("icon_id", out var iconEl) && iconEl.ValueKind == JsonValueKind.Number)
        {
            iconId = iconEl.GetInt64();
        }

        var materials = new List<TypeMaterial>();
        if (value.TryGetProperty("type_materials", out var matsEl) &&
            matsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var matEntry in matsEl.EnumerateObject())
            {
                var mv = matEntry.Value;
                if (mv.ValueKind != JsonValueKind.Object) continue;
                if (!mv.TryGetProperty("material_type_id", out var midEl)) continue;
                if (!mv.TryGetProperty("quantity", out var qtyEl)) continue;
                materials.Add(new TypeMaterial(midEl.GetInt64(), qtyEl.GetInt64()));
            }
        }

        return new TypeRecord(typeId, groupId, name, volume, portionSize, isOre, iconId, published, materials);
    }

    /// <summary>One row's worth of data extracted from <c>types.json</c>.</summary>
    private sealed record TypeRecord(
        long TypeId,
        long GroupId,
        string Name,
        double Volume,
        long PortionSize,
        bool IsOre,
        long? IconId,
        bool Published,
        IReadOnlyList<TypeMaterial> Materials);

    /// <summary>One row from a <c>type_materials</c> sub-dictionary inside a type record.</summary>
    private sealed record TypeMaterial(long MaterialTypeId, long Quantity);
}
