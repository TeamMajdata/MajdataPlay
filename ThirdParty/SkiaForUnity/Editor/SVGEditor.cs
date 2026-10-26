#if UNITY_EDITOR

using SkiaSharp.Unity;
using SkiaSharp.Unity.HB;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TextAlignment = Topten.RichTextKit.TextAlignment;


[CustomEditor(typeof(SVGLoader))]
public class SVGEditor : Editor {
  private GUIStyle selectedStyle;
  private SerializedProperty svgFile;
  bool showHaloSettings = false;
  bool showMoreSettings = false;

  private void OnEnable(){
    selectedStyle = new GUIStyle();
    selectedStyle.normal.background = EditorGUIUtility.Load("builtin skins/darkskin/images/pre background@2x.png") as Texture2D;
    selectedStyle.normal.textColor = Color.white;
    selectedStyle.alignment = TextAnchor.MiddleCenter;
    selectedStyle.fontSize = 14;
    
    // Find the serialized property for fontSize
    svgFile = serializedObject.FindProperty("svgFile");
  }
  public override void OnInspectorGUI(){
    SVGLoader script = (SVGLoader)target;
    GUIStyle largeLabelStyle = new GUIStyle(GUI.skin.label);
    largeLabelStyle.fontSize = 12; // Adjust the font size as needed
    largeLabelStyle.fontStyle = FontStyle.Bold;
    //script.text = EditorGUILayout.TextArea(script.text, GUILayout.Height(100));
    //textProperty.stringValue = script.text;
    EditorGUILayout.PropertyField(svgFile);

    
    serializedObject.ApplyModifiedProperties();
    if (!EditorApplication.isPlayingOrWillChangePlaymode) {
      script.RenderSVG();
    }
  }
}
#endif
