using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace TravelAgent.Editor
{
    /// <summary>Explicit command-line authoring/verification for board-free development.</summary>
    public static class FinnairDemoBuild
    {
        public static void Prepare()
        {
            EditorSceneManager.OpenScene(AppSceneBuilder.ScenePath);
            DemoUiPolish.Apply();
            EditorTaskBridge.Verify();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText("Artifacts/finnair-scene-prepared.txt",DateTime.UtcNow.ToString("O")+"\nSaved authoring and reference verification completed. No device accessed.");
        }
    }
}
