using UnityEngine;
using System;
using System.IO;

namespace SceneCapture
{
    public class CaptureSession : IDisposable
    {

        public string SavePath { get; set; }  // Görsellerin kaydedileceği klasör
        public Vector2Int Resolution { get; set; } = new Vector2Int(1920, 1080); // Çözünürlük
        public string BaseName { get; set; } = "Capture"; // Dosya adı prefix
        public bool UseTimestamp { get; set; } = true; // İsimde timestamp kullanılsın mı
        public CaptureChannels Channels { get; set; } = CaptureChannels.All; // Color, Depth, Normal hangileri alınacak?

        // Field of view dışarıdan ayarlanabilir
        public float FieldOfView
        {
            get => _camera?.fieldOfView ?? _fov; 
            // Kamera varsa onun FOV değerini döndür,
            // yoksa henüz kamera oluşmamış demektir → _fov'u döndür

            set 
            { 
                _fov = value; // Kamera yokken bile fov değerini sakla
                if (_camera != null) 
                    _camera.fieldOfView = value; // Kamera oluşmuşsa direkt uygula
            }
        }

        private Camera _camera;           // Render işlemini yapan kamera
        private GameObject _cameraObj;    // Kameranın bağlı olduğu GameObject
        private bool _ownsCamera;         // Kamerayı biz mi oluşturduk? (Dispose için önemli)
        private Shader _depthShader;      // Depth çekimi için shader
        private Shader _normalShader;     // Normal çekimi için shader
        private int _index;               // Sıra numarası (timestamp kullanılmazsa)
        private float _fov = 60f;         // Default FOV (kamera henüz yokken kullanılır)

        // Bitwise enum (Color + Depth + Normal kombinasyonları için)
        [Flags]
        public enum CaptureChannels
        {
            None = 0,
            Color = 1,
            Depth = 2,
            Normals = 4,
            All = Color | Depth | Normals
        }

        public static CaptureSession Create(Camera camera, string path = null, Vector2Int? resolution = null)
        {
            // Create #1 
            // *Var olan mevcut bir kamerayı kullanmak isteyen kullanıcı için*
            // → Yeni kamera oluşturmaz, verilen kamerayı kullanır.

            if (camera == null) 
            { 
                Debug.LogError("[CaptureSession] Camera is null!"); 
                return null; 
            }

            var s = new CaptureSession();

            // Mevcut kamerayı kullan
            s._camera = camera;
            s._cameraObj = camera.gameObject;
            s._ownsCamera = false;    // Bu kamera bize ait değil → Dispose'da silme

            // Kameranın o anki FOV’unu session içine al
            s._fov = camera.fieldOfView;

            if (path != null) s.SavePath = path;
            if (resolution.HasValue) s.Resolution = resolution.Value;

            return s;
        }

        public static CaptureSession Create(Transform transform, string path = null, Vector2Int? resolution = null)
        {
            // Create #2
            // Kullanıcı kamerayı kendisi vermez ama onun pozisyonunu başka bir transformdan almak isterse
            // → Bu transformun pozisyon & rotasyonuna göre yeni bir kamera oluşturulur.

            if (transform == null) 
            { 
                Debug.LogError("[CaptureSession] Transform is null!"); 
                return null; 
            }

            // Transformun konumu ve rotasyonunu kullanarak kamera yaratacak versiyona yönlendir
            return Create(transform.position, transform.rotation, path, resolution);
        }

        public static CaptureSession Create(Vector3 position, Quaternion rotation, string path = null, Vector2Int? resolution = null)
        {
            // Create #3
            // Kullanıcı kamera da vermedi, transform da vermedi.
            // → Sıfırdan *tamamen yeni bir kamera* oluşturur ve onu sadece capture için kullanır.

            var s = new CaptureSession();

            // Pozisyon ve rotasyona göre kamera oluştur
            s.CreateCamera(position, rotation);

            if (path != null) s.SavePath = path;
            if (resolution.HasValue) s.Resolution = resolution.Value;

            return s;
        }


        private CaptureSession()
        {
            // Default kayıt klasörü
            SavePath = Path.Combine(Application.dataPath, "Captures");

            // Gerekli shaderları bul
            _depthShader = Shader.Find("Custom/DepthGrayscale");
            _normalShader = Shader.Find("Custom/NormalsRGB");

            // Shader bulunamazsa hata ver
            if (_depthShader == null) Debug.LogError("[CaptureSession] Missing shader: Custom/DepthGrayscale");
            if (_normalShader == null) Debug.LogError("[CaptureSession] Missing shader: Custom/NormalsRGB");
        }

