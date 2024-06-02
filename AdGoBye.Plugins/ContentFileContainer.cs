using AssetsTools.NET.Extra;

namespace AdGoBye.Plugins;

public class ContentFileContainer
{
    public readonly AssetsFileInstance AssetsFile;
    public readonly BundleFileInstance Bundle;
    public readonly AssetsManager Manager;

    public ContentFileContainer(string contentPath)
    {
        Manager = new AssetsManager();
        Bundle = Manager.LoadBundleFile(contentPath);
        AssetsFile = Manager.LoadAssetsFileFromBundle(Bundle, 1);
    }
}