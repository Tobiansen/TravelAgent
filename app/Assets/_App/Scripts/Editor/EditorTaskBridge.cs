using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace TravelAgent.Editor
{
    /// <summary>Local development command inbox. Only explicit Temp requests run actions; no auto generation.</summary>
    [InitializeOnLoad]
    public static class EditorTaskBridge
    {
        [Serializable] private sealed class Command { public string id; public string action; public string argument; }
        [Serializable] private sealed class Result { public string id; public string action; public bool ok; public string message; public string utc; }
        private static double nextPoll;
        private static TestRunnerApi testRunner;
        private static TestCallbacks testCallbacks;
        private const string Inbox = "Temp/travel-agent-command.json";
        private const string Outbox = "Artifacts/editor-response.json";
        static EditorTaskBridge() { EditorApplication.update += Poll; }
        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(Inbox)) return;
            Command command = null;
            try
            {
                command = JsonUtility.FromJson<Command>(File.ReadAllText(Inbox));
                File.Delete(Inbox);
                switch (command.action)
                {
                    case "generate": AppSceneBuilder.Generate(); break;
                    case "upgrade": AppSceneBuilder.UpgradeCurrentScene(); break;
                    case "polish": DemoUiPolish.Apply(); break;
                    case "refresh": AssetDatabase.Refresh(); break;
                    case "save": EditorSceneManager.SaveOpenScenes(); break;
                    case "clear": typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries")?.GetMethod("Clear", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public)?.Invoke(null,null); break;
                    case "open": EditorSceneManager.OpenScene(AppSceneBuilder.ScenePath); break;
                    case "verify": Verify(); break;
                    case "play": EditorApplication.isPlaying = true; break;
                    case "stop": EditorApplication.isPlaying = false; break;
                    case "preview": UnityEngine.Object.FindAnyObjectByType<ScreenSelector>().Show(int.Parse(command.argument)); break;
                    case "capture": ScreenCapture.CaptureScreenshot(Path.GetFullPath("Artifacts/" + (command.argument ?? "app.png"))); break;
                    case "tests": RunTests(command.argument); break;
                    case "resolution": EditorVerificationScenarios.SetResolution(command.argument); break;
                    case "live": EditorVerificationScenarios.StartLiveProbe(command.argument); break;
                    case "ui-demo": EditorVerificationScenarios.StartUiPreview(command.argument); break;
                    case "build-android": AndroidBuild.BuildDevelopment(); break;
                    default: throw new InvalidOperationException("Unknown development command.");
                }
                Respond(command, true, "Command executed.");
            }
            catch (Exception error) { Debug.LogException(error); Respond(command, false, error.ToString()); }
        }
        private static void Respond(Command command, bool ok, string message)
        {
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText(Outbox, JsonUtility.ToJson(new Result { id=command?.id,action=command?.action,ok=ok,message=message,utc=DateTime.UtcNow.ToString("O") },true));
        }
        public static void Verify()
        {
            var view = UnityEngine.Object.FindAnyObjectByType<AppView>();
            if (!view) throw new InvalidOperationException("Open App.unity before verifying.");
            var missing = new List<string>();
            foreach (var field in typeof(AppView).GetFields())
            {
                if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType) && field.GetValue(view) == null) missing.Add(field.Name);
            }
            var scene = view.gameObject.scene;
            foreach (var root in scene.GetRootGameObjects()) foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject)>0) missing.Add("Missing script: "+transform.name);
            if (missing.Count>0) throw new InvalidOperationException(string.Join("\n",missing));
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText("Artifacts/scene-verification.txt","Saved App scene verified: all AppView references, four screen roots, no missing scripts.\n"+DateTime.UtcNow.ToString("O"));
            File.AppendAllText("Artifacts/scene-verification.txt","\nScene asset GUID: "+AssetDatabase.AssetPathToGUID(AppSceneBuilder.ScenePath)+"\nBuild scene GUID: "+typeof(EditorBuildSettingsScene).GetProperty("guid")?.GetValue(EditorBuildSettings.scenes[0]));
        }
        private static void RunTests(string mode)
        {
            if(testRunner && testCallbacks!=null)testRunner.UnregisterCallbacks(testCallbacks);
            if(testRunner)UnityEngine.Object.DestroyImmediate(testRunner);
            testRunner = ScriptableObject.CreateInstance<TestRunnerApi>();
            bool ticketVisual=mode=="ticket-visual", liveVisual=mode=="live-visual";
            bool visual=ticketVisual || liveVisual;bool play=mode=="play" || visual;
            testCallbacks=new TestCallbacks(ticketVisual ? "ticket-visual" : liveVisual ? "live-visual" : null);
            testRunner.RegisterCallbacks(testCallbacks);
            var filter=new Filter { testMode=play ? TestMode.PlayMode : TestMode.EditMode,assemblyNames=play ? new[]{"TravelAgent.PlayMode.Tests"} : new[]{"TravelAgent.Core.Tests","TravelAgent.Integrations.Tests"} };
            if(ticketVisual)filter.testNames=new[]{"TravelAgent.Tests.PlayMode.TicketDemoVisualTests.RenderTicketFirstScreensAtThreeSizes"};
            if(liveVisual)filter.testNames=new[]{"TravelAgent.Tests.PlayMode.LiveMotionVisualTests.RenderLiveMotionAtThreeSizes"};
            testRunner.Execute(new ExecutionSettings(filter));
        }
        private sealed class TestCallbacks : ICallbacks
        {
            private readonly string reportName;
            public TestCallbacks(string reportName=null){this.reportName=reportName;}
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                Directory.CreateDirectory("Artifacts");
                string name=reportName ?? (result.Test.TestMode==TestMode.PlayMode?"playmode":"editmode");
                File.WriteAllText("Artifacts/"+name+"-results.txt","Passed: "+result.PassCount+"\nFailed: "+result.FailCount+"\nSkipped: "+result.SkipCount+"\n"+result.Message+"\n"+Failures(result));
            }
            private static string Failures(ITestResultAdaptor result) => (result.FailCount>0?result.Name+": "+result.Message+"\n"+result.StackTrace+"\n":"") + string.Join("\n",result.Children.Select(Failures));
        }
    }
}
