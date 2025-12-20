using UnityEngine;
using SceneCapture;
using System.IO;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CaptureController : MonoBehaviour
{
    [Header("Camera")]
    public Camera sourceCamera; // Kullanıcı isterse kendi kamerasını verir

    [Header("Output")]
    public string savePath = ""; // Görseller nereye kaydedilecek
    public string baseName = "Capture"; // Dosya ismi prefix
    public bool useTimestamp = true; // Dosya ismine tarih ekle
    public int width = 1920; // Çözünürlük genişlik
    public int height = 1080; // Çözünürlük yükseklik

    [Header("Channels")]
    public bool captureColor = true; // Renk kanalı alınsın mı
    public bool captureDepth = true; // Depth kanalı alınsın mı
    public bool captureNormals = true; // Normal kanalı alınsın mı

    [Header("Controls")]
    public KeyCode captureKey = KeyCode.Space; // Tek tuşla capture alma
    public bool captureOnStart = false; // Oyun başlayınca otomatik capture

    [Header("Multi-Capture")]
    public bool multiCaptureMode = false; // Çoklu çekim modu aktif mi
    public int captureCount = 10; // Kaç adet capture alınacak

    [Header("Rotation (Multi-Capture)")]
    public bool rotateAroundTarget = false; // Objeyi 360 derece dönecek miyiz
    public Transform targetObject; // Döndürülecek hedef obje
    public float rotationRadius = 5f; // Daire yarıçapı
    public float rotationHeight = 1f; // Kamera yüksekliği

    [Header("Random Scene")]
    public bool generateRandomObjects = false; // Random sahne oluşturulsun mu
    [Range(1, 50)] public int objectCount = 5; // Kaç obje spawn edilecek
    public float spawnArea = 5f; // Rastgele alan boyutu
    public Vector2 sizeRange = new Vector2(0.5f, 2f); // Objelerin min-max boyutları

    [Header("Status")]
    [SerializeField] private int capturesTaken = 0; // Kaç capture alındı (readonly)

    private CaptureSession _session; // Multi-capture modunda kullanılan session
    private List<GameObject> _spawnedObjects = new List<GameObject>(); // Random objeler listesi
    private GameObject _ground, _light; // Sahne için zemin ve ışık referansları

    void Start()
    {
        // Kaydetme yolu boşsa default değer ver
        if (string.IsNullOrEmpty(savePath))
            savePath = Path.Combine(Application.dataPath, "Captures");

        // Oyun başlarken random sahne kurulacaksa oluştur
        if (generateRandomObjects)
            GenerateScene();

        // Oyun başlar başlamaz capture alınsın istiyorsak
        if (captureOnStart)
            TakeCapture();
    }

    void Update()
    {
        // Space tuşuna basınca capture başlat
        if (Input.GetKeyDown(captureKey))
            TakeCapture();

        // 'R' tuşuna basarak sahneyi yenile (Random Scene modu aktifse)
        if (Input.GetKeyDown(KeyCode.R) && generateRandomObjects)
        {
            ClearObjects(); // Eski objeleri temizle
            GenerateScene(); // Yeni random sahne oluştur
        }
    }

    public void TakeCapture()
    {
        // Eğer multi-capture modu açıksa coroutine başlat
        if (multiCaptureMode)
            StartCoroutine(MultiCapture()); // StartCoroutine tek frame'de değil de frame frame çalışmasını sağlıyor.
        else
            SingleCapture(); // Tek kare yakala
    }

    private void SingleCapture()
    {
        // Her seferinde yeni rastgele sahne istiyorsak
        if (generateRandomObjects)
        {
            ClearObjects();
            GenerateScene();
        }

        // CaptureSession oluştur (using ile bitince otomatik Dispose olur)
        using (var session = CreateSession())
        {
            if (session == null) return;
            
            // Eğer kullanıcı bir kamera seçmişse onu referans al
            if (sourceCamera != null)
                session.SetPosition(sourceCamera.transform);
            
            session.Capture(); // Tek kare al
            capturesTaken++;
        }
    }

    private IEnumerator MultiCapture()
    {
        // RotateAroundTarget açık ama target yoksa hata
        if (rotateAroundTarget && targetObject == null)
        {
            Debug.LogError("[CaptureController] Target object required for rotation!");
            yield break;
        }

        // Bu batch için özel bir klasör oluştur
        string batchFolder = Path.Combine(
            string.IsNullOrEmpty(savePath) ? Path.Combine(Application.dataPath, "Captures") : savePath,
            $"Batch_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}"
        );

        // Multi-capture için tek bir session oluştur
        _session = CreateSession(batchFolder);
        if (_session == null) yield break;

        _session.UseTimestamp = false; // Dosya isimlerinde timestamp istemiyoruz

        // captureCount kadar kare yakala
        for (int i = 0; i < captureCount; i++)
        {
            // Her karede yeni random sahne istiyorsak
            if (generateRandomObjects)
            {
                ClearObjects();
                GenerateScene();
                yield return new WaitForEndOfFrame(); // Sahne otursun
            }

            // Objeyi 360 derece döneceksek
            if (rotateAroundTarget && targetObject != null)
            {
                float angle = (i / (float)captureCount) * 360f * Mathf.Deg2Rad;
                Vector3 pos = targetObject.position + new Vector3(
                    Mathf.Cos(angle) * rotationRadius,
                    rotationHeight,
                    Mathf.Sin(angle) * rotationRadius
                );

                // Kamera objeye baksın
                _session.SetPosition(pos, Quaternion.LookRotation(targetObject.position - pos));
            }
            else if (sourceCamera != null)
            {
                // Kullanıcının kamerasına göre konumlandır
                _session.SetPosition(sourceCamera.transform);
            }

            // Dosya isimleri baseName_index formatında olsun
            _session.Capture($"{baseName}_{i:D4}");
            capturesTaken++;
            
            yield return new WaitForEndOfFrame(); // Sonraki kare
        }

        // Multi-capture bitti → session yok et
        _session.Dispose();
        _session = null;
        
        Debug.Log($"[CaptureController] Multi-capture complete: {captureCount} captures");
    }

    private CaptureSession CreateSession(string path = null)
    {
        // Kayıt klasörü doğru şekilde belirle
        path ??= string.IsNullOrEmpty(savePath) ? Path.Combine(Application.dataPath, "Captures") : savePath;

        var resolution = new Vector2Int(width, height);

        // Önce kullanıcı kamerasını kullan
        CaptureSession session = sourceCamera != null
            ? CaptureSession.Create(sourceCamera, path, resolution)
            // Yoksa main camera dene
            : Camera.main != null
                ? CaptureSession.Create(Camera.main, path, resolution)
                // Hiç kamera yoksa kendi kamerasını oluştur
                : CaptureSession.Create(Vector3.zero, Quaternion.identity, path, resolution);

        if (session == null) return null;

        // Temel ayarlar
        session.BaseName = baseName;
        session.UseTimestamp = useTimestamp;

        // Hangi kanallar alınacak (bitwise)
        var channels = CaptureSession.CaptureChannels.None;
        if (captureColor) channels |= CaptureSession.CaptureChannels.Color;
        if (captureDepth) channels |= CaptureSession.CaptureChannels.Depth;
        if (captureNormals) channels |= CaptureSession.CaptureChannels.Normals;
        session.Channels = channels;

        return session;
    }

    private void GenerateScene()
    {
        // Eğer daha önce zemin oluşturulmamışsa oluştur
        if (_ground == null)
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _ground.name = "Ground";
            _ground.transform.localScale = Vector3.one * (spawnArea / 5f); // Zemin spawn area'ya göre ölçeklenir
            _ground.GetComponent<Renderer>().material.color = Color.gray;
        }

        // Işık yoksa oluştur
        if (_light == null)
        {
            _light = new GameObject("Light");
            var l = _light.AddComponent<Light>();
            l.type = LightType.Directional;
            _light.transform.rotation = Quaternion.Euler(50, -30, 0);
        }

        // Random geometrik objeleri spawn et
        var types = new[] { PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Capsule, PrimitiveType.Cylinder };
        
        for (int i = 0; i < objectCount; i++)
        {
            var obj = GameObject.CreatePrimitive(types[Random.Range(0, types.Length)]);
            obj.name = $"Object_{i}";

            // X-Z düzleminde rastgele pozisyon, Y ekseninde rastgele yükseklik
            obj.transform.position = new Vector3(
                Random.Range(-spawnArea / 2f, spawnArea / 2f),
                Random.Range(sizeRange.x, sizeRange.y),
                Random.Range(-spawnArea / 2f, spawnArea / 2f)
            );

            obj.transform.rotation = Random.rotation; // Rastgele yön
            obj.transform.localScale = Vector3.one * Random.Range(sizeRange.x, sizeRange.y); // Rastgele boyut
            obj.GetComponent<Renderer>().material.color = Random.ColorHSV(); // Rastgele renk
            _spawnedObjects.Add(obj);
        }
    }

    private void ClearObjects()
    {
        // Random spawn edilen objeleri sil
        foreach (var obj in _spawnedObjects)
            if (obj != null) Destroy(obj);

        _spawnedObjects.Clear();
    }

    public void ClearAll()
    {
        // Tüm random objeleri sil
        ClearObjects();

        // Ground ve ışığı sil
        if (_ground != null) { Destroy(_ground); _ground = null; }
        if (_light != null) { Destroy(_light); _light = null; }
    }

    void OnDestroy()
    {
        // MultiCapture'da açık kalmış session varsa temizle
        _session?.Dispose();

        // Sahneyi temizle
        ClearAll();
    }
}

#if UNITY_EDITOR
// Inspector görünümünü özelleştiren Editor scripti
[CustomEditor(typeof(CaptureController))]
public class CaptureControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var controller = (CaptureController)target;
        serializedObject.Update();

        // Save path UI
        EditorGUILayout.BeginHorizontal();
        var pathProp = serializedObject.FindProperty("savePath");
        EditorGUILayout.PropertyField(pathProp);

        // Klasör seçme butonu
        if (GUILayout.Button("📁", GUILayout.Width(30)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Save Folder", controller.savePath, "");
            if (!string.IsNullOrEmpty(path))
            {
                pathProp.stringValue = path;
                serializedObject.ApplyModifiedProperties();
            }
        }
        EditorGUILayout.EndHorizontal();

        // Panelde kalan tüm alanları çiz
        DrawPropertiesExcluding(serializedObject, "m_Script", "savePath");
        serializedObject.ApplyModifiedProperties();

        // Oyun çalışırken özel butonlar
        if (Application.isPlaying)
        {
            EditorGUILayout.Space(10);
            if (GUILayout.Button("Take Capture", GUILayout.Height(30)))
                controller.TakeCapture();

            if (GUILayout.Button("Clear Scene"))
                controller.ClearAll();
        }
    }
}
#endif
