﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Core.MapData;
using Core.Player;
using Den.Tools;
using MapMagic.Core;
using Misc;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Environment = System.Environment;

namespace Core.MapData {
    public class LevelLoader : MonoBehaviour {
        
        public delegate void LevelLoadedAction();
        public static event LevelLoadedAction OnLevelLoaded;
        
        private LevelData _levelData = new LevelData();
        private List<AsyncOperation> _scenesLoading = new List<AsyncOperation>();
        public LevelData LoadedLevelData => _levelData;
        public LevelData LevelDataAtCurrentPosition => GenerateLevelData();
        public GameObject checkpointPrefab;

        public IEnumerator StartGame(LevelData levelData) {
            _levelData = levelData;

            string locationSceneToLoad = levelData.location.SceneToLoad;
            string environmentSceneToLoad = levelData.environment.SceneToLoad;

            // now we can finally start the level load
            _scenesLoading.Add(SceneManager.LoadSceneAsync(environmentSceneToLoad, LoadSceneMode.Additive));
            _scenesLoading.Add(SceneManager.LoadSceneAsync(locationSceneToLoad, LoadSceneMode.Additive));
            _scenesLoading.ForEach(scene => scene.allowSceneActivation = false);

            yield return StartCoroutine(LoadGameScenes());
        }

        public IEnumerator RestartLevel(Action onRestart) {
            var ship = ShipPlayer.FindLocal;
            if (ship) {
                // first let's check if this is a terrain world and handle that appropriately
                var mapMagic = FindObjectOfType<MapMagicObject>();
                
                void DoReset(Vector3 position, Quaternion rotation) {
                    ship.AbsoluteWorldPosition = position;
                    ship.transform.rotation = rotation;
                    ship.Reset();

                    onRestart();
                }

                // the terrain will not be loaded if we teleport there, we need to fade to black, wait for terrain to load, then fade back. This should still be faster than full reload.
                IEnumerator LoadTerrainAndReset(Vector3 position, Quaternion rotation) {
                    DoReset(position, rotation);
                    yield return new WaitForSeconds(0.5f);

                    // wait for fully loaded local terrain
                    while (mapMagic.IsGenerating()) {
                        var progressPercent = Mathf.Min(100, Mathf.Round(mapMagic.GetProgress() * 100));
                        var loadText = GameObject.FindGameObjectWithTag("DynamicLoadingText").GetComponent<Text>();
                        loadText.text = $"Generating terrain ({progressPercent}%)";

                        yield return null;
                    }

                    // unload the loading screen
                    var unload = SceneManager.UnloadSceneAsync("Loading");
                    while (!unload.isDone) {
                        yield return null;
                    }

                    Game.Instance.FadeFromBlack();
                    yield return new WaitForSeconds(0.7f);

                }

                IEnumerator ResetTrackIfNeeded() {
                    // if there's a track in the game world, start it
                    var track = FindObjectOfType<Track>();
                    if (track) {
                        yield return track.StartTrackWithCountdown();
                    }

                    var user = ship.User;
                    if (user) {
                        user.EnableGameInput();
                    }
                }

                var positionToWarpTo = new Vector3 {
                    x = LoadedLevelData.startPosition.x,
                    y = LoadedLevelData.startPosition.y,
                    z = LoadedLevelData.startPosition.z
                };

                var rotationToWarpTo = Quaternion.Euler(LoadedLevelData.startRotation.x,
                    LoadedLevelData.startRotation.y, LoadedLevelData.startRotation.z);
                
                // if multiplayer free-roam and not the host, warp to the host
                if (Game.Instance.SessionType == SessionType.Multiplayer && LoadedLevelData.gameType.CanWarpToHost && !ship.isHost) {
                    FindObjectsOfType<ShipPlayer>().ToList().ForEach(otherShipPlayer => {
                        if (otherShipPlayer.isHost) {
                            var emptyPosition = PositionalHelpers.FindClosestEmptyPosition(otherShipPlayer.AbsoluteWorldPosition, 10);
                            rotationToWarpTo = otherShipPlayer.transform.rotation;
                            positionToWarpTo = emptyPosition + FloatingOrigin.Instance.Origin;
                        }
                    });
                }

                var shipPosition = ship.AbsoluteWorldPosition;
                var distanceToStart = Vector3.Distance(shipPosition, positionToWarpTo);

                // TODO: Make this distance dynamic based on tiles?
                if (mapMagic && ship && distanceToStart > 20000) {
                    yield return StartCoroutine(ShowLoadingScreen(true));
                    yield return StartCoroutine(LoadTerrainAndReset(positionToWarpTo, rotationToWarpTo));
                    yield return ResetTrackIfNeeded();
                }
                else {
                    // don't need to wait for full scene reload, just reset state and notify subscribers
                    DoReset(positionToWarpTo, rotationToWarpTo);
                    yield return ResetTrackIfNeeded();
                }
            }
            else {
                Debug.LogWarning("No local ship player found for restart action");
            }
        }

        // This is a separate action so that we can safely move to a new active loading scene and fully unload everything
         // before moving to any other map or whatever we need to do.
         // On completion it executes callback `then` with a reference to the loading text.
         public IEnumerator ShowLoadingScreen(bool keepScene = false) {
            
             // disable user input if we're in-game while handling everything else
             var ship = ShipPlayer.FindLocal;
             if (ship != null) {
                 ship.User.DisableGameInput();
                 ship.User.DisableUIInput();
             }

             Game.Instance.FadeToBlack();
             yield return new WaitForSeconds(0.5f);
            
             // load loading screen (lol)
             var loadMode = keepScene ? LoadSceneMode.Additive : LoadSceneMode.Single;
             yield return SceneManager.LoadSceneAsync("Loading", loadMode);
         }

