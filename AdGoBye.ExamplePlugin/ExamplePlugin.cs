﻿using AdGoBye.Plugins;
using AssetsTools.NET;
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

    public override EPatchResult Patch(string contentId, string dataDirectoryPath)
    {
        var manager = new AssetsManager();
        var dataLocation = dataDirectoryPath + "/__data";
        try
        {
            var bundleInstance = manager.LoadBundleFile(dataLocation);
            var bundle = bundleInstance.file;

            var assetFileInstance = manager.LoadAssetsFileFromBundle(bundleInstance, 1);
            var assetsFile = assetFileInstance.file;

            var foundOneChair = false;
            foreach (var monoBehaviour in assetsFile.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                var monoBehaviourInfo = manager.GetBaseField(assetFileInstance, monoBehaviour);
                if (monoBehaviourInfo["PlayerMobility"].IsDummy) continue;

                var parentGameObject = assetsFile.GetAssetInfo(monoBehaviourInfo["m_GameObject.m_PathID"].AsLong);
                var parentGameObjectInfo = manager.GetBaseField(assetFileInstance, parentGameObject);

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

            Logger.Verbose("Writing changes to bundle");
            bundle.BlockAndDirInfo.DirectoryInfos[1].SetNewData(assetsFile);
            using var writer = new AssetsFileWriter(dataLocation + ".mod");
            bundle.Write(writer);

            writer.Close();
            assetsFile.Close();
            bundle.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return EPatchResult.Fail;
        }

        File.Replace(dataLocation + ".mod", dataLocation, null);
        return EPatchResult.Success;
    }
}