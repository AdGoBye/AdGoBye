/* Josip Medved <jmedved@jmedved.com> * www.medo64.com * MIT License */

//2022-12-01: Compatible with .NET 6 and 7
//2012-11-24: Suppressing bogus CA5122 warning (http://connect.microsoft.com/VisualStudio/feedback/details/729254/bogus-ca5122-warning-about-p-invoke-declarations-should-not-be-safe-critical)
//2010-10-07: Added IsOtherInstanceRunning method
//2008-11-14: Reworked code to use SafeHandle
//2008-04-11: Cleaned code to match FxCop 1.36 beta 2 (SpecifyMarshalingForPInvokeStringArguments, NestedTypesShouldNotBeVisible)
//2008-04-10: NewInstanceEventArgs is not nested class anymore
//2008-01-26: AutoExit parameter changed to NoAutoExit
//2008-01-08: Main method is now called Attach
//2008-01-06: System.Environment.Exit returns E_ABORT (0x80004004)
//2008-01-03: Added Resources
//2007-12-29: New version

using Serilog;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
// ReSharper disable All

namespace AdGoBye;

/// <summary>
/// Handles detection and communication of programs multiple instances.
/// This class is thread safe.
/// </summary>
public static class SingleInstance {

    private static Mutex? _mtxFirstInstance;
    private static Thread? _thread;
    private static readonly object _syncRoot = new();
    private static readonly ILogger _logger = Log.ForContext(typeof(SingleInstance));


    /// <summary>
    /// Returns true if this application is not already started.
    /// Another instance is contacted via named pipe.
    /// </summary>
    /// <exception cref="InvalidOperationException">API call failed.</exception>
    public static bool Attach() {
        return Attach(false);
    }

    /// <summary>
    /// Returns true if this application is not already started.
    /// Another instance is contacted via named pipe.
    /// </summary>
    /// <param name="noAutoExit">If true, application will exit after informing another instance.</param>
    /// <exception cref="InvalidOperationException">API call failed.</exception>
    public static bool Attach(bool noAutoExit) {
        lock (_syncRoot) {
            var isFirstInstance = false;
            try {
                var mutexName = @"Global\" + MutexName;
                _mtxFirstInstance = new Mutex(initiallyOwned: true, mutexName, out isFirstInstance);
                _logger.Debug("Mutex name: {mutexName}", mutexName);
                if (isFirstInstance == false) { //we need to contact previous instance
                    var contentObject = new InstanceInformation() {
                        CommandLine = Environment.CommandLine,
                        CommandLineArgs = Environment.GetCommandLineArgs(),
                        ProcessId = Process.GetCurrentProcess().Id
                    };
                    var contentBytes = JsonSerializer.SerializeToUtf8Bytes(contentObject);
                    using var clientPipe = new NamedPipeClientStream(".",
                                                                     MutexName,
                                                                     PipeDirection.Out,
                                                                     PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough);
                    clientPipe.Connect();
                    clientPipe.Write(contentBytes, 0, contentBytes.Length);
                } else {  //there is no application already running.
                    _thread = new Thread(Run) {
                        Name = typeof(SingleInstance).FullName,
                        IsBackground = true
                    };
                    _thread.Start();
                }
            } catch (Exception ex) {
                _logger.Error(ex, "Error in {methodName}", nameof(Attach));
            }

            if ((isFirstInstance == false) && (noAutoExit == false)) {
                _logger.Error("Exiting because another instance is already running.");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    Environment.Exit(unchecked((int)0x80004004));  // E_ABORT(0x80004004)
                } else {
                    Environment.Exit(114);  // EALREADY(114)
                }
            }

            return isFirstInstance;
        }
    }

    private static string? _mutexName;
    private static string MutexName {
        get {
            lock (_syncRoot) {
                if (_mutexName == null) {
                    var programName = "AdGoBye";

                    var sbMutextName = new StringBuilder();
                    sbMutextName.Append(programName, 0, Math.Min(programName.Length, 31));
                    sbMutextName.Append('.');

                    var sbHash = new StringBuilder();
                    sbHash.AppendLine(Environment.MachineName);
                    sbHash.AppendLine(Environment.UserName);
                    foreach (var b in SHA256.HashData(Encoding.UTF8.GetBytes(sbHash.ToString()))) {
                        if (sbMutextName.Length == 63) { sbMutextName.AppendFormat("{0:X1}", b >> 4); }  // just take the first nubble
                        if (sbMutextName.Length == 64) { break; }
                        sbMutextName.AppendFormat("{0:X2}", b);
                    }
                    _mutexName = sbMutextName.ToString();
                }
                return _mutexName;
            }
        }
    }

    /// <summary>
    /// Gets whether there is another instance running.
    /// It temporary creates mutex.
    /// </summary>
    public static bool IsOtherInstanceRunning {
        get {
            lock (_syncRoot) {
                if (_mtxFirstInstance != null) {
                    return false; //no other instance is running
                } else {
                    var tempInstance = new Mutex(true, MutexName, out var isFirstInstance);
                    tempInstance.Close();
                    return (isFirstInstance == false);
                }
            }
        }
    }

    /// <summary>
    /// Thread function.
    /// </summary>
    private static void Run() {
        using var serverPipe = new NamedPipeServerStream(MutexName,
                                                         PipeDirection.In,
                                                         maxNumberOfServerInstances: 1,
                                                         PipeTransmissionMode.Byte,
                                                         PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough);
        while (_mtxFirstInstance != null) {
            try {
                if (!serverPipe.IsConnected) { serverPipe.WaitForConnection(); }
                var contentObject = JsonSerializer.Deserialize<InstanceInformation>(serverPipe);
                serverPipe.Disconnect();
                if (contentObject != null) {
                    _logger.Warning("Another instance attempted to start: {instanceInformation}", JsonSerializer.Serialize(contentObject));
                }
            } catch (Exception ex) {
                _logger.Error(ex, "Error in {methodName}", nameof(Run));
                Thread.Sleep(100);
            }
        }
    }


    [Serializable]
    private sealed record InstanceInformation {  // just a storage
        [JsonInclude]
        public required string CommandLine;

        [JsonInclude]
        public required string[] CommandLineArgs;

        [JsonInclude]
        public required int ProcessId;
    }

}
