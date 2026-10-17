using MajdataPlay.Collections;
using MajdataPlay.IO;
using MajdataPlay.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

namespace MajdataPlay
{
    [DontDestroyOnLoad]
    internal sealed class DummyTouchPanelRenderer : MajComponent
    {
        public IReadOnlyDictionary<int, int> InstanceID2SensorIndexMappingTable
        {
            get
            {
                return _instanceID2SensorIndexMappingTable;
            }
        }

        [SerializeField]
        [FormerlySerializedAs("sharedInstancedMaterial")]
        Material _sharedInstancedMaterial;

        readonly Dictionary<int, int> _instanceID2SensorIndexMappingTable = new();
        readonly SensorRenderer[] _sensorRenderers = new SensorRenderer[34];        
        static readonly Color IdleSensorColor = new Color(1, 0, 0, 0.2f);
        static readonly Color ActiveSensorColor = new Color(0, 1, 0, 0.2f);
        bool _sensorsVisible;

        protected override void Awake()
        {
            base.Awake();
            Majdata<DummyTouchPanelRenderer>.SetAsSingleton(this);
            foreach (var (index, child) in Transform.ToEnumerable().WithIndex())
            {
                var collider = child.GetComponent<MeshCollider>();
                var renderer = child.GetComponent<MeshRenderer>();
                var filter = child.GetComponent<MeshFilter>();

                renderer.sortingLayerName = "Debug";
                renderer.sortingOrder = short.MaxValue;

                // MeshFilter 已绑定烘焙窄边，Collider 仍为完整分区；运行时不再生成网格。
                _sensorRenderers[index] = new SensorRenderer(index, filter, renderer, collider, child.gameObject, _sharedInstancedMaterial);
                _instanceID2SensorIndexMappingTable[collider.GetInstanceID()] = index;
            }
        }

        internal void OnPreUpdate()
        {
            bool visible = IsSensorRendererEnabled();
            if (visible != _sensorsVisible)
            {
                // 只停止绘制；Collider 和 GameObject 仍用于输入位置表及触摸命中。
                foreach (var sensor in _sensorRenderers)
                    sensor.SetVisible(visible);
                _sensorsVisible = visible;
            }
            // 稳定关闭时不遍历分区、不写透明颜色，也不提交这些 Mesh 的绘制。
            if (!visible) return;

            // 设置只改变当前边框颜色的 alpha；缓存仍避免稳定帧重复写 MPB。
            var idleColor = IdleSensorColor;
            var activeColor = ActiveSensorColor;
            float opacity = Math.Max(10, Math.Min(50, MajEnv.Settings.Debug.SensorOpacity)) / 100f;
            idleColor.a = activeColor.a = opacity;
            var tpRawData = InputManager.TouchPanelRawData;
            for (var i = 0; i < tpRawData.Length; i++)
            {
                if (i == 34)
                {
                    continue;
                }
                var state = tpRawData[i];
                // 所有平台未触发为红色，触发为绿色；稳定状态不重复写 MPB。
                _sensorRenderers[i].SetColor(state ? activeColor : idleColor);
            }
        }

        bool IsSensorRendererEnabled() => MajEnv.Settings.Debug.DisplaySensor;

        class SensorRenderer
        {
            public int Index { get; init; }
            public MeshFilter MeshFilter { get; init; }
            public MeshRenderer MeshRenderer { get; init; }
            public MeshCollider MeshCollider { get; init; }
            public GameObject GameObject { get; init; }

            MaterialPropertyBlock _propBlock;
            Color _lastColor;
            bool _hasColor;

            static readonly int ColorPropId = Shader.PropertyToID("_BaseColor");

            public SensorRenderer(int index, MeshFilter meshFilter, MeshRenderer meshRenderer, MeshCollider meshCollider, GameObject gameObject, Material sharedMaterial)
            {
                Index = index;
                MeshFilter = meshFilter;
                MeshRenderer = meshRenderer;
                MeshCollider = meshCollider;
                GameObject = gameObject;

                MeshRenderer.sharedMaterial = sharedMaterial;
                // 启动时即停止绘制，避免在首次读取开关前提交透明调试网格。
                MeshRenderer.enabled = false;
                _propBlock = new MaterialPropertyBlock();

                SetColor(IdleSensorColor);
            }
            public void SetVisible(bool visible) => MeshRenderer.enabled = visible;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetColor(Color color)
            {
                if (_hasColor && _lastColor == color) return;
                MeshRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetColor(ColorPropId, color);
                MeshRenderer.SetPropertyBlock(_propBlock);
                _lastColor = color;
                _hasColor = true;
            }

            public void Destroy()
            {
                GameObject.Destroy(GameObject);
            }
        }
    }
}
