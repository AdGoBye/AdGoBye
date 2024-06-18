using AdGoBye.Types;

namespace AdGoBye.Plugins;

public class BasePlugin : IPlugin
{
    public virtual EPluginType PluginType()
    {
        return EPluginType.Global;
    }

    public virtual string[]? ResponsibleForContentIds()
    {
        if (PluginType() == EPluginType.Global) return null;

        throw new NotImplementedException();
    }

    public virtual bool OverrideBlocklist(Content context)
    {
        return false;
    }

    public virtual EPatchResult Patch(Content context, ref ContentAssetManagerContainer assetContainer,
        bool dryRunRequested)
    {
        throw new NotImplementedException();
    }

    public virtual EVerifyResult Verify(Content context, ref readonly ContentAssetManagerContainer assetContainer)
    {
        return EVerifyResult.Success;
    }

    public void Initialize(Content context)
    {
    }

    public void PostPatch(Content context)
    {
    }

    public bool WantsIndexerTracking()
    {
        return true;
    }
}