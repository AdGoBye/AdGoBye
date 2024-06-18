using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AssetsTools.NET.Extra;

namespace AdGoBye.Types;

public enum ContentType
{
    Avatar,
    World
}

public record Content
{
    public required string Id { get; init; }
    public required ContentType Type { get; init; }
    public required ContentVersionMeta VersionMeta { get; set; }
    public required string StableContentName { get; init; }


    public record ContentVersionMeta
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public required int Version { get; set; }
        public required string Path { get; set; }
        public required List<string> PatchedBy { get; set; }
    }
}

public class ContentAssetManagerContainer
{
    public readonly AssetsFileInstance AssetsFile;
    public readonly BundleFileInstance Bundle;
    public readonly AssetsManager Manager;

    public ContentAssetManagerContainer(string contentPath)
    {
        Manager = new AssetsManager();
        Bundle = Manager.LoadBundleFile(contentPath);
        AssetsFile = Manager.LoadAssetsFileFromBundle(Bundle, 1);
    }
}