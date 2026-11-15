using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TravelAgent.Editor
{
    public static class AndroidBuild
    {
        [MenuItem("Tools/Travel Agent/Build Android development APK")]
        public static void BuildDevelopment()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before building Android.");
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android,BuildTarget.Android))throw new InvalidOperationException("Install Android Build Support and restart Unity first.");
            const string pluginPath="Assets/Plugins/Android/TravelAgentBle.androidlib";
            AssetDatabase.ImportAsset(pluginPath,ImportAssetOptions.ForceUpdate);
            if(AssetImporter.GetAtPath(pluginPath) is PluginImporter plugin)
            {
                plugin.SetCompatibleWithAnyPlatform(false);plugin.SetCompatibleWithEditor(false);plugin.SetCompatibleWithPlatform(BuildTarget.Android,true);plugin.SaveAndReimport();
            }
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.travelagent.companion");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion=(AndroidSdkVersions)36;
            PlayerSettings.defaultInterfaceOrientation=UIOrientation.Portrait;
            // The private laptop gateway uses HTTP during development; release players require HTTPS.
            PlayerSettings.insecureHttpOption=InsecureHttpOption.DevelopmentOnly;
            EditorUserBuildSettings.buildAppBundle=false;
            Directory.CreateDirectory("Builds/Android");Directory.CreateDirectory("Artifacts");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{AppSceneBuilder.ScenePath},locationPathName="Builds/Android/TravelAgent-development.apk",target=BuildTarget.Android,options=BuildOptions.Development});
            long apkBytes=File.Exists(report.summary.outputPath)?new FileInfo(report.summary.outputPath).Length:0;
            File.WriteAllText("Artifacts/android-build.txt",report.summary.result+"\nAPK bytes: "+apkBytes+"\nAll build output bytes (includes symbols): "+report.summary.totalSize+"\nErrors: "+report.summary.totalErrors+"\nWarnings: "+report.summary.totalWarnings+"\nHTTP policy: "+PlayerSettings.insecureHttpOption+"\nOutput: "+report.summary.outputPath);
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Android build failed. Inspect Artifacts/android-build.log and android-build.txt.");
            Debug.Log("Travel Agent development APK built: "+report.summary.outputPath);
        }
    }
}
