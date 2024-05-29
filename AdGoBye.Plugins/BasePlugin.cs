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

    public virtual bool OverrideBlocklist(string contentId)
    {
        return false;
    }

    public virtual EPatchResult Patch(ref ContentFileContainer fileContainer, bool dryRunRequested)
    {
        throw new NotImplementedException();
    }

    public virtual EVerifyResult Verify(ref readonly ContentFileContainer fileContainer)
    {
        return EVerifyResult.Success;
    }

    public void Initialize()
    {
    }

    public void PostPatch()
    {
    }

    public bool WantsIndexerTracking()
    {
        return true;
    }
}