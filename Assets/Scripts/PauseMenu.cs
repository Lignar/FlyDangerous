using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Canvas))]
public class PauseMenu : MonoBehaviour
{

    private FlyDangerousActions _gameActions;
    private Canvas _menuCanvas;

    public bool isGameMenuActive {
        get {
            return this._menuCanvas.enabled;
        }
    }
    private void Awake() {
        this._menuCanvas = GetComponent<Canvas>();
        
        this._gameActions = GlobalGameState.Actions;

        this._gameActions.Global.GameMenuToggle.performed += ToggleMenu;
        this._gameActions.Global.GameMenuToggle.canceled += ToggleMenu;
        
        this.HandlePauseGameState();
    }
    
    private void OnEnable() {
        _gameActions.Global.Enable();
    }

    private void OnDisable() {
        _gameActions.Global.Disable();
    }


    public void ToggleMenu(InputAction.CallbackContext context) {
        if (context.ReadValueAsButton()) {
            this._menuCanvas.enabled = !this._menuCanvas.enabled;
            this.HandlePauseGameState();
        }
    }

    // toggle ship controller input and timescales
    private void HandlePauseGameState() {
        if (this.isGameMenuActive) {
            this._gameActions.Ship.Disable();
            Time.timeScale = 0;
        }
        else {
            this._gameActions.Ship.Enable();
            Time.timeScale = 1;
        }
    }
}
