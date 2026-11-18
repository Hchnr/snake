// Copied into a temporary project by build-wechat-isolated.py.
// The SDK stays outside the repository and this build never uploads artifacts.
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WeChatWASM;

public static class WeChatIsolatedBuild
{
    public static void Run()
    {
        try
        {
            var prepare = typeof(SnakeClub.Editor.SnakeBuilder).GetMethod("EnsureScene", BindingFlags.NonPublic | BindingFlags.Static);
            if (prepare == null) throw new InvalidOperationException("Snake scene preparation is unavailable");
            prepare.Invoke(null, null);
            var config = WXConvertCore.config;
            string output = Require("SNAKE_WECHAT_OUTPUT");
            config.ProjectConf.projectName = "Snake Club";
            config.ProjectConf.Appid = Require("SNAKE_WECHAT_APPID");
            config.ProjectConf.CDN = Require("SNAKE_WECHAT_CDN");
            config.ProjectConf.relativeDST = output;
            config.ProjectConf.DST = output;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log("SNAKE_WECHAT_BUILD_START");
            var result = WXConvertCore.DoExport(true);
            if (result != WXConvertCore.WXExportError.SUCCEED) throw new InvalidOperationException("WXSDK export failed: " + result);
            File.WriteAllText(Require("SNAKE_WECHAT_STAMP"), "completed\n");
            Debug.Log("SNAKE_WECHAT_BUILD_COMPLETE");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogError("SNAKE_WECHAT_BUILD_FAILED " + error);
            EditorApplication.Exit(1);
        }
    }

    private static string Require(string name)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Missing " + name);
        return value;
    }
}
