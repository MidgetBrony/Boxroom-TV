using MelonLoader;
using MelonLoader.Utils;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Boxroom_TV.Videos;
using UnityEngine;
using UnityEngine.Video;
using SteamShelf;
using SteamShelf.Input;

namespace Boxroom_TV.TV
{
    public class TVController : MonoBehaviour
    {
        private RenderTexture renderTexture;
        private VideoPlayer player;
        private Renderer targetRenderer;
        private int materialIndex;
        private string StateKey;
        private float autosaveTimer = 0f;
        private List<string> videoFiles = new List<string>();
        private int currentIndex = 0;
        private float brightness = 1f;
        private bool showUI = false;
        private const float MinAudioDistance = 0.5f;
        private const float MaxAudioDistance = 8f;
        private bool hasVideoLoaded = false;
        public bool HasVideoLoaded => hasVideoLoaded;
        private static readonly KeyCode ToggleKey = KeyCode.T;
        private string urlInputText = "";
        private bool showMediaBrowser = false;
        private Vector2 mediaBrowserScroll;

        public void Setup(Renderer renderer, int matIndex, Material overrideMaterial)
        {
            if (isSetup) return;
            isSetup = true;
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
            }

            Material[] mats = targetRenderer.materials;
            Material screenMat = mats[materialIndex];

            screenMat.SetTexture("_MainTex", renderTexture);
            screenMat.mainTexture = renderTexture;

            targetRenderer.materials = mats;

            RefreshVideoList();

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.panStereo = 0f;
            audioSource.volume = Core.VolumePref.Value;

            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;
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
            SaveState();
        }

        public void NextVideo() => LoadVideo(currentIndex + 1);
        public void PreviousVideo() => LoadVideo(currentIndex - 1);

        public void TogglePlayPause()
        {
            if (player == null) return;
            if (player.isPlaying) player.Pause();
            else player.Play();
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
                IsOn = isOn
            });
        }

        public void AdjustBrightness(float delta)
        {
            brightness = Mathf.Clamp(brightness + delta, 0f, 3f);

            Material mat = targetRenderer.materials[materialIndex];
            mat.SetFloat("_EmissionStrength", brightness);

            SaveState();
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

            audioSource.volume = Core.VolumePref.Value * falloff;
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

        private void OnGUI()
        {
            if (!showUI) return;

            float w = 520, h = showMediaBrowser ? 530 : 370;
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

            GUI.Label(new Rect(x + 10, y + 155, 500, 20), $"Volume: {Mathf.RoundToInt(Core.VolumePref.Value * 100f)}%");
            float newVolume = GUI.HorizontalSlider(new Rect(x + 10, y + 175, 500, 20), Core.VolumePref.Value, 0f, 1f);
            if (!Mathf.Approximately(newVolume, Core.VolumePref.Value))
            {
                Core.VolumePref.Value = newVolume;
            }

            if (GUI.Button(new Rect(x + 10, y + 205, 240, 30), "Dim -"))
                AdjustBrightness(-0.1f);
            if (GUI.Button(new Rect(x + 270, y + 205, 240, 30), "Bright +"))
                AdjustBrightness(0.1f);

            GUI.Label(new Rect(x + 10, y + 245, 500, 20), "Direct video URL (.mp4 link):");
            urlInputText = GUI.TextField(new Rect(x + 10, y + 265, 390, 25), urlInputText);
            if (GUI.Button(new Rect(x + 410, y + 265, 100, 25), "Load"))
                LoadFromUrl(urlInputText);

            if (GUI.Button(new Rect(x + 10, y + 300, 500, 30), showMediaBrowser ? "Hide Media Folder" : "Browse Media Folder"))
                showMediaBrowser = !showMediaBrowser;

            if (showMediaBrowser)
            {
                List<string> mediaFiles = GetMediaFolderFiles();
                Rect scrollArea = new Rect(x + 10, y + 335, 500, 130);
                Rect viewRect = new Rect(0, 0, 480, mediaFiles.Count * 28);

                mediaBrowserScroll = GUI.BeginScrollView(scrollArea, mediaBrowserScroll, viewRect);
                for (int i = 0; i < mediaFiles.Count; i++)
                {
                    if (GUI.Button(new Rect(0, i * 28, 480, 25), Path.GetFileName(mediaFiles[i])))
                        LoadFromMediaFile(mediaFiles[i]);
                }
                GUI.EndScrollView();

                if (GUI.Button(new Rect(x + 10, y + 470, 500, 30), isOn ? "Turn Off" : "Turn On"))
                    TogglePower();

                if (GUI.Button(new Rect(x + 10, y + 500, 500, 30), "Stop Video"))
                    StopVideo();
            }
            else
            {
                if (GUI.Button(new Rect(x + 10, y + 335, 500, 30), isOn ? "Turn Off" : "Turn On"))
                    TogglePower();

                if (GUI.Button(new Rect(x + 10, y + 370, 500, 30), "Stop Video"))
                    StopVideo();
            }
        }
    }
}
