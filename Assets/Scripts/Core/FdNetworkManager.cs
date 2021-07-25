using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Core.Player;
using kcp2k;
using Mirror;
using UnityEngine;

namespace Core {
    
    public enum FdNetworkStatus {
        SinglePlayerMenu,
        LobbyMenu,
        Loading,
        InGame,
    }
    
    public class FdNetworkManager : NetworkManager {
        
        public static FdNetworkManager Instance => singleton as FdNetworkManager;
        
        // TODO: This is game mode dependent
        [SerializeField] private int minPlayers = 2;

        [Header("Room")] 
        [SerializeField] private LobbyPlayer lobbyPlayerPrefab;

        [Header("Loading")] 
        [SerializeField] private LoadingPlayer loadingPlayerPrefab;

        [Header("In-Game")] 
        [SerializeField] private ShipPlayer shipPlayerPrefab;

        private struct StartGameMessage : NetworkMessage {
            public SessionType sessionType;
            public LevelData levelData;
            public bool dynamicPlacement;
        }
        
        public static event Action OnClientConnected;
        public static event Action OnClientDisconnected;
        public List<LobbyPlayer> LobbyPlayers { get; } = new List<LobbyPlayer>();
        public List<LoadingPlayer> LoadingPlayers { get; } = new List<LoadingPlayer>();
        public List<ShipPlayer> ShipPlayers { get; } = new List<ShipPlayer>();
        public KcpTransport NetworkTransport => GetComponent<KcpTransport>();

        private FdNetworkStatus _status = FdNetworkStatus.SinglePlayerMenu;
        private FdNetworkStatus Status => _status;
        
        public IEnumerator WaitForAllPlayersLoaded() {
            yield return LoadingPlayers.All(loadingPlayer => loadingPlayer.IsLoaded) 
                ? null 
                : new WaitForFixedUpdate();
        }
        
        // TODO: finish lobby ready state handling
        public void NotifyPlayersOfReadyState() {
            foreach (var player in LobbyPlayers) {
                player.HandleReadyStatusChanged(IsReadyToLoad());
            }
        }

        private bool IsReadyToLoad() {
            if (numPlayers < minPlayers) {
                return false; 
            }

            foreach (var player in LobbyPlayers) {
                if (!player.isReady) {
                    return false; 
                }
            }

            return true;
        }

        #region Start / Quit Game
        public void StartGameLoadSequence(SessionType sessionType, LevelData levelData, bool dynamicPlacement = false) {
            if (NetworkServer.active) {
                // Transition any lobby players to loading state
                if (_status == FdNetworkStatus.LobbyMenu) {
                    // iterate over a COPY of the lobby players (the List is mutated by transitioning!)
                    foreach (var lobbyPlayer in LobbyPlayers.ToArray()) {
                        TransitionToLoadingPlayer(lobbyPlayer);
                    }
                }

                // notify all clients about the new scene
                NetworkServer.SendToAll(new StartGameMessage {
                    sessionType = sessionType, 
                    levelData = levelData, 
                    dynamicPlacement = dynamicPlacement
                });
            }
            else {
                throw new Exception("Cannot start a game without an active server!");
            }
        }

        private void StartLoadGame(StartGameMessage message) {
            _status = FdNetworkStatus.Loading;
            Game.Instance.StartGame(message.sessionType, message.levelData, message.dynamicPlacement);
        }
        
        public void StartMainGame() {
            _status = FdNetworkStatus.InGame;
            if (NetworkClient.connection.identity.isServer) {
                // iterate over a COPY of the lobby players (the List is mutated by transitioning!)
                foreach (var loadingPlayer in LoadingPlayers.ToArray()) {
                    TransitionToShipPlayer(loadingPlayer);
                }
            }
        }

        #endregion
        
        #region State Management
        
        public void StartLobbyServer() {
            _status = FdNetworkStatus.LobbyMenu;
            StartHost();
            // TODO: This should come from the lobby panel UI element
            maxConnections = 16;
        }

        public void StartLobbyJoin() {
            _status = FdNetworkStatus.LobbyMenu;
            StartClient();
        }

        public void StartOfflineServer() {
            _status = FdNetworkStatus.SinglePlayerMenu;
            maxConnections = 1;
            StartHost();
        }

        public void StopAll() {
            if (mode != NetworkManagerMode.Offline) {
                StopHost();
                StopClient();
            }
            _status = FdNetworkStatus.SinglePlayerMenu;
        }
        
        #endregion

        #region Client Handlers

        // player joins
        public override void OnClientConnect(NetworkConnection conn) {
            Debug.Log("[CLIENT] PLAYER CONNECT");
            base.OnClientConnect(conn);
            OnClientConnected?.Invoke();
            NetworkClient.RegisterHandler<StartGameMessage>(StartLoadGame);
        }
        
        // player leaves
        public override void OnClientDisconnect(NetworkConnection conn) {
            Debug.Log("[CLIENT] PLAYER DISCONNECT");
            base.OnClientDisconnect(conn);
            OnClientDisconnected?.Invoke();
        }

        #endregion
        
        #region Server Handlers

