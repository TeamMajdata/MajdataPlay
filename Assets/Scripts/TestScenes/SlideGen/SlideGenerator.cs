using MajdataPlay.Scenes.Game.Notes.Behaviours;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using Unity.VisualScripting;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.WSA;
#endif
using UnityEngine;
using UnityEngine.UIElements;


namespace MajdataPlay.TestScenes.SlideGen
{
    public class SlideGenerator : MonoBehaviour
    {
        LineRenderer lineRenderer;
        public string type;
        public Sprite slide;
        public float step;
        public float rad;
        public bool showLine = false;
        public bool generate = false;
        public float xoffset;
        // Start is called before the first frame update
        void Start()
        {
            //if (showLine)
            //{
            //    lineRenderer = GetComponent<LineRenderer>();
            //    var positions = new List<Vector3>();
            //    for (int i = 0; i <= 100; i++)
            //    {
            //        positions.Add((Vector2)GetPointAtPosition(type, i / 100f));
            //    }
            //    lineRenderer.positionCount = positions.Count;
            //    lineRenderer.SetPositions(positions.ToArray());
            //}
            //if (generate)
            //{
            //    GenerateSlides(type, step);
            //}
#if UNITY_EDITOR
            //var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefab/Game/Slides" });
            //var prefabs = new GameObject[guids.Length];

            //for (int i = 0; i < guids.Length; i++)
            //{
            //    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
            //    prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            //}
            //var poss = new Vector3[8][];
            //var sb = new StringBuilder();
            //foreach (var prefab in prefabs.Where(x => x.name.StartsWith("Star")))
            //{
            //    var isMirror = false;
            //MIRROR_START:
            //    for (var j = 1; j < 9; j++)
            //    {
            //        var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            //        var slideDrop = instance.GetComponent<SlideDrop>();
            //        slideDrop.StartPos = j;
            //        slideDrop.IsMirror = isMirror;
            //        slideDrop.Initialize();
            //        var posArray = slideDrop._starPositions.ToArray();
            //        poss[j - 1] = posArray;
            //        Object.DestroyImmediate(instance);
            //    }
            //    var arrayName = prefab.name;
            //    if(isMirror)
            //    {
            //        arrayName += "_Mirror";
            //    }
            //    var code = ArrayCodeGen(arrayName, poss);
            //    sb.AppendLine(code);
            //    if (!isMirror)
            //    {
            //        isMirror = true;
            //        goto MIRROR_START;
            //    }
            //}
            //var arrayCode = sb.ToString();
#endif
        }

        void GenerateSlides(string type, float step)
        {

            for (float i = step; i < 1f; i += step)
            {
                var result = GetPointAtPosition(type, i);
                var obj = new GameObject("Slide_" + (1f - i));
                obj.transform.parent = transform;
                obj.transform.position = (Vector2)result;
                obj.transform.rotation = Quaternion.Euler(0, 0, result.z);
                var rend = obj.AddComponent<SpriteRenderer>();
                rend.sortingLayerName = "Slides";
                rend.sprite = slide;
            }
        }

        Vector3 GetPointAtPosition(string type, float position)
        {
            if (type == "-")
            {
                var startPoint = GetPositionFromDistance(4.8f, 7);
                var endPoint = GetPositionFromDistance(4.8f, 5);
                var lerp = Vector2.Lerp(startPoint, endPoint, position);
                var vect = endPoint - startPoint;
                var angle = Mathf.Rad2Deg * Mathf.Atan2(vect.x, vect.y);
                return new Vector3(lerp.x, lerp.y, -angle - 90f);
            }
            else if (type == "q")
            {
                var start = 1;
                var end = 8;
                var startPoint = GetPositionFromDistance(4.8f, start);
                var endPoint = GetPositionFromDistance(rad, 7.5f);
                var vect = endPoint - startPoint;
                var curv_part = 0.75f;
                var line_s = vect.magnitude;
                var curv_s = Mathf.PI * rad * 2f * curv_part;
                var lineseg = line_s / (line_s + curv_s + line_s);
                var curvseg = (line_s + curv_s) / (line_s + curv_s + line_s);

                if (position < lineseg)
                {
                    startPoint = GetPositionFromDistance(4.8f, start);
                    endPoint = GetPositionFromDistance(rad, 7.5f);
                    var lerp = Vector2.Lerp(startPoint, endPoint, position / lineseg);
                    vect = endPoint - startPoint;
                    var angle = Mathf.Rad2Deg * Mathf.Atan2(vect.x, vect.y);
                    return new Vector3(lerp.x, lerp.y, angle + 180);
                }
                else if (position < curvseg)
                {
                    position = (position - curvseg) / (curvseg - lineseg) * 2f * Mathf.PI * -curv_part;
                    position += 45f * Mathf.Deg2Rad;
                    var circle = new Vector2(rad * Mathf.Sin(position), rad * Mathf.Cos(position));
                    var angle = Mathf.Rad2Deg * Mathf.Atan2(circle.x, circle.y);
                    return new Vector3(circle.x, circle.y, -angle);
                }
                else if (position <= 1f)
                {
                    startPoint = GetPositionFromDistance(rad, end + 1.5f);
                    endPoint = GetPositionFromDistance(4.8f, end);
                    var lerp = Vector2.Lerp(startPoint, endPoint, (position - curvseg) / lineseg);
                    vect = endPoint - startPoint;
                    var angle = Mathf.Rad2Deg * Mathf.Atan2(vect.x, vect.y);
                    return new Vector3(lerp.x, lerp.y, -angle - 90f);
                }
                /*position = position * 6.28f;
                var circle = new Vector2(rad*Mathf.Sin(position), rad*Mathf.Cos(position));
                return circle;*/
            }
            else if (type == ">")
            {
                var pos = (0.0625f + position * 0.125f) * 2 * Mathf.PI;
                var circle = new Vector2(4.8f * Mathf.Sin(pos), 4.8f * Mathf.Cos(pos));
                var angle = Mathf.Rad2Deg * Mathf.Atan2(circle.x, circle.y);
                return new Vector3(circle.x, circle.y, -angle + 180f);
            }
            else if (type == "qq")
            {
                position = position * 2 * Mathf.PI;
                var circle = new Vector2(rad * Mathf.Sin(position) + xoffset, rad * Mathf.Cos(position));
                var angle = Mathf.Rad2Deg * Mathf.Atan2(Mathf.Sin(position), Mathf.Cos(position));
                return new Vector3(circle.x, circle.y, -angle);
            }
            return new Vector3();
        }

        Vector3 GetPositionFromDistance(float distance, float position)
        {
            return new Vector3(
                distance * Mathf.Cos((position * -2f + 5f) * 0.125f * Mathf.PI),
                distance * Mathf.Sin((position * -2f + 5f) * 0.125f * Mathf.PI));
        }

        string ArrayCodeGen(string arrayName, Vector3[][] array)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"public static readonly Vector3[][] {arrayName} =");
            sb.AppendLine("{");

            var rows = array;
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];

                sb.Append("    new Vector3[] {");

                for (int j = 0; j < row.Length; j++)
                {
                    if (j > 0)
                    {
                        sb.Append(",");
                    }
                    sb.Append("new Vector3 (");
                    sb.Append(row[j].x).Append('f').Append(',');
                    sb.Append(row[j].y).Append('f').Append(',');
                    sb.Append(row[j].z).Append('f').Append(')');
                }

                sb.Append("}");

                if (i < rows.Length - 1)
                {
                    sb.Append(",");
                }

                sb.AppendLine();
            }

            sb.AppendLine("};");

            return sb.ToString();
        }
    }
}
