using Boxroom_TV.Patches;
using Boxroom_TV.TV;
using Boxroom_TV.Videos;
using HarmonyLib;
using MelonLoader;
using SteamShelf;
using SteamShelf.Input;
using SteamShelf.Media.Albums;
using SteamShelf.Placeables;
using SteamShelf.PlayerTools;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

[assembly: MelonInfo(typeof(Boxroom_TV.Core), "Boxroom-TV", "1.3.0", "scumgr33n", null)]
[assembly: MelonGame("NestedLoop", "BOXROOM")]

namespace Boxroom_TV
{
    public class Core : MelonMod
    {
        public static MelonPreferences_Category PrefsCategory;
        public static MelonPreferences_Entry<float> VolumePref;

        public override void OnInitializeMelon()
        {
            PrefsCategory = MelonPreferences.CreateCategory("Boxroom-TV");
            VolumePref = PrefsCategory.CreateEntry(
                "Volume", 1f, "Volume",
                "TV volume, from 0.0 (silent) to 1.0 (full)");

            LoggerInstance.Msg("Initialized Boxroom-TV");
        }

        private void PlayVideoOnTV(GameImagePainter painter, SteamGameData heldCase)
        {
            Renderer renderer = PainterReflection.GetRenderer(painter);
            TVHelper.IsSupportedDisplay(painter, out PlacementTag tag);
            int index = TVHelper.GetMaterialIndexOverride(tag, PainterReflection.GetMaterialIndex(painter));

            TVController controller = painter.GetComponent<TVController>();
            if (controller == null)
                controller = painter.gameObject.AddComponent<TVController>();

            controller.Setup(renderer, index, PainterReflection.GetOverrideMaterial(painter));
            controller.PlayHeldCase(VideoLibrarySystem.AppIdToVideoPaths[heldCase.AppId]);

            LoggerInstance.Msg($"Playing '{heldCase.Name}' on TV.");
        }
        private void OpenTVRemote(GameImagePainter painter, PlacementTag tag)
        {
            Renderer renderer = PainterReflection.GetRenderer(painter);
            int index = TVHelper.GetMaterialIndexOverride(tag, PainterReflection.GetMaterialIndex(painter));

            TVController controller = painter.GetComponent<TVController>();
            if (controller == null)
                controller = painter.gameObject.AddComponent<TVController>();

            controller.Setup(renderer, index, PainterReflection.GetOverrideMaterial(painter));
            controller.ToggleUI();
        }

        public override void OnUpdate()
        {
            if (Input.GetKey(KeyCode.LeftAlt) && Input.GetMouseButtonDown(0))
            {
                TryInteractWithTV();
            }

            if (Input.GetKeyDown(KeyCode.V))
            {
                SpawnMovieBox();
            }

            if (isPlacingMovieBox)
                UpdateMovieBoxPlacement();

            if (interactionTool == null)
                interactionTool = UnityEngine.Object.FindObjectOfType<PlayerInteractionTool>();

            if (interactionTool != null)
            {
                PlayerInputContext input = Singleton<InputManager>.Instance?.CurrentPlayerInputContext;

                if (input != null && input.PrimaryPressedThisFrame)
                {
                    if (interactionTool.IsHoldingGameBox)
                    {
                        SteamGameData held = interactionTool.CurrentHeldSteamGameData;

                        if (held != null && VideoLibrarySystem.KnownVideoGameAppIds.Contains(held.AppId))
                        {
                            PlacementTag lookingAt = interactionTool.LookingAtPlaceableTag;
                            GameImagePainter painter = lookingAt != null ? lookingAt.GetComponent<GameImagePainter>() : null;

                            if (painter != null && TVHelper.IsSupportedDisplay(painter, out _))
                            {
                                PlayVideoOnTV(painter, held);
                            }
                        }
                    }
                    else if (!interactionTool.IsHoldingProp)
                    {
                        PlacementTag lookingAt = interactionTool.LookingAtPlaceableTag;
                        GameImagePainter painter = lookingAt != null ? lookingAt.GetComponent<GameImagePainter>() : null;

                        if (painter != null && TVHelper.IsSupportedDisplay(painter, out PlacementTag tag))
                        {
                            OpenTVRemote(painter, tag);
                        }
                    }
                }
            }
        }
        private PlayerInteractionTool interactionTool;

        private FieldInfo controllerField;
        private bool isPlacingMovieBox = false;

