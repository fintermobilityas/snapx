// Copyright (c) 2019 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using JetBrains.Annotations;

namespace Snap.Core.UnitTest
{

    /// <summary>
    /// Detects if unit tests or design mode are currently running for the current application or library.
    /// </summary>
    public interface IModeDetector
    {
        /// <summary>
        /// Gets a value indicating whether the current library or application is running through a unit test.
        /// </summary>
        /// <returns>If we are currently running in a unit test.</returns>
        bool? InUnitTestRunner();

        /// <summary>
        /// Gets a value indicating whether the current library or application is running in a GUI design mode tool.
        /// </summary>
        /// <returns>If we are currently running in design mode.</returns>
        bool? InDesignMode();
    }

    /// <summary>
    /// A helper class which detect if we are currently running via a unit test or design mode.
    /// </summary>
    internal static class ModeDetector
    {
        const string DetectPlatformDetectorName = "Splat.PlatformModeDetector";
        const string XamlDesignPropertiesType = "System.ComponentModel.DesignerProperties, System.Windows, Version=2.0.5.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e";
        const string XamlControlBorderType = "System.Windows.Controls.Border, System.Windows, Version=2.0.5.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e";
        const string XamlDesignPropertiesDesignModeMethodName = "GetIsInDesignMode";
        const string WpfDesignerPropertiesType = "System.ComponentModel.DesignerProperties, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35";
        const string WpfDesignerPropertiesDesignModeMethod = "GetIsInDesignMode";
        const string WpfDependencyPropertyType = "System.Windows.DependencyObject, WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35";
        const string WinFormsDesignerPropertiesType = "Windows.ApplicationModel.DesignMode, Windows, ContentType=WindowsRuntime";
        const string WinFormsDesignerPropertiesDesignModeMethod = "DesignModeEnabled";

        static bool? _cachedInUnitTestRunnerResult;
        static bool? _cachedInDesignModeResult;

        /// <summary>
        /// Initializes static members of the <see cref="ModeDetector"/> class.
        /// </summary>
        static ModeDetector()
        {
            Current = AssemblyFinder.AttemptToLoadType<IModeDetector>(DetectPlatformDetectorName);
        }

        /// <summary>
        /// Gets or sets the current mode detector set.
        /// </summary>
        static IModeDetector Current { get; set; }

        /// <summary>
        /// Overrides the mode detector with one of your own provided ones.
        /// </summary>
        /// <param name="modeDetector">The mode detector to use.</param>
        [UsedImplicitly]
        public static void OverrideModeDetector(IModeDetector modeDetector)
        {
            Current = modeDetector;
            _cachedInDesignModeResult = null;
            _cachedInUnitTestRunnerResult = null;
        }

        /// <summary>
        /// Gets a value indicating whether we are currently running from a unit test.
        /// </summary>
        /// <returns>If we are currently running from a unit test.</returns>
        public static bool InUnitTestRunner()
        {
            if (_cachedInUnitTestRunnerResult.HasValue)
            {
                return _cachedInUnitTestRunnerResult.Value;
            }

            if (Current != null)
            {
                _cachedInUnitTestRunnerResult = Current.InUnitTestRunner();
                if (_cachedInUnitTestRunnerResult.HasValue)
                {
                    return _cachedInUnitTestRunnerResult.Value;
                }
            }

            // We have no sane platform-independent way to detect a unit test
            // runner :-/
            return false;
        }

        /// <summary>
        /// Gets a value indicating whether we are currently running from within a GUI design editor.
        /// </summary>
        /// <returns>If we are currently running from design mode.</returns>
        [UsedImplicitly]
        public static bool InDesignMode()
        {
            if (_cachedInDesignModeResult.HasValue)
            {
                return _cachedInDesignModeResult.Value;
            }

            if (Current != null)
            {
                _cachedInDesignModeResult = Current.InDesignMode();
                if (_cachedInDesignModeResult.HasValue)
                {
                    return _cachedInDesignModeResult.Value;
                }
            }

            // Check Silverlight / WP8 Design Mode
            var type = Type.GetType(XamlDesignPropertiesType, false);
            if (type != null)
            {
                var mInfo = type.GetMethod(XamlDesignPropertiesDesignModeMethodName);
                var dependencyObject = Type.GetType(XamlControlBorderType, false);

                if (dependencyObject != null)
                {
                    _cachedInDesignModeResult = (bool)mInfo.Invoke(null, new[] { Activator.CreateInstance(dependencyObject) });
                }
            }
            else if ((type = Type.GetType(WpfDesignerPropertiesType, false)) != null)
            {
                // loaded the assembly, could be .net
                var mInfo = type.GetMethod(WpfDesignerPropertiesDesignModeMethod);
                var dependencyObject = Type.GetType(WpfDependencyPropertyType, false);
                if (dependencyObject != null)
                {
                    _cachedInDesignModeResult = (bool)mInfo.Invoke(null, new[] { Activator.CreateInstance(dependencyObject) });
                }
            }
            else if ((type = Type.GetType(WinFormsDesignerPropertiesType, false)) != null)
            {
                // check WinRT next
                _cachedInDesignModeResult = (bool)type.GetProperty(WinFormsDesignerPropertiesDesignModeMethod).GetMethod.Invoke(null, null);
            }
            else
            {
                _cachedInDesignModeResult = false;
            }

            return _cachedInDesignModeResult.GetValueOrDefault();
        }
    }
}
