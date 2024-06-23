using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Serilog;
using Tomlyn;

namespace AdGoBye;

// The auto properties are implicitly used by Tomlyn, removing them breaks parsing.
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public static class Blocklist
{
    public static Dictionary<string, HashSet<GameObjectInstance>>? Blocks;
    private static readonly ILogger Logger = Log.ForContext(typeof(Blocklist));

    public class BlocklistModel
    {
        public string? Title { get; init; }
        public string? Description { get; init; }
        public string? Maintainer { get; init; }
        [JsonPropertyName("block")] public List<BlockEntry>? Blocks { get; init; }
    }

    public class BlockEntry
    {
        public string? FriendlyName { get; init; }
        public string? WorldId { get; init; }
        public List<GameObjectInstance>? GameObjects { get; init; }
    }


    public class GameObjectInstance
    {
        public required string Name { get; init; }
        public GameObjectPosition? Position { get; init; }
        public GameObjectInstance? Parent { get; init; }
    }

    public class GameObjectPosition
    {
        public required double X { get; init; }
        public required double Y { get; init; }
        public required double Z { get; init; }
    }

    public class NetworkBlocklist
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // Premature recommendation
        // ReSharper disable EntityFramework.ModelValidation.UnlimitedStringLength
        public required string Url { get; set; }
        public required string Contents { get; set; }

        public string? ETag { get; set; }
        // ReSharper enable EntityFramework.ModelValidation.UnlimitedStringLength
    }

    public static void ParseAllBlocklists()
    {
        Blocks = BlocklistsParser(GetBlocklists());
    }

    public static void UpdateNetworkBlocklists()
    {
        using var db = new State.IndexContext();
        var blocklistEntries = db.NetworkBlocklists;
        foreach (var danglingBlocklist in blocklistEntries.Where(blocklist =>
                     Settings.Options.BlocklistUrLs.All(url => url != blocklist.Url)))
        {
            Logger.Information("Removing dangling blocklist for {url}", danglingBlocklist.Url);
            blocklistEntries.RemoveRange(danglingBlocklist);
        }

        db.SaveChanges();

        foreach (var optionsUrl in Settings.Options.BlocklistUrLs)
        {
            var databaseQuery = blocklistEntries
                .FirstOrDefault(databaseEntry => databaseEntry.Url == optionsUrl);

            string? ETag = null;
            if (databaseQuery?.ETag != null) ETag = databaseQuery.ETag;

            var blocklistDownload = GetBlocklistFromUrl(optionsUrl, ETag);
            if (blocklistDownload is null) continue;

            if (databaseQuery is null)
            {
                var networkBlocklistElement = new NetworkBlocklist
                {
                    Url = optionsUrl,
                    Contents = blocklistDownload.Value.Result,
                    ETag = blocklistDownload.Value.ETag
                };
                blocklistEntries.Add(networkBlocklistElement);
                Logger.Information("Added network blocklist for {url}", optionsUrl);
            }
            else
            {
                databaseQuery.Contents = blocklistDownload.Value.Result;
                if (blocklistDownload.Value.ETag is not null) databaseQuery.ETag = blocklistDownload.Value.ETag;
            }

            db.SaveChanges();
        }
    }


    private static List<BlocklistModel> GetBlocklists()
    {
        using var db = new State.IndexContext();
        var final = new List<BlocklistModel>();
        Directory.CreateDirectory("./Blocklists");
        foreach (var file in Directory.GetFiles("./Blocklists"))
        {
            ParseAndAddBlocklist(file, File.ReadAllText(file));
        }

        if (Settings.Options.BlocklistUrLs.Length is 0) return final;

        foreach (var blocklist in db.NetworkBlocklists)
        {
            ParseAndAddBlocklist(blocklist.Url, blocklist.Contents);
        }

        return final;

        void ParseAndAddBlocklist(string location, string blocklistContent)
        {
            try
            {
                var blocklist = Toml.ToModel<BlocklistModel>(blocklistContent);
                Logger.Information("Read blocklist: {Name} ({Maintainer})", blocklist.Title,
                    blocklist.Maintainer);
                final.Add(blocklist);
            }
            catch (TomlException exception)
            {
                Logger.Error("Failed to parse blocklist {location}: {error}", location, exception.Message);
            }
        }
    }


    // ReSharper disable once InconsistentNaming
    private static (string Result, string? ETag)? GetBlocklistFromUrl(string url, string? ETag)
    {
        using HttpClient client = new();
        client.DefaultRequestHeaders.Add("User-Agent", "AdGoBye (https://github.com/AdGoBye/AdGoBye)");
        if (ETag is not null) client.DefaultRequestHeaders.Add("If-None-Match", ETag);
        var result = client.GetAsync(url);
        result.Wait();

        if (result.Result.StatusCode is HttpStatusCode.NotModified)
        {
            Logger.Verbose("{url} told us the resource has not been modified", url);
            return null;
        }

        if (!result.Result.IsSuccessStatusCode)
        {
            Logger.Error("Blocklist fetch for {url} failed! (Status code: {statusCode})", url,
                result.Result.StatusCode);
            return null;
        }

        var stringResult = result.Result.Content.ReadAsStringAsync();
        stringResult.Wait();
        return result.Result.Headers.ETag is not null
            ? (stringResult.Result, result.Result.Headers.ETag.Tag)
            : (stringResult.Result, null);
    }

    /// <summary>
    /// <para>
    /// <c>BlocklistsParser</c> converts the <c>BlocklistModel</c>s usually received from file blocklists and turns it
    /// into a deduplicated dictionary (per <c>HashSet</c>)
    /// </para>
    /// </summary>
    /// <param name="lists"> List of BlocklistModels, although only <c>Blocks</c> is used from it</param>
    /// <returns>Dictionary keyed to World ID string with value of a string Hashset containing GameObjects to be blocked</returns>
    public static Dictionary<string, HashSet<GameObjectInstance>> BlocklistsParser(List<BlocklistModel>? lists)
    {
        lists ??= GetBlocklists();
        var deduplicatedBlocklist = new Dictionary<string, HashSet<GameObjectInstance>>();
        foreach (var block in lists.SelectMany(list => list.Blocks!))
        {
            if (!deduplicatedBlocklist.TryGetValue(block.WorldId!, out _))
            {
                deduplicatedBlocklist.Add(block.WorldId!, [.. block.GameObjects!]);
                continue;
            }

            deduplicatedBlocklist[block.WorldId!].UnionWith(block.GameObjects!.ToHashSet());
        }

        return deduplicatedBlocklist;
    }


    public static List<GameObjectInstance>? Patch(ContentAssetManagerContainer assetContainer,
        GameObjectInstance[] gameObjectsToDisable)
    {
        var patchedGameObjects = new List<GameObjectInstance>();

        foreach (var gameObject in assetContainer.AssetsFile.file.GetAssetsOfType(AssetClassID.GameObject))
        {
            foreach (var blocklistGameObject in gameObjectsToDisable)
            {
                var baseGameObject =
                    assetContainer.Manager.GetBaseField(assetContainer.AssetsFile, gameObject);
                if (baseGameObject["m_Name"].AsString != blocklistGameObject.Name) continue;
                if (!baseGameObject["m_IsActive"].AsBool)
                {
                    patchedGameObjects.Add(blocklistGameObject);
                    continue;
                }

                if (blocklistGameObject.Parent is not null || blocklistGameObject.Position is not null)
                {
                    if (!ProcessPositionAndParent(blocklistGameObject, baseGameObject, assetContainer.AssetsFile))
                        continue;
                }

                baseGameObject["m_IsActive"].AsBool = false;
                gameObject.SetNewData(baseGameObject);
                assetContainer.Bundle.file.BlockAndDirInfo.DirectoryInfos[1]
                    .SetNewData(assetContainer.AssetsFile.file);
                patchedGameObjects.Add(blocklistGameObject);
                Logger.Debug("Found and disabled {gameObjectName}", blocklistGameObject.Name);
            }
        }

        var unpatchedObjects = gameObjectsToDisable.Except(patchedGameObjects).ToList();
        if (unpatchedObjects.Count != 0)
            Logger.Warning(
                "Following blocklist objects weren't disabled: {@UnpatchedList}" +
                "\nThis can mean that these blocklist entries are outdated, consider informing the maintainer",
                unpatchedObjects);
        if (Settings.Options.DryRun) return unpatchedObjects.Count != 0 ? unpatchedObjects : null;
        Logger.Information("Done, writing changes as bundle");

        return unpatchedObjects.Count != 0 ? unpatchedObjects : null;


        bool DoesParentMatch(AssetExternal FatherPos, AssetsFileInstance assetsFileInstance,
            GameObjectInstance blocklistGameObject)
        {
            var fatherGameObject =
                assetContainer.Manager.GetExtAsset(assetsFileInstance, FatherPos.baseField["m_GameObject"]);

            if (blocklistGameObject.Parent!.Position is not null)
            {
                if (!DoesPositionMatch(blocklistGameObject.Parent.Position, FatherPos.baseField["m_LocalPosition"]))
                    return false;
            }

            return fatherGameObject.baseField["m_Name"].AsString == blocklistGameObject.Parent.Name;
        }

        bool ProcessPositionAndParent(GameObjectInstance blocklistGameObject, AssetTypeValueField baseGameObject,
            AssetsFileInstance assetfileinstance)
        {
            foreach (var data in baseGameObject["m_Component.Array"])
            {
                var componentInstance = assetContainer.Manager.GetExtAsset(assetfileinstance, data["component"]);
                if ((AssetClassID)componentInstance.info.TypeId is not AssetClassID.RectTransform
                    and not AssetClassID.Transform)
                    continue;

                if (blocklistGameObject.Position is not null)
                {
                    if (!DoesPositionMatch(blocklistGameObject.Position,
                            componentInstance.baseField["m_LocalPosition"])) continue;
                }

                if (blocklistGameObject.Parent is not null)
                {
                    var fatherGameObject =
                        assetContainer.Manager.GetExtAsset(assetfileinstance,
                            componentInstance.baseField["m_Father"]);
                    if (!DoesParentMatch(fatherGameObject, assetfileinstance, blocklistGameObject)) return false;
                }
            }

            return true;
        }

        bool DoesPositionMatch(GameObjectPosition reference, AssetTypeValueField m_LocalPosition)
        {
            // m_LocalPosition's origin is float but TOML Parser dies when reference type is float so we need to cast it here
            switch (m_LocalPosition.FieldName)
            {
                case "x":
                    if ((float)reference.X != m_LocalPosition.AsFloat) return false;
                    break;
                case "y":
                    if ((float)reference.Y != m_LocalPosition.AsFloat) return false;
                    break;
                case "z":
                    if ((float)reference.Z != m_LocalPosition.AsFloat) return false;
                    break;
            }

            return true;
        }
    }

    private static CallbackObject ConstructUnmatchedPayload(Content content,
        IEnumerable<GameObjectInstance> unmatchedObjects)
    {
        return new CallbackObject
        {
            Version = content.VersionMeta.Version,
            WorldId = SHA256.HashData(Encoding.ASCII.GetBytes(content.Id)),
            UnmatchedObjects = unmatchedObjects.Select(gameObject => JsonSerializer.SerializeToUtf8Bytes(gameObject))
                .Select(SHA256.HashData).ToList()
        };
    }

    public static async void SendUnpatchedObjects(Content content, IEnumerable<GameObjectInstance> unmatchedObjects)
    {
        var payload = ConstructUnmatchedPayload(content, unmatchedObjects);
        Logger.Verbose("Callback Payload: {@payload}", payload);

        using var client = new HttpClient();
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(Settings.Options.BlocklistUnmatchedServer, payload);
        }
        catch (Exception e)
        {
            Logger.Error("Blocklist callback exception: {e}", e);
            return;
        }

        if (response.StatusCode != HttpStatusCode.NoContent)
            Logger.Warning("Blocklist callback failed:  [{statusCode}] {body}", response.StatusCode,
                await response.Content.ReadAsStringAsync());
        else
            Logger.Verbose("Blocklist callback response: [{statusCode}] {body}", response.StatusCode,
                await response.Content.ReadAsStringAsync());
    }

    public class CallbackObject
    {
        public required int Version { get; set; }
        public required byte[] WorldId { get; set; }
        public required List<byte[]> UnmatchedObjects { get; set; }
    }
}