        private void SpawnMovieBox()
        {
            PlaceableData[] allPlaceables = Resources.FindObjectsOfTypeAll<PlaceableData>();

            PlaceableData target = allPlaceables.FirstOrDefault(p =>
                p.ID != null &&
                p.ID.ToLower().Contains("unplaced") &&
                p.ID.ToLower().Contains("game"));

            if (target == null)
            {
                LoggerInstance.Warning("Could not find the Unplaced Games Box PlaceableData.");
                return;
            }

            if (interactionTool == null)
                interactionTool = UnityEngine.Object.FindObjectOfType<PlayerInteractionTool>();

            if (interactionTool == null)
            {
                LoggerInstance.Warning("Could not find PlayerInteractionTool.");
                return;
            }

            if (controllerField == null)
                controllerField = typeof(PlayerTool).GetField("controller", BindingFlags.NonPublic | BindingFlags.Instance);

            object controller = controllerField?.GetValue(interactionTool);
            if (controller == null)
            {
                LoggerInstance.Warning("Could not access controller field via reflection (name may differ).");
                return;
            }

            ObjectPlacer objectPlacer = controller.GetType().GetProperty("ObjectPlacer")?.GetValue(controller) as ObjectPlacer;
            PlacementRulesRegistry registry = controller.GetType().GetProperty("Registry")?.GetValue(controller) as PlacementRulesRegistry;

            if (objectPlacer == null || registry == null)
            {
                LoggerInstance.Warning("Could not access ObjectPlacer/Registry via reflection.");
                return;
            }

            Addressables.LoadAssetAsync<GameObject>(target.AssetReference.RuntimeKey).Completed += handle =>
            {
                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    LoggerInstance.Error("Failed to load Movie Box prefab.");
                    return;
                }

                PlacementToolSettings settings = registry.Get(target.PlacementType);
                FieldInfo validMatField = typeof(PlayerInteractionTool).GetField("validPreviewMaterial", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo invalidMatField = typeof(PlayerInteractionTool).GetField("invalidPreviewMaterial", BindingFlags.NonPublic | BindingFlags.Instance);

                Material validMat = validMatField?.GetValue(interactionTool) as Material;
                Material invalidMat = invalidMatField?.GetValue(interactionTool) as Material;

                objectPlacer.Begin(handle.Result, settings, validMat, invalidMat);
                isPlacingMovieBox = true;

                LoggerInstance.Msg("Placing Movie Box — move to position, left-click to place, right-click to cancel.");
            };
        }

        private void UpdateMovieBoxPlacement()
        {
            object controller = controllerField?.GetValue(interactionTool);
            ObjectPlacer objectPlacer = controller?.GetType().GetProperty("ObjectPlacer")?.GetValue(controller) as ObjectPlacer;
            if (objectPlacer == null) { isPlacingMovieBox = false; return; }

            PlayerInputContext input = Singleton<InputManager>.Instance?.CurrentPlayerInputContext;
            if (input == null) return;

            objectPlacer.UpdatePlacement(
                input.PlacementModifierPressed,
                input.RotateLeftPressedThisFrame,
                input.RotateRightPressedThisFrame,
                false,
                false);

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                objectPlacer.End();
                isPlacingMovieBox = false;
                LoggerInstance.Msg("Movie Box placement cancelled.");
                return;
            }

            if (objectPlacer.IsValid && Input.GetMouseButtonDown(0))
            {
                GameObject placed = objectPlacer.CommitPlacement();
                objectPlacer.End();
                isPlacingMovieBox = false;

                if (placed == null) return;

                placed.AddComponent<MovieBoxMarker>();
                PlaceablePainter painter = placed.GetComponent<PlaceablePainter>();
                if (painter == null)
                {
                    painter = placed.AddComponent<PlaceablePainter>();
                    painter.InitializeMaterials();
                    painter.SetupAsForcedPlaceable();
                    painter.ReCacheAllBaseMaterialsToCurrent();
                }
                VideoLibrarySystem.ScanAndRegister();

                UnplacedGamesBox box = placed.GetComponent<UnplacedGamesBox>();
                if (box != null)
                    box.OnPlaced();

                LoggerInstance.Msg("Movie Box placed.");
            }
        }

        private void TryInteractWithTV()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 4f))
                return;

            GameImagePainter painter = hit.collider.GetComponentInParent<GameImagePainter>();
            if (painter == null) return;

            if (!TVHelper.IsSupportedDisplay(painter, out PlacementTag tag))
                return;

            Renderer renderer = PainterReflection.GetRenderer(painter);
            int index = TVHelper.GetMaterialIndexOverride(tag, PainterReflection.GetMaterialIndex(painter));

            TVController controller = painter.GetComponent<TVController>();
            if (controller == null)
                controller = painter.gameObject.AddComponent<TVController>();

            controller.Setup(renderer, index, PainterReflection.GetOverrideMaterial(painter));

            if (!controller.HasVideoLoaded)
                controller.LoadFromMediaFolder();
            else
                controller.TogglePlayPause();
        }
    }
}