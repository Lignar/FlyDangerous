using System.Collections.Generic;
using Core;
using Core.Player;
using UnityEngine;

namespace Game_UI {
    public class TargettingSystem : MonoBehaviour {
        [SerializeField] private Target targetPrefab;
        Dictionary<ShipPlayer, Target> _players = new Dictionary<ShipPlayer, Target>();
        
        // Update is called once per frame
        void Update() {
            var players = FindObjectsOfType<ShipPlayer>();
            
            // if we don't have (players - 1) targets, rebuild 
            if (_players.Count != players.Length - 1) {
                foreach (var keyValuePair in _players) {
                    Destroy(keyValuePair.Value);
                }
                _players.Clear();
                foreach (var shipPlayer in players) {
                    if (!shipPlayer.isLocalPlayer) {
                        var target = Instantiate(targetPrefab, transform);
                        _players.Add(shipPlayer, target);
                    }
                }
            }
            
            // update target objects for players
            foreach (var keyValuePair in _players) {
                var player = keyValuePair.Key;
                var target = keyValuePair.Value;
                
                var playerName = player.playerName;
                var position = ShipPlayer.FindLocal.User.UserHeadPosition;
                
                var originPosition = position;
                var targetPosition = player.User.transform.position;
                
                var distance = Vector3.Distance(originPosition, targetPosition);
                var direction = (targetPosition - originPosition).normalized;

                target.Name = playerName;
                target.DistanceMeters = distance;

                var minDistance = 10f;
                var maxDistance = 30f + minDistance;
                
                target.transform.position = Vector3.MoveTowards(originPosition, targetPosition + (direction * minDistance), maxDistance);
                
                // rotate sprite to face HMD in VR (looks odd in flat screen!)
                if (Game.Instance.IsVREnabled) {
                    target.transform.LookAt(originPosition);
                    target.transform.RotateAround(target.transform.position, target.transform.up, 180f);
                }
            }
        }
    }
}
