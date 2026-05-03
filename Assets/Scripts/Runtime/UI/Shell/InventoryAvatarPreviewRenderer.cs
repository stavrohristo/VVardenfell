using UnityEngine;
using VVardenfell.Runtime.Inventory;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class InventoryAvatarPreviewRenderer
    {
        const int TextureWidth = 256;
        const int TextureHeight = 320;
        const float OrthographicSize = 1.12f;

        GameObject _cameraRoot;
        Camera _camera;
        RenderTexture _texture;

        public Texture Texture => EnsureTexture();

        public void Render()
        {
            var camera = EnsureCamera();
            var texture = EnsureTexture();
            camera.targetTexture = texture;
            camera.transform.position = ToVector3(InventoryAvatarPreviewRuntimeUtility.CameraPosition);
            camera.transform.LookAt(ToVector3(InventoryAvatarPreviewRuntimeUtility.LookAt), Vector3.up);
            camera.orthographicSize = OrthographicSize;
            camera.aspect = TextureWidth / (float)TextureHeight;
            camera.ResetProjectionMatrix();
            camera.ResetWorldToCameraMatrix();
            try
            {
                camera.Render();
            }
            finally
            {
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
                name = "VVardenfell.InventoryAvatarPreview",
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

            _cameraRoot = new GameObject("VVardenfell.InventoryAvatarPreviewCamera")
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
