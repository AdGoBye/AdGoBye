using AdGoBye.Plugins;
using AssetsTools.NET.Extra;
using Serilog;

namespace AdGoBye.ExamplePlugin;

public class ExamplePlugin : BasePlugin
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ExamplePlugin));

    public override EPluginType PluginType()
    {
        return EPluginType.Global;
    }

    public override EPatchResult Patch(ref ContentFileContainer fileContainer, bool dryRunRequested)
    {
        try
        {
            var assetsFile = fileContainer.AssetsFile.file;

            var foundOneChair = false;
            foreach (var monoBehaviour in assetsFile.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                var monoBehaviourInfo = fileContainer.Manager.GetBaseField(fileContainer.AssetsFile, monoBehaviour);
                if (monoBehaviourInfo["PlayerMobility"].IsDummy) continue;

                var parentGameObject = assetsFile.GetAssetInfo(monoBehaviourInfo["m_GameObject.m_PathID"].AsLong);
                var parentGameObjectInfo =
                    fileContainer.Manager.GetBaseField(fileContainer.AssetsFile, parentGameObject);

                if (parentGameObjectInfo["m_IsActive"].AsBool is false) continue;
                Logger.Verbose("Found chair on '{name}' [{PathID}], disabling", parentGameObjectInfo["m_Name"].AsString,
                    parentGameObject.PathId);

                parentGameObjectInfo["m_IsActive"].AsBool = false;
                parentGameObject.SetNewData(parentGameObjectInfo);

                if (foundOneChair) continue;
                foundOneChair = true;
            }

            if (!foundOneChair)
            {
                Logger.Verbose("Skipping, no chairs found");
                return EPatchResult.Skipped;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return EPatchResult.Fail;
        }

        return EPatchResult.Success;
    }
}