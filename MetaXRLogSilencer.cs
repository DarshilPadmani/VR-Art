using System;
using UnityEngine;

/// <summary>
/// Filters known noisy Meta XR app-space logs while leaving all other logs intact.
/// </summary>
public static class MetaXRLogSilencer
{
    private static bool _installed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallAtRuntime()
    {
        Install();
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void InstallInEditor()
    {
        Install();
    }
#endif

    private static void Install()
    {
        if (_installed)
        {
            return;
        }

        if (Debug.unityLogger.logHandler is FilteredLogHandler)
        {
            _installed = true;
            return;
        }

        Debug.unityLogger.logHandler = new FilteredLogHandler(Debug.unityLogger.logHandler);
        _installed = true;
    }

    private sealed class FilteredLogHandler : ILogHandler
    {
        private readonly ILogHandler _inner;

        public FilteredLogHandler(ILogHandler inner)
        {
            _inner = inner;
        }

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            string message = BuildMessage(format, args);

            if (logType == LogType.Log && ShouldSuppress(message))
            {
                return;
            }

            _inner.LogFormat(logType, context, format, args);
        }

        public void LogException(Exception exception, UnityEngine.Object context)
        {
            _inner.LogException(exception, context);
        }

        private static bool ShouldSuppress(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            return message.StartsWith("[OVRPlugin] ovrp_UnityOpenXR_OnAppSpaceChange2(", StringComparison.Ordinal)
                || message.StartsWith("[MetaXRFeature] OnAppSpaceChange:", StringComparison.Ordinal);
        }

        private static string BuildMessage(string format, object[] args)
        {
            if (string.IsNullOrEmpty(format))
            {
                return string.Empty;
            }

            if (args == null || args.Length == 0)
            {
                return format;
            }

            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }
    }
}