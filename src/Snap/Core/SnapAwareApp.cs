using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NuGet.Versioning;
using Snap.AnyOS;
using Snap.Core.Models;
using Snap.Core.UnitTest;
using Snap.Extensions;
using Snap.Logging;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public static class SnapAwareApp
    {
        static readonly ILog Logger = LogProvider.GetLogger(nameof(SnapAwareApp));
        static readonly object SyncRoot = new object();
        
        static ISnapOs SnapOs { get; }

        static SnapAwareApp()
        {
            lock (SyncRoot)
            {
                if (SnapOs != null)
                {
                    return;
                }
            }

            if (Current != null)
            {
                if (ModeDetector.InUnitTestRunner())
                {
                    return;
                }
                
                throw new Exception($"Expected {nameof(Current)} to equal null.");
            }
                        
            SnapOs = AnyOS.SnapOs.AnyOs;

            try
            {
                WorkingDirectory = SnapOs.Filesystem.PathGetDirectoryName(typeof(SnapAwareApp).Assembly.Location);
                Current = WorkingDirectory.GetSnapAppFromDirectory(SnapOs.Filesystem, new SnapAppReader());
            }
            catch (Exception e)
            {
                Logger.ErrorException("Failed to load snap manifest.", e);
            }
        }
        
        /// <summary>
        /// Current application release information.
        /// </summary>
        public static SnapApp Current { get; internal set; }
        
        /// <summary>
        /// Current application working directory.
        /// </summary>
        public static string WorkingDirectory { get; }

        /// <summary>
        /// Call this method as early as possible in app startup. This method
        /// will dispatch to your methods to set up your app. Depending on the
        /// parameter, your app will exit after this method is called, which 
        /// is required by Snap. 
        /// </summary>
        /// <param name="onInstalled">Called when your app is initially
        /// installed. Your application will not exit afterwards.
        /// </param>
        /// <param name="onUpdated">Called when your app is updated to a new
        /// version. Your application will exit afterwards.</param>
        /// <param name="arguments">Use in a unit-test runner to mock the 
        /// arguments. In your app, leave this as null.</param>
        public static void HandleEvents(
            Action<SemanticVersion> onInstalled = null,
            Action<SemanticVersion> onUpdated = null,
            string[] arguments = null)
        {
            void DefaultBlock(SemanticVersion v)
            {
            }

            var args = arguments ?? Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (args.Length == 0) return;

            var invoke = new[] {
                new { Key = "--snap-installed", Value = onInstalled ??  DefaultBlock },
                new { Key = "--snap-updated", Value = onUpdated ??  DefaultBlock }
            }.ToDictionary(k => k.Key, v => v.Value);

            var actionName = args[0];
            if (!invoke.ContainsKey(actionName))
            {
                return;
            }

            try
            {
                invoke[actionName](Current.Version);
                Logger.Trace($"Successfully handled event: {actionName}.");
                if (!string.Equals(actionName, "--snap-install", StringComparison.InvariantCulture))
                {
                    #if SNAP_NUPKG
                    Environment.Exit(0);
                    #endif
                }
            }
            catch (Exception ex)
            {
                Logger.ErrorException($"Exception thrown while handling snap event. Action: {actionName}", ex);
                #if SNAP_NUPKG
                Environment.Exit(-1);
                #endif
            }
        }
    }
}
