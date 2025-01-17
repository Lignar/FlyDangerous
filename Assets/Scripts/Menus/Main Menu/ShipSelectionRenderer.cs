using System;
using Core.Ship;
using Misc;
using UnityEngine;

namespace Menus.Main_Menu {
    public class ShipSelectionRenderer : MonoBehaviour {
        private GameObject _loadedShip;
        private Vector3 _targetScale;
        private Vector3 _rotation = new Vector3(0, 0.5f, 0);
        public void SetShip(ShipMeta shipData) {
            if (_loadedShip) {
                Destroy(_loadedShip);
            }
            
            _loadedShip = Instantiate(Resources.Load(shipData.PrefabToLoad, typeof(GameObject)) as GameObject);
            if (_loadedShip != null) {
                _loadedShip.transform.SetParent(transform, false);
                var layer = LayerMask.NameToLayer("Player");
                _loadedShip.gameObject.layer = layer;
                foreach (Transform child in _loadedShip.transform) {
                    child.SetLayer(layer);
                }
                transform.localScale = Vector3.zero;
                // TODO: measure ship and set an appropriate scale. This works well for the two ships we have now.
                _targetScale = new Vector3(45, 45, 45);
            }
        }
        
        public void SetShipPrimaryColor(string htmlColor) {
            _loadedShip.GetComponent<IShip>().SetPrimaryColor(htmlColor);
        }
        
        public void SetShipAccentColor(string htmlColor) {
            _loadedShip.GetComponent<IShip>().SetAccentColor(htmlColor);
        }

        private void FixedUpdate() {
            transform.Rotate(_rotation);
            transform.localScale = Vector3.Lerp(transform.localScale, _targetScale, 0.01f);
        }
    }
}
