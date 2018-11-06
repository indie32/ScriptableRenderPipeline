using System;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    [ExecuteInEditMode]
    public abstract partial class HDProbe : MonoBehaviour
    {
        [Serializable]
        public struct RenderData
        {
            public Matrix4x4 worldToCameraRHS;
            public Matrix4x4 projectionMatrix;
            public Vector3 capturePosition;

            public RenderData(CameraSettings camera, CameraPositionSettings position)
            {
                worldToCameraRHS = position.GetUsedWorldToCameraMatrix();
                projectionMatrix = camera.frustum.GetUsedProjectionMatrix();
                capturePosition = position.position;
            }
        }

        // Serialized Data
        [SerializeField]
        // This one is protected only to have access during migration of children classes.
        // In children classes, it must be used only during the migration.
        protected ProbeSettings m_ProbeSettings = ProbeSettings.@default;
        [SerializeField]
        ProbeSettingsOverride m_ProbeSettingsOverride;
        [SerializeField]
        ReflectionProxyVolumeComponent m_ProxyVolume;

        [SerializeField]
        Texture m_BakedTexture;
        [SerializeField]
        Texture m_CustomTexture;
        [SerializeField]
        RenderData m_BakedRenderData;
        [SerializeField]
        RenderData m_CustomRenderData;

        // Runtime Data
        RenderTexture m_RealtimeTexture;
        RenderData m_RealtimeRenderData;

        // Public API
        // Texture asset
        public Texture bakedTexture => m_BakedTexture;
        public Texture customTexture => m_CustomTexture;
        public RenderTexture realtimeTexture => m_RealtimeTexture;
        public Texture texture => GetTexture(mode);
        public Texture GetTexture(ProbeSettings.Mode targetMode)
        {
            switch (targetMode)
            {
                case ProbeSettings.Mode.Baked: return m_BakedTexture;
                case ProbeSettings.Mode.Custom: return m_CustomTexture;
                case ProbeSettings.Mode.Realtime: return m_RealtimeTexture;
                default: throw new ArgumentOutOfRangeException();
            }
        }
        public Texture SetTexture(ProbeSettings.Mode targetMode, Texture texture)
        {
            if (targetMode == ProbeSettings.Mode.Realtime && !(texture is RenderTexture))
                throw new ArgumentException("'texture' must be a RenderTexture for the Realtime mode.");

            switch (targetMode)
            {
                case ProbeSettings.Mode.Baked: return m_BakedTexture = texture;
                case ProbeSettings.Mode.Custom: return m_CustomTexture = texture;
                case ProbeSettings.Mode.Realtime: return m_RealtimeTexture = (RenderTexture)texture;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        public RenderData bakedRenderData { get => m_BakedRenderData; internal set => m_BakedRenderData = value; }
        public RenderData customRenderData { get => m_CustomRenderData; internal set => m_CustomRenderData = value; }
        public RenderData realtimeRenderData { get => m_RealtimeRenderData; internal set => m_RealtimeRenderData = value; }
        public RenderData renderData => GetRenderData(mode);
        public RenderData GetRenderData(ProbeSettings.Mode targetMode)
        {
            switch (mode)
            {
                case ProbeSettings.Mode.Baked: return bakedRenderData;
                case ProbeSettings.Mode.Custom: return customRenderData;
                case ProbeSettings.Mode.Realtime: return realtimeRenderData;
                default: throw new ArgumentOutOfRangeException();
            }
        }
        public void SetRenderData(ProbeSettings.Mode targetMode, RenderData renderData)
        {
            switch (mode)
            {
                case ProbeSettings.Mode.Baked: bakedRenderData = renderData; break;
                case ProbeSettings.Mode.Custom: customRenderData = renderData; break;
                case ProbeSettings.Mode.Realtime: realtimeRenderData = renderData; break;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        // Settings
        // General
        public ProbeSettings.ProbeType type { get => m_ProbeSettings.type; protected set => m_ProbeSettings.type = value; }
        /// <summary>The capture mode.</summary>
        public ProbeSettings.Mode mode { get => m_ProbeSettings.mode; set => m_ProbeSettings.mode = value; }
        public ProbeSettings.RealtimeMode realtimeMode { get => m_ProbeSettings.realtimeMode; set => m_ProbeSettings.realtimeMode = value; }

        // Lighting
        /// <summary>Light layer to use by this probe.</summary>
        public LightLayerEnum lightLayers
        { get => m_ProbeSettings.lighting.lightLayer; set => m_ProbeSettings.lighting.lightLayer = value; }
        // This function return a mask of light layers as uint and handle the case of Everything as being 0xFF and not -1
        public uint lightLayersAsUInt => lightLayers < 0 ? (uint)LightLayerEnum.Everything : (uint)lightLayers;
        /// <summary>Multiplier factor of reflection (non PBR parameter).</summary>
        public float multiplier
        { get => m_ProbeSettings.lighting.multiplier; set => m_ProbeSettings.lighting.multiplier = value; }
        /// <summary>Weight for blending amongst probes (non PBR parameter).</summary>
        public float weight
        { get => m_ProbeSettings.lighting.weight; set => m_ProbeSettings.lighting.weight = value; }

        // Proxy
        /// <summary>ProxyVolume currently used by this probe.</summary>
        public ReflectionProxyVolumeComponent proxyVolume => m_ProxyVolume;
        public bool useInfluenceVolumeAsProxyVolume => m_ProbeSettings.proxySettings.useInfluenceVolumeAsProxyVolume;
        /// <summary>Is the projection at infinite? Value could be changed by Proxy mode.</summary>
        public bool isProjectionInfinite
            => m_ProxyVolume != null && m_ProxyVolume.proxyVolume.shape == ProxyShape.Infinite
            || m_ProxyVolume == null && !m_ProbeSettings.proxySettings.useInfluenceVolumeAsProxyVolume;

        // Influence
        /// <summary>InfluenceVolume of the probe.</summary>
        public InfluenceVolume influenceVolume
        {
            get => m_ProbeSettings.influence ?? (m_ProbeSettings.influence = new InfluenceVolume());
            private set => m_ProbeSettings.influence = value;
        }
        internal Matrix4x4 influenceToWorld => influenceVolume.GetInfluenceToWorld(transform);

        // Camera
        /// <summary>Frame settings in use with this probe.</summary>
        public FrameSettings frameSettings => m_ProbeSettings.camera.frameSettings;
        internal Vector3 influenceExtents => influenceVolume.extents;
        internal Matrix4x4 proxyToWorld
            => proxyVolume != null ? proxyVolume.transform.localToWorldMatrix : influenceToWorld;
        public Vector3 proxyExtents
            => proxyVolume != null ? proxyVolume.proxyVolume.extents : influenceExtents;

        public BoundingSphere boundingSphere => influenceVolume.GetBoundingSphereAt(transform.position);
        public Bounds bounds => influenceVolume.GetBoundsAt(transform.position);

        internal ProbeSettings settings
        {
            get
            {
                var settings = m_ProbeSettings;
                // Special case here, we reference a component that is a wrapper
                // So we need to update with the actual value for the proxyVolume
                settings.proxy = m_ProxyVolume?.proxyVolume;
                return settings;
            }
        }
        internal ProbeSettingsOverride settingsOverrides => m_ProbeSettingsOverride;

        internal bool wasRenderedAfterOnEnable { get; set; } = false;
        internal int lastRenderedFrame { get; set; } = int.MinValue;

        // API
        /// <summary>
        /// Prepare the probe for culling.
        /// You should call this method when you update the <see cref="influenceVolume"/> parameters during runtime.
        /// </summary>
        public virtual void PrepareCulling() { }

        // Life cycle methods
        protected virtual void Awake() => k_Migration.Migrate(this);

        void OnEnable()
        {
            Debug.Log("onenable");
            wasRenderedAfterOnEnable = false;
            PrepareCulling();
            HDProbeSystem.RegisterProbe(this);
        }
        void OnDisable() => HDProbeSystem.UnregisterProbe(this);

        void OnValidate()
        {
            HDProbeSystem.UnregisterProbe(this);

            if (isActiveAndEnabled)
                HDProbeSystem.RegisterProbe(this);
        }
    }
}
