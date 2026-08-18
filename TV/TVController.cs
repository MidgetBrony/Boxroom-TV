using MelonLoader;
using MelonLoader.Utils;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Boxroom_TV.Videos;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.Rendering;
using Unity.Collections;
using SteamShelf;
using SteamShelf.Input;

namespace Boxroom_TV.TV
{
    public class TVController : MonoBehaviour
    {
        private RenderTexture renderTexture;
        private VideoPlayer player;
        private Renderer targetRenderer;
        private Light glowLight;
        private RenderTexture glowSampleRT;
        private Texture2D glowSampleTex;
        private int materialIndex;
        private float localVolume = 1f;
        private float autosaveTimer = 0f;
        private float colorSampleTimer = 0f;
        private int currentIndex = 0;
        private float brightness = 1f;
        private static readonly List<TVController> AllTVs = new List<TVController>();
        private bool isSynced = false;
        private bool showUI = false;
        private bool isLooping = true;
        private bool hasVideoLoaded = false;
        private bool showMediaBrowser = false;
        private const float MinAudioDistance = 0.5f;
        private const float MaxAudioDistance = 8f;
        public bool HasVideoLoaded => hasVideoLoaded;
        private static readonly KeyCode ToggleKey = KeyCode.T;
        private string urlInputText = "";
        private string StateKey;
        private List<string> videoFiles = new List<string>();
        private Vector2 mediaBrowserScroll;

        public void Setup(Renderer renderer, int matIndex, Material overrideMaterial)
        {
            if (isSetup) return;
            isSetup = true;
            if (!AllTVs.Contains(this))
                AllTVs.Add(this);
            StateKey = $"{transform.position.x:F2}_{transform.position.y:F2}_{transform.position.z:F2}";

            if (overrideMaterial != null)
            {
                Material[] currentMats = renderer.materials;
                currentMats[matIndex] = new Material(overrideMaterial);
                renderer.materials = currentMats;
            }

            MelonLogger.Msg($"[Boxroom-TV] Renderer '{renderer.name}' has {renderer.materials.Length} material(s). Using index {matIndex}.");
            for (int i = 0; i < renderer.materials.Length; i++)
            {
                MelonLogger.Msg($"[Boxroom-TV]   [{i}] {renderer.materials[i].name} (shader: {renderer.materials[i].shader.name})");
            }

            targetRenderer = renderer;
            materialIndex = matIndex;

            if (renderTexture == null)
            {
                renderTexture = new RenderTexture(1280, 720, 24)
                {
                    name = "Boxroom-TV-" + gameObject.GetInstanceID(),
                    useMipMap = false,
                    autoGenerateMips = false
                };
                renderTexture.Create();
                if (glowSampleRT == null)
                    glowSampleRT = new RenderTexture(8, 8, 0);
            }

            Material[] mats = targetRenderer.materials;
            Material screenMat = mats[materialIndex];

            screenMat.SetTexture("_MainTex", renderTexture);
            screenMat.mainTexture = renderTexture;

            targetRenderer.materials = mats;

            GameObject glowObj = new GameObject("Boxroom-TV-Glow");
            glowObj.transform.SetParent(transform, false);
            glowLight = glowObj.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.range = 3f;
            glowLight.color = new Color(0.6f, 0.75f, 1f);
            glowLight.intensity = 0f;
            glowLight.shadows = LightShadows.None;
            glowLight.renderMode = LightRenderMode.ForcePixel;
            glowObj.transform.position = renderer.bounds.center;

            RefreshVideoList();

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.panStereo = 0f;
            audioSource.volume = localVolume;

            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = isLooping;
            player.skipOnDrop = true;
            player.source = VideoSource.Url;
            player.renderMode = VideoRenderMode.MaterialOverride;
            player.targetMaterialRenderer = targetRenderer;
            player.targetMaterialProperty = "_MainTex";

            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            player.SetTargetAudioSource(0, audioSource);

            player.errorReceived += (vp, err) => MelonLogger.Error($"[Boxroom-TV] VideoPlayer Error: {err}");
            player.prepareCompleted += vp => vp.Play();

            TVSaveEntry saved = TVStateStore.Load(StateKey);
            if (saved != null && saved.VideoFiles != null && saved.VideoFiles.Count > 0)
            {
                videoFiles = saved.VideoFiles;
                currentIndex = saved.CurrentIndex;
                brightness = saved.Brightness;
                isOn = saved.IsOn;
                localVolume = saved.Volume;
                hasVideoLoaded = true;

                player.url = videoFiles[currentIndex];

                VideoPlayer.EventHandler restoreHandler = null;
                restoreHandler = vp =>
                {
                    vp.time = saved.PlaybackTime;
                    if (isOn) vp.Play();
                    player.prepareCompleted -= restoreHandler;
                };
                player.prepareCompleted += restoreHandler;
                player.Prepare();

                Material restoreMat = targetRenderer.materials[materialIndex];
                if (!isOn)
                {
                    restoreMat.SetTexture("_MainTex", Texture2D.blackTexture);
                    restoreMat.SetFloat("_EmissionStrength", 0f);
                }
                else
                {
                    restoreMat.SetFloat("_EmissionStrength", brightness);
                }
            }
            else if (videoFiles.Count > 0)
            {
                LoadVideo(currentIndex);
            }
        }
        public void ToggleLoop()
        {
            isLooping = !isLooping;
            if (player != null)
                player.isLooping = isLooping;
            SaveState();
        }