         public IEnumerator HideLoadingScreen() {
             // unload the loading screen
             yield return SceneManager.UnloadSceneAsync("Loading");
         }

        // Return a new level data object hydrated with all the information of the current game state
        private LevelData GenerateLevelData() {
            var levelData = new LevelData();
            levelData.name = _levelData.name;
            levelData.gameType = _levelData.gameType;
            levelData.location = _levelData.location;
            levelData.environment = _levelData.environment;
            levelData.terrainSeed = _levelData.terrainSeed;
            levelData.checkpoints = _levelData.checkpoints;

            var ship = ShipPlayer.FindLocal;
            if (ship) {
                var position = ship.AbsoluteWorldPosition;
                var rotation = ship.transform.rotation;
                levelData.startPosition.x = position.x;
                levelData.startPosition.y = position.y;
                levelData.startPosition.z = position.z;
                levelData.startRotation.x = rotation.eulerAngles.x;
                levelData.startRotation.y = rotation.eulerAngles.y;
                levelData.startRotation.z = rotation.eulerAngles.z;
            }

            var track = FindObjectOfType<Track>();
            if (track) {
                var checkpoints = track.Checkpoints;
                levelData.checkpoints = new List<CheckpointLocation>();
                foreach (var checkpoint in checkpoints) {
                    var checkpointLocation = new CheckpointLocation();
                    checkpointLocation.type = checkpoint.Type;
                    checkpointLocation.position = new LevelDataVector3<float>();
                    checkpointLocation.rotation = new LevelDataVector3<float>();

                    var checkpointTransform = checkpoint.transform;
                    var position = checkpointTransform.localPosition;
                    var rotation = checkpointTransform.rotation.eulerAngles;
                    checkpointLocation.position.x = position.x;
                    checkpointLocation.position.y = position.y;
                    checkpointLocation.position.z = position.z;
                    checkpointLocation.rotation.x = rotation.x;
                    checkpointLocation.rotation.y = rotation.y;
                    checkpointLocation.rotation.z = rotation.z;
                    levelData.checkpoints.Add(checkpointLocation);
                }
            }
            return levelData;
        }
        
        IEnumerator LoadGameScenes() {
        
            // disable all game interactions
            Time.timeScale = 0;

            // grab the load text to draw to
            var loadText = GameObject.FindGameObjectWithTag("DynamicLoadingText").GetComponent<Text>();

            float progress = 0;
            for (int i = 0; i < _scenesLoading.Count; ++i) {
                while (_scenesLoading[i].progress < 0.9f) { // this is literally what the unity docs recommend
                    yield return null;
                        
                    progress += _scenesLoading[i].progress;
                    var totalProgress = progress / _scenesLoading.Count;
                    var progressPercent = Mathf.Min(100, Mathf.Round(totalProgress * 100));
                    
                    // set loading text (last scene is always the engine)
                    loadText.text = i == _scenesLoading.Count
                        ? $"Loading Engine ({progressPercent}%)"
                        : $"Loading Assets ({progressPercent}%)";
                    
                    yield return null;
                }
            }
            
            // all scenes have loaded as far as they can without activation, allow them to activate
            for (int i = 0; i < _scenesLoading.Count; ++i) {
                _scenesLoading[i].allowSceneActivation = true;
                while (!_scenesLoading[i].isDone) {
                    yield return null;
                }
            }

            // checkpoint placement
            var track = FindObjectOfType<Track>();
            if (track && _levelData.checkpoints?.Count > 0) {
                _levelData.checkpoints.ForEach(c => {
                    var checkpointObject = Instantiate(checkpointPrefab, track.transform);
                    var checkpoint = checkpointObject.GetComponent<Checkpoint>();
                    checkpoint.Type = c.type;
                    var checkpointObjectTransform = checkpointObject.transform;
                    checkpointObjectTransform.position = new Vector3(
                        c.position.x,
                        c.position.y,
                        c.position.z
                    );
                    checkpointObjectTransform.rotation = Quaternion.Euler(
                        c.rotation.x,
                        c.rotation.y,
                        c.rotation.z
                    );
                    checkpoint.transform.parent = track.transform;
                });
            }

            // if terrain needs to generate, toggle special logic and wait for it to load all primary tiles
            var mapMagic = FindObjectOfType<MapMagicObject>();
            if (mapMagic) {
                // our terrain gen may start disabled to prevent painful threading fun
                mapMagic.enabled = true;
                
                // Stop auto-loading with default seed
                mapMagic.StopGenerate();
                Den.Tools.Tasks.ThreadManager.Abort();
                yield return new WaitForEndOfFrame();
                mapMagic.ClearAll();

                // replace with user seed
                mapMagic.graph.random = new Noise(_levelData.terrainSeed.GetHashCode(), 32768);
                mapMagic.StartGenerate();
                
                // wait for fully loaded local terrain
                while (mapMagic.IsGenerating()) {
                    var progressPercent = Mathf.Min(100, Mathf.Round(mapMagic.GetProgress() * 100));
                    
                    // this entity may be destroyed by server shutdown...
                    if (loadText != null) {
                        loadText.text = $"Generating terrain ({progressPercent}%)\n\n\nSeed: \"{_levelData.terrainSeed}\"";
                    }

                    yield return null;
                }
            }

            _scenesLoading.Clear();

            if (OnLevelLoaded != null) {
                OnLevelLoaded();
            } 
        }
    }
}