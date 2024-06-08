// Code is used externally
// ReSharper disable UnusedParameter.Global
// ReSharper disable UnusedMethodReturnValue.Global

namespace AdGoBye.Plugins;

public interface IPlugin
{
    /// <returns>The <see cref="EPluginType">EPluginType</see> of this plugin.</returns>
    EPluginType PluginType();

    /// <returns>
    ///     An array of Content IDs that a given plugin is responsible for.
    ///     This is ignored if <see cref="PluginType" /> is <see cref="EPluginType.Global" />.
    /// </returns>
    string[]? ResponsibleForContentIds();

    /// <summary>
    ///     A plugin can choose to override the default and user-specific blocklists
    ///     (in case the logic should be directly handled by the plugin).
    /// </summary>
    /// <param name="contentId">The id of the content that is to be evaluated</param>
    /// <returns>Whether the plugin will override the blocklist.</returns>
    bool OverrideBlocklist(string contentId);

    /// <summary>
    /// WantsIndexerTracking allows a Plugin to pick if it wants the Indexer to skip it when the Indexer thinks
    /// the Plugin has already patched the file.
    /// </summary>
    /// <remarks>
    /// When a Plugin patches a file, the Indexer keeps track of the Plugin that modified that version of the file,
    /// which non-deterministic and exotically designed Plugins may not benifit from.
    ///</remarks>
    /// <returns>Boolean indicating if the Indexer should skip if thought already patched.</returns>
    bool WantsIndexerTracking();

    /// <summary>
    ///     Patch is the main entrypoint to a plugin's operations. Plugins are expected to carry out their respective
    ///     behaviours in this method.
    /// </summary>
    /// <param name="fileContainer">Container for the underlying asset being operated on</param>
    /// <param name="dryRunRequested">Bool representing if the current operation is a dry run, you should only simluate changes if this is true</param>
    /// <returns>A <see cref="EPatchResult" /> that signifies the result of the plugin's patch operation</returns>
    EPatchResult Patch(ref ContentFileContainer fileContainer, bool dryRunRequested);

    /// <summary>
    ///     Verify is a non-edit stage where Plugins can run environment and validity checks on the asset before
    ///     operating on it in <see cref="Patch"/>.
    /// </summary>
    /// <param name="fileContainer">Container for the underlying asset being operated on</param>
    /// <returns>A <see cref="EVerifyResult" /> that signifies the result of the plugin's verify operation.
    ///          Non-<see cref="EVerifyResult.Success"/> returns will skip this Plugin from being executed. </returns>
    EVerifyResult Verify(ref readonly ContentFileContainer fileContainer);

    /// <summary>
    /// Initialize is an optional function ran before <see cref="Verify"/> which Plugins may use to prepare their state
    /// before patching a world. It's part of the patching loop and therefore may be called multiple times.
    /// </summary>
    void Initialize();

    /// <summary>
    /// PostPatch is a function that Plugins can use to clean up after themselves after execution.
    /// </summary>
    void PostPatch();
}