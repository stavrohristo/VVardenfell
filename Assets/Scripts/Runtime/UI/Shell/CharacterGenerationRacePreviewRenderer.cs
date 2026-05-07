using UnityEngine;
using VVardenfell.Runtime.Player;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class CharacterGenerationRacePreviewRenderer
    {
        static readonly int LocalMapRenderId = Shader.PropertyToID("_VV_LocalMapRender");
        const int TextureWidth = 237;
        const int TextureHeight = 216;
        const float OrthographicSize = 1.05f;

        GameObject _cameraRoot;
        Camera _camera;
        RenderTexture _texture;

        public Texture Texture => EnsureTexture();

        public void Render(float angleDegrees)
        {
            Camera camera = EnsureCamera();
            RenderTexture texture = EnsureTexture();
            Vector3 lookAt = ToVector3(CharacterGenerationRacePreviewRuntimeUtility.LookAt);
            Quaternion rotation = Quaternion.AngleAxis(angleDegrees, Vector3.up);
            Vector3 offset = rotation * ToVector3(CharacterGenerationRacePreviewRuntimeUtility.CameraOffset);
            camera.targetTexture = texture;
            camera.transform.position = lookAt + offset;
            camera.transform.LookAt(lookAt, Vector3.up);
            camera.orthographicSize = OrthographicSize;
            camera.aspect = TextureWidth / (float)TextureHeight;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 8f;
            camera.ResetProjectionMatrix();
            camera.ResetWorldToCameraMatrix();
            float previousLocalMapRender = Shader.GetGlobalFloat(LocalMapRenderId);
            Shader.SetGlobalFloat(LocalMapRenderId, 1f);
            try
            {
                camera.Render();
            }
            finally
            {
                Shader.SetGlobalFloat(LocalMapRenderId, previousLocalMapRender);
                camera.targetTexture = null;
            }
        }

        public void Dispose()
        {
            if (_cameraRoot != null)
                Object.Destroy(_cameraRoot);
            _cameraRoot = null;
            _camera = null;

            if (_texture != null)
                Object.Destroy(_texture);
            _texture = null;
        }

        RenderTexture EnsureTexture()
        {
            if (_texture != null)
                return _texture;

            _texture = new RenderTexture(TextureWidth, TextureHeight, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                name = "VVardenfell.CharacterGenerationRacePreview",
                hideFlags = HideFlags.HideAndDontSave,
                useMipMap = false,
                autoGenerateMips = false,
            };
            _texture.Create();
            return _texture;
        }

        Camera EnsureCamera()
        {
            if (_camera != null)
                return _camera;

            _cameraRoot = new GameObject("VVardenfell.CharacterGenerationRacePreviewCamera")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            Object.DontDestroyOnLoad(_cameraRoot);
            _camera = _cameraRoot.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.orthographic = true;
            _camera.renderingPath = RenderingPath.Forward;
            _camera.depthTextureMode = DepthTextureMode.None;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.useOcclusionCulling = false;
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                _camera.cullingMask &= ~(1 << uiLayer);
            return _camera;
        }

        static Vector3 ToVector3(Unity.Mathematics.float3 value)
            => new(value.x, value.y, value.z);
    }
}