        // player joins
        public override void OnServerConnect(NetworkConnection conn) {
            Debug.Log("[SERVER] PLAYER CONNECT" + " (" + (numPlayers + 1) + " / " + maxConnections + " players)");
            if (numPlayers >= maxConnections) {
                conn.Disconnect();
                // TODO: Send a message why
                return;
            }

            IEnumerator AddNewPlayerConnection() {
                while (!conn.isReady) {
                    yield return new WaitForEndOfFrame();
                }
                
                switch (Status) {
                    case FdNetworkStatus.SinglePlayerMenu:
                        LoadingPlayer loadingPlayer = Instantiate(loadingPlayerPrefab);
                        NetworkServer.AddPlayerForConnection(conn, loadingPlayer.gameObject);
                        if (conn.identity != null) {
                            var player = conn.identity.GetComponent<LoadingPlayer>();
                            AddPlayer(player);
                        }
                        break;
                
                    case FdNetworkStatus.LobbyMenu: 
                        LobbyPlayer lobbyPlayer = Instantiate(lobbyPlayerPrefab);
                        lobbyPlayer.isPartyLeader = LobbyPlayers.Count == 0;
            
                        NetworkServer.AddPlayerForConnection(conn, lobbyPlayer.gameObject);
                    
                        if (conn.identity != null) {
                            var player = conn.identity.GetComponent<LobbyPlayer>();
                            AddPlayer(player);
                        }
                        break;
                }
            }

            StartCoroutine(AddNewPlayerConnection());

        }
        
        // player leaves
        public override void OnServerDisconnect(NetworkConnection conn) {
            Debug.Log("[SERVER] PLAYER DISCONNECT");
                        
            if (conn.identity != null) {
                switch (Status) {
                    case FdNetworkStatus.SinglePlayerMenu:
                        var loadingPlayer = conn.identity.GetComponent<LoadingPlayer>();
                        RemovePlayer(loadingPlayer);
                        break;
                    
                    case FdNetworkStatus.LobbyMenu:
                        var lobbyPlayer = conn.identity.GetComponent<LobbyPlayer>();
                        RemovePlayer(lobbyPlayer);
                        break;
                }
                
            }
            
            base.OnServerDisconnect(conn);
        }

        // Server shutdown, notify all players
        public override void OnStopClient() {
            Debug.Log("[SERVER] SHUTDOWN");
            switch (Status) {
                
                case FdNetworkStatus.LobbyMenu:
                    foreach (var lobbyPlayer in LobbyPlayers) {
                        lobbyPlayer.CloseLobby();
                    }
                    LobbyPlayers.Clear();
                    break;
                
                case FdNetworkStatus.Loading:
                    // foreach (var loadingPlayer in LoadingPlayers) {
                    //     
                    // }
                    LoadingPlayers.Clear();
                    break;
                
                case FdNetworkStatus.InGame:
                    break;
                    
            }
            _status = FdNetworkStatus.SinglePlayerMenu;
        }

        #endregion

        #region Player Transition + List Management

        private LobbyPlayer TransitionToLobbyPlayer<T>(T previousPlayer) where T: NetworkBehaviour {
            var lobbyPlayer = Instantiate(lobbyPlayerPrefab);
            return ReplacePlayer(lobbyPlayer, previousPlayer);
        }
        
        private LoadingPlayer TransitionToLoadingPlayer<T>(T previousPlayer) where T: NetworkBehaviour {
            var loadingPlayer = Instantiate(loadingPlayerPrefab);
            return ReplacePlayer(loadingPlayer, previousPlayer);
        }
        
        private ShipPlayer TransitionToShipPlayer<T>(T previousPlayer) where T: NetworkBehaviour {
            var shipPlayer = Instantiate(shipPlayerPrefab);
            return ReplacePlayer(shipPlayer, previousPlayer);
        }
        
        private T ReplacePlayer<T, U>(T newPlayer, U previousPlayer) where T : NetworkBehaviour where U : NetworkBehaviour {
            Debug.Log("REPLACE PLAYER " + previousPlayer + " " + previousPlayer.connectionToClient + " " + newPlayer);
            var conn = previousPlayer.connectionToClient;
            if (previousPlayer.connectionToClient.identity != null) {
                NetworkServer.Destroy(previousPlayer.connectionToClient.identity.gameObject);
            }
            NetworkServer.ReplacePlayerForConnection(conn, newPlayer.gameObject, true);
            RemovePlayer(previousPlayer);
            AddPlayer(newPlayer);
            return newPlayer;
        }

        private void AddPlayer<T>(T player) where T : NetworkBehaviour {
            switch (player) {
                case LobbyPlayer lobbyPlayer: LobbyPlayers.Add(lobbyPlayer);
                    break;
                case LoadingPlayer loadingPlayer: LoadingPlayers.Add(loadingPlayer);
                    break;
                case ShipPlayer shipPlayer: ShipPlayers.Add(shipPlayer);
                    break;
                default:
                    throw new Exception("Unsupported player object tyep!");
            }
        }

        private void RemovePlayer<T>(T player) where T : NetworkBehaviour {
            if (player != null) {
                switch (player) {
                    case LobbyPlayer lobbyPlayer:
                        LobbyPlayers.Remove(lobbyPlayer);
                        break;
                    case LoadingPlayer loadingPlayer:
                        LoadingPlayers.Remove(loadingPlayer);
                        break;
                    case ShipPlayer shipPlayer:
                        ShipPlayers.Remove(shipPlayer);
                        break;
                    default:
                        throw new Exception("Unsupported player object type!");
                }
            }
        }
        
        #endregion
    }
}