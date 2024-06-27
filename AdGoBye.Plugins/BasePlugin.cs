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

    public virtual EPatchResult Patch(Content context, ref ContentAssetManagerContainer assetContainer)
    {
        throw new NotImplementedException();
    }

    public virtual EVerifyResult Verify(Content context, ref readonly ContentAssetManagerContainer assetContainer)
    {
        return EVerifyResult.Success;
    }

    public virtual void Initialize(Content context)
    {
    }

    public virtual void PostPatch(Content context)
    {
    }

    public virtual void PostDiskWrite(Content context)
    {
        
    }

    public virtual bool WantsIndexerTracking()
    {
        return true;
    }
}