        public void LoadFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            videoFiles = new List<string> { url.Trim() };
            currentIndex = 0;
            hasVideoLoaded = true;

            if (player != null)
                LoadVideo(0);
        }
        private List<string> GetMediaFolderFiles()
        {
            string mediaFolder = Path.Combine(MelonEnvironment.ModsDirectory, "Boxroom-TV", "Media");
            Directory.CreateDirectory(mediaFolder);
            return Directory.GetFiles(mediaFolder, "*.mp4").OrderBy(f => f).ToList();
        }

        public void LoadFromMediaFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            videoFiles = new List<string> { path };
            currentIndex = 0;
            LoadVideo(0);
            showMediaBrowser = false;
        }

        private void RefreshVideoList()
        {
            string mediaFolder = Path.Combine(MelonEnvironment.ModsDirectory, "Boxroom-TV", "Media");
            Directory.CreateDirectory(mediaFolder);
            videoFiles = Directory.GetFiles(mediaFolder, "*.mp4").OrderBy(f => f).ToList();
        }

        private void LoadVideo(int index)
        {
            if (videoFiles.Count == 0) return;
            currentIndex = ((index % videoFiles.Count) + videoFiles.Count) % videoFiles.Count;
            player.url = videoFiles[currentIndex];
            player.Prepare();
            hasVideoLoaded = true;
            if (glowLight != null)
            {
                glowLight.intensity = isOn ? brightness * 1.2f : 0f;
            }
            PropagateSync();
            SaveState();
        }

        public void NextVideo() => LoadVideo(currentIndex + 1);
        public void PreviousVideo() => LoadVideo(currentIndex - 1);

        public void TogglePlayPause()
        {
            if (player == null) return;
            if (player.isPlaying) player.Pause();
            else player.Play();
            PropagateSync();
        }

        public void StopVideo()
        {
            if (player != null)
                player.Stop();

            hasVideoLoaded = false;
            videoFiles.Clear();
            currentIndex = 0;

            Material mat = targetRenderer.materials[materialIndex];
            mat.SetTexture("_MainTex", Texture2D.blackTexture);
            mat.SetFloat("_EmissionStrength", 0f);
            if (glowLight != null) glowLight.intensity = 0f;

            SaveState();
        }

        public void LoadFromMediaFolder()
        {
            RefreshVideoList();
            if (videoFiles.Count > 0)
                LoadVideo(0);
        }

        private bool isOn = true;
        private double savedTime = 0;
        private AudioSource audioSource;
        private bool isSetup = false;
        public bool IsSetup => isSetup;
        private bool isDraggingScrub = false;
        private float scrubPreviewTime = 0f;
        private void SaveState()
        {
            if (string.IsNullOrEmpty(StateKey)) return;

            TVStateStore.Save(StateKey, new TVSaveEntry
            {
                VideoFiles = new List<string>(videoFiles),
                CurrentIndex = currentIndex,
                PlaybackTime = player != null ? player.time : 0,
                Brightness = brightness,
                IsOn = isOn,
                Volume = localVolume
            });
        }

        public void AdjustBrightness(float delta)
        {
            brightness = Mathf.Clamp(brightness + delta, 0f, 3f);

            Material mat = targetRenderer.materials[materialIndex];
            mat.SetFloat("_EmissionStrength", brightness);
            glowLight.intensity = isOn ? Mathf.Max(brightness * 1.2f, 0.3f) : 0f;

            SaveState();
        }
        public void ToggleSync()
        {
            isSynced = !isSynced;
        }

        private void PropagateSync()
        {
            if (!isSynced) return;

            foreach (TVController tv in AllTVs)
            {
                if (tv == this || !tv.isSynced) continue;
                tv.ReceiveSync(videoFiles, currentIndex, player != null ? player.time : 0, player != null && player.isPlaying);
            }
        }

        private void ReceiveSync(List<string> files, int idx, double time, bool playing)
        {
            if (player == null) return;

            bool sameVideo = videoFiles != null && videoFiles.SequenceEqual(files) && currentIndex == idx;

            if (!sameVideo)
            {
                videoFiles = new List<string>(files);
                currentIndex = idx;
                player.url = videoFiles[currentIndex];

                VideoPlayer.EventHandler syncHandler = null;
                syncHandler = vp =>
                {
                    vp.time = time;
                    if (playing) vp.Play(); else vp.Pause();
                    player.prepareCompleted -= syncHandler;
                };
                player.prepareCompleted += syncHandler;
                player.Prepare();
                hasVideoLoaded = true;
            }
            else
            {
                player.time = time;
                if (playing) player.Play(); else player.Pause();
            }
        }
        public void TogglePower()
        {
            isOn = !isOn;
            Material mat = targetRenderer.materials[materialIndex];

            if (isOn)
            {
                if (player != null)
                {
                    player.enabled = true;
                    player.time = savedTime;
                    player.Play();
                }
                mat.SetTexture("_MainTex", renderTexture);
                mat.SetFloat("_EmissionStrength", brightness);
                if (glowLight != null)
                {
                    glowLight.intensity = isOn ? Mathf.Max(brightness * 1.2f, 0.3f) : 0f;
                }
            }
            else
            {
                if (player != null)
                {
                    savedTime = player.time;
                    player.Pause();
                    player.enabled = false;
                }
                mat.SetTexture("_MainTex", Texture2D.blackTexture);
                mat.SetFloat("_EmissionStrength", 0f);
                if (glowLight != null)
                {
                    glowLight.intensity = isOn ? Mathf.Max(brightness * 1.2f, 0.3f) : 0f;
                }
            }
            SaveState();
        }

        private void OpenUI()
        {
            showUI = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Singleton<InputManager>.Instance.SwapToInputMap(EInputMap.UI);
        }

        private void CloseUI()
        {
            showUI = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Singleton<InputManager>.Instance.SwapToInputMap(EInputMap.Player);
        }

        private void Update()
        {
            UpdateSpatialVolume();

            if (hasVideoLoaded)
            {
                autosaveTimer += Time.deltaTime;
                if (autosaveTimer >= 5f)
                {
                    autosaveTimer = 0f;
                    SaveState();
                }
            }

            if (isOn && hasVideoLoaded && renderTexture != null)
            {
                colorSampleTimer += Time.deltaTime;
                if (colorSampleTimer >= 0.08f)
                {
                    colorSampleTimer = 0f;
                    SampleGlowColorSync();
                }
            }

            if (Input.GetKeyDown(ToggleKey) && (showUI || IsLookedAt()))
            {
                if (showUI) CloseUI();
                else OpenUI();
            }
        }

        private void UpdateSpatialVolume()
        {
            if (audioSource == null) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            float distance = Vector3.Distance(cam.transform.position, transform.position);
            float t = Mathf.InverseLerp(MaxAudioDistance, MinAudioDistance, distance);
            float falloff = Mathf.Clamp01(t);

            audioSource.volume = localVolume * falloff;
        }

        private void SampleGlowColorSync()
        {
            if (glowSampleRT == null)
                glowSampleRT = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);

            Texture liveTexture = player != null ? player.texture : null;
            if (liveTexture == null) return;

            Graphics.Blit(liveTexture, glowSampleRT);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = glowSampleRT;

            if (glowSampleTex == null)
                glowSampleTex = new Texture2D(8, 8, TextureFormat.RGB24, false);

            glowSampleTex.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
            glowSampleTex.Apply(false);

            RenderTexture.active = previous;

            Color32[] pixels = glowSampleTex.GetPixels32();
            if (pixels.Length == 0 || glowLight == null) return;

            float r = 0, g = 0, b = 0;
            foreach (Color32 p in pixels)
            {
                r += p.r;
                g += p.g;
                b += p.b;
            }

            Color avg = new Color(r / pixels.Length / 255f, g / pixels.Length / 255f, b / pixels.Length / 255f);

            Color.RGBToHSV(avg, out float h, out float s, out float v);
            s = Mathf.Clamp01(s * 1.8f);
            v = Mathf.Clamp01(v * 1.3f);
            glowLight.color = Color.Lerp(glowLight.color, Color.HSVToRGB(h, s, v), 0.4f);

        }

        private bool IsLookedAt()
        {
            Camera cam = Camera.main;
            if (cam == null) return false;

            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 4f))
            {
                return hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform);
            }
            return false;
        }

        public void PlayHeldCase(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return;

            bool sameCase = videoFiles != null && videoFiles.SequenceEqual(paths);
            if (sameCase && player != null && player.isPrepared)
            {
                if (showUI) CloseUI();
                else OpenUI();
                return;
            }

            videoFiles = new List<string>(paths);
            currentIndex = 0;

            if (player != null)
                LoadVideo(0);
        }
        private static string FormatTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return $"{m}:{s:00}";
        }

        public void ToggleUI()
        {
            if (showUI) CloseUI();
            else OpenUI();
        }

        private void OnDestroy()
        {
            if (AllTVs.Contains(this))
                AllTVs.Remove(this);

            if (showUI)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                Singleton<InputManager>.Instance?.SwapToInputMap(EInputMap.Player);
            }
            if (glowSampleRT != null)
                glowSampleRT.Release();
            if (glowSampleTex != null)
                Destroy(glowSampleTex);
        }

        private void OnGUI()
        {
            if (!showUI) return;

            float w = 520;
            float h = showMediaBrowser ? 610 : 475;
            float x = (Screen.width - w) / 2f;
            float y = Screen.height - h - 40f;

            GUI.Box(new Rect(x, y, w, h), "Boxroom-TV Remote");

            if (GUI.Button(new Rect(x + 10, y + 30, 240, 30), player != null && player.isPlaying ? "Pause" : "Play"))
                TogglePlayPause();

            if (GUI.Button(new Rect(x + 270, y + 30, 240, 30), "Close"))
                CloseUI();

            if (GUI.Button(new Rect(x + 10, y + 70, 50, 30), "<<"))
                PreviousVideo();
            GUI.Label(new Rect(x + 70, y + 75, 380, 20), videoFiles.Count > 0 ? Path.GetFileName(videoFiles[currentIndex]) : "No videos found");
            if (GUI.Button(new Rect(x + 460, y + 70, 50, 30), ">>"))
                NextVideo();

            if (player != null && player.length > 0)
            {
                float total = (float)player.length;
                Rect sliderRect = new Rect(x + 10, y + 115, 500, 20);

                Vector2 mouseGuiPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

                if (Input.GetMouseButtonDown(0) && sliderRect.Contains(mouseGuiPos))
                    isDraggingScrub = true;

                if (isDraggingScrub && !Input.GetMouseButton(0))
                    isDraggingScrub = false;

                float sliderValue = isDraggingScrub ? scrubPreviewTime : (float)player.time;
                float newTime = GUI.HorizontalSlider(sliderRect, sliderValue, 0f, total);

                if (isDraggingScrub)
                {
                    scrubPreviewTime = newTime;
                    player.time = newTime;
                }

                GUI.Label(new Rect(x + 10, y + 130, 500, 20), $"{FormatTime((float)player.time)} / {FormatTime(total)}");
            }

            GUI.Label(new Rect(x + 10, y + 155, 500, 20), $"Volume: {Mathf.RoundToInt(localVolume * 100f)}%");
            float newVolume = GUI.HorizontalSlider(new Rect(x + 10, y + 175, 500, 20), localVolume, 0f, 1f);
            if (!Mathf.Approximately(newVolume, localVolume))
            {
                localVolume = newVolume;
                if (audioSource != null) audioSource.volume = localVolume;
                SaveState();
            }

            if (GUI.Button(new Rect(x + 10, y + 205, 240, 30), "Dim -"))
                AdjustBrightness(-0.1f);
            if (GUI.Button(new Rect(x + 270, y + 205, 240, 30), "Bright +"))
                AdjustBrightness(0.1f);

            if (GUI.Button(new Rect(x + 10, y + 240, 500, 30), isLooping ? "Loop: On" : "Loop: Off"))
                ToggleLoop();

            if (GUI.Button(new Rect(x + 10, y + 275, 500, 30), isSynced ? "Sync: On" : "Sync: Off"))
                ToggleSync();

            GUI.Label(new Rect(x + 10, y + 310, 500, 20), "Direct video URL (.mp4 link):");
            urlInputText = GUI.TextField(new Rect(x + 10, y + 330, 390, 25), urlInputText);
            if (GUI.Button(new Rect(x + 410, y + 330, 100, 25), "Load"))
                LoadFromUrl(urlInputText);

            if (GUI.Button(new Rect(x + 10, y + 365, 500, 30), showMediaBrowser ? "Hide Media Folder" : "Browse Media Folder"))
                showMediaBrowser = !showMediaBrowser;

            if (showMediaBrowser)
            {
                List<string> mediaFiles = GetMediaFolderFiles();
                Rect scrollArea = new Rect(x + 10, y + 400, 500, 130);
                Rect viewRect = new Rect(0, 0, 480, mediaFiles.Count * 28);

                mediaBrowserScroll = GUI.BeginScrollView(scrollArea, mediaBrowserScroll, viewRect);
                for (int i = 0; i < mediaFiles.Count; i++)
                {
                    if (GUI.Button(new Rect(0, i * 28, 480, 25), Path.GetFileName(mediaFiles[i])))
                        LoadFromMediaFile(mediaFiles[i]);
                }
                GUI.EndScrollView();

                if (GUI.Button(new Rect(x + 10, y + 535, 500, 30), isOn ? "Turn Off" : "Turn On"))
                    TogglePower();

                if (GUI.Button(new Rect(x + 10, y + 570, 500, 30), "Stop Video"))
                    StopVideo();
            }
            else
            {
                if (GUI.Button(new Rect(x + 10, y + 400, 500, 30), isOn ? "Turn Off" : "Turn On"))
                    TogglePower();

                if (GUI.Button(new Rect(x + 10, y + 435, 500, 30), "Stop Video"))
                    StopVideo();
            }
        }
    }
}