        private void CreateCamera(Vector3 pos, Quaternion rot)
        {
            // Yeni GameObject oluştur
            _cameraObj = new GameObject("CaptureSession_Camera");

            // Pozisyon ve rotasyonu ayarla
            _cameraObj.transform.SetPositionAndRotation(pos, rot);

            // Camera component ekle
            _camera = _cameraObj.AddComponent<Camera>();

            // Bu kamera sahneyi göstermesin, sadece render için kullanıyoruz
            _camera.enabled = false;

            // FOV ayarla
            _camera.fieldOfView = _fov;

            // Bu kamera bize ait, Dispose’da silinecek
            _ownsCamera = true;
        }

        // Pozisyon manuel verildiğinde
        public void SetPosition(Vector3 position, Quaternion rotation)
        {
            if (_cameraObj != null)
                _cameraObj.transform.SetPositionAndRotation(position, rotation);
        }

        // Başka bir transformun pozisyonunu kopyalamak için
        public void SetPosition(Transform source)
        {
            if (source != null && _cameraObj != null)
                _cameraObj.transform.SetPositionAndRotation(source.position, source.rotation);
        }

        public void Capture(string name = null)
        {
            if (_camera == null) 
            { 
                Debug.LogError("[CaptureSession] No camera!"); 
                return; 
            }

            // Dosya adını oluştur (timestamp veya index)
            string captureName = name ?? GenerateName();

            // Klasörü oluştur
            Directory.CreateDirectory(SavePath);

            // Kamera ayarlarını geçici olarak sakla
            var origTarget = _camera.targetTexture;
            var origClear = _camera.clearFlags;
            var origBg = _camera.backgroundColor;

            // İstenen kanallara göre capture al
            if (Channels.HasFlag(CaptureChannels.Color))   CaptureChannel("Color", captureName, null);
            if (Channels.HasFlag(CaptureChannels.Depth))   CaptureChannel("Depth", captureName, _depthShader);
            if (Channels.HasFlag(CaptureChannels.Normals)) CaptureChannel("Normals", captureName, _normalShader);

            // Kamera ayarlarını geri yükle
            _camera.targetTexture = origTarget;
            _camera.clearFlags = origClear;
            _camera.backgroundColor = origBg;

            // Index arttır
            _index++;

            Debug.Log($"[CaptureSession] Captured: {captureName}");
        }


        private void CaptureChannel(string channel, string captureName, Shader shader)
        {
            // RenderTexture oluştur
            var rt = RenderTexture.GetTemporary(Resolution.x, Resolution.y, 24, RenderTextureFormat.ARGB32);
            _camera.targetTexture = rt;

            if (shader != null)
            {
                // Eğer depth çekiyorsak MaxDepth değerini güncelle
                if (channel == "Depth")
                    Shader.SetGlobalFloat("_MaxDepth", CalculateMaxDepth());

                // Depth / Normal için arka plan siyah olsun
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = Color.black;

                // Shader ile render et
                _camera.RenderWithShader(shader, "RenderType");
            }
            else
            {
                // Normal renk render’ı
                _camera.Render();
            }

            // PNG olarak diske kaydet
            SaveTexture(rt, Path.Combine(SavePath, $"{captureName}_{channel}.png"));

            // RenderTexture’ı serbest bırak
            RenderTexture.ReleaseTemporary(rt);
        }

        private float CalculateMaxDepth()
        {
            // Depth normalization için sahnedeki en uzak objeyi bul
            float max = 5f;

            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled) continue;

                // Kamera → obje mesafesi + objenin yarıçapı
                float d = Vector3.Distance(_camera.transform.position, r.bounds.center) 
                        + r.bounds.extents.magnitude;

                if (d > max) max = d;
            }

            // Biraz güvenlik payı ekle
            return max * 1.1f;
        }


        private void SaveTexture(RenderTexture rt, string path)
        {
            // RT’yi aktif yap
            RenderTexture.active = rt;

            // Texture2D’ye oku
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();

            RenderTexture.active = null;

            // PNG olarak yaz
            File.WriteAllBytes(path, tex.EncodeToPNG());

            // Belleği temizle
            UnityEngine.Object.Destroy(tex);
        }


        private string GenerateName() => UseTimestamp
            ? $"{BaseName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}"
            : $"{BaseName}_{_index:D4}";

        public void ResetIndex() => _index = 0;

        public void Dispose()
        {
            // Kamerayı biz oluşturduysak → sahneden sil
            if (_ownsCamera && _cameraObj != null)
                UnityEngine.Object.Destroy(_cameraObj);

            // Referansları temizle
            _camera = null;
            _cameraObj = null;
        }
    }
}
