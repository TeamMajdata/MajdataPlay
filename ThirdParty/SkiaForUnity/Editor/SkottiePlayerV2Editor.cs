#if UNITY_EDITOR
using System.Reflection;
using SkiaSharp.Unity;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace SkiaSharp.UnityEditor {
  [CustomEditor(typeof(SkottiePlayerV2))]
  public class SlottiePlayerV2Editor : Editor {
  private MethodInfo UpdateAnimation;
  private MethodInfo PlayAnimation;
  private static bool isEditorUpdateActive = false;
  private FieldInfo timerField;
  private FieldInfo playAniamtionField;
  private GUIStyle labelStyle;

  private SerializedProperty lottieFile,
    customResolution,
    resWidth,
    resHeight,
    stateName,
    resetAfterFinished,
    autoPlay,
    loop;
  private void OnEnable() {
    UpdateAnimation = target.GetType().GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    PlayAnimation = target.GetType().GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    timerField = target.GetType().GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic);
    playAniamtionField = target.GetType().GetField("playAniamtion", BindingFlags.Instance | BindingFlags.NonPublic);
    // Create a GUIStyle with desired color and size
    labelStyle = new GUIStyle(EditorStyles.label) {
      normal = {
        textColor = Color.red // Set the color
      },
      fontSize = 12 // Set the font size
    };
    
    lottieFile = serializedObject.FindProperty("lottieFile");
    customResolution = serializedObject.FindProperty("customResolution");
    resWidth = serializedObject.FindProperty("resWidth");
    resHeight = serializedObject.FindProperty("resHeight");
    stateName = serializedObject.FindProperty("stateName");
    resetAfterFinished = serializedObject.FindProperty("resetAfterFinished");
    autoPlay = serializedObject.FindProperty("autoPlay");
    loop = serializedObject.FindProperty("loop");
  }
  
  private void UpdateEditor() {
    SkottiePlayerV2 myScript = (SkottiePlayerV2)target;
    if (!(bool)playAniamtionField.GetValue(myScript)) {
      isEditorUpdateActive = false;
      EditorApplication.update -= UpdateEditor;
      CallUpdateAnimation();
    }

    if (!isEditorUpdateActive) {
      return;
    }
      
    CallPlayAnimation();
  }

  public override void OnInspectorGUI() {
    SkottiePlayerV2 myScript = (SkottiePlayerV2)target;

    EditorGUILayout.PropertyField(lottieFile);
    EditorGUILayout.PropertyField(customResolution);
    if (myScript.customResolution) {
      EditorGUILayout.PropertyField(resWidth);
      EditorGUILayout.PropertyField(resHeight);
    }
    EditorGUILayout.PropertyField(stateName);
    EditorGUILayout.PropertyField(resetAfterFinished);
    EditorGUILayout.PropertyField(autoPlay);
    EditorGUILayout.PropertyField(loop);

    
    //base.OnInspectorGUI();
    if (EditorApplication.isPlaying) {
      isEditorUpdateActive = false;
      EditorApplication.update -= UpdateEditor;

      return;
    }
    
    GUILayout.Space(20);
    EditorGUILayout.LabelField("SkottiePlayer Editor",labelStyle);

    if (isEditorUpdateActive) {
      if (GUILayout.Button("Stop Animation!")) {
        isEditorUpdateActive = false;
        EditorApplication.update -= UpdateEditor;
        CallUpdateAnimation();
      }
      EditorGUILayout.LabelField($"current timer : {(double)timerField.GetValue(myScript)}");

    } else {
      CallUpdateAnimation();
      if (GUILayout.Button("Play Animation!")) {
        timerField.SetValue(myScript,0);
        playAniamtionField.SetValue(myScript,true);
        isEditorUpdateActive = true;
        EditorApplication.update += UpdateEditor;
      }
    }
    
    serializedObject.ApplyModifiedProperties();

  }

  private void CallUpdateAnimation() {
    // Get the target script
    SkottiePlayerV2 myScript = (SkottiePlayerV2)target;
    if (target == null) {
      return;
    }

    // Use reflection to call the private method
    UpdateAnimation?.Invoke(myScript, null);
  }
  
  private void CallPlayAnimation() {
    // Get the target script
    SkottiePlayerV2 myScript = (SkottiePlayerV2)target;

    // Use reflection to call the private method
    PlayAnimation?.Invoke(myScript, null);
  }
  
  private void OnDisable() {
    isEditorUpdateActive = false;
    EditorApplication.update -= UpdateEditor;
    CallUpdateAnimation();
  }
}
}

[InitializeOnLoad]
public class EditorOpenCallback2 {
  private static MethodInfo UpdateAnimation;
  static EditorOpenCallback2() {
    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    EditorSceneManager.sceneOpened += OnSceneOpened;

    if (!EditorApplication.isPlayingOrWillChangePlaymode) {
      RenderTextures();
    }
     
  }

  private static void OnSceneOpened(Scene scene, OpenSceneMode mode) {
    RenderTextures();
  }

  private static void OnPlayModeStateChanged(PlayModeStateChange state) {
     if (state == PlayModeStateChange.EnteredEditMode) {
       RenderTextures();
     }
  }

   private static void RenderTextures() {
     Object[] objects = Resources.FindObjectsOfTypeAll(typeof(GameObject));
     foreach (Object obj in objects) {
       GameObject gameObj = obj as GameObject;
       if (gameObj != null){
         SkottiePlayerV2 myComponent = gameObj.GetComponent<SkottiePlayerV2>();
         if (myComponent != null)
         {
           UpdateAnimation = myComponent.GetType().GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
           UpdateAnimation?.Invoke(myComponent, null);

         }
       }
     }
   }

}
#endif